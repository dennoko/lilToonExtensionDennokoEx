//----------------------------------------------------------------------------------------------------------------------
// DennokoEx custom.hlsl - lilToon 2.x Extended Shader
// Features: 2nd Reflection (Specular) / 2nd Rim / 3rd Matcap / 3rd Normal Map
//----------------------------------------------------------------------------------------------------------------------

// VRC Light Volumes optional integration (package detected at compile time via direct include)
// To disable and fall back to no-VRCLV branch, add: #define DNKW_DISABLE_VRCLV
#include "UnityCG.cginc"
#if !defined(DNKW_DISABLE_VRCLV)
    #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
    #define DNKW_VRCLV_AVAILABLE 1
#else
    #define DNKW_VRCLV_AVAILABLE 0
#endif

// CBUFFER Variable Declarations
#define LIL_CUSTOM_PROPERTIES \
    float4 _CustomRefl2ndColor; \
    float  _CustomRefl2ndStrength; \
    float  _CustomRefl2ndSmoothness; \
    float  _CustomRefl2ndAnisotropic; \
    float  _CustomRefl2ndAnisoPrimaryShift; \
    float4 _CustomRefl2ndAnisoSecondaryColor; \
    float  _CustomRefl2ndAnisoSecondaryStrength; \
    float  _CustomRefl2ndAnisoSecondaryShift; \
    float  _CustomRefl2ndShadowAttenuation; \
    float  _CustomRefl2ndEnabled; \
    float4 _CustomRim2ndColor; \
    float  _CustomRim2ndPower; \
    float  _CustomRim2ndStrength; \
    float  _CustomRim2ndBlendMode; \
    float  _CustomRim2ndShadowAttenuation; \
    float  _CustomRim2ndEnabled; \
    float4 _CustomMatcap3rdColor; \
    float  _CustomMatcap3rdStrength; \
    float  _CustomMatcap3rdBlendMode; \
    float  _CustomMatcap3rdShadowAttenuation; \
    float  _CustomMatcap3rdEnabled; \
    float4 _CustomNormal3rdTex_ST; \
    float  _CustomNormal3rdStrength; \
    float  _CustomNormal3rdEnabled; \
    float  _CustomRefl2ndLVColorStrength; \
    float  _CustomRefl2ndMainColorStrength; \
    float  _CustomRefl2ndBlur; \
    float  _CustomRim2ndMainColorStrength; \
    float  _CustomRim2ndBlur;

// Texture Declarations (use 1 shared sampler to stay within ps_4_0 16-sampler limit)
#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_CustomRefl2ndMaskTex); \
    TEXTURE2D(_CustomRim2ndMaskTex); \
    TEXTURE2D(_CustomMatcap3rdTex); \
    TEXTURE2D(_CustomMatcap3rdMaskTex); \
    TEXTURE2D(_CustomNormal3rdTex); \
    TEXTURE2D(_CustomNormal3rdMaskTex);

// Add vertex copy
#define LIL_CUSTOM_VERT_COPY

// BEFORE_AUDIOLINK - 3rd Normal Map (composited after lilToon's normal map pipeline)
#define BEFORE_AUDIOLINK \
    if (_CustomNormal3rdEnabled > 0.5) { \
        float2 _n3UV   = fd.uv0 * _CustomNormal3rdTex_ST.xy + _CustomNormal3rdTex_ST.zw; \
        float  _n3Mask = LIL_SAMPLE_2D(_CustomNormal3rdMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float4 _n3Raw  = LIL_SAMPLE_2D(_CustomNormal3rdTex, sampler_linear_repeat, _n3UV); \
        float3 _n3NTS; \
        _n3NTS.xy = _n3Raw.ag * 2.0 - 1.0; \
        _n3NTS.z  = sqrt(max(0.0, 1.0 - dot(_n3NTS.xy, _n3NTS.xy))); \
        float3 _n3WS   = normalize(fd.TBN[0] * _n3NTS.x + fd.TBN[1] * _n3NTS.y + fd.TBN[2] * _n3NTS.z); \
        float  _n3Infl = saturate(_CustomNormal3rdStrength * _n3Mask); \
        fd.N = normalize(lerp(fd.N, _n3WS, _n3Infl)); \
    }

// BEFORE_REFLECTION - 2nd Reflection
// Isotropic (VRCLV): LightVolumeSpecular with LV color intensity control.
//   _CustomRefl2ndLVColorStrength: 0=LV輝度のみ(色無し), 1=LV色をフルで受け取る(デフォルト)
// Anisotropic: Kajiya-Kay (fd.L / fd.lightColor)
// 両パス共通: _CustomRefl2ndMainColorStrength でfd.albedoを最終出力に乗算
#if DNKW_VRCLV_AVAILABLE
#define BEFORE_REFLECTION \
    if (_CustomRefl2ndEnabled > 0.5) { \
        float  _r2Mask   = LIL_SAMPLE_2D(_CustomRefl2ndMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float  _r2Shadow = lerp(1.0, fd.shadowmix, _CustomRefl2ndShadowAttenuation); \
        float3 _r2Out; \
        if (_CustomRefl2ndAnisotropic > 0.5) { \
            float  _r2Shininess = exp2(_CustomRefl2ndSmoothness * 10.0 + 1.0); \
            float3 _r2H  = normalize(fd.L + fd.V); \
            float3 _r2T1 = normalize(fd.TBN[1] + fd.N * _CustomRefl2ndAnisoPrimaryShift); \
            float  _r2TH1   = dot(_r2T1, _r2H); \
            float  _r2Spec1 = smoothstep(-1.0, 0.0, _r2TH1) * pow(sqrt(max(0.0, 1.0 - _r2TH1 * _r2TH1)), _r2Shininess); \
            float3 _r2T2 = normalize(fd.TBN[1] + fd.N * _CustomRefl2ndAnisoSecondaryShift); \
            float  _r2TH2   = dot(_r2T2, _r2H); \
            float  _r2Spec2 = smoothstep(-1.0, 0.0, _r2TH2) * pow(sqrt(max(0.0, 1.0 - _r2TH2 * _r2TH2)), _r2Shininess * 0.5); \
            _r2Spec1 = lerp(step(0.5, _r2Spec1), _r2Spec1, _CustomRefl2ndBlur); \
            _r2Spec2 = lerp(step(0.5, _r2Spec2), _r2Spec2, _CustomRefl2ndBlur); \
            _r2Out = (_CustomRefl2ndColor.rgb * _r2Spec1 + _CustomRefl2ndAnisoSecondaryColor.rgb * _r2Spec2 * _CustomRefl2ndAnisoSecondaryStrength) * _CustomRefl2ndStrength * _r2Mask * _r2Shadow * fd.lightColor; \
        } else { \
            float3 _r2L0, _r2L1r, _r2L1g, _r2L1b; \
            LightVolumeSH(fd.positionWS, _r2L0, _r2L1r, _r2L1g, _r2L1b); \
            float3 _r2Spec = LightVolumeSpecular(float3(1.0, 1.0, 1.0), _CustomRefl2ndSmoothness, 1.0, fd.N, fd.V, _r2L0, _r2L1r, _r2L1g, _r2L1b); \
            float  _r2Lum  = dot(_r2Spec, float3(0.299, 0.587, 0.114)); \
            _r2Spec = lerp(float3(_r2Lum, _r2Lum, _r2Lum), _r2Spec, _CustomRefl2ndLVColorStrength); \
            float  _r2PostLum = dot(_r2Spec, float3(0.299, 0.587, 0.114)); \
            float  _r2Shaped  = lerp(step(0.5, _r2PostLum), _r2PostLum, _CustomRefl2ndBlur); \
            _r2Spec *= _r2Shaped / max(_r2PostLum, 1e-5); \
            _r2Out  = _r2Spec * _CustomRefl2ndColor.rgb * _CustomRefl2ndStrength * _r2Mask; \
        } \
        fd.col.rgb += _r2Out * lerp(float3(1.0, 1.0, 1.0), fd.albedo, _CustomRefl2ndMainColorStrength); \
    }
#else
#define BEFORE_REFLECTION \
    if (_CustomRefl2ndEnabled > 0.5) { \
        float  _r2Mask      = LIL_SAMPLE_2D(_CustomRefl2ndMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float  _r2Shininess = exp2(_CustomRefl2ndSmoothness * 10.0 + 1.0); \
        float  _r2Shadow    = lerp(1.0, fd.shadowmix, _CustomRefl2ndShadowAttenuation); \
        float3 _r2H         = normalize(fd.L + fd.V); \
        float3 _r2Out; \
        if (_CustomRefl2ndAnisotropic > 0.5) { \
            float3 _r2T1    = normalize(fd.TBN[1] + fd.N * _CustomRefl2ndAnisoPrimaryShift); \
            float  _r2TH1   = dot(_r2T1, _r2H); \
            float  _r2Spec1 = smoothstep(-1.0, 0.0, _r2TH1) * pow(sqrt(max(0.0, 1.0 - _r2TH1 * _r2TH1)), _r2Shininess); \
            float3 _r2T2    = normalize(fd.TBN[1] + fd.N * _CustomRefl2ndAnisoSecondaryShift); \
            float  _r2TH2   = dot(_r2T2, _r2H); \
            float  _r2Spec2 = smoothstep(-1.0, 0.0, _r2TH2) * pow(sqrt(max(0.0, 1.0 - _r2TH2 * _r2TH2)), _r2Shininess * 0.5); \
            _r2Spec1 = lerp(step(0.5, _r2Spec1), _r2Spec1, _CustomRefl2ndBlur); \
            _r2Spec2 = lerp(step(0.5, _r2Spec2), _r2Spec2, _CustomRefl2ndBlur); \
            _r2Out = (_CustomRefl2ndColor.rgb * _r2Spec1 + _CustomRefl2ndAnisoSecondaryColor.rgb * _r2Spec2 * _CustomRefl2ndAnisoSecondaryStrength) * _CustomRefl2ndStrength * _r2Mask * _r2Shadow * fd.lightColor; \
        } else { \
            float  _r2NdotH = saturate(dot(fd.N, _r2H)); \
            float  _r2Spec  = pow(_r2NdotH, _r2Shininess); \
            _r2Spec = lerp(step(0.5, _r2Spec), _r2Spec, _CustomRefl2ndBlur); \
            _r2Out = _CustomRefl2ndColor.rgb * _r2Spec * _CustomRefl2ndStrength * _r2Mask * _r2Shadow * fd.lightColor; \
        } \
        fd.col.rgb += _r2Out * lerp(float3(1.0, 1.0, 1.0), fd.albedo, _CustomRefl2ndMainColorStrength); \
    }
#endif

// BEFORE_RIMLIGHT - 3rd Matcap (Third matcap added after Matcap and 2nd Matcap processing)
#define BEFORE_RIMLIGHT \
    if (_CustomMatcap3rdEnabled > 0.5) { \
        float3 _mc3NVS  = mul((float3x3)UNITY_MATRIX_V, fd.N); \
        float2 _mc3UV   = _mc3NVS.xy * 0.5 + 0.5; \
        float4 _mc3Tex  = LIL_SAMPLE_2D(_CustomMatcap3rdTex, sampler_linear_repeat, _mc3UV); \
        float  _mc3Mask = LIL_SAMPLE_2D(_CustomMatcap3rdMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float  _mc3ShadowFactor = lerp(1.0, fd.shadowmix, _CustomMatcap3rdShadowAttenuation); \
        float3 _mc3Color = _mc3Tex.rgb * _CustomMatcap3rdColor.rgb * _mc3Mask * _mc3ShadowFactor; \
        if (_CustomMatcap3rdBlendMode < 0.5) { \
            fd.col.rgb += _mc3Color * _CustomMatcap3rdStrength; \
        } else if (_CustomMatcap3rdBlendMode < 1.5) { \
            fd.col.rgb *= lerp(float3(1.0, 1.0, 1.0), _mc3Color, _CustomMatcap3rdStrength); \
        } else { \
            fd.col.rgb = 1.0 - (1.0 - fd.col.rgb) * (1.0 - _mc3Color * _CustomMatcap3rdStrength); \
        } \
    }

// BEFORE_EMISSION_1ST - 2nd Rim (Applied after standard rim light processing)
// BlendMode: 0=RimLight(Add) / 1=RimShade(Multiply)
#define BEFORE_EMISSION_1ST \
    if (_CustomRim2ndEnabled > 0.5) { \
        float  _rim2Mask   = LIL_SAMPLE_2D(_CustomRim2ndMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float  _rim2NdotV  = saturate(dot(fd.N, fd.V)); \
        float  _rim2Val    = pow(1.0 - _rim2NdotV, _CustomRim2ndPower); \
        _rim2Val = lerp(step(0.5, _rim2Val), _rim2Val, _CustomRim2ndBlur); \
        float  _rim2Shadow = lerp(1.0, fd.shadowmix, _CustomRim2ndShadowAttenuation); \
        float  _rim2Amt    = _rim2Val * _CustomRim2ndStrength * _rim2Mask * _rim2Shadow; \
        float3 _rim2Color  = _CustomRim2ndColor.rgb * lerp(float3(1.0, 1.0, 1.0), fd.albedo, _CustomRim2ndMainColorStrength); \
        if (_CustomRim2ndBlendMode < 0.5) { \
            fd.col.rgb += _rim2Color * _rim2Amt; \
        } else { \
            fd.col.rgb = lerp(fd.col.rgb, fd.col.rgb * _rim2Color, _rim2Amt); \
        } \
    }

//----------------------------------------------------------------------------------------------------------------------
// Information about variables
//----------------------------------------------------------------------------------------------------------------------

//----------------------------------------------------------------------------------------------------------------------
// Vertex shader inputs (appdata structure)
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   input.positionOS        POSITION
// float2   input.uv0               TEXCOORD0
// float2   input.uv1               TEXCOORD1
// float2   input.uv2               TEXCOORD2
// float2   input.uv3               TEXCOORD3
// float2   input.uv4               TEXCOORD4
// float2   input.uv5               TEXCOORD5
// float2   input.uv6               TEXCOORD6
// float2   input.uv7               TEXCOORD7
// float4   input.color             COLOR
// float3   input.normalOS          NORMAL
// float4   input.tangentOS         TANGENT
// uint     vertexID                SV_VertexID

//----------------------------------------------------------------------------------------------------------------------
// Vertex shader outputs or pixel shader inputs (v2f structure)
//
// The structure depends on the pass.
// Please check lil_pass_xx.hlsl for details.
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   output.positionCS       SV_POSITION
// float2   output.uv01             TEXCOORD0 TEXCOORD1
// float2   output.uv23             TEXCOORD2 TEXCOORD3
// float3   output.positionOS       object space position
// float3   output.positionWS       world space position
// float3   output.normalWS         world space normal
// float4   output.tangentWS        world space tangent

//----------------------------------------------------------------------------------------------------------------------
// Variables commonly used in the forward pass
//
// These are members of `lilFragData fd`
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   col                     lit color
// float3   albedo                  unlit color
// float3   emissionColor           color of emission
// -------- ----------------------- --------------------------------------------------------------------
// float3   lightColor              color of light
// float3   indLightColor           color of indirectional light
// float3   addLightColor           color of additional light
// float    attenuation             attenuation of light
// float3   invLighting             saturate((1.0 - lightColor) * sqrt(lightColor));
// -------- ----------------------- --------------------------------------------------------------------
// float2   uv0                     TEXCOORD0
// float2   uv1                     TEXCOORD1
// float2   uv2                     TEXCOORD2
// float2   uv3                     TEXCOORD3
// float2   uvMain                  Main UV
// float2   uvMat                   MatCap UV
// float2   uvRim                   Rim Light UV
// float2   uvPanorama              Panorama UV
// float2   uvScn                   Screen UV
// bool     isRightHand             input.tangentWS.w > 0.0;
// -------- ----------------------- --------------------------------------------------------------------
// float3   positionOS              object space position
// float3   positionWS              world space position
// float4   positionCS              clip space position
// float4   positionSS              screen space position
// float    depth                   distance from camera
// -------- ----------------------- --------------------------------------------------------------------
// float3x3 TBN                     tangent / bitangent / normal matrix
// float3   T                       tangent direction
// float3   B                       bitangent direction
// float3   N                       normal direction
// float3   V                       view direction
// float3   L                       light direction
// float3   origN                   normal direction without normal map
// float3   origL                   light direction without sh light
// float3   headV                   middle view direction of 2 cameras
// float3   reflectionN             normal direction for reflection
// float3   matcapN                 normal direction for reflection for MatCap
// float3   matcap2ndN              normal direction for reflection for MatCap 2nd
// float    facing                  VFACE
// -------- ----------------------- --------------------------------------------------------------------
// float    vl                      dot(viewDirection, lightDirection);
// float    hl                      dot(headDirection, lightDirection);
// float    ln                      dot(lightDirection, normalDirection);
// float    nv                      saturate(dot(normalDirection, viewDirection));
// float    nvabs                   abs(dot(normalDirection, viewDirection));
// -------- ----------------------- --------------------------------------------------------------------
// float4   triMask                 TriMask (for lite version)
// float3   parallaxViewDirection   mul(tbnWS, viewDirection);
// float2   parallaxOffset          parallaxViewDirection.xy / (parallaxViewDirection.z+0.5);
// float    anisotropy              strength of anisotropy
// float    smoothness              smoothness
// float    roughness               roughness
// float    perceptualRoughness     perceptual roughness
// float    shadowmix               this variable is 0 in the shadow area
// float    audioLinkValue          volume acquired by AudioLink
// -------- ----------------------- --------------------------------------------------------------------
// uint     renderingLayers         light layer of object (for URP / HDRP)
// uint     featureFlags            feature flags (for HDRP)
// uint2    tileIndex               tile index (for HDRP)