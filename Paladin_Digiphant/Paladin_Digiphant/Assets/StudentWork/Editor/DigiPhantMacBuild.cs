using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DigiPhant
{
    // Builds Builds/DigiPhant.app with the frozen tracker inside. tools/build_mac_app.sh freezes the
    // tracker first, then calls Build() in batch mode; the menu item rebuilds with Unity open.
    public static class DigiPhantMacBuild
    {
        static readonly string[] Scenes =
        {
            "Assets/StudentWork/Scenes/DigiPhant_Adventure.unity",
            "Assets/StudentWork/TrunkTrail/Scenes/DigiPhant_TrunkTrail.unity",
        };
        // Every Shader.Find target in Assets/StudentWork (DigiPhantAdventure's course and sign materials).
        static readonly string[] FoundShaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit" };
        const string App = "Builds/DigiPhant.app";
        // Its own bundle (com.teampaladin.digiphant.tracker): macOS applies per-app camera settings such as
        // Center Stage only to bundled processes, so bridge.py can turn Center Stage off only from inside one.
        const string FrozenTracker = "Builds/Tracker/dist/DigiPhantTracker.app";
        const string Icon = "AppIcon/DigiPhantIcon.png";
        const string CameraReason = "DigiPhant reads your webcam to turn your poses into elephant moves. Video stays on this Mac.";

        [MenuItem("DigiPhant/Build Mac App")]
        static void BuildFromMenu()
        {
            if (Build()) EditorUtility.RevealInFinder(App);
        }

        public static bool Build()
        {
            if (!File.Exists(Path.Combine(FrozenTracker, "Contents/MacOS/DigiPhantTracker")))
            {
                UnityEngine.Debug.LogError("Frozen tracker missing at " + FrozenTracker + ". Run tools/build_mac_app.sh once first.");
                return false;
            }
            // Product name and bundle ID decide where PlayerPrefs live, so restore them to keep Editor settings.
            string product = PlayerSettings.productName, company = PlayerSettings.companyName;
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            // One shared field; Unity refuses to build WebCamTexture use (DigiPhantAppTracker) without it.
            string cameraUsage = PlayerSettings.iOS.cameraUsageDescription;
            // Scripts create materials with Shader.Find; a shader no scene material uses is stripped unless always included.
            var graphics = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
            var included = graphics.FindProperty("m_AlwaysIncludedShaders");
            int originalCount = included.arraySize;
            foreach (string name in FoundShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) { UnityEngine.Debug.LogError("Shader missing in project: " + name); return false; }
                bool present = false;
                for (int i = 0; i < included.arraySize; i++) present |= included.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present) continue;
                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            BuildReport report;
            try
            {
                PlayerSettings.productName = "DigiPhant";
                PlayerSettings.companyName = "Team Paladin";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.teampaladin.digiphant");
                PlayerSettings.iOS.cameraUsageDescription = CameraReason;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes, locationPathName = App, target = BuildTarget.StandaloneOSX, options = BuildOptions.None
                });
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.companyName = company;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                PlayerSettings.iOS.cameraUsageDescription = cameraUsage;
                graphics.Update();
                included.arraySize = originalCount;
                graphics.ApplyModifiedPropertiesWithoutUndo();
                // Unity saves settings to disk during the build; save the restored values too, or the build's stay.
                EditorUtility.SetDirty(graphics.targetObject);
                AssetDatabase.SaveAssets();
            }
            if (report.summary.result != BuildResult.Succeeded)
            {
                UnityEngine.Debug.LogError("Mac build failed: " + report.summary.result);
                return false;
            }
            // The tracker runs as a child of the app, so macOS asks camera permission on the app's behalf.
            // DigiPhantAppTracker starts Contents/Resources/DigiPhantTracker/DigiPhantTracker, a launcher for the bundle.
            string target = Path.Combine(App, "Contents/Resources/DigiPhantTracker");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            string launcher = Path.Combine(target, "DigiPhantTracker");
            File.WriteAllText(launcher, "#!/bin/sh\nexec \"$(dirname \"$0\")/DigiPhantTracker.app/Contents/MacOS/DigiPhantTracker\" \"$@\"\n");
            bool ok = Run("/bin/cp", "-R \"" + FrozenTracker + "\" \"" + target + "/\"")
                && Run("/bin/chmod", "+x \"" + launcher + "\"")
                && Run("/usr/bin/codesign", "--force --deep --sign - \"" + target + "/DigiPhantTracker.app\"")
                && Run("/usr/bin/plutil", "-replace NSCameraUsageDescription -string \"" + CameraReason + "\" \"" + App + "/Contents/Info.plist\"")
                && ReplaceIcon()
                && Run("/usr/bin/codesign", "--force --deep --sign - \"" + App + "\"");
            UnityEngine.Debug.Log(ok ? "Built " + Path.GetFullPath(App) : "Mac build finished but packaging failed; see errors above.");
            return ok;
        }

        // Team Paladin icon (1024 px, already on the macOS icon grid) in place of Unity's default PlayerIcon.icns.
        static bool ReplaceIcon()
        {
            string iconset = "Builds/Tracker/DigiPhant.iconset";
            if (Directory.Exists(iconset)) Directory.Delete(iconset, true);
            Directory.CreateDirectory(iconset);
            foreach (int size in new[] { 16, 32, 128, 256, 512 })
                for (int scale = 1; scale <= 2; scale++)
                {
                    string name = "icon_" + size + "x" + size + (scale == 2 ? "@2x" : "") + ".png";
                    if (!Run("/usr/bin/sips", "-z " + size * scale + " " + size * scale + " \"" + Icon + "\" --out \"" + iconset + "/" + name + "\"")) return false;
                }
            return Run("/usr/bin/iconutil", "-c icns \"" + iconset + "\" -o \"" + App + "/Contents/Resources/PlayerIcon.icns\"");
        }

        static bool Run(string tool, string arguments)
        {
            var info = new ProcessStartInfo(tool, arguments) { UseShellExecute = false, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0) return true;
                UnityEngine.Debug.LogError(Path.GetFileName(tool) + " failed: " + error);
                return false;
            }
        }

        // Batch mode entry: Unity -batchmode -quit -executeMethod DigiPhant.DigiPhantMacBuild.BuildBatch
        public static void BuildBatch() => EditorApplication.Exit(Build() ? 0 : 1);
    }
}
