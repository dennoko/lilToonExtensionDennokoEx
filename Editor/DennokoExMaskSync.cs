#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dennokoworks
{
    // Editor-only, NON-destructive preview of the packed mask.
    //
    // The runtime shader only samples _CustomMaskPacked (the four individual mask slots are no longer
    // sampled, to stay under the 64 texture-parameter limit). At upload the NDMF plugin bakes the
    // real packed texture onto a cloned material. In the editor, nothing would otherwise fill
    // _CustomMaskPacked, so masks would not preview. This class bakes an IN-MEMORY texture
    // (HideAndDontSave) and assigns it for preview only — it never writes to the material on disk.
    //
    // Driving model (loop-proof, self-healing):
    //   * We NEVER react to AssetPostprocessor.OnPostprocessAllAssets. Doing so caused an endless
    //     ~0.5s reload loop during VRC upload (lilToon calls AssetDatabase.Refresh repeatedly) and
    //     .unitypackage import, because SetTexture re-triggered imports.
    //   * Instead an EditorApplication.update tick does the work only when the editor is idle:
    //       (a) a one-shot full project scan after each domain reload, and
    //       (b) a throttled "self-heal" that re-bakes only previewed materials whose _CustomMaskPacked
    //           was cleared (this is what happens after an upload / export / shader reimport).
    //   * Everything is suppressed while compiling / importing / building / entering play mode, and
    //     it never reschedules aggressively, so it cannot spin.
    //   * SetTexture on an in-memory material does not change the asset database, so none of this can
    //     trigger further imports.
    [InitializeOnLoad]
    public static class DennokoExMaskSync
    {
        static readonly Dictionary<Material, Texture2D> _preview = new Dictionary<Material, Texture2D>();
        static readonly Dictionary<Material, string> _sig = new Dictionary<Material, string>();

        // Loop guard. Assigning the preview texture dirties the on-disk material asset; if some other
        // tool then calls AssetDatabase.SaveAssets() (e.g. a third-party asset post-processor that
        // re-saves a database on every import), the material is written, re-imported, our preview is
        // cleared, and self-heal would re-apply it forever — feeding that external import/save loop.
        // We count how many self-heal passes in a row had to re-apply a material's preview; once that
        // exceeds the threshold we stop auto-restoring it for a cooldown so DennokoEx never sustains
        // such a loop. Recovers automatically after the cooldown, on domain reload, or via ForceSync.
        static readonly Dictionary<Material, int> _healStrikes = new Dictionary<Material, int>();
        static readonly Dictionary<Material, double> _healMuteUntil = new Dictionary<Material, double>();

        static bool _pendingFullSync;
        static bool _pendingSceneSync;
        static double _nextHealTime;
        const double HealInterval = 1.0; // seconds between self-heal passes
        const int    HealMaxConsecutive = 4;    // consecutive re-clears before we conclude it's a loop
        const double HealMuteSeconds    = 60.0; // how long to pause auto-restore once a loop is detected

        static DennokoExMaskSync()
        {
            _pendingFullSync = true; // transient textures are gone after a domain reload; rebuild when idle
            EditorApplication.update += OnUpdate;
            // Placing/duplicating a prefab (or any hierarchy edit) can bring in materials whose preview
            // has not been baked yet. Coalesce into a single scene-scoped sync that runs when idle.
            EditorApplication.hierarchyChanged += () => _pendingSceneSync = true;
        }

        // Never touch materials while the asset pipeline or a build is busy — that is what caused the
        // import feedback loop. We simply wait; the update tick retries once things go idle.
        static bool Busy =>
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            BuildPipeline.isBuildingPlayer;

        public static bool IsDennokoEx(Material m)
            => m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx");

        // A material can only be previewed while its current shader actually declares the packed-mask
        // property. This must be checked before ANY Get/SetTexture(_CustomMaskPacked) call: Unity logs
        //   "Material 'X' with Shader 'Y' doesn't have a texture property '_CustomMaskPacked'"
        // for every such call on a foreign shader, which spammed the Console once per self-heal tick
        // after a material was switched back from DennokoEx to plain lilToon.
        static bool CanPreview(Material m)
            => IsDennokoEx(m) && m.HasProperty(DennokoExMaskPacker.PackedProp);

        // Stop tracking a material (destroyed, or its shader was switched away from DennokoEx) and
        // release the in-memory preview texture. Deliberately does NOT write to the material: the
        // property no longer exists there, so clearing it is both impossible and unnecessary — the
        // leftover _CustomMaskPacked entry in the material's saved properties is inert for other
        // shaders and is removed by lilToon's "Remove unused properties".
        static void Forget(Material m)
        {
            if (_preview.TryGetValue(m, out var tex) && tex != null) Object.DestroyImmediate(tex);
            _preview.Remove(m);
            _sig.Remove(m);
            _healStrikes.Remove(m);
            _healMuteUntil.Remove(m);
        }

        static void OnUpdate()
        {
            if (Busy) return;

            // (a) One-shot rebuild after a domain reload (waits here until the editor is idle).
            //     Scene materials only: a project-wide FindAssets("t:Material") + LoadAssetAtPath
            //     stalls the editor for seconds on every recompile in a project with thousands of
            //     materials, and a material that is not in a loaded scene is not being looked at.
            //     Materials opened in the inspector are covered by EnsurePreview() instead.
            if (_pendingFullSync)
            {
                _pendingFullSync = false;
                _pendingSceneSync = false;
                SyncLoadedScenes();
                _nextHealTime = EditorApplication.timeSinceStartup + HealInterval;
                return;
            }

            // Scene changed (e.g. a prefab was placed/duplicated) -> sync just the scene materials.
            if (_pendingSceneSync)
            {
                _pendingSceneSync = false;
                SyncLoadedScenes();
                return;
            }

            // (b) Self-heal: after an upload/export/reimport the material's _CustomMaskPacked gets
            //     reset to null. Re-bake just those whose preview was dropped. Cheap: only iterates
            //     materials we have previewed, throttled to once per HealInterval.
            if (EditorApplication.timeSinceStartup < _nextHealTime) return;
            _nextHealTime = EditorApplication.timeSinceStartup + HealInterval;

            foreach (var kv in _preview.ToList())
            {
                var m = kv.Key;
                // Destroyed material, or its shader is no longer DennokoEx (the user switched the
                // material back to plain lilToon): drop it before touching the packed-mask property.
                if (!CanPreview(m)) { Forget(m); continue; }

                // Preview still assigned -> healthy. Clear any strike history.
                if (m.GetTexture(DennokoExMaskPacker.PackedProp) == kv.Value) { _healStrikes.Remove(m); continue; }

                // Preview was cleared (upload / export / reimport). Normally restore it — but if it keeps
                // getting cleared right after we restore it, an external import/save loop is in progress;
                // stop feeding it. Paused materials recover after the cooldown or via the manual refresh.
                if (_healMuteUntil.TryGetValue(m, out var until) && EditorApplication.timeSinceStartup < until)
                    continue;

                int strikes = _healStrikes.TryGetValue(m, out var s) ? s + 1 : 1;
                if (strikes >= HealMaxConsecutive)
                {
                    _healStrikes.Remove(m);
                    _healMuteUntil[m] = EditorApplication.timeSinceStartup + HealMuteSeconds;
                    Debug.LogWarning(
                        $"[DennokoEx] Mask preview for '{m.name}' kept being cleared by an external asset " +
                        $"import/save loop, so auto-refresh is paused for {HealMuteSeconds:0}s to avoid feeding it. " +
                        "Use the material's manual mask-preview refresh button to restore immediately. " +
                        "(A third-party asset post-processor that re-saves on every import — e.g. SharedTexHub — can cause this.)");
                    continue;
                }
                _healStrikes[m] = strikes;
                Sync(m); // restore preview
            }
        }

        // Project-wide rebuild. Expensive (loads every material in the project), so it is NOT run
        // automatically — kept for manual/explicit use.
        public static void SyncAll()
        {
            if (Busy) return;
            try
            {
                foreach (var g in AssetDatabase.FindAssets("t:Material"))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                    if (IsDennokoEx(m)) Sync(m);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // Sync only materials used by renderers in the currently loaded scenes. Cheap compared to a
        // full project scan, and covers prefabs that were just placed/duplicated into the hierarchy.
        static void SyncLoadedScenes()
        {
            if (Busy) return;
            try
            {
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                            foreach (var mat in r.sharedMaterials)
                                if (IsDennokoEx(mat)) Sync(mat);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // Manual refresh: drop cached state for the material (this also clears any loop-guard mute)
        // and re-bake unconditionally.
        public static void ForceSync(Material m)
        {
            if (m == null) return;
            Forget(m);
            Sync(m);
        }

        // Bake the preview only if this material is not previewed yet. Cheap enough to call from
        // OnGUI: it never hashes the mask textures unless a bake is actually needed. Covers the case
        // where a material was just switched TO DennokoEx, which no other trigger notices.
        public static void EnsurePreview(Material m)
        {
            if (m == null || _preview.ContainsKey(m)) return;
            Sync(m);
        }

        public static void Sync(Material m)
        {
            if (Busy) return;
            if (m == null) return;
            if (!CanPreview(m)) { Forget(m); return; }

            string sig = DennokoExMaskPacker.NeedsPacking(m) ? Signature(m) : "none";

            // Already in sync and our preview texture is still assigned -> nothing to do.
            if (_sig.TryGetValue(m, out var prev) && prev == sig
                && _preview.TryGetValue(m, out var cur) && cur != null
                && m.GetTexture(DennokoExMaskPacker.PackedProp) == cur)
                return;

            if (_preview.TryGetValue(m, out var old) && old != null) Object.DestroyImmediate(old);
            _preview.Remove(m);

            if (sig == "none")
            {
                // No masks assigned -> leave the shader's "white" default.
                if (m.GetTexture(DennokoExMaskPacker.PackedProp) != null)
                    m.SetTexture(DennokoExMaskPacker.PackedProp, null);
                _sig[m] = sig;
                return;
            }

            var tex = DennokoExMaskPacker.Bake(m);
            if (tex != null)
            {
                tex.hideFlags = HideFlags.HideAndDontSave;
                m.SetTexture(DennokoExMaskPacker.PackedProp, tex);
                _preview[m] = tex;
            }
            _sig[m] = sig;
        }

        static string Signature(Material m)
        {
            var sb = new StringBuilder();
            foreach (var p in DennokoExMaskPacker.SourceProps)
            {
                var t = m.HasProperty(p) ? m.GetTexture(p) : null;
                if (t == null) { sb.Append("_;"); continue; }
                sb.Append(t.GetInstanceID()).Append(':');
                try { sb.Append(t.imageContentsHash.ToString()); } catch { }
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
#endif
