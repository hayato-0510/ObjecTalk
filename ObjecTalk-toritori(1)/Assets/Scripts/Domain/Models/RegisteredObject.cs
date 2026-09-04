using UnityEngine;
using OpenCvSharp;

namespace ObjecTalk.Domain.Models
{
    [System.Serializable]
    public class RegisteredObject
    {
        public string Name;
        public float[] FeatureVector;
        public KeyPoint[] Keypoints;
        public Mat Descriptors;
        public Size ImageSize;
        public Vector2 RelativeEyePosition;
        public Vector2 RelativeMouthPosition;
        public int EyeStyleId;
        public int MouthStyleId;
    }
}
