#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dennokoworks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Runs only in the isolated project prepared by run.ps1. Never imported into the user's project.
public sealed class MaskReference : ScriptableObject { public Texture texture; }

public static class MaskBakeTests
{
    const string F = "Assets/Fixtures/";
    const string Packed = DennokoExMaskPacker.PackedProp;
    static readonly string[] Props = DennokoExMaskPacker.SourceProps;
    static readonly Type SyncType = typeof(DennokoExMaskSync);
    static readonly MethodInfo TickMethod = SyncType.GetMethod("OnUpdate", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly FieldInfo NextScan = SyncType.GetField("_nextScan", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly List<string> Passed = new List<string>();

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Passed.Add(message);
        Debug.Log("PASS: " + message);
    }

    static void StopAutomaticTicks() => EditorApplication.update -=
        (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), TickMethod);

    static void Tick() => TickMethod.Invoke(null, null);
    static void Pump()
    {
        NextScan.SetValue(null, 0d);
        for (int i = 0; i < 200; i++) Tick();
    }

    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(F + name + ".mat");
    static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(F + name + ".png");
    static bool Ready(Material m) => DennokoExMaskPacker.IsPackedMaskUpToDate(m);
    static int PngCount() => Directory.GetFiles("Assets", "*.png", SearchOption.AllDirectories).Length;

    static Texture2D WriteTexture(string name, float value)
    {
        var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
        tex.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1), 256).ToArray());
        tex.Apply();
        File.WriteAllBytes(F + name + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(F + name + ".png", ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(F + name + ".png");
        importer.sRGBTexture = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return Tex(name);
    }

    static Material CreateMaterial(string name, params Texture[] textures)
    {
        var m = new Material(Shader.Find("dennokoworks/DennokoEx/CompatibilityFixture"));
        for (int i = 0; i < textures.Length; i++) m.SetTexture(Props[i], textures[i]);
        AssetDatabase.CreateAsset(m, F + name + ".mat");
        return m;
    }

    static Renderer Render(string name, Material mat, bool active = true, bool enabled = true)
    {
        var go = new GameObject(name);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.enabled = enabled;
        go.SetActive(active);
        return renderer;
    }

    public static void PrepareLegacy()
    {
        StopAutomaticTicks();
        AssetDatabase.CreateFolder("Assets", "Fixtures");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var sources = new Texture[] { WriteTexture("R", .2f), WriteTexture("G", .4f),
            WriteTexture("B", .6f), WriteTexture("A", .8f) };
        var active = CreateMaterial("Active", sources);
        // Same shader GUID will be overwritten between editor sessions, as in a package upgrade.
        active.shader = Shader.Find("dennokoworks/DennokoEx/LegacyFixture");
        Assert(!active.HasProperty(Packed), "legacy shader genuinely has no packed property");
        Render("Active", active);
        Render("Shared", CreateMaterial("Shared", sources));
        Render("Inactive", CreateMaterial("Inactive", Tex("G")), false);
        Render("Disabled", CreateMaterial("Disabled", Tex("B")), true, false);
        CreateMaterial("Unused", Tex("A"));

        var legacyPng = CreateMaterial("LegacyPng", WriteTexture("Legacy", .3f));
        var packed = DennokoExMaskPacker.PackAndSaveMask(legacyPng, recordUndo: false);
        Assert(packed != null, "prepare persistent mask from previous implementation");
        Assert(AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(packed), F + "Legacy_DnkwPackedMask.png") == "",
            "prepare old per-material PNG path with original marker");
        Render("LegacyPng", legacyPng);
        DennokoExMaskPacker.SetMigrated(true);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), F + "Legacy.unity");
        File.WriteAllText("legacy-png-ticks.txt", File.GetLastWriteTimeUtc(F + "Legacy_DnkwPackedMask.png").Ticks.ToString());
        File.WriteAllText("prepare-passed.txt", string.Join("\n", Passed));
    }

    public static void Run()
    {
        StopAutomaticTicks();
        EditorSceneManager.OpenScene(F + "Legacy.unity");
        var active = Mat("Active");
        Assert(active.HasProperty(Packed), "shader overwrite adds packed property without replacing material");
        Assert(active.GetTexture(Props[0]) == Tex("R"), "shader overwrite retains legacy source slots");
        Pump();
        Assert(Ready(active) && active.GetTexture(Packed) != null, "active legacy material automatically migrates despite old migration flag");
        Assert(active.GetTexture(Packed) == Mat("Shared").GetTexture(Packed), "identical inputs share one persistent PNG");
        Assert(Mat("Inactive").GetTexture(Packed) == null, "inactive mesh is not baked on startup");
        Assert(Mat("Disabled").GetTexture(Packed) == null, "disabled renderer is not baked on startup");
        Assert(Mat("Unused").GetTexture(Packed) == null, "unused project material is not loaded for migration");
        Assert(AssetDatabase.GetAssetPath(Mat("LegacyPng").GetTexture(Packed)) == F + "Legacy_DnkwPackedMask.png" &&
            File.GetLastWriteTimeUtc(F + "Legacy_DnkwPackedMask.png").Ticks.ToString() == File.ReadAllText("legacy-png-ticks.txt"),
            "valid old persistent PNG is reused without migration rewrite");

        var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        var readback = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
        try
        {
            Graphics.Blit(active.GetTexture(Packed), rt);
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            var c = readback.GetPixel(8, 8);
            Assert(Mathf.Abs(c.r - .2f) < .06f && Mathf.Abs(c.g - .4f) < .06f &&
                Mathf.Abs(c.b - .6f) < .06f && Mathf.Abs(c.a - .8f) < .06f, "GPU bake preserves all four mask channels");
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(readback); }

        var inactive = Resources.FindObjectsOfTypeAll<Renderer>().Single(r => r.name == "Inactive");
        inactive.gameObject.SetActive(true);
        Pump();
        Assert(Ready(Mat("Inactive")), "activation triggers additional migration");
        GameObject.Find("Disabled").GetComponent<Renderer>().enabled = true;
        Pump();
        Assert(Ready(Mat("Disabled")), "renderer enable triggers additional migration");
        DennokoExMaskSync.EnsurePreview(Mat("Unused"));
        Pump();
        Assert(Ready(Mat("Unused")), "inspector demand migrates an unused material");

        var transient = DennokoExMaskPacker.Bake(active);
        transient.hideFlags = HideFlags.HideAndDontSave;
        active.SetTexture(Packed, transient);
        Pump();
        Assert(Ready(active) && AssetDatabase.Contains(active.GetTexture(Packed)), "old transient preview is replaced by persistent asset");
        Object.DestroyImmediate(transient);

        var oldPacked = active.GetTexture(Packed);
        WriteTexture("R", .9f);
        Pump();
        Assert(Ready(active) && active.GetTexture(Packed) != oldPacked, "source reimport updates active materials without opening inspector");
        Assert(active.GetTexture(Packed) == Mat("Shared").GetTexture(Packed), "reimport still shares the new result");

        var beforeUndo = active.GetTexture(Packed);
        Undo.ClearAll();
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(active, "Change source");
        active.SetTexture(Props[0], Tex("G"));
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        var transform = GameObject.Find("Active").transform;
        Undo.RecordObject(transform, "Move unrelated object");
        transform.position = Vector3.one;
        Undo.FlushUndoRecordObjects();
        DennokoExMaskSync.Sync(active);
        Pump();
        Undo.PerformUndo();
        Assert(transform.position == Vector3.zero && active.GetTexture(Props[0]) == Tex("G"), "deferred bake does not collapse unrelated Undo operation");
        Undo.PerformUndo();
        Pump();
        Assert(active.GetTexture(Props[0]) == Tex("R") && active.GetTexture(Packed) == beforeUndo, "Undo restores source and derived mask consistently");
        Undo.PerformRedo();
        Pump();
        Assert(active.GetTexture(Props[0]) == Tex("G") && Ready(active), "Redo restores matching derived mask");

        foreach (var prop in Props) active.SetTexture(prop, null);
        Pump();
        Assert(active.GetTexture(Packed) == null, "external removal of last source clears obsolete generated mask");

        var one = CreateMaterial("QueuedOne", Tex("A"), Tex("B"));
        var two = CreateMaterial("QueuedTwo", Tex("B"), Tex("A"));
        DennokoExMaskSync.Sync(one);
        DennokoExMaskSync.Sync(two);
        for (int i = 0; i < 30; i++)
        {
            int count = PngCount();
            Tick();
            if (PngCount() - count > 1) throw new Exception("More than one PNG baked per tick");
        }
        Assert(Ready(one) && Ready(two), "multiple queued masks complete with at most one PNG bake per tick");

        var buildSource = CreateMaterial("BuildSource", Tex("A"), Tex("G"));
        var buildShared = CreateMaterial("BuildShared", Tex("A"), Tex("G"));
        var empty = CreateMaterial("BuildEmpty");
        empty.SetTexture(Packed, oldPacked);
        var buildRoot = new GameObject("BuildRoot");
        var buildRenderer = Render("BuildInactive", buildSource, false);
        buildRenderer.transform.SetParent(buildRoot.transform);
        buildRenderer.sharedMaterials = new[] { buildSource, buildShared, empty };
        int beforeBuild = PngCount();
        var saved = new List<Object>();
        DennokoExMaskBuild.Pack(buildRoot, saved.Add);
        var built = buildRenderer.sharedMaterials;
        Assert(built[0] != buildSource && built[0].GetTexture(Packed) != null, "build includes initially inactive renderer");
        Assert(built[0].GetTexture(Packed) == built[1].GetTexture(Packed) && saved.OfType<Texture2D>().Count() == 1,
            "build fallback bakes identical inputs once");
        Assert(built[2].GetTexture(Packed) == null && empty.GetTexture(Packed) == oldPacked,
            "build clears obsolete packed mask only on clone");
        Assert(Props.All(p => built[0].GetTexture(p) == null) && buildSource.GetTexture(Props[0]) != null &&
            buildSource.GetTexture(Packed) == null && PngCount() == beforeBuild, "build leaves authored inputs and PNG assets untouched");
        Object.DestroyImmediate(buildRoot);
        foreach (var obj in saved) Object.DestroyImmediate(obj);

        TestPrefabStage();
        TestDemandCancellation();
        TestCollisionAndManualMask();
        TestCleanup(oldPacked);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Snapshot("before-restart.txt");
        File.WriteAllText("tests-passed.txt", string.Join("\n", Passed));
    }

    static void TestPrefabStage()
    {
        var mat = CreateMaterial("PrefabOnly", Tex("G"), Tex("A"));
        var source = Render("PrefabOnly", mat).gameObject;
        PrefabUtility.SaveAsPrefabAsset(source, F + "Only.prefab");
        Object.DestroyImmediate(source);
        Pump();
        Assert(mat.GetTexture(Packed) == null, "unopened prefab is not baked");
        PrefabStageUtility.OpenPrefab(F + "Only.prefab");
        Pump();
        Assert(Ready(mat), "Prefab Stage creates demand and migrates its material");
        StageUtility.GoToMainStage();
    }

    static void TestCleanup(Texture protectedTexture)
    {
        string source = AssetDatabase.GetAssetPath(protectedTexture);
        Assert(AssetDatabase.CopyAsset(source, F + "Referenced_DnkwPackedMask.png"), "prepare referenced generated copy");
        Assert(AssetDatabase.CopyAsset(source, F + "Orphan_DnkwPackedMask.png"), "prepare orphan generated copy");
        var holder = ScriptableObject.CreateInstance<MaskReference>();
        holder.texture = Tex("Referenced_DnkwPackedMask");
        AssetDatabase.CreateAsset(holder, F + "Reference.asset");
        AssetDatabase.SaveAssets();
        DennokoExMaskPacker.CleanUpUnusedMasks(false);
        Assert(File.Exists(F + "Referenced_DnkwPackedMask.png"), "cleanup preserves ScriptableObject reference");
        Assert(!File.Exists(F + "Orphan_DnkwPackedMask.png"), "cleanup deletes unreferenced generated copy");
        Assert(File.Exists(source), "cleanup preserves masks replaced in current Undo session");
        Assert(File.Exists(F + "R.png"), "cleanup never deletes authored source image");
    }

    static void TestDemandCancellation()
    {
        var cancelled = CreateMaterial("Cancelled", Tex("B"), Tex("G"), Tex("A"));
        var renderer = Render("Cancelled", cancelled);
        var blockers = new List<Material>();
        for (int i = 0; i < 100; i++)
        {
            var blocker = new Material(cancelled) { hideFlags = HideFlags.HideAndDontSave };
            blockers.Add(blocker);
            DennokoExMaskSync.Sync(blocker);
        }
        NextScan.SetValue(null, 0d);
        var pending = (System.Collections.IDictionary)SyncType.GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        for (int i = 0; i < 30 && !pending.Contains(cancelled); i++) Tick();
        Assert(pending.Contains(cancelled), "active renderer is discovered and queued before cancellation");
        renderer.gameObject.SetActive(false);
        Pump();
        Assert(cancelled.GetTexture(Packed) == null, "queued automatic bake is cancelled when renderer becomes inactive");
        foreach (var blocker in blockers) Object.DestroyImmediate(blocker);
        renderer.gameObject.SetActive(true);
        Pump();
        Assert(Ready(cancelled), "cancelled material migrates when needed again");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var added = CreateMaterial("Additive", Tex("B"), Tex("A"), Tex("G"));
        Render("Additive", added);
        Pump();
        Assert(Ready(added), "new additive scene is automatically included in demand");
        EditorSceneManager.CloseScene(scene, true);
    }

    static void TestCollisionAndManualMask()
    {
        var mat = CreateMaterial("Collision", Tex("A"), Tex("R"), Tex("G"));
        var packed = DennokoExMaskPacker.PackAndSaveMask(mat, recordUndo: false);
        string path = AssetDatabase.GetAssetPath(packed);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        string marker = importer.userData;
        importer.userData = "user-authored replacement";
        importer.SaveAndReimport();
        var before = File.ReadAllBytes(path);
        bool reported = false;
        Application.LogCallback handler = (message, stack, type) =>
        {
            if (type == LogType.Error && message.Contains("occupied by a different asset")) reported = true;
        };
        Application.logMessageReceived += handler;
        try
        {
            Assert(DennokoExMaskPacker.PackAndSaveMask(mat, force: true, recordUndo: false) == null,
                "occupied output path refuses overwrite");
        }
        finally { Application.logMessageReceived -= handler; }
        Assert(reported && before.SequenceEqual(File.ReadAllBytes(path)), "collision is reported and replacement image remains intact");
        importer.userData = marker;
        importer.SaveAndReimport();

        var manual = CreateMaterial("Manual");
        manual.SetTexture(Packed, Tex("R"));
        DennokoExMaskPacker.PackAndSaveMask(manual, recordUndo: false);
        Assert(manual.GetTexture(Packed) == Tex("R") && Ready(manual), "manual packed-only material remains supported");
        var copy = CreateMaterial("AssignTarget", Tex("A"), Tex("R"), Tex("G"));
        DennokoExMaskPacker.PackAndSaveMask(mat, new[] { copy }, recordUndo: false);
        Assert(copy.GetTexture(Packed) == packed, "cache hit still assigns requested additional target");
    }

    static void Snapshot(string path) => File.WriteAllLines(path,
        Directory.GetFiles("Assets", "*.png", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => p + "|" + File.GetLastWriteTimeUtc(p).Ticks));

    public static void VerifyRestart()
    {
        StopAutomaticTicks();
        EditorSceneManager.OpenScene(F + "Legacy.unity");
        Pump();
        Snapshot("after-restart.txt");
        Assert(File.ReadAllText("before-restart.txt") == File.ReadAllText("after-restart.txt"),
            "fresh Unity session reuses persisted PNGs without writes or new bakes");
        Assert(Ready(Mat("Shared")) && Ready(Mat("Inactive")), "persisted references remain valid after editor restart");
        File.WriteAllText("restart-passed.txt", string.Join("\n", Passed));
    }
}
#endif
