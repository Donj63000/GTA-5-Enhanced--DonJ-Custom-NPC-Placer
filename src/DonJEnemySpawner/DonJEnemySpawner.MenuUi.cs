using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using GtaFont = GTA.Font;

public sealed partial class DonJEnemySpawner
{
    private const ulong NativeGetSafeZoneSize = 0xBAF107B6BB2C97F0UL;
    private const int MenuLogicalHeight = 720;
    private const int MenuDesignWidth = 980;
    private const int MenuDesignHeight = 648;
    private const int MenuRailWidth = 132;
    private const int MenuDetailsWidth = 260;
    private const int MenuZoneGap = 10;
    private const int MenuFooterHeight = 58;
    private const int MenuHeaderHeight = 82;
    private const int MenuRowHeight = 31;
    private const int MenuOpenAnimationMs = 160;
    private const int MenuCategoryAnimationMs = 120;
    private const int MenuSelectionAnimationMs = 100;
    private const int MenuRectanglePoolWarmSize = 256;
    private const int MenuTextPoolWarmSize = 192;
    private const int JusticeHudRectanglePoolSize = 12;
    private const int JusticeHudTextPoolSize = 3;
    private const int JusticeHudLogicalWidth = 310;
    private const int JusticeHudLogicalHeight = 94;
    private const int MenuSafeZoneRefreshMs = 250;
    private const int MenuSafeZoneCircuitRetryMs = 5000;

    private sealed class MenuTheme
    {
        public readonly Color Obsidian = Color.FromArgb(238, 5, 8, 13);
        public readonly Color Graphite = Color.FromArgb(235, 12, 18, 27);
        public readonly Color Card = Color.FromArgb(218, 20, 28, 39);
        public readonly Color CardLight = Color.FromArgb(228, 29, 39, 52);
        public readonly Color Border = Color.FromArgb(150, 54, 70, 88);
        public readonly Color TextPrimary = Color.FromArgb(245, 241, 246, 252);
        public readonly Color TextMuted = Color.FromArgb(220, 145, 160, 181);
        public readonly Color Red = Color.FromArgb(245, 239, 64, 89);
        public readonly Color Cyan = Color.FromArgb(242, 56, 202, 255);
        public readonly Color Amber = Color.FromArgb(242, 255, 183, 77);
        public readonly Color Green = Color.FromArgb(242, 62, 224, 151);
        public readonly Color Purple = Color.FromArgb(242, 166, 112, 255);
        public readonly Color Justice = Color.FromArgb(242, 89, 139, 255);
        public readonly Color Danger = Color.FromArgb(245, 255, 71, 93);
    }

    private struct MenuViewport
    {
        public int ScreenWidth;
        public int ScreenHeight;
        public float Aspect;
        public float SafeZone;
        public float LogicalWidth;
        public float SafeLogicalWidth;
        public float SafeLogicalHeight;
        public float SafeLeft;
        public float SafeTop;
    }

    private struct MenuFocusState
    {
        public MenuCategory Category;
        public MainMenuAction Action;
        public int Index;
    }

    private enum MenuRenderCommandKind
    {
        Rectangle,
        Text
    }

    private struct MenuRenderCommand
    {
        public MenuRenderCommandKind Kind;
        public Rectangle Bounds;
        public string Caption;
        public float Scale;
        public Color Color;
        public bool Centered;
        public bool Outline;
    }

    private struct JusticeRecordLedgerItem
    {
        public JusticeConviction Conviction;
        public JusticeConvictionChargeSummary Summary;
    }

    private static readonly MenuTheme ObsidianMenuTheme = new MenuTheme();
    private static Color MenuObsidian { get { return ObsidianMenuTheme.Obsidian; } }
    private static Color MenuGraphite { get { return ObsidianMenuTheme.Graphite; } }
    private static Color MenuCard { get { return ObsidianMenuTheme.Card; } }
    private static Color MenuCardLight { get { return ObsidianMenuTheme.CardLight; } }
    private static Color MenuBorder { get { return ObsidianMenuTheme.Border; } }
    private static Color MenuTextPrimary { get { return ObsidianMenuTheme.TextPrimary; } }
    private static Color MenuTextMuted { get { return ObsidianMenuTheme.TextMuted; } }
    private static Color MenuRed { get { return ObsidianMenuTheme.Red; } }
    private static Color MenuCyan { get { return ObsidianMenuTheme.Cyan; } }
    private static Color MenuAmber { get { return ObsidianMenuTheme.Amber; } }
    private static Color MenuGreen { get { return ObsidianMenuTheme.Green; } }
    private static Color MenuPurple { get { return ObsidianMenuTheme.Purple; } }
    private static Color MenuJustice { get { return ObsidianMenuTheme.Justice; } }
    private static Color MenuDanger { get { return ObsidianMenuTheme.Danger; } }

    // Je derive la navigation depuis l'enum pour qu'une nouvelle categorie ne
    // puisse jamais laisser le rail, les pages et la memoire des selections en decalage.
    private static readonly MenuCategory[] ObsidianMenuCategories =
        (MenuCategory[])Enum.GetValues(typeof(MenuCategory));
    private static readonly int MenuCategoryCount = ObsidianMenuCategories.Length;

    private sealed class MenuLayout
    {
        public Rectangle SafeBounds;
        public Rectangle Canvas;
        public Rectangle Rail;
        public Rectangle Content;
        public Rectangle Details;
        public Rectangle Footer;
        public Rectangle Header;
        public float LogicalWidth;
        public float Scale;
    }

    private readonly List<UIRectangle> _menuRectanglePool = new List<UIRectangle>(MenuRectanglePoolWarmSize);
    private readonly List<UIText> _menuTextPool = new List<UIText>(MenuTextPoolWarmSize);
    private int _menuRectangleCursor;
    private int _menuTextCursor;
    private bool _menuUiPoolsPrewarmed;

    // Je reserve un petit pool independant au HUD Justice afin que son rendu
    // hors menu ne perturbe jamais les curseurs ni le prechauffage de la console F10.
    private List<UIRectangle> _justiceHudRectanglePool = new List<UIRectangle>(JusticeHudRectanglePoolSize);
    private List<UIText> _justiceHudTextPool = new List<UIText>(JusticeHudTextPoolSize);
    private int _justiceHudRectangleCursor;
    private int _justiceHudTextCursor;
    private bool _justiceHudPoolsPrewarmed;
    private Rectangle _runtimeJusticeHudBounds;
    private int _runtimeJusticeHudScreenWidth;
    private int _runtimeJusticeHudScreenHeight;
    private float _runtimeJusticeHudSafeZone;

    private List<MainMenuEntry>[] _obsidianMenuEntries;
    private MenuCategory _mainMenuCategory = MenuCategory.Npc;
    private readonly MainMenuAction[] _mainMenuRememberedActions = CreateRememberedMenuActions();

    private MainMenuAction? _pendingDangerAction;
    private int _pendingDangerJusticeProfileSlot = -1;
    private int _pendingDangerJusticePlayerHandle;
    private int _pendingDangerJusticePlayerModelHash;
    private string _pendingDangerJusticeProfileDisplay = string.Empty;
    private string _pendingDangerJusticeFineDisplay = string.Empty;
    private long _pendingDangerJusticeFineAmount;
    private bool _dangerConfirmationRequiresEnterRelease;
    private float _menuOpenProgress;
    private int _menuAnimationLastGameTime;
    private int _menuCategoryTransitionStartedAt;
    private float _menuSelectionVisualY = -1.0f;
    private float _menuSelectionStartY = -1.0f;
    private float _menuSelectionTargetY = -1.0f;
    private int _menuSelectionAnimationStartedAt;
    private float _menuFrameAlpha = 1.0f;
    private int _menuFrameOffsetX;
    private int _justiceLedgerIndex;
    private int _justiceLedgerScrollOffset;
    private int _justiceLedgerProfileSlot = -1;
    private List<JusticeRecordLedgerItem> _justiceRecordLedgerCache;
    private JusticeRecordState _justiceRecordLedgerSource;
    private int _justiceRecordLedgerSignature;
    private int _justiceRecordLedgerRevision;

    private MenuLayout _runtimeMenuLayout;
    private int _runtimeMenuScreenWidth;
    private int _runtimeMenuScreenHeight;
    private float _runtimeMenuSafeZone;
    private float _cachedMenuSafeZone = 0.95f;
    private int _nextMenuSafeZoneReadAt;
    private int _menuSafeZoneCircuitRetryAt;
    private bool _menuSafeZoneCircuitOpen;

    private bool ShouldRenderMenu
    {
        get { return _menuVisible || _menuOpenProgress > 0.001f; }
    }

    private void SetMenuVisible(bool visible)
    {
        if (_menuVisible == visible)
        {
            return;
        }

        _menuVisible = visible;
        _menuAnimationLastGameTime = GetMenuGameTimeSafe();

        if (!visible)
        {
            CancelPendingDangerAction();
            if (_menuPage == MenuPage.JusticeCharges ||
                _menuPage == MenuPage.JusticeRecord)
            {
                _menuPage = MenuPage.Main;
                _justiceLedgerIndex = 0;
                _justiceLedgerScrollOffset = 0;
            }
            return;
        }

        PrewarmMenuUiPools();
        List<MainMenuEntry> entries = BuildMainMenuEntries();
        RestoreRememberedMainMenuSelection(entries);
        NormalizeMainMenuSelection(entries);
        _menuCategoryTransitionStartedAt = GetMenuGameTimeSafe();
        ResetMenuSelectionAnimation();
    }

    private void UpdateMenuAnimation()
    {
        int now = GetMenuGameTimeSafe();

        if (_menuAnimationLastGameTime == 0)
        {
            _menuAnimationLastGameTime = now;
        }

        int elapsed = now - _menuAnimationLastGameTime;
        _menuAnimationLastGameTime = now;

        if (elapsed < 0)
        {
            elapsed = 0;
        }
        else if (elapsed > 50)
        {
            elapsed = 50;
        }

        _menuOpenProgress = AdvanceMenuAnimation(
            _menuOpenProgress,
            _menuVisible,
            elapsed,
            MenuOpenAnimationMs);
    }

    private static float AdvanceMenuAnimation(float current, bool opening, int elapsedMs, int durationMs)
    {
        if (durationMs <= 0)
        {
            return opening ? 1.0f : 0.0f;
        }

        float step = Math.Max(0, elapsedMs) / (float)durationMs;
        float next = opening ? current + step : current - step;

        if (next < 0.0f)
        {
            return 0.0f;
        }

        return next > 1.0f ? 1.0f : next;
    }

    private static float EaseOutCubic(float value)
    {
        float clamped = value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;
        float inverse = 1.0f - clamped;
        return 1.0f - inverse * inverse * inverse;
    }

    private void ReleaseMenuUi()
    {
        _menuRectanglePool.Clear();
        _menuTextPool.Clear();
        _justiceHudRectanglePool.Clear();
        _justiceHudTextPool.Clear();
        _menuRectangleCursor = 0;
        _menuTextCursor = 0;
        _justiceHudRectangleCursor = 0;
        _justiceHudTextCursor = 0;
        _menuUiPoolsPrewarmed = false;
        _justiceHudPoolsPrewarmed = false;
        _obsidianMenuEntries = null;
        if (_justiceRecordLedgerCache != null)
        {
            _justiceRecordLedgerCache.Clear();
        }
        _justiceRecordLedgerSource = null;
        _justiceRecordLedgerSignature = 0;
        _justiceRecordLedgerRevision = 0;
        _runtimeMenuLayout = null;
        _runtimeJusticeHudBounds = Rectangle.Empty;
        _cachedMenuSafeZone = 0.95f;
        _nextMenuSafeZoneReadAt = 0;
        _menuSafeZoneCircuitRetryAt = 0;
        _menuSafeZoneCircuitOpen = false;
    }

    private void PrewarmMenuUiPools()
    {
        if (_menuUiPoolsPrewarmed)
        {
            return;
        }

        while (_menuRectanglePool.Count < MenuRectanglePoolWarmSize)
        {
            _menuRectanglePool.Add(new UIRectangle(Point.Empty, Size.Empty, Color.Transparent));
        }

        while (_menuTextPool.Count < MenuTextPoolWarmSize)
        {
            _menuTextPool.Add(new UIText(string.Empty, Point.Empty, 0.2f, Color.Transparent, GtaFont.ChaletLondon, false, false, false));
        }

        PrewarmJusticeHudPools();
        _menuUiPoolsPrewarmed = true;
    }

    private void PrewarmJusticeHudPools()
    {
        if (_justiceHudPoolsPrewarmed)
        {
            return;
        }

        // Je reconstruis aussi les pools dans les simulations headless qui
        // créent le script sans appeler son constructeur GTA.
        if (_justiceHudRectanglePool == null)
        {
            _justiceHudRectanglePool = new List<UIRectangle>(JusticeHudRectanglePoolSize);
        }
        if (_justiceHudTextPool == null)
        {
            _justiceHudTextPool = new List<UIText>(JusticeHudTextPoolSize);
        }

        while (_justiceHudRectanglePool.Count < JusticeHudRectanglePoolSize)
        {
            _justiceHudRectanglePool.Add(new UIRectangle(Point.Empty, Size.Empty, Color.Transparent));
        }

        while (_justiceHudTextPool.Count < JusticeHudTextPoolSize)
        {
            _justiceHudTextPool.Add(new UIText(string.Empty, Point.Empty, 0.2f, Color.Transparent, GtaFont.ChaletLondon, false, false, false));
        }

        _justiceHudPoolsPrewarmed = true;
    }

    private void DrawJusticeCustodyStatusLine()
    {
        if (!_justiceEnabled || !IsJusticePlayedProfileCustodyContextReady())
        {
            return;
        }

        PrewarmJusticeHudPools();
        _justiceHudRectangleCursor = 0;
        _justiceHudTextCursor = 0;

        Rectangle legacyBounds = GetRuntimeJusticeHudBounds();
        int lineHeight = Math.Max(24, Math.Min(30, legacyBounds.Height));
        Rectangle bounds = new Rectangle(
            legacyBounds.X,
            legacyBounds.Y,
            legacyBounds.Width,
            lineHeight);
        string caption = "JUSTICE  //  " + JusticeGetCustodyLocationDisplay() +
            "  •  reste " + GetJusticeSentenceDisplay();

        // Je garde uniquement une ligne discrète pendant la détention. Les crimes
        // et l'évasion utilisent le bandeau temporaire ShowStatus déjà existant.
        JusticeHudRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.FromArgb(178, 5, 9, 14));
        JusticeHudRectangle(bounds.X, bounds.Y, 3, bounds.Height, MenuAmber);
        JusticeHudText(
            FitText(caption, 58),
            bounds.X + 11,
            bounds.Y + 7,
            0.18f,
            Color.FromArgb(238, 232, 237, 244),
            false);
    }

    private List<MainMenuEntry> BuildObsidianMainMenuEntries()
    {
        EnsureObsidianMenuEntryCache();
        int categoryIndex = GetMenuCategoryIndex(_mainMenuCategory);
        List<MainMenuEntry> entries = categoryIndex >= 0
            ? _obsidianMenuEntries[categoryIndex]
            : null;

        if (entries == null)
        {
            entries = _obsidianMenuEntries[GetMenuCategoryIndex(MenuCategory.Npc)];
        }

        RefreshObsidianMenuEntryValues(entries);
        return entries;
    }

    private static MainMenuAction[] CreateRememberedMenuActions()
    {
        MainMenuAction[] remembered = new MainMenuAction[MenuCategoryCount];

        for (int i = 0; i < remembered.Length; i++)
        {
            remembered[i] = MainMenuAction.PrecisePlacement;
        }

        SetRememberedMenuAction(remembered, MenuCategory.Npc, MainMenuAction.PrecisePlacement);
        SetRememberedMenuAction(remembered, MenuCategory.Vehicle, MainMenuAction.PrecisePlacement);
        SetRememberedMenuAction(remembered, MenuCategory.Object, MainMenuAction.PrecisePlacement);
        SetRememberedMenuAction(remembered, MenuCategory.Interior, MainMenuAction.PlacementType);
        SetRememberedMenuAction(remembered, MenuCategory.Scene, MainMenuAction.Save);
        SetRememberedMenuAction(remembered, MenuCategory.Justice, MainMenuAction.JusticeEnabled);
        SetRememberedMenuAction(remembered, MenuCategory.Tools, MainMenuAction.TerminatorMode);
        return remembered;
    }

    private static void SetRememberedMenuAction(
        MainMenuAction[] remembered,
        MenuCategory category,
        MainMenuAction action)
    {
        int index = GetMenuCategoryIndex(category);

        if (remembered != null && index >= 0 && index < remembered.Length)
        {
            remembered[index] = action;
        }
    }

    private static int GetMenuCategoryIndex(MenuCategory category)
    {
        for (int i = 0; i < ObsidianMenuCategories.Length; i++)
        {
            if (ObsidianMenuCategories[i] == category)
            {
                return i;
            }
        }

        return -1;
    }

    private void SetObsidianMenuPage(MenuCategory category, List<MainMenuEntry> entries)
    {
        int index = GetMenuCategoryIndex(category);

        if (index >= 0 && index < _obsidianMenuEntries.Length)
        {
            _obsidianMenuEntries[index] = entries;
        }
    }

    private void EnsureObsidianMenuEntryCache()
    {
        if (_obsidianMenuEntries != null)
        {
            return;
        }

        _obsidianMenuEntries = new List<MainMenuEntry>[MenuCategoryCount];

        List<MainMenuEntry> npc = NewMenuPage(13);
        AddPlacementCommands(npc, false);
        AddCachedMenuEntry(npc, MainMenuAction.NpcCategory, "Catégorie NPC", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcModel, "Modèle NPC", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcWeaponCategory, "Catégorie arme", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcWeapon, "Arme", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcWeaponEditor, "Atelier arme", MainMenuRowKind.Action);
        AddCachedMenuEntry(npc, MainMenuAction.NpcHealth, "Santé NPC", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcArmor, "Armure NPC", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcBehavior, "Comportement NPC", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcPatrolRadius, "Rayon patrouille", MainMenuRowKind.Normal);
        AddCachedMenuEntry(npc, MainMenuAction.NpcAutoRespawn, "Réapparition auto", MainMenuRowKind.Normal);
        SetObsidianMenuPage(MenuCategory.Npc, npc);

        List<MainMenuEntry> vehicles = NewMenuPage(6);
        AddPlacementCommands(vehicles, false);
        AddCachedMenuEntry(vehicles, MainMenuAction.VehicleCategory, "Catégorie véhicule", MainMenuRowKind.Normal);
        AddCachedMenuEntry(vehicles, MainMenuAction.VehicleModel, "Véhicule", MainMenuRowKind.Normal);
        AddCachedMenuEntry(vehicles, MainMenuAction.VehicleAutoRespawn, "Réapparition auto", MainMenuRowKind.Normal);
        SetObsidianMenuPage(MenuCategory.Vehicle, vehicles);

        List<MainMenuEntry> objects = NewMenuPage(6);
        AddPlacementCommands(objects, false);
        AddCachedMenuEntry(objects, MainMenuAction.ObjectCategory, "Catégorie objet", MainMenuRowKind.Normal);
        AddCachedMenuEntry(objects, MainMenuAction.ObjectModel, "Objet", MainMenuRowKind.Normal);
        AddCachedMenuEntry(objects, MainMenuAction.ObjectAutoRespawn, "Réapparition auto", MainMenuRowKind.Normal);
        SetObsidianMenuPage(MenuCategory.Object, objects);

        List<MainMenuEntry> interiors = NewMenuPage(8);
        AddPlacementCommands(interiors, true);
        AddCachedMenuEntry(interiors, MainMenuAction.InteriorCategory, "Catégorie intérieur", MainMenuRowKind.Normal);
        AddCachedMenuEntry(interiors, MainMenuAction.InteriorModel, "Intérieur", MainMenuRowKind.Normal);
        AddCachedMenuEntry(interiors, MainMenuAction.ExitActiveInfo, "Sortie active", MainMenuRowKind.Info);
        AddCachedMenuEntry(interiors, MainMenuAction.ExitDestinationInfo, "Destination sortie", MainMenuRowKind.Info);
        SetObsidianMenuPage(MenuCategory.Interior, interiors);

        List<MainMenuEntry> scene = NewMenuPage(2);
        AddCachedMenuEntry(scene, MainMenuAction.Save, "Sauvegarder", MainMenuRowKind.Action);
        AddCachedMenuEntry(scene, MainMenuAction.Load, "Charger", MainMenuRowKind.Action);
        SetObsidianMenuPage(MenuCategory.Scene, scene);

        List<MainMenuEntry> justice = NewMenuPage(18);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeEnabled, "Justice du héros joué", MainMenuRowKind.Primary);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeProfile, "Personnage", MainMenuRowKind.Normal);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeStatus, "Statut judiciaire", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeLastCrime, "Dernière infraction", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeSeverity, "Niveau de gravité", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeWarrant, "Mandat actif", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeCharges, "Délits du dossier", MainMenuRowKind.Action);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeRecord, "Casier judiciaire", MainMenuRowKind.Action);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeFine, "Amende", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeFineDispute, "Montant litigieux", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticePayFine, "Payer la dette", MainMenuRowKind.Action);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeResolveFineDispute, "Résoudre le litige en ma faveur", MainMenuRowKind.Danger);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeSentence, "Peine", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeRecidivism, "Récidive", MainMenuRowKind.Info);
        AddCachedMenuEntry(justice, MainMenuAction.JusticePoliceMode, "Compatibilité police", MainMenuRowKind.Normal);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeRecovery, "Récupération contrôles / inventaire", MainMenuRowKind.Action);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeDiagnostic, "Diagnostic Justice", MainMenuRowKind.Action);
        AddCachedMenuEntry(justice, MainMenuAction.JusticeResetProfile, "Réinitialiser ce personnage", MainMenuRowKind.Danger);
        SetObsidianMenuPage(MenuCategory.Justice, justice);

        List<MainMenuEntry> tools = NewMenuPage(5);
        AddCachedMenuEntry(tools, MainMenuAction.TerminatorMode, "Mode Terminator", MainMenuRowKind.Primary);
        AddCachedMenuEntry(tools, MainMenuAction.CleanNpcs, "Nettoyer NPC", MainMenuRowKind.Danger);
        AddCachedMenuEntry(tools, MainMenuAction.CleanVehicles, "Nettoyer véhicules", MainMenuRowKind.Danger);
        AddCachedMenuEntry(tools, MainMenuAction.CleanObjects, "Nettoyer objets", MainMenuRowKind.Danger);
        AddCachedMenuEntry(tools, MainMenuAction.CleanInteriorPortals, "Nettoyer entrees/sorties", MainMenuRowKind.Danger);
        SetObsidianMenuPage(MenuCategory.Tools, tools);
    }

    private static List<MainMenuEntry> NewMenuPage(int capacity)
    {
        return new List<MainMenuEntry>(capacity);
    }

    private static void AddCachedMenuEntry(
        List<MainMenuEntry> entries,
        MainMenuAction action,
        string label,
        MainMenuRowKind kind)
    {
        entries.Add(new MainMenuEntry(action, label, string.Empty, kind, true));
    }

    private static void AddPlacementCommands(List<MainMenuEntry> entries, bool includePortalType)
    {
        if (includePortalType)
        {
            AddCachedMenuEntry(entries, MainMenuAction.PlacementType, "Entrée / Sortie", MainMenuRowKind.Primary);
        }

        AddCachedMenuEntry(entries, MainMenuAction.PrecisePlacement, "Placement caméra précis", MainMenuRowKind.PrimaryAction);
        AddCachedMenuEntry(entries, MainMenuAction.DistancePlacement, "Placement direct", MainMenuRowKind.Action);
        AddCachedMenuEntry(entries, MainMenuAction.PlacementDistance, "Distance placement direct", MainMenuRowKind.Normal);
    }

    private void RefreshObsidianMenuEntryValues(List<MainMenuEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            MainMenuEntry entry = entries[i];

            switch (entry.Action)
            {
                case MainMenuAction.PlacementType:
                    entry.Value = _selectedPlacementType == PlacementEntityType.Exit ? "Sortie" : "Entrée";
                    break;

                case MainMenuAction.PrecisePlacement:
                    entry.Value = "Ouvrir la camera de placement";
                    break;

                case MainMenuAction.DistancePlacement:
                    entry.Value = "Placer a " + _selectedDistance.ToString(CultureInfo.InvariantCulture) + " m";
                    break;

                case MainMenuAction.PlacementDistance:
                    entry.Value = _selectedDistance.ToString(CultureInfo.InvariantCulture) + " m";
                    break;

                case MainMenuAction.NpcCategory:
                    entry.Value = CurrentModelCategory().Name;
                    break;

                case MainMenuAction.NpcModel:
                    entry.Value = CurrentModelDisplayName();
                    break;

                case MainMenuAction.NpcWeaponCategory:
                    entry.Value = CurrentWeaponCategory().Name;
                    break;

                case MainMenuAction.NpcWeapon:
                    entry.Value = CurrentWeaponDisplayName();
                    break;

                case MainMenuAction.NpcWeaponEditor:
                    entry.Value = WeaponPresetDisplayName(_selectedWeaponLoadout.Preset) + " | " + _selectedWeaponLoadout.Summary();
                    break;

                case MainMenuAction.NpcHealth:
                    entry.Value = _selectedHealth.ToString(CultureInfo.InvariantCulture);
                    break;

                case MainMenuAction.NpcArmor:
                    entry.Value = _selectedArmor.ToString(CultureInfo.InvariantCulture);
                    break;

                case MainMenuAction.NpcBehavior:
                    entry.Value = NpcBehaviorDisplayName(_selectedBehavior);
                    break;

                case MainMenuAction.NpcPatrolRadius:
                    entry.Value = _selectedPatrolRadius.ToString(CultureInfo.InvariantCulture) + " m";
                    break;

                case MainMenuAction.NpcAutoRespawn:
                case MainMenuAction.VehicleAutoRespawn:
                case MainMenuAction.ObjectAutoRespawn:
                    entry.Value = BoolText(_selectedAutoRespawn);
                    break;

                case MainMenuAction.VehicleCategory:
                    entry.Value = CurrentVehicleCategory().Name;
                    break;

                case MainMenuAction.VehicleModel:
                    entry.Value = CurrentVehicleDisplayName();
                    break;

                case MainMenuAction.ObjectCategory:
                    entry.Value = CurrentObjectCategory().Name;
                    break;

                case MainMenuAction.ObjectModel:
                    entry.Value = CurrentObjectDisplayName();
                    break;

                case MainMenuAction.InteriorCategory:
                    entry.Value = CurrentInteriorCategory().Name;
                    break;

                case MainMenuAction.InteriorModel:
                    entry.Value = CurrentInteriorOption().DisplayName;
                    break;

                case MainMenuAction.ExitActiveInfo:
                    entry.Value = ActiveInteriorSessionDisplayName();
                    break;

                case MainMenuAction.ExitDestinationInfo:
                    entry.Value = ExitDestinationDisplayName();
                    break;

                case MainMenuAction.Save:
                case MainMenuAction.Load:
                    entry.Value = string.IsNullOrEmpty(_lastSaveFileName) ? "Aucun fichier" : _lastSaveFileName;
                    break;

                case MainMenuAction.JusticeEnabled:
                    entry.Value = GetJusticePlayedActivationDisplay();
                    entry.Kind = _justiceEnabled &&
                                 IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                        ? MainMenuRowKind.Primary
                        : MainMenuRowKind.Normal;
                    break;

                case MainMenuAction.JusticeProfile:
                    entry.Value = GetJusticeSelectedProfileContextDisplay();
                    break;

                case MainMenuAction.JusticeStatus:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedStatusDisplay());
                    break;

                case MainMenuAction.JusticeLastCrime:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedLastCrimeDisplay());
                    break;

                case MainMenuAction.JusticeSeverity:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedSeverityDisplay());
                    break;

                case MainMenuAction.JusticeWarrant:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedWarrantDisplay());
                    break;

                case MainMenuAction.JusticeCharges:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedChargesDisplay()) + " · ouvrir";
                    break;

                case MainMenuAction.JusticeRecord:
                    entry.Value = GetJusticeMenuSelectedConvictionCount()
                        .ToString(CultureInfo.InvariantCulture) + " condamnation(s)";
                    break;

                case MainMenuAction.JusticeFine:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedFineDisplay());
                    break;

                case MainMenuAction.JusticeFineDispute:
                    entry.Value = GetJusticeSelectedFineDisputeDisplay();
                    break;

                case MainMenuAction.JusticePayFine:
                    entry.Value = GetJusticeSelectedFinePaymentDisplay();
                    break;

                case MainMenuAction.JusticeResolveFineDispute:
                    entry.Value = CanJusticeResolveSelectedFineDispute()
                        ? "annuler sans nouveau débit"
                        : "aucun litige";
                    entry.Enabled = CanJusticeResolveSelectedFineDispute();
                    break;

                case MainMenuAction.JusticeSentence:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedSentenceDisplay());
                    break;

                case MainMenuAction.JusticeRecidivism:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedRecidivismDisplay());
                    break;

                case MainMenuAction.JusticePoliceMode:
                    entry.Value = GetJusticePoliceIntegrationModeDisplay();
                    break;

                case MainMenuAction.JusticeRecovery:
                    entry.Value = "fusion sûre · aucun retrait";
                    break;

                case MainMenuAction.JusticeDiagnostic:
                    entry.Value = GetJusticeDiagnosticMenuDisplay();
                    break;

                case MainMenuAction.JusticeResetProfile:
                    entry.Value = JusticeDisplayOrFallback(GetJusticeMenuSelectedProfileDisplay());
                    break;

                case MainMenuAction.TerminatorMode:
                    entry.Value = _terminatorModeEnabled ? "ACTIVE - vision rouge T-800" : "DESACTIVE";
                    entry.Kind = _terminatorModeEnabled ? MainMenuRowKind.Primary : MainMenuRowKind.Normal;
                    break;

                case MainMenuAction.CleanNpcs:
                    entry.Value = _spawnedNpcs.Count.ToString(CultureInfo.InvariantCulture) + " NPC geres";
                    break;

                case MainMenuAction.CleanVehicles:
                    entry.Value = _placedVehicles.Count.ToString(CultureInfo.InvariantCulture) + " vehicules geres";
                    break;

                case MainMenuAction.CleanObjects:
                    entry.Value = _placedObjects.Count.ToString(CultureInfo.InvariantCulture) + " objets geres";
                    break;

                case MainMenuAction.CleanInteriorPortals:
                    entry.Value = _placedInteriorPortals.Count.ToString(CultureInfo.InvariantCulture) + " portails geres";
                    break;
            }
        }
    }

    private static string JusticeDisplayOrFallback(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private void RememberMainMenuSelection(List<MainMenuEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return;
        }

        int index = Clamp(_mainMenuIndex, 0, entries.Count - 1);
        int categoryIndex = GetMenuCategoryIndex(_mainMenuCategory);

        if (categoryIndex >= 0 && categoryIndex < _mainMenuRememberedActions.Length)
        {
            _mainMenuRememberedActions[categoryIndex] = entries[index].Action;
        }
    }

    private void RestoreRememberedMainMenuSelection(List<MainMenuEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            _mainMenuIndex = 0;
            _mainMenuScrollOffset = 0;
            return;
        }

        int categoryIndex = GetMenuCategoryIndex(_mainMenuCategory);
        MainMenuAction remembered = categoryIndex >= 0 && categoryIndex < _mainMenuRememberedActions.Length
            ? _mainMenuRememberedActions[categoryIndex]
            : entries[0].Action;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Action == remembered)
            {
                _mainMenuIndex = i;
                _mainMenuScrollOffset = 0;
                EnsureMainMenuSelectionVisible(entries.Count);
                return;
            }
        }

        _mainMenuIndex = 0;
        _mainMenuScrollOffset = 0;
    }

    private void CycleMainMenuCategory(int direction)
    {
        if (MenuCategoryCount <= 0)
        {
            return;
        }

        int current = GetMenuCategoryIndex(_mainMenuCategory);
        int next = Wrap((current < 0 ? 0 : current) + (direction < 0 ? -1 : 1), MenuCategoryCount);
        SetMainMenuCategory(ObsidianMenuCategories[next]);
    }

    private void SetMainMenuCategory(MenuCategory category)
    {
        if (GetMenuCategoryIndex(category) < 0)
        {
            return;
        }

        List<MainMenuEntry> currentEntries = BuildMainMenuEntries();
        RememberMainMenuSelection(currentEntries);

        PlacementEntityType previousType = _selectedPlacementType;
        _mainMenuCategory = category;

        switch (category)
        {
            case MenuCategory.Npc:
                _selectedPlacementType = PlacementEntityType.Npc;
                break;

            case MenuCategory.Vehicle:
                _selectedPlacementType = PlacementEntityType.Vehicle;
                break;

            case MenuCategory.Object:
                _selectedPlacementType = PlacementEntityType.Object;
                break;

            case MenuCategory.Interior:
                if (_selectedPlacementType != PlacementEntityType.Entrance &&
                    _selectedPlacementType != PlacementEntityType.Exit)
                {
                    _selectedPlacementType = PlacementEntityType.Entrance;
                }
                break;
        }

        if (previousType != _selectedPlacementType)
        {
            DeletePlacementPreview();
        }

        List<MainMenuEntry> entries = BuildMainMenuEntries();
        RestoreRememberedMainMenuSelection(entries);
        NormalizeMainMenuSelection(entries);
        CancelPendingDangerAction();
        _menuCategoryTransitionStartedAt = GetMenuGameTimeSafe();
        ResetMenuSelectionAnimation();
    }

    private static MenuCategory CategoryForPlacementType(PlacementEntityType placementType)
    {
        switch (placementType)
        {
            case PlacementEntityType.Vehicle:
                return MenuCategory.Vehicle;

            case PlacementEntityType.Object:
                return MenuCategory.Object;

            case PlacementEntityType.Entrance:
            case PlacementEntityType.Exit:
                return MenuCategory.Interior;

            case PlacementEntityType.Npc:
            default:
                return MenuCategory.Npc;
        }
    }

    private void RequestDangerConfirmation(MainMenuAction action)
    {
        if (!IsDangerAction(action))
        {
            return;
        }

        _pendingDangerAction = action;
        _pendingDangerJusticeProfileSlot = GetDangerActionJusticeProfileSlot(action);
        _pendingDangerJusticeProfileDisplay = IsJusticeCanonicalProfileSlot(
            _pendingDangerJusticeProfileSlot)
                ? GetJusticeProfileDisplayName(_pendingDangerJusticeProfileSlot)
                : string.Empty;
        JusticeCaseState dangerProfileCase = IsJusticeCanonicalProfileSlot(
            _pendingDangerJusticeProfileSlot)
                ? GetJusticeProfileCaseForDisplay(_pendingDangerJusticeProfileSlot)
                : null;
        _pendingDangerJusticeFineDisplay = dangerProfileCase == null
            ? string.Empty
            : FormatJusticeMoney(dangerProfileCase.FineDue);
        _pendingDangerJusticeFineAmount = dangerProfileCase == null
            ? 0L
            : action == MainMenuAction.JusticePayFine
                ? Math.Max(0L, dangerProfileCase.FineDue)
                : action == MainMenuAction.JusticeResolveFineDispute
                    ? Math.Max(0L, dangerProfileCase.FineInDispute)
                    : 0L;
        _pendingDangerJusticePlayerHandle = 0;
        _pendingDangerJusticePlayerModelHash = 0;
        if (_pendingDangerJusticeProfileSlot == _justiceActivePlayerProfileSlot)
        {
            CaptureJusticeDangerActionIdentity(
                out _pendingDangerJusticePlayerHandle,
                out _pendingDangerJusticePlayerModelHash);
        }
        _dangerConfirmationRequiresEnterRelease = true;
        ShowStatus("Confirmation requise: Entree confirme, Echap annule.", 5000);
    }

    private int GetDangerActionJusticeProfileSlot(MainMenuAction action)
    {
        if (action == MainMenuAction.JusticeResetProfile ||
            action == MainMenuAction.JusticePayFine ||
            action == MainMenuAction.JusticeResolveFineDispute)
        {
            return GetJusticeMenuSelectedProfileSlot();
        }

        return -1;
    }

    private bool HandlePendingDangerKey(KeyEventArgs e)
    {
        if (!_pendingDangerAction.HasValue)
        {
            return false;
        }

        switch (e.KeyCode)
        {
            case Keys.Enter:
            case Keys.NumPad5:
                if (_dangerConfirmationRequiresEnterRelease)
                {
                    e.Handled = true;
                    return true;
                }

                ConfirmPendingDangerAction();
                e.Handled = true;
                return true;

            case Keys.Escape:
            case Keys.Back:
            case Keys.NumPad0:
                CancelPendingDangerAction();
                ShowStatus("Action sensible annulée.", 1800);
                e.Handled = true;
                return true;

            default:
                e.Handled = true;
                return true;
        }
    }

    private void ConfirmPendingDangerAction()
    {
        if (!_pendingDangerAction.HasValue)
        {
            return;
        }

        MainMenuAction action = _pendingDangerAction.Value;
        int justiceProfileSlot = _pendingDangerJusticeProfileSlot;
        int justicePlayerHandle = _pendingDangerJusticePlayerHandle;
        int justicePlayerModelHash = _pendingDangerJusticePlayerModelHash;
        long justiceFineAmount = _pendingDangerJusticeFineAmount;

        _pendingDangerAction = null;
        _pendingDangerJusticeProfileSlot = -1;
        _pendingDangerJusticePlayerHandle = 0;
        _pendingDangerJusticePlayerModelHash = 0;
        _pendingDangerJusticeFineAmount = 0L;
        _dangerConfirmationRequiresEnterRelease = false;

        switch (action)
        {
            case MainMenuAction.CleanNpcs:
                CleanAllSpawnedNpcs();
                break;

            case MainMenuAction.CleanVehicles:
                CleanAllPlacedVehicles();
                break;

            case MainMenuAction.CleanObjects:
                CleanAllPlacedObjects();
                break;

            case MainMenuAction.CleanInteriorPortals:
                CleanAllInteriorPortals();
                break;

            case MainMenuAction.JusticePayFine:
                // Le héros et le cash sont revalidés au second appui sur Entrée,
                // tout en conservant le montant numérique confirmé par le joueur.
                RequestJusticeConfirmedVoluntaryFinePayment(
                    justiceProfileSlot,
                    justiceFineAmount);
                break;

            case MainMenuAction.JusticeResolveFineDispute:
                ResolveJusticeFineDisputeInPlayerFavor(
                    justiceProfileSlot,
                    justiceFineAmount);
                break;

            case MainMenuAction.JusticeResetProfile:
                ExecuteJusticeConfirmedProfileReset(
                    justiceProfileSlot,
                    justicePlayerHandle,
                    justicePlayerModelHash);
                break;
        }
    }

    private void CancelPendingDangerAction()
    {
        _pendingDangerAction = null;
        _pendingDangerJusticeProfileSlot = -1;
        _pendingDangerJusticePlayerHandle = 0;
        _pendingDangerJusticePlayerModelHash = 0;
        _pendingDangerJusticeProfileDisplay = string.Empty;
        _pendingDangerJusticeFineDisplay = string.Empty;
        _pendingDangerJusticeFineAmount = 0L;
        _dangerConfirmationRequiresEnterRelease = false;
    }

    private static bool IsDangerAction(MainMenuAction action)
    {
        return
            action == MainMenuAction.CleanNpcs ||
            action == MainMenuAction.CleanVehicles ||
            action == MainMenuAction.CleanObjects ||
            action == MainMenuAction.CleanInteriorPortals ||
            action == MainMenuAction.JusticePayFine ||
            action == MainMenuAction.JusticeResolveFineDispute ||
            action == MainMenuAction.JusticeResetProfile;
    }

    private static MenuLayout CalculateMenuLayout(int screenWidth, int screenHeight, float safeZone)
    {
        MenuViewport viewport = CalculateMenuViewport(screenWidth, screenHeight, safeZone);
        float logicalWidth = viewport.LogicalWidth;
        float safeLogicalWidth = viewport.SafeLogicalWidth;
        float safeLogicalHeight = viewport.SafeLogicalHeight;
        float safeLeft = viewport.SafeLeft;
        float safeTop = viewport.SafeTop;
        float availableWidth = Math.Max(1.0f, safeLogicalWidth - 24.0f);
        float availableHeight = Math.Max(1.0f, safeLogicalHeight - 24.0f);
        float scale = Math.Min(1.0f, Math.Min(availableWidth / MenuDesignWidth, availableHeight / MenuDesignHeight));

        if (scale < 0.62f)
        {
            scale = 0.62f;
        }

        float canvasWidth = Math.Min(MenuDesignWidth * scale, safeLogicalWidth);
        float canvasHeight = Math.Min(MenuDesignHeight * scale, safeLogicalHeight);
        float canvasX = safeLeft + Math.Max(0.0f, Math.Min(12.0f * scale, safeLogicalWidth - canvasWidth));
        float canvasY = safeTop + Math.Max(0.0f, (safeLogicalHeight - canvasHeight) * 0.5f);
        float railWidth = MenuRailWidth * scale;
        float detailsWidth = MenuDetailsWidth * scale;
        float gap = MenuZoneGap * scale;
        float footerHeight = MenuFooterHeight * scale;
        float contentWidth = canvasWidth - railWidth - detailsWidth - gap * 2.0f;
        float upperHeight = canvasHeight - footerHeight - gap;
        float xFactor = 1280.0f / logicalWidth;
        Rectangle safeBounds = LogicalRectangleToUi(safeLeft, safeTop, safeLogicalWidth, safeLogicalHeight, xFactor);
        Rectangle canvas = LogicalRectangleToUi(canvasX, canvasY, canvasWidth, canvasHeight, xFactor);
        Rectangle rail = LogicalRectangleToUi(canvasX, canvasY, railWidth, canvasHeight, xFactor);
        Rectangle content = LogicalRectangleToUi(canvasX + railWidth + gap, canvasY, contentWidth, upperHeight, xFactor);
        Rectangle details = LogicalRectangleToUi(canvasX + railWidth + gap + contentWidth + gap, canvasY, detailsWidth, upperHeight, xFactor);
        Rectangle footer = LogicalRectangleToUi(canvasX + railWidth + gap, canvasY + upperHeight + gap, contentWidth + gap + detailsWidth, footerHeight, xFactor);
        Rectangle header = new Rectangle(content.X, content.Y, content.Width, Math.Min(content.Height, Math.Max(56, (int)Math.Round(MenuHeaderHeight * scale))));

        return new MenuLayout
        {
            SafeBounds = safeBounds,
            Canvas = canvas,
            Rail = rail,
            Content = content,
            Details = details,
            Footer = footer,
            Header = header,
            LogicalWidth = logicalWidth,
            Scale = scale
        };
    }

    private static MenuViewport CalculateMenuViewport(int screenWidth, int screenHeight, float safeZone)
    {
        int validWidth = screenWidth > 0 ? screenWidth : 1280;
        int validHeight = screenHeight > 0 ? screenHeight : 720;
        float aspect = validWidth / (float)validHeight;

        if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect < 1.2f || aspect > 4.0f)
        {
            aspect = 16.0f / 9.0f;
        }

        float safe = float.IsNaN(safeZone) || float.IsInfinity(safeZone)
            ? 0.95f
            : safeZone < 0.80f ? 0.80f : safeZone > 1.0f ? 1.0f : safeZone;
        float logicalWidth = MenuLogicalHeight * aspect;
        float safeLogicalWidth = logicalWidth * safe;
        float safeLogicalHeight = MenuLogicalHeight * safe;

        return new MenuViewport
        {
            ScreenWidth = validWidth,
            ScreenHeight = validHeight,
            Aspect = aspect,
            SafeZone = safe,
            LogicalWidth = logicalWidth,
            SafeLogicalWidth = safeLogicalWidth,
            SafeLogicalHeight = safeLogicalHeight,
            SafeLeft = (logicalWidth - safeLogicalWidth) * 0.5f,
            SafeTop = (MenuLogicalHeight - safeLogicalHeight) * 0.5f
        };
    }

    private static Rectangle LogicalRectangleToUi(float x, float y, float width, float height, float xFactor)
    {
        return new Rectangle(
            (int)Math.Round(x * xFactor),
            (int)Math.Round(y),
            Math.Max(1, (int)Math.Round(width * xFactor)),
            Math.Max(1, (int)Math.Round(height)));
    }

    private MenuLayout GetRuntimeMenuLayout()
    {
        Size resolution;

        try
        {
            resolution = Game.ScreenResolution;
        }
        catch
        {
            resolution = new Size(1280, 720);
        }

        int width = resolution.Width > 0 ? resolution.Width : 1280;
        int height = resolution.Height > 0 ? resolution.Height : 720;
        float safe = GetMenuSafeZoneSafe();

        if (_runtimeMenuLayout == null ||
            width != _runtimeMenuScreenWidth ||
            height != _runtimeMenuScreenHeight ||
            Math.Abs(safe - _runtimeMenuSafeZone) > 0.001f)
        {
            _runtimeMenuLayout = CalculateMenuLayout(width, height, safe);
            _runtimeMenuScreenWidth = width;
            _runtimeMenuScreenHeight = height;
            _runtimeMenuSafeZone = safe;
        }

        return _runtimeMenuLayout;
    }

    private float GetMenuSafeZoneSafe()
    {
        int now = GetMenuGameTimeSafe();
        if (_cachedMenuSafeZone < 0.80f || _cachedMenuSafeZone > 1.0f)
        {
            _cachedMenuSafeZone = 0.95f;
        }

        if (_menuSafeZoneCircuitOpen && !IsMenuDeadlineReached(now, _menuSafeZoneCircuitRetryAt))
        {
            return _cachedMenuSafeZone;
        }
        if (!_menuSafeZoneCircuitOpen && !IsMenuDeadlineReached(now, _nextMenuSafeZoneReadAt))
        {
            return _cachedMenuSafeZone;
        }

        try
        {
            float safe = Function.Call<float>((Hash)NativeGetSafeZoneSize);

            if (safe >= 0.80f && safe <= 1.0f)
            {
                _cachedMenuSafeZone = safe;
                _menuSafeZoneCircuitOpen = false;
                _menuSafeZoneCircuitRetryAt = 0;
                _nextMenuSafeZoneReadAt = unchecked(now + MenuSafeZoneRefreshMs);
                return _cachedMenuSafeZone;
            }
        }
        catch
        {
        }

        // Je coupe temporairement cette native si le loader ne l'expose pas. Une
        // frame défaillante ne doit jamais provoquer un nouvel appel à chaque tick.
        _menuSafeZoneCircuitOpen = true;
        _menuSafeZoneCircuitRetryAt = unchecked(now + MenuSafeZoneCircuitRetryMs);
        _nextMenuSafeZoneReadAt = _menuSafeZoneCircuitRetryAt;
        return _cachedMenuSafeZone;
    }

    private static bool IsMenuDeadlineReached(int now, int deadline)
    {
        return deadline == 0 || unchecked(now - deadline) >= 0;
    }

    private static int GetMenuGameTimeSafe()
    {
        try
        {
            return Game.GameTime;
        }
        catch
        {
            // Je garde les simulations headless et les erreurs de loader sans effet
            // sur la navigation du menu; le runtime GTA reprend toujours la priorite.
            return Environment.TickCount & int.MaxValue;
        }
    }

    private void DrawObsidianMainMenu()
    {
        List<MainMenuEntry> entries = BuildMainMenuEntries();
        NormalizeMainMenuSelection(entries);
        MenuFocusState focus = GetMainMenuFocus(entries);
        MainMenuEntry selectedEntry = focus.Index >= 0 && entries[focus.Index].Action == focus.Action
            ? entries[focus.Index]
            : null;
        BeginMenuFrame();

        MenuLayout layout = GetRuntimeMenuLayout();
        float easedOpen = EaseOutCubic(_menuOpenProgress);
        _menuFrameAlpha = easedOpen;
        _menuFrameOffsetX = (int)Math.Round((1.0f - easedOpen) * -18.0f);

        DrawObsidianShell(layout, focus.Category);
        DrawObsidianRail(layout, focus.Category);
        DrawObsidianContent(layout, entries);
        DrawObsidianDetails(layout, selectedEntry);
        DrawObsidianFooter(layout, false);

        if (_pendingDangerAction.HasValue)
        {
            DrawDangerConfirmation(layout, _pendingDangerAction.Value);
        }
    }

    private MenuFocusState GetMainMenuFocus(List<MainMenuEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return new MenuFocusState
            {
                Category = _mainMenuCategory,
                Action = MainMenuAction.PrecisePlacement,
                Index = -1
            };
        }

        int index = Clamp(_mainMenuIndex, 0, entries.Count - 1);
        return new MenuFocusState
        {
            Category = _mainMenuCategory,
            Action = entries[index].Action,
            Index = index
        };
    }

    private void DrawObsidianWeaponEditorMenu()
    {
        BeginMenuFrame();
        MenuLayout layout = GetRuntimeMenuLayout();
        float easedOpen = EaseOutCubic(_menuOpenProgress);
        _menuFrameAlpha = easedOpen;
        _menuFrameOffsetX = (int)Math.Round((1.0f - easedOpen) * -18.0f);

        DrawObsidianShell(layout, MenuCategory.Npc);
        DrawObsidianRail(layout, MenuCategory.Npc);
        DrawWeaponEditorContent(layout);
        DrawWeaponEditorDetails(layout);
        DrawObsidianFooter(layout, true);
    }

    private void OpenJusticeLedger(bool history)
    {
        _justiceLedgerProfileSlot = GetJusticeMenuSelectedProfileSlot();
        _menuPage = history ? MenuPage.JusticeRecord : MenuPage.JusticeCharges;
        _justiceLedgerIndex = 0;
        _justiceLedgerScrollOffset = 0;
        ResetMenuSelectionAnimation();
    }

    private void HandleJusticeLedgerKey(KeyEventArgs e)
    {
        bool history = _menuPage == MenuPage.JusticeRecord;
        int count = GetJusticeLedgerItemCount(history);
        NormalizeJusticeLedgerSelection(count);
        int pageSize = GetJusticeLedgerPageSize(count);

        switch (e.KeyCode)
        {
            case Keys.Up:
            case Keys.NumPad8:
                _justiceLedgerIndex = count <= 0
                    ? 0
                    : Wrap(_justiceLedgerIndex - 1, count);
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.Down:
            case Keys.NumPad2:
                _justiceLedgerIndex = count <= 0
                    ? 0
                    : Wrap(_justiceLedgerIndex + 1, count);
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.PageUp:
                _justiceLedgerIndex = count <= 0
                    ? 0
                    : Clamp(_justiceLedgerIndex - pageSize, 0, count - 1);
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.PageDown:
                _justiceLedgerIndex = count <= 0
                    ? 0
                    : Clamp(_justiceLedgerIndex + pageSize, 0, count - 1);
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.Home:
                _justiceLedgerIndex = 0;
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.End:
                _justiceLedgerIndex = Math.Max(0, count - 1);
                EnsureJusticeLedgerSelectionVisible(count, pageSize);
                e.Handled = true;
                break;

            case Keys.Tab:
                _menuPage = MenuPage.Main;
                ResetMenuSelectionAnimation();
                CycleMainMenuCategory(e.Shift ? -1 : 1);
                e.Handled = true;
                break;

            case Keys.Left:
            case Keys.NumPad4:
            case Keys.Right:
            case Keys.NumPad6:
            case Keys.Enter:
            case Keys.NumPad5:
                // Je garde ces touches consommées : la consultation ne modifie
                // jamais le dossier et ne déclenche aucune action gameplay.
                e.Handled = true;
                break;

            case Keys.Escape:
            case Keys.Back:
            case Keys.NumPad0:
                _menuPage = MenuPage.Main;
                ResetMenuSelectionAnimation();
                e.Handled = true;
                break;
        }
    }

    private void DrawObsidianJusticeLedgerMenu(bool history)
    {
        BeginMenuFrame();
        MenuLayout layout = GetRuntimeMenuLayout();
        float easedOpen = EaseOutCubic(_menuOpenProgress);
        _menuFrameAlpha = easedOpen;
        _menuFrameOffsetX = (int)Math.Round((1.0f - easedOpen) * -18.0f);

        DrawObsidianShell(layout, MenuCategory.Justice);
        DrawObsidianRail(layout, MenuCategory.Justice);
        DrawJusticeLedgerContent(layout, history);
        DrawJusticeLedgerDetails(layout, history);
        DrawJusticeLedgerFooter(layout, history);
    }

    private void DrawJusticeLedgerContent(MenuLayout layout, bool history)
    {
        Rectangle content = Offset(layout.Content, _menuFrameOffsetX, 0);
        Rectangle header = Offset(layout.Header, _menuFrameOffsetX, 0);
        int count = GetJusticeLedgerItemCount(history);
        int representedCount = GetJusticeLedgerRepresentedOffenseCount(history);
        NormalizeJusticeLedgerSelection(count);

        MenuRect(content.X, content.Y, content.Width, content.Height, MenuGraphite);
        DrawMenuFrame(content, Color.FromArgb(125, MenuJustice.R, MenuJustice.G, MenuJustice.B));
        MenuRect(header.X, header.Y, header.Width, header.Height, Color.FromArgb(236, 11, 17, 25));
        MenuRect(header.X, header.Y, 4, header.Height, MenuJustice);
        bool compact = IsCompactMenuContent(content.Width);
        string title = history ? "CASIER JUDICIAIRE" : "DÉLITS DU DOSSIER";
        bool hasConsolidatedFacts = representedCount > count;
        string subtitle = history
            ? (hasConsolidatedFacts
                ? "20 dernières condamnations · anciens faits regroupés xN"
                : "20 dernières condamnations · chaque charge conservée")
            : (hasConsolidatedFacts
                ? "Affaire en cours · anciens faits regroupés xN"
                : "Affaire en cours · faits confirmés uniquement");
        MenuText(title, header.X + (compact ? 14 : 20), header.Y + 13, compact ? 0.29f : 0.37f, MenuTextPrimary, false, true);
        MenuText(
            FitText(subtitle, compact ? Math.Max(22, (content.Width - 116) / 5) : 62),
            header.X + (compact ? 15 : 21),
            header.Y + 44,
            compact ? 0.16f : 0.195f,
            MenuTextMuted,
            false,
            false);
        DrawMenuStat(
            header.Right - 92,
            header.Y + 16,
            78,
            "DÉLITS",
            representedCount.ToString(CultureInfo.InvariantCulture),
            MenuCyan);

        int bodyTop = header.Bottom + 9;
        int bodyBottom = content.Bottom - 10;
        int bodyHeight = Math.Max(1, bodyBottom - bodyTop);
        int rowHeight = CalculateJusticeLedgerRowHeight(count, bodyHeight);
        int visibleRows = CalculateJusticeLedgerVisibleRowCount(count, bodyHeight);
        EnsureJusticeLedgerSelectionVisible(count, visibleRows);
        int startIndex = Math.Min(_justiceLedgerScrollOffset, Math.Max(0, count - visibleRows));
        int endIndex = Math.Min(count, startIndex + visibleRows);

        if (count == 0)
        {
            MenuRect(content.X + 8, bodyTop, content.Width - 16, 74, Color.FromArgb(70, 33, 41, 53));
            MenuRect(content.X + 8, bodyTop, 3, 74, MenuJustice);
            MenuText("AUCUN DÉLIT À AFFICHER", content.X + 24, bodyTop + 17, 0.26f, MenuTextPrimary, false, true);
            MenuText(
                history ? "Le casier ne contient encore aucune condamnation." : "Aucun fait confirmé dans le dossier actif.",
                content.X + 24,
                bodyTop + 44,
                0.19f,
                MenuTextMuted,
                false,
                false);
            return;
        }

        int selectedTargetY = bodyTop + (_justiceLedgerIndex - startIndex) * rowHeight;
        UpdateMenuSelectionAnimation(selectedTargetY);
        MenuRect(
            content.X + 7,
            (int)Math.Round(_menuSelectionVisualY),
            content.Width - 14,
            rowHeight,
            Color.FromArgb(145, MenuJustice.R, MenuJustice.G, MenuJustice.B));

        int valueX = content.X + Math.Max(210, (int)(content.Width * 0.62f));
        for (int index = startIndex; index < endIndex; index++)
        {
            int y = bodyTop + (index - startIndex) * rowHeight;
            DrawJusticeLedgerRow(content, valueX, y, rowHeight, index, history, index == _justiceLedgerIndex);
        }
        if (count > visibleRows)
        {
            DrawObsidianScrollbar(
                content.Right - 7,
                bodyTop,
                bodyBottom - bodyTop,
                count,
                visibleRows,
                _justiceLedgerScrollOffset,
                MenuJustice);
        }
    }

    private void DrawJusticeLedgerRow(
        Rectangle content,
        int valueX,
        int y,
        int rowHeight,
        int index,
        bool history,
        bool selected)
    {
        string label = string.Empty;
        string value = string.Empty;
        if (history)
        {
            JusticeConviction conviction;
            JusticeConvictionChargeSummary summary;
            if (TryGetJusticeRecordOffenseAt(index, out conviction, out summary))
            {
                label = conviction.JudgedAtUtc.ToLocalTime().ToString("dd/MM/yy", CultureInfo.InvariantCulture) +
                        "  " + summary.DisplayName;
                value = JusticeSeverityDisplayName(JusticePolicy.GetSeverity(summary.Points));
                if (summary.IsAggregate)
                {
                    value += " · x" + summary.AggregatedChargeCount.ToString(CultureInfo.InvariantCulture);
                }
            }
        }
        else
        {
            JusticeCharge charge = GetJusticeActiveChargeAt(index);
            if (charge != null)
            {
                label = charge.DisplayName;
                value = JusticeSeverityDisplayName(JusticePolicy.GetSeverity(charge.Points));
                if (charge.IsAggregate)
                {
                    value += " · x" + charge.AggregatedChargeCount.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        Color background = selected
            ? Color.FromArgb(132, 35, 48, 68)
            : Color.FromArgb(62, 31, 40, 53);
        MenuRect(content.X + 7, y + 1, content.Width - 14, rowHeight - 2, background);
        MenuRect(content.X + 7, y + 1, selected ? 4 : 2, rowHeight - 2, selected ? MenuJustice : Color.FromArgb(100, MenuJustice.R, MenuJustice.G, MenuJustice.B));
        MenuRect(content.X + 13, y + rowHeight - 1, content.Width - 26, 1, Color.FromArgb(44, 255, 255, 255));
        MenuText(
            FitText((index + 1).ToString("00", CultureInfo.InvariantCulture) + "  " + label, Math.Max(18, (valueX - content.X - 28) / 6)),
            content.X + 21,
            y + Math.Max(5, (rowHeight - 19) / 2),
            0.218f,
            selected ? MenuTextPrimary : MenuTextMuted,
            false,
            selected);
        MenuText(
            FitText(value, Math.Max(10, (content.Right - valueX - 16) / 6)),
            valueX,
            y + Math.Max(5, (rowHeight - 19) / 2),
            0.205f,
            selected ? MenuJustice : Color.FromArgb(220, 194, 205, 220),
            false,
            selected);
    }

    private void DrawJusticeLedgerDetails(MenuLayout layout, bool history)
    {
        Rectangle details = Offset(layout.Details, _menuFrameOffsetX, 0);
        MenuRect(details.X, details.Y, details.Width, details.Height, MenuGraphite);
        DrawMenuFrame(details, Color.FromArgb(125, MenuJustice.R, MenuJustice.G, MenuJustice.B));
        MenuRect(details.X, details.Y, details.Width, 46, Color.FromArgb(238, 9, 15, 23));
        MenuRect(details.X, details.Y, details.Width, 3, MenuCyan);
        MenuText(
            history ? "DETAIL // CASIER" : "DETAIL // DOSSIER",
            details.X + 15,
            details.Y + 14,
            IsCompactMenuDetails(details.Width) ? 0.22f : 0.26f,
            MenuTextPrimary,
            false,
            true);

        int count = GetJusticeLedgerItemCount(history);
        if (count <= 0)
        {
            DrawDetailLine(details, details.Y + 62, "STATUT", "Aucun délit", MenuJustice);
            DrawJusticePanelSummary(details);
            DrawMenuStatus(details, MenuJustice);
            return;
        }

        int top = details.Y + 58;
        string label;
        JusticeSeverity severity;
        int points;
        long fine;
        int sentence;
        JusticeCircumstances circumstances;
        string dateOrState;
        if (history)
        {
            JusticeConviction conviction;
            JusticeConvictionChargeSummary summary;
            if (!TryGetJusticeRecordOffenseAt(_justiceLedgerIndex, out conviction, out summary))
            {
                return;
            }
            label = summary.DisplayName;
            severity = JusticePolicy.GetSeverity(summary.Points);
            points = summary.Points;
            fine = summary.Fine;
            sentence = summary.SentenceSeconds;
            circumstances = summary.Circumstances;
            dateOrState = conviction.JudgedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            if (summary.IsAggregate)
            {
                dateOrState += " · x" + Math.Max(1, summary.AggregatedChargeCount)
                    .ToString(CultureInfo.InvariantCulture) + " faits";
            }
        }
        else
        {
            JusticeCharge charge = GetJusticeActiveChargeAt(_justiceLedgerIndex);
            if (charge == null)
            {
                return;
            }
            label = charge.DisplayName;
            severity = JusticePolicy.GetSeverity(charge.Points);
            points = charge.Points;
            fine = charge.Fine;
            sentence = charge.SentenceSeconds;
            circumstances = charge.Circumstances;
            dateOrState = charge.IsAdjudicated ? "Juge" : "Actif";
            if (charge.IsAggregate)
            {
                dateOrState += " · x" + Math.Max(1, charge.AggregatedChargeCount)
                    .ToString(CultureInfo.InvariantCulture) + " faits";
            }
        }

        MenuRect(details.X + 12, top, details.Width - 24, 76, MenuCard);
        MenuRect(details.X + 12, top, 3, 76, MenuJustice);
        MenuText(history ? "CONDAMNATION" : "INFRACTION CONFIRMEE", details.X + 23, top + 9, 0.16f, MenuCyan, false, false);
        MenuText(FitText(label, Math.Max(16, (details.Width - 46) / 5)), details.X + 23, top + 31, IsCompactMenuDetails(details.Width) ? 0.22f : 0.265f, MenuTextPrimary, false, true);
        MenuText(FitText(dateOrState, Math.Max(16, (details.Width - 46) / 5)), details.X + 23, top + 55, 0.17f, MenuTextMuted, false, false);

        int lineTop = top + 88;
        DrawDetailLine(details, lineTop, "GRAVITE", JusticeSeverityDisplayName(severity), MenuJustice);
        DrawDetailLine(details, lineTop + 27, "POINTS", points.ToString(CultureInfo.InvariantCulture), MenuCyan);
        DrawDetailLine(details, lineTop + 54, "AMENDE", FormatJusticeMoney(fine), MenuAmber);
        DrawDetailLine(details, lineTop + 81, "PEINE", FormatJusticeDuration(sentence), MenuDanger);

        int circumstanceTop = lineTop + 119;
        MenuRect(details.X + 12, circumstanceTop, details.Width - 24, 66, MenuCard);
        MenuRect(details.X + 12, circumstanceTop, 3, 66, MenuCyan);
        MenuText("CIRCONSTANCES", details.X + 23, circumstanceTop + 9, 0.16f, MenuCyan, false, false);
        DrawWrappedMenuHint(
            FormatJusticeCircumstances(circumstances),
            details.X + 23,
            circumstanceTop + 29,
            details.Width - 46);

        DrawJusticePanelSummary(details);
        DrawMenuStatus(details, MenuJustice);
    }

    private void DrawJusticeLedgerFooter(MenuLayout layout, bool history)
    {
        Rectangle footer = Offset(layout.Footer, _menuFrameOffsetX, 0);
        MenuRect(footer.X, footer.Y, footer.Width, footer.Height, Color.FromArgb(242, 8, 13, 20));
        DrawMenuFrame(footer, Color.FromArgb(115, MenuJustice.R, MenuJustice.G, MenuJustice.B));
        MenuRect(footer.X, footer.Y, footer.Width, 2, MenuJustice);
        MenuText(
            history ? "DONJ // CASIER CONSULTABLE" : "DONJ // DOSSIER CONSULTABLE",
            footer.X + 17,
            footer.Y + 11,
            IsCompactMenuFooter(footer.Width) ? 0.17f : 0.205f,
            MenuTextPrimary,
            false,
            true);
        MenuText("Échap retour · PageUp/PageDown défile", footer.X + 18, footer.Y + 34, 0.16f, MenuTextMuted, false, false);

        int right = footer.Right - 12;
        int chipY = footer.Y + 14;
        right = DrawKeyChipRight(right, chipY, 58, MenuToggleKeyLabel, MenuRed);
        right = DrawKeyChipRight(right - 6, chipY, 72, "ECHAP", MenuCyan);
        DrawKeyChipRight(right - 6, chipY, 86, "HAUT/BAS", MenuJustice);
    }

    private int GetJusticeLedgerItemCount(bool history)
    {
        if (!history)
        {
            JusticeCaseState source = GetJusticeProfileCaseForDisplay(
                _justiceLedgerProfileSlot);
            if (source == null)
            {
                return 0;
            }
            int count = 0;
            for (int index = 0; index < source.Charges.Count; index++)
            {
                if (source.Charges[index] != null)
                {
                    count++;
                }
            }
            return count;
        }

        EnsureJusticeRecordLedgerCache();
        return _justiceRecordLedgerCache == null ? 0 : _justiceRecordLedgerCache.Count;
    }

    private int GetJusticeLedgerRepresentedOffenseCount(bool history)
    {
        if (!history)
        {
            JusticeCaseState source = GetJusticeProfileCaseForDisplay(
                _justiceLedgerProfileSlot);
            return JusticePolicy.GetRepresentedChargeCount(source);
        }

        EnsureJusticeRecordLedgerCache();
        if (_justiceRecordLedgerCache == null)
        {
            return 0;
        }

        // Je compte les faits représentés, pas seulement les lignes. Ainsi une
        // consolidation de sécurité xN reste visible dans le total du casier.
        long represented = 0L;
        for (int index = 0; index < _justiceRecordLedgerCache.Count; index++)
        {
            JusticeConvictionChargeSummary summary =
                _justiceRecordLedgerCache[index].Summary;
            if (summary == null)
            {
                continue;
            }
            represented += summary.IsAggregate
                ? Math.Max(1, summary.AggregatedChargeCount)
                : 1;
            if (represented >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }
        return (int)represented;
    }

    private int GetJusticeLedgerPageSize(int count)
    {
        if (count <= 0)
        {
            return 1;
        }
        MenuLayout layout = GetRuntimeMenuLayout();
        int bodyHeight = Math.Max(
            1,
            layout.Content.Bottom - 10 - (layout.Header.Bottom + 9));
        return CalculateJusticeLedgerVisibleRowCount(count, bodyHeight);
    }

    private static int CalculateJusticeLedgerRowHeight(int count, int bodyHeight)
    {
        int referenceCount = Math.Max(
            1,
            Math.Min(MainMenuCompactVisibleRowLimit, Math.Max(1, count)));
        return Math.Max(
            24,
            Math.Min(MenuRowHeight, Math.Max(1, bodyHeight) / referenceCount));
    }

    private static int CalculateJusticeLedgerVisibleRowCount(int count, int bodyHeight)
    {
        int boundedCount = Math.Max(1, count);
        int rowHeight = CalculateJusticeLedgerRowHeight(count, bodyHeight);
        return Math.Max(
            1,
            Math.Min(boundedCount, Math.Max(1, bodyHeight) / Math.Max(1, rowHeight)));
    }

    private JusticeCharge GetJusticeActiveChargeAt(int visibleIndex)
    {
        JusticeCaseState source = GetJusticeProfileCaseForDisplay(
            _justiceLedgerProfileSlot);
        if (source == null || visibleIndex < 0)
        {
            return null;
        }
        int current = 0;
        for (int index = 0; index < source.Charges.Count; index++)
        {
            JusticeCharge charge = source.Charges[index];
            if (charge == null)
            {
                continue;
            }
            if (current == visibleIndex)
            {
                return charge;
            }
            current++;
        }
        return null;
    }

    private bool TryGetJusticeRecordOffenseAt(
        int visibleIndex,
        out JusticeConviction conviction,
        out JusticeConvictionChargeSummary summary)
    {
        conviction = null;
        summary = null;
        EnsureJusticeRecordLedgerCache();
        if (_justiceRecordLedgerCache == null ||
            visibleIndex < 0 ||
            visibleIndex >= _justiceRecordLedgerCache.Count)
        {
            return false;
        }

        JusticeRecordLedgerItem item = _justiceRecordLedgerCache[visibleIndex];
        conviction = item.Conviction;
        summary = item.Summary;
        return conviction != null && summary != null;
    }

    private void EnsureJusticeRecordLedgerCache()
    {
        JusticeRecordState source = GetJusticeProfileRecordForDisplay(
            _justiceLedgerProfileSlot);
        int signature = CalculateJusticeRecordLedgerSignature(source);
        if (ReferenceEquals(source, _justiceRecordLedgerSource) &&
            signature == _justiceRecordLedgerSignature &&
            _justiceRecordLedgerCache != null)
        {
            return;
        }

        if (_justiceRecordLedgerCache == null)
        {
            _justiceRecordLedgerCache = new List<JusticeRecordLedgerItem>(
                JusticePolicy.MaxConvictions * 4);
        }
        else
        {
            _justiceRecordLedgerCache.Clear();
        }

        if (source != null)
        {
            for (int convictionIndex = source.Convictions.Count - 1;
                 convictionIndex >= 0;
                 convictionIndex--)
            {
                JusticeConviction candidate = source.Convictions[convictionIndex];
                if (candidate == null)
                {
                    continue;
                }

                for (int chargeIndex = 0; chargeIndex < candidate.Charges.Count; chargeIndex++)
                {
                    JusticeConvictionChargeSummary candidateSummary = candidate.Charges[chargeIndex];
                    if (candidateSummary != null)
                    {
                        _justiceRecordLedgerCache.Add(new JusticeRecordLedgerItem
                        {
                            Conviction = candidate,
                            Summary = candidateSummary
                        });
                    }
                }
            }
        }

        _justiceRecordLedgerSource = source;
        _justiceRecordLedgerSignature = signature;
        _justiceRecordLedgerRevision++;
    }

    private static int CalculateJusticeRecordLedgerSignature(JusticeRecordState source)
    {
        return source == null ? 0 : source.LedgerRevision;
    }

    private void NormalizeJusticeLedgerSelection(int count)
    {
        if (count <= 0)
        {
            _justiceLedgerIndex = 0;
            _justiceLedgerScrollOffset = 0;
            return;
        }
        _justiceLedgerIndex = Clamp(_justiceLedgerIndex, 0, count - 1);
        EnsureJusticeLedgerSelectionVisible(count);
    }

    private void EnsureJusticeLedgerSelectionVisible(int count)
    {
        EnsureJusticeLedgerSelectionVisible(
            count,
            Math.Min(MainMenuCompactVisibleRowLimit, Math.Max(0, count)));
    }

    private void EnsureJusticeLedgerSelectionVisible(int count, int visibleRows)
    {
        if (count <= 0)
        {
            _justiceLedgerScrollOffset = 0;
            return;
        }
        visibleRows = Clamp(visibleRows, 1, count);
        if (_justiceLedgerIndex < _justiceLedgerScrollOffset)
        {
            _justiceLedgerScrollOffset = _justiceLedgerIndex;
        }
        else if (_justiceLedgerIndex >= _justiceLedgerScrollOffset + visibleRows)
        {
            _justiceLedgerScrollOffset = _justiceLedgerIndex - visibleRows + 1;
        }
        _justiceLedgerScrollOffset = Clamp(
            _justiceLedgerScrollOffset,
            0,
            Math.Max(0, count - visibleRows));
    }

    private static string FormatJusticeCircumstances(JusticeCircumstances circumstances)
    {
        if (circumstances == JusticeCircumstances.None)
        {
            return "Aucune circonstance aggravante retenue.";
        }

        StringBuilder text = new StringBuilder(112);
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.Armed, "arme");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.ExplosiveOrIncendiary, "explosif/incendiaire");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.ActiveWarrant, "mandat actif");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.InCustody, "en detention");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.MultipleVictims, "victimes multiples");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.GroupCrime, "en reunion");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.OrganizedBand, "bande organisee");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.ProportionalSelfDefense, "legitime defense");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.ExcessiveSelfDefense, "riposte excessive");
        AppendJusticeCircumstance(text, circumstances, JusticeCircumstances.VehicleUsedAsWeapon, "vehicule utilise comme arme");
        return text.ToString();
    }

    private static void AppendJusticeCircumstance(
        StringBuilder text,
        JusticeCircumstances circumstances,
        JusticeCircumstances flag,
        string label)
    {
        if ((circumstances & flag) == 0)
        {
            return;
        }
        if (text.Length > 0)
        {
            text.Append(" · ");
        }
        text.Append(label);
    }

    private void BeginMenuFrame()
    {
        PrewarmMenuUiPools();
        _menuRectangleCursor = 0;
        _menuTextCursor = 0;
    }

    private void DrawObsidianShell(MenuLayout layout, MenuCategory accentCategory)
    {
        Rectangle canvas = Offset(layout.Canvas, _menuFrameOffsetX, 0);
        Color accent = GetMenuCategoryAccent(accentCategory);

        MenuRect(canvas.X + 6, canvas.Y + 8, canvas.Width, canvas.Height, Color.FromArgb(110, 0, 0, 0));
        MenuRect(canvas.X, canvas.Y, canvas.Width, canvas.Height, MenuObsidian);
        DrawMenuFrame(canvas, accent);
        MenuRect(canvas.X, canvas.Y, canvas.Width, 3, accent);
        MenuRect(canvas.X + 1, canvas.Bottom - 2, canvas.Width - 2, 1, Color.FromArgb(95, MenuCyan.R, MenuCyan.G, MenuCyan.B));
    }

    private void DrawObsidianRail(MenuLayout layout, MenuCategory selectedCategory)
    {
        Rectangle rail = Offset(layout.Rail, _menuFrameOffsetX, 0);
        Color accent = GetMenuCategoryAccent(selectedCategory);
        MenuRect(rail.X, rail.Y, rail.Width, rail.Height, Color.FromArgb(244, 7, 11, 17));
        MenuRect(rail.Right - 1, rail.Y + 12, 1, rail.Height - 24, Color.FromArgb(105, MenuCyan.R, MenuCyan.G, MenuCyan.B));

        Rectangle logo = new Rectangle(rail.X + 18, rail.Y + 17, rail.Width - 36, 58);
        DrawDonJMonogram(logo, accent);
        MenuText("ENTITY CONTROL", rail.X + rail.Width / 2, rail.Y + 80, 0.185f, MenuTextMuted, true, false);

        int slotTop = rail.Y + 108;
        int slotGap = 5;
        int available = Math.Max(210, rail.Height - 165);
        int gapsHeight = slotGap * Math.Max(0, MenuCategoryCount - 1);
        int slotHeight = Math.Max(35, Math.Min(58, (available - gapsHeight) / Math.Max(1, MenuCategoryCount)));

        for (int i = 0; i < MenuCategoryCount; i++)
        {
            MenuCategory category = ObsidianMenuCategories[i];
            bool selected = category == selectedCategory;
            int y = slotTop + i * (slotHeight + slotGap);
            Color categoryAccent = GetMenuCategoryAccent(category);
            Color background = selected ? Color.FromArgb(210, 26, 36, 49) : Color.FromArgb(100, 19, 26, 36);

            MenuRect(rail.X + 10, y, rail.Width - 20, slotHeight, background);
            MenuRect(rail.X + 10, y, selected ? 4 : 2, slotHeight, selected ? categoryAccent : Color.FromArgb(95, categoryAccent.R, categoryAccent.G, categoryAccent.B));

            if (IsCompactMenuRail(rail.Width))
            {
                Rectangle compactIcon = new Rectangle(rail.X + rail.Width / 2 - 12, y + 5, 25, 24);
                DrawCategoryIcon(category, compactIcon, selected ? categoryAccent : MenuTextMuted);
                MenuText(FitText(MenuCategoryDisplayName(category), 11), rail.X + rail.Width / 2, y + slotHeight - 18, 0.17f, selected ? MenuTextPrimary : MenuTextMuted, true, selected);
            }
            else
            {
                Rectangle icon = new Rectangle(rail.X + 21, y + Math.Max(5, (slotHeight - 24) / 2), 25, 24);
                DrawCategoryIcon(category, icon, selected ? categoryAccent : MenuTextMuted);
                MenuText(MenuCategoryDisplayName(category), rail.X + 55, y + slotHeight / 2 - 7, 0.218f, selected ? MenuTextPrimary : MenuTextMuted, false, selected);
            }
        }

        MenuRect(rail.X + 14, rail.Bottom - 40, rail.Width - 28, 25, Color.FromArgb(120, 19, 27, 37));
        MenuText("TAB  CATEGORIES", rail.X + rail.Width / 2, rail.Bottom - 34, IsCompactMenuRail(rail.Width) ? 0.15f : 0.18f, MenuCyan, true, false);
    }

    private void DrawObsidianContent(MenuLayout layout, List<MainMenuEntry> entries)
    {
        Rectangle content = Offset(layout.Content, _menuFrameOffsetX, 0);
        Rectangle header = Offset(layout.Header, _menuFrameOffsetX, 0);
        Color accent = GetMenuCategoryAccent(_mainMenuCategory);
        float categoryAlpha = GetMenuCategoryTransitionAlpha();

        MenuRect(content.X, content.Y, content.Width, content.Height, MenuGraphite);
        DrawMenuFrame(content, Color.FromArgb(125, accent.R, accent.G, accent.B));
        MenuRect(header.X, header.Y, header.Width, header.Height, Color.FromArgb(236, 11, 17, 25));
        MenuRect(header.X, header.Y, 4, header.Height, accent);
        bool compactContent = IsCompactMenuContent(content.Width);
        int statWidth = Math.Min(78, Math.Max(62, content.Width / 7));
        int titleMaxChars = compactContent ? Math.Max(16, (content.Width - 105) / 7) : 52;
        int subtitleMaxChars = compactContent
            ? Math.Max(20, (content.Width - statWidth - 45) / 5)
            : Math.Max(24, (content.Width - 38) / 5);
        MenuText(FitText(MenuCategoryTitle(_mainMenuCategory), titleMaxChars), header.X + (compactContent ? 14 : 20), header.Y + 13, compactContent ? 0.30f : 0.39f, MenuTextPrimary, false, true);
        MenuText(FitText(MenuCategorySubtitle(_mainMenuCategory), subtitleMaxChars), header.X + (compactContent ? 15 : 21), header.Y + 44, compactContent ? 0.17f : 0.205f, MenuTextMuted, false, false);

        if (!compactContent)
        {
            DrawMenuStat(header.Right - statWidth * 2 - 24, header.Y + 16, statWidth, "TYPE", MenuCategoryShortName(_mainMenuCategory), accent);
        }

        DrawMenuStat(header.Right - statWidth - 14, header.Y + 16, statWidth, "LIGNE", (_mainMenuIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + entries.Count.ToString(CultureInfo.InvariantCulture), MenuCyan);

        int bodyTop = header.Bottom + 9;
        int bodyBottom = content.Bottom - 10;
        int rowHeight = Math.Max(24, Math.Min(MenuRowHeight, (bodyBottom - bodyTop) / Math.Max(1, Math.Min(MainMenuCompactVisibleRowLimit, entries.Count))));
        int visibleRows = Math.Max(1, Math.Min(entries.Count, (bodyBottom - bodyTop) / Math.Max(1, rowHeight)));
        EnsureMainMenuSelectionVisible(entries.Count);
        int startIndex = Math.Min(_mainMenuScrollOffset, Math.Max(0, entries.Count - visibleRows));
        int endIndex = Math.Min(entries.Count, startIndex + visibleRows);
        int selectedTargetY = bodyTop + (_mainMenuIndex - startIndex) * rowHeight;

        UpdateMenuSelectionAnimation(selectedTargetY);

        MenuRect(content.X + 7, (int)Math.Round(_menuSelectionVisualY), content.Width - 14, rowHeight, Color.FromArgb((int)(145 * categoryAlpha), accent.R, accent.G, accent.B));

        int valueX = content.X + Math.Max(190, (int)(content.Width * 0.56f));

        for (int i = startIndex; i < endIndex; i++)
        {
            MainMenuEntry entry = entries[i];
            int y = bodyTop + (i - startIndex) * rowHeight;
            bool selected = i == _mainMenuIndex;
            DrawObsidianEntryRow(content, valueX, y, rowHeight, entry, selected, categoryAlpha);
        }

        if (entries.Count > visibleRows)
        {
            DrawObsidianScrollbar(content.Right - 7, bodyTop, bodyBottom - bodyTop, entries.Count, visibleRows);
        }
    }

    private void DrawObsidianEntryRow(
        Rectangle content,
        int valueX,
        int y,
        int rowHeight,
        MainMenuEntry entry,
        bool selected,
        float transitionAlpha)
    {
        Color accent = entry.Kind == MainMenuRowKind.Danger ? MenuDanger : GetMenuCategoryAccent(_mainMenuCategory);
        Color background;

        if (entry.Kind == MainMenuRowKind.Danger)
        {
            background = selected ? Color.FromArgb(138, 92, 22, 34) : Color.FromArgb(72, 70, 20, 30);
        }
        else if (entry.Kind == MainMenuRowKind.PrimaryAction || entry.Kind == MainMenuRowKind.Action)
        {
            background = selected ? Color.FromArgb(140, 17, 68, 73) : Color.FromArgb(74, 17, 45, 53);
        }
        else if (entry.Kind == MainMenuRowKind.Info)
        {
            background = Color.FromArgb(selected ? 118 : 66, 33, 38, 50);
        }
        else
        {
            background = Color.FromArgb(selected ? 130 : 58, 35, 45, 59);
        }

        int alpha = (int)(transitionAlpha * 255.0f);
        MenuRect(content.X + 7, y + 1, content.Width - 14, rowHeight - 2, WithMaximumAlpha(background, alpha));
        MenuRect(content.X + 7, y + 1, selected ? 4 : 2, rowHeight - 2, WithMaximumAlpha(accent, alpha));
        MenuRect(content.X + 13, y + rowHeight - 1, content.Width - 26, 1, Color.FromArgb((int)(44 * transitionAlpha), 255, 255, 255));

        Color labelColor = selected ? MenuTextPrimary : entry.Kind == MainMenuRowKind.Danger ? Color.FromArgb(235, 255, 154, 165) : MenuTextMuted;
        Color valueColor = selected ? accent : Color.FromArgb(220, 194, 205, 220);
        int maxValueChars = Math.Max(14, (content.Right - valueX - 18) / 7);

        int maxLabelChars = Math.Max(12, (valueX - content.X - 28) / 6);
        MenuText(FitText(entry.Label, maxLabelChars), content.X + 21, y + Math.Max(5, (rowHeight - 19) / 2), 0.226f, WithMaximumAlpha(labelColor, alpha), false, selected);
        MenuText(FitText(entry.Value, maxValueChars), valueX, y + Math.Max(5, (rowHeight - 19) / 2), 0.218f, WithMaximumAlpha(valueColor, alpha), false, selected);

        if (IsObsidianValueEditable(entry.Action))
        {
            MenuText("<  >", content.Right - 32, y + Math.Max(5, (rowHeight - 19) / 2), 0.19f, WithMaximumAlpha(accent, alpha), true, false);
        }
        else if (entry.Kind == MainMenuRowKind.Action || entry.Kind == MainMenuRowKind.PrimaryAction || entry.Kind == MainMenuRowKind.Danger)
        {
            MenuText(">", content.Right - 20, y + Math.Max(5, (rowHeight - 19) / 2), 0.22f, WithMaximumAlpha(accent, alpha), true, selected);
        }
    }

    private void DrawObsidianDetails(MenuLayout layout, MainMenuEntry selectedEntry)
    {
        Rectangle details = Offset(layout.Details, _menuFrameOffsetX, 0);
        Color accent = GetMenuCategoryAccent(_mainMenuCategory);

        MenuRect(details.X, details.Y, details.Width, details.Height, MenuGraphite);
        DrawMenuFrame(details, Color.FromArgb(125, accent.R, accent.G, accent.B));
        MenuRect(details.X, details.Y, details.Width, 46, Color.FromArgb(238, 9, 15, 23));
        MenuRect(details.X, details.Y, details.Width, 3, MenuCyan);
        MenuText(FitText("DETAIL // " + MenuCategoryShortName(_mainMenuCategory), Math.Max(14, (details.Width - 30) / 7)), details.X + 15, details.Y + 14, IsCompactMenuDetails(details.Width) ? 0.23f : 0.27f, MenuTextPrimary, false, true);

        if (selectedEntry != null)
        {
            DrawSelectedEntryDetails(details, selectedEntry, accent);
        }

        if (_mainMenuCategory == MenuCategory.Justice)
        {
            DrawJusticePanelSummary(details);
        }
        else
        {
            DrawSceneMetrics(details, accent);
        }

        DrawMenuStatus(details, accent);
    }

    private void DrawSelectedEntryDetails(Rectangle details, MainMenuEntry entry, Color accent)
    {
        int top = details.Y + 58;
        bool compactDetails = IsCompactMenuDetails(details.Width);
        int labelMaxChars = Math.Max(14, (details.Width - 46) / 6);
        int valueMaxChars = Math.Max(16, (details.Width - 46) / 5);
        MenuRect(details.X + 12, top, details.Width - 24, 92, MenuCard);
        MenuRect(details.X + 12, top, 3, 92, entry.Kind == MainMenuRowKind.Danger ? MenuDanger : accent);
        MenuText(GetObsidianActionTag(entry), details.X + 23, top + 9, 0.175f, entry.Kind == MainMenuRowKind.Danger ? MenuDanger : MenuCyan, false, false);
        MenuText(FitText(entry.Label, labelMaxChars), details.X + 23, top + 29, compactDetails ? 0.235f : 0.285f, MenuTextPrimary, false, true);
        MenuText(FitText(entry.Value, valueMaxChars), details.X + 23, top + 59, compactDetails ? 0.175f : 0.205f, MenuTextMuted, false, false);

        string hint = GetObsidianActionHint(entry);
        int hintTop = top + 103;
        MenuText("AIDE CONTEXTUELLE", details.X + 13, hintTop, 0.17f, MenuCyan, false, false);
        DrawWrappedMenuHint(hint, details.X + 13, hintTop + 22, details.Width - 26);

        int contextTop = hintTop + 78;
        DrawContextSummary(details, contextTop, accent);
    }

    private static string GetObsidianActionTag(MainMenuEntry entry)
    {
        if (entry.Kind == MainMenuRowKind.Danger)
        {
            return "DANGER";
        }

        if (entry.Kind == MainMenuRowKind.PrimaryAction || entry.Kind == MainMenuRowKind.Action)
        {
            return "ACTION";
        }

        return entry.Kind == MainMenuRowKind.Info ? "INFO" : "REGLAGE";
    }

    private string GetObsidianActionHint(MainMenuEntry entry)
    {
        switch (entry.Action)
        {
            case MainMenuAction.PlacementType:
                return "Choisis explicitement si le portail place est une entree ou une sortie.";
            case MainMenuAction.PrecisePlacement:
                return "Ouvre la camera de placement fin avec apercu transparent.";
            case MainMenuAction.DistancePlacement:
                return "Pose rapidement l'element devant le joueur avec la distance reglee.";
            case MainMenuAction.PlacementDistance:
                return "Gauche/Droite ajuste la distance. Shift accelere le changement.";
            case MainMenuAction.NpcCategory:
                return "Filtre rapidement les peds par famille avant de choisir le modele.";
            case MainMenuAction.NpcModel:
                return CurrentModelOption().IsCustom
                    ? "Modele custom actif : appuie sur T pour saisir le nom exact."
                    : "Choisis le ped a placer dans la categorie NPC active.";
            case MainMenuAction.NpcWeaponCategory:
                return "Change la famille d'armes pour reduire la liste suivante.";
            case MainMenuAction.NpcWeapon:
                return "Choisis l'arme donnee au prochain NPC place.";
            case MainMenuAction.NpcWeaponEditor:
                return "Entree ouvre l'atelier; Gauche/Droite change le preset rapide.";
            case MainMenuAction.NpcHealth:
            case MainMenuAction.NpcArmor:
            case MainMenuAction.NpcPatrolRadius:
                return "Gauche/Droite ajuste la valeur. Shift accelere le changement.";
            case MainMenuAction.NpcBehavior:
                return "Selectionne le comportement IA applique au prochain NPC place.";
            case MainMenuAction.NpcAutoRespawn:
            case MainMenuAction.VehicleAutoRespawn:
            case MainMenuAction.ObjectAutoRespawn:
                return "Active la reapparition automatique quand le joueur quitte la zone.";
            case MainMenuAction.VehicleCategory:
                return "Filtre les vehicules par type pour aller plus vite dans la liste.";
            case MainMenuAction.VehicleModel:
                return "Choisis le vehicule qui sera place ou sauvegarde dans la scene.";
            case MainMenuAction.ObjectCategory:
                return "Filtre les props par usage : securite, butin, soin, mobilier ou decor.";
            case MainMenuAction.ObjectModel:
                return "Choisis l'objet a placer. Les butins affichent leur valeur utile.";
            case MainMenuAction.InteriorCategory:
                return "Filtre le catalogue d'interieurs avant de poser une entree.";
            case MainMenuAction.InteriorModel:
                return "Choisis la destination de l'entree interieure a placer.";
            case MainMenuAction.Save:
                return "Sauvegarde la scene courante dans le fichier XML actif.";
            case MainMenuAction.Load:
                return "Recharge NPC, vehicules, objets et portails depuis le XML actif.";
            case MainMenuAction.JusticeEnabled:
                return "Active ou met en pause Justice pour le héros joué : " +
                       GetJusticePlayedProfileDisplay() +
                       ". Désactiver ne supprime aucun dossier, mandat, casier, amende ou peine. " +
                       "Utilise Réinitialiser ce personnage uniquement pour tout effacer.";
            case MainMenuAction.JusticeProfile:
                return "Gauche/Droite choisit le dossier à consulter, payer ou réinitialiser. " +
                       "Pour payer, sélectionne le héros joué : " +
                       GetJusticePlayedProfileDisplay() +
                       ". L'activation ne suit pas ce sélecteur.";
            case MainMenuAction.JusticeStatus:
                return "Résume l'état judiciaire du personnage sélectionné et ses poursuites encore actives.";
            case MainMenuAction.JusticeLastCrime:
                return "Affiche la dernière infraction retenue pour le personnage sélectionné.";
            case MainMenuAction.JusticeSeverity:
                return "Indique la gravité cumulée des faits reprochés au personnage sélectionné.";
            case MainMenuAction.JusticeWarrant:
                return "Indique si le personnage sélectionné conserve un mandat après la poursuite.";
            case MainMenuAction.JusticeCharges:
                return "Entrée ouvre chaque charge conservée du dossier. Au-delà de " +
                       JusticePolicy.MaxActiveCharges.ToString(CultureInfo.InvariantCulture) + " charges, " +
                       "les faits les plus anciens sont regroupés sur une ligne xN sans disparaître du compteur.";
            case MainMenuAction.JusticeRecord:
                return "Entrée ouvre chaque charge conservée dans les vingt dernières condamnations; " +
                       "une ligne xN signale toute consolidation de sécurité.";
            case MainMenuAction.JusticeFine:
                return "Affiche l'amende du personnage sélectionné sans toucher à son argent.";
            case MainMenuAction.JusticeFineDispute:
                return "Un montant litigieux n'est ni redébité ni traité comme un paiement certain.";
            case MainMenuAction.JusticePayFine:
                return CanJusticeMenuPaySelectedProfile()
                    ? "Entrée demande le paiement volontaire de la dette du héros actuellement joué."
                    : IsJusticeMenuSelectedProfileCurrentlyPlayed()
                        ? "Paiement indisponible avec une tenue/ped custom : reprends brièvement le héros GTA canonique."
                        : "Ce dossier est consulté sans débit. Sélectionne le héros joué : " +
                          GetJusticePlayedProfileDisplay() + " pour payer.";
            case MainMenuAction.JusticeResolveFineDispute:
                return "Entrée ouvre une confirmation explicite : le montant litigieux sera annulé en faveur du joueur, sans nouveau débit.";
            case MainMenuAction.JusticeSentence:
                return "Affiche la peine théorique du dossier du personnage sélectionné.";
            case MainMenuAction.JusticeRecidivism:
                return "Indique la récidive du personnage sélectionné, utilisée pour ses prochaines sanctions.";
            case MainMenuAction.JusticePoliceMode:
                return "Gauche/Droite choisit Désactivée, jeu libre best-effort ou Forcée. Les missions et cinématiques restent protégées.";
            case MainMenuAction.JusticeRecovery:
                return "Libère les contrôles, restaure la police et fusionne le snapshot d'armes sans jamais supprimer l'inventaire courant.";
            case MainMenuAction.JusticeDiagnostic:
                return "Affiche le SHA-256 réellement chargé et le compare au manifest installé; le détail complet est aussi écrit dans le log.";
            case MainMenuAction.JusticeResetProfile:
                return "Entrée ouvre une confirmation avant d'effacer tout le profil Justice du personnage sélectionné.";
            case MainMenuAction.CleanNpcs:
            case MainMenuAction.CleanVehicles:
            case MainMenuAction.CleanObjects:
            case MainMenuAction.CleanInteriorPortals:
                return "Entree ouvre une confirmation. Entree confirme, Echap annule.";
            case MainMenuAction.TerminatorMode:
                return "Active ou desactive le mode T-800 sans modifier son etat pendant l'ouverture du menu.";
            case MainMenuAction.ExitActiveInfo:
            case MainMenuAction.ExitDestinationInfo:
                return "Place une entree, entre dedans, puis pose une sortie dans l'interieur.";
            default:
                return "Gauche/Droite modifie la valeur selectionnee.";
        }
    }

    private string GetJusticePlayedActivationDisplay()
    {
        if (_justiceProfileSwitchPersistencePending)
        {
            return "SAUVEGARDE DU CHANGEMENT EN COURS";
        }
        if (_justiceProfileSelectionPending ||
            !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot))
        {
            return "IDENTIFICATION DU PERSONNAGE EN COURS";
        }
        if (_justiceProfileContextBlocked ||
            !IsJusticeRuntimeProfileContextCompatible())
        {
            return "SUSPENDU · CHANGEMENT EN ATTENTE";
        }
        if (!IsJusticePlayedProfileContextReady())
        {
            return "SUSPENDU · CONTEXTE INDISPONIBLE";
        }
        return (_justiceEnabled ? "ACTIVÉE" : "DÉSACTIVÉE") + " · " +
               GetJusticePlayedProfileDisplay().ToUpperInvariant();
    }

    private string GetJusticePlayedProfileDisplay()
    {
        switch (_justiceActivePlayerProfileSlot)
        {
            case 0: return "Michael";
            case 1: return "Franklin";
            case 2: return "Trevor";
            default: return "identité en attente";
        }
    }

    private bool IsJusticeMenuSelectedProfileCurrentlyPlayed()
    {
        return IsJusticePlayedProfileContextReady() &&
               GetJusticeMenuSelectedProfileSlot() == _justiceActivePlayerProfileSlot;
    }

    private string GetJusticeSelectedProfileContextDisplay()
    {
        return JusticeDisplayOrFallback(GetJusticeMenuSelectedProfileDisplay()) +
               (IsJusticeMenuSelectedProfileCurrentlyPlayed()
                   ? " · JOUÉ"
                   : " · CONSULTATION");
    }

    private string GetJusticeSelectedFinePaymentDisplay()
    {
        string fine = JusticeDisplayOrFallback(GetJusticeMenuSelectedFineDisplay());
        bool canPaySelectedProfile = CanJusticeMenuPaySelectedProfile();
        JusticeCaseState selectedCase = GetJusticeMenuSelectedCaseState();
        if (canPaySelectedProfile &&
            (selectedCase == null || selectedCase.FineDue <= 0L))
        {
            return fine + " · aucune dette";
        }

        return canPaySelectedProfile
            ? fine + " · payer"
            : IsJusticeMenuSelectedProfileCurrentlyPlayed()
                ? fine + " · indisponible"
                : fine + " · consultation";
    }

    private static bool IsObsidianValueEditable(MainMenuAction action)
    {
        switch (action)
        {
            case MainMenuAction.PlacementType:
            case MainMenuAction.PlacementDistance:
            case MainMenuAction.NpcCategory:
            case MainMenuAction.NpcModel:
            case MainMenuAction.NpcWeaponCategory:
            case MainMenuAction.NpcWeapon:
            case MainMenuAction.NpcWeaponEditor:
            case MainMenuAction.NpcHealth:
            case MainMenuAction.NpcArmor:
            case MainMenuAction.NpcBehavior:
            case MainMenuAction.NpcPatrolRadius:
            case MainMenuAction.NpcAutoRespawn:
            case MainMenuAction.VehicleCategory:
            case MainMenuAction.VehicleModel:
            case MainMenuAction.VehicleAutoRespawn:
            case MainMenuAction.ObjectCategory:
            case MainMenuAction.ObjectModel:
            case MainMenuAction.ObjectAutoRespawn:
            case MainMenuAction.InteriorCategory:
            case MainMenuAction.InteriorModel:
            case MainMenuAction.JusticeEnabled:
            case MainMenuAction.JusticeProfile:
            case MainMenuAction.JusticePoliceMode:
            case MainMenuAction.TerminatorMode:
                return true;
            default:
                return false;
        }
    }

    private void DrawWrappedMenuHint(string text, int x, int y, int width)
    {
        string safe = text ?? string.Empty;
        int maxChars = Math.Max(20, width / 6);

        if (safe.Length <= maxChars)
        {
            MenuText(safe, x, y, 0.19f, MenuTextMuted, false, false);
            return;
        }

        int split = safe.LastIndexOf(' ', Math.Min(maxChars, safe.Length - 1));

        if (split < maxChars / 2)
        {
            split = Math.Min(maxChars, safe.Length);
        }

        MenuText(safe.Substring(0, split).Trim(), x, y, 0.19f, MenuTextMuted, false, false);
        MenuText(FitText(safe.Substring(split).Trim(), maxChars), x, y + 18, 0.19f, MenuTextMuted, false, false);
    }

    private void DrawContextSummary(Rectangle details, int top, Color accent)
    {
        switch (_mainMenuCategory)
        {
            case MenuCategory.Vehicle:
                DrawDetailLine(details, top, "MODELE", CurrentVehicleDisplayName(), MenuCyan);
                DrawDetailLine(details, top + 27, "CLASSE", CurrentVehicleCategory().Name, MenuCyan);
                DrawDetailLine(details, top + 54, "DISTANCE", _selectedDistance.ToString(CultureInfo.InvariantCulture) + " m", accent);
                break;

            case MenuCategory.Object:
                DrawDetailLine(details, top, "OBJET", CurrentObjectDisplayName(), MenuAmber);
                DrawDetailLine(details, top + 27, "CATEGORIE", CurrentObjectCategory().Name, MenuAmber);
                DrawDetailLine(details, top + 54, "RESPAWN", BoolText(_selectedAutoRespawn), MenuGreen);
                break;

            case MenuCategory.Interior:
                DrawDetailLine(details, top, "PORTAIL", _selectedPlacementType == PlacementEntityType.Exit ? "Sortie" : "Entrée", MenuPurple);
                DrawDetailLine(details, top + 27, "INTERIEUR", CurrentInteriorOption().DisplayName, MenuPurple);
                DrawDetailLine(details, top + 54, "SESSION", ActiveInteriorSessionDisplayName(), MenuGreen);
                break;

            case MenuCategory.Scene:
                DrawDetailLine(details, top, "FICHIER", string.IsNullOrEmpty(_lastSaveFileName) ? "Aucun" : _lastSaveFileName, MenuGreen);
                DrawDetailLine(details, top + 27, "CONTENU", "NPC / VEH / OBJ / INT", MenuCyan);
                DrawDetailLine(details, top + 54, "FORMAT", "XML compatible + .bak", MenuGreen);
                break;

            case MenuCategory.Justice:
                DrawDetailLine(details, top, "STATUT", JusticeDisplayOrFallback(GetJusticeMenuSelectedStatusDisplay()), MenuJustice);
                DrawDetailLine(details, top + 27, "INFRACTION", JusticeDisplayOrFallback(GetJusticeMenuSelectedLastCrimeDisplay()), MenuDanger);
                DrawDetailLine(details, top + 54, "MANDAT", JusticeDisplayOrFallback(GetJusticeMenuSelectedWarrantDisplay()), MenuJustice);
                break;

            case MenuCategory.Tools:
                DrawDetailLine(details, top, "TERMINATOR", _terminatorModeEnabled ? "ACTIF" : "INACTIF", _terminatorModeEnabled ? MenuRed : MenuTextMuted);
                DrawDetailLine(details, top + 27, "SECURITE", "Confirmation active", MenuGreen);
                DrawDetailLine(details, top + 54, "PORTEE", "Entites gerees DonJ", MenuCyan);
                break;

            case MenuCategory.Npc:
            default:
                DrawDetailLine(details, top, "MODELE", CurrentModelDisplayName(), MenuRed);
                DrawDetailLine(details, top + 27, "ARME", CurrentWeaponDisplayName(), MenuRed);
                DrawDetailLine(details, top + 54, "IA", NpcBehaviorDisplayName(_selectedBehavior), MenuGreen);
                break;
        }
    }

    private void DrawDetailLine(Rectangle details, int y, string label, string value, Color accent)
    {
        bool compact = IsCompactMenuDetails(details.Width);
        int valueX = details.X + (compact ? Math.Max(64, details.Width * 2 / 5) : 91);
        int maxValueChars = Math.Max(8, (details.Right - valueX - 9) / 5);
        MenuRect(details.X + 12, y, details.Width - 24, 22, Color.FromArgb(95, 31, 41, 54));
        MenuRect(details.X + 12, y, 3, 22, accent);
        MenuText(FitText(label, compact ? 7 : 11), details.X + 21, y + 5, compact ? 0.145f : 0.17f, MenuTextMuted, false, false);
        MenuText(FitText(value, maxValueChars), valueX, y + 5, compact ? 0.16f : 0.19f, MenuTextPrimary, false, false);
    }

    private void DrawSceneMetrics(Rectangle details, Color accent)
    {
        int y = details.Bottom - 82;
        int gap = 5;
        int width = Math.Max(34, (details.Width - 24 - gap * 3) / 4);
        DrawMetric(details.X + 12, y, width, "NPC", _spawnedNpcs.Count, MenuRed);
        DrawMetric(details.X + 12 + (width + gap), y, width, "VEH", _placedVehicles.Count, MenuCyan);
        DrawMetric(details.X + 12 + (width + gap) * 2, y, width, "OBJ", _placedObjects.Count, MenuAmber);
        DrawMetric(details.X + 12 + (width + gap) * 3, y, width, "INT", _placedInteriorPortals.Count, MenuPurple);

        MenuRect(details.X + 12, details.Bottom - 34, details.Width - 24, 22, Color.FromArgb(100, 31, 42, 54));
        MenuRect(details.X + 12, details.Bottom - 34, 3, 22, MenuGreen);
        string save = string.IsNullOrEmpty(_lastSaveFileName) ? "Aucun fichier" : _lastSaveFileName;
        MenuText("SAVE  " + FitText(save, Math.Max(10, (details.Width - 65) / 5)), details.X + 22, details.Bottom - 29, IsCompactMenuDetails(details.Width) ? 0.15f : 0.18f, MenuTextMuted, false, false);
    }

    private void DrawJusticePanelSummary(Rectangle details)
    {
        int y = details.Bottom - 82;
        int gap = 5;
        int width = Math.Max(34, (details.Width - 24 - gap * 3) / 4);
        DrawJusticeMetric(details.X + 12, y, width, "GRAV", GetJusticeMenuSelectedSeverityDisplay(), MenuJustice);
        DrawJusticeMetric(details.X + 12 + (width + gap), y, width, "CHRG", GetJusticeMenuSelectedChargesDisplay(), MenuCyan);
        DrawJusticeMetric(details.X + 12 + (width + gap) * 2, y, width, "AMENDE", GetJusticeMenuSelectedFineDisplay(), MenuAmber);
        DrawJusticeMetric(details.X + 12 + (width + gap) * 3, y, width, "PEINE", GetJusticeMenuSelectedSentenceDisplay(), MenuDanger);

        MenuRect(details.X + 12, details.Bottom - 34, details.Width - 24, 22, Color.FromArgb(100, 31, 42, 54));
        MenuRect(details.X + 12, details.Bottom - 34, 3, 22, MenuJustice);
        MenuText("RECIDIVE", details.X + 22, details.Bottom - 29, IsCompactMenuDetails(details.Width) ? 0.145f : 0.17f, MenuTextMuted, false, false);
        MenuText(
            FitText(JusticeDisplayOrFallback(GetJusticeMenuSelectedRecidivismDisplay()), Math.Max(8, (details.Width - 104) / 5)),
            details.X + Math.Max(82, details.Width * 2 / 5),
            details.Bottom - 29,
            IsCompactMenuDetails(details.Width) ? 0.15f : 0.18f,
            MenuTextPrimary,
            false,
            false);
    }

    private void DrawJusticeMetric(int x, int y, int width, string label, string value, Color accent)
    {
        MenuRect(x, y, width, 37, Color.FromArgb(110, 31, 42, 55));
        MenuRect(x, y, width, 2, accent);
        MenuText(FitText(label, Math.Max(4, width / 6)), x + width / 2, y + 6, 0.135f, MenuTextMuted, true, false);
        MenuText(FitText(JusticeDisplayOrFallback(value), Math.Max(4, width / 6)), x + width / 2, y + 19, 0.18f, MenuTextPrimary, true, true);
    }

    private void DrawMetric(int x, int y, int width, string label, int value, Color accent)
    {
        MenuRect(x, y, width, 37, Color.FromArgb(110, 31, 42, 55));
        MenuRect(x, y, width, 2, accent);
        MenuText(label, x + width / 2, y + 6, 0.15f, MenuTextMuted, true, false);
        MenuText(value.ToString(CultureInfo.InvariantCulture), x + width / 2, y + 19, 0.22f, MenuTextPrimary, true, true);
    }

    private void DrawMenuStatus(Rectangle details, Color accent)
    {
        if (GetMenuGameTimeSafe() > _statusUntil || string.IsNullOrEmpty(_statusText))
        {
            return;
        }

        int y = details.Bottom - 126;
        MenuRect(details.X + 12, y, details.Width - 24, 35, Color.FromArgb(205, 13, 45, 52));
        MenuRect(details.X + 12, y, 3, 35, accent);
        MenuText("SYSTEME", details.X + 22, y + 5, 0.15f, MenuCyan, false, false);
        MenuText(FitText(_statusText, Math.Max(18, (details.Width - 44) / 5)), details.X + 22, y + 17, IsCompactMenuDetails(details.Width) ? 0.15f : 0.175f, MenuTextPrimary, false, false);
    }

    private void DrawObsidianFooter(MenuLayout layout, bool weaponEditor)
    {
        Rectangle footer = Offset(layout.Footer, _menuFrameOffsetX, 0);
        Color accent = GetMenuCategoryAccent(weaponEditor ? MenuCategory.Npc : _mainMenuCategory);
        MenuRect(footer.X, footer.Y, footer.Width, footer.Height, Color.FromArgb(242, 8, 13, 20));
        DrawMenuFrame(footer, Color.FromArgb(115, accent.R, accent.G, accent.B));
        MenuRect(footer.X, footer.Y, footer.Width, 2, accent);

        bool compactFooter = IsCompactMenuFooter(footer.Width);
        string mode = weaponEditor ? "ATELIER D'ARMES" : MenuCategoryTitle(_mainMenuCategory);
        MenuText(FitText("DONJ // " + mode, compactFooter ? 32 : 52), footer.X + 17, footer.Y + 11, compactFooter ? 0.17f : 0.205f, MenuTextPrimary, false, true);
        MenuText(weaponEditor ? "Échap retour" : "Tab change de catégorie", footer.X + 18, footer.Y + 34, compactFooter ? 0.145f : 0.17f, MenuTextMuted, false, false);

        int chipY = footer.Y + 14;
        int right = footer.Right - 12;
        right = DrawKeyChipRight(right, chipY, compactFooter ? 46 : 58, MenuToggleKeyLabel, MenuRed);
        right = DrawKeyChipRight(right - (compactFooter ? 4 : 6), chipY, compactFooter ? 58 : 72, "ENTRÉE", accent);
        right = DrawKeyChipRight(right - (compactFooter ? 4 : 6), chipY, compactFooter ? 48 : 58, "G / D", MenuCyan);
        DrawKeyChipRight(right - (compactFooter ? 4 : 6), chipY, compactFooter ? 56 : 66, "HAUT/BAS", MenuTextMuted);
    }

    private int DrawKeyChipRight(int right, int y, int width, string label, Color accent)
    {
        int x = right - width;
        MenuRect(x, y, width, 28, Color.FromArgb(130, 32, 42, 55));
        MenuRect(x, y, 3, 28, accent);
        MenuText(label, x + width / 2 + 1, y + 7, 0.18f, MenuTextPrimary, true, false);
        return x;
    }

    private void DrawWeaponEditorContent(MenuLayout layout)
    {
        Rectangle content = Offset(layout.Content, _menuFrameOffsetX, 0);
        Rectangle header = Offset(layout.Header, _menuFrameOffsetX, 0);
        Color accent = MenuRed;
        MenuRect(content.X, content.Y, content.Width, content.Height, MenuGraphite);
        DrawMenuFrame(content, Color.FromArgb(125, accent.R, accent.G, accent.B));
        MenuRect(header.X, header.Y, header.Width, header.Height, Color.FromArgb(236, 11, 17, 25));
        MenuRect(header.X, header.Y, 4, header.Height, accent);
        bool compactContent = IsCompactMenuContent(content.Width);
        MenuText("ATELIER D'ARMES", header.X + (compactContent ? 14 : 20), header.Y + 13, compactContent ? 0.30f : 0.37f, MenuTextPrimary, false, true);
        MenuText(FitText(CurrentWeaponDisplayName(), compactContent ? Math.Max(18, (content.Width - 125) / 5) : 48), header.X + (compactContent ? 15 : 21), header.Y + 44, compactContent ? 0.17f : 0.205f, MenuTextMuted, false, false);
        DrawMenuStat(header.Right - 92, header.Y + 16, 78, "LIGNE", (_weaponEditorIndex + 1).ToString(CultureInfo.InvariantCulture) + "/12", MenuCyan);

        int bodyTop = header.Bottom + 9;
        int bodyBottom = content.Bottom - 10;
        int rowHeight = Math.Max(24, Math.Min(MenuRowHeight, (bodyBottom - bodyTop) / WeaponEditorItemCount));
        int valueX = content.X + Math.Max(190, (int)(content.Width * 0.56f));
        int selectedTargetY = bodyTop + _weaponEditorIndex * rowHeight;

        UpdateMenuSelectionAnimation(selectedTargetY);

        MenuRect(content.X + 7, (int)Math.Round(_menuSelectionVisualY), content.Width - 14, rowHeight, Color.FromArgb(140, accent.R, accent.G, accent.B));

        for (int i = 0; i < WeaponEditorItemCount; i++)
        {
            int y = bodyTop + i * rowHeight;
            bool selected = i == _weaponEditorIndex;
            MenuRect(content.X + 7, y + 1, content.Width - 14, rowHeight - 2, selected ? Color.FromArgb(130, 46, 35, 48) : Color.FromArgb(58, 35, 45, 59));
            MenuRect(content.X + 7, y + 1, selected ? 4 : 2, rowHeight - 2, selected ? accent : Color.FromArgb(100, accent.R, accent.G, accent.B));
            MenuText(FitText(WeaponEditorLabel(i), Math.Max(12, (valueX - content.X - 28) / 6)), content.X + 21, y + Math.Max(5, (rowHeight - 19) / 2), 0.226f, selected ? MenuTextPrimary : MenuTextMuted, false, selected);
            MenuText(FitText(WeaponEditorValue(i), Math.Max(12, (content.Right - valueX - 15) / 6)), valueX, y + Math.Max(5, (rowHeight - 19) / 2), 0.218f, selected ? accent : Color.FromArgb(220, 194, 205, 220), false, selected);
        }
    }

    private void DrawWeaponEditorDetails(MenuLayout layout)
    {
        Rectangle details = Offset(layout.Details, _menuFrameOffsetX, 0);
        MenuRect(details.X, details.Y, details.Width, details.Height, MenuGraphite);
        DrawMenuFrame(details, Color.FromArgb(125, MenuRed.R, MenuRed.G, MenuRed.B));
        MenuRect(details.X, details.Y, details.Width, 46, Color.FromArgb(238, 9, 15, 23));
        MenuRect(details.X, details.Y, details.Width, 3, MenuCyan);
        MenuText(FitText("LOADOUT // NPC", Math.Max(14, (details.Width - 30) / 7)), details.X + 15, details.Y + 14, IsCompactMenuDetails(details.Width) ? 0.23f : 0.27f, MenuTextPrimary, false, true);

        int top = details.Y + 61;
        DrawDetailLine(details, top, "ARME", CurrentWeaponDisplayName(), MenuRed);
        DrawDetailLine(details, top + 29, "PRESET", WeaponPresetDisplayName(_selectedWeaponLoadout.Preset), MenuRed);
        DrawDetailLine(details, top + 58, "VISEE", ScopeDisplayName(_selectedWeaponLoadout.Scope), MenuCyan);
        DrawDetailLine(details, top + 87, "MUNITIONS", Mk2AmmoDisplayName(_selectedWeaponLoadout.Mk2Ammo), MenuAmber);
        DrawDetailLine(details, top + 116, "TEINTE", _selectedWeaponLoadout.Tint.ToString(CultureInfo.InvariantCulture), MenuPurple);

        MenuRect(details.X + 12, top + 158, details.Width - 24, 83, MenuCard);
        MenuRect(details.X + 12, top + 158, 3, 83, MenuCyan);
        bool compactDetails = IsCompactMenuDetails(details.Width);
        MenuText("CONFIGURATION", details.X + 22, top + 169, compactDetails ? 0.15f : 0.17f, MenuCyan, false, false);
        MenuText(FitText(_selectedWeaponLoadout.Summary(), Math.Max(16, (details.Width - 44) / 5)), details.X + 22, top + 192, compactDetails ? 0.175f : 0.205f, MenuTextPrimary, false, true);
        MenuText(FitText("Gauche/Droite modifie. Entrée applique.", Math.Max(18, (details.Width - 44) / 5)), details.X + 22, top + 216, compactDetails ? 0.145f : 0.17f, MenuTextMuted, false, false);

        DrawSceneMetrics(details, MenuRed);
        DrawMenuStatus(details, MenuRed);
    }

    private string WeaponEditorLabel(int index)
    {
        switch (index)
        {
            case 0: return "Retour";
            case 1: return "Preset";
            case 2: return "Chargeur etendu";
            case 3: return "Silencieux";
            case 4: return "Lampe";
            case 5: return "Poignee";
            case 6: return "Visee";
            case 7: return "Frein de bouche";
            case 8: return "Canon ameliore";
            case 9: return "Munitions MK2";
            case 10: return "Teinte";
            case 11: return "Appliquer aux NPC";
            default: return string.Empty;
        }
    }

    private string WeaponEditorValue(int index)
    {
        switch (index)
        {
            case 0: return "Menu principal";
            case 1: return WeaponPresetDisplayName(_selectedWeaponLoadout.Preset);
            case 2: return BoolText(_selectedWeaponLoadout.ExtendedClip);
            case 3: return BoolText(_selectedWeaponLoadout.Suppressor);
            case 4: return BoolText(_selectedWeaponLoadout.Flashlight);
            case 5: return BoolText(_selectedWeaponLoadout.Grip);
            case 6: return ScopeDisplayName(_selectedWeaponLoadout.Scope);
            case 7: return BoolText(_selectedWeaponLoadout.Muzzle);
            case 8: return BoolText(_selectedWeaponLoadout.ImprovedBarrel);
            case 9: return Mk2AmmoDisplayName(_selectedWeaponLoadout.Mk2Ammo);
            case 10: return _selectedWeaponLoadout.Tint.ToString(CultureInfo.InvariantCulture);
            case 11: return "Arme identique pour tous";
            default: return string.Empty;
        }
    }

    private void DrawDangerConfirmation(
        MenuLayout layout,
        MainMenuAction action)
    {
        Rectangle left = Offset(
            layout.Content,
            _menuFrameOffsetX,
            0);

        Rectangle right = Offset(
            layout.Details,
            _menuFrameOffsetX,
            0);

        Rectangle area = Rectangle.FromLTRB(
            left.X,
            left.Y,
            right.Right,
            left.Bottom);

        MenuRect(
            area.X,
            area.Y,
            area.Width,
            area.Height,
            Color.FromArgb(205, 2, 5, 9));

        int width = Math.Min(540, area.Width - 40);
        int height = 222;
        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (area.Height - height) / 2;

        MenuRect(
            x + 5,
            y + 7,
            width,
            height,
            Color.FromArgb(130, 0, 0, 0));

        MenuRect(
            x,
            y,
            width,
            height,
            Color.FromArgb(248, 17, 11, 17));

        DrawMenuFrame(
            new Rectangle(x, y, width, height),
            MenuDanger);

        MenuRect(
            x,
            y,
            width,
            4,
            MenuDanger);

        bool justicePayment =
            action == MainMenuAction.JusticePayFine;

        bool justiceDispute =
            action == MainMenuAction.JusticeResolveFineDispute;

        bool justiceProfileReset =
            action == MainMenuAction.JusticeResetProfile;

        string selectedJusticeProfile =
            JusticeDisplayOrFallback(
                _pendingDangerJusticeProfileDisplay);

        string title = justiceProfileReset
            ? "CONFIRMATION DE RÉINITIALISATION"
            : justiceDispute
                ? "CONFIRMATION DE RÉSOLUTION"
                : justicePayment
                    ? "CONFIRMATION DE PAIEMENT"
                    : "CONFIRMATION DE NETTOYAGE";

        string firstDetail = justiceProfileReset
            ? "Personnage : " + selectedJusticeProfile
            : justiceDispute
                ? "Personnage : " +
                  selectedJusticeProfile +
                  " · Litige : " +
                  FormatJusticeMoney(
                      _pendingDangerJusticeFineAmount)
                : justicePayment
                    ? "Personnage : " +
                      selectedJusticeProfile +
                      " · Dette : " +
                      JusticeDisplayOrFallback(
                          _pendingDangerJusticeFineDisplay)
                    : DangerActionCount(action) +
                      " element(s) geres par DonJ sont concernes.";

        string secondDetail = justiceProfileReset
            ? "Casier, dossier, récidive, dette et détention seront effacés."
            : justiceDispute
                ? "Aucun nouveau débit : le montant sera annulé explicitement en faveur du joueur."
                : justicePayment
                    ? "Le débit restera plafonné au montant confirmé et au cash disponible."
                    : "Cette action ne touche pas aux sauvegardes XML.";

        MenuText(
            title,
            x + width / 2,
            y + 23,
            0.32f,
            MenuTextPrimary,
            true,
            true);

        MenuText(
            DangerActionDisplayName(action),
            x + width / 2,
            y + 61,
            0.29f,
            MenuDanger,
            true,
            true);

        MenuText(
            firstDetail,
            x + width / 2,
            y + 96,
            0.21f,
            MenuTextMuted,
            true,
            false);

        MenuText(
            secondDetail,
            x + width / 2,
            y + 121,
            0.19f,
            MenuTextMuted,
            true,
            false);

        int chipY = y + 158;

        DrawConfirmationChip(
            x + width / 2 - 166,
            chipY,
            152,
            "ENTREE  CONFIRMER",
            MenuDanger);

        DrawConfirmationChip(
            x + width / 2 + 14,
            chipY,
            152,
            "ECHAP  ANNULER",
            MenuCyan);
    }

    private void DrawConfirmationChip(int x, int y, int width, string text, Color accent)
    {
        MenuRect(x, y, width, 34, Color.FromArgb(180, 34, 30, 40));
        MenuRect(x, y, 4, 34, accent);
        MenuText(text, x + width / 2, y + 9, 0.19f, MenuTextPrimary, true, true);
    }

    private string DangerActionDisplayName(MainMenuAction action)
    {
        switch (action)
        {
            case MainMenuAction.CleanNpcs:
                return "SUPPRIMER TOUS LES NPC";

            case MainMenuAction.CleanVehicles:
                return "SUPPRIMER TOUS LES VEHICULES";

            case MainMenuAction.CleanObjects:
                return "SUPPRIMER TOUS LES OBJETS";

            case MainMenuAction.CleanInteriorPortals:
                return "SUPPRIMER TOUS LES PORTAILS";

            case MainMenuAction.JusticePayFine:
                return "PAYER LA DETTE DU HÉROS JOUÉ";

            case MainMenuAction.JusticeResolveFineDispute:
                return "ANNULER LE MONTANT LITIGIEUX";

            case MainMenuAction.JusticeResetProfile:
                return "RÉINITIALISER CE PERSONNAGE";

            default:
                return "ACTION DE NETTOYAGE";
        }
    }

    private int DangerActionCount(MainMenuAction action)
    {
        switch (action)
        {
            case MainMenuAction.CleanNpcs: return _spawnedNpcs.Count;
            case MainMenuAction.CleanVehicles: return _placedVehicles.Count;
            case MainMenuAction.CleanObjects: return _placedObjects.Count;
            case MainMenuAction.CleanInteriorPortals: return _placedInteriorPortals.Count;
            default: return 0;
        }
    }

    private void DrawDonJMonogram(Rectangle area, Color accent)
    {
        MenuRect(area.X, area.Y, area.Width, 2, accent);
        MenuRect(area.X, area.Bottom - 2, area.Width, 2, MenuCyan);
        MenuRect(area.X, area.Y, 2, 15, accent);
        MenuRect(area.Right - 2, area.Bottom - 15, 2, 15, MenuCyan);
        MenuRect(area.X + 7, area.Y + 7, area.Width - 14, area.Height - 14, Color.FromArgb(105, 30, 42, 55));
        MenuText("DJ", area.X + area.Width / 2, area.Y + 12, 0.48f, MenuTextPrimary, true, true);
        MenuRect(area.X + area.Width / 2 - 15, area.Bottom - 11, 30, 2, accent);
    }

    private void DrawCategoryIcon(MenuCategory category, Rectangle bounds, Color color)
    {
        int x = bounds.X;
        int y = bounds.Y;
        int w = Math.Max(18, bounds.Width);
        int h = Math.Max(18, bounds.Height);
        int centerX = x + w / 2;

        switch (category)
        {
            case MenuCategory.Npc:
                MenuRect(centerX - 4, y + 1, 8, 8, color);
                MenuRect(centerX - 8, y + 11, 16, 9, color);
                MenuRect(centerX - 11, y + 20, 5, 3, color);
                MenuRect(centerX + 6, y + 20, 5, 3, color);
                break;

            case MenuCategory.Vehicle:
                MenuRect(x + 2, y + 11, w - 4, 8, color);
                MenuRect(x + 7, y + 6, w - 14, 6, color);
                MenuRect(x + 5, y + 19, 5, 4, color);
                MenuRect(x + w - 10, y + 19, 5, 4, color);
                break;

            case MenuCategory.Object:
                DrawMenuFrame(new Rectangle(x + 4, y + 3, w - 8, h - 6), color);
                MenuRect(centerX - 1, y + 4, 2, h - 8, color);
                MenuRect(x + 5, y + h / 2, w - 10, 2, color);
                break;

            case MenuCategory.Interior:
                MenuRect(x + 3, y + 3, w - 6, 3, color);
                MenuRect(x + 5, y + 6, 3, h - 7, color);
                MenuRect(x + w - 8, y + 6, 3, h - 7, color);
                MenuRect(centerX - 1, y + 10, 2, h - 11, color);
                break;

            case MenuCategory.Scene:
                DrawMenuFrame(new Rectangle(x + 4, y + 2, w - 8, h - 4), color);
                MenuRect(x + 8, y + 5, w - 16, 6, color);
                MenuRect(x + 9, y + h - 8, w - 18, 5, color);
                break;

            case MenuCategory.Justice:
                MenuRect(centerX - 1, y + 2, 2, h - 5, color);
                MenuRect(x + 4, y + 6, w - 8, 2, color);
                MenuRect(x + 5, y + 8, 2, 6, color);
                MenuRect(x + w - 7, y + 8, 2, 6, color);
                MenuRect(x + 2, y + 14, 9, 2, color);
                MenuRect(x + w - 11, y + 14, 9, 2, color);
                MenuRect(x + 4, y + 16, 5, 5, color);
                MenuRect(x + w - 9, y + 16, 5, 5, color);
                MenuRect(centerX - 6, y + h - 4, 12, 2, color);
                break;

            case MenuCategory.Tools:
                MenuRect(centerX - 2, y + 2, 4, h - 4, color);
                MenuRect(x + 3, y + h / 2 - 2, w - 6, 4, color);
                MenuRect(centerX - 5, y + 5, 10, 3, color);
                break;
        }
    }

    private static string MenuCategoryDisplayName(MenuCategory category)
    {
        switch (category)
        {
            case MenuCategory.Vehicle: return "VÉHICULES";
            case MenuCategory.Object: return "OBJETS";
            case MenuCategory.Interior: return "INTÉRIEURS";
            case MenuCategory.Scene: return "SCÈNE";
            case MenuCategory.Justice: return "JUSTICE";
            case MenuCategory.Tools: return "OUTILS";
            case MenuCategory.Npc:
            default: return "NPC";
        }
    }

    private static string MenuCategoryTitle(MenuCategory category)
    {
        switch (category)
        {
            case MenuCategory.Vehicle: return "VÉHICULES // DÉPLOIEMENT";
            case MenuCategory.Object: return "OBJETS // ENVIRONNEMENT";
            case MenuCategory.Interior: return "INTÉRIEURS // PORTAILS";
            case MenuCategory.Scene: return "SCÈNE // SAUVEGARDE";
            case MenuCategory.Justice: return "JUSTICE // DOSSIER";
            case MenuCategory.Tools: return "OUTILS // SYSTÈME";
            case MenuCategory.Npc:
            default: return "NPC // CONFIGURATION";
        }
    }

    private static string MenuCategorySubtitle(MenuCategory category)
    {
        switch (category)
        {
            case MenuCategory.Vehicle: return "Choix, placement et réapparition des véhicules";
            case MenuCategory.Object: return "Props, interactions et composition de scène";
            case MenuCategory.Interior: return "Entrées, sorties et destinations intérieures";
            case MenuCategory.Scene: return "Persistance XML compatible avec les anciennes sauvegardes";
            case MenuCategory.Justice: return "Infractions, mandat, charges et sanctions en cours";
            case MenuCategory.Tools: return "Modes spéciaux et nettoyage protégé";
            case MenuCategory.Npc:
            default: return "Modele, armement, resistance et comportement IA";
        }
    }

    private static string MenuCategoryShortName(MenuCategory category)
    {
        switch (category)
        {
            case MenuCategory.Vehicle: return "VEH";
            case MenuCategory.Object: return "OBJ";
            case MenuCategory.Interior: return "INT";
            case MenuCategory.Scene: return "SAVE";
            case MenuCategory.Justice: return "JUS";
            case MenuCategory.Tools: return "SYS";
            case MenuCategory.Npc:
            default: return "NPC";
        }
    }

    private static bool IsCompactMenuRail(int width)
    {
        return width < 124;
    }

    private static bool IsCompactMenuContent(int width)
    {
        return width < 430;
    }

    private static bool IsCompactMenuDetails(int width)
    {
        return width < 210;
    }

    private static bool IsCompactMenuFooter(int width)
    {
        return width < 650;
    }

    private static Color GetMenuCategoryAccent(MenuCategory category)
    {
        switch (category)
        {
            case MenuCategory.Vehicle: return MenuCyan;
            case MenuCategory.Object: return MenuAmber;
            case MenuCategory.Interior: return MenuPurple;
            case MenuCategory.Scene: return MenuGreen;
            case MenuCategory.Justice: return MenuJustice;
            case MenuCategory.Tools: return MenuDanger;
            case MenuCategory.Npc:
            default: return MenuRed;
        }
    }

    private void ResetMenuSelectionAnimation()
    {
        _menuSelectionVisualY = -1.0f;
        _menuSelectionStartY = -1.0f;
        _menuSelectionTargetY = -1.0f;
        _menuSelectionAnimationStartedAt = 0;
    }

    private void UpdateMenuSelectionAnimation(float targetY)
    {
        int now = GetMenuGameTimeSafe();

        if (_menuSelectionVisualY < 0.0f)
        {
            _menuSelectionVisualY = targetY;
            _menuSelectionStartY = targetY;
            _menuSelectionTargetY = targetY;
            _menuSelectionAnimationStartedAt = now;
            return;
        }

        if (Math.Abs(targetY - _menuSelectionTargetY) > 0.01f)
        {
            _menuSelectionStartY = _menuSelectionVisualY;
            _menuSelectionTargetY = targetY;
            _menuSelectionAnimationStartedAt = now;
        }

        int elapsed = now - _menuSelectionAnimationStartedAt;
        _menuSelectionVisualY = InterpolateMenuSelection(
            _menuSelectionStartY,
            _menuSelectionTargetY,
            elapsed,
            MenuSelectionAnimationMs);
    }

    private static float InterpolateMenuSelection(float start, float target, int elapsedMs, int durationMs)
    {
        if (durationMs <= 0 || elapsedMs >= durationMs)
        {
            return target;
        }

        float progress = elapsedMs <= 0 ? 0.0f : elapsedMs / (float)durationMs;
        float eased = EaseOutCubic(progress);
        return start + (target - start) * eased;
    }

    private float GetMenuCategoryTransitionAlpha()
    {
        int elapsed = GetMenuGameTimeSafe() - _menuCategoryTransitionStartedAt;

        if (elapsed <= 0)
        {
            return 0.72f;
        }

        return Math.Min(1.0f, 0.72f + elapsed / (float)MenuCategoryAnimationMs * 0.28f);
    }

    private void DrawMenuStat(int x, int y, int width, string label, string value, Color accent)
    {
        MenuRect(x, y, width, 44, Color.FromArgb(110, 30, 40, 53));
        MenuRect(x, y, 3, 44, accent);
        MenuText(label, x + 10, y + 7, 0.15f, MenuTextMuted, false, false);
        MenuText(FitText(value, 10), x + 10, y + 23, 0.21f, MenuTextPrimary, false, true);
    }

    private void DrawObsidianScrollbar(int x, int y, int height, int entryCount, int visibleRows)
    {
        DrawObsidianScrollbar(
            x,
            y,
            height,
            entryCount,
            visibleRows,
            _mainMenuScrollOffset,
            GetMenuCategoryAccent(_mainMenuCategory));
    }

    private void DrawObsidianScrollbar(
        int x,
        int y,
        int height,
        int entryCount,
        int visibleRows,
        int scrollOffset,
        Color accent)
    {
        if (entryCount <= visibleRows || height <= 0)
        {
            return;
        }

        MenuRect(x, y, 3, height, Color.FromArgb(90, 72, 84, 101));
        int thumbHeight = Math.Max(18, height * visibleRows / entryCount);
        int thumbY = ComputeMenuScrollbarThumbY(
            y,
            height,
            thumbHeight,
            entryCount,
            visibleRows,
            scrollOffset);
        MenuRect(x, thumbY, 3, thumbHeight, accent);
    }

    private static int ComputeMenuScrollbarThumbY(
        int y,
        int height,
        int thumbHeight,
        int entryCount,
        int visibleRows,
        int scrollOffset)
    {
        int maxScroll = Math.Max(1, entryCount - visibleRows);
        int boundedOffset = Clamp(scrollOffset, 0, maxScroll);
        return y + Math.Max(0, height - thumbHeight) * boundedOffset / maxScroll;
    }

    private void DrawMenuFrame(Rectangle bounds, Color color)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        MenuRect(bounds.X, bounds.Y, bounds.Width, 1, color);
        MenuRect(bounds.X, bounds.Bottom - 1, bounds.Width, 1, color);
        MenuRect(bounds.X, bounds.Y, 1, bounds.Height, color);
        MenuRect(bounds.Right - 1, bounds.Y, 1, bounds.Height, color);
    }

    private void MenuRect(int x, int y, int width, int height, Color color)
    {
        if (width <= 0 || height <= 0 || _menuFrameAlpha <= 0.001f)
        {
            return;
        }

        ExecuteMenuRenderCommand(new MenuRenderCommand
        {
            Kind = MenuRenderCommandKind.Rectangle,
            Bounds = new Rectangle(x, y, width, height),
            Color = color
        });
    }

    private void MenuText(string text, int x, int y, float scale, Color color, bool centered, bool outline)
    {
        if (_menuFrameAlpha <= 0.001f)
        {
            return;
        }

        ExecuteMenuRenderCommand(new MenuRenderCommand
        {
            Kind = MenuRenderCommandKind.Text,
            Bounds = new Rectangle(x, y, 0, 0),
            Caption = text ?? string.Empty,
            Scale = scale,
            Color = color,
            Centered = centered,
            Outline = outline
        });
    }

    private void ExecuteMenuRenderCommand(MenuRenderCommand command)
    {
        if (command.Kind == MenuRenderCommandKind.Rectangle)
        {
            ExecuteMenuRectangleCommand(command);
            return;
        }

        ExecuteMenuTextCommand(command);
    }

    private void ExecuteMenuRectangleCommand(MenuRenderCommand command)
    {
        UIRectangle rectangle;

        if (_menuRectangleCursor >= _menuRectanglePool.Count)
        {
            rectangle = new UIRectangle(Point.Empty, Size.Empty, Color.Transparent);
            _menuRectanglePool.Add(rectangle);
        }
        else
        {
            rectangle = _menuRectanglePool[_menuRectangleCursor];
        }

        _menuRectangleCursor++;
        rectangle.Enabled = true;
        rectangle.Position = command.Bounds.Location;
        rectangle.Size = command.Bounds.Size;
        rectangle.Color = ApplyMenuFrameAlpha(command.Color);
        rectangle.Draw();
    }

    private void ExecuteMenuTextCommand(MenuRenderCommand command)
    {
        UIText uiText;

        if (_menuTextCursor >= _menuTextPool.Count)
        {
            uiText = new UIText(string.Empty, Point.Empty, command.Scale, Color.Transparent, GtaFont.ChaletLondon, command.Centered, false, command.Outline);
            _menuTextPool.Add(uiText);
        }
        else
        {
            uiText = _menuTextPool[_menuTextCursor];
        }

        _menuTextCursor++;
        uiText.Enabled = true;
        uiText.Caption = command.Caption ?? string.Empty;
        uiText.Position = command.Bounds.Location;
        uiText.Scale = command.Scale;
        uiText.Color = ApplyMenuFrameAlpha(command.Color);
        uiText.Font = GtaFont.ChaletLondon;
        uiText.Centered = command.Centered;
        uiText.Shadow = false;
        uiText.Outline = command.Outline;
        uiText.Draw();
    }

    private Color ApplyMenuFrameAlpha(Color color)
    {
        int alpha = (int)Math.Round(color.A * _menuFrameAlpha);
        return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
    }

    private static Color WithMaximumAlpha(Color color, int maximumAlpha)
    {
        return Color.FromArgb(Math.Min(color.A, Math.Max(0, maximumAlpha)), color.R, color.G, color.B);
    }

    private static Rectangle Offset(Rectangle rectangle, int x, int y)
    {
        return new Rectangle(rectangle.X + x, rectangle.Y + y, rectangle.Width, rectangle.Height);
    }
}
