using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class PlayerInvincibilityRegressionTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public void PlacementLifecycle_CleansPartialStartupAndUsesSharedProtection()
    {
        string placementSource = ReadSource("DonJEnemySpawner.cs");
        string protectionSource = ReadSource("DonJEnemySpawner.PlayerProtection.cs");
        string start = ExtractMethodBody(placementSource, "StartPlacementMode");
        string stop = ExtractMethodBody(placementSource, "StopPlacementMode");
        string keepSafe = ExtractMethodBody(placementSource, "KeepPlayerSafeDuringPlacement");
        string placementSessionState = ExtractMethodBody(
            placementSource,
            "HasPlacementSessionState");
        string custodySource = ReadSource("DonJEnemySpawner.Justice.Custody.cs");
        string custodyStart = ExtractMethodBody(
            custodySource,
            "JusticeBeginCustodyTransfer");

        StringAssert.Contains(start, "TryAcquirePlayerInvincibility(");
        StringAssert.Contains(start, "PlayerInvincibilityOwner.Placement");
        StringAssert.Contains(start, "StopPlacementMode(true)");
        StringAssert.Contains(stop, "TryRestorePlacementPlayerState()");
        Assert.IsFalse(
            stop.Contains("if (!_placementMode)"),
            "Le nettoyage ne doit pas ignorer un démarrage interrompu avant le commit du mode.");
        StringAssert.Contains(keepSafe, "HasPlayerInvincibilityOwner(");
        StringAssert.Contains(protectionSource, "_playerInvincibilityOwners |= owner");
        StringAssert.Contains(protectionSource, "TryRestoreSharedPlayerInvincibility");
        StringAssert.Contains(
            placementSessionState,
            "!object.ReferenceEquals(_placementCamera, null)");
        Assert.IsFalse(
            placementSessionState.Contains("_placementCamera != null"),
            "Le contrat ABI v2 ne permet pas d'appeler l'opérateur Camera !=.");
        AssertOrdered(
            custodyStart,
            "if (HasPlacementSessionState())",
            "StopPlacementMode(false)",
            "if (_placementPlayerStateStored ||",
            "HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement)",
            "return;",
            "_justiceCustodySite = GetJusticeCustodySiteForSentence");
    }

    [TestMethod]
    public void JusticeCustody_MisconductNeverAcquiresInvincibility()
    {
        string source = ReadSource("DonJEnemySpawner.Justice.Custody.cs");

        Assert.IsFalse(source.Contains("BeginJusticeCustodyDiscipline"));
        Assert.IsFalse(source.Contains("TryRestoreJusticeDisciplineInvincibility"));
        Assert.IsFalse(source.Contains("PlayerInvincibilityOwner.JusticeDiscipline"));
        Assert.IsFalse(source.Contains("TASK_COMBAT_PED"));
    }

#if DONJ_STUB_API
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PlacementCameraFailureBeforeCommit_RestoresExactPlayerState(
        bool initialInvincibility)
    {
        string temporarySaveDirectory = Path.Combine(
            Path.GetTempPath(),
            "DonJInvincibilityRegression_" + Guid.NewGuid().ToString("N"));
        string previousSaveDirectory = Environment.GetEnvironmentVariable(
            "DONJ_ENEMY_SPAWNER_SAVE_DIR");
        DonJEnemySpawner script = null;

        try
        {
            Directory.CreateDirectory(temporarySaveDirectory);
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                temporarySaveDirectory);
            StubRuntime.Reset();

            Ped player = Game.Player.Character;
            player.Handle = 77;
            player.IsInvincible = initialInvincibility;
            player.FreezePosition = false;
            script = new DonJEnemySpawner();

            // L'ancien code laissait le joueur invincible car _placementMode
            // n'était pas encore true lorsque cette native levait.
            StubRuntime.NativeCallHandler = (hash, arguments) =>
            {
                if (hash == (ulong)Hash.GET_GAMEPLAY_CAM_COORD)
                {
                    throw new MissingMethodException(
                        "Simulation d'une native caméra indisponible.");
                }

                return null;
            };

            InvokeInstance(script, "StartPlacementMode");

            Assert.AreEqual(initialInvincibility, player.IsInvincible);
            Assert.IsFalse(player.FreezePosition);
            Assert.IsFalse(GetField<bool>(script, "_placementMode"));
            Assert.IsFalse(GetField<bool>(script, "_placementPlayerStateStored"));
            Assert.IsFalse(GetField<bool>(script, "_playerInvincibilityBaselineCaptured"));
            Assert.IsFalse(GetField<bool>(script, "_playerInvincibilityRestorePending"));
            Assert.AreEqual(
                0,
                Convert.ToInt32(GetField<object>(script, "_playerInvincibilityOwners")));
            Assert.IsNull(World.RenderingCamera);
        }
        finally
        {
            StubRuntime.NativeCallHandler = null;
            if (script != null)
            {
                RaiseStubScriptEvent(script, "RaiseAborted");
            }

            StubRuntime.Reset();
            Environment.SetEnvironmentVariable(
                "DONJ_ENEMY_SPAWNER_SAVE_DIR",
                previousSaveDirectory);
            if (Directory.Exists(temporarySaveDirectory))
            {
                Directory.Delete(temporarySaveDirectory, true);
            }
        }
    }

    [TestMethod]
    public void SharedOwners_RestoreFalseBaselineInBothReleaseOrders()
    {
        VerifySharedOwnerReleaseOrder(
            "Placement",
            "JusticePreJudgmentHolding",
            false);
        VerifySharedOwnerReleaseOrder(
            "JusticePreJudgmentHolding",
            "Placement",
            false);
    }

    [TestMethod]
    public void SharedOwners_PreservePreexistingExternalInvincibility()
    {
        VerifySharedOwnerReleaseOrder(
            "Placement",
            "JusticePreJudgmentHolding",
            true);
    }

    [TestMethod]
    public void UntrackedPlacementCleanup_DoesNotOverwriteAnUnknownExternalBaseline()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Ped player = new Ped
        {
            Handle = 73,
            IsInvincible = true
        };
        object placement = GetOwner("Placement");

        Assert.IsTrue(Release(script, player, placement, false));
        Assert.IsTrue(
            player.IsInvincible,
            "Un démarrage interrompu avant la capture ne doit pas forcer false.");
    }

    [TestMethod]
    public void PersistedJusticeCleanup_CanUseItsDurableFallbackAfterReload()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Ped player = new Ped
        {
            Handle = 74,
            IsInvincible = true
        };
        object justice = GetOwner("JusticePreJudgmentHolding");

        Assert.IsTrue(Release(script, player, justice, false, true));
        Assert.IsFalse(player.IsInvincible);
    }

    [TestMethod]
    public void ShutdownFailSafe_ReleasesAnUnclosedProtectionOwner()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Ped player = new Ped
        {
            Handle = 91,
            IsInvincible = false
        };
        object placement = GetOwner("Placement");

        bool baseline;
        Assert.IsTrue(Acquire(script, player, placement, out baseline));
        Assert.IsFalse(baseline);
        Assert.IsTrue(player.IsInvincible);

        InvokeInstance(script, "ShutdownPlayerInvincibilityProtection");

        Assert.IsFalse(player.IsInvincible);
        Assert.IsFalse(GetField<bool>(script, "_playerInvincibilityBaselineCaptured"));
        Assert.AreEqual(
            0,
            Convert.ToInt32(GetField<object>(script, "_playerInvincibilityOwners")));
    }

    private static void VerifySharedOwnerReleaseOrder(
        string firstOwnerName,
        string secondOwnerName,
        bool initialInvincibility)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Ped player = new Ped
        {
            Handle = 42,
            IsInvincible = initialInvincibility
        };
        object firstOwner = GetOwner(firstOwnerName);
        object secondOwner = GetOwner(secondOwnerName);

        bool firstBaseline;
        bool secondBaseline;
        Assert.IsTrue(Acquire(script, player, firstOwner, out firstBaseline));
        Assert.IsTrue(Acquire(script, player, secondOwner, out secondBaseline));
        Assert.AreEqual(initialInvincibility, firstBaseline);
        Assert.AreEqual(initialInvincibility, secondBaseline);
        Assert.IsTrue(player.IsInvincible);

        Assert.IsTrue(Release(script, player, firstOwner, firstBaseline));
        Assert.IsTrue(
            player.IsInvincible,
            "Le premier propriétaire ne doit pas désactiver la protection du second.");

        Assert.IsTrue(Release(script, player, secondOwner, secondBaseline));
        Assert.AreEqual(initialInvincibility, player.IsInvincible);
        Assert.AreEqual(
            0,
            Convert.ToInt32(GetField<object>(script, "_playerInvincibilityOwners")));
        Assert.IsFalse(GetField<bool>(script, "_playerInvincibilityBaselineCaptured"));
    }

    private static bool Acquire(
        object script,
        Ped player,
        object owner,
        out bool baseline)
    {
        MethodInfo method = ScriptType.GetMethod(
            "TryAcquirePlayerInvincibility",
            PrivateInstance);
        Assert.IsNotNull(method);
        object[] arguments = { player, owner, false };
        bool result = (bool)method.Invoke(script, arguments);
        baseline = (bool)arguments[2];
        return result;
    }

    private static bool Release(
        object script,
        Ped player,
        object owner,
        bool baseline,
        bool allowUntrackedFallback = false)
    {
        MethodInfo method = ScriptType.GetMethod(
            "TryReleasePlayerInvincibility",
            PrivateInstance);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(
            script,
            new[]
            {
                player,
                owner,
                (object)baseline,
                allowUntrackedFallback
            });
    }

    private static object GetOwner(string name)
    {
        Type ownerType = ScriptType.GetNestedType(
            "PlayerInvincibilityOwner",
            BindingFlags.NonPublic);
        Assert.IsNotNull(ownerType);
        return Enum.Parse(ownerType, name);
    }

    private static void RaiseStubScriptEvent(
        DonJEnemySpawner script,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(Script).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "Événement du stub introuvable: " + methodName);
        method.Invoke(script, arguments);
    }
#endif

    private static object InvokeInstance(
        object target,
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Méthode privée introuvable: " + methodName);
        return method.Invoke(target, arguments);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable: " + fieldName);
        return (T)field.GetValue(target);
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
        string[] prefixes =
        {
            "private void ",
            "private bool ",
            "private static bool "
        };
        int methodIndex = -1;
        foreach (string prefix in prefixes)
        {
            methodIndex = source.IndexOf(prefix + methodName + "(", StringComparison.Ordinal);
            if (methodIndex >= 0)
            {
                break;
            }
        }

        Assert.IsTrue(methodIndex >= 0, "Méthode source introuvable: " + methodName);
        int openBrace = source.IndexOf('{', methodIndex);
        Assert.IsTrue(openBrace >= 0);
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
