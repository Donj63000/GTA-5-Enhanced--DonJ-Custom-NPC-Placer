#if DONJ_STUB_API
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DonJ.JusticeRecognition;
using GTA;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeRecognitionRuntimeTests
{
    private string _criticalIntentDirectory;
    private string _criticalIntentPath;

    [TestInitialize]
    public void Initialize()
    {
        // Je repars d'un runtime GTA et d'un pont statique totalement neutres.
        StubRuntime.Reset();
        ResetBridgeState();
        _criticalIntentDirectory = Path.Combine(
            Path.GetTempPath(),
            "DonJRecognitionBridgeTests_" + Guid.NewGuid().ToString("N"));
        _criticalIntentPath = Path.Combine(
            _criticalIntentDirectory,
            "critical-intents.xml");
        JusticeRecognitionBridge.ConfigureCriticalIntentJournalForTests(
            _criticalIntentPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        JusticeRecognitionBridge.UnbindWantedMinimum();
        ResetBridgeState();
        StubRuntime.Reset();

        if (!string.IsNullOrWhiteSpace(_criticalIntentDirectory) &&
            Directory.Exists(_criticalIntentDirectory))
        {
            Directory.Delete(_criticalIntentDirectory, true);
        }
    }

    [TestMethod]
    public void SuccessfulEscapeFinalization_CreatesPlateOutfitAppearanceAndSearchZoneWithTheStabilizationContract()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                0,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 101);
            VehicleSignatureData vehicle = VehicleSignature(501, "ESCAPE4");
            OutfitSignatureData outfit = OutfitSignature(player.Model.Hash);
            AppearanceSignatureData appearance = AppearanceSignature(player.Model.Hash);

            PursuitEpisodeRuntime episode =
                new PursuitEpisodeRuntime
                {
                    EpisodeId = 18,
                    PeakWantedLevel = 4,
                    StartedAtGameTime = 100,
                    LastKnownPosition = new PositionData
                    {
                        X = 125.0f,
                        Y = -340.0f,
                        Z = 28.0f
                    },
                    LastVehicle = vehicle,
                    LastOutfit = outfit,
                    LastAppearance = appearance
                };
            harness.Profile.LastEpisodeId = 18;

            Invoke(
                harness.Script,
                "CreateEvidenceFromSuccessfulEscape",
                episode,
                player,
                1900,
                nowUtc);

            Assert.AreEqual(
                1,
                harness.Profile.VehicleEvidence.Count,
                harness.ReadLog());
            Assert.AreEqual(
                1,
                harness.Profile.OutfitEvidence.Count,
                harness.ReadLog());

            VehicleEvidenceState vehicleEvidence = harness.Profile.VehicleEvidence[0];
            OutfitEvidenceState outfitEvidence = harness.Profile.OutfitEvidence[0];
            Assert.AreEqual(4, vehicleEvidence.WantedFloor);
            Assert.AreEqual("ESCAPE4", vehicleEvidence.Signature.NormalizedPlate);
            Assert.AreEqual(
                nowUtc.AddSeconds(RecognitionPolicy.GetVehicleEvidenceDurationSeconds(4)),
                vehicleEvidence.ExpiresUtc);
            Assert.AreEqual(4, outfitEvidence.WantedFloor);
            Assert.AreEqual(
                nowUtc.AddSeconds(RecognitionPolicy.GetOutfitEvidenceDurationSeconds(4)),
                outfitEvidence.ExpiresUtc);

            Assert.IsTrue(harness.Profile.AppearanceEvidence.Active);
            Assert.AreEqual(18L, harness.Profile.AppearanceEvidence.SourceEpisodeId);
            Assert.AreEqual(player.Model.Hash, harness.Profile.AppearanceEvidence.Signature.PedModelHash);

            SearchZoneState zone = harness.Profile.SearchZone;
            Assert.IsTrue(zone.Active);
            Assert.AreEqual(18L, zone.SourceEpisodeId);
            Assert.AreEqual(4, zone.WantedFloor);
            Assert.AreEqual(900.0f, zone.Radius, 0.001f);
            Assert.AreEqual(125.0f, zone.Center.X, 0.001f);
            Assert.AreEqual(-340.0f, zone.Center.Y, 0.001f);
            Assert.AreEqual(
                nowUtc.AddSeconds(RecognitionPolicy.GetZoneDurationSeconds(4)),
                zone.ExpiresUtc);
            Assert.AreEqual(
                nowUtc.AddSeconds(RecognitionPolicy.ZoneGraceSeconds),
                zone.GraceUntilUtc);

            Assert.AreEqual(
                900,
                GetPrivateConstant<int>("WantedLossStabilizationMilliseconds"));
            string transition = ExtractRecognitionMethod("ProcessWantedState");
            AssertContainsInOrder(
                transition,
                "FinalizeWantedLoss(",
                "BeginPendingWantedLoss(");
            string finalization = ExtractRecognitionMethod("FinalizeWantedLoss");
            AssertContainsInOrder(
                finalization,
                "pending.Suppressed",
                "IsPlayerDead(playerPed)",
                "CreateEvidenceFromSuccessfulEscape(",
                "_currentEpisode = null;");
        }
    }

    [TestMethod]
    public void DeferredRecognition_RestoresTheRememberedFloorOnceAndNeverLowersAHigherWanted()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                10,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 102);
            VehicleSignatureData currentVehicle = VehicleSignature(90210, "RECO123");
            OutfitSignatureData currentOutfit = OutfitSignature(player.Model.Hash);
            harness.Profile.VehicleEvidence.Add(
                new VehicleEvidenceState
                {
                    Active = true,
                    SourceEpisodeId = 70,
                    WantedFloor = 4,
                    CreatedUtc = nowUtc.AddMinutes(-1),
                    ExpiresUtc = nowUtc.AddMinutes(20),
                    Signature = VehicleSignature(90210, "RECO123")
                });
            harness.Profile.OutfitEvidence.Add(
                new OutfitEvidenceState
                {
                    Active = true,
                    SourceEpisodeId = 70,
                    WantedFloor = 3,
                    CreatedUtc = nowUtc.AddMinutes(-1),
                    ExpiresUtc = nowUtc.AddMinutes(20),
                    Signature = OutfitSignature(player.Model.Hash)
                });

            object[] vehicleFloorArguments =
            {
                currentVehicle,
                nowUtc,
                true,
                false
            };
            int vehicleFloor = (int)Invoke(
                harness.Script,
                "GetMatchingVehicleWantedFloor",
                vehicleFloorArguments);
            int outfitFloor = (int)Invoke(
                harness.Script,
                "GetMatchingOutfitWantedFloor",
                currentOutfit,
                nowUtc);
            Assert.AreEqual(4, vehicleFloor);
            Assert.AreEqual(3, outfitFloor);
            Assert.IsFalse((bool)vehicleFloorArguments[3]);

            Game.Player.WantedLevel = 1;
            Assert.IsFalse(
                (bool)Invoke(
                    harness.Script,
                    "ApplyWantedMinimum",
                    4,
                    "setter absent",
                    219),
                "Le module intégré refuse toute écriture native si le setter Justice est absent.");
            Assert.AreEqual(1, Game.Player.WantedLevel);

            int callCount = 0;
            int requestedFloor = 0;
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    callCount++;
                    requestedFloor = level;
                    Game.Player.WantedLevel = level;
                    return true;
                });
            Game.Player.WantedLevel = 1;

            Assert.IsTrue(
                (bool)Invoke(
                    harness.Script,
                    "ApplyWantedMinimum",
                    Math.Max(vehicleFloor, outfitFloor),
                    "plaque et tenue reconnues",
                    220));

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(4, requestedFloor);
            Assert.AreEqual(4, Game.Player.WantedLevel);
            Assert.AreEqual(
                4,
                GetField<int>(harness.Script, "_lastReliableWantedLevel"),
                "Le setter confirmé devient immédiatement le dernier wanted fiable.");

            Assert.IsFalse(
                (bool)Invoke(
                    harness.Script,
                    "ApplyWantedMinimum",
                    4,
                    "reconnaissance déjà consommée",
                    221));
            Assert.AreEqual(
                1,
                callCount,
                "Une reconnaissance consommée ne doit jamais écrire le wanted une seconde fois.");

            Game.Player.WantedLevel = 5;
            Assert.IsFalse(
                (bool)Invoke(
                    harness.Script,
                    "ApplyWantedMinimum",
                    4,
                    "plancher inférieur",
                    300));

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(
                5,
                Game.Player.WantedLevel,
                "Le plancher mémorisé ne doit jamais diminuer un wanted GTA déjà supérieur.");

            Assert.AreEqual(
                220,
                GetPrivateConstant<int>("WantedTransitionStabilizationMilliseconds"));
            string deferred = ExtractRecognitionMethod("ProcessPendingWantedEscalation");
            AssertContainsInOrder(
                deferred,
                "nowGameTime <",
                "EvaluateAtGameTime",
                "_pendingWantedEscalation = null;",
                "Math.Max(",
                "targetWanted <= currentWanted",
                "ApplyWantedMinimum(");
            StringAssert.Contains(deferred, "attemptCount < 2");
        }
    }

    [TestMethod]
    public void WantedScheduler_FourStarEscapeThenNewCrime_RestoresFourStarsAfterStabilization()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                15,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 112);

            Game.Player.WantedLevel = 4;
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                4,
                100,
                nowUtc);

            PursuitEpisodeRuntime firstEpisode =
                GetField<PursuitEpisodeRuntime>(harness.Script, "_currentEpisode");
            Assert.IsNotNull(firstEpisode);
            Assert.AreEqual(4, firstEpisode.PeakWantedLevel);

            Game.Player.WantedLevel = 0;
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                200,
                nowUtc);
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                1100,
                nowUtc);

            Assert.IsNull(GetField<object>(harness.Script, "_currentEpisode"));
            Assert.AreEqual(1, harness.Profile.OutfitEvidence.Count);
            Assert.AreEqual(4, harness.Profile.OutfitEvidence[0].WantedFloor);
            Assert.IsTrue(harness.Profile.SearchZone.Active);
            Assert.AreEqual(4, harness.Profile.SearchZone.WantedFloor);

            int callCount = 0;
            int requestedFloor = 0;
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    callCount++;
                    requestedFloor = level;
                    Game.Player.WantedLevel = level;
                    return true;
                });

            // Je simule un nouveau crime à une étoile après la fuite réussie.
            Game.Player.WantedLevel = 1;
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                1,
                2000,
                nowUtc.AddSeconds(2));

            PendingWantedEscalationRuntime pending =
                GetField<PendingWantedEscalationRuntime>(
                    harness.Script,
                    "_pendingWantedEscalation");
            Assert.IsNotNull(pending);
            Assert.AreEqual(2220, pending.EvaluateAtGameTime);

            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                1,
                2219,
                nowUtc.AddSeconds(2));
            Assert.AreEqual(0, callCount);

            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                1,
                2220,
                nowUtc.AddSeconds(2));

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(4, requestedFloor);
            Assert.AreEqual(4, Game.Player.WantedLevel);
            Assert.IsNull(
                GetField<object>(
                    harness.Script,
                    "_pendingWantedEscalation"));
        }
    }

    [TestMethod]
    public void ProlongedWantedReadFailures_KeepTheLastReliableLevelAndNeverCreateAFalseEscape()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                16,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 119);
            PursuitEpisodeRuntime episode =
                EscapeEpisode(player, 75, 4, "READFAIL");

            SetField(harness.Script, "_currentEpisode", episode);
            SetField(harness.Script, "_lastWantedLevel", 1);
            SetField(
                harness.Script,
                "_wantedLevelReaderOverride",
                (Func<int>)delegate { return 1; });
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    Game.Player.WantedLevel = level;
                    return true;
                });
            Assert.IsTrue(
                (bool)Invoke(
                    harness.Script,
                    "ApplyWantedMinimum",
                    4,
                    "plancher confirmé avant panne",
                    90));
            Assert.AreEqual(
                4,
                GetField<int>(harness.Script, "_lastReliableWantedLevel"));
            Assert.AreEqual(
                1,
                GetField<int>(harness.Script, "_lastWantedLevel"),
                "Le cache fiable ne doit pas masquer la prochaine transition.");

            int failedReads = 0;
            SetField(
                harness.Script,
                "_wantedLevelReaderOverride",
                (Func<int>)delegate
                {
                    failedReads++;
                    throw new InvalidOperationException("Lecture wanted simulée indisponible.");
                });

            for (int nowGameTime = 100;
                 nowGameTime <= 5100;
                 nowGameTime += 500)
            {
                int observedWanted =
                    (int)Invoke(
                        harness.Script,
                        "SafeGetWantedLevel");

                Assert.AreEqual(4, observedWanted);
                Invoke(
                    harness.Script,
                    "ProcessWantedState",
                    player,
                    observedWanted,
                    nowGameTime,
                    nowUtc.AddMilliseconds(nowGameTime));
            }

            Assert.IsTrue(failedReads > 2);
            Assert.AreSame(
                episode,
                GetField<PursuitEpisodeRuntime>(harness.Script, "_currentEpisode"));
            Assert.IsNull(GetField<object>(harness.Script, "_pendingWantedLoss"));
            Assert.AreEqual(0, harness.Profile.VehicleEvidence.Count);
            Assert.AreEqual(0, harness.Profile.OutfitEvidence.Count);
            Assert.IsFalse(harness.Profile.SearchZone.Active);
        }
    }

    [TestMethod]
    public void SearchZoneEntryAndEligibilityResume_RearmTheScanClockWithoutStaleExposure()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                18,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 113);
            harness.Profile.SearchZone = new SearchZoneState
            {
                Active = true,
                SourceEpisodeId = 44,
                WantedFloor = 4,
                Center = PositionData.FromVector3(player.Position),
                Radius = 900.0f,
                CreatedUtc = nowUtc.AddMinutes(-1),
                ExpiresUtc = nowUtc.AddMinutes(20),
                GraceUntilUtc = nowUtc.AddSeconds(-1),
                LastRecognitionUtc = DateTime.MinValue
            };

            SetField(harness.Script, "_insideSearchZone", false);
            SetField(harness.Script, "_lastRecognitionScanTime", 100);
            SetField(harness.Script, "_nextRecognitionScan", 10000);

            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                0,
                5000,
                nowUtc);

            Assert.IsTrue(GetField<bool>(harness.Script, "_insideSearchZone"));
            Assert.AreEqual(
                5000,
                GetField<int>(harness.Script, "_lastRecognitionScanTime"),
                "L'entrée dans la zone doit oublier tout ancien delta d'exposition.");

            Dictionary<int, ObserverExposureRuntime> exposures =
                GetField<Dictionary<int, ObserverExposureRuntime>>(
                    harness.Script,
                    "_observerExposures");
            exposures[701] = new ObserverExposureRuntime
            {
                Ped = new Ped(701),
                Handle = 701,
                ModelHash = 9001,
                Exposure = 1.0f,
                IsReporting = true,
                ReportAtGameTime = 8000
            };

            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                4,
                7000,
                nowUtc);

            Assert.AreEqual(0, exposures.Count);
            Assert.AreEqual(
                7000,
                GetField<int>(harness.Script, "_lastRecognitionScanTime"),
                "La période temporairement inéligible doit réarmer l'horloge avant la reprise.");

            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                0,
                7001,
                nowUtc);

            Assert.AreEqual(
                7000,
                GetField<int>(harness.Script, "_lastRecognitionScanTime"),
                "La reprise attend le prochain créneau sans réintroduire le delta ancien.");
        }
    }

    [TestMethod]
    public void PoliceObserver_WithLosRestoresTheWarrantFloorDuringALowerActivePursuit()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026, 8, 31, 12, 19, 0, DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 120);
            Ped officer = CreateObserverFacingPlayer(player, 720);
            bool clearLineOfSight = false;
            ConfigureObserverNatives(true, delegate { return clearLineOfSight; });
            SeedLocalAppearanceWarrant(harness.Profile, player, 4, nowUtc);
            StubRuntime.NearbyPeds = new[] { officer };

            Game.Player.WantedLevel = 1;
            SetField(harness.Script, "_lastWantedLevel", 1);
            SetField(
                harness.Script,
                "_currentEpisode",
                new PursuitEpisodeRuntime { EpisodeId = 201, PeakWantedLevel = 1 });

            int wantedWrites = 0;
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    wantedWrites++;
                    Game.Player.WantedLevel = level;
                    return true;
                });

            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                1,
                1000,
                nowUtc);

            Dictionary<int, ObserverExposureRuntime> exposures =
                GetField<Dictionary<int, ObserverExposureRuntime>>(
                    harness.Script,
                    "_observerExposures");
            Assert.AreEqual(0.0f, exposures[officer.Handle].Exposure, 0.0001f);

            clearLineOfSight = true;
            for (int nowGameTime = 2000;
                 nowGameTime <= 7000 && wantedWrites == 0;
                 nowGameTime += 1000)
            {
                Invoke(
                    harness.Script,
                    "UpdateSearchZoneRecognition",
                    player,
                    1,
                    nowGameTime,
                    nowUtc.AddMilliseconds(nowGameTime));
            }

            Assert.AreEqual(1, wantedWrites, harness.ReadLog());
            Assert.AreEqual(4, Game.Player.WantedLevel);
            Assert.AreEqual(
                4,
                GetField<int>(harness.Script, "_lastReliableWantedLevel"));
            Assert.IsTrue(harness.Profile.SearchZone.LastRecognitionUtc > DateTime.MinValue);

            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                4,
                8000,
                nowUtc.AddSeconds(8));
            Assert.AreEqual(1, wantedWrites);
        }
    }

    [TestMethod]
    public void SearchZoneWithoutValidAppearance_NeverBuildsBodyExposure()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026, 8, 31, 12, 19, 30, DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 121);
            Ped officer = CreateObserverFacingPlayer(player, 721);
            ConfigureObserverNatives(true, delegate { return true; });
            SeedLocalAppearanceWarrant(harness.Profile, player, 4, nowUtc);
            harness.Profile.AppearanceEvidence = new AppearanceEvidenceState();
            StubRuntime.NearbyPeds = new[] { officer };

            int wantedWrites = 0;
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    wantedWrites++;
                    Game.Player.WantedLevel = level;
                    return true;
                });

            for (int nowGameTime = 1000;
                 nowGameTime <= 15000;
                 nowGameTime += 1000)
            {
                Invoke(
                    harness.Script,
                    "UpdateSearchZoneRecognition",
                    player,
                    0,
                    nowGameTime,
                    nowUtc.AddMilliseconds(nowGameTime));
            }

            Dictionary<int, ObserverExposureRuntime> exposures =
                GetField<Dictionary<int, ObserverExposureRuntime>>(
                    harness.Script,
                    "_observerExposures");
            Assert.AreEqual(0, wantedWrites);
            Assert.AreEqual(0, Game.Player.WantedLevel);
            Assert.AreEqual(0.0f, exposures[officer.Handle].Exposure, 0.0001f);
        }
    }

    [TestMethod]
    public void CivilianObserver_RequiresLosThenReportsAfterDelayWhileExposureDecays()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026, 8, 31, 12, 19, 45, DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 122);
            Ped witness = CreateObserverFacingPlayer(player, 722);
            bool clearLineOfSight = true;
            ConfigureObserverNatives(false, delegate { return clearLineOfSight; });
            SeedLocalAppearanceWarrant(harness.Profile, player, 3, nowUtc);
            StubRuntime.NearbyPeds = new[] { witness };

            int wantedWrites = 0;
            JusticeRecognitionBridge.BindWantedMinimum(
                delegate(int level)
                {
                    wantedWrites++;
                    Game.Player.WantedLevel = level;
                    return true;
                });

            ObserverExposureRuntime state = null;
            int scanTime;
            for (scanTime = 1000; scanTime <= 20000; scanTime += 1000)
            {
                Invoke(
                    harness.Script,
                    "UpdateSearchZoneRecognition",
                    player,
                    0,
                    scanTime,
                    nowUtc.AddMilliseconds(scanTime));

                Dictionary<int, ObserverExposureRuntime> exposures =
                    GetField<Dictionary<int, ObserverExposureRuntime>>(
                        harness.Script,
                        "_observerExposures");
                if (exposures.TryGetValue(witness.Handle, out state) &&
                    state.IsReporting)
                {
                    break;
                }
            }

            Assert.IsNotNull(state);
            Assert.IsTrue(state.IsReporting);
            Assert.AreEqual(0, wantedWrites);
            float visibleExposure = state.Exposure;

            clearLineOfSight = false;
            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                0,
                scanTime + 1000,
                nowUtc.AddMilliseconds(scanTime + 1000));

            Assert.IsTrue(state.Exposure < visibleExposure);
            Assert.AreEqual(0, wantedWrites);

            int reportScanTime = state.ReportAtGameTime + 400;
            Invoke(
                harness.Script,
                "UpdateSearchZoneRecognition",
                player,
                0,
                reportScanTime,
                nowUtc.AddMilliseconds(reportScanTime));

            Assert.AreEqual(1, wantedWrites, harness.ReadLog());
            Assert.AreEqual(3, Game.Player.WantedLevel);
        }
    }

    [TestMethod]
    public void RecycledObserverHandle_ResetsExposureAndReportAcrossEntityGenerations()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            int modelHash = Game.GenerateHash("s_m_y_cop_01");
            Ped firstObserver = new Ped(702)
            {
                Model = new Model(modelHash)
            };

            SetField(harness.Script, "_recognitionScanSequence", 1);
            Invoke(
                harness.Script,
                "UpdateObserverExposure",
                firstObserver,
                false,
                1.25f,
                1.0f,
                1000);

            Dictionary<int, ObserverExposureRuntime> exposures =
                GetField<Dictionary<int, ObserverExposureRuntime>>(
                    harness.Script,
                    "_observerExposures");
            ObserverExposureRuntime firstState = exposures[702];
            Assert.AreEqual(1.25f, firstState.Exposure, 0.0001f);
            Assert.IsTrue(firstState.IsReporting);
            Assert.AreEqual(3500, firstState.ReportAtGameTime);

            // Je reproduis le repli sans adresse native du stub avec une autre enveloppe du même handle.
            Ped recycledObserver = new Ped(702)
            {
                Model = new Model(modelHash)
            };
            SetField(harness.Script, "_recognitionScanSequence", 2);
            Invoke(
                harness.Script,
                "UpdateObserverExposure",
                recycledObserver,
                false,
                0.2f,
                0.5f,
                1100);

            ObserverExposureRuntime recycledState = exposures[702];
            Assert.AreNotSame(firstState, recycledState);
            Assert.AreSame(recycledObserver, recycledState.Ped);
            Assert.AreEqual(702, recycledState.Handle);
            Assert.AreEqual(modelHash, recycledState.ModelHash);
            Assert.AreEqual(0L, recycledState.MemoryAddress);
            Assert.AreEqual(0.1f, recycledState.Exposure, 0.0001f);
            Assert.IsFalse(recycledState.IsReporting);
            Assert.AreEqual(0, recycledState.ReportAtGameTime);

            recycledState.MemoryAddress = 4100L;
            Assert.IsTrue(
                (bool)InvokeStatic(
                    "CanReuseObserverExposure",
                    recycledState,
                    recycledObserver,
                    modelHash,
                    4100L));
            Assert.IsFalse(
                (bool)InvokeStatic(
                    "CanReuseObserverExposure",
                    recycledState,
                    recycledObserver,
                    modelHash,
                    4200L),
                "Une adresse native différente désigne une autre génération, même avec le même handle et modèle.");

            recycledState.MemoryAddress = 0L;
            Assert.IsTrue(
                (bool)InvokeStatic(
                    "CanReuseObserverExposure",
                    recycledState,
                    recycledObserver,
                    modelHash,
                    0L));
            Assert.IsFalse(
                (bool)InvokeStatic(
                    "CanReuseObserverExposure",
                    recycledState,
                    new Ped(702) { Model = new Model(modelHash) },
                    modelHash,
                    0L),
                "Sans adresse native, seule la même référence Ped peut conserver l'exposition.");
        }
    }

    [TestMethod]
    public void VoluntaryWantedRemoval_SuppressesOnlyTheCurrentEpisodeAndCreatesNoFalseWarrant()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                20,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 103);
            SetField(
                harness.Script,
                "_currentEpisode",
                EscapeEpisode(player, 81, 3, "SUPPRESS3"));
            SetField(harness.Script, "_lastWantedLevel", 3);
            Game.Player.WantedLevel = 3;

            JusticeRecognitionBridge.SuppressNextWantedLoss("capture judiciaire simulée");
            Invoke(harness.Script, "DrainQueuedCommands", 100);

            Assert.IsTrue(GetField<bool>(harness.Script, "_suppressNextWantedLoss"));
            Assert.AreEqual(81L, GetField<long>(harness.Script, "_suppressedWantedLossEpisodeId"));

            Game.Player.WantedLevel = 0;
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                101,
                nowUtc);
            PendingWantedLossRuntime pending =
                GetField<PendingWantedLossRuntime>(harness.Script, "_pendingWantedLoss");
            Assert.IsNotNull(pending);
            Assert.IsTrue(pending.Suppressed);

            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                1001,
                nowUtc);

            Assert.IsNull(GetField<object>(harness.Script, "_currentEpisode"));
            Assert.IsNull(GetField<object>(harness.Script, "_pendingWantedLoss"));
            Assert.AreEqual(0, harness.Profile.VehicleEvidence.Count);
            Assert.AreEqual(0, harness.Profile.OutfitEvidence.Count);
            Assert.IsFalse(harness.Profile.AppearanceEvidence.Active);
            Assert.IsFalse(harness.Profile.SearchZone.Active);
        }
    }

    [TestMethod]
    public void ConfirmedCapture_ClearsTheTargetAndCannotTurnItsWantedRemovalIntoAnEscape()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                30,
                0,
                DateTimeKind.Utc);
            Ped player = CreatePlayerPed("player_zero", 104);
            SeedRecognition(harness.Profile, 2, "OLDMIKE", nowUtc);
            SetField(
                harness.Script,
                "_currentEpisode",
                EscapeEpisode(player, 91, 4, "CAPTURE4"));
            SetField(harness.Script, "_lastWantedLevel", 4);
            Game.Player.WantedLevel = 4;

            JusticeRecognitionBridge.NotifyPlayerCaptured(
                "Michael",
                "entrée en détention confirmée");
            Invoke(harness.Script, "DrainQueuedCommands", 200);

            Assert.IsNull(GetField<object>(harness.Script, "_currentEpisode"));
            Assert.IsNull(GetField<object>(harness.Script, "_pendingWantedLoss"));
            Assert.AreEqual(0, harness.Profile.VehicleEvidence.Count);
            Assert.AreEqual(0, harness.Profile.OutfitEvidence.Count);
            Assert.IsFalse(harness.Profile.SearchZone.Active);

            Game.Player.WantedLevel = 0;
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                201,
                nowUtc);
            Invoke(
                harness.Script,
                "ProcessWantedState",
                player,
                0,
                1200,
                nowUtc);

            Assert.AreEqual(0, harness.Profile.VehicleEvidence.Count);
            Assert.AreEqual(0, harness.Profile.OutfitEvidence.Count);
            Assert.IsFalse(harness.Profile.AppearanceEvidence.Active);
            Assert.IsFalse(harness.Profile.SearchZone.Active);
        }
    }

    [TestMethod]
    public void RuntimeSuspension_RemainsDistinctFromTheReversibleToggleAndPreservesEvidence()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                40,
                0,
                DateTimeKind.Utc);
            SeedRecognition(harness.Profile, 4, "KEEPDATA", nowUtc);
            SetField(
                harness.Script,
                "_currentEpisode",
                new PursuitEpisodeRuntime { EpisodeId = 100, PeakWantedLevel = 2 });

            JusticeRecognitionBridge.SetRuntimeSuspended(true);
            Invoke(harness.Script, "DrainQueuedCommands", 300);

            Assert.IsTrue(GetField<bool>(harness.Script, "_enabled"));
            Assert.IsTrue(GetField<bool>(harness.Script, "_runtimeSuspended"));
            Assert.IsNull(GetField<object>(harness.Script, "_currentEpisode"));
            AssertRecognitionPresent(harness.Profile, "KEEPDATA");

            JusticeRecognitionBridge.SetEnabled(false);
            Invoke(harness.Script, "DrainQueuedCommands", 301);

            Assert.IsFalse(GetField<bool>(harness.Script, "_enabled"));
            Assert.IsTrue(
                GetField<bool>(harness.Script, "_runtimeSuspended"),
                "Le toggle utilisateur ne doit pas acquitter une suspension de gameplay.");
            AssertRecognitionPresent(harness.Profile, "KEEPDATA");

            JusticeRecognitionBridge.SetEnabled(true);
            JusticeRecognitionBridge.SetRuntimeSuspended(false);
            Invoke(harness.Script, "DrainQueuedCommands", 302);

            Assert.IsTrue(GetField<bool>(harness.Script, "_enabled"));
            Assert.IsFalse(GetField<bool>(harness.Script, "_runtimeSuspended"));
            AssertRecognitionPresent(harness.Profile, "KEEPDATA");
        }
    }

    [TestMethod]
    public void RepaintOfTheSamePlate_NeutralizesTheEvidenceOnceAndNeverRestoresItsWantedFloor()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                45,
                0,
                DateTimeKind.Utc);
            VehicleSignatureData reported = VehicleSignature(4400, "PAINTME");
            reported.PrimaryColor = 10;
            reported.SecondaryColor = 20;
            harness.Profile.VehicleEvidence.Add(
                new VehicleEvidenceState
                {
                    Active = true,
                    SourceEpisodeId = 120,
                    WantedFloor = 4,
                    CreatedUtc = nowUtc.AddMinutes(-1),
                    ExpiresUtc = nowUtc.AddMinutes(20),
                    Signature = reported
                });

            VehicleSignatureData repainted = reported.Clone();
            repainted.PrimaryColor = 99;
            object[] firstArguments = { repainted, nowUtc, true, false };
            int firstFloor = (int)Invoke(
                harness.Script,
                "GetMatchingVehicleWantedFloor",
                firstArguments);

            VehicleEvidenceState evidence = harness.Profile.VehicleEvidence[0];
            Assert.AreEqual(0, firstFloor);
            Assert.IsTrue((bool)firstArguments[3]);
            Assert.IsTrue(evidence.Neutralized);
            Assert.IsTrue(evidence.NeutralizationNotified);
            Assert.AreEqual(
                1,
                CountOccurrences(harness.ReadLog(), "vehicle_evidence_neutralized"));

            object[] secondArguments = { repainted, nowUtc, true, false };
            int secondFloor = (int)Invoke(
                harness.Script,
                "GetMatchingVehicleWantedFloor",
                secondArguments);
            Assert.AreEqual(0, secondFloor);
            Assert.IsFalse(
                (bool)secondArguments[3],
                "La peinture déjà traitée ne doit plus republier une mutation de l'indice.");
            Assert.AreEqual(
                1,
                CountOccurrences(harness.ReadLog(), "vehicle_evidence_neutralized"));

            object[] originalPaintArguments = { reported.Clone(), nowUtc, true, false };
            Assert.AreEqual(
                0,
                (int)Invoke(
                    harness.Script,
                    "GetMatchingVehicleWantedFloor",
                    originalPaintArguments),
                "Un indice neutralisé ne doit plus restaurer le wanted, même si la peinture revient.");
        }
    }

    [TestMethod]
    public void SearchZoneDisguiseMultiplier_DropsFromOneToCombinedChangeAndMaskRisk()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            int pedModelHash = 5500;
            OutfitSignatureData rememberedOutfit = OutfitSignature(pedModelHash);
            AppearanceSignatureData rememberedAppearance = AppearanceSignature(pedModelHash);
            harness.Profile.AppearanceEvidence = new AppearanceEvidenceState
            {
                Active = true,
                SourceEpisodeId = 130,
                Signature = rememberedAppearance.Clone(),
                OutfitReference = rememberedOutfit.Clone()
            };

            IdentitySnapshot unchanged = new IdentitySnapshot
            {
                Outfit = rememberedOutfit.Clone(),
                Appearance = rememberedAppearance.Clone()
            };
            float unchangedMultiplier = (float)Invoke(
                harness.Script,
                "GetCurrentDisguiseMultiplier",
                unchanged);
            Assert.AreEqual(1.0f, unchangedMultiplier, 0.0001f);

            OutfitSignatureData changedOutfit = rememberedOutfit.Clone();
            FindComponent(changedOutfit, 11).Drawable = 9;
            AppearanceSignatureData changedAppearance = rememberedAppearance.Clone();
            changedAppearance.HairDrawable = 7;
            IdentitySnapshot fullyChanged = new IdentitySnapshot
            {
                Outfit = changedOutfit,
                Appearance = changedAppearance
            };
            float changedMultiplier = (float)Invoke(
                harness.Script,
                "GetCurrentDisguiseMultiplier",
                fullyChanged);
            Assert.AreEqual(
                RecognitionPolicy.ChangedOutfitAndAppearanceMultiplier,
                changedMultiplier,
                0.0001f);
            Assert.AreEqual(0.08f, changedMultiplier, 0.0001f);

            FindComponent(changedOutfit, 1).Drawable = 3;
            float maskedMultiplier = (float)Invoke(
                harness.Script,
                "GetCurrentDisguiseMultiplier",
                fullyChanged);
            Assert.AreEqual(
                0.08f * RecognitionPolicy.FaceMaskRecognitionMultiplier,
                maskedMultiplier,
                0.0001f);
            Assert.IsTrue(maskedMultiplier < changedMultiplier);
            Assert.IsTrue(
                maskedMultiplier >= RecognitionPolicy.MinimumRecognitionMultiplier,
                "Le masque réduit fortement le risque sans créer une invisibilité absolue.");
        }
    }

    [TestMethod]
    public void TargetedCommandsAndAuthoritativeProfile_KeepTheThreeHeroesStrictlyIsolated()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            DateTime nowUtc = new DateTime(
                2026,
                8,
                31,
                12,
                50,
                0,
                DateTimeKind.Utc);
            RecognitionProfileData franklin =
                harness.SaveData.GetOrCreateProfile("Franklin");
            RecognitionProfileData trevor =
                harness.SaveData.GetOrCreateProfile("Trevor");
            SeedRecognition(harness.Profile, 2, "MIKEONLY", nowUtc);
            SeedRecognition(franklin, 3, "FRANKONLY", nowUtc);
            SeedRecognition(trevor, 5, "TREVONLY", nowUtc);

            JusticeRecognitionBridge.ClearProfile(
                "Franklin",
                "reset explicite Franklin");
            JusticeRecognitionBridge.NotifyPlayerCaptured(
                "Trevor",
                "capture Trevor");
            Invoke(harness.Script, "DrainQueuedCommands", 400);

            AssertRecognitionPresent(harness.Profile, "MIKEONLY");
            AssertRecognitionCleared(franklin);
            AssertRecognitionCleared(trevor);

            JusticeRecognitionBridge.SetActiveProfile("Franklin");
            Invoke(harness.Script, "DrainQueuedCommands", 401);
            Assert.AreEqual(
                "Franklin",
                GetField<string>(harness.Script, "_authoritativeProfileId"));
            Assert.IsNull(GetField<object>(harness.Script, "_currentProfile"));

            JusticeRecognitionBridge.SetActiveProfile("Michael");
            Invoke(harness.Script, "DrainQueuedCommands", 402);
            Assert.AreEqual(
                "Michael",
                GetField<string>(harness.Script, "_authoritativeProfileId"));
            Assert.IsNull(GetField<object>(harness.Script, "_currentProfile"));
            Assert.IsNull(GetField<object>(harness.Script, "_currentProfileId"));
            AssertRecognitionPresent(harness.Profile, "MIKEONLY");

            string profileUpdate = ExtractRecognitionMethod("UpdateActiveProfile");
            AssertContainsInOrder(
                profileUpdate,
                "NormalizeProfileId(_authoritativeProfileId)",
                "modelProfileId != null",
                "PauseForUnknownProfile();",
                "return false;");
            StringAssert.Contains(profileUpdate, "profileId = modelProfileId;");
            StringAssert.Contains(profileUpdate, "_saveData.GetOrCreateProfile(profileId)");
        }
    }

    [TestMethod]
    public void CriticalCommandsForAbsentCanonicalProfiles_PersistAndAcknowledgeAsAlreadyClean()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            harness.SaveData.Profiles.RemoveAll(
                delegate(RecognitionProfileData profile)
                {
                    return profile != null &&
                           (profile.ProfileId == "Franklin" ||
                            profile.ProfileId == "Trevor");
                });

            Assert.IsTrue(
                JusticeRecognitionBridge.NotifyPlayerCaptured(
                    "Franklin",
                    "capture déjà vide"));
            Assert.IsTrue(
                JusticeRecognitionBridge.ClearProfile(
                    "Trevor",
                    "reset déjà vide"));

            Invoke(harness.Script, "DrainQueuedCommands", 450);

            Assert.AreEqual(0, GetStaticDictionaryCount("PendingProfileCaptureReasons"));
            Assert.AreEqual(0, GetStaticDictionaryCount("PendingProfileClearReasons"));
            Assert.AreEqual(
                0,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileCaptureReasons").Count);
            Assert.AreEqual(
                0,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileClearReasons").Count);

            RecognitionCriticalIntentStore reader =
                new RecognitionCriticalIntentStore(_criticalIntentPath);
            RecognitionCriticalIntentJournalData journal;
            Assert.IsTrue(reader.TryLoad(out journal));
            Assert.AreEqual(0, journal.Intents.Count);

            // Je rejoue les mêmes effets logiques : l'absence reste terminale
            // sans créer de profil fantôme ni laisser une intention durable.
            Assert.IsTrue(JusticeRecognitionBridge.ClearProfile("Trevor", "reset répété"));
            Invoke(harness.Script, "DrainQueuedCommands", 451);
            Assert.AreEqual(1, harness.SaveData.Profiles.Count);
            Assert.AreEqual("Michael", harness.SaveData.Profiles[0].ProfileId);
            Assert.IsTrue(reader.TryLoad(out journal));
            Assert.AreEqual(0, journal.Intents.Count);
        }
    }

    [TestMethod]
    public void DetachedBridge_DeduplicatesCriticalCommandsAndDeliversThemExactlyOnceOnAttach()
    {
        JusticeRecognitionBridge.SetActiveProfile("Michael");
        JusticeRecognitionBridge.NotifyPlayerCaptured("Trevor", "ancienne capture");
        JusticeRecognitionBridge.NotifyPlayerCaptured("Trevor", "capture finale");
        JusticeRecognitionBridge.ClearProfile("Franklin", "ancien reset");
        JusticeRecognitionBridge.ClearProfile("Franklin", "reset final");
        JusticeRecognitionBridge.ClearCurrentProfile("amnistie Michael");
        JusticeRecognitionBridge.SuppressNextWantedLoss("ne doit pas survivre");

        RuntimeHarness harness = new RuntimeHarness();
        try
        {
            harness.SaveData.GetOrCreateProfile("Franklin");
            harness.SaveData.GetOrCreateProfile("Trevor");

            Dictionary<string, BridgeCriticalCommand> captures =
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileCaptureReasons");
            Dictionary<string, BridgeCriticalCommand> clears =
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileClearReasons");

            Assert.AreEqual(1, captures.Count);
            Assert.AreEqual("capture finale", captures["Trevor"].Reason);
            Assert.AreEqual(2, clears.Count);
            Assert.AreEqual("reset final", clears["Franklin"].Reason);
            Assert.AreEqual("amnistie Michael", clears["Michael"].Reason);
            Assert.IsFalse(
                GetField<bool>(harness.Script, "_queuedSuppressWantedLoss"),
                "Une suppression wanted périmée ne traverse jamais une absence du script.");

            Invoke(harness.Script, "DrainQueuedCommands", 100);
            Assert.AreEqual(
                0,
                GetStaticDictionaryCount("PendingProfileCaptureReasons"));
            Assert.AreEqual(
                0,
                GetStaticDictionaryCount("PendingProfileClearReasons"));

            JusticeRecognitionBridge.Detach(harness.Script);
            DonJJusticeRecognitionScript second =
                new DonJJusticeRecognitionScript();
            try
            {
                Assert.AreEqual(
                    0,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        second,
                        "_queuedProfileCaptureReasons").Count);
                Assert.AreEqual(
                    0,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        second,
                        "_queuedProfileClearReasons").Count);
                Assert.IsNull(
                    GetField<BridgeCriticalCommand>(
                        second,
                        "_queuedClearAllProfiles"));
            }
            finally
            {
                JusticeRecognitionBridge.Detach(second);
            }

            JusticeRecognitionBridge.NotifyPlayerCaptured(
                "Trevor",
                "capture couverte par global");
            JusticeRecognitionBridge.ClearProfile(
                "Franklin",
                "reset couvert par global");
            JusticeRecognitionBridge.ClearAllProfiles("global final");

            DonJJusticeRecognitionScript global =
                new DonJJusticeRecognitionScript();
            try
            {
                Assert.AreEqual(
                    0,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        global,
                        "_queuedProfileCaptureReasons").Count);
                Assert.AreEqual(
                    0,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        global,
                        "_queuedProfileClearReasons").Count);
                Assert.AreEqual(
                    "global final",
                    GetField<BridgeCriticalCommand>(
                        global,
                        "_queuedClearAllProfiles").Reason);
            }
            finally
            {
                JusticeRecognitionBridge.Detach(global);
            }
        }
        finally
        {
            harness.Dispose();
        }
    }

    [TestMethod]
    public void CriticalIntentJournal_ReplaysAfterRestartAndAcknowledgesExactlyOnce()
    {
        Assert.IsTrue(
            JusticeRecognitionBridge.ClearProfile(
                "Franklin",
                "amnistie durable"));
        Assert.IsTrue(File.Exists(_criticalIntentPath));
        Assert.IsTrue(File.Exists(_criticalIntentPath + ".bak"));
        CollectionAssert.AreEqual(
            File.ReadAllBytes(_criticalIntentPath),
            File.ReadAllBytes(_criticalIntentPath + ".bak"));

        ResetBridgeState();
        JusticeRecognitionBridge.ConfigureCriticalIntentJournalForTests(
            _criticalIntentPath);

        RuntimeHarness harness = new RuntimeHarness();
        try
        {
            harness.SaveData.GetOrCreateProfile("Franklin");
            Assert.AreEqual(
                1,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileClearReasons").Count);

            Invoke(harness.Script, "DrainQueuedCommands", 100);
            Assert.AreEqual(0, GetStaticDictionaryCount("PendingProfileClearReasons"));
            CollectionAssert.AreEqual(
                File.ReadAllBytes(_criticalIntentPath),
                File.ReadAllBytes(_criticalIntentPath + ".bak"));
        }
        finally
        {
            harness.Dispose();
        }

        JusticeRecognitionBridge.ConfigureCriticalIntentJournalForTests(
            _criticalIntentPath);
        DonJJusticeRecognitionScript second = new DonJJusticeRecognitionScript();
        try
        {
            Assert.AreEqual(
                0,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    second,
                    "_queuedProfileClearReasons").Count,
                "L'intention acquittée ne doit jamais être rejouée une seconde fois.");
        }
        finally
        {
            JusticeRecognitionBridge.Detach(second);
        }
    }

    [TestMethod]
    public void CriticalIntentJournal_ReturnsFalseUntilItsBackupIsPublished()
    {
        Directory.CreateDirectory(_criticalIntentDirectory);
        Directory.CreateDirectory(_criticalIntentPath + ".bak");

        Assert.IsFalse(
            JusticeRecognitionBridge.ClearProfile(
                "Michael",
                "reset non acquitté"),
            "Le primaire seul ne suffit jamais à acquitter Justice.");
        Assert.AreEqual(1, GetStaticDictionaryCount("PendingProfileClearReasons"));

        Directory.Delete(_criticalIntentPath + ".bak");

        using (RuntimeHarness harness = new RuntimeHarness())
        {
            Invoke(harness.Script, "DrainQueuedCommands", 100);
            Assert.AreEqual(0, GetStaticDictionaryCount("PendingProfileClearReasons"));
            Assert.IsTrue(File.Exists(_criticalIntentPath + ".bak"));
            CollectionAssert.AreEqual(
                File.ReadAllBytes(_criticalIntentPath),
                File.ReadAllBytes(_criticalIntentPath + ".bak"));
        }
    }

    [TestMethod]
    public void CriticalIntentJournal_QuarantinesEveryCorruptVariantAndReplaysTheNewIntentIdempotently()
    {
        Directory.CreateDirectory(_criticalIntentDirectory);

        string[] corruptVariants =
        {
            _criticalIntentPath,
            _criticalIntentPath + ".bak",
            _criticalIntentPath + ".tmp",
            _criticalIntentPath + ".bak.tmp",
            _criticalIntentPath + ".rollback",
            _criticalIntentPath + ".bak.rollback"
        };

        for (int index = 0; index < corruptVariants.Length; index++)
        {
            File.WriteAllText(
                corruptVariants[index],
                "<journal-corrompu variante=\"" + index + "\">");
        }

        Assert.IsTrue(
            JusticeRecognitionBridge.ClearProfile(
                "Michael",
                "reset après corruption totale"),
            "La nouvelle intention doit être enregistrée après mise en quarantaine, jamais bloquée par les anciens octets.");

        string quarantineDirectory =
            _criticalIntentPath + ".corrupt-quarantine";
        Assert.IsTrue(Directory.Exists(quarantineDirectory));
        Assert.AreEqual(
            corruptVariants.Length,
            Directory.GetFiles(quarantineDirectory, "*.corrupt").Length,
            "Chaque variante illisible doit rester disponible hors des chemins de chargement.");

        Assert.IsFalse(File.Exists(_criticalIntentPath + ".tmp"));
        Assert.IsFalse(File.Exists(_criticalIntentPath + ".bak.tmp"));
        Assert.IsFalse(File.Exists(_criticalIntentPath + ".rollback"));
        Assert.IsFalse(File.Exists(_criticalIntentPath + ".bak.rollback"));
        Assert.IsTrue(File.Exists(_criticalIntentPath));
        Assert.IsTrue(File.Exists(_criticalIntentPath + ".bak"));
        CollectionAssert.AreEqual(
            File.ReadAllBytes(_criticalIntentPath),
            File.ReadAllBytes(_criticalIntentPath + ".bak"));

        RecognitionCriticalIntentJournalData recovered;
        RecognitionCriticalIntentStore reader =
            new RecognitionCriticalIntentStore(_criticalIntentPath);
        Assert.IsTrue(reader.TryLoad(out recovered));
        Assert.IsNotNull(recovered);
        Assert.AreEqual(1, recovered.Intents.Count);
        Assert.AreEqual(
            RecognitionCriticalIntentKinds.ClearProfile,
            recovered.Intents[0].Kind);
        Assert.AreEqual("Michael", recovered.Intents[0].ProfileId);

        int quarantinedFileCount =
            Directory.GetFiles(quarantineDirectory, "*.corrupt").Length;

        // Je simule un arrêt juste après l'enregistrement durable : le nouveau
        // processus doit rejouer la commande une fois, l'acquitter, puis ne plus
        // toucher à la quarantaine lors des chargements suivants.
        ResetBridgeState();
        JusticeRecognitionBridge.ConfigureCriticalIntentJournalForTests(
            _criticalIntentPath);

        using (RuntimeHarness harness = new RuntimeHarness())
        {
            Assert.AreEqual(
                1,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    harness.Script,
                    "_queuedProfileClearReasons").Count);

            Invoke(harness.Script, "DrainQueuedCommands", 100);
            Assert.AreEqual(
                0,
                GetStaticDictionaryCount("PendingProfileClearReasons"));
        }

        ResetBridgeState();
        JusticeRecognitionBridge.ConfigureCriticalIntentJournalForTests(
            _criticalIntentPath);
        DonJJusticeRecognitionScript second =
            new DonJJusticeRecognitionScript();
        try
        {
            Assert.AreEqual(
                0,
                GetField<Dictionary<string, BridgeCriticalCommand>>(
                    second,
                    "_queuedProfileClearReasons").Count,
                "Une reprise déjà acquittée ne doit jamais être rejouée.");
        }
        finally
        {
            JusticeRecognitionBridge.Detach(second);
        }

        Assert.AreEqual(
            quarantinedFileCount,
            Directory.GetFiles(quarantineDirectory, "*.corrupt").Length,
            "Un rechargement valide ne doit pas créer une seconde quarantaine.");

        RecognitionCriticalIntentJournalData acknowledged;
        Assert.IsTrue(reader.TryLoad(out acknowledged));
        Assert.AreEqual(0, acknowledged.Intents.Count);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(_criticalIntentPath),
            File.ReadAllBytes(_criticalIntentPath + ".bak"));
    }

    [TestMethod]
    public void IdentityCapture_UsesTheSharedCacheAndResolvesTheVehicleOncePerObserverScan()
    {
        Assert.AreEqual(600, GetPrivateConstant<int>("IdentityRefreshMilliseconds"));

        string pursuitUpdate = ExtractRecognitionMethod("UpdatePursuitEpisode");
        string escalation = ExtractRecognitionMethod("ProcessPendingWantedEscalation");
        string repaint = ExtractRecognitionMethod("CheckVehicleRepaint");
        string observers = ExtractRecognitionMethod("ScanObservers");
        string pursuitStart = ExtractRecognitionMethod("BeginPursuitEpisode");
        string escapeFinalization = ExtractRecognitionMethod("CreateEvidenceFromSuccessfulEscape");

        pursuitUpdate = pursuitUpdate.Replace("\r\n", "\n");
        escalation = escalation.Replace("\r\n", "\n");
        repaint = repaint.Replace("\r\n", "\n");
        observers = observers.Replace("\r\n", "\n");
        pursuitStart = pursuitStart.Replace("\r\n", "\n");
        escapeFinalization = escapeFinalization.Replace("\r\n", "\n");

        StringAssert.Contains(pursuitUpdate, "nowGameTime,\n                    false");
        StringAssert.Contains(escalation, "nowGameTime,\n                    false");
        StringAssert.Contains(repaint, "nowGameTime,\n                    false");
        StringAssert.Contains(observers, "nowGameTime,\n                    false");
        StringAssert.Contains(pursuitStart, "nowGameTime,\n                    true");
        StringAssert.Contains(escapeFinalization, "nowGameTime,\n                        true");
        Assert.AreEqual(
            1,
            CountOccurrences(observers, "GetCurrentVehicle(playerPed)"),
            "Je ne relis pas le véhicule pour chaque policier ou témoin.");
    }

    [TestMethod]
    public void StatusLines_AlwaysExposeFiveStableRowsWithOrWithoutASearchZone()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            SetField(harness.Script, "_initialized", true);
            harness.Profile.SearchZone = new SearchZoneState();

            string[] withoutZone =
                (string[])Invoke(
                    harness.Script,
                    "BuildStatusLines");

            Assert.AreEqual(5, withoutZone.Length);
            StringAssert.Contains(withoutZone[3], "Mandat local : aucun");
            StringAssert.Contains(withoutZone[4], "risque d'identification : aucun");

            SetField(harness.Script, "_currentProfile", null);
            string[] unavailable =
                (string[])Invoke(
                    harness.Script,
                    "BuildStatusLines");
            Assert.AreEqual(5, unavailable.Length);

            JusticeRecognitionBridge.Detach(harness.Script);
            Assert.AreEqual(
                5,
                JusticeRecognitionBridge.GetStatusLines().Length,
                "Le menu garde sa géométrie même si le script n'est pas chargé.");
            JusticeRecognitionBridge.Attach(harness.Script);

            DonJJusticeRecognitionScript initializing =
                new DonJJusticeRecognitionScript();
            try
            {
                Assert.AreEqual(5, initializing.GetStatusLines().Length);
            }
            finally
            {
                JusticeRecognitionBridge.Detach(initializing);
            }
        }
    }

    [TestMethod]
    public void CriticalClear_AcknowledgesOnlyAfterPersistenceAndRetriesWithBackoff()
    {
        using (RuntimeHarness harness = new RuntimeHarness())
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "DonJRecognitionAckTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string blockedDirectory = Path.Combine(directory, "bloque");
            File.WriteAllText(blockedDirectory, "Je bloque le dossier de sauvegarde.");

            try
            {
                RecognitionStore failingStore = new RecognitionStore(
                    Path.Combine(blockedDirectory, "Recognition.xml"),
                    harness.Logger);
                SetField(harness.Script, "_store", failingStore);

                JusticeRecognitionBridge.ClearProfile(
                    "Michael",
                    "clear à acquitter");
                Invoke(harness.Script, "DrainQueuedCommands", 100);

                Assert.AreEqual(
                    1,
                    GetStaticDictionaryCount("PendingProfileClearReasons"),
                    "Le Bridge conserve la commande tant que ForceSave n'a pas acquitté.");
                Assert.IsTrue(failingStore.IsDirty);
                Assert.AreEqual(
                    1,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        harness.Script,
                        "_queuedProfileClearReasons").Count);

                SetField(harness.Script, "_store", harness.Store);
                Invoke(harness.Script, "DrainQueuedCommands", 5099);
                Assert.AreEqual(1, GetStaticDictionaryCount("PendingProfileClearReasons"));

                Invoke(harness.Script, "DrainQueuedCommands", 5100);
                Assert.AreEqual(
                    0,
                    GetStaticDictionaryCount("PendingProfileClearReasons"));
                Assert.AreEqual(
                    0,
                    GetField<Dictionary<string, BridgeCriticalCommand>>(
                        harness.Script,
                        "_queuedProfileClearReasons").Count);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // Je laisse Windows finir un verrou antivirus temporaire.
                }
            }
        }
    }

    private static Ped CreateObserverFacingPlayer(
        Ped player,
        int handle)
    {
        return new Ped(handle)
        {
            Model = new Model(Game.GenerateHash("a_m_m_business_01")),
            Position = new Vector3(
                player.Position.X,
                player.Position.Y - 10.0f,
                player.Position.Z),
            ForwardVector = new Vector3(0.0f, 1.0f, 0.0f)
        };
    }

    private static void SeedLocalAppearanceWarrant(
        RecognitionProfileData profile,
        Ped player,
        int wantedFloor,
        DateTime nowUtc)
    {
        profile.VehicleEvidence.Clear();
        profile.OutfitEvidence.Clear();
        profile.AppearanceEvidence = new AppearanceEvidenceState
        {
            Active = true,
            SourceEpisodeId = 200 + wantedFloor,
            Signature = AppearanceSignature(player.Model.Hash),
            OutfitReference = OutfitSignature(player.Model.Hash)
        };
        profile.SearchZone = new SearchZoneState
        {
            Active = true,
            SourceEpisodeId = 200 + wantedFloor,
            WantedFloor = wantedFloor,
            Center = PositionData.FromVector3(player.Position),
            Radius = RecognitionPolicy.GetZoneRadius(wantedFloor),
            CreatedUtc = nowUtc.AddMinutes(-1),
            ExpiresUtc = nowUtc.AddMinutes(20),
            GraceUntilUtc = nowUtc.AddSeconds(-1),
            LastRecognitionUtc = DateTime.MinValue
        };
    }

    private static void ConfigureObserverNatives(
        bool lawOfficer,
        Func<bool> clearLineOfSight)
    {
        StubRuntime.NativeCallHandler =
            delegate(ulong hash, object[] arguments)
            {
                if (hash == RecognitionNativeHashes.DoesEntityExist)
                {
                    return true;
                }

                if (hash == RecognitionNativeHashes.IsPlayerSwitchInProgress)
                {
                    return false;
                }

                if (hash == (ulong)GTA.Native.Hash.IS_PED_HUMAN)
                {
                    return true;
                }

                if (hash == (ulong)GTA.Native.Hash.GET_PED_RELATIONSHIP_GROUP_HASH)
                {
                    return lawOfficer
                        ? Game.GenerateHash("COP")
                        : Game.GenerateHash("CIVMALE");
                }

                if (hash == (ulong)GTA.Native.Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY)
                {
                    return clearLineOfSight != null && clearLineOfSight();
                }

                return null;
            };
    }

    private static PursuitEpisodeRuntime EscapeEpisode(
        Ped player,
        long episodeId,
        int wantedFloor,
        string plate)
    {
        return new PursuitEpisodeRuntime
        {
            EpisodeId = episodeId,
            PeakWantedLevel = wantedFloor,
            LastKnownPosition = PositionData.FromVector3(player.Position),
            LastVehicle = VehicleSignature(700 + wantedFloor, plate),
            LastOutfit = OutfitSignature(player.Model.Hash),
            LastAppearance = AppearanceSignature(player.Model.Hash)
        };
    }

    private static Ped CreatePlayerPed(string modelName, int handle)
    {
        Ped player = new Ped(handle)
        {
            Model = new Model(Game.GenerateHash(modelName)),
            Position = new Vector3(125.0f, -340.0f, 28.0f),
            IsPlayer = true
        };
        Game.Player.Character = player;
        return player;
    }

    private static VehicleSignatureData VehicleSignature(int modelHash, string plate)
    {
        return new VehicleSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            ModelHash = modelHash,
            NormalizedPlate = plate,
            HasUsablePlate = !string.IsNullOrWhiteSpace(plate),
            PrimaryColor = 0,
            SecondaryColor = 0
        };
    }

    private static OutfitSignatureData OutfitSignature(int pedModelHash)
    {
        OutfitSignatureData signature = new OutfitSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            PedModelHash = pedModelHash
        };

        for (int slot = 0; slot <= 11; slot++)
        {
            signature.Components.Add(
                new DrawableVariationData
                {
                    Slot = slot,
                    Drawable = 0,
                    Texture = 0,
                    Palette = 0
                });
        }

        for (int slot = 0; slot <= 7; slot++)
        {
            signature.Props.Add(
                new PropVariationData
                {
                    Slot = slot,
                    Drawable = 0,
                    Texture = 0
                });
        }

        return signature;
    }

    private static AppearanceSignatureData AppearanceSignature(int pedModelHash)
    {
        return new AppearanceSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            PedModelHash = pedModelHash,
            HairDrawable = 0,
            HairTexture = 0,
            FaceDrawable = 0,
            FaceTexture = 0,
            BeardOverlay = 0
        };
    }

    private static DrawableVariationData FindComponent(
        OutfitSignatureData signature,
        int slot)
    {
        foreach (DrawableVariationData component in signature.Components)
        {
            if (component != null && component.Slot == slot)
            {
                return component;
            }
        }

        Assert.Fail("Composant de tenue introuvable : " + slot);
        return null;
    }

    private static int CountOccurrences(string value, string fragment)
    {
        int count = 0;
        int position = 0;
        while (!string.IsNullOrEmpty(value))
        {
            int found = value.IndexOf(fragment, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }

            count++;
            position = found + fragment.Length;
        }

        return count;
    }

    private static void SeedRecognition(
        RecognitionProfileData profile,
        int wantedFloor,
        string plate,
        DateTime nowUtc)
    {
        int modelHash = 8000 + wantedFloor;
        int pedModelHash = 9000 + wantedFloor;
        profile.VehicleEvidence.Add(
            new VehicleEvidenceState
            {
                Active = true,
                SourceEpisodeId = wantedFloor,
                WantedFloor = wantedFloor,
                CreatedUtc = nowUtc.AddMinutes(-1),
                ExpiresUtc = nowUtc.AddMinutes(20),
                Signature = VehicleSignature(modelHash, plate)
            });
        profile.OutfitEvidence.Add(
            new OutfitEvidenceState
            {
                Active = true,
                SourceEpisodeId = wantedFloor,
                WantedFloor = wantedFloor,
                CreatedUtc = nowUtc.AddMinutes(-1),
                ExpiresUtc = nowUtc.AddMinutes(20),
                Signature = OutfitSignature(pedModelHash)
            });
        profile.AppearanceEvidence = new AppearanceEvidenceState
        {
            Active = true,
            SourceEpisodeId = wantedFloor,
            Signature = AppearanceSignature(pedModelHash),
            OutfitReference = OutfitSignature(pedModelHash)
        };
        profile.SearchZone = new SearchZoneState
        {
            Active = true,
            SourceEpisodeId = wantedFloor,
            WantedFloor = wantedFloor,
            Center = new PositionData { X = 1.0f, Y = 2.0f, Z = 3.0f },
            Radius = RecognitionPolicy.GetZoneRadius(wantedFloor),
            CreatedUtc = nowUtc.AddMinutes(-1),
            ExpiresUtc = nowUtc.AddMinutes(20),
            GraceUntilUtc = nowUtc.AddSeconds(-1),
            LastRecognitionUtc = DateTime.MinValue
        };
    }

    private static void AssertRecognitionPresent(
        RecognitionProfileData profile,
        string expectedPlate)
    {
        Assert.AreEqual(1, profile.VehicleEvidence.Count);
        Assert.AreEqual(expectedPlate, profile.VehicleEvidence[0].Signature.NormalizedPlate);
        Assert.AreEqual(1, profile.OutfitEvidence.Count);
        Assert.IsTrue(profile.AppearanceEvidence.Active);
        Assert.IsTrue(profile.SearchZone.Active);
    }

    private static void AssertRecognitionCleared(RecognitionProfileData profile)
    {
        Assert.AreEqual(0, profile.VehicleEvidence.Count);
        Assert.AreEqual(0, profile.OutfitEvidence.Count);
        Assert.IsFalse(profile.AppearanceEvidence.Active);
        Assert.IsFalse(profile.SearchZone.Active);
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo[] methods = target.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic);
        List<MethodInfo> matches = new List<MethodInfo>();

        foreach (MethodInfo method in methods)
        {
            if (string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.GetParameters().Length == arguments.Length)
            {
                matches.Add(method);
            }
        }

        Assert.AreEqual(1, matches.Count, "Méthode privée ambiguë ou introuvable : " + methodName);
        return matches[0].Invoke(target, arguments);
    }

    private static object InvokeStatic(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(DonJJusticeRecognitionScript).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Méthode statique privée introuvable : " + methodName);
        return method.Invoke(null, arguments);
    }

    private static T GetPrivateConstant<T>(string fieldName)
    {
        FieldInfo field = typeof(DonJJusticeRecognitionScript).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Constante privée introuvable : " + fieldName);
        return (T)field.GetRawConstantValue();
    }

    private static string ExtractRecognitionMethod(string methodName)
    {
        string source = File.ReadAllText(GetRecognitionSourcePath());
        int nameIndex = source.IndexOf(
            "private void " + methodName + "(",
            StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            nameIndex = source.IndexOf(
                "private bool " + methodName + "(",
                StringComparison.Ordinal);
        }
        Assert.IsTrue(nameIndex >= 0, "Méthode source introuvable : " + methodName);
        int openingBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openingBrace > nameIndex, "Corps source introuvable : " + methodName);

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
                    return source.Substring(nameIndex, index - nameIndex + 1);
                }
            }
        }

        Assert.Fail("Fin du corps source introuvable : " + methodName);
        return string.Empty;
    }

    private static string GetRecognitionSourcePath()
    {
        DirectoryInfo current = new FileInfo(GetCompiledTestSourcePath()).Directory;
        while (current != null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "src",
                "DonJEnemySpawner",
                "JusticeRecognition",
                "DonJJusticeRecognition.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        Assert.Fail("Racine du dépôt introuvable depuis la sortie de tests.");
        return string.Empty;
    }

    private static string GetCompiledTestSourcePath(
        [CallerFilePath] string sourcePath = "")
    {
        return sourcePath;
    }

    private static void AssertContainsInOrder(string source, params string[] fragments)
    {
        int position = 0;
        foreach (string fragment in fragments)
        {
            int found = source.IndexOf(fragment, position, StringComparison.Ordinal);
            Assert.IsTrue(
                found >= position,
                "Fragment absent ou désordonné : " + fragment);
            position = found + fragment.Length;
        }
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(target);
    }

    private static void ResetBridgeState()
    {
        Type bridgeType = typeof(JusticeRecognitionBridge);
        SetStaticField(bridgeType, "_instance", null);
        SetStaticField(bridgeType, "_desiredEnabled", null);
        SetStaticField(bridgeType, "_desiredRuntimeSuspended", null);
        SetStaticField(bridgeType, "_desiredActiveProfileId", null);
        SetStaticField(bridgeType, "_wantedMinimumHandler", null);
        SetStaticField(bridgeType, "_nextCriticalCommandId", 0L);
        SetStaticField(bridgeType, "_pendingCurrentProfileCapture", null);
        SetStaticField(bridgeType, "_pendingCurrentProfileClear", null);
        SetStaticField(bridgeType, "_pendingGlobalClear", null);
        SetStaticField(bridgeType, "_criticalIntentStore", null);
        SetStaticField(bridgeType, "_criticalIntentsLoaded", false);
        SetStaticField(bridgeType, "_criticalIntentPathOverride", null);
        ClearStaticDictionary(bridgeType, "PendingProfileCaptureReasons");
        ClearStaticDictionary(bridgeType, "PendingProfileClearReasons");
    }

    private static void ClearStaticDictionary(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Dictionnaire statique introuvable : " + fieldName);
        System.Collections.IDictionary dictionary =
            field.GetValue(null) as System.Collections.IDictionary;
        Assert.IsNotNull(dictionary);
        dictionary.Clear();
    }

    private static int GetStaticDictionaryCount(string fieldName)
    {
        FieldInfo field = typeof(JusticeRecognitionBridge).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Dictionnaire statique introuvable : " + fieldName);
        System.Collections.IDictionary dictionary =
            field.GetValue(null) as System.Collections.IDictionary;
        Assert.IsNotNull(dictionary);
        return dictionary.Count;
    }

    private static void SetStaticField(Type type, string fieldName, object value)
    {
        FieldInfo field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ statique introuvable : " + fieldName);
        field.SetValue(null, value);
    }

    private sealed class RuntimeHarness : IDisposable
    {
        private const ulong AddBlipForRadiusHash = 0x46818D79B1F7499AUL;

        private readonly string _directory;

        public RuntimeHarness()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "DonJJusticeRecognitionRuntimeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            Logger = new RecognitionLogger(Path.Combine(_directory, "Recognition.log"));
            Store = new RecognitionStore(Path.Combine(_directory, "Recognition.xml"), Logger);
            SaveData = new JusticeRecognitionSaveData();
            Profile = SaveData.GetOrCreateProfile("Michael");
            Script = new DonJJusticeRecognitionScript();

            SetField(Script, "_logger", Logger);
            SetField(Script, "_store", Store);
            SetField(Script, "_saveData", SaveData);
            SetField(Script, "_radiusBlip", new RadiusBlipController(Logger));
            SetField(Script, "_enabled", true);
            SetField(Script, "_runtimeSuspended", false);
            SetField(Script, "_currentProfile", Profile);
            SetField(Script, "_currentProfileId", "Michael");

            StubRuntime.NativeCallHandler = HandleNativeCall;
        }

        public DonJJusticeRecognitionScript Script { get; private set; }
        public RecognitionLogger Logger { get; private set; }
        public RecognitionStore Store { get; private set; }
        public JusticeRecognitionSaveData SaveData { get; private set; }
        public RecognitionProfileData Profile { get; private set; }

        public string ReadLog()
        {
            string path = Path.Combine(_directory, "Recognition.log");
            return File.Exists(path)
                ? File.ReadAllText(path)
                : "Aucun événement runtime journalisé.";
        }

        public void Dispose()
        {
            JusticeRecognitionBridge.Detach(Script);
            JusticeRecognitionBridge.UnbindWantedMinimum();
            ResetBridgeState();

            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch
            {
                // Je laisse le nettoyage du dossier temporaire au système si un antivirus le verrouille brièvement.
            }
        }

        private object HandleNativeCall(ulong hash, object[] arguments)
        {
            if (hash == RecognitionNativeHashes.IsPlayerSwitchInProgress)
            {
                return false;
            }

            if (hash == RecognitionNativeHashes.DoesEntityExist)
            {
                // Je rends les entités construites par le scénario visibles aux garde-fous natifs.
                return true;
            }

            if (hash == AddBlipForRadiusHash)
            {
                // Je laisse le contrôleur suivre le cas sûr où GTA refuse de créer le blip.
                return 0;
            }

            return null;
        }
    }
}
#endif
