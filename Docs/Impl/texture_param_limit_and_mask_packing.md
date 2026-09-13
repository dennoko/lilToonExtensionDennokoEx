# テクスチャパラメータ64上限 と マスクRGBAパッキング（NDMF + エディタプレビュー）

lilToonカスタムシェーダー（DennokoEx）で発生した「VRCアップロード時にアバターが透明（不可視）になる」バグの根本原因と、その恒久対策の実装知見をまとめる。**他のlilToon系カスタムシェーダー／拡張プロジェクトでも再利用できる**よう、原因の仕組み・診断手順・対策の設計判断を一般化して記述する。

---

## 1. 症状

- VRChatアップロード（Build & Publish）を行うと、**シーンビュー上でアバター（の一部マテリアル）が透明・不可視**になる。
- Consoleに `State comes from an incompatible keyword space`（`UnityEngine.GUIUtility:ProcessEvent`）が**多数**出る。
- lilToonの **Refresh Shaders**（`Assets/lilToon/[Shader] Refresh shaders`）を実行すると一時的に直る。
- **編集中（アップロード前）は正常**で、アップロードを境に壊れる。

これらは別々のバグに見えるが、**単一の根本原因の主症状＋二次症状**である。

---

## 2. 根本原因：テクスチャパラメータ数が GPU の 64 上限を超える

決定的な証拠は、アップロード時にConsoleへ一瞬出る次の1行：

```
Shader 'Hidden/<your>/Transparent...' uses 65 texture parameters,
more than the 64 supported by the current graphics device.
```

### 2.1 なぜアップロード時だけ起きるか

lilToonはVRCアップロードの後処理（`SetShaderSettingAfterBuild` → `TurnOnAllShaderSetting`）で、シェーダーを**全機能ON**の状態で再コンパイルする（Consoleに巨大な `#define LIL_FEATURE_*` 一覧が出る）。

- 通常の編集中は、マテリアルが実際に使う機能だけが有効化されるので、宣言される **TEXTURE2D の総数は 64 未満**に収まっている。
- アップロード後の「全機能ON」では、lilToon標準の全テクスチャ + カスタムシェーダーが追加したテクスチャ が同時に宣言され、**透明パスが 65 に到達**する。
- 64を超えたパスは**コンパイル/ロードに失敗**する。透明モードのマテリアルは描画できず、結果として**透明・不可視**に見える。

### 2.2 「incompatible keyword space」は二次症状

上限超過で壊れたシェーダーがビルドGUIのキーワード検証に絡み、`State comes from an incompatible keyword space` を大量に吐く。**この警告を消そうと `shader_feature_local` を追加しても無意味**（原因はキーワード宣言ではない）。

### 2.3 「16サンプラー上限」とは別物

lilToonカスタムシェーダーでよく語られる「ps_4_0 の 16 サンプラー上限」（共有サンプラー `sampler_linear_repeat` で回避するやつ）とは**別の制限**。今回の上限は **テクスチャ“パラメータ”（宣言テクスチャ）数の 64**。サンプラーを共有していてもテクスチャ宣言ごとに1パラメータ消費するため、サンプラー対策とは独立して効いてくる。

> 重要：**64上限はHLSLで宣言・サンプリングされるテクスチャ数**で決まる。ShaderlabのProperties側に書いてあってもHLSLで使わなければカウントされない（後述の設計で利用する）。

---

## 3. 診断手順（再利用可）

「アップロード時だけ壊れる」「Refreshで直る」は一過性で、静止状態のスキャンでは捕まらない。以下の順で確実に切り分ける。

1. **Console を見る**。`uses NN texture parameters, more than the 64 supported` が出ていれば確定。これが本命。
2. キーワード警告だけに惑わされない。`incompatible keyword space` は症状であって原因ではない。
3. 一過性を捕まえたい場合は、**アップロード前後でマテリアル状態をスナップショットして差分**を取る（本リポジトリ `Editor/DennokoExKeywordDiagnostics.cs` の Begin/End Upload Capture が実装例）。「マテリアル状態は不変なのにシェーダーが壊れている」＝シェーダー側（テクスチャ数 or コンパイル失敗）が原因、と判定できる。
4. カスタムシェーダーが追加したテクスチャ枚数を数える（`LIL_CUSTOM_TEXTURES` の `TEXTURE2D(...)` 個数）。lilToonは全機能ONで上限ギリギリなので、**カスタム追加分が数枚でも超過し得る**。

---

## 4. 対策の設計判断

### 4.1 採用しなかった案と理由

- **シェーダーキーワードで「軽量バリアント」を作る**（編集時は個別テクスチャ、ビルド時だけパック）
  → ✗ **使えない**。アップロードの「全機能ON」再コンパイルは**全バリアントをコンパイル**するため、重い（個別テクスチャ）バリアントも必ずコンパイルされ、65超過が再発する。**“重いバリアントを一切作らない（無条件で上限内）”しか成立しない。**
- **機能（テクスチャ）を削る**
  → 上限は割れるが機能が減る。今回は不採用。

### 4.2 採用案：単チャンネルマスクの RGBA パッキング（機能維持）

複数の**単チャンネルマスク**（`.r` だけ使うマスク類）を **1枚のRGBAテクスチャの各チャンネル**に集約する。

- HLSLで宣言するテクスチャは「パック1枚」に減る → **宣言テクスチャ数が減る**（例：マスク4枚→1枚で −3）。
- **各マスクは引き続き個別のUV/タイリングでサンプリング**できる（パック1枚を複数回サンプリングするだけ。**サンプル回数は上限に無関係**、テクスチャ“枚数”だけが上限対象）。→ **タイリング等の機能を完全維持**。
- 個別マスクのスロットは **Properties には残す**（オーサリングUX維持）が、**HLSLではサンプリングしない**ので上限にカウントされない。

#### シェーダー実装の要点

```hlsl
// 宣言は「パック1枚」だけ（個別マスクは LIL_CUSTOM_TEXTURES から外す）
#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_CustomMaskPacked); \
    /* ...他の必須テクスチャ... */

// 各マスクは「同じパック」を「自分のUV/チャンネル」でサンプリング
// R = マスクA / G = マスクB / B = マスクC / A = マスクD
float maskA = LIL_SAMPLE_2D(_CustomMaskPacked, sampler_linear_repeat, uvA * _MaskA_ST.xy + _MaskA_ST.zw).r;
float maskB = LIL_SAMPLE_2D(_CustomMaskPacked, sampler_linear_repeat, uvB).g;
```

- パックは **1:1（UV変換なし）でベイク**する。タイリング/オフセットは**ランタイム側のサンプリングで適用**する（`_MaskA_ST` を残す）。こうすればパック後もタイリングが効く。
- 別UV空間のマスク（例：デカールUVのマスク）は uv0 系と混ぜられないので**パック対象から除外**して個別テクスチャのまま残す（その分の枚数は別途上限内に収める）。

#### チャンネル割当は1箇所に集約

`custom.hlsl` / パッキング用シェーダー / ベイクスクリプト の **3者で割当（R/G/B/A→どのマスク）を必ず一致**させる。本リポジトリでは「R=Refl2nd / G=Rim2nd / B=Normal3rd / A=Normal1st」。

---

## 5. パックドテクスチャをどう用意するか（2経路）

「シェーダーは `_CustomMaskPacked` しか見ない」状態にしたうえで、**そのテクスチャを誰が埋めるか**を2経路で用意する。両者は**同じベイク関数**を使い、見た目を一致させる。

### 5.1 アップロード（実機）：NDMF ビルド時ベイク（非破壊）

- NDMFの `Plugin<T>` を実装し、`BuildPhase.Transforming` でアバターの各レンダラーを走査。
- 対象マテリアルを**クローン**し（`new Material(m)`）、個別マスクから焼いた1枚を `_CustomMaskPacked` にセットして差し替える。
- 生成テクスチャとクローンを `ctx.AssetSaver.SaveAsset(...)` で保存。**元マテリアルは変更しない（非破壊）**。
- lilToonの最適化はVRCプリプロセス `callbackOrder = 100`。NDMFの `Transforming` はそれより前に走るので、**lilToonが見る前にパックを差し込める**。
- **実機マスクはNDMF必須**（エディタのインメモリ・プレビューはアップロードに乗らないため）。NDMF未導入環境では `versionDefine`(`DENNOKOEX_HAS_NDMF`)＋`defineConstraints` でアセンブリごと無効化し、**壊さない**（その場合マスクは焼かれず白）。

NDMF 1.12 系の最小実装：

```csharp
[assembly: ExportsPlugin(typeof(MyPackPlugin))]
public class MyPackPlugin : Plugin<MyPackPlugin> {
    public override string QualifiedName => "yourname.pack";
    public override string DisplayName   => "Mask Packer";
    protected override void Configure() {
        InPhase(BuildPhase.Transforming).Run("Pack masks", ctx => {
            foreach (var r in ctx.AvatarRootObject.GetComponentsInChildren<Renderer>(true)) {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) {
                    var m = mats[i];
                    if (/* DennokoExマテリアル かつ マスクあり */) {
                        var packed = MaskPacker.Bake(m);
                        var clone = new Material(m);
                        clone.SetTexture("_CustomMaskPacked", packed);
                        ctx.AssetSaver.SaveAsset(packed);
                        ctx.AssetSaver.SaveAsset(clone);
                        mats[i] = clone;
                    }
                }
                r.sharedMaterials = mats;
            }
        });
    }
}
```

> オプショナル依存にするための asmdef 設定：
> `versionDefines: [{ "name": "nadena.dev.ndmf", "expression": "", "define": "DENNOKOEX_HAS_NDMF" }]`
> `defineConstraints: ["DENNOKOEX_HAS_NDMF"]`、`references: ["...Editor", "nadena.dev.ndmf"]`。
> NDMFが無い環境では参照未解決でもアセンブリごとコンパイル対象外になるので壊れない。

### 5.2 エディタプレビュー：インメモリ・ベイク（ディスク非書込）

問題：シェーダーが `_CustomMaskPacked` しか見ないため、ビルド前のエディタでは**マスクがプレビューされない**（パックが白のまま）。

解決：`[InitializeOnLoad]` のエディタ専用クラスで、**インメモリの `HideAndDontSave` テクスチャ**を焼いて `_CustomMaskPacked` に割り当てる。**ディスクの `.mat` には書き込まない**ので git は汚れない。

再ベイクの駆動は **`EditorApplication.update` の「アイドル時のみ」** に集約する（ループ耐性＋自己修復）：
- **(a) 表示中マテリアルのみ遅延ベイク**：`InitializeOnLoad` で走査を予約し、アイドル時に**ロード中シーンとPrefab Stageの「activeInHierarchy かつ Renderer.enabled」なレンダラー**だけを走査する（非アクティブな枝は打ち切り）。未同期のマテリアルだけをキューに積み、**1フレームあたり約8ms（最低1件）の予算**で順次ベイクする（インメモリ品はリロードで破棄されるため毎回再生成）。非表示の衣装などは起動時に焼かず、アクティブ化された時点で焼く。以前は読み込み済みシーンの全マテリアル（非アクティブ含む）を1フレームで焼いており、プロジェクトを開いた直後に長く操作を待たせていた。**Busy中は実行せず待つ**ので、リロード直後にインポート中でも取りこぼさない。
- **(b) スロットル付き自己修復（例:1秒毎）**：アップロード/エクスポート/シェーダー再インポートの後処理で、マテリアルの `_CustomMaskPacked`（インメモリ割当）は **nullに戻される**。これを検知して**外れたものだけ再ベイク**する。「previewしたマテリアル」だけを巡回するので安価。→ **アップロード・エクスポート後に自動でプレビューが復帰する**。
- **(c) インスペクターでマスク変更時**：`EditorGUI.BeginChangeCheck/EndChangeCheck` で囲み、変更時に対象マテリアルを即再ベイク（1秒待たず即反映）。
- **(d) 表示状態の変化時（プレハブ配置/複製、SetActive、Renderer有効化、マテリアル差し替えなど）**：`EditorApplication.hierarchyChanged`・`ObjectChangeEvents.changesPublished`・`EditorSceneManager.sceneOpened`・`PrefabStage.prefabStageOpened`・`Undo.undoRedoPerformed` で (a) の走査を再予約する（最短0.25秒間隔に間引き）。走査は未同期マテリアルしか積まず署名も計算しないので、発火が多くても安価。新たに表示されたマテリアルのプレビューを反映する。`SetTexture` はヒエラルキーを変えないので再発火＝ループにならない。Timeline/Animationプレビューによる表示切替はイベントに出ない場合があるため、下記の定期走査で補完する。
- **(e) インスペクターの手動リフレッシュボタン**：署名キャッシュを破棄して**無条件に再ベイク**する `ForceSync` を呼ぶ。自動復帰が間に合わない／何か食い違ったときの保険。

復旧処理の補足：
- Undo/Redo と Edit モード復帰時には、マスク未指定（署名 `none`）を含む同期済みマテリアルをキューで再評価する。マテリアルのプロパティ変更通知でも対象を再評価する。署名は残して比較するため、入力が変わっていない通知では再ベイクしない。
- 表示状態はイベントに加えて最長2秒間隔でも走査する。Animation/Timeline などの通知されない表示切替も、表示状態が次の走査まで続けば未同期マテリアルを検出する。通常の走査では同期済みマテリアルの署名を計算しない。
- ベイクが `null` を返すか例外になった場合は同期済みにせず、5秒から倍々に延ばして最長60秒間隔で再試行する。ログは失敗が続く間マテリアルごとに1回だけ出す（パッカー用シェーダー欠落のエラーもエディタプレビューでは1回、ビルド時は毎回）。失敗したベイクは何も割り当てていないので自己修復の連続クリア回数をリセットし、外部の import/save ループと誤判定して停止しない。既存のプレビューは代替ベイクが成功するまで保持する。
- 自己修復ではテクスチャ自体の破棄も検出する。自己修復は「外れたプレビューの検出・ループ防止の判定・回数の加算」だけを行い、承認したマテリアルを初回ベイクと同じキューに積んで**1フレームあたりの予算内で復旧する**（アップロード後に全プレビューが一斉に外れても1フレームで全件焼かない）。キューで待機中の承認済みマテリアルは二重に数えない。変更通知経由の再評価では、自己修復の承認がない外れたプレビューを復旧せず、連続クリアのループ防止を迂回しない。
- Play モード中とアップロード用のベイクは NDMF が担当する。Busy 中の手動リフレッシュはプレビューを破棄せず、Edit モードのアイドル復帰後に処理する。

回帰確認手順（Unity Editor で実施）：
1. マスクを全解除してマテリアル Inspector を閉じ、Undo する。手動リフレッシュなしで元のマスクが再生成されること。
2. マスク設定を変更し Undo/Redo を繰り返す。入力が戻り、無関係な Undo ではベイク済みテクスチャが置き換わらないこと。
3. パッカー用シェーダーが利用できない状態で初回ベイクを試み、シェーダーを復旧する。エラーと失敗警告が1回ずつしか出ず、「外部 import/save ループ」の警告も出ないこと。ヒエラルキー操作なしで最長60秒以内に再試行されること。
4. ベイク済み `_CustomMaskPacked` のテクスチャを `DestroyImmediate` する。割当とキャッシュがともに Unity の null 判定でも復旧すること。
5. 割当だけを自己修復の直後に連続してクリアする。変更通知や Undo があっても4回目で60秒停止し、その後に復旧すること。
6. NDMF 有効で Play の開始・終了、VRCSDK のビルドを行う。Domain Reload 有効・無効の両方で Edit モードのプレビューが復帰すること。
7. マスク付きマテリアルを多数表示した状態でアップロード（またはシェーダー再インポート）する。復帰時にエディタが長時間止まらず、プレビューが数フレームかけて順次復旧すること。

> ⚠️ **`AssetPostprocessor.OnPostprocessAllAssets` で再ベイクしてはいけない（無限ループの罠）**。
> VRCアップロードはlilToonが `AssetDatabase.Refresh()` を何度も呼び、`.unitypackage` インポートも大量のアセットを再インポートする。これらに反応して `SetTexture`（マテリアル変更）を行うと、それがさらに再インポート/リフレッシュを誘発し、**約0.5秒周期でロードと通常状態が往復し続ける無限ループ**になる（プレビューも完了しない）。アップロード/エクスポート後の復帰は、上記 (b) の**「update＋アイドル時の自己修復」**で実現すること（インポートに反応させない）。`SetTexture` はアセットDBを変更しないので (b) はループしない。

無駄焼き防止：各マスクの `Texture.imageContentsHash`（＋InstanceID）から**署名**を作り、署名一致かつプレビュー品が割当済みなら何もしない。これで再描画/巡回のたびに焼かない。

実行抑止ガード（必須）：`EditorApplication.isCompiling / isUpdating / isPlayingOrWillChangePlaymode` または `BuildPipeline.isBuildingPlayer` のいずれかが真なら**ベイクを行わずに即return**（再スケジュールもしない＝インポート/ビルド中にスピンしない）。ビルド時の実体反映はNDMF側が担うので、エディタプレビューはビルド中に動く必要がない。アイドルに戻れば (a)(b) が自然に復帰させる。

経路の整理：

| 場面 | `_CustomMaskPacked` を埋める主体 | 永続性 |
|---|---|---|
| エディタプレビュー | インメモリ・ベイク（`HideAndDontSave`） | ディスク非書込（白のまま保存） |
| VRCアップロード | NDMFプラグイン（クローンに焼く） | ビルド成果物にのみ含む |

---

## 6. ベイクの実装メモ（色空間・解像度）

- ベイクは **ブリットシェーダー**（4テクスチャの `.r` を `float4(r,g,b,a)` に出力）→ `RenderTexture`(Linear) → `ReadPixels` → `Texture2D(RGBA32, linear:true)` で行う（`Read/Write` 不要）。
- **色空間の忠実性**：ソースをGPUサンプリングし（sRGBインポートなら自動でリニア化）、**リニアRTに書き** → 生成テクスチャを **`linear:true`** で作る → ランタイムシェーダーは `_CustomMaskPacked` を**リニアとして**サンプリング。これで「パック前に個別マスクを直接サンプリングしていた値」と一致する。**`linear:false` だと二重sRGB変換になり値が狂う**ので注意。
- **タイリングはベイクで焼かない**（1:1）。ランタイムの `_ST` サンプリングで適用する（§4.2）。
- 解像度はソースの最大辺を2の冪に丸めてクランプ（例 4〜2048）。全スロット未指定なら**焼かずデフォルト白**に任せる。
- プレビューとビルドは**同一のベイク関数**を共有して見た目を一致させる。

---

## 7. 既知のトレードオフ / 注意

- **エディタプレビューはマテリアルをメモリ上で dirty にする**（`SetTexture`）。プロジェクト保存時に `non-persistent object` 警告や軽微なgit差分（nullを書き戻す）が出る可能性がある。気になる場合は**隠しサブアセット方式**（`AssetDatabase.AddObjectToAsset` でパックを `.mat` 内サブアセットとして永続化＝git差分は出るがNDMF無しでも実機反映可）に切り替える。本リポジトリは「git非汚染」を優先しインメモリ方式を採用。
- **実機マスクはNDMF必須**。配布時は `package.json` の依存にNDMFを明記するか、READMEで必須化を案内する。
- リロード毎の全マテリアル走査はプロジェクト規模が大きいとリロードを重くする。署名比較で**焼くのは変更分のみ**にしてコストを抑える。
- チャンネル割当（R/G/B/A）の三者一致を崩すと**サイレントに別マスクが適用**される。変更時は3ファイル同時に直す。

---

## 8. 他プロジェクトへの移植チェックリスト

1. [ ] アップロード時 Console に `uses NN texture parameters, more than the 64` が出るか確認（出るなら本対策が有効）。
2. [ ] カスタムが追加した `TEXTURE2D` のうち、**単チャンネルかつ同一UV系**で束ねられるマスクを洗い出す。
3. [ ] それらを `LIL_CUSTOM_TEXTURES` から外し、**1枚のRGBAパック**を宣言。各サンプリング箇所を `パック.チャンネル` に置換（個別UV/`_ST`は維持）。
4. [ ] 個別スロットは **Properties に残す**（HLSL非サンプリング＝上限非カウント）。パックは `[HideInInspector]` + デフォルト `"white"`。
5. [ ] **ベイク関数**（ブリット＋ReadPixels、リニア忠実）を1つ実装し、NDMFとエディタプレビューで共有。
6. [ ] **NDMFプラグイン**（Transformingでクローンに焼く）を optional 依存で実装。
7. [ ] **エディタプレビュー**（InitializeOnLoadで1回 + インスペクター変更検知 + 署名）を実装。**AssetPostprocessorでインポートに反応させない**（無限ループ防止）。`isCompiling/isUpdating/isPlaying/isBuildingPlayer` ガードを必ず入れる。
8. [ ] パック後の全機能ON時テクスチャ数が **64 に十分な余裕**（数枚マージン）を持って収まるか再確認（将来のlilToon更新で増える前提）。

---

## 9. 本リポジトリでの該当ファイル

- シェーダー：`Shaders/custom.hlsl`（`_CustomMaskPacked` 宣言＋4箇所のチャンネルサンプリング）、`Shaders/lilCustomShaderProperties.lilblock`（`_CustomMaskPacked` 宣言、個別スロット残置）
- ベイク：`Shaders/DennokoEx_MaskPacker.shader`、`Editor/DennokoExMaskPacker.cs`
- アップロード：`Editor/NDMF/DennokoExPackMasksPlugin.cs`、`Editor/NDMF/DennokoEx.NDMF.Editor.asmdef`
- エディタプレビュー：`Editor/DennokoExMaskSync.cs`、インスペクター側フック `Editor/DennokoExInspector.cs`
- 診断：`Editor/DennokoExKeywordDiagnostics.cs`（キーワード空間スキャン／アップロード前後キャプチャ）

チャンネル割当（本リポジトリ）：**R = Reflection 2nd / G = Rim 2nd / B = Normal 3rd / A = Normal 1st**。デカールの配置マスクは別UV空間のためパック対象外（個別テクスチャのまま）。
