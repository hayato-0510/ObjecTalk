using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealtimeVoiceChat
{
    /// <summary>
    /// マイク入力と音声出力を管理するクラス
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        [Header("マイク設定")]
        [SerializeField] private int sampleRate = 24000;
        [SerializeField] private int recordingLength = 1; // 秒
        [SerializeField] private float sendInterval = 0.1f; // 秒

        [Header("音声出力設定")]
        [SerializeField] private AudioSource audioSource;

        [Header("デバッグ")]
        [SerializeField] private bool enableDebugLog = true;

        private AudioClip microphoneClip;
        private bool isRecording = false;
        private int lastSamplePosition = 0;
        private float nextSendTime = 0f;
        private RealtimeVoiceClient client;

        // 音声出力用のキュー
        private Queue<byte[]> audioChunkQueue = new Queue<byte[]>();
        private bool isPlaying = false;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            client = GetComponent<RealtimeVoiceClient>();
        }

        /// <summary>
        /// マイク録音を開始
        /// </summary>
        public void StartRecording()
        {
            if (isRecording)
            {
                LogWarning("既に録音中です");
                return;
            }

            string microphoneName = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
            if (string.IsNullOrEmpty(microphoneName))
            {
                LogError("マイクが見つかりません");
                return;
            }

            Log($"マイク開始: {microphoneName}");
            microphoneClip = Microphone.Start(microphoneName, true, recordingLength, sampleRate);
            isRecording = true;
            lastSamplePosition = 0;
            nextSendTime = Time.time + sendInterval;

            Log("録音開始");
        }

        /// <summary>
        /// マイク録音を停止
        /// </summary>
        public void StopRecording()
        {
            if (!isRecording)
            {
                return;
            }

            Microphone.End(null);
            isRecording = false;
            Log("録音停止");
        }

        /// <summary>
        /// 音声再生を停止してキューをクリア
        /// </summary>
        public void StopPlayback()
        {
            // 再生中の音声を停止
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                Log("🔇 音声再生を停止");
            }

            // キューをクリア
            audioChunkQueue.Clear();
            isPlaying = false;
            Log("🗑️ 音声キューをクリア");
        }

        /// <summary>
        /// 音声キューにデータがあるかチェック
        /// </summary>
        public bool HasQueuedAudio()
        {
            return audioChunkQueue.Count > 0 || (audioSource != null && audioSource.isPlaying);
        }

        private void Update()
        {
            if (isRecording && Time.time >= nextSendTime)
            {
                ProcessMicrophoneData();
                nextSendTime = Time.time + sendInterval;
            }

            // 音声出力処理
            if (!isPlaying && audioChunkQueue.Count > 0)
            {
                StartCoroutine(PlayNextChunk());
            }
        }

        private void ProcessMicrophoneData()
        {
            if (microphoneClip == null || client == null)
            {
                return;
            }

            int currentPosition = Microphone.GetPosition(null);
            if (currentPosition < 0 || currentPosition == lastSamplePosition)
            {
                return;
            }

            int sampleCount = currentPosition - lastSamplePosition;
            if (sampleCount < 0)
            {
                sampleCount += microphoneClip.samples;
            }

            // サンプルデータを取得
            float[] samples = new float[sampleCount];
            microphoneClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = currentPosition;

            // Float32からInt16に変換
            byte[] audioData = ConvertToInt16(samples);

            // サーバーに送信
            client.SendAudioData(audioData);
        }

        /// <summary>
        /// Float32配列をInt16バイト配列に変換
        /// </summary>
        private byte[] ConvertToInt16(float[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            int byteIndex = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                float sample = Mathf.Clamp(samples[i], -1f, 1f);
                short intSample = (short)(sample * short.MaxValue);
                bytes[byteIndex++] = (byte)(intSample & 0xFF);
                bytes[byteIndex++] = (byte)((intSample >> 8) & 0xFF);
            }

            return bytes;
        }

        /// <summary>
        /// 音声チャンクを再生キューに追加
        /// </summary>
        public void PlayAudioChunk(byte[] audioData)
        {
            audioChunkQueue.Enqueue(audioData);
        }

        private System.Collections.IEnumerator PlayNextChunk()
        {
            if (audioChunkQueue.Count == 0)
            {
                yield break;
            }

            isPlaying = true;
            byte[] audioData = audioChunkQueue.Dequeue();

            // try-catchの外でAudioClipを作成
            AudioClip clip = null;
            bool hasError = false;
            string errorMessage = "";

            // データの変換と準備（エラーハンドリング付き）
            try
            {
                // Int16バイト配列をFloat32配列に変換
                float[] samples = ConvertToFloat32(audioData);

                // AudioClipを作成
                clip = AudioClip.Create("AudioChunk", samples.Length, 1, sampleRate, false);
                clip.SetData(samples, 0);

                // 再生
                audioSource.clip = clip;
                audioSource.Play();
            }
            catch (Exception ex)
            {
                hasError = true;
                errorMessage = ex.Message;
            }

            // エラーチェック
            if (hasError)
            {
                LogError($"音声再生エラー: {errorMessage}");
                isPlaying = false;
                yield break;
            }

            // 再生完了まで待機（try-catchの外）
            if (clip != null)
            {
                yield return new WaitForSeconds(clip.length);
            }

            isPlaying = false;
        }

        /// <summary>
        /// Int16バイト配列をFloat32配列に変換
        /// </summary>
        private float[] ConvertToFloat32(byte[] bytes)
        {
            int sampleCount = bytes.Length / 2;
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short intSample = (short)((bytes[i * 2 + 1] << 8) | bytes[i * 2]);
                samples[i] = intSample / (float)short.MaxValue;
            }

            return samples;
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        // ログメソッド
        private void Log(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[AudioManager] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLog)
            {
                Debug.LogWarning($"[AudioManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[AudioManager] {message}");
        }
    }
}
