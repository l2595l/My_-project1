using System.Collections;
using TMPro;
using UnityEngine;

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
        /// <summary>伤害数字上飘的距离（设计画框单位）。</summary>
        public const float FloatRise = 56f;
        /// <summary>抖动幅度（设计画框单位）。</summary>
        public const float ShakeAmplitude = 6f;

        private const float FloatFontSize = 46f;
        private const float FloatWidth = 150f;
        private const float FloatHeight = 64f;

        private float _speed = 1f;

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
        /// 把一个 RectTransform 的中心点换算成本层的局部坐标。
        /// **必须在目标还活着的时候调** —— 卡被销毁后算出来的位置不可靠。
        /// </summary>
        public Vector2 LocalOf(RectTransform target)
        {
            if (target == null) return Vector2.zero;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[2]) * 0.5f;

            // 本层铺满 DesignFrame 且轴心在正中，所以换算出来的局部坐标
            // 就等于"设计画框坐标"（原点在正中心）。
            Vector3 local = transform.InverseTransformPoint(center);
            return new Vector2(local.x, local.y);
        }

        /// <summary>停掉所有动画并销毁残留的特效对象（重开一局时调用）。</summary>
        public void ClearAll()
        {
            StopAllCoroutines();

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
    }
}
