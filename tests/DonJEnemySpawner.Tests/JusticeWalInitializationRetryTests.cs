using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeWalInitializationRetryTests
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void Initialization_ExclusiveWalLockRetriesWithoutPermanentFailureOrPreparedLoss()
    {
        string previousSaveDirectory = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalInitRetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        object script = null;
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            script = CreateHeadlessScriptWithPreparedPayment(out string transactionId);
            string walPath = Path.Combine(directory, "_justice_state.wal");
            AppendPreparedPaymentWal(script, walPath, transactionId);

            using (FileStream exclusiveLock = new FileStream(
                walPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                Invoke(script, "InitializeJusticePersistenceServices");

                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePersistenceServicesUnavailable"));
                Assert.IsFalse(
                    GetField<bool>(
                        script,
                        "_justicePersistenceInitializationFailurePermanent"),
                    "Un verrou Windows temporaire ne doit jamais être classé comme une corruption permanente.");
                Assert.IsNull(GetField<object>(script, "_justiceRepository"));
                Assert.IsNull(GetField<object>(script, "_justiceWriteAheadLog"));
                StringAssert.Contains(
                    GetField<string>(script, "_justicePersistenceLastError"),
                    "IOException");
            }

            JusticeWalRecoveryResult afterUnlock =
                JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, afterUnlock.Status);
            Assert.AreEqual(1, afterUnlock.Records.Count);
            Assert.AreEqual(JusticeWalState.Prepared, afterUnlock.Records[0].State);
            Assert.AreEqual(transactionId, afterUnlock.Records[0].TransactionId);

            long retryAt = GetField<long>(
                script,
                "_justiceNextPersistenceInitializationRetryAtMs");
            Assert.IsTrue(retryAt > 0L);
            SetField(script, "_justiceMonotonicTimeMs", retryAt);
            Invoke(script, "InitializeJusticePersistenceServices");

            Assert.IsFalse(
                GetField<bool>(script, "_justicePersistenceServicesUnavailable"),
                GetField<string>(script, "_justicePersistenceLastError"));
            Assert.IsFalse(GetField<bool>(
                script,
                "_justicePersistenceInitializationFailurePermanent"));
            Assert.IsNotNull(GetField<object>(script, "_justiceRepository"));

            JusticeWriteAheadLog recoveredWal = GetField<JusticeWriteAheadLog>(
                script,
                "_justiceWriteAheadLog");
            Assert.IsNotNull(recoveredWal);
            JusticeWalRecord recovered = recoveredWal.GetLatest(transactionId);
            Assert.IsNotNull(recovered);
            Assert.AreEqual(
                JusticeWalState.Prepared,
                recovered.State,
                "La reprise ne doit ni inventer une tentative cash ni rejeter l'intention Prepared valide.");
            Assert.AreEqual(1, recoveredWal.GetOpenTransactions().Count);

            Assert.IsTrue(
                (bool)Invoke(
                    script,
                    "EnsureJusticeFinancialPreparedSnapshot",
                    "VoluntaryFinePayment"),
                "Le WAL Prepared relu doit réhydrater sa barrière sans capturer une nouvelle génération.");
            object[] armArguments = { "VoluntaryFinePayment", false };
            Assert.IsTrue(
                (bool)InvokeWithArguments(
                    script,
                    "TryArmJusticeFinancialAttempt",
                    armArguments));
            Assert.IsFalse(
                (bool)armArguments[1],
                "Prepared n'autorise qu'une première tentative; aucun SET antérieur ne doit être présumé.");
            Assert.AreEqual(
                JusticeWalState.Attempted,
                recoveredWal.GetLatest(transactionId).State,
                "La reprise doit pouvoir progresser jusqu'au jeton at-most-once Attempted.");
            Assert.AreEqual(
                1L,
                GetField<long[]>(
                    script,
                    "_justiceProfilePersistenceGenerations")[0],
                "Réhydrater Prepared ne doit pas créer une génération incompatible.");
        }
        finally
        {
            ShutdownPersistence(script);
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previousSaveDirectory);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void Initialization_SupersededWalAppendIoFailureRemainsRetryable()
    {
        string previousSaveDirectory = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalSupersededRetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        object script = null;
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            script = CreateHeadlessScriptWithPreparedPayment(
                out string transactionId);
            string walPath = Path.Combine(directory, "_justice_state.wal");
            AppendPreparedPaymentWal(script, walPath, transactionId);
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 2L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 2L);
            SetField(
                script,
                "_justiceWalFaultInjectorOverride",
                new OneShotWalIoFaultInjector());

            Invoke(script, "InitializeJusticePersistenceServices");

            Assert.IsTrue(GetField<bool>(
                script,
                "_justicePersistenceServicesUnavailable"));
            Assert.IsFalse(
                GetField<bool>(
                    script,
                    "_justicePersistenceInitializationFailurePermanent"),
                "L'échec d'Append terminal d'un ancien WAL doit rester retryable.");
            StringAssert.Contains(
                GetField<string>(script, "_justicePersistenceLastError"),
                "IOException");
            Assert.AreEqual(
                JusticeWalState.Prepared,
                JusticeWriteAheadLog.Recover(walPath).Records[0].State);

            long retryAt = GetField<long>(
                script,
                "_justiceNextPersistenceInitializationRetryAtMs");
            SetField(script, "_justiceMonotonicTimeMs", retryAt);
            Invoke(script, "InitializeJusticePersistenceServices");

            Assert.IsFalse(
                GetField<bool>(script, "_justicePersistenceServicesUnavailable"),
                GetField<string>(script, "_justicePersistenceLastError"));
            JusticeWalRecord terminal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog")
                    .GetLatest(transactionId);
            Assert.IsNotNull(terminal);
            Assert.AreEqual(
                JusticeWalState.Rejected,
                terminal.State,
                "Le retry doit fermer Prepared supersédé sans effet cash.");
        }
        finally
        {
            ShutdownPersistence(script);
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previousSaveDirectory);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static object CreateHeadlessScriptWithPreparedPayment(
        out string transactionId)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        const string incidentId = "incident:wal-init-retry";
        const string episodeId = "wanted:wal-init-retry";
        const string paymentId =
            "payment:00000000000000000000000000000041";
        int modelHash = GTA.Game.GenerateHash("player_zero");

        JusticeCaseState activeCase = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Wanted,
            WantedEpisodeId = episodeId
        };
        activeCase.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + incidentId,
            IncidentId = incidentId,
            EpisodeId = episodeId,
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 12,
            Fine = 600L,
            SentenceSeconds = 0,
            IsAdjudicated = false
        });
        activeCase.RecalculateTotals();
        JusticeRecordState activeRecord = new JusticeRecordState();

        JusticePlayerProfileState[] profiles =
            new JusticePlayerProfileState[3];
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            profiles[slot] = new JusticePlayerProfileState(slot)
            {
                LastCanonicalPlayerModel = slot == 0 ? modelHash : 0
            };
        }
        profiles[0].CaseState = activeCase;
        profiles[0].RecordState = activeRecord;

        SetField(script, "_justiceCaseState", activeCase);
        SetField(script, "_justiceRecordState", activeRecord);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceProfilePersistenceGenerations", new[] { 1L, 0L, 0L });
        SetField(script, "_justicePersistenceRevision", 1L);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", modelHash);

        object intent = Activator.CreateInstance(
            ScriptType.GetNestedType(
                "JusticeVoluntaryFinePaymentIntent",
                BindingFlags.NonPublic),
            true);
        SetMember(intent, "PaymentId", paymentId);
        SetMember(intent, "Slot", 0);
        SetMember(intent, "FineBefore", 600L);
        SetMember(intent, "DebitAmount", 600);
        SetMember(intent, "CashBefore", 1000);
        SetMember(intent, "CashAfter", 400);
        SetMember(intent, "FineInDisputeBefore", 0L);
        SetMember(intent, "PreparedAtUtcTicks", FixedPreparedAtUtcTicks());
        SetField(script, "_justiceVoluntaryFinePaymentIntent", intent);

        transactionId = "financial:0:VoluntaryFinePayment:" + paymentId;
        return script;
    }

    private static void AppendPreparedPaymentWal(
        object script,
        string walPath,
        string transactionId)
    {
        JusticePlayerProfileState[] profiles =
            GetField<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
        string identityKey = (string)Invoke(
            script,
            "CreateJusticeProfileIdentityKey",
            profiles[0]);
        List<JusticePersistenceField> fields =
            (List<JusticePersistenceField>)Invoke(
                script,
                "CreateJusticeFinancialWalFields",
                "VoluntaryFinePayment",
                1L,
                identityKey);
        Assert.IsNotNull(fields);

        JusticeWriteAheadLog wal = new JusticeWriteAheadLog(walPath);
        wal.Append(new JusticeWalRecord(
            transactionId,
            "VoluntaryFinePayment",
            0,
            JusticeWalState.Prepared,
            1L,
            FixedPreparedAtUtcTicks(),
            fields));
    }

    private static long FixedPreparedAtUtcTicks()
    {
        return new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc).Ticks;
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
            // Je préserve l'assertion principale si le cleanup d'un test échoue.
        }
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

    private static object InvokeWithArguments(
        object target,
        string methodName,
        object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, InstanceFlags);
        Assert.IsNotNull(method, methodName);
        return method.Invoke(target, arguments);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, InstanceFlags);
        Assert.IsNotNull(field, fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, InstanceFlags);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(target, value);
    }

    private static void SetMember(object target, string fieldName, object value)
    {
        Assert.IsNotNull(target);
        FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(target, value);
    }

    private sealed class OneShotWalIoFaultInjector :
        IJusticePersistenceFaultInjector
    {
        private bool _failed;

        public void Probe(JusticePersistenceFaultPoint point)
        {
            if (!_failed &&
                point == JusticePersistenceFaultPoint.BeforeWalFrameWrite)
            {
                _failed = true;
                throw new IOException(
                    "Verrou WAL transitoire injecté pendant la terminalisation.");
            }
        }
    }
}
