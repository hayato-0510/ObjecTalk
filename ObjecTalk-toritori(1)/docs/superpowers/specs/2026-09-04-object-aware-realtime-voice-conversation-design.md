# ObjecTalk 物体連動リアルタイム音声会話 設計

## 目的

ObjecTalkが登録物体を認識したとき、その物体の名前と人格を使った日本語の音声会話を自動的に開始する。ユーザーは録音ボタンを操作せず、ハンズフリーで会話できる。

既存の物体認識・登録機能は音声機能から分離し、音声接続に失敗しても引き続き利用可能とする。会話音声、文字起こし、会話履歴は端末、SQLite、バックエンドに保存しない。

## 確定要件

- 物体を認識すると常時リアルタイム会話を自動開始する。
- 同じ物体を見失っても10秒間は会話を維持する。
- 見失ってから10秒以内に同じ物体を再認識した場合は会話を継続する。
- 別の登録物体を2秒連続で認識した場合だけ会話相手を切り替える。
- 会話相手を切り替える際は、現在のAI応答と音声を停止し、新しいセッションを開始する。
- 会話履歴は現在のRealtimeセッション内だけで維持し、切断時に破棄する。
- `PersonalityPrompt`が空の場合は共通の標準人格を使用する。
- 開発時はローカル、本番時はクラウドのHTTPSバックエンドを使用できる。
- OpenAI APIキーはバックエンドだけで管理する。
- Androidを主要な実行環境とする。

## 採用方式

UnityクライアントからOpenAI Realtime APIへWebRTCで直接接続する。バックエンドはFirebase認証を検証し、Realtime接続の作成に必要な短期認証情報またはSDP交換を仲介する最小構成とする。

WebRTCを採用する理由は、双方向音声の低遅延通信、ジッター処理、音声トラック管理を通信層へ委ねられるためである。`D:\toritori-openai2`のUnity WebRTC実装を参考にし、`D:\toritori-2`から会話イベント、割り込み、バックエンド認証の設計知見を再利用する。ソースコードはそのままコピーせず、現行API仕様とObjecTalkの責務境界に合わせて実装し直す。

## 全体アーキテクチャ

```text
EmbeddingObjectMatcher
  │ RecognitionChanged
  ▼
ObjectConversationCoordinator
  ├─ 10秒の認識ロスト猶予
  ├─ 2秒の別物体切替判定
  └─ 会話開始・終了・切替の判断
        │
        ▼
IVoiceConversationService
  └─ OpenAIRealtimeConversationService
       ├─ SessionTokenClient ── HTTPS ──▶ ObjecTalk Backend
       ├─ RealtimeWebRtcTransport ─────▶ OpenAI Realtime API
       ├─ RealtimeEventParser
       ├─ ConversationStateMachine
       └─ MicrophonePermissionService
        │
        ▼
TalkController / TalkView
  ├─ 接続状態
  ├─ ユーザー発話
  ├─ 物体の応答
  └─ エラー・再接続表示
```

## コンポーネント

### `EmbeddingObjectMatcher`

物体認識だけを担当する。音声API、WebRTC、会話状態を参照しない。既存イベントで情報が不足する場合は、次の認識結果を通知するイベントへ拡張する。

```csharp
event Action<RecognizedObjectState> RecognitionChanged;
```

`RecognizedObjectState`は物体ID、名前、認識状態、信頼度、認識時刻を含む。認識ロストや別物体切替の時間判定は行わない。

### `ObjectConversationCoordinator`

物体認識と会話サブシステムをつなぐ唯一の層とする。

- 初回認識時に会話コンテキストを構築して会話を開始する。
- 同じ物体の認識ロストを10秒間猶予する。
- 別物体が2秒間連続で安定した場合だけ切り替える。
- 現在の会話相手と候補物体を別々に管理する。
- Unityのフレーム時刻ではなく、注入可能な時計を用いて判定をテスト可能にする。

### `ConversationContext`

会話開始時に必要な情報だけを保持する。

```text
ObjectId
ObjectName
PersonalityPrompt
UserId
Language
```

会話層はSQLiteや`DatabaseManager`を直接参照しない。物体情報はリポジトリインターフェースから取得し、`ConversationContext`として渡す。

### `IVoiceConversationService`

上位層に次の操作と状態通知を提供する。

```text
StartAsync(context)
StopAsync(reason)
SwitchAsync(context)
CurrentState
StateChanged
UserTranscriptReceived
AssistantTranscriptReceived
ErrorOccurred
```

多重開始は拒否または同一操作として扱い、停止処理は複数回呼ばれても安全な冪等操作とする。

### `OpenAIRealtimeConversationService`

セッション取得、WebRTC接続、会話状態、再接続を統括する。個々の通信処理は下位コンポーネントへ委譲し、OpenAI固有のイベントを上位層へ露出させない。

### `SessionTokenClient`

Firebase IDトークンを付けてObjecTalkバックエンドへHTTPSリクエストを送り、短期セッション情報を取得する。APIキーを受け取らず、ログにも短期認証情報を出力しない。開発用と本番用のベースURLは設定アセットで切り替える。

### `RealtimeWebRtcTransport`

RTCPeerConnection、ローカルマイクトラック、リモート音声トラック、データチャネル、SDP交換を担当する。会話相手や物体認識を知らない。

### `RealtimeEventParser`

Realtime APIのJSONイベントを型付きの内部イベントへ変換する。不正JSONと未知イベントはアプリを停止させず、安全に無視または分類済みエラーとして通知する。

### `ConversationStateMachine`

会話状態と許可される遷移を一元管理する。

```text
Idle
  → RequestingPermission
  → Connecting
  → Listening ⇄ Speaking
  → Reconnecting
  → Stopping
  → Idle
```

各開始処理にセッション世代IDを付け、終了済みセッションから遅れて届いたイベントを無視する。

### `MicrophonePermissionService`

Android、iOS、Editorのマイク権限差異を吸収する。拒否時はOS設定への案内に必要な状態を返す。権限取得前に録音やWebRTC接続を開始しない。

### `TalkController`と`TalkView`

`TalkController`は会話サービスの状態を表示モデルへ変換する。`TalkView`は接続中、聞き取り中、物体の発話中、再接続中、エラーを表示し、通信や認識ロジックを持たない。

## 推奨ファイル構成

```text
Assets/Scripts/
├─ Core/Conversation/
│  ├─ IVoiceConversationService.cs
│  ├─ ConversationContext.cs
│  ├─ ConversationState.cs
│  ├─ RecognizedObjectState.cs
│  └─ ObjectConversationCoordinator.cs
├─ Infrastructure/Conversation/
│  ├─ OpenAI/
│  │  ├─ OpenAIRealtimeConversationService.cs
│  │  ├─ RealtimeWebRtcTransport.cs
│  │  ├─ RealtimeEventParser.cs
│  │  └─ SessionTokenClient.cs
│  └─ Audio/
│     └─ MicrophonePermissionService.cs
└─ Presentation/Talk/
   ├─ TalkController.cs
   └─ TalkView.cs

Backend/
├─ src/
│  ├─ server.js
│  ├─ auth/firebaseAuth.js
│  ├─ realtime/createSession.js
│  ├─ conversation/buildInstructions.js
│  └─ config/environment.js
├─ tests/
├─ package.json
├─ .env.example
└─ Dockerfile
```

Conversation、Infrastructure、Presentationには必要に応じてasmdefを置き、依存方向をPresentation → Core、Infrastructure → Coreに限定する。CoreはUnityのWebRTC実装やOpenAIのJSON形式へ依存しない。

## 会話開始フロー

1. `EmbeddingObjectMatcher`が安定した物体認識結果を通知する。
2. `ObjectConversationCoordinator`が物体IDを使って名前と`PersonalityPrompt`を取得する。
3. `ConversationContext`を構築する。
4. マイク権限を確認・要求する。
5. UnityがFirebase IDトークンを添えてバックエンドへ接続作成を要求する。
6. バックエンドがFirebaseトークン、入力値、レート制限を検証する。
7. バックエンドがOpenAI Realtime接続に必要な短期情報を返す。
8. UnityがWebRTC接続を確立し、マイク音声の送信とAI音声の再生を開始する。
9. データチャネルから発話状態と文字起こしを受け、Talk画面を更新する。

## 会話コンテキストとプロンプト

Unityからバックエンドへ渡す入力は、次の許可済みフィールドに限定する。

```json
{
  "objectId": "registered-object-id",
  "objectName": "りんご",
  "personalityPrompt": "明るく好奇心旺盛",
  "language": "ja"
}
```

バックエンドは文字数と型を検証し、固定テンプレートへ値を組み込む。クライアントから完成済みのシステム指示を受け取らない。

標準人格は次の意図を持つ。

- 認識された物体になりきる。
- 自然な日本語で話す。
- 子どもにも分かりやすい、短く親しみやすい返答を行う。
- 危険な要求には安全な代替案または拒否を返す。

`PersonalityPrompt`が空なら標準人格だけを使用する。値がある場合も、固定された安全指示より優先させない。

## 認識ロストと物体切替

### 同じ物体の認識ロスト

- 最終認識時刻から10秒未満は現在の会話を維持する。
- Talk画面には物体を探している状態を表示する。
- 10秒以内に同じ物体を再認識したらロスト状態を解除する。
- 10秒経過後はAI応答、WebRTC接続、マイクの順に安全に停止し、`Idle`へ戻る。

### 別物体への切替

- 現在と異なる物体を認識したら候補物体として保持する。
- 同じ候補が連続2秒間安定して認識された場合だけ切替を確定する。
- 候補が変わるか認識されなくなった場合は2秒判定をリセットする。
- 切替時は現在のAI応答と再生音声を即時停止する。
- 既存セッションを終了後、新しい`ConversationContext`で新規セッションを開始する。
- 物体間で会話履歴を引き継がない。

## 割り込み会話

サーバー側VADによってユーザーの発話開始を検出する。AIが発話中の場合は現在の応答をキャンセルし、リモート音声再生を停止する。その後のユーザー発話を同じセッション内の次の入力として扱う。

キャンセル済み応答の遅延音声とイベントは、応答IDおよびセッション世代IDを照合して破棄する。

## 再接続とライフサイクル

- 一時的な通信切断は1秒、2秒、4秒の間隔で最大3回再接続する。
- 再接続中に対象物体を10秒以上見失った場合は再接続を中止する。
- 認証エラー、権限拒否、明確な利用制限エラーは自動再試行しない。
- アプリがバックグラウンドへ移行した場合は応答、WebRTC、マイクを停止する。
- フォアグラウンド復帰時は自動的に古いセッションへ戻らず、物体の再認識後に新規セッションを開始する。
- `OnDisable`、シーン遷移、アプリ終了のいずれから呼ばれても同じ停止経路を使用する。

## エラー処理

- マイク権限拒否：OS設定の確認方法をTalk画面へ表示する。
- バックエンド到達不能：会話だけを終了し、物体認識は継続する。
- Firebase認証エラー：再ログインを促し、自動再試行しない。
- Realtime接続の一時切断：上限付き指数バックオフで再接続する。
- OpenAI利用制限：会話を終了し、ユーザー向けメッセージを表示する。
- リモート音声再生エラー：不完全な接続を維持せず、セッションを安全に終了する。
- 不明イベント：診断分類のみ行い、会話を継続する。

エラーは内部の型付き分類へ変換し、UIへ生のHTTPレスポンス、例外、認証情報を表示しない。

## セキュリティとプライバシー

- OpenAI APIキーをUnity、PlayerPrefs、ScriptableObject、Resources、StreamingAssets、ビルド成果物へ含めない。
- バックエンドのAPIキーは環境変数またはクラウドのSecret Managerで管理する。
- バックエンドAPIはHTTPSのみで公開する。
- Firebase IDトークンを検証したユーザーだけにRealtime接続を作成する。
- ユーザー単位およびIP単位のレート制限を設定する。
- `objectName`と`PersonalityPrompt`へ型、長さ、制御文字の検証を行う。
- APIキー、短期認証情報、Firebaseトークン、音声、文字起こし、会話履歴、`PersonalityPrompt`本文をログへ出力しない。
- 会話内容を端末、SQLite、Firebase、バックエンドへ保存しない。

運用ログは匿名化ユーザー識別子、セッション開始・終了時刻、接続結果、エラー分類、ハッシュ化した物体IDだけに限定する。

`D:\toritori-opnai`の`WhisperClient.cs`にはAPIキーをソースへ直接記載した形跡があるため、コードおよびキーを再利用しない。キーが現在も有効なら、実装とは別に失効・再発行する。

## 設定

Unity側に秘密情報を含まない環境設定を用意する。

```text
BackendBaseUrl
RecognitionLossGraceSeconds = 10
ObjectSwitchConfirmationSeconds = 2
ReconnectDelaysSeconds = [1, 2, 4]
DefaultLanguage = ja
DebugLoggingEnabled
```

モデル名、音声、Realtimeセッションの上限、標準人格はバックエンド設定とする。デプロイなしで安全に調整できるよう、環境変数または検証済み設定ファイルから読み込む。

## テスト戦略

### Unity EditMode

`ObjectConversationCoordinatorTests`：

- 初回認識で会話を開始する。
- 同じ物体を10秒未満見失っても維持する。
- 10秒経過後に終了する。
- 10秒以内の再認識で継続する。
- 別物体が2秒連続した場合だけ切り替える。
- 短時間の誤認識では切り替えない。

`ConversationStateMachineTests`：

- 正常な開始、会話、停止、再接続の遷移。
- 不正な遷移と多重接続の拒否。
- 停止処理の冪等性。
- 古いセッション世代のイベント破棄。

`RealtimeEventParserTests`：

- 発話開始・終了、ユーザー文字起こし、AI応答、応答完了、エラーの解析。
- 未知イベントと不正JSONの安全な処理。

### バックエンド

- Firebase認証を必須とする。
- 入力フィールドの型、空値、最大長を検証する。
- 固定安全指示をクライアント入力で上書きできない。
- レート制限が動作する。
- 認証情報や会話本文がレスポンスとログへ漏れない。
- OpenAI障害を安全なエラーレスポンスへ変換する。

### Android実機

1. 初回と拒否後のマイク権限動作。
2. 物体認識から会話開始までの時間。
3. ユーザー音声とAI音声の双方向通信。
4. AI発話中の割り込み。
5. 10秒の認識ロスト猶予。
6. 2秒連続認識による別物体への切替。
7. Wi-Fiとモバイル回線の切替。
8. バックグラウンド移行と復帰。
9. 長時間会話時のメモリ、GC、音切れ、発熱。

## 完了条件

- APIキーや長期認証情報がUnityプロジェクトとビルドに含まれない。
- 認識した物体の名前と人格で会話が自動的に始まる。
- 通常会話で録音ボタンを必要としない。
- 同じ物体の10秒ロスト猶予が機能する。
- 別物体の2秒連続認識で安全に会話相手が切り替わる。
- AI発話へユーザーが自然に割り込める。
- 会話内容が端末、DB、バックエンドログへ保存されない。
- 音声機能が失敗しても既存の認識・登録機能が継続する。
- Unity EditModeテスト、バックエンドテスト、Android実機確認を通過する。

## 対象外

- 会話履歴の永続化・同期・閲覧。
- 複数物体が同時参加する会話。
- オフライン音声認識・音声合成。
- ユーザーが選択できる録音方式やプッシュ・トゥ・トーク。
- 音声ファイルの保存、共有、分析。
- 管理画面や会話分析ダッシュボード。
