using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 对局引擎。规则实现在这里，数据在 CardDb，状态在 GameState。
    ///
    /// 设计约束（设计文档 §8 第 3、4 条）：
    ///   · 本类不引用任何 UnityEngine 类型，可以直接被 headless 程序编译运行。
    ///   · 所有操作函数在"对局已结束"时一律返回失败，不再改动任何状态。
    ///
    /// 规则出处全部标注在方法注释里，对应《阶段0-设计文档.md》的章节号。
    ///
    /// [阶段2.5 · 提示词 1] 本类已按职责拆成 4 个 partial 文件（**纯结构调整，规则一行未改**）：
    ///   · GameEngine.cs        常量 / 字段 / 构造函数 / 回合归属守卫 / StartMatch / Restart
    ///   · GameEngine.Turn.cs   回合流程（BeginTurn / EndTurn）/ 疲劳 / 抽牌 / 终局
    ///   · GameEngine.Play.cs   出牌（CanPlayCard / PlayCard）/ 目标校验 / 指令卡结算 / SpendMove + SpendAttack
    ///   · GameEngine.Combat.cs 战斗与线位（CanUnitAct / MoveUnit / AttackWithUnit）/ 伤害与死亡清理 / 查询辅助
    /// </summary>
    public sealed partial class GameEngine
    {
        // ------------------------------------------------------------------
        // 规则常量（设计文档 §2.5 / §6.3 / §6.5）
        // ------------------------------------------------------------------

        /// <summary>HQ 初始与上限血量。</summary>
        public const int HqStartingHealth = 20;

        /// <summary>资源上限。取卡池最高费用（6）的约 1.6 倍，见 §2.5。</summary>
        public const int ResourceCap = 10;

        /// <summary>[STAGE2B] 先手（起始玩家）的起手张数。
        /// 2026-10-08 由"双方各 3 张"改为 Kards 口径：**先手 4 张**，且其第一个回合**不抽牌**。
        /// 见《阶段0-设计文档.md》§1.3 / §5.3。</summary>
        public const int StartingHandSizeFirst = 4;

        /// <summary>[STAGE2B] 后手（第二玩家）的起手张数。
        /// 2026-10-08 第二次调整：Kards 原版的 4/5 在本引擎的贪心 AI 下补偿**过头**了
        /// （200 局实测先手胜率从 57.5% 反向跌到 40.5%），所以改为**双方都 4 张**，
        /// 只保留"先手第一个回合不抽牌"这一处不对称 —— 后手净多 1 张。
        /// 见《阶段0-设计文档.md》§1.3。</summary>
        public const int StartingHandSizeSecond = 4;

        /// <summary>[STAGE2B] 起始玩家索引（先手）。只有他享有"第一个回合不抽牌"这条规则。</summary>
        public const int FirstPlayerIndex = 0;

        /// <summary>[STAGE2B] 支援阵线容量：新单位唯一的落点，每人独占。见设计文档 §7.1。
        /// [STAGE2C] 这个数字**含 HQ** —— HQ 固定占第 0 格，所以玩家最多再部署 4 个单位。
        /// "HQ 也占一格"意味着铺场与保命共享同一份空间。
        /// [阶段2.5 · 提交 B] 2026-10-08 由 **4 改回 Kards 口径的 5**（拍板项 ①）。
        /// 理由：兵种系统让铺场更重要（多兵种协同需要更多格子承载），4 格时
        /// "HQ + 3 个单位"会让火炮/战斗机这类支援型单位挤掉肉盾位。见设计文档 §10.4 / §10.6。</summary>
        public const int SupportLineCapacity = 5;

        /// <summary>[STAGE2B] 前线容量：双方共用，同一时间只能被一方占领。见设计文档 §7.1。</summary>
        public const int FrontlineCapacity = 5;

        /// <summary>回合上限（第二重终止保险，正常对局绝不会触发），见 §5.3。
        /// [STAGE2B] 2026-10-08 由 50 重算为 **72**：牌库改成 36 张后，牌库抽空的自然
        /// 终止点被推到 `HalfTurn ≈ 66`，旧的 50 会让 L3 **抢在它前面触发**。
        /// [STAGE2C] 牌库抽空改成"疲劳伤害"后这一条仍然够用：牌库先空的那一方最多再吃
        /// 4 次疲劳（2+4+8+16 = 30 &gt; 20 点 HQ）就归零，自然终止点仍落在 72 以内。
        /// [阶段2.5 · 提交 C] **72 → 88**：牌库由 36 扩到 42 后自然终止点上界
        /// `2 × 42 − 1 = 83 &gt; 72` ⇒ 不提就会让 L3 抢在疲劳结算完之前触发，
        /// 打破"L3 触发 0 局"这条性质。取 88 = 2D + 4，留 5 个半回合余量。推导见设计文档 §10.6。</summary>
        public const int MaxTurns = 88;

        /// <summary>[STAGE2B] 每张卡在牌库里的份数。2026-10-08 由 2 份（24 张）改为 **3 份（36 张）**，见 §4.5。
        /// 理由：200 局实测有 26.5% 的对局因牌库抽空结束，而 §4.5 要求它"是兜底、不是主要胜负手段"；
        /// 参考作品 Kards 的牌库是 40 张，本作取 3 份 = 36 张（12 张卡 × 3）。</summary>
        public const int DeckCopiesPerCard = 3;

        /// <summary>手牌上限，超出时抽到的牌直接进弃牌堆（爆牌），见 §6.5。</summary>
        public const int HandLimit = 10;

        /// <summary>
        /// 人类玩家的索引（[FIX-4] 任务 3）。HumanVsAi 模式下的回合归属守卫以它为准。
        /// </summary>
        public const int HumanPlayerIndex = 0;

        /// <summary>
        /// [STAGE2B] 一个单位"行动一次"（移动或攻击）要花几点资源。
        ///
        /// [阶段2.5 · 任务 A] **本方法已退化成一行取值。** 档位规则（1~2 费 → 1 / 3~4 费 → 2 /
        /// 5~6 费 → 3）搬到了 `CardDb.ExpectedActionCost()`，那里是**校验基准**；
        /// 真正的数据是 `CardDef.ActionCost`，值写在 `CardDb` 里。
        ///
        /// 为什么必须这样拆：
        ///   任务 B 要在卡面左上角印出行动花费，而卡面读不到引擎的推导逻辑。
        ///   旧的"引擎算档位"实现会逼 View 把同一套 if 再抄一遍 ——
        ///   那就是"规则有两个真相来源"，本项目已因它踩过两次（F2 精准打击归属、F6 召唤失调）。
        ///   现在卡面与引擎读的是**同一个字段**，从结构上不可能不一致；
        ///   而字段写错会被 `CardDb.ValidatePanelBudget()` 在自检第 1 项直接抓出来。
        ///
        /// ⚠️ **不要在这里把档位逻辑加回来。** 一旦加回来，卡面显示与引擎扣费就重新分家了。
        ///
        /// 行动费从 `Resources` 里扣，**没有独立的"行动点"资源**
        /// （用户 2026-10-08 更正：行动不必另建一套预算）。于是"打大牌"与"多动一次"
        /// 重新变成同一笔钱上的取舍 —— 这正是 Kards 的张力所在，见设计文档 §2.7。
        /// </summary>
        public static int ActionCostOf(CardDef card)
        {
            if (card == null) return 0;
            return card.ActionCost;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// [阶段2.6 · 任务3] 这个单位本回合**是否已无任何剩余行动**。全工程唯一出口：
        ///   · 坦克：移动与攻击**各一次** ⇒ 两件都做完才算没有行动空间；
        ///   · 其余兵种：移动与攻击**二选一** ⇒ 任一做过即没有行动空间。
        ///
        /// 谁在用它：Core 的 `CanUnitAct`（行动前置校验）、View 的"已行动"压暗与状态文字、
        /// AI 的预算预留（`ReserveForActions`）。**任何地方都不许再写一份兵种 if**
        /// （"规则只有一个真相来源"——本项目已因两套语义踩过两次）。
        /// 注意：它**不检查**归属 / 部署失调 / 资源 / 对局状态，那些在 `CanUnitAct` 里。
        /// </summary>
        public static bool HasNoActionLeft(UnitInstance u)
        {
            if (u == null) return true;
            if (u.Def.UnitKind == UnitType.Tank)
                return u.HasMovedThisTurn && u.HasAttackedThisTurn;
            return u.HasMovedThisTurn || u.HasAttackedThisTurn;
        }

        // ------------------------------------------------------------------

        /// <summary>对局状态。渲染层只读。</summary>
        public readonly GameState State;

        /// <summary>
        /// 对局模式（[FIX-4] 任务 3）。决定回合归属守卫是否启用。
        /// 构造时指定，之后不可变 —— 中途切换模式会让"同一局的状态"失去意义。
        /// </summary>
        public MatchMode Mode { get; private set; }

        private readonly Rng _rng;

        /// <summary>
        /// 电脑是否正在驱动"当前回合"（[FIX-4] 任务 3）。
        /// GreedyAI 在枚举 StepTurn 期间会把它置为 true，从而让归属守卫放行 ——
        /// 电脑的动作必须走公开 API（可单步枚举那条约定），所以引擎必须知道"现在轮到 AI 指挥"。
        /// </summary>
        private bool _aiDriving;

        /// <summary>本局使用的随机种子。相同种子 + 相同操作序列 = 完全相同的对局。</summary>
        public uint Seed { get { return _rng.Seed; } }

        /// <summary>
        /// 构造引擎。mode 默认 HumanVsAi，保持既有调用点不受影响。
        /// headless 批量模拟请显式传 MatchMode.AiVsAi，让回合归属守卫完全关闭。
        /// </summary>
        public GameEngine(uint seed, MatchMode mode = MatchMode.HumanVsAi)
        {
            _rng = new Rng(seed);
            State = new GameState();
            Mode = mode;
        }

        // ------------------------------------------------------------------
        // 回合归属守卫（[FIX-4] 任务 3）
        // ------------------------------------------------------------------

        /// <summary>
        /// 电脑开始驱动当前回合。由 GreedyAI.StepTurn 在枚举期间调用。
        /// internal：只有同程序集（Core）里的 AI 能声明，UI 无法用它绕过守卫。
        /// </summary>
        internal void BeginAiTurn() { _aiDriving = true; }

        /// <summary>电脑结束驱动。必须与 BeginAiTurn 成对出现（StepTurn 用 try/finally 保证）。</summary>
        internal void EndAiTurn() { _aiDriving = false; }

        /// <summary>
        /// 统一的归属校验：现在这一下操作到底允不允许落到引擎上。
        /// 抽成一个函数是为了不让守卫散落在各个方法里各写一遍（任务 3 要求 d）。
        ///
        /// 规则：
        ///   · 对局已结束 → 一律拒绝
        ///   · AiVsAi 模式 → 守卫完全关闭（阶段 4 的两 AI 对打要靠它跑起来）
        ///   · 电脑正在驱动（_aiDriving）→ 放行
        ///   · HumanVsAi 模式下，若不是人类的回合（CurrentPlayer != 0）→ 拒绝，并给出原因
        /// </summary>
        private bool CheckControl(out string reason)
        {
            reason = "";
            if (State.IsOver) { reason = "对局已结束"; return false; }
            if (Mode == MatchMode.AiVsAi) return true;
            if (_aiDriving) return true;

            if (State.CurrentPlayer != HumanPlayerIndex)
            {
                reason = "现在不是你的回合（当前行动方：" + State.Current.Name + "）";
                return false;
            }
            return true;
        }

        // ==================================================================
        // 开局
        // ==================================================================

        /// <summary>开始一局新对局。见设计文档 §1.3。</summary>
        public void StartMatch()
        {
            State.Reset();

            for (int i = 0; i < 2; i++)
            {
                PlayerState p = State.Players[i];
                p.Resources = 0;
                p.MaxResources = 0;

                // [STAGE2C] HQ 是场上一个**真实单位**：占支援阵线第 0 格，之后就一直在那里。
                // 它不参与洗牌、不进手牌；它的 CardDef（CardDb.Hq）刻意不在 Cards 数组里，
                // 所以既不会被抽进牌库，也不会被数值自检按"面板预算"公式误伤。
                p.Hq = new UnitInstance(State.NextUnitInstanceId++, CardDb.Hq, i);
                p.Hq.Line = Line.Support;
                p.SupportLine.Add(p.Hq);

                p.Deck.AddRange(CardDb.BuildDeck(DeckCopiesPerCard));
                _rng.Shuffle(p.Deck);   // 同一 seed 洗牌结果完全一致
            }

            // [STAGE2B] 起手照 Kards 口径，但张数按本引擎的实测调整过（见 §1.3）：
            // 目前是**双方都 4 张**，只保留"先手第一个回合不抽牌"这一处不对称（后手净多 1 张）。
            // 两个数字都取自常量，不在流程里硬编码。
            for (int round = 0; round < System.Math.Max(StartingHandSizeFirst, StartingHandSizeSecond); round++)
            {
                for (int i = 0; i < 2; i++)
                {
                    int need = (i == FirstPlayerIndex) ? StartingHandSizeFirst : StartingHandSizeSecond;
                    if (round < need) DrawCard(i);
                }
            }

            State.HalfTurn = 1;   // [FIX-3] 半回合计数，见 GameState.HalfTurn 的口径说明
            State.AddLog("=== 对局开始 · 随机种子 " + Seed + " ===");

            BeginTurn(0);   // 玩家先手
        }

        /// <summary>
        /// 用同一个种子重开（用于复现某一局）。
        /// 因为 GameEngine 的 _rng 是只读字段，重开需要构造新引擎，
        /// 所以这里不提供 ResetWithSameSeed —— 由上层持有种子并新建引擎。
        /// </summary>
        public static GameEngine Restart(uint seed)
        {
            GameEngine e = new GameEngine(seed);
            e.StartMatch();
            return e;
        }
    }
}
