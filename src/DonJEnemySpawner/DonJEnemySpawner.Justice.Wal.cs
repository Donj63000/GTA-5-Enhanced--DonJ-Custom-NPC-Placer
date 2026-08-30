using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal sealed class JusticeWalRecord
{
    private readonly ReadOnlyCollection<JusticePersistenceField> _fields;

    internal JusticeWalRecord(
        string transactionId,
        string operationKind,
        int profileSlot,
        JusticeWalState state,
        long persistenceRevision,
        long createdAtUtcTicks,
        IEnumerable<JusticePersistenceField> fields)
        : this(
            0L,
            transactionId,
            operationKind,
            profileSlot,
            state,
            persistenceRevision,
            createdAtUtcTicks,
            fields)
    {
    }

    internal JusticeWalRecord(
        long sequence,
        string transactionId,
        string operationKind,
        int profileSlot,
        JusticeWalState state,
        long persistenceRevision,
        long createdAtUtcTicks,
        IEnumerable<JusticePersistenceField> fields)
    {
        if (sequence < 0L)
        {
            throw new ArgumentOutOfRangeException("sequence");
        }

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException("L'identifiant de transaction est obligatoire.", "transactionId");
        }

        if (string.IsNullOrWhiteSpace(operationKind))
        {
            throw new ArgumentException("Le type d'operation est obligatoire.", "operationKind");
        }

        if (profileSlot < -1)
        {
            throw new ArgumentOutOfRangeException("profileSlot");
        }

        if (!Enum.IsDefined(typeof(JusticeWalState), state))
        {
            throw new ArgumentOutOfRangeException("state");
        }

        if (persistenceRevision < 0L)
        {
            throw new ArgumentOutOfRangeException("persistenceRevision");
        }

        if (createdAtUtcTicks < DateTime.MinValue.Ticks ||
            createdAtUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException("createdAtUtcTicks");
        }

        Sequence = sequence;
        TransactionId = transactionId.Trim();
        OperationKind = operationKind.Trim();
        ProfileSlot = profileSlot;
        State = state;
        PersistenceRevision = persistenceRevision;
        CreatedAtUtcTicks = createdAtUtcTicks;

        List<JusticePersistenceField> copy = new List<JusticePersistenceField>();
        if (fields != null)
        {
            foreach (JusticePersistenceField field in fields)
            {
                if (field == null)
                {
                    throw new ArgumentException(
                        "Une transaction WAL ne peut pas contenir de champ nul.",
                        "fields");
                }

                copy.Add(new JusticePersistenceField(field.Path, field.Value));
            }
        }

        _fields = new ReadOnlyCollection<JusticePersistenceField>(copy);
    }

    internal long Sequence { get; private set; }

    internal string TransactionId { get; private set; }

    internal string OperationKind { get; private set; }

    internal int ProfileSlot { get; private set; }

    internal JusticeWalState State { get; private set; }

    internal long PersistenceRevision { get; private set; }

    internal long CreatedAtUtcTicks { get; private set; }

    internal IReadOnlyList<JusticePersistenceField> Fields
    {
        get { return _fields; }
    }

    internal bool IsTerminal
    {
        get { return State == JusticeWalState.Confirmed || State == JusticeWalState.Rejected; }
    }

    internal JusticeWalRecord WithSequence(long sequence)
    {
        return new JusticeWalRecord(
            sequence,
            TransactionId,
            OperationKind,
            ProfileSlot,
            State,
            PersistenceRevision,
            CreatedAtUtcTicks,
            Fields);
    }
}

internal sealed class JusticeWalRecoveryResult
{
    private readonly ReadOnlyCollection<JusticeWalRecord> _records;

    internal JusticeWalRecoveryResult(
        JusticeWalRecoveryStatus status,
        IEnumerable<JusticeWalRecord> records,
        long lastValidLength,
        string error)
    {
        Status = status;
        _records = new ReadOnlyCollection<JusticeWalRecord>(
            new List<JusticeWalRecord>(records ?? new JusticeWalRecord[0]));
        LastValidLength = Math.Max(0L, lastValidLength);
        Error = error ?? string.Empty;
    }

    internal JusticeWalRecoveryStatus Status { get; private set; }

    internal IReadOnlyList<JusticeWalRecord> Records
    {
        get { return _records; }
    }

    internal long LastValidLength { get; private set; }

    internal string Error { get; private set; }
}

// Je journalise les transitions avant les effets irreversibles. Une frame n'est
// reconnue que si son entete, son contenu et son SHA-256 sont complets : une fin
// partielle est donc equivalente a une transition jamais acquittee par Append.
internal sealed class JusticeWriteAheadLog
{
    private static readonly byte[] FrameMagic = { (byte)'D', (byte)'J', (byte)'W', (byte)'L' };

    private const int FormatVersion = 1;
    private const int HashLength = 32;
    private const int HeaderLength = 4 + 4 + 4 + HashLength;
    private const int MaxPayloadLength = 1024;
    private const int MaxFieldsPerRecord = 20;
    private const int MaxTransactionIdBytes = 192;
    private const int MaxOperationKindBytes = 80;
    private const int MaxFieldPathBytes = 64;
    private const int MaxFieldValueBytes = 256;

    private readonly object _gate = new object();
    private readonly string _path;
    private readonly IJusticePersistenceFaultInjector _faultInjector;
    private readonly List<JusticeWalRecord> _records = new List<JusticeWalRecord>();
    private readonly Dictionary<string, JusticeWalRecord> _latestByTransaction =
        new Dictionary<string, JusticeWalRecord>(StringComparer.Ordinal);

    private long _durableLength;
    private long _lastSequence;
    private long _walRevision;
    private long _repairedTailCount;
    private JusticeWalRecoveryStatus _recoveryStatus;
    private string _lastError;

    internal JusticeWriteAheadLog(string path)
        : this(path, JusticeNoOpPersistenceFaultInjector.Instance)
    {
    }

    internal JusticeWriteAheadLog(
        string path,
        IJusticePersistenceFaultInjector faultInjector)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du WAL est obligatoire.", "path");
        }

        _path = Path.GetFullPath(path);
        _faultInjector = faultInjector ?? JusticeNoOpPersistenceFaultInjector.Instance;
        _lastError = string.Empty;
        ReloadAndRepairTail();
    }

    internal JusticeWalRecord Append(JusticeWalRecord requested)
    {
        if (requested == null)
        {
            throw new ArgumentNullException("requested");
        }

        lock (_gate)
        {
            EnsureHealthy();
            EnsureFileWasNotChangedExternally();

            JusticeWalRecord previous;
            if (_latestByTransaction.TryGetValue(requested.TransactionId, out previous))
            {
                if (AreSameTransition(previous, requested))
                {
                    // Je rends un retry post-Flush idempotent : l'appelant peut
                    // perdre l'acquittement sans dupliquer la transition durable.
                    return previous;
                }

                ValidateTransition(previous, requested);
            }
            else if (requested.State != JusticeWalState.Prepared)
            {
                throw new InvalidOperationException(
                    "Une transaction WAL doit commencer dans l'etat Prepared.");
            }

            JusticeWalRecord durable = requested.WithSequence(checked(_lastSequence + 1L));
            byte[] payload = SerializePayload(durable);
            byte[] frame = BuildFrame(payload);
            AppendFrame(frame, durable);
            return durable;
        }
    }

    internal JusticeWalRecord GetLatest(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        lock (_gate)
        {
            JusticeWalRecord record;
            return _latestByTransaction.TryGetValue(transactionId.Trim(), out record)
                ? record
                : null;
        }
    }

    internal IReadOnlyList<JusticeWalRecord> GetOpenTransactions()
    {
        lock (_gate)
        {
            List<JusticeWalRecord> open = new List<JusticeWalRecord>();
            foreach (JusticeWalRecord record in _latestByTransaction.Values)
            {
                if (!record.IsTerminal)
                {
                    open.Add(record);
                }
            }

            open.Sort(delegate(JusticeWalRecord left, JusticeWalRecord right)
            {
                return left.Sequence.CompareTo(right.Sequence);
            });
            return new ReadOnlyCollection<JusticeWalRecord>(open);
        }
    }

    internal bool HasOpenTransactionKind(string operationKind)
    {
        if (string.IsNullOrWhiteSpace(operationKind))
        {
            return false;
        }

        lock (_gate)
        {
            foreach (JusticeWalRecord record in _latestByTransaction.Values)
            {
                if (!record.IsTerminal && string.Equals(
                        record.OperationKind,
                        operationKind,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal IReadOnlyList<JusticeWalRecord> GetLatestTransactions()
    {
        lock (_gate)
        {
            List<JusticeWalRecord> latest = new List<JusticeWalRecord>(
                _latestByTransaction.Values);
            latest.Sort(delegate(JusticeWalRecord left, JusticeWalRecord right)
            {
                return left.Sequence.CompareTo(right.Sequence);
            });
            return new ReadOnlyCollection<JusticeWalRecord>(latest);
        }
    }

    internal bool CompactIfNoOpenTransactions()
    {
        lock (_gate)
        {
            EnsureHealthy();
            EnsureFileWasNotChangedExternally();
            foreach (JusticeWalRecord record in _latestByTransaction.Values)
            {
                if (!record.IsTerminal)
                {
                    return false;
                }
            }

            if (_durableLength == 0L)
            {
                return true;
            }

            string directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Le dossier du WAL est introuvable.");
            }

            Directory.CreateDirectory(directory);
            string temp = Path.Combine(
                directory,
                Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // Je remplace le WAL par un fichier vide préalablement flushé. Je
                // n'utilise aucun fallback Copy/Delete/Move faussement atomique.
                using (FileStream stream = new FileStream(
                    temp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Flush(true);
                }

                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null, true);
                }
                else
                {
                    File.Move(temp, _path);
                }

                _records.Clear();
                _latestByTransaction.Clear();
                _durableLength = 0L;
                _lastSequence = 0L;
                _walRevision = 0L;
                _recoveryStatus = JusticeWalRecoveryStatus.Clean;
                _lastError = string.Empty;
                return true;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch
                    {
                        // Je conserve l'erreur principale; ce reliquat temporaire
                        // ne change pas l'autorité du WAL courant.
                    }
                }
            }
        }
    }

    internal JusticeWalDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            int openCount = 0;
            foreach (JusticeWalRecord record in _latestByTransaction.Values)
            {
                if (!record.IsTerminal)
                {
                    openCount++;
                }
            }

            return new JusticeWalDiagnostics(
                _lastSequence,
                _walRevision,
                _durableLength,
                openCount,
                _recoveryStatus,
                _repairedTailCount,
                _lastError);
        }
    }

    internal static JusticeWalRecoveryResult Recover(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du WAL est obligatoire.", "path");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new JusticeWalRecoveryResult(
                JusticeWalRecoveryStatus.Clean,
                new JusticeWalRecord[0],
                0L,
                string.Empty);
        }

        List<JusticeWalRecord> records = new List<JusticeWalRecord>();
        Dictionary<string, JusticeWalRecord> latest =
            new Dictionary<string, JusticeWalRecord>(StringComparer.Ordinal);
        long lastValidLength = 0L;
        long lastSequence = 0L;

        try
        {
            using (FileStream stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                while (stream.Position < stream.Length)
                {
                    long frameStart = stream.Position;
                    long remaining = stream.Length - frameStart;
                    if (remaining < HeaderLength)
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.TruncatedTail,
                            records,
                            lastValidLength,
                            "Entete WAL finale incomplete.");
                    }

                    byte[] magic = reader.ReadBytes(FrameMagic.Length);
                    int version = reader.ReadInt32();
                    int payloadLength = reader.ReadInt32();
                    byte[] expectedHash = reader.ReadBytes(HashLength);
                    if (!BytesEqual(magic, FrameMagic) || version != FormatVersion ||
                        payloadLength <= 0 || payloadLength > MaxPayloadLength ||
                        expectedHash.Length != HashLength)
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.Corrupt,
                            records,
                            lastValidLength,
                            "Entete WAL invalide a l'octet " + frameStart + ".");
                    }

                    if (stream.Length - stream.Position < payloadLength)
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.TruncatedTail,
                            records,
                            lastValidLength,
                            "Payload WAL final incomplet.");
                    }

                    byte[] payload = reader.ReadBytes(payloadLength);
                    byte[] actualHash = ComputeSha256(payload);
                    if (!BytesEqual(expectedHash, actualHash))
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.Corrupt,
                            records,
                            lastValidLength,
                            "Checksum WAL invalide a l'octet " + frameStart + ".");
                    }

                    JusticeWalRecord record;
                    string parseError;
                    if (!TryDeserializePayload(payload, out record, out parseError))
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.Corrupt,
                            records,
                            lastValidLength,
                            "Payload WAL invalide : " + parseError);
                    }

                    if (record.Sequence <= lastSequence)
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.Corrupt,
                            records,
                            lastValidLength,
                            "Sequence WAL non monotone.");
                    }

                    JusticeWalRecord previous;
                    if (latest.TryGetValue(record.TransactionId, out previous))
                    {
                        try
                        {
                            ValidateTransition(previous, record);
                        }
                        catch (Exception exception)
                        {
                            return new JusticeWalRecoveryResult(
                                JusticeWalRecoveryStatus.Corrupt,
                                records,
                                lastValidLength,
                                "Transition WAL invalide : " + exception.Message);
                        }
                    }
                    else if (record.State != JusticeWalState.Prepared)
                    {
                        return new JusticeWalRecoveryResult(
                            JusticeWalRecoveryStatus.Corrupt,
                            records,
                            lastValidLength,
                            "Transaction WAL sans etape Prepared.");
                    }

                    records.Add(record);
                    latest[record.TransactionId] = record;
                    lastSequence = record.Sequence;
                    lastValidLength = stream.Position;
                }
            }
        }
        catch (Exception exception)
        {
            return new JusticeWalRecoveryResult(
                JusticeWalRecoveryStatus.Corrupt,
                records,
                lastValidLength,
                exception.GetType().Name + ": " + exception.Message);
        }

        return new JusticeWalRecoveryResult(
            JusticeWalRecoveryStatus.Clean,
            records,
            lastValidLength,
            string.Empty);
    }

    private void AppendFrame(byte[] frame, JusticeWalRecord record)
    {
        string directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Le dossier du WAL est introuvable.");
        }

        Directory.CreateDirectory(directory);
        long originalLength = _durableLength;
        try
        {
            using (FileStream stream = new FileStream(
                _path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough))
            {
                if (stream.Length != originalLength)
                {
                    throw new IOException("Le WAL a change pendant l'ecriture.");
                }

                stream.Position = originalLength;
                try
                {
                    _faultInjector.Probe(JusticePersistenceFaultPoint.BeforeWalFrameWrite);
                    stream.Write(frame, 0, frame.Length);
                    _faultInjector.Probe(JusticePersistenceFaultPoint.BeforeWalFlush);
                    stream.Flush(true);
                }
                catch
                {
                    // Je reviens au dernier prefixe durable pour toute erreur
                    // interceptee. Un vrai arret brutal sera repare au prochain load.
                    stream.SetLength(originalLength);
                    stream.Flush(true);
                    throw;
                }
            }

            RegisterDurableRecord(record, originalLength + frame.Length);
            _faultInjector.Probe(JusticePersistenceFaultPoint.AfterWalFlush);
        }
        catch (Exception exception)
        {
            _lastError = exception.GetType().Name + ": " + exception.Message;
            ReloadAndRepairTail();
            throw;
        }
    }

    private void RegisterDurableRecord(JusticeWalRecord record, long durableLength)
    {
        _records.Add(record);
        _latestByTransaction[record.TransactionId] = record;
        _lastSequence = record.Sequence;
        _walRevision = Math.Max(_walRevision, record.PersistenceRevision);
        _durableLength = durableLength;
        _recoveryStatus = JusticeWalRecoveryStatus.Clean;
        _lastError = string.Empty;
    }

    private void EnsureFileWasNotChangedExternally()
    {
        long actualLength = File.Exists(_path) ? new FileInfo(_path).Length : 0L;
        if (actualLength == _durableLength)
        {
            return;
        }

        ReloadAndRepairTail();
        EnsureHealthy();
    }

    private void ReloadAndRepairTail()
    {
        JusticeWalRecoveryResult recovered = Recover(_path);
        if (recovered.Status == JusticeWalRecoveryStatus.TruncatedTail)
        {
            RepairTruncatedTail(recovered.LastValidLength);
            _repairedTailCount++;
        }

        _records.Clear();
        _latestByTransaction.Clear();
        _lastSequence = 0L;
        _walRevision = 0L;
        foreach (JusticeWalRecord record in recovered.Records)
        {
            _records.Add(record);
            _latestByTransaction[record.TransactionId] = record;
            _lastSequence = Math.Max(_lastSequence, record.Sequence);
            _walRevision = Math.Max(_walRevision, record.PersistenceRevision);
        }

        _durableLength = recovered.LastValidLength;
        _recoveryStatus = recovered.Status;
        _lastError = recovered.Error;
    }

    private void RepairTruncatedTail(long validLength)
    {
        if (!File.Exists(_path))
        {
            return;
        }

        using (FileStream stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough))
        {
            _faultInjector.Probe(JusticePersistenceFaultPoint.BeforeWalTailRepairFlush);
            stream.SetLength(validLength);
            stream.Flush(true);
            _faultInjector.Probe(JusticePersistenceFaultPoint.AfterWalTailRepairFlush);
        }
    }

    private void EnsureHealthy()
    {
        if (_recoveryStatus == JusticeWalRecoveryStatus.Corrupt)
        {
            throw new InvalidDataException(
                "Le WAL Justice est corrompu; aucune transition ne peut etre rejouee. " +
                _lastError);
        }
    }

    private static void ValidateTransition(JusticeWalRecord previous, JusticeWalRecord next)
    {
        if (!string.Equals(previous.TransactionId, next.TransactionId, StringComparison.Ordinal) ||
            !string.Equals(previous.OperationKind, next.OperationKind, StringComparison.Ordinal) ||
            previous.ProfileSlot != next.ProfileSlot ||
            previous.CreatedAtUtcTicks != next.CreatedAtUtcTicks ||
            !FieldsEqual(previous.Fields, next.Fields))
        {
            throw new InvalidOperationException(
                "Les donnees immuables d'une transaction WAL ont change.");
        }

        if (next.PersistenceRevision < previous.PersistenceRevision)
        {
            throw new InvalidOperationException("La revision WAL ne peut pas reculer.");
        }

        bool allowed =
            (previous.State == JusticeWalState.Prepared &&
             (next.State == JusticeWalState.Attempted || next.State == JusticeWalState.Rejected)) ||
            (previous.State == JusticeWalState.Attempted &&
             (next.State == JusticeWalState.Confirmed || next.State == JusticeWalState.Rejected ||
              next.State == JusticeWalState.Ambiguous)) ||
            (previous.State == JusticeWalState.Ambiguous &&
             (next.State == JusticeWalState.Confirmed || next.State == JusticeWalState.Rejected));
        if (!allowed)
        {
            throw new InvalidOperationException(
                "Transition WAL interdite de " + previous.State + " vers " + next.State + ".");
        }
    }

    private static bool AreSameTransition(JusticeWalRecord left, JusticeWalRecord right)
    {
        return left.State == right.State &&
               left.PersistenceRevision == right.PersistenceRevision &&
               string.Equals(left.TransactionId, right.TransactionId, StringComparison.Ordinal) &&
               string.Equals(left.OperationKind, right.OperationKind, StringComparison.Ordinal) &&
               left.ProfileSlot == right.ProfileSlot &&
               left.CreatedAtUtcTicks == right.CreatedAtUtcTicks &&
               FieldsEqual(left.Fields, right.Fields);
    }

    private static bool FieldsEqual(
        IReadOnlyList<JusticePersistenceField> left,
        IReadOnlyList<JusticePersistenceField> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Path, right[index].Path, StringComparison.Ordinal) ||
                !string.Equals(left[index].Value, right[index].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] BuildFrame(byte[] payload)
    {
        byte[] hash = ComputeSha256(payload);
        using (MemoryStream stream = new MemoryStream(HeaderLength + payload.Length))
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(FrameMagic);
            writer.Write(FormatVersion);
            writer.Write(payload.Length);
            writer.Write(hash);
            writer.Write(payload);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static byte[] SerializePayload(JusticeWalRecord record)
    {
        ValidateBoundedRecord(record);
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(record.Sequence);
            writer.Write(record.TransactionId);
            writer.Write(record.OperationKind);
            writer.Write(record.ProfileSlot);
            writer.Write((int)record.State);
            writer.Write(record.PersistenceRevision);
            writer.Write(record.CreatedAtUtcTicks);
            writer.Write(record.Fields.Count);
            for (int index = 0; index < record.Fields.Count; index++)
            {
                writer.Write(record.Fields[index].Path);
                writer.Write(record.Fields[index].Value);
            }

            writer.Flush();
            if (stream.Length <= 0L || stream.Length > MaxPayloadLength)
            {
                throw new InvalidDataException("Le payload WAL depasse la borne autorisee.");
            }

            return stream.ToArray();
        }
    }

    private static bool TryDeserializePayload(
        byte[] payload,
        out JusticeWalRecord record,
        out string error)
    {
        record = null;
        error = string.Empty;
        try
        {
            using (MemoryStream stream = new MemoryStream(payload, false))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                long sequence = reader.ReadInt64();
                string transactionId = reader.ReadString();
                string operationKind = reader.ReadString();
                int profileSlot = reader.ReadInt32();
                int rawState = reader.ReadInt32();
                long persistenceRevision = reader.ReadInt64();
                long createdAtUtcTicks = reader.ReadInt64();
                int fieldCount = reader.ReadInt32();
                if (fieldCount < 0 || fieldCount > MaxFieldsPerRecord ||
                    !Enum.IsDefined(typeof(JusticeWalState), rawState))
                {
                    throw new InvalidDataException("Metadonnees WAL hors bornes.");
                }

                List<JusticePersistenceField> fields =
                    new List<JusticePersistenceField>(fieldCount);
                for (int index = 0; index < fieldCount; index++)
                {
                    fields.Add(new JusticePersistenceField(reader.ReadString(), reader.ReadString()));
                }

                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("Octets WAL surnumeraires.");
                }

                record = new JusticeWalRecord(
                    sequence,
                    transactionId,
                    operationKind,
                    profileSlot,
                    (JusticeWalState)rawState,
                    persistenceRevision,
                    createdAtUtcTicks,
                    fields);
                ValidateBoundedRecord(record);
                return true;
            }
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static void ValidateBoundedRecord(JusticeWalRecord record)
    {
        if (record == null || record.Fields.Count > MaxFieldsPerRecord ||
            Encoding.UTF8.GetByteCount(record.TransactionId) > MaxTransactionIdBytes ||
            Encoding.UTF8.GetByteCount(record.OperationKind) > MaxOperationKindBytes)
        {
            throw new InvalidDataException("Métadonnées WAL hors bornes.");
        }

        for (int index = 0; index < record.Fields.Count; index++)
        {
            JusticePersistenceField field = record.Fields[index];
            string path = field.Path ?? string.Empty;
            string value = field.Value ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(path) > MaxFieldPathBytes ||
                Encoding.UTF8.GetByteCount(value) > MaxFieldValueBytes ||
                string.Equals(path, "Case", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "Record", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "Custody", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOf("<Case", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("<Record", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("<Custody", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException(
                    "Le WAL contient un champ volumineux ou un fragment XML interdit.");
            }
        }
    }

    private static byte[] ComputeSha256(byte[] value)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            return algorithm.ComputeHash(value);
        }
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        int difference = 0;
        for (int index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }
}
