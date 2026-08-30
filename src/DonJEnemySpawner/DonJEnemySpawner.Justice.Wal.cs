using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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
    private const int ExclusiveLeaseTimeoutMilliseconds = 25;
    private const string ExclusiveLeaseNamePrefix = "Local\\DonJJusticeWal-";

    private readonly object _gate = new object();
    private readonly string _path;
    private readonly IJusticePersistenceFaultInjector _faultInjector;
    private readonly List<JusticeWalRecord> _records = new List<JusticeWalRecord>();
    private readonly Dictionary<string, JusticeWalRecord> _latestByTransaction =
        new Dictionary<string, JusticeWalRecord>(StringComparer.Ordinal);

    private long _durableLength;
    private byte[] _durablePrefixHash;
    private bool _durableFileExists;
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
        using (AcquireExclusiveLease(_path))
        {
            ReloadAndRepairTailUnderExclusiveLease();
        }
    }

    internal JusticeWalRecord Append(JusticeWalRecord requested)
    {
        if (requested == null)
        {
            throw new ArgumentNullException("requested");
        }

        lock (_gate)
        {
            using (AcquireExclusiveLease(_path))
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
                AppendFrameUnderExclusiveLease(frame, durable);
                return durable;
            }
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
            using (AcquireExclusiveLease(_path))
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

                    // Je garde le verrou inter-processus de la validation jusqu'au
                    // remplacement : aucune autre instance ne peut publier une frame
                    // dans l'intervalle puis la voir effacée par la compaction.
                    EnsureFileWasNotChangedExternally();
                    foreach (JusticeWalRecord record in _latestByTransaction.Values)
                    {
                        if (!record.IsTerminal)
                        {
                            return false;
                        }
                    }
                    _faultInjector.Probe(
                        JusticePersistenceFaultPoint.BeforeWalCompactReplace);
                    File.Replace(temp, _path, null, true);

                    _records.Clear();
                    _latestByTransaction.Clear();
                    _durableLength = 0L;
                    _durablePrefixHash = ComputeSha256(new byte[0]);
                    _durableFileExists = true;
                    _lastSequence = 0L;
                    _walRevision = 0L;
                    _recoveryStatus = JusticeWalRecoveryStatus.Clean;
                    _lastError = string.Empty;
                    return true;
                }
                finally
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
        using (AcquireExclusiveLease(fullPath))
        {
            return RecoverUnderExclusiveLease(fullPath);
        }
    }

    private static JusticeWalRecoveryResult RecoverUnderExclusiveLease(string fullPath)
    {
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
                FileShare.Read))
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
        catch (FileNotFoundException)
        {
            // Je ne conclus à l'absence qu'après l'échec explicite de l'ouverture.
            // File.Exists peut masquer un refus d'accès et n'est donc pas une preuve.
            return CreateMissingWalRecoveryResult();
        }
        catch (DirectoryNotFoundException)
        {
            return CreateMissingWalRecoveryResult();
        }
        catch (IOException)
        {
            // Je laisse remonter une indisponibilité disque transitoire afin que
            // l'initialisation Justice applique son backoff puis retente la lecture.
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Je distingue un accès momentanément refusé d'une corruption du
            // journal : ce problème d'environnement ne condamne pas la session.
            throw;
        }
        catch (System.Security.SecurityException)
        {
            // Je conserve la même sémantique retryable pour un refus de sécurité.
            throw;
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

    private static JusticeWalRecoveryResult CreateMissingWalRecoveryResult()
    {
        return new JusticeWalRecoveryResult(
            JusticeWalRecoveryStatus.Clean,
            new JusticeWalRecord[0],
            0L,
            string.Empty);
    }

    private void AppendFrameUnderExclusiveLease(byte[] frame, JusticeWalRecord record)
    {
        string directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Le dossier du WAL est introuvable.");
        }

        Directory.CreateDirectory(directory);
        long originalLength = _durableLength;
        byte[] durablePrefixHash = null;
        bool prefixIntegrityRejected = false;
        bool appendFileAcquired = false;
        try
        {
            FileStream appendStream = null;
            try
            {
                appendStream = new FileStream(
                    _path,
                    _durableFileExists ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);
                appendFileAcquired = true;
            }
            catch (FileNotFoundException)
            {
                prefixIntegrityRejected = true;
                RejectChangedDurablePrefix();
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                prefixIntegrityRejected = true;
                RejectChangedDurablePrefix();
                throw;
            }
            catch (IOException)
            {
                // CreateNew distingue la création appartenant à cette instance
                // d'un fichier apparu entre les deux contrôles d'intégrité.
                if (!_durableFileExists && File.Exists(_path))
                {
                    prefixIntegrityRejected = true;
                    RejectChangedDurablePrefix();
                }
                throw;
            }

            using (FileStream stream = appendStream)
            {
                if (stream.Length != originalLength)
                {
                    prefixIntegrityRejected = true;
                    RejectChangedDurablePrefix();
                }

                byte[] actualPrefixHash = ComputeStreamSha256(
                    stream,
                    originalLength);
                if (!BytesEqual(actualPrefixHash, _durablePrefixHash))
                {
                    prefixIntegrityRejected = true;
                    RejectChangedDurablePrefix();
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

                durablePrefixHash = ComputeStreamSha256(
                    stream,
                    checked(originalLength + frame.Length));
            }

            RegisterDurableRecord(
                record,
                originalLength + frame.Length,
                durablePrefixHash);
            _faultInjector.Probe(JusticePersistenceFaultPoint.AfterWalFlush);
        }
        catch (Exception exception)
        {
            if (prefixIntegrityRejected)
            {
                // Je conserve cet état fail-closed même si le contenu modifié est
                // encore syntaxiquement valide : il ne correspond plus au
                // préfixe durable que cette instance avait acquitté.
                _recoveryStatus = JusticeWalRecoveryStatus.Corrupt;
                _lastError = exception.Message;
            }
            else
            {
                _lastError = exception.GetType().Name + ": " + exception.Message;
                if (appendFileAcquired)
                {
                    // Je ne relis qu'après une erreur survenue une fois le fichier
                    // acquis par cet append. Un simple échec d'ouverture ne doit
                    // jamais faire adopter ni oublier un état externe concurrent.
                    ReloadAndRepairTailUnderExclusiveLease();
                }
            }
            throw;
        }
    }

    private void RegisterDurableRecord(
        JusticeWalRecord record,
        long durableLength,
        byte[] durablePrefixHash)
    {
        if (durablePrefixHash == null || durablePrefixHash.Length != HashLength)
        {
            throw new InvalidDataException(
                "L'empreinte du préfixe WAL acquitté est invalide.");
        }

        _records.Add(record);
        _latestByTransaction[record.TransactionId] = record;
        _lastSequence = record.Sequence;
        _walRevision = Math.Max(_walRevision, record.PersistenceRevision);
        _durableLength = durableLength;
        _durablePrefixHash = (byte[])durablePrefixHash.Clone();
        _durableFileExists = true;
        _recoveryStatus = JusticeWalRecoveryStatus.Clean;
        _lastError = string.Empty;
    }

    private void EnsureFileWasNotChangedExternally()
    {
        byte[] actualPrefixHash;
        if (!TryComputeCurrentDurablePrefixHash(
                _durableLength,
                _durableFileExists,
                out actualPrefixHash))
        {
            // Une instance vivante ne réacquiert jamais silencieusement un WAL
            // disparu, tronqué ou allongé. Les seules relectures réparatrices
            // restent le constructeur et le chemin d'erreur d'un append interne.
            RejectChangedDurablePrefix();
        }

        if (!BytesEqual(actualPrefixHash, _durablePrefixHash))
        {
            RejectChangedDurablePrefix();
        }
    }

    private void ReloadAndRepairTailUnderExclusiveLease()
    {
        JusticeWalRecoveryResult recovered = RecoverUnderExclusiveLease(_path);
        if (recovered.Status == JusticeWalRecoveryStatus.TruncatedTail)
        {
            RepairTruncatedTailUnderExclusiveLease(recovered.LastValidLength);
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
        _durableFileExists = WalFileExistsUnderExclusiveLease();

        long canonicalLength;
        _durablePrefixHash = ComputeCanonicalPrefixHash(
            _records,
            out canonicalLength);
        if (_recoveryStatus != JusticeWalRecoveryStatus.Corrupt &&
            canonicalLength != _durableLength)
        {
            _recoveryStatus = JusticeWalRecoveryStatus.Corrupt;
            _lastError =
                "Le préfixe WAL relu n'a pas une représentation canonique stable.";
        }
    }

    private bool TryComputeCurrentDurablePrefixHash(
        long expectedLength,
        bool expectedFileExists,
        out byte[] hash)
    {
        hash = null;
        try
        {
            using (FileStream stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan))
            {
                if (!expectedFileExists || stream.Length != expectedLength)
                {
                    return false;
                }

                hash = ComputeStreamSha256(stream, expectedLength);
                return true;
            }
        }
        catch (FileNotFoundException)
        {
            if (expectedFileExists || expectedLength != 0L)
            {
                return false;
            }

            hash = ComputeSha256(new byte[0]);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            if (expectedFileExists || expectedLength != 0L)
            {
                return false;
            }

            hash = ComputeSha256(new byte[0]);
            return true;
        }
    }

    private bool WalFileExistsUnderExclusiveLease()
    {
        try
        {
            using (new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan))
            {
                return true;
            }
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private void RejectChangedDurablePrefix()
    {
        _recoveryStatus = JusticeWalRecoveryStatus.Corrupt;
        _lastError =
            "Le préfixe durable du WAL a été modifié hors de cette instance.";
        throw new InvalidDataException(_lastError);
    }

    private void RepairTruncatedTailUnderExclusiveLease(long validLength)
    {
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

    private static IDisposable AcquireExclusiveLease(string path)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] identityHash = ComputeSha256(
            Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        string mutexName = ExclusiveLeaseNamePrefix +
                           BitConverter.ToString(identityHash).Replace("-", string.Empty);
        return new JusticeWalExclusiveLease(mutexName);
    }

    private sealed class JusticeWalExclusiveLease : IDisposable
    {
        private Mutex _mutex;
        private bool _ownsMutex;

        internal JusticeWalExclusiveLease(string mutexName)
        {
            _mutex = new Mutex(false, mutexName);
            try
            {
                try
                {
                    _ownsMutex = _mutex.WaitOne(
                        ExclusiveLeaseTimeoutMilliseconds,
                        false);
                }
                catch (AbandonedMutexException)
                {
                    // Je reprends un verrou abandonné : le contrôle intégral du WAL
                    // qui suit décidera si l'ancien processus a laissé une queue.
                    _ownsMutex = true;
                }

                if (!_ownsMutex)
                {
                    throw new IOException(
                        "Le WAL Justice est temporairement utilisé par une autre instance.");
                }
            }
            catch
            {
                _mutex.Dispose();
                _mutex = null;
                throw;
            }
        }

        public void Dispose()
        {
            Mutex mutex = _mutex;
            if (mutex == null)
            {
                return;
            }

            _mutex = null;
            try
            {
                if (_ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
            finally
            {
                _ownsMutex = false;
                mutex.Dispose();
            }
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

    private static byte[] ComputeStreamSha256(
        FileStream stream,
        long expectedLength)
    {
        if (stream == null)
        {
            throw new ArgumentNullException("stream");
        }
        if (expectedLength < 0L || stream.Length != expectedLength)
        {
            throw new IOException(
                "La longueur du préfixe WAL a changé avant son contrôle d'intégrité.");
        }

        stream.Position = 0L;
        byte[] hash;
        using (SHA256 algorithm = SHA256.Create())
        {
            // ComputeHash(Stream) travaille avec un tampon interne fixe : je ne
            // matérialise jamais le WAL complet en mémoire pour ce contrôle.
            hash = algorithm.ComputeHash(stream);
        }

        if (stream.Position != expectedLength || stream.Length != expectedLength)
        {
            throw new IOException(
                "Le préfixe WAL a changé pendant son contrôle d'intégrité.");
        }
        return hash;
    }

    private static byte[] ComputeCanonicalPrefixHash(
        IReadOnlyList<JusticeWalRecord> records,
        out long canonicalLength)
    {
        canonicalLength = 0L;
        byte[] hash;
        using (SHA256 algorithm = SHA256.Create())
        using (CryptoStream hashing = new CryptoStream(
            Stream.Null,
            algorithm,
            CryptoStreamMode.Write))
        {
            if (records != null)
            {
                for (int index = 0; index < records.Count; index++)
                {
                    byte[] frame = BuildFrame(SerializePayload(records[index]));
                    canonicalLength = checked(canonicalLength + frame.Length);
                    hashing.Write(frame, 0, frame.Length);
                }
            }

            hashing.FlushFinalBlock();
            hash = (byte[])algorithm.Hash.Clone();
        }
        return hash;
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
