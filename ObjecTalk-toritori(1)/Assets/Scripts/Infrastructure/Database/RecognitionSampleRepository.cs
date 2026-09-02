using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;

namespace ObjecTalk.Infrastructure.Database
{
    public readonly struct RecognitionDataVersion : IEquatable<RecognitionDataVersion>
    {
        public RecognitionDataVersion(int schemaVersion, string modelVersion, string preprocessingVersion)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (string.IsNullOrWhiteSpace(modelVersion))
            {
                throw new ArgumentException("A model version is required.", nameof(modelVersion));
            }

            if (string.IsNullOrWhiteSpace(preprocessingVersion))
            {
                throw new ArgumentException("A preprocessing version is required.", nameof(preprocessingVersion));
            }

            SchemaVersion = schemaVersion;
            ModelVersion = modelVersion;
            PreprocessingVersion = preprocessingVersion;
        }

        public int SchemaVersion { get; }

        public string ModelVersion { get; }

        public string PreprocessingVersion { get; }

        public bool Equals(RecognitionDataVersion other)
        {
            return SchemaVersion == other.SchemaVersion
                && string.Equals(ModelVersion, other.ModelVersion, StringComparison.Ordinal)
                && string.Equals(PreprocessingVersion, other.PreprocessingVersion, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RecognitionDataVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SchemaVersion;
                hash = (hash * 397) ^ (ModelVersion == null ? 0 : StringComparer.Ordinal.GetHashCode(ModelVersion));
                hash = (hash * 397) ^ (PreprocessingVersion == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(PreprocessingVersion));
                return hash;
            }
        }

        public static bool operator ==(RecognitionDataVersion left, RecognitionDataVersion right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RecognitionDataVersion left, RecognitionDataVersion right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class RecognitionSampleRecord
    {
        public RecognitionSampleRecord(
            int sampleIndex,
            RecognitionDataVersion version,
            float[] embedding,
            byte[] keypointBlob,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            byte[] descriptorBlob,
            int imageWidth,
            int imageHeight)
            : this(
                null,
                sampleIndex,
                version,
                embedding,
                keypointBlob,
                descriptorRows,
                descriptorColumns,
                descriptorType,
                descriptorBlob,
                imageWidth,
                imageHeight)
        {
        }

        internal RecognitionSampleRecord(
            string objectId,
            int sampleIndex,
            RecognitionDataVersion version,
            float[] embedding,
            byte[] keypointBlob,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            byte[] descriptorBlob,
            int imageWidth,
            int imageHeight)
        {
            ObjectId = objectId;
            SampleIndex = sampleIndex;
            Version = version;
            Embedding = Clone(embedding, nameof(embedding));
            KeypointBlob = Clone(keypointBlob, nameof(keypointBlob));
            DescriptorRows = descriptorRows;
            DescriptorColumns = descriptorColumns;
            DescriptorType = descriptorType;
            DescriptorBlob = Clone(descriptorBlob, nameof(descriptorBlob));
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
        }

        public string ObjectId { get; }

        public int SampleIndex { get; }

        public RecognitionDataVersion Version { get; }

        public float[] Embedding { get; }

        public byte[] KeypointBlob { get; }

        public int DescriptorRows { get; }

        public int DescriptorColumns { get; }

        public int DescriptorType { get; }

        public byte[] DescriptorBlob { get; }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        private static T[] Clone<T>(T[] source, string paramName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(paramName);
            }

            return (T[])source.Clone();
        }
    }

    public sealed class RecognitionSampleRepository : IDisposable
    {
        public const int RequiredSamplesPerObject = 3;
        public const int MaximumEmbeddingDimension = 576;
        public const int MaximumVerificationImageDimension = 320;

        private const int MaximumDescriptorRows = 4096;
        private const int MaximumDescriptorColumns = 512;
        private const int MaximumDescriptorChannels = 4;
        private const int MaximumKeypointBlobBytes = 4 * 1024 * 1024;
        private const int MaximumDescriptorBlobBytes = 8 * 1024 * 1024;

        private readonly SQLiteConnection _connection;
        private readonly bool _ownsConnection;
        private bool _disposed;

        public RecognitionSampleRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("A database path is required.", nameof(databasePath));
            }

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SQLiteConnection(databasePath);
            _ownsConnection = true;
            EnsureSchema();
        }

        public RecognitionSampleRepository(SQLiteConnection connection, bool ownsConnection = false)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _ownsConnection = ownsConnection;
            EnsureSchema();
        }

        public void SaveSamples(string objectId, IReadOnlyList<RecognitionSampleRecord> samples)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("An object id is required.", nameof(objectId));
            }

            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Count != RequiredSamplesPerObject)
            {
                throw new ArgumentException("Exactly three recognition samples are required.", nameof(samples));
            }

            var rows = new RecognitionSampleRow[RequiredSamplesPerObject];
            var indexes = new bool[RequiredSamplesPerObject];
            var batchVersion = default(RecognitionDataVersion);
            var hasBatchVersion = false;
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (sample == null)
                {
                    throw new ArgumentException("Recognition samples cannot contain null entries.", nameof(samples));
                }

                ValidateVersion(sample.Version);
                if (!hasBatchVersion)
                {
                    batchVersion = sample.Version;
                    hasBatchVersion = true;
                }
                else if (sample.Version != batchVersion)
                {
                    throw new ArgumentException(
                        "All recognition samples in a batch must use the same data version.",
                        nameof(samples));
                }

                if (sample.SampleIndex < 0
                    || sample.SampleIndex >= RequiredSamplesPerObject
                    || indexes[sample.SampleIndex])
                {
                    throw new ArgumentException("Sample indexes must contain 0, 1, and 2 exactly once.", nameof(samples));
                }

                indexes[sample.SampleIndex] = true;
                rows[i] = Serialize(objectId, sample);
            }

            _connection.RunInTransaction(() =>
            {
                _connection.Execute("DELETE FROM recognition_samples WHERE object_id = ?", objectId);
                foreach (var row in rows)
                {
                    _connection.Insert(row);
                }
            });
        }

        public IReadOnlyList<RecognitionSampleRecord> LoadValidSamples(
            string userId,
            RecognitionDataVersion expected)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("A user id is required.", nameof(userId));
            }

            ValidateVersion(expected);
            var metadataRows = _connection.Query<RecognitionSampleMetadataRow>(
                "SELECT rs.object_id, rs.sample_index, rs.embedding_dimension, "
                + "length(rs.embedding_blob) AS embedding_blob_length, "
                + "length(rs.keypoint_blob) AS keypoint_blob_length, "
                + "rs.descriptor_rows, rs.descriptor_columns, rs.descriptor_type, "
                + "length(rs.descriptor_blob) AS descriptor_blob_length, "
                + "rs.image_width, rs.image_height "
                + "FROM recognition_samples rs "
                + "INNER JOIN objects o ON o.object_id = rs.object_id "
                + "WHERE o.user_id = ? AND rs.schema_version = ? AND rs.model_version = ? "
                + "AND rs.preprocessing_version = ? ORDER BY rs.object_id, rs.sample_index",
                userId,
                expected.SchemaVersion,
                expected.ModelVersion,
                expected.PreprocessingVersion);

            var loaded = new List<RecognitionSampleRecord>();
            foreach (var group in metadataRows.GroupBy(row => row.ObjectId, StringComparer.Ordinal))
            {
                var orderedRows = group.OrderBy(row => row.SampleIndex).ToArray();
                if (orderedRows.Length != RequiredSamplesPerObject)
                {
                    continue;
                }

                var metadataIsComplete = true;
                for (var i = 0; i < orderedRows.Length; i++)
                {
                    if (orderedRows[i].SampleIndex != i || !IsValidSerializedMetadata(orderedRows[i]))
                    {
                        metadataIsComplete = false;
                        break;
                    }
                }

                if (!metadataIsComplete)
                {
                    continue;
                }

                var objectSamples = new RecognitionSampleRecord[RequiredSamplesPerObject];
                var isComplete = true;
                for (var i = 0; i < orderedRows.Length; i++)
                {
                    if (!TryLoadPayload(orderedRows[i], expected, out var payload)
                        || !TryDeserialize(payload, expected, out objectSamples[i]))
                    {
                        isComplete = false;
                        break;
                    }
                }

                if (isComplete)
                {
                    loaded.AddRange(objectSamples);
                }
            }

            return loaded;
        }

        private bool TryLoadPayload(
            RecognitionSampleMetadataRow metadata,
            RecognitionDataVersion expected,
            out RecognitionSampleRow payload)
        {
            payload = _connection.FindWithQuery<RecognitionSampleRow>(
                "SELECT * FROM recognition_samples WHERE object_id = ? AND sample_index = ? "
                + "AND schema_version = ? AND model_version = ? AND preprocessing_version = ? "
                + "AND embedding_dimension = ? AND descriptor_rows = ? AND descriptor_columns = ? "
                + "AND descriptor_type = ? AND image_width = ? AND image_height = ? "
                + "AND length(embedding_blob) = ? AND length(keypoint_blob) = ? "
                + "AND length(descriptor_blob) = ? LIMIT 1",
                metadata.ObjectId,
                metadata.SampleIndex,
                expected.SchemaVersion,
                expected.ModelVersion,
                expected.PreprocessingVersion,
                metadata.EmbeddingDimension,
                metadata.DescriptorRows,
                metadata.DescriptorColumns,
                metadata.DescriptorType,
                metadata.ImageWidth,
                metadata.ImageHeight,
                metadata.EmbeddingBlobLength,
                metadata.KeypointBlobLength,
                metadata.DescriptorBlobLength);
            return payload != null;
        }

        public void InvalidateObsolete(RecognitionDataVersion expected)
        {
            ThrowIfDisposed();
            ValidateVersion(expected);
            _connection.RunInTransaction(() =>
            {
                _connection.Execute(
                    "DELETE FROM recognition_samples "
                    + "WHERE schema_version <> ? OR model_version <> ? OR preprocessing_version <> ?",
                    expected.SchemaVersion,
                    expected.ModelVersion,
                    expected.PreprocessingVersion);
                _connection.Execute(
                    "UPDATE objects SET feature_vector_blob = NULL, keypoints_blob = NULL, descriptors_blob = NULL");
                _connection.Execute("UPDATE object_features SET feature_vector_blob = NULL");
            });
        }

        public void DeleteSamples(string objectId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("An object id is required.", nameof(objectId));
            }

            _connection.RunInTransaction(() =>
                _connection.Execute("DELETE FROM recognition_samples WHERE object_id = ?", objectId));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_ownsConnection)
            {
                _connection.Dispose();
            }
        }

        private static RecognitionSampleRow Serialize(string objectId, RecognitionSampleRecord sample)
        {
            ValidateVersion(sample.Version);
            ValidateSampleMetadata(
                sample.Embedding?.Length ?? 0,
                sample.KeypointBlob,
                sample.DescriptorRows,
                sample.DescriptorColumns,
                sample.DescriptorType,
                sample.DescriptorBlob,
                sample.ImageWidth,
                sample.ImageHeight);
            ValidateEmbedding(sample.Embedding);

            var embeddingBlob = new byte[checked(sample.Embedding.Length * sizeof(float))];
            Buffer.BlockCopy(sample.Embedding, 0, embeddingBlob, 0, embeddingBlob.Length);
            return new RecognitionSampleRow
            {
                ObjectId = objectId,
                SampleIndex = sample.SampleIndex,
                SchemaVersion = sample.Version.SchemaVersion,
                ModelVersion = sample.Version.ModelVersion,
                PreprocessingVersion = sample.Version.PreprocessingVersion,
                EmbeddingDimension = sample.Embedding.Length,
                EmbeddingBlob = embeddingBlob,
                KeypointBlob = (byte[])sample.KeypointBlob.Clone(),
                DescriptorRows = sample.DescriptorRows,
                DescriptorColumns = sample.DescriptorColumns,
                DescriptorType = sample.DescriptorType,
                DescriptorBlob = (byte[])sample.DescriptorBlob.Clone(),
                ImageWidth = sample.ImageWidth,
                ImageHeight = sample.ImageHeight
            };
        }

        private static bool TryDeserialize(
            RecognitionSampleRow row,
            RecognitionDataVersion expected,
            out RecognitionSampleRecord sample)
        {
            sample = null;
            if (row == null
                || row.SchemaVersion != expected.SchemaVersion
                || !string.Equals(row.ModelVersion, expected.ModelVersion, StringComparison.Ordinal)
                || !string.Equals(row.PreprocessingVersion, expected.PreprocessingVersion, StringComparison.Ordinal)
                || !IsValidSampleMetadata(
                    row.EmbeddingDimension,
                    row.EmbeddingBlob,
                    row.KeypointBlob,
                    row.DescriptorRows,
                    row.DescriptorColumns,
                    row.DescriptorType,
                    row.DescriptorBlob,
                    row.ImageWidth,
                    row.ImageHeight))
            {
                return false;
            }

            var embedding = new float[row.EmbeddingDimension];
            Buffer.BlockCopy(row.EmbeddingBlob, 0, embedding, 0, row.EmbeddingBlob.Length);
            if (!IsValidEmbedding(embedding))
            {
                return false;
            }

            sample = new RecognitionSampleRecord(
                row.ObjectId,
                row.SampleIndex,
                expected,
                embedding,
                row.KeypointBlob,
                row.DescriptorRows,
                row.DescriptorColumns,
                row.DescriptorType,
                row.DescriptorBlob,
                row.ImageWidth,
                row.ImageHeight);
            return true;
        }

        private static void ValidateSampleMetadata(
            int embeddingDimension,
            byte[] keypointBlob,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            byte[] descriptorBlob,
            int imageWidth,
            int imageHeight)
        {
            if (embeddingDimension <= 0
                || embeddingDimension > MaximumEmbeddingDimension
                || !IsValidNonEmbeddingMetadata(
                    keypointBlob,
                    descriptorRows,
                    descriptorColumns,
                    descriptorType,
                    descriptorBlob,
                    imageWidth,
                    imageHeight))
            {
                throw new ArgumentException("Recognition sample metadata is invalid.");
            }
        }

        private static bool IsValidSampleMetadata(
            int embeddingDimension,
            byte[] embeddingBlob,
            byte[] keypointBlob,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            byte[] descriptorBlob,
            int imageWidth,
            int imageHeight)
        {
            if (embeddingBlob == null || keypointBlob == null || descriptorBlob == null)
            {
                return false;
            }

            return IsValidSerializedMetadata(
                embeddingDimension,
                embeddingBlob.Length,
                keypointBlob.Length,
                descriptorRows,
                descriptorColumns,
                descriptorType,
                descriptorBlob.Length,
                imageWidth,
                imageHeight);
        }

        private static bool IsValidSerializedMetadata(RecognitionSampleMetadataRow row)
        {
            return row != null && IsValidSerializedMetadata(
                row.EmbeddingDimension,
                row.EmbeddingBlobLength,
                row.KeypointBlobLength,
                row.DescriptorRows,
                row.DescriptorColumns,
                row.DescriptorType,
                row.DescriptorBlobLength,
                row.ImageWidth,
                row.ImageHeight);
        }

        private static bool IsValidSerializedMetadata(
            int embeddingDimension,
            long embeddingBlobLength,
            long keypointBlobLength,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            long descriptorBlobLength,
            int imageWidth,
            int imageHeight)
        {
            if (embeddingDimension <= 0
                || embeddingDimension > MaximumEmbeddingDimension
                || embeddingBlobLength != (long)embeddingDimension * sizeof(float))
            {
                return false;
            }

            return IsValidNonEmbeddingMetadata(
                keypointBlobLength,
                descriptorRows,
                descriptorColumns,
                descriptorType,
                descriptorBlobLength,
                imageWidth,
                imageHeight);
        }

        private static bool IsValidNonEmbeddingMetadata(
            byte[] keypointBlob,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            byte[] descriptorBlob,
            int imageWidth,
            int imageHeight)
        {
            return keypointBlob != null
                && descriptorBlob != null
                && IsValidNonEmbeddingMetadata(
                    keypointBlob.Length,
                    descriptorRows,
                    descriptorColumns,
                    descriptorType,
                    descriptorBlob.Length,
                    imageWidth,
                    imageHeight);
        }

        private static bool IsValidNonEmbeddingMetadata(
            long keypointBlobLength,
            int descriptorRows,
            int descriptorColumns,
            int descriptorType,
            long descriptorBlobLength,
            int imageWidth,
            int imageHeight)
        {
            if (keypointBlobLength <= 0
                || keypointBlobLength > MaximumKeypointBlobBytes
                || descriptorRows <= 0
                || descriptorRows > MaximumDescriptorRows
                || descriptorColumns <= 0
                || descriptorColumns > MaximumDescriptorColumns
                || descriptorBlobLength <= 0
                || descriptorBlobLength > MaximumDescriptorBlobBytes
                || imageWidth <= 0
                || imageWidth > MaximumVerificationImageDimension
                || imageHeight <= 0
                || imageHeight > MaximumVerificationImageDimension
                || !TryGetOpenCvElementSize(descriptorType, out var elementSize))
            {
                return false;
            }

            var expectedDescriptorBytes = (long)descriptorRows * descriptorColumns * elementSize;
            return expectedDescriptorBytes <= MaximumDescriptorBlobBytes
                && descriptorBlobLength == expectedDescriptorBytes;
        }

        private static bool TryGetOpenCvElementSize(int descriptorType, out int elementSize)
        {
            elementSize = 0;
            if (descriptorType < 0)
            {
                return false;
            }

            var depth = descriptorType & 7;
            var channels = (descriptorType >> 3) + 1;
            if (depth > 6 || channels <= 0 || channels > MaximumDescriptorChannels)
            {
                return false;
            }

            int bytesPerChannel;
            switch (depth)
            {
                case 0:
                case 1:
                    bytesPerChannel = 1;
                    break;
                case 2:
                case 3:
                    bytesPerChannel = 2;
                    break;
                case 4:
                case 5:
                    bytesPerChannel = 4;
                    break;
                case 6:
                    bytesPerChannel = 8;
                    break;
                default:
                    return false;
            }

            elementSize = bytesPerChannel * channels;
            return true;
        }

        private static void ValidateVersion(RecognitionDataVersion version)
        {
            if (version.SchemaVersion <= 0
                || string.IsNullOrWhiteSpace(version.ModelVersion)
                || string.IsNullOrWhiteSpace(version.PreprocessingVersion))
            {
                throw new ArgumentException("Recognition data version is invalid.", nameof(version));
            }
        }

        private static void ValidateEmbedding(float[] embedding)
        {
            if (!IsValidEmbedding(embedding))
            {
                throw new ArgumentException("Recognition embeddings must contain only finite values.", nameof(embedding));
            }
        }

        private static bool IsValidEmbedding(float[] embedding)
        {
            if (embedding == null || embedding.Length == 0)
            {
                return false;
            }

            var hasMagnitude = false;
            foreach (var value in embedding)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }

                hasMagnitude |= value != 0f;
            }

            return hasMagnitude;
        }

        private void EnsureSchema()
        {
            _connection.Execute(
                "CREATE TABLE IF NOT EXISTS recognition_samples ("
                + "object_id TEXT NOT NULL, "
                + "sample_index INTEGER NOT NULL, "
                + "schema_version INTEGER NOT NULL, "
                + "model_version TEXT NOT NULL, "
                + "preprocessing_version TEXT NOT NULL, "
                + "embedding_dimension INTEGER NOT NULL, "
                + "embedding_blob BLOB NOT NULL, "
                + "keypoint_blob BLOB NOT NULL, "
                + "descriptor_rows INTEGER NOT NULL, "
                + "descriptor_columns INTEGER NOT NULL, "
                + "descriptor_type INTEGER NOT NULL, "
                + "descriptor_blob BLOB NOT NULL, "
                + "image_width INTEGER NOT NULL, "
                + "image_height INTEGER NOT NULL, "
                + "PRIMARY KEY (object_id, sample_index))");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RecognitionSampleRepository));
            }
        }

        [Table("recognition_samples")]
        private sealed class RecognitionSampleRow
        {
            [Column("object_id")]
            public string ObjectId { get; set; }

            [Column("sample_index")]
            public int SampleIndex { get; set; }

            [Column("schema_version")]
            public int SchemaVersion { get; set; }

            [Column("model_version")]
            public string ModelVersion { get; set; }

            [Column("preprocessing_version")]
            public string PreprocessingVersion { get; set; }

            [Column("embedding_dimension")]
            public int EmbeddingDimension { get; set; }

            [Column("embedding_blob")]
            public byte[] EmbeddingBlob { get; set; }

            [Column("keypoint_blob")]
            public byte[] KeypointBlob { get; set; }

            [Column("descriptor_rows")]
            public int DescriptorRows { get; set; }

            [Column("descriptor_columns")]
            public int DescriptorColumns { get; set; }

            [Column("descriptor_type")]
            public int DescriptorType { get; set; }

            [Column("descriptor_blob")]
            public byte[] DescriptorBlob { get; set; }

            [Column("image_width")]
            public int ImageWidth { get; set; }

            [Column("image_height")]
            public int ImageHeight { get; set; }
        }

        private sealed class RecognitionSampleMetadataRow
        {
            [Column("object_id")]
            public string ObjectId { get; set; }

            [Column("sample_index")]
            public int SampleIndex { get; set; }

            [Column("embedding_dimension")]
            public int EmbeddingDimension { get; set; }

            [Column("embedding_blob_length")]
            public long EmbeddingBlobLength { get; set; }

            [Column("keypoint_blob_length")]
            public long KeypointBlobLength { get; set; }

            [Column("descriptor_rows")]
            public int DescriptorRows { get; set; }

            [Column("descriptor_columns")]
            public int DescriptorColumns { get; set; }

            [Column("descriptor_type")]
            public int DescriptorType { get; set; }

            [Column("descriptor_blob_length")]
            public long DescriptorBlobLength { get; set; }

            [Column("image_width")]
            public int ImageWidth { get; set; }

            [Column("image_height")]
            public int ImageHeight { get; set; }
        }
    }
}
