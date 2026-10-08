using System.Collections.Generic;
using KardsLike.Core;

namespace KardsLike.View
{
    /// <summary>
    /// 交互层（阶段 2 · 任务 B 前置拆分，从 GamePresenter 抽出）。
    ///
    /// 职责边界：把"玩家的点击"翻译成"引擎调用"，并维护选择状态。
    ///   · 所有点击入口（手牌 / 战场单位 / 己方战场 / 敌方 HQ / 按钮）都在这里
    ///   · 校验一律问引擎（CanPlayCard / CanUnitAttack / CanTargetUnit），
    ///     自己**不复制**任何规则 —— 复制一份就会和引擎各自漂移
    ///   · 改完状态后调 host.Refresh() 重画，绝不直接改 GameState
    ///
    /// 它不持有引擎与对局数据，那些仍归 GamePresenter；这里只通过 host 访问。
    /// 这样"谁是状态的唯一持有者"始终清楚，不会出现两份 CurrentPlayer。
    /// </summary>
    internal sealed class GameInteraction
    {
        private readonly GamePresenter _host;

        // ---- 交互状态 ----
        private int _selHand = -1;      // 选中的手牌索引，-1 = 无
        private int _selUnitId = -1;    // 选中的攻击单位实例 id，-1 = 无
        private string _error = "";     // 上一次操作失败的提示，下一次交互时清空

        public GameInteraction(GamePresenter host)
        {
            _host = host;
        }

        public int SelHand { get { return _selHand; } }
        public int SelUnitId { get { return _selUnitId; } }

        /// <summary>上一次操作失败的原因。绘制层只读它，计时器等流程可以写它。</summary>
        public string Error
        {
            get { return _error; }
            set { _error = value; }
        }

        private GameEngine Engine { get { return _host.Engine; } }

        // ==================================================================
        // 状态查询
        // ==================================================================

        /// <summary>现在玩家能不能操作（不是电脑回合、没有在结算、对局没结束）。</summary>
        public bool CanInteract()
        {
            return !_host.Busy && Engine != null && !Engine.State.IsOver && Engine.State.CurrentPlayer == 0;
        }

        public CardDef SelectedHandCard()
        {
            if (_selHand < 0) return null;
            List<CardDef> hand = Engine.State.Current.Hand;
            if (_selHand >= hand.Count) return null;
            return hand[_selHand];
        }

        /// <summary>
        /// 该单位此刻是否可以被我指定为目标（指令卡的目标 / 攻击的目标）。
        ///
        /// [STAGE2B] 判定**全部转发引擎**。引入三条战线后，"打不打得到它"取决于双方各站在哪条线上：
        /// 支援阵线的单位只够得着敌方前线单位。这种规则一旦在 UI 里复写一遍，
        /// 就会出现"界面画成可打、引擎却拒绝"的两套语义 —— 阶段 4 的统计也会被污染。
        /// </summary>
        public bool IsTargetable(UnitInstance u)
        {
            if (!CanInteract()) return false;
            if (u == null) return false;
            if (u.OwnerIndex == Engine.State.CurrentPlayer) return false;

            string reason;
            CardDef card = SelectedHandCard();
            if (card != null)
            {
                if (!GameEngine.RequiresUnitTarget(card)) return false;
                return Engine.CanTargetUnit(card, u, out reason);
            }
            if (_selUnitId < 0) return false;
            return Engine.CanAttackTarget(Engine.FindUnit(_selUnitId), u, out reason);
        }

        /// <summary>该单位此刻是否可以被选为攻击者（且真的够得着某个目标）。</summary>
        public bool IsAttackable(UnitInstance u)
        {
            if (!CanInteract()) return false;
            if (u == null) return false;
            if (u.OwnerIndex != Engine.State.CurrentPlayer) return false;
            return Engine.HasAttackTarget(u);
        }

        /// <summary>[STAGE2B] 该单位此刻能否移动上前线（用来把"可移动"直接画在边框上）。</summary>
        public bool IsMovable(UnitInstance u)
        {
            if (!CanInteract()) return false;
            if (u == null) return false;
            if (u.OwnerIndex != Engine.State.CurrentPlayer) return false;
            string reason;
            return Engine.CanUnitMove(u, out reason);
        }

        public void ClearSelection()
        {
            _selHand = -1;
            _selUnitId = -1;
        }

        // ==================================================================
        // 点击处理：只做"校验 + 调引擎"，不自己改状态
        // ==================================================================

        public void OnHandCardClicked(CardView v)
        {
            if (!CanInteract()) return;
            _error = "";

            if (_selHand == v.HandIndex)
            {
                _selHand = -1;
                _host.Refresh();
                return;
            }

            _selHand = v.HandIndex;
            _selUnitId = -1;

            string reason;
            if (!Engine.CanPlayCard(_selHand, out reason))
            {
                _error = reason;   // 选中可以，但先告诉玩家为什么现在打不出去
            }
            _host.Refresh();
        }

        public void OnBoardUnitClicked(CardView v)
        {
            if (!CanInteract()) return;
            _error = "";

            // 情形一：选中的是需要目标的指令卡 → 这一下点在敌方单位上就是选目标
            CardDef card = SelectedHandCard();
            if (card != null)
            {
                if (GameEngine.RequiresUnitTarget(card))
                {
                    if (v.OwnerIndex == Engine.State.CurrentPlayer)
                    {
                        _error = "【" + card.Name + "】只能指定敌方单位";
                        _host.Refresh();
                        return;
                    }
                    TryPlayCard(_selHand, v.UnitId);
                    return;
                }
                _error = "请先点击绿色按钮确认打出，或按 Esc 取消选择";
                _host.Refresh();
                return;
            }

            // 情形二：已经选好攻击单位 → 点在敌方单位上就是发动攻击
            if (_selUnitId >= 0 && v.OwnerIndex != Engine.State.CurrentPlayer)
            {
                TryAttack(_selUnitId, v.UnitId);
                return;
            }

            // 情形三：点己方单位 → 切换为"选为攻击单位"
            if (v.OwnerIndex == Engine.State.CurrentPlayer)
            {
                // [STAGE2C] HQ 不能被选为攻击单位（它不能行动）。点在它上面只可能意味着
                // "把选中的单位卡部署下来"，所以直接走部署那条语义。
                UnitInstance own = Engine.FindUnit(v.UnitId);
                if (own != null && own.IsHq)
                {
                    OnPlayerBoardClicked();
                    return;
                }

                _selUnitId = (_selUnitId == v.UnitId) ? -1 : v.UnitId;
                _host.Refresh();
                return;
            }
        }

        /// <summary>
        /// [STAGE2C] 点**自己的 HQ 卡**：语义上等同于点"己方支援阵线的空白"。
        ///
        /// HQ 占了支援线第 0 格，玩家点在它上面时想表达的意思只可能是
        /// "把选中的单位卡部署下去"，而不是"把 HQ 选为攻击单位"。
        /// 直接转交 OnPlayerBoardClicked，保证两处行为**完全一致**（不要复制一份逻辑）。
        /// </summary>
        public void OnOwnHqClicked(CardView v)
        {
            OnPlayerBoardClicked();
        }

        /// <summary>点击己方支援阵线空白区：部署选中的单位卡。</summary>
        public void OnPlayerBoardClicked()
        {
            if (!CanInteract()) return;
            _error = "";

            CardDef card = SelectedHandCard();
            if (card == null)
            {
                _selUnitId = -1;
                _host.Refresh();
                return;
            }
            if (!card.IsUnit)
            {
                _error = "指令卡不能部署到战场，请点下方绿色按钮打出";
                _host.Refresh();
                return;
            }
            TryPlayCard(_selHand, -1);
        }

        /// <summary>
        /// [STAGE2B] 点击中央的「前线」：把选中的单位移上去。
        ///
        /// 三条战线给"部署"和"移动"各留了一个明确的落点 ——
        /// 点自己的支援阵线 = 部署，点前线 = 移动。这条分工是刻意的：
        /// 单位**不能**直接部署到前线（那是 Kards 的核心地理约束），
        /// 所以两处点击绝不能都接受同一张手牌。
        /// </summary>
        public void OnFrontlineClicked()
        {
            if (!CanInteract()) return;
            _error = "";

            CardDef card = SelectedHandCard();
            if (card != null)
            {
                _error = card.IsUnit
                    ? "单位只能部署到支援阵线，上前线要点选单位后移到前线"
                    : "指令卡请点下方绿色按钮打出";
                _host.Refresh();
                return;
            }

            if (_selUnitId < 0) { _host.Refresh(); return; }

            UnitInstance u = Engine.FindUnit(_selUnitId);
            if (u == null)
            {
                _error = "选择的单位已不在场上";
                ClearSelection();
                _host.Refresh();
                return;
            }
            if (u.OwnerIndex != Engine.State.CurrentPlayer)
            {
                _error = "只能移动自己的单位";
                _host.Refresh();
                return;
            }
            // 已经在最前线的单位：点前线等于"取消选择"，不该弹一条错误
            if (u.Line == Line.Front)
            {
                ClearSelection();
                _host.Refresh();
                return;
            }

            string reason;
            if (!Engine.CanUnitMove(u, out reason))
            {
                _error = reason;
                _host.Refresh();
                return;
            }

            _error = "";
            Engine.MoveUnit(_selUnitId);
            ClearSelection();
            _host.Refresh();
        }

        /// <summary>点击敌方 HQ / 敌方战场空白区：打脸，或提示该指令不能打脸。</summary>
        public void OnEnemyHqClicked()
        {
            if (!CanInteract()) return;
            _error = "";

            CardDef card = SelectedHandCard();
            if (card != null)
            {
                if (GameEngine.RequiresUnitTarget(card))
                {
                    _error = "【" + card.Name + "】只能指定单位为目标，不能指定 HQ";
                    _host.Refresh();
                    return;
                }
                _error = "请先点击绿色按钮确认打出，或按 Esc 取消选择";
                _host.Refresh();
                return;
            }

            if (_selUnitId >= 0) { TryAttack(_selUnitId, -1); return; }

            _host.Refresh();
        }

        public void OnPlayOrderClicked()
        {
            if (!CanInteract()) return;
            _error = "";

            CardDef card = SelectedHandCard();
            if (card == null) { _host.Refresh(); return; }
            if (card.IsUnit)
            {
                _error = "单位卡请点击己方战场部署";
                _host.Refresh();
                return;
            }
            TryPlayCard(_selHand, -1);
        }

        public void OnEndTurnClicked()
        {
            if (!CanInteract()) return;
            _error = "";
            ClearSelection();
            _host.EndTurnAndRunAi();
        }

        public void OnRestartClicked()
        {
            _host.RestartGame();
        }

        // ==================================================================
        // 实际操作：先校验，再交给引擎；失败时只把原因显示出来
        // ==================================================================

        private void TryPlayCard(int handIndex, int targetUnitId)
        {
            string reason;
            if (!Engine.CanPlayCard(handIndex, out reason))
            {
                _error = reason;
                _host.Refresh();
                return;
            }

            CardDef card = Engine.State.Current.Hand[handIndex];
            if (GameEngine.RequiresUnitTarget(card))
            {
                // [FIX-1] 目标合法性由引擎判定，UI 只负责把引擎给的原因显示出来。
                // 之前这里自己写了一遍"必须打敌方"的规则，和引擎的语义会各自漂移。
                UnitInstance target = Engine.FindUnit(targetUnitId);
                string targetReason;
                if (!Engine.CanTargetUnit(card, target, out targetReason))
                {
                    _error = targetReason;
                    _host.Refresh();
                    return;
                }
            }

            _error = "";
            Engine.PlayCard(handIndex, targetUnitId);
            ClearSelection();
            _host.Refresh();
        }

        private void TryAttack(int attackerId, int targetUnitId)
        {
            UnitInstance attacker = Engine.FindUnit(attackerId);
            string reason;
            if (!Engine.CanUnitAttack(attacker, out reason))
            {
                _error = reason;
                ClearSelection();
                _host.Refresh();
                return;
            }

            // [STAGE2B] 目标合法性同样交给引擎：线位规则（支援阵线单位只够得着敌方前线单位）
            // 与"只有前线单位能打 HQ"都住在引擎里，这里只把原因显示出来。
            if (targetUnitId >= 0)
            {
                UnitInstance target = Engine.FindUnit(targetUnitId);
                if (!Engine.CanAttackTarget(attacker, target, out reason))
                {
                    _error = reason;
                    _host.Refresh();
                    return;
                }
            }
            else if (!Engine.CanAttackHq(attacker, out reason))
            {
                _error = reason;
                _host.Refresh();
                return;
            }

            _error = "";
            Engine.AttackWithUnit(attackerId, targetUnitId);
            ClearSelection();
            _host.Refresh();
        }
    }
}
