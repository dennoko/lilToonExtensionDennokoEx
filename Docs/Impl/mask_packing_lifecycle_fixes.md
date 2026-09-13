# マスクパッキング：ライフサイクル不具合の修正計画

対象：`Editor/NDMF/DennokoExPackMasksPlugin.cs`、`Editor/DennokoExMaskSync.cs`
前提：Unity 2022.3.22f1 / NDMF 1.13.1 / Modular Avatar 導入済み。Play モードでは NDMF（Apply on Play）が動作する。
状態：**置き換えにより終了**。対象ファイルは永続PNG方式への移行で削除した（`mask_packing_alternatives.md` §11）。#1（アニメーション専用Materialの漏れ）はビルドフックのクリップ収集で、#2（プレビューのメモリリーク）はメモリ上Textureの廃止で解消。以下は旧実装の記録。

| # | 優先度 | 概要 | 影響 |
|---|---|---|---|
| 1 | P1 | アニメーションで切り替えるマテリアルが NDMF のベイク対象から漏れる | Play 中・アップロード後にマスクが欠落する |
| 2 | P2 | Domain Reload 前にプレビューテクスチャを破棄しておらず、使われなくなったプレビューも解放しない | エディタのメモリリーク（1枚あたり約22MB） |
| 3 | P2 | マスクの元画像だけを再インポートしてもプレビューが更新されない | エディタ上の見た目とビルド結果が食い違う |

---

## 1. [P1] アニメーション参照マテリアルのベイク漏れ

### 問題
`DennokoExPackMasksPlugin` は `Renderer.sharedMaterials` だけを収集・差し替えている。

- 初期マテリアル A → アニメーションで B に切り替える構成では、**B がベイクされない**（パック未割り当てのため白マスクになる）。
- クリップが A を再指定している場合も、差し替え後のクローン A' ではなく**元の A に戻る**。A はパック未割り当てなのでマスクが消える。
- MA Material Setter / Material Swap は、Transforming 内の `Reactive Components` パスでアニメーションクリップに変換される。このパスは現状のプラグインと実行順序が決まっていないため、同じ漏れが発生し得る。
- 元マテリアルに Editor プレビュー（`HideAndDontSave` テクスチャ）が残っていると、Play 中は見た目が正しく見えて問題に気付きにくい。アップロード時は DontSave オブジェクトへの参照がビルドに入り、欠落するかビルドエラーになる。

### 修正方針
NDMF の `AnimatorServicesContext` を有効にし、Renderer と AnimationClip の両方から DennokoEx マテリアルを集める。**同じ元マテリアルには同じクローン**を割り当てる。

```csharp
protected override void Configure()
{
    InPhase(BuildPhase.Transforming)
        .WithRequiredExtension(typeof(AnimatorServicesContext), s =>
        {
            // MA の Reactive Components（Material Setter/Swap → クリップ化）の後に実行する。
            // MA が未導入でも AfterPlugin の文字列指定は無視されるだけ（MA 自身も同じ書き方をしている）。
            s.AfterPlugin("nadena.dev.modular-avatar")
             .Run("Pack DennokoEx masks", Execute);
        });
}
```

`Execute` の処理：

1. **収集**
   - 候補1：`root.GetComponentsInChildren<Renderer>(true)` の `sharedMaterials`
   - 候補2：`ctx.Extension<AnimatorServicesContext>().AnimationIndex.GetPPtrReferencedObjectsWithBinding` のうち `obj is Material`。binding は `typeof(Renderer).IsAssignableFrom(binding.type)` かつ `propertyName` が `m_Materials.Array.data[` で始まるもの
   - 候補1と候補2を合わせ、DennokoEx シェーダーのものだけ残して重複を除く
2. **クローン表 `Dictionary<Material, Material>` を作る**（元マテリアル1つにつき1回だけ）
   - `NeedsPacking` が真：`Bake(m, forBuild: true)` で焼いたテクスチャをクローンに設定する。
   - `NeedsPacking` が偽でも、`_CustomMaskPacked` に**永続化されていないテクスチャ**（`!EditorUtility.IsPersistent(tex)`、つまり Editor プレビューの残骸）が入っている場合は、クローンを作って `null` を設定する。DontSave テクスチャへの参照をビルドに持ち込まないため。
   - ベイク失敗時（`Bake` が null を返す）も同様に、`null` を設定したクローンを使う（現在の `clones[m] = null` → 元マテリアルをそのまま出荷する挙動をやめる）。
   - 上記のどれにも当たらないもの（マスク無し、かつプレビュー残骸も無し）は差し替えない。
3. **差し替え**
   - Renderer：現状と同じく `sharedMaterials` を置き換える。
   - AnimationClip：`AnimationIndex.RewriteObjectCurves(obj => obj is Material m && map.TryGetValue(m, out var c) ? c : obj)`
     - mapping 関数は null を返してはならない（NDMF 側で例外になる）ので、表に無いものは元のオブジェクトを返す。
     - 書き換える対象は仮想クリップなので、元のクリップアセットは変更されない（非破壊）。コンテキストの無効化時に NDMF がビルド結果へ反映する。
4. `AssetSaver.SaveAsset` はテクスチャとクローンのそれぞれ1回だけ呼ぶ（現状どおり）。

### 補足・未決事項
- TexTransTool などのアトラス化ツールとの前後関係は、今回は決めない（別途検討）。アトラス化が先に動くと、マスクスロットの扱いはツール次第になる。
- Play 中の Apply on Play でも同じ経路を通る。Play 開始が遅い場合は、Play 中だけ圧縮を省く対応を別件で検討する。

### 確認手順
1. 初期 A、Expression メニューのアニメーションで B（マスクが異なる）に切り替えるアバターを用意する。Play と Build & Test のそれぞれで、A/B 両方のマスクが効くこと。
2. A → B → A と戻すクリップでも、戻った後にマスクが効くこと。
3. MA Material Setter でマテリアルを切り替える構成で、1 と同じ結果になること。
4. マスクを外したがプレビュー残骸が残っているマテリアルでアップロードしても、DontSave 関連のエラーが出ないこと。
5. 元のマテリアルアセットとクリップアセットに差分が出ないこと（非破壊であること）。

---

## 2. [P2] Domain Reload 時のプレビューテクスチャのリーク／不要プレビューの蓄積

### 問題
- `HideAndDontSave` の `Texture2D` はネイティブオブジェクトで、Domain Reload でも `UnloadUnusedAssets` でも解放されない。**所有者が明示的に破棄する必要がある**。
- static Dictionary（`_preview` など）は Reload で空になる。Reload 前のテクスチャはマテリアルに割り当てられたまま追跡できなくなる。再ベイク時の `TrySync` も `_preview` に登録されたものしか破棄しないため、**Reload のたびにリークする**。
  - 再コンパイル、および Domain Reload 有効時の Play 開始ごとに、表示中のマスク付きマテリアル1つにつき1枚（2048² RGBA32 のミップ付きで約22MB）。
- `DennokoExMaskSync.cs:99` の「transient textures are gone after a domain reload」というコメントは**誤り**。
- シーンや Prefab Stage を閉じても、Material アセットが生きている限りプレビューを保持し続ける。シーンを行き来するほどプレビューが蓄積する。

### 修正方針

#### 2-a. Reload 前に破棄する
静的コンストラクタで `AssemblyReloadEvents.beforeAssemblyReload += ReleaseAllPreviews;` を登録する。

```csharp
static void ReleaseAllPreviews()
{
    foreach (var tex in _preview.Values)
        if (tex != null) Object.DestroyImmediate(tex);
    _preview.Clear();
    // 以降の状態は Reload で消えるので、明示的なクリアは不要
}
```

- **`SetTexture(null)` は呼ばない**。テクスチャを破棄すると、マテリアル側の参照は Unity の null 判定で null になり、シェーダーは既定の "white" を使う。`SetTexture` を呼ぶと Reload 直前にマテリアルを再び dirty にするため避ける（外部の import/save ループ対策と同じ理由）。
- Reload 後は、既存の `_pendingScan = true` によって表示中のマテリアルが予算内で順次再ベイクされる。
- Domain Reload 有効時の Play 開始でも発火し、Play 中はプレビューが外れる。Play 中は NDMF が焼いたクローンで表示される前提なので問題ない。問題 1 の漏れが Play 中に隠れなくなるという利点もある。
- コメント（:99 と、クラス冒頭の説明）を実態に合わせて修正する。

#### 2-b. 保険：追跡されていない自前テクスチャを回収する
クラッシュ以外で `beforeAssemblyReload` が発火しない経路（例外で途中終了した場合など）に備え、`TrySync` で新しいテクスチャを割り当てる直前に次を行う。

- 現在の `_CustomMaskPacked` が `_preview` に無い、かつ `hideFlags == HideFlags.HideAndDontSave`、かつ名前が `_DnkwPackedMask` で終わる → **自前の孤児**とみなし、新しいテクスチャを割り当てた後に `DestroyImmediate` する。
- 名前とフラグの両方を確認し、他ツールのテクスチャを誤って破棄しないようにする。

#### 2-c. 使われなくなったプレビューを解放する
- `static Dictionary<Material, double> _lastSeen` を追加する。
  - `ScanVisibleMaterials` で**表示中と判定したすべての DennokoEx マテリアル**に現在時刻を記録する。現在は `_sig.ContainsKey` の場合に早期 `continue` しているので、その**前**に記録する。
  - `EnsurePreview`（インスペクターで表示中）でも記録する。
- 自己修復の巡回（1秒ごと）で、`now - _lastSeen[m] > ReleaseGraceSeconds`（案：60秒）のマテリアルは次のように扱う。
  - プレビューテクスチャを `DestroyImmediate` し、`_preview` / `_sig` / `_previewAssignedTick` / `_healApproved` / `_retryAt` / `_failCount` から削除する。ここでも `SetTexture` は呼ばない。
  - **`_healStrikes` と `_healMuteUntil` は残す**。解放 → 再表示 → 再ベイクという経路でループ防止の停止をすり抜けないため。
  - キューに入っている場合は、`DrainQueue` で `_lastSeen` が期限切れならスキップする。
- 解放されたマテリアルが再び表示されたら、スキャンで `_sig` 未登録として検出され、通常どおり予算内で再ベイクされる。
- 猶予を設けることで、非アクティブ化と再アクティブ化を短時間に繰り返しても再ベイクが連発しない。
- 定期スキャンが最長2秒ごとに走るため、表示中のマテリアルが猶予切れで誤って解放されることはない。

### 確認手順
1. マスク付きマテリアルを10個表示した状態で、スクリプトの再コンパイルを20回繰り返す。Profiler（Memory → Detailed → Texture2D の `_DnkwPackedMask`）の枚数が10枚から増えないこと。
2. Domain Reload 有効で Play の開始と終了を繰り返し、1 と同様に増えないこと。終了後にプレビューが復帰すること。
3. マスク付きマテリアルを使うシーンから別シーンへ移動し、60秒以上経ってから枚数が減ること。元のシーンに戻るとプレビューが復帰すること。
4. Reload 前後で `.mat` の git 差分や dirty 状態が増えないこと。
5. ループ防止で停止中のマテリアルを非表示 → 60秒以上経過 → 再表示しても、停止期間中は自動復旧しないこと。

---

## 3. [P2] 元画像の再インポートでプレビューが更新されない

### 問題
外部の画像ソフトでマスク PNG を上書きしても、Material の参照と `_CustomMaskPacked` は変わらないので再ベイクされない。

- `ObjectChangeEvents` は Undo 対象の変更を通知する仕組みで、インポートは通知されない。対象も Material に限定している。
- 定期スキャンは同期済み（`_sig` 登録済み）のマテリアルを除外する。
- 自己修復はパック済みテクスチャの参照が外れたかどうかしか見ない。
- `Signature` の `imageContentsHash` 比較まで処理が到達しない。

結果、エディタ上には古いマスクが表示され、NDMF ベイク後は新しいマスクになるという食い違いが起きる。

### 修正方針
**インポート通知では記録だけを行い、アイドル時に署名で検証する。**

1. `AssetPostprocessor` を追加し、`OnPostprocessAllAssets` ではテクスチャのパスを記録するだけにする。
   ```csharp
   class DennokoExMaskSourceWatcher : AssetPostprocessor
   {
       static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
           => DennokoExMaskSync.NotifySourceAssetsChanged(imported, deleted);
   }
   ```
   - この中では**マテリアルに一切触れない**（`SetTexture` もキューへの投入も行わない）。以前 `OnPostprocessAllAssets` で再ベイクして約0.5秒周期の無限ループになったため。
   - `NotifySourceAssetsChanged` は `HashSet<string> _changedSourcePaths` にパスを追加するだけ。削除されたパスがあれば `_anySourceDeleted = true` にする。拡張子による絞り込みはしない（`AssetDatabase.GetMainAssetTypeAtPath` も呼ばず、処理を最小にする）。
2. `OnUpdate` のアイドル時（`Busy` でないとき）に処理する。
   - `_changedSourcePaths` が空でなければ、`_sig` に登録された追跡中マテリアルそれぞれについて、4つのマスクスロットのテクスチャの `AssetDatabase.GetAssetPath` が集合に含まれるか調べる。含まれるものを `Enqueue` する。
   - `_anySourceDeleted` なら追跡中マテリアルをすべて `Enqueue` する（削除後はパスでは照合できないため）。
   - 処理後に集合とフラグをクリアする。
3. キュー処理は既存の `DrainQueue` → `Sync` → `TrySync` を通す。**署名が一致すれば何もしない**ので、内容が変わっていない再インポートでは `SetTexture` が走らない。
   - プレビューが外れていて、かつループ防止の承認が無いマテリアルは、既存どおり `DrainQueue` でスキップされる（ループ防止を迂回しない）。
4. **署名に `AssetDatabase.GetAssetDependencyHash(path)` を追加する**（アセットパスがあるテクスチャのみ）。
   - `imageContentsHash` は画像内容のハッシュで、sRGB やミップなど**インポート設定だけの変更**を反映しない可能性がある。`GetAssetDependencyHash` は元ファイルと `.meta`（インポート設定）の両方を反映し、ハッシュ取得も軽い。
   - 入力が同じなら再インポートしても値が変わらないので、外部ツールがテクスチャを繰り返し再インポートするループでも再ベイクは起きない。

### ループ安全性の整理
- 再ベイクが起きるのは署名が変わったときだけで、それは元画像か、そのインポート設定が実際に変わったとき。
- 再ベイクで行う `SetTexture` はマテリアルを dirty にするだけで、**テクスチャの再インポートは起こさない**。そのためこの経路自体はループしない。
- マテリアルの保存 → 再インポート → プレビューが外れる、というループは、既存の自己修復のループ防止で引き続き抑止される。

### 不採用案
- **定期的に `imageContentsHash` を監視する**：全追跡マテリアル × 4スロット分を常時計算するのは無駄。インポートが無ければ変わらない値なので、インポート通知をきっかけにすれば十分。

### 確認手順
1. マスク付きマテリアルを表示し、外部ソフトでマスク PNG を上書き保存する。フォーカスを Unity に戻した後、1〜2フレームでプレビューが更新されること。
2. マスクテクスチャのインポート設定（sRGB のオン/オフなど）だけを変えても、プレビューが更新されること。
3. 無関係なアセットの再インポートや、内容が変わらない「Reimport」でプレビューが再ベイクされないこと（テクスチャの instanceID が変わらないこと、マテリアルが新たに dirty にならないこと）。
4. マスクテクスチャを削除すると、プレビューが該当チャンネルを白として再ベイクされること。
5. VRC アップロード中（lilToon が Refresh を繰り返す間）に、Console やインポートのループが発生しないこと。

---

## 実装順序（案）
1. **#2-a / 2-b**：局所的でリスクが低く、メモリへの効果が大きい。
2. **#1**：NDMF 側の変更。アップロード結果が変わるので、確認手順を一通り実施する。
3. **#3**：`AssetPostprocessor` を追加するので、ループの再発確認（#3 確認手順 5）を必ず行う。
4. **#2-c**：解放のタイミング調整を含むので最後に行う。

実装後は `texture_param_limit_and_mask_packing.md` §5.2（プレビューの駆動モデル、回帰確認手順）と §7（既知のトレードオフ）を更新する。
