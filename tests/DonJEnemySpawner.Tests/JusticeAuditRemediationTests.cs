using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeAuditRemediationTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic =
        BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void LegacyInventory_LockWithoutSnapshotMigratesToNonDestructiveUnlockedState()
    {
        object script = NewHeadlessScript();
        SetField(script, "_justiceWeaponSnapshot", null);
        SetField(script, "_justiceDeferredInventoryRestore", false);
        SetField(script, "_justiceInventoryRemoved", false);
        SetField(script, "_justiceWeaponControlsLocked", true);

        Invoke(script, "MigrateLegacyJusticeInventoryCustodyState");

        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(
            GetField<bool>(script, "_justiceWeaponControlsLocked"),
            "Un ancien verrou sans snapshot ne doit jamais survivre au chargement.");
        Assert.AreEqual(
            "UnsupportedPreserved",
            GetField(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsTrue((bool)Invoke(script, "ValidateJusticeInventoryCustodyStateInvariant"));
    }

    [TestMethod]
    public void InventoryStateMachine_EnforcesSnapshotAndControlInvariants()
    {
        Type stateType = GetNestedType("JusticeInventoryCustodyState");
        CollectionAssert.AreEqual(
            new[]
            {
                "None",
                "CapturePending",
                "SnapshotPersisted",
                "RemovalPending",
                "RemovedVerified",
                "UnsupportedPreserved",
                "RestorePending",
                "RestoreAmbiguous"
            },
            Enum.GetNames(stateType));

        object script = NewHeadlessScript();
        AssertInventoryInvariant(script, "None", null, false, false, true);
        AssertInventoryInvariant(script, "CapturePending", null, false, false, true);
        AssertInventoryInvariant(script, "CapturePending", null, false, true, false);
        AssertInventoryInvariant(script, "UnsupportedPreserved", null, false, false, true);

        object snapshot = CreateValidatedEmptyWeaponSnapshot();
        AssertInventoryInvariant(script, "SnapshotPersisted", snapshot, false, false, true);
        AssertInventoryInvariant(script, "RemovalPending", snapshot, false, false, true);
        AssertInventoryInvariant(script, "RemovedVerified", snapshot, true, false, true);
        AssertInventoryInvariant(script, "RemovedVerified", null, true, false, false);
        AssertInventoryInvariant(script, "RestorePending", snapshot, false, false, true);
        AssertInventoryInvariant(script, "RestoreAmbiguous", snapshot, false, false, true);
        AssertInventoryInvariant(script, "RestoreAmbiguous", snapshot, false, true, false);
    }

    [TestMethod]
    public void EscapeInventoryRemovalFailure_IsBoundedAndNeverLocksCombatControls()
    {
        object script = NewHeadlessScript();
        SetField(script, "_justiceWeaponSnapshot", CreateValidatedEmptyWeaponSnapshot());

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Assert.IsFalse((bool)Invoke(
                script,
                "RegisterJusticeEscapeInventoryRemovalFailure",
                1000 + attempt));
            Assert.AreEqual(
                "RemovalPending",
                GetField(script, "_justiceInventoryCustodyState").ToString());
            Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
            Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
            Assert.IsTrue((bool)Invoke(script, "ValidateJusticeInventoryCustodyStateInvariant"));
        }

        Assert.IsTrue((bool)Invoke(
            script,
            "RegisterJusticeEscapeInventoryRemovalFailure",
            2000));
        Assert.AreEqual(
            "UnsupportedPreserved",
            GetField(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.AreEqual(0, GetField<int>(script, "_justiceNextInventoryPersistenceRetryAt"));
        Assert.IsTrue((bool)Invoke(script, "ValidateJusticeInventoryCustodyStateInvariant"));
    }

    [TestMethod]
    public void DeferredRuntimeFronts_RejectSameSlotModelMismatchWithoutMerging()
    {
        object script = NewHeadlessScript();
        Type frontsType = GetNestedType("JusticeDeferredRuntimeFront");
        object death = Enum.Parse(frontsType, "DeathStarted");
        object wanted = Enum.Parse(frontsType, "WantedRaised");
        Assert.IsTrue((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            0,
            111,
            death,
            true));

        object firstFronts = GetField(script, "_justiceDeferredRuntimeFronts");
        Assert.IsTrue(HasEnumFlag(firstFronts, "DeathStarted"));
        Assert.AreEqual(0, GetField<int>(script, "_justiceDeferredRuntimeFrontPlayerSlot"));

        // Je refuse ici qu'un modèle contradictoire du même slot adopte le lot.
        // L'appelant gardera ses latches intacts et pourra rééchantillonner.
        Assert.IsFalse((bool)Invoke(
            script,
            "TryStoreJusticeDeferredRuntimeFront",
            0,
            222,
            wanted,
            true));

        object combinedFronts = GetField(script, "_justiceDeferredRuntimeFronts");
        Assert.IsTrue(HasEnumFlag(combinedFronts, "DeathStarted"));
        Assert.IsFalse(
            HasEnumFlag(combinedFronts, "WantedRaised"),
            "Le front du second propriétaire ne doit pas être mélangé au lot initial.");
        Assert.IsTrue(
            HasEnumFlag(combinedFronts, "IdentityChanged"),
            "Une identité ambiguë doit être signalée sans rattacher le front au mauvais profil.");
    }

    [TestMethod]
    public void DeferredRuntimeFronts_DoNotAdvanceLatchesWhenOwnerLotCannotBeStored()
    {
        object script = NewHeadlessScript();
        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
        profiles[0].CaseState.Enabled = true;
        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", 111);
        SetField(script, "_justicePursuitActive", true);
        SetField(script, "_justiceWasDead", false);
        SetField(script, "_justiceWasBeingArrested", false);
        SetField(script, "_justiceLastWantedLevel", 0);

        // Je fournis une preuve policière directe mais aucun modèle propriétaire.
        // Le front ne peut pas être stocké et doit donc rester rééchantillonnable.
        Invoke(
            script,
            "ObserveJusticeFrontsWhilePersistenceBlocked",
            null,
            5,
            true,
            true,
            true);

        Assert.IsFalse(GetField<bool>(script, "_justiceWasDead"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWasBeingArrested"));
        Assert.AreEqual(0, GetField<int>(script, "_justiceLastWantedLevel"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceDeferredRuntimeLatchOwnerInitialized"));
        Assert.IsFalse((bool)Invoke(script, "HasJusticeDeferredRuntimeFronts"));
    }

    [TestMethod]
    public void DeferredRuntimeFronts_DoNotReusePursuitOrArrestLatchesAcrossPToQ()
    {
        Assert.IsTrue(
            JusticePolicy.IsDeferredRuntimeFrontLatchOwnerCompatible(
                0,
                111,
                0,
                111));
        Assert.IsFalse(
            JusticePolicy.IsDeferredRuntimeFrontLatchOwnerCompatible(
                0,
                111,
                1,
                222),
            "Le couple propriétaire de P ne doit jamais qualifier un front de Q.");
        Assert.IsFalse(
            JusticePolicy.IsDeferredArrestFrontAdmissionAllowed(
                true,
                false,
                false,
                true,
                true),
            "Le latch arrested=true de P ne doit pas fabriquer ArrestEnded chez Q.");
        Assert.IsTrue(
            JusticePolicy.IsDeferredArrestFrontAdmissionAllowed(
                true,
                false,
                true,
                true,
                false),
            "L'état natif arrested=true de Q reste une preuve directe propre à Q.");
        Assert.IsFalse(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                true,
                false,
                0,
                5,
                true,
                false),
            "Wanted et pursuit de P ne doivent pas admettre la mort de Q.");
    }

    [TestMethod]
    public void DeferredWantedOnlyFront_NeverFreezesHardeningWithoutAnOwnerSlot()
    {
        object script = NewHeadlessScript();
        Type frontsType = GetNestedType("JusticeDeferredRuntimeFront");
        SetField(
            script,
            "_justiceDeferredRuntimeFronts",
            Enum.Parse(frontsType, "WantedRaised"));
        SetField(script, "_justiceDeferredRuntimeFrontPlayerSlot", -1);
        SetField(script, "_justiceDeferredRuntimeFrontHadPursuit", true);

        Assert.IsTrue((bool)Invoke(
            script,
            "TryHardenJusticeDeferredCriticalFronts"));

        SetField(script, "_justiceDeferredRuntimeFrontPlayerSlot", 1);
        Assert.IsTrue(
            (bool)Invoke(script, "TryHardenJusticeDeferredCriticalFronts"),
            "L'arrivée ultérieure d'un slot valide ne doit pas créer un gel rétroactif.");
    }

    [TestMethod]
    public void AmbiguousPayment_MovesOnlyTheUnprovenAmountToFineInDispute()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            FineDue = 1250L,
            VoluntaryFinePaid = 200L,
            FineInDispute = 100L
        };

        long moved = JusticePolicy.MoveFineToDispute(state, 700L);

        Assert.AreEqual(700L, moved);
        Assert.AreEqual(550L, state.FineDue);
        Assert.AreEqual(800L, state.FineInDispute);
        Assert.AreEqual(200L, state.VoluntaryFinePaid);
        Assert.IsTrue(JusticePolicy.IsFineLedgerValid(state));
        Assert.AreEqual(
            JusticePaymentResolution.Ambiguous,
            (JusticePaymentResolution)Enum.Parse(
                typeof(JusticePaymentResolution),
                "Ambiguous"));
    }

    [TestMethod]
    public void SharedWorldSnapshot_UsesSquaredDistanceAndSixIncidentBudget()
    {
        Vector3 origin = new Vector3(0.0f, 0.0f, 0.0f);

        Assert.IsTrue(JusticeSpatialMath.IsWithinSquaredDistance(
            origin,
            new Vector3(3.0f, 4.0f, 0.0f),
            5.0f));
        Assert.IsFalse(JusticeSpatialMath.IsWithinSquaredDistance(
            origin,
            new Vector3(3.01f, 4.0f, 0.0f),
            5.0f));
        Assert.IsTrue(JusticeSpatialMath.IsWithinSquaredDistance(origin, origin, -10.0f));
        Assert.IsFalse(JusticeSpatialMath.IsWithinSquaredDistance(
            origin,
            new Vector3(0.0f, 0.0f, 0.01f),
            -10.0f));

        FieldInfo budget = ScriptType.GetField(
            "JusticeMaximumConfirmedIncidentsPerTick",
            PrivateStatic);
        Assert.IsNotNull(budget);
        Assert.AreEqual(6, (int)budget.GetRawConstantValue());

        JusticeWorldSnapshot snapshot = new JusticeWorldSnapshot();
        Assert.AreEqual(0, snapshot.PedQueryCount);
        Assert.AreEqual(0, snapshot.VehicleQueryCount);
        Assert.AreEqual(0, snapshot.NearbyPeds.Length);
        Assert.AreEqual(0, snapshot.NearbyVehicles.Length);
    }

    [TestMethod]
    public void PoliceIntegration_DefaultsToFreeroamBestEffortInTheRuntimeConstructor()
    {
        CollectionAssert.AreEqual(
            new[] { "Disabled", "FreeroamBestEffort", "Force" },
            Enum.GetNames(typeof(JusticePoliceIntegrationMode)));

        FieldInfo field = ScriptType.GetField(
            "_justicePoliceIntegrationMode",
            PrivateInstance);
        ConstructorInfo constructor = ScriptType.GetConstructor(Type.EmptyTypes);
        Assert.IsNotNull(field);
        Assert.IsNotNull(constructor);
        Assert.IsTrue(
            ConstructorStoresInt32FieldValue(constructor, field, 1),
            "Le constructeur runtime doit initialiser le mode police à FreeroamBestEffort.");
    }

    [TestMethod]
    public void ShutdownJusticeSystem_ConsumesTheFinalFlushResult()
    {
        MethodInfo shutdown = ScriptType.GetMethod("ShutdownJusticeSystem", PrivateInstance);
        MethodInfo flush = ScriptType.GetMethod("JusticeFlushStateNow", PrivateInstance);
        Assert.IsNotNull(shutdown);
        Assert.IsNotNull(flush);

        Assert.IsTrue(
            MethodCallsAndObservesBooleanResult(shutdown, flush),
            "L'arrêt Justice doit vérifier le booléen du flush final, pas abandonner son résultat.");
    }

    [TestMethod]
    public void PersistenceV2_RoundTripsOneProfilesAuthorityAndRejectsTampering()
    {
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        JusticePersistenceSnapshot source = CreatePersistenceSnapshot(41L);

        byte[] document = codec.Serialize(source);
        JusticePersistenceSnapshot decoded;
        string error;

        Assert.IsTrue(codec.TryDeserialize(document, out decoded, out error), error);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(JusticeXmlPersistenceCodec.SchemaMajor, decoded.SchemaVersion);
        Assert.AreEqual(41L, decoded.Revision);
        Assert.AreEqual(1, decoded.ActiveProfileSlot);
        Assert.AreEqual(3, decoded.Profiles.Count);
        Assert.AreEqual("slot:0", decoded.Profiles[0].IdentityKey);
        Assert.AreEqual("slot:1", decoded.Profiles[1].IdentityKey);
        Assert.AreEqual("slot:2", decoded.Profiles[2].IdentityKey);
        Assert.AreEqual(
            "true",
            JusticeXmlPersistenceCodec.GetFieldValue(
                decoded.Profiles[1].Fields,
                "pendingDeathCapture",
                "false"));

        XmlDocument xml = LoadXml(document);
        XmlElement root = xml.DocumentElement;
        Assert.IsNotNull(root);
        Assert.AreEqual("2", root.GetAttribute("schemaMajor"));
        Assert.AreEqual("0", root.GetAttribute("schemaMinor"));
        Assert.AreEqual(64, root.GetAttribute("recoverySha256").Length);
        Assert.AreEqual(1, root.SelectNodes("Profiles").Count);
        Assert.AreEqual(1, root.SelectNodes("RuntimeRecovery").Count);
        Assert.AreEqual(0, root.SelectNodes("Case|Record|Custody").Count);
        Assert.AreEqual(3, root.SelectNodes("Profiles/Profile").Count);

        byte[] alteredHash = ReplaceUtf8(
            document,
            root.GetAttribute("payloadSha256"),
            new string('0', 64));
        Assert.IsFalse(codec.TryDeserialize(alteredHash, out decoded, out error));
        StringAssert.Contains(error, "SHA-256");

        byte[] alteredProfileGeneration = ReplaceUtf8(
            document,
            "<Profile slot=\"0\" generation=\"8\"",
            "<Profile slot=\"0\" generation=\"9\"");
        Assert.IsFalse(codec.TryDeserialize(
            alteredProfileGeneration,
            out decoded,
            out error));
        StringAssert.Contains(error, "SHA-256");

        byte[] alteredDocumentGeneration = ReplaceUtf8(
            document,
            "generation=\"41\"",
            "generation=\"42\"");
        Assert.IsFalse(
            codec.TryDeserialize(alteredDocumentGeneration, out decoded, out error),
            "La génération globale participe à l'intégrité du document et ne peut pas être falsifiée seule.");
    }

    [TestMethod]
    public void PersistenceV2_NormalizesToTheLegacyReaderWithoutCreatingASecondV2Authority()
    {
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        XmlDocument v2 = LoadXml(codec.Serialize(CreatePersistenceSnapshot(51L)));
        MethodInfo normalize = ScriptType.GetMethod(
            "TryNormalizeJusticeV2DocumentForLegacyReader",
            PrivateStatic);
        Assert.IsNotNull(normalize);

        object[] arguments = { v2, null, null, null };
        Assert.IsTrue((bool)normalize.Invoke(null, arguments), arguments[3] as string);

        XmlElement legacyRoot = arguments[1] as XmlElement;
        JusticePersistenceSnapshot decoded = arguments[2] as JusticePersistenceSnapshot;
        Assert.IsNotNull(legacyRoot);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(51L, decoded.Revision);
        Assert.AreEqual("1", legacyRoot.GetAttribute("version"));
        Assert.AreEqual("1", legacyRoot.GetAttribute("activePlayerSlot"));
        Assert.AreEqual("true", legacyRoot.GetAttribute("enabled"));
        Assert.AreEqual(1, legacyRoot.SelectNodes("Case").Count);
        Assert.AreEqual(1, legacyRoot.SelectNodes("Record").Count);
        Assert.AreEqual(1, legacyRoot.SelectNodes("Custody").Count);
        Assert.AreEqual(3, legacyRoot.SelectNodes("PlayerProfiles/Profile").Count);

        XmlElement v2Root = v2.DocumentElement;
        Assert.IsNotNull(v2Root);
        Assert.AreEqual(1, v2Root.SelectNodes("Profiles").Count);
        Assert.AreEqual(0, v2Root.SelectNodes("Case|Record|Custody|PlayerProfiles").Count);
    }

    [TestMethod]
    public void PersistenceV2_IsolatesOnlyACorruptInactiveProfileFromAValidBackup()
    {
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        byte[] backup = codec.Serialize(CreatePersistenceSnapshot(40L));
        byte[] primary = codec.Serialize(CreatePersistenceSnapshot(41L));

        XmlDocument inactiveCorruption = LoadXml(primary);
        XmlElement inactiveProfile = inactiveCorruption.SelectSingleNode(
            "/JusticeState/Profiles/Profile[@slot='0']") as XmlElement;
        Assert.IsNotNull(inactiveProfile);
        inactiveProfile.SelectSingleNode("Case").Attributes["enabled"].Value = "false";

        JusticePersistenceSnapshot recovered;
        string error;
        Assert.IsTrue(
            codec.TryRecoverInactiveProfiles(
                Encoding.UTF8.GetBytes(inactiveCorruption.OuterXml),
                backup,
                out recovered,
                out error),
            error);
        Assert.IsNotNull(recovered);
        Assert.AreEqual(41L, recovered.Revision);
        Assert.AreEqual(1, recovered.ActiveProfileSlot);
        Assert.AreEqual(
            "<Case enabled=\"true\" />",
            JusticeXmlPersistenceCodec.GetFieldValue(
                recovered.Profiles[0].Fields,
                "Case",
                string.Empty));

        XmlDocument activeCorruption = LoadXml(primary);
        XmlElement activeProfile = activeCorruption.SelectSingleNode(
            "/JusticeState/Profiles/Profile[@slot='1']") as XmlElement;
        Assert.IsNotNull(activeProfile);
        activeProfile.SelectSingleNode("Case").Attributes["enabled"].Value = "false";
        Assert.IsFalse(codec.TryRecoverInactiveProfiles(
            Encoding.UTF8.GetBytes(activeCorruption.OuterXml),
            backup,
            out recovered,
            out error));
        StringAssert.Contains(error, "profil actif");
    }

    [TestMethod]
    public void PersistenceV2_InactiveIsolationRequiresProvenGlobalsGenerationAndActiveProfile()
    {
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        byte[] backup = codec.Serialize(CreatePersistenceSnapshot(40L));
        byte[] primary = codec.Serialize(CreatePersistenceSnapshot(41L));
        JusticePersistenceSnapshot recovered;
        string error;

        XmlDocument changedGlobals = CreateInactiveProfileCorruption(primary);
        XmlElement recovery = changedGlobals.SelectSingleNode(
            "/JusticeState/RuntimeRecovery") as XmlElement;
        Assert.IsNotNull(recovery);
        recovery.SetAttribute("nextIdentityGeneration", "13");
        Assert.IsFalse(codec.TryRecoverInactiveProfiles(
            Encoding.UTF8.GetBytes(changedGlobals.OuterXml),
            backup,
            out recovered,
            out error));
        StringAssert.Contains(error, "enveloppe de récupération");

        XmlDocument changedGeneration = CreateInactiveProfileCorruption(primary);
        changedGeneration.DocumentElement.SetAttribute("generation", "42");
        Assert.IsFalse(codec.TryRecoverInactiveProfiles(
            Encoding.UTF8.GetBytes(changedGeneration.OuterXml),
            backup,
            out recovered,
            out error));
        StringAssert.Contains(error, "enveloppe de récupération");

        XmlDocument missingProof = CreateInactiveProfileCorruption(primary);
        missingProof.DocumentElement.RemoveAttribute("recoverySha256");
        Assert.IsFalse(codec.TryRecoverInactiveProfiles(
            Encoding.UTF8.GetBytes(missingProof.OuterXml),
            backup,
            out recovered,
            out error));
        StringAssert.Contains(error, "enveloppe de récupération");
    }

    [TestMethod]
    public void PersistenceRuntime_V1MigrationBackupIsExactAndRejectsAnExistingMismatch()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeV1Backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "_justice_state.xml");
            string backup = Path.Combine(directory, "_justice_state.v1.bak");
            byte[] original = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><JusticeState version=\"1\" marker=\"exact\" />");
            File.WriteAllBytes(source, original);

            object script = NewHeadlessScript();
            Invoke(script, "PreserveJusticeV1StateBeforeMigration", source, directory);

            CollectionAssert.AreEqual(original, File.ReadAllBytes(backup));
            Assert.AreEqual(
                0,
                Directory.GetFiles(directory, "*.tmp").Length,
                "Le nom final ne doit être publié qu'après la disparition du temporaire atomique.");
            Invoke(script, "PreserveJusticeV1StateBeforeMigration", source, directory);

            byte[] partial = Encoding.UTF8.GetBytes("backup-partiel");
            File.WriteAllBytes(backup, partial);
            TargetInvocationException mismatch = Assert.ThrowsException<TargetInvocationException>(
                delegate
                {
                    Invoke(script, "PreserveJusticeV1StateBeforeMigration", source, directory);
                });
            Assert.IsInstanceOfType(mismatch.InnerException, typeof(InvalidDataException));
            CollectionAssert.AreEqual(
                partial,
                File.ReadAllBytes(backup),
                "Un backup final existant mais non prouvé ne doit jamais être écrasé silencieusement.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void PersistenceRuntime_RecoveryWriteRequiresExactBytesAndShaAfterReadBack()
    {
        byte[] expected = Encoding.UTF8.GetBytes("snapshot-réparé");
        string error;

        Assert.IsTrue(DonJEnemySpawner.TryWriteAndVerifyJusticeRecoveredPrimary(
            "memory-state.xml",
            expected,
            new MemoryRecoveryFileStore(false),
            out error), error);
        Assert.IsFalse(DonJEnemySpawner.TryWriteAndVerifyJusticeRecoveredPrimary(
            "memory-state.xml",
            expected,
            new MemoryRecoveryFileStore(true),
            out error));
        StringAssert.Contains(error, "octets ou le SHA-256");
    }

    [TestMethod]
    public void PersistenceRuntime_ExposesRepositoryWalAndDedicatedV1MigrationBackup()
    {
        FieldInfo repository = ScriptType.GetField("_justiceRepository", PrivateInstance);
        FieldInfo wal = ScriptType.GetField("_justiceWriteAheadLog", PrivateInstance);
        FieldInfo migrationBackup = ScriptType.GetField(
            "JusticeV1MigrationBackupFileName",
            PrivateStatic);

        Assert.IsNotNull(repository);
        Assert.AreEqual(typeof(JusticeRepository), repository.FieldType);
        Assert.IsNotNull(wal);
        Assert.AreEqual(typeof(JusticeWriteAheadLog), wal.FieldType);
        Assert.IsNotNull(migrationBackup);
        Assert.AreEqual("_justice_state.v1.bak", migrationBackup.GetRawConstantValue());
        Assert.AreEqual(2, JusticeXmlPersistenceCodec.SchemaMajor);
        Assert.IsNotNull(ScriptType.GetMethod("QueueJusticeStateCheckpoint", PrivateInstance));
        Assert.IsNotNull(ScriptType.GetMethod("PersistJusticeCriticalPrecommitToWal", PrivateInstance));
    }

    [TestMethod]
    public void DiagnosticMenu_ExposesBuildPackageAndDurabilityStatus()
    {
        CollectionAssert.Contains(
            Enum.GetNames(GetNestedType("MainMenuAction")),
            "JusticeDiagnostic");

        MethodInfo display = ScriptType.GetMethod(
            "GetJusticeDiagnosticMenuDisplay",
            PrivateInstance);
        MethodInfo show = ScriptType.GetMethod("ShowJusticeDiagnosticStatus", PrivateInstance);
        MethodInfo buildId = ScriptType.GetMethod("GetJusticeBuildId", PrivateStatic);
        MethodInfo readManifest = ScriptType.GetMethod(
            "ReadJusticeManifestSha256",
            PrivateStatic);
        Assert.IsNotNull(display);
        Assert.IsNotNull(show);
        Assert.IsNotNull(buildId);
        Assert.IsNotNull(readManifest);

        object script = NewHeadlessScript();
        string menuValue = (string)display.Invoke(script, null);
        string currentBuildId = (string)buildId.Invoke(null, null);
        Assert.IsFalse(string.IsNullOrWhiteSpace(currentBuildId));
        StringAssert.StartsWith(menuValue, currentBuildId);
        StringAssert.Contains(menuValue, "repo indisponible");
        StringAssert.Contains(menuValue, "WAL 0");

        JusticeMetricAccumulator persistence = new JusticeMetricAccumulator();
        JusticeMetricAccumulator detection = new JusticeMetricAccumulator();
        JusticeMetricAccumulator incidents = new JusticeMetricAccumulator();
        persistence.RecordElapsedTicks(11L);
        detection.RecordElapsedTicks(22L);
        incidents.RecordElapsedTicks(33L);
        SetField(script, "_justicePersistenceMetrics", persistence);
        SetField(script, "_justiceCrimeDetectionMetrics", detection);
        SetField(script, "_justiceIncidentProcessingMetrics", incidents);
        FieldInfo pendingIncidents = ScriptType.GetField(
            "_justicePendingIncidents",
            PrivateInstance);
        Assert.IsNotNull(pendingIncidents);
        pendingIncidents.SetValue(script, Activator.CreateInstance(pendingIncidents.FieldType));

        JusticeRepositoryDiagnostics repository = new JusticeRepositoryDiagnostics(
            JusticeRepositoryState.Running,
            7L,
            0L,
            0L,
            6L,
            1L,
            0L,
            string.Empty);
        JusticeWalDiagnostics wal = new JusticeWalDiagnostics(
            5L,
            6L,
            128L,
            2,
            JusticeWalRecoveryStatus.Clean,
            0L,
            string.Empty);
        string report = (string)ScriptType.GetMethod(
            "BuildJusticeDiagnosticReport",
            PrivateInstance).Invoke(script, new object[] { repository, wal });
        StringAssert.Contains(report, "schema=2");
        StringAssert.Contains(report, "WAL ouverts=2");
        StringAssert.Contains(report, "rev mémoire=7");
        StringAssert.Contains(report, "rev disque=6");
        foreach (string domain in new[] { "persistance", "détection", "incidents" })
        {
            StringAssert.Contains(report, domain + " moyenne ms=");
            StringAssert.Contains(report, domain + " p95 ms=");
            StringAssert.Contains(report, domain + " p99 ms=");
            StringAssert.Contains(report, domain + " max ms=");
        }

        string manifestPath = Path.GetTempFileName();
        string binaryHash = new string('a', 64);
        string commit = currentBuildId.Substring(currentBuildId.LastIndexOf('+') + 1);
        try
        {
            File.WriteAllText(
                manifestPath,
                "{\"manifestVersion\":2,\"product\":\"DonJCustomNpcPlacer\"," +
                "\"commit\":\"" + commit + "\",\"sourceDirty\":false," +
                "\"informationalVersion\":\"" + currentBuildId + "\"," +
                "\"justiceSchemaVersion\":2," +
                "\"scriptApi\":{\"major\":2,\"abiContract\":{" +
                "\"id\":\"nib-shvdn-v2.11.6\",\"version\":\"2.11.6\"," +
                "\"sha256\":\"" + new string('d', 64) + "\"}}," +
                "\"decoy\":{\"sha256\":\"" + new string('c', 64) + "\"}," +
                "\"files\":{\"binary\":{\"name\":\"DonJCustomNpcPlacer.ENdll\"," +
                "\"sha256\":\"" + binaryHash + "\"},\"symbols\":{\"sha256\":\"" +
                new string('b', 64) + "\"}}}");
            Assert.AreEqual(
                binaryHash,
                (string)readManifest.Invoke(null, new object[] { manifestPath }));
            File.WriteAllText(
                manifestPath,
                File.ReadAllText(manifestPath).Replace(
                    "\"sourceDirty\":false",
                    "\"sourceDirty\":true"));
            Assert.AreEqual(
                string.Empty,
                (string)readManifest.Invoke(null, new object[] { manifestPath }));
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    private static void AssertInventoryInvariant(
        object script,
        string stateName,
        object snapshot,
        bool removed,
        bool controlsLocked,
        bool expected)
    {
        Type stateType = GetNestedType("JusticeInventoryCustodyState");
        SetField(script, "_justiceInventoryCustodyState", Enum.Parse(stateType, stateName));
        SetField(script, "_justiceWeaponSnapshot", snapshot);
        SetField(script, "_justiceInventoryRemoved", removed);
        SetField(script, "_justiceWeaponControlsLocked", controlsLocked);

        Assert.AreEqual(
            expected,
            (bool)Invoke(script, "ValidateJusticeInventoryCustodyStateInvariant"),
            stateName + " ne respecte pas son invariant attendu.");
    }

    private static object CreateValidatedEmptyWeaponSnapshot()
    {
        Type snapshotType = GetNestedType("JusticeWeaponSnapshot");
        object snapshot = Activator.CreateInstance(snapshotType, true);
        FieldInfo validated = snapshotType.GetField(
            "IsValidated",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(validated);
        validated.SetValue(snapshot, true);
        return snapshot;
    }

    private static JusticePersistenceSnapshot CreatePersistenceSnapshot(long revision)
    {
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>();
        profiles.Add(CreatePersistenceProfile(2, 10L, false));
        profiles.Add(CreatePersistenceProfile(0, 8L, false));
        profiles.Add(CreatePersistenceProfile(1, 9L, true));

        return new JusticePersistenceSnapshot(
            revision,
            JusticeXmlPersistenceCodec.SchemaMajor,
            DateTime.UtcNow.Ticks,
            1,
            new[]
            {
                new JusticePersistenceField("activePlayerSlot", "1"),
                new JusticePersistenceField("nextIdentityGeneration", "12"),
                new JusticePersistenceField("policeIntegrationMode", "1")
            },
            profiles);
    }

    private static JusticePersistenceProfileSnapshot CreatePersistenceProfile(
        int slot,
        long generation,
        bool pendingDeathCapture)
    {
        return new JusticePersistenceProfileSnapshot(
            slot,
            generation,
            "slot:" + slot,
            new[]
            {
                new JusticePersistenceField(
                    "pendingDeathCapture",
                    pendingDeathCapture ? "true" : "false"),
                new JusticePersistenceField("Case", "<Case enabled=\"true\" />"),
                new JusticePersistenceField("Record", "<Record />"),
                new JusticePersistenceField("Custody", "<Custody />")
            });
    }

    private static XmlDocument LoadXml(byte[] document)
    {
        XmlDocument xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml(Encoding.UTF8.GetString(document));
        return xml;
    }

    private static XmlDocument CreateInactiveProfileCorruption(byte[] document)
    {
        XmlDocument corrupted = LoadXml(document);
        XmlElement inactiveProfile = corrupted.SelectSingleNode(
            "/JusticeState/Profiles/Profile[@slot='0']") as XmlElement;
        Assert.IsNotNull(inactiveProfile);
        inactiveProfile.SelectSingleNode("Case").Attributes["enabled"].Value = "false";
        return corrupted;
    }

    private static byte[] ReplaceUtf8(byte[] document, string before, string after)
    {
        string xml = Encoding.UTF8.GetString(document);
        StringAssert.Contains(xml, before);
        return Encoding.UTF8.GetBytes(xml.Replace(before, after));
    }

    private static bool HasEnumFlag(object value, string flagName)
    {
        Assert.IsNotNull(value);
        Type enumType = value.GetType();
        long current = Convert.ToInt64(value);
        long flag = Convert.ToInt64(Enum.Parse(enumType, flagName));
        return (current & flag) == flag;
    }

    private static bool ConstructorStoresInt32FieldValue(
        ConstructorInfo constructor,
        FieldInfo field,
        int expectedValue)
    {
        byte[] il = constructor.GetMethodBody().GetILAsByteArray();
        byte[] token = BitConverter.GetBytes(field.MetadataToken);
        byte expectedOpcode = expectedValue >= 0 && expectedValue <= 8
            ? (byte)(0x16 + expectedValue)
            : (byte)0;

        for (int index = 2; index + 4 < il.Length; index++)
        {
            if (il[index] != 0x7D || !MatchesToken(il, index + 1, token))
            {
                continue;
            }

            if (expectedOpcode != 0 && il[index - 1] == expectedOpcode && il[index - 2] == 0x02)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MethodCallsAndObservesBooleanResult(
        MethodInfo caller,
        MethodInfo callee)
    {
        byte[] il = caller.GetMethodBody().GetILAsByteArray();
        byte[] token = BitConverter.GetBytes(callee.MetadataToken);
        for (int index = 0; index + 5 < il.Length; index++)
        {
            if (il[index] != 0x28 || !MatchesToken(il, index + 1, token))
            {
                continue;
            }

            byte next = il[index + 5];
            // brfalse/brtrue, leurs formes courtes ou un stockage local prouvent
            // que le booléen participe au flot de contrôle au lieu d'être jeté.
            return next == 0x2C || next == 0x2D || next == 0x39 || next == 0x3A ||
                   (next >= 0x0A && next <= 0x0D) || next == 0x13;
        }

        return false;
    }

    private static bool MatchesToken(byte[] il, int offset, byte[] token)
    {
        if (offset < 0 || offset + token.Length > il.Length)
        {
            return false;
        }

        for (int index = 0; index < token.Length; index++)
        {
            if (il[offset + index] != token[index])
            {
                return false;
            }
        }

        return true;
    }

    private static object NewHeadlessScript()
    {
        return FormatterServices.GetUninitializedObject(ScriptType);
    }

    private static Type GetNestedType(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type privé introuvable: " + name);
        return type;
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Méthode privée introuvable: " + methodName);
        return method.Invoke(target, arguments);
    }

    private static object GetField(object target, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable: " + fieldName);
        return field.GetValue(target);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)GetField(target, fieldName);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable: " + fieldName);
        field.SetValue(target, value);
    }

    private sealed class MemoryRecoveryFileStore : IJusticeAtomicFileStore
    {
        private readonly bool _corruptReadBack;
        private byte[] _document;

        internal MemoryRecoveryFileStore(bool corruptReadBack)
        {
            _corruptReadBack = corruptReadBack;
        }

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            _document = (byte[])document.Clone();
        }

        public byte[] ReadAllBytes(string path)
        {
            byte[] result = (byte[])_document.Clone();
            if (_corruptReadBack)
            {
                result[result.Length - 1] ^= 0x01;
            }
            return result;
        }
    }
}
