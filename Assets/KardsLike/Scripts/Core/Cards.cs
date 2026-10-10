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
    /// [阶段2.5 · 任务 A] **单位兵种**。见设计文档 §9.1 / §10.1。
    ///
    /// 它不是"关键字"（阶段 3 的内容），而是**基础地理与结算规则**：它决定
    ///   ① 这个单位互相攻击时是否参与反击结算（本阶段，已实现）
    ///   ② 它够得着哪条战线上的目标（任务 D，尚未实现 —— 现行引擎等价于"全员步兵"）
    ///
    /// ⚠️ **`None` 是哨兵值，不是"第六种兵种"。** 把它定为 0 有两个刻意的理由：
    ///   ① 它是 C# 枚举的默认值 ⇒ 新加单位卡时**忘记填兵种会被 CardDb 校验抓出来**，
    ///      而不是静默地当成步兵（那种错误在 200 局数据里表现为"数据诡异但查不出原因"）；
    ///   ② HQ 与指令卡**本来就不是兵种**，给它们一个 `Infantry` 会让
    ///      "HQ 攻 0 所以打它不被反击"这件事误依赖"HQ 是步兵"这个巧合。
    ///      `None` 让默认行为（普通双向结算）天然正确。
    /// </summary>
    public enum UnitType
    {
        /// <summary>不是兵种。HQ 与指令卡用它；也是"忘填"的默认值，便于校验报错。</summary>
        None = 0,
        /// <summary>步兵：基准兵种，双向结算。射程上只能打相邻战线（任务 D 才实现）。</summary>
        Infantry = 1,
        /// <summary>坦克：双向结算。**同一回合可移动 + 攻击（顺序不限，各付一次行动费）** ——
        /// [阶段2.6 · 任务3] 已落地（设计文档 §14.5；旧注"本作当前仍是二选一"已作废）。</summary>
        Tank = 2,
        /// <summary>火炮：**造成**反击伤害，但**不受**反击伤害（阶段 2.5 任务 A 实现）。</summary>
        Artillery = 3,
        /// <summary>战斗机：正常双向结算（承受反击）。拦截轰炸机是任务 D 的内容。
        /// [阶段2.5 · 提交 C] 起池内已有 `unit_fighter` 实体，但射程与拦截仍**未**实现
        /// ⇒ 它当前的行为与步兵完全一致，只有面板/兵种图标不同。</summary>
        Fighter = 4,
        /// <summary>轰炸机：不造成反击（当防守方永远不还手）；当攻击者时也不受反击，
        /// **唯一例外是攻击目标是战斗机**（[阶段2.5 · 提交 C] 按官方原文落地）。
        /// "任意战线 + 无视守护"属任务 D。</summary>
        Bomber = 5
    }

    /// <summary>
    /// [阶段2.6 · 任务4] **关键字（词条）**——"国土阵线"对战词条的位掩码子集。
    ///
    /// 口径（官方原文核实见《阶段2.6-任务4-关键字第一批-规则复述与设计问答复核.md》§1）：
    ///   · **闪击 Blitz**：部署后可以立即行动（跳过部署失调；行动额度仍由行动经济决定——
    ///     非坦克"移动或攻击"二选一、坦克可"移动 + 攻击"）；
    ///   · **冲击 Shock**：首次攻击抵消敌人的反击（一次性）；**本作口径：
    ///     只要发动攻击就消耗（含攻击 HQ）**——官方原文"攻击总部不会移除冲击"不采纳（用户拍板）。
    ///
    /// 实现约定：
    ///   · 位掩码只收"布尔型"词条；**重甲不入本枚举**（它是带等级的数值，见 `CardDef.HeavyArmor`）；
    ///   · 只加**已实现**的词条位——未实现的（烟幕/守护/伏击/奋战）在各自轮次追加，
    ///     防止"加了枚举值忘了实现"静默上线（由 CardDb.ValidatePanelBudget 校验合法位）。
    /// </summary>
    [System.Flags]
    public enum Keyword
    {
        /// <summary>没有词条（HQ 与指令卡固定用这个值）。</summary>
        None = 0,
        /// <summary>闪击：部署后可以立即行动。</summary>
        Blitz = 1 << 0,
        /// <summary>冲击：首次攻击抵消敌人的反击（一次性；本作口径——只要攻击就消耗）。</summary>
        Shock = 1 << 1
    }

    /// <summary>
    /// [STAGE2B] 单位所在的战线（阶段 2 任务 B：三条战线）。见设计文档 §7.1。
    ///
    /// 三条战线的结构（自上而下）：
    ///   对手支援阵线（5 格，敌方专属）→ 前线（5 格，双方争夺）→ 我方支援阵线（5 格，我方专属）
    ///
    /// 为什么要有这个枚举而不是直接用"单位在哪个 List 里"：
    ///   渲染层与 AI 拿到一个 UnitInstance 时，往往不知道它是从哪个列表取出来的
    ///   （CollectUnitsOf 会把两个列表合成一个视图），所以必须让单位自己记得站在哪条线。
    /// </summary>
    public enum Line
    {
        /// <summary>支援阵线：新单位唯一的落点，容量 5（含 HQ），敌方单位永远进不来。</summary>
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

        /// <summary>
        /// [阶段2.5 · 任务 A] **行动花费**：这个单位"行动一次"（移动或攻击）要花几点资源。
        /// 指令卡与 HQ 固定为 0（它们不行动）。
        ///
        /// 为什么必须是**显式数据字段**、而不是引擎里算出来的：
        ///   卡面（任务 B）要把它印在左上角，而卡面读不到引擎的推导逻辑。
        ///   若让 View 把档位规则再抄一遍，规则就有了两个真相来源 ——
        ///   本项目已因"两套语义"踩过两次（F2 精准打击归属、F6 召唤失调）。
        ///   做成数据之后，卡面与引擎**不可能**不一致。
        ///
        /// 数值规则（必须满足，由 CardDb.ValidatePanelBudget 校验）：
        ///   部署费用 1~2 → 1 点；3~4 → 2 点；5~6 → 3 点。基准见 CardDb.ExpectedActionCost()。
        /// </summary>
        public readonly int ActionCost;

        public readonly CardType Type;

        /// <summary>
        /// [阶段2.5 · 任务 A] **兵种**。决定这张单位卡在战斗结算里受哪条反击规则约束。
        ///
        /// 命名说明：刻意不叫 `Type2`，也不叫 `UnitType`（会与类型名撞车）。
        /// `UnitKind` 读作"这张卡的兵种是…"，与已有的 `Type`（大类：单位 / 指令 / HQ）互补而不混淆。
        /// 指令卡与 HQ 固定为 <see cref="UnitType.None"/>。
        /// </summary>
        public readonly UnitType UnitKind;

        /// <summary>
        /// [阶段2.6 · 任务4] **布尔型词条**（位掩码）。见 `Keyword` 枚举。
        /// 指令卡与 HQ 固定为 <see cref="Keyword.None"/>（由 ValidatePanelBudget 校验）。
        /// </summary>
        public readonly Keyword Keywords;

        /// <summary>
        /// [阶段2.6 · 任务4] **重甲 X**（数值型词条）：该单位受到的"单位攻击"伤害 −X。
        ///
        /// 为什么独立成 int 而不是塞进 `Keywords` 位掩码：它带等级（1~3 直接参与减法），
        /// 位掩码表达等级要靠 3 个位硬编码、又丑又易错。**"是否拥有重甲"的唯一真相 = `&gt; 0`**，
        /// 不再设冗余的 flags 位。官方上限 3，由 `CardDb.ValidatePanelBudget` 校验。
        /// 0 = 无重甲（指令卡与 HQ 固定为 0）。
        /// </summary>
        public readonly int HeavyArmor;

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

        /// <summary>
        /// 构造一张卡的静态定义。
        ///
        /// 参数顺序刻意把三组成对的字段放在一起：
        ///   `cost / actionCost`（两种花费）、`type / unitKind / keywords / heavyArmor`（两种分类 + 词条）
        ///   与 `attack / health`（面板）。
        /// ⚠️ 参数顺序若写错，由 `CardDb.ValidatePanelBudget()` 兜底 ——
        ///    行动费与档位不一致、词条与重甲越界都会被自检第 1 项直接抓出来，不会静默上线。
        /// </summary>
        public CardDef(string id, string name, int cost, int actionCost,
                       CardType type, UnitType unitKind,
                       Keyword keywords, int heavyArmor,
                       int attack, int health,
                       EffectId effect, int effectValue, string text)
        {
            Id = id;
            Name = name;
            Cost = cost;
            ActionCost = actionCost;
            Type = type;
            UnitKind = unitKind;
            Keywords = keywords;
            HeavyArmor = heavyArmor;
            Attack = attack;
            Health = health;
            Effect = effect;
            EffectValue = effectValue;
            Text = text;
        }

        public bool IsUnit { get { return Type == CardType.Unit; } }
        public bool IsOrder { get { return Type == CardType.Order; } }

        /// <summary>[阶段2.6 · 任务4] 这张卡是否拥有某个布尔词条。</summary>
        public bool HasKeyword(Keyword k) { return (Keywords & k) != 0; }
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
        /// [阶段2.6 · 任务3] 本回合是否已移动 / 是否已攻击。两个字段一起取代了原来的
        /// `HasOperatedThisTurn`（单布尔"移动与攻击共用一次机会"）。
        ///
        /// 行动经济（Kards 口径，设计文档 §14.5）：
        ///   · **坦克**：每回合可移动一次**且**攻击一次（顺序不限）⇒ 两个字段各自独立；
        ///   · **其余兵种**：移动与攻击二选一 ⇒ 任一为 true 即不能再行动。
        /// 判定唯一出口是 `GameEngine.HasNoActionLeft` / `CanUnitMove` / `CanUnitAttack`，
        /// View 与 AI **不许**自己写这份兵种 if（否则立刻出现两个真相来源）。
        /// 与 DeployedThisTurn 分成不同字段，唯一目的是给出不同的失败原因（"刚部署" vs "已经动过"）。
        /// </summary>
        public bool HasMovedThisTurn;

        /// <summary>[阶段2.6 · 任务3] 见 `HasMovedThisTurn` 的合并说明。</summary>
        public bool HasAttackedThisTurn;

        /// <summary>
        /// [阶段2.6 · 任务4] 冲击"当前是否还没用掉"。构造时从 `Def.Keywords` 初始化；
        /// **只要发动过一次攻击就置 false**（本作口径：含攻击 HQ——官方"攻击总部不移除"
        /// 的细则不采纳，用户 2026-10-11 拍板），此后不再恢复
        /// （官方："Shock doesn't refresh unless the card explicitly says so"）。
        ///
        /// ⚠️ 一次性状态必须放实例上、**绝不能写回 Def** —— Def 是不可变的共享卡定义，
        /// 改它会污染"同一张卡的所有实例 + 下一局游戏"。
        /// 消耗点唯一：`AttackWithUnit`（`CounterDamage` 保持纯函数，PreviewAttack 只读不消耗）。
        /// </summary>
        public bool HasShock;

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
            HasMovedThisTurn = false;
            HasAttackedThisTurn = false;
            HasShock = (def.Keywords & Keyword.Shock) != 0;
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
