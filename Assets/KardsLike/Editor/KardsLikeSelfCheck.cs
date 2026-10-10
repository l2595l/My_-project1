#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using KardsLike.Core;
using UnityEditor;
using UnityEngine;

namespace KardsLike.EditorTools
{
    /// <summary>
    /// 阶段 1 收口自检（[FIX-6] 任务 4）。
    ///
    /// 存在的理由：在这之前，整个工程的验收完全依赖"手动 Play + 截图"，
    /// 而 CardDb.ValidatePanelBudget() 定义了却没有任何调用点 —— 被设计文档称为
    /// "防失衡第一道闸门"的东西其实没有装上闸门。同时架构约定第 2 条
    /// 「同种子结果完全一致」没有任何验证记录，而阶段 4 的整栋楼都建在这个地基上。
    ///
    /// 这个脚本只放在 Editor 目录下（编辑期程序集），不污染任何运行时代码。
    /// 菜单：KardsLike/自检（也可直接调用 RunAll()）。
    ///
    /// 全过程使用 MatchMode.AiVsAi —— 自检没有 UI，需要能自由驱动双方回合。
    /// </summary>
    public static class KardsLikeSelfCheck
    {
        private const string MenuPath = "KardsLike/自检";
        private const string ReportRelativePath = "Docs/自检报告-阶段1收口.md";

        private const uint ReproSeed = 12345u;   // 任务 4 第 3 项指定的种子
        private const int BiasGameCount = 200;   // 任务 4 第 4 项指定的局数
        private const int SafetyLoopGuard = 2000; // 单局最多推进次数，防止自检本身死循环

        [MenuItem(MenuPath)]
        public static void RunFromMenu()
        {
            RunAll();
        }

        /// <summary>跑完 11 项自检，返回是否全部通过。</summary>
        public static bool RunAll()
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;

            sb.AppendLine("# KardsLike 自检报告（阶段 1 收口 → 阶段 2 任务 B 扩展）");
            sb.AppendLine();
            sb.AppendLine("- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("- Unity 版本：" + Application.unityVersion);
            sb.AppendLine("- 对局模式：MatchMode.AiVsAi（自检没有 UI，需要自由驱动双方）");
            sb.AppendLine("- 覆盖：面板预算与兵种/行动费 / 资源上限 / 同种子可复现 / 终止性与先手偏差 / " +
                          "部署失调与每回合一次行动 / L1 判负 / 三条战线与行动费 / HQ 单位与疲劳结算 / " +
                          "兵种反击例外 / 越线射程与战斗机拦截 / 坦克移动+攻击");
            sb.AppendLine();
            sb.AppendLine("| # | 检查项 | 结果 |");
            sb.AppendLine("|---|---|---|");

            // 先把每一项跑出来，拿到布尔结果后再写表格（表格要放在正文之前）
            List<string> titles = new List<string>();
            List<bool> results = new List<bool>();
            List<string> bodies = new List<string>();

            bodies.Add(RunPanelBudget(out var ok1, out string title1));
            titles.Add(title1); results.Add(ok1);

            bodies.Add(RunResourceCap(out var ok2, out string title2));
            titles.Add(title2); results.Add(ok2);

            bodies.Add(RunSameSeedReproducible(out var ok3, out string title3));
            titles.Add(title3); results.Add(ok3);

            bodies.Add(RunTerminationAndBias(out var ok4, out string title4));
            titles.Add(title4); results.Add(ok4);

            bodies.Add(RunDeploySicknessAndLethal(out var ok5, out var ok6, out string title5, out string title6));
            titles.Add(title5); results.Add(ok5);
            titles.Add(title6); results.Add(ok6);

            bodies.Add(RunThreeLinesAndActionCost(out var ok7, out string title7));
            titles.Add(title7); results.Add(ok7);

            bodies.Add(RunHqUnitAndFatigue(out var ok8, out string title8));
            titles.Add(title8); results.Add(ok8);

            bodies.Add(RunUnitTypeCounterRules(out var ok9, out string title9));
            titles.Add(title9); results.Add(ok9);

            bodies.Add(RunLongRangeAndInterception(out var ok10, out string title10));
            titles.Add(title10); results.Add(ok10);

            bodies.Add(RunTankMoveAndAttack(out var ok11, out string title11));
            titles.Add(title11); results.Add(ok11);

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i]) passed++; else failed++;
                sb.AppendLine("| " + (i + 1) + " | " + titles[i] + " | " + (results[i] ? "✅ 通过" : "❌ 失败") + " |");
            }

            sb.AppendLine();
            sb.AppendLine("**汇总：通过 " + passed + " 项，失败 " + failed + " 项。**");
            sb.AppendLine();

            for (int i = 0; i < bodies.Count; i++)
            {
                sb.AppendLine(bodies[i]);
            }

            string report = sb.ToString();

            // 写到 Console（编辑期日志）与 Docs 下的报告文件（便于当作交付物留存）
            Debug.Log("[KardsLike 自检] 通过 " + passed + " / " + results.Count + " 项" +
                      (failed == 0 ? "，全部通过。" : "，有 " + failed + " 项失败，详见下方。"));
            Debug.Log(report);

            WriteReportFile(report);
            return failed == 0;
        }

        // ==================================================================
        // 第 1 项 · 面板预算
        // ==================================================================

        private static string RunPanelBudget(out bool ok, out string title)
        {
            title = "卡牌数据：面板预算 + 兵种已填写 + 行动费与档位一致（ValidatePanelBudget 返回 null）";
            ok = false;

            string violation = CardDb.ValidatePanelBudget();
            ok = (violation == null);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 1 项 · 卡牌数据（面板预算 / 兵种 / 行动费）");
            sb.AppendLine();
            sb.AppendLine("[阶段2.5 · 任务 A] 本条由「只查面板预算」扩展为查三条：");
            sb.AppendLine();
            sb.AppendLine("| # | 口径 | 出处 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| ① | 单位卡 `攻击 + 生命 = 2 × 费用 + 1` | 设计文档 §4.1 |");
            sb.AppendLine("| ② | 单位卡的**兵种必须显式填写**（`UnitType.None` 是「忘填」的哨兵值） | 设计文档 §10.1 |");
            sb.AppendLine("| ③ | `ActionCost` 必须等于档位期望（1~2 费→1 / 3~4 费→2 / 5~6 费→3） | 设计文档 §10.3 |");
            sb.AppendLine();
            sb.AppendLine("第 ② 条存在的理由：兵种漏填会静默地按「普通双向结算」跑，");
            sb.AppendLine("在 200 局数据里表现为「数字说不通但查不出原因」—— 必须在数据层就挡住。");
            sb.AppendLine();
            sb.AppendLine("检查的卡（" + CardDb.Units.Length + " 张单位卡）：");
            sb.AppendLine();
            sb.AppendLine("| id | 卡名 | 费用 | 攻/命 | 面板 | 预算 | 兵种 | 行动费 | 档位期望 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < CardDb.Units.Length; i++)
            {
                CardDef c = CardDb.Units[i];
                sb.AppendLine("| `" + c.Id + "` | " + c.Name + " | " + c.Cost + " | " +
                              c.Attack + "/" + c.Health + " | " + c.PanelValue + " | " +
                              CardDef.BudgetForCost(c.Cost) + " | " + c.UnitKind + " | " +
                              c.ActionCost + " | " + CardDb.ExpectedActionCost(c.Cost) + " |");
            }
            sb.AppendLine();
            sb.AppendLine(ok
                ? "**结果：全部符合预算公式。**"
                : "**结果：第一条违规 —— " + violation + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 2 项 · 资源上限
        // ==================================================================

        private static string RunResourceCap(out bool ok, out string title)
        {
            title = "资源上限：连续推进己方回合，Resources 封顶 10 且不再增长";
            ok = false;

            GameEngine eng = new GameEngine(1u, MatchMode.AiVsAi);
            eng.StartMatch();

            List<int> observed = new List<int>();
            int guard = 0;
            while (!eng.State.IsOver && observed.Count < 22 && guard++ < SafetyLoopGuard)
            {
                if (eng.State.CurrentPlayer == 0)
                {
                    observed.Add(eng.State.Players[0].Resources);
                }
                eng.EndTurn();
            }

            // 期望：第 k 个己方回合的资源 = min(k, 10)
            bool allMatch = observed.Count >= 20;
            int firstMismatch = -1;
            for (int i = 0; i < observed.Count; i++)
            {
                int expected = Math.Min(i + 1, GameEngine.ResourceCap);
                if (observed[i] != expected && firstMismatch < 0) firstMismatch = i;
                if (observed[i] != expected) allMatch = false;
            }

            ok = allMatch;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 2 项 · 资源上限（设计文档 §2.5 / §6.5）");
            sb.AppendLine();
            sb.AppendLine("连续推进己方回合（双方都不出牌，只结束回合），记录每个己方回合开始时的资源。");
            sb.AppendLine("推进到第 " + observed.Count + " 个己方回合（牌库 " +
                          (CardDb.Cards.Length * GameEngine.DeckCopiesPerCard) + " 张、每个己方回合抽 1，" +
                          "22 个己方回合远未抽空，所以这一项不会被疲劳结算干扰）。");
            sb.AppendLine();
            sb.AppendLine("| 己方回合 | 资源 | 期望 |");
            sb.AppendLine("|---|---|---|");
            int showCount = Math.Min(observed.Count, 22);
            for (int i = 0; i < showCount; i++)
            {
                int expected = Math.Min(i + 1, GameEngine.ResourceCap);
                sb.AppendLine("| 第 " + (i + 1) + " 回合 | " + observed[i] + " | " + expected + " |");
            }
            sb.AppendLine();
            sb.AppendLine("**结论：资源从 1 递增到 " + GameEngine.ResourceCap +
                          " 后不再增长**（首次偏差位置：" + (firstMismatch < 0 ? "无" : "第 " + (firstMismatch + 1) + " 个回合") + "）。");
            sb.AppendLine();
            sb.AppendLine("> 注：这是一条在实战中「几乎不可能被触发」的规则（资源要从 1 涨到 10 需要 10 个己方回合），");
            sb.AppendLine("> 手工试玩无法证明它是对的 —— 这正是必须用脚本验证它的原因。");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 3 项 · 同种子可复现
        // ==================================================================

        private static string RunSameSeedReproducible(out bool ok, out string title)
        {
            title = "同种子可复现：seed=12345 两局逐字段、逐日志一致";
            ok = false;

            GameEngine a = RunFullGame(ReproSeed);
            GameEngine b = RunFullGame(ReproSeed);

            bool sameResult = a.State.Result == b.State.Result;
            bool sameHalfTurn = a.State.HalfTurn == b.State.HalfTurn;
            bool sameHq0 = a.State.Players[0].HqHealth == b.State.Players[0].HqHealth;
            bool sameHq1 = a.State.Players[1].HqHealth == b.State.Players[1].HqHealth;
            bool sameLogCount = a.State.Log.Count == b.State.Log.Count;

            int firstLogDiff = -1;
            int n = Math.Min(a.State.Log.Count, b.State.Log.Count);
            for (int i = 0; i < n; i++)
            {
                if (a.State.Log[i] != b.State.Log[i]) { firstLogDiff = i; break; }
            }
            bool sameLogs = sameLogCount && firstLogDiff < 0;

            ok = sameResult && sameHalfTurn && sameHq0 && sameHq1 && sameLogs;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 3 项 · 同种子可复现（设计文档 §8 第 2 条）");
            sb.AppendLine();
            sb.AppendLine("用 seed=" + ReproSeed + " 构造两个引擎，各自跑完 GreedyAI vs GreedyAI 的完整对局。");
            sb.AppendLine();
            sb.AppendLine("| 字段 | 第 1 局 | 第 2 局 | 一致 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| MatchResult | " + a.State.Result + " | " + b.State.Result + " | " + Mark(sameResult) + " |");
            sb.AppendLine("| HalfTurn | " + a.State.HalfTurn + " | " + b.State.HalfTurn + " | " + Mark(sameHalfTurn) + " |");
            sb.AppendLine("| 玩家 HQ | " + a.State.Players[0].HqHealth + " | " + b.State.Players[0].HqHealth + " | " + Mark(sameHq0) + " |");
            sb.AppendLine("| 电脑 HQ | " + a.State.Players[1].HqHealth + " | " + b.State.Players[1].HqHealth + " | " + Mark(sameHq1) + " |");
            sb.AppendLine("| 日志条数 | " + a.State.Log.Count + " | " + b.State.Log.Count + " | " + Mark(sameLogCount) + " |");
            sb.AppendLine("| 逐条日志文本 | — | — | " + Mark(sameLogs) + " |");
            sb.AppendLine();
            if (firstLogDiff >= 0)
            {
                sb.AppendLine("**第一条不一致的日志（第 " + firstLogDiff + " 条）**");
                sb.AppendLine();
                sb.AppendLine("- 第 1 局：`" + a.State.Log[firstLogDiff] + "`");
                sb.AppendLine("- 第 2 局：`" + b.State.Log[firstLogDiff] + "`");
                sb.AppendLine();
            }
            sb.AppendLine("**结论：" + (ok
                ? "两局在结果、回合数、双方 HQ、日志条数与每一条日志文本上完全一致 —— 「同种子可复现」成立。"
                : "存在不一致，阶段 4 的批量模拟地基不成立，必须先修。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 4 项 · 终止性与先手偏差（本项数字将作为阶段 4 的第一份数据）
        // ==================================================================

        private static string RunTerminationAndBias(out bool ok, out string title)
        {
            title = "终止性 + 先手偏差：seed 1~200 全部在回合上限内终止";
            ok = false;

            int p0Win = 0, p1Win = 0, draw = 0;
            int fatigueEndings = 0;
            int turnLimitEndings = 0;
            int hqEndings = 0;
            int notTerminated = 0;
            int overLimit = 0;
            int maxHalfTurn = 0;
            long sumHalfTurn = 0;

            // [STAGE2B] 按终局类型分别统计先手胜率 —— 只看总胜率会把"哪种终局导致了偏差"藏起来。
            // [STAGE2C] L2 由"牌库抽空即判负"改成"疲劳伤害"之后，终局被拆成三类：
            //   ① 打脸击破 HQ（战斗的结果） ② 疲劳击破 HQ（牌库先空的代价） ③ 回合上限兜底。
            // 前两类同属 L1（都是 HQ 归零），但**必须分开统计** —— 它们的先手偏差含义完全不同。
            int hqP0Win = 0, hqP1Win = 0;
            int fatigueP0Win = 0, fatigueP1Win = 0;

            for (uint seed = 1; seed <= BiasGameCount; seed++)
            {
                GameEngine eng = RunFullGame(seed);

                if (!eng.State.IsOver) { notTerminated++; continue; }
                if (eng.State.HalfTurn > GameEngine.MaxTurns) overLimit++;

                maxHalfTurn = Math.Max(maxHalfTurn, eng.State.HalfTurn);
                sumHalfTurn += eng.State.HalfTurn;

                if (eng.State.Result == MatchResult.Player0Win) p0Win++;
                else if (eng.State.Result == MatchResult.Player1Win) p1Win++;
                else draw++;

                bool p0Won = (eng.State.Result == MatchResult.Player0Win);

                // 判定顺序很重要：疲劳的终局原因里**也含**"HQ 被击破"（它是 HQ 归零的一个子类），
                // 所以必须先判"疲劳"，否则疲劳局会被错记成打脸局。
                string why = eng.State.ResultReason;
                if (why.Contains("疲劳")) { fatigueEndings++; if (p0Won) fatigueP0Win++; else fatigueP1Win++; }
                else if (why.Contains("回合上限")) turnLimitEndings++;
                else if (why.Contains("HQ 被击破")) { hqEndings++; if (p0Won) hqP0Win++; else hqP1Win++; }
            }

            ok = (notTerminated == 0) && (overLimit == 0);

            double avgHalfTurn = sumHalfTurn / (double)BiasGameCount;
            double p0Rate = p0Win * 100.0 / BiasGameCount;
            double p1Rate = p1Win * 100.0 / BiasGameCount;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 4 项 · 终止性与先手偏差（" + BiasGameCount + " 局）");
            sb.AppendLine();
            sb.AppendLine("seed 从 1 到 " + BiasGameCount + " 各跑一局 GreedyAI vs GreedyAI（AiVsAi 模式）。");
            sb.AppendLine("这一项的数字就是阶段 4 平衡性统计要处理的第一份数据。");
            sb.AppendLine();
            sb.AppendLine("### 4.1 终止性");
            sb.AppendLine();
            sb.AppendLine("| 指标 | 值 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 未在推进上限内终止的局数 | " + notTerminated + " |");
            sb.AppendLine("| 超过回合上限（HalfTurn > " + GameEngine.MaxTurns + "）的局数 | " + overLimit + " |");
            sb.AppendLine("| 最大 HalfTurn | " + maxHalfTurn + " |");
            sb.AppendLine("| 平均 HalfTurn | " + avgHalfTurn.ToString("F2") + "（约 " + (avgHalfTurn / 2).ToString("F2") + " 轮） |");
            sb.AppendLine();
            sb.AppendLine("### 4.2 胜负分布");
            sb.AppendLine();
            sb.AppendLine("| 结果 | 局数 | 占比 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| 玩家（先手，索引 0）胜 | " + p0Win + " | " + p0Rate.ToString("F1") + "% |");
            sb.AppendLine("| 电脑（后手，索引 1）胜 | " + p1Win + " | " + p1Rate.ToString("F1") + "% |");
            sb.AppendLine("| 平局 | " + draw + " | " + (draw * 100.0 / BiasGameCount).ToString("F1") + "% |");
            sb.AppendLine();
            sb.AppendLine("### 4.3 终局原因");
            sb.AppendLine();
            sb.AppendLine("| 原因 | 局数 | 占比 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| 打脸击破 HQ | " + hqEndings + " | " + Pct(hqEndings) + " |");
            sb.AppendLine("| 疲劳击破 HQ（旧 L2 的位置） | " + fatigueEndings + " | " + Pct(fatigueEndings) + " |");
            sb.AppendLine("| 回合上限兜底（L3） | " + turnLimitEndings + " | " + Pct(turnLimitEndings) + " |");
            sb.AppendLine();
            double hqP0Rate = (hqP0Win + hqP1Win) > 0 ? hqP0Win * 100.0 / (hqP0Win + hqP1Win) : 0.0;
            double ftP0Rate = (fatigueP0Win + fatigueP1Win) > 0 ? fatigueP0Win * 100.0 / (fatigueP0Win + fatigueP1Win) : 0.0;

            sb.AppendLine("**L3 是否被触发**：" + turnLimitEndings + " 局。" +
                          "设计文档 §5.3 说 L3「永远不会被正常对局触发」—— 这行数字直接证实或证伪它。");
            sb.AppendLine();
            sb.AppendLine("**先手偏差的归因**（[STAGE2C] 口径，不要只看总胜率）：" +
                          "总胜率差 " + Math.Abs(p0Rate - p1Rate).ToString("F1") + " 个百分点，分布如下：");
            sb.AppendLine();
            sb.AppendLine("| 终局类型 | 局数 | 先手胜 | 后手胜 | 先手胜率 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine("| 打脸击破 HQ | " + hqEndings + " | " + hqP0Win + " | " + hqP1Win + " | " +
                          hqP0Rate.ToString("F1") + "% |");
            sb.AppendLine("| 疲劳击破 HQ | " + fatigueEndings + " | " + fatigueP0Win + " | " + fatigueP1Win + " | " +
                          ftP0Rate.ToString("F1") + "% |");
            sb.AppendLine();
            sb.AppendLine("> **旧口径对照（[STAGE2B]，已作废，只留作对照）**：那时 L2 是「牌库抽空即判负」，" +
                          "200 局里 76 局因它结束、先手胜率 96.1%，把总偏差从 0 抬到 34 个百分点。" +
                          "那张表说明的是：**只要「牌库先空」本身等于输，它就会被回合顺序单方面决定**，" +
                          "任何后手补偿都只是在「先手赢光 L2」与「后手赢光 L2」之间来回翻转。" +
                          "现在的疲劳结算把「牌库先空」降级成「开始挨伤害」，胜负重新回到 HQ 血量这一条统一战线上。");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 5、6 项 · 部署失调 / 每回合一次行动 与 L1 判负
        // ==================================================================

        private static string RunDeploySicknessAndLethal(out bool okSummon, out bool okLethal,
                                                        out string titleSummon, out string titleLethal)
        {
            titleSummon = "部署失调 + 每回合一次行动（非坦克）：部署当回合不能移动/攻击，下一回合可移动且只扣对应资源";
            titleLethal = "L1 判负：HQ 归零后 Result 正确，且此后所有操作返回 false";
            okSummon = false;
            okLethal = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 5 项 · 部署失调与「每单位每回合一次行动」（非坦克口径；坦克例外见第 11 项）（设计文档 §7.1）");
            sb.AppendLine();
            sb.AppendLine("Kards 官方口径：\"Without Blitz, you need to wait one turn after the deployment");
            sb.AppendLine("until you **use** the unit.\" —— 是 use 不是 attack，所以部署当回合**既不能移动也不能攻击**；");
            sb.AppendLine("要走到敌方 HQ 需要三个己方回合（部署 → 移动上前线 → 攻击）。");
            sb.AppendLine("（坦克例外——同回合可移动 + 攻击——见第 11 项。）");
            sb.AppendLine();

            // 需要一个"开局手上就有打得起的单位卡"的种子。
            // 1 号回合只有 1 点资源，所以必须找到手上有 1 费单位卡的种子，否则这个用例跑不起来。
            uint seed = 0;
            GameEngine eng = null;
            int handIndex = -1;
            for (uint s = 1; s <= 2000u; s++)
            {
                GameEngine probe = new GameEngine(s, MatchMode.AiVsAi);
                probe.StartMatch();
                string reason;
                for (int i = 0; i < probe.State.Players[0].Hand.Count; i++)
                {
                    // [阶段2.6 · 任务3] 加"非坦克"限定：本项的"二选一"口径只适用于非坦克
                    //（坦克"移动+攻击"例外由第 11 项覆盖）——否则抽到坦克种子会让断言语义漂移。
                    if (probe.State.Players[0].Hand[i].IsUnit
                        && probe.State.Players[0].Hand[i].UnitKind != UnitType.Tank
                        && probe.CanPlayCard(i, out reason))
                    {
                        seed = s; eng = probe; handIndex = i;
                        break;
                    }
                }
                if (eng != null) break;
            }

            if (eng == null)
            {
                sb.AppendLine("**无法构造用例：2000 个种子里没有一局开局手牌含可打出的单位卡。**");
                sb.AppendLine();
                titleSummon = "部署失调（用例构造失败）";
                return sb.ToString();
            }

            CardDef sumUnit = eng.State.Players[0].Hand[handIndex];
            int cost = GameEngine.ActionCostOf(sumUnit);
            bool deployOk = eng.PlayCard(handIndex, -1);
            // [STAGE2C] ⚠️ SupportLine[0] 永远是 HQ —— 新部署的单位落在它**后面**。
            // 取"列表里最后一个"就是刚部署的那个（旧版本取 [0] 会误拿 HQ，整个用例就废了）。
            UnitInstance unit = deployOk && eng.State.Players[0].SupportLine.Count > 1
                ? eng.State.Players[0].SupportLine[eng.State.Players[0].SupportLine.Count - 1] : null;

            string why1 = "";
            bool canAttackSameTurn = unit != null && eng.CanUnitAttack(unit, out why1);
            string why2 = "";
            bool canMoveSameTurn = unit != null && eng.CanUnitMove(unit, out why2);
            bool moveRejectedSameTurn = unit != null && !eng.MoveUnit(unit.InstanceId);

            sb.AppendLine("- 用例种子：**" + seed + "**（开局手上就有可打出的单位卡）");
            sb.AppendLine("- 部署：" + (deployOk ? "成功" : "失败") + " → " +
                          (unit == null ? "（无单位）" : unit.Def.Name + "（" + unit.Attack + "/" + unit.Health +
                           "，行动费 " + cost + " 点）"));
            sb.AppendLine("- 落点：" + (unit == null ? "—" : "Line." + unit.Line) +
                          "，SupportLine.Count = " + eng.State.Players[0].SupportLine.Count);
            sb.AppendLine("- 部署当回合 CanUnitAttack：" + canAttackSameTurn +
                          (why1.Length > 0 ? "（" + why1 + "）" : ""));
            sb.AppendLine("- 部署当回合 CanUnitMove：" + canMoveSameTurn +
                          (why2.Length > 0 ? "（" + why2 + "）" : ""));
            sb.AppendLine("- 部署当回合直接调 MoveUnit 被拒：" + moveRejectedSameTurn);
            sb.AppendLine();

            bool nextTurnMovable = false;
            bool moveOk = false;
            int resBefore = -1, resAfter = -1;
            bool blockedAfterMove = true;
            string why3 = "";

            if (unit != null && !eng.State.IsOver)
            {
                // 推进两方各一个回合：电脑回合（不做任何动作）→ 回到己方回合
                eng.EndTurn();
                if (!eng.State.IsOver) eng.EndTurn();

                UnitInstance again = eng.FindUnit(unit.InstanceId);
                nextTurnMovable = again != null && eng.CanUnitMove(again, out why3);
                resBefore = eng.State.Players[0].Resources;
                moveOk = again != null && eng.MoveUnit(again.InstanceId);
                resAfter = eng.State.Players[0].Resources;
                blockedAfterMove = again != null && !eng.CanUnitAttack(again, out why3);

                sb.AppendLine("- 推进一个完整轮次后（HalfTurn=" + eng.State.HalfTurn + "）CanUnitMove：" +
                              nextTurnMovable + (again == null ? "（单位已不在场）" : ""));
                sb.AppendLine("- 移动结果：" + moveOk +
                              "，Line = " + (again == null ? "—" : again.Line.ToString()) +
                              "，前线归属 = " + eng.State.FrontlineOwner + "（-1 = 无人占领）");
                sb.AppendLine("- 移动前后**资源**：" + resBefore + " → " + resAfter + "（期望扣 " + cost + "）");
                sb.AppendLine("- 移动后本回合 CanUnitAttack：" + !blockedAfterMove +
                              "（期望 false" + (why3.Length > 0 ? "：" + why3 : "") + "）");
                sb.AppendLine();
            }

            okSummon = (unit != null) && !canAttackSameTurn && !canMoveSameTurn && moveRejectedSameTurn
                       && nextTurnMovable && moveOk
                       && (resBefore - resAfter == cost)
                       && blockedAfterMove
                       && eng.State.FrontlineOwner == 0;

            sb.AppendLine("**结论：" + (okSummon
                ? "部署当回合既不能移动也不能攻击（部署失调成立）；下一回合可移动、扣除的资源与行动费一致，" +
                  "且移动后本回合不能再攻击（「非坦克每单位每回合一次行动」成立）。"
                : "部署失调或「每回合一次行动」行为不符预期。") + "**");
            sb.AppendLine();

            // ---------------- 第 6 项 ----------------
            sb.AppendLine("## 第 6 项 · L1 判负（设计文档 §5.1）");
            sb.AppendLine();

            if (unit == null || eng.State.IsOver)
            {
                sb.AppendLine("**无法构造用例：第 5 项的部署未成功或对局已结束。**");
                sb.AppendLine();
                titleLethal = "L1 判负（用例构造失败）";
                return sb.ToString();
            }

            // 打 HQ 需要单位站在前线 —— 第 5 项结束时它已经在那里了，
            // 但它"移动过"= 本回合已行动，所以要再推进一个完整轮次才能攻击。
            eng.EndTurn();
            if (!eng.State.IsOver) eng.EndTurn();

            UnitInstance attacker = eng.FindUnit(unit.InstanceId);
            bool inFront = attacker != null && attacker.Line == Line.Front;
            string hqReason = "";
            bool canHitHq = attacker != null && eng.CanAttackHq(attacker, out hqReason);
            int hpBefore = eng.State.Players[1].HqHealth;

            // 测试专用捷径：直接把对手 HQ 压到 1，再用一次攻击打穿。
            // 之所以不"实打实"打满 20 点，是因为那需要十几个回合，会让自检变慢且受随机性干扰。
            // [STAGE2C] HQ 现在是单位：改血量就是改它的 Health（已不存在可写的 HqHealth 字段）。
            eng.State.Players[1].Hq.Health = 1;
            bool attackOk = attacker != null && eng.AttackWithUnit(attacker.InstanceId, -1);

            bool resultOk = eng.State.Result == MatchResult.Player0Win;
            bool opsRejected = eng.EndTurn() == false
                               && eng.PlayCard(0, -1) == false
                               && eng.AttackWithUnit(unit.InstanceId, -1) == false
                               && eng.MoveUnit(unit.InstanceId) == false;

            okLethal = inFront && canHitHq && attackOk && resultOk && opsRejected;

            sb.AppendLine("- 打脸前单位所在线：" + (attacker == null ? "—" : attacker.Line.ToString()) + "（期望 Front）");
            sb.AppendLine("- CanAttackHq：" + canHitHq + (hqReason.Length > 0 ? "（" + hqReason + "）" : ""));
            sb.AppendLine("- 攻击前对手 HQ：" + hpBefore + " → 测试中压到 1");
            sb.AppendLine("- 打脸攻击返回：" + attackOk);
            sb.AppendLine("- 攻击后对手 HQ：" + eng.State.Players[1].HqHealth);
            sb.AppendLine("- MatchResult：" + eng.State.Result + "（期望 Player0Win）");
            sb.AppendLine("- 终局原因：" + eng.State.ResultReason);
            sb.AppendLine("- 终局后 EndTurn / PlayCard / AttackWithUnit / MoveUnit 全部返回 false：" +
                          (opsRejected ? "是" : "否"));
            sb.AppendLine();
            sb.AppendLine("**结论：" + (okLethal
                ? "只有前线单位能打 HQ、L1 判负正确，且终局后一切操作被拒绝（设计文档 §5.1 的终局锁定成立）。"
                : "L1 判负行为不符预期。") + "**");
            sb.AppendLine();

            return sb.ToString();
        }

        // ==================================================================
        // 第 7 项 · 三条战线与行动费（[STAGE2B] 新增，[STAGE2C] 改为扣资源）
        // ==================================================================

        private static string RunThreeLinesAndActionCost(out bool ok, out string title)
        {
            title = "三条战线与行动费：线位攻击关系 / 前线归属与容量 / 行动费分档（从资源扣）";
            ok = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 7 项 · 三条战线与行动费（设计文档 §7.1 / §2.7）");
            sb.AppendLine();
            sb.AppendLine("本项用一个**手工摆好的棋盘**（测试夹具）来验证线位、容量与行动费。");
            sb.AppendLine("之所以不靠随机对局抽样：这些规则在随机局里的触发概率很低，靠抽样等于没测。");
            sb.AppendLine();
            sb.AppendLine("[STAGE2C] 行动费从**资源**里扣（不再有独立的行动点），所以下面所有闸门检查");
            sb.AppendLine("都改成把 `Resources` 压到 0 来触发。");
            sb.AppendLine();

            GameEngine eng = new GameEngine(1u, MatchMode.AiVsAi);
            eng.StartMatch();
            GameState st = eng.State;

            CardDef scout = CardDb.Get("unit_scout");       // 1 费 1/2
            CardDef mg = CardDb.Get("unit_mg_nest");        // 2 费 2/3
            CardDef para = CardDb.Get("unit_paratrooper");  // 3 费 3/4
            CardDef tank = CardDb.Get("unit_tank");         // 6 费 7/6

            // ---- 手工摆盘：我方支援线一个 / 敌方支援线一个 / 敌方前线一个 ----
            // 注意：双方支援线的**第 0 格都是 HQ**（StartMatch 已经放好），下面这些都加在它后面。
            UnitInstance mySupport = new UnitInstance(st.NextUnitInstanceId++, mg, 0);
            mySupport.Line = Line.Support;
            st.Players[0].SupportLine.Add(mySupport);

            UnitInstance foeSupport = new UnitInstance(st.NextUnitInstanceId++, scout, 1);
            foeSupport.Line = Line.Support;
            st.Players[1].SupportLine.Add(foeSupport);

            UnitInstance foeFront = new UnitInstance(st.NextUnitInstanceId++, para, 1);
            foeFront.Line = Line.Front;
            st.Frontline.Add(foeFront);

            st.Players[0].Resources = 99;   // [STAGE2C] 行动费现在从资源扣，先给足额度让闸门检查有意义

            // ⚠️ [阶段2.5 · 任务D] 夹具 `mySupport` 是**机枪阵地（步兵）** —— 下面三条线位结论
            // 只代表"步兵/坦克"；越线兵种（火炮/战斗机/轰炸机）在支援线上可以打到支援线与 HQ，
            // 它们的**对偶断言**在**第 10 项**。三行断言的文字已加"步兵"限定，防止报告说谎。
            // ⚠️ r3 / r4 分开：早先两条断言共用 r3，"不能打 HQ"那行显示的是被后一个调用
            // （CanUnitMove）覆盖掉的 reason —— 结论没错但理由错位，属于"报告说谎"，已修。
            string r1 = "", r2 = "", r3 = "", r4 = "";
            bool isFrontOwner = st.FrontlineOwner == 1;
            bool supportCannotHitSupport = !eng.CanAttackTarget(mySupport, foeSupport, out r1);
            bool supportCanHitFront = eng.CanAttackTarget(mySupport, foeFront, out r2);
            bool supportCannotHitHq = !eng.CanAttackHq(mySupport, out r3);
            bool cannotMoveIntoEnemyFront = !eng.CanUnitMove(mySupport, out r4);

            sb.AppendLine("### 7.1 线位与攻击关系（棋盘：前线归电脑所有；攻击者 = 我方支援线的**步兵**）");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| FrontlineOwner == 1（前线归电脑） | " + Mark(isFrontOwner) + " |");
            sb.AppendLine("| 我方支援线**步兵**不能打敌方支援线单位 | " + Mark(supportCannotHitSupport) +
                          (r1.Length > 0 ? "<br>" + r1 : "") + " |");
            sb.AppendLine("| 我方支援线**步兵**可以打敌方前线单位 | " + Mark(supportCanHitFront) + " |");
            sb.AppendLine("| 我方支援线**步兵**不能打敌方 HQ | " + Mark(supportCannotHitHq) +
                          (r3.Length > 0 ? "<br>" + r3 : "") + " |");
            sb.AppendLine("| 前线被电脑占领时我方无法移入 | " + Mark(cannotMoveIntoEnemyFront) + " |");
            sb.AppendLine();
            sb.AppendLine("> [阶段2.5 · 任务D] 以上三条的限定词是**步兵**（机枪阵地 = `unit_mg_nest`）——");
            sb.AppendLine("> \"支援线限制\"只对**步兵/坦克**成立；越线兵种（火炮/战斗机/轰炸机）在支援线上");
            sb.AppendLine("> 可以打到敌方支援线与 HQ，其**对偶断言**见**第 10 项**。");
            sb.AppendLine();

            // ---- 前线清空 → 归属回到无人占领；再验证行动点闸门 ----
            st.Frontline.Clear();
            bool frontCleared = st.FrontlineOwner == -1;
            bool nowMovable = eng.CanUnitMove(mySupport, out r3);

            st.Players[0].Resources = 0;
            bool blockedByResources = !eng.CanUnitMove(mySupport, out r3);
            string resourceReason = r3;
            st.Players[0].Resources = 99;
            bool againMovable = eng.CanUnitMove(mySupport, out r3);

            sb.AppendLine("### 7.2 前线归属清空与资源闸门");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 前线单位清空后 FrontlineOwner == -1 | " + Mark(frontCleared) + " |");
            sb.AppendLine("| 此时我方可以移入 | " + Mark(nowMovable) + " |");
            sb.AppendLine("| **资源归零后不能移入** | " + Mark(blockedByResources) +
                          (resourceReason.Length > 0 ? "<br>" + resourceReason : "") + " |");
            sb.AppendLine("| 资源恢复后又可以移入 | " + Mark(againMovable) + " |");
            sb.AppendLine();

            // ---- 容量 ----
            // [STAGE2C] 支援线里已经有 HQ + mySupport 两个，这里一直加到"满"为止。
            // 断言它恰好停在容量上限（含 HQ 的格数），顺带验证了"HQ 占一格"这条规则。
            // ⚠️ 这里的期望值**不许写死数字** —— 容量是 `GameEngine.SupportLineCapacity`，
            //    2026-10-08 提交 B 把它从 4 改成 5，写死会让报告说谎（见 §7.3 那两行的写法）。
            int fillGuard = 0;
            while (!st.Players[0].IsSupportLineFull && fillGuard++ < 12)
            {
                UnitInstance u = new UnitInstance(st.NextUnitInstanceId++, scout, 0);
                u.Line = Line.Support;
                st.Players[0].SupportLine.Add(u);
            }
            int supportCount = st.Players[0].SupportLine.Count;
            bool supportFull = st.Players[0].IsSupportLineFull;
            bool hqTakesOneSlot = supportFull
                                  && supportCount == GameEngine.SupportLineCapacity
                                  && st.Players[0].SupportLine[0].IsHq;

            // 清掉刚才为测容量塞进去的临时单位，但**保留 HQ 与 mySupport** ——
            // 否则后面的 CanUnitMove 会对着一个已经不在场上的对象做判断。
            for (int i = st.Players[0].SupportLine.Count - 1; i >= 0; i--)
            {
                UnitInstance u = st.Players[0].SupportLine[i];
                if (u != mySupport && !u.IsHq) st.Players[0].SupportLine.RemoveAt(i);
            }

            for (int i = 0; i < GameEngine.FrontlineCapacity; i++)
            {
                UnitInstance u = new UnitInstance(st.NextUnitInstanceId++, scout, 0);
                u.Line = Line.Front;
                st.Frontline.Add(u);
            }
            bool frontlineFullBlocks = !eng.CanUnitMove(mySupport, out r3);
            string frontlineFullReason = r3;

            sb.AppendLine("### 7.3 容量");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 支援阵线放满 " + GameEngine.SupportLineCapacity + " 个后 IsSupportLineFull | " +
                          Mark(supportFull) + " |");
            sb.AppendLine("| **满格时恰好 " + GameEngine.SupportLineCapacity + " 个，且第 0 格是 HQ**" +
                          "（HQ 占一格 ⇒ 除 HQ 外只能放 " + (GameEngine.SupportLineCapacity - 1) + " 个） | " +
                          Mark(hqTakesOneSlot) + "（实测 " + supportCount + " 个） |");
            sb.AppendLine("| 前线放满 " + GameEngine.FrontlineCapacity + " 个后无法再移入 | " +
                          Mark(frontlineFullBlocks) +
                          (frontlineFullReason.Length > 0 ? "<br>" + frontlineFullReason : "") + " |");
            sb.AppendLine();

            // ---- 行动费分档 ----
            int c1 = GameEngine.ActionCostOf(scout);
            int c2 = GameEngine.ActionCostOf(mg);
            int c3 = GameEngine.ActionCostOf(para);
            int c4 = GameEngine.ActionCostOf(tank);
            bool costOk = (c1 == 1) && (c2 == 1) && (c3 == 2) && (c4 == 3);

            sb.AppendLine("### 7.4 行动费分档（1~2 费 → 1 点，3~4 费 → 2 点，5~6 费 → 3 点）");
            sb.AppendLine();
            sb.AppendLine("| 卡 | 费用 | 行动费 | 期望 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| " + scout.Name + " | " + scout.Cost + " | " + c1 + " | 1 |");
            sb.AppendLine("| " + mg.Name + " | " + mg.Cost + " | " + c2 + " | 1 |");
            sb.AppendLine("| " + para.Name + " | " + para.Cost + " | " + c3 + " | 2 |");
            sb.AppendLine("| " + tank.Name + " | " + tank.Cost + " | " + c4 + " | 3 |");
            sb.AppendLine();
            sb.AppendLine("**这笔费用从资源池里扣**（与部署共用同一笔钱，见 §2.7）：" +
                          "最强的" + tank.Name + " 要 " + c4 + " 点，所以资源紧张时它可能动不了 —— " +
                          "这正是「打大牌还是多动一次」这层取舍的来源。");
            sb.AppendLine();

            ok = isFrontOwner && supportCannotHitSupport && supportCanHitFront && supportCannotHitHq
                 && cannotMoveIntoEnemyFront && frontCleared && nowMovable && blockedByResources && againMovable
                 && supportFull && hqTakesOneSlot && frontlineFullBlocks && costOk;

            sb.AppendLine("**结论：" + (ok
                ? "三条战线的线位规则、前线归属与两侧容量（含 HQ 占位）、行动费分档全部符合设计文档 §7.1 / §2.7。"
                : "三条战线或行动费行为不符预期，详见上表。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 8 项 · HQ 单位与疲劳结算（[STAGE2C] 新增）
        // ==================================================================

        private static string RunHqUnitAndFatigue(out bool ok, out string title)
        {
            title = "HQ 单位与疲劳：HQ 占支援线一格 / 不可移动、不可被指令指定 / 只有前线单位能打它 / 牌库空后按 2^k 扣血";
            ok = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 8 项 · HQ 单位与疲劳结算（设计文档 §6.1.2 / §5.2.1）");
            sb.AppendLine();
            sb.AppendLine("两件事合在一项里，因为它们都是本阶段对「怎么输」的改写：");
            sb.AppendLine("HQ 从抽象血量变成场上一个单位；L2 从「牌库抽空即判负」变成「抽空后挨指数伤害」。");
            sb.AppendLine();

            // ---------------- A~D：HQ 的形态与攻击关系 ----------------
            GameEngine eng = new GameEngine(7u, MatchMode.AiVsAi);
            eng.StartMatch();
            GameState st = eng.State;

            CardDef scout = CardDb.Get("unit_scout");
            CardDef strike = CardDb.Get("order_strike");

            bool hqInSlot0 = st.Players[0].SupportLine.Count > 0 && st.Players[0].SupportLine[0].IsHq
                             && st.Players[0].SupportLine[0].OwnerIndex == 0
                             && st.Players[1].SupportLine.Count > 0 && st.Players[1].SupportLine[0].IsHq;
            bool hqHealthOk = st.Players[0].HqHealth == GameEngine.HqStartingHealth
                              && st.Players[1].HqHealth == GameEngine.HqStartingHealth;

            string r1 = "";
            bool hqCannotMove = !eng.CanUnitMove(st.Players[0].Hq, out r1);
            bool hqCannotAct = !eng.CanUnitAttack(st.Players[0].Hq, out r1);

            string r2 = "";
            bool hqNotTargetableByOrder = !eng.CanTargetUnit(strike, st.Players[1].Hq, out r2);

            // 摆一个我方支援线单位：它够不到敌方 HQ
            UnitInstance mine = new UnitInstance(st.NextUnitInstanceId++, scout, 0);
            mine.Line = Line.Support;
            st.Players[0].SupportLine.Add(mine);
            string r3 = "";
            bool supportCannotHitHq = !eng.CanAttackHq(mine, out r3);

            // 把它移上前线，就够得到了
            mine.Line = Line.Front;
            st.Players[0].SupportLine.Remove(mine);
            st.Frontline.Add(mine);
            st.Players[0].Resources = 99;
            mine.HasMovedThisTurn = false;
            mine.HasAttackedThisTurn = false;
            bool frontCanHitHq = eng.CanAttackHq(mine, out r3);

            int foeHpBefore = st.Players[1].HqHealth;
            bool hitOk = eng.AttackWithUnit(mine.InstanceId, -1);
            int foeHpAfter = st.Players[1].HqHealth;
            // 只有 HQ 掉血、攻击者毫发无伤 —— 这正是"HQ 攻击力为 0"换来的结果（无需方向特判）
            bool hqTookDamageOnly = hitOk
                                    && (foeHpBefore - foeHpAfter == mine.Attack)
                                    && (mine.Health == mine.MaxHealth);

            sb.AppendLine("### 8.1 HQ 的形态");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 双方支援线第 0 格都是自己的 HQ | " + Mark(hqInSlot0) + " |");
            sb.AppendLine("| HQ 初始血量 = " + GameEngine.HqStartingHealth + " | " + Mark(hqHealthOk) + " |");
            sb.AppendLine("| HQ 不能移动 | " + Mark(hqCannotMove) + (r1.Length > 0 ? "<br>" + r1 : "") + " |");
            sb.AppendLine("| HQ 不能攻击（它不是可行动单位） | " + Mark(hqCannotAct) + " |");
            sb.AppendLine("| 指令卡不能指定 HQ 为目标 | " + Mark(hqNotTargetableByOrder) +
                          (r2.Length > 0 ? "<br>" + r2 : "") + " |");
            sb.AppendLine();
            sb.AppendLine("### 8.2 打 HQ 的唯一陆路");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 支援线单位不能攻击敌方 HQ | " + Mark(supportCannotHitHq) +
                          (r3.Length > 0 ? "<br>" + r3 : "") + " |");
            sb.AppendLine("| 前线单位可以攻击敌方 HQ | " + Mark(frontCanHitHq) + " |");
            sb.AppendLine("| 打 HQ 只有 HQ 掉血（" + foeHpBefore + " → " + foeHpAfter +
                          "），攻击者毫发无伤（" + mine.Health + "/" + mine.MaxHealth + "） | " +
                          Mark(hqTookDamageOnly) + " |");
            sb.AppendLine();

            // ---------------- E：HQ 占支援线一格 ----------------
            // 此刻我方支援线里只剩 HQ（mine 已上前线），一直加到满
            int fillGuard = 0;
            while (!st.Players[0].IsSupportLineFull && fillGuard++ < 12)
            {
                UnitInstance u = new UnitInstance(st.NextUnitInstanceId++, scout, 0);
                u.Line = Line.Support;
                st.Players[0].SupportLine.Add(u);
            }
            int supportCount = st.Players[0].SupportLine.Count;
            bool hqTakesOneSlot = st.Players[0].IsSupportLineFull
                                  && supportCount == GameEngine.SupportLineCapacity
                                  && st.Players[0].SupportLine[0].IsHq;

            sb.AppendLine("### 8.3 HQ 占支援线一格");
            sb.AppendLine();
            sb.AppendLine("支援线总容量 `SupportLineCapacity = " + GameEngine.SupportLineCapacity +
                          "`，**含 HQ**，所以除 HQ 外只能再部署 " + (GameEngine.SupportLineCapacity - 1) + " 个单位。");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 塞满后恰好 " + GameEngine.SupportLineCapacity + " 个，且第 0 格是 HQ | " +
                          Mark(hqTakesOneSlot) + "（实测 " + supportCount + " 个） |");
            sb.AppendLine();

            // ---------------- F：疲劳（2^k，各方独立） ----------------
            GameEngine fe = new GameEngine(11u, MatchMode.AiVsAi);
            fe.StartMatch();
            GameState fs = fe.State;

            fs.Players[0].Deck.Clear();          // 手工清空**我方**牌库
            int handCountBefore = fs.Players[0].Hand.Count;
            int[] dmg = new int[3];
            for (int k = 0; k < 3; k++)
            {
                int before = fs.Players[0].HqHealth;
                fe.EndTurn();                                     // 我方结束回合 → 第 k+1 次疲劳
                dmg[k] = before - fs.Players[0].HqHealth;
                if (!fs.IsOver) fe.EndTurn();                     // 电脑回合（它牌库没空，不该吃疲劳）
                if (fs.IsOver) break;
            }
            bool fatigueSeq = dmg[0] == 2 && dmg[1] == 4 && dmg[2] == 8;
            // "牌库空不抽牌"：自检里没有 AI 出牌，手牌数只受抽牌影响，所以它不变就等于没抽
            bool noDrawWhenEmpty = fs.Players[0].Hand.Count == handCountBefore
                                   && fs.Players[0].Deck.Count == 0;
            bool foeUnaffected = fs.Players[1].HqHealth == GameEngine.HqStartingHealth;

            // 继续推到"疲劳把人打死"，确认它走的是 HQ 归零这条**统一**判负路径
            int deathGuard = 0;
            while (!fs.IsOver && deathGuard++ < 20) fe.EndTurn();
            bool fatigueCanKill = fs.IsOver
                                  && fs.Result == MatchResult.Player1Win
                                  && fs.ResultReason.Contains("疲劳");

            sb.AppendLine("### 8.4 疲劳结算（牌库空后的第 k 次结束回合扣 2^k）");
            sb.AppendLine();
            sb.AppendLine("只清空**我方**牌库，然后推进回合。关键口径：牌库空 → 不抽牌、**不判负**，");
            sb.AppendLine("改成「自己每次回合结束扣 2^k」，且**各方独立计数**（对手牌库没空就完全不受影响）。");
            sb.AppendLine();
            sb.AppendLine("| 次数 | 期望伤害 | 实测伤害 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| 第 1 次 | 2 | " + dmg[0] + " |");
            sb.AppendLine("| 第 2 次 | 4 | " + dmg[1] + " |");
            sb.AppendLine("| 第 3 次 | 8 | " + dmg[2] + " |");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 伤害序列 = 2 / 4 / 8 | " + Mark(fatigueSeq) + " |");
            sb.AppendLine("| 牌库空后不再抽牌（手牌数不变） | " + Mark(noDrawWhenEmpty) + " |");
            sb.AppendLine("| 对手牌库没空 ⇒ 完全不受疲劳影响 | " + Mark(foeUnaffected) + " |");
            sb.AppendLine("| 一直推下去会由疲劳击破 HQ 而终局（2+4+8+16 = 30 > 20） | " +
                          Mark(fatigueCanKill) + " |");
            sb.AppendLine();

            ok = hqInSlot0 && hqHealthOk && hqCannotMove && hqCannotAct && hqNotTargetableByOrder
                 && supportCannotHitHq && frontCanHitHq && hqTookDamageOnly
                 && hqTakesOneSlot
                 && fatigueSeq && noDrawWhenEmpty && foeUnaffected && fatigueCanKill;

            sb.AppendLine("**结论：" + (ok
                ? "HQ 作为后线单位的一切行为（占位 / 不可行动 / 不可被指令指定 / 只有前线能打 / 不反击）" +
                  "与疲劳的「不抽牌 + 2^k + 各方独立 + 统一判负」全部符合设计文档 §6.1.2 / §5.2.1。"
                : "HQ 单位或疲劳结算行为不符预期，详见上表。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 9 项 · 兵种反击例外（[阶段2.5 · 任务 A] 新增；[提交 C] 补"轰炸机打战斗机"例外）
        // ==================================================================

        private static string RunUnitTypeCounterRules(out bool ok, out string title)
        {
            title = "兵种反击例外：火炮免承受、轰炸机免承受且不还手（打战斗机除外）、战斗机与步兵坦克正常双向";
            ok = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 9 项 · 兵种反击例外（设计文档 §10.2 / §12.2）");
            sb.AppendLine();
            sb.AppendLine("### 9.0 为什么必须手工摆棋盘");
            sb.AppendLine();
            sb.AppendLine("这些规则在随机对局里触发概率极低 —— 池内只有 **" +
                          CardDb.CountUnitsOfKind(UnitType.Artillery) + " 张火炮、" +
                          CardDb.CountUnitsOfKind(UnitType.Fighter) + " 张战斗机、" +
                          CardDb.CountUnitsOfKind(UnitType.Bomber) + " 张轰炸机**，");
            sb.AppendLine("靠 200 局抽样等于没测。所以下面每条规则都用**独立构造的一局**来断言。");
            sb.AppendLine();
            sb.AppendLine("⚠️ **期望值全部硬编码。** 绝不调用 `CounterDamage` 去算期望值 —— 那是循环论证，");
            sb.AppendLine("被测代码写错了也会通过。这是本项唯一有意义的写法。");
            sb.AppendLine();
            sb.AppendLine("夹具：清空双方手牌与牌库（本项不推进回合，所以不会触发疲劳）、资源给 99（让行动费闸门不参与）；");
            sb.AppendLine("攻击者放在**我方支援阵线**，目标放在**敌方前线**（支援线单位唯一够得着的敌人）。");
            sb.AppendLine();
            sb.AppendLine("> [阶段2.5 · 提交 C] 起战斗机 / 轰炸机**已经进卡池**，但本项仍用**局部夹具**");
            sb.AppendLine("> `_test_fighter`(3费4/3) / `_test_bomber`(4费4/5)，**不**直接取池内实体：");
            sb.AppendLine("> 下表的「期望掉血」是**硬编码**的，用池内实体的话，以后每调一次卡面数值都要回来改期望值。");
            sb.AppendLine("> 夹具不放进 `CardDb.Cards`，所以不会进牌库、不影响任何真实对局。");
            sb.AppendLine();

            CardDef fighter = new CardDef("_test_fighter", "战斗机(测试)", 3, 2,
                                          CardType.Unit, UnitType.Fighter, 4, 3, EffectId.None, 0, "自检夹具，不进卡池");
            CardDef bomber = new CardDef("_test_bomber", "轰炸机(测试)", 4, 2,
                                         CardType.Unit, UnitType.Bomber, 4, 5, EffectId.None, 0, "自检夹具，不进卡池");

            CardDef mg = CardDb.Get("unit_mg_nest");        // 步兵 2/3
            CardDef para = CardDb.Get("unit_paratrooper");  // 步兵 3/4
            CardDef armored = CardDb.Get("unit_armored");   // 坦克 4/5
            CardDef howitzer = CardDb.Get("unit_howitzer"); // 火炮 6/5

            // ---- 9.1 单位互攻的反击矩阵 ----
            // ⚠️ 后三条是 [阶段2.5 · 提交 C] 新增，专门锁定「轰炸机打战斗机」这条例外的**方向**：
            //   第 9 条是例外本身；第 8 / 10 条是对照 —— 证明例外**只在战斗机身上生效**，
            //   而不是把"轰炸机当攻击者就一律挨反击"写成了通用规则。
            string[] labels = {
                "步兵 2/3 → 步兵 3/4（基准双向结算）",
                "坦克 4/5 → 步兵 3/4（坦克不吃例外）",
                "战斗机 4/3 → 步兵 3/4（战斗机正常承受反击）",
                "火炮 6/5 → 坦克 4/5（火炮**不受**反击）",
                "轰炸机 4/5 → 坦克 4/5（轰炸机**不承受**反击）",
                "步兵 2/3 → 火炮 6/5（火炮当防守方**照常还手**）",
                "步兵 2/3 → 轰炸机 4/5（轰炸机**不造成**反击）",
                "战斗机 4/3 → 轰炸机 4/5（轰炸机当防守方**一律不还手**）",
                "轰炸机 4/5 → 战斗机 4/3（**例外：打战斗机要挨反击**）",
                "轰炸机 4/5 → 步兵 3/4（对照：打非战斗机仍**不承受**）"
            };
            CardDef[] atkDefs = { mg, armored, fighter, howitzer, bomber, mg, mg, fighter, bomber, bomber };
            CardDef[] tgtDefs = { para, para, para, armored, armored, howitzer, bomber, bomber, fighter, para };
            int[] expAtkLoss = { 3, 3, 3, 0, 0, 6, 0, 0, 4, 0 };
            int[] expTgtLoss = { 2, 4, 4, 6, 4, 2, 2, 4, 4, 4 };

            sb.AppendLine("### 9.1 单位互攻的反击矩阵");
            sb.AppendLine();
            sb.AppendLine("| 场景 | 攻击者掉血 | 目标掉血 | 结果 |");
            sb.AppendLine("|---|---|---|---|");

            bool allExchange = true;
            for (int i = 0; i < labels.Length; i++)
            {
                GameEngine eng = NewCounterFixture((uint)(500 + i));
                UnitInstance atk = PlaceUnit(eng, atkDefs[i], 0, Line.Support);
                UnitInstance tgt = PlaceUnit(eng, tgtDefs[i], 1, Line.Front);

                int a0 = atk.Health, t0 = tgt.Health;
                bool attacked = eng.AttackWithUnit(atk.InstanceId, tgt.InstanceId);
                int aLoss = a0 - atk.Health;
                int tLoss = t0 - tgt.Health;

                bool pass = attacked && aLoss == expAtkLoss[i] && tLoss == expTgtLoss[i];
                if (!pass) allExchange = false;
                sb.AppendLine("| " + labels[i] + " | " + aLoss + "（期望 " + expAtkLoss[i] + "） | " +
                              tLoss + "（期望 " + expTgtLoss[i] + "） | " + Mark(pass) + " |");
            }
            sb.AppendLine();

            // ---- 9.2 打 HQ（含对照组）----
            // ⚠️ 这一节必须带对照组：
            //   "轰炸机打 HQ 不掉血"有两条独立理由（HQ 攻 0 + 轰炸机免反击），
            //   所以它单独**证明不了**轰炸机免反击生效了。
            //   必须配一个"步兵打 HQ 也不掉血"的对照 —— 后者只可能因为 HQ 攻 0。
            sb.AppendLine("### 9.2 打 HQ（带对照组）");
            sb.AppendLine();
            sb.AppendLine("| 场景 | 攻击者掉血 | 敌方 HQ 掉血 | 结果 |");
            sb.AppendLine("|---|---|---|---|");

            string[] hqLabels = { "轰炸机 4/5 → 敌方 HQ", "步兵 2/3 → 敌方 HQ（**对照组**）" };
            CardDef[] hqAtkDefs = { bomber, mg };
            int[] hqExpAtkLoss = { 0, 0 };
            int[] hqExpHqLoss = { 4, 2 };

            bool allHq = true;
            for (int i = 0; i < hqLabels.Length; i++)
            {
                GameEngine eng = NewCounterFixture((uint)(600 + i));
                UnitInstance atk = PlaceUnit(eng, hqAtkDefs[i], 0, Line.Front);   // 只有前线单位够得着 HQ

                int a0 = atk.Health;
                int h0 = eng.State.Players[1].HqHealth;
                bool attacked = eng.AttackWithUnit(atk.InstanceId, -1);           // -1 = 敌方 HQ
                int aLoss = a0 - atk.Health;
                int hqLoss = h0 - eng.State.Players[1].HqHealth;

                bool pass = attacked && aLoss == hqExpAtkLoss[i] && hqLoss == hqExpHqLoss[i];
                if (!pass) allHq = false;
                sb.AppendLine("| " + hqLabels[i] + " | " + aLoss + "（期望 " + hqExpAtkLoss[i] + "） | " +
                              hqLoss + "（期望 " + hqExpHqLoss[i] + "） | " + Mark(pass) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("对照组说明：步兵**没有**任何免反击规则，它打 HQ 也不掉血 ⇒ 唯一原因是 **HQ 攻 0**。");
            sb.AppendLine("⇒ 「打 HQ 不受反击」仍然由数值保证，而不是被兵种规则顺带覆盖掉的。");
            sb.AppendLine();

            // ---- 9.3 池内兵种分布（用于判断本轮 200 局变化多少算合理）----
            UnitType[] kinds = { UnitType.Infantry, UnitType.Tank, UnitType.Artillery,
                                 UnitType.Fighter, UnitType.Bomber };
            string[] kindNames = { "步兵", "坦克", "火炮", "战斗机", "轰炸机" };

            sb.AppendLine("### 9.3 池内兵种分布（判断 200 局变化幅度的依据）");
            sb.AppendLine();
            sb.AppendLine("| 兵种 | 单位卡张数 |");
            sb.AppendLine("|---|---|");
            for (int i = 0; i < kinds.Length; i++)
            {
                sb.AppendLine("| " + kindNames[i] + " | " + CardDb.CountUnitsOfKind(kinds[i]) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("⇒ [阶段2.5 · 提交 C] 本轮**同时改了三件事**：两只空军进池（+2 张卡 ⇒ 牌库 36 → 42）、");
            sb.AppendLine("`MaxTurns` 72 → 88、以及「轰炸机打战斗机要挨反击」这条例外。");
            sb.AppendLine("⇒ **200 局与提交 B 的逐位可比性不再成立**（卡池顺序变了，牌库构造顺序随之全变），");
            sb.AppendLine("本轮 200 局只能验证三条硬性质：**全部在回合上限内终止 / L3 仍为 0 / 先手偏差仍在可用区间**；");
            sb.AppendLine("「规则矩阵正确」由上面的手工摆盘负责，「牌库真的变成 42 张」由本节的池内分布与 §9.4 负责。");
            sb.AppendLine("**不要再拿本轮数字去和提交 B 做逐位对比 —— 那不是归因，那是刻舟求剑。**");
            sb.AppendLine();

            // ---- 9.4 数据层断言 ----
            bool howitzerOnlyArtillery = CardDb.CountUnitsOfKind(UnitType.Artillery) == 1
                                         && howitzer.UnitKind == UnitType.Artillery;
            bool airInPoolExactlyOne = CardDb.CountUnitsOfKind(UnitType.Fighter) == 1
                                       && CardDb.CountUnitsOfKind(UnitType.Bomber) == 1
                                       && CardDb.Cards.Length == 14
                                       && CardDb.Units.Length == 10
                                       && CardDb.BuildDeck(GameEngine.DeckCopiesPerCard).Count == 42;

            // [阶段2.5 · 提交 C] 把设计文档 §10.6 的推导**落成断言**，而不是只写在文档里：
            //   自然终止点上界 = 2D − 1（D = 牌库张数）。MaxTurns 必须盖住它，
            //   否则 L3 会抢在疲劳结算完之前触发，"L3 触发 0 局"这条性质会被打破。
            //   这条断言会在「改牌库张数」或「改 DeckCopiesPerCard」时立刻报警 ——
            //   它拦的正是历史上连续坑过三轮的那类改动。
            int deckCount = CardDb.BuildDeck(GameEngine.DeckCopiesPerCard).Count;
            bool maxTurnsCoversDeck = GameEngine.MaxTurns >= 2 * deckCount - 1;
            bool hqAttackZero = CardDb.Hq.Attack == 0 && CardDb.Hq.UnitKind == UnitType.None;
            bool actionCostIsPureField = true;
            for (int i = 0; i < CardDb.Cards.Length; i++)
            {
                if (GameEngine.ActionCostOf(CardDb.Cards[i]) != CardDb.Cards[i].ActionCost)
                    actionCostIsPureField = false;
            }
            if (GameEngine.ActionCostOf(CardDb.Hq) != CardDb.Hq.ActionCost) actionCostIsPureField = false;

            sb.AppendLine("### 9.4 数据层断言");
            sb.AppendLine();
            sb.AppendLine("| 检查 | 结果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 池内火炮只有榴弹炮一张（⇒ 火炮类规则只影响它一张卡） | " + Mark(howitzerOnlyArtillery) + " |");
            sb.AppendLine("| 战斗机 / 轰炸机各恰好 1 张已在池内；卡池 14 张 / 单位 10 张 / 牌库 42 张 | " + Mark(airInPoolExactlyOne) + " |");
            sb.AppendLine("| **`MaxTurns` 盖得住自然终止点上界**（`" + GameEngine.MaxTurns + " >= 2 × " + deckCount +
                          " − 1 = " + (2 * deckCount - 1) + "`） | " + Mark(maxTurnsCoversDeck) + " |");
            sb.AppendLine("| HQ 攻击力为 0 且不是兵种 | " + Mark(hqAttackZero) + " |");
            sb.AppendLine("| `ActionCostOf` 已退化为一行取值（与 `CardDef.ActionCost` 逐卡一致） | " + Mark(actionCostIsPureField) + " |");
            sb.AppendLine();

            ok = allExchange && allHq && howitzerOnlyArtillery && airInPoolExactlyOne && maxTurnsCoversDeck
                 && hqAttackZero && actionCostIsPureField;

            sb.AppendLine("**结论：" + (ok
                ? "兵种反击例外全部符合设计文档 §10.2 / §12.2 —— 火炮只免「承受」不免「造成」；"
                  + "轰炸机不造成反击（当防守方不还手），当攻击者时也不受反击**但打战斗机例外**；"
                  + "战斗机与步兵 / 坦克正常双向结算；打 HQ 仍由「HQ 攻 0」保证。"
                : "兵种反击例外行为不符预期，详见上表。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 10 项 · 越线射程与战斗机拦截（[阶段2.5 · 任务D] 新增）
        // ==================================================================

        private static string RunLongRangeAndInterception(out bool ok, out string title)
        {
            title = "越线射程与战斗机拦截：火炮/战斗机/轰炸机可打任意战线（含 HQ）；" +
                    "轰炸机不能打有敌方战斗机战线上的其他目标（战斗机本身可打）";
            ok = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 10 项 · 越线射程与战斗机拦截（设计文档 §13）");
            sb.AppendLine();
            sb.AppendLine("### 10.0 为什么必须手工摆棋盘");
            sb.AppendLine();
            sb.AppendLine("射程与拦截是**全称规则**（每一局的每一次攻击都要过这道判定），但组合正确性");
            sb.AppendLine("在随机对局里几乎抽不齐（\"轰炸机正好被战斗机拦\"这类盘面很罕见）——");
            sb.AppendLine("所以下面每条组合都用**独立构造的一局**来断言。");
            sb.AppendLine();
            sb.AppendLine("⚠️ **期望值全部硬编码**（合法性 bool 与 reason 关键词），绝不调用 `CanAttackTarget`");
            sb.AppendLine("去算期望值 —— 那是循环论证，被测代码写错了也会通过（与第 9 项同一条纪律）。");
            sb.AppendLine();
            sb.AppendLine("夹具：`NewCounterFixture`（清空双方手牌与牌库、资源 99；本项不推进回合，不会触发疲劳），");
            sb.AppendLine("用**池内真实卡**摆盘（本项断言的是合法性 bool，不依赖数值，不受以后调卡影响）。");
            sb.AppendLine();

            CardDef scout = CardDb.Get("unit_scout");       // 步兵 1/2
            CardDef mg = CardDb.Get("unit_mg_nest");        // 步兵 2/3
            CardDef howitzer = CardDb.Get("unit_howitzer"); // 火炮 6/5
            CardDef fighter = CardDb.Get("unit_fighter");   // 战斗机 4/3
            CardDef bomber = CardDb.Get("unit_bomber");     // 轰炸机 4/5

            // ---- 10.1 射程分岔 ----
            // 棋盘：我方支援线放四种攻击者；我方前线一个步兵；敌方支援线一个步兵（另有 HQ）。
            GameEngine eng = NewCounterFixture(700);
            UnitInstance foeSupportUnit = PlaceUnit(eng, scout, 1, Line.Support);
            UnitInstance infFront = PlaceUnit(eng, scout, 0, Line.Front);
            UnitInstance infSup = PlaceUnit(eng, mg, 0, Line.Support);
            UnitInstance artSup = PlaceUnit(eng, howitzer, 0, Line.Support);
            UnitInstance ftrSup = PlaceUnit(eng, fighter, 0, Line.Support);
            UnitInstance bmbSup = PlaceUnit(eng, bomber, 0, Line.Support);

            string r = "";
            bool a1 = !eng.CanAttackTarget(infSup, foeSupportUnit, out r);   // 步兵（支援线）→ 敌方支援线：拒绝
            string a1r = r;
            bool a2 = !eng.CanAttackHq(infSup, out r);                       // 步兵（支援线）→ HQ：拒绝
            string a2r = r;
            bool a3 = eng.CanAttackHq(infFront, out r);                      // 步兵（前线）→ HQ：通过
            bool a4 = eng.CanAttackTarget(artSup, foeSupportUnit, out r);    // 火炮 → 敌方支援线：通过
            bool a5 = eng.CanAttackHq(artSup, out r);                        // 火炮 → HQ：通过
            bool a6 = eng.CanAttackTarget(ftrSup, foeSupportUnit, out r);    // 战斗机 → 敌方支援线：通过
            bool a7 = eng.CanAttackHq(ftrSup, out r);                        // 战斗机 → HQ：通过
            bool a8 = eng.CanAttackTarget(bmbSup, foeSupportUnit, out r);    // 轰炸机 → 敌方支援线（无战斗机）：通过
            bool a9 = eng.CanAttackHq(bmbSup, out r);                        // 轰炸机 → HQ（无战斗机）：通过

            // ---- 10.2 战斗机拦截 ----
            // 棋盘 B：敌方**前线**是"步兵 + 战斗机"（战斗机保护同线其他目标）
            GameEngine engB = NewCounterFixture(701);
            UnitInstance bmbB = PlaceUnit(engB, bomber, 0, Line.Support);
            UnitInstance foeFrontInf = PlaceUnit(engB, scout, 1, Line.Front);
            UnitInstance foeFrontFtr = PlaceUnit(engB, fighter, 1, Line.Front);

            string rB = "";
            bool b10 = !engB.CanAttackTarget(bmbB, foeFrontInf, out rB) && rB.Contains("拦截");
            string b10r = rB;
            bool b11 = engB.CanAttackTarget(bmbB, foeFrontFtr, out rB);      // 打那架战斗机本身：通过

            // 棋盘 C：敌方**支援线**是"HQ + 战斗机"（战斗机保护 HQ 与支援线其他目标）
            GameEngine engC = NewCounterFixture(702);
            UnitInstance bmbC = PlaceUnit(engC, bomber, 0, Line.Support);
            UnitInstance foeSupFtr = PlaceUnit(engC, fighter, 1, Line.Support);

            string rC = "";
            bool b12 = !engC.CanAttackHq(bmbC, out rC) && rC.Contains("拦截");   // 轰炸机 → HQ：拒绝
            string b12r = rC;
            bool b13 = engC.CanAttackTarget(bmbC, foeSupFtr, out rC);            // 打支援线的战斗机本身：通过

            // 棋盘 D：敌方战斗机在**前线**、轰炸机打**敌方支援线**目标 —— 跨线不拦
            GameEngine engD = NewCounterFixture(703);
            UnitInstance bmbD = PlaceUnit(engD, bomber, 0, Line.Support);
            PlaceUnit(engD, fighter, 1, Line.Front);           // 场景元素：另一条线（前线）上有敌方战斗机
            UnitInstance foeSupInfD = PlaceUnit(engD, scout, 1, Line.Support);

            string rD = "";
            bool b14 = engD.CanAttackTarget(bmbD, foeSupInfD, out rD);       // 跨线：通过（拦截只保护同线）

            // 棋盘 E：对照 —— 目标战线没有战斗机，轰炸机正常打
            GameEngine engE = NewCounterFixture(704);
            UnitInstance bmbE = PlaceUnit(engE, bomber, 0, Line.Support);
            UnitInstance foeFrontE = PlaceUnit(engE, scout, 1, Line.Front);

            string rE = "";
            bool b15 = engE.CanAttackTarget(bmbE, foeFrontE, out rE);

            // 棋盘 F：对照 —— 拦截只拦轰炸机：火炮打"有敌方战斗机的战线"目标不受影响
            GameEngine engF = NewCounterFixture(705);
            UnitInstance artF = PlaceUnit(engF, howitzer, 0, Line.Support);
            PlaceUnit(engF, fighter, 1, Line.Front);           // 场景元素：该战线有敌方战斗机（但拦不了火炮）
            UnitInstance foeFrontInfF = PlaceUnit(engF, scout, 1, Line.Front);

            string rF = "";
            bool b16 = engF.CanAttackTarget(artF, foeFrontInfF, out rF);

            // ---- 10.3 端到端：被拦的攻击真的发生不了 ----
            GameEngine engG = NewCounterFixture(706);
            UnitInstance bmbG = PlaceUnit(engG, bomber, 0, Line.Support);
            PlaceUnit(engG, fighter, 1, Line.Support);         // 场景元素：拦路的敌方战斗机（保护 HQ）

            int logBefore = engG.State.Log.Count;
            bool attackedG = engG.AttackWithUnit(bmbG.InstanceId, -1);   // 轰炸机打敌方 HQ（被支援线战斗机拦）
            string lastLog = engG.State.Log.Count > logBefore
                ? engG.State.Log[engG.State.Log.Count - 1] : "";
            bool c17 = !attackedG && lastLog.Contains("拦截")
                       && !GameEngine.HasNoActionLeft(bmbG)            // 行动没有被消耗
                       && engG.State.Players[1].HqHealth == 20;        // HQ 一点血都没掉

            // ---- 表格 ----
            bool allRange = a1 && a2 && a3 && a4 && a5 && a6 && a7 && a8 && a9;
            bool allIntercept = b10 && b11 && b12 && b13 && b14 && b15 && b16;

            sb.AppendLine("### 10.1 射程分岔（9 条对偶断言）");
            sb.AppendLine();
            sb.AppendLine("| # | 场景（攻击者 → 目标） | 期望 | 实测 | 结果 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine("| 1 | 步兵（支援线）→ 敌方支援线单位 | 拒绝 | " +
                          (a1 ? "拒绝<br>" + a1r : "**通过 —— 差异！**") + " | " + Mark(a1) + " |");
            sb.AppendLine("| 2 | 步兵（支援线）→ 敌方 HQ | 拒绝 | " +
                          (a2 ? "拒绝<br>" + a2r : "**通过 —— 差异！**") + " | " + Mark(a2) + " |");
            sb.AppendLine("| 3 | 步兵（前线）→ 敌方 HQ | 通过 | " +
                          (a3 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a3) + " |");
            sb.AppendLine("| 4 | **火炮**（支援线）→ 敌方支援线单位 | 通过 | " +
                          (a4 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a4) + " |");
            sb.AppendLine("| 5 | **火炮**（支援线）→ 敌方 HQ | 通过 | " +
                          (a5 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a5) + " |");
            sb.AppendLine("| 6 | **战斗机**（支援线）→ 敌方支援线单位 | 通过 | " +
                          (a6 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a6) + " |");
            sb.AppendLine("| 7 | **战斗机**（支援线）→ 敌方 HQ | 通过 | " +
                          (a7 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a7) + " |");
            sb.AppendLine("| 8 | **轰炸机**（支援线）→ 敌方支援线单位（无战斗机） | 通过 | " +
                          (a8 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a8) + " |");
            sb.AppendLine("| 9 | **轰炸机**（支援线）→ 敌方 HQ（无战斗机） | 通过 | " +
                          (a9 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(a9) + " |");
            sb.AppendLine();
            sb.AppendLine("> 第 1~3 条是**对偶对照**：证明\"步兵/坦克保持相邻战线\"这条旧边界没有被越线规则误伤。");
            sb.AppendLine();

            sb.AppendLine("### 10.2 战斗机拦截（7 条）");
            sb.AppendLine();
            sb.AppendLine("| # | 场景 | 期望 | 实测 | 结果 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine("| 10 | 轰炸机 → 敌方前线步兵（该线**有**敌方战斗机） | 拒绝（拦截） | " +
                          (b10 ? "拒绝<br>" + b10r : "**通过 —— 差异！**") + " | " + Mark(b10) + " |");
            sb.AppendLine("| 11 | 轰炸机 → **那架战斗机本身** | 通过 | " +
                          (b11 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(b11) + " |");
            sb.AppendLine("| 12 | 轰炸机 → 敌方 HQ（敌方支援线**有**战斗机） | 拒绝（拦截） | " +
                          (b12 ? "拒绝<br>" + b12r : "**通过 —— 差异！**") + " | " + Mark(b12) + " |");
            sb.AppendLine("| 13 | 轰炸机 → 敌方支援线的**战斗机本身** | 通过 | " +
                          (b13 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(b13) + " |");
            sb.AppendLine("| 14 | 敌方战斗机在**前线**，轰炸机打**敌方支援线**目标 | 通过（跨线不拦） | " +
                          (b14 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(b14) + " |");
            sb.AppendLine("| 15 | 对照：敌方无战斗机时，轰炸机正常打敌方前线目标 | 通过 | " +
                          (b15 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(b15) + " |");
            sb.AppendLine("| 16 | 对照：**火炮**打\"有敌方战斗机的战线\"上的目标 | 通过（拦截只拦轰炸机） | " +
                          (b16 ? "通过" : "**拒绝 —— 差异！**") + " | " + Mark(b16) + " |");
            sb.AppendLine();
            sb.AppendLine("> 第 11 / 13 条是本轮**口径的关键**：\"拦截保护的是该战线上**除战斗机外**的其他目标\"——");
            sb.AppendLine("> 若按\"不能攻击任何单位\"实现，这两条会失败，且「轰炸机打战斗机要挨反击」");
            sb.AppendLine("> （第 9 项第 9 条）会变成永远不可达的死代码。");
            sb.AppendLine();

            sb.AppendLine("### 10.3 端到端：被拦的攻击真的发生不了（1 条）");
            sb.AppendLine();
            sb.AppendLine("| # | 场景 | 期望 | 实测 | 结果 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine("| 17 | `AttackWithUnit`：轰炸机打被拦的敌方 HQ | 拒绝 + 日志含\"拦截\" + 未消耗行动 + HQ 未掉血 | " +
                          (c17 ? "全部满足" : "**有差异！**") + " | " + Mark(c17) + " |");
            sb.AppendLine();
            sb.AppendLine("日志最后一条：" + (lastLog.Length > 0 ? lastLog : "（无）"));
            sb.AppendLine();

            ok = allRange && allIntercept && c17;

            sb.AppendLine("**结论：" + (ok
                ? "越线射程与战斗机拦截全部符合设计文档 §13 —— 火炮/战斗机/轰炸机可打任意战线（含 HQ）；"
                  + "轰炸机不能打有敌方战斗机战线上的其他目标，但战斗机本身可打（拦截只拦轰炸机、只保护同线）。"
                : "越线射程或拦截行为不符预期，详见上表。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 第 11 项 · 坦克「移动 + 攻击」（[阶段2.6 · 任务3] 新增）
        // ==================================================================

        private static string RunTankMoveAndAttack(out bool ok, out string title)
        {
            title = "坦克「移动 + 攻击」：坦克同回合可移动且攻击（各扣一次行动费、顺序不限）；非坦克仍二选一；部署失调不豁免";
            ok = false;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## 第 11 项 · 坦克「移动 + 攻击」（设计文档 §14.5）");
            sb.AppendLine();
            sb.AppendLine("Kards 口径（三来源一致）：坦克可在同一回合**移动并攻击（顺序不限）**、两次行动**各付一次**行动费、");
            sb.AppendLine("**不豁免部署失调**。全部断言手工摆盘 + 期望值硬编码（不拿被测代码算期望值）。");
            sb.AppendLine();

            CardDef armored = CardDb.Get("unit_armored");   // 坦克 4/5，行动费 2
            CardDef scout = CardDb.Get("unit_scout");       // 步兵 1/2，行动费 1

            // ---- 11.1 坦克：先移动、后攻击（各扣一次费）----
            GameEngine eng = NewCounterFixture(800);
            UnitInstance tankA = PlaceUnit(eng, armored, 0, Line.Support);
            PlaceUnit(eng, scout, 1, Line.Support);   // 场景元素：敌方后排有人（不挡前线）

            int cost = GameEngine.ActionCostOf(armored);
            int res0 = eng.State.Players[0].Resources;
            bool moveOk = eng.MoveUnit(tankA.InstanceId);
            int res1 = eng.State.Players[0].Resources;
            string whyA = "";
            bool canAttackAfterMove = eng.CanUnitAttack(tankA, out whyA);   // 关键：旧规则此处 false
            bool atkOk = eng.AttackWithUnit(tankA.InstanceId, -1);          // 移上前线 → 打 HQ
            int res2 = eng.State.Players[0].Resources;
            string whyM = "";
            bool moveAgainBlocked = !eng.CanUnitMove(tankA, out whyM);
            string whyAt = "";
            bool atkAgainBlocked = !eng.CanUnitAttack(tankA, out whyAt);

            bool e1 = moveOk && (res0 - res1 == cost);
            bool e2 = canAttackAfterMove && atkOk && (res1 - res2 == cost);
            bool e3 = moveAgainBlocked && atkAgainBlocked;

            sb.AppendLine("### 11.1 坦克：先移动、后攻击");
            sb.AppendLine();
            sb.AppendLine("- 移动成功：" + moveOk + "；资源 " + res0 + " → " + res1 + "（期望扣 " + cost + "）");
            sb.AppendLine("- **移动后 CanUnitAttack：" + canAttackAfterMove +
                          (whyA.Length > 0 ? "（" + whyA + "）" : "") + "** —— 旧规则此处为 false（本项最关键的对偶翻转）");
            sb.AppendLine("- 攻击成功：" + atkOk + "；资源 " + res1 + " → " + res2 + "（再扣 " + cost + "，总差 " + (res0 - res2) + "）");
            sb.AppendLine("- 再移动被拒：" + moveAgainBlocked + (whyM.Length > 0 ? "（" + whyM + "）" : ""));
            sb.AppendLine("- 再攻击被拒：" + atkAgainBlocked + (whyAt.Length > 0 ? "（" + whyAt + "）" : ""));
            sb.AppendLine();

            // ---- 11.2 顺序不限的另一半：先攻击、后移动（且证明"范围按当下位置算"）----
            // 敌方前线只有一个 2 血侦察兵 ⇒ 坦克 4 攻可击杀 → 清空后前线归"无主" ⇒ 可以移入
            //（这正好演示"先攻清路 → 再移占线"的组合；若先移则会被"敌占前线"拒绝）
            GameEngine engB = NewCounterFixture(801);
            UnitInstance tankB = PlaceUnit(engB, armored, 0, Line.Support);
            UnitInstance foeFrontB = PlaceUnit(engB, scout, 1, Line.Front);

            string whyB1 = "";
            bool hqBeforeMove = engB.CanAttackHq(tankB, out whyB1);    // 支援线：够不到 HQ
            bool atkB = engB.AttackWithUnit(tankB.InstanceId, foeFrontB.InstanceId);   // 先攻：打敌前线（击杀）
            bool moveB = engB.MoveUnit(tankB.InstanceId);               // 后移：前线已空，可移入
            string whyB2 = "";
            bool hqAfterMove = engB.CanAttackHq(tankB, out whyB2);      // 移动后：够得到 HQ

            bool e4 = !hqBeforeMove && atkB && moveB && hqAfterMove;

            sb.AppendLine("### 11.2 坦克：先攻击、后移动（顺序不限的另一半 + 范围按当下位置算）");
            sb.AppendLine();
            sb.AppendLine("- 支援线时 CanAttackHq：" + hqBeforeMove + "（期望 false" + (whyB1.Length > 0 ? "：" + whyB1 : "") + "）");
            sb.AppendLine("- 先攻击（击杀敌方前线侦察兵 → 清路）成功：" + atkB + " → 再移动（占线）成功：" + moveB);
            sb.AppendLine("- 移动后 CanAttackHq：" + hqAfterMove + "（期望 true —— 攻击范围按**当下位置**算）");
            sb.AppendLine();

            // ---- 11.3 对偶：非坦克仍二选一 ----
            GameEngine engC = NewCounterFixture(802);
            UnitInstance infC = PlaceUnit(engC, scout, 0, Line.Support);
            bool moveC = engC.MoveUnit(infC.InstanceId);
            string whyC = "";
            bool infAtkAfterMove = engC.CanUnitAttack(infC, out whyC);
            bool e5 = moveC && !infAtkAfterMove;

            sb.AppendLine("### 11.3 对偶：非坦克（步兵）仍「移动与攻击二选一」");
            sb.AppendLine();
            sb.AppendLine("- 步兵移动成功：" + moveC + " → 移动后 CanUnitAttack：" + infAtkAfterMove +
                          "（期望 false" + (whyC.Length > 0 ? "：" + whyC + "）" : "）"));
            sb.AppendLine();

            // ---- 11.4 部署失调不豁免（坦克也一样）----
            GameEngine engD = NewCounterFixture(803);
            UnitInstance tankD = PlaceUnit(engD, armored, 0, Line.Support);
            tankD.DeployedThisTurn = true;    // 模拟"刚部署"
            string whyD1 = "";
            bool dMoveRejected = !engD.CanUnitMove(tankD, out whyD1);
            string whyD2 = "";
            bool dAtkRejected = !engD.CanUnitAttack(tankD, out whyD2);
            bool e6 = dMoveRejected && dAtkRejected;

            sb.AppendLine("### 11.4 部署失调不豁免（坦克部署当回合仍不能行动）");
            sb.AppendLine();
            sb.AppendLine("- 部署当回合移动被拒：" + dMoveRejected + " / 攻击被拒：" + dAtkRejected +
                          "（期望均 true" + (whyD1.Length > 0 ? "：" + whyD1 : "") + "）");
            sb.AppendLine();

            // ---- 11.5 资源不足：第二个动作被拒、且不扣钱 ----
            GameEngine engE = NewCounterFixture(804);
            UnitInstance tankE = PlaceUnit(engE, armored, 0, Line.Support);
            PlaceUnit(engE, scout, 1, Line.Support);
            engE.MoveUnit(tankE.InstanceId);
            engE.State.Players[0].Resources = cost - 1;
            int resE0 = engE.State.Players[0].Resources;
            string whyE = "";
            bool eAtkRejected = !engE.CanUnitAttack(tankE, out whyE);
            bool eNoSpend = engE.State.Players[0].Resources == resE0;
            bool e7 = eAtkRejected && whyE.Contains("资源") && eNoSpend;

            sb.AppendLine("### 11.5 资源不足：坦克第二个动作被拒、不扣钱");
            sb.AppendLine();
            sb.AppendLine("- 移动后资源 = " + (cost - 1) + "（行动费 " + cost + "）→ CanUnitAttack 被拒：" + eAtkRejected +
                          "（" + whyE + "）；资源保持：" + eNoSpend);
            sb.AppendLine();
            sb.AppendLine("> ⚠️ 「闪击 × 坦克 = 登场即可移+攻」的断言**留桩**：依赖任务 4（闪击字段）落地后补进本项，");
            sb.AppendLine("> 不得在任务 4 前含糊成「已测」。");
            sb.AppendLine();

            ok = e1 && e2 && e3 && e4 && e5 && e6 && e7;

            sb.AppendLine("**结论：" + (ok
                ? "坦克同回合可移动且攻击（两笔各扣一次、顺序不限、范围按当下位置算）；非坦克仍二选一；部署失调不豁免；资源不足时第二动作被拒且不扣钱。"
                : "坦克「移动 + 攻击」行为不符预期，详见上文。") + "**");
            sb.AppendLine();
            return sb.ToString();
        }

        // ==================================================================
        // 工具
        // ==================================================================

        /// <summary>[阶段2.5 · 任务 A] 第 9 / 10 项的夹具：造一局、清空双方手牌与牌库、资源给足。
        /// 本项**不推进回合**，所以清空牌库不会触发疲劳结算，不会干扰断言。</summary>
        private static GameEngine NewCounterFixture(uint seed)
        {
            GameEngine eng = new GameEngine(seed, MatchMode.AiVsAi);
            eng.StartMatch();
            for (int i = 0; i < 2; i++)
            {
                eng.State.Players[i].Hand.Clear();
                eng.State.Players[i].Deck.Clear();
                eng.State.Players[i].Resources = 99;
            }
            return eng;
        }

        /// <summary>[阶段2.5 · 任务 A] 第 9 项的夹具：把一个单位直接摆到指定战线，
        /// 并清掉"部署失调 / 本回合的行动记录"（否则 CanUnitAct 会拦住攻击，断言全部失败）。
        /// 注意 UnitInstance 构造时默认 Line = Support，所以前线必须显式加进 Frontline 列表。</summary>
        private static UnitInstance PlaceUnit(GameEngine eng, CardDef def, int owner, Line line)
        {
            UnitInstance u = new UnitInstance(eng.State.NextUnitInstanceId++, def, owner);
            u.Line = line;
            if (line == Line.Support) eng.State.Players[owner].SupportLine.Add(u);
            else eng.State.Frontline.Add(u);
            u.DeployedThisTurn = false;
            u.HasMovedThisTurn = false;
            u.HasAttackedThisTurn = false;
            return u;
        }

        /// <summary>某类终局占 200 局的百分比（只用于第 4 项的报告）。</summary>
        private static string Pct(int n) { return (n * 100.0 / BiasGameCount).ToString("F1") + "%"; }

        /// <summary>跑完一局 GreedyAI vs GreedyAI，返回引擎（对局一定被推进到结束或触发保护上限）。</summary>
        private static GameEngine RunFullGame(uint seed)
        {
            GameEngine eng = new GameEngine(seed, MatchMode.AiVsAi);
            eng.StartMatch();
            GreedyAI ai = new GreedyAI(eng);

            int guard = 0;
            while (!eng.State.IsOver && guard++ < SafetyLoopGuard)
            {
                ai.TakeTurn();
            }
            return eng;
        }

        private static string Mark(bool ok) { return ok ? "✅" : "❌"; }

        private static void WriteReportFile(string report)
        {
            try
            {
                // Application.dataPath 形如 <project>/Assets，报告写到 <project>/Docs 下
                string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string fullPath = System.IO.Path.Combine(projectRoot, ReportRelativePath);
                string dir = System.IO.Path.GetDirectoryName(fullPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

                // 与 .cs 一致，写 UTF-8 with BOM，避免中文在 Windows 上被按 GBK 解析
                System.IO.File.WriteAllText(fullPath, report, new UTF8Encoding(true));
                Debug.Log("[KardsLike 自检] 报告已写入：" + fullPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KardsLike 自检] 写报告文件失败：" + e.Message);
            }
        }
    }
}
#endif
