using System.Collections;
using System.Collections.Generic;
using KardsLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    /// <summary>
    /// 战斗反馈特效层（阶段 2.5 · 任务 C 步骤 ①「双向伤害飘字」）。
    ///
    /// 它是一个挂在「特效层」空节点上的 MonoBehaviour，独立于 GamePresenter：
    ///   · 卡牌每帧都会被整批销毁重建（GamePresenter.Refresh 的全量重建策略），
    ///     所以飘字**绝不能**挂在卡上 —— 它会连同卡一起被销毁。
    ///     这里把它挂在**一个不被销毁的层**上，卡怎么重建都不影响它。
    ///   · 协程跑在本组件上而不是 GamePresenter 上，是为了让 RestartGame 的
    ///     `StopAllCoroutines()`（停主控自己的协程）不要顺手把特效也停掉 ——
    ///     重开时改由 ClearAll() 显式清场。
    ///
    /// ⚠️ 三条硬约束（阶段 2.5 文档 §6 提示词 4）：
    ///   1. 动画全部在 View 层用协程 + Time.deltaTime —— Core 里没有任何时间概念
    ///   2. **先结算、后播放**：本类只负责"演"，它连 GameState 都不读。
    ///      伤害数字是调用方（GamePresenter）传进来的，绝不在动画里推算规则。
    ///   3. 有「动画时长倍率」，**含 0 = 立即完成**（此时一律不创建特效对象）
    /// </summary>
    public sealed class CardEffectLayer : MonoBehaviour
    {
        // ---- 时长与幅度（都是"倍率 1.0"下的基准值）----
        /// <summary>伤害数字的总时长。与 AI 步进间隔（0.4s）重叠播放，不额外增加等待。</summary>
        public const float FloatBaseDuration = 0.6f;
        /// <summary>卡牌抖动时长。</summary>
        public const float ShakeBaseDuration = 0.15f;
        /// <summary>[任务C-③] 单位移动（支援线 → 前线）的滑动时长。</summary>
        public const float MoveBaseDuration = 0.25f;
        /// <summary>[任务C-④] 攻击"突刺"的总时长（去+回各一半）。</summary>
        public const float AttackBaseDuration = 0.2f;
        /// <summary>突刺时最多走完"到目标"的百分之多少 —— 不走满，否则两张卡会叠在一起。</summary>
        public const float LungeApproach = 0.35f;

        // ---- [任务C-④·兵种细化] 步兵：短突刺 + 命中处小火花 ----
        /// <summary>
        /// 步兵的突刺**更短**：捅一下，而不是像坦克那样整体推进。
        /// 与默认的 `LungeApproach` 只差一个数字，但那一瞬间的"手感"完全不同。
        /// </summary>
        public const float InfantryLungeApproach = 0.22f;
        /// <summary>步兵的突刺也更短促（0.16s 对默认 0.2s）。</summary>
        public const float InfantryAttackDuration = 0.16f;
        /// <summary>小火花的总时长。</summary>
        private const float SparkDuration = 0.25f;
        /// <summary>小火花：放射条数量。</summary>
        private const int SparkBarCount = 6;
        /// <summary>小火花：中心亮点的边长（圆角取一半 ⇒ 正圆）。</summary>
        private const float SparkCoreSize = 16f;
        // 下面三个数一起决定"看起来是不是火花"。第一版取的是 宽5/长24/半径17，
        // 结果六条首尾相接围成一个**闭合六边形**，加上 26 的圆心把里面填满 ——
        // 放大图一看像一枚"六边形勋章"，完全不是"炸开的火星"。
        // 第二版（宽4/长18/半径25）分开了，但条太短太粗，像六块"橙色小方片"。
        // 现在定成 宽3/长22/半径26：细而长、内圈留空洞、相邻两条之间空出约 24 像素 ——
        // 这才读成"向外炸开的火星"。
        private const float SparkBarWidth = 3f;
        private const float SparkBarLength = 22f;
        /// <summary>小火花：放射条中心离冲击点的距离（动画中还会再往外扩）。</summary>
        private const float SparkBarRadius = 26f;
        /// <summary>
        /// 冲击点从"目标卡中心"朝攻击者方向挪多少（设计像素）。
        /// 卡宽 112 ⇒ 半宽 56，取 34 仍落在目标卡面上（不会飘到卡外去）；
        /// 不挪的话火花正对卡心，会与伤害数字的起始位置叠在一起。
        /// </summary>
        private const float SparkInset = 34f;
        /// <summary>小火花配色（暖白 + 橙黄）。刻意与伤害数字的亮红拉开，两者叠在一起时也分得清。</summary>
        private static readonly Color SparkCoreColor = new Color32(255, 242, 205, 255);
        private static readonly Color SparkBarColor = new Color32(255, 198, 112, 255);
        /// <summary>伤害数字上飘的距离（设计画框单位）。</summary>
        public const float FloatRise = 56f;
        /// <summary>抖动幅度（设计画框单位）。</summary>
        public const float ShakeAmplitude = 6f;

        private const float FloatFontSize = 46f;
        private const float FloatWidth = 150f;
        private const float FloatHeight = 64f;

        // ---- [任务C-②] 指令卡打出动画的几何与时长 ----
        /// <summary>起始点：手牌行的中心。`HandRow` 定位是 (0, 32)、高 208 ⇒ 中心 y = 32 + 104。</summary>
        public const float OrderFromY = 136f;
        /// <summary>展示点：牌桌**中线偏左**（牌桌纵向中心 ≈ 124；再往左挪开中心区）。</summary>
        public static readonly Vector2 OrderShowPos = new Vector2(-150f, 120f);
        /// <summary>滑出屏幕左侧的终点 x（设计画框宽 1920，左边界在 −960）。</summary>
        public const float OrderExitX = -1520f;
        /// <summary>放大倍数（手牌 148×208 ⇒ 展示尺寸 237×333）。</summary>
        public const float OrderCardScale = 1.6f;

        private const float OrderFadeIn = 0.12f;
        private const float OrderMove = 0.25f;
        /// <summary>中线偏左的停留时长 —— 提示词给定「停留约 1.0s」。</summary>
        private const float OrderHold = 1.0f;
        private const float OrderExit = 0.30f;

        private float _speed = 1f;

        // 指令卡动画是**全场唯一实例**（阶段 2.5 文档 §2.3 的明确口径）：
        // 正在播时后续的打出先排队，一张一张来，绝不叠加成两张同时飞。
        private readonly Queue<CardDef> _orderQueue = new Queue<CardDef>();
        private Coroutine _orderRoutine;

        /// <summary>
        /// 动画时长倍率。1 = 原速；0.5 / 0.25 = 放慢；**0 = 关闭动画（立即完成）**。
        /// 由 GamePresenter 统一持有并下发，这里只负责用。
        /// </summary>
        public float Speed
        {
            get { return _speed; }
            set { _speed = value < 0f ? 0f : value; }
        }

        /// <summary>当前是否真的会播放动画（倍率为 0 时为 false）。</summary>
        public bool Enabled { get { return _speed > 0f; } }

        // ==================================================================
        // 对外接口
        // ==================================================================

        /// <summary>
        /// 在指定的**本层局部坐标**处弹一个伤害数字。
        /// 传坐标而不是传 RectTransform，是因为"单位被打死"时那张卡已经不在了，
        /// 调用方需要在卡被销毁**之前**把位置算成坐标存下来。
        /// </summary>
        public void DamageAt(Vector2 localPos, int amount)
        {
            if (!Enabled) return;
            StartCoroutine(PlayFloat(localPos, amount, FloatBaseDuration / _speed));
        }

        /// <summary>让一张卡抖一下。传 null（卡已被销毁）时静默跳过。</summary>
        public void Shake(RectTransform card)
        {
            if (!Enabled || card == null) return;
            StartCoroutine(PlayShake(card, ShakeBaseDuration / _speed));
        }

        /// <summary>
        /// [任务C-②] 播放一次「指令卡打出」动画。倍率为 0 时直接不播（立即完成）。
        ///
        /// 手牌位置放大出现 → 移到牌桌中线偏左 → 停留约 1.0s → 向左滑出屏幕。
        /// **不论谁打出、打出哪张指令，动画完全一致** —— 所以这里不接收"归属"参数，
        /// 唯一的输入就是那张 `CardDef`。
        /// </summary>
        public void ShowOrderCard(CardDef def)
        {
            if (!Enabled || def == null) return;

            _orderQueue.Enqueue(def);
            if (_orderRoutine == null) _orderRoutine = StartCoroutine(RunOrderQueue());
        }

        /// <summary>
        /// [任务C-③] 让一张卡从"它原来所在的位置"平滑滑到它**现在的布局位置**。
        ///
        /// 为什么传世界坐标而不是"旧节点"：卡每次 `Refresh()` 都被整批重建，
        /// 旧节点到这一步已经被销毁了 —— 只有**它当时的世界坐标**能留到重建之后用。
        /// 新卡现在已经在正确的新位置上（布局算好的），这里先把它挪回起点再插值滑回去。
        /// </summary>
        public void SlideFrom(RectTransform card, Vector3 fromWorld)
        {
            if (!Enabled || card == null) return;
            StartCoroutine(PlaySlide(card, fromWorld, MoveBaseDuration / _speed));
        }

        /// <summary>
        /// [任务C-④] 攻击特效：先做统一版（五个兵种共用"朝目标冲一段再弹回"），
        /// 再按 `kind` 逐兵种细化 —— **没被细化过的兵种自动落到统一版**，
        /// 所以每轮只加一个兵种、其余的行不为所动（这就是把统一版留作兜底的意义）。
        ///
        /// 参数给的是**目标的世界坐标**，因为目标可能已经被打死、节点不在了。
        /// `kind` 由调用方从引擎状态读出后传进来 —— 本类仍然**不读规则**，
        /// 它只是把"哪一类兵种"当成一个外观参数用。
        /// </summary>
        public void AttackEffect(RectTransform card, Vector3 targetWorld, UnitType kind)
        {
            if (!Enabled || card == null) return;

            // 冲击点要赶在 StartCoroutine 之前算：协程会在同一帧就把卡挪走，
            // 之后再读 card 的位置就不是"出击前"的落点了。
            Vector2 attackerLocal = LocalOf(card);
            Vector2 targetLocal = LocalOf(targetWorld);

            StartCoroutine(PlayLunge(card, targetWorld, AttackDurationOf(kind) / _speed, ApproachOf(kind)));

            if (kind == UnitType.Infantry)
                SpawnSpark(ImpactPoint(targetLocal, attackerLocal));
        }

        /// <summary>该兵种的突刺深度。**未细化的兵种一律用统一版的 0.35。**</summary>
        public static float ApproachOf(UnitType kind)
        {
            if (kind == UnitType.Infantry) return InfantryLungeApproach;
            return LungeApproach;
        }

        /// <summary>该兵种的突刺时长。**未细化的兵种一律用统一版的 0.2s。**</summary>
        public static float AttackDurationOf(UnitType kind)
        {
            if (kind == UnitType.Infantry) return InfantryAttackDuration;
            return AttackBaseDuration;
        }

        /// <summary>
        /// 把一个 RectTransform 的中心点换算成本层的局部坐标。
        /// **必须在目标还活着的时候调** —— 卡被销毁后算出来的位置不可靠。
        /// </summary>
        public Vector2 LocalOf(RectTransform target)
        {
            if (target == null) return Vector2.zero;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[2]) * 0.5f;
            return LocalOf(center);
        }

        /// <summary>世界坐标 → 本层局部坐标（本层铺满 DesignFrame 且轴心在正中，
        /// 所以算出来就等于"设计画框坐标"）。</summary>
        public Vector2 LocalOf(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        /// <summary>停掉所有动画并销毁残留的特效对象（重开一局时调用）。</summary>
        public void ClearAll()
        {
            StopAllCoroutines();
            _orderQueue.Clear();
            _orderRoutine = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == null) continue;
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        // ==================================================================
        // 动画
        // ==================================================================

        /// <summary>
        /// 一个伤害数字的生命周期：上飘 + 由大收小 + 后半段淡出，结束后自毁。
        ///
        /// 视觉上刻意做得"重"：深色描边垫底 + 亮红主字 + 46px 粗体。
        /// 因为卡框本身就是深灰石纹 + 金边，细字在这上面完全读不出来
        /// （任务 B 效果文字那次踩过的坑）。
        /// </summary>
        private IEnumerator PlayFloat(Vector2 start, int amount, float duration)
        {
            RectTransform rt = UiKit.Anchored("DamageFloat", transform,
                                              new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                              start, new Vector2(FloatWidth, FloatHeight));

            string text = amount.ToString();
            Color shadowColor = new Color32(8, 10, 14, 235);

            // 描边用"同尺寸的第二段文字垫在偏右下"来做，不走 TMP 的 outline：
            // outline 需要在运行期生成材质实例，这里两段文字更省事也更好预测。
            RectTransform shadowRt = UiKit.Anchored("Shadow", rt,
                                                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                    new Vector2(3f, -3f), new Vector2(FloatWidth, FloatHeight));
            TextMeshProUGUI shadow = UiKit.AddLabel(shadowRt, text, FloatFontSize, shadowColor,
                                                    TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform textRt = UiKit.Anchored("Value", rt,
                                                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(FloatWidth, FloatHeight));
            TextMeshProUGUI value = UiKit.AddLabel(textRt, text, FloatFontSize, UiKit.TargetColor,
                                                   TextAlignmentOptions.Center, FontStyles.Bold);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / duration);

                // 上飘：先快后慢（ease-out quad），"弹出去"的手感
                float rise = FloatRise * (1f - (1f - k) * (1f - k));
                rt.anchoredPosition = start + new Vector2(0f, rise);

                // 前 25% 由 1.35 收到 1.0 —— 出现的那一下要"顶"出来
                float punch = 1.35f - 0.35f * Mathf.Clamp01(k / 0.25f);
                rt.localScale = new Vector3(punch, punch, 1f);

                // 后 60% 淡出
                float alpha = 1f - Mathf.Clamp01((k - 0.4f) / 0.6f);
                Color c = value.color; c.a = alpha; value.color = c;
                c = shadow.color; c.a = alpha * 0.92f; shadow.color = c;

                yield return null;
            }

            if (rt != null) Destroy(rt.gameObject);
        }

        /// <summary>
        /// 抖动:水平方向来回摆，幅度随时间线性收窄，结束后**精确还原**原始位置。
        /// 还原这一步不能省 —— 位置是布局给的，抖完不回位就等于改了布局。
        ///
        /// 用 `cos` 而不是 `sin`：`sin(0) = 0`，抖动会从"零位移"慢慢涨起来，
        /// 第一帧看不出来；`cos(0) = 1` 则是**起手就是最大位移**，
        /// 更接近"挨了一下"的冲击感，也才能被"读第一帧"这种测试直接验到。
        /// </summary>
        private IEnumerator PlayShake(RectTransform card, float duration)
        {
            Vector2 basePos = card.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (card == null) yield break;   // 卡被重建/销毁了，立刻退出

                float decay = 1f - Mathf.Clamp01(elapsed / duration);
                float dx = Mathf.Cos(elapsed * 95f) * ShakeAmplitude * decay;
                card.anchoredPosition = basePos + new Vector2(dx, 0f);
                yield return null;
            }

            if (card != null) card.anchoredPosition = basePos;
        }

        /// <summary>
        /// 滑动：把新卡从"旧的世界坐标"平滑移到它的布局位置（`SmoothStep`，两端都软）。
        ///
        /// 两个坐标系必须换算：旧位置在**旧行**里、目标位置在**新行**里（支援线 → 前线换了父节点），
        /// 所以先把旧的世界坐标用 `parent.InverseTransformPoint` 换算成新父节点下的局部坐标。
        /// 卡节点的轴心与锚点都是 (0.5,0.5) ⇒ 换算结果正好就是 `anchoredPosition`。
        ///
        /// ⚠️ **注意 `StartCoroutine` 会在同一帧里立刻跑到第一个 `yield`** ——
        /// 也就是说"设好起点"和"走出第一帧"发生在**同一帧**，
        /// 外部在启动后立刻读 `anchoredPosition`，读到的是**已经推进约 1.55%** 的值
        /// （`SmoothStep(0.02 / 0.25) ≈ 1/55`），不是起点本身。
        /// 这一点曾经把我误导成"换算有 1.8% 的缩放误差"——**其实是这一帧的进度**。
        /// 要读真正的起点，得在调用 `SlideFrom` 之后、任何 `Step()` 之前读。
        /// </summary>
        private IEnumerator PlaySlide(RectTransform card, Vector3 fromWorld, float duration)
        {
            if (duration <= 0f) yield break;

            Transform parent = card.parent;
            if (parent == null) yield break;

            Vector2 to = card.anchoredPosition;            // 布局已经把它放在终点了，先记下来
            Vector3 localFrom = parent.InverseTransformPoint(fromWorld);
            Vector2 from = new Vector2(localFrom.x, localFrom.y);

            // 立刻挪回起点 —— 不这么做的话会先闪一帧在终点上
            card.anchoredPosition = from;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (card == null) yield break;             // 中途被重建了就算了
                card.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            if (card != null) card.anchoredPosition = to;
        }

        /// <summary>
        /// 突刺：朝目标走一段（`approach` × 到目标的距离）再原路弹回，**结束时精确还原**。
        ///
        /// `approach` 由调用方按兵种给出（见 `ApproachOf`）—— 步兵捅得短、统一版推得远，
        /// 差别只在"走多远"这一个数上，**去/回的节奏完全共用**，所以细化工种不会
        /// 各写一套动画（那才是五份要分别维护的真值）。
        ///
        /// 两个自制的硬规矩：
        ///   · 只走一部分，**不能真的走到目标身上** —— 否则两张卡会重叠成一坨
        ///   · 去与回各占一半时长，都用 `SmoothStep`，收尾都软 ⇒ 像"刺一下"而不是"平移"。
        ///     回程结束时必须回到 `home`：位置是布局给的，不还原就等于改了布局。
        /// </summary>
        private IEnumerator PlayLunge(RectTransform card, Vector3 targetWorld, float duration, float approach)
        {
            if (duration <= 0f) yield break;

            Transform parent = card.parent;
            if (parent == null) yield break;

            Vector2 home = card.anchoredPosition;
            Vector3 localTarget = parent.InverseTransformPoint(targetWorld);
            Vector2 lungeTo = Vector2.Lerp(home, new Vector2(localTarget.x, localTarget.y), approach);

            float half = duration * 0.5f;
            float t = 0f;
            while (t < half)          // 去
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                card.anchoredPosition = Vector2.Lerp(home, lungeTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / half)));
                yield return null;
            }

            t = 0f;
            while (t < half)          // 回
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                card.anchoredPosition = Vector2.Lerp(lungeTo, home, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / half)));
                yield return null;
            }

            if (card != null) card.anchoredPosition = home;
        }

        // ==================================================================
        // [任务C-④·兵种细化] 步兵的命中火花
        // ==================================================================

        /// <summary>在命中处炸一小簇火花。倍率 0 时调用方不会走到这里，这里再挡一道。</summary>
        private void SpawnSpark(Vector2 at)
        {
            if (!Enabled) return;
            StartCoroutine(PlaySpark(at, SparkDuration / _speed));
        }

        /// <summary>
        /// 冲击点：目标卡中心朝攻击者方向挪 `SparkInset`（设计像素）。
        /// 按"占两者距离的比例"来挪，并**卡住上限** —— 打敌方 HQ 时两者离得远，
        /// 比例法会自动挪得多一点；上限保证它绝不会挪到目标的另一侧去。
        /// </summary>
        private Vector2 ImpactPoint(Vector2 targetLocal, Vector2 attackerLocal)
        {
            float dist = Vector2.Distance(targetLocal, attackerLocal);
            if (dist < 1f) return targetLocal;
            float k = Mathf.Clamp(SparkInset / dist, 0f, 0.45f);
            return Vector2.Lerp(targetLocal, attackerLocal, k);
        }

        /// <summary>
        /// 小火花：中心一个亮点 + 六条放射短条，向外扩张的同时淡出，结束后自毁。
        ///
        /// 全部由 `UiKit.AddImage` 程序化生成，**不进 `Art/`** ——
        /// 与卡框/兵种图标"有素材就用素材"的策略相反，但理由成立：
        /// 它只在 0.25s 里出现一次，放进素材目录要多一份许可登记与换图风险，
        /// 而它本来就是几何图形（圆点 + 短条），程序化画出来更精确。
        /// </summary>
        private IEnumerator PlaySpark(Vector2 center, float duration)
        {
            if (duration <= 0f) yield break;

            RectTransform root = UiKit.Anchored("Spark", transform,
                                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                center, new Vector2(1f, 1f));
            root.localScale = new Vector3(0.45f, 0.45f, 1f);

            RectTransform coreRt = UiKit.Anchored("Core", root,
                                                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(SparkCoreSize, SparkCoreSize));
            Image core = UiKit.AddImage(coreRt, SparkCoreColor, Mathf.RoundToInt(SparkCoreSize * 0.5f));

            // 放射条：每条绕自己的轴心转一个角度，再沿该方向外移 —— 六条拼成一圈。
            RectTransform[] bars = new RectTransform[SparkBarCount];
            Image[] barImgs = new Image[SparkBarCount];
            float[] angles = new float[SparkBarCount];
            for (int i = 0; i < SparkBarCount; i++)
            {
                // 起手偏 15°，免得有条正好压在水平/竖直轴上（那样看着像"十字"而不是"炸开"）
                float ang = 360f / SparkBarCount * i + 15f;
                angles[i] = ang * Mathf.Deg2Rad;

                RectTransform barRt = UiKit.Anchored("Bar" + i, root,
                                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                     new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i])) * SparkBarRadius,
                                                     new Vector2(SparkBarWidth, SparkBarLength));
                barRt.localRotation = Quaternion.Euler(0f, 0f, ang);
                barImgs[i] = UiKit.AddImage(barRt, SparkBarColor, Mathf.RoundToInt(SparkBarWidth * 0.5f));
                bars[i] = barRt;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (root == null) yield break;

                float k = Mathf.Clamp01(elapsed / duration);

                // 前 35% 由 0.45 顶到 1.25（"炸开"的那一下），之后不再变
                float pop = Mathf.Lerp(0.45f, 1.25f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.35f)));
                root.localScale = new Vector3(pop, pop, 1f);

                // 放射条同时再往外扩 60%
                float radius = SparkBarRadius * (1f + 0.6f * k);
                for (int i = 0; i < SparkBarCount; i++)
                {
                    if (bars[i] == null) continue;
                    bars[i].anchoredPosition = new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i])) * radius;
                }

                float alpha = 1f - k;
                Color c = core.color; c.a = alpha; core.color = c;
                for (int i = 0; i < SparkBarCount; i++)
                {
                    if (barImgs[i] == null) continue;
                    c = barImgs[i].color; c.a = alpha; barImgs[i].color = c;
                }

                yield return null;
            }

            if (root != null) Destroy(root.gameObject);
        }

        /// <summary>
        /// 一张一张地播（全场唯一实例）。队列空了就退出并把 `_orderRoutine` 置空，
        /// 下一次 `ShowOrderCard` 才能重新启动 —— 这个"收尾置空"漏了的话，
        /// 第一次之后再也不会播（静默失效，且只有"第二张指令卡"才暴露）。
        /// </summary>
        private IEnumerator RunOrderQueue()
        {
            while (_orderQueue.Count > 0)
            {
                CardDef def = _orderQueue.Dequeue();
                yield return StartCoroutine(PlayOrderCard(def));
            }
            _orderRoutine = null;
        }

        /// <summary>
        /// 指令卡的一次完整亮相：淡入 → 移到中线偏左 → 停留 → 向左滑出 → 自毁。
        ///
        /// 卡面**直接复用手牌卡的工厂**（`CardView.CreateHandCard`）——
        /// 这就是"把任务 B 那张卡整体搬过来"，同一张牌在手牌里和动画里长得一模一样。
        /// `onClick` 传 null ⇒ `AttachClick` 直接返回、不加 Button ⇒ 展示中的卡不可点，
        /// 不会在动画期间抢走战场的点击。
        /// </summary>
        private IEnumerator PlayOrderCard(CardDef def)
        {
            float speed = _speed;
            // 队列里排到一半时玩家可能正好把倍率按成 0 —— 那时应当"立即完成"（直接不播），
            // 而不是除零得到 Infinity、把卡留在半透明状态。
            if (speed <= 0f) yield break;

            float fadeIn = OrderFadeIn / speed;
            float move = OrderMove / speed;
            float hold = OrderHold / speed;
            float exit = OrderExit / speed;

            Vector2 from = new Vector2(0f, OrderFromY);
            Vector2 to = OrderShowPos;

            CardView v = CardView.CreateHandCard(transform, from, -1, def, true, false, null);
            if (v == null || v.Rect == null) yield break;
            RectTransform rt = v.Rect;
            rt.localScale = new Vector3(OrderCardScale, OrderCardScale, 1f);

            // 用 CanvasGroup 统一淡入 —— 卡面有十几张 Image + 若干段文字，
            // 逐个改 alpha 既啰嗦又会漏；CanvasGroup.alpha 是整体乘算，一次搞定。
            CanvasGroup group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // ① 手牌位置淡入（"在手牌位置出现一张放大的指令卡"）
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.deltaTime;
                if (rt == null) yield break;
                group.alpha = Mathf.Clamp01(t / fadeIn);
                yield return null;
            }
            if (rt == null) yield break;
            group.alpha = 1f;

            // ② 移到中线偏左（SmoothStep：起步与收尾都软，像被"推"过去而不是弹过去）
            t = 0f;
            while (t < move)
            {
                t += Time.deltaTime;
                if (rt == null) yield break;
                rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / move)));
                yield return null;
            }
            if (rt == null) yield break;
            rt.anchoredPosition = to;

            // ③ 停留（提示词给定的 ~1.0s —— 让玩家看清打出的是哪张牌）
            t = 0f;
            while (t < hold)
            {
                t += Time.deltaTime;
                if (rt == null) yield break;
                yield return null;
            }

            // ④ 向左滑出屏幕：用二次缓入（k²）加速离场，比匀速更像"被扫走"
            t = 0f;
            while (t < exit)
            {
                t += Time.deltaTime;
                if (rt == null) yield break;
                float k = Mathf.Clamp01(t / exit);
                rt.anchoredPosition = new Vector2(Mathf.Lerp(to.x, OrderExitX, k * k), to.y);
                yield return null;
            }

            if (rt != null) Destroy(rt.gameObject);
        }
    }
}
