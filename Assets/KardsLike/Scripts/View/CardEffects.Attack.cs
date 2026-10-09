using System.Collections;
using System.Collections.Generic;
using KardsLike.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    public sealed partial class CardEffectLayer
    {
    /// <summary>
    /// 攻击特效全家（[阶段2.5 · 任务C-④] 统一版 → 兵种细化）：
    ///   `AttackStyle`/`StyleOf`（**默认值就是统一版**，逐一兵种覆盖）
    ///   + `AttackEffect` 入口 + `PlayAttack`（去 / 停 / 回）
    ///   + 步兵小火花 + 坦克炮口闪光 + 轻震屏。
    ///
    /// 放在单独 partial 里的理由见 `CardEffects.cs` 顶部的说明。
    /// 仍然是**同一个 MonoBehaviour**（partial class），没有新增任何运行时对象。
    /// </summary>
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

        // ---- [任务C-④·兵种细化] 坦克：直线推进 + 炮口闪光 + 轻震屏 ----
        /// <summary>
        /// 坦克**推得更深**：整体压上去，而不是步兵那样"捅一下"。
        /// 与步兵 0.22 一起看，两个数字就是"推进"与"突刺"的全部差别。
        /// </summary>
        public const float TankLungeApproach = 0.42f;
        /// <summary>坦克更重、更慢（0.30s 对统一版 0.2s）。</summary>
        public const float TankAttackDuration = 0.30f;
        /// <summary>
        /// 坦克冲到最深处后**停一下再退**（0.08s）—— 这一步是"推进 → 开火 → 退回"的关键，
        /// 少了它就只是"突刺得深一点"，读不出"坦克"。
        /// 炮口闪光与震屏都发生在这一瞬间（见 `PlayAttack`）。
        /// </summary>
        public const float TankHold = 0.08f;
        /// <summary>炮口闪光：总时长（很短 —— 它是"闪"不是"烧"）。</summary>
        private const float MuzzleDuration = 0.12f;
        /// <summary>炮口闪光：中心亮点的边长。</summary>
        private const float MuzzleCoreSize = 20f;
        /// <summary>炮口闪光：**沿炮管方向**那两条火舌的长度（比横向的两条长，才有"喷出去"的方向感）。</summary>
        private const float MuzzleJetLength = 30f;
        /// <summary>炮口闪光：横向两条的长度。</summary>
        private const float MuzzleFlankLength = 16f;
        private const float MuzzleBarWidth = 8f;
        /// <summary>炮口闪光：离枪口（攻击者卡边缘）的距离中心 —— 卡半宽 56，取 46 落在自身卡面近边上。</summary>
        private const float MuzzleOffset = 46f;
        /// <summary>炮口闪光配色（比步兵火花更白更亮，因为它要"刺眼"）。</summary>
        private static readonly Color MuzzleColor = new Color32(255, 250, 226, 255);
        private static readonly Color MuzzleFlankColor = new Color32(255, 229, 158, 255);

        /// <summary>轻震屏的幅度（设计像素）。"轻"是相对的 —— 7 对 1920 宽的画框约 0.36%。</summary>
        public const float ScreenShakeAmplitude = 7f;
        /// <summary>轻震屏时长。</summary>
        public const float ScreenShakeBaseDuration = 0.18f;

        /// <summary>
        /// 一个兵种的**攻击外观参数**。
        ///
        /// 为什么用结构体而不是几个 `XxxOf(kind)` 方法：兵种细化每轮都会新增参数
        /// （坦克这一轮一次就要加 3 个：停顿 / 炮口闪光 / 震屏；后面火炮、战斗机、轰炸机还会加），
        /// 每加一个参数就配一个方法会迅速退化成一排没意义的转发函数。
        ///
        /// **统一版 = 这个结构体的默认值**，细化就是逐字段覆盖 ——
        /// 于是"没被细化过的兵种自动等于统一版"这条纪律由**结构体的初值**保证，
        /// 而不是靠每个分支都记得写全。
        /// </summary>
        public struct AttackStyle
        {
            /// <summary>冲到"到目标距离"的百分之多少。</summary>
            public float Approach;
            /// <summary>总时长（去 + 停 + 回）。</summary>
            public float Duration;
            /// <summary>冲到最深处后的停顿。</summary>
            public float Hold;
            /// <summary>是否在**攻击者自己**的一侧画炮口闪光。</summary>
            public bool MuzzleFlash;
            /// <summary>是否轻震屏。</summary>
            public bool ScreenShake;
            /// <summary>是否在**目标**那一侧画命中火花。</summary>
            public bool ImpactSpark;
        }

        /// <summary>
        /// 取某兵种的攻击外观参数。**默认值就是统一版**（见 <see cref="AttackStyle"/> 的注释）。
        /// </summary>
        public static AttackStyle StyleOf(UnitType kind)
        {
            AttackStyle s = new AttackStyle();
            s.Approach = LungeApproach;
            s.Duration = AttackBaseDuration;
            s.Hold = 0f;
            s.MuzzleFlash = false;
            s.ScreenShake = false;
            s.ImpactSpark = false;

            if (kind == UnitType.Infantry)
            {
                s.Approach = InfantryLungeApproach;
                s.Duration = InfantryAttackDuration;
                s.ImpactSpark = true;
            }
            else if (kind == UnitType.Tank)
            {
                s.Approach = TankLungeApproach;
                s.Duration = TankAttackDuration;
                s.Hold = TankHold;
                s.MuzzleFlash = true;
                s.ScreenShake = true;
            }

            return s;
        }

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

            // 命中点要赶在 StartCoroutine 之前算：协程会在同一帧就把卡挪走，
            // 之后再读 card 的位置就不是"出击前"的落点了。
            Vector2 attackerLocal = LocalOf(card);
            Vector2 targetLocal = LocalOf(targetWorld);

            AttackStyle style = StyleOf(kind);

            StartCoroutine(PlayAttack(card, targetWorld, targetLocal,
                                      style.Duration / _speed, style.Hold / _speed,
                                      style.Approach, style.MuzzleFlash, style.ScreenShake));

            if (style.ImpactSpark) SpawnSpark(ImpactPoint(targetLocal, attackerLocal));
        }

        /// <summary>
        /// 攻击位移：朝目标走一段（`approach` × 到目标的距离）→ 停在最深处（`hold`）→ 原路弹回，
        /// **结束时精确还原**。
        ///
        /// 三个数字由调用方按兵种给出（`AttackStyle`）—— 步兵"捅得短"、坦克"推得深且停一下"，
        /// 但**去 / 停 / 回的骨架完全共用**，所以细化工种不会各写一套动画
        /// （那才是五份要分别维护的真值）。
        ///
        /// 三个自制的硬规矩：
        ///   · 只走一部分，**不能真的走到目标身上** —— 否则两张卡会重叠成一坨
        ///   · 去与回都用 `SmoothStep`、收尾都软 ⇒ 像"刺一下"而不是"平移"；
        ///     回程结束时必须回到 `home`：位置是布局给的，不还原就等于改了布局
        ///   · **`hold` 是"到了、开火、再退回"里的那个停顿** —— 没有它，坦克就只是"突刺得深一点"，
        ///     读不出兵种差别。炮口闪光与震屏都发生在这个瞬间（因为那时车体正好停在最前）
        /// </summary>
        private IEnumerator PlayAttack(RectTransform card, Vector3 targetWorld, Vector2 targetLocal,
                                       float duration, float hold, float approach,
                                       bool muzzleFlash, bool screenShake)
        {
            if (duration <= 0f) yield break;

            Transform parent = card.parent;
            if (parent == null) yield break;

            Vector2 home = card.anchoredPosition;
            Vector3 localTarget = parent.InverseTransformPoint(targetWorld);
            Vector2 lungeTo = Vector2.Lerp(home, new Vector2(localTarget.x, localTarget.y), approach);

            // 去 / 回各占"总时长减去停顿"的一半；hold 为 0 时退化成统一版的两段式
            float move = Mathf.Max(0f, (duration - hold) * 0.5f);

            float t = 0f;
            while (t < move)          // 去
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                card.anchoredPosition = Vector2.Lerp(home, lungeTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / move)));
                yield return null;
            }

            if (card == null) yield break;
            card.anchoredPosition = lungeTo;

            // ---- 最深处：开火 ----
            // 顺序有意义：先算炮口位置、再开震屏。因为炮口的局部坐标是"相对本层"的，
            // 而震屏抖的是本层的祖先（DesignFrame）—— 祖先的平移在相对换算里会约掉，
            // 所以两者其实互不影响；但把"不需要抖的换算"放在抖之前总是更省心。
            if (muzzleFlash)
            {
                float shotAngle;
                Vector2 muzzle = MuzzleAt(lungeTo, targetLocal, parent, out shotAngle);
                SpawnMuzzleFlash(muzzle, shotAngle);
            }
            if (screenShake) ShakeScreen();

            t = 0f;
            while (t < hold)          // 停在最前（开火的那一下）
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                yield return null;
            }

            t = 0f;
            while (t < move)          // 回
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                card.anchoredPosition = Vector2.Lerp(lungeTo, home, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / move)));
                yield return null;
            }

            if (card != null) card.anchoredPosition = home;
        }

        /// <summary>
        /// 炮口位置：车体停在最深处时，它**朝向目标的那条边**再往外一点（`MuzzleOffset`）。
        /// 卡半宽 56 ⇒ 46 仍贴在自身卡面近边上 —— 炮口闪光要看起来"从自己身上喷出去"，
        /// 飘到卡外就变成空中的一个亮点。
        ///
        /// 顺便把**射击方向角**（度）一起吐出去 —— 炮口闪光的火舌必须沿这个方向，
        /// 否则不管从哪个方向打都朝同一个方向喷（第一版就是这么错的，见 §18.5）。
        /// </summary>
        private Vector2 MuzzleAt(Vector2 lungeTo, Vector2 targetLocal, Transform parent, out float shotAngleDeg)
        {
            Vector2 attackerNow = LocalOf(parent.TransformPoint(new Vector3(lungeTo.x, lungeTo.y, 0f)));
            Vector2 dir = targetLocal - attackerNow;
            if (dir.sqrMagnitude < 1f)
            {
                shotAngleDeg = 0f;
                return attackerNow;
            }

            dir = dir.normalized;
            shotAngleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            return attackerNow + dir * MuzzleOffset;
        }

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
    }
}
