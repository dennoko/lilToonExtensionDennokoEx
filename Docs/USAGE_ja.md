# DennokoEx 使用方法

DennokoExは、lilToon 2.xを拡張し、追加の表現を可能にするシェーダーバリアントです。
主にスペキュラ（反射）、リムライト、Matcap、ノーマルマップのレイヤーを追加します。

---

## 追加機能の概要

各機能は lilToon の標準設定の下にある **DennokoEx** セクションから設定できます。

### 1. Reflection 2nd (スペキュラ/反射)
2層目の反射（スペキュラ）を追加します。VRC Light Volumesとの連携が特徴です。

- **Enabled**: 反射を有効にします。
- **Mask**: 反射を適用する範囲を指定するマスクです。
- **Color**: 反射の色。
- **Strength**: 反射の強度。
- **Smoothness**: 反射の鋭さ。値を上げるとハイライトが小さく鋭くなります。
- **Blur**: ハイライトの輪郭をぼかします。0に近づけると、トゥーン調のパキッとしたハイライトになります。
- **LV Color Strength**: VRC Light Volumes 使用時、ライトの色をどの程度反映させるか。
- **Main Color Strength**: 反射の色にメインテクスチャの色をどの程度乗せるか。
- **Shadow Attenuation**: 影の部分で反射をどの程度減衰させるか。
- **Anisotropic Mode (Kajiya-Kay)**:
  髪の毛のような光沢を表現する異方性反射モードです。
  - **Shift**: ハイライトの位置を法線方向にずらします。
  - **Secondary**: 2本目のハイライトの設定（色、強度、位置）が可能です。

### 2. Rim 2nd (リムライト)
2層目のリムライトを追加します。

- **Power**: リムライトの広がりを制御します。
- **Blur**: リムの境界のぼかし具合。
- **Blend Mode**:
  - **Rim Light (Add)**: 通常の光るリムライト。
  - **Rim Shade (Multiply)**: 逆光側の影を強調するような表現（影色）。

### 3. Matcap 3rd (マットキャップ)
3層目のMatcapを追加します。金属感や環境光の追加に使用します。

- **Blend Mode**: 加算(Add)、乗算(Multiply)、スクリーン(Screen)から選択可能です。
- **Shadow Attenuation**: 影の部分でMatcapを暗くします。

### 4. Normal Map 3rd (ノーマルマップ)
3層目のノーマルマップを追加します。細かなディテールを重ねるのに便利です。

- **Strength**: 凹凸の強さを調整します。


