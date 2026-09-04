#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks
{
    // Keeps every DennokoEx material's _CustomMaskPacked pointing at an up-to-date packed PNG asset.
    //
    // The runtime shader only samples _CustomMaskPacked; the four individual mask slots are kept in the
    // material purely so the packer can rebuild it later, and are stripped at build time.
    //
    // Previously this class baked an in-memory (HideAndDontSave) texture, rebuilt every material in the
    // loaded scenes after each domain reload, and ran a 1-second self-heal loop to re-apply previews that
    // an upload/export had cleared. That is all gone: the packed mask is now a real asset, so it survives
    // reloads, uploads and exports on its own, and startup does no work at all.
    //
    // What is left is the migration and scheduling layer:
    //   * One-shot silent migration the first time a project opens with this version, so materials
    //     authored under the old in-memory model light up without the user doing anything.
    //   * A deferred, coalesced bake queue. Packing imports an asset, which must never happen inside
    //     OnGUI - the reimport tears the inspector down and rebuilds it mid-layout - so requests made
    //     while drawing are flushed on the next editor tick, and only once the editor is idle.
    //   * Sync/ForceSync/EnsurePreview/SyncAll keep their original names and semantics; only the
    //     mechanism behind them changed.
    [InitializeOnLoad]
    public static class DennokoExMaskSync
    {
        static DennokoExMaskSync()
        {
            if (!DennokoExMaskPacker.IsMigrated())
                EditorApplication.update += CheckAndRunMigration;
        }

        // Never touch materials while the asset pipeline or a build is busy - reimporting from under
        // either is what caused the old import feedback loop. We simply wait and retry when idle.
        static bool Busy =>
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            BuildPipeline.isBuildingPlayer;

        static void CheckAndRunMigration()
        {
            if (Busy) return;
            EditorApplication.update -= CheckAndRunMigration;

            if (!DennokoExMaskPacker.IsMigrated())
                DennokoExMaskPacker.BatchMigrateAll(false);
        }

        public static bool IsDennokoEx(Material m)
            => m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx");

        // ====================================================================
        //  Public API (unchanged signatures; deferred + signature-guarded now)
        // ====================================================================

        /// <summary>
        /// Queue a re-pack for this material. A no-op when the packed asset already matches the
        /// material's mask slots, so it is safe to call from any change check.
        /// </summary>
        public static void Sync(Material m)
        {
            if (m == null || !IsDennokoEx(m)) return;
            Queue(m, force: false);
        }

        /// <summary>
        /// Re-pack unconditionally, and clear any recorded failure so a material that previously could
        /// not be written is retried. Backs the inspector's manual refresh button.
        /// </summary>
        public static void ForceSync(Material m)
        {
            if (m == null || !IsDennokoEx(m)) return;
            _failed.Remove(m);
            Queue(m, force: true);
        }

        /// <summary>
        /// Called while drawing the inspector: covers a material that was just switched TO DennokoEx,
        /// which no other trigger notices. Cheap - the packer's signature check does the real work, and
        /// a material whose pack failed is not retried on every repaint.
        /// </summary>
        public static void EnsurePreview(Material m)
        {
            if (m == null || !IsDennokoEx(m)) return;
            if (_failed.Contains(m)) return;

            // Deliberately the cheap check, not IsPackedMaskUpToDate: this runs on every repaint, and
            // the full signature comparison loads the texture importer and hashes four source assets.
            // Detecting a missing packed mask is all that is needed here - a stale one is caught by the
            // inspector's change check, and by the build pass as a last resort.
            if (!DennokoExMaskPacker.NeedsPacking(m)) return;
            var cur = m.GetTexture(DennokoExMaskPacker.PackedProp);
            if (cur != null && AssetDatabase.Contains(cur)) return;

            Queue(m, force: false);
        }

        /// <summary>Project-wide re-pack. Expensive; only reached from the menu item.</summary>
        public static void SyncAll()
        {
            _failed.Clear();
            DennokoExMaskPacker.BatchMigrateAll(true);
        }

        [MenuItem("Window/DennokoEx/Re-pack All Masks in Project")]
        static void MenuRepackAll()
        {
            _failed.Clear();
            int count = DennokoExMaskPacker.BatchMigrateAll(true);
            EditorUtility.DisplayDialog("DennokoEx", $"Re-packed masks for {count} material(s).", "OK");
        }

        // ====================================================================
        //  Deferred bake queue
        // ====================================================================

        static readonly HashSet<Material> _pending = new HashSet<Material>();
        static readonly HashSet<Material> _forced  = new HashSet<Material>();
        // Materials whose pack failed (unwritable path, missing packer shader, IO error). Without this,
        // EnsurePreview would retry - and log - on every single inspector repaint. Cleared by ForceSync
        // and by a domain reload.
        static readonly HashSet<Material> _failed  = new HashSet<Material>();
        static bool _queued;
        static int  _undoGroup = -1;

        static void Queue(Material m, bool force)
        {
            _pending.Add(m);
            if (force) _forced.Add(m);

            // Captured before the flush so the property change that triggered it and the resulting
            // asset write collapse into a single undo step.
            int group = Undo.GetCurrentGroup();
            _undoGroup = _undoGroup < 0 ? group : Mathf.Min(_undoGroup, group);

            if (_queued) return;
            _queued = true;
            EditorApplication.delayCall += Flush;
        }

        static void Flush()
        {
            // Busy: try again on the next tick rather than dropping the request or forcing an import
            // through the middle of a compile/build.
            if (Busy) { EditorApplication.delayCall += Flush; return; }

            _queued = false;
            var mats  = new List<Material>(_pending);
            var force = new HashSet<Material>(_forced);
            int group = _undoGroup;
            _pending.Clear();
            _forced.Clear();
            _undoGroup = -1;

            try
            {
                foreach (var m in mats)
                {
                    if (m == null || !IsDennokoEx(m)) continue;
                    bool f = force.Contains(m);
                    if (DennokoExMaskPacker.PackAndSaveMask(m, null, f) == null
                        && DennokoExMaskPacker.NeedsPacking(m))
                    {
                        _failed.Add(m);
                    }
                    else
                    {
                        _failed.Remove(m);
                    }
                }
            }
            finally
            {
                if (group >= 0)
                {
                    Undo.SetCurrentGroupName("DennokoEx Mask");
                    Undo.CollapseUndoOperations(group);
                }
            }
        }
    }
}
#endif
