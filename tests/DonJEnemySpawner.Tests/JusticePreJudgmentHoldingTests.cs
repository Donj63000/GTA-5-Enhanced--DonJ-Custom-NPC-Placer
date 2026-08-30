using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticePreJudgmentHoldingTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void PreJudgmentHolding_LeavesBusinessStateUntouchedAndOrdersVisualProofs()
    {
        string custodySource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs");
        string holding = ReadMethod(
            custodySource,
            "UpdateJusticePoliceDeathPreJudgmentHolding");
        string move = ReadMethod(
            custodySource,
            "TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer");

        AssertDoesNotContain(holding, "JusticeMarkStateDirty");
        AssertDoesNotContain(holding, "JusticeFlushStateNow");
        AssertDoesNotContain(holding, "PersistJustice");
        AssertDoesNotContain(holding, "ClearJusticeWanted");
        AssertDoesNotContain(holding, "EnsureJusticeInventory");
        AssertDoesNotContain(holding, "_justiceCaseState.Phase =");
        AssertDoesNotContain(holding, "_justiceCaseState.SentenceSeconds =");
        AssertDoesNotContain(holding, "_justiceCaseState.CustodyEpisodeId =");
        AssertDoesNotContain(holding, "_justiceCustodySite =");
        AssertDoesNotContain(move, "DO_SCREEN_FADE_IN");
        AssertDoesNotContain(move, "TeleportPlayerWithFadeSafe(");
        AssertDoesNotContain(move, "TryJusticeEmergencyTeleport(");
        AssertDoesNotContain(move, "Wait(");
        AssertOrdered(
            move,
            "REQUEST_COLLISION_AT_COORD",
            "IsJusticePreJudgmentHoldingGroundReady(safeTarget)",
            "SetEntityCoordsNoOffsetSafe(player, safeTarget)",
            "JusticeNativeHasCollisionLoadedAroundEntity");

        AssertOrdered(
            holding,
            "ReassertJusticeCustodyRespawnTransferMask()",
            "TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(",
            "IsInsideJusticeCustodyLayout(layout, player.Position)",
            "EnsureJusticePlayerMobilityCore(player)",
            "TryRestoreJusticeCustodyRespawnTransferMask()");
    }

    [TestMethod]
    public void PreJudgmentHolding_RunsBeforeBlockedLatePathsAndSurvivesCapturedWaiting()
    {
        string justiceSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs");
        string update = ReadMethod(justiceSource, "UpdateJusticeSystem");
        string failSafe = ReadMethod(
            justiceSource,
            "UpdateJusticeFailSafeMaintenance");
        AssertOrdered(
            update,
            "UpdateJusticeCustodyRespawnTransferMask(player)",
            "UpdateJusticePoliceDeathPreJudgmentHolding(player, nowRaw)",
            "HasOpenJusticeProfileResetWal()",
            "_justiceBackupRepairPending");
        StringAssert.Contains(
            failSafe,
            "UpdateJusticePoliceDeathPreJudgmentHolding(player, now)");

        string custodySource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs");
        string holding = ReadMethod(
            custodySource,
            "UpdateJusticePoliceDeathPreJudgmentHolding");
        StringAssert.Contains(
            holding,
            "RefreshJusticePreJudgmentHoldingIntent(player)");
        StringAssert.Contains(
            holding,
            "MustBlockJusticeLateForPreJudgmentHolding()");
        StringAssert.Contains(
            holding,
            "_justicePoliceDeathPreJudgmentHoldingEstablished");
        StringAssert.Contains(
            ReadMethod(
                custodySource,
                "RefreshJusticePreJudgmentHoldingIntent"),
            "JusticePreJudgmentHoldingSource.Captured");
        StringAssert.Contains(
            ReadMethod(custodySource, "CanMaskJusticeCustodyRespawnOrigin"),
            "IsInsideJusticePoliceDeathPreJudgmentHolding(player.Position)");

        string custodyUpdate = ReadMethod(
            custodySource,
            "JusticeUpdateCustody");
        AssertOrdered(
            custodyUpdate,
            "_justiceCaseState.Phase == JusticePhase.Captured",
            "!IsJusticeCapturePrecommitConfirmedForCurrentEpisode()",
            "_justiceCaptureRetryPending",
            "ResetJusticeCustodyClock(now)",
            "return;",
            "JusticeBeginCustodyTransfer(false)");
    }

    [TestMethod]
    public void CapturePrecommit_IsExplicitlyRevalidatedBeforeLateTransfer()
    {
        string justiceSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs");
        string early = ReadMethod(justiceSource, "UpdateJusticeEarly");
        StringAssert.Contains(
            early,
            "ArmJusticeCapturePrecommitRetryIfRequired()");
        StringAssert.Contains(
            early,
            "IsJusticeCapturedAwaitingPrecommit()" );

        string begin = ReadMethod(justiceSource, "BeginJusticeCapture");
        AssertOrdered(
            begin,
            "ResetJusticeCapturePrecommitConfirmation()",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "ConfirmJusticeCapturePrecommit()",
            "CompleteJusticeCaptureAfterCommit(");
        Assert.AreEqual(
            2,
            CountOccurrences(begin, "ConfirmJusticeCapturePrecommit();"),
            "Les deux chemins Capture doivent confirmer seulement après leur précommit.");

        string custodySource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs");
        string rearm = ReadMethod(
            custodySource,
            "ArmJusticeCapturePrecommitRetryIfRequired");
        StringAssert.Contains(rearm, "_justiceCaptureRetryPending = true");
        StringAssert.Contains(
            rearm,
            "_justiceCustodyWaitingForRespawn ||");
    }

    [TestMethod]
    public void RepairAndPendingWalSources_RequireExactStoredOwnersWithoutBusinessMutation()
    {
        string justiceSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs");
        string observe = ReadMethod(
            justiceSource,
            "ObserveJusticeFrontsWhilePersistenceBlocked");
        AssertOrdered(
            observe,
            "TryStoreJusticeDeferredRuntimeFront(",
            "ArmJusticeRepairPreJudgmentHoldingIntent(");

        string custodySource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs");
        string repair = ReadMethod(
            custodySource,
            "ArmJusticeRepairPreJudgmentHoldingIntent");
        StringAssert.Contains(
            repair,
            "JusticeDeferredRuntimeFront.ArrestEnded");
        StringAssert.Contains(
            repair,
            "TryGetJusticeDeferredRuntimeFrontLot(");
        StringAssert.Contains(
            repair,
            "currentSlot == ownerSlot || currentSlot == -1");

        string refresh = ReadMethod(
            custodySource,
            "RefreshJusticePreJudgmentHoldingIntent");
        AssertOrdered(
            refresh,
            "TryResolveJusticeStoredRepairHoldingIntent(",
            "HasJusticeActivePoliceDeathPreJudgmentIntent()");
        string storedRepair = ReadMethod(
            custodySource,
            "TryResolveJusticeStoredRepairHoldingIntent");
        StringAssert.Contains(storedRepair, "TryResolveJusticeStoredRepairHoldingCandidate(");
        StringAssert.Contains(storedRepair, "JusticePlayerProfileCount - 1");
        AssertDoesNotContain(storedRepair, "JusticeMarkStateDirty");
        AssertDoesNotContain(storedRepair, "PersistJustice");

        string pending = ReadMethod(
            custodySource,
            "TryResolveJusticePendingWalPoliceDeathHoldingIntent");
        StringAssert.Contains(pending, "IsJusticeDeathFrontWalRecordExact(record)");
        StringAssert.Contains(pending, "JusticePoliceDeathFrontMode");
        StringAssert.Contains(pending, "recordSlot != record.ProfileSlot");
        StringAssert.Contains(
            pending,
            "JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(");
        AssertDoesNotContain(pending, "ApplyJusticeDeathFrontToRuntime");
        AssertDoesNotContain(pending, "JusticeMarkStateDirty");

        string sentence = ReadMethod(
            custodySource,
            "GetJusticePreJudgmentHoldingSentenceSeconds");
        AssertDoesNotContain(sentence, "SentenceSeconds =");
    }

    [TestMethod]
    public void PreJudgmentHolding_ClearsOnlyAtExplicitLifecycleBoundaries()
    {
        string custodySource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs");
        string transfer = ReadMethod(
            custodySource,
            "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "!transferred",
            "TryRestoreJusticeCustodyRespawnTransferMask()",
            "ResetJusticePoliceDeathPreJudgmentHoldingState()",
            "_justicePoliceDeathRespawnMaskIntentPending = false");

        StringAssert.Contains(
            ReadMethod(
                custodySource,
                "CancelJusticePoliceDeathRespawnMaskIntentIfUnclaimed"),
            "ResetJusticePoliceDeathPreJudgmentHoldingState()");
        StringAssert.Contains(
            ReadMethod(custodySource, "ResetJusticeCustodyPersistentFields"),
            "ResetJusticePoliceDeathPreJudgmentHoldingState()");
        StringAssert.Contains(
            ReadMethod(custodySource, "JusticeShutdownCustody"),
            "ResetJusticePoliceDeathPreJudgmentHoldingState()");
        StringAssert.Contains(
            transfer,
            "ResetJusticeCapturePrecommitConfirmation()");
    }

#if DONJ_STUB_API
    private const ulong GroundReadyNative = 0xC906A7DAB05C8D2BUL;
    private const ulong CollisionReadyNative = 0xE9676F61BC0B3321UL;

    [DataTestMethod]
    [DataRow(299, "MissionRow")]
    [DataRow(300, "Bolingbroke")]
    public void PreJudgmentHolding_SelectsSiteAndPreservesGameplayState(
        int sentenceSeconds,
        string expectedSite)
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 901;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        player.FreezePosition = true;
        GTA.Game.Player.Money = 4321;
        GTA.Game.Player.WantedLevel = 4;

        object script = CreatePendingPoliceDeathScript(
            player,
            sentenceSeconds);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        JusticePhase phaseBefore = state.Phase;
        string episodeBefore = state.WantedEpisodeId;

        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        bool blocksLate = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000);

        Assert.IsTrue(blocksLate);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(
            expectedSite,
            GetFieldObject(
                script,
                "_justicePoliceDeathPreJudgmentHoldingSite").ToString());
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsFalse(player.FreezePosition);
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));

        Assert.AreEqual(phaseBefore, state.Phase);
        Assert.AreEqual(sentenceSeconds, state.SentenceSeconds);
        Assert.AreEqual(episodeBefore, state.WantedEpisodeId);
        Assert.AreEqual(string.Empty, state.CustodyEpisodeId);
        Assert.AreEqual(4321, GTA.Game.Player.Money);
        Assert.AreEqual(4, GTA.Game.Player.WantedLevel);
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
        Assert.AreEqual(
            "None",
            GetFieldObject(script, "_justiceCustodySite").ToString());
        Assert.IsNull(GetFieldObject(script, "_justiceWeaponSnapshot"));
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));

        int fadeOutIndex = GTA.StubRuntime.NativeCalls
            .Select((call, index) => new { call, index })
            .First(pair => pair.call.Hash ==
                (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT)
            .index;
        int fadeInIndex = GTA.StubRuntime.NativeCalls
            .Select((call, index) => new { call, index })
            .Last(pair => pair.call.Hash ==
                (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN)
            .index;
        Assert.IsTrue(
            fadeOutIndex < fadeInIndex,
            "Le masque doit précéder le déplacement et sa restitution vérifiée.");
    }

    [TestMethod]
    public void PreJudgmentHolding_KeepsWholeEnclosureVisibleAndReholdsDuringCapturedWaiting()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 902;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        object script = CreatePendingPoliceDeathScript(player, 600);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");

        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000);

        player.Position = new GTA.Math.Vector3(1760.0f, 2700.0f, 45.0f);
        int fadeOutInside = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_OUT);
        int fadeInInside = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN);
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000);
        Assert.AreEqual(fadeOutInside, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_OUT));
        Assert.AreEqual(fadeInInside, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(1760.0f, player.Position.X, 0.001f);
        Assert.AreEqual(2700.0f, player.Position.Y, 0.001f);

        // Je simule le précommit Capture : le latch brut est consommé, mais le
        // transfert normal n'a pas encore été vérifié.
        state.Phase = JusticePhase.Captured;
        state.CustodyEpisodeId = "custody:pre-judgment";
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyWaitingForRespawn", true);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", false);
        Invoke(script, "ConfirmJusticeCapturePrecommit");
        player.Position = new GTA.Math.Vector3(1900.0f, 2800.0f, 45.0f);
        bool blocksNormalController = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            3000);
        Assert.IsFalse(
            blocksNormalController,
            "Le holding maintient l'enceinte sans affamer le transfert normal Captured/waiting.");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.AreEqual(JusticePhase.Captured, state.Phase);
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual("custody:pre-judgment", state.CustodyEpisodeId);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
    }

    [TestMethod]
    public void PreJudgmentHolding_AcceptsOnlyProvenCustomModelAndPreservesIntentOnHeroSwitch()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 903;
        player.Model = new GTA.Model("mp_m_freemode_01");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        object script = CreatePendingPoliceDeathScript(player, 120);

        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));

        player.Model = new GTA.Model("player_one");
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000);
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.IsTrue(
            GetField<bool>(script, "_justicePoliceDeathRespawnMaskIntentPending"),
            "Le switch rend l'écran mais laisse le front au profil propriétaire.");

        GTA.StubRuntime.Reset();
        player = GTA.Game.Player.Character;
        player.Handle = 904;
        player.Model = new GTA.Model("mp_f_freemode_01");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        script = CreatePendingPoliceDeathScript(player, 120);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            new GTA.Model("mp_m_freemode_01").Hash);
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000),
            "Une identité custom contradictoire bloque le Late sans être téléportée.");
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(0, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_OUT));
    }

    [TestMethod]
    public void CapturedHolding_BlocksUntilExactPrecommitAndRearmsDeathReload()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 905;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        GTA.Game.Player.Money = 9876;
        GTA.Game.Player.WantedLevel = 3;

        object script = CreateCapturedScript(player, 420, false);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        SetField(script, "_justiceCaptureRetryPending", true);
        bool blocksBeforeCommit = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000);

        Assert.IsTrue(blocksBeforeCommit);
        Assert.IsTrue(GetField<bool>(script, "_justiceCaptureRetryPending"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCapturePrecommitConfirmed"));
        Assert.AreEqual(JusticePhase.Captured, state.Phase);
        Assert.AreEqual(420, state.SentenceSeconds);
        Assert.AreEqual("custody:captured", state.CustodyEpisodeId);
        Assert.AreEqual(9876, GTA.Game.Player.Money);
        Assert.AreEqual(3, GTA.Game.Player.WantedLevel);
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));

        Invoke(script, "ConfirmJusticeCapturePrecommit");
        SetField(script, "_justiceCaptureRetryPending", false);
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        bool blocksAfterCommit = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000);
        Assert.IsFalse(
            blocksAfterCommit,
            "Le contrôleur normal reprend après preuve, tandis que le ped reste physiquement maintenu.");
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(JusticePhase.Captured, state.Phase);
        Assert.AreEqual(9876, GTA.Game.Player.Money);

        object reloaded = CreateCapturedScript(player, 420, true);
        SetField(
            reloaded,
            "_justicePursuitDeathObservedDuringSuspension",
            true);
        Invoke(reloaded, "ArmJusticeCapturePrecommitRetryIfRequired");
        Assert.IsTrue(GetField<bool>(reloaded, "_justiceCaptureRetryPending"));
        Assert.IsTrue(
            GetField<bool>(reloaded, "_justiceCaptureRetryDeath"),
            "Une Capture mortelle rechargée doit reprendre même si son latch pursuit reste armé.");
        Assert.IsTrue((bool)Invoke(
            reloaded,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            3000));
    }

    [TestMethod]
    public void RepairArrestHolding_UsesStoredEndedFrontAcrossInactiveOwner()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 906;
        player.Model = new GTA.Model("player_one");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        GTA.Game.Player.Money = 7654;
        GTA.Game.Player.WantedLevel = 0;

        JusticePlayerProfileState[] profiles = CreateEnabledProfiles();
        profiles[0].CaseState.Phase = JusticePhase.Wanted;
        profiles[0].CaseState.SentenceSeconds = 60;
        profiles[1].CaseState.Phase = JusticePhase.Surrendering;
        profiles[1].CaseState.SentenceSeconds = 480;
        object script = CreateProfileBackedScript(profiles, 0, 1, player);
        Type frontsType = GetNestedType("JusticeDeferredRuntimeFront");
        object arrestStarted = Enum.Parse(frontsType, "ArrestStarted");
        object arrestEnded = Enum.Parse(frontsType, "ArrestEnded");

        Assert.IsTrue((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            1,
            player.Model.Hash,
            arrestStarted,
            true));
        Invoke(
            script,
            "ArmJusticeRepairPreJudgmentHoldingIntent",
            player,
            1,
            player.Model.Hash,
            arrestStarted,
            true);
        Assert.AreEqual(
            "None",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString(),
            "ArrestStarted seul reste révocable et ne doit pas confiner.");

        Assert.IsTrue((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            1,
            player.Model.Hash,
            arrestEnded,
            false));
        Invoke(
            script,
            "ArmJusticeRepairPreJudgmentHoldingIntent",
            player,
            1,
            player.Model.Hash,
            arrestEnded,
            false);
        bool blocksRepair = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000);

        Assert.IsTrue(blocksRepair);
        Assert.AreEqual(
            "RepairPoliceArrest",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        Assert.AreEqual(
            "Bolingbroke",
            GetFieldObject(
                script,
                "_justicePoliceDeathPreJudgmentHoldingSite").ToString());
        Assert.AreEqual(JusticePhase.Wanted, profiles[0].CaseState.Phase);
        Assert.AreEqual(JusticePhase.Surrendering, profiles[1].CaseState.Phase);
        Assert.AreEqual(60, profiles[0].CaseState.SentenceSeconds);
        Assert.AreEqual(480, profiles[1].CaseState.SentenceSeconds);
        Assert.AreEqual(7654, GTA.Game.Player.Money);
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));

        Invoke(script, "ClearJusticeDeferredRuntimeFronts");
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000));
        Assert.AreEqual(
            "RepairPoliceArrest",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString(),
            "La disparition du lot après réparation ne doit pas ouvrir un trou avant la sonde BUSTED.");
    }

    [TestMethod]
    public void BolingbrokeHolding_WaitsForGroundAndCollisionBeforeFadeIn()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 908;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        player.IsInvincible = false;
        object script = CreatePendingPoliceDeathScript(player, 600);
        bool groundReady = false;
        bool collisionReady = false;
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == GroundReadyNative)
            {
                return groundReady;
            }
            if (hash == CollisionReadyNative)
            {
                return collisionReady;
            }
            return null;
        };

        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingStreamingPending"));
        Assert.IsTrue(player.IsInvincible);
        Assert.AreEqual(0, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(310.0f, player.Position.X, 0.001f);

        groundReady = true;
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingPositionApplied"));
        Assert.IsTrue(player.FreezePosition);
        Assert.IsTrue(player.IsInvincible);
        Assert.AreEqual(0, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN));

        collisionReady = true;
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            4000));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePreJudgmentHoldingStreamingPending"));
        Assert.IsFalse(player.FreezePosition);
        Assert.IsFalse(player.IsInvincible);
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.AreEqual(1, CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN));
    }

    [DataTestMethod]
    [DataRow("DeathStarted", "RepairPoliceDeath", false)]
    [DataRow("ArrestEnded", "RepairPoliceArrest", false)]
    [DataRow("DeathStarted", "RepairPoliceDeath", true)]
    [DataRow("ArrestEnded", "RepairPoliceArrest", true)]
    public void RepairHolding_RearmsAfterOwnerRoundTripWithoutBusinessMutation(
        string frontName,
        string expectedSource,
        bool customIdentity)
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 909;
        GTA.Model ownerModel = new GTA.Model(
            customIdentity ? "mp_m_freemode_01" : "player_one");
        player.Model = ownerModel;
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        GTA.Game.Player.Money = 8765;
        GTA.Game.Player.WantedLevel = 2;

        JusticePlayerProfileState[] profiles = CreateEnabledProfiles();
        profiles[0].CaseState.Phase = JusticePhase.Wanted;
        profiles[0].CaseState.SentenceSeconds = 90;
        profiles[0].CaseState.WantedEpisodeId = "wanted:p";
        profiles[1].CaseState.Phase = frontName == "ArrestEnded"
            ? JusticePhase.Surrendering
            : JusticePhase.Wanted;
        profiles[1].CaseState.SentenceSeconds = 480;
        profiles[1].CaseState.WantedEpisodeId = "wanted:q";
        int currentSlot = customIdentity ? -1 : 1;
        object script = CreateProfileBackedScript(
            profiles,
            0,
            currentSlot,
            player);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 1);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => currentSlot));
        Type frontsType = GetNestedType("JusticeDeferredRuntimeFront");
        object observed = Enum.Parse(frontsType, frontName);

        Assert.IsTrue((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            1,
            ownerModel.Hash,
            observed,
            true));
        Invoke(
            script,
            "ArmJusticeRepairPreJudgmentHoldingIntent",
            player,
            1,
            ownerModel.Hash,
            observed,
            true);
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000));
        Assert.AreEqual(
            expectedSource,
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());

        if (frontName == "ArrestEnded")
        {
            // Je reproduis la fenêtre post-réparation : le lot est consommé,
            // mais la sonde BUSTED n'a pas encore décidé capture ou mandat.
            Invoke(script, "ClearJusticeDeferredRuntimeFronts");
        }

        currentSlot = 0;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        Assert.IsFalse((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000));
        Assert.AreEqual(
            "None",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());

        currentSlot = customIdentity ? -1 : 1;
        player.Model = ownerModel;
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        Assert.IsTrue((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            3000));
        Assert.AreEqual(
            expectedSource,
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(JusticePhase.Wanted, profiles[0].CaseState.Phase);
        Assert.AreEqual(
            frontName == "ArrestEnded"
                ? JusticePhase.Surrendering
                : JusticePhase.Wanted,
            profiles[1].CaseState.Phase);
        Assert.AreEqual(90, profiles[0].CaseState.SentenceSeconds);
        Assert.AreEqual(480, profiles[1].CaseState.SentenceSeconds);
        Assert.AreEqual("wanted:p", profiles[0].CaseState.WantedEpisodeId);
        Assert.AreEqual("wanted:q", profiles[1].CaseState.WantedEpisodeId);
        Assert.AreEqual(8765, GTA.Game.Player.Money);
        Assert.AreEqual(2, GTA.Game.Player.WantedLevel);
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsNull(GetFieldObject(script, "_justicePendingDeathFrontWalRecord"));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void PendingWalDeathHolding_PreservesPreparedRecordForCanonicalAndCustomRespawn(
        int currentSlot)
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 907;
        player.Model = new GTA.Model(
            currentSlot == 0 ? "player_zero" : "mp_m_freemode_01");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        GTA.Game.Player.Money = 6543;
        GTA.Game.Player.WantedLevel = 2;

        JusticePlayerProfileState[] profiles = CreateEnabledProfiles();
        profiles[0].CaseState.Phase = JusticePhase.Wanted;
        profiles[0].CaseState.SentenceSeconds = 180;
        object script = CreateProfileBackedScript(
            profiles,
            0,
            currentSlot,
            player);
        JusticeWalRecord prepared = CreatePendingDeathWalRecord(
            0,
            player.Model.Hash);
        SetField(script, "_justicePendingDeathFrontWalRecord", prepared);

        bool blocksLate = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            1000);

        Assert.IsTrue(blocksLate);
        Assert.AreSame(
            prepared,
            GetField<JusticeWalRecord>(
                script,
                "_justicePendingDeathFrontWalRecord"));
        Assert.AreEqual(
            "PendingWalPoliceDeath",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(JusticePhase.Wanted, profiles[0].CaseState.Phase);
        Assert.AreEqual(180, profiles[0].CaseState.SentenceSeconds);
        Assert.AreEqual(6543, GTA.Game.Player.Money);
        Assert.AreEqual(2, GTA.Game.Player.WantedLevel);
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
    }

    private static object CreatePendingPoliceDeathScript(
        GTA.Ped player,
        int sentenceSeconds)
    {
        ConfigureHoldingStreamingReady();
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Wanted,
            SentenceSeconds = sentenceSeconds,
            WantedEpisodeId = "wanted:pre-judgment"
        };
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", new JusticeRecordState());
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            player.Model.Hash);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetField(script, "_justicePoliceDeathRespawnMaskIntentPending", true);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingOwnerSlot",
            -1);
        return script;
    }

    private static object CreateCapturedScript(
        GTA.Ped player,
        int sentenceSeconds,
        bool waitingForRespawn)
    {
        ConfigureHoldingStreamingReady();
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Captured,
            SentenceSeconds = sentenceSeconds,
            WantedEpisodeId = "wanted:captured",
            CustodyEpisodeId = "custody:captured"
        };
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", new JusticeRecordState());
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyWaitingForRespawn", waitingForRespawn);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingOwnerSlot",
            -1);
        return script;
    }

    private static JusticePlayerProfileState[] CreateEnabledProfiles()
    {
        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
        for (int index = 0; index < profiles.Length; index++)
        {
            profiles[index].CaseState.Enabled = true;
        }
        return profiles;
    }

    private static object CreateProfileBackedScript(
        JusticePlayerProfileState[] profiles,
        int activeSlot,
        int currentSlot,
        GTA.Ped player)
    {
        ConfigureHoldingStreamingReady();
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceCaseState", profiles[activeSlot].CaseState);
        SetField(script, "_justiceRecordState", profiles[activeSlot].RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceActivePlayerProfileSlot", activeSlot);
        SetField(script, "_justiceLastCanonicalPlayerSlot", activeSlot);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => currentSlot));
        SetField(
            script,
            "_justicePoliceDeathPreJudgmentHoldingOwnerSlot",
            -1);
        return script;
    }

    private static void ConfigureHoldingStreamingReady()
    {
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == GroundReadyNative || hash == CollisionReadyNative
                ? (object)true
                : null;
    }

    private static JusticeWalRecord CreatePendingDeathWalRecord(
        int profileSlot,
        int playerModel)
    {
        string identityKey = "slot:" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":model:" + playerModel.ToString(CultureInfo.InvariantCulture);
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokeStatic(
                "CreateJusticeDeathFrontWalFields",
                "PoliceCapture",
                0L,
                0L,
                identityKey,
                string.Empty,
                0,
                profileSlot,
                playerModel,
                profileSlot,
                playerModel);
        return new JusticeWalRecord(
            "death-front:pre-judgment",
            "DeathFront",
            profileSlot,
            JusticeWalState.Prepared,
            0L,
            DateTime.UtcNow.Ticks,
            fields);
    }

    private static int CountNative(GTA.Native.Hash hash)
    {
        return GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)hash);
    }
#endif

    private static string ReadSource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { GetRepositoryRoot() }.Concat(segments).ToArray()));
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
        int nameIndex = -1;
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
            string declarationPrefix = source.Substring(
                lineStart < 0 ? 0 : lineStart + 1,
                candidate - (lineStart < 0 ? 0 : lineStart + 1));
            if (declarationPrefix.IndexOf(
                    "private ",
                    StringComparison.Ordinal) >= 0)
            {
                nameIndex = candidate;
                break;
            }
            searchFrom = candidate + methodName.Length + 1;
        }
        Assert.IsTrue(nameIndex >= 0, "Méthode source introuvable : " + methodName);
        int openBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openBrace >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(openBrace, index - openBrace + 1);
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
            int index = source.IndexOf(
                fragment,
                cursor + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                index > cursor,
                "Fragment absent ou désordonné : " + fragment);
            cursor = index;
        }
    }

    private static void AssertDoesNotContain(string source, string fragment)
    {
        Assert.IsFalse(
            source.IndexOf(fragment, StringComparison.Ordinal) >= 0,
            "Fragment interdit présent : " + fragment);
    }

    private static int CountOccurrences(string source, string fragment)
    {
        int count = 0;
        int cursor = 0;
        while (cursor < source.Length)
        {
            int index = source.IndexOf(
                fragment,
                cursor,
                StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }
            count++;
            cursor = index + fragment.Length;
        }
        return count;
    }

#if DONJ_STUB_API
    private static object Invoke(
        object target,
        string methodName,
        params object[] arguments)
    {
        MethodInfo[] methods = target.GetType()
            .GetMethods(PrivateInstance)
            .Where(method => method.Name == methodName &&
                method.GetParameters().Length == arguments.Length)
            .ToArray();
        Assert.AreEqual(1, methods.Length, "Méthode privée ambiguë : " + methodName);
        return methods[0].Invoke(target, arguments);
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

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static object InvokeStatic(
        string methodName,
        params object[] arguments)
    {
        MethodInfo[] methods = ScriptType
            .GetMethods(PrivateStatic)
            .Where(method => method.Name == methodName &&
                method.GetParameters().Length == arguments.Length)
            .ToArray();
        Assert.AreEqual(1, methods.Length, "Méthode statique ambiguë : " + methodName);
        return methods[0].Invoke(null, arguments);
    }

    private static Type GetNestedType(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type imbriqué introuvable : " + name);
        return type;
    }
#endif
}
