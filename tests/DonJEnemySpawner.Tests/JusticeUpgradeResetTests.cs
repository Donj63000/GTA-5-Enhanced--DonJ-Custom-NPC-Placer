using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeUpgradeResetTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    [TestMethod]
    public void LegacyProfiles_AreClearedAndKeepOnlyTheirEnabledPreference()
    {
        JusticePlayerProfileState[] profiles =
            new JusticePlayerProfileState[3];
        bool[] enabled = { true, false, true };
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            JusticeCaseState caseState = new JusticeCaseState
            {
                Enabled = enabled[slot],
                ActiveScore = 50 + slot,
                FineDue = 900L + slot,
                SentenceSeconds = 300 + slot,
                HasWarrant = true,
                Phase = JusticePhase.Wanted,
                WantedEpisodeId = "legacy:" + slot
            };
            caseState.CompletedOperationIds.Add("legacy-operation:" + slot);
            JusticeRecordState record = new JusticeRecordState
            {
                RecidivismIndex = 20 + slot,
                CleanGameplaySeconds = 120
            };
            profiles[slot] = new JusticePlayerProfileState(slot)
            {
                CaseState = caseState,
                RecordState = record,
                CustodySnapshot = CreateEmptyCustody(slot)
            };
        }

        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        int mask = (int)InvokeInstance(
            script,
            "PrepareJusticeProfilesForSentencePolicyUpgrade",
            (object)profiles);

        Assert.AreEqual(0, mask);
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            JusticePlayerProfileState profile = profiles[slot];
            Assert.AreEqual(enabled[slot], profile.CaseState.Enabled);
            Assert.AreEqual(JusticePhase.AtLarge, profile.CaseState.Phase);
            Assert.AreEqual(0, profile.CaseState.Charges.Count);
            Assert.AreEqual(0, profile.CaseState.ActiveScore);
            Assert.AreEqual(0L, profile.CaseState.FineDue);
            Assert.AreEqual(0, profile.CaseState.SentenceSeconds);
            Assert.IsFalse(profile.CaseState.HasWarrant);
            Assert.AreEqual(0, profile.CaseState.CompletedOperationIds.Count);
            Assert.AreEqual(0, profile.RecordState.RecidivismIndex);
            Assert.AreEqual(0, profile.RecordState.Convictions.Count);
            Assert.IsNull(profile.CustodySnapshot);
            Assert.IsFalse(profile.PendingDeathCapture);
            Assert.IsFalse(profile.PendingAmnestyWantedClear);
            Assert.IsFalse(profile.PendingLegalReleaseFinalization);
        }
    }

    [TestMethod]
    public void LegacyExtractor_AcceptsThirtyMinuteSentenceBeforeReset()
    {
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(
            "<JusticeState version='1' lastCanonicalPlayerSlot='1'>" +
            "<Case enabled='true' sentenceSeconds='1800' phase='Incarcerated' />" +
            "<Record recidivism='100' />" +
            "<Custody active='false' site='None' " +
            "policeSuppressionApplied='false' policeDispatchDisabled='false' " +
            "initialSentenceSeconds='0' " +
            "waitingForRespawn='false' deathRebindPending='false' " +
            "playerStateStored='false' storedInvincible='false' " +
            "storedFrozen='false' storedCanRagdoll='true' " +
            "playerModelHash='0' playerSlot='-1' releaseSelectedWeapon='-1569615261' />" +
            "</JusticeState>");
        MethodInfo extractor = typeof(DonJEnemySpawner).GetMethod(
            "TryExtractJusticeLegacyPolicyProfiles",
            PrivateStatic);
        Assert.IsNotNull(extractor);
        object[] arguments = { document.DocumentElement, -1, null, -1 };

        Assert.IsTrue((bool)extractor.Invoke(null, arguments));
        JusticePlayerProfileState[] profiles =
            (JusticePlayerProfileState[])arguments[2];
        Assert.AreEqual(1, (int)arguments[3]);

        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        int mask = (int)InvokeInstance(
            script,
            "PrepareJusticeProfilesForSentencePolicyUpgrade",
            (object)profiles);
        Assert.AreEqual(0, mask);
        Assert.IsTrue(profiles[1].CaseState.Enabled);
        Assert.AreEqual(0, profiles[1].CaseState.SentenceSeconds);
        Assert.AreEqual(0, profiles[1].RecordState.RecidivismIndex);
    }

    [TestMethod]
    public void LegacyCustodyRecoveryToken_DropsJudicialAndFinancialPayloads()
    {
        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                123,
                new[]
                {
                    new JusticeWeaponPersistenceSnapshot(
                        123,
                        30,
                        8,
                        1,
                        new int[0])
                });
        JusticeCustodyPersistenceSnapshot legacy =
            new JusticeCustodyPersistenceSnapshot(
                true,
                2,
                true,
                true,
                1800,
                90,
                true,
                true,
                4,
                4,
                3,
                false,
                true,
                true,
                true,
                true,
                true,
                false,
                987,
                1,
                123,
                true,
                true,
                new JusticeFineDebitPersistenceSnapshot(
                    "legacy", 1, 500L, true, 1L, 500, 1000, 500,
                    300, 600, false, true, 1, 1, 0L, 0L, 2L),
                new JusticeVoluntaryPaymentPersistenceSnapshot(
                    "legacy-payment", 1, 500L, 100, 500, 400, 0L,
                    1L, true, 2L, 1, 1, 0L, true),
                new JusticeDisciplinePersistenceSnapshot(
                    "legacy-discipline",
                    (int)JusticeCrimeKind.AssaultOfficer,
                    300),
                inventory,
                true,
                new[]
                {
                    new JusticeActivityCooldownPersistenceSnapshot(
                        "exercise",
                        200)
                });

        JusticeCustodyPersistenceSnapshot token =
            (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                "CreateJusticeSentencePolicyRecoveryToken",
                legacy,
                1);

        Assert.IsNotNull(token);
        Assert.IsTrue(token.Active);
        Assert.AreEqual(1, token.PlayerSlot);
        Assert.AreSame(inventory, token.InventorySnapshot);
        Assert.IsFalse(token.InventoryRemoved);
        Assert.IsFalse(token.WeaponControlsLocked);
        Assert.IsTrue(token.DeferredInventoryRestore);
        Assert.AreEqual(6, token.InventoryState);
        Assert.AreEqual(0, token.InitialSentenceSeconds);
        Assert.AreEqual(0, token.ActivityReductionSeconds);
        Assert.IsNull(token.FineDebitIntent);
        Assert.IsNull(token.VoluntaryPaymentIntent);
        Assert.IsNull(token.DisciplineIntent);
        Assert.IsFalse(token.HasActivityCooldownContainer);
        Assert.AreEqual(0, token.Cooldowns.Count);
        Assert.IsFalse(token.WaitingForRespawn);
        Assert.IsFalse(token.DeathRebindPending);
        Assert.IsFalse(token.LegalReleaseWantedClearAttempted);
        Assert.IsFalse(token.AmnestyWantedClearAttempted);
    }

    [TestMethod]
    public void DeferredInventoryToken_RemainsAttachedUntilItsPhysicalMergeCompletes()
    {
        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                0,
                new JusticeWeaponPersistenceSnapshot[0]);
        JusticeCustodyPersistenceSnapshot deferred =
            new JusticeCustodyPersistenceSnapshot(
                false,
                0,
                false,
                false,
                0,
                0,
                false,
                false,
                6,
                0,
                0,
                true,
                false,
                false,
                false,
                false,
                false,
                true,
                GTA.Game.GenerateHash("player_zero"),
                0,
                0,
                false,
                false,
                null,
                null,
                null,
                inventory,
                false,
                new JusticeActivityCooldownPersistenceSnapshot[0]);
        JusticeCustodyPersistenceSnapshot token =
            (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                "CreateJusticeSentencePolicyRecoveryToken",
                deferred,
                0);
        Assert.IsNotNull(token);
        Assert.IsTrue(token.DeferredInventoryRestore);
        Assert.IsNotNull(token.InventorySnapshot);

        JusticePlayerProfileState profile = new JusticePlayerProfileState(0)
        {
            CaseState = new JusticeCaseState { Enabled = true },
            RecordState = new JusticeRecordState(),
            CustodySnapshot = token
        };
        JusticePlayerProfileState[] profiles =
        {
            profile,
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceCaseState", profile.CaseState);
        SetField(script, "_justiceRecordState", profile.RecordState);
        SetField(script, "_justicePolicyResetRecoveryMask", 1);

        InvokeInstance(script, "SnapshotActiveJusticePlayerProfile");

        Assert.AreSame(
            token,
            profile.CustodySnapshot,
            "Un snapshot runtime vide ne doit pas effacer le dernier jeton différé durable.");
        Assert.AreEqual(1, GetField<int>(script, "_justicePolicyResetRecoveryMask"));
    }

    [TestMethod]
    public void PhysicalRecoveryGuard_CoversPoliceRespawnAndCustodyLatches()
    {
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "HasJusticeSentencePolicyPhysicalRecoveryState"));

        string[] guardedFlags =
        {
            "_justicePoliceSuppressionRestorePending",
            "_justiceCustodyRespawnRestorePending",
            "_justiceCustodyRespawnMaskNeedsRearm",
            "_justiceCustodyRuntimeActive",
            "_justiceCustodyDeathStatePersistencePending"
        };
        for (int index = 0; index < guardedFlags.Length; index++)
        {
            SetField(script, guardedFlags[index], true);
            Assert.IsTrue(
                (bool)InvokeInstance(
                    script,
                    "HasJusticeSentencePolicyPhysicalRecoveryState"),
                guardedFlags[index] + " doit conserver le bit policy.");
            SetField(script, guardedFlags[index], false);
        }
    }

    [TestMethod]
    public void GlobalPoliceRecovery_ClearsOnlyInactivePolicyTokensNowEmpty()
    {
        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                0,
                new JusticeWeaponPersistenceSnapshot[0]);
        JusticeCustodyPersistenceSnapshot policeOnly =
            new JusticeCustodyPersistenceSnapshot(
                false, 0, true, true, 0, 0, false, false, 0, 0, 0,
                false, false, false, false, false, false, true,
                101, 1, 0, false, false, null, null, null, null,
                false, new JusticeActivityCooldownPersistenceSnapshot[0]);
        JusticeCustodyPersistenceSnapshot policeAndInventory =
            new JusticeCustodyPersistenceSnapshot(
                false, 0, true, true, 0, 0, false, false, 6, 0, 0,
                true, false, false, false, false, false, true,
                102, 2, 0, false, false, null, null, null, inventory,
                false, new JusticeActivityCooldownPersistenceSnapshot[0]);
        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1)
            {
                CustodySnapshot = policeOnly
            },
            new JusticePlayerProfileState(2)
            {
                CustodySnapshot = policeAndInventory
            }
        };
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justicePolicyResetRecoveryMask", 6);

        Assert.IsTrue((bool)InvokeInstance(
            script,
            "TryClearJusticeInactiveProfilePoliceSuppressionTokens"));

        Assert.AreEqual(4, GetField<int>(script, "_justicePolicyResetRecoveryMask"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePolicyResetRecoveryPublicationPending"));
        Assert.IsNull(profiles[1].CustodySnapshot);
        Assert.IsNotNull(profiles[2].CustodySnapshot);
        Assert.IsFalse(profiles[2].CustodySnapshot.PoliceSuppressionApplied);
        Assert.IsFalse(profiles[2].CustodySnapshot.PoliceDispatchDisabled);
        Assert.IsTrue(profiles[2].CustodySnapshot.DeferredInventoryRestore);
        Assert.IsNotNull(profiles[2].CustodySnapshot.InventorySnapshot);
    }

#if DONJ_STUB_API
    [TestMethod]
    public void DeferredInventoryRecovery_IsAdvancedByPolicyControllerWhileLateTickIsBlocked()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = new GTA.Ped
        {
            Handle = 701,
            Model = new GTA.Model("player_zero"),
            IsDead = true
        };
        GTA.Game.Player.Character = player;

        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        JusticeInventoryPersistenceSnapshot persistedInventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                0,
                new JusticeWeaponPersistenceSnapshot[0]);
        object runtimeInventory = InvokeStatic(
            "RestoreJusticeInventorySnapshot",
            persistedInventory);
        SetField(script, "_justiceWeaponSnapshot", runtimeInventory);
        SetField(script, "_justiceDeferredInventoryRestore", true);
        SetPrivateEnumField(script, "_justiceInventoryCustodyState", "RestorePending");
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justicePolicyResetRecoveryMask", 1);

        Assert.IsFalse((bool)InvokeInstance(
            script,
            "TryFinalizeJusticeSentencePolicyDeferredInventoryRecovery"));
        Assert.IsTrue(GetField<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.AreEqual(1, GetField<int>(script, "_justicePolicyResetRecoveryMask"));
        Assert.IsTrue((bool)InvokeInstance(
            script,
            "HasJusticeSentencePolicyPhysicalRecoveryState"));

        player.IsDead = false;
        Assert.IsTrue((bool)InvokeInstance(
            script,
            "TryFinalizeJusticeSentencePolicyDeferredInventoryRecovery"));
        Assert.IsFalse(GetField<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsNull(GetField<object>(script, "_justiceWeaponSnapshot"));
        Assert.AreEqual(
            "None",
            GetField<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.AreEqual(
            1,
            GetField<int>(script, "_justicePolicyResetRecoveryMask"),
            "Le merge physique ne doit pas acquitter lui-même le commit policy.");
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "HasJusticeSentencePolicyPhysicalRecoveryState"));
    }
#endif

    [TestMethod]
    public void CurrentCustodyWriter_NeverEmitsRemovedActivityOrDisciplineFields()
    {
        JusticeCustodyPersistenceSnapshot legacyShaped =
            new JusticeCustodyPersistenceSnapshot(
                false,
                0,
                false,
                false,
                0,
                75,
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
                0,
                -1,
                -1569615261,
                false,
                false,
                null,
                null,
                new JusticeDisciplinePersistenceSnapshot(
                    "legacy",
                    (int)JusticeCrimeKind.SimpleAssault,
                    30),
                null,
                true,
                new[]
                {
                    new JusticeActivityCooldownPersistenceSnapshot("legacy", 20)
                });

        string xml = DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(
            legacyShaped);
        XElement custody = XElement.Parse(xml);

        Assert.IsNull(custody.Attribute("activityReductionSeconds"));
        Assert.IsNull(custody.Element("DisciplineIntent"));
        Assert.IsNull(custody.Element("ActivityCooldowns"));
    }

    [TestMethod]
    public void PolicyV2Reader_RejectsRemovedFieldsWhileLegacyResetStillAcceptsThem()
    {
        XElement custody = XElement.Parse(
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(
                CreateEmptyCustody(0)));
        custody.SetAttributeValue("activityReductionSeconds", "30");
        custody.Add(new XElement(
            "DisciplineIntent",
            new XAttribute("operationId", "legacy"),
            new XAttribute("crimeKind", "SimpleAssault"),
            new XAttribute("extraSentenceSeconds", "30")));
        custody.Add(new XElement("ActivityCooldowns"));
        string retiredCustody = custody.ToString(SaveOptions.DisableFormatting);

        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-fields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            string currentPath = Path.Combine(directory, "current.xml");
            File.WriteAllBytes(
                currentPath,
                codec.Serialize(CreatePolicySnapshot(
                    8L,
                    0,
                    2,
                    retiredCustody)));
            object currentReader = CreateHeadlessScript();
            SetField(
                currentReader,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            Assert.IsFalse((bool)InvokeInstance(
                currentReader,
                "TryReadJusticeStateFile",
                currentPath));

            string legacyPath = Path.Combine(directory, "legacy.xml");
            File.WriteAllBytes(
                legacyPath,
                codec.Serialize(CreatePolicySnapshot(
                    9L,
                    0,
                    1,
                    retiredCustody)));
            object legacyReader = CreateHeadlessScript();
            SetField(
                legacyReader,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));
            Assert.IsTrue((bool)InvokeInstance(
                legacyReader,
                "TryReadJusticeStateFile",
                legacyPath));
            Assert.AreEqual(
                2,
                GetField<int>(legacyReader, "_justiceSentencePolicyVersion"));
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

    [TestMethod]
    public void CurrentPolicyReader_ReloadsAnActiveTechnicalTokenWithItsMatchingBit()
    {
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-active-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            JusticeCustodyPersistenceSnapshot token =
                (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                    "CreateJusticeSentencePolicyRecoveryToken",
                    CreatePlayerStateRecoveryCustody(1, 24680),
                    1);
            JusticePersistenceSnapshot snapshot = CreatePolicySnapshot(
                14L,
                2,
                2,
                DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(token),
                1,
                1);
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            byte[] bytes = codec.Serialize(snapshot);
            string primary = Path.Combine(directory, "_justice_state.xml");
            File.WriteAllBytes(primary, bytes);
            File.WriteAllBytes(primary + ".bak", bytes);

            object script = CreateHeadlessScript();
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 1));
            Assert.IsTrue((bool)InvokeInstance(
                script,
                "TryReadJusticeStateFile",
                primary));
            Assert.AreEqual(1, GetField<int>(
                script,
                "_justiceActivePlayerProfileSlot"));
            Assert.AreEqual(2, GetField<int>(
                script,
                "_justicePolicyResetRecoveryMask"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyPlayerStateStored"));
            Assert.AreEqual(1, GetField<int>(script, "_justiceCustodyPlayerSlot"));
            Assert.AreEqual(24680, GetField<int>(
                script,
                "_justiceCustodyPlayerModelHash"));
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

    [TestMethod]
    public void SemanticWriter_RejectsANonCanonicalPolicyRecoveryToken()
    {
        JusticeCustodyPersistenceSnapshot token =
            (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                "CreateJusticeSentencePolicyRecoveryToken",
                CreatePlayerStateRecoveryCustody(0, 13579),
                0);
        XElement custody = XElement.Parse(
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(token));
        custody.SetAttributeValue("initialSentenceSeconds", "30");
        JusticePersistenceSnapshot snapshot = CreatePolicySnapshot(
            15L,
            1,
            2,
            custody.ToString(SaveOptions.DisableFormatting),
            0,
            0);
        object[] validationArguments = { snapshot, string.Empty };

        Assert.IsFalse((bool)InvokeStatic(
            "TryValidateJusticePersistenceSnapshotSemantics",
            validationArguments));
        StringAssert.Contains(
            (string)validationArguments[1],
            "Jetons techniques de récupération Justice v2 invalides");
    }

    [TestMethod]
    public void LegacyCustodyReader_RejectsInventoryRecoveryWithoutSnapshot()
    {
        string[] invalidAttributes =
        {
            "inventoryState='2'",
            "inventoryState='3'",
            "inventoryState='4' inventoryRemoved='true'",
            "inventoryState='6' deferredInventoryRestore='true'",
            "inventoryState='7' deferredInventoryRestore='true'",
            "inventoryRemoved='true'",
            "deferredInventoryRestore='true'"
        };
        for (int index = 0; index < invalidAttributes.Length; index++)
        {
            XmlElement custody = CreateLegacyCustodyElement(
                invalidAttributes[index]);
            object[] arguments = { custody, 0, null };
            Assert.IsFalse(
                (bool)typeof(DonJEnemySpawner).GetMethod(
                    "TryReadJusticeLegacyPolicyCustody",
                    PrivateStatic).Invoke(null, arguments),
                invalidAttributes[index]);
        }

        object[] supportedArguments =
        {
            CreateLegacyCustodyElement("inventoryState='5'"),
            0,
            null
        };
        Assert.IsTrue((bool)typeof(DonJEnemySpawner).GetMethod(
            "TryReadJusticeLegacyPolicyCustody",
            PrivateStatic).Invoke(null, supportedArguments));
    }

    [TestMethod]
    public void PresentButInvalidPolicyVersion_IsRejectedWithoutStartingReset()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-invalid-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string[] invalidVersions = { "not-a-version", "-1", "0" };
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            for (int index = 0; index < invalidVersions.Length; index++)
            {
                JusticePersistenceSnapshot invalid = ReplacePolicyVersion(
                    CreatePolicySnapshot(20L + index, 0),
                    invalidVersions[index]);
                string path = Path.Combine(
                    directory,
                    "invalid-" + index.ToString() + ".xml");
                File.WriteAllBytes(path, codec.Serialize(invalid));

                object script = CreateHeadlessScript();
                SetField(script, "_justiceSentencePolicyVersion", 2);
                SetField(
                    script,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));

                Assert.IsFalse(
                    (bool)InvokeInstance(script, "TryReadJusticeStateFile", path),
                    invalidVersions[index]);
                Assert.AreEqual(
                    2,
                    GetField<int>(script, "_justiceSentencePolicyVersion"));
                Assert.IsFalse(GetField<bool>(
                    script,
                    "_justicePolicyResetPublicationPending"));
                Assert.AreEqual(
                    0,
                    GetField<int>(script, "_justicePolicyResetRecoveryMask"));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void PolicyRecoveryMask_ExactlyTracksTechnicalRecoveryTokens()
    {
        JusticeCustodyPersistenceSnapshot token =
            (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                "CreateJusticeSentencePolicyRecoveryToken",
                CreateActivePhysicalRecoveryCustody(0),
                0);
        Assert.IsNotNull(token);

        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0)
            {
                CaseState = new JusticeCaseState(),
                RecordState = new JusticeRecordState(),
                CustodySnapshot = token
            },
            new JusticePlayerProfileState(1)
            {
                CaseState = new JusticeCaseState(),
                RecordState = new JusticeRecordState(),
                CustodySnapshot = CreateEmptyCustody(1)
            },
            new JusticePlayerProfileState(2)
            {
                CaseState = new JusticeCaseState(),
                RecordState = new JusticeRecordState(),
                CustodySnapshot = CreateEmptyCustody(2)
            }
        };

        Assert.IsFalse((bool)InvokeStatic(
            "AreJusticeSentencePolicyRecoveryTokensValid",
            profiles,
            0));
        Assert.IsTrue((bool)InvokeStatic(
            "AreJusticeSentencePolicyRecoveryTokensValid",
            profiles,
            1));

        // Je distingue le jeton technique du snapshot d'une nouvelle détention.
        profiles[0].CaseState.Phase = JusticePhase.Incarcerated;
        profiles[0].CaseState.SentenceSeconds = 30;
        Assert.IsTrue((bool)InvokeStatic(
            "AreJusticeSentencePolicyRecoveryTokensValid",
            profiles,
            0));
    }

    [TestMethod]
    public void PolicyPoliceRecoveryGate_NeverUsesNormalCustodyFlagsAsProof()
    {
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldRestoreJusticeSentencePolicyPoliceState",
            false,
            false));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldRestoreJusticeSentencePolicyPoliceState",
            true,
            false));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldRestoreJusticeSentencePolicyPoliceState",
            false,
            true));
    }

    [TestMethod]
    public void PolicyController_DoesNotRestorePoliceDuringANormalCustody()
    {
        object script = CreateHeadlessScript();
        SetField(script, "_justiceSentencePolicyVersion", 2);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justicePolicyResetRecoveryMask", 0);
        SetField(script, "_justicePolicyResetPublicationPending", false);
        SetField(script, "_justicePolicyResetRecoveryPublicationPending", false);
        SetField(script, "_justicePoliceIgnoreApplied", true);
        SetField(script, "_justicePoliceDispatchDisabled", true);
        SetField(script, "_justicePoliceSuppressionActive", true);
        SetField(script, "_justicePoliceSuppressionRestorePending", false);

        Assert.IsTrue((bool)InvokeInstance(
            script,
            "ResumeJusticeSentencePolicyUpgradeIfRequired"));
        Assert.IsTrue(GetField<bool>(script, "_justicePoliceIgnoreApplied"));
        Assert.IsTrue(GetField<bool>(script, "_justicePoliceDispatchDisabled"));
        Assert.IsTrue(GetField<bool>(script, "_justicePoliceSuppressionActive"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justicePolicyResetRecoveryPublicationPending"));
    }

    [TestMethod]
    public void LegacyFileQuarantine_IsIdempotentAndRejectsDifferentCollision()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "legacy.xml");
            string destination = Path.Combine(directory, "quarantine.xml");
            File.WriteAllText(source, "legacy-one");
            InvokeStatic(
                "MoveJusticeSentencePolicyLegacyFile",
                source,
                destination);
            Assert.IsFalse(File.Exists(source));
            Assert.AreEqual("legacy-one", File.ReadAllText(destination));

            File.WriteAllText(source, "legacy-one");
            InvokeStatic(
                "MoveJusticeSentencePolicyLegacyFile",
                source,
                destination);
            Assert.IsFalse(File.Exists(source));

            File.WriteAllText(source, "legacy-two");
            TargetInvocationException collision = Assert.ThrowsException<
                TargetInvocationException>(() => InvokeStatic(
                    "MoveJusticeSentencePolicyLegacyFile",
                    source,
                    destination));
            Assert.IsInstanceOfType(collision.InnerException, typeof(InvalidDataException));
            Assert.AreEqual("legacy-one", File.ReadAllText(destination));
            Assert.AreEqual("legacy-two", File.ReadAllText(source));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void QuarantineResume_AlwaysMovesCanonicalWalBeforeRepositoryStarts()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-reset-resume-" + Guid.NewGuid().ToString("N"));
        string quarantine = Path.Combine(
            directory,
            "_justice_policy_v1.quarantine");
        Directory.CreateDirectory(quarantine);
        try
        {
            string quarantinedPrimary = Path.Combine(
                quarantine,
                "legacy-primary.xml");
            string canonicalWal = Path.Combine(directory, "_justice_state.wal");
            File.WriteAllText(quarantinedPrimary, "legacy-primary");
            File.WriteAllText(canonicalWal, "legacy-wal-not-yet-moved");

            object script = FormatterServices.GetUninitializedObject(
                typeof(DonJEnemySpawner));
            SetField(script, "_justicePolicyResetPublicationPending", true);
            SetField(
                script,
                "_justicePolicyResetLegacySourcePath",
                quarantinedPrimary);

            InvokeInstance(
                script,
                "PrepareJusticeSentencePolicyQuarantine",
                directory);

            Assert.IsFalse(File.Exists(canonicalWal));
            Assert.AreEqual(
                "legacy-wal-not-yet-moved",
                File.ReadAllText(Path.Combine(quarantine, "legacy.wal")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void QuarantineResume_RecoversEveryPrimaryBackupWalCrashBoundary()
    {
        for (int completedMoves = 0; completedMoves <= 3; completedMoves++)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "donj-policy-reset-boundary-" +
                completedMoves.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N"));
            string quarantine = Path.Combine(
                directory,
                "_justice_policy_v1.quarantine");
            Directory.CreateDirectory(quarantine);
            try
            {
                string canonicalPrimary = Path.Combine(directory, "_justice_state.xml");
                string canonicalBackup = canonicalPrimary + ".bak";
                string canonicalWal = Path.Combine(directory, "_justice_state.wal");
                string quarantinedPrimary = Path.Combine(quarantine, "legacy-primary.xml");
                string quarantinedBackup = Path.Combine(quarantine, "legacy-backup.xml");
                string quarantinedWal = Path.Combine(quarantine, "legacy.wal");

                File.WriteAllText(
                    completedMoves >= 1 ? quarantinedPrimary : canonicalPrimary,
                    "legacy-primary");
                File.WriteAllText(
                    completedMoves >= 2 ? quarantinedBackup : canonicalBackup,
                    "legacy-backup");
                File.WriteAllText(
                    completedMoves >= 3 ? quarantinedWal : canonicalWal,
                    "legacy-wal");

                object script = FormatterServices.GetUninitializedObject(
                    typeof(DonJEnemySpawner));
                SetField(script, "_justicePolicyResetPublicationPending", true);
                SetField(
                    script,
                    "_justicePolicyResetLegacySourcePath",
                    completedMoves >= 1 ? quarantinedPrimary : canonicalPrimary);

                InvokeInstance(
                    script,
                    "PrepareJusticeSentencePolicyQuarantine",
                    directory);
                // Je répète immédiatement l'étape comme après un second crash :
                // chaque frontière doit rester sans effet de bord supplémentaire.
                InvokeInstance(
                    script,
                    "PrepareJusticeSentencePolicyQuarantine",
                    directory);

                Assert.IsFalse(File.Exists(canonicalPrimary));
                Assert.IsFalse(File.Exists(canonicalBackup));
                Assert.IsFalse(File.Exists(canonicalWal));
                Assert.AreEqual("legacy-primary", File.ReadAllText(quarantinedPrimary));
                Assert.AreEqual("legacy-backup", File.ReadAllText(quarantinedBackup));
                Assert.AreEqual("legacy-wal", File.ReadAllText(quarantinedWal));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }

    [TestMethod]
    public void Quarantine_PreservesDistinctPrimaryAndBackupPathsWhenBytesMatch()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-identical-pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            File.WriteAllText(primary, "same-legacy-revision");
            File.WriteAllText(backup, "same-legacy-revision");

            object script = FormatterServices.GetUninitializedObject(
                typeof(DonJEnemySpawner));
            SetField(script, "_justicePolicyResetPublicationPending", true);
            SetField(script, "_justicePolicyResetLegacySourcePath", primary);
            InvokeInstance(
                script,
                "PrepareJusticeSentencePolicyQuarantine",
                directory);

            string quarantine = Path.Combine(
                directory,
                "_justice_policy_v1.quarantine");
            Assert.IsFalse(File.Exists(primary));
            Assert.IsFalse(File.Exists(backup));
            Assert.AreEqual(
                "same-legacy-revision",
                File.ReadAllText(Path.Combine(quarantine, "legacy-primary.xml")));
            Assert.AreEqual(
                "same-legacy-revision",
                File.ReadAllText(Path.Combine(quarantine, "legacy-backup.xml")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void QuarantineResume_PreservesPublishedPolicyGenerationAndFreshWal()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-current-quarantine-" + Guid.NewGuid().ToString("N"));
        string quarantine = Path.Combine(
            directory,
            "_justice_policy_v1.quarantine");
        Directory.CreateDirectory(quarantine);
        try
        {
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            byte[] current = codec.Serialize(CreatePolicySnapshot(12L, 0));
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            string wal = Path.Combine(directory, "_justice_state.wal");
            File.WriteAllBytes(primary, current);
            File.WriteAllBytes(backup, current);
            File.WriteAllText(wal, "fresh-policy-wal");
            string legacy = Path.Combine(quarantine, "legacy-primary.xml");
            File.WriteAllText(legacy, "legacy-evidence");

            object script = FormatterServices.GetUninitializedObject(
                typeof(DonJEnemySpawner));
            SetField(script, "_justicePolicyResetPublicationPending", true);
            SetField(script, "_justicePolicyResetRecoveryMask", 0);
            SetField(script, "_justicePolicyResetLegacySourcePath", legacy);

            InvokeInstance(
                script,
                "PrepareJusticeSentencePolicyQuarantine",
                directory);
            InvokeInstance(
                script,
                "PrepareJusticeSentencePolicyQuarantine",
                directory);

            CollectionAssert.AreEqual(current, File.ReadAllBytes(primary));
            CollectionAssert.AreEqual(current, File.ReadAllBytes(backup));
            Assert.AreEqual("fresh-policy-wal", File.ReadAllText(wal));
            Assert.AreEqual("legacy-evidence", File.ReadAllText(legacy));
            Assert.IsTrue(File.Exists(Path.Combine(
                quarantine,
                "quarantine.complete")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void QuarantineResume_MapsRepairedPrimaryToTheRemainingLegacySlot()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-repaired-primary-" + Guid.NewGuid().ToString("N"));
        string quarantine = Path.Combine(
            directory,
            "_justice_policy_v1.quarantine");
        Directory.CreateDirectory(quarantine);
        try
        {
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            string quarantinedPrimary = Path.Combine(
                quarantine,
                "legacy-primary.xml");
            File.WriteAllText(quarantinedPrimary, "legacy-revision-a");
            File.WriteAllText(primary, "legacy-revision-b");
            File.WriteAllText(backup, "legacy-revision-b");

            object script = FormatterServices.GetUninitializedObject(
                typeof(DonJEnemySpawner));
            SetField(script, "_justicePolicyResetPublicationPending", true);
            SetField(
                script,
                "_justicePolicyResetLegacySourcePath",
                quarantinedPrimary);
            InvokeInstance(
                script,
                "PrepareJusticeSentencePolicyQuarantine",
                directory);

            Assert.IsFalse(File.Exists(primary));
            Assert.IsFalse(File.Exists(backup));
            Assert.AreEqual(
                "legacy-revision-a",
                File.ReadAllText(quarantinedPrimary));
            Assert.AreEqual(
                "legacy-revision-b",
                File.ReadAllText(Path.Combine(
                    quarantine,
                    "legacy-backup.xml")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void WalOnlyQuarantine_RemainsVisibleAndCanFinishCleanup()
    {
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-wal-only-" + Guid.NewGuid().ToString("N"));
        string quarantine = Path.Combine(
            directory,
            "_justice_policy_v1.quarantine");
        Directory.CreateDirectory(quarantine);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            File.WriteAllText(
                Path.Combine(quarantine, "legacy.wal"),
                "legacy-wal");
            object script = FormatterServices.GetUninitializedObject(
                typeof(DonJEnemySpawner));

            Assert.IsTrue((bool)InvokeInstance(
                script,
                "HasJusticeSentencePolicyQuarantine"));
            InvokeInstance(
                script,
                "DeleteJusticeSentencePolicyQuarantineIfPresent");
            Assert.IsFalse(Directory.Exists(quarantine));
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

#if DONJ_STUB_API
    [TestMethod]
    public void ActivePolicyRecovery_MergesWithoutRemoveAllCashOrWantedMutation()
    {
        GTA.StubRuntime.Reset();
        GTA.Ped player = new GTA.Ped
        {
            Handle = 702,
            Model = new GTA.Model("player_zero"),
            IsDead = false,
            IsInvincible = true,
            FreezePosition = true,
            CanRagdoll = false
        };
        GTA.Game.Player.Character = player;
        GTA.Game.Player.WantedLevel = 2;
        GTA.Game.Player.Money = 4321;

        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                0,
                new JusticeWeaponPersistenceSnapshot[0]);
        JusticeCustodyPersistenceSnapshot token =
            new JusticeCustodyPersistenceSnapshot(
                false, 0, false, false, 0, 0, false, false, 6, 0, 0,
                true, false, false, true, false, true, true,
                player.Model.Hash, 0, 0, false, false,
                null, null, null, inventory, false,
                new JusticeActivityCooldownPersistenceSnapshot[0]);
        object script = CreateHeadlessScript();
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetField(script, "_justiceCustodyPlayerStateStored", true);
        SetField(script, "_justiceCustodyStoredInvincible", false);
        SetField(script, "_justiceCustodyStoredFrozen", true);
        SetField(script, "_justiceCustodyStoredCanRagdoll", true);
        SetField(
            script,
            "_justiceWeaponSnapshot",
            InvokeStatic("RestoreJusticeInventorySnapshot", inventory));
        SetField(script, "_justiceDeferredInventoryRestore", true);
        SetPrivateEnumField(
            script,
            "_justiceInventoryCustodyState",
            "RestorePending");
        int removeAllBefore = player.Weapons.RemoveAllCount;

        Assert.IsTrue((bool)InvokeInstance(
            script,
            "TryRestoreJusticeSentencePolicyActivePlayer",
            token));
        Assert.AreEqual(removeAllBefore, player.Weapons.RemoveAllCount);
        Assert.AreEqual(4321, GTA.Game.Player.Money);
        Assert.AreEqual(2, GTA.Game.Player.WantedLevel);
        Assert.IsFalse(player.IsInvincible);
        Assert.IsFalse(player.FreezePosition);
        Assert.IsTrue(player.CanRagdoll);
        Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.IsFalse(GetField<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsNull(GetField<object>(script, "_justiceWeaponSnapshot"));
    }

    [TestMethod]
    public void InactivePolicyRecovery_WaitsForItsOwnerThenPublishesAndDoesNotResetAgain()
    {
        GTA.StubRuntime.Reset();
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-owner-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        object script = null;
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            int currentSlot = 0;
            GTA.Ped michael = new GTA.Ped
            {
                Handle = 710,
                Model = new GTA.Model("player_zero"),
                IsDead = false
            };
            GTA.Game.Player.Character = michael;

            GTA.Model franklinModel = new GTA.Model("player_one");
            JusticeCustodyPersistenceSnapshot token =
                (JusticeCustodyPersistenceSnapshot)InvokeStatic(
                    "CreateJusticeSentencePolicyRecoveryToken",
                    CreatePlayerStateRecoveryCustody(
                        1,
                        franklinModel.Hash),
                    1);
            string tokenXml =
                DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(token);
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            byte[] initial = codec.Serialize(CreatePolicySnapshot(
                30L,
                2,
                2,
                tokenXml,
                1));
            JusticePersistenceSnapshot decoded;
            string decodeError;
            Assert.IsTrue(
                codec.TryDeserialize(initial, out decoded, out decodeError),
                "Le codec doit relire le snapshot policy : " + decodeError);
            object[] rootArguments = { decoded, null, string.Empty };
            Assert.IsTrue(
                (bool)InvokeStatic(
                    "TryCreateLegacyJusticeRootFromSnapshot",
                    rootArguments),
                "Le miroir legacy du snapshot policy doit être reconstructible : " +
                (string)rootArguments[2]);
            object[] profileArguments =
            {
                (XmlElement)rootArguments[1],
                null,
                -1,
                false,
                2
            };
            Assert.IsTrue((bool)InvokeStatic(
                "TryReadJusticePlayerProfilesXml",
                profileArguments),
                "Les trois profils du miroir policy doivent être relus.");
            object hydrationProbe = CreateHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(
                hydrationProbe,
                "TryHydrateJusticeV2CustodySnapshots",
                (JusticePlayerProfileState[])profileArguments[1],
                2),
                "Le bit policy doit autoriser uniquement l'hydratation du jeton technique.");
            Assert.IsTrue((bool)InvokeStatic(
                "AreJusticeSentencePolicyRecoveryTokensValid",
                (JusticePlayerProfileState[])profileArguments[1],
                2),
                "Le masque et le jeton hydraté doivent être strictement équivalents.");
            string primary = Path.Combine(directory, "_justice_state.xml");
            File.WriteAllBytes(primary, initial);
            File.WriteAllBytes(primary + ".bak", initial);

            script = CreateHeadlessScript();
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => currentSlot));
            Assert.IsTrue((bool)InvokeInstance(
                script,
                "TryReadJusticeStateFile",
                primary),
                "Le snapshot policy v2 avec jeton inactif doit être relu.");
            InvokeInstance(script, "InitializeJusticePersistenceServices");

            Assert.IsTrue((bool)InvokeInstance(
                script,
                "ResumeJusticeSentencePolicyUpgradeIfRequired"),
                "Le héros non propriétaire ne doit pas être bloqué par le jeton inactif.");
            Assert.AreEqual(2, GetField<int>(
                script,
                "_justicePolicyResetRecoveryMask"));
            Assert.AreEqual(710, GTA.Game.Player.Character.Handle);

            GTA.Ped franklin = new GTA.Ped
            {
                Handle = 711,
                Model = franklinModel,
                IsDead = false,
                IsInvincible = true,
                FreezePosition = true,
                CanRagdoll = false
            };
            GTA.Game.Player.Character = franklin;
            currentSlot = 1;
            Assert.IsTrue((bool)InvokeInstance(
                script,
                "ActivateJusticePlayerProfile",
                1),
                "Le profil propriétaire du jeton doit pouvoir être activé.");

            Assert.IsFalse((bool)InvokeInstance(
                script,
                "ResumeJusticeSentencePolicyUpgradeIfRequired"));
            Assert.AreEqual(0, GetField<int>(
                script,
                "_justicePolicyResetRecoveryMask"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justicePolicyResetRecoveryPublicationPending"));
            Assert.IsFalse(franklin.IsInvincible);
            Assert.IsFalse(franklin.FreezePosition);
            Assert.IsTrue(franklin.CanRagdoll);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool complete = (bool)InvokeInstance(
                    script,
                    "ResumeJusticeSentencePolicyUpgradeIfRequired");
                if (complete)
                {
                    break;
                }
                Assert.IsTrue((bool)InvokeInstance(
                    script,
                    "JusticeAwaitQueuedPersistenceForTests"),
                    "Chaque publication policy demandée doit atteindre le disque.");
            }
            Assert.IsTrue((bool)InvokeInstance(
                script,
                "ResumeJusticeSentencePolicyUpgradeIfRequired"),
                "La reprise doit terminer après redondance primaire/backup.");
            Assert.IsTrue((bool)InvokeStatic(
                "IsJusticeSentencePolicySnapshotPairRedundant",
                primary,
                0),
                "La paire finale doit porter un masque nul.");

            InvokeInstance(script, "ShutdownJusticePersistenceServices");
            script = null;
            object restarted = CreateHeadlessScript();
            SetField(
                restarted,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 1));
            Assert.IsTrue((bool)InvokeInstance(
                restarted,
                "TryReadJusticeStateFile",
                primary),
                "Le snapshot final doit redémarrer sans second reset.");
            Assert.AreEqual(2, GetField<int>(
                restarted,
                "_justiceSentencePolicyVersion"));
            Assert.AreEqual(0, GetField<int>(
                restarted,
                "_justicePolicyResetRecoveryMask"));
            Assert.IsFalse(GetField<bool>(
                restarted,
                "_justicePolicyResetPublicationPending"));
        }
        finally
        {
            if (script != null)
            {
                InvokeInstance(script, "ShutdownJusticePersistenceServices");
            }
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
#endif

    [TestMethod]
    public void RecoveryCommit_RemainsBlockedUntilPrimaryAndBackupCarryTheSameMask()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-reset-pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            File.WriteAllBytes(primary, codec.Serialize(CreatePolicySnapshot(5L, 1)));
            File.WriteAllBytes(backup, codec.Serialize(CreatePolicySnapshot(4L, 0)));

            Assert.IsFalse((bool)InvokeStatic(
                "IsJusticeSentencePolicySnapshotPairRedundant",
                primary,
                1));

            File.WriteAllBytes(backup, codec.Serialize(CreatePolicySnapshot(6L, 1)));
            Assert.IsTrue((bool)InvokeStatic(
                "IsJusticeSentencePolicySnapshotPairRedundant",
                primary,
                1));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void CurrentPolicySnapshot_ReloadsWithoutASecondReset()
    {
        string previous = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "donj-policy-current-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                directory);
            string primary = Path.Combine(directory, "_justice_state.xml");
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            byte[] current = codec.Serialize(CreatePolicySnapshot(7L, 0));
            File.WriteAllBytes(primary, current);
            File.WriteAllBytes(primary + ".bak", current);

            object script = CreateHeadlessScript();
            SetField(
                script,
                "_justiceCanonicalPlayerSlotOverride",
                new Func<int>(() => 0));

            Assert.IsTrue((bool)InvokeInstance(
                script,
                "TryReadJusticeStateFile",
                primary));
            Assert.AreEqual(2, GetField<int>(script, "_justiceSentencePolicyVersion"));
            Assert.AreEqual(0, GetField<int>(script, "_justicePolicyResetRecoveryMask"));
            Assert.IsFalse(GetField<bool>(script, "_justicePolicyResetPublicationPending"));
            Assert.IsFalse(GetField<bool>(
                script,
                "_justicePolicyResetRecoveryPublicationPending"));
            Assert.AreEqual(7L, GetField<long>(script, "_justicePersistenceRevision"));
            Assert.AreEqual(
                string.Empty,
                GetField<string>(script, "_justicePolicyResetLegacySourcePath"));
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

    private static JusticeCustodyPersistenceSnapshot CreateEmptyCustody(int slot)
    {
        return new JusticeCustodyPersistenceSnapshot(
            false,
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
            0,
            -1,
            -1569615261,
            false,
            false,
            null,
            null,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateActivePhysicalRecoveryCustody(int slot)
    {
        return CreateActivePhysicalRecoveryCustody(slot, 12345 + slot);
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateActivePhysicalRecoveryCustody(int slot, int playerModelHash)
    {
        return new JusticeCustodyPersistenceSnapshot(
            true,
            2,
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
            playerModelHash,
            slot,
            -1569615261,
            false,
            false,
            null,
            null,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static JusticeCustodyPersistenceSnapshot
        CreatePlayerStateRecoveryCustody(int slot, int playerModelHash)
    {
        return new JusticeCustodyPersistenceSnapshot(
            false,
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
            true,
            false,
            false,
            true,
            playerModelHash,
            slot,
            -1569615261,
            false,
            false,
            null,
            null,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static JusticePersistenceSnapshot ReplacePolicyVersion(
        JusticePersistenceSnapshot source,
        string policyVersion)
    {
        List<JusticePersistenceField> globals =
            new List<JusticePersistenceField>();
        for (int index = 0; index < source.GlobalFields.Count; index++)
        {
            JusticePersistenceField field = source.GlobalFields[index];
            globals.Add(new JusticePersistenceField(
                field.Path,
                string.Equals(
                    field.Path,
                    "sentencePolicyVersion",
                    StringComparison.Ordinal)
                        ? policyVersion
                        : field.Value));
        }
        return new JusticePersistenceSnapshot(
            source.Revision,
            source.SchemaVersion,
            source.CapturedAtUtcTicks,
            source.ActiveProfileSlot,
            globals,
            source.Profiles);
    }

    private static JusticePersistenceSnapshot CreatePolicySnapshot(
        long revision,
        int recoveryMask)
    {
        return CreatePolicySnapshot(revision, recoveryMask, 2, null);
    }

    private static JusticePersistenceSnapshot CreatePolicySnapshot(
        long revision,
        int recoveryMask,
        int policyVersion,
        string custodyXml)
    {
        return CreatePolicySnapshot(
            revision,
            recoveryMask,
            policyVersion,
            custodyXml,
            0);
    }

    private static JusticePersistenceSnapshot CreatePolicySnapshot(
        long revision,
        int recoveryMask,
        int policyVersion,
        string custodyXml,
        int custodySlot)
    {
        return CreatePolicySnapshot(
            revision,
            recoveryMask,
            policyVersion,
            custodyXml,
            custodySlot,
            0);
    }

    private static JusticePersistenceSnapshot CreatePolicySnapshot(
        long revision,
        int recoveryMask,
        int policyVersion,
        string custodyXml,
        int custodySlot,
        int activeProfileSlot)
    {
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>();
        for (int slot = 0; slot < 3; slot++)
        {
            profiles.Add(new JusticePersistenceProfileSnapshot(
                slot,
                revision,
                "slot:" + slot,
                new[]
                {
                    new JusticePersistenceField(
                        "Case",
                        "<Case enabled=\"false\" />"),
                    new JusticePersistenceField("Record", "<Record />"),
                    new JusticePersistenceField(
                        "Custody",
                        slot == custodySlot && custodyXml != null
                            ? custodyXml
                            : DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(
                                CreateEmptyCustody(slot)))
                }));
        }

        return new JusticePersistenceSnapshot(
            revision,
            JusticeXmlPersistenceCodec.SchemaMajor,
            DateTime.UtcNow.Ticks,
            activeProfileSlot,
            new[]
            {
                new JusticePersistenceField(
                    "activePlayerSlot",
                    activeProfileSlot.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                new JusticePersistenceField(
                    "sentencePolicyVersion",
                    policyVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                new JusticePersistenceField(
                    "policyResetRecoveryMask",
                    recoveryMask.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new JusticePersistenceField("nextIdentityGeneration", "0"),
                new JusticePersistenceField(
                    "policeIntegrationMode",
                    ((int)JusticePoliceIntegrationMode.FreeroamBestEffort).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                new JusticePersistenceField(
                    "lastCanonicalPlayerSlot",
                    activeProfileSlot.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                new JusticePersistenceField("lastCanonicalPlayerModel", "0")
            },
            profiles);
    }

    private static XmlElement CreateLegacyCustodyElement(string overrides)
    {
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(
            "<Custody active='false' site='None' " +
            "policeSuppressionApplied='false' policeDispatchDisabled='false' " +
            "initialSentenceSeconds='0' " +
            "waitingForRespawn='false' deathRebindPending='false' " +
            "playerStateStored='false' storedInvincible='false' " +
            "storedFrozen='false' storedCanRagdoll='true' " +
            "playerModelHash='123' playerSlot='0' " +
            "releaseSelectedWeapon='-1569615261' " +
            (overrides ?? string.Empty) + " />");
        return document.DocumentElement;
    }

    private static object CreateHeadlessScript()
    {
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        foreach (string fieldName in new[]
                 {
                     "_justicePendingIncidents",
                     "_justiceRecentVictims",
                     "_justiceRecentVehicles",
                     "_justiceAllyTokens",
                     "_justiceTrackedIdentities",
                     "_justiceSelfDefenseUntilByVictim",
                     "_justiceDamageFrontsToConsume",
                     "_justiceDamagePairBaselines",
                     "_justiceWitnessSnapshots"
                 })
        {
            FieldInfo field = typeof(DonJEnemySpawner).GetField(
                fieldName,
                PrivateInstance);
            Assert.IsNotNull(field, "Collection introuvable : " + fieldName);
            field.SetValue(script, Activator.CreateInstance(field.FieldType, true));
        }
        SetField(
            script,
            "_justiceCaseState",
            new JusticeCaseState { Enabled = true });
        SetField(script, "_justiceRecordState", new JusticeRecordState());
        SetField(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetField(script, "_justiceCustodyPlayerSlot", -1);
        FieldInfo unarmed = typeof(DonJEnemySpawner).GetField(
            "JusticeUnarmedHash",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(unarmed);
        SetField(
            script,
            "_justiceReleaseSelectedWeaponHash",
            (int)unarmed.GetRawConstantValue());
        return script;
    }

    private static object InvokeInstance(object target, string method, params object[] args)
    {
        MethodInfo info = typeof(DonJEnemySpawner).GetMethod(
            method,
            PrivateInstance);
        Assert.IsNotNull(info, "Méthode introuvable : " + method);
        return info.Invoke(target, args);
    }

    private static object InvokeStatic(string method, params object[] args)
    {
        MethodInfo info = typeof(DonJEnemySpawner).GetMethod(
            method,
            PrivateStatic);
        Assert.IsNotNull(info, "Méthode statique introuvable : " + method);
        return info.Invoke(null, args);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            PrivateInstance);
        Assert.IsNotNull(field, "Champ introuvable : " + name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            PrivateInstance);
        Assert.IsNotNull(field, "Champ introuvable : " + name);
        return (T)field.GetValue(target);
    }

    private static void SetPrivateEnumField(
        object target,
        string name,
        string enumValue)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            PrivateInstance);
        Assert.IsNotNull(field, "Champ enum introuvable : " + name);
        field.SetValue(target, Enum.Parse(field.FieldType, enumValue));
    }
}
