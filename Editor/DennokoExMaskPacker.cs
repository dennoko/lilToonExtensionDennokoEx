#if UNITY_EDITOR
using UnityEditor;
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
        //
        // forBuild: the texture will be shipped on the avatar, so it is block-compressed and marked
        // for mipmap streaming. An uncompressed 2048^2 RGBA32 mask costs ~16.8 MB of VRAM per
        // material and counts fully against VRChat's texture budget; BC7 / ASTC brings that to ~1/4
        // with mipmaps included, and streaming keeps the top mips out of VRAM at distance.
        // The editor preview passes false: compression costs seconds per bake and never ships.
        // Mipmaps are always generated — without them the mask aliases/shimmers at distance and the
        // GPU always fetches the full-resolution texture.
        public static Texture2D Bake(Material m, bool forBuild = false)
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
                result = new Texture2D(size, size, TextureFormat.RGBA32, /*mipChain*/ true, /*linear*/ true);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                result.Apply(/*updateMipmaps*/ true, false);
                result.name = m.name + "_DnkwPackedMask";
                if (forBuild) { Compress(result); ConfigureForStreaming(result); }
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(mat);
            }
            return result;
        }

        // Block-compress in place for the build. BC7 is used on PC rather than DXT5 because the four
        // channels hold four UNRELATED masks: DXT5 encodes RGB along a single line per 4x4 block, so
        // independent R/G/B masks bleed into each other, while BC7's partition modes keep them apart.
        // Mobile (Quest) has no BC7, so ASTC 6x6 is used there. If the platform format is unavailable
        // the texture is simply left uncompressed rather than corrupted.
        static void Compress(Texture2D tex)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == BuildTarget.Android || target == BuildTarget.iOS;
            var format = mobile ? TextureFormat.ASTC_6x6 : TextureFormat.BC7;
            // Only the desktop format is checked against the editor's GPU: the mobile format is
            // encoded on the CPU for a device this machine does not have to be able to sample.
            if (!mobile && !SystemInfo.SupportsTextureFormat(format)) return;
            try
            {
                EditorUtility.CompressTexture(tex, format, TextureCompressionQuality.Normal);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DennokoEx] Packed mask compression to {format} failed, shipping it uncompressed. {e.Message}");
            }
        }

        // Marks the baked mask for VRChat's mipmap streaming and drops its CPU-side copy from the
        // build. Both live only in the serialized object: Texture2D.streamingMipmaps is get-only in
        // the scripting API, and a texture baked at build time has no TextureImporter behind it to
        // set them the normal way, so the fields are written the same way NDMF's CheckMipStreamingPass
        // reads them back. Without this the mask is never streamed — it stays resident at full
        // resolution however far away the avatar is — and NDMF reports it as a bug in the tool that
        // generated the texture.
        //
        // m_IsReadable is cleared rather than calling Apply(_, makeNoLongerReadable: true), which
        // would free the system-memory copy before the texture has been serialized into the build.
        static void ConfigureForStreaming(Texture2D tex)
        {
            var so = new SerializedObject(tex);
            var streaming = so.FindProperty("m_StreamingMipmaps");
            // A future Unity could rename these; leave the texture untouched rather than half-set.
            if (streaming == null) return;
            streaming.boolValue = true;

            var priority = so.FindProperty("m_StreamingMipmapsPriority");
            if (priority != null) priority.intValue = 0;

            // The mask is only ever sampled by the GPU, and a Read/Write enabled texture is excluded
            // from streaming besides.
            var readable = so.FindProperty("m_IsReadable");
            if (readable != null) readable.boolValue = false;

            // Without undo: this runs on a throwaway object during the build.
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Texture Get(Material m, string prop)
            => (m.HasProperty(prop)) ? m.GetTexture(prop) : null;
    }
}
#endif
