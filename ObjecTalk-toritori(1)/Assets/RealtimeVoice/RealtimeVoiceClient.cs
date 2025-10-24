using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RealtimeVoiceChat
{
    /// <summary>
    /// OpenAI Realtime APIとWebSocket通信を行うクライアント
    /// </summary>
    public class RealtimeVoiceClient : MonoBehaviour
    {
        [Header("サーバー設定")]
        [SerializeField] private string serverUrl = "http://localhost:3001";
        [SerializeField] private string openAiRealtimeUrl = "wss://api.openai.com/v1/realtime";

        [Header("デバッグ")]
        [SerializeField] private bool enableDebugLog = true;

        // イベント
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;
        public event Action<string, string> OnTranscriptReceived; // role, text
        public event Action<byte[]> OnAudioReceived;
        public event Action<string> OnStatusChanged;

        // プロパティ
        public bool IsConnected { get; private set; }
        public string Voice { get; private set; }
        public string Model { get; private set; }
        public string SystemPrompt { get; private set; }

        private WebSocket websocket;
        private string ephemeralKey;
        private AudioManager audioManager;
        private bool isAIResponding = false; // AI応答中フラグ
        private string currentResponseId = null; // 現在の応答ID

        private void Awake()
        {
            Log("🔧 RealtimeVoiceClient Awake()");
            
            audioManager = GetComponent<AudioManager>();
            if (audioManager == null)
            {
                Log("⚠️ AudioManagerが見つからないため、自動的に追加します");
                audioManager = gameObject.AddComponent<AudioManager>();
            }
            
            Log($"✅ 初期化完了 - ServerURL: {serverUrl}");
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
#endif
        }

        /// <summary>
        /// サーバーに接続して音声会話を開始
        /// </summary>
        public void Connect()
        {
            Log("🚀 Connect()メソッドが呼ばれました");
            
            if (IsConnected)
            {
                LogWarning("⚠️ 既に接続されています");
                return;
            }

            Log("📞 接続処理を開始します");
            StartCoroutine(ConnectCoroutine());
        }

        /// <summary>
        /// 接続を切断
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
            {
                LogWarning("接続されていません");
                return;
            }

            StartCoroutine(DisconnectCoroutine());
        }

        private IEnumerator ConnectCoroutine()
        {
            Log("📋 ConnectCoroutine開始");
            OnStatusChanged?.Invoke("接続中...");

            // 1. 設定情報を取得
            Log("ステップ1: 設定情報を取得");
            yield return StartCoroutine(LoadConfig());

            // 2. Ephemeral Keyを取得
            Log("ステップ2: Ephemeral Keyを取得");
            yield return StartCoroutine(GetEphemeralKey());

            if (string.IsNullOrEmpty(ephemeralKey))
            {
                LogError("❌ Ephemeral Keyの取得に失敗しました");
                OnError?.Invoke("Ephemeral Keyの取得に失敗しました");
                OnStatusChanged?.Invoke("エラー");
                yield break;
            }

            // 3. WebSocket接続
            Log("ステップ3: WebSocket接続");
            yield return StartCoroutine(ConnectWebSocket());

            // 4. マイク入力開始
            audioManager.StartRecording();

            IsConnected = true;
            OnConnected?.Invoke();
            OnStatusChanged?.Invoke("接続済み");
            Log("接続完了");
        }

        private IEnumerator DisconnectCoroutine()
        {
            OnStatusChanged?.Invoke("切断中...");

            // マイク停止
            audioManager.StopRecording();

            // WebSocket切断
            if (websocket != null)
            {
                yield return websocket.Close();
                websocket = null;
            }

            IsConnected = false;
            ephemeralKey = null;
            OnDisconnected?.Invoke();
            OnStatusChanged?.Invoke("未接続");
            Log("切断完了");
        }

        private IEnumerator LoadConfig()
        {
            string url = $"{serverUrl}/api/config";
            Log($"設定情報を取得中: {url}");
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var json = JObject.Parse(request.downloadHandler.text);
                    Voice = json["voice"]?.ToString();
                    Model = json["model"]?.ToString();
                    SystemPrompt = json["systemPrompt"]?.ToString();
                    Log($"✅ 設定取得成功: Voice={Voice}, Model={Model}");
                    Log($"   SystemPrompt: {SystemPrompt?.Substring(0, Math.Min(100, SystemPrompt?.Length ?? 0))}...");
                }
                else
                {
                    LogError($"❌ 設定取得エラー: {request.error}");
                    LogError($"   Response Code: {request.responseCode}");
                }
            }
        }

        private IEnumerator GetEphemeralKey()
        {
            string url = $"{serverUrl}/api/session";
            Log($"🔑 Ephemeral Key を要求中: {url}");
            
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Log($"✅ サーバーからのレスポンス受信");
                    var json = JObject.Parse(request.downloadHandler.text);
                    ephemeralKey = json["client_secret"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(ephemeralKey))
                    {
                        Log($"✅ Ephemeral Key取得成功: {ephemeralKey.Substring(0, 20)}...");
                    }
                    else
                    {
                        LogError($"❌ Ephemeral Keyがレスポンスに含まれていません");
                        LogError($"   Response: {request.downloadHandler.text}");
                    }
                }
                else
                {
                    LogError($"❌ Ephemeral Key取得エラー: {request.error}");
                    LogError($"   Response Code: {request.responseCode}");
                    if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    {
                        LogError($"   Response Body: {request.downloadHandler.text}");
                    }
                }
            }
        }

        private IEnumerator ConnectWebSocket()
        {
            // エフェメラルキーを使ってOpenAI Realtime APIに直接接続
            string wsUrl = $"{openAiRealtimeUrl}?model={Model}";
            Log($"🔌 OpenAI Realtime APIに直接接続開始: {wsUrl}");
            Log($"🔑 使用するEphemeral Key: {ephemeralKey?.Substring(0, 20)}...");
            
            // NativeWebSocketでヘッダーを設定して接続
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {ephemeralKey}" },
                { "OpenAI-Beta", "realtime=v1" }
            };
            
            websocket = new WebSocket(wsUrl, headers);

            websocket.OnOpen += () =>
            {
                Log("✅ OpenAI Realtime API接続成功");
                
                // セッション設定を送信
                var sessionUpdate = new
                {
                    type = "session.update",
                    session = new
                    {
                        modalities = new[] { "text", "audio" },
                        instructions = SystemPrompt ?? "あなたは親しみやすいアシスタントです。",
                        voice = Voice?.ToLower() ?? "alloy",
                        input_audio_format = "pcm16",
                        output_audio_format = "pcm16",
                        input_audio_transcription = new
                        {
                            model = "whisper-1"
                        },
                        turn_detection = new
                        {
                            type = "server_vad",
                            threshold = 0.5,
                            prefix_padding_ms = 300,
                            silence_duration_ms = 500
                        }
                    }
                };
                string json = JsonConvert.SerializeObject(sessionUpdate);
                Log($"📤 セッション設定を送信");
                Log($"   Instructions: {(SystemPrompt ?? "デフォルト").Substring(0, Math.Min(100, (SystemPrompt ?? "デフォルト").Length))}...");
                websocket.SendText(json);
            };

            websocket.OnMessage += (bytes) =>
            {
                string message = Encoding.UTF8.GetString(bytes);
                HandleServerMessage(message);
            };

            websocket.OnError += (error) =>
            {
                LogError($"WebSocketエラー: {error}");
                OnError?.Invoke(error);
            };

            websocket.OnClose += (code) =>
            {
                Log($"WebSocket切断: {code}");
            };

            yield return websocket.Connect();
        }

        private void HandleServerMessage(string message)
        {
            try
            {
                var json = JObject.Parse(message);
                string type = json["type"]?.ToString();

                Log($"受信: {type}");

                switch (type)
                {
                    case "connection.established":
                        Log("OpenAI Realtime API接続完了");
                        break;

                    case "session.created":
                    case "session.updated":
                        Log("セッション設定完了");
                        break;

                    case "input_audio_buffer.speech_started":
                        // ユーザーの音声入力開始 → AI応答を中断
                        Log("🎤 ユーザー音声入力開始");
                        if (isAIResponding && !string.IsNullOrEmpty(currentResponseId))
                        {
                            Log("⏸️ AI応答を中断します");
                            CancelCurrentResponse();
                            
                            // 音声再生も停止
                            if (audioManager != null)
                            {
                                audioManager.StopPlayback();
                            }
                        }
                        else
                        {
                            // 応答中でない場合は、念のため音声キューだけクリア
                            if (audioManager != null && audioManager.HasQueuedAudio())
                            {
                                Log("🗑️ 残っている音声キューをクリア");
                                audioManager.StopPlayback();
                            }
                        }
                        break;

                    case "input_audio_buffer.speech_stopped":
                        Log("🎤 ユーザー音声入力終了");
                        break;

                    case "conversation.item.input_audio_transcription.completed":
                        // ユーザーの音声テキスト
                        string userText = json["transcript"]?.ToString();
                        if (!string.IsNullOrEmpty(userText))
                        {
                            OnTranscriptReceived?.Invoke("user", userText);
                        }
                        break;

                    case "response.created":
                        // 応答開始
                        currentResponseId = json["response"]?["id"]?.ToString();
                        isAIResponding = true;
                        Log($"🤖 AI応答開始: {currentResponseId}");
                        break;

                    case "response.audio_transcript.delta":
                        // AIの音声テキスト
                        string aiText = json["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(aiText))
                        {
                            OnTranscriptReceived?.Invoke("assistant", aiText);
                        }
                        break;

                    case "response.audio.delta":
                        // 音声データ
                        string base64Audio = json["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(base64Audio))
                        {
                            byte[] audioData = Convert.FromBase64String(base64Audio);
                            OnAudioReceived?.Invoke(audioData);
                            audioManager.PlayAudioChunk(audioData);
                        }
                        break;

                    case "response.done":
                        Log("✅ 応答完了");
                        isAIResponding = false;
                        currentResponseId = null;
                        break;

                    case "response.cancelled":
                        Log("🚫 応答がキャンセルされました");
                        isAIResponding = false;
                        currentResponseId = null;
                        break;

                    case "error":
                        string errorMsg = json["error"]?["message"]?.ToString() ?? json["message"]?.ToString();
                        string errorCode = json["error"]?["code"]?.ToString() ?? json["code"]?.ToString();
                        
                        // キャンセル関連のエラーは警告レベルで処理
                        if (errorMsg != null && errorMsg.Contains("no active response"))
                        {
                            LogWarning($"⚠️ キャンセル警告: {errorMsg}");
                            isAIResponding = false;
                            currentResponseId = null;
                        }
                        else
                        {
                            LogError($"❌ エラー [{errorCode}]: {errorMsg}");
                            OnError?.Invoke(errorMsg);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"メッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のAI応答をキャンセル
        /// </summary>
        private void CancelCurrentResponse()
        {
            if (!IsConnected || websocket == null || websocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                // response.cancelイベントを送信
                var cancelMessage = new
                {
                    type = "response.cancel"
                };

                string json = JsonConvert.SerializeObject(cancelMessage);
                websocket.SendText(json);
                Log("📤 response.cancel送信");
            }
            catch (Exception ex)
            {
                LogError($"応答キャンセルエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 音声データを送信
        /// </summary>
        public void SendAudioData(byte[] audioData)
        {
            if (!IsConnected || websocket == null || websocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                // Int16からBase64に変換
                string base64Audio = Convert.ToBase64String(audioData);

                var message = new
                {
                    type = "input_audio_buffer.append",
                    audio = base64Audio
                };

                string json = JsonConvert.SerializeObject(message);
                websocket.SendText(json);
            }
            catch (Exception ex)
            {
                LogError($"音声送信エラー: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            if (IsConnected)
            {
                StartCoroutine(DisconnectCoroutine());
            }
        }

        private void OnApplicationQuit()
        {
            websocket?.Close();
        }

        // ログメソッド
        private void Log(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[RealtimeVoiceClient] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLog)
            {
                Debug.LogWarning($"[RealtimeVoiceClient] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[RealtimeVoiceClient] {message}");
        }
    }
}
