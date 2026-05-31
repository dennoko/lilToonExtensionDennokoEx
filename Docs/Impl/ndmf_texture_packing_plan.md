# DennokoEx 非破壊テクスチャパッキング（NDMF）実装計画

## 1. 概要
シェーダーのサンプリング負荷を削減するため、NDMFビルドプロセスに介入し、個別に設定されたマスクテクスチャを自動的に1枚のRGBAテクスチャにパッキング（統合）します。
「ユーザーがInspectorで切り替える」必要はなく、エディタ上では従来通り個別のテクスチャでプレビューでき、アップロード時に自動で最適化される仕組みとします。

## 2. チャンネル割り当て計画

統合先のテクスチャ（例: `_DennokoExPackedMask`）のRGBAチャンネルに、以下のマスクを割り当てます。

| チャンネル | 対象のマスクプロパティ | 参照するUV | 備考 |
| :--- | :--- | :--- | :--- |
| **R** | `_CustomRefl2ndMaskTex` | `fd.uv0` | 第2反射マスク |
| **G** | `_CustomRim2ndMaskTex` | `fd.uv0` | 第2リムマスク |
| **B** | `_CustomMatcap3rdMaskTex` | `fd.uv0` | 第3マットキャップマスク |
| **A** | `_CustomNormal3rdMaskTex` | `fd.uv0` | 第3ノーマルマスク |

※ `_CustomBump1stMaskTex` はUV参照が `fd.uvMain` と異なるため、今回のパッキング対象からは除外（または別途考慮）します。

## 3. 実装のステップ

### Step 1: シェーダー (`custom.hlsl`) の改修
ビルド時にNDMFから有効化されるキーワード（例: `DENNOKOEX_PACKED_MASKS`）を定義し、有効時は統合テクスチャを1回だけサンプリングするように変更します。

```hlsl
// custom.hlsl 内の各機能ブロックにて
#if defined(DENNOKOEX_PACKED_MASKS)
    // 最初のパッキング対象機能が呼ばれるタイミングでサンプリングし、変数を保持
    float4 _DennokoExPackedMaskVal = LIL_SAMPLE_2D(_DennokoExPackedMask, sampler_linear_repeat, fd.uv0);
#endif

// 例えば反射のマスク取得部：
#if defined(DENNOKOEX_PACKED_MASKS)
    float _r2Mask = _DennokoExPackedMaskVal.r;
#else
    float _r2Mask = LIL_SAMPLE_2D(_CustomRefl2ndMaskTex, sampler_linear_repeat, _r2MaskUV).r;
#endif
```

### Step 2: NDMF パス（Editorスクリプト）の作成
`Editor` フォルダ内に、NDMFの `Plugin` として動作する C# スクリプトを作成します。

**処理の流れ:**
1. アバター内のすべての `Renderer` から、`DennokoEx` が適用されているマテリアルを抽出。
2. 対象マテリアルから個別のマスクテクスチャ（R, G, B, A 用）を取得。
3. `Texture2D` （または `RenderTexture`）を用いて、各テクスチャの値を読み取り、1枚のRGBAテクスチャにベイク。
4. ベイクしたテクスチャをアバターの一時ディレクトリ（NDMF管理下）に保存。
5. マテリアルの `_DennokoExPackedMask` にベイク済みテクスチャをセットし、`DENNOKOEX_PACKED_MASKS` キーワードを有効化（またはフラグをON）。

### Step 3: Inspector (`DennokoExInspector.cs`) の整理
ユーザー向けには引き続き個別のスロットを表示しますが、内部で使用する `_DennokoExPackedMask` などのプロパティはInspector上で非表示（HideInInspector）にしておきます。

## 4. 懸念事項と確認事項
*   **UVの共通化**: パッキングすると、すべてのマスクは同じUVスケール/オフセット（基本的に `uv0`）を参照することになります。個別の Tiling/Offset (`_ST`) は無効化される前提で進めますが問題ないでしょうか？
*   **テクスチャが未指定の場合**: 未指定のマスクスロットは「真っ白（1.0）」または「真っ黒（0.0）」としてパッキング時に処理します。
