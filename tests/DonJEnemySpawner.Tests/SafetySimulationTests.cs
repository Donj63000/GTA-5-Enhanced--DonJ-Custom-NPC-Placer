using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class SafetySimulationTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void HeadlessMainMenuSimulation_KeepsCriticalActionsAndSections()
    {
        object script = CreateInitializedHeadlessScript();

        SetFieldValue(script, "_mainMenuNpcExpanded", true);
        SetFieldValue(script, "_mainMenuVehicleExpanded", true);
        SetFieldValue(script, "_mainMenuObjectExpanded", true);
        SetFieldValue(script, "_mainMenuInteriorExpanded", true);
        SetFieldValue(script, "_mainMenuSaveExpanded", true);
        SetFieldValue(script, "_mainMenuCleanupExpanded", true);

        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        Dictionary<string, object> byAction = entries
            .Cast<object>()
            .GroupBy(entry => GetFieldValue<object>(entry, "Action").ToString())
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "PlacementType",
                "PrecisePlacement",
                "DistancePlacement",
                "PlacementDistance",
                "SectionNpc",
                "NpcCategory",
                "NpcModel",
                "NpcWeaponCategory",
                "NpcWeapon",
                "NpcWeaponEditor",
                "NpcHealth",
                "NpcArmor",
                "NpcBehavior",
                "NpcPatrolRadius",
                "NpcAutoRespawn",
                "SectionVehicle",
                "VehicleCategory",
                "VehicleModel",
                "VehicleAutoRespawn",
                "SectionObject",
                "ObjectCategory",
                "ObjectModel",
                "ObjectAutoRespawn",
                "SectionInterior",
                "InteriorCategory",
                "InteriorModel",
                "ExitActiveInfo",
                "ExitDestinationInfo",
                "SectionSave",
                "Save",
                "Load",
                "SectionCleanup",
                "CleanNpcs",
                "CleanVehicles",
                "CleanObjects",
                "CleanInteriorPortals",
                "TerminatorMode"
            },
            byAction.Keys.ToArray());

        AssertEntry(byAction["PlacementType"], "Type de placement", "NPC", "Primary", 0, true);
        AssertEntry(byAction["PrecisePlacement"], "Placement camera precis", "Ouvrir le placement fin", "PrimaryAction", 0, true);
        AssertEntry(byAction["DistancePlacement"], "Placement direct", "Placer a 200 m devant le joueur", "Action", 0, true);
        AssertEntry(byAction["PlacementDistance"], "Distance placement direct", "200 m", "Normal", 0, true);
        AssertEntry(byAction["NpcHealth"], "Sante NPC", "300", "Normal", 1, true);
        AssertEntry(byAction["NpcArmor"], "Armure NPC", "100", "Normal", 1, true);
        AssertEntry(byAction["NpcAutoRespawn"], "Reapparition auto", "Non", "Normal", 1, true);
        AssertEntry(byAction["VehicleAutoRespawn"], "Reapparition auto", "Non", "Normal", 1, true);
        AssertEntry(byAction["ObjectAutoRespawn"], "Reapparition auto", "Non", "Normal", 1, true);
        AssertEntry(byAction["CleanInteriorPortals"], "Nettoyer entrees/sorties", "Supprimer les reperes interieurs", "Danger", 1, true);
        AssertEntry(byAction["TerminatorMode"], "Mode Terminator", "DESACTIVE", "Normal", 0, true);
        Assert.AreEqual("TerminatorMode", GetFieldValue<object>(entries[entries.Count - 1], "Action").ToString());
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_DefaultSectionsStayCollapsedExceptNpc()
    {
        object script = CreateInitializedHeadlessScript();

        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        string[] actions = entries
            .Cast<object>()
            .Select(entry => GetFieldValue<object>(entry, "Action").ToString())
            .ToArray();

        CollectionAssert.Contains(actions, "SectionNpc");
        CollectionAssert.Contains(actions, "NpcModel");
        CollectionAssert.Contains(actions, "NpcAutoRespawn");
        CollectionAssert.Contains(actions, "SectionVehicle");
        CollectionAssert.Contains(actions, "SectionObject");
        CollectionAssert.Contains(actions, "SectionInterior");
        CollectionAssert.Contains(actions, "SectionSave");
        CollectionAssert.Contains(actions, "SectionCleanup");
        CollectionAssert.Contains(actions, "TerminatorMode");

        CollectionAssert.DoesNotContain(actions, "VehicleModel");
        CollectionAssert.DoesNotContain(actions, "ObjectModel");
        CollectionAssert.DoesNotContain(actions, "InteriorModel");
        CollectionAssert.DoesNotContain(actions, "Save");
        CollectionAssert.DoesNotContain(actions, "CleanNpcs");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_TerminatorModeReflectsActiveState()
    {
        object script = CreateInitializedHeadlessScript();

        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        object terminator = entries[FindActionIndex(entries, "TerminatorMode")];
        AssertEntry(terminator, "Mode Terminator", "DESACTIVE", "Normal", 0, true);

        SetFieldValue(script, "_terminatorModeEnabled", true);

        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        terminator = entries[FindActionIndex(entries, "TerminatorMode")];
        AssertEntry(terminator, "Mode Terminator", "ACTIVE - vision rouge T-800", "Primary", 0, true);
        Assert.AreEqual("TerminatorMode", GetFieldValue<object>(entries[entries.Count - 1], "Action").ToString());
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_TabSectionFocusSkipsDetailRows()
    {
        object script = CreateInitializedHeadlessScript();
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");

        Assert.AreEqual(0, GetFieldValue<int>(script, "_mainMenuIndex"));

        InvokeInstance(script, "MoveMainMenuSectionFocus", entries, 1);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, entries, "SectionNpc");

        InvokeInstance(script, "MoveMainMenuSectionFocus", entries, 1);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, entries, "SectionVehicle");

        InvokeInstance(script, "MoveMainMenuSectionFocus", entries, -1);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, entries, "SectionNpc");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_LeftRightOpenAndCloseSection()
    {
        object script = CreateInitializedHeadlessScript();
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");

        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, "SectionVehicle"));
        InvokeInstance(script, "ChangeMainMenuValue", 1);

        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        CollectionAssert.Contains(ActionNames(entries), "VehicleModel");

        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, "SectionVehicle"));
        InvokeInstance(script, "ChangeMainMenuValue", -1);

        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        CollectionAssert.DoesNotContain(ActionNames(entries), "VehicleModel");
    }

    [TestMethod]
    public void SafetyContracts_KeepStableGameplayInvariants()
    {
        Assert.AreEqual(121, Convert.ToInt32(GetStaticFieldValue<object>("MenuToggleKey")));
        Assert.AreEqual("F10", GetStaticFieldValue<string>("MenuToggleKeyLabel"));
        Assert.AreEqual(1, GetStaticFieldValue<int>("MinHealth"));
        Assert.AreEqual(5000, GetStaticFieldValue<int>("MaxHealth"));
        Assert.AreEqual(0, GetStaticFieldValue<int>("MinArmor"));
        Assert.AreEqual(200, GetStaticFieldValue<int>("MaxArmor"));
        Assert.AreEqual(25, GetStaticFieldValue<int>("MinDistance"));
        Assert.AreEqual(2500, GetStaticFieldValue<int>("MaxDistance"));
        Assert.AreEqual(25, GetStaticFieldValue<int>("DistanceStep"));
        Assert.AreEqual(1000, GetStaticFieldValue<int>("AutoRespawnCheckIntervalMs"));
        Assert.AreEqual(6000, GetStaticFieldValue<int>("AutoRespawnMinDelayMs"));
        Assert.AreEqual(15000, GetStaticFieldValue<int>("AutoRespawnRetryDelayMs"));
        Assert.AreEqual(3, GetStaticFieldValue<int>("AutoRespawnMaxPerTick"));
        Assert.AreEqual(220.0f, GetStaticFieldValue<float>("AutoRespawnLeaveDistance"), 0.001f);
        Assert.AreEqual(70.0f, GetStaticFieldValue<float>("AutoRespawnNearSafetyDistance"), 0.001f);
    }

    [TestMethod]
    public void SafetyContracts_SaveNamesAndBackupResolutionResistDangerousInputs()
    {
        Assert.AreEqual("maison.xml", InvokeStatic("NormalizeSaveFileName", ""));
        Assert.AreEqual("maison.xml", InvokeStatic("NormalizeSaveFileName", ".."));
        Assert.AreEqual("villa.xml", InvokeStatic("NormalizeSaveFileName", @"..\villa"));
        Assert.AreEqual("safe_name.xml", InvokeStatic("NormalizeSaveFileName", "safe*name"));

        string previousDirectory = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DonJSafety_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", tempDirectory);

            string backupPath = Path.Combine(tempDirectory, "mission.xml.bak");
            File.WriteAllText(backupPath, "<DonJEnemySpawnerSave />");

            object script = FormatterServices.GetUninitializedObject(ScriptType);
            object[] args = { "mission", null, null };

            bool resolved = (bool)InvokeInstance(script, "TryResolveSavePathForLoad", args);

            Assert.IsTrue(resolved);
            Assert.AreEqual(Path.GetFullPath(backupPath), Path.GetFullPath((string)args[1]));
            Assert.AreEqual(Path.GetFullPath(tempDirectory), Path.GetFullPath((string)args[2]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", previousDirectory);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void SafetyContracts_ProjectAndScriptKeepEndllValidationPipeline()
    {
        XDocument project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.csproj"));
        string projectXml = project.ToString(SaveOptions.DisableFormatting);

        StringAssert.Contains(projectXml, "CreateLocalEndll");
        StringAssert.Contains(projectXml, "DeployAsEndll");
        StringAssert.Contains(projectXml, "$(TargetDir)$(AssemblyName).ENdll");
        StringAssert.Contains(projectXml, "$(GtaScriptsDir)\\$(AssemblyName).ENdll");
        StringAssert.Contains(projectXml, "$(GtaScriptsDir)\\DonJEnemySpawner.ENdll");

        string safetyScript = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tools", "run-safety-checks.ps1"));
        StringAssert.Contains(safetyScript, "dotnet");
        StringAssert.Contains(safetyScript, "restore");
        StringAssert.Contains(safetyScript, "build");
        StringAssert.Contains(safetyScript, "test");
        StringAssert.Contains(safetyScript, "GtaScriptsDir");
        StringAssert.Contains(safetyScript, "DonJCustomNpcPlacer.ENdll");
        StringAssert.Contains(safetyScript, "DonJEnemySpawner.ENdll");
        StringAssert.Contains(safetyScript, "UseStubApi");
    }

    [TestMethod]
    public void SafetyContracts_CiRunsSameSuiteWithStubApi()
    {
        string workflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "safety.yml");
        Assert.IsTrue(File.Exists(workflowPath), "Le workflow CI de securite doit exister.");

        string workflow = File.ReadAllText(workflowPath);
        StringAssert.Contains(workflow, "windows-latest");
        StringAssert.Contains(workflow, "push");
        StringAssert.Contains(workflow, "pull_request");
        StringAssert.Contains(workflow, ".\\tools\\run-safety-checks.ps1 -Ci -UseStubApi");
    }

    private static object CreateInitializedHeadlessScript()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);

        object allModelOptions = InvokeStatic("BuildAllModelOptions");
        object allWeaponOptions = InvokeStatic("BuildAllWeaponOptions");
        object allVehicleOptions = InvokeStatic("BuildAllVehicleOptions");
        object allObjectOptions = InvokeStatic("BuildAllObjectOptions");

        SetFieldValue(script, "_allModelOptions", allModelOptions);
        SetFieldValue(script, "_modelCategories", InvokeStatic("BuildModelCategories", allModelOptions));
        SetFieldValue(script, "_modelOptions", InvokeStatic("BuildModelOptions"));
        SetFieldValue(script, "_allWeaponOptions", allWeaponOptions);
        SetFieldValue(script, "_weaponCategories", InvokeStatic("BuildWeaponCategories", allWeaponOptions));
        SetFieldValue(script, "_weaponOptions", InvokeStatic("BuildWeaponOptions"));
        SetFieldValue(script, "_allVehicleOptions", allVehicleOptions);
        SetFieldValue(script, "_vehicleCategories", InvokeStatic("BuildVehicleCategories", allVehicleOptions));
        SetFieldValue(script, "_allObjectOptions", allObjectOptions);
        SetFieldValue(script, "_objectCategories", InvokeStatic("BuildObjectCategories", allObjectOptions));
        SetFieldValue(script, "_interiorCategories", InvokeStatic("BuildInteriorCategories"));
        SetFieldValue(script, "_selectedPlacementType", Enum.Parse(GetNestedType("PlacementEntityType"), "Npc"));
        SetFieldValue(script, "_selectedHealth", 300);
        SetFieldValue(script, "_selectedArmor", 100);
        SetFieldValue(script, "_selectedDistance", 200);
        SetFieldValue(script, "_selectedPatrolRadius", 35);
        SetFieldValue(script, "_selectedBehavior", Enum.Parse(GetNestedType("NpcBehavior"), "Attacker"));
        SetFieldValue(script, "_selectedAutoRespawn", false);
        SetFieldValue(script, "_selectedWeaponLoadout", CreateStandardWeaponLoadout());
        SetFieldValue(script, "_lastSaveFileName", "maison.xml");
        SetFieldValue(script, "_mainMenuNpcExpanded", true);

        return script;
    }

    private static object CreateStandardWeaponLoadout()
    {
        object loadout = Activator.CreateInstance(GetNestedType("WeaponLoadout"), true);
        SetFieldValue(loadout, "Weapon", WeaponHash.CarbineRifle);
        SetFieldValue(loadout, "Ammo", 9999);
        SetFieldValue(loadout, "Tint", 0);
        SetFieldValue(loadout, "Preset", Enum.Parse(GetNestedType("WeaponUpgradePreset"), "Standard"));
        SetFieldValue(loadout, "Scope", Enum.Parse(GetNestedType("WeaponScopeMode"), "None"));
        SetFieldValue(loadout, "Mk2Ammo", Enum.Parse(GetNestedType("WeaponMk2AmmoMode"), "Standard"));
        return loadout;
    }

    private static void AssertEntry(object entry, string label, string value, string kind, int level, bool enabled)
    {
        Assert.AreEqual(label, GetFieldValue<string>(entry, "Label"));
        Assert.AreEqual(value, GetFieldValue<string>(entry, "Value"));
        Assert.AreEqual(kind, GetFieldValue<object>(entry, "Kind").ToString());
        Assert.AreEqual(level, GetFieldValue<int>(entry, "Level"));
        Assert.AreEqual(enabled, GetFieldValue<bool>(entry, "Enabled"));
    }

    private static void AssertSelectedAction(object script, IList entries, string expectedAction)
    {
        int selectedIndex = GetFieldValue<int>(script, "_mainMenuIndex");
        object selected = entries[selectedIndex];

        Assert.AreEqual(expectedAction, GetFieldValue<object>(selected, "Action").ToString());
    }

    private static int FindActionIndex(IList entries, string actionName)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(GetFieldValue<object>(entries[i], "Action").ToString(), actionName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        Assert.Fail("Action menu introuvable: " + actionName);
        return -1;
    }

    private static string[] ActionNames(IList entries)
    {
        return entries
            .Cast<object>()
            .Select(entry => GetFieldValue<object>(entry, "Action").ToString())
            .ToArray();
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateStatic);
        Assert.IsNotNull(method, $"La methode privee statique '{methodName}' est introuvable.");
        return method.Invoke(null, args);
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        MethodInfo[] matches = ScriptType
            .GetMethods(PrivateInstance)
            .Where(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .ToArray();

        Assert.AreEqual(1, matches.Length, $"La methode privee d'instance '{methodName}' doit avoir une seule surcharge avec {args.Length} argument(s).");
        MethodInfo method = matches[0];

        return method.Invoke(target, args);
    }

    private static T GetStaticFieldValue<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, $"Le champ prive statique '{fieldName}' est introuvable.");

        object rawValue = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return (T)rawValue;
    }

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Le champ '{fieldName}' est introuvable sur '{target.GetType().FullName}'.");
        return (T)field.GetValue(target);
    }

    private static void SetFieldValue(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Le champ '{fieldName}' est introuvable sur '{target.GetType().FullName}'.");
        field.SetValue(target, value);
    }

    private static Type GetNestedType(string nestedTypeName)
    {
        Type nestedType = ScriptType.GetNestedType(nestedTypeName, BindingFlags.NonPublic);
        Assert.IsNotNull(nestedType, $"Le type imbrique prive '{nestedTypeName}' est introuvable.");
        return nestedType;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "GTA5modDEV.sln");

            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du depot depuis le dossier de test.");
        return string.Empty;
    }
}
