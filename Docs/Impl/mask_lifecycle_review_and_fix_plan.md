# マスク生成のライフサイクルレビューと修正方針

作成日: 2026-09-13。対象コミット: `875ee75`。ステータス: **置き換えにより終了**。対象コードは永続PNG方式への移行で削除した（[mask_packing_alternatives.md §11](mask_packing_alternatives.md)）。以下は旧実装の記録。

対象は `Editor/DennokoExMaskSync.cs`、`Editor/DennokoExMaskPacker.cs`、`Editor/NDMF/DennokoExPackMasksPlugin.cs`、`Editor/DennokoExInspector.cs` とパック用・描画用シェーダー。Unity 2022.3.22f1、NDMF 1.13.1、Modular Avatar (MA) 1.17.1 のローカルコードを確認した。Play モードでは NDMF が実行される前提とする。

Unity を操作した再現試験、メモリ測定、圧縮時間測定、実ビルドは行っていない。「確度: 高」はコード上の原因を確認した意味であり、発生頻度・具体的なログまで実測した意味ではない。行番号は対象コミット時点のもの。

## 提示されたレビューの評価

| 番号 | 指摘 | 評価・確度 | 対応優先度 |
| --- | --- | --- | --- |
| 1 | Domain Reload でプレビューの所有情報が失われる | 妥当・高。ただし毎回の増加量は未測定。修正案は B を基本にする | 高 |
| 2 | Renderer 以外のマテリアル参照を処理しない | 妥当・高。白化や DontSave エラーが必ず起きるという断定は不可 | 高 |
| 3 | MA との実行順序が未指定 | 妥当・高。後続追加の取りこぼしだけでなく Swap の照合にも影響 | 高 |
| 4 | Undo/Redo による参照復元を外部ループと誤認する | 妥当・中。Undo の保存内容と heal のタイミングに依存 | 中 |
| 5 | 元画像の再インポートを検知できない | 妥当・高。インポート通知で予約する方式を採用 | 中 |
| 6 | Play 用処理でも圧縮する | 妥当・高。遅延の程度は未測定。性能改善として扱う | 中 |
| 7 | マスクなしの破棄済み Material が `_sig` に残る | 妥当・高。ただし null がキューに追加されるという説明は誤り | 低 |
| 8 | ForceSync が成功前に既存プレビューを破棄する | 妥当・高。失敗時の表示維持を修正 | 中 |

### 1. テクスチャ所有権と Domain Reload

根拠: MaskSync 97–113、171–182、466–473 行。static の `_preview` は再初期化されるが、所有テクスチャをリロード前に解放する処理がない。次回のベイクは現 Dictionary のテクスチャしか破棄しない。シーンや Prefab Stage を閉じた後も、Material アセットが生存していれば追跡・テクスチャが残る。

`HideAndDontSave` は `UnloadUnusedAssets` による自動解放を抑止する。所有者が明示的に破棄する必要があり、「一時テクスチャは Domain Reload で消える」というコメントに依存できない。[Unity: HideAndDontSave](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/HideFlags.HideAndDontSave.html)

2048×2048 RGBA32 全ミップの画素データは約22.37 MB（21.33 MiB）。この値はテクスチャ1コピーの概算で、読み取り可能な CPU コピー、ドライバー側の確保、Play 遷移での実際の破棄・再ロードを含むプロセスメモリの実測値ではない。「Play 1回につき必ず1枚、20回で必ず数GB」という数量までは断定しない。

**修正方針:** 案B、すなわち `AssemblyReloadEvents.beforeAssemblyReload` で既知の所有テクスチャを解放する方式を基本とする。コールバックを解除し、Material がまだ存在し、対象プロパティがあり、そこに自分のテクスチャが付いている場合だけ参照を外し、所有物を重複なく破棄して管理状態をクリアする。リロード後は通常の遅延スキャンで再構築する。[Unity: beforeAssemblyReload](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssemblyReloadEvents-beforeAssemblyReload.html)

案Aの「名前の末尾と HideFlags が一致したら破棄」は採用しない。名前・フラグは所有権の証明ではなく、複製された Material が同じテクスチャを参照する場合もある。また、どの Material からも参照されなくなった孤児はこの方法では回収できない。既存バージョン由来の孤児は新処理で完全回収できると約束せず、確実な所有記録がないオブジェクトの一括破棄は行わない。

「案Bは dirty にするから案Aより危険」という結論には同意しない。必要なのは参照解除を条件付きにして保存副作用を検証することである。`SaveAssets`・`Refresh`・Undo 記録をリロードコールバックから呼ばず、ユーザーの編集を消し得る dirty フラグの強制クリアもしない。シーン／Stage を離れたプレビューは使用状況に基づく解放を追加し、Inspector で使用中のものと区別する。非破壊性は元 `.mat` の差分で確認する。

### 2. アニメーション参照とベイク失敗

根拠: Plugin 31–60 行。現在は `Renderer.sharedMaterials` にあるものしか収集・置換しない。クリップにだけ出現する B は対象外であり、初期 A もクリップの参照は元の A のまま残る。NDMF が動いてもこのプラグインの置換漏れを自動的に修正する保証はない。MA Setter/Swap については、静的に Renderer に反映済みなら対象になり得るため「必ず全部漏れる」ではなく、生成されたアニメーション参照と処理順序を分けて評価する。

DontSave の一時プレビューがアバターの参照グラフに残る危険も妥当。ただし NDMF の `BuildContext.Serialize` は保存済みアセットの参照先も探索し、未永続オブジェクトを保存対象にする。型による HideFlags 変更もあるため、必ず特定の DontSave エラーで止まるとは断定できない。一時プレビューが意図せず保存される、古い／未圧縮マスクが混入する、元 Material の参照と生成アセットの寿命が結び付くことも検証対象とする。

**修正方針:** Renderer と、アバターが使用するアニメーションの ObjectReference カーブの Material を収集する。NDMF `AnimatorServicesContext` の仮想クリップを利用し、MA 生成クリップ、FX 等の playable layers、AnimatorOverrideController、子 Animator を含む範囲を確認する。導入済み NDMF の `CheckMipStreamingPass` は `AnimationIndex.GetPPtrReferencedObjects` と Renderer の和集合を使用しており、収集の参考になる。

元 Material → ビルド用クローンの共通マップを作り、Renderer と全対象クリップを同じクローンへ置換する。元の Material／AnimationClip／Controller アセットは編集しない。ObjectRegistry への置換登録も行うが、それだけでクリップの参照が書き換わるとはみなさない。アニメーションに依存しない単なる float のマスク強度変更は通常どおり動作させる。

対象クローンにはプレビュー由来の `_CustomMaskPacked` を持ち込まず、マスクありなら今回生成したテクスチャ、なしなら白デフォルトを指定する。マスクなしでも古い packed 参照がある場合を処理する。最終的な参照グラフにプレビュー所有物や未処理の必要マスクが残っていないことを検査する。汎用の参照グラフ探索は検査にも用い、元アセットへの無差別な書き込みには使わない。

`NeedsPacking == true` なのにベイクが失敗した場合は、Material と原因を示す NDMF のビルド失敗として扱う。`clones[m] = null` のまま続行したり、白マスクのクローンに置換して成功扱いにしたりしない。圧縮だけ失敗し、正しい未圧縮テクスチャが得られた場合のフォールバックは別扱いで維持できる。

### 3. NDMF / MA の実行順序

根拠: Plugin 23 行に順序制約がない。MA `Editor/PluginDefinition/PluginDefinition.cs` の Transforming 内に Reactive Components があり、Setter/Swap を処理する。MA `ReactiveObjectAnalyzer.LocateReactions.cs` の `RegisterMaterialSwap` は Renderer の Material を ObjectRegistry で元に戻して照合する。

DennokoEx が先行して未登録のクローンへ置換すると、MA の `From` が元 Material と一致せず、Swap 自体が不成立になり得る。後行すれば初期状態を収集しやすくなるが、アニメーション参照の問題（2）は単独では直らない。「順序が毎回ランダム」という意味ではなく、依存関係として保証されていないことが問題。

**修正方針:** Transforming の `.AfterPlugin("nadena.dev.modular-avatar")` を基本制約にして、マテリアル／アニメーション生成後に収集・ベイクする。MA 1.17.1 の別プラグイン `nadena.dev.modular-avatar.late-transform-stages` は FloorAdjuster・コンポーネント除去・再バインドを行い、今回確認したマテリアル生成は本体側にある。将来バージョンや他プラグインまでこの制約だけで保証できるとはしない。

対象は MA 固有の前処理コンポーネントではなく、生成後の Renderer・アニメーションにする。他のマテリアル変更ツールとの互換順序は、そのツールの実際のフェーズを確認して追加する。NDMF の後続最適化と lilToon の処理より前に必要なベイク・参照置換を完了させ、依存関係の循環と MA 未導入構成を検証する。

### 4. Undo/Redo とループガード

根拠: MaskSync 108、135–140、238–270、275–300、470 行。Material 全体が Undo に記録されていれば、すでに破棄したテクスチャ参照が復元される可能性がある。Undo コールバックは通常キューへ積むだけで、`DrainQueue` は未承認の参照切れをスキップし、self-heal が後から strike を加算する。

一時的な白化と誤った外部ループ判定は成立し得る。ただし「Undo のたびに必ず加算」は不正確。健全な割り当てを後の heal が観測すると strike は消える。参照切れが観測の直前に連続し、健全な観測を挟まないタイミングで誤ミュートが発生する。「最大1秒」もキュー予算、Busy、既存ミュートにより超え得る。

**修正方針:** Undo/Redo を明示的な再検証理由として区別し、影響がある追跡 Material の参照切れを外部ループの strike に数えず、次のアイドル update の予算付きキューで修復する。Undo を受けた時点で少なくとも参照切れ／入力差分を絞り込み、関係ない Undo で全 Material のループガードを解除しない。対象のミュートも適切に解除し、`_healApproved` に追加するだけで既存 cooldown に再度止められる実装を避ける。失敗再試行の backoff は外部ループ状態と分離する。

### 5. 元画像の再インポート

根拠: MaskSync 116–125、238–243、346、395、477–488 行。元画像だけ変わり packed の参照が残るケースでは署名が再計算されない。Inspector を再表示するだけでも、`EnsurePreview` が既存 `_preview` を見て終了するため直らない。

`ObjectChangeEvents` は Undo 対象の変更を扱うため、画像ファイルのインポート通知の代用にできない。「インポート中は関連イベントが一切発行されない」という強い断定までは不要。[Unity: ObjectChangeEvents](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ObjectChangeEvents.html)

**修正方針:** `OnPostprocessAllAssets` では関連パスを記録するだけにする。コールバック内でベイク、Material 書き換え、SaveAssets、Refresh を行わない。アイドル update で、追跡 Material の元テクスチャ依存を使って対象を絞り、通常の署名比較・予算付きキューへ送る。削除・移動の旧新パス、複数 Material に共有された画像も扱う。全プロジェクトを走査しない。

元画像の変化と packed 参照の外部クリアを区別し、後者のループガードは維持する。署名に含める変更情報が実際のベイク入力を表すことを確認する。画像内容だけでなく sRGB、解像度のインポート設定変更も検証する。「SetTexture は絶対にテクスチャ再インポートを引き起こさないためループは再発しない」とは保証しない。第三者ツールの再保存を含む回帰試験で、通知を受けても入力が同じならベイクしないことを確認する。

### 6. Play 時の圧縮

根拠: Plugin 46 行は常に `forBuild: true`。Packer 98、114–130 行は対応環境で圧縮を呼ぶ。NDMF `ApplyOnPlay.cs` から同じ処理が動くため、Play 用でもこのコストを負う。秒数は環境依存で未測定。ASTC は Play 中の Editor GPU との互換性も確認が必要。

**修正方針:** 圧縮の有無とストリーミング設定を別のオプションに分離する。Edit プレビュー／NDMF Play／出荷ビルドを明示して、通常の Play では圧縮を省き、出荷では従来の圧縮を維持する。Play 判定は入口で固定し、遷移中も考慮する。導入済み NDMF の VRChat フックと同じ `EditorApplication.isPlayingOrWillChangePlaymode` を基本とし、`isPlaying` 単独には依存しない。[Unity: isPlayingOrWillChangePlaymode](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorApplication-isPlayingOrWillChangePlaymode.html)

単に `forBuild: false` にするとストリーミング設定まで外れるため、その変更を一括適用しない。NDMF の CheckMipStreamingPass と整合する設定、読み取り可否、保存後の表示を確認する。Play も生成物の所有・保存は NDMF に渡し、Edit 用 HideAndDontSave プレビューは共有しない。圧縮差による Play／出荷の画質差は検証上明示する。

### 7. 破棄済みキー

根拠: MaskSync 135–140、227、280–283、454–463 行。署名 `none` は `_sig` にのみ残り、`_preview` の巡回では清掃されない。破棄済み Unity Object の managed wrapper が辞書に残る問題は妥当。ただし `Enqueue` の `m != null` が防ぐので、Undo のたびに null のキュー項目が増えるわけではない。重いテクスチャのネイティブリークとは分ける。

**修正方針:** `_preview` だけでなく `_sig`、retry 等も含む追跡状態の破棄済みキーを低頻度で掃除する。列挙中の直接変更を避け、Unity の擬似 null と Dictionary キーとしての参照を区別して除去する。別シェーダーへ変更された `none` の Material も管理対象から外す。

### 8. 手動リフレッシュの失敗時動作

根拠: MaskSync 375–388 行。非 Busy の `ForceSync` は `Forget` で旧テクスチャを破棄してから `Sync` を呼ぶ。ベイク失敗時に旧表示を維持する通常の `Sync` 方針と矛盾する。

**修正方針:** キャッシュ無効化・ミュート解除とテクスチャ解放を分ける。旧プレビューの所有情報と割り当てを維持したまま強制再ベイクし、新テクスチャの生成・割り当てに成功してから旧物を破棄する。失敗時は旧表示と通常の失敗通知・再試行を維持する。元マスクを全解除した場合と別シェーダーへの変更は別の正常な解放経路とする。Busy 時の予約でも「手動」の理由を保持し、後で通常の参照切れガードに埋没させない。

## 「問題なし」とされた点の補足

- Play 中の Edit プレビュー停止、非アクティブ Renderer を含む NDMF のベイク、Play 終了時の再検証は基本方針として妥当。Domain Reload の有無による所有権の違いは別途修正する。
- Busy はコンパイル・インポート・Play 遷移と `BuildPipeline.isBuildingPlayer` を抑止するが、VRChat のアセットバンドル用前処理全体をこのフラグが覆うとは保証できない。通常の同期 NDMF パス中には update は割り込まないため、直ちに競合バグと断定もしない。実アップロード／手動 NDMF 実行と Editor update の境界で検証する。
- self-heal の tick 管理、backoff、別シェーダーに packed プロパティを触らないガードは維持する。ただし Undo の原因識別（4）が別途必要。
- RGBA のチャンネル対応と linear RT → linear Texture2D はコード上整合している。ただし色空間・Graphics.Blit の状態・圧縮後の画質まで実測で保証したものではない。
- Packer の通常経路は RenderTexture.active を復元し、temporary RT と blit Material を finally で解放する。ただし Texture2D 作成後の ReadPixels／Apply／ストリーミング設定などで例外が出ると `result` は解放されない。8の失敗処理を実装する際、途中生成物を失敗時に破棄し、成功時だけ所有権を呼び出し元へ渡す。GetTemporary が try の外にある点も含めて確保・解放の範囲を揃える。

## 実装順序と受け入れ条件

1. 所有権と失敗時の解放を整理する（1、7、8、Packer の例外経路）。既存のユーザー編集・保存状態を壊さないことを確認する。
2. NDMF の順序、Renderer／アニメーション収集・置換、失敗時停止を一組で実装する（2、3）。原本アセットの不変と、生成物に Edit プレビューが混入しないことを優先する。
3. 更新要求に理由を持たせ、Undo／手動／入力変更／外部クリアを区別する。画像インポートの遅延検証を追加する（4、5）。従来の import/save ループ防止を回帰検証する。
4. 正しさを確認してから Play の圧縮を省く（6）。圧縮とストリーミング設定を独立させ、前後の時間・表示を測定する。

| 検証場面 | 手順 | 受け入れ条件 |
| --- | --- | --- |
| スクリプト再コンパイル | マスク付き Material を表示して複数回リロード | 所有プレビュー数・メモリが際限なく増えず表示が復旧する |
| Play の4設定 | Domain Reload と Scene Reload の各 ON/OFF の組合せで開始・終了を反復 | NDMF 生成物と Edit プレビューを混同せず、終了後に表示復旧。未対応設定なら理由を記録 |
| シーン／Prefab Stage | 開閉、別シーンへの切り替え、Inspector のみで表示 | 使用中の表示を保持し、不要な所有物を回収 |
| マテリアル切り替え | 初期 A → クリップ専用 B → A、MA Setter/Swap、非アクティブ衣装 | Play と出荷の全状態で正しいマスク。元 A/B/Clip/Controller に差分なし |
| 依存順序 | MA あり／なし、MA Swap の From が初期 Material | 順序解決が成功し Swap が機能。アニメーション参照も置換される |
| プレビューの混入 | Inspector で開いた／未表示の同一アバターを別々に処理 | 開いた履歴に関係なく同じ入力から生成。Edit 所有テクスチャへのビルド参照なし |
| Undo/Redo | マスク差し替えを反復し heal 前後のタイミングで Undo/Redo | 誤った60秒ミュートなし。関係ない Undo で外部ループガードを解除しない |
| 画像インポート | PNG 上書き、sRGB・最大サイズ変更、移動・削除 | アイドル時に関係する Material のみ更新。Inspector 操作不要 |
| 外部 save/import | 同じ入力で繰り返し保存／再インポート、packed の連続クリア | 不要なベイクを繰り返さず、実際の外部ループにはガードが働く |
| 強制更新の失敗 | ベイク失敗を注入し、手動更新と Busy 中の予約を実行 | 旧プレビュー維持、再試行で復旧、途中生成物リークなし |
| 出荷ベイクの失敗 | 必須ベイク失敗を注入 | 対象と原因を明示して失敗。白マスクや元プレビューで成功扱いにしない |
| マスクなしの清掃 | none の一時 Material を多数生成・破棄 | 管理辞書・キューに破棄済みキーが蓄積しない |
| 圧縮と色 | PC／Android ターゲットで Play と出荷、既知の濃度パターン | RGBA 対応・色・ミップを維持、Play の圧縮呼び出しなし、出荷の圧縮あり |
| 保存・アップロード | 編集済み Material を含め SaveAssets、実ビルド、終了後再インポート | ユーザーの編集を保持し、不要な生成参照や継続的な保存ループを作らない |

上表は今後実行する検証計画であり、通過済みのチェックリストではない。

## 参照コード

- [MaskSync](../../Editor/DennokoExMaskSync.cs)、[MaskPacker](../../Editor/DennokoExMaskPacker.cs)、[NDMF Plugin](../../Editor/NDMF/DennokoExPackMasksPlugin.cs)、[Inspector](../../Editor/DennokoExInspector.cs)
- プロジェクトルート基準 `Packages/nadena.dev.ndmf/Editor/ApplyOnPlay.cs`
- 同 `Packages/nadena.dev.ndmf/Editor/VRChat/BuildFrameworkPreprocessHook.cs`、`CheckMipStreamingPass.cs`
- 同 `Packages/nadena.dev.ndmf/Editor/API/BuildContext.cs`、`Util/VisitAssets.cs`、`Serialization/AssetSaver.cs`
- 同 `Packages/nadena.dev.modular-avatar/Editor/PluginDefinition/PluginDefinition.cs`
- 同 `Packages/nadena.dev.modular-avatar/Editor/ReactiveObjects/AnimationGeneration/ReactiveObjectAnalyzer.LocateReactions.cs`
