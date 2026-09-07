# ObjecTalk Realtime Backend

このバックエンドは、UnityクライアントのFirebase IDトークンを検証し、OpenAI Realtime APIとのWebRTC接続に必要なSDPを交換します。OpenAI APIキー、音声、文字起こし、会話履歴はクライアントへ返したり保存したりしません。

## 必要な環境

- Node.js 20以上
- FirebaseプロジェクトとFirebase Admin用サービスアカウント
- OpenAI APIキー

依存関係をインストールします。

```powershell
npm ci
```

## ローカル設定

`.env.example`を`.env`へコピーし、次の値を設定します。`.env`とサービスアカウントJSONはGitへ追加しないでください。

```dotenv
OPENAI_API_KEY=your-key
FIREBASE_PROJECT_ID=your-project-id
GOOGLE_APPLICATION_CREDENTIALS=C:\absolute\path\to\service-account.json
PORT=8080
OPENAI_REALTIME_MODEL=gpt-realtime-2.1
OPENAI_REALTIME_VOICE=marin
OPENAI_REALTIME_FEMININE_VOICES=marin,coral,shimmer
OPENAI_REALTIME_MASCULINE_VOICES=cedar,ash,echo
OPENAI_REALTIME_NEUTRAL_VOICES=alloy,sage,verse,ballad
```

会話開始時の声は物体名と物体IDから決定します。名前が「ちゃん」で終わる場合は
`OPENAI_REALTIME_FEMININE_VOICES`、「くん」で終わる場合は
`OPENAI_REALTIME_MASCULINE_VOICES`、それ以外は
`OPENAI_REALTIME_NEUTRAL_VOICES`から選ばれます。同じ物体IDには常に同じ声が割り当てられます。
OpenAIは組み込み音声に性別を付けていないため、この分類はObjecTalk側の運用上の設定です。

ローカル用コマンドは`.env`を読み込んで起動します。

```powershell
npm run start:local
```

環境変数を実行環境から渡す本番環境では、通常の起動コマンドを使用します。

```powershell
npm start
```

起動確認:

```powershell
Invoke-RestMethod http://localhost:8080/health
```

正常時は`{"status":"ok"}`が返ります。

## Android実機からローカルへ接続する

USB接続したAndroid端末からPCのバックエンドへ接続する場合は、ポートを転送します。

```powershell
adb reverse tcp:8080 tcp:8080
```

Unityの`TalkConfiguration`では、開発用URLを`http://localhost:8080`に設定します。ループバックHTTPはEditorまたはDevelopment Buildでのみ許可されます。端末とPCが別ネットワークの場合や本番ビルドでは、HTTPSのバックエンドを使用してください。

## Docker

イメージに秘密情報を含めず、起動時に環境変数と認証情報を渡します。

```powershell
docker build -t objectalk-realtime-backend .
docker run --rm -p 8080:8080 `
  -e OPENAI_API_KEY `
  -e FIREBASE_PROJECT_ID `
  -e GOOGLE_APPLICATION_CREDENTIALS=/run/secrets/firebase.json `
  -e PORT=8080 `
  -v "C:\absolute\path\to\service-account.json:/run/secrets/firebase.json:ro" `
  objectalk-realtime-backend
```

本番ではコンテナ基盤のSecret Managerを使い、`.env`やJSONをイメージへコピーしないでください。

## 検証

```powershell
npm test
npm audit --omit=dev
```

`npm audit` は `firebase-admin` の推移的依存（`@google-cloud/storage` / `-firestore` →
`teeny-request` / `uuid`）にmoderate 8件を報告します。このバックエンドは
`firebase-admin` を `verifyIdToken` にしか使わず Firestore / Storage 経路も
`uuid` の `buf` 引数も通らないため、現時点で悪用可能な経路はありません。公開デプロイ前に
`npm update firebase-admin` と、必要なら `package.json` の `overrides` で解消してください。

実機確認では、音声や文字起こしをログへ残さず、端末名とOS、結果だけを記録します。

| 確認項目 | 状態 |
| --- | --- |
| マイク許可／拒否 | 未実施 |
| 認識後の自動会話開始 | 未実施 |
| ユーザー発話による応答中断 | 未実施 |
| 認識喪失後10秒の維持 | 未実施 |
| 別物体を2秒認識した後の切り替え | 未実施 |
| Wi-Fi／モバイル回線の切り替え | 未実施 |
| アプリの一時停止／再開 | 未実施 |
| 20分間の連続会話 | 未実施 |

## キーのローテーション

1. OpenAIまたはFirebaseで新しい認証情報を発行する。
2. デプロイ環境のSecret Managerを新しい値へ更新する。
3. バックエンドを再起動し、`/health`と認証付きRealtime接続を確認する。
4. 古い認証情報を失効させる。
5. ログへキー、Firebase IDトークン、SDP、会話内容が出ていないことを確認する。

キー漏えいが疑われる場合は、確認を待たずに該当キーを失効させてください。
