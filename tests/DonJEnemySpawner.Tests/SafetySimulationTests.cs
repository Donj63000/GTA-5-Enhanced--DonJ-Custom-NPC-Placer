using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
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
    public void HeadlessMainMenuSimulation_ExposesExactActionsForEveryCategory()
    {
        object script = CreateInitializedHeadlessScript();

        AssertCategoryActions(
            script,
            "Npc",
            "PrecisePlacement", "DistancePlacement", "PlacementDistance", "NpcCategory", "NpcModel",
            "NpcWeaponCategory", "NpcWeapon", "NpcWeaponEditor", "NpcHealth", "NpcArmor", "NpcBehavior",
            "NpcPatrolRadius", "NpcAutoRespawn");
        AssertCategoryActions(
            script,
            "Vehicle",
            "PrecisePlacement", "DistancePlacement", "PlacementDistance", "VehicleCategory", "VehicleModel",
            "VehicleAutoRespawn");
        AssertCategoryActions(
            script,
            "Object",
            "PrecisePlacement", "DistancePlacement", "PlacementDistance", "ObjectCategory", "ObjectModel",
            "ObjectAutoRespawn");
        AssertCategoryActions(
            script,
            "Interior",
            "PlacementType", "PrecisePlacement", "DistancePlacement", "PlacementDistance", "InteriorCategory",
            "InteriorModel", "ExitActiveInfo", "ExitDestinationInfo");
        AssertCategoryActions(script, "Scene", "Save", "Load");
        AssertCategoryActions(
            script,
            "Justice",
            "JusticeEnabled", "JusticeProfile", "JusticeStatus", "JusticeLastCrime", "JusticeSeverity",
            "JusticeWarrant", "JusticeRecognitionStatus", "JusticeRecognitionPlate",
            "JusticeRecognitionOutfit", "JusticeRecognitionWarrant", "JusticeRecognitionDistance",
            "JusticeCharges", "JusticeRecord", "JusticeFine", "JusticeFineDispute",
            "JusticePayFine", "JusticeResolveFineDispute", "JusticeSentence", "JusticeRecidivism",
            "JusticePoliceMode", "JusticeRecovery", "JusticeDiagnostic", "JusticeResetProfile");
        AssertCategoryActions(
            script,
            "Tools",
            "TerminatorMode", "CleanNpcs", "CleanVehicles", "CleanObjects", "CleanInteriorPortals");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_CategorySelectionMapsPlacementTypes()
    {
        object script = CreateInitializedHeadlessScript();

        AssertCategoryMapsPlacementType(script, "Vehicle", "Vehicle");
        AssertCategoryMapsPlacementType(script, "Object", "Object");
        AssertCategoryMapsPlacementType(script, "Npc", "Npc");

        SetFieldValue(script, "_selectedPlacementType", Enum.Parse(GetNestedType("PlacementEntityType"), "Entrance"));
        SetMenuCategory(script, "Interior");
        Assert.AreEqual(
            "Entrance",
            GetFieldValue<object>(script, "_selectedPlacementType").ToString(),
            "La categorie Interieurs doit conserver le choix explicite Entree / Sortie.");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_RemembersSelectionByActionForEachCategory()
    {
        object script = CreateInitializedHeadlessScript();

        SetMenuCategory(script, "Npc");
        IList npcEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(npcEntries, "NpcArmor"));

        SetMenuCategory(script, "Vehicle");
        IList vehicleEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(vehicleEntries, "VehicleModel"));

        SetMenuCategory(script, "Npc");
        npcEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, npcEntries, "NpcArmor");

        SetMenuCategory(script, "Vehicle");
        vehicleEntries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, vehicleEntries, "VehicleModel");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_JusticeProfileCyclesWithLeftAndRight()
    {
        object script = CreateInitializedHeadlessScript();
        SetMenuCategory(script, "Justice");
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        int profileIndex = FindActionIndex(entries, "JusticeProfile");
        SetFieldValue(script, "_mainMenuIndex", profileIndex);
        string initialProfile = GetFieldValue<string>(entries[profileIndex], "Value");

        InvokeInstance(script, "ChangeMainMenuValue", 1, entries);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        string nextProfile = GetFieldValue<string>(entries[profileIndex], "Value");
        Assert.AreNotEqual(initialProfile, nextProfile);

        InvokeInstance(script, "ChangeMainMenuValue", -1, entries);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        Assert.AreEqual(initialProfile, GetFieldValue<string>(entries[profileIndex], "Value"));
        AssertSelectedAction(script, entries, "JusticeProfile");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_DangerActionRequiresConfirmationAndCanBeCancelled()
    {
        object script = CreateInitializedHeadlessScript();
        SetMenuCategory(script, "Tools");
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, "CleanNpcs"));

        InvokeInstance(script, "ActivateMainMenuItem", entries);

        object pendingAction = GetFieldValue<object>(script, "_pendingDangerAction");
        Assert.IsNotNull(pendingAction, "Le premier appui doit seulement armer la confirmation.");
        Assert.AreEqual("CleanNpcs", pendingAction.ToString());

        InvokeInstance(script, "CancelPendingDangerAction");

        Assert.IsNull(GetFieldValue<object>(script, "_pendingDangerAction"));

        InvokeInstance(script, "ActivateMainMenuItem", entries);
        InvokeMainMenuKey(script, Keys.Enter);
        InvokeMainMenuKey(script, Keys.Enter);

        Assert.AreEqual("CleanNpcs", GetFieldValue<object>(script, "_pendingDangerAction").ToString());
        Assert.IsTrue(GetFieldValue<bool>(script, "_dangerConfirmationRequiresEnterRelease"));

        InvokeInstance(script, "OnKeyUp", null, new KeyEventArgs(Keys.Enter));
        Assert.IsFalse(GetFieldValue<bool>(script, "_dangerConfirmationRequiresEnterRelease"));

        InvokeMainMenuKey(script, Keys.Enter);

        Assert.IsNull(GetFieldValue<object>(script, "_pendingDangerAction"));
        StringAssert.Contains(GetFieldValue<string>(script, "_statusText"), "Nettoyage NPC: 0 supprime(s).");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_DangerLatchAlsoRequiresNumpad5Release()
    {
        object script = CreateInitializedHeadlessScript();
        ArmDangerConfirmation(script, "CleanObjects");

        InvokeMainMenuKey(script, Keys.NumPad5);
        InvokeMainMenuKey(script, Keys.NumPad5);
        Assert.AreEqual("CleanObjects", GetFieldValue<object>(script, "_pendingDangerAction").ToString());

        InvokeInstance(script, "OnKeyUp", null, new KeyEventArgs(Keys.NumPad5));
        InvokeMainMenuKey(script, Keys.NumPad5);

        Assert.IsNull(GetFieldValue<object>(script, "_pendingDangerAction"));
        StringAssert.Contains(GetFieldValue<string>(script, "_statusText"), "Nettoyage objets: 0 supprime(s).");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_TerminatorModeReflectsActiveState()
    {
        object script = CreateInitializedHeadlessScript();
        SetMenuCategory(script, "Tools");

        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        object terminator = entries[FindActionIndex(entries, "TerminatorMode")];
        Assert.AreEqual("DESACTIVE", GetFieldValue<string>(terminator, "Value"));

        SetFieldValue(script, "_terminatorModeEnabled", true);

        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        terminator = entries[FindActionIndex(entries, "TerminatorMode")];
        Assert.AreEqual("ACTIVE - vision rouge T-800", GetFieldValue<string>(terminator, "Value"));
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_ResponsiveLayoutStaysInsideSafeCanvas()
    {
        int[,] resolutions =
        {
            { 1280, 720 },
            { 1920, 1200 },
            { 2560, 1080 },
            { 3840, 2160 }
        };
        float[] safeZones = { 0.80f, 0.90f, 1.0f };
        float[] referenceRailWidths = new float[safeZones.Length];
        float[] referenceContentWidths = new float[safeZones.Length];
        float[] referenceDetailsWidths = new float[safeZones.Length];

        AssertCompactThreshold("IsCompactMenuRail", 124);
        AssertCompactThreshold("IsCompactMenuContent", 430);
        AssertCompactThreshold("IsCompactMenuDetails", 210);
        AssertCompactThreshold("IsCompactMenuFooter", 650);

        for (int resolutionIndex = 0; resolutionIndex < resolutions.GetLength(0); resolutionIndex++)
        {
            for (int safeZoneIndex = 0; safeZoneIndex < safeZones.Length; safeZoneIndex++)
            {
                object layout = AssertResponsiveLayout(
                    resolutions[resolutionIndex, 0],
                    resolutions[resolutionIndex, 1],
                    safeZones[safeZoneIndex]);
                object rail = GetMemberValue(layout, "Rail");
                object content = GetMemberValue(layout, "Content");
                object details = GetMemberValue(layout, "Details");
                object footer = GetMemberValue(layout, "Footer");
                float uiToLogical = Convert.ToSingle(GetMemberValue(layout, "LogicalWidth")) / 1280.0f;
                float logicalRailWidth = RectangleWidth(rail) * uiToLogical;
                float logicalContentWidth = RectangleWidth(content) * uiToLogical;
                float logicalDetailsWidth = RectangleWidth(details) * uiToLogical;

                Assert.AreEqual(RectangleWidth(rail) < 124.0f, (bool)InvokeStatic("IsCompactMenuRail", (int)RectangleWidth(rail)));
                Assert.AreEqual(RectangleWidth(content) < 430.0f, (bool)InvokeStatic("IsCompactMenuContent", (int)RectangleWidth(content)));
                Assert.AreEqual(RectangleWidth(details) < 210.0f, (bool)InvokeStatic("IsCompactMenuDetails", (int)RectangleWidth(details)));
                Assert.AreEqual(RectangleWidth(footer) < 650.0f, (bool)InvokeStatic("IsCompactMenuFooter", (int)RectangleWidth(footer)));

                if (resolutionIndex == 0)
                {
                    referenceRailWidths[safeZoneIndex] = logicalRailWidth;
                    referenceContentWidths[safeZoneIndex] = logicalContentWidth;
                    referenceDetailsWidths[safeZoneIndex] = logicalDetailsWidth;
                }
                else
                {
                    Assert.AreEqual(referenceRailWidths[safeZoneIndex], logicalRailWidth, 2.0f);
                    Assert.AreEqual(referenceContentWidths[safeZoneIndex], logicalContentWidth, 2.0f);
                    Assert.AreEqual(referenceDetailsWidths[safeZoneIndex], logicalDetailsWidth, 2.0f);
                }
            }
        }

        AssertResponsiveLayout(0, 0, -1.0f);
        AssertResponsiveLayout(8000, 600, 2.0f);
    }

    [TestMethod]
    public void HeadlessJusticeHud_StaysInsideSafeZoneAtSupportedResolutions()
    {
        int[,] resolutions =
        {
            { 1280, 720 },
            { 1920, 1200 },
            { 2560, 1080 },
            { 3840, 2160 }
        };
        float[] safeZones = { 0.90f, 0.95f, 1.00f };

        for (int resolutionIndex = 0; resolutionIndex < resolutions.GetLength(0); resolutionIndex++)
        {
            for (int safeIndex = 0; safeIndex < safeZones.Length; safeIndex++)
            {
                int width = resolutions[resolutionIndex, 0];
                int height = resolutions[resolutionIndex, 1];
                float safeZone = safeZones[safeIndex];
                Rectangle hud = (Rectangle)InvokeStatic("CalculateJusticeHudBounds", width, height, safeZone);
                object layout = InvokeStatic("CalculateMenuLayout", width, height, safeZone);
                Rectangle safeBounds = (Rectangle)GetMemberValue(layout, "SafeBounds");

                Assert.IsTrue(hud.Width > 0 && hud.Height > 0);
                Assert.IsTrue(hud.Left >= safeBounds.Left && hud.Top >= safeBounds.Top);
                Assert.IsTrue(hud.Right <= safeBounds.Right && hud.Bottom <= safeBounds.Bottom,
                    width + "x" + height + " safe=" + safeZone);
            }
        }
    }

    [TestMethod]
    public void HeadlessJusticeLedger_ScrollbarTracksItsOwnBoundedOffset()
    {
        Assert.AreEqual(100, InvokeStatic("ComputeMenuScrollbarThumbY", 100, 200, 40, 20, 5, 0));
        Assert.AreEqual(174, InvokeStatic("ComputeMenuScrollbarThumbY", 100, 200, 40, 20, 5, 7));
        Assert.AreEqual(260, InvokeStatic("ComputeMenuScrollbarThumbY", 100, 200, 40, 20, 5, 15));
        Assert.AreEqual(100, InvokeStatic("ComputeMenuScrollbarThumbY", 100, 200, 40, 20, 5, -10));
        Assert.AreEqual(260, InvokeStatic("ComputeMenuScrollbarThumbY", 100, 200, 40, 20, 5, 99));
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_PageModelIsCachedAndRefreshedInPlace()
    {
        object script = CreateInitializedHeadlessScript();
        SetMenuCategory(script, "Npc");

        IList first = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        IList second = (IList)InvokeInstance(script, "BuildMainMenuEntries");

        Assert.AreSame(first, second, "La page ne doit pas recreer sa liste a chaque frame.");
        Assert.AreSame(first[0], second[0], "Les lignes doivent etre reutilisees apres le prechauffage.");

        SetFieldValue(script, "_selectedDistance", 425);
        IList refreshed = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        object distanceEntry = refreshed[FindActionIndex(refreshed, "PlacementDistance")];

        Assert.AreSame(first, refreshed);
        Assert.AreEqual("425 m", GetFieldValue<string>(distanceEntry, "Value"));
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_AnimationsRemainBoundedAndMonotonic()
    {
        Assert.AreEqual(160, GetStaticFieldValue<int>("MenuOpenAnimationMs"));
        Assert.AreEqual(120, GetStaticFieldValue<int>("MenuCategoryAnimationMs"));
        Assert.AreEqual(100, GetStaticFieldValue<int>("MenuSelectionAnimationMs"));
        Assert.AreEqual(1.0f, (float)InvokeStatic("AdvanceMenuAnimation", 0.95f, true, 100, 160), 0.0001f);
        Assert.AreEqual(0.0f, (float)InvokeStatic("AdvanceMenuAnimation", 0.05f, false, 100, 160), 0.0001f);
        Assert.AreEqual(0.4f, (float)InvokeStatic("AdvanceMenuAnimation", 0.4f, true, -20, 160), 0.0001f);
        Assert.AreEqual(1.0f, (float)InvokeStatic("AdvanceMenuAnimation", 0.4f, true, 20, 0), 0.0001f);
        Assert.AreEqual(0.0f, (float)InvokeStatic("AdvanceMenuAnimation", 0.4f, false, 20, 0), 0.0001f);
        Assert.AreEqual(25.0f, (float)InvokeStatic("InterpolateMenuSelection", 25.0f, 125.0f, 0, 100), 0.0001f);
        Assert.AreEqual(125.0f, (float)InvokeStatic("InterpolateMenuSelection", 25.0f, 125.0f, 100, 100), 0.0001f);
        Assert.AreEqual(125.0f, (float)InvokeStatic("InterpolateMenuSelection", 25.0f, 125.0f, 140, 100), 0.0001f);
        Assert.AreEqual(125.0f, (float)InvokeStatic("InterpolateMenuSelection", 25.0f, 125.0f, 1, 0), 0.0001f);

        for (int elapsed = -20; elapsed <= 120; elapsed += 5)
        {
            float forward = (float)InvokeStatic("InterpolateMenuSelection", 25.0f, 125.0f, elapsed, 100);
            float reverse = (float)InvokeStatic("InterpolateMenuSelection", 125.0f, 25.0f, elapsed, 100);
            Assert.IsTrue(forward >= 25.0f && forward <= 125.0f, "L'interpolation avant doit rester bornee.");
            Assert.IsTrue(reverse >= 25.0f && reverse <= 125.0f, "L'interpolation arriere doit rester bornee.");
        }

        float previous = -1.0f;

        for (int step = 0; step <= 10; step++)
        {
            float eased = (float)InvokeStatic("EaseOutCubic", step / 10.0f);
            Assert.IsTrue(eased >= 0.0f && eased <= 1.0f, "L'easing doit rester borne.");
            Assert.IsTrue(eased >= previous, "L'easing doit progresser sans retour en arriere.");
            previous = eased;
        }

        object script = CreateInitializedHeadlessScript();
        int now = (int)InvokeStatic("GetMenuGameTimeSafe");
        SetFieldValue(script, "_menuCategoryTransitionStartedAt", now);
        float justStarted = (float)InvokeInstance(script, "GetMenuCategoryTransitionAlpha");
        Assert.IsTrue(
            justStarted >= 0.72f && justStarted <= 0.82f,
            "Le test tolère les quelques millisecondes écoulées entre les deux lectures GameTime.");
        SetFieldValue(script, "_menuCategoryTransitionStartedAt", now - 120);
        Assert.AreEqual(1.0f, (float)InvokeInstance(script, "GetMenuCategoryTransitionAlpha"), 0.001f);
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_KeyboardAndNumpadAliasesStayEquivalent()
    {
        AssertMainMenuKeyAlias(Keys.Up, Keys.NumPad8, "Npc", "NpcArmor");
        AssertMainMenuKeyAlias(Keys.Down, Keys.NumPad2, "Npc", "NpcArmor");
        AssertMainMenuKeyAlias(Keys.Left, Keys.NumPad4, "Npc", "PlacementDistance");
        AssertMainMenuKeyAlias(Keys.Right, Keys.NumPad6, "Npc", "PlacementDistance");
        AssertMainMenuKeyAlias(Keys.Enter, Keys.NumPad5, "Tools", "CleanObjects");
        AssertMainMenuKeyAlias(Keys.Escape, Keys.Back, "Npc", "NpcArmor", true);
        AssertMainMenuKeyAlias(Keys.Escape, Keys.NumPad0, "Npc", "NpcArmor", true);
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_PageHomeEndAndTabNavigatePredictably()
    {
        object script = CreateInitializedHeadlessScript();
        SetMenuCategory(script, "Npc");

        InvokeMainMenuKey(script, Keys.End);
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, entries, "NpcAutoRespawn");

        InvokeMainMenuKey(script, Keys.Home);
        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        AssertSelectedAction(script, entries, "PrecisePlacement");

        InvokeMainMenuKey(script, Keys.PageDown);
        Assert.IsTrue(GetFieldValue<int>(script, "_mainMenuIndex") > 0);
        InvokeMainMenuKey(script, Keys.PageUp);
        Assert.AreEqual(0, GetFieldValue<int>(script, "_mainMenuIndex"));

        InvokeMainMenuKey(script, Keys.Tab);
        Assert.AreEqual("Vehicle", GetFieldValue<object>(script, "_mainMenuCategory").ToString());

        InvokeInstance(script, "HandleMainMenuKey", new KeyEventArgs(Keys.Shift | Keys.Tab));
        Assert.AreEqual("Npc", GetFieldValue<object>(script, "_mainMenuCategory").ToString());
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_TRequestsInputOnlyForCustomNpcModel()
    {
        object script = CreateInitializedHeadlessScript();
        IList modelCategories = GetFieldValue<IList>(script, "_modelCategories");
        int customCategoryIndex = -1;
        int customModelIndex = -1;

        for (int categoryIndex = 0; categoryIndex < modelCategories.Count && customModelIndex < 0; categoryIndex++)
        {
            IList modelOptions = GetFieldValue<IList>(modelCategories[categoryIndex], "Options");

            for (int modelIndex = 0; modelIndex < modelOptions.Count; modelIndex++)
            {
                if (GetFieldValue<bool>(modelOptions[modelIndex], "IsCustom"))
                {
                    customCategoryIndex = categoryIndex;
                    customModelIndex = modelIndex;
                    break;
                }
            }
        }

        Assert.IsTrue(customModelIndex >= 0, "La liste NPC doit conserver une option de modele personnalise.");
        SetFieldValue(script, "_selectedModelCategoryIndex", customCategoryIndex);
        SetFieldValue(script, "_selectedModelIndexInCategory", customModelIndex);
        SetMenuCategory(script, "Npc");
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, "NpcModel"));

        InvokeMainMenuKey(script, Keys.T);

        Assert.IsTrue(GetFieldValue<bool>(script, "_customModelInputRequested"));
    }

    [TestMethod]
    public void HeadlessWeaponEditorSimulation_KeyboardAndNumpadAliasesStayEquivalent()
    {
        AssertWeaponEditorKeyAlias(Keys.Up, Keys.NumPad8, 5);
        AssertWeaponEditorKeyAlias(Keys.Down, Keys.NumPad2, 5);
        AssertWeaponEditorKeyAlias(Keys.Left, Keys.NumPad4, 10, 5);
        AssertWeaponEditorKeyAlias(Keys.Right, Keys.NumPad6, 10, 5);
        AssertWeaponEditorKeyAlias(Keys.Enter, Keys.NumPad5, 0);
        AssertWeaponEditorKeyAlias(Keys.Escape, Keys.Back, 5);
        AssertWeaponEditorKeyAlias(Keys.Escape, Keys.NumPad0, 5);
    }

    [TestMethod]
    public void HeadlessWeaponEditorSimulation_AllRowsExposeAValueAndLoadoutSummary()
    {
        object script = CreateInitializedHeadlessScript();

        for (int index = 0; index < GetStaticFieldValue<int>("WeaponEditorItemCount"); index++)
        {
            string label = (string)InvokeInstance(script, "WeaponEditorLabel", index);
            string value = (string)InvokeInstance(script, "WeaponEditorValue", index);

            Assert.IsFalse(string.IsNullOrWhiteSpace(label), "La ligne " + index + " doit avoir un libelle.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), "La ligne " + index + " doit avoir une valeur.");
        }

        object loadout = GetFieldValue<object>(script, "_selectedWeaponLoadout");
        string firstSummary = (string)InvokeObjectInstance(loadout, "Summary");
        string unchangedSummary = (string)InvokeObjectInstance(loadout, "Summary");
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstSummary), "Le panneau droit doit pouvoir presenter un resume du loadout.");
        Assert.AreSame(firstSummary, unchangedSummary, "Summary doit reutiliser la meme chaine tant que le loadout ne change pas.");

        SetFieldValue(loadout, "Tint", 7);
        string mutatedSummary = (string)InvokeObjectInstance(loadout, "Summary");
        string stableMutatedSummary = (string)InvokeObjectInstance(loadout, "Summary");

        Assert.AreNotSame(firstSummary, mutatedSummary, "Une mutation doit invalider le cache de Summary.");
        Assert.AreEqual("teinte 7", mutatedSummary);
        Assert.AreSame(mutatedSummary, stableMutatedSummary, "Le nouveau resume doit ensuite etre remis en cache.");
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_InteriorTypeStaysExplicitlyLimitedToEntranceAndExit()
    {
        object script = CreateInitializedHeadlessScript();
        SetFieldValue(script, "_selectedPlacementType", Enum.Parse(GetNestedType("PlacementEntityType"), "Entrance"));
        SetMenuCategory(script, "Interior");
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, "PlacementType"));

        InvokeInstance(script, "ChangeMainMenuValue", 1, entries);
        Assert.AreEqual("Exit", GetFieldValue<object>(script, "_selectedPlacementType").ToString());

        entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        InvokeInstance(script, "ChangeMainMenuValue", -1, entries);
        Assert.AreEqual("Entrance", GetFieldValue<object>(script, "_selectedPlacementType").ToString());
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_DangerConfirmationCancelsThroughEveryBackKeyAndF10()
    {
        AssertDangerCancellationKey(Keys.Escape);
        AssertDangerCancellationKey(Keys.Back);
        AssertDangerCancellationKey(Keys.NumPad0);

        object script = CreateInitializedHeadlessScript();
        ArmDangerConfirmation(script, "CleanVehicles");
        SetFieldValue(script, "_menuVisible", true);
        KeyEventArgs f10 = new KeyEventArgs(Keys.F10);

        InvokeInstance(script, "OnKeyDown", null, f10);

        Assert.IsTrue(f10.Handled);
        Assert.IsFalse(GetFieldValue<bool>(script, "_menuVisible"));
        Assert.IsNull(GetFieldValue<object>(script, "_pendingDangerAction"));
    }

    [TestMethod]
    public void HeadlessMainMenuSimulation_UiPoolsStabilizeAfterStubWarmup()
    {
        PropertyInfo screenResolution = typeof(GTA.Game).GetProperty("ScreenResolution", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo gameTime = typeof(GTA.Game).GetProperty("GameTime", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo lastFrameTime = typeof(GTA.Game).GetProperty("LastFrameTime", BindingFlags.Public | BindingFlags.Static);

        if (screenResolution == null || !screenResolution.CanWrite ||
            gameTime == null || !gameTime.CanWrite ||
            lastFrameTime == null || !lastFrameTime.CanWrite)
        {
            // Avec l'API NIB reelle je n'appelle jamais Draw hors du jeu.
            // La suite -UseStubApi execute le parcours complet ci-dessous.
            return;
        }

        object previousResolution = screenResolution.GetValue(null, null);

        try
        {
            screenResolution.SetValue(null, new Size(1920, 1200), null);
            object script = CreateInitializedHeadlessScript();
            InitializeEmptyCollectionField(script, "_menuRectanglePool");
            InitializeEmptyCollectionField(script, "_menuTextPool");
            InitializeEmptyCollectionField(script, "_justiceHudRectanglePool");
            InitializeEmptyCollectionField(script, "_justiceHudTextPool");
            SetFieldValue(script, "_menuOpenProgress", 1.0f);
            SetFieldValue(script, "_menuVisible", true);
            InvokeInstance(script, "PrewarmMenuUiPools");

            int rectangleCountAfterWarmup = GetCollectionCount(script, "_menuRectanglePool");
            int textCountAfterWarmup = GetCollectionCount(script, "_menuTextPool");
            Assert.AreEqual(GetStaticFieldValue<int>("MenuRectanglePoolWarmSize"), rectangleCountAfterWarmup);
            Assert.AreEqual(GetStaticFieldValue<int>("MenuTextPoolWarmSize"), textCountAfterWarmup);
            int justiceRectangleCount = GetCollectionCount(script, "_justiceHudRectanglePool");
            int justiceTextCount = GetCollectionCount(script, "_justiceHudTextPool");
            Assert.AreEqual(GetStaticFieldValue<int>("JusticeHudRectanglePoolSize"), justiceRectangleCount);
            Assert.AreEqual(GetStaticFieldValue<int>("JusticeHudTextPoolSize"), justiceTextCount);
            InvokeInstance(script, "PrewarmJusticeHudPools");
            Assert.AreEqual(justiceRectangleCount, GetCollectionCount(script, "_justiceHudRectanglePool"));
            Assert.AreEqual(justiceTextCount, GetCollectionCount(script, "_justiceHudTextPool"));

            string[] categories = { "Npc", "Vehicle", "Object", "Interior", "Scene", "Justice", "Tools" };

            for (int index = 0; index < categories.Length; index++)
            {
                SetMenuCategory(script, categories[index]);
                InvokeInstance(script, "DrawObsidianMainMenu");
                AssertMenuPoolsKeepWarmSize(script, rectangleCountAfterWarmup, textCountAfterWarmup, categories[index]);
            }

            SetFieldValue(script, "_menuPage", Enum.Parse(GetNestedType("MenuPage"), "WeaponEditor"));
            InvokeInstance(script, "DrawObsidianWeaponEditorMenu");
            AssertMenuPoolsKeepWarmSize(script, rectangleCountAfterWarmup, textCountAfterWarmup, "Atelier");

            JusticeCaseState justiceCase = new JusticeCaseState { Enabled = true };
            JusticeRecordState justiceRecord = new JusticeRecordState();
            JusticeConviction conviction = new JusticeConviction
            {
                ConvictionId = "conviction:pool-ledger",
                JudgedAtUtc = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc),
                Severity = JusticeSeverity.Critical
            };
            for (int index = 0; index < 20; index++)
            {
                JusticeCharge charge = new JusticeCharge
                {
                    ChargeId = "charge:pool:" + index,
                    IncidentId = "incident:pool:" + index,
                    EpisodeId = "episode:pool",
                    Kind = JusticeCrimeKind.SimpleAssault,
                    DisplayName = "Délit consultable " + index,
                    Points = 18,
                    Fine = 1000L,
                    SentenceSeconds = 90,
                    Circumstances = JusticeCircumstances.Armed
                };
                justiceCase.Charges.Add(charge);
                conviction.Charges.Add(new JusticeConvictionChargeSummary
                {
                    Kind = charge.Kind,
                    DisplayName = charge.DisplayName,
                    Points = charge.Points,
                    Fine = charge.Fine,
                    SentenceSeconds = charge.SentenceSeconds,
                    Circumstances = charge.Circumstances
                });
            }
            justiceCase.RecalculateTotals();
            conviction.Score = justiceCase.ActiveScore;
            conviction.Fine = justiceCase.FineDue;
            conviction.SentenceSeconds = justiceCase.SentenceSeconds;
            justiceRecord.Convictions.Add(conviction);
            SetFieldValue(script, "_justiceCaseState", justiceCase);
            SetFieldValue(script, "_justiceRecordState", justiceRecord);
            SetFieldValue(script, "_justiceEnabled", true);

            InvokeInstance(script, "OpenJusticeLedger", false);
            InvokeInstance(script, "DrawObsidianJusticeLedgerMenu", false);
            InvokeInstance(script, "DrawObsidianJusticeLedgerMenu", false);
            AssertMenuPoolsKeepWarmSize(script, rectangleCountAfterWarmup, textCountAfterWarmup, "Délits du dossier");
            InvokeInstance(script, "OpenJusticeLedger", true);
            InvokeInstance(script, "DrawObsidianJusticeLedgerMenu", true);
            InvokeInstance(script, "DrawObsidianJusticeLedgerMenu", true);
            AssertMenuPoolsKeepWarmSize(script, rectangleCountAfterWarmup, textCountAfterWarmup, "Casier judiciaire");

            ArmDangerConfirmation(script, "CleanNpcs");
            InvokeInstance(script, "DrawObsidianMainMenu");
            AssertMenuPoolsKeepWarmSize(script, rectangleCountAfterWarmup, textCountAfterWarmup, "Modale");
        }
        finally
        {
            screenResolution.SetValue(null, previousResolution, null);
        }
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
        StringAssert.Contains(projectXml, "<DeployToGta Condition=\"'$(DeployToGta)' == ''\">false</DeployToGta>");
        StringAssert.Contains(projectXml, "'$(DeployToGta)' == 'true'");
        StringAssert.Contains(projectXml, "PackageGameReadyScript");
        StringAssert.Contains(projectXml, "DeployGameReadyScript");

        string deployScript = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tools", "deploy-game-ready.ps1"));
        StringAssert.Contains(deployScript, "File]::Replace");
        StringAssert.Contains(deployScript, "DonJCustomNpcPlacer.ENdll");
        StringAssert.Contains(deployScript, "DonJEnemySpawner.ENdll");
        StringAssert.Contains(deployScript, "manifest.json");
        StringAssert.Contains(deployScript, "DonJCustomNpcPlacer.manifest.json");

        string safetyScript = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tools", "run-safety-checks.ps1"));
        StringAssert.Contains(safetyScript, "dotnet");
        StringAssert.Contains(safetyScript, "restore");
        StringAssert.Contains(safetyScript, "build");
        StringAssert.Contains(safetyScript, "test");
        StringAssert.Contains(safetyScript, "GtaScriptsDir");
        StringAssert.Contains(safetyScript, "package-game-ready.ps1");
        StringAssert.Contains(safetyScript, "deploy-game-ready.ps1");
        StringAssert.Contains(safetyScript, "buildEndllHash");
        StringAssert.Contains(safetyScript, "packageEndllHash");
        StringAssert.Contains(safetyScript, "deployedEndllHash");
        StringAssert.Contains(safetyScript, "deployedManifestHash");
        StringAssert.Contains(safetyScript, "DonJCustomNpcPlacer.ENdll");
        StringAssert.Contains(safetyScript, "DonJCustomNpcPlacer.manifest.json");
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
        int categoryCount = Enum.GetValues(GetNestedType("MenuCategory")).Length;
        SetFieldValue(script, "_mainMenuRememberedActions", Array.CreateInstance(GetNestedType("MainMenuAction"), categoryCount));
        InitializeEmptyCollectionField(script, "_spawnedNpcs");
        InitializeEmptyCollectionField(script, "_placedVehicles");
        InitializeEmptyCollectionField(script, "_placedObjects");
        InitializeEmptyCollectionField(script, "_placedInteriorPortals");

        return script;
    }

    private static void InitializeEmptyCollectionField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Le champ collection '{fieldName}' est introuvable sur '{target.GetType().FullName}'.");
        field.SetValue(target, Activator.CreateInstance(field.FieldType, true));
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

    private static void AssertCategoryActions(object script, string categoryName, params string[] expectedActions)
    {
        SetMenuCategory(script, categoryName);
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");

        CollectionAssert.AreEqual(
            expectedActions,
            ActionNames(entries),
            "La categorie '" + categoryName + "' doit exposer exactement ses actions dans l'ordre de navigation.");
    }

    private static void AssertCategoryMapsPlacementType(object script, string categoryName, string placementTypeName)
    {
        SetMenuCategory(script, categoryName);

        Assert.AreEqual(categoryName, GetFieldValue<object>(script, "_mainMenuCategory").ToString());
        Assert.AreEqual(
            placementTypeName,
            GetFieldValue<object>(script, "_selectedPlacementType").ToString(),
            "La categorie doit selectionner automatiquement le bon type de placement.");
    }

    private static void SetMenuCategory(object script, string categoryName)
    {
        object category = Enum.Parse(GetNestedType("MenuCategory"), categoryName);
        InvokeInstance(script, "SetMainMenuCategory", category);
    }

    private static object AssertResponsiveLayout(int screenWidth, int screenHeight, float safeZone)
    {
        object layout = InvokeStatic("CalculateMenuLayout", screenWidth, screenHeight, safeZone);
        object safeBounds = GetMemberValue(layout, "SafeBounds");
        object canvas = GetMemberValue(layout, "Canvas");
        object rail = GetMemberValue(layout, "Rail");
        object content = GetMemberValue(layout, "Content");
        object details = GetMemberValue(layout, "Details");
        object footer = GetMemberValue(layout, "Footer");

        AssertPositiveRectangle(safeBounds, "SafeBounds");
        AssertPositiveRectangle(canvas, "Canvas");
        AssertPositiveRectangle(rail, "Rail");
        AssertPositiveRectangle(content, "Content");
        AssertPositiveRectangle(details, "Details");
        AssertPositiveRectangle(footer, "Footer");

        AssertRectangleInside(canvas, safeBounds, "Canvas");
        AssertRectangleInside(rail, canvas, "Rail");
        AssertRectangleInside(content, canvas, "Content");
        AssertRectangleInside(details, canvas, "Details");
        AssertRectangleInside(footer, canvas, "Footer");

        Assert.IsTrue(
            RectangleRight(rail) <= RectangleX(content) + 0.01f,
            "Le rail et le contenu ne doivent pas se chevaucher.");
        Assert.IsTrue(
            RectangleRight(content) <= RectangleX(details) + 0.01f,
            "Le contenu et le panneau de details ne doivent pas se chevaucher.");
        float uiToLogical = Convert.ToSingle(GetMemberValue(layout, "LogicalWidth")) / 1280.0f;
        Assert.IsTrue(RectangleWidth(rail) * uiToLogical >= 100.0f, "Le rail logique doit garder une largeur utile.");
        Assert.IsTrue(RectangleWidth(content) * uiToLogical >= 400.0f, "Le contenu logique doit garder une largeur utile.");
        Assert.IsTrue(RectangleWidth(details) * uiToLogical >= 200.0f, "Les details logiques doivent garder une largeur utile.");
        Assert.AreEqual(RectangleX(content), RectangleX(footer), 1.0f, "Le footer doit commencer sous le contenu.");
        Assert.AreEqual(RectangleRight(details), RectangleRight(footer), 1.0f, "Le footer doit finir sous les details.");

        return layout;
    }

    private static void AssertCompactThreshold(string methodName, int threshold)
    {
        Assert.IsTrue((bool)InvokeStatic(methodName, threshold - 1), methodName + " doit activer le mode compact sous le seuil.");
        Assert.IsFalse((bool)InvokeStatic(methodName, threshold), methodName + " ne doit pas activer le mode compact au seuil.");
        Assert.IsFalse((bool)InvokeStatic(methodName, threshold + 1), methodName + " doit rester non compact au-dessus du seuil.");
    }

    private static void AssertMainMenuKeyAlias(
        Keys keyboardKey,
        Keys numpadKey,
        string categoryName,
        string actionName,
        bool menuVisible = false)
    {
        object keyboardScript = CreateInitializedHeadlessScript();
        object numpadScript = CreateInitializedHeadlessScript();
        PrepareMainMenuSelection(keyboardScript, categoryName, actionName, menuVisible);
        PrepareMainMenuSelection(numpadScript, categoryName, actionName, menuVisible);

        KeyEventArgs keyboardEvent = InvokeMainMenuKey(keyboardScript, keyboardKey);
        KeyEventArgs numpadEvent = InvokeMainMenuKey(numpadScript, numpadKey);

        Assert.IsTrue(keyboardEvent.Handled, keyboardKey + " doit etre consommee par le menu.");
        Assert.IsTrue(numpadEvent.Handled, numpadKey + " doit etre consommee par le menu.");
        Assert.AreEqual(
            MainMenuStateSnapshot(keyboardScript),
            MainMenuStateSnapshot(numpadScript),
            keyboardKey + " et " + numpadKey + " doivent produire le meme etat.");
    }

    private static void AssertWeaponEditorKeyAlias(
        Keys keyboardKey,
        Keys numpadKey,
        int selectedIndex,
        int startingTint = 0)
    {
        object keyboardScript = CreateInitializedHeadlessScript();
        object numpadScript = CreateInitializedHeadlessScript();
        PrepareWeaponEditor(keyboardScript, selectedIndex, startingTint);
        PrepareWeaponEditor(numpadScript, selectedIndex, startingTint);

        KeyEventArgs keyboardEvent = new KeyEventArgs(keyboardKey);
        KeyEventArgs numpadEvent = new KeyEventArgs(numpadKey);
        InvokeInstance(keyboardScript, "HandleWeaponEditorKey", keyboardEvent);
        InvokeInstance(numpadScript, "HandleWeaponEditorKey", numpadEvent);

        Assert.IsTrue(keyboardEvent.Handled, keyboardKey + " doit etre consommee par l'atelier.");
        Assert.IsTrue(numpadEvent.Handled, numpadKey + " doit etre consommee par l'atelier.");
        Assert.AreEqual(WeaponEditorStateSnapshot(keyboardScript), WeaponEditorStateSnapshot(numpadScript));
    }

    private static void PrepareWeaponEditor(object script, int selectedIndex, int startingTint)
    {
        SetFieldValue(script, "_menuPage", Enum.Parse(GetNestedType("MenuPage"), "WeaponEditor"));
        SetFieldValue(script, "_weaponEditorIndex", selectedIndex);
        SetFieldValue(GetFieldValue<object>(script, "_selectedWeaponLoadout"), "Tint", startingTint);
    }

    private static string WeaponEditorStateSnapshot(object script)
    {
        object loadout = GetFieldValue<object>(script, "_selectedWeaponLoadout");

        return string.Join(
            "|",
            GetFieldValue<object>(script, "_menuPage"),
            GetFieldValue<int>(script, "_weaponEditorIndex"),
            GetFieldValue<int>(loadout, "Tint"),
            InvokeObjectInstance(loadout, "Summary"));
    }

    private static void ArmDangerConfirmation(object script, string actionName)
    {
        SetMenuCategory(script, "Tools");
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, actionName));
        InvokeInstance(script, "ActivateMainMenuItem", entries);
        Assert.AreEqual(actionName, GetFieldValue<object>(script, "_pendingDangerAction").ToString());
    }

    private static void AssertDangerCancellationKey(Keys key)
    {
        object script = CreateInitializedHeadlessScript();
        ArmDangerConfirmation(script, "CleanInteriorPortals");

        KeyEventArgs keyEvent = InvokeMainMenuKey(script, key);

        Assert.IsTrue(keyEvent.Handled);
        Assert.IsNull(GetFieldValue<object>(script, "_pendingDangerAction"));
        Assert.AreEqual("Action sensible annulée.", GetFieldValue<string>(script, "_statusText"));
    }

    private static void AssertMenuPoolsKeepWarmSize(
        object script,
        int expectedRectangleCount,
        int expectedTextCount,
        string renderedPage)
    {
        Assert.AreEqual(
            expectedRectangleCount,
            GetCollectionCount(script, "_menuRectanglePool"),
            renderedPage + " ne doit pas agrandir le pool de rectangles apres prechauffage.");
        Assert.AreEqual(
            expectedTextCount,
            GetCollectionCount(script, "_menuTextPool"),
            renderedPage + " ne doit pas agrandir le pool de textes apres prechauffage.");
    }

    private static int GetCollectionCount(object script, string fieldName)
    {
        ICollection collection = GetFieldValue<ICollection>(script, fieldName);
        Assert.IsNotNull(collection);
        return collection.Count;
    }

    private static void PrepareMainMenuSelection(object script, string categoryName, string actionName, bool menuVisible)
    {
        SetMenuCategory(script, categoryName);
        IList entries = (IList)InvokeInstance(script, "BuildMainMenuEntries");
        SetFieldValue(script, "_mainMenuIndex", FindActionIndex(entries, actionName));
        SetFieldValue(script, "_menuVisible", menuVisible);
    }

    private static KeyEventArgs InvokeMainMenuKey(object script, Keys key)
    {
        KeyEventArgs keyEvent = new KeyEventArgs(key);
        InvokeInstance(script, "HandleMainMenuKey", keyEvent);
        return keyEvent;
    }

    private static string MainMenuStateSnapshot(object script)
    {
        object pendingAction = GetFieldValue<object>(script, "_pendingDangerAction");

        return string.Join(
            "|",
            GetFieldValue<object>(script, "_mainMenuCategory"),
            GetFieldValue<int>(script, "_mainMenuIndex"),
            GetFieldValue<int>(script, "_mainMenuScrollOffset"),
            GetFieldValue<int>(script, "_selectedDistance"),
            GetFieldValue<bool>(script, "_selectedAutoRespawn"),
            GetFieldValue<bool>(script, "_menuVisible"),
            GetFieldValue<bool>(script, "_dangerConfirmationRequiresEnterRelease"),
            pendingAction == null ? "none" : pendingAction.ToString());
    }

    private static void AssertPositiveRectangle(object rectangle, string name)
    {
        Assert.IsTrue(RectangleWidth(rectangle) > 0.0f, name + " doit avoir une largeur positive.");
        Assert.IsTrue(RectangleHeight(rectangle) > 0.0f, name + " doit avoir une hauteur positive.");
    }

    private static void AssertRectangleInside(object rectangle, object container, string name)
    {
        const float tolerance = 0.01f;

        Assert.IsTrue(RectangleX(rectangle) + tolerance >= RectangleX(container), name + " depasse a gauche.");
        Assert.IsTrue(RectangleY(rectangle) + tolerance >= RectangleY(container), name + " depasse en haut.");
        Assert.IsTrue(RectangleRight(rectangle) <= RectangleRight(container) + tolerance, name + " depasse a droite.");
        Assert.IsTrue(RectangleBottom(rectangle) <= RectangleBottom(container) + tolerance, name + " depasse en bas.");
    }

    private static float RectangleX(object rectangle)
    {
        return Convert.ToSingle(GetMemberValue(rectangle, "X"));
    }

    private static float RectangleY(object rectangle)
    {
        return Convert.ToSingle(GetMemberValue(rectangle, "Y"));
    }

    private static float RectangleWidth(object rectangle)
    {
        return Convert.ToSingle(GetMemberValue(rectangle, "Width"));
    }

    private static float RectangleHeight(object rectangle)
    {
        return Convert.ToSingle(GetMemberValue(rectangle, "Height"));
    }

    private static float RectangleRight(object rectangle)
    {
        return RectangleX(rectangle) + RectangleWidth(rectangle);
    }

    private static float RectangleBottom(object rectangle)
    {
        return RectangleY(rectangle) + RectangleHeight(rectangle);
    }

    private static object GetMemberValue(object target, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo field = target.GetType().GetField(memberName, flags);

        if (field != null)
        {
            return field.GetValue(target);
        }

        PropertyInfo property = target.GetType().GetProperty(memberName, flags);
        Assert.IsNotNull(property, $"Le membre '{memberName}' est introuvable sur '{target.GetType().FullName}'.");
        return property.GetValue(target, null);
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

    private static object InvokeObjectInstance(object target, string methodName, params object[] args)
    {
        MethodInfo[] matches = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .ToArray();

        Assert.AreEqual(1, matches.Length, $"La methode privee d'instance '{methodName}' doit avoir une seule surcharge avec {args.Length} argument(s).");
        return matches[0].Invoke(target, args);
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
