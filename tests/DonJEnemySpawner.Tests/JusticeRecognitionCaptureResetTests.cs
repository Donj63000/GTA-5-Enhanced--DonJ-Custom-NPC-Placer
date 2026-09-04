using System;
using System.IO;
using System.Reflection;
using DonJ.JusticeRecognition;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeRecognitionCaptureResetTests
{
    [TestMethod]
    public void CaptureReset_ClearsTheThreeRecognitionIconsAndRemainsIdempotent()
    {
        RecognitionProfileData profile = new RecognitionProfileData
        {
            ProfileId = "Franklin",
            LastEpisodeId = 42L,
            AppearanceEvidence = new AppearanceEvidenceState
            {
                Active = true,
                SourceEpisodeId = 42L
            },
            SearchZone = new SearchZoneState
            {
                Active = true,
                SourceEpisodeId = 42L,
                WantedFloor = 4,
                Radius = 900.0f
            }
        };

        profile.VehicleEvidence.Add(
            new VehicleEvidenceState
            {
                Active = true,
                SourceEpisodeId = 42L,
                WantedFloor = 4,
                Signature = new VehicleSignatureData
                {
                    IsValid = true,
                    HasUsablePlate = true,
                    NormalizedPlate = "CAPTURE42"
                }
            });
        profile.OutfitEvidence.Add(
            new OutfitEvidenceState
            {
                Active = true,
                SourceEpisodeId = 42L,
                WantedFloor = 4,
                Signature = new OutfitSignatureData()
            });

        MethodInfo reset = typeof(DonJJusticeRecognitionScript).GetMethod(
            "ClearProfileRecognitionData",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(reset);

        // Je rejoue volontairement la même capture : le nettoyage doit rester
        // terminal sans recréer d'indice ni altérer l'identité du profil.
        reset.Invoke(null, new object[] { profile });
        reset.Invoke(null, new object[] { profile });

        Assert.AreEqual("Franklin", profile.ProfileId);
        Assert.AreEqual(
            42L,
            profile.LastEpisodeId,
            "Je conserve la séquence anti-rejeu tout en supprimant les signalements.");
        Assert.IsNotNull(profile.VehicleEvidence);
        Assert.AreEqual(0, profile.VehicleEvidence.Count, "L'icône plaque doit disparaître.");
        Assert.IsNotNull(profile.OutfitEvidence);
        Assert.AreEqual(0, profile.OutfitEvidence.Count, "L'icône tenue doit disparaître.");
        Assert.IsNotNull(profile.AppearanceEvidence);
        Assert.IsFalse(profile.AppearanceEvidence.Active);
        Assert.AreEqual(0L, profile.AppearanceEvidence.SourceEpisodeId);
        Assert.IsNotNull(profile.SearchZone);
        Assert.IsFalse(profile.SearchZone.Active, "L'icône mandat local doit disparaître.");
        Assert.AreEqual(0L, profile.SearchZone.SourceEpisodeId);
        Assert.AreEqual(0, profile.SearchZone.WantedFloor);
        Assert.AreEqual(0.0f, profile.SearchZone.Radius, 0.001f);
    }

    [TestMethod]
    public void CustodyBoundaries_ResetRecognitionAfterCellTransferAndPaidArrest()
    {
        string root = GetRepositoryRoot();
        string custody = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Custody.cs"));
        string integration = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Recognition.cs"));
        string durableRecognitionReset = ExtractMethod(
            integration,
            "private bool EnsureJusticeRecognitionCaptureResetDurable(string reason)",
            "private void ResetJusticeRecognitionCaptureResetConfirmation()");

        string completedTransfer = ExtractMethod(
            custody,
            "private void CompleteJusticeCustodyTransfer(Ped player, int now)",
            "private bool TrySecureJusticeCustodyAdmission(Ped player, int now)");
        string secureAdmission = ExtractMethod(
            custody,
            "private bool TrySecureJusticeCustodyAdmission(Ped player, int now)",
            "private void ResetJusticeCustodyAdmissionWantedStability(int now)");
        string admissionFinalization = ExtractMethod(
            custody,
            "private void FinalizeJusticeCustodyAdmissionAfterFadeIn(",
            "private JusticePreJudgmentHoldingSource GetJusticeCustodyAdmissionHoldingSource()");
        string beginTransfer = ExtractMethod(
            custody,
            "private void JusticeBeginCustodyTransfer(bool deathCapture)",
            "private void JusticeUpdateCustody(Ped player, int now)");

        AssertContainsInOrder(
            completedTransfer,
            "if (!_justiceCustodyTransferPrecommitConfirmed)",
            "_justiceCustodyTransferPrecommitConfirmed = true;",
            "if (!EnsureJusticeRecognitionCaptureResetDurable(",
            "EnforceJusticePreJudgmentHoldingControlLock(player);",
            "EnsureJusticeInventoryReadyForCustodyTransfer(player, now)",
            "if (!transferred)",
            "_justiceCustodyAdmissionPositionEstablished = true;",
            "PrimeJusticeCustodyGuardDamageFrontsForAdmission(player);",
            "if (!TrySecureJusticeCustodyAdmission(player, now))",
            "_justiceCaseState.Phase = JusticePhase.Incarcerated;",
            "EnsureJusticeCustodyScene(now);",
            "PersistJusticeCriticalPrecommitRedundantly(",
            "RestoreJusticeCustodyRespawnTransferMask()",
            "TryFinishJusticeCustodyAdmissionFadeIn(",
            "FinalizeJusticeCustodyAdmissionAfterFadeIn(layout, now)");
        AssertContainsInOrder(
            admissionFinalization,
            "_justiceCustodyRespawnTransferPending = false;",
            "ClearPendingJusticeDeathCapture();",
            "_justiceCustodyLastTickAt = now;");
        Assert.AreEqual(
            1,
            CountOccurrences(
                completedTransfer,
                "EnsureJusticeRecognitionCaptureResetDurable("),
            "Je ne crée qu'une intention de reset à la frontière commune aux arrestations et morts policières.");
        Assert.AreEqual(
            1,
            CountOccurrences(
                completedTransfer,
                "RestoreJusticeCustodyRespawnTransferMask()"),
            "Le transfert réussi ne peut rendre l'écran qu'une fois, tout à la fin.");
        AssertContainsInOrder(
            secureAdmission,
            "SuppressJusticeRecognitionWantedLoss(",
            "ClearJusticeWantedLevelOnceDetailed()",
            "wantedClear != JusticeWantedClearResult.Succeeded",
            "GetJusticeWantedLevelSafe() != 0",
            "SetJusticeCustodyPoliceSuppression(true)",
            "_justiceCustodyAdmissionWantedStabilityStarted = true;",
            "JusticeCustodyAdmissionWantedStabilityMs",
            "return true;");
        Assert.IsFalse(
            secureAdmission.Contains("TryRestoreJusticeCustodyRespawnTransferMask("),
            "La vérification wanted/police ne doit jamais rendre elle-même le masque.");
        Assert.IsFalse(
            completedTransfer.Contains("DO_SCREEN_FADE_IN"),
            "Le transfert passe uniquement par le propriétaire idempotent du masque.");
        AssertContainsInOrder(
            beginTransfer,
            "if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L)",
            "if (!EnsureJusticeRecognitionCaptureResetDurable(",
            "return;",
            "SuppressJusticeRecognitionWantedLoss(",
            "ResetJusticeCustodyPersistentFields(",
            "JusticePrepareLegalReleaseState();");
        Assert.AreEqual(
            1,
            CountOccurrences(
                beginTransfer,
                "EnsureJusticeRecognitionCaptureResetDurable("),
            "Une arrestation soldée par l'amende doit réinitialiser Recognition une seule fois sur cette branche.");
        StringAssert.Contains(
            integration,
            "GetJusticeRecognitionProfileId(_justiceActivePlayerProfileSlot)");
        StringAssert.Contains(
            integration,
            "JusticeRecognitionBridge.NotifyPlayerCaptured(");
        StringAssert.Contains(
            integration,
            "if (NotifyJusticeRecognitionPlayerCaptured(reason))");
        StringAssert.Contains(
            integration,
            "frontière d'arrestation suspendue et retry armé");
        AssertContainsInOrder(
            durableRecognitionReset,
            "_justiceRecognitionCaptureResetConfirmedProfileSlot == profileSlot",
            "_justiceRecognitionCaptureResetConfirmedEpisodeId,",
            "return true;",
            "if (NotifyJusticeRecognitionPlayerCaptured(reason))",
            "_justiceRecognitionCaptureResetConfirmedProfileSlot = profileSlot;",
            "_justiceRecognitionCaptureResetConfirmedEpisodeId =");
        Assert.AreEqual(
            1,
            CountOccurrences(
                durableRecognitionReset,
                "NotifyJusticeRecognitionPlayerCaptured(reason)"),
            "Je ne recrée pas une commande Recognition après sa confirmation pour le même épisode.");
    }

    private static string ExtractMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        Assert.IsTrue(start >= 0, "La frontière de transfert doit rester identifiable.");
        Assert.IsTrue(end > start, "La frontière de transfert doit rester bornée.");
        return source.Substring(start, end - start);
    }

    private static void AssertContainsInOrder(
        string source,
        params string[] markers)
    {
        int cursor = 0;
        for (int index = 0; index < markers.Length; index++)
        {
            int found = source.IndexOf(
                markers[index],
                cursor,
                StringComparison.Ordinal);
            Assert.IsTrue(
                found >= cursor,
                "Le marqueur doit rester ordonné : " + markers[index]);
            cursor = found + markers[index].Length;
        }
    }

    private static int CountOccurrences(string source, string marker)
    {
        int count = 0;
        int cursor = 0;

        while (cursor < source.Length)
        {
            int found = source.IndexOf(marker, cursor, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            count++;
            cursor = found + marker.Length;
        }

        return count;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("La racine du dépôt GTA5modDEV est introuvable.");
        return null;
    }
}
