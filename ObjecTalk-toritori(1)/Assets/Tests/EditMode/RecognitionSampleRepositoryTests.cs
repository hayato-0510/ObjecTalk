using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using ObjecTalk.Infrastructure.Database;
using SQLite;

namespace ObjecTalk.Tests.EditMode
{
    [TestFixture]
    public sealed class RecognitionSampleRepositoryTests
    {
        private static readonly RecognitionDataVersion CurrentVersion =
            new RecognitionDataVersion(1, "model-v2", "rgb-224-v1");

        private string _databasePath;

        [SetUp]
        public void SetUp()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"objectalk-recognition-{Guid.NewGuid():N}.sqlite");
        }

        [TearDown]
        public void TearDown()
        {
            DeleteIfPresent(_databasePath);
            DeleteIfPresent(_databasePath + "-shm");
            DeleteIfPresent(_databasePath + "-wal");
        }

        [Test]
        public void DatabaseManager_PathConstructor_UsesRequestedDatabase()
        {
            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(CreateUser());
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<User>("user-1").Name, Is.EqualTo("owner"));
                Assert.That(
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'recognition_samples'"),
                    Is.EqualTo(1));
            }
        }

        [Test]
        public void SaveSamples_RejectsAnythingOtherThanThreeSamples()
        {
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                Assert.Throws<ArgumentException>(() =>
                    repository.SaveSamples("object-1", CreateSamples(CurrentVersion).Take(2).ToArray()));
            }
        }

        [Test]
        public void SaveSamples_InvalidReplacement_DoesNotChangeExistingRows()
        {
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));

                var invalid = CreateSamples(CurrentVersion).ToArray();
                invalid[2] = CreateSample(1, CurrentVersion, 99f);
                Assert.Throws<ArgumentException>(() => repository.SaveSamples("object-1", invalid));

                var loaded = repository.LoadValidSamples("user-1", CurrentVersion);
                Assert.That(loaded, Has.Count.EqualTo(3));
                Assert.That(loaded[0].Embedding[0], Is.EqualTo(1f));
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?", "object-1"),
                    Is.EqualTo(3));
                Assert.That(
                    connection.ExecuteScalar<double>(
                        "SELECT sample_index FROM recognition_samples WHERE object_id = ? ORDER BY sample_index LIMIT 1",
                        "object-1"),
                    Is.EqualTo(0));
            }
        }

        [Test]
        public void SaveSamples_MixedVersionReplacement_DoesNotChangeExistingRows()
        {
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));

                var mismatchedVersions = new[]
                {
                    new RecognitionDataVersion(2, "model-v2", "rgb-224-v1"),
                    new RecognitionDataVersion(1, "other-model", "rgb-224-v1"),
                    new RecognitionDataVersion(1, "model-v2", "other-preprocessing")
                };

                foreach (var mismatchedVersion in mismatchedVersions)
                {
                    var mixedVersions = CreateSamples(CurrentVersion).ToArray();
                    mixedVersions[2] = CreateSample(2, mismatchedVersion, 99f);

                    Assert.Throws<ArgumentException>(() =>
                        repository.SaveSamples("object-1", mixedVersions));

                    var loaded = repository.LoadValidSamples("user-1", CurrentVersion);
                    Assert.That(loaded, Has.Count.EqualTo(3));
                    Assert.That(loaded.Select(sample => sample.Embedding[0]), Is.EqualTo(new[] { 1f, 2f, 3f }));
                }
            }
        }

        [Test]
        public void LoadValidSamples_ExactlyThreeSamples_RoundTripsAllMetadata()
        {
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));

                var loaded = repository.LoadValidSamples("user-1", CurrentVersion);

                Assert.That(loaded, Has.Count.EqualTo(3));
                Assert.That(loaded.Select(sample => sample.SampleIndex), Is.EqualTo(new[] { 0, 1, 2 }));
                Assert.That(loaded.All(sample => sample.ObjectId == "object-1"), Is.True);
                Assert.That(loaded[1].Version, Is.EqualTo(CurrentVersion));
                Assert.That(loaded[1].Embedding, Is.EqualTo(new[] { 2f, 3f, 4f, 5f }));
                Assert.That(loaded[1].KeypointBlob, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(loaded[1].DescriptorRows, Is.EqualTo(2));
                Assert.That(loaded[1].DescriptorColumns, Is.EqualTo(3));
                Assert.That(loaded[1].DescriptorType, Is.EqualTo(0));
                Assert.That(loaded[1].DescriptorBlob, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
                Assert.That(loaded[1].ImageWidth, Is.EqualTo(320));
                Assert.That(loaded[1].ImageHeight, Is.EqualTo(240));
            }
        }

        [TestCase("model_version", "other-model", TestName = "LoadValidSamples_RejectsWrongModelVersion")]
        [TestCase("embedding_blob", new byte[] { 0, 1, 2 }, TestName = "LoadValidSamples_RejectsInvalidEmbeddingByteLength")]
        [TestCase("embedding_dimension", RecognitionSampleRepository.MaximumEmbeddingDimension + 1,
            TestName = "LoadValidSamples_RejectsExcessiveEmbeddingDimension")]
        [TestCase("descriptor_type", 7, TestName = "LoadValidSamples_RejectsInvalidOpenCvMetadata")]
        [TestCase("descriptor_blob", new byte[] { 1, 2, 3 }, TestName = "LoadValidSamples_RejectsCorruptDescriptorLength")]
        [TestCase("image_width", 321, TestName = "LoadValidSamples_RejectsOversizedVerificationImage")]
        public void LoadValidSamples_CorruptRowRejectsIncompleteObject(string column, object invalidValue)
        {
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                connection.Execute(
                    $"UPDATE recognition_samples SET {column} = ? WHERE object_id = ? AND sample_index = 1",
                    invalidValue,
                    "object-1");
            }

            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                Assert.That(repository.LoadValidSamples("user-1", CurrentVersion), Is.Empty);
            }
        }

        [Test]
        public void LoadValidSamples_CorruptObjectDoesNotPreventOtherObjectsLoading()
        {
            CreateOwningObject("corrupt-object");
            CreateOwningObject("valid-object");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("corrupt-object", CreateSamples(CurrentVersion));
                repository.SaveSamples("valid-object", CreateSamples(CurrentVersion));
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                connection.Execute(
                    "UPDATE recognition_samples SET descriptor_rows = -1 WHERE object_id = ? AND sample_index = 0",
                    "corrupt-object");
            }

            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                var loaded = repository.LoadValidSamples("user-1", CurrentVersion);
                Assert.That(loaded, Has.Count.EqualTo(3));
                Assert.That(loaded.All(sample => sample.ObjectId == "valid-object"), Is.True);
            }
        }

        [Test]
        public void LoadValidSamples_OversizedCorruptBlob_IsRejectedWithoutMaterializingPayload()
        {
            const int maximumValidBlobBytes = 8 * 1024 * 1024;
            const int oversizedBlobBytes = 16 * 1024 * 1024;
            const long maximumAllowedAllocationBytes = 2 * 1024 * 1024;
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                connection.Execute(
                    "UPDATE recognition_samples SET descriptor_rows = 4096, descriptor_columns = 512, "
                    + "descriptor_type = 5, descriptor_blob = zeroblob(?) "
                    + "WHERE object_id = ? AND sample_index = 0",
                    maximumValidBlobBytes,
                    "object-1");
                connection.Execute(
                    "UPDATE recognition_samples SET descriptor_rows = 1, descriptor_columns = 1, "
                    + "descriptor_type = 0, descriptor_blob = zeroblob(?) "
                    + "WHERE object_id = ? AND sample_index = 1",
                    oversizedBlobBytes,
                    "object-1");
            }

            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var loaded = repository.LoadValidSamples("user-1", CurrentVersion);
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(loaded, Is.Empty);
                Assert.That(
                    allocatedBytes,
                    Is.LessThan(maximumAllowedAllocationBytes),
                    "Invalid BLOB payloads must be rejected from scalar length metadata before sqlite-net materializes them.");
            }
        }

        [Test]
        public void LoadValidSamples_TextBlobWithEarlyNull_IsRejectedWithoutMaterializingPayload()
        {
            const int corruptTextBytes = 16 * 1024 * 1024;
            const long maximumAllowedAllocationBytes = 2 * 1024 * 1024;
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
            }

            var corruptTextPayload = new byte[corruptTextBytes];
            for (var i = 0; i < corruptTextPayload.Length; i++)
            {
                corruptTextPayload[i] = (byte)'x';
            }

            corruptTextPayload[0] = (byte)'A';
            corruptTextPayload[1] = (byte)'B';
            corruptTextPayload[2] = (byte)'C';
            corruptTextPayload[3] = (byte)'D';
            corruptTextPayload[4] = (byte)'E';
            corruptTextPayload[5] = (byte)'F';
            corruptTextPayload[6] = 0;
            using (var connection = new SQLiteConnection(_databasePath))
            {
                connection.Execute(
                    "UPDATE recognition_samples SET descriptor_blob = CAST(? AS TEXT) "
                    + "WHERE object_id = ? AND sample_index = 1",
                    corruptTextPayload,
                    "object-1");
                Assert.That(
                    connection.ExecuteScalar<string>(
                        "SELECT typeof(descriptor_blob) FROM recognition_samples "
                        + "WHERE object_id = ? AND sample_index = 1",
                        "object-1"),
                    Is.EqualTo("text"));
                Assert.That(
                    connection.ExecuteScalar<int>(
                        "SELECT length(descriptor_blob) FROM recognition_samples "
                        + "WHERE object_id = ? AND sample_index = 1",
                        "object-1"),
                    Is.EqualTo(6),
                    "SQLite TEXT length stops at the embedded NUL and can mimic valid descriptor metadata.");
                Assert.That(
                    connection.ExecuteScalar<int>(
                        "SELECT length(CAST(descriptor_blob AS BLOB)) FROM recognition_samples "
                        + "WHERE object_id = ? AND sample_index = 1",
                        "object-1"),
                    Is.EqualTo(corruptTextBytes));
            }

            corruptTextPayload = null;
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var loaded = repository.LoadValidSamples("user-1", CurrentVersion);
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(loaded, Is.Empty);
                Assert.That(
                    allocatedBytes,
                    Is.LessThan(maximumAllowedAllocationBytes),
                    "TEXT values in BLOB columns must be rejected by storage class before payload materialization.");
            }
        }

        [Test]
        public void LoadValidSamples_ConcurrentAtomicSave_ReturnsOneCompleteGeneration()
        {
            CreateOwningObject("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
            }

            using (var loadConnection = new SQLiteConnection(_databasePath))
            using (var loadRepository = new RecognitionSampleRepository(loadConnection))
            using (var writerRepository = new RecognitionSampleRepository(_databasePath))
            using (var secondPayloadRead = new ManualResetEventSlim(false))
            using (var writerFinished = new ManualResetEventSlim(false))
            {
                loadConnection.EnableWriteAheadLogging();
                var payloadReadCount = 0;
                loadConnection.Trace = true;
                loadConnection.Tracer = sql =>
                {
                    if (!sql.Contains("SELECT * FROM recognition_samples"))
                    {
                        return;
                    }

                    if (Interlocked.Increment(ref payloadReadCount) == 2)
                    {
                        secondPayloadRead.Set();
                        if (!writerFinished.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Concurrent sample replacement did not finish at the payload barrier.");
                        }
                    }
                };

                Exception writerFailure = null;
                var writer = new Thread(() =>
                {
                    try
                    {
                        if (!secondPayloadRead.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Load did not reach the second payload query.");
                        }

                        writerRepository.SaveSamples("object-1", new[]
                        {
                            CreateSample(0, CurrentVersion, 101f),
                            CreateSample(1, CurrentVersion, 102f),
                            CreateSample(2, CurrentVersion, 103f)
                        });
                    }
                    catch (Exception exception)
                    {
                        writerFailure = exception;
                    }
                    finally
                    {
                        writerFinished.Set();
                    }
                });

                writer.Start();
                var loaded = loadRepository.LoadValidSamples("user-1", CurrentVersion);
                Assert.That(writer.Join(TimeSpan.FromSeconds(5)), Is.True, "Writer thread did not terminate.");
                Assert.That(writerFailure, Is.Null);

                var loadedGeneration = loaded.Select(sample => sample.Embedding[0]).ToArray();
                Assert.That(
                    loadedGeneration.SequenceEqual(new[] { 1f, 2f, 3f })
                    || loadedGeneration.SequenceEqual(new[] { 101f, 102f, 103f }),
                    Is.True,
                    $"Loaded a mixed generation: [{string.Join(", ", loadedGeneration)}]");
            }
        }

        [Test]
        public void InvalidateObsolete_RemovesOnlyRecognitionData_AndIsIdempotent()
        {
            var objectBefore = CreateOwningObject("object-1");
            CreateOwningObject("current-object");
            var relationBefore = CreateTagRelation("object-1");
            var obsoleteVersion = new RecognitionDataVersion(1, "model-v1", "rgb-224-v1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(obsoleteVersion));
                repository.SaveSamples("current-object", CreateSamples(CurrentVersion));
                repository.InvalidateObsolete(CurrentVersion);
                repository.InvalidateObsolete(CurrentVersion);
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                var userAfter = connection.Find<User>("user-1");
                var objectAfter = connection.Find<ObjectItem>("object-1");
                var relationAfter = connection.Table<ObjectFeature>().Single();

                Assert.That(userAfter.Name, Is.EqualTo("owner"));
                Assert.That(userAfter.DateOfBirth, Is.EqualTo("2000-01-02"));
                Assert.That(userAfter.GmailAddress, Is.EqualTo("owner@example.com"));
                Assert.That(userAfter.IsCalendarSyncEnabled, Is.True);
                Assert.That(userAfter.IsWeatherSyncEnabled, Is.False);
                Assert.That(objectAfter.Name, Is.EqualTo(objectBefore.Name));
                Assert.That(objectAfter.ImageData, Is.EqualTo(objectBefore.ImageData));
                Assert.That(objectAfter.Category, Is.EqualTo(objectBefore.Category));
                Assert.That(objectAfter.AiDescription, Is.EqualTo(objectBefore.AiDescription));
                Assert.That(objectAfter.EyePositionX, Is.EqualTo(objectBefore.EyePositionX));
                Assert.That(objectAfter.EyePositionY, Is.EqualTo(objectBefore.EyePositionY));
                Assert.That(objectAfter.MouthPositionX, Is.EqualTo(objectBefore.MouthPositionX));
                Assert.That(objectAfter.MouthPositionY, Is.EqualTo(objectBefore.MouthPositionY));
                Assert.That(objectAfter.ImageWidth, Is.EqualTo(objectBefore.ImageWidth));
                Assert.That(objectAfter.ImageHeight, Is.EqualTo(objectBefore.ImageHeight));
                Assert.That(objectAfter.EyeStyleId, Is.EqualTo(objectBefore.EyeStyleId));
                Assert.That(objectAfter.MouthStyleId, Is.EqualTo(objectBefore.MouthStyleId));
                Assert.That(objectAfter.PersonalityPrompt, Is.EqualTo(objectBefore.PersonalityPrompt));
                Assert.That(objectAfter.FeatureVectorBlob, Is.Null);
                Assert.That(objectAfter.KeyPointsBlob, Is.Null);
                Assert.That(objectAfter.DescriptorsBlob, Is.Null);
                Assert.That(relationAfter.object_id, Is.EqualTo(relationBefore.object_id));
                Assert.That(relationAfter.feature_id, Is.EqualTo(relationBefore.feature_id));
                Assert.That(relationAfter.ImageURL, Is.EqualTo(relationBefore.ImageURL));
                Assert.That(relationAfter.FeatureVectorBlob, Is.Null);
                Assert.That(connection.Table<Feature>().Single().name, Is.EqualTo("important"));
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples"), Is.EqualTo(3));
                Assert.That(
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?",
                        "current-object"),
                    Is.EqualTo(3));
            }
        }

        [Test]
        public void DeleteSamples_IsIdempotent()
        {
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
                repository.DeleteSamples("object-1");
                repository.DeleteSamples("object-1");
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples"), Is.Zero);
            }
        }

        [Test]
        public void DatabaseManager_DeleteObject_DeletesSampleRowsAndRelationsButPreservesUser()
        {
            CreateOwningObject("object-1");
            CreateTagRelation("object-1");
            using (var repository = new RecognitionSampleRepository(_databasePath))
            {
                repository.SaveSamples("object-1", CreateSamples(CurrentVersion));
            }

            using (var database = new DatabaseManager(_databasePath))
            {
                database.DeleteObject("object-1");
                database.DeleteObject("object-1");
            }

            using (var connection = new SQLiteConnection(_databasePath))
            {
                Assert.That(connection.Find<User>("user-1"), Is.Not.Null);
                Assert.That(connection.Find<ObjectItem>("object-1"), Is.Null);
                Assert.That(connection.Table<ObjectFeature>().Count(), Is.Zero);
                Assert.That(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recognition_samples"), Is.Zero);
            }
        }

        private ObjectItem CreateOwningObject(string objectId)
        {
            var user = CreateUser();
            var item = new ObjectItem
            {
                object_id = objectId,
                user_id = user.user_id,
                Name = "mug",
                ImageData = new byte[] { 9, 8, 7, 6 },
                Category = "kitchen",
                AiDescription = "blue ceramic mug",
                FeatureVectorBlob = new byte[] { 10, 11 },
                KeyPointsBlob = new byte[] { 12, 13 },
                DescriptorsBlob = new byte[] { 14, 15 },
                EyePositionX = 0.125f,
                EyePositionY = 0.25f,
                MouthPositionX = 0.5f,
                MouthPositionY = 0.75f,
                ImageWidth = 1024,
                ImageHeight = 768,
                EyeStyleId = 3,
                MouthStyleId = 4,
                PersonalityPrompt = "Cheerful and concise"
            };

            using (var database = new DatabaseManager(_databasePath))
            {
                database.SaveUser(user);
                database.SaveObjectWithFeatures(item, new List<string>());
            }

            return item;
        }

        private ObjectFeature CreateTagRelation(string objectId)
        {
            using (var connection = new SQLiteConnection(_databasePath))
            {
                var feature = new Feature { name = "important" };
                connection.Insert(feature);
                var relation = new ObjectFeature
                {
                    object_id = objectId,
                    feature_id = feature.feature_id,
                    FeatureVectorBlob = new byte[] { 21, 22 },
                    ImageURL = "https://example.invalid/object.png"
                };
                connection.Insert(relation);
                return relation;
            }
        }

        private static User CreateUser()
        {
            return new User
            {
                user_id = "user-1",
                Name = "owner",
                DateOfBirth = "2000-01-02",
                GmailAddress = "owner@example.com",
                IsCalendarSyncEnabled = true,
                IsWeatherSyncEnabled = false
            };
        }

        private static IReadOnlyList<RecognitionSampleRecord> CreateSamples(RecognitionDataVersion version)
        {
            return new[]
            {
                CreateSample(0, version, 1f),
                CreateSample(1, version, 2f),
                CreateSample(2, version, 3f)
            };
        }

        private static RecognitionSampleRecord CreateSample(
            int sampleIndex,
            RecognitionDataVersion version,
            float firstEmbeddingValue)
        {
            return new RecognitionSampleRecord(
                sampleIndex,
                version,
                new[] { firstEmbeddingValue, 3f, 4f, 5f },
                new byte[] { 1, 2, 3, 4 },
                2,
                3,
                0,
                new byte[] { 1, 2, 3, 4, 5, 6 },
                320,
                240);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
