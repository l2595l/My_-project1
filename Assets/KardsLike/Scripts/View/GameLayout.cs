using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KardsLike.View
{
    /// <summary>
    /// 界面装配层（阶段 2 · 任务 B 前置拆分，从 GamePresenter 抽出）。
    ///
    /// 职责边界只有两条：
    ///   · 把整个界面由代码搭出来，并持有每一个控件的引用
    ///   · 记住"这一帧由脚本生成的卡牌对象"，供下一次刷新整批销毁
    ///
    /// 它**不含**任何规则判断与交互逻辑：不知道什么牌打得出去、什么单位能攻击，
    /// 那些是 Core 与 GameInteraction 的事。它只在 Build 时把点击事件转交给交互层。
    ///
    /// [STAGE2B] 三条战线把"两条战场行"改成三条：
    ///   电脑支援阵线（上）→ 前线（中，双方争夺）→ 我方支援阵线（下）
    /// 纵向预算的重排见 Build 里的注释 —— 1080 高要塞下三行 + 手牌 + 倒计时，
    /// 每一处 y 都是算过的，改动前请先重新算一遍。
    /// </summary>
    internal sealed class GameLayout
    {
        /// <summary>参考分辨率。坐标都基于它，最后按 min(屏宽/1920, 屏高/1080) 等比缩放并居中。</summary>
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        // ---- [STAGE2B] 三条战线行的纵向位置（设计画框坐标，原点在正中）----
        // 从上到下：384..224 / 204..44 / 24..-136，行高统一 160。
        public const float EnemySupportY = 304f;
        public const float FrontlineY = 124f;
        public const float PlayerSupportY = -56f;
        public const float LineRowHeight = 160f;
        private const float LineRowWidth = 1120f;

        // ---- 固定控件引用（只建一次，之后只改内容）----
        public RectTransform CanvasRect;
        public RectTransform DesignFrame;

        public HqWidget EnemyHq;
        public HqWidget PlayerHq;

        /// <summary>[STAGE2B] 三条战线行：电脑支援阵线 / 前线 / 我方支援阵线。</summary>
        public RectTransform EnemySupportRow;
        public RectTransform FrontlineRow;
        public RectTransform PlayerSupportRow;

        /// <summary>前线行的边框（用来把"谁占着前线"直接画在颜色上）。</summary>
        public Image FrontlineFrame;

        /// <summary>前线行左上角的说明文字（"前线（无人占领）"这类）。</summary>
        public TextMeshProUGUI FrontlineInfoText;

        public RectTransform HandRow;
        public RectTransform EnemyHandBacks;

        public TextMeshProUGUI EnemyInfoText;
        public TextMeshProUGUI ResourceText;
        public TextMeshProUGUI ResourceSubText;
        public TextMeshProUGUI TurnValueText;
        public TextMeshProUGUI TurnOwnerText;
        public TextMeshProUGUI SeedText;
        public TextMeshProUGUI HintText;
        public TextMeshProUGUI LogText;

        public Image PlayOrderFrame;
        public GameObject PlayOrderRoot;

        public Image EndTurnFrame;
        public Button EndTurnButton;
        public TextMeshProUGUI EndTurnLabel;

        public GameObject GameOverRoot;
        public Image GameOverFrame;
        public TextMeshProUGUI GameOverTitle;
        public TextMeshProUGUI GameOverSub;

        // ---- 回合倒计时（阶段 2 · 任务 A）----
        public GameObject TimerRoot;
        public Image TimerRingFill;
        public TextMeshProUGUI TimerValue;

        /// <summary>当前这一帧由脚本生成出来的所有卡牌，刷新时整批销毁。</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        // ==================================================================
        // 装配
        // ==================================================================

        /// <summary>
        /// 搭出整个界面，并把所有点击事件转交给交互层。整个生命周期只调用一次（Start）。
        /// </summary>
        public void Build(GameInteraction interaction)
        {
            EnsureEventSystem();

            // ---- Canvas ----
            GameObject canvasGo = new GameObject("KardsCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 刻意不用 CanvasScaler 的 ScaleWithScreenSize。
            // 原因：那套是按"宽高各自缩放再混合"来的，在非 16:9 的窗口下
            // 参考坐标系会被拉伸，导致左右两侧的元素和中间的手牌挤在一起重叠。
            // 改成"设计画框"方案：整个界面固定搭在 1920x1080 里，
            // 再按 min(屏宽/1920, 屏高/1080) 等比缩放并居中。
            // 代价是极端比例下会留黑边，换来的是"任何窗口尺寸都不会错位"。
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            RectTransform canvasRoot = (RectTransform)canvasGo.transform;
            CanvasRect = canvasRoot;

            // 铺满整屏的底色（放在画框之外，保证黑边也是深色而不是 URP 天空盒）
            RectTransform screenBg = UiKit.Stretch("ScreenBackground", canvasRoot, 0f);
            UiKit.AddImageSized(screenBg, new Color32(9, 12, 17, 255), 0, 8, 8);

            DesignFrame = UiKit.Anchored("DesignFrame", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                          Vector2.zero, new Vector2(RefWidth, RefHeight));
            UpdateDesignScale();

            RectTransform root = DesignFrame;

            // ---- 背景 ----
            RectTransform bg = UiKit.Stretch("Background", root, 0f);
            UiKit.AddImageSized(bg, UiKit.Bg, 0, 8, 8);

            // ---- 顶部：敌方 HQ / 敌方手牌 / 回合信息 ----
            EnemyHq = BuildHqWidget(root, true, interaction);

            RectTransform enemyInfo = UiKit.Panel("EnemyInfo", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                 new Vector2(0f, -24f), new Vector2(420f, 112f),
                                                 UiKit.PanelFill, UiKit.PanelBorder, 12);
            RectTransform enemyInfoTitle = UiKit.Anchored("Title", enemyInfo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                          new Vector2(0f, -10f), new Vector2(390f, 28f));
            EnemyInfoText = UiKit.AddLabel(enemyInfoTitle, "", 17f, UiKit.TextMain, TextAlignmentOptions.Center);
            EnemyHandBacks = UiKit.Anchored("Backs", enemyInfo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                             new Vector2(0f, 12f), new Vector2(400f, 54f));

            RectTransform turnPanel = UiKit.Panel("TurnPanel", root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                                  new Vector2(-24f, -24f), new Vector2(360f, 132f),
                                                  UiKit.PanelFill, UiKit.PanelBorder, 12);
            RectTransform turnValueRt = UiKit.Anchored("TurnValue", turnPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                       new Vector2(0f, -12f), new Vector2(330f, 44f));
            TurnValueText = UiKit.AddLabel(turnValueRt, "", 30f, UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            RectTransform turnOwnerRt = UiKit.Anchored("TurnOwner", turnPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                       new Vector2(0f, -54f), new Vector2(330f, 28f));
            TurnOwnerText = UiKit.AddLabel(turnOwnerRt, "", 19f, UiKit.Accent, TextAlignmentOptions.Center);
            RectTransform seedRt = UiKit.Anchored("Seed", turnPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                                  new Vector2(0f, 12f), new Vector2(330f, 26f));
            SeedText = UiKit.AddLabel(seedRt, "", 14f, UiKit.TextDim, TextAlignmentOptions.Center);

            // ---- 重开按钮 ----
            RectTransform restartRt = UiKit.Anchored("RestartButton", root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                                     new Vector2(-24f, -168f), new Vector2(360f, 52f));
            Image restartFrame = UiKit.AddImage(restartRt, UiKit.PanelBorder, 10);
            RectTransform restartInner = UiKit.Stretch("Fill", restartRt, 2f);
            UiKit.AddImageSized(restartInner, UiKit.PanelFill, 9, 356, 48);
            RectTransform restartLabelRt = UiKit.Stretch("Label", restartRt, 0f);
            UiKit.AddLabel(restartLabelRt, "重开一局（R）", 18f, UiKit.TextMain, TextAlignmentOptions.Center);
            Button restartBtn = UiKit.AddButton(restartRt, restartFrame, Color.white, new Color(1.1f, 1.1f, 1.1f, 1f));
            restartBtn.onClick.AddListener(interaction.OnRestartClicked);

            // ---- 右侧：日志 ----
            RectTransform logPanel = UiKit.Panel("LogPanel", root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                                 new Vector2(-24f, -236f), new Vector2(360f, 430f),
                                                 new Color32(20, 25, 34, 255), UiKit.PanelBorder, 12);
            RectTransform logTitleRt = UiKit.Anchored("Title", logPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -10f), new Vector2(320f, 26f));
            UiKit.AddLabel(logTitleRt, "对局日志", 16f, UiKit.Accent, TextAlignmentOptions.Center);
            RectTransform logBodyRt = UiKit.Stretch("Body", logPanel, 0f);
            logBodyRt.offsetMin = new Vector2(14f, 14f);
            logBodyRt.offsetMax = new Vector2(-14f, -44f);
            LogText = UiKit.AddLabel(logBodyRt, "", 14f, UiKit.TextDim,
                                     TextAlignmentOptions.TopLeft, FontStyles.Normal, true);

            // ---- [STAGE2B] 三条战线 ----
            // 纵向预算（画框 1080 高，中心在 0）：
            //   顶部信息带   +404 .. +540   （敌方 HQ / 敌方手牌 / 回合面板）
            //   电脑支援阵线 +384 .. +224
            //   前线         +204 ..  +44
            //   我方支援阵线  +24 .. -136
            //   提示 / 按钮  -151 .. -239
            //   手牌行       -300 .. -508   （选中上浮时顶到 -278）
            // 相邻处最小留白 12px，全部目视核对过；改任何一个 y 都要把这张表重算一遍。
            EnemySupportRow = BuildLineRow(root, "EnemySupportLine", EnemySupportY, "电脑 · 支援阵线",
                                           new Color32(24, 28, 38, 255), new Color32(74, 46, 50, 255),
                                           null, out _);
            FrontlineRow = BuildLineRow(root, "Frontline", FrontlineY, "前线（无人占领）",
                                        new Color32(26, 33, 30, 255), new Color32(48, 68, 54, 255),
                                        interaction.OnFrontlineClicked, out TextMeshProUGUI frontTitle);
            FrontlineInfoText = frontTitle;
            FrontlineFrame = FrontlineRow.GetComponent<Image>();
            PlayerSupportRow = BuildLineRow(root, "PlayerSupportLine", PlayerSupportY, "你 · 支援阵线",
                                            new Color32(22, 28, 37, 255), new Color32(38, 48, 60, 255),
                                            interaction.OnPlayerBoardClicked, out _);

            // ---- 中央：操作提示 / 确认按钮 ----
            RectTransform hintRt = UiKit.Anchored("Hint", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  new Vector2(0f, -166f), new Vector2(1340f, 30f));
            HintText = UiKit.AddLabel(hintRt, "", 19f, UiKit.TextMain, TextAlignmentOptions.Center);

            PlayOrderRoot = UiKit.Anchored("PlayOrderButton", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                           new Vector2(0f, -216f), new Vector2(420f, 46f)).gameObject;
            PlayOrderFrame = UiKit.AddImage((RectTransform)PlayOrderRoot.transform, UiKit.LegalColor, 12);
            RectTransform playInner = UiKit.Stretch("Fill", PlayOrderRoot.transform, 2f);
            UiKit.AddImageSized(playInner, new Color32(24, 52, 36, 255), 11, 416, 42);
            RectTransform playLabelRt = UiKit.Stretch("Label", PlayOrderRoot.transform, 0f);
            UiKit.AddLabel(playLabelRt, "打出指令", 19f, UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Button playBtn = UiKit.AddButton((RectTransform)PlayOrderRoot.transform, PlayOrderFrame,
                                             Color.white, new Color(1.1f, 1.1f, 1.1f, 1f));
            playBtn.onClick.AddListener(interaction.OnPlayOrderClicked);
            PlayOrderRoot.SetActive(false);

            // ---- 底部：我方 HQ / 资源与行动点 / 结束回合 / 手牌 ----
            PlayerHq = BuildHqWidget(root, false, interaction);

            RectTransform resPanel = UiKit.Panel("ResourcePanel", root, new Vector2(0f, 0f), new Vector2(0f, 0f),
                                                 new Vector2(24f, 148f), new Vector2(320f, 108f),
                                                 UiKit.PanelFill, UiKit.PanelBorder, 10);
            RectTransform resValueRt = UiKit.Anchored("Value", resPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                      new Vector2(0f, -12f), new Vector2(300f, 42f));
            ResourceText = UiKit.AddLabel(resValueRt, "", 27f, UiKit.Accent, TextAlignmentOptions.Center, FontStyles.Bold);

            // [STAGE2C] 独立的"行动点"已删除 —— 移动与攻击的花费改成从**资源**里扣。
            // 腾出来的这一行改成常驻说明：不写清楚的话，玩家会以为移动是免费的，
            // 然后对着"资源莫名少了一截"困惑。
            RectTransform actionCostRt = UiKit.Anchored("ActionCost", resPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                        new Vector2(0f, -58f), new Vector2(300f, 24f));
            UiKit.AddLabel(actionCostRt, "移动 / 攻击消耗资源（按费用 1~3）", 14f, UiKit.TextDim,
                           TextAlignmentOptions.Center);

            RectTransform resSubRt = UiKit.Anchored("Sub", resPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                                    new Vector2(0f, 8f), new Vector2(300f, 22f));
            ResourceSubText = UiKit.AddLabel(resSubRt, "", 14f, UiKit.TextDim, TextAlignmentOptions.Center);

            RectTransform endTurnRt = UiKit.Anchored("EndTurnButton", root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                                                     new Vector2(-24f, 24f), new Vector2(320f, 112f));
            EndTurnFrame = UiKit.AddImage(endTurnRt, UiKit.Accent, 14);
            RectTransform endTurnInner = UiKit.Stretch("Fill", endTurnRt, 2f);
            UiKit.AddImageSized(endTurnInner, new Color32(58, 48, 26, 255), 13, 316, 108);
            RectTransform endTurnLabelRt = UiKit.Stretch("Label", endTurnRt, 0f);
            EndTurnLabel = UiKit.AddLabel(endTurnLabelRt, "结束回合", 26f, UiKit.TextMain,
                                          TextAlignmentOptions.Center, FontStyles.Bold);
            EndTurnButton = UiKit.AddButton(endTurnRt, EndTurnFrame, Color.white, new Color(1.12f, 1.12f, 1.12f, 1f));
            EndTurnButton.onClick.AddListener(interaction.OnEndTurnClicked);

            HandRow = UiKit.Anchored("HandRow", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                     new Vector2(0f, 32f), new Vector2(1200f, 208f));

            // ---- 回合倒计时（阶段 2 · 任务 A）----
            BuildTurnTimer(root);

            // ---- 结算浮层 ----
            BuildGameOverLayer(root, interaction);
        }

        /// <summary>
        /// 把 1920x1080 的设计画框等比缩放到当前窗口能容纳的最大尺寸并居中。
        /// 这样界面在任何宽高比下都不会重叠或错位，只会留出黑边。
        /// </summary>
        public void UpdateDesignScale()
        {
            if (DesignFrame == null) return;

            float scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
            if (scale <= 0f) scale = 1f;

            Vector3 target = new Vector3(scale, scale, 1f);
            if ((DesignFrame.localScale - target).sqrMagnitude > 1e-6f)
            {
                DesignFrame.localScale = target;
            }
        }

        // ==================================================================
        // 生成对象的记账
        // ==================================================================

        /// <summary>把这一帧生成的卡牌登记进来，下次刷新时整批销毁。</summary>
        public void Track(GameObject go)
        {
            _spawned.Add(go);
        }

        /// <summary>
        /// 清空上一帧生成的对象。先 SetActive(false) 再 Destroy ——
        /// Destroy 要等到帧末才真正执行，不先隐藏的话新旧卡牌会在同一帧重叠显示。
        /// </summary>
        public void ClearSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] == null) continue;
                _spawned[i].SetActive(false);
                Object.Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        // ==================================================================
        // 子装配
        // ==================================================================

        private static void EnsureEventSystem()
        {
            // 没有 EventSystem 的话所有按钮都点不动 —— 这是运行时建 UI 最常见的"界面正常但没反应"
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>
        /// [STAGE2B] 一条战线行：底衬 + 左上角说明文字 +（可选）整块可点击。
        ///
        /// 三条行共用这一个构造函数，是为了保证它们的尺寸、留白、点击方式完全一致 ——
        /// 三行里少写一行交互、或者高度差 2px，在截图里都很难看出来，但手感会立刻变差。
        /// </summary>
        private RectTransform BuildLineRow(Transform parent, string name, float y, string title,
                                           Color fill, Color border,
                                           UnityEngine.Events.UnityAction onClick,
                                           out TextMeshProUGUI titleText)
        {
            RectTransform row = UiKit.Panel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                            new Vector2(0f, y), new Vector2(LineRowWidth, LineRowHeight),
                                            fill, border, 14);

            RectTransform titleRt = UiKit.Anchored("Title", row, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                   new Vector2(14f, -6f), new Vector2(460f, 22f));
            titleText = UiKit.AddLabel(titleRt, title, 15f, UiKit.TextDim, TextAlignmentOptions.Left);

            if (onClick != null)
            {
                Image frame = row.GetComponent<Image>();
                Button btn = UiKit.AddButton(row, frame, Color.white, new Color(1.06f, 1.08f, 1.12f, 1f));
                btn.onClick.AddListener(onClick);
            }
            return row;
        }

        private HqWidget BuildHqWidget(Transform parent, bool isEnemy, GameInteraction interaction)
        {
            HqWidget w = new HqWidget();

            Vector2 anchor = isEnemy ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            Vector2 pivot = anchor;
            Vector2 pos = isEnemy ? new Vector2(24f, -24f) : new Vector2(24f, 24f);

            w.Root = UiKit.Anchored(isEnemy ? "EnemyHQ" : "PlayerHQ", parent, anchor, pivot, pos,
                                    new Vector2(320f, 112f));

            Color frameColor = isEnemy ? UiKit.CardUnitEnemy : UiKit.PanelFill;
            w.Frame = UiKit.AddImage(w.Root, frameColor, 12);
            RectTransform inner = UiKit.Stretch("Fill", w.Root, 2f);
            UiKit.AddImageSized(inner, new Color32(28, 34, 45, 236), 11, 316, 108);

            RectTransform labelRt = UiKit.Anchored("Label", w.Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                   new Vector2(16f, -12f), new Vector2(200f, 26f));
            UiKit.AddLabel(labelRt, isEnemy ? "敌方 HQ" : "我方 HQ", 17f,
                           isEnemy ? UiKit.TextMain : UiKit.Accent, TextAlignmentOptions.Left);

            RectTransform valueRt = UiKit.Anchored("Value", w.Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                                                   new Vector2(16f, -40f), new Vector2(160f, 52f));
            w.Value = UiKit.AddLabel(valueRt, "20", 42f, UiKit.TextMain, TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform subRt = UiKit.Anchored("Sub", w.Root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                                 new Vector2(-16f, -22f), new Vector2(160f, 28f));
            w.Sub = UiKit.AddLabel(subRt, "", 15f, UiKit.TextDim, TextAlignmentOptions.Right);

            RectTransform barBg = UiKit.Anchored("BarBg", w.Root, new Vector2(0f, 0f), new Vector2(0f, 0f),
                                                 new Vector2(16f, 14f), new Vector2(288f, 16f));
            UiKit.AddImage(barBg, new Color32(14, 18, 24, 255), 8);
            w.BarFill = UiKit.AddImage(UiKit.Anchored("BarFill", barBg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                                      new Vector2(3f, 0f), new Vector2(282f, 10f)),
                                       UiKit.HealthColor, 5);

            // [STAGE2B] 敌方 HQ 是"打脸"的唯一入口（以前敌方战场行的空白区也能打脸，
            // 但那条行的位置上现在是"电脑支援阵线"，语义完全不同，所以去掉了那个绑定）。
            Button btn = UiKit.AddButton(w.Root, w.Frame, Color.white,
                                         isEnemy ? new Color(1.15f, 0.95f, 0.95f, 1f) : new Color(1.03f, 1.03f, 1.03f, 1f));
            btn.onClick.AddListener(isEnemy ? (UnityEngine.Events.UnityAction)interaction.OnEnemyHqClicked
                                            : interaction.OnPlayerBoardClicked);
            return w;
        }

        private void BuildGameOverLayer(Transform parent, GameInteraction interaction)
        {
            GameOverRoot = UiKit.Stretch("GameOver", parent, 0f).gameObject;

            RectTransform dim = UiKit.Stretch("Dim", GameOverRoot.transform, 0f);
            UiKit.AddImageSized(dim, new Color32(6, 9, 13, 220), 0, 8, 8);

            RectTransform card = UiKit.Anchored("Card", GameOverRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                Vector2.zero, new Vector2(660f, 340f));
            GameOverFrame = UiKit.AddImage(card, UiKit.Accent, 18);
            RectTransform cardInner = UiKit.Stretch("Fill", card, 3f);
            UiKit.AddImageSized(cardInner, new Color32(24, 30, 40, 255), 16, 654, 334);

            RectTransform titleRt = UiKit.Anchored("Title", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                   new Vector2(0f, -46f), new Vector2(600f, 80f));
            GameOverTitle = UiKit.AddLabel(titleRt, "", 60f, UiKit.WinColor, TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform subRt = UiKit.Anchored("Sub", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                                 new Vector2(0f, -140f), new Vector2(580f, 70f));
            GameOverSub = UiKit.AddLabel(subRt, "", 19f, UiKit.TextDim, TextAlignmentOptions.Center, FontStyles.Normal, true);

            RectTransform btnRt = UiKit.Anchored("Again", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                                 new Vector2(0f, 32f), new Vector2(300f, 62f));
            Image btnFrame = UiKit.AddImage(btnRt, UiKit.Accent, 12);
            RectTransform btnInner = UiKit.Stretch("Fill", btnRt, 2f);
            UiKit.AddImageSized(btnInner, new Color32(58, 48, 26, 255), 11, 296, 58);
            RectTransform btnLabelRt = UiKit.Stretch("Label", btnRt, 0f);
            UiKit.AddLabel(btnLabelRt, "再来一局", 22f, UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Button btn = UiKit.AddButton(btnRt, btnFrame, Color.white, new Color(1.12f, 1.12f, 1.12f, 1f));
            btn.onClick.AddListener(interaction.OnRestartClicked);

            GameOverRoot.SetActive(false);
        }

        /// <summary>
        /// 建倒计时控件：暗色底环 + 可填充的进度环 + 居中的秒数（阶段 2 · 任务 A）。
        /// 位置放在「结束回合」按钮正上方 —— 那里既不会压到手牌行
        /// （手牌行收紧后最右一张牌距屏幕右边至少 370px），也不会压到右侧日志面板。
        /// </summary>
        private void BuildTurnTimer(Transform parent)
        {
            const int diameter = 116;
            const int ringWidth = 12;

            TimerRoot = UiKit.Anchored("TurnTimer", parent, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                                       new Vector2(-184f, 156f), new Vector2(diameter, diameter)).gameObject;

            // 底环：始终画满一整圈，颜色压暗，作为"总量"的背景提示
            UiKit.AddSprite((RectTransform)TimerRoot.transform,
                            UiKit.Ring(diameter, ringWidth), new Color32(30, 38, 50, 235));

            // 进度环：Filled + Radial360，从 12 点方向顺时针消退
            RectTransform fillRt = UiKit.Stretch("Fill", TimerRoot.transform, 0f);
            TimerRingFill = UiKit.AddSprite(fillRt, UiKit.Ring(diameter, ringWidth), UiKit.LegalColor);
            TimerRingFill.type = Image.Type.Filled;
            TimerRingFill.fillMethod = Image.FillMethod.Radial360;
            TimerRingFill.fillOrigin = (int)Image.Origin360.Top;
            TimerRingFill.fillClockwise = true;
            TimerRingFill.fillAmount = 1f;

            RectTransform valueRt = UiKit.Stretch("Value", TimerRoot.transform, 0f);
            TimerValue = UiKit.AddLabel(valueRt, "", 34f, UiKit.TextMain, TextAlignmentOptions.Center, FontStyles.Bold);

            TimerRoot.SetActive(false);
        }

        /// <summary>HQ 控件的引用集合（血值 / 血条 / 副标题）。</summary>
        internal sealed class HqWidget
        {
            public RectTransform Root;
            public Image Frame;
            public Image BarFill;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI Sub;
        }
    }
}
