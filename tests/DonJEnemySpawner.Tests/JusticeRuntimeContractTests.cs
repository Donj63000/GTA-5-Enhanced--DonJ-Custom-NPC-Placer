using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeRuntimeContractTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null))
        .ToDictionary(opCode => opCode.Value);

    [TestMethod]
    public void RuntimeJustice_CadencesAndBuffersStayBoundedWithoutWorldWideScans()
    {
        Assert.AreEqual(120, GetStaticFieldValue<int>("JusticeCrimeScanIntervalMs"));
        Assert.AreEqual(120, GetStaticFieldValue<int>("JusticeScalarScanIntervalMs"));
        Assert.IsTrue(GetStaticFieldValue<int>("JusticeCrimeScanIntervalMs") >= 90);
        Assert.IsTrue(GetStaticFieldValue<int>("JusticeCrimeScanIntervalMs") <= 150);
        Assert.AreEqual(24, GetStaticFieldValue<int>("JusticeMaximumWitnessesPerEvent"));
        Assert.AreEqual(16, GetStaticFieldValue<int>("JusticeMaximumVehiclesPerEvent"));
        Assert.AreEqual(32, GetStaticFieldValue<int>("JusticeMaximumPendingIncidents"));
        Assert.AreEqual(48, GetStaticFieldValue<int>("JusticeMaximumAllyTokens"));
        Assert.AreEqual(24, GetStaticFieldValue<int>("JusticeMaximumWitnessActorSnapshots"));
        Assert.AreEqual(6, GetStaticFieldValue<int>("JusticeMaximumConfirmedIncidentsPerTick"));
        Assert.AreEqual(80.0f, GetStaticFieldValue<float>("JusticeWitnessRadius"), 0.001f);
        Assert.AreEqual(12000, GetStaticFieldValue<int>("JusticeAllyAttributionLifetimeMs"));
        Assert.AreEqual(120.0f, GetStaticFieldValue<float>("JusticeAllyAttributionRadius"), 0.001f);

        string[] hotMethods =
        {
            "UpdateJusticeSystem",
            "DetectJusticeEventFronts",
            "BuildJusticeEvidence",
            "UpdateJusticeWarrantRecognition",
            "ProcessJusticeAllyAttributionTokens"
        };

        foreach (string methodName in hotMethods)
        {
            MethodInfo method = FindMethod(methodName, PrivateInstance);
            List<MethodBase> calls = ReadCalledMethods(method);
            Assert.IsFalse(
                calls.Any(call => call.DeclaringType == typeof(Enumerable)),
                methodName + " ne doit pas allouer via LINQ dans le chemin runtime.");
            Assert.IsFalse(
                calls.Any(call => call.Name == "GetAllPeds" || call.Name == "GetAllVehicles" || call.Name == "GetAllEntities"),
                methodName + " ne doit jamais scanner tout le monde GTA.");
        }

        List<MethodBase> evidenceCalls = ReadCalledMethods(FindMethod("BuildJusticeEvidence", PrivateInstance));
        Assert.AreEqual(
            1,
            evidenceCalls.Count(call => call.Name == "GetJusticeWitnessCandidatesForActor"),
            "Je réutilise au plus un snapshot borné de témoins pour chaque acteur du nouvel acte.");
        Assert.AreEqual(
            0,
            evidenceCalls.Count(call => call.Name == "GetNearbyPedsSafe"),
            "La qualification d'une infraction ne doit plus rescanner directement le monde.");

        List<MethodBase> actorSnapshotCalls = ReadCalledMethods(
            FindMethod("GetJusticeWitnessCandidatesForActor", PrivateInstance));
        Assert.AreEqual(
            0,
            actorSnapshotCalls.Count(call => call.Name == "GetNearbyPedsSafe"),
            "Le filtre d'un acteur ne doit jamais relancer une requête monde GTA.");
        Assert.AreEqual(
            1,
            actorSnapshotCalls.Count(call => call.Name == "GetJusticeSnapshotPeds"),
            "Chaque acteur réutilise le tableau peds immuable de la passe courante.");
        Assert.AreEqual(
            1,
            actorSnapshotCalls.Count(call => call.Name == "IsJusticeSnapshotEntityWithin"),
            "La proximité d'un acteur doit être filtrée par distance au carré dans le snapshot partagé.");

        List<MethodBase> worldCaptureCalls = ReadCalledMethods(
            FindMethod("CaptureJusticeWorldSnapshot", PrivateInstance));
        Assert.AreEqual(
            1,
            worldCaptureCalls.Count(call => call.Name == "GetNearbyPedsSafe"),
            "Une passe Justice réalise une seule requête peds GTA.");
        Assert.AreEqual(
            1,
            worldCaptureCalls.Count(call => call.Name == "GetNearbyVehiclesSafe"),
            "Une passe Justice réalise une seule requête véhicules GTA.");

        string evidence = ExecutableMethodBody(ReadRuntimeSource(), "BuildJusticeEvidence");
        StringAssert.Contains(evidence, "humans < JusticeMaximumWitnessesPerEvent");
    }

    [TestMethod]
    public void RuntimeJustice_WantedRiseOnlyCorrelatesRecentObservedIncidents()
    {
        Assert.IsTrue(JusticePolicy.IsWantedCorrelationCandidate(5000L, 1000L, true, true));
        Assert.IsFalse(JusticePolicy.IsWantedCorrelationCandidate(5000L, 999L, true, true));
        Assert.IsFalse(JusticePolicy.IsWantedCorrelationCandidate(5000L, 4500L, false, true));
        Assert.IsFalse(JusticePolicy.IsWantedCorrelationCandidate(5000L, 4500L, true, false));

        string source = ReadRuntimeSource();
        string correlation = ExecutableMethodBody(source, "CorrelateJusticeWantedRise");
        StringAssert.Contains(correlation, "JusticePolicy.IsWantedCorrelationCandidate(");
        StringAssert.Contains(correlation, "HasCredibleJusticeObserverAtWantedRise(pending)");
        StringAssert.Contains(correlation, "incident.CreatedAtMs > bestMatch.CreatedAtMs");
        StringAssert.Contains(correlation, "if (bestMatch != null)");
        Assert.IsFalse(
            correlation.IndexOf("new JusticeIncident", StringComparison.Ordinal) >= 0,
            "Une hausse d'étoiles seule ne doit jamais fabriquer une infraction.");
    }

    [TestMethod]
    public void RuntimeJustice_OwnWantedWriteSynchronizesTheNextEdgeWithoutCorrelation()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        state.ActiveScore = 60;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justiceLastWantedLevel", 1);
        SetFieldValue(script, "_justiceMonotonicTimeMs", 5000L);
        SetFieldValue(script, "_justiceWrittenWantedLevel", 3);
        SetFieldValue(script, "_justiceWrittenWantedExpiresAtMs", 5100L);

        JusticeIncident pendingIncident = NewUnconfirmedIncident("own-wanted", 4500L, true);
        GetFieldValue<IList>(script, "_justicePendingIncidents")
            .Add(CreatePendingRuntimeIncident(pendingIncident));

        // Je simule ici le tick qui observe la hausse écrite par Justice.
        InvokeInstance(script, "UpdateJusticeWantedEdges", 3);
        Assert.AreEqual(
            JusticeEvidenceKind.None,
            pendingIncident.Evidence.Kind,
            "L'étoile écrite par Justice ne doit pas confirmer son propre incident au tick suivant.");

        // Je vérifie la règle pure d'une vraie hausse externe après expiration
        // du jeton. L'entité témoin GTA vivante est couverte par l'inspection
        // structurelle ci-dessous, car un Ped ne peut pas être simulé hors jeu.
        SetFieldValue(script, "_justiceMonotonicTimeMs", 5200L);
        InvokeInstance(script, "UpdateJusticeWantedEdges", 4);
        Assert.IsTrue(
            JusticePolicy.IsWantedCorrelationCandidate(5200L, 4500L, true, true));

        string source = ReadRuntimeSource();
        string wantedEdges = ExecutableMethodBody(source, "UpdateJusticeWantedEdges");
        AssertOrdered(
            wantedEdges,
            "bool justiceAuthoredRise",
            "_justiceWrittenWantedExpiresAtMs",
            "_justiceWrittenWantedLevel",
            "if (!justiceAuthoredRise)",
            "CorrelateJusticeWantedRise()");

        string wantedWrite = ExecutableMethodBody(source, "SetJusticeWantedMinimum");
        AssertOrdered(
            wantedWrite,
            "int current = Game.Player.WantedLevel",
            "if (current < bounded)",
            "Game.Player.WantedLevel = bounded",
            "current = Game.Player.WantedLevel",
            "if (current < bounded)",
            "return false",
            "_justiceWrittenWantedLevel = bounded",
            "_justiceWrittenWantedExpiresAtMs",
            "_justiceLastWantedLevel = Math.Max(");
    }

    [TestMethod]
    public void RuntimeJustice_WitnessSelectionPrioritizesPoliceThenVictimAndKeepsAllCandidates()
    {
        object pending = Activator.CreateInstance(GetNestedType("JusticePendingRuntimeIncident"), true);
        IList witnesses = (IList)GetMemberValue(pending, "Witnesses");
        object civilian = CreateRuntimeWitness(JusticeEvidenceKind.CivilianWitness, 4000L);
        object victim = CreateRuntimeWitness(JusticeEvidenceKind.VictimWitness, 4000L);
        object police = CreateRuntimeWitness(JusticeEvidenceKind.PoliceWitness, 1000L);
        witnesses.Add(civilian);
        witnesses.Add(victim);
        witnesses.Add(police);

        object selected = InvokeStatic("SelectBestJusticeRuntimeWitness", pending);

        Assert.AreSame(police, selected, "Un policier observateur doit confirmer immédiatement.");
        Assert.AreEqual(3, witnesses.Count, "Le choix du meilleur témoin ne doit pas jeter les témoins de secours.");

        witnesses.Remove(police);
        selected = InvokeStatic("SelectBestJusticeRuntimeWitness", pending);
        Assert.AreSame(victim, selected, "La victime survivante est prioritaire sur un témoin civil ordinaire.");
    }

    [TestMethod]
    public void RuntimeJustice_PoliceVictimDeadlineIsComputedBeforeWitnessRegistration()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        string method = ExtractMethodBody(source, "BuildJusticeEvidence");

        AssertOrdered(
            method,
            "bool policeVictim",
            "long reportDueAtMs",
            "evidence.ReportDueAtMs = reportDueAtMs",
            "AddJusticeRuntimeWitness(pending, victimPed, evidence.Kind, reportDueAtMs)");
        Assert.IsTrue(
            Regex.IsMatch(
                method,
                @"policeVictim\s*\?\s*_justiceMonotonicTimeMs\s*:\s*_justiceMonotonicTimeMs\s*\+\s*JusticePolicy\.CivilianReportDelayMs"),
            "Une victime policière isolée doit être confirmable immédiatement, jamais après le délai civil de trois secondes.");
    }

    [TestMethod]
    public void RuntimeJustice_SuspensionSynchronizesLatchesBeforeAnyWantedOrCaptureMutation()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        string method = ExtractMethodBody(source, "UpdateJusticeEarly");
        int gate = method.IndexOf("if (IsJusticeRuntimeSuspended(player))", StringComparison.Ordinal);
        Assert.IsTrue(gate >= 0, "Le tick précoce doit posséder son propre coupe-circuit de suspension.");
        string afterGate = method.Substring(gate);
        AssertOrdered(
            afterGate,
            "if (IsJusticeRuntimeSuspended(player))",
            "_justiceWasBeingArrested = arrested",
            "_justiceWasDead = dead",
            "_justiceLastWantedLevel = wantedLevel",
            "return;",
            "bool policePursuitDeath",
            "UpdateJusticeWantedEdges(wantedLevel)");
        Assert.IsTrue(
            afterGate.IndexOf("BeginJusticeCapture", StringComparison.Ordinal) >
            afterGate.IndexOf("return;", StringComparison.Ordinal),
            "Aucun transfert ne doit pouvoir précéder la sortie du gate de suspension.");
    }

    [TestMethod]
    public void RuntimeJustice_SuspensionProbeIsSharedCadencedAndCircuitProtected()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        string cache = ExtractMethodBody(source, "IsJusticeRuntimeSuspended");
        AssertOrdered(
            cache,
            "_justiceMonotonicTimeMs < _justiceNextSuspensionCheckAtMs",
            "return _justiceRuntimeSuspendedCached",
            "_justiceNextSuspensionCheckAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs",
            "ComputeJusticeRuntimeSuspended(player)");

        int[] circuits =
        {
            GetStaticFieldValue<int>("JusticeCircuitLoading"),
            GetStaticFieldValue<int>("JusticeCircuitMission"),
            GetStaticFieldValue<int>("JusticeCircuitCutscene"),
            GetStaticFieldValue<int>("JusticeCircuitPlayerSwitch"),
            GetStaticFieldValue<int>("JusticeCircuitArrestState"),
            GetStaticFieldValue<int>("JusticeCircuitLastArrest"),
            GetStaticFieldValue<int>("JusticeCircuitStolenVehicleReport"),
            GetStaticFieldValue<int>("JusticeCircuitHitPedTimer"),
            GetStaticFieldValue<int>("JusticeCircuitHitVehicleTimer")
        };
        Assert.AreEqual(circuits.Length, circuits.Distinct().Count());
        Assert.IsTrue(circuits.All(value => value > 0 && (value & (value - 1)) == 0));

        foreach (string wrapperName in new[] { "CallJusticeBooleanNativeWithCircuit", "TryCallJusticeIntegerNativeWithCircuit" })
        {
            string wrapper = ExtractMethodBody(source, wrapperName);
            AssertOrdered(
                wrapper,
                "bool circuitWasOpen",
                "_justiceMonotonicTimeMs < _justiceNativeCircuitRetryAtMs[circuitIndex]",
                "_justiceUnavailableNativeCircuits &= ~circuit",
                "Function.Call",
                "_justiceUnavailableNativeCircuits |= circuit",
                "LogWarning");
        }
    }

    [TestMethod]
    public void RuntimeJustice_AllyTokensRequireLivePursuitUnsuspendedCurrentEpisodeAndArePurgedOnLoss()
    {
        string source = ReadRuntimeSource();
        string record = ExecutableMethodBody(source, "RecordJusticeAllyPoliceEngagement");
        AssertOrdered(
            record,
            "_justiceEnabled",
            "_justicePursuitActive",
            "IsJusticeRuntimeSuspended",
            "CurrentJusticeEpisodeId()",
            "_justiceAllyTokens.Add");

        string validation = ExecutableMethodBody(source, "IsJusticeAllyTokenValid");
        StringAssert.Contains(validation, "_justicePursuitActive");
        StringAssert.Contains(validation, "!IsJusticeRuntimeSuspended(player)");
        StringAssert.Contains(
            validation,
            "string.Equals(token.EpisodeId, CurrentJusticeEpisodeId(), StringComparison.Ordinal)");

        string wantedEdges = ExecutableMethodBody(source, "UpdateJusticeWantedEdges");
        int pursuitLost = wantedEdges.IndexOf(
            "else if (_justiceLastWantedLevel > 0",
            StringComparison.Ordinal);
        Assert.IsTrue(pursuitLost >= 0, "La branche de fin de poursuite doit rester explicite.");
        string lossBranch = wantedEdges.Substring(pursuitLost);
        AssertOrdered(
            lossBranch,
            "_justicePursuitActive = false",
            "_justiceAllyTokens.Clear()",
            "_justiceCaseState.HasWarrant = true");
    }

    [TestMethod]
    public void RuntimeJustice_DeadDonJAllyKeepsOnlyItsFreshSnapshottedAttribution()
    {
        Assert.IsTrue((bool)InvokeStatic(
            "HasJusticeValidAllyOwnership",
            false,
            true,
            true));
        Assert.IsFalse((bool)InvokeStatic(
            "HasJusticeValidAllyOwnership",
            false,
            false,
            true));
        Assert.IsFalse((bool)InvokeStatic(
            "HasJusticeValidAllyOwnership",
            false,
            true,
            false));

        string source = ReadRuntimeSource();
        string validation = ExecutableMethodBody(source, "IsJusticeAllyTokenValid");
        StringAssert.Contains(validation, "token.WasDonJOwnedAtCreation");
        StringAssert.Contains(validation, "GetJusticeEntityGeneration(token.Ally) == token.AllyGeneration");
        StringAssert.Contains(validation, "JusticeAllyAttributionRadius");
        Assert.IsFalse(
            validation.IndexOf("!token.Ally.IsDead", StringComparison.Ordinal) >= 0,
            "La mort mutuelle ne doit pas invalider le jeton avant la preuve de l'homicide.");

        string processing = ExecutableMethodBody(source, "ProcessJusticeAllyAttributionTokens");
        AssertOrdered(
            processing,
            "IsJusticeAllyTokenValid(token, player)",
            "IsJusticeDeathAttributedTo(",
            "token.AllyGeneration",
            "JusticeCrimeKind.AccessoryMurderOfficer",
            "token.WasDonJOwnedAtCreation");

        string queue = ExecutableMethodBody(source, "QueueJusticeIncident");
        StringAssert.Contains(queue, "HasJusticeValidAllyOwnership(");
        StringAssert.Contains(queue, "currentAllyGeneration != allyGeneration");
        StringAssert.Contains(queue, "AllyGeneration = allyGeneration");
    }

    [TestMethod]
    public void RuntimeJustice_IncidentIdsDistinguishReusedAllyGenerations()
    {
        object script = CreateJusticeHeadlessScript();
        SetFieldValue(script, "_justiceSessionId", "identity-session");

        string first = (string)InvokeInstance(
            script,
            "BuildJusticeIncidentId",
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "identity-episode",
            900,
            17,
            610,
            21);
        string recycled = (string)InvokeInstance(
            script,
            "BuildJusticeIncidentId",
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "identity-episode",
            900,
            17,
            610,
            22);

        Assert.AreNotEqual(first, recycled);
        StringAssert.EndsWith(first, ":610:21");
        StringAssert.EndsWith(recycled, ":610:22");
    }

    [TestMethod]
    public void RuntimeJustice_WitnessOrientationAndLineOfSightFailClosed()
    {
        string source = ReadRuntimeSource();
        string visibility = ExecutableMethodBody(source, "CanPedSeeJusticeEvent");
        AssertOrdered(
            visibility,
            "CanJusticePedSeeEntitySafe(witness, actor)",
            "HasJusticeEntityInFront(witness, actor)",
            "CanJusticePedSeeEntitySafe(witness, eventEntity)",
            "HasJusticeEntityInFront(witness, eventEntity)");

        MethodInfo safeLineOfSight = FindMethod("CanJusticePedSeeEntitySafe", PrivateInstance);
        Assert.IsTrue(
            safeLineOfSight.GetMethodBody().ExceptionHandlingClauses.Count > 0,
            "Une exception de la LOS partagée doit être absorbée dans le pont Justice.");
        string lineOfSight = ExecutableMethodBody(source, "CanJusticePedSeeEntitySafe");
        Assert.IsTrue(
            Regex.IsMatch(lineOfSight, @"catch\s*\([^)]*\)\s*\{.*?return false\s*;", RegexOptions.Singleline),
            "La LOS indisponible doit refuser le témoin, jamais le valider par défaut.");

        string orientation = ExecutableMethodBody(source, "HasJusticeEntityInFront");
        AssertOrdered(
            orientation,
            "CallJusticeBooleanNativeWithCircuit",
            "JusticeNativeHasEntityClearLosInFront",
            "JusticeCircuitLineOfSight",
            "false",
            "witness.Handle",
            "target.Handle");
    }

    [TestMethod]
    public void RuntimeJustice_EntityGenerationExpiresEvenWhenHandleAndModelAreReused()
    {
        Assert.AreEqual(30000, GetStaticFieldValue<int>("JusticeIdentityLifetimeMs"));
        string source = ReadRuntimeSource();
        string generation = ExecutableMethodBody(source, "GetJusticeEntityGeneration");

        AssertOrdered(
            generation,
            "_justiceTrackedIdentities.TryGetValue(handle, out tracked)",
            "CanReuseJusticeTrackedIdentity(tracked, entity, modelHash, memoryAddress)",
            "tracked.LastSeenAtMs = _justiceMonotonicTimeMs",
            "return tracked.Generation",
            "_justiceNextIdentityGeneration++",
            "_justiceTrackedIdentities[handle] = new JusticeTrackedIdentity",
            "MemoryAddress = memoryAddress");

        string reuse = ExecutableMethodBody(source, "CanReuseJusticeTrackedIdentity");
        AssertOrdered(
            reuse,
            "tracked.ModelHash != modelHash",
            "!Entity.Exists(tracked.Entity)",
            "tracked.MemoryAddress != 0L",
            "tracked.MemoryAddress != memoryAddress",
            "!ReferenceEquals(tracked.Entity, currentEntity)",
            "long age = _justiceMonotonicTimeMs - tracked.LastSeenAtMs",
            "age >= 0L && age <= JusticeIdentityLifetimeMs");

        string memoryAddress = ExecutableMethodBody(source, "GetJusticeEntityMemoryAddressSafe");
        StringAssert.Contains(memoryAddress, "entity.MemoryAddress");
        StringAssert.Contains(memoryAddress, "return 0L");
    }

    [TestMethod]
    public void RuntimeJustice_OrdinaryCrimesAndWarrantRecognitionNeverWriteWanted()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
        state.Enabled = true;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justiceInitialized", true);
        int wantedWrites = 0;
        SetFieldValue(
            script,
            "_justiceWantedWriteOverride",
            new Func<int, bool>(level =>
            {
                wantedWrites++;
                return true;
            }));

        JusticeCharge charge = JusticePolicy.ApplyConfirmedIncident(
            state,
            CreateConfirmedDirectIncident(
                JusticeCrimeKind.MurderCivilian,
                "incident:wanted-authority",
                "episode:wanted-authority",
                JusticeCircumstances.None),
            record);
        Assert.IsNotNull(charge);
        InvokeInstance(script, "OnJusticeChargeConfirmed", charge);

        Assert.AreEqual(0, wantedWrites);
        Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
        Assert.IsFalse(state.HasWarrant);
        Assert.IsFalse(GetFieldValue<bool>(script, "_justicePursuitActive"));

        List<MethodBase> chargeCalls = ReadCalledMethods(
            FindMethod("OnJusticeChargeConfirmed", PrivateInstance));
        List<MethodBase> recognitionCalls = ReadCalledMethods(
            FindMethod("UpdateJusticeWarrantRecognition", PrivateInstance));
        List<MethodBase> escapeCalls = ReadCalledMethods(
            FindMethod("RetryJusticeEscapeWantedMinimum", PrivateInstance));
        Assert.IsFalse(chargeCalls.Any(call => call.Name == "SetJusticeWantedMinimum"));
        Assert.IsFalse(recognitionCalls.Any(call => call.Name == "SetJusticeWantedMinimum"));
        Assert.IsTrue(
            escapeCalls.Any(call => call.Name == "SetJusticeWantedMinimum"),
            "Seule l'évasion explicite conserve son minimum de trois étoiles.");
    }

    [TestMethod]
    public void RuntimeJustice_CustomPedAtStartupCannotAdoptPersistedProfileWithoutCanonicalProof()
    {
        object script = CreateJusticeHeadlessScript();
        SetFieldValue(script, "_justiceActivePlayerProfileSlot", 0);
        SetFieldValue(script, "_justiceProfileSelectionPending", true);
        SetFieldValue(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => -1));

        Assert.IsFalse((bool)InvokeInstance(
            script,
            "IsJusticeRuntimeProfileContextCompatible"));
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "EnsureJusticeProfileMatchesCanonicalPlayer",
            new object[] { null }));

        // Je conserve en revanche une transformation faite après identification
        // du héros : seul le verrou de démarrage distingue ces deux situations.
        SetFieldValue(script, "_justiceProfileSelectionPending", false);
        Assert.IsTrue((bool)InvokeInstance(
            script,
            "IsJusticeRuntimeProfileContextCompatible"));
    }

    [TestMethod]
    public void RuntimeJustice_EscapeWantedWalRetriesKnownFailureThenNeverReappliesAfterSuccess()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateJusticeHeadlessScript();
            InvokeInstance(script, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] profiles =
                GetFieldValue<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
            state.Enabled = true;
            JusticeCharge charge = JusticePolicy.ApplyConfirmedIncident(
                state,
                CreateConfirmedDirectIncident(
                    JusticeCrimeKind.SimpleAssault,
                    "incident:escape-wanted-wal",
                    "episode:escape-wanted-wal",
                    JusticeCircumstances.InCustody),
                record);
            Assert.IsNotNull(charge);
            state.HasWarrant = true;
            state.Phase = JusticePhase.Fugitive;
            state.EscapeWantedMinimumPending = true;
            state.EscapeWantedMinimumAttempted = false;
            profiles[0].CaseState = state;
            profiles[0].RecordState = record;
            SetFieldValue(script, "_justiceEnabled", true);
            SetFieldValue(script, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(script, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(script, "_justiceProfileSelectionPending", false);

            int writes = 0;
            SetFieldValue(
                script,
                "_justiceWantedWriteOverride",
                new Func<int, bool>(level => ++writes >= 2));

            InvokeInstance(script, "RetryJusticeEscapeWantedMinimum", 0);
            Assert.AreEqual(0, writes);
            Assert.IsTrue(state.EscapeWantedMinimumPending);
            Assert.IsTrue(state.EscapeWantedMinimumAttempted);

            AwaitQueuedPersistence(script);
            InvokeInstance(script, "RetryJusticeEscapeWantedMinimum", 0);
            Assert.AreEqual(1, writes);
            Assert.IsTrue(state.EscapeWantedMinimumPending);
            Assert.IsFalse(state.EscapeWantedMinimumAttempted);

            AwaitQueuedPersistence(script);
            InvokeInstance(script, "RetryJusticeEscapeWantedMinimum", 0);
            Assert.AreEqual(1, writes);
            Assert.IsTrue(state.EscapeWantedMinimumPending);
            Assert.IsTrue(state.EscapeWantedMinimumAttempted);

            AwaitQueuedPersistence(script);
            InvokeInstance(script, "RetryJusticeEscapeWantedMinimum", 0);
            Assert.AreEqual(2, writes);
            Assert.IsFalse(state.EscapeWantedMinimumPending);
            Assert.IsFalse(state.EscapeWantedMinimumAttempted);
            AwaitQueuedPersistence(script);

            // Une descente ultérieure des étoiles reste entièrement à GTA.
            InvokeInstance(script, "RetryJusticeEscapeWantedMinimum", 0);
            Assert.AreEqual(2, writes);
        });
    }

    [TestMethod]
    public void RuntimeJustice_AmnestyWantedClearIsPersistedAtMostOnceAcrossAckRetryAndReload()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateJusticeHeadlessScript();
            InvokeInstance(script, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] profiles =
                GetFieldValue<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
            state.Enabled = true;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
                state,
                CreateConfirmedDirectIncident(
                    JusticeCrimeKind.SimpleAssault,
                    "incident:amnesty-clear-wal",
                    "episode:amnesty-clear-wal",
                    JusticeCircumstances.None),
                record));
            profiles[0].CaseState = state;
            profiles[0].RecordState = record;
            SetFieldValue(script, "_justiceEnabled", true);
            SetFieldValue(script, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(script, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(script, "_justiceProfileSelectionPending", false);
            SetFieldValue(script, "_justiceAmnestyPending", true);

            int clears = 0;
            SetFieldValue(
                script,
                "_justiceWantedClearObservationOverride",
                new Func<int?>(() => { clears++; return 0; }));

            Assert.IsFalse(
                (bool)InvokeInstance(script, "TryApplyJusticeAmnestyWantedClear"),
                "Le premier passage doit seulement enfiler le snapshot critique.");
            AwaitQueuedPersistence(script);
            Assert.IsTrue((bool)InvokeInstance(script, "TryApplyJusticeAmnestyWantedClear"));
            Assert.AreEqual(1, clears);
            Assert.IsTrue(GetFieldValue<bool>(script, "_justiceAmnestyWantedClearAttempted"));
            Assert.IsTrue((bool)InvokeInstance(script, "TryApplyJusticeAmnestyWantedClear"));
            Assert.AreEqual(1, clears, "Un échec d'acquittement ne doit jamais rejouer le clear.");
            FlushAndAwait(script);

            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(
                reader,
                "TryReadJusticeStateFile",
                Path.Combine(directory, "_justice_state.xml")));
            int replayedClears = 0;
            SetFieldValue(
                reader,
                "_justiceWantedClearObservationOverride",
                new Func<int?>(() => { replayedClears++; return 3; }));
            Assert.IsTrue((bool)InvokeInstance(reader, "TryApplyJusticeAmnestyWantedClear"));
            Assert.AreEqual(0, replayedClears);
        });
    }

    [TestMethod]
    public void RuntimeJustice_TransactionalReturnPersistsWantedLossAndPursuitDeathFronts()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object warrantScript = CreateJusticeHeadlessScript();
            InvokeInstance(warrantScript, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] warrantProfiles =
                GetFieldValue<JusticePlayerProfileState[]>(warrantScript, "_justicePlayerProfiles");
            JusticeCaseState warrantCase =
                GetFieldValue<JusticeCaseState>(warrantScript, "_justiceCaseState");
            JusticeRecordState warrantRecord =
                GetFieldValue<JusticeRecordState>(warrantScript, "_justiceRecordState");
            warrantCase.Enabled = true;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
                warrantCase,
                CreateConfirmedDirectIncident(
                    JusticeCrimeKind.Carjacking,
                    "incident:transaction-wanted-loss",
                    "episode:transaction-fronts",
                    JusticeCircumstances.None),
                warrantRecord));
            warrantCase.Phase = JusticePhase.Wanted;
            warrantProfiles[0].CaseState = warrantCase;
            warrantProfiles[0].RecordState = warrantRecord;
            SetFieldValue(warrantScript, "_justiceEnabled", true);
            SetFieldValue(warrantScript, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(warrantScript, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(warrantScript, "_justiceProfileSelectionPending", false);
            SetFieldValue(warrantScript, "_justicePursuitActive", true);
            SetFieldValue(warrantScript, "_justiceLastWantedLevel", 1);

            Assert.IsFalse((bool)InvokeInstance(
                warrantScript,
                "ObserveJusticeCriticalFrontsBeforeTransactionReturn",
                null,
                0,
                false,
                false,
                false,
                true));
            Assert.IsTrue(warrantCase.HasWarrant);
            Assert.AreEqual(JusticePhase.AtLarge, warrantCase.Phase);

            object deathScript = CreateJusticeHeadlessScript();
            InvokeInstance(deathScript, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] deathProfiles =
                GetFieldValue<JusticePlayerProfileState[]>(deathScript, "_justicePlayerProfiles");
            JusticeCaseState deathCase =
                GetFieldValue<JusticeCaseState>(deathScript, "_justiceCaseState");
            JusticeRecordState deathRecord =
                GetFieldValue<JusticeRecordState>(deathScript, "_justiceRecordState");
            deathCase.Enabled = true;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
                deathCase,
                CreateConfirmedDirectIncident(
                    JusticeCrimeKind.MurderCivilian,
                    "incident:transaction-death",
                    "episode:transaction-death",
                    JusticeCircumstances.None),
                deathRecord));
            deathCase.Phase = JusticePhase.Wanted;
            deathProfiles[0].CaseState = deathCase;
            deathProfiles[0].RecordState = deathRecord;
            SetFieldValue(deathScript, "_justiceEnabled", true);
            SetFieldValue(deathScript, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(deathScript, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(deathScript, "_justiceProfileSelectionPending", false);
            SetFieldValue(deathScript, "_justicePursuitActive", true);
            SetFieldValue(deathScript, "_justiceLastWantedLevel", 2);

            InvokeInstance(
                deathScript,
                "ObserveJusticeCriticalFrontsBeforeTransactionReturn",
                null,
                0,
                true,
                false,
                false,
                true);
            Assert.IsTrue(GetFieldValue<bool>(
                deathScript,
                "_justicePursuitDeathObservedDuringSuspension"));
            Assert.AreEqual(
                0,
                GetFieldValue<int>(deathScript, "_justiceSuspendedPursuitDeathPlayerSlot"));
        });
    }

    [TestMethod]
    public void RuntimeJustice_DisabledProfileCanReloadPendingLegalReleaseRecovery()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = CreateJusticeHeadlessScript();
            InvokeInstance(writer, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] profiles =
                GetFieldValue<JusticePlayerProfileState[]>(writer, "_justicePlayerProfiles");
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(writer, "_justiceRecordState");
            state.Enabled = false;
            state.Phase = JusticePhase.AtLarge;
            profiles[0].CaseState = state;
            profiles[0].RecordState = record;
            SetFieldValue(writer, "_justiceEnabled", false);
            SetFieldValue(writer, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(writer, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(writer, "_justiceProfileSelectionPending", false);
            SetFieldValue(writer, "_justiceLegalReleaseFinalizationPending", true);
            FieldInfo releaseSite = ScriptType.GetField(
                "_justiceLegalReleaseFinalizationSite",
                PrivateInstance);
            Assert.IsNotNull(releaseSite);
            releaseSite.SetValue(writer, Enum.Parse(releaseSite.FieldType, "None"));
            SetFieldValue(writer, "_justiceLegalReleaseSelectedWeaponHash", 0);
            FlushAndAwait(writer);

            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(
                reader,
                "TryReadJusticeStateFile",
                Path.Combine(directory, "_justice_state.xml")));
            Assert.IsFalse(GetFieldValue<bool>(reader, "_justiceEnabled"));
            Assert.IsTrue(GetFieldValue<bool>(
                reader,
                "_justiceLegalReleaseFinalizationPending"));
        });
    }

    [TestMethod]
    public void RuntimeJustice_TransferRollbackResumesBeforeAnyNewCustodyMutation()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(custodySource, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "JusticeOperationKind.TransferRollback",
            "ResumeJusticeCustodyTransferRollback",
            "_justiceFineDebitIntent",
            "CompleteJusticeCustodyTransfer");

        string rollback = ExecutableMethodBody(
            custodySource,
            "ResumeJusticeCustodyTransferRollback");
        AssertOrdered(
            rollback,
            "HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback)",
            "CompletedOperationIds.Remove(rollbackId)",
            "_justiceCaseState.Phase = JusticePhase.Transporting",
            "_justiceCustodyTransferPending = true",
            "_justiceCustodyResumePending = true",
            "JusticeMarkStateDirty()",
            "EnsureJusticeCustodyTransferRollbackPrecommitRedundant()",
            "_justiceCustodyTransferRollbackFinalizationPending = false");
        Assert.IsFalse(
            rollback.Contains("RestoreJusticeInventoryForLegalRelease"),
            "Un rollback hérité ne doit jamais restituer l'inventaire ni libérer le joueur.");
        Assert.IsFalse(
            rollback.Contains("_justiceCaseState.Phase = JusticePhase.AtLarge"),
            "Une peine héritée doit rester en transfert vers son lieu de détention.");

        string beginRollback = ExecutableMethodBody(
            custodySource,
            "TryRollbackJusticeCustodyTransfer");
        AssertOrdered(
            beginRollback,
            "JusticePolicy.TryRegisterOperation",
            "_justiceCustodyTransferRollbackPrecommitRedundant = false",
            "_justiceCustodyTransferRollbackFinalizationPending = true",
            "EnsureJusticeCustodyTransferRollbackPrecommitRedundant()");
        Assert.IsFalse(
            beginRollback.Contains("CompletedOperationIds.Remove"),
            "Le rollback durable ne doit pas oublier une intention potentiellement écrite au primaire.");
    }

    [TestMethod]
    public void RuntimeJustice_DeferredWantedLossCreatesOneWarrantBeforeDeathEvaluation()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        state.ActiveScore = 48;
        state.Phase = JusticePhase.Wanted;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justicePursuitActive", true);
        SetFieldValue(script, "_justiceWantedLossPending", true);

        InvokeInstance(script, "ResolveDeferredJusticeWantedLoss", 0);

        Assert.IsTrue(state.HasWarrant);
        Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
        Assert.IsFalse(GetFieldValue<bool>(script, "_justicePursuitActive"));
        Assert.IsFalse(GetFieldValue<bool>(script, "_justiceWantedLossPending"));

        string earlyTick = ExecutableMethodBody(ReadRuntimeSource(), "UpdateJusticeEarly");
        AssertOrdered(
            earlyTick,
            "bool policePursuitDeath",
            "TryResolveJusticeMaskedArrestOnWantedLoss",
            "ResolveDeferredJusticeWantedLoss(wantedLevel)");
    }

    [TestMethod]
    public void RuntimeJustice_PoliceCustodyMaterializesExactlyOneMinimalCaseBeforeCapture()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justiceSessionId", "custody-test");
        SetFieldValue(script, "_justiceMonotonicTimeMs", 5000L);

        Assert.IsTrue((bool)InvokeInstance(
            script,
            "EnsureJusticeCaseForPoliceCustody",
            true,
            "test de capture"));
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.EvadingPolice, state.Charges[0].Kind);
        Assert.AreEqual(120, state.SentenceSeconds);
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
        Assert.IsTrue(GetFieldValue<bool>(script, "_justicePursuitActive"));

        Assert.IsTrue((bool)InvokeInstance(
            script,
            "EnsureJusticeCaseForPoliceCustody",
            true,
            "second passage idempotent"));
        Assert.AreEqual(1, state.Charges.Count,
            "Un retry de la même capture ne doit jamais doubler la peine.");

        object fineOnlyScript = CreateJusticeHeadlessScript();
        JusticeCaseState fineOnlyState = GetFieldValue<JusticeCaseState>(
            fineOnlyScript,
            "_justiceCaseState");
        fineOnlyState.Enabled = true;
        fineOnlyState.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:fine-only",
            IncidentId = "incident:fine-only",
            EpisodeId = "wanted:fine-only",
            Kind = JusticeCrimeKind.RecklessDischarge,
            DisplayName = "Tir dangereux sans victime",
            Points = 6,
            Fine = 300L,
            SentenceSeconds = 0
        });
        fineOnlyState.RecalculateTotals();
        SetFieldValue(fineOnlyScript, "_justiceEnabled", true);
        SetFieldValue(fineOnlyScript, "_justiceSessionId", "custody-fine-only");
        SetFieldValue(fineOnlyScript, "_justiceMonotonicTimeMs", 6000L);

        Assert.IsTrue((bool)InvokeInstance(
            fineOnlyScript,
            "EnsureJusticeCaseForPoliceCustody",
            true,
            "capture avec dossier sans détention"));
        Assert.AreEqual(2, fineOnlyState.Charges.Count,
            "Un dossier limité à une amende doit recevoir la peine minimale de capture.");
        Assert.AreEqual(120, fineOnlyState.SentenceSeconds,
            "Une arrestation réelle ne doit jamais déboucher sur une libération immédiate.");
        Assert.AreEqual(
            1,
            fineOnlyState.Charges.Count(charge => charge.Kind == JusticeCrimeKind.EvadingPolice),
            "La peine minimale doit rester idempotente même avec un dossier préexistant.");

        object custodialScript = CreateJusticeHeadlessScript();
        JusticeCaseState custodialState = GetFieldValue<JusticeCaseState>(
            custodialScript,
            "_justiceCaseState");
        custodialState.Enabled = true;
        custodialState.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:custodial",
            IncidentId = "incident:custodial",
            EpisodeId = "wanted:custodial",
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression simple",
            Points = 18,
            Fine = 1000L,
            SentenceSeconds = 90
        });
        custodialState.RecalculateTotals();
        SetFieldValue(custodialScript, "_justiceEnabled", true);
        SetFieldValue(custodialScript, "_justiceSessionId", "custody-existing");
        SetFieldValue(custodialScript, "_justiceMonotonicTimeMs", 7000L);

        Assert.IsTrue((bool)InvokeInstance(
            custodialScript,
            "EnsureJusticeCaseForPoliceCustody",
            true,
            "capture avec peine existante"));
        Assert.AreEqual(1, custodialState.Charges.Count,
            "Une peine de détention existante ne doit recevoir aucune charge artificielle.");
        Assert.AreEqual(90, custodialState.SentenceSeconds);

        string runtime = ReadRuntimeSource();
        string earlyTick = ExecutableMethodBody(runtime, "UpdateJusticeEarly");
        AssertOrdered(
            earlyTick,
            "bool liveArrestEvidence",
            "bool policeCustodyEvidence",
            "bool livePoliceCustodyFront",
            "EnsureJusticeCaseForPoliceCustody(",
            "bool policePursuitDeath",
            "TryResolveJusticeMaskedArrestOnWantedLoss");
        Assert.IsTrue(
            Regex.IsMatch(
                earlyTick,
                @"HasJusticePoliceCustodyEvidence\(wantedLevel,\s*player,\s*dead\)\s*\|\|\s*liveArrestEvidence",
                RegexOptions.CultureInvariant),
            "La preuve de capture policière doit accepter la mise en forme C# sans perdre l'opérateur OU.");
        StringAssert.Contains(
            ExecutableMethodBody(runtime, "HasJusticePoliceCustodyEvidence"),
            "WasJusticePlayerKilledByPoliceSafe(player)");
    }

    [TestMethod]
    public void RuntimeJustice_RawPoliceDeathLatchCanPersistBeforeItsFallbackCharge()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.AtLarge
        };

        Assert.IsTrue((bool)InvokeStatic(
            "IsJusticeProfilePendingDeathValid",
            state,
            true,
            -1,
            0),
            "Le front de mort doit survivre au redémarrage avant la matérialisation de sa charge minimale.");

        state.Enabled = false;
        Assert.IsFalse((bool)InvokeStatic(
            "IsJusticeProfilePendingDeathValid",
            state,
            true,
            -1,
            0),
            "Un profil Justice désactivé ne doit jamais adopter un front de mort brut.");
    }

    [TestMethod]
    public void RuntimeJustice_MaskedArrestUsesRecentTimerBeforeCreatingAWarrant()
    {
        Assert.AreEqual(12000, GetStaticFieldValue<int>("JusticeMaskedArrestProbeMaximumMs"));
        string source = ReadRuntimeSource();
        string recovery = ExecutableMethodBody(
            source,
            "TryResolveJusticeMaskedArrestOnWantedLoss");

        AssertOrdered(
            recovery,
            "_justiceLastWantedLevel > 0 || _justiceWantedLossPending",
            "_justiceArrestCompletionProbePending = true",
            "_justiceWantedLossPending = true",
            "TryGetJusticeArrestConfirmedSafe(",
            "if (completionStateValid && completedArrest)",
            "BeginJusticeCapture(false)",
            "JusticeMaskedArrestProbeMaximumMs",
            "_justiceArrestCompletionProbePending = false",
            "return false;");
        Assert.IsFalse(
            recovery.Contains("ResistingArrest"),
            "Une panne de l'état BUSTED ne doit jamais inventer une résistance.");
    }

    [TestMethod]
    public void RuntimeJustice_AmnestyRetriesAndVerifiesWantedClearAfterDisable()
    {
        string source = ReadRuntimeSource();
        string amnesty = ExecutableMethodBody(source, "ExecuteJusticeAmnestyAndDisable");
        string resume = ExecutableMethodBody(source, "ResumeJusticeAmnestyTransaction");
        string applyClear = ExecutableMethodBody(source, "TryApplyJusticeAmnestyWantedClear");
        string clear = ExecutableMethodBody(source, "ClearJusticeWantedLevelOnceDetailed");
        string retry = ExecutableMethodBody(source, "RetryJusticeWantedClearAfterAmnesty");
        string early = ExecutableMethodBody(source, "UpdateJusticeEarly");

        AssertOrdered(
            amnesty,
            "_justiceAmnestyPending = true",
            "_justiceAmnestyPrecommitRedundant = false",
            "EnsureJusticeAmnestyPrecommitRedundant()",
            "CancelJusticeAmnestyConfirmation()",
            "ResumeJusticeAmnestyTransaction()");
        AssertOrdered(
            resume,
            "EnsureJusticeAmnestyPrecommitRedundant()",
            "EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction()",
            "ClearPendingJusticeDeathCapture()",
            "JusticeAmnestyCustody()",
            "_justiceCaseState.ClearActiveCase(false)",
            "_justiceEnabled = false",
            "JusticeMarkStateDirty()",
            "JusticeFlushStateNow()",
            "TryApplyJusticeAmnestyWantedClear()",
            "_justiceAmnestyPending = false",
            "JusticeFlushStateNow()");
        AssertOrdered(
            applyClear,
            "_justiceAmnestyWantedClearAttempted = true",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "ClearJusticeWantedLevelOnceDetailed()",
            "JusticeWantedClearResult.Rejected",
            "aucun retry tardif");
        AssertOrdered(
            clear,
            "JusticeNativeClearPlayerWantedLevel",
            "TryReadJusticeWantedLevel(out observed)",
            "Game.Player.WantedLevel = 0",
            "finalReadSucceeded = TryReadJusticeWantedLevel(out observed)",
            "JusticeWantedClearResult.Rejected",
            "_justiceWantedClearPending = false");
        AssertOrdered(
            retry,
            "_justiceWantedClearPending",
            "_justiceWantedClearRetryUntilMs",
            "ClearJusticeWantedLevelOnce()",
            "_justiceNextWantedClearRetryAtMs");
        int suspensionGate = early.IndexOf("if (IsJusticeRuntimeSuspended(player))", StringComparison.Ordinal);
        int retryAt = early.IndexOf("RetryJusticeWantedClearAfterAmnesty()", StringComparison.Ordinal);
        int refreshedWanted = early.IndexOf("wantedLevel = GetJusticeWantedLevelSafe()", retryAt, StringComparison.Ordinal);
        Assert.IsTrue(
            suspensionGate >= 0 && retryAt > suspensionGate && refreshedWanted > retryAt,
            "Le retry d'amnistie doit rester après le gate de suspension puis rafraîchir le snapshot wanted.");
        StringAssert.Contains(retry, "_justiceEnabled || HasActiveJusticeCase() || JusticeIsCustodyActive");
        StringAssert.Contains(retry, "CancelJusticeWantedClearRetry()");
        StringAssert.Contains(
            ExecutableMethodBody(source, "RequestJusticeToggle"),
            "CancelJusticeWantedClearRetry();",
            "Réactiver Justice doit invalider le jeton d'amnistie précédent.");
    }

    [TestMethod]
    public void RuntimeJustice_ConfirmedChargeUsesTheNativeStatusBannerOnly()
    {
        string confirmed = ExecutableMethodBody(ReadRuntimeSource(), "OnJusticeChargeConfirmed");
        AssertOrdered(
            confirmed,
            "string notificationDetail =",
            "ShowStatus(");
        StringAssert.Contains(confirmed, "Justice · ");
        string source = ReadRuntimeSource();
        Assert.IsFalse(source.Contains("DrawJusticeCompactHud"));
        Assert.IsFalse(source.Contains("JusticeShouldShowCompactHud"));
        Assert.IsFalse(source.Contains("_justiceHudNoticeUntilMs"));
    }

    [TestMethod]
    public void JusticeDomain_MultiVictimUsesOnlyConfirmedDistinctIdentitiesFromTheSameBatch()
    {
        JusticeCaseState state = new JusticeCaseState { Enabled = true };
        JusticeRecordState record = new JusticeRecordState();
        const string batchId = "batch:confirmed-only";
        const string episodeId = "episode:multi";

        JusticeIncident first = NewConfirmedBatchIncident(
            "multi-one", episodeId, batchId, 101, 1);
        JusticeCharge firstCharge = JusticePolicy.ApplyConfirmedIncident(state, first, record);
        Assert.IsNotNull(firstCharge);
        Assert.AreEqual(0, firstCharge.AdditionalVictimCount);

        JusticeIncident unreported = NewUnconfirmedIncident("multi-unreported", 4200L, true);
        unreported.EpisodeId = episodeId;
        unreported.DetectionBatchId = batchId;
        unreported.Kind = JusticeCrimeKind.SimpleAssault;
        unreported.VictimHandle = 202;
        unreported.VictimGeneration = 1;
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, unreported, record));
        Assert.AreEqual(0, firstCharge.AdditionalVictimCount,
            "Un témoin qui n'a pas encore signalé l'acte ne doit jamais aggraver une charge confirmée.");

        JusticeIncident recycledHandle = NewConfirmedBatchIncident(
            "multi-generation-two", episodeId, batchId, 101, 2);
        JusticeCharge secondCharge = JusticePolicy.ApplyConfirmedIncident(state, recycledHandle, record);
        Assert.IsNotNull(secondCharge);
        Assert.AreEqual(1, firstCharge.AdditionalVictimCount);
        Assert.AreEqual(1, secondCharge.AdditionalVictimCount);

        JusticeCharge thirdCharge = JusticePolicy.ApplyConfirmedIncident(
            state,
            NewConfirmedBatchIncident("multi-three", episodeId, batchId, 303, 1),
            record);
        JusticeCharge fourthCharge = JusticePolicy.ApplyConfirmedIncident(
            state,
            NewConfirmedBatchIncident("multi-four", episodeId, batchId, 404, 1),
            record);
        JusticeCharge fifthCharge = JusticePolicy.ApplyConfirmedIncident(
            state,
            NewConfirmedBatchIncident("multi-five", episodeId, batchId, 505, 1),
            record);

        Assert.IsNotNull(thirdCharge);
        Assert.IsNotNull(fourthCharge);
        Assert.IsNotNull(fifthCharge);
        foreach (JusticeCharge charge in state.Charges)
        {
            Assert.AreEqual(3, charge.AdditionalVictimCount,
                "L'aggravant multi-victimes doit rester borné à trois victimes supplémentaires.");
            Assert.IsTrue((charge.Circumstances & JusticeCircumstances.MultipleVictims) != 0);
        }
    }

    [TestMethod]
    public void JusticeDomain_RecklessDischargeIsSupersededOnlyByTheSameExplicitCausalEvent()
    {
        JusticeRecordState record = new JusticeRecordState();

        JusticeCaseState unrelatedState = new JusticeCaseState { Enabled = true };
        JusticeIncident unrelatedShot = NewConfirmedBatchIncident(
            "shot-unrelated", "episode:shot", string.Empty, 0, 0);
        unrelatedShot.Kind = JusticeCrimeKind.RecklessDischarge;
        unrelatedShot.CausalEventId = "discharge:one";
        JusticeIncident unrelatedAssault = NewConfirmedBatchIncident(
            "assault-unrelated", "episode:shot", string.Empty, 700, 1);
        unrelatedAssault.CausalEventId = "discharge:two";

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(unrelatedState, unrelatedShot, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(unrelatedState, unrelatedAssault, record));
        Assert.AreEqual(2, unrelatedState.Charges.Count,
            "Une simple proximité temporelle ne doit jamais effacer le tir confirmé.");

        JusticeCaseState relatedState = new JusticeCaseState { Enabled = true };
        JusticeIncident relatedShot = NewConfirmedBatchIncident(
            "shot-related", "episode:related", string.Empty, 0, 0);
        relatedShot.Kind = JusticeCrimeKind.RecklessDischarge;
        relatedShot.CausalEventId = "discharge:shared";
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(relatedState, relatedShot, record));

        JusticeIncident stillProvisional = NewUnconfirmedIncident(
            "assault-provisional", 4200L, true);
        stillProvisional.EpisodeId = "episode:related";
        stillProvisional.Kind = JusticeCrimeKind.SimpleAssault;
        stillProvisional.VictimHandle = 701;
        stillProvisional.VictimGeneration = 1;
        stillProvisional.CausalEventId = "discharge:shared";
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(relatedState, stillProvisional, record));
        Assert.AreEqual(JusticeCrimeKind.RecklessDischarge, relatedState.Charges[0].Kind);

        JusticeIncident provenAssault = NewConfirmedBatchIncident(
            "assault-related", "episode:related", string.Empty, 701, 1);
        provenAssault.CausalEventId = "discharge:shared";
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(relatedState, provenAssault, record));
        Assert.AreEqual(1, relatedState.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.SimpleAssault, relatedState.Charges[0].Kind);
    }

    [TestMethod]
    public void JusticePersistence_VersionTwoRoundTripsAndLegacyV1RemainsReadable()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writerScript = CreateJusticeHeadlessScript();
            JusticeCaseState caseState = GetFieldValue<JusticeCaseState>(writerScript, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(writerScript, "_justiceRecordState");
            PopulatePersistedJusticeState(caseState, record);
            SetFieldValue(writerScript, "_justiceActivePlayerProfileSlot", 1);
            SetFieldValue(writerScript, "_justiceEnabled", true);
            SetFieldValue(writerScript, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
            SetFieldValue(writerScript, "_justiceInventoryRemoved", true);
            SetEnumField(writerScript, "_justiceInventoryCustodyState", "RemovedVerified");
            SetFieldValue(writerScript, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetFieldValue(writerScript, "_justiceCustodyPlayerStateStored", true);
            SetFieldValue(writerScript, "_justiceCustodyStoredInvincible", false);
            SetFieldValue(writerScript, "_justiceCustodyStoredFrozen", true);
            SetFieldValue(writerScript, "_justiceCustodyStoredCanRagdoll", false);
            SetFieldValue(writerScript, "_justiceCustodyWaitingForRespawn", true);
            SetFieldValue(writerScript, "_justiceCustodyDeathRebindPending", true);
            SetFieldValue(writerScript, "_justiceCustodyPlayerSlot", 1);
            SetFieldValue(writerScript, "_justiceCustodyInitialSentenceSeconds", 720);
            SetFieldValue(writerScript, "_justiceActivityReductionGrantedSeconds", 90);
            SetEnumField(writerScript, "_justiceCustodySite", "Bolingbroke");

            object fineIntent = Activator.CreateInstance(GetNestedType("JusticeFineDebitIntent"), true);
            SetMemberValue(fineIntent, "EpisodeId", "custody:one:fine:release:incident:one");
            SetMemberValue(fineIntent, "Slot", 1);
            SetMemberValue(fineIntent, "FineAmount", 4321L);
            SetMemberValue(fineIntent, "CashPlanPrepared", true);
            SetMemberValue(
                fineIntent,
                "PreparedAtUtcTicks",
                new DateTime(2026, 8, 25, 20, 0, 55, DateTimeKind.Utc).Ticks);
            SetMemberValue(fineIntent, "DebitAmount", 2000);
            SetMemberValue(fineIntent, "CashBefore", 2000);
            SetMemberValue(fineIntent, "CashAfter", 0);
            SetMemberValue(fineIntent, "SentenceIfDebited", 780);
            SetMemberValue(fineIntent, "SentenceIfConverted", 810);
            SetMemberValue(fineIntent, "StationPlanned", false);
            SetMemberValue(fineIntent, "DebitAttempted", true);
            SetMemberValue(
                fineIntent,
                "AttemptedAtUtcTicks",
                new DateTime(2026, 8, 25, 20, 1, 0, DateTimeKind.Utc).Ticks);
            SetFieldValue(writerScript, "_justiceFineDebitIntent", fineIntent);

            FlushAndAwait(writerScript);
            string path = Path.Combine(directory, "_justice_state.xml");
            Assert.IsTrue(File.Exists(path));

            XDocument xml = XDocument.Load(path);
            Assert.AreEqual("JusticeState", xml.Root.Name.LocalName);
            Assert.AreEqual("2", (string)xml.Root.Attribute("schemaMajor"));
            Assert.AreEqual("0", (string)xml.Root.Attribute("schemaMinor"));
            Assert.IsFalse(string.IsNullOrWhiteSpace((string)xml.Root.Attribute("payloadSha256")));
            XElement persistedProfile = GetPersistedActiveJusticeProfile(xml);
            Assert.IsNotNull(persistedProfile.Element("Case"));
            Assert.IsNotNull(persistedProfile.Element("Record"));
            Assert.IsNotNull(persistedProfile.Element("Custody"));
            Assert.IsNull(xml.Root.Element("Npcs"), "L'état Justice doit rester séparé des scènes XML.");
            Assert.AreEqual(
                "true",
                (string)persistedProfile.Element("Case").Element("Charges").Element("Charge").Attribute("adjudicated"));
            XElement[] persistedAllies = persistedProfile.Element("Case")
                .Element("Charges")
                .Element("Charge")
                .Element("AlliedContributors")
                .Elements("Ally")
                .ToArray();
            Assert.AreEqual(2, persistedAllies.Length);
            CollectionAssert.AreEquivalent(
                new[] { "41", "42" },
                persistedAllies.Select(element => (string)element.Attribute("generation")).ToArray());

            object readerScript = CreateJusticeHeadlessScript();
            Assert.IsTrue(
                (bool)InvokeInstance(readerScript, "TryReadJusticeStateFile", path),
                "Le fichier Justice multi-profils doit etre rechargeable.");

            JusticeCaseState loadedCase = GetFieldValue<JusticeCaseState>(readerScript, "_justiceCaseState");
            JusticeRecordState loadedRecord = GetFieldValue<JusticeRecordState>(readerScript, "_justiceRecordState");
            Assert.AreEqual(87, loadedCase.ActiveScore);
            Assert.AreEqual(4321L, loadedCase.FineDue);
            Assert.AreEqual(720, loadedCase.SentenceSeconds);
            Assert.AreEqual("pursuit:one", loadedCase.WantedEpisodeId);
            object loadedFineIntent = GetFieldValue<object>(readerScript, "_justiceFineDebitIntent");
            Assert.IsNotNull(loadedFineIntent);
            Assert.AreEqual(
                "custody:one:fine:release:incident:one",
                GetMemberValue(loadedFineIntent, "EpisodeId"));
            Assert.AreEqual(2000, GetMemberValue(loadedFineIntent, "DebitAmount"));
            Assert.IsTrue((bool)GetMemberValue(loadedFineIntent, "CashPlanPrepared"));
            Assert.AreNotEqual(0L, GetMemberValue(loadedFineIntent, "PreparedAtUtcTicks"));
            Assert.IsTrue((bool)GetMemberValue(loadedFineIntent, "DebitAttempted"));
            Assert.AreNotEqual(0L, GetMemberValue(loadedFineIntent, "AttemptedAtUtcTicks"));
            CollectionAssert.Contains(
                loadedCase.CompletedOperationIds,
                JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, "custody:one"));
            Assert.AreEqual(
                2 + JusticePolicy.MaxRememberedOperations,
                loadedCase.CompletedOperationIds.Count,
                "Les opérations irréversibles restent, seules les écritures wanted transitoires sont bornées.");
            CollectionAssert.Contains(loadedCase.FleeingChargedEpisodeIds, "pursuit:one");
            CollectionAssert.Contains(loadedCase.EscapeChargedEpisodeIds, "custody:one");
            Assert.AreEqual(1, loadedCase.Charges.Count);
            Assert.AreEqual(2, loadedCase.Charges[0].AdditionalVictimCount);
            Assert.IsTrue(loadedCase.Charges[0].IsAdjudicated);
            CollectionAssert.Contains(loadedCase.Charges[0].AlliedContributorHandles, 701);
            Assert.AreEqual(2, loadedCase.Charges[0].AlliedContributors.Count);
            Assert.IsTrue(loadedCase.Charges[0].HasAlliedContributor(701, 41));
            Assert.IsTrue(loadedCase.Charges[0].HasAlliedContributor(701, 42));
            Assert.AreEqual(
                JusticeCircumstances.OrganizedBand,
                loadedCase.Charges[0].Circumstances);

            Assert.AreEqual(28, loadedRecord.RecidivismIndex);
            CollectionAssert.Contains(loadedRecord.AppliedConvictionIds, "conviction:custody:one");
            Assert.AreEqual(1, loadedRecord.Convictions.Count);
            Assert.AreEqual(1, loadedRecord.Convictions[0].Charges.Count);
            Assert.AreEqual(JusticeCrimeKind.MurderOfficer, loadedRecord.Convictions[0].Charges[0].Kind);
            Assert.AreEqual(
                JusticeCircumstances.Armed | JusticeCircumstances.VehicleUsedAsWeapon,
                loadedRecord.Convictions[0].Charges[0].Circumstances);
            Assert.IsTrue(loadedRecord.Convictions[0].Charges[0].CircumstancesWerePersisted);

            object loadedSnapshot = GetFieldValue<object>(readerScript, "_justiceWeaponSnapshot");
            Assert.IsNotNull(loadedSnapshot);
            Assert.IsTrue((bool)InvokeStatic("ValidateJusticeWeaponSnapshot", loadedSnapshot));
            Assert.IsTrue(GetFieldValue<bool>(readerScript, "_justiceInventoryRemoved"));
            Assert.AreEqual("Bolingbroke", GetFieldValue<object>(readerScript, "_justiceCustodySite").ToString());
            Assert.IsTrue(GetFieldValue<bool>(readerScript, "_justiceCustodyWaitingForRespawn"));
            Assert.IsTrue(GetFieldValue<bool>(readerScript, "_justiceCustodyDeathRebindPending"));
            Assert.AreEqual(1, GetFieldValue<int>(readerScript, "_justiceCustodyPlayerSlot"));
            Assert.IsTrue(GetFieldValue<bool>(readerScript, "_justiceCustodyPlayerStateStored"));
            Assert.IsFalse(GetFieldValue<bool>(readerScript, "_justiceCustodyStoredInvincible"));
            Assert.IsTrue(GetFieldValue<bool>(readerScript, "_justiceCustodyStoredFrozen"));
            Assert.IsFalse(GetFieldValue<bool>(readerScript, "_justiceCustodyStoredCanRagdoll"));

            XDocument canonicalLegacyV1 = ConvertJusticeV2ToLegacyV1(xml);
            canonicalLegacyV1.Root.Element("PlayerProfiles")?.Remove();
            canonicalLegacyV1.Root.Attribute("activePlayerSlot")?.Remove();
            string canonicalStateXml = canonicalLegacyV1.ToString(SaveOptions.DisableFormatting);
            XDocument emptyConvictionIdXml = XDocument.Parse(canonicalStateXml);
            emptyConvictionIdXml.Root
                .Element("Record")
                .Element("Convictions")
                .Element("Conviction")
                .SetAttributeValue("id", "conviction:");
            emptyConvictionIdXml.Root
                .Element("Record")
                .Element("AppliedConvictions")
                .Element("ConvictionId")
                .SetAttributeValue("id", "conviction:");
            emptyConvictionIdXml.Save(path);
            object invalidConvictionReader = CreateJusticeHeadlessScript();
            Assert.IsFalse((bool)InvokeInstance(invalidConvictionReader, "TryReadJusticeStateFile", path));
            canonicalLegacyV1.Save(path);

            File.Copy(path, path + ".bak", true);
            XDocument invalidAmmoXml = XDocument.Load(path);
            invalidAmmoXml.Root
                .Element("Custody")
                .Element("InventorySnapshot")
                .Element("Weapon")
                .SetAttributeValue("ammo", "illisible");
            invalidAmmoXml.Save(path);
            object ammoFallbackReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(ammoFallbackReader, "TryLoadJusticeState", false));
            object restoredSnapshot = GetFieldValue<object>(
                ammoFallbackReader,
                "_justiceWeaponSnapshot");
            object restoredWeapon = ((IList)GetMemberValue(restoredSnapshot, "Weapons"))[0];
            Assert.AreEqual(50, GetMemberValue(restoredWeapon, "Ammo"));
            File.Copy(path + ".bak", path, true);

            XDocument foreignIntentXml = XDocument.Load(path);
            foreignIntentXml.Root
                .Element("Custody")
                .Element("FineDebitIntent")
                .SetAttributeValue("episodeId", "custody:foreign:fine:pending");
            foreignIntentXml.Save(path);
            object fallbackReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(fallbackReader, "TryLoadJusticeState", false));
            object fallbackIntent = GetFieldValue<object>(fallbackReader, "_justiceFineDebitIntent");
            Assert.IsNotNull(fallbackIntent);
            Assert.AreEqual(
                "custody:one:fine:release:incident:one",
                GetMemberValue(fallbackIntent, "EpisodeId"));

            XDocument unpreparedIntentXml = new XDocument(canonicalLegacyV1);
            XElement unpreparedIntent = unpreparedIntentXml.Root
                .Element("Custody")
                .Element("FineDebitIntent");
            unpreparedIntent.SetAttributeValue("cashPlanPrepared", "false");
            unpreparedIntent.SetAttributeValue(
                "preparedAtUtcTicks",
                new DateTime(2026, 8, 25, 20, 2, 0, DateTimeKind.Utc).Ticks);
            unpreparedIntent.SetAttributeValue("debitAmount", "0");
            unpreparedIntent.SetAttributeValue("cashBefore", "0");
            unpreparedIntent.SetAttributeValue("cashAfter", "0");
            unpreparedIntent.SetAttributeValue("sentenceIfDebited", "810");
            unpreparedIntent.SetAttributeValue("debitAttempted", "false");
            unpreparedIntent.SetAttributeValue("attemptedAtUtcTicks", "0");
            string unpreparedPath = Path.Combine(directory, "_justice_state_unprepared_fine.xml");
            unpreparedIntentXml.Save(unpreparedPath);
            object unpreparedReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(
                unpreparedReader,
                "TryReadJusticeStateFile",
                unpreparedPath));
            object loadedUnpreparedIntent = GetFieldValue<object>(
                unpreparedReader,
                "_justiceFineDebitIntent");
            Assert.IsFalse((bool)GetMemberValue(loadedUnpreparedIntent, "CashPlanPrepared"));
            Assert.AreNotEqual(
                0L,
                GetMemberValue(loadedUnpreparedIntent, "PreparedAtUtcTicks"));

            XDocument legacyXml = new XDocument(canonicalLegacyV1);
            foreach (XElement ally in legacyXml.Descendants("Ally"))
            {
                ally.Attribute("generation")?.Remove();
            }
            XElement legacyFineIntent = legacyXml.Root.Element("Custody").Element("FineDebitIntent");
            legacyFineIntent.Attribute("cashPlanPrepared")?.Remove();
            legacyFineIntent.Attribute("preparedAtUtcTicks")?.Remove();
            legacyFineIntent.Attribute("debitAttempted")?.Remove();
            legacyFineIntent.Attribute("attemptedAtUtcTicks")?.Remove();
            XElement legacyCustody = legacyXml.Root.Element("Custody");
            legacyCustody.Attribute("playerStateStored")?.Remove();
            legacyCustody.Attribute("storedInvincible")?.Remove();
            legacyCustody.Attribute("storedFrozen")?.Remove();
            legacyCustody.Attribute("storedCanRagdoll")?.Remove();
            legacyXml.Root.Element("PlayerProfiles")?.Remove();
            legacyXml.Root.Attribute("activePlayerSlot")?.Remove();
            string legacyV1Path = Path.Combine(directory, "_justice_state_legacy_v1.xml");
            legacyXml.Save(legacyV1Path);
            object legacyReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(legacyReader, "TryReadJusticeStateFile", legacyV1Path));
            JusticeCharge legacyCharge = GetFieldValue<JusticeCaseState>(legacyReader, "_justiceCaseState").Charges[0];
            Assert.AreEqual(1, legacyCharge.AlliedContributors.Count);
            Assert.IsTrue(legacyCharge.HasAlliedContributor(701, 0));
            object legacyFineLoaded = GetFieldValue<object>(legacyReader, "_justiceFineDebitIntent");
            Assert.IsTrue((bool)GetMemberValue(legacyFineLoaded, "CashPlanPrepared"));
            Assert.AreEqual(0L, GetMemberValue(legacyFineLoaded, "PreparedAtUtcTicks"));
            Assert.IsTrue((bool)GetMemberValue(legacyFineLoaded, "DebitAttempted"));
            Assert.AreEqual(0L, GetMemberValue(legacyFineLoaded, "AttemptedAtUtcTicks"));
        });
    }

    [TestMethod]
    public void JusticePersistence_CollectiveMergeKeepsCanonicalIdentityAndFullSnapshotRoundTrips()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = CreateJusticeHeadlessScript();
            InvokeInstance(writer, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] profiles =
                GetFieldValue<JusticePlayerProfileState[]>(writer, "_justicePlayerProfiles");
            JusticeCaseState caseState = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(writer, "_justiceRecordState");
            caseState.Enabled = true;

            const string episodeId = "wanted:collective-roundtrip";
            JusticeIncident first = CreateConfirmedDirectIncident(
                JusticeCrimeKind.AccessoryAssaultOfficer,
                "incident:collective:first",
                episodeId,
                JusticeCircumstances.None);
            first.VictimHandle = 200;
            first.VictimGeneration = 2;
            first.IsAlliedAction = true;
            first.AllyHandle = 501;
            first.AllyGeneration = 11;

            JusticeIncident second = CreateConfirmedDirectIncident(
                JusticeCrimeKind.AccessoryAssaultOfficer,
                "incident:collective:second",
                episodeId,
                JusticeCircumstances.None);
            second.VictimHandle = first.VictimHandle;
            second.VictimGeneration = first.VictimGeneration;
            second.IsAlliedAction = true;
            second.AllyHandle = 502;
            second.AllyGeneration = 12;

            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(caseState, first, record));
            JusticeCharge merged = JusticePolicy.ApplyConfirmedIncident(caseState, second, record);
            Assert.IsNotNull(merged);
            Assert.AreEqual(1, caseState.Charges.Count);
            Assert.AreEqual(second.IncidentId, merged.IncidentId);
            Assert.AreEqual("charge:" + second.IncidentId, merged.ChargeId);
            caseState.Phase = JusticePhase.Wanted;

            // Je publie le dossier fusionné par le vrai repository afin de couvrir
            // le DTO figé, le codec v2, les SHA-256 et la relecture des profils.
            profiles[0].CaseState = caseState;
            profiles[0].RecordState = record;
            SetFieldValue(writer, "_justiceEnabled", true);
            SetFieldValue(writer, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(writer, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(writer, "_justiceProfileSelectionPending", false);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            Assert.IsTrue(File.Exists(path));

            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(reader, "TryReadJusticeStateFile", path));
            JusticeCaseState reloaded =
                GetFieldValue<JusticeCaseState>(reader, "_justiceCaseState");
            Assert.AreEqual(caseState.ActiveScore, reloaded.ActiveScore);
            Assert.AreEqual(1, reloaded.Charges.Count);
            Assert.AreEqual(second.IncidentId, reloaded.Charges[0].IncidentId);
            Assert.AreEqual("charge:" + second.IncidentId, reloaded.Charges[0].ChargeId);
        });
    }

    [TestMethod]
    public void JusticePersistence_ConsolidatesActiveChargesAndRoundTripsEveryRepresentedFact()
    {
        WithTemporarySaveDirectory(directory =>
        {
            const int confirmedFacts = 700;
            object writer = CreateJusticeHeadlessScript();
            JusticeCaseState writerCase = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            JusticeRecordState writerRecord = GetFieldValue<JusticeRecordState>(writer, "_justiceRecordState");
            writerCase.Enabled = true;
            writerCase.Phase = JusticePhase.Captured;
            writerCase.WantedEpisodeId = "pursuit:charge-cap";
            writerCase.CustodyEpisodeId = "custody:charge-cap";
            SetFieldValue(writer, "_justiceEnabled", true);
            SetFieldValue(writer, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetFieldValue(writer, "_justiceCustodyPlayerSlot", 0);

            for (int index = 0; index < confirmedFacts; index++)
            {
                JusticeIncident incident = CreateConfirmedDirectIncident(
                    JusticeCrimeKind.VehicleDestruction,
                    "persisted-cap-" + index,
                    "persisted-cap-episode-" + index,
                    JusticeCircumstances.None);
                Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(writerCase, incident, writerRecord));
            }

            int scoreBeforeSave = writerCase.ActiveScore;
            long fineBeforeSave = writerCase.FineDue;
            int sentenceBeforeSave = writerCase.SentenceSeconds;
            JusticeConviction conviction = JusticePolicy.ApplyConviction(
                writerCase,
                writerRecord,
                new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
            Assert.IsNotNull(conviction);
            writerCase.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.ApplyConviction,
                writerCase.CustodyEpisodeId));

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            XDocument xml = XDocument.Load(path);
            XElement[] persistedCharges = GetPersistedActiveJusticeProfile(xml)
                .Element("Case")
                .Element("Charges")
                .Elements("Charge")
                .ToArray();

            Assert.AreEqual(JusticePolicy.MaxActiveCharges, persistedCharges.Length);
            Assert.AreEqual(
                confirmedFacts,
                persistedCharges.Sum(element =>
                    string.Equals((string)element.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase)
                        ? (int)element.Attribute("aggregatedChargeCount")
                        : 1));
            Assert.AreEqual(
                "true",
                (string)persistedCharges.Single(element =>
                    string.Equals((string)element.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
                    .Attribute("adjudicated"));

            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(reader, "TryReadJusticeStateFile", path));
            JusticeCaseState loadedCase = GetFieldValue<JusticeCaseState>(reader, "_justiceCaseState");
            JusticeRecordState loadedRecord = GetFieldValue<JusticeRecordState>(reader, "_justiceRecordState");

            Assert.AreEqual(JusticePolicy.MaxActiveCharges, loadedCase.Charges.Count);
            Assert.AreEqual(
                confirmedFacts,
                JusticePolicy.GetRepresentedChargeCount(loadedCase));
            Assert.AreEqual(
                confirmedFacts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                InvokeInstance(reader, "GetJusticeChargesDisplay"),
                "L'UI doit afficher les faits représentés et non les seules entrées persistées.");
            Assert.AreEqual(scoreBeforeSave, loadedCase.ActiveScore);
            Assert.AreEqual(fineBeforeSave, loadedCase.FineDue);
            Assert.AreEqual(sentenceBeforeSave, loadedCase.SentenceSeconds);
            Assert.AreEqual(1, loadedRecord.Convictions.Count);
            JusticeConvictionChargeSummary aggregateSummary = loadedRecord.Convictions[0]
                .Charges
                .Single(summary => summary.IsAggregate);
            Assert.AreEqual(
                confirmedFacts - (JusticePolicy.MaxActiveCharges - 1),
                aggregateSummary.AggregatedChargeCount);
        });
    }

    [TestMethod]
    public void JusticePersistence_MaximumThreeProfileLedgerFitsTheBoundedStateFile()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = CreateJusticeHeadlessScript();
            InvokeInstance(writer, "EnsureJusticePlayerProfilesInitialized");
            JusticePlayerProfileState[] profiles =
                GetFieldValue<JusticePlayerProfileState[]>(writer, "_justicePlayerProfiles");

            for (int slot = 0; slot < profiles.Length; slot++)
            {
                profiles[slot].CaseState.Enabled = false;
                profiles[slot].RecordState = BuildMaximumJusticeRecord(slot);
            }

            SetFieldValue(writer, "_justiceActivePlayerProfileSlot", 0);
            SetFieldValue(writer, "_justiceLastCanonicalPlayerSlot", 0);
            SetFieldValue(writer, "_justiceCaseState", profiles[0].CaseState);
            SetFieldValue(writer, "_justiceRecordState", profiles[0].RecordState);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            long fileLength = new FileInfo(path).Length;
            long maximum = GetStaticFieldValue<long>("JusticeStateMaximumFileBytes");

            Assert.AreEqual(16L * 1024L * 1024L, maximum);
            Assert.IsTrue(
                fileLength > 4L * 1024L * 1024L,
                "Le scénario doit réellement dépasser l'ancienne limite de 4 Mio.");
            Assert.IsTrue(fileLength <= maximum, "Le pire registre borné doit rester sérialisable.");

            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(reader, "TryReadJusticeStateFile", path));
            JusticePlayerProfileState[] loaded =
                GetFieldValue<JusticePlayerProfileState[]>(reader, "_justicePlayerProfiles");
            Assert.AreEqual(3, loaded.Length);
            for (int slot = 0; slot < loaded.Length; slot++)
            {
                Assert.AreEqual(JusticePolicy.MaxConvictions, loaded[slot].RecordState.Convictions.Count);
                Assert.AreEqual(
                    JusticePolicy.MaxActiveCharges,
                    loaded[slot].RecordState.Convictions[0].Charges.Count);
            }
        });
    }

    [TestMethod]
    public void ScenePersistence_ReservesEveryInternalNameAndCannotResolveJusticeStateAsAScene()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateJusticeHeadlessScript();
            string justicePath = Path.Combine(directory, "_justice_state.xml");
            File.WriteAllText(justicePath, "justice-sentinel");

            foreach (Tuple<string, string> sample in new[]
                     {
                         Tuple.Create("_justice_state.xml", "scene_justice_state.xml"),
                         Tuple.Create("_last_save.txt", "scene_last_save.txt.xml"),
                         Tuple.Create("_ma_scene", "scene_ma_scene.xml")
                     })
            {
                Assert.AreEqual(sample.Item2, InvokeStatic("NormalizeSaveFileName", sample.Item1));

                string savePath = (string)InvokeInstance(script, "GetSavePath", sample.Item1);
                Assert.AreEqual(Path.Combine(directory, sample.Item2), savePath);
                Assert.AreNotEqual(justicePath, savePath);

                object[] resolveArguments = { sample.Item1, null, null };
                bool found = (bool)FindMethod("TryResolveSavePathForLoad", PrivateInstance)
                    .Invoke(script, resolveArguments);
                Assert.IsFalse(found);
                Assert.AreEqual(Path.Combine(directory, sample.Item2), resolveArguments[1]);
                Assert.AreNotEqual(justicePath, resolveArguments[1]);
            }

            Assert.AreEqual(
                "justice-sentinel",
                File.ReadAllText(justicePath),
                "La résolution directe d'une scène ne doit jamais toucher le fichier Justice réservé.");

            string coreSource = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.cs"));
            AssertOrdered(
                ExecutableMethodBody(coreSource, "SaveCurrentSetup"),
                "NormalizeSaveFileName(fileName)",
                "Path.Combine(saveDirectory, normalizedFileName)");
            AssertOrdered(
                ExecutableMethodBody(coreSource, "LoadSetup"),
                "NormalizeSaveFileName(fileName)",
                "TryResolveSavePathForLoad(normalizedFileName");
        });
    }

    [TestMethod]
    public void CustodyDisciplineIntent_ReloadBetweenBeginAndCompleteProducesExactlyOneCharge()
    {
        WithTemporarySaveDirectory(directory =>
        {
            string custodySource = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Custody.cs"));
            AssertOrdered(
                ExecutableMethodBody(custodySource, "BeginJusticeCustodyDiscipline"),
                "_justiceDisciplineIntent = new JusticeDisciplineIntent",
                "JusticeMarkStateDirty()",
                "if (!JusticeFlushStateNow())",
                "_justiceDisciplineActive = true");
            AssertOrdered(
                ExecutableMethodBody(custodySource, "UpdateJusticeCustodyDiscipline"),
                "_justiceDisciplineIntent != null && !_justiceDisciplineActive",
                "_justiceDisciplineActive = true",
                "_justiceDisciplineEndsAt = now",
                "CompleteJusticeCustodyDiscipline(player, now)");

            const string incidentId =
                "discipline:custody:discipline-reload:0123456789abcdef0123456789abcdef";
            object writer = CreateJusticeHeadlessScript();
            JusticeCaseState writerState = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            writerState.Enabled = true;
            writerState.Phase = JusticePhase.Incarcerated;
            writerState.WantedEpisodeId = "pursuit:discipline-reload";
            writerState.CustodyEpisodeId = "custody:discipline-reload";
            writerState.ActiveScore = 18;
            writerState.SentenceSeconds = 300;
            writerState.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:discipline-base",
                IncidentId = "incident:discipline-base",
                EpisodeId = "pursuit:discipline-reload",
                Kind = JusticeCrimeKind.SimpleAssault,
                DisplayName = "Agression simple",
                Points = 18,
                Fine = 0L,
                SentenceSeconds = 300,
                IsAdjudicated = true
            });
            writerState.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.ApplyConviction,
                writerState.CustodyEpisodeId));
            JusticeRecordState writerRecord = GetFieldValue<JusticeRecordState>(writer, "_justiceRecordState");
            writerRecord.AppliedConvictionIds.Add("conviction:custody:discipline-reload");
            JusticeConviction baseConviction = new JusticeConviction
            {
                ConvictionId = "conviction:custody:discipline-reload",
                JudgedAtUtc = new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc),
                Severity = JusticeSeverity.Misdemeanor,
                Score = 18,
                Fine = 0L,
                SentenceSeconds = 300
            };
            baseConviction.Charges.Add(new JusticeConvictionChargeSummary
            {
                Kind = JusticeCrimeKind.SimpleAssault,
                DisplayName = "Agression simple",
                Points = 18,
                Fine = 0L,
                SentenceSeconds = 300
            });
            writerRecord.Convictions.Add(baseConviction);
            SetFieldValue(writer, "_justiceEnabled", true);
            SetFieldValue(writer, "_justiceCustodyRuntimeActive", true);
            SetFieldValue(writer, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetFieldValue(writer, "_justiceCustodyPlayerSlot", 0);
            SetFieldValue(writer, "_justiceCustodyInitialSentenceSeconds", 300);
            SetEnumField(writer, "_justiceCustodySite", "MissionRow");

            object intent = Activator.CreateInstance(GetNestedType("JusticeDisciplineIntent"), true);
            SetMemberValue(intent, "IncidentId", incidentId);
            SetMemberValue(intent, "CrimeKind", JusticeCrimeKind.AssaultOfficer);
            SetMemberValue(intent, "PenaltySeconds", 60);
            SetFieldValue(writer, "_justiceDisciplineIntent", intent);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            XElement persistedIntent = GetPersistedActiveJusticeProfile(XDocument.Load(path))
                .Element("Custody")
                .Element("DisciplineIntent");
            Assert.IsNotNull(persistedIntent);
            Assert.AreEqual(incidentId, (string)persistedIntent.Attribute("incidentId"));
            Assert.AreEqual("AssaultOfficer", (string)persistedIntent.Attribute("crimeKind"));
            Assert.AreEqual("60", (string)persistedIntent.Attribute("penaltySeconds"));

            string validPrecommitXml = File.ReadAllText(path);
            SetFieldValue(writer, "_justiceCustodyPlayerModelHash", 0);
            Assert.IsTrue(
                (bool)InvokeInstance(writer, "JusticeFlushStateNow"),
                "Le thread GTA doit accepter le DTO sans bloquer sur sa validation disque.");
            Assert.IsFalse(
                (bool)InvokeInstance(writer, "JusticeAwaitQueuedPersistenceForTests"),
                "Le writer doit rejeter le snapshot de détention invalide.");
            Assert.AreEqual(
                validPrecommitXml,
                File.ReadAllText(path),
                "Le primaire valide doit rester byte pour byte intact après un temp sémantiquement invalide.");
            SetFieldValue(writer, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetFieldValue(
                writer,
                "_justiceMonotonicTimeMs",
                GetFieldValue<long>(writer, "_justiceNextStateFlushAttemptAtMs"));
            FlushAndAwait(writer);

            File.Copy(path, path + ".bak", true);
            XDocument foreignIntent = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            foreignIntent.Root.Element("PlayerProfiles")?.Remove();
            foreignIntent.Root
                .Element("Custody")
                .Element("DisciplineIntent")
                .SetAttributeValue(
                    "incidentId",
                    "discipline:custody:foreign:0123456789abcdef0123456789abcdef");
            foreignIntent.Save(path);
            object fallbackReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(fallbackReader, "TryLoadJusticeState", false));
            Assert.AreEqual(
                incidentId,
                GetMemberValue(
                    GetFieldValue<object>(fallbackReader, "_justiceDisciplineIntent"),
                    "IncidentId"));
            File.Copy(path + ".bak", path, true);

            XDocument mixedWal = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            mixedWal.Root.Element("PlayerProfiles")?.Remove();
            mixedWal.Root
                .Element("Case")
                .Element("ProcessedIncidents")
                .Add(new XElement("Incident", incidentId));
            mixedWal.Save(path);
            object mixedWalReader = CreateJusticeHeadlessScript();
            Assert.IsTrue(
                (bool)InvokeInstance(mixedWalReader, "TryLoadJusticeState", false),
                "Un WAL disciplinaire partiel doit être rejeté au profit du backup précommit cohérent.");
            Assert.AreEqual(
                1,
                GetFieldValue<JusticeCaseState>(mixedWalReader, "_justiceCaseState").Charges.Count);
            Assert.IsNotNull(GetFieldValue<object>(mixedWalReader, "_justiceDisciplineIntent"));
            File.Copy(path + ".bak", path, true);

            object resumed = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(resumed, "TryReadJusticeStateFile", path));
            JusticeCaseState resumedState = GetFieldValue<JusticeCaseState>(resumed, "_justiceCaseState");
            JusticeRecordState resumedRecord = GetFieldValue<JusticeRecordState>(resumed, "_justiceRecordState");
            Assert.AreEqual(1, resumedState.Charges.Count, "Le précommit seul ne doit pas inventer une charge.");
            Assert.IsNotNull(GetFieldValue<object>(resumed, "_justiceDisciplineIntent"));

            InvokeInstance(resumed, "CompleteJusticeCustodyDiscipline", (object)null, 5000);
            AwaitQueuedPersistence(resumed);

            Assert.IsNull(GetFieldValue<object>(resumed, "_justiceDisciplineIntent"));
            Assert.AreEqual(2, resumedState.Charges.Count);
            Assert.AreEqual(incidentId, resumedState.Charges[1].IncidentId);
            Assert.AreEqual(2, resumedRecord.Convictions.Count);
            Assert.AreEqual(1, resumedRecord.Convictions[1].Charges.Count);

            object reloadedAfterCompletion = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(reloadedAfterCompletion, "TryReadJusticeStateFile", path));
            JusticeCaseState completedState = GetFieldValue<JusticeCaseState>(
                reloadedAfterCompletion,
                "_justiceCaseState");
            JusticeRecordState completedRecord = GetFieldValue<JusticeRecordState>(
                reloadedAfterCompletion,
                "_justiceRecordState");
            Assert.IsNull(GetFieldValue<object>(reloadedAfterCompletion, "_justiceDisciplineIntent"));
            Assert.AreEqual(2, completedState.Charges.Count, "La reprise ne doit ni perdre ni doubler la faute.");
            Assert.AreEqual(incidentId, completedState.Charges[1].IncidentId);
            Assert.AreEqual(2, completedRecord.Convictions.Count);

            XDocument legacyCommittedWal = ConvertJusticeV2ToLegacyV1(XDocument.Load(path));
            legacyCommittedWal.Root.Element("PlayerProfiles")?.Remove();
            legacyCommittedWal.Root.Attribute("activePlayerSlot")?.Remove();
            XElement legacySummary = legacyCommittedWal.Root
                .Element("Record")
                .Element("Convictions")
                .Elements("Conviction")
                .Last()
                .Element("ChargeSummaries")
                .Element("Charge");
            legacySummary.Attribute("circumstances")?.Remove();
            legacyCommittedWal.Root.Element("Custody").Add(
                new XElement(
                    "DisciplineIntent",
                    new XAttribute("incidentId", incidentId),
                    new XAttribute("crimeKind", JusticeCrimeKind.AssaultOfficer),
                    new XAttribute("penaltySeconds", 60)));
            legacyCommittedWal.Save(path);
            object legacyWalReader = CreateJusticeHeadlessScript();
            Assert.IsTrue(
                (bool)InvokeInstance(legacyWalReader, "TryReadJusticeStateFile", path),
                "Un WAL v1 commis sans attribut circumstances doit rester reprenable.");
            JusticeConvictionChargeSummary legacyLoadedSummary =
                GetFieldValue<JusticeRecordState>(legacyWalReader, "_justiceRecordState")
                    .Convictions.Last()
                    .Charges.Single();
            Assert.IsFalse(legacyLoadedSummary.CircumstancesWerePersisted);
            Assert.IsNotNull(GetFieldValue<object>(legacyWalReader, "_justiceDisciplineIntent"));

            XElement committedDisciplineCharge = legacyCommittedWal.Root
                .Element("Case")
                .Element("Charges")
                .Elements("Charge")
                .Single(element => (string)element.Attribute("incidentId") == incidentId);
            long disciplineFine = (long)committedDisciplineCharge.Attribute("fine");
            int disciplineSentence = (int)committedDisciplineCharge.Attribute("sentenceSeconds");

            XDocument erasedDisciplineFine = XDocument.Parse(legacyCommittedWal.ToString());
            erasedDisciplineFine.Root.Element("Case").SetAttributeValue(
                "fineDue",
                Math.Max(0L, disciplineFine - 1L));
            erasedDisciplineFine.Save(path);
            Assert.IsFalse(
                (bool)InvokeInstance(CreateJusticeHeadlessScript(), "TryReadJusticeStateFile", path),
                "Le WAL disciplinaire commis ne doit jamais perdre son amende avant le nettoyage de l'intent.");

            XDocument erasedDisciplineSentence = XDocument.Parse(legacyCommittedWal.ToString());
            erasedDisciplineSentence.Root.Element("Case").SetAttributeValue(
                "sentenceSeconds",
                Math.Max(0, disciplineSentence - 1));
            erasedDisciplineSentence.Save(path);
            Assert.IsFalse(
                (bool)InvokeInstance(CreateJusticeHeadlessScript(), "TryReadJusticeStateFile", path),
                "Le WAL disciplinaire commis ne doit jamais perdre sa peine avant le nettoyage de l'intent.");
        });
    }

    [TestMethod]
    public void DeathCapture_UnknownIdentityPersistsUntilAProtagonistCanBeProven()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object writer = CreateJusticeHeadlessScript();
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            state.Enabled = true;
            state.ActiveScore = 18;
            state.FineDue = 1000L;
            state.SentenceSeconds = 90;
            state.Phase = JusticePhase.Wanted;
            state.WantedEpisodeId = "pursuit:death-unknown";
            state.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:death-unknown",
                IncidentId = "incident:death-unknown",
                EpisodeId = state.WantedEpisodeId,
                Kind = JusticeCrimeKind.SimpleAssault,
                Points = 18,
                Fine = 1000L,
                SentenceSeconds = 90
            });
            SetFieldValue(writer, "_justiceEnabled", true);
            SetFieldValue(writer, "_justicePursuitDeathObservedDuringSuspension", true);
            SetFieldValue(writer, "_justiceSuspendedPursuitDeathPlayerSlot", -1);
            SetFieldValue(writer, "_justiceSuspendedPursuitDeathPlayerModelHash", 0);
            SetFieldValue(writer, "_justiceLastCanonicalPlayerSlot", -1);
            SetFieldValue(writer, "_justiceLastCanonicalPlayerModelHash", 0);

            FlushAndAwait(writer);

            string path = Path.Combine(directory, "_justice_state.xml");
            object reader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(reader, "TryReadJusticeStateFile", path));
            Assert.IsTrue(GetFieldValue<bool>(
                reader,
                "_justicePursuitDeathObservedDuringSuspension"));
            Assert.AreEqual(-1, GetFieldValue<int>(
                reader,
                "_justiceSuspendedPursuitDeathPlayerSlot"));
            Assert.AreEqual(0, GetFieldValue<int>(
                reader,
                "_justiceSuspendedPursuitDeathPlayerModelHash"));
            Assert.IsFalse(
                JusticePolicy.IsCanonicalPlayerIdentityCompatible(-1, -1, 0, -1, 0),
                "Un latch durable sans identité ne doit jamais autoriser jugement, débit ou confiscation.");
        });
    }

    [TestMethod]
    public void JusticePersistence_CorruptPrimaryFallsBackToBakAndUnknownVersionIsRejected()
    {
        WithTemporarySaveDirectory(directory =>
        {
            object script = CreateJusticeHeadlessScript();
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
            state.Enabled = true;
            state.ActiveScore = 11;
            state.FineDue = 500L;
            state.SentenceSeconds = 60;
            state.HasWarrant = true;
            state.Phase = JusticePhase.AtLarge;
            state.WantedEpisodeId = "backup:pursuit";
            state.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:backup",
                IncidentId = "incident:backup",
                EpisodeId = "backup:pursuit",
                Kind = JusticeCrimeKind.ArmedThreat,
                Points = 11,
                Fine = 500L,
                SentenceSeconds = 60
            });
            SetFieldValue(script, "_justiceEnabled", true);
            FlushAndAwait(script);

            state.ActiveScore = 22;
            state.Charges[0].Points = 22;
            FlushAndAwait(script);
            string primary = Path.Combine(directory, "_justice_state.xml");
            string backup = primary + ".bak";
            Assert.IsTrue(File.Exists(backup), "La seconde écriture atomique doit conserver le dernier état valide en .bak.");

            File.WriteAllText(primary, "<JusticeState version='1'><Case>");
            object backupReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(backupReader, "TryLoadJusticeState", false));
            Assert.AreEqual(11, GetFieldValue<JusticeCaseState>(backupReader, "_justiceCaseState").ActiveScore);
            object repairedPrimaryReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(
                repairedPrimaryReader,
                "TryReadJusticeStateFile",
                primary));
            Assert.AreEqual(
                11,
                GetFieldValue<JusticeCaseState>(repairedPrimaryReader, "_justiceCaseState").ActiveScore);

            string unknown = Path.Combine(directory, "unknown.xml");
            File.WriteAllText(unknown, "<JusticeState version='99' enabled='true'><Case/><Record/></JusticeState>");
            object versionReader = CreateJusticeHeadlessScript();
            JusticeCaseState sentinel = GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState");
            sentinel.ActiveScore = 44;
            Assert.IsFalse((bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", unknown));
            Assert.AreEqual(44, GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState").ActiveScore);

            string malformed = Path.Combine(directory, "malformed.xml");
            File.WriteAllText(malformed, "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///never'>]><JusticeState version='1'>&y;</JusticeState>");
            Assert.IsFalse((bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", malformed));
            Assert.AreEqual(44, GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState").ActiveScore);

            XDocument invalidPhaseDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            invalidPhaseDocument.Root.Element("PlayerProfiles")?.Remove();
            invalidPhaseDocument.Root.Element("Case").SetAttributeValue("phase", "999");
            string invalidPhase = Path.Combine(directory, "invalid-phase.xml");
            invalidPhaseDocument.Save(invalidPhase);
            Assert.IsFalse((bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", invalidPhase));
            Assert.AreEqual(44, GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState").ActiveScore);

            XDocument impossibleRecidivismDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            impossibleRecidivismDocument.Root.Element("PlayerProfiles")?.Remove();
            impossibleRecidivismDocument.Root
                .Element("Record")
                .SetAttributeValue("recidivism", "100");
            string impossibleRecidivism = Path.Combine(directory, "impossible-recidivism.xml");
            impossibleRecidivismDocument.Save(impossibleRecidivism);
            Assert.IsFalse(
                (bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", impossibleRecidivism),
                "Un indice R sans aucune condamnation capable de le produire doit être rejeté.");

            XDocument invalidOperationDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            invalidOperationDocument.Root.Element("PlayerProfiles")?.Remove();
            XElement operations = invalidOperationDocument.Root
                .Element("Case")
                .Element("CompletedOperations");
            operations.Add(new XElement("Operation", "ApplyFine|forged"));
            string invalidOperation = Path.Combine(directory, "invalid-operation.xml");
            invalidOperationDocument.Save(invalidOperation);
            Assert.IsFalse((bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", invalidOperation));
            Assert.AreEqual(44, GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState").ActiveScore);

            XDocument invalidFineDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            invalidFineDocument.Root.Element("PlayerProfiles")?.Remove();
            invalidFineDocument.Root.Element("Case").SetAttributeValue(
                "fineDue",
                (JusticePolicy.MaxActiveFine + 1L).ToString(CultureInfo.InvariantCulture));
            string invalidFine = Path.Combine(directory, "invalid-fine.xml");
            invalidFineDocument.Save(invalidFine);
            Assert.IsFalse((bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", invalidFine));
            Assert.AreEqual(44, GetFieldValue<JusticeCaseState>(versionReader, "_justiceCaseState").ActiveScore);

            XDocument erasedPendingFineDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            erasedPendingFineDocument.Root.Element("PlayerProfiles")?.Remove();
            erasedPendingFineDocument.Root.Element("Case").SetAttributeValue("fineDue", "0");
            string erasedPendingFine = Path.Combine(directory, "erased-pending-fine.xml");
            erasedPendingFineDocument.Save(erasedPendingFine);
            Assert.IsFalse(
                (bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", erasedPendingFine),
                "Une charge non jugée doit conserver au minimum toute son amende dérivable.");

            XDocument erasedPendingSentenceDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            erasedPendingSentenceDocument.Root.Element("PlayerProfiles")?.Remove();
            erasedPendingSentenceDocument.Root.Element("Case").SetAttributeValue("sentenceSeconds", "0");
            string erasedPendingSentence = Path.Combine(directory, "erased-pending-sentence.xml");
            erasedPendingSentenceDocument.Save(erasedPendingSentence);
            Assert.IsFalse(
                (bool)InvokeInstance(versionReader, "TryReadJusticeStateFile", erasedPendingSentence),
                "Une charge non jugée doit conserver au minimum toute sa peine dérivable.");

            XDocument mismatchedEnabledDocument = ConvertJusticeV2ToLegacyV1(XDocument.Load(backup));
            mismatchedEnabledDocument.Root.Element("PlayerProfiles")?.Remove();
            mismatchedEnabledDocument.Root.SetAttributeValue("enabled", "false");
            string mismatchedEnabled = Path.Combine(directory, "mismatched-enabled.xml");
            mismatchedEnabledDocument.Save(mismatchedEnabled);
            Assert.IsFalse((bool)InvokeInstance(
                versionReader,
                "TryReadJusticeStateFile",
                mismatchedEnabled));
        });
    }

    [TestMethod]
    public void JusticePersistence_CapturedConvictionCannotEraseBalancesBeforeFineCommit()
    {
        WithTemporarySaveDirectory(directory =>
        {
            const string custodyEpisode = "custody:captured-balance";
            object writer = CreateJusticeHeadlessScript();
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(writer, "_justiceCaseState");
            state.Enabled = true;
            state.ActiveScore = 18;
            state.FineDue = 1000L;
            state.SentenceSeconds = 90;
            state.Phase = JusticePhase.Captured;
            state.WantedEpisodeId = "pursuit:captured-balance";
            state.CustodyEpisodeId = custodyEpisode;
            state.LastCrimeKind = JusticeCrimeKind.SimpleAssault;
            state.LastCrimeLabel = "Agression simple";
            state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.Capture,
                custodyEpisode));
            state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.ApplyConviction,
                custodyEpisode));
            state.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:captured-balance",
                IncidentId = "incident:captured-balance",
                EpisodeId = state.WantedEpisodeId,
                Kind = JusticeCrimeKind.SimpleAssault,
                DisplayName = "Agression simple",
                Points = 18,
                Fine = 1000L,
                SentenceSeconds = 90,
                IsAdjudicated = true
            });

            JusticeRecordState record = GetFieldValue<JusticeRecordState>(writer, "_justiceRecordState");
            record.RecidivismIndex = 5;
            string convictionId = "conviction:" + custodyEpisode;
            record.AppliedConvictionIds.Add(convictionId);
            JusticeConviction conviction = new JusticeConviction
            {
                ConvictionId = convictionId,
                JudgedAtUtc = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc),
                Severity = JusticeSeverity.Misdemeanor,
                Score = 18,
                Fine = 1000L,
                SentenceSeconds = 90
            };
            conviction.Charges.Add(new JusticeConvictionChargeSummary
            {
                Kind = JusticeCrimeKind.SimpleAssault,
                DisplayName = "Agression simple",
                Points = 18,
                Fine = 1000L,
                SentenceSeconds = 90
            });
            record.Convictions.Add(conviction);
            SetFieldValue(writer, "_justiceEnabled", true);
            SetFieldValue(writer, "_justiceCustodyPlayerModelHash", 0x12345678);
            SetFieldValue(writer, "_justiceCustodyPlayerSlot", 0);

            FlushAndAwait(writer);
            string path = Path.Combine(directory, "_justice_state.xml");
            string canonical = File.ReadAllText(path);
            object validReader = CreateJusticeHeadlessScript();
            Assert.IsTrue((bool)InvokeInstance(validReader, "TryReadJusticeStateFile", path));

            XDocument erasedFine = ConvertJusticeV2ToLegacyV1(XDocument.Parse(canonical));
            erasedFine.Root.Element("PlayerProfiles")?.Remove();
            erasedFine.Root.Element("Case").SetAttributeValue("fineDue", "0");
            erasedFine.Save(path);
            Assert.IsFalse((bool)InvokeInstance(
                CreateJusticeHeadlessScript(),
                "TryReadJusticeStateFile",
                path));

            XDocument erasedSentence = ConvertJusticeV2ToLegacyV1(XDocument.Parse(canonical));
            erasedSentence.Root.Element("PlayerProfiles")?.Remove();
            erasedSentence.Root.Element("Case").SetAttributeValue("sentenceSeconds", "0");
            erasedSentence.Save(path);
            Assert.IsFalse((bool)InvokeInstance(
                CreateJusticeHeadlessScript(),
                "TryReadJusticeStateFile",
                path));
        });
    }

    [TestMethod]
    public void CustodyLayouts_ExposeExactSitesVolumesGuardsInmatesAndActivities()
    {
        object station = GetStaticFieldValue<object>("JusticeMissionRowLayout");
        object prison = GetStaticFieldValue<object>("JusticeBolingbrokeLayout");

        AssertCustodyLayout(
            station,
            "MissionRow",
            2,
            0,
            60,
            new[]
            {
                Tuple.Create("station_formalites", 20, 20),
                Tuple.Create("station_nettoyage", 40, 30)
            });
        AssertCustodyLayout(
            prison,
            "Bolingbroke",
            4,
            8,
            300,
            new[]
            {
                Tuple.Create("prison_tour", 60, 60),
                Tuple.Create("prison_exercice", 40, 45),
                Tuple.Create("prison_travail", 75, 90),
                Tuple.Create("prison_rassemblement", 30, 30)
            });

        Assert.AreEqual(6000, GetStaticFieldValue<int>("JusticeCustodyEscapeGraceMs"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("JusticeCustodyMaximumSentenceSeconds"));
    }

    [TestMethod]
    public void BolingbrokeEscapeVolume_FollowsTheEnclosureAndRequiresAClearExit()
    {
        object prison = GetStaticFieldValue<object>("JusticeBolingbrokeLayout");
        Array playableVolumes = (Array)GetMemberValue(prison, "AllowedVolumes");
        Array containmentVolumes = (Array)GetMemberValue(prison, "ContainmentVolumes");
        Vector3[] insidePlayablePerimeter =
        {
            new Vector3(1510.0f, 2550.0f, 45.0f),
            new Vector3(1805.0f, 2550.0f, 45.0f),
            new Vector3(1650.0f, 2410.0f, 45.0f),
            new Vector3(1650.0f, 2715.0f, 45.0f)
        };

        foreach (Vector3 position in insidePlayablePerimeter)
        {
            Assert.IsTrue(
                playableVolumes.Cast<object>().Any(
                    volume => (bool)InvokeObjectInstance(volume, "Contains", position)),
                "Le périmètre intérieur de Bolingbroke doit rester jouable.");
        }

        Vector3[] outsidePlayableCorners =
        {
            new Vector3(1505.0f, 2390.0f, 45.0f),
            new Vector3(1805.0f, 2390.0f, 45.0f),
            new Vector3(1505.0f, 2710.0f, 45.0f),
            new Vector3(1805.0f, 2710.0f, 45.0f)
        };
        foreach (Vector3 position in outsidePlayableCorners)
        {
            Assert.IsFalse(
                playableVolumes.Cast<object>().Any(
                    volume => (bool)InvokeObjectInstance(volume, "Contains", position)),
                "Un coin au-delà des murs ne doit pas devenir une zone d'activité artificielle.");
        }

        Vector3[] insideOuterEnclosure =
        {
            new Vector3(1490.0f, 2550.0f, 45.0f),
            new Vector3(1828.0f, 2550.0f, 45.0f),
            new Vector3(1650.0f, 2370.0f, 45.0f),
            new Vector3(1650.0f, 2740.0f, 45.0f)
        };
        foreach (Vector3 position in insideOuterEnclosure)
        {
            Assert.IsTrue(
                containmentVolumes.Cast<object>().Any(
                    volume => (bool)InvokeObjectInstance(volume, "Contains", position)),
                "Les murs, portes, tours et marges de streaming doivent rester dans l'enceinte de détention.");
        }

        Vector3[] clearOutsidePositions =
        {
            new Vector3(1450.0f, 2550.0f, 45.0f),
            new Vector3(1850.0f, 2550.0f, 45.0f),
            new Vector3(1650.0f, 2330.0f, 45.0f),
            new Vector3(1650.0f, 2770.0f, 45.0f)
        };
        foreach (Vector3 position in clearOutsidePositions)
        {
            Assert.IsFalse(
                containmentVolumes.Cast<object>().Any(
                    volume => (bool)InvokeObjectInstance(volume, "Contains", position)),
                "L'évasion ne doit être possible qu'après une sortie claire de l'enceinte extérieure.");
        }

        Vector3 release = (Vector3)GetMemberValue(prison, "ReleasePosition");
        Assert.IsFalse(
            containmentVolumes.Cast<object>().Any(
                volume => (bool)InvokeObjectInstance(volume, "Contains", release)),
            "Le point de libération légale doit rester hors de l'enceinte.");
    }

    [TestMethod]
    public void CustodyFineConversion_RoundsAndCapsWithoutChangingSiteClass()
    {
        AssertFineConversion(0, 1L, true, 30);
        AssertFineConversion(0, 1500L, true, 30);
        AssertFineConversion(0, 1501L, true, 45);
        AssertFineConversion(290, 1000000L, true, 300);
        AssertFineConversion(1790, 1000000L, false, 1800);

        Assert.AreEqual(45L, (long)InvokeStatic("RoundJusticeCustodySecondsUp", 31L, 15));
        Assert.AreEqual(1800, (int)InvokeStatic("JusticeCustodySaturatingAdd", 1790, 30, 1800));
    }

    [TestMethod]
    public void CustodyActivityReduction_IsLimitedBySiteAndQuarterOfInitialSentence()
    {
        object script = CreateJusticeHeadlessScript();

        SetEnumField(script, "_justiceCustodySite", "MissionRow");
        SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 200);
        Assert.AreEqual(50, InvokeInstance(script, "GetJusticeCustodyMaximumActivityReduction"));
        SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 600);
        Assert.AreEqual(60, InvokeInstance(script, "GetJusticeCustodyMaximumActivityReduction"));

        SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
        SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 800);
        Assert.AreEqual(200, InvokeInstance(script, "GetJusticeCustodyMaximumActivityReduction"));
        SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 1800);
        Assert.AreEqual(300, InvokeInstance(script, "GetJusticeCustodyMaximumActivityReduction"));
    }

    [TestMethod]
    public void CustodyWeaponSnapshot_RejectsDuplicatesInvalidSelectionAndUnsafeValues()
    {
        object valid = CreateValidWeaponSnapshot();
        Assert.IsTrue((bool)InvokeStatic("ValidateJusticeWeaponSnapshot", valid));

        IList weapons = (IList)GetMemberValue(valid, "Weapons");
        object duplicate = CreateWeaponSnapshotItem(weaponHash: 12345, ammo: 50, clip: 12, tint: 1, components: new[] { 777 });
        weapons.Add(duplicate);
        Assert.IsFalse((bool)InvokeStatic("ValidateJusticeWeaponSnapshot", valid));

        object missingSelection = CreateValidWeaponSnapshot();
        SetMemberValue(missingSelection, "SelectedWeaponHash", 99999);
        Assert.IsFalse((bool)InvokeStatic("ValidateJusticeWeaponSnapshot", missingSelection));

        object duplicateComponentSnapshot = CreateValidWeaponSnapshot();
        object item = ((IList)GetMemberValue(duplicateComponentSnapshot, "Weapons"))[0];
        ((IList)GetMemberValue(item, "ComponentHashes")).Add(777);
        Assert.IsFalse((bool)InvokeStatic("ValidateJusticeWeaponSnapshot", duplicateComponentSnapshot));
    }

    [TestMethod]
    public void CustodyDiscipline_IsIncrementalAndIdempotentPerIncident()
    {
        WithTemporarySaveDirectory(_ =>
        {
            object script = CreateJusticeHeadlessScript();
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
            state.Enabled = true;
            state.CustodyEpisodeId = "custody:discipline";
            state.WantedEpisodeId = "pursuit:discipline";
            state.Phase = JusticePhase.Incarcerated;
            state.SentenceSeconds = 420;
            state.FineDue = 9999L;
            state.ActiveScore = 70;
            state.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:discipline:base",
                IncidentId = "incident:discipline:base",
                EpisodeId = state.WantedEpisodeId,
                Kind = JusticeCrimeKind.MurderCivilian,
                DisplayName = "Condamnation initiale",
                Points = 70,
                Fine = 9999L,
                SentenceSeconds = 420,
                IsAdjudicated = true
            });
            SetFieldValue(script, "_justiceEnabled", true);
            SetFieldValue(script, "_justiceInitialized", true);
            SetFieldValue(script, "_justiceMonotonicTimeMs", 5000L);
            SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
            SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 420);
            SetFieldValue(script, "_justiceCustodyPlayerModelHash", 12345);
            SetFieldValue(script, "_justiceCustodyPlayerSlot", 0);

            bool first = (bool)InvokeInstance(
                script,
                "JusticeRegisterCustodyDisciplineCharge",
                JusticeCrimeKind.ReportedViolentAct,
                45,
                "Faute disciplinaire",
                "discipline:unique");

            Assert.IsTrue(first);
            Assert.AreEqual(465, state.SentenceSeconds, "Seule la peine minimale de la nouvelle faute est ajoutée.");
            Assert.AreEqual(10349L, state.FineDue, "Seule l'amende de la nouvelle faute en détention est ajoutée.");
            Assert.AreEqual(2, state.Charges.Count);
            Assert.AreEqual(1, record.Convictions.Count);
            Assert.AreEqual(1, record.Convictions[0].Charges.Count);

            int sentenceAfterFirst = state.SentenceSeconds;
            long fineAfterFirst = state.FineDue;
            int scoreAfterFirst = state.ActiveScore;
            bool second = (bool)InvokeInstance(
                script,
                "JusticeRegisterCustodyDisciplineCharge",
                JusticeCrimeKind.ReportedViolentAct,
                45,
                "Faute disciplinaire",
                "discipline:unique");

            Assert.IsFalse(second);
            Assert.AreEqual(sentenceAfterFirst, state.SentenceSeconds);
            Assert.AreEqual(fineAfterFirst, state.FineDue);
            Assert.AreEqual(scoreAfterFirst, state.ActiveScore);
            Assert.AreEqual(2, state.Charges.Count);
            Assert.AreEqual(1, record.Convictions.Count, "La même faute ne doit jamais réaugmenter le casier.");
        });
    }

    [TestMethod]
    public void CustodyDiscipline_DistinctIncidentsEachReceiveOneSanction()
    {
        WithTemporarySaveDirectory(_ =>
        {
            object script = CreateJusticeHeadlessScript();
            JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
            JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
            state.Enabled = true;
            state.CustodyEpisodeId = "custody:discipline-distinct";
            state.WantedEpisodeId = "pursuit:discipline-distinct";
            state.Phase = JusticePhase.Incarcerated;
            state.SentenceSeconds = 300;
            state.FineDue = 1000L;
            state.ActiveScore = 5;
            state.Charges.Add(new JusticeCharge
            {
                ChargeId = "charge:discipline-distinct:base",
                IncidentId = "incident:discipline-distinct:base",
                EpisodeId = state.WantedEpisodeId,
                Kind = JusticeCrimeKind.ReportedViolentAct,
                DisplayName = "Condamnation initiale",
                Points = 5,
                Fine = 1000L,
                SentenceSeconds = 300,
                IsAdjudicated = true
            });
            SetFieldValue(script, "_justiceEnabled", true);
            SetFieldValue(script, "_justiceInitialized", true);
            SetFieldValue(script, "_justiceMonotonicTimeMs", 5000L);
            SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
            SetFieldValue(script, "_justiceCustodyInitialSentenceSeconds", 300);
            SetFieldValue(script, "_justiceCustodyPlayerModelHash", 12345);
            SetFieldValue(script, "_justiceCustodyPlayerSlot", 0);

            Assert.IsTrue((bool)InvokeInstance(
                script,
                "JusticeRegisterCustodyDisciplineCharge",
                JusticeCrimeKind.ReportedViolentAct,
                45,
                "Première faute",
                "discipline:first"));
            int sentenceAfterFirst = state.SentenceSeconds;
            long fineAfterFirst = state.FineDue;

            Assert.IsTrue((bool)InvokeInstance(
                script,
                "JusticeRegisterCustodyDisciplineCharge",
                JusticeCrimeKind.ReportedViolentAct,
                45,
                "Deuxième faute",
                "discipline:second"));
            Assert.AreEqual(sentenceAfterFirst + 45, state.SentenceSeconds);
            Assert.IsTrue(state.FineDue > fineAfterFirst);
            Assert.AreEqual(3, state.Charges.Count);
            Assert.AreEqual(2, record.Convictions.Count);

            int finalSentence = state.SentenceSeconds;
            long finalFine = state.FineDue;
            Assert.IsFalse((bool)InvokeInstance(
                script,
                "JusticeRegisterCustodyDisciplineCharge",
                JusticeCrimeKind.ReportedViolentAct,
                45,
                "Rejeu de la deuxième faute",
                "discipline:second"));
            Assert.AreEqual(finalSentence, state.SentenceSeconds);
            Assert.AreEqual(finalFine, state.FineDue);
            Assert.AreEqual(3, state.Charges.Count);
            Assert.AreEqual(2, record.Convictions.Count);
        });
    }

    [TestMethod]
    public void JusticeRecapture_AfterEscapeAdjudicatesOnlyTheNewEscapeCharge()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            CustodyEpisodeId = "custody:first"
        };
        JusticeRecordState record = new JusticeRecordState();
        JusticeCharge initialCharge = JusticePolicy.ApplyConfirmedIncident(
            state,
            CreateConfirmedDirectIncident(
                JusticeCrimeKind.ReportedViolentAct,
                "incident:initial",
                "pursuit:first",
                JusticeCircumstances.None),
            record);
        Assert.IsNotNull(initialCharge);

        JusticeConviction initialConviction = JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 8, 25, 18, 0, 0, DateTimeKind.Utc));
        Assert.IsNotNull(initialConviction);
        Assert.IsTrue(initialCharge.IsAdjudicated);
        Assert.AreEqual(2, record.RecidivismIndex);

        // Je simule ici la dette déjà prélevée et le temps partiellement purgé.
        state.FineDue = 0L;
        state.SentenceSeconds = 120;
        JusticeCharge escapeCharge = JusticePolicy.ApplyConfirmedIncident(
            state,
            CreateConfirmedDirectIncident(
                JusticeCrimeKind.Escape,
                "incident:escape",
                "custody:first",
                JusticeCircumstances.InCustody),
            record);
        Assert.IsNotNull(escapeCharge);
        Assert.IsFalse(escapeCharge.IsAdjudicated);
        Assert.AreEqual(
            escapeCharge.Fine,
            state.FineDue,
            "L'amende de la première condamnation déjà débitée ne doit jamais renaître.");
        Assert.AreEqual(
            Math.Min(JusticePolicy.MaxActiveSentenceSeconds, 120 + escapeCharge.SentenceSeconds),
            state.SentenceSeconds,
            "Le reliquat purgé doit seulement recevoir la nouvelle peine d'évasion.");

        state.CustodyEpisodeId = "custody:recapture";
        JusticeConviction recaptureConviction = JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 8, 25, 19, 0, 0, DateTimeKind.Utc));

        Assert.IsNotNull(recaptureConviction);
        Assert.AreEqual(2, record.Convictions.Count);
        Assert.AreEqual(1, recaptureConviction.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.Escape, recaptureConviction.Charges[0].Kind);
        Assert.AreEqual(escapeCharge.Points, recaptureConviction.Score);
        Assert.AreEqual(escapeCharge.Fine, recaptureConviction.Fine);
        Assert.AreEqual(escapeCharge.SentenceSeconds, recaptureConviction.SentenceSeconds);
        Assert.IsTrue(escapeCharge.IsAdjudicated);
        Assert.AreEqual(30, record.RecidivismIndex, "R ne doit augmenter que pour la nouvelle charge d'évasion.");

        JusticeConviction replay = JusticePolicy.ApplyConviction(state, record, DateTime.UtcNow);
        Assert.AreSame(recaptureConviction, replay);
        Assert.AreEqual(30, record.RecidivismIndex);
        Assert.AreEqual(2, record.Convictions.Count);
    }

    [TestMethod]
    public void JusticeNewCrimeAfterJudgment_PreservesPaidFineAndRemainingSentenceLedger()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            CustodyEpisodeId = "custody:paid"
        };
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident murder = CreateConfirmedDirectIncident(
            JusticeCrimeKind.MurderCivilian,
            "incident:judged-murder",
            "pursuit:ledger",
            JusticeCircumstances.None);
        murder.VictimHandle = 440;
        murder.VictimGeneration = 7;
        JusticeCharge judgedCharge = JusticePolicy.ApplyConfirmedIncident(state, murder, record);

        Assert.IsNotNull(judgedCharge);
        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc)));
        state.FineDue = 0L;
        state.SentenceSeconds = 90;
        state.Phase = JusticePhase.Fugitive;
        state.HasWarrant = true;

        JusticeIncident newAssault = CreateConfirmedDirectIncident(
            JusticeCrimeKind.SimpleAssault,
            "incident:new-assault",
            "pursuit:ledger",
            JusticeCircumstances.ActiveWarrant);
        newAssault.VictimHandle = 440;
        newAssault.VictimGeneration = 7;
        JusticeCharge pendingCharge = JusticePolicy.ApplyConfirmedIncident(state, newAssault, record);

        Assert.IsNotNull(pendingCharge);
        Assert.AreEqual(2, state.Charges.Count);
        Assert.IsTrue(judgedCharge.IsAdjudicated);
        Assert.AreEqual(JusticeCrimeKind.MurderCivilian, judgedCharge.Kind);
        Assert.AreEqual(pendingCharge.Fine, state.FineDue);
        Assert.AreEqual(
            Math.Min(JusticePolicy.MaxActiveSentenceSeconds, 90 + pendingCharge.SentenceSeconds),
            state.SentenceSeconds);
        Assert.AreEqual(judgedCharge.Points + pendingCharge.Points, state.ActiveScore);
    }

    [TestMethod]
    public void JusticeLegacyContributorAndVictimGeneration_MigrateWithoutInventingASecondIdentity()
    {
        JusticeCharge legacyContributor = new JusticeCharge
        {
            IsAlliedAction = true,
            Kind = JusticeCrimeKind.AccessoryAssaultOfficer
        };
        legacyContributor.AlliedContributorHandles.Add(701);

        Assert.IsTrue(legacyContributor.HasAlliedContributor(701, 14));
        legacyContributor.AddAlliedContributor(701, 14);
        Assert.AreEqual(1, legacyContributor.AlliedContributors.Count);
        Assert.AreEqual(14, legacyContributor.AlliedContributors[0].Generation);

        JusticeCaseState state = new JusticeCaseState { Enabled = true };
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident legacyAssault = CreateConfirmedDirectIncident(
            JusticeCrimeKind.SimpleAssault,
            "incident:legacy-victim",
            "episode:legacy-victim",
            JusticeCircumstances.None);
        legacyAssault.VictimHandle = 702;
        legacyAssault.VictimGeneration = 0;
        JusticeCharge legacyCharge = JusticePolicy.ApplyConfirmedIncident(state, legacyAssault, record);
        Assert.IsNotNull(legacyCharge);

        JusticeIncident homicide = CreateConfirmedDirectIncident(
            JusticeCrimeKind.MurderCivilian,
            "incident:legacy-victim-upgrade",
            "episode:legacy-victim",
            JusticeCircumstances.None);
        homicide.VictimHandle = 702;
        homicide.VictimGeneration = 9;
        JusticeCharge upgraded = JusticePolicy.ApplyConfirmedIncident(state, homicide, record);

        Assert.IsNotNull(upgraded);
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.MurderCivilian, upgraded.Kind);
        Assert.AreEqual(9, upgraded.VictimGeneration);
    }

    [TestMethod]
    public void JusticeEscape_PersistsDiscardIntentBeforeRemovalThenCommitsFugitiveState()
    {
        string repositoryRoot = GetRepositoryRoot();
        string custodySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string runtimeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        string completion = ExtractMethodBody(custodySource, "CompleteJusticeCustodyEscape");
        AssertOrdered(
            completion,
            "TryRegisterOperation",
            "JusticeMarkStateDirty()",
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            "RemoveJusticePlayerWeaponsSafe(player)",
            "JusticeRegisterEscape()",
            "PruneClosedCustodyOperations",
            "_justiceCaseState.CustodyEpisodeId = string.Empty",
            "ResetJusticeCustodyPersistentFields(preserveAmbiguousInventoryRecovery)",
            "JusticeMarkStateDirty()",
            "JusticeFlushStateNow()");
        AssertOrdered(
            completion,
            "JusticeInventoryRemovalResult removalResult",
            "JusticeInventoryRemovalResult.EffectMayHaveApplied",
            "RegisterJusticeInventoryRemovalFailure(removalResult, now)",
            "preserveAmbiguousInventoryRecovery = true",
            "if (!preserveAmbiguousInventoryRecovery)",
            "_justiceWeaponSnapshot = null",
            "ResetJusticeCustodyPersistentFields(preserveAmbiguousInventoryRecovery)");
        Assert.AreEqual(
            1,
            Regex.Matches(completion, @"JusticeFlushStateNow\s*\(").Count,
            "La sortie ferme directement l'épisode après le précommit redondant encapsulé.");
        Assert.AreEqual(
            0,
            Regex.Matches(
                ExtractMethodBody(custodySource, "PersistJusticeCriticalPrecommitRedundantly"),
                @"JusticeFlushStateNow\s*\(").Count,
            "Le précommit critique ne doit plus bloquer le thread GTA sur deux flush XML complets.");
        Assert.AreEqual(
            1,
            Regex.Matches(
                ExtractMethodBody(custodySource, "PersistJusticeCriticalPrecommitRedundantly"),
                @"PersistJusticeCriticalPrecommitToWal\s*\(").Count,
            "Un effet critique doit passer par une unique écriture WAL durable.");

        string persistenceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.Runtime.cs"));
        string walPrecommit = ExtractMethodBody(
            persistenceSource,
            "PersistJusticeCriticalPrecommitToWal");
        Assert.AreEqual(
            0,
            Regex.Matches(walPrecommit, @"_justiceWriteAheadLog\.Append\s*\(prepared\s*\)").Count,
            "Le thread GTA ne doit écrire aucune frame avant la confirmation disque du snapshot.");
        AssertOrdered(
            walPrecommit,
            "TryCaptureJusticePersistenceSnapshot",
            "TryEnqueueJusticeSnapshot(snapshot, false)",
            "_justiceCriticalBarrierRevision = snapshot.Revision",
            "TryCommitJusticeCriticalBarrierToWal()");
        string walCommit = ExtractMethodBody(
            persistenceSource,
            "TryCommitJusticeCriticalBarrierToWal");
        AssertOrdered(
            walCommit,
            "_justiceRepository.GetDiagnostics()",
            "diagnostics.DiskRevision < _justiceCriticalBarrierRevision",
            "JusticeWalState.Prepared",
            "_justiceWriteAheadLog.Append(prepared)",
            "JusticeWalState.Attempted");
        Assert.AreEqual(
            1,
            Regex.Matches(walCommit, @"_justiceWriteAheadLog\.Append\s*\(prepared\s*\)").Count,
            "Une seule frame Prepared compacte doit être écrite après confirmation disque.");

        string registration = ExtractMethodBody(runtimeSource, "JusticeRegisterEscape");
        Assert.IsFalse(
            registration.Contains("JusticeFlushStateNow"),
            "L'enregistrement interne ne doit pas créer un état fugitif intermédiaire avec l'ancien épisode de garde à vue.");
    }

    [TestMethod]
    public void JusticeEscape_DiscardsTheSnapshotOnlyAfterANonAmbiguousRemovalOutcome()
    {
        string completion = ExecutableMethodBody(
            File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Custody.cs")),
            "CompleteJusticeCustodyEscape");

        int precommitAt = completion.IndexOf(
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            StringComparison.Ordinal);
        int removalAt = completion.IndexOf("RemoveJusticePlayerWeaponsSafe(player)", StringComparison.Ordinal);
        int snapshotDiscardAt = completion.IndexOf("_justiceWeaponSnapshot = null", StringComparison.Ordinal);
        Assert.IsTrue(precommitAt >= 0 && precommitAt < removalAt && removalAt < snapshotDiscardAt);

        string beforeRemoval = completion.Substring(precommitAt, removalAt - precommitAt);
        Assert.IsFalse(
            beforeRemoval.Contains("_justiceInventoryRemoved"),
            "RemoveAll ne doit pas dépendre du fait que l'inventaire initial ait déjà été confisqué.");
        AssertOrdered(
            completion,
            "JusticePolicy.TryRegisterOperation(_justiceCaseState, discard)",
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            "IsJusticeCustodyPlayerIdentityCompatible(player)",
            "ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot)",
            "RemoveJusticePlayerWeaponsSafe(player)",
            "JusticeInventoryRemovalResult.EffectMayHaveApplied",
            "preserveAmbiguousInventoryRecovery = true",
            "if (!preserveAmbiguousInventoryRecovery)",
            "_justiceWeaponSnapshot = null",
            "JusticeRegisterEscape()");
    }

    [TestMethod]
    public void CustodyDiscipline_QualifiesProvenDeathsBeforeGenericAssaults()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string method = ExtractMethodBody(source, "TryGetJusticeCustodyMisconduct");
        AssertOrdered(
            method,
            "TryCaptureJusticeDamageFront(guard, player)",
            "guard.IsDead && IsJusticeDeathAttributedTo(",
            "JusticeCrimeKind.MurderOfficer",
            "if (damagedByPlayer)",
            "JusticeCrimeKind.AssaultOfficer",
            "TryCaptureJusticeDamageFront(player, inmate)",
            "RememberJusticeCustodyAggressor(inmate)",
            "TryCaptureJusticeDamageFront(inmate, player)",
            "inmate.IsDead && IsJusticeDeathAttributedTo(",
            "JusticeCrimeKind.MurderCivilian",
            "if (damagedByPlayer)",
            "HasFreshJusticeCustodyAggression(inmate, canUseUnarmedCombat)",
            "JusticeCrimeKind.SimpleAssault");
    }

    [TestMethod]
    public void CustodyRuntime_SuspendsWorldMutationAndQualifiesDisciplineBeforeSceneCompaction()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExtractMethodBody(source, "JusticeUpdateCustody");

        AssertOrdered(
            update,
            "if (!JusticeCustodyCanMutateWorld(player))",
            "RetryJusticeInventoryConfiscationIfDue(player, now)",
            "UpdateJusticeCustodyDiscipline(player, now)",
            "EnsureJusticeCustodyScene(now)");
    }

    [TestMethod]
    public void CustodyGuards_RemoveDefaultWeaponsBeforeReceivingOnlyNonLethalEquipment()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string creation = ExecutableMethodBody(custodySource, "CreateJusticeCustodyPed");

        AssertOrdered(
            creation,
            "if (guard)",
            "ped.Weapons.RemoveAll()",
            "JusticeStunGunHash",
            "JusticeNightstickHash",
            "TASK_STAND_STILL");
    }

    [TestMethod]
    public void CustodyTeleportFailure_RestoresTransientStateThenRemasksAndKeepsStagesRetryable()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string transfer = ExecutableMethodBody(custodySource, "CompleteJusticeCustodyTransfer");
        string emergency = ExecutableMethodBody(custodySource, "TryJusticeEmergencyTeleport");
        string discipline = ExecutableMethodBody(custodySource, "CompleteJusticeCustodyDiscipline");

        AssertOrdered(
            transfer,
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)",
            "TryJusticeEmergencyTeleport(",
            "if (maskRespawnOrigin)",
            "ReassertJusticeCustodyRespawnTransferMask()",
            "HandleJusticeCustodyTransferFailure(player, now)",
            "RestoreJusticeCustodyPlayerTransientState(player)",
            "return;");
        AssertOrdered(
            emergency,
            "SetEntityCoordsNoOffsetSafe(",
            "IsJusticeTeleportVerified(player, targetPosition, 8.0f)",
            "player.Position = targetPosition",
            "DO_SCREEN_FADE_IN");
        AssertOrdered(
            discipline,
            "bool returnedToCell = false",
            "TryJusticeEmergencyTeleport(",
            "if (!returnedToCell)",
            "JusticeRegisterCustodyDisciplineCharge(");
    }

    [TestMethod]
    public void CustodyReleaseFineStage_UsesThePersistedIncidentInsteadOfRepeatableTotals()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.CustodyEpisodeId = "custody:fines";
        state.FineDue = JusticePolicy.MaxActiveFine;
        state.Charges.Add(new JusticeCharge
        {
            IncidentId = "discipline:first",
            Fine = 5000L
        });
        state.Charges.Add(new JusticeCharge
        {
            IncidentId = "discipline:second",
            Fine = 5000L
        });

        Assert.AreEqual(
            "release:discipline:second",
            InvokeInstance(script, "BuildJusticeReleaseFineStage"));

        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string release = ExecutableMethodBody(custodySource, "CompleteJusticeLegalRelease");
        StringAssert.Contains(release, "BuildJusticeReleaseFineStage()");
        Assert.IsFalse(release.Contains("Charges.Count.ToString"));
        Assert.IsFalse(release.Contains("FineDue.ToString"));
    }

    [TestMethod]
    public void CustodyLegalRelease_CommitsAReplayableWalBeforeInventoryAndResumesBeforeCustody()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string completion = ExecutableMethodBody(
            custodySource,
            "CompleteJusticeLegalRelease");
        AssertOrdered(
            completion,
            "JusticeOperationKind.Release",
            "_justiceLegalReleaseFinalizationPending = true",
            "ResumeJusticeLegalReleaseFinalization(player, now)");
        Assert.IsFalse(
            completion.Contains("RestoreJusticeInventoryForLegalRelease"),
            "La restitution ne doit jamais précéder le précommit repris par Resume.");

        string resume = ExecutableMethodBody(
            custodySource,
            "ResumeJusticeLegalReleaseFinalization");
        AssertOrdered(
            resume,
            "PersistJusticeLegalReleaseBarrier()",
            "RestoreJusticeInventoryForLegalRelease(player, now)",
            "JusticePrepareLegalReleaseState()",
            "PersistJusticeLegalReleaseBarrier()",
            "TeleportPlayerWithFadeSafe",
            "_justiceLegalReleaseWantedClearAttempted = true",
            "ClearJusticeWantedLevelOnceDetailed()",
            "CommitJusticeLegalReleaseFinalizationAcknowledgement()");
        string barrier = ExecutableMethodBody(
            custodySource,
            "PersistJusticeLegalReleaseBarrier");
        StringAssert.Contains(barrier, "PersistJusticeCriticalPrecommitRedundantly()");
        string acknowledgement = ExecutableMethodBody(
            custodySource,
            "CommitJusticeLegalReleaseFinalizationAcknowledgement");
        AssertOrdered(
            acknowledgement,
            "_justiceLegalReleaseFinalizationPending = false",
            "PersistJusticeLegalReleaseBarrier()",
            "_justiceLegalReleaseFinalizationPending = true");

        string update = ExecutableMethodBody(ReadRuntimeSource(), "UpdateJusticeSystem");
        AssertOrdered(
            update,
            "ResumeJusticeLegalReleaseFinalization(player, nowRaw)",
            "bool legalReleasePending",
            "JusticeUpdateCustody(player, nowRaw)");
        StringAssert.Contains(update, "legalReleasePending ||");
    }

    [TestMethod]
    public void CustodyTransfer_ReleasesOnlyTheCurrentPoliceCombatTaskWithoutGlobalImmobilization()
    {
        string transfer = ExecutableMethodBody(
            ReadRuntimeSource(),
            "ReleaseJusticeAllyPoliceTargetsForTransfer");

        AssertOrdered(
            transfer,
            "IsJusticeAllyTokenValidForTransfer(token, player)",
            "TryReleaseJusticeAllyPoliceTargetForTransfer(token, player)",
            "_justiceReleasedAllyHandles.Add",
            "_justiceAllyTokens.Clear()");
        string coreSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.cs"));
        string releaseHelper = ExecutableMethodBody(
            coreSource,
            "TryReleaseJusticeAllyPoliceTargetForTransfer");
        AssertOrdered(
            releaseHelper,
            "IsJusticeTransferTargetContextValid(",
            "TryHoldJusticeAllyServiceDuringCustody(token.Ally)",
            "PrepareJusticeAllyServiceResume(token.Ally)",
            "return true;");
        string holdHelper = ExecutableMethodBody(
            coreSource,
            "TryHoldJusticeAllyServiceDuringCustody");
        StringAssert.Contains(holdHelper, "NativeTaskVehicleTempAction");
        StringAssert.Contains(holdHelper, "TASK_STAND_STILL");
        StringAssert.Contains(holdHelper, "JusticeAllyCustodyHoldMs");
        Assert.IsFalse(holdHelper.Contains("CLEAR_PED_TASKS"));
        string transferValidation = ExecutableMethodBody(
            ReadRuntimeSource(),
            "IsJusticeAllyTokenValidForTransfer");
        StringAssert.Contains(transferValidation, "JusticePhase.Captured");
        Assert.IsFalse(transferValidation.Contains("!JusticeIsCustodyActive"));

        string runtime = ReadRuntimeSource();
        string capture = ExecutableMethodBody(runtime, "BeginJusticeCapture");
        string committedCapture = ExecutableMethodBody(
            runtime,
            "CompleteJusticeCaptureAfterCommit");
        AssertOrdered(
            committedCapture,
            "ReleaseJusticeAllyPoliceTargetsForTransfer()",
            "_justicePursuitActive = false");
        AssertOrdered(
            capture,
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            "CompleteJusticeCaptureAfterCommit(");

        Assert.AreEqual(
            0,
            Regex.Matches(transfer, @"\bCLEAR_PED_TASKS\b").Count,
            "La boucle ne doit pas contourner le helper qui revalide le jeton.");
        Assert.AreEqual(
            0,
            Regex.Matches(releaseHelper, @"\bCLEAR_PED_TASKS\b").Count,
            "Le transfert doit remplacer la tâche prouvée sans vider la pile de tâches.");
        Assert.IsFalse(transfer.Contains("HoldAllJusticeAlliesForCustodyTransfer"));
        Assert.IsFalse(transfer.Contains("TASK_STAND_STILL"));
        Assert.IsFalse(transfer.Contains("SET_VEHICLE_FORWARD_SPEED"));
        Assert.IsFalse(transfer.Contains("StopHighSecurityEscortConvoyImmediately"));
        Assert.IsFalse(transfer.Contains("Delete"));
        Assert.IsFalse(transfer.Contains("Dismiss"));
    }

    [TestMethod]
    public void CustodyExternalMutations_AreGuardedByPersistedPrecommit()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));

        string fineMethod = ExecutableMethodBody(source, "JusticeCollectFineAndConvertDetention");
        AssertOrdered(
            fineMethod,
            "_justiceFineDebitIntent = new JusticeFineDebitIntent",
            "JusticeMarkStateDirty",
            "if (!EnsureJusticeFinancialPreparedSnapshot(\"FineDebit\"))",
            "ResumeJusticeFineDebitIntent()");

        string fineResume = ExecutableMethodBody(source, "ResumeJusticeFineDebitIntent");
        StringAssert.Contains(
            fineResume,
            "intent.DebitAttempted && cash == intent.CashAfter",
            "CashAfter ne doit prouver le débit qu'après son précommit irréversible.");
        int unresolvedReadAt = fineResume.IndexOf(
            "if (!resolvedWithoutCash && !cashRead && !intent.DebitAttempted)",
            StringComparison.Ordinal);
        int attemptedReadAt = fineResume.IndexOf(
            "else if (!resolvedWithoutCash && !cashRead)",
            unresolvedReadAt,
            StringComparison.Ordinal);
        Assert.IsTrue(unresolvedReadAt >= 0 && attemptedReadAt > unresolvedReadAt);
        string unresolvedRead = fineResume.Substring(
            unresolvedReadAt,
            attemptedReadAt - unresolvedReadAt);
        AssertOrdered(
            unresolvedRead,
            "HasFineDebitPreparationTimedOut",
            "return false;",
            "intent.SentenceIfConverted");
        Assert.IsFalse(unresolvedRead.Contains("TryWriteJusticeSinglePlayerCash"));

        int knownSlotReadAt = fineMethod.IndexOf(
            "bool cashPlanPrepared = TryReadJusticeSinglePlayerCash(slot, out currentCash)",
            StringComparison.Ordinal);
        int intentCreationAt = fineMethod.IndexOf(
            "_justiceFineDebitIntent = new JusticeFineDebitIntent",
            StringComparison.Ordinal);
        Assert.IsTrue(knownSlotReadAt >= 0 && intentCreationAt > knownSlotReadAt);
        string knownSlotReadFailure = fineMethod.Substring(
            knownSlotReadAt,
            intentCreationAt - knownSlotReadAt);
        StringAssert.Contains(knownSlotReadFailure, "preparedAtUtcTicks");
        Assert.IsFalse(knownSlotReadFailure.Contains("SentenceSeconds ="));

        int ambiguousAttemptAt = fineResume.LastIndexOf(
            "else if (!resolvedWithoutCash)",
            StringComparison.Ordinal);
        Assert.IsTrue(ambiguousAttemptAt >= 0);
        AssertOrdered(
            fineResume.Substring(ambiguousAttemptAt),
            "HasFineDebitAttemptTimedOut",
            "return false;",
            "ResetJusticeFineCashReadRetry()");

        int externalDebit = fineResume.IndexOf("TryWriteJusticeSinglePlayerCash", StringComparison.Ordinal);
        Assert.IsTrue(externalDebit >= 0);
        AssertOrdered(
            fineResume.Substring(0, externalDebit),
            "TryArmJusticeFinancialAttempt(",
            "intent.DebitAttempted = true",
            "intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks");
        Assert.AreEqual(
            1,
            Regex.Matches(fineResume, "TryWriteJusticeSinglePlayerCash").Count,
            "Une intention Attempted ne doit jamais pouvoir réémettre le débit absolu.");
        StringAssert.Contains(fineResume, "HasFineDebitAttemptTimedOut");
        StringAssert.Contains(fineResume, "at-most-once");
        Assert.IsTrue(
            fineMethod.LastIndexOf("EnsureJusticeFinancialPreparedSnapshot", StringComparison.Ordinal) <
            fineMethod.LastIndexOf("ResumeJusticeFineDebitIntent", StringComparison.Ordinal),
            "L'intention durable doit précéder tout appel capable d'écrire le cash GTA.");

        string confiscation = ExtractMethodBody(source, "PrepareJusticeInventoryConfiscation");
        AssertOrdered(
            confiscation,
            "ValidateJusticeWeaponSnapshot",
            "JusticeInventoryCustodyState.SnapshotPersisted",
            "PersistJusticeCriticalPrecommitRedundantly",
            "JusticeInventoryCustodyState.RemovalPending",
            "RemoveJusticePlayerWeaponsSafe");
        string criticalWrapper = ExtractMethodBody(
            source,
            "PersistJusticeCriticalPrecommitRedundantly");
        Assert.AreEqual(
            1,
            Regex.Matches(criticalWrapper, @"PersistJusticeCriticalPrecommitToWal\s*\(").Count,
            "Le wrapper critique doit déléguer une seule fois au WAL sans double flush XML.");
        Assert.AreEqual(0, Regex.Matches(criticalWrapper, @"JusticeFlushStateNow\s*\(").Count);

        string persistenceSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.Runtime.cs"));
        string walPrecommit = ExtractMethodBody(
            persistenceSource,
            "PersistJusticeCriticalPrecommitToWal");
        AssertOrdered(
            walPrecommit,
            "TryCaptureJusticePersistenceSnapshot",
            "TryEnqueueJusticeSnapshot(snapshot, false)",
            "_justiceCriticalBarrierRevision = snapshot.Revision",
            "TryCommitJusticeCriticalBarrierToWal()");
        Assert.AreEqual(
            0,
            Regex.Matches(walPrecommit, @"_justiceWriteAheadLog\.Append\s*\(prepared\s*\)").Count,
            "Aucune frame WAL ne doit précéder la confirmation disque du snapshot complet.");
        string walCommit = ExtractMethodBody(
            persistenceSource,
            "TryCommitJusticeCriticalBarrierToWal");
        AssertOrdered(
            walCommit,
            "_justiceRepository.GetDiagnostics()",
            "diagnostics.DiskRevision < _justiceCriticalBarrierRevision",
            "JusticeWalState.Prepared",
            "_justiceWriteAheadLog.Append(prepared)",
            "JusticeWalState.Attempted");
        Assert.AreEqual(
            1,
            Regex.Matches(walCommit, @"_justiceWriteAheadLog\.Append\s*\(prepared\s*\)").Count,
            "Une mutation externe ne doit produire qu'une frame Prepared compacte.");
        int failedSnapshotFlush = confiscation.IndexOf(
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            StringComparison.Ordinal);
        int removeAll = confiscation.LastIndexOf("RemoveJusticePlayerWeaponsSafe", StringComparison.Ordinal);
        Assert.IsTrue(failedSnapshotFlush >= 0 && failedSnapshotFlush < removeAll);
        StringAssert.Contains(
            confiscation.Substring(failedSnapshotFlush, removeAll - failedSnapshotFlush),
            "return JusticeInventoryPreparationResult.RetryableFailure;",
            "Un snapshot non persisté doit préserver physiquement l'inventaire.");
    }

    [TestMethod]
    public void CustodyIdentity_UsesPersistedModelAndAmnestyCannotRestoreAnotherHero()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string identity = ExecutableMethodBody(source, "IsJusticeCustodyPlayerIdentityCompatible");
        StringAssert.Contains(identity, "JusticePolicy.IsCustodyLiveIdentityCompatible");
        Assert.IsFalse(
            identity.Contains("return player.Handle == _justiceCustodyPlayerHandle"),
            "Un handle GTA réutilisable ne doit jamais remplacer l'identité persistée du protagoniste.");
        Assert.IsFalse(
            identity.Contains("_justiceCustodyPlayerModelHash = modelHash"),
            "Une vérification d'identité ne doit jamais lier implicitement le héros courant.");

        string runtime = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        AssertOrdered(
            ExecutableMethodBody(runtime, "BeginJusticeCapture"),
            "TryBindJusticeCustodyPlayerIdentityForCapture(capturedPlayer, deathCapture)",
            "JusticePolicy.TryRegisterOperation(_justiceCaseState, capture)",
            "PersistJusticeCriticalPrecommitRedundantly()");

        string amnesty = ExecutableMethodBody(source, "JusticeAmnestyCustody");
        AssertOrdered(
            amnesty,
            "HasJusticeCustodyRecoveryState()",
            "IsJusticeCustodyPlayerIdentityCompatible(player)",
            "RestoreJusticeInventoryForLegalRelease(player");
    }

    [TestMethod]
    public void Amnesty_WithOnlyAnActiveCase_DoesNotRequireACustodyIdentity()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        state.ActiveScore = 25;
        state.HasWarrant = true;
        state.Phase = JusticePhase.AtLarge;

        Assert.IsFalse((bool)InvokeInstance(script, "HasJusticeCustodyRecoveryState"));
        Assert.IsTrue(
            (bool)InvokeInstance(script, "JusticeAmnestyCustody"),
            "Un dossier en jeu libre ne possède aucun inventaire de détenu à restaurer.");
        Assert.AreEqual(0, GetFieldValue<int>(script, "_justiceCustodyPlayerModelHash"));
    }

    [TestMethod]
    public void CustodyRespawn_RebindRequiresEitherPristineCaptureOrObservedCustodyDeath()
    {
        object script = CreateJusticeHeadlessScript();
        SetFieldValue(script, "_justiceCustodyWaitingForRespawn", true);

        Assert.IsTrue((bool)InvokeInstance(script, "CanRebindJusticeCustodyIdentityAfterInitialRespawn"));
        SetFieldValue(script, "_justiceInventoryRemoved", true);
        Assert.IsFalse((bool)InvokeInstance(script, "CanRebindJusticeCustodyIdentityAfterInitialRespawn"));
        SetFieldValue(script, "_justiceCustodyDeathRebindPending", true);
        Assert.IsTrue(
            (bool)InvokeInstance(script, "CanRebindJusticeCustodyIdentityAfterInitialRespawn"),
            "Une mort réellement observée doit permettre le remplacement du ped sans perdre le snapshot.");
        SetFieldValue(script, "_justiceCustodyDeathRebindPending", false);
        SetFieldValue(script, "_justiceInventoryRemoved", false);
        SetFieldValue(script, "_justiceCustodyPlayerStateStored", true);
        Assert.IsFalse((bool)InvokeInstance(script, "CanRebindJusticeCustodyIdentityAfterInitialRespawn"));

        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)",
            "TryRebindJusticeCustodyIdentityAfterRespawn(player)",
            "if (_justiceFineDebitIntent != null)",
            "if (!IsJusticeCustodyPlayerIdentityCompatible(player))");
        string suspendedDeath = ExecutableMethodBody(
            source,
            "ObserveJusticeCustodyDeathDuringSuspension");
        AssertOrdered(
            suspendedDeath,
            "TryPersistJusticeCustodyDeathFrontToWal(player)",
            "ArmJusticeCustodyDeathFailClosedState(");
        string armDeathFailClosed = ExecutableMethodBody(
            source,
            "ArmJusticeCustodyDeathFailClosedState");
        AssertOrdered(
            armDeathFailClosed,
            "_justiceCustodyDeathRebindPending = true",
            "_justiceCustodyWaitingForRespawn = true",
            "JusticeMarkStateDirty()",
            "_justiceCustodyDeathStatePersistencePending = true",
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)");
        string persistDeath = ExecutableMethodBody(
            source,
            "PersistJusticeCustodyDeathStateBeforeRespawn");
        AssertOrdered(
            persistDeath,
            "TryRejectJusticeCriticalBarrierBeforeCustodyDeath()",
            "JusticeFlushStateNow()",
            "_justiceCustodyDeathPersistenceRevision =",
            "return false;");
        AssertOrdered(
            persistDeath,
            "diagnostics.DiskRevision >=",
            "_justiceCustodyDeathPersistenceRevision",
            "_justiceCustodyDeathStatePersistencePending = false");
        StringAssert.Contains(source, "waitingForRespawn");
    }

    [TestMethod]
    public void DeathCapture_PersistsRespawnWaitBeforeItsFirstFlushAndBeforeTransfer()
    {
        string runtime = ReadRuntimeSource();
        string capture = ExecutableMethodBody(runtime, "BeginJusticeCapture");
        int deathBranchAt = capture.IndexOf("if (deathCapture)", StringComparison.Ordinal);
        int waitingAt = capture.IndexOf(
            "_justiceCustodyWaitingForRespawn = true",
            deathBranchAt,
            StringComparison.Ordinal);
        int dirtyAt = capture.IndexOf("JusticeMarkStateDirty()", waitingAt, StringComparison.Ordinal);
        int committedAt = capture.IndexOf(
            "if (!PersistJusticeCriticalPrecommitRedundantly())",
            dirtyAt,
            StringComparison.Ordinal);
        int transferStageAt = capture.IndexOf(
            "CompleteJusticeCaptureAfterCommit(deathCapture)",
            committedAt,
            StringComparison.Ordinal);
        Assert.IsTrue(
            deathBranchAt >= 0 && deathBranchAt < waitingAt && waitingAt < dirtyAt &&
            dirtyAt < committedAt && committedAt < transferStageAt,
            "L'attente du respawn et le jugement doivent être durables avant tout transfert.");
        int deathRebindAt = capture.IndexOf(
            "_justiceCustodyDeathRebindPending = true",
            deathBranchAt,
            StringComparison.Ordinal);
        Assert.IsTrue(
            deathBranchAt < deathRebindAt && deathRebindAt < waitingAt,
            "Une capture par mort doit autoriser le même profil custom avant son premier tick vivant.");
        StringAssert.Contains(
            ExecutableMethodBody(runtime, "CompleteJusticeCaptureAfterCommit"),
            "JusticeBeginCustodyTransfer(deathCapture)");

        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        AssertOrdered(
            ExecutableMethodBody(custodySource, "JusticeWriteCustodyXml"),
            "waitingForRespawn",
            "WriteJusticeDisciplineIntentXml(writer)");

        string custodyUpdate = ExecutableMethodBody(custodySource, "JusticeUpdateCustody");
        AssertOrdered(
            custodyUpdate,
            "UpdateJusticeCustodyRespawnTransferMask(player)",
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)",
            "_justiceCustodyWaitingForRespawn = false",
            "JusticeFlushStateNow()",
            "CompleteJusticeCustodyTransfer(player, now)");

        string transfer = ExecutableMethodBody(custodySource, "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "bool maskRespawnOrigin = _justiceCustodyRespawnTransferPending",
            "ReassertJusticeCustodyRespawnTransferMask()",
            "TeleportPlayerWithFadeSafe",
            "IsInsideJusticeCustodyLayout(layout, player.Position)",
            "TryRestoreJusticeCustodyRespawnTransferMask()",
            "_justiceCustodyContainmentEstablished = true");
    }

    [TestMethod]
    public void CustodyRespawn_MaskPrecedesPersistenceAndSurvivesBlockedTicks()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(custodySource, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "UpdateJusticeCustodyRespawnTransferMask(player)",
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)");

        string maskUpdate = ExecutableMethodBody(
            custodySource,
            "UpdateJusticeCustodyRespawnTransferMask");
        AssertOrdered(
            maskUpdate,
            "_justiceCustodyRespawnRestorePending",
            "TryRestoreJusticeCustodyRespawnTransferMask()",
            "_justiceCustodyRespawnTransferPending",
            "HasJusticeCustodyRespawnChangedCanonicalPlayer(player)",
            "CanMaskJusticeCustodyRespawnOrigin(player)",
            "_justiceCustodyRespawnTransferPending = true",
            "ReassertJusticeCustodyRespawnTransferMask()");

        string maskIdentity = ExecutableMethodBody(
            custodySource,
            "CanMaskJusticeCustodyRespawnOrigin");
        AssertOrdered(
            maskIdentity,
            "Entity.Exists(player)",
            "player.IsDead",
            "CanMaskJusticePoliceDeathRespawnOrigin(player)",
            "CanRebindJusticeCustodyIdentityAfterInitialRespawn()",
            "GetCurrentSinglePlayerCashSlotSafe()",
            "JusticePolicy.CanRebindCustodyFineIntentSlot(",
            "JusticePolicy.CanRebindCustodyRespawnSlot(",
            "GetJusticePedModelHashSafe(player) != 0");

        int earlyMaskAt = update.IndexOf(
            "UpdateJusticeCustodyRespawnTransferMask(player)",
            StringComparison.Ordinal);
        int transferAt = update.IndexOf(
            "CompleteJusticeCustodyTransfer(player, now)",
            earlyMaskAt,
            StringComparison.Ordinal);
        Assert.IsTrue(earlyMaskAt >= 0 && transferAt > earlyMaskAt);
        Assert.IsFalse(
            update.Substring(earlyMaskAt, transferAt - earlyMaskAt).Contains(
                "_justiceCustodyRespawnTransferPending = false"),
            "Aucun retry de persistance, rebind ou précommit ne doit consommer le masque.");

        string transfer = ExecutableMethodBody(
            custodySource,
            "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "IsJusticeTeleportVerified(player, transferPosition, 8.0f)",
            "TryJusticeEmergencyTeleport(",
            "IsInsideJusticeCustodyLayout(layout, player.Position)",
            "EnsureJusticeCustodyPlayerMobility(player)",
            "if (!transferred)",
            "if (maskRespawnOrigin)",
            "ReassertJusticeCustodyRespawnTransferMask()",
            "HandleJusticeCustodyTransferFailure(player, now)",
            "RestoreJusticeCustodyPlayerTransientState(player)",
            "return;",
            "if (maskRespawnOrigin)",
            "TryRestoreJusticeCustodyRespawnTransferMask()");
        Assert.AreEqual(
            0,
            Regex.Matches(
                transfer,
                Regex.Escape("_justiceCustodyRespawnTransferPending = false"))
                .Count,
            "Le transfert ne doit jamais consommer le masque hors du helper qui vérifie FADE_IN.");
        string restoreMask = ExecutableMethodBody(
            custodySource,
            "TryRestoreJusticeCustodyRespawnTransferMask");
        AssertOrdered(
            restoreMask,
            "if (!RestoreJusticeCustodyRespawnTransferMask())",
            "_justiceCustodyRespawnRestorePending = true",
            "return false;",
            "_justiceCustodyRespawnTransferPending = false",
            "_justiceCustodyRespawnRestorePending = false");

        string shutdown = ExecutableMethodBody(
            custodySource,
            "JusticeShutdownCustody");
        AssertOrdered(
            shutdown,
            "Ped player = TryGetJusticeShutdownPlayer()",
            "try",
            "RunJusticeCustodyShutdownStep(",
            "\"Activite\"",
            "if (_justiceCustodyRespawnTransferPending ||",
            "_justiceCustodyRespawnRestorePending",
            "RestoreJusticeCustodyRespawnTransferMask()",
            "_justiceCustodyRespawnTransferPending = false");
        string shutdownPlayer = ExecutableMethodBody(
            custodySource,
            "TryGetJusticeShutdownPlayer");
        AssertOrdered(
            shutdownPlayer,
            "try",
            "return Game.Player.Character",
            "catch (Exception ex)",
            "LogException(\"Justice.ArretDetention.Joueur\", ex)",
            "return null");

        string observedDeath = ExecutableMethodBody(
            custodySource,
            "ObserveJusticeCustodyDeath");
        StringAssert.Contains(
            observedDeath,
            "_justiceCustodyRespawnMaskNeedsRearm |=");
        Assert.IsFalse(
            observedDeath.Contains("_justiceCustodyRespawnTransferPending = false"),
            "Un nouveau décès doit conserver le latch et demander son réarmement au prochain ped vivant.");
        string suspendedDeath = ExecutableMethodBody(
            custodySource,
            "ObserveJusticeCustodyDeathDuringSuspension");
        StringAssert.Contains(
            suspendedDeath,
            "_justiceCustodyRespawnMaskNeedsRearm |=");
        Assert.IsFalse(
            suspendedDeath.Contains("_justiceCustodyRespawnTransferPending = false"),
            "Le front suspendu doit garder le masque et demander son réarmement.");

        string deathFrontSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.DeathFront.cs"));
        string deathFrontApply = ExecutableMethodBody(
            deathFrontSource,
            "ApplyJusticeDeathFrontToRuntime");
        StringAssert.Contains(
            deathFrontApply,
            "_justiceCustodyRespawnMaskNeedsRearm |=");
        Assert.IsFalse(
            deathFrontApply.Contains("_justiceCustodyRespawnTransferPending = false"),
            "La reprise WAL ne doit jamais effacer un masque runtime encore actif.");

        string beginTransfer = ExecutableMethodBody(
            custodySource,
            "JusticeBeginCustodyTransfer");
        AssertOrdered(
            beginTransfer,
            "bool waitForRespawn",
            "if (!waitForRespawn)",
            "if (!TryRestoreJusticeCustodyRespawnTransferMask())",
            "return;");

        string outageHolding = ExecutableMethodBody(
            custodySource,
            "TryMaintainJusticeCustodyDuringPermanentPersistenceOutage");
        AssertOrdered(
            outageHolding,
            "_justicePersistenceInitializationFailurePermanent",
            "_justiceCustodyDeathPersistenceWriterFailureObserved",
            "CanMaskJusticeCustodyRespawnOrigin(player)",
            "GetJusticeCustodySiteForSentence(",
            "TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(",
            "EnsureJusticeCustodyPlayerMobility(player)",
            "CompleteJusticePreJudgmentHoldingStreamingProtection(player)",
            "_justiceCustodyPersistenceOutageHoldingEstablished = true",
            "TryRestoreJusticeCustodyRespawnTransferMask()");
        Assert.IsFalse(outageHolding.Contains("TeleportPlayerWithFadeSafe("));
        Assert.IsFalse(outageHolding.Contains("TryJusticeEmergencyTeleport("));
        Assert.IsFalse(outageHolding.Contains("JusticePhase.AtLarge"));
        Assert.IsFalse(outageHolding.Contains("RemoveAll"));
        Assert.IsFalse(outageHolding.Contains("SentenceSeconds ="));

#if DONJ_STUB_API
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 73;
        player.Model = new GTA.Model("player_zero");
        player.IsDead = false;

        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Enabled = true;
        state.Phase = JusticePhase.Transporting;
        state.SentenceSeconds = 600;
        state.CustodyEpisodeId = "custody:respawn-mask";
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justiceActivePlayerProfileSlot", 0);
        SetFieldValue(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetFieldValue(script, "_justiceCustodyPlayerSlot", 0);
        SetFieldValue(script, "_justiceCustodyRuntimeActive", true);
        SetFieldValue(script, "_justiceCustodyWaitingForRespawn", true);
        SetFieldValue(script, "_justiceCustodyDeathRebindPending", true);
        SetFieldValue(script, "_justiceCustodyDeathStatePersistencePending", true);
        SetFieldValue(script, "_justicePersistenceServicesUnavailable", true);
        SetFieldValue(script, "_justicePersistenceInitializationFailurePermanent", true);

        ulong groundProbe = GetStaticFieldValue<ulong>(
            "JusticeNativeGetGroundZFor3DCoord");
        ulong collisionProbe = GetStaticFieldValue<ulong>(
            "JusticeNativeHasCollisionLoadedAroundEntity");
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == groundProbe || hash == collisionProbe ? (object)true : null;
        try
        {
            InvokeInstance(script, "JusticeUpdateCustody", player, 1000);
        }
        finally
        {
            GTA.StubRuntime.NativeCallHandler = null;
        }
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyPersistenceOutageHoldingEstablished"));
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyContainmentEstablished"));
        Assert.AreEqual(JusticePhase.Transporting, state.Phase);
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.IsTrue((bool)InvokeInstance(
            script,
            "IsInsideJusticeCustody",
            player.Position));
        int fadeOutAfterHolding = GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT);
        int fadeInAfterHolding = GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN);
        Assert.IsTrue(fadeOutAfterHolding >= 1);
        Assert.IsTrue(
            fadeInAfterHolding >= 1,
            "Une corruption définitive doit maintenir le détenu en cellule puis rendre l'écran.");

        InvokeInstance(script, "JusticeUpdateCustody", player, 2000);
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"),
            "Le même détenu déjà dans l'enceinte ne doit pas repasser au noir.");
        Assert.AreEqual(
            fadeOutAfterHolding,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT),
            "Le maintien déjà établi ne doit pas retéléporter ni refondre l'écran.");
        Assert.AreEqual(
            fadeInAfterHolding,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN),
            "Le maintien déjà établi ne doit pas répéter son fade-in.");

        SetFieldValue(script, "_justicePersistenceInitializationFailurePermanent", false);
        SetFieldValue(script, "_justiceCustodyPersistenceOutageHoldingEstablished", false);
        int fadeOutAttempts = 0;
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT &&
                fadeOutAttempts++ == 0)
            {
                throw new InvalidOperationException("fade-out indisponible");
            }
            return null;
        };
        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnMaskNeedsRearm"));
        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.AreEqual(2, fadeOutAttempts);
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnMaskNeedsRearm"));
        GTA.StubRuntime.NativeCallHandler = null;

        SetFieldValue(script, "_justiceCustodyRespawnTransferPending", true);
        player.IsDead = true;
        InvokeInstance(script, "ObserveJusticeCustodyDeath", player, 2500);
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnMaskNeedsRearm"));
        int fadeOutBeforeSecondRespawn = GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT);
        player.IsDead = false;
        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.AreEqual(
            fadeOutBeforeSecondRespawn + 1,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT),
            "Le second respawn vivant doit réaffirmer le masque avant toute persistance.");
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnMaskNeedsRearm"));

        int fadeInAttempts = 0;
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN &&
                fadeInAttempts++ == 0)
            {
                throw new InvalidOperationException("fade-in indisponible");
            }
            return null;
        };
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "TryRestoreJusticeCustodyRespawnTransferMask"));
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnRestorePending"));
        GTA.StubRuntime.NativeCallHandler = null;
        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnRestorePending"));

        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        int fadeInBeforeShutdown = GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN);
        InvokeInstance(script, "JusticeShutdownCustody");
        Assert.IsFalse(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.AreEqual(
            fadeInBeforeShutdown + 1,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN),
            "Un unload pendant un retry doit rendre l'écran avant d'effacer le masque.");
#endif
    }

    [TestMethod]
    public void DeferredFrontObservation_DoesNotConsumeAnUnstoredOwnerEdge()
    {
        string observe = ExecutableMethodBody(
            ReadRuntimeSource(),
            "ObserveJusticeFrontsWhilePersistenceBlocked");
        AssertOrdered(
            observe,
            "if (!TryStoreJusticeDeferredRuntimeFront(",
            "_justiceDamageFrontPrimingPending = true",
            "return;",
            "_justiceDeferredRuntimeLatchOwnerInitialized = true",
            "_justiceWasBeingArrested = arrested",
            "_justiceWasDead = dead",
            "_justiceLastWantedLevel = wantedLevel");
    }

    [TestMethod]
    public void PoliceDeathRespawn_MasksBeforeCustodyJudgementAndWalBackupRotation()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string canMask = ExecutableMethodBody(
            custodySource,
            "CanMaskJusticeCustodyRespawnOrigin");
        AssertOrdered(
            canMask,
            "Entity.Exists(player)",
            "CanMaskJusticePoliceDeathRespawnOrigin(player)",
            "CanRebindJusticeCustodyIdentityAfterInitialRespawn()");

        string deathFrontSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.DeathFront.cs"));
        string applyFront = ExecutableMethodBody(
            deathFrontSource,
            "ApplyJusticeDeathFrontToRuntime");
        AssertOrdered(
            applyFront,
            "ownerIsActive && !arrestFront",
            "_justicePursuitDeathObservedDuringSuspension = true",
            "_justiceSuspendedPursuitDeathPlayerSlot = playerSlot",
            "_justiceSuspendedPursuitDeathPlayerModelHash = playerModel",
            "_justicePoliceDeathRespawnMaskIntentPending = true");

#if DONJ_STUB_API
        GTA.StubRuntime.Reset();
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 84;
        player.Model = new GTA.Model("player_zero");
        player.IsDead = false;

        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Enabled = true;
        state.Phase = JusticePhase.Wanted;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(script, "_justiceActivePlayerProfileSlot", 0);
        SetFieldValue(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetFieldValue(script, "_justicePursuitDeathObservedDuringSuspension", true);
        SetFieldValue(script, "_justiceSuspendedPursuitDeathPlayerSlot", 0);
        SetFieldValue(
            script,
            "_justiceSuspendedPursuitDeathPlayerModelHash",
            player.Model.Hash);
        SetFieldValue(script, "_justicePoliceDeathRespawnMaskIntentPending", true);
        SetFieldValue(script, "_justiceCustodyWaitingForRespawn", false);

        InvokeInstance(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        Assert.IsTrue(GetFieldValue<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.AreEqual(
            1,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_OUT),
            "Le premier ped vivant doit être masqué avant le jugement et les rotations XML.");
        InvokeInstance(script, "JusticeShutdownCustody");
#endif
    }

    [TestMethod]
    public void CustodyTransfer_PersistsTransientPlayerStateBeforeConfiscationAndTeleport()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));

        string writeCustody = ExecutableMethodBody(source, "JusticeWriteCustodyXml");
        StringAssert.Contains(writeCustody, "playerStateStored");
        StringAssert.Contains(writeCustody, "storedInvincible");
        StringAssert.Contains(writeCustody, "storedFrozen");
        StringAssert.Contains(writeCustody, "storedCanRagdoll");

        string transfer = ExecutableMethodBody(source, "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "StoreJusticeCustodyPlayerState(player)",
            "_justiceCustodyTransferPrecommitConfirmed = true",
            "JusticeInventoryPreparationResult inventoryPreparation",
            "EnsureJusticeInventoryReadyForCustodyTransfer(player, now)",
            "inventoryPreparation != JusticeInventoryPreparationResult.Ready",
            "TeleportPlayerWithFadeSafe(player");

        string discipline = ExecutableMethodBody(source, "BeginJusticeCustodyDiscipline");
        AssertOrdered(
            discipline,
            "TryAcquirePlayerInvincibility(",
            "PlayerInvincibilityOwner.JusticeDiscipline",
            "if (!nonLethalProtectionVerified)",
            "_justiceDisciplineInvincibilityRestorePending = true",
            "TryRestoreJusticeDisciplineInvincibility(player)",
            "Hash.TASK_COMBAT_PED");

        string disciplineUpdate = ExecutableMethodBody(source, "UpdateJusticeCustodyDiscipline");
        StringAssert.Contains(
            disciplineUpdate,
            "_justiceDisciplineStoredInvincible = _justiceCustodyPlayerStateStored");
        StringAssert.Contains(disciplineUpdate, "_justiceCustodyStoredInvincible");
    }

    [TestMethod]
    public void CustodyEscapeGrace_ScansDisciplineBeforeEscapeAndRestraintReturnsToCustody()
    {
        string custodySource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(custodySource, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "UpdateJusticeCustodyDiscipline(player, now)",
            "UpdateJusticeCustodyEscape(player, now)");

        string escape = ExecutableMethodBody(custodySource, "UpdateJusticeCustodyEscape");
        AssertOrdered(
            escape,
            "bool insideContainment",
            "if (!_justiceCustodyContainmentEstablished)",
            "if (!insideContainment)",
            "return",
            "_justiceCustodyContainmentEstablished = true",
            "if (insideContainment)",
            "_justiceOutsideCustodySinceAt = 0",
            "_justiceCaseState.Phase = JusticePhase.Escaping",
            "elapsed < JusticeCustodyEscapeGraceMs",
            "return",
            "CompleteJusticeCustodyEscape(player)");
        StringAssert.Contains(
            ExecutableMethodBody(custodySource, "IsInsideJusticeCustodyLayout"),
            "layout.ContainmentVolumes ?? layout.AllowedVolumes");

        string discipline = ExecutableMethodBody(custodySource, "CompleteJusticeCustodyDiscipline");
        AssertOrdered(
            discipline,
            "JusticeRegisterCustodyDisciplineCharge(",
            "_justiceCaseState.Phase == JusticePhase.Escaping",
            "JusticeSignal.Restrained",
            "_justiceDisciplineIntent = null");

        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Escaping,
            CustodyEpisodeId = "custody:grace"
        };
        JusticeTransition transition = JusticePolicy.Transition(state, new JusticeTickInput
        {
            EpisodeId = state.CustodyEpisodeId,
            Signals = JusticeSignal.Restrained
        });
        Assert.AreEqual(JusticePhase.Incarcerated, transition.NextPhase);
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
        Assert.IsFalse(state.IsEscapeChargedForEpisode("custody:grace"));
    }

    [TestMethod]
    public void CustodyReload_ResumesCapturedFineOnlyAndPersistedEscapeIntent()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "JusticePhase.Captured",
            "JusticeBeginCustodyTransfer(false)",
            "HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory)",
            "CompleteJusticeCustodyEscape(player)");
        StringAssert.Contains(
            source,
            "_justiceCaseState.Phase == JusticePhase.Captured ||",
            "Captured doit être une phase de détention reprise, même avec zéro seconde.");
    }

    [TestMethod]
    public void CustodyActivity_RequiresTheScenarioToRemainActiveAfterStartupGrace()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string start = ExecutableMethodBody(source, "StartJusticeCustodyActivity");
        AssertOrdered(
            start,
            "JusticeCustodyActivityScenarioGraceMs",
            "JusticeNativeTaskStartScenarioInPlace");

        string update = ExecutableMethodBody(source, "UpdateJusticeCustodyActivity");
        AssertOrdered(
            update,
            "JusticeNativeIsPedUsingAnyScenario",
            "if (!scenarioActive)",
            "CancelJusticeCustodyActivity(true, now)",
            "_justiceActivityElapsedMs = AdvanceJusticeActivityClock");
    }

    [TestMethod]
    public void CustodyActivity_ClockIsFrameRateIndependentAndFreezesOnlyOnUnknownNativeState()
    {
        const int durationMs = 60000;
        foreach (int framesPerSecond in new[] { 30, 60, 120 })
        {
            int elapsed = 0;
            int lastTick = 0;
            for (int frame = 1; lastTick < durationMs; frame++)
            {
                int now = Math.Min(
                    durationMs,
                    (int)Math.Ceiling(frame * 1000.0 / framesPerSecond));
                elapsed = DonJEnemySpawner.AdvanceJusticeActivityClock(
                    elapsed,
                    now,
                    ref lastTick,
                    durationMs,
                    false);
            }

            Assert.AreEqual(
                durationMs,
                elapsed,
                "Une activité doit finir au même temps de gameplay à " +
                framesPerSecond.ToString(CultureInfo.InvariantCulture) + " FPS.");
        }

        int frozenElapsed = 0;
        int frozenLastTick = 0;
        frozenElapsed = DonJEnemySpawner.AdvanceJusticeActivityClock(
            frozenElapsed,
            1000,
            ref frozenLastTick,
            durationMs,
            false);
        frozenElapsed = DonJEnemySpawner.AdvanceJusticeActivityClock(
            frozenElapsed,
            4000,
            ref frozenLastTick,
            durationMs,
            true);
        frozenElapsed = DonJEnemySpawner.AdvanceJusticeActivityClock(
            frozenElapsed,
            4500,
            ref frozenLastTick,
            durationMs,
            true);
        frozenElapsed = DonJEnemySpawner.AdvanceJusticeActivityClock(
            frozenElapsed,
            5500,
            ref frozenLastTick,
            durationMs,
            false);
        Assert.AreEqual(2000, frozenElapsed);

        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(source, "UpdateJusticeCustodyActivity");
        Assert.AreEqual(
            0,
            Regex.Matches(update, @"_justiceActivityLastTickAt\s*=\s*now").Count,
            "Une sonde de scénario valide ne doit plus consommer la frame courante.");
        StringAssert.Contains(update, "AdvanceJusticeActivityClock");
    }

    [TestMethod]
    public void CustodySnapshot_EnumeratesDlcWeaponsAndFailsClosedOnComponentReadError()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string collect = ExecutableMethodBody(source, "TryCollectJusticeWeaponHashes");
        AssertOrdered(
            collect,
            "Enum.GetValues(typeof(WeaponHash))",
            "JusticeNativeGetNumDlcWeapons",
            "JusticeNativeGetDlcWeaponData",
            "AddJusticeWeaponHashIfUnique");

        string components = ExtractMethodBody(source, "CaptureJusticeWeaponComponents");
        StringAssert.Contains(source, "private bool CaptureJusticeWeaponComponents");
        StringAssert.Contains(components, "return false;");
        StringAssert.Contains(components, "return true;");
        string capture = ExecutableMethodBody(source, "TryCaptureJusticeWeaponSnapshot");
        AssertOrdered(
            capture,
            "if (!CaptureJusticeWeaponComponents(player, item))",
            "return false",
            "candidate.IsValidated = true");

        int clipReadAt = capture.IndexOf("bool clipRead = Function.Call<bool>", StringComparison.Ordinal);
        Assert.IsTrue(clipReadAt >= 0, "La lecture fidèle du chargeur doit être explicite.");
        string clipSection = capture.Substring(clipReadAt);
        AssertOrdered(
            clipSection,
            "if (!clipRead)",
            "return false;",
            "item.AmmoInClip = Math.Max");
    }

    [TestMethod]
    public void CustodyDiscipline_HomicideBypassesCooldownBeforeDeadPedCompaction()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string discipline = ExecutableMethodBody(source, "UpdateJusticeCustodyDiscipline");
        AssertOrdered(
            discipline,
            "TryGetJusticeCustodyMisconduct(player, out crimeKind)",
            "bool homicide",
            "if (!homicide && !JusticeCustodyHasReached(now, _justiceDisciplineCooldownUntil))",
            "BeginJusticeCustodyDiscipline(player, now, crimeKind)");

        string update = ExecutableMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "UpdateJusticeCustodyDiscipline(player, now)",
            "EnsureJusticeCustodyScene(now)");
    }

    [TestMethod]
    public void CustodyFineDebitIntent_IsPersistedAndReconciledBeforeCompletion()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        StringAssert.Contains(source, "FineDebitIntent");
        string resume = ExecutableMethodBody(source, "ResumeJusticeFineDebitIntent");
        AssertOrdered(
            resume,
            "GetCurrentSinglePlayerCashSlotSafe() != intent.Slot",
            "!EnsureJusticeFinancialPreparedSnapshot(\"FineDebit\")",
            "TryReadJusticeSinglePlayerCash(intent.Slot, out cash)",
            "TryArmJusticeFinancialAttempt(",
            "intent.CashWriteResult = TryWriteJusticeSinglePlayerCash(",
            "JusticePolicy.TryRegisterOperation(_justiceCaseState, operation)",
            "bool outcomePersisted =");
    }

    [TestMethod]
    public void CustodyFineDebitIntent_IsResolvedBeforeReloadedDetentionCanAdvance()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = true;
        state.Phase = JusticePhase.Captured;
        state.SentenceSeconds = 240;
        SetFieldValue(script, "_justiceEnabled", true);
        SetFieldValue(
            script,
            "_justiceFineDebitIntent",
            Activator.CreateInstance(GetNestedType("JusticeFineDebitIntent"), true));

        InvokeInstance(script, "NormalizeLoadedJusticeState");
        Assert.AreEqual(
            JusticePhase.Captured,
            state.Phase,
            "Le précommit d'amende doit rester Captured tant que son résultat absolu n'est pas réconcilié.");

        object committedCapture = CreateJusticeHeadlessScript();
        JusticeCaseState committedState = GetFieldValue<JusticeCaseState>(
            committedCapture,
            "_justiceCaseState");
        committedState.Enabled = true;
        committedState.Phase = JusticePhase.Captured;
        committedState.SentenceSeconds = 240;
        committedState.FineDue = 1500L;
        SetFieldValue(committedCapture, "_justiceEnabled", true);

        InvokeInstance(committedCapture, "NormalizeLoadedJusticeState");
        Assert.AreEqual(
            JusticePhase.Captured,
            committedState.Phase,
            "Une capture durable sans intention financière doit reprendre le débit avant l'incarcération.");
        Assert.AreEqual(1500L, committedState.FineDue);

        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string update = ExecutableMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "if (_justiceFineDebitIntent != null)",
            "ResumeJusticeFineDebitIntent()",
            "RestoreJusticeCustodyRuntimeFromCase()",
            "AdvanceJusticeCustodyClock(now)");
    }

    [TestMethod]
    public void CustodyReload_PreservesPendingConfiscationAndWaitsForDisciplineBeforeRelease()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));

        string transfer = ExecutableMethodBody(source, "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "JusticeInventoryPreparationResult inventoryPreparation",
            "EnsureJusticeInventoryReadyForCustodyTransfer(player, now)",
            "inventoryPreparation != JusticeInventoryPreparationResult.Ready",
            "CanContinueJusticeCustodyTransferWithoutInventoryConfiscation(",
            "EnterJusticeNonDestructiveCustodyFallback(player, now)",
            "TeleportPlayerWithFadeSafe(player");
        string fallback = ExecutableMethodBody(
            source,
            "EnterJusticeNonDestructiveCustodyFallback");
        Assert.IsFalse(fallback.Contains("TryRollbackJusticeCustodyTransfer"));
        StringAssert.Contains(fallback, "détention maintenue");

        Type inventoryState = GetNestedType("JusticeInventoryCustodyState");
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
            Enum.GetNames(inventoryState));
        string custodyReader = ExecutableMethodBody(source, "JusticeReadCustodyXml");
        StringAssert.Contains(custodyReader, "inventoryState");
        StringAssert.Contains(custodyReader, "MigrateLegacyJusticeInventoryCustodyState");

        string shutdown = ExecutableMethodBody(source, "JusticeShutdownCustody");
        StringAssert.Contains(shutdown, "RestoreJusticeInventoryProvisionallyOnShutdown(player)");
        StringAssert.Contains(
            shutdown,
            "_justiceWeaponControlsLocked = false",
            "OnAborted doit toujours rendre les contrôles, tandis que l'état inventaire durable garde la reprise.");
        Assert.AreEqual(
            6,
            Regex.Matches(shutdown, @"RunJusticeCustodyShutdownStep\s*\(").Count,
            "Une panne d'un nettoyage ne doit pas empêcher les cinq autres domaines de s'exécuter.");

        string update = ExecutableMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "UpdateJusticeCustodyDiscipline(player, now)",
            "!_justiceDisciplineActive",
            "CompleteJusticeLegalRelease(player)");
    }

    [TestMethod]
    public void InventoryWalAttemptedWithoutDurableResult_UsesDeferredAmbiguousRestore()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(
            script,
            "_justiceCaseState");
        state.Enabled = true;
        state.Phase = JusticePhase.Transporting;
        state.CustodyEpisodeId = "custody:wal-inventory";
        JusticePlayerProfileState[] profiles = ConfigureWalRecoveryProfiles(
            script,
            0,
            new[] { 17L, 0L, 0L });
        SetFieldValue(script, "_justicePersistenceRevision", 17L);
        SetFieldValue(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
        SetEnumField(script, "_justiceInventoryCustodyState", "SnapshotPersisted");
        SetFieldValue(script, "_justiceInventoryRemoved", false);
        SetFieldValue(script, "_justiceWeaponControlsLocked", false);
        SetFieldValue(script, "_justiceDeferredInventoryRestore", false);
        SetFieldValue(script, "_justiceStateDirty", false);

        JusticeWalRecord attempted = CreateInventoryWalRecord(
            JusticeWalState.Attempted,
            0,
            17L,
            17L,
            17L,
            profiles[0].LastCanonicalPlayerModel);

        InvokeInstance(
            script,
            "RecoverJusticeInventoryConfiscationFromWal",
            attempted);

        Assert.AreEqual(
            "RestoreAmbiguous",
            GetFieldValue<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsFalse(GetFieldValue<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(GetFieldValue<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.IsTrue(GetFieldValue<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsTrue(GetFieldValue<bool>(script, "_justiceStateDirty"));
#if DONJ_STUB_API
        GTA.StubRuntime.Reset();
#endif
        Assert.AreEqual(
            "Ready",
            InvokeInstance(
                script,
                "RetryJusticeInventoryConfiscationIfDue",
                (object)null,
                1000).ToString(),
            "La reprise WAL ne doit jamais repasser par RemoveAll.");
#if DONJ_STUB_API
        Assert.AreEqual(
            0,
            GTA.StubRuntime.NativeCalls.Count(call =>
                call.Hash == (ulong)GTA.Native.Hash.REMOVE_ALL_PED_WEAPONS),
            "Aucune native destructive ne doit être rejouée après une reprise ambiguë.");
#endif

        string persistenceSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.Runtime.cs"));
        string recovery = ExecutableMethodBody(
            persistenceSource,
            "RecoverJusticePersistenceFromWalIfRequired");
        AssertOrdered(
            recovery,
            "inventoryConfiscations.Add(candidate)",
            "RecoverJusticeInventoryConfiscationFromWal(");
    }

    [TestMethod]
    public void InventoryWalAmbiguousOnInactiveProfile_RecoversOnlyItsOwner()
    {
        object script = CreateJusticeHeadlessScript();
        JusticePlayerProfileState[] profiles = ConfigureWalRecoveryProfiles(
            script,
            1,
            new[] { 17L, 6L, 3L });
        JusticeCustodyPersistenceSnapshot precommit =
            CreateInventoryCustodyPersistenceSnapshot(0, "SnapshotPersisted");
        profiles[0].CustodySnapshot = precommit;
        SetFieldValue(script, "_justicePersistenceRevision", 17L);
        SetEnumField(script, "_justiceInventoryCustodyState", "UnsupportedPreserved");
        SetFieldValue(script, "_justiceInventoryRemoved", false);
        SetFieldValue(script, "_justiceWeaponControlsLocked", false);
        SetFieldValue(script, "_justiceDeferredInventoryRestore", false);
        SetFieldValue(script, "_justiceStateDirty", false);

        JusticeWalRecord ambiguous = CreateInventoryWalRecord(
            JusticeWalState.Ambiguous,
            0,
            18L,
            17L,
            17L,
            profiles[0].LastCanonicalPlayerModel);

        InvokeInstance(
            script,
            "RecoverJusticeInventoryConfiscationFromWal",
            ambiguous);

        JusticeCustodyPersistenceSnapshot recovered = profiles[0].CustodySnapshot;
        Assert.AreNotSame(precommit, recovered);
        Assert.AreEqual(
            Convert.ToInt32(
                Enum.Parse(GetNestedType("JusticeInventoryCustodyState"), "RestoreAmbiguous"),
                CultureInfo.InvariantCulture),
            recovered.InventoryState);
        Assert.IsFalse(recovered.InventoryRemoved);
        Assert.IsFalse(recovered.WeaponControlsLocked);
        Assert.IsTrue(recovered.DeferredInventoryRestore);
        Assert.IsNotNull(recovered.InventorySnapshot);
        Assert.IsTrue(recovered.InventorySnapshot.IsValidated);
        Assert.AreEqual(string.Empty, profiles[0].CustodyXml);
        Assert.AreEqual(
            "UnsupportedPreserved",
            GetFieldValue<object>(script, "_justiceInventoryCustodyState").ToString(),
            "Le héros joué ne doit recevoir aucun état d'inventaire du profil détenu.");
        Assert.IsFalse(GetFieldValue<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsTrue(GetFieldValue<bool>(script, "_justiceStateDirty"));
    }

    [TestMethod]
    public void InventoryWalWithDurableResult_DoesNotDegradeAnInactiveProfile()
    {
        object script = CreateJusticeHeadlessScript();
        JusticePlayerProfileState[] profiles = ConfigureWalRecoveryProfiles(
            script,
            1,
            new[] { 17L, 6L, 3L });
        JusticeCustodyPersistenceSnapshot durable =
            CreateInventoryCustodyPersistenceSnapshot(0, "RemovedVerified");
        profiles[0].CustodySnapshot = durable;
        SetFieldValue(script, "_justicePersistenceRevision", 18L);
        SetFieldValue(script, "_justiceStateDirty", false);

        JusticeWalRecord ambiguous = CreateInventoryWalRecord(
            JusticeWalState.Ambiguous,
            0,
            18L,
            17L,
            17L,
            profiles[0].LastCanonicalPlayerModel);

        InvokeInstance(
            script,
            "RecoverJusticeInventoryConfiscationFromWal",
            ambiguous);

        Assert.AreSame(durable, profiles[0].CustodySnapshot);
        Assert.IsFalse(GetFieldValue<bool>(script, "_justiceStateDirty"));
    }

    [TestMethod]
    public void InventoryWalIdentityMismatch_IsRejectedBeforeRecovery()
    {
        object script = CreateJusticeHeadlessScript();
        ConfigureWalRecoveryProfiles(script, 0, new[] { 17L, 0L, 0L });
        SetFieldValue(script, "_justicePersistenceRevision", 17L);
        SetFieldValue(script, "_justiceWeaponSnapshot", CreateValidWeaponSnapshot());
        SetEnumField(script, "_justiceInventoryCustodyState", "SnapshotPersisted");

        JusticeWalRecord mismatched = CreateInventoryWalRecord(
            JusticeWalState.Attempted,
            0,
            17L,
            17L,
            17L,
            999999);

        TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
            () => InvokeInstance(
                script,
                "RecoverJusticeInventoryConfiscationFromWal",
                mismatched));
        Assert.IsInstanceOfType(exception.InnerException, typeof(InvalidDataException));
        Assert.AreEqual(
            "SnapshotPersisted",
            GetFieldValue<object>(script, "_justiceInventoryCustodyState").ToString());
    }

    [TestMethod]
    public void AttemptedWal_IsAdvancedOnlyAfterItsResultRevisionIsDurable()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalDurability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            JusticeWalRecord prepared = CreateInventoryWalRecord(
                JusticeWalState.Prepared,
                0,
                17L,
                17L,
                17L,
                1000);
            wal.Append(prepared);
            wal.Append(CreateInventoryWalRecord(
                JusticeWalState.Attempted,
                0,
                17L,
                17L,
                17L,
                1000));
            object script = CreateJusticeHeadlessScript();
            SetFieldValue(script, "_justiceWriteAheadLog", wal);

            InvokeInstance(
                script,
                "MarkAttemptedJusticeWalTransactionsWhoseResultIsDurable",
                17L);
            Assert.AreEqual(
                JusticeWalState.Attempted,
                wal.GetLatest(prepared.TransactionId).State,
                "L'acceptation du snapshot précommit ne prouve aucun résultat post-effet.");

            InvokeInstance(
                script,
                "MarkAttemptedJusticeWalTransactionsWhoseResultIsDurable",
                18L);
            JusticeWalRecord durable = wal.GetLatest(prepared.TransactionId);
            Assert.AreEqual(JusticeWalState.Ambiguous, durable.State);
            Assert.AreEqual(18L, durable.PersistenceRevision);
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
    public void PreparedInventoryWalWithoutEffect_IsRejectedAndCompactableOnRecovery()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalPrepared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "_justice_state.wal"));
            JusticeWalRecord prepared = CreateInventoryWalRecord(
                JusticeWalState.Prepared,
                0,
                17L,
                17L,
                17L,
                1000);
            wal.Append(prepared);
            object script = CreateJusticeHeadlessScript();
            SetFieldValue(script, "_justiceWriteAheadLog", wal);
            SetFieldValue(script, "_justicePersistenceRevision", 17L);

            InvokeInstance(script, "RecoverJusticePersistenceFromWalIfRequired");

            Assert.AreEqual(
                JusticeWalState.Rejected,
                wal.GetLatest(prepared.TransactionId).State);
            Assert.AreEqual(0, wal.GetOpenTransactions().Count);
            Assert.IsTrue(
                wal.CompactIfNoOpenTransactions(),
                "Une frame Prepared fermée avant effet ne doit plus bloquer la compaction.");
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
    public void ObsidianJustice_UsesSevenCategoriesExactActionsAndSafeAmnestyModal()
    {
        Type categoryType = GetNestedType("MenuCategory");
        CollectionAssert.AreEqual(
            new[] { "Npc", "Vehicle", "Object", "Interior", "Scene", "Justice", "Tools" },
            Enum.GetNames(categoryType));

        object menuScript = CreateJusticeHeadlessScript();
        InvokeInstance(menuScript, "EnsureObsidianMenuEntryCache");
        Array pages = GetFieldValue<Array>(menuScript, "_obsidianMenuEntries");
        Assert.AreEqual(7, pages.Length);
        IList justicePage = (IList)pages.GetValue((int)Enum.Parse(categoryType, "Justice"));
        CollectionAssert.AreEqual(
            new[]
            {
                "JusticeEnabled", "JusticeProfile", "JusticeStatus", "JusticeLastCrime", "JusticeSeverity",
                "JusticeWarrant", "JusticeCharges", "JusticeRecord", "JusticeFine", "JusticeFineDispute",
                "JusticePayFine", "JusticeResolveFineDispute", "JusticeSentence", "JusticeRecidivism",
                "JusticePoliceMode", "JusticeRecovery", "JusticeDiagnostic", "JusticeResetProfile"
            },
            justicePage.Cast<object>().Select(entry => GetMemberValue(entry, "Action").ToString()).ToArray());
        Assert.AreEqual(18, justicePage.Count);
        Assert.AreEqual(
            "Normal",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeProfile"),
                "Kind").ToString());
        Assert.AreEqual(
            "Personnage",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeProfile"),
                "Label"));
        Assert.AreEqual(
            "Action",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticePayFine"),
                "Kind").ToString());
        Assert.AreEqual(
            "Payer la dette",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticePayFine"),
                "Label"));
        Assert.AreEqual(
            "Info",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeFineDispute"),
                "Kind").ToString());
        Assert.AreEqual(
            "Danger",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeResolveFineDispute"),
                "Kind").ToString());
        Assert.AreEqual(
            "Normal",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticePoliceMode"),
                "Kind").ToString());
        Assert.AreEqual(
            "Action",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeDiagnostic"),
                "Kind").ToString());
        Assert.AreEqual(
            "Diagnostic Justice",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeDiagnostic"),
                "Label"));
        Assert.AreEqual(
            "Danger",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeResetProfile"),
                "Kind").ToString());
        Assert.AreEqual(
            "Réinitialiser ce personnage",
            GetMemberValue(
                justicePage.Cast<object>().Single(entry =>
                    GetMemberValue(entry, "Action").ToString() == "JusticeResetProfile"),
                "Label"));

        JusticeCaseState state = GetFieldValue<JusticeCaseState>(menuScript, "_justiceCaseState");
        state.Enabled = true;
        state.ActiveScore = 25;
        SetFieldValue(menuScript, "_justiceEnabled", true);
        SetFieldValue(menuScript, "_justiceInitialized", true);

        InvokeInstance(menuScript, "RequestJusticeToggle");
        Assert.AreEqual("JusticeEnabled", GetFieldValue<object>(menuScript, "_pendingDangerAction").ToString());
        Assert.IsTrue(GetFieldValue<bool>(menuScript, "_dangerConfirmationRequiresEnterRelease"));
        Assert.IsTrue((bool)InvokeStatic("IsDangerAction", Enum.Parse(GetNestedType("MainMenuAction"), "JusticeEnabled")));
        Assert.AreEqual(
            "EFFACER LE DOSSIER ACTIF",
            InvokeInstance(menuScript, "DangerActionDisplayName", Enum.Parse(GetNestedType("MainMenuAction"), "JusticeEnabled")));

        object profileAction = Enum.Parse(GetNestedType("MainMenuAction"), "JusticeProfile");
        object policeModeAction = Enum.Parse(GetNestedType("MainMenuAction"), "JusticePoliceMode");
        object resolveDisputeAction = Enum.Parse(GetNestedType("MainMenuAction"), "JusticeResolveFineDispute");
        object resetAction = Enum.Parse(GetNestedType("MainMenuAction"), "JusticeResetProfile");
        Assert.IsTrue((bool)InvokeStatic("IsObsidianValueEditable", profileAction));
        Assert.IsTrue((bool)InvokeStatic("IsObsidianValueEditable", policeModeAction));
        Assert.IsTrue((bool)InvokeStatic("IsDangerAction", resolveDisputeAction));
        Assert.IsTrue((bool)InvokeStatic("IsDangerAction", resetAction));
        Assert.AreEqual(
            "RÉINITIALISER CE PERSONNAGE",
            InvokeInstance(menuScript, "DangerActionDisplayName", resetAction));

        InvokeInstance(menuScript, "RequestDangerConfirmation", resetAction);
        Assert.AreEqual(
            "JusticeResetProfile",
            GetFieldValue<object>(menuScript, "_pendingDangerAction").ToString());
        Assert.IsTrue(GetFieldValue<bool>(menuScript, "_dangerConfirmationRequiresEnterRelease"));
        InvokeInstance(menuScript, "CancelPendingDangerAction");
        Assert.IsNull(GetFieldValue<object>(menuScript, "_pendingDangerAction"));

        InvokeInstance(menuScript, "CancelPendingDangerAction");
        Assert.IsNull(GetFieldValue<object>(menuScript, "_pendingDangerAction"));
        Assert.IsTrue(GetFieldValue<bool>(menuScript, "_justiceEnabled"));
        Assert.AreEqual(25, state.ActiveScore, "Annuler la modale ne doit produire aucune amnistie.");

        List<MethodBase> confirmCalls = ReadCalledMethods(FindMethod("ConfirmPendingDangerAction", PrivateInstance));
        Assert.IsTrue(
            confirmCalls.Any(call => call.Name == "ExecuteJusticeConfirmedAmnestyAndDisable"),
            "La seconde validation stylée doit être le seul chemin vers l'amnistie.");
        Assert.IsTrue(
            confirmCalls.Any(call => call.Name == "ExecuteJusticeConfirmedProfileReset"),
            "La seconde validation stylée doit être le seul chemin vers la réinitialisation du profil.");

        MethodInfo activationMethod = ScriptType
            .GetMethods(PrivateInstance)
            .Single(method =>
                method.Name == "ActivateMainMenuItem" &&
                method.GetParameters().Length == 1);
        List<MethodBase> activationCalls = ReadCalledMethods(activationMethod);
        Assert.IsTrue(
            activationCalls.Any(call => call.Name == "ShowJusticeDiagnosticStatus"),
            "L'action diagnostic doit journaliser et afficher la build réellement chargée.");
        Assert.IsTrue(
            activationCalls.Any(call => call.Name == "RecoverJusticeControlsAndInventoryFromMenu"),
            "La récupération manuelle doit rester accessible dans Justice avancée.");

        string menuSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.MenuUi.cs"));
        string resetModal = ExecutableMethodBody(menuSource, "DrawDangerConfirmation");
        StringAssert.Contains(resetModal, "_pendingDangerJusticeProfileDisplay");
        StringAssert.Contains(resetModal, "_pendingDangerJusticeFineDisplay");
        StringAssert.Contains(resetModal, "Casier, dossier, récidive, dette et détention seront effacés.");
    }

    [TestMethod]
    public void JusticeLedger_ListsEveryActiveAndHistoricalOffenseAndKeepsNavigationBounded()
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        JusticeRecordState record = GetFieldValue<JusticeRecordState>(script, "_justiceRecordState");
        state.Charges.Add(new JusticeCharge
        {
            DisplayName = "Vol de véhicule",
            Kind = JusticeCrimeKind.VehicleTheft,
            Points = 12
        });
        state.Charges.Add(new JusticeCharge
        {
            DisplayName = "Meurtre d'un civil",
            Kind = JusticeCrimeKind.MurderCivilian,
            Points = 75,
            Circumstances = JusticeCircumstances.Armed
        });

        JusticeConviction older = new JusticeConviction
        {
            ConvictionId = "conviction:older",
            JudgedAtUtc = new DateTime(2026, 8, 24, 18, 0, 0, DateTimeKind.Utc),
            Severity = JusticeSeverity.Misdemeanor
        };
        older.Charges.Add(new JusticeConvictionChargeSummary
        {
            DisplayName = "Dégradation de véhicule",
            Kind = JusticeCrimeKind.VehicleDamage,
            Points = 8
        });
        JusticeConviction newest = new JusticeConviction
        {
            ConvictionId = "conviction:newest",
            JudgedAtUtc = new DateTime(2026, 8, 25, 19, 0, 0, DateTimeKind.Utc),
            Severity = JusticeSeverity.Crime
        };
        newest.Charges.Add(new JusticeConvictionChargeSummary
        {
            DisplayName = "Agression aggravée",
            Kind = JusticeCrimeKind.AggravatedAssault,
            Points = 34,
            Circumstances = JusticeCircumstances.VehicleUsedAsWeapon
        });
        newest.Charges.Add(new JusticeConvictionChargeSummary
        {
            DisplayName = "Refus d'obtempérer",
            Kind = JusticeCrimeKind.EvadingPolice,
            Points = 20
        });
        newest.Charges.Add(null);
        record.Convictions.Add(older);
        record.Convictions.Add(newest);
        SetFieldValue(
            script,
            "_mainMenuRememberedActions",
            Array.CreateInstance(
                GetNestedType("MainMenuAction"),
                Enum.GetNames(GetNestedType("MenuCategory")).Length));

        Assert.AreEqual(2, InvokeInstance(script, "GetJusticeLedgerItemCount", false));
        Assert.AreEqual(3, InvokeInstance(script, "GetJusticeLedgerItemCount", true));
        Assert.AreEqual(
            "Meurtre d'un civil",
            GetMemberValue(InvokeInstance(script, "GetJusticeActiveChargeAt", 1), "DisplayName"));

        object[] historyArgs = { 0, null, null };
        Assert.IsTrue((bool)FindMethodByArguments(
            ScriptType,
            "TryGetJusticeRecordOffenseAt",
            PrivateInstance,
            3).Invoke(script, historyArgs));
        Assert.AreEqual("conviction:newest", GetMemberValue(historyArgs[1], "ConvictionId"));
        Assert.AreEqual("Agression aggravée", GetMemberValue(historyArgs[2], "DisplayName"));

        SetEnumField(script, "_mainMenuCategory", "Justice");
        IList justiceEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        int chargesIndex = Enumerable.Range(0, justiceEntries.Count).Single(index =>
            string.Equals(
                GetMemberValue(justiceEntries[index], "Action").ToString(),
                "JusticeCharges",
                StringComparison.Ordinal));
        SetFieldValue(script, "_mainMenuIndex", chargesIndex);
        InvokeInstance(script, "ActivateMainMenuItem", justiceEntries);
        Assert.AreEqual("JusticeCharges", GetFieldValue<object>(script, "_menuPage").ToString());

        int scoreBeforeReadOnlyInput = state.ActiveScore;
        int chargeCountBeforeReadOnlyInput = state.Charges.Count;
        System.Windows.Forms.KeyEventArgs readOnlyEnter =
            new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter);
        InvokeInstance(script, "HandleJusticeLedgerKey", readOnlyEnter);
        Assert.IsTrue(readOnlyEnter.Handled);
        Assert.AreEqual(scoreBeforeReadOnlyInput, state.ActiveScore);
        Assert.AreEqual(chargeCountBeforeReadOnlyInput, state.Charges.Count);

        InvokeInstance(
            script,
            "HandleJusticeLedgerKey",
            new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Escape));
        justiceEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        int recordIndex = Enumerable.Range(0, justiceEntries.Count).Single(index =>
            string.Equals(
                GetMemberValue(justiceEntries[index], "Action").ToString(),
                "JusticeRecord",
                StringComparison.Ordinal));
        SetFieldValue(script, "_mainMenuIndex", recordIndex);
        InvokeInstance(script, "ActivateMainMenuItem", justiceEntries);
        Assert.AreEqual("JusticeRecord", GetFieldValue<object>(script, "_menuPage").ToString());
        InvokeInstance(
            script,
            "HandleJusticeLedgerKey",
            new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.End));
        Assert.AreEqual(2, GetFieldValue<int>(script, "_justiceLedgerIndex"));
        InvokeInstance(
            script,
            "HandleJusticeLedgerKey",
            new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.PageDown));
        Assert.AreEqual(2, GetFieldValue<int>(script, "_justiceLedgerIndex"));
        InvokeInstance(
            script,
            "HandleJusticeLedgerKey",
            new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Escape));
        Assert.AreEqual("Main", GetFieldValue<object>(script, "_menuPage").ToString());
    }

    [TestMethod]
    public void JusticeHud_StabilizesItsDedicatedPoolAndStaysInsideSafeZones()
    {
        object script = CreateJusticeHeadlessScript();
        InitializeEmptyCollectionField(script, "_justiceHudRectanglePool");
        InitializeEmptyCollectionField(script, "_justiceHudTextPool");

        InvokeInstance(script, "PrewarmJusticeHudPools");
        Assert.AreEqual(12, GetFieldValue<ICollection>(script, "_justiceHudRectanglePool").Count);
        Assert.AreEqual(3, GetFieldValue<ICollection>(script, "_justiceHudTextPool").Count);
        InvokeInstance(script, "PrewarmJusticeHudPools");
        Assert.AreEqual(12, GetFieldValue<ICollection>(script, "_justiceHudRectanglePool").Count);
        Assert.AreEqual(3, GetFieldValue<ICollection>(script, "_justiceHudTextPool").Count);

        int[,] resolutions =
        {
            { 1280, 720 },
            { 1920, 1200 },
            { 2560, 1080 },
            { 3840, 2160 }
        };
        float[] safeZones = { 0.80f, 0.90f, 1.0f };
        foreach (int row in Enumerable.Range(0, resolutions.GetLength(0)))
        {
            foreach (float safe in safeZones)
            {
                AssertJusticeHudInsideSafeZone(resolutions[row, 0], resolutions[row, 1], safe);
            }
        }
    }

    private static JusticeIncident NewUnconfirmedIncident(string id, long createdAtMs, bool plausibleObserver)
    {
        return new JusticeIncident
        {
            IncidentId = id,
            EpisodeId = "episode:" + id,
            Kind = JusticeCrimeKind.VehicleTheft,
            CreatedAtMs = createdAtMs,
            ExpiresAtMs = createdAtMs + JusticePolicy.PendingIncidentLifetimeMs,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.None,
                HasPlausibleObserver = plausibleObserver,
                ObservedAtMs = createdAtMs,
                ReportDueAtMs = createdAtMs + JusticePolicy.CivilianReportDelayMs
            }
        };
    }

    private static JusticeIncident NewConfirmedBatchIncident(
        string id,
        string episodeId,
        string batchId,
        int victimHandle,
        int victimGeneration)
    {
        const long confirmedAtMs = 4200L;
        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = id,
            EpisodeId = episodeId,
            DetectionBatchId = batchId,
            Kind = JusticeCrimeKind.SimpleAssault,
            VictimHandle = victimHandle,
            VictimGeneration = victimGeneration,
            CreatedAtMs = confirmedAtMs,
            ExpiresAtMs = confirmedAtMs + JusticePolicy.PendingIncidentLifetimeMs,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.PoliceWitness,
                WitnessHandle = 999,
                WitnessGeneration = 1,
                HasPlausibleObserver = true,
                ObservedAtMs = confirmedAtMs,
                ReportDueAtMs = confirmedAtMs
            }
        };
        Assert.IsTrue(incident.TryConfirm(confirmedAtMs, true));
        return incident;
    }

    private static object CreatePendingRuntimeIncident(JusticeIncident incident)
    {
        object pending = Activator.CreateInstance(GetNestedType("JusticePendingRuntimeIncident"), true);
        SetMemberValue(pending, "Incident", incident);
        return pending;
    }

    private static object CreateRuntimeWitness(JusticeEvidenceKind kind, long reportDueAtMs)
    {
        object witness = Activator.CreateInstance(GetNestedType("JusticeRuntimeWitness"), true);
        SetMemberValue(witness, "Kind", kind);
        SetMemberValue(witness, "ReportDueAtMs", reportDueAtMs);
        return witness;
    }

    private static object CreateJusticeHeadlessScript()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetFieldValue(script, "_justiceCaseState", new JusticeCaseState { Enabled = false });
        SetFieldValue(script, "_justiceRecordState", new JusticeRecordState());

        string[] collectionFields =
        {
            "_justicePendingIncidents",
            "_justiceRecentVictims",
            "_justiceRecentVehicles",
            "_justiceAllyTokens",
            "_justiceTrackedIdentities",
            "_justiceSelfDefenseUntilByVictim",
            "_justiceDamageFrontsToConsume",
            "_justiceDamagePairBaselines",
            "_justiceWitnessSnapshots",
            "_justiceCustodyGuards",
            "_justiceCustodyInmates",
            "_justiceActivityCooldownUntil",
            "_justiceLoadedActivityCooldownSeconds"
        };
        foreach (string field in collectionFields)
        {
            InitializeEmptyCollectionField(script, field);
        }

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

    private static void PopulatePersistedJusticeState(JusticeCaseState caseState, JusticeRecordState record)
    {
        caseState.Enabled = true;
        caseState.ActiveScore = 87;
        caseState.FineDue = 4321L;
        caseState.SentenceSeconds = 720;
        caseState.HasWarrant = false;
        caseState.Phase = JusticePhase.Incarcerated;
        caseState.WantedEpisodeId = "pursuit:one";
        caseState.CustodyEpisodeId = "custody:one";
        caseState.LastCrimeKind = JusticeCrimeKind.MurderOfficer;
        caseState.LastCrimeLabel = "Meurtre d'un agent";
        caseState.ProcessedIncidentIds.Add("incident:one");
        caseState.CompletedOperationIds.Add(
            JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, "custody:one"));
        caseState.CompletedOperationIds.Add(
            JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyConviction, "custody:one"));
        for (int index = 1; index < 140; index++)
        {
            caseState.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
                JusticeOperationKind.ApplyWantedFloor,
                "persisted:" + index.ToString(CultureInfo.InvariantCulture)));
        }
        caseState.FleeingChargedEpisodeIds.Add("pursuit:one");
        caseState.EscapeChargedEpisodeIds.Add("custody:one");
        JusticeCharge charge = new JusticeCharge
        {
            ChargeId = "charge:one",
            IncidentId = "incident:one",
            EpisodeId = "pursuit:one",
            Kind = JusticeCrimeKind.MurderOfficer,
            DisplayName = "Meurtre d'un agent",
            VictimHandle = 700,
            VictimGeneration = 3,
            Points = 87,
            Fine = 4321L,
            SentenceSeconds = 720,
            IsAlliedAction = true,
            AdditionalVictimCount = 2,
            Circumstances = JusticeCircumstances.OrganizedBand,
            IsAdjudicated = true
        };
        charge.AddAlliedContributor(701, 41);
        charge.AddAlliedContributor(701, 42);
        caseState.Charges.Add(charge);

        record.RecidivismIndex = 28;
        record.CleanGameplaySeconds = 0;
        record.AppliedCleanDecay = 0;
        record.AppliedConvictionIds.Add("conviction:custody:one");
        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = "conviction:custody:one",
            JudgedAtUtc = new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc),
            Severity = JusticeSeverity.Major,
            Score = 87,
            Fine = 4321L,
            SentenceSeconds = 720
        };
        conviction.Charges.Add(new JusticeConvictionChargeSummary
        {
            Kind = JusticeCrimeKind.MurderOfficer,
            DisplayName = "Meurtre d'un agent",
            Points = 87,
            Fine = 4321L,
            SentenceSeconds = 720,
            Circumstances = JusticeCircumstances.Armed |
                            JusticeCircumstances.VehicleUsedAsWeapon
        });
        record.Convictions.Add(conviction);
    }

    private static JusticeRecordState BuildMaximumJusticeRecord(int slot)
    {
        JusticeRecordState record = new JusticeRecordState();
        string label = "Infraction confirmée · registre borné " + new string('D', 64);
        for (int convictionIndex = 0;
             convictionIndex < JusticePolicy.MaxConvictions;
             convictionIndex++)
        {
            string convictionId = "conviction:max-ledger:" +
                slot.ToString(CultureInfo.InvariantCulture) + ":" +
                convictionIndex.ToString(CultureInfo.InvariantCulture);
            JusticeConviction conviction = new JusticeConviction
            {
                ConvictionId = convictionId,
                JudgedAtUtc = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
                    .AddMinutes(convictionIndex),
                Severity = JusticeSeverity.Critical,
                Score = JusticePolicy.MaxActiveCharges,
                Fine = JusticePolicy.MaxActiveCharges * 50L,
                SentenceSeconds = 0
            };
            for (int chargeIndex = 0;
                 chargeIndex < JusticePolicy.MaxActiveCharges;
                 chargeIndex++)
            {
                conviction.Charges.Add(new JusticeConvictionChargeSummary
                {
                    Kind = JusticeCrimeKind.ReportedViolentAct,
                    DisplayName = label,
                    Points = 1,
                    Fine = 50L,
                    SentenceSeconds = 0,
                    Circumstances = JusticeCircumstances.Armed,
                    CircumstancesWerePersisted = true,
                    IsAggregate = false,
                    AggregatedChargeCount = 0
                });
            }
            record.Convictions.Add(conviction);
            record.AppliedConvictionIds.Add(convictionId);
        }
        return record;
    }

    private static object CreateValidWeaponSnapshot()
    {
        object snapshot = Activator.CreateInstance(GetNestedType("JusticeWeaponSnapshot"), true);
        SetMemberValue(snapshot, "IsValidated", true);
        SetMemberValue(snapshot, "SelectedWeaponHash", 12345);
        ((IList)GetMemberValue(snapshot, "Weapons")).Add(
            CreateWeaponSnapshotItem(12345, 50, 12, 1, new[] { 777 }));
        return snapshot;
    }

    private static JusticePlayerProfileState[] ConfigureWalRecoveryProfiles(
        object script,
        int activeSlot,
        long[] generations)
    {
        Assert.IsNotNull(script);
        Assert.IsNotNull(generations);
        Assert.AreEqual(3, generations.Length);
        JusticeCaseState activeCase = GetFieldValue<JusticeCaseState>(
            script,
            "_justiceCaseState");
        JusticeRecordState activeRecord = GetFieldValue<JusticeRecordState>(
            script,
            "_justiceRecordState");
        JusticePlayerProfileState[] profiles = new JusticePlayerProfileState[3];
        for (int slot = 0; slot < profiles.Length; slot++)
        {
            profiles[slot] = new JusticePlayerProfileState(slot)
            {
                CaseState = slot == activeSlot
                    ? activeCase
                    : new JusticeCaseState(),
                RecordState = slot == activeSlot
                    ? activeRecord
                    : new JusticeRecordState(),
                CustodyXml = (string)InvokeStatic(
                    "CreateCanonicalEmptyJusticeCustodyXml"),
                LastCanonicalPlayerModel = 1000 + slot
            };
        }

        SetFieldValue(script, "_justicePlayerProfiles", profiles);
        SetFieldValue(script, "_justiceActivePlayerProfileSlot", activeSlot);
        SetFieldValue(script, "_justiceProfilePersistenceGenerations", generations);
        return profiles;
    }

    private static JusticeWalRecord CreateInventoryWalRecord(
        JusticeWalState state,
        int profileSlot,
        long walRevision,
        long snapshotRevision,
        long profileGeneration,
        int profileModel)
    {
        return new JusticeWalRecord(
            "critical:" + profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":InventoryConfiscation:" +
            snapshotRevision.ToString(CultureInfo.InvariantCulture),
            "Inventory",
            profileSlot,
            state,
            walRevision,
            1L,
            new[]
            {
                new JusticePersistenceField(
                    "snapshotRevision",
                    snapshotRevision.ToString(CultureInfo.InvariantCulture)),
                new JusticePersistenceField(
                    "profileGeneration",
                    profileGeneration.ToString(CultureInfo.InvariantCulture)),
                new JusticePersistenceField(
                    "identityKey",
                    "slot:" + profileSlot.ToString(CultureInfo.InvariantCulture) +
                    ":model:" + profileModel.ToString(CultureInfo.InvariantCulture)),
                new JusticePersistenceField("boundary", "InventoryConfiscation"),
                new JusticePersistenceField(
                    "schemaMajor",
                    JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                        CultureInfo.InvariantCulture))
            });
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateInventoryCustodyPersistenceSnapshot(int playerSlot, string inventoryState)
    {
        int state = Convert.ToInt32(
            Enum.Parse(GetNestedType("JusticeInventoryCustodyState"), inventoryState),
            CultureInfo.InvariantCulture);
        bool removed = string.Equals(
            inventoryState,
            "RemovedVerified",
            StringComparison.Ordinal);
        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                12345,
                new[]
                {
                    new JusticeWeaponPersistenceSnapshot(
                        12345,
                        50,
                        12,
                        1,
                        new[] { 777 })
                });
        return new JusticeCustodyPersistenceSnapshot(
            true,
            2,
            false,
            false,
            600,
            0,
            removed,
            false,
            state,
            0,
            0,
            false,
            false,
            false,
            true,
            false,
            false,
            true,
            1000 + playerSlot,
            playerSlot,
            12345,
            false,
            false,
            null,
            null,
            null,
            inventory,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static JusticeIncident CreateConfirmedDirectIncident(
        JusticeCrimeKind kind,
        string incidentId,
        string episodeId,
        JusticeCircumstances circumstances)
    {
        return new JusticeIncident
        {
            IncidentId = incidentId,
            EpisodeId = episodeId,
            Kind = kind,
            CreatedAtMs = 1000L,
            ExpiresAtMs = 7000L,
            Circumstances = circumstances,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ObservedAtMs = 1000L,
                ReportDueAtMs = 1000L
            },
            IsConfirmed = true
        };
    }

    private static object CreateWeaponSnapshotItem(int weaponHash, int ammo, int clip, int tint, int[] components)
    {
        object item = Activator.CreateInstance(GetNestedType("JusticeWeaponSnapshotItem"), true);
        SetMemberValue(item, "WeaponHash", weaponHash);
        SetMemberValue(item, "Ammo", ammo);
        SetMemberValue(item, "AmmoInClip", clip);
        SetMemberValue(item, "Tint", tint);
        IList hashes = (IList)GetMemberValue(item, "ComponentHashes");
        foreach (int component in components)
        {
            hashes.Add(component);
        }
        return item;
    }

    private static void AssertCustodyLayout(
        object layout,
        string expectedSite,
        int expectedGuards,
        int expectedInmates,
        int expectedMaximumReduction,
        Tuple<string, int, int>[] expectedActivities)
    {
        Assert.AreEqual(expectedSite, GetMemberValue(layout, "Site").ToString());
        Array volumes = (Array)GetMemberValue(layout, "AllowedVolumes");
        Array containmentVolumes = (Array)GetMemberValue(layout, "ContainmentVolumes");
        Array guards = (Array)GetMemberValue(layout, "GuardPositions");
        Array inmates = (Array)GetMemberValue(layout, "InmatePositions");
        Array activities = (Array)GetMemberValue(layout, "Activities");
        Assert.IsTrue(volumes.Length >= 1);
        Assert.IsTrue(containmentVolumes.Length >= 1);
        Assert.AreEqual(expectedGuards, guards.Length);
        Assert.AreEqual(expectedInmates, inmates.Length);
        Assert.AreEqual(expectedMaximumReduction, GetMemberValue(layout, "MaximumActivityReductionSeconds"));
        Assert.AreEqual(expectedActivities.Length, activities.Length);

        Vector3 arrival = (Vector3)GetMemberValue(layout, "ArrivalPosition");
        Vector3 cell = (Vector3)GetMemberValue(layout, "CellPosition");
        Vector3 release = (Vector3)GetMemberValue(layout, "ReleasePosition");
        Assert.IsTrue(volumes.Cast<object>().Any(volume => (bool)InvokeObjectInstance(volume, "Contains", arrival)));
        Assert.IsTrue(volumes.Cast<object>().Any(volume => (bool)InvokeObjectInstance(volume, "Contains", cell)));
        Assert.IsTrue(containmentVolumes.Cast<object>().Any(
            volume => (bool)InvokeObjectInstance(volume, "Contains", arrival)));
        Assert.IsTrue(containmentVolumes.Cast<object>().Any(
            volume => (bool)InvokeObjectInstance(volume, "Contains", cell)));
        Assert.IsFalse(containmentVolumes.Cast<object>().Any(
            volume => (bool)InvokeObjectInstance(volume, "Contains", release)));

        for (int index = 0; index < expectedActivities.Length; index++)
        {
            object activity = activities.GetValue(index);
            Assert.AreEqual(expectedActivities[index].Item1, GetMemberValue(activity, "Id"));
            Assert.AreEqual(expectedActivities[index].Item2, GetMemberValue(activity, "DurationSeconds"));
            Assert.AreEqual(expectedActivities[index].Item3, GetMemberValue(activity, "ReductionSeconds"));
            Vector3 position = (Vector3)GetMemberValue(activity, "Position");
            Assert.IsTrue(
                volumes.Cast<object>().Any(volume => (bool)InvokeObjectInstance(volume, "Contains", position)),
                expectedActivities[index].Item1 + " doit rester dans un volume autorisé.");
        }
    }

    private static void AssertFineConversion(int initialSeconds, long unpaidFine, bool stationPlanned, int expectedSeconds)
    {
        object script = CreateJusticeHeadlessScript();
        JusticeCaseState state = GetFieldValue<JusticeCaseState>(script, "_justiceCaseState");
        state.SentenceSeconds = initialSeconds;
        InvokeInstance(script, "AddJusticeFineConversionTime", unpaidFine, stationPlanned);
        Assert.AreEqual(expectedSeconds, state.SentenceSeconds);
    }

    private static void AssertJusticeHudInsideSafeZone(int width, int height, float safeZone)
    {
        object viewport = InvokeStatic("CalculateMenuViewport", width, height, safeZone);
        float logicalWidth = Convert.ToSingle(GetMemberValue(viewport, "LogicalWidth"));
        float safeWidth = Convert.ToSingle(GetMemberValue(viewport, "SafeLogicalWidth"));
        float safeHeight = Convert.ToSingle(GetMemberValue(viewport, "SafeLogicalHeight"));
        float safeLeft = Convert.ToSingle(GetMemberValue(viewport, "SafeLeft"));
        float safeTop = Convert.ToSingle(GetMemberValue(viewport, "SafeTop"));
        float xFactor = 1280.0f / logicalWidth;
        Rectangle safeBounds = (Rectangle)InvokeStatic(
            "LogicalRectangleToUi",
            safeLeft,
            safeTop,
            safeWidth,
            safeHeight,
            xFactor);
        Rectangle hud = (Rectangle)InvokeStatic(
            "LogicalRectangleToUi",
            safeLeft + 12.0f,
            safeTop + 12.0f,
            Math.Min(GetStaticFieldValue<int>("JusticeHudLogicalWidth"), safeWidth - 24.0f),
            GetStaticFieldValue<int>("JusticeHudLogicalHeight"),
            xFactor);

        Assert.IsTrue(hud.Width > 0 && hud.Height > 0);
        Assert.IsTrue(hud.Left >= safeBounds.Left);
        Assert.IsTrue(hud.Top >= safeBounds.Top);
        Assert.IsTrue(hud.Right <= safeBounds.Right + 1);
        Assert.IsTrue(hud.Bottom <= safeBounds.Bottom + 1);
    }

    private static void WithTemporarySaveDirectory(Action<string> action)
    {
        string previous = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string directory = Path.Combine(Path.GetTempPath(), "DonJJusticeTests-" + Guid.NewGuid().ToString("N"));
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
            if (fullDirectory.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullDirectory))
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
            new XAttribute("policeIntegrationMode", (string)recovery.Attribute("policeIntegrationMode") ?? "1"),
            new XAttribute("activePlayerSlot", (string)recovery.Attribute("activePlayerSlot")),
            new XAttribute("nextIdentityGeneration", (string)recovery.Attribute("nextIdentityGeneration") ?? "0"),
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

    private static string ReadRuntimeSource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
    }

    private static string ExecutableMethodBody(string source, string methodName)
    {
        return StripSourceComments(ExtractMethodBody(source, methodName));
    }

    private static string StripSourceComments(string source)
    {
        return Regex.Replace(
            source ?? string.Empty,
            @"/\*.*?\*/|//[^\r\n]*",
            string.Empty,
            RegexOptions.Singleline);
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

    private static List<MethodBase> ReadCalledMethods(MethodInfo method)
    {
        var result = new List<MethodBase>();
        MethodBody body = method.GetMethodBody();
        if (body == null)
        {
            return result;
        }

        byte[] il = body.GetILAsByteArray();
        int position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[position++]));
            }

            OpCode opCode;
            Assert.IsTrue(OpCodesByValue.TryGetValue(value, out opCode), "Opcode IL inconnu : " + value);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, position);
                try
                {
                    result.Add(method.Module.ResolveMethod(
                        token,
                        method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                        method.GetGenericArguments()));
                }
                catch (ArgumentException)
                {
                }
            }
            position += OperandSize(opCode.OperandType, il, position);
        }
        return result;
    }

    private static int OperandSize(OperandType operandType, byte[] il, int position)
    {
        switch (operandType)
        {
            case OperandType.InlineNone: return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar: return 1;
            case OperandType.InlineVar: return 2;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR: return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR: return 8;
            case OperandType.InlineSwitch:
                int count = BitConverter.ToInt32(il, position);
                return 4 + count * 4;
            default:
                throw new InvalidOperationException("OperandType IL non géré : " + operandType);
        }
    }

    private static void InitializeEmptyCollectionField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Collection privée introuvable : " + fieldName);
        field.SetValue(target, Activator.CreateInstance(field.FieldType, true));
    }

    private static void SetEnumField(object target, string fieldName, string value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Enum privé introuvable : " + fieldName);
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static MethodInfo FindMethod(string methodName, BindingFlags flags)
    {
        MethodInfo method = ScriptType.GetMethods(flags).SingleOrDefault(candidate => candidate.Name == methodName);
        Assert.IsNotNull(method, "Méthode privée introuvable : " + methodName);
        return method;
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        MethodInfo method = FindMethodByArguments(ScriptType, methodName, PrivateStatic, args.Length);
        return method.Invoke(null, args);
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        MethodInfo method = FindMethodByArguments(target.GetType(), methodName, PrivateInstance, args.Length);
        return method.Invoke(target, args);
    }

    private static void FlushAndAwait(object script)
    {
        bool accepted = (bool)InvokeInstance(script, "JusticeFlushStateNow");
        Assert.IsTrue(
            accepted,
            "Le snapshot doit être accepté par le repository. Détail : " +
            GetFieldValue<string>(script, "_justicePersistenceLastError"));
        AwaitQueuedPersistence(script);
    }

    private static void AwaitQueuedPersistence(object script)
    {
        Assert.IsTrue(
            (bool)InvokeInstance(script, "JusticeAwaitQueuedPersistenceForTests"),
            "La barrière réservée aux tests doit confirmer la révision sur disque.");
    }

    private static object InvokeObjectInstance(object target, string methodName, params object[] args)
    {
        MethodInfo method = FindMethodByArguments(
            target.GetType(),
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            args.Length);
        return method.Invoke(target, args);
    }

    private static MethodInfo FindMethodByArguments(Type type, string methodName, BindingFlags flags, int argumentCount)
    {
        MethodInfo[] matches = type.GetMethods(flags)
            .Where(method => method.Name == methodName && method.GetParameters().Length == argumentCount)
            .ToArray();
        Assert.AreEqual(1, matches.Length, methodName + " doit avoir une surcharge unique avec " + argumentCount + " argument(s).");
        return matches[0];
    }

    private static Type GetNestedType(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type privé introuvable : " + name);
        return type;
    }

    private static T GetStaticFieldValue<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, "Champ statique privé introuvable : " + fieldName);
        object value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return (T)value;
    }

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetFieldValue(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
