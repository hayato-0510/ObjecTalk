# 開発時の実機動作確認手順

物体認識からリアルタイム音声会話までの一連の流れを、Android実機で確認するための手順をまとめる。ローカルで動かしているバックエンドに実機をつなぎ、チェックリストを順に消し込むところまでを対象にする。

- **1〜4章は最初に通読する。** ここまで読めば、実機を接続してチェックリストを回せる状態になる。
- **5章以降は必要なときに引く。** 記録の付け方と、つまずいたときの対処をまとめてある。

## このドキュメントの範囲

APKのビルド方法とバックエンドの本番デプロイは扱わない。ビルドは別途プロジェクト内のビルド手順に従い、ここでは「ビルド済みのdev APKを実機に入れ、ローカルのバックエンドに向けて動作を確認する」ところだけを書く。

回線切り替えのように、USB経由のローカル接続では十分に確認できない項目がある。その旨は各項目に明記した。すべてを検証しきるには、HTTPSで公開したバックエンドが必要になる。

## 用意するもの

| もの | 補足 |
| --- | --- |
| Android実機 | USBデバッグを有効にしておく。動作実績はSamsung SC-03L / Android 12 / arm64 |
| dev APK | 実機にインストール済みであること（`Builds/Android/ObjecTalk-dev.apk`） |
| バックエンドを動かすPC | Node.js 20以上。実機とUSBケーブルで接続する |
| Firebase Admin用サービスアカウントJSON | バックエンドの認証に使う。Gitには入れない |
| OpenAI APIキー | バックエンドの`.env`に設定する |
| `adb` | Android SDK Platform Toolsに含まれる。PATHを通しておく |

## 手順

### バックエンドをローカルで起動する

リポジトリ直下の`Backend`で、`.env.example`を`.env`にコピーして値を入れる。最低限、`OPENAI_API_KEY`、`FIREBASE_PROJECT_ID`、`GOOGLE_APPLICATION_CREDENTIALS`（サービスアカウントJSONの絶対パス）、`PORT`、`OPENAI_REALTIME_VOICE`を設定する。`PORT`はこの手順では`8080`を使う。

```powershell
cd Backend
npm ci
npm run start:local
```

別のPowerShellで起動を確認する。`{"status":"ok"}`が返れば起動できている。

```powershell
Invoke-RestMethod http://localhost:8080/health
```

### 実機をつないでポートを転送する

実機をUSBでつなぎ、認識されているか確認する。

```powershell
adb devices
```

実機側の`localhost:8080`へのアクセスをPCへ転送する。これで実機のアプリからPCのバックエンドに届く。

```powershell
adb reverse tcp:8080 tcp:8080
```

`adb reverse`は端末を再接続すると消える。USBを挿し直したときや`adb`サーバーが再起動したときは、もう一度実行する。

### Unity側の接続先を合わせる

`Assets/Scenes/ObjectDetectionScene.unity`の`TalkConversationRoot`にある`TalkConfiguration`で、次のように設定する。

- `Use Development Backend` にチェックを入れる
- `Development Backend Base Url` を `http://localhost:8080` にする

このURLのポートは`.env`の`PORT`と一致させる。`TalkConfiguration`の初期値は`http://localhost:3000`なので、必ず書き換える。ループバックのHTTP（`http://localhost:...`）はEditorまたはDevelopment Buildでのみ許可される。Release Buildや、実機とPCが別ネットワークにある構成では、HTTPSのバックエンドが必要になる。

設定を変えたらdev APKをビルドし直して実機に入れる。

### アプリを起動してログを見る

実機でアプリを起動し、ログイン後にTalk画面まで進む。PC側でlogcatを流しておくと、接続の成否と状態遷移を追える。

```powershell
adb logcat -s Unity
```

`http://localhost:8080`への接続エラーが出る場合は、`adb reverse`が効いているか、`npm run start:local`が動いているかを先に確認する。

## 動作確認チェックリスト

登録済みの物体を2つ用意してから始める。各項目の「期待結果」を満たすか、`○`／`×`で記録する。

| 確認項目 | 操作 | 期待結果 |
| --- | --- | --- |
| マイク許可 | 初回起動時のOS許可ダイアログで「許可」を選ぶ | 物体を認識すると会話が始まる |
| マイク拒否 | 許可ダイアログで「拒否」を選ぶ | 会話は始まらないが、物体認識は動き続け、アプリは落ちない |
| 認識後の自動会話開始 | 登録済みの物体にカメラを向ける | 数秒で「接続中」→「聞いています」と表示され、物体の声で応答が返る |
| ユーザー発話による応答中断 | AIが話している最中に話しかける | AIの音声が止まり、こちらの発話が優先される |
| 認識喪失後10秒の維持 | 会話中に物体を画面外へ外す | 10秒間は会話が続き、10秒を超えると会話が終わる |
| 別物体への切り替え | もう一方の登録済み物体を2秒以上続けて映す | 会話相手がその物体に切り替わる。1〜2秒未満のちらつきでは切り替わらない |
| アプリの一時停止／再開 | ホームボタンで背面に回し、しばらくして戻る | 背面で会話が停止し、復帰後に破綻なく再開できる |
| 20分間の連続会話 | 20分間、会話を続ける | 途中で切断されず、音声の劣化やメモリ増加による不安定さが出ない |
| Wi-Fi／モバイル回線の切り替え | 会話中に回線を切り替える | 「再接続中」の表示を挟んで会話に復帰する（下記の注意を参照） |

回線切り替えの項目は、`adb reverse`によるUSB経由の接続では正しく確認できない。localhostへの通信がUSBを通るため、端末側の回線を切り替えても影響しないためだ。この項目を実機で確認するには、HTTPSで公開したバックエンドに向ける必要がある。

## 記録の付け方

会話の音声と文字起こしはログに残さない。記録するのは端末名、OSバージョン、各項目の結果だけにする。結果は`Backend/README.md`の確認表、またはプロジェクトのハンドオフノートに追記する。

## うまくいかないとき

- **`/health`が返らない** → `npm run start:local`が起動しているか、`.env`の`PORT`が`8080`か、別プロセスがポートを使っていないかを確認する。
- **実機から接続できない** → `adb reverse tcp:8080 tcp:8080`を再実行する。USBを挿し直すと転送設定は消える。
- **「音声会話の接続先が設定されていません」と表示される** → `TalkConfiguration`の`Development Backend Base Url`が空か、URLとして不正。`http://localhost:8080`を入れる。
- **「開発用の接続先にはHTTPSまたはループバックHTTPを指定してください」と表示される** → Release BuildでループバックHTTPを使っている。dev APK（Development Build）でビルドし直す。
- **会話が始まらない** → マイク権限を確認する。端末の設定 → アプリ → 権限から手動で許可し直せる。

## 関連ドキュメント

- ローカル設定・ポート転送・Docker・キーのローテーション: [`Backend/README.md`](../README.md)
- 音声会話の構成とUnity側の設定: [`ObjecTalk-toritori(1)/README.md`](../../ObjecTalk-toritori(1)/README.md)
- 設計資料: [`object-aware-realtime-voice-conversation-design.md`](../../ObjecTalk-toritori(1)/docs/superpowers/specs/2026-09-04-object-aware-realtime-voice-conversation-design.md)

---

最終更新: 2026-09-07
