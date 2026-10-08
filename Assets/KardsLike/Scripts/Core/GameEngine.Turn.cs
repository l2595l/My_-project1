using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 对局引擎 · 回合流程（设计文档 §1.1）。
    ///
    /// [阶段2.5 · 提示词 1] 从 GameEngine.cs 拆出（**纯结构调整，规则一行未改**）。
    /// 本文件包含：BeginTurn / EndTurn / 抽牌与爆牌 / 疲劳结算 / 终局。
    /// </summary>
    public sealed partial class GameEngine
    {
        // ==================================================================
        // 回合流程（设计文档 §1.1）
        // ==================================================================

        private void BeginTurn(int playerIndex)
        {
            if (State.IsOver) return;

            State.CurrentPlayer = playerIndex;
            PlayerState p = State.Players[playerIndex];

            // L3 兜底：半回合数超过上限 → 按 HQ 血量判定（设计文档 §5.1 / §5.3）
            // [FIX-3] MaxTurns 是半回合口径（50 = 25 轮），不要按"轮"来理解这个数字。
            if (State.HalfTurn > MaxTurns)
            {
                EndByTurnLimit();
                return;
            }

            // ① 资源 +1（封顶），并回满。部署与"单位行动"**共用**这一笔钱（见 §2.7）。
            p.MaxResources = System.Math.Min(p.MaxResources + 1, ResourceCap);
            p.Resources = p.MaxResources;

            // ② 重置本单位状态：解除部署失调 + 清除"本回合已行动"标记。
            //    必须**连前线单位一起**重置 —— 它们不在 SupportLine 里，
            //    只清 SupportLine 会让前线单位永远停在"已行动"，整个前线机制直接死掉。
            ResetUnitStates(p);

            // [FIX-3] 日志用"轮"而不是"回合"：一轮 = 双方各行动一次。
            // 旧文案「第 N 回合」会让玩家以为第 2 回合是自己的第二个回合（实际是电脑的第 1 个）。
            string log = "--- 第 " + State.RoundNumber + " 轮 · " + p.Name + "的回合" +
                         "（资源 " + p.Resources + "/" + p.MaxResources + "）";
            // [STAGE2C] 牌库空了就把话说明白：这不是"马上要输"，而是"开始吃疲劳伤害"。
            if (p.Deck.Count == 0) log += " · 牌库已空，本回合起每次结束回合受疲劳伤害";
            State.AddLog(log + " ---");

            // ③ 抽 1 张。
            // [STAGE2C] 牌库为空**不再判负**（旧的 L2 已删除）—— DrawCard 会静默跳过，
            // 由 EndTurn 里的疲劳结算接管。唯一的例外仍是：起始玩家的第一个回合不抽牌（§1.3）。
            if (!IsOpeningTurnWithoutDraw(playerIndex)) DrawCard(playerIndex);
        }

        /// <summary>
        /// [STAGE2B] 这个"回合开始"是不是"起始玩家的第一个回合"—— 只有它不抽牌。
        /// 它是 §1.3 后手补偿的**唯一**载体：先手全程因此比后手少抽 1 张。
        /// ⚠️ [STAGE2C] 这张牌的差在旧 L2（抽空即负）下会被放大成决定性的胜负差
        /// （后手牌库先空 ⇒ L2 的 96% 归先手）。改成"牌库空只吃疲劳"之后，
        /// 它退化成一张普通的牌差 —— 不再能单方面决定胜负。
        /// 判据：起始玩家 + 本局第一个半回合。HalfTurn 只在 EndTurn 里自增，所以 == 1 只可能是开局那一次。
        /// </summary>
        private bool IsOpeningTurnWithoutDraw(int playerIndex)
        {
            return playerIndex == FirstPlayerIndex && State.HalfTurn == 1;
        }

        /// <summary>
        /// [STAGE2B] 回合开始：解除这一方全部单位的"部署失调"与"本回合已行动"。
        /// 支援阵线与（归他所有的）前线都要清 —— 见 BeginTurn 里的告警注释。
        /// </summary>
        private void ResetUnitStates(PlayerState p)
        {
            for (int i = 0; i < p.SupportLine.Count; i++)
            {
                p.SupportLine[i].DeployedThisTurn = false;
                p.SupportLine[i].HasOperatedThisTurn = false;
            }
            if (State.FrontlineOwner == p.Index)
            {
                for (int i = 0; i < State.Frontline.Count; i++)
                {
                    State.Frontline[i].DeployedThisTurn = false;
                    State.Frontline[i].HasOperatedThisTurn = false;
                }
            }
        }

        /// <summary>结束当前回合 → 结算疲劳 → 移交控制权。</summary>
        public bool EndTurn()
        {
            // [FIX-4] 回合归属守卫：HumanVsAi 下，电脑回合不接受外部驱动
            string control;
            if (!CheckControl(out control))
            {
                State.AddLog("结束回合失败：" + control);
                return false;
            }

            // [STAGE2C] 疲劳结算：牌库空的一方在**自己每次回合结束时**挨一次指数伤害。
            // 先结算再移交，这样"被疲劳打死"和"被打脸打死"走的是同一条判负路径（HQ 归零）。
            // 一旦这一下分出胜负，就不必再开下一个回合了。
            if (!ResolveFatigue(State.CurrentPlayer)) return true;

            State.HalfTurn++;   // [FIX-3] 半回合计数（一轮 = 两次）
            BeginTurn(1 - State.CurrentPlayer);
            return true;
        }

        // ==================================================================
        // 疲劳（[STAGE2C]：取代旧的 L2"牌库抽空即负"，设计文档 §5.2.1）
        // ==================================================================

        /// <summary>
        /// 结算一次疲劳。返回 true = 对局继续；false = 这一下把玩家打死了，别再移交回合。
        ///
        /// 口径（用户 2026-10-08 拍板）：**牌库为空时，该玩家第 k 次结束回合扣 2^k 点 HQ 血量**，
        /// k **各方独立计数**（谁牌库空谁开始数，与对手无关）。
        ///
        /// 为什么这样改：旧的"抽空即负"有一个结构性缺陷 —— 先手首回合不抽牌 ⇒ 先手全程多 1 张留在牌库
        /// ⇒ 后手必然先抽空 ⇒ 后手必然输，200 局实测先手胜率因此被抬到 67%，而真正的战斗（L1）
        /// 是 49.2% / 50.8%。换成疲劳之后，"牌库先空"从**判负**降级为**挨伤害**，
        /// 胜负重新回到 HQ 血量这一条统一战线上，先手偏差失去了放大器。
        ///
        /// 指数增长是刻意的：它给疲劳局一个确定的、很短的上限 —— 2+4+8+16 = 30 &gt; 20，
        /// 所以牌库先空的一方最多再活 4 个自己的回合，绝不会出现"双方都抽空了还在互相干瞪眼"。
        /// </summary>
        private bool ResolveFatigue(int playerIndex)
        {
            PlayerState p = State.Players[playerIndex];
            if (p.Deck.Count > 0) return true;    // 牌库还有牌，不吃疲劳

            p.FatigueCount++;
            int damage = FatigueDamage(p.FatigueCount);

            State.AddLog("  " + p.Name + " 牌库已空，第 " + p.FatigueCount +
                         " 次疲劳：HQ 受到 " + damage + " 点伤害");
            DamageUnit(p.Hq, damage);
            // 传入专门的终局原因：让"输在疲劳"和"输在被打脸"在日志与统计里可区分。
            // 两条路径都归于 L1（HQ 归零），但成因不同，阶段 4 的统计必须能分开看。
            ResolveDeaths("的 HQ 被疲劳伤害击破");

            return !State.IsOver;
        }

        /// <summary>
        /// [STAGE2C] 疲劳伤害 = 2^k（k 从 1 开始）。
        /// k ≥ 20 时封顶：那时 2^20 ≈ 100 万，任何 HQ 都早已归零，封顶只是防止 int 溢出。
        /// </summary>
        private static int FatigueDamage(int k)
        {
            if (k <= 0) return 0;
            return k >= 20 ? (1 << 20) : (1 << k);
        }

        // ==================================================================
        // 抽牌（设计文档 §5.1 L2 / §6.5）
        // ==================================================================

        /// <summary>
        /// 给指定玩家抽一张牌。返回**是否真的抽到了**。
        ///
        /// [STAGE2C] 牌库为空时静默返回 false —— **不再判负**（旧的 L2 已删除）。
        /// 牌库空只是"不抽了"，代价改由 EndTurn 的疲劳结算体现（见 ResolveFatigue）。
        /// 之所以要返回 bool：`补给空投` 一次要抽 2 张，第一张抽空之后就得停下，
        /// 不能再对着空牌库空转。
        /// </summary>
        private bool DrawCard(int playerIndex)
        {
            PlayerState p = State.Players[playerIndex];

            if (p.Deck.Count == 0) return false;   // [STAGE2C] 不判负、不抽

            CardDef card = p.Deck[0];
            p.Deck.RemoveAt(0);

            if (p.Hand.Count >= HandLimit)
            {
                // 爆牌：手牌已满，抽到的牌直接进弃牌堆
                p.Discard.Add(card);
                State.AddLog("  " + p.Name + " 手牌已满，" + card.Name + " 被爆掉");
            }
            else
            {
                p.Hand.Add(card);
            }
            return true;
        }

        // ==================================================================
        // 终局（设计文档 §5）
        // ==================================================================

        private void EndMatch(int winnerIndex, string reason)
        {
            if (State.IsOver) return;   // 终局只确立一次
            State.Result = (MatchResult)winnerIndex;
            State.ResultReason = reason;
            State.AddLog("=== 对局结束：" + State.Players[winnerIndex].Name + " 获胜 · " + reason + " ===");
        }

        private void EndByTurnLimit()
        {
            if (State.IsOver) return;
            int h0 = State.Players[0].HqHealth;
            int h1 = State.Players[1].HqHealth;

            if (h0 == h1)
            {
                State.Result = MatchResult.Draw;
                State.ResultReason = "达到回合上限，双方 HQ 血量相同";
            }
            else
            {
                int winner = h0 > h1 ? 0 : 1;
                State.Result = (MatchResult)winner;
                State.ResultReason = "达到回合上限，HQ 血量更高者胜";
            }
            State.AddLog("=== 对局结束：" + State.ResultReason + " ===");
        }
    }
}
