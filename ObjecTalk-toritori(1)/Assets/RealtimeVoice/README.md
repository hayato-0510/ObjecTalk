# 🎙️ Unity リアルタイム音声会話システム

OpenAI Realtime APIを使用したUnity統合のリアルタイム音声会話アプリケーション

## ✨ 特徴

- **Unity完全統合**: C#スクリプトによるネイティブ実装
- **エフェメラルキー方式**: セキュアなAPIキー管理
- **WebSocket通信**: リアルタイムの双方向音声通信
- **Docker環境**: Node.js 22.12による簡単なサーバーセットアップ
- **音声認識・合成**: OpenAI Realtime APIによる高品質な音声処理
- **クロスプラットフォーム**: Windows、Android、iOS、Mac、WebGLで動作

## 🏗️ プロジェクト構造

```
toritori-2/
├── realtime-voice-chat/      # Node.jsサーバー
│   ├── src/backend/
│   │   ├── server.js         # バックエンドサーバー (WebSocket対応)
│   │   └── ai.config.js      # AI設定ファイル
│   ├── docker-compose.yml    # Docker Compose設定
│   ├── Dockerfile            # Dockerイメージ定義
│   ├── package.json          # Node.js依存関係
│   └── .env                  # 環境変数（要作成）
├── Assets/Scripts/           # Unity C#スクリプト
│   ├── RealtimeVoiceClient.cs  # WebSocketクライアント
│   ├── AudioManager.cs         # 音声入出力管理
│   ├── UIManager.cs            # UI管理
│   └── RealtimeVoiceChat.asmdef
├── UNITY_SETUP.md            # Unity詳細セットアップガイド
├── QUICKSTART_UNITY.md       # Unity クイックスタート
└── README.md                 # このファイル (サーバー設定)
```

## 🚀 サーバーセットアップ手順

このREADMEはNode.jsバックエンドサーバーの設定について説明します。
**Unity側のセットアップは [QUICKSTART_UNITY.md](../QUICKSTART_UNITY.md) を参照してください。**

### 前提条件

- Docker Desktop がインストールされていること
- OpenAI APIキーを取得していること
- Unity 2021.3以上

### 1. サーバーディレクトリに移動

```bash
cd d:\toritori-2\realtime-voice-chat
```

### 2. 環境変数の設定

`.env.example`を`.env`にコピーして、APIキーを設定します。

```bash
# Windowsの場合
copy .env.example .env

# Linux/Macの場合
cp .env.example .env
```

`.env`ファイルを編集して、OpenAI APIキーを設定してください：

```env
OPENAI_API_KEY=sk-your-actual-api-key-here
NODE_ENV=development
```

### 3. Dockerコンテナの起動

```bash
docker-compose up --build
```

初回起動時は依存関係のインストールが行われるため、数分かかる場合があります。

### 4. サーバー動作確認

サーバーが正常に起動すると、以下のようなログが表示されます：

```
✅ Backend server running on http://localhost:3001
🎙️  Voice: alloy
🤖 Model: gpt-4o-realtime-preview-2024-12-17
💬 System Prompt: あなたは親しみやすく...
🔌 WebSocket server ready
```

### 5. Unityクライアントの起動

Unityクライアントのセットアップと実行については、以下を参照：
- **クイックスタート**: [QUICKSTART_UNITY.md](../QUICKSTART_UNITY.md)
- **詳細設定**: [UNITY_SETUP.md](../UNITY_SETUP.md)

## 🎯 Unityでの使用方法

1. **サーバー起動**: このディレクトリで `docker-compose up`
2. **Unity起動**: Unityエディタでプロジェクトを開く
3. **接続開始**: Unity上で「接続開始」ボタンをクリック
4. **マイク許可**: マイクアクセス許可を承認（初回のみ）
5. **会話開始**: マイクに向かって話しかける
6. **AIの応答**: AIが音声で返答します
7. **接続終了**: 「接続終了」ボタンで会話を終了

詳細は [QUICKSTART_UNITY.md](../QUICKSTART_UNITY.md) を参照してください。

## ⚙️ 設定のカスタマイズ

### AI設定の変更

`src/backend/ai.config.js`で以下の設定を変更できます：

```javascript
export const AI_CONFIG = {
    voice: 'alloy',  // 音声の種類を変更
    model: 'gpt-4o-realtime-preview-2024-12-17',
    roleKeyword: null,  // 'メイド'などのキーワードで自動ロール生成
    baseSystemPrompt: `...`,  // システムプロンプトをカスタマイズ
};
```

#### 利用可能な音声の種類

- `alloy` - 中性的でバランスの取れた声
- `echo` - 男性的で落ち着いた声
- `fable` - イギリス英語的な声
- `onyx` - 深みのある男性的な声
- `nova` - 明るく活気のある女性的な声
- `shimmer` - 優しく穏やかな女性的な声

### ロールプロンプトの自動生成

`roleKeyword`を設定すると、GPT-4が自動的にキャラクター設定を生成します：

```javascript
roleKeyword: '猫娘',  // 猫娘キャラクターのプロンプトを自動生成
```

## 🛠️ 開発モード

### Docker外でローカル実行する場合

```bash
# 依存関係のインストール
npm install

# サーバーの起動
npm run dev
# または
npm start
```

サーバーは http://localhost:3001 で起動します。

## �️ 技術スタック

### バックエンド
- **Node.js 22.12**: JavaScript実行環境
- **Express**: Webフレームワーク
- **ws**: WebSocketライブラリ
- **axios**: HTTPクライアント
- **dotenv**: 環境変数管理

### Webフロントエンド
- **Vite**: 高速ビルドツール
- **Web Audio API**: 音声処理
- **WebSocket API**: リアルタイム通信
- **Vanilla JavaScript**: フレームワークレス

### Unityクライアント
- **Unity 2021.3+**: ゲームエンジン
- **C#**: プログラミング言語
- **NativeWebSocket**: WebSocket通信
- **Newtonsoft.Json**: JSON処理
- **TextMeshPro**: UI表示

### インフラ
- **Docker**: コンテナ化
- **Docker Compose**: マルチコンテナ管理

## 📝 APIエンドポイント

### REST API

- `GET /api/config` - AI設定情報を取得
  - レスポンス: `{ voice, model, systemPrompt, roleKeyword }`
  
- `POST /api/session` - エフェメラルキーを生成
  - レスポンス: `{ client_secret, expires_at }`

### WebSocket

- `ws://localhost:3001` - WebSocket接続エンドポイント
  - クライアント → サーバー:
    - `{ type: "connect", ephemeralKey: "..." }`
    - `{ type: "input_audio_buffer.append", audio: "base64..." }`
  - サーバー → クライアント:
    - `{ type: "connection.established" }`
    - `{ type: "response.audio.delta", delta: "base64..." }`
    - `{ type: "conversation.item.input_audio_transcription.completed", transcript: "..." }`
    - など

## 🐛 サーバー側トラブルシューティング

### サーバーが起動しない

1. `.env`ファイルにAPIキーが正しく設定されているか確認
2. Docker Desktopが起動しているか確認
3. ポート3001が他のプロセスで使用されていないか確認
   ```powershell
   netstat -ano | findstr :3001
   ```

### WebSocket接続エラー

1. サーバーのログを確認: `docker-compose logs -f`
2. ファイアウォール設定を確認
3. CORS設定を確認（デフォルトは`*`で全許可）

### OpenAI APIエラー

1. APIキーが有効か確認
2. APIの使用制限を確認
3. サーバーログでエラー詳細を確認

### Unity側の問題

Unity側のトラブルシューティングは以下を参照：
- [QUICKSTART_UNITY.md](../QUICKSTART_UNITY.md#-よくある問題と解決策)
- [UNITY_SETUP.md](../UNITY_SETUP.md#-トラブルシューティング)

## 📚 関連ドキュメント

### このプロジェクト
- [QUICKSTART_UNITY.md](../QUICKSTART_UNITY.md) - Unity クイックスタート（5分で開始）
- [UNITY_SETUP.md](../UNITY_SETUP.md) - Unity 詳細セットアップガイド

### 外部リソース
- [OpenAI Realtime API Documentation](https://platform.openai.com/docs/guides/realtime)
- [NativeWebSocket for Unity](https://github.com/endel/NativeWebSocket)
- [Unity Microphone API](https://docs.unity3d.com/ScriptReference/Microphone.html)

## 📄 ライセンス

MIT License

## ⚠️ 注意事項

- **API料金**: OpenAI Realtime APIの使用には料金が発生します
- **コスト管理**: リアルタイム音声通信は比較的高コストなので、使用量に注意
- **セキュリティ**: 本番環境では以下を実施してください：
  - CORS設定を適切に制限
  - APIキーの厳重な管理
  - HTTPS/WSS通信の使用
  - レート制限の実装

## 🚀 次のステップ

1. ✅ サーバーを起動（このREADME）
2. 📱 [Unityクライアントをセットアップ](../QUICKSTART_UNITY.md)
3. 🎨 UIをカスタマイズ
4. 🎭 キャラクター設定を変更（`ai.config.js`）
5. 🎮 ゲームロジックと統合
