#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dennokoworks
{
    /// <summary>Build-only packing, independent of editor demand and of the asset database writer.</summary>
    public static class DennokoExMaskBuild
    {
        public static void Pack(GameObject root, Action<Object> saveAsset)
        {
            if (root == null) return;
            var clones = new Dictionary<Material, Material>();
            var packedBySignature = new Dictionary<string, Texture2D>();
            // Disabled clothing can become active in the uploaded avatar. Never apply editor demand
            // filtering here. The supplied root is the build clone, not the authored scene object.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!DennokoExMaskSync.IsDennokoEx(source)) continue;
                    bool needsPacking = DennokoExMaskPacker.NeedsPacking(source);
                    if (!needsPacking && !DennokoExMaskPacker.ShouldClearPackedMask(source)) continue;
                    if (!clones.TryGetValue(source, out var clone))
                    {
                        Texture2D packed = null;
                        if (needsPacking)
                        {
                            string signature = DennokoExMaskPacker.SourceSignature(source);
                            if (!packedBySignature.TryGetValue(signature, out packed))
                            {
                                if (DennokoExMaskPacker.IsPackedMaskUpToDate(source))
                                    packed = source.GetTexture(DennokoExMaskPacker.PackedProp) as Texture2D;
                                else
                                {
                                    packed = DennokoExMaskPacker.Bake(source, forBuild: true);
                                    if (packed == null)
                                        throw new InvalidOperationException(
                                            $"[DennokoEx] Cannot bake masks for '{source.name}'. Refresh its masks before building.");
                                    saveAsset(packed);
                                }
                                packedBySignature.Add(signature, packed);
                            }
                        }

                        clone = new Material(source) { name = source.name + " (DnkwPacked)" };
                        clone.SetTexture(DennokoExMaskPacker.PackedProp, packed);
                        // Only the packed mask is sampled; retain ST values, strip authoring inputs.
                        foreach (var prop in DennokoExMaskPacker.SourceProps)
                            if (clone.HasProperty(prop)) clone.SetTexture(prop, null);
                        saveAsset(clone);
                        clones.Add(source, clone);
                    }
                    materials[i] = clone;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}
#endif
