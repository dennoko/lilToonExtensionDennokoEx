// =============================================================================
// DennokoEx custom.hlsl - lilToon 2.x Extended Shader
// Features: 2nd Reflection (Anisotropy) / 2nd Rim / 3rd Matcap / Additional Decal
// =============================================================================
#ifndef DENNOKOEX_CUSTOM_INCLUDED
#define DENNOKOEX_CUSTOM_INCLUDED

// =============================================================================
// CBUFFER Variable Declarations
// =============================================================================
#define LIL_CUSTOM_PROPERTIES \
    float4 _CustomRefl2ndColor; \
    float  _CustomRefl2ndStrength; \
    float  _CustomRefl2ndAnisotropy; \
    float  _CustomRefl2ndAnisotropyAngle; \
    float  _CustomRefl2ndEnabled; \
    float4 _CustomRim2ndColor; \
    float  _CustomRim2ndPower; \
    float  _CustomRim2ndStrength; \
    float  _CustomRim2ndBlendMode; \
    float  _CustomRim2ndEnabled; \
    float4 _CustomMatcap3rdColor; \
    float  _CustomMatcap3rdStrength; \
    float  _CustomMatcap3rdBlendMode; \
    float  _CustomMatcap3rdEnabled; \
    float4 _CustomDecalColor; \
    float4 _CustomDecalTiling; \
    float  _CustomDecalNormalStrength; \
    float  _CustomDecalMatcapStrength; \
    float  _CustomDecalBlendMode; \
    float  _CustomDecalEnabled;

// =============================================================================
// Texture Declarations
// =============================================================================
#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_CustomRefl2ndTex); \
    SAMPLER(sampler_CustomRefl2ndTex); \
    TEXTURE2D(_CustomRefl2ndMaskTex); \
    SAMPLER(sampler_CustomRefl2ndMaskTex); \
    TEXTURE2D(_CustomRim2ndMaskTex); \
    SAMPLER(sampler_CustomRim2ndMaskTex); \
    TEXTURE2D(_CustomMatcap3rdTex); \
    SAMPLER(sampler_CustomMatcap3rdTex); \
    TEXTURE2D(_CustomMatcap3rdMaskTex); \
    SAMPLER(sampler_CustomMatcap3rdMaskTex); \
    TEXTURE2D(_CustomDecalSharedMaskTex); \
    SAMPLER(sampler_CustomDecalSharedMaskTex); \
    TEXTURE2D(_CustomDecalTex); \
    SAMPLER(sampler_CustomDecalTex); \
    TEXTURE2D(_CustomDecalNormalTex); \
    SAMPLER(sampler_CustomDecalNormalTex); \
    TEXTURE2D(_CustomDecalMatcapTex); \
    SAMPLER(sampler_CustomDecalMatcapTex);

// =============================================================================
// BEFORE_MAIN — Apply decal normal to fd.N (affecting lighting)
// =============================================================================
#define BEFORE_MAIN \
    if (_CustomDecalEnabled > 0.5) { \
        float2 _decalUV0 = fd.uv0 * _CustomDecalTiling.xy + _CustomDecalTiling.zw; \
        float  _decalMask0 = LIL_SAMPLE_2D(_CustomDecalSharedMaskTex, sampler_CustomDecalSharedMaskTex, fd.uv0).r; \
        float3 _decalNTS = LIL_SAMPLE_2D(_CustomDecalNormalTex, sampler_CustomDecalNormalTex, _decalUV0).rgb * 2.0 - 1.0; \
        fd.N = normalize(fd.N + (fd.TBN[0] * _decalNTS.x + fd.TBN[1] * _decalNTS.y) * _CustomDecalNormalStrength * _decalMask0); \
    }

// =============================================================================
// BEFORE_MAIN2ND — Blend decal color after main color
// =============================================================================
#define BEFORE_MAIN2ND \
    if (_CustomDecalEnabled > 0.5) { \
        float2 _decalUV = fd.uv0 * _CustomDecalTiling.xy + _CustomDecalTiling.zw; \
        float  _decalMask = LIL_SAMPLE_2D(_CustomDecalSharedMaskTex, sampler_CustomDecalSharedMaskTex, fd.uv0).r; \
        float4 _decalTex  = LIL_SAMPLE_2D(_CustomDecalTex, sampler_CustomDecalTex, _decalUV); \
        float  _decalBlend = _decalTex.a * _decalMask; \
        if (_CustomDecalBlendMode < 0.5) { \
            fd.col.rgb = lerp(fd.col.rgb, _decalTex.rgb * _CustomDecalColor.rgb, _decalBlend); \
        } else if (_CustomDecalBlendMode < 1.5) { \
            fd.col.rgb += _decalTex.rgb * _CustomDecalColor.rgb * _decalBlend; \
        } else { \
            fd.col.rgb *= lerp(float3(1.0, 1.0, 1.0), _decalTex.rgb * _CustomDecalColor.rgb, _decalBlend); \
        } \
    }

// =============================================================================
// BEFORE_REFLECTION — 2nd Reflection (Anisotropic Matcap method)
// Rotate TBN[0]=Tangent / TBN[1]=Bitangent to distort normals and reproduce anisotropic reflection
// =============================================================================
#define BEFORE_REFLECTION \
    if (_CustomRefl2ndEnabled > 0.5) { \
        float  _r2Mask = LIL_SAMPLE_2D(_CustomRefl2ndMaskTex, sampler_CustomRefl2ndMaskTex, fd.uv0).r; \
        float  _r2CosA = cos(_CustomRefl2ndAnisotropyAngle); \
        float  _r2SinA = sin(_CustomRefl2ndAnisotropyAngle); \
        float3 _r2AnisoDir = normalize(_r2CosA * fd.TBN[0] + _r2SinA * fd.TBN[1]); \
        float3 _r2PerturbN = normalize(fd.N + _r2AnisoDir * _CustomRefl2ndAnisotropy); \
        float3 _r2NVS = mul((float3x3)UNITY_MATRIX_V, _r2PerturbN); \
        float2 _r2UV = _r2NVS.xy * 0.5 + 0.5; \
        float4 _r2Tex = LIL_SAMPLE_2D(_CustomRefl2ndTex, sampler_CustomRefl2ndTex, _r2UV); \
        fd.col.rgb += _r2Tex.rgb * _CustomRefl2ndColor.rgb * _CustomRefl2ndStrength * _r2Mask; \
    }

// =============================================================================
// BEFORE_RIMLIGHT — 3rd Matcap (Third matcap added after Matcap and 2nd Matcap processing)
// =============================================================================
#define BEFORE_RIMLIGHT \
    if (_CustomMatcap3rdEnabled > 0.5) { \
        float3 _mc3NVS = mul((float3x3)UNITY_MATRIX_V, fd.N); \
        float2 _mc3UV  = _mc3NVS.xy * 0.5 + 0.5; \
        float4 _mc3Tex = LIL_SAMPLE_2D(_CustomMatcap3rdTex, sampler_CustomMatcap3rdTex, _mc3UV); \
        float  _mc3Mask = LIL_SAMPLE_2D(_CustomMatcap3rdMaskTex, sampler_CustomMatcap3rdMaskTex, fd.uv0).r; \
        float3 _mc3Color = _mc3Tex.rgb * _CustomMatcap3rdColor.rgb * _mc3Mask; \
        if (_CustomMatcap3rdBlendMode < 0.5) { \
            fd.col.rgb += _mc3Color * _CustomMatcap3rdStrength; \
        } else if (_CustomMatcap3rdBlendMode < 1.5) { \
            fd.col.rgb *= lerp(float3(1.0, 1.0, 1.0), _mc3Color, _CustomMatcap3rdStrength); \
        } else { \
            fd.col.rgb = 1.0 - (1.0 - fd.col.rgb) * (1.0 - _mc3Color * _CustomMatcap3rdStrength); \
        } \
    }

// =============================================================================
// BEFORE_EMISSION_1ST — 2nd Rim (Applied after standard rim light processing)
// BlendMode: 0=RimLight(Add) / 1=RimShade(Multiply)
// =============================================================================
#define BEFORE_EMISSION_1ST \
    if (_CustomRim2ndEnabled > 0.5) { \
        float  _rim2Mask = LIL_SAMPLE_2D(_CustomRim2ndMaskTex, sampler_CustomRim2ndMaskTex, fd.uv0).r; \
        float  _rim2NdotV = saturate(dot(fd.N, fd.V)); \
        float  _rim2Val  = pow(1.0 - _rim2NdotV, _CustomRim2ndPower); \
        float  _rim2Amt  = _rim2Val * _CustomRim2ndStrength * _rim2Mask; \
        if (_CustomRim2ndBlendMode < 0.5) { \
            fd.col.rgb += _CustomRim2ndColor.rgb * _rim2Amt; \
        } else { \
            fd.col.rgb = lerp(fd.col.rgb, fd.col.rgb * _CustomRim2ndColor.rgb, _rim2Amt); \
        } \
    }

// =============================================================================
// BEFORE_OUTPUT — Decal Matcap (Overlay matcap only on decal area)
// =============================================================================
#define BEFORE_OUTPUT \
    if (_CustomDecalEnabled > 0.5) { \
        float  _dcMask = LIL_SAMPLE_2D(_CustomDecalSharedMaskTex, sampler_CustomDecalSharedMaskTex, fd.uv0).r; \
        float3 _dcNVS  = mul((float3x3)UNITY_MATRIX_V, fd.N); \
        float2 _dcMCUV = _dcNVS.xy * 0.5 + 0.5; \
        float4 _dcMCTex = LIL_SAMPLE_2D(_CustomDecalMatcapTex, sampler_CustomDecalMatcapTex, _dcMCUV); \
        fd.col.rgb += _dcMCTex.rgb * _CustomDecalMatcapStrength * _dcMask; \
    }

#endif // DENNOKOEX_CUSTOM_INCLUDED
