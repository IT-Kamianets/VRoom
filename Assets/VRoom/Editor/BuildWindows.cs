using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Builds the Windows player the installer packs: Builds/Windows/VRoom.exe (see RELEASING.md).
// Menu: VRoom > Build Windows. Command line: Unity.exe -batchmode -quit -projectPath . -executeMethod VRoom.EditorTools.BuildWindows.Run
// The environment variable VROOM_VERSION (for example 0.1.7) sets the player version, so every CI build is numbered.
namespace VRoom.EditorTools
{
    public static class BuildWindows
    {
        private const string OAuthSource = "Assets/VRoom/Firebase/google-oauth-client.json";
        private const string OAuthCopy = "Assets/StreamingAssets/google-oauth-client.json";

        [MenuItem("VRoom/Build Windows")]
        public static void Run()
        {
            var folder = Path.GetFullPath("Builds/Windows");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);

            var version = Environment.GetEnvironmentVariable("VROOM_VERSION");
            if (!string.IsNullOrWhiteSpace(version))
            {
                PlayerSettings.bundleVersion = version.Trim();
            }

            // The Google sign-in needs the (git-ignored) OAuth client file inside the game. It is copied next to the
            // streaming assets for the build only, and removed again afterwards.
            var copied = false;
            if (File.Exists(OAuthSource))
            {
                Directory.CreateDirectory("Assets/StreamingAssets");
                File.Copy(OAuthSource, OAuthCopy, true);
                AssetDatabase.Refresh();
                copied = true;
            }
            else
            {
                UnityEngine.Debug.LogWarning("[VRoom] " + OAuthSource + " is missing: the build will not be able to sign in with Google.");
            }

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Login.unity" },
                    locationPathName = Path.Combine(folder, "VRoom.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new Exception("Windows build failed: " + report.summary.result);
                }

                UnityEngine.Debug.Log("Windows build written to " + folder + " (" + report.summary.totalSize / (1024 * 1024) + " MB), version " + PlayerSettings.bundleVersion);
            }
            finally
            {
                if (copied)
                {
                    AssetDatabase.DeleteAsset(OAuthCopy);
                }
            }
        }
    }
}
