#if DONJ_STUB_API
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using GTA;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class StubRuntimeBehaviorTests
{
    [TestInitialize]
    public void Initialize()
    {
        StubRuntime.Reset();
    }

    [TestMethod]
    public void InputArgument_UlongConserveLePointeur64Bits()
    {
        const ulong expected = 0xFEDCBA9876543210UL;
        InputArgument argument = new InputArgument(expected);
        PropertyInfo valueProperty = typeof(InputArgument).GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(valueProperty);
        Assert.AreEqual(expected, (ulong)valueProperty.GetValue(argument));
    }

    [TestMethod]
    public void FunctionCall_EnregistreLesNativesEtRetourneLaValeurConfiguree()
    {
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            Assert.AreEqual(0x1234UL, hash);
            Assert.AreEqual(2, arguments.Length);
            Assert.AreEqual(7, ReadInputArgumentValue(arguments[0]));
            Assert.AreEqual(false, ReadInputArgumentValue(arguments[1]));
            return true;
        };

        bool result = Function.Call<bool>((Hash)0x1234UL, 7, false);

        Assert.IsTrue(result);
        Assert.AreEqual(1, StubRuntime.NativeCalls.Count);
        Assert.AreEqual(0x1234UL, StubRuntime.NativeCalls[0].Hash);
    }

    [TestMethod]
    public void StubApi_ExposeUniquementLesSignaturesNib2116Consommees()
    {
        MethodInfo[] calls = typeof(Function).GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Call")
            .ToArray();

        Assert.AreEqual(36, calls.Length, "NIB v2.11.6 expose 18 surcharges génériques et 18 void.");
        Assert.IsFalse(
            calls.Any(method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(object[]) ||
                parameter.ParameterType == typeof(ulong))),
            "Le stub ne doit jamais permettre une référence Function.Call absente de NIB.");
        Assert.AreEqual(
            2,
            calls.Count(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Hash) &&
                       parameters[1].ParameterType == typeof(InputArgument[]) &&
                       parameters[1].GetCustomAttribute<ParamArrayAttribute>() != null;
            }),
            "Les variantes générique et void avec params InputArgument[] doivent exister.");

        Assert.IsFalse(typeof(InputArgument).IsSealed);
        Assert.AreEqual(typeof(InputArgument), typeof(OutputArgument).BaseType);
        Assert.IsFalse(typeof(OutputArgument).IsSealed);
        Assert.IsTrue(typeof(RaycastResult).IsValueType);
        Assert.IsFalse(typeof(RaycastResult).GetProperty("HitCoords").CanWrite);

        Assert.IsNull(typeof(Entity).GetProperty(
            "Speed",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.IsNotNull(typeof(Vehicle).GetProperty(
            "Speed",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.IsNotNull(typeof(Entity).GetProperty(
            "Health",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.IsNotNull(typeof(Entity).GetProperty(
            "Model",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.IsNotNull(typeof(Entity).GetMethod(
            "op_Equality",
            BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(typeof(Blip).GetMethod(
            "op_Equality",
            BindingFlags.Public | BindingFlags.Static));

        MethodInfo give = typeof(WeaponCollection).GetMethod(
            "Give",
            new[] { typeof(WeaponHash), typeof(int), typeof(bool), typeof(bool) });
        MethodInfo select = typeof(WeaponCollection).GetMethod(
            "Select",
            new[] { typeof(WeaponHash), typeof(bool) });
        Assert.IsNotNull(give);
        Assert.AreEqual(typeof(Weapon), give.ReturnType);
        Assert.IsNotNull(select);
        Assert.AreEqual(typeof(bool), select.ReturnType);
    }

    [TestMethod]
    public void StubApi_RespecteLesTypesSousJacentsEtLaFormeExacteDeScript()
    {
        Assert.AreEqual(typeof(ulong), Enum.GetUnderlyingType(typeof(Hash)));
        Assert.AreEqual(typeof(uint), Enum.GetUnderlyingType(typeof(PedHash)));
        Assert.AreEqual(typeof(uint), Enum.GetUnderlyingType(typeof(VehicleHash)));
        Assert.AreEqual(typeof(uint), Enum.GetUnderlyingType(typeof(WeaponHash)));

        ConstructorInfo constructor = typeof(Script).GetConstructor(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        Assert.IsNotNull(constructor);
        Assert.IsTrue(constructor.IsPublic, "Le chargeur NIB doit pouvoir appeler le constructeur public.");

        PropertyInfo interval = typeof(Script).GetProperty(
            "Interval",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        Assert.IsNotNull(interval);
        Assert.AreEqual(typeof(int), interval.PropertyType);

        MethodInfo getter = interval.GetGetMethod(true);
        MethodInfo setter = interval.GetSetMethod(true);
        Assert.IsNotNull(getter);
        Assert.IsNotNull(setter);

        // Je verrouille les deux accesseurs family de NIB 2.11.6 : rendre
        // seulement le setter protégé laisserait encore un getter public erroné.
        Assert.IsTrue(getter.IsFamily);
        Assert.IsFalse(getter.IsPublic);
        Assert.IsFalse(getter.IsStatic);
        Assert.AreEqual(typeof(int), getter.ReturnType);
        Assert.AreEqual(0, getter.GetParameters().Length);

        Assert.IsTrue(setter.IsFamily);
        Assert.IsFalse(setter.IsPublic);
        Assert.IsFalse(setter.IsStatic);
        Assert.AreEqual(typeof(void), setter.ReturnType);
        ParameterInfo[] setterParameters = setter.GetParameters();
        Assert.AreEqual(1, setterParameters.Length);
        Assert.AreEqual(typeof(int), setterParameters[0].ParameterType);
    }

    [TestMethod]
    public void WeaponCollection_RetourneLesTypesEtResultatsDuContratNib()
    {
        Ped ped = new Ped();

        Weapon weapon = ped.Weapons.Give(WeaponHash.Pistol, 42, true, true);

        Assert.IsNotNull(weapon);
        Assert.AreEqual(WeaponHash.Pistol, weapon.Hash);
        Assert.AreEqual(42, weapon.Ammo);
        Assert.IsTrue(weapon.IsPresent);
        Assert.IsTrue(ped.Weapons.Select(WeaponHash.Pistol));
        Assert.IsTrue(ped.Weapons.Select(WeaponHash.Pistol, true));
    }

    [TestMethod]
    public void MondeEtDegats_UtilisentLesScenariosConfigurables()
    {
        Ped player = new Ped { Handle = 10 };
        Ped witness = new Ped { Handle = 20 };
        StubRuntime.NearbyPeds = new[] { witness };
        StubRuntime.DamageHandler = (victim, attacker) =>
            victim.Handle == witness.Handle && attacker.Handle == player.Handle;
        StubRuntime.CombatHandler = (attacker, target) =>
            attacker.Handle == witness.Handle && target.Handle == player.Handle;

        CollectionAssert.AreEqual(new[] { witness }, World.GetNearbyPeds(player, 80.0f));
        Assert.IsTrue(witness.HasBeenDamagedBy(player));
        Assert.IsTrue(witness.IsInCombatAgainst(player));
    }

    [TestMethod]
    public void InventaireDlc_EcritTouteLaStructureNativeSansOutputArgument()
    {
        const ulong getCount = 0xEE47635F352DA367UL;
        const ulong getData = 0x79923CD21BECE14EUL;
        const int expectedWeaponHash = unchecked((int)0xF00DBAAD);
        bool wroteLastByte = false;
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == getCount)
            {
                return 1;
            }
            if (hash != getData)
            {
                return null;
            }

            Assert.AreEqual(2, arguments.Length);
            InputArgument pointerArgument = (InputArgument)arguments[1];
            PropertyInfo valueProperty = typeof(InputArgument).GetProperty(
                "Value",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ulong pointerValue = (ulong)valueProperty.GetValue(pointerArgument);
            IntPtr pointer = new IntPtr(unchecked((long)pointerValue));
            Assert.AreNotEqual(IntPtr.Zero, pointer);
            Marshal.WriteInt32(pointer, 8, expectedWeaponHash);
            Marshal.WriteByte(pointer, 311, 0x5A);
            wroteLastByte = Marshal.ReadByte(pointer, 311) == 0x5A;
            return true;
        };

        object script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        MethodInfo collect = typeof(DonJEnemySpawner).GetMethod(
            "TryCollectJusticeWeaponHashes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        HashSet<int> seen = new HashSet<int>();
        List<int> hashes = new List<int>();

        bool success = (bool)collect.Invoke(script, new object[] { seen, hashes });

        Assert.IsTrue(success);
        Assert.IsTrue(wroteLastByte, "Le backend doit pouvoir écrire jusqu'à l'octet 311.");
        CollectionAssert.Contains(hashes, expectedWeaponHash);
    }

    [TestMethod]
    public void ArrestationPedCustom_ConserveLIdentiteLieeSansAdopterUnAutreHeros()
    {
        object script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        Ped customPlayer = new Ped
        {
            Handle = 44,
            Model = new Model(123)
        };
        Game.Player.Character = customPlayer;
        SetPrivateField(script, "_justiceLastCanonicalPlayerSlot", 1);
        SetPrivateField(script, "_justiceCustodyPlayerSlot", -1);

        MethodInfo bind = typeof(DonJEnemySpawner).GetMethod(
            "TryBindJusticeCustodyPlayerIdentityForCapture",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo validate = typeof(DonJEnemySpawner).GetMethod(
            "IsJusticeCustodyPlayerIdentityCompatible",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(bind);
        Assert.IsNotNull(validate);
        Assert.IsTrue((bool)bind.Invoke(script, new object[] { customPlayer, false }));
        Assert.AreEqual(1, GetPrivateField<int>(script, "_justiceCustodyPlayerSlot"));
        Assert.IsTrue((bool)validate.Invoke(script, new object[] { customPlayer }));

        customPlayer.Handle = 45;
        Assert.IsFalse(
            (bool)validate.Invoke(script, new object[] { customPlayer }),
            "Un handle différent ne doit pas récupérer l'identité du ped custom capturé.");

        Game.Player.Character = new Ped
        {
            Handle = 46,
            Model = new Model(Game.GenerateHash("player_two"))
        };
        Assert.IsFalse(
            (bool)validate.Invoke(script, new object[] { Game.Player.Character }),
            "Trevor ne doit jamais adopter la détention liée au dernier slot Franklin.");
    }

    [TestMethod]
    public void Detention_DegèleLeJoueurEtRépareLeSnapshotAprèsLeTransfert()
    {
        object script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        Ped player = new Ped
        {
            Handle = 61,
            Model = new Model(Game.GenerateHash("player_one")),
            FreezePosition = true,
            IsInvincible = true,
            CanRagdoll = false
        };
        Game.Player.Character = player;
        SetPrivateField(script, "_justiceInitialized", true);
        SetPrivateField(script, "_justiceCaseState", new JusticeCaseState());
        SetPrivateField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetPrivateField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetPrivateField(script, "_justiceCustodyPlayerSlot", 1);
        SetPrivateField(script, "_justiceCustodyPlayerStateStored", true);
        SetPrivateField(script, "_justiceCustodyStoredFrozen", true);

        MethodInfo ensureMobility = typeof(DonJEnemySpawner).GetMethod(
            "EnsureJusticeCustodyPlayerMobility",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(ensureMobility);
        Assert.IsTrue((bool)ensureMobility.Invoke(script, new object[] { player }));
        Assert.IsFalse(player.FreezePosition);
        Assert.IsFalse(GetPrivateField<bool>(script, "_justiceCustodyStoredFrozen"));
        Assert.IsTrue(GetPrivateField<bool>(script, "_justiceStateDirty"));
        Assert.IsTrue(player.IsInvincible, "Le correctif de mobilité ne doit pas altérer l'invincibilité.");
        Assert.IsFalse(player.CanRagdoll, "Le correctif de mobilité ne doit pas altérer le ragdoll.");

        Assert.IsTrue((bool)ensureMobility.Invoke(script, new object[] { player }));
        Assert.IsFalse(player.FreezePosition, "Le dégel doit rester idempotent.");

        player.Handle = 62;
        player.Model = new Model(Game.GenerateHash("player_two"));
        player.FreezePosition = true;
        Assert.IsFalse((bool)ensureMobility.Invoke(script, new object[] { player }));
        Assert.IsTrue(
            player.FreezePosition,
            "Un autre ped ne doit jamais hériter de la réparation de mobilité du détenu.");
    }

    [TestMethod]
    public void RollbackDetention_NeRestaureJamaisLeGelTransitoireDeLArrestation()
    {
        object script = FormatterServices.GetUninitializedObject(typeof(DonJEnemySpawner));
        Ped player = new Ped
        {
            Handle = 71,
            Model = new Model(Game.GenerateHash("player_one")),
            FreezePosition = true,
            IsInvincible = true,
            CanRagdoll = false
        };
        Game.Player.Character = player;
        SetPrivateField(script, "_justiceInitialized", true);
        SetPrivateField(script, "_justiceCaseState", new JusticeCaseState());
        SetPrivateField(script, "_justiceCustodyPlayerHandle", player.Handle);
        SetPrivateField(script, "_justiceCustodyPlayerModelHash", player.Model.Hash);
        SetPrivateField(script, "_justiceCustodyPlayerSlot", 1);
        SetPrivateField(script, "_justiceCustodyPlayerStateStored", true);
        SetPrivateField(script, "_justiceCustodyStoredFrozen", true);
        SetPrivateField(script, "_justiceCustodyStoredInvincible", false);
        SetPrivateField(script, "_justiceCustodyStoredCanRagdoll", true);

        MethodInfo rollbackRestore = typeof(DonJEnemySpawner).GetMethod(
            "RestoreJusticeCustodyPlayerTransientStateForRollback",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(rollbackRestore);
        Assert.IsTrue((bool)rollbackRestore.Invoke(script, new object[] { player }));
        Assert.IsFalse(player.FreezePosition, "La remise en liberté technique doit toujours rendre le déplacement.");
        Assert.IsFalse(GetPrivateField<bool>(script, "_justiceCustodyStoredFrozen"));
        Assert.IsFalse(player.IsInvincible, "Les autres propriétés du snapshot restent restaurées normalement.");
        Assert.IsTrue(player.CanRagdoll);
    }

    private static void SetPrivateField(object instance, string name, object value)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ privé introuvable: " + name);
        field.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        FieldInfo field = typeof(DonJEnemySpawner).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ privé introuvable: " + name);
        return (T)field.GetValue(instance);
    }

    private static object ReadInputArgumentValue(object argument)
    {
        Assert.IsInstanceOfType(argument, typeof(InputArgument));
        PropertyInfo valueProperty = typeof(InputArgument).GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(valueProperty);
        return valueProperty.GetValue(argument);
    }
}
#endif
