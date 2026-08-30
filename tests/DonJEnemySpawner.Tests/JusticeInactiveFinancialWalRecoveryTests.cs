using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeInactiveFinancialWalRecoveryTests
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags StaticFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void VoluntaryPaymentAttemptedWal_RestartedOnAnotherSlotRecoversOwnerWithoutReplay()
    {
        RunInactiveOwnerRecoveryScenario("VoluntaryFinePayment");
    }

    [TestMethod]
    public void FineDebitAttemptedWal_RestartedOnAnotherSlotRecoversOwnerWithoutReplay()
    {
        RunInactiveOwnerRecoveryScenario("FineDebit");
    }

    [TestMethod]
    public void TwoSlotsWithAttemptedVoluntaryWal_RecoverBothInactiveOwners()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            long[] generations = { 10L, 11L, 12L };
            SetField(script, "_justiceProfilePersistenceGenerations", generations);
            SetField(script, "_justicePersistenceRevision", 12L);

            string episode0 = "wanted:multi-voluntary:0";
            string episode1 = "wanted:multi-voluntary:1";
            string payment0 = "payment:00000000000000000000000000000010";
            string payment1 = "payment:00000000000000000000000000000011";
            ConfigureFinancialProfile(profiles[0], "VoluntaryFinePayment", episode0);
            ConfigureFinancialProfile(profiles[1], "VoluntaryFinePayment", episode1);
            profiles[0].CustodySnapshot = CreateFinancialCustodySnapshot(
                profiles[0],
                "VoluntaryFinePayment",
                payment0,
                FixedPreparedAtUtcTicks(),
                true);
            profiles[1].CustodySnapshot = CreateFinancialCustodySnapshot(
                profiles[1],
                "VoluntaryFinePayment",
                payment1,
                FixedPreparedAtUtcTicks() + 1L,
                true);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "multi-same-kind.wal"));
            AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[0],
                "VoluntaryFinePayment",
                payment0,
                10L,
                10L,
                FixedPreparedAtUtcTicks());
            AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[1],
                "VoluntaryFinePayment",
                payment1,
                11L,
                11L,
                FixedPreparedAtUtcTicks() + 1L);
            SetField(script, "_justiceWriteAheadLog", wal);

            Invoke(script, "RecoverJusticePersistenceFromWalIfRequired");

            AssertRecoveredAttemptedSnapshot(
                profiles[0].CustodySnapshot,
                "VoluntaryFinePayment");
            AssertRecoveredAttemptedSnapshot(
                profiles[1].CustodySnapshot,
                "VoluntaryFinePayment",
                1);
            Assert.IsNull(GetField<object>(script, "_justiceFineDebitIntent"));
            Assert.IsNull(
                GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
            Assert.AreEqual(12L, GetField<long>(script, "_justicePersistenceRevision"));
        });
    }

    [TestMethod]
    public void MixedFinancialWal_WithReverseSequenceOrder_AppliesByRevision()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 5L, 5L, 9L });
            SetField(script, "_justicePersistenceRevision", 9L);

            string fineEpisode = "custody:multi-mixed:0";
            string voluntaryEpisode = "wanted:multi-mixed:1";
            string payment = "payment:00000000000000000000000000000021";
            ConfigureFinancialProfile(profiles[0], "FineDebit", fineEpisode);
            ConfigureFinancialProfile(
                profiles[1],
                "VoluntaryFinePayment",
                voluntaryEpisode);
            profiles[0].CustodySnapshot = CreateFinancialCustodySnapshot(
                profiles[0],
                "FineDebit",
                fineEpisode,
                FixedPreparedAtUtcTicks() + 11L,
                false);
            profiles[1].CustodySnapshot = CreateFinancialCustodySnapshot(
                profiles[1],
                "VoluntaryFinePayment",
                payment,
                FixedPreparedAtUtcTicks() + 10L,
                false);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "multi-reverse-order.wal"));
            JusticeWalRecord highRevision = AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[0],
                "FineDebit",
                fineEpisode,
                11L,
                11L,
                FixedPreparedAtUtcTicks() + 11L);
            JusticeWalRecord lowRevision = AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[1],
                "VoluntaryFinePayment",
                payment,
                10L,
                10L,
                FixedPreparedAtUtcTicks() + 10L);
            Assert.IsTrue(highRevision.Sequence < lowRevision.Sequence);
            Assert.IsTrue(
                highRevision.PersistenceRevision > lowRevision.PersistenceRevision);
            SetField(script, "_justiceWriteAheadLog", wal);

            Invoke(script, "RecoverJusticePersistenceFromWalIfRequired");

            AssertRecoveredAttemptedSnapshot(
                profiles[0].CustodySnapshot,
                "FineDebit");
            AssertRecoveredAttemptedSnapshot(
                profiles[1].CustodySnapshot,
                "VoluntaryFinePayment",
                1);
            Assert.AreEqual(
                JusticeWalState.Attempted,
                wal.GetLatest(highRevision.TransactionId).State);
            Assert.AreEqual(
                JusticeWalState.Attempted,
                wal.GetLatest(lowRevision.TransactionId).State,
                "La révision basse ne doit pas être classée supersédée par l'ordre des séquences.");
            Assert.AreEqual(11L, GetField<long>(script, "_justicePersistenceRevision"));
        });
    }

    [TestMethod]
    public void TwoFinancialWalForSameSlot_AreRejectedBeforeAnyProfileMutation()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 10L, 5L, 9L });
            SetField(script, "_justicePersistenceRevision", 10L);
            ConfigureFinancialProfile(
                profiles[0],
                "VoluntaryFinePayment",
                "wanted:duplicate-slot");
            JusticeCustodyPersistenceSnapshot originalCustody =
                CreateFinancialCustodySnapshot(
                    profiles[0],
                    "VoluntaryFinePayment",
                    "payment:00000000000000000000000000000030",
                    FixedPreparedAtUtcTicks() + 30L,
                    false);
            profiles[0].CustodySnapshot = originalCustody;

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "duplicate-slot.wal"));
            JusticeWalRecord first = AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[0],
                "VoluntaryFinePayment",
                "payment:00000000000000000000000000000030",
                10L,
                10L,
                FixedPreparedAtUtcTicks() + 30L);
            JusticeWalRecord second = AppendAttemptedFinancialWal(
                wal,
                script,
                profiles[0],
                "VoluntaryFinePayment",
                "payment:00000000000000000000000000000031",
                11L,
                10L,
                FixedPreparedAtUtcTicks() + 31L);
            SetField(script, "_justiceWriteAheadLog", wal);

            TargetInvocationException exception =
                Assert.ThrowsException<TargetInvocationException>(() =>
                    Invoke(script, "RecoverJusticePersistenceFromWalIfRequired"));
            Assert.IsInstanceOfType(exception.InnerException, typeof(InvalidDataException));
            StringAssert.Contains(
                exception.InnerException.Message,
                "même profil et la même génération");
            Assert.AreSame(originalCustody, profiles[0].CustodySnapshot);
            Assert.IsNull(originalCustody.FineDebitIntent);
            Assert.IsNull(originalCustody.VoluntaryPaymentIntent);
            Assert.IsNull(GetField<object>(script, "_justiceFineDebitIntent"));
            Assert.IsNull(
                GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
            Assert.AreEqual(10L, GetField<long>(script, "_justicePersistenceRevision"));
            Assert.AreEqual(10L, GetField<long[]>(
                script,
                "_justiceProfilePersistenceGenerations")[0]);
            Assert.AreEqual(JusticeWalState.Attempted, wal.GetLatest(first.TransactionId).State);
            Assert.AreEqual(JusticeWalState.Attempted, wal.GetLatest(second.TransactionId).State);
        });
    }

    [TestMethod]
    public void SameSlotOlderAndCurrentVoluntaryWal_TerminalizesOldAndRestoresOnlyCurrent()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            JusticePlayerProfileState owner = profiles[0];
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "same-slot-voluntary-rotation.wal"));
            string oldPayment = "payment:00000000000000000000000000000040";
            string currentPayment = "payment:00000000000000000000000000000041";

            ConfigureFinancialProfile(
                owner,
                "VoluntaryFinePayment",
                "wanted:rotation:old");
            JusticeWalRecord oldRecord = AppendAttemptedFinancialWal(
                wal,
                script,
                owner,
                "VoluntaryFinePayment",
                oldPayment,
                10L,
                10L,
                FixedPreparedAtUtcTicks() + 40L);
            oldRecord = wal.Append(new JusticeWalRecord(
                oldRecord.TransactionId,
                oldRecord.OperationKind,
                oldRecord.ProfileSlot,
                JusticeWalState.Ambiguous,
                oldRecord.PersistenceRevision,
                oldRecord.CreatedAtUtcTicks,
                oldRecord.Fields));

            ConfigureFinancialProfile(
                owner,
                "VoluntaryFinePayment",
                "wanted:rotation:current");
            owner.CustodySnapshot = CreateFinancialCustodySnapshot(
                owner,
                "VoluntaryFinePayment",
                currentPayment,
                FixedPreparedAtUtcTicks() + 41L,
                true);
            JusticeWalRecord currentRecord = AppendAttemptedFinancialWal(
                wal,
                script,
                owner,
                "VoluntaryFinePayment",
                currentPayment,
                11L,
                11L,
                FixedPreparedAtUtcTicks() + 41L);
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 11L, 5L, 9L });
            SetField(script, "_justicePersistenceRevision", 11L);
            SetField(script, "_justiceWriteAheadLog", wal);
            int cashWriteCount = 0;
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    cashWriteCount++;
                    return true;
                }));

            Invoke(script, "RecoverJusticePersistenceFromWalIfRequired");

            Assert.AreEqual(
                JusticeWalState.Confirmed,
                wal.GetLatest(oldRecord.TransactionId).State);
            Assert.AreEqual(
                JusticeWalState.Attempted,
                wal.GetLatest(currentRecord.TransactionId).State);
            AssertRecoveredAttemptedSnapshot(
                owner.CustodySnapshot,
                "VoluntaryFinePayment");
            Assert.AreEqual(
                currentPayment,
                owner.CustodySnapshot.VoluntaryPaymentIntent.PaymentId);
            Assert.AreEqual(0, cashWriteCount, "La reprise ne doit rejouer aucun débit cash.");
        });
    }

    [TestMethod]
    public void SameSlotSupersededFineAndCurrentVoluntaryWal_RestoresOnlyCurrentKind()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            JusticePlayerProfileState owner = profiles[0];
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "same-slot-mixed-rotation.wal"));
            string oldEpisode = "custody:rotation:mixed:old";
            string currentPayment = "payment:00000000000000000000000000000051";

            ConfigureFinancialProfile(owner, "FineDebit", oldEpisode);
            JusticeWalRecord oldRecord = AppendPreparedFinancialWal(
                wal,
                script,
                owner,
                "FineDebit",
                oldEpisode,
                20L,
                20L,
                FixedPreparedAtUtcTicks() + 50L);

            ConfigureFinancialProfile(
                owner,
                "VoluntaryFinePayment",
                "wanted:rotation:mixed:current");
            owner.CustodySnapshot = CreateFinancialCustodySnapshot(
                owner,
                "VoluntaryFinePayment",
                currentPayment,
                FixedPreparedAtUtcTicks() + 51L,
                true);
            JusticeWalRecord currentRecord = AppendAttemptedFinancialWal(
                wal,
                script,
                owner,
                "VoluntaryFinePayment",
                currentPayment,
                21L,
                21L,
                FixedPreparedAtUtcTicks() + 51L);
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 21L, 5L, 9L });
            SetField(script, "_justicePersistenceRevision", 21L);
            SetField(script, "_justiceWriteAheadLog", wal);
            int cashWriteCount = 0;
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    cashWriteCount++;
                    return true;
                }));

            Invoke(script, "RecoverJusticePersistenceFromWalIfRequired");

            Assert.AreEqual(
                JusticeWalState.Rejected,
                wal.GetLatest(oldRecord.TransactionId).State);
            Assert.AreEqual(
                JusticeWalState.Attempted,
                wal.GetLatest(currentRecord.TransactionId).State);
            AssertRecoveredAttemptedSnapshot(
                owner.CustodySnapshot,
                "VoluntaryFinePayment");
            Assert.AreEqual(
                currentPayment,
                owner.CustodySnapshot.VoluntaryPaymentIntent.PaymentId);
            Assert.IsNull(owner.CustodySnapshot.FineDebitIntent);
            Assert.AreEqual(0, cashWriteCount, "La reprise ne doit rejouer aucun débit cash.");
        });
    }

    [TestMethod]
    public void SnapshotOlderThanTwoFinancialWalGenerations_IsRejectedBeforeMutation()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateHeadlessScript(2);
            JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles");
            JusticePlayerProfileState owner = profiles[0];
            ConfigureFinancialProfile(
                owner,
                "VoluntaryFinePayment",
                "wanted:two-lost-generations");
            JusticeCustodyPersistenceSnapshot originalCustody =
                CreateFinancialCustodySnapshot(
                    owner,
                    "VoluntaryFinePayment",
                    "payment:00000000000000000000000000000060",
                    FixedPreparedAtUtcTicks() + 60L,
                    false);
            owner.CustodySnapshot = originalCustody;
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 5L, 5L, 9L });
            SetField(script, "_justicePersistenceRevision", 9L);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "two-lost-generations.wal"));
            JusticeWalRecord first = AppendAttemptedFinancialWal(
                wal,
                script,
                owner,
                "VoluntaryFinePayment",
                "payment:00000000000000000000000000000060",
                10L,
                10L,
                FixedPreparedAtUtcTicks() + 60L);
            JusticeWalRecord second = AppendAttemptedFinancialWal(
                wal,
                script,
                owner,
                "VoluntaryFinePayment",
                "payment:00000000000000000000000000000061",
                11L,
                11L,
                FixedPreparedAtUtcTicks() + 61L);
            SetField(script, "_justiceWriteAheadLog", wal);

            TargetInvocationException exception =
                Assert.ThrowsException<TargetInvocationException>(() =>
                    Invoke(script, "RecoverJusticePersistenceFromWalIfRequired"));

            Assert.IsInstanceOfType(exception.InnerException, typeof(InvalidDataException));
            StringAssert.Contains(exception.InnerException.Message, "plusieurs générations");
            Assert.AreSame(originalCustody, owner.CustodySnapshot);
            Assert.AreEqual(5L, GetField<long[]>(
                script,
                "_justiceProfilePersistenceGenerations")[0]);
            Assert.AreEqual(9L, GetField<long>(script, "_justicePersistenceRevision"));
            Assert.AreEqual(JusticeWalState.Attempted, wal.GetLatest(first.TransactionId).State);
            Assert.AreEqual(JusticeWalState.Attempted, wal.GetLatest(second.TransactionId).State);
        });
    }

    private static void ConfigureFinancialProfile(
        JusticePlayerProfileState profile,
        string operationKind,
        string episode)
    {
        Assert.IsNotNull(profile);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = operationKind == "FineDebit"
                ? JusticePhase.Captured
                : JusticePhase.Wanted,
            WantedEpisodeId = operationKind == "FineDebit"
                ? "wanted:" + episode
                : episode,
            CustodyEpisodeId = operationKind == "FineDebit"
                ? episode
                : string.Empty
        };
        string incidentId = "incident:" + profile.Slot.ToString(
            CultureInfo.InvariantCulture) + ":" + operationKind;
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + incidentId,
            IncidentId = incidentId,
            EpisodeId = state.WantedEpisodeId,
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 12,
            Fine = 600L,
            SentenceSeconds = operationKind == "FineDebit"
                ? GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds")
                : 0,
            IsAdjudicated = operationKind == "FineDebit"
        });
        state.RecalculateTotals();
        profile.CaseState = state;
        profile.RecordState = new JusticeRecordState();
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateFinancialCustodySnapshot(
            JusticePlayerProfileState profile,
            string operationKind,
            string operationIdentity,
            long preparedAtUtcTicks,
            bool includePreparedIntent)
    {
        JusticeFineDebitPersistenceSnapshot fineIntent = null;
        JusticeVoluntaryPaymentPersistenceSnapshot voluntaryIntent = null;
        bool fineDebit = operationKind == "FineDebit";
        if (includePreparedIntent && fineDebit)
        {
            int sentence = profile.CaseState.SentenceSeconds;
            bool stationPlanned = sentence <
                GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds");
            fineIntent = new JusticeFineDebitPersistenceSnapshot(
                operationIdentity,
                profile.Slot,
                600L,
                true,
                preparedAtUtcTicks,
                600,
                1000,
                400,
                (int)InvokeStatic(
                    "CalculateJusticeSentenceAfterFineConversion",
                    sentence,
                    0L,
                    stationPlanned),
                (int)InvokeStatic(
                    "CalculateJusticeSentenceAfterFineConversion",
                    sentence,
                    600L,
                    stationPlanned),
                stationPlanned,
                false,
                0,
                (int)JusticePaymentResolution.Prepared,
                0L,
                0L,
                0L);
        }
        else if (includePreparedIntent)
        {
            voluntaryIntent = new JusticeVoluntaryPaymentPersistenceSnapshot(
                operationIdentity,
                profile.Slot,
                600L,
                600,
                1000,
                400,
                0L,
                preparedAtUtcTicks,
                false,
                0L,
                0,
                (int)JusticePaymentResolution.Prepared,
                0L,
                false);
        }

        return new JusticeCustodyPersistenceSnapshot(
            fineDebit,
            0,
            false,
            false,
            0,
            0,
            false,
            false,
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            fineDebit ? profile.LastCanonicalPlayerModel : 0,
            fineDebit ? profile.Slot : -1,
            GetStaticField<int>("JusticeUnarmedHash"),
            false,
            false,
            fineIntent,
            voluntaryIntent,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static JusticeWalRecord AppendAttemptedFinancialWal(
        JusticeWriteAheadLog wal,
        object script,
        JusticePlayerProfileState profile,
        string operationKind,
        string operationIdentity,
        long persistenceRevision,
        long profileGeneration,
        long createdAtUtcTicks)
    {
        JusticeWalRecord prepared = CreateFinancialWalRecord(
            script,
            profile,
            operationKind,
            operationIdentity,
            JusticeWalState.Prepared,
            persistenceRevision,
            profileGeneration,
            createdAtUtcTicks);
        wal.Append(prepared);
        return wal.Append(CreateFinancialWalRecord(
            script,
            profile,
            operationKind,
            operationIdentity,
            JusticeWalState.Attempted,
            persistenceRevision,
            profileGeneration,
            createdAtUtcTicks));
    }

    private static JusticeWalRecord AppendPreparedFinancialWal(
        JusticeWriteAheadLog wal,
        object script,
        JusticePlayerProfileState profile,
        string operationKind,
        string operationIdentity,
        long persistenceRevision,
        long profileGeneration,
        long createdAtUtcTicks)
    {
        return wal.Append(CreateFinancialWalRecord(
            script,
            profile,
            operationKind,
            operationIdentity,
            JusticeWalState.Prepared,
            persistenceRevision,
            profileGeneration,
            createdAtUtcTicks));
    }

    private static JusticeWalRecord CreateFinancialWalRecord(
        object script,
        JusticePlayerProfileState profile,
        string operationKind,
        string operationIdentity,
        JusticeWalState state,
        long persistenceRevision,
        long profileGeneration,
        long createdAtUtcTicks)
    {
        string identityKey = (string)Invoke(
            script,
            "CreateJusticeProfileIdentityKey",
            profile);
        if (operationKind == "VoluntaryFinePayment")
        {
            return new JusticeWalRecord(
                "financial:" + profile.Slot.ToString(CultureInfo.InvariantCulture) +
                ":VoluntaryFinePayment:" + operationIdentity,
                operationKind,
                profile.Slot,
                state,
                persistenceRevision,
                createdAtUtcTicks,
                new[]
                {
                    WalField("paymentId", operationIdentity),
                    WalField("slot", profile.Slot),
                    WalField("fineBefore", 600L),
                    WalField("paidBefore", 0L),
                    WalField("debitAmount", 600),
                    WalField("cashBefore", 1000),
                    WalField("cashAfter", 400),
                    WalField("disputeBefore", 0L),
                    WalField("preparedAt", createdAtUtcTicks),
                    WalField("caseEpisode", profile.CaseState.WantedEpisodeId),
                    WalField("profileGeneration", profileGeneration),
                    WalField("identityKey", identityKey),
                    WalField("schemaMajor", JusticeXmlPersistenceCodec.SchemaMajor)
                });
        }

        int sentence = profile.CaseState.SentenceSeconds;
        bool stationPlanned = sentence <
            GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds");
        return new JusticeWalRecord(
            "financial:" + profile.Slot.ToString(CultureInfo.InvariantCulture) +
            ":FineDebit:" + operationIdentity,
            operationKind,
            profile.Slot,
            state,
            persistenceRevision,
            createdAtUtcTicks,
            new[]
            {
                WalField("episodeId", operationIdentity),
                WalField("slot", profile.Slot),
                WalField("fineAmount", 600L),
                WalField("cashPlan", true),
                WalField("preparedAt", createdAtUtcTicks),
                WalField("debitAmount", 600),
                WalField("cashBefore", 1000),
                WalField("cashAfter", 400),
                WalField(
                    "sentenceDebited",
                    (int)InvokeStatic(
                        "CalculateJusticeSentenceAfterFineConversion",
                        sentence,
                        0L,
                        stationPlanned)),
                WalField(
                    "sentenceConverted",
                    (int)InvokeStatic(
                        "CalculateJusticeSentenceAfterFineConversion",
                        sentence,
                        600L,
                        stationPlanned)),
                WalField("stationPlanned", stationPlanned),
                WalField("disputeBefore", 0L),
                WalField("sentenceBefore", sentence),
                WalField("custodyEpisode", profile.CaseState.CustodyEpisodeId),
                WalField("profileGeneration", profileGeneration),
                WalField("identityKey", identityKey),
                WalField("schemaMajor", JusticeXmlPersistenceCodec.SchemaMajor)
            });
    }

    private static JusticePersistenceField WalField(string path, string value)
    {
        return new JusticePersistenceField(path, value);
    }

    private static JusticePersistenceField WalField(string path, bool value)
    {
        return WalField(path, value ? "true" : "false");
    }

    private static JusticePersistenceField WalField(string path, int value)
    {
        return WalField(path, value.ToString(CultureInfo.InvariantCulture));
    }

    private static JusticePersistenceField WalField(string path, long value)
    {
        return WalField(path, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void RunInactiveOwnerRecoveryScenario(string operationKind)
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = null;
            object inactiveRestart = null;
            object ownerRestart = null;
            try
            {
                writer = CreateHeadlessScript(0);
                ConfigurePreparedFinancialIntent(writer, operationKind);
                FlushAndAwait(writer);

                long snapshotRevision =
                    GetField<long>(writer, "_justicePersistenceRevision");
                long[] generations = GetField<long[]>(
                    writer,
                    "_justiceProfilePersistenceGenerations");
                JusticePlayerProfileState[] writerProfiles =
                    GetField<JusticePlayerProfileState[]>(
                        writer,
                        "_justicePlayerProfiles");
                string identityKey = (string)Invoke(
                    writer,
                    "CreateJusticeProfileIdentityKey",
                    writerProfiles[0]);
                List<JusticePersistenceField> walFields =
                    (List<JusticePersistenceField>)Invoke(
                        writer,
                        "CreateJusticeFinancialWalFields",
                        operationKind,
                        generations[0],
                        identityKey);
                string transactionId = (string)Invoke(
                    writer,
                    "CreateJusticeFinancialTransactionId",
                    operationKind);
                long preparedAt = (long)Invoke(
                    writer,
                    "GetJusticeFinancialPreparedAtUtcTicks",
                    operationKind);
                JusticeWriteAheadLog wal =
                    GetField<JusticeWriteAheadLog>(writer, "_justiceWriteAheadLog");
                wal.Append(new JusticeWalRecord(
                    transactionId,
                    operationKind,
                    0,
                    JusticeWalState.Prepared,
                    snapshotRevision,
                    preparedAt,
                    walFields));
                wal.Append(new JusticeWalRecord(
                    transactionId,
                    operationKind,
                    0,
                    JusticeWalState.Attempted,
                    snapshotRevision,
                    preparedAt,
                    walFields));

                // Je ferme seulement le writer : le WAL Attempted et le snapshot
                // Prepared représentent exactement la coupure après l'effet cash.
                ShutdownPersistence(writer);
                writer = null;

                string statePath = Path.Combine(directory, "_justice_state.xml");
                inactiveRestart = CreateHeadlessScript(1);
                Assert.IsTrue(
                    (bool)Invoke(
                        inactiveRestart,
                        "TryReadJusticeStateFile",
                        statePath),
                    "Le redémarrage doit relire le snapshot multi-profils.");
                Assert.AreEqual(
                    1,
                    GetField<int>(
                        inactiveRestart,
                        "_justiceActivePlayerProfileSlot"),
                    "Le héros présent au redémarrage doit rester autoritaire.");

                Invoke(inactiveRestart, "InitializeJusticePersistenceServices");
                Assert.IsFalse(
                    GetField<bool>(
                        inactiveRestart,
                        "_justicePersistenceServicesUnavailable"),
                    GetField<string>(
                        inactiveRestart,
                        "_justicePersistenceLastError"));
                Assert.IsNull(
                    GetField<object>(inactiveRestart, "_justiceFineDebitIntent"),
                    "Le FineDebit du slot 0 ne doit pas contaminer le slot 1.");
                Assert.IsNull(
                    GetField<object>(
                        inactiveRestart,
                        "_justiceVoluntaryFinePaymentIntent"),
                    "Le paiement du slot 0 ne doit pas contaminer le slot 1.");

                JusticePlayerProfileState[] recoveredProfiles =
                    GetField<JusticePlayerProfileState[]>(
                        inactiveRestart,
                        "_justicePlayerProfiles");
                AssertRecoveredAttemptedSnapshot(
                    recoveredProfiles[0].CustodySnapshot,
                    operationKind);

                // Je durcis la reprise alors que Franklin reste actif. Le writer
                // doit accepter le profil Michael modifié sans changer de contexte.
                FlushAndAwait(inactiveRestart);
                ShutdownPersistence(inactiveRestart);
                inactiveRestart = null;

                ownerRestart = CreateHeadlessScript(0);
                Assert.IsTrue((bool)Invoke(
                    ownerRestart,
                    "TryReadJusticeStateFile",
                    statePath));
                Invoke(ownerRestart, "InitializeJusticePersistenceServices");
                Assert.IsFalse(
                    GetField<bool>(
                        ownerRestart,
                        "_justicePersistenceServicesUnavailable"),
                    GetField<string>(ownerRestart, "_justicePersistenceLastError"));

                object recoveredIntent = GetRecoveredRuntimeIntent(
                    ownerRestart,
                    operationKind);
                Assert.IsNotNull(recoveredIntent);
                Assert.IsTrue(
                    GetMember<bool>(recoveredIntent, "DebitAttempted"),
                    "Attempted est le jeton durable qui interdit un second SET.");
                Assert.AreNotEqual(
                    0L,
                    GetMember<long>(recoveredIntent, "AttemptedAtUtcTicks"));

                int cashWriteCount = 0;
                SetField(
                    ownerRestart,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => 400));
                SetField(
                    ownerRestart,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cashWriteCount++;
                        return true;
                    }));

#if DONJ_STUB_API
                ResumeRecoveredIntentOnStub(ownerRestart, operationKind);
#endif
                Assert.AreEqual(
                    0,
                    cashWriteCount,
                    "Une intention restaurée Attempted ne doit jamais réémettre STAT_SET_INT.");
            }
            finally
            {
                ShutdownPersistence(ownerRestart);
                ShutdownPersistence(inactiveRestart);
                ShutdownPersistence(writer);
            }
        });
    }

    private static void ConfigurePreparedFinancialIntent(
        object script,
        string operationKind)
    {
        JusticeCaseState state = GetField<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        string incidentId = "incident:inactive-financial";
        string wantedEpisode = "wanted:inactive-financial";
        int sentence = operationKind == "FineDebit"
            ? GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds")
            : 0;
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + incidentId,
            IncidentId = incidentId,
            EpisodeId = wantedEpisode,
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 12,
            Fine = 600L,
            SentenceSeconds = sentence,
            IsAdjudicated = operationKind == "FineDebit"
        });
        state.RecalculateTotals();
        state.WantedEpisodeId = wantedEpisode;
        SetField(script, "_justiceEnabled", true);

        if (operationKind == "VoluntaryFinePayment")
        {
            state.Phase = JusticePhase.Wanted;
            object intent = CreateNested("JusticeVoluntaryFinePaymentIntent");
            SetMember(intent, "PaymentId", "payment:00000000000000000000000000000001");
            SetMember(intent, "Slot", 0);
            SetMember(intent, "FineBefore", 600L);
            SetMember(intent, "DebitAmount", 600);
            SetMember(intent, "CashBefore", 1000);
            SetMember(intent, "CashAfter", 400);
            SetMember(intent, "FineInDisputeBefore", 0L);
            SetMember(intent, "PreparedAtUtcTicks", FixedPreparedAtUtcTicks());
            SetField(script, "_justiceVoluntaryFinePaymentIntent", intent);
            return;
        }

        const string custodyEpisode = "custody:inactive-financial";
        state.Phase = JusticePhase.Captured;
        state.CustodyEpisodeId = custodyEpisode;
        bool stationPlanned = sentence <
            GetStaticField<int>("JusticeCustodyPrisonThresholdSeconds");
        int sentenceIfDebited = (int)InvokeStatic(
            "CalculateJusticeSentenceAfterFineConversion",
            sentence,
            0L,
            stationPlanned);
        int sentenceIfConverted = (int)InvokeStatic(
            "CalculateJusticeSentenceAfterFineConversion",
            sentence,
            600L,
            stationPlanned);
        object fineIntent = CreateNested("JusticeFineDebitIntent");
        SetMember(fineIntent, "EpisodeId", custodyEpisode);
        SetMember(fineIntent, "Slot", 0);
        SetMember(fineIntent, "FineAmount", 600L);
        SetMember(fineIntent, "CashPlanPrepared", true);
        SetMember(fineIntent, "PreparedAtUtcTicks", FixedPreparedAtUtcTicks());
        SetMember(fineIntent, "DebitAmount", 600);
        SetMember(fineIntent, "CashBefore", 1000);
        SetMember(fineIntent, "CashAfter", 400);
        SetMember(fineIntent, "SentenceIfDebited", sentenceIfDebited);
        SetMember(fineIntent, "SentenceIfConverted", sentenceIfConverted);
        SetMember(fineIntent, "StationPlanned", stationPlanned);
        SetMember(fineIntent, "FineInDisputeBefore", 0L);
        SetField(script, "_justiceFineDebitIntent", fineIntent);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyPlayerModelHash", MichaelModelHash());
    }

    private static void AssertRecoveredAttemptedSnapshot(
        JusticeCustodyPersistenceSnapshot custody,
        string operationKind,
        int expectedSlot = 0)
    {
        Assert.IsNotNull(custody);
        if (operationKind == "VoluntaryFinePayment")
        {
            Assert.IsNull(custody.FineDebitIntent);
            Assert.IsNotNull(custody.VoluntaryPaymentIntent);
            Assert.AreEqual(expectedSlot, custody.VoluntaryPaymentIntent.Slot);
            Assert.IsTrue(custody.VoluntaryPaymentIntent.DebitAttempted);
            Assert.AreNotEqual(
                0L,
                custody.VoluntaryPaymentIntent.AttemptedAtUtcTicks);
            return;
        }

        Assert.IsNull(custody.VoluntaryPaymentIntent);
        Assert.IsNotNull(custody.FineDebitIntent);
        Assert.AreEqual(expectedSlot, custody.FineDebitIntent.Slot);
        Assert.IsTrue(custody.FineDebitIntent.DebitAttempted);
        Assert.AreNotEqual(0L, custody.FineDebitIntent.AttemptedAtUtcTicks);
    }

#if DONJ_STUB_API
    private static void ResumeRecoveredIntentOnStub(
        object script,
        string operationKind)
    {
        GTA.StubRuntime.Reset();
        GTA.Game.Player.Character = new GTA.Ped
        {
            Handle = 501,
            Model = new GTA.Model(MichaelModelHash()),
            IsDead = false
        };
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            object intent = GetRecoveredRuntimeIntent(script, operationKind);
            if (intent == null)
            {
                return;
            }

            long queued = GetField<long>(
                script,
                "_justiceLastQueuedPersistenceRevision");
            if (queued > 0L)
            {
                Assert.IsTrue((bool)Invoke(
                    script,
                    "JusticeAwaitQueuedPersistenceForTests"));
            }
            if (operationKind == "VoluntaryFinePayment")
            {
                SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
                Invoke(script, "ResumeJusticeVoluntaryFinePayment");
            }
            else
            {
                SetField(script, "_justiceNextFineCashReadAttemptAt", 0);
                Invoke(script, "ResumeJusticeFineDebitIntent");
            }
        }
    }
#endif

    private static object GetRecoveredRuntimeIntent(
        object script,
        string operationKind)
    {
        return operationKind == "VoluntaryFinePayment"
            ? GetField<object>(script, "_justiceVoluntaryFinePaymentIntent")
            : GetField<object>(script, "_justiceFineDebitIntent");
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
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => activeSlot));
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

    private static int MichaelModelHash()
    {
        return GTA.Game.GenerateHash("player_zero");
    }

    private static long FixedPreparedAtUtcTicks()
    {
        return new DateTime(2026, 8, 30, 8, 0, 0, DateTimeKind.Utc).Ticks;
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
            // Je laisse l'assertion d'origine expliquer l'échec du scénario.
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

    private static object InvokeStatic(string methodName, params object[] arguments)
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

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = ScriptType.GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }

    private static T GetMember<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        return (T)field.GetValue(target);
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
            "DonJInactiveFinancialWal-" + Guid.NewGuid().ToString("N"));
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
