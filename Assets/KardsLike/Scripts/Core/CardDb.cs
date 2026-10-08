using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 卡池数据。这是**唯一**允许存放卡牌数值的地方。
    ///
    /// 工程约定（设计文档 §8 第 1 条）：
    ///   加一张新卡只需要改下面的 Cards 数组，不需要动 GameEngine 里任何一行。
    ///   如果加卡时被迫修改了结算函数，说明抽象漏了，要回头补设计。
    ///
    /// 数值自查口径：单位卡的 攻击 + 生命 必须等于 2 × 费用 + 1。
    /// </summary>
    public static class CardDb
    {
        // ------------------------------------------------------------------
        // MVP 卡池：12 张 = 8 张单位卡 + 4 张指令卡
        // 费用曲线（1~6费）：3 / 3 / 2 / 2 / 1 / 1
        // ------------------------------------------------------------------

        /// <summary>8 张单位卡。攻防分配见设计文档 §4.2。</summary>
        public static readonly CardDef[] Units = new CardDef[]
        {
            // ---- 1 费（预算 3）：一对同预算反向分配的对照卡 ----
            // 1/2：站得住，但打不死人。鼓励"第一回合就下东西"。
            new CardDef("unit_scout",      "侦察兵",  1, CardType.Unit, 1, 2, EffectId.None, 0,
                "1 费基准体：能白吃一个 1/1 并存活，但威胁极低。"),

            // 2/1：打得疼，但任何伤害都能带走它。
            new CardDef("unit_rifleman",   "步兵班",  1, CardType.Unit, 2, 1, EffectId.None, 0,
                "1 费最高的攻击力，代价是 1 血：碰到就死。"),

            // ---- 2 费（预算 5）----
            // 2/3：3 血让 1 费单位（最高 2 攻）无法一击清除。
            new CardDef("unit_mg_nest",    "机枪阵地", 2, CardType.Unit, 2, 3, EffectId.None, 0,
                "2 费标准体：至少能在场上活过一个回合。"),

            // ---- 3 费（预算 7）：第二对同预算反向分配的对照卡 ----
            // 3/4：中期的"参照物"，打得过它又比它便宜的卡就是超模。
            new CardDef("unit_paratrooper", "伞兵",    3, CardType.Unit, 3, 4, EffectId.None, 0,
                "3 费标准体，攻守均衡，是中期的强度标尺。"),

            // 4/3：能秒杀 3 血及以下的一切，但自己也在任何 3 攻面前站不住。
            new CardDef("unit_sniper",     "狙击手",  3, CardType.Unit, 4, 3, EffectId.None, 0,
                "高风险高回报：4 攻能秒杀多数 3 费以下单位，但极易被反杀。"),

            // ---- 4 费（预算 9）----
            // 4/5：只有榴弹炮（6 攻）能一击清除它，滚雪球能力最强。
            new CardDef("unit_armored",    "装甲车",  4, CardType.Unit, 4, 5, EffectId.None, 0,
                "全卡池最难被单张牌处理的单位，容易滚雪球。"),

            // ---- 5 费（预算 11）----
            // 6/5：全卡池最高攻击力，放着不管三次攻击就是 18 点。
            new CardDef("unit_howitzer",   "榴弹炮",  5, CardType.Unit, 6, 5, EffectId.None, 0,
                "给对局设定时钟：不解它，三回合后 HQ 就没了。"),

            // ---- 6 费（预算 13）----
            // 7/6：最强也最卡手，资源 5 之前摸到就是废牌。
            new CardDef("unit_tank",       "重型坦克", 6, CardType.Unit, 7, 6, EffectId.None, 0,
                "两刀砍掉 14 点 HQ，代价是极高的卡手风险。")
        };

        /// <summary>
        /// [STAGE2C] HQ（大本营）的卡牌定义。**刻意不放进 Units / Orders / Cards 任何一个数组**。
        ///
        /// 三个"不放进数组"的理由，每个都会造成实打实的 bug：
        ///   ① 进了 Cards 就会被 BuildDeck 抽进牌库（36 张变 39 张，抽空节奏全错）；
        ///   ② 进了 Units 就会被 ValidatePanelBudget 按 `2 × 费用 + 1` 校验 ——
        ///      HQ 是 0 费 20 血，天然不合那条公式（它是地基，不是一张牌）；
        ///   ③ 它一旦出现在手牌里，就会被 CanPlayCard 当成普通单位打出去。
        ///
        /// **攻 0 是刻意的**：有了它，"攻击 HQ 不会被反击"这件事由数值本身保证（0 点伤害），
        /// 引擎里不需要任何方向特判。血量取自 GameEngine.HqStartingHealth（20）。
        /// </summary>
        public static readonly CardDef Hq =
            new CardDef("hq", "指挥部", 0, CardType.Hq, 0, GameEngine.HqStartingHealth, EffectId.None, 0,
                "大本营。被击破即告负。");

        /// <summary>4 张指令卡。见设计文档 §4.3。</summary>
        public static readonly CardDef[] Orders = new CardDef[]
        {
            // 1 费唯一"不影响场面"的牌，给运气差的一方一条追赶路径。
            new CardDef("order_supply",  "补给空投", 1, CardType.Order, 0, 0, EffectId.DrawCards, 2,
                "抽 2 张牌。"),

            // 3 点伤害刻意打不死满血 2 费单位（预算 5），必须配合补刀。
            // [FIX-1] 卡面文案已同步"仅敌方单位"这个决定（设计文档 §4.3 / §6.4.1）——
            //         引擎校验改了、卡面还写着"任意一个单位"的话，玩家会以为能打自己。
            new CardDef("order_strike",  "精准打击", 2, CardType.Order, 0, 0, EffectId.DamageUnit, 3,
                "对敌方任意一个单位造成 3 点伤害。"),

            // 上限封顶 20，满血时使用等于白扔 —— 逼玩家做真实取舍。
            new CardDef("order_medic",   "战场急救", 2, CardType.Order, 0, 0, EffectId.HealHq, 5,
                "我方 HQ 回复 5 点生命（上限 20）。"),

            // 唯一的 AOE，存在意义是威慑对手不要无脑铺小怪。
            new CardDef("order_barrage", "炮火覆盖", 4, CardType.Order, 0, 0, EffectId.DamageAllEnemyUnits, 2,
                "对敌方所有单位造成 2 点伤害。")
        };

        /// <summary>全部 12 张卡（单位在前、指令在后，顺序稳定）。</summary>
        public static readonly CardDef[] Cards = Concat(Units, Orders);

        private static CardDef[] Concat(CardDef[] a, CardDef[] b)
        {
            CardDef[] result = new CardDef[a.Length + b.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i];
            for (int i = 0; i < b.Length; i++) result[a.Length + i] = b[i];
            return result;
        }

        /// <summary>按 id 查卡。找不到返回 null（调用方必须处理）。</summary>
        public static CardDef Get(string id)
        {
            for (int i = 0; i < Cards.Length; i++)
            {
                if (Cards[i].Id == id) return Cards[i];
            }
            return null;
        }

        /// <summary>
        /// 构造一副牌库：12 张卡各 copiesPerCard 份。
        /// ⚠️ **HQ 不在牌库里** —— 它由 `CardDb.Hq` 单独定义，开局直接落位到支援阵线。
        ///
        /// 设计文档 §4.5 论证了取 3 份（36 张，对齐 Kards 的 40 张量级）。
        /// [STAGE2C] 牌库抽空不再判负，改为"不抽牌 + 每次结束回合扣 2^k 点 HQ 血量"（§5.2.1），
        /// 所以牌库张数现在的作用只是"给对局设一个自然的资源下限"，不再是终止性保证。
        /// </summary>
        public static List<CardDef> BuildDeck(int copiesPerCard)
        {
            List<CardDef> deck = new List<CardDef>(Cards.Length * copiesPerCard);
            for (int c = 0; c < copiesPerCard; c++)
            {
                for (int i = 0; i < Cards.Length; i++) deck.Add(Cards[i]);
            }
            return deck;
        }

        /// <summary>
        /// 数值自检：验证每张单位卡都符合 攻击 + 生命 = 2 × 费用 + 1。
        /// 返回 null 表示全部通过；否则返回第一条违规说明。
        /// 阶段 4 的批量模拟会先调用它，避免用错误数据跑统计。
        /// </summary>
        public static string ValidatePanelBudget()
        {
            for (int i = 0; i < Units.Length; i++)
            {
                CardDef c = Units[i];
                int expected = CardDef.BudgetForCost(c.Cost);
                if (c.PanelValue != expected)
                {
                    return c.Id + " 面板点数 " + c.PanelValue + " != 预算 " + expected +
                           "（攻击 " + c.Attack + " + 生命 " + c.Health + "，费用 " + c.Cost + "）";
                }
            }
            return null;
        }
    }
}
