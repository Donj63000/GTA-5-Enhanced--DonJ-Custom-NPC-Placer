using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyRecoveryTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [DataTestMethod]
    [DataRow("MissionRow", 459.86f, -994.38f, 19.99f, true)]
    [DataRow("MissionRow", 459.86f, -994.38f, 20.0f, false)]
    [DataRow("MissionRow", 500.0f, -994.38f, 19.0f, false)]
    [DataRow("MissionRow", 459.86f, -994.38f, 50.0f, false)]
    [DataRow("Bolingbroke", 1690.86f, 2565.12f, 24.99f, true)]
    [DataRow("Bolingbroke", 1690.86f, 2565.12f, 25.0f, false)]
    [DataRow("Bolingbroke", 1501.0f, 2381.0f, 10.0f, false)]
    public void UnderfloorRecovery_RequiresThePlayableHorizontalPolygon(
        string site, float x, float y, float z, bool expected)
    {
        object layout = CallStatic("GetJusticeCustodyLayoutForSite", Site(site));
        Assert.AreEqual(expected, (bool)CallStatic(
            "IsJusticeCustodyPositionBelowPlayableFloor", layout, new Vector3(x, y, z)));
    }

#if DONJ_STUB_API
    private object _script;
    private Ped _player;
    private JusticeCaseState _state;

    [TestInitialize]
    public void Initialize()
    {
        StubRuntime.Reset();
        _player = Game.Player.Character;
        _player.Handle = 824;
        _player.Model = new Model("player_zero");
        _player.Position = new Vector3(459.86f, -994.38f, 24.91f);
        _state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Incarcerated,
            CustodyEpisodeId = "custody:geometry-recovery",
            WantedEpisodeId = "wanted:geometry-recovery",
            SentenceSeconds = 90
        };
        _script = FormatterServices.GetUninitializedObject(ScriptType);
        Set("_justiceCaseState", _state);
        Set("_justiceRecordState", new JusticeRecordState());
        Set("_justiceEnabled", true);
        Set("_justiceActivePlayerProfileSlot", 0);
        Set("_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 0));
        Set("_justiceCustodyPlayerSlot", 0);
        Set("_justiceCustodyPlayerHandle", _player.Handle);
        Set("_justiceCustodyPlayerModelHash", _player.Model.Hash);
        Set("_justiceCustodySite", Site("MissionRow"));
        Set("_justiceCustodyRuntimeActive", true);
        Set("_justiceCustodyContainmentEstablished", true);
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);
        StubRuntime.NativeCallHandler = (hash, args) =>
            hash == 0xB0F7F8663821D9C3UL ? (object)123 :
            hash == 0x26B0E73D7EAAF4D3UL || hash == 0x6726BDCCC1932F0EUL ||
            hash == 0xE9676F61BC0B3321UL ? (object)true : null;
        StubRuntime.RaycastHandler = (source, target, options, ignored) =>
            new RaycastResult(true, (source + target) * 0.5f - new Vector3(0, 0, 1),
                new Vector3(0, 0, 1));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Call("ResetJusticePreJudgmentHoldingStreamingState", _player);
        Call("ResetJusticeCustodyStreamingRecovery");
        StubRuntime.Reset();
    }

    [TestMethod]
    public void MissingFloor_ReholdsTheSameEpisodeWithoutEscapeOrInventoryMutation()
    {
        _state.Phase = JusticePhase.Escaping;
        _state.FineDue = 123L;
        _state.CustodyGuardPenaltySeconds = 60L;
        Set("_justiceOutsideCustodySinceAt", 1000);
        object inventory = NewNested("JusticeWeaponSnapshot");
        Set("_justiceWeaponSnapshot", inventory);
        _player.Position = new Vector3(459.86f, -994.38f, 18.0f);

        Assert.IsTrue((bool)Call("TryRecoverJusticeCustodyMissingGeometry", _player, 8000));
        Assert.AreEqual(JusticePhase.Incarcerated, _state.Phase);
        Assert.AreEqual("custody:geometry-recovery", _state.CustodyEpisodeId);
        Assert.AreEqual(90, _state.SentenceSeconds);
        Assert.AreEqual(60L, _state.CustodyGuardPenaltySeconds);
        Assert.AreEqual(123L, _state.FineDue);
        Assert.AreEqual(0, _state.Charges.Count);
        Assert.AreEqual(0, _state.CompletedOperationIds.Count);
        Assert.AreEqual(0, Get<int>("_justiceOutsideCustodySinceAt"));
        Assert.IsFalse(Get<bool>("_justiceCustodyContainmentEstablished"));
        Assert.IsTrue(Get<bool>("_justiceCustodyResumePending"));
        Assert.AreSame(inventory, Get<object>("_justiceWeaponSnapshot"));
        Assert.AreEqual(0, _player.Weapons.RemoveAllCount);
        Assert.IsTrue(_player.FreezePosition);
        Assert.IsTrue(_player.IsInvincible);
        Assert.AreEqual(0, Game.Player.WantedLevel);
    }

    [TestMethod]
    public void MissingInteriorWithSolidFloor_DoesNotCaptureAPlayerLeavingTheStation()
    {
        StubRuntime.NativeCallHandler = (hash, args) => null;
        Assert.IsFalse((bool)Call("TryRecoverJusticeCustodyMissingGeometry", _player, 1000));
        Assert.IsFalse(_player.FreezePosition);
        Assert.IsFalse(Get<bool>("_justiceCustodyResumePending"));
    }

    [DataTestMethod]
    [DataRow(500.0f, -994.38f, 24.91f)]
    [DataRow(459.86f, -994.38f, 50.0f)]
    public void HorizontalEscapeAndWallJump_KeepTheirNaturalGrace(float x, float y, float z)
    {
        _player.Position = new Vector3(x, y, z);
        Assert.IsFalse((bool)Call("TryRecoverJusticeCustodyMissingGeometry", _player, 1000));
        Call("UpdateJusticeCustodyEscape", _player, 1000);
        Call("UpdateJusticeCustodyEscape", _player, 6999);
        Assert.AreEqual(JusticePhase.Escaping, _state.Phase);
        Assert.AreEqual(1000, Get<int>("_justiceOutsideCustodySinceAt"));
        Assert.AreEqual(0, _state.CompletedOperationIds.Count);
        Assert.IsFalse(Get<bool>("_justiceCustodyResumePending"));
        _player.Position = new Vector3(459.86f, -994.38f, 24.91f);
        Call("UpdateJusticeCustodyEscape", _player, 7000);
        Assert.AreEqual(JusticePhase.Incarcerated, _state.Phase);
        Assert.AreEqual(0, Get<int>("_justiceOutsideCustodySinceAt"));
    }

    [TestMethod]
    public void CommittedEscape_IsNeverCancelledByUnderfloorRecovery()
    {
        _state.Phase = JusticePhase.Escaping;
        _state.CompletedOperationIds.Add(JusticePolicy.CreateOperationId(
            JusticeOperationKind.DiscardInventory, _state.CustodyEpisodeId));
        _player.Position = new Vector3(459.86f, -994.38f, 18.0f);
        Assert.IsFalse((bool)Call("TryRecoverJusticeCustodyMissingGeometry", _player, 8000));
        Assert.AreEqual(JusticePhase.Escaping, _state.Phase);
        Assert.AreEqual(1, _state.CompletedOperationIds.Count);
    }

    [TestMethod]
    public void FallbackDuringPreparedFine_PreservesItsLegalSiteAndRoundTripsXml()
    {
        _state.Phase = JusticePhase.Captured;
        _state.FineDue = 1000L;
        object intent = NewNested("JusticeFineDebitIntent");
        SetNested(intent, "EpisodeId", _state.CustodyEpisodeId);
        SetNested(intent, "Slot", 0);
        SetNested(intent, "FineAmount", 1000L);
        SetNested(intent, "CashPlanPrepared", false);
        SetNested(intent, "PreparedAtUtcTicks", DateTime.UtcNow.Ticks);
        SetNested(intent, "StationPlanned", true);
        int sentence = (int)CallStatic("CalculateJusticeSentenceAfterFineConversion", 90, 1000L, true);
        SetNested(intent, "SentenceIfDebited", sentence);
        SetNested(intent, "SentenceIfConverted", sentence);
        Set("_justiceFineDebitIntent", intent);
        string before = FineXml();
        Assert.IsNotNull(ReadFine(before));

        Call("HandleJusticeCustodyDestinationStreamingTimeout", _player, Site("MissionRow"), 31000);
        Assert.AreEqual("Bolingbroke", Call("GetJusticePreJudgmentHoldingRequiredSite").ToString());
        Assert.IsFalse((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
        Assert.AreEqual("MissionRow", Get<object>("_justiceCustodySite").ToString());
        Assert.AreSame(intent, Get<object>("_justiceFineDebitIntent"));
        Assert.AreEqual(before, FineXml());
        Assert.IsNotNull(ReadFine(FineXml()));
        Assert.AreEqual(1000L, _state.FineDue);
        Assert.AreEqual(90, _state.SentenceSeconds);

        _state.Phase = JusticePhase.Transporting;
        Assert.IsFalse((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
        Set("_justiceFineDebitIntent", null);
        Assert.IsTrue((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
        Assert.AreEqual("Bolingbroke", Get<object>("_justiceCustodySite").ToString());
    }

    [TestMethod]
    public void FallbackWaitsForCustodyRebindWalInsteadOfAnUnrelatedPoliceCapture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DonJ-custody-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (JusticeRepository repository = new JusticeRepository(
                Path.Combine(directory, "state.xml"), Path.Combine(directory, "state.xml.bak"),
                new JusticeXmlPersistenceCodec(), 0L))
            {
                Set("_justiceRepository", repository);
                Set("_justiceWriteAheadLog", new JusticeWriteAheadLog(Path.Combine(directory, "state.wal")));
                Set("_justicePendingDeathFrontWalRecord", CustodyRebindRecord());
                Set("_justicePreJudgmentHoldingSource", NestedEnum("JusticePreJudgmentHoldingSource", "PendingWalCustodyRebind"));
                Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
                Set("_justicePoliceDeathPreJudgmentHoldingOwnerModelHash", _player.Model.Hash);
                Call("HandleJusticeCustodyDestinationStreamingTimeout", _player, Site("MissionRow"), 31000);

                Assert.IsTrue((bool)Call("IsJusticePoliceDeathFrontResultDurable"));
                Assert.IsFalse((bool)Call("IsJusticeCustodyDeathFrontResultDurable"));
                Assert.IsFalse((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
                Assert.AreEqual("MissionRow", Get<object>("_justiceCustodySite").ToString());

                Set("_justicePendingDeathFrontWalRecord", null);
                Assert.IsTrue((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
                Assert.AreEqual("Bolingbroke", Get<object>("_justiceCustodySite").ToString());
            }
        }
        finally
        {
            Set("_justiceRepository", null);
            Set("_justiceWriteAheadLog", null);
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void RepairHoldingFallback_BelongsToItsInactiveOwnerAcrossProfileReconciliation()
    {
        _player.Model = new Model("player_one");
        Set("_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
        Set("_justicePreJudgmentHoldingSource", NestedEnum("JusticePreJudgmentHoldingSource", "RepairPoliceArrest"));
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 1);
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerModelHash", _player.Model.Hash);
        JusticeCaseState ownerCase = new JusticeCaseState
        {
            Enabled = true, Phase = JusticePhase.Captured,
            CustodyEpisodeId = "custody:repair-owner-one", SentenceSeconds = 60
        };
        JusticePlayerProfileState[] profiles = new JusticePlayerProfileState[3];
        profiles[1] = new JusticePlayerProfileState(1) { CaseState = ownerCase };
        Set("_justicePlayerProfiles", profiles);

        Call("HandleJusticeCustodyDestinationStreamingTimeout", _player, Site("MissionRow"), 31000);
        Assert.AreEqual(1, Get<int>("_justiceCustodyRecoveryOwnerSlot"));
        Assert.AreEqual(ownerCase.CustodyEpisodeId, Get<string>("_justiceCustodyRecoveryEpisodeId"));
        Assert.IsTrue((bool)Call("HasJusticeCustodyStreamingRecoveryOwner"));
        Assert.IsFalse((bool)Call("ApplyJusticeCustodyStreamingFallbackSite"));
        Assert.AreEqual("MissionRow", Get<object>("_justiceCustodySite").ToString());

        Set("_justiceActivePlayerProfileSlot", 1);
        Set("_justiceCaseState", ownerCase);
        Assert.IsTrue((bool)Call("HasJusticeCustodyStreamingRecoveryOwner"));
        Assert.AreEqual("Bolingbroke", Call("GetJusticePreJudgmentHoldingRequiredSite").ToString());
        Set("_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 2));
        _player.Model = new Model("player_two");
        Assert.IsFalse((bool)Call("HasJusticeCustodyStreamingRecoveryOwner"));
    }

    [TestMethod]
    public void BothDestinationsTimeout_StopsStreamingWithoutAnotherFadeOrAdmission()
    {
        _state.Phase = JusticePhase.Transporting;
        Set("_justiceCustodyTransferPending", true);
        Set("_justiceCustodyRespawnTransferPending", true);
        Set("_justicePreJudgmentHoldingSource", NestedEnum("JusticePreJudgmentHoldingSource", "Captured"));
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerModelHash", _player.Model.Hash);
        StubRuntime.RaycastHandler = (source, target, options, ignored) => new RaycastResult();
        Vector3 cell = _player.Position;
        Assert.IsFalse(Move(cell, 1000));
        Assert.IsFalse(Move(cell, 31000));
        Assert.IsTrue(Get<bool>("_justiceCustodyStreamingFallbackToPrison"));
        Assert.IsFalse(Get<bool>("_justiceCustodyStreamingTechnicalFailure"));
        Vector3 prison = new Vector3(1690.86f, 2565.12f, 45.56f);
        Assert.IsFalse(Move(prison, 32000));
        Assert.IsFalse(Move(prison, 62000));
        Assert.IsTrue(Get<bool>("_justiceCustodyStreamingTechnicalFailure"));
        Assert.IsFalse(Get<bool>("_justiceCustodyStreamingFocusRequested"));
        Assert.IsFalse(Get<bool>("_justiceCustodyStreamingSceneRequested"));
        int streamingRequests = Count((ulong)Hash.REQUEST_COLLISION_AT_COORD);
        int fadeRequests = Count((ulong)Hash.DO_SCREEN_FADE_OUT) + Count((ulong)Hash.DO_SCREEN_FADE_IN);
        for (int now = 63000; now <= 70000; now += 1000)
            Assert.IsFalse(Move(prison, now));
        Assert.AreEqual(streamingRequests, Count((ulong)Hash.REQUEST_COLLISION_AT_COORD));
        Assert.AreEqual(fadeRequests, Count((ulong)Hash.DO_SCREEN_FADE_OUT) + Count((ulong)Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(JusticePhase.Transporting, _state.Phase);
        Assert.AreEqual(90, _state.SentenceSeconds);
        Assert.AreEqual(0, _state.Charges.Count);
        Assert.IsTrue(_player.FreezePosition);
        Assert.IsTrue(_player.IsInvincible);
    }

    [TestMethod]
    public void WriterOutage_UsesPhysicalPrisonFallbackWithoutRewritingTheJudicialSite()
    {
        Set("_justicePersistenceInitializationFailurePermanent", true);
        Set("_justicePersistenceServicesUnavailable", true);
        Set("_justiceCustodyWaitingForRespawn", true);
        _state.FineDue = 230L;
        _player.Position = new Vector3(310.0f, -590.0f, 43.0f);
        StubRuntime.RaycastHandler = (source, target, options, ignored) =>
            new RaycastResult(source.X > 1000.0f,
                (source + target) * 0.5f - new Vector3(0, 0, 1), new Vector3(0, 0, 1));

        Game.GameTime = 1000;
        Assert.IsFalse((bool)Call("TryMaintainJusticeCustodyDuringPermanentPersistenceOutage", _player, 1000));
        Game.GameTime = 31000;
        Assert.IsFalse((bool)Call("TryMaintainJusticeCustodyDuringPermanentPersistenceOutage", _player, 31000));
        Assert.IsTrue(Get<bool>("_justiceCustodyStreamingFallbackToPrison"));
        Game.GameTime = 32000;
        Assert.IsTrue((bool)Call("TryMaintainJusticeCustodyDuringPermanentPersistenceOutage", _player, 32000));

        Assert.AreEqual(1690.86f, _player.Position.X, 0.01f);
        Assert.AreEqual("MissionRow", Get<object>("_justiceCustodySite").ToString());
        Assert.AreEqual("Bolingbroke", Get<object>("_justiceCustodyStreamingSite").ToString());
        Assert.AreEqual(90, _state.SentenceSeconds);
        Assert.AreEqual(230L, _state.FineDue);
        Assert.AreEqual(JusticePhase.Incarcerated, _state.Phase);
        Assert.AreEqual(0, _state.Charges.Count);
        Assert.AreEqual(0, _player.Weapons.RemoveAllCount);
        Assert.IsTrue(Get<bool>("_justiceCustodyPersistenceOutageHoldingEstablished"));
        Assert.IsTrue(_player.IsInvincible);
        Assert.IsTrue(_player.FreezePosition);
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [TestMethod]
    public void HeroSwitchBeforeTeleport_RestoresOnlyThePreviouslyFrozenPed()
    {
        _player.CanRagdoll = true;
        StubRuntime.RaycastHandler = (source, target, options, ignored) => new RaycastResult();
        Assert.IsFalse(Move(_player.Position, 1000));
        Assert.IsFalse(Get<bool>("_justicePreJudgmentHoldingPositionApplied"));
        Assert.IsTrue(_player.FreezePosition);
        Assert.IsFalse(_player.CanRagdoll);
        Ped incoming = new Ped
        {
            Handle = 825,
            Model = new Model("player_one"),
            FreezePosition = true,
            IsInvincible = true,
            CanRagdoll = false
        };
        Game.Player.Character = incoming;

        Call("ResetJusticePreJudgmentHoldingStreamingState", new object[] { null });

        Assert.IsFalse(_player.FreezePosition);
        Assert.IsFalse(_player.IsInvincible);
        Assert.IsTrue(_player.CanRagdoll);
        Assert.IsTrue(incoming.FreezePosition);
        Assert.IsTrue(incoming.IsInvincible);
        Assert.IsFalse(incoming.CanRagdoll);
        Assert.IsFalse(Get<bool>("_justicePreJudgmentHoldingStreamingPending"));
    }

    [TestMethod]
    public void CleanupBeforeFirstTeleport_RestoresTheFreezeOwnedDuringMapLoading()
    {
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (hash == 0x0888C3502DBBEEF5UL) throw new InvalidOperationException("MP indisponible");
            return null;
        };
        Assert.IsFalse(Move(_player.Position, 1000));
        Assert.IsFalse(Get<bool>("_justicePreJudgmentHoldingPositionApplied"));
        Assert.IsTrue(_player.FreezePosition);
        Assert.IsTrue(_player.IsInvincible);
        Call("ResetJusticePreJudgmentHoldingStreamingState", _player);
        Assert.IsFalse(_player.FreezePosition);
        Assert.IsFalse(_player.IsInvincible);
    }

    private bool Move(Vector3 target, int now)
    {
        Game.GameTime = now;
        return (bool)Call("TryMoveJusticePoliceDeathPreJudgmentHoldingPlayerWithFallback", _player, target, 88.0f, now);
    }

    private JusticeWalRecord CustodyRebindRecord()
    {
        IEnumerable<JusticePersistenceField> fields = (IEnumerable<JusticePersistenceField>)CallStatic(
            "CreateJusticeDeathFrontWalFields", "CustodyRebind", 0L, 0L,
            "slot:0:model:" + _player.Model.Hash.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _state.CustodyEpisodeId, Convert.ToInt32(Site("MissionRow")),
            0, _player.Model.Hash, 0, _player.Model.Hash);
        return new JusticeWalRecord("death-front:geometry-recovery", "DeathFront", 0,
            JusticeWalState.Prepared, 0L, DateTime.UtcNow.Ticks, fields);
    }

    private string FineXml()
    {
        StringBuilder xml = new StringBuilder();
        using (XmlWriter writer = XmlWriter.Create(xml, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("Custody");
            Call("WriteJusticeFineDebitIntentXml", writer);
            writer.WriteEndElement();
        }
        return xml.ToString();
    }

    private object ReadFine(string xml)
    {
        XmlDocument document = new XmlDocument();
        document.LoadXml(xml);
        return Call("ReadJusticeFineDebitIntentXml", document.DocumentElement);
    }

    private static int Count(ulong hash) => StubRuntime.NativeCalls.Count(call => call.Hash == hash);
    private object Call(string name, params object[] args) => Invoke(_script, name, Instance, args);
    private void Set(string name, object value) => ScriptType.GetField(name, Instance).SetValue(_script, value);
    private T Get<T>(string name) => (T)ScriptType.GetField(name, Instance).GetValue(_script);
    private static object NewNested(string name) => Activator.CreateInstance(ScriptType.GetNestedType(name, BindingFlags.NonPublic), true);
    private static void SetNested(object instance, string name, object value) => instance.GetType().GetField(name, Instance | BindingFlags.Public).SetValue(instance, value);
#endif

    private static object Site(string name) => NestedEnum("JusticeCustodySite", name);
    private static object NestedEnum(string type, string name) => Enum.Parse(ScriptType.GetNestedType(type, BindingFlags.NonPublic), name);
    private static object CallStatic(string name, params object[] args) => Invoke(null, name, Static, args);
    private static object Invoke(object target, string name, BindingFlags flags, object[] args) =>
        ScriptType.GetMethods(flags).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
}
