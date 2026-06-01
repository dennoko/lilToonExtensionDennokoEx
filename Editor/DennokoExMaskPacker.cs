#if UNITY_EDITOR
using UnityEngine;

namespace Dennokoworks
{
    // Packs DennokoEx's four single-channel mask textures into one RGBA texture so the runtime
    // shader declares one texture instead of four (keeping the shader under the 64 texture-parameter
    // limit). Each mask keeps its own UV/tiling because the runtime shader samples the packed texture
    // separately per channel — packing is done 1:1 with no UV transform here.
    //
    // Channel layout (must match custom.hlsl / DennokoEx_MaskPacker.shader):
    //   R = _CustomRefl2ndMaskTex   G = _CustomRim2ndMaskTex
    //   B = _CustomNormal3rdMaskTex  A = _CustomBump1stMaskTex
    public static class DennokoExMaskPacker
    {
        public const string PackedProp = "_CustomMaskPacked";

        // Channel order must match custom.hlsl and DennokoEx_MaskPacker.shader.
        public static readonly string[] SourceProps =
        {
            "_CustomRefl2ndMaskTex",   // R
            "_CustomRim2ndMaskTex",    // G
            "_CustomNormal3rdMaskTex", // B
            "_CustomBump1stMaskTex",   // A
        };

        const string PackerShader = "Hidden/dennokoworks/DennokoEx/MaskPacker";
        const int MaxSize = 2048;
        const int MinSize = 4;

        // Returns true if the material has at least one non-default mask worth packing.
        public static bool NeedsPacking(Material m)
        {
            if (m == null) return false;
            foreach (var p in SourceProps)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return true;
            return false;
        }

        // Bakes a packed RGBA Texture2D from the material's four mask slots. Returns null if there is
        // nothing to pack (all slots empty) — in that case the shader's "white" default is correct.
        public static Texture2D Bake(Material m)
        {
            if (!NeedsPacking(m)) return null;

            var texR = Get(m, SourceProps[0]);
            var texG = Get(m, SourceProps[1]);
            var texB = Get(m, SourceProps[2]);
            var texA = Get(m, SourceProps[3]);

            int size = MinSize;
            foreach (var t in new[] { texR, texG, texB, texA })
                if (t != null) size = Mathf.Max(size, Mathf.Max(t.width, t.height));
            size = Mathf.Clamp(Mathf.NextPowerOfTwo(size), MinSize, MaxSize);

            var shader = Shader.Find(PackerShader);
            if (shader == null)
            {
                Debug.LogError($"[DennokoEx] Mask packer shader '{PackerShader}' not found; cannot pack masks.");
                return null;
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // Null -> Unity binds the shader's "white" default, which is the correct neutral mask value.
            if (texR != null) mat.SetTexture("_TexR", texR);
            if (texG != null) mat.SetTexture("_TexG", texG);
            if (texB != null) mat.SetTexture("_TexB", texB);
            if (texA != null) mat.SetTexture("_TexA", texA);

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prevActive = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(null, rt, mat);
                RenderTexture.active = rt;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false, /*linear*/ true);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                result.Apply(false, false);
                result.name = m.name + "_DnkwPackedMask";
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(mat);
            }
            return result;
        }

        static Texture Get(Material m, string prop)
            => (m.HasProperty(prop)) ? m.GetTexture(prop) : null;
    }
}
#endif
