using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyDeathFailClosedTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void CustodyDeath_WalFailureArmsFailClosedStateAndPendingHolding()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
        string observed = ReadMethod(source, "ObserveJusticeCustodyDeath");
        AssertOrdered(
            observed,
            "TryPersistJusticeCustodyDeathFrontToWal(player)",
            "ArmJusticeCustodyDeathFailClosedState(player, now)",
            "CancelJusticeCustodyActivity(false, now)",
            "ResetJusticeCustodyClock(now)");

        string suspended = ReadMethod(
            source,
            "ObserveJusticeCustodyDeathDuringSuspension");
        AssertOrdered(
            suspended,
            "TryPersistJusticeCustodyDeathFrontToWal(player)",
            "ArmJusticeCustodyDeathFailClosedState(");

        string arm = ReadMethod(
            source,
            "ArmJusticeCustodyDeathFailClosedState");
        StringAssert.Contains(arm, "_justiceCustodyWaitingForRespawn = true");
        StringAssert.Contains(arm, "_justiceCustodyDeathRebindPending = true");
        StringAssert.Contains(
            arm,
            "_justiceCustodyDeathStatePersistencePending = true");
        AssertOrdered(
            arm,
            "_justiceCustodyContainmentEstablished = false",
            "_justiceOutsideCustodySinceAt = 0",
            "PersistJusticeCustodyDeathStateBeforeRespawn(now)");

        string refresh = ReadMethod(
            source,
            "RefreshJusticePreJudgmentHoldingIntent");
        StringAssert.Contains(
            refresh,
            "TryResolveJusticePendingWalCustodyRebindHoldingIntent(");
        StringAssert.Contains(
            refresh,
            "JusticePreJudgmentHoldingSource.PendingWalCustodyRebind");

        string pending = ReadMethod(
            source,
            "TryResolveJusticePendingWalCustodyRebindHoldingIntent");
        StringAssert.Contains(pending, "JusticeCustodyDeathFrontMode");
        StringAssert.Contains(pending, "IsJusticeCustodyPhase(ownerCase.Phase)");
        StringAssert.Contains(pending, "record.ProfileSlot");
        StringAssert.Contains(pending, "_justiceCustodyPlayerSlot");
        Assert.IsFalse(pending.Contains("JusticeMarkStateDirty"));
        Assert.IsFalse(pending.Contains("SentenceSeconds ="));

        string blocking = ReadMethod(
            source,
            "MustBlockJusticeLateForPreJudgmentHolding");
        StringAssert.Contains(
            blocking,
            "JusticePreJudgmentHoldingSource.PendingWalCustodyRebind");
        StringAssert.Contains(blocking, "_justiceCustodyWaitingForRespawn");
        StringAssert.Contains(blocking, "_justiceCustodyDeathRebindPending");
    }

#if DONJ_STUB_API
    [TestMethod]
    public void CustodyDeath_WalOutageReholdsRespawnAtPrisonAndFreezesSentence()
    {
        GTA.StubRuntime.Reset();
        ulong groundProbe = GetPrivateConstant<ulong>(
            "JusticeNativeGetGroundZFor3DCoord");
        ulong collisionProbe = GetPrivateConstant<ulong>(
            "JusticeNativeHasCollisionLoadedAroundEntity");
        GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            hash == groundProbe || hash == collisionProbe
                ? (object)true
                : null;
        GTA.Ped player = GTA.Game.Player.Character;
        player.Handle = 981;
        player.Model = new GTA.Model("player_zero");
        player.Position = new GTA.Math.Vector3(1691.0f, 2566.0f, 45.5f);
        player.IsDead = true;

        object script = CreateStableCustodyScript(player, 600);
        JusticeCaseState state = GetField<JusticeCaseState>(
            script,
            "_justiceCaseState");

        Invoke(script, "ObserveJusticeCustodyDeath", player, 1000);

        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyWaitingForRespawn"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyDeathRebindPending"));
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyDeathStatePersistencePending"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyContainmentEstablished"));
        object pending = GetFieldObject(
            script,
            "_justicePendingDeathFrontWalRecord");
        Assert.IsNotNull(pending);
        Assert.AreEqual(
            "CustodyRebind",
            ReadWalField((JusticeWalRecord)pending, "mode"));
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);

        // Je reproduis le respawn vanilla à l'hôpital alors que le WAL reste
        // indisponible. Le holding doit masquer ce point puis replacer le détenu.
        player.IsDead = false;
        player.Position = new GTA.Math.Vector3(307.0f, -595.0f, 43.0f);
        Invoke(script, "UpdateJusticeCustodyRespawnTransferMask", player);
        bool blocksLate = (bool)Invoke(
            script,
            "UpdateJusticePoliceDeathPreJudgmentHolding",
            player,
            2000);

        Assert.IsTrue(blocksLate);
        Assert.AreEqual(
            "PendingWalCustodyRebind",
            GetFieldObject(script, "_justicePreJudgmentHoldingSource").ToString());
        Assert.AreEqual(
            "Bolingbroke",
            GetFieldObject(
                script,
                "_justicePoliceDeathPreJudgmentHoldingSite").ToString());
        Assert.IsTrue(GetField<bool>(
            script,
            "_justicePoliceDeathPreJudgmentHoldingEstablished"));
        Assert.IsTrue((bool)Invoke(
            script,
            "IsInsideJusticePoliceDeathPreJudgmentHolding",
            player.Position));
        Assert.IsFalse(player.FreezePosition);
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyRespawnTransferPending"));
        Assert.AreEqual(600, state.SentenceSeconds);
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);

        int fadeOut = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_OUT);
        int fadeIn = CountNative(GTA.Native.Hash.DO_SCREEN_FADE_IN);
        Assert.IsTrue(fadeOut >= 1, "L'hôpital doit être masqué avant le déplacement.");
        Assert.IsTrue(fadeIn >= 1, "L'écran doit revenir seulement dans l'enceinte.");

        Invoke(script, "JusticeUpdateCustody", player, 3000);
        Invoke(script, "JusticeUpdateCustody", player, 8000);
        Assert.AreEqual(
            600,
            state.SentenceSeconds,
            "La peine doit rester suspendue tant que CustodyRebind n'est pas durable.");
        Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyContainmentEstablished"));
        Assert.IsTrue((bool)Invoke(script, "IsInsideJusticeCustody", player.Position));
    }

    private static object CreateStableCustodyScript(GTA.Ped player, int sentenceSeconds)
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticePlayerProfileState[] profiles =
        {
            new JusticePlayerProfileState(0),
            new JusticePlayerProfileState(1),
            new JusticePlayerProfileState(2)
        };
        JusticeCaseState state = profiles[0].CaseState;
        state.Enabled = true;
        state.Phase = JusticePhase.Incarcerated;
        state.SentenceSeconds = sentenceSeconds;
        state.CustodyEpisodeId = "custody:wal-outage";
        profiles[0].LastCanonicalPlayerModel = player.Model.Hash;

        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profiles[0].RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceProfilePersistenceGenerations", new[] { 0L, 0L, 0L });
        SetField(
            script,
            "_justiceCanonicalPlayerSlotOverride",
            new Func<int>(() => 0));
        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
        SetField(script, "_justiceCustodyContainmentEstablished", true);
        SetField(script, "_justicePersistenceServicesUnavailable", true);
        SetField(script, "_justicePersistenceInitializationFailurePermanent", true);
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);

        foreach (string collectionField in new[]
        {
            "_justiceCustodyGuards",
            "_justiceCustodyInmates",
            "_justiceActivityCooldownUntil",
            "_justiceLoadedActivityCooldownSeconds"
        })
        {
            InitializeEmptyCollectionField(script, collectionField);
        }
        return script;
    }

    private static int CountNative(GTA.Native.Hash hash)
    {
        return GTA.StubRuntime.NativeCalls.Count(call =>
            call.Hash == (ulong)hash);
    }

    [TestCleanup]
    public void ResetStubRuntimeHandler()
    {
        GTA.StubRuntime.NativeCallHandler = null;
    }
#endif

    private static string ReadWalField(JusticeWalRecord record, string path)
    {
        JusticePersistenceField field = record.Fields.Single(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal));
        return field.Value;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null &&
               !File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
        {
            current = current.Parent;
        }
        Assert.IsNotNull(current, "Racine du dépôt introuvable.");
        return current.FullName;
    }

    private static string ReadMethod(string source, string methodName)
    {
        int name = -1;
        int searchFrom = 0;
        while (searchFrom < source.Length)
        {
            int candidate = source.IndexOf(
                methodName + "(",
                searchFrom,
                StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }
            int lineStart = source.LastIndexOf('\n', candidate);
            int prefixStart = lineStart < 0 ? 0 : lineStart + 1;
            string prefix = source.Substring(prefixStart, candidate - prefixStart);
            if (prefix.IndexOf("private ", StringComparison.Ordinal) >= 0)
            {
                name = candidate;
                break;
            }
            searchFrom = candidate + methodName.Length + 1;
        }
        Assert.IsTrue(name >= 0, "Méthode source introuvable : " + methodName);
        int open = source.IndexOf('{', name);
        Assert.IsTrue(open >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(open, index - open + 1);
            }
        }
        Assert.Fail("Fin de méthode source introuvable : " + methodName);
        return string.Empty;
    }

    private static void AssertOrdered(string source, params string[] fragments)
    {
        int cursor = -1;
        foreach (string fragment in fragments)
        {
            int index = source.IndexOf(fragment, cursor + 1, StringComparison.Ordinal);
            Assert.IsTrue(index > cursor, "Fragment absent ou désordonné : " + fragment);
            cursor = index;
        }
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo[] methods = target.GetType()
            .GetMethods(PrivateInstance)
            .Where(candidate => candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length)
            .ToArray();
        Assert.AreEqual(1, methods.Length, "Méthode privée ambiguë : " + methodName);
        return methods[0].Invoke(target, arguments);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)GetFieldObject(target, fieldName);
    }

    private static object GetFieldObject(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return field.GetValue(target);
    }

    private static T GetPrivateConstant<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, "Constante privée introuvable : " + fieldName);
        return (T)field.GetRawConstantValue();
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static void SetEnumField(object target, string fieldName, string value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ enum privé introuvable : " + fieldName);
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static void InitializeEmptyCollectionField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Collection privée introuvable : " + fieldName);
        object value = field.FieldType.IsInterface
            ? null
            : Activator.CreateInstance(field.FieldType, true);
        if (value == null && field.FieldType.IsGenericType)
        {
            Type generic = field.FieldType.GetGenericTypeDefinition();
            Type[] arguments = field.FieldType.GetGenericArguments();
            if (generic == typeof(IList<>) || generic == typeof(ICollection<>))
            {
                value = Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments));
            }
            else if (generic == typeof(IDictionary<,>))
            {
                value = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
            }
        }
        Assert.IsNotNull(value, "Collection non initialisable : " + fieldName);
        field.SetValue(target, value);
    }
}
