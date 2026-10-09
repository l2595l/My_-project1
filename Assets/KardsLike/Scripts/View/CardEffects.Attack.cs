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
    /// 攻击特效的**骨架与参数**（[阶段2.5 · 任务C-④] 统一版 → 兵种细化）：
    ///   · 参数表：五个兵种的常量组 + `AttackStyle` / `StyleOf`（**默认值就是统一版**，逐一兵种覆盖）
    ///   · 骨架：`AttackEffect` 入口 + `PlayAttack`（去 / 停 / 回）+ 弧线几何 `ArcPoint` + 炮口几何 `MuzzleAt`
    /// 各兵种"画东西"的实现（火花 / 炮口闪光 / 震屏 / 弹道 / 爆炸）都在 `CardEffects.Attack.Effects.cs`。
    ///
    /// 拆分理由：加完战斗机后本文件 936 行（阈值 1100），而轰炸机这一轮还要加
    /// 投弹 + 范围爆炸 + 强震屏三样 —— 与其等越过阈值再拆（`GameEngine.cs` 就是这么挂了三轮 P0 的），
    /// 不如**动它之前先拆**。纯结构搬家，验收口径＝编译通过 + 演出行为逐条一致。
    /// 两个文件是**同一个 MonoBehaviour**（partial class），没有新增任何运行时对象。
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

    }
}
