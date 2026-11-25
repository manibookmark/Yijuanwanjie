using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns memory-only mutation admission and exact, non-evicting receipts.
    /// Durable services use their existing disk archive instead. Payloads
    /// share immutable compressed UTF-16 chunks, but replay never reconstructs a
    /// response from game state or another receipt. The chunk store alone owns
    /// retained payloads and byte accounting; receipts own requests and indices.
    /// Both retained bytes and receipt count are bounded. Ordinary requests leave
    /// worst-case capacity for every live session to close; only the session
    /// owner may identify a revision-valid close as releasing a live session.
    /// </summary>
    internal sealed class AgentGameReceiptJournal : IDisposable
    {
        internal const int MaximumResponseCharacters = 1024 * 1024;
        private const int DefaultStoredByteLimit = 128 * 1024 * 1024;
        private const int ResponseChunkCharacters = 2048;
        private const int MaximumResponseChunks =
            (MaximumResponseCharacters + ResponseChunkCharacters - 1) / ResponseChunkCharacters;
        private const int ChunkMetadataBytes = 128, ReceiptMetadataBytes = 64;
        private const int MaximumReceiptBytes =
            2 * (MaximumResponseCharacters + AgentGameProtocol.MaximumRequestCharacters) +
            MaximumResponseChunks * (sizeof(int) + ChunkMetadataBytes) + ReceiptMetadataBytes;

        internal enum Outcome { Response, RequestConflict, RequestTooLarge, BudgetExceeded }
        internal readonly struct Result
        {
            internal readonly Outcome Status;
            internal readonly string Response;
            internal Result(Outcome status, string response = null)
            { Status = status; Response = response; }
        }

        private readonly Dictionary<string, Receipt> _receipts =
            new Dictionary<string, Receipt>(StringComparer.Ordinal);
        private readonly ResponseChunkStore _responses = new ResponseChunkStore();
        private readonly int _maximumEntries, _ordinaryEntryLimit;
        private readonly long _maximumStoredBytes, _ordinaryByteLimit;
        private long _receiptStoredBytes;
        private bool _disposed;
        internal long StoredBytes => _receiptStoredBytes + _responses.StoredBytes;
        internal long StoredByteLimit => _maximumStoredBytes;

        // Smaller capacities are injectable for deterministic saturation tests;
        // production capacity is derived from the advertised session/action limits.
        internal AgentGameReceiptJournal(int maximumLiveSessions, int maximumActionsPerSession,
            int storedByteLimit = DefaultStoredByteLimit)
        {
            if (maximumLiveSessions < 1 || maximumActionsPerSession < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumLiveSessions));
            long closeReserve = (long)maximumLiveSessions * MaximumReceiptBytes;
            if (storedByteLimit < closeReserve + MaximumReceiptBytes)
                throw new ArgumentOutOfRangeException(nameof(storedByteLimit));
            // Closed receipts remain in the journal. Include a whole lifecycle
            // in the ordinary allowance so interleaving one session's close with
            // another's final action cannot prematurely consume its quota. Keep
            // a separate worst-case release reserve for all live sessions.
            _ordinaryEntryLimit = checked(maximumLiveSessions * (maximumActionsPerSession + 3));
            _maximumEntries = checked(_ordinaryEntryLimit + maximumLiveSessions);
            _maximumStoredBytes = storedByteLimit;
            _ordinaryByteLimit = storedByteLimit - closeReserve;
        }

        /// <summary>
        /// Checks replay/conflict before capacity. The callback is never invoked
        /// for either a replay or a rejection, and admission reserves the largest
        /// allowed receipt before the callback can mutate game state.
        /// </summary>
        internal Result Execute(AgentGameRequest request, bool releasesLiveSession,
            Func<string> execute)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentGameReceiptJournal));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            string canonical = JsonUtility.ToJson(request);
            if (_receipts.TryGetValue(request.requestId, out Receipt prior))
                return string.Equals(prior.Request, canonical, StringComparison.Ordinal)
                    ? new Result(Outcome.Response, prior.ReadResponse())
                    : new Result(Outcome.RequestConflict);
            // Normalization adds defaults and escaping. Never discover a request
            // size violation after its mutation has already run without a receipt.
            if (canonical.Length > AgentGameProtocol.MaximumRequestCharacters)
                return new Result(Outcome.RequestTooLarge);

            int entryLimit = releasesLiveSession ? _maximumEntries : _ordinaryEntryLimit;
            long byteLimit = releasesLiveSession ? _maximumStoredBytes : _ordinaryByteLimit;
            if (_receipts.Count >= entryLimit || StoredBytes + MaximumReceiptBytes > byteLimit)
                return new Result(Outcome.BudgetExceeded);

            string response = execute();
            if (response == null || response.Length > MaximumResponseCharacters)
                throw new InvalidOperationException("Mutation receipt exceeds its admitted size contract.");
            var receipt = new Receipt(canonical, response, _responses);
            _receipts.Add(request.requestId, receipt);
            _receiptStoredBytes += receipt.StoredBytes;
            return new Result(Outcome.Response, response);
        }

        private sealed class Receipt
        {
            internal readonly string Request;
            private readonly int[] _responseChunks;
            private readonly ResponseChunkStore _responses;
            private readonly int _responseCharacters;
            internal long StoredBytes => 2L * Request.Length +
                (long)_responseChunks.Length * sizeof(int) + ReceiptMetadataBytes;

            internal Receipt(string request, string response, ResponseChunkStore responses)
            {
                Request = request;
                _responseCharacters = response.Length;
                _responses = responses;
                _responseChunks = responses.Store(response);
            }

            internal string ReadResponse() => _responses.Read(_responseChunks, _responseCharacters);
        }

        /// <summary>Generic receipt storage; it knows no JSON fields or gameplay.
        /// Chunk fingerprints only locate candidates: a byte comparison after
        /// decompression proves equality, so hash collisions cannot alter receipts.
        /// No decoded payload or chain of previous responses is retained.</summary>
        private sealed class ResponseChunkStore
        {
            private readonly List<Chunk> _chunks = new List<Chunk>();
            private readonly Dictionary<ulong, List<int>> _fingerprints = new Dictionary<ulong, List<int>>();
            internal long StoredBytes { get; private set; }

            internal int[] Store(string response)
            {
                // Preserve UTF-16 code units exactly, including surrogate pairs;
                // an encoding fallback must not silently change a replayed receipt.
                var raw = new byte[checked(response.Length * 2)];
                Buffer.BlockCopy(response.ToCharArray(), 0, raw, 0, raw.Length);
                var indices = new int[(response.Length + ResponseChunkCharacters - 1) / ResponseChunkCharacters];
                for (int i = 0; i < indices.Length; i++)
                {
                    int offset = i * ResponseChunkCharacters * 2;
                    int count = Math.Min(ResponseChunkCharacters * 2, raw.Length - offset);
                    ulong fingerprint = Fingerprint(raw, offset, count);
                    if (!_fingerprints.TryGetValue(fingerprint, out List<int> candidates))
                    {
                        candidates = new List<int>();
                        _fingerprints.Add(fingerprint, candidates);
                    }
                    int index = -1;
                    foreach (int candidate in candidates)
                        if (_chunks[candidate].Matches(raw, offset, count)) { index = candidate; break; }
                    if (index < 0)
                    {
                        var chunk = new Chunk(raw, offset, count);
                        index = _chunks.Count;
                        _chunks.Add(chunk); candidates.Add(index);
                        StoredBytes += chunk.StoredBytes;
                    }
                    indices[i] = index;
                }
                return indices;
            }

            internal string Read(int[] indices, int characterCount)
            {
                var raw = new byte[checked(characterCount * 2)];
                int offset = 0;
                foreach (int index in indices)
                {
                    byte[] bytes = _chunks[index].Read();
                    if (bytes.Length > raw.Length - offset)
                        throw new InvalidDataException("Mutation receipt exceeds its recorded length.");
                    Buffer.BlockCopy(bytes, 0, raw, offset, bytes.Length);
                    offset += bytes.Length;
                }
                if (offset != raw.Length) throw new InvalidDataException("Incomplete mutation receipt.");
                var characters = new char[characterCount];
                Buffer.BlockCopy(raw, 0, characters, 0, raw.Length);
                return new string(characters);
            }

            private static ulong Fingerprint(byte[] raw, int offset, int count)
            {
                ulong value = 14695981039346656037UL;
                unchecked
                {
                    for (int i = offset; i < offset + count; i++) value = (value ^ raw[i]) * 1099511628211UL;
                }
                return value;
            }

            internal void Clear() { _chunks.Clear(); _fingerprints.Clear(); StoredBytes = 0; }

            private sealed class Chunk
            {
                private readonly byte[] _payload;
                private readonly int _rawLength;
                private readonly bool _compressed;
                internal long StoredBytes => _payload.Length + ChunkMetadataBytes;

                internal Chunk(byte[] raw, int offset, int count)
                {
                    _rawLength = count;
                    using (var buffer = new MemoryStream())
                    {
                        using (var deflater = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Optimal, true))
                            deflater.Write(raw, offset, count);
                        _compressed = buffer.Length < count;
                        if (_compressed) _payload = buffer.ToArray();
                        else
                        {
                            _payload = new byte[count];
                            Buffer.BlockCopy(raw, offset, _payload, 0, count);
                        }
                    }
                }

                internal bool Matches(byte[] raw, int offset, int count)
                {
                    if (count != _rawLength) return false;
                    byte[] existing = Read();
                    for (int i = 0; i < count; i++)
                        if (raw[offset + i] != existing[i]) return false;
                    return true;
                }

                internal byte[] Read()
                {
                    if (!_compressed) return _payload;
                    var raw = new byte[_rawLength];
                    using (var buffer = new MemoryStream(_payload, false))
                    using (var inflater = new DeflateStream(buffer, CompressionMode.Decompress))
                    {
                        int offset = 0;
                        while (offset < raw.Length)
                        {
                            int count = inflater.Read(raw, offset, raw.Length - offset);
                            if (count == 0) throw new InvalidDataException("Incomplete mutation receipt chunk.");
                            offset += count;
                        }
                        if (inflater.ReadByte() != -1)
                            throw new InvalidDataException("Mutation receipt chunk exceeds its recorded length.");
                    }
                    return raw;
                }
            }
        }

        public void Dispose()
        { _disposed = true; _receipts.Clear(); _responses.Clear(); _receiptStoredBytes = 0; }
    }
}
