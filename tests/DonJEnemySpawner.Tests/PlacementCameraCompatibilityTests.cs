#if DONJ_STUB_API
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class PlacementCameraCompatibilityTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SaveDirectoryVariable = "DONJ_ENEMY_SPAWNER_SAVE_DIR";
    private DonJEnemySpawner _script;
    private string _temporarySaveDirectory;
    private string _previousSaveDirectory;

    [TestInitialize]
    public void Initialize()
    {
        _previousSaveDirectory = Environment.GetEnvironmentVariable(SaveDirectoryVariable);
        _temporarySaveDirectory = Path.Combine(
            Path.GetTempPath(), "DonJPlacementCamera_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporarySaveDirectory);
        Environment.SetEnvironmentVariable(SaveDirectoryVariable, _temporarySaveDirectory);
        StubRuntime.Reset();
        World.RenderingCamera = null;
        Game.Player.Character.Handle = 107;
        // Je fournis un état Justice vide pour ne jamais charger une sauvegarde réelle.
        SeedCanonicalJusticeState(_temporarySaveDirectory);
        _script = new DonJEnemySpawner();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            // Retirer les pannes injectées AVANT le nettoyage de l'instance.
            StubRuntime.NativeCallHandler = null;
            StubRuntime.KeyPressedHandler = null;
            StubRuntime.FreezePositionReadHandler = null;
            StubRuntime.InvincibilityReadHandler = null;
            StubRuntime.FreezePositionWriteHandler = null;
            StubRuntime.InvincibilityWriteHandler = null;
            if (_script != null)
            {
                RaiseScriptEvent("RaiseAborted");
            }
        }
        finally
        {
            _script = null;
            World.RenderingCamera = null;
            StubRuntime.Reset();
            Environment.SetEnvironmentVariable(SaveDirectoryVariable, _previousSaveDirectory);
            if (Directory.Exists(_temporarySaveDirectory))
            {
                Directory.Delete(_temporarySaveDirectory, true);
            }
        }
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void NormalExit_RestoresCapturedState(bool invincible, bool frozen)
    {
        Ped player = Game.Player.Character;
        player.IsInvincible = invincible;
        player.FreezePosition = frozen;

        Invoke("StartPlacementMode");
        AssertCameraActive();
        Assert.IsTrue(player.IsInvincible);
        Assert.IsTrue(player.FreezePosition);
        Invoke("StopPlacementMode", false);

        AssertSessionReleased();
        Assert.AreEqual(invincible, player.IsInvincible);
        Assert.AreEqual(frozen, player.FreezePosition);
    }

    [DataTestMethod]
    [DataRow("Npc", false)]
    [DataRow("Npc", true)]
    [DataRow("Vehicle", false)]
    [DataRow("Vehicle", true)]
    [DataRow("Object", false)]
    [DataRow("Object", true)]
    [DataRow("Entrance", true)]
    [DataRow("Exit", true)]
    public void StaleReadbacks_DoNotCancelCameraOrMovement(
        string placementType, bool staleInvincibility)
    {
        FieldInfo selection = typeof(DonJEnemySpawner).GetField(
            "_selectedPlacementType", PrivateInstance);
        Assert.IsNotNull(selection);
        selection.SetValue(_script, Enum.Parse(selection.FieldType, placementType));
        Ped player = Game.Player.Character;
        int freezeReads = 0;
        StubRuntime.FreezePositionReadHandler = (entity, actual) =>
        {
            if (!object.ReferenceEquals(entity, player))
            {
                return actual;
            }
            freezeReads++;
            return false;
        };
        if (staleInvincibility)
        {
            StubRuntime.InvincibilityReadHandler = (entity, actual) =>
                object.ReferenceEquals(entity, player) ? false : actual;
        }

        Invoke("StartPlacementMode");
        AssertCameraActive();
        Camera camera = GetField<Camera>("_placementCamera");
        float initialY = camera.Position.Y;
        StubRuntime.KeyPressedHandler = key => key == Keys.Z;
        for (int i = 0; i < 3; i++)
        {
            Invoke("UpdatePlacementMode");
            AssertCameraActive();
        }
        Assert.IsTrue(camera.Position.Y > initialY, "La caméra doit avancer en ZQSD.");
        Assert.AreEqual(
            staleInvincibility ? 0 : 1,
            Convert.ToInt32(GetField<object>("_playerInvincibilityOwners")));

        Invoke("StopPlacementMode", false);
        AssertSessionReleased();
        Assert.AreEqual(1, freezeReads, "Le gel ne se relit que pour capturer l'état initial.");
        StubRuntime.FreezePositionReadHandler = null;
        StubRuntime.InvincibilityReadHandler = null;
        Assert.IsFalse(player.FreezePosition);
        Assert.IsFalse(player.IsInvincible);
    }

    [TestMethod]
    public void ReadbackFailureDuringPlacement_ReleasesProtectionButKeepsCamera()
    {
        Invoke("StartPlacementMode");
        AssertCameraActive();
        StubRuntime.InvincibilityReadHandler = (entity, actual) => false;

        Invoke("UpdatePlacementMode");

        AssertCameraActive();
        Assert.AreEqual(0, Convert.ToInt32(GetField<object>("_playerInvincibilityOwners")));
        Assert.IsFalse(GetField<bool>("_playerInvincibilityRestorePending"));
        StubRuntime.InvincibilityReadHandler = null;
        Assert.IsFalse(Game.Player.Character.IsInvincible);
        Invoke("StopPlacementMode", false);
        AssertSessionReleased();
    }

    [TestMethod]
    public void StaleFreezeReadOnExit_DoesNotLeaveRestorationPending()
    {
        Ped player = Game.Player.Character;
        player.FreezePosition = true;
        Invoke("StartPlacementMode");
        AssertCameraActive();
        StubRuntime.FreezePositionReadHandler = (entity, actual) => false;

        Invoke("StopPlacementMode", false);

        AssertSessionReleased();
        StubRuntime.FreezePositionReadHandler = null;
        Assert.IsTrue(player.FreezePosition, "Conserver le gel préexistant capturé.");
        Invoke("StartPlacementMode");
        AssertCameraActive();
        Invoke("StopPlacementMode", false);
        AssertSessionReleased();
        Assert.IsTrue(player.FreezePosition);
    }

    [TestMethod]
    public void FreezeSetterFailureBeforeCameraCreation_RollsBackPartialStartup()
    {
        StubRuntime.FreezePositionWriteHandler = (entity, value) =>
        {
            if (value)
            {
                throw new InvalidOperationException("Gel indisponible après mutation.");
            }
        };

        Invoke("StartPlacementMode");

        AssertSessionReleased();
        Assert.IsFalse(Game.Player.Character.FreezePosition);
        Assert.IsFalse(Game.Player.Character.IsInvincible);
        StubRuntime.FreezePositionWriteHandler = null;
        Invoke("StartPlacementMode");
        AssertCameraActive();
    }

    [TestMethod]
    public void FreezeRestoreSetterFailure_RetainsStateForNextTick()
    {
        Invoke("StartPlacementMode");
        AssertCameraActive();
        StubRuntime.FreezePositionWriteHandler = (entity, value) =>
        {
            if (!value)
            {
                throw new InvalidOperationException("Dégel temporairement indisponible.");
            }
        };

        Invoke("StopPlacementMode", false);

        Assert.IsFalse(GetField<bool>("_placementMode"));
        Assert.IsNull(World.RenderingCamera);
        Assert.IsTrue(GetField<bool>("_placementPlayerStateStored"));
        StubRuntime.FreezePositionWriteHandler = null;
        Invoke("MaintainPlacementPlayerStateRecovery");
        AssertSessionReleased();
        Assert.IsFalse(Game.Player.Character.FreezePosition);
    }

    [TestMethod]
    public void FailedInvincibilityRollback_DoesNotEnterCompatibilityMode()
    {
        StubRuntime.InvincibilityReadHandler = (entity, actual) => false;
        StubRuntime.InvincibilityWriteHandler = (entity, value) =>
        {
            if (!value)
            {
                throw new InvalidOperationException("Restauration temporairement indisponible.");
            }
        };

        Invoke("StartPlacementMode");

        Assert.IsFalse(GetField<bool>("_placementMode"));
        Assert.IsNull(World.RenderingCamera);
        Assert.IsTrue(GetField<bool>("_playerInvincibilityRestorePending"));
        Assert.IsTrue(GetField<bool>("_placementPlayerStateStored"));
        StubRuntime.InvincibilityWriteHandler = null;
        StubRuntime.InvincibilityReadHandler = null;
        Invoke("MaintainPlayerInvincibilityProtection");
        Invoke("MaintainPlacementPlayerStateRecovery");
        AssertSessionReleased();
        Assert.IsFalse(Game.Player.Character.IsInvincible);
        Invoke("StartPlacementMode");
        AssertCameraActive();
    }

    [TestMethod]
    public void UnverifiableTrueBaseline_DoesNotDiscardRestoration()
    {
        Game.Player.Character.IsInvincible = true;
        Invoke("StartPlacementMode");
        AssertCameraActive();
        StubRuntime.InvincibilityReadHandler = (entity, actual) => false;

        Invoke("UpdatePlacementMode");

        Assert.IsFalse(GetField<bool>("_placementMode"));
        Assert.IsNull(World.RenderingCamera);
        Assert.IsTrue(GetField<bool>("_playerInvincibilityRestorePending"));
        Assert.IsTrue(GetField<bool>("_placementPlayerStateStored"));
        StubRuntime.InvincibilityReadHandler = null;
        Invoke("MaintainPlayerInvincibilityProtection");
        Invoke("MaintainPlacementPlayerStateRecovery");
        AssertSessionReleased();
        Assert.IsTrue(Game.Player.Character.IsInvincible);
    }

    [TestMethod]
    public void CompatibilityMode_StillHonorsJusticeBeforeAndDuringPlacement()
    {
        StubRuntime.InvincibilityReadHandler = (entity, actual) => false;
        SetField("_justiceLegalReleaseFinalizationPending", true);
        Invoke("StartPlacementMode");
        AssertSessionReleased();

        SetField("_justiceLegalReleaseFinalizationPending", false);
        Invoke("StartPlacementMode");
        AssertCameraActive();
        SetField("_justiceLegalReleaseFinalizationPending", true);
        Invoke("UpdatePlacementMode");

        AssertSessionReleased();
        Assert.IsFalse(Game.Player.Character.FreezePosition);
        SetField("_justiceLegalReleaseFinalizationPending", false);
    }

    [TestMethod]
    public void PlayerReplacement_RestoresOriginalPedAndStopsCamera()
    {
        Ped original = Game.Player.Character;
        Invoke("StartPlacementMode");
        AssertCameraActive();
        Ped replacement = new Ped { Handle = 108 };
        Game.Player.Character = replacement;

        Invoke("UpdatePlacementMode");

        AssertSessionReleased();
        Assert.IsFalse(original.IsInvincible);
        Assert.IsFalse(original.FreezePosition);
        Assert.IsFalse(replacement.IsInvincible);
        Assert.IsFalse(replacement.FreezePosition);
    }

    [TestMethod]
    public void RepeatedPlacementTickExceptions_CleanUpEvenWhenLoggingIsThrottled()
    {
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == (ulong)Hash.HIDE_HUD_AND_RADAR_THIS_FRAME)
            {
                throw new InvalidOperationException("Erreur injectée pendant le placement.");
            }
            return null;
        };

        // Même GameTime : la seconde erreur peut être filtrée par le logger,
        // mais sa caméra et son état joueur doivent quand même être restaurés.
        for (int i = 0; i < 2; i++)
        {
            Invoke("StartPlacementMode");
            AssertCameraActive();
            RaiseScriptEvent("RaiseTick");
            AssertSessionReleased();
            Assert.IsFalse(Game.Player.Character.FreezePosition);
            Assert.IsFalse(Game.Player.Character.IsInvincible);
        }
    }

    private static void SeedCanonicalJusticeState(string directory)
    {
        string emptyCustody = (string)typeof(DonJEnemySpawner).GetMethod(
            "CreateCanonicalEmptyJusticeCustodyXml",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>();
        for (int slot = 0; slot < 3; slot++)
        {
            profiles.Add(new JusticePersistenceProfileSnapshot(
                slot,
                1L,
                "slot:" + slot.ToString(CultureInfo.InvariantCulture) +
                ":model:0",
                new[]
                {
                    new JusticePersistenceField("pendingDeathCapture", "false"),
                    new JusticePersistenceField(
                        "pendingDeathCapturePlayerSlot",
                        "-1"),
                    new JusticePersistenceField(
                        "pendingDeathCapturePlayerModel",
                        "0"),
                    new JusticePersistenceField(
                        "pendingAmnestyWantedClear",
                        "false"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseFinalization",
                        "false"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseSite",
                        "0"),
                    new JusticePersistenceField(
                        "pendingLegalReleaseSelectedWeapon",
                        "0"),
                    new JusticePersistenceField(
                        "lastCanonicalPlayerModel",
                        "0"),
                    new JusticePersistenceField(
                        "Case",
                        "<Case enabled=\"false\" />"),
                    new JusticePersistenceField("Record", "<Record />"),
                    new JusticePersistenceField("Custody", emptyCustody)
                }));
        }

        JusticePersistenceSnapshot snapshot = new JusticePersistenceSnapshot(
            1L,
            JusticeXmlPersistenceCodec.SchemaMajor,
            DateTime.UtcNow.Ticks,
            0,
            new[]
            {
                new JusticePersistenceField("activePlayerSlot", "0"),
                new JusticePersistenceField("sentencePolicyVersion", "2"),
                new JusticePersistenceField("policyResetRecoveryMask", "0"),
                new JusticePersistenceField("nextIdentityGeneration", "0"),
                new JusticePersistenceField("policeIntegrationMode", "1"),
                new JusticePersistenceField("lastCanonicalPlayerSlot", "0"),
                new JusticePersistenceField("lastCanonicalPlayerModel", "0")
            },
            profiles);
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        byte[] document = codec.Serialize(snapshot);
        JusticePersistenceSnapshot decoded;
        string decodeError;
        Assert.IsTrue(
            codec.TryDeserialize(document, out decoded, out decodeError),
            decodeError);
        string semanticError;
        Assert.IsTrue(
            DonJEnemySpawner.TryValidateJusticePersistenceSnapshotSemantics(
                decoded,
                out semanticError),
            semanticError);
        string primaryPath = Path.Combine(directory, "_justice_state.xml");
        File.WriteAllBytes(primaryPath, document);
        // Je fournis aussi la copie redondante attendue par le contrat v2 : le
        // scénario ne doit pas démarrer dans une réparation de policy étrangère.
        File.WriteAllBytes(primaryPath + ".bak", document);
    }

    private void AssertCameraActive()
    {
        Assert.IsTrue(GetField<bool>("_placementMode"));
        Camera camera = GetField<Camera>("_placementCamera");
        Assert.IsTrue(Camera.Exists(camera));
        Assert.AreSame(camera, World.RenderingCamera);
    }

    private void AssertSessionReleased()
    {
        Assert.IsFalse(GetField<bool>("_placementMode"));
        Assert.IsNull(GetField<Camera>("_placementCamera"));
        Assert.IsNull(World.RenderingCamera);
        Assert.IsFalse(GetField<bool>("_placementPlayerStateStored"));
        Assert.IsFalse(GetField<bool>("_playerInvincibilityBaselineCaptured"));
        Assert.IsFalse(GetField<bool>("_playerInvincibilityRestorePending"));
        Assert.AreEqual(0, Convert.ToInt32(GetField<object>("_playerInvincibilityOwners")));
    }

    private object Invoke(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(DonJEnemySpawner).GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Méthode introuvable : " + methodName);
        return method.Invoke(_script, arguments);
    }

    private T GetField<T>(string fieldName)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ introuvable : " + fieldName);
        return (T)field.GetValue(_script);
    }

    private void SetField(string fieldName, object value)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ introuvable : " + fieldName);
        field.SetValue(_script, value);
    }

    private void RaiseScriptEvent(string methodName)
    {
        MethodInfo method = typeof(Script).GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Événement du stub introuvable : " + methodName);
        method.Invoke(_script, new object[0]);
    }
}
#endif
