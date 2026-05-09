デカール化のロジックは主に `lil_common_functions.hlsl` の `lilCalcDecalUV` と `lilGetSubTex` に実装されています。

---

## 全体の流れ

`lilGetSubTex` がエントリポイントです。`LIL_FEATURE_DECAL` が有効な場合、通常の `lilCalcUV` の代わりに `lilCalcDecalUV` を呼び出します。 [1](#0-0) 

---

## `lilCalcDecalUV` の座標計算（ステップ順） [2](#0-1) 

処理は以下の順で行われます：

### 1. Copy（左右コピー）
```hlsl
if(shouldCopy) outUV.x = abs(outUV.x - 0.5) + 0.5;
```
UV の x 座標を `0.5` を軸に折り返すことで、左半分（x < 0.5）を右半分（x > 0.5）と同じ内容にします。

### 2. Scale & Offset（タイリング・オフセット）
```hlsl
outUV = outUV * uv_ST.xy + uv_ST.zw;
```
`uv_ST.xy` がスケール（Tiling）、`uv_ST.zw` がオフセット（Offset）です。通常のUnityテクスチャ変換と同じ形式です。

### 3. Flip（反転）
```hlsl
if(shouldFlipCopy && uv.x < 0.5) outUV.x = 1.0 - outUV.x;
if(shouldFlipMirror && isRightHand)  outUV.x = 1.0 - outUV.x;
```
- `shouldFlipCopy`：コピーされた側（左半分）のみ x を反転
- `shouldFlipMirror`：右手側のみ x を反転（ミラー対称デカール用）

### 4. Hide（非表示化）
```hlsl
if(isLeftOnly  && isRightHand)  outUV.x = -1.0;
if(isRightOnly && !isRightHand) outUV.x = -1.0;
```
`outUV.x = -1.0` という範囲外の値を代入することで、後述の `lilIsIn0to1` チェックで透明になります。

### 5. Rotate（回転）
```hlsl
outUV = (outUV - uv_ST.zw) / uv_ST.xy;  // オフセット・スケールを一時的に除去
outUV = lilRotateUV(outUV, angle);        // 中心(0.5, 0.5)を軸に回転
outUV = outUV * uv_ST.xy + uv_ST.zw;    // スケール・オフセットを再適用
```
回転はオフセット空間ではなく「テクスチャ中心」を軸に行うため、一度 Scale & Offset を逆算してから回転し、再適用しています。

`lilRotateUV` の内部は：
```hlsl
outuv = uv - 0.5;
outuv = float2(outuv.x * cos - outuv.y * sin,
               outuv.x * sin + outuv.y * cos);
outuv += 0.5;
``` [3](#0-2) 

---

## アニメーション付きオーバーロード

`uv_SR`（ScrollRotate）パラメータを受け取るオーバーロードでは、時間に応じてオフセットと角度を変化させます：

```hlsl
float4 uv_ST2 = uv_ST + float4(0, 0, uv_SR.xy) * LIL_TIME;  // オフセットをスクロール
float  angle2 = uv_SR.z + uv_SR.w * LIL_TIME;               // 角度を回転アニメーション
``` [4](#0-3) 

`uv_SR` の各成分の意味：
| 成分 | 意味 |
|------|------|
| `.xy` | スクロール速度（U, V） |
| `.z` | 初期角度 |
| `.w` | 回転速度（rad/s） |

---

## デカールの「はみ出し透明化」

`isDecal = true` のとき、UV が `[0, 1]` の範囲外に出た部分のアルファを 0 にします：

```hlsl
if(isDecal) outCol.a *= lilIsIn0to1(uv2, saturate(nv - 0.05));
``` [5](#0-4) 

`lilIsIn0to1(uv2, nv)` は UV が `[0,1]×[0,1]` の内側なら `1`、外側なら `0` を返します。第2引数の `saturate(nv - 0.05)` は法線と視線の内積（NdotV）で、真横から見たときにエッジが消えるのを防ぐフェードです。

---

## アトラスアニメーション

`LIL_FEATURE_ANIMATE_DECAL` が有効な場合、`lilCalcAtlasAnimation` でスプライトシートのフレームを切り出します：

```hlsl
uint animTime = (decalAnimation.w == 0) 
    ? (uint)decalAnimation.z                              // 固定フレーム
    : (uint)(LIL_TIME * decalAnimation.w) % (uint)decalAnimation.z; // 時間アニメ
uint offsetX = animTime % (uint)decalAnimation.x;
uint offsetY = animTime / (uint)decalAnimation.x;
outuv = (outuv + float2(offsetX, offsetY)) * decalSubParam.xy / decalAnimation.xy;
``` [6](#0-5) 

`decalAnimation` の各成分：
| 成分 | 意味 |
|------|------|
| `.x` | 横方向のフレーム数 |
| `.y` | 縦方向のフレーム数 |
| `.z` | 総フレーム数 |
| `.w` | FPS（0なら固定フレーム） |

### Citations

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L424-435)
```text
float2 lilRotateUV(float2 uv, float angle)
{
    float si,co;
    sincos(angle, si, co);
    float2 outuv = uv - 0.5;
    outuv = float2(
        outuv.x * co - outuv.y * si,
        outuv.x * si + outuv.y * co
    );
    outuv += 0.5;
    return outuv;
}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L473-506)
```text
float2 lilCalcDecalUV(
    float2 uv,
    float4 uv_ST,
    float angle,
    bool isLeftOnly,
    bool isRightOnly,
    bool shouldCopy,
    bool shouldFlipMirror,
    bool shouldFlipCopy,
    bool isRightHand)
{
    float2 outUV = uv;

    // Copy
    if(shouldCopy) outUV.x = abs(outUV.x - 0.5) + 0.5;

    // Scale & Offset
    outUV = outUV * uv_ST.xy + uv_ST.zw;

    // Flip
    if(shouldFlipCopy && uv.x<0.5) outUV.x = 1.0 - outUV.x;
    if(shouldFlipMirror && isRightHand) outUV.x = 1.0 - outUV.x;

    // Hide
    if(isLeftOnly && isRightHand) outUV.x = -1.0;
    if(isRightOnly && !isRightHand) outUV.x = -1.0;

    // Rotate
    outUV = (outUV - uv_ST.zw) / uv_ST.xy;
    outUV = lilRotateUV(outUV, angle);
    outUV = outUV * uv_ST.xy + uv_ST.zw;

    return outUV;
}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L519-520)
```text
    float4 uv_ST2 = uv_ST + float4(0,0,uv_SR.xy) * LIL_TIME;
    float angle2 = uv_SR.z+ uv_SR.w * LIL_TIME;
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L533-542)
```text
float2 lilCalcAtlasAnimation(float2 uv, float4 decalAnimation, float4 decalSubParam)
{
    float2 outuv = lerp(float2(uv.x, 1.0-uv.y), 0.5, decalSubParam.z);
    uint animTime = decalAnimation.w == 0.0 ? (uint)decalAnimation.z : (uint)(LIL_TIME * decalAnimation.w) % (uint)decalAnimation.z;
    uint offsetX = animTime % (uint)decalAnimation.x;
    uint offsetY = animTime / (uint)decalAnimation.x;
    outuv = (outuv + float2(offsetX,offsetY)) * decalSubParam.xy / decalAnimation.xy;
    outuv.y = 1.0-outuv.y;
    return outuv;
}
```

**File:** Assets/lilToon/Shader/Includes/lil_common_functions.hlsl (L733-744)
```text
    #if defined(LIL_FEATURE_DECAL)
        float4 uv_SR2 = float4(uv_SR.xy, angle, uv_SR.w);
        float2 uv2 = lilCalcDecalUV(uv, uv_ST, uv_SR2, isLeftOnly, isRightOnly, shouldCopy, shouldFlipMirror, shouldFlipCopy, isRightHand);
        #if defined(LIL_FEATURE_ANIMATE_DECAL)
            float2 uv2samp = lilCalcAtlasAnimation(uv2, decalAnimation, decalSubParam);
        #else
            float2 uv2samp = uv2;
        #endif
        float4 outCol = LIL_SAMPLE_2D(tex,samp,uv2samp);
        if(isMSDF) outCol = float4(1.0, 1.0, 1.0, lilMSDF(outCol.rgb));
        if(isDecal) outCol.a *= lilIsIn0to1(uv2, saturate(nv-0.05));
        return outCol;
```
