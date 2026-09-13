using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml;
using DonJ.JusticeRecognition;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Math;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticeReviewRegressionTests
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private string _directory;
    private string _previousSavePath;
    private readonly List<object> _scripts = new List<object>();

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DonJJusticeReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousSavePath = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _directory);
        JusticeRecognitionBridge.BindObserverExclusion(null);
#if DONJ_STUB_API
        StubRuntime.Reset();
#endif
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (object script in _scripts) Invoke(script, "ShutdownJusticePersistenceServices");
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _previousSavePath);
        JusticeRecognitionBridge.BindObserverExclusion(null);
#if DONJ_STUB_API
        StubRuntime.Reset();
#endif
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public void DisputeResolution_PreservesAlreadyServedSentenceAndConvertedDebt()
    {
        object script = PaymentScript();
        JusticeCaseState state = Get<JusticeCaseState>(script, "_justiceCaseState");
        state.Charges[0].SentenceSeconds = 240;
        state.Charges[0].IsAdjudicated = true;
        state.SentenceSeconds = 40;
        state.CustodyGuardPenaltySeconds = 60;
        state.FineDue = 0;
        state.FineInDispute = 600;
        // Je vérifie l'action réelle sans publier cette fixture de peine isolée.
        Set(script, "_justiceInitialized", false);
        Invoke(script, "ResolveJusticeFineDisputeInPlayerFavor", 0, 600L);
        Assert.AreEqual(40, state.SentenceSeconds);
        Assert.AreEqual(60L, state.CustodyGuardPenaltySeconds);
        Assert.AreEqual(0L, state.FineDue);
        Assert.AreEqual(0L, state.FineInDispute);
        Assert.AreEqual(600L, state.VoluntaryFinePaid);
    }

    [DataTestMethod]
    [DataRow(0, 400, 800L)] [DataRow(0, 2000, 0L)]
    [DataRow(1, 400, 800L)] [DataRow(1, 2000, 0L)]
    [DataRow(2, 400, 800L)] [DataRow(2, 2000, 0L)]
    public void PaymentWhileDisabled_RoundTripsWithoutReactivationOrDoubleDebit(int playerSlot, int initialCash, long expectedDebt)
    {
        object script = PaymentScript();
        Set(script, "_justiceActivePlayerProfileSlot", playerSlot);
        Set(script, "_justiceMenuSelectedProfileSlot", playerSlot);
        Set(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => playerSlot));
        JusticeCaseState state = Get<JusticeCaseState>(script, "_justiceCaseState");
        state.Enabled = false;
        Set(script, "_justiceEnabled", false);
        int cash = initialCash;
        int writes = 0;
        Set(script, "_justiceCashReadOverride", new Func<int, int?>(slot => cash));
        Set(script, "_justiceCashWriteOverride", new Func<int, int, bool?>((slot, amount) => { writes++; cash = amount; return true; }));
        Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
        Await(script);
        Invoke(script, "RequestJusticeVoluntaryFinePayment");
        for (int i = 0; i < 16 && Get<object>(script, "_justiceVoluntaryFinePaymentIntent") != null; i++)
        {
            Await(script);
            Set(script, "_justiceNextVoluntaryPaymentResumeAt", 0);
            Invoke(script, "ResumeJusticeVoluntaryFinePayment");
        }
        Await(script);
        Assert.IsNull(Get<object>(script, "_justiceVoluntaryFinePaymentIntent"));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(expectedDebt, state.FineDue);
        Assert.IsFalse(state.Enabled);
        object reader = PaymentScript();
        Set(reader, "_justiceActivePlayerProfileSlot", playerSlot);
        Set(reader, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => playerSlot));
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", Path.Combine(_directory, "_justice_state.xml")));
        Assert.AreEqual(expectedDebt, Get<JusticeCaseState>(reader, "_justiceCaseState").FineDue);
        Assert.IsFalse(Get<JusticeCaseState>(reader, "_justiceCaseState").Enabled);
    }

    [TestMethod]
    public void CancelledUnattemptedPayment_IsAcceptedByItsReader()
    {
        object script = PaymentScript();
        Set(script, "_justiceCashReadOverride", new Func<int, int?>(slot => 2000));
        Invoke(script, "RequestJusticeVoluntaryFinePayment");
        object intent = Get<object>(script, "_justiceVoluntaryFinePaymentIntent");
        Assert.IsNotNull(intent);
        Set(intent, "CashWriteResult", Enum.Parse(intent.GetType().GetField("CashWriteResult", Fields).FieldType, "Rejected"));
        Set(intent, "Resolution", JusticePaymentResolution.Rejected);
        Set(intent, "DebtCommitted", true);
        Invoke(script, "JusticeMarkStateDirty");
        Assert.IsTrue((bool)Invoke(script, "JusticeFlushStateNow"));
        Await(script);
        string xml = File.ReadAllText(Path.Combine(_directory, "_justice_state.xml"));
        StringAssert.Contains(xml, "resolution=\"Rejected\"");
        object reader = PaymentScript();
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", Path.Combine(_directory, "_justice_state.xml")));
        Assert.AreEqual(1200L, Get<JusticeCaseState>(reader, "_justiceCaseState").FineDue);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RecognitionLockedValidCopy_NeverQuarantinesOrOverwrites(bool bothCopies)
    {
        string path = Path.Combine(_directory, "recognition.xml");
        RecognitionStore store = new RecognitionStore(path, new RecognitionLogger(Path.Combine(_directory, "recognition.log")));
        JusticeRecognitionSaveData data = new JusticeRecognitionSaveData();
        data.Profiles.Add(new RecognitionProfileData { ProfileId = "Michael" });
        Assert.IsTrue(store.ForceSave(data));
        byte[] before = File.ReadAllBytes(path);
        using (FileStream primary = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete))
        using (FileStream backup = bothCopies ? new FileStream(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.Delete) : null)
        {
            Assert.ThrowsException<IOException>(() => store.Load());
            Assert.IsFalse(Directory.Exists(path + ".corrupt-quarantine"));
        }
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(1, store.Load().Profiles.Count);
    }

    [TestMethod]
    public void CriticalJournal_LockedValidIntentSurvivesAndResumes()
    {
        string path = Path.Combine(_directory, "critical.xml");
        RecognitionCriticalIntentStore store = new RecognitionCriticalIntentStore(path);
        RecognitionCriticalIntentJournalData data = new RecognitionCriticalIntentJournalData { NextCommandId = 2 };
        data.Intents.Add(new RecognitionCriticalIntentRecord { CommandId = 1, Kind = "capture-profile", ProfileId = "Michael", Reason = "test" });
        Assert.IsTrue(store.ForceSave(data));
        using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            RecognitionCriticalIntentJournalData unreadable;
            Assert.IsFalse(store.TryLoad(out unreadable));
            Assert.IsFalse(Directory.Exists(path + ".corrupt-quarantine"));
        }
        RecognitionCriticalIntentJournalData loaded;
        Assert.IsTrue(store.TryLoad(out loaded));
        Assert.AreEqual(1, loaded.Intents.Count);
        Assert.AreEqual(1L, loaded.Intents[0].CommandId);
    }

    [DataTestMethod]
    [DataRow("")] [DataRow(".bak")] [DataRow(".tmp")]
    [DataRow(".bak.tmp")] [DataRow(".rollback")] [DataRow(".bak.rollback")]
    public void BothRecognitionStores_BlockRecoveryWhenAnyVariantIsUnavailable(string suffix)
    {
        string path = Path.Combine(_directory, "variants.xml");
        RecognitionStore store = new RecognitionStore(path, new RecognitionLogger(Path.Combine(_directory, "log.txt")));
        Assert.IsTrue(store.ForceSave(new JusticeRecognitionSaveData()));
        string criticalPath = Path.Combine(_directory, "critical-variants.xml");
        RecognitionCriticalIntentStore critical = new RecognitionCriticalIntentStore(criticalPath);
        Assert.IsTrue(critical.ForceSave(new RecognitionCriticalIntentJournalData()));
        if (suffix.Length > 0)
        {
            File.Copy(path, path + suffix, true);
            File.Copy(criticalPath, criticalPath + suffix, true);
        }
        byte[] before = File.ReadAllBytes(path + suffix);
        byte[] criticalBefore = File.ReadAllBytes(criticalPath + suffix);
        using (FileStream locked = new FileStream(path + suffix, FileMode.Open, FileAccess.Read, FileShare.Delete))
        using (FileStream lockedCritical = new FileStream(criticalPath + suffix, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            Assert.ThrowsException<IOException>(() => store.Load());
            RecognitionCriticalIntentJournalData ignored;
            Assert.IsFalse(critical.TryLoad(out ignored));
        }
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path + suffix));
        CollectionAssert.AreEqual(criticalBefore, File.ReadAllBytes(criticalPath + suffix));
        Assert.IsFalse(Directory.Exists(path + ".corrupt-quarantine"));
        Assert.IsFalse(Directory.Exists(criticalPath + ".corrupt-quarantine"));
    }

    [TestMethod]
    public void DisputeResolution_InactiveOwnerWithOpenTransactionIsBlocked()
    {
        object script = PaymentScript();
        Invoke(script, "InitializeJusticePersistenceServices");
        JusticePlayerProfileState[] profiles = Get<JusticePlayerProfileState[]>(script, "_justicePlayerProfiles");
        profiles[1].CaseState.FineInDispute = 500;
        JusticeWriteAheadLog wal = Get<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog");
        wal.Append(new JusticeWalRecord("pending:owner", "VoluntaryFinePayment", 1,
            JusticeWalState.Prepared, 0, DateTime.UtcNow.Ticks, new JusticePersistenceField[0]));
        Invoke(script, "ResolveJusticeFineDisputeInPlayerFavor", 1, 500L);
        Assert.AreEqual(500L, profiles[1].CaseState.FineInDispute);
        Assert.AreEqual(0L, profiles[1].CaseState.VoluntaryFinePaid);
    }

    [TestMethod]
    public void InventoryBarrier_WaitsForWriterAndTwoActualRotations()
    {
        object script = InventoryScript();
        Invoke(script, "InitializeJusticePersistenceServices");
        Get<JusticeRepository>(script, "_justiceRepository").Dispose();
        using (PausedStore store = new PausedStore())
        {
            string path = Path.Combine(_directory, "_justice_state.xml");
            JusticeRepository repository = new JusticeRepository(path, path + ".bak",
                new JusticeXmlPersistenceCodec(), 0, store, JusticeNoOpPersistenceFaultInjector.Instance, 10);
            Set(script, "_justiceRepository", repository);
            repository.Start();
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
            Assert.IsTrue(store.Entered.WaitOne(5000));
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
            Assert.AreEqual(0L, repository.GetDiagnostics().DiskRevision);
            Assert.IsNotNull(Get<object>(script, "_justiceWeaponSnapshot"));
            store.Continue.Set();
            Await(script);
            Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
            Await(script);
            Assert.IsTrue((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
            Assert.IsTrue(File.Exists(path + ".bak"));
        }
    }

    [TestMethod]
    public void InventoryBarrier_SkippedRevisionIsNeverTreatedAsExactProof()
    {
        object script = InventoryScript();
        Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
        Await(script);
        Invoke(script, "JusticeMarkStateDirty");
        Invoke(script, "JusticeFlushStateNow");
        Await(script);
        Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
        Assert.AreEqual(0L, Get<long>(script, "_justiceInventoryBarrierFirstProof"));
        Await(script);
        Assert.IsFalse((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
        Await(script);
        Assert.IsTrue((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly"));
    }

    [TestMethod]
    public void InventoryProgress_RoundTripsAndLegacySnapshotDefaultsToPending()
    {
        object script = InventoryScript();
        object item = ((IList)Get<object>(Get<object>(script, "_justiceWeaponSnapshot"), "Weapons"))[0];
        Set(item, "DeferredRestoreAttempted", true);
        Set(item, "DeferredRestoreCompleted", true);
        Invoke(script, "JusticeFlushStateNow");
        Await(script);
        object reader = PaymentScript();
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", Path.Combine(_directory, "_justice_state.xml")));
        object loaded = ((IList)Get<object>(Get<object>(reader, "_justiceWeaponSnapshot"), "Weapons"))[0];
        Assert.IsTrue(Get<bool>(loaded, "DeferredRestoreCompleted"));

        XmlDocument legacy = new XmlDocument { XmlResolver = null };
        legacy.LoadXml("<Custody><InventorySnapshot validated='true' selectedWeapon='101'><Weapon hash='101' ammo='60' clip='12' tint='2'/></InventorySnapshot></Custody>");
        object snapshot = InvokeStatic("ReadJusticeWeaponSnapshotXml", legacy.DocumentElement);
        Assert.IsNotNull(snapshot);
        Assert.IsFalse(Get<bool>(((IList)Get<object>(snapshot, "Weapons"))[0], "DeferredRestoreAttempted"));
        ((XmlElement)legacy.SelectSingleNode("//Weapon")).SetAttribute("restoreCompleted", "true");
        Assert.IsNull(InvokeStatic("ReadJusticeWeaponSnapshotXml", legacy.DocumentElement));
    }

#if DONJ_STUB_API
    [TestMethod]
    public void DeferredInventory_NextBatchUsesTheDurableWalWithoutRepeatingTheInitialBarrier()
    {
        object script = InventoryScript();
        JusticeInventoryPersistenceSnapshot inventory = new JusticeInventoryPersistenceSnapshot(true, 101,
            Enumerable.Range(101, 5).Select(hash => new JusticeWeaponPersistenceSnapshot(hash, 60, 12, 2, new int[0])),
            Guid.NewGuid().ToString("N"));
        Set(script, "_justiceWeaponSnapshot", InvokeStatic("RestoreJusticeInventorySnapshot", inventory));
        Ped player = new Ped(900) { Model = new Model(111) };
        HashSet<int> owned = new HashSet<int>();
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON) return owned.Contains(ReadNativeInt(args[1]));
            if (hash == 0xBF0FD6E56C964FCBUL) owned.Add(ReadNativeInt(args[1]));
            if (hash == (ulong)Hash.GET_AMMO_IN_CLIP)
            {
                typeof(OutputArgument).GetMethod("SetResult", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(typeof(int)).Invoke(args[2], new object[] { 12 });
                return owned.Contains(ReadNativeInt(args[1]));
            }
            return null;
        };
        CompleteBarrier(script);
        Assert.IsFalse((bool)Invoke(script, "RestoreJusticeDeferredWeapons", player));
        Assert.AreEqual(4, owned.Count);
        Assert.IsFalse((bool)Invoke(script, "CommitJusticeDeferredInventoryRestore"));
        // Je n'attends aucun ACK intermediaire entre ces deux lots proteges par WAL.
        Assert.IsTrue((bool)Invoke(script, "RestoreJusticeDeferredWeapons", player));
        Assert.AreEqual(5, owned.Count);
        Await(script);
        CompleteBarrier(script);
        Assert.IsTrue((bool)Invoke(script, "CommitJusticeDeferredInventoryRestore"));
        Await(script);
    }

    [TestMethod]
    public void DeferredInventory_PartialRetryAndBackupReloadPreservePlayerChanges()
    {
        object script = InventoryScript();
        Ped player = new Ped(900) { Model = new Model(111) };
        HashSet<int> owned = new HashSet<int> { 101 };
        HashSet<int> components = new HashSet<int>();
        Dictionary<int, int> ammo = new Dictionary<int, int> { [101] = 7 };
        bool allowThird = false;
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (args.Length < 2) return null;
            int weapon = ReadNativeInt(args[1]);
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON) return owned.Contains(weapon);
            if (hash == 0xBF0FD6E56C964FCBUL)
            {
                if (weapon != 103 || allowThird) { owned.Add(weapon); ammo[weapon] = ReadNativeInt(args[2]); }
            }
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON_COMPONENT) return components.Contains(weapon);
            if (hash == (ulong)Hash.GIVE_WEAPON_COMPONENT_TO_PED) components.Add(weapon);
            if (hash == (ulong)Hash.GET_AMMO_IN_CLIP)
            {
                typeof(OutputArgument).GetMethod("SetResult", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(typeof(int)).Invoke(args[2], new object[] { 12 });
                return owned.Contains(weapon);
            }
            return null;
        };
        CompleteBarrier(script);
        Assert.IsFalse((bool)Invoke(script, "RestoreJusticeDeferredWeapons", player));
        Await(script);
        Assert.AreEqual(7, ammo[101]);
        Assert.IsTrue(owned.Contains(102));
        Assert.IsFalse(owned.Contains(103));
        Assert.IsFalse(StubRuntime.NativeCalls.Any(call =>
            call.Hash == (ulong)Hash.SET_CURRENT_PED_WEAPON ||
            (call.Hash == (ulong)Hash.SET_PED_WEAPON_TINT_INDEX && ReadNativeInt(call.Arguments[1]) == 101)));
        // Je simule les choix du joueur apres la restitution partielle.
        ammo[101] = 2;
        owned.Remove(102);
        allowThird = true;
        Invoke(script, "ShutdownJusticePersistenceServices");
        object reader = PaymentScript();
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", Path.Combine(_directory, "_justice_state.xml.bak")));
        Invoke(reader, "InitializeJusticePersistenceServices");
        CompleteBarrier(reader);
        Assert.IsTrue((bool)Invoke(reader, "RestoreJusticeDeferredWeapons", player));
        Await(reader);
        Assert.AreEqual(2, ammo[101]);
        Assert.IsFalse(owned.Contains(102), "L'arme jetee apres sa restitution ne doit pas reapparaitre.");
        Assert.IsTrue(owned.Contains(103));
        CompleteBarrier(reader);
        Assert.IsTrue((bool)Invoke(reader, "CommitJusticeDeferredInventoryRestore"));
        Await(reader);
        Assert.IsNull(Get<object>(reader, "_justiceWeaponSnapshot"));
        Assert.AreEqual(0, Get<JusticeWriteAheadLog>(reader, "_justiceWriteAheadLog").GetOpenTransactions().Count);
    }

    [TestMethod]
    public void DeferredInventory_AmbiguousGiveIsNotReplayedAfterCrash()
    {
        object script = InventoryScript();
        Ped player = new Ped(900) { Model = new Model(111) };
        int attempts = 0;
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (hash == 0xBF0FD6E56C964FCBUL) { attempts++; throw new InvalidOperationException("Injected ambiguous native"); }
            return null;
        };
        CompleteBarrier(script);
        Assert.IsFalse((bool)Invoke(script, "RestoreJusticeDeferredWeapons", player));
        Await(script);
        Assert.AreEqual(3, attempts);
        Invoke(script, "ShutdownJusticePersistenceServices");
        object reader = PaymentScript();
        Assert.IsTrue((bool)Invoke(reader, "TryReadJusticeStateFile", Path.Combine(_directory, "_justice_state.xml.bak")));
        Invoke(reader, "InitializeJusticePersistenceServices");
        CompleteBarrier(reader);
        Assert.IsFalse((bool)Invoke(reader, "RestoreJusticeDeferredWeapons", player));
        Assert.AreEqual(3, attempts);
        Assert.IsNotNull(Get<object>(reader, "_justiceWeaponSnapshot"));
    }

    [TestMethod]
    public void ObserverSelection_PrioritizesPoliceAndExcludesAllies()
    {
        object script = EmptyRecognition();
        Ped player = new Ped(900) { Model = new Model("player_zero") };
        Ped[] nearby = Enumerable.Range(1, 13).Select(i => new Ped(i) { Model = new Model("a_m_m_business_01") }).ToArray();
        nearby[12].Model = new Model("s_m_y_cop_01");
        StubRuntime.NativeCallHandler = (hash, args) => hash == (ulong)Hash.IS_PED_HUMAN || hash == RecognitionNativeHashes.DoesEntityExist ? (object)true :
            hash == (ulong)Hash.GET_PED_RELATIONSHIP_GROUP_HASH && ReadNativeInt(args[0]) == 13 ? (object)Game.GenerateHash("COP") : null;
        Ped[] selected = (Ped[])Invoke(script, "SelectObserverCandidates", player, nearby);
        Assert.AreSame(nearby[12], selected[0]);
        JusticeRecognitionBridge.BindObserverExclusion(ped => ped.Handle == 13);
        selected = (Ped[])Invoke(script, "SelectObserverCandidates", player, nearby);
        Assert.IsFalse(selected.Any(ped => ped != null && ped.Handle == 13));
        Assert.AreEqual(12, selected.Length);
        Assert.AreSame(nearby[1], selected[0], "Les civils doivent avancer dans la rotation.");
        Invoke(script, "UpdateObserverExposure", nearby[0], false, 40f, 1f, 100);
        JusticeRecognitionBridge.BindObserverExclusion(ped => ped.Handle == 1);
        Invoke(script, "RemoveInvalidObservers");
        Assert.AreEqual(0, ((IDictionary)Get<object>(script, "_observerExposures")).Count);
    }

    [TestMethod]
    public void VictimAndVehicleSelection_FilterBeforeQuotaAndRotate()
    {
        object script = PaymentScript();
        Ped player = new Ped(900) { Model = new Model("player_zero") };
        JusticeWorldSnapshot snapshot = new JusticeWorldSnapshot();
        Set(script, "_justiceWorldSnapshot", snapshot);
        snapshot.NearbyPeds = Enumerable.Range(1, 9).Select(i => new Ped(i) { IsDead = true, Model = new Model("a_m_m_business_01") }).ToArray();
        Ped[] first = (Ped[])Invoke(script, "GetJusticeVictimCandidatesForActor", player);
        Assert.IsFalse(first.Any(ped => ped != null && ped.Handle == 9));
        Ped[] second = (Ped[])Invoke(script, "GetJusticeVictimCandidatesForActor", player);
        Assert.IsTrue(second.Any(ped => ped != null && ped.Handle == 9));
        snapshot.NearbyVehicles = Enumerable.Range(1, 17).Select(i => new Vehicle(i)
            { Position = new Vector3(i < 17 ? 120f : 5f, 0, 0) }).ToArray();
        Vehicle[] vehicles = (Vehicle[])Invoke(script, "GetJusticeVehicleCandidates", player);
        Assert.AreEqual(17, vehicles.Single(vehicle => vehicle != null).Handle);
    }

    [TestMethod]
    public void RecognitionInitialization_RetriesAfterLockWithBackoff()
    {
        string path = Path.Combine(_directory, "JusticeRecognition.xml");
        RecognitionStore store = new RecognitionStore(path, new RecognitionLogger(Path.Combine(_directory, "log.txt")));
        Assert.IsTrue(store.ForceSave(new JusticeRecognitionSaveData()));
        object script = EmptyRecognition();
        Set(script, "_initializationDirectoryOverride", _directory);
        using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete))
            Assert.IsFalse((bool)Invoke(script, "EnsureInitialized"));
        Assert.IsFalse((bool)Invoke(script, "EnsureInitialized"));
        Set(script, "_nextInitializationRetryAt", unchecked(Environment.TickCount - 1));
        Assert.IsTrue((bool)Invoke(script, "EnsureInitialized"));
        Assert.IsFalse(Get<bool>(script, "_initializationFailed"));
        Invoke(script, "OnAborted", null, EventArgs.Empty);
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public void EscapeCleanup_PreservesHigherWanted(int wanted)
    {
        object script = PaymentScript();
        Set(script, "_justiceCustodyGuardRetaliationActive", true);
        Get<JusticeCaseState>(script, "_justiceCaseState").Phase = JusticePhase.Escaping;
        Game.Player.WantedLevel = wanted;
        Invoke(script, "CleanupJusticeCustodyEntitiesAndGroupsCore", false);
        Invoke(script, "SetJusticeWantedMinimum", 3);
        Assert.AreEqual(Math.Max(wanted, 3), Game.Player.WantedLevel);
        Assert.IsFalse(StubRuntime.NativeCalls.Any(call => call.Hash == 0xB302540597885499UL));
    }

    private static object EmptyRecognition()
    {
        object script = FormatterServices.GetUninitializedObject(typeof(DonJJusticeRecognitionScript));
        foreach (FieldInfo field in script.GetType().GetFields(Fields))
        {
            if (field.FieldType.IsGenericType && field.FieldType.Namespace == "System.Collections.Generic")
                field.SetValue(script, Activator.CreateInstance(field.FieldType));
        }
        Set(script, "_statusSync", new object());
        Set(script, "_observerCandidates", new Ped[12]);
        return script;
    }
#endif

    private object InventoryScript()
    {
        object script = PaymentScript();
        JusticeInventoryPersistenceSnapshot inventory = new JusticeInventoryPersistenceSnapshot(true, 101,
            Enumerable.Range(101, 3).Select(hash => new JusticeWeaponPersistenceSnapshot(hash, 60, 12, 2, new[] { 7001 })),
            Guid.NewGuid().ToString("N"));
        Set(script, "_justiceWeaponSnapshot", InvokeStatic("RestoreJusticeInventorySnapshot", inventory));
        Set(script, "_justiceDeferredInventoryRestore", true);
        Set(script, "_justiceCustodyPlayerSlot", 0);
        Set(script, "_justiceCustodyPlayerModelHash", 111);
        Set(script, "_justiceInventoryCustodyState", Enum.Parse(typeof(DonJEnemySpawner).GetField("_justiceInventoryCustodyState", Fields).FieldType, "RestorePending"));
        return script;
    }

    private static int ReadNativeInt(object argument) => Convert.ToInt32(
        argument.GetType().GetProperty("Value", Fields).GetValue(argument, null));

    private static void CompleteBarrier(object script)
    {
        for (int i = 0; i < 8; i++)
        {
            if ((bool)Invoke(script, "PersistJusticeDeferredRestoreRedundantly")) return;
            Await(script);
        }
        Assert.Fail("La preuve des deux copies ne progresse pas.");
    }

    private static object InvokeStatic(string method, params object[] args) => typeof(DonJEnemySpawner).GetMethod(method, Static).Invoke(null, args);

    private sealed class PausedStore : IJusticeAtomicFileStore, IDisposable
    {
        internal readonly ManualResetEvent Entered = new ManualResetEvent(false);
        internal readonly ManualResetEvent Continue = new ManualResetEvent(false);
        public void WriteAtomically(string targetPath, string backupPath, byte[] document, IJusticePersistenceFaultInjector faultInjector)
        {
            Entered.Set();
            if (!Continue.WaitOne(10000)) throw new TimeoutException("Writer de test non libere");
            new JusticeAtomicFileStore().WriteAtomically(targetPath, backupPath, document, faultInjector);
        }
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
        public void Dispose() { Continue.Set(); Entered.Dispose(); Continue.Dispose(); }
    }

    private object PaymentScript()
    {
        object script = typeof(JusticeVoluntaryPaymentTests).GetMethod("CreatePaymentScript", Static).Invoke(null, new object[] { 1200L });
        foreach (FieldInfo field in script.GetType().GetFields(Fields))
            if (field.GetValue(script) == null && field.FieldType.IsGenericType && field.FieldType.Namespace == "System.Collections.Generic")
                field.SetValue(script, Activator.CreateInstance(field.FieldType));
        _scripts.Add(script);
        return script;
    }
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    private static void Await(object script) => Assert.IsTrue((bool)Invoke(script, "JusticeAwaitQueuedPersistenceForTests"));
}
