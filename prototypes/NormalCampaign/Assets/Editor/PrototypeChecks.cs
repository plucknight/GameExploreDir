// PROTOTYPE - NOT FOR PRODUCTION
// 自动检查状态机边界与确定性；不替代玩家试玩或 56 小时节奏验收。
using System;
using System.Collections.Generic;
using System.Linq;

namespace NormalCampaignPrototype
{
    public static class PrototypeChecks
    {
        private static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("原型检查失败：" + label);
        }

        public static string[] Run(CampaignData data, HeroCatalog catalog)
        {
            List<string> passed = new List<string>();
            Require(data.regions.Length == 6 && data.stages.Length == 54, "6 区 54 关");
            Require(data.regions.Sum(r => r.budget_hours) == 56, "56 小时仅为预算合计");
            Dictionary<string, MonsterSpec> monsters = data.monsters.ToDictionary(m => m.id);
            for (int i = 0; i < data.stages.Length; i++)
            {
                StageSpec s = data.stages[i];
                Require(s.id == (i / 9 + 1) + "-" + (i % 9 + 1), "顺序关卡 ID");
                Require(i == 0 ? String.IsNullOrEmpty(s.predecessor) : s.predecessor == data.stages[i - 1].id, "前置连续且无环");
                Require(s.market_eligible == (s.index == 5 || s.index == 7 || s.index == 9), "资格仅 5/7/9");
                Require(s.encounters.Length == 3, "每关三场");
                foreach (EncounterSpec e in s.encounters)
                {
                    Require(e.enemies.Length >= 1 && e.enemies.Length <= 3, "初始敌人数 1..3");
                    Require(e.enemies.All(monsters.ContainsKey), "怪物引用存在");
                    int requests = 0;
                    foreach (string id in e.enemies)
                    {
                        MonsterSpec m = monsters[id];
                        Require(m.region == s.region, "怪物属于当前区");
                        if (m.summons == null) continue;
                        foreach (SummonSpec summon in m.summons)
                        {
                            Require(summon.count > 0 && summon.count <= 2 && monsters.ContainsKey(summon.monster_id), "有限召唤引用");
                            MonsterSpec child = monsters[summon.monster_id];
                            Require(child.region == m.region && child.kind == "normal" && (child.summons == null || child.summons.Length == 0), "非递归召唤");
                            requests += summon.count;
                        }
                    }
                    Require(requests <= 2, "整场召唤总额度最多二个，满员延迟");
                }
            }
            passed.Add("PASS 数据：54 关前置、3 场、3 活敌上限、非递归召唤及市场产地");

            GameSession start = new GameSession(data, catalog);
            Require(start.Heroes.Count == 2 && start.Teams[0].Members.Count == 2, "初始双人一队");
            Require(start.TeamUnlocked(0) && !start.TeamUnlocked(1) && !start.TeamUnlocked(2) && !start.TeamUnlocked(3), "初始队伍门槛");
            Require(!start.Start(0, "1-2", false), "禁止跳过前置");
            Require(!start.Assign(0, 1, start.Heroes[0].Spec.id), "同队不可重复角色");
            Require(!start.CycleCharm("C01", 1), "第二纹章门槛");
            Require(start.Heroes.All(h => h.Charm == -1 && h.Charm2 == -1) && !start.CycleCharm("C01", 0), "开局无纹章且不能提前装备");
            Require(!start.ChangeSkill("C01", 0), "2-7 之前不能选择第三技能");
            Require(!start.Recruit("C03") && start.Materials == 0, "招募前置与余额");
            start.Materials = 42; start.Cleared.Add("1-2");
            Require(start.Recruit("C03") && start.Materials == 0, "招募真实扣料");
            Require(!start.Recruit("C03"), "不得重复招募");
            start.Cleared.Add("1-3");
            Require(start.CycleCharm("C01", 0) && start.Heroes[0].Charm == 0, "1-3 解锁创伤");
            Require(!start.CycleCharm("C01", 0) && start.Heroes[0].Charm == 0, "未到配方节点不得切其他纹章");
            start.Cleared.Add("1-4");
            Require(start.CycleCharm("C01", 0) && start.Heroes[0].Charm == 2, "1-4 开放守护并跳过未开放净化");
            passed.Add("PASS 新开局：角色、队伍、招募扣料和技能/纹章门槛");

            GameSession move = new GameSession(data, catalog);
            move.LoadLab("3-9");
            Require(!move.Assign(1, 0, "C01") && move.Teams[0].Members.Count == 2, "不能抢占另一支停队的角色");
            Require(move.Unassign(0, 0) && move.Assign(1, 0, "C01"), "明确卸下后才能转队");
            Require(move.Teams[0].Members.Count == 1 && !move.Start(0, "1-1", false), "空缺队伍不能出发");

            GameSession lab = new GameSession(data, catalog);
            lab.LoadLab("3-9");
            Require(lab.Heroes.Count == 10 && !lab.Cleared.Contains("3-9") && lab.StageUnlocked("3-9"), "实验档独立且目标未通过");
            Require(!lab.CycleCharm("C01", 1) && lab.ChangeSkill("C01", 0), "3-9 实验保留单槽且第三技能已解锁");
            Require(lab.Teams.All(t => lab.TeamUnlocked(t.Index)), "三队已解锁");
            Require(lab.Start(0, "1-5", false), "第一队出发");
            Require(!lab.Start(1, "1-5", false), "禁止两队占同关");
            Require(lab.Start(1, "1-7", false) && lab.Start(2, "1-9", false), "三队独立出发");
            Require(!lab.Upgrade("C01") && !lab.CycleCondition("C01", 0) && !lab.Assign(1, 0, "C01"), "运行中不可修改配置");
            lab.Advance(1);
            Require(lab.Teams.All(t => t.RoomSeconds > 0), "三队都推进");
            Require(Math.Abs(lab.OnlineSeconds - 1) < 1e-7, "账号时间不会按队伍翻倍");
            Require(!lab.SetRareTeam(1), "途中禁止更换稀有目标");
            lab.Stop(0);
            Require(!lab.Start(0, "1-5", false) && !lab.Unassign(0, 0) && !lab.Assign(1, 0, "C01"), "主动撤退后不能重派/换队跳过角色恢复");
            Require(lab.SetRareTeam(1), "主动放弃后可切目标");
            Require(lab.QualifiedSeconds.Sum() == 0, "失败/半趟时间尚未提交");
            passed.Add("PASS 三队独立、账号时间、关卡占用、停队改装、资格目标切换");

            // 以下控制血量/场次来注入失败，是状态转移测试，不是假称自然战斗胜率。
            GameSession fail = new GameSession(data, catalog);
            Require(fail.Start(0, "1-1", false), "失败奖励检查开始");
            DefeatEnemies(fail.Teams[0]); fail.Advance(.1);
            double earned = fail.Materials;
            Require(earned > 0 && !fail.Cleared.Contains("1-1"), "普通房间立即结算而非直接通关");
            fail.Advance(4.1);
            foreach (UnitState u in fail.Teams[0].Allies) u.Hp = 0;
            fail.Advance(.1);
            Require(fail.Materials == earned && fail.Teams[0].ConsecutiveFailures == 1 && fail.Teams[0].RestSeconds > 29,
                "败退保留先前材料且等 30 秒");
            Require(fail.QualifiedSeconds.Sum() == 0 && fail.Teams[0].RunSeconds == 0, "失败整趟资格清空");
            fail.Advance(30.1);
            Require(fail.Teams[0].Allies.All(u => u.Hp == u.MaxHp && u.Channel == 0), "重新开始满血清状态");
            for (int i = 0; i < 2; i++)
            {
                foreach (UnitState u in fail.Teams[0].Allies) u.Hp = 0;
                fail.Advance(.1); fail.Advance(30.1);
            }
            Require(!fail.Teams[0].Running, "三败无安全关自动停队");
            passed.Add("PASS 故障注入：逐场奖励保留、败退计时、三败停队、满血清状态");

            GameSession safe = new GameSession(data, catalog);
            safe.Start(0, "1-1", false); CompleteRun(safe, 0);
            safe.Stop(0);
            Require(!safe.Start(0, "1-2", false), "停止不能跳过胜后 4 秒休整");
            safe.Advance(4.1); Require(safe.Start(0, "1-2", false), "通过前置并休整后可进下一关");
            for (int i = 0; i < 3; i++)
            {
                foreach (UnitState u in safe.Teams[0].Allies) u.Hp = 0;
                safe.Advance(.1); safe.Advance(30.1);
            }
            Require(safe.Teams[0].Running && safe.Teams[0].StageId == "1-1" && !safe.Teams[0].AdvanceMode,
                "三败回到此配置验证过的安全关");
            safe.Stop(0); safe.Advance(30.1); safe.Materials = 999;
            Require(safe.Upgrade("C01"), "强化改变配置");
            safe.Start(0, "1-2", false);
            for (int i = 0; i < 3; i++)
            {
                foreach (UnitState u in safe.Teams[0].Allies) u.Hp = 0;
                safe.Advance(.1); safe.Advance(30.1);
            }
            Require(!safe.Teams[0].Running, "改装清除旧安全记录");
            Require(safe.Teams[0].LastFailure.Contains("1-2") && safe.Summary().Any(line => line.Contains("最近失败")), "重试后仍能回访最近失败来源");
            passed.Add("PASS 三败安全回退和配置变更后的记录失效");

            GameSession formerlySafe = new GameSession(data, catalog);
            formerlySafe.Start(0, "1-1", false); CompleteRun(formerlySafe, 0);
            formerlySafe.Advance(4.1);
            for (int i = 0; i < 3; i++)
            {
                foreach (UnitState u in formerlySafe.Teams[0].Allies) u.Hp = 0;
                formerlySafe.Advance(.1); formerlySafe.Advance(30.1);
            }
            Require(!formerlySafe.Teams[0].Running, "曾胜当前关随后三败，不得把当前关当安全回退无限重试");
            passed.Add("PASS 主动停止/换队不能跳过胜败休整，当前失败关不能作为安全回退");

            GameSession rare = new GameSession(data, catalog);
            rare.LoadLab("3-9"); rare.Start(0, "1-5", false);
            rare.Start(1, "1-7", false); rare.Start(2, "1-9", false);
            foreach (TeamState t in rare.Teams)
            {
                t.EncounterIndex = 2; t.RunSeconds = 899.9;
                DefeatEnemies(t);
            }
            rare.Advance(.1);
            Require(rare.RareChecks == 1 && rare.QualifiedSeconds.Sum() < 1e-6, "同刻三队通关只有目标队提交一次资格");
            Require(rare.Teams.All(t => t.Wins == 1), "三队普通通关仍各自生效");
            Require(rare.SetRareTeam(1), "完成整趟边界允许换队");
            for (int n = 0; n < 20; n++)
            {
                rare.Stop(1); rare.Advance(4.1); Require(rare.Start(1, "1-7", false), "稀有检查等待胜后休整");
                rare.Teams[1].EncounterIndex = 2; rare.Teams[1].RunSeconds = 899.9;
                DefeatEnemies(rare.Teams[1]); rare.Advance(.1);
            }
            Require(rare.RareDrops >= 1 && rare.RareChecks == 21, "20 次硬保底至少一次本地计数");
            passed.Add("PASS 故障注入：整趟资格提交、三队不复制、20 次硬保底；未发行市场物品");

            GameSession summonSession = new GameSession(data, catalog);
            summonSession.LoadLab("3-9"); summonSession.Start(0, "3-9", false);
            bool observedSummon = false;
            for (int i = 0; i < 2400; i++)
            {
                summonSession.Advance(.1);
                Require(summonSession.Teams[0].Enemies.Count(e => e.Hp > 0) <= 3, "运行时活敌不超过三");
                if (summonSession.Teams[0].Log.Any(line => line.Contains("本场额度不恢复"))) observedSummon = true;
                foreach (UnitState u in summonSession.Teams[0].Allies)
                    Require(u.Shield <= u.MaxHp * .35f + .01f && u.Hp <= u.MaxHp + .01f, "护盾与治疗不溢出");
            }
            // 是否出现由战斗长度和击杀顺序决定；不把没有触发的分支记作通过。
            passed.Add(observedSummon ? "PASS 3-9 自然战斗观察到有限召唤且活敌始终≤3" :
                "NOT_OBSERVED 3-9 自然战斗未触发召唤；三活敌上限已检查，召唤需定向测试");

            GameSession one = new GameSession(data, catalog, 12345u);
            GameSession chunks = new GameSession(data, catalog, 12345u);
            one.Start(0, "1-1", true); chunks.Start(0, "1-1", true);
            one.Advance(600);
            for (int i = 0; i < 1000; i++) { chunks.Advance(.037); chunks.Advance(.563); }
            Require(one.Materials == chunks.Materials && one.Level == chunks.Level && one.Cleared.SetEquals(chunks.Cleared), "同种子不同调用粒度状态一致");
            Require(one.Teams[0].StageId == chunks.Teams[0].StageId && one.Teams[0].Log.SequenceEqual(chunks.Teams[0].Log), "同种子事件序列一致");
            Require(Math.Abs(one.OnlineSeconds - chunks.OnlineSeconds) < 1e-7, "固定步长时间一致");
            Require(one.Cleared.Contains("1-1"), "真实自动战斗至少完成首关");
            passed.Add("PASS 600 秒实际自动战斗与同种子分块推进一致；已通 " + one.Cleared.Count + " 关（不是节奏验收）");
            passed.Add("NOT_VALIDATED 普通 56 小时、玩家体验、长期平衡、存档和真实 Steam 服务");
            return passed.ToArray();
        }

        private static void DefeatEnemies(TeamState team)
        {
            foreach (UnitState enemy in team.Enemies) enemy.Hp = 0;
        }
        private static void CompleteRun(GameSession session, int team)
        {
            int before = session.Teams[team].Wins;
            for (int i = 0; i < 3; i++)
            {
                DefeatEnemies(session.Teams[team]); session.Advance(.1);
                if (i < 2) session.Advance(4.1);
            }
            Require(session.Teams[team].Wins == before + 1, "控制击败三场形成完整通关");
        }
    }
}
