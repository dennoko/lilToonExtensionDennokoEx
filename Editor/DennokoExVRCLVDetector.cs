#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Dennokoworks
{
    [InitializeOnLoad]
    public static class DennokoExVRCLVDetector
    {
        const string VRCLVPackage = "red.sim.lightvolumes";
        const string LilToonPackage = "jp.lilxyzw.liltoon";
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

        const string CheckedKey = "DennokoEx.VRCLVChecked";

        static DennokoExVRCLVDetector()
        {
            // Only VRCLV / lilToon package changes can change the inject content. Reimporting the bridge
            // recompiles every DennokoEx shader, so unrelated package installs must not reach it.
            Events.registeredPackages += OnPackagesRegistered;
            // Once per editor session; later changes arrive through registeredPackages or the menu.
            if (SessionState.GetBool(CheckedKey, false)) return;
            SessionState.SetBool(CheckedKey, true);
            Regenerate();
        }

        static void OnPackagesRegistered(PackageRegistrationEventArgs args)
        {
            if (!Involves(args.added) && !Involves(args.removed) && !Involves(args.changedTo)) return;
            // Unity's post-install compile is likely still running here; Regenerate defers the reimport
            // until the editor is idle so it does not stack a second shader compile on top of it.
            EditorApplication.delayCall += () => Regenerate();
        }

        static bool Involves(System.Collections.Generic.IEnumerable<UnityEditor.PackageManager.PackageInfo> packages)
        {
            foreach (var p in packages)
                if (p.name == VRCLVPackage || p.name == LilToonPackage) return true;
            return false;
        }

        [MenuItem("Window/DennokoEx/Regenerate VRCLV Bridge")]
        public static void Regenerate()
        {
            if (!WriteInjectIfChanged()) return;
            ScheduleBridgeReimportWhenIdle();
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
            if (AssetDatabase.IsValidFolder(VRCLVFolder))
                return ContentWithInclude(VRCLVInclude);

            foreach (string candidate in LilToonBundledIncludes)
            {
                if (File.Exists(Path.GetFullPath(candidate)))
                    return ContentWithInclude(candidate);
            }

            return ContentUnavailable();
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
