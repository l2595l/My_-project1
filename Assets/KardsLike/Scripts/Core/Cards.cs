namespace KardsLike.Core
{
    /// <summary>卡牌大类。见设计文档 §3.1。</summary>
    public enum CardType
    {
        /// <summary>单位卡：部署到战场，有攻击力与生命值。</summary>
        Unit = 0,
        /// <summary>指令卡：结算一次效果后进弃牌堆，不留场。</summary>
        Order = 1,
        /// <summary>
        /// [STAGE2C] HQ（大本营）：一种**不可打出**的单位卡。双方开局各有一个，
        /// 固定站在自己支援阵线的第 0 格，之后永远不动。
        ///
        /// 它攻击力为 0（所以"打 HQ 不会被反击"这件事由数值本身保证，不需要特判），
        /// 不能被移动、不能被指令卡指定，**只有敌方前线单位够得着它**；
        /// 血量归零即该方告负（设计文档 §6.1.2）。
        ///
        /// 为什么单列一个枚举值、而不是复用 Unit：
        ///   代码里所有"遍历单位卡"的地方都在判 `IsUnit`（出牌、数值自检、AI 出牌偏好……），
        ///   单列一个值能让这些地方**天然**把 HQ 排除在外，
        ///   不必挨个补 `if (card.IsHq) continue;` —— 少写一处就少一类漏改。
        /// </summary>
        Hq = 2
    }

    /// <summary>
    /// [STAGE2B] 单位所在的战线（阶段 2 任务 B：三条战线）。见设计文档 §7.1。
    ///
    /// 三条战线的结构（自上而下）：
    ///   对手支援阵线（4 格，敌方专属）→ 前线（5 格，双方争夺）→ 我方支援阵线（4 格，我方专属）
    ///
    /// 为什么要有这个枚举而不是直接用"单位在哪个 List 里"：
    ///   渲染层与 AI 拿到一个 UnitInstance 时，往往不知道它是从哪个列表取出来的
    ///   （CollectUnitsOf 会把两个列表合成一个视图），所以必须让单位自己记得站在哪条线。
    /// </summary>
    public enum Line
    {
        /// <summary>支援阵线：新单位唯一的落点，容量 4，敌方单位永远进不来。</summary>
        Support = 0,
        /// <summary>前线：争夺区，容量 5，同一时间只能被一方占领；单位移入后不再退回。</summary>
        Front = 1
    }

    /// <summary>
    /// 指令卡效果标识。引擎按这个 id 分派逻辑。
    /// 刻意用枚举而不是委托：
    ///   委托无法序列化，一旦卡牌数据里塞了闭包，
    ///   阶段 4 的 CSV 导出、卡组编辑器全部做不了。见设计文档 §3.1。
    /// </summary>
    public enum EffectId
    {
        /// <summary>无效果（单位卡固定用这个值）。</summary>
        None = 0,
        /// <summary>抽 EffectValue 张牌。</summary>
        DrawCards = 1,
        /// <summary>对单个目标单位造成 EffectValue 点伤害。</summary>
        DamageUnit = 2,
        /// <summary>我方 HQ 回复 EffectValue 点（上限封顶）。</summary>
        HealHq = 3,
        /// <summary>对敌方所有单位造成 EffectValue 点伤害。</summary>
        DamageAllEnemyUnits = 4
    }

    /// <summary>
    /// 一张卡的静态定义（不可变）。
    ///
    /// 关键约定：全场只有一份 CardDef 实例，对局中永远不会被修改。
    /// 场上"那个掉血的单位"是 UnitInstance，不是 CardDef。
    /// 把这两者合并是卡牌游戏最常见的早期架构错误 ——
    /// 一旦合并，同名单位就无法各自独立掉血。见设计文档 §3.2。
    /// </summary>
    public sealed class CardDef
    {
        /// <summary>唯一标识，用于日志、统计、存档。一旦发布不可更改。</summary>
        public readonly string Id;

        /// <summary>中文卡名，仅用于显示。</summary>
        public readonly string Name;

        /// <summary>费用（资源）。设计范围 1 ~ 6。</summary>
        public readonly int Cost;

        public readonly CardType Type;

        /// <summary>【仅单位卡】攻击力。指令卡固定为 0。</summary>
        public readonly int Attack;

        /// <summary>【仅单位卡】生命值。指令卡固定为 0。</summary>
        public readonly int Health;

        /// <summary>【仅指令卡】效果标识。单位卡固定为 None。</summary>
        public readonly EffectId Effect;

        /// <summary>效果的数值参数（伤害量 / 抽牌数 / 回复量）。单位卡为 0。</summary>
        public readonly int EffectValue;

        /// <summary>卡面描述文字。</summary>
        public readonly string Text;

        public CardDef(string id, string name, int cost, CardType type,
                       int attack, int health,
                       EffectId effect, int effectValue, string text)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Type = type;
            Attack = attack;
            Health = health;
            Effect = effect;
            EffectValue = effectValue;
            Text = text;
        }

        public bool IsUnit { get { return Type == CardType.Unit; } }
        public bool IsOrder { get { return Type == CardType.Order; } }
        /// <summary>[STAGE2C] 是不是 HQ（大本营）卡。HQ 不进牌库、不参与出牌。</summary>
        public bool IsHq { get { return Type == CardType.Hq; } }

        /// <summary>
        /// 面板点数 = 攻击 + 生命。
        /// 设计口径：面板点数 = 2 × 费用 + 1（见设计文档 §4.1）。
        /// 加新卡时用这条公式自查，是防止失衡的第一道闸门。
        /// </summary>
        public int PanelValue { get { return Attack + Health; } }

        /// <summary>按设计公式算出的该费用的理论面板预算。</summary>
        public static int BudgetForCost(int cost) { return 2 * cost + 1; }

        public override string ToString()
        {
            return IsUnit
                ? Name + "(" + Cost + "费 " + Attack + "/" + Health + ")"
                : Name + "(" + Cost + "费 指令)";
        }
    }

    /// <summary>
    /// 战场上一个单位的运行时实例（可变）。
    /// 同一张 CardDef 可以在场上对应多个互不相干的 UnitInstance。
    /// </summary>
    public sealed class UnitInstance
    {
        /// <summary>对局内唯一编号，UI 交互与攻击目标都靠它定位。</summary>
        public readonly int InstanceId;

        /// <summary>指向不可变的卡牌定义。</summary>
        public readonly CardDef Def;

        /// <summary>归属玩家索引：0 = 玩家，1 = 电脑。</summary>
        public readonly int OwnerIndex;

        /// <summary>当前攻击力。MVP 没有增益效果，所以恒等于 Def.Attack；
        /// 保留为可变字段是为了后续加"加攻击"类异能时不用改结构。</summary>
        public int Attack;

        /// <summary>当前生命值。&lt;= 0 即为阵亡。</summary>
        public int Health;

        /// <summary>出场时的生命上限。用于画血条与"未被伤害"判断。</summary>
        public readonly int MaxHealth;

        /// <summary>[STAGE2B] 单位当前所在的战线。部署时是 Support，移上前线后变 Front。</summary>
        public Line Line;

        /// <summary>
        /// [STAGE2B] 是否本回合刚部署 —— 也就是**部署失调**。
        ///
        /// Kards 官方口径（how-to-play）："Without Blitz, you need to wait one turn after
        /// the deployment until you **use** the unit." 注意是 use，不是 attack ——
        /// 所以部署当回合**既不能移动也不能攻击**，下一个自己的回合才解除。
        /// （早期版本把这一条记成"Kards 没有召唤失调"，是错的，见设计文档 §7.1 的更正。）
        /// </summary>
        public bool DeployedThisTurn;

        /// <summary>
        /// [STAGE2B] 本回合是否已经行动过。**移动与攻击共用这一次机会** ——
        /// 这就是 Kards 的「每单位每回合一次操作」。
        ///
        /// 与 DeployedThisTurn 分成两个字段，唯一目的是给出不同的失败原因
        /// （"刚部署" vs "已经动过"）；判定时两者任一为 true 都不能行动。
        /// </summary>
        public bool HasOperatedThisTurn;

        public UnitInstance(int instanceId, CardDef def, int ownerIndex)
        {
            InstanceId = instanceId;
            Def = def;
            OwnerIndex = ownerIndex;
            Attack = def.Attack;
            Health = def.Health;
            MaxHealth = def.Health;
            Line = Line.Support;
            DeployedThisTurn = false;
            HasOperatedThisTurn = false;
        }

        public bool IsAlive { get { return Health > 0; } }

        /// <summary>
        /// [STAGE2C] 这个实例是不是 HQ（大本营）。
        ///
        /// 引擎里凡是"这个单位能不能被打出去/移走/被指定"的判断，都要先看它 ——
        /// HQ 是场上唯一"永远不动、只能挨打"的单位。集中在这一个属性上判断，
        /// 好过让 `Def.Type == CardType.Hq` 散落在十来个地方。
        /// </summary>
        public bool IsHq { get { return Def.IsHq; } }

        /// <summary>是否被完全治疗（用于 UI 显示血条颜色）。</summary>
        public bool IsAtFullHealth { get { return Health >= MaxHealth; } }
    }
}
