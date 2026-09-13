using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticeInventoryAdmissionRegressionTests
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void PreservedInventory_RetryIsCadencedAndDoesNotChangeTheXmlContract()
    {
        Assert.AreEqual(5000, typeof(DonJEnemySpawner).GetField("JusticeCustodyPreservedInventoryRetryMs", Static).GetRawConstantValue());
        Type state = typeof(DonJEnemySpawner).GetNestedType("JusticeInventoryCustodyState", BindingFlags.NonPublic);
        MethodInfo validate = typeof(DonJEnemySpawner).GetMethod("IsJusticeInventoryCustodyStateSemanticallyValid", Static);
        foreach (string name in new[] { "CapturePending", "UnsupportedPreserved" })
        {
            Assert.IsTrue((bool)validate.Invoke(null, new object[] { Enum.Parse(state, name), false, false, false, null }));
            Assert.IsFalse((bool)validate.Invoke(null, new object[] { Enum.Parse(state, name), false, true, false, null }));
        }
    }

#if DONJ_STUB_API
    private string _directory, _previousDirectory;
    private readonly List<object> _scripts = new List<object>();

    [TestInitialize]
    public void Initialize()
    {
        StubRuntime.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "DonJInventoryAdmission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousDirectory = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (object script in _scripts) Call(script, "ShutdownJusticePersistenceServices");
        StubRuntime.Reset();
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _previousDirectory);
        Directory.Delete(_directory, true);
    }

    [DataTestMethod]
    [DataRow(0, "MissionRow")]
    [DataRow(1, "MissionRow")]
    [DataRow(2, "MissionRow")]
    [DataRow(0, "Bolingbroke")]
    [DataRow(1, "Bolingbroke")]
    [DataRow(2, "Bolingbroke")]
    public void MixedInventory_IsCapturedConfiscatedAndRestoredIncludingWeaponsWithoutMagazines(int slot, string site)
    {
        object script = Custody(slot, site);
        var world = new InventoryWorld();
        world.Install();
        Call(script, "EnforceJusticeCustodyWeaponLock", Game.Player.Character);
        Assert.AreEqual(unchecked((int)WeaponHash.Unarmed), world.Selected);
        object snapshot = Capture(script);
        Set(script, "_justiceWeaponSnapshot", snapshot);
        var deposit = (JusticeInventoryPersistenceSnapshot)Call(script, "CaptureJusticeInventoryPersistenceSnapshot");
        Assert.AreEqual(4, deposit.Weapons.Count);
        Assert.AreEqual(0, deposit.Weapons.Single(w => w.WeaponHash == unchecked((int)WeaponHash.Knife)).AmmoInClip);
        Assert.AreEqual(1, deposit.Weapons.Single(w => w.WeaponHash == (int)WeaponHash.Pistol).ComponentHashes.Count);
        Assert.AreEqual("RemovedVerified", Call(script, "RemoveJusticePlayerWeaponsSafe", Game.Player.Character).ToString());
        Assert.AreEqual(0, world.Owned.Count);
        Assert.IsTrue(world.Pools.Values.All(value => value == 0));
        Assert.IsTrue((bool)Call(script, "RestoreJusticeWeaponSnapshot", Game.Player.Character));
        CollectionAssert.AreEquivalent(world.InitialWeapons, world.Owned.ToArray());
        Assert.AreEqual(180, world.Pools[42]);
        Assert.AreEqual(7, world.Pools[43]);
        Assert.AreEqual(12, world.Clips[(int)WeaponHash.Pistol]);
        Assert.AreEqual(0, world.UnsupportedClipWrites);
        Assert.AreEqual((int)WeaponHash.Pistol, world.Selected);
    }

    [DataTestMethod]
    [DataRow("clip", "chargeur")]
    [DataRow("components", "composants")]
    [DataRow("dlc", "catalogue")]
    public void UnreadableInventory_PreservesWeaponsAndRecordsTheFailedStep(string fault, string stage)
    {
        object script = Custody(2, "Bolingbroke");
        var world = new InventoryWorld { Fault = fault };
        world.Install();
        Assert.AreEqual("RetryableFailure", Call(script, "PrepareJusticeInventoryConfiscation", Game.Player.Character).ToString());
        Assert.AreEqual(stage, Get<string>(script, "_justiceInventoryCaptureStage"));
        Assert.IsNull(Get<object>(script, "_justiceWeaponSnapshot"));
        Assert.AreEqual(0, world.Removals);
        CollectionAssert.AreEquivalent(world.InitialWeapons, world.Owned.ToArray());
        Assert.AreEqual(5000, Get<int>(script, "_justiceNextInventoryPersistenceRetryAt"));
    }

    [TestMethod]
    public void FailedGunClipRead_CannotBeNormalizedToAnAbsentMagazine()
    {
        object script = Custody(0, "MissionRow");
        var world = new InventoryWorld { Fault = "clip", GroupOverride = unchecked((int)3566412244U) };
        world.Install();
        object[] args = { Game.Player.Character, null };
        Assert.IsFalse((bool)Call(script, "TryCaptureJusticeWeaponSnapshot", args));
        Assert.IsNull(args[1]);
        Assert.AreEqual(0, world.Removals,
            "Une capacité positive interdit le fallback sans chargeur, même avec une catégorie de mêlée incohérente.");
    }

    [TestMethod]
    public void WeaponLock_PreservesAnUnknownSelectionSoPartialCataloguesRemainNonDestructive()
    {
        object script = Custody(2, "Bolingbroke");
        var world = new InventoryWorld { Selected = unchecked((int)0xF00DBAAD) };
        world.Install();
        Call(script, "EnforceJusticeCustodyWeaponLock", Game.Player.Character);
        Assert.AreEqual(unchecked((int)WeaponHash.Unarmed), world.Selected);
        Call(script, "PrepareJusticeInventoryConfiscation", Game.Player.Character);
        Assert.AreEqual("validation", Get<string>(script, "_justiceInventoryCaptureStage"));
        Assert.IsNull(Get<object>(script, "_justiceWeaponSnapshot"));
        Assert.AreEqual(0, world.Removals);
        Assert.AreEqual(unchecked((int)0xF00DBAAD), Get<int>(script, "_justiceReleaseSelectedWeaponHash"));
        Call(script, "ResetJusticeCustodyInventorySelection");
        Call(script, "EnforceJusticeCustodyWeaponLock", Game.Player.Character);
        Call(script, "PrepareJusticeInventoryConfiscation", Game.Player.Character);
        Assert.IsNull(Get<object>(script, "_justiceWeaponSnapshot"), "Le cache réarmé reprend la sélection persistée de ce propriétaire.");
        Assert.AreEqual(0, world.Removals);
    }

    [TestMethod]
    public void PreservedCustody_RetriesCaptureAfterFiveSecondsAndWaitsForDurabilityBeforeRemoval()
    {
        object script = Custody(0, "MissionRow");
        var world = new InventoryWorld { Fault = "clip" };
        world.Install();
        object first = Call(script, "PrepareJusticeInventoryConfiscation", Game.Player.Character);
        Call(script, "EnterJusticeNonDestructiveCustodyFallback", Game.Player.Character, 0);
        Assert.AreEqual("RetryableFailure", first.ToString());
        int reads = world.ClipReads;
        world.Fault = null;
        for (int now = 1; now < 5000; now += 100)
            Call(script, "RetryJusticeInventoryConfiscationIfDue", Game.Player.Character, now);
        Assert.AreEqual(reads, world.ClipReads);
        Call(script, "InitializeJusticePersistenceServices");
        Game.GameTime = 5000;
        Assert.AreEqual("RetryableFailure", Call(script, "RetryJusticeInventoryConfiscationIfDue", Game.Player.Character, 5000).ToString());
        Assert.AreEqual("SnapshotPersisted", Get<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.AreEqual(0, world.Removals, "Un dépôt seulement enfilé en mémoire ne permet aucun retrait.");
        for (int attempt = 0; attempt < 12 && world.Removals == 0; attempt++)
        {
            WaitForWriter(script);
            Game.GameTime += 250;
            Call(script, "RetryJusticeInventoryConfiscationIfDue", Game.Player.Character, Game.GameTime);
        }
        Assert.AreEqual(1, world.Removals);
        Assert.AreEqual("RemovedVerified", Get<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.AreEqual(JusticePhase.Incarcerated, Get<JusticeCaseState>(script, "_justiceCaseState").Phase);
        Assert.AreEqual(600, Get<JusticeCaseState>(script, "_justiceCaseState").SentenceSeconds);
        Assert.IsFalse(StubRuntime.NativeCalls.Any(call => call.Hash == (ulong)Hash.DO_SCREEN_FADE_OUT || call.Hash == (ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [DataTestMethod]
    [DataRow("CapturePending")]
    [DataRow("UnsupportedPreserved")]
    public void RuntimeWeaponLock_IsDerivedFromCustodyAndCannotBeClearedByOrphanRepair(string state)
    {
        object script = Custody(1, "MissionRow");
        var world = new InventoryWorld();
        world.Install();
        SetEnum(script, "_justiceInventoryCustodyState", state);
        var disabled = new HashSet<Control>();
        StubRuntime.ControlDisabledHandler = (index, control) => disabled.Add(control);
        Call(script, "RepairJusticeOrphanedCustodyControls", Game.Player.Character);
        Call(script, "EnforceJusticeCustodyWeaponLock", Game.Player.Character);
        Assert.AreEqual(unchecked((int)WeaponHash.Unarmed), world.Selected);
        Assert.IsTrue(disabled.Contains(Control.SelectWeapon));
        Assert.IsFalse(Get<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.IsTrue((bool)Call(script, "ValidateJusticeInventoryCustodyStateInvariant"));
        int selections = world.Selections;
        Set(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        Call(script, "EnforceJusticeCustodyWeaponLock", new Ped(55) { Model = new Model("player_zero") });
        Assert.AreEqual(selections, world.Selections);
        Get<JusticeCaseState>(script, "_justiceCaseState").Phase = JusticePhase.AtLarge;
        Set(script, "_justiceCustodyRuntimeActive", false);
        Call(script, "EnforceJusticeCustodyWeaponLock", Game.Player.Character);
        Assert.AreEqual(selections, world.Selections);
    }

    [TestMethod]
    public void ReacquiredWeapon_RefusedRemovalPreservesDepositAndDropsTheVerifiedState()
    {
        object script = Custody(2, "Bolingbroke");
        var world = new InventoryWorld();
        world.Install();
        object snapshot = Capture(script);
        Set(script, "_justiceWeaponSnapshot", snapshot);
        Assert.AreEqual("RemovedVerified", Call(script, "RemoveJusticePlayerWeaponsSafe", Game.Player.Character).ToString());
        SetEnum(script, "_justiceInventoryCustodyState", "RemovedVerified");
        Set(script, "_justiceInventoryRemoved", true);
        world.Owned.Add((int)WeaponHash.Pistol);
        world.RefuseRemoval = true;
        Call(script, "MaintainJusticeCustodyPersonalEffects", Game.Player.Character, 1000);
        Assert.AreSame(snapshot, Get<object>(script, "_justiceWeaponSnapshot"));
        Assert.IsFalse(Get<bool>(script, "_justiceInventoryRemoved"));
        Assert.AreEqual("RestoreAmbiguous", Get<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsTrue((bool)Call(script, "ShouldEnforceJusticeCustodyWeaponLock"));
        Call(script, "RetryJusticeDeferredInventoryRestore", Game.Player.Character, 100000);
        Assert.AreEqual(0, world.Gives);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeferredProjectile_GetsItsStockUnderAmmoWalWithoutReplayingAfterConsumption(bool loseNativeAcknowledgement)
    {
        object script = Custody(0, "Bolingbroke");
        var world = new InventoryWorld();
        world.Install();
        world.Owned.Clear();
        world.Owned.Add(unchecked((int)WeaponHash.Grenade));
        world.Selected = unchecked((int)WeaponHash.Grenade);
        world.Pools[42] = 0;
        world.Pools[44] = 0;
        object snapshot = Capture(script);
        string restoreId = Guid.NewGuid().ToString("N");
        snapshot.GetType().GetField("RestoreId", Instance).SetValue(snapshot, restoreId);
        Set(script, "_justiceWeaponSnapshot", snapshot);
        Assert.AreEqual("RemovedVerified", Call(script, "RemoveJusticePlayerWeaponsSafe", Game.Player.Character).ToString());
        Set(script, "_justiceDeferredInventoryRestore", true);
        SetEnum(script, "_justiceInventoryCustodyState", "RestorePending");
        // Je simule ici un dépôt dont la barrière initiale a déjà été confirmée.
        // Le test de recapture précédent vérifie séparément cette durabilité.
        Set(script, "_justiceInventoryPreparedRestoreId", restoreId);
        Set(script, "_justiceInventoryPreparedRestoreSlot", 0);
        Call(script, "InitializeJusticePersistenceServices");
        world.ThrowAfterProjectileGive = loseNativeAcknowledgement;
        bool result = (bool)Call(script, "RestoreJusticeDeferredWeapons", Game.Player.Character);
        Assert.AreEqual(!loseNativeAcknowledgement, result);
        Assert.AreEqual(7, world.Pools[43]);
        Assert.IsTrue(world.Owned.Contains(unchecked((int)WeaponHash.Grenade)));
        Assert.AreEqual(1, world.Gives);
        Assert.IsTrue(Get<JusticeWriteAheadLog>(script, "_justiceWriteAheadLog").GetOpenTransactions().Any(record =>
            record.OperationKind == "InventoryRestoreResult" && record.Fields.Any(field => field.Path == "ammoTypeHash" && field.Value == "43")));
        var progress = (JusticeInventoryPersistenceSnapshot)Call(script, "CaptureJusticeInventoryPersistenceSnapshot");
        Assert.IsTrue(progress.AmmoPools.Single(pool => pool.TypeHash == 43).RestoreAttempted);
        world.Pools[43] = 0;
        world.Owned.Clear();
        world.ThrowAfterProjectileGive = false;
        Set(script, "_justiceWeaponSnapshot", typeof(DonJEnemySpawner).GetMethod("RestoreJusticeInventorySnapshot", Static).Invoke(null, new object[] { progress }));
        Call(script, "RestoreJusticeDeferredWeapons", Game.Player.Character);
        Assert.AreEqual(1, world.Gives, "Le projectile consommé ne doit jamais être redonné après perte d'ACK ou reprise.");
        Assert.AreEqual(0, world.Pools[43]);
    }

    private object Custody(int slot, string site)
    {
        object script = typeof(JusticeVoluntaryPaymentTests).GetMethod("CreatePaymentScript", Static).Invoke(null, new object[] { 0L });
        foreach (FieldInfo field in typeof(DonJEnemySpawner).GetFields(Instance))
            if (field.GetValue(script) == null && field.FieldType.IsGenericType && field.FieldType.Namespace == "System.Collections.Generic")
                field.SetValue(script, Activator.CreateInstance(field.FieldType));
        _scripts.Add(script);
        var player = new Ped(900 + slot) { Model = new Model(new[] { "player_zero", "player_one", "player_two" }[slot]) };
        Game.Player.Character = player;
        Set(script, "_justiceActivePlayerProfileSlot", slot);
        Set(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => slot));
        Set(script, "_justiceCustodyPlayerSlot", slot);
        Set(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        Set(script, "_justiceCustodyPlayerHandle", player.Handle);
        Set(script, "_justiceCustodyPlayerStateStored", true);
        Set(script, "_justiceCustodyStoredCanRagdoll", true);
        Set(script, "_justiceCustodyRuntimeActive", true);
        Set(script, "_justiceCustodyInitialSentenceSeconds", 600);
        SetEnum(script, "_justiceCustodySite", site);
        JusticeCaseState state = Get<JusticeCaseState>(script, "_justiceCaseState");
        state.Phase = JusticePhase.Incarcerated;
        state.CustodyEpisodeId = "custody:inventory-regression";
        state.SentenceSeconds = 600;
        state.Charges[0].IsAdjudicated = true;
        state.Charges[0].SentenceSeconds = 600;
        return script;
    }

    private static object Capture(object script)
    {
        object[] args = { Game.Player.Character, null };
        Assert.IsTrue((bool)Call(script, "TryCaptureJusticeWeaponSnapshot", args));
        return args[1];
    }

    private static void WaitForWriter(object script)
    {
        JusticeRepository repository = Get<JusticeRepository>(script, "_justiceRepository");
        long revision = Get<long>(script, "_justiceLastQueuedPersistenceRevision");
        if (repository != null && revision > 0)
            Assert.IsTrue(repository.Flush(revision, TimeSpan.FromSeconds(10)), repository.GetDiagnostics().LastError);
    }

    private sealed class InventoryWorld
    {
        internal readonly int[] InitialWeapons = { (int)WeaponHash.Pistol, unchecked((int)WeaponHash.Knife), unchecked((int)WeaponHash.Grenade), (int)WeaponHash.PetrolCan };
        internal readonly HashSet<int> Owned = new HashSet<int>();
        internal readonly Dictionary<int, int> Pools = new Dictionary<int, int> { [42] = 180, [43] = 7, [44] = 150 };
        internal readonly Dictionary<int, int> Clips = new Dictionary<int, int> { [(int)WeaponHash.Pistol] = 12 };
        private readonly HashSet<int> _components = new HashSet<int> { unchecked((int)WeaponComponent.AtPiSupp) };
        internal int Selected = (int)WeaponHash.Pistol, Removals, Gives, ClipReads, Selections, UnsupportedClipWrites;
        internal string Fault;
        internal int? GroupOverride;
        internal bool RefuseRemoval, ThrowAfterProjectileGive;

        internal InventoryWorld() { foreach (int weapon in InitialWeapons) Owned.Add(weapon); }
        internal void Install()
        {
            StubRuntime.NativeCallHandler = Handle;
            StubRuntime.WeaponComponentsHandler = weapon => Fault == "components" ? null :
                weapon == WeaponHash.Pistol ? new[] { WeaponComponent.AtPiSupp } : new WeaponComponent[0];
        }
        private int Type(int weapon) => weapon == (int)WeaponHash.Pistol ? 42 : weapon == unchecked((int)WeaponHash.Grenade) ? 43 : weapon == (int)WeaponHash.PetrolCan ? 44 : 0;
        private static int Int(object argument) => unchecked((int)Convert.ToInt64(
            argument.GetType().GetProperty("Value", Instance).GetValue(argument)));
        private object Handle(ulong hash, object[] args)
        {
            int weapon = args.Length > 1 ? Int(args[1]) : 0;
            if (hash == 0xEE47635F352DA367UL) return Fault == "dlc" ? 513 : 0;
            if (hash == 0x0A6DB4965674D243UL) return Selected;
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON) return Owned.Contains(weapon);
            if (hash == (ulong)Hash.GET_AMMO_IN_PED_WEAPON) return Type(weapon) == 0 ? 0 : Pools[Type(weapon)];
            if (hash == (ulong)Hash.GET_PED_WEAPON_TINT_INDEX) return 0;
            if (hash == (ulong)Hash.GET_AMMO_IN_CLIP)
            {
                ClipReads++;
                if (weapon != (int)WeaponHash.Pistol || Fault == "clip") return false;
                typeof(OutputArgument).GetMethod("SetResult", Instance).MakeGenericMethod(typeof(int)).Invoke(args[2], new object[] { Clips[weapon] });
                return true;
            }
            if (hash == 0xC3287EE3050FB74CUL)
            {
                weapon = Int(args[0]);
                if (GroupOverride.HasValue) return GroupOverride.Value;
                return weapon == (int)WeaponHash.Pistol ? 416676503 : weapon == unchecked((int)WeaponHash.Knife) ? unchecked((int)3566412244U) :
                    weapon == unchecked((int)WeaponHash.Grenade) ? 1548507267 : weapon == (int)WeaponHash.PetrolCan ? 1595662460 : 0;
            }
            if (hash == 0x583BE370B1EC6EB4UL) return Int(args[0]) == (int)WeaponHash.Pistol ? 12 : 0;
            if (hash == 0xA38DCFFCEA8962FAUL) return weapon == (int)WeaponHash.Pistol ? 12 : 0;
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON_COMPONENT) return _components.Contains(Int(args[2]));
            if (hash == (ulong)Hash.GIVE_WEAPON_COMPONENT_TO_PED) { _components.Add(Int(args[2])); return null; }
            if (hash == 0x7FEAD38B326B9F74UL) return Type(weapon);
            if (hash == 0x39D22031557946C1UL) return Pools[weapon];
            if (hash == 0x5FD1E1F011E76D7EUL) { Pools[weapon] = Int(args[2]); return null; }
            if (hash == 0x14E56BC5B5DB6A19UL) { if (Type(weapon) != 0) Pools[Type(weapon)] = Int(args[2]); return null; }
            if (hash == (ulong)Hash.SET_AMMO_IN_CLIP)
            {
                if (!Clips.ContainsKey(weapon)) UnsupportedClipWrites++;
                else Clips[weapon] = Int(args[2]);
                return null;
            }
            if (hash == (ulong)Hash.REMOVE_ALL_PED_WEAPONS)
            {
                Removals++;
                if (!RefuseRemoval) { Owned.Clear(); _components.Clear(); }
                return null;
            }
            if (hash == (ulong)Hash.SET_CURRENT_PED_WEAPON) { Selected = weapon; Selections++; return null; }
            if (hash == 0xBF0FD6E56C964FCBUL)
            {
                Gives++;
                if (Type(weapon) != 0) Pools[Type(weapon)] += Int(args[2]);
                if (weapon != unchecked((int)WeaponHash.Grenade) || Pools[43] > 0) Owned.Add(weapon);
                if (weapon == unchecked((int)WeaponHash.Grenade) && ThrowAfterProjectileGive)
                    throw new InvalidOperationException("Injected projectile ACK loss after stock return");
                return null;
            }
            return null;
        }
    }

    private static T Get<T>(object target, string field) => (T)typeof(DonJEnemySpawner).GetField(field, Instance).GetValue(target);
    private static void Set(object target, string field, object value) => typeof(DonJEnemySpawner).GetField(field, Instance).SetValue(target, value);
    private static void SetEnum(object target, string field, string value) => Set(target, field, Enum.Parse(typeof(DonJEnemySpawner).GetField(field, Instance).FieldType, value));
    private static object Call(object target, string name, params object[] args) => typeof(DonJEnemySpawner).GetMethods(Instance).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
#endif
}
