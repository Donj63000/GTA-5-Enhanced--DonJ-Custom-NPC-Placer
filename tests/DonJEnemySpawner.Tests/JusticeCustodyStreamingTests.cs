using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GTA.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if DONJ_STUB_API
using GTA;
using GTA.Native;
#endif

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyStreamingTests
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Vector3 Cell = new Vector3(459.86f, -994.38f, 24.91f);

    [TestMethod]
    public void FloorProof_RequiresLocalFiniteHorizontalSupport()
    {
        Assert.IsTrue(Floor(Cell, Cell - new Vector3(0, 0, 1), new Vector3(0, 0, 1)));
        Assert.IsTrue(Floor(Cell, Cell - new Vector3(0, 0, 1.5f), new Vector3(0, 0, 0.5f)));
        Assert.IsFalse(Floor(Cell, new Vector3(Cell.X, Cell.Y, 30.71f), new Vector3(0, 0, 1)),
            "La rue au-dessus de la cellule ne constitue pas son plancher.");
        Assert.IsFalse(Floor(Cell, Cell - new Vector3(0, 0, 1.6f), new Vector3(0, 0, 1)));
        Assert.IsFalse(Floor(Cell, Cell + new Vector3(0, 0, 0.3f), new Vector3(0, 0, 1)));
        Assert.IsFalse(Floor(Cell, Cell + new Vector3(1, 0, 0), new Vector3(0, 0, 1)));
        Assert.IsFalse(Floor(Cell, Cell, new Vector3(1, 0, 0)));
        Assert.IsFalse(Floor(Cell, Cell, new Vector3(0, 0, -1)));
        Assert.IsFalse(Floor(Cell, new Vector3(Cell.X, Cell.Y, float.NaN), new Vector3(0, 0, 1)));
        Assert.IsFalse(Floor(Cell, Cell, new Vector3(0, 0, float.PositiveInfinity)));
    }

    [TestMethod]
    public void StreamingBudgets_KeepBoundedProbeAndTimeoutContracts()
    {
        Assert.AreEqual(250, typeof(DonJEnemySpawner).GetField("JusticeCustodyStreamingPollMs", Static).GetRawConstantValue());
        Assert.AreEqual(30000, typeof(DonJEnemySpawner).GetField("JusticeCustodyStreamingTimeoutMs", Static).GetRawConstantValue());
        Assert.AreEqual(2.0f, typeof(DonJEnemySpawner).GetField("JusticeCustodyStreamingProbeHalfHeight", Static).GetRawConstantValue());
    }

#if DONJ_STUB_API
    private const ulong LoadMp = 0x0888C3502DBBEEF5UL;
    private const ulong Priority = 0x9BAE5AD2508DF078UL;
    private const ulong InteriorAt = 0xB0F7F8663821D9C3UL;
    private const ulong InteriorValid = 0x26B0E73D7EAAF4D3UL;
    private const ulong InteriorReady = 0x6726BDCCC1932F0EUL;
    private const ulong Pin = 0x2CA429C029CCF247UL;
    private const ulong Unpin = 0x261CCE7EED010641UL;
    private const ulong Focus = 0xBB7454BAFF08FE25UL;
    private const ulong NewScene = 0x212A8D0D2BABFAC2UL;
    private const ulong ClearFocus = 0x31B73D1EA9F01DA2UL;
    private const ulong Collision = 0xE9676F61BC0B3321UL;
    private object _script;
    private Ped _player;
    private int _interiorId;
    private bool _interiorValid;
    private bool _interiorReady;
    private bool _collisionReady;
    private bool _floorReady;
    private float _floorOffset;
    private int _rayCount;

    [TestInitialize]
    public void Initialize()
    {
        StubRuntime.Reset();
        _script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", -1);
        Set("_justiceCustodyPlayerSlot", 0);
        _player = Game.Player.Character;
        _player.Handle = 812;
        _player.Model = new Model("player_zero");
        _player.Position = new Vector3(310, -590, 43);
        _interiorId = 123;
        _interiorValid = true;
        _interiorReady = true;
        _collisionReady = true;
        _floorReady = true;
        _floorOffset = -1.0f;
        _rayCount = 0;
        StubRuntime.NativeCallHandler = Native;
        StubRuntime.RaycastHandler = (source, target, options, ignored) =>
        {
            _rayCount++;
            Assert.AreSame(_player, ignored);
            Assert.AreEqual(IntersectOptions.Map, options);
            Assert.AreEqual(4.0f, source.Z - target.Z, 0.001f);
            Vector3 center = (source + target) * 0.5f;
            return new RaycastResult(_floorReady,
                center + new Vector3(0, 0, _floorOffset), new Vector3(0, 0, 1));
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        Call("ResetJusticeCustodyDestinationStreaming");
        StubRuntime.Reset();
    }

    [TestMethod]
    public void MissionRow_RequestsMapsBeforeGeometryAndKeepsSingleFocusAcrossRetries()
    {
        _interiorReady = false;
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual("Pending", Prepare(1100));
        Assert.AreEqual("Pending", Prepare(1300));
        Assert.AreEqual(0, _rayCount);
        Assert.AreEqual(1, Count(LoadMp));
        Assert.AreEqual(1, Count(Priority));
        Assert.AreEqual(1, Count(Focus));
        Assert.AreEqual(1, Count(NewScene));
        Assert.AreEqual(1, Count(ClearFocus));
        Assert.AreEqual(1, Count(Pin));
        Assert.AreEqual(2, Count(InteriorReady));
        ulong[] calls = StubRuntime.NativeCalls.Select(c => c.Hash).ToArray();
        Assert.IsTrue(Array.IndexOf(calls, LoadMp) < Array.IndexOf(calls, Priority));
        Assert.IsTrue(Array.IndexOf(calls, Priority) < Array.IndexOf(calls, InteriorAt));
        _interiorReady = true;
        Assert.AreEqual("Ready", Prepare(1600));
        Assert.AreEqual(1, _rayCount);
        Assert.AreEqual(310.0f, _player.Position.X, 0.001f);
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_OUT));
        Assert.IsFalse(_player.FreezePosition, "Le helper de streaming n'acquiert pas les contrôles du joueur.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultiplayerMap_RetriesOnlyFailedStageWithoutPoisoningSuccess(bool failSecondStage)
    {
        bool fail = true;
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (fail && hash == (failSecondStage ? Priority : LoadMp))
                throw new InvalidOperationException("Refus simulé du chargement MP");
            return Native(hash, args);
        };
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual(0, Count(Focus));
        fail = false;
        Assert.AreEqual("Pending", Prepare(1500));
        Assert.AreEqual("Ready", Prepare(2000));
        Assert.AreEqual(failSecondStage ? 1 : 2, Count(LoadMp));
        Assert.AreEqual(failSecondStage ? 2 : 1, Count(Priority));
        Assert.AreEqual("Ready", Prepare(2500));
        Assert.AreEqual(failSecondStage ? 1 : 2, Count(LoadMp));
        Assert.AreEqual(failSecondStage ? 2 : 1, Count(Priority));
    }

    [TestMethod]
    public void InteriorRefreshFailure_RetriesRefreshWithoutPinningAgain()
    {
        const ulong refresh = 0x41F37C3427C75AE0UL;
        bool fail = true;
        StubRuntime.NativeCallHandler = (hash, args) =>
        {
            if (hash == refresh && fail) throw new InvalidOperationException("Refresh temporairement refusé");
            return Native(hash, args);
        };
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual(1, Count(Pin));
        fail = false;
        Assert.AreEqual("Ready", Prepare(1250));
        Assert.AreEqual(1, Count(Pin));
        Assert.AreEqual(2, Count(refresh));
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("invalid")]
    [DataRow("notReady")]
    [DataRow("exception")]
    public void MissionRow_RejectsEveryUnprovedInteriorEvenAfterTimeout(string failure)
    {
        _interiorId = failure == "missing" ? 0 : 123;
        _interiorValid = failure != "invalid";
        _interiorReady = failure != "notReady";
        if (failure == "exception")
        {
            StubRuntime.NativeCallHandler = (hash, args) =>
            {
                if (hash == InteriorReady) throw new InvalidOperationException("Intérieur inaccessible");
                return Native(hash, args);
            };
        }
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual("TimedOut", Prepare(31000));
        Assert.AreEqual(0, _rayCount);
        Assert.IsTrue(TimedOut(31000));
        Assert.AreEqual(310.0f, _player.Position.X, 0.001f);
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [DataTestMethod]
    [DataRow(false, -1.0f)]
    [DataRow(true, 5.8f)]
    [DataRow(true, -2.0f)]
    public void GroundProof_RejectsVoidRoofAndDistantFloorDespiteCollision(bool hit, float offset)
    {
        _floorReady = hit;
        _floorOffset = offset;
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual("TimedOut", Prepare(31000));
        _player.Position = Cell;
        Assert.IsFalse(ReadyForPlayer(31000));
    }

    [TestMethod]
    public void FinalAdmissionProof_RechecksGeometryInSameTickWithoutResettingDeadline()
    {
        Assert.AreEqual("Ready", Prepare(1000));
        _player.Position = Cell;
        Assert.IsTrue(ReadyForPlayer(1000));
        _floorReady = false;
        Assert.IsFalse(ReadyForPlayer(1000), "La preuve finale doit relire la géométrie même dans le tick du téléport.");
        Assert.IsTrue(TimedOut(31000));
        Assert.AreEqual(1, Count(Focus));
        Assert.AreEqual(1, Count(LoadMp));
        _floorReady = true;
        Assert.AreEqual("Ready", Prepare(32000), "Une preuve réellement prête prime sur le délai expiré.");
        _interiorReady = false;
        Assert.IsFalse(ReadyForPlayer(32000));
    }

    [TestMethod]
    public void CollisionProof_MustBelongToPlayerAtDestinationAndCanRecoverAfterDeadline()
    {
        Assert.AreEqual("Ready", Prepare(1000));
        Assert.IsFalse(ReadyForPlayer(1000));
        _player.Position = Cell;
        _collisionReady = false;
        Assert.IsFalse(ReadyForPlayer(1100));
        Assert.IsTrue(TimedOut(31000));
        StringAssert.Contains((string)Call("GetJusticeCustodyDestinationStreamingDiagnostic"), "CollisionUnavailable");
        _collisionReady = true;
        Assert.IsTrue(ReadyForPlayer(32000));
    }

    [TestMethod]
    public void VerifiedPlacement_DisplacedThreeMetersIsMovedBackBeforeFinalProof()
    {
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerModelHash", _player.Model.Hash);
        StubRuntime.ScreenFadedOut = true;
        Game.GameTime = 1000;
        Assert.IsTrue((bool)Call("TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer", _player, Cell, 88.0f));
        int moves = Count(0x239A3351AC1DA385UL);
        _player.Position = Cell + new Vector3(3.0f, 0.0f, 0.0f);
        Game.GameTime = 2000;
        Assert.IsTrue((bool)Call("TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer", _player, Cell, 88.0f),
            "Le latch de déplacement doit partager la tolérance de la preuve finale.");
        Assert.AreEqual(moves + 1, Count(0x239A3351AC1DA385UL));
        Assert.IsTrue(_player.Position.DistanceTo(Cell) <= 2.0f);
        Assert.AreEqual(1, Count(LoadMp));
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [TestMethod]
    public void DestinationChange_ReleasesPreviousPinAndStartsSeparateTimeout()
    {
        Assert.AreEqual("Ready", Prepare(1000));
        Vector3 prison = new Vector3(1690.86f, 2565.12f, 45.56f);
        _floorReady = false;
        Assert.AreEqual("Pending", Prepare(31000, "Bolingbroke", prison));
        Assert.AreEqual(1, Count(Unpin));
        Assert.IsFalse(TimedOut(31000, "Bolingbroke", prison));
        Assert.AreEqual("TimedOut", Prepare(61000, "Bolingbroke", prison));
        Assert.AreEqual(1, Count(LoadMp));
        Assert.AreEqual(1, Count(InteriorAt));
        Assert.AreEqual(2, Count(Focus));
    }

    [TestMethod]
    public void Bolingbroke_UsesExteriorGeometryWithoutMultiplayerOrInteriorRequirement()
    {
        Vector3 prison = new Vector3(1690.86f, 2565.12f, 45.56f);
        _interiorId = 0;
        Assert.AreEqual("Ready", Prepare(1000, "Bolingbroke", prison));
        Assert.AreEqual(0, Count(LoadMp));
        Assert.AreEqual(0, Count(InteriorAt));
        _player.Position = prison;
        Assert.IsTrue(ReadyForPlayer(1000, "Bolingbroke", prison));
    }

    [TestMethod]
    public void ChangedCharacter_InvalidatesLeaseAndResetDoesNotRepeatMultiplayerLoading()
    {
        Assert.AreEqual("Ready", Prepare(1000));
        _player.Model = new Model("player_one");
        Set("_justiceCustodyPlayerSlot", 1);
        Assert.IsFalse(ReadyForPlayer(1100));
        Assert.IsFalse(TimedOut(31000));
        Assert.AreEqual("Ready", Prepare(31000));
        Assert.AreEqual(1, Count(Unpin));
        Assert.IsFalse(TimedOut(31000));
        Call("ResetJusticeCustodyDestinationStreaming");
        Call("ResetJusticeCustodyDestinationStreaming");
        Assert.AreEqual(2, Count(Unpin));
        Assert.AreEqual(1, Count(LoadMp));
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CapturedHeroChange_WithoutDeathFrontReleasesStreamingBeforeBlockedProfileSwitch(bool fadeInPending)
    {
        var state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Captured,
            CustodyEpisodeId = "custody:stream-cleanup",
            SentenceSeconds = 180
        };
        Set("_justiceCaseState", state);
        Set("_justicePreJudgmentHoldingSource", Enum.Parse(
            typeof(DonJEnemySpawner).GetField("_justicePreJudgmentHoldingSource", Instance).FieldType,
            "Captured"));
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerSlot", 0);
        Set("_justicePoliceDeathPreJudgmentHoldingOwnerModelHash", _player.Model.Hash);
        Assert.AreEqual("Ready", Prepare(1000));
        Set("_justiceCustodyRespawnTransferPending", true);
        Set("_justiceCustodyAdmissionFadeInRequested", fadeInPending);
        Set("_justiceProfileContextBlocked", true);
        Set("_justiceCanonicalPlayerSlotOverride", new Func<int>(() => 1));
        var nextHero = new Ped { Handle = 813, Model = new Model("player_one"), Position = new Vector3(310, -590, 43) };
        Game.Player.Character = nextHero;
        Game.Player.WantedLevel = 3;
        StubRuntime.ScreenFadedOut = true;
        Call("UpdateJusticeCustodyRespawnTransferMask", nextHero);
        Assert.AreEqual(1, Count(Unpin));
        Assert.AreEqual(2, Count(ClearFocus));
        Assert.IsFalse((bool)typeof(DonJEnemySpawner).GetField("_justiceCustodyStreamingActive", Instance).GetValue(_script));
        Assert.AreEqual(JusticePhase.Captured, state.Phase);
        Assert.AreEqual(180, state.SentenceSeconds);
        Assert.AreEqual(3, Game.Player.WantedLevel);
        Assert.IsFalse(nextHero.FreezePosition);
        Assert.IsFalse(nextHero.IsInvincible);
    }

    [TestMethod]
    public void CompletedAdmission_ReleasesDistantFocusButKeepsCellPinnedUntilExit()
    {
        Assert.AreEqual("Ready", Prepare(1000));
        Call("CompleteJusticeCustodyDestinationStreaming");
        Call("CompleteJusticeCustodyDestinationStreaming");
        Assert.AreEqual(2, Count(ClearFocus));
        Assert.AreEqual(0, Count(Unpin));
        Assert.AreEqual(1, Count(Pin));
        Call("ResetJusticeCustodyDestinationStreaming");
        Assert.AreEqual(1, Count(Unpin));
        Assert.AreEqual(0, Count((ulong)Hash.DO_SCREEN_FADE_IN));
    }

    [TestMethod]
    public void LocalMaintenanceProbes_LeaveStreamingLeaseAndFocusUntouched()
    {
        _player.Position = Cell;
        Assert.IsTrue((bool)typeof(DonJEnemySpawner).GetMethod("IsJusticeCustodyLocalFloorReady", Static)
            .Invoke(null, new object[] { _player, Cell }));
        Assert.IsTrue((bool)Call("IsJusticeCustodyInteriorStreamingReadyAt", _player, Site("MissionRow")));
        _interiorReady = false;
        Assert.IsFalse((bool)Call("IsJusticeCustodyInteriorStreamingReadyAt", _player, Site("MissionRow")));
        Assert.AreEqual(0, Count(Focus));
        Assert.AreEqual(0, Count(Pin));
        Assert.AreEqual(0, Count(LoadMp));
    }

    [TestMethod]
    public void Timeout_RemainsCorrectAcrossGameTimeOverflow()
    {
        _floorReady = false;
        int startedAt = int.MaxValue - 1000;
        Assert.AreEqual("Pending", Prepare(startedAt));
        Assert.IsFalse(TimedOut(unchecked(startedAt + 29999)));
        Assert.AreEqual("TimedOut", Prepare(unchecked(startedAt + 30000)));
    }

    [TestMethod]
    public void TimeoutBoundary_ReprobesInsteadOfUsingRecentNegativeCache()
    {
        _floorReady = false;
        Assert.AreEqual("Pending", Prepare(1000));
        Assert.AreEqual("Pending", Prepare(30999));
        _floorReady = true;
        Assert.AreEqual("Ready", Prepare(31000));
        Assert.AreEqual(3, _rayCount);
    }

    private object Native(ulong hash, object[] args)
    {
        if (hash == InteriorAt) return _interiorId;
        if (hash == InteriorValid) return _interiorValid;
        if (hash == InteriorReady) return _interiorReady;
        if (hash == Collision) return _collisionReady;
        return null;
    }

    private string Prepare(int now, string site = "MissionRow", Vector3? target = null)
    {
        return Call("PrepareJusticeCustodyDestinationStreaming", _player, Site(site), target ?? Cell, now).ToString();
    }

    private bool ReadyForPlayer(int now, string site = "MissionRow", Vector3? target = null)
    {
        return (bool)Call("IsJusticeCustodyDestinationStreamingReadyForPlayer", _player, Site(site), target ?? Cell, now);
    }

    private bool TimedOut(int now, string site = "MissionRow", Vector3? target = null)
    {
        return (bool)Call("HasJusticeCustodyDestinationStreamingTimedOut", _player, Site(site), target ?? Cell, now);
    }

    private static object Site(string name)
    {
        return Enum.Parse(typeof(DonJEnemySpawner).GetNestedType("JusticeCustodySite", BindingFlags.NonPublic), name);
    }

    private static int Count(ulong hash) => StubRuntime.NativeCalls.Count(c => c.Hash == hash);
    private object Call(string method, params object[] args) => typeof(DonJEnemySpawner).GetMethod(method, Instance).Invoke(_script, args);
    private void Set(string field, object value) => typeof(DonJEnemySpawner).GetField(field, Instance).SetValue(_script, value);
#endif

    private static bool Floor(Vector3 target, Vector3 hit, Vector3 normal)
    {
        return (bool)typeof(DonJEnemySpawner).GetMethod("IsJusticeCustodyStreamingFloorValid", Static)
            .Invoke(null, new object[] { target, hit, normal });
    }
}
