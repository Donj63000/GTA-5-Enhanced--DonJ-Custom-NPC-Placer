using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeUiIntegrationObservabilityTests
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
    public void JusticeHud_UsesOnlyTheDiscreetCustodyLineOutsideF10()
    {
        List<MethodBase> tickCalls = ReadCalledMethods(FindMethod("OnTick", PrivateInstance));

        Assert.IsTrue(tickCalls.Any(call => call.Name == "DrawJusticeCustodyStatusLine"));
        Assert.IsFalse(tickCalls.Any(call => call.Name == "DrawJusticeCompactHud"));

        List<MethodBase> lineCalls = ReadCalledMethods(
            FindMethod("DrawJusticeCustodyStatusLine", PrivateInstance));
        Assert.AreEqual(2, lineCalls.Count(call => call.Name == "JusticeHudRectangle"));
        Assert.AreEqual(1, lineCalls.Count(call => call.Name == "JusticeHudText"));
        Assert.IsFalse(lineCalls.Any(call => call.Name == "JusticeShouldShowCompactHud"));
        Assert.IsTrue(lineCalls.Any(call =>
            call.Name == "IsJusticePlayedProfileCustodyContextReady"));

        Assert.IsTrue(ReadCalledMethods(FindMethod("OnJusticeChargeConfirmed", PrivateInstance))
            .Any(call => call.Name == "ShowStatus"));
        Assert.IsTrue(ReadCalledMethods(FindMethod("JusticeRegisterEscape", PrivateInstance))
            .Any(call => call.Name == "ShowStatus"));
    }

    [TestMethod]
    public void JusticeHud_CustodyContextBelongsOnlyToTheHeroActuallyPlayed()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justiceActivePlayerProfileSlot", 1);
        SetField(script, "_justiceCustodyPlayerSlot", 1);
        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceProfileSelectionPending", false);
        SetField(script, "_justiceProfileSwitchPersistencePending", false);
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 2));

        Assert.IsFalse((bool)InvokeInstance(
            script,
            "IsJusticePlayedProfileCustodyContextReady"));

        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
        Assert.IsTrue((bool)InvokeInstance(
            script,
            "IsJusticePlayedProfileCustodyContextReady"));

        SetField(script, "_justiceRuntimeSuspendedCached", true);
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "IsJusticePlayedProfileCustodyContextReady"));

        SetField(script, "_justiceRuntimeSuspendedCached", false);
        SetField(script, "_justiceProfileContextBlocked", true);
        Assert.IsFalse((bool)InvokeInstance(
            script,
            "IsJusticePlayedProfileCustodyContextReady"));
    }

    [TestMethod]
    public void SafeZoneCache_IsCadencedAndCircuitBreakerAvoidsPerFrameRetries()
    {
        Assert.IsTrue(GetStaticField<int>("MenuSafeZoneRefreshMs") >= 250);
        Assert.IsTrue(GetStaticField<int>("MenuSafeZoneCircuitRetryMs") >
                      GetStaticField<int>("MenuSafeZoneRefreshMs"));
        Assert.IsFalse((bool)InvokeStatic("IsMenuDeadlineReached", 1249, 1250));
        Assert.IsTrue((bool)InvokeStatic("IsMenuDeadlineReached", 1250, 1250));
        Assert.IsTrue((bool)InvokeStatic(
            "IsMenuDeadlineReached",
            unchecked(int.MinValue + 2),
            unchecked(int.MaxValue - 2)));

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        float first = (float)InvokeInstance(script, "GetMenuSafeZoneSafe");
        Assert.IsTrue(first >= 0.80f && first <= 1.0f);

        bool circuitOpen = GetField<bool>(script, "_menuSafeZoneCircuitOpen");
        int deadline = circuitOpen
            ? GetField<int>(script, "_menuSafeZoneCircuitRetryAt")
            : GetField<int>(script, "_nextMenuSafeZoneReadAt");
        Assert.AreNotEqual(0, deadline);

        float second = (float)InvokeInstance(script, "GetMenuSafeZoneSafe");
        Assert.AreEqual(first, second, 0.0001f);
        Assert.AreEqual(
            deadline,
            circuitOpen
                ? GetField<int>(script, "_menuSafeZoneCircuitRetryAt")
                : GetField<int>(script, "_nextMenuSafeZoneReadAt"));
    }

    [TestMethod]
    public void JusticeRecordLedger_FlattensNewestFirstAndInvalidatesOnlyOnRevisionChange()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeRecordState record = new JusticeRecordState();
        JusticeConviction older = CreateConviction("conviction:older", "Dégradation");
        JusticeConviction newest = CreateConviction("conviction:newest", "Agression");
        newest.Charges.Add(new JusticeConvictionChargeSummary { DisplayName = "Refus" });
        record.Convictions.Add(older);
        record.Convictions.Add(newest);
        SetField(script, "_justiceRecordState", record);

        Assert.AreEqual(3, InvokeInstance(script, "GetJusticeLedgerItemCount", true));
        int firstRevision = GetField<int>(script, "_justiceRecordLedgerRevision");
        Assert.IsTrue(firstRevision > 0);

        object[] firstArgs = { 0, null, null };
        Assert.IsTrue((bool)FindMethod(
            "TryGetJusticeRecordOffenseAt",
            PrivateInstance,
            3).Invoke(script, firstArgs));
        Assert.AreSame(newest, firstArgs[1]);
        Assert.AreEqual("Agression", ((JusticeConvictionChargeSummary)firstArgs[2]).DisplayName);

        Assert.AreEqual(3, InvokeInstance(script, "GetJusticeLedgerItemCount", true));
        Assert.AreEqual(firstRevision, GetField<int>(script, "_justiceRecordLedgerRevision"));

        newest.Charges.Add(new JusticeConvictionChargeSummary { DisplayName = "Évasion" });
        record.MarkLedgerChanged();
        Assert.AreEqual(4, InvokeInstance(script, "GetJusticeLedgerItemCount", true));
        Assert.AreEqual(firstRevision + 1, GetField<int>(script, "_justiceRecordLedgerRevision"));
    }

    [TestMethod]
    public void JusticeProfileMenu_WiresSelectionPaymentResetAndKeepsActivationReachable()
    {
        List<MethodBase> valueChanges = ReadCalledMethods(
            FindMethod("ChangeMainMenuValue", PrivateInstance, 2));
        Assert.IsTrue(valueChanges.Any(call =>
            call.Name == "ChangeJusticeMenuSelectedProfile"));

        List<MethodBase> activations = ReadCalledMethods(
            FindMethod("ActivateMainMenuItem", PrivateInstance, 1));
        Assert.IsTrue(activations.Any(call => call.Name == "RequestJusticeToggle"));
        Assert.IsTrue(activations.Any(call =>
            call.Name == "RequestJusticeSelectedProfileFinePaymentConfirmation"));
        Assert.IsTrue(activations.Any(call =>
            call.Name == "RequestJusticeSelectedProfileReset"));

        List<MethodBase> refresh = ReadCalledMethods(
            FindMethod("RefreshObsidianMenuEntryValues", PrivateInstance, 1));
        Assert.IsTrue(refresh.Any(call =>
            call.Name == "GetJusticeMenuSelectedProfileDisplay"));
        Assert.IsTrue(refresh.Any(call =>
            call.Name == "GetJusticePlayedActivationDisplay"));
        Assert.IsTrue(refresh.Any(call =>
            call.Name == "GetJusticeSelectedProfileContextDisplay"));
        Assert.IsTrue(refresh.Any(call =>
            call.Name == "GetJusticeSelectedFinePaymentDisplay"));

        List<MethodBase> confirmations = ReadCalledMethods(
            FindMethod("ConfirmPendingDangerAction", PrivateInstance, 0));
        Assert.IsTrue(confirmations.Any(call =>
            call.Name == "ExecuteJusticeConfirmedProfileReset"));
        Assert.IsTrue(confirmations.Any(call =>
            call.Name == "RequestJusticeConfirmedVoluntaryFinePayment"));
    }

    [TestMethod]
    public void JusticeProfileMenu_DistinguishesPlayedHeroFromConsultedFile()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justiceActivePlayerProfileSlot", 1);
        SetField(script, "_justiceMenuSelectedProfileSlot", 2);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceCaseState", new JusticeCaseState { Enabled = true, FineDue = 1250L });
        SetField(script, "_justiceRecordState", new JusticeRecordState());
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));

        Assert.AreEqual("ACTIVÉE · FRANKLIN", InvokeInstance(script, "GetJusticePlayedActivationDisplay"));
        Assert.AreEqual("Trevor · CONSULTATION", InvokeInstance(script, "GetJusticeSelectedProfileContextDisplay"));
        Assert.AreEqual("0$ · consultation", InvokeInstance(script, "GetJusticeSelectedFinePaymentDisplay"));
        Assert.IsFalse((bool)InvokeInstance(script, "IsJusticeMenuSelectedProfileCurrentlyPlayed"));

        SetField(script, "_justiceMenuSelectedProfileSlot", 1);
        Assert.AreEqual("Franklin · JOUÉ", InvokeInstance(script, "GetJusticeSelectedProfileContextDisplay"));
        Assert.AreEqual("1 250$ · payer", InvokeInstance(script, "GetJusticeSelectedFinePaymentDisplay"));
        Assert.IsTrue((bool)InvokeInstance(script, "IsJusticeMenuSelectedProfileCurrentlyPlayed"));

        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => -1));
        Assert.AreEqual(
            "1 250$ · indisponible",
            InvokeInstance(script, "GetJusticeSelectedFinePaymentDisplay"));
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));

        SetField(script, "_justiceProfileContextBlocked", true);
        Assert.AreEqual(
            "IDENTIFICATION / CHANGEMENT EN COURS",
            InvokeInstance(script, "GetJusticePlayedActivationDisplay"));
        Assert.IsFalse((bool)InvokeInstance(script, "IsJusticeMenuSelectedProfileCurrentlyPlayed"));

        SetField(script, "_justiceProfileContextBlocked", false);
        SetField(script, "_justiceActivePlayerProfileSlot", -1);
        Assert.AreEqual(
            "IDENTIFICATION / CHANGEMENT EN COURS",
            InvokeInstance(script, "GetJusticePlayedActivationDisplay"));
    }

    [TestMethod]
    public void JusticeLedger_CountsEveryRepresentedFactAndExposesConsolidatedRows()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState();
        state.Charges.Add(new JusticeCharge
        {
            DisplayName = "Vol de véhicule",
            Kind = JusticeCrimeKind.VehicleTheft
        });
        state.Charges.Add(new JusticeCharge
        {
            DisplayName = "Infractions consolidées",
            Kind = JusticeCrimeKind.ReportedViolentAct,
            IsAggregate = true,
            AggregatedChargeCount = 513
        });
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLedgerProfileSlot", 0);
        SetField(script, "_justiceCaseState", state);

        Assert.AreEqual(2, InvokeInstance(script, "GetJusticeLedgerItemCount", false));
        Assert.AreEqual(514, InvokeInstance(script, "GetJusticeLedgerRepresentedOffenseCount", false));
        Assert.AreEqual(
            "Infractions consolidées",
            ((JusticeCharge)InvokeInstance(script, "GetJusticeActiveChargeAt", 1)).DisplayName);

        JusticeRecordState record = new JusticeRecordState();
        JusticeConviction conviction = CreateConviction("conviction:aggregate", "Agression");
        conviction.Charges.Add(new JusticeConvictionChargeSummary
        {
            DisplayName = "Infractions consolidées",
            IsAggregate = true,
            AggregatedChargeCount = 8
        });
        record.Convictions.Add(conviction);
        record.MarkLedgerChanged();
        SetField(script, "_justiceRecordState", record);

        Assert.AreEqual(2, InvokeInstance(script, "GetJusticeLedgerItemCount", true));
        Assert.AreEqual(9, InvokeInstance(script, "GetJusticeLedgerRepresentedOffenseCount", true));
    }

    [TestMethod]
    public void AllyAttribution_IsRecordedAfterOffensiveOrdersAndTransferValidationIsBounded()
    {
        AssertCallOrder("ActivateAllyCombat", "TryActivateCombatAgainstTarget", "RecordJusticeAllyPoliceEngagement");

        foreach (string dispatcher in new[] { "EngageCartelGuardThreat", "EngageHighSecurityEscortGuardThreat" })
        {
            Assert.IsFalse(ReadCalledMethods(FindMethod(dispatcher, PrivateInstance))
                .Any(call => call.Name == "RecordJusticeAllyPoliceEngagement"));
        }

        foreach (string offensiveHelper in new[]
        {
            "StartCartelPassengerDriveBy",
            "StartCartelOnFootCombat",
            "CommandCartelVehicleForCombat",
            "StartHighSecurityEscortPassengerDriveBy",
            "StartHighSecurityEscortOnFootCombat",
            "CommandHighSecurityEscortVehicleForCombat"
        })
        {
            List<MethodBase> calls = ReadCalledMethods(FindMethod(offensiveHelper, PrivateInstance));
            int recordIndex = calls.FindLastIndex(call => call.Name == "RecordJusticeAllyPoliceEngagement");
            int nativeIndex = calls.FindLastIndex(call => call.Name == "Call");
            Assert.IsTrue(recordIndex > nativeIndex, offensiveHelper + " doit enregistrer la causalité après la native offensive.");
        }

        Assert.IsTrue((bool)InvokeStatic("IsJusticeTransferTargetContextValid", 100L, 100L, 120.0f, 120.0f, true));
        Assert.IsFalse((bool)InvokeStatic("IsJusticeTransferTargetContextValid", 101L, 100L, 1.0f, 1.0f, true));
        Assert.IsFalse((bool)InvokeStatic("IsJusticeTransferTargetContextValid", 100L, 101L, 120.1f, 1.0f, true));
        Assert.IsFalse((bool)InvokeStatic("IsJusticeTransferTargetContextValid", 100L, 101L, 1.0f, 1.0f, false));

        List<MethodBase> transferCalls = ReadCalledMethods(
            FindMethod("TryReleaseJusticeAllyPoliceTargetForTransfer", PrivateInstance));
        int validationIndex = transferCalls.FindIndex(call => call.Name == "IsJusticeTransferTargetContextValid");
        int holdIndex = transferCalls.FindIndex(call => call.Name == "TryHoldJusticeAllyServiceDuringCustody");
        int resumeIndex = transferCalls.FindIndex(call => call.Name == "PrepareJusticeAllyServiceResume");
        Assert.IsTrue(validationIndex >= 0 && holdIndex > validationIndex && resumeIndex > holdIndex);
        Assert.IsTrue(ReadCalledMethods(FindMethod("TryHoldJusticeAllyServiceDuringCustody", PrivateInstance))
            .Any(call => call.Name == "Call"));
        Assert.IsTrue(ReadCalledMethods(FindMethod("PrepareJusticeAllyServiceResume", PrivateInstance))
            .Any(call => call.Name == "PrepareHighSecurityEscortGuardServiceResumeAfterJustice"));
    }

    [TestMethod]
    public void RuntimeLogger_PrefersStableLocationsBeforeShadowCopyCandidates()
    {
        List<string> candidates = (List<string>)InvokeStatic("BuildRuntimeLogDirectoryCandidates");
        string localLogs = (string)InvokeStatic("GetLocalAppDataRuntimeLogDirectorySafe");
        string assembly = (string)InvokeStatic("GetAssemblyDirectorySafe");
        string appDomain = AppDomain.CurrentDomain.BaseDirectory;

        int localIndex = IndexOfPath(candidates, localLogs);
        int assemblyIndex = IndexOfPath(candidates, assembly);
        int appDomainIndex = IndexOfPath(candidates, appDomain);
        Assert.IsTrue(localIndex >= 0);
        if (assemblyIndex >= 0)
        {
            Assert.IsTrue(localIndex < assemblyIndex);
        }
        if (appDomainIndex >= 0)
        {
            Assert.IsTrue(localIndex < appDomainIndex);
        }
    }

    [TestMethod]
    public void BugCollector_CopiesOnlyTheNewestInjectedLegacyRuntimeLog()
    {
        string collectorSource = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "tools",
            "collect-bug-logs.ps1"));
        StringAssert.Contains(collectorSource, "Join-Path $localAppData \"assembly\"");

        string tempRoot = Path.Combine(Path.GetTempPath(), "DonJLegacyLogs_" + Guid.NewGuid().ToString("N"));
        string fakeGtaRoot = Path.Combine(tempRoot, "Grand Theft Auto V Enhanced");
        string legacyRoot = Path.Combine(tempRoot, "shadow-copies");
        string oldLog = Path.Combine(legacyRoot, "old", "DonJCustomNpcPlacer.log");
        string newLog = Path.Combine(legacyRoot, "new", "DonJCustomNpcPlacer.log");
        string title = "legacy-unit-" + Guid.NewGuid().ToString("N");
        string reportRoot = string.Empty;

        try
        {
            Directory.CreateDirectory(fakeGtaRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(oldLog));
            Directory.CreateDirectory(Path.GetDirectoryName(newLog));
            File.WriteAllText(Path.Combine(fakeGtaRoot, "GTA5_Enhanced.exe"), string.Empty);
            File.WriteAllText(oldLog, "ancien-log");
            File.WriteAllText(newLog, "nouveau-log");
            File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newLog, DateTime.UtcNow.AddMinutes(-1));

            RunCollector(title, fakeGtaRoot, legacyRoot);
            reportRoot = FindNewestReport(title);
            string[] legacyCopies = Directory.GetFiles(
                Path.Combine(reportRoot, "raw-logs"),
                "DonJ-Runtime-Legacy*",
                SearchOption.TopDirectoryOnly);
            Assert.AreEqual(1, legacyCopies.Length);
            Assert.AreEqual("nouveau-log", File.ReadAllText(legacyCopies[0]).Trim());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
            if (!string.IsNullOrWhiteSpace(reportRoot) && Directory.Exists(reportRoot))
            {
                Directory.Delete(reportRoot, true);
            }
        }
    }

    private static JusticeConviction CreateConviction(string id, string offense)
    {
        JusticeConviction conviction = new JusticeConviction { ConvictionId = id };
        conviction.Charges.Add(new JusticeConvictionChargeSummary { DisplayName = offense });
        return conviction;
    }

    private static void AssertCallOrder(string methodName, string firstName, string secondName)
    {
        List<MethodBase> calls = ReadCalledMethods(FindMethod(methodName, PrivateInstance));
        int first = calls.FindIndex(call => call.Name == firstName);
        int second = calls.FindIndex(call => call.Name == secondName);
        Assert.IsTrue(first >= 0 && second > first, methodName + " appelle les intégrations dans le mauvais ordre.");
    }

    private static int IndexOfPath(IList<string> paths, string expected)
    {
        if (paths == null || string.IsNullOrWhiteSpace(expected))
        {
            return -1;
        }
        for (int index = 0; index < paths.Count; index++)
        {
            if (string.Equals(paths[index], expected, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }

    private static void RunCollector(string title, string gtaRoot, string legacyRoot)
    {
        string script = Path.Combine(GetRepositoryRoot(), "tools", "collect-bug-logs.ps1");
        string arguments =
            "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(script) +
            " -Title " + QuoteArgument(title) +
            " -SinceHours 1 -GtaRoot " + QuoteArgument(gtaRoot) +
            " -LegacyLogRoot " + QuoteArgument(legacyRoot);
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.IsNotNull(process);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(120000), "Le collecteur n'a pas terminé.");
            Assert.AreEqual(0, process.ExitCode, output + Environment.NewLine + error);
        }
    }

    private static string FindNewestReport(string title)
    {
        DirectoryInfo report = new DirectoryInfo(Path.Combine(GetRepositoryRoot(), "bug-reports"))
            .GetDirectories("*-" + title, SearchOption.TopDirectoryOnly)
            .OrderByDescending(directory => directory.Name)
            .FirstOrDefault();
        Assert.IsNotNull(report);
        return report.FullName;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        return FindMethod(methodName, PrivateInstance, args == null ? 0 : args.Length).Invoke(target, args);
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        return FindMethod(methodName, PrivateStatic, args == null ? 0 : args.Length).Invoke(null, args);
    }

    private static MethodInfo FindMethod(string methodName, BindingFlags flags, int parameterCount = -1)
    {
        MethodInfo method = ScriptType.GetMethods(flags)
            .FirstOrDefault(candidate => candidate.Name == methodName &&
                (parameterCount < 0 || candidate.GetParameters().Length == parameterCount));
        Assert.IsNotNull(method, "Méthode privée introuvable : " + methodName);
        return method;
    }

    private static T GetStaticField<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, "Champ statique introuvable : " + fieldName);
        return (T)field.GetValue(null);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static List<MethodBase> ReadCalledMethods(MethodInfo method)
    {
        List<MethodBase> result = new List<MethodBase>();
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
        Assert.Fail("Racine du dépôt introuvable.");
        return string.Empty;
    }
}
