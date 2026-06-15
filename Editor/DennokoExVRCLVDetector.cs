#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Dennokoworks
{
    [InitializeOnLoad]
    public static class DennokoExVRCLVDetector
    {
        const string VRCLVFolder  = "Packages/red.sim.lightvolumes";
        const string VRCLVInclude = "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc";

        // lilToon bundles its own copy of LightVolumes.cginc (LIL_FEATURE_VRCLIGHTVOLUMES_WITHOUTPACKAGE).
        // DennokoEx is a lilToon 2.x extension, so this fallback is effectively always present.
        // Same include guard (VRC_LIGHT_VOLUMES_INCLUDED) and API as the package version.
        static readonly string[] LilToonBundledIncludes =
        {
            "Packages/jp.lilxyzw.liltoon/Shader/Includes/VRC Light Volumes/LightVolumes.cginc",
            "Assets/lilToon/Shader/Includes/VRC Light Volumes/LightVolumes.cginc",
        };

        // ~ suffix: Unity ignores this file entirely (no .meta, excluded from Export Package and VPM).
        const string InjectAsset  = "Assets/dennokoworks/DennokoEx/Shaders/DennokoEx_VRCLV_Inject.cginc~";
        // Bridge is a normal Unity asset; reimporting it triggers transitive shader recompile.
        const string BridgeAsset  = "Assets/dennokoworks/DennokoEx/Shaders/DennokoEx_VRCLV_Bridge.cginc";

        static DennokoExVRCLVDetector()
        {
            // On package add/remove: write inject file and schedule bridge reimport.
            // Unity's post-install compile is likely still running at this point, so
            // we defer the ImportAsset call until the editor is fully idle to avoid
            // triggering a second heavy shader compile pass on top of Unity's own.
            Events.registeredPackages += _ => EditorApplication.delayCall += RegenerateAfterPackageChange;
            SetupGitSkipWorktree();
            Regenerate();
        }

        [MenuItem("Window/DennokoEx/Regenerate VRCLV Bridge")]
        public static void Regenerate()
        {
            LogDetectionState();
            if (!WriteInjectIfChanged()) return;
            ScheduleBridgeReimportWhenIdle();
        }

        // Dumps the full VRCLV detection state to the Console so the recognized
        // include path and the resulting DNKW_VRCLV_AVAILABLE value can be verified.
        // Runs automatically on every Regenerate (startup + package change) and is
        // also exposed as a manual menu action for on-demand inspection.
        [MenuItem("Window/DennokoEx/Log VRCLV Detection State")]
        public static void LogDetectionState()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DennokoEx VRCLV] Detection state");

            bool pkg = AssetDatabase.IsValidFolder(VRCLVFolder);
            sb.AppendLine($"  red.sim package folder ({VRCLVFolder}): {(pkg ? "FOUND" : "not found")}");

            foreach (string candidate in LilToonBundledIncludes)
            {
                bool exists = File.Exists(Path.GetFullPath(candidate));
                sb.AppendLine($"  lilToon bundled include ({candidate}): {(exists ? "FOUND" : "not found")}");
            }

            string includePath = ResolveIncludePath();
            if (includePath != null)
                sb.AppendLine($"  => Resolved include: {includePath}  (DNKW_VRCLV_AVAILABLE = 1)");
            else
                sb.AppendLine("  => No LightVolumes.cginc found  (DNKW_VRCLV_AVAILABLE = 0, Reflection 2nd disabled)");

            string fullPath = Path.GetFullPath(InjectAsset);
            if (File.Exists(fullPath))
            {
                sb.AppendLine($"  Inject file present: {InjectAsset}");
                sb.AppendLine("  --- current inject content on disk ---");
                sb.Append(File.ReadAllText(fullPath, Encoding.UTF8).TrimEnd('\n'));
            }
            else
            {
                sb.AppendLine($"  Inject file MISSING: {fullPath}");
                sb.Append("  (Bridge will default DNKW_VRCLV_AVAILABLE to 0)");
            }

            UnityEngine.Debug.Log(sb.ToString());
        }

        // Resets inject file to safe default (VRCLV=0) before distributing the package.
        // Run this before exporting/releasing the package.
        [MenuItem("Window/DennokoEx/Prepare for Export (Reset VRCLV Bridge)")]
        public static void PrepareForExport()
        {
            string fullPath = Path.GetFullPath(InjectAsset);
            string safe = ContentUnavailable();
            if (File.Exists(fullPath) && File.ReadAllText(fullPath, Encoding.UTF8) == safe)
            {
                EditorUtility.DisplayDialog("DennokoEx", "Inject file is already in safe state. No changes made.", "OK");
                return;
            }
            File.WriteAllText(fullPath, safe, Encoding.UTF8);
            ScheduleBridgeReimportWhenIdle();
            EditorUtility.DisplayDialog("DennokoEx", "DennokoEx_VRCLV_Inject.cginc~ has been reset to safe default.\nYou can now export the package.", "OK");
        }

        // Prevent accidental commits of the VRCLV=1 inject file in git-based VPM packages.
        static void SetupGitSkipWorktree()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                var psi = new ProcessStartInfo("git", $"update-index --skip-worktree {InjectAsset}")
                {
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                };
                Process.Start(psi);
            }
            catch { } // Silently ignore if git is unavailable or this is not a git repo
        }

        // Called via delayCall when packages are registered/unregistered.
        // Unity's own post-install shader compile may still be in progress here,
        // so we schedule the bridge reimport to run only once Unity is fully idle.
        static void RegenerateAfterPackageChange()
        {
            WriteInjectIfChanged();
            ScheduleBridgeReimportWhenIdle();
        }

        // Polls until Unity is neither compiling nor updating, then reimports the bridge.
        // This prevents stacking our forced shader recompile on top of Unity's own,
        // which was causing editor freezes when VRCLV was added mid-compilation.
        static void ScheduleBridgeReimportWhenIdle()
        {
            EditorApplication.delayCall += TryBridgeReimport;
        }

        static void TryBridgeReimport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // Not idle yet — check again next frame.
                EditorApplication.delayCall += TryBridgeReimport;
                return;
            }
            AssetDatabase.ImportAsset(BridgeAsset, ImportAssetOptions.ForceUpdate);

            // Reimport all .lilcontainer files to force compilation of custom shaders with the new bridge state.
            string assetsDir = UnityEngine.Application.dataPath;
            if (Directory.Exists(assetsDir))
            {
                string[] containers = Directory.GetFiles(assetsDir, "*.lilcontainer", SearchOption.AllDirectories);
                foreach (string container in containers)
                {
                    string assetPath = "Assets" + container.Substring(assetsDir.Length).Replace("\\", "/");
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
                UnityEngine.Debug.Log($"[DennokoEx] Force reimported {containers.Length} .lilcontainer files.");
            }
        }

        // Returns true if the inject file was written (content changed).
        static bool WriteInjectIfChanged()
        {
            string content = ResolveInjectContent();
            string fullPath = Path.GetFullPath(InjectAsset);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath, Encoding.UTF8) == content)
                return false;
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return true;
        }

        // Decide which LightVolumes.cginc to include (priority order):
        //  1. red.sim.lightvolumes package present  -> package copy
        //  2. lilToon's bundled copy present         -> lilToon copy (always-on VRCLV without the package)
        //  3. neither (extreme legacy env only)      -> empty (DNKW_VRCLV_AVAILABLE=0)
        static string ResolveInjectContent()
        {
            UnityEngine.Debug.Log("[DennokoEx] Resolving VRCLV inject content...");
            bool packageFolderExists = AssetDatabase.IsValidFolder(VRCLVFolder);
            UnityEngine.Debug.Log($"[DennokoEx] IsValidFolder('{VRCLVFolder}'): {packageFolderExists}");

            if (packageFolderExists)
            {
                UnityEngine.Debug.Log($"[DennokoEx] VRCLV detected via package: {VRCLVInclude}");
                return ContentWithInclude(VRCLVInclude);
            }

            foreach (string candidate in LilToonBundledIncludes)
            {
                string fullPath = Path.GetFullPath(candidate);
                bool fileExists = File.Exists(fullPath);
                string guid = AssetDatabase.AssetPathToGUID(candidate);
                bool assetExists = !string.IsNullOrEmpty(guid);
                UnityEngine.Debug.Log($"[DennokoEx] Candidate: '{candidate}'\n" +
                                      $"  - Full Path: {fullPath}\n" +
                                      $"  - File.Exists: {fileExists}\n" +
                                      $"  - AssetDatabase.AssetPathToGUID: {guid} (Exists: {assetExists})");

                if (assetExists)
                {
                    UnityEngine.Debug.Log($"[DennokoEx] VRCLV detected via lilToon bundled copy: {candidate}");
                    return ContentWithInclude(candidate);
                }
            }

            UnityEngine.Debug.Log("[DennokoEx] VRCLV not detected. Falling back to unavailable.");
            return ContentUnavailable();
        }

        // Returns the LightVolumes.cginc include path to use, or null if VRCLV is unavailable.
        static string ResolveIncludePath()
        {
            if (AssetDatabase.IsValidFolder(VRCLVFolder))
                return VRCLVInclude;

            foreach (string candidate in LilToonBundledIncludes)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate)))
                    return candidate;
            }

            return null;
        }

        static string ContentWithInclude(string includePath) =>
            "// Auto-generated by DennokoExVRCLVDetector.cs — do not edit manually.\n" +
            "// VRCLV available. To regenerate: Window > DennokoEx > Regenerate VRCLV Bridge\n" +
            "#ifndef DNKW_VRCLV_INJECT_INCLUDED\n" +
            "#define DNKW_VRCLV_INJECT_INCLUDED\n" +
            "#include \"" + includePath + "\"\n" +
            "#define DNKW_VRCLV_INJECTED 1\n" +
            "#endif\n";

        static string ContentUnavailable() =>
            "// Auto-generated by DennokoExVRCLVDetector.cs — do not edit manually.\n" +
            "// VRCLV not detected. To regenerate: Window > DennokoEx > Regenerate VRCLV Bridge\n";
    }
}
#endif
