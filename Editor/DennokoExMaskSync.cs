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

        static bool _pendingFullSync;
        static bool _pendingSceneSync;
        static double _nextHealTime;
        const double HealInterval = 1.0; // seconds between self-heal passes

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

        static void OnUpdate()
        {
            if (Busy) return;

            // (a) One-shot full rebuild after a domain reload (waits here until the editor is idle).
            if (_pendingFullSync)
            {
                _pendingFullSync = false;
                _pendingSceneSync = false; // full scan already covers everything in scenes
                SyncAll();
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
                if (m == null) { _preview.Remove(m); continue; }
                if (m.GetTexture(DennokoExMaskPacker.PackedProp) != kv.Value)
                    Sync(m); // preview was cleared (e.g. by an upload) -> restore it
            }
        }

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

        // Manual refresh: drop cached state for the material and re-bake unconditionally.
        public static void ForceSync(Material m)
        {
            if (m == null) return;
            if (_preview.TryGetValue(m, out var old) && old != null) Object.DestroyImmediate(old);
            _preview.Remove(m);
            _sig.Remove(m);
            Sync(m);
        }

        public static void Sync(Material m)
        {
            if (Busy) return;
            if (!IsDennokoEx(m) || !m.HasProperty(DennokoExMaskPacker.PackedProp)) return;

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
