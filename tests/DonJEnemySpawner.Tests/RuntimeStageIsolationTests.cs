using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeStageIsolationTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void OnTick_PreservesDomainOrderAndUsesJusticeFailSafeAfterEarlyFailure()
    {
        string onTick = ExtractMethodBody(ReadSource("DonJEnemySpawner.cs"), "OnTick");

        AssertOrdered(
            onTick,
            "RunTickStage(RuntimeTickStage.Relationships)",
            "justiceEarlySucceeded = RunTickStage(RuntimeTickStage.JusticeEarly)",
            "RunTickStage(RuntimeTickStage.CartelEarly)",
            "RunTickStage(RuntimeTickStage.Terminator)",
            "_autoRespawnsThisTick = 0",
            "RunTickStage(RuntimeTickStage.CustomModelRequest)",
            "RunTickStage(RuntimeTickStage.SaveRequest)",
            "RunTickStage(RuntimeTickStage.LoadRequest)",
            "RunTickStage(RuntimeTickStage.Placement)",
            "RunTickStage(RuntimeTickStage.MenuAnimation)",
            "RunTickStage(RuntimeTickStage.Menu)",
            "RunTickStage(RuntimeTickStage.PendingSpawn)",
            "RunTickStage(RuntimeTickStage.PlayerHostility)",
            "if (justiceEarlySucceeded)",
            "RunTickStage(RuntimeTickStage.JusticeLate)",
            "RunTickStage(RuntimeTickStage.JusticeRecovery)",
            "RunTickStage(RuntimeTickStage.Npcs)",
            "RunTickStage(RuntimeTickStage.CartelLate)",
            "RunTickStage(RuntimeTickStage.Vehicles)",
            "RunTickStage(RuntimeTickStage.Objects)",
            "RunTickStage(RuntimeTickStage.ObjectInteractions)",
            "RunTickStage(RuntimeTickStage.Portals)",
            "RunTickStage(RuntimeTickStage.Status)",
            "finally",
            "RunTickStage(RuntimeTickStage.JusticeDamageFlush)");

        StringAssert.Contains(onTick, "UpdateJusticeFailSafeMaintenance();");
        Assert.IsFalse(
            onTick.IndexOf("Action", StringComparison.Ordinal) >= 0,
            "Le chemin appelé à chaque frame ne doit pas créer de delegate Action.");
        Assert.IsFalse(
            onTick.IndexOf("=>", StringComparison.Ordinal) >= 0,
            "Le dispatcher du tick doit rester un switch direct sans lambda capturante.");

        AssertMethodHasNoDelegateLocals("OnTick");
    }

    [TestMethod]
    public void TickErrorCooldown_IsIndependentPerStageAndWrapSafe()
    {
        Type stageType = ScriptType.GetNestedType("RuntimeTickStage", BindingFlags.NonPublic);
        Assert.IsNotNull(stageType);

        int stageCount = Convert.ToInt32(Enum.Parse(stageType, "Count"));
        object script = FormatterServices.GetUninitializedObject(ScriptType);

        SetField(script, "_runtimeTickStageNextErrorLogAt", new int[stageCount]);
        SetField(script, "_runtimeTickStageHasLoggedError", new bool[stageCount]);

        object relationships = Enum.Parse(stageType, "Relationships");
        object cartel = Enum.Parse(stageType, "CartelEarly");

        Assert.IsTrue(InvokeShouldLog(script, relationships, 100));
        Assert.IsFalse(InvokeShouldLog(script, relationships, 101));
        Assert.IsTrue(
            InvokeShouldLog(script, cartel, 101),
            "Une erreur Relations ne doit pas masquer la première erreur Cartel.");
        Assert.IsTrue(InvokeShouldLog(script, relationships, 10100));

        MethodInfo elapsed = ScriptType.GetMethod(
            "HasRuntimeStageCooldownElapsed",
            PrivateStatic);
        Assert.IsNotNull(elapsed);

        int beforeWrap = int.MaxValue - 4;
        int wrappedDeadline = unchecked(beforeWrap + 10);
        int afterWrap = unchecked(beforeWrap + 11);

        Assert.IsFalse((bool)elapsed.Invoke(null, new object[] { beforeWrap, wrappedDeadline }));
        Assert.IsTrue((bool)elapsed.Invoke(null, new object[] { afterWrap, wrappedDeadline }));
    }

    [TestMethod]
    public void OnAborted_RestoresJusticeFirstAndKeepsEveryCleanupIsolated()
    {
        string onAborted = ExtractMethodBody(ReadSource("DonJEnemySpawner.cs"), "OnAborted");
        string safetySource = ReadSource("DonJEnemySpawner.RuntimeSafety.cs");

        AssertOrdered(
            onAborted,
            "RunShutdownStep(RuntimeShutdownStage.Justice)",
            "RunShutdownStep(RuntimeShutdownStage.Terminator)",
            "RunShutdownStep(RuntimeShutdownStage.Placement)",
            "RunShutdownStep(RuntimeShutdownStage.Menu)",
            "RunShutdownStep(RuntimeShutdownStage.DangerAction)",
            "RunShutdownStep(RuntimeShutdownStage.HighSecurityEscort)",
            "RunShutdownStep(RuntimeShutdownStage.NpcBlips)",
            "RunShutdownStep(RuntimeShutdownStage.VehicleBlips)",
            "RunShutdownStep(RuntimeShutdownStage.Relationships)");

        StringAssert.Contains(onAborted, "catch (Exception ex)");
        StringAssert.Contains(onAborted, "ReportRuntimeShutdownFailure(stage, ex);");
        Assert.IsFalse(
            onAborted.IndexOf("Action action", StringComparison.Ordinal) >= 0 ||
            onAborted.IndexOf("() =>", StringComparison.Ordinal) >= 0,
            "La chaîne d'arrêt ne doit pas créer une série de delegates pour ses étapes.");

        StringAssert.Contains(safetySource, "for (int i = 0; i < _spawnedNpcs.Count; i++)");
        StringAssert.Contains(safetySource, "for (int i = 0; i < _placedVehicles.Count; i++)");
        StringAssert.Contains(safetySource, "catch (Exception ex)");
        AssertOrdered(
            safetySource,
            "ref _hostileGroupHash",
            "ref _neutralGroupHash",
            "ref _allyGroupHash");

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        InvokeInstance(script, "RemoveAllNpcBlipsForShutdown");
        InvokeInstance(script, "RemoveAllVehicleBlipsForShutdown");
        InvokeInstance(script, "RemoveRuntimeRelationshipGroupsForShutdown");

        MethodInfo dispatcher = FindGeneratedLocalFunction("<OnAborted>g__RunShutdownStep|");
        Type shutdownStageType = ScriptType.GetNestedType("RuntimeShutdownStage", BindingFlags.NonPublic);
        Assert.IsNotNull(shutdownStageType);

        // Je laisse volontairement les collections de l'escorte non initialisées ici;
        // je vérifie que le dispatcher absorbe l'échec puis accepte l'étape suivante.
        dispatcher.Invoke(
            script,
            new[] { Enum.Parse(shutdownStageType, "HighSecurityEscort") });
        dispatcher.Invoke(
            script,
            new[] { Enum.Parse(shutdownStageType, "Relationships") });

        AssertMethodHasNoDelegateLocals("OnAborted");
    }

    [TestMethod]
    public void ReplaceFileAtomically_FailedReplacementKeepsExistingPrimaryUntouched()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJRuntimeSafety_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            string missingTempPath = Path.Combine(directory, "missing.tmp");
            string targetPath = Path.Combine(directory, "state.xml");
            string backupPath = targetPath + ".bak";
            File.WriteAllText(targetPath, "primary-intact");

            TargetInvocationException failure = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeStatic("ReplaceFileAtomically", missingTempPath, targetPath));

            Assert.IsInstanceOfType(failure.InnerException, typeof(IOException));
            Assert.AreEqual("primary-intact", File.ReadAllText(targetPath));
            Assert.IsFalse(File.Exists(backupPath));

            string replacement = ExtractMethodBody(
                ReadSource("DonJEnemySpawner.cs"),
                "ReplaceFileAtomically");
            StringAssert.Contains(replacement, "File.Replace(tempPath, targetPath, backupPath, true);");
            Assert.IsFalse(replacement.IndexOf("File.Delete(targetPath)", StringComparison.Ordinal) >= 0);
            Assert.IsFalse(replacement.IndexOf("File.Copy(targetPath", StringComparison.Ordinal) >= 0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static bool InvokeShouldLog(object script, object stage, int now)
    {
        MethodInfo method = ScriptType.GetMethod(
            "ShouldLogRuntimeTickStageFailure",
            PrivateInstance);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(script, new[] { stage, (object)now });
    }

    private static object InvokeInstance(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Méthode privée introuvable: " + methodName);
        return method.Invoke(target, arguments);
    }

    private static object InvokeStatic(string methodName, params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateStatic);
        Assert.IsNotNull(method, "Méthode statique privée introuvable: " + methodName);
        return method.Invoke(null, arguments);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable: " + fieldName);
        field.SetValue(target, value);
    }

    private static MethodInfo FindGeneratedLocalFunction(string namePrefix)
    {
        foreach (MethodInfo method in ScriptType.GetMethods(PrivateInstance))
        {
            if (method.Name.StartsWith(namePrefix, StringComparison.Ordinal))
            {
                return method;
            }
        }

        Assert.Fail("Fonction locale compilée introuvable: " + namePrefix);
        return null;
    }

    private static void AssertMethodHasNoDelegateLocals(string methodName)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method);
        MethodBody body = method.GetMethodBody();
        Assert.IsNotNull(body);

        foreach (LocalVariableInfo local in body.LocalVariables)
        {
            Assert.IsFalse(
                typeof(Delegate).IsAssignableFrom(local.LocalType),
                methodName + " ne doit matérialiser aucun delegate local par appel.");
        }
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        int cursor = -1;

        foreach (string marker in markers)
        {
            int index = source.IndexOf(marker, cursor + 1, StringComparison.Ordinal);
            Assert.IsTrue(index > cursor, "Ordre invalide ou marqueur absent: " + marker);
            cursor = index;
        }
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        string[] declarationPrefixes =
        {
            "private void ",
            "private static void ",
            "private bool ",
            "private static bool ",
            "internal void ",
            "internal static void "
        };

        int methodIndex = -1;
        foreach (string prefix in declarationPrefixes)
        {
            methodIndex = source.IndexOf(prefix + methodName + "(", StringComparison.Ordinal);
            if (methodIndex >= 0)
            {
                break;
            }
        }

        Assert.IsTrue(methodIndex >= 0, "Méthode source introuvable: " + methodName);

        int openBrace = source.IndexOf('{', methodIndex);
        Assert.IsTrue(openBrace >= 0, "Accolade ouvrante introuvable: " + methodName);

        int depth = 0;
        for (int index = openBrace; index < source.Length; index++)
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
                    return source.Substring(openBrace, index - openBrace + 1);
                }
            }
        }

        Assert.Fail("Accolade fermante introuvable: " + methodName);
        return string.Empty;
    }

    private static string ReadSource(string fileName)
    {
        string root = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(root))
        {
            string candidate = Path.Combine(root, "src", "DonJEnemySpawner", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            DirectoryInfo parent = Directory.GetParent(root);
            root = parent == null ? null : parent.FullName;
        }

        Assert.Fail("Source introuvable: " + fileName);
        return string.Empty;
    }
}
