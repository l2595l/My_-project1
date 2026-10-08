using KardsLike.Core;
using UnityEngine;

namespace KardsLike.View
{
    /// <summary>
    /// 卡面美术资源的引用集合（阶段 2.5 · 任务 B 新增）。
    ///
    /// 【为什么需要一个 ScriptableObject，而不是直接 Resources.Load 每张图】
    ///   `Art/` 目录**刻意没有**叫 `Resources`（那是 Unity 的保留名，会把整批素材
    ///   打进入包体，且路径一改 CREDITS.txt 与阶段 2.5 文档 §8 的登记就全部失效）。
    ///   所以用一个 `.asset` 把 7 张图的引用集中起来，放进 `Assets/KardsLike/Resources/`，
    ///   运行期只 `Resources.Load<CardArt>("CardArt")` 一次。
    ///   好处：① 美术文件仍在登记过的路径上、文件名与像素尺寸一字未动；
    ///         ② 编辑器与打包都可用（不像 `AssetDatabase` 只在编辑器里有效）；
    ///         ③ 缺资源时不抛异常，只是退回程序化绘制（见 Instance 的警告）。
    ///
    /// 【本类只描述"长什么样"，不含任何对局判断】—— 配色、几何都是常量，
    /// 与规则无关。CardView 负责把它们摆到卡上。
    /// </summary>
    public sealed class CardArt : ScriptableObject
    {
        // ==================================================================
        // 资源引用（在 Assets/KardsLike/Resources/CardArt.asset 里配置）
        // ==================================================================

        [Header("卡框（Art/CardFrame.png，550×766，CC0）")]
        public Sprite Frame;

        [Header("兵种图标（Art/UnitIcons/*.png，白色剪影，靠 Image.color 着色）")]
        public Sprite IconInfantry;
        public Sprite IconTank;
        public Sprite IconArtillery;
        public Sprite IconFighter;
        public Sprite IconBomber;

        // ==================================================================
        // 空安全加载
        // ==================================================================

        private const string ResourcePath = "CardArt";

        private static CardArt _instance;
        private static bool _missingLogged;

        /// <summary>资源集合。取不到时返回 null，调用方一律按"退回程序化绘制"处理。</summary>
        public static CardArt Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<CardArt>(ResourcePath);
                    if (_instance == null && !_missingLogged)
                    {
                        _missingLogged = true;
                        Debug.LogWarning("[CardArt] 未找到 Assets/KardsLike/Resources/CardArt.asset —— " +
                                         "卡面将退回程序化绘制（功能不受影响，只是没有卡框与兵种图标）。");
                    }
                }
                return _instance;
            }
        }

        /// <summary>卡框 Sprite；缺资源时为 null。</summary>
        public static Sprite FrameOrNull()
        {
            CardArt a = Instance;
            return a == null ? null : a.Frame;
        }

        /// <summary>兵种图标 Sprite；`None` 或缺资源时为 null（调用方退回汉字徽章）。</summary>
        public static Sprite IconOrNull(UnitType kind)
        {
            CardArt a = Instance;
            return a == null ? null : a.IconFor(kind);
        }

        /// <summary>按兵种取图标（实例方法，供 Inspector 时代码调用）。</summary>
        public Sprite IconFor(UnitType kind)
        {
            switch (kind)
            {
                case UnitType.Infantry:  return IconInfantry;
                case UnitType.Tank:      return IconTank;
                case UnitType.Artillery: return IconArtillery;
                case UnitType.Fighter:   return IconFighter;
                case UnitType.Bomber:    return IconBomber;
                default:                 return null;
            }
        }

        // ==================================================================
        // 卡框实测几何（在 550×766 上量出来的，见 Art/CREDITS.txt 与 §8.3）
        //
        // 数值是对 CardFrame.png 逐像素扫描 alpha 得到的，**不要自己目测**：
        //   · 开窗（透明矩形）： x[32..518] y[61..471]
        //   · 圆角：最上一行的左右内缩 23px，到第 24 行收敛到 0 ⇒ 半径 ≈ 24px
        // ==================================================================

        /// <summary>开窗左边界，占卡宽的比。</summary>
        public const float WindowLeft = 0.0582f;
        /// <summary>开窗上边界，占卡高的比。</summary>
        public const float WindowTop = 0.0796f;
        /// <summary>开窗宽度，占卡宽的比。</summary>
        public const float WindowWidth = 0.8855f;
        /// <summary>开窗高度，占卡高的比。</summary>
        public const float WindowHeight = 0.5366f;
        /// <summary>卡框圆角半径，占**卡宽**的比（24 / 550）。底色块的圆角必须 ≥ 它，否则会在四角露出底色。</summary>
        public const float CornerRadius = 0.0436f;

        // ==================================================================
        // 兵种配色（提示词 3：步兵土黄 / 坦克橄榄 / 火炮灰蓝 / 战斗机浅蓝 / 轰炸机深蓝）
        // ==================================================================

        /// <summary>
        /// 开窗底色（= "插画区"的底）。刻意压到中低明度：
        /// 兵种图标是**白色剪影**，底色一亮就看不清了。
        /// </summary>
        public static Color WindowColor(UnitType kind)
        {
            switch (kind)
            {
                case UnitType.Infantry:  return new Color32(146, 112, 54, 255);   // 土黄
                case UnitType.Tank:      return new Color32(96, 110, 56, 255);    // 橄榄
                case UnitType.Artillery: return new Color32(92, 104, 122, 255);   // 灰蓝
                case UnitType.Fighter:   return new Color32(78, 130, 176, 255);   // 浅蓝
                case UnitType.Bomber:    return new Color32(48, 74, 122, 255);    // 深蓝
                default:                 return new Color32(46, 42, 70, 255);      // 指令卡
            }
        }

        /// <summary>
        /// 压在卡框上的**乘法**色调（Image.color）。
        /// 卡框本体是深灰石纹 + 金边，乘一个接近 1 的偏色能让整卡带上兵种倾向，
        /// 又不会把金边染坏。全部刻意留在 0.72~1.0 之间 —— 这是"微染"，不是"换色"。
        /// </summary>
        public static Color FrameTint(UnitType kind)
        {
            switch (kind)
            {
                case UnitType.Infantry:  return new Color(1.00f, 0.93f, 0.78f);
                case UnitType.Tank:      return new Color(0.90f, 0.97f, 0.80f);
                case UnitType.Artillery: return new Color(0.86f, 0.92f, 1.00f);
                case UnitType.Fighter:   return new Color(0.82f, 0.92f, 1.00f);
                case UnitType.Bomber:    return new Color(0.74f, 0.84f, 1.00f);
                default:                 return Color.white;
            }
        }

        /// <summary>兵种全称（给提示文案用）。</summary>
        public static string FullName(UnitType kind)
        {
            switch (kind)
            {
                case UnitType.Infantry:  return "步兵";
                case UnitType.Tank:      return "坦克";
                case UnitType.Artillery: return "火炮";
                case UnitType.Fighter:   return "战斗机";
                case UnitType.Bomber:    return "轰炸机";
                default:                 return "—";
            }
        }

        /// <summary>
        /// 兵种单字简称。**只在图标取不到时当兜底**（"零资源也能跑"这条底线不能丢）：
        /// 图标方案更贴近 Kards，但汉字语义零歧义，两者随时可切换。
        /// </summary>
        public static string ShortName(UnitType kind)
        {
            switch (kind)
            {
                case UnitType.Infantry:  return "步";
                case UnitType.Tank:      return "坦";
                case UnitType.Artillery: return "炮";
                case UnitType.Fighter:   return "战";
                case UnitType.Bomber:    return "轰";
                default:                 return "—";
            }
        }
    }
}
