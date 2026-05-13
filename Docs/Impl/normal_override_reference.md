lilToonのNormalmap 1st/2nd処理のオーバーライドに関わる実装を全てまとめます。

---

## 1. シェーダーキーワードとフィーチャーフラグ

`lil_replace_keywords.hlsl` でUnityのシェーダーキーワードとフィーチャーフラグが対応付けられています。

| Unityキーワード | lilToonフィーチャー |
|---|---|
| `_NORMALMAP` | `LIL_FEATURE_NORMAL_1ST` |
| `EFFECT_BUMP` | `LIL_FEATURE_NORMAL_2ND` | [1](#0-0) 

---

## 2. シェーダープロパティ定義

`.shader` ファイル（例: `lts.shader`）でプロパティが定義されています。

```hlsl
// NormalMap 1st
[lilToggleLeft] _UseBumpMap     ("sNormalMap", Int) = 0
[Normal]        _BumpMap        ("Normal Map", 2D) = "bump" {}
                _BumpScale      ("Scale", Range(-10,10)) = 1

// NormalMap 2nd
[lilToggleLeft] _UseBump2ndMap  ("sNormalMap2nd", Int) = 0
[Normal]        _Bump2ndMap     ("Normal Map", 2D) = "bump" {}
[lilEnum]       _Bump2ndMap_UVMode ("UV Mode|UV0|UV1|UV2|UV3", Int) = 0
                _Bump2ndScale   ("Scale", Range(-10,10)) = 1
[NoScaleOffset] _Bump2ndScaleMask ("Mask", 2D) = "white" {}
``` [2](#0-1) 

---

## 3. OVERRIDE_NORMAL_1ST / OVERRIDE_NORMAL_2ND マクロ定義

`lil_common_frag.hlsl` に定義されています。**`#if !defined(OVERRIDE_NORMAL_1ST)`** ガードがあるため、カスタムシェーダー側で事前に定義することでオーバーライドできます。

### OVERRIDE_NORMAL_1ST

```hlsl
#if !defined(OVERRIDE_NORMAL_1ST)
    #if defined(LIL_FEATURE_BumpMap)
        #define OVERRIDE_NORMAL_1ST \
            if(_UseBumpMap) \
            { \
                float4 normalTex = LIL_SAMPLE_2D_ST(_BumpMap, sampler_MainTex, fd.uvMain); \
                normalmap = lilUnpackNormalScale(normalTex, _BumpScale); \
            }
    #else
        #define OVERRIDE_NORMAL_1ST
    #endif
#endif
``` [3](#0-2) 

### OVERRIDE_NORMAL_2ND

```hlsl
#if !defined(OVERRIDE_NORMAL_2ND)
    #if defined(LIL_FEATURE_Bump2ndScaleMask)
        #define LIL_SAMPLE_Bump2ndScaleMask \
            bump2ndScale *= LIL_SAMPLE_2D_ST(_Bump2ndScaleMask, sampler_MainTex, fd.uvMain).r
    #else
        #define LIL_SAMPLE_Bump2ndScaleMask
    #endif

    #if defined(LIL_FEATURE_Bump2ndMap)
        #define OVERRIDE_NORMAL_2ND \
            if(_UseBump2ndMap) \
            { \
                float2 uvBump2nd = fd.uv0; \
                if(_Bump2ndMap_UVMode == 1) uvBump2nd = fd.uv1; \
                if(_Bump2ndMap_UVMode == 2) uvBump2nd = fd.uv2; \
                if(_Bump2ndMap_UVMode == 3) uvBump2nd = fd.uv3; \
                float4 normal2ndTex = LIL_SAMPLE_2D_ST(_Bump2ndMap, lil_sampler_linear_repeat, uvBump2nd); \
                float bump2ndScale = _Bump2ndScale; \
                LIL_SAMPLE_Bump2ndScaleMask; \
                normalmap = lilBlendNormal(normalmap, lilUnpackNormalScale(normal2ndTex, bump2ndScale)); \
            }
    #else
        #define OVERRIDE_NORMAL_2ND
    #endif
#endif
``` [4](#0-3) 

---

## 4. 呼び出し箇所（フラグメントシェーダー本体）

`lil_pass_forward_normal.hlsl` の通常パスと `lil_pass_forward_gem.hlsl` のGemパスの両方で同じパターンで呼ばれます。

```hlsl
#if defined(LIL_FEATURE_NORMAL_1ST) || defined(LIL_FEATURE_NORMAL_2ND)
    float3 normalmap = float3(0.0, 0.0, 1.0);  // タンジェント空間の初期値

    // 1st
    BEFORE_NORMAL_1ST
    #if defined(LIL_FEATURE_NORMAL_1ST)
        OVERRIDE_NORMAL_1ST
    #endif

    // 2nd
    BEFORE_NORMAL_2ND
    #if defined(LIL_FEATURE_NORMAL_2ND)
        OVERRIDE_NORMAL_2ND
    #endif

    fd.N = normalize(mul(normalmap, fd.TBN));
    fd.N = fd.facing < (_FlipNormal-1.0) ? -fd.N : fd.N;
#else
    fd.N = normalize(input.normalWS);
    fd.N = fd.facing < (_FlipNormal-1.0) ? -fd.N : fd.N;
#endif
fd.origN = normalize(input.normalWS);
fd.uvMat = mul(fd.cameraMatrix, fd.N).xy * 0.5 + 0.5;
fd.reflectionN = fd.N;
fd.matcapN = fd.N;
fd.matcap2ndN = fd.N;
``` [5](#0-4) [6](#0-5) 

---

## 5. ヘルパー関数の実装

`lil_common_functions.hlsl` に定義されています。

### lilUnpackNormalScale

```hlsl
float3 lilUnpackNormalScale(float4 normalTex, float scale)
{
    float3 normal;
    #if defined(UNITY_NO_DXT5nm)
        normal = normalTex.rgb * 2.0 - 1.0;
        normal.xy *= scale;
    #else
        #if !defined(UNITY_ASTC_NORMALMAP_ENCODING)
            normalTex.a *= normalTex.r;
        #endif
        normal.xy = normalTex.ag * 2.0 - 1.0;
        normal.xy *= scale;
        normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
    #endif
    return normal;
}
``` [7](#0-6) 

### lilBlendNormal（タンジェント空間でのブレンド）

```hlsl
float3 lilBlendNormal(float3 dstNormal, float3 srcNormal)
{
    return float3(dstNormal.xy + srcNormal.xy, dstNormal.z * srcNormal.z);
}
``` [8](#0-7) 

---

## 6. カスタムシェーダーでのオーバーライド方法

`lil_common_frag.hlsl` が `#include` される**前**に定義することでオーバーライドできます。`BEFORE_NORMAL_1ST` / `BEFORE_NORMAL_2ND` は処理の直前に挿入できるフックポイントです。

```hlsl
// lil_common_frag.hlsl をインクルードする前に定義する

#define OVERRIDE_NORMAL_1ST \
    if(_UseBumpMap) \
    { \
        float4 normalTex = LIL_SAMPLE_2D_ST(_BumpMap, sampler_MainTex, fd.uvMain); \
        normalmap = lilUnpackNormalScale(normalTex, _BumpScale); \
        /* ここに独自処理を追加 */ \
    }

#define OVERRIDE_NORMAL_2ND \
    if(_UseBump2ndMap) \
    { \
        float2 uvBump2nd = fd.uv0; \
        if(_Bump2ndMap_UVMode == 1) uvBump2nd = fd.uv1; \
        float4 normal2ndTex = LIL_SAMPLE_2D_ST(_Bump2ndMap, lil_sampler_linear_repeat, uvBump2nd); \
        float bump2ndScale = _Bump2ndScale; \
        normalmap = lilBlendNormal(normalmap, lilUnpackNormalScale(normal2ndTex, bump2ndScale)); \
        /* ここに独自処理を追加 */ \
    }

#include "lil_common_frag.hlsl"
```

重要な点として、`normalmap` 変数はタンジェント空間の法線（初期値 `float3(0,0,1)`）で、最終的に `fd.N = normalize(mul(normalmap, fd.TBN))` でワールド空間に変換されます。`fd.TBN` は `float3x3(tangentWS, bitangentWS, normalWS)` です。 [9](#0-8)

### Citations

**File:** Assets/lilToon/Shader/Includes/lil_replace_keywords.hlsl (L30-31)
```text
// _NORMALMAP                           LIL_FEATURE_NORMAL_1ST
// EFFECT_BUMP                          LIL_FEATURE_NORMAL_2ND
```

**File:** Assets/lilToon/Shader/lts.shader (L122-133)
```text
        // NormalMap
        [lilToggleLeft] _UseBumpMap                 ("sNormalMap", Int) = 0
        [Normal]        _BumpMap                    ("Normal Map", 2D) = "bump" {}
                        _BumpScale                  ("Scale", Range(-10,10)) = 1

        //----------------------------------------------------------------------------------------------------------------------
        // NormalMap 2nd
        [lilToggleLeft] _UseBump2ndMap              ("sNormalMap2nd", Int) = 0
        [Normal]        _Bump2ndMap                 ("Normal Map", 2D) = "bump" {}
        [lilEnum]       _Bump2ndMap_UVMode          ("UV Mode|UV0|UV1|UV2|UV3", Int) = 0
                        _Bump2ndScale               ("Scale", Range(-10,10)) = 1
        [NoScaleOffset] _Bump2ndScaleMask           ("Mask", 2D) = "white" {}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_frag.hlsl (L564-575)
```text
#if !defined(OVERRIDE_NORMAL_1ST)
    #if defined(LIL_FEATURE_BumpMap)
        #define OVERRIDE_NORMAL_1ST \
            if(_UseBumpMap) \
            { \
                float4 normalTex = LIL_SAMPLE_2D_ST(_BumpMap, sampler_MainTex, fd.uvMain); \
                normalmap = lilUnpackNormalScale(normalTex, _BumpScale); \
            }
    #else
        #define OVERRIDE_NORMAL_1ST
    #endif
#endif
```

**File:** Assets/lilToon/Shader/Includes/lil_common_frag.hlsl (L577-600)
```text
#if !defined(OVERRIDE_NORMAL_2ND)
    #if defined(LIL_FEATURE_Bump2ndScaleMask)
        #define LIL_SAMPLE_Bump2ndScaleMask bump2ndScale *= LIL_SAMPLE_2D_ST(_Bump2ndScaleMask, sampler_MainTex, fd.uvMain).r
    #else
        #define LIL_SAMPLE_Bump2ndScaleMask
    #endif

    #if defined(LIL_FEATURE_Bump2ndMap)
        #define OVERRIDE_NORMAL_2ND \
            if(_UseBump2ndMap) \
            { \
                float2 uvBump2nd = fd.uv0; \
                if(_Bump2ndMap_UVMode == 1) uvBump2nd = fd.uv1; \
                if(_Bump2ndMap_UVMode == 2) uvBump2nd = fd.uv2; \
                if(_Bump2ndMap_UVMode == 3) uvBump2nd = fd.uv3; \
                float4 normal2ndTex = LIL_SAMPLE_2D_ST(_Bump2ndMap, lil_sampler_linear_repeat, uvBump2nd); \
                float bump2ndScale = _Bump2ndScale; \
                LIL_SAMPLE_Bump2ndScaleMask; \
                normalmap = lilBlendNormal(normalmap, lilUnpackNormalScale(normal2ndTex, bump2ndScale)); \
            }
    #else
        #define OVERRIDE_NORMAL_2ND
    #endif
#endif
```

**File:** Assets/lilToon/Shader/Includes/lil_pass_forward_normal.hlsl (L284-316)
```text
            #if defined(LIL_FEATURE_NORMAL_1ST) || defined(LIL_FEATURE_NORMAL_2ND)
                float3 normalmap = float3(0.0,0.0,1.0);

                // 1st
                BEFORE_NORMAL_1ST
                #if defined(LIL_FEATURE_NORMAL_1ST)
                    OVERRIDE_NORMAL_1ST
                #endif

                // 2nd
                BEFORE_NORMAL_2ND
                #if defined(LIL_FEATURE_NORMAL_2ND)
                    OVERRIDE_NORMAL_2ND
                #endif

                fd.N = normalize(mul(normalmap, fd.TBN));
                fd.N = fd.facing < (_FlipNormal-1.0) ? -fd.N : fd.N;
            #else
                fd.N = normalize(input.normalWS);
                fd.N = fd.facing < (_FlipNormal-1.0) ? -fd.N : fd.N;
            #endif
            fd.ln = dot(fd.L, fd.N);
            #if defined(LIL_V2F_POSITION_WS)
                fd.nv = saturate(dot(fd.N, fd.V));
                fd.nvabs = abs(dot(fd.N, fd.V));
                fd.uvRim = float2(fd.nvabs,fd.nvabs);
            #endif
            fd.origN = normalize(input.normalWS);
            fd.uvMat = mul(fd.cameraMatrix, fd.N).xy * 0.5 + 0.5;
        #endif
        fd.reflectionN = fd.N;
        fd.matcapN = fd.N;
        fd.matcap2ndN = fd.N;
```

**File:** Assets/lilToon/Shader/Includes/lil_pass_forward_gem.hlsl (L148-175)
```text
        #if defined(LIL_FEATURE_NORMAL_1ST) || defined(LIL_FEATURE_NORMAL_2ND)
            float3 normalmap = float3(0.0,0.0,1.0);

            // 1st
            BEFORE_NORMAL_1ST
            #if defined(LIL_FEATURE_NORMAL_1ST)
                OVERRIDE_NORMAL_1ST
            #endif

            // 2nd
            BEFORE_NORMAL_2ND
            #if defined(LIL_FEATURE_NORMAL_2ND)
                OVERRIDE_NORMAL_2ND
            #endif

            fd.N = mul(normalmap, fd.TBN);
            fd.N = fd.facing < 0.0 ? -fd.N - fd.V * 0.2 : fd.N;
            fd.N = normalize(fd.N);
        #else
            fd.N = input.normalWS;
            fd.N = fd.facing < 0.0 ? -fd.N - fd.V * 0.2 : fd.N;
            fd.N = normalize(fd.N);
        #endif
        fd.origN = normalize(input.normalWS);
        fd.uvMat = mul(fd.cameraMatrix, fd.N).xy * 0.5 + 0.5;
        fd.reflectionN = fd.N;
        fd.matcapN = fd.N;
        fd.matcap2ndN = fd.N;
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L135-138)
```text
float3 lilBlendNormal(float3 dstNormal, float3 srcNormal)
{
    return float3(dstNormal.xy + srcNormal.xy, dstNormal.z * srcNormal.z);
}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L166-181)
```text
float3 lilUnpackNormalScale(float4 normalTex, float scale)
{
    float3 normal;
    #if defined(UNITY_NO_DXT5nm)
        normal = normalTex.rgb * 2.0 - 1.0;
        normal.xy *= scale;
    #else
        #if !defined(UNITY_ASTC_NORMALMAP_ENCODING)
            normalTex.a *= normalTex.r;
        #endif
        normal.xy = normalTex.ag * 2.0 - 1.0;
        normal.xy *= scale;
        normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
    #endif
    return normal;
}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_macro.hlsl (L2323-2325)
```text
#define LIL_GET_TBN_DATA(input,fd) \
    float3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * (input.tangentWS.w * LIL_NEGATIVE_SCALE); \
    fd.TBN = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)
```
