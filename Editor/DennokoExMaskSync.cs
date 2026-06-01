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
    // real packed texture onto a cloned material. In the editor, nothing would otherwise fill
    // _CustomMaskPacked, so masks would not preview. This class bakes an IN-MEMORY texture
    // (HideAndDontSave) and assigns it for preview only — it never writes to the material on disk.
    //
    // IMPORTANT — why this does NOT react to asset imports:
    // An earlier version re-synced from AssetPostprocessor.OnPostprocessAllAssets. During a VRChat
    // upload (lilToon calls AssetDatabase.Refresh repeatedly) or a .unitypackage import, that fired
    // continuously and SetTexture re-triggered more imports — an endless ~0.5s reload loop. So preview
    // is now driven ONLY by (a) a single scan after each domain reload and (b) inspector edits, and is
    // suppressed entirely while building / importing / compiling / in play mode.
    [InitializeOnLoad]
    public static class DennokoExMaskSync
    {
        static readonly Dictionary<Material, Texture2D> _preview = new Dictionary<Material, Texture2D>();
        static readonly Dictionary<Material, string> _sig = new Dictionary<Material, string>();

        static DennokoExMaskSync()
        {
            // Transient textures are destroyed across domain reloads, so re-bake once on load.
            EditorApplication.delayCall += SyncAll;
        }

        // Never touch materials while the asset pipeline or a build is busy — that is what caused the
        // import feedback loop. Preview will catch up on the next reload or inspector interaction.
        static bool Busy =>
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            BuildPipeline.isBuildingPlayer;

        public static bool IsDennokoEx(Material m)
            => m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx");

        public static void SyncAll()
        {
            if (Busy) return; // do not reschedule — avoids spinning during long imports/builds
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
