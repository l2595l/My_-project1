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
    ///
    /// ==================================================================
    /// [阶段2.5 · 任务 B] 卡面重构
    /// ==================================================================
    /// 从"纯色圆角块"升级成"真卡框 + 兵种插画区 + 行动花费"：
    ///
    ///   ① 整卡铺 `Art/CardFrame.png`（550×766，CC0），**不做九宫格** ——
    ///      它四角的金饰必须按整卡一起缩放才不错位。
    ///   ② 开窗（卡框上的透明矩形）里画 **兵种底色 + 居中的白色兵种剪影**。
    ///      开窗几何是**量出来的**（见 CardArt.WindowLeft 等常量），不是目测的；
    ///      底色块还刻意比开窗四周各大 3px —— 多出来的部分被卡框的实心区盖住，
    ///      于是"底色与开窗的接缝"永远不会露出来（对齐误差全被吸收）。
    ///   ③ 左上角在**手牌**状态显示「部署费用（大）+ 行动花费（小）」，
    ///      在**战场**状态只显示行动花费 —— 这是 Kards 的官方口径：
    ///      「单位在部署后将仅会显示此数字」，也是"我这次动它要花几点"的唯一提示。
    ///   ④ 兵种图标在**三个位置**同时出现（开窗大图 / 底部中徽章 / 整卡色调），
    ///      因为战场卡只有 112px 宽，单一线索在这个尺寸下不够可靠。
    ///
    /// ⚠️ 卡面**不自己算行动费**，只读 `CardDef.ActionCost` / `UnitInstance.Def.ActionCost`。
    ///    档位逻辑的唯一真相来源在 `CardDb`（阶段 2.5 任务 A 已字段化）——
    ///    在 View 里再抄一遍档位表，就是本项目已经踩过两次的"规则有两个真相来源"。
    /// </summary>
    public sealed class CardView
    {
        public RectTransform Rect;
        /// <summary>卡框图（同时是接收点击的图形）。资源缺失时退回程序化圆角块。</summary>
        public Image Frame;
        /// <summary>开窗底色块。保留为公开字段，便于以后做"高亮某个区域"的动效。</summary>
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

        // ---- 卡面内部分层（按建节点顺序 = 绘制顺序，后面的盖在前面上）----
        private const string LayerHalo = "Halo";
        private const string LayerBase = "Base";
        private const string LayerWindow = "Window";
        private const string LayerWindowFade = "WindowFade";
        private const string LayerIllustration = "Illustration";
        private const string LayerFrame = "Frame";
        private const string LayerDim = "Dim";
        private const string LayerContent = "Content";

        private CardView() { }

        // ==================================================================
        // 手牌
        // ==================================================================

        /// <summary>建一张手牌。playable = 现在打不打得出（用来决定是否灰掉）。</summary>
        public static CardView CreateHandCard(Transform parent, Vector2 position, int handIndex,
                                              CardDef def, bool playable, bool selected,
                                              Action<CardView> onClick)
        {
            // 单位卡按兵种上色；指令卡没有兵种，用固定色（对应 CardArt 的 default 分支）
            UnitType kind = def.IsUnit ? def.UnitKind : UnitType.None;

            Color state = selected ? UiKit.SelectColor : new Color(0f, 0f, 0f, 0f);
            Face f = BuildFace(parent, position, HandCardSize, state,
                               CardArt.WindowColor(kind), CardArt.FrameTint(kind), !playable, false);

            CardView v = f.View;
            v.HandIndex = handIndex;
            v.OwnerIndex = 0;

            Color mainText = playable ? UiKit.TextMain : UiKit.TextDim;

            // ---- 开窗：插画区 ----
            if (def.IsUnit) AddUnitIllustration(f, kind);
            else AddOrderSigil(f);

            // ---- 左上角：部署费用（大） + 行动花费（小）----
            TextMeshProUGUI costText;
            BuildBadge(f.Content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -3f),
                       40, playable ? UiKit.Accent : UiKit.PanelBorder,
                       def.Cost.ToString(), playable ? UiKit.TextDark : UiKit.TextDim,
                       24f, out costText);

            // 行动花费：紧贴在部署费用徽章下方（Kards 官方口径：指挥点字段里字体较小的那个数）。
            // 指令卡没有行动费（它不上战场），所以只有单位卡画这一枚。
            if (def.IsUnit)
            {
                AddActionCostPill(f.Content, new Vector2(9f, -50f), new Vector2(34f, 22f), def.ActionCost, 15f);
            }

            // ---- 卡名列 + 名称底色条（压在开窗的上沿）----
            // 指令卡的右上角要放感叹号徽章（Kards 的惯例），所以名称条必须**窄 24px** 让开它 ——
            // 否则 4 字卡名（如"精准打击"）的最后一字会被徽章压住，这是实际踩到过的。
            float nameWidth = def.IsUnit ? 94f : 70f;
            AddNamePlate(f.Content, new Vector2(48f, -4f), new Vector2(nameWidth, 26f),
                         def.Name, 17f, mainText, new Color32(10, 13, 19, 210));

            // ---- 开窗下方：效果文字 + 底部徽章行 ----
            if (def.IsUnit)
            {
                RectTransform bodyRt = UiKit.Anchored("Body", f.Content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -132f), new Vector2(132f, 32f));
                UiKit.AddLabelAuto(bodyRt, def.Text, 12f, 7f, mainText,
                                   TextAlignmentOptions.Center, FontStyles.Normal, true);

                TextMeshProUGUI atkText, hpText;
                BuildBadge(f.Content, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(3f, 3f),
                           38, UiKit.AttackColor, def.Attack.ToString(), UiKit.TextDark, 21f, out atkText);
                BuildBadge(f.Content, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-3f, 3f),
                           38, UiKit.HealthColor, def.Health.ToString(), UiKit.TextMain, 21f, out hpText);
                AddUnitBadge(f.Content, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0f, 5f), 28, kind);
            }
            else
            {
                // 指令卡：没有攻/血/兵种三个徽章，整个下半区都给效果文字
                RectTransform bodyRt = UiKit.Anchored("Body", f.Content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -130f), new Vector2(130f, 66f));
                UiKit.AddLabelAuto(bodyRt, def.Text, 13f, 8f, mainText,
                                   TextAlignmentOptions.Center, FontStyles.Normal, true);

                // 右上角感叹号：Kards 用感叹号标识指令卡
                AddExclamationBadge(f.Content, new Vector2(-4f, -4f), 26);
            }

            AttachClick(v, onClick);
            return v;
        }

        // ==================================================================
        // 战场上的单位
        // ==================================================================

        /// <summary>
        /// 建一个战场单位。selected = 被选为攻击者；targetable = 当前可被指定为攻击/指令目标。
        /// showStatus：是否显示"部署失调 / 已行动"状态条。
        ///   这两个标记表达的都是"它这回合动不了了"，只有在该单位所属玩家的回合才有意义。
        ///   对手回合还挂着"部署失调"只会让玩家困惑，所以要传 false。
        /// </summary>
        public static CardView CreateBoardUnit(Transform parent, Vector2 position, UnitInstance unit,
                                               bool selected, bool targetable, bool attackable,
                                               bool showStatus, Action<CardView> onClick)
        {
            // [STAGE2C] HQ 与普通单位共用"战场卡"的位置，但外观必须一眼可辨：
            // 它没有攻击力、没有费用、没有部署失调，血量却是整局的胜负指标。
            if (unit.IsHq) return CreateHqCard(parent, position, unit, targetable, onClick);

            UnitType kind = unit.Def.UnitKind;

            Color state;
            if (selected) state = UiKit.SelectColor;
            else if (targetable) state = UiKit.TargetColor;
            else if (attackable) state = UiKit.LegalColor;
            else state = new Color(0f, 0f, 0f, 0f);

            // 本回合已经行动过的压暗一点，提示"它这回合动不了了"
            // [阶段2.6 · 任务3] "还有没有行动空间"改问引擎（坦克移+攻各一次；其余兵种二选一）
            Face f = BuildFace(parent, position, BoardCardSize, state,
                               CardArt.WindowColor(kind), CardArt.FrameTint(kind),
                               false, GameEngine.HasNoActionLeft(unit));

            CardView v = f.View;
            v.UnitId = unit.InstanceId;
            v.OwnerIndex = unit.OwnerIndex;

            AddUnitIllustration(f, kind);

            // ---- 左上角：战场上**只**显示行动花费（不再显示部署费用）----
            TextMeshProUGUI actText;
            BuildBadge(f.Content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -3f),
                       28, UiKit.Accent, unit.Def.ActionCost.ToString(), UiKit.TextDark, 17f, out actText);

            // ---- 卡名列 ----
            AddNamePlate(f.Content, new Vector2(36f, -1f), new Vector2(73f, 24f),
                         unit.Def.Name, 14f, UiKit.TextMain, new Color32(10, 13, 19, 200));

            // ---- 血条：压在开窗下沿（开窗底部在 92.4 / 150 处）----
            BuildHealthBar(f.Content, unit.Health, unit.MaxHealth, -84f, 70f);

            // ---- 状态文字：优先显示"不能动"的原因，没有就不画 ----
            string status = "";
            if (showStatus)
            {
                if (unit.DeployedThisTurn) status = "部署失调";
                // [阶段2.6 · 任务3] "已行动"= 已无任何行动空间（坦克=移+攻都做完；其余兵种=动过一次）
                else if (GameEngine.HasNoActionLeft(unit)) status = "已行动";
            }
            if (status.Length > 0)
            {
                RectTransform statusRt = UiKit.Anchored("Status", f.Content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                        new Vector2(0f, -94f), new Vector2(104f, 15f));
                Color statusColor = unit.DeployedThisTurn ? UiKit.TextDim : UiKit.LegalColor;
                UiKit.AddLabel(statusRt, status, 11f, statusColor, TextAlignmentOptions.Center);
            }

            // ---- 底部徽章行：攻击 / 兵种 / 生命 ----
            TextMeshProUGUI atkText, hpText;
            BuildBadge(f.Content, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(3f, 3f),
                       34, UiKit.AttackColor, unit.Attack.ToString(), UiKit.TextDark, 19f, out atkText);
            BuildBadge(f.Content, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-3f, 3f),
                       34, UiKit.HealthColor, unit.Health.ToString(), UiKit.TextMain, 19f, out hpText);
            AddUnitBadge(f.Content, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                         new Vector2(0f, 5f), 24, kind);

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
        ///   · 卡框染成金（自己）/ 暗红（敌方），与普通单位卡的兵种色调拉开距离
        /// </summary>
        public static CardView CreateHqCard(Transform parent, Vector2 position, UnitInstance unit,
                                            bool targetable, Action<CardView> onClick)
        {
            bool isSelf = unit.OwnerIndex == 0;

            Color window = isSelf ? new Color32(56, 46, 20, 255) : new Color32(54, 30, 30, 255);
            Color tint = targetable ? UiKit.TargetColor
                                    : (isSelf ? new Color(1.00f, 0.94f, 0.74f) : new Color(1.00f, 0.82f, 0.82f));

            Face f = BuildFace(parent, position, BoardCardSize,
                               targetable ? UiKit.TargetColor : new Color(0f, 0f, 0f, 0f),
                               window, tint, false, false);

            CardView v = f.View;
            v.UnitId = unit.InstanceId;
            v.OwnerIndex = unit.OwnerIndex;

            // ---- 开窗：一个大号星徽（一看就不是普通兵种）----
            // 卡高只有 150，竖向预算必须一分一分地排（开窗在 11.9..92.4 之间）：
            //   22..56 星徽 / 56..90 血量数字 / 93..102 血条 / 104..119 上限说明
            RectTransform markRt = UiKit.Anchored("HqMark", f.Illustration, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                  new Vector2(0f, -22f), new Vector2(34f, 34f));
            UiKit.AddImage(markRt, new Color32(0, 0, 0, 70), 17);
            UiKit.AddLabel(UiKit.Stretch("Glyph", markRt, 0f), "★", 24f,
                           isSelf ? UiKit.Accent : UiKit.TargetColor, TextAlignmentOptions.Center, FontStyles.Bold);

            // ---- 卡名列（压在开窗上沿）----
            AddNamePlate(f.Content, new Vector2(4f, -1f), new Vector2(104f, 24f),
                         "HQ · 指挥部", 14f, UiKit.Accent, new Color32(10, 13, 19, 210));

            // ---- 血量数字：整个卡面最重要的一个数 ----
            RectTransform hpRt = UiKit.Anchored("Hp", f.Content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                new Vector2(0f, -56f), new Vector2(100f, 34f));
            UiKit.AddLabel(hpRt, unit.Health.ToString(), 28f, UiKit.TextMain,
                           TextAlignmentOptions.Center, FontStyles.Bold);

            // ---- 血条 + 上限提示（都压在开窗下沿之下，避开窗内的星徽与数字）----
            BuildHealthBar(f.Content, unit.Health, unit.MaxHealth, -93f, 76f);

            RectTransform subRt = UiKit.Anchored("Sub", f.Content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                 new Vector2(0f, -104f), new Vector2(104f, 14f));
            UiKit.AddLabel(subRt, "血量上限 " + unit.MaxHealth, 10f, UiKit.TextDim, TextAlignmentOptions.Center);

            AttachClick(v, onClick);
            return v;
        }

        /// <summary>
        /// 建一个"背面"卡（用于显示对手手牌数量），不可点击。
        /// </summary>
        public static CardView CreateCardBack(Transform parent, Vector2 position, Vector2 size)
        {
            Face f = BuildFace(parent, position, size, new Color(0f, 0f, 0f, 0f),
                               new Color32(30, 36, 50, 255), new Color(0.74f, 0.80f, 0.96f),
                               false, false);

            CardView v = f.View;
            RectTransform markRt = UiKit.Anchored("Mark", f.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(size.x - 10f, size.y - 10f));
            UiKit.AddLabel(markRt, "?", Mathf.Min(size.x, size.y) * 0.42f, UiKit.TextMain,
                           TextAlignmentOptions.Center, FontStyles.Bold);
            return v;
        }

        // ==================================================================
        // 卡面骨架
        // ==================================================================

        /// <summary>BuildFace 的产物：卡对象本身 + 两个可继续往里放东西的层。</summary>
        private sealed class Face
        {
            public CardView View;
            /// <summary>插画层（开窗之内、卡框之下）。</summary>
            public RectTransform Illustration;
            /// <summary>内容层（名称、徽章、文字）—— 永远压在最上面。</summary>
            public RectTransform Content;
            /// <summary>开窗在卡面内的位置与尺寸（像素）。</summary>
            public float WindowTop, WindowHeight, WindowWidth;
        }

        /// <summary>
        /// 搭一张卡的骨架，子节点顺序 = 绘制顺序：
        /// 光晕 → 兜底底色 → 开窗底色 → 开窗阴影 → 插画层 → 卡框 → 状态薄光 → 压暗层 → 内容层。
        ///
        /// **为什么所有层都先建空节点、再往里填**：uGUI 的绘制顺序就是子物体的顺序，
        /// 而"卡框必须压在插画之上、内容又必须压在卡框之上"这条要求，
        /// 只能在建节点的时候就定好顺序。先建后填比"先画内容再补一张卡框"可控得多。
        /// </summary>
        private static Face BuildFace(Transform parent, Vector2 position, Vector2 size,
                                      Color stateColor, Color windowColor, Color frameTint,
                                      bool dim, bool muted)
        {
            float w = size.x;
            float h = size.y;

            // 底色块的圆角必须 ≥ 卡框自身的圆角，否则四角会露出底色的小尖角。
            // 卡框圆角实测 24px / 550px 宽 ⇒ 按卡宽换算；再 +2px 留余量。
            int cornerR = Mathf.Max(3, Mathf.RoundToInt(w * CardArt.CornerRadius) + 2);

            Face f = new Face();
            CardView v = new CardView();
            f.View = v;

            RectTransform rt = UiKit.Anchored("Card", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                              position, size);
            v.Rect = rt;

            bool hasState = stateColor.a > 0.01f;
            Sprite frameSprite = CardArt.FrameOrNull();

            // ---- ① 状态光晕（画在卡后，只露 3px；在 112px 的战场卡上比"只换边框色"醒目得多）----
            if (hasState)
            {
                RectTransform haloRt = UiKit.Stretch(LayerHalo, rt, -3f);
                UiKit.AddImageSized(haloRt, stateColor, cornerR + 3,
                                    Mathf.RoundToInt(w) + 6, Mathf.RoundToInt(h) + 6);
            }

            // ---- ② 兜底底色块（只在卡框取不到时才需要）----
            RectTransform baseRt = UiKit.Stretch(LayerBase, rt, 0f);
            Image baseImg = null;
            if (frameSprite == null)
            {
                baseImg = UiKit.AddImageSized(baseRt, new Color32(38, 44, 58, 255), cornerR,
                                              Mathf.RoundToInt(w), Mathf.RoundToInt(h));
            }

            // ---- ③ 开窗底色：四周各大 3px，多出来的部分被卡框实心区盖住 ⇒ 接缝永不外露 ----
            float wx = w * CardArt.WindowLeft - 3f;
            float wy = h * CardArt.WindowTop - 3f;
            float ww = w * CardArt.WindowWidth + 6f;
            float wh = h * CardArt.WindowHeight + 6f;

            RectTransform winRt = UiKit.Anchored(LayerWindow, rt, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                 new Vector2(wx, -wy), new Vector2(ww, wh));
            v.Fill = UiKit.AddImageSized(winRt, windowColor, 3,
                                         Mathf.RoundToInt(ww), Mathf.RoundToInt(wh));

            f.WindowTop = h * CardArt.WindowTop;
            f.WindowHeight = h * CardArt.WindowHeight;
            f.WindowWidth = w * CardArt.WindowWidth;

            // ---- ④ 开窗内的纵向压暗：底部略深，让白色兵种剪影与底色拉出层次 ----
            RectTransform fadeRt = UiKit.Stretch(LayerWindowFade, winRt, 0f);
            UiKit.AddSprite(fadeRt, UiKit.VerticalFade(64), new Color(0f, 0f, 0f, 0.34f));

            // ---- ⑤ 插画层（开窗之内、卡框之下）----
            f.Illustration = UiKit.Stretch(LayerIllustration, rt, 0f);

            // ---- ⑥ 卡框 ----
            if (frameSprite != null)
            {
                RectTransform frameRt = UiKit.Stretch(LayerFrame, rt, 0f);
                v.Frame = UiKit.AddSprite(frameRt, frameSprite, frameTint);

                // 状态色在卡框上再罩一层薄光：只靠背后 3px 的边，在战场尺寸下不够醒目
                if (hasState)
                {
                    RectTransform glowRt = UiKit.Stretch("Glow", rt, 0f);
                    UiKit.AddSprite(glowRt, frameSprite,
                                    new Color(stateColor.r, stateColor.g, stateColor.b, 0.26f));
                }
            }
            else
            {
                // 没卡框时，把"整卡底色块"当作可点击/可着色的图形
                v.Frame = baseImg;
            }

            // ---- ⑦ 压暗层（打不出的手牌 / 本回合已行动的单位）----
            // 0.45 是试出来的：0.5 会把兵种底色压到看不清"这张是什么兵种"，
            // 0.35 又不足以一眼看出"这张现在打不出去"。
            float dimAlpha = dim ? 0.45f : (muted ? 0.24f : 0f);
            if (dimAlpha > 0f)
            {
                RectTransform dimRt = UiKit.Stretch(LayerDim, rt, 0f);
                UiKit.AddImageSized(dimRt, new Color(0f, 0f, 0f, dimAlpha), cornerR,
                                    Mathf.RoundToInt(w), Mathf.RoundToInt(h));
            }

            // ---- ⑧ 内容层 ----
            f.Content = UiKit.Stretch(LayerContent, rt, 0f);

            return f;
        }

        // ==================================================================
        // 卡面零件
        // ==================================================================

        /// <summary>开窗里的兵种插画：居中的白色剪影。</summary>
        private static void AddUnitIllustration(Face f, UnitType kind)
        {
            float size = Mathf.Round(f.WindowHeight * 0.60f);
            float centerY = f.WindowTop + (f.WindowHeight - size) * 0.5f;

            RectTransform rt = UiKit.Anchored("UnitIcon", f.Illustration, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                              new Vector2(0f, -centerY), new Vector2(size, size));
            Sprite icon = CardArt.IconOrNull(kind);
            if (icon != null)
            {
                // 0.94 而不是纯白：纯白在亮底色上会"发光过头"，略压一点更像印上去的
                UiKit.AddSprite(rt, icon, new Color(1f, 1f, 1f, 0.94f));
                return;
            }

            // 兜底：图标资源缺失时退回汉字徽章（语义零歧义，随时可切回）
            UiKit.AddImageSized(rt, new Color32(16, 20, 28, 210), Mathf.RoundToInt(size * 0.5f),
                                Mathf.RoundToInt(size), Mathf.RoundToInt(size));
            UiKit.AddLabel(UiKit.Stretch("Glyph", rt, 0f), CardArt.ShortName(kind), size * 0.5f,
                           UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        /// <summary>开窗里的指令卡标记：一个大感叹号。</summary>
        private static void AddOrderSigil(Face f)
        {
            float size = Mathf.Round(f.WindowHeight * 0.60f);
            float centerY = f.WindowTop + (f.WindowHeight - size) * 0.5f;

            RectTransform rt = UiKit.Anchored("OrderSigil", f.Illustration, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                              new Vector2(0f, -centerY), new Vector2(size, size));
            UiKit.AddImageSized(rt, new Color32(14, 18, 26, 170), Mathf.RoundToInt(size * 0.5f),
                                Mathf.RoundToInt(size), Mathf.RoundToInt(size));
            UiKit.AddLabel(UiKit.Stretch("Glyph", rt, 0f), "!", size * 0.72f, UiKit.Accent,
                           TextAlignmentOptions.Center, FontStyles.Bold);
        }

        /// <summary>"名称底色条 + 卡名"，压在开窗的上沿。</summary>
        private static void AddNamePlate(RectTransform parent, Vector2 position, Vector2 size,
                                         string name, float fontSize, Color textColor, Color plateColor)
        {
            RectTransform plate = UiKit.Anchored("NamePlate", parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                 position, size);
            UiKit.AddImageSized(plate, plateColor, 5, Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));

            RectTransform labelRt = UiKit.Stretch("Name", plate, 2f);
            UiKit.AddLabelAuto(labelRt, name, fontSize, fontSize * 0.72f, textColor,
                               TextAlignmentOptions.Center, FontStyles.Bold, true);
        }

        /// <summary>
        /// 小号"行动花费"药丸：深色底 + 亮字。
        /// 它压在开窗的兵种底色上，所以必须有自己的一层深色底，否则数字看不清。
        /// </summary>
        private static void AddActionCostPill(RectTransform parent, Vector2 position, Vector2 size,
                                              int actionCost, float fontSize)
        {
            RectTransform pill = UiKit.Anchored("ActionCost", parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                position, size);
            int iw = Mathf.RoundToInt(size.x);
            int ih = Mathf.RoundToInt(size.y);

            UiKit.AddImageSized(pill, new Color32(120, 138, 160, 200), 6, iw, ih);
            RectTransform inner = UiKit.Stretch("Inner", pill, 2f);
            UiKit.AddImageSized(inner, new Color32(14, 19, 27, 255), 4, iw - 4, ih - 4);

            RectTransform labelRt = UiKit.Stretch("Value", pill, 0f);
            UiKit.AddLabel(labelRt, actionCost.ToString(), fontSize, UiKit.TextMain,
                           TextAlignmentOptions.Center, FontStyles.Bold);
        }

        /// <summary>右上角的感叹号（Kards 用它标识指令卡）。</summary>
        private static void AddExclamationBadge(RectTransform parent, Vector2 position, int size)
        {
            RectTransform rt = UiKit.Anchored("OrderMark", parent, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                              position, new Vector2(size, size));
            UiKit.AddImage(rt, UiKit.Accent, size / 2);
            RectTransform inner = UiKit.Stretch("Inner", rt, 3f);
            UiKit.AddImageSized(inner, new Color32(22, 17, 6, 255), (size - 6) / 2, size - 6, size - 6);

            UiKit.AddLabel(UiKit.Stretch("Glyph", rt, 0f), "!", size * 0.60f, UiKit.Accent,
                           TextAlignmentOptions.Center, FontStyles.Bold);
        }

        /// <summary>底部中间的兵种徽章：深色圆底 + 内嵌兵种剪影。</summary>
        private static void AddUnitBadge(RectTransform parent, Vector2 anchor, Vector2 pivot,
                                         Vector2 position, int dia, UnitType kind)
        {
            RectTransform rt = UiKit.Anchored("UnitBadge", parent, anchor, pivot, position,
                                              new Vector2(dia, dia));
            UiKit.AddImage(rt, new Color32(140, 158, 180, 210), dia / 2);

            RectTransform inner = UiKit.Stretch("Inner", rt, 2f);
            UiKit.AddImageSized(inner, new Color32(24, 30, 41, 255), (dia - 4) / 2, dia - 4, dia - 4);

            Sprite icon = CardArt.IconOrNull(kind);
            if (icon != null)
            {
                // 图标占徽章内径的 ~75%，四周留白才不会"顶到圈上"
                RectTransform iconRt = UiKit.Stretch("Icon", rt, Mathf.RoundToInt(dia * 0.125f));
                UiKit.AddSprite(iconRt, icon, UiKit.TextMain);
            }
            else
            {
                UiKit.AddLabel(UiKit.Stretch("Glyph", rt, 0f), CardArt.ShortName(kind), dia * 0.52f,
                               UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            }
        }

        /// <summary>建一个圆形数值徽章（费用 / 攻击 / 生命都用它）。外圈有一道深色描边，
        /// 保证它压在插画或卡框金饰上都分得清。</summary>
        private static Image BuildBadge(RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 position,
                                        int size, Color fill, string label, Color labelColor,
                                        float fontSize, out TextMeshProUGUI text)
        {
            RectTransform holder = UiKit.Anchored("BadgeHolder", parent, anchor, pivot, position,
                                                  new Vector2(size + 4, size + 4));
            UiKit.AddImage(holder, new Color32(11, 14, 20, 240), (size + 4) / 2);

            RectTransform inner = UiKit.Stretch("Badge", holder, 2f);
            Image img = UiKit.AddImageSized(inner, fill, size / 2, size, size);

            RectTransform labelRt = UiKit.Stretch("Label", holder, 0f);
            text = UiKit.AddLabel(labelRt, label, fontSize, labelColor,
                                  TextAlignmentOptions.Center, FontStyles.Bold);
            return img;
        }

        /// <summary>
        /// 细血条。topOffset 是"从卡顶往下多少像素"（负值），width 是条宽。
        /// 用它而不是只靠数字：一眼就能看出还剩几成，不用读数字。
        /// </summary>
        private static void BuildHealthBar(RectTransform parent, int current, int max, float topOffset, float width)
        {
            int iw = Mathf.RoundToInt(width);

            RectTransform bg = UiKit.Anchored("HpBarBg", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                              new Vector2(0f, topOffset), new Vector2(width, 9f));
            UiKit.AddImageSized(bg, new Color32(10, 13, 19, 235), 4, iw, 9);

            float ratio = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
            // 血条宽度取整：避免浮点宽度生成一堆几乎一样的贴图，把缓存撑爆
            int fillWidth = Mathf.RoundToInt((iw - 6) * ratio);
            if (fillWidth < 2) fillWidth = 2;

            RectTransform fill = UiKit.Anchored("HpBarFill", bg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                                new Vector2(3f, 0f), new Vector2(fillWidth, 5f));
            UiKit.AddImage(fill, ratio > 0.5f ? UiKit.HealthColor : UiKit.HealthLow, 3);
        }

        private static void AttachClick(CardView v, Action<CardView> onClick)
        {
            if (onClick == null) return;
            Button btn = UiKit.AddButton(v.Rect, v.Frame, Color.white, new Color(1.08f, 1.08f, 1.08f, 1f));
            btn.onClick.AddListener(delegate { onClick(v); });
        }
    }
}
