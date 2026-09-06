# ObjecTalk

ObjecTalkは、登録した物体をカメラで認識し、その物体の情報を使ってリアルタイム音声会話を行うUnity 6アプリケーションです。

## 必要な環境

- Unity `6000.1.7f1`
- Android SDK（Android実機で確認する場合）
- Node.js 20以上（Realtimeバックエンドを動かす場合）
- Firebaseプロジェクト
- OpenAI APIキー

## 音声会話の構成

UnityクライアントはOpenAIへAPIキーを送信しません。Firebase IDトークンを付けてObjecTalkバックエンドへ接続し、バックエンドが認証とOpenAI Realtimeセッションの作成を担当します。

Talk画面では、認識した物体に対して自動的に会話を開始します。認識を失ってから10秒間は会話を維持し、別の物体を2秒間連続して認識すると会話対象を切り替えます。

## Unity側の設定

`Assets/Scenes/ObjectDetectionScene.unity` の `TalkConversationRoot` にある `TalkConfiguration` で接続先を設定します。

- 開発時: ループバックHTTPまたはHTTPSを使用できます。
- 本番時: HTTPSのみ使用できます。
- APIキーやFirebaseサービスアカウントをUnityのInspector、Assets、Resources、StreamingAssetsへ保存しないでください。

Android実機からローカルバックエンドへ接続する手順は、`Backend/README.md`を参照してください。

## テスト

Unity Editorを開いた状態で、uloopからEditMode／PlayModeテストを実行します。

```powershell
uloop compile
uloop run-tests --test-mode EditMode
uloop run-tests --test-mode PlayMode
```

バックエンドのテストはリポジトリ直下の`Backend`で実行します。

```powershell
cd Backend
npm ci
npm test
```

## 設計資料

- `docs/superpowers/specs/2026-09-04-object-aware-realtime-voice-conversation-design.md`
- `docs/superpowers/plans/2026-09-04-object-aware-realtime-voice-conversation.md`
