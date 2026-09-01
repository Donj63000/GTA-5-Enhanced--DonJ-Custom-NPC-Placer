using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using DonJ.JusticeRecognition;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeRecognitionDomainTests
{
    [TestCleanup]
    public void CleanupBridge()
    {
        // Je rends le pont statique neutre afin qu'un test ne contamine jamais le suivant.
        JusticeRecognitionBridge.UnbindWantedMinimum();
    }

    [TestMethod]
    public void RecognitionPolicy_UsesTheExactBalancedTablesForEveryWantedLevel()
    {
        float[] expectedRadii = { 350.0f, 500.0f, 700.0f, 900.0f, 1200.0f };
        int[] expectedZoneSeconds = { 180, 300, 480, 720, 1080 };
        int[] expectedVehicleSeconds = { 480, 720, 1080, 1500, 2100 };
        int[] expectedOutfitSeconds = { 360, 600, 900, 1200, 1800 };

        for (int wantedLevel = 1; wantedLevel <= 5; wantedLevel++)
        {
            int index = wantedLevel - 1;
            Assert.AreEqual(expectedRadii[index], RecognitionPolicy.GetZoneRadius(wantedLevel), 0.001f);
            Assert.AreEqual(expectedZoneSeconds[index], RecognitionPolicy.GetZoneDurationSeconds(wantedLevel));
            Assert.AreEqual(expectedVehicleSeconds[index], RecognitionPolicy.GetVehicleEvidenceDurationSeconds(wantedLevel));
            Assert.AreEqual(expectedOutfitSeconds[index], RecognitionPolicy.GetOutfitEvidenceDurationSeconds(wantedLevel));
        }

        Assert.AreEqual(350.0f, RecognitionPolicy.GetZoneRadius(0), 0.001f);
        Assert.AreEqual(1200.0f, RecognitionPolicy.GetZoneRadius(99), 0.001f);
        Assert.AreEqual(100.0f, RecognitionPolicy.MinimumValidZoneRadius, 0.001f);
        Assert.AreEqual(2000.0f, RecognitionPolicy.MaximumValidZoneRadius, 0.001f);
        Assert.AreEqual(8, RecognitionPolicy.ZoneGraceSeconds);
        Assert.AreEqual(20, RecognitionPolicy.ZoneRecognitionCooldownSeconds);
        Assert.AreEqual(3, RecognitionPolicy.SearchZoneBlipColor);
        Assert.AreEqual(48, RecognitionPolicy.SearchZoneBlipAlpha);
    }

    [TestMethod]
    public void VehicleComparer_RequiresTheSameModelAndPlateAndDistinguishesARepaint()
    {
        VehicleSignatureData evidence = Vehicle(123, "ABC123", 10, 20);

        Assert.AreEqual(
            VehicleMatchKind.Exact,
            VehicleSignatureComparer.Compare(evidence, Vehicle(123, "ABC123", 10, 20)));
        Assert.AreEqual(
            VehicleMatchKind.SameIdentityDifferentPaint,
            VehicleSignatureComparer.Compare(evidence, Vehicle(123, "ABC123", 99, 20)));
        Assert.AreEqual(
            VehicleMatchKind.None,
            VehicleSignatureComparer.Compare(evidence, Vehicle(123, "XYZ999", 10, 20)));
        Assert.AreEqual(
            VehicleMatchKind.None,
            VehicleSignatureComparer.Compare(evidence, Vehicle(456, "ABC123", 10, 20)));
        Assert.AreEqual(
            VehicleMatchKind.None,
            VehicleSignatureComparer.Compare(evidence, VehicleWithoutPlate(123, 10, 20)),
            "Une plaque lisible ne doit jamais retomber sur le modèle et la peinture si la lecture courante échoue.");

        Assert.IsTrue(
            VehicleSignatureComparer.IsSamePersistentIdentity(
                evidence,
                Vehicle(123, "ABC123", 91, 92)),
            "Je conserve l'identité de plaque même après une peinture différente.");
        Assert.IsFalse(
            VehicleSignatureComparer.IsSamePersistentIdentity(
                evidence,
                Vehicle(123, "ZZZ000", 10, 20)));
        Assert.IsFalse(
            VehicleSignatureComparer.IsSamePersistentIdentity(
                evidence,
                VehicleWithoutPlate(123, 10, 20)));
    }

    [TestMethod]
    public void VehicleComparer_FallsBackToModelAndPaintOnlyWhenNoPlateIsUsable()
    {
        VehicleSignatureData evidence = VehicleWithoutPlate(789, 4, 8);

        Assert.AreEqual(
            VehicleMatchKind.Exact,
            VehicleSignatureComparer.Compare(evidence, VehicleWithoutPlate(789, 4, 8)));
        Assert.AreEqual(
            VehicleMatchKind.None,
            VehicleSignatureComparer.Compare(evidence, VehicleWithoutPlate(789, 5, 8)));
        Assert.IsTrue(
            VehicleSignatureComparer.IsSamePersistentIdentity(
                evidence,
                VehicleWithoutPlate(789, 4, 8)));
    }

    [TestMethod]
    public void OutfitComparer_WeightsVisibleClothingAndExcludesHairFromTheOutfit()
    {
        OutfitSignatureData evidence = CompleteOutfit(9001);
        OutfitSignatureData same = evidence.Clone();
        Assert.AreEqual(0.0f, OutfitSignatureComparer.GetDifferenceScore(evidence, same), 0.001f);
        Assert.IsTrue(OutfitSignatureComparer.IsRecognizedMatch(evidence, same));

        OutfitSignatureData changedHair = evidence.Clone();
        FindComponent(changedHair, 2).Drawable += 10;
        Assert.AreEqual(
            0.0f,
            OutfitSignatureComparer.GetDifferenceScore(evidence, changedHair),
            0.001f,
            "Je réserve la coiffure au comparateur d'apparence.");

        OutfitSignatureData changedJacket = evidence.Clone();
        FindComponent(changedJacket, 11).Drawable += 1;
        Assert.AreEqual(2.0f, OutfitSignatureComparer.GetDifferenceScore(evidence, changedJacket), 0.001f);
        Assert.IsFalse(
            OutfitSignatureComparer.IsRecognizedMatch(evidence, changedJacket),
            "Un changement de vêtement principal atteint strictement le seuil d'oubli de la tenue.");

        OutfitSignatureData changedHat = evidence.Clone();
        FindProp(changedHat, 0).Drawable += 1;
        Assert.AreEqual(0.75f, OutfitSignatureComparer.GetDifferenceScore(evidence, changedHat), 0.001f);
        Assert.IsTrue(OutfitSignatureComparer.IsRecognizedMatch(evidence, changedHat));

        OutfitSignatureData otherHero = evidence.Clone();
        otherHero.PedModelHash++;
        Assert.IsFalse(OutfitSignatureComparer.IsRecognizedMatch(evidence, otherHero));
    }

    [TestMethod]
    public void OutfitComparer_DetectsAFaceMaskFromComponentOne()
    {
        OutfitSignatureData outfit = CompleteOutfit(9002);
        FindComponent(outfit, 1).Drawable = 0;
        Assert.IsFalse(OutfitSignatureComparer.HasFaceMask(outfit));

        FindComponent(outfit, 1).Drawable = 3;
        Assert.IsTrue(OutfitSignatureComparer.HasFaceMask(outfit));
    }

    [TestMethod]
    public void AppearanceComparer_RequiresHairFaceAndKnownBeardButAllowsAnUnreadableBeard()
    {
        AppearanceSignatureData evidence = Appearance(101, 2, 3, 4, 5, 6);
        Assert.IsTrue(
            AppearanceSignatureComparer.IsRecognizedMatch(
                evidence,
                Appearance(101, 2, 3, 4, 5, 6)));
        Assert.IsFalse(
            AppearanceSignatureComparer.IsRecognizedMatch(
                evidence,
                Appearance(101, 9, 3, 4, 5, 6)));
        Assert.IsFalse(
            AppearanceSignatureComparer.IsRecognizedMatch(
                evidence,
                Appearance(101, 2, 3, 4, 5, 7)));
        Assert.IsTrue(
            AppearanceSignatureComparer.IsRecognizedMatch(
                evidence,
                Appearance(101, 2, 3, 4, 5, -1)),
            "Je ne produis pas de faux négatif lorsque la native barbe n'est pas lisible.");
        Assert.IsFalse(
            AppearanceSignatureComparer.IsRecognizedMatch(
                evidence,
                Appearance(202, 2, 3, 4, 5, 6)));
    }

    [TestMethod]
    public void HudConversion_MapsPhysicalPixelsToTheLogical1280By720Canvas()
    {
        PointF center = ReflectionPngSprite.ConvertPixelPointToLogical(960, 540, 1920, 1080);
        SizeF size = ReflectionPngSprite.ConvertPixelSizeToLogical(192, 108, 1920, 1080);

        Assert.AreEqual(640.0f, center.X, 0.001f);
        Assert.AreEqual(360.0f, center.Y, 0.001f);
        Assert.AreEqual(128.0f, size.Width, 0.001f);
        Assert.AreEqual(72.0f, size.Height, 0.001f);

        SizeF nonNegative = ReflectionPngSprite.ConvertPixelSizeToLogical(-50, -70, 0, 0);
        Assert.AreEqual(0.0f, nonNegative.Width, 0.001f);
        Assert.AreEqual(0.0f, nonNegative.Height, 0.001f);
    }

    [TestMethod]
    public void ProfileIds_AreCanonicalAndRejectUnknownOrEmptyIdentities()
    {
        Assert.AreEqual("Michael", DonJJusticeRecognitionScript.NormalizeProfileId(" michael "));
        Assert.AreEqual("Franklin", DonJJusticeRecognitionScript.NormalizeProfileId("FRANKLIN"));
        Assert.AreEqual("Trevor", DonJJusticeRecognitionScript.NormalizeProfileId("Trevor"));
        Assert.IsNull(DonJJusticeRecognitionScript.NormalizeProfileId("OnlinePlayer"));
        Assert.IsNull(DonJJusticeRecognitionScript.NormalizeProfileId(" "));
        Assert.IsNull(DonJJusticeRecognitionScript.NormalizeProfileId(null));
    }

    [TestMethod]
    public void WantedBridge_UsesTheBooleanResultAndFailsClosedOnMissingOrThrowingHandlers()
    {
        JusticeRecognitionBridge.UnbindWantedMinimum();
        Assert.IsFalse(JusticeRecognitionBridge.HasWantedMinimumHandler());
        Assert.IsFalse(JusticeRecognitionBridge.TryApplyWantedMinimum(4));
        WantedMinimumApplicationResult missing =
            JusticeRecognitionBridge.ApplyWantedMinimumAtomically(4);
        Assert.IsFalse(missing.HandlerPresent);
        Assert.IsFalse(missing.Applied);

        int receivedLevel = 0;
        JusticeRecognitionBridge.BindWantedMinimum(
            delegate(int level)
            {
                receivedLevel = level;
                return level == 4;
            });

        Assert.IsTrue(JusticeRecognitionBridge.HasWantedMinimumHandler());
        WantedMinimumApplicationResult accepted =
            JusticeRecognitionBridge.ApplyWantedMinimumAtomically(4);
        Assert.IsTrue(accepted.HandlerPresent);
        Assert.IsTrue(accepted.Applied);
        Assert.AreEqual(4, receivedLevel);
        Assert.IsFalse(JusticeRecognitionBridge.TryApplyWantedMinimum(3));

        JusticeRecognitionBridge.BindWantedMinimum(
            delegate(int level)
            {
                throw new InvalidOperationException("échec simulé");
            });
        Assert.IsFalse(JusticeRecognitionBridge.TryApplyWantedMinimum(5));
    }

    [TestMethod]
    public void WritableDirectoryProbe_FallsBackToInjectedLocalAppDataAndLeavesNoProbeFile()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string blockedRuntime = Path.Combine(directory, "runtime-bloque");
                File.WriteAllText(blockedRuntime, "Je simule un chemin non inscriptible.");

                string localAppData = Path.Combine(directory, "local-app-data");
                string assemblyDirectory = Path.Combine(directory, "assembly");

                string resolved =
                    DonJJusticeRecognitionScript.ResolveWritableDataDirectoryForTests(
                        blockedRuntime,
                        assemblyDirectory,
                        localAppData);

                string expected = Path.Combine(
                    localAppData,
                    "DonJEnemySpawner",
                    "JusticeRecognition");

                Assert.AreEqual(Path.GetFullPath(expected), resolved);
                Assert.IsTrue(Directory.Exists(resolved));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(
                        resolved,
                        ".donj-recognition-write-probe-*.tmp").Length,
                    "Je nettoie toujours le fichier de création, flush et suppression.");
            });
    }

    [TestMethod]
    public void Store_MarkDirtyFlushesOnlyWhenDueAndRoundTripsEveryProfileIndependently()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                RecognitionLogger logger = new RecognitionLogger(Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore store = new RecognitionStore(path, logger);
                JusticeRecognitionSaveData data = CreateSeparatedProfiles();

                store.MarkDirty(data, 1000);
                store.FlushIfDue(1499);
                Assert.IsFalse(File.Exists(path), "Je respecte le délai de coalescence des écritures.");

                store.FlushIfDue(1500);
                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".bak"));
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(path),
                    File.ReadAllBytes(path + ".bak"),
                    "L'acquittement exige deux exemplaires valides et à jour.");

                RecognitionStore reloadedStore = new RecognitionStore(path, logger);
                JusticeRecognitionSaveData reloaded = reloadedStore.Load();
                RecognitionProfileData michael = reloaded.GetOrCreateProfile("Michael");
                RecognitionProfileData franklin = reloaded.GetOrCreateProfile("Franklin");
                RecognitionProfileData trevor = reloaded.GetOrCreateProfile("Trevor");

                Assert.AreEqual(11L, michael.LastEpisodeId);
                Assert.AreEqual("MICHAEL1", michael.VehicleEvidence[0].Signature.NormalizedPlate);
                Assert.AreEqual(22L, franklin.LastEpisodeId);
                Assert.AreEqual(4, franklin.OutfitEvidence[0].WantedFloor);
                Assert.AreEqual(33L, trevor.LastEpisodeId);
                Assert.AreEqual(5, trevor.SearchZone.WantedFloor);
                Assert.AreEqual(3, reloaded.Profiles.Count);
            });
    }

    [TestMethod]
    public void Store_ForceSaveAcknowledgesFailureAndKeepsTheWritePending()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string blockedDirectory = Path.Combine(directory, "bloque");
                File.WriteAllText(blockedDirectory, "Ce fichier bloque CreateDirectory.");

                RecognitionLogger logger = new RecognitionLogger(
                    Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore store = new RecognitionStore(
                    Path.Combine(blockedDirectory, "JusticeRecognition.xml"),
                    logger);

                Assert.IsFalse(
                    store.ForceSave(new JusticeRecognitionSaveData()),
                    "Un clear critique doit recevoir un acquittement négatif fiable.");
                Assert.IsTrue(
                    store.IsDirty,
                    "La donnée reste en attente du retry cadencé après l'échec.");
            });
    }

    [TestMethod]
    public void Store_RecoversACompleteTemporaryFileAndRejectsAFutureSchema()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                RecognitionLogger logger = new RecognitionLogger(Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore store = new RecognitionStore(path, logger);
                JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();
                data.GetOrCreateProfile("Michael").LastEpisodeId = 41;
                Assert.IsTrue(store.ForceSave(data));

                File.Move(path, path + ".tmp");
                File.SetLastWriteTimeUtc(
                    path + ".tmp",
                    DateTime.UtcNow.AddSeconds(2));
                JusticeRecognitionSaveData recovered = new RecognitionStore(path, logger).Load();
                Assert.AreEqual(41L, recovered.GetOrCreateProfile("Michael").LastEpisodeId);
                Assert.IsTrue(File.Exists(path), "Je republie le temporaire valide comme primaire.");

                string futurePath = Path.Combine(directory, "Future.xml");
                string futureXml = File.ReadAllText(path)
                    .Replace("<SchemaVersion>1</SchemaVersion>", "<SchemaVersion>99</SchemaVersion>");
                File.WriteAllText(futurePath, futureXml);

                JusticeRecognitionSaveData rejected =
                    new RecognitionStore(futurePath, logger).Load();
                Assert.AreEqual(RecognitionPolicy.SchemaVersion, rejected.SchemaVersion);
                Assert.AreEqual(0, rejected.Profiles.Count);
            });
    }

    [TestMethod]
    public void Store_PrefersANewerValidTemporaryAndRepublishesBothCopies()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                RecognitionLogger logger = new RecognitionLogger(
                    Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore store = new RecognitionStore(path, logger);

                JusticeRecognitionSaveData oldData = new JusticeRecognitionSaveData();
                oldData.GetOrCreateProfile("Michael").LastEpisodeId = 10;
                Assert.IsTrue(store.ForceSave(oldData));

                string stagingPath = Path.Combine(directory, "Staging.xml");
                RecognitionStore staging = new RecognitionStore(stagingPath, logger);
                JusticeRecognitionSaveData newData = new JusticeRecognitionSaveData();
                newData.GetOrCreateProfile("Michael").LastEpisodeId = 99;
                Assert.IsTrue(staging.ForceSave(newData));

                File.Move(stagingPath, path + ".tmp");
                DateTime oldWrite = DateTime.UtcNow.AddMinutes(-2);
                File.SetLastWriteTimeUtc(path, oldWrite);
                File.SetLastWriteTimeUtc(path + ".bak", oldWrite);
                File.SetLastWriteTimeUtc(path + ".tmp", DateTime.UtcNow);

                JusticeRecognitionSaveData loaded =
                    new RecognitionStore(path, logger).Load();

                Assert.AreEqual(
                    99L,
                    loaded.GetOrCreateProfile("Michael").LastEpisodeId);
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(path),
                    File.ReadAllBytes(path + ".bak"));
            });
    }

    [TestMethod]
    public void Store_RecoversBackupTemporaryAndBothRollbacksAsTheNewestValidCopy()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string[] recoverySuffixes =
                {
                    ".bak.tmp",
                    ".rollback",
                    ".bak.rollback"
                };

                for (int index = 0; index < recoverySuffixes.Length; index++)
                {
                    string caseDirectory = Path.Combine(
                        directory,
                        "variant-" + index);
                    Directory.CreateDirectory(caseDirectory);

                    string path = Path.Combine(
                        caseDirectory,
                        "JusticeRecognition.xml");
                    RecognitionLogger logger = new RecognitionLogger(
                        Path.Combine(caseDirectory, "JusticeRecognition.log"));

                    JusticeRecognitionSaveData oldData =
                        new JusticeRecognitionSaveData();
                    oldData.GetOrCreateProfile("Michael").LastEpisodeId = 10;
                    Assert.IsTrue(
                        new RecognitionStore(path, logger).ForceSave(oldData));

                    DateTime oldWrite = DateTime.UtcNow.AddMinutes(-10);
                    File.SetLastWriteTimeUtc(path, oldWrite);
                    File.SetLastWriteTimeUtc(path + ".bak", oldWrite);

                    string stagingPath = Path.Combine(
                        caseDirectory,
                        "Staging.xml");
                    JusticeRecognitionSaveData newestData =
                        new JusticeRecognitionSaveData();
                    newestData.GetOrCreateProfile("Michael").LastEpisodeId =
                        100 + index;
                    Assert.IsTrue(
                        new RecognitionStore(stagingPath, logger)
                            .ForceSave(newestData));

                    string recoveryPath =
                        path + recoverySuffixes[index];
                    File.Move(stagingPath, recoveryPath);
                    File.SetLastWriteTimeUtc(
                        recoveryPath,
                        DateTime.UtcNow.AddMinutes(1));

                    JusticeRecognitionSaveData loaded =
                        new RecognitionStore(path, logger).Load();

                    Assert.AreEqual(
                        100L + index,
                        loaded.GetOrCreateProfile("Michael").LastEpisodeId,
                        "Je récupère toujours la variante transactionnelle la plus récente.");
                    Assert.IsTrue(File.Exists(path));
                    Assert.IsTrue(File.Exists(path + ".bak"));
                    CollectionAssert.AreEqual(
                        File.ReadAllBytes(path),
                        File.ReadAllBytes(path + ".bak"),
                        "Je republie la récupération dans une paire redondante identique.");

                    JusticeRecognitionSaveData restarted =
                        new RecognitionStore(path, logger).Load();
                    Assert.AreEqual(
                        100L + index,
                        restarted.GetOrCreateProfile("Michael").LastEpisodeId);
                }
            });
    }

    [TestMethod]
    public void Store_QuarantinesEveryCorruptVariantAndPublishesAFreshRedundantPair()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                string[] variants =
                {
                    path,
                    path + ".bak",
                    path + ".tmp",
                    path + ".bak.tmp",
                    path + ".rollback",
                    path + ".bak.rollback"
                };

                for (int index = 0; index < variants.Length; index++)
                {
                    File.WriteAllText(
                        variants[index],
                        "xml-corrompu-" + index);
                }

                string logPath = Path.Combine(
                    directory,
                    "JusticeRecognition.log");
                RecognitionStore store = new RecognitionStore(
                    path,
                    new RecognitionLogger(logPath));

                JusticeRecognitionSaveData recovered = store.Load();

                Assert.AreEqual(RecognitionPolicy.SchemaVersion, recovered.SchemaVersion);
                Assert.AreEqual(0, recovered.Profiles.Count);
                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".bak"));
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(path),
                    File.ReadAllBytes(path + ".bak"));
                Assert.IsFalse(File.Exists(path + ".tmp"));
                Assert.IsFalse(File.Exists(path + ".bak.tmp"));
                Assert.IsFalse(File.Exists(path + ".rollback"));
                Assert.IsFalse(File.Exists(path + ".bak.rollback"));

                string quarantine = path + ".corrupt-quarantine";
                Assert.IsTrue(Directory.Exists(quarantine));
                Assert.AreEqual(
                    variants.Length,
                    Directory.GetFiles(quarantine, "*.corrupt").Length,
                    "Je conserve les six fichiers illisibles pour le diagnostic.");
                StringAssert.Contains(
                    File.ReadAllText(logPath),
                    "save_corrupt_variants_quarantined");
            });
    }

    [TestMethod]
    public void Store_ResumesAnInterruptedQuarantineWithoutReturningAnEmptyUnpublishedSave()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                string[] variants =
                {
                    path,
                    path + ".bak",
                    path + ".tmp",
                    path + ".bak.tmp",
                    path + ".rollback",
                    path + ".bak.rollback"
                };

                for (int index = 0; index < variants.Length; index++)
                {
                    File.WriteAllText(
                        variants[index],
                        "variante-invalide-" + index);
                }

                int moveCount = 0;
                Action<string, string> interruptedMover =
                    delegate(string source, string destination)
                    {
                        if (moveCount == 2)
                        {
                            throw new IOException(
                                "Je simule une coupure au milieu de la quarantaine.");
                        }

                        moveCount++;
                        File.Move(source, destination);
                    };

                RecognitionLogger logger = new RecognitionLogger(
                    Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore interruptedStore = new RecognitionStore(
                    path,
                    logger,
                    interruptedMover);

                Assert.ThrowsException<InvalidDataException>(
                    delegate
                    {
                        interruptedStore.Load();
                    },
                    "Je reste fermé tant qu'une variante corrompue n'est pas isolée.");
                Assert.IsFalse(
                    File.Exists(path) && File.Exists(path + ".bak"),
                    "Une quarantaine partielle ne doit pas ressembler à une paire neuve acquittée.");

                RecognitionStore resumedStore = new RecognitionStore(path, logger);
                JusticeRecognitionSaveData resumed = resumedStore.Load();

                Assert.AreEqual(RecognitionPolicy.SchemaVersion, resumed.SchemaVersion);
                Assert.AreEqual(0, resumed.Profiles.Count);
                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".bak"));
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(path),
                    File.ReadAllBytes(path + ".bak"));

                string quarantine = path + ".corrupt-quarantine";
                Assert.AreEqual(
                    variants.Length,
                    Directory.GetFiles(quarantine, "*.corrupt").Length,
                    "La reprise conserve aussi les variantes déplacées avant la coupure.");

                byte[] primaryBeforeRestart = File.ReadAllBytes(path);
                int quarantinedBeforeRestart =
                    Directory.GetFiles(quarantine, "*.corrupt").Length;
                JusticeRecognitionSaveData restarted =
                    new RecognitionStore(path, logger).Load();

                Assert.AreEqual(0, restarted.Profiles.Count);
                CollectionAssert.AreEqual(
                    primaryBeforeRestart,
                    File.ReadAllBytes(path),
                    "Une reprise déjà terminée ne réinitialise pas une seconde fois la paire.");
                Assert.AreEqual(
                    quarantinedBeforeRestart,
                    Directory.GetFiles(quarantine, "*.corrupt").Length);
            });
    }

    [TestMethod]
    public void Store_DoesNotAcknowledgeUntilTheBackupCanBePublishedAndValidated()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                string path = Path.Combine(directory, "JusticeRecognition.xml");
                Directory.CreateDirectory(path + ".bak");
                RecognitionLogger logger = new RecognitionLogger(
                    Path.Combine(directory, "JusticeRecognition.log"));
                RecognitionStore store = new RecognitionStore(path, logger);
                JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();
                data.GetOrCreateProfile("Trevor").LastEpisodeId = 77;

                Assert.IsFalse(store.ForceSave(data));
                Assert.IsTrue(store.IsDirty);
                Assert.IsTrue(
                    File.Exists(path),
                    "Le primaire peut être publié, mais il ne suffit pas à l'ACK critique.");

                Directory.Delete(path + ".bak");
                Assert.IsTrue(store.ForceSave(data));
                Assert.IsFalse(store.IsDirty);
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(path),
                    File.ReadAllBytes(path + ".bak"));
            });
    }

    [TestMethod]
    public void Sanitizer_RemovesEvidenceWithAnInvalidWantedFloorInsteadOfInventingOneStar()
    {
        WithTemporaryDirectory(
            delegate(string directory)
            {
                DateTime now = DateTime.UtcNow;
                RecognitionLogger logger = new RecognitionLogger(Path.Combine(directory, "JusticeRecognition.log"));
                JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();
                RecognitionProfileData profile = data.GetOrCreateProfile("Michael");

                profile.VehicleEvidence.Add(
                    new VehicleEvidenceState
                    {
                        Active = true,
                        WantedFloor = 0,
                        CreatedUtc = now,
                        ExpiresUtc = now.AddMinutes(5),
                        Signature = Vehicle(10, "BADFLOOR", 1, 2)
                    });
                profile.OutfitEvidence.Add(
                    new OutfitEvidenceState
                    {
                        Active = true,
                        WantedFloor = 6,
                        CreatedUtc = now,
                        ExpiresUtc = now.AddMinutes(5),
                        Signature = CompleteOutfit(20)
                    });
                profile.SearchZone = new SearchZoneState
                {
                    Active = true,
                    WantedFloor = -2,
                    Radius = 700.0f,
                    Center = new PositionData { X = 1.0f, Y = 2.0f, Z = 3.0f },
                    CreatedUtc = now,
                    ExpiresUtc = now.AddMinutes(5),
                    GraceUntilUtc = now
                };

                RecognitionDataSanitizer.SanitizeSaveData(data, now, logger);

                Assert.AreEqual(0, profile.VehicleEvidence.Count);
                Assert.AreEqual(0, profile.OutfitEvidence.Count);
                Assert.IsFalse(profile.SearchZone.Active);
                Assert.IsFalse(profile.AppearanceEvidence.Active);
            });
    }

    [TestMethod]
    public void SaveData_GetOrCreateProfileNeverAliasesEvidenceAcrossHeroes()
    {
        JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();
        RecognitionProfileData michael = data.GetOrCreateProfile("Michael");
        RecognitionProfileData franklin = data.GetOrCreateProfile("Franklin");

        michael.VehicleEvidence.Add(
            new VehicleEvidenceState
            {
                Active = true,
                Signature = Vehicle(1, "ONLYMIKE", 2, 3)
            });

        Assert.AreSame(michael, data.GetOrCreateProfile("Michael"));
        Assert.AreNotSame(michael, franklin);
        Assert.AreEqual(1, michael.VehicleEvidence.Count);
        Assert.AreEqual(0, franklin.VehicleEvidence.Count);
        Assert.AreNotSame(michael.SearchZone, franklin.SearchZone);
        Assert.AreNotSame(michael.AppearanceEvidence, franklin.AppearanceEvidence);
    }

    [TestMethod]
    public void RecognitionSource_KeepsProfileIsolationBoundedScansAndFailClosedIntegration()
    {
        string source = File.ReadAllText(GetRecognitionSourcePath());
        string integration = File.ReadAllText(
            Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Recognition.cs"));

        StringAssert.Contains(source, "\"GetNearbyPeds\"");
        Assert.IsFalse(
            source.Contains("World.GetAllPeds("),
            "Je n'autorise jamais un scan global de tous les PNJ comme fallback.");
        StringAssert.Contains(source, "_authoritativeProfileId");
        StringAssert.Contains(source, "NormalizeProfileId");
        StringAssert.Contains(source, "Func<int, bool> _wantedMinimumHandler");
        StringAssert.Contains(source, "ApplyWantedMinimumAtomically");
        StringAssert.Contains(source, "application.HandlerPresent");
        StringAssert.Contains(source, "PublishValidatedTemporary");
        Assert.IsFalse(
            source.Contains("File.Copy("),
            "Je ne publie jamais le temporaire en recopiant sur le primaire.");
        StringAssert.Contains(source, "\"NIBScriptHookVDotNet3\"");
        StringAssert.Contains(source, "MaximumConsecutiveDrawFailures");
        StringAssert.Contains(source, "save_loaded_from_temporary");
        Assert.IsFalse(
            source.Contains("Action<int> _wantedMinimumHandler"),
            "Le module doit connaître le succès réel du setter wanted Justice.");
        Assert.IsFalse(
            source.Contains("C:\\Users\\"),
            "Aucun chemin propre au poste de développement ne doit atteindre le runtime.");

        StringAssert.Contains(integration, "JusticeRecognitionBridge.SetEnabled(false)");
        StringAssert.Contains(integration, "JusticeRecognitionBridge.SetRuntimeSuspended(true)");
        StringAssert.Contains(integration, "JusticeRecognitionBridge.SetActiveProfile(null)");
        StringAssert.Contains(integration, "GetJusticeRecognitionProfileId(profileSlot)");
    }

    [TestMethod]
    public void JusticeLifecycle_ConnectsRecognitionOnlyAtConfirmedBoundaries()
    {
        string root = GetRepositoryRoot();
        string justice = File.ReadAllText(Path.Combine(root, "src", "DonJEnemySpawner", "DonJEnemySpawner.Justice.cs"));
        string custody = File.ReadAllText(Path.Combine(root, "src", "DonJEnemySpawner", "DonJEnemySpawner.Justice.Custody.cs"));
        string profiles = File.ReadAllText(Path.Combine(root, "src", "DonJEnemySpawner", "DonJEnemySpawner.Justice.Profiles.cs"));
        string integration = File.ReadAllText(Path.Combine(root, "src", "DonJEnemySpawner", "DonJEnemySpawner.Justice.Recognition.cs"));

        StringAssert.Contains(justice, "InitializeJusticeRecognitionFailClosed();");
        StringAssert.Contains(justice, "BindAndSynchronizeJusticeRecognition();");
        StringAssert.Contains(justice, "SynchronizeJusticeRecognition(true);");
        StringAssert.Contains(justice, "SuppressJusticeRecognitionWantedLoss(");
        StringAssert.Contains(
            custody,
            "EnsureJusticeRecognitionCaptureResetDurable(");
        StringAssert.Contains(custody, "entrée en détention confirmée");
        StringAssert.Contains(profiles, "ClearJusticeRecognitionProfile(");
        StringAssert.Contains(integration, "gameplaySuspended = IsJusticeRuntimeSuspended(");

        int toggleStart = justice.IndexOf("private void RequestJusticeToggle()", StringComparison.Ordinal);
        int toggleEnd = justice.IndexOf("private bool IsJusticePauseTemporarilyUnsafe()", toggleStart, StringComparison.Ordinal);
        Assert.IsTrue(toggleStart >= 0 && toggleEnd > toggleStart);
        string toggle = justice.Substring(toggleStart, toggleEnd - toggleStart);
        StringAssert.Contains(toggle, "SynchronizeJusticeRecognition(true);");
        Assert.IsFalse(toggle.Contains("ClearJusticeRecognitionProfile("));
        Assert.IsFalse(toggle.Contains("NotifyJusticeRecognitionPlayerCaptured("));

        int amnestyStart = justice.IndexOf(
            "private bool ResumeJusticeAmnestyTransaction()",
            StringComparison.Ordinal);
        int amnestyEnd = justice.IndexOf(
            "private bool TryApplyJusticeAmnestyWantedClear()",
            amnestyStart,
            StringComparison.Ordinal);
        Assert.IsTrue(amnestyStart >= 0 && amnestyEnd > amnestyStart);
        string confirmedAmnesty = justice.Substring(
            amnestyStart,
            amnestyEnd - amnestyStart);
        StringAssert.Contains(
            confirmedAmnesty,
            "amnistie explicitement confirmée");
        StringAssert.Contains(
            confirmedAmnesty,
            "reprise confirmée de l'amnistie explicite");
    }

    [TestMethod]
    public void ProjectCopiesTheThreeJusticeIconsWithoutCompilingAgainstApiV3()
    {
        string project = File.ReadAllText(
            Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.csproj"));

        StringAssert.Contains(project, "Assets\\Justice\\immatriculation.png");
        StringAssert.Contains(project, "Assets\\Justice\\tenue.png");
        StringAssert.Contains(project, "Assets\\Justice\\mandat.png");
        StringAssert.Contains(project, "<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
        Assert.IsFalse(
            project.Contains("<Reference Include=\"NIBScriptHookVDotNet3\""),
            "Je garde CustomSprite comme fournisseur HUD réfléchi sans dépendance de compilation v3.");
    }

    private static JusticeRecognitionSaveData CreateSeparatedProfiles()
    {
        DateTime now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();

        RecognitionProfileData michael = data.GetOrCreateProfile("Michael");
        michael.LastEpisodeId = 11;
        michael.VehicleEvidence.Add(
            new VehicleEvidenceState
            {
                Active = true,
                SourceEpisodeId = 11,
                WantedFloor = 3,
                CreatedUtc = now,
                ExpiresUtc = now.AddMinutes(18),
                Signature = Vehicle(100, "MICHAEL1", 10, 20)
            });

        RecognitionProfileData franklin = data.GetOrCreateProfile("Franklin");
        franklin.LastEpisodeId = 22;
        franklin.OutfitEvidence.Add(
            new OutfitEvidenceState
            {
                Active = true,
                SourceEpisodeId = 22,
                WantedFloor = 4,
                CreatedUtc = now,
                ExpiresUtc = now.AddMinutes(20),
                Signature = CompleteOutfit(200)
            });

        RecognitionProfileData trevor = data.GetOrCreateProfile("Trevor");
        trevor.LastEpisodeId = 33;
        trevor.SearchZone.Active = true;
        trevor.SearchZone.SourceEpisodeId = 33;
        trevor.SearchZone.WantedFloor = 5;
        trevor.SearchZone.Center = new PositionData { X = 10.0f, Y = 20.0f, Z = 30.0f };
        trevor.SearchZone.Radius = 1200.0f;
        trevor.SearchZone.CreatedUtc = now;
        trevor.SearchZone.ExpiresUtc = now.AddMinutes(18);

        return data;
    }

    private static VehicleSignatureData Vehicle(
        int modelHash,
        string plate,
        int primaryColor,
        int secondaryColor)
    {
        return new VehicleSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            ModelHash = modelHash,
            NormalizedPlate = plate,
            HasUsablePlate = true,
            PrimaryColor = primaryColor,
            SecondaryColor = secondaryColor
        };
    }

    private static VehicleSignatureData VehicleWithoutPlate(
        int modelHash,
        int primaryColor,
        int secondaryColor)
    {
        return new VehicleSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            ModelHash = modelHash,
            NormalizedPlate = string.Empty,
            HasUsablePlate = false,
            PrimaryColor = primaryColor,
            SecondaryColor = secondaryColor
        };
    }

    private static OutfitSignatureData CompleteOutfit(int modelHash)
    {
        OutfitSignatureData outfit = new OutfitSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            PedModelHash = modelHash
        };

        for (int slot = 0; slot <= 11; slot++)
        {
            outfit.Components.Add(
                new DrawableVariationData
                {
                    Slot = slot,
                    Drawable = slot + 1,
                    Texture = slot + 2,
                    Palette = slot % 3
                });
        }

        for (int slot = 0; slot <= 7; slot++)
        {
            outfit.Props.Add(
                new PropVariationData
                {
                    Slot = slot,
                    Drawable = slot + 3,
                    Texture = slot + 4
                });
        }

        return outfit;
    }

    private static AppearanceSignatureData Appearance(
        int modelHash,
        int hairDrawable,
        int hairTexture,
        int faceDrawable,
        int faceTexture,
        int beardOverlay)
    {
        return new AppearanceSignatureData
        {
            IsValid = true,
            SignatureVersion = 1,
            PedModelHash = modelHash,
            HairDrawable = hairDrawable,
            HairTexture = hairTexture,
            FaceDrawable = faceDrawable,
            FaceTexture = faceTexture,
            BeardOverlay = beardOverlay
        };
    }

    private static DrawableVariationData FindComponent(OutfitSignatureData outfit, int slot)
    {
        return outfit.Components.Find(component => component != null && component.Slot == slot);
    }

    private static PropVariationData FindProp(OutfitSignatureData outfit, int slot)
    {
        return outfit.Props.Find(prop => prop != null && prop.Slot == slot);
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJRecognitionTests_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            action(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static string GetRecognitionSourcePath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "JusticeRecognition",
            "DonJJusticeRecognition.cs");
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
