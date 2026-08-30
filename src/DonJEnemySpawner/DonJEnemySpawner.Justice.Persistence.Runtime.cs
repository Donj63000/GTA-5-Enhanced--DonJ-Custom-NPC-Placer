using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

public sealed partial class DonJEnemySpawner
{
    private const int JusticePersistenceFlushTimeoutMs = 2500;
    private const int JusticePersistenceTestFlushTimeoutMs = 30000;
    private const int JusticePersistenceInitializationRetryMinimumMs = 1000;
    private const int JusticePersistenceInitializationRetryMaximumMs = 30000;
    private const string JusticeWalFileName = "_justice_state.wal";
    private const string JusticeV1MigrationBackupFileName = "_justice_state.v1.bak";

    private JusticeRepository _justiceRepository;
    private JusticeWriteAheadLog _justiceWriteAheadLog;
    private long _justicePersistenceRevision;
    private long _justiceLastQueuedPersistenceRevision;
    private long[] _justiceProfilePersistenceGenerations;
    private int _justiceLoadedSchemaMajor = JusticeStateVersion;
    private bool _justicePersistenceServicesUnavailable;
    private bool _justicePersistenceInitializationFailurePermanent;
    private int _justicePersistenceInitializationFailureCount;
    private long _justiceNextPersistenceInitializationRetryAtMs;
    private string _justicePersistenceLastError = string.Empty;
    private long _justiceLastPersistenceCompletedAtUtcTicks;
    private long _justiceObservedRepositoryWriteFailures;
    private long _justiceObservedRepositoryDiskRevision;
    private string _justiceV1MigrationSourcePath = string.Empty;
    private string _justiceCriticalBarrierCaller = string.Empty;
    private string _justiceCriticalBarrierOperationKind = string.Empty;
    private string _justiceCriticalBarrierTransactionId = string.Empty;
    private string _justiceCriticalBarrierIdentityKey = string.Empty;
    private long _justiceCriticalBarrierRevision;
    private long _justiceCriticalBarrierProfileGeneration;
    private long _justiceCriticalBarrierCreatedAtUtcTicks;
    private int _justiceCriticalBarrierProfileSlot = -1;
    private string _justiceFinancialBarrierOperationKind = string.Empty;
    private string _justiceFinancialBarrierTransactionId = string.Empty;
    private string _justiceFinancialBarrierIdentityKey = string.Empty;
    private long _justiceFinancialBarrierRevision;
    private long _justiceFinancialBarrierProfileGeneration;
    private int _justiceFinancialBarrierProfileSlot = -1;
    private List<JusticePersistenceField> _justiceFinancialBarrierFields;
    private IJusticePersistenceFaultInjector _justiceWalFaultInjectorOverride = null;
    private long _justiceWalCompactionProofSequence;
    private long _justiceWalCompactionProofDiskRevision;

    private void InitializeJusticePersistenceServices()
    {
        if (_justiceRepository != null)
        {
            return;
        }
        if (_justicePersistenceServicesUnavailable &&
            (_justicePersistenceInitializationFailurePermanent ||
             _justiceMonotonicTimeMs <
                _justiceNextPersistenceInitializationRetryAtMs))
        {
            return;
        }

        try
        {
            _justicePersistenceServicesUnavailable = false;
            EnsureJusticeProfilePersistenceGenerations();
            string directory = GetSaveDirectory();
            Directory.CreateDirectory(directory);
            string statePath = Path.Combine(directory, JusticeStateFileName);
            if (!string.IsNullOrWhiteSpace(_justiceV1MigrationSourcePath) &&
                File.Exists(_justiceV1MigrationSourcePath))
            {
                PreserveJusticeV1StateBeforeMigration(
                    _justiceV1MigrationSourcePath,
                    directory);
                _justiceV1MigrationSourcePath = string.Empty;
            }

            _justiceWriteAheadLog = new JusticeWriteAheadLog(
                Path.Combine(directory, JusticeWalFileName),
                _justiceWalFaultInjectorOverride ??
                    JusticeNoOpPersistenceFaultInjector.Instance);
            RecoverJusticePersistenceFromWalIfRequired();

            _justiceRepository = new JusticeRepository(
                statePath,
                statePath + ".bak",
                new JusticeXmlPersistenceCodec(),
                Math.Max(0L, _justicePersistenceRevision));
            _justiceRepository.Start();
            FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
            _justicePersistenceServicesUnavailable = false;
            _justicePersistenceInitializationFailurePermanent = false;
            _justicePersistenceInitializationFailureCount = 0;
            _justiceNextPersistenceInitializationRetryAtMs = 0L;
            _justicePersistenceLastError = string.Empty;
        }
        catch (Exception exception)
        {
            JusticeRepository failedRepository = _justiceRepository;
            _justiceRepository = null;
            if (failedRepository != null)
            {
                try
                {
                    failedRepository.Stop(TimeSpan.FromMilliseconds(
                        JusticePersistenceFlushTimeoutMs));
                    failedRepository.Dispose();
                }
                catch
                {
                    // Je conserve l'erreur d'initialisation d'origine. Le nettoyage
                    // défensif d'un writer partiellement créé ne doit pas la masquer.
                }
            }
            _justiceWriteAheadLog = null;
            _justicePersistenceServicesUnavailable = true;
            _justicePersistenceInitializationFailurePermanent =
                exception is InvalidDataException || exception is XmlException;
            _justicePersistenceInitializationFailureCount = Math.Min(
                30,
                _justicePersistenceInitializationFailureCount + 1);
            int exponent = Math.Min(
                5,
                Math.Max(0, _justicePersistenceInitializationFailureCount - 1));
            long retryDelay = Math.Min(
                JusticePersistenceInitializationRetryMaximumMs,
                (long)JusticePersistenceInitializationRetryMinimumMs << exponent);
            _justiceNextPersistenceInitializationRetryAtMs =
                _justiceMonotonicTimeMs >= long.MaxValue - retryDelay
                    ? long.MaxValue
                    : _justiceMonotonicTimeMs + retryDelay;
            _justicePersistenceLastError = exception.GetType().Name + ": " + exception.Message;
            LogException("Justice.Repository.Initialisation", exception);
        }
    }

    private void PreserveJusticeV1StateBeforeMigration(string sourcePath, string directory)
    {
        string migrationBackup = Path.Combine(directory, JusticeV1MigrationBackupFileName);
        byte[] sourceDocument = File.ReadAllBytes(Path.GetFullPath(sourcePath));
        if (sourceDocument.Length == 0 ||
            sourceDocument.LongLength > JusticeStateMaximumFileBytes)
        {
            throw new InvalidDataException(
                "L'original Justice v1 à préserver est vide ou dépasse la limite autorisée.");
        }

        if (File.Exists(migrationBackup))
        {
            byte[] existingBackup = File.ReadAllBytes(migrationBackup);
            if (!AreJusticePersistenceBytesEqual(sourceDocument, existingBackup))
            {
                throw new InvalidDataException(
                    "Le backup de migration Justice v1 existant ne correspond pas à l'original.");
            }
            return;
        }

        // Je passe par le même contrat temporaire, WriteThrough et Flush(true)
        // que le repository avant de publier le backup v1 sous son nom final.
        IJusticeAtomicFileStore fileStore = new JusticeAtomicFileStore();
        fileStore.WriteAtomically(
            migrationBackup,
            null,
            sourceDocument,
            JusticeNoOpPersistenceFaultInjector.Instance);

        byte[] persistedBackup = fileStore.ReadAllBytes(migrationBackup);
        if (!AreJusticePersistenceBytesEqual(sourceDocument, persistedBackup) ||
            !string.Equals(
                JusticeXmlPersistenceCodec.ComputeSha256Hex(sourceDocument),
                JusticeXmlPersistenceCodec.ComputeSha256Hex(persistedBackup),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Le backup de migration Justice v1 ne correspond pas à l'original.");
        }
    }

    private static bool AreJusticePersistenceBytesEqual(byte[] left, byte[] right)
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

    private void ShutdownJusticePersistenceServices()
    {
        JusticeRepository repository = _justiceRepository;
        if (repository == null)
        {
            return;
        }

        bool stopped = repository.Stop(
            TimeSpan.FromMilliseconds(JusticePersistenceFlushTimeoutMs));
        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        if (!stopped)
        {
            LogWarning(
                "Justice.Repository.Arret",
                "Le writer n'a pas confirmé sa dernière révision avant le délai borné.");
        }
        repository.Dispose();
        _justiceRepository = null;
    }

    private void QueueJusticeStateCheckpoint()
    {
        bool retryWasDue = _justiceNextStateFlushAttemptAtMs <= 0L ||
            _justiceMonotonicTimeMs >= _justiceNextStateFlushAttemptAtMs;
        ObserveJusticeRepositoryFailure();
        if (!retryWasDue || _justiceCriticalBarrierRevision > 0L)
        {
            return;
        }
        JusticePersistenceSnapshot snapshot;
        if (!TryCaptureJusticePersistenceSnapshot(out snapshot))
        {
            RegisterJusticePersistenceFailure("capture du checkpoint impossible");
            return;
        }

        if (!TryEnqueueJusticeSnapshot(snapshot, false))
        {
            RegisterJusticePersistenceFailure("checkpoint refusé par le repository");
            return;
        }
        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
    }

    private bool JusticeFlushStateNow()
    {
        bool retryWasDue = _justiceNextStateFlushAttemptAtMs <= 0L ||
            _justiceMonotonicTimeMs >= _justiceNextStateFlushAttemptAtMs;
        ObserveJusticeRepositoryFailure();
        if (!retryWasDue || _justiceCriticalBarrierRevision > 0L)
        {
            return false;
        }

        if (ShouldForceJusticePersistenceFailureForTest())
        {
            RegisterJusticePersistenceFailure("échec injecté par le test");
            return false;
        }

        JusticePersistenceSnapshot snapshot;
        if (!TryCaptureJusticePersistenceSnapshot(out snapshot))
        {
            RegisterJusticePersistenceFailure("capture durable impossible");
            return false;
        }

        // Le thread GTA ne fait qu'enfiler le DTO immuable. Le writer sérialise,
        // valide, flush et remplace le fichier sur son thread dédié.
        if (!TryEnqueueJusticeSnapshot(snapshot, false))
        {
            RegisterJusticePersistenceFailure("snapshot refusé par le repository");
            return false;
        }

        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        return true;
    }

    // Cette barrière n'est utilisée que par les tests hors jeu. En production,
    // seul Stop() attend de façon bornée pendant OnAborted.
    private bool JusticeAwaitQueuedPersistenceForTests()
    {
        JusticeRepository repository = _justiceRepository;
        long revision = _justiceLastQueuedPersistenceRevision;
        bool persisted = repository != null && revision > 0L &&
            repository.Flush(
                revision,
                TimeSpan.FromMilliseconds(JusticePersistenceTestFlushTimeoutMs));
        ObserveJusticeRepositoryFailure();
        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        return persisted;
    }

    private bool ObserveJusticeRepositoryFailure()
    {
        JusticeRepository repository = _justiceRepository;
        if (repository == null)
        {
            return false;
        }

        JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
        if (diagnostics.DiskRevision > _justiceObservedRepositoryDiskRevision)
        {
            _justiceObservedRepositoryDiskRevision = diagnostics.DiskRevision;
            _justiceLastPersistenceCompletedAtUtcTicks = DateTime.UtcNow.Ticks;
        }
        if (diagnostics.WriteFailures <= _justiceObservedRepositoryWriteFailures)
        {
            return false;
        }

        _justiceObservedRepositoryWriteFailures = diagnostics.WriteFailures;
        if (diagnostics.IsCaughtUp &&
            diagnostics.DiskRevision >= _justiceLastQueuedPersistenceRevision &&
            string.IsNullOrWhiteSpace(diagnostics.LastError))
        {
            // Je peux observer tardivement les échecs d'une ancienne révision
            // après que sa remplaçante valide a déjà atteint le disque. Dans ce
            // cas le repository est revenu à un état sain : je n'arme pas un
            // nouveau délai de retry contre une panne désormais acquittée.
            return false;
        }

        RegisterJusticePersistenceFailure(
            "writer asynchrone: " +
            (string.IsNullOrWhiteSpace(diagnostics.LastError)
                ? "échec disque ou validation"
                : diagnostics.LastError));
        return true;
    }

    private bool PersistJusticeCriticalPrecommitToWal(string caller)
    {
        InitializeJusticePersistenceServices();
        if (_justiceRepository == null || _justiceWriteAheadLog == null ||
            _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        string normalizedCaller = NormalizeJusticeCriticalBarrierCaller(caller);
        if (_justiceCriticalBarrierRevision > 0L)
        {
            if (!string.Equals(
                    _justiceCriticalBarrierCaller,
                    normalizedCaller,
                    StringComparison.Ordinal))
            {
                // Je n'autorise qu'une frontière irréversible à la fois. Le tick
                // suivant reprendra d'abord l'opération dont le snapshot est déjà
                // en cours de validation sur le writer.
                return false;
            }

            return TryCommitJusticeCriticalBarrierToWal();
        }

        if (normalizedCaller == "FineDebit" ||
            normalizedCaller == "VoluntaryFinePayment")
        {
            return PersistJusticeFinancialPrecommitToWal(normalizedCaller);
        }

        if (ShouldForceJusticePersistenceFailureForTest())
        {
            RegisterJusticePersistenceFailure("échec WAL injecté par le test");
            return false;
        }

        JusticePersistenceSnapshot snapshot;
        if (!TryCaptureJusticePersistenceSnapshot(out snapshot))
        {
            string captureError = _justicePersistenceLastError;
            RegisterJusticePersistenceFailure(
                "capture du précommit impossible" +
                (string.IsNullOrWhiteSpace(captureError)
                    ? string.Empty
                    : " : " + captureError));
            return false;
        }

        JusticePersistenceProfileSnapshot active = FindJusticePersistenceProfile(
            snapshot,
            snapshot.ActiveProfileSlot);
        if (active == null)
        {
            RegisterJusticePersistenceFailure("profil actif absent du précommit");
            return false;
        }

        if (!TryEnqueueJusticeSnapshot(snapshot, false))
        {
            RegisterJusticePersistenceFailure("snapshot critique refusé par le repository");
            return false;
        }

        _justiceCriticalBarrierCaller = normalizedCaller;
        _justiceCriticalBarrierOperationKind =
            GetJusticeCriticalOperationKind(normalizedCaller);
        _justiceCriticalBarrierRevision = snapshot.Revision;
        _justiceCriticalBarrierProfileGeneration = active.Generation;
        _justiceCriticalBarrierProfileSlot = snapshot.ActiveProfileSlot;
        _justiceCriticalBarrierIdentityKey = active.IdentityKey ?? string.Empty;
        _justiceCriticalBarrierCreatedAtUtcTicks = DateTime.UtcNow.Ticks;
        _justiceCriticalBarrierTransactionId = CreateJusticeCriticalTransactionId(
            normalizedCaller,
            snapshot.ActiveProfileSlot,
            snapshot.Revision);

        // Je ne bloque jamais le thread GTA. Même si le writer termine très vite,
        // je ne franchis la frontière qu'après une lecture non bloquante de sa
        // révision disque.
        return TryCommitJusticeCriticalBarrierToWal();
    }

    private bool PersistJusticeFinancialPrecommitToWal(string operationKind)
    {
        return EnsureJusticeFinancialPreparedSnapshot(operationKind);
    }

    private bool EnsureJusticeFinancialPreparedSnapshot(string operationKind)
    {
        if (ShouldForceJusticePersistenceFailureForTest())
        {
            RegisterJusticePersistenceFailure("échec du snapshot financier injecté par le test");
            return false;
        }
        InitializeJusticePersistenceServices();
        if (_justiceRepository == null || _justiceWriteAheadLog == null ||
            _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        string transactionId = CreateJusticeFinancialTransactionId(operationKind);
        int profileSlot = GetJusticeFinancialIntentSlot(operationKind);
        if (string.IsNullOrWhiteSpace(transactionId) ||
            !IsJusticeCanonicalProfileSlot(profileSlot) ||
            profileSlot != _justiceActivePlayerProfileSlot)
        {
            RegisterJusticePersistenceFailure("identité financière invalide avant snapshot");
            return false;
        }

        JusticeWalRecord terminal = _justiceWriteAheadLog.GetLatest(transactionId);
        if (terminal != null && terminal.State == JusticeWalState.Rejected)
        {
            ApplyJusticeRejectedFinancialWalToCurrentIntent(operationKind);
            ClearJusticeFinancialBarrier();
            return true;
        }
        if (terminal != null && terminal.State == JusticeWalState.Confirmed)
        {
            ApplyJusticeConfirmedFinancialWalToCurrentIntent(
                operationKind,
                terminal.CreatedAtUtcTicks);
            ClearJusticeFinancialBarrier();
            return true;
        }

        if (_justiceFinancialBarrierRevision > 0L)
        {
            if (!string.Equals(
                    _justiceFinancialBarrierOperationKind,
                    operationKind,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _justiceFinancialBarrierTransactionId,
                    transactionId,
                    StringComparison.Ordinal) ||
                _justiceFinancialBarrierProfileSlot != profileSlot)
            {
                return false;
            }

            JusticeRepositoryDiagnostics pendingDiagnostics =
                _justiceRepository.GetDiagnostics();
            return pendingDiagnostics.DiskRevision >=
                   _justiceFinancialBarrierRevision;
        }

        JusticePersistenceSnapshot snapshot;
        if (!TryCaptureJusticePersistenceSnapshot(out snapshot))
        {
            RegisterJusticePersistenceFailure("capture du snapshot financier impossible");
            return false;
        }

        JusticePersistenceProfileSnapshot active = FindJusticePersistenceProfile(
            snapshot,
            snapshot.ActiveProfileSlot);
        if (active == null || snapshot.ActiveProfileSlot != profileSlot)
        {
            RegisterJusticePersistenceFailure("profil financier absent du snapshot");
            return false;
        }
        List<JusticePersistenceField> immutableFields =
            CreateJusticeFinancialWalFields(
                operationKind,
                active.Generation,
                active.IdentityKey ?? string.Empty);
        if (immutableFields == null)
        {
            RegisterJusticePersistenceFailure("plan financier absent du snapshot");
            return false;
        }
        if (!TryEnqueueJusticeSnapshot(snapshot, false))
        {
            RegisterJusticePersistenceFailure("snapshot financier refusé par le repository");
            return false;
        }

        _justiceFinancialBarrierOperationKind = operationKind;
        _justiceFinancialBarrierTransactionId = transactionId;
        _justiceFinancialBarrierRevision = snapshot.Revision;
        _justiceFinancialBarrierProfileGeneration = active.Generation;
        _justiceFinancialBarrierProfileSlot = snapshot.ActiveProfileSlot;
        _justiceFinancialBarrierIdentityKey = active.IdentityKey ?? string.Empty;
        _justiceFinancialBarrierFields =
            new List<JusticePersistenceField>(immutableFields);

        // Je rends toujours la main après le premier enqueue. Même si le writer a
        // fini dans ce tick, l'effet cash ne franchira la barrière qu'à la reprise.
        return false;
    }

    private bool PersistJusticeFinancialOutcomeWithoutEffect(string operationKind)
    {
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        string transactionId = CreateJusticeFinancialTransactionId(operationKind);
        if (string.IsNullOrWhiteSpace(transactionId) ||
            _justiceWriteAheadLog == null)
        {
            ClearJusticeFinancialBarrier();
            return true;
        }

        JusticeWalRecord durable = _justiceWriteAheadLog.GetLatest(transactionId);
        if (durable != null && durable.State == JusticeWalState.Rejected)
        {
            ClearJusticeFinancialBarrier();
            return true;
        }
        if (durable != null &&
            (durable.State != JusticeWalState.Prepared ||
             !IsJusticeFinancialWalRecordForCurrentIntent(durable, operationKind) ||
             !DoJusticeFinancialWalFieldsMatchBarrier(durable.Fields))
            )
        {
            return false;
        }

        long resultRevision = Math.Max(
            durable == null
                ? _justiceFinancialBarrierRevision
                : durable.PersistenceRevision,
            _justiceLastQueuedPersistenceRevision);
        try
        {
            if (durable == null)
            {
                List<JusticePersistenceField> fields =
                    _justiceFinancialBarrierFields == null
                        ? null
                        : new List<JusticePersistenceField>(
                            _justiceFinancialBarrierFields);
                if (fields == null || _justiceFinancialBarrierRevision <= 0L)
                {
                    return false;
                }
                durable = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    transactionId,
                    operationKind,
                    GetJusticeFinancialIntentSlot(operationKind),
                    JusticeWalState.Prepared,
                    _justiceFinancialBarrierRevision,
                    GetJusticeFinancialPreparedAtUtcTicks(operationKind),
                    fields));
            }
            _justiceWriteAheadLog.Append(new JusticeWalRecord(
                durable.TransactionId,
                durable.OperationKind,
                durable.ProfileSlot,
                JusticeWalState.Rejected,
                resultRevision,
                durable.CreatedAtUtcTicks,
                durable.Fields));
            ClearJusticeFinancialBarrier();
            return true;
        }
        catch (Exception exception)
        {
            JusticeWalRecord afterFailure =
                _justiceWriteAheadLog.GetLatest(transactionId);
            if (afterFailure != null &&
                afterFailure.State == JusticeWalState.Rejected)
            {
                ClearJusticeFinancialBarrier();
                return true;
            }
            RegisterJusticePersistenceFailure(
                "rejet WAL financier refusé: " + exception.GetType().Name);
            LogException("Justice.WAL.RejetFinance", exception);
            return false;
        }
    }

    private bool TryArmJusticeFinancialAttempt(
        string operationKind,
        out bool attemptWasAlreadyDurable)
    {
        attemptWasAlreadyDurable = false;
        if (_justiceRepository == null || _justiceWriteAheadLog == null ||
            _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        string transactionId = CreateJusticeFinancialTransactionId(operationKind);
        int profileSlot = GetJusticeFinancialIntentSlot(operationKind);
        if (_justiceFinancialBarrierRevision <= 0L ||
            !string.Equals(
                _justiceFinancialBarrierOperationKind,
                operationKind,
                StringComparison.Ordinal) ||
            !string.Equals(
                _justiceFinancialBarrierTransactionId,
                transactionId,
                StringComparison.Ordinal) ||
            _justiceFinancialBarrierProfileSlot != profileSlot ||
            _justiceRepository.GetDiagnostics().DiskRevision <
                _justiceFinancialBarrierRevision)
        {
            return false;
        }

        JusticeWalRecord durableBefore = _justiceWriteAheadLog.GetLatest(transactionId);
        if (durableBefore != null)
        {
            if (!IsJusticeFinancialWalRecordForCurrentIntent(
                    durableBefore,
                    operationKind) ||
                !DoJusticeFinancialWalFieldsMatchBarrier(durableBefore.Fields))
            {
                RegisterJusticePersistenceFailure(
                    "le plan financier ne correspond plus au WAL Prepared");
                return false;
            }
            if (durableBefore.State == JusticeWalState.Attempted ||
                durableBefore.State == JusticeWalState.Ambiguous)
            {
                attemptWasAlreadyDurable = true;
                ClearJusticeFinancialBarrier();
                return true;
            }
            if (durableBefore.State != JusticeWalState.Prepared)
            {
                return false;
            }
        }

        List<JusticePersistenceField> fields = durableBefore == null
            ? (_justiceFinancialBarrierFields == null
                ? null
                : new List<JusticePersistenceField>(_justiceFinancialBarrierFields))
            : new List<JusticePersistenceField>(durableBefore.Fields);
        if (fields == null)
        {
            RegisterJusticePersistenceFailure("intention financière absente du WAL");
            return false;
        }

        long createdAtUtcTicks = durableBefore == null
            ? GetJusticeFinancialPreparedAtUtcTicks(operationKind)
            : durableBefore.CreatedAtUtcTicks;
        long persistenceRevision = durableBefore == null
            ? _justiceFinancialBarrierRevision
            : Math.Max(
                durableBefore.PersistenceRevision,
                _justiceFinancialBarrierRevision);
        JusticeWalRecord prepared = durableBefore ?? new JusticeWalRecord(
            transactionId,
            operationKind,
            profileSlot,
            JusticeWalState.Prepared,
            persistenceRevision,
            createdAtUtcTicks,
            fields);
        try
        {
            if (durableBefore == null)
            {
                _justiceWriteAheadLog.Append(prepared);
            }
            _justiceWriteAheadLog.Append(new JusticeWalRecord(
                transactionId,
                operationKind,
                profileSlot,
                JusticeWalState.Attempted,
                persistenceRevision,
                createdAtUtcTicks,
                fields));
            ClearJusticeFinancialBarrier();
            return true;
        }
        catch (Exception exception)
        {
            JusticeWalRecord durable = _justiceWriteAheadLog.GetLatest(transactionId);
            if (durable != null && durable.State == JusticeWalState.Attempted)
            {
                // Cette invocation vient elle-même de durcir Attempted : elle est
                // la seule autorisée à appeler SET après une perte d'ACK.
                ClearJusticeFinancialBarrier();
                return true;
            }
            RegisterJusticePersistenceFailure(
                "WAL financier refusé: " + exception.GetType().Name);
            LogException("Justice.WAL.Finance", exception);
            return false;
        }
    }

    private void InvalidateJusticeFinancialPreparedSnapshot(string operationKind)
    {
        if (_justiceFinancialBarrierRevision > 0L &&
            string.Equals(
                _justiceFinancialBarrierOperationKind,
                operationKind,
                StringComparison.Ordinal))
        {
            ClearJusticeFinancialBarrier();
        }
    }

    private bool HasJusticePreparedFinancialWal(string operationKind)
    {
        if (_justiceWriteAheadLog == null)
        {
            return false;
        }

        string transactionId = CreateJusticeFinancialTransactionId(operationKind);
        JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(transactionId);
        return latest != null && latest.State == JusticeWalState.Prepared;
    }

    private void ClearJusticeFinancialBarrier()
    {
        _justiceFinancialBarrierOperationKind = string.Empty;
        _justiceFinancialBarrierTransactionId = string.Empty;
        _justiceFinancialBarrierIdentityKey = string.Empty;
        _justiceFinancialBarrierRevision = 0L;
        _justiceFinancialBarrierProfileGeneration = 0L;
        _justiceFinancialBarrierProfileSlot = -1;
        _justiceFinancialBarrierFields = null;
    }

    private bool DoJusticeFinancialWalFieldsMatchBarrier(
        IReadOnlyList<JusticePersistenceField> fields)
    {
        if (fields == null || _justiceFinancialBarrierFields == null ||
            fields.Count != _justiceFinancialBarrierFields.Count)
        {
            return false;
        }
        for (int index = 0; index < fields.Count; index++)
        {
            if (!string.Equals(
                    fields[index].Path,
                    _justiceFinancialBarrierFields[index].Path,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    fields[index].Value,
                    _justiceFinancialBarrierFields[index].Value,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private void ApplyJusticeRejectedFinancialWalToCurrentIntent(
        string operationKind)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            _justiceVoluntaryFinePaymentIntent.DebitAttempted = false;
            _justiceVoluntaryFinePaymentIntent.AttemptedAtUtcTicks = 0L;
            _justiceVoluntaryFinePaymentIntent.CashWriteResult =
                JusticeCashWriteResult.Rejected;
            _justiceVoluntaryFinePaymentIntent.Resolution =
                JusticePaymentResolution.Rejected;
            return;
        }
        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            _justiceFineDebitIntent.DebitAttempted = false;
            _justiceFineDebitIntent.AttemptedAtUtcTicks = 0L;
            _justiceFineDebitIntent.CashWriteResult =
                JusticeCashWriteResult.Rejected;
            _justiceFineDebitIntent.Resolution =
                JusticePaymentResolution.Rejected;
        }
    }

    private void ApplyJusticeConfirmedFinancialWalToCurrentIntent(
        string operationKind,
        long attemptedAtUtcTicks)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null &&
            !_justiceVoluntaryFinePaymentIntent.DebitAttempted)
        {
            _justiceVoluntaryFinePaymentIntent.DebitAttempted = true;
            _justiceVoluntaryFinePaymentIntent.AttemptedAtUtcTicks =
                Math.Max(1L, attemptedAtUtcTicks);
            _justiceVoluntaryFinePaymentIntent.CashWriteResult =
                JusticeCashWriteResult.Unknown;
            _justiceVoluntaryFinePaymentIntent.Resolution =
                JusticePaymentResolution.Attempted;
            return;
        }
        if (operationKind == "FineDebit" &&
            _justiceFineDebitIntent != null &&
            !_justiceFineDebitIntent.DebitAttempted)
        {
            _justiceFineDebitIntent.DebitAttempted = true;
            _justiceFineDebitIntent.AttemptedAtUtcTicks =
                Math.Max(1L, attemptedAtUtcTicks);
            _justiceFineDebitIntent.CashWriteResult =
                JusticeCashWriteResult.Unknown;
            _justiceFineDebitIntent.Resolution =
                JusticePaymentResolution.Attempted;
        }
    }

    private List<JusticePersistenceField> CreateJusticeFinancialWalFields(
        string operationKind,
        long profileGeneration,
        string identityKey)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            JusticeVoluntaryFinePaymentIntent intent =
                _justiceVoluntaryFinePaymentIntent;
            string caseEpisode = string.IsNullOrWhiteSpace(
                _justiceCaseState == null ? string.Empty : _justiceCaseState.CustodyEpisodeId)
                    ? (_justiceCaseState == null
                        ? string.Empty
                        : _justiceCaseState.WantedEpisodeId ?? string.Empty)
                    : _justiceCaseState.CustodyEpisodeId;
            return new List<JusticePersistenceField>(13)
            {
                WalField("paymentId", intent.PaymentId),
                WalField("slot", intent.Slot),
                WalField("fineBefore", intent.FineBefore),
                WalField(
                    "paidBefore",
                    _justiceCaseState == null
                        ? 0L
                        : Math.Max(0L, _justiceCaseState.VoluntaryFinePaid)),
                WalField("debitAmount", intent.DebitAmount),
                WalField("cashBefore", intent.CashBefore),
                WalField("cashAfter", intent.CashAfter),
                WalField("disputeBefore", intent.FineInDisputeBefore),
                WalField("preparedAt", intent.PreparedAtUtcTicks),
                WalField("caseEpisode", caseEpisode),
                WalField("profileGeneration", profileGeneration),
                WalField("identityKey", identityKey),
                WalField("schemaMajor", JusticeXmlPersistenceCodec.SchemaMajor)
            };
        }
        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            JusticeFineDebitIntent intent = _justiceFineDebitIntent;
            return new List<JusticePersistenceField>(17)
            {
                WalField("episodeId", intent.EpisodeId),
                WalField("slot", intent.Slot),
                WalField("fineAmount", intent.FineAmount),
                WalField("cashPlan", intent.CashPlanPrepared),
                WalField("preparedAt", intent.PreparedAtUtcTicks),
                WalField("debitAmount", intent.DebitAmount),
                WalField("cashBefore", intent.CashBefore),
                WalField("cashAfter", intent.CashAfter),
                WalField("sentenceDebited", intent.SentenceIfDebited),
                WalField("sentenceConverted", intent.SentenceIfConverted),
                WalField("stationPlanned", intent.StationPlanned),
                WalField("disputeBefore", intent.FineInDisputeBefore),
                WalField(
                    "sentenceBefore",
                    _justiceCaseState == null
                        ? 0
                        : Math.Max(0, _justiceCaseState.SentenceSeconds)),
                WalField(
                    "custodyEpisode",
                    _justiceCaseState == null
                        ? string.Empty
                        : _justiceCaseState.CustodyEpisodeId ?? string.Empty),
                WalField("profileGeneration", profileGeneration),
                WalField("identityKey", identityKey),
                WalField("schemaMajor", JusticeXmlPersistenceCodec.SchemaMajor)
            };
        }
        return null;
    }

    private static JusticePersistenceField WalField(string path, string value)
    {
        return new JusticePersistenceField(path, value ?? string.Empty);
    }

    private static JusticePersistenceField WalField(string path, bool value)
    {
        return new JusticePersistenceField(path, value ? "true" : "false");
    }

    private static JusticePersistenceField WalField(string path, int value)
    {
        return new JusticePersistenceField(
            path,
            value.ToString(CultureInfo.InvariantCulture));
    }

    private static JusticePersistenceField WalField(string path, long value)
    {
        return new JusticePersistenceField(
            path,
            value.ToString(CultureInfo.InvariantCulture));
    }

    private string CreateJusticeFinancialTransactionId(string operationKind)
    {
        string operationIdentity = GetJusticeFinancialOperationIdentity(operationKind);
        int slot = GetJusticeFinancialIntentSlot(operationKind);
        if (string.IsNullOrWhiteSpace(operationIdentity) ||
            operationIdentity.Length > 128 ||
            !IsJusticeCanonicalProfileSlot(slot))
        {
            return string.Empty;
        }

        return "financial:" + slot.ToString(CultureInfo.InvariantCulture) + ":" +
               operationKind + ":" + operationIdentity.Trim();
    }

    private string GetJusticeFinancialOperationIdentity(string operationKind)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            return _justiceVoluntaryFinePaymentIntent.PaymentId ?? string.Empty;
        }
        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            return _justiceFineDebitIntent.EpisodeId ?? string.Empty;
        }
        return string.Empty;
    }

    private int GetJusticeFinancialIntentSlot(string operationKind)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            return _justiceVoluntaryFinePaymentIntent.Slot;
        }
        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            return _justiceFineDebitIntent.Slot;
        }
        return -1;
    }

    private long GetJusticeFinancialPreparedAtUtcTicks(string operationKind)
    {
        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            return Math.Max(
                1L,
                _justiceVoluntaryFinePaymentIntent.PreparedAtUtcTicks);
        }
        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            return Math.Max(1L, _justiceFineDebitIntent.PreparedAtUtcTicks);
        }
        return Math.Max(1L, DateTime.UtcNow.Ticks);
    }

    private bool IsJusticeFinancialWalRecordForCurrentIntent(
        JusticeWalRecord record,
        string operationKind)
    {
        if (record == null ||
            !string.Equals(record.OperationKind, operationKind, StringComparison.Ordinal) ||
            record.ProfileSlot != GetJusticeFinancialIntentSlot(operationKind) ||
            ReadWalInt(record, "schemaMajor", -1) !=
                JusticeXmlPersistenceCodec.SchemaMajor)
        {
            return false;
        }

        if (operationKind == "VoluntaryFinePayment" &&
            _justiceVoluntaryFinePaymentIntent != null)
        {
            JusticeVoluntaryFinePaymentIntent intent =
                _justiceVoluntaryFinePaymentIntent;
            return string.Equals(
                       ReadWalString(record, "paymentId", string.Empty),
                       intent.PaymentId ?? string.Empty,
                       StringComparison.Ordinal) &&
                   ReadWalLong(record, "fineBefore", -1L) == intent.FineBefore &&
                   ReadWalInt(record, "debitAmount", -1) == intent.DebitAmount &&
                   ReadWalInt(record, "cashBefore", -1) == intent.CashBefore &&
                   ReadWalInt(record, "cashAfter", -1) == intent.CashAfter &&
                   ReadWalLong(record, "disputeBefore", -1L) ==
                       intent.FineInDisputeBefore &&
                   ReadWalLong(record, "preparedAt", -1L) ==
                       intent.PreparedAtUtcTicks;
        }

        if (operationKind == "FineDebit" && _justiceFineDebitIntent != null)
        {
            JusticeFineDebitIntent intent = _justiceFineDebitIntent;
            return string.Equals(
                       ReadWalString(record, "episodeId", string.Empty),
                       intent.EpisodeId ?? string.Empty,
                       StringComparison.Ordinal) &&
                   ReadWalLong(record, "fineAmount", -1L) == intent.FineAmount &&
                   ReadWalBool(record, "cashPlan", !intent.CashPlanPrepared) ==
                       intent.CashPlanPrepared &&
                   ReadWalInt(record, "debitAmount", -1) == intent.DebitAmount &&
                   ReadWalInt(record, "cashBefore", -1) == intent.CashBefore &&
                   ReadWalInt(record, "cashAfter", -1) == intent.CashAfter &&
                   ReadWalInt(record, "sentenceDebited", -1) ==
                       intent.SentenceIfDebited &&
                   ReadWalInt(record, "sentenceConverted", -1) ==
                       intent.SentenceIfConverted &&
                   ReadWalBool(record, "stationPlanned", !intent.StationPlanned) ==
                       intent.StationPlanned &&
                   ReadWalLong(record, "disputeBefore", -1L) ==
                       intent.FineInDisputeBefore &&
                   ReadWalLong(record, "preparedAt", -1L) ==
                       intent.PreparedAtUtcTicks;
        }

        return false;
    }

    private bool TryCommitJusticeCriticalBarrierToWal()
    {
        if (_justiceRepository == null || _justiceWriteAheadLog == null ||
            _justiceCriticalBarrierRevision <= 0L)
        {
            return false;
        }

        JusticeRepositoryDiagnostics diagnostics = _justiceRepository.GetDiagnostics();
        if (diagnostics.DiskRevision < _justiceCriticalBarrierRevision)
        {
            return false;
        }

        List<JusticePersistenceField> walFields = CreateJusticeWalRecoveryFields(
            _justiceCriticalBarrierRevision,
            _justiceCriticalBarrierProfileGeneration,
            _justiceCriticalBarrierIdentityKey,
            _justiceCriticalBarrierCaller);
        JusticeWalRecord prepared = new JusticeWalRecord(
            _justiceCriticalBarrierTransactionId,
            _justiceCriticalBarrierOperationKind,
            _justiceCriticalBarrierProfileSlot,
            JusticeWalState.Prepared,
            _justiceCriticalBarrierRevision,
            _justiceCriticalBarrierCreatedAtUtcTicks,
            walFields);

        try
        {
            JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                prepared.TransactionId);
            if (latest != null &&
                !IsJusticeCriticalBarrierWalRecordExact(latest))
            {
                throw new InvalidDataException(
                    "La frontière WAL durable ne correspond plus à sa barrière runtime.");
            }

            // Le snapshot complet est déjà relu et validé sur disque. Je reprends
            // chaque frame séparément : une panne après Flush peut avoir rendu la
            // transition durable sans rendre son acquittement à l'appelant.
            if (latest == null)
            {
                latest = _justiceWriteAheadLog.Append(prepared);
            }
            if (latest.State == JusticeWalState.Prepared)
            {
                if (latest.PersistenceRevision != _justiceCriticalBarrierRevision)
                {
                    throw new InvalidDataException(
                        "La révision Prepared ne correspond plus à sa barrière runtime.");
                }
                latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    prepared.TransactionId,
                    prepared.OperationKind,
                    prepared.ProfileSlot,
                    JusticeWalState.Attempted,
                    prepared.PersistenceRevision,
                    prepared.CreatedAtUtcTicks,
                    prepared.Fields));
            }
            if (latest.State == JusticeWalState.Rejected)
            {
                // Le propriétaire n'a reçu aucun droit d'effet. Je libère la
                // barrière, puis son prochain tick préparera une nouvelle révision.
                ClearJusticeCriticalBarrier();
                return false;
            }
            if (latest.State != JusticeWalState.Attempted ||
                latest.PersistenceRevision != _justiceCriticalBarrierRevision)
            {
                throw new InvalidDataException(
                    "La frontière WAL n'est pas reprenable avant son effet.");
            }

            // Une barrière runtime encore présente avec Attempted prouve que
            // l'acquittement de cette Append a été perdu : Clear précède toujours
            // le seul return true qui autorise le propriétaire à agir.
            ClearJusticeCriticalBarrier();
            return true;
        }
        catch (Exception exception)
        {
            RegisterJusticePersistenceFailure(
                "WAL critique refusé: " + exception.GetType().Name);
            LogException("Justice.WAL.Precommit", exception);
            return false;
        }
    }

    private bool IsJusticeCriticalBarrierWalRecordExact(JusticeWalRecord record)
    {
        return record != null &&
               string.Equals(
                   record.TransactionId,
                   _justiceCriticalBarrierTransactionId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   record.OperationKind,
                   _justiceCriticalBarrierOperationKind,
                   StringComparison.Ordinal) &&
               record.ProfileSlot == _justiceCriticalBarrierProfileSlot &&
               record.PersistenceRevision >= _justiceCriticalBarrierRevision &&
               record.CreatedAtUtcTicks == _justiceCriticalBarrierCreatedAtUtcTicks &&
               HasExactJusticeWalFields(
                   record,
                   "snapshotRevision",
                   "profileGeneration",
                   "identityKey",
                   "boundary",
                   "schemaMajor") &&
               ReadWalLong(record, "snapshotRevision", -1L) ==
                   _justiceCriticalBarrierRevision &&
               ReadWalLong(record, "profileGeneration", -1L) ==
                   _justiceCriticalBarrierProfileGeneration &&
               string.Equals(
                   ReadWalString(record, "identityKey", string.Empty),
                   _justiceCriticalBarrierIdentityKey,
                   StringComparison.Ordinal) &&
               string.Equals(
                   ReadWalString(record, "boundary", string.Empty),
                   _justiceCriticalBarrierCaller,
                   StringComparison.Ordinal) &&
               ReadWalInt(record, "schemaMajor", -1) ==
                   JusticeXmlPersistenceCodec.SchemaMajor;
    }

    private void ClearJusticeCriticalBarrier()
    {
        _justiceCriticalBarrierCaller = string.Empty;
        _justiceCriticalBarrierOperationKind = string.Empty;
        _justiceCriticalBarrierTransactionId = string.Empty;
        _justiceCriticalBarrierIdentityKey = string.Empty;
        _justiceCriticalBarrierRevision = 0L;
        _justiceCriticalBarrierProfileGeneration = 0L;
        _justiceCriticalBarrierCreatedAtUtcTicks = 0L;
        _justiceCriticalBarrierProfileSlot = -1;
    }

    private bool TryRejectJusticeCriticalBarrierBeforeCustodyDeath()
    {
        if (_justiceCriticalBarrierRevision <= 0L)
        {
            return true;
        }
        if (_justiceWriteAheadLog == null)
        {
            return false;
        }

        try
        {
            JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                _justiceCriticalBarrierTransactionId);
            if (latest != null &&
                !IsJusticeCriticalBarrierWalRecordExact(latest))
            {
                throw new InvalidDataException(
                    "La frontière WAL du décès ne correspond plus à sa barrière runtime.");
            }
            if (latest != null &&
                (latest.State == JusticeWalState.Ambiguous ||
                 latest.State == JusticeWalState.Confirmed))
            {
                // Je refuse d'effacer une preuve disant que l'effet a pu
                // commencer. Ce cas anormal reste bloqué pour être repris par
                // son contrôleur au lieu d'inventer un état de décès concurrent.
                return false;
            }
            if (latest != null &&
                (latest.State == JusticeWalState.Prepared ||
                 latest.State == JusticeWalState.Attempted))
            {
                // La barrière runtime prouve que le contrôleur n'a pas reçu le
                // droit de franchir cette frontière, même si l'ACK Attempted a été
                // perdu après Flush. Je ferme la frame avant de publier le décès.
                _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    latest.TransactionId,
                    latest.OperationKind,
                    latest.ProfileSlot,
                    JusticeWalState.Rejected,
                    latest.PersistenceRevision,
                    latest.CreatedAtUtcTicks,
                    latest.Fields));
            }

            ClearJusticeCriticalBarrier();
            LogInfo(
                "Justice.WAL.Deces",
                "Frontière critique sans effet rejetée avant persistance du décès en détention.");
            return true;
        }
        catch (Exception exception)
        {
            RegisterJusticePersistenceFailure(
                "rejet de la frontière critique avant décès impossible");
            LogException("Justice.WAL.Deces", exception);
            return false;
        }
    }

    private bool TryRejectJusticeCriticalBarrierForProfileChange(int nextProfileSlot)
    {
        if (_justiceCriticalBarrierRevision <= 0L)
        {
            return true;
        }
        if (!IsJusticeCanonicalProfileSlot(nextProfileSlot) ||
            _justiceCriticalBarrierProfileSlot == nextProfileSlot ||
            _justiceWriteAheadLog == null)
        {
            return false;
        }

        try
        {
            JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                _justiceCriticalBarrierTransactionId);
            if (latest != null && latest.State == JusticeWalState.Confirmed)
            {
                // Une confirmation terminale interdit de prétendre que l'effet
                // n'a pas eu lieu. Je bloque donc le changement de protagoniste.
                return false;
            }
            if (latest != null && !latest.IsTerminal)
            {
                // Tant que la barrière vit encore, son appelant a reçu false et
                // n'a pas franchi l'effet externe. Je peux donc la rejeter avant
                // de détacher le profil qui possède son snapshot.
                _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    latest.TransactionId,
                    latest.OperationKind,
                    latest.ProfileSlot,
                    JusticeWalState.Rejected,
                    latest.PersistenceRevision,
                    latest.CreatedAtUtcTicks,
                    latest.Fields));
            }

            ClearJusticeCriticalBarrier();
            LogInfo(
                "Justice.WAL.Profil",
                "Frontière critique sans effet rejetée avant changement de protagoniste.");
            return true;
        }
        catch (Exception exception)
        {
            RegisterJusticePersistenceFailure(
                "rejet de la frontière critique avant changement de profil impossible");
            LogException("Justice.WAL.Profil", exception);
            return false;
        }
    }

    private bool TryCaptureJusticePersistenceSnapshot(
        out JusticePersistenceSnapshot snapshot)
    {
        snapshot = null;
        long startedAt = BeginJusticeMetric();
        try
        {
            InitializeJusticePersistenceServices();
            if (_justiceRepository == null || _justicePersistenceServicesUnavailable)
            {
                return false;
            }

            if (HasJusticeDeathFrontPersistenceWork())
            {
                // Je qualifie d'abord toute révision déjà terminée par le writer.
                // Une nouvelle capture ne peut ainsi ni masquer ni déplacer une
                // preuve résultat qui vient d'atteindre le disque.
                FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
            }

            PrepareJusticeActiveProfileForPersistence();
            if (_justiceLegacyProfileReloadPending ||
                _justiceCaseState == null || _justiceRecordState == null ||
                !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot))
            {
                return false;
            }
            if (_justiceBackupRepairPending &&
                (_justiceMonotonicTimeMs < _justiceNextBackupRepairAtMs ||
                 !TryRepairJusticePrimaryFromLoadedBackup()))
            {
                return false;
            }

            NormalizeJusticePinnedConvictionForCurrentCase();
            SnapshotActiveJusticePlayerProfile();
            EnsureJusticePlayerProfilesInitialized();
            EnsureJusticeProfilePersistenceGenerations();

            JusticeRepositoryDiagnostics diagnostics = _justiceRepository.GetDiagnostics();
            long nextRevision = Math.Max(
                Math.Max(_justicePersistenceRevision, diagnostics.MemoryRevision),
                diagnostics.DiskRevision) + 1L;
            if (nextRevision <= 0L)
            {
                throw new InvalidOperationException("Révision Justice arrivée à saturation.");
            }

            _justiceProfilePersistenceGenerations[_justiceActivePlayerProfileSlot] =
                Math.Max(
                    _justiceProfilePersistenceGenerations[_justiceActivePlayerProfileSlot] + 1L,
                    nextRevision);

            List<JusticePersistenceProfileSnapshot> profiles =
                new List<JusticePersistenceProfileSnapshot>(JusticePlayerProfileCount);
            for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
            {
                JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
                if (!IsJusticePersistenceProfileSemanticallyValid(profile))
                {
                    throw new InvalidDataException(
                        "Profil Justice incohérent avant snapshot, slot=" +
                        slot.ToString(CultureInfo.InvariantCulture) + ".");
                }
                JusticeCaseRecordPersistenceDto caseRecord =
                    CaptureJusticeCaseRecordPersistenceDto(
                        profile.CaseState,
                        profile.RecordState);
                profiles.Add(new JusticePersistenceProfileSnapshot(
                    slot,
                    Math.Max(0L, _justiceProfilePersistenceGenerations[slot]),
                    CreateJusticeProfileIdentityKey(profile),
                    CaptureJusticePersistenceProfileFields(profile),
                    caseRecord.Case,
                    caseRecord.Record,
                    profile.CustodySnapshot));
            }

            List<JusticePersistenceField> globalFields = new List<JusticePersistenceField>
            {
                Field("activePlayerSlot", _justiceActivePlayerProfileSlot),
                Field("nextIdentityGeneration", Math.Max(0, _justiceNextIdentityGeneration)),
                Field("policeIntegrationMode", (int)_justicePoliceIntegrationMode),
                Field("lastCanonicalPlayerSlot", _justiceLastCanonicalPlayerSlot),
                Field("lastCanonicalPlayerModel", _justiceLastCanonicalPlayerModelHash)
            };
            snapshot = new JusticePersistenceSnapshot(
                nextRevision,
                JusticeXmlPersistenceCodec.SchemaMajor,
                DateTime.UtcNow.Ticks,
                _justiceActivePlayerProfileSlot,
                globalFields,
                profiles);
            _justicePersistenceRevision = nextRevision;
            return true;
        }
        catch (Exception exception)
        {
            _justicePersistenceLastError = exception.GetType().Name + ": " + exception.Message;
            LogException("Justice.Repository.Capture", exception);
            snapshot = null;
            return false;
        }
        finally
        {
            CompleteJusticeMetric(_justicePersistenceMetrics, startedAt);
        }
    }

    private static bool IsJusticePersistenceProfileSemanticallyValid(
        JusticePlayerProfileState profile)
    {
        if (profile == null || !IsJusticeCanonicalProfileSlot(profile.Slot) ||
            profile.CaseState == null || profile.RecordState == null ||
            !IsJusticeCaseRecordLinkValid(profile.CaseState, profile.RecordState) ||
            (!profile.CaseState.Enabled && IsLoadedJusticeCaseActive(profile.CaseState)) ||
            !IsJusticeProfilePendingDeathValid(
                profile.CaseState,
                profile.PendingDeathCapture,
                profile.PendingDeathCapturePlayerSlot,
                profile.PendingDeathCapturePlayerModel) ||
            (profile.PendingDeathCapture &&
             profile.PendingDeathCapturePlayerSlot >= 0 &&
             profile.PendingDeathCapturePlayerSlot != profile.Slot) ||
            !IsJusticePendingLegalReleaseValid(
                profile.CaseState,
                profile.PendingLegalReleaseFinalization,
                profile.PendingLegalReleaseSite,
                profile.PendingLegalReleaseSelectedWeapon))
        {
            return false;
        }

        JusticeCustodyPersistenceSnapshot custody = profile.CustodySnapshot;
        if (custody != null)
        {
            bool identityRequired = custody.Active || custody.InventoryRemoved ||
                custody.DeferredInventoryRestore || custody.InventorySnapshot != null ||
                custody.FineDebitIntent != null || custody.DisciplineIntent != null;
            return (!identityRequired || custody.PlayerSlot == profile.Slot) &&
                   (custody.VoluntaryPaymentIntent == null ||
                    custody.VoluntaryPaymentIntent.Slot == profile.Slot);
        }

        // Un fragment relu du disque a déjà été validé. Sa relecture, ainsi que la
        // validation métier complète du document produit, se font sur le writer.
        return !string.IsNullOrWhiteSpace(profile.CustodyXml);
    }

    private bool TryEnqueueJusticeSnapshot(
        JusticePersistenceSnapshot snapshot,
        bool waitForDisk)
    {
        if (snapshot == null || _justiceRepository == null)
        {
            return false;
        }

        JusticeRepositoryEnqueueResult result = _justiceRepository.Enqueue(snapshot);
        if (result != JusticeRepositoryEnqueueResult.Accepted &&
            result != JusticeRepositoryEnqueueResult.Duplicate)
        {
            return false;
        }

        _justiceLastQueuedPersistenceRevision = Math.Max(
            _justiceLastQueuedPersistenceRevision,
            snapshot.Revision);
        _justiceStateDirty = false;
        _justiceNextStateSaveAtMs = 0L;
        _justiceNextCheckpointAtMs = _justiceMonotonicTimeMs + JusticeStateCheckpointMs;
        _justiceNextStateFlushAttemptAtMs = 0L;

        TrackJusticeDeathFrontResultSnapshots(snapshot);
        TrackJusticeProfileResetResultSnapshots(snapshot);

        bool persisted = true;
        if (waitForDisk)
        {
            long startedAt = Stopwatch.GetTimestamp();
            persisted = _justiceRepository.Flush(
                snapshot.Revision,
                TimeSpan.FromMilliseconds(JusticePersistenceFlushTimeoutMs));
            CompleteJusticeMetric(_justicePersistenceMetrics, startedAt);
            if (persisted)
            {
                _justiceLastPersistenceCompletedAtUtcTicks = DateTime.UtcNow.Ticks;
            }
        }

        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        if (waitForDisk && persisted && _justiceSuppressedStateFailureLogs > 0)
        {
            LogInfo(
                "Justice.Sauvegarde",
                _justiceSuppressedStateFailureLogs.ToString(CultureInfo.InvariantCulture) +
                " échec(s) répétitif(s) masqué(s) avant reprise du repository.");
            _justiceSuppressedStateFailureLogs = 0;
        }
        return persisted;
    }

    private bool ShouldForceJusticePersistenceFailureForTest()
    {
        if (_justiceStateFlushFailureOverride == null)
        {
            return false;
        }
        _justiceStateFlushAttemptSequence++;
        if (_justiceStateFlushAttemptSequence <= 0)
        {
            _justiceStateFlushAttemptSequence = 1;
        }
        try
        {
            return _justiceStateFlushFailureOverride(_justiceStateFlushAttemptSequence);
        }
        catch
        {
            return true;
        }
    }

    private void RegisterJusticePersistenceFailure(string reason)
    {
        _justicePersistenceLastError = reason ?? "échec inconnu";
        _justiceStateDirty = true;
        _justiceNextStateSaveAtMs = _justiceMonotonicTimeMs + JusticeStateCheckpointMs;
        _justiceNextStateFlushAttemptAtMs =
            _justiceMonotonicTimeMs + JusticeStateFailureRetryMs;
        if (!_justiceInitialized ||
            _justiceMonotonicTimeMs >= _justiceNextStateFailureLogAtMs)
        {
            LogWarning("Justice.Sauvegarde", _justicePersistenceLastError);
            _justiceNextStateFailureLogAtMs =
                _justiceMonotonicTimeMs + JusticeStateFailureLogCooldownMs;
        }
        else if (_justiceSuppressedStateFailureLogs < int.MaxValue)
        {
            _justiceSuppressedStateFailureLogs++;
        }
    }

    private List<JusticePersistenceField> CaptureJusticePersistenceProfileFields(
        JusticePlayerProfileState profile)
    {
        List<JusticePersistenceField> fields = new List<JusticePersistenceField>
        {
            Field("pendingDeathCapture", profile.PendingDeathCapture),
            Field("pendingDeathCapturePlayerSlot", profile.PendingDeathCapturePlayerSlot),
            Field("pendingDeathCapturePlayerModel", profile.PendingDeathCapturePlayerModel),
            Field("pendingAmnestyWantedClear", profile.PendingAmnestyWantedClear),
            Field("pendingLegalReleaseFinalization", profile.PendingLegalReleaseFinalization),
            Field("pendingLegalReleaseSite", profile.PendingLegalReleaseSite),
            Field("pendingLegalReleaseSelectedWeapon", profile.PendingLegalReleaseSelectedWeapon),
            Field("lastCanonicalPlayerModel", profile.LastCanonicalPlayerModel)
        };
        if (profile.CustodySnapshot == null)
        {
            fields.Add(new JusticePersistenceField(
                "Custody",
                string.IsNullOrWhiteSpace(profile.CustodyXml)
                    ? CreateCanonicalEmptyJusticeCustodyXml()
                    : profile.CustodyXml));
        }
        return fields;
    }

    private static JusticePersistenceField Field(string path, bool value)
    {
        return new JusticePersistenceField(path, value ? "true" : "false");
    }

    private static JusticePersistenceField Field(string path, int value)
    {
        return new JusticePersistenceField(
            path,
            value.ToString(CultureInfo.InvariantCulture));
    }

    private string CreateJusticeProfileIdentityKey(JusticePlayerProfileState profile)
    {
        return "slot:" + profile.Slot.ToString(CultureInfo.InvariantCulture) +
               ":model:" + profile.LastCanonicalPlayerModel.ToString(CultureInfo.InvariantCulture);
    }

    private void EnsureJusticeProfilePersistenceGenerations()
    {
        if (_justiceProfilePersistenceGenerations == null ||
            _justiceProfilePersistenceGenerations.Length != JusticePlayerProfileCount)
        {
            _justiceProfilePersistenceGenerations = new long[JusticePlayerProfileCount];
        }
    }

    private void LoadJusticeProfilePersistenceGenerations(
        JusticePersistenceSnapshot snapshot)
    {
        EnsureJusticeProfilePersistenceGenerations();
        if (snapshot == null)
        {
            return;
        }
        for (int index = 0; index < snapshot.Profiles.Count; index++)
        {
            JusticePersistenceProfileSnapshot profile = snapshot.Profiles[index];
            if (profile != null && IsJusticeCanonicalProfileSlot(profile.Slot))
            {
                _justiceProfilePersistenceGenerations[profile.Slot] =
                    Math.Max(0L, profile.Generation);
            }
        }
    }

    private static JusticePersistenceProfileSnapshot FindJusticePersistenceProfile(
        JusticePersistenceSnapshot snapshot,
        int slot)
    {
        if (snapshot != null)
        {
            for (int index = 0; index < snapshot.Profiles.Count; index++)
            {
                JusticePersistenceProfileSnapshot profile = snapshot.Profiles[index];
                if (profile != null && profile.Slot == slot)
                {
                    return profile;
                }
            }
        }
        return null;
    }

    private static List<JusticePersistenceField> CreateJusticeWalRecoveryFields(
        long snapshotRevision,
        long profileGeneration,
        string identityKey,
        string caller)
    {
        // Je ne place jamais Case, Record, Custody ni l'inventaire dans le WAL.
        // La frame ne référence qu'un snapshot v2 déjà durable et validé.
        return new List<JusticePersistenceField>(5)
        {
            new JusticePersistenceField(
                "snapshotRevision",
                snapshotRevision.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "profileGeneration",
                profileGeneration.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField("identityKey", identityKey ?? string.Empty),
            new JusticePersistenceField("boundary", caller ?? string.Empty),
            new JusticePersistenceField(
                "schemaMajor",
                JusticeXmlPersistenceCodec.SchemaMajor.ToString(CultureInfo.InvariantCulture))
        };
    }

    private static string CreateJusticeCriticalTransactionId(
        string caller,
        int profileSlot,
        long revision)
    {
        return "critical:" + profileSlot.ToString(CultureInfo.InvariantCulture) + ":" +
               (caller ?? "JusticeCritical") + ":" +
               revision.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeJusticeCriticalBarrierCaller(string caller)
    {
        string value = string.IsNullOrWhiteSpace(caller)
            ? "JusticeCritical"
            : caller.Trim();
        if (value == "PrepareJusticeInventoryConfiscation" ||
            value == "RetryJusticeInventoryConfiscationIfDue")
        {
            return "InventoryConfiscation";
        }
        if (value == "RequestJusticeConfirmedVoluntaryFinePayment" ||
            value == "ResumeJusticeVoluntaryFinePayment" ||
            value == "AbortJusticeVoluntaryPaymentIntent")
        {
            return "VoluntaryFinePayment";
        }
        if (value == "JusticeCollectFineAndConvertDetention" ||
            value == "ResumeJusticeFineDebitIntent")
        {
            return "FineDebit";
        }
        if (value == "EnsureJusticeAmnestyPrecommitRedundant" ||
            value == "TryApplyJusticeAmnestyWantedClear")
        {
            return "Amnesty";
        }
        if (value == "EnsureJusticeCustodyTransferRollbackPrecommitRedundant" ||
            value == "ResumeJusticeCustodyTransferRollback")
        {
            return "CustodyRollback";
        }
        return value;
    }

    private bool IsJusticeCriticalBarrierPending(string caller)
    {
        return _justiceCriticalBarrierRevision > 0L &&
               string.Equals(
                   _justiceCriticalBarrierCaller,
                   NormalizeJusticeCriticalBarrierCaller(caller),
                   StringComparison.Ordinal);
    }

    private string GetJusticeCriticalOperationKind(string caller)
    {
        switch (caller)
        {
            case "FineDebit":
                return "FineDebit";
            case "VoluntaryFinePayment":
                return "VoluntaryFinePayment";
            case "ApplyJusticeCustodyDiscipline":
            case "ResumeJusticeDisciplineIntent":
                return "Discipline";
            case "InventoryConfiscation":
            case "CompleteJusticeCustodyEscape":
                return "Inventory";
            case "BeginJusticeActiveProfileResetTransaction":
            case "EnsureJusticeActiveProfileResetPrecommitRedundant":
                return "ProfileReset";
            case "Amnesty":
                return "Amnesty";
            case "BeginJusticeCapture":
            case "CompleteJusticeCaptureAfterCommit":
                return "Capture";
            case "CompleteJusticeCustodyTransfer":
            case "PromoteJusticeCustodyToBolingbroke":
            case "ScheduleJusticeBolingbrokeTransferIfRequired":
                return "Transfer";
            case "CustodyRollback":
            case "TryRollbackJusticeCustodyTransfer":
                return "Rollback";
            case "CompleteJusticeLegalRelease":
            case "PersistJusticeLegalReleaseBoundary":
            case "PersistJusticeLegalReleaseBarrier":
                return "Release";
            case "SetJusticeCustodyPoliceSuppression":
            case "RestoreJusticeCustodyPoliceSuppression":
                return "Police";
            default:
                return string.IsNullOrWhiteSpace(caller)
                    ? "JusticeCritical"
                    : caller;
        }
    }

    private void MarkAttemptedJusticeWalTransactionsWhoseResultIsDurable(
        long diskRevision)
    {
        if (_justiceWriteAheadLog == null || diskRevision <= 0L)
        {
            return;
        }

        IReadOnlyList<JusticeWalRecord> open =
            _justiceWriteAheadLog.GetOpenTransactions();
        for (int index = 0; index < open.Count; index++)
        {
            JusticeWalRecord record = open[index];
            if (record.State != JusticeWalState.Attempted ||
                string.Equals(
                    record.OperationKind,
                    JusticeDeathFrontOperationKind,
                    StringComparison.Ordinal) ||
                string.Equals(
                    record.OperationKind,
                    JusticeProfileResetWalOperationKind,
                    StringComparison.Ordinal) ||
                diskRevision <= record.PersistenceRevision)
            {
                continue;
            }
            _justiceWriteAheadLog.Append(new JusticeWalRecord(
                record.TransactionId,
                record.OperationKind,
                record.ProfileSlot,
                JusticeWalState.Ambiguous,
                diskRevision,
                record.CreatedAtUtcTicks,
                record.Fields));
            // Je programme la rotation suivante qui prouvera que le backup
            // contient lui aussi le résultat avant la confirmation terminale.
            JusticeMarkStateDirty();
        }
    }

    private void FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable()
    {
        if (_justiceWriteAheadLog == null || _justiceRepository == null)
        {
            return;
        }
        try
        {
            long diskRevision = _justiceRepository.GetDiagnostics().DiskRevision;
            AdvanceJusticeDeathFrontWalResults(diskRevision);
            AdvanceJusticeProfileResetWalResults(diskRevision);
            // Je ne qualifie jamais un résultat sur la seule acceptation du
            // writer. La révision qui acquitte l'effet doit déjà être relue sur
            // disque, sinon un crash pourrait faire référencer un XML inexistant.
            MarkAttemptedJusticeWalTransactionsWhoseResultIsDurable(
                diskRevision);
            IReadOnlyList<JusticeWalRecord> open =
                _justiceWriteAheadLog.GetOpenTransactions();
            for (int index = 0; index < open.Count; index++)
            {
                JusticeWalRecord record = open[index];
                if (record.State != JusticeWalState.Ambiguous ||
                    string.Equals(
                        record.OperationKind,
                        JusticeDeathFrontOperationKind,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        record.OperationKind,
                        JusticeProfileResetWalOperationKind,
                        StringComparison.Ordinal) ||
                    record.PersistenceRevision >= diskRevision)
                {
                    if (record.State == JusticeWalState.Ambiguous &&
                        !string.Equals(
                            record.OperationKind,
                            JusticeDeathFrontOperationKind,
                            StringComparison.Ordinal) &&
                        !string.Equals(
                            record.OperationKind,
                            JusticeProfileResetWalOperationKind,
                            StringComparison.Ordinal) &&
                        record.PersistenceRevision >= diskRevision &&
                        record.PersistenceRevision == diskRevision)
                    {
                        // La rotation suivante place le résultat générique dans
                        // le backup. DeathFront possède son tracker dédié et ne
                        // passe jamais par cette confirmation générique.
                        JusticeMarkStateDirty();
                    }
                    continue;
                }
                // Je garde Ambiguous tant que seul le primaire contient le
                // résultat. Une révision disque strictement suivante prouve que
                // File.Replace a aussi poussé ce résultat dans le backup.
                _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    record.TransactionId,
                    record.OperationKind,
                    record.ProfileSlot,
                    JusticeWalState.Confirmed,
                    record.PersistenceRevision,
                    record.CreatedAtUtcTicks,
                    record.Fields));
            }
            JusticeWalDiagnostics walDiagnostics = _justiceWriteAheadLog.GetDiagnostics();
            if (walDiagnostics.OpenTransactionCount > 0 ||
                walDiagnostics.WalRevision > diskRevision)
            {
                _justiceWalCompactionProofSequence = 0L;
                _justiceWalCompactionProofDiskRevision = 0L;
                return;
            }

            if (_justiceWalCompactionProofSequence != walDiagnostics.LastSequence)
            {
                // Je garde un terminal pendant au moins un remplacement primaire
                // supplémentaire. Le backup porte alors lui aussi un état qui ne
                // peut plus ressusciter une intention Prepared.
                _justiceWalCompactionProofSequence = walDiagnostics.LastSequence;
                _justiceWalCompactionProofDiskRevision = diskRevision;
                return;
            }

            if (diskRevision > _justiceWalCompactionProofDiskRevision &&
                _justiceWriteAheadLog.CompactIfNoOpenTransactions())
            {
                _justiceWalCompactionProofSequence = 0L;
                _justiceWalCompactionProofDiskRevision = 0L;
            }
        }
        catch (Exception exception)
        {
            _justicePersistenceLastError = exception.GetType().Name + ": " + exception.Message;
            LogException("Justice.WAL.Confirmation", exception);
        }
    }

    private void RecoverJusticePersistenceFromWalIfRequired()
    {
        if (_justiceWriteAheadLog == null)
        {
            return;
        }
        JusticeWalDiagnostics diagnostics = _justiceWriteAheadLog.GetDiagnostics();
        if (diagnostics.RecoveryStatus == JusticeWalRecoveryStatus.Corrupt)
        {
            throw new InvalidDataException(
                "WAL Justice corrompu : " + diagnostics.LastError);
        }

        IReadOnlyList<JusticeWalRecord> open = _justiceWriteAheadLog.GetOpenTransactions();
        JusticeWalRecord newestFineDebit = null;
        JusticeWalRecord newestVoluntaryPayment = null;
        JusticeWalRecord newestProfileResetResult = null;
        List<JusticeWalRecord> inventoryConfiscations =
            new List<JusticeWalRecord>();
        for (int index = 0; index < open.Count; index++)
        {
            JusticeWalRecord candidate = open[index];
            if (string.Equals(
                    candidate.OperationKind,
                    JusticeDeathFrontOperationKind,
                    StringComparison.Ordinal))
            {
                RecoverJusticeDeathFrontFromWal(candidate);
            }
            else if (string.Equals(
                         candidate.OperationKind,
                         JusticeProfileResetWalOperationKind,
                         StringComparison.Ordinal))
            {
                if (newestProfileResetResult != null && !string.Equals(
                        newestProfileResetResult.TransactionId,
                        candidate.TransactionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Plusieurs resets de profil sont ouverts dans le WAL.");
                }
                newestProfileResetResult = candidate;
                RecoverJusticeProfileResetFromWal(candidate);
            }
            else if (candidate.OperationKind == "FineDebit")
            {
                if (newestFineDebit != null && !string.Equals(
                        newestFineDebit.TransactionId,
                        candidate.TransactionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Plusieurs transactions FineDebit sont ouvertes dans le WAL.");
                }
                newestFineDebit = candidate;
            }
            else if (candidate.OperationKind == "VoluntaryFinePayment")
            {
                if (newestVoluntaryPayment != null && !string.Equals(
                        newestVoluntaryPayment.TransactionId,
                        candidate.TransactionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Plusieurs paiements volontaires sont ouverts dans le WAL.");
                }
                newestVoluntaryPayment = candidate;
            }
            else
            {
                if (!HasExactJusticeWalFields(
                        candidate,
                        "snapshotRevision",
                        "profileGeneration",
                        "identityKey",
                        "boundary",
                        "schemaMajor") ||
                    ReadWalInt(candidate, "schemaMajor", -1) !=
                        JusticeXmlPersistenceCodec.SchemaMajor)
                {
                    throw new InvalidDataException(
                        "Le WAL Justice contient une frontière critique invalide.");
                }

                long referencedSnapshotRevision = ReadWalLong(
                    candidate,
                    "snapshotRevision",
                    -1L);
                bool invalidRevision = referencedSnapshotRevision <= 0L ||
                    referencedSnapshotRevision > _justicePersistenceRevision ||
                    candidate.PersistenceRevision < referencedSnapshotRevision ||
                    (candidate.State != JusticeWalState.Ambiguous &&
                     candidate.PersistenceRevision != referencedSnapshotRevision);
                if (invalidRevision)
                {
                    throw new InvalidDataException(
                        "Le WAL Justice référence un snapshot critique absent ou incohérent.");
                }
                if (candidate.State == JusticeWalState.Prepared)
                {
                    // Prepared n'a pas encore rendu la main à son contrôleur :
                    // aucun effet externe n'a pu commencer. Je ferme cette frame
                    // orpheline avant que le retry crée une nouvelle transaction.
                    RejectJusticePreparedWalBeforeEffect(candidate);
                    continue;
                }
                if (string.Equals(
                        candidate.OperationKind,
                        "Inventory",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        ReadWalString(candidate, "boundary", string.Empty),
                        "InventoryConfiscation",
                        StringComparison.Ordinal))
                {
                    inventoryConfiscations.Add(candidate);
                }
            }
        }

        if (newestFineDebit != null && newestVoluntaryPayment != null)
        {
            throw new InvalidDataException(
                "Deux opérations financières incompatibles sont ouvertes dans le WAL.");
        }

        if (newestFineDebit != null &&
            !TryApplyJusticeFineDebitWalRecord(newestFineDebit))
        {
            throw new InvalidDataException("Intention FineDebit WAL invalide.");
        }
        if (newestVoluntaryPayment != null &&
            !TryApplyJusticeVoluntaryPaymentWalRecord(newestVoluntaryPayment))
        {
            throw new InvalidDataException("Intention de paiement volontaire WAL invalide.");
        }
        for (int index = 0; index < inventoryConfiscations.Count; index++)
        {
            RecoverJusticeInventoryConfiscationFromWal(
                inventoryConfiscations[index]);
        }

        // Les versions antérieures confirmaient dès que seul le primaire portait
        // le résultat. Si ce primaire est perdu, le terminal conservé protège le
        // backup précommit : je le traite comme une confiscation ambiguë ciblée.
        IReadOnlyList<JusticeWalRecord> latest =
            _justiceWriteAheadLog.GetLatestTransactions();
        for (int index = 0; index < latest.Count; index++)
        {
            JusticeWalRecord candidate = latest[index];
            if (candidate.State == JusticeWalState.Confirmed &&
                string.Equals(
                    candidate.OperationKind,
                    "Inventory",
                    StringComparison.Ordinal) &&
                string.Equals(
                    ReadWalString(candidate, "boundary", string.Empty),
                    "InventoryConfiscation",
                    StringComparison.Ordinal))
            {
                RecoverJusticeInventoryConfiscationFromWal(candidate);
            }
        }

        // Chaque contrôleur reprend selon l'état durable : Prepared peut encore
        // armer une tentative, tandis qu'Attempted/Ambiguous interdit tout replay.
        if (open.Count > 0)
        {
            LogWarning(
                "Justice.WAL.Recuperation",
                open.Count.ToString(CultureInfo.InvariantCulture) +
                " frontière(s) critique(s) reprise(s) depuis le snapshot durable.");
        }
    }

    private void RejectJusticePreparedWalBeforeEffect(JusticeWalRecord record)
    {
        if (record == null || record.State != JusticeWalState.Prepared ||
            _justiceWriteAheadLog == null)
        {
            return;
        }

        _justiceWriteAheadLog.Append(new JusticeWalRecord(
            record.TransactionId,
            record.OperationKind,
            record.ProfileSlot,
            JusticeWalState.Rejected,
            Math.Max(record.PersistenceRevision, _justicePersistenceRevision),
            record.CreatedAtUtcTicks,
            record.Fields));
    }

    private void RecoverJusticeInventoryConfiscationFromWal(
        JusticeWalRecord record)
    {
        if (record == null ||
            !string.Equals(record.OperationKind, "Inventory", StringComparison.Ordinal) ||
            (record.State != JusticeWalState.Attempted &&
             record.State != JusticeWalState.Ambiguous &&
             record.State != JusticeWalState.Confirmed) ||
            !string.Equals(
                ReadWalString(record, "boundary", string.Empty),
                "InventoryConfiscation",
                StringComparison.Ordinal))
        {
            return;
        }

        long referencedSnapshotRevision = ReadWalLong(
            record,
            "snapshotRevision",
            -1L);
        if (referencedSnapshotRevision <= 0L)
        {
            throw new InvalidDataException(
                "Le WAL de confiscation référence un snapshot absent.");
        }

        EnsureJusticePlayerProfilesInitialized();
        EnsureJusticeProfilePersistenceGenerations();
        if (!IsJusticeCanonicalProfileSlot(record.ProfileSlot) ||
            record.ProfileSlot >= _justicePlayerProfiles.Length)
        {
            throw new InvalidDataException(
                "Le WAL de confiscation cible un profil Justice invalide.");
        }

        JusticePlayerProfileState targetProfile =
            _justicePlayerProfiles[record.ProfileSlot];
        long expectedGeneration =
            _justiceProfilePersistenceGenerations[record.ProfileSlot];
        long walGeneration = ReadWalLong(record, "profileGeneration", -1L);
        if (targetProfile == null || walGeneration < 0L)
        {
            throw new InvalidDataException(
                "Le WAL de confiscation ne possède plus son profil.");
        }
        if (expectedGeneration > walGeneration)
        {
            // Une génération plus récente du même profil contient déjà le
            // résultat ou une supersession explicite : je ne la dégrade jamais.
            return;
        }
        if (expectedGeneration < walGeneration ||
            !string.Equals(
                ReadWalString(record, "identityKey", string.Empty),
                CreateJusticeProfileIdentityKey(targetProfile),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Le WAL de confiscation ne correspond pas à l'identité persistée.");
        }

        if (record.ProfileSlot != _justiceActivePlayerProfileSlot)
        {
            JusticeCustodyPersistenceSnapshot inactiveCustody =
                targetProfile.CustodySnapshot;
            if (inactiveCustody != null &&
                (inactiveCustody.InventoryState ==
                     (int)JusticeInventoryCustodyState.RemovedVerified ||
                 inactiveCustody.InventoryState ==
                     (int)JusticeInventoryCustodyState.UnsupportedPreserved ||
                 inactiveCustody.InventoryState ==
                     (int)JusticeInventoryCustodyState.RestorePending ||
                 inactiveCustody.InventoryState ==
                     (int)JusticeInventoryCustodyState.RestoreAmbiguous))
            {
                return;
            }
            if (inactiveCustody == null ||
                inactiveCustody.InventoryState !=
                    (int)JusticeInventoryCustodyState.SnapshotPersisted)
            {
                throw new InvalidDataException(
                    "Le profil inactif du WAL ne possède aucun précommit d'inventaire restaurable.");
            }

            JusticeCustodyPersistenceSnapshot recoveredCustody =
                CloneJusticeCustodyPersistenceSnapshotForAmbiguousInventoryRecovery(
                    inactiveCustody);
            if (recoveredCustody == null)
            {
                throw new InvalidDataException(
                    "Le profil inactif du WAL ne possède aucun snapshot d'inventaire validé.");
            }

            // Je modifie uniquement le DTO du propriétaire de la transaction.
            // Le héros actuellement jouable garde ainsi son propre inventaire et
            // activera plus tard la restitution différée du détenu concerné.
            targetProfile.CustodySnapshot = recoveredCustody;
            targetProfile.CustodyXml = string.Empty;
            JusticeMarkStateDirty();
            LogWarning(
                "Justice.WAL.Recuperation",
                "Confiscation interrompue sur un profil inactif : restitution différée enregistrée sans toucher au héros courant.");
            return;
        }

        // Un snapshot plus récent ou un état terminal contient déjà le résultat
        // durable de la native. Je ne dégrade pas une confiscation confirmée ni
        // une restitution déjà planifiée.
        if (_justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RemovedVerified ||
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.UnsupportedPreserved ||
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RestorePending ||
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RestoreAmbiguous)
        {
            return;
        }

        bool inventoryAlreadyCleared =
            _justiceInventoryCustodyState == JusticeInventoryCustodyState.None &&
            !_justiceInventoryRemoved &&
            !_justiceWeaponControlsLocked &&
            !_justiceDeferredInventoryRestore &&
            !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) &&
            !JusticeIsCustodyActive;
        if (inventoryAlreadyCleared)
        {
            return;
        }
        if (!ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            throw new InvalidDataException(
                "Le WAL de confiscation ambigu ne possède aucun snapshot restaurable.");
        }

        // Attempted signifie que RemoveAll a pu être exécuté avant le crash.
        // Je conserve donc le snapshot et interdis tout replay destructif : la
        // fusion sera tentée uniquement après la sortie réelle de détention.
        _justiceInventoryCustodyState =
            JusticeInventoryCustodyState.RestoreAmbiguous;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceDeferredInventoryRestore = true;
        _justiceNextInventoryPersistenceRetryAt = 0;
        _justiceNextDeferredInventoryRestoreAt = 0;
        JusticeMarkStateDirty();
        LogWarning(
            "Justice.WAL.Recuperation",
            "Confiscation interrompue après tentative : snapshot conservé pour restitution différée, sans nouveau RemoveAll.");
    }

    private bool TryApplyJusticeVoluntaryPaymentWalRecord(JusticeWalRecord record)
    {
        if (record == null ||
            (record.State != JusticeWalState.Prepared &&
             record.State != JusticeWalState.Attempted &&
             record.State != JusticeWalState.Ambiguous) ||
            !HasExactJusticeWalFields(
                record,
                "paymentId",
                "slot",
                "fineBefore",
                "paidBefore",
                "debitAmount",
                "cashBefore",
                "cashAfter",
                "disputeBefore",
                "preparedAt",
                "caseEpisode",
                "profileGeneration",
                "identityKey",
                "schemaMajor"))
        {
            return false;
        }

        string paymentId = ReadWalString(record, "paymentId", string.Empty);
        int slot = ReadWalInt(record, "slot", -1);
        long fineBefore = ReadWalLong(record, "fineBefore", -1L);
        long paidBefore = ReadWalLong(record, "paidBefore", -1L);
        int debitAmount = ReadWalInt(record, "debitAmount", -1);
        int cashBefore = ReadWalInt(record, "cashBefore", -1);
        int cashAfter = ReadWalInt(record, "cashAfter", -1);
        long disputeBefore = ReadWalLong(record, "disputeBefore", -1L);
        long preparedAt = ReadWalLong(record, "preparedAt", 0L);
        string caseEpisode = ReadWalString(record, "caseEpisode", string.Empty);
        long profileGeneration;
        string expectedTransactionId = "financial:" +
            slot.ToString(CultureInfo.InvariantCulture) +
            ":VoluntaryFinePayment:" + paymentId;
        if (record.ProfileSlot != slot ||
            slot != _justiceActivePlayerProfileSlot ||
            !IsJusticeCanonicalProfileSlot(slot) ||
            !IsCanonicalJusticeVoluntaryPaymentId(paymentId) ||
            fineBefore <= 0L || fineBefore > JusticePolicy.MaxActiveFine ||
            paidBefore < 0L || paidBefore > JusticePolicy.MaxActiveFine ||
            disputeBefore < 0L || disputeBefore > JusticePolicy.MaxActiveFine ||
            paidBefore > JusticePolicy.MaxActiveFine - disputeBefore ||
            fineBefore > JusticePolicy.MaxActiveFine - paidBefore - disputeBefore ||
            debitAmount <= 0 || debitAmount > cashBefore ||
            cashBefore < 0 || cashAfter < 0 ||
            cashAfter != cashBefore - debitAmount ||
            debitAmount > fineBefore || preparedAt <= 0L ||
            string.IsNullOrWhiteSpace(caseEpisode) || caseEpisode.Length > 256 ||
            !string.Equals(
                expectedTransactionId,
                record.TransactionId,
                StringComparison.Ordinal) ||
            !TryValidateJusticeFinancialWalProfile(record, out profileGeneration) ||
            !string.Equals(
                caseEpisode,
                GetCurrentJusticeFinancialCaseEpisode(),
                StringComparison.Ordinal))
        {
            return false;
        }

        JusticeVoluntaryFinePaymentIntent existing =
            _justiceVoluntaryFinePaymentIntent;
        if (existing != null && !string.Equals(
                existing.PaymentId,
                paymentId,
                StringComparison.Ordinal))
        {
            return TryFinalizeSupersededJusticeFinancialWal(record);
        }
        if (existing == null && record.PersistenceRevision < _justicePersistenceRevision)
        {
            return TryFinalizeSupersededJusticeFinancialWal(record);
        }
        if (existing == null && record.PersistenceRevision == _justicePersistenceRevision)
        {
            return false;
        }
        if (existing != null &&
            !IsJusticeFinancialWalRecordForCurrentIntent(
                record,
                "VoluntaryFinePayment"))
        {
            return false;
        }

        bool attempted = record.State != JusticeWalState.Prepared ||
            (existing != null && existing.DebitAttempted);
        if (existing == null)
        {
            existing = new JusticeVoluntaryFinePaymentIntent
            {
                PaymentId = paymentId,
                Slot = slot,
                FineBefore = fineBefore,
                DebitAmount = debitAmount,
                CashBefore = cashBefore,
                CashAfter = cashAfter,
                FineInDisputeBefore = disputeBefore,
                PreparedAtUtcTicks = preparedAt,
                DebitAttempted = attempted,
                AttemptedAtUtcTicks = attempted ? record.CreatedAtUtcTicks : 0L,
                CashWriteResult = JusticeCashWriteResult.Unknown,
                Resolution = attempted
                    ? JusticePaymentResolution.Attempted
                    : JusticePaymentResolution.Prepared
            };
            _justiceVoluntaryFinePaymentIntent = existing;
        }
        else if (attempted && !existing.DebitAttempted)
        {
            existing.DebitAttempted = true;
            existing.AttemptedAtUtcTicks = Math.Max(1L, record.CreatedAtUtcTicks);
            existing.CashWriteResult = JusticeCashWriteResult.Unknown;
            existing.Resolution = JusticePaymentResolution.Attempted;
            existing.AmbiguousAmount = 0L;
            existing.DebtCommitted = false;
        }

        if (record.PersistenceRevision > _justicePersistenceRevision)
        {
            _justiceCaseState.FineDue = fineBefore;
            _justiceCaseState.VoluntaryFinePaid = paidBefore;
            _justiceCaseState.FineInDispute = disputeBefore;
            _justiceProfilePersistenceGenerations[slot] = Math.Max(
                _justiceProfilePersistenceGenerations[slot],
                profileGeneration);
        }
        _justicePersistenceRevision = Math.Max(
            _justicePersistenceRevision,
            record.PersistenceRevision);
        JusticeMarkStateDirty();
        return true;
    }

    private bool TryApplyJusticeFineDebitWalRecord(JusticeWalRecord record)
    {
        if (record == null ||
            (record.State != JusticeWalState.Prepared &&
             record.State != JusticeWalState.Attempted &&
             record.State != JusticeWalState.Ambiguous) ||
            !HasExactJusticeWalFields(
                record,
                "episodeId",
                "slot",
                "fineAmount",
                "cashPlan",
                "preparedAt",
                "debitAmount",
                "cashBefore",
                "cashAfter",
                "sentenceDebited",
                "sentenceConverted",
                "stationPlanned",
                "disputeBefore",
                "sentenceBefore",
                "custodyEpisode",
                "profileGeneration",
                "identityKey",
                "schemaMajor"))
        {
            return false;
        }

        string episodeId = ReadWalString(record, "episodeId", string.Empty);
        int slot = ReadWalInt(record, "slot", -1);
        long fineAmount = ReadWalLong(record, "fineAmount", -1L);
        bool cashPlan;
        bool stationPlanned;
        bool cashPlanValid = bool.TryParse(
            ReadWalString(record, "cashPlan", string.Empty),
            out cashPlan);
        bool stationPlanValid = bool.TryParse(
            ReadWalString(record, "stationPlanned", string.Empty),
            out stationPlanned);
        int debitAmount = ReadWalInt(record, "debitAmount", -1);
        int cashBefore = ReadWalInt(record, "cashBefore", -1);
        int cashAfter = ReadWalInt(record, "cashAfter", -1);
        int sentenceDebited = ReadWalInt(record, "sentenceDebited", -1);
        int sentenceConverted = ReadWalInt(record, "sentenceConverted", -1);
        int sentenceBefore = ReadWalInt(record, "sentenceBefore", -1);
        long disputeBefore = ReadWalLong(record, "disputeBefore", -1L);
        long preparedAt = ReadWalLong(record, "preparedAt", 0L);
        string custodyEpisode = ReadWalString(record, "custodyEpisode", string.Empty);
        long profileGeneration;
        int expectedDebit = cashPlan
            ? (int)Math.Min(fineAmount, (long)Math.Max(0, cashBefore))
            : 0;
        string expectedTransactionId = "financial:" +
            slot.ToString(CultureInfo.InvariantCulture) +
            ":FineDebit:" + episodeId;
        if (record.ProfileSlot != slot ||
            slot != _justiceActivePlayerProfileSlot ||
            !IsJusticeCanonicalProfileSlot(slot) ||
            string.IsNullOrWhiteSpace(episodeId) || episodeId.Length > 256 ||
            string.IsNullOrWhiteSpace(custodyEpisode) || custodyEpisode.Length > 256 ||
            fineAmount <= 0L || fineAmount > JusticePolicy.MaxActiveFine ||
            !cashPlanValid || !stationPlanValid ||
            debitAmount < 0 || debitAmount != expectedDebit ||
            cashBefore < 0 || cashAfter < 0 ||
            cashAfter != cashBefore - debitAmount ||
            sentenceBefore < 0 ||
            sentenceBefore > JusticeCustodyMaximumSentenceSeconds ||
            sentenceDebited < 0 ||
            sentenceDebited > JusticeCustodyMaximumSentenceSeconds ||
            sentenceConverted < sentenceDebited ||
            sentenceConverted > JusticeCustodyMaximumSentenceSeconds ||
            sentenceDebited != CalculateJusticeSentenceAfterFineConversion(
                sentenceBefore,
                fineAmount - debitAmount,
                stationPlanned) ||
            sentenceConverted != CalculateJusticeSentenceAfterFineConversion(
                sentenceBefore,
                fineAmount,
                stationPlanned) ||
            disputeBefore < 0L || disputeBefore > JusticePolicy.MaxActiveFine ||
            preparedAt <= 0L ||
            !string.Equals(
                expectedTransactionId,
                record.TransactionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                custodyEpisode,
                _justiceCaseState == null
                    ? string.Empty
                    : (_justiceCaseState.CustodyEpisodeId ?? string.Empty).Trim(),
                StringComparison.Ordinal) ||
            !IsJusticeFineOperationEpisodeValid(_justiceCaseState, episodeId) ||
            !TryValidateJusticeFinancialWalProfile(record, out profileGeneration))
        {
            return false;
        }

        JusticeFineDebitIntent existing = _justiceFineDebitIntent;
        if (existing != null && !string.Equals(
                existing.EpisodeId,
                episodeId,
                StringComparison.Ordinal))
        {
            return TryFinalizeSupersededJusticeFinancialWal(record);
        }
        if (existing == null && record.PersistenceRevision < _justicePersistenceRevision)
        {
            return TryFinalizeSupersededJusticeFinancialWal(record);
        }
        if (existing == null && record.PersistenceRevision == _justicePersistenceRevision)
        {
            return false;
        }
        if (existing != null &&
            !IsJusticeFinancialWalRecordForCurrentIntent(record, "FineDebit"))
        {
            return false;
        }

        bool attempted = record.State != JusticeWalState.Prepared ||
            (existing != null && existing.DebitAttempted);
        if (existing == null)
        {
            existing = new JusticeFineDebitIntent
            {
                EpisodeId = episodeId,
                Slot = slot,
                FineAmount = fineAmount,
                CashPlanPrepared = cashPlan,
                PreparedAtUtcTicks = preparedAt,
                DebitAmount = debitAmount,
                CashBefore = cashBefore,
                CashAfter = cashAfter,
                SentenceIfDebited = sentenceDebited,
                SentenceIfConverted = sentenceConverted,
                StationPlanned = stationPlanned,
                DebitAttempted = attempted,
                AttemptedAtUtcTicks = attempted ? record.CreatedAtUtcTicks : 0L,
                CashWriteResult = JusticeCashWriteResult.Unknown,
                Resolution = attempted
                    ? JusticePaymentResolution.Attempted
                    : JusticePaymentResolution.Prepared,
                FineInDisputeBefore = disputeBefore
            };
            _justiceFineDebitIntent = existing;
        }
        else if (attempted && !existing.DebitAttempted)
        {
            existing.DebitAttempted = true;
            existing.AttemptedAtUtcTicks = Math.Max(1L, record.CreatedAtUtcTicks);
            existing.CashWriteResult = JusticeCashWriteResult.Unknown;
            existing.Resolution = JusticePaymentResolution.Attempted;
            existing.AmbiguousAmount = 0L;
        }

        if (record.PersistenceRevision > _justicePersistenceRevision)
        {
            _justiceCaseState.FineDue = fineAmount;
            _justiceCaseState.FineInDispute = disputeBefore;
            _justiceCaseState.SentenceSeconds = sentenceBefore;
            _justiceProfilePersistenceGenerations[slot] = Math.Max(
                _justiceProfilePersistenceGenerations[slot],
                profileGeneration);
        }
        _justicePersistenceRevision = Math.Max(
            _justicePersistenceRevision,
            record.PersistenceRevision);
        JusticeMarkStateDirty();
        return true;
    }

    private bool TryValidateJusticeFinancialWalProfile(
        JusticeWalRecord record,
        out long profileGeneration)
    {
        profileGeneration = record == null
            ? -1L
            : ReadWalLong(record, "profileGeneration", -1L);
        if (record == null || profileGeneration <= 0L ||
            ReadWalInt(record, "schemaMajor", -1) !=
                JusticeXmlPersistenceCodec.SchemaMajor ||
            !IsJusticeCanonicalProfileSlot(record.ProfileSlot))
        {
            return false;
        }

        EnsureJusticePlayerProfilesInitialized();
        EnsureJusticeProfilePersistenceGenerations();
        JusticePlayerProfileState profile =
            _justicePlayerProfiles[record.ProfileSlot];
        string identityKey = ReadWalString(record, "identityKey", string.Empty);
        long loadedGeneration =
            _justiceProfilePersistenceGenerations[record.ProfileSlot];
        if (profile == null || string.IsNullOrWhiteSpace(identityKey) ||
            !string.Equals(
                identityKey,
                CreateJusticeProfileIdentityKey(profile),
                StringComparison.Ordinal))
        {
            return false;
        }

        return record.PersistenceRevision > _justicePersistenceRevision
            ? profileGeneration >= loadedGeneration
            : profileGeneration <= loadedGeneration;
    }

    private bool TryFinalizeSupersededJusticeFinancialWal(JusticeWalRecord record)
    {
        if (record == null || _justiceWriteAheadLog == null ||
            record.PersistenceRevision >= _justicePersistenceRevision)
        {
            return false;
        }

        JusticeWalState terminal = record.State == JusticeWalState.Prepared
            ? JusticeWalState.Rejected
            : JusticeWalState.Confirmed;
        try
        {
            _justiceWriteAheadLog.Append(new JusticeWalRecord(
                record.TransactionId,
                record.OperationKind,
                record.ProfileSlot,
                terminal,
                _justicePersistenceRevision,
                record.CreatedAtUtcTicks,
                record.Fields));
            return true;
        }
        catch (Exception exception)
        {
            _justicePersistenceLastError =
                exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private string GetCurrentJusticeFinancialCaseEpisode()
    {
        if (_justiceCaseState == null)
        {
            return string.Empty;
        }

        string custodyEpisode =
            (_justiceCaseState.CustodyEpisodeId ?? string.Empty).Trim();
        return custodyEpisode.Length > 0
            ? custodyEpisode
            : (_justiceCaseState.WantedEpisodeId ?? string.Empty).Trim();
    }

    private static bool HasExactJusticeWalFields(
        JusticeWalRecord record,
        params string[] expectedPaths)
    {
        if (record == null || expectedPaths == null ||
            record.Fields.Count != expectedPaths.Length)
        {
            return false;
        }

        HashSet<string> remaining = new HashSet<string>(
            expectedPaths,
            StringComparer.Ordinal);
        if (remaining.Count != expectedPaths.Length)
        {
            return false;
        }
        for (int index = 0; index < record.Fields.Count; index++)
        {
            JusticePersistenceField field = record.Fields[index];
            if (field == null || !remaining.Remove(field.Path))
            {
                return false;
            }
        }
        return remaining.Count == 0;
    }

    private static string ReadWalString(
        JusticeWalRecord record,
        string path,
        string fallback)
    {
        string value = record == null
            ? string.Empty
            : JusticeXmlPersistenceCodec.GetFieldValue(
                record.Fields,
                path,
                string.Empty);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static bool ReadWalBool(
        JusticeWalRecord record,
        string path,
        bool fallback)
    {
        bool value;
        return bool.TryParse(
            JusticeXmlPersistenceCodec.GetFieldValue(record.Fields, path, string.Empty),
            out value)
            ? value
            : fallback;
    }

    private static int ReadWalInt(
        JusticeWalRecord record,
        string path,
        int fallback)
    {
        int value;
        return int.TryParse(
            JusticeXmlPersistenceCodec.GetFieldValue(record.Fields, path, string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
            ? value
            : fallback;
    }

    private static long ReadWalLong(
        JusticeWalRecord record,
        string path,
        long fallback)
    {
        long value;
        return long.TryParse(
            JusticeXmlPersistenceCodec.GetFieldValue(record.Fields, path, string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
            ? value
            : fallback;
    }
}
