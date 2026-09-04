using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml.Linq;
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
    private const ulong ScreenFadedInNative = 0x5A859503B0C08678UL;
    private const ulong ScreenFadingInNative = 0x5C544BC6C57AC575UL;
    private const ulong MissionFlagNative = 0xA33CDCCDA663159EUL;
    private const ulong LoadingScreenNative = 0x10D0A8F259E93EC9UL;
    private const ulong CutsceneNative = 0x991251AFC3981F84UL;
    private const ulong PlayerSwitchNative = 0xD9D2CFFF49FAB35FUL;
    private const ulong PlayerBeingArrestedNative = 0x388A47C51ABDAC8EUL;

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
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Assert.IsFalse(player.CanRagdoll);
        Assert.AreEqual(
            0,
            CountNative(Hash.DO_SCREEN_FADE_IN),
            "Le holding ne doit rendre l'image qu'après l'admission finale.");
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
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Assert.IsFalse(player.CanRagdoll);
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
    public void PoliceCapture_VerifiedHoldingReplacesPlayerWithoutRestoringScreen()
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
            fadeOutBeforeEscape,
            CountNative(Hash.DO_SCREEN_FADE_OUT),
            "Le masque déjà actif doit couvrir le replacement sans nouveau flash.");
        Assert.AreEqual(
            fadeInBeforeEscape,
            CountNative(Hash.DO_SCREEN_FADE_IN),
            "Le replacement ne doit jamais rendre l'image avant l'admission finale.");
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Assert.IsFalse(player.CanRagdoll);

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
                // Je fournis au stub la lecture et l'écriture de cash attendues :
                // cette régression cible la rotation du DeathFront, pas un timeout financier.
                int cash = Game.Player.Money;
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        return true;
                    }));
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
                Assert.IsTrue(player.IsInvincible);
                Assert.IsTrue(player.FreezePosition);
                Assert.IsFalse(player.CanRagdoll);
                Assert.AreEqual(0, CountNative(Hash.DO_SCREEN_FADE_IN));
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

    [TestMethod]
    public void PoliceCapture_ResidualMissionFlagAdmitsSameHeroBeforeFadeAndStartsSentenceClock()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1210;
            deadPlayer.Model = new Model("player_zero");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            bool screenFadedIn = false;
            bool screenFadingIn = false;
            int fadeInRequestCount = 0;
            List<int> wantedObservedAtFadeIn = new List<int>();
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                wantedObservedAtFadeIn,
                () =>
                {
                    fadeInRequestCount++;
                    screenFadedIn = false;
                    screenFadingIn = true;
                    return false;
                },
                null,
                null,
                null,
                () => screenFadedIn,
                () => screenFadingIn);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, deadPlayer, 180);
                JusticeCaseState state = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                // Je neutralise ici la transaction financière, déjà couverte par
                // sa propre suite, pour isoler strictement l'admission post-mortem.
                state.FineDue = 0L;
                state.Charges[0].Fine = 0L;
                state.HasWarrant = true;

                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    deadPlayer));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                // Je reproduis le nouveau handle vivant rendu par GTA pour le
                // même protagoniste, sans aucune permutation de personnage.
                Ped respawnedPlayer = new Ped
                {
                    Handle = 1214,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    IsInvincible = true,
                    FreezePosition = true,
                    CanRagdoll = true
                };
                Game.Player.Character = respawnedPlayer;
                Game.Player.IsDead = false;
                residualMissionFlag = true;

                // Je laisse d'abord UpdateJusticeSystem établir le holding : le
                // latch résiduel ne peut jamais juger le joueur depuis l'hôpital.
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Assert.AreEqual(JusticePhase.Wanted, state.Phase);
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Invoke(script, "UpdateJusticeSystem");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"),
                    "Le holding initial doit être établi avant de préparer l'amende.");
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    respawnedPlayer.Position));
                Assert.AreEqual(
                    new Model("player_zero").Hash,
                    (int)InvokeStatic(
                        "GetJusticePedModelHashSafe",
                        respawnedPlayer));
                wantedObservedAtFadeIn.Clear();

                AdvanceUntilCustodySnapshotStored(script, state, 32, 250);
                Assert.AreEqual(
                    "JusticePreJudgmentHolding",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyPlayerStateStored"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyStoredInvincible"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyStoredFrozen"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyStoredCanRagdoll"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue(StubRuntime.ScreenFadedOut);
                Assert.AreEqual(
                    0,
                    wantedObservedAtFadeIn.Count,
                    "Le snapshot ne doit jamais libérer le holding avant l'admission finale.");

                for (int tick = 0;
                     tick < 16 && GetField<int>(
                         script,
                         "_justiceRecognitionCaptureResetConfirmedProfileSlot") != 0;
                     tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }
                Assert.AreEqual(0, GetField<int>(
                    script,
                    "_justiceRecognitionCaptureResetConfirmedProfileSlot"));
                Assert.AreEqual(
                    state.CustodyEpisodeId,
                    GetField<string>(
                        script,
                        "_justiceRecognitionCaptureResetConfirmedEpisodeId"));
                FieldInfo nextRecognitionCommandIdField = typeof(
                    DonJ.JusticeRecognition.JusticeRecognitionBridge).GetField(
                        "_nextCriticalCommandId",
                        PrivateStatic);
                Assert.IsNotNull(nextRecognitionCommandIdField);
                long recognitionCommandIdAfterConfirmation = (long)
                    nextRecognitionCommandIdField.GetValue(null);

                int sentenceBeforeFadeIn = state.SentenceSeconds;
                for (int tick = 0; tick < 32 && fadeInRequestCount == 0; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }
                Assert.AreEqual(1, fadeInRequestCount);
                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyRespawnTransferPending"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.IsTrue(screenFadingIn);
                Assert.IsFalse(screenFadedIn);
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
                Assert.AreEqual(sentenceBeforeFadeIn, state.SentenceSeconds);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);
                Assert.AreEqual(
                    "JusticePreJudgmentHolding",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());
                Assert.AreEqual(
                    recognitionCommandIdAfterConfirmation,
                    (long)nextRecognitionCommandIdField.GetValue(null),
                    "Les retries avant FadeIn ne doivent pas recréer la commande Recognition.");

                // Je maintiens uniquement FADING_IN : cet etat transitoire ne
                // doit ni liberer le joueur ni amorcer le temps de peine.
                for (int tick = 0; tick < 2; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }
                Assert.AreEqual(1, fadeInRequestCount);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
                Assert.AreEqual(sentenceBeforeFadeIn, state.SentenceSeconds);
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);

                int fadeOutCountBeforeWantedRebound =
                    CountNative(Hash.DO_SCREEN_FADE_OUT);
                screenFadingIn = false;
                Game.Player.WantedLevel = 3;
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                AwaitQueuedPersistence(script);

                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    CountNative(Hash.DO_SCREEN_FADE_OUT) >
                    fadeOutCountBeforeWantedRebound,
                    "Le rebond wanted doit refermer l'ecran dans le meme retry.");
                Assert.IsTrue(StubRuntime.ScreenFadedOut);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyAdmissionWantedStabilityStarted"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
                Assert.AreEqual(sentenceBeforeFadeIn, state.SentenceSeconds);
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);

                // Je tolere les retries durables intermediaires, mais chaque
                // reset doit produire son propre timestamp stable d'au moins 1 s.
                int wantedReboundHandledAt = Game.GameTime;
                int wantedRestartedAt = AdvanceUntilAdmissionFadeInRequested(
                    script,
                    () => fadeInRequestCount,
                    2,
                    wantedReboundHandledAt,
                    96,
                    250);
                Assert.IsTrue(
                    unchecked((uint)(Game.GameTime - wantedRestartedAt)) >= 1000U);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.IsTrue(screenFadingIn);

                int fadeOutCountBeforeInterruptedFade =
                    CountNative(Hash.DO_SCREEN_FADE_OUT);
                screenFadingIn = false;
                screenFadedIn = false;
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                AwaitQueuedPersistence(script);

                Assert.AreEqual(2, fadeInRequestCount);
                Assert.IsTrue(
                    CountNative(Hash.DO_SCREEN_FADE_OUT) >
                    fadeOutCountBeforeInterruptedFade,
                    "Un FadeIn interrompu doit rearmer le masque avant tout nouveau retry.");
                Assert.IsTrue(StubRuntime.ScreenFadedOut);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyAdmissionWantedStabilityStarted"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
                Assert.AreEqual(sentenceBeforeFadeIn, state.SentenceSeconds);
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);

                int interruptedFadeHandledAt = Game.GameTime;
                int interruptedFadeRestartedAt =
                    AdvanceUntilAdmissionFadeInRequested(
                        script,
                        () => fadeInRequestCount,
                        3,
                        interruptedFadeHandledAt,
                        96,
                        250);
                Assert.IsTrue(
                    unchecked((uint)(Game.GameTime - interruptedFadeRestartedAt)) >= 1000U);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyLastTickAt"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.AreEqual(
                    recognitionCommandIdAfterConfirmation,
                    (long)nextRecognitionCommandIdField.GetValue(null),
                    "Les retries avant FadeIn ne doivent pas recreer la commande Recognition.");

                // Je fournis enfin la seule preuve autorisee. La consommation du
                // DeathFront et l'horloge doivent commencer a ce tick exact.
                screenFadingIn = false;
                screenFadedIn = true;
                int admissionCompletedAt = 0;
                for (int tick = 0;
                     tick < 8 && GetField<bool>(
                         script,
                         "_justiceCustodyTransferPending");
                     tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                    if (!GetField<bool>(script, "_justiceCustodyTransferPending"))
                    {
                        admissionCompletedAt = Game.GameTime;
                    }
                }
                Assert.AreNotEqual(0, admissionCompletedAt);
                Assert.AreEqual(
                    admissionCompletedAt,
                    GetField<int>(script, "_justiceCustodyLastTickAt"));

                Assert.AreSame(
                    respawnedPlayer,
                    Game.Player.Character,
                    "La capture ne doit jamais exiger ni provoquer un changement de personnage.");
                Assert.AreEqual(1214, respawnedPlayer.Handle);
                Assert.AreEqual(
                    new Model("player_zero").Hash,
                    respawnedPlayer.Model.Hash);
                Assert.AreEqual(0, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyPlayerSlot"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceRuntimeSuspendedByMissionFlagOnlyCached"));
                Assert.IsFalse(respawnedPlayer.IsInvincible);
                Assert.IsFalse(respawnedPlayer.FreezePosition);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticeCustody",
                    respawnedPlayer.Position));
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsFalse(state.HasWarrant);
                Assert.IsFalse(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("EnterCustody:", StringComparison.Ordinal)));
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0,
                    "L'admission terminée doit rendre l'écran au joueur.");
                Assert.IsTrue(
                    wantedObservedAtFadeIn.All(wanted => wanted == 0),
                    "Aucun fondu entrant ne doit exposer le détenu avec des étoiles actives.");
                Assert.IsFalse(
                    StubRuntime.ScreenFadedOut,
                    "Le masque ne doit être rendu qu'après l'admission sûre.");
                Assert.AreEqual(
                    recognitionCommandIdAfterConfirmation,
                    (long)nextRecognitionCommandIdField.GetValue(null),
                    "La fin d'admission doit conserver l'idempotence Recognition de l'épisode.");

                int sentenceAtAdmission = state.SentenceSeconds;
                Assert.IsTrue(sentenceAtAdmission > 1);
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }

                Assert.AreEqual(
                    sentenceAtAdmission - 1,
                    state.SentenceSeconds,
                    "La peine doit avancer sur le même héros dès la première seconde jouable.");
                Assert.AreEqual(
                    0,
                    Game.Player.WantedLevel,
                    "Le wanted ne doit pas réapparaître pendant la détention normale.");
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

    [TestMethod]
    public void CustodyAdmission_FinalizesDeathFrontAndHoldingOnlyAfterExactFadeInProof()
    {
        string custodySource = ReadJusticeSource(
            "DonJEnemySpawner.Justice.Custody.cs");
        string justiceSource = ReadJusticeSource(
            "DonJEnemySpawner.Justice.cs");
        string transfer = ExtractPrivateMethodSource(
            custodySource,
            "CompleteJusticeCustodyTransfer");
        string finishFadeIn = ExtractPrivateMethodSource(
            custodySource,
            "TryFinishJusticeCustodyAdmissionFadeIn");
        string finalize = ExtractPrivateMethodSource(
            custodySource,
            "FinalizeJusticeCustodyAdmissionAfterFadeIn");
        string armDeathFront = ExtractPrivateMethodSource(
            justiceSource,
            "TryArmPendingJusticeDeathCaptureForTransfer");

        StringAssert.Contains(
            transfer,
            "TryArmPendingJusticeDeathCaptureForTransfer()");
        Assert.IsFalse(
            transfer.Contains("ClearPendingJusticeDeathCapture()"),
            "Le transfert ne doit plus acquitter le DeathFront avant la preuve visuelle.");
        AssertSourceOrder(
            transfer,
            "TryFinishJusticeCustodyAdmissionFadeIn(",
            "FinalizeJusticeCustodyAdmissionAfterFadeIn(");
        AssertSourceOrder(
            finishFadeIn,
            "IsJusticeCustodyRespawnTransferMaskFullyRestored()",
            "CompleteJusticePreJudgmentHoldingStreamingProtection(player)",
            "return true;");
        Assert.IsFalse(
            armDeathFront.Contains("ClearPendingJusticeDeathCapture()"),
            "L'armement physique conserve le front durable pendant tout le FadeIn.");
        AssertSourceOrder(
            finalize,
            "ClearPendingJusticeDeathCapture()",
            "ResetJusticePoliceDeathPreJudgmentHoldingState()",
            "_justiceCustodyLastTickAt = now;");
    }

    [TestMethod]
    public void PoliceCapture_PreparedFineBeforeResidualMissionFlagResumesAdmissionAndSentenceClock()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1224;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = false;
            player.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            int cash = 900;
            int cashWriteCount = 0;
            List<int> wantedObservedAtFadeIn = new List<int>();
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                wantedObservedAtFadeIn);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureCapturedPoliceDeathFineResumeState(
                    script,
                    player,
                    false);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                // Je laisse le contrôleur physique établir son enceinte avant de
                // préparer l'amende, comme dans le Late précédant le flag BUSTED.
                Game.GameTime += 250;
                Invoke(
                    script,
                    "UpdateJusticePoliceDeathPreJudgmentHolding",
                    player,
                    Game.GameTime);
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    player.Position));

                object preparedIntent = PrepareJusticeFineDebitIntent(script);
                Assert.AreEqual("Prepared", GetFieldObject(preparedIntent, "Resolution").ToString());
                Assert.IsTrue(GetField<bool>(preparedIntent, "CashPlanPrepared"));
                Assert.IsFalse(GetField<bool>(preparedIntent, "DebitAttempted"));
                Assert.AreEqual(state.CustodyEpisodeId, GetField<string>(preparedIntent, "EpisodeId"));
                Assert.AreEqual(900, cash);
                Assert.AreEqual(0, cashWriteCount);
                Assert.IsFalse(residualMissionFlag);

                // Je vérifie qu'une suspension forte ne peut réutiliser le relais
                // mission-only ni provoquer le moindre effet financier.
                residualMissionFlag = true;
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    "Loading",
                    wantedObservedAtFadeIn);
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                }
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceRuntimeSuspendedByMissionFlagOnlyCached"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.AreSame(preparedIntent, GetFieldObject(script, "_justiceFineDebitIntent"));
                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.AreEqual(900L, state.FineDue);
                Assert.AreEqual(900, cash);
                Assert.AreEqual(0, cashWriteCount);

                // Le seul flag mission résiduel peut désormais rendre la main au
                // contrôleur Late, sans appliquer lui-même le débit.
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    wantedObservedAtFadeIn);
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceRuntimeSuspendedByMissionFlagOnlyCached"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.AreEqual(900, cash);
                Assert.AreEqual(0, cashWriteCount);

                AdvanceUntilIncarcerated(script, state, 80, 250);

                Assert.AreSame(player, Game.Player.Character);
                Assert.AreEqual(0, cash);
                Assert.AreEqual(
                    1,
                    cashWriteCount,
                    "La reprise ne doit jamais réémettre le débit préparé.");
                Assert.AreEqual(0L, state.FineDue);
                Assert.IsNull(GetFieldObject(script, "_justiceFineDebitIntent"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));

                int sentenceAtAdmission = state.SentenceSeconds;
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }
                Assert.AreEqual(
                    sentenceAtAdmission - 1,
                    state.SentenceSeconds,
                    "Le compteur doit partir dès la première seconde après l'admission.");
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

    [TestMethod]
    public void PoliceCapture_ReloadedPreparedFineUnderResidualMissionFlagRebuildsProofAndCompletes()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped writerPlayer = Game.Player.Character;
            writerPlayer.Handle = 1225;
            writerPlayer.Model = new Model("player_zero");
            writerPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            writerPlayer.IsDead = false;
            writerPlayer.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            int cash = 900;
            int cashWriteCount = 0;
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                null);

            DonJEnemySpawner writer = null;
            DonJEnemySpawner reader = null;
            bool writerPersistenceStopped = false;
            try
            {
                writer = new DonJEnemySpawner();
                JusticeCaseState writerState = ConfigureCapturedPoliceDeathFineResumeState(
                    writer,
                    writerPlayer,
                    true);
                SetField(
                    writer,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    writer,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        cashWriteCount++;
                        return true;
                    }));
                object writerIntent = PrepareJusticeFineDebitIntent(writer);
                Assert.AreEqual("Prepared", GetFieldObject(writerIntent, "Resolution").ToString());
                Assert.AreEqual(JusticePhase.Captured, writerState.Phase);
                Assert.IsTrue(GetField<bool>(writer, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<bool>(writer, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(0, cashWriteCount);

                string statePath = Path.Combine(directory, "_justice_state.xml");
                Assert.IsTrue(File.Exists(statePath));
                StringAssert.Contains(File.ReadAllText(statePath), "<FineDebitIntent");
                Invoke(writer, "ShutdownJusticePersistenceServices");
                writerPersistenceStopped = true;

                // Je simule un vrai reload : le handle, le holding et la preuve
                // volatile du précommit ne survivent pas, contrairement au DTO.
                Ped reloadedPlayer = new Ped
                {
                    Handle = 1226,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    CanRagdoll = true
                };
                Game.Player.Character = reloadedPlayer;
                Game.Player.IsDead = false;
                residualMissionFlag = true;
                List<int> wantedObservedAtFadeIn = new List<int>();
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    wantedObservedAtFadeIn);

                reader = new DonJEnemySpawner();
                SetField(
                    reader,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));
                SetField(
                    reader,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    reader,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                JusticeCaseState state = GetField<JusticeCaseState>(
                    reader,
                    "_justiceCaseState");
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    reader,
                    "_justicePlayerProfiles")[0];
                object reloadedIntent = GetFieldObject(reader, "_justiceFineDebitIntent");
                Assert.IsNotNull(reloadedIntent);
                Assert.AreEqual("Prepared", GetFieldObject(reloadedIntent, "Resolution").ToString());
                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(0, GetField<int>(reader, "_justiceCustodyPlayerHandle"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsNull(GetField<JusticeWalRecord>(
                    reader,
                    "_justicePendingDeathFrontWalRecord"));
                Assert.IsFalse((bool)Invoke(
                    reader,
                    "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));
                Assert.AreEqual(
                    "None",
                    GetFieldObject(reader, "_justicePreJudgmentHoldingSource").ToString());

                // Le premier Early ne peut rien reprendre avant la preuve
                // physique; le Late suivant établit seulement le holding.
                Game.GameTime += 250;
                Invoke(reader, "UpdateJusticeEarly");
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Invoke(reader, "UpdateJusticeSystem");
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(reader, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue(GetField<bool>(
                    reader,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue((bool)Invoke(
                    reader,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    reloadedPlayer.Position));
                Assert.AreEqual(900, cash);
                Assert.AreEqual(0, cashWriteCount);

                // Je casse temporairement le propriétaire persistant : ni la
                // preuve volatile ni le débit ne doivent franchir cette garde.
                int exactOwnerModel = profile.PendingDeathCapturePlayerModel;
                profile.PendingDeathCapturePlayerModel = new Model("player_one").Hash;
                Game.GameTime += 250;
                Invoke(reader, "UpdateJusticeEarly");
                Invoke(reader, "UpdateJusticeSystem");
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.IsFalse((bool)Invoke(
                    reader,
                    "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));
                Assert.AreSame(reloadedIntent, GetFieldObject(reader, "_justiceFineDebitIntent"));
                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.AreEqual(900, cash);
                Assert.AreEqual(0, cashWriteCount);
                profile.PendingDeathCapturePlayerModel = exactOwnerModel;

                bool rebuiltPrecommit = false;
                bool observedBypass = false;
                for (int tick = 0; tick < 96; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(reader, "UpdateJusticeEarly");
                    rebuiltPrecommit |= (bool)Invoke(
                        reader,
                        "HasJusticeCapturePrecommitConfirmationForCurrentEpisode");
                    observedBypass |= GetField<bool>(
                        reader,
                        "_justiceCustodyResidualMissionFlagBypassArmed");
                    Invoke(reader, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(reader);
                    if (state.Phase == JusticePhase.Incarcerated &&
                        !GetField<bool>(reader, "_justiceCustodyTransferPending") &&
                        GetField<bool>(reader, "_justiceCustodyContainmentEstablished"))
                    {
                        break;
                    }
                }

                Assert.IsTrue(
                    rebuiltPrecommit,
                    "Le reload doit reconstruire le précommit exact avant le débit.");
                Assert.IsTrue(
                    observedBypass,
                    "Le holding exact doit ouvrir le relais du seul flag mission résiduel.");
                Assert.AreSame(reloadedPlayer, Game.Player.Character);
                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyTransferPending"));
                Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyContainmentEstablished"));
                Assert.IsNull(GetFieldObject(reader, "_justiceFineDebitIntent"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));

                int sentenceAtAdmission = state.SentenceSeconds;
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(reader, "UpdateJusticeEarly");
                    Invoke(reader, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(reader);
                }
                Assert.AreEqual(sentenceAtAdmission - 1, state.SentenceSeconds);
            }
            finally
            {
                if (reader != null)
                {
                    Invoke(reader, "ShutdownJusticeSystem");
                }
                if (writer != null && !writerPersistenceStopped)
                {
                    Invoke(writer, "ShutdownJusticePersistenceServices");
                }
            }
        });
    }

    [TestMethod]
    public void PoliceCapture_FullyPaidFineKeepsMissionBypassUntilLegalReleaseAcknowledgement()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1227;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = false;
            player.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            int cash = 900;
            int cashWriteCount = 0;
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureCapturedPoliceDeathFineResumeState(
                    script,
                    player,
                    false,
                    0);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Game.GameTime += 250;
                Invoke(
                    script,
                    "UpdateJusticePoliceDeathPreJudgmentHolding",
                    player,
                    Game.GameTime);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                object preparedIntent = PrepareJusticeFineDebitIntent(script);
                Assert.AreEqual("Prepared", GetFieldObject(preparedIntent, "Resolution").ToString());

                bool releaseBarrierObserved = false;
                int forcedReleaseBarrierFailures = 0;
                int forcedAcknowledgementFailures = 0;
                SetField(
                    script,
                    "_justiceStateFlushFailureOverride",
                    new Func<int, bool>(attempt =>
                    {
                        bool releasePending = GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending");
                        if (releasePending)
                        {
                            releaseBarrierObserved = true;
                            if (forcedReleaseBarrierFailures < 2)
                            {
                                forcedReleaseBarrierFailures++;
                                return true;
                            }
                            return false;
                        }

                        // Je force aussi l'ACK final, reconnaissable au latch mis
                        // provisoirement à false par son commit at-most-once.
                        if (releaseBarrierObserved &&
                            forcedAcknowledgementFailures < 2)
                        {
                            forcedAcknowledgementFailures++;
                            return true;
                        }
                        return false;
                    }));

                residualMissionFlag = true;
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    null);
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));

                bool sawReleaseAfterCustodyReset = false;
                bool custodyWasArmed = false;
                int pendingReleaseTicks = 0;
                for (int tick = 0; tick < 120; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    custodyWasArmed |=
                        GetField<bool>(script, "_justiceCustodyRuntimeActive") ||
                        GetField<bool>(script, "_justiceCustodyTransferPending") ||
                        GetField<bool>(script, "_justiceCustodyResumePending");

                    if (GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending"))
                    {
                        pendingReleaseTicks++;
                        if (state.Phase == JusticePhase.AtLarge)
                        {
                            sawReleaseAfterCustodyReset = true;
                            Assert.AreEqual(
                                "None",
                                GetFieldObject(
                                    script,
                                    "_justiceLegalReleaseFinalizationSite").ToString());
                            Assert.IsTrue(GetField<bool>(
                                script,
                                "_justiceCustodyResidualMissionFlagBypassArmed"));
                            Assert.IsFalse(GetField<bool>(
                                script,
                                "_justiceCustodyRuntimeActive"));
                            Assert.AreEqual(-1, GetField<int>(
                                script,
                                "_justiceCustodyPlayerSlot"));
                        }
                    }

                    AwaitQueuedPersistence(script);
                    if (sawReleaseAfterCustodyReset &&
                        !GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending"))
                    {
                        break;
                    }
                }

                SetField(script, "_justiceStateFlushFailureOverride", null);
                Assert.IsTrue(sawReleaseAfterCustodyReset);
                Assert.IsTrue(
                    pendingReleaseTicks >= 4,
                    "La barrière et son ACK forcés doivent maintenir la reprise sur plusieurs ticks.");
                Assert.AreEqual(2, forcedReleaseBarrierFailures);
                Assert.AreEqual(
                    2,
                    forcedAcknowledgementFailures,
                    "L'ACK final doit rester reprenable apr\u00e8s deux refus durables.");
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.AreEqual(0L, GetField<long>(
                    script,
                    "_justiceCustodyResidualMissionFlagObservationDeadlineMs"));
                Assert.AreSame(player, Game.Player.Character);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsFalse(custodyWasArmed);
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsNull(GetFieldObject(script, "_justiceFineDebitIntent"));
                Assert.AreEqual(0L, state.FineDue);
                Assert.AreEqual(0, state.SentenceSeconds);
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(0, Game.Player.WantedLevel);

                // Un tick supplémentaire ne doit ni rejuger ni redébiter après ACK.
                int captureCount = state.CompletedOperationIds.Count(operation =>
                    operation.StartsWith("Capture:", StringComparison.Ordinal));
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(
                    captureCount,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
            }
            finally
            {
                if (script != null)
                {
                    SetField(script, "_justiceStateFlushFailureOverride", null);
                    Invoke(script, "ShutdownJusticeSystem");
                }
            }
        });
    }

    [TestMethod]
    public void PoliceCapture_ReloadedNoCellReleaseRebuildsMissionBypassAndAcknowledgesDeathFront()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped writerPlayer = Game.Player.Character;
            writerPlayer.Handle = 1230;
            writerPlayer.Model = new Model("player_zero");
            writerPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            writerPlayer.IsDead = false;
            writerPlayer.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            int cash = 900;
            int cashWriteCount = 0;
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                null);

            DonJEnemySpawner writer = null;
            DonJEnemySpawner reader = null;
            bool writerPersistenceStopped = false;
            try
            {
                writer = new DonJEnemySpawner();
                JusticeCaseState writerState =
                    ConfigureCapturedPoliceDeathFineResumeState(
                        writer,
                        writerPlayer,
                        false,
                        0);
                JusticePlayerProfileState writerProfile =
                    GetField<JusticePlayerProfileState[]>(
                        writer,
                        "_justicePlayerProfiles")[0];
                int convictionCount = writerProfile.RecordState.Convictions.Count;
                SetField(
                    writer,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    writer,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Game.GameTime += 250;
                Invoke(
                    writer,
                    "UpdateJusticePoliceDeathPreJudgmentHolding",
                    writerPlayer,
                    Game.GameTime);
                Assert.IsTrue(GetField<bool>(
                    writer,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                PrepareJusticeFineDebitIntent(writer);

                int forcedPrepareFailures = 0;
                SetField(
                    writer,
                    "_justiceStateFlushFailureOverride",
                    new Func<int, bool>(attempt =>
                    {
                        if (GetField<bool>(
                                writer,
                                "_justiceLegalReleaseFinalizationPending"))
                        {
                            forcedPrepareFailures++;
                            return true;
                        }
                        return false;
                    }));

                residualMissionFlag = true;
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    null);
                bool durableHandoffReady = false;
                for (int tick = 0; tick < 96; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(writer, "UpdateJusticeEarly");
                    Invoke(writer, "UpdateJusticeSystem");
                    durableHandoffReady =
                        writerState.Phase == JusticePhase.AtLarge &&
                        GetField<bool>(
                            writer,
                            "_justiceLegalReleaseFinalizationPending") &&
                        writerProfile.PendingDeathCapture;
                    if (durableHandoffReady)
                    {
                        break;
                    }
                    if (GetField<long>(
                            writer,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(writer);
                    }
                }

                Assert.IsTrue(durableHandoffReady);
                Assert.IsTrue(forcedPrepareFailures >= 1);
                Assert.IsTrue(GetField<bool>(
                    writer,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.AreEqual(
                    "None",
                    GetFieldObject(
                        writer,
                        "_justiceLegalReleaseFinalizationSite").ToString());
                Assert.IsTrue((bool)Invoke(
                    writer,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    writerPlayer.Position));
                Assert.IsNull(GetFieldObject(writer, "_justiceFineDebitIntent"));
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);

                // Je publie exactement le couple PendingDeath + LegalRelease avant
                // de simuler le crash; aucun cache volatile ne doit être requis.
                SetField(writer, "_justiceStateFlushFailureOverride", null);
                SetField(writer, "_justiceNextStateFlushAttemptAtMs", 0L);
                FlushAndAwait(writer);
                string statePath = Path.Combine(directory, "_justice_state.xml");
                XElement durableProfile = GetPersistedActiveJusticeProfile(
                    XDocument.Load(statePath));
                Assert.AreEqual(
                    "true",
                    (string)durableProfile.Attribute("pendingDeathCapture"));
                Assert.AreEqual(
                    "true",
                    (string)durableProfile.Attribute(
                        "pendingLegalReleaseFinalization"));
                Assert.AreEqual(
                    "0",
                    (string)durableProfile.Attribute("pendingLegalReleaseSite"));
                GTA.Math.Vector3 persistedHoldingPosition = writerPlayer.Position;
                Invoke(writer, "ShutdownJusticePersistenceServices");
                writerPersistenceStopped = true;

                StubRuntime.Reset();
                Ped reloadedPlayer = Game.Player.Character;
                reloadedPlayer.Handle = 1231;
                reloadedPlayer.Model = new Model("player_zero");
                reloadedPlayer.Position = persistedHoldingPosition;
                reloadedPlayer.IsDead = false;
                reloadedPlayer.CanRagdoll = true;
                Game.Player.IsDead = false;
                Game.Player.WantedLevel = 3;
                residualMissionFlag = true;
                List<int> wantedObservedAtFadeIn = new List<int>();
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    wantedObservedAtFadeIn);

                reader = new DonJEnemySpawner();
                SetField(
                    reader,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));
                SetField(
                    reader,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    reader,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                JusticeCaseState state = GetField<JusticeCaseState>(
                    reader,
                    "_justiceCaseState");
                JusticePlayerProfileState profile =
                    GetField<JusticePlayerProfileState[]>(
                        reader,
                        "_justicePlayerProfiles")[0];
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsTrue(GetField<bool>(
                    reader,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.AreEqual(
                    "None",
                    GetFieldObject(
                        reader,
                        "_justiceLegalReleaseFinalizationSite").ToString());
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsTrue(GetField<bool>(
                    reader,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));

                int forcedAcknowledgementFailures = 0;
                SetField(
                    reader,
                    "_justiceStateFlushFailureOverride",
                    new Func<int, bool>(attempt =>
                    {
                        if (!GetField<bool>(
                                reader,
                                "_justiceLegalReleaseFinalizationPending") &&
                            forcedAcknowledgementFailures < 2)
                        {
                            forcedAcknowledgementFailures++;
                            return true;
                        }
                        return false;
                    }));

                bool rebuiltHolding = false;
                bool rebuiltBypass = false;
                bool sawCoupledPendingState = false;
                for (int tick = 0; tick < 160; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(reader, "UpdateJusticeEarly");
                    rebuiltBypass |= GetField<bool>(
                        reader,
                        "_justiceCustodyResidualMissionFlagBypassArmed");
                    Invoke(reader, "UpdateJusticeSystem");
                    rebuiltHolding |= GetField<bool>(
                        reader,
                        "_justicePoliceDeathPreJudgmentHoldingEstablished");
                    if (GetField<bool>(
                            reader,
                            "_justiceLegalReleaseFinalizationPending") &&
                        profile.PendingDeathCapture)
                    {
                        sawCoupledPendingState = true;
                    }
                    if (GetField<long>(
                            reader,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(reader);
                    }
                    if (!GetField<bool>(
                            reader,
                            "_justiceLegalReleaseFinalizationPending") &&
                        !profile.PendingDeathCapture &&
                        !GetField<bool>(
                            reader,
                            "_justicePoliceDeathNoCellReleaseProtectionRestorePending"))
                    {
                        break;
                    }
                }

                SetField(reader, "_justiceStateFlushFailureOverride", null);
                Assert.IsTrue(rebuiltHolding);
                Assert.IsTrue(rebuiltBypass);
                Assert.IsTrue(sawCoupledPendingState);
                Assert.AreEqual(2, forcedAcknowledgementFailures);
                Assert.AreSame(reloadedPlayer, Game.Player.Character);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsFalse(GetField<bool>(
                    reader,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyTransferPending"));
                Assert.IsFalse((bool)Invoke(
                    reader,
                    "IsInsideJusticeCustody",
                    reloadedPlayer.Position));
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));

                XElement acknowledgedProfile = GetPersistedActiveJusticeProfile(
                    XDocument.Load(statePath));
                Assert.AreEqual(
                    "false",
                    (string)acknowledgedProfile.Attribute("pendingDeathCapture"));
                Assert.AreEqual(
                    "false",
                    (string)acknowledgedProfile.Attribute(
                        "pendingLegalReleaseFinalization"));
            }
            finally
            {
                if (reader != null)
                {
                    SetField(reader, "_justiceStateFlushFailureOverride", null);
                    Invoke(reader, "ShutdownJusticeSystem");
                }
                if (writer != null && !writerPersistenceStopped)
                {
                    SetField(writer, "_justiceStateFlushFailureOverride", null);
                    Invoke(writer, "ShutdownJusticePersistenceServices");
                }
            }
        });
    }

    [TestMethod]
    public void PoliceCapture_NoCellRejectedFadeInRestartsWantedStabilityBeforeAcknowledgement()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1232;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = false;
            player.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            bool rejectFirstFadeIn = true;
            int rejectedFadeInAt = -1;
            int rejectedFadeInCount = 0;
            int cash = 900;
            int cashWriteCount = 0;
            List<int> wantedObservedAtFadeIn = new List<int>();
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                wantedObservedAtFadeIn,
                () =>
                {
                    if (!rejectFirstFadeIn)
                    {
                        return false;
                    }

                    // Je reproduis la race moteur observée : GTA refuse le rendu
                    // puis réarme ses étoiles avant le tick de reprise suivant.
                    rejectFirstFadeIn = false;
                    rejectedFadeInAt = Game.GameTime;
                    rejectedFadeInCount++;
                    Game.Player.WantedLevel = 3;
                    return true;
                });

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state =
                    ConfigureCapturedPoliceDeathFineResumeState(
                        script,
                        player,
                        false,
                        0);
                JusticePlayerProfileState profile =
                    GetField<JusticePlayerProfileState[]>(
                        script,
                        "_justicePlayerProfiles")[0];
                int convictionCount = profile.RecordState.Convictions.Count;
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Game.GameTime += 250;
                Invoke(
                    script,
                    "UpdateJusticePoliceDeathPreJudgmentHolding",
                    player,
                    Game.GameTime);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                PrepareJusticeFineDebitIntent(script);

                residualMissionFlag = true;
                for (int tick = 0; tick < 160 && rejectedFadeInCount == 0; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                }

                Assert.AreEqual(
                    1,
                    rejectedFadeInCount,
                    "Le premier FadeIn pré-ACK doit être tenté puis refusé une seule fois.");
                Assert.IsTrue(rejectedFadeInAt >= 0);
                Assert.AreSame(player, Game.Player.Character);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsTrue(profile.PendingLegalReleaseFinalization);
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathNoCellReleaseProtectionRestorePending"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyRespawnRestorePending"));
                Assert.IsTrue((bool)InvokeStatic(
                    "IsJusticeCustodyRespawnTransferMaskActive"));
                Assert.IsTrue(player.FreezePosition);
                Assert.IsTrue(player.IsInvincible);
                Assert.IsFalse(player.CanRagdoll);
                Assert.AreEqual(
                    "JusticePreJudgmentHolding",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());
                Assert.AreEqual(3, Game.Player.WantedLevel);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);

                int rejectedFadeInCalls = CountNative(Hash.DO_SCREEN_FADE_IN);
                int wantedClearedAt = -1;
                int acceptedFadeInAt = -1;
                for (int tick = 0; tick < 40; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                    if (Game.Player.WantedLevel == 0 && wantedClearedAt < 0)
                    {
                        wantedClearedAt = Game.GameTime;
                    }

                    int fadeInCalls = CountNative(Hash.DO_SCREEN_FADE_IN);
                    if (fadeInCalls > rejectedFadeInCalls)
                    {
                        acceptedFadeInAt = Game.GameTime;
                        break;
                    }
                    if (wantedClearedAt >= 0 &&
                        unchecked((uint)(Game.GameTime - wantedClearedAt)) < 1000U)
                    {
                        Assert.AreEqual(rejectedFadeInCalls, fadeInCalls);
                    }
                }

                Assert.IsTrue(
                    wantedClearedAt > rejectedFadeInAt,
                    "Le wanted doit etre relu puis efface apres le refus; cleared=" +
                    wantedClearedAt.ToString(CultureInfo.InvariantCulture) +
                    ", rejected=" +
                    rejectedFadeInAt.ToString(CultureInfo.InvariantCulture) +
                    ", wanted=" + Game.Player.WantedLevel +
                    ", release=" +
                    GetField<bool>(script, "_justiceLegalReleaseFinalizationPending") +
                    ", protection=" +
                    GetField<bool>(
                        script,
                        "_justicePoliceDeathNoCellReleaseProtectionRestorePending") +
                    ", fadeIn=" +
                    GetField<bool>(
                        script,
                        "_justicePoliceDeathNoCellReleaseFadeInRequested") +
                    ", physical=" +
                    GetField<bool>(
                        script,
                        "_justicePoliceDeathNoCellReleasePhysicalReadyForAcknowledgement") +
                    ", source=" +
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource") +
                    ", suspended=" +
                    GetField<bool>(script, "_justiceRuntimeSuspendedCached") +
                    ", missionOnly=" +
                    GetField<bool>(
                        script,
                        "_justiceRuntimeSuspendedByMissionFlagOnlyCached") +
                    ", bypass=" +
                    GetField<bool>(
                        script,
                        "_justiceCustodyResidualMissionFlagBypassArmed") +
                    ", deadline=" +
                    GetField<long>(
                        script,
                        "_justiceCustodyResidualMissionFlagObservationDeadlineMs") +
                    ", protectedNoCell=" +
                    (bool)Invoke(
                        script,
                        "IsJusticeProtectedPoliceDeathNoCellLegalRelease",
                        player) +
                    ", canIgnore=" +
                    (bool)Invoke(
                        script,
                        "CanIgnoreJusticeMissionFlagForCustody",
                        player) +
                    ", transferMask=" +
                    GetField<bool>(script, "_justiceCustodyRespawnTransferPending") +
                    ", restoreMask=" +
                    GetField<bool>(script, "_justiceCustodyRespawnRestorePending") +
                    ", maskIntent=" +
                    GetField<bool>(script, "_justicePoliceDeathRespawnMaskIntentPending") +
                    ", nextWanted=" +
                    GetField<int>(
                        script,
                        "_justiceNextPoliceDeathNoCellReleaseWantedObservationAt") +
                    ", now=" + Game.GameTime +
                    ", maskNeeds=" +
                    GetField<bool>(script, "_justiceCustodyRespawnMaskNeedsRearm") +
                    ", barrier=" +
                    (GetField<string>(script, "_justiceCriticalBarrierCaller") ?? "") +
                    ", barrierRevision=" +
                    GetField<long>(script, "_justiceCriticalBarrierRevision") +
                    ", nextLegal=" +
                    GetField<int>(script, "_justiceNextLegalReleaseWantedClearAt") +
                    ", profileBlocked=" +
                    GetField<bool>(script, "_justiceProfileContextBlocked") +
                    ", selection=" +
                    GetField<bool>(script, "_justiceProfileSelectionPending") +
                    ", switch=" +
                    GetField<bool>(script, "_justiceProfileSwitchPersistencePending"));

                Assert.IsTrue(
                    acceptedFadeInAt >= 0,
                    "Le retry FadeIn no-cell doit progresser; now=" +
                    Game.GameTime.ToString(CultureInfo.InvariantCulture) +
                    ", nextWanted=" +
                    GetField<int>(
                        script,
                        "_justiceNextPoliceDeathNoCellReleaseWantedObservationAt")
                        .ToString(CultureInfo.InvariantCulture) +
                    ", stable=" +
                    GetField<bool>(
                        script,
                        "_justicePoliceDeathNoCellReleaseWantedStabilityStarted") +
                    ", stableSince=" +
                    GetField<int>(
                        script,
                        "_justicePoliceDeathNoCellReleaseWantedStableSinceAt")
                        .ToString(CultureInfo.InvariantCulture) +
                    ", release=" +
                    GetField<bool>(script, "_justiceLegalReleaseFinalizationPending") +
                    ", protection=" +
                    GetField<bool>(
                        script,
                        "_justicePoliceDeathNoCellReleaseProtectionRestorePending") +
                    ", erreur=" +
                    (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
                Assert.IsTrue(
                    unchecked((uint)(acceptedFadeInAt - wantedClearedAt)) >= 1000U,
                    "Le FadeIn accepté doit attendre une nouvelle seconde continue à zéro.");
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count >= 2 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));
                for (int tick = 0;
                     tick < 40 && GetField<bool>(
                         script,
                         "_justicePoliceDeathNoCellReleaseProtectionRestorePending");
                     tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                }
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathNoCellReleaseProtectionRestorePending"));
                Assert.IsFalse((bool)InvokeStatic(
                    "IsJusticeCustodyRespawnTransferMaskActive"));
                Assert.IsFalse(player.FreezePosition);
                Assert.IsFalse(player.IsInvincible);
                Assert.IsTrue(player.CanRagdoll);
                Assert.AreEqual(
                    "None",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);

                // Une étoile née après la restitution est une nouvelle poursuite :
                // l'ancien ACK ne possède plus aucun droit de clear tardif.
                int fadeInCallsAfterRestore = CountNative(Hash.DO_SCREEN_FADE_IN);
                Game.Player.WantedLevel = 2;
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                }
                Assert.AreEqual(2, Game.Player.WantedLevel);
                Assert.AreEqual(
                    fadeInCallsAfterRestore,
                    CountNative(Hash.DO_SCREEN_FADE_IN));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(profile.PendingLegalReleaseFinalization);
                Assert.AreEqual(1, cashWriteCount);
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

    [TestMethod]
    public void PoliceCapture_NoCellReleaseIgnoresResidualArrestFlagAndLetsLateFinish()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1233;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = false;
            player.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            bool residualArrestFlag = false;
            int cash = 900;
            int cashWriteCount = 0;
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                null,
                null,
                null,
                null,
                () => residualArrestFlag);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state =
                    ConfigureCapturedPoliceDeathFineResumeState(
                        script,
                        player,
                        false,
                        0);
                JusticePlayerProfileState profile =
                    GetField<JusticePlayerProfileState[]>(
                        script,
                        "_justicePlayerProfiles")[0];
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Game.GameTime += 250;
                Invoke(
                    script,
                    "UpdateJusticePoliceDeathPreJudgmentHolding",
                    player,
                    Game.GameTime);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                PrepareJusticeFineDebitIntent(script);

                SetField(
                    script,
                    "_justiceStateFlushFailureOverride",
                    new Func<int, bool>(attempt => GetField<bool>(
                        script,
                        "_justiceLegalReleaseFinalizationPending")));
                residualMissionFlag = true;
                bool pendingNoCellRelease = false;
                for (int tick = 0; tick < 96; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    pendingNoCellRelease =
                        state.Phase == JusticePhase.AtLarge &&
                        GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending") &&
                        profile.PendingDeathCapture;
                    if (pendingNoCellRelease)
                    {
                        break;
                    }
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                }

                Assert.IsTrue(
                    pendingNoCellRelease,
                    "La peine nulle doit atteindre la sortie durable sans cellule.");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"),
                    "Le bypass mission résiduel doit rester armé jusqu'à l'ACK.");

                // Je publie le couple PendingDeath + LegalRelease avant de faire
                // retomber le flag mission afin de tester la vraie reprise durable.
                SetField(script, "_justiceStateFlushFailureOverride", null);
                SetField(script, "_justiceNextStateFlushAttemptAtMs", 0L);
                FlushAndAwait(script);
                Assert.IsTrue(
                    profile.PendingLegalReleaseFinalization,
                    "Le profil doit porter LegalRelease avant la chute du flag mission.");
                Assert.IsTrue(profile.PendingDeathCapture);
                int chargeCount = state.Charges.Count;
                int convictionCount = profile.RecordState.Convictions.Count;
                string[] completedOperations = state.CompletedOperationIds.ToArray();

                residualMissionFlag = false;
                residualArrestFlag = true;
                ConfigureAdmissionNatives(
                    () => residualMissionFlag,
                    null,
                    null,
                    null,
                    null,
                    null,
                    () => residualArrestFlag);

                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");

                Assert.AreSame(player, Game.Player.Character);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.AreEqual(chargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                CollectionAssert.AreEqual(
                    completedOperations,
                    state.CompletedOperationIds.ToArray());
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"),
                    "Early ne doit pas acquitter la sortie sous le flag d'arrestation résiduel.");
                Assert.IsTrue(
                    profile.PendingLegalReleaseFinalization,
                    "Le profil doit conserver la barrière LegalRelease pour Late.");
                Assert.IsTrue(
                    profile.PendingDeathCapture,
                    "Le DeathFront doit rester couplé à LegalRelease jusqu'à l'ACK.");
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.AreEqual(1, cashWriteCount);

                bool custodyWasArmed = false;
                for (int tick = 0; tick < 4; tick++)
                {
                    Invoke(script, "UpdateJusticeSystem");
                    custodyWasArmed |=
                        GetField<bool>(script, "_justiceCustodyRuntimeActive") ||
                        GetField<bool>(script, "_justiceCustodyTransferPending") ||
                        GetField<bool>(script, "_justiceCustodyResumePending");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                }

                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsTrue(profile.PendingLegalReleaseFinalization);
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.AreEqual(chargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                CollectionAssert.AreEqual(
                    completedOperations,
                    state.CompletedOperationIds.ToArray());
                Assert.IsTrue(player.IsInvincible);
                Assert.IsTrue(player.FreezePosition);
                Assert.IsFalse(player.CanRagdoll);
                Assert.AreEqual(0, CountNative(Hash.DO_SCREEN_FADE_IN));

                residualArrestFlag = false;
                for (int tick = 0; tick < 160; tick++)
                {
                    Invoke(script, "UpdateJusticeSystem");
                    custodyWasArmed |=
                        GetField<bool>(script, "_justiceCustodyRuntimeActive") ||
                        GetField<bool>(script, "_justiceCustodyTransferPending") ||
                        GetField<bool>(script, "_justiceCustodyResumePending");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                    if (!GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending") &&
                        !profile.PendingDeathCapture &&
                        !GetField<bool>(
                            script,
                            "_justicePoliceDeathNoCellReleaseProtectionRestorePending"))
                    {
                        break;
                    }

                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                }

                Assert.IsFalse(custodyWasArmed);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.AreEqual(chargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                CollectionAssert.AreEqual(
                    completedOperations,
                    state.CompletedOperationIds.ToArray());
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(profile.PendingLegalReleaseFinalization);
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsInsideJusticeCustody",
                    player.Position));
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    CountNative(Hash.DO_SCREEN_FADE_IN) > 0,
                    "Le contrôleur Late doit terminer la même sortie sans cellule.");

                int fadeInCountAfterRelease = CountNative(Hash.DO_SCREEN_FADE_IN);
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(chargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                CollectionAssert.AreEqual(
                    completedOperations,
                    state.CompletedOperationIds.ToArray());
                Assert.AreEqual(
                    fadeInCountAfterRelease,
                    CountNative(Hash.DO_SCREEN_FADE_IN));
                Assert.IsFalse(profile.PendingDeathCapture);
            }
            finally
            {
                if (script != null)
                {
                    SetField(script, "_justiceStateFlushFailureOverride", null);
                    Invoke(script, "ShutdownJusticeSystem");
                }
            }
        });
    }

    [TestMethod]
    public void PoliceCapture_CustomNoCellReleaseResumesUnderMissionAndBustedUntilFlagDrops()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1234;
            deadPlayer.Model = new Model("mp_m_freemode_01");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            deadPlayer.CanRagdoll = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            bool residualArrestFlag = true;
            int currentSlot = 0;
            List<int> wantedObservedAtFadeIn = new List<int>();
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                wantedObservedAtFadeIn,
                null,
                null,
                null,
                () => residualArrestFlag);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state =
                    ConfigureCapturedPoliceDeathFineResumeState(
                        script,
                        deadPlayer,
                        true,
                        0);
                JusticePlayerProfileState profile =
                    GetField<JusticePlayerProfileState[]>(
                        script,
                        "_justicePlayerProfiles")[0];
                JusticeOperation paidFine = new JusticeOperation(
                    JusticePolicy.CreateOperationId(
                        JusticeOperationKind.ApplyFine,
                        state.CustodyEpisodeId),
                    JusticeOperationKind.ApplyFine,
                    state.CustodyEpisodeId);
                Assert.IsTrue(JusticePolicy.TryRegisterOperation(state, paidFine));
                state.FineDue = 0L;
                state.SentenceSeconds = 0;
                state.CustodyGuardPenaltySeconds = 0L;
                Invoke(script, "JusticeMarkStateDirty");
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));
                int convictionCount = profile.RecordState.Convictions.Count;
                int cash = 900;
                int cashWriteCount = 0;
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Ped respawnedPlayer = new Ped
                {
                    Handle = 1235,
                    Model = new Model("mp_m_freemode_01"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    CanRagdoll = true
                };
                Game.Player.Character = respawnedPlayer;
                Game.Player.IsDead = false;
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => currentSlot));
                residualMissionFlag = true;

                // Je laisse d'abord Late établir le holding exact du respawn
                // custom; aucun bypass ne doit précéder cette preuve physique.
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(
                        script,
                        "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.AreEqual(0, GetField<int>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingOwnerSlot"));
                Assert.AreEqual(
                    respawnedPlayer.Model.Hash,
                    GetField<int>(
                        script,
                        "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    respawnedPlayer.Position));
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);

                int forcedReleaseFailures = 0;
                SetField(
                    script,
                    "_justiceStateFlushFailureOverride",
                    new Func<int, bool>(attempt =>
                    {
                        if (GetField<bool>(
                                script,
                                "_justiceLegalReleaseFinalizationPending"))
                        {
                            forcedReleaseFailures++;
                            return true;
                        }
                        return false;
                    }));

                bool legalReleaseObservedUnderMission = false;
                for (int tick = 0; tick < 160; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    legalReleaseObservedUnderMission |=
                        state.Phase == JusticePhase.AtLarge &&
                        GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending");
                    if (legalReleaseObservedUnderMission)
                    {
                        break;
                    }
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                }

                Assert.IsTrue(
                    legalReleaseObservedUnderMission,
                    "Le bypass doit atteindre LegalRelease malgré le respawn custom et BUSTED.");
                Assert.IsTrue(forcedReleaseFailures >= 1);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.AreEqual(0, cashWriteCount);
                Assert.AreEqual(900, cash);

                // Après l'effacement volontaire du snapshot custody, le modèle
                // custom n'expose plus de slot GTA. Le holding exact doit suffire
                // au contrôleur Late pendant le seul flag mission résiduel.
                currentSlot = -1;
                residualArrestFlag = false;
                SetField(script, "_justiceStateFlushFailureOverride", null);
                bool acknowledgedUnderMission = false;
                for (int tick = 0; tick < 160; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                    if (!GetField<bool>(
                            script,
                            "_justiceLegalReleaseFinalizationPending") &&
                        !profile.PendingDeathCapture)
                    {
                        acknowledgedUnderMission = true;
                        break;
                    }
                }

                Assert.IsTrue(
                    acknowledgedUnderMission,
                    "Late doit acquitter la sortie sans cellule avant la chute du flag mission.");
                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.AreEqual(-1, (int)Invoke(
                    script,
                    "GetCurrentSinglePlayerCashSlotSafe"));
                Assert.AreEqual(0, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                int releasedChargeCount = state.Charges.Count;
                Assert.AreEqual(0, releasedChargeCount);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathNoCellReleaseProtectionRestorePending"));
                Assert.IsFalse(respawnedPlayer.IsInvincible);
                Assert.IsFalse(respawnedPlayer.FreezePosition);
                Assert.IsTrue(respawnedPlayer.CanRagdoll);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));

                // Une fois BUSTED retombé, le seul flag mission qualifié ne doit
                // pas bloquer la seconde stable ni le FadeIn du respawn custom.
                for (int tick = 0; tick < 32 && GetField<bool>(
                         script,
                         "_justicePoliceDeathNoCellReleaseProtectionRestorePending"); tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                }

                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathNoCellReleaseProtectionRestorePending"));
                Assert.IsTrue(
                    residualMissionFlag,
                    "La restitution physique doit finir sous le bypass mission-only.");
                Assert.IsFalse(residualArrestFlag);
                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.IsFalse(respawnedPlayer.IsInvincible);
                Assert.IsFalse(respawnedPlayer.FreezePosition);
                Assert.IsTrue(respawnedPlayer.CanRagdoll);
                Assert.IsFalse(StubRuntime.ScreenFadedOut);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.AreEqual(0, cashWriteCount);
                Assert.AreEqual(releasedChargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsInsideJusticeCustody",
                    respawnedPlayer.Position));

                // La chute réelle du flag ne doit plus pouvoir rejuger ce dossier.
                residualMissionFlag = false;
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(releasedChargeCount, state.Charges.Count);
                Assert.AreEqual(convictionCount, profile.RecordState.Convictions.Count);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(profile.PendingDeathCapture);
            }
            finally
            {
                if (script != null)
                {
                    SetField(script, "_justiceStateFlushFailureOverride", null);
                    Invoke(script, "ShutdownJusticeSystem");
                }
            }
        });
    }

    [TestMethod]
    public void PoliceCapture_RefusedFirstRespawnFadeKeepsExactHeroProtectedUntilRetryAdmission()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1228;
            deadPlayer.Model = new Model("player_zero");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            deadPlayer.CanRagdoll = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            bool refuseFadeOutState = true;
            List<int> wantedObservedAtFadeIn = new List<int>();
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                wantedObservedAtFadeIn,
                null,
                null,
                () => refuseFadeOutState);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, deadPlayer, 180);
                JusticeCaseState state = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                state.FineDue = 0L;
                state.Charges[0].Fine = 0L;

                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    deadPlayer));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                Ped respawnedPlayer = new Ped
                {
                    Handle = 1229,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    CanRagdoll = true
                };
                Game.Player.Character = respawnedPlayer;
                Game.Player.IsDead = false;
                residualMissionFlag = true;
                GTA.Math.Vector3 hospitalPosition = respawnedPlayer.Position;
                int sentenceBeforeRetry = state.SentenceSeconds;

                for (int tick = 0; tick < 6; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                }

                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.IsFalse((bool)InvokeStatic(
                    "IsJusticeCustodyRespawnTransferMaskActive"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyRespawnMaskNeedsRearm"));
                Assert.AreEqual(
                    255,
                    respawnedPlayer.Alpha,
                    "Le refus du fondu ne doit jamais cacher le h\u00e9ros lui-m\u00eame.");
                Assert.IsTrue(respawnedPlayer.FreezePosition);
                Assert.IsTrue(respawnedPlayer.IsInvincible);
                Assert.IsFalse(respawnedPlayer.CanRagdoll);
                Assert.AreEqual(
                    "JusticePreJudgmentHolding",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());
                Assert.AreEqual(hospitalPosition.X, respawnedPlayer.Position.X, 0.001f);
                Assert.AreEqual(hospitalPosition.Y, respawnedPlayer.Position.Y, 0.001f);
                Assert.AreEqual(hospitalPosition.Z, respawnedPlayer.Position.Z, 0.001f);
                Assert.AreEqual(JusticePhase.Wanted, state.Phase);
                Assert.AreEqual(sentenceBeforeRetry, state.SentenceSeconds);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePreJudgmentHoldingPositionApplied"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.AreEqual(0, GetField<int>(
                    script,
                    "_justiceCustodyElapsedRemainderMs"));
                Assert.AreEqual(
                    0,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
                Assert.IsTrue(CountNative(Hash.DO_SCREEN_FADE_OUT) >= 2);
                Assert.AreEqual(0, CountNative(Hash.DO_SCREEN_FADE_IN));

                // Dès que GTA confirme réellement le noir, le même héros peut
                // quitter l'hôpital, être admis sans étoiles et démarrer sa peine.
                refuseFadeOutState = false;
                AdvanceUntilIncarcerated(script, state, 96, 250);

                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyContainmentEstablished"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticeCustody",
                    respawnedPlayer.Position));
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(
                    wantedObservedAtFadeIn.Count > 0 &&
                    wantedObservedAtFadeIn.All(wanted => wanted == 0));

                int sentenceAtAdmission = state.SentenceSeconds;
                for (int tick = 0; tick < 4; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);
                }
                Assert.AreEqual(sentenceAtAdmission - 1, state.SentenceSeconds);
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

    [TestMethod]
    public void PoliceCapture_CustomCapturedRebindKeepsDeathFrontUntilTransferIsArmed()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1216;
            deadPlayer.Model = new Model("mp_m_freemode_01");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;

            bool residualMissionFlag = false;
            ConfigureAdmissionNatives(
                () => residualMissionFlag,
                null,
                null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, deadPlayer, 180);
                // Je reproduis un modèle custom apparu après qu'un héros
                // canonique a déjà prouvé le profil actif dans cette session.
                SetField(script, "_justiceProfileSelectionPending", false);
                JusticeCaseState state = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                state.FineDue = 0L;
                state.Charges[0].Fine = 0L;

                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    deadPlayer));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                // Je reconstruis ici la frontière exacte d'un crash situé après
                // le jugement durable, mais avant l'armement du transfert runtime.
                const string custodyEpisode = "custody:custom-captured-rebind";
                state.Phase = JusticePhase.Captured;
                state.CustodyEpisodeId = custodyEpisode;
                state.HasWarrant = false;
                Assert.IsTrue(JusticePolicy.TryRegisterOperation(
                    state,
                    new JusticeOperation(
                        JusticePolicy.CreateOperationId(
                            JusticeOperationKind.Capture,
                            custodyEpisode),
                        JusticeOperationKind.Capture,
                        custodyEpisode)));
                Assert.IsTrue(JusticePolicy.TryRegisterOperation(
                    state,
                    new JusticeOperation(
                        JusticePolicy.CreateOperationId(
                            JusticeOperationKind.ApplyConviction,
                            custodyEpisode),
                        JusticeOperationKind.ApplyConviction,
                        custodyEpisode)));
                Assert.IsNotNull(JusticePolicy.ApplyConviction(
                    state,
                    GetField<JusticeRecordState>(script, "_justiceRecordState"),
                    DateTime.UtcNow));
                SetField(script, "_justiceCustodyPlayerSlot", 0);
                SetField(script, "_justiceCustodyPlayerHandle", deadPlayer.Handle);
                SetField(
                    script,
                    "_justiceCustodyPlayerModelHash",
                    deadPlayer.Model.Hash);
                SetField(script, "_justiceCustodyWaitingForRespawn", true);
                SetField(script, "_justiceCustodyDeathRebindPending", true);
                SetField(script, "_justiceCustodyRuntimeActive", false);
                SetField(script, "_justiceCustodyTransferPending", false);
                SetField(script, "_justiceCustodyResumePending", false);
                Invoke(script, "JusticeMarkStateDirty");
                CompleteCriticalPrecommit(script, "BeginJusticeCapture");
                Invoke(script, "ConfirmJusticeCapturePrecommit");
                Assert.IsTrue((bool)Invoke(
                    script,
                    "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));

                Ped respawnedPlayer = new Ped
                {
                    Handle = 1217,
                    Model = new Model("mp_m_freemode_01"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    CanRagdoll = true
                };
                Game.Player.Character = respawnedPlayer;
                Game.Player.IsDead = false;
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => -1));
                residualMissionFlag = true;

                // Je fais d'abord établir le holding Captured. Le bypass ne doit
                // pas exister tant que cette position physique n'est pas prouvée.
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    respawnedPlayer.Position));
                Assert.AreEqual(0, GetField<int>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingOwnerSlot"));
                Assert.AreEqual(
                    respawnedPlayer.Model.Hash,
                    GetField<int>(
                        script,
                        "_justicePoliceDeathPreJudgmentHoldingOwnerModelHash"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));

                bool transferArmed = false;
                bool deathFrontConsumedBeforeTransfer = false;
                for (int tick = 0; tick < 24; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    bool runtimeActive = GetField<bool>(
                        script,
                        "_justiceCustodyRuntimeActive");
                    bool transferPending = GetField<bool>(
                        script,
                        "_justiceCustodyTransferPending");
                    if (!GetField<bool>(
                            script,
                            "_justicePursuitDeathObservedDuringSuspension") &&
                        (!runtimeActive || !transferPending))
                    {
                        deathFrontConsumedBeforeTransfer = true;
                    }
                    Invoke(script, "UpdateJusticeSystem");
                    AwaitQueuedPersistence(script);

                    runtimeActive = GetField<bool>(
                        script,
                        "_justiceCustodyRuntimeActive");
                    transferPending = GetField<bool>(
                        script,
                        "_justiceCustodyTransferPending");
                    if (!GetField<bool>(
                            script,
                            "_justicePursuitDeathObservedDuringSuspension") &&
                        (!runtimeActive || !transferPending))
                    {
                        deathFrontConsumedBeforeTransfer = true;
                    }
                    if (runtimeActive && transferPending)
                    {
                        transferArmed = true;
                        break;
                    }
                }

                Assert.IsTrue(
                    transferArmed,
                    "Le bypass mission-only doit permettre le rebind custom puis armer le transfert.");
                Assert.IsFalse(
                    deathFrontConsumedBeforeTransfer,
                    "Le front ne doit jamais être consommé avant runtime+transfer exacts.");
                Assert.AreEqual(1217, GetField<int>(script, "_justiceCustodyPlayerHandle"));
                for (int tick = 0; tick < 3; tick++)
                {
                    // Je laisse Early repasser sans preuve visuelle : ni le latch
                    // runtime ni le DeathFront persistant ne doivent disparaître.
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    AwaitQueuedPersistence(script);
                }
                Assert.IsTrue(
                    GetField<bool>(
                        script,
                        "_justicePursuitDeathObservedDuringSuspension"),
                    "Le latch runtime doit survivre jusqu'au FadeIn final exact.");
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);

                Invoke(script, "UpdateJusticeSystem");
                AdvanceUntilIncarcerated(script, state, 48, 250);
                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.AreEqual(-1, (int)Invoke(
                    script,
                    "GetCurrentSinglePlayerCashSlotSafe"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyPlayerSlot"));
                Assert.AreEqual(
                    respawnedPlayer.Model.Hash,
                    GetField<int>(script, "_justiceCustodyPlayerModelHash"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsFalse(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
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

    [TestMethod]
    public void PoliceCapture_CustomDeathCanonicalRespawnKeepsCapturedHoldingUntilTransferRetryCompletes()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            int currentSlot = -1;
            bool streamingReady = true;
            int refusedStreamingProbes = 0;
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1223;
            deadPlayer.Model = new Model("mp_m_freemode_01");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            deadPlayer.CanRagdoll = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(
                () => false,
                null,
                null,
                null,
                () =>
                {
                    if (!streamingReady)
                    {
                        refusedStreamingProbes++;
                    }
                    return streamingReady;
                });

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = PrepareCustomPoliceDeathCaptureRetry(
                    script,
                    deadPlayer,
                    new Model("player_zero").Hash,
                    () => currentSlot);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];
                int deathModel = deadPlayer.Model.Hash;

                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.AreEqual(deathModel, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));

                Ped respawnedPlayer = new Ped
                {
                    Handle = 1224,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f),
                    CanRagdoll = true
                };
                Game.Player.Character = respawnedPlayer;
                Game.Player.IsDead = false;
                currentSlot = 0;

                AdvanceEarlyUntilPoliceDeathTransferArmed(script, state, 16, 250);
                Assert.AreEqual(JusticePhase.Transporting, state.Phase);
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(deadPlayer.Handle, GetField<int>(
                    script,
                    "_justiceCustodyPlayerHandle"));
                Assert.AreEqual(deathModel, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));
                Assert.IsTrue(profile.PendingDeathCapture);

                Invoke(script, "RefreshJusticePreJudgmentHoldingIntent", respawnedPlayer);
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue((bool)Invoke(
                    script,
                    "MustBlockJusticeLateForPreJudgmentHolding",
                    respawnedPlayer));

                // Je refuse d'abord le streaming : Late ne doit ni adopter le
                // modèle canonique ni consommer le front avant la preuve physique.
                streamingReady = false;
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeSystem");
                Assert.IsTrue(refusedStreamingProbes > 0);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(deathModel, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));
                Assert.IsTrue(profile.PendingDeathCapture);

                // Je rends ensuite le streaming disponible. Ce tick établit le
                // holding avec l'identité A, mais conserve le DeathFront jusqu'au
                // FadeIn final qui suivra l'adoption du modèle canonique B.
                streamingReady = true;
                Game.GameTime += 1000;
                Invoke(script, "UpdateJusticeSystem");
                AwaitQueuedPersistence(script);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsInsideJusticePoliceDeathPreJudgmentHolding",
                    respawnedPlayer.Position));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(deathModel, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));

                // Je provoque maintenant un échec transitoire de Complete après
                // le rebind B. Le relais Captured, le masque et le transfert
                // doivent survivre exactement jusqu'au retry suivant.
                streamingReady = false;
                int probesBeforeTransferFailure = refusedStreamingProbes;
                int failuresBeforeTransferRetry = GetField<int>(
                    script,
                    "_justiceCustodyTransferFailureCount");
                for (int tick = 0;
                     tick < 64 &&
                     (GetField<bool>(script, "_justiceCustodyDeathRebindPending") ||
                      refusedStreamingProbes == probesBeforeTransferFailure);
                     tick++)
                {
                    Game.GameTime += 250;
                    // Je vise ici le contrôleur Late lui-même : repasser d'abord
                    // par UpdateHolding réessaierait son streaming encore protégé
                    // et masquerait l'échec spécifique de Complete.
                    Invoke(
                        script,
                        "JusticeUpdateCustody",
                        respawnedPlayer,
                        Game.GameTime);
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }
                }

                Assert.IsTrue(
                    refusedStreamingProbes > probesBeforeTransferFailure,
                    "Complete doit avoir rencontré le refus de streaming injecté.");
                Assert.IsTrue(
                    GetField<int>(script, "_justiceCustodyTransferFailureCount") >
                        failuresBeforeTransferRetry,
                    "L'échec transitoire doit rester enregistré pour le retry borné.");
                Assert.AreEqual(JusticePhase.Transporting, state.Phase);
                Assert.AreEqual(
                    "Captured",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(respawnedPlayer.Handle, GetField<int>(
                    script,
                    "_justiceCustodyPlayerHandle"));
                Assert.AreEqual(respawnedPlayer.Model.Hash, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));
                Assert.IsTrue(
                    respawnedPlayer.IsInvincible,
                    "Le propriétaire Justice doit rester invincible après l'échec de Complete.");
                Assert.IsTrue(
                    StubRuntime.ScreenFadedOut,
                    "Le masque doit rester actif après l'échec de Complete.");
                Assert.AreEqual(
                    "JusticePreJudgmentHolding",
                    GetFieldObject(script, "_playerInvincibilityOwners").ToString());

                streamingReady = true;
                bool retryPersistenceConfirmed = true;
                for (int tick = 0; tick < 64; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    retryPersistenceConfirmed &= (bool)Invoke(
                        script,
                        "JusticeAwaitQueuedPersistenceForTests");
                    if (state.Phase == JusticePhase.Incarcerated &&
                        !GetField<bool>(script, "_justiceCustodyTransferPending") &&
                        GetField<bool>(script, "_justiceCustodyContainmentEstablished"))
                    {
                        break;
                    }
                }

                Assert.AreSame(respawnedPlayer, Game.Player.Character);
                Assert.IsTrue(
                    retryPersistenceConfirmed,
                    "Le writer doit rester acquitté pendant le retry final; erreur=" +
                    (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
                Assert.AreEqual(
                    JusticePhase.Incarcerated,
                    state.Phase,
                    "Le retry custom doit finir; transfer=" +
                    GetField<bool>(script, "_justiceCustodyTransferPending") +
                    ", waiting=" +
                    GetField<bool>(script, "_justiceCustodyWaitingForRespawn") +
                    ", rebind=" +
                    GetField<bool>(script, "_justiceCustodyDeathRebindPending") +
                    ", fadeIn=" +
                    GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested") +
                    ", holding=" +
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource") +
                    ", next=" +
                    GetField<int>(script, "_justiceNextCustodyTransferAttemptAt") +
                    ", now=" + Game.GameTime +
                    ", erreur=" +
                    (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
                Assert.IsFalse(
                    GetField<bool>(script, "_justiceCustodyTransferPending"),
                    "Le retry final doit acquitter le transfert; échecs=" +
                    GetField<int>(script, "_justiceCustodyTransferFailureCount")
                        .ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(
                    GetField<bool>(script, "_justiceCustodyContainmentEstablished"),
                    "Le retry final doit conserver le containment établi.");
                Assert.AreEqual(
                    "None",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("Capture:", StringComparison.Ordinal)));
                Assert.AreEqual(
                    1,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("EnterCustody:", StringComparison.Ordinal)));
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

    [TestMethod]
    public void PoliceCapture_ArmedCustomDeathTransferRejectsDivergentModelWithoutAdoption()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            int currentSlot = -1;
            Ped deadPlayer = Game.Player.Character;
            deadPlayer.Handle = 1225;
            deadPlayer.Model = new Model("mp_m_freemode_01");
            deadPlayer.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            deadPlayer.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(() => false, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = PrepareCustomPoliceDeathCaptureRetry(
                    script,
                    deadPlayer,
                    new Model("player_zero").Hash,
                    () => currentSlot);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];

                Ped canonicalRespawn = new Ped
                {
                    Handle = 1226,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f)
                };
                Game.Player.Character = canonicalRespawn;
                Game.Player.IsDead = false;
                currentSlot = 0;
                AdvanceEarlyUntilPoliceDeathTransferArmed(script, state, 16, 250);

                // Je remplace le respawn admissible avant tout rebind par un ped
                // custom divergent. Son modèle seul ne peut jamais hériter du slot 0.
                Ped divergentPlayer = new Ped
                {
                    Handle = 1227,
                    Model = new Model("s_m_y_cop_01"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f)
                };
                GTA.Math.Vector3 hospitalPosition = divergentPlayer.Position;
                Game.Player.Character = divergentPlayer;
                currentSlot = -1;

                Invoke(script, "RefreshJusticePreJudgmentHoldingIntent", divergentPlayer);
                Assert.AreEqual(
                    "DurablePoliceDeath",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsPendingJusticeDeathCaptureIdentityCompatible",
                    divergentPlayer));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "MustBlockJusticeLateForPreJudgmentHolding",
                    divergentPlayer));

                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeSystem");

                Assert.AreSame(divergentPlayer, Game.Player.Character);
                Assert.AreEqual(hospitalPosition.X, divergentPlayer.Position.X, 0.001f);
                Assert.AreEqual(hospitalPosition.Y, divergentPlayer.Position.Y, 0.001f);
                Assert.AreEqual(JusticePhase.Transporting, state.Phase);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(deadPlayer.Handle, GetField<int>(
                    script,
                    "_justiceCustodyPlayerHandle"));
                Assert.AreEqual(deadPlayer.Model.Hash, GetField<int>(
                    script,
                    "_justiceCustodyPlayerModelHash"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathPreJudgmentHoldingEstablished"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.AreEqual(0, CountNative(Hash.DO_SCREEN_FADE_IN));
                Assert.AreEqual(
                    0,
                    state.CompletedOperationIds.Count(operation =>
                        operation.StartsWith("EnterCustody:", StringComparison.Ordinal)));
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

    [TestMethod]
    public void PoliceCapture_StrongSuspensionsNeverConsumeDurableDeathFront()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1211;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(() => false, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, player, 180);
                JusticeCaseState state = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    player));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                player.IsDead = false;
                Game.Player.IsDead = false;
                string[] strongSuspensions =
                {
                    "Loading",
                    "Cutscene",
                    "PlayerSwitch",
                    "MissionNativeFailure"
                };
                foreach (string suspensionKind in strongSuspensions)
                {
                    ConfigureAdmissionNatives(
                        () => true,
                        suspensionKind,
                        null);
                    Assert.IsTrue((bool)Invoke(
                        script,
                        "ComputeJusticeRuntimeSuspended",
                        player), suspensionKind);
                    Assert.IsFalse(
                        GetField<bool>(
                            script,
                            "_justiceRuntimeSuspendedByMissionFlagOnlyCached"),
                        suspensionKind +
                        " ne doit jamais être qualifié comme un simple latch BUSTED.");
                    Assert.IsFalse((bool)Invoke(
                        script,
                        "TryFinalizeJusticePendingPoliceDeathCaptureDuringResidualMissionFlag",
                        player), suspensionKind);
                    Assert.AreEqual(JusticePhase.Wanted, state.Phase, suspensionKind);
                    Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                        script,
                        "_justicePlayerProfiles")[0].PendingDeathCapture,
                        suspensionKind);
                }

                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.AreEqual(
                    0,
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

    [TestMethod]
    public void PoliceCapture_ResidualMissionFlagRejectsWrongSlotAndWrongModel()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped owner = Game.Player.Character;
            owner.Handle = 1212;
            owner.Model = new Model("player_zero");
            owner.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            owner.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(() => false, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                ConfigureLivePoliceDeathCase(script, owner, 180);
                JusticeCaseState ownerState = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryPersistJusticePoliceDeathFrontToWal",
                    owner));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                Ped otherHero = new Ped
                {
                    Handle = 1213,
                    Model = new Model("player_zero"),
                    Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f)
                };
                Game.Player.Character = otherHero;
                Game.Player.IsDead = false;
                owner.IsDead = false;
                ConfigureAdmissionNatives(() => true, null, null);

                // Je garde d'abord le bon modèle mais fournis le mauvais slot.
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "ComputeJusticeRuntimeSuspended",
                    otherHero));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceRuntimeSuspendedByMissionFlagOnlyCached"));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "TryFinalizeJusticePendingPoliceDeathCaptureDuringResidualMissionFlag",
                    otherHero));

                // Je fournis ensuite le bon slot mais un modèle d'autre héros.
                otherHero.Model = new Model("player_one");
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "ComputeJusticeRuntimeSuspended",
                    otherHero));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceRuntimeSuspendedByMissionFlagOnlyCached"));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "TryFinalizeJusticePendingPoliceDeathCaptureDuringResidualMissionFlag",
                    otherHero));

                Assert.AreSame(otherHero, Game.Player.Character);
                Assert.AreEqual(1213, otherHero.Handle);
                Assert.AreEqual(310.0f, otherHero.Position.X, 0.001f);
                Assert.AreEqual(-590.0f, otherHero.Position.Y, 0.001f);
                Assert.AreEqual(JusticePhase.Wanted, ownerState.Phase);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.AreEqual(
                    0,
                    ownerState.CompletedOperationIds.Count(operation =>
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

    [TestMethod]
    public void PoliceCapture_ReloadedAdoptedCustodyRebindOutranksPersistentDeathFrontAndCompletes()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1218;
            player.Model = new Model("mp_m_freemode_01");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = false;
            player.CanRagdoll = true;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(() => true, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureReloadedAdoptedCustodyState(
                    script,
                    player,
                    player.Model.Hash);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];

                Assert.IsNull(GetField<JusticeWalRecord>(
                    script,
                    "_justicePendingDeathFrontWalRecord"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "CanRebindJusticeCustodyAdoptedRespawnIdentity",
                    player));

                Invoke(script, "RefreshJusticePreJudgmentHoldingIntent", player);
                Assert.AreEqual(
                    "PendingWalCustodyRebind",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString(),
                    "Le handoff de détention doit rester prioritaire sur le front policier persistant.");
                Assert.IsFalse((bool)Invoke(
                    script,
                    "MustBlockJusticeLateForPreJudgmentHolding",
                    player));

                Ped sameHero = Game.Player.Character;
                for (int tick = 0; tick < 80; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        // Je n'attends qu'après la première publication : les
                        // ticks de holding précédents n'ont aucune révision à flusher.
                        AwaitQueuedPersistence(script);
                    }
                    if (!profile.PendingDeathCapture &&
                        !GetField<bool>(script, "_justiceCustodyTransferPending") &&
                        !GetField<bool>(script, "_justiceCustodyResumePending") &&
                        !GetField<bool>(script, "_justiceCustodyWaitingForRespawn") &&
                        GetField<bool>(script, "_justiceCustodyContainmentEstablished"))
                    {
                        break;
                    }
                }

                Assert.AreSame(
                    sameHero,
                    Game.Player.Character,
                    "Le reload doit reprendre le même héros sans permutation artificielle.");
                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyResumePending"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyContainmentEstablished"));
                Assert.AreEqual(player.Handle, GetField<int>(script, "_justiceCustodyPlayerHandle"));
                Assert.AreEqual(player.Model.Hash, GetField<int>(script, "_justiceCustodyPlayerModelHash"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.AreEqual(
                    "None",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.AreEqual(0, Game.Player.WantedLevel);
                Assert.IsTrue(CountNative(Hash.DO_SCREEN_FADE_IN) > 0);
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

    [TestMethod]
    public void PoliceCapture_ReloadedCustodyRebindStaysBlockedByOpenPoliceDeathWal()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1219;
            player.Model = new Model("mp_m_freemode_01");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            Game.Player.WantedLevel = 3;
            ConfigureAdmissionNatives(() => true, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureReloadedAdoptedCustodyState(
                    script,
                    player,
                    player.Model.Hash);
                JusticeWalRecord openPoliceDeath = CreatePoliceDeathRecord(
                    player.Model.Hash,
                    JusticeWalState.Prepared);
                SetField(
                    script,
                    "_justicePendingDeathFrontWalRecord",
                    openPoliceDeath);

                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));
                Invoke(script, "RefreshJusticePreJudgmentHoldingIntent", player);
                Assert.AreNotEqual(
                    "PendingWalCustodyRebind",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue((bool)Invoke(
                    script,
                    "MustBlockJusticeLateForPreJudgmentHolding",
                    player));

                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeSystem");

                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyContainmentEstablished"));
                Assert.AreEqual(0, CountNative(Hash.DO_SCREEN_FADE_IN));
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

    [TestMethod]
    public void PoliceCapture_ReloadedCustodyRebindRejectsDivergentAdoptedIdentity()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            int ownerModel = new Model("mp_m_freemode_01").Hash;
            Ped otherIdentity = Game.Player.Character;
            otherIdentity.Handle = 1220;
            otherIdentity.Model = new Model("s_m_y_cop_01");
            otherIdentity.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            GTA.Math.Vector3 hospitalPosition = otherIdentity.Position;
            ConfigureAdmissionNatives(() => true, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureReloadedAdoptedCustodyState(
                    script,
                    otherIdentity,
                    ownerModel);

                Assert.IsFalse((bool)Invoke(
                    script,
                    "CanRebindJusticeCustodyAdoptedRespawnIdentity",
                    otherIdentity));
                Invoke(
                    script,
                    "RefreshJusticePreJudgmentHoldingIntent",
                    otherIdentity);
                Assert.AreNotEqual(
                    "PendingWalCustodyRebind",
                    GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
                Assert.IsTrue((bool)Invoke(
                    script,
                    "MustBlockJusticeLateForPreJudgmentHolding",
                    otherIdentity));

                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");

                Assert.AreSame(otherIdentity, Game.Player.Character);
                Assert.AreEqual(hospitalPosition.X, otherIdentity.Position.X, 0.001f);
                Assert.AreEqual(hospitalPosition.Y, otherIdentity.Position.Y, 0.001f);
                Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
                Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyPlayerHandle"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0].PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyContainmentEstablished"));
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

    [TestMethod]
    public void PoliceCapture_FullyPaidDeferredDeathCaptureClearsExactFrontWithoutCustody()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1221;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = true;
            player.CanRagdoll = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            Game.Player.Money = 900;
            ConfigureAdmissionNatives(() => false, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureFullyPaidDeathCaptureState(
                    script,
                    player);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];
                JusticeRecordState record = GetField<JusticeRecordState>(
                    script,
                    "_justiceRecordState");
                int convictionCount = record.Convictions.Count;
                int cash = Game.Player.Money;
                int cashWriteCount = 0;
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                Assert.IsTrue((bool)Invoke(
                    script,
                    "HasExactJusticePendingPoliceDeathCaptureOwner"));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));

                // Je passe par la vraie première tentative post-jugement : le
                // cadavre arme uniquement l'attente de respawn et interdit le débit.
                Invoke(script, "JusticeBeginCustodyTransfer", true);
                AwaitQueuedPersistence(script);
                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.AreEqual(0, cashWriteCount);

                // Je réveille le même héros. JusticeUpdateCustody reprend
                // alors la capture avec deathCapture=false, sans perdre le latch.
                player.IsDead = false;
                Game.Player.IsDead = false;
                Game.GameTime += 1000;
                Invoke(script, "JusticeUpdateCustody", player, Game.GameTime);
                Assert.IsNotNull(GetFieldObject(script, "_justiceFineDebitIntent"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsTrue(profile.PendingDeathCapture);
                AwaitQueuedPersistence(script);

                bool custodyWasArmed = false;
                bool holdingWasArmed = false;
                for (int tick = 0; tick < 80; tick++)
                {
                    Game.GameTime += 250;
                    Invoke(script, "UpdateJusticeEarly");
                    Invoke(script, "UpdateJusticeSystem");

                    custodyWasArmed |=
                        GetField<bool>(script, "_justiceCustodyRuntimeActive") ||
                        GetField<bool>(script, "_justiceCustodyTransferPending") ||
                        GetField<bool>(script, "_justiceCustodyResumePending");
                    holdingWasArmed |=
                        GetField<bool>(
                            script,
                            "_justicePoliceDeathPreJudgmentHoldingEstablished") ||
                        !string.Equals(
                            "None",
                            GetFieldObject(
                                script,
                                "_justicePreJudgmentHoldingSource").ToString(),
                            StringComparison.Ordinal);
                    if (GetField<long>(
                            script,
                            "_justiceLastQueuedPersistenceRevision") > 0L)
                    {
                        AwaitQueuedPersistence(script);
                    }

                    if (!GetField<bool>(
                            script,
                            "_justicePursuitDeathObservedDuringSuspension") &&
                        !profile.PendingDeathCapture &&
                        state.Phase == JusticePhase.AtLarge)
                    {
                        break;
                    }
                }

                Assert.AreSame(player, Game.Player.Character);
                Assert.AreEqual(0, cash);
                Assert.AreEqual(1, cashWriteCount);
                Assert.AreEqual(0L, state.FineDue);
                Assert.AreEqual(0, state.SentenceSeconds);
                Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(custodyWasArmed);
                Assert.IsTrue(holdingWasArmed);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyResidualMissionFlagBypassArmed"));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsInsideJusticeCustody",
                    player.Position));
                Assert.AreEqual(convictionCount, record.Convictions.Count);

                // Je laisse passer un tick supplémentaire : le front consommé
                // ne doit ni rejuger le dossier ni recréer une détention.
                Game.GameTime += 250;
                Invoke(script, "UpdateJusticeEarly");
                Invoke(script, "UpdateJusticeSystem");
                Assert.AreEqual(convictionCount, record.Convictions.Count);
                Assert.AreEqual(1, cashWriteCount);
                Assert.IsFalse(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
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

    [DataTestMethod]
    [DataRow("Owner")]
    [DataRow("Precommit")]
    public void PoliceCapture_FullyPaidDeathCaptureKeepsFrontWhenProofDiverges(
        string divergentProof)
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            StubRuntime.Reset();
            Ped player = Game.Player.Character;
            player.Handle = 1222;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(310.0f, -590.0f, 43.0f);
            player.IsDead = true;
            Game.Player.IsDead = true;
            Game.Player.WantedLevel = 3;
            Game.Player.Money = 900;
            ConfigureAdmissionNatives(() => false, null, null);

            DonJEnemySpawner script = null;
            try
            {
                script = new DonJEnemySpawner();
                JusticeCaseState state = ConfigureFullyPaidDeathCaptureState(
                    script,
                    player);
                JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
                    script,
                    "_justicePlayerProfiles")[0];
                JusticeRecordState record = GetField<JusticeRecordState>(
                    script,
                    "_justiceRecordState");
                int convictionCount = record.Convictions.Count;
                int cash = Game.Player.Money;
                int cashWriteCount = 0;
                SetField(
                    script,
                    "_justiceCashReadOverride",
                    new Func<int, int?>(slot => cash));
                SetField(
                    script,
                    "_justiceCashWriteOverride",
                    new Func<int, int, bool?>((slot, value) =>
                    {
                        cash = value;
                        Game.Player.Money = value;
                        cashWriteCount++;
                        return true;
                    }));

                player.IsDead = false;
                Game.Player.IsDead = false;
                state.FineDue = 0L;
                Assert.IsTrue(JusticePolicy.TryRegisterOperation(
                    state,
                    new JusticeOperation(
                        JusticePolicy.CreateOperationId(
                            JusticeOperationKind.ApplyFine,
                            state.CustodyEpisodeId),
                        JusticeOperationKind.ApplyFine,
                        state.CustodyEpisodeId)));
                cash = 0;
                Game.Player.Money = 0;
                if (string.Equals(divergentProof, "Owner", StringComparison.Ordinal))
                {
                    SetField(
                        script,
                        "_justiceSuspendedPursuitDeathPlayerModelHash",
                        new Model("player_one").Hash);
                }
                else
                {
                    SetField(
                        script,
                        "_justiceCapturePrecommitConfirmedEpisodeId",
                        state.CustodyEpisodeId + ":divergent");
                }

                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeRecognitionCaptureResetDurable",
                    "capture policière acquittée test"));
                // Je vise directement la frontière post-paiement : le reset
                // Recognition est durable, mais la preuve divergente reste bloquante.
                Invoke(script, "JusticeBeginCustodyTransfer", false);

                Assert.AreEqual(0, cash);
                Assert.AreEqual(0, cashWriteCount);
                Assert.AreEqual(0L, state.FineDue);
                Assert.AreEqual(0, state.SentenceSeconds);
                Assert.AreEqual(JusticePhase.Captured, state.Phase);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.IsTrue(profile.PendingDeathCapture);
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
                Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceLegalReleaseFinalizationPending"));
                Assert.AreEqual(
                    string.Equals(divergentProof, "Owner", StringComparison.Ordinal),
                    !(bool)Invoke(
                        script,
                        "HasExactJusticePendingPoliceDeathCaptureOwner"));
                Assert.AreEqual(
                    string.Equals(divergentProof, "Precommit", StringComparison.Ordinal),
                    !(bool)Invoke(
                        script,
                        "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));

                Game.GameTime += 1000;
                Invoke(script, "JusticeUpdateCustody", player, Game.GameTime);
                Assert.AreEqual(convictionCount, record.Convictions.Count);
                Assert.AreEqual(0, cashWriteCount);
                Assert.IsTrue(profile.PendingDeathCapture);
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

    [TestMethod]
    public void CustodyAdmission_WantedReappearanceRestartsFullStabilityWindow()
    {
        StubRuntime.Reset();
        try
        {
            Ped player = Game.Player.Character;
            player.Handle = 1215;
            player.Model = new Model("player_zero");
            player.Position = new GTA.Math.Vector3(
                459.86f,
                -994.38f,
                24.91f);
            player.IsDead = false;
            Game.Player.IsDead = false;
            Game.Player.WantedLevel = 0;

            object script = FormatterServices.GetUninitializedObject(ScriptType);
            JusticeCaseState state = new JusticeCaseState
            {
                Enabled = true,
                Phase = JusticePhase.Transporting,
                SentenceSeconds = 180,
                CustodyEpisodeId = "custody:admission-stability"
            };
            SetField(script, "_justiceCaseState", state);
            SetField(script, "_justiceEnabled", true);
            SetField(script, "_justiceCustodyRuntimeActive", true);
            SetField(script, "_justiceCustodyTransferPending", true);
            SetField(script, "_justiceCustodyAdmissionPositionEstablished", true);
            SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
            SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
            SetField(script, "_justiceCustodyPlayerSlot", 0);
            SetField(script, "_justiceActivePlayerProfileSlot", 0);
            SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
            SetField(
                script,
                "_justiceCustodySite",
                Enum.Parse(
                    GetFieldObject(script, "_justiceCustodySite").GetType(),
                    "MissionRow"));
            SetField(
                script,
                "_justicePoliceIntegrationMode",
                Enum.Parse(
                    GetFieldObject(script, "_justicePoliceIntegrationMode").GetType(),
                    "Disabled"));

            Assert.IsFalse((bool)Invoke(
                script,
                "TrySecureJusticeCustodyAdmission",
                player,
                100));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyAdmissionWantedStabilityStarted"));
            Assert.AreEqual(
                100,
                GetField<int>(
                    script,
                    "_justiceCustodyAdmissionWantedStableSinceAt"));

            // Je simule une réapparition policière juste avant l'ancien délai :
            // le clear réussi ne doit jamais cacher cette rupture de stabilité.
            Game.Player.WantedLevel = 3;
            Assert.IsFalse((bool)Invoke(
                script,
                "TrySecureJusticeCustodyAdmission",
                player,
                900));
            Assert.AreEqual(0, Game.Player.WantedLevel);
            Assert.AreEqual(
                900,
                GetField<int>(
                    script,
                    "_justiceCustodyAdmissionWantedStableSinceAt"));

            Assert.IsFalse(
                (bool)Invoke(
                    script,
                    "TrySecureJusticeCustodyAdmission",
                    player,
                    1100),
                "L'ancien timestamp aurait admis trop tôt après le retour des étoiles.");
            Assert.IsTrue((bool)Invoke(
                script,
                "TrySecureJusticeCustodyAdmission",
                player,
                1900));
        }
        finally
        {
            StubRuntime.Reset();
        }
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

        // Je garde ici le service volontairement indisponible : ces scénarios
        // ciblent les barrières précédant toute mutation et n'ouvrent aucun writer.
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

    private static JusticeCaseState ConfigureCapturedPoliceDeathFineResumeState(
        object script,
        Ped player,
        bool deathRebindPending,
        int sentenceSeconds = 180)
    {
        ConfigureLivePoliceDeathCase(script, player, sentenceSeconds);
        JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles");
        JusticePlayerProfileState profile = profiles[0];
        JusticeCaseState state = profile.CaseState;
        const string custodyEpisode = "custody:prepared-fine-police-death";
        state.CustodyEpisodeId = custodyEpisode;
        state.HasWarrant = false;
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.Capture,
                    custodyEpisode),
                JusticeOperationKind.Capture,
                custodyEpisode)));
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.ApplyConviction,
                    custodyEpisode),
                JusticeOperationKind.ApplyConviction,
                custodyEpisode)));
        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            state,
            profile.RecordState,
            DateTime.UtcNow));
        state.Phase = JusticePhase.Captured;

        // Je matérialise ici le snapshot exact situé après le jugement et avant
        // le débit : le front de mort appartient toujours au même profil/modèle.
        profile.PendingDeathCapture = true;
        profile.PendingDeathCapturePlayerSlot = 0;
        profile.PendingDeathCapturePlayerModel = player.Model.Hash;
        profile.LastCanonicalPlayerModel = player.Model.Hash;
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profile.RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        SetField(script, "_justicePursuitActive", false);
        SetField(script, "_justiceLastWantedLevel", Game.Player.WantedLevel);
        SetField(script, "_justiceCaptureRetryPending", false);
        SetField(script, "_justiceCaptureRetryDeath", false);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            player.Model.Hash);
        SetField(script, "_justicePendingDeathFrontWalRecord", null);
        SetField(script, "_justicePoliceDeathRespawnMaskIntentPending", true);
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyRuntimeActive", false);
        SetField(script, "_justiceCustodyTransferPending", false);
        SetField(script, "_justiceCustodyResumePending", false);
        SetField(script, "_justiceCustodyWaitingForRespawn", true);
        SetField(script, "_justiceCustodyDeathRebindPending", deathRebindPending);
        SetField(script, "_justiceCustodyRespawnIdentityRebindConfirmed", false);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));

        Invoke(script, "JusticeMarkStateDirty");
        CompleteCriticalPrecommit(script, "BeginJusticeCapture");
        Invoke(script, "ConfirmJusticeCapturePrecommit");
        Assert.IsTrue((bool)Invoke(
            script,
            "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));
        Assert.IsTrue((bool)Invoke(
            script,
            "HasExactJusticePendingPoliceDeathCaptureOwner"));
        return state;
    }

    private static object PrepareJusticeFineDebitIntent(object script)
    {
        object preparedIntent = null;
        for (int attempt = 0; attempt < 12 && preparedIntent == null; attempt++)
        {
            Invoke(script, "JusticeBeginCustodyTransfer", false);
            preparedIntent = GetFieldObject(script, "_justiceFineDebitIntent");
            if (GetField<long>(
                    script,
                    "_justiceLastQueuedPersistenceRevision") > 0L)
            {
                AwaitQueuedPersistence(script);
            }
        }

        Assert.IsNotNull(
            preparedIntent,
            "Le snapshot financier Prepared doit être créé avant le flag mission résiduel.");
        Assert.IsFalse(GetField<bool>(preparedIntent, "DebitAttempted"));
        return preparedIntent;
    }

    private static JusticeCaseState PrepareCustomPoliceDeathCaptureRetry(
        object script,
        Ped deadPlayer,
        int canonicalRespawnModel,
        Func<int> currentSlot)
    {
        ConfigureLivePoliceDeathCase(script, deadPlayer, 180);
        JusticePlayerProfileState profile = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles")[0];
        JusticeCaseState state = profile.CaseState;
        state.FineDue = 0L;
        state.Charges[0].Fine = 0L;
        profile.LastCanonicalPlayerModel = canonicalRespawnModel;
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", canonicalRespawnModel);
        SetField(script, "_justiceCanonicalPlayerSlotOverride", currentSlot);

        Assert.IsTrue((bool)Invoke(
            script,
            "TryPersistJusticePoliceDeathFrontToWal",
            deadPlayer));
        AwaitQueuedPersistence(script);
        FlushAndAwait(script);
        Assert.IsTrue((bool)Invoke(
            script,
            "IsJusticePoliceDeathFrontResultDurable"));

        // Je déclenche le vrai premier précommit sur le cadavre. Le writer peut
        // finir avant le retour dans un environnement très rapide; je normalise
        // ensuite la frontière volatile interrompue que le tick Early doit reprendre.
        Invoke(script, "BeginJusticeCapture", true);
        if (GetField<long>(
                script,
                "_justiceLastQueuedPersistenceRevision") > 0L)
        {
            AwaitQueuedPersistence(script);
        }
        Invoke(script, "ResetJusticeCapturePrecommitConfirmation");
        SetField(script, "_justiceCaptureRetryPending", true);
        SetField(script, "_justiceCaptureRetryDeath", true);
        Assert.AreEqual(JusticePhase.Captured, state.Phase);
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
        Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
        Assert.IsTrue(profile.PendingDeathCapture);
        return state;
    }

    private static void AdvanceEarlyUntilPoliceDeathTransferArmed(
        object script,
        JusticeCaseState state,
        int maximumTicks,
        int tickDurationMs)
    {
        for (int tick = 0; tick < maximumTicks; tick++)
        {
            Game.GameTime += tickDurationMs;
            Invoke(script, "UpdateJusticeEarly");
            if (GetField<long>(
                    script,
                    "_justiceLastQueuedPersistenceRevision") > 0L)
            {
                AwaitQueuedPersistence(script);
            }
            if (state.Phase == JusticePhase.Transporting &&
                GetField<bool>(script, "_justiceCustodyRuntimeActive") &&
                GetField<bool>(script, "_justiceCustodyTransferPending"))
            {
                return;
            }
        }

        Assert.AreEqual(
            JusticePhase.Transporting,
            state.Phase,
            "Early doit reprendre le précommit du cadavre et armer le transfert.");
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
    }

    private static JusticeCaseState ConfigureReloadedAdoptedCustodyState(
        object script,
        Ped player,
        int ownerModel)
    {
        ConfigureLivePoliceDeathCase(script, player, 180);
        JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles");
        JusticePlayerProfileState profile = profiles[0];
        JusticeCaseState state = profile.CaseState;
        const string custodyEpisode = "custody:reloaded-adopted-death";
        state.FineDue = 0L;
        state.Charges[0].Fine = 0L;
        state.CustodyEpisodeId = custodyEpisode;
        state.HasWarrant = false;
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.Capture,
                    custodyEpisode),
                JusticeOperationKind.Capture,
                custodyEpisode)));
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.ApplyConviction,
                    custodyEpisode),
                JusticeOperationKind.ApplyConviction,
                custodyEpisode)));
        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            state,
            profile.RecordState,
            DateTime.UtcNow));
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.ApplyFine,
                    custodyEpisode),
                JusticeOperationKind.ApplyFine,
                custodyEpisode)));
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.EnterCustody,
                    custodyEpisode),
                JusticeOperationKind.EnterCustody,
                custodyEpisode)));
        state.Phase = JusticePhase.Incarcerated;

        profile.PendingDeathCapture = true;
        profile.PendingDeathCapturePlayerSlot = 0;
        profile.PendingDeathCapturePlayerModel = ownerModel;
        profile.LastCanonicalPlayerModel = ownerModel;

        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profile.RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", ownerModel);
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        SetField(script, "_justicePursuitActive", false);
        SetField(script, "_justiceLastWantedLevel", Game.Player.WantedLevel);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            ownerModel);
        SetField(script, "_justicePendingDeathFrontWalRecord", null);

        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyTransferPending", false);
        SetField(script, "_justiceCustodyResumePending", true);
        SetField(script, "_justiceCustodyWaitingForRespawn", true);
        SetField(script, "_justiceCustodyDeathRebindPending", false);
        SetField(script, "_justiceCustodyRespawnIdentityRebindConfirmed", false);
        SetField(script, "_justiceCustodyPlayerHandle", 0);
        SetField(script, "_justiceCustodyPlayerModelHash", ownerModel);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyInitialSentenceSeconds", 180);
        SetField(script, "_justiceCustodyContainmentEstablished", false);
        SetField(script, "_justiceCustodyAdmissionPositionEstablished", false);
        SetField(script, "_justiceCustodyDeathStatePersistencePending", false);
        SetField(script, "_justiceCustodyPlayerStateStored", true);
        SetField(script, "_justiceCustodyStoredInvincible", false);
        SetField(script, "_justiceCustodyStoredFrozen", false);
        SetField(script, "_justiceCustodyStoredCanRagdoll", true);
        SetField(script, "_justiceInventoryRemoved", false);
        SetField(script, "_justiceWeaponControlsLocked", false);
        SetField(script, "_justiceDeferredInventoryRestore", false);
        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(
                GetFieldObject(script, "_justiceInventoryCustodyState").GetType(),
                "UnsupportedPreserved"));
        SetField(
            script,
            "_justiceCustodySite",
            Enum.Parse(
                GetFieldObject(script, "_justiceCustodySite").GetType(),
                "MissionRow"));
        SetField(
            script,
            "_justicePoliceIntegrationMode",
            Enum.Parse(
                GetFieldObject(script, "_justicePoliceIntegrationMode").GetType(),
                "Disabled"));
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => -1));
        return state;
    }

    private static JusticeCaseState ConfigureFullyPaidDeathCaptureState(
        object script,
        Ped player)
    {
        ConfigureLivePoliceDeathCase(script, player, 0);
        JusticePlayerProfileState[] profiles = GetField<JusticePlayerProfileState[]>(
            script,
            "_justicePlayerProfiles");
        JusticePlayerProfileState profile = profiles[0];
        JusticeCaseState state = profile.CaseState;
        const string custodyEpisode = "custody:fully-paid-police-death";
        state.SentenceSeconds = 0;
        state.Charges[0].SentenceSeconds = 0;
        state.CustodyEpisodeId = custodyEpisode;
        state.HasWarrant = false;
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.Capture,
                    custodyEpisode),
                JusticeOperationKind.Capture,
                custodyEpisode)));
        Assert.IsTrue(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.ApplyConviction,
                    custodyEpisode),
                JusticeOperationKind.ApplyConviction,
                custodyEpisode)));
        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            state,
            profile.RecordState,
            DateTime.UtcNow));
        state.Phase = JusticePhase.Captured;

        // Je rends le DeathFront de profil et son propriétaire runtime
        // strictement identiques avant de durcir le précommit du jugement.
        profile.PendingDeathCapture = true;
        profile.PendingDeathCapturePlayerSlot = 0;
        profile.PendingDeathCapturePlayerModel = player.Model.Hash;
        profile.LastCanonicalPlayerModel = player.Model.Hash;
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profile.RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        SetField(script, "_justicePursuitActive", false);
        SetField(script, "_justiceCaptureRetryPending", false);
        SetField(script, "_justiceCaptureRetryDeath", false);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetField(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            player.Model.Hash);
        SetField(script, "_justicePendingDeathFrontWalRecord", null);
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyRuntimeActive", false);
        SetField(script, "_justiceCustodyTransferPending", false);
        SetField(script, "_justiceCustodyResumePending", false);
        SetField(script, "_justiceCustodyWaitingForRespawn", false);
        SetField(script, "_justiceCustodyDeathRebindPending", false);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));

        Invoke(script, "JusticeMarkStateDirty");
        CompleteCriticalPrecommit(script, "BeginJusticeCapture");
        Invoke(script, "ConfirmJusticeCapturePrecommit");
        Assert.IsTrue((bool)Invoke(
            script,
            "HasJusticeCapturePrecommitConfirmationForCurrentEpisode"));
        Assert.IsTrue((bool)Invoke(
            script,
            "HasExactJusticePendingPoliceDeathCaptureOwner"));
        return state;
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
        for (int attempt = 0; attempt < 64; attempt++)
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

        Assert.IsFalse(
            GetField<JusticePlayerProfileState[]>(
                script,
                "_justicePlayerProfiles")[0].PendingDeathCapture,
            "Le front Confirmed doit être consommé dans le budget de retries.");
        Assert.AreEqual(
            1,
            state.CompletedOperationIds.Count(operation =>
                operation.StartsWith("Capture:", StringComparison.Ordinal)),
            "Le front Confirmed doit produire une capture unique.");
    }

    private static void AdvanceUntilIncarcerated(
        object script,
        JusticeCaseState state,
        int maximumTicks,
        int tickDurationMs)
    {
        for (int tick = 0; tick < maximumTicks; tick++)
        {
            Game.GameTime += tickDurationMs;
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
            AwaitQueuedPersistence(script);
            if (state.Phase == JusticePhase.Incarcerated &&
                !GetField<bool>(script, "_justiceCustodyTransferPending") &&
                GetField<bool>(script, "_justiceCustodyContainmentEstablished"))
            {
                return;
            }
        }

        Assert.AreEqual(
            JusticePhase.Incarcerated,
            state.Phase,
            "L'admission doit atteindre la détention dans le budget de ticks; " +
            "transfer=" +
            GetField<bool>(script, "_justiceCustodyTransferPending") +
            ", waiting=" +
            GetField<bool>(script, "_justiceCustodyWaitingForRespawn") +
            ", rebind=" +
            GetField<bool>(script, "_justiceCustodyDeathRebindPending") +
            ", fadeIn=" +
            GetField<bool>(script, "_justiceCustodyAdmissionFadeInRequested") +
            ", holding=" +
            GetFieldObject(script, "_justicePreJudgmentHoldingSource") +
            ", next=" +
            GetField<int>(script, "_justiceNextCustodyTransferAttemptAt") +
            ", now=" + Game.GameTime +
            ", erreur=" +
            (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
        Assert.IsFalse(
            GetField<bool>(script, "_justiceCustodyTransferPending"),
            "Le transfert doit être acquitté; source=" +
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString() +
            ", échecs=" +
            GetField<int>(script, "_justiceCustodyTransferFailureCount")
                .ToString(CultureInfo.InvariantCulture) +
            ", erreur=" +
            (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
        Assert.IsTrue(
            GetField<bool>(script, "_justiceCustodyContainmentEstablished"),
            "Le containment final doit être établi après le transfert acquitté.");
    }

    private static void AdvanceUntilCustodySnapshotStored(
        object script,
        JusticeCaseState state,
        int maximumTicks,
        int tickDurationMs)
    {
        for (int tick = 0; tick < maximumTicks; tick++)
        {
            Game.GameTime += tickDurationMs;
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
            AwaitQueuedPersistence(script);
            if (GetField<bool>(script, "_justiceCustodyPlayerStateStored") &&
                GetField<bool>(script, "_justiceCustodyTransferPending"))
            {
                return;
            }
        }

        Assert.IsTrue(
            GetField<bool>(script, "_justiceCustodyPlayerStateStored"),
            "Le snapshot doit être capturé sous le holding dans le budget de ticks.");
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
    }

    private static void CompleteCriticalPrecommit(object script, string caller)
    {
        bool committed = false;
        for (int attempt = 0; attempt < 8 && !committed; attempt++)
        {
            committed = (bool)Invoke(
                script,
                "PersistJusticeCriticalPrecommitRedundantly",
                caller);
            if (!committed)
            {
                AwaitQueuedPersistence(script);
            }
        }

        Assert.IsTrue(committed, "Le précommit critique doit devenir durable.");
    }

    private static int AdvanceUntilAdmissionFadeInRequested(
        object script,
        Func<int> fadeInRequestCount,
        int expectedRequestCount,
        int invalidatedAt,
        int maximumTicks,
        int tickDurationMs)
    {
        int previousRequestCount = fadeInRequestCount();
        for (int tick = 0; tick < maximumTicks; tick++)
        {
            Game.GameTime += tickDurationMs;
            Invoke(script, "UpdateJusticeEarly");
            Invoke(script, "UpdateJusticeSystem");
            AwaitQueuedPersistence(script);

            int currentRequestCount = fadeInRequestCount();
            if (currentRequestCount == previousRequestCount)
            {
                continue;
            }

            Assert.AreEqual(expectedRequestCount, currentRequestCount);
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyAdmissionWantedStabilityStarted"));
            int stableSinceAt = GetField<int>(
                script,
                "_justiceCustodyAdmissionWantedStableSinceAt");
            Assert.IsTrue(
                stableSinceAt > invalidatedAt,
                "Le nouveau FadeIn ne doit jamais reutiliser le timestamp invalide.");
            Assert.IsTrue(
                unchecked((uint)(Game.GameTime - stableSinceAt)) >= 1000U,
                "Le nouveau FadeIn exige une seconde wanted zero continue.");
            return stableSinceAt;
        }

        Assert.AreEqual(
            expectedRequestCount,
            fadeInRequestCount(),
            "Le FadeIn attendu doit etre demande dans le budget; now=" +
            Game.GameTime.ToString(CultureInfo.InvariantCulture) +
            ", stableSince=" +
            GetField<int>(script, "_justiceCustodyAdmissionWantedStableSinceAt")
                .ToString(CultureInfo.InvariantCulture) +
            ", next=" +
            GetField<int>(script, "_justiceNextCustodyTransferAttemptAt")
                .ToString(CultureInfo.InvariantCulture) +
            ", failureCount=" +
            GetField<int>(script, "_justiceCustodyTransferFailureCount")
                .ToString(CultureInfo.InvariantCulture) +
            ", source=" +
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        return GetField<int>(
            script,
            "_justiceCustodyAdmissionWantedStableSinceAt");
    }

    private static string ReadJusticeSource(string fileName)
    {
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null &&
               !File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
        {
            current = current.Parent;
        }

        Assert.IsNotNull(current, "La racine du depot doit etre accessible au test.");
        return File.ReadAllText(Path.Combine(
            current.FullName,
            "src",
            "DonJEnemySpawner",
            fileName));
    }

    private static string ExtractPrivateMethodSource(
        string source,
        string methodName)
    {
        string marker = methodName + "(";
        int methodNameAt = -1;
        int searchAt = 0;
        while (searchAt < source.Length)
        {
            int candidate = source.IndexOf(
                marker,
                searchAt,
                StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string declaration = source.Substring(
                lineStart,
                candidate - lineStart);
            if (declaration.Contains("private "))
            {
                methodNameAt = candidate;
                break;
            }

            searchAt = candidate + marker.Length;
        }

        Assert.IsTrue(methodNameAt >= 0, "Methode source introuvable : " + methodName);
        int openingBrace = source.IndexOf('{', methodNameAt);
        Assert.IsTrue(openingBrace >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
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
                    return source.Substring(
                        openingBrace,
                        index - openingBrace + 1);
                }
            }
        }

        Assert.IsTrue(false, "Corps source non ferme : " + methodName);
        return string.Empty;
    }

    private static void AssertSourceOrder(string source, params string[] markers)
    {
        int previous = -1;
        foreach (string marker in markers)
        {
            int current = source.IndexOf(
                marker,
                previous + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                current > previous,
                "Ordre source invalide ou marqueur absent : " + marker);
            previous = current;
        }
    }

    private static void ConfigureAdmissionNatives(
        Func<bool> missionFlag,
        string strongSuspension,
        ICollection<int> wantedObservedAtFadeIn,
        Func<bool> rejectFadeIn = null,
        Func<bool> streamingReady = null,
        Func<bool> rejectFadeOutState = null,
        Func<bool> playerBeingArrested = null,
        Func<bool> screenFadedIn = null,
        Func<bool> screenFadingIn = null)
    {
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == GroundReadyNative || hash == CollisionReadyNative)
            {
                return streamingReady == null || streamingReady();
            }
            if ((hash == ScreenFadedOutNative ||
                 hash == ScreenFadingOutNative) &&
                rejectFadeOutState != null && rejectFadeOutState())
            {
                return false;
            }
            if (hash == ScreenFadedInNative && screenFadedIn != null)
            {
                return screenFadedIn();
            }
            if (hash == ScreenFadingInNative && screenFadingIn != null)
            {
                return screenFadingIn();
            }
            if (hash == LoadingScreenNative)
            {
                return string.Equals(
                    strongSuspension,
                    "Loading",
                    StringComparison.Ordinal);
            }
            if (hash == CutsceneNative)
            {
                return string.Equals(
                    strongSuspension,
                    "Cutscene",
                    StringComparison.Ordinal);
            }
            if (hash == PlayerSwitchNative)
            {
                return string.Equals(
                    strongSuspension,
                    "PlayerSwitch",
                    StringComparison.Ordinal);
            }
            if (hash == PlayerBeingArrestedNative)
            {
                return playerBeingArrested != null && playerBeingArrested();
            }
            if (hash == MissionFlagNative)
            {
                if (string.Equals(
                        strongSuspension,
                        "MissionNativeFailure",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Panne native mission simulée.");
                }
                return missionFlag != null && missionFlag();
            }
            if (hash == (ulong)Hash.DO_SCREEN_FADE_IN)
            {
                if (wantedObservedAtFadeIn != null)
                {
                    wantedObservedAtFadeIn.Add(Game.Player.WantedLevel);
                }
                if (rejectFadeIn != null && rejectFadeIn())
                {
                    throw new InvalidOperationException(
                        "Fondu entrant refusé par le stub.");
                }
            }
            return null;
        };
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
            SeedCanonicalJusticeState(directory);
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

    private static void SeedCanonicalJusticeState(string directory)
    {
        string emptyCustody = (string)InvokeStatic(
            "CreateCanonicalEmptyJusticeCustodyXml");
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>();
        for (int slot = 0; slot < 3; slot++)
        {
            profiles.Add(new JusticePersistenceProfileSnapshot(
                slot,
                1L,
                "slot:" + slot.ToString(CultureInfo.InvariantCulture) +
                ":model:0",
                new[]
                {
                    new JusticePersistenceField("pendingDeathCapture", "false"),
                    new JusticePersistenceField(
                        "pendingDeathCapturePlayerSlot",
                        "-1"),
                    new JusticePersistenceField(
                        "pendingDeathCapturePlayerModel",
                        "0"),
                    new JusticePersistenceField(
                        "pendingAmnestyWantedClear",
                        "false"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseFinalization",
                        "false"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseSite",
                        "0"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseSelectedWeapon",
                        "0"),
                    new JusticePersistenceField(
                        "lastCanonicalPlayerModel",
                        "0"),
                    new JusticePersistenceField(
                        "Case",
                        "<Case enabled=\"false\" />"),
                    new JusticePersistenceField("Record", "<Record />"),
                    new JusticePersistenceField("Custody", emptyCustody)
                }));
        }

        JusticePersistenceSnapshot snapshot = new JusticePersistenceSnapshot(
            1L,
            JusticeXmlPersistenceCodec.SchemaMajor,
            DateTime.UtcNow.Ticks,
            0,
            new[]
            {
                new JusticePersistenceField("activePlayerSlot", "0"),
                new JusticePersistenceField("sentencePolicyVersion", "2"),
                new JusticePersistenceField("policyResetRecoveryMask", "0"),
                new JusticePersistenceField("nextIdentityGeneration", "0"),
                new JusticePersistenceField("policeIntegrationMode", "1"),
                new JusticePersistenceField("lastCanonicalPlayerSlot", "0"),
                new JusticePersistenceField("lastCanonicalPlayerModel", "0")
            },
            profiles);
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        byte[] document = codec.Serialize(snapshot);
        JusticePersistenceSnapshot decoded;
        string decodeError;
        Assert.IsTrue(
            codec.TryDeserialize(document, out decoded, out decodeError),
            decodeError);
        string semanticError;
        Assert.IsTrue(
            DonJEnemySpawner.TryValidateJusticePersistenceSnapshotSemantics(
                decoded,
                out semanticError),
            semanticError);
        string primaryPath = Path.Combine(directory, "_justice_state.xml");
        File.WriteAllBytes(primaryPath, document);
        // Je fournis aussi la copie redondante attendue par le contrat v2 : le
        // scénario ne doit pas démarrer dans une réparation de policy étrangère.
        File.WriteAllBytes(primaryPath + ".bak", document);
    }

    private static void AwaitQueuedPersistence(object script)
    {
        bool persisted = (bool)Invoke(
            script,
            "JusticeAwaitQueuedPersistenceForTests");
        JusticeRepository repository = GetField<JusticeRepository>(
            script,
            "_justiceRepository");
        JusticeRepositoryDiagnostics diagnostics = repository == null
            ? null
            : repository.GetDiagnostics();
        Assert.IsTrue(
            persisted,
            "Le repository doit confirmer la révision avant de poursuivre le scénario. " +
            "Révision demandée=" +
            GetField<long>(script, "_justiceLastQueuedPersistenceRevision")
                .ToString(CultureInfo.InvariantCulture) +
            ", mémoire=" +
            (diagnostics == null
                ? "indisponible"
                : diagnostics.MemoryRevision.ToString(CultureInfo.InvariantCulture)) +
            ", disque=" +
            (diagnostics == null
                ? "indisponible"
                : diagnostics.DiskRevision.ToString(CultureInfo.InvariantCulture)) +
            ", erreur=" +
            (GetField<string>(script, "_justicePersistenceLastError") ?? string.Empty));
    }

    private static XElement GetPersistedActiveJusticeProfile(XDocument document)
    {
        Assert.IsNotNull(document);
        Assert.IsNotNull(document.Root);
        XElement profiles = document.Root.Element("Profiles");
        if (profiles == null)
        {
            return document.Root;
        }

        XElement recovery = document.Root.Element("RuntimeRecovery");
        Assert.IsNotNull(recovery);
        string activeSlot = (string)recovery.Attribute("activePlayerSlot");
        XElement profile = profiles.Elements("Profile").SingleOrDefault(
            candidate => string.Equals(
                (string)candidate.Attribute("slot"),
                activeSlot,
                StringComparison.Ordinal));
        Assert.IsNotNull(profile, "Le profil v2 actif doit rester l'unique autorit\u00e9.");
        return profile;
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
