#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Dennokoworks
{
    // Diagnostic utility for the "State comes from an incompatible keyword space" warning.
    //
    // The warning fires when a material has an enabled keyword whose stored space (global vs
    // shader_feature_local) no longer matches the keyword space declared by its current shader.
    // This tool scans materials and reports, per material, exactly which keywords are in an
    // incompatible space, plus the transparent-mode / blend state that decides visibility.
    //
    // Usage:
    //   1. Select the avatar root (or any GameObjects / Material assets) in the Hierarchy/Project.
    //      If nothing is selected, the active scene is scanned.
    //   2. Window > DennokoEx > Diagnostics > Scan Keyword Space   -> prints a report to the Console.
    //   3. Window > DennokoEx > Diagnostics > Repair Keyword Space -> attempts to re-sync keywords.
    internal static class DennokoExKeywordDiagnostics
    {
        const string kAlphaClip = "UNITY_UI_ALPHACLIP"; // -> LIL_RENDER 1 (Cutout)
        const string kClipRect  = "UNITY_UI_CLIP_RECT"; // -> LIL_RENDER 2 (Transparent)

        [MenuItem("Window/DennokoEx/Diagnostics/Scan Keyword Space")]
        static void Scan()
        {
            Debug.LogWarning("[DennokoEx][KW] Scan started. (Unmissable warning-level marker. If you don't see THIS line after clicking, the script didn't recompile — focus Unity and wait for the compile spinner, and check the Console for red compile errors.)");
            try
            {
                ScanImpl();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DennokoEx][KW] Scan threw an exception:\n" + e);
            }
        }

        static void ScanImpl()
        {
            var mats = CollectMaterials();
            Debug.Log($"[DennokoEx][KW] Collected {mats.Count} material(s) from selection/scene.");
            if (mats.Count == 0)
            {
                Debug.LogWarning("[DennokoEx][KW] No materials found. Select the avatar root, GameObjects, or Material assets and run again.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[DennokoEx][KW] Keyword-space scan: {mats.Count} material(s)");
            int problems = 0;

            foreach (var m in mats)
            {
              try
              {
                if (m == null || m.shader == null) continue;

                // Names enabled according to the legacy string API (includes stale/wrong-space entries).
                string[] legacy = m.shaderKeywords ?? new string[0];

                // Names enabled that Unity actually resolved within the current shader's local space.
                var resolved = new HashSet<string>(m.enabledKeywords.Select(k => k.name));

                // Names the current shader actually declares.
                var declared = new HashSet<string>(m.shader.keywordSpace.keywordNames);

                // Incompatible space = enabled in the legacy list but NOT resolved as a valid local keyword.
                var incompatible = legacy.Where(n => !resolved.Contains(n)).ToArray();

                bool hasProblem = incompatible.Length > 0;

                int tpmode = m.HasProperty("_TransparentMode") ? Mathf.RoundToInt(m.GetFloat("_TransparentMode")) : -1;
                bool wantAlphaClip = tpmode == 1;
                bool wantClipRect  = tpmode == 2 || tpmode == 4;
                bool hasAlphaClip  = legacy.Contains(kAlphaClip);
                bool hasClipRect   = legacy.Contains(kClipRect);

                // Only keyword-driven (Multi) shaders express render mode via UNITY_UI_* keywords.
                // Non-multi variants bake render mode with `#define LIL_RENDER` and declare no such
                // keyword, so the check below is meaningless (and a false positive) for them.
                bool shaderUsesRenderModeKeywords = declared.Contains(kAlphaClip) || declared.Contains(kClipRect);

                // The transparent-mode keyword the material needs is not actually live in the shader space.
                bool renderModeBroken = shaderUsesRenderModeKeywords && (
                    (wantAlphaClip && !resolved.Contains(kAlphaClip)) ||
                    (wantClipRect  && !resolved.Contains(kClipRect)));

                if (hasProblem || renderModeBroken) problems++;

                if (!hasProblem && !renderModeBroken) continue; // only print suspects

                sb.AppendLine("--------------------------------------------------------------------");
                sb.AppendLine($"  Material : {m.name}   ({AssetDatabase.GetAssetPath(m)})");
                sb.AppendLine($"  Shader   : {m.shader.name}");
                sb.AppendLine($"  Queue    : {m.renderQueue}   _TransparentMode={tpmode}");
                sb.AppendLine($"  Blend    : _SrcBlend={GetF(m,"_SrcBlend")} _DstBlend={GetF(m,"_DstBlend")} _ZWrite={GetF(m,"_ZWrite")} _Cull={GetF(m,"_Cull")} _Cutoff={GetF(m,"_Cutoff")}");
                sb.AppendLine($"  RenderMode kw: AlphaClip(want={wantAlphaClip},legacyOn={hasAlphaClip},live={resolved.Contains(kAlphaClip)})  ClipRect(want={wantClipRect},legacyOn={hasClipRect},live={resolved.Contains(kClipRect)})");
                sb.AppendLine($"  Declared by shader ({declared.Count}): {string.Join(" ", declared.OrderBy(x => x))}");
                sb.AppendLine($"  Enabled (legacy string): {(legacy.Length == 0 ? "<none>" : string.Join(" ", legacy))}");
                sb.AppendLine($"  Enabled (resolved local): {(resolved.Count == 0 ? "<none>" : string.Join(" ", resolved.OrderBy(x => x)))}");
                if (incompatible.Length > 0)
                    sb.AppendLine($"  >>> INCOMPATIBLE KEYWORD SPACE: {string.Join(" ", incompatible)}");
                if (renderModeBroken)
                    sb.AppendLine($"  >>> RENDER-MODE KEYWORD NOT LIVE -> shader will treat material as OPAQUE/wrong (visible as transparent/invisible).");
              }
              catch (System.Exception e)
              {
                problems++;
                sb.AppendLine($"  [ERROR scanning '{(m == null ? "<null>" : m.name)}']: {e.Message}");
              }
            }

            sb.AppendLine("====================================================================");
            sb.AppendLine(problems == 0
                ? "[DennokoEx][KW] No keyword-space problems detected."
                : $"[DennokoEx][KW] {problems} material(s) with keyword-space problems. See above. Run 'Repair Keyword Space' or lilToon's Refresh Shaders.");

            if (problems == 0) Debug.Log(sb.ToString());
            else Debug.LogWarning(sb.ToString());
        }

        [MenuItem("Window/DennokoEx/Diagnostics/Repair Keyword Space")]
        static void Repair()
        {
            var mats = CollectMaterials();
            int fixedCount = 0;
            foreach (var m in mats)
            {
                if (m == null || m.shader == null) continue;

                var resolved = new HashSet<string>(m.enabledKeywords.Select(k => k.name));
                string[] legacy = m.shaderKeywords ?? new string[0];
                var incompatible = legacy.Where(n => !resolved.Contains(n)).ToArray();
                if (incompatible.Length == 0) continue;

                Undo.RecordObject(m, "DennokoEx Repair Keyword Space");

                // Drop every keyword, then force Unity to re-resolve the space against the current
                // shader, then let lilToon re-apply the correct local keywords from _TransparentMode etc.
                foreach (var n in legacy) m.DisableKeyword(n);
                var sh = m.shader;
                m.shader = sh; // reassignment forces keyword-space re-resolution

                if (lilToon.lilMaterialUtils.CheckShaderIslilToon(m) &&
                    lilToon.lilShaderUtils.IsMultiShaderName(sh.name))
                {
                    lilToon.lilMaterialUtils.SetupMultiMaterial(m);
                }

                EditorUtility.SetDirty(m);
                fixedCount++;
                Debug.Log($"[DennokoEx][KW] Repaired '{m.name}' (removed: {string.Join(" ", incompatible)})");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[DennokoEx][KW] Repair finished. {fixedCount} material(s) changed.");
        }

        // ================================================================================================
        // BUILD-TIME CAPTURE
        // The "incompatible keyword space" warning is transient: it fires while lilToon reimports shaders
        // during the VRChat upload preprocess, then the resting state re-syncs. To catch it we snapshot
        // every avatar material BEFORE the upload, capture log messages during it, then diff AFTER.
        //
        // Flow:
        //   1. Select the avatar root.
        //   2. Window > DennokoEx > Diagnostics > 1. Begin Upload Capture
        //   3. Run the VRChat upload (Build & Publish). Let it finish (or fail).
        //   4. Window > DennokoEx > Diagnostics > 2. End Upload Capture & Report
        // ================================================================================================

        const string kActiveKey   = "DennokoEx.KWCapture.Active";
        const string kSnapKey      = "DennokoEx.KWCapture.SnapPath";
        const string kLogKey       = "DennokoEx.KWCapture.LogPath";

        [System.Serializable]
        class MatSnap
        {
            public string path, name, shader;
            public int tpmode, queue;
            public float src, dst, zwrite;
            public bool shaderSupported;
            public string legacyKw;   // space-joined
            public string resolvedKw; // space-joined
        }

        [System.Serializable]
        class SnapList { public List<MatSnap> items = new List<MatSnap>(); }

        static MatSnap Capture(Material m)
        {
            var s = new MatSnap
            {
                path   = AssetDatabase.GetAssetPath(m),
                name   = m.name,
                shader = m.shader != null ? m.shader.name : "<null>",
                tpmode = m.HasProperty("_TransparentMode") ? Mathf.RoundToInt(m.GetFloat("_TransparentMode")) : -1,
                queue  = m.renderQueue,
                src    = m.HasProperty("_SrcBlend") ? m.GetFloat("_SrcBlend") : -1,
                dst    = m.HasProperty("_DstBlend") ? m.GetFloat("_DstBlend") : -1,
                zwrite = m.HasProperty("_ZWrite")   ? m.GetFloat("_ZWrite")   : -1,
                shaderSupported = m.shader != null && m.shader.isSupported,
                legacyKw   = string.Join(" ", (m.shaderKeywords ?? new string[0]).OrderBy(x => x)),
                resolvedKw = string.Join(" ", m.enabledKeywords.Select(k => k.name).OrderBy(x => x)),
            };
            return s;
        }

        static string KeyOf(MatSnap s) => string.IsNullOrEmpty(s.path) ? "scene:" + s.name : s.path;

        [MenuItem("Window/DennokoEx/Diagnostics/1. Begin Upload Capture")]
        static void BeginCapture()
        {
            var mats = CollectMaterials();
            if (mats.Count == 0)
            {
                Debug.LogWarning("[DennokoEx][KW] Begin Capture: no materials found. Select the avatar root first.");
                return;
            }

            var list = new SnapList();
            foreach (var m in mats) { if (m != null && m.shader != null) list.items.Add(Capture(m)); }

            string snapPath = Path.Combine(Application.temporaryCachePath, "dnkw_kw_snapshot.json");
            File.WriteAllText(snapPath, JsonUtility.ToJson(list));

            string logPath = Path.Combine(Application.temporaryCachePath, "dnkw_kw_log.txt");
            File.WriteAllText(logPath, ""); // reset

            EditorPrefs.SetString(kSnapKey, snapPath);
            EditorPrefs.SetString(kLogKey, logPath);
            EditorPrefs.SetBool(kActiveKey, true);
            Subscribe();

            Debug.LogWarning($"[DennokoEx][KW] CAPTURE STARTED. Snapshotted {list.items.Count} material(s).\n" +
                             $"Now run the VRChat upload. When it finishes (or fails), run '2. End Upload Capture & Report'.");
        }

        [MenuItem("Window/DennokoEx/Diagnostics/2. End Upload Capture & Report")]
        static void EndCapture()
        {
            if (!EditorPrefs.GetBool(kActiveKey, false))
            {
                Debug.LogWarning("[DennokoEx][KW] No capture in progress. Run '1. Begin Upload Capture' first.");
                return;
            }
            Unsubscribe();
            EditorPrefs.SetBool(kActiveKey, false);

            string snapPath = EditorPrefs.GetString(kSnapKey, "");
            string logPath  = EditorPrefs.GetString(kLogKey, "");

            SnapList before = null;
            if (!string.IsNullOrEmpty(snapPath) && File.Exists(snapPath))
                before = JsonUtility.FromJson<SnapList>(File.ReadAllText(snapPath));
            if (before == null) { Debug.LogError("[DennokoEx][KW] Before-snapshot missing; cannot diff."); return; }

            var beforeByKey = before.items.ToDictionary(KeyOf, x => x);

            // Re-capture current state of the SAME materials (resolve by asset path).
            var sb = new StringBuilder();
            sb.AppendLine("[DennokoEx][KW] ===== UPLOAD CAPTURE REPORT =====");

            int changed = 0;
            foreach (var b in before.items)
            {
                Material m = null;
                if (!string.IsNullOrEmpty(b.path)) m = AssetDatabase.LoadAssetAtPath<Material>(b.path);
                if (m == null) continue;
                var a = Capture(m);

                var diffs = new List<string>();
                if (a.shader != b.shader)   diffs.Add($"shader: '{b.shader}' -> '{a.shader}'");
                if (a.tpmode != b.tpmode)   diffs.Add($"_TransparentMode: {b.tpmode} -> {a.tpmode}");
                if (a.queue  != b.queue)    diffs.Add($"queue: {b.queue} -> {a.queue}");
                if (a.src    != b.src)      diffs.Add($"_SrcBlend: {b.src} -> {a.src}");
                if (a.dst    != b.dst)      diffs.Add($"_DstBlend: {b.dst} -> {a.dst}");
                if (a.zwrite != b.zwrite)   diffs.Add($"_ZWrite: {b.zwrite} -> {a.zwrite}");
                if (a.shaderSupported != b.shaderSupported) diffs.Add($"shaderSupported: {b.shaderSupported} -> {a.shaderSupported}");
                if (a.legacyKw   != b.legacyKw)   diffs.Add($"legacyKeywords: [{b.legacyKw}] -> [{a.legacyKw}]");
                if (a.resolvedKw != b.resolvedKw) diffs.Add($"resolvedKeywords: [{b.resolvedKw}] -> [{a.resolvedKw}]");

                if (diffs.Count > 0)
                {
                    changed++;
                    sb.AppendLine("------------------------------------------------------------");
                    sb.AppendLine($"  {b.name}  ({b.path})");
                    foreach (var d in diffs) sb.AppendLine($"    {d}");
                    if (!a.shaderSupported) sb.AppendLine("    >>> SHADER NOT SUPPORTED AFTER UPLOAD (compile failed / variant stripped) -> renders wrong/invisible.");
                }
            }

            sb.AppendLine("============================================================");
            sb.AppendLine(changed == 0
                ? "No per-material state changes detected before vs after upload."
                : $"{changed} material(s) changed state across the upload. See diffs above.");

            // Captured warnings
            int kwWarnings = 0;
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                kwWarnings = lines.Count(l => l.Contains("incompatible keyword space"));
                sb.AppendLine($"Captured log lines: {lines.Length}. 'incompatible keyword space' occurrences: {kwWarnings}.");
            }
            else sb.AppendLine("(No log file captured — domain reload during build may have dropped log capture; the state diff above is still valid.)");

            if (changed == 0 && kwWarnings == 0) Debug.Log(sb.ToString());
            else Debug.LogWarning(sb.ToString());
        }

        // --- log capture (re-subscribes after domain reload while capture is active) ---
        [InitializeOnLoadMethod]
        static void ResubscribeAfterReload()
        {
            if (EditorPrefs.GetBool(kActiveKey, false)) Subscribe();
        }

        static bool _subscribed;
        static void Subscribe()
        {
            if (_subscribed) return;
            Application.logMessageReceivedThreaded += OnLog;
            _subscribed = true;
        }
        static void Unsubscribe()
        {
            if (!_subscribed) return;
            Application.logMessageReceivedThreaded -= OnLog;
            _subscribed = false;
        }
        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition)) return;
            if (!condition.Contains("keyword space") && !condition.Contains("incompatible")) return;
            try
            {
                string logPath = EditorPrefs.GetString(kLogKey, "");
                if (!string.IsNullOrEmpty(logPath))
                    File.AppendAllText(logPath, condition.Replace("\n", " ") + "\n");
            }
            catch { /* best effort */ }
        }

        static string GetF(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("0.##") : "-";

        static List<Material> CollectMaterials()
        {
            var set = new HashSet<Material>();

            foreach (var go in Selection.gameObjects)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in r.sharedMaterials)
                        if (mat != null) set.Add(mat);

            foreach (var obj in Selection.objects)
                if (obj is Material mat) set.Add(mat);

            if (set.Count == 0)
            {
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                            foreach (var mat in r.sharedMaterials)
                                if (mat != null) set.Add(mat);
                }
            }

            return set.ToList();
        }
    }
}
#endif
