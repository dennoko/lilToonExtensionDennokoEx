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
                            var packed = DennokoExMaskPacker.Bake(m);
                            if (packed == null) { clones[m] = null; continue; }

                            clone = new Material(m) { name = m.name + " (DnkwPacked)" };
                            clone.SetTexture(DennokoExMaskPacker.PackedProp, packed);

                            ctx.AssetSaver.SaveAsset(packed);
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
