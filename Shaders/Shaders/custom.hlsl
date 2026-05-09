//----------------------------------------------------------------------------------------------------------------------
// DennokoEx custom.hlsl - lilToon 2.x Extended Shader
// Features: 2nd Reflection (Specular) / 2nd Rim / 3rd Matcap / 3rd Normal Map
//----------------------------------------------------------------------------------------------------------------------

// CBUFFER Variable Declarations
#define LIL_CUSTOM_PROPERTIES \
    float4 _CustomRefl2ndColor; \
    float  _CustomRefl2ndStrength; \
    float  _CustomRefl2ndSmoothness; \
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
    float  _CustomNormal3rdEnabled;

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
        float3 _n3NTS  = LIL_SAMPLE_2D(_CustomNormal3rdTex, sampler_linear_repeat, _n3UV).rgb * 2.0 - 1.0; \
        float3 _n3WS   = normalize(fd.TBN[0] * _n3NTS.x + fd.TBN[1] * _n3NTS.y + fd.TBN[2] * _n3NTS.z); \
        float  _n3Infl = saturate(_CustomNormal3rdStrength * _n3Mask); \
        fd.N = normalize(lerp(fd.N, _n3WS, _n3Infl)); \
    }

// BEFORE_REFLECTION - 2nd Reflection (Blinn-Phong Specular)
#define BEFORE_REFLECTION \
    if (_CustomRefl2ndEnabled > 0.5) { \
        float  _r2Mask     = LIL_SAMPLE_2D(_CustomRefl2ndMaskTex, sampler_linear_repeat, fd.uv0).r; \
        float3 _r2H        = normalize(fd.L + fd.V); \
        float  _r2NdotH    = saturate(dot(fd.N, _r2H)); \
        float  _r2Shininess = exp2(_CustomRefl2ndSmoothness * 10.0 + 1.0); \
        float  _r2Spec     = pow(_r2NdotH, _r2Shininess); \
        fd.col.rgb += _CustomRefl2ndColor.rgb * _r2Spec * _CustomRefl2ndStrength * _r2Mask * fd.lightColor; \
    }

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
        float  _rim2Shadow = lerp(1.0, fd.shadowmix, _CustomRim2ndShadowAttenuation); \
        float  _rim2Amt    = _rim2Val * _CustomRim2ndStrength * _rim2Mask * _rim2Shadow; \
        if (_CustomRim2ndBlendMode < 0.5) { \
            fd.col.rgb += _CustomRim2ndColor.rgb * _rim2Amt; \
        } else { \
            fd.col.rgb = lerp(fd.col.rgb, fd.col.rgb * _CustomRim2ndColor.rgb, _rim2Amt); \
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