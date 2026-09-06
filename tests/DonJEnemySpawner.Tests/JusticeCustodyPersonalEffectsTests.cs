using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyPersonalEffectsTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private string _directory, _oldDirectory;
    private readonly List<object> _scripts = new List<object>();

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DonJPersonalEffects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _oldDirectory = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _directory);
#if DONJ_STUB_API
        StubRuntime.Reset();
#endif
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (object script in _scripts) Call(script, "ShutdownJusticePersistenceServices");
        Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", _oldDirectory);
#if DONJ_STUB_API
        StubRuntime.Reset();
#endif
        Directory.Delete(_directory, true);
    }

    [DataTestMethod]
    [DataRow(0, 12, 11, 3, 1, 0)]
    [DataRow(1, 1, 1, 3, 1, 14)]
    [DataRow(2, 5, 5, 2, 5, 14)]
    public void Uniform_UsesSoloNavyBoilerComponentsAndPreservesFace(int slot, int top, int legs, int texture, int shoes, int undershirt)
    {
        JusticeClothingItem[] items = (JusticeClothingItem[])CallStatic("GetJusticeCustodyUniform", slot);
        Assert.AreEqual(18, items.Length);
        Assert.AreEqual(top, items.Single(i => !i.Prop && i.Slot == 3).Drawable);
        Assert.AreEqual(legs, items.Single(i => !i.Prop && i.Slot == 4).Drawable);
        Assert.AreEqual(texture, items[0].Texture);
        Assert.AreEqual(shoes, items.Single(i => !i.Prop && i.Slot == 6).Drawable);
        Assert.AreEqual(undershirt, items.Single(i => !i.Prop && i.Slot == 8).Drawable);
        Assert.IsFalse(items.Any(i => !i.Prop && i.Slot < 3));
        Assert.IsNull(CallStatic("GetJusticeCustodyUniform", -1));
        Assert.IsNull(CallStatic("GetJusticeCustodyUniform", 3));
    }

    [TestMethod]
    public void AppearanceXml_RoundTripsAndRejectsMissingDuplicateOrInvalidEntries()
    {
        var items = (JusticeClothingItem[])CallStatic("GetJusticeCustodyUniform", 2);
        var snapshot = new JusticeAppearancePersistenceSnapshot("custody:test", 2, -1686040670, items, true);
        string xml = AppearanceXml(snapshot);
        object[] arguments = { Element(xml), null };
        Assert.IsTrue((bool)CallStatic("TryReadJusticeAppearanceXml", arguments));
        var restored = (JusticeAppearancePersistenceSnapshot)arguments[1];
        Assert.AreEqual(snapshot.EpisodeId, restored.EpisodeId);
        Assert.AreEqual(snapshot.ModelHash, restored.ModelHash);
        Assert.IsTrue(restored.RestorePending);
        Assert.AreEqual(18, restored.Items.Count);
        foreach (string invalid in new[] { xml.Replace("palette=\"0\"", "palette=\"4\""),
            xml.Replace("slot=\"3\"", "slot=\"2\""), xml.Replace("playerSlot=\"2\"", "playerSlot=\"-1\""),
            xml.Replace("</AppearanceSnapshot>", "<Prop slot=\"8\" drawable=\"-1\" texture=\"0\" palette=\"0\" /></AppearanceSnapshot>") })
            Assert.IsFalse((bool)CallStatic("TryReadJusticeAppearanceXml", Element(invalid), null));
        Assert.IsTrue((bool)CallStatic("TryReadJusticeAppearanceXml", Element("<Custody />"), null));
        items[0] = null;
        Assert.IsNotNull(snapshot.Items[0], "Le DTO doit copier la collection mutable.");
    }

    [TestMethod]
    public void AmmoXml_RoundTripsSharedAndSpecialPoolsWithProgressAndRejectsDuplicates()
    {
        var inventory = Inventory();
        object script = Script();
        Set(script, "_justiceWeaponSnapshot", CallStatic("RestoreJusticeInventorySnapshot", inventory));
        var copy = (JusticeInventoryPersistenceSnapshot)Call(script, "CaptureJusticeInventoryPersistenceSnapshot");
        Assert.AreEqual(2, copy.AmmoPools.Count);
        Assert.AreEqual(42, copy.Weapons[1].AmmoTypeHash);
        Assert.AreEqual(180, copy.AmmoPools[0].Ammo);
        var text = new StringBuilder();
        using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        { writer.WriteStartElement("Custody"); CallStatic("WriteJusticeInventoryPersistenceXml", writer, copy); writer.WriteEndElement(); }
        object parsed = CallStatic("ReadJusticeWeaponSnapshotXml", Element(text.ToString()));
        Assert.IsNotNull(parsed);
        Assert.IsTrue((bool)CallStatic("ValidateJusticeWeaponSnapshot", parsed));
        Assert.IsNull(CallStatic("ReadJusticeWeaponSnapshotXml", Element(text.ToString().Replace("type=\"43\"", "type=\"42\""))));
        Assert.IsNull(CallStatic("ReadJusticeWeaponSnapshotXml", Element(text.ToString().Replace("ammoType=\"42\"", "ammoType=\"99\""))));
    }

#if DONJ_STUB_API
    [DataTestMethod]
    [DataRow(0, "MissionRow")] [DataRow(0, "Bolingbroke")]
    [DataRow(1, "MissionRow")] [DataRow(1, "Bolingbroke")]
    [DataRow(2, "MissionRow")] [DataRow(2, "Bolingbroke")]
    public void Custody_OutfitFiveMinutesReloadAndExactRelease(int slot, string site)
    {
        object script = CustodyScript(slot, site);
        Ped player = Game.Player.Character;
        var clothing = new ClothingWorld();
        StubRuntime.NativeCallHandler = clothing.Handle;
        Call(script, "PrepareJusticeCustodyAppearance", player);
        var original = Get<JusticeAppearancePersistenceSnapshot>(script, "_justiceCustodyAppearance");
        Assert.IsNotNull(original);
        Call(script, "ApplyJusticeCustodyAppearance", player, 0);
        Assert.AreEqual(0, clothing.Writes, "Aucune tenue avant le précommit durable.");
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        Call(script, "ApplyJusticeCustodyAppearance", player, 1);
        int writes = clothing.Writes;
        Assert.IsTrue(writes > 0);
        Set(script, "_justiceCustodyTransferPending", false);
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", false);
        for (int now = 2; now < 300000; now += 100)
            Call(script, "ApplyJusticeCustodyAppearance", player, now);
        Assert.AreEqual(writes, clothing.Writes, "Je ne réécris pas les mêmes composants à chaque tick.");
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Assert.AreSame(original, Get<JusticeAppearancePersistenceSnapshot>(script, "_justiceCustodyAppearance"));
        var saved = (JusticeCustodyPersistenceSnapshot)Call(script, "CaptureJusticeCustodyPersistenceSnapshot", 300000);
        Assert.AreSame(original, saved.AppearanceSnapshot);
        Assert.IsTrue((bool)Call(script, "RestoreJusticeCustodyAppearance", player, true));
        Assert.AreSame(original, Get<JusticeAppearancePersistenceSnapshot>(script, "_justiceCustodyAppearance"));
        Call(script, "ApplyJusticeCustodyAppearance", player, 301001);
        Assert.IsTrue((bool)Call(script, "RestoreJusticeCustodyAppearance", player, false));
        Assert.IsNull(Get<object>(script, "_justiceCustodyAppearance"));
        Assert.IsTrue(clothing.IsOriginal);
        Assert.IsFalse(StubRuntime.NativeCalls.Any(c => c.Hash == (ulong)Hash.DO_SCREEN_FADE_OUT || c.Hash == (ulong)Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(new Model(new[] { "player_zero", "player_one", "player_two" }[slot]).Hash, player.Model.Hash);
    }

    [TestMethod]
    public void Outfit_FaultRollsBackAndWrongHeroNeverReceivesTheSnapshot()
    {
        object script = CustodyScript(2, "Bolingbroke");
        var clothing = new ClothingWorld();
        StubRuntime.NativeCallHandler = clothing.Handle;
        Ped player = Game.Player.Character;
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        clothing.FailOnceSlot = 4;
        Call(script, "ApplyJusticeCustodyAppearance", player, 1);
        Assert.IsTrue(clothing.IsOriginal);
        int writes = clothing.Writes;
        Call(script, "ApplyJusticeCustodyAppearance", player, 10001);
        Assert.AreEqual(writes, clothing.Writes);
        Set(script, "_justiceActivePlayerProfileSlot", 0);
        Assert.IsFalse((bool)Call(script, "RestoreJusticeCustodyAppearance", player, false));
        Assert.IsNotNull(Get<object>(script, "_justiceCustodyAppearance"));
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public void Outfit_TypedReloadAndPendingReleaseXmlKeepOriginalOwner(int slot)
    {
        object script = CustodyScript(slot, "Bolingbroke");
        var clothing = new ClothingWorld();
        StubRuntime.NativeCallHandler = clothing.Handle;
        Ped player = Game.Player.Character;
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        Call(script, "ApplyJusticeCustodyAppearance", player, 1);
        var snapshot = (JusticeCustodyPersistenceSnapshot)Call(script, "CaptureJusticeCustodyPersistenceSnapshot", 2);
        Call(script, "ResetJusticeCustodyPersistentFields", false, false);
        Assert.IsTrue((bool)Call(script, "RestoreJusticeCustodyPersistenceSnapshot", snapshot));
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Assert.AreSame(snapshot.AppearanceSnapshot, Get<object>(script, "_justiceCustodyAppearance"));
        Call(script, "ResetJusticeCustodyPersistentFields", true, false);
        Get<JusticeCaseState>(script, "_justiceCaseState").Phase = JusticePhase.AtLarge;
        var pending = (JusticeCustodyPersistenceSnapshot)Call(script, "CaptureJusticeCustodyPersistenceSnapshot", 3);
        string xml = "<Root>" + DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(pending) + "</Root>";
        Assert.IsTrue((bool)CallStatic("IsJusticeCustodyXmlSemanticallyValid", Element(xml),
            Get<JusticeCaseState>(script, "_justiceCaseState"), Get<JusticeRecordState>(script, "_justiceRecordState")), xml);
        Assert.AreEqual(slot, pending.AppearanceSnapshot.PlayerSlot);
        Assert.IsTrue(pending.AppearanceSnapshot.RestorePending);
        Call(script, "RetryJusticeCustodyAppearanceRestore", player, 5000);
        Assert.IsNull(Get<object>(script, "_justiceCustodyAppearance"));
        Assert.IsTrue(clothing.IsOriginal);
        Assert.AreEqual(-1, Get<int>(script, "_justiceCustodyPlayerSlot"));
        Assert.AreEqual(0, Get<int>(script, "_justiceCustodyPlayerModelHash"));
    }

    [TestMethod]
    public void Outfit_UnavailableComponentOrReadFailureNeverChangesClothesOrStartsFade()
    {
        object script = CustodyScript(2, "Bolingbroke");
        Ped player = Game.Player.Character;
        StubRuntime.NativeCallHandler = (hash, args) => hash == 0xE825F6B6CEA7671DUL ? (object)false : null;
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Assert.IsNull(Get<object>(script, "_justiceCustodyAppearance"));
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        Call(script, "ApplyJusticeCustodyAppearance", player, 1000);
        Assert.IsFalse(StubRuntime.NativeCalls.Any(c => c.Hash == 0x262B14F48D29DE80UL || c.Hash == (ulong)Hash.DO_SCREEN_FADE_OUT));
    }

    [TestMethod]
    public void Outfit_DeferredRestoreSurvivesResetAndProtectsNewClothing()
    {
        object script = CustodyScript(0, "MissionRow");
        var clothing = new ClothingWorld();
        StubRuntime.NativeCallHandler = clothing.Handle;
        Ped player = Game.Player.Character;
        Call(script, "PrepareJusticeCustodyAppearance", player);
        Set(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        Call(script, "ApplyJusticeCustodyAppearance", player, 1);
        Call(script, "ResetJusticeCustodyPersistentFields", true, false);
        Assert.IsTrue(Get<JusticeAppearancePersistenceSnapshot>(script, "_justiceCustodyAppearance").RestorePending);
        clothing.Components[3] = new[] { 100, 1, 0 };
        Assert.IsTrue((bool)Call(script, "RestoreJusticeCustodyAppearance", player, false));
        Assert.AreEqual(100, clothing.Components[3][0]);
        Assert.AreEqual(2, clothing.Components[4][0]);
    }

    [TestMethod]
    public void Ammo_ConfiscationClearsPoolsAndEmptyCustodyDoesNotRepeatRemoval()
    {
        object script = CustodyScript(0, "Bolingbroke");
        Ped player = Game.Player.Character;
        var world = new AmmoWorld();
        StubRuntime.NativeCallHandler = world.Handle;
        Set(script, "_justiceWeaponSnapshot", CallStatic("RestoreJusticeInventorySnapshot", Inventory()));
        Assert.AreEqual("RemovedVerified", Call(script, "RemoveJusticePlayerWeaponsSafe", player).ToString());
        Assert.IsTrue(world.Pools.Values.All(v => v == 0));
        Assert.AreEqual(0, world.Owned.Count);
        Set(script, "_justiceInventoryRemoved", true);
        SetEnum(script, "_justiceInventoryCustodyState", "RemovedVerified");
        int removals = world.Removals;
        for (int now = 1; now < 300000; now += 100)
            Call(script, "MaintainJusticeCustodyPersonalEffects", player, now);
        Assert.AreEqual(removals, world.Removals);
        world.Owned.Add((int)WeaponHash.Pistol);
        world.Pools[42] = 50;
        Call(script, "MaintainJusticeCustodyPersonalEffects", player, 301001);
        Assert.AreEqual(removals + 1, world.Removals);
        Assert.AreEqual(0, world.Pools[42]);
        Assert.AreEqual(180, ((JusticeInventoryPersistenceSnapshot)Call(script, "CaptureJusticeInventoryPersistenceSnapshot")).AmmoPools[0].Ammo);
        Assert.IsTrue((bool)Call(script, "RestoreJusticeAmmoPoolsExact", player));
        Assert.AreEqual(180, world.Pools[42]);
        Assert.AreEqual(12, world.Pools[43]);
    }

    [TestMethod]
    public void Ammo_DeferredSharedPoolIsReturnedOnlyOnceIncludingAmbiguousNative()
    {
        object script = Script();
        var inventory = Inventory(true);
        Set(script, "_justiceWeaponSnapshot", CallStatic("RestoreJusticeInventorySnapshot", inventory));
        Set(script, "_justiceDeferredInventoryRestore", true);
        Set(script, "_justiceCustodyPlayerSlot", 0);
        Set(script, "_justiceCustodyPlayerModelHash", 111);
        SetEnum(script, "_justiceInventoryCustodyState", "RestorePending");
        Call(script, "InitializeJusticePersistenceServices");
        var world = new AmmoWorld();
        world.Pools[42] = 0; world.Pools[43] = 0;
        StubRuntime.NativeCallHandler = world.Handle;
        Ped player = new Ped(900) { Model = new Model(111) };
        world.ThrowAfterPoolWrite = true;
        Assert.IsFalse((bool)Call(script, "RestoreJusticeDeferredAmmo", player));
        int writes = world.PoolWrites;
        Assert.AreEqual(2, writes);
        world.Pools[42] = 4; world.Pools[43] = 0;
        world.ThrowAfterPoolWrite = false;
        Assert.IsTrue((bool)Call(script, "RestoreJusticeDeferredAmmo", player));
        Assert.AreEqual(writes, world.PoolWrites);
        Assert.AreEqual(4, world.Pools[42]);
        Assert.AreEqual(0, world.Pools[43]);
        var progress = (JusticeInventoryPersistenceSnapshot)Call(script, "CaptureJusticeInventoryPersistenceSnapshot");
        Assert.IsTrue(progress.AmmoPools.All(p => p.RestoreAttempted && p.RestoreCompleted));
        Set(script, "_justiceWeaponSnapshot", CallStatic("RestoreJusticeInventorySnapshot", progress));
        Call(script, "RestoreJusticeDeferredAmmo", player);
        Assert.AreEqual(writes, world.PoolWrites);
    }

    [TestMethod]
    public void Ammo_UnreturnedPoolPreventsCommitEvenWhenAllWeaponsAreComplete()
    {
        object script = Script();
        Set(script, "_justiceWeaponSnapshot", CallStatic("RestoreJusticeInventorySnapshot", Inventory(true)));
        Assert.IsFalse((bool)Call(script, "CommitJusticeDeferredInventoryRestore"));
        Assert.IsNotNull(Get<object>(script, "_justiceWeaponSnapshot"));
    }

    private object CustodyScript(int slot, string site)
    {
        object script = Script();
        var player = new Ped(900) { Model = new Model(new[] { "player_zero", "player_one", "player_two" }[slot]) };
        Game.Player.Character = player;
        Set(script, "_justiceActivePlayerProfileSlot", slot);
        Set(script, "_justiceCanonicalPlayerSlotOverride", new Func<int>(() => slot));
        Set(script, "_justiceCustodyPlayerSlot", slot);
        Set(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        Set(script, "_justiceCustodyPlayerHandle", player.Handle);
        Set(script, "_justiceCustodyPlayerStateStored", true);
        Set(script, "_justiceCustodyRuntimeActive", true);
        Set(script, "_justiceCustodyTransferPending", true);
        SetEnum(script, "_justiceCustodySite", site);
        var state = Get<JusticeCaseState>(script, "_justiceCaseState");
        state.Phase = JusticePhase.Incarcerated;
        state.CustodyEpisodeId = "custody:test";
        state.SentenceSeconds = 600;
        return script;
    }

    private sealed class ClothingWorld
    {
        internal readonly Dictionary<int, int[]> Components = Enumerable.Range(3, 9).ToDictionary(i => i, i => new[] { 2, 1, 2 });
        private readonly Dictionary<int, int[]> _props = Enumerable.Range(0, 9).ToDictionary(i => i, i => new[] { 1, 1, 0 });
        internal int Writes, FailOnceSlot = -1;
        internal bool IsOriginal => Components.Values.All(v => v.SequenceEqual(new[] { 2, 1, 2 })) && _props.Values.All(v => v[0] == 1 && v[1] == 1);
        internal object Handle(ulong hash, object[] args)
        {
            if (hash == 0xE825F6B6CEA7671DUL) return true;
            if (args.Length < 2) return null;
            int slot = NativeInt(args[1]);
            if (hash == 0x67F3780DD425D4FCUL) return Components[slot][0];
            if (hash == 0x04A355E041E004E6UL) return Components[slot][1];
            if (hash == 0xE3DD5F2A84B42281UL) return Components[slot][2];
            if (hash == 0x898CC20EA75BACD8UL) return _props[slot][0];
            if (hash == 0xE131A28626F81AB2UL) return _props[slot][1];
            if (hash == 0x262B14F48D29DE80UL)
            {
                if (slot == FailOnceSlot) { FailOnceSlot = -1; throw new InvalidOperationException("Injected clothing failure"); }
                Components[slot] = new[] { NativeInt(args[2]), NativeInt(args[3]), NativeInt(args[4]) }; Writes++;
            }
            if (hash == 0x0943E5B8E078E76EUL) { _props[slot] = new[] { -1, 0, 0 }; Writes++; }
            if (hash == 0x93376B65A266EB5FUL) { _props[slot] = new[] { NativeInt(args[2]), NativeInt(args[3]), 0 }; Writes++; }
            return null;
        }
    }

    private sealed class AmmoWorld
    {
        internal readonly Dictionary<int, int> Pools = new Dictionary<int, int> { [42] = 180, [43] = 12 };
        internal readonly HashSet<int> Owned = new HashSet<int> { (int)WeaponHash.Pistol, (int)WeaponHash.MicroSMG, unchecked((int)WeaponHash.CarbineRifle) };
        internal int Removals, PoolWrites;
        internal bool ThrowAfterPoolWrite;
        internal object Handle(ulong hash, object[] args)
        {
            if (hash == (ulong)Hash.REMOVE_ALL_PED_WEAPONS) { Owned.Clear(); Removals++; }
            if (args.Length < 2) return null;
            int key = NativeInt(args[1]);
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON) return Owned.Contains(key);
            if (hash == 0x7FEAD38B326B9F74UL) return key == (int)WeaponHash.Pistol || key == (int)WeaponHash.MicroSMG ? 42 : key == unchecked((int)WeaponHash.CarbineRifle) ? 43 : 0;
            if (hash == 0x39D22031557946C1UL) return Pools[key];
            if (hash == 0x5FD1E1F011E76D7EUL)
            {
                Pools[key] = NativeInt(args[2]); PoolWrites++;
                if (ThrowAfterPoolWrite) throw new InvalidOperationException("Injected ammo ACK loss");
            }
            return null;
        }
    }
    private static int NativeInt(object argument) => Convert.ToInt32(argument.GetType().GetProperty("Value", Instance).GetValue(argument));
#endif

    private static JusticeInventoryPersistenceSnapshot Inventory(bool completedWeapons = false) =>
        new JusticeInventoryPersistenceSnapshot(true, 453432689,
            new[] { new JusticeWeaponPersistenceSnapshot(453432689, 180, 12, 0, new int[0], completedWeapons, completedWeapons, 42),
                new JusticeWeaponPersistenceSnapshot(324215364, 180, 12, 0, new int[0], completedWeapons, completedWeapons, 42),
                new JusticeWeaponPersistenceSnapshot(-2084633992, 12, 6, 0, new int[0], completedWeapons, completedWeapons, 43) },
            Guid.NewGuid().ToString("N"), new[] { new JusticeAmmoPersistenceSnapshot(42, 180), new JusticeAmmoPersistenceSnapshot(43, 12) });

    private object Script()
    {
        object script = typeof(JusticeVoluntaryPaymentTests).GetMethod("CreatePaymentScript", Static).Invoke(null, new object[] { 0L });
        foreach (FieldInfo field in script.GetType().GetFields(Instance))
            if (field.GetValue(script) == null && field.FieldType.IsGenericType && field.FieldType.Namespace == "System.Collections.Generic")
                field.SetValue(script, Activator.CreateInstance(field.FieldType));
        _scripts.Add(script);
        return script;
    }
    private static string AppearanceXml(JusticeAppearancePersistenceSnapshot snapshot)
    {
        var text = new StringBuilder();
        using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        { writer.WriteStartElement("Custody"); CallStatic("WriteJusticeAppearanceXml", writer, snapshot); writer.WriteEndElement(); }
        return text.ToString();
    }
    private static XmlElement Element(string xml) { var doc = new XmlDocument(); doc.LoadXml(xml); return doc.DocumentElement; }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Instance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Instance).SetValue(target, value);
    private static void SetEnum(object target, string field, string value) => Set(target, field, Enum.Parse(target.GetType().GetField(field, Instance).FieldType, value));
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(Instance).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    private static object CallStatic(string name, params object[] args) => typeof(DonJEnemySpawner).GetMethod(name, Static).Invoke(null, args);
}
