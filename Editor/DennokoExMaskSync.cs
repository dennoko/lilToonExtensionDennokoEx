#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks
{
    // Editor-only, NON-destructive preview of the packed mask.
    //
    // The runtime shader only samples _CustomMaskPacked (the four individual mask slots are no longer
    // sampled, to stay under the 64 texture-parameter limit). At upload the NDMF plugin bakes the
    // real packed texture onto a cloned material. In the editor, however, nothing would fill
    // _CustomMaskPacked, so masks would not preview. This class bakes an IN-MEMORY packed texture
    // (HideAndDontSave) and assigns it for preview only — it never writes to the material on disk,
    // so .mat files (and git) stay clean. Re-bakes automatically on edit, asset import and reload.
    [InitializeOnLoad]
    public static class DennokoExMaskSync
    {
        static readonly Dictionary<Material, Texture2D> _preview = new Dictionary<Material, Texture2D>();
        static readonly Dictionary<Material, string> _sig = new Dictionary<Material, string>();

        static DennokoExMaskSync()
        {
            // Transient textures are destroyed across domain reloads, so re-bake everything on load.
            EditorApplication.delayCall += SyncAll;
        }

        public static bool IsDennokoEx(Material m)
            => m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx");

        public static void SyncAll()
        {
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

        public static void Sync(Material m)
        {
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

        // Re-sync when a material or any texture-like asset is (re)imported.
        class Postprocessor : AssetPostprocessor
        {
            static readonly string[] Exts = { ".mat", ".png", ".tga", ".psd", ".jpg", ".jpeg", ".exr", ".tif", ".tiff", ".gif", ".bmp" };
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var p in imported)
                    foreach (var e in Exts)
                        if (p.EndsWith(e, System.StringComparison.OrdinalIgnoreCase))
                        {
                            EditorApplication.delayCall += SyncAll;
                            return;
                        }
            }
        }
    }
}
#endif
