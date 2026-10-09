using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    public sealed partial class CardEffectLayer
    {
    /// <summary>
    /// 攻击特效的**实现**（[阶段2.5 · 任务C-④]）：坦克炮口闪光 + 轻震屏 / 步兵命中火花 /
    /// 火炮屏幕外抛物线弹道 + 落点爆炸圈 / 战斗机弹道线。骨架与参数在
    /// `CardEffects.Attack.cs`（本类的另一个 partial），拆分说明见那里顶部。
    /// </summary>
        /// <summary>
        /// 震屏要抖的节点（**由 `GameLayout` 赋成 `DesignFrame`**）。
        ///
        /// 为什么一定是它：卡的节点每次 `Refresh()` 都会被 `ClearSpawned()` 销毁重建，
        /// 把震屏挂在卡或卡行上 ⇒ 动画被腰斩、还会把布局位置抖歪。
        /// `DesignFrame` 在整个对局里只建一次，而且**没有任何代码写它的 `anchoredPosition`**
        /// （只有 `localScale` 被 `UpdateDesignScale` 管）—— 抖它、再精确还原，是安全的。
        /// </summary>
        [System.NonSerialized] public RectTransform ShakeTarget;

        /// <summary>震屏开始前记下的基准位置 —— 还原时用它，不靠"假设基准是 0"。</summary>
        private Vector2 _screenShakeBase;
        private Coroutine _screenShakeRoutine;

        // ==================================================================
        // [任务C-④·兵种细化] 坦克的炮口闪光 + 轻震屏
        // ==================================================================

        /// <summary>在炮口炸一下亮光。倍率 0 时调用方不会走到这里，这里再挡一道。</summary>
        private void SpawnMuzzleFlash(Vector2 at, float shotAngleDeg)
        {
            if (!Enabled) return;
            StartCoroutine(PlayMuzzleFlash(at, shotAngleDeg, MuzzleDuration / _speed));
        }

        /// <summary>
        /// 炮口闪光：中心亮点 + **沿炮管方向的两条火舌**（长）与**横向的两条**（短），
        /// 拼成一个有方向感的十字。收得极快（0.12s）—— 它是"闪"不是"烧"。
        ///
        /// ⚠️ **角度这一步不能省**：uGUI 里矩形的长度轴是**局部 Y**，所以"长度沿某个方向"
        /// 需要把矩形整体转到 `方向 − 90°`。第一版把火舌放在 0°/180°、以为那就是水平，
        /// 结果不管怎么打都是**竖着喷**（放大图里就是一根竖亮条）。
        /// 现在整组条都按 `shotAngleDeg` 转，横打就横喷、竖打（打 HQ）就竖喷。
        ///
        /// 与步兵火花的区别是刻意的：火花在**目标**上、6 条等长的细条（无方向）；炮口闪光在
        /// **攻击者自己**身上、4 条有长短之分（有方向）。两者同时出现也不会读混。
        /// </summary>
        private IEnumerator PlayMuzzleFlash(Vector2 at, float shotAngleDeg, float duration)
        {
            if (duration <= 0f) yield break;

            float rad = shotAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 perp = new Vector2(-dir.y, dir.x);

            RectTransform root = UiKit.Anchored("MuzzleFlash", transform,
                                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                at, new Vector2(1f, 1f));
            root.localScale = new Vector3(0.6f, 0.6f, 1f);

            RectTransform coreRt = UiKit.Anchored("Core", root,
                                                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(MuzzleCoreSize, MuzzleCoreSize));
            Image core = UiKit.AddImage(coreRt, MuzzleColor, Mathf.RoundToInt(MuzzleCoreSize * 0.5f));

            // 4 条：两条沿炮管（长、亮白）、两条横向（短、略黄）
            Image[] barImgs = new Image[4];
            float[] barFrac = new float[] { 0f, 0f, 1f, 1f };   // 0 = 火舌，1 = 横向
            for (int i = 0; i < 4; i++)
            {
                bool along = barFrac[i] < 0.5f;
                float len = along ? MuzzleJetLength : MuzzleFlankLength;
                float w = along ? MuzzleBarWidth : MuzzleBarWidth - 3f;
                Vector2 axis = along ? dir : perp;
                float sign = (i % 2 == 0) ? 1f : -1f;
                // 长度轴（局部 Y）指向 axis ⇒ 旋转角 = axis 的极角 − 90°
                float barAngle = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg - 90f;

                RectTransform barRt = UiKit.Anchored("Bar" + i, root,
                                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                     axis * (sign * len * 0.5f), new Vector2(w, len));
                barRt.localRotation = Quaternion.Euler(0f, 0f, barAngle);
                barImgs[i] = UiKit.AddImage(barRt, along ? MuzzleColor : MuzzleFlankColor, 3);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (root == null) yield break;

                float k = Mathf.Clamp01(elapsed / duration);

                // 先"炸开"再收：前 30% 由 0.6 冲到 1.3，其余时间维持
                float pop = Mathf.Lerp(0.6f, 1.3f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.3f)));
                root.localScale = new Vector3(pop, pop, 1f);

                // 淡出比扩张快 —— "闪"的观感来自亮度掉得比尺寸快
                float alpha = 1f - Mathf.Clamp01(k * 1.15f);
                Color c = core.color; c.a = alpha; core.color = c;
                for (int i = 0; i < 4; i++)
                {
                    if (barImgs[i] == null) continue;
                    c = barImgs[i].color; c.a = alpha; barImgs[i].color = c;
                }

                yield return null;
            }

            if (root != null) Destroy(root.gameObject);
        }

        /// <summary>
        /// 轻震屏：抖 `ShakeTarget`（=`DesignFrame`）。
        ///
        /// 两条与"抖卡片"不同的地方：
        ///   · **重复调用要先把上一次停掉并还原** —— 两次震屏叠在一起时，先启动的那个
        ///     会先回到基准，把后一个的位移硬拽回去，看起来像"抖到一半被掐断"。
        ///     所以这里记住句柄，重入时先 `StopCoroutine` + 精确还原，再重新起。
        ///   · 抖完 / 被 `ClearAll` 停掉时**都必须还原** —— 位置是布局给的，
        ///     震屏不还原就等于把整个界面搬走了（比卡片跑偏严重得多）。
        /// </summary>
        public void ShakeScreen()
        {
            if (!Enabled || ShakeTarget == null) return;

            if (_screenShakeRoutine != null)
            {
                StopCoroutine(_screenShakeRoutine);
                _screenShakeRoutine = null;
                ShakeTarget.anchoredPosition = _screenShakeBase;
            }

            _screenShakeBase = ShakeTarget.anchoredPosition;
            _screenShakeRoutine = StartCoroutine(PlayScreenShake(ScreenShakeBaseDuration / _speed));
        }

        private IEnumerator PlayScreenShake(float duration)
        {
            RectTransform target = ShakeTarget;
            if (target == null || duration <= 0f) { _screenShakeRoutine = null; yield break; }

            Vector2 basePos = _screenShakeBase;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (target == null) { _screenShakeRoutine = null; yield break; }

                float decay = 1f - Mathf.Clamp01(elapsed / duration);
                // 两个轴用不同频率，否则会读成"沿一条对角线来回"而不是"震"
                float dx = Mathf.Cos(elapsed * 96f) * ScreenShakeAmplitude * decay;
                float dy = Mathf.Sin(elapsed * 131f) * ScreenShakeAmplitude * 0.62f * decay;
                target.anchoredPosition = basePos + new Vector2(dx, dy);
                yield return null;
            }

            if (target != null) target.anchoredPosition = basePos;
            _screenShakeRoutine = null;
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

        // ==================================================================
        // [任务C-④·兵种细化] 火炮：屏幕外抛物线弹道 + 落点爆炸圈
        // ==================================================================

        /// <summary>
        /// 屏幕外抛物线弹道：炮弹**从画框左外飞入**，拱到最高点后落向目标，落地炸开一圈冲击波。
        /// 攻击者自己一动不动 —— 这是"隔山打牛"唯一的视觉答案。
        ///
        /// 抛物线用最朴素的"线性插值 + 正弦拱高"：`P(t) = Lerp(start, end, t) + up · arc · sin(πt)`。
        /// 不用真实重力公式，因为这里要的是"**看得出是一条抛物线**"而不是物理正确；
        /// 正弦还有个免费的好处：`sin` 在 0 与 π 处斜率平缓 ⇒ 起飞与落地都是软收，读起来像"抛出去的弹丸"。
        ///
        /// 起点固定在**画框左边界再往外**（与目标位置无关）—— 于是无论火炮打哪一格，
        /// 玩家看到的都是"炮弹从屏幕外飞进来"，而不是"从某个刚好在框内的位置凭空出现"。
        /// </summary>
        private IEnumerator PlayShellArc(Vector2 targetLocal, float duration, bool blast)
        {
            if (duration <= 0f || _speed <= 0f) yield break;

            float total = duration / _speed;
            float flight = total * ArtilleryFlightShare;
            if (flight <= 0f) yield break;

            // 半宽取自**本层**的 rect（本层铺满画框），所以起点永远落在画框之外。
            // 本类是 MonoBehaviour，`rect` 得从自己的 RectTransform 上取。
            RectTransform self = transform as RectTransform;
            if (self == null) yield break;
            float startX = -(self.rect.width * 0.5f) - ShellStartMargin;
            Vector2 start = new Vector2(startX, targetLocal.y + ShellStartLift);

            RectTransform shell = UiKit.Anchored("Shell", transform,
                                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                 start, new Vector2(ShellSize, ShellSize));
            UiKit.AddImage(shell, ShellColor, Mathf.RoundToInt(ShellSize * 0.5f));

            // 拖尾：一个更小更淡的点，永远挂在炮弹的**后方** ——
            // 15px 的圆点不给个"尾巴"，读不出它在往哪个方向飞。
            RectTransform trail = UiKit.Anchored("Trail", transform,
                                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                 start, new Vector2(ShellSize * 0.8f, ShellSize * 0.8f));
            UiKit.AddImage(trail, ShellTrailColor, Mathf.RoundToInt(ShellSize * 0.4f));

            Vector2 prev = start;
            float t = 0f;
            while (t < flight)
            {
                t += Time.deltaTime;
                if (shell == null || trail == null) yield break;

                float k = Mathf.Clamp01(t / flight);
                Vector2 p = Vector2.Lerp(start, targetLocal, k) + new Vector2(0f, Mathf.Sin(Mathf.PI * k) * ShellArcHeight);
                shell.anchoredPosition = p;

                // 尾巴的方向 = "上一帧 → 这一帧"的**反**方向（也就是来路），
                // 比"直接指向起点"准：弧线中段的实际速度方向与起点方向差很远。
                Vector2 back = prev - p;
                if (back.sqrMagnitude < 0.01f) back = new Vector2(-1f, 1f);
                trail.anchoredPosition = p + back.normalized * ShellTrail;
                prev = p;

                yield return null;
            }

            if (shell != null) Destroy(shell.gameObject);
            if (trail != null) Destroy(trail.gameObject);

            if (blast) StartCoroutine(PlayBlast(targetLocal, total - flight));
        }

        /// <summary>
        /// 落点爆炸圈：`BlastRingCount` 层**空心环**依次向外扩散淡出，中心再补一下爆心闪光。
        ///
        /// 为什么必须是"环"而不是"实心圆"：冲击波的关键是**中间留空** ——
        /// 实心圆扩散看起来像"原地长出一块东西"，空心环才读成"炸开的一道波"。
        /// 所以这里用 `UiKit.Ring`（环带走有符号距离场生成的**空心**贴图）。
        ///
        /// ⚠️ 环带会随 `localScale` 一起变粗（`Image` 缩放绕不过去，环贴图也不能动态改厚度）：
        /// 用**大直径贴图（200）+ 小 scale** 把这件事压到不显眼（环带从 ~1.4 到 ~7.8），
        /// 再让 alpha 一路降到 0 ⇒ 读起来是"波散开了"，不是"圈变胖了"。
        /// </summary>
        private IEnumerator PlayBlast(Vector2 center, float duration)
        {
            if (duration <= 0f) yield break;

            RectTransform root = UiKit.Anchored("Blast", transform,
                                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                center, new Vector2(1f, 1f));

            RectTransform coreRt = UiKit.Anchored("Core", root,
                                                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                                  Vector2.zero, new Vector2(BlastCoreSize, BlastCoreSize));
            Image core = UiKit.AddImage(coreRt, BlastCoreColor, Mathf.RoundToInt(BlastCoreSize * 0.5f));

            Sprite ringSprite = UiKit.Ring(BlastRingSprite, BlastRingThickness);
            RectTransform[] rings = new RectTransform[BlastRingCount];
            Image[] ringImgs = new Image[BlastRingCount];
            float[] ringDelay = new float[BlastRingCount];
            float[] ringEnd = new float[BlastRingCount];

            for (int i = 0; i < BlastRingCount; i++)
            {
                rings[i] = UiKit.Anchored("Ring" + i, root,
                                          new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                          Vector2.zero, new Vector2(BlastRingSprite, BlastRingSprite));
                ringImgs[i] = UiKit.AddSprite(rings[i], ringSprite, BlastRingColor);
                rings[i].localScale = new Vector3(BlastRingStartScale, BlastRingStartScale, 1f);

                ringDelay[i] = duration * BlastRingStaggerShare * i;
                // 外层扩得更大（内层 0.55 × 终点、最外层 = 终点）⇒ 三道波拉开层次
                float outer = BlastRingCount <= 1 ? 1f : (float)i / (BlastRingCount - 1);
                ringEnd[i] = Mathf.Lerp(BlastRingEndScale * 0.55f, BlastRingEndScale, outer);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (root == null) yield break;

                // 爆心：只亮头 30%，迅速缩着炸开 —— 它是"起爆的那一下"，不是主角
                float ck = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration * 0.30f));
                float cs = Mathf.Lerp(0.25f, 0.95f, Mathf.SmoothStep(0f, 1f, ck));
                coreRt.localScale = new Vector3(cs, cs, 1f);
                Color cc = core.color; cc.a = 1f - ck; core.color = cc;

                for (int i = 0; i < BlastRingCount; i++)
                {
                    if (rings[i] == null || ringImgs[i] == null) continue;

                    float span = Mathf.Max(0.01f, duration - ringDelay[i]);
                    float k = Mathf.Clamp01((elapsed - ringDelay[i]) / span);
                    float s = Mathf.Lerp(BlastRingStartScale, ringEnd[i], Mathf.SmoothStep(0f, 1f, k));
                    rings[i].localScale = new Vector3(s, s, 1f);

                    // 前 12% 保持全亮（否则一出生就在变淡，读起来像"本来就有的淡圈"），
                    // 之后平滑降到 0（扩到最大时正好消失）。
                    // 还没轮到的那层必须**完全透明**，否则会在中心先冒出三个小环（"提前起爆"的错读）。
                    Color c = ringImgs[i].color;
                    c.a = elapsed < ringDelay[i] ? 0f : (1f - Mathf.SmoothStep(0.12f, 1f, k));
                    ringImgs[i].color = c;
                }

                yield return null;
            }

            if (root != null) Destroy(root.gameObject);
        }

        // ==================================================================
        // [任务C-④·兵种细化] 战斗机的弹道线
        // ==================================================================

        /// <summary>
        /// 在"攻击者 → 目标"之间拖一条曳光。倍率 0 时调用方不会走到这里，这里再挡一道。
        /// `delay` 是"等到开火时刻"的等待时长（= 去程时长，由调用方按兵种算好）。
        /// </summary>
        private void SpawnTracer(Vector2 from, Vector2 to, float delay)
        {
            if (!Enabled) return;
            StartCoroutine(PlayTracer(from, to, delay / _speed, TracerDuration / _speed));
        }

        /// <summary>
        /// 弹道线：一条**从攻击者长到目标**的细线（前 `TracerGrowShare` 生长、其余淡出）——
        /// 读起来像一梭子曳光弹沿着那条轨迹打出去，而不是"凭空出现一根线"。
        ///
        /// `delay` 让它与动画的"开火时刻"对齐，**但它不依赖动画协程**：
        /// 攻击者的卡每次 `Refresh()` 都会被销毁重建，而 AI 回合每 0.4s 刷新一次 ——
        /// 把这条线挂在 `PlayAttack` 里的话，倍率调慢时协程会先被打断，线就再也不出现了。
        /// 所以它自己等、自己长、自己淡出（挂在特效层上，不受卡重建影响）。
        ///
        /// 两个几何要点（都是这个项目踩过的那两个坑，这里再明确一次）：
        ///   · **长度轴是"局部 Y"** ⇒ 想让线指向某个方向，旋转角必须是 `方向的极角 − 90°`
        ///     （第一版炮口火舌就是照"角度 = 方向"写错的，见 §18.5）；
        ///   · **轴心放在起点端**（`pivot = (0.5, 0)`）⇒ 让 `sizeDelta.y` 从 0 长到全长时，
        ///     线是"从攻击者这边往目标长"，而不是"从中心往两头长"。
        ///
        /// 用 `AddImage(..., radius: 0)` 的直角贴图：它是一条线，不该有圆角。
        /// </summary>
        private IEnumerator PlayTracer(Vector2 from, Vector2 to, float delay, float duration)
        {
            if (duration <= 0f) yield break;

            float wait = delay;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            Vector2 delta = to - from;
            float len = delta.magnitude;
            if (len < 1f) yield break;          // 攻击者与目标几乎重合 ⇒ 画出来是个点，不如不画

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;

            RectTransform line = UiKit.Anchored("Tracer", transform,
                                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                                                from, new Vector2(TracerWidth, 0f));
            line.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image img = UiKit.AddImage(line, TracerColor, 0);

            float grow = duration * TracerGrowShare;
            float fade = Mathf.Max(0.01f, duration - grow);

            float t = 0f;
            while (t < grow)          // 生长：从攻击者往目标
            {
                t += Time.deltaTime;
                if (line == null) yield break;
                line.sizeDelta = new Vector2(TracerWidth, len * Mathf.Clamp01(t / grow));
                yield return null;
            }
            if (line == null) yield break;
            line.sizeDelta = new Vector2(TracerWidth, len);

            t = 0f;
            while (t < fade)          // 淡出（长度不动，只掉亮度）
            {
                t += Time.deltaTime;
                if (line == null || img == null) yield break;
                Color c = img.color;
                c.a = 1f - Mathf.Clamp01(t / fade);
                img.color = c;
                yield return null;
            }

            if (line != null) Destroy(line.gameObject);
        }
    }
}
