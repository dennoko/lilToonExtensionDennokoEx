VRCLVのスペキュラーを実装するには、以下の手順で行います。

## 1. インクルード

フラグメントシェーダーの前に以下を記述します（`UnityCG.cginc` は必ず先に）:

```hlsl
#include "UnityCG.cginc"
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

## 2. SHデータの取得

`LightVolumeSH()` でSH成分を取得します。これはLVが無い場合はUnityのLight Probeに自動フォールバックします。 [1](#0-0) 

```hlsl
float3 L0, L1r, L1g, L1b;
LightVolumeSH(worldPos, L0, L1r, L1g, L1b);
```

## 3. スペキュラーの計算

用途に応じて2つの関数があります:

### `LightVolumeSpecular()` — アバター向け（RGB各方向のスペキュラー） [2](#0-1) 

```hlsl
float3 specular = LightVolumeSpecular(albedo, smoothness, metallic, worldNormal, viewDir, L0, L1r, L1g, L1b);
```

### `LightVolumeSpecularDominant()` — 静的PBRサーフェス向け（主方向1色、軽量） [3](#0-2) 

```hlsl
float3 specular = LightVolumeSpecularDominant(albedo, smoothness, metallic, worldNormal, viewDir, L0, L1r, L1g, L1b);
```

## 4. 最終カラーへの加算

**重要**: スペキュラー結果はアルベドで乗算せず、そのまま加算します。 [4](#0-3) 

```hlsl
finalColor += specular; // albedoで掛けないこと
```

## 実装例（フラグメントシェーダー全体の流れ）

```hlsl
// フラグメントシェーダー内
float3 L0, L1r, L1g, L1b;
LightVolumeSH(i.worldPos, L0, L1r, L1g, L1b);

// ディフューズ（アルベドを掛けて加算）
float3 diffuse = LightVolumeEvaluate(worldNormal, L0, L1r, L1g, L1b);
finalColor += diffuse * albedo;

// スペキュラー（そのまま加算）
float3 specular = LightVolumeSpecular(albedo, smoothness, metallic, worldNormal, viewDir, L0, L1r, L1g, L1b);
finalColor += specular;
```

実際の動作例はサンプルシェーダー [5](#0-4)  を参照してください。

---

**関数の引数まとめ:**

| 引数 | 説明 |
|---|---|
| `albedo` | アルベドカラー |
| `smoothness` | スムースネス値 |
| `metallic` | メタリック値 |
| `worldNormal` | ワールド空間の法線（正規化済み） |
| `viewDir` | ワールド空間のカメラ方向（正規化済み） |
| `L0, L1r, L1g, L1b` | `LightVolumeSH()` から取得したSH成分 | [6](#0-5)

### Citations

**File:** Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc (L858-862)
```text
// Calculates speculars for light volumes or any SH L1 data
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04f, albedo, metallic);
    return LightVolumeSpecular(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}
```

**File:** Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc (L880-884)
```text
// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04f, albedo, metallic);
    return LightVolumeSpecularDominant(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}
```

**File:** Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc (L892-901)
```text
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    if (_UdonLightVolumeEnabled == 0) {
        LV_SampleLightProbeDering(L0, L1r, L1g, L1b);
    } else {
        float4 occlusion = 1;
        LV_LightVolumeSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b, occlusion);
        LV_PointLightVolumeSH(worldPos, occlusion, L0, L1r, L1g, L1b);
    }
}
```

**File:** Documentation/ForShaderDevelopers.md (L76-78)
```markdown
Add the result straight to your final fragment color.

These functions already apply albedo internally **do not multiply again**. You can still apply your own specular occlusion/masking if needed.
```

**File:** Documentation/ForShaderDevelopers.md (L157-175)
```markdown
### float3 LightVolumeSpecular()
Calculates approximated speculars based on SH components. Can be used with Light Volumes or even with any other SH L1 values, like Unity default light probes. The result should be added to the final color, just like emission. You should NOT multiply this by albedo color!

Usually works much better for avatars, because can show several color speculars at the same time for each of R, G, B light directions. Slightly less performant than LightVolumeSpecularDominant()

```hlsl
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b)
```

| Function argument | Description |
| --- | --- |
|`float3 albedo` | Final albedo color.|
|`float smoothness` | Final surface smoothness.|
|`float metallic` | Final surface metalness.|
|`float3 worldNormal` | World normal of the current fragment. Must be normalized to avoid artefacts.|
|`float3 viewDir` | World space camera view direction. Must be normalized.|
|`out float3 L0` | Outputs ambient color of the current fragment.|
|`out float3 L1r` <br/> `out float3 L1g` <br/> `out float3 L1b` | Outputs vectors that stores the Red, Green and Blue light directions and power, as a magnitude of these vectors.|

```

**File:** Packages/red.sim.lightvolumes/Shaders/ASE Shaders/Light Volume PBR.shader (L181-215)
```text
			float3 localLightVolumeSpecular157_g220 = LightVolumeSpecular( albedo157_g220 , smoothness157_g220 , metallic157_g220 , worldNormal157_g220 , viewDir157_g220 , L0157_g220 , L1r157_g220 , L1g157_g220 , L1b157_g220 );
			float3 temp_output_138_0_g221 = Albedo337;
			float3 albedo158_g221 = temp_output_138_0_g221;
			float temp_output_3_0_g221 = Smoothness109;
			float smoothness158_g221 = temp_output_3_0_g221;
			float temp_output_137_0_g221 = Metallic334;
			float metallic158_g221 = temp_output_137_0_g221;
			float3 temp_output_2_0_g221 = World_Normal112;
			float3 worldNormal158_g221 = temp_output_2_0_g221;
			float3 temp_output_9_0_g221 = ase_viewDirSafeWS;
			float3 viewDir158_g221 = temp_output_9_0_g221;
			float3 temp_output_65_0_g221 = L098;
			float3 L0158_g221 = temp_output_65_0_g221;
			float3 temp_output_1_0_g221 = L1r99;
			float3 L1r158_g221 = temp_output_1_0_g221;
			float3 temp_output_36_0_g221 = L1g100;
			float3 L1g158_g221 = temp_output_36_0_g221;
			float3 temp_output_37_0_g221 = L1b101;
			float3 L1b158_g221 = temp_output_37_0_g221;
			float3 localLightVolumeSpecularDominant158_g221 = LightVolumeSpecularDominant( albedo158_g221 , smoothness158_g221 , metallic158_g221 , worldNormal158_g221 , viewDir158_g221 , L0158_g221 , L1r158_g221 , L1g158_g221 , L1b158_g221 );
			#ifdef _DOMINANTDIRSPECULARS_ON
				float3 staticSwitch410 = localLightVolumeSpecularDominant158_g221;
			#else
				float3 staticSwitch410 = localLightVolumeSpecular157_g220;
			#endif
			float lerpResult57 = lerp( 1.0 , tex2DNode50.g , _OcclusionStrength);
			float AO357 = lerpResult57;
			float3 Speculars412 = ( staticSwitch410 * AO357 );
			#ifdef _SPECULARS_ON
				float3 staticSwitch361 = ( temp_output_406_0 + Speculars412 );
			#else
				float3 staticSwitch361 = temp_output_406_0;
			#endif
			float3 IndirectAndSpeculars444 = ( staticSwitch361 * AO357 );
			float3 Emission452 = ( ( _EmissionColor.rgb * tex2D( _EmissionMap, uv_MainTex ).rgb ) + IndirectAndSpeculars444 );
```
