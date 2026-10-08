using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KardsLike.View
{
    /// <summary>
    /// 界面工具层：调色板、字体、程序化生成的圆角贴图、以及一批建 UI 的工厂函数。
    ///
    /// 为什么圆角贴图要在运行时生成：
    ///   1. 整个 MVP 不依赖任何美术资源，只靠代码就能跑出像样的界面；
    ///   2. 用 Image.color 给白色的圆角贴图上色，一张贴图能复用到所有同尺寸元素上。
    ///
    /// 贴图按 (宽, 高, 圆角半径) 缓存。因为每次刷新都会重建卡牌，
    /// 没有缓存的话会不断泄漏 Texture2D —— 这是运行时建 UI 最容易踩的坑。
    /// </summary>
    public static class UiKit
    {
        // ==================================================================
        // 调色板（深色军事风，与 URP 2D 场景的暗背景协调）
        // ==================================================================
        public static readonly Color Bg            = new Color32(15, 19, 26, 255);
        public static readonly Color PanelFill     = new Color32(30, 37, 48, 255);
        public static readonly Color PanelBorder   = new Color32(58, 72, 90, 255);
        public static readonly Color CardUnitSelf  = new Color32(38, 52, 68, 255);
        public static readonly Color CardUnitEnemy = new Color32(60, 40, 46, 255);
        public static readonly Color CardOrder     = new Color32(46, 42, 70, 255);
        public static readonly Color Accent        = new Color32(201, 165, 74, 255);
        public static readonly Color TextMain      = new Color32(233, 239, 246, 255);
        public static readonly Color TextDim       = new Color32(146, 160, 178, 255);
        public static readonly Color TextDark      = new Color32(20, 24, 30, 255);
        public static readonly Color AttackColor   = new Color32(233, 172, 56, 255);
        public static readonly Color HealthColor   = new Color32(206, 80, 80, 255);
        public static readonly Color HealthLow     = new Color32(150, 40, 40, 255);
        public static readonly Color SelectColor   = new Color32(94, 202, 255, 255);
        public static readonly Color TargetColor   = new Color32(255, 92, 92, 255);
        public static readonly Color LegalColor    = new Color32(104, 214, 142, 255);
        public static readonly Color WinColor      = new Color32(104, 214, 142, 255);
        public static readonly Color LoseColor     = new Color32(214, 100, 100, 255);

        // ==================================================================
        // 字体
        // ==================================================================
        private static TMP_FontAsset _font;

        /// <summary>
        /// 中文字体。优先从 Resources 读，读不到就退回 TMP 的默认字体设置。
        /// 两级回退是为了"不管哪一环配置漏了，界面不会变成一排空白方块"。
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<TMP_FontAsset>("Fonts/SimHei SDF");
                    if (_font == null) _font = TMP_Settings.defaultFontAsset;
                }
                return _font;
            }
        }

        // ==================================================================
        // 贴图缓存
        // ==================================================================
        private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private static Sprite _solid;

        /// <summary>纯白 4x4 实心贴图。用于不需要圆角的背景。</summary>
        public static Sprite Solid
        {
            get
            {
                if (_solid == null)
                {
                    Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Color32[] px = new Color32[16];
                    for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    _solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                }
                return _solid;
            }
        }

        /// <summary>
        /// 生成（并缓存）一张指定尺寸的白色圆角贴图。
        /// 白色是为了让 Image.color 能把它染成任意颜色。
        /// </summary>
        public static Sprite Rounded(int width, int height, int radius)
        {
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(width, height) / 2);

            string key = width + "x" + height + "r" + radius;
            Sprite cached;
            if (_spriteCache.TryGetValue(key, out cached) && cached != null) return cached;

            Color32[] px = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = Coverage(x + 0.5f, y + 0.5f, width, height, radius);
                    px[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(px);
            tex.Apply();

            Sprite s = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            _spriteCache[key] = s;
            return s;
        }

        /// <summary>
        /// 生成（并缓存）一张白色圆环贴图，供环形进度条使用（阶段 2 · 任务 A 的倒计时）。
        ///
        /// 用法：img.sprite = Ring(...)；img.type = Image.Type.Filled；
        ///       img.fillMethod = Radial360；img.fillOrigin = Top；img.fillAmount = 剩余比例。
        /// 这样只要改 fillAmount 就能画出"时间正在流失"的圆环，不需要任何美术资源。
        ///
        /// 为什么单独写一个而不是复用 Rounded：
        ///   Rounded 走的是有符号距离场（圆角矩形），画不出"中间是空的"环带。
        /// </summary>
        public static Sprite Ring(int diameter, int thickness)
        {
            if (diameter <= 0) diameter = 1;
            thickness = Mathf.Clamp(thickness, 1, Mathf.Max(1, diameter / 2));

            string key = "ring" + diameter + "t" + thickness;
            Sprite cached;
            if (_spriteCache.TryGetValue(key, out cached) && cached != null) return cached;

            float center = diameter * 0.5f;
            float outer = diameter * 0.5f;
            float half = thickness * 0.5f;
            float mid = outer - half;      // 环带中线的半径

            Color32[] px = new Color32[diameter * diameter];
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // 到环带中线的距离 → 内外两条边都拿到 1 像素抗锯齿
                    float a = Mathf.Clamp01(half + 0.5f - Mathf.Abs(d - mid));
                    // 再裁掉贴图最外圈，避免采样溢出到贴图外
                    a *= Mathf.Clamp01(outer - d + 0.5f);
                    px[y * diameter + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            Texture2D tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(px);
            tex.Apply();

            Sprite s = Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
            _spriteCache[key] = s;
            return s;
        }

        /// <summary>
        /// 圆角矩形的有符号距离场覆盖率（0 = 完全在外，1 = 完全在内）。
        /// 用距离场而不是"逐像素判断在不在圆内"，是为了拿到 1 像素的抗锯齿边缘 ——
        /// 直接二值化会让圆角看起来全是锯齿。
        /// </summary>
        private static float Coverage(float px, float py, float w, float h, float r)
        {
            float halfW = w * 0.5f;
            float halfH = h * 0.5f;
            float qx = Mathf.Abs(px - halfW) - (halfW - r);
            float qy = Mathf.Abs(py - halfH) - (halfH - r);
            float dx = Mathf.Max(qx, 0f);
            float dy = Mathf.Max(qy, 0f);
            float outside = Mathf.Sqrt(dx * dx + dy * dy);
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            float sd = outside + inside - r;
            return Mathf.Clamp01(0.5f - sd);
        }

        // ==================================================================
        // 布局工厂
        // ==================================================================

        /// <summary>建一个只有 RectTransform 的空节点。</summary>
        public static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            return rt;
        }

        /// <summary>建一个铺满父节点的节点。</summary>
        public static RectTransform Stretch(string name, Transform parent, float inset = 0f)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>
        /// 建一个"锚点 + 轴心 + 偏移 + 尺寸"定位的节点。
        /// 这是最常用的定位方式：anchor 决定参考点，pivot 决定自身对齐方式。
        /// </summary>
        public static RectTransform Anchored(string name, Transform parent,
                                             Vector2 anchor, Vector2 pivot,
                                             Vector2 position, Vector2 size)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>给节点挂一张圆角图，尺寸取自节点自身的 sizeDelta。radius 传 0 表示直角。</summary>
        public static Image AddImage(RectTransform rt, Color color, int radius)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(rt.sizeDelta.x));
            int h = Mathf.Max(1, Mathf.RoundToInt(rt.sizeDelta.y));
            return AddImageSized(rt, color, radius, w, h);
        }

        /// <summary>
        /// 给节点挂一张圆角图，但用外部指定的尺寸去生成贴图。
        /// 用于"锚点拉伸"的节点 —— 这类节点的 sizeDelta 是负数或 0，
        /// 拿它当贴图尺寸会得到一张 1x1 的图，圆角就没了。
        /// </summary>
        public static Image AddImageSized(RectTransform rt, Color color, int radius, int width, int height)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            if (radius > 0)
            {
                img.sprite = Rounded(Mathf.Max(1, width), Mathf.Max(1, height), radius);
                img.type = Image.Type.Simple;
            }
            else
            {
                img.sprite = Solid;
            }
            img.color = color;
            // 默认关掉射线检测：装饰性的图不该拦截点击。
            // 需要接收点击的元素（按钮、可点的卡牌）由 AddButton 重新打开。
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 给节点挂一张**指定的**贴图，不走圆角生成。
        /// 用于"贴图本身就是形状"的元素，目前只有倒计时的环形进度（Ring）。
        /// </summary>
        public static Image AddSprite(RectTransform rt, Sprite sprite, Color color)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 给节点挂一段文字。
        /// 默认关闭 raycastTarget：文字是纯显示元素，不该挡住下面的按钮点击。
        /// </summary>
        public static TextMeshProUGUI AddLabel(RectTransform rt, string text, float size, Color color,
                                               TextAlignmentOptions align,
                                               FontStyles style = FontStyles.Normal,
                                               bool wrap = false)
        {
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>给节点挂一个按钮。target 是接收点击的图形（必须有 raycastTarget）。</summary>
        public static Button AddButton(RectTransform rt, Graphic target, Color normal, Color highlighted)
        {
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = target;
            target.raycastTarget = true;
            ColorBlock cb = b.colors;
            cb.normalColor = normal;
            cb.highlightedColor = highlighted;
            cb.pressedColor = highlighted * 0.85f;
            cb.selectedColor = normal;
            cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            return b;
        }

        /// <summary>
        /// 建一个"面板"：外框 + 内填充（双层圆角图做出描边效果）。
        /// 返回的 GameObject 是外框节点。
        /// </summary>
        public static RectTransform Panel(string name, Transform parent,
                                          Vector2 anchor, Vector2 pivot,
                                          Vector2 position, Vector2 size,
                                          Color fill, Color border, int radius = 12)
        {
            RectTransform outer = Anchored(name, parent, anchor, pivot, position, size);
            AddImage(outer, border, radius);

            RectTransform inner = Stretch("Fill", outer, 2f);
            // 拉伸节点的 sizeDelta 是 (-4,-4)，不能直接当贴图尺寸用，所以要显式传入
            AddImageSized(inner, fill, Mathf.Max(0, radius - 1),
                          Mathf.RoundToInt(size.x) - 4, Mathf.RoundToInt(size.y) - 4);
            return outer;
        }
    }
}
