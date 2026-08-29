using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal sealed class JusticeNoOpPersistenceFaultInjector : IJusticePersistenceFaultInjector
{
    internal static readonly JusticeNoOpPersistenceFaultInjector Instance =
        new JusticeNoOpPersistenceFaultInjector();

    private JusticeNoOpPersistenceFaultInjector()
    {
    }

    public void Probe(JusticePersistenceFaultPoint point)
    {
    }
}

internal sealed class JusticeAtomicFileStore : IJusticeAtomicFileStore
{
    public void WriteAtomically(
        string targetPath,
        string backupPath,
        byte[] document,
        IJusticePersistenceFaultInjector faultInjector)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Le chemin cible est obligatoire.", "targetPath");
        }

        if (document == null || document.Length == 0)
        {
            throw new ArgumentException("Le document a persister est vide.", "document");
        }

        IJusticePersistenceFaultInjector faults = faultInjector ??
            JusticeNoOpPersistenceFaultInjector.Instance;
        string target = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Le dossier cible est introuvable.");
        }

        Directory.CreateDirectory(directory);
        string backup = string.IsNullOrWhiteSpace(backupPath)
            ? null
            : Path.GetFullPath(backupPath);
        if (backup != null && string.Equals(target, backup, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Le backup doit etre distinct du fichier cible.",
                "backupPath");
        }

        string temp = Path.Combine(
            directory,
            Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            faults.Probe(JusticePersistenceFaultPoint.BeforeAtomicTempWrite);
            using (FileStream stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(document, 0, document.Length);
                faults.Probe(JusticePersistenceFaultPoint.BeforeAtomicTempFlush);
                stream.Flush(true);
                faults.Probe(JusticePersistenceFaultPoint.AfterAtomicTempFlush);
            }

            faults.Probe(JusticePersistenceFaultPoint.BeforeAtomicReplace);
            if (File.Exists(target))
            {
                // Je ne supprime jamais la cible si Replace echoue : l'ancien
                // snapshot reste l'autorite durable et le repository reessaiera.
                File.Replace(temp, target, backup, true);
            }
            else
            {
                File.Move(temp, target);
            }

            faults.Probe(JusticePersistenceFaultPoint.AfterAtomicReplace);
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
                    // Je ne masque jamais l'erreur de persistance principale
                    // pour un simple reliquat temporaire nettoyable au demarrage.
                }
            }
        }
    }

    public byte[] ReadAllBytes(string path)
    {
        return File.ReadAllBytes(Path.GetFullPath(path));
    }
}

// Je dedie ce writer au disque et au codec pur. Le thread GTA ne fait que
// capturer un snapshot profond, l'enfiler et lire les diagnostics de revision.
internal sealed class JusticeRepository : IDisposable
{
    private const int DefaultRetryDelayMs = 100;
    private const int DefaultDisposeTimeoutMs = 2000;

    private readonly object _gate = new object();
    private readonly AutoResetEvent _workSignal = new AutoResetEvent(false);
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly IJusticePersistenceCodec _codec;
    private readonly IJusticeAtomicFileStore _fileStore;
    private readonly IJusticePersistenceFaultInjector _faultInjector;
    private readonly int _retryDelayMs;

    private Thread _worker;
    private JusticeRepositoryState _state;
    private JusticePersistenceSnapshot _pending;
    private long _memoryRevision;
    private long _writingRevision;
    private long _diskRevision;
    private long _writeAttempts;
    private long _writeFailures;
    private string _lastError;
    private bool _disposed;

    internal JusticeRepository(
        string statePath,
        string backupPath,
        IJusticePersistenceCodec codec,
        long initialDiskRevision)
        : this(
            statePath,
            backupPath,
            codec,
            initialDiskRevision,
            new JusticeAtomicFileStore(),
            JusticeNoOpPersistenceFaultInjector.Instance,
            DefaultRetryDelayMs)
    {
    }

    internal JusticeRepository(
        string statePath,
        string backupPath,
        IJusticePersistenceCodec codec,
        long initialDiskRevision,
        IJusticeAtomicFileStore fileStore,
        IJusticePersistenceFaultInjector faultInjector,
        int retryDelayMs)
    {
        if (string.IsNullOrWhiteSpace(statePath))
        {
            throw new ArgumentException("Le chemin d'etat est obligatoire.", "statePath");
        }

        if (codec == null)
        {
            throw new ArgumentNullException("codec");
        }

        if (fileStore == null)
        {
            throw new ArgumentNullException("fileStore");
        }

        if (initialDiskRevision < 0L)
        {
            throw new ArgumentOutOfRangeException("initialDiskRevision");
        }

        if (retryDelayMs < 1)
        {
            throw new ArgumentOutOfRangeException("retryDelayMs");
        }

        _statePath = Path.GetFullPath(statePath);
        _backupPath = string.IsNullOrWhiteSpace(backupPath)
            ? _statePath + ".bak"
            : Path.GetFullPath(backupPath);
        _codec = codec;
        _fileStore = fileStore;
        _faultInjector = faultInjector ?? JusticeNoOpPersistenceFaultInjector.Instance;
        _retryDelayMs = retryDelayMs;
        _state = JusticeRepositoryState.Created;
        _memoryRevision = initialDiskRevision;
        _diskRevision = initialDiskRevision;
        _lastError = string.Empty;
    }

    internal void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state == JusticeRepositoryState.Created)
            {
                _worker = CreateWorker();
                _state = JusticeRepositoryState.Running;
                _worker.Start();
            }
            else if (_state != JusticeRepositoryState.Running)
            {
                throw new InvalidOperationException(
                    "Un repository Justice arrete ne peut pas etre redemarre.");
            }
        }
    }

    internal JusticeRepositoryEnqueueResult Enqueue(JusticePersistenceSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }

        JusticeRepositoryEnqueueResult result;
        lock (_gate)
        {
            if (_disposed || _state == JusticeRepositoryState.Stopping ||
                _state == JusticeRepositoryState.Stopped)
            {
                return JusticeRepositoryEnqueueResult.Stopped;
            }

            if (snapshot.Revision == _memoryRevision)
            {
                return JusticeRepositoryEnqueueResult.Duplicate;
            }

            if (snapshot.Revision < _memoryRevision)
            {
                return JusticeRepositoryEnqueueResult.Stale;
            }

            if (_state == JusticeRepositoryState.Created)
            {
                _worker = CreateWorker();
                _state = JusticeRepositoryState.Running;
                _worker.Start();
            }

            // Je ne garde qu'une case d'attente : pendant une ecriture, toute
            // nouvelle revision remplace la precedente sans perdre la plus recente.
            _pending = snapshot;
            _memoryRevision = snapshot.Revision;
            result = JusticeRepositoryEnqueueResult.Accepted;
            Monitor.PulseAll(_gate);
        }

        _workSignal.Set();
        return result;
    }

    internal bool Flush(long targetRevision, TimeSpan timeout)
    {
        if (targetRevision < 0L)
        {
            throw new ArgumentOutOfRangeException("targetRevision");
        }

        ValidateTimeout(timeout);
        Stopwatch stopwatch = Stopwatch.StartNew();
        lock (_gate)
        {
            if (targetRevision <= _diskRevision)
            {
                return true;
            }

            if (targetRevision > _memoryRevision || _state == JusticeRepositoryState.Created ||
                _state == JusticeRepositoryState.Stopped)
            {
                return false;
            }

            _workSignal.Set();
            while (targetRevision > _diskRevision)
            {
                if (_state == JusticeRepositoryState.Stopped)
                {
                    return false;
                }

                int remaining = RemainingMilliseconds(timeout, stopwatch);
                if (remaining == 0)
                {
                    return false;
                }

                Monitor.Wait(_gate, remaining);
            }

            return true;
        }
    }

    internal bool Stop(TimeSpan timeout)
    {
        ValidateTimeout(timeout);
        Thread worker;
        lock (_gate)
        {
            if (_state == JusticeRepositoryState.Created)
            {
                _state = JusticeRepositoryState.Stopped;
                Monitor.PulseAll(_gate);
                return _memoryRevision <= _diskRevision;
            }

            if (_state == JusticeRepositoryState.Stopped)
            {
                return _memoryRevision <= _diskRevision;
            }

            _state = JusticeRepositoryState.Stopping;
            worker = _worker;
            Monitor.PulseAll(_gate);
        }

        _workSignal.Set();
        int milliseconds = TimeoutMilliseconds(timeout);
        bool joined = worker == null || worker.Join(milliseconds);
        lock (_gate)
        {
            return joined && _state == JusticeRepositoryState.Stopped &&
                   _memoryRevision <= _diskRevision;
        }
    }

    internal JusticeRepositoryDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            return new JusticeRepositoryDiagnostics(
                _state,
                _memoryRevision,
                _pending == null ? 0L : _pending.Revision,
                _writingRevision,
                _diskRevision,
                _writeAttempts,
                _writeFailures,
                _lastError);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop(TimeSpan.FromMilliseconds(DefaultDisposeTimeoutMs));
    }

    private Thread CreateWorker()
    {
        return new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "DonJ Justice persistence"
        };
    }

    private void WriterLoop()
    {
        while (true)
        {
            JusticePersistenceSnapshot snapshot = null;
            lock (_gate)
            {
                if (_pending != null)
                {
                    snapshot = _pending;
                    _pending = null;
                    _writingRevision = snapshot.Revision;
                    _writeAttempts++;
                }
                else if (_state == JusticeRepositoryState.Stopping)
                {
                    _writingRevision = 0L;
                    _state = JusticeRepositoryState.Stopped;
                    Monitor.PulseAll(_gate);
                    return;
                }
            }

            if (snapshot == null)
            {
                _workSignal.WaitOne();
                continue;
            }

            string error;
            bool persisted = TryPersist(snapshot, out error);
            bool stopAfterFailure = false;
            lock (_gate)
            {
                _writingRevision = 0L;
                if (persisted)
                {
                    _diskRevision = Math.Max(_diskRevision, snapshot.Revision);
                    _lastError = string.Empty;
                }
                else
                {
                    _writeFailures++;
                    _lastError = error ?? "Echec de persistance Justice inconnu.";

                    if (_pending == null || _pending.Revision < snapshot.Revision)
                    {
                        _pending = snapshot;
                    }

                    if (_state == JusticeRepositoryState.Stopping)
                    {
                        _state = JusticeRepositoryState.Stopped;
                        stopAfterFailure = true;
                    }
                }

                Monitor.PulseAll(_gate);
            }

            if (stopAfterFailure)
            {
                return;
            }

            if (!persisted)
            {
                _workSignal.WaitOne(_retryDelayMs);
            }
        }
    }

    private bool TryPersist(JusticePersistenceSnapshot snapshot, out string error)
    {
        try
        {
            _faultInjector.Probe(JusticePersistenceFaultPoint.BeforeSnapshotSerialization);
            byte[] document = _codec.Serialize(snapshot);
            if (document == null || document.Length == 0)
            {
                throw new InvalidDataException("Le codec Justice a produit un document vide.");
            }

            _faultInjector.Probe(JusticePersistenceFaultPoint.AfterSnapshotSerialization);
            ValidateDocument(document, snapshot.Revision, "avant ecriture");
            _fileStore.WriteAtomically(
                _statePath,
                _backupPath,
                document,
                _faultInjector);

            byte[] persisted = _fileStore.ReadAllBytes(_statePath);
            if (!AreEqual(document, persisted))
            {
                throw new InvalidDataException(
                    "Le snapshot Justice relu ne correspond pas aux octets valides ecrits.");
            }

            ValidateDocument(persisted, snapshot.Revision, "apres relecture");
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private void ValidateDocument(byte[] document, long expectedRevision, string stage)
    {
        JusticePersistenceSnapshot decoded;
        string validationError;
        if (!_codec.TryDeserialize(document, out decoded, out validationError) || decoded == null)
        {
            throw new InvalidDataException(
                "Validation du snapshot Justice impossible " + stage + ": " +
                (validationError ?? "erreur inconnue"));
        }

        if (decoded.Revision != expectedRevision)
        {
            throw new InvalidDataException(
                "Revision Justice inattendue " + stage + ". Attendue=" +
                expectedRevision + ", lue=" + decoded.Revision + ".");
        }

        string semanticError;
        if (_codec is JusticeXmlPersistenceCodec &&
            !DonJEnemySpawner.TryValidateJusticePersistenceSnapshotSemantics(
                decoded,
                out semanticError))
        {
            throw new InvalidDataException(
                "Validation métier du snapshot Justice impossible " + stage +
                ": " + (semanticError ?? "erreur inconnue"));
        }
    }

    private static bool AreEqual(byte[] left, byte[] right)
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

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException("timeout");
        }
    }

    private static int RemainingMilliseconds(TimeSpan timeout, Stopwatch stopwatch)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.Infinite;
        }

        double remaining = timeout.TotalMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
        if (remaining <= 0D)
        {
            return 0;
        }

        return remaining >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(remaining));
    }

    private static int TimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return Timeout.Infinite;
        }

        return timeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)Math.Ceiling(timeout.TotalMilliseconds));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("JusticeRepository");
        }
    }
}
