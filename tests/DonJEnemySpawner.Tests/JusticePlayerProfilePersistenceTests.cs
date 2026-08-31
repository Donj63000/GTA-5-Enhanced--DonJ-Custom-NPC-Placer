using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
            ConfigureConsistentActiveCase(
                profiles[0].CaseState,
                "paused-v2-roundtrip",
                42,
                2400L,
                180);
            profiles[0].CaseState.Enabled = false;
            profiles[0].CaseState.HasWarrant = true;
            profiles[0].CaseState.Phase = JusticePhase.Wanted;
            object writer = CreateHeadlessScript(profiles, 0);

            Assert.AreEqual(
                "Désactivée · dossier conservé",
                (string)Invoke(writer, "GetJusticeStatusDisplay"));

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
            Assert.AreEqual(42, loaded[0].CaseState.ActiveScore);
            Assert.AreEqual(2400L, loaded[0].CaseState.FineDue);
            Assert.AreEqual(180, loaded[0].CaseState.SentenceSeconds);
            Assert.IsTrue(loaded[0].CaseState.HasWarrant);
            Assert.AreEqual(JusticePhase.Wanted, loaded[0].CaseState.Phase);
            Assert.AreEqual("episode:paused-v2-roundtrip", loaded[0].CaseState.WantedEpisodeId);
            Assert.AreEqual("Agression test", loaded[0].CaseState.LastCrimeLabel);
            Assert.AreEqual(1, loaded[0].CaseState.Charges.Count);
            Assert.IsTrue(loaded[1].CaseState.Enabled);
            SetField(reader, "_justiceMenuSelectedProfileSlot", 0);
            Assert.AreEqual(
                "Désactivée · dossier conservé",
                (string)Invoke(reader, "GetJusticeMenuSelectedStatusDisplay"));
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

            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            SwitchProfileAndAwait(reader);

            JusticePlayerProfileState[] loaded =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(1, GetField<int>(reader, "_justiceActivePlayerProfileSlot"));
            Assert.AreSame(loaded[1].CaseState, GetField<JusticeCaseState>(reader, "_justiceCaseState"));
            Assert.IsTrue(loaded[0].CanAdvanceCustodyInBackground);
            Assert.AreEqual(0, RequireTypedCustodySnapshot(loaded[0]).Cooldowns.Count);
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
    public void PlayerProfiles_LegacyAmnestyMigrationClearsEveryProfileAndPreservesCase()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        ConfigureConsistentActiveCase(
            profiles[0].CaseState,
            "legacy-amnesty-migration",
            25,
            1500L,
            120);
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            profiles[slot].PendingAmnestyWantedClear = true;
        }

        object script = CreateHeadlessScript(profiles, 0);
        InitializeProfileResetRuntimeCollections(script);
        SetField(script, "_justiceAmnestyPending", true);
        SetField(script, "_justiceAmnestyWantedClearAttempted", true);
        SetField(script, "_justiceAmnestyPrecommitRedundant", true);
        SetField(script, "_justiceWantedClearPending", true);
        SetField(script, "_justiceNextWantedClearRetryAtMs", 1200L);
        SetField(script, "_justiceWantedClearRetryUntilMs", 9000L);

        Assert.IsTrue((bool)Invoke(script, "MigrateLegacyJusticeAmnestyState"));
        Invoke(script, "NormalizeLoadedJusticeState");

        Assert.IsFalse(GetField<bool>(script, "_justiceAmnestyPending"));
        Assert.IsFalse(GetField<bool>(script, "_justiceAmnestyWantedClearAttempted"));
        Assert.IsFalse(GetField<bool>(script, "_justiceAmnestyPrecommitRedundant"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWantedClearPending"));
        Assert.AreEqual(0L, GetField<long>(script, "_justiceNextWantedClearRetryAtMs"));
        Assert.AreEqual(0L, GetField<long>(script, "_justiceWantedClearRetryUntilMs"));
        Assert.IsTrue(
            GetField<bool>(script, "_justiceStateDirty"),
            "La normalisation de démarrage doit conserver la migration à réécrire.");
        Assert.IsTrue(profiles.All(profile => !profile.PendingAmnestyWantedClear));
        Assert.IsTrue(profiles[0].CaseState.Enabled);
        Assert.AreEqual(25, profiles[0].CaseState.ActiveScore);
        Assert.AreEqual(1500L, profiles[0].CaseState.FineDue);
        Assert.AreEqual(120, profiles[0].CaseState.SentenceSeconds);
        Assert.IsFalse(
            (bool)Invoke(script, "MigrateLegacyJusticeAmnestyState"),
            "La migration doit devenir idempotente après neutralisation.");
    }

    [TestMethod]
    public void PlayerProfiles_LegacyAmnestyV2ReloadMigratesAndPersistsWithoutWantedSideEffects()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            JusticeCaseState originalCase = profiles[0].CaseState;
            ConfigureConsistentActiveCase(
                originalCase,
                "legacy-amnesty-v2-reload",
                31,
                1700L,
                90);
            originalCase.HasWarrant = true;
            originalCase.Phase = JusticePhase.Wanted;
            for (int slot = 0; slot < profiles.Length; slot++)
            {
                profiles[slot].PendingAmnestyWantedClear = true;
            }

            object writer = CreateHeadlessScript(profiles, 0);
            SetField(writer, "_justiceAmnestyPending", true);
            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            XDocument legacySnapshot = XDocument.Load(path);
            Assert.AreEqual("2", (string)legacySnapshot.Root.Attribute("schemaMajor"));
            Assert.IsTrue(
                legacySnapshot.Root
                    .Element("Profiles")
                    .Elements("Profile")
                    .All(profile => string.Equals(
                        (string)profile.Attribute("pendingAmnestyWantedClear"),
                        "true",
                        StringComparison.Ordinal)),
                "Le snapshot v2 de départ doit reproduire les anciens verrous d'amnistie.");
            Invoke(writer, "ShutdownJusticePersistenceServices");

            int wantedObservations = 0;
            int wantedWrites = 0;
            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(
                reader,
                "_justiceWantedClearObservationOverride",
                new Func<int?>(() => { wantedObservations++; return 0; }));
            SetField(
                reader,
                "_justiceWantedWriteOverride",
                new Func<int, bool>(wanted => { wantedWrites++; return true; }));

            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));
            JusticePlayerProfileState[] migrated =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            JusticeCaseState migratedCase = migrated[0].CaseState;

            Assert.IsFalse(GetField<bool>(reader, "_justiceAmnestyPending"));
            Assert.IsTrue(migrated.All(profile => !profile.PendingAmnestyWantedClear));
            Assert.IsTrue(migratedCase.Enabled);
            Assert.AreEqual(31, migratedCase.ActiveScore);
            Assert.AreEqual(1700L, migratedCase.FineDue);
            Assert.AreEqual(90, migratedCase.SentenceSeconds);
            Assert.IsTrue(migratedCase.HasWarrant);
            Assert.AreEqual(JusticePhase.Wanted, migratedCase.Phase);
            Assert.AreEqual(
                "episode:legacy-amnesty-v2-reload",
                migratedCase.WantedEpisodeId);
            Assert.AreEqual(1, migratedCase.Charges.Count);
            Assert.AreEqual("Agression test", migratedCase.LastCrimeLabel);
            Assert.AreEqual(0, wantedObservations);
            Assert.AreEqual(0, wantedWrites);

            // Je reproduis la seconde normalisation du démarrage pour garantir
            // que la migration reste marquée comme une réécriture obligatoire.
            Invoke(reader, "NormalizeLoadedJusticeState");
            Assert.IsTrue(
                GetField<bool>(reader, "_justiceStateDirty"),
                "La normalisation ne doit pas perdre la migration à persister.");
            Assert.AreEqual(0, wantedObservations);
            Assert.AreEqual(0, wantedWrites);

            FlushAndAwait(reader);
            Invoke(reader, "ShutdownJusticePersistenceServices");

            XDocument migratedSnapshot = XDocument.Load(path);
            Assert.IsTrue(
                migratedSnapshot.Root
                    .Element("Profiles")
                    .Elements("Profile")
                    .All(profile => string.Equals(
                        (string)profile.Attribute("pendingAmnestyWantedClear"),
                        "false",
                        StringComparison.Ordinal)),
                "Tous les verrous legacy doivent être neutralisés sur disque.");

            object finalReader = CreateHeadlessScript(null, -1);
            SetField(finalReader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            SetField(
                finalReader,
                "_justiceWantedClearObservationOverride",
                new Func<int?>(() => { wantedObservations++; return 0; }));
            SetField(
                finalReader,
                "_justiceWantedWriteOverride",
                new Func<int, bool>(wanted => { wantedWrites++; return true; }));
            Assert.IsTrue((bool)Invoke(finalReader, "TryReadJusticeStateFile", path));

            JusticePlayerProfileState[] reloaded =
                GetField<JusticePlayerProfileState[]>(finalReader, "_justicePlayerProfiles");
            Assert.IsFalse(GetField<bool>(finalReader, "_justiceAmnestyPending"));
            Assert.IsTrue(reloaded.All(profile => !profile.PendingAmnestyWantedClear));
            Assert.AreEqual(31, reloaded[0].CaseState.ActiveScore);
            Assert.AreEqual(1700L, reloaded[0].CaseState.FineDue);
            Assert.AreEqual(90, reloaded[0].CaseState.SentenceSeconds);
            Assert.IsTrue(reloaded[0].CaseState.HasWarrant);
            Assert.AreEqual(JusticePhase.Wanted, reloaded[0].CaseState.Phase);
            Assert.AreEqual(0, wantedObservations);
            Assert.AreEqual(0, wantedWrites);
            Assert.IsFalse(
                (bool)Invoke(finalReader, "MigrateLegacyJusticeAmnestyState"),
                "Le fichier réécrit ne doit plus contenir de verrou à migrer.");
        });
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
    public void PlayerProfiles_InactiveResetRejectsAnOpenDeathFrontEvenBeforeItsLatchIsApplied()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeWalRecord prepared = CreateDeathFrontRecord(
                "death-front:inactive-reset-guard",
                "PoliceCapture",
                1,
                JusticeWalState.Prepared,
                4L,
                4L,
                string.Empty,
                0,
                1,
                2000,
                1,
                2000);
            wal.Append(prepared);
            wal.Append(CopyWalRecord(prepared, JusticeWalState.Attempted, 4L));

            Assert.IsFalse(profiles[1].PendingDeathCapture);
            Assert.IsTrue((bool)Invoke(
                script,
                "HasOpenJusticeDeathFrontForProfileSlot",
                1));
            Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));

            wal.Append(CopyWalRecord(prepared, JusticeWalState.Ambiguous, 5L));
            Assert.IsFalse((bool)Invoke(script, "ResetJusticePlayerProfile", 1));

            wal.Append(CopyWalRecord(prepared, JusticeWalState.Confirmed, 5L));
            Assert.IsFalse((bool)Invoke(
                script,
                "HasOpenJusticeDeathFrontForProfileSlot",
                1));
            Assert.IsTrue((bool)Invoke(script, "ResetJusticePlayerProfile", 1));
            Assert.AreEqual(0, profiles[0].CaseState.SentenceSeconds);
        });
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
    public void PlayerProfiles_LegacyV1ResetsTheProvenCanonicalSlotAndKeepsEnabledOnly()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            ConfigureConsistentActiveCase(
                profiles[1].CaseState,
                "paused-v1-migration",
                34,
                1800L,
                90);
            profiles[1].CaseState.Enabled = false;
            profiles[1].CaseState.HasWarrant = true;
            profiles[1].CaseState.Phase = JusticePhase.Wanted;
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
            Assert.AreEqual(0, migrated[1].RecordState.RecidivismIndex);
            Assert.AreEqual(0, migrated[2].RecordState.RecidivismIndex);
            Assert.IsFalse(migrated[1].CaseState.Enabled);
            Assert.AreEqual(0, migrated[1].CaseState.ActiveScore);
            Assert.AreEqual(0L, migrated[1].CaseState.FineDue);
            Assert.AreEqual(0, migrated[1].CaseState.SentenceSeconds);
            Assert.IsFalse(migrated[1].CaseState.HasWarrant);
            Assert.AreEqual(JusticePhase.AtLarge, migrated[1].CaseState.Phase);
            Assert.AreEqual(string.Empty, migrated[1].CaseState.WantedEpisodeId);
            Assert.AreEqual(string.Empty, migrated[1].CaseState.LastCrimeLabel);
            Assert.AreEqual(0, migrated[1].CaseState.Charges.Count);
            Assert.AreEqual(
                2,
                GetField<int>(reader, "_justiceSentencePolicyVersion"));

            FlushAndAwait(reader);
            XDocument migratedXml = XDocument.Load(path);
            Assert.AreEqual(
                3,
                migratedXml.Root.Element("Profiles").Elements("Profile").Count());
            Assert.AreEqual(
                "1",
                (string)migratedXml.Root.Element("RuntimeRecovery").Attribute("activePlayerSlot"));
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
    public void PlayerProfiles_JusticeToggleIsNotDangerousAndResetConfirmationKeepsItsTarget()
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

            object toggleAction = Enum.Parse(actionType, "JusticeEnabled");
            Assert.IsFalse((bool)InvokeStatic("IsDangerAction", toggleAction));
            Assert.AreEqual(
                -1,
                (int)Invoke(script, "GetDangerActionJusticeProfileSlot", toggleAction));
            Invoke(
                script,
                "RequestDangerConfirmation",
                toggleAction);
            Assert.IsNull(GetField<object>(script, "_pendingDangerAction"));
            Assert.AreEqual(25, profiles[0].CaseState.ActiveScore);
            Assert.AreEqual(1500L, profiles[0].CaseState.FineDue);

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
    public void PlayerProfiles_ResetWalReplaysAfterTheFirstResultWriteFails()
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
                0,
                GetField<JusticeRecordState>(script, "_justiceRecordState").RecidivismIndex);
            Assert.AreEqual(durableBeforeReset, File.ReadAllText(path));
            Assert.IsNotNull(GetField<JusticeWalRecord>(
                script,
                "_justicePendingProfileResetWalRecord"));
            Invoke(script, "ShutdownJusticePersistenceServices");

            object afterCrash = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(afterCrash);
            SetField(afterCrash, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
            Assert.AreEqual(
                2,
                GetField<JusticeRecordState>(afterCrash, "_justiceRecordState").RecidivismIndex);
            Invoke(afterCrash, "InitializeJusticePersistenceServices");
            Assert.AreEqual(
                0,
                GetField<JusticeRecordState>(afterCrash, "_justiceRecordState").RecidivismIndex,
                "Attempted doit réappliquer le reset même si aucun XML résultat n'existait au crash.");
            Assert.IsFalse((bool)Invoke(
                afterCrash,
                "TryResumePendingJusticeProfileResetWal"));
            AwaitQueuedPersistence(afterCrash);
            Assert.IsFalse((bool)Invoke(
                afterCrash,
                "TryResumePendingJusticeProfileResetWal"));
            AwaitQueuedPersistence(afterCrash);
            Assert.IsTrue((bool)Invoke(
                afterCrash,
                "TryResumePendingJusticeProfileResetWal"));
            foreach (string persistedPath in new[] { path, path + ".bak" })
            {
                Assert.AreEqual(
                    "0",
                    (string)GetPersistedActiveJusticeProfile(
                            XDocument.Load(persistedPath))
                        .Element("Record")
                        .Attribute("recidivism"));
            }
            Invoke(afterCrash, "ShutdownJusticePersistenceServices");
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
            Assert.IsFalse((bool)Invoke(
                script,
                "TryResumePendingJusticeProfileResetWal"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "TryResumePendingJusticeProfileResetWal"));

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

            Assert.IsFalse((bool)Invoke(
                script,
                "TryResumePendingJusticeProfileResetWal"),
                "La seconde rotation injectée en échec doit garder le WAL ouvert.");

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
            Assert.IsFalse((bool)Invoke(
                script,
                "TryResumePendingJusticeProfileResetWal"));
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "TryResumePendingJusticeProfileResetWal"));
            Assert.AreEqual(
                "0",
                (string)GetPersistedActiveJusticeProfile(XDocument.Load(path + ".bak"))
                    .Element("Record")
                    .Attribute("recidivism"));
        });
    }

    [TestMethod]
    public void ProfileReset_LatestWinsSkippedEmptyCandidateRequiresTwoExactDiskCopies()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            string statePath = Path.Combine(directory, "_justice_state.xml");
            using (FirstWriteBlockingAtomicFileStore store =
                new FirstWriteBlockingAtomicFileStore())
            {
                JusticeRepository repository = new JusticeRepository(
                    statePath,
                    statePath + ".bak",
                    new JusticeXmlPersistenceCodec(),
                    0L,
                    store,
                    JusticeNoOpPersistenceFaultInjector.Instance,
                    10);
                JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                    Path.Combine(directory, "_justice_state.wal"));
                repository.Start();
                SetField(script, "_justiceRepository", repository);
                SetField(script, "_justiceWriteAheadLog", wal);
                try
                {
                    Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
                    Assert.IsTrue(
                        store.FirstWriteStarted.WaitOne(TimeSpan.FromSeconds(5)),
                        "Le snapshot ancien doit occuper le writer avant le reset.");

                    Assert.IsTrue((bool)Invoke(
                        script,
                        "BeginJusticeProfileResetWalTransaction",
                        0));
                    JusticeWalRecord attempted = wal.GetOpenTransactions().Single();
                    Assert.AreEqual(JusticeWalState.Attempted, attempted.State);
                    long skippedEmptyCandidate = GetField<Dictionary<string, long>>(
                        script,
                        "_justiceProfileResetResultCandidates")[attempted.TransactionId];

                    // Je remplace le candidat vide encore en attente par un état
                    // non vide afin de reproduire exactement le latest-wins.
                    profiles[0].CaseState.Enabled = true;
                    profiles[0].CaseState.LastCrimeKind =
                        JusticeCrimeKind.ReportedViolentAct;
                    profiles[0].CaseState.LastCrimeLabel =
                        "Mutation coalescée après le reset";
                    SetField(script, "_justiceEnabled", true);
                    Invoke(script, "JusticeMarkStateDirty");
                    Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
                    long nonEmptyRevision = repository.GetDiagnostics().PendingRevision;
                    Assert.IsTrue(nonEmptyRevision > skippedEmptyCandidate);
                    Assert.AreEqual(
                        skippedEmptyCandidate,
                        GetField<Dictionary<string, long>>(
                            script,
                            "_justiceProfileResetResultCandidates")[
                                attempted.TransactionId],
                        "Le snapshot non vide ne doit jamais devenir une preuve du reset.");

                    store.ReleaseFirstWrite.Set();
                    AwaitQueuedPersistence(script);
                    Assert.AreEqual(
                        JusticeWalState.Attempted,
                        wal.GetLatest(attempted.TransactionId).State,
                        "Une révision plus récente ne qualifie pas le candidat vide sauté.");
                    Assert.IsFalse(PersistedJusticeProfileContainsExactReset(
                        statePath,
                        0));

                    Assert.IsTrue((bool)Invoke(
                        script,
                        "ReplaceJusticePlayerProfileWithEmptyState",
                        0));
                    FlushAndAwait(script);
                    Assert.AreEqual(
                        JusticeWalState.Ambiguous,
                        wal.GetLatest(attempted.TransactionId).State,
                        "Le premier snapshot vide exact ne prouve encore que le primaire.");
                    Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                        statePath,
                        0));
                    Assert.IsFalse(PersistedJusticeProfileContainsExactReset(
                        statePath + ".bak",
                        0));

                    FlushAndAwait(script);
                    Assert.AreEqual(
                        JusticeWalState.Confirmed,
                        wal.GetLatest(attempted.TransactionId).State,
                        "Le WAL ne se ferme qu'après une seconde rotation vide exacte.");
                    Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                        statePath,
                        0));
                    Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                        statePath + ".bak",
                        0));
                }
                finally
                {
                    store.ReleaseFirstWrite.Set();
                    repository.Stop(TimeSpan.FromSeconds(5));
                    repository.Dispose();
                }
            }
        });
    }

    [TestMethod]
    public void ProfileReset_AmbiguousRecoveryFromOldProfileRequiresTwoFreshRotations()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            string statePath = Path.Combine(directory, "_justice_state.xml");
            object writer = CreateHeadlessScript(CreateDistinctProfiles(), 0);
            InitializeProfileResetRuntimeCollections(writer);
            FlushAndAwait(writer);
            FlushAndAwait(writer);
            Invoke(writer, "ShutdownJusticePersistenceServices");

            object recovered = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(recovered);
            SetField(
                recovered,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(
                recovered,
                "TryReadJusticeStateFile",
                statePath));
            JusticePlayerProfileState[] loadedProfiles =
                GetField<JusticePlayerProfileState[]>(
                    recovered,
                    "_justicePlayerProfiles");
            Assert.IsFalse((bool)InvokeStatic(
                "IsJusticeProfileResetResultPresent",
                loadedProfiles[0]));
            long loadedRevision = GetField<long>(
                recovered,
                "_justicePersistenceRevision");
            long loadedGeneration = GetField<long[]>(
                recovered,
                "_justiceProfilePersistenceGenerations")[0];

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            JusticeWalRecord prepared = CreateProfileResetRecord(
                "profile-reset-result:ambiguous-old",
                0,
                JusticeWalState.Prepared,
                loadedRevision,
                loadedGeneration,
                loadedProfiles[0].LastCanonicalPlayerModel);
            wal.Append(prepared);
            wal.Append(CopyWalRecord(
                prepared,
                JusticeWalState.Attempted,
                loadedRevision));
            JusticeWalRecord ambiguous = wal.Append(CopyWalRecord(
                prepared,
                JusticeWalState.Ambiguous,
                loadedRevision));
            SetField(recovered, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                recovered,
                directory,
                loadedRevision);
            try
            {
                Invoke(
                    recovered,
                    "RecoverJusticeProfileResetFromWal",
                    ambiguous);
                Assert.IsTrue((bool)InvokeStatic(
                    "IsJusticeProfileResetResultPresent",
                    GetField<JusticePlayerProfileState[]>(
                        recovered,
                        "_justicePlayerProfiles")[0]));

                FlushAndAwait(recovered);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(prepared.TransactionId).State,
                    "Une seule rotation fraîche ne doit pas qualifier l'ancien backup.");
                Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                    statePath,
                    0));
                Assert.IsFalse(PersistedJusticeProfileContainsExactReset(
                    statePath + ".bak",
                    0));

                FlushAndAwait(recovered);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(prepared.TransactionId).State);
                Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                    statePath,
                    0));
                Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                    statePath + ".bak",
                    0));
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
                repository.Dispose();
            }
        });
    }

    [TestMethod]
    public void ProfileReset_AttemptedAlreadyInPrimaryUsesLoadedRevisionThenRepairsBackup()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            string statePath = Path.Combine(directory, "_justice_state.xml");
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object writer = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(writer);
            FlushAndAwait(writer);
            long baseRevision = GetField<long>(writer, "_justicePersistenceRevision");
            long baseGeneration = GetField<long[]>(
                writer,
                "_justiceProfilePersistenceGenerations")[0];
            JusticeWalRecord prepared = CreateProfileResetRecord(
                "profile-reset-result:attempted-primary",
                0,
                JusticeWalState.Prepared,
                baseRevision,
                baseGeneration,
                profiles[0].LastCanonicalPlayerModel);

            Invoke(writer, "ApplyJusticeProfileResetWalResult", prepared);
            FlushAndAwait(writer);
            long resetPrimaryRevision = GetField<long>(
                writer,
                "_justicePersistenceRevision");
            Invoke(writer, "ShutdownJusticePersistenceServices");
            Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                statePath,
                0));
            Assert.IsFalse(PersistedJusticeProfileContainsExactReset(
                statePath + ".bak",
                0));

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            wal.Append(prepared);
            wal.Append(CopyWalRecord(
                prepared,
                JusticeWalState.Attempted,
                baseRevision));

            object afterCrash = CreateHeadlessScript(null, -1);
            InitializeProfileResetRuntimeCollections(afterCrash);
            SetField(
                afterCrash,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(
                afterCrash,
                "TryReadJusticeStateFile",
                statePath));
            Assert.AreEqual(
                resetPrimaryRevision,
                GetField<long>(afterCrash, "_justicePersistenceRevision"));
            Invoke(afterCrash, "InitializeJusticePersistenceServices");
            try
            {
                JusticeWriteAheadLog recoveredWal =
                    GetField<JusticeWriteAheadLog>(
                        afterCrash,
                        "_justiceWriteAheadLog");
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    recoveredWal.GetLatest(prepared.TransactionId).State,
                    "Le primaire vide relu doit devenir la preuve exacte du résultat.");
                Assert.IsFalse((bool)Invoke(
                    afterCrash,
                    "TryResumePendingJusticeProfileResetWal"));
                AwaitQueuedPersistence(afterCrash);
                Assert.IsTrue((bool)Invoke(
                    afterCrash,
                    "TryResumePendingJusticeProfileResetWal"));
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    recoveredWal.GetLatest(prepared.TransactionId).State);
                Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                    statePath,
                    0));
                Assert.IsTrue(PersistedJusticeProfileContainsExactReset(
                    statePath + ".bak",
                    0));
            }
            finally
            {
                Invoke(afterCrash, "ShutdownJusticePersistenceServices");
            }
        });
    }

    [TestMethod]
    public void ProfileReset_PreparedSurvivesAttemptAndRejectFailuresUntilInMemoryRetry()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            WalOccurrenceFaultInjector injector = new WalOccurrenceFaultInjector(
                JusticePersistenceFaultPoint.BeforeWalFrameWrite,
                2,
                3);
            SetField(script, "_justiceWalFaultInjectorOverride", injector);
            try
            {
                Assert.IsFalse((bool)Invoke(
                    script,
                    "BeginJusticeProfileResetWalTransaction",
                    0));
                JusticeWalRecord pending = GetField<JusticeWalRecord>(
                    script,
                    "_justicePendingProfileResetWalRecord");
                Assert.IsNotNull(
                    pending,
                    "Prepared doit rester repris en mémoire si son rejet échoue aussi.");
                Assert.AreEqual(JusticeWalState.Prepared, pending.State);
                JusticeWriteAheadLog wal = GetField<JusticeWriteAheadLog>(
                    script,
                    "_justiceWriteAheadLog");
                Assert.AreEqual(
                    JusticeWalState.Prepared,
                    wal.GetLatest(pending.TransactionId).State);
                Assert.AreEqual(
                    2,
                    profiles[0].RecordState.RecidivismIndex,
                    "Prepared n'autorise jamais l'effet destructif.");

                injector.AllowWrites();
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryResumePendingJusticeProfileResetWal"));
                Assert.IsNull(GetField<JusticeWalRecord>(
                    script,
                    "_justicePendingProfileResetWalRecord"));
                Assert.AreEqual(
                    JusticeWalState.Rejected,
                    wal.GetLatest(pending.TransactionId).State,
                    "Le même runtime doit rejeter Prepared dès la levée de panne.");
                Assert.AreEqual(0, wal.GetOpenTransactions().Count);
            }
            finally
            {
                Invoke(script, "ShutdownJusticePersistenceServices");
            }
        });
    }

    [TestMethod]
    public void ProfileReset_ResultPredicateRejectsEveryNonCanonicalResidual()
    {
        JusticePlayerProfileState canonical = CreateCanonicalProfileResetResult(0);
        Assert.IsTrue((bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            canonical));

        JusticePlayerProfileState metadata = CreateCanonicalProfileResetResult(0);
        metadata.CaseState.ProcessedIncidentIds.Add("incident:résiduel");
        Assert.IsFalse((bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            metadata));

        JusticePlayerProfileState identity = CreateCanonicalProfileResetResult(0);
        identity.LastCanonicalPlayerModel = 123456;
        Assert.IsFalse((bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            identity));

        JusticePlayerProfileState pending = CreateCanonicalProfileResetResult(0);
        pending.PendingAmnestyWantedClear = true;
        Assert.IsFalse((bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            pending));

        JusticePlayerProfileState custody = CreateCanonicalProfileResetResult(0);
        custody.CustodyXml = "<Custody />";
        Assert.IsFalse((bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            custody));
    }

    [TestMethod]
    public void ProfileReset_OpenWalFreezesToggleAndLateJusticeRuntime()
    {
        JusticePlayerProfileState[] profiles =
        {
            CreateCanonicalProfileResetResult(0),
            CreateCanonicalProfileResetResult(1),
            CreateCanonicalProfileResetResult(2)
        };
        object script = CreateHeadlessScript(profiles, 0);
        SetField(script, "_justiceInitialized", true);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        JusticeWalRecord pending = CreateProfileResetRecord(
            "profile-reset-result:runtime-freeze",
            0,
            JusticeWalState.Attempted,
            0L,
            0L,
            0);
        SetField(script, "_justicePendingProfileResetWalRecord", pending);
        SetField(script, "_justiceNextIncidentProcessingAtMs", 987654L);

        Assert.IsTrue((bool)Invoke(script, "HasOpenJusticeProfileResetWal"));
        Invoke(script, "RequestJusticeToggle");
        Assert.IsFalse(GetField<bool>(script, "_justiceEnabled"));
        Assert.IsFalse(profiles[0].CaseState.Enabled);
        StringAssert.Contains(
            GetField<string>(script, "_statusText"),
            "réinitialisation");

#if DONJ_STUB_API
        // Je réserve l'exécution du pont GTA au stub configurable. L'assembly
        // NIB réel ne peut pas fournir Game.Player hors du processus du jeu.
        GTA.StubRuntime.Reset();
        Invoke(script, "UpdateJusticeSystem");
        Assert.AreEqual(
            987654L,
            GetField<long>(script, "_justiceNextIncidentProcessingAtMs"),
            "Le runtime tardif ne doit progresser aucun front pendant le WAL.");
#endif

        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        int updateStart = source.IndexOf(
            "private void UpdateJusticeSystem()",
            StringComparison.Ordinal);
        int updateEnd = source.IndexOf(
            "private void UpdateJusticeFailSafeMaintenance()",
            updateStart,
            StringComparison.Ordinal);
        Assert.IsTrue(updateStart >= 0 && updateEnd > updateStart);
        AssertOrdered(
            source.Substring(updateStart, updateEnd - updateStart),
            "if (!_justiceInitialized)",
            "Ped player = Game.Player.Character",
            "UpdateJusticeCustodyRespawnTransferMask(player)",
            "UpdateJusticePoliceDeathPreJudgmentHolding(player, nowRaw)",
            "if (HasOpenJusticeProfileResetWal())",
            "return;",
            "if (_justiceBackupRepairPending)");

        int toggleStart = source.IndexOf(
            "private void RequestJusticeToggle()",
            StringComparison.Ordinal);
        int toggleEnd = source.IndexOf(
            "private bool IsJusticePauseTemporarilyUnsafe()",
            toggleStart,
            StringComparison.Ordinal);
        Assert.IsTrue(toggleStart >= 0 && toggleEnd > toggleStart);
        AssertOrdered(
            source.Substring(toggleStart, toggleEnd - toggleStart),
            "if (!IsJusticePlayedProfileContextReady())",
            "if (HasOpenJusticeProfileResetWal())",
            "MigrateLegacyJusticeAmnestyState()",
            "bool targetEnabled = !_justiceEnabled");
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

        Assert.IsFalse(franklin.HasWarrant);
        Assert.AreEqual(JusticePhase.Surrendering, franklin.Phase);
        Assert.IsTrue(GetField<bool>(script, "_justiceArrestCompletionProbePending"));
        Assert.IsTrue(GetField<bool>(script, "_justiceWantedLossPending"));
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
    public void PlayerProfiles_RapidSecondSwitchWaitsForFirstDiskRevisionAndOneBoundaryTick()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            Invoke(script, "InitializeJusticePersistenceServices");
            Invoke(script, "ShutdownJusticePersistenceServices");

            FirstWriteBlockingAtomicFileStore store =
                new FirstWriteBlockingAtomicFileStore();
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
            int[] repairArrestHoldingIntents = { 111, 222, 333 };
            SetField(
                script,
                "_justiceRepairArrestPreJudgmentHoldingModelHashes",
                (int[])repairArrestHoldingIntents.Clone());

            try
            {
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.IsTrue(
                    store.FirstWriteStarted.WaitOne(TimeSpan.FromSeconds(5)),
                    "Le snapshot Q doit être retenu avant sa publication disque.");
                Assert.AreEqual(1, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));
                long qRevision = GetField<long>(
                    script,
                    "_justiceProfileSwitchPersistenceRevision");
                Assert.IsTrue(qRevision > 0L);

                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 2));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.AreEqual(
                    1,
                    GetField<int>(script, "_justiceActivePlayerProfileSlot"),
                    "R ne doit jamais écraser Q tant que sa révision n'est pas durable.");
                Assert.AreEqual(
                    qRevision,
                    GetField<long>(script, "_justiceProfileSwitchPersistenceRevision"),
                    "La barrière de Q doit garder exactement sa révision initiale.");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));

                store.ReleaseFirstWrite.Set();
                AwaitQueuedPersistence(script);

                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Le tick qui constate DiskRevision doit rester une frontière sans activation de R.");
                Assert.AreEqual(1, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));
                Assert.AreEqual(
                    0L,
                    GetField<long>(script, "_justiceProfileSwitchPersistenceRevision"));

                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Le tick suivant peut activer R, mais doit attendre sa propre publication.");
                Assert.AreEqual(2, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.AreSame(
                    profiles[2].CaseState,
                    GetField<JusticeCaseState>(script, "_justiceCaseState"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));
                CollectionAssert.AreEqual(
                    repairArrestHoldingIntents,
                    GetField<int[]>(
                        script,
                        "_justiceRepairArrestPreJudgmentHoldingModelHashes"));
                AwaitQueuedPersistence(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));
                CollectionAssert.AreEqual(
                    repairArrestHoldingIntents,
                    GetField<int[]>(
                        script,
                        "_justiceRepairArrestPreJudgmentHoldingModelHashes"),
                    "Les intents RepairArrest restent attachés à leurs slots après deux switches valides.");
            }
            finally
            {
                store.ReleaseFirstWrite.Set();
                repository.Stop(TimeSpan.FromSeconds(5));
                store.Dispose();
            }
        });
    }

    [TestMethod]
    public void PlayerProfiles_UnknownSlotCannotBypassPendingSwitchAndResumesAfterDurability()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            Invoke(script, "InitializeJusticePersistenceServices");
            Invoke(script, "ShutdownJusticePersistenceServices");

            FirstWriteBlockingAtomicFileStore store =
                new FirstWriteBlockingAtomicFileStore();
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

            try
            {
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.IsTrue(
                    store.FirstWriteStarted.WaitOne(TimeSpan.FromSeconds(5)),
                    "Le snapshot de Franklin doit être retenu pendant le slot transitoire.");

                JusticeCaseState activeCase = GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState");
                JusticeRecordState activeRecord = GetField<JusticeRecordState>(
                    script,
                    "_justiceRecordState");
                long pendingRevision = GetField<long>(
                    script,
                    "_justiceProfileSwitchPersistenceRevision");
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => -1));

                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Un slot inconnu ne doit pas contourner la publication encore en attente.");
                Assert.AreEqual(1, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.AreSame(activeCase, GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState"));
                Assert.AreSame(activeRecord, GetField<JusticeRecordState>(
                    script,
                    "_justiceRecordState"));
                Assert.AreEqual(
                    pendingRevision,
                    GetField<long>(script, "_justiceProfileSwitchPersistenceRevision"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));

                store.ReleaseFirstWrite.Set();
                AwaitQueuedPersistence(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Après DiskRevision, le dernier profil prouvé peut reprendre sous un ped sans slot.");
                Assert.AreEqual(1, GetField<int>(
                    script,
                    "_justiceActivePlayerProfileSlot"));
                Assert.AreSame(activeCase, GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));

                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Le retour du slot canonique déjà actif doit rester immédiat et stable.");
            }
            finally
            {
                store.ReleaseFirstWrite.Set();
                repository.Stop(TimeSpan.FromSeconds(5));
                store.Dispose();
            }
        });
    }

    [TestMethod]
    public void PlayerProfiles_FailedTargetCustodyActivationRestoresSourceAtomically()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[1].CustodyXml = profiles[1].CustodyXml.Replace(
                "site=\"None\"",
                "site=\"Inconnu\"");
            profiles[1].CaseState.Enabled = true;
            profiles[1].CaseState.Phase = JusticePhase.Incarcerated;
            profiles[1].CaseState.SentenceSeconds = 90;
            profiles[1].CanAdvanceCustodyInBackground = true;
            profiles[1].InactiveCustodyLastTickAt = -2000;
            profiles[1].InactiveCustodyElapsedRemainderMs = 500;

            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceStateDirty", true);
            SetField(script, "_justiceNextStateSaveAtMs", 12345L);
            int[] repairArrestHoldingIntents = { 111, 222, 333 };
            SetField(
                script,
                "_justiceRepairArrestPreJudgmentHoldingModelHashes",
                (int[])repairArrestHoldingIntents.Clone());
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 1));
            JusticeCaseState sourceCase = profiles[0].CaseState;
            JusticeRecordState sourceRecord = profiles[0].RecordState;

            try
            {
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.AreEqual(
                    0,
                    GetField<int>(script, "_justiceActivePlayerProfileSlot"),
                    "L'échec de restauration cible ne doit jamais laisser un demi-switch actif.");
                Assert.AreSame(sourceCase, GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState"));
                Assert.AreSame(sourceRecord, GetField<JusticeRecordState>(
                    script,
                    "_justiceRecordState"));
                Assert.AreEqual(sourceCase.Enabled, GetField<bool>(
                    script,
                    "_justiceEnabled"));
                Assert.AreEqual(90, profiles[1].CaseState.SentenceSeconds);
                Assert.IsTrue(profiles[1].CanAdvanceCustodyInBackground);
                Assert.AreEqual(-2000, profiles[1].InactiveCustodyLastTickAt);
                Assert.AreEqual(500, profiles[1].InactiveCustodyElapsedRemainderMs);
                Assert.IsTrue(GetField<bool>(script, "_justiceStateDirty"));
                Assert.AreEqual(12345L, GetField<long>(
                    script,
                    "_justiceNextStateSaveAtMs"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceProfileSwitchPersistencePending"));
                Assert.AreEqual(0L, GetField<long>(
                    script,
                    "_justiceProfileSwitchPersistenceRevision"));
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileSelectionPending"),
                    "Après rollback, un nouveau slot canonique doit encore prouver le propriétaire joué.");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceProfileContextBlocked"));
                CollectionAssert.AreEqual(
                    repairArrestHoldingIntents,
                    GetField<int[]>(
                        script,
                        "_justiceRepairArrestPreJudgmentHoldingModelHashes"),
                    "Les intents RepairArrest déjà prouvés doivent survivre bit pour bit au double RestoreCustody du rollback.");

                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => -1));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Le ped cible sans slot ne doit jamais rouvrir le dossier source restauré.");
                Assert.AreSame(sourceCase, GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState"));

                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }),
                    "Le retour canonique du héros source doit lever proprement la sélection pending.");
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceProfileSelectionPending"));
            }
            finally
            {
                Invoke(script, "ShutdownJusticePersistenceServices");
            }
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
            // Je garde Attempted ouvert jusqu'à ce que le snapshot du profil
            // entrant rende le résultat Ambiguous, puis la rotation suivante
            // Confirmed. Rejected annulerait à tort une restauration déjà faite.
            Assert.IsFalse(
                policeRecord != null &&
                policeRecord.State == JusticeWalState.Rejected,
                "La restauration police déjà exécutée ne doit jamais être annulée lors du basculement de profil.");
            Assert.IsTrue(
                policeRecord == null ||
                policeRecord.State == JusticeWalState.Attempted ||
                policeRecord.State == JusticeWalState.Ambiguous ||
                policeRecord.State == JusticeWalState.Confirmed,
                "La restauration police doit être tentée, acquittée ou déjà compactée après le basculement.");

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
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeCustodyDeathStateBeforeRespawn", 2000));
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathStatePersistencePending"));
            Assert.IsTrue(GetField<long>(script, "_justiceCustodyDeathPersistenceRevision") > 0L);
            Assert.AreEqual(2, attempts);
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(script, "PersistJusticeCustodyDeathStateBeforeRespawn", 2001));
            Assert.IsFalse(GetField<bool>(script, "_justiceCustodyDeathStatePersistencePending"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCustodyDeathPersistenceRevision"));

            XDocument durable = XDocument.Load(Path.Combine(directory, "_justice_state.xml"));
            XElement custody = GetPersistedActiveJusticeProfile(durable).Element("Custody");
            Assert.AreEqual("true", (string)custody.Attribute("waitingForRespawn"));
            Assert.AreEqual("true", (string)custody.Attribute("deathRebindPending"));
        });
    }

    [TestMethod]
    public void CustodyDeath_AsyncWriterFailureKeepsTheRebindBlockedUntilARetryIsDurable()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
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
            SetField(script, "_justiceCustodyWaitingForRespawn", true);
            SetField(script, "_justiceCustodyDeathRebindPending", true);
            SetField(script, "_justiceCustodyDeathStatePersistencePending", true);

            try
            {
                Assert.IsFalse((bool)Invoke(
                    script,
                    "PersistJusticeCustodyDeathStateBeforeRespawn",
                    1000));
                long rejectedRevision = GetField<long>(
                    script,
                    "_justiceCustodyDeathPersistenceRevision");
                Assert.IsTrue(rejectedRevision > 0L);
                Assert.IsTrue(
                    store.Attempted.WaitOne(TimeSpan.FromSeconds(5)),
                    "Le writer fautif doit recevoir le checkpoint de décès.");
                Assert.IsTrue(
                    SpinWait.SpinUntil(
                        delegate()
                        {
                            return repository.GetDiagnostics().WriteFailures > 0L;
                        },
                        TimeSpan.FromSeconds(5)),
                    "L'échec asynchrone doit être visible avant le retry.");

                int retryAt = GetField<int>(
                    script,
                    "_justiceNextCustodyDeathPersistenceRetryAt");
                Assert.IsFalse((bool)Invoke(
                    script,
                    "PersistJusticeCustodyDeathStateBeforeRespawn",
                    retryAt - 1));
                Assert.AreEqual(
                    rejectedRevision,
                    GetField<long>(script, "_justiceCustodyDeathPersistenceRevision"),
                    "Le rebind doit rester lié à la révision rejetée avant l'échéance.");

                Assert.IsFalse((bool)Invoke(
                    script,
                    "PersistJusticeCustodyDeathStateBeforeRespawn",
                    retryAt));
                long retryRevision = GetField<long>(
                    script,
                    "_justiceCustodyDeathPersistenceRevision");
                Assert.IsTrue(
                    retryRevision > rejectedRevision,
                    "Le retry doit produire une nouvelle révision traçable.");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justiceCustodyDeathStatePersistencePending"));

                store.AllowWrites();
                AwaitQueuedPersistence(script);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "PersistJusticeCustodyDeathStateBeforeRespawn",
                    retryAt + 1));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justiceCustodyDeathStatePersistencePending"));

                XDocument durable = XDocument.Load(
                    Path.Combine(directory, "_justice_state.xml"));
                XElement custody = GetPersistedActiveJusticeProfile(durable)
                    .Element("Custody");
                Assert.AreEqual("true", (string)custody.Attribute("waitingForRespawn"));
                Assert.AreEqual("true", (string)custody.Attribute("deathRebindPending"));
            }
            finally
            {
                store.AllowWrites();
                Invoke(script, "ShutdownJusticePersistenceServices");
                store.Dispose();
            }
        });
    }

    [TestMethod]
    public void CustodyDeath_RejectsAPreparedBarrierBeforePersistingTheRespawnRight()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            AssertCurrentCustodyFragmentIsValid(script, profiles[0]);
            Invoke(script, "InitializeJusticePersistenceServices");

            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            const string transactionId = "custody-death:prepared-barrier";
            long createdAt = DateTime.UtcNow.Ticks;
            JusticePersistenceField[] fields =
            {
                new JusticePersistenceField("snapshotRevision", "1"),
                new JusticePersistenceField("profileGeneration", "1"),
                new JusticePersistenceField("identityKey", "slot:0:model:1000"),
                new JusticePersistenceField("boundary", "InventoryConfiscation"),
                new JusticePersistenceField(
                    "schemaMajor",
                    JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                        CultureInfo.InvariantCulture))
            };
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            SetField(script, "_justiceCriticalBarrierCaller", "InventoryConfiscation");
            SetField(script, "_justiceCriticalBarrierOperationKind", "Inventory");
            SetField(script, "_justiceCriticalBarrierTransactionId", transactionId);
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:1000");
            SetField(script, "_justiceCriticalBarrierRevision", 1L);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 1L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", createdAt);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);
            SetField(script, "_justiceCustodyWaitingForRespawn", true);
            SetField(script, "_justiceCustodyDeathRebindPending", true);
            SetField(script, "_justiceCustodyDeathStatePersistencePending", true);

            Assert.IsFalse((bool)Invoke(
                script,
                "PersistJusticeCustodyDeathStateBeforeRespawn",
                1000));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyDeathStatePersistencePending"));
            Assert.IsTrue(GetField<long>(
                script,
                "_justiceCustodyDeathPersistenceRevision") > 0L);
            Assert.AreEqual(
                JusticeWalState.Rejected,
                wal.GetLatest(transactionId).State,
                "La frame sans effet doit être terminale avant le checkpoint de mort.");

            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)Invoke(
                script,
                "PersistJusticeCustodyDeathStateBeforeRespawn",
                1001));
            Assert.IsFalse(GetField<bool>(
                script,
                "_justiceCustodyDeathStatePersistencePending"));
            XDocument durable = XDocument.Load(
                Path.Combine(directory, "_justice_state.xml"));
            XElement custody = GetPersistedActiveJusticeProfile(durable)
                .Element("Custody");
            Assert.AreEqual("true", (string)custody.Attribute("waitingForRespawn"));
            Assert.AreEqual("true", (string)custody.Attribute("deathRebindPending"));
        });
    }

    [TestMethod]
    public void CustodyDeath_ClearsAnUnmaterializedBarrierWithoutResettingTransferLatches()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            Invoke(script, "InitializeJusticePersistenceServices");
            SetField(script, "_justiceCriticalBarrierCaller", "CompleteJusticeCustodyTransfer");
            SetField(script, "_justiceCriticalBarrierOperationKind", "Transfer");
            SetField(script, "_justiceCriticalBarrierTransactionId", "custody-death:not-yet-prepared");
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:1000");
            SetField(script, "_justiceCriticalBarrierRevision", 1L);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 1L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", DateTime.UtcNow.Ticks);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);
            SetField(script, "_justiceCustodyTransferPrecommitConfirmed", true);
            SetField(script, "_justiceCustodyFallbackPrecommitPending", true);

            Assert.IsTrue((bool)Invoke(
                script,
                "TryRejectJusticeCriticalBarrierBeforeCustodyDeath"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferPrecommitConfirmed"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyFallbackPrecommitPending"));
            Assert.AreEqual(
                0,
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog")
                    .GetOpenTransactions()
                    .Count);
        });
    }

    [TestMethod]
    public void CustodyDeath_RejectsAnAttemptedBarrierWhoseAcknowledgementWasLost()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            Invoke(script, "InitializeJusticePersistenceServices");
            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            const string transactionId = "custody-death:attempted-barrier";
            long createdAt = DateTime.UtcNow.Ticks;
            JusticePersistenceField[] fields =
            {
                new JusticePersistenceField("snapshotRevision", "1"),
                new JusticePersistenceField("profileGeneration", "1"),
                new JusticePersistenceField("identityKey", "slot:0:model:1000"),
                new JusticePersistenceField("boundary", "InventoryConfiscation"),
                new JusticePersistenceField(
                    "schemaMajor",
                    JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                        CultureInfo.InvariantCulture))
            };
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Attempted,
                1L,
                createdAt,
                fields));
            SetField(script, "_justiceCriticalBarrierCaller", "InventoryConfiscation");
            SetField(script, "_justiceCriticalBarrierOperationKind", "Inventory");
            SetField(script, "_justiceCriticalBarrierTransactionId", transactionId);
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:1000");
            SetField(script, "_justiceCriticalBarrierRevision", 1L);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 1L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", createdAt);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);

            Assert.IsTrue((bool)Invoke(
                script,
                "TryRejectJusticeCriticalBarrierBeforeCustodyDeath"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            Assert.AreEqual(
                JusticeWalState.Rejected,
                wal.GetLatest(transactionId).State);
        });
    }

    [TestMethod]
    public void CustodyDeath_DoesNotDiscardAnAmbiguousBarrierWhoseEffectMayHaveStarted()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            Invoke(script, "InitializeJusticePersistenceServices");
            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            const string transactionId = "custody-death:ambiguous-barrier";
            long createdAt = DateTime.UtcNow.Ticks;
            JusticePersistenceField[] fields =
            {
                new JusticePersistenceField("snapshotRevision", "1"),
                new JusticePersistenceField("profileGeneration", "1"),
                new JusticePersistenceField("identityKey", "slot:0:model:1000"),
                new JusticePersistenceField("boundary", "InventoryConfiscation"),
                new JusticePersistenceField(
                    "schemaMajor",
                    JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                        CultureInfo.InvariantCulture))
            };
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Prepared,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Attempted,
                1L,
                createdAt,
                fields));
            wal.Append(new JusticeWalRecord(
                transactionId,
                "Inventory",
                0,
                JusticeWalState.Ambiguous,
                2L,
                createdAt,
                fields));
            SetField(script, "_justiceCriticalBarrierCaller", "InventoryConfiscation");
            SetField(script, "_justiceCriticalBarrierOperationKind", "Inventory");
            SetField(script, "_justiceCriticalBarrierTransactionId", transactionId);
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:1000");
            SetField(script, "_justiceCriticalBarrierRevision", 1L);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 1L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", createdAt);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);

            Assert.IsFalse((bool)Invoke(
                script,
                "TryRejectJusticeCriticalBarrierBeforeCustodyDeath"));
            Assert.AreEqual(1L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            Assert.AreEqual(
                JusticeWalState.Ambiguous,
                wal.GetLatest(transactionId).State);
        });
    }

    [TestMethod]
    public void CriticalBarrier_LostAttemptedAcknowledgementResumesWithoutRewritingPrepared()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            SetField(
                script,
                "_justiceWalFaultInjectorOverride",
                new NthWalFaultInjector(
                    JusticePersistenceFaultPoint.AfterWalFlush,
                    2));
            FlushAndAwait(script);

            JusticeRepository repository =
                GetField<JusticeRepository>(script, "_justiceRepository");
            long revision = repository.GetDiagnostics().DiskRevision;
            Assert.IsTrue(revision > 0L);
            long createdAt = DateTime.UtcNow.Ticks;
            const string transactionId = "critical:lost-attempted-ack";
            SetField(script, "_justiceCriticalBarrierCaller", "CompleteJusticeCustodyTransfer");
            SetField(script, "_justiceCriticalBarrierOperationKind", "Transfer");
            SetField(script, "_justiceCriticalBarrierTransactionId", transactionId);
            SetField(script, "_justiceCriticalBarrierIdentityKey", "slot:0:model:1000");
            SetField(script, "_justiceCriticalBarrierRevision", revision);
            SetField(script, "_justiceCriticalBarrierProfileGeneration", 1L);
            SetField(script, "_justiceCriticalBarrierCreatedAtUtcTicks", createdAt);
            SetField(script, "_justiceCriticalBarrierProfileSlot", 0);

            Assert.IsFalse((bool)Invoke(
                script,
                "TryCommitJusticeCriticalBarrierToWal"));
            JusticeWriteAheadLog wal =
                GetField<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
            Assert.AreEqual(JusticeWalState.Attempted, wal.GetLatest(transactionId).State);
            Assert.AreEqual(revision, GetField<long>(script, "_justiceCriticalBarrierRevision"));

            Assert.IsTrue((bool)Invoke(
                script,
                "TryCommitJusticeCriticalBarrierToWal"));
            Assert.AreEqual(0L, GetField<long>(script, "_justiceCriticalBarrierRevision"));
            JusticeWalRecoveryResult recovered = JusticeWriteAheadLog.Recover(
                Path.Combine(directory, "_justice_state.wal"));
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, recovered.Status);
            Assert.AreEqual(
                2,
                recovered.Records.Count(record =>
                    string.Equals(
                        record.TransactionId,
                        transactionId,
                        StringComparison.Ordinal)),
                "Le retry ne doit jamais réécrire Prepared après un ACK Attempted perdu.");
            Assert.AreEqual(
                JusticeWalState.Attempted,
                recovered.Records.Last(record =>
                    string.Equals(
                        record.TransactionId,
                        transactionId,
                        StringComparison.Ordinal)).State);
        });
    }

    [TestMethod]
    public void DeathFront_RawPoliceCaseWithoutWantedEpisodeSurvivesAttemptedRecovery()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = false;
            profiles[0].CaseState.Phase = JusticePhase.AtLarge;
            object script = CreateHeadlessScript(profiles, 0);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 5L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 5L);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                5L);
            try
            {
                JusticeWalRecord prepared = CreateDeathFrontRecord(
                    "death-front:raw-police",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    5L,
                    5L,
                    string.Empty,
                    0,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(prepared);
                JusticeWalRecord attempted = wal.Append(CopyWalRecord(
                    prepared,
                    JusticeWalState.Attempted,
                    5L));

                Invoke(script, "RecoverJusticeDeathFrontFromWal", attempted);
                Assert.IsTrue(profiles[0].PendingDeathCapture);
                Assert.AreEqual(0, profiles[0].PendingDeathCapturePlayerSlot);
                Assert.AreEqual(1000, profiles[0].PendingDeathCapturePlayerModel);
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePursuitDeathObservedDuringSuspension"));
                Assert.AreEqual(JusticePhase.AtLarge, profiles[0].CaseState.Phase);
                Assert.IsTrue(
                    profiles[0].CaseState.Enabled,
                    "La preuve WAL brute doit réactiver le dossier qui avait déjà accepté la mort.");

                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(prepared.TransactionId).State,
                    "Le snapshot portant le front brut doit acquitter Attempted.");
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"),
                    "Le primaire seul ne doit pas autoriser la capture.");

                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(prepared.TransactionId).State);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                XElement active = GetPersistedActiveJusticeProfile(XDocument.Load(
                    Path.Combine(directory, "_justice_state.xml")));
                Assert.AreEqual("true", (string)active.Attribute("pendingDeathCapture"));
                Assert.IsTrue(string.IsNullOrWhiteSpace(
                    (string)active.Element("Case").Attribute("wantedEpisodeId")));
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeathFront_InactivePoliceOwnerIsNormalizedAndConfirmedOnBothCopies()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = false;
            profiles[0].CaseState.Phase = JusticePhase.AtLarge;
            string activeLabel = profiles[1].CaseState.LastCrimeLabel;
            object script = CreateHeadlessScript(profiles, 1);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 2L, 9L, 0L });
            SetField(script, "_justicePersistenceRevision", 9L);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                9L);
            try
            {
                JusticeWalRecord prepared = CreateDeathFrontRecord(
                    "death-front:inactive-police",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    2L,
                    2L,
                    "episode:inactive-police",
                    0,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(prepared);
                JusticeWalRecord attempted = wal.Append(CopyWalRecord(
                    prepared,
                    JusticeWalState.Attempted,
                    2L));

                Invoke(script, "RecoverJusticeDeathFrontFromWal", attempted);
                Assert.IsTrue(profiles[0].CaseState.Enabled);
                Assert.AreEqual(JusticePhase.Wanted, profiles[0].CaseState.Phase);
                Assert.AreEqual(
                    "episode:inactive-police",
                    profiles[0].CaseState.WantedEpisodeId);
                Assert.AreEqual(1, profiles[0].CaseState.Charges.Count);
                Assert.IsTrue(profiles[0].CaseState.SentenceSeconds > 0);
                Assert.AreEqual(
                    JusticeCrimeKind.EvadingPolice,
                    profiles[0].CaseState.Charges[0].Kind);
                Assert.IsTrue(profiles[0].PendingDeathCapture);
                Assert.AreEqual(activeLabel, profiles[1].CaseState.LastCrimeLabel);
                Assert.IsFalse(profiles[1].PendingDeathCapture);
                Assert.AreSame(profiles[1].CaseState, GetField<JusticeCaseState>(
                    script,
                    "_justiceCaseState"));

                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(prepared.TransactionId).State);
                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(prepared.TransactionId).State);

                string primaryPath = Path.Combine(directory, "_justice_state.xml");
                foreach (string path in new[] { primaryPath, primaryPath + ".bak" })
                {
                    XDocument document = XDocument.Load(path);
                    XElement owner = document.Root.Element("Profiles")
                        .Elements("Profile")
                        .Single(profile => string.Equals(
                            (string)profile.Attribute("slot"),
                            "0",
                            StringComparison.Ordinal));
                    Assert.AreEqual("true", (string)owner.Attribute("pendingDeathCapture"));
                    Assert.AreEqual(
                        "Wanted",
                        (string)owner.Element("Case").Attribute("phase"));
                    Assert.AreEqual(
                        "episode:inactive-police",
                        (string)owner.Element("Case").Attribute("wantedEpisodeId"));
                    Assert.IsTrue(int.Parse(
                        (string)owner.Element("Case").Attribute("sentenceSeconds"),
                        CultureInfo.InvariantCulture) > 0);
                    Assert.AreEqual(
                        1,
                        owner.Element("Case").Element("Charges").Elements("Charge").Count());
                }
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeathFront_LostAttemptedAcknowledgementAtSameGenerationReplaysBeforeConfirmation()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = true;
            object writer = CreateHeadlessScript(profiles, 0);
            SetField(
                writer,
                "_justiceWalFaultInjectorOverride",
                new NthWalFaultInjector(
                    JusticePersistenceFaultPoint.AfterWalFlush,
                    2));
            FlushAndAwait(writer);

            string primaryPath = Path.Combine(directory, "_justice_state.xml");
            XElement baseProfile = GetPersistedActiveJusticeProfile(
                XDocument.Load(primaryPath));
            long baseGeneration = long.Parse(
                (string)baseProfile.Attribute("generation"),
                CultureInfo.InvariantCulture);
            Assert.IsFalse(bool.Parse(
                (string)baseProfile.Attribute("pendingDeathCapture")));

            Assert.IsFalse((bool)Invoke(
                writer,
                "TryPersistJusticeDeathFrontToWal",
                "PoliceCapture",
                0,
                string.Empty,
                0,
                0,
                1000));
            JusticeWriteAheadLog writerWal = GetField<JusticeWriteAheadLog>(
                writer,
                "_justiceWriteAheadLog");
            JusticeWalRecord attempted = writerWal.GetOpenTransactions().Single();
            Assert.AreEqual(JusticeWalState.Attempted, attempted.State);
            Assert.AreEqual(
                baseGeneration.ToString(CultureInfo.InvariantCulture),
                JusticeXmlPersistenceCodec.GetFieldValue(
                    attempted.Fields,
                    "profileGeneration",
                    string.Empty),
                "Le snapshot pré-front et le WAL perdu doivent partager la même génération.");
            Assert.IsFalse(profiles[0].PendingDeathCapture);
            Assert.IsNotNull(GetField<JusticeWalRecord>(
                writer,
                "_justicePendingDeathFrontWalRecord"));
            Invoke(writer, "ShutdownJusticePersistenceServices");

            object reader = CreateHeadlessScript(null, -1);
            SetField(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", primaryPath));
            Assert.IsFalse(GetField<bool>(
                reader,
                "_justicePursuitDeathObservedDuringSuspension"));
            Invoke(reader, "InitializeJusticePersistenceServices");

            JusticePlayerProfileState[] recoveredProfiles =
                GetField<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.IsTrue(recoveredProfiles[0].PendingDeathCapture);
            Assert.IsTrue(GetField<bool>(
                reader,
                "_justicePursuitDeathObservedDuringSuspension"));
            JusticeWriteAheadLog recoveredWal = GetField<JusticeWriteAheadLog>(
                reader,
                "_justiceWriteAheadLog");
            Assert.AreEqual(
                JusticeWalState.Attempted,
                recoveredWal.GetLatest(attempted.TransactionId).State);

            SetField(reader, "_justiceMonotonicTimeMs", 100000L);
            SetField(reader, "_justiceNextStateSaveAtMs", 0L);
            SetField(reader, "_justiceNextCheckpointAtMs", 0L);
            Invoke(reader, "PersistJusticeStateIfDue");
            AwaitQueuedPersistence(reader);
            Assert.AreEqual(
                JusticeWalState.Ambiguous,
                recoveredWal.GetLatest(attempted.TransactionId).State,
                "Le primaire seul ne doit jamais rendre le front consommable.");

            SetField(reader, "_justiceMonotonicTimeMs", 101000L);
            SetField(reader, "_justiceNextStateSaveAtMs", 0L);
            SetField(reader, "_justiceNextCheckpointAtMs", 0L);
            Invoke(reader, "PersistJusticeStateIfDue");
            AwaitQueuedPersistence(reader);
            Assert.AreEqual(
                JusticeWalState.Confirmed,
                recoveredWal.GetLatest(attempted.TransactionId).State);

            foreach (string path in new[] { primaryPath, primaryPath + ".bak" })
            {
                XElement active = GetPersistedActiveJusticeProfile(XDocument.Load(path));
                Assert.AreEqual("true", (string)active.Attribute("pendingDeathCapture"));
            }
            Invoke(reader, "ShutdownJusticePersistenceServices");
        });
    }

    [TestMethod]
    public void DeathFront_DurabilityRequiresEveryOpenFrontOfTheActiveProfile()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 10L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 10L);
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                10L);
            try
            {
                JusticeWalRecord oldPrepared = CreateDeathFrontRecord(
                    "death-front:old-police",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    9L,
                    9L,
                    string.Empty,
                    0,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(oldPrepared);
                wal.Append(CopyWalRecord(oldPrepared, JusticeWalState.Attempted, 9L));
                wal.Append(CopyWalRecord(oldPrepared, JusticeWalState.Ambiguous, 10L));

                JusticeWalRecord newPrepared = CreateDeathFrontRecord(
                    "death-front:new-police",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    10L,
                    10L,
                    string.Empty,
                    0,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(newPrepared);
                wal.Append(CopyWalRecord(newPrepared, JusticeWalState.Attempted, 10L));

                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"),
                    "Un ancien Ambiguous ne doit jamais masquer le nouvel Attempted.");
                Assert.AreEqual(
                    JusticeWalState.Attempted,
                    wal.GetLatest(newPrepared.TransactionId).State);
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeathFront_DestructiveGuardWaitsForPoliceAndCustodyConfirmation()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                10L);
            try
            {
                JusticeWalRecord police = CreateDeathFrontRecord(
                    "death-front:destructive-police",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    10L,
                    10L,
                    string.Empty,
                    0,
                    0,
                    1000,
                    0,
                    1000);
                JusticeWalRecord custody = CreateDeathFrontRecord(
                    "death-front:destructive-custody",
                    "CustodyRebind",
                    0,
                    JusticeWalState.Prepared,
                    10L,
                    10L,
                    "custody:destructive-guard",
                    2,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(police);
                wal.Append(CopyWalRecord(police, JusticeWalState.Attempted, 10L));
                wal.Append(custody);
                wal.Append(CopyWalRecord(custody, JusticeWalState.Attempted, 10L));
                wal.Append(CopyWalRecord(custody, JusticeWalState.Ambiguous, 11L));
                wal.Append(CopyWalRecord(custody, JusticeWalState.Confirmed, 11L));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction"));

                wal.Append(CopyWalRecord(police, JusticeWalState.Ambiguous, 11L));
                wal.Append(CopyWalRecord(police, JusticeWalState.Confirmed, 11L));
                JusticeWalRecord secondCustody = CreateDeathFrontRecord(
                    "death-front:destructive-custody-2",
                    "CustodyRebind",
                    0,
                    JusticeWalState.Prepared,
                    10L,
                    10L,
                    "custody:destructive-guard-2",
                    2,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(secondCustody);
                wal.Append(CopyWalRecord(
                    secondCustody,
                    JusticeWalState.Attempted,
                    10L));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction"));

                wal.Append(CopyWalRecord(
                    secondCustody,
                    JusticeWalState.Ambiguous,
                    11L));
                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction"));

                wal.Append(CopyWalRecord(
                    secondCustody,
                    JusticeWalState.Confirmed,
                    11L));
                Assert.IsTrue((bool)Invoke(
                    script,
                    "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction"));
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeathFront_PartialServiceInitializationNeverConfirmsOrConsumesTheFront()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = true;
            object script = CreateHeadlessScript(profiles, 0);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 3L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 3L);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            JusticeWalRecord prepared = CreateDeathFrontRecord(
                "death-front:partial-init",
                "PoliceCapture",
                0,
                JusticeWalState.Prepared,
                3L,
                3L,
                string.Empty,
                0,
                0,
                1000,
                0,
                1000);
            SetField(script, "_justiceWriteAheadLog", wal);
            SetField(script, "_justicePendingDeathFrontWalRecord", prepared);
            SetField(script, "_justicePersistenceServicesUnavailable", true);
            SetField(script, "_justicePersistenceInitializationFailureCount", 1);
            SetField(script, "_justiceNextPersistenceInitializationRetryAtMs", 1000L);

            Assert.IsFalse((bool)Invoke(script, "TryResumePendingJusticeDeathFrontWal"));
            Assert.AreSame(
                prepared,
                GetField<JusticeWalRecord>(script, "_justicePendingDeathFrontWalRecord"));
            Assert.AreEqual(0, wal.GetOpenTransactions().Count);
            Assert.IsFalse(profiles[0].PendingDeathCapture);
            Assert.IsFalse((bool)Invoke(
                script,
                "IsJusticePoliceDeathFrontResultDurable"));

            try
            {
                SetField(script, "_justiceMonotonicTimeMs", 1000L);
                Assert.IsTrue((bool)Invoke(script, "TryResumePendingJusticeDeathFrontWal"));
                JusticeWriteAheadLog resumedWal = GetField<JusticeWriteAheadLog>(
                    script,
                    "_justiceWriteAheadLog");
                Assert.AreNotSame(wal, resumedWal);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePersistenceServicesUnavailable"));
                Assert.IsNull(GetField<JusticeWalRecord>(
                    script,
                    "_justicePendingDeathFrontWalRecord"));
                Assert.IsTrue(profiles[0].PendingDeathCapture);
                Assert.IsFalse((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));

                FlushAndAwait(script);
                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    resumedWal.GetLatest(prepared.TransactionId).State);
                Assert.IsTrue((bool)Invoke(
                    script,
                    "IsJusticePoliceDeathFrontResultDurable"));
            }
            finally
            {
                Invoke(script, "ShutdownJusticePersistenceServices");
            }
        });
    }

    [TestMethod]
    public void DeathFront_LatestWinsCoalescingOnlyQualifiesTheExactDiskRevision()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.Enabled = true;
            object script = CreateHeadlessScript(profiles, 0);
            string statePath = Path.Combine(directory, "_justice_state.xml");
            using (FirstWriteBlockingAtomicFileStore store =
                new FirstWriteBlockingAtomicFileStore())
            {
                JusticeRepository repository = new JusticeRepository(
                    statePath,
                    statePath + ".bak",
                    new JusticeXmlPersistenceCodec(),
                    0L,
                    store,
                    JusticeNoOpPersistenceFaultInjector.Instance,
                    10);
                JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                    Path.Combine(directory, "_justice_state.wal"));
                repository.Start();
                SetField(script, "_justiceRepository", repository);
                SetField(script, "_justiceWriteAheadLog", wal);
                try
                {
                    Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
                    Assert.IsTrue(
                        store.FirstWriteStarted.WaitOne(TimeSpan.FromSeconds(5)),
                        "Le snapshot de base doit occuper le writer avant le front.");
                    Assert.AreEqual(
                        1L,
                        repository.GetDiagnostics().WritingRevision);

                    Assert.IsTrue((bool)Invoke(
                        script,
                        "TryPersistJusticeDeathFrontToWal",
                        "PoliceCapture",
                        0,
                        string.Empty,
                        0,
                        0,
                        1000));
                    JusticeWalRecord attempted = wal.GetOpenTransactions().Single();
                    Assert.AreEqual(JusticeWalState.Attempted, attempted.State);
                    long firstCandidate = GetField<Dictionary<string, long>>(
                        script,
                        "_justiceDeathFrontResultCandidates")[attempted.TransactionId];

                    profiles[0].CaseState.LastCrimeLabel =
                        "Mutation coalescée après le front";
                    Invoke(script, "JusticeMarkStateDirty");
                    Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
                    long exactCandidate = GetField<Dictionary<string, long>>(
                        script,
                        "_justiceDeathFrontResultCandidates")[attempted.TransactionId];
                    Assert.IsTrue(exactCandidate > firstCandidate);
                    Assert.AreEqual(
                        exactCandidate,
                        repository.GetDiagnostics().PendingRevision,
                        "Le writer latest-wins doit remplacer le premier candidat en attente.");

                    store.ReleaseFirstWrite.Set();
                    AwaitQueuedPersistence(script);
                    JusticeRepositoryDiagnostics afterCoalescing =
                        repository.GetDiagnostics();
                    Assert.AreEqual(exactCandidate, afterCoalescing.DiskRevision);
                    Assert.AreEqual(
                        JusticeWalState.Ambiguous,
                        wal.GetLatest(attempted.TransactionId).State);
                    Assert.AreEqual(
                        exactCandidate,
                        wal.GetLatest(attempted.TransactionId).PersistenceRevision,
                        "La révision sautée ne doit jamais être utilisée comme preuve résultat.");

                    FlushAndAwait(script);
                    Assert.AreEqual(
                        JusticeWalState.Confirmed,
                        wal.GetLatest(attempted.TransactionId).State);
                    foreach (string path in new[] { statePath, statePath + ".bak" })
                    {
                        XElement active = GetPersistedActiveJusticeProfile(
                            XDocument.Load(path));
                        Assert.AreEqual(
                            "true",
                            (string)active.Attribute("pendingDeathCapture"));
                    }
                }
                finally
                {
                    store.ReleaseFirstWrite.Set();
                    repository.Stop(TimeSpan.FromSeconds(5));
                    repository.Dispose();
                }
            }
        });
    }

    [TestMethod]
    public void DeathFront_AmbiguousRecoveredFromOlderBackupForcesBothRotations()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            profiles[0].CaseState.ClearActiveCase(false);
            profiles[0].CaseState.Enabled = true;
            object script = CreateHeadlessScript(profiles, 0);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 2L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 2L);
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                2L);
            try
            {
                JusticeWalRecord prepared = CreateDeathFrontRecord(
                    "death-front:backup-replay",
                    "PoliceCapture",
                    0,
                    JusticeWalState.Prepared,
                    2L,
                    5L,
                    string.Empty,
                    0,
                    0,
                    1000,
                    0,
                    1000);
                wal.Append(prepared);
                wal.Append(CopyWalRecord(prepared, JusticeWalState.Attempted, 2L));
                JusticeWalRecord ambiguous = wal.Append(CopyWalRecord(
                    prepared,
                    JusticeWalState.Ambiguous,
                    5L));

                Invoke(script, "RecoverJusticeDeathFrontFromWal", ambiguous);
                Assert.IsTrue(profiles[0].PendingDeathCapture);
                Assert.AreEqual(
                    5L,
                    GetField<long[]>(script, "_justiceProfilePersistenceGenerations")[0],
                    "La génération portée par le WAL doit relever le plancher du backup ancien.");
                Assert.AreEqual(5L, GetField<long>(script, "_justicePersistenceRevision"));

                SetField(script, "_justiceMonotonicTimeMs", 100000L);
                SetField(script, "_justiceNextStateSaveAtMs", 0L);
                SetField(script, "_justiceNextCheckpointAtMs", 0L);
                Invoke(script, "PersistJusticeStateIfDue");
                AwaitQueuedPersistence(script);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(prepared.TransactionId).State,
                    "La première rotation ne prouve encore que le primaire.");

                SetField(script, "_justiceMonotonicTimeMs", 101000L);
                SetField(script, "_justiceNextStateSaveAtMs", 0L);
                SetField(script, "_justiceNextCheckpointAtMs", 0L);
                Invoke(script, "PersistJusticeStateIfDue");
                AwaitQueuedPersistence(script);

                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(prepared.TransactionId).State,
                    "La reprise doit dépasser la révision historique sans mutation externe.");
                Assert.IsTrue(repository.GetDiagnostics().DiskRevision > 5L);
                string primaryPath = Path.Combine(directory, "_justice_state.xml");
                foreach (string path in new[] { primaryPath, primaryPath + ".bak" })
                {
                    XElement active = GetPersistedActiveJusticeProfile(XDocument.Load(path));
                    Assert.AreEqual("true", (string)active.Attribute("pendingDeathCapture"));
                    Assert.AreEqual("0", (string)active.Attribute("pendingDeathCapturePlayerSlot"));
                    Assert.AreEqual("1000", (string)active.Attribute("pendingDeathCapturePlayerModel"));
                }
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void CustodyDeath_SecondRespawnModelRecoversOnlyFromAnOlderProvenGeneration()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            Invoke(script, "SnapshotActiveJusticePlayerProfile");
            JusticeCustodyPersistenceSnapshot before =
                RequireTypedCustodySnapshot(profiles[0]);
            Assert.AreEqual(123456, before.PlayerModelHash);
            SetField(script, "_justiceProfilePersistenceGenerations", new[] { 4L, 0L, 0L });
            SetField(script, "_justicePersistenceRevision", 4L);
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(
                script,
                directory,
                4L);
            try
            {
                JusticeWalRecord prepared = CreateDeathFrontRecord(
                    "death-front:second-custody-model",
                    "CustodyRebind",
                    0,
                    JusticeWalState.Prepared,
                    4L,
                    5L,
                    profiles[0].CaseState.CustodyEpisodeId,
                    before.Site,
                    0,
                    654321,
                    0,
                    1000);
                wal.Append(prepared);
                JusticeWalRecord attempted = wal.Append(CopyWalRecord(
                    prepared,
                    JusticeWalState.Attempted,
                    4L));

                Invoke(script, "RecoverJusticeDeathFrontFromWal", attempted);
                JusticeCustodyPersistenceSnapshot recovered =
                    RequireTypedCustodySnapshot(profiles[0]);
                Assert.AreEqual(654321, recovered.PlayerModelHash);
                Assert.IsTrue(recovered.WaitingForRespawn);
                Assert.IsTrue(recovered.DeathRebindPending);
                Assert.AreEqual(654321, GetField<int>(script, "_justiceCustodyPlayerModelHash"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyWaitingForRespawn"));
                Assert.IsTrue(GetField<bool>(script, "_justiceCustodyDeathRebindPending"));
                Assert.AreEqual(
                    5L,
                    GetField<long[]>(script, "_justiceProfilePersistenceGenerations")[0]);

                SetField(script, "_justiceMonotonicTimeMs", 100000L);
                SetField(script, "_justiceNextStateSaveAtMs", 0L);
                SetField(script, "_justiceNextCheckpointAtMs", 0L);
                Invoke(script, "PersistJusticeStateIfDue");
                AwaitQueuedPersistence(script);
                Assert.AreEqual(
                    JusticeWalState.Ambiguous,
                    wal.GetLatest(prepared.TransactionId).State);

                SetField(script, "_justiceMonotonicTimeMs", 101000L);
                SetField(script, "_justiceNextStateSaveAtMs", 0L);
                SetField(script, "_justiceNextCheckpointAtMs", 0L);
                Invoke(script, "PersistJusticeStateIfDue");
                AwaitQueuedPersistence(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(prepared.TransactionId).State);

                string primaryPath = Path.Combine(directory, "_justice_state.xml");
                foreach (string path in new[] { primaryPath, primaryPath + ".bak" })
                {
                    object persisted = CreateHeadlessScript(null, -1);
                    SetField(
                        persisted,
                        "_justiceCanonicalPlayerSlotOverride",
                        new Func<int>(() => 0));
                    Assert.IsTrue((bool)Invoke(persisted, "TryReadJusticeStateFile", path));
                    JusticePlayerProfileState[] persistedProfiles =
                        GetField<JusticePlayerProfileState[]>(
                            persisted,
                            "_justicePlayerProfiles");
                    JusticeCustodyPersistenceSnapshot persistedCustody =
                        RequireTypedCustodySnapshot(persistedProfiles[0]);
                    Assert.AreEqual(654321, persistedCustody.PlayerModelHash);
                    Assert.IsTrue(persistedCustody.WaitingForRespawn);
                    Assert.IsTrue(persistedCustody.DeathRebindPending);
                }

                JusticePlayerProfileState[] invalidProfiles = CreateDistinctProfiles();
                object invalid = CreateHeadlessScript(invalidProfiles, 0);
                ConfigureIncarceratedRuntime(invalid, invalidProfiles[0]);
                Invoke(invalid, "SnapshotActiveJusticePlayerProfile");
                SetField(invalid, "_justiceProfilePersistenceGenerations", new[] { 6L, 0L, 0L });
                SetField(invalid, "_justicePersistenceRevision", 6L);
                SetField(invalid, "_justiceWriteAheadLog", wal);
                TargetInvocationException rejected =
                    Assert.ThrowsException<TargetInvocationException>(
                        () => Invoke(
                            invalid,
                            "RecoverJusticeDeathFrontFromWal",
                            attempted));
                Assert.IsInstanceOfType(
                    rejected.InnerException,
                    typeof(InvalidDataException));
                Assert.AreEqual(
                    123456,
                    RequireTypedCustodySnapshot(invalidProfiles[0]).PlayerModelHash,
                    "Une génération plus récente interdit de remplacer le modèle déjà prouvé.");
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void LegacyTransferRollback_PreservesSentenceRecordAndInventoryAcrossCrash()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            ConfigureIncarceratedRuntime(script, profiles[0]);
            JusticeCaseState state = profiles[0].CaseState;
            state.Phase = JusticePhase.Transporting;
            JusticeOperation rollback = new JusticeOperation(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.TransferRollback,
                    state.CustodyEpisodeId),
                JusticeOperationKind.TransferRollback,
                state.CustodyEpisodeId);
            Assert.IsTrue(JusticePolicy.TryRegisterOperation(state, rollback));
            SetField(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetField(script, "_justiceInventoryRemoved", true);
            SetPrivateEnumField(script, "_justiceInventoryCustodyState", "RemovedVerified");
            SetField(script, "_justiceWeaponControlsLocked", true);
            state.HasWarrant = false;

            // Je publie d'abord le snapshot legacy exact. Il devient le backup
            // de crash que la migration doit remplacer sans toucher au dossier,
            // à la peine ni au moindre détail de l'inventaire confisqué.
            FlushAndAwait(script);
            string primaryPath = Path.Combine(directory, "_justice_state.xml");
            XDocument legacyDocument = XDocument.Load(primaryPath);
            XElement legacyProfile = GetPersistedActiveJusticeProfile(legacyDocument);
            XElement expectedCase = new XElement(legacyProfile.Element("Case"));
            XElement expectedRecord = new XElement(legacyProfile.Element("Record"));
            XElement expectedCustody = new XElement(legacyProfile.Element("Custody"));
            expectedCase
                .Element("CompletedOperations")
                .Elements("Operation")
                .Where(operation => operation.Value == rollback.OperationId)
                .Remove();

            Assert.IsFalse((bool)Invoke(
                script,
                "ResumeJusticeCustodyTransferRollback",
                null,
                1000));
            Assert.AreEqual(JusticePhase.Transporting, state.Phase);
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyRuntimeActive"));
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
            Assert.IsTrue(GetField<bool>(script, "_justiceCustodyResumePending"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackFinalizationPending"));
            Assert.IsTrue(GetField<bool>(script, "_justiceInventoryRemoved"));
            Assert.AreEqual(
                "RemovedVerified",
                GetField<object>(script, "_justiceInventoryCustodyState").ToString());
            Assert.IsTrue(GetField<bool>(script, "_justiceWeaponControlsLocked"));
            CollectionAssert.DoesNotContain(state.CompletedOperationIds, rollback.OperationId);

            AwaitQueuedPersistence(script);
            XDocument migratedPrimary = XDocument.Load(primaryPath);
            XElement migratedProfile = GetPersistedActiveJusticeProfile(migratedPrimary);
            Assert.IsTrue(
                XNode.DeepEquals(expectedCase, migratedProfile.Element("Case")),
                "La première écriture de migration doit préserver exactement la peine et les charges.");
            Assert.IsTrue(
                XNode.DeepEquals(expectedRecord, migratedProfile.Element("Record")),
                "La première écriture de migration doit préserver exactement le casier.");
            Assert.IsTrue(
                XNode.DeepEquals(expectedCustody, migratedProfile.Element("Custody")),
                "La première écriture de migration doit préserver exactement le snapshot d'armes.");

            object midCrash = CreateHeadlessScript(null, -1);
            SetField(midCrash, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
            Assert.IsTrue((bool)Invoke(
                midCrash,
                "TryReadJusticeStateFile",
                primaryPath));
            Assert.AreEqual(
                JusticePhase.Incarcerated,
                GetField<JusticeCaseState>(midCrash, "_justiceCaseState").Phase,
                "Le disque reste en Transporting, puis la normalisation de reprise doit replacer le détenu en détention, jamais AtLarge.");
            Assert.IsTrue(GetField<bool>(midCrash, "_justiceCustodyRuntimeActive"));
            Assert.IsTrue(GetField<bool>(midCrash, "_justiceCustodyResumePending"));
            Assert.IsFalse((bool)Invoke(
                midCrash,
                "ResumeJusticeCustodyTransferRollback",
                null,
                1500),
                "Le primaire déjà migré ne doit pas rejouer l'ancien rollback.");

            // Le passage suivant acquitte le précommit puis enfile une rotation
            // dédiée. J'injecte d'abord un refus de cette rotation : le latch
            // doit rester actif et reprendre à cadence bornée, sans libération.
            SetField(script, "_justiceMonotonicTimeMs", 2000L);
            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => true));
            Assert.IsFalse((bool)Invoke(
                script,
                "ResumeJusticeCustodyTransferRollback",
                null,
                2000));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackFinalizationPending"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackPrecommitRedundant"));
            Assert.AreEqual(
                0L,
                GetField<long>(
                    script,
                    "_justiceCustodyTransferRollbackFinalizationRevision"));

            SetField(
                script,
                "_justiceStateFlushFailureOverride",
                new Func<int, bool>(attempt => false));
            SetField(script, "_justiceMonotonicTimeMs", 2500L);
            Assert.IsFalse((bool)Invoke(
                script,
                "ResumeJusticeCustodyTransferRollback",
                null,
                2500));
            Assert.AreEqual(
                0L,
                GetField<long>(
                    script,
                    "_justiceCustodyTransferRollbackFinalizationRevision"),
                "Le retry de finalisation doit respecter sa cadence bornée.");

            SetField(script, "_justiceMonotonicTimeMs", 3000L);
            Assert.IsFalse((bool)Invoke(
                script,
                "ResumeJusticeCustodyTransferRollback",
                null,
                3000));
            Assert.IsTrue(GetField<long>(
                script,
                "_justiceCustodyTransferRollbackFinalizationRevision") > 0L);
            AwaitQueuedPersistence(script);
            SetField(script, "_justiceMonotonicTimeMs", 4000L);
            Assert.IsTrue((bool)Invoke(
                script,
                "ResumeJusticeCustodyTransferRollback",
                null,
                4000));
            Assert.IsFalse(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackFinalizationPending"));
            Assert.IsFalse(GetField<bool>(
                script,
                "_justiceCustodyTransferRollbackPrecommitRedundant"));
            Assert.AreEqual(
                0L,
                GetField<long>(
                    script,
                    "_justiceCustodyTransferRollbackFinalizationRevision"));

            // Je termine aussi le WAL générique afin de prouver que le résultat
            // reste identique dans le primaire et son backup après rotation.
            FlushAndAwait(script);
            foreach (string path in new[] { primaryPath, primaryPath + ".bak" })
            {
                Assert.IsTrue(File.Exists(path), path);
                XDocument durableDocument = XDocument.Load(path);
                XElement durableProfile = GetPersistedActiveJusticeProfile(durableDocument);
                Assert.IsTrue(
                    XNode.DeepEquals(expectedCase, durableProfile.Element("Case")),
                    "Le dossier exact a changé dans " + path);
                Assert.IsTrue(
                    XNode.DeepEquals(expectedRecord, durableProfile.Element("Record")),
                    "Le casier exact a changé dans " + path);
                Assert.IsTrue(
                    XNode.DeepEquals(expectedCustody, durableProfile.Element("Custody")),
                    "Le snapshot d'armes exact a changé dans " + path);

                object afterCrash = CreateHeadlessScript(null, -1);
                SetField(
                    afterCrash,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));
                Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
                JusticeCaseState loadedCase = GetField<JusticeCaseState>(
                    afterCrash,
                    "_justiceCaseState");
                Assert.AreEqual(
                    JusticePhase.Incarcerated,
                    loadedCase.Phase,
                    "La reprise normalisée doit rester en détention, jamais AtLarge.");
                Assert.IsTrue(GetField<bool>(afterCrash, "_justiceCustodyRuntimeActive"));
                Assert.IsTrue(GetField<bool>(afterCrash, "_justiceCustodyResumePending"));
                Assert.IsFalse(GetField<bool>(
                    afterCrash,
                    "_justiceCustodyTransferRollbackFinalizationPending"));
                Assert.IsTrue(GetField<bool>(afterCrash, "_justiceInventoryRemoved"));
                Assert.IsTrue(GetField<bool>(afterCrash, "_justiceWeaponControlsLocked"));
                Assert.AreEqual(
                    "RemovedVerified",
                    GetField<object>(afterCrash, "_justiceInventoryCustodyState").ToString());
                CollectionAssert.DoesNotContain(
                    loadedCase.CompletedOperationIds,
                    rollback.OperationId);

                XElement custodyBeforeIdempotentRetry = XElement.Parse(
                    (string)Invoke(afterCrash, "CaptureCurrentJusticeCustodyXml"));
                Assert.IsFalse((bool)Invoke(
                    afterCrash,
                    "ResumeJusticeCustodyTransferRollback",
                    null,
                    5000));
                Assert.AreEqual(JusticePhase.Incarcerated, loadedCase.Phase);
                Assert.IsTrue(XNode.DeepEquals(
                    custodyBeforeIdempotentRetry,
                    XElement.Parse((string)Invoke(
                        afterCrash,
                        "CaptureCurrentJusticeCustodyXml"))));
            }
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
            AssertOrdered(
                resumeBody,
                "EnsureJusticeActiveProfileResetPrecommitRedundant()",
                "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction()",
                "ClearPendingJusticeDeathCapture()",
                "JusticeAmnestyCustody()",
                "ReplaceJusticePlayerProfileWithEmptyState(slot)");
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
            bool legacyLoaded = (bool)Invoke(
                legacyReader,
                "TryReadJusticeStateFile",
                path);
            Assert.IsTrue(
                legacyLoaded,
                "L'ancien précommit doit être lisible par le reset de politique.");
            Assert.IsFalse(
                GetField<bool>(legacyReader, "_justiceLegalReleaseFinalizationPending"),
                "Le WAL de libération historique ne doit jamais être rejoué.");
            Assert.IsNotNull(GetField<object>(legacyReader, "_justiceWeaponSnapshot"));
            Assert.IsFalse((bool)Invoke(legacyReader, "IsJusticeLegalReleasePrecommitState"));
            Assert.AreEqual(
                JusticePhase.AtLarge,
                GetField<JusticeCaseState>(legacyReader, "_justiceCaseState").Phase);
            Assert.AreEqual(1, GetField<int>(legacyReader, "_justicePolicyResetRecoveryMask"));
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

    [TestMethod]
    public void DeferredPoliceDeath_IsInWalAndSwitchSnapshotBeforePostRepairReconcile()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            SetPrivateEnumField(
                script,
                "_justiceDeferredRuntimeFronts",
                "DeathStarted");
            SetField(script, "_justiceDeferredRuntimeFrontPlayerSlot", 1);
            SetField(script, "_justiceDeferredRuntimeFrontPlayerModelHash", 1001);
            SetField(script, "_justiceDeferredRuntimeFrontHadPursuit", true);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(script, directory, 0L);
            try
            {
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryHardenJusticeDeferredCriticalFronts"));
                Assert.IsTrue(profiles[1].PendingDeathCapture);
                Assert.AreEqual(1, profiles[1].PendingDeathCapturePlayerSlot);
                Assert.AreEqual(1001, profiles[1].PendingDeathCapturePlayerModel);
                JusticeWalRecord attempted = wal.GetOpenTransactions().Single();
                Assert.AreEqual(JusticeWalState.Attempted, attempted.State);
                Assert.AreEqual(1, attempted.ProfileSlot);
                Assert.AreEqual(
                    "PoliceCapture",
                    JusticeXmlPersistenceCodec.GetFieldValue(
                        attempted.Fields,
                        "mode",
                        string.Empty));

                JusticePlayerProfileState[] replayProfiles = CreateDistinctProfiles();
                object replay = CreateHeadlessScript(replayProfiles, 1);
                SetField(replay, "_justiceProfilePersistenceGenerations", new long[3]);
                Invoke(replay, "RecoverJusticeDeathFrontFromWal", attempted);
                Assert.IsTrue(replayProfiles[1].PendingDeathCapture);
                Assert.IsTrue(GetField<bool>(
                    replay,
                    "_justicePoliceDeathRespawnMaskIntentPending"));

                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                Assert.AreEqual(
                    1,
                    GetField<int>(script, "_justiceActivePlayerProfileSlot"),
                    "Le premier passage doit avoir activé Q avant d'attendre DiskRevision.");
                Assert.IsTrue(GetField<bool>(
                    script,
                    "_justicePoliceDeathRespawnMaskIntentPending"),
                    "L'activation du profil Q doit réarmer son intent de masque durable.");
                AwaitQueuedPersistence(script);

                string path = Path.Combine(directory, "_justice_state.xml");
                object afterCrash = CreateHeadlessScript(null, -1);
                SetField(
                    afterCrash,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
                JusticePlayerProfileState[] loaded =
                    GetField<JusticePlayerProfileState[]>(
                        afterCrash,
                        "_justicePlayerProfiles");
                Assert.AreEqual(1, GetField<int>(
                    afterCrash,
                    "_justiceActivePlayerProfileSlot"));
                Assert.IsTrue(
                    loaded[1].PendingDeathCapture,
                    "Le crash avant Reconcile ne doit pas perdre la mort de Q.");
                Assert.AreEqual(1, loaded[1].PendingDeathCapturePlayerSlot);
                Assert.AreEqual(1001, loaded[1].PendingDeathCapturePlayerModel);
                Assert.IsTrue(GetField<bool>(
                    afterCrash,
                    "_justicePoliceDeathRespawnMaskIntentPending"));
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeferredPoliceArrest_SurvivesCrashBetweenSwitchSnapshotAndReconcile()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            ConfigureConsistentActiveCase(
                profiles[1].CaseState,
                "fine-only-arrest",
                12,
                750L,
                0);
            profiles[1].CaseState.Phase = JusticePhase.Wanted;
            object script = CreateHeadlessScript(profiles, 0);
            InitializeProfileResetRuntimeCollections(script);
            SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
            SetPrivateEnumField(
                script,
                "_justiceDeferredRuntimeFronts",
                "ArrestStarted");
            SetField(script, "_justiceDeferredRuntimeFrontPlayerSlot", 1);
            SetField(script, "_justiceDeferredRuntimeFrontPlayerModelHash", 1001);
            SetField(script, "_justiceDeferredRuntimeFrontHadPursuit", true);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(script, directory, 0L);
            try
            {
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryHardenJusticeDeferredCriticalFronts"));
                Assert.AreEqual(JusticePhase.Surrendering, profiles[1].CaseState.Phase);
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePoliceDeathRespawnMaskIntentPending"));
                Assert.IsTrue(profiles[1].CaseState.Charges.Count >= 2);
                Assert.IsTrue(
                    profiles[1].CaseState.SentenceSeconds > 0,
                    "Une charge pécuniaire existante ne doit pas neutraliser la peine minimale de capture.");
                JusticeWalRecord attempted = wal.GetOpenTransactions().Single();
                Assert.AreEqual(JusticeWalState.Attempted, attempted.State);
                Assert.AreEqual(1, attempted.ProfileSlot);
                Assert.AreEqual(
                    "PoliceArrest",
                    JusticeXmlPersistenceCodec.GetFieldValue(
                        attempted.Fields,
                        "mode",
                        string.Empty));

                JusticePlayerProfileState[] replayProfiles = CreateDistinctProfiles();
                ConfigureConsistentActiveCase(
                    replayProfiles[1].CaseState,
                    "fine-only-arrest",
                    12,
                    750L,
                    0);
                replayProfiles[1].CaseState.Phase = JusticePhase.Wanted;
                object replayBeforeSnapshot = CreateHeadlessScript(replayProfiles, 0);
                SetField(
                    replayBeforeSnapshot,
                    "_justiceProfilePersistenceGenerations",
                    new long[3]);
                Invoke(
                    replayBeforeSnapshot,
                    "RecoverJusticeDeathFrontFromWal",
                    attempted);
                Assert.AreEqual(
                    JusticePhase.Surrendering,
                    replayProfiles[1].CaseState.Phase,
                    "Le WAL seul doit restaurer l'arrestation si le crash précède le snapshot du switch.");
                Assert.IsTrue(replayProfiles[1].CaseState.Charges.Count > 0);
                Assert.IsTrue(replayProfiles[1].CaseState.SentenceSeconds > 0);

                Assert.IsFalse((bool)Invoke(
                    script,
                    "EnsureJusticeProfileMatchesCanonicalPlayer",
                    new object[] { null }));
                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                Assert.AreEqual(
                    JusticeWalState.Confirmed,
                    wal.GetLatest(attempted.TransactionId).State);

                string path = Path.Combine(directory, "_justice_state.xml");
                foreach (string durablePath in new[] { path, path + ".bak" })
                {
                    object durableReader = CreateHeadlessScript(null, -1);
                    SetField(
                        durableReader,
                        "_justiceCanonicalPlayerSlotOverride",
                        new Func<int>(() => 1));
                    Assert.IsTrue((bool)Invoke(
                        durableReader,
                        "TryReadJusticeStateFile",
                        durablePath));
                    JusticePlayerProfileState durableProfile =
                        GetField<JusticePlayerProfileState[]>(
                            durableReader,
                            "_justicePlayerProfiles")[1];
                    Assert.AreEqual(
                        JusticePhase.Surrendering,
                        durableProfile.CaseState.Phase);
                    Assert.IsTrue(durableProfile.CaseState.SentenceSeconds > 0);
                }

                object afterCrash = CreateHeadlessScript(null, -1);
                SetField(
                    afterCrash,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 1));
                Assert.IsTrue((bool)Invoke(afterCrash, "TryReadJusticeStateFile", path));
                JusticePlayerProfileState[] loaded =
                    GetField<JusticePlayerProfileState[]>(
                        afterCrash,
                        "_justicePlayerProfiles");
                Assert.AreEqual(JusticePhase.Surrendering, loaded[1].CaseState.Phase);
                Assert.IsTrue(loaded[1].CaseState.Charges.Count > 0);

                Invoke(afterCrash, "ReconcileLoadedJusticePursuitState", 0);
                Assert.AreEqual(
                    JusticePhase.Surrendering,
                    loaded[1].CaseState.Phase,
                    "Le reload ne doit pas dégrader immédiatement l'arrestation en mandat.");
                Assert.IsTrue(GetField<bool>(
                    afterCrash,
                    "_justiceArrestCompletionProbePending"));
                Assert.IsTrue(GetField<bool>(afterCrash, "_justiceWantedLossPending"));
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeferredFrontLots_PDeathAndQArrestBothSurviveRepairCrashAndReload()
    {
        WithTemporaryJusticeDirectory(directory =>
        {
            JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
            object script = CreateHeadlessScript(profiles, 1);
            InitializeProfileResetRuntimeCollections(script);
            int pModel = GTA.Game.GenerateHash("player_one");
            int qModel = GTA.Game.GenerateHash("player_two");
            Type frontsType = ScriptType.GetNestedType(
                "JusticeDeferredRuntimeFront",
                BindingFlags.NonPublic);
            Assert.IsNotNull(frontsType);

            Assert.IsTrue((bool)Invoke(
                script,
                "TryStoreJusticeDeferredRuntimeFront",
                1,
                pModel,
                Enum.Parse(frontsType, "DeathStarted"),
                true));
            Assert.IsTrue((bool)Invoke(
                script,
                "TryStoreJusticeDeferredRuntimeFront",
                2,
                qModel,
                Enum.Parse(frontsType, "ArrestStarted"),
                true));

            Array additionalLots = (Array)GetField<object>(
                script,
                "_justiceAdditionalDeferredRuntimeFronts");
            Assert.IsTrue(HasEnumFlag(
                GetField<object>(script, "_justiceDeferredRuntimeFronts"),
                "DeathStarted"));
            Assert.IsFalse(HasEnumFlag(
                GetField<object>(script, "_justiceDeferredRuntimeFronts"),
                "ArrestStarted"));
            Assert.IsTrue(HasEnumFlag(additionalLots.GetValue(2), "ArrestStarted"));
            Assert.IsFalse(HasEnumFlag(additionalLots.GetValue(2), "DeathStarted"));

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            SetField(script, "_justiceWriteAheadLog", wal);
            JusticeRepository repository = AttachJusticeRepository(script, directory, 0L);
            try
            {
                Assert.IsTrue((bool)Invoke(
                    script,
                    "TryHardenJusticeDeferredCriticalFronts"));
                Assert.IsTrue(profiles[1].PendingDeathCapture);
                Assert.AreEqual(1, profiles[1].PendingDeathCapturePlayerSlot);
                Assert.AreEqual(pModel, profiles[1].PendingDeathCapturePlayerModel);
                Assert.AreEqual(JusticePhase.Surrendering, profiles[2].CaseState.Phase);
                Assert.IsTrue(profiles[2].CaseState.SentenceSeconds > 0);

                JusticeWalRecord[] attempted = wal.GetOpenTransactions()
                    .Where(record => string.Equals(
                        record.OperationKind,
                        "DeathFront",
                        StringComparison.Ordinal))
                    .OrderBy(record => record.ProfileSlot)
                    .ToArray();
                Assert.AreEqual(2, attempted.Length);
                Assert.AreEqual(1, attempted[0].ProfileSlot);
                Assert.AreEqual(
                    "PoliceCapture",
                    JusticeXmlPersistenceCodec.GetFieldValue(
                        attempted[0].Fields,
                        "mode",
                        string.Empty));
                Assert.AreEqual(2, attempted[1].ProfileSlot);
                Assert.AreEqual(
                    "PoliceArrest",
                    JusticeXmlPersistenceCodec.GetFieldValue(
                        attempted[1].Fields,
                        "mode",
                        string.Empty));

                // Je reproduis la fin de réparation sur Q : son lot est
                // réconcilié puis tous les latches mémoire sont nettoyés. La
                // preuve de P doit rester dans son DTO, déjà appliqué par le WAL.
                Assert.IsTrue((bool)Invoke(
                    script,
                    "ActivateJusticePlayerProfile",
                    2));
                Invoke(
                    script,
                    "ReconcileJusticeDeferredRuntimeFrontLotAfterPersistenceRepair",
                    2,
                    qModel,
                    0);
                Assert.IsFalse((bool)Invoke(
                    script,
                    "HasJusticeDeferredRuntimeFronts"));
                Assert.IsTrue(
                    profiles[1].PendingDeathCapture,
                    "Le nettoyage après la réconciliation de Q ne doit pas effacer le DTO de P.");
                Assert.AreEqual(JusticePhase.Surrendering, profiles[2].CaseState.Phase);

                // Je rejoue ici le crash avant le snapshot du switch : les deux
                // propriétaires doivent être restaurés depuis leurs WAL séparés.
                JusticePlayerProfileState[] replayProfiles = CreateDistinctProfiles();
                object replayBeforeSnapshot = CreateHeadlessScript(replayProfiles, 1);
                SetField(
                    replayBeforeSnapshot,
                    "_justiceProfilePersistenceGenerations",
                    new long[3]);
                foreach (JusticeWalRecord record in attempted)
                {
                    Invoke(
                        replayBeforeSnapshot,
                        "RecoverJusticeDeathFrontFromWal",
                        record);
                }
                Assert.IsTrue(replayProfiles[1].PendingDeathCapture);
                Assert.AreEqual(JusticePhase.Surrendering, replayProfiles[2].CaseState.Phase);
                Assert.IsTrue(replayProfiles[2].CaseState.SentenceSeconds > 0);

                AwaitQueuedPersistence(script);
                FlushAndAwait(script);
                foreach (JusticeWalRecord record in attempted)
                {
                    Assert.AreEqual(
                        JusticeWalState.Confirmed,
                        wal.GetLatest(record.TransactionId).State);
                }

                string primaryPath = Path.Combine(directory, "_justice_state.xml");
                foreach (string durablePath in
                    new[] { primaryPath, primaryPath + ".bak" })
                {
                    object afterCrash = CreateHeadlessScript(null, -1);
                    SetField(
                        afterCrash,
                        "_justiceCanonicalPlayerSlotOverride",
                        new Func<int>(() => 1));
                    Assert.IsTrue((bool)Invoke(
                        afterCrash,
                        "TryReadJusticeStateFile",
                        durablePath));
                    JusticePlayerProfileState[] loaded =
                        GetField<JusticePlayerProfileState[]>(
                            afterCrash,
                            "_justicePlayerProfiles");
                    Assert.IsTrue(
                        loaded[1].PendingDeathCapture,
                        "La mort de P doit rester durable dans chaque rotation.");
                    Assert.AreEqual(1, loaded[1].PendingDeathCapturePlayerSlot);
                    Assert.AreEqual(pModel, loaded[1].PendingDeathCapturePlayerModel);
                    Assert.AreEqual(
                        JusticePhase.Surrendering,
                        loaded[2].CaseState.Phase,
                        "L'arrestation de Q ne doit pas être perdue derrière le lot de P.");
                    Assert.IsTrue(loaded[2].CaseState.SentenceSeconds > 0);
                }
            }
            finally
            {
                repository.Stop(TimeSpan.FromSeconds(5));
            }
        });
    }

    [TestMethod]
    public void DeferredWantedLostOnly_IsReconstructedWhenItsInactiveOwnerReturns()
    {
        JusticePlayerProfileState[] profiles = CreateDistinctProfiles();
        ConfigureConsistentActiveCase(
            profiles[1].CaseState,
            "inactive-wanted-lost",
            20,
            1500L,
            90);
        profiles[1].CaseState.Phase = JusticePhase.Wanted;
        object script = CreateHeadlessScript(profiles, 2);
        Type frontsType = ScriptType.GetNestedType(
            "JusticeDeferredRuntimeFront",
            BindingFlags.NonPublic);
        Assert.IsNotNull(frontsType);
        Assert.IsTrue((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            1,
            GTA.Game.GenerateHash("player_one"),
            Enum.Parse(frontsType, "WantedLost"),
            true));

        // Je peux nettoyer ce front scalaire sans WAL : au retour de P, son
        // dossier Wanted et le wanted GTA à zéro reconstruisent le mandat exact.
        Invoke(script, "ClearJusticeDeferredRuntimeFronts");
        Assert.IsTrue((bool)Invoke(script, "ActivateJusticePlayerProfile", 1));
        Invoke(script, "ReconcileLoadedJusticePursuitState", 0);

        Assert.IsTrue(profiles[1].CaseState.HasWarrant);
        Assert.AreEqual(JusticePhase.AtLarge, profiles[1].CaseState.Phase);
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
        state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyFine,
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

    private static bool HasEnumFlag(object value, string flagName)
    {
        Assert.IsNotNull(value);
        Type enumType = value.GetType();
        long current = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        long flag = Convert.ToInt64(
            Enum.Parse(enumType, flagName),
            CultureInfo.InvariantCulture);
        return (current & flag) == flag;
    }

    private static void SetPrivateEnumField(object script, string fieldName, string value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(script, Enum.Parse(field.FieldType, value));
    }

    private static JusticeRepository AttachJusticeRepository(
        object script,
        string directory,
        long initialRevision)
    {
        string statePath = Path.Combine(directory, "_justice_state.xml");
        JusticeRepository repository = new JusticeRepository(
            statePath,
            statePath + ".bak",
            new JusticeXmlPersistenceCodec(),
            initialRevision);
        repository.Start();
        SetField(script, "_justiceRepository", repository);
        return repository;
    }

    private static JusticeWalRecord CreateProfileResetRecord(
        string transactionId,
        int profileSlot,
        JusticeWalState state,
        long persistenceRevision,
        long profileGeneration,
        int playerModel)
    {
        string identityKey = "slot:" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":model:" + playerModel.ToString(CultureInfo.InvariantCulture);
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokeStatic(
                "CreateJusticeProfileResetWalFields",
                profileGeneration,
                identityKey);
        return new JusticeWalRecord(
            transactionId,
            "ProfileResetResult",
            profileSlot,
            state,
            persistenceRevision,
            DateTime.UtcNow.Ticks,
            fields);
    }

    private static JusticePlayerProfileState CreateCanonicalProfileResetResult(
        int profileSlot)
    {
        return new JusticePlayerProfileState(profileSlot)
        {
            CustodyXml = (string)InvokeStatic(
                "CreateCanonicalEmptyJusticeCustodyXml"),
            LastCanonicalPlayerModel = 0
        };
    }

    private static bool PersistedJusticeProfileContainsExactReset(
        string path,
        int profileSlot)
    {
        Assert.IsTrue(File.Exists(path), "Copie Justice absente : " + path);
        object reader = CreateHeadlessScript(null, -1);
        SetField(
            reader,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => profileSlot));
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", path));
        JusticePlayerProfileState[] profiles =
            GetField<JusticePlayerProfileState[]>(
                reader,
                "_justicePlayerProfiles");
        return (bool)InvokeStatic(
            "IsJusticeProfileResetResultPresent",
            profiles[profileSlot]);
    }

    private static JusticeWalRecord CreateDeathFrontRecord(
        string transactionId,
        string mode,
        int profileSlot,
        JusticeWalState state,
        long persistenceRevision,
        long profileGeneration,
        string episodeId,
        int custodySite,
        int playerSlot,
        int playerModel,
        int lastCanonicalSlot,
        int lastCanonicalModel)
    {
        string identityKey = "slot:" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":model:" + lastCanonicalModel.ToString(CultureInfo.InvariantCulture);
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokeStatic(
                "CreateJusticeDeathFrontWalFields",
                mode,
                persistenceRevision,
                profileGeneration,
                identityKey,
                episodeId,
                custodySite,
                playerSlot,
                playerModel,
                lastCanonicalSlot,
                lastCanonicalModel);
        return new JusticeWalRecord(
            transactionId,
            "DeathFront",
            profileSlot,
            state,
            persistenceRevision,
            DateTime.UtcNow.Ticks,
            fields);
    }

    private static JusticeWalRecord CopyWalRecord(
        JusticeWalRecord source,
        JusticeWalState state,
        long persistenceRevision)
    {
        Assert.IsNotNull(source);
        return new JusticeWalRecord(
            source.TransactionId,
            source.OperationKind,
            source.ProfileSlot,
            state,
            persistenceRevision,
            source.CreatedAtUtcTicks,
            source.Fields);
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

    private static void AssertOrdered(string source, params string[] tokens)
    {
        int cursor = -1;
        for (int index = 0; index < tokens.Length; index++)
        {
            int found = source.IndexOf(
                tokens[index],
                cursor + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                found > cursor,
                "Jeton absent ou désordonné : " + tokens[index]);
            cursor = found;
        }
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
        bool persisted =
            (bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests");
        JusticeRepository repository = GetField<JusticeRepository>(
            script,
            "_justiceRepository");
        JusticeRepositoryDiagnostics diagnostics = repository == null
            ? null
            : repository.GetDiagnostics();
        string runtimeError = GetField<string>(
            script,
            "_justicePersistenceLastError");
        Assert.IsTrue(
            persisted,
            "La barrière réservée aux tests doit confirmer la révision sur disque." +
            (diagnostics == null
                ? " Repository absent, erreur runtime=" + runtimeError + "."
                : " Etat=" + diagnostics.State +
                  ", mémoire=" + diagnostics.MemoryRevision.ToString(
                      CultureInfo.InvariantCulture) +
                  ", disque=" + diagnostics.DiskRevision.ToString(
                      CultureInfo.InvariantCulture) +
                  ", erreur repository=" + diagnostics.LastError +
                  ", erreur runtime=" + runtimeError));
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

    private sealed class FirstWriteBlockingAtomicFileStore :
        IJusticeAtomicFileStore,
        IDisposable
    {
        private readonly JusticeAtomicFileStore _inner =
            new JusticeAtomicFileStore();
        private int _writeCount;

        internal ManualResetEvent FirstWriteStarted { get; } =
            new ManualResetEvent(false);
        internal ManualResetEvent ReleaseFirstWrite { get; } =
            new ManualResetEvent(false);

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                FirstWriteStarted.Set();
                if (!ReleaseFirstWrite.WaitOne(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException(
                        "Le test n'a pas libéré la première écriture Justice.");
                }
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

        public void Dispose()
        {
            FirstWriteStarted.Dispose();
            ReleaseFirstWrite.Dispose();
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
                throw new IOException(
                    "Panne WAL critique déterministe injectée.");
            }
        }
    }

    private sealed class WalOccurrenceFaultInjector :
        IJusticePersistenceFaultInjector
    {
        private readonly JusticePersistenceFaultPoint _point;
        private readonly HashSet<int> _failingOccurrences;
        private int _occurrence;
        private volatile bool _writesAllowed;

        internal WalOccurrenceFaultInjector(
            JusticePersistenceFaultPoint point,
            params int[] failingOccurrences)
        {
            _point = point;
            _failingOccurrences = new HashSet<int>(
                failingOccurrences ?? new int[0]);
        }

        internal void AllowWrites()
        {
            _writesAllowed = true;
        }

        public void Probe(JusticePersistenceFaultPoint point)
        {
            if (point != _point)
            {
                return;
            }

            int occurrence = Interlocked.Increment(ref _occurrence);
            if (!_writesAllowed && _failingOccurrences.Contains(occurrence))
            {
                // Je simule successivement la perte de Attempted puis celle de
                // Rejected, sans forcer un redémarrage entre les deux retries.
                throw new IOException(
                    "Panne WAL déterministe sur l'occurrence " +
                    occurrence.ToString(CultureInfo.InvariantCulture) + ".");
            }
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
