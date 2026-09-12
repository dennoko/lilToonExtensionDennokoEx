#if UNITY_EDITOR && DENNOKOEX_HAS_NDMF
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(Dennokoworks.DennokoExPackMasksPlugin))]

namespace Dennokoworks
{
    public class DennokoExPackMasksPlugin : Plugin<DennokoExPackMasksPlugin>
    {
        public override string QualifiedName => "dennokoworks.dennokoex.packmasks";
        public override string DisplayName => "DennokoEx Mask Packer";

        protected override void Configure()
        {
            // Transforming precedes lilToon's VRChat preprocess optimization.
            InPhase(BuildPhase.Transforming).Run("Pack DennokoEx masks", ctx =>
                DennokoExMaskBuild.Pack(ctx.AvatarRootObject, ctx.AssetSaver.SaveAsset));
        }
    }
}
#endif
