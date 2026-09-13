#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

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
    // Driving model (loop-proof, self-healing, lazy):
    //   * We NEVER react to AssetPostprocessor.OnPostprocessAllAssets. Doing so caused an endless
    //     ~0.5s reload loop during VRC upload (lilToon calls AssetDatabase.Refresh repeatedly) and
    //     .unitypackage import, because SetTexture re-triggered imports.
    //   * Only materials that are actually visible get baked automatically: renderers that are enabled
    //     and active in hierarchy, in the loaded scenes or the open Prefab Stage. Inactive branches
    //     (e.g. hidden outfits) are not baked until they become active. Baking every scene material at
    //     startup stalled the editor for a long time right after opening a project.
    //   * An EditorApplication.update tick does the work only when the editor is idle:
    //       (a) a scene scan, requested after each domain reload and whenever the scene may have
    //           changed (hierarchy / object changes, scene or Prefab Stage opened, undo/redo). The scan
    //           only enqueues materials that have never been synced, so it stays cheap;
    //       (b) the queue is drained under a per-frame time budget, so many pending bakes never freeze
    //           the editor in a single frame;
    //       (c) a throttled "self-heal" that detects previewed materials whose _CustomMaskPacked was
    //           cleared (this is what happens after an upload / export / shader reimport), applies the
    //           loop guard, and hands the approved restores to the budgeted queue. Restoring them inline
    //           re-baked every previewed material in one frame after each upload.
    //   * A bake that fails is retried with exponential backoff and reported once per failure streak,
    //     so a persistent cause (e.g. the packer shader is missing) neither spams the Console nor looks
    //     like an external import/save loop to the loop guard.
    //   * Everything is suppressed while compiling / importing / building / entering play mode, and
    //     it never reschedules aggressively, so it cannot spin.
    //   * SetTexture on an in-memory material does not change the asset database, so none of this can
    //     trigger further imports.
    //   * Materials opened in the inspector are baked immediately via EnsurePreview(), whether or not
    //     they are used in the scene.
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

        // Initial bakes, input validation, self-heal restores and retries share a deduplicated,
        // budgeted queue.
        static readonly Queue<Material> _bakeQueue = new Queue<Material>();
        static readonly HashSet<Material> _queued = new HashSet<Material>();
        // Cleared previews that self-heal has already passed through the loop guard. Only these may be
        // restored by the queue; other queue entries for a cleared preview are left to self-heal.
        static readonly HashSet<Material> _healApproved = new HashSet<Material>();
        static readonly Dictionary<Material, double> _retryAt = new Dictionary<Material, double>();
        // Consecutive failed bakes per material; drives the retry backoff and log-once behaviour.
        static readonly Dictionary<Material, int> _failCount = new Dictionary<Material, int>();

        static bool _pendingScan;
        static double _nextScanTime;
        static double _nextHealTime;
        static double _nextFallbackScanTime;
        const double FallbackScanInterval = 2.0;
        const double RetryInterval    = 5.0;  // first retry delay after a failed bake; doubles per failure
        const double RetryMaxInterval = 60.0; // backoff cap, so a fixed cause is still picked up within a minute
        const double ScanInterval = 0.25; // min seconds between scans (object-change events fire while dragging)
        const long   BakeBudgetMs = 8;    // per-frame bake budget; at least one material is always processed
        const double HealInterval = 1.0; // seconds between self-heal passes
        const int    HealMaxConsecutive = 4;    // consecutive re-clears before we conclude it's a loop
        const double HealMuteSeconds    = 60.0; // how long to pause auto-restore once a loop is detected

        static DennokoExMaskSync()
        {
            _pendingScan = true; // transient textures are gone after a domain reload; rebuild when idle
            EditorApplication.update += OnUpdate;
            // Anything that can make a DennokoEx material newly visible requests a (coalesced) scan:
            // placing/duplicating a prefab, activating a GameObject, enabling a Renderer, swapping a
            // material, opening a scene or Prefab Stage, undo/redo.
            EditorApplication.hierarchyChanged += RequestScan;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorSceneManager.sceneOpened += (_, __) => RequestScan();
            PrefabStage.prefabStageOpened += _ => RequestScan();
            Undo.undoRedoPerformed += RevalidateTrackedMaterials;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) RevalidateTrackedMaterials();
            };
        }

        static void RequestScan() => _pendingScan = true;
        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            RequestScan();
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var data);
                var m = EditorUtility.InstanceIDToObject(data.instanceId) as Material;
                if (m != null && (_sig.ContainsKey(m) || _preview.ContainsKey(m))) Enqueue(m);
            }
        }

        static void Enqueue(Material m)
        {
            if (m != null && _queued.Add(m)) _bakeQueue.Enqueue(m);
        }

        static void RevalidateTrackedMaterials()
        {
            // Keep signatures: Sync compares inputs, so unrelated Undo and our own texture
            // assignments do not cause another bake. Include the "none" state (no preview).
            foreach (var m in _sig.Keys) Enqueue(m);
            RequestScan();
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

        // True when we had assigned a preview that is no longer on the material (cleared by an upload /
        // export / reimport, or the texture itself was destroyed). Call only after CanPreview(m).
        static bool IsPreviewCleared(Material m)
            => _preview.TryGetValue(m, out var tex)
               && (tex == null || m.GetTexture(DennokoExMaskPacker.PackedProp) != tex);

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
            _healApproved.Remove(m);
            _retryAt.Remove(m);
            _failCount.Remove(m);
        }

        static void OnUpdate()
        {
            if (Busy) return;

            double now = EditorApplication.timeSinceStartup;

            // (a) Scan the visible scene content and enqueue materials that were never synced.
            if ((_pendingScan || now >= _nextFallbackScanTime) && now >= _nextScanTime)
            {
                _pendingScan = false;
                _nextScanTime = now + ScanInterval;
                _nextFallbackScanTime = now + FallbackScanInterval;
                ScanVisibleMaterials();
            }

            if (_retryAt.Count > 0)
            {
                foreach (var kv in _retryAt.ToList())
                {
                    var m = kv.Key;
                    if (!CanPreview(m)) { Forget(m); continue; }
                    if (now < kv.Value) continue;
                    // A cleared preview is retried by self-heal (through the loop guard), not from here;
                    // enqueueing it would only be skipped by DrainQueue again every frame.
                    if (IsPreviewCleared(m) && !_healApproved.Contains(m)) continue;
                    Enqueue(m);
                }
            }

            // (b) Drain the queue under a time budget so a large backlog is spread over many frames.
            if (_bakeQueue.Count > 0)
            {
                DrainQueue();
            }

            // (c) Self-heal: after an upload/export/reimport the material's _CustomMaskPacked gets
            //     reset to null. Detect those, run the loop guard, and queue the approved restores so
            //     they are baked under the frame budget. Only iterates materials we have previewed,
            //     throttled to once per HealInterval.
            if (now < _nextHealTime) return;
            _nextHealTime = now + HealInterval;

            foreach (var kv in _preview.ToList())
            {
                var m = kv.Key;
                // Destroyed material, or its shader is no longer DennokoEx (the user switched the
                // material back to plain lilToon): drop it before touching the packed-mask property.
                if (!CanPreview(m)) { Forget(m); continue; }

                // Preview still assigned -> healthy. Clear any strike history.
                if (!IsPreviewCleared(m)) { _healStrikes.Remove(m); continue; }

                // Already approved and waiting for its budgeted bake: do not count the same clear twice.
                if (_healApproved.Contains(m)) continue;

                if (_retryAt.TryGetValue(m, out var retry) && now < retry) continue;

                // Preview was cleared (upload / export / reimport). Normally restore it — but if it keeps
                // getting cleared right after we restore it, an external import/save loop is in progress;
                // stop feeding it. Paused materials recover after the cooldown or via the manual refresh.
                if (_healMuteUntil.TryGetValue(m, out var until) && now < until)
                    continue;

                int strikes = _healStrikes.TryGetValue(m, out var s) ? s + 1 : 1;
                if (strikes >= HealMaxConsecutive)
                {
                    _healStrikes.Remove(m);
                    _healMuteUntil[m] = now + HealMuteSeconds;
                    Debug.LogWarning(
                        $"[DennokoEx] Mask preview for '{m.name}' kept being cleared by an external asset " +
                        $"import/save loop, so auto-refresh is paused for {HealMuteSeconds:0}s to avoid feeding it. " +
                        "Use the material's manual mask-preview refresh button to restore immediately. " +
                        "(A third-party asset post-processor that re-saves on every import — e.g. SharedTexHub — can cause this.)");
                    continue;
                }
                _healStrikes[m] = strikes;
                _healApproved.Add(m);
                Enqueue(m); // restored by DrainQueue under the frame budget
            }
        }

        static void DrainQueue()
        {
            var sw = Stopwatch.StartNew();
            while (_bakeQueue.Count > 0)
            {
                var m = _bakeQueue.Dequeue();
                _queued.Remove(m);
                if (m == null) continue;
                if (!CanPreview(m)) { Forget(m); continue; }
                bool healApproved = _healApproved.Remove(m);
                // Missing previews are restored only after self-heal approved them, because self-heal
                // counts repeated clears. Object-change/Undo notifications must not restore them ahead
                // of that loop guard.
                if (!healApproved && IsPreviewCleared(m)) continue;
                // Automatic validation must not bypass the import/save loop cooldown.
                if (_healMuteUntil.TryGetValue(m, out var until) && EditorApplication.timeSinceStartup < until)
                {
                    _retryAt[m] = until;
                    continue;
                }

                try { Sync(m); }
                catch (System.Exception e) { Debug.LogException(e); }

                if (sw.ElapsedMilliseconds >= BakeBudgetMs) break;
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

        // Enqueue never-synced DennokoEx materials used by visible renderers (active in hierarchy and
        // enabled) in the loaded scenes and the open Prefab Stage. Already-synced materials are skipped
        // without hashing their textures; input validation is queued by material changes and Undo.
        static void ScanVisibleMaterials()
        {
            try
            {
                var seen = new HashSet<Material>();
                var renderers = new List<Renderer>();
                var stack = new Stack<Transform>();

                void Visit(GameObject root)
                {
                    stack.Push(root.transform);
                    while (stack.Count > 0)
                    {
                        var tr = stack.Pop();
                        // Inactive GameObjects hide their whole subtree: prune it.
                        if (!tr.gameObject.activeSelf) continue;

                        tr.GetComponents(renderers);
                        foreach (var r in renderers)
                        {
                            if (!r.enabled) continue;
                            foreach (var mat in r.sharedMaterials)
                            {
                                if (mat == null || !seen.Add(mat)) continue;
                                if (_sig.ContainsKey(mat) || _queued.Contains(mat)) continue;
                                if (!IsDennokoEx(mat)) continue;
                                Enqueue(mat);
                            }
                        }

                        for (int i = tr.childCount - 1; i >= 0; i--)
                            stack.Push(tr.GetChild(i));
                    }
                }

                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        Visit(root);
                }

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null)
                    Visit(stage.prefabContentsRoot);
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // Manual refresh: drop cached state for the material (this also clears any loop-guard mute
        // and failure backoff) and re-bake unconditionally.
        public static void ForceSync(Material m)
        {
            if (m == null) return;
            if (Busy)
            {
                _sig.Remove(m);
                _healStrikes.Remove(m);
                _healMuteUntil.Remove(m);
                _retryAt.Remove(m);
                _failCount.Remove(m);
                Enqueue(m);
                return;
            }
            Forget(m);
            Sync(m);
        }

        // Bake the preview only if this material is not previewed yet. Cheap enough to call from
        // OnGUI: it never hashes the mask textures unless a bake is actually needed. Covers materials
        // that are not visible in the scene, and a material that was just switched TO DennokoEx.
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
            if (_retryAt.TryGetValue(m, out var retry) && EditorApplication.timeSinceStartup < retry) return;

            // Null results and exceptions both remain retryable, even without another scene event.
            // Do not destroy a usable preview until its replacement has been baked successfully.
            System.Exception error = null;
            try
            {
                if (TrySync(m))
                {
                    _retryAt.Remove(m);
                    _failCount.Remove(m);
                    return;
                }
            }
            catch (System.Exception e) { error = e; }

            int fails = _failCount.TryGetValue(m, out var f) ? f + 1 : 1;
            _failCount[m] = fails;
            double delay = System.Math.Min(RetryInterval * System.Math.Pow(2, fails - 1), RetryMaxInterval);

            // Report once per failure streak. A persistent cause (e.g. the packer shader is missing)
            // would otherwise log again on every retry for every affected material.
            if (fails == 1)
            {
                if (error != null) Debug.LogException(error);
                Debug.LogWarning(
                    $"[DennokoEx] Mask preview bake failed for '{m.name}'. Retrying in the background " +
                    $"(backoff up to {RetryMaxInterval:0}s); further failures are not logged. " +
                    "Use the material's manual mask-preview refresh button to retry immediately.");
            }

            _sig.Remove(m);
            // Nothing was assigned, so this material cannot be feeding an external import/save loop.
            // Without this, a bake that keeps failing while its preview is cleared would reach the loop
            // guard's strike limit and be misreported as such a loop.
            _healStrikes.Remove(m);
            _retryAt[m] = EditorApplication.timeSinceStartup + delay;
        }

        static bool TrySync(Material m)
        {
            string sig = DennokoExMaskPacker.NeedsPacking(m) ? Signature(m) : "none";

            // Already in sync and our preview texture is still assigned -> nothing to do.
            if (_sig.TryGetValue(m, out var prev) && prev == sig
                && _preview.TryGetValue(m, out var cur) && cur != null
                && m.GetTexture(DennokoExMaskPacker.PackedProp) == cur)
                return true;

            if (sig == "none")
            {
                if (_preview.TryGetValue(m, out var old) && old != null) Object.DestroyImmediate(old);
                _preview.Remove(m);
                // No masks assigned -> leave the shader's "white" default.
                if (m.GetTexture(DennokoExMaskPacker.PackedProp) != null)
                    m.SetTexture(DennokoExMaskPacker.PackedProp, null);
                _sig[m] = sig;
                return true;
            }

            var tex = DennokoExMaskPacker.Bake(m);
            if (tex == null) return false;
            tex.hideFlags = HideFlags.HideAndDontSave;
            m.SetTexture(DennokoExMaskPacker.PackedProp, tex);
            if (_preview.TryGetValue(m, out var previous) && previous != null) Object.DestroyImmediate(previous);
            _preview[m] = tex;
            _sig[m] = sig;
            return true;
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
