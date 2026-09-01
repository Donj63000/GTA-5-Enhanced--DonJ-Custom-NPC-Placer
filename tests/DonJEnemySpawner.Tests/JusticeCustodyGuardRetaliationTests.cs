using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeCustodyGuardRetaliationTests
{
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public void GuardRetaliation_ScansOnlyFourOwnedGuardsAndIgnoresInmates()
    {
        string source = ReadCustodySource();
        string update = ReadMethod(
            source,
            "UpdateJusticeCustodyGuardRetaliation");

        StringAssert.Contains(
            update,
            "JusticeCustodyMaximumGuardCount");
        StringAssert.Contains(
            update,
            "_justiceCustodyGuards.Count");
        StringAssert.Contains(
            update,
            "IsJusticeCustodyPedOwnershipValid(guard)");
        StringAssert.Contains(
            update,
            "TryCaptureJusticeDamageFront(guard, player)");
        AssertOrdered(
            update,
            "if (!IsJusticeCustodyPedOwnershipValid(guard))",
            "TryCaptureJusticeDamageFront(guard, player)",
            "guard.IsDead");
        Assert.IsFalse(
            update.Contains("_justiceCustodyInmates"),
            "Une bagarre avec un détenu ne doit jamais armer la riposte.");

        Assert.AreEqual(
            4,
            ReadPrivateConstant<int>("JusticeCustodyMaximumGuardCount"));
        int cadence = ReadPrivateConstant<int>(
            "JusticeCustodyGuardRetaliationScanMs");
        Assert.IsTrue(cadence >= 150 && cadence <= 200);
    }

    [TestMethod]
    public void GuardRetaliation_UsesWantedFloorAndCadencedCombatTasks()
    {
        string source = ReadCustodySource();
        string begin = ReadMethod(
            source,
            "BeginJusticeCustodyGuardRetaliation");
        string command = ReadMethod(
            source,
            "CommandJusticeCustodyGuardCombatIfDue");
        string suppression = ReadMethod(
            source,
            "MaintainJusticeCustodyPoliceSuppression");

        StringAssert.Contains(
            begin,
            "SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum)");
        StringAssert.Contains(begin, "SetJusticeCustodyPoliceSuppression(true)");
        StringAssert.Contains(command, "guard.IsInCombatAgainst(player)");
        StringAssert.Contains(command, "JusticeCustodyGuardCombatRetryMs");
        StringAssert.Contains(command, "Hash.TASK_COMBAT_PED");
        StringAssert.Contains(
            suppression,
            "SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum)");
        Assert.AreEqual(
            2,
            ReadPrivateConstant<int>("JusticeCustodyGuardWantedMinimum"));
        Assert.IsTrue(
            ReadPrivateConstant<int>("JusticeCustodyGuardCombatRetryMs") >
            ReadPrivateConstant<int>("JusticeCustodyGuardRetaliationScanMs"));
    }

    [TestMethod]
    public void GuardDeathAttribution_RequiresExactOwnedGenerationOrStrictlyFreshDamage()
    {
        string source = ReadCustodySource();
        string attribution = ReadMethod(
            source,
            "IsJusticeCustodyDeathCausedByOwnedGuard");
        string exactGuard = ReadMethod(
            source,
            "IsJusticeExactOwnedCustodyGuard");
        string deathSampling = ReadMethod(
            source,
            "CaptureJusticeCustodyGuardDamageFrontsAtDeath");
        string update = ReadMethod(
            source,
            "UpdateJusticeCustodyGuardRetaliation");

        AssertOrdered(
            attribution,
            "Entity killer = player.GetKiller()",
            "if (Entity.Exists(killer))",
            "return IsJusticeExactOwnedCustodyGuard(",
            "long guardDamageAge");
        StringAssert.Contains(
            attribution,
            "guardDamageAge < JusticePolicy.PendingIncidentLifetimeMs");
        Assert.IsFalse(
            attribution.Contains("guardDamageAge <= JusticePolicy.PendingIncidentLifetimeMs"));
        StringAssert.Contains(exactGuard, "IsJusticeCustodyPedOwnershipValid(guard)");
        StringAssert.Contains(exactGuard, "guard.Handle == handle");
        StringAssert.Contains(
            exactGuard,
            "GetJusticeEntityGeneration(guard) == generation");
        AssertOrdered(
            update,
            "CaptureJusticeCustodyGuardDamageFrontsAtDeath(player)",
            "FreezeJusticeCustodyGuardDeathPenalty(player)");
        StringAssert.Contains(
            deathSampling,
            "JusticeCustodyMaximumGuardCount");
        StringAssert.Contains(
            deathSampling,
            "IsJusticeCustodyPedOwnershipValid(guard)");
        AssertOrdered(
            deathSampling,
            "TryCaptureJusticeDamageFront(guard, player)",
            "BeginJusticeCustodyGuardRetaliation(",
            "int generation = GetJusticeEntityGeneration(guard)",
            "TryCaptureJusticeDamageFront(player, guard)",
            "_justiceCustodyLastDamagingGuardGeneration = generation",
            "FlushJusticeConsumedDamageFronts()");
        Assert.IsFalse(deathSampling.Contains("_justiceCustodyInmates"));
    }

    [TestMethod]
    public void CustodySentence_ConsumesGuardExtensionBeforeBaseAndAllowsSixHundredPlusSixty()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            SentenceSeconds = 600,
            CustodyGuardPenaltySeconds = 60L
        };

        Assert.AreEqual(
            660L,
            DonJEnemySpawner.GetJusticeCustodyTotalRemainingSeconds(state));

        DonJEnemySpawner.ConsumeJusticeCustodySentenceSeconds(state, 30);
        Assert.AreEqual(30L, state.CustodyGuardPenaltySeconds);
        Assert.AreEqual(600, state.SentenceSeconds);

        DonJEnemySpawner.ConsumeJusticeCustodySentenceSeconds(state, 45);
        Assert.AreEqual(0L, state.CustodyGuardPenaltySeconds);
        Assert.AreEqual(585, state.SentenceSeconds);
        Assert.AreEqual(
            585L,
            DonJEnemySpawner.GetJusticeCustodyTotalRemainingSeconds(state));
    }

    [TestMethod]
    public void CustodySentence_BackgroundProfileConsumesTheExtensionFirstAndTotalSaturates()
    {
        JusticePlayerProfileState profile = new JusticePlayerProfileState(1);
        profile.CaseState.Enabled = true;
        profile.CaseState.Phase = JusticePhase.Incarcerated;
        profile.CaseState.SentenceSeconds = 600;
        profile.CaseState.CustodyGuardPenaltySeconds = 60L;
        profile.CanAdvanceCustodyInBackground = true;
        profile.InactiveCustodyLastTickAt = 1000;

        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        MethodInfo advance = typeof(DonJEnemySpawner).GetMethod(
            "AdvanceJusticeInactiveCustodyProfileClock",
            PrivateInstance);
        Assert.IsNotNull(advance);
        Assert.AreEqual(true, advance.Invoke(script, new object[] { profile, 3000, false }));
        Assert.AreEqual(58L, profile.CaseState.CustodyGuardPenaltySeconds);
        Assert.AreEqual(600, profile.CaseState.SentenceSeconds);

        profile.CaseState.CustodyGuardPenaltySeconds = long.MaxValue;
        Assert.AreEqual(
            long.MaxValue,
            DonJEnemySpawner.GetJusticeCustodyTotalRemainingSeconds(
                profile.CaseState));
    }

    [TestMethod]
    public void CustodySentence_HudDisplaysTheBasePlusGuardExtension()
    {
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        JusticeCaseState state = new JusticeCaseState
        {
            SentenceSeconds = 600,
            CustodyGuardPenaltySeconds = 60L
        };
        FieldInfo caseField = typeof(DonJEnemySpawner).GetField(
            "_justiceCaseState",
            PrivateInstance);
        Assert.IsNotNull(caseField);
        caseField.SetValue(script, state);

        MethodInfo display = typeof(DonJEnemySpawner).GetMethod(
            "GetJusticeSentenceDisplay",
            PrivateInstance);
        Assert.IsNotNull(display);
        Assert.AreEqual("11:00", display.Invoke(script, null));
    }

    [TestMethod]
    public void Retaliation_StopsOnDeathAndEveryCustodyExit()
    {
        string source = ReadCustodySource();
        string death = ReadMethod(source, "ObserveJusticeCustodyDeath");
        string suspendedDeath = ReadMethod(
            source,
            "ObserveJusticeCustodyDeathDuringSuspension");
        string cleanup = ReadMethod(
            source,
            "CleanupJusticeCustodyEntitiesAndGroups");
        string reset = ReadMethod(
            source,
            "ResetJusticeCustodyGuardRetaliation");

        StringAssert.Contains(death, "FreezeJusticeCustodyGuardDeathPenalty(player)");
        StringAssert.Contains(
            death,
            "ResetJusticeCustodyGuardRetaliation(player, true, true)");
        StringAssert.Contains(
            suspendedDeath,
            "ResetJusticeCustodyGuardRetaliation(player, true, true)");
        StringAssert.Contains(
            cleanup,
            "ResetJusticeCustodyGuardRetaliation(player, true, false)");
        StringAssert.Contains(reset, "_justiceCustodyGuardRetaliationActive = false");
        StringAssert.Contains(reset, "Hash.CLEAR_PED_TASKS");
    }

    private static T ReadPrivateConstant<T>(string name)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            PrivateStatic);
        Assert.IsNotNull(field, "Constante privée absente : " + name);
        return (T)field.GetRawConstantValue();
    }

    private static string ReadCustodySource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
    }

    private static string ReadMethod(string source, string methodName)
    {
        Match signatureMatch = Regex.Match(
            source,
            @"(?m)^\s*(?:private|internal)\s+(?:static\s+)?[^\r\n(]+\s+" +
            Regex.Escape(methodName) + @"\s*\(");
        Assert.IsTrue(
            signatureMatch.Success,
            "Méthode absente : " + methodName);
        int signature = signatureMatch.Index;
        int opening = source.IndexOf('{', signature);
        Assert.IsTrue(opening >= 0, "Corps absent : " + methodName);

        int depth = 0;
        for (int index = opening; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(opening, index - opening + 1);
            }
        }

        Assert.Fail("Corps non équilibré : " + methodName);
        return string.Empty;
    }

    private static void AssertOrdered(string source, params string[] fragments)
    {
        int cursor = -1;
        for (int index = 0; index < fragments.Length; index++)
        {
            int found = source.IndexOf(
                fragments[index],
                cursor + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                found > cursor,
                "Fragment absent ou hors ordre : " + fragments[index]);
            cursor = found;
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(
            AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Racine GTA5modDEV introuvable.");
    }
}
