using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeFinancialWalBackupFloorTests
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags StaticFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void VoluntaryAttemptedWal_PrimaryNLostRecoversFromCompatibleBackupNMinusOne()
    {
        RunPrimaryLossRecoveryScenario("VoluntaryFinePayment");
    }

    [TestMethod]
    public void FineDebitAttemptedWal_PrimaryNLostRecoversFromCompatibleBackupNMinusOne()
    {
        RunPrimaryLossRecoveryScenario("FineDebit");
    }

    [TestMethod]
    public void VoluntaryPreparedWal_PrimaryNLostIsRejectedWithoutCashEffect()
    {
        RunPreparedPrimaryLossScenario("VoluntaryFinePayment");
    }

    [TestMethod]
    public void FineDebitPreparedWal_PrimaryNLostIsRejectedWithoutCashEffect()
    {
        RunPreparedPrimaryLossScenario("FineDebit");
    }

    [TestMethod]
    public void VoluntaryAttemptedWal_RejectsBackupWithDifferentPreEffectAggregates()
    {
        RunAggregateMismatchScenario("VoluntaryFinePayment");
    }

    [TestMethod]
    public void FineDebitAttemptedWal_RejectsBackupWithDifferentPreEffectAggregates()
    {
        RunAggregateMismatchScenario("FineDebit");
    }

    private static void RunPrimaryLossRecoveryScenario(string operationKind)
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = null;
            object restarted = null;
            try
            {
                long backupRevision;
                long attemptedRevision;
                string transactionId;
                PreparePrimaryLossFixture(
                    directory,
                    operationKind,
                    false,
                    true,
                    out writer,
                    out backupRevision,
                    out attemptedRevision,
                    out transactionId);
                ShutdownPersistence(writer);
                writer = null;

                string primary = Path.Combine(directory, "_justice_state.xml");
                Assert.IsTrue(File.Exists(primary));
                Assert.IsTrue(File.Exists(primary + ".bak"));
                File.Delete(primary);

                restarted = CreateHeadlessScript(0);
                Assert.IsTrue(
                    (bool)Invoke(restarted, "TryLoadJusticeState", false),
                    "Le backup N-1 compatible doit rester une autorité lisible.");
                Assert.AreEqual(
                    backupRevision,
                    GetField<long>(restarted, "_justicePersistenceRevision"));

                int cashWriteCount = 0;
                SetField(
                    restarted,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cashWriteCount++;
                        return true;
                    }));
                Invoke(restarted, "InitializeJusticePersistenceServices");

                Assert.IsFalse(
                    GetField<bool>(
                        restarted,
                        "_justicePersistenceServicesUnavailable"),
                    GetField<string>(restarted, "_justicePersistenceLastError"));
                Assert.AreEqual(
                    attemptedRevision,
                    GetField<long>(restarted, "_justicePersistenceRevision"),
                    "Le WAL relève seulement l'horloge logique au snapshot N perdu.");

                object recoveredIntent = GetRuntimeIntent(restarted, operationKind);
                Assert.IsNotNull(recoveredIntent);
                Assert.IsTrue(GetMember<bool>(recoveredIntent, "DebitAttempted"));
                Assert.AreNotEqual(
                    0L,
                    GetMember<long>(recoveredIntent, "AttemptedAtUtcTicks"));
                Assert.AreEqual(
                    0,
                    cashWriteCount,
                    "La récupération d'un WAL Attempted ne doit jamais rappeler le writer cash.");

                JusticeRepository repository = GetField<JusticeRepository>(
                    restarted,
                    "_justiceRepository");
                Assert.IsNotNull(repository);
                Assert.AreEqual(
                    backupRevision,
                    repository.GetDiagnostics().DiskRevision,
                    "Le repository doit annoncer le document N-1 réellement relu, pas la reconstruction encore en mémoire.");

                JusticeWriteAheadLog wal = GetField<JusticeWriteAheadLog>(
                    restarted,
                    "_justiceWriteAheadLog");
                Assert.AreEqual(
                    JusticeWalState.Attempted,
                    wal.GetLatest(transactionId).State,
                    "Le simple chargement mémoire ne doit pas acquitter l'effet.");

                FlushAndAwait(restarted);
                long reconstructedRevision = attemptedRevision + 1L;
                Assert.AreEqual(
                    reconstructedRevision,
                    GetField<long>(restarted, "_justicePersistenceRevision"));
                Assert.AreEqual(
                    reconstructedRevision,
                    repository.GetDiagnostics().DiskRevision,
                    "Le checkpoint N+1 doit être la première preuve disque de la reconstruction.");
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(transactionId).State,
                    "Attempted ne peut progresser qu'après le vrai checkpoint reconstruit N+1.");
                Assert.AreEqual(0, cashWriteCount);
            }
            finally
            {
                ShutdownPersistence(restarted);
                ShutdownPersistence(writer);
            }
        });
    }

    private static void RunPreparedPrimaryLossScenario(string operationKind)
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = null;
            object restarted = null;
            try
            {
                long backupRevision;
                long preparedRevision;
                string transactionId;
                PreparePrimaryLossFixture(
                    directory,
                    operationKind,
                    false,
                    false,
                    out writer,
                    out backupRevision,
                    out preparedRevision,
                    out transactionId);
                ShutdownPersistence(writer);
                writer = null;

                string primary = Path.Combine(directory, "_justice_state.xml");
                File.Delete(primary);
                restarted = CreateHeadlessScript(0);
                Assert.IsTrue((bool)Invoke(
                    restarted,
                    "TryLoadJusticeState",
                    false));

                int cashWriteCount = 0;
                SetField(
                    restarted,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cashWriteCount++;
                        return true;
                    }));
                Invoke(restarted, "InitializeJusticePersistenceServices");

                Assert.IsFalse(
                    GetField<bool>(
                        restarted,
                        "_justicePersistenceServicesUnavailable"),
                    GetField<string>(restarted, "_justicePersistenceLastError"));
                Assert.IsNull(
                    GetRuntimeIntent(restarted, operationKind),
                    "L'intention Prepared orpheline ne doit pas être reconstruite depuis le seul WAL.");
                Assert.AreEqual(0, cashWriteCount);
                Assert.AreEqual(
                    preparedRevision,
                    GetField<long>(restarted, "_justicePersistenceRevision"),
                    "La révision perdue reste consommée afin de ne jamais être réutilisée.");

                JusticeRepository repository = GetField<JusticeRepository>(
                    restarted,
                    "_justiceRepository");
                Assert.AreEqual(
                    backupRevision,
                    repository.GetDiagnostics().DiskRevision);
                JusticeWriteAheadLog wal = GetField<JusticeWriteAheadLog>(
                    restarted,
                    "_justiceWriteAheadLog");
                Assert.AreEqual(
                    JusticeWalState.Rejected,
                    wal.GetLatest(transactionId).State,
                    "Prepared sans snapshot N ne donne aucun droit d'effet et doit être fermé.");

                FlushAndAwait(restarted);
                Assert.AreEqual(
                    preparedRevision + 1L,
                    repository.GetDiagnostics().DiskRevision,
                    "Le premier nouveau checkpoint doit avancer à N+1.");
                Assert.AreEqual(0, cashWriteCount);
            }
            finally
            {
                ShutdownPersistence(restarted);
                ShutdownPersistence(writer);
            }
        });
    }

    private static void RunAggregateMismatchScenario(string operationKind)
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = null;
            object restarted = null;
            try
            {
                long backupRevision;
                long attemptedRevision;
                string transactionId;
                PreparePrimaryLossFixture(
                    directory,
                    operationKind,
                    true,
                    true,
                    out writer,
                    out backupRevision,
                    out attemptedRevision,
                    out transactionId);
                ShutdownPersistence(writer);
                writer = null;

                string primary = Path.Combine(directory, "_justice_state.xml");
                File.Delete(primary);
                restarted = CreateHeadlessScript(0);
                Assert.IsTrue((bool)Invoke(
                    restarted,
                    "TryLoadJusticeState",
                    false));
                Assert.AreEqual(
                    backupRevision,
                    GetField<long>(restarted, "_justicePersistenceRevision"));
                Assert.AreEqual(
                    500L,
                    GetField<JusticeCaseState>(restarted, "_justiceCaseState").FineDue,
                    "Le backup de contrôle doit être valide mais antérieur aux agrégats du WAL N.");

                int cashWriteCount = 0;
                SetField(
                    restarted,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cashWriteCount++;
                        return true;
                    }));
                Invoke(restarted, "InitializeJusticePersistenceServices");

                Assert.IsTrue(GetField<bool>(
                    restarted,
                    "_justicePersistenceServicesUnavailable"));
                Assert.IsTrue(
                    GetField<bool>(
                        restarted,
                        "_justicePersistenceInitializationFailurePermanent"),
                    "Un backup métier incompatible ne doit jamais être retraité comme une panne I/O transitoire.");
                Assert.IsNull(GetField<object>(restarted, "_justiceRepository"));
                Assert.IsNull(GetRuntimeIntent(restarted, operationKind));
                Assert.AreEqual(
                    500L,
                    GetField<JusticeCaseState>(restarted, "_justiceCaseState").FineDue,
                    "La récupération refusée ne doit pas écraser le dossier N-1 avec les seuls agrégats du WAL.");
                Assert.AreEqual(0, cashWriteCount);

                JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(
                    Path.Combine(directory, "_justice_state.wal"));
                Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
                JusticeWalRecord latest = FindLatestWalRecord(wal, transactionId);
                Assert.IsNotNull(latest);
                Assert.AreEqual(JusticeWalState.Attempted, latest.State);
                Assert.AreEqual(attemptedRevision, latest.PersistenceRevision);
            }
            finally
            {
                ShutdownPersistence(restarted);
                ShutdownPersistence(writer);
            }
        });
    }

    private static void PreparePrimaryLossFixture(
        string directory,
        string operationKind,
        bool changeAggregatesAtRevisionN,
        bool appendAttempted,
        out object writer,
        out long backupRevision,
        out long attemptedRevision,
        out string transactionId)
    {
        writer = CreateHeadlessScript(0);
        ConfigureFinancialCase(writer, operationKind, 500L);
        if (!changeAggregatesAtRevisionN)
        {
            AddFinancialCharge(writer, operationKind, 100L, "stable-supplement");
        }
        FlushAndAwait(writer);
        backupRevision = GetField<long>(writer, "_justicePersistenceRevision");

        if (changeAggregatesAtRevisionN)
        {
            AddFinancialCharge(writer, operationKind, 100L, "late-supplement");
        }
        ConfigurePreparedFinancialIntent(writer, operationKind);
        FlushAndAwait(writer);
        attemptedRevision = GetField<long>(writer, "_justicePersistenceRevision");
        Assert.AreEqual(backupRevision + 1L, attemptedRevision);

        long[] generations = GetField<long[]>(
            writer,
            "_justiceProfilePersistenceGenerations");
        JusticePlayerProfileState[] profiles =
            GetField<JusticePlayerProfileState[]>(writer, "_justicePlayerProfiles");
        string identityKey = (string)Invoke(
            writer,
            "CreateJusticeProfileIdentityKey",
            profiles[0]);
        List<JusticePersistenceField> walFields =
            (List<JusticePersistenceField>)Invoke(
                writer,
                "CreateJusticeFinancialWalFields",
                operationKind,
                generations[0],
                identityKey);
        transactionId = (string)Invoke(
            writer,
            "CreateJusticeFinancialTransactionId",
            operationKind);
        long preparedAt = (long)Invoke(
            writer,
            "GetJusticeFinancialPreparedAtUtcTicks",
            operationKind);
        JusticeWriteAheadLog wal = GetField<JusticeWriteAheadLog>(
            writer,
            "_justiceWriteAheadLog");
        wal.Append(new JusticeWalRecord(
            transactionId,
            operationKind,
            0,
            JusticeWalState.Prepared,
            attemptedRevision,
            preparedAt,
            walFields));
        if (appendAttempted)
        {
            wal.Append(new JusticeWalRecord(
                transactionId,
                operationKind,
                0,
                JusticeWalState.Attempted,
                attemptedRevision,
                preparedAt,
                walFields));
        }

        Assert.IsTrue(File.Exists(Path.Combine(directory, "_justice_state.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(
            directory,
            "_justice_state.xml.bak")));
    }

    private static void ConfigureFinancialCase(
        object script,
        string operationKind,
        long initialFine)
    {
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Enabled = true;
        state.WantedEpisodeId = "wanted:backup-floor";
        state.CustodyEpisodeId = operationKind == "FineDebit"
            ? "custody:backup-floor"
            : string.Empty;
        state.Phase = operationKind == "FineDebit"
            ? JusticePhase.Captured
            : JusticePhase.Wanted;
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:incident:backup-floor:base",
            IncidentId = "incident:backup-floor:base",
            EpisodeId = state.WantedEpisodeId,
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 10,
            Fine = initialFine,
            SentenceSeconds = operationKind == "FineDebit"
                ? GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds")
                : 0,
            IsAdjudicated = operationKind == "FineDebit"
        });
        state.RecalculateTotals();
        SetField(script, "_justiceEnabled", true);

        if (operationKind == "FineDebit")
        {
            SetField(script, "_justiceCustodyPlayerSlot", 0);
            SetField(script, "_justiceCustodyPlayerModelHash", MichaelModelHash());
        }
    }

    private static void AddFinancialCharge(
        object script,
        string operationKind,
        long fine,
        string suffix)
    {
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        string incidentId = "incident:backup-floor:" + suffix;
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + incidentId,
            IncidentId = incidentId,
            EpisodeId = state.WantedEpisodeId,
            Kind = JusticeCrimeKind.VehicleDamage,
            Points = 2,
            Fine = fine,
            SentenceSeconds = 0,
            IsAdjudicated = operationKind == "FineDebit"
        });
        state.RecalculateTotals();
        Assert.AreEqual(600L, state.FineDue);
    }

    private static void ConfigurePreparedFinancialIntent(
        object script,
        string operationKind)
    {
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        Assert.AreEqual(600L, state.FineDue);

        if (operationKind == "VoluntaryFinePayment")
        {
            object voluntary = CreateNested("JusticeVoluntaryFinePaymentIntent");
            SetMember(
                voluntary,
                "PaymentId",
                "payment:00000000000000000000000000000051");
            SetMember(voluntary, "Slot", 0);
            SetMember(voluntary, "FineBefore", state.FineDue);
            SetMember(voluntary, "DebitAmount", 600);
            SetMember(voluntary, "CashBefore", 1000);
            SetMember(voluntary, "CashAfter", 400);
            SetMember(voluntary, "FineInDisputeBefore", state.FineInDispute);
            SetMember(voluntary, "PreparedAtUtcTicks", FixedPreparedAtUtcTicks());
            SetField(script, "_justiceVoluntaryFinePaymentIntent", voluntary);
            return;
        }

        bool stationPlanned = state.SentenceSeconds <
            GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds");
        int sentenceIfDebited = (int)InvokeStatic(
            "CalculateJusticeSentenceAfterFineConversion",
            state.SentenceSeconds,
            0L,
            stationPlanned);
        int sentenceIfConverted = (int)InvokeStatic(
            "CalculateJusticeSentenceAfterFineConversion",
            state.SentenceSeconds,
            state.FineDue,
            stationPlanned);
        object fineDebit = CreateNested("JusticeFineDebitIntent");
        SetMember(fineDebit, "EpisodeId", state.CustodyEpisodeId);
        SetMember(fineDebit, "Slot", 0);
        SetMember(fineDebit, "FineAmount", state.FineDue);
        SetMember(fineDebit, "CashPlanPrepared", true);
        SetMember(fineDebit, "PreparedAtUtcTicks", FixedPreparedAtUtcTicks());
        SetMember(fineDebit, "DebitAmount", 600);
        SetMember(fineDebit, "CashBefore", 1000);
        SetMember(fineDebit, "CashAfter", 400);
        SetMember(fineDebit, "SentenceIfDebited", sentenceIfDebited);
        SetMember(fineDebit, "SentenceIfConverted", sentenceIfConverted);
        SetMember(fineDebit, "StationPlanned", stationPlanned);
        SetMember(fineDebit, "FineInDisputeBefore", state.FineInDispute);
        SetField(script, "_justiceFineDebitIntent", fineDebit);
    }

    private static object CreateHeadlessScript(int activeSlot)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState();
        JusticeRecordState record = new JusticeRecordState();
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", record);
        SetField(script, "_justiceCustodyStoredCanRagdoll", true);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        int unarmed = GetStaticField<int>("JusticeUnarmedHash");
        SetField(script, "_justiceReleaseSelectedWeaponHash", unarmed);
        SetField(script, "_justiceLegalReleaseSelectedWeaponHash", unarmed);

        string[] collectionFields =
        {
            "_justicePendingIncidents",
            "_justiceRecentVictims",
            "_justiceRecentVehicles",
            "_justiceAllyTokens",
            "_justiceTrackedIdentities",
            "_justiceSelfDefenseUntilByVictim",
            "_justiceDamageFrontsToConsume",
            "_justiceDamagePairBaselines",
            "_justiceWitnessSnapshots",
            "_justiceCustodyGuards",
            "_justiceCustodyInmates",
            "_justiceActivityCooldownUntil",
            "_justiceLoadedActivityCooldownSeconds"
        };
        for (int index = 0; index < collectionFields.Length; index++)
        {
            FieldInfo field = ScriptType.GetField(
                collectionFields[index],
                InstanceFlags);
            Assert.IsNotNull(field, collectionFields[index]);
            field.SetValue(script, Activator.CreateInstance(field.FieldType, true));
        }

        Invoke(script, "InitializeJusticePlayerProfiles");
        SetField(script, "_justiceActivePlayerProfileSlot", activeSlot);
        SetField(script, "_justiceMenuSelectedProfileSlot", activeSlot);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => activeSlot));
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        SetField(script, "_justiceLastCanonicalPlayerSlot", activeSlot);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", MichaelModelHash());
        JusticePlayerProfileState[] profiles =
            GetField<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
        profiles[activeSlot].CaseState = state;
        profiles[activeSlot].RecordState = record;
        profiles[activeSlot].LastCanonicalPlayerModel = MichaelModelHash();
        return script;
    }

    private static object GetRuntimeIntent(object script, string operationKind)
    {
        return operationKind == "VoluntaryFinePayment"
            ? GetField<object>(script, "_justiceVoluntaryFinePaymentIntent")
            : GetField<object>(script, "_justiceFineDebitIntent");
    }

    private static JusticeWalRecord FindLatestWalRecord(
        JusticeWalRecoveryResult recovery,
        string transactionId)
    {
        JusticeWalRecord latest = null;
        for (int index = 0; index < recovery.Records.Count; index++)
        {
            JusticeWalRecord candidate = recovery.Records[index];
            if (string.Equals(
                    candidate.TransactionId,
                    transactionId,
                    StringComparison.Ordinal))
            {
                latest = candidate;
            }
        }
        return latest;
    }

    private static int MichaelModelHash()
    {
        return GTA.Game.GenerateHash("player_zero");
    }

    private static long FixedPreparedAtUtcTicks()
    {
        return new DateTime(2026, 8, 30, 14, 0, 0, DateTimeKind.Utc).Ticks;
    }

    private static void FlushAndAwait(object script)
    {
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeFlushStateNow"),
            GetField<string>(script, "_justicePersistenceLastError"));
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"),
            GetField<string>(script, "_justicePersistenceLastError"));
    }

    private static void ShutdownPersistence(object script)
    {
        if (script == null)
        {
            return;
        }
        try
        {
            Invoke(script, "ShutdownJusticePersistenceServices");
        }
        catch
        {
            // Je garde l'assertion métier d'origine comme cause du test.
        }
    }

    private static object CreateNested(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, name);
        return Activator.CreateInstance(type, true);
    }

    private static object Invoke(
        object target,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, InstanceFlags);
        Assert.IsNotNull(method, methodName);
        return method.Invoke(target, arguments);
    }

    private static object InvokeStatic(
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, StaticFlags);
        Assert.IsNotNull(method, methodName);
        return method.Invoke(null, arguments);
    }

    private static T GetStaticField<T>(string name)
    {
        FieldInfo field = ScriptType.GetField(name, StaticFlags);
        Assert.IsNotNull(field, name);
        return (T)(field.IsLiteral
            ? field.GetRawConstantValue()
            : field.GetValue(null));
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = ScriptType.GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        return (T)field.GetValue(target);
    }

    private static T GetMember<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        return (T)field.GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = ScriptType.GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }

    private static void SetMember(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }

    private static void WithTemporarySaveDirectory(Action<string> action)
    {
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalBackupFloor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            action(directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
