# マスクパッキング実装の代替案検討

更新日: 2026-09-13。ステータス: **案A（外部PNG・入力指紋名の不変ファイル）を採用し実装済み**。実装内容は §11。Unity上の動作検証（§9.1）は未実施。

現行の「NDMFビルド時ベイク＋一時Textureによる編集プレビュー」を見直し、テクスチャパラメータ数の制限回避と「白黒マスクを各スロットに設定するだけ」の操作感を維持する方針を整理する。NDMF依存は基本機能から外すことを目指すが、ビルド中に入力を変更するツールへの対応には利用を許容する。

現行コード、既存設計資料、Unity 2022.3のAPIを確認した。Unity上での保存・再インポート・実ビルド試験、性能測定は未実施。以下の採用条件と検証項目は、確認済みの動作保証ではない。

## 1. 結論と採用判断

**RGBAパッキングを維持し、生成物を永続化して編集・Play・出荷で共用する方針を推奨する。** 大きく変えるべき箇所は、パッキング形式より生成物の所有者と寿命である。

永続化先には、次の二つを有力候補として残す。

- **外部PNG:** 圧縮・プラットフォーム別設定・ミップストリーミングをTextureImporterに任せることを優先する場合の第一候補。
- **Materialのサブアセット:** 生成物の所有者をMaterialに固定し、保存先・命名・孤立ファイル管理を減らすことを優先する場合の有力候補。PC中心で既存の圧縮・ストリーミング処理を維持できるなら、先に試作する価値がある。

サブアセット案を「フォルダーを増やせない場合だけの妥協案」とは扱わない。一方、永続化だけで更新検知・Undo・複製・出荷設定まで自動解決すると考えない。採用方式は §9 の試作で決める。

## 2. 制約と現行方式の問題

### 2.1 回避する制限

- 今回の問題はサンプリング命令数ではなく、環境で観測された **64テクスチャパラメータ上限**。16サンプラー制限は共有サンプラーによって別途対策済み。
- 64をGPU全般の一律の上限とは表現しない。Unityも、例えばD3D11ではテクスチャ数とサンプラー数を別の制限として説明している。[Unity: Texture samplers](https://docs.unity.cn/6000.1/Documentation/Manual/SL-SamplerStates.html)
- ローカルのlilToonは `SetShaderSettingAfterBuild` で `TurnOnAllShaderSetting` を呼ぶ。通常編集中だけでなく、アップロード後の全機能有効状態でも各パスが上限内に収まる必要がある。
- 本提案ではHLSLのマスク宣言を `_CustomMaskPacked` 1枚に保ち、個別スロットをオーサリング用Propertiesとして残す。
- 現行チャンネルは **R=Reflection 2nd、G=Rim 2nd、B=Normal 3rd、A=Main Color 4th**。過去資料のNormal 1st／Matcap 3rd割り当てと混同しない。
- 各チャンネルをそれぞれのUVでサンプルできる。UVが異なることだけを理由にパッキング不可能とはしない。解像度・フィルター・Wrap・ミップ等の共有条件は別に評価する。

### 2.2 複雑さの中心

現在はMaterialの個別スロット、一時的な編集用Texture、NDMFが生成する出荷用TextureとMaterialクローンを管理している。保存されるMaterialに一時Textureを割り当てるため、所有権と保存状態が一致していない。

| 現行処理 | 永続化による変化 |
| --- | --- |
| 一時Textureの所有・解放、リロード後の再構築 | 保存済みアセットの参照を利用する |
| 参照消失のself-heal・連続クリアのミュート | 通常経路から除去し、欠落時の修復に限定する |
| 表示中Rendererの走査 | 表示状態をベイク条件にしない |
| 編集用と出荷用の別ベイク | 同じ保存済み生成物を使用する |
| パッキングのためのMaterial・アニメーション参照置換 | 保存済みMaterialをそのまま使うケースでは不要 |
| 元画像更新・失敗処理 | 引き続き必要。入力変更と明示的な失敗状態に整理する |

`HideAndDontSave`がDomain Reloadで必ず破棄される、または絶対にビルドへ混入しないとは仮定しない。現行NDMFプラグインはRendererの参照しか収集せず、クリップだけに出現するMaterialや参照置換の取りこぼしがある。詳細は [ライフサイクルレビュー](mask_lifecycle_review_and_fix_plan.md) を参照。

## 3. 永続化方式に共通する設計

### 3.1 正とするデータと生成の指紋

個別スロットの元画像を正とし、packed Textureを再生成可能な派生データとする。Shaderとユーザー向けスロットは維持する。元画像やスロットを破壊せず、生成参照をMaterialへ保存する設計変更を受け入れる。

ベイク入力の指紋には以下を含める。

- 各スロットのTextureのGUIDとローカルID、未指定の識別。
- 入力画像のインポート結果の変更を検出する依存ハッシュ。
- 出力解像度・色空間等の生成条件とパッカーバージョン。
- 出力に影響する場合のみビルドターゲットや関連設定。

**Material全体の依存ハッシュは使わない。** packed Textureも依存に含まれて循環し得るため、入力を明示的に列挙する。現行同様、UV変換や強度を描画時に適用するなら、その変更は再ベイク不要。

### 3.2 更新の入口

| 操作・変更 | 処理 |
| --- | --- |
| スロット変更・解除、ペースト、Undo／Redo | 対象Materialの入力を再評価 |
| 外部ツールによるMaterial変更・Material再インポート | 入力を再評価 |
| 元画像の内容・Import設定変更、移動・削除 | 利用Materialを再評価 |
| 生成物・生成参照の欠落 | 存在と所有情報を検査し、修復または明示的エラー |
| Scene／Prefab開閉、Domain Reload、Play移行 | 保存済み生成物を使用。未完了要求があれば再評価 |
| ビルド前 | 出荷対象の入力と生成物の一致を最終検査 |

アセット通知では要求をキューに積み、インポート処理の外でまとめて評価する。未ロード・非アクティブ・クリップ専用Materialも更新対象になるため、「読み込み済みMaterialだけを走査すれば完了」としない。利用関係の索引や変更アセットからの収集が必要で、索引は再構築可能な補助情報とする。未保存変更はInspectorやUndo通知でも扱う。

同じ入力かつ正常な生成物なら何も書かない。出力を入力に循環させず、同じ内容の再保存を避ける。生成フォルダーの除外だけでループ防止が完了するとは考えない。外部ツールが入力を変更し続ける場合まで収束を保証しない。[Unity: Asset Database Refresh](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetDatabaseRefreshing.html)

### 3.3 更新・保存・失敗

- 入力検証、生成、出力検証を終えてから生成参照や成功時の指紋を更新する。
- 失敗時は前回の表示を保持して原因を示す。前回の生成物を最新として記録しない。
- 必須マスクの不整合が解決できないビルドは停止する。白マスクや古い生成物で成功扱いにしない。
- 入力のUndoに応じて派生データを整合させる。自動更新のたびにUndo項目を追加しない。
- 毎回の無条件な `SaveAssets()` は避ける。対象Materialの保存でもユーザーの他の未保存編集を含み得るため、通常の保存操作との関係を設計する。
- 失敗時の後始末や再試行は残るが、参照消失を常時自己修復する状態機械は持たない。

## 4. 案A: 外部PNG＋TextureImporter

### 4.1 構成

生成PNGを管理フォルダーへ保存し、Materialから通常の永続参照を持つ。リニア値で出力し、TextureImporterを `sRGBTexture=false`、入力Alpha使用、`alphaIsTransparency=false`、ミップ有効、Read/Write無効、ストリーミング有効に設定する。元画像のsRGB変換を含め、現行と同じ値になることを検証する。

圧縮はPCでBC7等を明示設定する。DXT5は独立したRGBマスクの品質を損ね得るため避けるが、BC7にも量子化誤差はある。プラットフォーム別形式の選択はImporterに委ねられる。Android向け形式を設定できることと、このカスタムシェーダーがVRChat Androidで使用できることは別問題であり、本提案は後者を保証しない。

通常のTextureImporterを利用するため、現行の `Compress`／`ConfigureForStreaming` を通常経路から外せる。[TextureImporter.streamingMipmaps](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/TextureImporter-streamingMipmaps.html)

### 4.2 ファイルの所有方法

| 方法 | 利点 | 注意点 |
| --- | --- | --- |
| Materialごとに所有する固定出力 | 所有関係が単純。入力画像更新で参照を交換せずに済む | 複製時の所有者分離が必要。重複排除なし |
| 入力指紋を名前にした不変出力 | 同じ入力を共有でき、過去の出力を残せる | 入力の各改訂でファイルが増える。参照更新・未使用判定が必要 |

初期実装は所有者を固定する方法を基本候補とし、共有による効果が必要なら指紋方式を採用する。指紋方式を初めから必須とはしない。

指紋方式でもファイル名一致だけでは正常と判定しない。存在、読込可能なTexture、生成バージョン、Importer設定等を確認する。過去のファイルはUndoや別アセットから参照され得るため、更新直後に自動削除しない。

### 4.3 利点と代償

- 標準Importerによる出荷設定が最大の利点。Materialのファイルサイズも小さく保てる。
- 生成PNGと `.meta` は保存・配布対象。エクスポートでは依存関係を含め、別プロジェクトで確認する。
- 生成先・所有情報・欠落修復・掃除を管理するコードが必要。
- 画像更新時にはGPUベイク、PNGエンコード、インポート、圧縮の待ち時間がある。具体的な時間や実装行数は未測定であり、断定しない。

## 5. 案B: Materialのサブアセット

### 5.1 構成と適用範囲

`AssetDatabase.AddObjectToAsset` で生成Texture2DをMaterialファイルへ追加し、`_CustomMaskPacked`から参照する。

```text
Example.mat
  Material
    個別マスク → 元画像
    _CustomMaskPacked → 内包Texture2D
  Texture2D（生成済みRGBAマスク）
```

生成マスクが同じファイルに収まるため、保存先・命名・孤立した外部ファイルの管理を減らせる。元画像、Shader、スクリプト等は外部依存のままであり、Material一つで編集環境すべてが完結するわけではない。

対象は書き込み可能な独立Materialアセットに限定する。FBX等のインポート結果や一時Materialへ一律に追加しない。Unity 2022.3の `AddObjectToAsset` の説明は追加先を `.asset` と記載し、インポートされたモデル等への追加ではデータ消失を注意している。したがって `.mat` への内包は、この説明だけで保証済みとはせず、実プロジェクトの保存・再インポート試験で確認する。[AddObjectToAsset](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetDatabase.AddObjectToAsset.html)

### 5.2 所有と更新

- 原則として1 Materialが1枚を所有する。他Materialから参照すること自体は可能だが、共有すると独立した所有・削除の利点が薄れるため、初期設計では共有しない。
- 更新前に参照Textureが対象Materialの所有物かを確認する。名前やHideFlagsだけで所有者と判定しない。
- 毎回削除・追加するとfileIDやUndo参照が不安定になる。別の一時Textureで生成を完了させ、既存サブアセットの同一性を保って更新する方式を優先して検証する。解像度・圧縮形式変更も検証対象。
- `new Material(original)` はTexture参照を引き継ぐ。複製先の入力変更で元の内包Textureを上書きしないよう、所有者が異なれば複製先用の生成物を用意する。
- Project上のファイル複製、`CopyAsset`、`new Material`＋`CreateAsset`、Material Variantを分けて確認する。
- Undoは入力を戻して派生画像を再生成することを基本とし、巨大な画素データを毎操作記録しない。初回作成・参照割り当て・全解除のUndoは別途整合を取る。
- Editor専用の生成メタデータを保存する場合、スクリプト欠落時の扱いとビルドへの混入も確認する。

### 5.3 圧縮・ストリーミング

内包Texture2DにはTextureImporterがない。通常PNGのようなプラットフォーム別Import設定は利用できず、圧縮形式の選択・再生成を自前で担当する。

Unity 2022.3では、ミップストリーミングの有効化はTextureImporter経由とされている。現行のSerializedObjectによる内部フィールド操作は、内包しただけでは不要にならない。保存・再ロード後と実機で検証し、専用処理へ隔離する。[Texture2D.streamingMipmaps](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D-streamingMipmaps.html)

| 保存・圧縮方法 | 評価 |
| --- | --- |
| 編集時に圧縮済みTextureを保存 | 同一ターゲットなら編集・出荷で共用しやすい。編集時の圧縮待ちと再更新方法が課題 |
| 未圧縮で保存し、ビルド用クローンを圧縮 | 編集時の圧縮を省けるが、ビルド用変換・参照置換が残る |
| ターゲット切替時に保存内容を変える | 保存済みMaterialに環境依存差分が発生するため、基本案にしない |

「サブアセットなら非圧縮1024以下にする必要がある」とはしない。解像度を落とすのは画質要件の変更であり、元のUX・品質を維持する提案とは分ける。

### 5.4 保存サイズと差分

本プロジェクトはForce Text（`m_SerializationMode: 2`）。生成TextureがMaterialファイルに入るため、保存サイズ・保存時間・バージョン管理への影響を測定する必要がある。

2048×2048、全ミップの画素ペイロード概算はRGBA32で約21.33 MiB、BC7で約5.33 MiB。仮に全画素が16進テキストとして格納されれば文字数は概ね2倍になるが、実際の保存形式・メタデータ・ファイルサイズはUnityで確認する。これを実測 `.mat` サイズとして扱わない。

画素が変わらないMaterialの色変更でも、大きいファイルの保存I/Oが負担になり得る。一方、**保存しただけで毎回巨大なgit差分が出るとは限らない**。通常プロパティ変更と再ベイクを分けて、保存時間・差分・競合時の扱いを評価する。

## 6. ScriptedImporterと他の方式

### 6.1 レシピをScriptedImporterでTextureへ変換

元画像の参照と設定を記述した `.dnkwmask` 等を導入し、ImporterがTexture2Dを生成する案。`DependsOnArtifact` で入力のインポート結果に依存でき、画像更新時の再生成をUnityに委ねられる。[DependsOnArtifact](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetImporters.AssetImportContext.DependsOnArtifact.html)

ただし、Materialの個別スロットを正とするならレシピとの同期が残り、レシピを正とするなら外部ツールや既存スロットとの互換性を再設計する必要がある。生成Textureは通常のTextureImporterを通らず、圧縮・ストリーミング設定も自動解決しない。

依存は「入力画像 → レシピ → 生成Texture」とする。「Material → レシピ → Texture → Material」という循環は避け、Materialへの割り当てをインポート処理外へ分離する。GPUベイクをImport Worker・バッチ環境で実行可能かも未検証。新規の専用形式には魅力があるが、今回の第一候補にはしない。

### 6.2 その他

| 案 | 評価 |
| --- | --- |
| Texture2DArray | テクスチャパラメータ削減が可能。独立マスクの圧縮品質等には検討価値があるが、サイズ・形式統一、生成・更新管理は残る。ライフサイクル問題の直接解決にはならない |
| 編集時は個別、ビルド時だけpacked | 個別側のシェーダーが上限を超えるなら成立しない。編集用として分離しただけでは保証にならない |
| 機能別キーワード・パス整理 | 不要な宣言を除けるなら有効。ただし必要な全機能を同時利用する構成も上限内であることが条件 |
| lilToonの全機能ONを内部フラグから推定 | 内部構造に依存し、状態によって見た目が変わるため推奨しない |
| 機能削減・ユーザーによる事前パック | 今回の機能・操作感の維持条件を満たさない |
| 現行方式を継続して修正 | 移行までの修正として有効だが、一時状態の管理自体は残る |

Texture2DArrayの同一サイズ・形式等の要件は [Unity: Texture arrays](https://docs.unity3d.com/ru/current/Manual/class-Texture2DArray.html) を参照。

## 7. NDMFとビルドの役割

**保存済み生成物をそのまま使う通常ケースでは、NDMFを必須にしない。** Material A／Bの両方が整合したpacked参照を持っていれば、アニメーションやMAによる単なるMaterial切替はそのまま成立する。

ただし、以下は別経路として扱う。

- ビルド中にMaterialや元マスクが生成・変更される。
- 読み取り専用Materialに生成参照を書き込めない。
- サブアセットを未圧縮で保存し、出荷時だけ圧縮する。
- 実行中に元マスクTextureそのものを差し替える。静的パックだけでは自動追従しない。

ビルド中の変換が必要なら、NDMFアダプターで同じベイク処理を呼び、クローン・Renderer／アニメーション参照置換を行う。単にSDKコールバックへ移しても、この複雑さはなくならない。実行中のTexture差し替えはビルド時変換だけでは解決せず、別の仕様・対応が必要。

SDK側には出荷対象の入力と生成物を検査する薄い処理を置く。NDMFやMAによる入力変更後、かつlilToon等がその結果を必要とする前に処理する順序を確認する。「callbackOrderを100未満にすれば常に十分」とは決めない。SDK・NDMFへの依存はアダプター層へ分離する。

ビルド開始時の自動修復が元Materialの保存を伴う場合、明示的な編集時更新と区別する。非破壊ビルドを守るケースでは、元アセットを直接更新せずクローン経路または不整合エラーを選ぶ。

## 8. 比較

| 項目 | 現行の一時プレビュー | 外部PNG | Materialサブアセット | ScriptedImporter |
| --- | --- | --- | --- | --- |
| リロードを越えた生成物 | 独自の所有・復旧管理 | 永続参照 | 永続参照 | インポート成果物 |
| 生成物の配置・所有 | Editorとビルドで別 | 別ファイルの管理が必要 | Materialに集約 | レシピに集約 |
| 入力変更の検知 | 自前 | 自前 | 自前 | 画像依存はUnityへ委譲、Material同期は残る |
| 圧縮・ストリーミング | 自前 | 標準Importer | 自前 | 自前 |
| 通常ケースのNDMF | 現実装で利用 | 不要にできる | 不要にできる | 不要にできる |
| ビルド中の入力変更 | 収集・順序の改善が必要 | アダプターが必要 | アダプターが必要 | アダプターが必要 |
| Materialの保存サイズ | 小さい | 小さい | 画素データを含む | 小さい |
| 生成物共有 | 実装次第 | 指紋方式なら可能 | 可能だが所有が複雑化 | 同じレシピを共有可能 |
| 主な未解決点 | 一時状態と参照の管理 | 配置・更新・配布 | 保存・更新・出荷設定 | レシピ同期と生成環境 |

## 9. 試作・検証と移行

### 9.1 採用前の小規模試作

同じ入力と解像度で、外部PNGとサブアセットの両方を生成する。既存の `MaskSync` と同じMaterialを同時に管理させず、試作用Materialで比較する。

| 検証 | 合格条件・判断材料 |
| --- | --- |
| RGBA・中間階調・空スロット・UV | 現行との値・見た目の差を把握。未指定は白。UV／強度変更で不要な再ベイクなし |
| 保存・再起動・Domain Reload | 生成参照が残り、同じ入力で再ベイク・再保存しない |
| 全再インポート・lilToonアップロード後処理 | packed参照と内容が維持される |
| 元画像・sRGB・最大サイズの変更 | 未ロードやクリップ専用Materialを含め必要な対象を更新 |
| Undo／Redo・全解除 | 入力と生成物が整合し、Missingや不要なUndo項目が生じない |
| ファイル複製・コード複製・Variant | 複製先の編集で元の生成物を上書きしない |
| サブアセットのサイズ・形式変更 | 参照の同一性、保存・再ロード、旧データの解放を確認 |
| 生成失敗・画像削除・生成物欠落 | 前回表示を保持してエラーを示し、不整合のまま出荷しない |
| 通常保存と再ベイクの性能 | 待ち時間、ファイルサイズ、git差分を別々に測定 |
| 圧縮とストリーミング | 保存・再ロード・実ビルド後の形式、Read/Write、実機動作を確認 |
| Material切替・ビルド中の生成 | Rendererとクリップの全参照が整合。元アセットの不要な変更なし |
| エクスポート・別プロジェクト | 必要な依存を同梱し、NDMFなしの通常ケースも確認 |

サブアセットで保存・出荷設定と更新待ちが許容できれば、所有管理の単純さを理由に採用できる。標準Importerの利点が大きい、または内包保存の性能・互換性に問題がある場合は外部PNGを採用する。

### 9.2 移行順序

1. ベイク処理を、入力取得・画素生成・永続化・出荷設定に分離する。
2. 試作で永続化先と圧縮タイミングを決め、入力の指紋と所有管理を実装する。
3. 更新通知、Undo、未完了処理の再評価、ビルド検査を追加する。
4. 既存Materialを移行する。Inspectorで開いたものだけで完了扱いにせず、一括移行と対象参照の検査を用意する。
5. 永続化済みMaterialを現行プレビュー管理から外し、所有が確認できる一時Textureを安全に解放する。
6. 通常経路の `MaskSync` を撤去し、NDMFは必要な例外アダプターだけ残す。圧縮・ストリーミング処理の削除可否は採用方式に応じて決める。
7. 手動操作は「プレビュー復旧」から診断・再生成へ整理する。関連資料・Inspector・翻訳を更新する。

実装前に現行コードを一括削除しない。「200行程度になる」「NDMF一式を無条件に削除できる」「永続化すれば全ケースでループしない」といった未検証の見積もりは採用根拠にしない。

## 10. 関連コード・資料

- [DennokoExMaskPacker.cs](../../Editor/DennokoExMaskPacker.cs)
- [DennokoExPackedMaskStore.cs](../../Editor/DennokoExPackedMaskStore.cs)
- [DennokoExPackedMaskWatcher.cs](../../Editor/DennokoExPackedMaskWatcher.cs)
- [DennokoExPackedMaskBuildHook.cs](../../Editor/VRCSDK/DennokoExPackedMaskBuildHook.cs)
- [DennokoExInspector.cs](../../Editor/DennokoExInspector.cs)
- 削除済み: `Editor/DennokoExMaskSync.cs`、`Editor/NDMF/`（git履歴を参照）
- [custom.hlsl](../../Shaders/custom.hlsl)
- [DennokoEx_MaskPacker.shader](../../Shaders/DennokoEx_MaskPacker.shader)
- [現行方式の背景](texture_param_limit_and_mask_packing.md) — 歴史的記録。チャンネル割り当て等は現行コードを優先する。
- [ライフサイクルレビューと修正方針](mask_lifecycle_review_and_fix_plan.md)
- プロジェクトルート基準: `Packages/jp.lilxyzw.liltoon/Editor/lilToonSetting.cs`、`ProjectSettings/EditorSettings.asset`

## 11. 採用した実装

### 11.1 選定理由

保守性と動作安定性を基準に **案A（外部PNG）** を採用した。

- 圧縮（PC: BC7、Android/iOS: ASTC 6x6）・ミップ・ストリーミング・Read/Write無効をTextureImporterに任せられ、`EditorUtility.CompressTexture` と SerializedObject による内部フィールド操作（`m_StreamingMipmaps` 等）を削除できた。案Bではこの自前処理が残る。
- Materialファイルに画素データが入らず、Force Textでの保存I/O・差分の懸念がない。
- エディタ表示とアップロードで同じアセットを使うため、「プレビューは正しいが出荷物が違う」種類の不具合が構造上起きない。

所有方法は §4.2 の **入力指紋名の不変ファイル** を選んだ。固定出力方式は複製Material間の上書き分離が必要になるが、指紋方式は「同じ入力なら同じファイル・入力が変われば別ファイル」で、複製・Undo・共有入力を追加の所有管理なしに整合させられる。代償の未使用ファイル増加は、手動の削除メニューで扱う（自動削除はしない）。

### 11.2 構成

| ファイル | 役割 |
| --- | --- |
| `DennokoExMaskPacker` | 4スロットをGPUでRGBAにパックしPNGバイト列を返すだけ。永続化を知らない |
| `DennokoExPackedMaskStore` | `EnsureAll(materials, persist, rebake)`。指紋計算、PNG書き出し・インポート、生成PNGのImport設定（差分時のみ再インポート）、参照割り当て。保守メニュー |
| `DennokoExPackedMaskWatcher` | いつ `EnsureAll` を呼ぶか（`OnPostprocessAllAssets` のみ） |

> ⚠️ 生成PNGのImport設定を `AssetPostprocessor.OnPreprocessTexture` で行ってはいけない。テクスチャ用ポストプロセッサーの登録（と `GetVersion`）は全テクスチャのインポート依存に含まれるため、導入・更新のたびにプロジェクト内の全テクスチャが再インポートされ、大規模プロジェクトで数十分かかった。
| `VRCSDK/DennokoExPackedMaskBuildHook` | アップロード前の最終整合（`com.vrchat.avatars` 導入時のみコンパイル） |

生成先は `Assets/DennokoEx_Generated/PackedMasks/<指紋>.png`。拡張本体のフォルダー外に置き、配布パッケージへ混入させない。

### 11.3 指紋と冪等性

- 指紋 = `"i" + Hash128(パッカーVersion; 各スロットの GUID:ローカルID:GetAssetDependencyHash | 空)`。依存ハッシュが元画像の内容とImport設定（sRGB、最大サイズ等）の変更を表す。Material自身の依存ハッシュは循環するため使わない。
- スロットに保存されていないTexture（ビルド中に他ツールが生成した物等）がある場合のみ、ベイク結果のバイト列のハッシュ（`"o"` 接頭辞）を名前にする。
- `EnsureAll` は「期待ファイルが存在しない（または rebake）なら書く」「参照が期待と異なるなら割り当てる」だけを行う。同じ入力での再呼び出しは何も書かないため、どのトリガーから何度呼ばれても最大1回の書き込みで収束する。現行の自己修復・ループガード・再試行バックオフ・表示中Renderer走査・フレーム予算はすべて不要になり削除した。
- 参照が変わった独立 `.mat` のみ `SaveAssetIfDirty` で即保存する（ユーザーの無関係な未保存Materialは保存しない）。自分の保存による再インポートは参照一致で no-op になる。モデル内蔵・イミュータブルパッケージ・メモリ上のMaterialは割り当てのみ。
- 生成失敗時は前回の参照を残してエラーを出す。自動再試行はせず、次の実際の変更か手動の再生成ボタンを待つ。

### 11.4 トリガー

| 契機 | 処理 |
| --- | --- |
| Domain Reload、Scene／Prefab Stageを開いた時 | 読み込み済みSceneとPrefab Stageの全Renderer（非アクティブ含む）のMaterialをキュー。旧一時プレビューからの自動移行も兼ねる |
| GameObject生成（Prefab配置・貼り付け）、Rendererのプロパティ変更（Material差し替え） | 該当RendererのMaterialをキュー |
| Inspector描画 | スロット参照（インスタンスID）が前回確認時から変わった時だけキュー。開いた時、編集・ペースト・Undo、DennokoExへの切替を網羅 |
| `ObjectChangeEvents` のMaterialプロパティ変更 | キュー |
| Material格納ファイル（`.mat`／`.asset`／モデル）のインポート | DennokoExシェーダーフォルダーへの依存を `GetDependencies` で確認し、該当ファイルだけ `LoadAllAssetsAtPath` でファイル内の全Materialをキュー（VCS更新・外部ツール・自分の保存・モデル再インポート）。全体インポート時に全モデル・`.asset` を読み込まないため |
| 元画像のインポート | DennokoExシェーダーに依存し、かつ変更画像に依存する保存済みファイルを `GetDependencies` で探し、ファイル内の全Materialをキュー。加えて、過去に確認したメモリ上Material（Scene埋め込み・スクリプト生成クローン）のうち、そのパスをスロットに持つ物をキュー |
| 画像・生成物の削除 | 依存が辿れないため、DennokoExシェーダーを使う保存済みMaterialと、確認済みメモリ上Materialをすべて再確認（no-opが大半） |
| VRChatアバターのアップロード | callbackOrder 0（NDMF/MAの後、lilToon 100の前）で Renderer とアニメーションクリップ参照のMaterialを `EnsureAll(persist:false)`。失敗時はアップロードを中止 |

キューは `EditorApplication.update` で、コンパイル・インポート・Play移行中を避けて1回ずつ処理する。アセット通知の中では何も書かない。

§2.2 で指摘したアニメーション専用Materialの取りこぼしは、ビルドフックがクリップの参照も収集することで解消した。NDMFクローン等のメモリ上Materialもその場で割り当てるため、NDMFプラグインは削除した（NDMF依存なし）。

### 11.5 手動操作

- Inspector「パック済みマスクを再生成」: 選択Materialのファイルを再ベイクして上書き。
- `Window > DennokoEx > Packed Masks > Update All Materials`: 既存Materialの一括移行・点検。
- `Window > DennokoEx > Packed Masks > Delete Unused Generated Masks`: 保存済みMaterialから参照されない生成PNGを確認付きで削除。

### 11.6 未検証事項

コンパイルはUnityのアセンブリ参照を用いた外部ビルドで確認したのみ。§9.1 の項目、特に以下はUnity上で確認が必要。

- 旧プレビュー値との一致（PNG 8bitリニア保存 → sRGB off インポート）。
- BC7/ASTC圧縮後の見た目、ストリーミング設定がアップロード後に有効か。
- lilToonアップロード後処理・全再インポート後に参照が保持されること。
- 大規模プロジェクトで、元画像インポート時の `GetDependencies` 走査にかかる時間。
- 既存Materialに保存されている旧一時Textureの参照（Missing）が `Update All Materials` で置き換わること。
