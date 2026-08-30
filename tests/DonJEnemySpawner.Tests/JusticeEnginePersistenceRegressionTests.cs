using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeEnginePersistenceRegressionTests
{
    [TestMethod]
    public void IncidentResolution_PrioritizesRelatedViolenceBeforeRecklessDischarge()
    {
        JusticeIncident reckless = Incident(
            JusticeCrimeKind.RecklessDischarge,
            "reckless",
            "episode:one",
            "causal:shot",
            2000L,
            0);
        JusticeIncident violence = Incident(
            JusticeCrimeKind.MurderCivilian,
            "violence",
            "episode:one",
            "causal:shot",
            2000L,
            42);

        Assert.IsTrue(JusticePolicy.CompareIncidentResolutionPriority(violence, reckless) > 0);
        Assert.IsTrue(JusticePolicy.CompareIncidentResolutionPriority(reckless, violence) < 0);
        Assert.IsTrue(JusticePolicy.DoesConfirmedViolenceSupersedeRecklessDischarge(
            violence,
            reckless));

        string resolver = ExtractMethodBody(ReadJusticeSource(), "ProcessJusticePendingIncidents");
        AssertOrdered(
            resolver,
            "JusticePolicy.CompareIncidentResolutionPriority",
            "_justicePendingIncidents.RemoveAt(pendingIndex)",
            "DoesConfirmedViolenceSupersedeRecklessDischarge",
            "JusticePolicy.ApplyConfirmedIncident",
            "OnJusticeChargeConfirmed");
    }

    [TestMethod]
    public void CanonicalPlayerIdentity_NeverAdoptsAnotherKnownStorySlot()
    {
        Assert.IsTrue(JusticePolicy.IsCanonicalPlayerIdentityCompatible(1, 0, 123, 1, 456));
        Assert.IsFalse(JusticePolicy.IsCanonicalPlayerIdentityCompatible(1, 0, 123, 2, 123));
        Assert.IsTrue(JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, 0, 123, 0, 999));
        Assert.IsFalse(JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, 0, 123, 2, 123));
        Assert.IsFalse(JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, -1, 123, 0, 123));
        Assert.IsTrue(JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, -1, 123, -1, 123));
        Assert.IsFalse(JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, -1, 0, -1, 0));

        Assert.AreEqual(2, JusticePolicy.ResolveTrustedCanonicalPlayerSlot(2, 1));
        Assert.AreEqual(1, JusticePolicy.ResolveTrustedCanonicalPlayerSlot(-1, 1));
        Assert.AreEqual(-1, JusticePolicy.ResolveTrustedCanonicalPlayerSlot(-1, -1));

        Assert.AreEqual(
            1,
            JusticePolicy.ResolvePoliceDeathFrontOwnerSlot(1, 0, 0),
            "Le ped canonique mort doit gagner sur le profil encore actif pendant le switch.");
        Assert.AreEqual(
            0,
            JusticePolicy.ResolvePoliceDeathFrontOwnerSlot(-1, 0, 2),
            "Sans slot courant, le profil actif reste la dernière autorité sûre.");
        Assert.AreEqual(
            2,
            JusticePolicy.ResolvePoliceDeathFrontOwnerSlot(-1, -1, 2));
        Assert.AreEqual(
            -1,
            JusticePolicy.ResolvePoliceDeathFrontOwnerSlot(-1, -1, -1));

        Assert.IsFalse(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                false,
                false,
                5,
                5,
                true,
                true),
            "Un profil Q désactivé ne doit jamais être réactivé par la poursuite de P.");
        Assert.IsTrue(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                true,
                false,
                2,
                0,
                false,
                false),
            "Q actif doit conserver sa mort lorsque le ped courant porte ses étoiles.");
        Assert.IsTrue(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                true,
                false,
                0,
                0,
                false,
                true),
            "Le tueur policier courant doit suffire pendant le changement P vers Q.");
        Assert.IsFalse(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                true,
                false,
                0,
                5,
                true,
                false),
            "Les anciens latches de P ne doivent jamais fabriquer un front pour Q.");
        Assert.IsTrue(
            JusticePolicy.IsPoliceDeathFrontAdmissionAllowed(
                true,
                true,
                0,
                5,
                false,
                false),
            "Le profil déjà actif conserve la tolérance à l'effacement précoce des étoiles.");
    }

    [TestMethod]
    public void DeathFrontResultCandidate_WaitsForItsExactDiskRevisionAndRetriesAfterCoalescing()
    {
        object script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        object wal = FormatterServices.GetUninitializedObject(typeof(JusticeWriteAheadLog));
        const string transactionId = "death-front-candidate";
        Dictionary<string, long> candidates = new Dictionary<string, long>
        {
            { transactionId, 42L }
        };

        SetPrivateField(script, "_justiceWriteAheadLog", wal);
        SetPrivateField(script, "_justiceDeathFrontResultCandidates", candidates);
        SetPrivateField(script, "_justiceInitialized", true);
        SetPrivateField(script, "_justiceStateDirty", false);

        MethodInfo advance = typeof(DonJEnemySpawner).GetMethod(
            "AdvanceJusticeDeathFrontWalResults",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(advance);

        advance.Invoke(script, new object[] { 41L });

        Assert.IsFalse(
            GetPrivateField<bool>(script, "_justiceStateDirty"),
            "Une écriture encore en vol ne doit pas créer une nouvelle révision qui la coalescerait.");
        Assert.AreSame(
            candidates,
            GetPrivateField<Dictionary<string, long>>(
                script,
                "_justiceDeathFrontResultCandidates"));
        Assert.AreEqual(42L, candidates[transactionId]);

        SetPrivateField(script, "_justiceStateDirty", false);
        advance.Invoke(script, new object[] { 43L });

        Assert.IsTrue(
            GetPrivateField<bool>(script, "_justiceStateDirty"),
            "Une révision candidate coalescée doit demander un nouveau snapshot du résultat.");
        Assert.AreSame(
            candidates,
            GetPrivateField<Dictionary<string, long>>(
                script,
                "_justiceDeathFrontResultCandidates"));
        Assert.AreEqual(42L, candidates[transactionId]);
    }

    [TestMethod]
    public void BackupRepair_ReconcilesDeferredFrontsOnlyAfterCanonicalProfileSwitchCompletes()
    {
        string early = ExtractMethodBody(ReadJusticeSource(), "UpdateJusticeEarly");
        const string profileGate =
            "if (!EnsureJusticeProfileMatchesCanonicalPlayer(player))";
        const string durabilityGate =
            "!TryHardenJusticeDeferredCriticalFronts()";
        const string reconcile =
            "ReconcileJusticeFrontsAfterPersistenceRepair(player, wantedLevel)";

        int durabilityGateIndex = early.IndexOf(
            durabilityGate,
            StringComparison.Ordinal);
        int profileGateIndex = early.IndexOf(profileGate, StringComparison.Ordinal);
        int reconcileIndex = early.IndexOf(reconcile, StringComparison.Ordinal);
        Assert.IsTrue(
            durabilityGateIndex >= 0,
            "La preuve WAL des fronts critiques différés est absente.");
        Assert.IsTrue(profileGateIndex >= 0, "Le gate du profil canonique est absent.");
        Assert.IsTrue(reconcileIndex >= 0, "La réconciliation post-réparation est absente.");
        Assert.IsTrue(
            durabilityGateIndex < profileGateIndex && profileGateIndex < reconcileIndex,
            "Le WAL doit précéder le snapshot du switch, puis la réconciliation métier.");

        string switchGate = ExtractBlockFromMarker(early, profileGate);
        AssertOrdered(
            switchGate,
            profileGate,
            "_justiceProfileContextBlocked = true",
            "preserveUnattributedDeathEdge =",
            "return;");
        Assert.IsFalse(
            switchGate.Contains(reconcile),
            "Un switch inachevé ne doit jamais consommer les fronts de réparation.");
    }

    [TestMethod]
    public void DeathCapture_RequiresAPreviouslyProvenCanonicalSlot()
    {
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, true, 1, 1));
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, false, 1, -1));
        Assert.IsFalse(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, false, 1, -1, true));
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, false, 1, -2, true));
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, false, false, 1, -1, true));
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, true, 1, -1, true));
        Assert.IsTrue(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, false, 1, 2));
        Assert.IsFalse(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            true, true, false, 1, 1));
        Assert.IsFalse(JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
            false, true, true, 1, 1));

        Assert.IsTrue(JusticePolicy.IsCustodyDeathIdentityCompatible(
            1, -1, 44, 44, 123, 123));
        Assert.IsFalse(JusticePolicy.IsCustodyDeathIdentityCompatible(
            1, -1, 44, 45, 123, 123));
        Assert.IsFalse(JusticePolicy.IsCustodyDeathIdentityCompatible(
            1, -1, 44, 44, 123, 456));
        Assert.IsTrue(JusticePolicy.IsCustodyDeathIdentityCompatible(
            1, 1, 44, 99, 123, 456));
        Assert.IsFalse(JusticePolicy.IsCustodyDeathIdentityCompatible(
            1, 2, 44, 44, 123, 123));

        Assert.IsTrue(JusticePolicy.IsCustodyLiveIdentityCompatible(
            1, -1, 44, 44, 123, 123));
        Assert.IsFalse(JusticePolicy.IsCustodyLiveIdentityCompatible(
            1, -1, 44, 45, 123, 123));
        Assert.IsTrue(JusticePolicy.IsCustodyLiveIdentityCompatible(
            1, 1, 0, 99, 123, 456));
        Assert.IsFalse(JusticePolicy.IsCustodyLiveIdentityCompatible(
            1, 2, 44, 44, 123, 123));
        Assert.IsFalse(JusticePolicy.IsCustodyLiveIdentityCompatible(
            1, -1, 0, 44, 123, 123));

        string custody = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string bind = ExtractMethodBody(
            custody,
            "TryBindJusticeCustodyPlayerIdentityForCapture");
        AssertOrdered(
            bind,
            "ResolveTrustedCanonicalPlayerSlot",
            "if (trustedSlot < 0)",
            "_justiceCustodyPlayerModelHash = modelHash");

        string rebind = ExtractMethodBody(
            custody,
            "TryRebindJusticeCustodyIdentityAfterRespawn");
        AssertOrdered(
            rebind,
            "int currentSlot = GetCurrentSinglePlayerCashSlotSafe()",
            "JusticePolicy.CanRebindCustodyRespawnSlot(",
            "_justiceCustodyPlayerHandle = player.Handle");
        Assert.IsFalse(rebind.Contains("_justiceCustodyPlayerSlot = currentSlot"));

        string transfer = ExtractMethodBody(custody, "JusticeBeginCustodyTransfer");
        AssertOrdered(
            transfer,
            "_justiceCustodyWaitingForRespawn = true",
            "JusticeFlushStateNow()",
            "bool provenCustomRespawn = waitingPlayerSlot == -1",
            "ShouldDeferCustodyFinancialMutationUntilRespawn",
            "JusticeCollectFineAndConvertDetention");
        string collectFine = ExtractMethodBody(
            custody,
            "JusticeCollectFineAndConvertDetention");
        AssertOrdered(
            collectFine,
            "int slot = GetCurrentSinglePlayerCashSlotSafe()",
            "if (slot < 0)",
            "_justiceCaseState.FineDue = 0L",
            "CalculateJusticeSentenceAfterFineConversion",
            "JusticeFlushStateNow()");
        string deathIdentity = ExtractMethodBody(
            custody,
            "IsJusticeCustodyDeathIdentityCompatible");
        StringAssert.Contains(deathIdentity, "JusticePolicy.IsCustodyDeathIdentityCompatible");
        string liveIdentity = ExtractMethodBody(
            custody,
            "IsJusticeCustodyPlayerIdentityCompatible");
        StringAssert.Contains(liveIdentity, "JusticePolicy.IsCustodyLiveIdentityCompatible");

        string runtime = ReadJusticeSource();
        string capture = ExtractMethodBody(runtime, "BeginJusticeCapture");
        AssertOrdered(
            capture,
            "TryBindJusticeCustodyPlayerIdentityForCapture(capturedPlayer, deathCapture)",
            "_justicePursuitDeathObservedDuringSuspension = true",
            "JusticeFlushStateNow()");
        AssertOrdered(
            capture,
            "else if (captureTrustedSlot < 0)",
            "FinalizeUnknownJusticeCaptureAsWarrant(",
            "return true;");
        string warrant = ExtractMethodBody(
            runtime,
            "FinalizeUnknownJusticeCaptureAsWarrant");
        AssertOrdered(
            warrant,
            "_justiceCaseState.HasWarrant = true",
            "ClearPendingJusticeDeathCapture()",
            "JusticeFlushStateNow()");
        Assert.IsFalse(warrant.Contains("JusticeBeginCustodyTransfer"));
        Assert.IsFalse(warrant.Contains("RemoveAll"));
    }

    [TestMethod]
    public void Amnesty_PersistsIntentBeforeSideEffectsAndReloadsTheSameLatch()
    {
        string source = ReadJusticeSource();
        string begin = ExtractMethodBody(source, "ExecuteJusticeAmnestyAndDisable");
        AssertOrdered(
            begin,
            "_justiceAmnestyPending = true",
            "_justiceAmnestyPrecommitRedundant = false",
            "EnsureJusticeAmnestyPrecommitRedundant()",
            "ResumeJusticeAmnestyTransaction()");

        string ensure = ExtractMethodBody(source, "EnsureJusticeAmnestyPrecommitRedundant");
        AssertOrdered(
            ensure,
            "_justiceAmnestyPending",
            "JusticeMarkStateDirty()",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "_justiceAmnestyPrecommitRedundant = true");
        Assert.IsFalse(
            begin.Contains("_justiceAmnestyPending = false"),
            "Un échec ambigu du backup ne doit pas annuler l'intention déjà écrite au primaire.");

        string resume = ExtractMethodBody(source, "ResumeJusticeAmnestyTransaction");
        AssertOrdered(
            resume,
            "EnsureJusticeAmnestyPrecommitRedundant()",
            "JusticeAmnestyCustody()",
            "JusticeFlushStateNow()",
            "TryApplyJusticeAmnestyWantedClear()",
            "_justiceAmnestyPending = false",
            "JusticeFlushStateNow()");

        StringAssert.Contains(source, "\"pendingAmnestyWantedClear\"");
        StringAssert.Contains(source, "out loadedPendingAmnesty");
    }

    [TestMethod]
    public void OfflineRepair_ClearsOnlyWantedAndKeepsJusticeEnabled()
    {
        Assert.IsTrue(JusticePolicy.IsWantedOnlyRepairRecovery(true, false, false));
        Assert.IsFalse(JusticePolicy.IsWantedOnlyRepairRecovery(false, false, false));
        Assert.IsFalse(JusticePolicy.IsWantedOnlyRepairRecovery(true, true, false));
        Assert.IsFalse(JusticePolicy.IsWantedOnlyRepairRecovery(true, false, true));

        string source = ReadJusticeSource();
        string resume = ExtractMethodBody(source, "ResumeJusticeAmnestyTransaction");
        AssertOrdered(
            resume,
            "JusticePolicy.IsWantedOnlyRepairRecovery",
            "ResumeJusticeWantedOnlyRepair()",
            "JusticeAmnestyCustody()");

        string repair = ExtractMethodBody(source, "ResumeJusticeWantedOnlyRepair");
        AssertOrdered(
            repair,
            "TryApplyJusticeAmnestyWantedClear()",
            "_justiceAmnestyPending = false",
            "JusticeFlushStateNow()");
        Assert.IsFalse(repair.Contains("_justiceEnabled = false"));
        Assert.IsFalse(repair.Contains("ClearActiveCase"));
    }

    [TestMethod]
    public void AppliedConvictions_KeepTheActiveCustodyConvictionPinnedPastTheBound()
    {
        JusticeRecordState record = new JusticeRecordState();
        JusticeCaseState main = ConvictionCase("custody:main", 25);
        JusticeConviction mainConviction = JusticePolicy.ApplyConviction(main, record, DateTime.UtcNow);
        Assert.IsNotNull(mainConviction);

        for (int index = 0; index < JusticePolicy.MaxAppliedConvictionIds + 20; index++)
        {
            JusticeCaseState discipline = ConvictionCase(
                "custody:main:discipline:" + index,
                10);
            Assert.IsNotNull(JusticePolicy.ApplyConviction(discipline, record, DateTime.UtcNow));
        }

        Assert.AreEqual("conviction:custody:main", record.PinnedConvictionId);
        Assert.AreEqual(JusticePolicy.MaxAppliedConvictionIds, record.AppliedConvictionIds.Count);
        CollectionAssert.Contains(record.AppliedConvictionIds, record.PinnedConvictionId);
        Assert.AreEqual(JusticePolicy.MaxConvictions, record.Convictions.Count);
    }

    [TestMethod]
    public void WantedRise_IsLatchedUntilThePostDetectionPassAndCannotSeeFutureIncidents()
    {
        string source = ReadJusticeSource();
        string edges = ExtractMethodBody(source, "UpdateJusticeWantedEdges");
        StringAssert.Contains(edges, "CorrelateJusticeWantedRise()");

        string update = ExtractMethodBody(source, "UpdateJusticeSystem");
        AssertOrdered(
            update,
            "DetectJusticeEventFronts(player)",
            "CorrelateJusticeWantedRise()",
            "ProcessJusticePendingIncidents()");

        string correlate = ExtractMethodBody(source, "CorrelateJusticeWantedRise");
        StringAssert.Contains(correlate, "incident.CreatedAtMs > _justiceWantedRiseObservedAtMs");
        StringAssert.Contains(correlate, "_justiceEventDetectionPass < _justiceWantedRiseDetectionPass");
        AssertOrdered(
            correlate,
            "JusticePolicy.CompareIncidentResolutionPriority",
            "ClearLatchedJusticeWantedRise()");

        Assert.IsTrue(JusticePolicy.IsWantedCorrelationCandidate(4000L, 0L, true, true));
        Assert.IsFalse(JusticePolicy.IsWantedCorrelationCandidate(4001L, 0L, true, true));
        Assert.IsFalse(JusticePolicy.IsWantedCorrelationCandidate(999L, 1000L, true, true));
    }

    [TestMethod]
    public void SelfDefense_UsesTheObservedThreatToClassifyEscalation()
    {
        Assert.AreEqual(
            JusticeCircumstances.ProportionalSelfDefense,
            JusticePolicy.ClassifySelfDefenseResponse(true, false, true, false, true));
        Assert.AreEqual(
            JusticeCircumstances.ProportionalSelfDefense,
            JusticePolicy.ClassifySelfDefenseResponse(false, false, false, false, false));
        Assert.AreEqual(
            JusticeCircumstances.ExcessiveSelfDefense,
            JusticePolicy.ClassifySelfDefenseResponse(false, false, true, false, false));
        Assert.AreEqual(
            JusticeCircumstances.ExcessiveSelfDefense,
            JusticePolicy.ClassifySelfDefenseResponse(true, false, false, true, true));

        string source = ReadJusticeSource();
        string detection = ExtractMethodBody(source, "ObserveJusticeHostileInitiation");
        StringAssert.Contains(detection, "IsPedShooting(candidate)");
        StringAssert.Contains(detection, "candidate.IsInCombatAgainst(player)");
        string circumstances = ExtractMethodBody(source, "BuildJusticeAssaultCircumstances");
        StringAssert.Contains(circumstances, "!IsJusticePolicePed(victim)");
        StringAssert.Contains(circumstances, "ClassifySelfDefenseResponse");
    }

    [TestMethod]
    public void WitnessSnapshot_ReservesVictimsBeforePoliceAndLivingWitnesses()
    {
        string runtime = ReadJusticeSource();
        string worldSnapshot = ReadJusticeWorldSnapshotSource();
        string worldCapture = ExtractMethodBody(worldSnapshot, "CaptureJusticeWorldSnapshot");
        string detection = ExtractMethodBody(runtime, "DetectJusticeEventFronts");
        string witnessCapture = ExtractMethodBody(runtime, "GetJusticeWitnessCandidatesForActor");

        Assert.AreEqual(1, CountOccurrences(worldCapture, "GetNearbyPedsSafe"));
        Assert.AreEqual(1, CountOccurrences(worldCapture, "GetNearbyVehiclesSafe"));
        AssertOrdered(
            worldCapture,
            "GetNearbyPedsSafe(player, JusticeWorldSnapshotRadius)",
            "_justiceWorldSnapshot.PedQueryCount = 1",
            "GetNearbyVehiclesSafe(player, JusticeWorldSnapshotRadius)",
            "_justiceWorldSnapshot.VehicleQueryCount = 1");
        Assert.AreEqual(1, CountOccurrences(detection, "CaptureJusticeWorldSnapshot(player)"));
        AssertOrdered(
            detection,
            "CaptureJusticeWorldSnapshot(player)",
            "ScanJusticeEventVictims(");

        Assert.AreEqual(0, CountOccurrences(runtime, "GetNearbyPedsSafe"));
        Assert.AreEqual(0, CountOccurrences(runtime, "GetNearbyVehiclesSafe"));
        StringAssert.Contains(witnessCapture, "Ped[] nearby = GetJusticeSnapshotPeds()");
        StringAssert.Contains(
            witnessCapture,
            "IsJusticeSnapshotEntityWithin(candidate, actor, JusticeWitnessRadius)");
        StringAssert.Contains(witnessCapture, "for (int pass = 0; pass < 3");
        AssertOrdered(
            witnessCapture,
            "bool dead",
            "dead && victimCount < JusticeMaximumVictimCandidatesPerEvent",
            "!dead && IsJusticePolicePed(candidate)",
            "!dead && !IsJusticePolicePed(candidate)",
            "target.Candidates[candidateCount++]");
    }

    [TestMethod]
    public void XmlV1ChargeIdentity_IsMigratedButContradictoryOrDuplicateIdsAreRejected()
    {
        JusticeCharge legacy = new JusticeCharge
        {
            IncidentId = "incident:legacy",
            IsAggregate = false,
            AggregatedChargeCount = 0
        };
        Assert.IsTrue(JusticePolicy.TryNormalizePersistedChargeIdentity(legacy, "episode:legacy"));
        Assert.AreEqual("charge:incident:legacy", legacy.ChargeId);
        Assert.AreEqual("episode:legacy", legacy.EpisodeId);

        JusticeCharge aggregate = new JusticeCharge
        {
            ChargeId = "charge:aggregate:pending",
            IncidentId = "incident:aggregate",
            IsAggregate = true,
            IsAdjudicated = true,
            AggregatedChargeCount = 4
        };
        Assert.IsTrue(JusticePolicy.TryNormalizePersistedChargeIdentity(aggregate, "episode:legacy"));
        Assert.AreEqual(
            "charge:aggregate:adjudicated:incident:aggregate",
            aggregate.ChargeId);

        JusticeCharge contradictory = new JusticeCharge
        {
            ChargeId = "charge:other",
            IncidentId = "incident:expected",
            AggregatedChargeCount = 0
        };
        Assert.IsFalse(JusticePolicy.TryNormalizePersistedChargeIdentity(
            contradictory,
            "episode:legacy"));

        Assert.IsNotNull(ReadCaseXml(CaseXml(
            "<Charge incidentId='incident:one' kind='VehicleTheft' points='12' fine='750' />",
            12,
            750L)));
        Assert.IsNull(ReadCaseXml(CaseXml(
            "<Charge id='charge:wrong' incidentId='incident:one' kind='VehicleTheft' points='12' fine='750' />",
            12,
            750L)));
        Assert.IsNull(ReadCaseXml(CaseXml(
            "<Charge incidentId='incident:one' kind='VehicleTheft' points='12' fine='750' />" +
            "<Charge incidentId='incident:one' kind='VehicleTheft' points='12' fine='750' />",
            24,
            1500L)));
    }

    private static JusticeIncident Incident(
        JusticeCrimeKind kind,
        string id,
        string episode,
        string causal,
        long createdAt,
        int victimHandle)
    {
        return new JusticeIncident
        {
            Kind = kind,
            IncidentId = id,
            EpisodeId = episode,
            CausalEventId = causal,
            CreatedAtMs = createdAt,
            VictimHandle = victimHandle
        };
    }

    private static JusticeCaseState ConvictionCase(string custodyEpisodeId, int points)
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            CustodyEpisodeId = custodyEpisodeId
        };
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + custodyEpisodeId,
            IncidentId = custodyEpisodeId,
            EpisodeId = custodyEpisodeId,
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Test",
            Points = points,
            Fine = 100L,
            SentenceSeconds = 15
        });
        state.RecalculateTotals();
        return state;
    }

    private static string CaseXml(string charges, int score, long fine)
    {
        return "<Case enabled='true' activeScore='" + score + "' fineDue='" + fine +
               "' sentenceSeconds='0' hasWarrant='true' phase='AtLarge' " +
               "wantedEpisodeId='episode:legacy' custodyEpisodeId='' lastCrimeKind='VehicleTheft'>" +
               "<Charges>" + charges + "</Charges><FleeingEpisodes/><EscapeEpisodes/>" +
               "<ProcessedIncidents/><CompletedOperations/></Case>";
    }

    private static JusticeCaseState ReadCaseXml(string xml)
    {
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        MethodInfo reader = typeof(DonJEnemySpawner).GetMethod(
            "ReadJusticeCaseXml",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(reader);
        return (JusticeCaseState)reader.Invoke(null, new object[] { document.DocumentElement });
    }

    private static string ReadJusticeSource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
    }

    private static string ReadJusticeWorldSnapshotSource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.WorldSnapshot.cs"));
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
            string prefix = source.Substring(lineStart, candidate - lineStart);
            if (prefix.Contains("private "))
            {
                nameIndex = candidate;
                break;
            }
            searchAt = candidate + marker.Length;
        }

        Assert.IsTrue(nameIndex >= 0, "Méthode introuvable : " + methodName);
        int openingBrace = source.IndexOf('{', nameIndex);
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
        Assert.Fail("Corps non fermé : " + methodName);
        return string.Empty;
    }

    private static string ExtractBlockFromMarker(string source, string marker)
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(markerIndex >= 0, "Marqueur de bloc introuvable : " + marker);
        int openingBrace = source.IndexOf('{', markerIndex);
        Assert.IsTrue(openingBrace >= 0, "Bloc sans accolade : " + marker);
        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0)
            {
                return source.Substring(markerIndex, index - markerIndex + 1);
            }
        }
        Assert.Fail("Bloc non fermé : " + marker);
        return string.Empty;
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        int previous = -1;
        for (int index = 0; index < markers.Length; index++)
        {
            int current = source.IndexOf(markers[index], previous + 1, StringComparison.Ordinal);
            Assert.IsTrue(current > previous, "Ordre invalide ou marqueur absent : " + markers[index]);
            previous = current;
        }
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

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GTA5modDEV.sln")))
        {
            directory = directory.Parent;
        }
        Assert.IsNotNull(directory, "Racine du dépôt introuvable.");
        return directory.FullName;
    }
}
