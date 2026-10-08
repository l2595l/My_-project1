using System;
using KardsLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    /// <summary>
    /// 一张卡（或一个单位）的界面表现。
    ///
    /// 关键约定：CardView 是纯"显示 + 上报点击"的对象，**不持有任何对局状态**。
    /// 它只在 HandIndex / UnitId 里记住"我代表逻辑上的哪个东西"，
    /// 点击时把这两个数字交回给 GamePresenter，由后者去问引擎"这一步合法吗"。
    /// 这样渲染层永远不会成为规则的第二个真相来源。
    /// </summary>
    public sealed class CardView
    {
        public RectTransform Rect;
        public Image Frame;
        public Image Fill;

        // ---- 语义标识：把点击映射回逻辑对象 ----
        /// <summary>手牌索引，&gt;= 0 表示这是一张手牌。</summary>
        public int HandIndex = -1;
        /// <summary>单位实例 id，&gt;= 0 表示这是场上的一个单位。</summary>
        public int UnitId = -1;
        /// <summary>归属玩家索引。</summary>
        public int OwnerIndex = -1;

        public static readonly Vector2 HandCardSize = new Vector2(148f, 208f);

        /// <summary>[STAGE2B] 战场卡尺寸。三条战线把纵向空间切成三份，
        /// 卡片必须从 124×168 收到 112×150，否则会溢出所在的行。</summary>
        public static readonly Vector2 BoardCardSize = new Vector2(112f, 150f);

        private CardView() { }

        // ==================================================================
        // 手牌
        // ==================================================================

        /// <summary>建一张手牌。playable = 现在打不打得出（用来决定是否灰掉）。</summary>
        public static CardView CreateHandCard(Transform parent, Vector2 position, int handIndex,
                                              CardDef def, bool playable, bool selected,
                                              Action<CardView> onClick)
        {
            Color frameColor = selected
                ? UiKit.SelectColor
                : (playable ? UiKit.Accent : UiKit.PanelBorder);

            Color fillColor = def.IsUnit ? UiKit.CardUnitSelf : UiKit.CardOrder;
            if (!playable) fillColor = Blend(fillColor, UiKit.Bg, 0.5f);

            CardView v = BuildFrame(parent, position, HandCardSize, frameColor, fillColor, 14);
            v.HandIndex = handIndex;
            v.OwnerIndex = 0;

            // 费用徽章（左上角）
            TextMeshProUGUI costText;
            BuildBadge(v.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(7f, -7f),
                       42, playable ? UiKit.Accent : UiKit.PanelBorder,
                       def.Cost.ToString(), playable ? UiKit.TextDark : UiKit.TextDim,
                       24f, out costText);

            // 卡名列
            RectTransform nameRt = UiKit.Anchored("Name", v.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                  new Vector2(0f, -58f), new Vector2(132f, 54f));
            UiKit.AddLabel(nameRt, def.Name, 19f, playable ? UiKit.TextMain : UiKit.TextDim,
                           TextAlignmentOptions.Center, FontStyles.Bold, true);

            if (def.IsUnit)
            {
                // 单位卡：底部左右两个数值徽章
                RectTransform typeRt = UiKit.Anchored("Type", v.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -116f), new Vector2(130f, 22f));
                UiKit.AddLabel(typeRt, "单位", 13f, UiKit.TextDim, TextAlignmentOptions.Center);

                TextMeshProUGUI atkText, hpText;
                BuildBadge(v.Rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(7f, 7f),
                           40, UiKit.AttackColor, def.Attack.ToString(), UiKit.TextDark,
                           22f, out atkText);
                BuildBadge(v.Rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-7f, 7f),
                           40, UiKit.HealthColor, def.Health.ToString(), UiKit.TextMain,
                           22f, out hpText);
            }
            else
            {
                // 指令卡：中间显示效果说明
                RectTransform typeRt = UiKit.Anchored("Type", v.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -116f), new Vector2(130f, 22f));
                UiKit.AddLabel(typeRt, "指令", 13f, UiKit.TextDim, TextAlignmentOptions.Center);

                RectTransform bodyRt = UiKit.Anchored("Body", v.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                                      new Vector2(0f, 22f), new Vector2(124f, 58f));
                UiKit.AddLabel(bodyRt, def.Text, 14f, playable ? UiKit.TextMain : UiKit.TextDim,
                               TextAlignmentOptions.Center, FontStyles.Normal, true);
            }

            AttachClick(v, onClick);
            return v;
        }

        // ==================================================================
        // 战场上的单位
        // ==================================================================

        /// <summary>
        /// 建一个战场单位。selected = 被选为攻击者；targetable = 当前可被指定为攻击/指令目标。
        /// showStatus：是否显示"召唤失调 / 已行动"状态条。
        ///   这两个标记表达的都是"它这回合动不了了"，只有在该单位所属玩家的回合才有意义。
        ///   对手回合还挂着"召唤失调"只会让玩家困惑，所以要传 false。
        /// </summary>
        public static CardView CreateBoardUnit(Transform parent, Vector2 position, UnitInstance unit,
                                               bool selected, bool targetable, bool attackable,
                                               bool showStatus, Action<CardView> onClick)
        {
            // [STAGE2C] HQ 与普通单位共用"战场卡"的位置，但外观必须一眼可辨：
            // 它没有攻击力、没有费用、没有部署失调，血量却是整局的胜负指标。
            if (unit.IsHq) return CreateHqCard(parent, position, unit, targetable, onClick);

            bool isSelf = unit.OwnerIndex == 0;

            Color frameColor;
            if (selected) frameColor = UiKit.SelectColor;
            else if (targetable) frameColor = UiKit.TargetColor;
            else if (attackable) frameColor = UiKit.LegalColor;
            else frameColor = UiKit.PanelBorder;

            Color fillColor = isSelf ? UiKit.CardUnitSelf : UiKit.CardUnitEnemy;
            // 行动力为 0 的单位（MVP 里没有）或本回合已经行动过的，压暗一点提示"它这回合动不了了"
            if (unit.HasOperatedThisTurn) fillColor = Blend(fillColor, UiKit.Bg, 0.3f);

            CardView v = BuildFrame(parent, position, BoardCardSize, frameColor, fillColor, 12);
            v.UnitId = unit.InstanceId;
            v.OwnerIndex = unit.OwnerIndex;

            RectTransform nameRt = UiKit.Anchored("Name", v.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                  new Vector2(0f, -8f), new Vector2(100f, 40f));
            UiKit.AddLabel(nameRt, unit.Def.Name, 17f, UiKit.TextMain,
                           TextAlignmentOptions.Center, FontStyles.Bold, true);

            // 血条
            BuildHealthBar(v.Rect, unit.Health, unit.MaxHealth);

            // 状态文字：优先显示"不能动"的原因，没有就显示空
            string status = "";
            if (showStatus)
            {
                if (unit.DeployedThisTurn) status = "部署失调";
                else if (unit.HasOperatedThisTurn) status = "已行动";
            }
            if (status.Length > 0)
            {
                RectTransform statusRt = UiKit.Anchored("Status", v.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                        new Vector2(0f, -14f), new Vector2(104f, 24f));
                Color statusColor = unit.DeployedThisTurn ? UiKit.TextDim : UiKit.LegalColor;
                UiKit.AddLabel(statusRt, status, 13f, statusColor, TextAlignmentOptions.Center);
            }

            TextMeshProUGUI atkText, hpText;
            BuildBadge(v.Rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(6f, 6f),
                       36, UiKit.AttackColor, unit.Attack.ToString(), UiKit.TextDark,
                       20f, out atkText);
            BuildBadge(v.Rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 6f),
                       36, UiKit.HealthColor, unit.Health.ToString(), UiKit.TextMain,
                       20f, out hpText);

            AttachClick(v, onClick);
            return v;
        }

        /// <summary>
        /// [STAGE2C] 建一张 **HQ（大本营）卡** —— 双方支援阵线第 0 格那个常驻单位。
        ///
        /// 与普通单位卡的差别全部来自规则：
        ///   · 不画攻击徽章（它攻击力恒为 0，画出来只会让玩家以为"这个能打人"）
        ///   · 不画费用、不画"部署失调 / 已行动"状态条（它永远不动）
        ///   · 血量数字放大居中 —— 20 → 0 就是输赢，这是全局最重要的一个数
        ///   · 边框用金色 Accent；被当前选择攻击到时转红（TargetColor），与普通目标一致
        /// </summary>
        public static CardView CreateHqCard(Transform parent, Vector2 position, UnitInstance unit,
                                            bool targetable, Action<CardView> onClick)
        {
            Color frameColor = targetable ? UiKit.TargetColor : UiKit.Accent;
            // 深金（自己）/ 深暗红（敌方）—— 与普通单位卡的蓝灰 / 暗红拉开距离
            Color fillColor = unit.OwnerIndex == 0
                ? new Color32(56, 46, 20, 255)
                : new Color32(54, 30, 30, 255);

            CardView v = BuildFrame(parent, position, BoardCardSize, frameColor, fillColor, 12);
            v.UnitId = unit.InstanceId;
            v.OwnerIndex = unit.OwnerIndex;

            RectTransform nameRt = UiKit.Anchored("Name", v.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                  new Vector2(0f, -6f), new Vector2(104f, 26f));
            UiKit.AddLabel(nameRt, "HQ · 指挥部", 16f, UiKit.Accent,
                           TextAlignmentOptions.Center, FontStyles.Bold, true);

            RectTransform hpRt = UiKit.Anchored("Hp", v.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                new Vector2(0f, 10f), new Vector2(100f, 54f));
            UiKit.AddLabel(hpRt, unit.Health.ToString(), 42f, UiKit.TextMain,
                           TextAlignmentOptions.Center, FontStyles.Bold);

            // 底部血条。位置与普通单位卡不同（普通卡在正中）—— 这里必须避开大号血量数字。
            RectTransform bg = UiKit.Anchored("HpBarBg", v.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                              new Vector2(0f, 14f), new Vector2(88f, 12f));
            UiKit.AddImage(bg, new Color32(12, 16, 22, 255), 5);

            float ratio = unit.MaxHealth <= 0 ? 0f : Mathf.Clamp01((float)unit.Health / unit.MaxHealth);
            int innerWidth = Mathf.Max(2, Mathf.RoundToInt(82f * ratio));
            RectTransform fill = UiKit.Anchored("HpBarFill", bg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                                new Vector2(3f, 0f), new Vector2(innerWidth, 7f));
            UiKit.AddImage(fill, ratio > 0.5f ? UiKit.HealthColor : UiKit.HealthLow, 4);

            AttachClick(v, onClick);
            return v;
        }

        /// <summary>
        /// 建一个"背面"卡（用于显示对手手牌数量），不可点击。
        /// </summary>
        public static CardView CreateCardBack(Transform parent, Vector2 position, Vector2 size)
        {
            CardView v = BuildFrame(parent, position, size, UiKit.PanelBorder, UiKit.CardUnitEnemy, 8);
            RectTransform markRt = UiKit.Anchored("Mark", v.Rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(size.x - 16f, size.y - 16f));
            UiKit.AddLabel(markRt, "?", Mathf.Min(size.x, size.y) * 0.5f, UiKit.TextDim,
                           TextAlignmentOptions.Center, FontStyles.Bold);
            return v;
        }

        // ==================================================================
        // 内部构建
        // ==================================================================

        private static CardView BuildFrame(Transform parent, Vector2 position, Vector2 size,
                                           Color frameColor, Color fillColor, int radius)
        {
            CardView v = new CardView();

            RectTransform rt = UiKit.Anchored("Card", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                              position, size);
            v.Rect = rt;
            v.Frame = UiKit.AddImage(rt, frameColor, radius);

            RectTransform inner = UiKit.Stretch("Fill", rt, 2f);
            v.Fill = UiKit.AddImageSized(inner, fillColor, Mathf.Max(0, radius - 1),
                                         Mathf.RoundToInt(size.x) - 4, Mathf.RoundToInt(size.y) - 4);
            return v;
        }

        /// <summary>建一个圆角数值徽章（费用 / 攻击 / 生命都用它）。</summary>
        private static Image BuildBadge(RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 position,
                                        int size, Color fill, string label, Color labelColor,
                                        float fontSize, out TextMeshProUGUI text)
        {
            RectTransform rt = UiKit.Anchored("Badge", parent, anchor, pivot, position,
                                              new Vector2(size, size));
            Image img = UiKit.AddImage(rt, fill, size / 2);

            RectTransform labelRt = UiKit.Stretch("Label", rt, 0f);
            text = UiKit.AddLabel(labelRt, label, fontSize, labelColor,
                                  TextAlignmentOptions.Center, FontStyles.Bold);
            return img;
        }

        /// <summary>单位卡中间那条细血条。</summary>
        private static void BuildHealthBar(RectTransform parent, int current, int max)
        {
            RectTransform bg = UiKit.Anchored("HpBarBg", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                              new Vector2(0f, 6f), new Vector2(94f, 9f));
            UiKit.AddImage(bg, new Color32(12, 16, 22, 255), 4);

            float ratio = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
            // 血条宽度取整：避免浮点宽度生成一堆几乎一样的贴图，把缓存撑爆
            int innerWidth = Mathf.RoundToInt(88f * ratio);
            if (innerWidth < 2) innerWidth = 2;

            RectTransform fill = UiKit.Anchored("HpBarFill", bg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                                new Vector2(3f, 0f), new Vector2(innerWidth, 5f));
            UiKit.AddImage(fill, ratio > 0.5f ? UiKit.HealthColor : UiKit.HealthLow, 3);
        }

        private static void AttachClick(CardView v, Action<CardView> onClick)
        {
            if (onClick == null) return;
            Button btn = UiKit.AddButton(v.Rect, v.Frame, Color.white, new Color(1.08f, 1.08f, 1.08f, 1f));
            btn.onClick.AddListener(delegate { onClick(v); });
        }

        /// <summary>两色线性混合。用于把不可用的卡压暗。</summary>
        private static Color Blend(Color a, Color b, float t)
        {
            return new Color(
                Mathf.Lerp(a.r, b.r, t),
                Mathf.Lerp(a.g, b.g, t),
                Mathf.Lerp(a.b, b.b, t),
                Mathf.Lerp(a.a, b.a, t));
        }
    }
}
