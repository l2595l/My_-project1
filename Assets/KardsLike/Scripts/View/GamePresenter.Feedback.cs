using System.Collections.Generic;
using KardsLike.Core;
using UnityEngine;

namespace KardsLike.View
{
    /// <summary>
    /// 战斗反馈层（阶段 2.5 · 任务 C 步骤 ①，从 <see cref="GamePresenter"/> 拆出）。
    ///
    /// 为什么拆：主文件在任务 C 之前已经 664 行，本轮加完飘字到了 905 行 ——
    /// 而任务 C 还剩 ② 指令卡动画 / ③ 移动动画 / ④ 攻击特效三步，全都属于这一块。
    /// 与其等到越过 1100 行阈值再拆（`GameEngine.cs` 就是这么挂了三轮 P0 的），
    /// 不如在**动它之前**先拆开 —— 这是本项目已经用过的做法。
    ///
    /// 边界：这里放「战斗反馈」的**全部状态与驱动** ——
    ///   · 动画时长倍率的档位与切换
    ///   · 伤害事件的采集（两帧血量相减）与播放（飘字 + 抖动）
    /// 界面怎么画、回合怎么走，仍然在 `GamePresenter.cs`。
    /// 两个文件是同一个类的 partial，**没有新增任何运行时对象**（不是第二个组件）。
    /// </summary>
    public sealed partial class GamePresenter
    {
        // ==================================================================
        // [阶段2.5 · 任务C-①] 战斗反馈：动画倍率 + 伤害事件
        // ==================================================================

        /// <summary>
        /// 动画时长倍率的档位（提示词 4 的硬约束 3：**必须有 0 = 立即完成**）。
        /// 没有它，以后每改一次代码都要在动画里干等，调试体验会崩。
        /// 用档位而不是连续值，是为了"按一下就到下一个可辨认的速度"，不用调半天。
        /// </summary>
        private static readonly float[] AnimSpeedSteps = new float[] { 1f, 0.5f, 0.25f, 0f };

        /// <summary>
        /// 动画时长倍率的档位索引（0 → ×1，1 → ×0.5，2 → ×0.25，3 → 关闭）。
        /// 默认 0 = 原速。Play 中按 **Y** 键循环切换；也可在 Inspector 里直接改。
        /// 当前档位同时显示在右上角"随机种子"那一行（`动画 ×1`），否则按了键没有反馈。
        /// </summary>
        [Tooltip("动画时长倍率档位：0=×1（原速，默认）  1=×0.5  2=×0.25  3=关闭动画。Play 中按 Y 键循环。")]
        [SerializeField] private int _animSpeedIndex;

        /// <summary>
        /// 上一帧每个单位实例的血量（id → HP）。
        ///
        /// **这就是伤害数字的全部数据来源**：引擎状态在动画开始前就已经改完
        /// （硬约束 2「先结算、后播放」），所以这里只做一次"两帧相减"，
        /// 不去推算任何规则 —— 谁的血掉了、掉了多少，是"读结果"，不是"重算规则"。
        ///
        /// 这么做顺带把三条验收标准变成了**构造上必然成立**：
        ///   · 反击为 0（打 HQ / 火炮免反击 / 轰炸机免反击）⇒ 攻击者血量没变 ⇒ 自然不弹
        ///   · 双方都掉血 ⇒ 两张卡各弹一个（"双向"）
        ///   · 打 HQ ⇒ 只有 HQ 掉血 ⇒ 只有 HQ 弹
        /// 若改成"在 View 里算反击伤害"，就必须把四条兵种判定抄第二遍，
        /// 那正是本项目已经栽过两次的"规则有两个真相来源"。
        /// </summary>
        private Dictionary<int, int> _hpPrev = new Dictionary<int, int>();
        private Dictionary<int, int> _hpCur = new Dictionary<int, int>();
        private bool _damageBaselineReady;

        /// <summary>这一帧新出现的伤害事件（与 _damageAmounts 一一对应）。</summary>
        private readonly List<int> _damagedIds = new List<int>();
        private readonly List<int> _damageAmounts = new List<int>();

        /// <summary>
        /// 帧首记下的"动作发生前"每个单位卡的**设计画框坐标**（特效层局部坐标）。
        /// 两个地方要用它，都必须赶在旧卡被销毁之前算好：
        ///   · 单位被打死 ⇒ 飘字只能打在它**原来**的位置上
        ///   · 单位移上前线 ⇒ 新卡要**从旧位置滑过去**（旧节点那时已经没了，只剩坐标）
        ///
        /// 用"设计画框坐标"而不是世界坐标，是为了让**采集**这一步与设计缩放无关 ——
        /// 一个落点，两处（飘字 / 滑动）都能直接用；真要世界坐标时再在**同一帧**里换算回去。
        ///
        /// [更正] 早期曾把这里记成"存世界坐标会带上百分之几的缩放误差"，**那是错的**：
        /// 实测到的 1.8% 偏差其实是"动画已推进一帧"（`smoothstep(0.02/0.25) = 1/55`），
        /// 世界坐标版本本身是精确的。改用设计坐标只是为了两处共用一份采集，
        /// **不是因为后者有错** —— 别再照着一个不存在的问题去"修"它。
        /// </summary>
        private readonly Dictionary<int, Vector2> _preActionPos = new Dictionary<int, Vector2>();

        /// <summary>本帧版面上每个单位卡（id → 卡节点）。每帧重建时重填。</summary>
        private readonly Dictionary<int, RectTransform> _unitRects = new Dictionary<int, RectTransform>();

        // ==================================================================
        // [阶段2.5 · 任务C-②] 指令卡打出动画：数据来源
        //
        // 同样是"读引擎改完的状态"，不重算规则 —— 这次读的是**手牌**。
        // 引擎里唯一会从手牌拿走一张牌的地方是 `PlayCard` 的 `Hand.RemoveAt`；
        // 抽牌（含满手爆牌）只会往手牌里**加**（爆牌是"抽到就丢掉"，手牌不变）。
        // ⇒ **手牌少了一张 = 打出了一张牌**，再看它 IsOrder 就知道要不要播动画。
        //
        // 为什么不用"弃牌堆变长"来判断：弃牌堆同时也会收到**爆牌**丢掉的那张，
        // 那张根本没被打出过 —— 用它判断会偶尔凭空播一张牌。手牌差值没有这个歧义。
        // ==================================================================

        /// <summary>上一帧两个玩家手牌的并集（按 CardDef 引用计数）。</summary>
        private Dictionary<CardDef, int> _handPrev = new Dictionary<CardDef, int>();
        private Dictionary<CardDef, int> _handCur = new Dictionary<CardDef, int>();
        private bool _orderBaselineReady;

        // ==================================================================
        // [阶段2.5 · 任务C-①] 动画倍率开关（Y 键循环：×1 → ×0.5 → ×0.25 → 关闭）
        // ==================================================================

        private float CurrentAnimSpeed { get { return AnimSpeedSteps[_animSpeedIndex]; } }

        /// <summary>把当前档位下发到特效层。倍率 0 时特效层直接不创建任何对象。</summary>
        private void ApplyAnimSpeed()
        {
            if (_layout.Effects != null) _layout.Effects.Speed = CurrentAnimSpeed;
            UpdateSeedText();
        }

        /// <summary>种子行顺带显示当前动画倍率 —— 否则按了 Y 键没有任何可见反馈。</summary>
        private void UpdateSeedText()
        {
            if (_layout.SeedText == null) return;
            _layout.SeedText.text = "随机种子 " + _seed + " · 动画 ×" + AnimSpeedLabel(CurrentAnimSpeed);
        }

        private static string AnimSpeedLabel(float speed)
        {
            if (speed <= 0f) return "关";
            return speed.ToString("0.##");
        }

        private void CycleAnimSpeed()
        {
            _animSpeedIndex = (_animSpeedIndex + 1) % AnimSpeedSteps.Length;
            ApplyAnimSpeed();
            Debug.Log("[AnimSpeed] 动画倍率 → ×" + AnimSpeedLabel(CurrentAnimSpeed) +
                      "（Y 键继续切换；0 = 关闭动画、立即完成）");
        }

        /// <summary>
        /// 把战斗反馈的全部中间状态清空，并把两个"基准快照"都标记为未就绪。
        ///
        /// 职责上刻意跟着**字段**走：反馈状态住在这个 partial 里，复位也就住在这里。
        /// 漏掉任何一个标志的后果都是"重开一局时弹出一片不存在的伤害数字/动画" ——
        /// 因为旧局的单位与手牌在新局里统统"消失"了，差值算法会把它们全判成刚刚发生。
        /// </summary>
        private void ResetFeedbackTracking()
        {
            // 伤害追踪（任务 C 步骤①）
            _hpPrev.Clear();
            _hpCur.Clear();
            _damagedIds.Clear();
            _damageAmounts.Clear();
            _preActionPos.Clear();
            _unitRects.Clear();
            _damageBaselineReady = false;

            // 指令卡打出追踪（任务 C 步骤②）
            _handPrev.Clear();
            _handCur.Clear();
            _orderBaselineReady = false;

            // 单位移动追踪（任务 C 步骤③）
            _linePrev.Clear();
            _lineCur.Clear();
            _movedIds.Clear();
            _moveBaselineReady = false;
        }

        // ==================================================================
        // [阶段2.5 · 任务C-①] 双向伤害飘字：数据采集
        //
        // 设计要点：**不在 View 里算伤害**。
        //   引擎把状态改完之后，这里只做"两帧相减"。
        //   于是"反击为 0 就不弹攻击者"这条要求**不需要写任何判断** ——
        //   攻击者的血没少，差值就是 0，自然没有事件。
        //   若在这里按兵种重算一遍反击，就是把 Core 的四条判定抄第二遍
        //   （阶段 2.5 任务 A 刚刚才把它收口到 CounterDamage 一个出口）。
        // ==================================================================

        /// <summary>把某一方场上全部单位（含 HQ）的血量写进快照。</summary>
        private static void SnapshotHealth(GameState s, Dictionary<int, int> dst)
        {
            dst.Clear();

            List<UnitInstance> front = s.Frontline;
            for (int i = 0; i < front.Count; i++) dst[front[i].InstanceId] = front[i].Health;

            for (int p = 0; p < s.Players.Length; p++)
            {
                // 支援阵线第 0 格就是 HQ（它也是一个真实单位），所以这一圈把 HQ 一起覆盖了
                List<UnitInstance> sup = s.Players[p].SupportLine;
                for (int i = 0; i < sup.Count; i++) dst[sup[i].InstanceId] = sup[i].Health;
            }
        }

        /// <summary>
        /// 比较"上一帧的血量"与"这一帧的血量"，得出这一帧的伤害事件。
        ///
        /// 两类都要收：
        ///   · 还在场上、但血变少了的 —— 掉血量 = 差值
        ///   · 上一帧还在、这一帧不见了的 —— 说明这次被打死了，掉血量 = 它当时的血
        ///     （引擎的伤害可以溢出，但玩家关心的是"它掉了多少"，所以用死亡前的血量）
        /// 新上场的单位在上一帧没有记录，自然被忽略。
        /// </summary>
        private void CollectDamageEvents()
        {
            SnapshotHealth(_engine.State, _hpCur);

            _damagedIds.Clear();
            _damageAmounts.Clear();

            if (_damageBaselineReady)
            {
                foreach (KeyValuePair<int, int> kv in _hpCur)
                {
                    int before;
                    if (!_hpPrev.TryGetValue(kv.Key, out before)) continue;   // 这一帧新上场的
                    int delta = before - kv.Value;
                    if (delta > 0) { _damagedIds.Add(kv.Key); _damageAmounts.Add(delta); }
                }

                foreach (KeyValuePair<int, int> kv in _hpPrev)
                {
                    if (_hpCur.ContainsKey(kv.Key)) continue;                 // 还在场上
                    if (kv.Value <= 0) continue;                              // 之前就是死的，不该有
                    _damagedIds.Add(kv.Key);
                    _damageAmounts.Add(kv.Value);
                }
            }
            else
            {
                // 第一帧（或刚重开）：只建立基准，不产生任何伤害事件
                _damageBaselineReady = true;
            }

            // 交换快照：这一帧成为下一帧的"上一帧"
            Dictionary<int, int> swap = _hpPrev;
            _hpPrev = _hpCur;
            _hpCur = swap;
        }

        /// <summary>
        /// 帧首把当前版面上每张单位卡的中心点换算成特效层的局部坐标。
        /// 只有**还活着**的卡能算（卡一被销毁，位置就不可靠了），所以必须在刷新前调。
        /// </summary>
        private void CapturePreActionAnchors()
        {
            _preActionPos.Clear();
            if (_layout.Effects == null) return;

            foreach (KeyValuePair<int, RectTransform> kv in _unitRects)
            {
                if (kv.Value == null) continue;
                _preActionPos[kv.Key] = _layout.Effects.LocalOf(kv.Value);   // 设计画框坐标
            }
        }

        /// <summary>
        /// 按本帧收集到的伤害事件播放反馈：受伤的卡弹数字 + 抖一下。
        /// 还活着的用**现在**的位置（版面可能已经重排）；已经阵亡的用帧首存下的旧位置。
        /// </summary>
        private void PlayDamageFloats()
        {
            CardEffectLayer fx = _layout.Effects;
            if (fx == null || _damagedIds.Count == 0) return;

            for (int i = 0; i < _damagedIds.Count; i++)
            {
                int id = _damagedIds[i];
                int amount = _damageAmounts[i];

                RectTransform rt;
                if (_unitRects.TryGetValue(id, out rt) && rt != null)
                {
                    fx.DamageAt(fx.LocalOf(rt), amount);
                    fx.Shake(rt);
                    continue;
                }

                // 阵亡：卡已经不在了，只能打在它倒下的地方
                Vector2 fallback;
                if (_preActionPos.TryGetValue(id, out fallback)) fx.DamageAt(fallback, amount);
            }
        }

        // ==================================================================
        // [阶段2.5 · 任务C-③] 单位移动动画：数据来源
        //
        // 还是同一个套路 —— 读引擎改完的状态。这次读的是**单位所在战线**：
        // `UnitInstance.Line` 只有"支援线 → 前线"这一个方向（单位不能退回），
        // 所以"上一帧在支援线、这一帧在前线"就等价于"刚刚移上前线"。
        // ==================================================================

        /// <summary>上一帧每个单位所在的战线（0 = 支援线，1 = 前线）。</summary>
        private Dictionary<int, int> _linePrev = new Dictionary<int, int>();
        private Dictionary<int, int> _lineCur = new Dictionary<int, int>();
        private readonly List<int> _movedIds = new List<int>();
        private bool _moveBaselineReady;


        /// <summary>
        /// [任务C-②] 比较前后两帧的手牌，得出这一帧"被打出的牌"，指令卡就去播动画。
        /// 判据只有一条：**上一帧还有、这一帧少了的牌**（见本节顶部的注释）。
        /// </summary>
        private void CollectOrderPlays()
        {
            CardEffectLayer fx = _layout.Effects;
            if (fx == null) return;

            GameState s = _engine.State;

            _handCur.Clear();
            for (int p = 0; p < s.Players.Length; p++)
            {
                List<CardDef> hand = s.Players[p].Hand;
                for (int i = 0; i < hand.Count; i++)
                {
                    CardDef d = hand[i];
                    if (d == null) continue;
                    int n;
                    _handCur.TryGetValue(d, out n);
                    _handCur[d] = n + 1;
                }
            }

            if (_orderBaselineReady)
            {
                foreach (KeyValuePair<CardDef, int> kv in _handPrev)
                {
                    int now;
                    _handCur.TryGetValue(kv.Key, out now);
                    int lost = kv.Value - now;
                    if (lost <= 0) continue;

                    // 同一张卡在一帧里被打出多张（只在脚本驱动下可能）就播几张
                    for (int k = 0; k < lost; k++)
                    {
                        if (kv.Key.IsOrder) fx.ShowOrderCard(kv.Key);
                    }
                }
            }
            else
            {
                _orderBaselineReady = true;
            }

            // 交换：这一帧的手牌成为下一帧的"上一帧"
            Dictionary<CardDef, int> swap = _handPrev;
            _handPrev = _handCur;
            _handCur = swap;
        }

        /// <summary>把场上每个单位所在的战线写进快照（含 HQ，它永远在支援线）。</summary>
        private static void SnapshotLines(GameState s, Dictionary<int, int> dst)
        {
            dst.Clear();

            List<UnitInstance> front = s.Frontline;
            for (int i = 0; i < front.Count; i++) dst[front[i].InstanceId] = (int)front[i].Line;

            for (int p = 0; p < s.Players.Length; p++)
            {
                List<UnitInstance> sup = s.Players[p].SupportLine;
                for (int i = 0; i < sup.Count; i++) dst[sup[i].InstanceId] = (int)sup[i].Line;
            }
        }

        /// <summary>
        /// [任务C-③] 比较前后两帧的单位战线，得出"这一帧移上前线"的单位。
        /// 只收 **支援线 → 前线** 这一个方向（反向不存在），所以判据就是"值变大了"。
        /// </summary>
        private void CollectMoveEvents()
        {
            SnapshotLines(_engine.State, _lineCur);

            _movedIds.Clear();

            if (!_moveBaselineReady)
            {
                _moveBaselineReady = true;
            }
            else
            {
                foreach (KeyValuePair<int, int> kv in _lineCur)
                {
                    int before;
                    if (!_linePrev.TryGetValue(kv.Key, out before)) continue;   // 这一帧新上场的
                    if (kv.Value > before) _movedIds.Add(kv.Key);
                }
            }

            Dictionary<int, int> swap = _linePrev;
            _linePrev = _lineCur;
            _lineCur = swap;
        }

        /// <summary>
        /// [任务C-③] 播放移动动画：把新卡挪回它**动作发生前的位置**，再平滑滑到布局位置。
        /// 必须在版面重建**之后**调 —— 那时才有卡节点、也才知道终点在哪。
        /// </summary>
        private void PlayMoveSlides()
        {
            CardEffectLayer fx = _layout.Effects;
            if (fx == null || _movedIds.Count == 0) return;

            for (int i = 0; i < _movedIds.Count; i++)
            {
                RectTransform rt;
                if (!_unitRects.TryGetValue(_movedIds[i], out rt) || rt == null) continue;

                Vector2 design;
                if (!_preActionPos.TryGetValue(_movedIds[i], out design)) continue;

                // 设计坐标 → 世界坐标 →（由 SlideFrom 转到新父节点下）。
                // 两步都在**同一帧**里做，缩放自然约掉，起点是精确的。
                Vector3 world = fx.transform.TransformPoint(new Vector3(design.x, design.y, 0f));
                fx.SlideFrom(rt, world);
            }
        }
    }
}
