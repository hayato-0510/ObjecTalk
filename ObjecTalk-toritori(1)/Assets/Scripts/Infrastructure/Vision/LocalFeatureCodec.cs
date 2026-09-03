using System;
using System.IO;
using System.Runtime.InteropServices;
using ObjecTalk.Domain.Recognition;
using OpenCvSharp;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// Converts AKAZE keypoints and descriptors to and from the opaque byte payloads the database
    /// stores. It lives beside the verifier because it is the only other place in the project that
    /// is allowed to know OpenCV's memory layout.
    ///
    /// Every read validates the declared geometry against the actual payload length before any
    /// buffer is allocated or any matrix is reconstructed, so a corrupt row is rejected rather
    /// than being turned into an out-of-bounds copy.
    /// </summary>
    public static class LocalFeatureCodec
    {
        /// <summary>x, y, size, angle, response, octave, class id.</summary>
        private const int BytesPerKeypoint = (5 * sizeof(float)) + (2 * sizeof(int));

        private const int MaximumKeypoints = 1 << 16;
        private const int MaximumDescriptorBytes = 4 * 1024 * 1024;

        public static byte[] SerializeKeypoints(KeyPoint[] keypoints)
        {
            if (keypoints == null)
            {
                throw new ArgumentNullException(nameof(keypoints));
            }

            var payload = new byte[checked(keypoints.Length * BytesPerKeypoint)];
            using (var stream = new MemoryStream(payload, true))
            using (var writer = new BinaryWriter(stream))
            {
                for (var i = 0; i < keypoints.Length; i++)
                {
                    var keypoint = keypoints[i];
                    writer.Write(keypoint.Pt.X);
                    writer.Write(keypoint.Pt.Y);
                    writer.Write(keypoint.Size);
                    writer.Write(keypoint.Angle);
                    writer.Write(keypoint.Response);
                    writer.Write(keypoint.Octave);
                    writer.Write(keypoint.ClassId);
                }
            }

            return payload;
        }

        /// <summary>Returns null when the payload is not a whole number of keypoints.</summary>
        public static KeyPoint[] DeserializeKeypoints(byte[] payload)
        {
            if (payload == null
                || payload.Length == 0
                || payload.Length % BytesPerKeypoint != 0)
            {
                return null;
            }

            var count = payload.Length / BytesPerKeypoint;
            if (count > MaximumKeypoints)
            {
                return null;
            }

            var keypoints = new KeyPoint[count];
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream))
            {
                for (var i = 0; i < count; i++)
                {
                    var x = reader.ReadSingle();
                    var y = reader.ReadSingle();
                    var size = reader.ReadSingle();
                    var angle = reader.ReadSingle();
                    var response = reader.ReadSingle();
                    var octave = reader.ReadInt32();
                    var classId = reader.ReadInt32();
                    keypoints[i] = new KeyPoint(new Point2f(x, y), size, angle, response, octave, classId);
                }
            }

            return keypoints;
        }

        public static byte[] SerializeDescriptors(Mat descriptors)
        {
            if (descriptors == null || descriptors.IsDisposed || descriptors.Empty())
            {
                throw new ArgumentException("Descriptors must be a live, non-empty matrix.", nameof(descriptors));
            }

            // Marshal.Copy needs one unbroken run of bytes; a padded matrix is compacted first.
            var source = descriptors.IsContinuous() ? descriptors : descriptors.Clone();
            try
            {
                var length = checked((int)(source.Total() * source.ElemSize()));
                var payload = new byte[length];
                Marshal.Copy(source.Data, payload, 0, length);
                return payload;
            }
            finally
            {
                if (!ReferenceEquals(source, descriptors))
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// Rebuilds the descriptor matrix, or returns null when the declared rows, columns, and
        /// type do not account for exactly the bytes present.
        /// </summary>
        public static Mat DeserializeDescriptors(LocalFeatureBlob blob)
        {
            if (!blob.HasPayload
                || blob.Descriptors.Length == 0
                || blob.Descriptors.Length > MaximumDescriptorBytes)
            {
                return null;
            }

            // MatType has no parse method in this OpenCvSharp build; the implicit conversion is
            // the documented way in, and an unusable value shows up as a failed allocation below.
            MatType type = blob.DescriptorType;

            Mat descriptors = null;
            try
            {
                descriptors = new Mat(blob.DescriptorRows, blob.DescriptorColumns, type);
                var expected = checked((int)(descriptors.Total() * descriptors.ElemSize()));
                if (expected != blob.Descriptors.Length || !descriptors.IsContinuous())
                {
                    descriptors.Dispose();
                    return null;
                }

                Marshal.Copy(blob.Descriptors, 0, descriptors.Data, expected);
                return descriptors;
            }
            catch (Exception)
            {
                descriptors?.Dispose();
                return null;
            }
        }
    }
}
