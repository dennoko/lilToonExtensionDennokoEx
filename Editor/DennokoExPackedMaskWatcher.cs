#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dennokoworks
{
    // Decides WHEN DennokoExPackedMaskStore.EnsureAll runs, and owns the import settings of the
    // generated files. Every trigger only queues work; the queue is processed once per editor update
    // outside of imports, compilation and play-mode transitions. There is no retry, backoff or loop
    // guard: EnsureAll is idempotent, so a repeated or overly broad trigger is a cheap no-op, and a
    // failure is logged and waits for the next real change (or the manual rebuild button).
    //
    // Triggers:
    //   * Scene content: after a domain reload, when a scene or Prefab Stage is opened, when objects
    //     are created (prefab placement, paste) and when a Renderer changes (material swap). All
    //     renderers count, active or not. This also migrates materials from the old in-memory preview.
    //   * Inspector: whenever the drawn material's mask slot references differ from what was last
    //     checked (covers opening, editing, paste, Undo while inspected, switching to DennokoEx).
    //   * Material property changes published by the editor (edits from other windows, Undo).
    //   * Imported material containers (.mat, .asset, models): every material inside is checked.
    //   * Imported or deleted source textures: saved materials that depend on them, and in-memory
    //     materials (scene-embedded, script-created clones) checked earlier that use them.
    //   * The VRChat avatar build hook calls EnsureAll directly (DennokoExPackedMaskBuildHook).
    [InitializeOnLoad]
    public static class DennokoExPackedMaskWatcher
    {
        static readonly HashSet<Material> _materials = new HashSet<Material>();
        static readonly HashSet<string> _containerPaths = new HashSet<string>();
        static readonly HashSet<string> _texturePaths = new HashSet<string>();
        static bool _texturesDeleted;
        static bool _scanScenes;
        static bool _scheduled;

        // Material instance ID -> hash of its slot texture references when last queued.
        static readonly Dictionary<int, int> _slotState = new Dictionary<int, int>();

        // Materials that are not assets (scene-embedded, created by scripts) and were checked before.
        // AssetDatabase searches cannot find them, so source texture changes are matched against this set.
        static readonly HashSet<Material> _inMemory = new HashSet<Material>();

        static readonly HashSet<string> TextureExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".gif", ".exr", ".hdr",
            ".iff", ".pict", ".asset", ".rendertexture",
        };

        // Files that can hold materials (models embed them as sub-assets).
        static readonly HashSet<string> MaterialContainerExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ".mat", ".asset", ".fbx", ".obj", ".blend", ".dae", ".3ds", ".max", ".ma", ".mb",
        };

        static DennokoExPackedMaskWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorSceneManager.sceneOpened += (_, __) => RequestSceneScan();
            PrefabStage.prefabStageOpened += _ => RequestSceneScan();
            // Domain reload: covers opening the project and the first load after upgrading from the
            // in-memory preview, whose textures were never saved.
            RequestSceneScan();
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

        static void RequestSceneScan()
        {
            _scanScenes = true;
            Schedule();
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.update += Process;
        }

        static bool Busy =>
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer ||
            (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying);

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
            }

            foreach (var path in _containerPaths)
                targets.AddRange(DennokoExPackedMaskStore.LoadMaterialsAtPath(path));
            _containerPaths.Clear();

            if (_texturePaths.Count > 0 || _texturesDeleted)
            {
                CollectAffectedAssetMaterials(targets, _texturePaths, _texturesDeleted);
                CollectAffectedInMemoryMaterials(targets, _texturePaths, _texturesDeleted);
            }
            _texturePaths.Clear();
            _texturesDeleted = false;

            try { DennokoExPackedMaskStore.EnsureAll(targets, persist: true); }
            catch (System.Exception e) { Debug.LogException(e); }

            foreach (var m in targets)
                if (DennokoExMaskPacker.HasPackedSlot(m) && !EditorUtility.IsPersistent(m))
                    _inMemory.Add(m);
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

        // Saved DennokoEx materials that use one of the changed textures. A deleted path no longer shows
        // up as a dependency (neither a source nor a generated mask), so after a deletion every material
        // using a DennokoEx shader is re-checked instead; EnsureAll is a no-op for the unaffected ones.
        static void CollectAffectedAssetMaterials(List<Material> targets, HashSet<string> changed, bool anyDeleted)
        {
            string shaderDir = null;
            if (anyDeleted)
            {
                var shaderPath = AssetDatabase.GetAssetPath(Shader.Find("dennokoworks/DennokoEx"));
                if (!string.IsNullOrEmpty(shaderPath)) shaderDir = Path.GetDirectoryName(shaderPath).Replace('\\', '/') + "/";
            }

            foreach (var path in DennokoExPackedMaskStore.FindMaterialContainerPaths())
            {
                foreach (var dep in AssetDatabase.GetDependencies(path, false))
                {
                    if (changed.Contains(dep) || (shaderDir != null && dep.StartsWith(shaderDir, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        targets.AddRange(DennokoExPackedMaskStore.LoadMaterialsAtPath(path));
                        break;
                    }
                }
            }
        }

        static void CollectAffectedInMemoryMaterials(List<Material> targets, HashSet<string> changed, bool anyDeleted)
        {
            _inMemory.RemoveWhere(m => !DennokoExMaskPacker.HasPackedSlot(m)); // destroyed or shader switched
            foreach (var m in _inMemory)
            {
                // A deleted source turns the slot into a missing reference; just re-check everything.
                bool hit = anyDeleted;
                for (int i = 0; !hit && i < DennokoExMaskPacker.SourceProps.Length; i++)
                {
                    var t = DennokoExMaskPacker.GetSource(m, i);
                    hit = t != null && changed.Contains(AssetDatabase.GetAssetPath(t));
                }
                if (hit) targets.Add(m);
            }
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                    {
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var data);
                        if (EditorUtility.InstanceIDToObject(data.instanceId) is Material m && DennokoExMaskPacker.HasPackedSlot(m))
                            Request(m);
                        break;
                    }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var data);
                        if (EditorUtility.InstanceIDToObject(data.instanceId) is GameObject go)
                            RequestRenderers(go);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        // Only Renderers matter (material swaps); ignore transforms etc. to stay cheap.
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
                        if (EditorUtility.InstanceIDToObject(data.instanceId) is Renderer r)
                            foreach (var m in r.sharedMaterials)
                                if (DennokoExMaskPacker.HasPackedSlot(m)) Request(m);
                        break;
                    }
                }
            }
        }

        static void RequestRenderers(GameObject root)
        {
            var list = new List<Material>();
            CollectRendererMaterials(root, list);
            foreach (var m in list) Request(m);
        }

        class Postprocessor : AssetPostprocessor
        {
            // Bump when ApplyImportSettings changes so existing generated files are reimported.
            public override uint GetVersion() => 1;

            void OnPreprocessTexture()
            {
                if (DennokoExPackedMaskStore.IsGeneratedPath(assetPath))
                    ApplyImportSettings((TextureImporter)assetImporter);
            }

            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                bool queued = false;
                foreach (var path in imported)
                {
                    if (DennokoExPackedMaskStore.IsGeneratedPath(path)) continue;
                    string ext = Path.GetExtension(path);
                    // .asset can be either, so it is checked both ways.
                    if (MaterialContainerExtensions.Contains(ext))
                    {
                        _containerPaths.Add(path);
                        queued = true;
                    }
                    if (TextureExtensions.Contains(ext))
                    {
                        _texturePaths.Add(path);
                        queued = true;
                    }
                }
                // Moves keep the GUID and contents, so they do not change any packed mask.
                foreach (var path in deleted)
                {
                    if (DennokoExPackedMaskStore.IsGeneratedPath(path)
                        || TextureExtensions.Contains(Path.GetExtension(path)))
                    {
                        _texturesDeleted = true;
                        queued = true;
                    }
                }
                if (queued) Schedule();
            }
        }

        // The packed channels are four unrelated linear masks:
        //   * sRGB off and alpha taken as-is, so values match what the individual slots produced.
        //   * BC7 on PC rather than DXT5: DXT5 fits RGB to one line per 4x4 block, bleeding the
        //     independent R/G/B masks into each other. ASTC on mobile, which has no BC7.
        //   * Mipmaps + streaming for VRChat's texture memory budget; no CPU copy.
        static void ApplyImportSettings(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = true;
            ti.isReadable = false;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            SetPlatformFormat(ti, "Standalone", TextureImporterFormat.BC7);
            SetPlatformFormat(ti, "Android", TextureImporterFormat.ASTC_6x6);
            SetPlatformFormat(ti, "iPhone", TextureImporterFormat.ASTC_6x6);
        }

        static void SetPlatformFormat(TextureImporter ti, string platform, TextureImporterFormat format)
        {
            var s = ti.GetPlatformTextureSettings(platform);
            s.overridden = true;
            s.format = format;
            s.maxTextureSize = 2048;
            s.compressionQuality = (int)TextureCompressionQuality.Normal;
            ti.SetPlatformTextureSettings(s);
        }
    }
}
#endif
