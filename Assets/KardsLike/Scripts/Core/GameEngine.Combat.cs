using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// 对局引擎 · 战斗与线位（设计文档 §6.1 / §6.2 / §7.1）。
    ///
    /// [阶段2.5 · 提示词 1] 从 GameEngine.cs 拆出（**纯结构调整，规则一行未改**）。
    /// 本文件包含：可行动校验 / 移动 / 攻击 / 目标配对 / 伤害与死亡清理 / 单位查询辅助。
    /// </summary>
    public sealed partial class GameEngine
    {
        // ==================================================================
        // 攻击（设计文档 §6.1 / §6.2）
        // ==================================================================

        /// <summary>
        /// [STAGE2B] 单位"能不能行动"（移动或攻击都算）的公共前置校验。
        ///
        /// 把四条约束抽成一个函数，是为了让 MoveUnit 与 AttackWithUnit **不可能**各自漂移出
        /// 两套语义 —— 这是阶段 2 决定"移动与攻击付出相同代价"之后最容易写歪的地方：
        ///   · HQ 不能行动（它永远不动，是靶子不是棋子）[STAGE2C]
        ///   · 部署失调（当回合刚部署，不能移动也不能攻击）
        ///   · 每单位每回合只能行动一次（移动与攻击共用）
        ///   · 资源够不够（行动费按部署费用分 1/2/3 档，与部署共用同一个资源池）[STAGE2C]
        /// 归属与对局状态也在这里一起挡掉。
        /// </summary>
        private bool CanUnitAct(UnitInstance u, out string reason)
        {
            reason = "";
            if (State.IsOver) { reason = "对局已结束"; return false; }
            if (u == null) { reason = "单位不存在"; return false; }
            if (!u.IsAlive) { reason = "单位已阵亡"; return false; }
            // [STAGE2C] HQ 也在支援阵线上，但它永远不动、也不攻击。挡在这里，
            // 于是 CanUnitAttack / CanUnitMove / HasAttackTarget / GetMovableUnits
            // 全都**自动**把 HQ 排除掉，不必挨个补判断。
            if (u.IsHq) { reason = "HQ 不能行动"; return false; }
            if (u.OwnerIndex != State.CurrentPlayer) { reason = "不是你的单位"; return false; }

            if (u.DeployedThisTurn) { reason = "本回合刚部署，处于部署失调"; return false; }
            if (u.HasOperatedThisTurn) { reason = "该单位本回合已经行动过"; return false; }

            // [STAGE2C] 行动费从**资源**里出，与部署共用同一笔预算（见 §2.7）。
            int cost = ActionCostOf(u.Def);
            int left = State.Players[u.OwnerIndex].Resources;
            if (left < cost)
            {
                reason = "资源不足（行动需要 " + cost + "，剩余 " + left + "）";
                return false;
            }
            return true;
        }

        /// <summary>判断某个单位当前能否发动攻击。</summary>
        public bool CanUnitAttack(UnitInstance u, out string reason)
        {
            return CanUnitAct(u, out reason);
        }

        /// <summary>[STAGE2B] 这个单位此刻有没有**任何一个**合法攻击目标。
        /// 渲染层用它把"能动但够不着任何人"的单位画成不可攻击，否则玩家点了没反应会更困惑。</summary>
        public bool HasAttackTarget(UnitInstance u)
        {
            string reason;
            if (!CanUnitAct(u, out reason)) return false;
            if (u.Line == Line.Front) return true;   // 前线单位至少够得着敌方 HQ
            return FindFrontlineTargetFor(u) != null;
        }

        /// <summary>[STAGE2B] 支援阵线的单位能打谁：**只有**敌方前线单位。
        /// 注意"敌方前线"等价于"前线被对方占领"，所以这里先比归属再扫列表。</summary>
        private UnitInstance FindFrontlineTargetFor(UnitInstance u)
        {
            if (State.FrontlineOwner != 1 - u.OwnerIndex) return null;
            for (int i = 0; i < State.Frontline.Count; i++)
            {
                if (State.Frontline[i].IsAlive) return State.Frontline[i];
            }
            return null;
        }

        /// <summary>
        /// [STAGE2B] 攻击方与目标单位的配对是否合法（三条战线的攻击关系，设计文档 §7.1）：
        ///   · 前线单位   → 敌方前线 / 敌方支援阵线 / 敌方 HQ（任意敌方单位都够得着）
        ///   · 支援线单位 → **只能**打敌方前线单位
        ///
        /// 最后这条的推论是本作最重要的一条地理约束：**HQ 在支援阵线里，
        /// 而支援线单位打不到支援线单位** ⇒ 抢下前线是打到 HQ 的唯一陆路。
        ///
        /// [STAGE2C] HQ 变成支援阵线里的一个**真实单位**之后，这条既有规则**无需修改**
        /// 就自动覆盖了"打 HQ"：HQ 的 Line 是 Support，于是支援线单位打不到它、
        /// 前线单位打得到它。`CanAttackHq` 现在只是这条规则对 HQ 的一个便捷包装（给 UI 用）。
        /// </summary>
        public bool CanAttackTarget(UnitInstance attacker, UnitInstance target, out string reason)
        {
            reason = "";
            if (attacker == null || target == null) { reason = "目标不存在"; return false; }
            if (!target.IsAlive) { reason = "目标已阵亡"; return false; }
            if (target.OwnerIndex == attacker.OwnerIndex) { reason = "不能攻击己方单位"; return false; }

            if (attacker.Line == Line.Support && target.Line != Line.Front)
            {
                reason = "支援阵线的单位只能攻击敌方前线单位";
                return false;
            }
            return true;
        }

        /// <summary>[STAGE2B] 能不能攻击敌方 HQ：**只有站在前线的单位够得着**。
        /// [STAGE2C] 语义上等价于 `CanAttackTarget(attacker, 敌方HQ单位)`，
        /// 保留成独立函数只是为了让 UI 不必自己去取敌方 HQ 的单位实例。</summary>
        public bool CanAttackHq(UnitInstance attacker, out string reason)
        {
            reason = "";
            if (attacker == null) { reason = "单位不存在"; return false; }
            if (attacker.Line != Line.Front)
            {
                reason = "只有前线单位可以攻击敌方 HQ";
                return false;
            }
            return true;
        }

        /// <summary>[STAGE2B] 判断某个单位能否移动上前线。</summary>
        public bool CanUnitMove(UnitInstance u, out string reason)
        {
            if (!CanUnitAct(u, out reason)) return false;

            if (u.Line != Line.Support)
            {
                reason = "该单位已经在前线，不能退回支援阵线";
                return false;
            }

            int owner = State.FrontlineOwner;
            if (owner != -1 && owner != u.OwnerIndex)
            {
                reason = "前线已被对方占领，无法移入";
                return false;
            }
            if (State.Frontline.Count >= FrontlineCapacity)
            {
                reason = "前线已满（上限 " + FrontlineCapacity + "）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// [STAGE2B] 把支援阵线上的一个单位移上前线。这是本阶段**唯一新增的公开操作**，
        /// 所以它自带回合归属守卫（CheckControl）—— 与 PlayCard / AttackWithUnit 保持同一把锁。
        ///
        /// "移动"与"攻击"是同价的：都花掉单位自己那一次行动机会，
        /// 也都按同一条档位扣**资源**（[STAGE2C] 与部署共用同一笔预算）。
        /// </summary>
        public bool MoveUnit(int unitInstanceId)
        {
            // [FIX-4] 回合归属守卫
            string control;
            if (!CheckControl(out control))
            {
                State.AddLog("移动失败：" + control);
                return false;
            }

            UnitInstance u = FindUnit(unitInstanceId);

            string reason;
            if (!CanUnitMove(u, out reason))
            {
                State.AddLog("移动失败：" + reason);
                return false;
            }

            State.Players[u.OwnerIndex].SupportLine.Remove(u);
            State.Frontline.Add(u);
            u.Line = Line.Front;
            SpendAction(u);

            State.AddLog(u.Def.Name + " 移动至前线（资源剩 " +
                         State.Players[u.OwnerIndex].Resources + "）");
            return true;
        }

        /// <summary>
        /// 用指定单位发动攻击。
        /// targetUnitInstanceId 传 -1 表示攻击敌方 HQ（只有前线单位做得到）。
        ///
        /// [STAGE2C] HQ 变成场上单位之后，这里**只剩一条结算路径**：双向同时扣血。
        /// "打 HQ 不会被反击"不再需要单开一个 if 分支 —— HQ 的攻击力是 0，
        /// 于是 `damageToAttacker = 0` 天然成立。少一个分支就少一处"两条路径行为不一致"的隐患。
        /// </summary>
        public bool AttackWithUnit(int attackerInstanceId, int targetUnitInstanceId)
        {
            // [FIX-4] 回合归属守卫（CanUnitAct 只校验单位归属，不校验"谁在指挥"）
            string control;
            if (!CheckControl(out control))
            {
                State.AddLog("攻击失败：" + control);
                return false;
            }

            UnitInstance attacker = FindUnit(attackerInstanceId);

            string reason;
            if (!CanUnitAttack(attacker, out reason))
            {
                State.AddLog("攻击失败：" + reason);
                return false;
            }

            // [STAGE2C] -1 是"敌方 HQ"的简写：解析成敌方 HQ 那个单位实例之后，
            // 后面的校验与结算跟打普通单位**完全一致**。
            UnitInstance target = ResolveAttackTarget(attacker, targetUnitInstanceId);
            if (target == null) { State.AddLog("攻击失败：目标不存在"); return false; }

            // 线位规则一次挡下两类非法攻击：
            //   · 支援阵线单位打不到敌方支援阵线（HQ 就在那儿）⇒ 只有前线单位够得着 HQ
            //   · 不能攻击己方单位
            if (!CanAttackTarget(attacker, target, out reason))
            {
                State.AddLog("攻击失败：" + reason);
                return false;
            }

            // 先把两边的攻击力都读出来，再同时施加伤害 —— 避免"先手方占便宜"。
            // 打 HQ 时 damageToAttacker = HQ.Attack = 0，等价于旧版本的"单向结算"。
            int damageToTarget = attacker.Attack;
            int damageToAttacker = target.Attack;

            attacker.Health -= damageToAttacker;
            target.Health -= damageToTarget;
            SpendAction(attacker);

            State.AddLog(attacker.Def.Name + "(" + damageToTarget + "/" +
                         System.Math.Max(0, attacker.Health) + ") 攻击 " +
                         DescribeTarget(target, attacker.OwnerIndex) + "(" + damageToAttacker + "/" +
                         System.Math.Max(0, target.Health) + ")");

            ResolveDeaths();   // 清理阵亡 + 检查 HQ 是否归零（含刚被打的这个 HQ）
            return true;
        }

        /// <summary>[STAGE2C] 把"攻击目标参数"解析成单位实例。负数 = 敌方 HQ。</summary>
        private UnitInstance ResolveAttackTarget(UnitInstance attacker, int targetUnitInstanceId)
        {
            if (targetUnitInstanceId < 0) return State.Players[1 - attacker.OwnerIndex].Hq;
            return FindUnit(targetUnitInstanceId);
        }

        /// <summary>日志里怎么称呼攻击目标：HQ 要写清楚归属，别混在单位名里。</summary>
        private string DescribeTarget(UnitInstance target, int attackerOwner)
        {
            return target.IsHq ? State.Players[1 - attackerOwner].Name + "的 HQ" : target.Def.Name;
        }

        // ==================================================================
        // 伤害与死亡清理
        // ==================================================================

        private void DamageUnit(UnitInstance u, int amount)
        {
            if (u == null || amount <= 0) return;
            u.Health -= amount;
        }

        /// <summary>
        /// 把所有 Health &lt;= 0 的单位移出战场并记入弃牌堆，**然后检查双方 HQ 是否归零**。
        ///
        /// [STAGE2B] 要扫三处：双方各自的支援阵线 + 共用的前线。
        /// 前线单位被清空后"归属自动回到无人占领"，**不需要任何重置代码** ——
        /// 因为归属是从 Frontline 列表推导出来的（见 GameState.FrontlineOwner）。
        ///
        /// ⚠️ [STAGE2C] **HQ 永远不会被移出列表**。它血归零不是"阵亡"，而是"这一方输了"，
        /// 所以下面两个循环必须显式跳过 IsHq —— 漏掉的话会出现
        /// "HQ 从支援阵线上被移除、但胜负没判、界面以为 HQ 还在"这种静默错误。
        ///
        /// 这个函数是**所有伤害的收口**：攻击、指令卡、AOE、疲劳四条路径打完都调它，
        /// 于是"HQ 归零即判负"只需要在这里写一次。
        ///
        /// defeatCause：HQ 归零时拼进终局原因的那个后缀。四个调用点里只有疲劳会传非默认值 ——
        /// 目的是让"输在疲劳"在日志和统计里能被打脸致死区分开。
        /// </summary>
        private void ResolveDeaths(string defeatCause = "的 HQ 被击破")
        {
            for (int pi = 0; pi < 2; pi++)
            {
                PlayerState p = State.Players[pi];
                for (int i = p.SupportLine.Count - 1; i >= 0; i--)
                {
                    UnitInstance u = p.SupportLine[i];
                    if (u.IsHq) continue;        // HQ 不阵亡，只判负（见上方告警）
                    if (u.IsAlive) continue;
                    p.SupportLine.RemoveAt(i);
                    p.Discard.Add(u.Def);
                    State.AddLog("  " + u.Def.Name + " 被消灭");
                }
            }

            for (int i = State.Frontline.Count - 1; i >= 0; i--)
            {
                UnitInstance u = State.Frontline[i];
                if (u.IsAlive) continue;
                State.Frontline.RemoveAt(i);
                State.Players[u.OwnerIndex].Discard.Add(u.Def);
                State.AddLog("  " + u.Def.Name + " 被消灭");
            }

            CheckHqDefeat(defeatCause);
        }

        /// <summary>
        /// [STAGE2C] HQ 归零即判负 —— 这是**唯一**的"伤害判负"路径（L1）。
        ///
        /// 旧的 L2（牌库抽空即负）已删除，所以现在"怎么输"只有一种说法：
        /// HQ 血量被打到 0 或以下（来源可以是单位攻击、指令卡，也可以是疲劳伤害）。
        /// 按 0 号、1 号顺序检查，保证双方同时归零时结果确定、可复现。
        /// </summary>
        private void CheckHqDefeat(string cause)
        {
            if (State.IsOver) return;
            for (int pi = 0; pi < 2; pi++)
            {
                if (State.Players[pi].HqHealth <= 0)
                {
                    EndMatch(1 - pi, State.Players[pi].Name + cause);
                    return;
                }
            }
        }

        // ==================================================================
        // 查询辅助（供渲染层与 AI 使用，只读）
        // ==================================================================

        /// <summary>在三处战场（双方支援阵线 + 前线）里按 InstanceId 找单位。找不到返回 null。
        /// [STAGE2C] HQ 也在支援阵线里，所以它**同样能被这个函数找到** ——
        /// 目标是 HQ 时（UI 传它的 InstanceId、或内部解析 -1）靠的就是这条路径。</summary>
        public UnitInstance FindUnit(int instanceId)
        {
            if (instanceId < 0) return null;
            for (int pi = 0; pi < 2; pi++)
            {
                List<UnitInstance> line = State.Players[pi].SupportLine;
                for (int i = 0; i < line.Count; i++)
                {
                    if (line[i].InstanceId == instanceId) return line[i];
                }
            }
            for (int i = 0; i < State.Frontline.Count; i++)
            {
                if (State.Frontline[i].InstanceId == instanceId) return State.Frontline[i];
            }
            return null;
        }

        /// <summary>
        /// [STAGE2B] 把一个玩家"看得见的**可作战**单位"（支援阵线 + 他占领的前线）装进 into。
        ///
        /// ⚠️ [STAGE2C] **HQ 不算在内**。它在数据结构上确实躺在 SupportLine 里，但它不是棋子：
        /// 不能移动、不能攻击、不能交换。这里把它滤掉，是让所有靠这个函数枚举单位的调用方
        /// （AI 的攻击/移动决策、AOE 的目标收集、UI 的可攻击列表）一次性拿到正确的集合，
        /// 而不是各自去记得补 `if (u.IsHq) continue;`。
        /// 需要**按 id 定位任意单位（含 HQ）**请用 FindUnit。
        ///
        /// 参数用调用方给的 List（而不是每次 new）是因为阶段 4 要跑上万局，
        /// 这个函数在 AI 的攻击循环里是每步都调的。
        /// </summary>
        public void CollectUnitsOf(int playerIndex, List<UnitInstance> into)
        {
            into.Clear();
            List<UnitInstance> line = State.Players[playerIndex].SupportLine;
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].IsHq) continue;     // [STAGE2C] HQ 不是可作战单位
                into.Add(line[i]);
            }
            if (State.FrontlineOwner == playerIndex)
            {
                for (int i = 0; i < State.Frontline.Count; i++) into.Add(State.Frontline[i]);
            }
        }

        /// <summary>[STAGE2B] 某个玩家场上还有几个**可作战**单位（支援阵线 + 前线）。
        /// [STAGE2C] 不含 HQ —— 调用方（`精准打击` 的"敌方没有可选目标"判定）要的是
        /// "有没有能被我指定的单位"，HQ 恰恰是那个**打不到**的目标。</summary>
        public int CountUnitsOf(int playerIndex)
        {
            PlayerState p = State.Players[playerIndex];
            int n = p.SupportLine.Count;
            if (p.Hq != null) n--;              // [STAGE2C] 扣掉常驻的 HQ 那一格
            if (State.FrontlineOwner == playerIndex) n += State.Frontline.Count;
            return n;
        }

        /// <summary>[STAGE2B] 指定玩家"能攻击且够得着目标"的单位列表。</summary>
        public List<UnitInstance> GetAttackableUnits(int playerIndex)
        {
            List<UnitInstance> result = new List<UnitInstance>();
            List<UnitInstance> units = new List<UnitInstance>();
            CollectUnitsOf(playerIndex, units);
            for (int i = 0; i < units.Count; i++)
            {
                if (HasAttackTarget(units[i])) result.Add(units[i]);
            }
            return result;
        }

        /// <summary>[STAGE2B] 指定玩家"可以移上前线"的单位列表（只在支援阵线里找）。</summary>
        public List<UnitInstance> GetMovableUnits(int playerIndex)
        {
            List<UnitInstance> result = new List<UnitInstance>();
            List<UnitInstance> line = State.Players[playerIndex].SupportLine;
            string reason;
            for (int i = 0; i < line.Count; i++)
            {
                if (CanUnitMove(line[i], out reason)) result.Add(line[i]);
            }
            return result;
        }
    }
}
