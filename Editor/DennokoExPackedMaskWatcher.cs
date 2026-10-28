#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dennokoworks
{
    // Decides WHEN DennokoExPackedMaskStore.EnsureAll runs automatically. Deliberately narrow: asset
    // imports, scene events and generic object changes are NOT watched, because reacting to them made
    // package installs, image imports and ordinary editing slow. Every trigger only queues work; the
    // queue is processed once per editor update outside of imports, compilation and play mode.
    //
    // Automatic triggers:
    //   * Inspector: the drawn material's mask slot references differ from what was last checked
    //     (covers opening, editing, paste, Undo while inspected, switching to DennokoEx).
    //   * Once per project: the loaded scenes are checked after the first load (migration from
    //     versions without persistent packed masks).
    //   * The VRChat avatar build hook calls EnsureAll directly (DennokoExPackedMaskBuildHook).
    // Changes the editor does not see (source image edited or reimported, materials changed outside the
    // DennokoEx inspector) are fixed by the inspector's rebuild button or Window > DennokoEx > Packed
    // Masks > Update All Materials, and at the latest by the build hook.
    [InitializeOnLoad]
    public static class DennokoExPackedMaskWatcher
    {
        // Stored in UserSettings/ so it is per project. Bump to run the scene check again after an update.
        const string MigrationKey = "DennokoEx.PackedMaskMigration";
        const string MigrationVersion = "1";

        static readonly HashSet<Material> _materials = new HashSet<Material>();
        static bool _scanScenes;
        static bool _scheduled;

        // Material instance ID -> hash of its slot texture references when last queued.
        static readonly Dictionary<int, int> _slotState = new Dictionary<int, int>();

        static DennokoExPackedMaskWatcher()
        {
            if (EditorUserSettings.GetConfigValue(MigrationKey) == MigrationVersion) return;
            _scanScenes = true;
            Schedule();
        }

        public static void Request(Material m)
        {
            if (m == null) return;
            _materials.Add(m);
            Schedule();
        }

        // Cheap enough for OnGUI: compares slot references only, queues when they changed.
        public static void RequestIfSlotsChanged(Material m)
        {
            if (!DennokoExMaskPacker.HasPackedSlot(m)) return;
            int state = 17;
            for (int i = 0; i < DennokoExMaskPacker.SourceProps.Length; i++)
            {
                var t = DennokoExMaskPacker.GetSource(m, i);
                state = state * 31 + (t != null ? t.GetInstanceID() : 0);
            }
            int id = m.GetInstanceID();
            if (_slotState.TryGetValue(id, out var prev) && prev == state) return;
            _slotState[id] = state;
            Request(m);
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.update += Process;
        }

        // Play mode is skipped entirely: materials there are mostly temporary clones (NDMF etc.), and the
        // queue survives until play mode ends (exiting does not reload the domain).
        static bool Busy =>
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer ||
            EditorApplication.isPlayingOrWillChangePlaymode;

        static void Process()
        {
            if (Busy) return;
            EditorApplication.update -= Process;
            _scheduled = false;

            var targets = new List<Material>(_materials);
            _materials.Clear();

            if (_scanScenes)
            {
                _scanScenes = false;
                CollectSceneMaterials(targets);
                EditorUserSettings.SetConfigValue(MigrationKey, MigrationVersion);
            }

            if (targets.Count == 0) return;
            try { DennokoExPackedMaskStore.EnsureAll(targets, persist: true, checkAllImportSettings: false); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        static void CollectSceneMaterials(List<Material> targets)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    CollectRendererMaterials(root, targets);
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                CollectRendererMaterials(stage.prefabContentsRoot, targets);
        }

        static void CollectRendererMaterials(GameObject root, List<Material> targets)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (DennokoExMaskPacker.HasPackedSlot(m)) targets.Add(m);
        }
    }
}
#endif
