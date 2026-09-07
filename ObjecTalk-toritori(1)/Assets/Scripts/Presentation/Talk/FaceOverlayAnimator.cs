using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Presentation.Talk
{
    /// <summary>
    /// Brings the recognition face overlay to life while an object talks: the mouth opens and closes
    /// with the voice, and the eyes blink now and then. It only touches vertical scale, so it rides
    /// on top of whatever position/size <c>EmbeddingObjectMatcher</c> gives the images, and it
    /// self-gates on the mouth image being active (the matcher toggles that with the overlay).
    /// The voice AudioSource is handed in at runtime by <see cref="TalkController"/>.
    /// </summary>
    public sealed class FaceOverlayAnimator : MonoBehaviour
    {
        [Header("Overlay images (talk preview)")]
        [SerializeField] private RectTransform eyeImage;
        [SerializeField] private RectTransform mouthImage;

        [Header("Mouth")]
        [SerializeField] private float mouthClosedScaleY = 0.35f;
        [SerializeField] private float mouthOpenScaleY = 1.15f;
        [SerializeField] private float mouthGain = 12f;
        [SerializeField] private float mouthNoiseFloor = 0.01f;
        [SerializeField] private float mouthResponse = 20f;

        [Header("Blink")]
        [SerializeField] private float blinkIntervalMin = 2.5f;
        [SerializeField] private float blinkIntervalMax = 6f;
        [SerializeField] private float blinkDuration = 0.09f;
        [SerializeField] private float blinkClosedScaleY = 0.06f;

        private AudioSource voice;
        private readonly float[] samples = new float[256];
        private float mouthScaleY;
        private float nextBlinkAt;
        private float blinkEndsAt;

        public void SetVoiceSource(AudioSource source) => voice = source;

        private void OnEnable()
        {
            mouthScaleY = mouthClosedScaleY;
            nextBlinkAt = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
            blinkEndsAt = 0f;
        }

        private void LateUpdate()
        {
            UpdateMouth();
            UpdateBlink();
        }

        private void UpdateMouth()
        {
            if (mouthImage == null)
            {
                return;
            }

            float openness = 0f;
            if (mouthImage.gameObject.activeInHierarchy && voice != null && voice.isPlaying)
            {
                voice.GetOutputData(samples, 0);
                float rms = FaceOverlayMotion.Rms(samples, samples.Length);
                openness = FaceOverlayMotion.MouthOpenness(rms, mouthGain, mouthNoiseFloor);
            }

            float target = FaceOverlayMotion.MouthScaleY(openness, mouthClosedScaleY, mouthOpenScaleY);
            mouthScaleY = Mathf.Lerp(mouthScaleY, target, 1f - Mathf.Exp(-mouthResponse * Time.deltaTime));

            Vector3 s = mouthImage.localScale;
            mouthImage.localScale = new Vector3(s.x, mouthScaleY, s.z);
        }

        private void UpdateBlink()
        {
            if (eyeImage == null)
            {
                return;
            }

            float now = Time.time;
            if (now >= nextBlinkAt)
            {
                blinkEndsAt = now + blinkDuration;
                nextBlinkAt = blinkEndsAt + Random.Range(blinkIntervalMin, blinkIntervalMax);
            }

            float targetY = eyeImage.gameObject.activeInHierarchy && now < blinkEndsAt ? blinkClosedScaleY : 1f;
            Vector3 s = eyeImage.localScale;
            float k = 1f - Mathf.Exp(-35f * Time.deltaTime);
            eyeImage.localScale = new Vector3(s.x, Mathf.Lerp(s.y, targetY, k), s.z);
        }
    }
}
