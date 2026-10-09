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

        // ---- [任务C-④·兵种细化] 火炮：屏幕外抛物线弹道 + 落点爆炸圈 ----
        /// <summary>
        /// 火炮整套表演的总时长（弹道 + 落地爆炸圈）。
        /// 比坦克的 0.30s 长得多是**故意的**：炮弹要走完一整条弧线才落地，
        /// 太快就只剩"闪了一下"，读不出"隔山打牛"。
        /// </summary>
        public const float ArtilleryShellDuration = 0.78f;
        /// <summary>炮弹飞行占总时长的比例（其余留给落地爆炸圈）。</summary>
        public const float ArtilleryFlightShare = 0.58f;
        /// <summary>
        /// 炮弹飞行的拱高（设计像素）。太小就退化成一条直线，看不出"抛物线"。
        /// 与画框半高（540）比约 0.46 —— 弧顶落在画框内、但明显高于两端。
        /// </summary>
        private const float ShellArcHeight = 250f;
        /// <summary>炮弹起点：画框左边界**再往外**这么远（保证"从屏幕外飞进来"）。</summary>
        private const float ShellStartMargin = 210f;
        /// <summary>炮弹起点相对目标抬高多少（让弧线从上方起、往下方落）。</summary>
        private const float ShellStartLift = 70f;
        /// <summary>炮弹大小（直径）。</summary>
        private const float ShellSize = 15f;
        /// <summary>拖尾挂在炮弹后方多远处（跟着速度方向走）。</summary>
        private const float ShellTrail = 26f;
        private static readonly Color ShellColor = new Color32(255, 226, 158, 255);
        private static readonly Color ShellTrailColor = new Color32(255, 190, 110, 130);

        /// <summary>爆炸圈：空心环的层数（多层错开 ⇒ 像连续几道冲击波）。</summary>
        private const int BlastRingCount = 3;
        /// <summary>
        /// 爆炸圈：环贴图的像素直径。取得比显示尺寸大得多 —— 因为环带会随 `localScale`
        /// 一起变粗，用"大贴图 + 小 scale"能把变粗压到不显眼（见 `PlayBlast` 的注释）。
        /// </summary>
        private const int BlastRingSprite = 200;
        /// <summary>
        /// 爆炸圈：环带宽度（贴图像素）。
        /// 第一版取 8 —— 放大图一看环是"几道几乎看不见的细线"，整个爆炸读成了
        /// "一个半透明的光球"（中心那个大白圆把所有注意力都吸走了）。
        /// 加粗到 11，并把中心爆心缩小一半、提前熄灭（见 `BlastCoreSize`）。
        /// </summary>
        private const int BlastRingThickness = 11;
        /// <summary>爆炸圈：每层环起始的缩放（很小 ⇒ 读起来是"从一个点炸开"）。</summary>
        private const float BlastRingStartScale = 0.13f;
        /// <summary>爆炸圈：最外层的结束缩放（直径 ≈ 236 设计像素 ≈ 两张卡宽）。</summary>
        private const float BlastRingEndScale = 1.18f;
        /// <summary>爆炸圈：相邻两层环错开的启动时间（占爆炸时长的比例）。</summary>
        private const float BlastRingStaggerShare = 0.12f;
        /// <summary>
        /// 爆炸圈：中心爆心闪光的尺寸。
        /// 第一版 56 太大了 —— 它盖住了整个卡面、还因为 alpha 慢慢降而一直糊在那里，
        /// 于是"爆心"变成了"主角"。缩到 30 且只亮头 30% 的时间，它才是"起爆的那一下"。
        /// </summary>
        private const float BlastCoreSize = 30f;
        private static readonly Color BlastRingColor = new Color32(255, 206, 122, 255);
        private static readonly Color BlastCoreColor = new Color32(255, 248, 224, 255);

        // ---- [任务C-④·兵种细化] 战斗机：斜向俯掠 + 弹道线 ----
        /// <summary>
        /// 战斗机**掠得最远**（走到两者距离的一半）。三个已细化兵种的推进深度连起来看就是
        /// 一句兵种说明：步兵 0.22 是"捅一下"、统一版 0.35 是"推一下"、坦克 0.42 是"压上去"、
        /// 战斗机 0.50 是"掠过"。
        /// </summary>
        public const float FighterLungeApproach = 0.50f;
        /// <summary>战斗机最快（0.24s 对坦克 0.30s）—— "掠"这个动作本身就该是干脆的。</summary>
        public const float FighterAttackDuration = 0.24f;
        /// <summary>
        /// 战斗机走位的**侧向拱高**（占"到目标距离"的比例）。
        /// 这就是"**斜向**俯掠"这个说法的全部实现：位移不再是直线，而是沿一条**垂直于连线**的弧，
        /// 半途侧偏最多。取值只有它自己一个，所以"撞过去"与"掠过去"的区别一眼可辨。
        /// </summary>
        public const float FighterArcShare = 0.42f;
        /// <summary>弹道线的宽度（设计像素）。</summary>
        private const float TracerWidth = 4f;
        /// <summary>
        /// 弹道线的总时长。
        /// 第一版是 0.22s、生长占 45% —— 放大图一看，全长状态只维持一瞬，
        /// 截到的那一帧线才 16 长（缩在两张卡之间几乎看不见）。改成 0.26s + 生长占 30%：
        /// "打出一梭子"的感觉还在，但**全长状态占了后 70%**，肉眼有足够时间读到"这一条弹道"。
        /// </summary>
        private const float TracerDuration = 0.26f;
        /// <summary>弹道线里"生长"阶段占的比例（其余是淡出）。</summary>
        private const float TracerGrowShare = 0.30f;
        /// <summary>弹道线配色：接近纯白的暖色 —— 它要读成"一道曳光"，不是"一根边框"。</summary>
        private static readonly Color TracerColor = new Color32(255, 252, 236, 255);

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
            /// <summary>
            /// 走位是**直线**还是**侧向拱起的弧**：值是拱高占"到目标距离"的比例（0 = 直线）。
            ///
            /// 战斗机靠它把"撞过去"变成"掠过去"—— 沿一条垂直于连线的弧冲向目标，
            /// 侧偏最大发生在半途。**0 是默认值**，所以统一版与其它已细化兵种仍是直线。
            /// </summary>
            public float Arc;
            /// <summary>是否在开火瞬间画一条**从攻击者到目标的弹道线**（战斗机）。</summary>
            public bool Tracer;
            /// <summary>
            /// 是否走"**屏幕外抛物线弹道**"（火炮）：**攻击者自己一步都不动**，
            /// 炮弹从画框外划一道弧落到目标。开了它就不再走 `PlayAttack` 的"去 / 停 / 回"骨架
            /// —— 那条骨架里全是位移，对"隔山打牛"全是空转。
            /// </summary>
            public bool ShellArc;
            /// <summary>炮弹落点是否炸开一圈冲击波（爆炸圈）。</summary>
            public bool Blast;
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
            s.Arc = 0f;              // 0 = 直线：统一版与已细化的步兵 / 坦克 / 火炮都不走弧
            s.Tracer = false;
            s.ShellArc = false;
            s.Blast = false;

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
            else if (kind == UnitType.Artillery)
            {
                // 隔山打牛：自己**一步都不动**（`Approach = 0`）——
                // 炮弹从画框外划弧落下来，攻击者只是那条弹道的来源，画面上不需要位移。
                // 这个选择顺带说明了一件事：它免反击、能打任意战线，与"待在原地开火"是同一个意思。
                s.Approach = 0f;
                s.Duration = ArtilleryShellDuration;
                s.ShellArc = true;
                s.Blast = true;
            }
            else if (kind == UnitType.Fighter)
            {
                // "斜向俯掠"：走位从直线换成**侧向拱起的弧**（`Arc`），并在开火瞬间拖一条**弹道线**。
                // 它的位移仍然走 `PlayAttack` 那条骨架（去 / 回），只是路径从弦换成了弧 ——
                // 与火炮那种"连骨架都不用"是两回事：战斗机确实在位移，只是不走直线。
                s.Approach = FighterLungeApproach;
                s.Duration = FighterAttackDuration;
                s.Arc = FighterArcShare;
                s.Tracer = true;
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

            // 火炮走的是另一条骨架：自己不位移，炮弹从画框外划弧落到目标。
            // 它没有"去 / 停 / 回"这三个阶段，硬塞进 PlayAttack 只会让那段位移代码空转
            // （approach = 0 ⇒ 起点＝终点），读代码的人还得自己推一遍才知道"它其实没动"。
            if (style.ShellArc)
            {
                StartCoroutine(PlayShellArc(targetLocal, style.Duration, style.Blast));
                return;
            }

            StartCoroutine(PlayAttack(card, targetWorld, targetLocal, style));

            if (style.ImpactSpark) SpawnSpark(ImpactPoint(targetLocal, attackerLocal));

            // [战斗机] 弹道线**在这里启动，而不是在 `PlayAttack` 跑到位移最深处时创建**。
            // 原因：攻击者的卡每次 `Refresh()` 都会被销毁重建，而 AI 回合每 0.4s 就刷新一次 ——
            // 玩家把动画调慢（×0.5 / ×0.25）时，去程会长过 0.4s，动画协程在到达最深处之前
            // 就被下一步刷新打断（`card == null` ⇒ yield break），弹道线于是**根本不会出现**。
            // 实测 ×0.25 下 AI 侧完全看不到这条线 —— 而它正是这个兵种唯一的识别特征。
            // 改成由本方法启动、自带"等到开火时刻"的延迟 ⇒ 特效的生命周期不再依赖另一个协程。
            if (style.Tracer)
            {
                Transform tparent = card.parent;
                if (tparent != null)
                {
                    // 起点 = 攻击者"冲到最深处"时的中心，换算到设计画框坐标
                    Vector3 lt = tparent.InverseTransformPoint(targetWorld);
                    Vector2 lungeTo = Vector2.Lerp(card.anchoredPosition,
                                                   new Vector2(lt.x, lt.y), style.Approach);
                    Vector2 fromDesign = LocalOf(tparent.TransformPoint(new Vector3(lungeTo.x, lungeTo.y, 0f)));
                    float approachTime = (style.Duration - style.Hold) * 0.5f;   // 去程时长 = 开火时刻
                    SpawnTracer(fromDesign, targetLocal, approachTime);
                }
            }
        }

        /// <summary>
        /// 攻击位移：朝目标走一段（`style.Approach` × 到目标的距离）→ 停在最深处（`style.Hold`）→ 原路弹回，
        /// **结束时精确还原**。
        ///
        /// 参数由 `AttackStyle` 整份传进来（而不是拆成一串 `bool` / `float`）——
        /// 每加一个兵种都会加字段，拆开传的话签名会越来越长、而且"漏传一个"编译器不会拦
        /// （`PlayAttack` 只管播，不知道哪个兵种该有什么）。
        ///
        /// 三个自制的硬规矩：
        ///   · 只走一部分，**不能真的走到目标身上** —— 否则两张卡会重叠成一坨
        ///   · 去与回都用 `SmoothStep`、收尾都软 ⇒ 像"刺一下"而不是"平移"；
        ///     回程结束时必须回到 `home`：位置是布局给的，不还原就等于改了布局
        ///   · **`hold` 是"到了、开火、再退回"里的那个停顿** —— 没有它，坦克就只是"突刺得深一点"，
        ///     读不出兵种差别。炮口闪光、弹道线与震屏都发生在这个瞬间（那时正好停在最前）
        ///
        /// [战斗机] `style.Arc > 0` 时，去 / 回都不再走直线，而是沿一条**垂直于连线**的弧：
        /// `pos = Lerp(a, b, s) + perp · arc · sin(πs)`。两个端点处 `sin` 为 0 ⇒
        /// 起点、最深处、终点都与直线版**完全重合**（所以"结束精确还原"这条不受影响），
        /// 只有中段被侧向推开 —— 这就是"掠过去"与"撞过去"的全部差别。
        /// </summary>
        private IEnumerator PlayAttack(RectTransform card, Vector3 targetWorld, Vector2 targetLocal,
                                       AttackStyle style)
        {
            float duration = style.Duration / _speed;
            float hold = style.Hold / _speed;
            if (duration <= 0f) yield break;

            Transform parent = card.parent;
            if (parent == null) yield break;

            Vector2 home = card.anchoredPosition;
            Vector3 localTarget = parent.InverseTransformPoint(targetWorld);
            Vector2 targetV = new Vector2(localTarget.x, localTarget.y);

            Vector2 lungeTo = Vector2.Lerp(home, targetV, style.Approach);

            // 弧的"鼓向"：把"朝目标方向"逆时针转 90°。
            // 选这个方向（而不是"固定朝上"）的理由：画框是 1920×1080 的横向矩形 ——
            // 侧向推开的可用空间总比纵向大，所以无论目标在上下左右，这样鼓都不容易顶出画框。
            Vector2 toTarget = targetV - home;
            float dist = toTarget.magnitude;
            Vector2 dir = dist > 0.001f ? toTarget / dist : Vector2.right;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float arc = dist * style.Arc;

            // 去 / 回各占"总时长减去停顿"的一半；hold 为 0 时退化成统一版的两段式
            float move = Mathf.Max(0f, (duration - hold) * 0.5f);

            float t = 0f;
            while (t < move)          // 去
            {
                t += Time.deltaTime;
                if (card == null) yield break;
                card.anchoredPosition = ArcPoint(home, lungeTo, perp, arc, Mathf.Clamp01(t / move));
                yield return null;
            }

            if (card == null) yield break;
            card.anchoredPosition = lungeTo;

            // ---- 最深处：开火 ----
            // 顺序有意义：先算炮口位置 / 弹道线、再开震屏。因为这两者的局部坐标都是"相对本层"的，
            // 而震屏抖的是本层的祖先（DesignFrame）—— 祖先的平移在相对换算里会约掉，
            // 所以其实互不影响；但把"不需要抖的换算"放在抖之前总是更省心。
            if (style.MuzzleFlash)
            {
                // 注意第二个参数要传 `targetLocal`（设计画框坐标）而不是 `targetV`：
                // `MuzzleAt` 内部把"攻击者当前位置"也换算到设计画框坐标再相减，两者必须同一坐标系。
                float shotAngle;
                Vector2 muzzle = MuzzleAt(lungeTo, targetLocal, parent, out shotAngle);
                SpawnMuzzleFlash(muzzle, shotAngle);
            }
            if (style.ScreenShake) ShakeScreen();

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
                card.anchoredPosition = ArcPoint(lungeTo, home, perp, arc, Mathf.Clamp01(t / move));
                yield return null;
            }

            if (card != null) card.anchoredPosition = home;
        }

        /// <summary>
        /// 弧线上的一个点：`Lerp(a, b, s)` 再沿 `perp` 侧偏 `arc · sin(πs)`。
        ///
        /// `sin(πs)` 在 s = 0 与 s = 1 处都恰好为 0 ⇒ **两个端点与直线版逐位相同**，
        /// 只有中段被推开。于是"只走到一部分"和"结束精确还原"这两条硬规矩在走弧时**自动继续成立**，
        /// 不需要为弧线单写一套收尾逻辑。
        /// </summary>
        private static Vector2 ArcPoint(Vector2 a, Vector2 b, Vector2 perp, float arc, float k)
        {
            float s = Mathf.SmoothStep(0f, 1f, k);
            return Vector2.Lerp(a, b, s) + perp * (arc * Mathf.Sin(Mathf.PI * s));
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
