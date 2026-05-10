# VRC Light Volumes 反射実装ガイド (移植用)

このドキュメントでは、本プロジェクトに実装されている **VRC Light Volumes** を使用した反射（スペキュラ）ロジックを他のシェーダーに移植するための詳細な実装手順を解説します。

## 1. 概要
VRC Light Volumes は、シーン内のライト情報を Spherical Harmonics (SH) として 3D テクスチャに保存するシステムです。これを利用することで、通常のライトプロップよりも高精細な、方向性を持った間接光の反射を表現できます。

## 2. 準備
実装には [VRC Light Volumes](https://github.com/S-S-R/VRC-Light-Volumes) パッケージが必要です。

### インクルード設定
シェーダーのプロパティまたはインクルードセクションで、以下のようにファイルを読み込みます。`UnityCG.cginc` を先に読み込むことで、パッケージ未導入時のフォールバックが機能するようになります。

```hlsl
// Unity標準のライトプロファイル等を使用するために必要
#include "UnityCG.cginc"

// パッケージが導入されている場合のみインクルードする設定
#if !defined(DNKW_DISABLE_VRCLV)
    // パッケージ内のコア関数を読み込む
    #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
    #define DNKW_VRCLV_AVAILABLE 1
#else
    #define DNKW_VRCLV_AVAILABLE 0
#endif
```

## 3. フォールバックとマクロの定義
パッケージがない環境でもコンパイルエラーを防ぎ、通常の Unity ライトプロップにフォールバックするためのマクロを定義します。

```hlsl
#if !DNKW_VRCLV_AVAILABLE
    // パッケージがない場合のフォールバック処理
    #define DNKW_SH_L1_SCALE 0.565f
    
    // Unity標準のライトプロップ(unity_SH*)からSH係数を取得する
    void dnkw_lightVolumeSHFallback(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b)
    {
        /* Fallback uses unity_SH* probe uniforms (per-object), not worldPos volume sampling */
        L0 = float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
        /* DNKW_SH_L1_SCALE reduces SH ringing artifacts in probe L1 terms and matches LightVolumes.cginc fallback */
        L1r = unity_SHAr.xyz * DNKW_SH_L1_SCALE;
        L1g = unity_SHAg.xyz * DNKW_SH_L1_SCALE;
        L1b = unity_SHAb.xyz * DNKW_SH_L1_SCALE;
    }
    
    float3 dnkw_lightVolumeSpecularFallback(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
    {
        /* Without LightVolumes.cginc we keep prior behavior and avoid approximating a different spec model */
        return 0; 
    }
    
    #define DNKW_LIGHTVOLUME_SH(worldPos, L0, L1r, L1g, L1b) dnkw_lightVolumeSHFallback((worldPos), (L0), (L1r), (L1g), (L1b))
    #define DNKW_LIGHTVOLUME_SPECULAR(...) dnkw_lightVolumeSpecularFallback(__VA_ARGS__)
#else
    // パッケージ提供の関数をそのまま使用する
    #define DNKW_LIGHTVOLUME_SH(worldPos, L0, L1r, L1g, L1b) LightVolumeSH((worldPos), (L0), (L1r), (L1g), (L1b))
    #define DNKW_LIGHTVOLUME_SPECULAR(albedo, smoothness, metallic, worldNormal, viewDir, L0, L1r, L1g, L1b) LightVolumeSpecular((albedo), (smoothness), (metallic), (worldNormal), (viewDir), (L0), (L1r), (L1g), (L1b))
#endif
```

## 4. スペキュラ計算の実装
ピクセルシェーダー内で以下の手順で計算を行います。

### 実装例
以下は `lilFragData` を使用した例ですが、他のシェーダーでも同様の変数を渡すことで動作します。

```hlsl
// 1. SHデータの取得（ワールド座標からその地点のライト情報をサンプリング）
float3 L0, L1r, L1g, L1b;
DNKW_LIGHTVOLUME_SH(fd.positionWS, L0, L1r, L1g, L1b);

// 2. スペキュラの蓄積変数の初期化
float3 lvSpecAccum = 0;

// 3. 各レイヤーやパーツごとに計算（ループ内など）
{
    // パラメータの準備
    float3 baseCol = _SpecColor.rgb;   // 反射の色（F0として扱われる）
    float smooth = _SpecSmoothness;   // 滑らかさ (0-1)
    const float LV_F0_METALLIC = 1.0; // 1.0に固定することでbaseColをそのまま反射色として使用
    float3 N = worldNormal;           // 法線
    float3 V = viewDir;               // 視線方向
    
    // ライトボリュームベースのスペキュラ計算
    // 内部的には3つのL1方向（R,G,B各チャンネルのライト方向）に対してGGX配分を計算しています
    lvSpecAccum += intensity * DNKW_LIGHTVOLUME_SPECULAR(baseCol, smooth, LV_F0_METALLIC, N, V, L0, L1r, L1g, L1b);
}

// 4. 最終カラーへの加算
// ライトボリュームのスペキュラは既にサンプリングしたライトの色を含んでいるため、
// メインのディレクショナルライトカラーを掛けずにそのまま加算します。
fd.col.rgb += lvSpecAccum;
```

## 5. 実装のポイント
- **L1方向の活用**: `LightVolumeSpecular` は、R/G/Bそれぞれのチャンネルが持つ「最も強い光の方向」を個別に計算するため、複数の色のライトがある環境で非常に綺麗にボケた反射が得られます。
- **Metallicの設定**: `LightVolumeSpecular` の第3引数 `metallic` に `1.0` を渡すと、`albedo` 引数がそのまま `F0` (垂直入射反射率) として扱われます。これにより、 albedo に反射色を渡すだけで直感的に色が付きます。
- **加算タイミング**: この反射は「間接光のスペキュラ」としての扱いになります。通常、ディレクショナルライトによる直接光のスペキュラは `fd.lightColor` を掛け合わせますが、こちらは既に `L0/L1` の中にライトの強さと色が含まれているため、そのまま足し合わせます。
