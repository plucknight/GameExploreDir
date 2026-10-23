// PROTOTYPE - NOT FOR PRODUCTION
// Question: 看见三队战斗与失败原因后，玩家会不会主动修改配置？
// Date: 2026-10-23
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NormalCampaignPrototype
{
    public sealed class PrototypeView : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        CampaignData data;
        HeroCatalog heroes;
        Font font;
        Texture2D circle;
        readonly bool[] advanceModes = { true, false, false };
        int selectedRegion = 1;
        string selectedStage = "1-1";
        float speed = 1;
        bool paused;
        bool showSummary;
        string exported = "";
        bool smoke;
        bool backgroundEnabled;
        Vector2 scroll;
        PrototypeClock clock = new PrototypeClock();
        double lastWall;
        readonly Color ink = new Color(.19f, .23f, .28f);
        readonly Color paper = new Color(.94f, .92f, .87f);
        readonly Color navy = new Color(.28f, .39f, .52f);
        static readonly string[] charmHints = {
            "创伤：每8秒向首个目标施加持续伤害（简化原型）",
            "净化：每8秒尝试清除一名同伴的负面状态",
            "守护：每场开战时为自身增加护盾",
            "猎印：对被标记目标的伤害加15%",
            "蓄能：提高重击类技能收益",
            "余烬：当前以持续伤害代理，尚未实现独立演化"
        };

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            data = JsonUtility.FromJson<CampaignData>(Resources.Load<TextAsset>("campaign").text);
            heroes = JsonUtility.FromJson<HeroCatalog>(Resources.Load<TextAsset>("heroes").text);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 16);
            circle = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    circle.SetPixel(x, y, new Color(1, 1, 1, Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) < 15.5f ? 1 : 0));
            circle.Apply();
            ResetSession();
            lastWall = Time.realtimeSinceStartupAsDouble;
            smoke = Environment.GetCommandLineArgs().Contains("-prototype-smoke");
            if (smoke) StartCoroutine(SmokeRun());
        }

        void ResetSession()
        {
            Session = new GameSession(data, heroes);
            selectedRegion = 1;
            selectedStage = "1-1";
            speed = 1;
            paused = false;
            clock = new PrototypeClock();
            advanceModes[0] = true;
            advanceModes[1] = advanceModes[2] = false;
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = Math.Max(0, now - lastWall);
            lastWall = now;
            if (Session == null) return;
            double batch = clock.Next(elapsed, paused, speed);
            if (clock.Suspended)
            {
                paused = true;
                Session.Notice = "检测到超过30秒无帧间隔，原型已暂停；该段不作为离线收益，请手动继续。";
            }
            if (batch > 0) Session.Advance(batch);
        }

        IEnumerator SmokeRun()
        {
            // 真实Player的可视回归场景，不冒充陌生玩家试玩或50小时验收。
            Session.LoadLab("3-9");
            selectedRegion = 3;
            selectedStage = "3-9";
            Session.Start(0, "3-9", false);
            Session.Start(1, "3-6", false);
            Session.Start(2, "2-7", false);
            for (int i = 0; i < 90; i++) yield return null;
            yield return new WaitForEndOfFrame();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Evidence"));
            Directory.CreateDirectory(folder);
            string dimensions = Screen.width + "x" + Screen.height;
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null) { Debug.LogError("PROTOTYPE_SCREENSHOT_FAILED"); Application.Quit(2); yield break; }
            File.WriteAllBytes(Path.Combine(folder, "three-teams-" + dimensions + ".png"), frame.EncodeToPNG());
            Destroy(frame);
            for (int i = 0; i < 10; i++) yield return null;
            File.WriteAllText(Path.Combine(folder, "player-smoke-" + dimensions + ".txt"),
                "PLAYER_SMOKE_RENDERED\nUnity=" + Application.unityVersion +
                "\nSource=" + data.source_sha256 + "\n" + string.Join("\n", Session.Summary()));
            Debug.Log("PROTOTYPE_PLAYER_SMOKE_COMPLETE");
            Application.Quit(0);
        }

        void OnDestroy()
        {
            if (circle != null) Destroy(circle);
            if (font != null) Destroy(font);
        }

        void OnGUI()
        {
            if (Session == null) return;
            // 小屏保留字号，通过滚动查看配置，不把整幅1080画布缩成不可读的小字。
            float scale = Mathf.Max(1280f / 1440f, Mathf.Min(1.1f, Screen.width / 1440f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float viewWidth = Screen.width / scale;
            float viewHeight = Screen.height / scale;
            backgroundEnabled = !showSummary && clock.PendingSeconds < .1;
            GUI.enabled = backgroundEnabled;
            scroll = GUI.BeginScrollView(new Rect(0, 0, viewWidth, viewHeight - 34), scroll, new Rect(0, 0, 1424, 1080));
            GUI.skin.font = font;
            GUI.skin.label.fontSize = 16;
            GUI.skin.label.normal.textColor = ink;
            GUI.skin.button.fontSize = 14;
            GUI.skin.button.wordWrap = true;
            GUI.skin.box.normal.textColor = ink;
            GUI.backgroundColor = new Color(.82f, .85f, .86f);
            Fill(new Rect(0, 0, 1440, 1080), paper);
            Label(new Rect(22, 12, 1390, 40), "地下城行旅  /  普通难度玩法实验", 28);
            Label(new Rect(24, 54, 1380, 25), "占位原型 · 六区54关数据 · 本地模拟无Steam资产 · 数值用于机制测试，尚未验证56小时", 15);
            Label(new Rect(24, 84, 1380, 25), $"{(Session.IsLab ? "独立实验场景" : "新开局会话")}   等级 {Session.Level}   材料 {Session.Materials:0.0}   真实 {clock.RealSeconds / 60:0.0}分 / 探索模拟 {Session.OnlineSeconds / 60:0.0}分 / 积压 {clock.PendingSeconds:0.0}秒   已通 {Session.Cleared.Count}/54", 16);

            GUI.enabled = !showSummary; // 积压期间仍可降速、暂停、重置和查看摘要。
            if (Button(24, 119, 94, paused ? "继续模拟" : "暂停模拟")) paused = !paused;
            if (Button(124, 119, 74, "1倍")) speed = 1;
            if (Button(204, 119, 74, "10倍")) speed = 10;
            if (Button(284, 119, 74, "60倍")) speed = 60;
            Label(new Rect(366, 122, 120, 25), $"当前 {speed:0} 倍", 15);
            if (Button(492, 119, 120, "新开局重置")) ResetSession();
            if (Button(618, 119, 145, "实验：3-9单槽")) Lab("3-9");
            if (Button(769, 119, 145, "实验：4-1双槽")) Lab("4-1");
            if (Button(920, 119, 145, "实验：5-5迟缓")) Lab("5-5");
            if (Button(1071, 119, 145, "回访摘要")) showSummary = !showSummary;
            if (Button(1222, 119, 190, "导出本次观察记录")) ExportSummary();
            GUI.enabled = backgroundEnabled;

            foreach (var region in data.regions)
                if (Button(24 + (region.id - 1) * 232, 159, 226, $"{region.id}. {region.name}")) selectedRegion = region.id;

            var stages = data.stages.Where(s => s.region == selectedRegion).OrderBy(s => s.index).ToArray();
            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                bool unlocked = Session.StageUnlocked(stage.id);
                string marker = Session.Cleared.Contains(stage.id) ? "✓" : unlocked ? "·" : "锁";
                GUI.backgroundColor = stage.id == selectedStage ? new Color(.53f, .69f, .75f) : new Color(.84f, .84f, .80f);
                if (GUI.Button(new Rect(24 + i * 155, 199, 149, 46), new GUIContent($"{marker} {stage.id} {(stage.kind == "boss" ? "Boss" : stage.kind == "elite" ? "精英" : "")}\n{Short(stage.name, 8)}", stage.decision))) selectedStage = stage.id;
            }
            GUI.backgroundColor = new Color(.82f, .85f, .86f);
            var selected = Session.Stage(selectedStage);
            Label(new Rect(24, 252, 1380, 25), $"当前选择 {selected.id} {selected.name}  /  {(selected.market_eligible ? "完整通关计入稀有资格" : "本关不累计稀有资格")}  /  {selected.decision}", 14);

            for (int i = 0; i < 3; i++) DrawTeam(i, 24 + i * 466, 286);

            Label(new Rect(24, 914, 1380, 24), "招募（仅本体；付费DLC未接入）  ·  资格与材料同时满足才可招募  ·  配置/成员调整需先停队", 15);
            var baseHeroes = heroes.characters.Where(h => h.kind == "base").ToArray();
            for (int i = 0; i < baseHeroes.Length; i++)
            {
                var h = baseHeroes[i];
                bool owned = Session.Heroes.Any(s => s.Spec.id == h.id);
                string state = owned ? "已加入" : Session.Cleared.Contains(h.unlock_stage ?? "") ? $"{Session.RecruitmentCost(h):0}料 / 缺{Math.Max(0, Session.RecruitmentCost(h) - Session.Materials):0}" : "资格 " + h.unlock_stage;
                GUI.enabled = backgroundEnabled && !owned;
                if (GUI.Button(new Rect(24 + i * 139, 944, 133, 48), h.name + "\n" + state)) Session.Recruit(h.id);
            }
            GUI.enabled = backgroundEnabled;
            Label(new Rect(24, 1002, 1380, 28), clock.PendingSeconds >= .1 ? "正在追赶模拟：可降到1倍并继续，积压清空后可派遣和调整配置。" : Session.Notice ?? "", 15);
            Label(new Rect(24, 1034, 1380, 22), string.IsNullOrEmpty(GUI.tooltip) ? "仅本次会话，关闭会重置；实验按钮重建独立状态。稀有计数是规则模拟，不可交易。" : GUI.tooltip, 13);

            GUI.EndScrollView();
            GUI.enabled = true;
            Fill(new Rect(0, viewHeight - 34, viewWidth, 34), new Color(.90f, .90f, .85f));
            string notice = clock.PendingSeconds >= .1 ? "正在追赶模拟：降到1倍并继续，积压清空后可派遣和调整配置。" : Session.Notice ?? "";
            Label(new Rect(18, viewHeight - 31, viewWidth - 36, 30), string.IsNullOrEmpty(GUI.tooltip) ? notice : GUI.tooltip, 14);
            if (showSummary)
            {
                float mx = Math.Max(8, (viewWidth - 1000) / 2);
                float my = Math.Max(8, (viewHeight - 540) / 2);
                Fill(new Rect(mx, my, 1000, 540), new Color(.97f, .96f, .92f));
                GUI.Box(new Rect(mx, my, 1000, 540), "");
                Label(new Rect(mx + 24, my + 16, 930, 38), "回访摘要 / 可观察的变化", 24);
                float y = my + 65;
                foreach (string line in Session.Summary())
                { Label(new Rect(mx + 24, y, 930, 42), line, 16); y += 44; }
                Label(new Rect(mx + 24, my + 432, 930, 50), exported, 14);
                if (Button(mx + 870, my + 495, 100, "关闭")) showSummary = false;
            }
        }

        void Lab(string stage)
        {
            Session.LoadLab(stage);
            selectedStage = stage;
            selectedRegion = Session.Stage(stage).region;
            speed = 1;
            paused = false;
            clock = new PrototypeClock();
            advanceModes[0] = advanceModes[1] = advanceModes[2] = false;
        }

        void DrawTeam(int index, float x, float y)
        {
            TeamState team = Session.Teams[index];
            Fill(new Rect(x, y, 454, 615), new Color(.985f, .978f, .948f));
            GUI.Box(new Rect(x, y, 454, 615), "");
            bool unlocked = Session.TeamUnlocked(index);
            Label(new Rect(x + 12, y + 8, 330, 30), $"第{index + 1}队  {(unlocked ? team.Running ? "探索中" : "休息" : "尚未开放")}", 21);
            if (Button(x + 364, y + 8, 78, team.Expanded ? "折叠" : "展开")) team.Expanded = !team.Expanded;
            if (!unlocked)
            {
                Label(new Rect(x + 18, y + 72, 415, 70), index == 1 ? "拥有4名角色并首通1-5\n之后开放第二队" : "拥有6名角色并首通1-9\n之后开放第三队", 19);
                return;
            }
            if (!team.Expanded)
            {
                Label(new Rect(x + 18, y + 64, 415, 60), $"{team.StageId}  本次成功 {team.Wins} 趟\n折叠不影响模拟", 17);
                return;
            }
            if (Button(x + 12, y + 48, 122, "派往 " + selectedStage)) Session.Start(index, selectedStage, advanceModes[index]);
            if (Button(x + 140, y + 48, 64, "停队")) Session.Stop(index);
            GUI.enabled = backgroundEnabled && !team.Running;
            bool shownMode = team.Running ? team.AdvanceMode : advanceModes[index];
            if (Button(x + 210, y + 48, 108, shownMode ? "自动推进" : "重复刷取")) advanceModes[index] = !advanceModes[index];
            GUI.enabled = backgroundEnabled;
            if (Button(x + 324, y + 48, 118, Session.RareTeam == index ? "稀有目标 ✓" : "设稀有目标")) Session.SetRareTeam(index);
            Label(new Rect(x + 12, y + 85, 430, 26), $"{team.StageId ?? "未派遣"}  第{team.EncounterIndex + 1}/3场  连败{team.ConsecutiveFailures}  成功{team.Wins}", 14);

            Rect board = new Rect(x + 12, y + 114, 430, 160);
            int actualRegion = Session.Stage(team.StageId)?.region ?? 1;
            Fill(board, new Color(.86f - actualRegion * .008f, .87f - actualRegion * .012f, .85f));
            Fill(new Rect(board.x, board.y + 129, board.width, 31), new Color(.74f, .76f, .73f));
            for (int i = 0; i < team.Allies.Count; i++) DrawUnit(team.Allies[i], board.x + 48 + i * 84, board.y + 53, i);
            for (int i = 0; i < team.Enemies.Count; i++) DrawUnit(team.Enemies[i], board.x + 240 + i * 72, board.y + 53, i);
            Label(new Rect(board.x + 8, board.y + 144, 414, 16), team.RestSeconds > 0 ? $"休整 {team.RestSeconds:0.0}秒 · {team.LastReason}" : team.LastReason ?? "等待出发", 10);
            for (int slot = 0; slot < 2; slot++) DrawHero(index, slot, x + 12, y + 284 + slot * 111);

            Label(new Rect(x + 12, y + 509, 420, 20), "最近事件（完整情况见回访摘要）", 13);
            var recent = team.Log.Skip(Math.Max(0, team.Log.Count - 3)).ToArray();
            for (int i = 0; i < recent.Length; i++) Label(new Rect(x + 12, y + 535 + i * 23, 430, 23), Short(recent[i], 32), 13);
        }

        void DrawHero(int teamIndex, int slot, float x, float y)
        {
            TeamState team = Session.Teams[teamIndex];
            HeroState h = slot < team.Members.Count ? team.Members[slot] : null;
            if (Button(x, y, 94, h == null ? "选择队员" : h.Spec.name + " 换人")) CycleMember(teamIndex, slot);
            if (Button(x + 100, y, 45, "卸下")) Session.Unassign(teamIndex, slot);
            if (h == null) return;
            if (Button(x + 151, y, 132, $"兵装{h.Weapon} ↑{6 + (h.Weapon - 8) * 2}料")) Session.Upgrade(h.Spec.id);
            string charm = h.Charm < 0 ? "未装" : GameSession.CharmNames[h.Charm];
            if (GUI.Button(new Rect(x + 289, y, 67, 30), new GUIContent(charm, h.Charm < 0 ? "通过配方关后点击选择基础纹章" : charmHints[h.Charm]))) Session.CycleCharm(h.Spec.id, 0);
            GUI.enabled = backgroundEnabled && Session.Cleared.Contains("3-9");
            if (GUI.Button(new Rect(x + 362, y, 67, 30), new GUIContent(h.Charm2 < 0 ? "第二槽" : GameSession.CharmNames[h.Charm2], h.Charm2 < 0 ? "通过3-9后开放第二槽" : charmHints[h.Charm2]))) Session.CycleCharm(h.Spec.id, 1);
            GUI.enabled = backgroundEnabled;
            for (int k = 0; k < 2; k++)
            {
                SkillSpec skill = h.Spec.skills[h.Active[k]];
                if (Button(x + k * 216, y + 38, 210, skill.name + " · 换技能")) Session.ChangeSkill(h.Spec.id, k);
                if (Button(x + k * 216, y + 74, 210, "条件：" + h.Conditions[k], 26)) Session.CycleCondition(h.Spec.id, k);
            }
        }

        void CycleMember(int team, int slot)
        {
            string current = Session.Teams[team].Members.Count > slot ? Session.Teams[team].Members[slot].Spec.id : null;
            int start = Session.Heroes.FindIndex(h => h.Spec.id == current);
            for (int i = 1; i <= Session.Heroes.Count; i++)
            {
                var candidate = Session.Heroes[(start + i + Session.Heroes.Count) % Session.Heroes.Count];
                if (candidate.Spec.id != current && Session.Assign(team, slot, candidate.Spec.id)) return;
            }
        }

        void DrawUnit(UnitState unit, float x, float y, int index)
        {
            float bob = unit.Hp > 0 ? Mathf.Sin((float)Session.OnlineSeconds * 2 + index) * 2 : 0;
            Color coat = unit.IsEnemy ? new Color(.53f, .40f, .41f) : navy;
            if (unit.Hp <= 0) coat = Color.gray;
            GUI.color = new Color(.90f, .76f, .61f);
            GUI.DrawTexture(new Rect(x - 13, y - 27 + bob, 26, 26), circle);
            GUI.color = Color.white;
            Fill(new Rect(x - 11, y + bob, 22, 28), coat);
            Fill(new Rect(x - 10, y + 28, 7, 14), ink);
            Fill(new Rect(x + 3, y + 28, 7, 14), ink);
            Fill(new Rect(x + (unit.IsEnemy ? -20 : 17), y + 3 + bob, 4, 27), new Color(.63f, .65f, .66f));
            Label(new Rect(x - 37, y - 51, 75, 22), Short(unit.Name, 5), 12);
            Fill(new Rect(x - 31, y + 49, 62, 6), new Color(.58f, .57f, .53f));
            Fill(new Rect(x - 31, y + 49, 62 * Mathf.Clamp01(unit.Hp / Math.Max(1, unit.MaxHp)), 6), new Color(.36f, .57f, .39f));
            if (unit.Shield > 0) Fill(new Rect(x - 31, y + 57, 62 * Mathf.Clamp01(unit.Shield / Math.Max(1, unit.MaxHp)), 3), new Color(.3f, .6f, .8f));
            if (unit.Channel > 0) Label(new Rect(x - 36, y + 62, 80, 24), "蓄力 " + unit.Channel.ToString("0.0"), 14);
            else if (!string.IsNullOrEmpty(unit.Status)) Label(new Rect(x - 36, y + 62, 80, 24), Short(unit.Status, 6), 14);
        }

        void ExportSummary()
        {
            string path = Path.Combine(Application.persistentDataPath, "observation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(path, "PROTOTYPE - NOT A PLAYER STUDY\n" + string.Join("\n", Session.Summary()) +
                "\nreal_seconds=" + clock.RealSeconds + "\nexploration_simulated_seconds=" + Session.OnlineSeconds +
                "\npending_simulated_seconds=" + clock.PendingSeconds + "\nsuspended_seconds=" + clock.SuspendedSeconds + "\nsource=" + data.source_sha256);
            exported = "已导出到 " + path;
            showSummary = true;
        }

        bool Button(float x, float y, float width, string text, float height = 30) => GUI.Button(new Rect(x, y, width, height), text);
        void Label(Rect rect, string text, int size)
        {
            GUI.Label(rect, text, new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true });
        }
        static string Short(string value, int n) => string.IsNullOrEmpty(value) || value.Length <= n ? value : value.Substring(0, n - 1) + "…";
        static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
