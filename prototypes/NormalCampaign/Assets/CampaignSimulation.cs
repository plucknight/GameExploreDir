// PROTOTYPE - NOT FOR PRODUCTION
// 仅验证双人队伍配置、威胁反馈与探索状态机；这里的产率不能验证普通 56 小时目标。
using System;
using System.Collections.Generic;
using System.Linq;

namespace NormalCampaignPrototype
{
    public sealed class HeroState
    {
        public HeroSpec Spec;
        public int[] Active = { 0, 1 };
        public string[] Conditions = { "默认", "默认" };
        public int Weapon = 8, Armor, Charm = -1, Charm2 = -1;
        internal double ReadyAt;
    }

    public sealed class TeamState
    {
        public int Index;
        public List<HeroState> Members = new List<HeroState>();
        public string StageId = "1-1";
        public bool Running, Expanded = true, AdvanceMode;
        public int EncounterIndex, ConsecutiveFailures, Wins;
        public List<UnitState> Allies = new List<UnitState>();
        public List<UnitState> Enemies = new List<UnitState>();
        public List<string> Log = new List<string>();
        public string LastReason = "尚未出发";
        public string LastFailure = "尚无失败";
        public float RoomSeconds, RestSeconds;
        public double RunSeconds;
        internal int NextAction; // 1 下一场，2 下一趟，3 败退结束
        internal string Fallback;
        internal HashSet<string> SafeStages = new HashSet<string>();
        internal int SummonQuota;
        internal bool RareEligibleRun;
        internal string LastDamageSource = "未知来源";
        internal double ResumeAt;
    }

    public sealed class UnitState
    {
        public string Name;
        public float Hp, MaxHp, Shield;
        public bool IsEnemy;
        public float Channel;
        public string Status = "";
        internal HeroState Hero;
        internal MonsterSpec Monster;
        internal float Attack, BasicTimer, AbilityTimer, PoisonTime, PoisonPower;
        internal float SlowTime, MarkTime, StunTime, CounterTime, CharmTimer;
        internal float[] SkillTimers = new float[2];
        internal float Stagger;
        internal string DotSource = "持续伤害";
        internal bool SummonsRequested;
        internal Queue<string> PendingSummons = new Queue<string>();
        internal Queue<RecoveryEntry> Recovery = new Queue<RecoveryEntry>();
    }

    internal struct RecoveryEntry
    {
        internal double Time;
        internal float Amount;
    }

    public sealed class GameSession
    {
        public CampaignData Data { get; private set; }
        public HeroCatalog Catalog { get; private set; }
        public List<HeroState> Heroes = new List<HeroState>();
        public TeamState[] Teams = new TeamState[3];
        public HashSet<string> Cleared = new HashSet<string>();
        public double Materials, OnlineSeconds;
        public int Level = 1, RareTeam, RareChecks, RareDrops;
        public double[] QualifiedSeconds = new double[6];
        public bool IsLab;
        // 仅供实验打开 DLC 招募资格；不代表真实购买验证。
        public bool DlcOwnedForExperiment;
        public string Notice = "会话原型：退出不保存；数值与产率仅供机制实验。";
        public static readonly string[] CharmNames = { "创伤", "净化", "守护", "猎印", "蓄能", "余烬" };
        public static readonly string[] CharmUnlockStages = { "1-3", "2-2", "1-4", "2-4", "1-8", "2-6" };
        public static readonly string[] ConditionNames = { "默认", "友方低血", "敌方蓄力", "友方有负面" };
        private readonly Dictionary<string, StageSpec> stages;
        private readonly Dictionary<string, MonsterSpec> monsters;
        private readonly Dictionary<string, int> order;
        private double accumulator, clock;
        private uint randomState;
        private int rareMisses;
        private const double Step = 0.1;

        public GameSession(CampaignData data, HeroCatalog catalog) : this(data, catalog, 20261023u) { }
        public GameSession(CampaignData data, HeroCatalog catalog, uint seed)
        {
            if (data == null || catalog == null || data.stages == null || data.monsters == null || catalog.characters == null)
                throw new ArgumentException("原型数据未准备完成");
            Data = data;
            Catalog = catalog;
            stages = data.stages.ToDictionary(s => s.id);
            monsters = data.monsters.ToDictionary(m => m.id);
            order = data.stages.Select((s, i) => new { s.id, i }).ToDictionary(x => x.id, x => x.i);
            randomState = seed == 0 ? 1u : seed;
            for (int i = 0; i < 3; i++) Teams[i] = new TeamState { Index = i };
            foreach (HeroSpec h in catalog.characters.Where(h => h.initial)) Heroes.Add(new HeroState { Spec = h });
            if (Heroes.Count != 2) throw new ArgumentException("必须恰有两名初始角色");
            Teams[0].Members.AddRange(Heroes);
        }

        public StageSpec Stage(string id) { return id != null && stages.ContainsKey(id) ? stages[id] : null; }
        public bool StageUnlocked(string id)
        {
            StageSpec s = Stage(id);
            return s != null && (String.IsNullOrEmpty(s.predecessor) || Cleared.Contains(s.predecessor));
        }
        public bool TeamUnlocked(int team)
        {
            return team == 0 || team == 1 && Heroes.Count >= 4 && Cleared.Contains("1-5") ||
                team == 2 && Heroes.Count >= 6 && Cleared.Contains("1-9");
        }
        private bool Fail(string reason) { Notice = reason; return false; }
        private bool ValidTeam(int index) { return index >= 0 && index < Teams.Length; }
        private bool Occupied(string id, int except)
        {
            return Teams.Any(t => t.Index != except && t.Running && t.StageId == id);
        }
        private HeroState Hero(string id) { return Heroes.FirstOrDefault(h => h.Spec.id == id); }
        private bool Editable(HeroState hero)
        {
            return hero != null && hero.ReadyAt <= clock + 1e-8 && !Teams.Any(t => t.Running && t.Members.Contains(hero));
        }
        private double ReadyTime(TeamState t)
        {
            return Math.Max(t.ResumeAt, t.Members.Count == 0 ? 0 : t.Members.Max(h => h.ReadyAt));
        }
        private void RefreshRest(TeamState t) { t.RestSeconds = (float)Math.Max(0, ReadyTime(t) - clock); }
        private void ScheduleRest(TeamState t, double seconds, int nextAction)
        {
            t.ResumeAt = clock + seconds;
            foreach (HeroState h in t.Members) h.ReadyAt = Math.Max(h.ReadyAt, t.ResumeAt);
            t.NextAction = nextAction; RefreshRest(t);
        }
        private void InvalidateSafety(HeroState hero)
        {
            foreach (TeamState t in Teams.Where(t => t.Members.Contains(hero))) t.SafeStages.Clear();
        }

        public double RecruitmentCost(HeroSpec spec)
        {
            if (spec.kind == "dlc") return 63;
            int bought = Heroes.Count(h => h.Spec.kind == "base" && !h.Spec.initial);
            return 42 + bought * 21;
        }
        public bool Recruit(string id)
        {
            HeroSpec spec = Catalog.characters.FirstOrDefault(h => h.id == id);
            if (spec == null || Hero(id) != null) return Fail("角色不存在或已招募");
            if (spec.kind == "dlc" && !DlcOwnedForExperiment) return Fail("此会话未启用 DLC 购买资格实验；未接通真实购买");
            if (!String.IsNullOrEmpty(spec.unlock_stage) && !Cleared.Contains(spec.unlock_stage)) return Fail("须先通关 " + spec.unlock_stage);
            double cost = RecruitmentCost(spec);
            if (Materials < cost) return Fail("招募材料不足，需要 " + cost);
            Materials -= cost;
            Heroes.Add(new HeroState { Spec = spec });
            Notice = "已招募 " + spec.name + "；队伍仍须满足人数和关卡门槛";
            return true;
        }
        public bool Assign(int team, int slot, string heroId)
        {
            if (!ValidTeam(team) || slot < 0 || slot > 1 || !TeamUnlocked(team)) return Fail("队伍或槽位未解锁");
            HeroState hero = Hero(heroId);
            TeamState target = Teams[team];
            if (target.Running || ReadyTime(target) > clock + 1e-8 || !Editable(hero)) return Fail("先停止相关队伍并等待休整完成再换人");
            if (slot > target.Members.Count) return Fail("请先填入前一个槽位");
            int oldSlot = target.Members.IndexOf(hero);
            if (oldSlot == slot) return true;
            if (oldSlot >= 0) return Fail("同队不能重复同一角色");
            if (Teams.Any(source => source.Index != team && source.Members.Contains(hero)))
                return Fail("该角色仍在另一队；请先停队并明确卸下成员");
            if (slot == target.Members.Count) target.Members.Add(hero);
            else target.Members[slot] = hero;
            target.SafeStages.Clear();
            target.Allies.Clear(); target.Enemies.Clear();
            Notice = "已调整队伍；该队安全关记录已清除";
            return true;
        }
        public bool Unassign(int team, int slot)
        {
            if (!ValidTeam(team) || slot < 0 || slot >= Teams[team].Members.Count) return Fail("无有效成员可卸下");
            TeamState t = Teams[team];
            if (t.Running || ReadyTime(t) > clock + 1e-8) return Fail("先停止队伍并等待休整完成再卸下成员");
            t.Members.RemoveAt(slot); t.SafeStages.Clear(); t.Allies.Clear(); t.Enemies.Clear();
            Notice = "已卸下成员；需要重新填满两人才能出发";
            return true;
        }
        public bool Start(int team, string stage, bool advance)
        {
            if (!ValidTeam(team) || !TeamUnlocked(team)) return Fail("队伍未解锁");
            TeamState t = Teams[team];
            if (t.Running) return Fail("队伍正在探索，请先停止");
            if (ReadyTime(t) > clock + 1e-8) return Fail("队伍或角色仍在休整，不能用重派或换队跳过等待");
            if (t.Members.Count != 2 || t.Members.Distinct().Count() != 2) return Fail("每队需要两名不同角色");
            if (!StageUnlocked(stage)) return Fail("关卡前置未通过");
            if (Occupied(stage, team)) return Fail("该关卡已被另一队占用");
            t.StageId = stage; t.AdvanceMode = advance; t.Running = true;
            t.RestSeconds = 0; t.NextAction = 0; t.Fallback = null;
            BeginRun(t);
            Notice = "队伍 " + (team + 1) + " 已出发";
            return true;
        }
        public void Stop(int team)
        {
            if (!ValidTeam(team)) return;
            TeamState t = Teams[team];
            if (t.Running && t.NextAction == 0) Defeat(t, "主动撤退");
            t.Running = false; t.RunSeconds = 0; t.NextAction = 0; RefreshRest(t);
            t.LastReason = "停止：已结算普通材料保留，本趟资格作废；角色休整剩余 " + t.RestSeconds.ToString("F1") + " 秒";
            AddLog(t, t.LastReason);
        }
        public bool SetRareTeam(int team)
        {
            if (!ValidTeam(team) || !TeamUnlocked(team)) return Fail("资格目标队未解锁");
            if (team == RareTeam) return true;
            TeamState old = Teams[RareTeam];
            if (old.Running && old.RunSeconds > 0) return Fail("旧目标队已开始本趟；须先停止并放弃本趟资格，或在整趟结算边界切换");
            RareTeam = team;
            Notice = "资格目标已切至队伍 " + (team + 1) + "；只有之后完整通关的合格趟次累计";
            // 若新目标已开始一趟，也不得截取后半趟作为资格。
            Teams[team].RunSeconds = 0;
            Teams[team].RareEligibleRun = !Teams[team].Running || Teams[team].EncounterIndex == 0 && Teams[team].RoomSeconds == 0;
            return true;
        }
        public bool Upgrade(string heroId)
        {
            HeroState h = Hero(heroId);
            if (!Editable(h)) return Fail("先停队再强化");
            int region = FurthestRegion();
            int cap = Data.regions.First(r => r.id == region).end_weapon;
            if (h.Weapon >= cap) return Fail("当前来源地区武器上限为 " + cap);
            double cost = 6 + (h.Weapon - 8) * 2;
            if (Materials < cost) return Fail("材料不足，需要 " + cost);
            Materials -= cost; h.Weapon = Math.Min(cap, h.Weapon + 2);
            InvalidateSafety(h);
            Notice = "强化完成；仅为原型成本，不代表正式养成节奏";
            return true;
        }
        public bool ChangeSkill(string heroId, int slot)
        {
            HeroState h = Hero(heroId);
            if (!Editable(h) || slot < 0 || slot > 1) return Fail("先停队再调整有效技能槽");
            int count = Cleared.Contains("2-7") ? Math.Min(3, h.Spec.skills.Length) : Math.Min(2, h.Spec.skills.Length);
            for (int j = 1; j <= count; j++)
            {
                int next = (h.Active[slot] + j) % count;
                if (next == h.Active[1 - slot]) continue;
                if (next == h.Active[slot]) return Fail("2-7 后解锁第三主动；当前两项已全部装备");
                h.Active[slot] = next; InvalidateSafety(h); return true;
            }
            return false;
        }
        public bool CycleCondition(string heroId, int slot)
        {
            HeroState h = Hero(heroId);
            if (!Editable(h) || slot < 0 || slot > 1) return Fail("先停队再调整释放条件");
            int i = Array.IndexOf(ConditionNames, h.Conditions[slot]);
            h.Conditions[slot] = ConditionNames[(i + 1) % ConditionNames.Length];
            InvalidateSafety(h); return true;
        }
        public bool CycleCharm(string heroId, int slot)
        {
            HeroState h = Hero(heroId);
            if (!Editable(h) || slot < 0 || slot > 1) return Fail("先停队再调整纹章");
            if (slot == 1 && !Cleared.Contains("3-9")) return Fail("通关 3-9 后开启第二纹章槽");
            int other = slot == 0 ? h.Charm2 : h.Charm;
            int current = slot == 0 ? h.Charm : h.Charm2;
            for (int j = 1; j <= 6; j++)
            {
                int next = (current + j) % 6;
                if (next == other || next == current || !Cleared.Contains(CharmUnlockStages[next])) continue;
                if (slot == 0) h.Charm = next; else h.Charm2 = next;
                InvalidateSafety(h); Notice = "已装备基础纹章 " + CharmNames[next] + "；演化尚未实装"; return true;
            }
            return Fail("没有其他已解锁纹章；基础配方随关卡推进开放");
        }
        public void LoadLab(string target)
        {
            StageSpec s = Stage(target);
            if (s == null) { Fail("实验关卡不存在"); return; }
            for (int i = 0; i < 3; i++) Teams[i] = new TeamState { Index = i };
            Heroes.Clear(); Cleared.Clear();
            foreach (StageSpec prior in Data.stages.Take(order[target])) Cleared.Add(prior.id);
            RegionSpec anchor = Data.regions.First(r => r.id == s.region);
            foreach (HeroSpec h in Catalog.characters.Where(h => h.kind == "base"))
                Heroes.Add(new HeroState { Spec = h, Weapon = anchor.end_weapon });
            for (int i = 0; i < 3; i++) Teams[i].Members.AddRange(Heroes.Skip(i * 2).Take(2));
            Teams[0].StageId = target; Teams[1].StageId = "1-5"; Teams[2].StageId = "1-7";
            Level = anchor.end_level; Materials = 500; OnlineSeconds = 0;
            RareTeam = RareChecks = RareDrops = rareMisses = 0;
            QualifiedSeconds = new double[6]; accumulator = clock = 0; randomState = 20261023u;
            IsLab = true;
            Notice = "独立实验档 " + target + "：跳过前置并使用区域末锚点；退出不保存，不证明正式通关时长";
        }
        public string[] Summary()
        {
            List<string> lines = new List<string>();
            lines.Add("账号探索 " + (OnlineSeconds / 60).ToString("F1") + " 分钟；材料 " + Materials.ToString("F0") +
                "；已通 " + Cleared.Count + "/54；共享等级 " + Level + "；已招募 " + Heroes.Count + " 人。");
            foreach (TeamState t in Teams)
                lines.Add("队 " + (t.Index + 1) + "：" + (TeamUnlocked(t.Index) ? t.Running ? "探索 " : "停驻 " : "未解锁 ") +
                    t.StageId + "，成功 " + t.Wins + " 趟，连败 " + t.ConsecutiveFailures + "。最近失败：" + t.LastFailure);
            lines.Add("本地稀有目标队 " + (RareTeam + 1) + "：资格累计 " + (RareChecks * 15 + QualifiedSeconds.Sum() / 60).ToString("F1") +
                " 分钟，判定 " + RareChecks + " 次，模拟掉落 " + RareDrops + " 次；当前保底 " + rareMisses + "/20。");
            lines.Add("会话原型不保存；无真实 Steam 资产，纹章演化/独立身份被动未实装；56 小时与长期平衡 NOT_VALIDATED。");
            return lines.ToArray();
        }

        public void Advance(double seconds)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException("seconds");
            accumulator += seconds;
            while (accumulator + 1e-9 >= Step)
            {
                accumulator -= Step;
                if (accumulator < 0 && accumulator > -1e-8) accumulator = 0;
                clock += Step;
                if (Teams.Any(t => t.Running)) OnlineSeconds += Step;
                foreach (TeamState t in Teams)
                {
                    if (t.Running) TickTeam(t, (float)Step);
                    else RefreshRest(t);
                }
            }
        }
        private int FurthestRegion()
        {
            int region = 1;
            foreach (StageSpec s in Data.stages) if (StageUnlocked(s.id)) region = Math.Max(region, s.region);
            return region;
        }
        private void BeginRun(TeamState t)
        {
            t.EncounterIndex = 0; t.RunSeconds = 0; t.RareEligibleRun = t.Index == RareTeam;
            BeginEncounter(t);
        }
        private void BeginEncounter(TeamState t)
        {
            t.Allies.Clear(); t.Enemies.Clear(); t.RoomSeconds = 0; t.SummonQuota = 0;
            for (int i = 0; i < t.Members.Count; i++)
            {
                HeroState h = t.Members[i];
                float hp = 240 + Level * 14 + h.Armor * 12;
                UnitState u = new UnitState { Name = h.Spec.name, Hero = h, Hp = hp, MaxHp = hp,
                    Attack = 20 + 2 * (Level - 1) + h.Weapon, BasicTimer = 0.4f + i * 0.2f, CharmTimer = 5 };
                u.SkillTimers[0] = 1 + i * 0.2f; u.SkillTimers[1] = 2 + i * 0.2f;
                t.Allies.Add(u);
                if (HasCharm(h, 2)) Recover(t, u, u.MaxHp * .15f, true, "守护纹章");
            }
            foreach (string id in Stage(t.StageId).encounters[t.EncounterIndex].enemies)
            {
                if (t.Enemies.Count >= 3) throw new InvalidOperationException("初始敌人超过三人");
                t.Enemies.Add(CreateEnemy(monsters[id], Stage(t.StageId)));
            }
            t.LastReason = "探索 " + t.StageId + " · 第 " + (t.EncounterIndex + 1) + " 场";
            AddLog(t, t.LastReason + "；满血并清除上场状态");
        }
        private UnitState CreateEnemy(MonsterSpec m, StageSpec s)
        {
            float hp = 80 + s.region * 32 + s.index * 7;
            float attack = 9 + s.region * 4 + s.index * 1.1f;
            if (m.kind == "elite") { hp *= 2.2f; attack *= 1.2f; }
            if (m.kind == "boss") { hp *= 4.2f; attack *= 1.35f; }
            UnitState u = new UnitState { Name = m.name, Monster = m, IsEnemy = true,
                Hp = hp, MaxHp = hp, Attack = attack, BasicTimer = 1.5f, AbilityTimer = 4 };
            if (Contains(m.mechanic, "盾", "护甲", "屏障"))
            {
                u.Shield = hp * .2f;
                u.Recovery.Enqueue(new RecoveryEntry { Time = clock, Amount = u.Shield });
            }
            return u;
        }
        private static bool Contains(string text, params string[] words)
        {
            return text != null && words.Any(text.Contains);
        }
        private static bool HasCharm(HeroState h, int charm) { return h.Charm == charm || h.Charm2 == charm; }
        private static bool Alive(UnitState u) { return u.Hp > 0; }
        private void TickTeam(TeamState t, float dt)
        {
            if (t.NextAction != 0)
            {
                if (t.NextAction == 1) t.RunSeconds += dt;
                RefreshRest(t);
                if (t.RestSeconds > .001f) return;
                t.RestSeconds = 0;
                if (t.NextAction == 1) { t.EncounterIndex++; BeginEncounter(t); }
                else if (t.NextAction == 2)
                {
                    if (t.AdvanceMode)
                    {
                        int index = order[t.StageId] + 1;
                        if (index < Data.stages.Length)
                        {
                            string next = Data.stages[index].id;
                            if (StageUnlocked(next) && !Occupied(next, t.Index)) t.StageId = next;
                            else AddLog(t, "下一关被占用；继续当前关");
                        }
                    }
                    BeginRun(t);
                }
                else if (t.NextAction == 3)
                {
                    if (t.ConsecutiveFailures >= 3)
                    {
                        if (t.Fallback == null || Occupied(t.Fallback, t.Index)) { Stop(t.Index); t.LastReason = "三败后没有当前配置已验证且空闲的安全关，已停队"; AddLog(t, t.LastReason); return; }
                        t.StageId = t.Fallback; t.AdvanceMode = false; t.ConsecutiveFailures = 0;
                        AddLog(t, "三败退回已验证安全关 " + t.StageId);
                    }
                    BeginRun(t);
                }
                t.NextAction = 0;
                return;
            }
            t.RoomSeconds += dt; t.RunSeconds += dt;
            foreach (UnitState u in t.Allies.Concat(t.Enemies).ToArray()) if (Alive(u)) TickStatus(t, u, dt);
            if (!t.Allies.Any(Alive)) { Defeat(t, "队伍倒下"); return; }
            foreach (UnitState u in t.Allies.ToArray()) if (Alive(u)) TickHero(t, u, dt);
            foreach (UnitState u in t.Enemies.ToArray()) if (Alive(u)) TickEnemy(t, u, dt);
            ProcessSummons(t);
            if (!t.Allies.Any(Alive)) { Defeat(t, "队伍倒下"); return; }
            if (!t.Enemies.Any(Alive)) { Victory(t); return; }
            float timeout = Stage(t.StageId).kind == "boss" && t.EncounterIndex == 2 ? 180 : 90;
            if (t.RoomSeconds >= timeout) Defeat(t, "超过 " + timeout + " 秒；仍存活：" + String.Join("、", t.Enemies.Where(Alive).Select(e => e.Name)));
            foreach (UnitState u in t.Allies.Concat(t.Enemies)) UpdateStatus(u);
        }
        private void TickStatus(TeamState t, UnitState u, float dt)
        {
            if (u.PoisonTime > 0)
            {
                u.PoisonTime = Math.Max(0, u.PoisonTime - dt);
                // DoT 是既定实效伤害，不暴击、不吃追加事件、不二次乘算。
                float loss = Math.Min(u.Hp, u.PoisonPower * dt);
                u.Hp -= loss;
                if (!u.IsEnemy && loss > 0) t.LastDamageSource = u.DotSource + "持续伤害";
                if (u.PoisonTime <= 0) u.PoisonPower = 0;
            }
            u.SlowTime = Math.Max(0, u.SlowTime - dt); u.MarkTime = Math.Max(0, u.MarkTime - dt);
            u.StunTime = Math.Max(0, u.StunTime - dt); u.CounterTime = Math.Max(0, u.CounterTime - dt);
            while (u.Recovery.Count > 0 && clock - u.Recovery.Peek().Time >= 20) u.Recovery.Dequeue();
            if (!Alive(u)) { u.Channel = 0; u.PendingSummons.Clear(); }
        }
        private void TickHero(TeamState t, UnitState u, float dt)
        {
            if (u.StunTime > 0) return;
            float speed = u.SlowTime > 0 ? .7f : 1;
            u.BasicTimer -= dt * speed;
            if (u.BasicTimer <= 0)
            {
                UnitState target = t.Enemies.FirstOrDefault(Alive);
                if (target != null) Hit(t, u, target, u.Attack * .65f, "普攻", true);
                u.BasicTimer += 1.6f;
            }
            for (int i = 0; i < 2; i++)
            {
                u.SkillTimers[i] -= dt;
                if (u.SkillTimers[i] > 0 || !MeetsCondition(t, u.Hero.Conditions[i])) continue;
                SkillSpec skill = u.Hero.Spec.skills[u.Hero.Active[i]];
                if (UseSkill(t, u, skill)) u.SkillTimers[i] = Math.Max(1, skill.prototype_cooldown_seconds);
            }
            u.CharmTimer -= dt;
            if (u.CharmTimer <= 0)
            {
                UnitState target = t.Enemies.FirstOrDefault(Alive);
                if (target != null && (HasCharm(u.Hero, 0) || HasCharm(u.Hero, 5)))
                {
                    ApplyDot(target, u.Attack * .08f, 5, u.Name); AddLog(t, u.Name + " 的纹章施加持续伤害");
                }
                if (HasCharm(u.Hero, 1))
                {
                    UnitState ally = t.Allies.FirstOrDefault(a => Alive(a) && (a.PoisonTime > 0 || a.SlowTime > 0));
                    if (ally != null) { Cleanse(ally); AddLog(t, u.Name + " 的净化纹章清除负面"); }
                }
                u.CharmTimer = 8;
            }
        }
        private static bool MeetsCondition(TeamState t, string condition)
        {
            if (condition == "友方低血") return t.Allies.Any(a => Alive(a) && a.Hp < a.MaxHp * .6f);
            if (condition == "敌方蓄力") return t.Enemies.Any(e => Alive(e) && e.Channel > 0);
            if (condition == "友方有负面") return t.Allies.Any(a => Alive(a) && (a.PoisonTime > 0 || a.SlowTime > 0));
            return true;
        }
        private bool UseSkill(TeamState t, UnitState u, SkillSpec skill)
        {
            UnitState target = t.Enemies.FirstOrDefault(Alive);
            if (target == null) return false;
            float power = u.Attack * Math.Max(.1f, skill.prototype_power);
            string effect = skill.effect;
            if (effect == "heal")
            {
                UnitState ally = t.Allies.Where(Alive).OrderBy(a => a.Hp / a.MaxHp).First();
                if (ally.Hp >= ally.MaxHp - 1) return false;
                Recover(t, ally, power * 1.5f, false, u.Name + "·" + skill.name); return true;
            }
            if (effect == "shield")
            {
                UnitState ally = t.Allies.Where(Alive).OrderBy(a => (a.Hp + a.Shield) / a.MaxHp).First();
                Recover(t, ally, power, true, u.Name + "·" + skill.name); return true;
            }
            if (effect == "cleanse")
            {
                UnitState ally = t.Allies.FirstOrDefault(a => Alive(a) && (a.PoisonTime > 0 || a.SlowTime > 0));
                if (ally == null) return false;
                Cleanse(ally); AddLog(t, u.Name + "·" + skill.name + " 净化 " + ally.Name); return true;
            }
            if (effect == "interrupt")
            {
                target = t.Enemies.FirstOrDefault(e => Alive(e) && e.Channel > 0);
                if (target == null) return false; // 自动保留打断直至观察到蓄力。
                if (target.Monster.kind == "boss")
                {
                    target.Stagger += 1;
                    if (target.Stagger >= 2) { target.Channel = 0; target.Stagger = 0; target.AbilityTimer = 6; AddLog(t, target.Name + " 失衡，蓄力中止"); }
                    else AddLog(t, target.Name + " 失衡 +1，需要 2 点打断");
                }
                else { target.Channel = 0; target.StunTime = 1; target.AbilityTimer = 6; AddLog(t, target.Name + " 蓄力被打断"); }
            }
            if (effect == "aoe")
            {
                foreach (UnitState enemy in t.Enemies.Where(Alive).ToArray()) Hit(t, u, enemy, power * .65f, skill.name, false);
                return true;
            }
            if (effect == "backline") target = t.Enemies.Last(Alive);
            if (effect == "dispel") { target.Shield = 0; AddLog(t, target.Name + " 的护盾被驱散"); }
            if (effect == "mark" || effect == "break") target.MarkTime = 8;
            if (effect == "slow") target.SlowTime = 8;
            if (effect == "poison" || effect == "burn") ApplyDot(target, power * .12f, 6, u.Name);
            if (effect == "counter") { u.CounterTime = 5; Recover(t, u, power * .4f, true, skill.name); }
            if (effect == "heavy") power *= HasCharm(u.Hero, 4) ? 1.5f : 1.25f;
            if (effect == "execute" && target.Hp < target.MaxHp * .35f) power *= 1.5f;
            if (effect == "detonate" && target.PoisonTime > 0) { power += target.PoisonPower * Math.Min(3, target.PoisonTime); target.PoisonTime = 0; }
            if (effect == "multihit")
            {
                for (int i = 0; i < 3 && Alive(target); i++) Hit(t, u, target, power * .4f, skill.name, false);
            }
            else Hit(t, u, target, power, skill.name, false);
            return true;
        }
        private void TickEnemy(TeamState t, UnitState u, float dt)
        {
            if (u.StunTime > 0) return;
            if (u.Channel > 0)
            {
                u.Channel = Math.Max(0, u.Channel - dt);
                if (u.Channel <= 0) EnemyAbility(t, u);
                return;
            }
            u.AbilityTimer -= dt;
            bool special = u.Monster.kind != "normal" || u.Monster.summons != null && u.Monster.summons.Length > 0 ||
                Contains(u.Monster.mechanic, "毒", "治疗", "护盾", "蓄力", "减速", "恢复", "后排");
            if (special && u.AbilityTimer <= 0)
            {
                u.Channel = u.Monster.kind == "boss" ? 2.5f : 1.5f;
                AddLog(t, u.Name + " 开始蓄力 " + u.Channel + " 秒"); return;
            }
            u.BasicTimer -= dt * (u.SlowTime > 0 ? .7f : 1);
            if (u.BasicTimer <= 0)
            {
                UnitState target = t.Allies.FirstOrDefault(Alive);
                if (target != null) Hit(t, u, target, u.Attack, "攻击", true);
                u.BasicTimer += 2;
            }
        }
        private void EnemyAbility(TeamState t, UnitState u)
        {
            u.AbilityTimer = 7;
            string m = u.Monster.mechanic;
            if (!u.SummonsRequested && u.Monster.summons != null)
            {
                u.SummonsRequested = true;
                foreach (SummonSpec request in u.Monster.summons)
                {
                    MonsterSpec child;
                    if (!monsters.TryGetValue(request.monster_id, out child) || child.region != u.Monster.region || child.kind != "normal" ||
                        child.summons != null && child.summons.Length > 0) throw new InvalidOperationException("非法或递归召唤");
                    for (int n = 0; n < request.count && t.SummonQuota < 2; n++) { u.PendingSummons.Enqueue(request.monster_id); t.SummonQuota++; }
                }
                if (u.PendingSummons.Count > 0) AddLog(t, u.Name + " 请求有限召唤；满三人时等待空位");
            }
            if (Contains(m, "治疗", "恢复", "愈合"))
            {
                UnitState ally = t.Enemies.Where(Alive).OrderBy(e => e.Hp / e.MaxHp).FirstOrDefault();
                if (ally != null) Recover(t, ally, ally.MaxHp * .15f, false, u.Name);
            }
            if (Contains(m, "盾", "屏障")) Recover(t, u, u.MaxHp * .15f, true, u.Name);
            UnitState victim = Contains(m, "后排") ? t.Allies.LastOrDefault(Alive) : t.Allies.FirstOrDefault(Alive);
            if (victim == null) return;
            if (Contains(m, "毒", "瘴", "腐蚀")) { ApplyDot(victim, u.Attack * .18f, 7, u.Name); AddLog(t, victim.Name + " 中毒，可净化"); }
            if (Contains(m, "减速", "迟缓", "缓行")) victim.SlowTime = 7;
            Hit(t, u, victim, u.Attack * 2, "蓄力技", false);
        }
        private void ProcessSummons(TeamState t)
        {
            foreach (UnitState source in t.Enemies.ToArray())
            {
                if (!Alive(source)) { source.PendingSummons.Clear(); continue; }
                while (source.PendingSummons.Count > 0 && t.Enemies.Count(Alive) < 3)
                {
                    string id = source.PendingSummons.Dequeue();
                    t.Enemies.RemoveAll(e => !Alive(e));
                    t.Enemies.Add(CreateEnemy(monsters[id], Stage(t.StageId)));
                    AddLog(t, source.Name + " 召唤 " + monsters[id].name + "（本场额度不恢复）");
                }
            }
        }
        private void Hit(TeamState t, UnitState source, UnitState target, float raw, string label, bool basic)
        {
            if (!Alive(target)) return;
            float vulnerability = target.MarkTime > 0 ? .2f : 0;
            float additive = source.Hero != null && HasCharm(source.Hero, 3) && target.MarkTime > 0 ? .15f : 0;
            additive = Math.Min(.5f, additive);
            float reduction = target.Hero != null ? Math.Min(.35f, target.Hero.Armor * .025f) : 0;
            if (target.CounterTime > 0) reduction = Math.Min(.6f, reduction + .25f);
            float amount = Math.Max(0, raw * (1 + additive) * (1 + vulnerability) * (1 - reduction));
            // 原型采用固定 10% 暴击；无递归追加。召唤、DoT、反击不会再触发这个入口。
            if (source.Hero != null && NextRandom() < .1) amount *= 1.75f;
            float absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed;
            float damage = Math.Min(target.Hp, amount - absorbed); target.Hp -= damage;
            if (source.IsEnemy && !target.IsEnemy && damage > 0) t.LastDamageSource = source.Name + "·" + label;
            AddLog(t, source.Name + "·" + label + " → " + target.Name + " " + Math.Round(damage) + (absorbed > 0 ? " /盾 " + Math.Round(absorbed) : ""));
            if (basic && target.CounterTime > 0 && Alive(source))
            {
                float counter = Math.Min(source.Hp, target.Attack * .25f);
                source.Hp -= counter; AddLog(t, target.Name + " 反击 " + Math.Round(counter) + "（不再次触发）");
            }
            if (!Alive(target)) { target.Channel = 0; target.PendingSummons.Clear(); AddLog(t, target.Name + " 倒下"); }
        }
        private void Recover(TeamState t, UnitState target, float requested, bool shield, string label)
        {
            while (target.Recovery.Count > 0 && clock - target.Recovery.Peek().Time >= 20) target.Recovery.Dequeue();
            float used = target.Recovery.Sum(e => e.Amount);
            float available = Math.Max(0, target.MaxHp * .6f - used);
            float room = shield ? Math.Max(0, target.MaxHp * .35f - target.Shield) : Math.Max(0, target.MaxHp - target.Hp);
            float amount = Math.Min(Math.Max(0, requested), Math.Min(available, room));
            if (amount <= 0) return;
            if (shield) target.Shield += amount; else target.Hp += amount;
            target.Recovery.Enqueue(new RecoveryEntry { Time = clock, Amount = amount });
            AddLog(t, label + " → " + target.Name + (shield ? " 护盾 +" : " 治疗 +") + Math.Round(amount));
        }
        private static void ApplyDot(UnitState target, float power, float seconds, string source)
        {
            target.PoisonTime = Math.Max(target.PoisonTime, seconds); target.PoisonPower = Math.Max(target.PoisonPower, power);
            target.DotSource = source;
        }
        private static void Cleanse(UnitState target) { target.PoisonTime = target.SlowTime = 0; target.PoisonPower = 0; }
        private static void UpdateStatus(UnitState u)
        {
            List<string> parts = new List<string>();
            if (!Alive(u)) parts.Add("倒下");
            if (u.Channel > 0) parts.Add("蓄力 " + u.Channel.ToString("F1"));
            if (u.PoisonTime > 0) parts.Add("持续伤害");
            if (u.SlowTime > 0) parts.Add("减速");
            if (u.MarkTime > 0) parts.Add("易伤 20%");
            if (u.Shield > 0) parts.Add("护盾 " + Math.Round(u.Shield));
            if (u.PendingSummons.Count > 0) parts.Add("召唤待空位");
            u.Status = String.Join(" / ", parts);
        }
        private void Victory(TeamState t)
        {
            StageSpec s = Stage(t.StageId);
            // 奖励逐场提交，后续失败不会回滚；这些快速演示产率不是经济验收参数。
            double reward = 8 + s.region * 2 + s.index;
            Materials += reward; AddLog(t, "本场胜利，普通材料 +" + reward + "（已保留）");
            if (t.EncounterIndex < s.encounters.Length - 1) { ScheduleRest(t, 4, 1); return; }
            Cleared.Add(s.id); t.SafeStages.Add(s.id); t.Wins++; t.ConsecutiveFailures = 0;
            UpdateLevel(s);
            if (t.Index == RareTeam && t.RareEligibleRun && s.market_eligible) CommitQualified(s.region, t.RunSeconds);
            t.RunSeconds = 0; ScheduleRest(t, 4, 2); t.LastReason = "整趟通关 " + s.id + "，休整 4 秒";
            AddLog(t, t.LastReason);
        }
        private void UpdateLevel(StageSpec s)
        {
            int before = s.region == 1 ? 1 : Data.regions.First(r => r.id == s.region - 1).end_level;
            int end = Data.regions.First(r => r.id == s.region).end_level;
            Level = Math.Min(20, Math.Max(Level, before + (end - before) * s.index / 9));
        }
        private void Defeat(TeamState t, string reason)
        {
            t.ConsecutiveFailures++; t.RunSeconds = 0; ScheduleRest(t, 30, 3);
            t.LastReason = reason + "；败退 30 秒，已结算材料不丢，本趟资格作废";
            t.LastFailure = reason + "（" + t.StageId + " 第 " + (t.EncounterIndex + 1) + " 场；最近伤害 " + t.LastDamageSource + "）";
            if (t.ConsecutiveFailures >= 3)
            {
                t.SafeStages.Remove(t.StageId);
                t.Fallback = t.SafeStages.Where(id => order[id] < order[t.StageId] && !Occupied(id, t.Index))
                    .OrderByDescending(id => order[id]).FirstOrDefault();
            }
            AddLog(t, t.LastReason);
        }
        private void CommitQualified(int region, double seconds)
        {
            QualifiedSeconds[region - 1] += seconds;
            while (QualifiedSeconds[region - 1] + 1e-9 >= 900)
            {
                QualifiedSeconds[region - 1] -= 900; RareChecks++;
                bool hit = rareMisses >= 19 || NextRandom() < .1;
                if (hit) { RareDrops++; rareMisses = 0; Notice = "本地稀有模拟成功；未发放任何 Steam 物品"; }
                else rareMisses++;
            }
        }
        private double NextRandom()
        {
            // 固定 LCG，不依赖 Unity 或 .NET Random 的版本行为。
            randomState = unchecked(1664525u * randomState + 1013904223u);
            return randomState / 4294967296.0;
        }
        private static void AddLog(TeamState t, string line)
        {
            t.Log.Add(line);
            if (t.Log.Count > 20) t.Log.RemoveRange(0, t.Log.Count - 20);
        }
    }
}
