#if UNITY_EDITOR && DENNOKOEX_HAS_NDMF
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using Dennokoworks;

[assembly: ExportsPlugin(typeof(DennokoExPackMasksPlugin))]

namespace Dennokoworks
{
    // Build-time, non-destructive mask packing.
    // For every DennokoEx material on the avatar that uses individual mask textures, bakes those
    // masks into a single RGBA texture (_CustomMaskPacked) and assigns it on a cloned material.
    // This must run BEFORE lilToon's optimization (VRChat preprocess, callbackOrder 100); NDMF's
    // Transforming phase runs well ahead of that.
    public class DennokoExPackMasksPlugin : Plugin<DennokoExPackMasksPlugin>
    {
        public override string QualifiedName => "dennokoworks.dennokoex.packmasks";
        public override string DisplayName   => "DennokoEx Mask Packer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).Run("Pack DennokoEx masks", ctx =>
            {
                var root = ctx.AvatarRootObject;
                if (root == null) return;

                // Cache clones so a material shared by several renderers is packed only once.
                var clones = new Dictionary<Material, Material>();

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool dirty = false;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || m.shader == null) continue;
                        if (!m.shader.name.Contains("dennokoworks/DennokoEx")) continue;
                        if (!DennokoExMaskPacker.NeedsPacking(m)) continue;

                        if (!clones.TryGetValue(m, out var clone))
                        {
                            // Normally the editor has already written a packed PNG asset and the material
                            // points at it, so the build just ships that file - no bake, no upload delay.
                            //
                            // It is only trusted when the packer's recorded signature still matches the
                            // material's mask slots. The slots are ordinary user-facing properties: another
                            // tool, a script, or a material copied around can change them without the
                            // DennokoEx inspector ever running, which would leave the PNG stale. Since the
                            // slots are stripped just below, shipping a stale mask would be unrecoverable,
                            // so anything not provably current is re-baked here.
                            Texture2D packed = null;
                            if (!DennokoExMaskPacker.IsPackedMaskUpToDate(m))
                            {
                                // forBuild: mipmapped + block compressed, this one ships on the avatar.
                                packed = DennokoExMaskPacker.Bake(m, forBuild: true);
                                if (packed == null)
                                {
                                    // Packing is impossible (the packer shader is missing). Leave the
                                    // material untouched rather than stripping the masks it still needs.
                                    clones[m] = null;
                                    continue;
                                }
                            }

                            clone = new Material(m) { name = m.name + " (DnkwPacked)" };
                            if (packed != null)
                            {
                                clone.SetTexture(DennokoExMaskPacker.PackedProp, packed);
                                ctx.AssetSaver.SaveAsset(packed);
                            }

                            // The four slot textures are never sampled by the shader - only _CustomMaskPacked
                            // is - but Unity still pulls them into the build because the material references
                            // them. Dropping them here saves both build size and VRAM. Their _ST tiling stays
                            // on the material, which is what the shader actually reads.
                            foreach (var p in DennokoExMaskPacker.SourceProps)
                            {
                                if (clone.HasProperty(p)) clone.SetTexture(p, null);
                            }

                            ctx.AssetSaver.SaveAsset(clone);
                            clones[m] = clone;
                        }

                        if (clone != null) { mats[i] = clone; dirty = true; }
                    }

                    if (dirty) r.sharedMaterials = mats;
                }
            });
        }
    }
}
#endif
