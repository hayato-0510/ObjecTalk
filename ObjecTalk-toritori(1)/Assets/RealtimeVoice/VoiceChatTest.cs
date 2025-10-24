using UnityEngine;
using RealtimeVoiceChat;

namespace RealtimeVoiceChat
{
    /// <summary>
    /// RealtimeVoiceClientをテストするための簡単なコントローラー
    /// 物体認識トリガー専用バージョン（自動接続なし）
    /// </summary>
    public class VoiceChatTest : MonoBehaviour
    {
        [Header("起動制御")]
        [Tooltip("Start後に自動接続するか（物体認識トリガー使用時はfalse推奨）")]
        public bool autoConnectOnStart = false;

        [Tooltip("自動接続までの待機時間（秒）")]
        public float autoConnectDelay = 1f;

        private RealtimeVoiceClient client;
        private float autoConnectTimer = 0f;
        private bool autoConnectTriggered = false;

        void Start()
        {
            Debug.Log("═══════════════════════════════════════");
            Debug.Log("🧪 VoiceChatTest Start()");
            Debug.Log("═══════════════════════════════════════");
            
            // RealtimeVoiceClientを取得
            client = FindFirstObjectByType<RealtimeVoiceClient>();
            
            if (client == null)
            {
                Debug.LogError("❌ RealtimeVoiceClientが見つかりません！");
                return;
            }

            Debug.Log("✅ RealtimeVoiceClientを発見しました");

            // イベントを登録
            client.OnConnected += OnConnected;
            client.OnDisconnected += OnDisconnected;
            client.OnError += OnError;
            client.OnTranscriptReceived += OnTranscriptReceived;
            client.OnStatusChanged += OnStatusChanged;

            Debug.Log("✅ イベントを登録しました");
            Debug.Log($"⚙️ 自動接続設定: {autoConnectOnStart}");
            Debug.Log("═══════════════════════════════════════");
        }

        void Update()
        {
            // 自動接続が有効な場合のみ実行
            if (autoConnectOnStart && !autoConnectTriggered && client != null && !client.IsConnected)
            {
                autoConnectTimer += Time.deltaTime;
                if (autoConnectTimer >= autoConnectDelay)
                {
                    autoConnectTriggered = true;
                    Debug.Log($"🚀 [VoiceChatTest] 自動接続を開始します（{autoConnectDelay}秒経過）");
                    StartConnection();
                }
            }
        }

        // 公開メソッド：外部から呼び出し可能
        public void StartConnection()
        {
            Debug.Log("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Debug.Log("🟢 StartConnection() 呼び出し");
            Debug.Log($"   Time: {System.DateTime.Now:HH:mm:ss.fff}");
            
            // 詳細なチェック
            Debug.Log($"   1. client オブジェクト: {(client != null ? "存在する" : "NULL")}");
            
            if (client == null)
            {
                Debug.LogError("❌ RealtimeVoiceClientがnullです！再取得を試みます...");
                client = FindFirstObjectByType<RealtimeVoiceClient>();
                
                if (client == null)
                {
                    Debug.LogError("❌ 再取得も失敗！シーンにRealtimeVoiceClientが存在しません！");
                    return;
                }
                else
                {
                    Debug.Log("✅ RealtimeVoiceClientを再取得しました");
                }
            }

            Debug.Log($"   2. client.IsConnected: {client.IsConnected}");
            Debug.Log($"   3. client.gameObject.name: {client.gameObject.name}");
            Debug.Log($"   4. client.gameObject.activeInHierarchy: {client.gameObject.activeInHierarchy}");
            Debug.Log($"   5. client.enabled: {client.enabled}");

            if (client.IsConnected)
            {
                Debug.LogWarning("⚠️ 既に接続済みです。再接続はスキップします。");
                Debug.Log($"   → 一度切断してから再接続が必要な場合は StopConnection() を呼んでください");
                return;
            }

            Debug.Log("▶️▶️▶️ client.Connect() を呼び出します...");
            try
            {
                client.Connect();
                Debug.Log("✅ client.Connect() 呼び出し完了");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Connect()でエラー発生: {e.Message}");
                Debug.LogError($"スタックトレース: {e.StackTrace}");
            }
            Debug.Log("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        public void StopConnection()
        {
            Debug.Log("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Debug.Log("🔴 StopConnection() 呼び出し");
            Debug.Log($"   client != null: {client != null}");
            Debug.Log($"   client.IsConnected: {client?.IsConnected}");

            if (client == null)
            {
                Debug.LogError("❌ RealtimeVoiceClientがnullです！");
                return;
            }

            if (!client.IsConnected)
            {
                Debug.LogWarning("ℹ️ 既に切断されています。");
                return;
            }

            Debug.Log("▶️ client.Disconnect() を呼び出します...");
            try
            {
                client.Disconnect();
                Debug.Log("✅ client.Disconnect() 呼び出し完了");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Disconnect()でエラー発生: {e.Message}");
                Debug.LogError($"スタックトレース: {e.StackTrace}");
            }
            Debug.Log("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        // イベントハンドラー
        void OnConnected()
        {
            Debug.Log("✅✅✅ 接続完了！音声会話が可能です ✅✅✅");
        }

        void OnDisconnected()
        {
            Debug.Log("🔌 切断しました");
        }

        void OnError(string error)
        {
            Debug.LogError($"❌ エラー: {error}");
        }

        void OnTranscriptReceived(string role, string text)
        {
            Debug.Log($"💬 [{role}] {text}");
        }

        void OnStatusChanged(string status)
        {
            Debug.Log($"📊 ステータス: {status}");
        }

        void OnDestroy()
        {
            Debug.Log("🗑️ VoiceChatTest OnDestroy - イベント登録解除");
            
            // イベント登録解除
            if (client != null)
            {
                client.OnConnected -= OnConnected;
                client.OnDisconnected -= OnDisconnected;
                client.OnError -= OnError;
                client.OnTranscriptReceived -= OnTranscriptReceived;
                client.OnStatusChanged -= OnStatusChanged;
                Debug.Log("✅ イベント登録を解除しました");
            }
        }
    }
}