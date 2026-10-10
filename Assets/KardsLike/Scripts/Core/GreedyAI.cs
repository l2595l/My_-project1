using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// MVP 阶段的贪心 AI（阶段 2 任务 B：适配三条战线 + 行动费）。
    ///
    /// 设计约束（设计文档 §8 第 5 条）：
    ///   对外只暴露一个"逐动作的序列"（StepTurn）。一次跑完（TakeTurn）与
    ///   分步播放（UI）共用同一份实现，不允许写两套逻辑 ——
    ///   否则"模拟里 AI 很强、实际对局里 AI 很蠢"这种问题会永远查不出来。
    ///   这个约定在本阶段**必须跟着扩展**：新增的「移动」也是序列里的一个动作。
    ///
    /// 策略（三线版）：
    ///   ① 出牌：在**给行动留足资源**的前提下，优先把资源花在费用最高的牌上（落点固定在支援阵线）
    ///   ② 攻击：先做"能击杀"的交换（支援线单位），再由前线单位打脸
    ///   ③ 移动：前线进得去时增援一个单位
    ///
    /// [STAGE2C] **"预留资源"是这一版 AI 最关键的一条。**
    ///   行动费与部署费共用同一个资源池（用户 2026-10-08 更正），所以出牌阶段如果把资源铺光，
    ///   AI 就会陷入"场面很大、但一个单位都动不了"的僵局：双方互相站桩，HQ 谁也打不掉。
    ///   于是出牌前先算一笔账 —— **每个本回合还没动过的单位，至少给它留 1 点行动费**，
    ///   剩下的钱才拿去出牌。
    /// </summary>
    public sealed class GreedyAI
    {
        private readonly GameEngine _engine;

        /// <summary>
        /// 复用两个列表装"我方 / 敌方的全部单位"（[STAGE2C] 不含 HQ）。
        /// 参数化的 CollectUnitsOf（而不是每次都 new 一个 List）是为了阶段 4 的批量模拟 ——
        /// 这个函数在攻击循环里是每步都要调的。
        /// </summary>
        private readonly List<UnitInstance> _myUnits = new List<UnitInstance>();
        private readonly List<UnitInstance> _enemyUnits = new List<UnitInstance>();

        public GreedyAI(GameEngine engine)
        {
            _engine = engine;
        }

        /// <summary>一次性把整个回合跑完。headless 批量模拟用这个入口。</summary>
        public void TakeTurn()
        {
            // 只是把 StepTurn 抽干，逻辑完全一致
            foreach (string unused in StepTurn()) { }
        }

        /// <summary>
        /// 逐动作枚举整个回合。UI 用这个入口做"一步一步播"的展示。
        /// 每 yield 一次表示发生了一个动作，调用方可以在这时刷新界面。
        ///
        /// [FIX-4] 整个枚举过程包在 BeginAiTurn / EndAiTurn 里：
        ///   引擎有"回合归属守卫"（HumanVsAi 下，非人类回合的公开操作会被拒绝），
        ///   而电脑的动作必须走公开 API（"可单步枚举"这条约定），
        ///   所以 AI 需要显式向引擎声明"现在是我在指挥"。
        ///   用 try/finally 保证迭代器被中断（StopAllCoroutines）时也会解除声明。
        /// </summary>
        public IEnumerable<string> StepTurn()
        {
            _engine.BeginAiTurn();
            try
            {
                foreach (string step in PlayCardsPhase()) yield return step;
                if (_engine.State.IsOver) yield break;

                // [STAGE2B] 顺序很关键：**先攻击，再移动**。
                // 反过来的话，移动会先把资源花光（一个 6 费单位移上前线就要 3 点），
                // 于是"已经站在前线、本该打 HQ 的单位"整回合都动不了 ——
                // 这是第一版 200 局实测里 L1 = 0 的直接原因。（[STAGE2C] 行动费改成从资源扣之后，
                // 这条顺序的理由**没有变**：钱是同一个池子，先花在哪一步就挤掉哪一步。）
                foreach (string step in AttackPhase()) yield return step;
                if (_engine.State.IsOver) yield break;

                foreach (string step in MovePhase()) yield return step;
                if (_engine.State.IsOver) yield break;

                // [阶段2.6 · 任务3] 坦克冲锋收尾：本回合新移上前线的坦克若还能攻击 → 打 HQ。
                // 全局顺序是"先攻击、后移动"，所以坦克移上前线后已经没有后续攻击阶段 ——
                // 而"移 + 立刻打脸"正是坦克"移动+攻击"的组合价值（旧规则下本段永不触发）。
                foreach (string step in TankChargePhase()) yield return step;
                if (_engine.State.IsOver) yield break;

                _engine.EndTurn();
                yield return "结束回合";
            }
            finally
            {
                _engine.EndAiTurn();
            }
        }

        // ==================================================================
        // 阶段一：出牌
        // ==================================================================

        private IEnumerable<string> PlayCardsPhase()
        {
            while (true)
            {
                if (_engine.State.IsOver) yield break;

                // [任务4] "必须留给行动的那笔钱"改为**每次循环重算**：
                // 闪击单位部署后立刻就能行动（DeployedThisTurn=false），会即时进入
                // ReserveForActions 的计数——旧注释"一轮出牌不会改变这个数"的前提被闪击打破。
                // 重算成本可忽略（扫一遍己方单位），换来 AI 会自动给闪击单位留出行动费。
                int reserve = ReserveForActions(_engine.State.Current);

                PlayerState ai = _engine.State.Current;
                int bestIndex = -1;
                int bestCost = -1;

                // 贪心：在所有"打得出去且值得打"的牌里挑费用最高的。
                // 先出大牌是经典做法 —— 小牌留到后面还有机会用，大牌不用就浪费资源上限了。
                for (int i = 0; i < ai.Hand.Count; i++)
                {
                    CardDef card = ai.Hand[i];
                    string reason;
                    if (!_engine.CanPlayCard(i, out reason)) continue;
                    if (!IsWorthPlaying(ai, card)) continue;
                    // [STAGE2C] 花掉这张牌之后，还必须剩得下预留的行动费。
                    if (card.Cost > ai.Resources - reserve) continue;
                    if (card.Cost > bestCost) { bestCost = card.Cost; bestIndex = i; }
                }

                if (bestIndex < 0) yield break;

                CardDef chosen = ai.Hand[bestIndex];
                int target = ChooseOrderTarget(chosen);

                if (!_engine.PlayCard(bestIndex, target)) yield break;

                yield return "出牌：" + chosen.Name;
            }
        }

        /// <summary>
        /// [STAGE2C] 这一回合必须**留给行动**的资源量。
        ///
        /// 行动费与部署费共用资源池之后，"打一张牌"与"动一下单位"变成了同一笔钱上的取舍。
        /// 而贪心 AI 做不了长线规划：放任它出牌，它必然把钱全铺在场面上，
        /// 结果是"场面越来越大、却一个单位也推不动"—— 双方在中线上互相站桩、HQ 谁也打不掉，
        /// 对局被硬拖到疲劳。这是必须提前堵死的。
        ///
        /// 口径：场上**本回合还没动过**的单位，每个留 1 点（最低档行动费）；
        /// [阶段2.6 · 任务3] 坦克若两个动作都没做，按 **2 笔**预留（它真的会花两次）。
        /// 用最低档而不是逐单位按各自费用算，是因为贪心 AI 每回合实际会动几个单位并不确定；
        /// 留一个保守下界即可，真花超了 CanUnitMove / CanUnitAttack 会自然拒绝。
        /// </summary>
        private int ReserveForActions(PlayerState ai)
        {
            _engine.CollectUnitsOf(ai.Index, _myUnits);
            int pending = 0;
            for (int i = 0; i < _myUnits.Count; i++)
            {
                UnitInstance u = _myUnits[i];
                // [阶段2.6 · 任务3] "还能行动"改问引擎（坦克=移+攻两个动作，其余兵种=二选一）。
                if (u.DeployedThisTurn || GameEngine.HasNoActionLeft(u)) continue;
                // 坦克任何动作都没做过 ⇒ 它真会花两笔（移动 + 攻击）；已做过一个动作的坦克
                // 还剩 1 笔，与其余兵种一致。
                int left = 1;
                if (u.Def.UnitKind == UnitType.Tank && !u.HasMovedThisTurn && !u.HasAttackedThisTurn) left = 2;
                pending += left;
            }
            return pending;
        }

        /// <summary>
        /// 这张牌现在打出去是否有意义。
        /// 单位卡永远有意义；指令卡要看"这一手能不能吃满价值"。
        /// 这一层判断让 AI 不至于把"战场急救"在满血时白扔。
        /// </summary>
        private bool IsWorthPlaying(PlayerState ai, CardDef card)
        {
            if (card.IsUnit) return true;

            switch (card.Effect)
            {
                case EffectId.DrawCards:
                    // [FIX-5] 手牌接近上限时抽牌会爆牌，等于白费 1 费 + 1 张牌。
                    // 打出本卡后手牌先 -1，再抽 EffectValue 张，所以"不爆牌"的条件是：
                    //   Hand.Count - 1 + EffectValue <= HandLimit
                    return ai.Hand.Count - 1 + card.EffectValue <= GameEngine.HandLimit;

                case EffectId.DamageUnit:
                    // CanPlayCard 已经保证了敌方有目标
                    return true;

                case EffectId.HealHq:
                    // 只有回复量能全部吃满时才值得。满血使用等于白扔一整张牌。
                    return ai.HqHealth <= GameEngine.HqStartingHealth - card.EffectValue;

                case EffectId.DamageAllEnemyUnits:
                    // 至少能干掉一个敌方单位才用，否则 4 费换 0 收益
                    return CountKillableEnemyUnits(ai, card.EffectValue) >= 1;

                default:
                    return false;
            }
        }

        /// <summary>[STAGE2B] 敌方单位现在分布在两条线上（支援阵线 + 前线），
        /// 所以要用引擎提供的合成视图来数，不能再看某一个 List。</summary>
        private int CountKillableEnemyUnits(PlayerState ai, int damage)
        {
            _engine.CollectUnitsOf(1 - ai.Index, _enemyUnits);
            int count = 0;
            for (int i = 0; i < _enemyUnits.Count; i++)
            {
                if (_enemyUnits[i].Health <= damage) count++;
            }
            return count;
        }

        /// <summary>为需要目标的指令卡挑一个目标。不需要目标时返回 -1。
        /// 注意指令卡**不受线位限制**（卡面写的是"任意一个敌方单位"），
        /// 这是它与"单位攻击"之间刻意保留的差别。</summary>
        private int ChooseOrderTarget(CardDef card)
        {
            if (!GameEngine.RequiresUnitTarget(card)) return -1;

            _engine.CollectUnitsOf(1 - _engine.State.CurrentPlayer, _enemyUnits);
            UnitInstance best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < _enemyUnits.Count; i++)
            {
                UnitInstance u = _enemyUnits[i];
                bool kills = u.Health <= card.EffectValue;
                // 优先级：能击杀 > 打不死但攻击力高
                // 「击杀」给一个足够大的基数，保证它永远压过其他因素
                int score = (kills ? 1000 : 0) + u.Attack * 10 + u.Health;
                if (score > bestScore) { bestScore = score; best = u; }
            }

            return best == null ? -1 : best.InstanceId;
        }

        // ==================================================================
        // 阶段二：移动（[STAGE2B] 新增）
        // ==================================================================

        /// <summary>
        /// 抢前线 / 加固前线。每回合至多移动一个单位。
        ///
        /// 两条策略要点，都是 200 局实测逼出来的（第一版 AI 在这里吃了大亏）：
        ///   ① **前线归我时也要继续增援**，不能只在"无人占领"时才动。
        ///      第一版只占一个就走，结果那个单位要独自挨过对方整个回合 ——
        ///      对方一个回合攒下的动作足以把单个前线单位清掉，前线于是变成旋转门，
        ///      **200 局里 HQ 一次都没被打破**（L1 = 0）。堆到两个以上才可能有人活到自己的回合。
        ///   ② 选人标准是**血量优先**（`Health * 2 + Attack`），不是攻击优先。
        ///      前线是"会被围殴"的地方：一个 1 血单位进去只会换来对方一次免费击杀，
        ///      而 5 血单位对方一击打不掉，才能撑到自己行动、把伤害送到 HQ。
        /// </summary>
        private IEnumerable<string> MovePhase()
        {
            if (_engine.State.IsOver) yield break;

            // 对方占着前线 → 进不去（CanUnitMove 也会拒绝，这里提前退出省一次扫描）
            if (_engine.State.FrontlineOwner == 1 - _engine.State.CurrentPlayer) yield break;
            if (_engine.State.Frontline.Count >= GameEngine.FrontlineCapacity) yield break;

            PlayerState ai = _engine.State.Current;
            UnitInstance best = null;
            int bestScore = int.MinValue;
            string reason;

            for (int i = 0; i < ai.SupportLine.Count; i++)
            {
                UnitInstance u = ai.SupportLine[i];
                if (u.IsHq) continue;   // [STAGE2C] HQ 常驻支援线第 0 格，它不是可移动的单位
                // [阶段2.5 · 任务D] 越线兵种（火炮/战斗机/轰炸机）不参与"抢前线"：
                // 它们站在支援线上就能打任意目标 ⇒ 上前线收益为零，反而暴露给敌方支援线步兵。
                if (GameEngine.IsLongRangeKind(u.Def.UnitKind)) continue;
                // 能不能移入由引擎判定（部署失调 / 已行动 / 资源 / 容量 / 归属 / 是不是 HQ 都在里面）
                if (!_engine.CanUnitMove(u, out reason)) continue;

                int score = u.Health * 2 + u.Attack;
                if (score > bestScore) { bestScore = score; best = u; }
            }

            if (best == null) yield break;

            string name = best.Def.Name;
            if (!_engine.MoveUnit(best.InstanceId)) yield break;
            yield return "移动：" + name + " 上前线";
        }

        // ==================================================================
        // 阶段二·补：坦克冲锋收尾（[阶段2.6 · 任务3]）
        // ==================================================================

        /// <summary>
        /// [阶段2.6 · 任务3] 坦克冲锋收尾：本回合**新移上前线**的坦克若还能攻击，直接打 HQ。
        ///
        /// 为什么需要这一段：全局顺序是"先攻击、后移动"（见 StepTurn 注释），
        /// 坦克移上前线后已经没有后续攻击阶段 —— 而"移 + 立刻打脸"正是坦克
        /// "移动+攻击"的核心组合价值。没有它，新机制在 200 局里只上线一半。
        ///
        /// 触发条件极窄：必须是**坦克**、本回合**移动过**（必然已在前线——移动只有
        /// 支援→前线一个方向）、且**还没攻击过**。每回合最多触发一次（MovePhase 只移 1 个单位）。
        /// ⚠️ 旧规则下本段永不触发（坦克移后不能攻击）⇒ 反向对照轮天然安全。
        /// 目标口径与阶段 B 一致：前线单位默认打脸，避免中线上无限对耗。
        /// </summary>
        private IEnumerable<string> TankChargePhase()
        {
            if (_engine.State.IsOver) yield break;

            PlayerState ai = _engine.State.Current;
            _engine.CollectUnitsOf(ai.Index, _myUnits);
            for (int i = 0; i < _myUnits.Count; i++)
            {
                UnitInstance u = _myUnits[i];
                if (u.Def.UnitKind != UnitType.Tank) continue;
                if (!u.HasMovedThisTurn || u.HasAttackedThisTurn) continue;   // 必须是"移过、还没攻"

                string why;
                if (!_engine.CanUnitAttack(u, out why)) continue;   // 资源不足等
                if (!_engine.CanAttackHq(u, out why)) continue;     // 与阶段 B 同一口径
                if (!_engine.AttackWithUnit(u.InstanceId, -1)) continue;
                yield return "冲锋：" + u.Def.Name + " 移上前线后打击 HQ";
            }
        }

        // ==================================================================
        // 阶段三：攻击
        // ==================================================================

        private IEnumerable<string> AttackPhase()
        {
            // ---- 阶段 A：支援阵线单位做**防守交换** ----
            // [阶段2.5 · 任务D] 候选池不再硬编码"敌方前线"：越线兵种（火炮/战斗机/轰炸机）
            // 还能打到敌方支援线（含 HQ），步兵/坦克的边界照旧 —— **全部交给引擎判**
            // （这才是 R4"AI 必须知道越线"的落点：放开过滤，让新射程真的进入 AI 的选择集）。
            // 第一轮只做白吃交换；第二轮放宽为"能击杀就换"。
            for (int round = 0; round < 2; round++)
            {
                bool requireNoLoss = (round == 0);

                while (true)
                {
                    if (_engine.State.IsOver) yield break;

                    PlayerState ai = _engine.State.Current;
                    _engine.CollectUnitsOf(ai.Index, _myUnits);
                    _engine.CollectUnitsOf(1 - ai.Index, _enemyUnits);

                    UnitInstance bestAttacker = null;
                    UnitInstance bestTarget = null;
                    int bestScore = int.MinValue;
                    string reason;

                    for (int a = 0; a < _myUnits.Count; a++)
                    {
                        UnitInstance atk = _myUnits[a];
                        if (atk.Line != Line.Support) continue;     // 前线单位留给阶段 B
                        if (!_engine.CanUnitAttack(atk, out reason)) continue;

                        for (int t = 0; t < _enemyUnits.Count; t++)
                        {
                            UnitInstance tgt = _enemyUnits[t];

                            // 线位规则交给引擎判，AI 不复制一份
                            // [阶段2.5 · 任务D] 旧版这里还有一道 `tgt.Line != Line.Front` 的硬过滤，
                            // 它会让越线兵种永远看不到敌方支援线的目标（新射程在 AI 里等于没上线）；
                            // 已删除 —— 合法性完全由 CanAttackTarget 判定（内含射程与拦截）。
                            if (!_engine.CanAttackTarget(atk, tgt, out reason)) continue;

                            // [任务4] "能不能击杀"必须**问引擎出口**（含目标重甲）——
                            // 自己拿 atk.Attack 算会高估（重甲会吃掉伤害），AI 会做出送死交换。
                            bool kills = GameEngine.UnitAttackDamage(atk, tgt) >= tgt.Health;
                            if (!kills) continue;

                            // [阶段2.5 · 任务 A] "我会不会死"必须**问引擎**，不能自己拿 tgt.Attack 猜。
                            // 火炮 / 轰炸机不吃反击，自己猜会恒等于"会死"，于是 AI 放弃必杀交换 ——
                            // 新规则在 AI 对局里等于没上线，200 局也测不出它的真实影响（测试失效）。
                            // 走同一个出口还有一层好处：以后加"重甲""烟幕"时 AI 自动跟上，不必再改这里。
                            bool dies = GameEngine.CounterDamage(atk, tgt) >= atk.Health;
                            if (requireNoLoss && dies) continue;

                            int score = tgt.Attack * 10 + tgt.Health - (dies ? atk.Health * 2 : 0);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestAttacker = atk;
                                bestTarget = tgt;
                            }
                        }
                    }

                    if (bestAttacker == null) break;

                    string label = bestAttacker.Def.Name + " → " + bestTarget.Def.Name;
                    if (!_engine.AttackWithUnit(bestAttacker.InstanceId, bestTarget.InstanceId)) break;
                    yield return "换牌：" + label;
                }
            }

            // ---- 阶段 B：前线单位打脸 ----
            // 「抢下前线」的全部意义就在这里，所以前线单位的默认动作是**打 HQ**，
            // 而不是继续纠缠场面。这条分工是 200 局实测逼出来的：
            // 如果前线单位也去交换，双方会在中线上无限对耗，20 点 HQ 一点都掉不下来
            // （L1 = 0，HQ 全场没掉过一滴血，胜负被硬拖到疲劳/回合上限）。
            // 支援线单位则专门负责清掉对方的推进（阶段 A），两边的角色因此完全不重叠。
            if (_engine.State.IsOver) yield break;

            _engine.CollectUnitsOf(_engine.State.CurrentPlayer, _myUnits);
            for (int i = 0; i < _myUnits.Count; i++)
            {
                UnitInstance u = _myUnits[i];
                string why;
                if (!_engine.CanUnitAttack(u, out why)) continue;   // 可能已被前面的结算带走了
                if (!_engine.CanAttackHq(u, out why)) continue;     // 只有前线单位够得着 HQ

                if (!_engine.AttackWithUnit(u.InstanceId, -1)) continue;
                yield return "打脸：" + u.Def.Name;

                if (_engine.State.IsOver) yield break;
            }
        }
    }
}
