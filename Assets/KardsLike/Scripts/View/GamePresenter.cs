using System.Collections;
using System.Collections.Generic;
using KardsLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    /// <summary>
    /// 对局主控（阶段 2 · 任务 B 前置拆分后的瘦身版）。
    ///
    /// 现在它只剩三件事：
    ///   · 持有引擎、AI、随机种子与"电脑回合中"这一个流程标记
    ///   · 把 GameState 画出来（刷新策略仍是"整个重建"，见下）
    ///   · 驱动人类回合的 60 秒倒计时
    ///
    /// 拆出去的两块（阶段 2 任务 B 之前完成，因为原文件已到 1145 行、越过
    /// 项目自定的 1100 行阈值）：
    ///   · <see cref="GameLayout"/> —— 界面装配与控件引用，"画在哪"
    ///   · <see cref="GameInteraction"/> —— 选择状态与点击处理，"点了算什么意思"
    ///
    /// 架构约定（设计文档 §8 第 3 条）：
    ///   · 它只 **读** GameState 来渲染，只 **调用** GameEngine 的方法来改状态。
    ///     绝不允许自己去改 state 里的任何字段。
    ///   · 刷新策略是"整个重建"而不是"增量更新"。因为卡牌数量最多
    ///     7+7+10 = 24 个，全量重建的开销可以忽略，但换来的是
    ///     "界面永远等于状态"这条不变量 —— 增量更新一旦漏了一处，
    ///     就会出现"界面显示 5 血、实际已经死了"这类最难查的 bug。
    /// </summary>
    public sealed partial class GamePresenter : MonoBehaviour
    {
        /// <summary>
        /// 人类回合的时长上限（秒）。来自《阶段2-设计要点与启动提示词.md》§三。
        ///
        /// 它**只能**待在 View 层，这是硬性的（同文档 §3.3）：
        ///   · Core 的硬约束是零 UnityEngine 依赖，Time.deltaTime 会让它直接编不过
        ///   · 阶段 4 要 headless 跑上万局模拟，Core 里只要有一处 60 秒等待，模拟就不可用
        ///   · 一旦"时间"进入规则层，"同种子结果完全一致"这条契约立刻失效
        /// </summary>
        private const float TurnTimeLimitSeconds = 60f;

        /// <summary>剩余时间进入这个阈值后，倒计时转红并做强调（同文档 §3.1「表现」）。</summary>
        private const float TimerWarnSeconds = 10f;

        private GameEngine _engine;
        private GreedyAI _ai;
        private uint _seed;

        // ---- 拆分出来的两个协作者 ----
        private GameLayout _layout;
        private GameInteraction _interaction;

        // ---- 流程状态 ----
        /// <summary>电脑回合 / 结算中，屏蔽玩家输入。</summary>
        private bool _busy;

        // ---- 回合倒计时（阶段 2 · 任务 A）----
        /// <summary>
        /// 倒计时总开关。Play 时可在 Inspector 勾选，也可按 T 键即时切换。
        /// 留着它是为了调试：否则每改一次代码都得在 60 秒内完成验证（§3.4 第 4 条）。
        /// </summary>
        [Tooltip("人类回合的 60 秒倒计时总开关。Play 时也可按 T 键切换。")]
        [SerializeField] private bool _turnTimerEnabled = true;

        private float _turnTimeLeft;      // 本回合剩余秒数
        private bool _timerRunning;       // 是否正在走表（只有人类回合会为 true）

        /// <summary>
        /// 本段计时对应的 HalfTurn。只有它变化才说明"新的人类回合开始了"，据此把读数
        /// 重置为 60。这样玩家出牌引起的界面刷新不会把倒计时重置掉。
        /// </summary>
        private int _timerHalfTurnStamp = int.MinValue;

        // ==================================================================
        // 供协作者使用的内部接口
        // ==================================================================

        /// <summary>引擎。GameInteraction 靠它做校验，但只读。</summary>
        internal GameEngine Engine { get { return _engine; } }

        /// <summary>是否正在播放电脑回合。</summary>
        internal bool Busy { get { return _busy; } }

        // ==================================================================
        // 生命周期
        // ==================================================================

        private void Start()
        {
            _layout = new GameLayout();
            _interaction = new GameInteraction(this);

            _layout.Build(_interaction);
            ApplyAnimSpeed();
            NewGame(NewSeed());
        }

        private void Update()
        {
            _layout.UpdateDesignScale();

            // 键盘快捷键：R 重开，T 切换倒计时，Y 切换动画倍率，Esc 取消当前选择。
            // 都只是便利，不影响规则。
            if (Input.GetKeyDown(KeyCode.R)) { RestartGame(); return; }
            if (Input.GetKeyDown(KeyCode.T)) { ToggleTurnTimer(); }
            if (Input.GetKeyDown(KeyCode.Y)) { CycleAnimSpeed(); }
            if (Input.GetKeyDown(KeyCode.Escape)) { _interaction.ClearSelection(); _interaction.Error = ""; Refresh(); }

            // 倒计时推进放在最后：它可能触发"超时自动结束回合"，
            // 那是本帧最重的一个动作，让按键处理先跑完。
            TickTurnTimer(Time.deltaTime);
        }

        private static uint NewSeed()
        {
            return (uint)(System.DateTime.Now.Ticks & 0xFFFFFFFFu);
        }

        /// <summary>用新种子重开一局。由交互层（R 键 / 重开按钮 / 再来一局）调用。</summary>
        internal void RestartGame()
        {
            NewGame(NewSeed());
        }

        private void NewGame(uint seed)
        {
            StopAllCoroutines();
            _seed = seed;
            _busy = false;
            _interaction.Error = "";
            _interaction.ClearSelection();

            // 倒计时整体复位（[STAGE2-TASK-A]）：
            // 必须把 stamp 抹掉，否则"上一局恰好停在同一个 HalfTurn"会让读数不被重置。
            _timerHalfTurnStamp = int.MinValue;
            _timerRunning = false;
            _turnTimeLeft = TurnTimeLimitSeconds;

            _engine = new GameEngine(seed);
            _ai = new GreedyAI(_engine);
            _engine.StartMatch();

            // [阶段2.5 · 任务C-①] 伤害追踪与特效整体复位。
            // 不复位会有一个很难查的假象：旧局场上那些单位在新局里"不存在"，
            // 差值算法会把它们统统判成"这一次被打死了"，于是一重开就弹一片伤害数字。
            // 定义在 GamePresenter.Feedback.cs（反馈状态与它的复位放在一起）。
            ResetFeedbackTracking();
            if (_layout.Effects != null) _layout.Effects.ClearAll();

            if (_layout.GameOverRoot != null) _layout.GameOverRoot.SetActive(false);
            Refresh();
        }

        // ==================================================================
        // 刷新：把 GameState 画出来
        // ==================================================================

        internal void Refresh()
        {
            if (_engine == null) return;

            // ---- [阶段2.5 · 任务C] 战斗反馈的准备，三件都必须在 ClearSpawned() 之前 ----
            // ① 把"动作发生前"每张单位卡的中心点算成坐标存下来。
            //    必须现在做 —— 旧卡马上会被销毁，而"单位被打死"时飘字只能打在原来的位置上。
            // ② 比较前后两帧的血量，得出这一帧发生了哪些伤害。
            //    **不去推算规则**：引擎状态在动画开始前就已经改完了（硬约束 2「先结算、后播放」），
            //    所以"谁掉了多少血"是读结果，不是重算规则。
            // ③ 比较前后两帧的**手牌**，得出这一帧被打出的牌（指令卡就去播动画）。
            //    同样只读状态：引擎里唯一会从手牌拿牌的地方是 PlayCard，
            //    而抽牌/爆牌只会往手牌加 —— 所以"手牌少了"等价于"打出了一张"。
            // ④ 比较前后两帧的**单位战线 + 本回合是否已行动**，得出这一帧
            //    移上前线的单位（要播滑动）与发动攻击的单位（要播突刺）。
            CapturePreActionAnchors();
            CollectDamageEvents();
            CollectOrderPlays();
            CollectUnitActionEvents();

            _unitRects.Clear();
            _layout.ClearSpawned();

            GameState s = _engine.State;
            PlayerState me = s.Players[0];
            PlayerState foe = s.Players[1];

            UpdateHqWidget(_layout.EnemyHq, foe);
            UpdateHqWidget(_layout.PlayerHq, me);

            _layout.EnemyInfoText.text = "电脑手牌 " + foe.Hand.Count + " · 牌库 " + foe.Deck.Count;
            _layout.ResourceText.text = "资源 " + me.Resources + " / " + me.MaxResources;
            // [STAGE2C] 行动费改成从资源扣之后，资源读数变成了"还能做多少事"的唯一指示，
            // 所以只在自己回合高亮它（电脑回合灰掉降噪）。
            _layout.ResourceText.color = (!_busy && !s.IsOver && s.CurrentPlayer == 0) ? UiKit.Accent : UiKit.TextDim;

            _layout.ResourceSubText.text = "手牌 " + me.Hand.Count + " · 牌库 " + me.Deck.Count;

            // [FIX-3] 显示"轮"而不是"回合"：一轮 = 双方各行动一次。
            // 用 HalfTurn 直接显示会让玩家把"第 2 手"误读成自己的第二个回合。
            _layout.TurnValueText.text = s.IsOver ? "对局结束" : "第 " + s.RoundNumber + " 轮";
            if (s.IsOver)
            {
                _layout.TurnOwnerText.text = "—";
                _layout.TurnOwnerText.color = UiKit.TextDim;
            }
            else
            {
                bool myTurn = s.CurrentPlayer == 0;
                _layout.TurnOwnerText.text = myTurn ? "你的回合" : "电脑回合";
                _layout.TurnOwnerText.color = myTurn ? UiKit.LegalColor : UiKit.TargetColor;
            }
            UpdateSeedText();

            BuildEnemyHandBacks(foe);
            BuildSupportLineCards(_layout.EnemySupportRow, foe);
            BuildFrontlineCards(s);
            BuildSupportLineCards(_layout.PlayerSupportRow, me);
            BuildHandCards(me);
            UpdateFrontlineInfo(s);

            UpdateActionButtons();
            UpdateHint();
            UpdateLog(s);

            // [阶段2.5 · 任务C] 版面上所有单位卡都建完了，这时才知道
            // "哪些受伤的单位还活着（打在它现在的位置上）、哪些已经被打死（只能用旧位置）"，
            // 以及"哪张卡要从前线/支援线的旧位置滑过来""哪张卡要朝目标突刺"。
            //
            // ⚠️ 顺序有意义：**突刺必须排在伤害反馈之前**。
            //    飘字会给受伤的卡加抖动、抖动改的正是 anchoredPosition；
            //    突刺要读"目标现在在哪"，若排在抖动之后，读到的就是**被抖动推过**的位置 ——
            //    实测目标点会偏 6 设计像素、突刺多走 4.5%。
            //    排在前面时两者都读到干净的布局位置，且突刺的卡本身不会再被抖动
            //    （`PlayDamageFloats` 里对 `IsAttackingThisFrame` 的卡跳过抖动）。
            PlayAttackLunges();
            PlayDamageFloats();
            PlayMoveSlides();

            // [STAGE2-TASK-A] 界面刷新之后同步倒计时的运行与可见性。
            // 放最后是因为它只依赖"当前回合是谁"，跟上面的展示逻辑互不干扰。
            SyncTurnTimer();

            if (s.IsOver) ShowGameOver();
        }

        private static void UpdateHqWidget(GameLayout.HqWidget w, PlayerState p)
        {
            int hp = Mathf.Max(0, p.HqHealth);
            w.Value.text = hp.ToString();
            w.Sub.text = hp + " / " + GameEngine.HqStartingHealth;

            float ratio = Mathf.Clamp01((float)hp / GameEngine.HqStartingHealth);
            w.Value.color = ratio > 0.6f ? UiKit.TextMain : (ratio > 0.3f ? UiKit.AttackColor : UiKit.TargetColor);

            // 血条按比例改宽度。用整数像素，避免浮点宽度把贴图缓存撑爆。
            int width = Mathf.Max(3, Mathf.RoundToInt(282f * ratio));
            w.BarFill.rectTransform.sizeDelta = new Vector2(width, 10f);
            // 满血用绿色、半血橙色、危险红色 —— 光看颜色就知道还剩多少余地
            w.BarFill.color = ratio > 0.6f ? UiKit.LegalColor
                            : (ratio > 0.3f ? UiKit.AttackColor : UiKit.TargetColor);
            w.BarFill.sprite = UiKit.Rounded(width, 10, 5);
        }

        private void BuildEnemyHandBacks(PlayerState foe)
        {
            const float cardW = 34f;
            const float cardH = 48f;
            const float spacing = 38f;

            int n = foe.Hand.Count;
            float total = (n - 1) * spacing;
            for (int i = 0; i < n; i++)
            {
                Vector2 pos = new Vector2(-total * 0.5f + i * spacing, 0f);
                CardView v = CardView.CreateCardBack(_layout.EnemyHandBacks, pos, new Vector2(cardW, cardH));
                _layout.Track(v.Rect.gameObject);
            }
        }

        /// <summary>
        /// [STAGE2B] 画一条支援阵线。两条支援阵线（电脑 / 我方）的绘制逻辑完全一样，
        /// 区别只在传进来的 PlayerState —— 所以共用一个函数。
        ///
        /// [STAGE2C] 这条线上第 0 格永远是 **HQ**（它现在是一个真实单位，占一格容量）。
        /// 它的点击语义与普通单位不同，所以在这里分派：
        ///   · 己方 HQ → 等同"点支援阵线空白"（把选中的单位部署下去）
        ///   · 敌方 HQ → 交给单位点击处理器（选中攻击单位时它就是"打脸"的目标）
        /// 混用同一个处理器的话，"点自己的 HQ"会被当成"选它当攻击者"。
        /// </summary>
        private void BuildSupportLineCards(RectTransform row, PlayerState owner)
        {
            const float spacing = 132f;
            int n = owner.SupportLine.Count;
            float total = (n - 1) * spacing;

            // 只在"轮到这一方行动"时，才显示部署失调 / 已行动 —— 否则标记对玩家没有意义
            bool showStatus = !_engine.State.IsOver && owner.Index == _engine.State.CurrentPlayer;

            for (int i = 0; i < n; i++)
            {
                UnitInstance u = owner.SupportLine[i];
                Vector2 pos = new Vector2(-total * 0.5f + i * spacing, 0f);

                System.Action<CardView> onClick = u.IsHq
                    ? (owner.Index == 0 ? _interaction.OnOwnHqClicked : _interaction.OnBoardUnitClicked)
                    : _interaction.OnBoardUnitClicked;

                CardView v = CardView.CreateBoardUnit(row, pos, u,
                    selected: !u.IsHq && _interaction.SelUnitId == u.InstanceId,
                    targetable: _interaction.IsTargetable(u),
                    attackable: _interaction.IsAttackable(u),
                    showStatus: showStatus,
                    onClick: onClick);

                // [任务C-①] 记下这张卡，伤害飘字要按它在版面中的实际位置来落点
                _unitRects[u.InstanceId] = v.Rect;
                _layout.Track(v.Rect.gameObject);
            }
        }

        /// <summary>
        /// [STAGE2B] 画前线。前线是一个**共享**列表（谁占着就画谁的单位），
        /// 所以它不像支援阵线那样按 owner 取，而是直接读 GameState.Frontline。
        /// </summary>
        private void BuildFrontlineCards(GameState s)
        {
            const float spacing = 132f;
            int n = s.Frontline.Count;
            float total = (n - 1) * spacing;

            bool showStatus = !s.IsOver && s.FrontlineOwner == s.CurrentPlayer;

            for (int i = 0; i < n; i++)
            {
                UnitInstance u = s.Frontline[i];
                Vector2 pos = new Vector2(-total * 0.5f + i * spacing, 0f);

                CardView v = CardView.CreateBoardUnit(_layout.FrontlineRow, pos, u,
                    selected: _interaction.SelUnitId == u.InstanceId,
                    targetable: _interaction.IsTargetable(u),
                    attackable: _interaction.IsAttackable(u),
                    showStatus: showStatus,
                    onClick: _interaction.OnBoardUnitClicked);

                // [任务C-①] 同支援阵线：飘字要靠这个位置落点
                _unitRects[u.InstanceId] = v.Rect;
                _layout.Track(v.Rect.gameObject);
            }
        }

        /// <summary>
        /// [STAGE2B] 把"前线现在归谁"直接写在这一行的标题与描边上。
        /// 三行摆在一起时，"我的单位在前线"和"我的单位在支援阵线"必须是**一眼可辨**的差别 ——
        /// 这恰恰是三线机制对玩家提出的新认知负担，界面有义务把它降到最低。
        /// </summary>
        private void UpdateFrontlineInfo(GameState s)
        {
            int owner = s.FrontlineOwner;
            if (owner < 0)
            {
                _layout.FrontlineInfoText.text = "前线（无人占领 · 上限 " + GameEngine.FrontlineCapacity + "）";
                _layout.FrontlineInfoText.color = UiKit.TextDim;
                _layout.FrontlineFrame.color = new Color32(48, 68, 54, 255);
            }
            else if (owner == 0)
            {
                _layout.FrontlineInfoText.text = "前线（你占领中）";
                _layout.FrontlineInfoText.color = UiKit.LegalColor;
                _layout.FrontlineFrame.color = UiKit.LegalColor;
            }
            else
            {
                _layout.FrontlineInfoText.text = "前线（电脑占领中）";
                _layout.FrontlineInfoText.color = UiKit.TargetColor;
                _layout.FrontlineFrame.color = UiKit.TargetColor;
            }
        }

        private void BuildHandCards(PlayerState me)
        {
            const float baseSpacing = 152f;
            const float maxRowWidth = 1180f;
            int n = me.Hand.Count;
            if (n == 0) return;

            // 手牌多于 8 张时自动收紧间距，保证整排不超出预留宽度
            float spacing = baseSpacing;
            float needed = (n - 1) * spacing + CardView.HandCardSize.x;
            if (n > 1 && needed > maxRowWidth)
            {
                spacing = (maxRowWidth - CardView.HandCardSize.x) / (n - 1);
            }

            string reason;
            float total = (n - 1) * spacing;

            for (int i = 0; i < n; i++)
            {
                CardDef def = me.Hand[i];
                bool playable = _interaction.CanInteract() && _engine.CanPlayCard(i, out reason);
                bool selected = _interaction.SelHand == i;
                Vector2 pos = new Vector2(-total * 0.5f + i * spacing, selected ? 22f : 0f);

                CardView v = CardView.CreateHandCard(_layout.HandRow, pos, i, def, playable, selected,
                                                     _interaction.OnHandCardClicked);
                _layout.Track(v.Rect.gameObject);
            }
        }

        private void UpdateActionButtons()
        {
            bool myTurn = _interaction.CanInteract();

            // 结束回合
            _layout.EndTurnButton.interactable = myTurn;
            Color endFrame = myTurn ? UiKit.Accent : UiKit.PanelBorder;
            _layout.EndTurnFrame.color = endFrame;
            _layout.EndTurnLabel.color = myTurn ? UiKit.TextMain : UiKit.TextDim;

            // 打出指令按钮：只在"选中了一张不需要目标的指令卡"时出现
            bool show = false;
            CardDef card = _interaction.SelectedHandCard();
            if (myTurn && card != null && card.IsOrder && !GameEngine.RequiresUnitTarget(card))
            {
                show = true;
                _layout.PlayOrderFrame.color = UiKit.LegalColor;
            }
            _layout.PlayOrderRoot.SetActive(show);
        }

        private void UpdateHint()
        {
            _layout.HintText.color = UiKit.TextMain;

            if (_engine.State.IsOver)
            {
                _layout.HintText.text = "对局已结束 —— 点「再来一局」或按 R 重开";
                return;
            }
            if (_busy)
            {
                _layout.HintText.text = "电脑正在行动…";
                return;
            }
            if (_interaction.Error.Length > 0)
            {
                _layout.HintText.text = "✕ " + _interaction.Error;
                _layout.HintText.color = UiKit.TargetColor;
                return;
            }

            CardDef card = _interaction.SelectedHandCard();
            if (card != null)
            {
                if (GameEngine.RequiresUnitTarget(card))
                {
                    _layout.HintText.text = "【" + card.Name + "】：点击一个敌方单位作为目标（指令卡不受战线限制）";
                }
                else if (card.IsUnit)
                {
                    // [阶段2.5-TASK-B] 顺带交代它的兵种与"上场后动一次要花几点" ——
                    // 卡面左上角那枚小数字就是它，这里用文字再说一遍，省得玩家自己去找。
                    _layout.HintText.text = "【" + card.Name + " · " + CardArt.FullName(card.UnitKind) +
                                            "】：点击下方「你 · 支援阵线」部署（上场后每次行动花 " +
                                            GameEngine.ActionCostOf(card) + " 点资源）";
                }
                else
                {
                    _layout.HintText.text = "【" + card.Name + "】：点击下方绿色按钮确认打出";
                }
                return;
            }

            if (_interaction.SelUnitId >= 0)
            {
                UnitInstance u = _engine.FindUnit(_interaction.SelUnitId);
                if (u == null)
                {
                    _layout.HintText.text = "选择的单位已不在场上";
                    return;
                }

                // [STAGE2B] 一个单位能做的事取决于它站在哪条线上：
                //   支援阵线 → 可以移上前线；够得着的只有敌方前线单位
                //   前线     → 不能再移动；够得着敌方任意单位与 HQ
                // 这里把两种可能都说清楚，避免玩家反复试点。
                string reason;
                bool canMove = _engine.CanUnitMove(u, out reason);
                bool canAttack = _engine.HasAttackTarget(u);

                // [阶段2.5-TASK-B] 选中的单位要能一眼看到"它属于哪个兵种"与"这次动它要花几点资源"
                //   —— 前者对应卡面开窗的插画与底部徽章，后者对应左上角那枚小数字。
                string kindText = u.Def.UnitKind == UnitType.None ? "" : " · " + CardArt.FullName(u.Def.UnitKind);
                string head = "【" + u.Def.Name + kindText + "】（" +
                              (u.Line == Line.Front ? "前线" : "支援阵线") + "）";
                string costText = "，本次行动需要 " + GameEngine.ActionCostOf(u.Def) + " 点资源";

                if (canMove && canAttack)
                    _layout.HintText.text = head + "：点「前线」移入，或点击敌方目标 / 敌方 HQ" + costText;
                else if (canMove)
                    _layout.HintText.text = head + "：点击中央的「前线」把它移上去" + costText;
                else if (canAttack)
                    _layout.HintText.text = head + "：点击敌方单位交换，或点击敌方 HQ 打脸" + costText;
                else
                    _layout.HintText.text = head + "：" + reason;
                return;
            }

            _layout.HintText.text = "点手牌 → 部署到「支援阵线」　｜　点己方单位 → 点「前线」移入 或 点敌方目标攻击";
        }

        private void UpdateLog(GameState s)
        {
            const int maxLines = 13;
            int from = Mathf.Max(0, s.Log.Count - maxLines);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = from; i < s.Log.Count; i++)
            {
                if (i > from) sb.Append('\n');
                sb.Append(s.Log[i]);
            }
            _layout.LogText.text = sb.ToString();
        }

        private void ShowGameOver()
        {
            if (_layout.GameOverRoot.activeSelf) return;

            MatchResult r = _engine.State.Result;
            if (r == MatchResult.Player0Win)
            {
                _layout.GameOverTitle.text = "胜  利";
                _layout.GameOverTitle.color = UiKit.WinColor;
                _layout.GameOverFrame.color = UiKit.WinColor;
            }
            else if (r == MatchResult.Player1Win)
            {
                _layout.GameOverTitle.text = "失  败";
                _layout.GameOverTitle.color = UiKit.LoseColor;
                _layout.GameOverFrame.color = UiKit.LoseColor;
            }
            else
            {
                _layout.GameOverTitle.text = "平  局";
                _layout.GameOverTitle.color = UiKit.Accent;
                _layout.GameOverFrame.color = UiKit.Accent;
            }

            _layout.GameOverSub.text = _engine.State.ResultReason + "\n本局随机种子：" + _seed;
            _layout.GameOverRoot.SetActive(true);
        }

        // ==================================================================
        // 电脑回合：一步一步播出来，而不是一帧跑完
        // ==================================================================

        /// <summary>结束人类回合并把控制权交给电脑。结束回合按钮与倒计时超时共用这一条路径。</summary>
        internal void EndTurnAndRunAi()
        {
            _engine.EndTurn();
            Refresh();

            if (_engine.State.IsOver) return;
            StartCoroutine(RunAiTurn());
        }

        private IEnumerator RunAiTurn()
        {
            _busy = true;
            _interaction.ClearSelection();
            Refresh();

            yield return new WaitForSeconds(0.5f);

            // StepTurn 是 GreedyAI 暴露的逐动作序列。这里只负责"播"，
            // 决策逻辑和 headless 批量模拟用的是同一份代码。
            foreach (string step in _ai.StepTurn())
            {
                Refresh();
                if (_engine.State.IsOver) break;
                yield return new WaitForSeconds(0.4f);
            }

            _busy = false;
            _interaction.Error = "";
            Refresh();
        }

        // ==================================================================
        // 回合倒计时（阶段 2 · 任务 A）
        //
        // 全部逻辑都待在 View 层，Core 里没有任何时间概念（见文件顶部
        // TurnTimeLimitSeconds 的注释：那是编译期就锁死的硬约束，不是自觉）。
        //
        // AI 回合完全不显示、不计时 —— 这既是规则要求，也是"AI 动作序列不可被打断"
        // 的保证：只要计时器在 AI 回合停下，StepTurn() 的每一步就不会依赖真实时钟，
        // "同种子结果一致"这条契约才不会破。
        // ==================================================================

        /// <summary>
        /// 把计时器的运行 / 隐藏状态同步到当前回合。每次 Refresh 都会调用，
        /// 但**只有 HalfTurn 变化时才会重置读数** —— 所以玩家出牌引起的刷新不会重置倒计时。
        /// </summary>
        private void SyncTurnTimer()
        {
            if (_layout.TimerRoot == null || _engine == null) return;

            bool myTurn = !_busy
                          && !_engine.State.IsOver
                          && _engine.State.CurrentPlayer == GameEngine.HumanPlayerIndex;
            bool shouldRun = _turnTimerEnabled && myTurn;

            if (shouldRun && _timerHalfTurnStamp != _engine.State.HalfTurn)
            {
                // 新的人类回合开始 → 读数归满
                _timerHalfTurnStamp = _engine.State.HalfTurn;
                _turnTimeLeft = TurnTimeLimitSeconds;
            }

            _timerRunning = shouldRun;
            _layout.TimerRoot.SetActive(shouldRun);
            if (shouldRun) UpdateTimerVisual();
        }

        /// <summary>走表。只在计时器处于运行状态时执行，而它只在人类回合运行。</summary>
        private void TickTurnTimer(float deltaTime)
        {
            if (!_timerRunning) return;

            _turnTimeLeft -= deltaTime;
            if (_turnTimeLeft <= 0f)
            {
                _turnTimeLeft = 0f;
                UpdateTimerVisual();
                OnTurnTimerExpired();
                return;
            }
            UpdateTimerVisual();
        }

        /// <summary>刷新倒计时的数字与圆环；剩 10 秒内转红并做呼吸强调。</summary>
        private void UpdateTimerVisual()
        {
            if (_layout.TimerRingFill == null) return;

            int seconds = Mathf.CeilToInt(Mathf.Max(0f, _turnTimeLeft));
            bool warn = seconds <= TimerWarnSeconds;

            _layout.TimerValue.text = seconds.ToString();
            _layout.TimerRingFill.fillAmount = Mathf.Clamp01(_turnTimeLeft / TurnTimeLimitSeconds);
            _layout.TimerRingFill.color = warn ? UiKit.TargetColor : UiKit.LegalColor;

            // 强调：最后一段把字号顶上去并做呼吸闪烁，余光就能注意到
            _layout.TimerValue.color = warn ? UiKit.TargetColor : UiKit.TextMain;
            _layout.TimerValue.fontSize = warn ? 40f : 34f;
            _layout.TimerValue.alpha = warn ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f)) : 1f;
        }

        /// <summary>
        /// 归零处理。执行顺序由《阶段2-设计要点与启动提示词.md》§3.2 钉死，不能调换：
        ///   ① 停表 → ② 清空选择 → ③ EndTurn → ④ 刷新 → ⑤ 未结束则启动 AI 回合
        ///
        /// 第 ② 步不能省：否则玩家当时若正选着一张需要目标的指令卡，选择状态会
        /// 一直残留到"AI 回合打完、重新轮到自己"之后，界面上就会出现一个本不该存在的选中状态。
        /// </summary>
        private void OnTurnTimerExpired()
        {
            if (!_timerRunning) return;   // 防重入：本方法自己会再触发一次 Refresh

            // ① 停表
            _timerRunning = false;

            // ② 清空选择（三个字段都要清）
            _interaction.ClearSelection();
            _interaction.Error = "";

            // ③ 移交控制权。人类回合内必定成功 —— 这是引擎的回合归属守卫说了算，不是 UI 说了算。
            _engine.EndTurn();

            // 给玩家一个可见的交代。放在清空之后设置，所以不算"残留的选择状态"；
            // AI 回合结束时它会被自然清掉（RunAiTurn 末尾的 _error = ""）。
            _interaction.Error = "回合超时：已自动结束你的回合";
            Debug.Log("[TurnTimer] 人类回合超时，自动 EndTurn（HalfTurn=" + _engine.State.HalfTurn + "）");

            // ④ 刷新（这一步会把计时器隐藏，因为行动方已经不是人类了）
            Refresh();

            // ⑤ 对局未结束 → 播放 AI 回合
            if (_engine.State.IsOver) return;
            StartCoroutine(RunAiTurn());
        }

        /// <summary>
        /// 调试开关（T 键）：一键开关倒计时。
        /// 关掉后本回合立刻停表并隐藏；重新打开时给当前人类回合一个完整的 60 秒。
        /// </summary>
        private void ToggleTurnTimer()
        {
            _turnTimerEnabled = !_turnTimerEnabled;
            Debug.Log("[TurnTimer] 倒计时已" + (_turnTimerEnabled ? "启用" : "关闭"));

            if (_turnTimerEnabled)
            {
                // 抹掉 stamp，让 SyncTurnTimer 重新给满值 —— 否则会沿用"关掉那一刻剩下的秒数"
                _timerHalfTurnStamp = int.MinValue;
            }
            SyncTurnTimer();
        }
    }
}
