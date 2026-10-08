using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 对局模式（[FIX-4] 任务 3）。用来决定"回合归属守卫"是否启用。
    ///
    /// 为什么需要它：
    ///   引擎的公开操作（EndTurn / PlayCard / AttackWithUnit）原本不校验当前行动方，
    ///   规则正确性完全由 View 的 CanInteract() 承担。这与设计文档 §8 第 3 条
    ///   「渲染层只读、绝不回写状态」的精神相悖 —— 阶段 4 的 headless 模拟里没有 UI，
    ///   一旦有人写出两个 AI 对打的循环，越权操作会静默生效。
    /// </summary>
    public enum MatchMode
    {
        /// <summary>人类（索引 0）对电脑。公开操作只允许在人类回合被外部驱动。</summary>
        HumanVsAi = 0,

        /// <summary>电脑对电脑。守卫完全关闭，两侧都可被外部驱动（阶段 4 批量模拟用）。</summary>
        AiVsAi = 1
    }

    /// <summary>对局结果。</summary>
    public enum MatchResult
    {
        /// <summary>对局进行中。</summary>
        InProgress = -1,
        /// <summary>玩家（索引 0）获胜。</summary>
        Player0Win = 0,
        /// <summary>电脑（索引 1）获胜。</summary>
        Player1Win = 1,
        /// <summary>平局（只有回合上限兜底会触发）。</summary>
        Draw = 2
    }

    /// <summary>
    /// 单个玩家的全部对局状态。渲染层只读这个对象，绝不回写。
    /// </summary>
    public sealed class PlayerState
    {
        /// <summary>玩家索引：0 = 玩家，1 = 电脑。</summary>
        public readonly int Index;

        /// <summary>显示名。</summary>
        public readonly string Name;

        /// <summary>
        /// [STAGE2C] 我方 HQ（大本营）的**单位实例**。开局由 GameEngine 放进支援阵线第 0 格，
        /// 之后一直留在那里 —— 它不是一个抽象的血量数字，而是场上一个真实存在、可被攻击的单位。
        ///
        /// 只有 `Reset()` 之后到 `StartMatch()` 建好之前它是 null。
        /// </summary>
        public UnitInstance Hq;

        /// <summary>
        /// HQ 当前血量。**推导自 Hq 单位的 Health**，不再是一个独立字段（[STAGE2C]）。
        ///
        /// 保留这个属性名，是为了让渲染层 / AI / 自检里的 `Players[i].HqHealth` 一处都不用改；
        /// 同时从根上杜绝"HQ 单位掉血了、HqHealth 没跟着变"这类双真相来源的 bug ——
        /// HQ 现在可以像普通单位一样被攻击、被指令卡治疗，血量只能有一个出处。
        /// </summary>
        public int HqHealth { get { return Hq == null ? 0 : Hq.Health; } }

        /// <summary>本回合剩余可用资源。部署与"单位行动"**共用**这一个池子（见 §2.7）。</summary>
        public int Resources;

        /// <summary>资源上限（回合开始时 +1，封顶 ResourceCap）。</summary>
        public int MaxResources;

        /// <summary>
        /// [STAGE2C] 本局已经结算过几次疲劳 —— 也就是"我方牌库空之后的第 k 次结束回合"里的 k。
        ///
        /// 牌库为空时，每次我方回合结束 k 就 +1，并扣掉 2^k 点 HQ 血量（2 / 4 / 8 / 16 …）。
        /// 它**不是一个判负条件**：牌库空了只是"不再抽牌 + 开始吃疲劳伤害"，
        /// 胜负仍然统一由 HQ 血量归零（L1）决定。见设计文档 §5.2.1。
        /// 各方独立计数（后手提前抽空不再等于提前输）。
        /// </summary>
        public int FatigueCount;

        /// <summary>牌库（未抽的牌）。索引 0 是下一张要抽的牌。</summary>
        public readonly List<CardDef> Deck = new List<CardDef>();

        /// <summary>手牌。</summary>
        public readonly List<CardDef> Hand = new List<CardDef>();

        /// <summary>弃牌堆（打出的牌 + 爆掉的手牌）。MVP 不参与结算，为后续"坟场"机制预留。</summary>
        public readonly List<CardDef> Discard = new List<CardDef>();

        /// <summary>
        /// [STAGE2B] **支援阵线**上的单位（原 `Board`）。这是该玩家自己的一条线，
        /// 敌方单位永远进不来，新单位也只能部署在这里。容量见 `GameEngine.SupportLineCapacity`。
        ///
        /// ⚠️ [STAGE2C] **索引 0 永远是 HQ**（`Hq` 字段指向同一个对象）。容量 5 是**含 HQ** 的，
        /// 所以玩家最多再往这里放 **3 个**单位。这条"HQ 也占一格"是用户明确拍板的
        /// （2026-10-08），它让"铺场"与"保命"共享同一份空间，支援线不再是纯资源。
        ///
        /// 改名与拆分的理由：阶段 2 引入三条战线后，"战场"不再是一个列表 ——
        /// 支援阵线是每人独占的，前线是双方共用的。把它们塞进同一个 List 再靠
        /// 一个 Line 字段过滤，会让"前线只能被一方占领"这条规则变成每次都要重新证明的不变量；
        /// 拆成"每人一个支援线 List + 全局一个前线 List"后，这条规则**由数据结构本身保证**。
        /// </summary>
        public readonly List<UnitInstance> SupportLine = new List<UnitInstance>();

        public PlayerState(int index, string name)
        {
            Index = index;
            Name = name;
        }

        /// <summary>是否已战败（HQ 归零）。
        /// [STAGE2C] 注意：开局建 HQ 之前 `Hq` 是 null、这个属性会返回 true —— 别在那之前读它。</summary>
        public bool IsDefeated { get { return HqHealth <= 0; } }
        public bool IsHandFull { get { return Hand.Count >= GameEngine.HandLimit; } }

        /// <summary>[STAGE2B] 支援阵线是否已满（满了就不能再部署单位卡）。
        /// [STAGE2C] 注意 HQ 也占一格，所以"没满"时最多还能再放 4 个单位。</summary>
        public bool IsSupportLineFull { get { return SupportLine.Count >= GameEngine.SupportLineCapacity; } }
    }

    /// <summary>
    /// 一局对局的完整状态。
    ///
    /// 架构约定（设计文档 §8 第 3 条）：
    ///   引擎只读写 GameState，渲染层只读 GameState、绝不回写。
    ///   所以 UI 可以在任何时候被整个销毁重建，对局不受影响。
    /// </summary>
    public sealed class GameState
    {
        /// <summary>两个玩家：[0] = 玩家，[1] = 电脑。</summary>
        public readonly PlayerState[] Players = new PlayerState[2];

        /// <summary>当前行动方索引。</summary>
        public int CurrentPlayer;

        /// <summary>
        /// [STAGE2B] **前线**上的单位。双方共用这一条线，容量 `GameEngine.FrontlineCapacity`。
        ///
        /// 关键设计：前线归属**不是**一个需要单独维护的字段，而是从"列表里有没有单位"推导出来的
        /// （见 FrontlineOwner）。因为"前线只能被一方占领"这条规则由"移入时校验列表为空或为己方"
        /// 保证，所以列表里的单位必然同属一方。这样"前线单位全灭 → 归属清空"就**自动成立**，
        /// 不需要在 ResolveDeaths 里写任何重置代码 —— 少一个可以忘记同步的字段，就少一类 bug。
        /// </summary>
        public readonly List<UnitInstance> Frontline = new List<UnitInstance>();

        /// <summary>
        /// 半回合计数（[FIX-3] 任务 2）。从 1 开始，**每次移交控制权 +1**，
        /// 也就是说「你 + 电脑」走完一轮会让它 +2。
        ///
        /// 命名口径（这是本次专门钉死的一条，避免后人再按"回合"误用）：
        ///   · HalfTurn = 1 → 第 1 轮的你
        ///   · HalfTurn = 2 → 第 1 轮的电脑
        ///   · HalfTurn = 3 → 第 2 轮的你
        ///   要展示给人看，请用 RoundNumber（= (HalfTurn + 1) / 2），不要直接用这个字段。
        ///
        /// 之所以保留半回合这个粒度，是因为 L3 回合上限（MaxTurns = 50）是按它计数的，
        /// 换粒度会改变对局长度 —— 本次修复的硬约束是"不改对局长度"。
        /// </summary>
        public int HalfTurn;

        /// <summary>
        /// 轮数（[FIX-3] 任务 2）。一轮 = 双方各行动一次。
        /// 由 HalfTurn 推导，只读，供日志与后续统计使用（设计文档 §5.3 的口径）。
        /// </summary>
        public int RoundNumber { get { return (HalfTurn + 1) / 2; } }

        /// <summary>对局结果。InProgress 表示还在打。</summary>
        public MatchResult Result = MatchResult.InProgress;

        /// <summary>终局原因的文字说明（用于结算面板与日志）。</summary>
        public string ResultReason = "";

        /// <summary>单位实例编号分配器。只增不减，保证同一局内 InstanceId 唯一。</summary>
        public int NextUnitInstanceId = 1;

        /// <summary>对局日志（只保留最近 N 条，防止长时间对局内存无限增长）。</summary>
        public readonly List<string> Log = new List<string>();

        private const int MaxLogEntries = 200;

        public GameState()
        {
            Players[0] = new PlayerState(0, "你");
            Players[1] = new PlayerState(1, "电脑");
        }

        public bool IsOver { get { return Result != MatchResult.InProgress; } }

        public PlayerState Current { get { return Players[CurrentPlayer]; } }
        public PlayerState Opponent { get { return Players[1 - CurrentPlayer]; } }

        public PlayerState GetPlayer(int index) { return Players[index]; }

        /// <summary>[STAGE2B] 前线归属：-1 = 无人占领（前线为空），否则是占领方的索引。</summary>
        public int FrontlineOwner { get { return Frontline.Count == 0 ? -1 : Frontline[0].OwnerIndex; } }

        /// <summary>[STAGE2B] 前线是否被指定玩家占领。</summary>
        public bool IsFrontlineOwnedBy(int playerIndex) { return FrontlineOwner == playerIndex; }

        /// <summary>取另一个玩家（相对于给定索引）。</summary>
        public PlayerState GetOpponentOf(int index) { return Players[1 - index]; }

        public void AddLog(string line)
        {
            Log.Add(line);
            while (Log.Count > MaxLogEntries) Log.RemoveAt(0);
        }

        public void Reset()
        {
            Result = MatchResult.InProgress;
            ResultReason = "";
            HalfTurn = 0;
            CurrentPlayer = 0;
            NextUnitInstanceId = 1;
            Log.Clear();
            Frontline.Clear();
            for (int i = 0; i < Players.Length; i++)
            {
                PlayerState p = Players[i];
                p.Hq = null;              // [STAGE2C] HQ 实例由 StartMatch 重新建
                p.FatigueCount = 0;
                p.Resources = 0;
                p.MaxResources = 0;
                p.Deck.Clear();
                p.Hand.Clear();
                p.Discard.Clear();
                p.SupportLine.Clear();
            }
        }
    }
}
