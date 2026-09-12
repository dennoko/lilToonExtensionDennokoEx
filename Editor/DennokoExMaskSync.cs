#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dennokoworks
{
    // Migration is per material, not an EditorPrefs flag. Old transient previews, missing PNGs
    // and stale persistent masks are repaired whenever their material is needed.
    [InitializeOnLoad]
    public static class DennokoExMaskSync
    {
        const double ScanInterval = 1.0;
        const double InspectorLease = 2.0;
        const double ScanBudgetSeconds = 0.002;
        const int ScanStepsPerTick = 64;

        sealed class Request
        {
            public readonly HashSet<Renderer> Renderers = new HashSet<Renderer>();
            public double InspectedUntil;
            public bool Explicit;
            public bool Force;
        }

        static readonly Queue<Material> _queue = new Queue<Material>();
        static readonly Dictionary<Material, Request> _pending = new Dictionary<Material, Request>();
        static readonly Dictionary<Material, double> _inspected = new Dictionary<Material, double>();
        // Failed inputs retry after an external import, a different input or a manual refresh.
        static readonly Dictionary<Material, string> _failed = new Dictionary<Material, string>();
        static IEnumerator<KeyValuePair<Material, Renderer>> _scan;
        static double _nextScan;

        static DennokoExMaskSync()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.hierarchyChanged += RequestScan;
            EditorSceneManager.sceneOpened += (_, __) => RequestScan();
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.playModeStateChanged += _ => Invalidate();
        }

        internal static bool Busy =>
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer;

        public static bool IsDennokoEx(Material m) =>
            m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx") &&
            m.HasProperty(DennokoExMaskPacker.PackedProp);

        static void RequestScan() => _nextScan = 0;

        internal static void Invalidate()
        {
            _failed.Clear();
            RequestScan();
            foreach (var pair in _inspected)
                if (pair.Key != null && pair.Value >= EditorApplication.timeSinceStartup)
                    Enqueue(pair.Key).InspectedUntil = pair.Value;
        }

        /// <summary>Deferred consistency check after an explicit material edit.</summary>
        public static void Sync(Material m)
        {
            if (IsDennokoEx(m)) Enqueue(m).Explicit = true;
        }

        public static void ForceSync(Material m)
        {
            if (!IsDennokoEx(m)) return;
            _failed.Remove(m);
            var request = Enqueue(m);
            request.Explicit = request.Force = true;
        }

        // Repaints only renew interest. Signature/importer checks happen off the GUI once per sweep.
        public static void EnsurePreview(Material m)
        {
            if (!IsDennokoEx(m)) return;
            double now = EditorApplication.timeSinceStartup;
            bool newlyInspected = !_inspected.TryGetValue(m, out var until) || until < now;
            _inspected[m] = now + InspectorLease;
            if (newlyInspected) Enqueue(m).InspectedUntil = now + InspectorLease;
        }

        /// <summary>Explicit, synchronous project-wide repair; never called during startup.</summary>
        public static void SyncAll()
        {
            if (Busy) return;
            _failed.Clear();
            DennokoExMaskPacker.BatchMigrateAll(true);
        }

        [MenuItem("Window/DennokoEx/Re-pack All Masks in Project")]
        static void MenuRepackAll() => SyncAll();

        [MenuItem("Window/DennokoEx/Clean Up Unused Masks")]
        static void MenuCleanUpMasks()
        {
            if (!Busy) DennokoExMaskPacker.CleanUpUnusedMasks();
        }

        static Request Enqueue(Material m)
        {
            if (!_pending.TryGetValue(m, out var request))
            {
                request = new Request();
                _pending.Add(m, request);
                _queue.Enqueue(m);
            }
            return request;
        }

        static void OnUpdate()
        {
            if (Busy) return;
            double now = EditorApplication.timeSinceStartup;
            if (_scan == null && now >= _nextScan)
            {
                _scan = EnumerateDemand().GetEnumerator();
                _nextScan = now + ScanInterval;
                foreach (var pair in new List<KeyValuePair<Material, double>>(_inspected))
                {
                    if (pair.Key == null || pair.Value < now) _inspected.Remove(pair.Key);
                    else Enqueue(pair.Key).InspectedUntil = pair.Value;
                }
                foreach (var m in new List<Material>(_failed.Keys))
                    if (m == null) _failed.Remove(m);
            }

            // Traverse incrementally too. Inactive subtrees are pruned.
            for (int i = 0; _scan != null && i < ScanStepsPerTick; i++)
            {
                if (!_scan.MoveNext())
                {
                    _scan.Dispose();
                    _scan = null;
                    break;
                }
                var pair = _scan.Current;
                if (IsDennokoEx(pair.Key)) Enqueue(pair.Key).Renderers.Add(pair.Value);
                if (EditorApplication.timeSinceStartup - now >= ScanBudgetSeconds) break;
            }

            // At most ONE material (and hence one PNG bake/import) per editor update.
            // Identical source sets reuse the persistent texture on subsequent ticks.
            if (_queue.Count == 0) return;
            var material = _queue.Dequeue();
            var pending = _pending[material];
            _pending.Remove(material);
            if (!IsDennokoEx(material) || !StillNeeded(material, pending)) return;
            if (!DennokoExMaskPacker.CanPersistMaterial(material)) return;

            string signature = DennokoExMaskPacker.SourceSignature(material);
            if (!pending.Force && _failed.TryGetValue(material, out var failed) && failed == signature) return;
            try
            {
                // Derived assignments never collapse unrelated Undo groups. Undo restores source
                // fields and undoRedoPerformed schedules their derived state again.
                DennokoExMaskPacker.PackAndSaveMask(material, force: pending.Force, recordUndo: false);
                if (!DennokoExMaskPacker.IsPackedMaskUpToDate(material)) _failed[material] = signature;
                else _failed.Remove(material);
            }
            catch (System.Exception e)
            {
                _failed[material] = signature;
                Debug.LogException(e);
            }
        }

        static bool StillNeeded(Material m, Request request)
        {
            if (request.Explicit || request.InspectedUntil >= EditorApplication.timeSinceStartup) return true;
            foreach (var r in request.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                foreach (var assigned in r.sharedMaterials)
                    if (assigned == m) return true;
            }
            return false;
        }

        static IEnumerable<KeyValuePair<Material, Renderer>> EnumerateDemand()
        {
            var roots = new List<GameObject>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) roots.Add(stage.prefabContentsRoot);
            var visited = new HashSet<Transform>();
            var stack = new Stack<Transform>();
            foreach (var root in roots) if (root != null) stack.Push(root.transform);
            while (stack.Count != 0)
            {
                var t = stack.Pop();
                // Yield for non-rendering objects as well, to bound traversal work per update.
                yield return default;
                if (t == null || !visited.Add(t) || !t.gameObject.activeInHierarchy) continue;
                foreach (var r in t.GetComponents<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    foreach (var m in r.sharedMaterials)
                        if (IsDennokoEx(m)) yield return new KeyValuePair<Material, Renderer>(m, r);
                }
                // A scene can be closed or an object deleted between the yields above.
                if (t == null) continue;
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }
        }
    }

    // Import callbacks only invalidate; they never write assets or bake.
    sealed class DennokoExMaskInvalidation : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var paths in new[] { imported, deleted, moved, movedFrom })
                foreach (var path in paths)
                    if (!DennokoExMaskPacker.IsGeneratedMaskPath(path))
                    {
                        DennokoExMaskSync.Invalidate();
                        return;
                    }
        }
    }
}
#endif
