using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticePersistenceStallContractTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

#if DONJ_STUB_API
    [TestMethod]
    public void CustodyRebind_RuntimeWriterOutageKeepsPhysicalCustodyWithoutBusinessMutation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeRuntimeWriterOutage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        JusticeRepository repository = new JusticeRepository(
            Path.Combine(directory, "_justice_state.xml"),
            Path.Combine(directory, "_justice_state.xml.bak"),
            new JusticeXmlPersistenceCodec(),
            3L);
        try
        {
            // Je reproduis un writer runtime déjà démarré dont la révision
            // CustodyRebind reste en mémoire après plusieurs échecs disque.
            SetPrivateField(repository, "_state", JusticeRepositoryState.Running);
            SetPrivateField(repository, "_memoryRevision", 10L);
            SetPrivateField(repository, "_diskRevision", 3L);
            SetPrivateField(repository, "_writeAttempts", 8L);
            SetPrivateField(repository, "_writeFailures", 8L);
            SetPrivateField(repository, "_lastError", "writer runtime indisponible");

            GTA.StubRuntime.Reset();
            GTA.Ped player = GTA.Game.Player.Character;
            player.Handle = 812;
            player.Model = new GTA.Model("player_zero");
            player.Position = new GTA.Math.Vector3(307.0f, -595.0f, 43.0f);
            player.IsDead = false;
            player.FreezePosition = true;
            player.CanRagdoll = false;

            object script = CreateCustodyRebindScript(player, repository);
            JusticeCaseState state = GetField<JusticeCaseState>(
                script,
                "_justiceCaseState");
            object inventoryStateBefore = GetFieldObject(
                script,
                "_justiceInventoryCustodyState");
            int chargeCountBefore = state.Charges.Count;
            int processedCountBefore = state.ProcessedIncidentIds.Count;
            int operationCountBefore = state.CompletedOperationIds.Count;
            int fadeInCount = 0;
            bool fadeInOutsideBolingbroke = false;
            bool fadeInWhileFrozen = false;
            ulong groundProbe = GetPrivateConstant<ulong>(
                "JusticeNativeGetGroundZFor3DCoord");
            ulong collisionProbe = GetPrivateConstant<ulong>(
                "JusticeNativeHasCollisionLoadedAroundEntity");

            GTA.StubRuntime.NativeCallHandler = (hash, arguments) =>
            {
                if (hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN)
                {
                    fadeInCount++;
                    fadeInOutsideBolingbroke |= !(bool)Invoke(
                        script,
                        "IsInsideJusticeCustody",
                        player.Position);
                    fadeInWhileFrozen |= player.FreezePosition;
                }
                if (hash == groundProbe || hash == collisionProbe)
                {
                    // Je valide uniquement le streaming visé par ce scénario :
                    // tous les autres appels natifs gardent le défaut strict.
                    return true;
                }
                return null;
            };

            Invoke(script, "JusticeUpdateCustody", player, 4000);

            Assert.IsTrue(
                GetField<bool>(script, "_justiceCustodyPersistenceOutageHoldingEstablished"),
                "Une panne durable du writer runtime doit activer le maintien physique, même si l'initialisation avait réussi.");
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyContainmentEstablished"));
            Assert.IsTrue((bool)Invoke(
                script,
                "IsInsideJusticeCustody",
                player.Position));
            Assert.IsFalse(
                player.FreezePosition,
                "Le détenu doit être mobile avant de rendre l'écran.");
            Assert.IsTrue(fadeInCount >= 1, "Le masque ne doit pas rester noir.");
            Assert.IsFalse(
                fadeInOutsideBolingbroke,
                "Aucun fade-in ne doit révéler l'hôpital ou une position hors enceinte.");
            Assert.IsFalse(
                fadeInWhileFrozen,
                "Aucun fade-in ne doit précéder la preuve de mobilité du détenu.");
            Assert.IsFalse(GetField<bool>(
                script,
                "_justiceCustodyRespawnTransferPending"));

            // Je conserve la frontière métier exactement telle que le WAL
            // CustodyRebind l'avait appliquée : seul le monde est remis en sûreté.
            Assert.AreEqual(JusticePhase.Incarcerated, state.Phase);
            Assert.AreEqual(600, state.SentenceSeconds);
            Assert.AreEqual("custody:runtime-writer-outage", state.CustodyEpisodeId);
            Assert.AreEqual(chargeCountBefore, state.Charges.Count);
            Assert.AreEqual(processedCountBefore, state.ProcessedIncidentIds.Count);
            Assert.AreEqual(operationCountBefore, state.CompletedOperationIds.Count);
            Assert.AreEqual(
                inventoryStateBefore.ToString(),
                GetFieldObject(script, "_justiceInventoryCustodyState").ToString());
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyWaitingForRespawn"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyDeathRebindPending"));
            Assert.IsTrue(GetField<bool>(
                script,
                "_justiceCustodyDeathStatePersistencePending"));
            Assert.IsNull(
                GetFieldObject(script, "_justicePendingDeathFrontWalRecord"),
                "Le WAL a déjà été appliqué; seul son snapshot résultat reste à durcir.");

            Invoke(script, "JusticeUpdateCustody", player, 4500);
            Assert.AreEqual(
                600,
                state.SentenceSeconds,
                "La peine doit rester suspendue pendant toute la panne runtime.");
            Assert.AreEqual(
                fadeInCount,
                GTA.StubRuntime.NativeCalls.Count(call =>
                    call.Hash == (ulong)GTA.Native.Hash.DO_SCREEN_FADE_IN),
                "Le maintien déjà établi ne doit pas refondre l'écran à chaque tick.");
        }
        finally
        {
            GTA.StubRuntime.NativeCallHandler = null;
            SetPrivateField(repository, "_state", JusticeRepositoryState.Created);
            repository.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
#endif

    [TestMethod]
    public void ProfileResetPendingWal_IsRetriedAfterCadenceInsteadOfSortingEveryFrame()
    {
        string justiceSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs");
        string early = ReadMethod(justiceSource, "UpdateJusticeEarly");

        int cadenceGuard = early.IndexOf(
            "if (_justiceMonotonicTimeMs < _justiceNextEarlyScanAtMs)",
            StringComparison.Ordinal);
        int cadenceArm = early.IndexOf(
            "_justiceNextEarlyScanAtMs =",
            cadenceGuard < 0 ? 0 : cadenceGuard,
            StringComparison.Ordinal);
        int resetResume = early.IndexOf(
            "TryResumePendingJusticeProfileResetWal()",
            StringComparison.Ordinal);

        Assert.IsTrue(cadenceGuard >= 0, "La cadence scalaire Justice est absente.");
        Assert.IsTrue(cadenceArm > cadenceGuard, "La prochaine échéance doit être armée après son garde.");
        Assert.IsTrue(resetResume > cadenceArm,
            "Un ProfileReset pending ne doit plus finaliser, trier ou copier le WAL avant la cadence de tick.");

        string resetSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Persistence.ProfileReset.cs");
        string resume = ReadMethod(
            resetSource,
            "TryResumePendingJusticeProfileResetWal");
        Assert.AreEqual(
            1,
            CountOccurrences(
                resume,
                "FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable()"),
            "Une échéance ne doit lancer qu'une seule passe de finalisation WAL.");

        string system = ReadMethod(justiceSource, "UpdateJusticeSystem");
        StringAssert.Contains(
            system,
            "HasOpenJusticeProfileResetWal()",
            "Le gameplay doit rester gelé entre deux retries cadencés.");

        string walSource = ReadSource(
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Wal.cs");
        string kindProbe = ReadMethod(walSource, "HasOpenTransactionKind");
        Assert.IsFalse(
            kindProbe.Contains("GetOpenTransactions("),
            "Le garde exécuté entre deux échéances ne doit pas matérialiser la liste triée.");
        Assert.IsFalse(
            kindProbe.Contains("new List<JusticeWalRecord>"),
            "Le garde chaud ProfileReset doit rester sans copie de liste.");
    }

#if DONJ_STUB_API
    private static object CreateCustodyRebindScript(
        GTA.Ped player,
        JusticeRepository repository)
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
        state.SentenceSeconds = 600;
        state.CustodyEpisodeId = "custody:runtime-writer-outage";
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:runtime-writer-outage",
            IncidentId = "incident:runtime-writer-outage",
            EpisodeId = state.CustodyEpisodeId,
            DisplayName = "Refus d'obtempérer",
            Points = 30,
            SentenceSeconds = 600
        });
        state.ProcessedIncidentIds.Add("incident:runtime-writer-outage");
        profiles[0].LastCanonicalPlayerModel = player.Model.Hash;

        SetField(script, "_justicePlayerProfiles", profiles);
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceRecordState", profiles[0].RecordState);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceInitialized", true);
        SetField(script, "_justiceActivePlayerProfileSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerSlot", 0);
        SetField(script, "_justiceLastCanonicalPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceProfilePersistenceGenerations", new[] { 2L, 0L, 0L });
        SetField(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        SetField(script, "_justiceRepository", repository);
        SetField(script, "_justicePersistenceRevision", 10L);
        SetField(script, "_justiceLastQueuedPersistenceRevision", 10L);
        SetField(script, "_justiceObservedRepositoryDiskRevision", 3L);
        SetField(script, "_justiceObservedRepositoryWriteFailures", 8L);
        SetField(script, "_justicePersistenceServicesUnavailable", false);
        SetField(script, "_justicePersistenceInitializationFailurePermanent", false);

        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyWaitingForRespawn", true);
        SetField(script, "_justiceCustodyDeathRebindPending", true);
        SetField(script, "_justiceCustodyDeathStatePersistencePending", true);
        SetField(script, "_justiceCustodyDeathPersistenceRevision", 10L);
        SetField(script, "_justiceCustodyDeathPersistenceWriteFailures", 7L);
        SetField(script, "_justiceNextCustodyDeathPersistenceRetryAt", 5000);
        SetField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetField(script, "_justiceCustodyPlayerSlot", 0);
        SetEnumField(script, "_justiceCustodySite", "Bolingbroke");
        SetEnumField(script, "_justiceInventoryCustodyState", "None");
        SetField(script, "_justiceCustodyContainmentEstablished", false);
        SetField(script, "_justiceCustodyRespawnTransferPending", false);
        SetField(script, "_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);

        foreach (string collectionField in new[]
        {
            "_justiceCustodyGuards",
            "_justiceCustodyInmates"
        })
        {
            InitializeEmptyCollectionField(script, collectionField);
        }
        return script;
    }
#endif

    private static string ReadSource(params string[] pathParts)
    {
        string path = pathParts.Aggregate(
            GetRepositoryRoot(),
            Path.Combine);
        return File.ReadAllText(path);
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
            if (prefix.IndexOf("private ", StringComparison.Ordinal) >= 0 ||
                prefix.IndexOf("internal ", StringComparison.Ordinal) >= 0)
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

    private static int CountOccurrences(string source, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(
                    fragment,
                    index,
                    StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    private static object Invoke(
        object target,
        string methodName,
        params object[] arguments)
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
        SetPrivateField(target, fieldName, value);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
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

    private static void InitializeEmptyCollectionField(
        object target,
        string fieldName)
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
                value = Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(arguments));
            }
            else if (generic == typeof(IDictionary<,>))
            {
                value = Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(arguments));
            }
        }
        Assert.IsNotNull(value, "Collection non initialisable : " + fieldName);
        field.SetValue(target, value);
    }
}
