// Injected at lilToon's *LIL_SUBSHADER_INSERT* point, i.e. INSIDE each pass program:
//   #define LIL_PASS_FORWARDADD   (ForwardAdd passes only)
//   #include lil_pipeline_*.hlsl
//   #include lil_common.hlsl
//   >>> this file <<<
//   #include lil_pass_*.hlsl      (this is where the BEFORE_* hook macros are expanded)
//
// That position matters twice:
//  * the LIL_V2F_FORCE_* defines below still reach the v2f struct built in lil_pass_*.hlsl;
//  * unlike custom.hlsl — which is included from the shader-level HLSLINCLUDE, BEFORE the pass
//    defines LIL_PASS_FORWARDADD — code here can actually branch on the pass with #if.
//    Any helper that must behave differently in the additive pass therefore lives HERE, not in
//    custom.hlsl, where `#if !defined(LIL_PASS_FORWARDADD)` would silently always be true.

#ifndef DNKW_CUSTOM_INSERT_INCLUDED
#define DNKW_CUSTOM_INSERT_INCLUDED

// Ensure TBN matrix is passed to the fragment shader (used for decal normals and 2nd reflection anisotropy)
#define LIL_V2F_FORCE_TANGENT
#define LIL_V2F_FORCE_BITANGENT

// Matcap lighting correction — mirrors lilToon's lilGetMatCap (_MatCapEnableLighting).
// Without this the decal matcap renders at constant full brightness and "floats" against
// the surrounding environment. fd.lightColor is the scene/light color of the current pass.
// ForwardBase: lerp toward lightColor by enableLighting (matches lilToon base pass).
// ForwardAdd : multiply by the additive light color so extra lights don't add the matcap at
//   full brightness; skipped for Multiply blend (mode 3), same as lilToon's `_MatCapBlendMode < 3`.
float3 DNKW_MatcapLighting(float3 mc, float3 lightColor, float enableLighting, float blendMode)
{
    #if !defined(LIL_PASS_FORWARDADD)
        return lerp(mc, mc * lightColor, enableLighting);
    #else
        return (blendMode < 3) ? mc * lightColor * enableLighting : mc;
    #endif
}

#if DNKW_VRCLV_AVAILABLE
// Light-Volume specular term of Reflection 2nd.
// lilToon expands BEFORE_REFLECTION in the ForwardAdd pass too, which runs once PER additional
// realtime light. The light-volume / light-probe term depends only on position and view, so adding
// it again for every extra light multiplies the whole reflection by the light count (a room with
// 3 point lights renders it at 4x) and re-samples the volume 3D textures each time. lilToon guards
// its own environment reflection identically with `#if !defined(LIL_PASS_FORWARDADD)`; only the
// direct-light Blinn-Phong term (_CustomRefl2ndDirectBlend) is legitimately per-light and stays in
// the BEFORE_REFLECTION macro.
float3 DNKW_Refl2ndLVSpecular(float3 positionWS, float smoothness, float3 N, float3 V)
{
    #if !defined(LIL_PASS_FORWARDADD)
        float3 L0, L1r, L1g, L1b;
        LightVolumeSH(positionWS, L0, L1r, L1g, L1b);
        return LightVolumeSpecular(float3(1.0, 1.0, 1.0), smoothness, 1.0, N, V, L0, L1r, L1g, L1b);
    #else
        return float3(0.0, 0.0, 0.0);
    #endif
}
#endif // DNKW_VRCLV_AVAILABLE

#endif // DNKW_CUSTOM_INSERT_INCLUDED
