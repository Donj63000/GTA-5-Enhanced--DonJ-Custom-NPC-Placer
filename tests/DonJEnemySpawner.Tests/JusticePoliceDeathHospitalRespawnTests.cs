using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticePoliceDeathHospitalRespawnTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

#if DONJ_STUB_API
    private const ulong GroundReadyNative = 0xC906A7DAB05C8D2BUL;
    private const ulong CollisionReadyNative = 0xE9676F61BC0B3321UL;
    private const ulong ScreenFadedOutNative = 0xB16FCE9DDC7BA182UL;
    private const ulong ScreenFadingOutNative = 0x797AC7CB535BA28FUL;

    [TestMethod]
    public void RespawnMask_VerifiesTheRealFadeAndReassertsWhileGtaKeepsTheScreenOpen()
    {
        StubRuntime.Reset();
        Ped player = Game.Player.Character;
        player.Handle = 1199;
        player.Model = new Model("player_zero");
        player.IsDead = true;
        Game.Player.IsDead = true;
        object script = CreatePendingProfileScenario(
            player,
            180,
            CreatePoliceDeathRecord(player.Model.Hash, JusticeWalState.Ambiguous));
        bool refuseFadeState = true;
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == ScreenFadedOutNative ||
                hash == ScreenFadingOutNative)
            {
                return refuseFadeState ? (object)false : null;
            }
            return null;
        };

        Invoke(
            script,
            "ArmJusticePoliceDeathRespawnMaskForAcceptedFront",
            0,
            player.Model.Hash);
        int firstAttempts = CountNative(Hash.DO_SCREEN_FADE_OUT);
        Assert.IsTrue(firstAttempts >= 1);
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyRespawnTransferPending"));
        Assert.IsTrue(
            GetField<bool>(script, "_justiceCustodyRespawnMaskNeedsRearm"),
            "Un appel sans effet ne doit jamais être traité comme un écran noir.");

        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue(
            CountNative(Hash.DO_SCREEN_FADE_OUT) > firstAttempts,
            "Le masque doit être réaffirmé même tant que le ped est mort.");
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyRespawnMaskNeedsRearm"));

        refuseFadeState = false;
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRespawnMaskNeedsRearm"));
    }

    [DataTestMethod]
    [DataRow("Prepared", 180, "MissionRow")]
    [DataRow("Attempted", 180, "MissionRow")]
    [DataRow("Ambiguous", 180, "MissionRow")]
    [DataRow("Prepared", 600, "Bolingbroke")]
    [DataRow("Attempted", 600, "Bolingbroke")]
    [DataRow("Ambiguous", 600, "Bolingbroke")]
    public void PoliceCapture_OpenWalMasksDeathThenHoldsHospitalRespawnWithoutBusinessMutation(
        string walStateName,
        int sentenceSeconds,
        string expectedSite)
    {
        StubRuntime.Reset();
        ConfigureHoldingStreamingReady();
        Ped player = Game.Player.Character;
        player.Handle = 1201;
        player.Model = new Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        player.IsDead = true;
        Game.Player.IsDead = true;
        Game.Player.Money = 4321;
        Game.Player.WantedLevel = 2;

        JusticeWalState walState = (JusticeWalState)Enum.Parse(
            typeof(JusticeWalState),
            walStateName);
        object script = CreatePendingProfileScenario(
            player,
            sentenceSeconds,
            CreatePoliceDeathRecord(player.Model.Hash, walState));
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        string episodeBefore = state.WantedEpisodeId;
        int scoreBefore = state.ActiveScore;
        long fineBefore = state.FineDue;

        Game.GameTime = 100;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");

        Assert.IsTrue(
            CountNative(Hash.DO_SCREEN_FADE_OUT) >= 1,
            "Le Prepared exact doit masquer WASTED avant que GTA rende l'hôpital.");
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
        Assert.AreEqual(sentenceSeconds, state.SentenceSeconds);
        Assert.AreEqual(scoreBefore, state.ActiveScore);
        Assert.AreEqual(fineBefore, state.FineDue);
        Assert.AreEqual(episodeBefore, state.WantedEpisodeId);
        Assert.AreEqual(4321, Game.Player.Money);
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));

        // Je reproduis ici le premier ped vivant rendu par l'hôpital vanilla.
        // Le même tick Early/Late doit le retirer de cette origine sans juger le dossier.
        player.IsDead = false;
        Game.Player.IsDead = false;
        player.IsInvincible = true;
        player.FreezePosition = true;
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        Game.GameTime = 1000;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");

        Assert.AreEqual(
            expectedSite,
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
        Assert.IsFalse(player.IsInvincible);
        Assert.IsFalse(player.FreezePosition);
        Assert.IsTrue(CountNative(Hash.DO_SCREEN_FADE_IN) >= 1);
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
        Assert.AreEqual(sentenceSeconds, state.SentenceSeconds);
        Assert.AreEqual(scoreBefore, state.ActiveScore);
        Assert.AreEqual(fineBefore, state.FineDue);
        Assert.AreEqual(episodeBefore, state.WantedEpisodeId);
        Assert.AreEqual(string.Empty, state.CustodyEpisodeId);
        Assert.AreEqual(4321, Game.Player.Money);
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles")[0].PendingDeathCapture);

        int stableFadeOutCount = CountNative(Hash.DO_SCREEN_FADE_OUT);
        int stableFadeInCount = CountNative(Hash.DO_SCREEN_FADE_IN);
        for (int tick = 0; tick < 40; tick++)
        {
            Game.GameTime = 1250 + (tick * 250);
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
        }

        Assert.AreEqual(
            stableFadeOutCount,
            CountNative(Hash.DO_SCREEN_FADE_OUT),
            "Le WAL ouvert ne doit plus réarmer le noir après le maintien vérifié.");
        Assert.AreEqual(
            stableFadeInCount,
            CountNative(Hash.DO_SCREEN_FADE_IN),
            "Le maintien vérifié ne doit plus alterner restitution et nouveau masque.");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsFalse(player.IsInvincible);
        Assert.IsFalse(player.FreezePosition);
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
        Assert.AreEqual(sentenceSeconds, state.SentenceSeconds);
        Assert.AreEqual(scoreBefore, state.ActiveScore);
        Assert.AreEqual(fineBefore, state.FineDue);
        Assert.AreEqual(episodeBefore, state.WantedEpisodeId);
        Assert.AreEqual(4321, Game.Player.Money);
    }

    [TestMethod]
    public void PoliceCapture_ReloadedPendingProfileRearmsHoldingAndNeverMovesAnotherHero()
    {
        StubRuntime.Reset();
        ConfigureHoldingStreamingReady();
        Ped owner = Game.Player.Character;
        owner.Handle = 1202;
        owner.Model = new Model("player_zero");
        owner.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        owner.IsDead = true;
        Game.Player.IsDead = true;

        object script = CreatePendingProfileScenario(owner, 600, null);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", false);
        SetField(script, "_justicePoliceDeathRespawnMaskIntentPending", false);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerModelHash", 0);
        Assert.IsTrue(
            (bool)Invoke(script, "ActivateJusticePlayerProfile", 0),
            "Le reload doit reconstruire les latches runtime depuis le DTO du profil.");
        SetField(script, "_justiceNextEarlyScanAtMs", long.MaxValue);

        Game.GameTime = 100;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");
        Assert.IsTrue(
            CountNative(Hash.DO_SCREEN_FADE_OUT) >= 1,
            "Le profil PendingDeathCapture rechargé doit réarmer le masque sans latch volatile.");

        owner.IsDead = false;
        Game.Player.IsDead = false;
        owner.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        Game.GameTime = 500;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            owner.Position));

        int stableFadeOutCount = CountNative(Hash.DO_SCREEN_FADE_OUT);
        int stableFadeInCount = CountNative(Hash.DO_SCREEN_FADE_IN);
        for (int tick = 0; tick < 32; tick++)
        {
            Game.GameTime = 750 + (tick * 250);
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
        }
        Assert.AreEqual(
            stableFadeOutCount,
            CountNative(Hash.DO_SCREEN_FADE_OUT),
            "Le PendingDeathCapture rechargé ne doit pas créer de flash périodique.");
        Assert.AreEqual(
            stableFadeInCount,
            CountNative(Hash.DO_SCREEN_FADE_IN),
            "Le reload sans WAL runtime doit conserver une restitution unique.");

        int fadeOutBeforeSwitch = CountNative(Hash.DO_SCREEN_FADE_OUT);
        Ped otherHero = new Ped
        {
            Handle = 1203,
            Model = new Model("player_one"),
            Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f)
        };
        Game.Player.Character = otherHero;
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 1));
        Game.GameTime = 9000;
        Invoke(script, "UpdateJusticeEarly");
        // Je cible ici les deux contrôleurs placés en tête de UpdateJusticeSystem :
        // le script headless n'initialise volontairement pas le moteur d'incidents
        // du nouveau héros, qui est sans rapport avec la restitution du masque.
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", otherHero);
        Assert.IsFalse((bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            otherHero,
            9000));

        Assert.AreEqual(310.0f, otherHero.Position.X, 0.001f);
        Assert.AreEqual(-590.0f, otherHero.Position.Y, 0.001f);
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.AreEqual(
            fadeOutBeforeSwitch,
            CountNative(Hash.DO_SCREEN_FADE_OUT),
            "Le front de l'ancien profil ne doit jamais masquer le nouveau héros.");
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyRespawnRestorePending"));
        JusticePlayerProfileState ownerProfile = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles")[0];
        Assert.IsTrue(ownerProfile.PendingDeathCapture);
        Assert.AreEqual(0, ownerProfile.PendingDeathCapturePlayerSlot);
        Assert.AreEqual(owner.Model.Hash, ownerProfile.PendingDeathCapturePlayerModel);
        Assert.AreEqual(JusticePhase.Wanted, ownerProfile.CaseState.Phase);
    }

    [TestMethod]
    public void PoliceCapture_VerifiedHoldingRearmsExactlyOnceAfterLeavingEnclosure()
    {
        StubRuntime.Reset();
        ConfigureHoldingStreamingReady();
        Ped player = Game.Player.Character;
        player.Handle = 1205;
        player.Model = new Model("player_zero");
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        player.IsDead = true;
        Game.Player.IsDead = true;
        Game.Player.Money = 6543;

        object script = CreatePendingProfileScenario(
            player,
            600,
            CreatePoliceDeathRecord(
                player.Model.Hash,
                JusticeWalState.Ambiguous));
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");
        int scoreBefore = state.ActiveScore;
        long fineBefore = state.FineDue;

        Game.GameTime = 100;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");
        player.IsDead = false;
        Game.Player.IsDead = false;
        Game.GameTime = 1000;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));

        int fadeOutBeforeEscape = CountNative(Hash.DO_SCREEN_FADE_OUT);
        int fadeInBeforeEscape = CountNative(Hash.DO_SCREEN_FADE_IN);
        player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
        Game.GameTime = 1500;
        Invoke(script, "UpdateJusticeEarly");
        Invoke(script, "UpdateJusticeSystem");

        Assert.AreEqual(
            fadeOutBeforeEscape + 1,
            CountNative(Hash.DO_SCREEN_FADE_OUT),
            "Une sortie réelle de l'enceinte doit produire un unique nouveau masque.");
        Assert.AreEqual(
            fadeInBeforeEscape + 1,
            CountNative(Hash.DO_SCREEN_FADE_IN),
            "Le replacement vérifié doit produire une unique restitution.");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsFalse(player.IsInvincible);
        Assert.IsFalse(player.FreezePosition);

        int stableFadeOutCount = CountNative(Hash.DO_SCREEN_FADE_OUT);
        int stableFadeInCount = CountNative(Hash.DO_SCREEN_FADE_IN);
        for (int tick = 0; tick < 24; tick++)
        {
            Game.GameTime = 1750 + (tick * 250);
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
        }
        Assert.AreEqual(stableFadeOutCount, CountNative(Hash.DO_SCREEN_FADE_OUT));
        Assert.AreEqual(stableFadeInCount, CountNative(Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual(scoreBefore, state.ActiveScore);
        Assert.AreEqual(fineBefore, state.FineDue);
        Assert.AreEqual(6543, Game.Player.Money);
        Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles")[0].PendingDeathCapture);
    }

    [TestMethod]
    public void PoliceCapture_DoubleRotationThenCapturesExactlyOnce()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            ConfigureHoldingStreamingReady();
            Ped player = Game.Player.Character;
            player.Handle = 1204;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 2;
            Game.Player.Money = 9876;

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, player, 180);
                JusticeCaseState state = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                string wantedEpisode = state.WantedEpisodeId;
                int scoreBefore = state.ActiveScore;
                long fineBefore = state.FineDue;

                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    player));
                Assert.IsTrue(
                    CountNative(Hash.DO_SCREEN_FADE_OUT) >= 1,
                    "Le masque doit être armé par l'acceptation du front, pendant le ped mort.");

                JusticeWriteAheadLog wal = GetField<JusticeWriteAheadLog>(
                    script,
                    "_justiceWriteAheadLog");
                JusticeWalRecord open = wal.GetOpenTransactions().Single(record =>
                    string.Equals(record.OperationKind, "DeathFront", StringComparison.Ordinal));
                string transactionId = open.TransactionId;
                AssertWalStates(
                    directory,
                    transactionId,
                    JusticeWalState.Prepared,
                    JusticeWalState.Attempted);

                Game.GameTime = 100;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                player.IsDead = false;
                Game.Player.IsDead = false;
                player.IsInvincible = true;
                player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
                Game.GameTime = 1000;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");

                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    player.Position));
                Assert.IsFalse(player.IsInvincible);
                Assert.AreEqual(JusticePhase.Wanted, state.Phase);
                Assert.AreEqual(180, state.SentenceSeconds);
                Assert.AreEqual(scoreBefore, state.ActiveScore);
                Assert.AreEqual(fineBefore, state.FineDue);
                Assert.AreEqual(wantedEpisode, state.WantedEpisodeId);
                Assert.AreEqual(string.Empty, state.CustodyEpisodeId);
                Assert.AreEqual(9876, Game.Player.Money);
                Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));

                // Je matérialise le primaire résultat, puis une seconde révision
                // qui pousse ce primaire dans le backup avant Confirmed.
                AwaitQueuedPersistence(script);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(transactionId).State);
                AssertWalStates(
                    directory,
                    transactionId,
                    JusticeWalState.Prepared,
                    JusticeWalState.Attempted,
                    JusticeWalState.Ambiguous);
                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(transactionId).State);
                AssertWalStates(
                    directory,
                    transactionId,
                    JusticeWalState.Prepared,
                    JusticeWalState.Attempted,
                    JusticeWalState.Ambiguous,
                    JusticeWalState.Confirmed);

                CompleteCaptureThroughRuntimeTicks(script, state);
                Assert.IsFalse(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
                string custodyEpisode = state.CustodyEpisodeId;
                Assert.IsFalse(string.IsNullOrWhiteSpace(custodyEpisode));

                // Je repasse volontairement plusieurs fois sur Early/Late : le
                // front Confirmed ne doit ni rejuger ni créer un deuxième épisode.
                for (int index = 0; index < 3; index++)
                {
                    Game.GameTime += 500;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                }
                Assert.AreEqual(custodyEpisode, state.CustodyEpisodeId);
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
            }
            finally
            {
                if (script != null)
                {
                    Invoke(script, "ShutdownJusticeSystem");
                }
            }
        });
    }

    private static object CreatePendingProfileScenario(
        Ped player,
        int sentenceSeconds,
        JusticeWalRecord pendingWal)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticePlayerProfileState[] profiles = CreateProfiles();
        JusticeCaseState state = profiles[0].CaseState;
        ConfigureConsistentActiveCase(state, "hospital", 28, 750L, sentenceSeconds);
        state.Phase = JusticePhase.Wanted;
        profiles[0].LastCanonicalPlayerModel = player.Model.Hash;
        profiles[0].PendingDeathCapture = true;
        profiles[0].PendingDeathCapturePlayerSlot = 0;
        profiles[0].PendingDeathCapturePlayerModel = player.Model.Hash;

        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profiles[0].RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            player.Model.Hash);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetField(script, "_justicePoliceDeathRespawnMaskIntentPending", true);
        SetField(script, "_justicePendingDeathFrontWalRecord", pendingWal);
        SetField(script, "_justiceProfilePersistenceGenerations", new long[3]);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));

        // Je garde ici le WAL volontairement non confirmé et sans service : le
        // test vérifie uniquement le confinement physique fail-closed.
        SetField(script, "_justicePersistenceServicesUnavailable", true);
        SetField(script, "_justicePersistenceInitializationFailurePermanent", true);
        return script;
    }

    private static JusticePlayerProfileState[] CreateProfiles()
    {
        return new[]
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
    }

    private static void ConfigureLivePoliceDeathCase(
        object script,
        Ped player,
        int sentenceSeconds)
    {
        JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles");
        Assert.IsNotNull(profiles);
        JusticePlayerProfileState profile = profiles[0];
        ConfigureConsistentActiveCase(
            profile.CaseState,
            "live-hospital",
            31,
            900L,
            sentenceSeconds);
        profile.CaseState.Phase = JusticePhase.Wanted;
        profile.LastCanonicalPlayerModel = player.Model.Hash;

        SetField(script, "_justiceCaseState", profile.CaseState);
        SetField(script, "_justiceRecordState", profile.RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justicePursuitActive", true);
        SetField(script, "_justiceLastWantedLevel", 2);
        SetField(script, "_justiceWasDead", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
    }

    private static void ConfigureConsistentActiveCase(
        JusticeCaseState state,
        string suffix,
        int points,
        long fine,
        int sentenceSeconds)
    {
        string incident = "incident:" + suffix;
        string episode = "episode:" + suffix;
        state.ClearActiveCase(false);
        state.Enabled = true;
        state.ActiveScore = points;
        state.FineDue = fine;
        state.SentenceSeconds = sentenceSeconds;
        state.WantedEpisodeId = episode;
        state.LastCrimeKind = JusticeCrimeKind.SimpleAssault;
        state.LastCrimeLabel = "Agression test";
        state.ProcessedIncidentIds.Add(incident);
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + suffix,
            IncidentId = incident,
            EpisodeId = episode,
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression test",
            Points = points,
            Fine = fine,
            SentenceSeconds = sentenceSeconds
        });
    }

    private static JusticeWalRecord CreatePoliceDeathRecord(
        int playerModel,
        JusticeWalState state)
    {
        string identityKey = "slot:0:model:" +
            playerModel.ToString(CultureInfo.InvariantCulture);
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokeStatic(
                "CreateJusticeDeathFrontWalFields",
                "PoliceCapture",
                0L,
                0L,
                identityKey,
                "episode:hospital",
                0,
                0,
                playerModel,
                0,
                playerModel);
        return new JusticeWalRecord(
            "death-front:hospital:" + state,
            "DeathFront",
            0,
            state,
            0L,
            DateTime.UtcNow.Ticks,
            fields);
    }

    private static void CompleteCaptureThroughRuntimeTicks(
        object script,
        JusticeCaseState state)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Game.GameTime += 500;
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
            AwaitQueuedPersistence(script);

            if (!GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture &&
                state.CompletedOperationIds.Count(operation =>
                    operation.StartsWith("Capture:", StringComparison.Ordinal)) == 1)
            {
                return;
            }
        }

        Assert.Fail(
            "Le front Confirmed n'a pas produit une capture unique dans le budget de retries. " +
            "phase=" + state.Phase +
            ", pending=" + GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles")[0].PendingDeathCapture +
            ", runtimePending=" + GetField<bool>(
                script,
                "_justicePursuitDeathObservedDuringSuspension") +
            ", maskIntent=" + GetField<bool>(
                script,
                "_justicePoliceDeathRespawnMaskIntentPending") +
            ", captureRetry=" + GetField<bool>(script, "_justiceCaptureRetryPending") +
            ", precommit=" + GetField<bool>(script, "_justiceCapturePrecommitConfirmed") +
            ", operations=" + string.Join(
                ",",
                state.CompletedOperationIds.ToArray()) +
            ", criticalRevision=" + GetField<long>(script, "_justiceCriticalBarrierRevision") +
            ", dirty=" + GetField<bool>(script, "_justiceStateDirty") +
            ", erreur=" + GetField<string>(script, "_justicePersistenceLastError") + ".");
    }

    private static void AssertWalStates(
        string directory,
        string transactionId,
        params JusticeWalState[] expectedStates)
    {
        JusticeWalRecoveryResult recovered = JusticeWriteAheadLog.Recover(
            Path.Combine(directory, "_justice_state.wal"));
        JusticeWalState[] actual = recovered.Records
            .Where(record => string.Equals(
                record.TransactionId,
                transactionId,
                StringComparison.Ordinal))
            .Select(record => record.State)
            .ToArray();
        CollectionAssert.AreEqual(expectedStates, actual);
    }

    private static void ConfigureHoldingStreamingReady()
    {
        StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == GroundReadyNative || hash == CollisionReadyNative
                ? (object)true
                : null;
    }

    private static int CountNative(Hash hash)
    {
        return StubRuntime.NativeCalls.Count(call => call.Hash == (ulong)hash);
    }

    private static void WithTemporaryJusticeDirectory(Action<string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJPoliceDeathHospital-" + Guid.NewGuid().ToString("N"));
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
            StubRuntime.Reset();
        }
    }

    private static void FlushAndAwait(object script)
    {
        Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
        AwaitQueuedPersistence(script);
    }

    private static void AwaitQueuedPersistence(object script)
    {
        Assert.IsTrue(
            (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"),
            "Le repository doit confirmer la révision avant de poursuivre le scénario.");
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
#endif

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
        Assert.AreEqual(1, methods.Length, "Méthode statique ambiguë : " + methodName);
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

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }
}
