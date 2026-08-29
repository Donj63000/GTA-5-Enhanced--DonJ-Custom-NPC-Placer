using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeTypedCustodyPersistenceTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void TypedCustodySerializer_ProducesTheCompleteParseableCustodyContract()
    {
        JusticeFineDebitPersistenceSnapshot fine =
            new JusticeFineDebitPersistenceSnapshot(
                "custody:fine",
                1,
                900L,
                true,
                -5L,
                450,
                1500,
                1050,
                120,
                180,
                true,
                true,
                1,
                (int)JusticePaymentResolution.Attempted,
                -3L,
                450L,
                -2L);
        JusticeVoluntaryPaymentPersistenceSnapshot voluntary =
            new JusticeVoluntaryPaymentPersistenceSnapshot(
                "payment:one",
                1,
                800L,
                200,
                1000,
                800,
                20L,
                30L,
                true,
                40L,
                1,
                (int)JusticePaymentResolution.Confirmed,
                0L,
                true);
        JusticeDisciplinePersistenceSnapshot discipline =
            new JusticeDisciplinePersistenceSnapshot(
                "incident:discipline",
                (int)JusticeCrimeKind.AssaultOfficer,
                45);
        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(
                true,
                1234,
                new[]
                {
                    new JusticeWeaponPersistenceSnapshot(
                        1234,
                        80,
                        12,
                        3,
                        new[] { 5001, 5002 })
                });
        JusticeCustodyPersistenceSnapshot snapshot =
            new JusticeCustodyPersistenceSnapshot(
                true,
                1,
                true,
                true,
                300,
                20,
                true,
                false,
                4,
                2,
                1,
                false,
                false,
                false,
                false,
                true,
                true,
                false,
                98765,
                1,
                1234,
                true,
                true,
                fine,
                voluntary,
                discipline,
                inventory,
                true,
                new[]
                {
                    new JusticeActivityCooldownPersistenceSnapshot("exercise", 17)
                });

        string serialized =
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(snapshot);
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(serialized);

        XmlElement root = document.DocumentElement;
        Assert.IsNotNull(root);
        Assert.AreEqual("Custody", root.Name);
        Assert.AreEqual("true", root.GetAttribute("active"));
        Assert.AreEqual("MissionRow", root.GetAttribute("site"));
        Assert.AreEqual("4", root.GetAttribute("inventoryState"));
        Assert.AreEqual("false", root.GetAttribute("storedInvincible"));
        Assert.AreEqual("false", root.GetAttribute("storedFrozen"));
        Assert.AreEqual("true", root.GetAttribute("storedCanRagdoll"));
        Assert.AreEqual(1, root.SelectNodes("FineDebitIntent").Count);
        Assert.AreEqual(1, root.SelectNodes("VoluntaryFinePaymentIntent").Count);
        Assert.AreEqual(1, root.SelectNodes("DisciplineIntent").Count);
        Assert.AreEqual(1, root.SelectNodes("InventorySnapshot/Weapon").Count);
        Assert.AreEqual(2, root.SelectNodes("InventorySnapshot/Weapon/Component").Count);
        Assert.AreEqual("0", ((XmlElement)root.SelectSingleNode("FineDebitIntent"))
            .GetAttribute("preparedAtUtcTicks"));
        Assert.AreEqual("Attempted", ((XmlElement)root.SelectSingleNode("FineDebitIntent"))
            .GetAttribute("resolution"));
        Assert.AreEqual("17", ((XmlElement)root.SelectSingleNode(
            "ActivityCooldowns/Cooldown[@id='exercise']"))
            .GetAttribute("remainingSeconds"));
    }

    [TestMethod]
    public void TypedCustodyCapture_DetachesRuntimeIntentsWeaponsComponentsAndCooldowns()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Dictionary<string, int> cooldowns =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "exercise", 2500 }
            };
        SetField(script, "_justiceActivityCooldownUntil", cooldowns);

        object item = CreateNested("JusticeWeaponSnapshotItem");
        SetNestedField(item, "WeaponHash", 111);
        SetNestedField(item, "Ammo", 60);
        SetNestedField(item, "AmmoInClip", 10);
        SetNestedField(item, "Tint", 2);
        IList components = (IList)GetNestedField(item, "ComponentHashes");
        components.Add(7001);

        object inventory = CreateNested("JusticeWeaponSnapshot");
        SetNestedField(inventory, "IsValidated", true);
        SetNestedField(inventory, "SelectedWeaponHash", 111);
        IList weapons = (IList)GetNestedField(inventory, "Weapons");
        weapons.Add(item);
        SetField(script, "_justiceWeaponSnapshot", inventory);

        object fine = CreateNested("JusticeFineDebitIntent");
        SetNestedField(fine, "EpisodeId", "before:fine");
        SetNestedField(fine, "Slot", 0);
        SetNestedField(fine, "FineAmount", 250L);
        SetField(script, "_justiceFineDebitIntent", fine);

        object voluntary = CreateNested("JusticeVoluntaryFinePaymentIntent");
        SetNestedField(voluntary, "PaymentId", "before:payment");
        SetNestedField(voluntary, "Slot", 0);
        SetNestedField(voluntary, "FineBefore", 400L);
        SetField(script, "_justiceVoluntaryFinePaymentIntent", voluntary);

        object discipline = CreateNested("JusticeDisciplineIntent");
        SetNestedField(discipline, "IncidentId", "before:discipline");
        SetNestedField(discipline, "PenaltySeconds", 15);
        SetField(script, "_justiceDisciplineIntent", discipline);

        JusticeCustodyPersistenceSnapshot captured =
            InvokeDeterministicCapture(script, 1000);
        string beforeMutation =
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(captured);

        SetNestedField(item, "WeaponHash", 222);
        components.Add(7002);
        weapons.Clear();
        cooldowns.Clear();
        SetNestedField(fine, "EpisodeId", "after:fine");
        SetNestedField(voluntary, "PaymentId", "after:payment");
        SetNestedField(discipline, "IncidentId", "after:discipline");

        string afterMutation =
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(captured);
        Assert.AreEqual(beforeMutation, afterMutation);

        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(afterMutation);
        XmlElement root = document.DocumentElement;
        Assert.AreEqual("before:fine", ((XmlElement)root.SelectSingleNode("FineDebitIntent"))
            .GetAttribute("episodeId"));
        Assert.AreEqual("before:payment", ((XmlElement)root.SelectSingleNode(
            "VoluntaryFinePaymentIntent")).GetAttribute("paymentId"));
        Assert.AreEqual("before:discipline", ((XmlElement)root.SelectSingleNode(
            "DisciplineIntent")).GetAttribute("incidentId"));
        Assert.AreEqual("111", ((XmlElement)root.SelectSingleNode(
            "InventorySnapshot/Weapon")).GetAttribute("hash"));
        Assert.AreEqual(1, root.SelectNodes("InventorySnapshot/Weapon/Component").Count);
        Assert.AreEqual("2", ((XmlElement)root.SelectSingleNode(
            "ActivityCooldowns/Cooldown[@id='exercise']"))
            .GetAttribute("remainingSeconds"));
    }

    [TestMethod]
    public void TypedCustodyDtoGraph_ExposesOnlyImmutableDetachedValues()
    {
        Type[] dtoTypes =
        {
            typeof(JusticeCustodyPersistenceSnapshot),
            typeof(JusticeFineDebitPersistenceSnapshot),
            typeof(JusticeVoluntaryPaymentPersistenceSnapshot),
            typeof(JusticeDisciplinePersistenceSnapshot),
            typeof(JusticeInventoryPersistenceSnapshot),
            typeof(JusticeWeaponPersistenceSnapshot),
            typeof(JusticeActivityCooldownPersistenceSnapshot)
        };

        foreach (Type dtoType in dtoTypes)
        {
            Assert.IsTrue(dtoType.IsSealed, dtoType.FullName + " doit rester scellé.");
            foreach (PropertyInfo property in dtoType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(
                    property.CanWrite,
                    dtoType.Name + "." + property.Name + " ne doit pas exposer de setter.");
                AssertSafePersistenceType(property.PropertyType, dtoType.Name + "." + property.Name);
            }

            foreach (FieldInfo field in dtoType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(
                    field.FieldType.IsArray || IsGenericType(field.FieldType, typeof(List<>)) ||
                    IsGenericType(field.FieldType, typeof(Dictionary<,>)),
                    dtoType.Name + "." + field.Name + " conserve une collection modifiable.");
                AssertSafePersistenceType(field.FieldType, dtoType.Name + "." + field.Name);
            }
        }

        JusticeWeaponPersistenceSnapshot weapon =
            new JusticeWeaponPersistenceSnapshot(1, 2, 3, 4, new[] { 5 });
        ICollection<int> components = (ICollection<int>)weapon.ComponentHashes;
        Assert.IsTrue(components.IsReadOnly);

        JusticeInventoryPersistenceSnapshot inventory =
            new JusticeInventoryPersistenceSnapshot(true, 1, new[] { weapon });
        ICollection<JusticeWeaponPersistenceSnapshot> weapons =
            (ICollection<JusticeWeaponPersistenceSnapshot>)inventory.Weapons;
        Assert.IsTrue(weapons.IsReadOnly);
    }

    private static JusticeCustodyPersistenceSnapshot InvokeDeterministicCapture(
        object script,
        int gameTime)
    {
        MethodInfo method = ScriptType.GetMethod(
            "CaptureJusticeCustodyPersistenceSnapshot",
            PrivateInstance,
            null,
            new[] { typeof(int) },
            null);
        Assert.IsNotNull(method);
        return (JusticeCustodyPersistenceSnapshot)method.Invoke(
            script,
            new object[] { gameTime });
    }

    private static object CreateNested(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type imbriqué introuvable: " + name);
        return Activator.CreateInstance(type, true);
    }

    private static object GetNestedField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ imbriqué introuvable: " + name);
        return field.GetValue(target);
    }

    private static void SetNestedField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Champ imbriqué introuvable: " + name);
        field.SetValue(target, value);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = ScriptType.GetField(name, PrivateInstance);
        Assert.IsNotNull(field, "Champ script introuvable: " + name);
        field.SetValue(target, value);
    }

    private static bool IsGenericType(Type candidate, Type definition)
    {
        return candidate.IsGenericType &&
               candidate.GetGenericTypeDefinition() == definition;
    }

    private static void AssertSafePersistenceType(Type type, string owner)
    {
        string fullName = type.FullName ?? string.Empty;
        Assert.IsFalse(
            fullName.StartsWith("GTA", StringComparison.Ordinal) ||
            fullName.StartsWith("System.Xml", StringComparison.Ordinal),
            owner + " expose un type interdit: " + fullName);
        Assert.AreNotEqual(typeof(JusticeCaseState), type, owner);
        Assert.AreNotEqual(typeof(JusticeRecordState), type, owner);
        Assert.AreNotEqual(typeof(JusticePlayerProfileState), type, owner);

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            Assert.IsTrue(
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(ReadOnlyCollection<>),
                owner + " expose une collection non autorisée: " + fullName);
        }
    }
}
