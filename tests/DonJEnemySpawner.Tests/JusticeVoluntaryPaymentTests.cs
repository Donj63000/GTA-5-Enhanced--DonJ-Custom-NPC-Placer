using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeVoluntaryPaymentTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public void VoluntaryPayment_PaysAvailableCashOnceAndKeepsTheRemainingDebt()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 800;
            int writeCount = 0;
            object script = CreatePaymentScript(1200L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));

            FlushAndAwait(script);

            Invoke(script, "RequestJusticeVoluntaryFinePayment");
            CompleteVoluntaryPayment(script);

            Assert.AreEqual(
                1,
                writeCount,
                "Le WAL ne doit jamais réémettre STAT_SET_INT. Statut=" +
                GetField<string>(script, "_statusText"));
            Assert.AreEqual(0, cash);
            Assert.AreEqual(400L, GetCase(script).FineDue);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
            string statePath = Path.Combine(directory, "_justice_state.xml");
            string persisted = File.ReadAllText(statePath);
            Assert.IsFalse(
                persisted.Contains("VoluntaryFinePaymentIntent"),
                "L'intention acquittée doit disparaître après son commit durable.");
            StringAssert.Contains(persisted, "voluntaryFinePaid=\"800\"");

            object reloaded = CreatePaymentScript(1L);
            Assert.IsTrue((bool)Invoke(reloaded, "TryReadJusticeStateFile", statePath));
            Assert.AreEqual(400L, GetCase(reloaded).FineDue);
            Assert.AreEqual(800L, GetCase(reloaded).VoluntaryFinePaid);
        });
    }

    [TestMethod]
    public void VoluntaryPayment_ExplicitRejectionNeverReducesDebtOrRetriesTheWrite()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int writeCount = 0;
            object script = CreatePaymentScript(1200L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => 1500));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    return false;
                }));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");
            CompleteVoluntaryPayment(script);

            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(1200L, GetCase(script).FineDue);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
        });
    }

    [TestMethod]
    public void VoluntaryPayment_AmbiguousAcceptedWriteIsReconciledWithoutSecondDebit()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 1000;
            int writeCount = 0;
            object script = CreatePaymentScript(600L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return null;
                }));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");
            CompleteVoluntaryPayment(script);

            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(400, cash);
            Assert.AreEqual(0L, GetCase(script).FineDue);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
        });
    }

    [TestMethod]
    public void VoluntaryPayment_UnreadableCashAfterUnknownWriteTimesOutWithoutReplay()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cashReadCount = 0;
            int writeCount = 0;
            object script = CreatePaymentScript(600L);
            SetField(
                script,
                "_justiceCashReadOverride",
                new Func<int, int?>(slot => ++cashReadCount <= 2 ? (int?)1000 : null));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    return null;
                }));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");

            AwaitQueuedPersistence(script);
            SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Assert.IsFalse((bool)Invoke(script, "ResumeJusticeVoluntaryFinePayment"));

            object intent = GetField<object>(script, "_justiceVoluntaryFinePaymentIntent");
            Assert.IsNotNull(intent, "L'intention ambiguë doit attendre sa fenêtre de réconciliation.");
            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(600L, GetCase(script).FineDue);

            SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Assert.IsFalse(
                (bool)Invoke(script, "ResumeJusticeVoluntaryFinePayment"),
                "Avant le timeout, une lecture indisponible doit conserver l'intention sans rejouer le débit.");
            Assert.AreEqual(1, writeCount);
            Assert.AreSame(intent, GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));

            SetNestedField(
                intent,
                "AttemptedAtUtcTicks",
                DateTime.UtcNow.Ticks - JusticePolicy.FineDebitAmbiguityTimeoutTicks);
            AwaitQueuedPersistence(script);
            SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Assert.IsTrue((bool)Invoke(script, "ResumeJusticeVoluntaryFinePayment"));
            AwaitQueuedPersistence(script);

            Assert.AreEqual(1, writeCount, "Une écriture Unknown déjà tentée ne doit jamais être rejouée.");
            Assert.AreEqual(0L, GetCase(script).FineDue);
            Assert.AreEqual(0L, GetCase(script).VoluntaryFinePaid);
            Assert.AreEqual(600L, GetCase(script).FineInDispute);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));

            string statePath = Path.Combine(directory, "_justice_state.xml");
            object reloaded = CreatePaymentScript(1L);
            Assert.IsTrue((bool)Invoke(reloaded, "TryReadJusticeStateFile", statePath));
            Assert.AreEqual(0L, GetCase(reloaded).FineDue);
            Assert.AreEqual(0L, GetCase(reloaded).VoluntaryFinePaid);
            Assert.AreEqual(600L, GetCase(reloaded).FineInDispute);
            Assert.IsNull(GetField<object>(reloaded, "_justiceVoluntaryFinePaymentIntent"));
        });
    }

    [TestMethod]
    public void VoluntaryPayment_PartialAndFullBalancesRemainValidThroughCaptureAndReload()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = CreatePaymentScript(1000L);
            JusticeCaseState state = GetCase(writer);
            JusticeRecordState record = GetField<JusticeRecordState>(writer, "_justiceRecordState");
            string custodyEpisode = "custody:payment-before-capture";
            string convictionId = "conviction:" + custodyEpisode;

            state.VoluntaryFinePaid = 800L;
            state.FineDue = 200L;
            state.Phase = JusticePhase.Captured;
            state.CustodyEpisodeId = custodyEpisode;
            state.HasWarrant = false;
            state.Charges[0].IsAdjudicated = true;
            state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.Capture,
                custodyEpisode));
            state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.ApplyConviction,
                custodyEpisode));

            JusticeConviction conviction = new JusticeConviction
            {
                ConvictionId = convictionId,
                JudgedAtUtc = new DateTime(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc),
                Severity = JusticeSeverity.Misdemeanor,
                Score = 12,
                Fine = 1000L,
                SentenceSeconds = 0
            };
            conviction.Charges.Add(new JusticeConvictionChargeSummary
            {
                Kind = JusticeCrimeKind.VehicleTheft,
                DisplayName = "Vol de véhicule vide",
                Points = 12,
                Fine = 1000L,
                SentenceSeconds = 0,
                CircumstancesWerePersisted = true
            });
            record.Convictions.Add(conviction);
            record.AppliedConvictionIds.Add(convictionId);
            record.PinnedConvictionId = convictionId;
            SetField(writer, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetField(writer, "_justiceCustodyPlayerSlot", 0);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            object partialReader = CreatePaymentScript(1L);
            Assert.IsTrue((bool)Invoke(partialReader, "TryReadJusticeStateFile", path));
            Assert.AreEqual(200L, GetCase(partialReader).FineDue);
            Assert.AreEqual(800L, GetCase(partialReader).VoluntaryFinePaid);

            state.VoluntaryFinePaid = 1000L;
            state.FineDue = 0L;
            FlushAndAwait(writer);
            object fullReader = CreatePaymentScript(1L);
            Assert.IsTrue((bool)Invoke(fullReader, "TryReadJusticeStateFile", path));
            Assert.AreEqual(0L, GetCase(fullReader).FineDue);
            Assert.AreEqual(1000L, GetCase(fullReader).VoluntaryFinePaid);
            Assert.AreEqual(JusticePhase.Captured, GetCase(fullReader).Phase);
        });
    }

    [TestMethod]
    public void VoluntaryPayment_RefusesAnInactiveSelectedCharacter()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int writeCount = 0;
            object script = CreatePaymentScript(500L);
            SetField(script, "_justiceMenuSelectedProfileSlot", 1);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => 1000));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    return true;
                }));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");

            Assert.AreEqual(0, writeCount);
            Assert.AreEqual(500L, GetCase(script).FineDue);
        });
    }

    [TestMethod]
    public void VoluntaryPayment_ModalCapturesTheDisplayedHeroAndDebtBeforeConfirmation()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreatePaymentScript(500L);
            SetField(script, "_justiceMenuSelectedProfileSlot", 1);

            Invoke(script, "RequestJusticeSelectedProfileFinePaymentConfirmation");
            Assert.IsNull(GetField<object>(script, "_pendingDangerAction"));

            SetField(script, "_justiceMenuSelectedProfileSlot", 0);
            Invoke(script, "RequestJusticeSelectedProfileFinePaymentConfirmation");
            Assert.AreEqual(
                "JusticePayFine",
                GetField<object>(script, "_pendingDangerAction").ToString());
            Assert.AreEqual(0, GetField<int>(script, "_pendingDangerJusticeProfileSlot"));
            Assert.AreEqual(
                "Michael",
                GetField<string>(script, "_pendingDangerJusticeProfileDisplay"));
            Assert.AreEqual(
                "500$",
                GetField<string>(script, "_pendingDangerJusticeFineDisplay"));
            Assert.AreEqual(500L, GetField<long>(script, "_pendingDangerJusticeFineAmount"));

            SetField(script, "_justiceMenuSelectedProfileSlot", 1);
            Assert.AreEqual(0, GetField<int>(script, "_pendingDangerJusticeProfileSlot"));
            Assert.AreEqual(
                "Michael",
                GetField<string>(script, "_pendingDangerJusticeProfileDisplay"));
            Assert.AreEqual(
                "500$",
                GetField<string>(script, "_pendingDangerJusticeFineDisplay"));
            Assert.AreEqual(500L, GetField<long>(script, "_pendingDangerJusticeFineAmount"));
            Invoke(script, "CancelPendingDangerAction");
        });
    }

    [TestMethod]
    public void VoluntaryPayment_DebtIncreaseAfterDisplayRequiresAnewConfirmation()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 5000;
            int writeCount = 0;
            object script = CreatePaymentScript(500L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));

            Invoke(script, "RequestJusticeSelectedProfileFinePaymentConfirmation");
            Assert.AreEqual(500L, GetField<long>(script, "_pendingDangerJusticeFineAmount"));

            JusticeCaseState state = GetCase(script);
            state.Charges[0].Fine = 3000L;
            state.RecalculateTotals();
            Assert.AreEqual(3000L, state.FineDue);

            Invoke(script, "ConfirmPendingDangerAction");

            Assert.AreEqual(0, writeCount);
            Assert.AreEqual(5000, cash);
            Assert.AreEqual(3000L, state.FineDue);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
            StringAssert.Contains(GetField<string>(script, "_statusText"), "confirmez à nouveau");
        });
    }

    [TestMethod]
    public void VoluntaryPayment_ZeroDebtDisplayNeverOffersPayment()
    {
        object script = CreatePaymentScript(0L);

        string display = (string)Invoke(script, "GetJusticeSelectedFinePaymentDisplay");

        Assert.AreEqual("0$ · aucune dette", display);
        Assert.IsFalse(display.Contains("payer"));
    }

    [TestMethod]
    public void VoluntaryPayment_CancelledPreparedIntentCannotBeResurrectedFromBackup()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 1000;
            int writeCount = 0;
            object writer = CreatePaymentScript(600L);
            SetField(writer, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                writer,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));
            SetField(
                writer,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 3));

            // Je rends l'intention préparée durable, puis je bloque sa première
            // reprise avant toute lecture ou écriture cash.
            Invoke(writer, "RequestJusticeVoluntaryFinePayment");
            Assert.AreEqual(0, writeCount);
            Assert.IsNotNull(GetField<object>(writer, "_justiceVoluntaryFinePaymentIntent"));
            AwaitQueuedPersistence(writer);

            SetField(writer, "_justiceStateFlushFailureOverride", null);
            SetField(writer, "_justiceNextStateFlushAttemptAtMs", 0L);
            SetField(writer, "_justiceMonotonicTimeMs", 2000L);
            SetField(writer, "_justiceNextVoluntaryPaymentResumeAt", 0);
            cash = 900;

            Assert.IsTrue(
                (bool)Invoke(writer, "ResumeJusticeVoluntaryFinePayment"),
                "L'abandon Prepared doit converger après la barrière disque.");
            AwaitQueuedPersistence(writer);
            Assert.AreEqual(0, writeCount, "Un solde modifié doit annuler sans appeler le writer cash.");
            Assert.AreEqual(600L, GetCase(writer).FineDue);
            Assert.IsNull(GetField<object>(writer, "_justiceVoluntaryFinePaymentIntent"));

            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            Assert.IsTrue(File.Exists(primary), "Le primaire final doit exister.");
            Assert.IsTrue(File.Exists(backup), "Le backup final doit exister.");
            Assert.IsFalse(File.ReadAllText(primary).Contains("VoluntaryFinePaymentIntent"));
            Assert.IsTrue(
                File.ReadAllText(backup).Contains("VoluntaryFinePaymentIntent"),
                "Le backup atomique peut conserver l'état terminal précédent sans réintroduire de débit.");

            File.WriteAllText(primary, "<JusticeState version='1'><broken>");
            object reloaded = CreatePaymentScript(1L);
            SetField(reloaded, "_justiceCashReadOverride", new Func<int, int?>(slot => 1000));
            SetField(
                reloaded,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    return true;
                }));

            Assert.IsTrue(
                (bool)Invoke(reloaded, "TryLoadJusticeState", false),
                "Le fallback backup doit rester chargeable.");
            Assert.AreEqual(600L, GetCase(reloaded).FineDue);
            CompleteVoluntaryPayment(reloaded);
            Assert.IsNull(GetField<object>(reloaded, "_justiceVoluntaryFinePaymentIntent"));
            Assert.AreEqual(0, writeCount, "Le fallback backup ne doit jamais recréer un débit annulé.");
        });
    }

    [TestMethod]
    public void VoluntaryPayment_FirstPrecommitFailureKeepsIntentAndRetriesWithoutDoubleDebit()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 900;
            int writeCount = 0;
            object script = CreatePaymentScript(600L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 1));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");

            Assert.AreEqual(0, writeCount, "Un WAL non persisté ne doit jamais débiter le joueur.");
            Assert.AreEqual(600L, GetCase(script).FineDue);
            Assert.IsNotNull(
                GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"),
                "L'intention doit rester reprenable au lieu d'être annoncée comme annulée.");
            StringAssert.Contains(GetField<string>(script, "_statusText"), "Paiement en attente");

            SetField(script, "_justiceStateFlushFailureOverride", null);
            SetField(script, "_justiceMonotonicTimeMs", 1000L);
            CompleteVoluntaryPayment(script);

            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(300, cash);
            Assert.AreEqual(0L, GetCase(script).FineDue);
            Assert.IsNull(GetField<object>(script, "_justiceVoluntaryFinePaymentIntent"));
        });
    }

    [TestMethod]
    public void VoluntaryPayment_FineDuePreconditionIsDurableBeforeAttemptedWal()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 1000;
            int writeCount = 0;
            object script = CreatePaymentScript(100L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));
            FlushAndAwait(script);

            JusticeCaseState state = GetCase(script);
            state.Charges[0].Fine = 600L;
            state.RecalculateTotals();
            Assert.AreEqual(600L, state.FineDue);

            Invoke(script, "RequestJusticeVoluntaryFinePayment");

            Assert.AreEqual(0, writeCount);
            string walPath = Path.Combine(directory, "_justice_state.wal");
            Assert.IsFalse(
                File.Exists(walPath) &&
                JusticeWriteAheadLog.Recover(walPath).Records.Any(record =>
                    record.State == JusticeWalState.Attempted),
                "Attempted ne doit jamais précéder le snapshot qui porte FineDue=600.");

            AwaitQueuedPersistence(script);
            object durable = CreatePaymentScript(1L);
            Assert.IsTrue((bool)Invoke(
                durable,
                "TryReadJusticeStateFile",
                Path.Combine(directory, "_justice_state.xml")));
            Assert.AreEqual(600L, GetCase(durable).FineDue);
            Assert.IsNotNull(GetField<object>(durable, "_justiceVoluntaryFinePaymentIntent"));

            CompleteVoluntaryPayment(script);
            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(400, cash);
        });
    }

    [TestMethod]
    public void VoluntaryPayment_LostPreparedAcknowledgementRetriesWithoutDoubleDebit()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 1000;
            int writeCount = 0;
            object writer = CreatePaymentScript(700L);
            SetField(writer, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                writer,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));
            SetField(
                writer,
                "_justiceWalFaultInjectorOverride",
                new NthWalFaultInjector(
                    JusticePersistenceFaultPoint.AfterWalFlush,
                    1));

            Invoke(writer, "RequestJusticeVoluntaryFinePayment");
            Assert.AreEqual(0, writeCount);
            AwaitQueuedPersistence(writer);

            SetField(writer, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Assert.IsFalse(
                (bool)Invoke(writer, "ResumeJusticeVoluntaryFinePayment"),
                "La perte d'ACK Prepared doit différer SET sans perdre l'intention.");
            Assert.AreEqual(0, writeCount);

            string walPath = Path.Combine(directory, "_justice_state.wal");
            Assert.IsTrue(File.Exists(walPath), "Le WAL financier autonome doit précéder tout débit.");
            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.IsTrue(wal.Records.Any(record =>
                record.State == JusticeWalState.Prepared &&
                record.OperationKind == "VoluntaryFinePayment" &&
                record.Fields.Count == 13 &&
                record.Fields.Any(field => field.Path == "paymentId") &&
                record.Fields.Any(field => field.Path == "debitAmount" && field.Value == "700") &&
                !record.Fields.Any(field =>
                    field.Path == "Case" ||
                    field.Path == "Record" ||
                    field.Path == "Custody")));
            Assert.IsFalse(
                wal.Records.Any(record => record.State == JusticeWalState.Attempted),
                "Une perte d'ACK Prepared ne doit pas inventer Attempted.");

            SetField(writer, "_justiceNextStateFlushAttemptAtMs", 0L);
            SetField(writer, "_justiceMonotonicTimeMs", 5000L);
            CompleteVoluntaryPayment(writer);
            Assert.AreEqual(1, writeCount, "La reprise ne doit émettre qu'un seul débit.");
            Assert.AreEqual(300, cash);
            Assert.AreEqual(0L, GetCase(writer).FineDue);
            Assert.IsNull(GetField<object>(writer, "_justiceVoluntaryFinePaymentIntent"));
        });
    }

    [TestMethod]
    public void VoluntaryPayment_TruncatedAttemptedFrameRetriesExactlyOneDebit()
    {
        WithTemporarySaveDirectory(directory =>
        {
            int cash = 1000;
            int writeCount = 0;
            object script = CreatePaymentScript(600L);
            SetField(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
            SetField(
                script,
                "_justiceCashWriteOverride",
                new Func<int, int, bool?>((slot, value) =>
                {
                    writeCount++;
                    cash = value;
                    return true;
                }));
            SetField(
                script,
                "_justiceWalFaultInjectorOverride",
                new NthWalFaultInjector(
                    JusticePersistenceFaultPoint.BeforeWalFlush,
                    2));

            Invoke(script, "RequestJusticeVoluntaryFinePayment");
            AwaitQueuedPersistence(script);
            SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Assert.IsFalse((bool)Invoke(script, "ResumeJusticeVoluntaryFinePayment"));
            Assert.AreEqual(0, writeCount);

            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(
                Path.Combine(directory, "_justice_state.wal"));
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.AreEqual(1, wal.Records.Count);
            Assert.AreEqual(JusticeWalState.Prepared, wal.Records[0].State);

            SetField(script, "_justiceNextStateFlushAttemptAtMs", 0L);
            SetField(script, "_justiceMonotonicTimeMs", 5000L);
            CompleteVoluntaryPayment(script);
            Assert.AreEqual(1, writeCount);
            Assert.AreEqual(400, cash);
            Assert.AreEqual(0L, GetCase(script).FineDue);
        });
    }

    [TestMethod]
    public void JusticeToggle_ActivationFlushFailureKeepsSessionEnabledAndRearmsRuntime()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreatePaymentScript(0L);
            JusticeCaseState state = GetCase(script);
            state.Enabled = false;
            state.Phase = JusticePhase.AtLarge;
            state.HasWarrant = true;
            SetField(script, "_justiceEnabled", false);
            SetField(script, "_justiceWantedClearPending", true);
            SetField(script, "_justiceDamagePairBaselineCount", 7);
            SetField(script, "_justiceDamageFrontPrimingPending", false);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => true));

            Invoke(script, "RequestJusticeToggle");

            Assert.IsTrue(GetField<bool>(script, "_justiceEnabled"));
            Assert.IsTrue(state.Enabled);
            Assert.IsFalse(GetField<bool>(script, "_justiceWantedClearPending"));
            Assert.AreEqual(0, GetField<int>(script, "_justiceDamagePairBaselineCount"));
            Assert.IsTrue(GetField<bool>(script, "_justiceDamageFrontPrimingPending"));
            Assert.IsTrue(
                GetField<bool>(script, "_justiceStateDirty"),
                "Le changement de session doit rester à sauvegarder après l'échec injecté.");
            Assert.AreEqual(12, state.ActiveScore);
            Assert.AreEqual(1, state.Charges.Count);
            Assert.IsTrue(state.HasWarrant);
            StringAssert.Contains(GetField<string>(script, "_statusText"), "ACTIVÉE");
        });
    }

    [TestMethod]
    public void JusticeToggle_DeactivationFlushFailureKeepsPauseAndClearsOnlyRuntimeCaches()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreatePaymentScript(0L);
            JusticeCaseState state = GetCase(script);
            state.Enabled = true;
            int scoreBeforePause = state.ActiveScore;
            int chargeCountBeforePause = state.Charges.Count;
            string episodeBeforePause = state.WantedEpisodeId;
            SetField(script, "_justiceEnabled", true);
            SetField(script, "_justiceWantedLossPending", true);
            SetField(script, "_justiceDamagePairBaselineCount", 9);
            SetField(script, "_justiceDamageFrontPrimingPending", true);
            SetField(script, "_justiceAimTargetHandle", 42);
            SetField(script, "_justicePursuitActive", true);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => true));

            Invoke(script, "RequestJusticeToggle");

            Assert.IsFalse(GetField<bool>(script, "_justiceEnabled"));
            Assert.IsFalse(state.Enabled);
            Assert.IsFalse(GetField<bool>(script, "_justiceWantedLossPending"));
            Assert.AreEqual(0, GetField<int>(script, "_justiceDamagePairBaselineCount"));
            Assert.IsFalse(GetField<bool>(script, "_justiceDamageFrontPrimingPending"));
            Assert.AreEqual(0, GetField<int>(script, "_justiceAimTargetHandle"));
            Assert.IsFalse(GetField<bool>(script, "_justicePursuitActive"));
            Assert.IsTrue(
                GetField<bool>(script, "_justiceStateDirty"),
                "La pause doit rester effective et être retentée par le writer.");
            Assert.AreEqual(scoreBeforePause, state.ActiveScore);
            Assert.AreEqual(chargeCountBeforePause, state.Charges.Count);
            Assert.AreEqual(episodeBeforePause, state.WantedEpisodeId);
            StringAssert.Contains(GetField<string>(script, "_statusText"), "DÉSACTIVÉE");
        });
    }

    [TestMethod]
    public void JusticePersistence_AFailureIsRateLimitedAndRetriesAfterOneSecond()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreatePaymentScript(500L);
            FlushAndAwait(script);

            JusticeCaseState state = GetCase(script);
            state.ActiveScore = 13;
            SetField(script, "_justiceMonotonicTimeMs", 100L);
            Assert.IsTrue(
                (bool)Invoke(script, "JusticeFlushStateNow"),
                "Le thread GTA doit accepter le DTO sans attendre sa validation.");
            Assert.IsFalse(
                (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"),
                "La barrière doit observer le rejet sémantique du writer.");
            Assert.IsFalse(
                (bool)Invoke(script, "JusticeFlushStateNow"),
                "Le flush suivant doit remonter l'échec worker et armer le retry.");
            Assert.AreEqual(
                1100L,
                GetField<long>(script, "_justiceNextStateFlushAttemptAtMs"));

            state.ActiveScore = 12;
            Assert.IsFalse(
                (bool)Invoke(script, "JusticeFlushStateNow"),
                "Le même tick ne doit ni réécrire le temporaire ni relancer l'exception.");
            SetField(script, "_justiceMonotonicTimeMs", 1100L);
            FlushAndAwait(script);
            Assert.AreEqual(0L, GetField<long>(script, "_justiceNextStateFlushAttemptAtMs"));
        });
    }

    private static object CreatePaymentScript(long fineDue)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            ActiveScore = 12,
            FineDue = fineDue,
            Phase = JusticePhase.Wanted,
            WantedEpisodeId = "payment:test"
        };
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:payment",
            IncidentId = "incident:payment",
            EpisodeId = "payment:test",
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 12,
            Fine = fineDue,
            SentenceSeconds = 0
        });
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", new JusticeRecordState());
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceMenuSelectedProfileSlot", 0);
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        SetField(script, "_justiceReleaseSelectedWeaponHash", GetStaticField<int>("JusticeUnarmedHash"));

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
            "_justiceCustodyInmates"
        };
        for (int index = 0; index < collectionFields.Length; index++)
        {
            FieldInfo field = ScriptType.GetField(collectionFields[index], InstanceFlags);
            Assert.IsNotNull(field, collectionFields[index]);
            field.SetValue(script, Activator.CreateInstance(field.FieldType, true));
        }

        Invoke(script, "InitializeJusticePlayerProfiles");
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceMenuSelectedProfileSlot", 0);
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        return script;
    }

    private static JusticeCaseState GetCase(object script)
    {
        return GetField<JusticeCaseState>(script, "_justiceCaseState");
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, InstanceFlags);
        Assert.IsNotNull(method, methodName);
        return method.Invoke(target, arguments);
    }

    private static void FlushAndAwait(object script)
    {
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeFlushStateNow"),
            "Le snapshot doit être accepté par le repository.");
        AwaitQueuedPersistence(script);
    }

    private static void AwaitQueuedPersistence(object script)
    {
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"),
            "La barrière réservée aux tests doit confirmer la révision sur disque.");
    }

    private static void CompleteVoluntaryPayment(object script)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            if (GetField<object>(script, "_justiceVoluntaryFinePaymentIntent") == null)
            {
                if (GetField<long>(script, "_justiceLastQueuedPersistenceRevision") > 0L)
                {
                    AwaitQueuedPersistence(script);
                }
                return;
            }

            if (GetField<long>(script, "_justiceLastQueuedPersistenceRevision") > 0L)
            {
                AwaitQueuedPersistence(script);
            }
            SetField(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Invoke(script, "ResumeJusticeVoluntaryFinePayment");
        }

        Assert.Fail("La transaction de paiement volontaire n'a pas convergé après ses barrières bornées.");
    }

    private static T GetStaticField<T>(string name)
    {
        FieldInfo field = ScriptType.GetField(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field, name);
        return (T)(field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null));
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

    private static void SetNestedField(object target, string name, object value)
    {
        Assert.IsNotNull(target);
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }

    private static void WithTemporarySaveDirectory(Action<string> action)
    {
        string previous = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticePaymentTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", directory);
            action(directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", previous);
            string fullDirectory = Path.GetFullPath(directory);
            string fullTemp = Path.GetFullPath(Path.GetTempPath());
            if (fullDirectory.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullDirectory))
            {
                DeleteDirectoryAfterPersistenceWriterStops(fullDirectory);
            }
        }
    }

    private static void DeleteDirectoryAfterPersistenceWriterStops(string directory)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw;
                }
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw;
                }
                Thread.Sleep(25);
            }
        }
    }

    private sealed class NthWalFaultInjector : IJusticePersistenceFaultInjector
    {
        private readonly JusticePersistenceFaultPoint _point;
        private readonly int _targetOccurrence;
        private int _occurrence;

        internal NthWalFaultInjector(
            JusticePersistenceFaultPoint point,
            int targetOccurrence)
        {
            _point = point;
            _targetOccurrence = targetOccurrence;
        }

        public void Probe(JusticePersistenceFaultPoint point)
        {
            if (point == _point && ++_occurrence == _targetOccurrence)
            {
                throw new IOException("Panne WAL financière déterministe injectée.");
            }
        }
    }
}
