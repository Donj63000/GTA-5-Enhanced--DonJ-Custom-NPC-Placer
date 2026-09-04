using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyAdoptedRespawnIdentityTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void AdoptedRespawnIdentity_SourceContractConsumesBroadGrantIntoStrictReloadIdentity()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string rebind = ExtractMethodBody(
            source,
            "TryRebindJusticeCustodyIdentityAfterRespawn");
        string adopted = ExtractMethodBody(
            source,
            "CanRebindJusticeCustodyAdoptedRespawnIdentity");
        string load = ExtractMethodBody(source, "JusticeReadCustodyXml");

        StringAssert.Contains(
            rebind,
            "_justiceCustodyDeathRebindPending = false");
        StringAssert.Contains(
            rebind,
            "_justiceCustodyRespawnIdentityRebindConfirmed = true");
        AssertOrdered(
            rebind,
            "_justiceCustodyPlayerModelHash = modelHash",
            "_justiceCustodyDeathRebindPending = false",
            "JusticeFlushStateNow()",
            "_justiceCustodyRespawnIdentityRebindConfirmed = true");
        StringAssert.Contains(
            adopted,
            "_justiceCustodyDeathRebindPending");
        StringAssert.Contains(
            adopted,
            "currentModelHash == _justiceCustodyPlayerModelHash");
        StringAssert.Contains(
            adopted,
            "currentSlot == _justiceCustodyPlayerSlot || currentSlot == -1");
        StringAssert.Contains(
            load,
            "_justiceCustodyRespawnIdentityRebindConfirmed = false");
    }

#if DONJ_STUB_API
    [TestMethod]
    public void AdoptedRespawnIdentity_DurableReloadReconstructsOnlyExactCustomIdentity()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            GTA.StubRuntime.Reset();
            GTA.Ped firstRespawn = CreatePlayer(
                1401,
                "mp_m_freemode_01");
            GTA.Game.Player.Character = firstRespawn;

            object writer = CreateCustodyScript(
                CreateProfilesWithIncarceratedOwner(),
                0);
            SetField(
                writer,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => -1));
            SetField(writer, "_justiceCustodyWaitingForRespawn", true);
            SetField(writer, "_justiceCustodyDeathRebindPending", true);
            SetField(writer, "_justiceCustodyPlayerHandle", 1399);
            SetField(
                writer,
                "_justiceCustodyPlayerModelHash",
                new GTA.Model("player_zero").Hash);

            try
            {
                Assert.IsTrue(
                    CompleteRespawnIdentityRebind(writer, firstRespawn),
                    "Le premier respawn doit consommer son unique droit d'adoption.");
                AwaitQueuedPersistence(writer);

                string statePath = Path.Combine(directory, "_justice_state.xml");
                XDocument durable = XDocument.Load(statePath);
                XElement custody = GetPersistedActiveProfile(durable)
                    .Element("Custody");
                Assert.IsNotNull(custody);
                Assert.AreEqual(
                    "true",
                    (string)custody.Attribute("waitingForRespawn"));
                Assert.AreEqual(
                    "false",
                    (string)custody.Attribute("deathRebindPending"));
                Assert.AreEqual(
                    firstRespawn.Model.Hash.ToString(),
                    (string)custody.Attribute("playerModelHash"));

                Invoke(writer, "ShutdownJusticePersistenceServices");

                GTA.Ped exactReload = CreatePlayer(
                    1402,
                    "mp_m_freemode_01");
                object exactReader = LoadAdoptedCustody(
                    statePath,
                    exactReload,
                    -1);
                try
                {
                    Assert.AreEqual(
                        0,
                        GetField<int>(exactReader, "_justiceCustodyPlayerHandle"),
                        "Le handle GTA reste volontairement volatil après reload.");
                    Assert.IsTrue((bool)Invoke(
                        exactReader,
                        "CanRebindJusticeCustodyAdoptedRespawnIdentity",
                        exactReload));
                    Assert.IsTrue((bool)Invoke(
                        exactReader,
                        "TryRebindJusticeCustodyIdentityAfterRespawn",
                        exactReload));
                    Assert.AreEqual(
                        exactReload.Handle,
                        GetField<int>(exactReader, "_justiceCustodyPlayerHandle"));
                    Assert.IsTrue(GetField<bool>(
                        exactReader,
                        "_justiceCustodyRespawnIdentityRebindConfirmed"));
                    Assert.IsFalse(GetField<bool>(
                        exactReader,
                        "_justiceCustodyDeathRebindPending"));
                    Assert.IsTrue((bool)Invoke(
                        exactReader,
                        "IsJusticeCustodyPlayerIdentityCompatible",
                        exactReload));
                    AwaitQueuedPersistence(exactReader);
                }
                finally
                {
                    Invoke(exactReader, "ShutdownJusticePersistenceServices");
                }

                GTA.Ped wrongModel = CreatePlayer(1403, "s_m_y_cop_01");
                object wrongModelReader = LoadAdoptedCustody(
                    statePath,
                    wrongModel,
                    -1);
                Assert.IsFalse((bool)Invoke(
                    wrongModelReader,
                    "CanRebindJusticeCustodyAdoptedRespawnIdentity",
                    wrongModel));
                Assert.IsFalse((bool)Invoke(
                    wrongModelReader,
                    "TryRebindJusticeCustodyIdentityAfterRespawn",
                    wrongModel));
                Assert.AreEqual(
                    0,
                    GetField<int>(wrongModelReader, "_justiceCustodyPlayerHandle"));

                GTA.Ped otherHero = CreatePlayer(1404, "player_one");
                object otherSlotReader = LoadAdoptedCustody(
                    statePath,
                    otherHero,
                    1);
                Assert.IsFalse((bool)Invoke(
                    otherSlotReader,
                    "CanRebindJusticeCustodyAdoptedRespawnIdentity",
                    otherHero));
                Assert.IsFalse((bool)Invoke(
                    otherSlotReader,
                    "TryRebindJusticeCustodyIdentityAfterRespawn",
                    otherHero));
                Assert.AreEqual(
                    0,
                    GetField<int>(otherSlotReader, "_justiceCustodyPlayerHandle"));
            }
            finally
            {
                Invoke(writer, "ShutdownJusticePersistenceServices");
            }
        });
    }

    [TestMethod]
    public void AdoptedRespawnIdentity_ConsumedBroadGrantCannotBeReusedForAnotherPed()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped adoptedPlayer = CreatePlayer(
            1411,
            "mp_m_freemode_01");
        GTA.Game.Player.Character = adoptedPlayer;
        object script = CreateCustodyScript(
            CreateProfilesWithIncarceratedOwner(),
            0);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => -1));
        SetField(script, "_justiceCustodyWaitingForRespawn", true);
        SetField(script, "_justiceCustodyDeathRebindPending", false);
        SetField(script, "_justiceCustodyPlayerHandle", adoptedPlayer.Handle);
        SetField(
            script,
            "_justiceCustodyPlayerModelHash",
            adoptedPlayer.Model.Hash);
        SetField(
            script,
            "_justiceCustodyRespawnIdentityRebindConfirmed",
            true);

        Assert.IsTrue(
            JusticePolicy.CanRebindCustodyRespawnSlot(0, -1, 0, 0, true),
            "Le droit initial serait large pour un respawn custom non encore adopté.");

        GTA.Ped unrelatedPed = CreatePlayer(1412, "s_m_y_cop_01");
        GTA.Game.Player.Character = unrelatedPed;
        Assert.IsFalse((bool)Invoke(
            script,
            "CanRebindJusticeCustodyAdoptedRespawnIdentity",
            unrelatedPed));
        Assert.IsFalse((bool)Invoke(
            script,
            "TryRebindJusticeCustodyIdentityAfterRespawn",
            unrelatedPed));
        Assert.AreEqual(
            adoptedPlayer.Handle,
            GetField<int>(script, "_justiceCustodyPlayerHandle"));
        Assert.AreEqual(
            adoptedPlayer.Model.Hash,
            GetField<int>(script, "_justiceCustodyPlayerModelHash"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyDeathRebindPending"));
    }

    [TestMethod]
    public void PreJudgmentHolding_DisplacedPedIsMovedAgainUnderMaskAndProtection()
    {
        GTA.StubRuntime.Reset();
        ulong groundProbe = GetPrivateConstant<ulong>(
            "JusticeNativeGetGroundZFor3DCoord");
        ulong collisionProbe = GetPrivateConstant<ulong>(
            "JusticeNativeHasCollisionLoadedAroundEntity");
        ulong setCoords = GetPrivateConstant<ulong>(
            "AdvancedNativeSetEntityCoordsNoOffset");
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == groundProbe || hash == collisionProbe
                ? (object)true
                : null;
        GTA.StubRuntime.ScreenFadedOut = true;

        GTA.Ped player = CreatePlayer(1421, "player_zero");
        GTA.Game.Player.Character = player;
        GTA.Math.Vector3 holding = new GTA.Math.Vector3(
            1691.0f,
            2566.0f,
            45.5f);
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash",
            player.Model.Hash);

        Assert.IsTrue((bool)Invoke(
            script,
            "TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer",
            player,
            holding,
            90.0f));
        int callsAfterFirstMove = GTA.StubRuntime.NativeCalls.Count(
            call => call.Hash == setCoords);
        Assert.AreEqual(1, callsAfterFirstMove);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingPositionApplied"));

        // Je reproduis le déplacement imposé par WASTED après une première
        // téléportation validée : le latch ne doit jamais bloquer la correction.
        player.Position = new GTA.Math.Vector3(307.0f, -595.0f, 43.0f);
        Assert.IsTrue((bool)Invoke(
            script,
            "TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer",
            player,
            holding,
            90.0f));

        Assert.AreEqual(
            callsAfterFirstMove + 1,
            GTA.StubRuntime.NativeCalls.Count(call => call.Hash == setCoords),
            "SET_ENTITY_COORDS_NO_OFFSET doit être rejoué dès que GTA sort le ped du holding.");
        Assert.IsTrue(GTA.StubRuntime.ScreenFadedOut);
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Assert.IsFalse(player.CanRagdoll);
        Assert.AreEqual(
            "JusticePreJudgmentHolding",
            GetField<object>(script, "_playerInvincibilityOwners").ToString());
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingProtectionOwned"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingPositionApplied"));
        Assert.IsTrue(player.Position.DistanceTo(holding) <= 8.0f);
    }

    [TestMethod]
    public void ResidualMissionBypass_OrdinaryTransferCannotArmOrUseTheDeathException()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = CreatePlayer(1431, "player_zero");
        GTA.Game.Player.Character = player;
        object script = CreateCustodyScript(
            CreateProfilesWithIncarceratedOwner(),
            0);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Phase = JusticePhase.Transporting;
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(
            script,
            "_justiceCustodyPlayerModelHash",
            player.Model.Hash);
        SetField(
            script,
            "_justiceRuntimeSuspendedByMissionFlagOnlyCached",
            true);
        SetField(script, "_justiceCustodyTransferPending", true);
        SetField(script, "_justiceCustodyWaitingForRespawn", false);
        SetField(script, "_justiceCustodyDeathRebindPending", false);

        Assert.IsFalse(GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles")[0].PendingDeathCapture);
        Assert.IsFalse((bool)Invoke(
            script,
            "TryArmJusticeCustodyDeathResidualMissionFlagBypassAfterHolding",
            player));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyResidualMissionFlagBypassArmed"));
        Assert.IsFalse((bool)Invoke(
            script,
            "CanIgnoreJusticeMissionFlagForCustody",
            player));

        // Je simule aussi un ancien jeton observé : sans décès, sans waiting et
        // sans holding exact, le Transporting ordinaire reste strictement bloqué.
        Invoke(script, "ArmJusticeCustodyResidualMissionFlagBypassObserved");
        Assert.IsFalse((bool)Invoke(
            script,
            "CanIgnoreJusticeMissionFlagForCustody",
            player));
    }

    [TestMethod]
    public void ResidualMissionBypass_ExactCustodyDeathAndHoldingCanArmTheException()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            GTA.StubRuntime.Reset();
            GTA.Ped player = CreatePlayer(1432, "player_zero");
            player.Position = new GTA.Math.Vector3(
                1691.0f,
                2566.0f,
                45.5f);
            GTA.Game.Player.Character = player;
            object script = CreateCustodyScript(
                CreateProfilesWithIncarceratedOwner(),
                0);
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
            SetField(
                script,
                "_justiceCustodyPlayerModelHash",
                player.Model.Hash);
            SetField(
                script,
                "_justiceRuntimeSuspendedByMissionFlagOnlyCached",
                true);
            SetField(script, "_justiceCustodyWaitingForRespawn", true);
            SetField(script, "_justiceCustodyDeathRebindPending", true);
            SetEnumField(
                script,
                "_justicePreJudgmentHoldingSource",
                "PendingWalCustodyRebind");
            SetField(
                script,
                "_justicePoliceDeathPreJudgmentHoldingEstablished",
                true);
            SetEnumField(
                script,
                "_justicePoliceDeathPreJudgmentHoldingSite",
                "Bolingbroke");
            SetField(
                script,
                "_justicePoliceDeathPreJudgmentHoldingOwnerSlot",
                0);
            SetField(
                script,
                "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash",
                player.Model.Hash);
            SetField(
                script,
                "_justiceProfilePersistenceGenerations",
                new[] { 0L, 0L, 0L });

            string statePath = Path.Combine(directory, "positive-state.xml");
            using (JusticeRepository repository = new JusticeRepository(
                statePath,
                statePath + ".bak",
                new JusticeXmlPersistenceCodec(),
                4L))
            {
                JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                    Path.Combine(directory, "positive-state.wal"));
                AppendConfirmedCustodyDeathFront(
                    wal,
                    player.Model.Hash,
                    GetField<JusticeCaseState>(
                        script,
                        "_justiceCaseState").CustodyEpisodeId);
                SetField(script, "_justiceRepository", repository);
                SetField(script, "_justiceWriteAheadLog", wal);
                SetField(script, "_justicePersistenceServicesUnavailable", false);

                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticeCustodyDeathFrontResultDurable"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryArmJusticeCustodyDeathResidualMissionFlagBypassAfterHolding",
                    player));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.AreEqual(
                    0L,
                    GetField<long>(
                        script,
                        "_justiceCustodyResidualMissionFlagObservationDeadlineMs"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "CanIgnoreJusticeMissionFlagForCustody",
                    player));
            }
        });
    }

    private static GTA.Ped CreatePlayer(int handle, string modelName)
    {
        return new GTA.Ped
        {
            Handle = handle,
            Model = new GTA.Model(modelName),
            IsPlayer = true,
            IsDead = false,
            CanRagdoll = true
        };
    }

    private static object LoadAdoptedCustody(
        string statePath,
        GTA.Ped player,
        int currentSlot)
    {
        GTA.Game.Player.Character = player;
        object reader = CreateCustodyScript(null, -1);
        SetField(
            reader,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => -1));
        Assert.IsTrue((bool)Invoke(
            reader,
            "TryReadJusticeStateFile",
            statePath));
        SetField(
            reader,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => currentSlot));
        Assert.IsTrue(GetField<bool>(
            reader,
            "_justiceCustodyWaitingForRespawn"));
        Assert.IsFalse(GetField<bool>(
            reader,
            "_justiceCustodyDeathRebindPending"));
        Assert.IsFalse(GetField<bool>(
            reader,
            "_justiceCustodyRespawnIdentityRebindConfirmed"));
        return reader;
    }

    private static bool CompleteRespawnIdentityRebind(
        object script,
        GTA.Ped player)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if ((bool)Invoke(
                    script,
                    "TryRebindJusticeCustodyIdentityAfterRespawn",
                    player))
            {
                return true;
            }

            AwaitQueuedPersistence(script);
        }

        return false;
    }

    private static void AppendConfirmedCustodyDeathFront(
        JusticeWriteAheadLog wal,
        int playerModel,
        string custodyEpisode)
    {
        const string transactionId = "death-front:adopted-bypass:confirmed";
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokeStatic(
                "CreateJusticeDeathFrontWalFields",
                "CustodyRebind",
                1L,
                0L,
                "slot:0:model:" + playerModel,
                custodyEpisode,
                2,
                0,
                playerModel,
                0,
                playerModel);
        long createdAt = DateTime.UtcNow.Ticks;
        foreach (JusticeWalState state in new[]
                 {
                     JusticeWalState.Prepared,
                     JusticeWalState.Attempted,
                     JusticeWalState.Confirmed
                 })
        {
            wal.Append(new JusticeWalRecord(
                transactionId,
                "DeathFront",
                0,
                state,
                state == JusticeWalState.Prepared ? 1L : 2L,
                createdAt,
                fields));
        }
    }
#endif

    private static JusticePlayerProfileState[] CreateProfilesWithIncarceratedOwner()
    {
        JusticePlayerProfileState[] profiles = new JusticePlayerProfileState[3];
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            profiles[slot] = new JusticePlayerProfileState(slot)
            {
                CaseState = new JusticeCaseState { Enabled = true },
                RecordState = new JusticeRecordState(),
                CustodyXml = (string)InvokeStatic(
                    "CreateCanonicalEmptyJusticeCustodyXml"),
                LastCanonicalPlayerModel = 1000 + slot
            };
        }

        JusticeCaseState state = profiles[0].CaseState;
        const string incident = "incident:adopted-respawn";
        const string episode = "episode:adopted-respawn";
        const string custodyEpisode = "custody:adopted-respawn";
        state.ActiveScore = 75;
        state.FineDue = 0L;
        state.SentenceSeconds = 600;
        state.WantedEpisodeId = episode;
        state.CustodyEpisodeId = custodyEpisode;
        state.LastCrimeKind = JusticeCrimeKind.SimpleAssault;
        state.LastCrimeLabel = "Agression test";
        state.Phase = JusticePhase.Incarcerated;
        state.ProcessedIncidentIds.Add(incident);
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:adopted-respawn",
            IncidentId = incident,
            EpisodeId = episode,
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression test",
            Points = 75,
            Fine = 10000L,
            SentenceSeconds = 600,
            IsAdjudicated = true
        });
        state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyConviction,
            custodyEpisode));
        state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyFine,
            custodyEpisode));

        string convictionId = "conviction:" + custodyEpisode;
        JusticeRecordState record = profiles[0].RecordState;
        record.AppliedConvictionIds.Add(convictionId);
        record.PinnedConvictionId = convictionId;
        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = convictionId,
            JudgedAtUtc = new DateTime(
                2026,
                9,
                3,
                12,
                0,
                0,
                DateTimeKind.Utc),
            Severity = JusticePolicy.GetSeverity(75),
            Score = 75,
            Fine = 10000L,
            SentenceSeconds = 600
        };
        conviction.Charges.Add(new JusticeConvictionChargeSummary
        {
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression test",
            Points = 75,
            Fine = 10000L,
            SentenceSeconds = 600
        });
        record.Convictions.Add(conviction);
        return profiles;
    }

    private static object CreateCustodyScript(
        JusticePlayerProfileState[] profiles,
        int activeSlot)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState activeCase = activeSlot >= 0 && profiles != null
            ? profiles[activeSlot].CaseState
            : new JusticeCaseState();
        JusticeRecordState activeRecord = activeSlot >= 0 && profiles != null
            ? profiles[activeSlot].RecordState
            : new JusticeRecordState();
        SetField(script, "_justiceCaseState", activeCase);
        SetField(script, "_justiceRecordState", activeRecord);
        SetField(script, "_justiceEnabled", activeCase.Enabled);
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceActivePlayerProfileSlot", activeSlot);
        SetField(script, "_justiceLastCanonicalPlayerSlot", activeSlot);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetField(script, "_justiceCustodyPlayerSlot", activeSlot);
        SetField(
            script,
            "_justiceReleaseSelectedWeaponHash",
            unchecked((int)0xA2719263));

        if (activeSlot >= 0)
        {
            SetField(script, "_justiceCustodyRuntimeActive", true);
            SetField(script, "_justiceCustodyInitialSentenceSeconds", 600);
            SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
        }

        return script;
    }

    private static XElement GetPersistedActiveProfile(XDocument document)
    {
        XElement recovery = document.Root.Element("RuntimeRecovery");
        Assert.IsNotNull(recovery);
        string activeSlot = (string)recovery.Attribute("activePlayerSlot");
        XElement profile = document.Root
            .Element("Profiles")
            .Elements("Profile")
            .Single(candidate => string.Equals(
                (string)candidate.Attribute("slot"),
                activeSlot,
                StringComparison.Ordinal));
        return profile;
    }

    private static void WithTemporaryJusticeDirectory(Action<string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJAdoptedRespawn-" + Guid.NewGuid().ToString("N"));
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            test(directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previous);
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void DeleteTemporaryDirectory(string directory)
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
            }
            catch (UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw;
                }
            }

            Thread.Sleep(25);
        }
    }

    private static void AwaitQueuedPersistence(object script)
    {
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"),
            "Le writer doit confirmer le snapshot adopté sur disque.");
    }

    private static T GetPrivateConstant<T>(string name)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateStatic);
        Assert.IsNotNull(field, "Constante privée absente : " + name);
        return (T)field.GetRawConstantValue();
    }

    private static object Invoke(
        object target,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = ScriptType
            .GetMethods(PrivateInstance)
            .Single(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(target, arguments);
    }

    private static object InvokeStatic(
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = ScriptType
            .GetMethods(PrivateStatic)
            .Single(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(null, arguments);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé absent : " + name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé absent : " + name);
        return (T)field.GetValue(target);
    }

    private static void SetEnumField(
        object target,
        string fieldName,
        string value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ enum privé absent : " + fieldName);
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        int nameIndex = source.IndexOf(
            "private bool " + methodName + "(",
            StringComparison.Ordinal);
        Assert.IsTrue(nameIndex >= 0, "Méthode absente : " + methodName);
        int openBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openBrace >= 0, "Corps absent : " + methodName);
        int depth = 0;
        for (int index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openBrace, index - openBrace + 1);
                }
            }
        }

        Assert.Fail("Corps incomplet : " + methodName);
        return string.Empty;
    }

    private static void AssertOrdered(string source, params string[] tokens)
    {
        int cursor = -1;
        foreach (string token in tokens)
        {
            int found = source.IndexOf(
                token,
                cursor + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                found > cursor,
                "Jeton absent ou désordonné : " + token);
            cursor = found;
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(
            AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "GTA5modDEV.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du dépôt.");
        return string.Empty;
    }
}
