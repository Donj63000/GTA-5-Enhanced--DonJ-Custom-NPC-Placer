using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticePlayerProfilePersistenceTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void PlayerProfiles_RoundTripKeepsCaseRecordAndActivationIsolated()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            XDocument document = XDocument.Load(path);
            XElement[] serialized = document.Root
                .Element("Profiles")
                .Elements("Profile")
                .ToArray();

            Assert.AreEqual("2", (string)document.Root.Attribute("schemaMajor"));
            Assert.AreEqual("0", (string)document.Root.Attribute("schemaMinor"));
            Assert.AreEqual(3, serialized.Length);
            CollectionAssert.AreEqual(
                new[] { "2", "5", "10" },
                serialized
                    .Select(profile => (string)profile.Element("Record").Attribute("recidivism"))
                    .ToArray());
            Assert.AreEqual(
                "0",
                (string)document.Root.Element("RuntimeRecovery").Attribute("activePlayerSlot"));
            Assert.AreEqual("0", (string)GetPersistedActiveJusticeProfile(document).Attribute("slot"));
            Assert.IsNull(
                document.Root.Element("Case"),
                "Le schéma v2 ne doit jamais dupliquer le profil actif à la racine.");

            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 2));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));
            Assert.AreEqual(2, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.AreEqual(10, GetField<JusticeRecordState>(reader, "_justiceRecordState").RecidivismIndex);
            Assert.AreEqual(
                "Profil Trevor",
                GetField<JusticeCaseState>(reader, "_justiceCaseState").LastCrimeLabel);

            JusticePlayerProfileState[] loaded =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            loaded[2].RecordState.RecidivismIndex = 99;
            Assert.AreEqual(2, loaded[0].RecordState.RecidivismIndex);
            Assert.AreEqual(5, loaded[1].RecordState.RecidivismIndex);
            Assert.IsFalse(loaded[0].CaseState.Enabled);
            Assert.IsTrue(loaded[1].CaseState.Enabled);
        });
    }

    [TestMethod]
    public void PlayerProfiles_IncarceratedHeroCanSwitchAndKeepsServingAnIsolatedSentence()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            SetField(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetField(script, "_justiceInventoryRemoved", true);
            SetPrivateEnumField(script, "_justiceInventoryCustodyState", "RemovedVerified");
            SetField(script, "_justiceWeaponControlsLocked", true);
            SetField(script, "_justiceCustodyPlayerStateStored", true);
            SetField(script, "_justiceCustodyStoredInvincible", false);
            SetField(script, "_justiceCustodyStoredFrozen", false);
            SetField(script, "_justiceCustodyStoredCanRagdoll", true);
            SetField(script, "_justiceReleaseSelectedWeaponHash", 12345);
            AssertCurrentCustodyFragmentIsValid(script, profiles[0]);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));

            SwitchProfileAndAwait(script);
            Assert.AreEqual(1, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
            Assert.AreSame(profiles[1].CaseState, GetField<JusticeCaseState>(script, "_justiceCaseState"));
            Assert.AreSame(profiles[1].RecordState, GetField<JusticeRecordState>(script, "_justiceRecordState"));
            Assert.AreEqual(600, profiles[0].CaseState.SentenceSeconds);
            Assert.IsTrue(profiles[0].CanAdvanceCustodyInBackground);
            JusticeCustodyPersistenceSnapshot parkedCustody =
                RequireTypedCustodySnapshot(profiles[0]);
            Assert.IsTrue(parkedCustody.Active);
            Assert.IsTrue(parkedCustody.InventoryRemoved);
            Assert.IsTrue(parkedCustody.PlayerStateStored);
            Assert.IsNotNull(parkedCustody.InventorySnapshot);
            Assert.IsNull(GetField<object>(script, "_justiceWeaponSnapshot"));
            Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
            Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyPlayerStateStored"));

            string path = Path.Combine(directory, "_justice_state.xml");
            Assert.IsTrue(File.Exists(path));
            object reader = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(reader);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));

            JusticePlayerProfileState[] loaded =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(2, loaded[0].RecordState.RecidivismIndex);
            Assert.AreEqual(5, loaded[1].RecordState.RecidivismIndex);
            Assert.IsTrue(loaded[0].CanAdvanceCustodyInBackground);
            Assert.AreSame(loaded[1].CaseState, GetField<JusticeCaseState>(reader, "_justiceCaseState"));

            Invoke(reader, "AdvanceJusticeInactiveCustodyProfiles", 1000, false);
            Invoke(reader, "AdvanceJusticeInactiveCustodyProfiles", 2500, false);
            Assert.AreEqual(599, loaded[0].CaseState.SentenceSeconds);
            Assert.AreEqual(
                0,
                loaded[1].CaseState.SentenceSeconds,
                "Le dossier joué ne doit jamais recevoir la peine de Michael.");

            // Je ferme ici l'intervalle simulé : le prochain switch headless ne
            // doit pas interpréter GameTime=0 comme un second wrap artificiel.
            loaded[0].InactiveCustodyLastTickAt = 0;
            loaded[0].InactiveCustodyElapsedRemainderMs = 0;
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SwitchProfileAndAwait(reader);
            Assert.AreEqual(0, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.AreSame(loaded[0].CaseState, GetField<JusticeCaseState>(reader, "_justiceCaseState"));
            Assert.AreSame(loaded[0].RecordState, GetField<JusticeRecordState>(reader, "_justiceRecordState"));
            Assert.AreEqual(599, loaded[0].CaseState.SentenceSeconds);
            Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyRuntimeActive"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyResumePending"));
            Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyTransferPending"));
            Assert.AreEqual(0, GetField<int>(reader, "_justiceCustodyPlayerSlot"));
            Assert.IsNotNull(GetField<object>(reader, "_justiceWeaponSnapshot"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceInventoryRemoved"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceWeaponControlsLocked"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyPlayerStateStored"));
            Assert.IsFalse(loaded[0].CanAdvanceCustodyInBackground);
            Assert.AreEqual(0, loaded[0].InactiveCustodyLastTickAt);
            Assert.AreEqual(0, loaded[0].InactiveCustodyElapsedRemainderMs);
        });
    }

    [TestMethod]
    public void PlayerProfiles_ZeroOffscreenSentenceWaitsForTheReturningInmate()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            SetField(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetField(script, "_justiceInventoryRemoved", true);
            SetPrivateEnumField(script, "_justiceInventoryCustodyState", "RemovedVerified");
            SetField(script, "_justiceWeaponControlsLocked", true);
            SetField(script, "_justiceCustodyPlayerStateStored", true);
            SetField(script, "_justiceCustodyStoredCanRagdoll", true);
            AssertCurrentCustodyFragmentIsValid(script, profiles[0]);

            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            SwitchProfileAndAwait(script);

            profiles[0].CaseState.SentenceSeconds = 1;
            profiles[0].CanAdvanceCustodyInBackground = true;
            profiles[0].InactiveCustodyLastTickAt = 1000;
            Invoke(script, "AdvanceJusticeInactiveCustodyProfiles", 2500, false);

            Assert.AreEqual(0, profiles[0].CaseState.SentenceSeconds);
            Assert.AreEqual(JusticePhase.Incarcerated, profiles[0].CaseState.Phase);
            Assert.IsFalse(profiles[0].CanAdvanceCustodyInBackground);
            Assert.IsTrue(RequireTypedCustodySnapshot(profiles[0]).Active);

            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SwitchProfileAndAwait(script);

            Assert.AreEqual(0, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
            Assert.AreSame(profiles[0].CaseState, GetField<JusticeCaseState>(script, "_justiceCaseState"));
            Assert.AreEqual(0, GetField<JusticeCaseState>(script, "_justiceCaseState").SentenceSeconds);
            Assert.AreEqual(JusticePhase.Incarcerated, GetField<JusticeCaseState>(script, "_justiceCaseState").Phase);
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyResumePending"));
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyTransferPending"));
            Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyPlayerSlot"));
            Assert.IsNotNull(GetField<object>(script, "_justiceWeaponSnapshot"));
            Assert.IsTrue(GetField<bool>(script, "_justiceInventoryRemoved"));
            Assert.IsTrue(GetField<bool>(script, "_justiceWeaponControlsLocked"));
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyPlayerStateStored"));
        });
    }

    [TestMethod]
    public void PlayerProfiles_UnknownStartupSlotCanParkAResumeWhenAnotherHeroAppears()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(writer);
            ConfigureIncarceratedRuntime(writer, profiles[0]);
            SetField(writer, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetField(writer, "_justiceInventoryRemoved", true);
            SetPrivateEnumField(writer, "_justiceInventoryCustodyState", "RemovedVerified");
            SetField(writer, "_justiceWeaponControlsLocked", true);
            SetField(writer, "_justiceCustodyPlayerStateStored", true);
            SetField(writer, "_justiceCustodyStoredCanRagdoll", true);
            GetField<Dictionary<string, int>>(writer, "_justiceActivityCooldownUntil")
                ["prison_travail"] = 60000;
            AssertCurrentCustodyFragmentIsValid(writer, profiles[0]);
            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            object reader = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(reader);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => -1));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));
            Assert.AreEqual(0, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyRuntimeActive"));
            Assert.IsTrue(GetField<bool>(reader, "_justiceCustodyResumePending"));
            Assert.AreEqual(
                1,
                GetField<Dictionary<string, int>>(
                    reader,
                    "_justiceLoadedActivityCooldownSeconds").Count);

            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            SwitchProfileAndAwait(reader);

            JusticePlayerProfileState[] loaded =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(1, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.AreSame(loaded[1].CaseState, GetField<JusticeCaseState>(reader, "_justiceCaseState"));
            Assert.IsTrue(loaded[0].CanAdvanceCustodyInBackground);
            Assert.IsTrue(
                RequireTypedCustodySnapshot(loaded[0]).Cooldowns.Any(
                    cooldown => string.Equals(
                        cooldown.Id,
                        "prison_travail",
                        StringComparison.Ordinal)));
            Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyRuntimeActive"));
            Assert.IsFalse(GetField<bool>(reader, "_justiceCustodyResumePending"));
            Assert.IsNull(GetField<object>(reader, "_justiceWeaponSnapshot"));
            Assert.IsFalse(GetField<bool>(reader, "_justiceInventoryRemoved"));
        });
    }

#if DONJ_STUB_API
    [TestMethod]
    public void PlayerProfiles_InactivePoliceTokensAreRestoredAndClearedAfterCrash()
    {
        GTA.StubRuntime.Reset();
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(writer);
            ConfigureIncarceratedRuntime(writer, profiles[0]);
            SetField(writer, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetField(writer, "_justiceInventoryRemoved", true);
            SetPrivateEnumField(writer, "_justiceInventoryCustodyState", "RemovedVerified");
            SetField(writer, "_justiceWeaponControlsLocked", true);
            SetField(writer, "_justiceCustodyPlayerStateStored", true);
            SetField(writer, "_justiceCustodyStoredCanRagdoll", true);
            SetField(writer, "_justicePoliceIgnoreApplied", true);
            SetField(writer, "_justicePoliceDispatchDisabled", true);
            SetField(writer, "_justicePoliceSuppressionActive", true);
            AssertCurrentCustodyFragmentIsValid(writer, profiles[0]);
            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            object reader = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(reader);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));

            JusticePlayerProfileState[] loaded =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(1, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.IsTrue(GetField<bool>(reader, "_justicePoliceIgnoreApplied"));
            Assert.IsTrue(GetField<bool>(reader, "_justicePoliceDispatchDisabled"));
            Assert.IsTrue(GetField<bool>(reader, "_justicePoliceSuppressionRestorePending"));
            Assert.IsTrue(
                RequireTypedCustodySnapshot(loaded[0]).PoliceSuppressionApplied);

            int removeAllBefore = GTA.Game.Player.Character.Weapons.RemoveAllCount;
            Invoke(reader, "SetJusticeCustodyPoliceSuppression", false);

            Assert.IsTrue(
                GetField<bool>(reader, "_justicePoliceSuppressionRestorePending"),
                "Le premier passage doit garder les jetons jusqu'à la confirmation disque.");
            AwaitQueuedPersistence(reader);
            Invoke(reader, "SetJusticeCustodyPoliceSuppression", false);

            Assert.IsFalse(GetField<bool>(reader, "_justicePoliceIgnoreApplied"));
            Assert.IsFalse(GetField<bool>(reader, "_justicePoliceDispatchDisabled"));
            Assert.IsFalse(GetField<bool>(reader, "_justicePoliceSuppressionActive"));
            Assert.IsFalse(GetField<bool>(reader, "_justicePoliceSuppressionRestorePending"));
            JusticeCustodyPersistenceSnapshot restoredCustody =
                RequireTypedCustodySnapshot(loaded[0]);
            Assert.IsFalse(restoredCustody.PoliceSuppressionApplied);
            Assert.IsFalse(restoredCustody.PoliceDispatchDisabled);
            Assert.AreEqual(
                removeAllBefore,
                GTA.Game.Player.Character.Weapons.RemoveAllCount,
                "La récupération globale police ne doit jamais toucher l'inventaire du héros joué.");

            ulong ignoreHash = (ulong)ScriptType
                .GetField("JusticeNativeSetPoliceIgnorePlayer", PrivateStatic)
                .GetRawConstantValue();
            ulong dispatchHash = (ulong)ScriptType
                .GetField("JusticeNativeSetDispatchCopsForPlayer", PrivateStatic)
                .GetRawConstantValue();
            Assert.IsTrue(GTA.StubRuntime.NativeCalls.Any(call => call.Hash == ignoreHash));
            Assert.IsTrue(GTA.StubRuntime.NativeCalls.Any(call => call.Hash == dispatchHash));
        });
    }
#endif

    [TestMethod]
    public void PlayerProfiles_InactiveCustodyClockHandlesPauseRemainderWrapAndZero()
    {
        MethodInfo clock = ScriptType.GetMethod(
            "AdvanceJusticeInactiveCustodySentenceClock",
            PrivateStatic);
        Assert.IsNotNull(clock);

        object[] first = { 3, 1000, 0, 0, false };
        Assert.AreEqual(3, clock.Invoke(null, first));
        Assert.AreEqual(1000, first[2]);
        Assert.AreEqual(0, first[3]);

        object[] partial = { 3, 1600, first[2], first[3], false };
        Assert.AreEqual(3, clock.Invoke(null, partial));
        Assert.AreEqual(600, partial[3]);

        object[] carried = { 3, 2200, partial[2], partial[3], false };
        Assert.AreEqual(2, clock.Invoke(null, carried));
        Assert.AreEqual(200, carried[3]);

        object[] paused = { 2, 9000, carried[2], carried[3], true };
        Assert.AreEqual(2, clock.Invoke(null, paused));
        Assert.AreEqual(9000, paused[2]);
        Assert.AreEqual(0, paused[3]);

        object[] cappedAtZero = { 2, 14000, paused[2], paused[3], false };
        Assert.AreEqual(0, clock.Invoke(null, cappedAtZero));

        object[] wrapped =
        {
            2,
            unchecked(int.MinValue + 499),
            int.MaxValue - 500,
            0,
            false
        };
        Assert.AreEqual(1, clock.Invoke(null, wrapped));
        Assert.AreEqual(0, wrapped[3]);
    }

    [TestMethod]
    public void PlayerProfiles_BackgroundClockMutatesOnlyStableInactiveIncarceration()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            profiles[slot].CaseState.Enabled = true;
            profiles[slot].CaseState.SentenceSeconds = 10;
            profiles[slot].CanAdvanceCustodyInBackground = true;
            profiles[slot].InactiveCustodyLastTickAt = 1000;
        }
        profiles[0].CaseState.Phase = JusticePhase.Incarcerated;
        profiles[1].CaseState.Phase = JusticePhase.Incarcerated;
        profiles[2].CaseState.Phase = JusticePhase.Transporting;

        object script = CreateHeadlessScript(profiles, 1);
        Invoke(script, "AdvanceJusticeInactiveCustodyProfiles", 2500, false);

        Assert.AreEqual(9, profiles[0].CaseState.SentenceSeconds);
        Assert.AreEqual(10, profiles[1].CaseState.SentenceSeconds);
        Assert.AreEqual(10, profiles[2].CaseState.SentenceSeconds);
        Assert.AreEqual(0, profiles[1].InactiveCustodyLastTickAt);
        Assert.AreEqual(0, profiles[2].InactiveCustodyLastTickAt);

        profiles[0].PendingLegalReleaseFinalization = true;
        Invoke(script, "AdvanceJusticeInactiveCustodyProfiles", 4500, false);
        Assert.AreEqual(9, profiles[0].CaseState.SentenceSeconds);
        Assert.AreEqual(0, profiles[0].InactiveCustodyLastTickAt);
    }

    [TestMethod]
    public void PlayerProfiles_ProfileChangeCannotCarryAnAmnestyWantedRetryToAnotherHero()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        profiles[0].PendingAmnestyWantedClear = true;
        object script = CreateHeadlessScript(profiles, 0);
        InitializeProfileResetRuntimeCollections(script);
        SetField(script, "_justiceAmnestyPending", true);
        SetField(script, "_justiceWantedClearPending", true);
        SetField(script, "_justiceNextWantedClearRetryAtMs", 1200L);
        SetField(script, "_justiceWantedClearRetryUntilMs", 9000L);
        Type actionType = ScriptType.GetNestedType("MainMenuAction", BindingFlags.NonPublic);
        Assert.IsNotNull(actionType);
        Invoke(script, "RequestDangerConfirmation", Enum.Parse(actionType, "JusticeEnabled"));
        Assert.IsNotNull(GetField<object>(script, "_pendingDangerAction"));

        Invoke(script, "ResetJusticeRuntimeFrontsForProfileChange");

        Assert.IsFalse(GetField<bool>(script, "_justiceWantedClearPending"));
        Assert.AreEqual(0L, GetField<long>(script, "_justiceNextWantedClearRetryAtMs"));
        Assert.AreEqual(0L, GetField<long>(script, "_justiceWantedClearRetryUntilMs"));
        Assert.IsNull(
            GetField<object>(script, "_pendingDangerAction"),
            "Le second Entrée ne doit jamais exécuter l'amnistie sur le héros nouvellement actif.");
        Assert.IsTrue(
            GetField<bool>(script, "_justiceAmnestyPending"),
            "Le cache global est annulé, mais l'intention durable du profil reste reprenable.");
        Assert.IsTrue(profiles[0].PendingAmnestyWantedClear);
    }

    [TestMethod]
    public void PlayerProfiles_ProfileChangeClearsCleanTimeAndPursuitTimestamps()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceLastCleanAdvanceAtMs", 12000L);
            SetField(script, "_justiceCleanCarryMilliseconds", 875L);
            SetField(script, "_justiceWantedEpisodeStartedAtMs", 6400L);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));

            SwitchProfileAndAwait(script);

            Assert.AreEqual(1, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceLastCleanAdvanceAtMs"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCleanCarryMilliseconds"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceWantedEpisodeStartedAtMs"));
            Assert.AreEqual(
                0,
                profiles[1].RecordState.CleanGameplaySeconds,
                "Franklin ne doit recevoir ni reste de seconde propre ni timestamp de Michael.");
        });
    }

    [TestMethod]
    public void PlayerProfiles_ProfileRecoveryGuardCoversEveryDurableWal()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        JusticePlayerProfileState profile = profiles[1];
        object script = CreateHeadlessScript(profiles, 0);
        SetField(script, "_justiceMenuSelectedProfileSlot", 1);

        profile.PendingDeathCapture = true;
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        Invoke(script, "RequestJusticeSelectedProfileReset");
        Assert.IsNull(GetField<object>(script, "_pendingDangerAction"));
        profile.PendingDeathCapture = false;

        profile.PendingAmnestyWantedClear = true;
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        Invoke(script, "RequestJusticeSelectedProfileReset");
        Assert.IsNull(GetField<object>(script, "_pendingDangerAction"));
        profile.PendingAmnestyWantedClear = false;

        profile.PendingLegalReleaseFinalization = true;
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        profile.PendingLegalReleaseFinalization = false;

        profile.CaseState.EscapeWantedMinimumPending = true;
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        profile.CaseState.EscapeWantedMinimumPending = false;

        profile.CaseState.EscapeWantedMinimumAttempted = true;
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        profile.CaseState.EscapeWantedMinimumAttempted = false;

        string resetOperation = JusticePolicy.CreateOperationId(
            JusticeOperationKind.ResetProfile,
            "profile-recovery-guard");
        profile.CaseState.CompletedOperationIds.Add(resetOperation);
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));
        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        profile.CaseState.CompletedOperationIds.Remove(resetOperation);

        Assert.IsFalse((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));

        SetField(script, "_justiceMenuSelectedProfileSlot", 0);
        SetField(script, "_justicePursuitDeathObservedDuringSuspension", true);
        Invoke(script, "RequestJusticeSelectedProfileReset");
        Assert.IsNull(
            GetField<object>(script, "_pendingDangerAction"),
            "Le WAL de capture du héros joué doit bloquer la modale avant confirmation.");
    }

    [TestMethod]
    public void PlayerProfiles_PoliceSuppressionRestoreBlocksSwitchAndProfileResetUntilRecovered()
    {
        JusticePlayerProfileState profile = new JusticePlayerProfileState(0)
        {
            CustodyXml =
                "<Custody active=\"false\" site=\"None\" " +
                "policeSuppressionApplied=\"true\" policeDispatchDisabled=\"false\" />"
        };
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));

        profile.CustodyXml =
            "<Custody active=\"false\" site=\"None\" " +
            "policeSuppressionApplied=\"false\" policeDispatchDisabled=\"true\" />";
        Assert.IsTrue((bool)InvokeStatic("HasJusticeProfileCustodyRecovery", profile));

        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        object script = CreateHeadlessScript(profiles, 0);
        SetField(script, "_justicePoliceSuppressionActive", true);
        SetField(script, "_justicePoliceIgnoreApplied", true);
        SetField(script, "_justicePoliceSuppressionRestorePending", true);
        Assert.IsTrue((bool)Invoke(script, "HasJusticeCustodyRecoveryState"));

        SetField(script, "_justicePoliceSuppressionActive", false);
        SetField(script, "_justicePoliceIgnoreApplied", false);
        SetField(script, "_justicePoliceDispatchDisabled", false);
        SetField(script, "_justicePoliceSuppressionRestorePending", false);
        Assert.IsFalse((bool)Invoke(script, "HasJusticeCustodyRecoveryState"));
    }

    [TestMethod]
    public void PlayerProfiles_LegacyV1MigratesOnlyToTheProvenCanonicalSlot()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 1);
            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");

            XDocument legacy = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            legacy.Root.Element("PlayerProfiles").Remove();
            legacy.Root.Attribute("activePlayerSlot").Remove();
            Invoke(writer, "ShutdownJusticePersistenceServices");
            legacy.Save(path);

            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));

            JusticePlayerProfileState[] migrated =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(0, migrated[0].RecordState.RecidivismIndex);
            Assert.AreEqual(5, migrated[1].RecordState.RecidivismIndex);
            Assert.AreEqual(0, migrated[2].RecordState.RecidivismIndex);
            Assert.AreEqual("Profil Franklin", migrated[1].CaseState.LastCrimeLabel);

            FlushAndAwait(reader);
            XDocument migratedXml = XDocument.Load(path);
            Assert.AreEqual(
                3,
                migratedXml.Root.Element("Profiles").Elements("Profile").Count());
            Assert.AreEqual(
                "1",
                (string)migratedXml.Root.Element("RuntimeRecovery").Attribute("activePlayerSlot"));
            Assert.IsTrue(
                File.Exists(Path.Combine(directory, "_justice_state.v1.bak")),
                "La migration doit conserver l'original v1 avant le premier remplacement v2.");
        });
    }

    [TestMethod]
    public void PlayerProfiles_LegacyWithoutProofIsNeverAdoptedOrOverwritten()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            object writer = CreateHeadlessScript(CreateDistinctProfiles(), 1);
            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");

            XDocument legacy = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            legacy.Root.Element("PlayerProfiles").Remove();
            legacy.Root.Attribute("activePlayerSlot").Remove();
            legacy.Root.SetAttributeValue("lastCanonicalPlayerSlot", "-1");
            legacy.Root.SetAttributeValue("lastCanonicalPlayerModel", "0");
            Invoke(writer, "ShutdownJusticePersistenceServices");
            legacy.Save(path);
            string original = File.ReadAllText(path);

            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceActivePlayerProfileSlot", -1);
            SetField(reader, "_justiceProfileSelectionPending", true);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => -1));

            Assert.IsFalse((bool)Invoke(reader, "TryReadJusticeStateFile", path));
            Assert.IsTrue(GetField<bool>(reader, "_justiceLegacyProfileReloadPending"));
            Assert.IsFalse((bool)Invoke(reader, "JusticeFlushStateNow"));
            Assert.AreEqual(original, File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void PlayerProfiles_ResetRefusesRecoverableInventoryAndDoesNotTouchOtherHero()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        profiles[1].CustodyXml =
            "<Custody active=\"false\" site=\"None\" playerSlot=\"1\">" +
            "<InventorySnapshot validated=\"true\" selectedWeapon=\"1\" />" +
            "</Custody>";
        object script = CreateHeadlessScript(profiles, 0);

        Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        Assert.AreEqual(5, profiles[1].RecordState.RecidivismIndex);
        Assert.AreEqual(2, profiles[0].RecordState.RecidivismIndex);

        profiles[1].CustodyXml = (string)InvokeStatic("CreateCanonicalEmptyJusticeCustodyXml");
        Assert.IsTrue((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
        JusticePlayerProfileState[] reset =
            GetField<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
        Assert.AreEqual(0, reset[1].RecordState.RecidivismIndex);
        Assert.AreEqual(string.Empty, reset[1].CaseState.LastCrimeLabel);
        Assert.AreEqual(2, reset[0].RecordState.RecidivismIndex);
    }

    [TestMethod]
    public void PlayerProfiles_DangerConfirmationKeepsItsTargetAndRejectsAPlayedHeroSwitch()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            ConfigureConsistentActiveCase(
                profiles[0].CaseState,
                "modal-michael",
                25,
                1500L,
                120);
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(script, "_justiceMenuSelectedProfileSlot", 0);
            Type actionType = ScriptType.GetNestedType("MainMenuAction", BindingFlags.NonPublic);
            Assert.IsNotNull(actionType);

            Invoke(
                script,
                "RequestDangerConfirmation",
                Enum.Parse(actionType, "JusticeEnabled"));
            Assert.AreEqual(0, GetField<int>(script, "_pendingDangerJusticeProfileSlot"));

            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            Invoke(script, "ConfirmPendingDangerAction");

            Assert.IsFalse(GetField<bool>(script, "_justiceAmnestyPending"));
            Assert.AreEqual(25, profiles[0].CaseState.ActiveScore);
            Assert.AreEqual(1500L, profiles[0].CaseState.FineDue);
            Assert.IsNull(GetField<object>(script, "_pendingDangerAction"));

            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(script, "_justiceMenuSelectedProfileSlot", 1);
            Invoke(
                script,
                "RequestDangerConfirmation",
                Enum.Parse(actionType, "JusticeResetProfile"));
            Assert.AreEqual(1, GetField<int>(script, "_pendingDangerJusticeProfileSlot"));
            SetField(script, "_justiceMenuSelectedProfileSlot", 2);
            Invoke(script, "ConfirmPendingDangerAction");

            JusticePlayerProfileState[] resetProfiles =
                GetField<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
            Assert.AreEqual(0, resetProfiles[1].RecordState.RecidivismIndex);
            Assert.AreEqual(10, resetProfiles[2].RecordState.RecidivismIndex);
        });
    }

    [TestMethod]
    public void PlayerProfiles_ResetIsCancelledInMemoryAndOnDiskWhenItsCommitFails()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(script, "_justiceMenuSelectedProfileSlot", 0);
            FlushAndAwait(script);
            string path = Path.Combine(directory, "_justice_state.xml");
            string durableBeforeReset = File.ReadAllText(path);

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => true));
            Invoke(script, "ExecuteJusticeSelectedProfileReset");

            Assert.AreEqual(
                2,
                GetField<JusticeRecordState>(script, "_justiceRecordState").RecidivismIndex);
            Assert.AreEqual(durableBeforeReset, File.ReadAllText(path));

            object afterCrash = CreateHeadlessScript(null, -1);
            SetField(afterCrash, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
            Assert.AreEqual(
                2,
                GetField<JusticeRecordState>(afterCrash, "_justiceRecordState").RecidivismIndex);
        });
    }

    [TestMethod]
    public void PlayerProfiles_SuccessfulResetWritesTheEmptyProfileToPrimaryAndBackup()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(script, "_justiceMenuSelectedProfileSlot", 0);
            FlushAndAwait(script);

            Invoke(script, "ExecuteJusticeSelectedProfileReset");
            AwaitQueuedPersistence(script);

            string path = Path.Combine(directory, "_justice_state.xml");
            string backupPath = path + ".bak";
            Assert.IsTrue(File.Exists(backupPath));
            Assert.AreEqual(
                "0",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(path))
                    .Element("Record")
                    .Attribute("recidivism"));
            Assert.AreEqual(
                "0",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(backupPath))
                    .Element("Record")
                    .Attribute("recidivism"));

            File.WriteAllText(path, "<JusticeState>");
            object afterCorruption = CreateHeadlessScript(null, -1);
            SetField(
                afterCorruption,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(afterCorruption, "TryLoadJusticeState", false));
            Assert.AreEqual(
                0,
                GetField<JusticeRecordState>(afterCorruption, "_justiceRecordState")
                    .RecidivismIndex,
                "Le .bak redondant ne doit jamais ressusciter le casier supprimé.");
        });
    }

    [TestMethod]
    public void PlayerProfiles_SecondResetWriteFailureKeepsTheCommittedResetUntilBackupRetry()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(script, "_justiceMenuSelectedProfileSlot", 0);
            FlushAndAwait(script);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 2));

            Invoke(script, "ExecuteJusticeSelectedProfileReset");
            AwaitQueuedPersistence(script);

            Assert.AreEqual(
                0,
                GetField<JusticeRecordState>(script, "_justiceRecordState").RecidivismIndex,
                "Un primaire déjà vide ne doit jamais être contredit par un rollback mémoire.");
            string path = Path.Combine(directory, "_justice_state.xml");
            Assert.AreEqual(
                "0",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(path))
                    .Element("Record")
                    .Attribute("recidivism"));

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            SetField(
                script,
                "_justiceMonotonicTimeMs",
                GetField<long>(script, "_justiceNextStateFlushAttemptAtMs"));
            FlushAndAwait(script);
            Assert.AreEqual(
                "0",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(path + ".bak"))
                    .Element("Record")
                    .Attribute("recidivism"));
        });
    }

    [TestMethod]
    public void PlayerProfiles_ResetModalIsBlockedByEveryConflictingRecoveryWal()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        object script = CreateHeadlessScript(profiles, 0);
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        SetField(script, "_justiceMenuSelectedProfileSlot", 0);

        foreach (string pendingField in new[]
                 {
                     "_justiceAmnestyPending",
                     "_justiceLegalReleaseFinalizationPending",
                     "_justiceCustodyTransferRollbackFinalizationPending",
                     "_justiceActiveProfileResetPending",
                     "_justiceBackupRepairPending",
                     "_justiceProfileSwitchPersistencePending"
                 })
        {
            SetField(script, pendingField, true);
            Invoke(script, "RequestJusticeSelectedProfileReset");
            Assert.IsNull(
                GetField<object>(script, "_pendingDangerAction"),
                pendingField + " ne doit jamais laisser ouvrir la confirmation de reset.");
            SetField(script, pendingField, false);
        }
    }

    [TestMethod]
    public void PlayerProfiles_DisabledProfileReloadsAndRestoresPoliceSuppressionTokens()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.Enabled = false;
            profiles[0].CaseState.ClearActiveCase(false);
            object writer = CreateHeadlessScript(profiles, 0);
            SetField(writer, "_justicePoliceIgnoreApplied", true);
            SetField(writer, "_justicePoliceSuppressionActive", true);
            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));
            Assert.IsFalse(GetField<bool>(reader, "_justiceEnabled"));
            Assert.IsTrue(GetField<bool>(reader, "_justicePoliceIgnoreApplied"));
            Assert.IsTrue(GetField<bool>(reader, "_justicePoliceSuppressionRestorePending"));
            Assert.IsTrue((bool)Invoke(reader, "HasJusticeCustodyRecoveryState"));

            string source = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.cs"));
            int updateStart = source.IndexOf(
                "private void UpdateJusticeSystem()",
                StringComparison.Ordinal);
            int restoreCall = source.IndexOf(
                "RetryJusticePoliceSuppressionRestore(player, nowRaw)",
                updateStart,
                StringComparison.Ordinal);
            int enabledRuntime = source.IndexOf(
                "if (_justiceEnabled && (suspended || custodyActive))",
                updateStart,
                StringComparison.Ordinal);
            Assert.IsTrue(
                restoreCall > updateStart && restoreCall < enabledRuntime,
                "La restauration policière doit rester active même si le profil chargé est désactivé.");
        });
    }

    [TestMethod]
    public void PlayerProfiles_MenuAndCrimeLedgerReadTheSelectedHeroOnly()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        JusticeCaseState franklin = profiles[1].CaseState;
        franklin.Enabled = true;
        franklin.ActiveScore = 18;
        franklin.FineDue = 1000L;
        franklin.SentenceSeconds = 90;
        franklin.HasWarrant = true;
        franklin.Phase = JusticePhase.AtLarge;
        franklin.WantedEpisodeId = "profile:franklin";
        franklin.LastCrimeLabel = "Agression Franklin";
        franklin.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:profile:franklin",
            IncidentId = "incident:profile:franklin",
            EpisodeId = franklin.WantedEpisodeId,
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression Franklin",
            Points = 18,
            Fine = 1000L,
            SentenceSeconds = 90
        });

        object script = CreateHeadlessScript(profiles, 0);
        SetField(script, "_justiceMenuSelectedProfileSlot", 1);
        SetField(script, "_justiceLedgerProfileSlot", 1);

        Assert.AreEqual("Franklin", Invoke(script, "GetJusticeMenuSelectedProfileDisplay"));
        Assert.AreEqual("Recherché sous mandat", Invoke(script, "GetJusticeMenuSelectedStatusDisplay"));
        Assert.AreEqual("Agression Franklin", Invoke(script, "GetJusticeMenuSelectedLastCrimeDisplay"));
        Assert.AreEqual("Délit", Invoke(script, "GetJusticeMenuSelectedSeverityDisplay"));
        Assert.AreEqual("ACTIF", Invoke(script, "GetJusticeMenuSelectedWarrantDisplay"));
        Assert.AreEqual("1", Invoke(script, "GetJusticeMenuSelectedChargesDisplay"));
        Assert.AreEqual("1 000$", Invoke(script, "GetJusticeMenuSelectedFineDisplay"));
        Assert.AreEqual("1:30", Invoke(script, "GetJusticeMenuSelectedSentenceDisplay"));
        Assert.AreEqual("R 5/100", Invoke(script, "GetJusticeMenuSelectedRecidivismDisplay"));
        Assert.AreEqual(1, Invoke(script, "GetJusticeLedgerItemCount", false));
        Assert.AreEqual(1, Invoke(script, "GetJusticeLedgerItemCount", true));

        JusticeCharge selected = (JusticeCharge)Invoke(script, "GetJusticeActiveChargeAt", 0);
        Assert.AreEqual("Agression Franklin", selected.DisplayName);
        Assert.AreEqual("Profil Michael", profiles[0].CaseState.LastCrimeLabel);
    }

    [TestMethod]
    public void PlayerProfiles_RuntimeMutationsNeverCrossAProvenHeroSwitch()
    {
        object script = CreateHeadlessScript(CreateDistinctProfiles(), 0);

        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        Assert.IsTrue((bool)Invoke(script, "IsJusticeRuntimeProfileContextCompatible"));

        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
        Assert.IsFalse(
            (bool)Invoke(script, "IsJusticeRuntimeProfileContextCompatible"),
            "Franklin ne doit jamais alimenter le dossier Michael pendant une transaction bloquante.");

        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => -1));
        Assert.IsTrue(
            (bool)Invoke(script, "IsJusticeRuntimeProfileContextCompatible"),
            "Un ped custom sans slot conserve le dernier protagoniste déjà prouvé.");

        SetField(script, "_justiceActivePlayerProfileSlot", -1);
        Assert.IsFalse(
            (bool)Invoke(script, "IsJusticeRuntimeProfileContextCompatible"),
            "Sans aucune identité prouvée, le runtime doit rester gelé.");
    }

    [TestMethod]
    public void PlayerProfiles_ActiveCaseWithoutVanillaPursuitReloadsWithoutInventingAWarrant()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            JusticeCaseState michael = profiles[0].CaseState;
            michael.Enabled = true;
            michael.ActiveScore = 18;
            michael.FineDue = 1000L;
            michael.SentenceSeconds = 90;
            michael.HasWarrant = false;
            michael.Phase = JusticePhase.AtLarge;
            michael.WantedEpisodeId = "case:no-vanilla-wanted";
            michael.LastCrimeKind = JusticeCrimeKind.SimpleAssault;
            michael.LastCrimeLabel = "Agression signalée";
            michael.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:no-vanilla-wanted",
                IncidentId = "incident:no-vanilla-wanted",
                EpisodeId = michael.WantedEpisodeId,
                Kind = JusticeCrimeKind.SimpleAssault,
                DisplayName = michael.LastCrimeLabel,
                Points = 18,
                Fine = 1000L,
                SentenceSeconds = 90
            });
            michael.ProcessedIncidentIds.Add("incident:no-vanilla-wanted");

            object writer = CreateHeadlessScript(profiles, 0);
            FlushAndAwait(writer);

            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(
                reader,
                "TryReadJusticeStateFile",
                Path.Combine(directory, "_justice_state.xml")));

            JusticeCaseState loaded = GetField<JusticeCaseState>(reader, "_justiceCaseState");
            Assert.AreEqual(JusticePhase.AtLarge, loaded.Phase);
            Assert.IsFalse(loaded.HasWarrant);
            Assert.AreEqual(18, loaded.ActiveScore);
        });
    }

    [TestMethod]
    public void PlayerProfiles_SwitchFinalizesOldPursuitAndReconcilesTheActivatedProfile()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        JusticeCaseState michael = profiles[0].CaseState;
        ConfigureConsistentActiveCase(michael, "pursuit-michael", 48, 0L, 0);
        michael.Phase = JusticePhase.Wanted;
        michael.HasWarrant = false;
        JusticeCaseState franklin = profiles[1].CaseState;
        ConfigureConsistentActiveCase(franklin, "pursuit-franklin", 34, 0L, 0);
        franklin.Phase = JusticePhase.Surrendering;
        franklin.HasWarrant = false;

        object script = CreateHeadlessScript(profiles, 0);
        InitializeRuntimeCollection(script, "_justiceAllyTokens");
        SetField(script, "_justiceWantedLossPending", true);

        Invoke(script, "FinalizeJusticePursuitStateBeforeProfileSwitch", 0);
        Invoke(script, "SnapshotActiveJusticePlayerProfile");

        Assert.IsTrue(michael.HasWarrant);
        Assert.AreEqual(JusticePhase.AtLarge, michael.Phase);
        Assert.IsFalse(GetField<bool>(script, "_justiceWantedLossPending"));

        Assert.IsTrue((bool)Invoke(script, "ActivateJusticePlayerProfile", 1));
        Invoke(script, "ReconcileLoadedJusticePursuitState", 0);

        Assert.IsTrue(franklin.HasWarrant);
        Assert.AreEqual(JusticePhase.AtLarge, franklin.Phase);
        Assert.AreEqual(2, profiles[0].RecordState.RecidivismIndex);

        JusticeCaseState ordinary = profiles[2].CaseState;
        ConfigureConsistentActiveCase(ordinary, "ordinary-trevor", 18, 0L, 0);
        ordinary.Phase = JusticePhase.AtLarge;
        ordinary.HasWarrant = false;
        Assert.IsTrue((bool)Invoke(script, "ActivateJusticePlayerProfile", 2));
        Invoke(script, "ReconcileLoadedJusticePursuitState", 0);
        Assert.IsFalse(ordinary.HasWarrant, "Un dossier normal ne doit pas devenir un mandat au changement de héros.");
    }

    [TestMethod]
    public void PlayerProfiles_FailedSwitchCommitBlocksUntilAllProfilesAreDurable()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            ConfigureConsistentActiveCase(
                profiles[0].CaseState,
                "switch-michael",
                25,
                0L,
                0);
            profiles[0].CaseState.Phase = JusticePhase.Wanted;
            object script = CreateHeadlessScript(profiles, 0);

            Invoke(script, "FinalizeJusticePursuitStateBeforeProfileSwitch", 0);
            Invoke(script, "SnapshotActiveJusticePlayerProfile");
            Assert.IsTrue((bool)Invoke(script, "ActivateJusticePlayerProfile", 1));
            SetField(script, "_justiceProfileSwitchPersistencePending", true);
            SetField(script, "_justiceStateFlushFailureOverride", new Func<int, bool>(attempt => true));

            Assert.IsFalse((bool)Invoke(script, "PersistPendingJusticeProfileSwitch"));
            Assert.IsTrue(GetField<bool>(script, "_justiceProfileSwitchPersistencePending"));
            Assert.IsTrue(profiles[0].CaseState.HasWarrant, "Le snapshot Michael doit rester en mémoire pendant le retry.");

            SetField(script, "_justiceStateFlushFailureOverride", new Func<int, bool>(attempt => false));
            SetField(
                script,
                "_justiceMonotonicTimeMs",
                GetField<long>(script, "_justiceNextStateFlushAttemptAtMs"));
            Assert.IsFalse(
                (bool)Invoke(script, "PersistPendingJusticeProfileSwitch"),
                "Le premier passage doit seulement enfiler le snapshot du nouveau profil.");
            AwaitQueuedPersistence(script);
            Assert.IsTrue(
                (bool)Invoke(script, "PersistPendingJusticeProfileSwitch"),
                "Le contexte ne doit être libéré qu'après confirmation de DiskRevision.");
            Assert.IsFalse(GetField<bool>(script, "_justiceProfileSwitchPersistencePending"));

            XDocument durable = XDocument.Load(Path.Combine(directory, "_justice_state.xml"));
            Assert.AreEqual(
                "1",
                (string)durable.Root.Element("RuntimeRecovery").Attribute("activePlayerSlot"));
            XElement michael = durable.Root
                .Element("Profiles")
                .Elements("Profile")
                .Single(profile => (string)profile.Attribute("slot") == "0");
            Assert.AreEqual("true", (string)michael.Element("Case").Attribute("hasWarrant"));
        });
    }

    [TestMethod]
    public void PlayerProfiles_ProfileSwitchCapturesWriterFailureBaselineBeforeEnqueue()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            Invoke(script, "InitializeJusticePersistenceServices");
            Invoke(script, "ShutdownJusticePersistenceServices");

            SwitchFailureAtomicFileStore store =
                new SwitchFailureAtomicFileStore();
            JusticeRepository repository = new JusticeRepository(
                Path.Combine(directory, "_justice_state.xml"),
                Path.Combine(directory, "_justice_state.xml.bak"),
                new JusticeXmlPersistenceCodec(),
                0L,
                store,
                JusticeNoOpPersistenceFaultInjector.Instance,
                10);
            repository.Start();
            SetField(script, "_justiceRepository", repository);
            SetField(script, "_justiceProfileSwitchPersistencePending", true);

            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            FieldInfo walGateField = typeof(JusticeWriteAheadLog).GetField(
                "_gate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(walGateField);
            object walGate = walGateField.GetValue(wal);
            Assert.IsNotNull(walGate);

            bool persistResult = true;
            Exception persistException = null;
            Thread persistThread = new Thread(delegate()
            {
                try
                {
                    persistResult =
                        (bool)Invoke(script, "PersistPendingJusticeProfileSwitch");
                }
                catch (Exception exception)
                {
                    persistException = exception;
                }
            });
            persistThread.IsBackground = true;

            bool writerAttempted;
            bool failureObserved;
            Monitor.Enter(walGate);
            try
            {
                persistThread.Start();
                writerAttempted = store.Attempted.WaitOne(TimeSpan.FromSeconds(5));
                failureObserved = writerAttempted && SpinWait.SpinUntil(
                    delegate()
                    {
                        return repository.GetDiagnostics().WriteFailures > 0L;
                    },
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                Monitor.Exit(walGate);
            }

            bool threadCompleted = persistThread.Join(TimeSpan.FromSeconds(5));
            try
            {
                Assert.IsTrue(writerAttempted, "Le writer fautif doit recevoir le snapshot.");
                Assert.IsTrue(failureObserved, "L'échec doit précéder la fin de l'enqueue simulé.");
                Assert.IsTrue(threadCompleted, "Le thread de test ne doit pas rester bloqué sur le WAL.");
                Assert.IsNull(persistException);
                Assert.IsFalse(persistResult);
                Assert.IsTrue(repository.GetDiagnostics().WriteFailures > 0L);
                Assert.AreEqual(
                    0L,
                    GetField<long>(script, "_justiceProfileSwitchPersistenceWriteFailures"),
                    "La baseline doit précéder l'enqueue, même si le writer échoue avant son retour.");
            }
            finally
            {
                store.AllowWrites();
                long revision =
                    GetField<long>(script, "_justiceProfileSwitchPersistenceRevision");
                repository.Flush(revision, TimeSpan.FromSeconds(5));
                Invoke(script, "ShutdownJusticePersistenceServices");
                store.Dispose();
            }
        });
    }

    [TestMethod]
    public void PlayerProfiles_ProfileChangeRejectsAnUneffectedAttemptedBarrier()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            Invoke(script, "InitializeJusticePersistenceServices");

            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            const string transactionId = "profile-switch:old-barrier";
            long createdAt = DateTime.UtcNow.Ticks;
            JusticePersistenceField[] fields =
            {
                new JusticePersistenceField("caller", "OldProfileOperation")
            };
            wal.Append(new JusticeWalRecord(
                transactionId,
                "OldProfileOperation",
                0,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                "profile-switch:unrelated",
                "UnrelatedOperation",
                2,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                "profile-switch:unrelated",
                "UnrelatedOperation",
                2,
                JusticeWalState.Attempted,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                transactionId,
                "OldProfileOperation",
                0,
                JusticeWalState.Attempted,
                1L,
                createdAt,
                fields));

            SetField(script, "_justiceCriticalBarrierCaller", "OldProfileOperation");
            SetField(script, "_justiceCriticalBarrierOperationKind", "OldProfileOperation");
            SetField(script, "_justiceCriticalBarrierTransactionId", transactionId);
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:0");
            SetField(script, "_justiceCriticalBarrierRevision", 1L);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 0L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", createdAt);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);

            Assert.IsTrue((bool)Invoke(
                script,
                "TryRejectJusticeCriticalBarrierForProfileChange",
                1));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            Assert.AreEqual(
                JusticeWalState.Rejected,
                wal.GetLatest(transactionId).State,
                "Attempted est annulable ici car la barrière encore présente prouve que l'appelant n'a reçu aucun droit d'effet.");
            Assert.AreEqual(
                1,
                wal.GetOpenTransactions().Count,
                "La frontière sans rapport doit rester ouverte et empêcher la compaction du test.");
        });
    }

#if DONJ_STUB_API
    [TestMethod]
    public void PlayerProfiles_ProfileChangeCompletesPoliceRestorationBarrierBeforeSwitch()
    {
        GTA.StubRuntime.Reset();
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            SetField(script, "_justicePoliceIgnoreApplied", true);
            SetField(script, "_justicePoliceDispatchDisabled", true);
            SetField(script, "_justicePoliceSuppressionActive", true);
            SetField(script, "_justicePoliceSuppressionRestorePending", false);
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 1));

            Assert.IsFalse((bool)Invoke(
                script,
                "EnsureJusticeProfileMatchesCanonicalPlayer",
                new object[] { null }));
            Assert.AreEqual(0, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
            Assert.AreEqual(
                "SetJusticeCustodyPoliceSuppression",
                GetField<string>(script, "_justiceCriticalBarrierCaller"));
            Assert.IsTrue(GetField<long>(script, "_justiceCriticalBarrierRevision") > 0L);
            string transactionId =
                GetField<string>(script, "_justiceCriticalBarrierTransactionId");
            AwaitQueuedPersistence(script);

            Assert.IsFalse(
                (bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                "Le profil entrant doit encore attendre sa propre révision disque.");
            Assert.AreEqual(1, GetField<int>(script, "_justiceActivePlayerProfileSlot"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            JusticeWalRecord policeRecord = wal.GetLatest(transactionId);
            Assert.IsTrue(
                policeRecord == null ||
                policeRecord.State == JusticeWalState.Ambiguous ||
                policeRecord.State == JusticeWalState.Confirmed,
                "La restauration police doit être en attente, acquittée ou déjà compactée, jamais rejetée.");

            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "EnsureJusticeProfileMatchesCanonicalPlayer",
                new object[] { null }));
            Assert.IsFalse(GetField<bool>(script, "_justiceProfileSwitchPersistencePending"));
            Assert.IsFalse(GetField<bool>(script, "_justicePoliceIgnoreApplied"));
            Assert.IsFalse(GetField<bool>(script, "_justicePoliceDispatchDisabled"));
            Assert.IsFalse(GetField<bool>(script, "_justicePoliceSuppressionActive"));
            JusticeCustodyPersistenceSnapshot parked =
                RequireTypedCustodySnapshot(profiles[0]);
            Assert.IsFalse(parked.PoliceSuppressionApplied);
            Assert.IsFalse(parked.PoliceDispatchDisabled);
        });
    }
#endif

    [TestMethod]
    public void CustodyDeath_RebindCannotProceedBeforeTheRetryIsPersisted()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            SetField(script, "_justiceCustodyWaitingForRespawn", true);
            SetField(script, "_justiceCustodyDeathRebindPending", true);
            SetField(script, "_justiceCustodyDeathStatePersistencePending", true);
            AssertCurrentCustodyFragmentIsValid(script, profiles[0]);
            int attempts = 0;
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt =>
                {
                    attempts++;
                    return attempts == 1;
                }));

            Assert.IsFalse((bool)Invoke(script, "PersistJusticeCustodyDeathStateBeforeRespawn", 1000));
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathStatePersistencePending"));
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeCustodyDeathStateBeforeRespawn", 1500));
            Assert.AreEqual(1, attempts, "Le retry doit être cadencé au lieu de réécrire chaque frame.");

            SetField(
                script,
                "_justiceMonotonicTimeMs",
                GetField<long>(script, "_justiceNextStateFlushAttemptAtMs"));
            Assert.IsTrue((bool)Invoke(script, "PersistJusticeCustodyDeathStateBeforeRespawn", 2000));
            AwaitQueuedPersistence(script);
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyDeathStatePersistencePending"));
            Assert.AreEqual(2, attempts);

            XDocument durable = XDocument.Load(Path.Combine(directory, "_justice_state.xml"));
            XElement custody = GetPersistedActiveJusticeProfile(durable).Element("Custody");
            Assert.AreEqual("true", (string)custody.Attribute("waitingForRespawn"));
            Assert.AreEqual("true", (string)custody.Attribute("deathRebindPending"));
        });
    }

    [TestMethod]
    public void ActiveCustodyReset_WalSurvivesFinalFlushFailureAndReload()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(writer, profiles[0]);
            AssertCurrentCustodyFragmentIsValid(writer, profiles[0]);

            // Je vérifie le nouveau précommit : deux petites frames WAL bornées
            // suivent le snapshot durable, sans embarquer un second XML complet.
            Assert.IsFalse(
                (bool)Invoke(writer, "BeginJusticeActiveProfileResetTransaction", 0),
                "Le premier passage doit seulement enfiler le snapshot critique.");
            AwaitQueuedPersistence(writer);
            Assert.IsTrue((bool)Invoke(writer, "BeginJusticeActiveProfileResetTransaction", 0));
            Assert.IsTrue(GetField<bool>(writer, "_justiceActiveProfileResetPending"));
            Assert.IsTrue(GetField<bool>(writer, "_justiceActiveProfileResetPrecommitRedundant"));

            string walPath = Path.Combine(directory, "_justice_state.wal");
            Assert.IsTrue(File.Exists(walPath));
            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.IsTrue(wal.Records.Any(record =>
                record.State == JusticeWalState.Prepared &&
                string.Equals(record.OperationKind, "ProfileReset", StringComparison.Ordinal) &&
                record.Fields.Any(field =>
                    field.Path == "boundary" &&
                    field.Value == "EnsureJusticeActiveProfileResetPrecommitRedundant") &&
                !record.Fields.Any(field =>
                    field.Path == "Case" ||
                    field.Path == "Record" ||
                    field.Path == "Custody")));

            string path = Path.Combine(directory, "_justice_state.xml");
            FlushAndAwait(writer);
            XDocument precommit = XDocument.Load(path);
            Assert.IsTrue(GetPersistedActiveJusticeProfile(precommit)
                .Element("Case")
                .Element("CompletedOperations")
                .Elements("Operation")
                .Any(operation => operation.Value.StartsWith("ResetProfile:", StringComparison.Ordinal)));

            object resumed = CreateHeadlessScript(null, -1);
            SetField(resumed, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            if (!(bool)Invoke(resumed, "TryReadJusticeStateFile", path))
            {
                Assert.Fail(
                    "Le snapshot de reset doit rester relisible après acquittement WAL. WAL=" +
                    string.Join(",", JusticeWriteAheadLog.Recover(walPath).Records.Select(record =>
                        record.OperationKind + ":" + record.State)) +
                    "; erreur=" + GetField<string>(resumed, "_justicePersistenceLastError"));
            }
            Assert.IsTrue(
                GetField<bool>(resumed, "_justiceActiveProfileResetPending"),
                "Le snapshot relu doit conserver l'intention ResetProfile.");
            Assert.IsFalse((bool)Invoke(
                resumed,
                "EnsureJusticeActiveProfileResetPrecommitRedundant"),
                "La reprise doit d'abord enfiler son nouveau snapshot critique.");
            AwaitQueuedPersistence(resumed);
            Assert.IsTrue((bool)Invoke(
                resumed,
                "EnsureJusticeActiveProfileResetPrecommitRedundant"),
                "La reprise doit confirmer le précommit après la barrière disque.");
            Assert.IsTrue(
                GetField<bool>(resumed, "_justiceActiveProfileResetPrecommitRedundant"),
                "Le latch de précommit redondant doit être restauré.");

            // Je simule ici les effets monde déjà terminés : le commit final doit
            // rester rejouable sans ressusciter l'ancien casier après un crash.
            Invoke(resumed, "ResetJusticeCustodyPersistentFields", false);
            GetField<JusticeCaseState>(resumed, "_justiceCaseState").Phase = JusticePhase.AtLarge;
            InitializeProfileResetRuntimeCollections(resumed);
            SetField(resumed, "_justiceStateFlushFailureOverride", new Func<int, bool>(attempt => true));
            Assert.IsFalse((bool)Invoke(resumed, "ResumeJusticeActiveProfileResetTransaction"));
            Assert.IsTrue(GetField<bool>(resumed, "_justiceActiveProfileResetPending"));
            Assert.AreEqual(
                0,
                GetField<JusticeRecordState>(resumed, "_justiceRecordState").RecidivismIndex,
                "Après le précommit redondant, l'état vide reste repris en mémoire jusqu'à son ACK.");

            object afterCrash = CreateHeadlessScript(null, -1);
            SetField(afterCrash, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
            Assert.IsTrue(GetField<bool>(afterCrash, "_justiceActiveProfileResetPending"));
            Assert.AreEqual(2, GetField<JusticeRecordState>(afterCrash, "_justiceRecordState").RecidivismIndex);

            SetField(resumed, "_justiceStateFlushFailureOverride", new Func<int, bool>(attempt => false));
            SetField(
                resumed,
                "_justiceMonotonicTimeMs",
                GetField<long>(resumed, "_justiceNextStateFlushAttemptAtMs"));
            Assert.IsTrue((bool)Invoke(resumed, "ResumeJusticeActiveProfileResetTransaction"));
            AwaitQueuedPersistence(resumed);
            Assert.IsFalse(GetField<bool>(resumed, "_justiceActiveProfileResetPending"));

            XDocument committed = XDocument.Load(path);
            XElement committedProfile = GetPersistedActiveJusticeProfile(committed);
            Assert.AreEqual("0", (string)committedProfile.Element("Record").Attribute("recidivism"));
            Assert.IsFalse(committedProfile
                .Element("Case")
                .Element("CompletedOperations")
                .Elements("Operation")
                .Any(operation => operation.Value.StartsWith("ResetProfile:", StringComparison.Ordinal)));

            string source = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Profiles.cs"));
            int resumeStart = source.IndexOf(
                "private bool ResumeJusticeActiveProfileResetTransaction()",
                StringComparison.Ordinal);
            int nextMethod = source.IndexOf(
                "private static bool HasPendingJusticeProfileResetOperation",
                resumeStart,
                StringComparison.Ordinal);
            string resumeBody = source.Substring(resumeStart, nextMethod - resumeStart);
            StringAssert.Contains(resumeBody, "JusticeAmnestyCustody()");
            StringAssert.Contains(resumeBody, "ReplaceJusticePlayerProfileWithEmptyState(slot)");
            int resumePrecommitAt = resumeBody.IndexOf(
                "EnsureJusticeActiveProfileResetPrecommitRedundant()",
                StringComparison.Ordinal);
            int resumeAmnestyAt = resumeBody.IndexOf(
                "JusticeAmnestyCustody()",
                StringComparison.Ordinal);
            Assert.IsTrue(
                resumePrecommitAt >= 0 && resumeAmnestyAt > resumePrecommitAt,
                "Le WAL redondant doit être confirmé avant toute restitution.");
            Assert.IsFalse(
                resumeBody.Contains("ResetJusticePlayerProfile(slot)"),
                "Après restitution, le reset WAL doit contourner le garde recovery devenu obsolète.");

            int beginStart = source.IndexOf(
                "private bool BeginJusticeActiveProfileResetTransaction",
                StringComparison.Ordinal);
            int resumeDeclaration = source.IndexOf(
                "private bool ResumeJusticeActiveProfileResetTransaction",
                beginStart,
                StringComparison.Ordinal);
            string beginBody = source.Substring(
                beginStart,
                resumeDeclaration - beginStart);
            StringAssert.Contains(
                beginBody,
                "EnsureJusticeActiveProfileResetPrecommitRedundant()",
                "Le début doit conserver et reprendre le WAL ambigu au lieu de l'annuler.");
            int ensureStart = source.IndexOf(
                "private bool EnsureJusticeActiveProfileResetPrecommitRedundant()",
                beginStart,
                StringComparison.Ordinal);
            Assert.IsTrue(ensureStart >= 0 && ensureStart < resumeStart);
            string ensureBody = source.Substring(ensureStart, resumeStart - ensureStart);
            StringAssert.Contains(ensureBody, "PersistJusticeCriticalPrecommitRedundantly()");
            Assert.IsFalse(
                beginBody.Contains("CompletedOperationIds.Remove(operation.OperationId)"),
                "Un échec de la copie redondante ne doit jamais retirer l'intention en mémoire.");
        });
    }

    [TestMethod]
    public void TransferRollback_WalPrecommitFailureKeepsOperationUntilRetry()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            JusticeCaseState activeCase = GetField<JusticeCaseState>(script, "_justiceCaseState");
            string episode = activeCase.CustodyEpisodeId;
            JusticeOperation rollback = new JusticeOperation(
                JusticePolicy.CreateOperationId(JusticeOperationKind.TransferRollback, episode),
                JusticeOperationKind.TransferRollback,
                episode);
            Assert.IsTrue(JusticePolicy.TryRegisterOperation(activeCase, rollback));

            SetField(script, "_justiceCustodyTransferRollbackFinalizationPending", true);
            SetField(script, "_justiceCustodyTransferRollbackPrecommitRedundant", false);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 1));

            Assert.IsFalse((bool)Invoke(
                script,
                "EnsureJusticeCustodyTransferRollbackPrecommitRedundant"));
            CollectionAssert.Contains(activeCase.CompletedOperationIds, rollback.OperationId);
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackFinalizationPending"));
            Assert.IsFalse(
                File.Exists(Path.Combine(directory, "_justice_state.wal")),
                "Un échec injecté avant Append ne doit créer aucune fausse transaction WAL.");

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            Assert.IsFalse((bool)Invoke(
                script,
                "EnsureJusticeCustodyTransferRollbackPrecommitRedundant"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "EnsureJusticeCustodyTransferRollbackPrecommitRedundant"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackPrecommitRedundant"));

            string primaryPath = Path.Combine(directory, "_justice_state.xml");
            string walPath = Path.Combine(directory, "_justice_state.wal");
            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.IsTrue(wal.Records.Any(record =>
                record.State == JusticeWalState.Prepared &&
                string.Equals(record.OperationKind, "Rollback", StringComparison.Ordinal) &&
                record.Fields.Any(field =>
                    field.Path == "boundary" &&
                    field.Value == "CustodyRollback") &&
                !record.Fields.Any(field =>
                    field.Path == "Case" ||
                    field.Path == "Record" ||
                    field.Path == "Custody")));

            FlushAndAwait(script);
            Assert.IsTrue(GetPersistedActiveJusticeProfile(XDocument.Load(primaryPath))
                .Element("Case")
                .Element("CompletedOperations")
                .Elements("Operation")
                .Any(operation => operation.Value == rollback.OperationId));
        });
    }

    [TestMethod]
    public void Amnesty_WalPrecommitFailureKeepsIntentUntilDurableRetry()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            JusticeCaseState activeCase = GetField<JusticeCaseState>(script, "_justiceCaseState");
            int originalScore = activeCase.ActiveScore;

            SetField(script, "_justiceAmnestyPending", true);
            SetField(script, "_justiceAmnestyPrecommitRedundant", false);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 1));

            Assert.IsFalse((bool)Invoke(script, "EnsureJusticeAmnestyPrecommitRedundant"));
            Assert.IsTrue(GetField<bool>(script, "_justiceAmnestyPending"));
            Assert.IsFalse(GetField<bool>(script, "_justiceAmnestyPrecommitRedundant"));
            Assert.AreEqual(originalScore, activeCase.ActiveScore);

            string primaryPath = Path.Combine(directory, "_justice_state.xml");
            Assert.IsFalse(File.Exists(primaryPath));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "_justice_state.wal")));

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            Assert.IsFalse((bool)Invoke(script, "EnsureJusticeAmnestyPrecommitRedundant"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(script, "EnsureJusticeAmnestyPrecommitRedundant"));
            Assert.IsTrue(GetField<bool>(script, "_justiceAmnestyPending"));
            Assert.IsTrue(GetField<bool>(script, "_justiceAmnestyPrecommitRedundant"));

            string walPath = Path.Combine(directory, "_justice_state.wal");
            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.IsTrue(wal.Records.Any(record =>
                record.State == JusticeWalState.Prepared &&
                string.Equals(record.OperationKind, "Amnesty", StringComparison.Ordinal)));

            FlushAndAwait(script);
            Assert.AreEqual(
                "true",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(primaryPath))
                    .Attribute("pendingAmnestyWantedClear"));
            Assert.AreEqual(originalScore, activeCase.ActiveScore);
        });
    }

    [TestMethod]
    public void LegalRelease_WalFailureKeepsTheCustodySnapshotBeforeWorldEffects()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureLegalReleasePrecommitRuntime(script, profiles[0]);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => attempt == 1));

            Assert.IsFalse((bool)Invoke(script, "PersistJusticeLegalReleaseBarrier"));
            Assert.IsTrue(GetField<bool>(script, "_justiceLegalReleaseFinalizationPending"));
            Assert.IsTrue(GetField<bool>(script, "_justiceInventoryRemoved"));
            Assert.IsNotNull(GetField<object>(script, "_justiceWeaponSnapshot"));
            Assert.AreEqual(
                JusticePhase.Incarcerated,
                GetField<JusticeCaseState>(script, "_justiceCaseState").Phase);

            string path = Path.Combine(directory, "_justice_state.xml");
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "_justice_state.wal")));

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeLegalReleaseBarrier"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(script, "PersistJusticeLegalReleaseBarrier"));
            string walPath = Path.Combine(directory, "_justice_state.wal");
            JusticeWalRecoveryResult wal = JusticeWriteAheadLog.Recover(walPath);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, wal.Status);
            Assert.IsTrue(wal.Records.Any(record =>
                record.State == JusticeWalState.Prepared &&
                string.Equals(record.OperationKind, "Release", StringComparison.Ordinal)));

            FlushAndAwait(script);
            XElement durable = GetPersistedActiveJusticeProfile(XDocument.Load(path));
            Assert.AreEqual(
                "true",
                (string)durable.Attribute("pendingLegalReleaseFinalization"));
            Assert.AreEqual(
                "Incarcerated",
                (string)durable.Element("Case").Attribute("phase"));
            Assert.AreEqual(
                "true",
                (string)durable.Element("Custody").Attribute("inventoryRemoved"));
            Assert.IsNotNull(durable.Element("Custody").Element("InventorySnapshot"));
        });
    }

    [TestMethod]
    public void LegalRelease_PrecommitReloadPreservesTheWalAndRoutesOnlyItsFinalization()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);
            ConfigureLegalReleasePrecommitRuntime(writer, profiles[0]);
            Assert.IsFalse((bool)Invoke(writer, "PersistJusticeLegalReleaseBarrier"));
            AwaitQueuedPersistence(writer);
            Assert.IsTrue((bool)Invoke(writer, "PersistJusticeLegalReleaseBarrier"));
            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));

            Assert.IsTrue(GetField<bool>(reader, "_justiceLegalReleaseFinalizationPending"));
            Assert.AreEqual(
                "Bolingbroke",
                GetField<object>(reader, "_justiceLegalReleaseFinalizationSite").ToString());
            Assert.AreEqual(12345, GetField<int>(reader, "_justiceLegalReleaseSelectedWeaponHash"));
            Assert.AreEqual(
                JusticePhase.Incarcerated,
                GetField<JusticeCaseState>(reader, "_justiceCaseState").Phase);
            Assert.IsNotNull(GetField<object>(reader, "_justiceWeaponSnapshot"));
            Assert.IsTrue((bool)Invoke(reader, "IsJusticeLegalReleasePrecommitState"));

            XDocument legacy = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            legacy.Root.Element("PlayerProfiles").Remove();
            legacy.Root.Attribute("activePlayerSlot").Remove();
            Invoke(writer, "ShutdownJusticePersistenceServices");
            legacy.Save(path);
            object legacyReader = CreateHeadlessScript(null, -1);
            SetField(legacyReader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(legacyReader, "TryReadJusticeStateFile", path));
            Assert.IsTrue(GetField<bool>(legacyReader, "_justiceLegalReleaseFinalizationPending"));
            Assert.IsNotNull(GetField<object>(legacyReader, "_justiceWeaponSnapshot"));
            Assert.IsTrue((bool)Invoke(legacyReader, "IsJusticeLegalReleasePrecommitState"));
        });
    }

    [TestMethod]
    public void LegalRelease_AcknowledgementFailureKeepsAReplayableReleasedProfile()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = true;
            object script = CreateHeadlessScript(profiles, 0);
            SetField(script, "_justiceLegalReleaseFinalizationPending", true);
            SetPrivateEnumField(script, "_justiceLegalReleaseFinalizationSite", "Bolingbroke");
            SetField(script, "_justiceLegalReleaseSelectedWeaponHash", 12345);
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeLegalReleaseBarrier"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(script, "PersistJusticeLegalReleaseBarrier"));
            FlushAndAwait(script);

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => true));
            Assert.IsFalse((bool)Invoke(
                script,
                "CommitJusticeLegalReleaseFinalizationAcknowledgement"));
            Assert.IsTrue(GetField<bool>(script, "_justiceLegalReleaseFinalizationPending"));
            Assert.AreEqual(12345, GetField<int>(script, "_justiceLegalReleaseSelectedWeaponHash"));

            string path = Path.Combine(directory, "_justice_state.xml");
            object afterCrash = CreateHeadlessScript(null, -1);
            SetField(afterCrash, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
            Assert.IsTrue(GetField<bool>(afterCrash, "_justiceLegalReleaseFinalizationPending"));
            Assert.AreEqual(
                JusticePhase.AtLarge,
                GetField<JusticeCaseState>(afterCrash, "_justiceCaseState").Phase);

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            Assert.IsFalse((bool)Invoke(
                script,
                "CommitJusticeLegalReleaseFinalizationAcknowledgement"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "CommitJusticeLegalReleaseFinalizationAcknowledgement"));
            Assert.IsFalse(GetField<bool>(script, "_justiceLegalReleaseFinalizationPending"));
            FlushAndAwait(script);

            object committed = CreateHeadlessScript(null, -1);
            SetField(committed, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(committed, "TryReadJusticeStateFile", path));
            Assert.IsFalse(GetField<bool>(committed, "_justiceLegalReleaseFinalizationPending"));
            Assert.AreEqual(
                JusticePhase.AtLarge,
                GetField<JusticeCaseState>(committed, "_justiceCaseState").Phase);
        });
    }

    private static JusticePlayerProfileState[] CreateDistinctProfiles()
    {
        JusticePlayerProfileState[] profiles = new JusticePlayerProfileState[3];
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            JusticeCaseState caseState = new JusticeCaseState
            {
                Enabled = slot != 0,
                LastCrimeKind = JusticeCrimeKind.ReportedViolentAct,
                LastCrimeLabel = slot == 0
                    ? "Profil Michael"
                    : (slot == 1 ? "Profil Franklin" : "Profil Trevor")
            };
            int points = slot == 0 ? 5 : (slot == 1 ? 18 : 34);
            JusticeSeverity severity = slot == 0
                ? JusticeSeverity.Minor
                : (slot == 1 ? JusticeSeverity.Misdemeanor : JusticeSeverity.Serious);
            string convictionId = "conviction:profile:" + slot;
            JusticeRecordState recordState = new JusticeRecordState
            {
                RecidivismIndex = slot == 0 ? 2 : (slot == 1 ? 5 : 10)
            };
            JusticeConviction conviction = new JusticeConviction
            {
                ConvictionId = convictionId,
                JudgedAtUtc = new DateTime(2026, 8, 25, 12 + slot, 0, 0, DateTimeKind.Utc),
                Severity = severity,
                Score = points,
                Fine = 250L + slot * 750L,
                SentenceSeconds = slot * 90
            };
            conviction.Charges.Add(new JusticeConvictionChargeSummary
            {
                Kind = slot == 0
                    ? JusticeCrimeKind.ReportedViolentAct
                    : JusticeCrimeKind.SimpleAssault,
                DisplayName = "Condamnation profil " + slot,
                Points = points,
                Fine = conviction.Fine,
                SentenceSeconds = conviction.SentenceSeconds
            });
            recordState.Convictions.Add(conviction);
            recordState.AppliedConvictionIds.Add(convictionId);
            profiles[slot] = new JusticePlayerProfileState(slot)
            {
                CaseState = caseState,
                RecordState = recordState,
                CustodyXml = (string)InvokeStatic("CreateCanonicalEmptyJusticeCustodyXml"),
                LastCanonicalPlayerModel = 1000 + slot
            };
        }
        return profiles;
    }

    private static void ConfigureIncarceratedRuntime(
        object script,
        JusticePlayerProfileState profile)
    {
        JusticeCaseState state = profile.CaseState;
        ConfigureConsistentActiveCase(
            state,
            "profile-reset-test",
            75,
            10000L,
            600);
        state.FineDue = 0L;
        state.Phase = JusticePhase.Incarcerated;
        state.CustodyEpisodeId = "custody:profile-reset-test";
        state.Charges[0].IsAdjudicated = true;
        state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyConviction,
            state.CustodyEpisodeId));

        string convictionId = "conviction:" + state.CustodyEpisodeId;
        JusticeRecordState record = profile.RecordState;
        record.AppliedConvictionIds.Add(convictionId);
        record.PinnedConvictionId = convictionId;
        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = convictionId,
            JudgedAtUtc = new DateTime(2026, 8, 26, 1, 0, 0, DateTimeKind.Utc),
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
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyInitialSentenceSeconds", 600);
        SetField(script, "_justiceCustodyPlayerModelHash", 123456);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetPrivateEnumField(script, "_justiceCustodySite", "Bolingbroke");
    }

    private static void ConfigureLegalReleasePrecommitRuntime(
        object script,
        JusticePlayerProfileState profile)
    {
        ConfigureIncarceratedRuntime(script, profile);
        JusticeCaseState state = profile.CaseState;
        state.SentenceSeconds = 0;
        state.FineDue = 0L;
        state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.Release,
            state.CustodyEpisodeId));
        SetField(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
        SetField(script, "_justiceInventoryRemoved", true);
        SetPrivateEnumField(script, "_justiceInventoryCustodyState", "RemovedVerified");
        SetField(script, "_justiceWeaponControlsLocked", true);
        SetField(script, "_justiceLegalReleaseFinalizationPending", true);
        SetPrivateEnumField(script, "_justiceLegalReleaseFinalizationSite", "Bolingbroke");
        SetField(script, "_justiceLegalReleaseSelectedWeaponHash", 12345);
    }

    private static object CreateValidWeaponSnapshot()
    {
        Type snapshotType = ScriptType.GetNestedType(
            "JusticeWeaponSnapshot",
            BindingFlags.NonPublic);
        Type itemType = ScriptType.GetNestedType(
            "JusticeWeaponSnapshotItem",
            BindingFlags.NonPublic);
        Assert.IsNotNull(snapshotType);
        Assert.IsNotNull(itemType);
        object snapshot = Activator.CreateInstance(snapshotType, true);
        object item = Activator.CreateInstance(itemType, true);
        SetMemberValue(snapshot, "IsValidated", true);
        SetMemberValue(snapshot, "SelectedWeaponHash", 12345);
        SetMemberValue(item, "WeaponHash", 12345);
        SetMemberValue(item, "Ammo", 50);
        SetMemberValue(item, "AmmoInClip", 12);
        SetMemberValue(item, "Tint", 1);
        ((IList)GetMemberValue(item, "ComponentHashes")).Add(777);
        ((IList)GetMemberValue(snapshot, "Weapons")).Add(item);
        return snapshot;
    }

    private static object GetMemberValue(object target, string name)
    {
        Type type = target.GetType();
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            return field.GetValue(target);
        }

        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(property, name);
        return property.GetValue(target, null);
    }

    private static void SetMemberValue(object target, string name, object value)
    {
        Type type = target.GetType();
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }

        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(property, name);
        property.SetValue(target, value, null);
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

    private static void AssertCurrentCustodyFragmentIsValid(
        object script,
        JusticePlayerProfileState profile)
    {
        string custody = (string)Invoke(script, "CaptureCurrentJusticeCustodyXml");
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml("<Profile>" + custody + "</Profile>");
        Assert.IsTrue(
            (bool)InvokeStatic(
                "IsJusticeCustodyXmlSemanticallyValid",
                document.DocumentElement,
                profile.CaseState,
                profile.RecordState),
            custody);
    }

    private static void InitializeProfileResetRuntimeCollections(object script)
    {
        foreach (string fieldName in new[]
                 {
                     "_justicePendingIncidents",
                     "_justiceRecentVictims",
                     "_justiceRecentVehicles",
                     "_justiceAllyTokens",
                     "_justiceTrackedIdentities",
                     "_justiceSelfDefenseUntilByVictim",
                     "_justiceSelfDefenseThreatByVictim"
                 })
        {
            InitializeRuntimeCollection(script, fieldName);
        }
    }

    private static void InitializeRuntimeCollection(object script, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, fieldName);
        if (field.GetValue(script) == null)
        {
            field.SetValue(script, Activator.CreateInstance(field.FieldType));
        }
    }

    private static void SetPrivateEnumField(object script, string fieldName, string value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(script, Enum.Parse(field.FieldType, value));
    }

    private static object CreateHeadlessScript(
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
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        SetField(script, "_justiceReleaseSelectedWeaponHash", unchecked((int)0xA2719263));
        SetField(script, "_justiceActivityCooldownUntil", new Dictionary<string, int>());
        SetField(script, "_justiceLoadedActivityCooldownSeconds", new Dictionary<string, int>());
        return script;
    }

    private static void WithTemporaryJusticeDirectory(Action<string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeProfiles-" + Guid.NewGuid().ToString("N"));
        string previous = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", directory);
            test(directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", previous);
            string fullDirectory = Path.GetFullPath(directory);
            string fullTemp = Path.GetFullPath(Path.GetTempPath());
            if (fullDirectory.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullDirectory))
            {
                DeleteTemporaryDirectoryAfterJusticeWriterStops(fullDirectory);
            }
        }
    }

    private static void DeleteTemporaryDirectoryAfterJusticeWriterStops(string directory)
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
        Assert.IsNotNull(profile, "Le profil v2 actif doit être l'unique autorité.");
        return profile;
    }

    private static XDocument ConvertJusticeV2ToLegacyV1(XDocument document)
    {
        Assert.IsNotNull(document);
        Assert.IsNotNull(document.Root);
        if (document.Root.Element("Profiles") == null)
        {
            return new XDocument(document);
        }

        XElement active = GetPersistedActiveJusticeProfile(document);
        XElement recovery = document.Root.Element("RuntimeRecovery");
        XElement root = new XElement(
            "JusticeState",
            new XAttribute("version", "1"),
            new XAttribute("enabled", (string)active.Element("Case").Attribute("enabled")),
            new XAttribute(
                "policeIntegrationMode",
                (string)recovery.Attribute("policeIntegrationMode") ?? "1"),
            new XAttribute(
                "activePlayerSlot",
                (string)recovery.Attribute("activePlayerSlot")),
            new XAttribute(
                "nextIdentityGeneration",
                (string)recovery.Attribute("nextIdentityGeneration") ?? "0"),
            CopyLegacyProfileAttribute(active, "pendingDeathCapture", "false"),
            CopyLegacyProfileAttribute(active, "pendingDeathCapturePlayerSlot", "-1"),
            CopyLegacyProfileAttribute(active, "pendingDeathCapturePlayerModel", "0"),
            CopyLegacyProfileAttribute(active, "pendingAmnestyWantedClear", "false"),
            CopyLegacyProfileAttribute(active, "pendingLegalReleaseFinalization", "false"),
            CopyLegacyProfileAttribute(active, "pendingLegalReleaseSite", "0"),
            CopyLegacyProfileAttribute(active, "pendingLegalReleaseSelectedWeapon", "0"),
            new XAttribute("lastCanonicalPlayerSlot", (string)active.Attribute("slot")),
            CopyLegacyProfileAttribute(active, "lastCanonicalPlayerModel", "0"),
            new XElement(active.Element("Case")),
            new XElement(active.Element("Record")),
            new XElement(active.Element("Custody")),
            new XElement(
                "PlayerProfiles",
                document.Root.Element("Profiles").Elements("Profile").Select(
                    profile => new XElement(profile))));
        return new XDocument(root);
    }

    private static XAttribute CopyLegacyProfileAttribute(
        XElement profile,
        string name,
        string fallback)
    {
        return new XAttribute(name, (string)profile.Attribute(name) ?? fallback);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        MethodInfo method = ScriptType
            .GetMethods(PrivateInstance)
            .Single(candidate => candidate.Name == name &&
                candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(target, arguments);
    }

    private static void SwitchProfileAndAwait(object script)
    {
        Assert.IsFalse(
            (bool)Invoke(
                script,
                "EnsureJusticeProfileMatchesCanonicalPlayer",
                new object[] { null }),
            "Le switch doit garder le nouveau contexte bloqué pendant l'enfilement.");
        AwaitQueuedPersistence(script);
        Assert.IsTrue(
            (bool)Invoke(
                script,
                "EnsureJusticeProfileMatchesCanonicalPlayer",
                new object[] { null }),
            "Le switch doit s'achever uniquement après confirmation de DiskRevision.");
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

    private static JusticeCustodyPersistenceSnapshot RequireTypedCustodySnapshot(
        JusticePlayerProfileState profile)
    {
        Assert.IsNotNull(profile);
        Assert.IsNotNull(
            profile.CustodySnapshot,
            "Le profil runtime doit exposer son snapshot de détention typé.");
        return profile.CustodySnapshot;
    }

    private sealed class SwitchFailureAtomicFileStore : IJusticeAtomicFileStore, IDisposable
    {
        private readonly JusticeAtomicFileStore _inner = new JusticeAtomicFileStore();
        private volatile bool _failWrites = true;

        internal ManualResetEvent Attempted { get; } = new ManualResetEvent(false);

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            Attempted.Set();
            if (_failWrites)
            {
                throw new IOException("Echec writer déterministe pour le switch de profil.");
            }

            _inner.WriteAtomically(
                targetPath,
                backupPath,
                document,
                faultInjector);
        }

        public byte[] ReadAllBytes(string path)
        {
            return _inner.ReadAllBytes(path);
        }

        internal void AllowWrites()
        {
            _failWrites = false;
        }

        public void Dispose()
        {
            Attempted.Dispose();
        }
    }

    private static object InvokeStatic(string name, params object[] arguments)
    {
        MethodInfo method = ScriptType
            .GetMethods(PrivateStatic)
            .Single(candidate => candidate.Name == name &&
                candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(null, arguments);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateInstance);
        Assert.IsNotNull(field, "Champ runtime absent : " + name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateInstance);
        Assert.IsNotNull(field, "Champ runtime absent : " + name);
        return (T)field.GetValue(target);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GTA5modDEV.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du dépôt.");
        return string.Empty;
    }
}
