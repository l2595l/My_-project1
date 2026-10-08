using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 对局引擎 · 出牌（设计文档 §6.3 / §6.4）。
    ///
    /// [阶段2.5 · 提示词 1] 从 GameEngine.cs 拆出（**纯结构调整，规则一行未改**）。
    /// 本文件包含：CanPlayCard / 目标校验 / PlayCard / 指令卡结算 / SpendAction，
    /// 以及从原「查询辅助」段移入的 GetPlayableHandIndices（它只依赖本文件的 CanPlayCard）。
    /// </summary>
    public sealed partial class GameEngine
    {
        // ==================================================================
        // 出牌（设计文档 §6.3 / §6.4）
        // ==================================================================

        /// <summary>
        /// 判断当前行动方能否打出第 handIndex 张手牌。
        /// 返回 false 时 reason 里是可显示给玩家的中文原因。
        /// </summary>
        public bool CanPlayCard(int handIndex, out string reason)
        {
            reason = "";
            if (State.IsOver) { reason = "对局已结束"; return false; }

            PlayerState p = State.Current;
            if (handIndex < 0 || handIndex >= p.Hand.Count) { reason = "手牌不存在"; return false; }

            CardDef card = p.Hand[handIndex];

            if (card.Cost > p.Resources)
            {
                reason = "资源不足（需要 " + card.Cost + "，当前 " + p.Resources + "）";
                return false;
            }
            if (card.IsUnit && p.IsSupportLineFull)
            {
                // [STAGE2B] 单位卡只能落在自己的支援阵线（4 格）；想上前线要另外执行「移动」。
                // 容量判定换线之后，"能不能部署"与"能不能移入前线"成了两个独立的问题，
                // 所以不能再共用一个战场上限常量（旧的 MaxBoardSize = 7 已删除）。
                reason = "支援阵线已满（上限 " + SupportLineCapacity + "）";
                return false;
            }
            if (card.Effect == EffectId.DamageUnit && CountUnitsOf(1 - p.Index) == 0)
            {
                reason = "敌方没有可选目标";
                return false;
            }
            return true;
        }

        /// <summary>该卡是否需要玩家指定一个敌方单位作为目标。</summary>
        public static bool RequiresUnitTarget(CardDef card)
        {
            return card != null && card.Effect == EffectId.DamageUnit;
        }

        /// <summary>
        /// 判断某个单位能否成为 card 的指定目标（[FIX-1] 任务 1）。
        ///
        /// 规则钉死为「**仅敌方单位**」，出处见设计文档 §4.3 / §6.4。
        /// 之所以把这个判断放在引擎而不是 UI：UI 只负责展示，不是规则。
        /// 之前引擎用 FindUnit 查目标（会同时搜双方战场、不校验归属），
        /// 造成"引擎允许打己方、UI 却禁止"的两套语义 —— 阶段 4 的 AI 搜索
        /// 会把玩家永远打不出的操作也算进搜索空间，污染每张卡的胜率贡献统计。
        /// </summary>
        public bool CanTargetUnit(CardDef card, UnitInstance target, out string reason)
        {
            reason = "";
            if (card == null || card.Effect != EffectId.DamageUnit)
            {
                reason = "该卡不需要指定单位目标";
                return false;
            }
            if (target == null) { reason = "目标不存在"; return false; }
            if (!target.IsAlive) { reason = "目标已阵亡"; return false; }
            if (target.OwnerIndex == State.CurrentPlayer)
            {
                reason = card.Name + " 只能指定敌方单位";
                return false;
            }
            // [STAGE2C] HQ 现在也是场上一个单位，但指令卡**够不到**它 ——
            // 否则"精准打击"就能越过前线直接拆 HQ，三条战线辛苦建立的地理约束一次抹掉。
            if (target.IsHq)
            {
                reason = card.Name + " 不能指定敌方 HQ（只有前线单位能攻击它）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 打出第 handIndex 张手牌。
        /// targetUnitInstanceId：需要目标的指令卡传入**敌方**单位 InstanceId，其余传 -1。
        /// </summary>
        public bool PlayCard(int handIndex, int targetUnitInstanceId)
        {
            // [FIX-4] 回合归属守卫
            string control;
            if (!CheckControl(out control))
            {
                State.AddLog("出牌失败：" + control);
                return false;
            }

            string reason;
            if (!CanPlayCard(handIndex, out reason))
            {
                State.AddLog("出牌失败：" + reason);
                return false;
            }

            PlayerState p = State.Current;
            CardDef card = p.Hand[handIndex];

            // [FIX-1] 目标合法性必须在"扣资源 / 移出手牌"之前校验，
            // 否则一次非法目标会白白吃掉一张牌。校验落在引擎（UI 只是展示）。
            if (RequiresUnitTarget(card))
            {
                UnitInstance target = FindUnit(targetUnitInstanceId);
                string targetReason;
                if (!CanTargetUnit(card, target, out targetReason))
                {
                    State.AddLog("出牌失败：" + targetReason);
                    return false;
                }
            }

            // ① 移出手牌 → 扣资源 → 指令卡进弃牌堆
            p.Hand.RemoveAt(handIndex);
            p.Resources -= card.Cost;

            if (card.IsUnit)
            {
                UnitInstance unit = new UnitInstance(State.NextUnitInstanceId++, card, p.Index);
                unit.Line = Line.Support;        // [STAGE2B] 新单位一律落在支援阵线
                unit.DeployedThisTurn = true;    // 部署失调：当回合既不能移动也不能攻击
                p.SupportLine.Add(unit);
                State.AddLog(p.Name + " 部署 " + card.Name + " 到支援阵线（" +
                             card.Attack + "/" + card.Health + "）");
            }
            else
            {
                p.Discard.Add(card);
                ResolveOrder(p, card, targetUnitInstanceId);
            }

            return true;
        }

        /// <summary>指令卡效果分派（设计文档 §6.4）。加新效果只需在这里加一个 case。</summary>
        private void ResolveOrder(PlayerState p, CardDef card, int targetUnitInstanceId)
        {
            PlayerState opp = State.GetOpponentOf(p.Index);

            switch (card.Effect)
            {
                case EffectId.DrawCards:
                {
                    State.AddLog(p.Name + " 使用 " + card.Name + "：抽 " + card.EffectValue + " 张牌");
                    for (int i = 0; i < card.EffectValue; i++)
                    {
                        // [STAGE2C] 牌库空时 DrawCard 返回 false（不判负）——
                        // 剩下的抽牌额度直接作废，这里什么都不用做。
                        if (!DrawCard(p.Index)) break;
                    }
                    break;
                }

                case EffectId.DamageUnit:
                {
                    UnitInstance target = FindUnit(targetUnitInstanceId);
                    // [FIX-1] 防御性二次校验：主校验已在 PlayCard 里完成（那里会拒绝整个出牌动作），
                    // 这里再挡一次，保证即使有人绕过 PlayCard 直接调 ResolveOrder 也不会伤到己方单位。
                    string targetReason;
                    if (!CanTargetUnit(card, target, out targetReason))
                    {
                        State.AddLog(p.Name + " 使用 " + card.Name + "：" + targetReason + "，效果落空");
                        break;
                    }
                    State.AddLog(p.Name + " 使用 " + card.Name + "：对 " + target.Def.Name +
                                 " 造成 " + card.EffectValue + " 点伤害");
                    DamageUnit(target, card.EffectValue);
                    break;
                }

                case EffectId.HealHq:
                {
                    // [STAGE2C] HQ 现在是单位，治疗就是直接改它的 Health（上限 HqStartingHealth）。
                    // 这里**没有第二个字段需要同步** —— 这正是把 HQ 做成单位换来的好处：
                    // 治疗、伤害、判负读的都是同一份数据。
                    int before = p.Hq.Health;
                    p.Hq.Health = System.Math.Min(HqStartingHealth, p.Hq.Health + card.EffectValue);
                    State.AddLog(p.Name + " 使用 " + card.Name + "：HQ 回复 " +
                                 (p.Hq.Health - before) + " 点（" + p.Hq.Health + "/" + HqStartingHealth + "）");
                    break;
                }

                case EffectId.DamageAllEnemyUnits:
                {
                    State.AddLog(p.Name + " 使用 " + card.Name + "：对敌方所有单位造成 " +
                                 card.EffectValue + " 点伤害");
                    // [STAGE2B] 跨线结算：敌方**支援阵线与前线**的单位一起吃伤害。
                    // 卡面写的是"对敌方所有单位"，引入三线后"所有"必须显式定义 ——
                    // 这里选择跨线（这是这张 AOE 存在的意义：它是唯一能惩罚"堆满前线"的牌）。
                    // 先复制一份快照，避免边遍历边移除。
                    List<UnitInstance> targets = new List<UnitInstance>();
                    CollectUnitsOf(opp.Index, targets);
                    for (int i = 0; i < targets.Count; i++) DamageUnit(targets[i], card.EffectValue);
                    break;
                }
            }

            ResolveDeaths();
        }

        /// <summary>
        /// [STAGE2B] 记一次行动：标记"本回合已行动" + 扣掉行动费。
        /// 移动与攻击都必须走这里，这样"同价"就不是靠自觉，而是只有一个出口。
        /// [STAGE2C] 扣的是 `Resources`（与部署共用），不再是独立的行动点。
        /// </summary>
        private void SpendAction(UnitInstance u)
        {
            u.HasOperatedThisTurn = true;
            State.Players[u.OwnerIndex].Resources -= ActionCostOf(u.Def);
        }

        // ==================================================================
        // 查询辅助（出牌相关，只读）
        // ==================================================================

        /// <summary>当前行动方手牌里"打得出去"的索引列表。</summary>
        public List<int> GetPlayableHandIndices()
        {
            List<int> result = new List<int>();
            PlayerState p = State.Current;
            string reason;
            for (int i = 0; i < p.Hand.Count; i++)
            {
                if (CanPlayCard(i, out reason)) result.Add(i);
            }
            return result;
        }
    }
}
