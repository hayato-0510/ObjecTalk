using System.Collections.Generic;

namespace ObjecTalk.Infrastructure.Database
{
    /// <summary>
    /// Persistence surface the recognition orchestrator uses. It exists so the orchestrator can be
    /// tested, and reasoned about, without SQLite: the concrete repository owns the connection,
    /// the schema, and the transactions behind it.
    /// </summary>
    public interface IRecognitionSampleStore
    {
        /// <summary>
        /// Every sample belonging to <paramref name="userId"/> that still matches
        /// <paramref name="expected"/> and survives deserialization. Corrupt rows are skipped
        /// rather than failing the load.
        /// </summary>
        IReadOnlyList<RecognitionSampleRecord> LoadValidSamples(string userId, RecognitionDataVersion expected);

        /// <summary>Replaces one object's samples atomically. Exactly three are required.</summary>
        void SaveSamples(string objectId, IReadOnlyList<RecognitionSampleRecord> samples);

        void DeleteSamples(string objectId);

        /// <summary>Removes samples written by a different model or preprocessing version.</summary>
        void InvalidateObsolete(RecognitionDataVersion expected);
    }
}
