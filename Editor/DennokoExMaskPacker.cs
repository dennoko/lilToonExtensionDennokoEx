#if UNITY_EDITOR
using UnityEngine;

namespace Dennokoworks
{
    // Packs DennokoEx's four single-channel mask textures into one RGBA image so the runtime
    // shader declares one texture instead of four (keeping the shader under the 64 texture-parameter
    // limit). Each mask keeps its own UV/tiling because the runtime shader samples the packed texture
    // separately per channel — packing is done 1:1 with no UV transform here.
    //
    // This class only produces pixels. Persisting them (PNG asset, import settings, assignment to the
    // material) is DennokoExPackedMaskStore's job.
    //
    // Channel layout (must match custom.hlsl / DennokoEx_MaskPacker.shader):
    //   R = _CustomRefl2ndMaskTex   G = _CustomRim2ndMaskTex
    //   B = _CustomNormal3rdMaskTex  A = _CustomMain4thMaskTex
    public static class DennokoExMaskPacker
    {
        public const string PackedProp = "_CustomMaskPacked";

        // Channel order must match custom.hlsl and DennokoEx_MaskPacker.shader.
        public static readonly string[] SourceProps =
        {
            "_CustomRefl2ndMaskTex",   // R
            "_CustomRim2ndMaskTex",    // G
            "_CustomNormal3rdMaskTex", // B
            "_CustomMain4thMaskTex",   // A
        };

        // Bump when the produced pixels change for identical inputs (shader, size rule, encoding),
        // so previously generated files are no longer considered up to date.
        public const int Version = 1;

        const string PackerShader = "Hidden/dennokoworks/DennokoEx/MaskPacker";
        const int MaxSize = 2048;
        const int MinSize = 4;

        public static bool IsDennokoEx(Material m)
            => m != null && m.shader != null && m.shader.name.Contains("dennokoworks/DennokoEx");

        // The material's current shader declares the packed slot. Must be checked before any
        // Get/SetTexture(PackedProp): Unity logs an error per call on shaders without the property.
        public static bool HasPackedSlot(Material m)
            => IsDennokoEx(m) && m.HasProperty(PackedProp);

        public static Texture GetSource(Material m, int channel)
            => m.HasProperty(SourceProps[channel]) ? m.GetTexture(SourceProps[channel]) : null;

        // True if the material has at least one mask worth packing. With none, the packed slot stays
        // empty and the shader's "white" default is the correct neutral mask.
        public static bool NeedsPacking(Material m)
        {
            if (m == null) return false;
            for (int i = 0; i < SourceProps.Length; i++)
                if (GetSource(m, i) != null) return true;
            return false;
        }

        // Bakes the four mask slots into a linear RGBA32 image and returns it PNG-encoded, or null on
        // failure (reason logged). Mipmaps, compression and streaming are applied by the importer.
        public static byte[] BakePng(Material m)
        {
            if (!NeedsPacking(m)) return null;

            var shader = Shader.Find(PackerShader);
            if (shader == null)
            {
                Debug.LogError($"[DennokoEx] Mask packer shader '{PackerShader}' not found; cannot pack masks.");
                return null;
            }

            int size = MinSize;
            for (int i = 0; i < SourceProps.Length; i++)
            {
                var t = GetSource(m, i);
                if (t != null) size = Mathf.Max(size, Mathf.Max(t.width, t.height));
            }
            size = Mathf.Clamp(Mathf.NextPowerOfTwo(size), MinSize, MaxSize);

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            // Unset -> Unity binds the shader's "white" default, the correct neutral mask value.
            string[] targets = { "_TexR", "_TexG", "_TexB", "_TexA" };
            for (int i = 0; i < targets.Length; i++)
            {
                var t = GetSource(m, i);
                if (t != null) mat.SetTexture(targets[i], t);
            }

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                Graphics.Blit(null, rt, mat);
                RenderTexture.active = rt;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, /*mipChain*/ false, /*linear*/ true);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                tex.Apply(false, false);
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(mat);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }
    }
}
#endif
