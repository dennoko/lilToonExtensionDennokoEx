#if UNITY_EDITOR && DENNOKOEX_HAS_VRCSDK_AVATARS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Dennokoworks
{
    // Final consistency check before an avatar is uploaded. Normally every material already references
    // its packed mask from editing, and this is a no-op. It matters when the inputs changed without the
    // editor noticing, or when build tools (NDMF / Modular Avatar / TexTransTool...) created or edited
    // materials: those clones are packed here, in place, with the same store as the editor.
    //
    // Materials are collected from renderers AND animation clips, so materials that are only swapped
    // in by animation are covered too. A material that cannot be packed aborts the upload instead of
    // shipping white or stale masks.
    public class DennokoExPackedMaskBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        // After NDMF (-11000, optimizing at -1025) and Modular Avatar, before lilToon (100), which
        // enables every shader feature and so relies on the masks already being packed.
        public int callbackOrder => 0;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var materials = new HashSet<Material>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) materials.Add(m);

            foreach (var clip in CollectClips(avatar))
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                        if (key.value is Material m) materials.Add(m);

            if (DennokoExPackedMaskStore.EnsureAll(materials, persist: false)) return true;
            Debug.LogError("[DennokoEx] Upload stopped: some DennokoEx masks could not be packed (see the errors above).");
            return false;
        }

        static IEnumerable<AnimationClip> CollectClips(GameObject avatar)
        {
            var clips = new HashSet<AnimationClip>();
            void Add(RuntimeAnimatorController c)
            {
                if (c == null) return;
                foreach (var clip in c.animationClips)
                    if (clip != null) clips.Add(clip);
            }

            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
                Add(animator.runtimeAnimatorController);

            foreach (var descriptor in avatar.GetComponentsInChildren<VRCAvatarDescriptor>(true))
            {
                foreach (var layer in descriptor.baseAnimationLayers) Add(layer.animatorController);
                foreach (var layer in descriptor.specialAnimationLayers) Add(layer.animatorController);
            }
            return clips;
        }
    }
}
#endif
