# ObjecTalk 物体登録キャプチャUIとフレーム前処理 設計

## 目的

物体登録の3枚撮影フローを、ユーザーが迷わず操作できるUIにする。あわせて、カメラフレームを直立補正し、embeddingに渡す領域を中央の狭いボックスに絞って認識の再現性を上げる。

現状の課題:

- 登録ボタンはあるが「3枚撮る」ことが伝わらない。何枚目かの表示がない。
- どこを撮ればよいか（フレームのどの範囲がAIに渡るか）が分からない。
- 実機のカメラプレビューが横向きに表示される。
- embeddingがカメラフレーム中央の正方形フルサイズを見ており、対象物以外の背景が多く入る。

## 確定要件

- 登録セッション開始時から「1 / 3 枚目」形式の進捗を常時表示する。
- 3枚のうち何枚撮ったかを、進捗ドット3個で視覚的に示す。
- カメラプレビュー上に、AIが実際に切り出す領域と一致するガイド枠を表示する。
- 「物体を枠の中央に収めて撮影」という説明文を表示する。2枚目以降は「角度を少し変えて」を追加する。
- カメラプレビューを端末の向きに合わせて直立表示する（`videoRotationAngle` と `videoVerticallyMirrored` を反映）。
- embeddingとローカル特徴の入力を、直立補正済みフレームの中央75%の正方形に限定する。
- プレビューに表示する画とAIが見る画は、向き・切り出し領域が一致していなければならない。
- 前処理の変更で既存のembeddingサンプルは無効になる。既存の登録データは全消去する。
- 認識・登録が前処理変更後も従来どおり動作すること。前処理変更以外の認識ロジックは変えない。

## 対象外

- 撮影ごとに異なる角度を強制する仕組み。
- AKAZEローカル特徴照合の再有効化（arm64ネイティブライブラリ導入は別タスク）。
- 日本語フォントが実機で豆腐表示になる問題（壊れた `NotoSansJP-Medium SDF.asset`、別タスク）。
- 認識しきい値・時間フィルタのチューニング。
- `IFramePreprocessor` インターフェースの署名変更。

## 採用方式

カメラの向き補正はコントローラ（`EmbeddingObjectMatcher`）側で行い、`CameraFramePreprocessor` は向きに依存しないまま、切り出し率だけをパラメータ化する。

`EmbeddingObjectMatcher` が直立補正済みの中間 `RenderTexture`（`_orientedFrame`）を1枚保持し、毎フレーム `webcamTexture` を回転行列付きで描画する。認識・登録・プレビューの全経路はこの `_orientedFrame` を参照する。回転はカスタムシェーダを使わず、クワッドメッシュへの `Graphics.DrawMeshNow` と `Matrix4x4` の組み合わせ、`Unlit/Texture` 相当のマテリアルで実装する。

この方式を選ぶ理由:

- `IFramePreprocessor.PrepareEmbedding` / `CopyVerificationFrame` の署名を変えずに済み、`ObjectRecognitionService` とそのテストへの波及が最小になる。
- プレビューとAI入力が同一の `_orientedFrame` を共有するため、「見えている画」と「認識される画」が構造的に一致する。
- 回転補正は純粋な行列・UV計算に切り出せ、レンダリングなしでテストできる。

代償として、フレームごとに全画面blitが1回増える。対象端末（Adreno 640）では無視できる負荷。

## 全体アーキテクチャ

```text
WebCamTexture
  │ 毎フレーム DrawMeshNow(quad, 回転行列, Unlit/Texture)
  ▼
_orientedFrame (RenderTexture, 直立。90/270°時は幅高スワップ)
  ├─▶ registerCameraDisplay / talkCameraDisplay
  │      （AspectRatioFitter で _orientedFrame の縦横比に追従）
  │
  └─▶ ObjectRecognitionService.StartRecognition / CaptureRegistrationView
         │
         ▼
       CameraFramePreprocessor
         ├─ PrepareEmbedding: 中央 cropFraction(0.75) の正方形 → 224x224
         └─ CopyVerificationFrame: 同じ切り出し → CPU可読フレーム
         │
         ▼
       SentisEmbeddingExtractor → embeddingベクトル
         （PreprocessingVersion は新値。旧サンプルは読込時に棄却）
```

## コンポーネント設計

### FrameOrientation（新規・純粋型）

`Assets/Scripts/Domain/Recognition/FrameOrientation.cs`

- `int RotationDegrees`（0 / 90 / 180 / 270 に正規化）、`bool Mirrored` を保持する。
- `WebCamTexture` から生成するファクトリ（`FromWebCam(int videoRotationAngle, bool videoVerticallyMirrored)`）。負角・360超は正規化する。
- 直立補正blitに使う頂点・UVマッピング（またはUV変換 `Matrix4x4`）を返すメソッド。回転と反転の合成結果を1つの変換として返す。
- 直立後のサイズを返すヘルパー（`OrientedSize(int sourceWidth, int sourceHeight)` — 90/270°で幅高スワップ）。
- Unity描画に依存しない。テストは4つの回転×反転の組み合わせで、四隅がどこへ写るかを検証する。

### ImageGeometry（既存・拡張）

`Assets/Scripts/Domain/Recognition/ImageGeometry.cs`

- `const float DefaultCropFraction = 0.75f` を追加する。
- `CenterCrop(int sourceWidth, int sourceHeight, float cropFraction)` オーバーロードを追加する。中央に配置した `cropFraction * min(width, height)` 辺の正方形を返す。
- `NormalizedCenterCrop(int sourceWidth, int sourceHeight, float cropFraction)` オーバーロードを追加する。
- `cropFraction` は `(0, 1]` にクランプする。既存の引数なしオーバーロードは `1.0f` 相当の挙動を維持する。
- テスト: 正方形サイズ、中央配置のオフセット、正規化後の scale/offset、クランプ境界。

### CameraFramePreprocessor（既存・拡張）

`Assets/Scripts/Infrastructure/Vision/Preprocessing/CameraFramePreprocessor.cs`

- コンストラクタに `float cropFraction`（既定 `ImageGeometry.DefaultCropFraction`）を追加する。フィールドに保持する。
- `PrepareEmbedding` と `CopyVerificationFrame` の切り出し計算を `NormalizedCenterCrop(w, h, _cropFraction)` / `CenterCrop(w, h, _cropFraction)` に置き換える。
- 向きの補正はしない（コントローラで直立済みのフレームを受け取る前提）。
- 出力次元は従来どおり（embedding 224、verification は上限内）。
- テスト: 既存テストを75%切り出し前提に更新。出力次元・バッファ再利用・dispose挙動は不変。

### EmbeddingModelSpec（既存・版数更新）

`Assets/Scripts/Domain/Recognition/EmbeddingModelSpec.cs`

- `DefaultPreprocessingVersion` を `"rgb01-imagenet-mean-std-v1"` から `"rgb01-imagenet-mean-std-crop75-upright-v2"` に変更する。
- 効果: `RecognitionSampleRepository` が読込時に `preprocessing_version` 不一致の行を弾く既存機構により、旧サンプルは認識対象から外れる。
- テスト: 既定値を検証するテストがあれば更新。

### 既存データの消去

`Assets/Scripts/Infrastructure/Database/RecognitionSampleRepository.cs`（または `DataManager` の初期化経路）

- 起動時に一度だけ、現在の `PreprocessingVersion` と一致しない `recognition_samples` 行を `DELETE` する。
- 版数不一致による読込棄却だけでも認識上は消去済みだが、ストレージを解放するため物理削除も行う。
- テスト: 旧版数で書いた行が、新版数での読み込み後に存在しないこと。現版数の行は残ること。

### EmbeddingObjectMatcher（既存・拡張）

`Assets/Scripts/Presentation/Controllers/EmbeddingObjectMatcher.cs`

直立補正:

- `RenderTexture _orientedFrame` と `Material _orientMaterial`（`Unlit/Texture` 相当）、クワッド `Mesh` を保持する。
- カメラ開始後、`webcamTexture` の `videoRotationAngle` / `videoVerticallyMirrored` から `FrameOrientation` を作り、`_orientedFrame` を直立後サイズで確保する。カメラ解像度・向きが変わったら作り直す。
- 直立補正blitに使うマテリアルのシェーダは、IL2CPPビルドで stripping されないこと（`Graphics.Blit` 既定の `Hidden/BlitCopy` を使うか、使用シェーダを Always Included Shaders に登録するか、`Resources` 経由でロードする）。回転はマテリアルのUV変換行列（`mainTextureScale` / `mainTextureOffset` では回転できないため、blitマテリアルにUV回転を持たせるか、クワッドの頂点行列で回す）で与える。
- `Update` で `webcamTexture.didUpdateThisFrame` のとき `_orientedFrame` へ直立補正blitする。
- `_recognitionService.StartRecognition` / `CaptureRegistrationView` に渡す引数を `webcamTexture` から `_orientedFrame` に変更する。
- `registerCameraDisplay.texture` / `talkCameraDisplay.texture` を `_orientedFrame` にし、`AspectRatioFitter` の比率を `_orientedFrame` の縦横比に設定する。
- `_orientedFrame` の確保に失敗、またはカメラが 0x0 を返す場合は、従来どおり `webcamTexture` を直接渡してログを出す（機能を止めない既存方針に合わせる）。

登録UI:

- `[SerializeField] Image[] registrationDots`（3個）、`[SerializeField] RectTransform captureGuideFrame`、`[SerializeField] TMP_Text registrationInstructionText` を追加する。既存の `registrationStatusText` を進捗表示に使う。
- `BeginRegistrationSession` で進捗を「1 / 3 枚目」に初期化し、ドットを全消灯、説明文を初期メッセージに設定、`captureGuideFrame` を有効化する。
- `HandleTickResult` の `CaptureAccepted` で、`registrationStatusText` を「(n+1) / 3 枚目」に更新し、ドットを n 個点灯、2枚目以降は説明文に「角度を少し変えて」を加える。
- `captureGuideFrame` を、プレビュー矩形内で中央 `DefaultCropFraction` の正方形（`_orientedFrame` の縦横比基準で min 辺の75%）に一致させる。プレビューのサイズ確定後（`OnEnable` のレイアウト後 or 次フレーム）にサイズ設定する。参照が未設定なら警告のみ（既存パターン）。
- 登録フロー離脱時（`EnterPlacementFromCaptures` / `OnBackButtonClicked` / `OnDisable`）に `captureGuideFrame` を無効化する。

### RecognitionMessages（既存・文言）

- `RegistrationProgress(int captured, int total)` の文言を「n / total 枚目」形式に合わせる。必要なら初期メッセージ用の定数と、2枚目以降の追加ヒント文言を追加する。

### ObjectDetectionScene.unity（既存・シーン編集）

`RegisterCameraPanel` 配下に追加する:

- `CaptureGuideFrame`（`Image`、9スライスの枠 or アウトライン）。
- `RegistrationDots`（`Image` 3個、横並び）。
- 説明文の `TMP_Text`。
- `RegisterCameraDisplay` / `TalkCameraDisplay` に `AspectRatioFitter` を付与（未付与なら）。
- `EmbeddingObjectMatcher` コンポーネントの新規フィールド（`registrationDots`, `captureGuideFrame`, `registrationInstructionText`, 既存の `registrationStatusText`）を配線する。
- 編集は実行中の Unity Editor（uloop）で行う。シーンファイルは他の大半のアセットと同様 git 未追跡のまま。

## データフローとテスト戦略

| 層 | 何を | どうテストするか |
|---|---|---|
| `FrameOrientation` | 回転・反転の四隅マッピング | EditMode 純粋関数テスト（4回転 × mirror） |
| `ImageGeometry` | crop率の正方形サイズ・正規化・クランプ | EditMode 純粋関数テスト |
| `CameraFramePreprocessor` | 75%切り出し、出力次元 | EditMode（既存テスト更新） |
| `RecognitionSampleRepository` | 旧版数サンプルの棄却・削除 | EditMode（一時SQLite） |
| `EmbeddingObjectMatcher` | 進捗テキスト 1→2→3、ガイド枠の有効/無効 | PlayMode（`ObjectDetectionSceneNavigationTests` を拡張 or 新規） |
| 統合 | 登録→保存→認識が新前処理で成立 | 実機スモーク（SC-03L）、logcat で `★ 物体認識: ON` を確認 |

## エラー処理

- `_orientedFrame` 確保失敗・カメラ 0x0: `webcamTexture` を直接使用し `Debug.LogWarning`。認識・登録・プレビューは継続。
- `captureGuideFrame` 等のUI参照未設定: `Debug.LogError` 相当の警告のみ。フローは継続。
- 旧版数サンプル削除の失敗: ログのみ。読込側の版数フィルタが安全網として機能する。

## 実機での確認手順

1. Android dev APK をビルドし SC-03L にインストールする。
2. ログイン後、登録フローに入り、進捗が「1 / 3 枚目」から始まること、ドットが撮影ごとに増えること、ガイド枠が中央に出ることを確認する。
3. プレビューが直立していること、ガイド枠内の物体だけが切り出されている感覚と一致することを確認する。
4. 3枚撮影 → 配置 → 命名 → 保存まで通し、`Object '<名前>' saved to DB successfully` を確認する。
5. 保存物体にカメラを向け、`★ 物体認識: ON (1) - 物体名: <名前>` が出ることを確認する。
6. 旧前処理で登録済みだった物体（もしあれば）が `0 recognizable` になっていることを確認する。
