using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

// Je transporte uniquement des valeurs immuables et primitives vers le writer.
// Aucun objet GTA ni aucune collection mutable du runtime ne franchit cette limite.
internal sealed class JusticePersistenceField
{
    internal JusticePersistenceField(string path, string value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin de persistance est obligatoire.", "path");
        }

        Path = path.Trim();
        Value = value ?? string.Empty;
    }

    internal string Path { get; private set; }

    internal string Value { get; private set; }
}

internal sealed class JusticePersistenceProfileSnapshot
{
    private readonly ReadOnlyCollection<JusticePersistenceField> _fields;

    internal JusticePersistenceProfileSnapshot(
        int slot,
        long generation,
        string identityKey,
        IEnumerable<JusticePersistenceField> fields)
        : this(slot, generation, identityKey, fields, null, null, null)
    {
    }

    internal JusticePersistenceProfileSnapshot(
        int slot,
        long generation,
        string identityKey,
        IEnumerable<JusticePersistenceField> fields,
        JusticeCasePersistenceDto caseState,
        JusticeRecordPersistenceDto recordState,
        JusticeCustodyPersistenceSnapshot custodyState)
    {
        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException("slot");
        }

        if (generation < 0L)
        {
            throw new ArgumentOutOfRangeException("generation");
        }

        Slot = slot;
        Generation = generation;
        IdentityKey = identityKey ?? string.Empty;
        _fields = CopyFields(fields);
        CaseState = caseState;
        RecordState = recordState;
        CustodyState = custodyState;
    }

    internal int Slot { get; private set; }

    internal long Generation { get; private set; }

    internal string IdentityKey { get; private set; }

    internal IReadOnlyList<JusticePersistenceField> Fields
    {
        get { return _fields; }
    }

    // Ces graphes sont déjà profondément immuables. Ils restent optionnels pour
    // les snapshots relus depuis le XML, dont les fragments vivent dans Fields.
    internal JusticeCasePersistenceDto CaseState { get; private set; }

    internal JusticeRecordPersistenceDto RecordState { get; private set; }

    internal JusticeCustodyPersistenceSnapshot CustodyState { get; private set; }

    internal bool HasTypedFragments
    {
        get
        {
            return CaseState != null || RecordState != null || CustodyState != null;
        }
    }

    private static ReadOnlyCollection<JusticePersistenceField> CopyFields(
        IEnumerable<JusticePersistenceField> fields)
    {
        List<JusticePersistenceField> copy = new List<JusticePersistenceField>();
        if (fields != null)
        {
            foreach (JusticePersistenceField field in fields)
            {
                if (field == null)
                {
                    throw new ArgumentException(
                        "Un snapshot de profil ne peut pas contenir de champ nul.",
                        "fields");
                }

                // Je recopie meme les feuilles immuables pour que le contrat de
                // profondeur reste explicite si leur implementation evolue.
                copy.Add(new JusticePersistenceField(field.Path, field.Value));
            }
        }

        return new ReadOnlyCollection<JusticePersistenceField>(copy);
    }
}

internal sealed class JusticePersistenceSnapshot
{
    private readonly ReadOnlyCollection<JusticePersistenceField> _globalFields;
    private readonly ReadOnlyCollection<JusticePersistenceProfileSnapshot> _profiles;

    internal JusticePersistenceSnapshot(
        long revision,
        int schemaVersion,
        long capturedAtUtcTicks,
        int activeProfileSlot,
        IEnumerable<JusticePersistenceField> globalFields,
        IEnumerable<JusticePersistenceProfileSnapshot> profiles)
    {
        if (revision <= 0L)
        {
            throw new ArgumentOutOfRangeException("revision");
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException("schemaVersion");
        }

        if (capturedAtUtcTicks < DateTime.MinValue.Ticks ||
            capturedAtUtcTicks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException("capturedAtUtcTicks");
        }

        if (activeProfileSlot < -1)
        {
            throw new ArgumentOutOfRangeException("activeProfileSlot");
        }

        Revision = revision;
        SchemaVersion = schemaVersion;
        CapturedAtUtcTicks = capturedAtUtcTicks;
        ActiveProfileSlot = activeProfileSlot;
        _globalFields = CopyFields(globalFields, "globalFields");
        _profiles = CopyProfiles(profiles);
    }

    internal long Revision { get; private set; }

    internal int SchemaVersion { get; private set; }

    internal long CapturedAtUtcTicks { get; private set; }

    internal int ActiveProfileSlot { get; private set; }

    internal IReadOnlyList<JusticePersistenceField> GlobalFields
    {
        get { return _globalFields; }
    }

    internal IReadOnlyList<JusticePersistenceProfileSnapshot> Profiles
    {
        get { return _profiles; }
    }

    private static ReadOnlyCollection<JusticePersistenceField> CopyFields(
        IEnumerable<JusticePersistenceField> fields,
        string parameterName)
    {
        List<JusticePersistenceField> copy = new List<JusticePersistenceField>();
        if (fields != null)
        {
            foreach (JusticePersistenceField field in fields)
            {
                if (field == null)
                {
                    throw new ArgumentException(
                        "Un snapshot ne peut pas contenir de champ nul.",
                        parameterName);
                }

                copy.Add(new JusticePersistenceField(field.Path, field.Value));
            }
        }

        return new ReadOnlyCollection<JusticePersistenceField>(copy);
    }

    private static ReadOnlyCollection<JusticePersistenceProfileSnapshot> CopyProfiles(
        IEnumerable<JusticePersistenceProfileSnapshot> profiles)
    {
        List<JusticePersistenceProfileSnapshot> copy =
            new List<JusticePersistenceProfileSnapshot>();
        HashSet<int> slots = new HashSet<int>();
        if (profiles != null)
        {
            foreach (JusticePersistenceProfileSnapshot profile in profiles)
            {
                if (profile == null)
                {
                    throw new ArgumentException(
                        "Un snapshot ne peut pas contenir de profil nul.",
                        "profiles");
                }

                if (!slots.Add(profile.Slot))
                {
                    throw new ArgumentException(
                        "Chaque slot de profil doit etre unique dans un snapshot.",
                        "profiles");
                }

                copy.Add(new JusticePersistenceProfileSnapshot(
                    profile.Slot,
                    profile.Generation,
                    profile.IdentityKey,
                    profile.Fields,
                    profile.CaseState,
                    profile.RecordState,
                    profile.CustodyState));
            }
        }

        return new ReadOnlyCollection<JusticePersistenceProfileSnapshot>(copy);
    }
}

internal interface IJusticePersistenceCodec
{
    byte[] Serialize(JusticePersistenceSnapshot snapshot);

    bool TryDeserialize(
        byte[] document,
        out JusticePersistenceSnapshot snapshot,
        out string error);
}

internal enum JusticeRepositoryEnqueueResult
{
    Accepted,
    Duplicate,
    Stale,
    Stopped
}

internal enum JusticeRepositoryState
{
    Created,
    Running,
    Stopping,
    Stopped
}

internal enum JusticePersistenceFaultPoint
{
    BeforeSnapshotSerialization,
    AfterSnapshotSerialization,
    BeforeAtomicTempWrite,
    BeforeAtomicTempFlush,
    AfterAtomicTempFlush,
    BeforeAtomicReplace,
    AfterAtomicReplace,
    BeforeWalFrameWrite,
    BeforeWalFlush,
    AfterWalFlush,
    BeforeWalTailRepairFlush,
    AfterWalTailRepairFlush
}

internal interface IJusticePersistenceFaultInjector
{
    void Probe(JusticePersistenceFaultPoint point);
}

internal interface IJusticeAtomicFileStore
{
    void WriteAtomically(
        string targetPath,
        string backupPath,
        byte[] document,
        IJusticePersistenceFaultInjector faultInjector);

    byte[] ReadAllBytes(string path);
}

internal sealed class JusticeRepositoryDiagnostics
{
    internal JusticeRepositoryDiagnostics(
        JusticeRepositoryState state,
        long memoryRevision,
        long pendingRevision,
        long writingRevision,
        long diskRevision,
        long writeAttempts,
        long writeFailures,
        string lastError)
    {
        State = state;
        MemoryRevision = memoryRevision;
        PendingRevision = pendingRevision;
        WritingRevision = writingRevision;
        DiskRevision = diskRevision;
        WriteAttempts = writeAttempts;
        WriteFailures = writeFailures;
        LastError = lastError ?? string.Empty;
    }

    internal JusticeRepositoryState State { get; private set; }

    internal long MemoryRevision { get; private set; }

    internal long PendingRevision { get; private set; }

    internal long WritingRevision { get; private set; }

    internal long DiskRevision { get; private set; }

    internal long WriteAttempts { get; private set; }

    internal long WriteFailures { get; private set; }

    internal string LastError { get; private set; }

    internal bool IsCaughtUp
    {
        get
        {
            return MemoryRevision <= DiskRevision && PendingRevision == 0L &&
                   WritingRevision == 0L;
        }
    }
}

internal enum JusticeWalState
{
    Prepared,
    Attempted,
    Confirmed,
    Rejected,
    Ambiguous
}

internal enum JusticeWalRecoveryStatus
{
    Clean,
    TruncatedTail,
    Corrupt
}

internal sealed class JusticeWalDiagnostics
{
    internal JusticeWalDiagnostics(
        long lastSequence,
        long walRevision,
        long durableLength,
        int openTransactionCount,
        JusticeWalRecoveryStatus recoveryStatus,
        long repairedTailCount,
        string lastError)
    {
        LastSequence = lastSequence;
        WalRevision = walRevision;
        DurableLength = durableLength;
        OpenTransactionCount = openTransactionCount;
        RecoveryStatus = recoveryStatus;
        RepairedTailCount = repairedTailCount;
        LastError = lastError ?? string.Empty;
    }

    internal long LastSequence { get; private set; }

    internal long WalRevision { get; private set; }

    internal long DurableLength { get; private set; }

    internal int OpenTransactionCount { get; private set; }

    internal JusticeWalRecoveryStatus RecoveryStatus { get; private set; }

    internal long RepairedTailCount { get; private set; }

    internal string LastError { get; private set; }
}

internal sealed class JusticeDurabilityDiagnostics
{
    internal JusticeDurabilityDiagnostics(
        JusticeRepositoryDiagnostics repository,
        JusticeWalDiagnostics wal)
    {
        if (repository == null)
        {
            throw new ArgumentNullException("repository");
        }

        if (wal == null)
        {
            throw new ArgumentNullException("wal");
        }

        MemoryRevision = repository.MemoryRevision;
        DiskRevision = repository.DiskRevision;
        WalRevision = wal.WalRevision;
        WalSequence = wal.LastSequence;
        RepositoryCaughtUp = repository.IsCaughtUp;
        WalHealthy = wal.RecoveryStatus != JusticeWalRecoveryStatus.Corrupt;
    }

    internal long MemoryRevision { get; private set; }

    internal long DiskRevision { get; private set; }

    internal long WalRevision { get; private set; }

    internal long WalSequence { get; private set; }

    internal bool RepositoryCaughtUp { get; private set; }

    internal bool WalHealthy { get; private set; }
}
