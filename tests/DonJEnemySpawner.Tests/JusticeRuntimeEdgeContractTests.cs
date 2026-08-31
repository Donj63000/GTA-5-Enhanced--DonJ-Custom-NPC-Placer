using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeRuntimeEdgeContractTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public void DamageFronts_AreBoundedConsumedOnceAndFlushedAfterGameplayConsumers()
    {
        string justiceSource = ReadSource("DonJEnemySpawner.Justice.cs");
        string coreSource = ReadSource("DonJEnemySpawner.cs");
        string schedule = ExtractMethodBody(justiceSource, "ScheduleJusticeDamageFrontConsumption");
        string flush = ExtractMethodBody(justiceSource, "FlushJusticeConsumedDamageFronts");

        StringAssert.Contains(schedule, "entity.Handle");
        Assert.IsTrue(
            Regex.IsMatch(schedule, @"(?:\.Count|_justiceDamageFrontCount)\s*>=\s*JusticeMaximum\w+"),
            "Je borne explicitement la file des fronts de dégâts consommés.");
        Assert.IsTrue(
            schedule.IndexOf("Contains", StringComparison.Ordinal) >= 0 ||
            schedule.IndexOf("TryGetValue", StringComparison.Ordinal) >= 0 ||
            schedule.IndexOf("for (", StringComparison.Ordinal) >= 0,
            "Je déduplique une entité avant de programmer son reset de dégâts.");

        StringAssert.Contains(flush, "CLEAR_ENTITY_LAST_DAMAGE_ENTITY");
        StringAssert.Contains(
            flush,
            "_justiceDamageFrontCount = 0",
            "Je réarme le curseur du pool sans réallouer sa liste après le flush.");

        foreach (string methodName in new[]
                 {
                     "SynchronizeJusticeDamageFronts",
                     "ScanJusticeEventVictims",
                     "ScanJusticeDamagedVehicles",
                     "ProcessJusticeAllyAttributionTokens"
                 })
        {
            string detector = ExtractMethodBody(justiceSource, methodName);
            Assert.IsTrue(
                detector.IndexOf("TryCaptureJusticeDamageFront", StringComparison.Ordinal) >= 0 ||
                detector.IndexOf("ScheduleJusticeDamageFrontConsumption", StringComparison.Ordinal) >= 0 ||
                detector.IndexOf("SynchronizeJusticeDamagePair", StringComparison.Ordinal) >= 0,
                methodName + " doit consommer le flag GTA qu'il vient de photographier.");
        }

        string onTick = ExtractMethodBody(coreSource, "OnTick");
        AssertOrdered(
            onTick,
            "UpdateJusticeSystem()",
            "UpdateNpcs()",
            "UpdateCartelConvoyLate()",
            "FlushJusticeConsumedDamageFronts()");
    }

    [TestMethod]
    public void HomicideFallback_AcceptsOnlyAFreshCausalTokenUnderSixSeconds()
    {
        object script = CreateHeadlessScript();
        SetFieldValue(script, "_justiceMonotonicTimeMs", 10000L);

        Assert.IsTrue((bool)InvokeInstance(script, "IsJusticeCausalDamageFresh", 10000L));
        Assert.IsTrue((bool)InvokeInstance(script, "IsJusticeCausalDamageFresh", 4001L));
        Assert.IsFalse((bool)InvokeInstance(script, "IsJusticeCausalDamageFresh", 3999L));
        Assert.IsFalse((bool)InvokeInstance(script, "IsJusticeCausalDamageFresh", 0L));
        Assert.IsFalse((bool)InvokeInstance(script, "IsJusticeCausalDamageFresh", 10001L));
        Assert.AreEqual(6000, JusticePolicy.PendingIncidentLifetimeMs);

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string attribution = ExtractMethodBody(source, "IsJusticeDeathAttributedTo");
        StringAssert.Contains(attribution, "IsJusticeCausalDamageFresh(causalDamageAtMs)");
        Assert.IsFalse(
            attribution.IndexOf("HasJusticeEntityBeenDamagedBy", StringComparison.Ordinal) >= 0,
            "Un tueur indisponible ne doit jamais réutiliser un historique GTA non daté.");

        string playerUpgrade = ExtractMethodBody(source, "ProcessJusticeRecentVictimUpgrades");
        StringAssert.Contains(playerUpgrade, "recent.LastPlayerAttackAtMs");

        string alliedUpgrade = ExtractMethodBody(source, "ProcessJusticeAllyAttributionTokens");
        AssertOrdered(
            alliedUpgrade,
            "token.LastObservedDamageAtMs = _justiceMonotonicTimeMs",
            "IsJusticeDeathAttributedTo(",
            "token.LastObservedDamageAtMs",
            "token.AllyGeneration");
    }

    [TestMethod]
    public void CrimeScan_StaysOpenDuringSustainedMeleeWhenTheNativeHitTimerFails()
    {
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldKeepJusticeCrimeScanOpen",
            false, false,
            true, true, true,
            false, false, false));
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldKeepJusticeCrimeScanOpen",
            false, false,
            false, true, true,
            false, false, false));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldKeepJusticeCrimeScanOpen",
            false, false,
            false, true, false,
            false, false, false));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldKeepJusticeCrimeScanOpen",
            false, false,
            false, false, false,
            true, false, false));
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldKeepJusticeCrimeScanOpen",
            false, false,
            false, false, false,
            false, false, false));
    }

    [TestMethod]
    public void CivilianSelfDefense_RemainsValidWithAWarrantAndNeverReadsHistoricalDamageDirectly()
    {
        JusticeIncident incident = new JusticeIncident
        {
            Kind = JusticeCrimeKind.SimpleAssault,
            Circumstances = JusticeCircumstances.ActiveWarrant |
                            JusticeCircumstances.ProportionalSelfDefense
        };

        JusticeSanction sanction = JusticePolicy.Evaluate(incident, new JusticeRecordState());
        Assert.IsFalse(
            sanction.IsChargeable,
            "Un mandat sans rapport avec l'agression civile ne supprime pas la légitime défense.");

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string circumstances = ExtractMethodBody(source, "BuildJusticeAssaultCircumstances");
        Assert.IsFalse(
            circumstances.IndexOf("_justiceCaseState.HasWarrant", StringComparison.Ordinal) >= 0,
            "Le domaine exclut déjà policiers et résistance; le runtime ne doit pas interdire toute défense sous mandat.");

        string aggressor = ExtractMethodBody(source, "RememberJusticePotentialAggressor");
        Assert.IsFalse(
            aggressor.IndexOf("HasJusticeEntityBeenDamagedBy", StringComparison.Ordinal) >= 0,
            "La fenêtre de défense doit venir d'un front entrant daté, pas d'un flag GTA historique.");
        Assert.IsTrue(
            aggressor.IndexOf("IsJusticeCausalDamageFresh", StringComparison.Ordinal) >= 0 ||
            Regex.IsMatch(aggressor, @"\b\w*Justice\w*(?:Fresh|Recent|Causal)\w*\s*\("),
            "Je valide explicitement la fraîcheur du front entrant avant de réarmer les huit secondes.");

        string victimScan = ExtractMethodBody(source, "ScanJusticeEventVictims");
        AssertOrdered(
            victimScan,
            "TryCaptureJusticeDamageFront(player, victim, playerVitalityDropped)",
            "RememberJusticePotentialAggressor(",
            "_justiceMonotonicTimeMs");
    }

    [TestMethod]
    public void VehicleImpact_StartsAsAggravatedAssaultAndHitAndRunNeedsDelayAndDistance()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string initialClassification = ExtractMethodBody(source, "ClassifyJusticeAssault");
        Assert.IsFalse(
            initialClassification.IndexOf("JusticeCrimeKind.HitAndRun", StringComparison.Ordinal) >= 0,
            "Le choc initial ne constitue pas encore un délit de fuite.");
        Assert.IsTrue(
            Regex.IsMatch(
                initialClassification,
                @"if\s*\(vehicleWasWeapon\)[\s\S]*?return\s+JusticeCrimeKind\.AggravatedAssault"),
            "Un véhicule utilisé comme arme commence par une agression aggravée.");

        string upgrade = ExtractMethodBody(source, "UpdateJusticeHitAndRunUpgrades");
        StringAssert.Contains(upgrade, "JusticeCrimeKind.HitAndRun");
        StringAssert.Contains(upgrade, "DistanceTo");
        StringAssert.Contains(upgrade, "_justiceMonotonicTimeMs");
        Assert.IsTrue(
            Regex.IsMatch(upgrade, @"JusticeHitAndRun\w*(?:Delay|Distance)"),
            "La fuite exige des seuils temporel et spatial explicites.");
        Assert.IsTrue(
            Regex.IsMatch(upgrade, @"\w*HitAndRun\w*Queued"),
            "Une victime ne doit produire qu'une seule charge de délit de fuite par épisode.");

        StringAssert.Contains(
            ExtractMethodBody(source, "ProcessJusticeRecentVictimUpgrades"),
            "UpdateJusticeHitAndRunUpgrades");
    }

    [TestMethod]
    public void WitnessCandidates_ReuseOneSharedWorldSnapshotAndStayBoundedPerActor()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string worldSnapshot = ReadSource("DonJEnemySpawner.Justice.WorldSnapshot.cs");
        string worldCapture = ExtractMethodBody(worldSnapshot, "CaptureJusticeWorldSnapshot");
        Assert.AreEqual(
            1,
            CountOccurrences(worldCapture, "GetNearbyPedsSafe"),
            "Une passe Justice doit effectuer une seule requête peds GTA.");
        Assert.AreEqual(
            1,
            CountOccurrences(worldCapture, "GetNearbyVehiclesSafe"),
            "Une passe Justice doit effectuer une seule requête véhicules GTA.");

        string capture = ExtractMethodBody(source, "GetJusticeWitnessCandidatesForActor");
        Assert.AreEqual(
            0,
            CountOccurrences(capture, "GetNearbyPedsSafe"),
            "Un acteur doit filtrer le snapshot partagé sans relancer de requête GTA.");
        StringAssert.Contains(capture, "GetJusticeSnapshotPeds()");
        StringAssert.Contains(capture, "IsJusticeSnapshotEntityWithin");
        StringAssert.Contains(capture, "JusticeMaximumWitnessesPerEvent");

        string playerPass = ExtractMethodBody(source, "ScanJusticeEventVictims");
        Assert.AreEqual(1, CountOccurrences(playerPass, "GetJusticeWitnessCandidatesForActor"));
        Assert.IsTrue(
            playerPass.IndexOf("GetJusticeWitnessCandidatesForActor", StringComparison.Ordinal) <
            playerPass.IndexOf("for (", StringComparison.Ordinal),
            "Je photographie les témoins une fois avant de parcourir les victimes du même acte.");

        string evidence = ExtractMethodBody(source, "BuildJusticeEvidence");
        Assert.IsFalse(
            evidence.IndexOf("GetNearbyPedsSafe", StringComparison.Ordinal) >= 0,
            "Chaque victime doit réutiliser la photographie de témoins déjà bornée.");
        Assert.IsFalse(
            evidence.IndexOf("GetNearbyVehiclesSafe", StringComparison.Ordinal) >= 0,
            "La qualification des preuves ne doit jamais déclencher un second scan véhicules.");
        StringAssert.Contains(evidence, "witnessCandidates");
    }

    [TestMethod]
    public void PendingQueue_WhenFullEvictsLowerPriorityProvisionalEvidenceButProtectsAuthoritativeCases()
    {
        object script = CreateHeadlessScript();
        IList pending = GetFieldValue<IList>(script, "_justicePendingIncidents");
        int capacity = GetStaticFieldValue<int>("JusticeMaximumPendingIncidents");

        for (int index = 0; index < capacity; index++)
        {
            pending.Add(CreatePending(JusticeCrimeKind.RecklessDischarge, index + 1L, false));
        }

        int eviction = (int)InvokeInstance(
            script,
            "FindJusticePendingEvictionIndex",
            JusticeCrimeKind.MurderOfficer,
            false);
        Assert.IsTrue(eviction >= 0 && eviction < capacity);
        Assert.AreEqual(
            JusticeCrimeKind.RecklessDischarge,
            ((JusticeIncident)GetMemberValue(pending[eviction], "Incident")).Kind);

        pending.Clear();
        for (int index = 0; index < capacity; index++)
        {
            pending.Add(CreatePending(JusticeCrimeKind.MurderOfficer, index + 1L, true));
        }
        Assert.AreEqual(
            -1,
            (int)InvokeInstance(
                script,
                "FindJusticePendingEvictionIndex",
                JusticeCrimeKind.RecklessDischarge,
                false),
            "Une preuve mineure ne doit jamais chasser un homicide déjà confirmé par une source autoritaire.");

        pending[7] = CreatePending(JusticeCrimeKind.RecklessDischarge, 1L, false);
        Assert.AreEqual(
            7,
            (int)InvokeInstance(
                script,
                "FindJusticePendingEvictionIndex",
                JusticeCrimeKind.VehicleTheft,
                true),
            "Un signal GTA explicite remplace en priorité le plus faible provisoire.");

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        StringAssert.Contains(ExtractMethodBody(source, "QueueJusticeIncident"), "EnsureJusticePendingCapacity");
        StringAssert.Contains(ExtractMethodBody(source, "QueueJusticeDirectGameReport"), "EnsureJusticePendingCapacity");
        StringAssert.Contains(ExtractMethodBody(source, "EnsureJusticePendingCapacity"), "FindJusticePendingEvictionIndex");
    }

    [TestMethod]
    public void TrackedIdentity_ExpiresBeforeSameModelHandleCanReuseItsGeneration()
    {
        object script = CreateHeadlessScript();
        object tracked = Activator.CreateInstance(GetNestedType("JusticeTrackedIdentity"), true);
        SetMemberValue(tracked, "ModelHash", 8123);
        SetMemberValue(tracked, "LastSeenAtMs", 1000L);
        SetFieldValue(
            script,
            "_justiceMonotonicTimeMs",
            1001L + GetStaticFieldValue<int>("JusticeIdentityLifetimeMs"));

        Assert.IsFalse((bool)InvokeInstance(
            script,
            "CanReuseJusticeTrackedIdentity",
            tracked,
            null,
            8123,
            0L));

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string reuse = ExtractMethodBody(source, "CanReuseJusticeTrackedIdentity");
        StringAssert.Contains(reuse, "JusticeIdentityLifetimeMs");
        StringAssert.Contains(reuse, "tracked.LastSeenAtMs");
        StringAssert.Contains(reuse, "tracked.MemoryAddress");
        StringAssert.Contains(reuse, "memoryAddress");
        StringAssert.Contains(reuse, "ReferenceEquals(tracked.Entity, currentEntity)");
        StringAssert.Contains(ExtractMethodBody(source, "GetJusticeEntityGeneration"), "CanReuseJusticeTrackedIdentity");
    }

    [TestMethod]
    public void RuntimeSuspension_UsesOnlyScalarFrontsAndReprimesBeforeWorldDetection()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string update = ExtractMethodBody(source, "UpdateJusticeSystem");

        AssertOrdered(
            update,
            "bool runtimeSuspended = IsJusticeRuntimeSuspended(player)",
            "AdvanceJusticeInactiveCustodyProfiles(",
            "bool suspended = runtimeSuspended",
            "bool custodyActive = JusticeIsCustodyActive",
            "if (_justiceEnabled && (suspended || custodyActive))",
            "SynchronizeJusticeScalarFronts(player, false)",
            "_justiceDamageFrontPrimingPending = true",
            "else if (_justiceEnabled && _justiceDamageFrontPrimingPending)",
            "PrimeJusticeEventFronts(player)",
            "DetectJusticeEventFronts(player)");

        int scalarGate = update.IndexOf(
            "if (_justiceEnabled && (suspended || custodyActive))",
            StringComparison.Ordinal);
        int resumeGate = update.IndexOf(
            "else if (_justiceEnabled && _justiceDamageFrontPrimingPending)",
            scalarGate,
            StringComparison.Ordinal);
        string suspendedBranch = update.Substring(scalarGate, resumeGate - scalarGate);
        Assert.IsFalse(
            suspendedBranch.IndexOf("GetNearby", StringComparison.Ordinal) >= 0 ||
            suspendedBranch.IndexOf("World.", StringComparison.Ordinal) >= 0 ||
            suspendedBranch.IndexOf("CLEAR_ENTITY_LAST_DAMAGE_ENTITY", StringComparison.Ordinal) >= 0,
            "Le gate suspendu ne doit effectuer ni scan du monde ni purge de dégâts.");
    }

    [TestMethod]
    public void VictimDetection_KeepsDeadCandidatesButRequiresLiveWitnesses()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string snapshot = ExtractMethodBody(source, "GetJusticeWitnessCandidatesForActor");
        string victimScan = ExtractMethodBody(source, "ScanJusticeEventVictims");
        string evidence = ExtractMethodBody(source, "BuildJusticeEvidence");

        StringAssert.Contains(snapshot, "IsJusticePotentialVictimCandidate(candidate, actor)");
        StringAssert.Contains(snapshot, "for (int pass = 0; pass < 3");
        StringAssert.Contains(snapshot, "dead && victimCount < JusticeMaximumVictimCandidatesPerEvent");
        AssertOrdered(
            victimScan,
            "IsJusticePotentialVictimCandidate(victim, player)",
            "victim.IsDead",
            "bool attributedDeath = IsJusticeDeathAttributedTo(",
            "causalDamage ? _justiceMonotonicTimeMs : -1L",
            "(!freshDeath && !causalDamage)",
            "QueueJusticeIncident(");
        StringAssert.Contains(evidence, "!victimPed.IsDead");
        StringAssert.Contains(evidence, "IsJusticeHumanCandidate(witness, actor)");
    }

    [TestMethod]
    public void EntityIdentityAndRecognition_IncludeMemoryAddressAndGeneration()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string generation = ExtractMethodBody(source, "GetJusticeEntityGeneration");
        string memory = ExtractMethodBody(source, "GetJusticeEntityMemoryAddressSafe");
        string recognition = ExtractMethodBody(source, "UpdateJusticeWarrantRecognition");

        AssertOrdered(
            generation,
            "GetJusticeEntityMemoryAddressSafe(entity)",
            "CanReuseJusticeTrackedIdentity(tracked, entity, modelHash, memoryAddress)",
            "MemoryAddress = memoryAddress");
        StringAssert.Contains(memory, "entity.MemoryAddress");
        AssertOrdered(
            recognition,
            "int recognizerGeneration = GetJusticeEntityGeneration(recognizer)",
            "_justiceRecognitionCandidateGeneration != recognizerGeneration",
            "_justiceRecognitionCandidateGeneration = recognizerGeneration",
            "_justiceRecognitionStartedAtMs = _justiceMonotonicTimeMs");
    }

    [TestMethod]
    public void ArmedThreatTimer_RequiresTheSameHandleAndEntityGeneration()
    {
        Assert.IsTrue((bool)InvokeStatic("IsSameJusticeTrackedTarget", 42, 7, 42, 7));
        Assert.IsFalse((bool)InvokeStatic("IsSameJusticeTrackedTarget", 42, 7, 42, 8));
        Assert.IsFalse((bool)InvokeStatic("IsSameJusticeTrackedTarget", 42, 0, 42, 0));

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string armedThreat = ExtractMethodBody(source, "UpdateJusticeArmedThreatFront");
        AssertOrdered(
            armedThreat,
            "GetJusticeEntityGeneration(target)",
            "IsSameJusticeTrackedTarget(",
            "_justiceAimTargetGeneration = targetGeneration",
            "_justiceMonotonicTimeMs - _justiceAimStartedAtMs");
        StringAssert.Contains(
            ExtractMethodBody(source, "SynchronizeJusticeScalarFrontsCore"),
            "_justiceAimTargetGeneration = 0");
    }

    [TestMethod]
    public void VehicleDamage_AttributesCurrentOrLastPlayerVehicleAndPreservesCircumstances()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string scan = ExtractMethodBody(source, "ScanJusticeDamagedVehicles");

        AssertOrdered(
            scan,
            "GetJusticeCurrentVehicleSafe(player)",
            "GetJusticeLastVehicleSafe(player)",
            "TryCaptureJusticeDamageFront(",
            "currentVehicle",
            "lastVehicle",
            "GetJusticeWeaponCircumstances(player)",
            "JusticeCircumstances.VehicleUsedAsWeapon",
            "RememberJusticeRecentVehicle(vehicle, generation, circumstances)");
    }

    [TestMethod]
    public void CustodyWorldKeyHandler_IsRemovedWithActivities()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.Custody.cs");
        Assert.IsFalse(source.Contains("JusticeHandleCustodyWorldKey"));
    }

    [TestMethod]
    public void CustodyActivityScenarioRuntime_IsCompletelyAbsent()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.Custody.cs");
        Assert.IsFalse(source.Contains("UpdateJusticeCustodyActivity"));
        Assert.IsFalse(source.Contains("AdvanceJusticeActivityClock"));
        Assert.IsFalse(source.Contains("JusticeNativeTaskStartScenarioInPlace"));
        Assert.IsFalse(source.Contains("World.DrawMarker("));
    }

    [TestMethod]
    public void CustodyTransientState_RetriesEachSetterWithoutDisciplineInvincibility()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.Custody.cs");
        string restore = ExtractMethodBody(source, "RestoreJusticeCustodyPlayerTransientState");
        string amnesty = ExtractMethodBody(source, "JusticeAmnestyCustody");

        AssertOrdered(
            restore,
            "player.IsInvincible = _justiceCustodyStoredInvincible",
            "player.FreezePosition = _justiceCustodyStoredFrozen",
            "player.CanRagdoll = _justiceCustodyStoredCanRagdoll",
            "return restored;");
        Assert.AreEqual(
            3,
            Regex.Matches(restore, @"catch\s*\{").Count,
            "Chaque propriété temporaire doit conserver son propre chemin de retry.");
        AssertOrdered(
            amnesty,
            "RestoreJusticeCustodyPlayerTransientState(player)",
            "_justiceCustodyPlayerStateStored = false");
        Assert.IsFalse(source.Contains("TryRestoreJusticeDisciplineInvincibility"));
        Assert.IsFalse(source.Contains("PlayerInvincibilityOwner.JusticeDiscipline"));
    }

    [TestMethod]
    public void ActivationPauseAndReload_UseDedicatedRuntimeFrontHelpersBeforeDetection()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string initialize = ExtractMethodBody(source, "InitializeJusticeSystem");
        string toggle = ExtractMethodBody(source, "RequestJusticeToggle");
        string resume = ExtractMethodBody(source, "PrepareJusticeRuntimeAfterResume");
        string pause = ExtractMethodBody(source, "PauseJusticeRuntimeWithoutErasingCase");
        string update = ExtractMethodBody(source, "UpdateJusticeSystem");
        string early = ExtractMethodBody(source, "UpdateJusticeEarly");
        string prime = ExtractMethodBody(source, "PrimeJusticeEventFronts");
        string read = ExtractMethodBody(source, "TryReadJusticeStateFile");

        AssertOrdered(
            initialize,
            "TryLoadJusticeState(false)",
            "NormalizeLoadedJusticeState()",
            "MigrateLegacyJusticeAmnestyState()",
            "InitializeJusticePersistenceServices()",
            "_justiceLastWantedLevel = GetJusticeWantedLevelSafe()",
            "_justiceDamageFrontPrimingPending = _justiceEnabled");
        AssertOrdered(
            toggle,
            "IsJusticePlayedProfileContextReady()",
            "HasOpenJusticeProfileResetWal()",
            "MigrateLegacyJusticeAmnestyState()",
            "bool targetEnabled = !_justiceEnabled",
            "_justiceEnabled = targetEnabled",
            "if (targetEnabled)",
            "PrepareJusticeRuntimeAfterResume()",
            "PauseJusticeRuntimeWithoutErasingCase()",
            "JusticeMarkStateDirty()",
            "JusticeFlushStateNow()");
        Assert.IsFalse(
            toggle.IndexOf("SynchronizeJusticeEventFronts", StringComparison.Ordinal) >= 0,
            "L'activation doit laisser le tick synchroniser et purger dans une même opération atomique.");
        Assert.IsFalse(toggle.Contains("RequestDangerConfirmation"));
        Assert.IsFalse(toggle.Contains("ResumeJusticeAmnestyTransaction"));

        AssertOrdered(
            resume,
            "CancelJusticeWantedClearRetry()",
            "CancelJusticeAmnestyConfirmation()",
            "_justiceDamagePairBaselineCount = 0",
            "_justiceDamageFrontPrimingPending = true",
            "GetJusticeWantedLevelSafe()",
            "ReconcileLoadedJusticePursuitState(wantedLevel)");
        AssertOrdered(
            pause,
            "_justiceDamageFrontPrimingPending = false",
            "_justiceDamagePairBaselineCount = 0",
            "_justiceWantedLossPending = false",
            "_justicePursuitActive = false",
            "_justicePendingIncidents.Clear()",
            "ResetJusticeWitnessSnapshots()",
            "CancelJusticeWantedClearRetry()",
            "CancelJusticeAmnestyConfirmation()");
        Assert.IsFalse(pause.Contains("ClearActiveCase"));
        Assert.IsFalse(pause.Contains("ClearJusticeWantedLevel"));

        StringAssert.Contains(early, "if (_justiceAmnestyPending)");
        StringAssert.Contains(early, "MigrateLegacyJusticeAmnestyState()");
        Assert.IsFalse(early.Contains("ResumeJusticeAmnestyTransaction()"));
        Assert.IsFalse(early.Contains("RetryJusticeWantedClearAfterAmnesty()"));

        StringAssert.Contains(update, "_justiceEnabled && _justiceDamageFrontPrimingPending");
        StringAssert.Contains(update, "_justiceEnabled && !_justiceDamageFrontPrimingPending");
        AssertOrdered(update, "PrimeJusticeEventFronts(player)", "DetectJusticeEventFronts(player)");
        AssertOrdered(
            prime,
            "SynchronizeJusticeEventFronts(player, true)",
            "FlushJusticeConsumedDamageFronts()",
            "_justiceDamageFrontPrimingPending = false");
        StringAssert.Contains(read, "_justiceDamageFrontPrimingPending = _justiceEnabled");
    }

    [TestMethod]
    public void LoadedPursuitWithoutWanted_ReconcilesWantedAndSurrenderingWithoutCreatingACharge()
    {
        object wantedPursuit = CreateHeadlessScript();
        JusticeCaseState wantedState = GetFieldValue<JusticeCaseState>(wantedPursuit, "_justiceCaseState");
        wantedState.Enabled = true;
        wantedState.ActiveScore = 48;
        wantedState.Phase = JusticePhase.Wanted;
        SetFieldValue(wantedPursuit, "_justiceEnabled", true);
        SetFieldValue(wantedPursuit, "_justicePursuitActive", true);

        InvokeInstance(wantedPursuit, "ReconcileLoadedJusticePursuitState", 0);

        Assert.IsTrue(wantedState.HasWarrant, "Wanted sans étoile doit devenir un mandat.");
        Assert.AreEqual(JusticePhase.AtLarge, wantedState.Phase);
        Assert.AreEqual(0, wantedState.Charges.Count, "Le reload ne doit inventer aucune infraction.");
        Assert.IsFalse(GetFieldValue<bool>(wantedPursuit, "_justicePursuitActive"));
        Assert.IsTrue(GetFieldValue<bool>(wantedPursuit, "_justiceStateDirty"));

        object surrenderingPursuit = CreateHeadlessScript();
        JusticeCaseState surrenderingState = GetFieldValue<JusticeCaseState>(surrenderingPursuit, "_justiceCaseState");
        surrenderingState.Enabled = true;
        surrenderingState.ActiveScore = 48;
        surrenderingState.Phase = JusticePhase.Surrendering;
        SetFieldValue(surrenderingPursuit, "_justiceEnabled", true);
        SetFieldValue(surrenderingPursuit, "_justicePursuitActive", true);

        InvokeInstance(surrenderingPursuit, "ReconcileLoadedJusticePursuitState", 0);

        // Je conserve le front durable Surrendering après reload : la sonde
        // BUSTED décidera ensuite capture ou mandat sans inventer de charge.
        Assert.IsFalse(surrenderingState.HasWarrant);
        Assert.AreEqual(JusticePhase.Surrendering, surrenderingState.Phase);
        Assert.IsTrue(GetFieldValue<bool>(surrenderingPursuit, "_justiceArrestCompletionProbePending"));
        Assert.IsTrue(GetFieldValue<bool>(surrenderingPursuit, "_justiceWantedLossPending"));
        Assert.AreEqual(0, surrenderingState.Charges.Count, "La reprise de sonde ne doit inventer aucune infraction.");
        Assert.IsTrue(GetFieldValue<bool>(surrenderingPursuit, "_justicePursuitActive"));
        Assert.IsFalse(GetFieldValue<bool>(surrenderingPursuit, "_justiceStateDirty"));

        object activePursuit = CreateHeadlessScript();
        JusticeCaseState activeState = GetFieldValue<JusticeCaseState>(activePursuit, "_justiceCaseState");
        activeState.Enabled = true;
        activeState.ActiveScore = 48;
        activeState.Phase = JusticePhase.Wanted;
        SetFieldValue(activePursuit, "_justiceEnabled", true);
        SetFieldValue(activePursuit, "_justicePursuitActive", true);

        InvokeInstance(activePursuit, "ReconcileLoadedJusticePursuitState", 2);

        Assert.IsFalse(activeState.HasWarrant, "Une poursuite encore portée par GTA reste active.");
        Assert.AreEqual(JusticePhase.Wanted, activeState.Phase);
        Assert.IsTrue(GetFieldValue<bool>(activePursuit, "_justicePursuitActive"));
        Assert.IsFalse(GetFieldValue<bool>(activePursuit, "_justiceStateDirty"));
    }

    [TestMethod]
    public void WantedBeforeFirstConfirmedCase_StartsTheEvadingClockOnlyWhenTheCaseExists()
    {
        object script = CreateHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        IList pending = GetFieldValue<IList>(script, "_justicePendingIncidents");

        SetFieldValue(script, "_justiceMonotonicTimeMs", 100000L);
        SetFieldValue(script, "_justiceLastWantedLevel", 0);
        InvokeInstance(script, "UpdateJusticeWantedEdges", 2);

        Assert.IsFalse(GetFieldValue<bool>(script, "_justicePursuitActive"));
        Assert.AreEqual(0L, GetFieldValue<long>(script, "_justiceWantedEpisodeStartedAtMs"));

        // Je simule ici la confirmation différée du premier incident pendant que
        // les étoiles GTA sont déjà stables.
        state.ActiveScore = 10;
        SetFieldValue(script, "_justiceLastWantedLevel", 2);
        SetFieldValue(script, "_justiceMonotonicTimeMs", 100050L);
        InvokeInstance(script, "UpdateJusticeWantedEdges", 2);

        Assert.IsTrue(GetFieldValue<bool>(script, "_justicePursuitActive"));
        Assert.AreEqual(100050L, GetFieldValue<long>(script, "_justiceWantedEpisodeStartedAtMs"));
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);

        SetFieldValue(script, "_justiceMonotonicTimeMs", 112049L);
        InvokeInstance(script, "UpdateJusticeEvadingPoliceCharge", (object)null);
        Assert.AreEqual(0, pending.Count, "Aucune fuite ne doit être créée avant douze secondes pleines.");

        SetFieldValue(script, "_justiceMonotonicTimeMs", 112050L);
        InvokeInstance(script, "UpdateJusticeEvadingPoliceCharge", (object)null);
        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(
            JusticeCrimeKind.EvadingPolice,
            ((JusticeIncident)GetMemberValue(pending[0], "Incident")).Kind);
    }

    [TestMethod]
    public void Persistence_CanonicalCorruptionNeverFallsThroughToAnUnversionedLegacyState()
    {
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldContinueJusticeStateSearch",
            0, true, false));
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldContinueJusticeStateSearch",
            0, false, true));
        Assert.IsFalse((bool)InvokeStatic(
            "ShouldContinueJusticeStateSearch",
            0, true, true));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldContinueJusticeStateSearch",
            0, false, false));
        Assert.IsTrue((bool)InvokeStatic(
            "ShouldContinueJusticeStateSearch",
            1, true, true));

        string source = ReadSource("DonJEnemySpawner.Justice.cs");
        string loader = ExtractMethodBody(source, "TryLoadJusticeState");
        AssertOrdered(
            loader,
            "TryReadJusticeStateFile(primary)",
            "TryReadJusticeStateFile(backup)",
            "ShouldContinueJusticeStateSearch(index, primaryExists, backupExists)",
            "return false;");
    }

    [TestMethod]
    public void Persistence_IncompleteLegacyProfileFallsBackThenResetsBackup()
    {
        WithTemporarySaveDirectory(directory =>
        {
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            string validBackup =
                "<JusticeState version='1' enabled='true'>" +
                "<Case enabled='true' activeScore='73' fineDue='1250' sentenceSeconds='60' " +
                "hasWarrant='true' phase='AtLarge' wantedEpisodeId='backup-episode'>" +
                "<Charges><Charge kind='MurderCivilian' points='73' fine='1250' sentenceSeconds='60' /></Charges>" +
                "</Case>" +
                "<Record recidivism='0' cleanGameplaySeconds='0' appliedCleanDecay='0' />" +
                "<Custody active='false' site='None' />" +
                "</JusticeState>";

            foreach (string incompletePrimary in new[]
                     {
                          "<JusticeState version='1' enabled='true'><Record recidivism='99' /></JusticeState>",
                         "<JusticeState version='1' enabled='true'><Case activeScore='99' /></JusticeState>",
                         "<JusticeState version='1' enabled='true'><Case activeScore='99' />" +
                         "<Record recidivism='99' /></JusticeState>"
                     })
            {
                File.WriteAllText(primary, incompletePrimary);
                File.WriteAllText(backup, validBackup);
                object reader = CreateHeadlessScript();
                SetFieldValue(
                    reader,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));

                Assert.IsTrue(
                    (bool)InvokeInstance(reader, "TryLoadJusticeState", false),
                    "Un primaire incomplet doit être rejeté pour permettre la lecture du .bak.");
                JusticeCaseState loaded = GetFieldValue<JusticeCaseState>(reader, "_justiceCaseState");
                Assert.IsTrue(loaded.Enabled, "La préférence du backup valide doit être conservée.");
                Assert.AreEqual(0, loaded.ActiveScore);
                Assert.AreEqual(string.Empty, loaded.WantedEpisodeId);
                Assert.AreEqual(0, loaded.Charges.Count);
                Assert.IsTrue(GetFieldValue<bool>(reader, "_justiceDamageFrontPrimingPending"));
                Assert.AreEqual(2, GetFieldValue<int>(reader, "_justiceSentencePolicyVersion"));
            }
        });
    }

    [TestMethod]
    public void Persistence_LegacyFineIntentIsIgnoredButInvalidInventoryFallsBack()
    {
        WithTemporarySaveDirectory(directory =>
        {
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            string validBackup =
                "<JusticeState version='1' enabled='false'>" +
                "<Case enabled='false' activeScore='73' fineDue='1250' sentenceSeconds='60' " +
                "hasWarrant='true' phase='AtLarge' wantedEpisodeId='backup-semantic'>" +
                "<Charges><Charge kind='MurderCivilian' points='73' fine='1250' sentenceSeconds='60' /></Charges>" +
                "</Case>" +
                "<Record recidivism='0' cleanGameplaySeconds='0' appliedCleanDecay='0' />" +
                "<Custody active='false' site='None' />" +
                "</JusticeState>";
            string[] invalidPrimaries =
            {
                "<JusticeState version='1' enabled='true'>" +
                "<Case enabled='true' activeScore='30' fineDue='1000' sentenceSeconds='240' " +
                "phase='Captured' custodyEpisodeId='custody:bad-fine' />" +
                "<Record recidivism='0' />" +
                "<Custody active='true' site='MissionRow' playerModelHash='12345' playerSlot='0'>" +
                "<FineDebitIntent episodeId='custody:bad-fine' slot='0' fineAmount='1000' " +
                "debitAmount='600' cashBefore='2000' cashAfter='1900' " +
                "sentenceIfDebited='240' sentenceIfConverted='270' stationPlanned='true' />" +
                "</Custody></JusticeState>",
                "<JusticeState version='1' enabled='true'>" +
                "<Case enabled='true' activeScore='30' fineDue='0' sentenceSeconds='240' " +
                "phase='Incarcerated' custodyEpisodeId='custody:bad-snapshot' />" +
                "<Record recidivism='0' />" +
                "<Custody active='true' site='MissionRow' inventoryRemoved='true' " +
                "weaponControlsLocked='false' playerModelHash='12345' playerSlot='0'>" +
                "<InventorySnapshot validated='true' selectedWeapon='0'>" +
                "<Weapon hash='0' ammo='25' clip='10' tint='0' />" +
                "</InventorySnapshot></Custody></JusticeState>"
            };

            for (int index = 0; index < invalidPrimaries.Length; index++)
            {
                File.WriteAllText(primary, invalidPrimaries[index]);
                File.WriteAllText(backup, validBackup);
                object reader = CreateHeadlessScript();
                SetFieldValue(
                    reader,
                    "_justiceCanonicalPlayerSlotOverride",
                    new Func<int>(() => 0));

                Assert.IsTrue((bool)InvokeInstance(reader, "TryLoadJusticeState", false));
                JusticeCaseState loaded = GetFieldValue<JusticeCaseState>(reader, "_justiceCaseState");
                Assert.AreEqual(0, loaded.ActiveScore);
                Assert.AreEqual(string.Empty, loaded.WantedEpisodeId);
                Assert.AreEqual(0, loaded.Charges.Count);
                Assert.AreEqual(
                    index == 0,
                    loaded.Enabled,
                    index == 0
                        ? "Une ancienne intention financière invalide est effacée avec le dossier."
                        : "Un inventaire physique invalide doit forcer le fallback sur le backup OFF.");
                Assert.AreEqual(index == 0 ? 1 : 0, GetFieldValue<int>(
                    reader,
                    "_justicePolicyResetRecoveryMask"));
            }
        });
    }

    private static object CreatePending(JusticeCrimeKind kind, long createdAtMs, bool authoritative)
    {
        object pending = Activator.CreateInstance(GetNestedType("JusticePendingRuntimeIncident"), true);
        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = "edge:" + kind + ":" + createdAtMs,
            EpisodeId = "edge-episode",
            Kind = kind,
            CreatedAtMs = createdAtMs,
            ExpiresAtMs = createdAtMs + JusticePolicy.PendingIncidentLifetimeMs,
            Evidence = new JusticeEvidence
            {
                Kind = authoritative
                    ? JusticeEvidenceKind.DirectGameReport
                    : JusticeEvidenceKind.CivilianWitness,
                HasPlausibleObserver = true,
                ObservedAtMs = createdAtMs,
                ReportDueAtMs = createdAtMs
            }
        };
        SetMemberValue(pending, "Incident", incident);
        return pending;
    }

    private static object CreateHeadlessScript()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
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
            InitializeEmptyCollectionField(script, fieldName);
        }
        SetFieldValue(script, "_justiceCaseState", new JusticeCaseState { Enabled = true });
        SetFieldValue(script, "_justiceRecordState", new JusticeRecordState());
        // Je reproduis ici les valeurs des initialiseurs que
        // FormatterServices.GetUninitializedObject n'exécute pas.
        SetFieldValue(script, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
        SetFieldValue(script, "_justiceCustodyPlayerSlot", -1);
        SetFieldValue(
            script,
            "_justiceReleaseSelectedWeaponHash",
            GetStaticFieldValue<int>("JusticeUnarmedHash"));
        return script;
    }

    private static void WithTemporarySaveDirectory(Action<string> action)
    {
        string previous = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(Path.GetTempPath(), "DonJJusticeEdgeTests-" + Guid.NewGuid().ToString("N"));
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
                Directory.Delete(fullDirectory, true);
            }
        }
    }

    private static string ReadSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            fileName));
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        string marker = methodName + "(";
        int nameIndex = -1;
        int searchAt = 0;
        while (searchAt < source.Length)
        {
            int candidate = source.IndexOf(marker, searchAt, StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string declarationPrefix = source.Substring(lineStart, candidate - lineStart);
            if (declarationPrefix.IndexOf("private ", StringComparison.Ordinal) >= 0)
            {
                nameIndex = candidate;
                break;
            }
            searchAt = candidate + marker.Length;
        }

        Assert.IsTrue(nameIndex >= 0, "Méthode source introuvable : " + methodName);
        int openingBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openingBrace >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0)
            {
                return source.Substring(openingBrace, index - openingBrace + 1);
            }
        }

        Assert.Fail("Corps source non fermé : " + methodName);
        return string.Empty;
    }

    private static int CountOccurrences(string source, string marker)
    {
        int count = 0;
        int position = 0;
        while ((position = source.IndexOf(marker, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += marker.Length;
        }
        return count;
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        int previous = -1;
        foreach (string marker in markers)
        {
            int current = source.IndexOf(marker, previous + 1, StringComparison.Ordinal);
            Assert.IsTrue(current > previous, "Ordre de sécurité invalide ou marqueur absent : " + marker);
            previous = current;
        }
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        MethodInfo[] matches = target.GetType()
            .GetMethods(PrivateInstance)
            .Where(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .ToArray();
        Assert.AreEqual(1, matches.Length, methodName + " doit exposer une signature privée unique.");
        return matches[0].Invoke(target, args);
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        MethodInfo[] matches = ScriptType
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .ToArray();
        Assert.AreEqual(1, matches.Length, methodName + " doit exposer une signature statique privée unique.");
        return matches[0].Invoke(null, args);
    }

    private static Type GetNestedType(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type privé introuvable : " + name);
        return type;
    }

    private static T GetStaticFieldValue<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field, "Champ statique privé introuvable : " + fieldName);
        object value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return (T)value;
    }

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetFieldValue(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static object GetMemberValue(object target, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo field = target.GetType().GetField(memberName, flags);
        if (field != null) return field.GetValue(target);
        PropertyInfo property = target.GetType().GetProperty(memberName, flags);
        Assert.IsNotNull(property, "Membre privé introuvable : " + memberName);
        return property.GetValue(target, null);
    }

    private static void SetMemberValue(object target, string memberName, object value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo field = target.GetType().GetField(memberName, flags);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }
        PropertyInfo property = target.GetType().GetProperty(memberName, flags);
        Assert.IsNotNull(property, "Membre privé introuvable : " + memberName);
        property.SetValue(target, value, null);
    }

    private static void InitializeEmptyCollectionField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | PrivateInstance);
        Assert.IsNotNull(field, "Collection privée introuvable : " + fieldName);
        field.SetValue(target, Activator.CreateInstance(field.FieldType, true));
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
