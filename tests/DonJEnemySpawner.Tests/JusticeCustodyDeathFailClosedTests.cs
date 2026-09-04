using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyDeathFailClosedTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
#if DONJ_STUB_API
    private string _recognitionIntentDirectory;
#endif

    [TestMethod]
    public void CustodyDeath_WalFailureArmsFailClosedStateAndPendingHolding()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string observed = ReadMethod(source, "ObserveJusticeCustodyDeath");
        AssertOrdered(
            observed,
            "TryPersistJusticeCustodyDeathFrontToWal(",
            "ArmJusticeCustodyDeathFailClosedState(player, now)",
            "ResetJusticeCustodyClock(now)");

        string suspended = ReadMethod(
            source,
            "ObserveJusticeCustodyDeathDuringSuspension");
        AssertOrdered(
            suspended,
            "TryPersistJusticeCustodyDeathFrontToWal(",
            "ArmJusticeCustodyDeathFailClosedState(");

        string arm = ReadMethod(
            source,
            "ArmJusticeCustodyDeathFailClosedState");
        StringAssert.Contains(arm, "_justiceCustodyWaitingForRespawn = true");
        StringAssert.Contains(arm, "_justiceCustodyDeathRebindPending = true");
        StringAssert.Contains(
            arm,
            "_justiceCustodyDeathStatePersistencePending = true");
        AssertOrdered(
            arm,
            "_justiceCustodyContainmentEstablished = false",
            "_justiceOutsideCustodySinceAt = 0",
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)");

        string refresh = ReadMethod(
            source,
            "RefreshJusticePreJudgmentHoldingIntent");
        StringAssert.Contains(
            refresh,
            "TryResolveJusticePendingWalCustodyRebindHoldingIntent(");
        StringAssert.Contains(
            refresh,
            "JusticePreJudgmentHoldingSource.PendingWalCustodyRebind");

        string pending = ReadMethod(
            source,
            "TryResolveJusticePendingWalCustodyRebindHoldingIntent");
        StringAssert.Contains(pending, "JusticeCustodyDeathFrontMode");
        StringAssert.Contains(pending, "IsJusticeCustodyPhase(ownerCase.Phase)");
        StringAssert.Contains(pending, "record.ProfileSlot");
        StringAssert.Contains(pending, "_justiceCustodyPlayerSlot");
        Assert.IsFalse(pending.Contains("JusticeMarkStateDirty"));
        Assert.IsFalse(pending.Contains("SentenceSeconds ="));

        string blocking = ReadMethod(
            source,
            "MustBlockJusticeLateForPreJudgmentHolding");
        StringAssert.Contains(
            blocking,
            "JusticePreJudgmentHoldingSource.PendingWalCustodyRebind");
        StringAssert.Contains(blocking, "_justiceCustodyWaitingForRespawn");
        StringAssert.Contains(blocking, "_justiceCustodyDeathRebindPending");
    }

#if DONJ_STUB_API
    [TestMethod]
    public void CustodyDeath_WalOutageReholdsRespawnAtPrisonAndFreezesSentence()
    {
        GTA.StubRuntime.Reset();
        ulong groundProbe = GetPrivateConstant<ulong>(
            "JusticeNativeGetGroundZFor3DCoord");
        ulong collisionProbe = GetPrivateConstant<ulong>(
            "JusticeNativeHasCollisionLoadedAroundEntity");
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == groundProbe || hash == collisionProbe
                ? (object)true
                : null;
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 981;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(1691.0f, 2566.0f, 45.5f);
        player.IsDead = true;

        object script = CreateStableCustodyScript(player, 600);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");

        Invoke(script, "ObserveJusticeCustodyDeath", player, 1000);

        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyWaitingForRespawn"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyDeathRebindPending"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyDeathStatePersistencePending"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyContainmentEstablished"));
        object pending = GetFieldObject(
            script,
            "_justicePendingDeathFrontWalRecord");
        Assert.IsNotNull(pending);
        Assert.AreEqual(
            "CustodyRebind",
            ReadWalField((JusticeWalRecord)pending, "mode"));
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);

        // Je reproduis le respawn vanilla à l'hôpital alors que le WAL reste
        // indisponible. Le holding doit masquer ce point puis replacer le détenu.
        player.IsDead = false;
        player.Position = new GTA.Math.Vector3(307.0f, -595.0f, 43.0f);
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        bool blocksLate = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000);

        Assert.IsTrue(blocksLate);
        Assert.AreEqual(
            "PendingWalCustodyRebind",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        Assert.AreEqual(
            "Bolingbroke",
            GetFieldObject(
                script,
                "_justicePoliceDeathPreJudgmentHoldingSite").ToString());
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsTrue(player.FreezePosition);
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);

        int fadeOut = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_OUT);
        int fadeIn = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN);
        Assert.IsTrue(fadeOut >= 1, "L'hôpital doit être masqué avant le déplacement.");
        Assert.AreEqual(
            0,
            fadeIn,
            "Sans writer durable, aucune admission sûre ne peut rendre l'écran.");

        Invoke(script, "JusticeUpdateCustody", player, 3000);
        Invoke(script, "JusticeUpdateCustody", player, 8000);
        Assert.AreEqual(
            600,
            state.SentenceSeconds,
            "La peine doit rester suspendue tant que CustodyRebind n'est pas durable.");
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyContainmentEstablished"));
        Assert.IsTrue((bool)Invoke(script, "IsInsideJusticeCustody", player.Position));
        Assert.IsTrue(player.FreezePosition);
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.AreEqual(0, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN));
    }

    [DataTestMethod]
    [DataRow("holding-absent")]
    [DataRow("outside-custody")]
    [DataRow("death-front-unavailable")]
    [DataRow("death-front-persistence-pending")]
    [DataRow("wal-record-pending")]
    [DataRow("identity-divergent")]
    [DataRow("policy-recovery")]
    public void CustodyDeathResidualMissionFlagBypass_RejectsEveryIncompleteProof(
        string missingProof)
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 982;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(1691.0f, 2566.0f, 45.5f);
        player.IsDead = false;

        using (DurableCustodyBypassHarness harness =
            new DurableCustodyBypassHarness(player))
        {
            switch (missingProof)
            {
                case "holding-absent":
                    SetField(
                        harness.Script,
                        "_justicePoliceDeathPreJudgmentHoldingEstablished",
                        false);
                    break;

                case "outside-custody":
                    player.Position = new GTA.Math.Vector3(
                        307.0f,
                        -595.0f,
                        43.0f);
                    break;

                case "death-front-unavailable":
                    SetField(
                        harness.Script,
                        "_justicePersistenceServicesUnavailable",
                        true);
                    break;

                case "death-front-persistence-pending":
                    SetField(
                        harness.Script,
                        "_justiceCustodyDeathStatePersistencePending",
                        true);
                    break;

                case "wal-record-pending":
                    SetField(
                        harness.Script,
                        "_justicePendingDeathFrontWalRecord",
                        CreatePendingCustodyDeathFrontRecord());
                    break;

                case "identity-divergent":
                    player.Model = new GTA.Model("player_one");
                    break;

                case "policy-recovery":
                    SetField(
                        harness.Script,
                        "_justicePolicyResetRecoveryMask",
                        1);
                    break;

                default:
                    Assert.Fail("Preuve négative non prise en charge : " + missingProof);
                    break;
            }

            bool armed = (bool)Invoke(
                harness.Script,
                "TryArmJusticeCustodyDeathResidualMissionFlagBypassAfterHolding",
                player);

            Assert.IsFalse(
                armed,
                "Le bypass ne doit jamais s'armer avec une preuve incomplète : " +
                missingProof);
            Assert.IsFalse(GetField<bool>(
                harness.Script,
                "_justiceCustodyResidualMissionFlagBypassArmed"));
            Assert.AreEqual(
                0L,
                GetField<long>(
                    harness.Script,
                    "_justiceCustodyResidualMissionFlagObservationDeadlineMs"));
        }
    }

    [TestMethod]
    public void CustodyDeathResidualMissionFlagBypass_ArmsOnlyAfterDurableHoldingProof()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 983;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(1691.0f, 2566.0f, 45.5f);
        player.IsDead = false;

        using (DurableCustodyBypassHarness harness =
            new DurableCustodyBypassHarness(player))
        {
            Assert.AreEqual(
                0,
                harness.WriteAheadLog.GetOpenTransactions().Count,
                "Le scénario positif doit partir d'un WAL sans front ouvert.");
            Assert.AreEqual(
                JusticeWalState.Confirmed,
                harness.WriteAheadLog.GetLatest(
                    DurableCustodyBypassHarness.DeathFrontTransactionId).State,
                "Le front CustodyRebind positif doit être terminal et confirmé.");
            Assert.IsTrue((bool)Invoke(
                harness.Script,
                "IsJusticeCustodyDeathFrontResultDurable"));

            bool armed = (bool)Invoke(
                harness.Script,
                "TryArmJusticeCustodyDeathResidualMissionFlagBypassAfterHolding",
                player);

            Assert.IsTrue(armed);
            Assert.IsTrue(GetField<bool>(
                harness.Script,
                "_justiceCustodyResidualMissionFlagBypassArmed"));
            Assert.AreEqual(
                0L,
                GetField<long>(
                    harness.Script,
                    "_justiceCustodyResidualMissionFlagObservationDeadlineMs"),
                "Zéro matérialise un latch mission déjà observé et qualifié.");
            Assert.IsTrue((bool)Invoke(
                harness.Script,
                "CanIgnoreJusticeMissionFlagForCustody",
                player));
        }
    }

    [TestMethod]
    public void CustodyTransfer_FadeOutRefusalKeepsOriginClockAndProtectionFailClosed()
    {
        GTA.StubRuntime.Reset();
        ResetJusticeRecognitionBridge();
        _recognitionIntentDirectory = Path.Combine(
            Path.GetTempPath(),
            "DonJCustodyFadeOutTests_" + Guid.NewGuid().ToString("N"));
        DonJ.JusticeRecognition.JusticeRecognitionBridge
            .ConfigureCriticalIntentJournalForTests(Path.Combine(
                _recognitionIntentDirectory,
                "critical-intents.xml"));

        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 984;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(307.0f, -595.0f, 43.0f);
        player.IsDead = false;
        player.IsInvincible = true;
        player.FreezePosition = true;
        player.CanRagdoll = false;
        GTA.Math.Vector3 origin = player.Position;

        object script = CreateStableCustodyScript(player, 600);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Phase = JusticePhase.Transporting;
        state.CustodyEpisodeId = "custody:fade-out-refused";
        SetField(script, "_justiceCustodyTransferPending", true);
        SetField(script, "_justiceCustodyWaitingForRespawn", false);
        SetField(script, "_justiceCustodyDeathRebindPending", false);
        SetField(script, "_justiceCustodyPlayerStateStored", true);
        SetField(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        SetEnumField(
            script,
            "_justiceInventoryCustodyState",
            "UnsupportedPreserved");
        SetField(script, "_justiceCustodyRespawnTransferPending", true);
        SetEnumField(script, "_justicePreJudgmentHoldingSource", "Captured");
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished",
            true);
        SetEnumField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingSite",
            "Bolingbroke");
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash",
            player.Model.Hash);

        // Je simule la protection déjà acquise au respawn : le refus du noir
        // ne doit ni la relâcher ni autoriser le premier déplacement vers la prison.
        SetEnumField(
            script,
            "_playerInvincibilityOwners",
            "JusticePreJudgmentHolding");
        SetField(script, "_playerInvincibilityPed", player);
        SetField(script, "_playerInvincibilityPedHandle", player.Handle);
        SetField(script, "_playerInvincibilityBaselineCaptured", true);
        SetField(script, "_justicePreJudgmentHoldingStreamingPending", true);
        SetField(script, "_justicePreJudgmentHoldingProtectionOwned", true);
        SetField(script, "_justicePreJudgmentHoldingCanRagdollCaptured", true);
        SetField(
            script,
            "_justicePreJudgmentHoldingStreamingPlayerHandle",
            player.Handle);
        SetField(
            script,
            "_justicePreJudgmentHoldingStreamingPlayerModelHash",
            player.Model.Hash);

        int fadeOutAttempts = 0;
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT)
            {
                fadeOutAttempts++;
                throw new InvalidOperationException("fade-out refusé");
            }
            return null;
        };

        Invoke(script, "CompleteJusticeCustodyTransfer", player, 5000);

        Assert.AreEqual(1, fadeOutAttempts);
        Assert.AreEqual(
            0,
            CountNative(GetPrivateConstant<ulong>(
                "AdvancedNativeSetEntityCoordsNoOffset")),
            "Aucune téléportation ne doit précéder la preuve du fondu noir.");
        Assert.AreEqual(origin.X, player.Position.X, 0.001f);
        Assert.AreEqual(origin.Y, player.Position.Y, 0.001f);
        Assert.AreEqual(origin.Z, player.Position.Z, 0.001f);
        Assert.AreEqual(JusticePhase.Transporting, state.Phase);
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
        Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
        Assert.AreEqual(
            0,
            GetField<int>(script, "_justiceCustodyElapsedRemainderMs"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyRespawnMaskNeedsRearm"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingProtectionOwned"));
        Assert.AreEqual(
            "JusticePreJudgmentHolding",
            GetFieldObject(script, "_playerInvincibilityOwners").ToString());
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Assert.IsFalse(player.CanRagdoll);
    }

    private static JusticeWalRecord CreatePendingCustodyDeathFrontRecord()
    {
        return new JusticeWalRecord(
            "death-front:custody:pending",
            "DeathFront",
            0,
            JusticeWalState.Prepared,
            1L,
            DateTime.UtcNow.Ticks,
            new[]
            {
                new JusticePersistenceField("mode", "CustodyRebind")
            });
    }

    private static object CreateStableCustodyScript(GTA.Ped player, int sentenceSeconds)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
        JusticeCaseState state = profiles[0].CaseState;
        state.Enabled = true;
        state.Phase = JusticePhase.Incarcerated;
        state.SentenceSeconds = sentenceSeconds;
        state.CustodyEpisodeId = "custody:wal-outage";
        profiles[0].LastCanonicalPlayerModel = player.Model.Hash;

        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profiles[0].RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceProfilePersistenceGenerations", new[] { 0L, 0L, 0L });
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
        SetField(script, "_justiceCustodyContainmentEstablished", true);
        SetField(script, "_justicePersistenceServicesUnavailable", true);
        SetField(script, "_justicePersistenceInitializationFailurePermanent", true);
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);

        foreach (string collectionField in new[]
        {
            "_justiceCustodyGuards",
            "_justiceCustodyInmates"
        })
        {
            InitializeEmptyCollectionField(script, collectionField);
        }
        return script;
    }

    private static int CountNative(GTA.Native.Hash hash)
    {
        return CountNative((ulong)hash);
    }

    private static int CountNative(ulong hash)
    {
        return GTA.StubRuntime.NativeCalls.Count(call => call.Hash == hash);
    }

    [TestCleanup]
    public void ResetStubRuntimeHandler()
    {
        GTA.StubRuntime.Reset();
        ResetJusticeRecognitionBridge();
        if (!string.IsNullOrWhiteSpace(_recognitionIntentDirectory) &&
            Directory.Exists(_recognitionIntentDirectory))
        {
            Directory.Delete(_recognitionIntentDirectory, true);
        }
        _recognitionIntentDirectory = null;
    }

    private static void ResetJusticeRecognitionBridge()
    {
        Type bridgeType = typeof(
            DonJ.JusticeRecognition.JusticeRecognitionBridge);
        SetStaticField(bridgeType, "_instance", null);
        SetStaticField(bridgeType, "_desiredEnabled", null);
        SetStaticField(bridgeType, "_desiredRuntimeSuspended", null);
        SetStaticField(bridgeType, "_desiredActiveProfileId", null);
        SetStaticField(bridgeType, "_wantedMinimumHandler", null);
        SetStaticField(bridgeType, "_nextCriticalCommandId", 0L);
        SetStaticField(bridgeType, "_pendingCurrentProfileCapture", null);
        SetStaticField(bridgeType, "_pendingCurrentProfileClear", null);
        SetStaticField(bridgeType, "_pendingGlobalClear", null);
        SetStaticField(bridgeType, "_criticalIntentStore", null);
        SetStaticField(bridgeType, "_criticalIntentsLoaded", false);
        SetStaticField(bridgeType, "_criticalIntentPathOverride", null);
        ClearStaticDictionary(bridgeType, "PendingProfileCaptureReasons");
        ClearStaticDictionary(bridgeType, "PendingProfileClearReasons");
    }

    private static void SetStaticField(Type type, string fieldName, object value)
    {
        FieldInfo field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ statique introuvable : " + fieldName);
        field.SetValue(null, value);
    }

    private static void ClearStaticDictionary(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Dictionnaire statique introuvable : " + fieldName);
        IDictionary dictionary = field.GetValue(null) as IDictionary;
        Assert.IsNotNull(dictionary);
        dictionary.Clear();
    }

    private sealed class DurableCustodyBypassHarness : IDisposable
    {
        internal const string DeathFrontTransactionId =
            "death-front:custody:confirmed";

        private readonly string _directory;

        internal DurableCustodyBypassHarness(GTA.Ped player)
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "DonJCustodyBypassTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            string statePath = Path.Combine(_directory, "_justice_state.xml");
            Repository = new JusticeRepository(
                statePath,
                statePath + ".bak",
                new JusticeXmlPersistenceCodec(),
                4L);
            WriteAheadLog = new JusticeWriteAheadLog(
                Path.Combine(_directory, "_justice_state.wal"));
            AppendConfirmedCustodyDeathFront(WriteAheadLog, player.Model.Hash);

            Script = CreateStableCustodyScript(player, 600);
            SetField(Script, "_justiceRepository", Repository);
            SetField(Script, "_justiceWriteAheadLog", WriteAheadLog);
            SetField(Script, "_justicePersistenceServicesUnavailable", false);
            SetField(
                Script,
                "_justicePersistenceInitializationFailurePermanent",
                false);
            SetField(
                Script,
                "_justiceRuntimeSuspendedByMissionFlagOnlyCached",
                true);
            SetField(Script, "_justiceCustodyWaitingForRespawn", true);
            SetField(Script, "_justiceCustodyDeathRebindPending", true);
            SetField(
                Script,
                "_justiceCustodyDeathStatePersistencePending",
                false);
            SetField(Script, "_justicePendingDeathFrontWalRecord", null);
            SetEnumField(
                Script,
                "_justicePreJudgmentHoldingSource",
                "PendingWalCustodyRebind");
            SetField(
                Script,
                "_justicePoliceDeathPreJudgmentHoldingEstablished",
                true);
            SetEnumField(
                Script,
                "_justicePoliceDeathPreJudgmentHoldingSite",
                "Bolingbroke");
            SetField(
                Script,
                "_justicePoliceDeathPreJudgmentHoldingOwnerSlot",
                0);
            SetField(
                Script,
                "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash",
                player.Model.Hash);
        }

        internal object Script { get; private set; }

        internal JusticeRepository Repository { get; private set; }

        internal JusticeWriteAheadLog WriteAheadLog { get; private set; }

        public void Dispose()
        {
            Repository.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        private static void AppendConfirmedCustodyDeathFront(
            JusticeWriteAheadLog wal,
            int playerModel)
        {
            IEnumerable<JusticePersistenceField> fields =
                (IEnumerable<JusticePersistenceField>)InvokeStatic(
                    "CreateJusticeDeathFrontWalFields",
                    "CustodyRebind",
                    1L,
                    0L,
                    "slot:0:model:" + playerModel,
                    "custody:wal-outage",
                    2,
                    0,
                    playerModel,
                    0,
                    playerModel);
            long createdAt = DateTime.UtcNow.Ticks;
            wal.Append(new JusticeWalRecord(
                DeathFrontTransactionId,
                "DeathFront",
                0,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                DeathFrontTransactionId,
                "DeathFront",
                0,
                JusticeWalState.Attempted,
                2L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                DeathFrontTransactionId,
                "DeathFront",
                0,
                JusticeWalState.Confirmed,
                2L,
                createdAt,
                fields));
        }
    }
#endif

    private static string ReadWalField(JusticeWalRecord record, string path)
    {
        JusticePersistenceField field = record.Fields.Single(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal));
        return field.Value;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null &&
               !File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
        {
            current = current.Parent;
        }
        Assert.IsNotNull(current, "Racine du dépôt introuvable.");
        return current.FullName;
    }

    private static string ReadMethod(string source, string methodName)
    {
        int name = -1;
        int searchFrom = 0;
        while (searchFrom < source.Length)
        {
            int candidate = source.IndexOf(
                methodName + "(",
                searchFrom,
                StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }
            int lineStart = source.LastIndexOf('\n', candidate);
            int prefixStart = lineStart < 0 ? 0 : lineStart + 1;
            string prefix = source.Substring(prefixStart, candidate - prefixStart);
            if (prefix.IndexOf("private ", StringComparison.Ordinal) >= 0)
            {
                name = candidate;
                break;
            }
            searchFrom = candidate + methodName.Length + 1;
        }
        Assert.IsTrue(name >= 0, "Méthode source introuvable : " + methodName);
        int open = source.IndexOf('{', name);
        Assert.IsTrue(open >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(open, index - open + 1);
            }
        }
        Assert.Fail("Fin de méthode source introuvable : " + methodName);
        return string.Empty;
    }

    private static void AssertOrdered(string source, params string[] fragments)
    {
        int cursor = -1;
        foreach (string fragment in fragments)
        {
            int index = source.IndexOf(fragment, cursor + 1, StringComparison.Ordinal);
            Assert.IsTrue(index > cursor, "Fragment absent ou désordonné : " + fragment);
            cursor = index;
        }
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo[] methods = target.GetType()
            .GetMethods(PrivateInstance)
            .Where(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length)
            .ToArray();
        Assert.AreEqual(1, methods.Length, "Méthode privée ambiguë : " + methodName);
        return methods[0].Invoke(target, arguments);
    }

    private static object InvokeStatic(string methodName, params object[] arguments)
    {
        MethodInfo[] methods = ScriptType
            .GetMethods(PrivateStatic)
            .Where(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length)
            .ToArray();
        Assert.AreEqual(
            1,
            methods.Length,
            "Méthode statique privée ambiguë : " + methodName);
        return methods[0].Invoke(null, arguments);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)GetFieldObject(target, fieldName);
    }

    private static object GetFieldObject(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return field.GetValue(target);
    }

    private static T GetPrivateConstant<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, "Constante privée introuvable : " + fieldName);
        return (T)field.GetRawConstantValue();
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static void SetEnumField(object target, string fieldName, string value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ enum privé introuvable : " + fieldName);
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static void InitializeEmptyCollectionField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Collection privée introuvable : " + fieldName);
        object value = field.FieldType.IsInterface
            ? null
            : Activator.CreateInstance(field.FieldType, true);
        if (value == null && field.FieldType.IsGenericType)
        {
            Type generic = field.FieldType.GetGenericTypeDefinition();
            Type[] arguments = field.FieldType.GetGenericArguments();
            if (generic == typeof(IList<>) || generic == typeof(ICollection<>))
            {
                value = Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments));
            }
            else if (generic == typeof(IDictionary<,>))
            {
                value = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
            }
        }
        Assert.IsNotNull(value, "Collection non initialisable : " + fieldName);
        field.SetValue(target, value);
    }
}
