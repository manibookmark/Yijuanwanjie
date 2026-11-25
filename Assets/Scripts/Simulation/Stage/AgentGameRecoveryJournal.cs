using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns one writer's durable mutation archive, its framing and integrity.
    /// A complete frame is committed before an acknowledgement is returned.
    /// Only an incomplete final frame is discarded; complete corruption or a
    /// different frozen build rejects recovery. No gameplay or user-save state
    /// is read here. This is the sole durable receipt payload owner: ordinary
    /// retries read one indexed record without retaining another history in RAM.
    /// The service reconstructs state by executing these receipts.
    /// Schema 2 bounds both stored and decoded bytes and compresses unchanged
    /// UTF-16 receipts. Schema 1 remains readable and is appended as Schema 1.
    /// </summary>
    internal sealed class AgentGameRecoveryJournal : IDisposable
    {
        internal const string ArchiveFileName = "mutations.wjgj";
        internal const int MaximumRecords = 4 * (AgentGameCampaignSession.MaximumActions + 3) + 4;
        private const int SchemaVersion = 2;
        private const uint FrameMagic = 0x314a4757;
        private const uint CompressedFrameMagic = 0x324a4757, RawFrameMagic = 0x32524757;
        private const int HashBytes = 32;
        private const int CurrentFrameHeaderBytes = 44;
        private const int MaximumPayloadBytes = 16 * 1024 * 1024;
        private const long MaximumArchiveBytes = 512L * 1024 * 1024;
        private readonly long _maximumArchiveBytes, _maximumFrameBytes;
        private readonly int _maximumPayloadBytes;
        private int _schemaVersion = SchemaVersion;
        private readonly List<FrameIndex> _frames = new List<FrameIndex>();
        private readonly Dictionary<string, int> _requestIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly RecordView _records;
        private readonly Action _beforeCommit;
        private FileStream _writerLock, _stream;
        private byte[] _previousHash = new byte[HashBytes];
        private bool _disposed, _writeFailed;

        internal string BuildId { get; }
        internal IReadOnlyList<Record> Records => _records;
        internal int RecordCount => _frames.Count;
        internal long DiscardedTailBytes { get; private set; }

        [Serializable] private sealed class Header
        { public int schemaVersion; public string buildId; }
        [Serializable] internal sealed class Record
        { public int sequence; public string request, response, stateDigest; }
        private sealed class FrameIndex
        { internal long offset; internal byte[] previousHash; }

        // Bodies belong to disk. Each access decodes one detached receipt;
        // this view never caches decoded responses, including during replay.
        private sealed class RecordView : IReadOnlyList<Record>
        {
            private readonly AgentGameRecoveryJournal _owner;
            internal RecordView(AgentGameRecoveryJournal owner) { _owner = owner; }
            public int Count => _owner.RecordCount;
            public Record this[int index] => _owner.ReadRecord(index);
            public IEnumerator<Record> GetEnumerator()
            {
                int count = Count;
                for (int i = 0; i < count; i++) yield return this[i];
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        internal sealed class AdmissionException : IOException
        { internal AdmissionException() : base("Recovery archive admission limit reached.") { } }

        internal AgentGameRecoveryJournal(string directory, string buildId, Action beforeCommit = null,
            long maximumArchiveBytes = MaximumArchiveBytes, int maximumPayloadBytes = MaximumPayloadBytes)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("An archive directory is required.", nameof(directory));
            if (string.IsNullOrWhiteSpace(buildId) || buildId.Length > 256)
                throw new ArgumentException("A bounded frozen build identity is required.", nameof(buildId));
            if (maximumPayloadBytes <= 0 || maximumPayloadBytes > MaximumPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
            _maximumFrameBytes = (long)maximumPayloadBytes + CurrentFrameHeaderBytes + HashBytes;
            if (maximumArchiveBytes < 6 * _maximumFrameBytes || maximumArchiveBytes > MaximumArchiveBytes)
                throw new ArgumentOutOfRangeException(nameof(maximumArchiveBytes));
            BuildId = buildId; _beforeCommit = beforeCommit;
            _records = new RecordView(this);
            _maximumArchiveBytes = maximumArchiveBytes; _maximumPayloadBytes = maximumPayloadBytes;
            try
            {
                string root = Path.GetFullPath(directory);
                Directory.CreateDirectory(root);
                // A separate lock prevents a second owner replacing the archive
                // while this writer still holds an older file handle.
                _writerLock = new FileStream(Path.Combine(root, "writer.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _stream = new FileStream(Path.Combine(root, ArchiveFileName),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                if (_stream.Length == 0)
                    WriteFrame(JsonUtility.ToJson(new Header { schemaVersion = SchemaVersion, buildId = BuildId }), true);
                else ReadCommittedPrefix();
            }
            catch { Dispose(); throw; }
        }

        internal void RequireAdmission(bool releasesLiveSession)
        {
            ThrowIfUnavailable();
            int entryLimit = releasesLiveSession ? MaximumRecords : MaximumRecords - 4;
            long byteLimit = releasesLiveSession ? _maximumArchiveBytes : _maximumArchiveBytes - 4 * _maximumFrameBytes;
            if (_records.Count >= entryLimit || _stream.Length + _maximumFrameBytes > byteLimit)
                throw new AdmissionException();
        }

        internal void Append(string request, string response, string stateDigest)
        {
            ThrowIfUnavailable();
            if (string.IsNullOrEmpty(request) || request.Length > AgentGameProtocol.MaximumRequestCharacters ||
                response == null || response.Length > AgentGameReceiptJournal.MaximumResponseCharacters || !IsHash(stateDigest))
                throw new InvalidDataException("Mutation receipt exceeds its recovery contract.");
            var record = new Record { sequence = _records.Count + 1, request = request,
                response = response, stateDigest = stateDigest };
            string requestId = ReadRequestId(record);
            if (_requestIndexes.ContainsKey(requestId))
                throw new InvalidDataException("Recovery contains duplicate request identities.");
            long start = _stream.Length;
            byte[] prior = _previousHash;
            try
            {
                _beforeCommit?.Invoke();
                WriteFrame(JsonUtility.ToJson(record));
                AddIndex(start, prior, requestId);
            }
            catch
            {
                _writeFailed = true;
                // This is best effort. If IO is unavailable, reopening must still
                // decide from verified framing, never from an in-memory claim.
                try { _stream.SetLength(start); _stream.Position = start; _stream.Flush(true); }
                catch { }
                throw;
            }
        }

        private void ReadCommittedPrefix()
        {
            if (_stream.Length > _maximumArchiveBytes) throw new InvalidDataException("Recovery archive exceeds its bounded size.");
            if (!TryReadFrame(out string first, true)) throw new InvalidDataException("Recovery archive header is incomplete.");
            Header header = JsonUtility.FromJson<Header>(first);
            if (header == null || (header.schemaVersion != 1 && header.schemaVersion != SchemaVersion) || header.buildId != BuildId)
                throw new InvalidDataException("Recovery archive schema or frozen build does not match.");
            // Never silently migrate an existing archive. Its complete chain stays
            // in its original format, including subsequent appends after restart.
            _schemaVersion = header.schemaVersion;
            long completeEnd = _stream.Position;
            while (_stream.Position < _stream.Length)
            {
                long start = _stream.Position;
                byte[] prior = _previousHash;
                if (!TryReadFrame(out string payload))
                {
                    DiscardedTailBytes = _stream.Length - completeEnd;
                    _stream.SetLength(completeEnd);
                    _stream.Position = completeEnd;
                    _stream.Flush(true);
                    break;
                }
                if (_records.Count >= MaximumRecords) throw new InvalidDataException("Recovery archive has too many records.");
                Record record = JsonUtility.FromJson<Record>(payload);
                if (record == null || record.sequence != _records.Count + 1 ||
                    string.IsNullOrEmpty(record.request) || record.request.Length > AgentGameProtocol.MaximumRequestCharacters ||
                    record.response == null || record.response.Length > AgentGameReceiptJournal.MaximumResponseCharacters ||
                    !IsHash(record.stateDigest)) throw new InvalidDataException("Recovery receipt is malformed.");
                string requestId = ReadRequestId(record);
                if (_requestIndexes.ContainsKey(requestId))
                    throw new InvalidDataException("Recovery contains duplicate or missing request identities.");
                AddIndex(start, prior, requestId);
                completeEnd = _stream.Position;
            }
        }

        internal Record FindRecord(string requestId)
        {
            ThrowIfDisposed();
            return requestId != null && _requestIndexes.TryGetValue(requestId, out int index)
                ? ReadRecord(index) : null;
        }

        private void AddIndex(long offset, byte[] previousHash, string requestId)
        {
            _requestIndexes.Add(requestId, _frames.Count);
            _frames.Add(new FrameIndex { offset = offset, previousHash = previousHash });
        }

        private static string ReadRequestId(Record record)
        {
            AgentGameRequest request = JsonUtility.FromJson<AgentGameRequest>(record.request);
            if (request == null || string.IsNullOrEmpty(request.requestId))
                throw new InvalidDataException("Recovery contains a missing request identity.");
            return request.requestId;
        }

        private Record ReadRecord(int index)
        {
            ThrowIfDisposed();
            FrameIndex frame = _frames[index];
            long position = _stream.Position;
            byte[] prior = _previousHash;
            try
            {
                _stream.Position = frame.offset;
                _previousHash = frame.previousHash;
                if (!TryReadFrame(out string payload))
                    throw new InvalidDataException("Committed recovery frame is incomplete.");
                return JsonUtility.FromJson<Record>(payload);
            }
            finally { _stream.Position = position; _previousHash = prior; }
        }

        // The length has its own checksum, so a corrupt complete length cannot
        // masquerade as a harmless half-written final payload.
        // The header and Schema 1 use magic/length (8 bytes). Schema 2 records
        // use magic/storedLength/decodedLength (12 bytes), then in both versions
        // SHA-256(prefix), stored data, SHA-256(previous hash + prefix + data).
        private bool TryReadFrame(out string payload, bool isHeader = false)
        {
            payload = null;
            bool legacy = isHeader || _schemaVersion == 1;
            int prefixBytes = legacy ? 8 : 12;
            int headerBytes = prefixBytes + HashBytes;
            if (_stream.Length - _stream.Position < headerBytes) return false;
            byte[] prefix = ReadBytes(prefixBytes), lengthHash = ReadBytes(HashBytes);
            uint magic = ReadUInt(prefix, 0);
            if ((legacy ? magic != FrameMagic : magic != CompressedFrameMagic && magic != RawFrameMagic) ||
                !Equal(Hash(prefix), lengthHash))
                throw new InvalidDataException("Recovery frame header is corrupt.");
            uint length = ReadUInt(prefix, 4);
            uint decodedLength = legacy ? length : ReadUInt(prefix, 8);
            bool compressed = !legacy && magic == CompressedFrameMagic;
            if (length == 0 || length > _maximumPayloadBytes || decodedLength == 0 ||
                decodedLength > _maximumPayloadBytes || decodedLength % 2 != 0 ||
                (!compressed && decodedLength != length))
                throw new InvalidDataException("Recovery frame length is invalid.");
            if (_stream.Length - _stream.Position < (long)length + HashBytes) return false;
            byte[] data = ReadBytes((int)length), checksum = ReadBytes(HashBytes);
            if (!Equal(HashChain(_previousHash, prefix, data), checksum))
                throw new InvalidDataException("Recovery frame checksum or chain is corrupt.");
            if (compressed) data = Decompress(data, (int)decodedLength);
            var characters = new char[data.Length / 2];
            Buffer.BlockCopy(data, 0, characters, 0, data.Length);
            payload = new string(characters);
            _previousHash = checksum;
            return true;
        }

        private void WriteFrame(string payload, bool isHeader = false)
        {
            byte[] data = new byte[checked(payload.Length * 2)];
            if (data.Length == 0 || data.Length > _maximumPayloadBytes) throw new InvalidDataException("Recovery frame exceeds its bounded size.");
            // Preserve UTF-16 code units exactly, as the request receipt owner does.
            Buffer.BlockCopy(payload.ToCharArray(), 0, data, 0, data.Length);
            bool legacy = isHeader || _schemaVersion == 1;
            uint decodedLength = (uint)data.Length;
            uint magic = FrameMagic;
            if (!legacy)
            {
                byte[] packed = Compress(data);
                if (packed.Length < data.Length) { data = packed; magic = CompressedFrameMagic; }
                else magic = RawFrameMagic;
            }
            byte[] prefix = new byte[legacy ? 8 : 12];
            WriteUInt(prefix, 0, magic); WriteUInt(prefix, 4, (uint)data.Length);
            if (!legacy) WriteUInt(prefix, 8, decodedLength);
            if (_stream.Length + prefix.Length + HashBytes * 2L + data.Length > _maximumArchiveBytes)
                throw new AdmissionException();
            byte[] headerChecksum = Hash(prefix), checksum = HashChain(_previousHash, prefix, data);
            _stream.Position = _stream.Length;
            _stream.Write(prefix, 0, prefix.Length);
            _stream.Write(headerChecksum, 0, headerChecksum.Length);
            _stream.Write(data, 0, data.Length);
            _stream.Write(checksum, 0, checksum.Length);
            _stream.Flush(true);
            _previousHash = checksum;
        }

        private static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var compressor = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    compressor.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] data, int decodedLength)
        {
            // Allocate only the authenticated, bounded declared length. An extra
            // decoded byte rejects bombs and length lies without growing a buffer.
            var decoded = new byte[decodedLength];
            using (var input = new MemoryStream(data, false))
            using (var decompressor = new DeflateStream(input, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < decoded.Length)
                {
                    int read = decompressor.Read(decoded, offset, decoded.Length - offset);
                    if (read == 0) throw new InvalidDataException("Recovery compressed frame is truncated.");
                    offset += read;
                }
                if (decompressor.ReadByte() != -1)
                    throw new InvalidDataException("Recovery compressed frame exceeds its declared length.");
            }
            return decoded;
        }

        private byte[] ReadBytes(int count)
        {
            var data = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(data, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Recovery archive changed while reading.");
                offset += read;
            }
            return data;
        }
        private static byte[] Hash(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(bytes); }
        private static byte[] HashChain(byte[] prior, byte[] prefix, byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                sha.TransformBlock(prior, 0, prior.Length, null, 0);
                sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
                sha.TransformFinalBlock(data, 0, data.Length);
                return sha.Hash;
            }
        }
        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
        private static uint ReadUInt(byte[] value, int offset)
            => (uint)(value[offset] | value[offset + 1] << 8 | value[offset + 2] << 16 | value[offset + 3] << 24);
        private static void WriteUInt(byte[] value, int offset, uint number)
        { for (int i = 0; i < 4; i++) value[offset + i] = (byte)(number >> (8 * i)); }
        private void ThrowIfUnavailable()
        {
            ThrowIfDisposed();
            if (_writeFailed) throw new IOException("Recovery writer stopped after a persistence failure.");
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentGameRecoveryJournal));
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream?.Dispose(); }
            finally { _writerLock?.Dispose(); }
        }
    }
}
