using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeCustodySceneMaintenanceTests
{
    [TestMethod]
    public void CustodyScene_DoesNotReplaceADeadOrLostPedBeforeTeardown()
    {
        Assert.AreEqual(
            -1,
            DonJEnemySpawner.SelectJusticeCustodyReplacementSlot(4, 4, 1),
            "Un slot déjà créé reste occupé jusqu'au démontage de la scène.");
        Assert.AreEqual(
            3,
            DonJEnemySpawner.SelectJusticeCustodyReplacementSlot(3, 4, -1),
            "Une scène encore incomplète doit continuer au prochain poste.");
        Assert.AreEqual(
            -1,
            DonJEnemySpawner.SelectJusticeCustodyReplacementSlot(4, 4, -1),
            "Une scène complète ne doit pas créer un PNJ surnuméraire.");

        string source = ReadCustodySource();
        string compaction = ExtractMethodBody(
            source,
            "CompactJusticeCustodyPedList");
        StringAssert.Contains(compaction, "peds[index] = null");
        StringAssert.Contains(compaction, "if (ownedPed)");
        Assert.IsFalse(
            compaction.Contains("DeleteEntitySafe(ped)"),
            "Un cadavre possédé ne doit être ni supprimé ni remplacé pendant la peine.");

        string scene = ExtractMethodBody(source, "EnsureJusticeCustodyScene");
        StringAssert.Contains(scene, "FindJusticeCustodyVacantPedSlot(");
        string vacant = ExtractMethodBody(source, "FindJusticeCustodyVacantPedSlot");
        Assert.IsFalse(vacant.Contains("ped.IsDead"));
    }

    [TestMethod]
    public void CustodyScene_ReturnPolicyKeepsInmatesFreeInsideAndReturnsEscapedPeds()
    {
        Assert.IsFalse(
            DonJEnemySpawner.ShouldCommandJusticeCustodyPedReturn(
                false,
                true,
                10000.0f),
            "Un détenu encore dans le volume jouable ne doit pas être rappelé à un point fixe.");
        Assert.IsTrue(
            DonJEnemySpawner.ShouldCommandJusticeCustodyPedReturn(
                false,
                false,
                1.0f),
            "Un détenu sorti du volume jouable doit être rapatrié par le navmesh.");
        Assert.IsFalse(
            DonJEnemySpawner.ShouldCommandJusticeCustodyPedReturn(
                true,
                true,
                6.25f),
            "Un gardien dans la tolérance de son poste ne doit pas être retaské.");
        Assert.IsTrue(
            DonJEnemySpawner.ShouldCommandJusticeCustodyPedReturn(
                true,
                true,
                6.26f),
            "Un gardien éloigné doit recevoir un ordre de retour.");
    }

    [TestMethod]
    public void CustodyScene_ReturnOrdersWaitForNaturalCombatAndTenSecondsOfCalm()
    {
        Type scriptType = typeof(DonJEnemySpawner);
        FieldInfo retry = scriptType.GetField(
            "JusticeCustodySceneReturnRetryMs",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(retry);
        Assert.IsTrue(
            (int)retry.GetRawConstantValue() >= 5000,
            "Le retour ne doit jamais devenir un ordre IA réémis à chaque tick.");
        FieldInfo calmDelay = scriptType.GetField(
            "JusticeCustodySceneCalmDelayMs",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(calmDelay);
        Assert.AreEqual(10000, (int)calmDelay.GetRawConstantValue());

        int calmUntil = 0;
        bool wasNaturallyBusy = false;
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            true,
            1000,
            ref calmUntil,
            ref wasNaturallyBusy));
        Assert.AreEqual(
            0,
            calmUntil,
            "Le délai ne doit pas commencer avant que GTA observe le calme.");
        Assert.IsTrue(wasNaturallyBusy);
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            false,
            1001,
            ref calmUntil,
            ref wasNaturallyBusy));
        Assert.AreEqual(11001, calmUntil);
        Assert.IsFalse(wasNaturallyBusy);
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            false,
            11000,
            ref calmUntil,
            ref wasNaturallyBusy));
        Assert.IsFalse(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            false,
            11001,
            ref calmUntil,
            ref wasNaturallyBusy));
        Assert.AreEqual(0, calmUntil);
        Assert.IsFalse(wasNaturallyBusy);

        int interruptedCalmUntil = 0;
        bool interruptedWasNaturallyBusy = false;
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            true,
            20000,
            ref interruptedCalmUntil,
            ref interruptedWasNaturallyBusy));
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            false,
            20001,
            ref interruptedCalmUntil,
            ref interruptedWasNaturallyBusy));
        Assert.AreEqual(30001, interruptedCalmUntil);
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            true,
            25000,
            ref interruptedCalmUntil,
            ref interruptedWasNaturallyBusy));
        Assert.AreEqual(
            0,
            interruptedCalmUntil,
            "Une nouvelle réaction naturelle doit annuler le calme partiel.");
        Assert.IsTrue(DonJEnemySpawner.ShouldDelayJusticeCustodyPedReturn(
            false,
            25001,
            ref interruptedCalmUntil,
            ref interruptedWasNaturallyBusy));
        Assert.AreEqual(
            35001,
            interruptedCalmUntil,
            "Les dix secondes doivent repartir après la nouvelle accalmie.");

        string source = ReadCustodySource();
        string scene = ExtractMethodBody(source, "EnsureJusticeCustodyScene");
        string maintenance = ExtractMethodBody(
            source,
            "MaintainJusticeCustodyPedPosts");
        string reset = ExtractMethodBody(
            source,
            "ResetJusticeCustodySceneMaintenanceBuffers");
        string busy = ExtractMethodBody(
            source,
            "IsJusticeCustodyPedNaturallyBusy");
        string spawn = ExtractMethodBody(source, "CreateJusticeCustodyPed");

        StringAssert.Contains(scene, "MaintainJusticeCustodyScenePositions(layout, now)");
        StringAssert.Contains(maintenance, "IsInsideJusticeCustodyAllowedArea");
        StringAssert.Contains(maintenance, "ShouldDelayJusticeCustodyPedReturn(");
        StringAssert.Contains(maintenance, "ref wasNaturallyBusy[index]");
        StringAssert.Contains(maintenance, "wasNaturallyBusy[index] = false");
        StringAssert.Contains(reset, "_justiceCustodyGuardWasNaturallyBusy");
        StringAssert.Contains(reset, "_justiceCustodyInmateWasNaturallyBusy");
        StringAssert.Contains(maintenance, "JusticeCustodyHasReached(now, retryAt[index])");
        StringAssert.Contains(maintenance, "JusticeCustodySceneReturnRetryMs");
        StringAssert.Contains(maintenance, "TASK_FOLLOW_NAV_MESH_TO_COORD");
        Assert.IsFalse(
            maintenance.Contains("TeleportPlayerWithFadeSafe"),
            "La maintenance des PNJ ne doit jamais téléporter une entité visible.");
        StringAssert.Contains(busy, "ped.IsInCombat");
        StringAssert.Contains(busy, "Hash.IS_PED_IN_MELEE_COMBAT");
        StringAssert.Contains(busy, "ped.IsBeingStunned");
        StringAssert.Contains(busy, "JusticeNativeIsPedFleeing");
        StringAssert.Contains(busy, "JusticeNativeIsPedRagdoll");
        StringAssert.Contains(spawn, "ped.BlockPermanentEvents = false");
        StringAssert.Contains(
            spawn,
            "JusticeNativeSetBlockingOfNonTemporaryEvents, ped.Handle, false");
        StringAssert.Contains(spawn, "JusticeNativeSetPedKeepTask, ped.Handle, false");
    }

    private static string ReadCustodySource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        string marker = methodName + "(";
        int nameIndex = -1;
        int searchAt = 0;
        while (searchAt < source.Length)
        {
            int candidate = source.IndexOf(
                marker,
                searchAt,
                StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string declarationPrefix = source.Substring(
                lineStart,
                candidate - lineStart);
            if (declarationPrefix.Contains("private ") ||
                declarationPrefix.Contains("internal "))
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
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openingBrace, index - openingBrace + 1);
                }
            }
        }

        Assert.Fail("Corps source non fermé : " + methodName);
        return string.Empty;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null &&
               !File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
        {
            current = current.Parent;
        }

        Assert.IsNotNull(current, "Racine du dépôt introuvable.");
        return current.FullName;
    }
}
