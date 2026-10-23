// PROTOTYPE - NOT FOR PRODUCTION
// Question: 同一份关卡数据能否在真实Unity中编译、运行并显示三队？
// Date: 2026-10-23
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NormalCampaignPrototype.EditorTools
{
    public static class PrototypeBuild
    {
        [Serializable] class CheckReport
        {
            public string status, unity, source_sha256, timestamp_utc;
            public string[] checks;
        }

        public static void Validate()
        {
            var campaign = JsonUtility.FromJson<CampaignData>(Resources.Load<TextAsset>("campaign").text);
            var heroes = JsonUtility.FromJson<HeroCatalog>(Resources.Load<TextAsset>("heroes").text);
            if (campaign.stages.Length != 54 || campaign.regions.Length != 6 || heroes.characters.Length != 12)
                throw new InvalidOperationException("Canonical data missing or stale.");
            var checks = new List<string>(PrototypeChecks.Run(campaign, heroes));
            var slow = new PrototypeClock();
            double processed = slow.Next(2, false, 10);
            for (int i = 0; i < 10; i++) processed += slow.Next(0, false, 10);
            if (Math.Abs(processed - 20) > .000001 || slow.PendingSeconds != 0)
                throw new InvalidOperationException("Clock discarded slow-frame backlog.");
            if (slow.Next(1, true, 10) != 0 || slow.Next(31, false, 10) != 0 || !slow.Suspended)
                throw new InvalidOperationException("Pause/suspension awarded simulation time.");
            checks.Add("Clock keeps slow-frame backlog; pause and suspension award no time.");
            string evidence = Path.Combine(Root(), "production/qa/evidence/local");
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "unity-domain-checks.json"), JsonUtility.ToJson(new CheckReport
            {
                status = "PASS", unity = Application.unityVersion, source_sha256 = campaign.source_sha256,
                timestamp_utc = DateTime.UtcNow.ToString("O"), checks = checks.ToArray()
            }, true));
            Debug.Log("PROTOTYPE_DOMAIN_CHECKS_PASS: " + checks.Count);
        }

        public static void Build()
        {
            Validate();
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.94f, .92f, .87f);
            camera.orthographic = true;
            cameraObject.tag = "MainCamera";
            new GameObject("Normal Campaign Prototype").AddComponent<PrototypeView>();
            const string scenePath = "Assets/Scenes/CampaignPrototype.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            PlayerSettings.companyName = "GameExploreDir";
            PlayerSettings.productName = "Normal Campaign Prototype";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Windows/NormalCampaign.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.SaveAssets();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Root(), "production/qa/evidence/local/windows-build.txt"),
                "PASS\nUnity=" + Application.unityVersion + "\nTarget=StandaloneWindows64\nBytes=" + report.summary.totalSize + "\n");
            Debug.Log("PROTOTYPE_WINDOWS_BUILD_PASS");
        }

        static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
    }
}
