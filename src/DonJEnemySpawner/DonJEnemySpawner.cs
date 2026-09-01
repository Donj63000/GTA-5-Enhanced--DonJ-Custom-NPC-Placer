using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;
using GtaControl = GTA.Control;
using GtaFont = GTA.Font;
using KeyEventArgs = System.Windows.Forms.KeyEventArgs;
using Keys = System.Windows.Forms.Keys;

public sealed partial class DonJEnemySpawner : Script
{
    /*
     * DonJ Custom NPC Placer
     * Cible : NIBScriptHookVDotNet / ScriptHookVDotNet API v2.x
     *
     * Remplacement complet du fichier DonJEnemySpawner.cs.
     *
     * Ajouts principaux :
     * - Type de placement : NPC / Vehicule / Objet.
     * - Selection NPC par categorie.
     * - Selection arme par categorie + modification arme.
     * - Selection vehicule par categorie.
     * - Selection objet par categorie.
     * - Placement camera persistant avec apercu pour NPC / vehicule / objet.
     * - Comportements :
     *   Statique, Attaquer, Neutre, Allie,
     *   Garde du corps,
     *   Neutre patrouille, Hostile patrouille, Allie patrouille.
     * - Rayon de patrouille configurable.
     * - Gardes neutres facon agents de securite.
     * - Gardes allies et gardes du corps defensifs.
     * - Gardes du corps qui suivent le joueur a pied ou en vehicule.
     * - Sauvegarde / chargement XML des NPC, vehicules et objets.
     * - Nettoyage separe NPC / vehicules / objets.
     * - Blips minimap colores.
     */

    private const string TrainerTitle = "DonJ Custom NPC Placer";
    private const string TrainerSubtitle = "Placement propre pour NPC, vehicules et objets";
    private const Keys MenuToggleKey = Keys.F10;
    private const string MenuToggleKeyLabel = "F10";
    private const string SaveFolderName = "DonJEnemySpawnerSaves";
    private const string LastSaveFileMarkerName = "_last_save.txt";
    private const string SaveDirectoryEnvironmentVariable = "DONJ_ENEMY_SPAWNER_SAVE_DIR";
    private const string DefaultEnhancedGtaRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced";
    private const int MaxSaveFileNameLength = 96;

    private const int MinHealth = 1;
    private const int MaxHealth = 5000;

    private const int MinArmor = 0;
    private const int MaxArmor = 200;

    private const int MinDistance = 25;
    private const int MaxDistance = 2500;
    private const int DistanceStep = 25;

    private const int MinPatrolRadius = 5;
    private const int MaxPatrolRadius = 500;
    private const int PatrolRadiusStep = 5;

// Je limite la page la plus dense (NPC) a treize lignes visibles.
// Les autres pages tiennent sans scroll avec les actions actuelles.
private const int MainMenuCompactVisibleRowLimit = 13;
private const int StatusTextMaxLength = 112;
private const int WeaponEditorItemCount = 12;

    private const int RelationshipCompanion = 0;
    private const int RelationshipNeutral = 3;
    private const int RelationshipDislike = 4;
    private const int RelationshipHate = 5;

    private const int ThinkIntervalMs = 700;

    // Anti micro-freeze : les cerveaux IA ne doivent jamais tous se reveiller
    // sur la meme frame. Avec 25/30 PNJ, une salve synchronisee de scans
    // GetNearbyPeds + natives TASK_* provoque un petit pic CPU visible.
    private const int NpcThinkJitterMs = 750;
    private const int MaxNpcBrainsPerTick = 6;
    private const int PassiveHoldRefreshMs = 2400;
    private const int PassiveHoldJitterMs = 900;
    private const int NpcBlipRefreshIntervalMs = 1200;
    private const int NpcBlipRefreshJitterMs = 700;
    private const int MaxNpcBlipRefreshPerTick = 4;

    // Cache global des menaces pour les allies : au lieu de scanner le monde
    // une fois par allie, on scanne par petites tranches puis on partage le
    // resultat avec tous les allies proches.
    private const int AllyThreatScanIntervalMs = 260;
    private const int AllyThreatCacheLifetimeMs = 950;
    private const int AllyThreatGuardScansPerPass = 4;

    private const int AutoRespawnCheckIntervalMs = 1000;
    private const int AutoRespawnMinDelayMs = 6000;
    private const int AutoRespawnRetryDelayMs = 15000;
    private const int AutoRespawnMaxPerTick = 3;
    private const float AutoRespawnLeaveDistance = 220.0f;
    private const float AutoRespawnNearSafetyDistance = 70.0f;

    private const float StaticSightDistance = 2500.0f;
    private const float AttackRefreshDistance = 3000.0f;
    private const float CombatRefreshDistance = 3000.0f;

    private const float NeutralAssistRadius = 95.0f;
    private const float NeutralWitnessSightDistance = 140.0f;
    private const float NeutralShootingReactionDistance = 55.0f;

    private const float RuntimeNeutralAssistRadius = 105.0f;
    private const float NeutralWitnessDistance = 155.0f;
    private const float NeutralShotReactionDistance = 95.0f;
    private const float NeutralNearbyAttackReactionDistance = 55.0f;

    // Je garde en memoire courte les actes hostiles du joueur afin que les gardes
    // neutres ne ratent pas un coup de feu bref entre deux ticks de reflexion.
    private const int PlayerHostilityMemoryMs = 2200;
    private const int PlayerHostilityScanIntervalMs = 90;
    private const float NeutralBulletWhizReactionDistance = 9.0f;
    private const float NeutralBulletImpactReactionDistance = 14.0f;
    private const float NeutralMeleeReactionDistance = 42.0f;
    private const float NeutralNearbyVehicleAttackReactionDistance = 70.0f;
    private const float PlayerVehicleHostilityMinSpeed = 7.5f;

    private const float AllyDefenseRadius = 150.0f;
    private const float AllySightDistance = 180.0f;
    private const float AllyShootingThreatDistance = 45.0f;

    private const float RuntimeAllyDefenseRadius = 165.0f;
    private const float RuntimeAllySightDistance = 190.0f;
    private const float AllyShotThreatDistance = 50.0f;

    private const int GuardReturnDelayMs = 30000;
    private const int GuardReturnRetaskMs = 3500;
    private const float GuardReturnArriveDistance = 1.35f;
    private const float GuardWalkSpeed = 1.05f;

    private const int PatrolRetaskMs = 6000;
    private const float PatrolArriveDistance = 1.75f;
    private const float PatrolWalkSpeed = 1.0f;

    private const int BodyguardRetaskMs = 1200;
    private const float BodyguardFootFollowDistance = 3.5f;
    private const float BodyguardVehicleSearchRadius = 90.0f;

    // Je garde un style de conduite propre avec evitement pour l'escorte.
    private const int ProfessionalDrivingStyle = 786603;

    private const int PlacementPreviewAlpha = 170;
    private const int PlacementSpawnCooldownMs = 350;
    private const int PreviewRetryIntervalMs = 650;

    private const ulong PlaceEntityOnGroundProperlyNative = 0x58A850EAEE20FAA3UL;
    // Interactions objets poses : argent, soins, armure, munitions.
    private const float ObjectInteractionDistance = 2.05f;
    private const float ObjectInteractionMarkerHeight = 0.85f;

    // Natives utilisees par hash direct pour rester compatible NIB/SHVDN v2.
    private const ulong NativeStatGetInt = 0x767FBC2AC802EF3DUL;
    private const ulong NativeStatSetInt = 0xB3271D7AB655B441UL;
    private const ulong NativeGetSelectedPedWeapon = 0x0A6DB4965674D243UL;
    private const ulong NativeAddAmmoToPed = 0x78F0424C34306220UL;

    private readonly Random _random = new Random();

    private readonly List<ModelOption> _allModelOptions;
    private readonly List<ModelCategory> _modelCategories;

    private readonly List<WeaponOption> _allWeaponOptions;
    private readonly List<WeaponCategory> _weaponCategories;

    private readonly List<VehicleOption> _allVehicleOptions;
    private readonly List<VehicleCategory> _vehicleCategories;

    private readonly List<ObjectOption> _allObjectOptions;
    private readonly List<ObjectCategory> _objectCategories;

    // Je garde aussi ces champs pour rester compatible avec les tests historiques existants.
    private List<ModelOption> _modelOptions;
    private List<WeaponOption> _weaponOptions;
    private int _selectedModelIndex;
    private int _selectedWeaponIndex;

    private readonly List<SpawnedNpc> _spawnedNpcs = new List<SpawnedNpc>();
    private readonly List<PlacedVehicle> _placedVehicles = new List<PlacedVehicle>();
    private readonly List<PlacedObject> _placedObjects = new List<PlacedObject>();

    private int _selectedModelCategoryIndex;
    private int _selectedModelIndexInCategory;

    private int _selectedWeaponCategoryIndex;
    private int _selectedWeaponIndexInCategory;

    private int _selectedVehicleCategoryIndex;
    private int _selectedVehicleIndexInCategory;

    private int _selectedObjectCategoryIndex;
    private int _selectedObjectIndexInCategory;

    private PlacementEntityType _selectedPlacementType = PlacementEntityType.Npc;

    private int _selectedHealth = 300;
    private int _selectedArmor = 100;
    private int _selectedDistance = 200;
    private int _selectedPatrolRadius = 35;

    private NpcBehavior _selectedBehavior = NpcBehavior.Attacker;
    private bool _selectedAutoRespawn;
    private WeaponLoadout _selectedWeaponLoadout;

private int _mainMenuIndex;
private int _mainMenuScrollOffset;
private int _weaponEditorIndex;
private MenuPage _menuPage = MenuPage.Main;

private bool _menuVisible;

    private string _customModelName = "s_m_y_swat_01";
    private bool _customModelInputRequested;

    private string _lastSaveFileName = "maison.xml";
    private bool _saveRequested;
    private bool _loadRequested;

    private int _hostileGroupHash;
    private int _neutralGroupHash;
    private int _allyGroupHash;
    private int _lastKnownPlayerGroupHash;
    private int _nextRelationshipRefreshAt;
    private int _nextPlayerHostilityScanAt;
    private int _lastPlayerGunfireAt = -1000000;
    private Vector3 _lastPlayerGunfirePosition = Vector3.Zero;
    private int _lastPlayerGunfireImpactAt = -1000000;
    private Vector3 _lastPlayerGunfireImpactPosition = Vector3.Zero;
    private int _lastPlayerMeleeHostilityAt = -1000000;
    private Vector3 _lastPlayerMeleeHostilityPosition = Vector3.Zero;
    private int _autoRespawnsThisTick;

    private Ped _allyCachedThreatPed;
    private int _allyCachedThreatUntil;
    private int _nextAllyThreatScanAt;
    private int _allyThreatScanCursor;

    private string _statusText = string.Empty;
    private int _statusUntil;
    private bool _statusOwnedByJusticeProfile;
    private bool _objectInteractionKeyLatch;

    private bool _spawnRequested;
    private PlacementEntityType _requestedPlacementType;
    private bool _spawnRequestedPrecise;
    private bool _requestedHasHeadingOverride;
    private float _requestedSpawnHeading;
    private Vector3 _requestedSpawnPosition;
    private Vector3 _requestedSpawnSurfaceNormal;

    private Camera _placementCamera;
    private bool _placementMode;
    private Vector3 _placementCameraRotation;
    private Vector3 _placementHitPoint;
    private Vector3 _placementSpawnPoint;
    private Vector3 _placementSurfaceNormal;
    private bool _placementHasHit;
    private bool _placementCancelRequested;
    private bool _placementConfirmRequested;
    private float _placementHeading;
    private int _nextPlacementSpawnAllowedAt;

    private Ped _placementPreviewPed;
    private Vehicle _placementPreviewVehicle;
    private Prop _placementPreviewProp;
    private PlacementEntityType _placementPreviewType;
    private string _placementPreviewKey = string.Empty;
    private int _nextPreviewRetryAt;

    private bool _storedPlayerInvincible;
    private bool _storedPlayerFrozen;
    private Ped _placementProtectedPlayer;
    private int _placementProtectedPlayerHandle;
    private bool _placementPlayerStateStored;

    private static Type _weaponComponentHashType;

    /*
     * Je protege ces groupes ambiants pour ne jamais les transformer en ennemis globaux.
     * Les gardes peuvent toujours combattre un individu precis si cet individu
     * attaque reellement le joueur ou un garde, mais j'evite une guerre de groupe.
     */
    private static readonly HashSet<int> ProtectedAmbientRelationshipGroups = BuildProtectedAmbientRelationshipGroups();

    public DonJEnemySpawner()
    {
        Interval = 0;

        _allModelOptions = BuildAllModelOptions();
        _modelCategories = BuildModelCategories(_allModelOptions);
        _modelOptions = BuildModelOptions();

        _allWeaponOptions = BuildAllWeaponOptions();
        _weaponCategories = BuildWeaponCategories(_allWeaponOptions);
        _weaponOptions = BuildWeaponOptions();

        _allVehicleOptions = BuildAllVehicleOptions();
        _vehicleCategories = BuildVehicleCategories(_allVehicleOptions);

        _allObjectOptions = BuildAllObjectOptions();
        _objectCategories = BuildObjectCategories(_allObjectOptions);

        SelectDefaultModel();
        SelectDefaultWeapon();
        SelectDefaultVehicle();
        SelectDefaultObject();

        _selectedModelIndex = FindDefaultModelIndex();
        _selectedWeaponIndex = FindDefaultWeaponIndex();

        _selectedWeaponLoadout = new WeaponLoadout
        {
            Weapon = CurrentWeaponOption().Hash,
            Ammo = 9999,
            Tint = 0,
            Preset = WeaponUpgradePreset.Standard
        };

        // Je rends d'abord le coeur du script joignable par NIB : une native
        // ou une UI secondaire défaillante ne doit plus empêcher F10 ni l'arrêt propre.
        Tick += OnTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Aborted += OnAborted;

        RunRuntimeStartupStage(RuntimeStartupStage.MenuWarmup);
        RunRuntimeStartupStage(RuntimeStartupStage.PersistentSaveState);
        RunRuntimeStartupStage(RuntimeStartupStage.Justice);
        RunRuntimeStartupStage(RuntimeStartupStage.Relationships);
        RunRuntimeStartupStage(RuntimeStartupStage.Status);
    }

private enum MenuPage
{
    Main,
    WeaponEditor,
    JusticeCharges,
    JusticeRecord
}

private enum MenuCategory
{
    Npc,
    Vehicle,
    Object,
    Interior,
    Scene,
    Justice,
    Tools
}

private enum MainMenuAction
{
    PlacementType,
    PrecisePlacement,
    DistancePlacement,
    PlacementDistance,

    NpcCategory,
    NpcModel,
    NpcWeaponCategory,
    NpcWeapon,
    NpcWeaponEditor,
    NpcHealth,
    NpcArmor,
    NpcBehavior,
    NpcPatrolRadius,
    NpcAutoRespawn,

    VehicleCategory,
    VehicleModel,
    VehicleAutoRespawn,

    ObjectCategory,
    ObjectModel,
    ObjectAutoRespawn,

    InteriorCategory,
    InteriorModel,
    ExitActiveInfo,
    ExitDestinationInfo,

    Save,
    Load,

    JusticeEnabled,
    JusticeProfile,
    JusticeStatus,
    JusticeLastCrime,
    JusticeSeverity,
    JusticeWarrant,
    JusticeRecognitionStatus,
    JusticeRecognitionPlate,
    JusticeRecognitionOutfit,
    JusticeRecognitionWarrant,
    JusticeRecognitionDistance,
    JusticeCharges,
    JusticeRecord,
    JusticeFine,
    JusticeFineDispute,
    JusticePayFine,
    JusticeResolveFineDispute,
    JusticeSentence,
    JusticeRecidivism,
    JusticePoliceMode,
    JusticeRecovery,
    JusticeDiagnostic,
    JusticeResetProfile,

    CleanNpcs,
    CleanVehicles,
    CleanObjects,
    CleanInteriorPortals,

    TerminatorMode
}

private enum MainMenuRowKind
{
    Normal,
    Primary,
    PrimaryAction,
    Action,
    Info,
    Danger
}

private sealed class MainMenuEntry
{
    public MainMenuAction Action;
    public string Label;
    public string Value;
    public MainMenuRowKind Kind;
    public bool Enabled;

    public MainMenuEntry(MainMenuAction action, string label, string value, MainMenuRowKind kind, bool enabled)
    {
        Action = action;
        Label = label ?? string.Empty;
        Value = value ?? string.Empty;
        Kind = kind;
        Enabled = enabled;
    }
}

// Je garde cet enum historique pour les tests et les contrats deja poses.
private enum EnemyBehavior
    {
        Static,
        Attacker,
        Neutral,
        Ally
    }

    private enum PlacementEntityType
    {
        Npc,
        Vehicle,
        Object,
        Entrance,
        Exit
    }

    private enum NpcBehavior
    {
        Static,
        Attacker,
        Neutral,
        Ally,
        Bodyguard,
        NeutralPatrol,
        HostilePatrol,
        AllyPatrol
    }

    private enum WeaponUpgradePreset
    {
        Standard,
        ChargeurEtendu,
        Silencieux,
        Tactique,
        Full
    }

    private enum WeaponScopeMode
    {
        None,
        Small,
        Medium,
        Large
    }

    private enum WeaponMk2AmmoMode
    {
        Standard,
        Tracer,
        Incendiary,
        ArmorPiercing,
        FMJ,
        Explosive
    }

    private enum ObjectPlacementCategory
    {
        Securite,
        Couverture,
        ArgentButin,
        MaterielTactique,
        SoinSurvie,
        BureauInformatique,
        AtelierOutils,
        Mobilier,
        CaisseStockage,
        Decoration,
        Lumiere,
        Exterieur,
        Divers
    }

    private enum ObjectInteractionKind
    {
        None,
        Cash,
        Health,
        Armor,
        Ammo
    }

    private sealed class ModelOption
    {
        public string DisplayName;
        public bool IsCustom;
        public int Hash;

        public Model ToModel(string customModelName)
        {
            return IsCustom ? new Model(customModelName) : new Model(Hash);
        }

        public string GetDisplayName(string customModelName)
        {
            return IsCustom ? "Custom: " + customModelName : DisplayName;
        }
    }

    private sealed class ModelCategory
    {
        public string Name;
        public List<ModelOption> Options;
    }

    private sealed class WeaponOption
    {
        public string DisplayName;
        public WeaponHash Hash;
    }

    private sealed class WeaponCategory
    {
        public string Name;
        public List<WeaponOption> Options;
    }

    private sealed class VehicleOption
    {
        public string DisplayName;
        public VehicleHash Hash;

        public Model ToModel()
        {
            return new Model((int)Hash);
        }
    }

    private sealed class VehicleCategory
    {
        public string Name;
        public List<VehicleOption> Options;
    }

    private sealed class ObjectOption
    {
        public string DisplayName;
        public string ModelName;
        public ObjectPlacementCategory Category;

        // Interaction optionnelle.
        // Si ces valeurs restent a zero, le script essaie quand meme de deviner
        // automatiquement selon le nom/modele : cash, health, ammo, armor.
        public ObjectInteractionKind InteractionKind;
        public int CashValue;
        public int HealAmount;
        public int ArmorAmount;
        public int AmmoAmount;

        public Model ToModel()
        {
            return new Model(ModelName);
        }
    }

    private sealed class ObjectCategory
    {
        public string Name;
        public List<ObjectOption> Options;
    }

    private sealed class WeaponLoadout
    {
        public WeaponHash Weapon;
        public int Ammo;
        public int Tint;
        public WeaponUpgradePreset Preset;

        public bool ExtendedClip;
        public bool Suppressor;
        public bool Flashlight;
        public bool Grip;
        public WeaponScopeMode Scope;
        public bool Muzzle;
        public bool ImprovedBarrel;
        public WeaponMk2AmmoMode Mk2Ammo;

        private int _cachedSummaryFingerprint = int.MinValue;
        private string _cachedSummary = "Standard";

        public WeaponLoadout Clone()
        {
            return new WeaponLoadout
            {
                Weapon = Weapon,
                Ammo = Ammo,
                Tint = Tint,
                Preset = Preset,
                ExtendedClip = ExtendedClip,
                Suppressor = Suppressor,
                Flashlight = Flashlight,
                Grip = Grip,
                Scope = Scope,
                Muzzle = Muzzle,
                ImprovedBarrel = ImprovedBarrel,
                Mk2Ammo = Mk2Ammo
            };
        }

        public string Summary()
        {
            int fingerprint = CalculateSummaryFingerprint();

            if (fingerprint == _cachedSummaryFingerprint)
            {
                return _cachedSummary;
            }

            string summary = string.Empty;
            if (ExtendedClip) AppendSummaryPart(ref summary, "chargeur+");
            if (Suppressor) AppendSummaryPart(ref summary, "silencieux");
            if (Flashlight) AppendSummaryPart(ref summary, "lampe");
            if (Grip) AppendSummaryPart(ref summary, "poignee");
            if (Scope != WeaponScopeMode.None) AppendSummaryPart(ref summary, "lunette " + Scope.ToString().ToLowerInvariant());
            if (Muzzle) AppendSummaryPart(ref summary, "comp.");
            if (ImprovedBarrel) AppendSummaryPart(ref summary, "canon+");
            if (Mk2Ammo != WeaponMk2AmmoMode.Standard) AppendSummaryPart(ref summary, Mk2Ammo.ToString());
            if (Tint > 0) AppendSummaryPart(ref summary, "teinte " + Tint.ToString(CultureInfo.InvariantCulture));

            _cachedSummaryFingerprint = fingerprint;
            _cachedSummary = summary.Length == 0 ? "Standard" : summary;
            return _cachedSummary;
        }

        private int CalculateSummaryFingerprint()
        {
            int fingerprint = Tint & 0x3F;
            fingerprint |= ((int)Scope & 0x03) << 6;
            fingerprint |= ((int)Mk2Ammo & 0x07) << 8;
            if (ExtendedClip) fingerprint |= 1 << 11;
            if (Suppressor) fingerprint |= 1 << 12;
            if (Flashlight) fingerprint |= 1 << 13;
            if (Grip) fingerprint |= 1 << 14;
            if (Muzzle) fingerprint |= 1 << 15;
            if (ImprovedBarrel) fingerprint |= 1 << 16;
            return fingerprint;
        }

        private static void AppendSummaryPart(ref string summary, string part)
        {
            summary = summary.Length == 0 ? part : summary + ", " + part;
        }
    }

    private sealed class ModelIdentity
    {
        public bool IsCustom;
        public string Name;
        public int Hash;
        public string DisplayName;

        public Model ToModel()
        {
            return IsCustom ? new Model(Name) : new Model(Hash);
        }
    }

    private sealed class VehicleIdentity
    {
        public string Name;
        public int Hash;
        public string DisplayName;

        public Model ToModel()
        {
            return new Model(Hash);
        }
    }

    private sealed class ObjectIdentity
    {
        public string ModelName;
        public string DisplayName;

        public ObjectInteractionKind InteractionKind;
        public int CashValue;
        public int HealAmount;
        public int ArmorAmount;
        public int AmmoAmount;

        public Model ToModel()
        {
            return new Model(ModelName);
        }
    }

    private sealed class SpawnedNpc
    {
        public Ped Ped;
        public Blip Blip;

        public NpcBehavior Behavior;
        public NpcBehavior BaseBehavior;

        public bool Activated;
        public bool DeathAlerted;

        public int NextThinkAt;
        public int NextPassiveTaskAt;
        public int NextBlipRefreshAt;

        public Vector3 HomePosition;
        public float HomeHeading;
        public int LastCombatActivityAt;
        public bool IsReturningHome;
        public int NextReturnTaskAt;

        public Vector3 PatrolCenter;
        public float PatrolRadius;
        public Vector3 PatrolTarget;
        public int NextPatrolTaskAt;

        public int NextBodyguardTaskAt;
        public int BodyguardAssignedVehicleHandle;
        public int BodyguardAssignedSeat = 999;
        public bool BodyguardIsDriver;

        public ModelIdentity ModelIdentity;
        public WeaponLoadout Loadout;

        public int SavedMaxHealth;
        public int SavedArmor;

        public bool AutoRespawn;
        public bool RespawnPending;
        public int RespawnEligibleAt;
        public int NextRespawnCheckAt;

    }

    private sealed class PlacedVehicle
    {
        public Vehicle Vehicle;
        public Blip Blip;
        public VehicleIdentity Identity;
        public Vector3 Position;
        public float Heading;
        public Vector3 RespawnPosition;
        public float RespawnHeading;
        public bool AutoRespawn;
        public bool RespawnPending;
        public int RespawnEligibleAt;
        public int NextRespawnCheckAt;

        /*
         * true par défaut : les véhicules placés manuellement restent sauvegardables.
         * false : véhicule runtime temporaire, utilisé par un service/événement,
         * jamais écrit dans le XML même s'il est vivant au moment de la sauvegarde.
         */
        public bool PersistentInSave = true;
    }

    private sealed class PlacedObject
    {
        public Prop Prop;
        public ObjectIdentity Identity;
        public Vector3 Position;
        public float Heading;
        public Vector3 RespawnPosition;
        public float RespawnHeading;
        public bool AutoRespawn;
        public bool RespawnPending;
        public int RespawnEligibleAt;
        public int NextRespawnCheckAt;
    }

    private void OnTick(object sender, EventArgs e)
    {
        // Je route chaque domaine par un enum et un switch : aucun delegate n'est créé à chaque frame.
        bool RunTickStage(RuntimeTickStage stage)
        {
            try
            {
                switch (stage)
                {
                    case RuntimeTickStage.Relationships:
                        RefreshPlayerRelationshipIfNeeded();
                        break;

                    case RuntimeTickStage.JusticeEarly:
                        UpdateJusticeEarly();
                        break;

                    case RuntimeTickStage.CartelEarly:
                        UpdateCartelContactAndConvoy();
                        break;

                    case RuntimeTickStage.Terminator:
                        UpdateTerminatorMode();
                        break;

                    case RuntimeTickStage.PlayerProtection:
                        MaintainPlayerInvincibilityProtection();
                        MaintainPlacementPlayerStateRecovery();
                        break;

                    case RuntimeTickStage.CustomModelRequest:
                        if (_customModelInputRequested)
                        {
                            _customModelInputRequested = false;
                            EditCustomModelName();
                        }
                        break;

                    case RuntimeTickStage.SaveRequest:
                        if (_saveRequested)
                        {
                            _saveRequested = false;
                            SaveCurrentSetupWithPrompt();
                        }
                        break;

                    case RuntimeTickStage.LoadRequest:
                        if (_loadRequested)
                        {
                            _loadRequested = false;
                            LoadSetupWithPrompt();
                        }
                        break;

                    case RuntimeTickStage.Placement:
                        if (_placementMode)
                        {
                            UpdatePlacementMode();
                        }
                        break;

                    case RuntimeTickStage.MenuAnimation:
                        UpdateMenuAnimation();
                        break;

                    case RuntimeTickStage.Menu:
                        if (ShouldRenderMenu)
                        {
                            if (_menuVisible)
                            {
                                DisableMenuGameplayControls();
                            }

                            DrawMenu();
                        }
                        break;

                    case RuntimeTickStage.PendingSpawn:
                        ProcessPendingSpawn();
                        break;

                    case RuntimeTickStage.PlayerHostility:
                        UpdatePlayerHostilityMemory(Game.Player.Character);
                        break;

                    case RuntimeTickStage.JusticeLate:
                        UpdateJusticeSystem();
                        break;

                    case RuntimeTickStage.JusticeRecovery:
                        UpdateJusticeFailSafeMaintenance();
                        break;

                    case RuntimeTickStage.Npcs:
                        UpdateNpcs();
                        break;

                    case RuntimeTickStage.CartelLate:
                        UpdateCartelConvoyLate();
                        break;

                    case RuntimeTickStage.Vehicles:
                        UpdatePlacedVehicles();
                        break;

                    case RuntimeTickStage.Objects:
                        UpdatePlacedObjects();
                        break;

                    case RuntimeTickStage.ObjectInteractions:
                        UpdatePlacedObjectInteractions();
                        break;

                    case RuntimeTickStage.Portals:
                        UpdateInteriorPortals();
                        break;

                    case RuntimeTickStage.Status:
                        DrawStatus();
                        break;

                    case RuntimeTickStage.JusticeDamageFlush:
                        FlushJusticeConsumedDamageFronts();
                        break;

                    default:
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeTickStageFailure(stage, ex);
                return false;
            }
        }

        try
        {
            RunTickStage(RuntimeTickStage.Relationships);
            bool justiceEarlySucceeded = RunTickStage(RuntimeTickStage.JusticeEarly);
            RunTickStage(RuntimeTickStage.CartelEarly);
            RunTickStage(RuntimeTickStage.Terminator);
            RunTickStage(RuntimeTickStage.PlayerProtection);
            _autoRespawnsThisTick = 0;

            RunTickStage(RuntimeTickStage.CustomModelRequest);
            RunTickStage(RuntimeTickStage.SaveRequest);
            RunTickStage(RuntimeTickStage.LoadRequest);
            RunTickStage(RuntimeTickStage.Placement);
            RunTickStage(RuntimeTickStage.MenuAnimation);

            // Je masque seulement le HUD Terminator pendant la transition du menu
            // afin que les deux interfaces ne se superposent jamais.
            try
            {
                if (!ShouldRenderMenu)
                {
                    DrawTerminatorModeHud();
                }
            }
            catch (Exception ex)
            {
                ReportRuntimeTickStageFailure(RuntimeTickStage.Hud, ex);
            }

            RunTickStage(RuntimeTickStage.Menu);
            RunTickStage(RuntimeTickStage.PendingSpawn);
            RunTickStage(RuntimeTickStage.PlayerHostility);

            bool justiceLateSucceeded = false;
            if (justiceEarlySucceeded)
            {
                justiceLateSucceeded =
                    RunTickStage(RuntimeTickStage.JusticeLate);
            }
            else
            {
                // Je n'avance jamais le dossier sur un état Early incomplet;
                // je ne conserve ici que les reprises de sécurité Justice.
                RunTickStage(RuntimeTickStage.JusticeRecovery);
            }

            if (justiceEarlySucceeded && justiceLateSucceeded &&
                !ShouldRenderMenu)
            {
                try
                {
                    // Je dessine le temps seulement après un cycle Justice
                    // complet. Une exception Late ne peut ainsi laisser une
                    // ligne figée issue d'un état qui n'avance plus.
                    DrawJusticeCustodyStatusLine();
                }
                catch (Exception ex)
                {
                    ReportRuntimeTickStageFailure(RuntimeTickStage.Hud, ex);
                }
            }

            RunTickStage(RuntimeTickStage.Npcs);
            RunTickStage(RuntimeTickStage.CartelLate);
            RunTickStage(RuntimeTickStage.Vehicles);
            RunTickStage(RuntimeTickStage.Objects);
            RunTickStage(RuntimeTickStage.ObjectInteractions);
            RunTickStage(RuntimeTickStage.Portals);
            RunTickStage(RuntimeTickStage.Status);
        }
        finally
        {
            // Je consomme les fronts de dégâts après toutes les IA du tick afin
            // qu'un ancien flag GTA ne puisse jamais recréer un crime plus tard.
            RunTickStage(RuntimeTickStage.JusticeDamageFlush);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.KeyCode == MenuToggleKey)
            {
                if (_placementMode)
                {
                    StopPlacementMode(true);
                }
                else
                {
                    SetMenuVisible(!_menuVisible);
                }

                e.Handled = true;
                return;
            }

            if (_placementMode)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
                {
                    _placementCancelRequested = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.NumPad5)
                {
                    _placementConfirmRequested = true;
                    e.Handled = true;
                    return;
                }

                return;
            }

            if (TryHandleTerminatorVisionKey(e.KeyCode))
            {
                e.Handled = true;
                return;
            }

            if (!_menuVisible)
            {
                return;
            }

            if (_menuPage == MenuPage.WeaponEditor)
            {
                HandleWeaponEditorKey(e);
            }
            else if (_menuPage == MenuPage.JusticeCharges ||
                     _menuPage == MenuPage.JusticeRecord)
            {
                HandleJusticeLedgerKey(e);
            }
            else
            {
                HandleMainMenuKey(e);
            }
        }
        catch (Exception ex)
        {
            LogException("OnKeyDown", ex);
            ShowStatus("Erreur touche " + TrainerTitle + ": " + ex.Message, 7000);
        }
    }

    private void OnAborted(object sender, EventArgs e)
    {
        LogInfo("Arret", TrainerTitle + " arrete.");

        // Je restaure d'abord les effets gameplay critiques, puis j'isole chaque nettoyage.
        void RunShutdownStep(RuntimeShutdownStage stage)
        {
            try
            {
                switch (stage)
                {
                    case RuntimeShutdownStage.Justice:
                        ShutdownJusticeSystem();
                        break;

                    case RuntimeShutdownStage.Terminator:
                        DisableTerminatorMode(false);
                        break;

                    case RuntimeShutdownStage.Placement:
                        StopPlacementMode(false);
                        break;

                    case RuntimeShutdownStage.PlayerProtection:
                        ShutdownPlayerInvincibilityProtection();
                        break;

                    case RuntimeShutdownStage.Menu:
                        ReleaseMenuUi();
                        break;

                    case RuntimeShutdownStage.DangerAction:
                        CancelPendingDangerAction();
                        break;

                    case RuntimeShutdownStage.HighSecurityEscort:
                        ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
                        break;

                    case RuntimeShutdownStage.NpcBlips:
                        RemoveAllNpcBlipsForShutdown();
                        break;

                    case RuntimeShutdownStage.VehicleBlips:
                        RemoveAllVehicleBlipsForShutdown();
                        break;

                    case RuntimeShutdownStage.Relationships:
                        RemoveRuntimeRelationshipGroupsForShutdown();
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportRuntimeShutdownFailure(stage, ex);
            }
        }

        RunShutdownStep(RuntimeShutdownStage.Justice);
        RunShutdownStep(RuntimeShutdownStage.Terminator);
        RunShutdownStep(RuntimeShutdownStage.Placement);
        RunShutdownStep(RuntimeShutdownStage.PlayerProtection);
        RunShutdownStep(RuntimeShutdownStage.Menu);
        RunShutdownStep(RuntimeShutdownStage.DangerAction);
        RunShutdownStep(RuntimeShutdownStage.HighSecurityEscort);
        RunShutdownStep(RuntimeShutdownStage.NpcBlips);
        RunShutdownStep(RuntimeShutdownStage.VehicleBlips);
        RunShutdownStep(RuntimeShutdownStage.Relationships);
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (_pendingDangerAction.HasValue &&
            (e.KeyCode == Keys.Enter || e.KeyCode == Keys.NumPad5))
        {
            _dangerConfirmationRequiresEnterRelease = false;
        }
    }

private void HandleMainMenuKey(KeyEventArgs e)
{
    if (HandlePendingDangerKey(e))
    {
        return;
    }

    List<MainMenuEntry> entries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(entries);

    if (entries.Count == 0)
    {
        return;
    }

    int pageSize = GetMainMenuCompactVisibleRowCount(entries.Count);

    switch (e.KeyCode)
    {
        case Keys.Up:
        case Keys.NumPad8:
            _mainMenuIndex = Wrap(_mainMenuIndex - 1, entries.Count);
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.Down:
        case Keys.NumPad2:
            _mainMenuIndex = Wrap(_mainMenuIndex + 1, entries.Count);
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.PageUp:
            _mainMenuIndex = Clamp(_mainMenuIndex - pageSize, 0, entries.Count - 1);
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.PageDown:
            _mainMenuIndex = Clamp(_mainMenuIndex + pageSize, 0, entries.Count - 1);
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.Home:
            _mainMenuIndex = 0;
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.End:
            _mainMenuIndex = entries.Count - 1;
            EnsureMainMenuSelectionVisible(entries.Count);
            RememberMainMenuSelection(entries);
            e.Handled = true;
            break;

        case Keys.Tab:
            MoveMainMenuSectionFocus(entries, e.Shift ? -1 : 1);
            e.Handled = true;
            break;

        case Keys.Left:
        case Keys.NumPad4:
            ChangeMainMenuValue(-1, entries);
            e.Handled = true;
            break;

        case Keys.Right:
        case Keys.NumPad6:
            ChangeMainMenuValue(1, entries);
            e.Handled = true;
            break;

        case Keys.Enter:
        case Keys.NumPad5:
            ActivateMainMenuItem(entries);
            e.Handled = true;
            break;

        case Keys.T:
            if (GetSelectedMainMenuAction(entries) == MainMenuAction.NpcModel && CurrentModelOption().IsCustom)
            {
                _customModelInputRequested = true;
                e.Handled = true;
            }
            break;

        case Keys.Escape:
        case Keys.Back:
        case Keys.NumPad0:
            SetMenuVisible(false);
            e.Handled = true;
            break;
    }
}

private void MoveMainMenuSectionFocus(List<MainMenuEntry> entries, int direction)
{
    RememberMainMenuSelection(entries);
    CycleMainMenuCategory(direction);
}

    private void HandleWeaponEditorKey(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
            case Keys.NumPad8:
                _weaponEditorIndex = Wrap(_weaponEditorIndex - 1, WeaponEditorItemCount);
                e.Handled = true;
                break;

            case Keys.Down:
            case Keys.NumPad2:
                _weaponEditorIndex = Wrap(_weaponEditorIndex + 1, WeaponEditorItemCount);
                e.Handled = true;
                break;

            case Keys.PageUp:
                _weaponEditorIndex = Clamp(_weaponEditorIndex - 5, 0, WeaponEditorItemCount - 1);
                e.Handled = true;
                break;

            case Keys.PageDown:
                _weaponEditorIndex = Clamp(_weaponEditorIndex + 5, 0, WeaponEditorItemCount - 1);
                e.Handled = true;
                break;

            case Keys.Home:
                _weaponEditorIndex = 0;
                e.Handled = true;
                break;

            case Keys.End:
                _weaponEditorIndex = WeaponEditorItemCount - 1;
                e.Handled = true;
                break;

            case Keys.Left:
            case Keys.NumPad4:
                ChangeWeaponEditorValue(-1);
                e.Handled = true;
                break;

            case Keys.Right:
            case Keys.NumPad6:
                ChangeWeaponEditorValue(1);
                e.Handled = true;
                break;

            case Keys.Enter:
            case Keys.NumPad5:
                ActivateWeaponEditorItem();
                e.Handled = true;
                break;

            case Keys.Tab:
                _menuPage = MenuPage.Main;
                ResetMenuSelectionAnimation();
                CycleMainMenuCategory(e.Shift ? -1 : 1);
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

private void ChangeMainMenuValue(int direction)
{
    List<MainMenuEntry> entries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(entries);
    ChangeMainMenuValue(direction, entries);
}

private void ChangeMainMenuValue(int direction, List<MainMenuEntry> entries)
{
    MainMenuEntry entry = GetSelectedMainMenuEntry(entries);

    switch (entry.Action)
    {
        case MainMenuAction.PlacementType:
            // Dans la page Interieurs, le type ne doit jamais sortir du couple
            // Entree/Sortie quand je le modifie avec Gauche/Droite.
            _selectedPlacementType = _selectedPlacementType == PlacementEntityType.Entrance
                ? PlacementEntityType.Exit
                : PlacementEntityType.Entrance;
            DeletePlacementPreview();
            ShowStatus("Type de placement: " + PlacementTypeDisplayName(_selectedPlacementType), 1600);
            break;

        case MainMenuAction.PrecisePlacement:
        case MainMenuAction.DistancePlacement:
            // Ces lignes sont des actions. Entree valide, Gauche/Droite ne change rien.
            break;

        case MainMenuAction.PlacementDistance:
            int distanceFast = GetMainMenuFastStep();
            _selectedDistance = Clamp(_selectedDistance + direction * DistanceStep * distanceFast, MinDistance, MaxDistance);
            _selectedDistance = RoundToStep(_selectedDistance, DistanceStep);
            break;

        case MainMenuAction.NpcCategory:
            ChangeModelCategory(direction);
            break;

        case MainMenuAction.NpcModel:
            ChangeModel(direction * GetMainMenuFastStep());
            break;

        case MainMenuAction.NpcWeaponCategory:
            ChangeWeaponCategory(direction);
            break;

        case MainMenuAction.NpcWeapon:
            ChangeWeapon(direction * GetMainMenuFastStep());
            break;

        case MainMenuAction.NpcWeaponEditor:
            ChangeWeaponPreset(direction);
            break;

        case MainMenuAction.NpcHealth:
            _selectedHealth = Clamp(_selectedHealth + direction * (IsMenuShiftHeldSafe() ? 250 : 25), MinHealth, MaxHealth);
            break;

        case MainMenuAction.NpcArmor:
            _selectedArmor = Clamp(_selectedArmor + direction * (IsMenuShiftHeldSafe() ? 25 : 5), MinArmor, MaxArmor);
            break;

        case MainMenuAction.NpcBehavior:
            _selectedBehavior = CycleEnum(_selectedBehavior, direction);
            break;

        case MainMenuAction.NpcPatrolRadius:
            int patrolFast = GetMainMenuFastStep();
            _selectedPatrolRadius = Clamp(_selectedPatrolRadius + direction * PatrolRadiusStep * patrolFast, MinPatrolRadius, MaxPatrolRadius);
            _selectedPatrolRadius = RoundToStep(_selectedPatrolRadius, PatrolRadiusStep);
            break;

        case MainMenuAction.NpcAutoRespawn:
        case MainMenuAction.VehicleAutoRespawn:
        case MainMenuAction.ObjectAutoRespawn:
            _selectedAutoRespawn = !_selectedAutoRespawn;
            break;

        case MainMenuAction.VehicleCategory:
            ChangeVehicleCategory(direction);
            break;

        case MainMenuAction.VehicleModel:
            ChangeVehicle(direction * GetMainMenuFastStep());
            break;

        case MainMenuAction.ObjectCategory:
            ChangeObjectCategory(direction);
            break;

        case MainMenuAction.ObjectModel:
            ChangeObject(direction * GetMainMenuFastStep());
            break;

        case MainMenuAction.InteriorCategory:
            ChangeInteriorCategory(direction);
            break;

        case MainMenuAction.InteriorModel:
            ChangeInterior(direction * GetMainMenuFastStep());
            break;

        case MainMenuAction.ExitActiveInfo:
        case MainMenuAction.ExitDestinationInfo:
            ShowStatus("Les sorties utilisent automatiquement la derniere entree active.", 2500);
            break;

        case MainMenuAction.TerminatorMode:
            ToggleTerminatorMode();
            break;

        case MainMenuAction.JusticeEnabled:
            RequestJusticeToggle();
            break;

        case MainMenuAction.JusticeProfile:
            ChangeJusticeMenuSelectedProfile(direction);
            break;

        case MainMenuAction.JusticePoliceMode:
            CycleJusticePoliceIntegrationMode(direction);
            break;
    }

    List<MainMenuEntry> refreshedEntries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(refreshedEntries);
    RememberMainMenuSelection(refreshedEntries);
}

private int GetMainMenuFastStep()
{
    return IsMenuShiftHeldSafe() ? 10 : 1;
}

private static bool IsMenuShiftHeldSafe()
{
    try
    {
        return IsShiftHeld();
    }
    catch
    {
        // Je garde le pas normal quand l'API GTA n'est pas chargee dans les simulations headless.
        return false;
    }
}

private void ActivateMainMenuItem()
{
    List<MainMenuEntry> entries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(entries);
    ActivateMainMenuItem(entries);
}

private void ActivateMainMenuItem(List<MainMenuEntry> entries)
{
    MainMenuEntry entry = GetSelectedMainMenuEntry(entries);

    switch (entry.Action)
    {
        case MainMenuAction.PlacementType:
            _selectedPlacementType = _selectedPlacementType == PlacementEntityType.Entrance
                ? PlacementEntityType.Exit
                : PlacementEntityType.Entrance;
            DeletePlacementPreview();
            ShowStatus("Type de placement: " + PlacementTypeDisplayName(_selectedPlacementType), 1600);
            break;

        case MainMenuAction.PrecisePlacement:
            StartPlacementMode();
            break;

        case MainMenuAction.DistancePlacement:
            QueueCurrentPlacementAtDistance();
            break;

        case MainMenuAction.NpcModel:
            if (CurrentModelOption().IsCustom)
            {
                _customModelInputRequested = true;
            }
            break;

        case MainMenuAction.NpcWeaponEditor:
            _menuPage = MenuPage.WeaponEditor;
            _weaponEditorIndex = 1;
            ResetMenuSelectionAnimation();
            break;

        case MainMenuAction.NpcAutoRespawn:
        case MainMenuAction.VehicleAutoRespawn:
        case MainMenuAction.ObjectAutoRespawn:
            _selectedAutoRespawn = !_selectedAutoRespawn;
            break;

        case MainMenuAction.ExitActiveInfo:
        case MainMenuAction.ExitDestinationInfo:
            ShowStatus("Place une entree, entre dedans, puis place une sortie dans l'interieur.", 3500);
            break;

        case MainMenuAction.Save:
            _saveRequested = true;
            break;

        case MainMenuAction.Load:
            _loadRequested = true;
            break;

        case MainMenuAction.CleanNpcs:
        case MainMenuAction.CleanVehicles:
        case MainMenuAction.CleanObjects:
        case MainMenuAction.CleanInteriorPortals:
            RequestDangerConfirmation(entry.Action);
            break;

        case MainMenuAction.TerminatorMode:
            ToggleTerminatorMode();
            break;

        case MainMenuAction.JusticeEnabled:
            RequestJusticeToggle();
            break;

        case MainMenuAction.JusticeProfile:
            ShowStatus("Utilise Gauche/Droite pour choisir le personnage Justice.", 2400);
            break;

        case MainMenuAction.JusticeStatus:
        case MainMenuAction.JusticeLastCrime:
        case MainMenuAction.JusticeSeverity:
        case MainMenuAction.JusticeWarrant:
        case MainMenuAction.JusticeRecognitionStatus:
        case MainMenuAction.JusticeRecognitionPlate:
        case MainMenuAction.JusticeRecognitionOutfit:
        case MainMenuAction.JusticeRecognitionWarrant:
        case MainMenuAction.JusticeRecognitionDistance:
        case MainMenuAction.JusticeFine:
        case MainMenuAction.JusticeFineDispute:
        case MainMenuAction.JusticeSentence:
        case MainMenuAction.JusticeRecidivism:
            ShowStatus(
                entry.Action == MainMenuAction.JusticeRecognitionStatus ||
                entry.Action == MainMenuAction.JusticeRecognitionPlate ||
                entry.Action == MainMenuAction.JusticeRecognitionOutfit ||
                entry.Action == MainMenuAction.JusticeRecognitionWarrant ||
                entry.Action == MainMenuAction.JusticeRecognitionDistance
                    ? "La reconnaissance concerne toujours le héros actuellement joué."
                    : "Les details Justice sont regroupes dans le panneau droit.",
                2600);
            break;

        case MainMenuAction.JusticePayFine:
            RequestJusticeSelectedProfileFinePaymentConfirmation();
            break;

        case MainMenuAction.JusticeResolveFineDispute:
            if (CanJusticeResolveSelectedFineDispute())
            {
                RequestDangerConfirmation(entry.Action);
            }
            else
            {
                ShowStatus("Justice : aucun montant litigieux à résoudre.", 3000);
            }
            break;

        case MainMenuAction.JusticePoliceMode:
            ShowStatus("Utilise Gauche/Droite pour régler la compatibilité police.", 3000);
            break;

        case MainMenuAction.JusticeRecovery:
            RecoverJusticeControlsAndInventoryFromMenu();
            break;

        case MainMenuAction.JusticeDiagnostic:
            ShowJusticeDiagnosticStatus();
            break;

        case MainMenuAction.JusticeResetProfile:
            RequestJusticeSelectedProfileReset();
            break;

        case MainMenuAction.JusticeCharges:
            OpenJusticeLedger(false);
            break;

        case MainMenuAction.JusticeRecord:
            OpenJusticeLedger(true);
            break;
    }

    List<MainMenuEntry> refreshedEntries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(refreshedEntries);
    RememberMainMenuSelection(refreshedEntries);
}

private List<MainMenuEntry> BuildMainMenuEntries()
{
    return BuildObsidianMainMenuEntries();
}

private MainMenuEntry GetSelectedMainMenuEntry()
{
    List<MainMenuEntry> entries = BuildMainMenuEntries();
    NormalizeMainMenuSelection(entries);
    return GetSelectedMainMenuEntry(entries);
}

private MainMenuEntry GetSelectedMainMenuEntry(List<MainMenuEntry> entries)
{
    if (entries.Count == 0)
    {
        return new MainMenuEntry(MainMenuAction.PlacementType, string.Empty, string.Empty, MainMenuRowKind.Normal, false);
    }

    return entries[_mainMenuIndex];
}

private MainMenuAction GetSelectedMainMenuAction()
{
    return GetSelectedMainMenuEntry().Action;
}

private MainMenuAction GetSelectedMainMenuAction(List<MainMenuEntry> entries)
{
    return GetSelectedMainMenuEntry(entries).Action;
}

private void NormalizeMainMenuSelection(List<MainMenuEntry> entries)
{
    int count = entries == null ? 0 : entries.Count;

    if (count <= 0)
    {
        _mainMenuIndex = 0;
        _mainMenuScrollOffset = 0;
        return;
    }

    _mainMenuIndex = Clamp(_mainMenuIndex, 0, count - 1);
    EnsureMainMenuSelectionVisible(count);
}

private void EnsureMainMenuSelectionVisible(int entryCount)
{
    int visibleRows = GetMainMenuCompactVisibleRowCount(entryCount);

    if (_mainMenuIndex < _mainMenuScrollOffset)
    {
        _mainMenuScrollOffset = _mainMenuIndex;
    }
    else if (_mainMenuIndex >= _mainMenuScrollOffset + visibleRows)
    {
        _mainMenuScrollOffset = _mainMenuIndex - visibleRows + 1;
    }

    int maxScroll = Math.Max(0, entryCount - visibleRows);
    _mainMenuScrollOffset = Clamp(_mainMenuScrollOffset, 0, maxScroll);
}

private static int GetMainMenuCompactVisibleRowCount(int entryCount)
{
    if (entryCount <= 0)
    {
        return 1;
    }

    return Math.Min(MainMenuCompactVisibleRowLimit, entryCount);
}

private void OpenMainMenuSectionForPlacementType(PlacementEntityType placementType)
{
    SetMainMenuCategory(CategoryForPlacementType(placementType));
}

private string ActiveInteriorSessionDisplayName()
{
    return _activeInteriorSession != null && _activeInteriorSession.Interior != null
        ? _activeInteriorSession.Interior.DisplayName
        : "Aucune entree active";
}

private string ExitDestinationDisplayName()
{
    return _activeInteriorSession != null
        ? "Retour au marqueur d'entree"
        : "Entre d'abord par une entree";
}

private void DrawMenu()
{
    if (_menuPage == MenuPage.WeaponEditor)
    {
        DrawObsidianWeaponEditorMenu();
        return;
    }

    if (_menuPage == MenuPage.JusticeCharges ||
        _menuPage == MenuPage.JusticeRecord)
    {
        DrawObsidianJusticeLedgerMenu(_menuPage == MenuPage.JusticeRecord);
        return;
    }

    DrawObsidianMainMenu();
}


    private void ChangeWeaponEditorValue(int direction)
    {
        switch (_weaponEditorIndex)
        {
            case 1:
                ChangeWeaponPreset(direction);
                break;

            case 2:
                _selectedWeaponLoadout.ExtendedClip = !_selectedWeaponLoadout.ExtendedClip;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 3:
                _selectedWeaponLoadout.Suppressor = !_selectedWeaponLoadout.Suppressor;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 4:
                _selectedWeaponLoadout.Flashlight = !_selectedWeaponLoadout.Flashlight;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 5:
                _selectedWeaponLoadout.Grip = !_selectedWeaponLoadout.Grip;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 6:
                _selectedWeaponLoadout.Scope = CycleEnum(_selectedWeaponLoadout.Scope, direction);
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 7:
                _selectedWeaponLoadout.Muzzle = !_selectedWeaponLoadout.Muzzle;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 8:
                _selectedWeaponLoadout.ImprovedBarrel = !_selectedWeaponLoadout.ImprovedBarrel;
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 9:
                _selectedWeaponLoadout.Mk2Ammo = CycleEnum(_selectedWeaponLoadout.Mk2Ammo, direction);
                _selectedWeaponLoadout.Preset = WeaponUpgradePreset.Standard;
                RefreshPlacementPreviewWeapon();
                break;

            case 10:
                _selectedWeaponLoadout.Tint = Clamp(_selectedWeaponLoadout.Tint + direction, 0, 31);
                RefreshPlacementPreviewWeapon();
                break;
        }
    }

    private void ActivateWeaponEditorItem()
    {
        switch (_weaponEditorIndex)
        {
            case 0:
                _menuPage = MenuPage.Main;
                ResetMenuSelectionAnimation();
                break;

            case 11:
                ApplySelectedWeaponLoadoutToSpawnedPeds();
                break;
        }
    }

    private void ChangeWeaponPreset(int direction)
    {
        _selectedWeaponLoadout.Preset = CycleEnum(_selectedWeaponLoadout.Preset, direction);
        ApplyWeaponPreset(_selectedWeaponLoadout);
        RefreshPlacementPreviewWeapon();
    }

    private static void ApplyWeaponPreset(WeaponLoadout loadout)
    {
        if (loadout == null)
        {
            return;
        }

        loadout.ExtendedClip = false;
        loadout.Suppressor = false;
        loadout.Flashlight = false;
        loadout.Grip = false;
        loadout.Scope = WeaponScopeMode.None;
        loadout.Muzzle = false;
        loadout.ImprovedBarrel = false;
        loadout.Mk2Ammo = WeaponMk2AmmoMode.Standard;

        switch (loadout.Preset)
        {
            case WeaponUpgradePreset.ChargeurEtendu:
                loadout.ExtendedClip = true;
                break;

            case WeaponUpgradePreset.Silencieux:
                loadout.ExtendedClip = true;
                loadout.Suppressor = true;
                break;

            case WeaponUpgradePreset.Tactique:
                loadout.ExtendedClip = true;
                loadout.Suppressor = true;
                loadout.Flashlight = true;
                loadout.Grip = true;
                loadout.Scope = WeaponScopeMode.Small;
                break;

            case WeaponUpgradePreset.Full:
                loadout.ExtendedClip = true;
                loadout.Suppressor = true;
                loadout.Flashlight = true;
                loadout.Grip = true;
                loadout.Scope = WeaponScopeMode.Medium;
                loadout.Muzzle = true;
                loadout.ImprovedBarrel = true;
                break;
        }
    }

    private void ApplySelectedWeaponLoadoutToSpawnedPeds()
    {
        int updated = 0;

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc spawned = _spawnedNpcs[i];

            if (spawned == null || !Entity.Exists(spawned.Ped) || spawned.Ped.IsDead)
            {
                continue;
            }

            spawned.Loadout = _selectedWeaponLoadout.Clone();
            GiveWeaponLoadout(spawned.Ped, spawned.Loadout, false);
            updated++;
        }

        ShowStatus("Arme mise a jour sur " + updated.ToString(CultureInfo.InvariantCulture) + " NPC.", 3500);
    }

    private void RefreshPlacementPreviewWeapon()
    {
        if (Entity.Exists(_placementPreviewPed))
        {
            GiveWeaponLoadout(_placementPreviewPed, _selectedWeaponLoadout, false);
        }
    }

    private ModelCategory CurrentModelCategory()
    {
        if (_modelCategories.Count == 0)
        {
            return new ModelCategory { Name = "Aucune", Options = new List<ModelOption>() };
        }

        _selectedModelCategoryIndex = Clamp(_selectedModelCategoryIndex, 0, _modelCategories.Count - 1);
        return _modelCategories[_selectedModelCategoryIndex];
    }

    private ModelOption CurrentModelOption()
    {
        ModelCategory category = CurrentModelCategory();

        if (category.Options.Count == 0)
        {
            return _allModelOptions.Count > 0
                ? _allModelOptions[0]
                : new ModelOption { DisplayName = "Custom", IsCustom = true, Hash = 0 };
        }

        _selectedModelIndexInCategory = Wrap(_selectedModelIndexInCategory, category.Options.Count);
        return category.Options[_selectedModelIndexInCategory];
    }

    private WeaponCategory CurrentWeaponCategory()
    {
        if (_weaponCategories.Count == 0)
        {
            return new WeaponCategory { Name = "Aucune", Options = new List<WeaponOption>() };
        }

        _selectedWeaponCategoryIndex = Clamp(_selectedWeaponCategoryIndex, 0, _weaponCategories.Count - 1);
        return _weaponCategories[_selectedWeaponCategoryIndex];
    }

    private WeaponOption CurrentWeaponOption()
    {
        WeaponCategory category = CurrentWeaponCategory();

        if (category.Options.Count == 0)
        {
            return _allWeaponOptions.Count > 0
                ? _allWeaponOptions[0]
                : new WeaponOption { DisplayName = "Unarmed", Hash = WeaponHash.Unarmed };
        }

        _selectedWeaponIndexInCategory = Wrap(_selectedWeaponIndexInCategory, category.Options.Count);
        return category.Options[_selectedWeaponIndexInCategory];
    }

    private VehicleCategory CurrentVehicleCategory()
    {
        if (_vehicleCategories.Count == 0)
        {
            return new VehicleCategory { Name = "Aucune", Options = new List<VehicleOption>() };
        }

        _selectedVehicleCategoryIndex = Clamp(_selectedVehicleCategoryIndex, 0, _vehicleCategories.Count - 1);
        return _vehicleCategories[_selectedVehicleCategoryIndex];
    }

    private VehicleOption CurrentVehicleOption()
    {
        VehicleCategory category = CurrentVehicleCategory();

        if (category.Options.Count == 0)
        {
            return _allVehicleOptions.Count > 0
                ? _allVehicleOptions[0]
                : new VehicleOption { DisplayName = "Adder", Hash = VehicleHash.Adder };
        }

        _selectedVehicleIndexInCategory = Wrap(_selectedVehicleIndexInCategory, category.Options.Count);
        return category.Options[_selectedVehicleIndexInCategory];
    }

    private ObjectCategory CurrentObjectCategory()
    {
        if (_objectCategories.Count == 0)
        {
            return new ObjectCategory { Name = "Aucune", Options = new List<ObjectOption>() };
        }

        _selectedObjectCategoryIndex = Clamp(_selectedObjectCategoryIndex, 0, _objectCategories.Count - 1);
        return _objectCategories[_selectedObjectCategoryIndex];
    }

    private ObjectOption CurrentObjectOption()
    {
        ObjectCategory category = CurrentObjectCategory();

        if (category.Options.Count == 0)
        {
            return _allObjectOptions.Count > 0
                ? _allObjectOptions[0]
                : new ObjectOption { DisplayName = "Cone orange", ModelName = "prop_mp_cone_01", Category = ObjectPlacementCategory.Securite };
        }

        _selectedObjectIndexInCategory = Wrap(_selectedObjectIndexInCategory, category.Options.Count);
        return category.Options[_selectedObjectIndexInCategory];
    }

    private string CurrentModelDisplayName()
    {
        return CurrentModelOption().GetDisplayName(_customModelName);
    }

    private string CurrentWeaponDisplayName()
    {
        return CurrentWeaponOption().DisplayName;
    }

    private string CurrentVehicleDisplayName()
    {
        return CurrentVehicleOption().DisplayName;
    }

    private string CurrentObjectDisplayName()
    {
        return ObjectOptionMenuDisplayName(CurrentObjectOption());
    }

    private static string ObjectOptionMenuDisplayName(ObjectOption option)
    {
        ObjectIdentity identity = CreateObjectIdentityPreview(option);

        if (identity.InteractionKind == ObjectInteractionKind.Cash && identity.CashValue > 0)
        {
            return identity.DisplayName + " | +" + FormatMoney(identity.CashValue);
        }

        return identity.DisplayName;
    }

    private static ObjectIdentity CreateObjectIdentityPreview(ObjectOption option)
    {
        if (option == null)
        {
            return new ObjectIdentity
            {
                ModelName = string.Empty,
                DisplayName = "Aucun objet",
                InteractionKind = ObjectInteractionKind.None
            };
        }

        string displayName = string.IsNullOrWhiteSpace(option.DisplayName)
            ? (string.IsNullOrWhiteSpace(option.ModelName) ? "Objet" : option.ModelName)
            : option.DisplayName;

        ObjectIdentity identity = new ObjectIdentity
        {
            ModelName = option.ModelName,
            DisplayName = displayName,
            InteractionKind = option.InteractionKind,
            CashValue = option.CashValue,
            HealAmount = option.HealAmount,
            ArmorAmount = option.ArmorAmount,
            AmmoAmount = option.AmmoAmount
        };

        ApplyDefaultObjectInteractionIfNeeded(identity);
        return identity;
    }

    private void ChangeModelCategory(int direction)
    {
        _selectedModelCategoryIndex = Wrap(_selectedModelCategoryIndex + direction, _modelCategories.Count);
        _selectedModelIndexInCategory = 0;
        DeletePlacementPreview();
    }

    private void ChangeModel(int direction)
    {
        ModelCategory category = CurrentModelCategory();

        if (category.Options.Count == 0)
        {
            return;
        }

        _selectedModelIndexInCategory = Wrap(_selectedModelIndexInCategory + direction, category.Options.Count);
        DeletePlacementPreview();
    }

    private void ChangeWeaponCategory(int direction)
    {
        _selectedWeaponCategoryIndex = Wrap(_selectedWeaponCategoryIndex + direction, _weaponCategories.Count);
        _selectedWeaponIndexInCategory = 0;
        OnSelectedWeaponChanged();
    }

    private void ChangeWeapon(int direction)
    {
        WeaponCategory category = CurrentWeaponCategory();

        if (category.Options.Count == 0)
        {
            return;
        }

        _selectedWeaponIndexInCategory = Wrap(_selectedWeaponIndexInCategory + direction, category.Options.Count);
        OnSelectedWeaponChanged();
    }

    private void ChangeVehicleCategory(int direction)
    {
        _selectedVehicleCategoryIndex = Wrap(_selectedVehicleCategoryIndex + direction, _vehicleCategories.Count);
        _selectedVehicleIndexInCategory = 0;
        DeletePlacementPreview();
    }

    private void ChangeVehicle(int direction)
    {
        VehicleCategory category = CurrentVehicleCategory();

        if (category.Options.Count == 0)
        {
            return;
        }

        _selectedVehicleIndexInCategory = Wrap(_selectedVehicleIndexInCategory + direction, category.Options.Count);
        DeletePlacementPreview();
    }

    private void ChangeObjectCategory(int direction)
    {
        _selectedObjectCategoryIndex = Wrap(_selectedObjectCategoryIndex + direction, _objectCategories.Count);
        _selectedObjectIndexInCategory = 0;
        DeletePlacementPreview();
    }

    private void ChangeObject(int direction)
    {
        ObjectCategory category = CurrentObjectCategory();

        if (category.Options.Count == 0)
        {
            return;
        }

        _selectedObjectIndexInCategory = Wrap(_selectedObjectIndexInCategory + direction, category.Options.Count);
        DeletePlacementPreview();
    }

    private void OnSelectedWeaponChanged()
    {
        _selectedWeaponLoadout.Weapon = CurrentWeaponOption().Hash;
        RefreshPlacementPreviewWeapon();
    }

    private void SelectDefaultModel()
    {
        int securityCategory = FindCategoryIndex(_modelCategories, "Securite");
        _selectedModelCategoryIndex = securityCategory >= 0 ? securityCategory : 0;

        ModelCategory category = CurrentModelCategory();

        int swat = category.Options.FindIndex(m =>
            !m.IsCustom &&
            string.Equals(m.DisplayName, "Swat01SMY", StringComparison.OrdinalIgnoreCase));

        if (swat >= 0)
        {
            _selectedModelIndexInCategory = swat;
            return;
        }

        int cop = category.Options.FindIndex(m =>
            !m.IsCustom &&
            m.DisplayName.IndexOf("Cop", StringComparison.OrdinalIgnoreCase) >= 0);

        _selectedModelIndexInCategory = cop >= 0 ? cop : 0;
    }

    private void SelectDefaultWeapon()
    {
        int riflesCategory = FindCategoryIndex(_weaponCategories, "Fusils d'assaut");
        _selectedWeaponCategoryIndex = riflesCategory >= 0 ? riflesCategory : 0;

        WeaponCategory category = CurrentWeaponCategory();

        int carbine = category.Options.FindIndex(w =>
            string.Equals(w.DisplayName, "CarbineRifle", StringComparison.OrdinalIgnoreCase));

        if (carbine >= 0)
        {
            _selectedWeaponIndexInCategory = carbine;
            return;
        }

        int pistol = category.Options.FindIndex(w =>
            string.Equals(w.DisplayName, "Pistol", StringComparison.OrdinalIgnoreCase));

        _selectedWeaponIndexInCategory = pistol >= 0 ? pistol : 0;
    }

    private void SelectDefaultVehicle()
    {
        int sportCategory = FindCategoryIndex(_vehicleCategories, "Sport");
        _selectedVehicleCategoryIndex = sportCategory >= 0 ? sportCategory : 0;
        _selectedVehicleIndexInCategory = 0;
    }

    private void SelectDefaultObject()
    {
        int securityCategory = FindCategoryIndex(_objectCategories, "Securite");
        _selectedObjectCategoryIndex = securityCategory >= 0 ? securityCategory : 0;
        _selectedObjectIndexInCategory = 0;
    }

    private static int FindCategoryIndex<TCategory>(List<TCategory> categories, string name)
    {
        for (int i = 0; i < categories.Count; i++)
        {
            object category = categories[i];

            ModelCategory modelCategory = category as ModelCategory;
            if (modelCategory != null && modelCategory.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return i;

            WeaponCategory weaponCategory = category as WeaponCategory;
            if (weaponCategory != null && weaponCategory.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return i;

            VehicleCategory vehicleCategory = category as VehicleCategory;
            if (vehicleCategory != null && vehicleCategory.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return i;

            ObjectCategory objectCategory = category as ObjectCategory;
            if (objectCategory != null && objectCategory.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return i;
        }

        return -1;
    }

    private void EditCustomModelName()
    {
        SetMenuVisible(false);

        string input = Game.GetUserInput(_customModelName, 64);

        if (!string.IsNullOrWhiteSpace(input))
        {
            _customModelName = input.Trim();
            SelectCustomModelCategory();
            ShowStatus("Modele custom defini: " + _customModelName, 3500);
        }
        else
        {
            ShowStatus("Nom de modele custom ignore.", 2500);
        }

        SetMenuVisible(true);
    }

    private void SelectCustomModelCategory()
    {
        for (int i = 0; i < _modelCategories.Count; i++)
        {
            if (_modelCategories[i].Options.Any(o => o.IsCustom))
            {
                _selectedModelCategoryIndex = i;
                _selectedModelIndexInCategory = _modelCategories[i].Options.FindIndex(o => o.IsCustom);
                return;
            }
        }
    }

    private void QueueCurrentPlacementAtDistance()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Impossible: joueur introuvable ou mort.", 3000);
            return;
        }

        Vector3 spawnPosition = FindDistanceSpawnPosition(player);
        QueueSpawn(_selectedPlacementType, spawnPosition, new Vector3(0.0f, 0.0f, 1.0f), false);
    }

    private void QueueSpawn(PlacementEntityType placementType, Vector3 position, Vector3 surfaceNormal, bool precise)
    {
        QueueSpawn(placementType, position, surfaceNormal, precise, false, 0.0f);
    }

    private void QueueSpawn(PlacementEntityType placementType, Vector3 position, Vector3 surfaceNormal, bool precise, bool hasHeadingOverride, float headingOverride)
    {
        _requestedPlacementType = placementType;
        _requestedSpawnPosition = position;
        _requestedSpawnSurfaceNormal = surfaceNormal;
        _spawnRequestedPrecise = precise;
        _requestedHasHeadingOverride = hasHeadingOverride;
        _requestedSpawnHeading = headingOverride;
        _spawnRequested = true;
    }

    private void ProcessPendingSpawn()
    {
        if (!_spawnRequested)
        {
            return;
        }

        _spawnRequested = false;

        TrySpawnPlacement(
            _requestedPlacementType,
            _requestedSpawnPosition,
            _requestedSpawnSurfaceNormal,
            _spawnRequestedPrecise,
            _requestedHasHeadingOverride,
            _requestedSpawnHeading);
    }

    private bool TrySpawnPlacement(PlacementEntityType placementType, Vector3 requestedPosition, Vector3 surfaceNormal, bool precise, bool hasHeadingOverride, float headingOverride)
    {
        switch (placementType)
        {
            case PlacementEntityType.Vehicle:
                return TrySpawnVehicle(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);

            case PlacementEntityType.Object:
                return TrySpawnObject(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);

            case PlacementEntityType.Entrance:
                return TryPlaceInteriorEntrance(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);

            case PlacementEntityType.Exit:
                return TryPlaceInteriorExit(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);

            case PlacementEntityType.Npc:
            default:
                return TrySpawnNpc(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);
        }
    }

    private bool TrySpawnNpc(Vector3 requestedPosition, Vector3 surfaceNormal, bool precise, bool hasHeadingOverride, float headingOverride)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            LogWarning("SpawnNpc", "Spawn annule: joueur invalide.");
            ShowStatus("Spawn annule: joueur invalide.", 3000);
            return false;
        }

        EnsureRelationshipGroups();

        Vector3 spawnPosition = precise
            ? AdjustNpcSpawnPosition(requestedPosition, surfaceNormal)
            : AdjustDistanceSpawnPosition(requestedPosition);

        float heading = hasHeadingOverride
            ? NormalizeHeading(headingOverride)
            : HeadingFromTo(spawnPosition, player.Position);

        ModelIdentity modelIdentity = BuildCurrentModelIdentity();
        WeaponLoadout loadout = _selectedWeaponLoadout.Clone();

        Ped ped = CreatePedFromModelIdentity(modelIdentity, spawnPosition, heading);

        if (!Entity.Exists(ped))
        {
            return false;
        }

        RegisterSpawnedNpc(
            ped,
            _selectedBehavior,
            precise,
            true,
            modelIdentity,
            loadout,
            _selectedHealth,
            _selectedArmor,
            ped.Position,
            heading,
            _selectedPatrolRadius,
            _selectedAutoRespawn);

        return true;
    }

    private bool TrySpawnVehicle(Vector3 requestedPosition, Vector3 surfaceNormal, bool precise, bool hasHeadingOverride, float headingOverride)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            LogWarning("SpawnVehicle", "Placement vehicule annule: joueur invalide.");
            ShowStatus("Placement vehicule annule: joueur invalide.", 3000);
            return false;
        }

        Vector3 position = precise
            ? AdjustVehicleSpawnPosition(requestedPosition, surfaceNormal)
            : AdjustDistanceSpawnPosition(requestedPosition);

        float heading = hasHeadingOverride
            ? NormalizeHeading(headingOverride)
            : HeadingFromTo(position, player.Position);

        VehicleIdentity identity = BuildCurrentVehicleIdentity();
        Vehicle vehicle = CreateVehicleFromIdentity(identity, position, heading);

        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        RegisterPlacedVehicle(vehicle, identity, position, heading, true, _selectedAutoRespawn);
        return true;
    }

    private bool TrySpawnObject(Vector3 requestedPosition, Vector3 surfaceNormal, bool precise, bool hasHeadingOverride, float headingOverride)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            LogWarning("SpawnObject", "Placement objet annule: joueur invalide.");
            ShowStatus("Placement objet annule: joueur invalide.", 3000);
            return false;
        }

        Vector3 position = precise
            ? AdjustObjectSpawnPosition(requestedPosition, surfaceNormal)
            : AdjustDistanceSpawnPosition(requestedPosition);

        float heading = hasHeadingOverride
            ? NormalizeHeading(headingOverride)
            : HeadingFromTo(position, player.Position);

        ObjectIdentity identity = BuildCurrentObjectIdentity();
        Prop prop = CreatePropFromIdentity(identity, position, heading);

        if (!Entity.Exists(prop))
        {
            return false;
        }

        RegisterPlacedObject(prop, identity, position, heading, true, _selectedAutoRespawn);
        return true;
    }

    private ModelIdentity BuildCurrentModelIdentity()
    {
        ModelOption option = CurrentModelOption();

        if (option.IsCustom)
        {
            return new ModelIdentity
            {
                IsCustom = true,
                Name = _customModelName,
                Hash = 0,
                DisplayName = "Custom: " + _customModelName
            };
        }

        return new ModelIdentity
        {
            IsCustom = false,
            Name = option.DisplayName,
            Hash = option.Hash,
            DisplayName = option.DisplayName
        };
    }

    private VehicleIdentity BuildCurrentVehicleIdentity()
    {
        VehicleOption option = CurrentVehicleOption();

        return new VehicleIdentity
        {
            Name = option.DisplayName,
            Hash = EnumToIntHash(option.Hash),
            DisplayName = option.DisplayName
        };
    }

    private ObjectIdentity BuildCurrentObjectIdentity()
    {
        ObjectOption option = CurrentObjectOption();

        ObjectIdentity identity = new ObjectIdentity
        {
            ModelName = option.ModelName,
            DisplayName = option.DisplayName,
            InteractionKind = option.InteractionKind,
            CashValue = option.CashValue,
            HealAmount = option.HealAmount,
            ArmorAmount = option.ArmorAmount,
            AmmoAmount = option.AmmoAmount
        };

        ApplyDefaultObjectInteractionIfNeeded(identity);
        return identity;
    }

    private Ped CreatePedFromModelIdentity(ModelIdentity identity, Vector3 position, float heading)
    {
        if (identity == null)
        {
            LogWarning("SpawnNpc", "Modele NPC manquant.");
            ShowStatus("Spawn annule: modele manquant.", 3000);
            return null;
        }

        Model model = identity.ToModel();

        if (!model.IsValid || !model.IsInCdImage || !model.IsPed)
        {
            LogWarning("SpawnNpc", "Modele invalide ou non monte: " + identity.DisplayName);
            ShowStatus("Modele invalide ou non monte: " + identity.DisplayName, 5000);
            return null;
        }

        if (!model.Request(2500))
        {
            LogWarning("SpawnNpc", "Chargement modele impossible: " + identity.DisplayName);
            ShowStatus("Chargement modele impossible: " + identity.DisplayName, 5000);
            return null;
        }

        Ped ped = World.CreatePed(model, position, NormalizeHeading(heading));
        model.MarkAsNoLongerNeeded();

        if (!Entity.Exists(ped))
        {
            LogWarning("SpawnNpc", "Echec creation NPC: " + identity.DisplayName);
            ShowStatus("Echec creation NPC: " + identity.DisplayName, 5000);
            return null;
        }

        return ped;
    }

    private Vehicle CreateVehicleFromIdentity(VehicleIdentity identity, Vector3 position, float heading)
    {
        if (identity == null)
        {
            LogWarning("SpawnVehicle", "Modele vehicule manquant.");
            ShowStatus("Vehicule annule: modele manquant.", 3000);
            return null;
        }

        Model model = identity.ToModel();

        if (!model.IsValid || !model.IsInCdImage || !model.IsVehicle)
        {
            LogWarning("SpawnVehicle", "Vehicule invalide ou non monte: " + identity.DisplayName);
            ShowStatus("Vehicule invalide ou non monte: " + identity.DisplayName, 5000);
            return null;
        }

        if (!model.Request(2500))
        {
            LogWarning("SpawnVehicle", "Chargement vehicule impossible: " + identity.DisplayName);
            ShowStatus("Chargement vehicule impossible: " + identity.DisplayName, 5000);
            return null;
        }

        Vehicle vehicle = World.CreateVehicle(model, position, NormalizeHeading(heading));
        model.MarkAsNoLongerNeeded();

        if (!Entity.Exists(vehicle))
        {
            LogWarning("SpawnVehicle", "Echec creation vehicule: " + identity.DisplayName);
            ShowStatus("Echec creation vehicule: " + identity.DisplayName, 5000);
            return null;
        }

        return vehicle;
    }

    private Prop CreatePropFromIdentity(ObjectIdentity identity, Vector3 position, float heading)
    {
        if (identity == null || string.IsNullOrWhiteSpace(identity.ModelName))
        {
            LogWarning("SpawnObject", "Modele objet manquant.");
            ShowStatus("Objet annule: modele manquant.", 3000);
            return null;
        }

        Model model = identity.ToModel();

        if (!model.IsValid || !model.IsInCdImage)
        {
            LogWarning("SpawnObject", "Objet invalide ou non monte: " + identity.DisplayName);
            ShowStatus("Objet invalide ou non monte: " + identity.DisplayName, 5000);
            return null;
        }

        if (!model.Request(2500))
        {
            LogWarning("SpawnObject", "Chargement objet impossible: " + identity.DisplayName);
            ShowStatus("Chargement objet impossible: " + identity.DisplayName, 5000);
            return null;
        }

        Prop prop = World.CreateProp(model, position, false, false);
        model.MarkAsNoLongerNeeded();

        if (!Entity.Exists(prop))
        {
            LogWarning("SpawnObject", "Echec creation objet: " + identity.DisplayName);
            ShowStatus("Echec creation objet: " + identity.DisplayName, 5000);
            return null;
        }

        prop.Heading = NormalizeHeading(heading);
        return prop;
    }

    private SpawnedNpc RegisterSpawnedNpc(
        Ped ped,
        NpcBehavior behavior,
        bool precisePlacement,
        bool showStatus,
        ModelIdentity modelIdentity,
        WeaponLoadout loadout,
        int health,
        int armor,
        Vector3 homePosition,
        float homeHeading,
        float patrolRadius,
        bool autoRespawn = false)
    {
        if (!Entity.Exists(ped))
        {
            return null;
        }

        EnsureRelationshipGroups();

        ConfigureSpawnedPed(ped, behavior, health, armor, loadout);

        if (!precisePlacement)
        {
            Function.Call((Hash)PlaceEntityOnGroundProperlyNative, ped.Handle);
            homePosition = ped.Position;
        }

        SpawnedNpc spawned = new SpawnedNpc
        {
            Ped = ped,
            Behavior = behavior,
            BaseBehavior = behavior,
            Activated = behavior == NpcBehavior.Attacker || behavior == NpcBehavior.HostilePatrol,
            DeathAlerted = false,
            NextThinkAt = GetInitialNpcThinkTime(),
            NextPassiveTaskAt = GetInitialPassiveTaskTime(),
            NextBlipRefreshAt = GetInitialNpcBlipRefreshTime(),
            HomePosition = homePosition,
            HomeHeading = NormalizeHeading(homeHeading),
            LastCombatActivityAt = Game.GameTime,
            IsReturningHome = false,
            NextReturnTaskAt = 0,
            PatrolCenter = homePosition,
            PatrolRadius = ClampFloat(patrolRadius, MinPatrolRadius, MaxPatrolRadius),
            PatrolTarget = Vector3.Zero,
            NextPatrolTaskAt = 0,
            NextBodyguardTaskAt = 0,
            BodyguardAssignedVehicleHandle = 0,
            BodyguardAssignedSeat = 999,
            BodyguardIsDriver = false,
            ModelIdentity = modelIdentity,
            Loadout = loadout.Clone(),
            SavedMaxHealth = health,
            SavedArmor = armor,
            AutoRespawn = autoRespawn,
            RespawnPending = false,
            RespawnEligibleAt = 0,
            NextRespawnCheckAt = 0
        };

        _spawnedNpcs.Add(spawned);
        CreateOrUpdateNpcBlip(spawned);

        StartNpcRuntimeBehavior(spawned);

        if (showStatus)
        {
            ShowStatus(
                "NPC spawn: " + modelIdentity.DisplayName +
                " | " + CurrentWeaponDisplayName() +
                " | " + NpcBehaviorDisplayName(behavior) +
                " | respawn " + BoolText(autoRespawn),
                3500);
        }

        return spawned;
    }

    private PlacedVehicle RegisterPlacedVehicle(
        Vehicle vehicle,
        VehicleIdentity identity,
        Vector3 position,
        float heading,
        bool showStatus,
        bool autoRespawn = false,
        bool persistentInSave = true)
    {
        if (!Entity.Exists(vehicle))
        {
            return null;
        }

        ConfigurePlacedVehicleEntity(vehicle, heading);

        PlacedVehicle placed = new PlacedVehicle
        {
            Vehicle = vehicle,
            Identity = identity,
            Position = vehicle.Position,
            Heading = NormalizeHeading(vehicle.Heading),
            RespawnPosition = position,
            RespawnHeading = NormalizeHeading(heading),
            AutoRespawn = autoRespawn,
            RespawnPending = false,
            RespawnEligibleAt = 0,
            NextRespawnCheckAt = 0,
            PersistentInSave = persistentInSave
        };

        _placedVehicles.Add(placed);
        CreateOrUpdatePlacedVehicleBlip(placed);

        if (showStatus)
        {
            ShowStatus("Vehicule place: " + identity.DisplayName + " | respawn " + BoolText(autoRespawn), 3500);
        }

        return placed;
    }

    private PlacedObject RegisterPlacedObject(Prop prop, ObjectIdentity identity, Vector3 position, float heading, bool showStatus, bool autoRespawn = false)
    {
        if (!Entity.Exists(prop))
        {
            return null;
        }

        ApplyDefaultObjectInteractionIfNeeded(identity);
        ConfigurePlacedObjectEntity(prop, position, heading);

        PlacedObject placed = new PlacedObject
        {
            Prop = prop,
            Identity = identity,
            Position = prop.Position,
            Heading = NormalizeHeading(prop.Heading),
            RespawnPosition = position,
            RespawnHeading = NormalizeHeading(heading),
            AutoRespawn = autoRespawn,
            RespawnPending = false,
            RespawnEligibleAt = 0,
            NextRespawnCheckAt = 0
        };

        _placedObjects.Add(placed);

        if (showStatus)
        {
            string interactionText = HasObjectInteraction(identity)
                ? " | interactif: " + ObjectInteractionDisplayName(identity)
                : string.Empty;

            ShowStatus("Objet place: " + identity.DisplayName + interactionText + " | respawn " + BoolText(autoRespawn), 3500);
        }

        return placed;
    }

    private void StartNpcRuntimeBehavior(SpawnedNpc spawned)
    {
        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            return;
        }

        switch (spawned.BaseBehavior)
        {
            case NpcBehavior.Attacker:
                ActivateCombatAgainstBestTarget(spawned, false);
                break;

            case NpcBehavior.HostilePatrol:
            case NpcBehavior.NeutralPatrol:
            case NpcBehavior.AllyPatrol:
                StartOrContinuePatrol(spawned, true);
                break;

            case NpcBehavior.Static:
                HoldStaticPosition(spawned.Ped);
                break;

            case NpcBehavior.Neutral:
                HoldGuardPosition(spawned.Ped);
                break;

            case NpcBehavior.Ally:
                HoldAllyPosition(spawned.Ped);
                break;

            case NpcBehavior.Bodyguard:
                UpdateBodyguard(spawned, Game.Player.Character);
                break;
        }
    }

    private void ConfigurePlacedVehicleEntity(Vehicle vehicle, float heading)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        vehicle.IsPersistent = true;
        vehicle.Heading = NormalizeHeading(heading);

        try
        {
            vehicle.Repair();
            vehicle.EngineHealth = 1000.0f;
            vehicle.BodyHealth = 1000.0f;
            vehicle.PetrolTankHealth = 1000.0f;
        }
        catch
        {
        }

        try
        {
            Function.Call((Hash)PlaceEntityOnGroundProperlyNative, vehicle.Handle);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, false, true, false);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
        }
        catch
        {
        }
    }

    private void ConfigurePlacedObjectEntity(Prop prop, Vector3 position, float heading)
    {
        if (!Entity.Exists(prop))
        {
            return;
        }

        prop.IsPersistent = true;
        prop.Position = position;
        prop.Heading = NormalizeHeading(heading);

        try
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, prop.Handle, true, true);
            Function.Call(Hash.SET_ENTITY_COLLISION, prop.Handle, true, true);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, prop.Handle, true);
            Function.Call(Hash.SET_ENTITY_VISIBLE, prop.Handle, true, false);
        }
        catch
        {
        }
    }

    private void ConfigureSpawnedPed(Ped ped, NpcBehavior behavior, int health, int armor, WeaponLoadout loadout)
    {
        ped.IsPersistent = true;
        ped.AlwaysKeepTask = true;

        ped.BlockPermanentEvents =
            behavior == NpcBehavior.Static ||
            behavior == NpcBehavior.Neutral ||
            behavior == NpcBehavior.NeutralPatrol;

        ped.FreezePosition = false;
        ped.IsInvincible = false;
        ped.CanBeTargetted = true;

        ped.MaxHealth = Clamp(health, MinHealth, MaxHealth);
        ped.Health = Clamp(health, MinHealth, MaxHealth);
        ped.Armor = Clamp(armor, MinArmor, MaxArmor);

        ped.Accuracy = 50;
        ped.ShootRate = 750;
        ped.CanSwitchWeapons = true;
        ped.CanRagdoll = true;
        ped.IsEnemy = IsHostileBehavior(behavior);

        Function.Call(Hash.RESET_ENTITY_ALPHA, ped.Handle);
        Function.Call(Hash.SET_ENTITY_ALPHA, ped.Handle, 255, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, ped.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, false);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);
        Function.Call(Hash.SET_ENTITY_VISIBLE, ped.Handle, true, false);

        Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, ped.Handle, false);
        Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, true);

        Function.Call(Hash.SET_PED_SEEING_RANGE, ped.Handle, 2500.0f);
        Function.Call(Hash.SET_PED_HEARING_RANGE, ped.Handle, 2500.0f);
        Function.Call(Hash.SET_PED_ALERTNESS, ped.Handle, 3);

        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);

        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 0, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);

        Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 2);
        Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 2);
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, behavior == NpcBehavior.Static ? 0 : 2);

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, GetRelationshipGroupForNpcBehavior(behavior));

        GiveWeaponLoadout(ped, loadout, false);
    }

    private int GetRelationshipGroupForNpcBehavior(NpcBehavior behavior)
    {
        if (IsAllyBehavior(behavior))
        {
            return _allyGroupHash;
        }

        if (IsNeutralBehavior(behavior))
        {
            return _neutralGroupHash;
        }

        return _hostileGroupHash;
    }

    private Vector3 FindDistanceSpawnPosition(Ped player)
    {
        Vector3 forward = Normalize(player.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 desired = player.Position + forward * _selectedDistance;

        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, desired.X, desired.Y, desired.Z);

        Vector3 safe = World.GetSafeCoordForPed(desired, false, 16);

        if (!IsZeroVector(safe))
        {
            return safe;
        }

        Vector3 top = new Vector3(desired.X, desired.Y, desired.Z + 900.0f);
        Vector3 bottom = new Vector3(desired.X, desired.Y, desired.Z - 250.0f);

        RaycastResult ray = World.Raycast(top, bottom, IntersectOptions.Map, player);

        if (ray.DitHitAnything)
        {
            return ray.HitCoords + new Vector3(0.0f, 0.0f, 1.0f);
        }

        float ground = World.GetGroundHeight(new Vector3(desired.X, desired.Y, desired.Z + 1000.0f));

        if (Math.Abs(ground) > 0.001f)
        {
            desired.Z = ground + 1.0f;
        }

        return desired;
    }

    private Vector3 AdjustDistanceSpawnPosition(Vector3 requested)
    {
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, requested.X, requested.Y, requested.Z);

        Vector3 safe = World.GetSafeCoordForPed(requested, false, 16);

        if (!IsZeroVector(safe))
        {
            return safe;
        }

        return requested;
    }

    private Vector3 AdjustNpcSpawnPosition(Vector3 requested, Vector3 surfaceNormal)
    {
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, requested.X, requested.Y, requested.Z);

        Vector3 normal = Normalize(surfaceNormal);

        if (normal.Length() < 0.001f)
        {
            normal = new Vector3(0.0f, 0.0f, 1.0f);
        }

        if (normal.Z > 0.35f)
        {
            return requested + new Vector3(0.0f, 0.0f, 0.75f);
        }

        return requested + normal * 0.85f + new Vector3(0.0f, 0.0f, 0.35f);
    }

    private Vector3 AdjustVehicleSpawnPosition(Vector3 requested, Vector3 surfaceNormal)
    {
        Vector3 normal = Normalize(surfaceNormal);

        if (normal.Length() < 0.001f)
        {
            normal = new Vector3(0.0f, 0.0f, 1.0f);
        }

        if (normal.Z > 0.35f)
        {
            return requested + new Vector3(0.0f, 0.0f, 0.35f);
        }

        return requested + normal * 0.85f + new Vector3(0.0f, 0.0f, 0.35f);
    }

    private Vector3 AdjustObjectSpawnPosition(Vector3 requested, Vector3 surfaceNormal)
    {
        Vector3 normal = Normalize(surfaceNormal);

        if (normal.Length() < 0.001f)
        {
            normal = new Vector3(0.0f, 0.0f, 1.0f);
        }

        if (normal.Z > 0.35f)
        {
            return requested + new Vector3(0.0f, 0.0f, 0.05f);
        }

        return requested + normal * 0.25f;
    }

    private void CleanAllSpawnedNpcs()
    {
        int deleted = 0;

        DeletePlacementPreview();

        for (int i = _spawnedNpcs.Count - 1; i >= 0; i--)
        {
            SpawnedNpc spawned = _spawnedNpcs[i];

            RemoveNpcBlip(spawned);

            if (spawned != null && Entity.Exists(spawned.Ped))
            {
                try
                {
                    spawned.Ped.Delete();
                    deleted++;
                }
                catch
                {
                }
            }
        }

        _spawnedNpcs.Clear();

        ShowStatus("Nettoyage NPC: " + deleted.ToString(CultureInfo.InvariantCulture) + " supprime(s).", 4500);
    }

    private void CleanAllPlacedVehicles()
    {
        int deleted = 0;

        DeletePlacementPreview();

        for (int i = _placedVehicles.Count - 1; i >= 0; i--)
        {
            PlacedVehicle placed = _placedVehicles[i];

            RemovePlacedVehicleBlip(placed);

            if (placed != null && Entity.Exists(placed.Vehicle))
            {
                try
                {
                    placed.Vehicle.Delete();
                    deleted++;
                }
                catch
                {
                }
            }
        }

        _placedVehicles.Clear();

        ShowStatus("Nettoyage vehicules: " + deleted.ToString(CultureInfo.InvariantCulture) + " supprime(s).", 4500);
    }

    private void CleanAllPlacedObjects()
    {
        int deleted = 0;

        DeletePlacementPreview();

        for (int i = _placedObjects.Count - 1; i >= 0; i--)
        {
            PlacedObject placed = _placedObjects[i];

            if (placed != null && Entity.Exists(placed.Prop))
            {
                try
                {
                    placed.Prop.Delete();
                    deleted++;
                }
                catch
                {
                }
            }
        }

        _placedObjects.Clear();

        ShowStatus("Nettoyage objets: " + deleted.ToString(CultureInfo.InvariantCulture) + " supprime(s).", 4500);
    }

    private void GiveWeaponLoadout(Ped ped, WeaponLoadout loadout, bool showErrors)
    {
        if (!Entity.Exists(ped) || loadout == null)
        {
            return;
        }

        try
        {
            ped.Weapons.RemoveAll();

            if (loadout.Weapon == WeaponHash.Unarmed)
            {
                ped.Weapons.Select(WeaponHash.Unarmed);
                return;
            }

            ped.Weapons.Give(loadout.Weapon, Math.Max(loadout.Ammo, 1), true, true);
            ped.Weapons.Select(loadout.Weapon, true);

            ApplyWeaponComponents(ped, loadout);
            ApplyWeaponTint(ped, loadout);
        }
        catch (Exception ex)
        {
            LogException("GiveWeaponLoadout", ex);
            ped.Weapons.Select(WeaponHash.Unarmed);

            if (showErrors)
            {
                ShowStatus("Arme non compatible: " + ex.Message, 4000);
            }
        }
    }

    private void ApplyWeaponComponents(Ped ped, WeaponLoadout loadout)
    {
        if (!Entity.Exists(ped) || loadout == null || loadout.Weapon == WeaponHash.Unarmed)
        {
            return;
        }

        string weaponName = loadout.Weapon.ToString();

        if (loadout.Mk2Ammo != WeaponMk2AmmoMode.Standard)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, Mk2AmmoComponentCandidates(weaponName, loadout.Mk2Ammo));
        }
        else if (loadout.ExtendedClip)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, ExtendedClipCandidates(weaponName));
        }

        if (loadout.Suppressor)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, new[]
            {
                "AtPiSupp", "AtPiSupp02", "AtSrSupp", "AtSrSupp03",
                "AtArSupp", "AtArSupp02", "CeramicPistolSupp"
            });
        }

        if (loadout.Flashlight)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, new[]
            {
                "AtPiFlsh", "AtPiFlsh02", "AtPiFlsh03", "AtArFlsh", "AtArFlshReh"
            });
        }

        if (loadout.Grip)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, new[] { "AtArAfGrip", "AtArAfGrip02" });
        }

        if (loadout.Scope != WeaponScopeMode.None)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, ScopeCandidates(loadout.Scope));
        }

        if (loadout.Muzzle)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, new[]
            {
                "AtPiComp", "AtPiComp02", "AtPiComp03",
                "AtMuzzle01", "AtMuzzle02", "AtMuzzle03", "AtMuzzle04", "AtMuzzle05",
                "AtMuzzle06", "AtMuzzle07", "AtMuzzle08", "AtMuzzle09"
            });
        }

        if (loadout.ImprovedBarrel)
        {
            TryGiveFirstCompatibleComponent(ped, loadout.Weapon, new[]
            {
                "AtArBarrel02", "AtBpBarrel02", "AtCrBarrel02", "AtMGBarrel02",
                "AtMrFlBarrel02", "AtSbBarrel02", "AtScBarrel02", "AtSrBarrel02"
            });
        }
    }

    private void ApplyWeaponTint(Ped ped, WeaponLoadout loadout)
    {
        if (!Entity.Exists(ped) || loadout == null || loadout.Weapon == WeaponHash.Unarmed)
        {
            return;
        }

        int weaponHash = EnumToIntHash(loadout.Weapon);
        int tintCount = 32;

        try
        {
            tintCount = Function.Call<int>(Hash.GET_WEAPON_TINT_COUNT, weaponHash);
        }
        catch
        {
        }

        int tint = Clamp(loadout.Tint, 0, Math.Max(0, tintCount - 1));

        try
        {
            Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX, ped.Handle, weaponHash, tint);
        }
        catch
        {
        }
    }

    private bool TryGiveFirstCompatibleComponent(Ped ped, WeaponHash weapon, IEnumerable<string> componentNames)
    {
        foreach (string componentName in componentNames)
        {
            if (TryGiveWeaponComponent(ped, weapon, componentName))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGiveWeaponComponent(Ped ped, WeaponHash weapon, string componentEnumName)
    {
        if (!Entity.Exists(ped) || string.IsNullOrWhiteSpace(componentEnumName))
        {
            return false;
        }

        int componentHash;

        if (!TryGetWeaponComponentHash(componentEnumName, out componentHash))
        {
            return false;
        }

        int weaponHash = EnumToIntHash(weapon);

        try
        {
            bool compatible = Function.Call<bool>(
                Hash.DOES_WEAPON_TAKE_WEAPON_COMPONENT,
                weaponHash,
                componentHash);

            if (!compatible)
            {
                return false;
            }

            Function.Call(
                Hash.GIVE_WEAPON_COMPONENT_TO_PED,
                ped.Handle,
                weaponHash,
                componentHash);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWeaponComponentHash(string enumName, out int hash)
    {
        hash = 0;

        try
        {
            Type type = GetWeaponComponentHashType();

            if (type == null)
            {
                return false;
            }

            object value = Enum.Parse(type, enumName, false);
            hash = EnumToIntHash((Enum)value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Type GetWeaponComponentHashType()
    {
        if (_weaponComponentHashType != null)
        {
            return _weaponComponentHashType;
        }

        Assembly gtaAssembly = typeof(WeaponHash).Assembly;
        _weaponComponentHashType = gtaAssembly.GetType("GTA.WeaponComponentHash", false);

        return _weaponComponentHashType;
    }

    private static IEnumerable<string> ExtendedClipCandidates(string weaponName)
    {
        return new[]
        {
            weaponName + "Clip03",
            weaponName + "Clip02",
            weaponName + "Clip01"
        };
    }

    private static IEnumerable<string> Mk2AmmoComponentCandidates(string weaponName, WeaponMk2AmmoMode ammoMode)
    {
        string suffix;

        switch (ammoMode)
        {
            case WeaponMk2AmmoMode.Tracer:
                suffix = "ClipTracer";
                break;

            case WeaponMk2AmmoMode.Incendiary:
                suffix = "ClipIncendiary";
                break;

            case WeaponMk2AmmoMode.ArmorPiercing:
                suffix = "ClipArmorPiercing";
                break;

            case WeaponMk2AmmoMode.FMJ:
                suffix = "ClipFMJ";
                break;

            case WeaponMk2AmmoMode.Explosive:
                suffix = "ClipExplosive";
                break;

            default:
                suffix = "Clip01";
                break;
        }

        return new[]
        {
            weaponName + suffix,
            weaponName + "Clip02",
            weaponName + "Clip01"
        };
    }

    private static IEnumerable<string> ScopeCandidates(WeaponScopeMode scope)
    {
        switch (scope)
        {
            case WeaponScopeMode.Small:
                return new[]
                {
                    "AtSights", "AtSightsSMG",
                    "AtScopeSmall", "AtScopeSmall02", "AtScopeSmallMk2", "AtScopeSmallSMGMk2",
                    "AtScopeMacro", "AtScopeMacro02", "AtScopeMacroMk2", "AtScopeMacro02SMGMk2"
                };

            case WeaponScopeMode.Medium:
                return new[]
                {
                    "AtScopeMedium", "AtScopeMediumMk2",
                    "AtScopeSmall", "AtScopeSmall02", "AtScopeMacro", "AtScopeMacro02"
                };

            case WeaponScopeMode.Large:
                return new[]
                {
                    "AtScopeLarge", "AtScopeLargeMk2", "AtScopeLargeFixedZoom",
                    "AtScopeLargeFixedZoomMk2", "AtScopeMax", "AtScopeNV", "AtScopeThermal",
                    "AtScopeMedium", "AtScopeMediumMk2"
                };

            default:
                return new string[0];
        }
    }

    private void InitializeRelationshipGroups()
    {
        _hostileGroupHash = World.AddRelationshipGroup("DONJ_ENEMY_HOSTILE");
        _neutralGroupHash = World.AddRelationshipGroup("DONJ_ENEMY_NEUTRAL");
        _allyGroupHash = World.AddRelationshipGroup("DONJ_PLAYER_ALLY");

        _lastKnownPlayerGroupHash = GetPlayerRelationshipGroup();

        ApplyRelationshipRules();
    }

    private void EnsureRelationshipGroups()
    {
        if (_hostileGroupHash == 0)
        {
            _hostileGroupHash = World.AddRelationshipGroup("DONJ_ENEMY_HOSTILE");
        }

        if (_neutralGroupHash == 0)
        {
            _neutralGroupHash = World.AddRelationshipGroup("DONJ_ENEMY_NEUTRAL");
        }

        if (_allyGroupHash == 0)
        {
            _allyGroupHash = World.AddRelationshipGroup("DONJ_PLAYER_ALLY");
        }

        int playerGroup = GetPlayerRelationshipGroup();

        if (playerGroup != 0 && playerGroup != _lastKnownPlayerGroupHash)
        {
            _lastKnownPlayerGroupHash = playerGroup;
        }

        ApplyRelationshipRules();
    }

    private void RefreshPlayerRelationshipIfNeeded()
    {
        if (Game.GameTime < _nextRelationshipRefreshAt)
        {
            return;
        }

        _nextRelationshipRefreshAt = Game.GameTime + 3000;
        EnsureRelationshipGroups();
    }

    private int GetPlayerRelationshipGroup()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return 0;
        }

        return Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, player.Handle);
    }

    private int GetPedRelationshipGroup(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return 0;
        }

        return Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
    }

    private void ApplyRelationshipRules()
    {
        if (_hostileGroupHash == 0 || _neutralGroupHash == 0 || _allyGroupHash == 0 || _lastKnownPlayerGroupHash == 0)
        {
            return;
        }

        SetRelationshipBothWays((Relationship)RelationshipCompanion, _hostileGroupHash, _hostileGroupHash);
        SetRelationshipBothWays((Relationship)RelationshipCompanion, _neutralGroupHash, _neutralGroupHash);
        SetRelationshipBothWays((Relationship)RelationshipCompanion, _hostileGroupHash, _neutralGroupHash);

        SetRelationshipBothWays((Relationship)RelationshipCompanion, _allyGroupHash, _allyGroupHash);
        SetRelationshipBothWays((Relationship)RelationshipCompanion, _allyGroupHash, _lastKnownPlayerGroupHash);

        SetRelationshipBothWays((Relationship)RelationshipHate, _hostileGroupHash, _lastKnownPlayerGroupHash);
        SetRelationshipBothWays((Relationship)RelationshipHate, _hostileGroupHash, _allyGroupHash);

        SetRelationshipBothWays((Relationship)RelationshipNeutral, _neutralGroupHash, _lastKnownPlayerGroupHash);
        SetRelationshipBothWays((Relationship)RelationshipNeutral, _neutralGroupHash, _allyGroupHash);

        /*
         * Je neutralise defensivement les groupes ambiants proteges pour eviter
         * qu'un ancien combat laisse une haine globale durable contre eux.
         */
        ResetAllyRelationsWithProtectedAmbientGroups();
    }

    private static void SetRelationshipBothWays(Relationship relationship, int groupA, int groupB)
    {
        World.SetRelationshipBetweenGroups(relationship, groupA, groupB);
        World.SetRelationshipBetweenGroups(relationship, groupB, groupA);
    }

    private static HashSet<int> BuildProtectedAmbientRelationshipGroups()
    {
        HashSet<int> groups = new HashSet<int>();

        string[] names = new[]
        {
            "CIVMALE",
            "CIVFEMALE",
            "FIREMAN",
            "MEDIC",
            "COP",
            "SECURITY_GUARD",
            "PRIVATE_SECURITY",
            "ARMY",
            "PLAYER",
            "PLAYER_1",
            "PLAYER_2",
            "PLAYER_3",
            "PLAYER_4",
            "PLAYER_5",
            "PLAYER_6",
            "PLAYER_7",
            "PLAYER_8",
            "NO_RELATIONSHIP"
        };

        for (int i = 0; i < names.Length; i++)
        {
            AddRelationshipGroupHash(groups, names[i]);
        }

        return groups;
    }

    private static void AddRelationshipGroupHash(HashSet<int> groups, string name)
    {
        if (groups == null || string.IsNullOrEmpty(name))
        {
            return;
        }

        try
        {
            int hash = Game.GenerateHash(name);

            if (hash != 0)
            {
                groups.Add(hash);
            }
        }
        catch
        {
            /*
             * Je ne laisse jamais un groupe manquant casser le mod.
             */
        }
    }

    private void ResetAllyRelationsWithProtectedAmbientGroups()
    {
        if (_allyGroupHash == 0 || ProtectedAmbientRelationshipGroups == null)
        {
            return;
        }

        foreach (int protectedGroup in ProtectedAmbientRelationshipGroups)
        {
            if (protectedGroup == 0 ||
                protectedGroup == _allyGroupHash ||
                protectedGroup == _hostileGroupHash ||
                protectedGroup == _neutralGroupHash ||
                protectedGroup == _lastKnownPlayerGroupHash)
            {
                continue;
            }

            try
            {
                SetRelationshipBothWays((Relationship)RelationshipNeutral, _allyGroupHash, protectedGroup);
            }
            catch
            {
                /*
                 * Je garde cet echec sans impact parce qu'un groupe absent
                 * ou non initialise ne doit jamais casser le mod.
                 */
            }
        }
    }

    private bool IsProtectedAmbientRelationshipGroup(int group)
    {
        if (group == 0 || group == _allyGroupHash || group == _neutralGroupHash || group == _lastKnownPlayerGroupHash)
        {
            return true;
        }

        return ProtectedAmbientRelationshipGroups != null && ProtectedAmbientRelationshipGroups.Contains(group);
    }

    private bool ShouldUseGroupHostilityForThreat(Ped threat, int targetGroup)
    {
        if (!Entity.Exists(threat) || targetGroup == 0 || targetGroup == _allyGroupHash)
        {
            return false;
        }

        if (targetGroup == _hostileGroupHash)
        {
            return true;
        }

        if (IsProtectedAmbientRelationshipGroup(targetGroup))
        {
            return false;
        }

        return false;
    }

    private bool HasHostileRelationshipToProtectedPed(Ped candidate, Ped protectedPed)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(protectedPed))
        {
            return false;
        }

        try
        {
            int relationship = (int)candidate.GetRelationshipWithPed(protectedPed);
            return relationship == RelationshipHate || relationship == RelationshipDislike;
        }
        catch
        {
            return false;
        }
    }

    private bool HasDefensiveDamageAgainstProtectedPed(Ped candidate, Ped protectedPed)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(protectedPed))
        {
            return false;
        }

        if (!protectedPed.HasBeenDamagedBy(candidate))
        {
            return false;
        }

        int group = GetPedRelationshipGroup(candidate);

        if (!IsProtectedAmbientRelationshipGroup(group))
        {
            return true;
        }

        if (IsPedInCombatWith(candidate, protectedPed))
        {
            return true;
        }

        if (HasHostileRelationshipToProtectedPed(candidate, protectedPed))
        {
            return true;
        }

        return IsPedShooting(candidate) &&
               candidate.Position.DistanceTo(protectedPed.Position) <= AllyShotThreatDistance;
    }

    private bool HasHostileRelationshipToAnyManagedAlly(Ped candidate)
    {
        if (!Entity.Exists(candidate))
        {
            return false;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped) ||
                ally.Ped.IsDead)
            {
                continue;
            }

            if (HasHostileRelationshipToProtectedPed(candidate, ally.Ped))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateNpcs()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return;
        }

        int now = Game.GameTime;
        int brainsBudget = MaxNpcBrainsPerTick;
        int blipBudget = MaxNpcBlipRefreshPerTick;

        for (int i = _spawnedNpcs.Count - 1; i >= 0; i--)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc == null)
            {
                _spawnedNpcs.RemoveAt(i);
                continue;
            }

            if (npc.RespawnPending)
            {
                TryProcessNpcAutoRespawn(npc, player);
                continue;
            }

            if (!Entity.Exists(npc.Ped))
            {
                RemoveNpcBlip(npc);

                if (npc.AutoRespawn)
                {
                    MarkNpcForAutoRespawn(npc);
                    TryProcessNpcAutoRespawn(npc, player);
                }
                else
                {
                    _spawnedNpcs.RemoveAt(i);
                }

                continue;
            }

            if (npc.Ped.IsDead)
            {
                HandleDeadNpc(npc, player);
                RemoveNpcBlip(npc);

                if (npc.AutoRespawn)
                {
                    MarkNpcForAutoRespawn(npc);
                }

                continue;
            }

            RefreshNpcBlipIfNeeded(npc, now, ref blipBudget);

            if (JusticeIsCustodyActive && IsJusticeOwnedAlly(npc.Ped))
            {
                // Je ne donne aucun nouvel ordre pendant la détention. Les alliés
                // restent dans le monde, sans immobilisation ni ordre de retraite,
                // puis leur IA normale reprend à la libération ou après l'évasion.
                continue;
            }

            /*
             * Les vagues ennemies appelées au téléphone ont leur propre couche IA :
             * conduite véhicule, drive-by, descente véhicule, combat à pied.
             * On évite donc que le comportement Attacker générique remplace
             * les ordres de conduite sur la frame suivante.
             */
            if (_enemyRaidKnownNpcHandles.Contains(npc.Ped.Handle) ||
                IsHighSecurityEscortPedHandle(npc.Ped.Handle))
            {
                continue;
            }

            if (now < npc.NextThinkAt)
            {
                continue;
            }

            if (brainsBudget <= 0)
            {
                /*
                 * On ne repousse pas NextThinkAt : les PNJ restants seront
                 * traites sur les frames suivantes. Cela etale la charge sans
                 * rendre l'IA aveugle.
                 */
                continue;
            }

            brainsBudget--;
            npc.NextThinkAt = GetNextNpcThinkTime();

            switch (npc.Behavior)
            {
                case NpcBehavior.Attacker:
                    UpdateAttacker(npc, player);
                    break;

                case NpcBehavior.Static:
                    UpdateStatic(npc, player);
                    break;

                case NpcBehavior.Neutral:
                    UpdateNeutral(npc, player);
                    break;

                case NpcBehavior.Ally:
                    UpdateAlly(npc, player);
                    break;

                case NpcBehavior.Bodyguard:
                    UpdateBodyguard(npc, player);
                    break;

                case NpcBehavior.NeutralPatrol:
                    UpdateNeutralPatrol(npc, player);
                    break;

                case NpcBehavior.HostilePatrol:
                    UpdateHostilePatrol(npc, player);
                    break;

                case NpcBehavior.AllyPatrol:
                    UpdateAllyPatrol(npc, player);
                    break;
            }
        }
    }

    private int GetInitialNpcThinkTime()
    {
        return Game.GameTime + _random.Next(80, ThinkIntervalMs + NpcThinkJitterMs + 1);
    }

    private int GetNextNpcThinkTime()
    {
        return Game.GameTime + ThinkIntervalMs + _random.Next(0, NpcThinkJitterMs + 1);
    }

    private int GetInitialPassiveTaskTime()
    {
        return Game.GameTime + _random.Next(0, PassiveHoldRefreshMs + PassiveHoldJitterMs + 1);
    }

    private int GetNextPassiveTaskTime()
    {
        return Game.GameTime + PassiveHoldRefreshMs + _random.Next(0, PassiveHoldJitterMs + 1);
    }

    private int GetInitialNpcBlipRefreshTime()
    {
        return Game.GameTime + _random.Next(0, NpcBlipRefreshIntervalMs + NpcBlipRefreshJitterMs + 1);
    }

    private int GetNextNpcBlipRefreshTime()
    {
        return Game.GameTime + NpcBlipRefreshIntervalMs + _random.Next(0, NpcBlipRefreshJitterMs + 1);
    }

    private void RefreshNpcBlipIfNeeded(SpawnedNpc npc, int now, ref int blipBudget)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            RemoveNpcBlip(npc);
            return;
        }

        bool missingBlip = npc.Blip == null || !npc.Blip.Exists();

        if (!missingBlip && now < npc.NextBlipRefreshAt)
        {
            return;
        }

        if (!missingBlip && blipBudget <= 0)
        {
            return;
        }

        if (!missingBlip)
        {
            blipBudget--;
        }

        npc.NextBlipRefreshAt = GetNextNpcBlipRefreshTime();
        CreateOrUpdateNpcBlip(npc);
    }

    private void MarkNpcForAutoRespawn(SpawnedNpc npc)
    {
        if (npc == null || !npc.AutoRespawn || npc.RespawnPending)
        {
            return;
        }

        npc.RespawnPending = true;
        npc.RespawnEligibleAt = Game.GameTime + AutoRespawnMinDelayMs;
        npc.NextRespawnCheckAt = Game.GameTime + _random.Next(0, AutoRespawnCheckIntervalMs + 1);
        RemoveNpcBlip(npc);
    }

    private void MarkPlacedVehicleForAutoRespawn(PlacedVehicle placed)
    {
        if (placed == null || !placed.AutoRespawn || placed.RespawnPending)
        {
            return;
        }

        placed.RespawnPending = true;
        placed.RespawnEligibleAt = Game.GameTime + AutoRespawnMinDelayMs;
        placed.NextRespawnCheckAt = Game.GameTime + _random.Next(0, AutoRespawnCheckIntervalMs + 1);
        RemovePlacedVehicleBlip(placed);
    }

    private void MarkPlacedObjectForAutoRespawn(PlacedObject placed)
    {
        if (placed == null || !placed.AutoRespawn || placed.RespawnPending)
        {
            return;
        }

        placed.RespawnPending = true;
        placed.RespawnEligibleAt = Game.GameTime + AutoRespawnMinDelayMs;
        placed.NextRespawnCheckAt = Game.GameTime + _random.Next(0, AutoRespawnCheckIntervalMs + 1);
    }

    private bool TryProcessNpcAutoRespawn(SpawnedNpc npc, Ped player)
    {
        if (npc == null || !npc.AutoRespawn || !npc.RespawnPending)
        {
            return false;
        }

        if (Game.GameTime < npc.NextRespawnCheckAt)
        {
            return false;
        }

        npc.NextRespawnCheckAt = Game.GameTime + AutoRespawnCheckIntervalMs;

        if (!CanAutoRespawnAt(player, npc.HomePosition, npc.Ped, npc.RespawnEligibleAt))
        {
            return false;
        }

        DeleteEntitySafe(npc.Ped);
        RemoveNpcBlip(npc);

        Ped ped = CreatePedFromModelIdentity(npc.ModelIdentity, npc.HomePosition, npc.HomeHeading);

        if (!Entity.Exists(ped))
        {
            npc.RespawnEligibleAt = Game.GameTime + AutoRespawnRetryDelayMs;
            return false;
        }

        npc.Ped = ped;
        ResetNpcRuntimeAfterAutoRespawn(npc);
        CreateOrUpdateNpcBlip(npc);
        StartNpcRuntimeBehavior(npc);
        _autoRespawnsThisTick++;
        return true;
    }

    private bool TryProcessPlacedVehicleAutoRespawn(PlacedVehicle placed, Ped player)
    {
        if (placed == null || !placed.AutoRespawn || !placed.RespawnPending)
        {
            return false;
        }

        if (Game.GameTime < placed.NextRespawnCheckAt)
        {
            return false;
        }

        placed.NextRespawnCheckAt = Game.GameTime + AutoRespawnCheckIntervalMs;

        if (!CanAutoRespawnAt(player, placed.RespawnPosition, placed.Vehicle, placed.RespawnEligibleAt))
        {
            return false;
        }

        DeleteEntitySafe(placed.Vehicle);
        RemovePlacedVehicleBlip(placed);

        Vehicle vehicle = CreateVehicleFromIdentity(placed.Identity, placed.RespawnPosition, placed.RespawnHeading);

        if (!Entity.Exists(vehicle))
        {
            placed.RespawnEligibleAt = Game.GameTime + AutoRespawnRetryDelayMs;
            return false;
        }

        ConfigurePlacedVehicleEntity(vehicle, placed.RespawnHeading);

        placed.Vehicle = vehicle;
        placed.Position = vehicle.Position;
        placed.Heading = NormalizeHeading(vehicle.Heading);
        placed.RespawnPending = false;
        placed.RespawnEligibleAt = 0;
        placed.NextRespawnCheckAt = 0;

        CreateOrUpdatePlacedVehicleBlip(placed);
        _autoRespawnsThisTick++;
        return true;
    }

    private bool TryProcessPlacedObjectAutoRespawn(PlacedObject placed, Ped player)
    {
        if (placed == null || !placed.AutoRespawn || !placed.RespawnPending)
        {
            return false;
        }

        if (Game.GameTime < placed.NextRespawnCheckAt)
        {
            return false;
        }

        placed.NextRespawnCheckAt = Game.GameTime + AutoRespawnCheckIntervalMs;

        if (!CanAutoRespawnAt(player, placed.RespawnPosition, placed.Prop, placed.RespawnEligibleAt))
        {
            return false;
        }

        DeleteEntitySafe(placed.Prop);

        Prop prop = CreatePropFromIdentity(placed.Identity, placed.RespawnPosition, placed.RespawnHeading);

        if (!Entity.Exists(prop))
        {
            placed.RespawnEligibleAt = Game.GameTime + AutoRespawnRetryDelayMs;
            return false;
        }

        ConfigurePlacedObjectEntity(prop, placed.RespawnPosition, placed.RespawnHeading);

        placed.Prop = prop;
        placed.Position = prop.Position;
        placed.Heading = NormalizeHeading(prop.Heading);
        placed.RespawnPending = false;
        placed.RespawnEligibleAt = 0;
        placed.NextRespawnCheckAt = 0;

        _autoRespawnsThisTick++;
        return true;
    }

    private void ResetNpcRuntimeAfterAutoRespawn(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped))
        {
            return;
        }

        WeaponLoadout loadout = npc.Loadout ?? _selectedWeaponLoadout.Clone();

        npc.Behavior = npc.BaseBehavior;
        npc.Activated = npc.BaseBehavior == NpcBehavior.Attacker || npc.BaseBehavior == NpcBehavior.HostilePatrol;
        npc.DeathAlerted = false;
        npc.NextThinkAt = GetInitialNpcThinkTime();
        npc.NextPassiveTaskAt = GetInitialPassiveTaskTime();
        npc.NextBlipRefreshAt = GetInitialNpcBlipRefreshTime();
        npc.LastCombatActivityAt = Game.GameTime;
        npc.IsReturningHome = false;
        npc.NextReturnTaskAt = 0;
        npc.PatrolCenter = npc.HomePosition;
        npc.PatrolTarget = Vector3.Zero;
        npc.NextPatrolTaskAt = 0;
        npc.NextBodyguardTaskAt = 0;
        npc.BodyguardAssignedVehicleHandle = 0;
        npc.BodyguardAssignedSeat = 999;
        npc.BodyguardIsDriver = false;
        npc.RespawnPending = false;
        npc.RespawnEligibleAt = 0;
        npc.NextRespawnCheckAt = 0;
        npc.Loadout = loadout;

        ConfigureSpawnedPed(npc.Ped, npc.BaseBehavior, npc.SavedMaxHealth, npc.SavedArmor, loadout);
        npc.Ped.Position = npc.HomePosition;
        npc.Ped.Heading = NormalizeHeading(npc.HomeHeading);
    }

    private bool CanAutoRespawnAt(Ped player, Vector3 spawnPosition, Entity oldEntity, int eligibleAt)
    {
        if (_autoRespawnsThisTick >= AutoRespawnMaxPerTick)
        {
            return false;
        }

        if (!Entity.Exists(player) || Game.GameTime < eligibleAt)
        {
            return false;
        }

        float distance = player.Position.DistanceTo(spawnPosition);

        if (distance < AutoRespawnNearSafetyDistance || distance < AutoRespawnLeaveDistance)
        {
            return false;
        }

        if (Entity.Exists(oldEntity) && IsEntityLikelyVisibleToPlayer(oldEntity))
        {
            return false;
        }

        if (IsPointInPlayerView(player, spawnPosition) && distance < AutoRespawnLeaveDistance + 80.0f)
        {
            return false;
        }

        return true;
    }

    private static bool IsPlacedVehicleDestroyed(PlacedVehicle placed)
    {
        if (placed == null || !Entity.Exists(placed.Vehicle))
        {
            return true;
        }

        try
        {
            if (placed.Vehicle.IsDead)
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (!placed.Vehicle.IsDriveable)
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (placed.Vehicle.EngineHealth <= -350.0f || placed.Vehicle.PetrolTankHealth <= -350.0f)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsPlacedObjectDestroyed(PlacedObject placed)
    {
        if (placed == null || !Entity.Exists(placed.Prop))
        {
            return true;
        }

        try
        {
            return placed.Prop.IsDead;
        }
        catch
        {
            return false;
        }
    }

    private void HandleDeadNpc(SpawnedNpc npc, Ped player)
    {
        if (npc.DeathAlerted)
        {
            return;
        }

        npc.DeathAlerted = true;

        if (IsNeutralBehavior(npc.BaseBehavior) && npc.Ped.HasBeenDamagedBy(player))
        {
            AlertNearbyNeutralGuards(npc.Ped.Position, player, npc.Ped);
            return;
        }

        if (IsAllyBehavior(npc.BaseBehavior))
        {
            Ped threat = FindDamagingPedForVictim(npc.Ped, npc.Ped.Position, RuntimeAllyDefenseRadius);

            if (Entity.Exists(threat))
            {
                AlertAlliesToThreat(threat, npc.Ped.Position);
            }
        }
    }

    private void UpdateAttacker(SpawnedNpc npc, Ped player)
    {
        if (npc.IsReturningHome)
        {
            if (HasGuardCombatActivity(npc, player))
            {
                CancelReturnHome(npc);
            }
            else
            {
                UpdateReturnHome(npc);
                return;
            }
        }

        if (ShouldReturnToPostAfterCalm(npc, player))
        {
            BeginReturnHome(npc);
            UpdateReturnHome(npc);
            return;
        }

        Ped target = FindHostileTargetForEnemy(npc.Ped, player);

        if (Entity.Exists(target))
        {
            if (HasCombatActivityWithTarget(npc.Ped, target, 160.0f))
            {
                MarkCombatActivity(npc);
            }

            ActivateCombatAgainstTarget(npc, target, false);
        }
        else if (IsPatrolBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            StartOrContinuePatrol(npc, false);
        }
    }

    private void UpdateStatic(SpawnedNpc npc, Ped player)
    {
        if (CanPedSeeEntity(npc.Ped, player, StaticSightDistance))
        {
            MarkCombatActivity(npc);
            ActivateCombatAgainstTarget(npc, player, true);
            return;
        }

        if (!npc.Activated)
        {
            HoldStaticPositionThrottled(npc, player);
        }
    }

    private void UpdateNeutral(SpawnedNpc npc, Ped player)
    {
        Vector3 eventPosition;
        Entity witnessedEntity;

        if (npc.IsReturningHome)
        {
            if (TryGetPlayerProvocationAgainstNeutralGuard(npc.Ped, player, out eventPosition, out witnessedEntity))
            {
                CancelReturnHome(npc);
                ConvertNeutralToHostile(npc, player);
                AlertNearbyNeutralGuards(eventPosition, player, witnessedEntity);
                return;
            }

            UpdateReturnHome(npc);
            return;
        }

        if (TryGetPlayerProvocationAgainstNeutralGuard(npc.Ped, player, out eventPosition, out witnessedEntity))
        {
            ConvertNeutralToHostile(npc, player);
            AlertNearbyNeutralGuards(eventPosition, player, witnessedEntity);
            return;
        }

        HoldGuardPositionThrottled(npc);
    }

    private void UpdateAlly(SpawnedNpc ally, Ped player)
    {
        if (ally.IsReturningHome)
        {
            Ped returningThreat = FindThreatForAlly(ally.Ped, player);

            if (Entity.Exists(returningThreat))
            {
                CancelReturnHome(ally);
                AlertAlliesToThreat(returningThreat, ally.Ped.Position);
                return;
            }

            UpdateReturnHome(ally);
            return;
        }

        Ped threat = FindThreatForAlly(ally.Ped, player);

        if (Entity.Exists(threat))
        {
            MarkCombatActivity(ally);
            AlertAlliesToThreat(threat, ally.Ped.Position);
            return;
        }

        if (ally.Activated && Game.GameTime - ally.LastCombatActivityAt >= GuardReturnDelayMs)
        {
            BeginReturnHome(ally);
            UpdateReturnHome(ally);
            return;
        }

        HoldAllyPositionThrottled(ally);
    }

    private void UpdateNeutralPatrol(SpawnedNpc npc, Ped player)
    {
        Vector3 eventPosition;
        Entity witnessedEntity;

        if (npc.IsReturningHome)
        {
            if (TryGetPlayerProvocationAgainstNeutralGuard(npc.Ped, player, out eventPosition, out witnessedEntity))
            {
                CancelReturnHome(npc);
                ConvertNeutralToHostile(npc, player);
                AlertNearbyNeutralGuards(eventPosition, player, witnessedEntity);
                return;
            }

            UpdateReturnHome(npc);
            return;
        }

        if (TryGetPlayerProvocationAgainstNeutralGuard(npc.Ped, player, out eventPosition, out witnessedEntity))
        {
            ConvertNeutralToHostile(npc, player);
            AlertNearbyNeutralGuards(eventPosition, player, witnessedEntity);
            return;
        }

        StartOrContinuePatrol(npc, false);
    }

    private void UpdateHostilePatrol(SpawnedNpc npc, Ped player)
    {
        if (npc.IsReturningHome)
        {
            if (HasGuardCombatActivity(npc, player))
            {
                CancelReturnHome(npc);
            }
            else
            {
                UpdateReturnHome(npc);
                return;
            }
        }

        Ped target = FindHostileTargetForEnemy(npc.Ped, player);

        if (Entity.Exists(target) && CanPedSeeEntity(npc.Ped, target, StaticSightDistance))
        {
            MarkCombatActivity(npc);
            ActivateCombatAgainstTarget(npc, target, false);
            return;
        }

        if (npc.Activated && Game.GameTime - npc.LastCombatActivityAt >= GuardReturnDelayMs)
        {
            BeginReturnHome(npc);
            UpdateReturnHome(npc);
            return;
        }

        StartOrContinuePatrol(npc, false);
    }

    private void UpdateAllyPatrol(SpawnedNpc ally, Ped player)
    {
        if (ally.IsReturningHome)
        {
            Ped returningThreat = FindThreatForAlly(ally.Ped, player);

            if (Entity.Exists(returningThreat))
            {
                CancelReturnHome(ally);
                AlertAlliesToThreat(returningThreat, ally.Ped.Position);
                return;
            }

            UpdateReturnHome(ally);
            return;
        }

        Ped threat = FindThreatForAlly(ally.Ped, player);

        if (Entity.Exists(threat))
        {
            MarkCombatActivity(ally);
            AlertAlliesToThreat(threat, ally.Ped.Position);
            return;
        }

        if (ally.Activated && Game.GameTime - ally.LastCombatActivityAt >= GuardReturnDelayMs)
        {
            BeginReturnHome(ally);
            UpdateReturnHome(ally);
            return;
        }

        StartOrContinuePatrol(ally, false);
    }

    private void UpdateBodyguard(SpawnedNpc bodyguard, Ped player)
    {
        if (bodyguard == null || !Entity.Exists(bodyguard.Ped) || !Entity.Exists(player))
        {
            return;
        }

        /*
         * Les gardes Cartel ont une couche IA dédiée plus bas dans le fichier
         * (UpdateCartelConvoyState / MaintainCartelTeamWeaponsAndDrivers).
         *
         * Important perf : avant, chaque garde Cartel passait aussi par
         * FindThreatForAlly(), qui lance des scans World.GetNearbyPeds autour
         * du joueur et autour du garde. Avec 11 gardes, cela créait une salve
         * de scans synchronisés toutes les ~700 ms, visible comme une micro
         * coupure régulière dès que l'équipe Cartel était présente.
         *
         * On court-circuite donc uniquement les gardes Cartel actifs ici :
         * ils restent entretenus légèrement, mais la détection de menace et les
         * ordres de convoi restent centralisés dans la couche Cartel optimisée.
         */
        if (_cartelNpcHandles.Contains(bodyguard.Ped.Handle))
        {
            UpdateCartelBodyguardFromGenericPass(bodyguard);
            return;
        }

        Ped threat = FindThreatForAlly(bodyguard.Ped, player);

        if (Entity.Exists(threat))
        {
            MarkCombatActivity(bodyguard);
            AlertAlliesToThreat(threat, bodyguard.Ped.Position);
            return;
        }

        /*
         * Je garde le bodyguard sous controle script quand il n'y a pas de
         * menace reelle, pour eviter qu'il parte regler des evenements ambiants.
         */
        bodyguard.Ped.IsEnemy = false;
        bodyguard.Ped.BlockPermanentEvents = true;
        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, bodyguard.Ped.Handle, _allyGroupHash);

        if (Game.GameTime < bodyguard.NextBodyguardTaskAt)
        {
            return;
        }

        bodyguard.NextBodyguardTaskAt = Game.GameTime + BodyguardRetaskMs;

        if (player.IsInVehicle())
        {
            ManageBodyguardVehicleFollow(bodyguard, player);
        }
        else
        {
            ManageBodyguardFootFollow(bodyguard, player);
        }
    }

    private void UpdateCartelBodyguardFromGenericPass(SpawnedNpc bodyguard)
    {
        if (bodyguard == null || !Entity.Exists(bodyguard.Ped) || bodyguard.Ped.IsDead)
        {
            return;
        }

        /*
         * Cette méthode est volontairement légère : elle ne scanne pas le monde,
         * ne cherche pas de véhicule, et ne renvoie pas d'ordre de conduite.
         * Le Cartel possède déjà sa propre boucle centralisée pour ça.
         */
        bodyguard.BaseBehavior = NpcBehavior.Bodyguard;
        bodyguard.Behavior = NpcBehavior.Bodyguard;
        bodyguard.Activated = false;
        bodyguard.IsReturningHome = false;

        if (!ShouldRunCartelGuardPassiveMaintenance(bodyguard.Ped, false))
        {
            return;
        }

        MaintainCartelGuardPassiveState(bodyguard, false);
    }

    private void UpdatePlayerHostilityMemory(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead || _placementMode)
        {
            return;
        }

        int now = Game.GameTime;

        /*
         * Le tir est volontairement teste a chaque tick: avec un intervalle de
         * reflexion NPC de 700 ms, un coup de feu unique peut sinon etre rate.
         */
        if (IsPedShooting(player))
        {
            _lastPlayerGunfireAt = now;
            _lastPlayerGunfirePosition = player.Position;

            Vector3 impactPosition;

            if (TryGetLastWeaponImpactPosition(player, out impactPosition))
            {
                _lastPlayerGunfireImpactAt = now;
                _lastPlayerGunfireImpactPosition = impactPosition;
            }
        }

        if (now < _nextPlayerHostilityScanAt)
        {
            return;
        }

        _nextPlayerHostilityScanAt = now + PlayerHostilityScanIntervalMs;

        if (IsPedInMeleeCombatSafe(player))
        {
            _lastPlayerMeleeHostilityAt = now;
            _lastPlayerMeleeHostilityPosition = player.Position;
        }
    }

    private bool TryGetPlayerProvocationAgainstNeutralGuard(Ped guard, Ped player, out Vector3 eventPosition, out Entity witnessedEntity)
    {
        eventPosition = Entity.Exists(guard) ? guard.Position : Vector3.Zero;
        witnessedEntity = guard;

        if (!Entity.Exists(guard) || !Entity.Exists(player))
        {
            return false;
        }

        if (guard.HasBeenDamagedBy(player) || HasEntityBeenDamagedByEntitySafe(guard, player))
        {
            eventPosition = guard.Position;
            witnessedEntity = guard;
            return true;
        }

        if (HasRecentPlayerGunfireNearGuard(guard, player, out eventPosition))
        {
            witnessedEntity = player;
            return true;
        }

        if (DidPlayerAttackSomeoneOrSomethingNearGuard(guard, player, out eventPosition, out witnessedEntity))
        {
            return true;
        }

        if (HasRecentPlayerMeleeNearGuard(guard, player, out eventPosition))
        {
            witnessedEntity = player;
            return true;
        }

        bool freeAiming = Function.Call<bool>(
            Hash.IS_PLAYER_FREE_AIMING_AT_ENTITY,
            Game.Player.Handle,
            guard.Handle);

        if (freeAiming)
        {
            eventPosition = guard.Position;
            witnessedEntity = guard;
            return true;
        }

        bool targeting = Function.Call<bool>(
            Hash.IS_PLAYER_TARGETTING_ENTITY,
            Game.Player.Handle,
            guard.Handle);

        if (targeting)
        {
            eventPosition = guard.Position;
            witnessedEntity = guard;
            return true;
        }

        return false;
    }

    private bool HasPlayerProvokedNeutralGuard(Ped guard, Ped player)
    {
        Vector3 eventPosition;
        Entity witnessedEntity;

        return TryGetPlayerProvocationAgainstNeutralGuard(guard, player, out eventPosition, out witnessedEntity);
    }

    private bool HasRecentPlayerGunfireNearGuard(Ped guard, Ped player, out Vector3 eventPosition)
    {
        eventPosition = Entity.Exists(guard) ? guard.Position : Vector3.Zero;

        if (!Entity.Exists(guard) || !Entity.Exists(player))
        {
            return false;
        }

        if (IsPlayerBulletCurrentlyNearPed(guard, NeutralBulletWhizReactionDistance))
        {
            eventPosition = guard.Position;
            return true;
        }

        if (!HasRecentGameTime(_lastPlayerGunfireAt, PlayerHostilityMemoryMs))
        {
            return false;
        }

        if (!IsZeroVector(_lastPlayerGunfirePosition) &&
            guard.Position.DistanceTo(_lastPlayerGunfirePosition) <= NeutralShotReactionDistance)
        {
            eventPosition = _lastPlayerGunfirePosition;
            return true;
        }

        if (HasRecentGameTime(_lastPlayerGunfireImpactAt, PlayerHostilityMemoryMs) &&
            !IsZeroVector(_lastPlayerGunfireImpactPosition) &&
            guard.Position.DistanceTo(_lastPlayerGunfireImpactPosition) <= NeutralBulletImpactReactionDistance)
        {
            eventPosition = _lastPlayerGunfireImpactPosition;
            return true;
        }

        return false;
    }

    private bool HasRecentPlayerMeleeNearGuard(Ped guard, Ped player, out Vector3 eventPosition)
    {
        eventPosition = Entity.Exists(player) ? player.Position : Vector3.Zero;

        if (!Entity.Exists(guard) || !Entity.Exists(player))
        {
            return false;
        }

        if (!HasRecentGameTime(_lastPlayerMeleeHostilityAt, PlayerHostilityMemoryMs))
        {
            return false;
        }

        Vector3 hostilityPosition = IsZeroVector(_lastPlayerMeleeHostilityPosition)
            ? player.Position
            : _lastPlayerMeleeHostilityPosition;

        if (guard.Position.DistanceTo(hostilityPosition) > NeutralMeleeReactionDistance)
        {
            return false;
        }

        if (guard.Position.DistanceTo(player.Position) <= 16.0f ||
            CanPedSeeEntity(guard, player, NeutralWitnessDistance))
        {
            eventPosition = hostilityPosition;
            return true;
        }

        return false;
    }

    private bool DidPlayerAttackSomeoneOrSomethingNearGuard(Ped guard, Ped player)
    {
        Vector3 eventPosition;
        Entity witnessedEntity;

        return DidPlayerAttackSomeoneOrSomethingNearGuard(guard, player, out eventPosition, out witnessedEntity);
    }

    private bool DidPlayerAttackSomeoneOrSomethingNearGuard(Ped guard, Ped player, out Vector3 eventPosition, out Entity witnessedEntity)
    {
        eventPosition = Entity.Exists(guard) ? guard.Position : Vector3.Zero;
        witnessedEntity = null;

        if (!Entity.Exists(guard) || !Entity.Exists(player))
        {
            return false;
        }

        bool recentGunOrMelee =
            HasRecentGameTime(_lastPlayerGunfireAt, PlayerHostilityMemoryMs) ||
            HasRecentGameTime(_lastPlayerMeleeHostilityAt, PlayerHostilityMemoryMs);

        Ped[] nearbyPeds = GetNearbyPedsSafe(guard, NeutralNearbyAttackReactionDistance);

        for (int i = 0; i < nearbyPeds.Length; i++)
        {
            Ped candidate = nearbyPeds[i];

            if (!Entity.Exists(candidate) || candidate.IsDead)
            {
                continue;
            }

            if (candidate.Handle == guard.Handle || candidate.Handle == player.Handle)
            {
                continue;
            }

            if (Entity.Exists(_placementPreviewPed) && candidate.Handle == _placementPreviewPed.Handle)
            {
                continue;
            }

            bool damagedByPlayer = recentGunOrMelee &&
                                   (candidate.HasBeenDamagedBy(player) || HasEntityBeenDamagedByEntitySafe(candidate, player));
            bool hitByPlayerVehicle = IsPlayerVehicleAggressionAgainstEntity(player, candidate);

            if (!damagedByPlayer && !hitByPlayerVehicle)
            {
                continue;
            }

            if (!CanGuardWitnessAttackOnEntity(guard, candidate, player))
            {
                continue;
            }

            eventPosition = candidate.Position;
            witnessedEntity = candidate;
            return true;
        }

        Vehicle currentPlayerVehicle = player.IsInVehicle() ? player.CurrentVehicle : null;
        Vehicle[] nearbyVehicles = GetNearbyVehiclesSafe(guard, NeutralNearbyVehicleAttackReactionDistance);

        for (int i = 0; i < nearbyVehicles.Length; i++)
        {
            Vehicle vehicle = nearbyVehicles[i];

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            if (Entity.Exists(currentPlayerVehicle) && vehicle.Handle == currentPlayerVehicle.Handle)
            {
                continue;
            }

            bool damagedByPlayer = recentGunOrMelee && HasEntityBeenDamagedByEntitySafe(vehicle, player);
            bool hitByPlayerVehicle = IsPlayerVehicleAggressionAgainstEntity(player, vehicle);

            if (!damagedByPlayer && !hitByPlayerVehicle)
            {
                continue;
            }

            if (!CanGuardWitnessAttackOnEntity(guard, vehicle, player))
            {
                continue;
            }

            eventPosition = vehicle.Position;
            witnessedEntity = vehicle;
            return true;
        }

        return false;
    }

    private bool CanGuardWitnessAttackOnEntity(Ped guard, Entity witnessedEntity, Ped player)
    {
        if (!Entity.Exists(guard) || !Entity.Exists(witnessedEntity))
        {
            return false;
        }

        float distanceToVictim = guard.Position.DistanceTo(witnessedEntity.Position);

        if (distanceToVictim <= 20.0f)
        {
            return true;
        }

        if (CanPedSeeEntity(guard, witnessedEntity, NeutralWitnessDistance))
        {
            return true;
        }

        return Entity.Exists(player) &&
               distanceToVictim <= 35.0f &&
               CanPedSeeEntity(guard, player, NeutralWitnessDistance);
    }

    private bool IsPlayerBulletCurrentlyNearPed(Ped ped, float radius)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(
                Hash.IS_BULLET_IN_AREA,
                ped.Position.X,
                ped.Position.Y,
                ped.Position.Z,
                radius,
                true);
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetLastWeaponImpactPosition(Ped ped, out Vector3 impactPosition)
    {
        impactPosition = Vector3.Zero;

        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            OutputArgument outPosition = new OutputArgument();

            bool found = Function.Call<bool>(
                Hash.GET_PED_LAST_WEAPON_IMPACT_COORD,
                ped.Handle,
                outPosition);

            if (!found)
            {
                return false;
            }

            impactPosition = outPosition.GetResult<Vector3>();
            return !IsZeroVector(impactPosition);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPedInMeleeCombatSafe(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, ped.Handle);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasEntityBeenDamagedByEntitySafe(Entity victim, Entity attacker)
    {
        if (!Entity.Exists(victim) || !Entity.Exists(attacker))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(
                Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY,
                victim.Handle,
                attacker.Handle,
                true);
        }
        catch
        {
            return false;
        }
    }

    private bool IsPlayerVehicleAggressionAgainstEntity(Ped player, Entity victim)
    {
        if (!Entity.Exists(player) || !Entity.Exists(victim) || !player.IsInVehicle())
        {
            return false;
        }

        Vehicle playerVehicle = player.CurrentVehicle;

        if (!Entity.Exists(playerVehicle) || playerVehicle.Handle == victim.Handle)
        {
            return false;
        }

        if (playerVehicle.Speed < PlayerVehicleHostilityMinSpeed)
        {
            return false;
        }

        if (!HasEntityBeenDamagedByEntitySafe(victim, playerVehicle))
        {
            return false;
        }

        return playerVehicle.Position.DistanceTo(victim.Position) <= 12.0f ||
               AreEntitiesTouching(playerVehicle, victim);
    }

    private static bool AreEntitiesTouching(Entity first, Entity second)
    {
        if (!Entity.Exists(first) || !Entity.Exists(second))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(Hash.IS_ENTITY_TOUCHING_ENTITY, first.Handle, second.Handle);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasRecentGameTime(int timestamp, int memoryMs)
    {
        return timestamp > 0 && Game.GameTime - timestamp <= memoryMs;
    }

    private void AlertNearbyNeutralGuards(Vector3 eventPosition, Ped player, Entity witnessedEntity)
    {
        int converted = 0;

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc candidate = _spawnedNpcs[i];

            if (candidate == null ||
                !IsNeutralBehavior(candidate.BaseBehavior) ||
                !Entity.Exists(candidate.Ped) ||
                candidate.Ped.IsDead)
            {
                continue;
            }

            if (!IsNeutralBehavior(candidate.Behavior) && candidate.Behavior != NpcBehavior.Attacker)
            {
                continue;
            }

            float distanceToEvent = candidate.Ped.Position.DistanceTo(eventPosition);

            if (distanceToEvent > RuntimeNeutralAssistRadius)
            {
                continue;
            }

            bool isDirectVictim =
                Entity.Exists(witnessedEntity) &&
                candidate.Ped.Handle == witnessedEntity.Handle;

            Vector3 heardShotPosition;
            bool heardShots =
                Entity.Exists(player) &&
                HasRecentPlayerGunfireNearGuard(candidate.Ped, player, out heardShotPosition);

            bool sawPlayer = CanPedSeeEntity(candidate.Ped, player, NeutralWitnessDistance);
            bool sawVictim = Entity.Exists(witnessedEntity) &&
                             CanPedSeeEntity(candidate.Ped, witnessedEntity, NeutralWitnessDistance);

            if (!isDirectVictim && !heardShots && !sawPlayer && !sawVictim)
            {
                continue;
            }

            ConvertNeutralToHostile(candidate, player);
            converted++;
        }

        if (converted > 1)
        {
            ShowStatus(converted.ToString(CultureInfo.InvariantCulture) + " gardes neutres passent hostiles.", 3500);
        }
    }

    private void ConvertNeutralToHostile(SpawnedNpc npc, Ped player)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        npc.Behavior = NpcBehavior.Attacker;
        npc.Activated = true;
        npc.IsReturningHome = false;
        npc.LastCombatActivityAt = Game.GameTime;

        npc.Ped.BlockPermanentEvents = false;
        npc.Ped.IsEnemy = true;

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _hostileGroupHash);

        CreateOrUpdateNpcBlip(npc);
        ActivateCombatAgainstBestTarget(npc, false);
    }

    private Ped FindThreatForAlly(Ped allyPed, Ped player)
    {
        if (!Entity.Exists(allyPed) || !Entity.Exists(player))
        {
            return null;
        }

        /*
         * Les menaces creees par notre mod sont peu couteuses a verifier et
         * doivent rester reactives. Cette partie ne lance pas de scan monde.
         */
        Ped managedThreat = FindManagedHostileThreatForAlly(allyPed, player);

        if (Entity.Exists(managedThreat))
        {
            CacheAllyThreat(managedThreat);
            return managedThreat;
        }

        Ped cachedThreat = GetUsableCachedAllyThreat(allyPed, player);

        if (Entity.Exists(cachedThreat))
        {
            return cachedThreat;
        }

        int now = Game.GameTime;

        if (now < _nextAllyThreatScanAt)
        {
            return null;
        }

        _nextAllyThreatScanAt = now + AllyThreatScanIntervalMs;

        Ped ambientThreat = FindBestAmbientThreatForAllies(player);

        if (Entity.Exists(ambientThreat))
        {
            CacheAllyThreat(ambientThreat);

            if (IsAllyThreatUsableFor(ambientThreat, allyPed, player))
            {
                return ambientThreat;
            }
        }

        return null;
    }

    private void CacheAllyThreat(Ped threat)
    {
        if (!Entity.Exists(threat) || threat.IsDead)
        {
            _allyCachedThreatPed = null;
            _allyCachedThreatUntil = 0;
            return;
        }

        _allyCachedThreatPed = threat;
        _allyCachedThreatUntil = Game.GameTime + AllyThreatCacheLifetimeMs;
    }

    private Ped GetUsableCachedAllyThreat(Ped allyPed, Ped player)
    {
        if (Game.GameTime > _allyCachedThreatUntil)
        {
            _allyCachedThreatPed = null;
            _allyCachedThreatUntil = 0;
            return null;
        }

        if (!IsAllyThreatUsableFor(_allyCachedThreatPed, allyPed, player))
        {
            return null;
        }

        return _allyCachedThreatPed;
    }

    private bool IsAllyThreatUsableFor(Ped threat, Ped allyPed, Ped player)
    {
        if (!IsValidThreatCandidateForAlly(threat, player))
        {
            return false;
        }

        if (Entity.Exists(player) && threat.Position.DistanceTo(player.Position) <= RuntimeAllyDefenseRadius)
        {
            return true;
        }

        return Entity.Exists(allyPed) && threat.Position.DistanceTo(allyPed.Position) <= RuntimeAllyDefenseRadius;
    }

    private Ped FindBestAmbientThreatForAllies(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return null;
        }

        Ped best = null;
        float bestScore = float.MaxValue;

        Ped[] nearPlayer = GetNearbyPedsSafe(player, RuntimeAllyDefenseRadius);

        for (int i = 0; i < nearPlayer.Length; i++)
        {
            TryPromoteAllyThreatCandidate(nearPlayer[i], player, player, player, ref best, ref bestScore);
        }

        List<SpawnedNpc> allies = CollectAlliesForThreatScan();

        if (allies.Count > 0)
        {
            int scansThisPass = Math.Min(AllyThreatGuardScansPerPass, allies.Count);

            for (int scan = 0; scan < scansThisPass; scan++)
            {
                int index = Wrap(_allyThreatScanCursor + scan, allies.Count);
                SpawnedNpc ally = allies[index];

                if (ally == null || !Entity.Exists(ally.Ped) || ally.Ped.IsDead)
                {
                    continue;
                }

                Ped[] nearAlly = GetNearbyPedsSafe(ally.Ped, RuntimeAllyDefenseRadius);

                for (int j = 0; j < nearAlly.Length; j++)
                {
                    TryPromoteAllyThreatCandidate(nearAlly[j], ally.Ped, player, ally.Ped, ref best, ref bestScore);
                }
            }

            _allyThreatScanCursor = Wrap(_allyThreatScanCursor + scansThisPass, allies.Count);
        }

        return best;
    }

    private List<SpawnedNpc> CollectAlliesForThreatScan()
    {
        List<SpawnedNpc> allies = new List<SpawnedNpc>();

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc == null ||
                !IsAllyBehavior(npc.BaseBehavior) ||
                !Entity.Exists(npc.Ped) ||
                npc.Ped.IsDead)
            {
                continue;
            }

            allies.Add(npc);
        }

        return allies;
    }

    private void TryPromoteAllyThreatCandidate(Ped candidate, Ped protectedPed, Ped player, Ped witness, ref Ped best, ref float bestScore)
    {
        if (!IsValidThreatCandidateForAlly(candidate, player) || !Entity.Exists(protectedPed))
        {
            return;
        }

        bool evidence =
            HasDefensiveDamageAgainstProtectedPed(candidate, protectedPed) ||
            IsPedInCombatWith(candidate, protectedPed);

        if (!evidence && IsPedShooting(candidate))
        {
            bool closeToProtectedPed = candidate.Position.DistanceTo(protectedPed.Position) <= AllyShotThreatDistance;
            bool hostileRelation =
                HasHostileRelationshipToProtectedPed(candidate, protectedPed) ||
                (Entity.Exists(player) && HasHostileRelationshipToProtectedPed(candidate, player));

            evidence = closeToProtectedPed && hostileRelation;
        }

        if (!evidence)
        {
            return;
        }

        if (Entity.Exists(witness) &&
            candidate.Position.DistanceTo(witness.Position) > 45.0f &&
            !CanPedSeeEntity(witness, candidate, RuntimeAllySightDistance))
        {
            return;
        }

        float score = candidate.Position.DistanceTo(protectedPed.Position);

        if (Entity.Exists(player))
        {
            score = Math.Min(score, candidate.Position.DistanceTo(player.Position));
        }

        if (score < bestScore)
        {
            bestScore = score;
            best = candidate;
        }
    }

    private Ped FindManagedHostileThreatForAlly(Ped allyPed, Ped player)
    {
        Ped best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc candidate = _spawnedNpcs[i];

            if (candidate == null ||
                !Entity.Exists(candidate.Ped) ||
                candidate.Ped.IsDead ||
                IsAllyBehavior(candidate.Behavior) ||
                IsNeutralBehavior(candidate.Behavior))
            {
                continue;
            }

            if (candidate.Behavior == NpcBehavior.Static && !candidate.Activated)
            {
                continue;
            }

            float distanceToAlly = candidate.Ped.Position.DistanceTo(allyPed.Position);
            float distanceToPlayer = candidate.Ped.Position.DistanceTo(player.Position);
            float distance = Math.Min(distanceToAlly, distanceToPlayer);

            if (distance > RuntimeAllyDefenseRadius)
            {
                continue;
            }

            if (distanceToAlly > 45.0f && !CanPedSeeEntity(allyPed, candidate.Ped, RuntimeAllySightDistance))
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate.Ped;
            }
        }

        return best;
    }

    private void AlertAlliesToThreat(Ped threat, Vector3 origin)
    {
        if (!Entity.Exists(threat) || threat.IsDead)
        {
            return;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped) ||
                ally.Ped.IsDead)
            {
                continue;
            }

            float distanceToThreat = ally.Ped.Position.DistanceTo(threat.Position);
            float distanceToOrigin = ally.Ped.Position.DistanceTo(origin);

            if (distanceToThreat > RuntimeAllyDefenseRadius && distanceToOrigin > RuntimeAllyDefenseRadius)
            {
                continue;
            }

            if (distanceToThreat > 45.0f && !CanPedSeeEntity(ally.Ped, threat, RuntimeAllySightDistance))
            {
                continue;
            }

            ActivateAllyCombat(ally, threat);
        }
    }

    private void ActivateAllyCombat(SpawnedNpc ally, Ped target)
    {
        if (ally == null || !Entity.Exists(ally.Ped) || !Entity.Exists(target) || target.IsDead)
        {
            return;
        }

        MarkCombatActivity(ally);

        ally.Activated = true;
        ally.IsReturningHome = false;

        ally.Ped.AlwaysKeepTask = true;
        ally.Ped.BlockPermanentEvents = false;
        ally.Ped.IsEnemy = false;

        try
        {
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ally.Ped.Handle, _allyGroupHash);

            int targetGroup = GetPedRelationshipGroup(target);

            if (targetGroup != 0 &&
                targetGroup != _allyGroupHash &&
                ShouldUseGroupHostilityForThreat(target, targetGroup))
            {
                World.SetRelationshipBetweenGroups((Relationship)RelationshipHate, _allyGroupHash, targetGroup);
                World.SetRelationshipBetweenGroups((Relationship)RelationshipHate, targetGroup, _allyGroupHash);
            }

            Function.Call(Hash.SET_PED_COMBAT_ABILITY, ally.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ally.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, ally.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ally.Ped.Handle, 0, false);

            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ally.Ped.Handle, 0, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ally.Ped.Handle, 5, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ally.Ped.Handle, 46, true);
        }
        catch
        {
        }

        /*
         * Cas special : si cet allie est un garde Cartel actif,
         * on utilise la couche Cartel dediee qui gere le tir en vehicule.
         */
        if (_cartelNpcHandles.Contains(ally.Ped.Handle))
        {
            Ped player = Game.Player.Character;

            if (Entity.Exists(player))
            {
                EngageCartelGuardThreat(ally, target, player, false);
                return;
            }
        }

        if (TryActivateCombatAgainstTarget(ally, target, false))
        {
            // Je crée le jeton causal seulement après l'ordre offensif accepté.
            RecordJusticeAllyPoliceEngagement(ally.Ped, target, false);
        }
    }

    private void ActivateCombatAgainstBestTarget(SpawnedNpc npc, bool stationary)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return;
        }

        Ped target = FindHostileTargetForEnemy(npc.Ped, player);

        if (Entity.Exists(target))
        {
            ActivateCombatAgainstTarget(npc, target, stationary);
        }
    }

    private Ped FindHostileTargetForEnemy(Ped enemyPed, Ped player)
    {
        if (!Entity.Exists(enemyPed) || !Entity.Exists(player))
        {
            return null;
        }

        Ped best = player;
        float bestDistance = enemyPed.Position.DistanceTo(player.Position);

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc candidate = _spawnedNpcs[i];

            if (candidate == null ||
                !IsAllyBehavior(candidate.BaseBehavior) ||
                !Entity.Exists(candidate.Ped) ||
                candidate.Ped.IsDead)
            {
                continue;
            }

            float distance = enemyPed.Position.DistanceTo(candidate.Ped.Position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate.Ped;
            }
        }

        return best;
    }

    private void ActivateCombatAgainstTarget(SpawnedNpc npc, Ped target, bool stationary)
    {
        TryActivateCombatAgainstTarget(npc, target, stationary);
    }

    private bool TryActivateCombatAgainstTarget(SpawnedNpc npc, Ped target, bool stationary)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(target) || target.IsDead)
        {
            return false;
        }

        npc.Activated = true;
        npc.IsReturningHome = false;

        npc.Ped.AlwaysKeepTask = true;
        npc.Ped.BlockPermanentEvents = false;
        npc.Ped.IsEnemy = !IsAllyBehavior(npc.BaseBehavior);

        try
        {
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, IsAllyBehavior(npc.BaseBehavior) ? _allyGroupHash : _hostileGroupHash);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, npc.Ped.Handle, stationary ? 0 : 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, npc.Ped.Handle, 2);

            Function.Call(
                Hash.TASK_COMBAT_PED,
                npc.Ped.Handle,
                target.Handle,
                0,
                16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReleaseJusticeAllyPoliceTargetForTransfer(
        JusticeAllyCausalToken token,
        Ped player)
    {
        if (!_justiceEnabled || _justiceCaseState == null ||
            _justiceCaseState.Phase != JusticePhase.Captured ||
            token == null ||
            !string.Equals(token.EpisodeId, CurrentJusticeEpisodeId(), StringComparison.Ordinal) ||
            !Entity.Exists(player) || !Entity.Exists(token.Ally) ||
            !Entity.Exists(token.PoliceTarget))
        {
            return false;
        }

        try
        {
            if (token.Ally.IsDead || token.PoliceTarget.IsDead ||
                !HasJusticeValidAllyOwnership(
                    IsJusticeOwnedAlly(token.Ally),
                    token.Ally.IsDead,
                    token.WasDonJOwnedAtCreation) ||
                !IsJusticePolicePed(token.PoliceTarget) ||
                GetJusticeEntityGeneration(token.Ally) != token.AllyGeneration ||
                GetJusticeEntityGeneration(token.PoliceTarget) != token.TargetGeneration)
            {
                return false;
            }

            float allyDistance = token.Ally.Position.DistanceTo(player.Position);
            float targetDistance = token.PoliceTarget.Position.DistanceTo(player.Position);
            bool stillTargetingPolice = IsPedInCombatWith(token.Ally, token.PoliceTarget);
            if (!IsJusticeTransferTargetContextValid(
                    _justiceMonotonicTimeMs,
                    token.ExpiresAtMs,
                    allyDistance,
                    targetDistance,
                    stillTargetingPolice))
            {
                return false;
            }

            // Je remplace immédiatement la tâche offensive prouvée par un ordre
            // de service neutre. L'allié ne suit donc jamais le joueur en prison
            // et ne reste pas sans tâche pendant la suspension de son IA.
            if (!TryHoldJusticeAllyServiceDuringCustody(token.Ally))
            {
                return false;
            }
            PrepareJusticeAllyServiceResume(token.Ally);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryHoldJusticeAllyServiceDuringCustody(Ped ally)
    {
        if (!Entity.Exists(ally) || ally.IsDead)
        {
            return false;
        }

        try
        {
            Vehicle currentVehicle = ally.IsInVehicle() ? ally.CurrentVehicle : null;
            if (Entity.Exists(currentVehicle) && IsPedDriverOfVehicle(ally, currentVehicle))
            {
                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, ally.Handle, 0.0f);
                Function.Call(
                    (Hash)NativeTaskVehicleTempAction,
                    ally.Handle,
                    currentVehicle.Handle,
                    HighSecurityEscortImmediateBrakeAction,
                    JusticeAllyCustodyHoldMs);
            }
            else
            {
                Function.Call(
                    Hash.TASK_STAND_STILL,
                    ally.Handle,
                    JusticeAllyCustodyHoldMs);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeTransferTargetContextValid(
        long nowMs,
        long expiresAtMs,
        float allyDistance,
        float targetDistance,
        bool stillTargetingPolice)
    {
        return expiresAtMs >= nowMs &&
               allyDistance >= 0.0f && allyDistance <= JusticeAllyAttributionRadius &&
               targetDistance >= 0.0f && targetDistance <= JusticeAllyAttributionRadius &&
               stillTargetingPolice;
    }

    private void PrepareJusticeAllyServiceResume(Ped ally)
    {
        if (!Entity.Exists(ally))
        {
            return;
        }

        SpawnedNpc npc = FindSpawnedNpcByHandle(ally.Handle);
        if (npc == null)
        {
            return;
        }

        int now = Game.GameTime;
        npc.Activated = true;
        npc.IsReturningHome = false;
        npc.LastCombatActivityAt = unchecked(now - GuardReturnDelayMs);
        npc.NextThinkAt = now;
        npc.NextBodyguardTaskAt = now;

        if (_cartelNpcHandles.Contains(ally.Handle))
        {
            _cartelNextCombatOrderAt[ally.Handle] = 0;
            Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);
            if (Entity.Exists(assignedVehicle))
            {
                _cartelNextVehicleOrderAt[assignedVehicle.Handle] = 0;
            }
        }

        PrepareHighSecurityEscortGuardServiceResumeAfterJustice(npc);
    }

    private void StartOrContinuePatrol(SpawnedNpc npc, bool forceNewTarget)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || !IsPatrolBehavior(npc.BaseBehavior))
        {
            return;
        }

        if (npc.PatrolRadius <= 0.1f)
        {
            npc.PatrolRadius = _selectedPatrolRadius;
        }

        float distanceToCenter = npc.Ped.Position.DistanceTo(npc.PatrolCenter);
        bool outsidePatrol = distanceToCenter > npc.PatrolRadius + 7.5f;
        bool arrived = !IsZeroVector(npc.PatrolTarget) && npc.Ped.Position.DistanceTo(npc.PatrolTarget) <= PatrolArriveDistance + 0.75f;

        if (forceNewTarget || outsidePatrol || arrived || IsZeroVector(npc.PatrolTarget) || Game.GameTime >= npc.NextPatrolTaskAt)
        {
            npc.PatrolTarget = outsidePatrol
                ? FindSafePatrolPoint(npc.PatrolCenter, Math.Max(3.0f, npc.PatrolRadius * 0.45f))
                : FindSafePatrolPoint(npc.PatrolCenter, npc.PatrolRadius);

            npc.NextPatrolTaskAt = Game.GameTime + PatrolRetaskMs + _random.Next(0, 4500);

            Function.Call(
                Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                npc.Ped.Handle,
                npc.PatrolTarget.X,
                npc.PatrolTarget.Y,
                npc.PatrolTarget.Z,
                PatrolWalkSpeed,
                -1,
                PatrolArriveDistance,
                true,
                npc.HomeHeading);
        }
    }

    private Vector3 FindSafePatrolPoint(Vector3 center, float radius)
    {
        radius = ClampFloat(radius, MinPatrolRadius, MaxPatrolRadius);

        for (int i = 0; i < 12; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2.0;
            double distance = Math.Sqrt(_random.NextDouble()) * radius;

            Vector3 desired = new Vector3(
                center.X + (float)(Math.Cos(angle) * distance),
                center.Y + (float)(Math.Sin(angle) * distance),
                center.Z);

            Vector3 safe = World.GetSafeCoordForPed(desired, false, 16);

            if (!IsZeroVector(safe) && safe.DistanceTo(center) <= radius + 3.0f)
            {
                return safe;
            }

            float ground = World.GetGroundHeight(new Vector3(desired.X, desired.Y, desired.Z + 1000.0f));

            if (Math.Abs(ground) > 0.001f)
            {
                desired.Z = ground + 0.35f;
                return desired;
            }
        }

        return center;
    }

    private void ManageBodyguardFootFollow(SpawnedNpc bodyguard, Ped player)
    {
        if (!Entity.Exists(bodyguard.Ped) || !Entity.Exists(player))
        {
            return;
        }

        Vehicle currentVehicle = bodyguard.Ped.CurrentVehicle;

        if (Entity.Exists(currentVehicle))
        {
            if (bodyguard.Ped.Position.DistanceTo(player.Position) <= 18.0f)
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, bodyguard.Ped.Handle, currentVehicle.Handle, 0);
            }
            else
            {
                DriveBodyguardVehicleToPlayer(bodyguard, currentVehicle, player);
            }

            return;
        }

        float distance = bodyguard.Ped.Position.DistanceTo(player.Position);

        if (distance > 2.0f)
        {
            float offsetSide = ((bodyguard.Ped.Handle % 5) - 2) * 0.85f;
            float offsetBack = -BodyguardFootFollowDistance - ((bodyguard.Ped.Handle % 3) * 0.75f);

            Function.Call(
                Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY,
                bodyguard.Ped.Handle,
                player.Handle,
                offsetSide,
                offsetBack,
                0.0f,
                2.0f,
                -1,
                1.5f,
                true);
        }
        else
        {
            Function.Call(Hash.TASK_STAND_STILL, bodyguard.Ped.Handle, 1200);
        }
    }

    private void ManageBodyguardVehicleFollow(SpawnedNpc bodyguard, Ped player)
    {
        if (!Entity.Exists(bodyguard.Ped) || !Entity.Exists(player) || !player.IsInVehicle())
        {
            return;
        }

        Vehicle playerVehicle = player.CurrentVehicle;

        if (!Entity.Exists(playerVehicle))
        {
            return;
        }

        if (bodyguard.Ped.IsInVehicle(playerVehicle))
        {
            bodyguard.BodyguardAssignedVehicleHandle = playerVehicle.Handle;
            bodyguard.BodyguardAssignedSeat = (int)bodyguard.Ped.SeatIndex;
            bodyguard.BodyguardIsDriver = false;
            return;
        }

        if (!Entity.Exists(bodyguard.Ped.CurrentVehicle))
        {
            int playerSeat = FindFreePassengerSeatForBodyguard(playerVehicle, bodyguard);

            if (playerSeat != 999)
            {
                AssignBodyguardToVehicleSeat(bodyguard, playerVehicle, playerSeat, false);
                return;
            }
        }

        Vehicle convoyVehicle = Entity.Exists(bodyguard.Ped.CurrentVehicle)
            ? bodyguard.Ped.CurrentVehicle
            : FindOrAssignConvoyVehicle(bodyguard, player, playerVehicle);

        if (!Entity.Exists(convoyVehicle))
        {
            ManageBodyguardFootFollow(bodyguard, player);
            return;
        }

        if (!bodyguard.Ped.IsInVehicle(convoyVehicle))
        {
            int seat = bodyguard.BodyguardAssignedSeat;

            if (seat == 999 || !IsVehicleSeatUsableForBodyguard(convoyVehicle, seat, bodyguard))
            {
                seat = FindFreeSeatForConvoyVehicle(convoyVehicle, bodyguard);
            }

            if (seat == 999)
            {
                ManageBodyguardFootFollow(bodyguard, player);
                return;
            }

            AssignBodyguardToVehicleSeat(bodyguard, convoyVehicle, seat, seat == -1);
            return;
        }

        if (IsPedDriverOfVehicle(bodyguard.Ped, convoyVehicle))
        {
            DriveBodyguardConvoyVehicle(bodyguard, convoyVehicle, playerVehicle);
        }
    }

    private Vehicle FindOrAssignConvoyVehicle(SpawnedNpc bodyguard, Ped player, Vehicle playerVehicle)
    {
        Vehicle assigned = FindVehicleByHandle(bodyguard.BodyguardAssignedVehicleHandle);

        if (Entity.Exists(assigned) &&
            assigned.Handle != playerVehicle.Handle &&
            IsVehicleDriveable(assigned))
        {
            return assigned;
        }

        Vehicle[] nearby = GetNearbyVehiclesSafe(player, BodyguardVehicleSearchRadius);
        Vehicle best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nearby.Length; i++)
        {
            Vehicle vehicle = nearby[i];

            if (!Entity.Exists(vehicle) ||
                vehicle.Handle == playerVehicle.Handle ||
                !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            int freeSeat = FindFreeSeatForConvoyVehicle(vehicle, bodyguard);

            if (freeSeat == 999)
            {
                continue;
            }

            float distance = vehicle.Position.DistanceTo(bodyguard.Ped.Position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = vehicle;
            }
        }

        if (Entity.Exists(best))
        {
            int seat = FindFreeSeatForConvoyVehicle(best, bodyguard);

            if (seat != 999)
            {
                bodyguard.BodyguardAssignedVehicleHandle = best.Handle;
                bodyguard.BodyguardAssignedSeat = seat;
                bodyguard.BodyguardIsDriver = seat == -1;
            }
        }

        return best;
    }

    private void AssignBodyguardToVehicleSeat(SpawnedNpc bodyguard, Vehicle vehicle, int seat, bool driver)
    {
        if (bodyguard == null || !Entity.Exists(bodyguard.Ped) || !Entity.Exists(vehicle))
        {
            return;
        }

        bodyguard.BodyguardAssignedVehicleHandle = vehicle.Handle;
        bodyguard.BodyguardAssignedSeat = seat;
        bodyguard.BodyguardIsDriver = driver;

        Function.Call(
            Hash.TASK_ENTER_VEHICLE,
            bodyguard.Ped.Handle,
            vehicle.Handle,
            5000,
            seat,
            2.0f,
            1,
            0);
    }

    private void DriveBodyguardConvoyVehicle(SpawnedNpc bodyguard, Vehicle convoyVehicle, Vehicle playerVehicle)
    {
        if (bodyguard == null || !Entity.Exists(bodyguard.Ped) || !Entity.Exists(convoyVehicle) || !Entity.Exists(playerVehicle))
        {
            return;
        }

        float playerSpeed = Math.Max(8.0f, playerVehicle.Speed * 1.25f + 8.0f);
        float cruiseSpeed = ClampFloat(playerSpeed, 10.0f, 85.0f);

        Function.Call(Hash.SET_DRIVER_ABILITY, bodyguard.Ped.Handle, 1.0f);
        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, bodyguard.Ped.Handle, 0.45f);
        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, bodyguard.Ped.Handle, ProfessionalDrivingStyle);
        Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, bodyguard.Ped.Handle, cruiseSpeed);

        Function.Call(
            Hash.TASK_VEHICLE_ESCORT,
            bodyguard.Ped.Handle,
            convoyVehicle.Handle,
            playerVehicle.Handle,
            -1,
            cruiseSpeed,
            ProfessionalDrivingStyle,
            8.0f,
            0,
            18.0f);
    }

    private void DriveBodyguardVehicleToPlayer(SpawnedNpc bodyguard, Vehicle vehicle, Ped player)
    {
        if (bodyguard == null || !Entity.Exists(bodyguard.Ped) || !Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        if (!IsPedDriverOfVehicle(bodyguard.Ped, vehicle))
        {
            if (IsVehicleSeatUsableForBodyguard(vehicle, -1, bodyguard))
            {
                AssignBodyguardToVehicleSeat(bodyguard, vehicle, -1, true);
            }

            return;
        }

        Function.Call(Hash.SET_DRIVER_ABILITY, bodyguard.Ped.Handle, 1.0f);
        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, bodyguard.Ped.Handle, 0.25f);
        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, bodyguard.Ped.Handle, ProfessionalDrivingStyle);

        Function.Call(
            Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
            bodyguard.Ped.Handle,
            vehicle.Handle,
            player.Position.X,
            player.Position.Y,
            player.Position.Z,
            25.0f,
            ProfessionalDrivingStyle,
            8.0f);
    }

    private int FindFreePassengerSeatForBodyguard(Vehicle vehicle, SpawnedNpc bodyguard)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        int maxPassengers = Math.Max(0, Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle));

        for (int seat = 0; seat < maxPassengers; seat++)
        {
            if (IsVehicleSeatUsableForBodyguard(vehicle, seat, bodyguard))
            {
                return seat;
            }
        }

        return 999;
    }

    private int FindFreeSeatForConvoyVehicle(Vehicle vehicle, SpawnedNpc bodyguard)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        if (IsVehicleSeatUsableForBodyguard(vehicle, -1, bodyguard))
        {
            return -1;
        }

        return FindFreePassengerSeatForBodyguard(vehicle, bodyguard);
    }

    private bool IsVehicleSeatUsableForBodyguard(Vehicle vehicle, int seat, SpawnedNpc bodyguard)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        if (!Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, vehicle.Handle, seat, false))
        {
            return false;
        }

        if (IsSeatReservedByAnotherBodyguard(vehicle.Handle, seat, bodyguard))
        {
            return false;
        }

        return true;
    }

    private bool IsSeatReservedByAnotherBodyguard(int vehicleHandle, int seat, SpawnedNpc current)
    {
        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc other = _spawnedNpcs[i];

            if (other == null ||
                other == current ||
                other.BaseBehavior != NpcBehavior.Bodyguard ||
                !Entity.Exists(other.Ped) ||
                other.Ped.IsDead)
            {
                continue;
            }

            if (other.BodyguardAssignedVehicleHandle == vehicleHandle &&
                other.BodyguardAssignedSeat == seat)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesVehicleHandleExist(int handle)
    {
        if (handle == 0)
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle) &&
                   Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, handle);
        }
        catch
        {
            return false;
        }
    }

    private Vehicle FindVehicleByHandle(int handle)
    {
        if (handle == 0)
        {
            return null;
        }

        /*
         * Les véhicules Cartel sont enregistrés dans _placedVehicles. Cette voie
         * évite un World.GetAllVehicles() à chaque vérification de handle.
         */
        PlacedVehicle placedVehicle = FindPlacedVehicleByHandle(handle);

        if (placedVehicle != null && Entity.Exists(placedVehicle.Vehicle))
        {
            return placedVehicle.Vehicle;
        }

        Vehicle[] vehicles = World.GetAllVehicles();

        for (int i = 0; i < vehicles.Length; i++)
        {
            if (Entity.Exists(vehicles[i]) && vehicles[i].Handle == handle)
            {
                return vehicles[i];
            }
        }

        return null;
    }

    private Vehicle[] GetNearbyVehiclesSafe(Ped center, float radius)
    {
        if (!Entity.Exists(center))
        {
            return new Vehicle[0];
        }

        try
        {
            return World.GetNearbyVehicles(center, radius) ?? new Vehicle[0];
        }
        catch
        {
            return new Vehicle[0];
        }
    }

    private static bool IsVehicleDriveable(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(Hash.IS_VEHICLE_DRIVEABLE, vehicle.Handle, false);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPedDriverOfVehicle(Ped ped, Vehicle vehicle)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            return Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, -1, false) == ped.Handle;
        }
        catch
        {
            return false;
        }
    }

    private bool HasGuardCombatActivity(SpawnedNpc npc, Ped player)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(player))
        {
            return false;
        }

        if (npc.Ped.HasBeenDamagedBy(player))
        {
            return true;
        }

        Ped target = FindHostileTargetForEnemy(npc.Ped, player);

        return HasCombatActivityWithTarget(npc.Ped, target, 160.0f);
    }

    private bool HasCombatActivityWithTarget(Ped ped, Ped target, float sightDistance)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(target))
        {
            return false;
        }

        if (IsPedShooting(ped) || IsPedShooting(target))
        {
            if (ped.Position.DistanceTo(target.Position) <= sightDistance)
            {
                return true;
            }
        }

        if (ped.Position.DistanceTo(target.Position) <= 40.0f)
        {
            return true;
        }

        if (IsPedInCombatWith(ped, target) || IsPedInCombatWith(target, ped))
        {
            return true;
        }

        return CanPedSeeEntity(ped, target, sightDistance);
    }

    private void MarkCombatActivity(SpawnedNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        npc.LastCombatActivityAt = Game.GameTime;
        npc.IsReturningHome = false;
    }

    private bool ShouldReturnToPostAfterCalm(SpawnedNpc npc, Ped player)
    {
        if (npc == null ||
            npc.BaseBehavior == NpcBehavior.Bodyguard ||
            npc.BaseBehavior == NpcBehavior.Attacker ||
            npc.BaseBehavior == NpcBehavior.Static)
        {
            return false;
        }

        if (HasGuardCombatActivity(npc, player))
        {
            return false;
        }

        return Game.GameTime - npc.LastCombatActivityAt >= GuardReturnDelayMs;
    }

    private void BeginReturnHome(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || npc.BaseBehavior == NpcBehavior.Bodyguard)
        {
            return;
        }

        npc.IsReturningHome = true;
        npc.Activated = false;
        npc.NextReturnTaskAt = 0;
        npc.PatrolTarget = Vector3.Zero;

        Function.Call(Hash.CLEAR_PED_TASKS, npc.Ped.Handle);

        if (IsNeutralBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.Ped.IsEnemy = false;
            npc.Ped.BlockPermanentEvents = true;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _neutralGroupHash);
        }
        else if (IsAllyBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.Ped.IsEnemy = false;
            npc.Ped.BlockPermanentEvents = false;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
        }
        else if (IsHostileBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.Ped.IsEnemy = true;
            npc.Ped.BlockPermanentEvents = false;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _hostileGroupHash);
        }

        CreateOrUpdateNpcBlip(npc);
    }

    private void CancelReturnHome(SpawnedNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        npc.IsReturningHome = false;
        npc.NextReturnTaskAt = 0;
        npc.LastCombatActivityAt = Game.GameTime;
    }

    private void UpdateReturnHome(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        float distance = npc.Ped.Position.DistanceTo(npc.HomePosition);

        if (distance <= GuardReturnArriveDistance)
        {
            FinishReturnHome(npc);
            return;
        }

        if (Game.GameTime >= npc.NextReturnTaskAt)
        {
            npc.NextReturnTaskAt = Game.GameTime + GuardReturnRetaskMs;

            Function.Call(
                Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                npc.Ped.Handle,
                npc.HomePosition.X,
                npc.HomePosition.Y,
                npc.HomePosition.Z,
                GuardWalkSpeed,
                -1,
                GuardReturnArriveDistance,
                true,
                npc.HomeHeading);
        }
    }

    private void FinishReturnHome(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped))
        {
            return;
        }

        npc.IsReturningHome = false;
        npc.Activated = false;
        npc.LastCombatActivityAt = Game.GameTime;

        Function.Call(Hash.CLEAR_PED_TASKS, npc.Ped.Handle);

        if (IsPatrolBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.PatrolTarget = Vector3.Zero;

            if (IsNeutralBehavior(npc.BaseBehavior))
            {
                npc.Ped.BlockPermanentEvents = true;
                npc.Ped.IsEnemy = false;
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _neutralGroupHash);
            }
            else if (IsAllyBehavior(npc.BaseBehavior))
            {
                npc.Ped.BlockPermanentEvents = false;
                npc.Ped.IsEnemy = false;
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
            }
            else
            {
                npc.Ped.BlockPermanentEvents = false;
                npc.Ped.IsEnemy = true;
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _hostileGroupHash);
            }

            StartOrContinuePatrol(npc, true);
            CreateOrUpdateNpcBlip(npc);
            return;
        }

        npc.Ped.Position = npc.HomePosition;
        npc.Ped.Heading = NormalizeHeading(npc.HomeHeading);

        Function.Call(Hash.TASK_ACHIEVE_HEADING, npc.Ped.Handle, npc.HomeHeading, 1500);
        Function.Call(Hash.TASK_STAND_STILL, npc.Ped.Handle, 2500);

        if (IsNeutralBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.Ped.BlockPermanentEvents = true;
            npc.Ped.IsEnemy = false;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _neutralGroupHash);
        }
        else if (IsAllyBehavior(npc.BaseBehavior))
        {
            npc.Behavior = npc.BaseBehavior;
            npc.Ped.BlockPermanentEvents = false;
            npc.Ped.IsEnemy = false;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
        }

        CreateOrUpdateNpcBlip(npc);
    }

    private Ped FindDamagingPedForVictim(Ped victim, Vector3 eventPosition, float radius)
    {
        if (!Entity.Exists(victim))
        {
            return null;
        }

        Ped[] candidates = GetNearbyPedsSafe(victim, radius);

        for (int i = 0; i < candidates.Length; i++)
        {
            Ped candidate = candidates[i];

            if (!Entity.Exists(candidate) || candidate.IsDead || candidate.Handle == victim.Handle)
            {
                continue;
            }

            if (victim.HasBeenDamagedBy(candidate))
            {
                return candidate;
            }
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc candidate = _spawnedNpcs[i];

            if (candidate == null ||
                !Entity.Exists(candidate.Ped) ||
                candidate.Ped.IsDead ||
                candidate.Ped.Handle == victim.Handle)
            {
                continue;
            }

            if (candidate.Ped.Position.DistanceTo(eventPosition) <= radius &&
                victim.HasBeenDamagedBy(candidate.Ped))
            {
                return candidate.Ped;
            }
        }

        return null;
    }

    private bool HasDamagedAnyManagedAlly(Ped candidate)
    {
        if (!Entity.Exists(candidate))
        {
            return false;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped) ||
                ally.Ped.IsDead)
            {
                continue;
            }

            if (HasDefensiveDamageAgainstProtectedPed(candidate, ally.Ped))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInCombatWithAnyManagedAlly(Ped candidate)
    {
        if (!Entity.Exists(candidate))
        {
            return false;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped) ||
                ally.Ped.IsDead)
            {
                continue;
            }

            if (IsPedInCombatWith(candidate, ally.Ped))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsShootingThreatNearProtectedTarget(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return false;
        }

        if (candidate.Position.DistanceTo(player.Position) <= AllyShotThreatDistance)
        {
            return true;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped) ||
                ally.Ped.IsDead)
            {
                continue;
            }

            if (candidate.Position.DistanceTo(ally.Ped.Position) <= AllyShotThreatDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsValidThreatCandidateForAlly(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || candidate.IsDead)
        {
            return false;
        }

        if (Entity.Exists(player) && candidate.Handle == player.Handle)
        {
            return false;
        }

        if (Entity.Exists(_placementPreviewPed) && candidate.Handle == _placementPreviewPed.Handle)
        {
            return false;
        }

        if (IsManagedAlly(candidate))
        {
            return false;
        }

        int group = GetPedRelationshipGroup(candidate);

        if (group == _allyGroupHash)
        {
            return false;
        }

        return true;
    }

    private bool IsManagedAlly(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc ally = _spawnedNpcs[i];

            if (ally == null ||
                !IsAllyBehavior(ally.BaseBehavior) ||
                !Entity.Exists(ally.Ped))
            {
                continue;
            }

            if (ally.Ped.Handle == ped.Handle)
            {
                return true;
            }
        }

        return false;
    }

    private Ped[] GetUniqueNearbyPeds(Ped firstCenter, Ped secondCenter, float radius)
    {
        List<Ped> result = new List<Ped>();
        HashSet<int> seen = new HashSet<int>();

        AddNearbyPeds(firstCenter, radius, result, seen);
        AddNearbyPeds(secondCenter, radius, result, seen);

        return result.ToArray();
    }

    private void AddNearbyPeds(Ped center, float radius, List<Ped> result, HashSet<int> seen)
    {
        Ped[] peds = GetNearbyPedsSafe(center, radius);

        for (int i = 0; i < peds.Length; i++)
        {
            Ped ped = peds[i];

            if (!Entity.Exists(ped) || seen.Contains(ped.Handle))
            {
                continue;
            }

            seen.Add(ped.Handle);
            result.Add(ped);
        }
    }

    private static Ped[] GetNearbyPedsSafe(Ped center, float radius)
    {
        if (!Entity.Exists(center))
        {
            return new Ped[0];
        }

        try
        {
            return World.GetNearbyPeds(center, radius) ?? new Ped[0];
        }
        catch
        {
            return new Ped[0];
        }
    }

    private bool CanPedSeeEntity(Ped ped, Entity target, float maxDistance)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(target))
        {
            return false;
        }

        if (ped.Position.DistanceTo(target.Position) > maxDistance)
        {
            return false;
        }

        return Function.Call<bool>(
            Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
            ped.Handle,
            target.Handle,
            17);
    }

    private bool IsPlayerShootingNearPed(Ped player, Ped ped, float maxDistance)
    {
        if (!Entity.Exists(player) || !Entity.Exists(ped))
        {
            return false;
        }

        if (!IsPedShooting(player))
        {
            return false;
        }

        return player.Position.DistanceTo(ped.Position) <= maxDistance;
    }

    private static bool IsPedShooting(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        return Function.Call<bool>(Hash.IS_PED_SHOOTING, ped.Handle);
    }

    private static bool IsPedInCombatWith(Ped ped, Ped target)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(target))
        {
            return false;
        }

        return Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped.Handle, target.Handle);
    }

    private bool ShouldRefreshPassiveTask(SpawnedNpc npc)
    {
        if (npc == null)
        {
            return false;
        }

        if (Game.GameTime < npc.NextPassiveTaskAt)
        {
            return false;
        }

        npc.NextPassiveTaskAt = GetNextPassiveTaskTime();
        return true;
    }

    private void HoldStaticPositionThrottled(SpawnedNpc npc, Ped player)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        if (!ShouldRefreshPassiveTask(npc))
        {
            return;
        }

        if (Entity.Exists(player))
        {
            Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, npc.Ped.Handle, player.Handle, 1200);
        }

        Function.Call(Hash.TASK_STAND_STILL, npc.Ped.Handle, PassiveHoldRefreshMs + 800);
    }

    private void HoldGuardPositionThrottled(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        if (!ShouldRefreshPassiveTask(npc))
        {
            return;
        }

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _neutralGroupHash);
        Function.Call(Hash.TASK_STAND_STILL, npc.Ped.Handle, PassiveHoldRefreshMs + 800);
    }

    private void HoldAllyPositionThrottled(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        if (!ShouldRefreshPassiveTask(npc))
        {
            return;
        }

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
        Function.Call(Hash.TASK_STAND_STILL, npc.Ped.Handle, PassiveHoldRefreshMs + 800);
    }

    private void HoldStaticPosition(Ped ped)
    {
        if (!Entity.Exists(ped) || ped.IsDead)
        {
            return;
        }

        Ped player = Game.Player.Character;

        if (Entity.Exists(player))
        {
            Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, ped.Handle, player.Handle, 1200);
        }

        Function.Call(Hash.TASK_STAND_STILL, ped.Handle, 2500);
    }

    private void HoldGuardPosition(Ped ped)
    {
        if (!Entity.Exists(ped) || ped.IsDead)
        {
            return;
        }

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, _neutralGroupHash);
        Function.Call(Hash.TASK_STAND_STILL, ped.Handle, 2500);
    }

    private void HoldAllyPosition(Ped ped)
    {
        if (!Entity.Exists(ped) || ped.IsDead)
        {
            return;
        }

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, _allyGroupHash);
        Function.Call(Hash.TASK_STAND_STILL, ped.Handle, 2500);
    }

    private static bool IsAllyBehavior(NpcBehavior behavior)
    {
        return behavior == NpcBehavior.Ally ||
               behavior == NpcBehavior.AllyPatrol ||
               behavior == NpcBehavior.Bodyguard;
    }

    private static bool IsNeutralBehavior(NpcBehavior behavior)
    {
        return behavior == NpcBehavior.Neutral ||
               behavior == NpcBehavior.NeutralPatrol;
    }

    private static bool IsHostileBehavior(NpcBehavior behavior)
    {
        return behavior == NpcBehavior.Attacker ||
               behavior == NpcBehavior.Static ||
               behavior == NpcBehavior.HostilePatrol;
    }

    private static bool IsPatrolBehavior(NpcBehavior behavior)
    {
        return behavior == NpcBehavior.NeutralPatrol ||
               behavior == NpcBehavior.HostilePatrol ||
               behavior == NpcBehavior.AllyPatrol;
    }

    private void CreateOrUpdateNpcBlip(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            RemoveNpcBlip(npc);
            return;
        }

        int pedHandle = GetPedEntityHandleSafe(npc.Ped);
        bool isEnemyRaidNpc = pedHandle != 0 && _enemyRaidKnownNpcHandles.Contains(pedHandle);

        if (npc.Blip == null || !npc.Blip.Exists())
        {
            try
            {
                npc.Blip = npc.Ped.AddBlip();
                npc.Blip.Scale = isEnemyRaidNpc ? 0.82f : 0.72f;
                npc.Blip.IsShortRange = false;
            }
            catch
            {
                npc.Blip = null;
                return;
            }
        }

        try
        {
            npc.Blip.Sprite = BlipSprite.Enemy2;
            npc.Blip.IsShortRange = false;

            if (isEnemyRaidNpc)
            {
                npc.Blip.Color = BlipColor.Red;
                npc.Blip.IsFriendly = false;
                npc.Blip.Name = "Ballas Ennemi";
                npc.Blip.IsFlashing = false;
                npc.Blip.Scale = 0.82f;
                return;
            }

            npc.Blip.Name = "DonJ NPC";

            if (IsAllyBehavior(npc.BaseBehavior))
            {
                npc.Blip.Color = BlipColor.Green;
                npc.Blip.IsFriendly = true;
                npc.Blip.Name = npc.BaseBehavior == NpcBehavior.Bodyguard ? "DonJ Garde du corps" : "DonJ Allie";
            }
            else if (IsNeutralBehavior(npc.Behavior))
            {
                npc.Blip.Color = BlipColor.Yellow;
                npc.Blip.IsFriendly = false;
                npc.Blip.Name = "DonJ Neutre";
            }
            else
            {
                npc.Blip.Color = BlipColor.Red;
                npc.Blip.IsFriendly = false;
                npc.Blip.Name = "DonJ Ennemi";
            }

            npc.Blip.IsFlashing = npc.IsReturningHome;
        }
        catch
        {
        }
    }

    private void RemoveNpcBlip(SpawnedNpc npc)
    {
        if (npc == null || npc.Blip == null)
        {
            return;
        }

        try
        {
            if (npc.Blip.Exists())
            {
                npc.Blip.Remove();
            }
        }
        catch
        {
        }

        npc.Blip = null;
    }

    private void CreateOrUpdatePlacedVehicleBlip(PlacedVehicle placed)
    {
        if (placed == null || !Entity.Exists(placed.Vehicle))
        {
            RemovePlacedVehicleBlip(placed);
            return;
        }

        int vehicleHandle = GetVehicleEntityHandleSafe(placed.Vehicle);

        /*
         * Véhicule Ballas terminé / en attente de nettoyage :
         * on ne veut plus aucun rond/blip sur la map.
         * Le véhicule physique sera supprimé dès que le joueur s'éloigne
         * ou qu'il n'est plus visible.
         */
        if (vehicleHandle != 0 && _enemyRaidVehicleCleanupHandles.Contains(vehicleHandle))
        {
            RemovePlacedVehicleBlip(placed);
            return;
        }

        if (placed.Blip == null || !placed.Blip.Exists())
        {
            try
            {
                placed.Blip = placed.Vehicle.AddBlip();
                placed.Blip.Scale = 0.65f;
                placed.Blip.IsShortRange = false;
                placed.Blip.Name = "DonJ Vehicule";
            }
            catch
            {
                placed.Blip = null;
                return;
            }
        }

        try
        {
            if (vehicleHandle != 0 && _enemyRaidVehicleHandles.Contains(vehicleHandle))
            {
                placed.Blip.Color = BlipColor.Red;
                placed.Blip.IsFriendly = false;
                placed.Blip.Name = "Ballas Vehicule";
            }
            else
            {
                placed.Blip.Color = BlipColor.Blue;
                placed.Blip.IsFriendly = true;
                placed.Blip.Name = "DonJ Vehicule";
            }
        }
        catch
        {
        }
    }

    private void RemovePlacedVehicleBlip(PlacedVehicle placed)
    {
        if (placed == null || placed.Blip == null)
        {
            return;
        }

        try
        {
            if (placed.Blip.Exists())
            {
                placed.Blip.Remove();
            }
        }
        catch
        {
        }

        placed.Blip = null;
    }

    private void UpdatePlacedVehicles()
    {
        Ped player = Game.Player.Character;

        for (int i = _placedVehicles.Count - 1; i >= 0; i--)
        {
            PlacedVehicle placed = _placedVehicles[i];

            if (placed == null)
            {
                _placedVehicles.RemoveAt(i);
                continue;
            }

            if (placed.RespawnPending)
            {
                TryProcessPlacedVehicleAutoRespawn(placed, player);
                continue;
            }

            if (!Entity.Exists(placed.Vehicle))
            {
                RemovePlacedVehicleBlip(placed);

                if (placed.AutoRespawn)
                {
                    MarkPlacedVehicleForAutoRespawn(placed);
                    TryProcessPlacedVehicleAutoRespawn(placed, player);
                }
                else
                {
                    _placedVehicles.RemoveAt(i);
                }

                continue;
            }

            if (placed.AutoRespawn && IsPlacedVehicleDestroyed(placed))
            {
                MarkPlacedVehicleForAutoRespawn(placed);
                continue;
            }

            placed.Position = placed.Vehicle.Position;
            placed.Heading = placed.Vehicle.Heading;
            CreateOrUpdatePlacedVehicleBlip(placed);
        }
    }

    private void UpdatePlacedObjects()
    {
        Ped player = Game.Player.Character;

        for (int i = _placedObjects.Count - 1; i >= 0; i--)
        {
            PlacedObject placed = _placedObjects[i];

            if (placed == null)
            {
                _placedObjects.RemoveAt(i);
                continue;
            }

            if (placed.RespawnPending)
            {
                TryProcessPlacedObjectAutoRespawn(placed, player);
                continue;
            }

            if (!Entity.Exists(placed.Prop))
            {
                if (placed.AutoRespawn)
                {
                    MarkPlacedObjectForAutoRespawn(placed);
                    TryProcessPlacedObjectAutoRespawn(placed, player);
                }
                else
                {
                    _placedObjects.RemoveAt(i);
                }

                continue;
            }

            if (placed.AutoRespawn && IsPlacedObjectDestroyed(placed))
            {
                MarkPlacedObjectForAutoRespawn(placed);
                continue;
            }

            placed.Position = placed.Prop.Position;
            placed.Heading = placed.Prop.Heading;
        }
    }

    private void StartPlacementMode()
    {
        if (_placementMode)
        {
            return;
        }

        if (IsJusticeTemporaryPlayerProtectionForbidden())
        {
            // Je refuse la protection Placement dès l'arrestation ou le maintien :
            // elle ne doit jamais recréer une invincibilité dans la cellule.
            ShowStatus(
                "Placement indisponible pendant une arrestation ou une détention.",
                3500);
            return;
        }

        if (HasPlacementSessionState())
        {
            // Une précédente initialisation partielle ne doit jamais servir de
            // base à une nouvelle capture d'état. Je la nettoie d'abord.
            StopPlacementMode(false);
            if (HasPlacementSessionState())
            {
                ShowStatus("Placement indisponible: restauration du joueur en cours.", 4000);
                return;
            }
        }

        Ped player = Game.Player.Character;
        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Placement impossible: joueur invalide.", 3000);
            return;
        }

        try
        {
            EnsureRelationshipGroups();
            SetMenuVisible(false);

            int playerHandle = player.Handle;
            if (playerHandle == 0)
            {
                throw new InvalidOperationException("Handle joueur invalide.");
            }

            _storedPlayerFrozen = player.FreezePosition;
            _placementProtectedPlayer = player;
            _placementProtectedPlayerHandle = playerHandle;
            _placementPlayerStateStored = true;

            bool originalInvincibility;
            if (!TryAcquirePlayerInvincibility(
                    player,
                    PlayerInvincibilityOwner.Placement,
                    out originalInvincibility))
            {
                throw new InvalidOperationException(
                    "La protection temporaire du joueur n'a pas pu être vérifiée.");
            }

            _storedPlayerInvincible = originalInvincibility;
            player.FreezePosition = true;
            if (!player.FreezePosition)
            {
                throw new InvalidOperationException(
                    "Le gel temporaire du joueur n'a pas pu être vérifié.");
            }

            Vector3 cameraPosition = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_COORD);
            Vector3 cameraRotation = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_ROT, 2);

            _placementCameraRotation = cameraRotation;
            _placementHeading = NormalizeHeading(player.Heading);
            _placementCamera = World.CreateCamera(cameraPosition, cameraRotation, 60.0f);

            if (!Camera.Exists(_placementCamera))
            {
                throw new InvalidOperationException(
                    "La caméra de placement n'a pas pu être créée.");
            }

            _placementCamera.FarClip = 10000.0f;
            World.RenderingCamera = _placementCamera;

            // Le mode n'est publié qu'après la réussite de toutes les mutations.
            _placementMode = true;
            _placementHasHit = false;
            _placementCancelRequested = false;
            _placementConfirmRequested = false;
            _nextPlacementSpawnAllowedAt = 0;
            _nextPreviewRetryAt = 0;

            ShowStatus(
                "Placement camera actif: clic gauche/Entree place, Echap/clic droit quitte.",
                5000);
        }
        catch (Exception ex)
        {
            // La restauration passe avant le diagnostic : une panne de log ou
            // d'UI ne doit jamais empêcher la sortie du mode invincible.
            StopPlacementMode(true);
            try
            {
                LogException("Placement.Demarrage", ex);
            }
            catch
            {
            }
            try
            {
                ShowStatus(
                    "Placement annulé: état du joueur et caméra restaurés.",
                    4500);
            }
            catch
            {
            }
        }
    }

    private void StopPlacementMode(bool reopenMenu)
    {
        // Je ne retourne jamais uniquement sur _placementMode : ce booléen n'est
        // vrai qu'à la fin du démarrage, alors que le joueur est protégé avant.
        _placementMode = false;

        Exception firstCleanupFailure = null;
        try
        {
            DeletePlacementPreview();
        }
        catch (Exception ex)
        {
            firstCleanupFailure = ex;
        }

        Camera camera = _placementCamera;
        if (!object.ReferenceEquals(camera, null))
        {
            try
            {
                // NIB/SHVDN v2 expose le setter de RenderingCamera mais pas
                // nécessairement son getter. Dès que notre caméra existe, nous
                // devons rendre la caméra de gameplay avant de la détruire.
                World.RenderingCamera = null;
            }
            catch (Exception ex)
            {
                if (firstCleanupFailure == null)
                {
                    firstCleanupFailure = ex;
                }
            }

            try
            {
                if (Camera.Exists(camera))
                {
                    camera.Destroy();
                }
            }
            catch (Exception ex)
            {
                if (firstCleanupFailure == null)
                {
                    firstCleanupFailure = ex;
                }
            }
        }
        _placementCamera = null;

        bool playerRestored = false;
        try
        {
            playerRestored = TryRestorePlacementPlayerState();
        }
        catch (Exception ex)
        {
            if (firstCleanupFailure == null)
            {
                firstCleanupFailure = ex;
            }
        }

        _placementCancelRequested = false;
        _placementConfirmRequested = false;
        _placementHasHit = false;

        if (reopenMenu)
        {
            try
            {
                SetMenuVisible(true);
            }
            catch (Exception ex)
            {
                if (firstCleanupFailure == null)
                {
                    firstCleanupFailure = ex;
                }
            }
        }

        if (firstCleanupFailure != null)
        {
            try
            {
                LogException("Placement.Nettoyage", firstCleanupFailure);
            }
            catch
            {
            }
        }

        if (!playerRestored)
        {
            try
            {
                LogWarning(
                    "Placement.RestaurationJoueur",
                    "Restauration différée; une nouvelle tentative sera faite au prochain tick.");
            }
            catch
            {
            }
        }
    }

    private void UpdatePlacementMode()
    {
        Ped player = Game.Player.Character;
        if (!IsPlacementProtectedPlayer(player) || player.IsDead)
        {
            StopPlacementMode(true);
            ShowStatus("Placement quitté: le personnage joueur a changé.", 3200);
            return;
        }

        if (!Camera.Exists(_placementCamera))
        {
            StopPlacementMode(true);
            return;
        }

        if (!KeepPlayerSafeDuringPlacement())
        {
            StopPlacementMode(true);
            ShowStatus("Placement quitté: protection du joueur non vérifiée.", 3500);
            return;
        }

        DisablePlacementControls();

        bool mouseCancel = IsDisabledControlJustPressed(GtaControl.Aim);
        bool mouseConfirm = IsDisabledControlJustPressed(GtaControl.Attack);

        if (_placementCancelRequested || mouseCancel)
        {
            StopPlacementMode(true);
            ShowStatus("Placement camera quitte.", 2500);
            return;
        }

        UpdatePlacementCameraMovement();
        UpdatePlacementRaycast();
        CreateOrUpdatePlacementPreview();

        if (_placementHasHit)
        {
            DrawPlacementMarker();
        }

        DrawPlacementHud();

        if (_placementConfirmRequested || mouseConfirm)
        {
            _placementConfirmRequested = false;

            if (Game.GameTime < _nextPlacementSpawnAllowedAt)
            {
                return;
            }

            _nextPlacementSpawnAllowedAt = Game.GameTime + PlacementSpawnCooldownMs;

            if (_placementHasHit)
            {
                ConfirmPlacementSpawn();
            }
            else
            {
                ShowStatus("Aucune surface detectee au centre de l'ecran.", 2500);
            }
        }
    }

    private bool KeepPlayerSafeDuringPlacement()
    {
        Ped player = Game.Player.Character;
        if (!IsPlacementProtectedPlayer(player) ||
            !HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement))
        {
            return false;
        }

        MaintainPlayerInvincibilityProtection();
        try
        {
            player.FreezePosition = true;
            return player.IsInvincible && player.FreezePosition;
        }
        catch
        {
            return false;
        }
    }

    private bool HasPlacementSessionState()
    {
        return _placementMode ||
               _placementPlayerStateStored ||
               HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement) ||
               !object.ReferenceEquals(_placementCamera, null) ||
               !object.ReferenceEquals(_placementPreviewPed, null) ||
               !object.ReferenceEquals(_placementPreviewVehicle, null) ||
               !object.ReferenceEquals(_placementPreviewProp, null);
    }

    private void MaintainPlacementPlayerStateRecovery()
    {
        if (!_placementMode &&
            (_placementPlayerStateStored ||
             HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement)))
        {
            TryRestorePlacementPlayerState();
        }
    }

    private bool TryRestorePlacementPlayerState()
    {
        bool hasPlacementProtection =
            HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement);
        if (!_placementPlayerStateStored && !hasPlacementProtection)
        {
            return true;
        }

        Ped player = ResolvePlacementProtectedPlayer();
        bool invincibilityRestored = TryReleasePlayerInvincibility(
            player,
            PlayerInvincibilityOwner.Placement,
            _storedPlayerInvincible,
            false);

        bool freezeRestored = true;
        if (_placementPlayerStateStored)
        {
            if (object.ReferenceEquals(player, null))
            {
                freezeRestored = IsKnownMissingPlayerEntity(_placementProtectedPlayer);
            }
            else
            {
                try
                {
                    player.FreezePosition = _storedPlayerFrozen;
                    freezeRestored = player.FreezePosition == _storedPlayerFrozen;
                }
                catch
                {
                    freezeRestored = false;
                }
            }
        }

        if (invincibilityRestored && freezeRestored)
        {
            _placementProtectedPlayer = null;
            _placementProtectedPlayerHandle = 0;
            _placementPlayerStateStored = false;
            _storedPlayerInvincible = false;
            _storedPlayerFrozen = false;
            return true;
        }

        return false;
    }

    private Ped ResolvePlacementProtectedPlayer()
    {
        if (IsPlacementProtectedPlayer(_placementProtectedPlayer))
        {
            return _placementProtectedPlayer;
        }

        try
        {
            Ped currentPlayer = Game.Player.Character;
            if (IsPlacementProtectedPlayer(currentPlayer))
            {
                return currentPlayer;
            }
        }
        catch
        {
        }

        return null;
    }

    private bool IsPlacementProtectedPlayer(Ped player)
    {
        if (!IsExistingPlayerEntity(player) || _placementProtectedPlayerHandle == 0)
        {
            return false;
        }

        try
        {
            return player.Handle == _placementProtectedPlayerHandle;
        }
        catch
        {
            return false;
        }
    }

    private void DisablePlacementControls()
    {
        Game.DisableAllControlsThisFrame(0);
        Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
    }

    private void DisableMenuGameplayControls()
    {
        Game.DisableControlThisFrame(0, GtaControl.Attack);
        Game.DisableControlThisFrame(0, GtaControl.Aim);
        Game.DisableControlThisFrame(0, GtaControl.Reload);
        Game.DisableControlThisFrame(0, GtaControl.Phone);
        Game.DisableControlThisFrame(0, GtaControl.SelectWeapon);
        Game.DisableControlThisFrame(0, GtaControl.WeaponWheelLeftRight);
        Game.DisableControlThisFrame(0, GtaControl.WeaponWheelUpDown);
    }

    private void UpdatePlacementCameraMovement()
    {
        float dt = Math.Min(Game.LastFrameTime, 0.05f);

        float lookX = Game.GetDisabledControlNormal(0, GtaControl.LookLeftRight);
        float lookY = Game.GetDisabledControlNormal(0, GtaControl.LookUpDown);

        _placementCameraRotation.Z -= lookX * 8.0f;
        _placementCameraRotation.X -= lookY * 8.0f;
        _placementCameraRotation.X = ClampFloat(_placementCameraRotation.X, -89.0f, 89.0f);
        _placementCameraRotation.Y = 0.0f;

        _placementCamera.Rotation = _placementCameraRotation;

        Vector3 forward = Normalize(_placementCamera.Direction);
        Vector3 right = Normalize(new Vector3(forward.Y, -forward.X, 0.0f));
        Vector3 up = new Vector3(0.0f, 0.0f, 1.0f);

        float speed = 18.0f;

        if (IsShiftHeld())
        {
            speed = 65.0f;
        }
        else if (IsAltHeld())
        {
            speed = 5.0f;
        }

        Vector3 movement = Vector3.Zero;

        if (IsKeyDown(Keys.W) || IsKeyDown(Keys.Z)) movement += forward;
        if (IsKeyDown(Keys.S)) movement -= forward;
        if (IsKeyDown(Keys.D)) movement += right;
        if (IsKeyDown(Keys.Q)) movement -= right;
        if (IsKeyDown(Keys.Space)) movement += up;
        if (IsControlHeld()) movement -= up;

        if (movement.Length() > 0.001f)
        {
            movement = Normalize(movement);
            _placementCamera.Position += movement * speed * dt;
        }

        float headingSpeed = IsShiftHeld() ? 180.0f : 75.0f;

        if (IsKeyDown(Keys.A))
        {
            _placementHeading = NormalizeHeading(_placementHeading - headingSpeed * dt);
        }

        if (IsKeyDown(Keys.E))
        {
            _placementHeading = NormalizeHeading(_placementHeading + headingSpeed * dt);
        }
    }

    private void UpdatePlacementRaycast()
    {
        Vector3 origin = _placementCamera.Position;
        Vector3 direction = Normalize(_placementCamera.Direction);

        IntersectOptions options = (IntersectOptions)(
            (int)IntersectOptions.Map |
            (int)IntersectOptions.Objects |
            (int)IntersectOptions.Vegetation);

        RaycastResult ray = World.Raycast(origin, direction, 5000.0f, options, Game.Player.Character);

        if (ray.DitHitAnything)
        {
            _placementHasHit = true;
            _placementHitPoint = ray.HitCoords;
            _placementSurfaceNormal = ray.SurfaceNormal;
            _placementSpawnPoint = CalculatePlacementSpawnPoint(ray.HitCoords, ray.SurfaceNormal, _selectedPlacementType);
        }
        else
        {
            _placementHasHit = false;
            _placementHitPoint = origin + direction * 20.0f;
            _placementSpawnPoint = _placementHitPoint;
            _placementSurfaceNormal = new Vector3(0.0f, 0.0f, 1.0f);
        }
    }

    private static Vector3 CalculatePlacementSpawnPoint(Vector3 hitCoords, Vector3 surfaceNormal, PlacementEntityType placementType)
    {
        Vector3 normal = Normalize(surfaceNormal);

        if (normal.Length() < 0.001f)
        {
            normal = new Vector3(0.0f, 0.0f, 1.0f);
        }

        if (normal.Z > 0.35f)
        {
            switch (placementType)
            {
                case PlacementEntityType.Vehicle:
                    return hitCoords + new Vector3(0.0f, 0.0f, 0.35f);

                case PlacementEntityType.Object:
                case PlacementEntityType.Entrance:
                case PlacementEntityType.Exit:
                    return hitCoords + new Vector3(0.0f, 0.0f, 0.05f);

                case PlacementEntityType.Npc:
                default:
                    return hitCoords + new Vector3(0.0f, 0.0f, 0.75f);
            }
        }

        if (placementType == PlacementEntityType.Object ||
            placementType == PlacementEntityType.Entrance ||
            placementType == PlacementEntityType.Exit)
        {
            return hitCoords + normal * 0.25f;
        }

        return hitCoords + normal * 0.85f + new Vector3(0.0f, 0.0f, 0.35f);
    }

    private void CreateOrUpdatePlacementPreview()
    {
        if (!_placementMode || !_placementHasHit)
        {
            DeletePlacementPreview();
            return;
        }

        string currentKey = CurrentPlacementKey();

        if (IsCurrentPlacementPreviewValid(currentKey))
        {
            MovePlacementPreview();
            return;
        }

        DeletePlacementPreview();

        if (Game.GameTime < _nextPreviewRetryAt)
        {
            return;
        }

        _nextPreviewRetryAt = Game.GameTime + PreviewRetryIntervalMs;

        switch (_selectedPlacementType)
        {
            case PlacementEntityType.Vehicle:
                CreateVehiclePlacementPreview(currentKey);
                break;

            case PlacementEntityType.Object:
                CreateObjectPlacementPreview(currentKey);
                break;

            case PlacementEntityType.Entrance:
            case PlacementEntityType.Exit:
                _placementPreviewType = _selectedPlacementType;
                _placementPreviewKey = currentKey;
                break;

            case PlacementEntityType.Npc:
            default:
                CreateNpcPlacementPreview(currentKey);
                break;
        }
    }

    private bool IsCurrentPlacementPreviewValid(string currentKey)
    {
        if (!string.Equals(_placementPreviewKey, currentKey, StringComparison.Ordinal) ||
            _placementPreviewType != _selectedPlacementType)
        {
            return false;
        }

        switch (_selectedPlacementType)
        {
            case PlacementEntityType.Vehicle:
                return Entity.Exists(_placementPreviewVehicle);

            case PlacementEntityType.Object:
                return Entity.Exists(_placementPreviewProp);

            case PlacementEntityType.Entrance:
            case PlacementEntityType.Exit:
                return true;

            case PlacementEntityType.Npc:
            default:
                return Entity.Exists(_placementPreviewPed);
        }
    }

    private void CreateNpcPlacementPreview(string currentKey)
    {
        ModelIdentity identity = BuildCurrentModelIdentity();
        Ped preview = CreatePedFromModelIdentity(identity, _placementSpawnPoint, NormalizeHeading(_placementHeading));

        if (!Entity.Exists(preview))
        {
            return;
        }

        _placementPreviewPed = preview;
        _placementPreviewType = PlacementEntityType.Npc;
        _placementPreviewKey = currentKey;

        ConfigureNpcPlacementPreview(preview);
        MovePlacementPreview();
    }

    private void CreateVehiclePlacementPreview(string currentKey)
    {
        VehicleIdentity identity = BuildCurrentVehicleIdentity();
        Vehicle preview = CreateVehicleFromIdentity(identity, _placementSpawnPoint, NormalizeHeading(_placementHeading));

        if (!Entity.Exists(preview))
        {
            return;
        }

        _placementPreviewVehicle = preview;
        _placementPreviewType = PlacementEntityType.Vehicle;
        _placementPreviewKey = currentKey;

        ConfigureVehiclePlacementPreview(preview);
        MovePlacementPreview();
    }

    private void CreateObjectPlacementPreview(string currentKey)
    {
        ObjectIdentity identity = BuildCurrentObjectIdentity();
        Prop preview = CreatePropFromIdentity(identity, _placementSpawnPoint, NormalizeHeading(_placementHeading));

        if (!Entity.Exists(preview))
        {
            return;
        }

        _placementPreviewProp = preview;
        _placementPreviewType = PlacementEntityType.Object;
        _placementPreviewKey = currentKey;

        ConfigureObjectPlacementPreview(preview);
        MovePlacementPreview();
    }

    private void ConfigureNpcPlacementPreview(Ped preview)
    {
        if (!Entity.Exists(preview))
        {
            return;
        }

        preview.IsPersistent = true;
        preview.AlwaysKeepTask = true;
        preview.BlockPermanentEvents = true;
        preview.FreezePosition = true;
        preview.IsInvincible = true;
        preview.CanBeTargetted = false;
        preview.CanRagdoll = false;
        preview.Health = Math.Max(1000, _selectedHealth);
        preview.Armor = 0;

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, preview.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_ALPHA, preview.Handle, PlacementPreviewAlpha, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, preview.Handle, false, false);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, preview.Handle, true);
        Function.Call(Hash.SET_ENTITY_VISIBLE, preview.Handle, true, false);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, preview.Handle, false);
        Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, preview.Handle, false);
        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, preview.Handle, _neutralGroupHash);
        Function.Call(Hash.TASK_STAND_STILL, preview.Handle, -1);

        GiveWeaponLoadout(preview, _selectedWeaponLoadout, false);
    }

    private void ConfigureVehiclePlacementPreview(Vehicle preview)
    {
        if (!Entity.Exists(preview))
        {
            return;
        }

        preview.IsPersistent = true;
        preview.FreezePosition = true;
        preview.IsInvincible = true;
        preview.Heading = NormalizeHeading(_placementHeading);

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, preview.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_ALPHA, preview.Handle, PlacementPreviewAlpha, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, preview.Handle, false, false);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, preview.Handle, true);
        Function.Call(Hash.SET_ENTITY_VISIBLE, preview.Handle, true, false);
        Function.Call(Hash.SET_VEHICLE_ENGINE_ON, preview.Handle, false, true, false);
    }

    private void ConfigureObjectPlacementPreview(Prop preview)
    {
        if (!Entity.Exists(preview))
        {
            return;
        }

        preview.IsPersistent = true;
        preview.FreezePosition = true;
        preview.IsInvincible = true;
        preview.Heading = NormalizeHeading(_placementHeading);

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, preview.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_ALPHA, preview.Handle, PlacementPreviewAlpha, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, preview.Handle, false, false);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, preview.Handle, true);
        Function.Call(Hash.SET_ENTITY_VISIBLE, preview.Handle, true, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, preview.Handle, true);
    }

    private void MovePlacementPreview()
    {
        switch (_selectedPlacementType)
        {
            case PlacementEntityType.Vehicle:
                if (!Entity.Exists(_placementPreviewVehicle)) return;

                _placementPreviewVehicle.Position = _placementSpawnPoint;
                _placementPreviewVehicle.Heading = NormalizeHeading(_placementHeading);
                _placementPreviewVehicle.FreezePosition = true;
                Function.Call(Hash.SET_ENTITY_ALPHA, _placementPreviewVehicle.Handle, PlacementPreviewAlpha, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, _placementPreviewVehicle.Handle, false, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _placementPreviewVehicle.Handle, true);
                break;

            case PlacementEntityType.Object:
                if (!Entity.Exists(_placementPreviewProp)) return;

                _placementPreviewProp.Position = _placementSpawnPoint;
                _placementPreviewProp.Heading = NormalizeHeading(_placementHeading);
                _placementPreviewProp.FreezePosition = true;
                Function.Call(Hash.SET_ENTITY_ALPHA, _placementPreviewProp.Handle, PlacementPreviewAlpha, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, _placementPreviewProp.Handle, false, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _placementPreviewProp.Handle, true);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, _placementPreviewProp.Handle, true);
                break;

            case PlacementEntityType.Npc:
            default:
                if (!Entity.Exists(_placementPreviewPed)) return;

                _placementPreviewPed.Position = _placementSpawnPoint;
                _placementPreviewPed.Heading = NormalizeHeading(_placementHeading);
                _placementPreviewPed.FreezePosition = true;

                Function.Call(Hash.SET_ENTITY_ALPHA, _placementPreviewPed.Handle, PlacementPreviewAlpha, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, _placementPreviewPed.Handle, false, false);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, _placementPreviewPed.Handle, true);
                Function.Call(Hash.TASK_STAND_STILL, _placementPreviewPed.Handle, -1);
                break;
        }
    }

    private void DeletePlacementPreview()
    {
        DeleteEntitySafe(_placementPreviewPed);
        DeleteEntitySafe(_placementPreviewVehicle);
        DeleteEntitySafe(_placementPreviewProp);

        _placementPreviewPed = null;
        _placementPreviewVehicle = null;
        _placementPreviewProp = null;
        _placementPreviewType = _selectedPlacementType;
        _placementPreviewKey = string.Empty;
    }

    private void RestorePreviewAsRealPed(Ped ped)
    {
        if (!Entity.Exists(ped)) return;

        ped.FreezePosition = false;
        ped.IsInvincible = false;
        ped.CanBeTargetted = true;
        ped.CanRagdoll = true;
        ped.Position = _placementSpawnPoint;
        ped.Heading = NormalizeHeading(_placementHeading);

        Function.Call(Hash.RESET_ENTITY_ALPHA, ped.Handle);
        Function.Call(Hash.SET_ENTITY_ALPHA, ped.Handle, 255, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, ped.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, false);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);
        Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, ped.Handle, true);
        Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ped.Handle);
    }

    private void RestorePreviewAsRealVehicle(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle)) return;

        vehicle.FreezePosition = false;
        vehicle.IsInvincible = false;
        vehicle.Position = _placementSpawnPoint;
        vehicle.Heading = NormalizeHeading(_placementHeading);

        Function.Call(Hash.RESET_ENTITY_ALPHA, vehicle.Handle);
        Function.Call(Hash.SET_ENTITY_ALPHA, vehicle.Handle, 255, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, vehicle.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, vehicle.Handle, false);
        Function.Call(Hash.SET_ENTITY_VISIBLE, vehicle.Handle, true, false);
    }

    private void RestorePreviewAsRealObject(Prop prop)
    {
        if (!Entity.Exists(prop)) return;

        prop.FreezePosition = false;
        prop.IsInvincible = false;
        prop.Position = _placementSpawnPoint;
        prop.Heading = NormalizeHeading(_placementHeading);

        Function.Call(Hash.RESET_ENTITY_ALPHA, prop.Handle);
        Function.Call(Hash.SET_ENTITY_ALPHA, prop.Handle, 255, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, prop.Handle, true, true);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, prop.Handle, false);
        Function.Call(Hash.SET_ENTITY_VISIBLE, prop.Handle, true, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, prop.Handle, true);
    }

    private void ConfirmPlacementSpawn()
    {
        if (!_placementHasHit)
        {
            ShowStatus("Aucune surface detectee.", 2500);
            return;
        }

        switch (_selectedPlacementType)
        {
            case PlacementEntityType.Vehicle:
                ConfirmVehiclePlacementSpawn();
                break;

            case PlacementEntityType.Object:
                ConfirmObjectPlacementSpawn();
                break;

            case PlacementEntityType.Entrance:
                ConfirmInteriorEntrancePlacementSpawn();
                break;

            case PlacementEntityType.Exit:
                ConfirmInteriorExitPlacementSpawn();
                break;

            case PlacementEntityType.Npc:
            default:
                ConfirmNpcPlacementSpawn();
                break;
        }

        _nextPreviewRetryAt = 0;
        CreateOrUpdatePlacementPreview();
    }

    private void ConfirmNpcPlacementSpawn()
    {
        Ped ped = null;
        string currentKey = CurrentPlacementKey();
        ModelIdentity modelIdentity = BuildCurrentModelIdentity();
        WeaponLoadout loadout = _selectedWeaponLoadout.Clone();

        if (Entity.Exists(_placementPreviewPed) &&
            _placementPreviewType == PlacementEntityType.Npc &&
            string.Equals(_placementPreviewKey, currentKey, StringComparison.Ordinal))
        {
            ped = _placementPreviewPed;
            _placementPreviewPed = null;
            _placementPreviewKey = string.Empty;
            RestorePreviewAsRealPed(ped);
        }
        else
        {
            ped = CreatePedFromModelIdentity(modelIdentity, _placementSpawnPoint, _placementHeading);
        }

        if (!Entity.Exists(ped)) return;

        RegisterSpawnedNpc(
            ped,
            _selectedBehavior,
            true,
            true,
            modelIdentity,
            loadout,
            _selectedHealth,
            _selectedArmor,
            _placementSpawnPoint,
            _placementHeading,
            _selectedPatrolRadius,
            _selectedAutoRespawn);
    }

    private void ConfirmVehiclePlacementSpawn()
    {
        Vehicle vehicle = null;
        string currentKey = CurrentPlacementKey();
        VehicleIdentity identity = BuildCurrentVehicleIdentity();

        if (Entity.Exists(_placementPreviewVehicle) &&
            _placementPreviewType == PlacementEntityType.Vehicle &&
            string.Equals(_placementPreviewKey, currentKey, StringComparison.Ordinal))
        {
            vehicle = _placementPreviewVehicle;
            _placementPreviewVehicle = null;
            _placementPreviewKey = string.Empty;
            RestorePreviewAsRealVehicle(vehicle);
        }
        else
        {
            vehicle = CreateVehicleFromIdentity(identity, _placementSpawnPoint, _placementHeading);
        }

        if (!Entity.Exists(vehicle)) return;

        RegisterPlacedVehicle(vehicle, identity, _placementSpawnPoint, _placementHeading, true, _selectedAutoRespawn);
    }

    private void ConfirmObjectPlacementSpawn()
    {
        Prop prop = null;
        string currentKey = CurrentPlacementKey();
        ObjectIdentity identity = BuildCurrentObjectIdentity();

        if (Entity.Exists(_placementPreviewProp) &&
            _placementPreviewType == PlacementEntityType.Object &&
            string.Equals(_placementPreviewKey, currentKey, StringComparison.Ordinal))
        {
            prop = _placementPreviewProp;
            _placementPreviewProp = null;
            _placementPreviewKey = string.Empty;
            RestorePreviewAsRealObject(prop);
        }
        else
        {
            prop = CreatePropFromIdentity(identity, _placementSpawnPoint, _placementHeading);
        }

        if (!Entity.Exists(prop)) return;

        RegisterPlacedObject(prop, identity, _placementSpawnPoint, _placementHeading, true, _selectedAutoRespawn);
    }

    private void DrawPlacementMarker()
    {
        World.DrawMarker(
            MarkerType.VerticalCylinder,
            _placementHitPoint + new Vector3(0.0f, 0.0f, 0.05f),
            Vector3.Zero,
            Vector3.Zero,
            new Vector3(0.65f, 0.65f, 0.25f),
            Color.FromArgb(170, 255, 40, 40));

        World.DrawMarker(
            MarkerType.DebugSphere,
            _placementSpawnPoint,
            Vector3.Zero,
            Vector3.Zero,
            new Vector3(0.28f, 0.28f, 0.28f),
            Color.FromArgb(220, 255, 255, 255));
    }

    private void DrawPlacementHud()
    {
        DrawRect(0, 0, 1280, 54, Color.FromArgb(155, 0, 0, 0));

        DrawText(
            "Placement camera - type: " + PlacementTypeDisplayName(_selectedPlacementType) + " | clic gauche/Entree: placer | clic droit/Echap: quitter | A/E: tourner",
            18,
            12,
            0.32f,
            Color.White,
            false,
            true);

        DrawText("+", 640, 346, 0.55f, Color.White, true, true);

        string info = _placementHasHit
            ? "Position: " + FormatVector(_placementHitPoint) + " | Direction: " + NormalizeHeading(_placementHeading).ToString("0", CultureInfo.InvariantCulture) + "°"
            : "Aucune surface detectee";

        DrawRect(0, 676, 1280, 44, Color.FromArgb(155, 0, 0, 0));
        DrawText(info, 18, 688, 0.32f, Color.White, false, true);
    }

    private static bool IsDisabledControlJustPressed(GtaControl control)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)control);
    }

    private string CurrentModelKey()
    {
        if (_modelCategories != null && _modelCategories.Count > 0)
        {
            ModelOption option = CurrentModelOption();

            if (option.IsCustom)
            {
                return "custom:" + (_customModelName ?? string.Empty).Trim().ToLowerInvariant();
            }

            return "hash:" + option.Hash.ToString(CultureInfo.InvariantCulture);
        }

        if (_modelOptions == null || _modelOptions.Count == 0)
        {
            return "none";
        }

        _selectedModelIndex = Wrap(_selectedModelIndex, _modelOptions.Count);
        ModelOption legacyOption = _modelOptions[_selectedModelIndex];

        if (legacyOption.IsCustom)
        {
            return "custom:" + (_customModelName ?? string.Empty).Trim().ToLowerInvariant();
        }

        return "hash:" + legacyOption.Hash.ToString(CultureInfo.InvariantCulture);
    }

    private string CurrentPlacementKey()
    {
        switch (_selectedPlacementType)
        {
            case PlacementEntityType.Vehicle:
                return "vehicle:" + EnumToIntHash(CurrentVehicleOption().Hash).ToString(CultureInfo.InvariantCulture);

            case PlacementEntityType.Object:
                return "object:" + (CurrentObjectOption().ModelName ?? string.Empty).Trim().ToLowerInvariant();

            case PlacementEntityType.Entrance:
                return "entrance:" + (CurrentInteriorOption().Id ?? string.Empty).Trim().ToLowerInvariant();

            case PlacementEntityType.Exit:
                return _activeInteriorSession != null && _activeInteriorSession.Interior != null
                    ? "exit:" + (_activeInteriorSession.Interior.Id ?? string.Empty).Trim().ToLowerInvariant()
                    : "exit:none";

            case PlacementEntityType.Npc:
            default:
                return "npc:" + CurrentModelKey();
        }
    }

    private void SaveCurrentSetupWithPrompt()
    {
        SetMenuVisible(false);

        string input = Game.GetUserInput(_lastSaveFileName, 64);

        SetMenuVisible(true);

        if (string.IsNullOrWhiteSpace(input))
        {
            ShowStatus("Sauvegarde annulee.", 2500);
            return;
        }

        _lastSaveFileName = NormalizeSaveFileName(input);
        SaveCurrentSetup(_lastSaveFileName);
    }

    private void LoadSetupWithPrompt()
    {
        SetMenuVisible(false);

        string input = Game.GetUserInput(_lastSaveFileName, 64);

        SetMenuVisible(true);

        if (string.IsNullOrWhiteSpace(input))
        {
            ShowStatus("Chargement annule.", 2500);
            return;
        }

        _lastSaveFileName = NormalizeSaveFileName(input);
        LoadSetup(_lastSaveFileName);
    }

    private void SaveCurrentSetup(string fileName)
    {
        string normalizedFileName = NormalizeSaveFileName(fileName);
        string tempPath = null;

        try
        {
            string saveDirectory = GetSaveDirectory();
            Directory.CreateDirectory(saveDirectory);

            string path = Path.Combine(saveDirectory, normalizedFileName);
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = System.Text.Encoding.UTF8
            };

            int savedNpcs = 0;
            int savedVehicles = 0;
            int savedObjects = 0;
            int savedPortals = 0;

            using (XmlWriter writer = XmlWriter.Create(tempPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("DonJEnemySpawnerSave");
                writer.WriteAttributeString("version", "5");
                writer.WriteAttributeString("createdUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("saveFile", normalizedFileName);
                writer.WriteAttributeString("saveDirectory", saveDirectory);

                writer.WriteStartElement("Npcs");

                for (int i = 0; i < _spawnedNpcs.Count; i++)
                {
                    SpawnedNpc npc = _spawnedNpcs[i];

                    if (npc == null || npc.ModelIdentity == null || npc.Loadout == null)
                    {
                        continue;
                    }

                    bool liveNpc = Entity.Exists(npc.Ped) && !npc.Ped.IsDead;

                    if (!liveNpc && !npc.AutoRespawn)
                    {
                        continue;
                    }

                    writer.WriteStartElement("Npc");

                    writer.WriteAttributeString("behavior", npc.BaseBehavior.ToString());
                    writer.WriteAttributeString("currentBehavior", npc.Behavior.ToString());

                    writer.WriteAttributeString("modelIsCustom", npc.ModelIdentity.IsCustom.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("modelName", npc.ModelIdentity.Name ?? string.Empty);
                    writer.WriteAttributeString("modelHash", npc.ModelIdentity.Hash.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("modelDisplayName", npc.ModelIdentity.DisplayName ?? string.Empty);

                    writer.WriteAttributeString("x", npc.HomePosition.X.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", npc.HomePosition.Y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", npc.HomePosition.Z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("heading", npc.HomeHeading.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("patrolRadius", npc.PatrolRadius.ToString(CultureInfo.InvariantCulture));

                    int saveHealth = liveNpc ? npc.Ped.Health : npc.SavedMaxHealth;
                    int saveArmor = liveNpc ? npc.Ped.Armor : npc.SavedArmor;

                    writer.WriteAttributeString("maxHealth", npc.SavedMaxHealth.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("health", saveHealth.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("armor", saveArmor.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("autoRespawn", npc.AutoRespawn.ToString(CultureInfo.InvariantCulture));

                    WriteLoadoutXml(writer, npc.Loadout);

                    writer.WriteEndElement();
                    savedNpcs++;
                }

                writer.WriteEndElement();

                writer.WriteStartElement("Vehicles");

                for (int i = 0; i < _placedVehicles.Count; i++)
                {
                    PlacedVehicle placed = _placedVehicles[i];

                    if (placed == null || placed.Identity == null)
                    {
                        continue;
                    }

                    /*
                     * Les véhicules runtime comme la limousine appelée avec L ne doivent jamais
                     * être écrits dans les sauvegardes utilisateur.
                     */
                    if (!placed.PersistentInSave)
                    {
                        continue;
                    }

                    bool liveVehicle = Entity.Exists(placed.Vehicle);

                    if (!liveVehicle && !placed.AutoRespawn)
                    {
                        continue;
                    }

                    Vector3 savePosition = placed.AutoRespawn ? placed.RespawnPosition : placed.Vehicle.Position;
                    float saveHeading = placed.AutoRespawn ? placed.RespawnHeading : placed.Vehicle.Heading;

                    writer.WriteStartElement("Vehicle");
                    writer.WriteAttributeString("name", placed.Identity.Name ?? string.Empty);
                    writer.WriteAttributeString("hash", placed.Identity.Hash.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("displayName", placed.Identity.DisplayName ?? string.Empty);
                    writer.WriteAttributeString("x", savePosition.X.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", savePosition.Y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", savePosition.Z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("heading", saveHeading.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("autoRespawn", placed.AutoRespawn.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                    savedVehicles++;
                }

                writer.WriteEndElement();

                writer.WriteStartElement("Objects");

                for (int i = 0; i < _placedObjects.Count; i++)
                {
                    PlacedObject placed = _placedObjects[i];

                    if (placed == null || placed.Identity == null)
                    {
                        continue;
                    }

                    bool liveObject = Entity.Exists(placed.Prop);

                    if (!liveObject && !placed.AutoRespawn)
                    {
                        continue;
                    }

                    Vector3 savePosition = placed.AutoRespawn ? placed.RespawnPosition : placed.Prop.Position;
                    float saveHeading = placed.AutoRespawn ? placed.RespawnHeading : placed.Prop.Heading;

                    writer.WriteStartElement("Object");
                    writer.WriteAttributeString("modelName", placed.Identity.ModelName ?? string.Empty);
                    writer.WriteAttributeString("displayName", placed.Identity.DisplayName ?? string.Empty);
                    writer.WriteAttributeString("x", savePosition.X.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", savePosition.Y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", savePosition.Z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("heading", saveHeading.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("autoRespawn", placed.AutoRespawn.ToString(CultureInfo.InvariantCulture));
                    WriteObjectInteractionXmlAttributes(writer, placed.Identity);
                    writer.WriteEndElement();
                    savedObjects++;
                }

                writer.WriteEndElement();

                savedPortals = WriteInteriorPortalsXml(writer);

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            ReplaceFileAtomically(tempPath, path);
            tempPath = null;

            _lastSaveFileName = normalizedFileName;
            PersistLastSaveFileNameSafe(normalizedFileName);

            ShowStatus(
                "Sauvegarde OK: " + normalizedFileName +
                " | NPC " + savedNpcs.ToString(CultureInfo.InvariantCulture) +
                " | vehicules " + savedVehicles.ToString(CultureInfo.InvariantCulture) +
                " | objets " + savedObjects.ToString(CultureInfo.InvariantCulture) +
                " | portails " + savedPortals.ToString(CultureInfo.InvariantCulture),
                6000);
        }
        catch (Exception ex)
        {
            LogException("SaveCurrentSetup", ex);
            ShowStatus("Erreur sauvegarde: " + ex.Message, 7000);
        }
        finally
        {
            DeleteFileIfExistsSafe(tempPath);
        }
    }

    private void LoadSetup(string fileName)
    {
        string normalizedFileName = NormalizeSaveFileName(fileName);

        try
        {
            string path;
            string searchedDirectory;

            if (!TryResolveSavePathForLoad(normalizedFileName, out path, out searchedDirectory))
            {
                LogWarning("LoadSetup", "Fichier introuvable: " + normalizedFileName + " | dossier: " + searchedDirectory);
                ShowStatus(
                    "Fichier introuvable: " + normalizedFileName + " | dossier: " + CompactPathForStatus(searchedDirectory),
                    7000);
                return;
            }

            XmlDocument doc = new XmlDocument();

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                doc.Load(stream);
            }

            XmlNodeList npcNodes = doc.SelectNodes("/DonJEnemySpawnerSave/Npcs/Npc");

            if (npcNodes == null || npcNodes.Count == 0)
            {
                npcNodes = doc.SelectNodes("/DonJEnemySpawnerSave/Npc");
            }

            XmlNodeList vehicleNodes = doc.SelectNodes("/DonJEnemySpawnerSave/Vehicles/Vehicle");
            XmlNodeList objectNodes = doc.SelectNodes("/DonJEnemySpawnerSave/Objects/Object");

            CleanAllSpawnedNpcs();
            CleanAllPlacedVehicles();
            CleanAllPlacedObjects();

            int loadedNpcs = 0;
            int loadedVehicles = 0;
            int loadedObjects = 0;
            int loadedPortals = 0;

            if (npcNodes != null)
            {
                foreach (XmlNode node in npcNodes)
                {
                    ModelIdentity identity = ReadModelIdentityXml(node);
                    WeaponLoadout loadout = ReadLoadoutXml(node);

                    NpcBehavior behavior = ReadEnumAttribute(node, "behavior", NpcBehavior.Neutral);

                    Vector3 position = new Vector3(
                        ReadFloatAttribute(node, "x", 0.0f),
                        ReadFloatAttribute(node, "y", 0.0f),
                        ReadFloatAttribute(node, "z", 0.0f));

                    float heading = ReadFloatAttribute(node, "heading", 0.0f);
                    float patrolRadius = ReadFloatAttribute(node, "patrolRadius", _selectedPatrolRadius);
                    int maxHealth = ReadIntAttribute(node, "maxHealth", _selectedHealth);
                    int health = ReadIntAttribute(node, "health", maxHealth);
                    int armor = ReadIntAttribute(node, "armor", _selectedArmor);
                    bool autoRespawn = ReadBoolAttribute(node, "autoRespawn", false);

                    Ped ped = CreatePedFromModelIdentity(identity, position, heading);

                    if (!Entity.Exists(ped))
                    {
                        continue;
                    }

                    SpawnedNpc spawned = RegisterSpawnedNpc(
                        ped,
                        behavior,
                        true,
                        false,
                        identity,
                        loadout,
                        maxHealth,
                        armor,
                        position,
                        heading,
                        patrolRadius,
                        autoRespawn);

                    if (spawned != null && Entity.Exists(spawned.Ped))
                    {
                        spawned.Ped.Health = Clamp(health, MinHealth, MaxHealth);
                        spawned.Ped.Armor = Clamp(armor, MinArmor, MaxArmor);
                        loadedNpcs++;
                    }
                }
            }

            if (vehicleNodes != null)
            {
                foreach (XmlNode node in vehicleNodes)
                {
                    VehicleIdentity identity = ReadVehicleIdentityXml(node);

                    Vector3 position = new Vector3(
                        ReadFloatAttribute(node, "x", 0.0f),
                        ReadFloatAttribute(node, "y", 0.0f),
                        ReadFloatAttribute(node, "z", 0.0f));

                    float heading = ReadFloatAttribute(node, "heading", 0.0f);
                    bool autoRespawn = ReadBoolAttribute(node, "autoRespawn", false);

                    Vehicle vehicle = CreateVehicleFromIdentity(identity, position, heading);

                    if (!Entity.Exists(vehicle))
                    {
                        continue;
                    }

                    RegisterPlacedVehicle(vehicle, identity, position, heading, false, autoRespawn);
                    loadedVehicles++;
                }
            }

            if (objectNodes != null)
            {
                foreach (XmlNode node in objectNodes)
                {
                    ObjectIdentity identity = ReadObjectIdentityXml(node);

                    Vector3 position = new Vector3(
                        ReadFloatAttribute(node, "x", 0.0f),
                        ReadFloatAttribute(node, "y", 0.0f),
                        ReadFloatAttribute(node, "z", 0.0f));

                    float heading = ReadFloatAttribute(node, "heading", 0.0f);
                    bool autoRespawn = ReadBoolAttribute(node, "autoRespawn", false);

                    Prop prop = CreatePropFromIdentity(identity, position, heading);

                    if (!Entity.Exists(prop))
                    {
                        continue;
                    }

                    RegisterPlacedObject(prop, identity, position, heading, false, autoRespawn);
                    loadedObjects++;
                }
            }

            loadedPortals = LoadInteriorPortalsFromXml(doc);

            _lastSaveFileName = normalizedFileName;
            PersistLastSaveFileNameSafe(normalizedFileName);
            MigrateLoadedSaveToCanonicalLocationSafe(path, normalizedFileName);

            ShowStatus(
                "Chargement OK: " + normalizedFileName +
                " | NPC " + loadedNpcs.ToString(CultureInfo.InvariantCulture) +
                " | vehicules " + loadedVehicles.ToString(CultureInfo.InvariantCulture) +
                " | objets " + loadedObjects.ToString(CultureInfo.InvariantCulture) +
                " | portails " + loadedPortals.ToString(CultureInfo.InvariantCulture),
                6000);
        }
        catch (Exception ex)
        {
            LogException("LoadSetup", ex);
            ShowStatus("Erreur chargement: " + ex.Message, 7000);
        }
    }

    private static void WriteLoadoutXml(XmlWriter writer, WeaponLoadout loadout)
    {
        writer.WriteStartElement("Weapon");

        writer.WriteAttributeString("hash", EnumToIntHash(loadout.Weapon).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("name", loadout.Weapon.ToString());
        writer.WriteAttributeString("ammo", loadout.Ammo.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("tint", loadout.Tint.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("preset", loadout.Preset.ToString());
        writer.WriteAttributeString("extendedClip", loadout.ExtendedClip.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("suppressor", loadout.Suppressor.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("flashlight", loadout.Flashlight.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("grip", loadout.Grip.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("scope", loadout.Scope.ToString());
        writer.WriteAttributeString("muzzle", loadout.Muzzle.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("improvedBarrel", loadout.ImprovedBarrel.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("mk2Ammo", loadout.Mk2Ammo.ToString());

        writer.WriteEndElement();
    }

    private WeaponLoadout ReadLoadoutXml(XmlNode npcNode)
    {
        XmlNode weaponNode = npcNode.SelectSingleNode("Weapon");

        WeaponLoadout loadout = new WeaponLoadout
        {
            Weapon = _selectedWeaponLoadout.Weapon,
            Ammo = 9999,
            Tint = 0,
            Preset = WeaponUpgradePreset.Standard
        };

        if (weaponNode == null)
        {
            return loadout;
        }

        loadout.Weapon = ReadWeaponHashAttribute(weaponNode, "name", "hash", _selectedWeaponLoadout.Weapon);
        loadout.Ammo = ReadIntAttribute(weaponNode, "ammo", 9999);
        loadout.Tint = ReadIntAttribute(weaponNode, "tint", 0);
        loadout.Preset = ReadEnumAttribute(weaponNode, "preset", WeaponUpgradePreset.Standard);
        loadout.ExtendedClip = ReadBoolAttribute(weaponNode, "extendedClip", false);
        loadout.Suppressor = ReadBoolAttribute(weaponNode, "suppressor", false);
        loadout.Flashlight = ReadBoolAttribute(weaponNode, "flashlight", false);
        loadout.Grip = ReadBoolAttribute(weaponNode, "grip", false);
        loadout.Scope = ReadEnumAttribute(weaponNode, "scope", WeaponScopeMode.None);
        loadout.Muzzle = ReadBoolAttribute(weaponNode, "muzzle", false);
        loadout.ImprovedBarrel = ReadBoolAttribute(weaponNode, "improvedBarrel", false);
        loadout.Mk2Ammo = ReadEnumAttribute(weaponNode, "mk2Ammo", WeaponMk2AmmoMode.Standard);

        return loadout;
    }

    private static ModelIdentity ReadModelIdentityXml(XmlNode node)
    {
        bool isCustom = ReadBoolAttribute(node, "modelIsCustom", false);
        string modelName = ReadStringAttribute(node, "modelName", string.Empty);
        int modelHash = ReadIntAttribute(node, "modelHash", 0);
        string displayName = ReadStringAttribute(node, "modelDisplayName", modelName);

        return new ModelIdentity
        {
            IsCustom = isCustom,
            Name = modelName,
            Hash = modelHash,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? modelName : displayName
        };
    }

    private static VehicleIdentity ReadVehicleIdentityXml(XmlNode node)
    {
        string name = ReadStringAttribute(node, "name", string.Empty);
        int hash = ReadIntAttribute(node, "hash", 0);
        string displayName = ReadStringAttribute(node, "displayName", name);

        if (hash == 0 && !string.IsNullOrWhiteSpace(name))
        {
            VehicleHash parsed;

            if (Enum.TryParse(name, true, out parsed))
            {
                hash = EnumToIntHash(parsed);
            }
        }

        return new VehicleIdentity
        {
            Name = name,
            Hash = hash,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName
        };
    }

    private static ObjectIdentity ReadObjectIdentityXml(XmlNode node)
    {
        string modelName = ReadStringAttribute(node, "modelName", string.Empty);
        string displayName = ReadStringAttribute(node, "displayName", modelName);

        ObjectIdentity identity = new ObjectIdentity
        {
            ModelName = modelName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? modelName : displayName,
            InteractionKind = ReadEnumAttribute(node, "interactionKind", ObjectInteractionKind.None),
            CashValue = ReadIntAttribute(node, "cashValue", 0),
            HealAmount = ReadIntAttribute(node, "healAmount", 0),
            ArmorAmount = ReadIntAttribute(node, "armorAmount", 0),
            AmmoAmount = ReadIntAttribute(node, "ammoAmount", 0)
        };

        ApplyDefaultObjectInteractionIfNeeded(identity);
        return identity;
    }

    private void InitializePersistentSaveState()
    {
        try
        {
            _lastSaveFileName = NormalizeSaveFileName(_lastSaveFileName);

            string rememberedFileName = ReadRememberedSaveFileNameSafe();

            if (!string.IsNullOrWhiteSpace(rememberedFileName))
            {
                _lastSaveFileName = NormalizeSaveFileName(rememberedFileName);
            }

            Directory.CreateDirectory(GetSaveDirectory());
        }
        catch
        {
            _lastSaveFileName = NormalizeSaveFileName(_lastSaveFileName);
        }
    }

    private string GetSaveDirectory()
    {
        List<string> candidates = BuildSaveDirectoryCandidates();

        for (int i = 0; i < candidates.Count; i++)
        {
            string candidate = candidates[i];

            if (CanUseDirectoryForSave(candidate))
            {
                return candidate;
            }
        }

        string fallbackBaseDirectory = GetAssemblyDirectorySafe();

        if (string.IsNullOrWhiteSpace(fallbackBaseDirectory))
        {
            fallbackBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        return Path.Combine(fallbackBaseDirectory, SaveFolderName);
    }

    private string GetSavePath(string fileName)
    {
        return Path.Combine(GetSaveDirectory(), NormalizeSaveFileName(fileName));
    }

    private bool TryResolveSavePathForLoad(string fileName, out string resolvedPath, out string searchedDirectory)
    {
        string normalizedFileName = NormalizeSaveFileName(fileName);
        List<string> searchDirectories = GetSaveSearchDirectories();

        for (int i = 0; i < searchDirectories.Count; i++)
        {
            string directory = searchDirectories[i];
            string candidate = Path.Combine(directory, normalizedFileName);

            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                searchedDirectory = directory;
                return true;
            }

            string backupCandidate = candidate + ".bak";

            if (File.Exists(backupCandidate))
            {
                resolvedPath = backupCandidate;
                searchedDirectory = directory;
                return true;
            }
        }

        searchedDirectory = searchDirectories.Count > 0 ? searchDirectories[0] : GetSaveDirectory();
        resolvedPath = Path.Combine(searchedDirectory, normalizedFileName);
        return false;
    }

    private List<string> GetSaveSearchDirectories()
    {
        List<string> result = new List<string>();

        AddUniqueDirectory(result, GetSaveDirectory());

        List<string> candidates = BuildSaveDirectoryCandidates();

        for (int i = 0; i < candidates.Count; i++)
        {
            AddUniqueDirectory(result, candidates[i]);
        }

        return result;
    }

    private List<string> BuildSaveDirectoryCandidates()
    {
        List<string> result = new List<string>();

        AddUniqueDirectory(result, GetConfiguredSaveDirectorySafe());

        List<string> scriptDirectories = BuildScriptDirectoryCandidates();

        for (int i = 0; i < scriptDirectories.Count; i++)
        {
            AddUniqueDirectory(result, Path.Combine(scriptDirectories[i], SaveFolderName));
        }

        AddUniqueDirectory(result, GetDocumentsSaveDirectorySafe());
        AddUniqueDirectory(result, GetLocalAppDataSaveDirectorySafe());
        AddUniqueDirectory(result, GetLegacyAssemblySaveDirectorySafe());

        string appDomainBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(appDomainBaseDirectory))
        {
            AddUniqueDirectory(result, Path.Combine(appDomainBaseDirectory, SaveFolderName));
        }

        return result;
    }

    private List<string> BuildScriptDirectoryCandidates()
    {
        List<string> result = new List<string>();

        AddUniqueDirectory(result, GetProcessScriptsDirectorySafe());
        AddScriptsDirectoryCandidateFromBase(result, AppDomain.CurrentDomain.BaseDirectory);
        AddScriptsDirectoryCandidateFromBase(result, GetAssemblyDirectorySafe());

        if (LooksLikeGtaRoot(DefaultEnhancedGtaRoot))
        {
            AddUniqueDirectory(result, Path.Combine(DefaultEnhancedGtaRoot, "Scripts"));
        }

        return result;
    }

    private static void AddScriptsDirectoryCandidateFromBase(List<string> result, string baseDirectory)
    {
        if (result == null || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return;
        }

        string fullBaseDirectory = TryGetFullPath(baseDirectory);

        if (string.IsNullOrWhiteSpace(fullBaseDirectory))
        {
            return;
        }

        if (LooksLikeScriptsDirectory(fullBaseDirectory))
        {
            AddUniqueDirectory(result, fullBaseDirectory);
        }

        if (LooksLikeGtaRoot(fullBaseDirectory))
        {
            AddUniqueDirectory(result, Path.Combine(fullBaseDirectory, "Scripts"));
        }

        string scriptsChild = Path.Combine(fullBaseDirectory, "Scripts");

        if (Directory.Exists(scriptsChild) && (LooksLikeGtaRoot(fullBaseDirectory) || LooksLikeScriptsDirectory(scriptsChild)))
        {
            AddUniqueDirectory(result, scriptsChild);
        }

        string parentDirectory = GetParentDirectorySafe(fullBaseDirectory);

        if (LooksLikeGtaRoot(parentDirectory))
        {
            AddUniqueDirectory(result, Path.Combine(parentDirectory, "Scripts"));
        }
    }

    private static string GetProcessScriptsDirectorySafe()
    {
        try
        {
            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
            {
                if (process == null || process.MainModule == null)
                {
                    return string.Empty;
                }

                string executablePath = process.MainModule.FileName;

                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return string.Empty;
                }

                string rootDirectory = Path.GetDirectoryName(executablePath);
                string executableName = Path.GetFileNameWithoutExtension(executablePath);

                if (string.IsNullOrWhiteSpace(rootDirectory))
                {
                    return string.Empty;
                }

                if ((executableName != null && executableName.StartsWith("GTA5", StringComparison.OrdinalIgnoreCase)) || LooksLikeGtaRoot(rootDirectory))
                {
                    return Path.Combine(rootDirectory, "Scripts");
                }
            }
        }
        catch
        {
            // Le chemin du process peut etre refuse par Windows; les autres candidats prennent le relais.
        }

        return string.Empty;
    }

    private static string GetConfiguredSaveDirectorySafe()
    {
        try
        {
            string configuredDirectory = Environment.GetEnvironmentVariable(SaveDirectoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configuredDirectory) ? string.Empty : configuredDirectory.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetDocumentsSaveDirectorySafe()
    {
        try
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(documents))
            {
                return string.Empty;
            }

            return Path.Combine(documents, "Rockstar Games", "GTA V Enhanced", SaveFolderName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLocalAppDataSaveDirectorySafe()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return string.Empty;
            }

            return Path.Combine(localAppData, "DonJEnemySpawner", "Saves");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLegacyAssemblySaveDirectorySafe()
    {
        string assemblyDirectory = GetAssemblyDirectorySafe();
        return string.IsNullOrWhiteSpace(assemblyDirectory) ? string.Empty : Path.Combine(assemblyDirectory, SaveFolderName);
    }

    private static string GetAssemblyDirectorySafe()
    {
        try
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;

            if (string.IsNullOrWhiteSpace(assemblyLocation))
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }

            string directory = Path.GetDirectoryName(assemblyLocation);
            return string.IsNullOrWhiteSpace(directory) ? AppDomain.CurrentDomain.BaseDirectory : directory;
        }
        catch
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    private static bool LooksLikeGtaRoot(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.Combine(directory, "GTA5_Enhanced.exe")) ||
                   File.Exists(Path.Combine(directory, "GTA5.exe")) ||
                   File.Exists(Path.Combine(directory, "ScriptHookV.dll")) ||
                   File.Exists(Path.Combine(directory, "NIBScriptHookVDotNet.asi"));
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeScriptsDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string directoryName = new DirectoryInfo(directory).Name;

            if (string.Equals(directoryName, "Scripts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (File.Exists(Path.Combine(directory, "DonJCustomNpcPlacer.ENdll")) ||
                File.Exists(Path.Combine(directory, "DonJCustomNpcPlacer.dll")) ||
                File.Exists(Path.Combine(directory, "DonJEnemySpawner.ENdll")) ||
                File.Exists(Path.Combine(directory, "DonJEnemySpawner.dll")))
            {
                return true;
            }

            return Directory.Exists(directory) && Directory.GetFiles(directory, "*.ENdll").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanUseDirectoryForSave(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);

            string testPath = Path.Combine(directory, ".write_test_" + Guid.NewGuid().ToString("N") + ".tmp");

            File.WriteAllText(testPath, "ok", System.Text.Encoding.UTF8);
            File.Delete(testPath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PersistLastSaveFileNameSafe(string fileName)
    {
        try
        {
            string normalizedFileName = NormalizeSaveFileName(fileName);
            string markerPath = Path.Combine(GetSaveDirectory(), LastSaveFileMarkerName);
            WriteTextFileAtomically(markerPath, normalizedFileName + Environment.NewLine);
        }
        catch
        {
            // Le marqueur ne doit jamais bloquer une vraie sauvegarde XML.
        }
    }

    private string ReadRememberedSaveFileNameSafe()
    {
        List<string> searchDirectories = GetSaveSearchDirectories();

        for (int i = 0; i < searchDirectories.Count; i++)
        {
            try
            {
                string markerPath = Path.Combine(searchDirectories[i], LastSaveFileMarkerName);

                if (!File.Exists(markerPath))
                {
                    continue;
                }

                string remembered = File.ReadAllText(markerPath, System.Text.Encoding.UTF8).Trim();

                if (!string.IsNullOrWhiteSpace(remembered))
                {
                    return NormalizeSaveFileName(remembered);
                }
            }
            catch
            {
                // Je continue avec les autres dossiers possibles.
            }
        }

        return FindNewestXmlSaveFileNameSafe(searchDirectories);
    }

    private static string FindNewestXmlSaveFileNameSafe(List<string> searchDirectories)
    {
        string newestFileName = string.Empty;
        DateTime newestWriteTimeUtc = DateTime.MinValue;

        if (searchDirectories == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < searchDirectories.Count; i++)
        {
            string directory = searchDirectories[i];

            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly);

                for (int j = 0; j < files.Length; j++)
                {
                    string file = files[j];
                    string fileName = Path.GetFileName(file);

                    if (string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    DateTime writeTimeUtc = File.GetLastWriteTimeUtc(file);

                    if (writeTimeUtc > newestWriteTimeUtc)
                    {
                        newestWriteTimeUtc = writeTimeUtc;
                        newestFileName = fileName;
                    }
                }
            }
            catch
            {
                // Un dossier inaccessible ne doit pas empecher la lecture du prochain.
            }
        }

        return newestFileName;
    }

    private void MigrateLoadedSaveToCanonicalLocationSafe(string loadedPath, string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(loadedPath) || !File.Exists(loadedPath))
            {
                return;
            }

            string canonicalPath = GetSavePath(fileName);

            if (IsSamePath(loadedPath, canonicalPath) || File.Exists(canonicalPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath));
            File.Copy(loadedPath, canonicalPath, false);
        }
        catch
        {
            // Migration opportuniste seulement: le chargement a deja reussi.
        }
    }

    private static void WriteTextFileAtomically(string path, string content)
    {
        string tempPath = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, content ?? string.Empty, System.Text.Encoding.UTF8);
            ReplaceFileAtomically(tempPath, path);
            tempPath = null;
        }
        finally
        {
            DeleteFileIfExistsSafe(tempPath);
        }
    }

    private static void ReplaceFileAtomically(string tempPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(tempPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

        if (!File.Exists(targetPath))
        {
            File.Move(tempPath, targetPath);
            return;
        }

        string backupPath = targetPath + ".bak";

        // Je refuse un fallback Delete+Move : si Replace n'est pas supporté,
        // le primaire existant reste intact et l'échec remonte à l'appelant.
        File.Replace(tempPath, targetPath, backupPath, true);
    }

    private static void DeleteFileIfExistsSafe(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Rien a faire: nettoyage best-effort.
        }
    }

    private static void AddUniqueDirectory(List<string> directories, string directory)
    {
        if (directories == null || string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string fullDirectory = TryGetFullPath(directory);

        if (string.IsNullOrWhiteSpace(fullDirectory))
        {
            return;
        }

        for (int i = 0; i < directories.Count; i++)
        {
            if (IsSamePath(directories[i], fullDirectory))
            {
                return;
            }
        }

        directories.Add(fullDirectory);
    }

    private static bool IsSamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(TryGetFullPath(left), TryGetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string GetParentDirectorySafe(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            DirectoryInfo parent = Directory.GetParent(directory);
            return parent == null ? string.Empty : parent.FullName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CompactPathForStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string compact = path.Trim();

        if (compact.Length <= 58)
        {
            return compact;
        }

        return "..." + compact.Substring(compact.Length - 55);
    }

    private static string NormalizeSaveFileName(string input)
    {
        string raw = (input ?? string.Empty).Replace("\0", string.Empty).Trim();
        string safe;

        try
        {
            safe = Path.GetFileName(raw);
        }
        catch
        {
            safe = raw;
        }

        safe = (safe ?? string.Empty).Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        safe = safe.Replace('/', '_').Replace('\\', '_');

        if (string.IsNullOrWhiteSpace(safe) || string.Equals(safe, ".", StringComparison.Ordinal) || string.Equals(safe, "..", StringComparison.Ordinal))
        {
            safe = "maison.xml";
        }

        if (!safe.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            safe += ".xml";
        }

        if (safe.StartsWith("_", StringComparison.Ordinal))
        {
            // Je réserve tous les noms internes au mod. Une scène saisie comme
            // `_justice_state.xml` devient ainsi un fichier de scène distinct
            // et ne peut ni écraser ni charger l'état judiciaire.
            safe = "scene" + safe;
        }

        if (safe.Length > MaxSaveFileNameLength)
        {
            string extension = Path.GetExtension(safe);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(safe);

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".xml";
            }

            int allowedNameLength = Math.Max(1, MaxSaveFileNameLength - extension.Length);

            if (nameWithoutExtension.Length > allowedNameLength)
            {
                nameWithoutExtension = nameWithoutExtension.Substring(0, allowedNameLength);
            }

            safe = nameWithoutExtension + extension;
        }

        return safe;
    }

    private static List<ModelOption> BuildAllModelOptions()
    {
        List<ModelOption> result = new List<ModelOption>();
        HashSet<int> seen = new HashSet<int>();

        result.Add(new ModelOption
        {
            DisplayName = "Custom",
            IsCustom = true,
            Hash = 0
        });

        foreach (PedHash pedHash in Enum.GetValues(typeof(PedHash)))
        {
            int hash = EnumToIntHash(pedHash);

            if (seen.Contains(hash))
            {
                continue;
            }

            seen.Add(hash);

            string name = pedHash.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new ModelOption
            {
                DisplayName = name,
                IsCustom = false,
                Hash = hash
            });
        }

        ModelOption custom = result[0];

        List<ModelOption> sorted = result
            .Skip(1)
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sorted.Insert(0, custom);
        return sorted;
    }

    private static List<ModelCategory> BuildModelCategories(List<ModelOption> all)
    {
        List<ModelCategory> categories = new List<ModelCategory>();

        AddModelCategory(categories, "Custom / Add-on", all, m => m.IsCustom);
        AddModelCategory(categories, "Securite / Police / Militaire", all, m => IsSecurityModelName(m.DisplayName));
        AddModelCategory(categories, "Gangs / Criminels", all, m => IsGangModelName(m.DisplayName));
        AddModelCategory(categories, "Multiplayer / Online", all, m => IsMultiplayerModelName(m.DisplayName));
        AddModelCategory(categories, "Scenario / Services", all, m => IsScenarioServiceModelName(m.DisplayName));
        AddModelCategory(categories, "Civils hommes", all, m => IsMaleAmbientModelName(m.DisplayName));
        AddModelCategory(categories, "Civils femmes", all, m => IsFemaleAmbientModelName(m.DisplayName));
        AddModelCategory(categories, "Story / Cutscene", all, m => IsStoryModelName(m.DisplayName));
        AddModelCategory(categories, "Animaux", all, m => IsAnimalModelName(m.DisplayName));
        AddModelCategory(categories, "Tous les PNJ", all, m => true);

        return categories;
    }

    private static void AddModelCategory(List<ModelCategory> categories, string name, List<ModelOption> all, Func<ModelOption, bool> predicate)
    {
        List<ModelOption> options = all
            .Where(predicate)
            .OrderBy(m => m.IsCustom ? "000_" : m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        categories.Add(new ModelCategory
        {
            Name = name,
            Options = options
        });
    }

    private static List<WeaponOption> BuildAllWeaponOptions()
    {
        List<WeaponOption> result = new List<WeaponOption>();
        HashSet<int> seen = new HashSet<int>();

        foreach (WeaponHash weaponHash in Enum.GetValues(typeof(WeaponHash)))
        {
            int hash = EnumToIntHash(weaponHash);

            if (seen.Contains(hash))
            {
                continue;
            }

            seen.Add(hash);

            string name = weaponHash.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new WeaponOption
            {
                DisplayName = name,
                Hash = weaponHash
            });
        }

        return result
            .OrderBy(w => w.DisplayName == "Unarmed" ? "000_" : w.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<WeaponCategory> BuildWeaponCategories(List<WeaponOption> all)
    {
        List<WeaponCategory> categories = new List<WeaponCategory>();

        AddWeaponCategory(categories, "Sans arme", all, w => w.Hash == WeaponHash.Unarmed);
        AddWeaponCategory(categories, "Pistolets", all, w => IsPistolLikeWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "SMG / PDW", all, w => IsSmgWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Fusils d'assaut", all, w => IsAssaultRifleWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Fusils a pompe", all, w => w.DisplayName.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0);
        AddWeaponCategory(categories, "Snipers / Marksman", all, w => IsSniperWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Mitrailleuses", all, w => IsMachineGunWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Lourdes / Launchers", all, w => IsHeavyWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Projectiles", all, w => IsThrowableWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Melee", all, w => IsMeleeWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Speciales / Divers", all, w => IsSpecialWeaponName(w.DisplayName));
        AddWeaponCategory(categories, "Toutes les armes", all, w => true);

        return categories;
    }

    private static void AddWeaponCategory(List<WeaponCategory> categories, string name, List<WeaponOption> all, Func<WeaponOption, bool> predicate)
    {
        List<WeaponOption> options = all
            .Where(predicate)
            .OrderBy(w => w.DisplayName == "Unarmed" ? "000_" : w.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        categories.Add(new WeaponCategory
        {
            Name = name,
            Options = options
        });
    }

    private static List<VehicleOption> BuildAllVehicleOptions()
    {
        List<VehicleOption> result = new List<VehicleOption>();
        HashSet<int> seen = new HashSet<int>();

        foreach (VehicleHash vehicleHash in Enum.GetValues(typeof(VehicleHash)))
        {
            int hash = EnumToIntHash(vehicleHash);

            if (seen.Contains(hash))
            {
                continue;
            }

            seen.Add(hash);

            string name = vehicleHash.ToString();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new VehicleOption
            {
                DisplayName = name,
                Hash = vehicleHash
            });
        }

        return result
            .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<VehicleCategory> BuildVehicleCategories(List<VehicleOption> all)
    {
        List<VehicleCategory> categories = new List<VehicleCategory>();

        AddVehicleCategory(categories, "Sport / Super", all, v => IsSportsVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Berlines / Coupes", all, v => IsCarVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "SUV / 4x4", all, v => IsSuvVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Motos", all, v => IsMotorcycleVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Police / Secours", all, v => IsEmergencyVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Militaire", all, v => IsMilitaryVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Utilitaires / Vans", all, v => IsUtilityVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Camions", all, v => IsTruckVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Avions / Helicos", all, v => IsAircraftVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Bateaux", all, v => IsBoatVehicleName(v.DisplayName));
        AddVehicleCategory(categories, "Tous les vehicules", all, v => true);

        return categories;
    }

    private static void AddVehicleCategory(List<VehicleCategory> categories, string name, List<VehicleOption> all, Func<VehicleOption, bool> predicate)
    {
        List<VehicleOption> options = all
            .Where(predicate)
            .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        categories.Add(new VehicleCategory
        {
            Name = name,
            Options = options
        });
    }

    private static List<ObjectOption> BuildAllObjectOptions()
    {
        return new List<ObjectOption>
        {
            Obj("Cone orange", "prop_mp_cone_01", ObjectPlacementCategory.Securite),
            Obj("Cone de chantier", "prop_roadcone02a", ObjectPlacementCategory.Securite),
            Obj("Barriere police", "prop_barrier_work05", ObjectPlacementCategory.Securite),
            Obj("Barriere chantier", "prop_barrier_work06a", ObjectPlacementCategory.Securite),
            Obj("Barriere metal", "prop_mp_barrier_02b", ObjectPlacementCategory.Securite),
            Obj("Potelet de securite", "prop_bollard_02a", ObjectPlacementCategory.Securite),
            Obj("Ruban / borne police", "prop_barrier_work01a", ObjectPlacementCategory.Securite),
            Obj("Projecteur securite", "prop_worklight_03b", ObjectPlacementCategory.Securite),

            Obj("Bloc beton", "prop_mp_barrier_01", ObjectPlacementCategory.Couverture),
            Obj("Barriere crash", "prop_barriercrash_03", ObjectPlacementCategory.Couverture),
            Obj("Benne metal", "prop_dumpster_01a", ObjectPlacementCategory.Couverture),
            Obj("Benne verte", "prop_dumpster_02a", ObjectPlacementCategory.Couverture),
            Obj("Palette bois", "prop_pallet_01a", ObjectPlacementCategory.Couverture),
            Obj("Sac de sable", "prop_sandbag_01", ObjectPlacementCategory.Couverture),
            Obj("Pile sacs de sable", "prop_sandbag_02", ObjectPlacementCategory.Couverture),

            // Argent / butin : props decoratifs persistants pour scenes, braquages, planques ou recompenses.
            // Les libelles 10 000$ donnent une valeur RP claire sans toucher aux stats GTA.
            Obj("Billets 10 000$ - liasse plate", "prop_cash_pile_01", ObjectPlacementCategory.ArgentButin),
            Obj("Billets 10 000$ - liasse epaisse", "prop_cash_pile_02", ObjectPlacementCategory.ArgentButin),
            Obj("Billets 10 000$ - tas scene 1", "prop_anim_cash_pile_01", ObjectPlacementCategory.ArgentButin),
            Obj("Billets 10 000$ - tas scene 2", "prop_anim_cash_pile_02", ObjectPlacementCategory.ArgentButin),
            Obj("Billet seul", "p_banknote_s", ObjectPlacementCategory.ArgentButin),
            Obj("Billet un dollar", "p_banknote_onedollar_s", ObjectPlacementCategory.ArgentButin),
            Obj("Paquet de cash", "v_corp_cashpack", ObjectPlacementCategory.ArgentButin),
            Obj("Enveloppe de cash", "prop_cash_envelope_01", ObjectPlacementCategory.ArgentButin),
            Obj("Pochette argent plastique", "prop_poly_bag_money", ObjectPlacementCategory.ArgentButin),
            Obj("Portefeuille argent", "prop_ld_wallet_pickup", ObjectPlacementCategory.ArgentButin),
            Obj("Sac d'argent", "prop_money_bag_01", ObjectPlacementCategory.ArgentButin),
            Obj("Sac de braquage", "p_ld_heist_bag_s", ObjectPlacementCategory.ArgentButin),
            Obj("Sac de braquage pro", "p_ld_heist_bag_s_pro", ObjectPlacementCategory.ArgentButin),
            Obj("Malette cash fermee", "prop_cash_case_01", ObjectPlacementCategory.ArgentButin),
            Obj("Malette cash ouverte", "prop_cash_case_02", ObjectPlacementCategory.ArgentButin),
            Obj("Caisse de cash", "prop_cash_crate_01", ObjectPlacementCategory.ArgentButin),
            Obj("Coffre or", "prop_ld_gold_chest", ObjectPlacementCategory.ArgentButin),
            Obj("Tas cash braquage", "hei_prop_heist_cash_pile", ObjectPlacementCategory.ArgentButin),
            Obj("Chariot cash", "prop_cash_trolly", ObjectPlacementCategory.ArgentButin),

            // Objets utiles pour bases, checkpoints, caches d'armes et scenes de mission.
            Obj("Pack munitions 1", "prop_ld_ammo_pack_01", ObjectPlacementCategory.MaterielTactique),
            Obj("Pack munitions 2", "prop_ld_ammo_pack_02", ObjectPlacementCategory.MaterielTactique),
            Obj("Pack munitions 3", "prop_ld_ammo_pack_03", ObjectPlacementCategory.MaterielTactique),
            Obj("Boite munitions", "prop_box_ammo01a", ObjectPlacementCategory.MaterielTactique),
            Obj("Caisse munitions lourde", "prop_box_ammo03a", ObjectPlacementCategory.MaterielTactique),
            Obj("Set caisse munitions", "prop_box_ammo03a_set", ObjectPlacementCategory.MaterielTactique),
            Obj("Caisse armes bleue", "prop_box_guncase_02a", ObjectPlacementCategory.MaterielTactique),
            Obj("Caisse armes noire", "prop_box_guncase_03a", ObjectPlacementCategory.MaterielTactique),
            Obj("Mallette tactique", "prop_ld_case_01", ObjectPlacementCategory.MaterielTactique),
            Obj("Sac sport", "prop_cs_heist_bag_02", ObjectPlacementCategory.MaterielTactique),
            Obj("Etui arme", "prop_pistol_holster", ObjectPlacementCategory.MaterielTactique),

            Obj("Kit de soin", "prop_ld_health_pack", ObjectPlacementCategory.SoinSurvie),
            Obj("Sac medical", "prop_med_bag_01", ObjectPlacementCategory.SoinSurvie),
            Obj("Bouteille eau", "prop_water_bottle_dark", ObjectPlacementCategory.SoinSurvie),
            Obj("Bouteille club", "ba_prop_club_water_bottle", ObjectPlacementCategory.SoinSurvie),
            Obj("Sac nourriture", "prop_food_bag1", ObjectPlacementCategory.SoinSurvie),
            Obj("Sac shopping provisions", "prop_shopping_bags01", ObjectPlacementCategory.SoinSurvie),

            Obj("Ordinateur portable", "prop_laptop_01a", ObjectPlacementCategory.BureauInformatique),
            Obj("Ordinateur portable ferme", "prop_laptop_02_closed", ObjectPlacementCategory.BureauInformatique),
            Obj("Clavier acces", "prop_ld_keypad_01", ObjectPlacementCategory.BureauInformatique),
            Obj("Clavier acces mural", "prop_ld_keypad_01b", ObjectPlacementCategory.BureauInformatique),
            Obj("Livre / registre", "prop_cs_stock_book", ObjectPlacementCategory.BureauInformatique),
            Obj("Sac papier dossier", "prop_paper_bag_01", ObjectPlacementCategory.BureauInformatique),
            Obj("Television", "prop_tv_flat_01", ObjectPlacementCategory.BureauInformatique),

            Obj("Boite a outils", "prop_tool_box_04", ObjectPlacementCategory.AtelierOutils),
            Obj("Boite a outils 2", "prop_tool_box_06", ObjectPlacementCategory.AtelierOutils),
            Obj("Bidon huile", "prop_oilcan_01a", ObjectPlacementCategory.AtelierOutils),
            Obj("Bidon essence", "prop_jerrycan_01a", ObjectPlacementCategory.AtelierOutils),
            Obj("Bouteille gaz", "prop_gascyl_01a", ObjectPlacementCategory.AtelierOutils),
            Obj("Caisse atelier", "prop_crate_11e", ObjectPlacementCategory.AtelierOutils),

            Obj("Chaise simple", "prop_chair_01a", ObjectPlacementCategory.Mobilier),
            Obj("Chaise bureau", "prop_off_chair_04", ObjectPlacementCategory.Mobilier),
            Obj("Table", "prop_table_04", ObjectPlacementCategory.Mobilier),
            Obj("Banc", "prop_bench_01a", ObjectPlacementCategory.Mobilier),
            Obj("Canape", "prop_couch_01", ObjectPlacementCategory.Mobilier),
            Obj("Lit simple", "prop_bed_01", ObjectPlacementCategory.Mobilier),

            Obj("Caisse bois", "prop_box_wood02a", ObjectPlacementCategory.CaisseStockage),
            Obj("Caisse bois renforcee", "prop_box_wood05a", ObjectPlacementCategory.CaisseStockage),
            Obj("Caisse militaire", "prop_box_ammo03a", ObjectPlacementCategory.CaisseStockage),
            Obj("Pile de cartons", "prop_boxpile_06b", ObjectPlacementCategory.CaisseStockage),
            Obj("Caisse transport", "prop_crate_11e", ObjectPlacementCategory.CaisseStockage),
            Obj("Valise", "prop_ld_suitcase_01", ObjectPlacementCategory.CaisseStockage),
            Obj("Valise rouge", "prop_suitcase_03", ObjectPlacementCategory.CaisseStockage),
            Obj("Carton vetements", "prop_cs_clothes_box", ObjectPlacementCategory.CaisseStockage),

            Obj("Plante bureau", "prop_plant_int_01a", ObjectPlacementCategory.Decoration),
            Obj("Frigo", "prop_fridge_01", ObjectPlacementCategory.Decoration),
            Obj("Machine cafe", "prop_coffee_mac_02", ObjectPlacementCategory.Decoration),
            Obj("Sac shopping", "prop_shopping_bags02", ObjectPlacementCategory.Decoration),
            Obj("Sac a main", "prop_ld_handbag", ObjectPlacementCategory.Decoration),

            Obj("Projecteur chantier", "prop_worklight_03b", ObjectPlacementCategory.Lumiere),
            Obj("Lampe chantier", "prop_worklight_01a", ObjectPlacementCategory.Lumiere),
            Obj("Lampadaire", "prop_streetlight_01", ObjectPlacementCategory.Lumiere),

            Obj("Tente", "prop_skid_tent_01", ObjectPlacementCategory.Exterieur),
            Obj("Parasol", "prop_parasol_01", ObjectPlacementCategory.Exterieur),
            Obj("Table pique-nique", "prop_picnictable_01", ObjectPlacementCategory.Exterieur),
            Obj("Feu de camp", "prop_beach_fire", ObjectPlacementCategory.Exterieur),
            Obj("Antenne mobile", "prop_mobile_mast_1", ObjectPlacementCategory.Exterieur),

            Obj("Extincteur", "prop_fire_exting_1a", ObjectPlacementCategory.Divers),
            Obj("Sac papier petit", "prop_paper_bag_small", ObjectPlacementCategory.Divers),
            Obj("Cigare", "prop_cigar_02", ObjectPlacementCategory.Divers)
        };
    }

    private static ObjectOption Obj(string displayName, string modelName, ObjectPlacementCategory category)
    {
        return new ObjectOption
        {
            DisplayName = displayName,
            ModelName = modelName,
            Category = category,
            InteractionKind = ObjectInteractionKind.None,
            CashValue = 0,
            HealAmount = 0,
            ArmorAmount = 0,
            AmmoAmount = 0
        };
    }

    private static List<ObjectCategory> BuildObjectCategories(List<ObjectOption> all)
    {
        List<ObjectCategory> categories = new List<ObjectCategory>();

        AddObjectCategory(categories, "Securite", all, ObjectPlacementCategory.Securite);
        AddObjectCategory(categories, "Couverture / Combat", all, ObjectPlacementCategory.Couverture);
        AddObjectCategory(categories, "Argent / butin", all, ObjectPlacementCategory.ArgentButin);
        AddObjectCategory(categories, "Materiel tactique", all, ObjectPlacementCategory.MaterielTactique);
        AddObjectCategory(categories, "Soins / survie", all, ObjectPlacementCategory.SoinSurvie);
        AddObjectCategory(categories, "Bureau / informatique", all, ObjectPlacementCategory.BureauInformatique);
        AddObjectCategory(categories, "Atelier / outils", all, ObjectPlacementCategory.AtelierOutils);
        AddObjectCategory(categories, "Mobilier", all, ObjectPlacementCategory.Mobilier);
        AddObjectCategory(categories, "Caisses / Stockage", all, ObjectPlacementCategory.CaisseStockage);
        AddObjectCategory(categories, "Decoration", all, ObjectPlacementCategory.Decoration);
        AddObjectCategory(categories, "Lumieres", all, ObjectPlacementCategory.Lumiere);
        AddObjectCategory(categories, "Exterieur", all, ObjectPlacementCategory.Exterieur);
        AddObjectCategory(categories, "Divers", all, ObjectPlacementCategory.Divers);

        categories.Add(new ObjectCategory
        {
            Name = "Tous les objets",
            Options = all.OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
        });

        return categories;
    }

    private static void AddObjectCategory(List<ObjectCategory> categories, string name, List<ObjectOption> all, ObjectPlacementCategory category)
    {
        List<ObjectOption> options = all
            .Where(o => o.Category == category)
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        categories.Add(new ObjectCategory
        {
            Name = name,
            Options = options
        });
    }

    private static bool IsSecurityModelName(string name)
    {
        return ContainsAny(name,
            "Cop", "Sheriff", "Swat", "Army", "Marine", "Fib", "CIA", "Security", "Sec",
            "Guard", "Prison", "Ranger", "Uscg", "Military", "Blackops", "Agent", "Doa",
            "Medic", "Fireman", "Paramedic");
    }

    private static bool IsGangModelName(string name)
    {
        return ContainsAny(name,
            "Ballas", "Vagos", "Families", "Lost", "Salva", "Azteca", "Mex", "Korean",
            "Triad", "Arm", "Mob", "Gang", "Biker", "PoloGoon") ||
            EndsWithAny(name, "GFY", "GMM", "GMY");
    }

    private static bool IsMultiplayerModelName(string name)
    {
        return name.StartsWith("MP", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("MP", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsScenarioServiceModelName(string name)
    {
        return EndsWithAny(name, "SFM", "SFY", "SMM", "SMY") ||
               ContainsAny(name, "Shop", "Dealer", "Bouncer", "Waiter", "Valet", "Mechanic", "Pilot", "Worker", "Doctor");
    }

    private static bool IsMaleAmbientModelName(string name)
    {
        return EndsWithAny(name, "AMM", "AMO", "AMY");
    }

    private static bool IsFemaleAmbientModelName(string name)
    {
        return EndsWithAny(name, "AFM", "AFO", "AFY");
    }

    private static bool IsStoryModelName(string name)
    {
        if (IsAnimalModelName(name) ||
            IsSecurityModelName(name) ||
            IsGangModelName(name) ||
            IsMultiplayerModelName(name) ||
            IsScenarioServiceModelName(name) ||
            IsMaleAmbientModelName(name) ||
            IsFemaleAmbientModelName(name))
        {
            return false;
        }

        return !string.Equals(name, "Custom", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnimalModelName(string name)
    {
        return ContainsAny(name,
            "Boar", "Cat", "Chicken", "Chimp", "Chop", "Cormorant", "Cow", "Coyote",
            "Crow", "Deer", "Dolphin", "Fish", "Hen", "Humpback", "Husky", "KillerWhale",
            "MountainLion", "Pig", "Pigeon", "Poodle", "Pug", "Rabbit", "Rat", "Retriever",
            "Rhesus", "Rottweiler", "Seagull", "Shark", "Shepherd", "Stingray", "Westy");
    }

    private static bool IsPistolLikeWeaponName(string name)
    {
        return name.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Revolver", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("DoubleAction", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("FlareGun", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("UpNAtomizer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSmgWeaponName(string name)
    {
        return name.IndexOf("SMG", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("CombatPDW", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("MachinePistol", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsAssaultRifleWeaponName(string name)
    {
        return ContainsAny(name,
            "AssaultRifle", "CarbineRifle", "AdvancedRifle", "SpecialCarbine",
            "BullpupRifle", "CompactRifle", "MilitaryRifle", "HeavyRifle",
            "ServiceCarbine", "BattleRifle");
    }

    private static bool IsSniperWeaponName(string name)
    {
        return name.IndexOf("Sniper", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Marksman", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsMachineGunWeaponName(string name)
    {
        return name.IndexOf("MG", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Gusenberg", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Minigun", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Widowmaker", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsHeavyWeaponName(string name)
    {
        return ContainsAny(name, "RPG", "Launcher", "Minigun", "Railgun", "Widowmaker", "Firework", "EMP");
    }

    private static bool IsThrowableWeaponName(string name)
    {
        return ContainsAny(name,
            "Grenade", "Molotov", "StickyBomb", "ProximityMine", "TearGas",
            "PipeBomb", "Snowball", "Flare", "BZGas");
    }

    private static bool IsMeleeWeaponName(string name)
    {
        return ContainsAny(name,
            "Knife", "Nightstick", "Hammer", "Bat", "GolfClub", "Crowbar", "Bottle",
            "Dagger", "Hatchet", "Machete", "Knuckle", "SwitchBlade", "PoolCue",
            "Wrench", "BattleAxe", "StoneHatchet", "CandyCane");
    }

    private static bool IsSpecialWeaponName(string name)
    {
        if (IsPistolLikeWeaponName(name) ||
            IsSmgWeaponName(name) ||
            IsAssaultRifleWeaponName(name) ||
            IsSniperWeaponName(name) ||
            IsMachineGunWeaponName(name) ||
            IsHeavyWeaponName(name) ||
            IsThrowableWeaponName(name) ||
            IsMeleeWeaponName(name) ||
            name.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(name, "Unarmed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsSportsVehicleName(string name)
    {
        return ContainsAny(name, "Adder", "Banshee", "Bullet", "Cheetah", "Comet", "Coquette", "Elegy", "Entity",
            "Feltzer", "Furore", "Itali", "Jester", "Kuruma", "Nero", "Osiris", "Pariah", "Penetrator", "Pfister",
            "Reaper", "T20", "Turismo", "Vacca", "Vagner", "Voltic", "Zentorno", "Cyclone", "Deveste", "Emerus",
            "Furia", "Ignus", "Krieger", "Locust", "Neo", "Sultan");
    }

    private static bool IsCarVehicleName(string name)
    {
        if (IsSportsVehicleName(name) || IsEmergencyVehicleName(name) || IsUtilityVehicleName(name) ||
            IsTruckVehicleName(name) || IsMotorcycleVehicleName(name) || IsAircraftVehicleName(name) ||
            IsBoatVehicleName(name) || IsMilitaryVehicleName(name))
        {
            return false;
        }

        return ContainsAny(name, "Asea", "Asterope", "Blista", "Buffalo", "Dilettante", "Emperor", "Fugitive",
            "Glendale", "Ingot", "Intruder", "Oracle", "Premier", "Primo", "Regina", "Schafter", "Stanier",
            "Stratum", "Stretch", "Surge", "Tailgater", "Warrener", "Washington", "Zion", "F620");
    }

    private static bool IsSuvVehicleName(string name)
    {
        return ContainsAny(name, "Baller", "BeeJay", "Cavalcade", "Dubsta", "FQ2", "Granger", "Gresley",
            "Habanero", "Huntley", "Landstalker", "Mesa", "Patriot", "Radius", "Rocoto", "Seminole", "Serrano",
            "XLS", "Rebla", "Novak", "Jubilee");
    }

    private static bool IsMotorcycleVehicleName(string name)
    {
        return ContainsAny(name, "Akuma", "Avarus", "Bagger", "Bati", "BF400", "CarbonRS", "Chimera", "Cliffhanger",
            "Daemon", "Defiler", "Diablous", "Double", "Enduro", "Esskey", "Faggio", "Gargoyle", "Hakuchou",
            "Hexer", "Innovation", "Lectro", "Manchez", "Nemesis", "Nightblade", "PCJ", "RatBike", "Ruffian",
            "Sanchez", "Sanctus", "Shotaro", "Sovereign", "Thrust", "Vader", "Vindicator", "Vortex", "Wolfsbane",
            "Zombie");
    }

    private static bool IsEmergencyVehicleName(string name)
    {
        return ContainsAny(name, "Police", "Sheriff", "FBI", "FIB", "Ambulance", "Fire", "Riot", "Pranger", "Predator");
    }

    private static bool IsMilitaryVehicleName(string name)
    {
        return ContainsAny(name, "Barracks", "Crusader", "Rhino", "Lazer", "Hydra", "Titan", "Cargobob", "Savage",
            "Hunter", "APC", "Halftrack", "Insurgent", "Khanjali", "Menacer", "Nightshark", "Patrolboat", "Technical",
            "Valkyrie", "Barrage");
    }

    private static bool IsUtilityVehicleName(string name)
    {
        return ContainsAny(name, "Bison", "Bobcat", "Boxville", "Burrito", "Camper", "Journey", "Minivan", "Mule",
            "Pony", "Rumpo", "Speedo", "Surfer", "Taco", "Towtruck", "Trash", "Utility", "Youga", "Sadler");
    }

    private static bool IsTruckVehicleName(string name)
    {
        return ContainsAny(name, "Benson", "Biff", "Hauler", "Packer", "Phantom", "Pounder", "Stockade", "Trailer",
            "Tanker", "Tipper", "Flatbed", "Mixer", "Docktug");
    }

    private static bool IsAircraftVehicleName(string name)
    {
        return ContainsAny(name, "AlphaZ1", "Besra", "Blimp", "Buzzard", "Cargobob", "Cuban", "Dodo", "Duster",
            "Frogger", "Hydra", "Jet", "Lazer", "Luxor", "Mammatus", "Maverick", "Miljet", "Nimbus", "Shamal",
            "Stunt", "Titan", "Velum", "Vestra", "Volatol", "Akula", "Hunter", "Savage", "Seabreeze", "Skylift");
    }

    private static bool IsBoatVehicleName(string name)
    {
        return ContainsAny(name, "Boat", "Dinghy", "Jetmax", "Marquis", "Seashark", "Speeder", "Squalo", "Submersible",
            "Suntrap", "Toro", "Tropic", "Tug", "Predator");
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithAny(string value, params string[] suffixes)
    {
        for (int i = 0; i < suffixes.Length; i++)
        {
            if (value.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string PlacementTypeDisplayName(PlacementEntityType placementType)
    {
        switch (placementType)
        {
            case PlacementEntityType.Vehicle:
                return "Vehicule";

            case PlacementEntityType.Object:
                return "Objet";

            case PlacementEntityType.Entrance:
                return "Entree";

            case PlacementEntityType.Exit:
                return "Sortie";

            case PlacementEntityType.Npc:
            default:
                return "NPC";
        }
    }

    private static string NpcBehaviorDisplayName(NpcBehavior behavior)
    {
        switch (behavior)
        {
            case NpcBehavior.Static:
                return "Statique / hostile \u00E0 vue";

            case NpcBehavior.Attacker:
                return "Attaquer / agressif";

            case NpcBehavior.Neutral:
                return "Neutre / garde passif";

            case NpcBehavior.Ally:
                return "Alli\u00E9 / garde d\u00E9fense";

            case NpcBehavior.Bodyguard:
                return "Garde du corps / escorte joueur";

            case NpcBehavior.NeutralPatrol:
                return "Neutre patrouille";

            case NpcBehavior.HostilePatrol:
                return "Hostile patrouille";

            case NpcBehavior.AllyPatrol:
                return "Allie patrouille";

            default:
                return behavior.ToString();
        }
    }

    private static string BoolText(bool value)
    {
        return value ? "Oui" : "Non";
    }

    private static string WeaponPresetDisplayName(WeaponUpgradePreset preset)
    {
        switch (preset)
        {
            case WeaponUpgradePreset.Standard:
                return "Standard";

            case WeaponUpgradePreset.ChargeurEtendu:
                return "Chargeur etendu";

            case WeaponUpgradePreset.Silencieux:
                return "Silencieux";

            case WeaponUpgradePreset.Tactique:
                return "Tactique";

            case WeaponUpgradePreset.Full:
                return "Full";

            default:
                return preset.ToString();
        }
    }

    private static string ScopeDisplayName(WeaponScopeMode scope)
    {
        switch (scope)
        {
            case WeaponScopeMode.None:
                return "Aucune";

            case WeaponScopeMode.Small:
                return "Petite";

            case WeaponScopeMode.Medium:
                return "Moyenne";

            case WeaponScopeMode.Large:
                return "Grande";

            default:
                return scope.ToString();
        }
    }

    private static string Mk2AmmoDisplayName(WeaponMk2AmmoMode ammo)
    {
        switch (ammo)
        {
            case WeaponMk2AmmoMode.Standard:
                return "Standard";

            case WeaponMk2AmmoMode.Tracer:
                return "Traceur";

            case WeaponMk2AmmoMode.Incendiary:
                return "Incendiaire";

            case WeaponMk2AmmoMode.ArmorPiercing:
                return "Perforante";

            case WeaponMk2AmmoMode.FMJ:
                return "FMJ";

            case WeaponMk2AmmoMode.Explosive:
                return "Explosive";

            default:
                return ammo.ToString();
        }
    }

    private static string ReadStringAttribute(XmlNode node, string name, string defaultValue)
    {
        XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[name];

        if (attribute == null)
        {
            return defaultValue;
        }

        return attribute.Value;
    }

    private static bool ReadBoolAttribute(XmlNode node, string name, bool defaultValue)
    {
        string value = ReadStringAttribute(node, name, null);

        bool parsed;

        if (bool.TryParse(value, out parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static int ReadIntAttribute(XmlNode node, string name, int defaultValue)
    {
        string value = ReadStringAttribute(node, name, null);

        int parsed;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static float ReadFloatAttribute(XmlNode node, string name, float defaultValue)
    {
        string value = ReadStringAttribute(node, name, null);

        float parsed;

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static TEnum ReadEnumAttribute<TEnum>(XmlNode node, string name, TEnum defaultValue) where TEnum : struct
    {
        string value = ReadStringAttribute(node, name, null);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        TEnum parsed;

        if (Enum.TryParse(value, true, out parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static WeaponHash ReadWeaponHashAttribute(XmlNode node, string nameAttribute, string hashAttribute, WeaponHash defaultValue)
    {
        string name = ReadStringAttribute(node, nameAttribute, null);

        if (!string.IsNullOrWhiteSpace(name))
        {
            WeaponHash parsed;

            if (Enum.TryParse(name, true, out parsed))
            {
                return parsed;
            }
        }

        int hash = ReadIntAttribute(node, hashAttribute, EnumToIntHash(defaultValue));

        try
        {
            return (WeaponHash)hash;
        }
        catch
        {
            return defaultValue;
        }
    }

    private void UpdatePlacedObjectInteractions()
    {
        if (_placementMode || _menuVisible)
        {
            _objectInteractionKeyLatch = false;
            return;
        }

        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            _objectInteractionKeyLatch = false;
            return;
        }

        PlacedObject nearest = FindNearestInteractablePlacedObject(player);

        if (nearest == null)
        {
            if (!Game.IsKeyPressed(Keys.E))
            {
                _objectInteractionKeyLatch = false;
            }

            return;
        }

        DrawPlacedObjectInteractionMarker(nearest);
        DrawPlacedObjectInteractionPrompt(nearest);

        bool ePressed = Game.IsKeyPressed(Keys.E);

        if (!ePressed)
        {
            _objectInteractionKeyLatch = false;
            return;
        }

        if (_objectInteractionKeyLatch)
        {
            return;
        }

        _objectInteractionKeyLatch = true;
        TryUsePlacedObject(nearest, player);
    }

    private PlacedObject FindNearestInteractablePlacedObject(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return null;
        }

        PlacedObject best = null;
        float bestDistance = ObjectInteractionDistance;
        Vector3 playerPosition = player.Position;

        for (int i = 0; i < _placedObjects.Count; i++)
        {
            PlacedObject placed = _placedObjects[i];

            if (placed == null || !Entity.Exists(placed.Prop))
            {
                continue;
            }

            ApplyDefaultObjectInteractionIfNeeded(placed.Identity);

            if (!HasObjectInteraction(placed.Identity))
            {
                continue;
            }

            float distance = (placed.Prop.Position - playerPosition).Length();

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = placed;
            }
        }

        return best;
    }

    private void DrawPlacedObjectInteractionMarker(PlacedObject placed)
    {
        if (placed == null || !Entity.Exists(placed.Prop))
        {
            return;
        }

        Vector3 markerPosition = placed.Prop.Position + new Vector3(0.0f, 0.0f, ObjectInteractionMarkerHeight);

        World.DrawMarker(
            MarkerType.DebugSphere,
            markerPosition,
            Vector3.Zero,
            Vector3.Zero,
            new Vector3(0.18f, 0.18f, 0.18f),
            Color.FromArgb(220, 255, 230, 80));
    }

    private void DrawPlacedObjectInteractionPrompt(PlacedObject placed)
    {
        if (placed == null || placed.Identity == null)
        {
            return;
        }

        string action = ObjectInteractionPromptText(placed.Identity);

        DrawRect(305, 616, 670, 48, Color.FromArgb(165, 0, 0, 0));
        DrawText("E - " + FitText(action, 86), 640, 629, 0.32f, Color.White, true, true);
    }

    private bool TryUsePlacedObject(PlacedObject placed, Ped player)
    {
        if (placed == null || placed.Identity == null || !Entity.Exists(placed.Prop) || !Entity.Exists(player))
        {
            return false;
        }

        ApplyDefaultObjectInteractionIfNeeded(placed.Identity);

        switch (placed.Identity.InteractionKind)
        {
            case ObjectInteractionKind.Cash:
                return TryUseCashObject(placed);

            case ObjectInteractionKind.Health:
                return TryUseHealthObject(placed, player);

            case ObjectInteractionKind.Armor:
                return TryUseArmorObject(placed, player);

            case ObjectInteractionKind.Ammo:
                return TryUseAmmoObject(placed, player);

            case ObjectInteractionKind.None:
            default:
                return false;
        }
    }

    private bool TryUseCashObject(PlacedObject placed)
    {
        int amount = placed.Identity.CashValue;

        if (amount <= 0)
        {
            ShowStatus("Argent invalide sur cet objet.", 2500);
            return false;
        }

        int oldCash;
        int newCash;

        if (!TryAddSinglePlayerCash(amount, out oldCash, out newCash))
        {
            ShowStatus("Impossible d'ajouter l'argent solo. Objet conserve.", 3500);
            return false;
        }

        ConsumePlacedObject(placed);
        ShowStatus("Argent recupere: +" + FormatMoney(amount) + " | total: " + FormatMoney(newCash), 4200);
        return true;
    }

    private bool TryUseHealthObject(PlacedObject placed, Ped player)
    {
        int amount = Math.Max(1, placed.Identity.HealAmount);
        int maxHealth = Math.Max(100, player.MaxHealth);

        if (player.Health >= maxHealth)
        {
            ShowStatus("Sante deja au maximum.", 2500);
            return false;
        }

        int oldHealth = player.Health;
        player.Health = Clamp(player.Health + amount, 1, maxHealth);

        ConsumePlacedObject(placed);
        ShowStatus("Soin utilise: +" + (player.Health - oldHealth).ToString(CultureInfo.InvariantCulture) + " PV", 3200);
        return true;
    }

    private bool TryUseArmorObject(PlacedObject placed, Ped player)
    {
        int amount = Math.Max(1, placed.Identity.ArmorAmount);

        if (player.Armor >= MaxArmor)
        {
            ShowStatus("Armure deja au maximum.", 2500);
            return false;
        }

        int oldArmor = player.Armor;
        player.Armor = Clamp(player.Armor + amount, MinArmor, MaxArmor);

        ConsumePlacedObject(placed);
        ShowStatus("Armure equipee: +" + (player.Armor - oldArmor).ToString(CultureInfo.InvariantCulture), 3200);
        return true;
    }

    private bool TryUseAmmoObject(PlacedObject placed, Ped player)
    {
        int amount = Math.Max(1, placed.Identity.AmmoAmount);

        if (!TryAddAmmoToCurrentWeapon(player, amount))
        {
            ShowStatus("Equipe une arme pour prendre ces munitions.", 3000);
            return false;
        }

        ConsumePlacedObject(placed);
        ShowStatus("Munitions ajoutees: +" + amount.ToString(CultureInfo.InvariantCulture), 3200);
        return true;
    }

    private void ConsumePlacedObject(PlacedObject placed)
    {
        if (placed == null)
        {
            return;
        }

        if (placed.AutoRespawn)
        {
            // Je garde l'objet dans la liste pour qu'il puisse reapparaitre apres eloignement.
            MarkPlacedObjectForAutoRespawn(placed);
            DeleteEntitySafe(placed.Prop);
            return;
        }

        DeleteEntitySafe(placed.Prop);
        _placedObjects.Remove(placed);
    }

    private bool TryAddSinglePlayerCash(int amount, out int oldCash, out int newCash)
    {
        oldCash = 0;
        newCash = 0;

        if (amount <= 0)
        {
            return false;
        }

        amount = Clamp(amount, 1, 100000000);

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();

        if (currentSlot >= 0)
        {
            return TryAddCashToSinglePlayerSlot(currentSlot, amount, out oldCash, out newCash);
        }

        // Si le joueur utilise un modele custom, on ne sait pas toujours quel slot story mode
        // est actif. Dans ce cas on incremente les trois slots SP pour eviter un pickup sans effet.
        bool anySuccess = false;
        int firstOldCash = 0;
        int firstNewCash = 0;

        for (int i = 0; i < 3; i++)
        {
            int slotOldCash;
            int slotNewCash;

            if (TryAddCashToSinglePlayerSlot(i, amount, out slotOldCash, out slotNewCash))
            {
                if (!anySuccess)
                {
                    firstOldCash = slotOldCash;
                    firstNewCash = slotNewCash;
                }

                anySuccess = true;
            }
        }

        oldCash = firstOldCash;
        newCash = firstNewCash;
        return anySuccess;
    }

    private bool TryAddCashToSinglePlayerSlot(int slot, int amount, out int oldCash, out int newCash)
    {
        oldCash = 0;
        newCash = 0;

        if (slot < 0 || slot > 2 || amount <= 0)
        {
            return false;
        }

        try
        {
            string statName = "SP" + slot.ToString(CultureInfo.InvariantCulture) + "_TOTAL_CASH";
            int statHash = Game.GenerateHash(statName);

            OutputArgument outValue = new OutputArgument();
            bool readOk = Function.Call<bool>((Hash)NativeStatGetInt, statHash, outValue, -1);

            if (!readOk)
            {
                return false;
            }

            oldCash = outValue.GetResult<int>();

            long target = (long)oldCash + amount;

            if (target > int.MaxValue)
            {
                target = int.MaxValue;
            }

            if (target < 0)
            {
                target = 0;
            }

            newCash = (int)target;
            return Function.Call<bool>((Hash)NativeStatSetInt, statHash, newCash, true);
        }
        catch
        {
            oldCash = 0;
            newCash = 0;
            return false;
        }
    }

    private int GetCurrentSinglePlayerCashSlotSafe()
    {
        try
        {
            Ped player = Game.Player.Character;

            if (!Entity.Exists(player))
            {
                return -1;
            }

            int modelHash = player.Model.Hash;

            if (modelHash == Game.GenerateHash("player_zero"))
            {
                return 0; // Michael
            }

            if (modelHash == Game.GenerateHash("player_one"))
            {
                return 1; // Franklin
            }

            if (modelHash == Game.GenerateHash("player_two"))
            {
                return 2; // Trevor
            }
        }
        catch
        {
        }

        return -1;
    }

    private bool TryAddAmmoToCurrentWeapon(Ped player, int amount)
    {
        if (!Entity.Exists(player) || player.IsDead || amount <= 0)
        {
            return false;
        }

        try
        {
            int weaponHash = Function.Call<int>((Hash)NativeGetSelectedPedWeapon, player.Handle);

            if (weaponHash == 0 || weaponHash == Game.GenerateHash("WEAPON_UNARMED"))
            {
                return false;
            }

            Function.Call((Hash)NativeAddAmmoToPed, player.Handle, weaponHash, amount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteObjectInteractionXmlAttributes(XmlWriter writer, ObjectIdentity identity)
    {
        if (writer == null || identity == null)
        {
            return;
        }

        writer.WriteAttributeString("interactionKind", identity.InteractionKind.ToString());
        writer.WriteAttributeString("cashValue", identity.CashValue.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("healAmount", identity.HealAmount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("armorAmount", identity.ArmorAmount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("ammoAmount", identity.AmmoAmount.ToString(CultureInfo.InvariantCulture));
    }

    private static bool HasObjectInteraction(ObjectIdentity identity)
    {
        if (identity == null)
        {
            return false;
        }

        switch (identity.InteractionKind)
        {
            case ObjectInteractionKind.Cash:
                return identity.CashValue > 0;

            case ObjectInteractionKind.Health:
                return identity.HealAmount > 0;

            case ObjectInteractionKind.Armor:
                return identity.ArmorAmount > 0;

            case ObjectInteractionKind.Ammo:
                return identity.AmmoAmount > 0;

            case ObjectInteractionKind.None:
            default:
                return false;
        }
    }

    private static string ObjectInteractionDisplayName(ObjectIdentity identity)
    {
        if (identity == null)
        {
            return "aucune";
        }

        switch (identity.InteractionKind)
        {
            case ObjectInteractionKind.Cash:
                return "argent +" + FormatMoney(identity.CashValue);

            case ObjectInteractionKind.Health:
                return "soin +" + identity.HealAmount.ToString(CultureInfo.InvariantCulture);

            case ObjectInteractionKind.Armor:
                return "armure +" + identity.ArmorAmount.ToString(CultureInfo.InvariantCulture);

            case ObjectInteractionKind.Ammo:
                return "munitions +" + identity.AmmoAmount.ToString(CultureInfo.InvariantCulture);

            case ObjectInteractionKind.None:
            default:
                return "aucune";
        }
    }

    private static string ObjectInteractionPromptText(ObjectIdentity identity)
    {
        if (identity == null)
        {
            return "Interagir";
        }

        string name = string.IsNullOrWhiteSpace(identity.DisplayName)
            ? "Objet"
            : identity.DisplayName;

        switch (identity.InteractionKind)
        {
            case ObjectInteractionKind.Cash:
                return "Ramasser " + name + " (+" + FormatMoney(identity.CashValue) + ")";

            case ObjectInteractionKind.Health:
                return "Utiliser " + name + " (soin +" + identity.HealAmount.ToString(CultureInfo.InvariantCulture) + ")";

            case ObjectInteractionKind.Armor:
                return "Equiper " + name + " (armure +" + identity.ArmorAmount.ToString(CultureInfo.InvariantCulture) + ")";

            case ObjectInteractionKind.Ammo:
                return "Prendre " + name + " (munitions +" + identity.AmmoAmount.ToString(CultureInfo.InvariantCulture) + ")";

            case ObjectInteractionKind.None:
            default:
                return "Interagir avec " + name;
        }
    }

    private static void ApplyDefaultObjectInteractionIfNeeded(ObjectIdentity identity)
    {
        if (identity == null)
        {
            return;
        }

        // Si l'interaction est deja configuree et valide, on ne change rien.
        if (HasObjectInteraction(identity))
        {
            return;
        }

        string text = ((identity.DisplayName ?? string.Empty) + " " + (identity.ModelName ?? string.Empty)).ToLowerInvariant();

        if (LooksLikeCashObject(text))
        {
            identity.InteractionKind = ObjectInteractionKind.Cash;
            identity.CashValue = InferCashValue(text);
            identity.HealAmount = 0;
            identity.ArmorAmount = 0;
            identity.AmmoAmount = 0;
            return;
        }

        if (LooksLikeHealthObject(text))
        {
            identity.InteractionKind = ObjectInteractionKind.Health;
            identity.CashValue = 0;
            identity.HealAmount = 75;
            identity.ArmorAmount = 0;
            identity.AmmoAmount = 0;
            return;
        }

        if (LooksLikeArmorObject(text))
        {
            identity.InteractionKind = ObjectInteractionKind.Armor;
            identity.CashValue = 0;
            identity.HealAmount = 0;
            identity.ArmorAmount = 50;
            identity.AmmoAmount = 0;
            return;
        }

        if (LooksLikeAmmoObject(text))
        {
            identity.InteractionKind = ObjectInteractionKind.Ammo;
            identity.CashValue = 0;
            identity.HealAmount = 0;
            identity.ArmorAmount = 0;
            identity.AmmoAmount = 90;
        }
    }

    private static bool LooksLikeCashObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("cash") ||
               text.Contains("money") ||
               text.Contains("argent") ||
               text.Contains("billet") ||
               text.Contains("banknote") ||
               text.Contains("poly_bag_money") ||
               text.Contains("heist_bag") ||
               text.Contains("gold_chest") ||
               text.Contains("cashpack") ||
               text.Contains("cash_case") ||
               text.Contains("cash_crate") ||
               text.Contains("cash_trolly");
    }

    private static bool LooksLikeHealthObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("health") ||
               text.Contains("soin") ||
               text.Contains("medical") ||
               text.Contains("med_bag");
    }

    private static bool LooksLikeArmorObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("armor") ||
               text.Contains("armour") ||
               text.Contains("armure") ||
               text.Contains("gilet pare");
    }

    private static bool LooksLikeAmmoObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("ammo") ||
               text.Contains("munition") ||
               text.Contains("munitions") ||
               text.Contains("guncase");
    }

    private static int InferCashValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 10000;
        }

        string compact = text
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("$", string.Empty);

        if (compact.Contains("10000") || compact.Contains("10k"))
        {
            return 10000;
        }

        if (compact.Contains("onedollar"))
        {
            return 1;
        }

        if (compact.Contains("banknote") || compact.Contains("billetseul"))
        {
            return 100;
        }

        if (compact.Contains("envelope") || compact.Contains("enveloppe"))
        {
            return 2500;
        }

        if (compact.Contains("cashpack") || compact.Contains("paquetdecash"))
        {
            return 5000;
        }

        if (compact.Contains("heistbag") || compact.Contains("moneybag") || compact.Contains("sacdargent") || compact.Contains("sacdebraquage"))
        {
            return 50000;
        }

        if (compact.Contains("cashcase") || compact.Contains("malette"))
        {
            return 50000;
        }

        if (compact.Contains("cashcrate") || compact.Contains("goldchest") || compact.Contains("coffreor"))
        {
            return 250000;
        }

        if (compact.Contains("cashtrolly") || compact.Contains("chariotcash"))
        {
            return 200000;
        }

        return 10000;
    }

    private static string FormatMoney(int amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") + "$";
    }

    private void DrawStatus()
    {
        // Quand la console est visible, je rends ce message dans son panneau
        // de detail pour ne jamais recouvrir l'en-tete.
        if (ShouldRenderMenu || Game.GameTime > _statusUntil || string.IsNullOrEmpty(_statusText))
        {
            return;
        }

        DrawRect(270, 28, 760, 38, Color.FromArgb(170, 0, 0, 0));
        DrawText(FitText(_statusText, StatusTextMaxLength), 650, 38, 0.32f, Color.White, true, true);
    }

    private void ShowStatus(string text, int milliseconds)
    {
        _statusText = text ?? string.Empty;
        _statusUntil = GetMenuGameTimeSafe() + milliseconds;
        // Je retire l'ancien propriétaire à chaque message générique. Les
        // chemins Justice qui suivent un protagoniste le réarment explicitement.
        _statusOwnedByJusticeProfile = false;
    }

    private static void DrawRect(int x, int y, int width, int height, Color color)
    {
        new UIRectangle(new Point(x, y), new Size(width, height), color).Draw();
    }

    private static void DrawText(string text, int x, int y, float scale, Color color, bool centered, bool outline)
    {
        new UIText(
            text ?? string.Empty,
            new Point(x, y),
            scale,
            color,
            GtaFont.ChaletLondon,
            centered,
            false,
            outline).Draw();
    }

    private static void DeleteEntitySafe(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return;
        }

        try
        {
            entity.Delete();
        }
        catch
        {
        }
    }

    private static string FitText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= 3)
        {
            return text.Substring(0, maxLength);
        }

        return text.Substring(0, maxLength - 3) + "...";
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        while (value < 0)
        {
            value += count;
        }

        while (value >= count)
        {
            value -= count;
        }

        return value;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static float ClampFloat(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static int RoundToStep(int value, int step)
    {
        if (step <= 0)
        {
            return value;
        }

        return (int)Math.Round(value / (double)step) * step;
    }

    private static TEnum CycleEnum<TEnum>(TEnum current, int direction) where TEnum : struct
    {
        Array values = Enum.GetValues(typeof(TEnum));
        int currentIndex = 0;

        for (int i = 0; i < values.Length; i++)
        {
            if (values.GetValue(i).Equals(current))
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = Wrap(currentIndex + direction, values.Length);
        return (TEnum)values.GetValue(nextIndex);
    }

    private static Vector3 Normalize(Vector3 vector)
    {
        float length = vector.Length();

        if (length < 0.0001f)
        {
            return Vector3.Zero;
        }

        return vector / length;
    }

    private static bool IsZeroVector(Vector3 vector)
    {
        return Math.Abs(vector.X) < 0.001f &&
               Math.Abs(vector.Y) < 0.001f &&
               Math.Abs(vector.Z) < 0.001f;
    }

    private static float HeadingFromTo(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        double heading = Math.Atan2(delta.X, delta.Y) * 180.0 / Math.PI;

        if (heading < 0.0)
        {
            heading += 360.0;
        }

        return (float)heading;
    }

    private static float NormalizeHeading(float heading)
    {
        while (heading < 0.0f)
        {
            heading += 360.0f;
        }

        while (heading >= 360.0f)
        {
            heading -= 360.0f;
        }

        return heading;
    }

    private static string FormatVector(Vector3 v)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "X {0:0.0} | Y {1:0.0} | Z {2:0.0}",
            v.X,
            v.Y,
            v.Z);
    }

    private static int EnumToIntHash(Enum value)
    {
        Type underlying = Enum.GetUnderlyingType(value.GetType());
        object raw = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);

        if (raw is uint) return unchecked((int)(uint)raw);
        if (raw is int) return (int)raw;
        if (raw is ulong) return unchecked((int)(ulong)raw);
        if (raw is long) return unchecked((int)(long)raw);
        if (raw is ushort) return (ushort)raw;
        if (raw is short) return (short)raw;
        if (raw is byte) return (byte)raw;
        if (raw is sbyte) return (sbyte)raw;

        return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
    }

    private static bool IsKeyDown(Keys key)
    {
        return Game.IsKeyPressed(key);
    }

    private static bool IsShiftHeld()
    {
        return Game.IsKeyPressed(Keys.ShiftKey) ||
               Game.IsKeyPressed(Keys.LShiftKey) ||
               Game.IsKeyPressed(Keys.RShiftKey);
    }

    private static bool IsAltHeld()
    {
        return Game.IsKeyPressed(Keys.Menu) ||
               Game.IsKeyPressed(Keys.LMenu) ||
               Game.IsKeyPressed(Keys.RMenu);
    }

    private static bool IsControlHeld()
    {
        return Game.IsKeyPressed(Keys.ControlKey) ||
               Game.IsKeyPressed(Keys.LControlKey) ||
               Game.IsKeyPressed(Keys.RControlKey);
    }

    // ---------------------------------------------------------------------
    // Compatibilite tests historiques
    // ---------------------------------------------------------------------

    private static List<ModelOption> BuildModelOptions()
    {
        return BuildAllModelOptions();
    }

    private static List<WeaponOption> BuildWeaponOptions()
    {
        return BuildAllWeaponOptions();
    }

    private int FindDefaultModelIndex()
    {
        List<ModelOption> options = _modelOptions ?? _allModelOptions;

        if (options == null || options.Count == 0)
        {
            return 0;
        }

        int swat = options.FindIndex(m =>
            !m.IsCustom &&
            string.Equals(m.DisplayName, "Swat01SMY", StringComparison.OrdinalIgnoreCase));

        if (swat >= 0)
        {
            return swat;
        }

        int cop = options.FindIndex(m =>
            !m.IsCustom &&
            m.DisplayName.IndexOf("Cop", StringComparison.OrdinalIgnoreCase) >= 0);

        return cop >= 0 ? cop : 0;
    }

    private int FindDefaultWeaponIndex()
    {
        List<WeaponOption> options = _weaponOptions ?? _allWeaponOptions;

        if (options == null || options.Count == 0)
        {
            return 0;
        }

        int carbine = options.FindIndex(w =>
            string.Equals(w.DisplayName, "CarbineRifle", StringComparison.OrdinalIgnoreCase));

        if (carbine >= 0)
        {
            return carbine;
        }

        int pistol = options.FindIndex(w =>
            string.Equals(w.DisplayName, "Pistol", StringComparison.OrdinalIgnoreCase));

        return pistol >= 0 ? pistol : 0;
    }

    private static EnemyBehavior CycleBehavior(EnemyBehavior current, int direction)
    {
        int count = Enum.GetValues(typeof(EnemyBehavior)).Length;
        int next = Wrap((int)current + direction, count);
        return (EnemyBehavior)next;
    }

    private static string BehaviorDisplayName(EnemyBehavior behavior)
    {
        switch (behavior)
        {
            case EnemyBehavior.Static:
                return "Statique / hostile \u00E0 vue";

            case EnemyBehavior.Attacker:
                return "Attaquer / agressif";

            case EnemyBehavior.Neutral:
                return "Neutre / garde passif";

            case EnemyBehavior.Ally:
                return "Alli\u00E9 / garde d\u00E9fense";

            default:
                return behavior.ToString();
        }
    }

    private int GetRelationshipGroupForBehavior(EnemyBehavior behavior)
    {
        switch (behavior)
        {
            case EnemyBehavior.Neutral:
                return _neutralGroupHash;

            case EnemyBehavior.Ally:
                return _allyGroupHash;

            case EnemyBehavior.Static:
            case EnemyBehavior.Attacker:
            default:
                return _hostileGroupHash;
        }
    }

    // ---------------------------------------------------------------------
    // Contact téléphone : Cartel
    // ---------------------------------------------------------------------
    /*
     * Amélioration Cartel :
     * - Arrivée plus rapide.
     * - Spawn plus proche mais hors champ de vision.
     * - Spawn prioritaire sur route / node véhicule.
     * - Déblocage automatique des véhicules bloqués.
     * - Téléportation de secours hors champ de vision si le joueur s'éloigne trop.
     * - Repli plus rapide.
     * - Les hommes restent alliés pendant le repli.
     * - Les anciens hommes en repli n'empêchent plus de rappeler une nouvelle équipe.
     */

    private const string CartelContactName = "Cartel";

    private const int CartelGuardCount = 11;
    private const int CartelVehicleCount = 3;

    private const int CartelGuardHealth = 500;
    private const int CartelGuardArmor = 200;

    private const int CartelCallCooldownMs = 1800;
    private const int CartelThinkIntervalMs = 700;

    /*
     * Fréquence des ordres de conduite.
     * On espace un peu les ordres pour éviter de "spam" l'IA conducteur.
     * Le véhicule reste contrôlé par le conducteur, pas par une force physique.
     */
    private const int CartelVehicleOrderIntervalMs = 1800;
    private const int CartelDismissOrderIntervalMs = 2200;

    private const int CartelStuckTimeoutMs = 6500;
    private const int CartelRescueCooldownMs = 6500;
    private const int CartelGuardRescueCooldownMs = 5500;

    private const int CartelDismissMinLifeMs = 2200;
    private const int CartelDismissForceCleanupMs = 18000;

    /*
     * Spawn plus proche que l'ancienne version, mais pas collé au joueur.
     * Le script cherche une route hors champ de vision.
     */
    private const float CartelSpawnMinDistance = 68.0f;
    private const float CartelSpawnMaxDistance = 118.0f;

    private const float CartelRelocationMinDistance = 68.0f;
    private const float CartelRelocationMaxDistance = 118.0f;

    /*
     * Vitesses réalistes en mètres/seconde GTA.
     * 72.0f était beaucoup trop violent : ça poussait la voiture comme un projectile
     * quand combiné à une propulsion scriptée.
     */
    private const float CartelArrivalDriveSpeed = 38.0f;
    private const float CartelRetreatDriveSpeed = 34.0f;

    private const float CartelTooFarVehicleDistance = 185.0f;
    private const float CartelCriticalVehicleDistance = 285.0f;
    private const float CartelTooFarGuardDistance = 165.0f;

    private const float CartelDismissDeleteDistance = 28.0f;

    /*
     * Style de conduite naturel/professionnel.
     * On garde une conduite PNJ crédible : routes, évitement, virages.
     * La rapidité vient du spawn proche + ordre de conduite, pas d'une force physique.
     */
    private const int CartelRapidDrivingStyle = ProfessionalDrivingStyle;

    private const ulong NativeIsPedRunningMobilePhoneTask = 0x2AFE52F782F25775UL;
    private const int PhoneOpenDetectionGraceMs = 350;

    private bool _cartelPhoneKeyLatch;
    private bool _enemyRaidPhoneKeyLatch;
    private bool _cartelPhoneCommandConsumedUntilRelease;
    private bool _enemyRaidPhoneCommandConsumedUntilRelease;
    private bool _playerPhoneNativeOpenThisTick;
    private bool _playerPhoneOpenGraceActive;
    private int _playerPhoneOpenLastConfirmedAt;
    private int _playerPhoneOpenPlayerHandle;
    private bool _cartelConvoyActive;
    private bool _cartelConvoyDismissing;

    private int _nextCartelCallAllowedAt;
    private int _nextCartelThinkAt;
    private int _cartelDismissStartedAt;
    private int _cartelDismissCleanupAt;
    private int _nextCartelDismissOrderAt;

    /*
     * Handles actifs :
     * - Ces hommes sont encore au service du joueur.
     * - Si le joueur rappelle Cartel pendant qu'ils sont actifs, ils passent en repli.
     */
    private readonly HashSet<int> _cartelNpcHandles = new HashSet<int>();
    private readonly HashSet<int> _cartelVehicleHandles = new HashSet<int>();

    /*
     * Handles en repli :
     * - Ils ne bloquent plus un nouvel appel.
     * - Ils restent alliés, mais ne sont plus gérés comme gardes du corps actifs.
     * - Ils disparaissent vite dès qu'ils ne sont plus visibles.
     */
    private readonly Dictionary<int, SpawnedNpc> _cartelDismissingNpcRecords = new Dictionary<int, SpawnedNpc>();
    private readonly HashSet<int> _cartelDismissingVehicleHandles = new HashSet<int>();

    private readonly Dictionary<int, Vector3> _cartelLastVehiclePositions = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> _cartelLastVehicleMoveAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cartelLastVehicleRescueAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cartelNextVehicleOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cartelLastGuardRescueAt = new Dictionary<int, int>();
    /*
     * Anti-pulsation véhicules Cartel :
     * - Les upgrades lourdes sont appliquées une seule fois par véhicule.
     * - Les tâches de conduite ne sont pas ré-envoyées si le véhicule est déjà proche.
     * - Le véhicule n'est plus remis au sol en boucle.
     */
    private readonly HashSet<int> _cartelFullyUpgradedVehicleHandles = new HashSet<int>();
    private readonly Dictionary<int, int> _cartelLastVehicleSoftMaintenanceAt = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector3> _cartelLastVehicleOrderTarget = new Dictionary<int, Vector3>();
    /*
     * Combat Cartel :
     * Les dernieres mises a jour ont rendu les vehicules plus propres, mais les gardes
     * peuvent rester dans un etat "vise la cible" sans tirer. Cette couche force
     * un vrai ordre de tir uniquement quand une menace reelle existe.
     */
    private const int CartelCombatOrderIntervalMs = 750;
    private const float CartelThreatScanRadius = 210.0f;
    private const float CartelThreatEvidenceRadius = 230.0f;
    private const float CartelDriveByDistance = 135.0f;
    private const float CartelPassengerExitCombatDistance = 45.0f;
    private const float CartelOnFootShootDistance = 145.0f;

    /*
     * Firing pattern GTA full-auto.
     * Permet d'eviter le comportement "je vise mais je ne tire pas".
     */
    private const int CartelFullAutoFiringPattern = unchecked((int)0xC6EE6B4C);

    /*
     * Anti-spam des ordres de combat.
     * On veut forcer le tir, mais pas reassigner TASK_COMBAT_PED a chaque frame.
     */
    private readonly Dictionary<int, int> _cartelNextCombatOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cartelNextGuardPassiveMaintenanceAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cartelNextGuardMobilityOrderAt = new Dictionary<int, int>();
    /*
     * Optimisation Cartel :
     * La derniere correction du tir scannait trop de PNJ trop souvent.
     * On met maintenant en cache la menace et on limite les scans lourds.
     */
    private const int CartelThreatScanIntervalMs = 1250;
    private const int CartelThreatCacheLifetimeMs = 1800;
    private const int CartelLateMaintenanceIntervalMs = 500;
    private const int CartelMaxGuardThreatScansPerPass = 2;
    private const int CartelThreatRelationshipRefreshMs = 2500;
    /*
     * Maintenance passive des gardes Cartel.
     * Elle remplace le passage générique Bodyguard pour éviter les scans et
     * les ordres synchronisés toutes les secondes. Le jitter par handle évite
     * que les 11 gardes exécutent leur petite maintenance exactement au même tick.
     */
    private const int CartelGuardPassiveMaintenanceIntervalMs = 2400;
    private const int CartelGuardPassiveMaintenanceJitterMs = 700;

    /*
     * Synchronisation pied / vehicule des gardes Cartel.
     * Important : cette couche ne scanne pas le monde. Elle ne fait que gérer
     * les gardes et véhicules Cartel déjà connus par handle.
     */
    private const int CartelGuardMobilityOrderIntervalMs = 900;
    private const int CartelGuardFootFollowIntervalMs = 850;

    private const float CartelVehicleFootExitDistance = 30.0f;
    private const float CartelVehicleFootExitSpeed = 5.0f;
    private const float CartelVehicleForcedFootExitMaxDistance = 125.0f;

    private const float CartelGuardFootFollowDistance = 3.4f;
    private const float CartelGuardFootStandDistance = 2.4f;
    private const float CartelGuardImmediateThreatDistance = 26.0f;

    private int _nextCartelThreatScanAt;
    private int _cartelCachedThreatUntil;
    private int _cartelGuardThreatScanCursor;
    private int _nextCartelLateMaintenanceAt;

    private int _cartelLastThreatRelationshipHandle;
    private int _cartelLastThreatRelationshipAt;

    private Ped _cartelCachedThreatPed;

    private void UpdateCartelContactAndConvoy()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            ResetPlayerPhoneOpenDetection();
            return;
        }

        bool phoneServicesAvailable = ArePhoneServicesAvailableDuringJustice(player);

        // Je garde les trois contacts visibles même quand Justice suspend leurs
        // effets. Le gel ci-dessous ne doit concerner que les IA et les spawns.
        UpdateCartelPhoneContact(player, phoneServicesAvailable);

        if (!phoneServicesAvailable)
        {
            // Je fige le raid sans supprimer ses entités : aucun ennemi ne doit
            // être relocalisé dans la prison ou au commissariat. Son état reprend
            // normalement dès la libération ou l'évasion.
            return;
        }

        UpdateCartelConvoyState(player);
        UpdateEnemyRaidState(player);
        UpdateHighSecurityEscortState(player);
    }

    private bool ArePhoneServicesAvailableDuringJustice(Ped player)
    {
        if (JusticeIsCustodyActive)
        {
            return false;
        }

        // Je suspends aussi les spawns pendant un maintien provisoire réellement
        // rattaché au héros courant, sans bloquer le téléphone d'un autre profil.
        return _justicePreJudgmentHoldingSource == JusticePreJudgmentHoldingSource.None ||
               !IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(player);
    }

    /*
     * Appelé après UpdateNpcs().
     * Important : l'IA bodyguard standard peut donner des ordres plus lents.
     * Cette passe tardive laisse surtout l'IA conducteur se recaler proprement.
     */
    private void UpdateCartelConvoyLate()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return;
        }

        if (!ArePhoneServicesAvailableDuringJustice(player))
        {
            return;
        }

        if (_cartelConvoyActive)
        {
            if (Game.GameTime >= _nextCartelLateMaintenanceAt)
            {
                _nextCartelLateMaintenanceAt = Game.GameTime + CartelLateMaintenanceIntervalMs;
                MaintainCartelTeamWeaponsAndDrivers(player, true);
            }
        }

        if (_cartelConvoyDismissing)
        {
            UpdateCartelDismissal(player, true);
        }
    }

    private void UpdateCartelPhoneContact(Ped player, bool servicesAvailable)
    {
        bool cPressedNow = Game.IsKeyPressed(Keys.C);
        bool rPressedNow = Game.IsKeyPressed(Keys.R);
        bool lPressedNow = Game.IsKeyPressed(Keys.L);

        if (!cPressedNow)
        {
            _cartelPhoneCommandConsumedUntilRelease = false;
        }

        if (!rPressedNow)
        {
            _enemyRaidPhoneCommandConsumedUntilRelease = false;
        }

        /*
         * Latch global pour la touche L :
         * - téléphone ouvert : L appelle/renvoie l'escorte ;
         * - téléphone fermé + joueur dans limousine : L valide la destination.
         *
         * Une action L consommée reste bloquée jusqu'au relâchement complet.
         */
        if (!lPressedNow)
        {
            _highSecurityEscortRouteKeyLatch = false;
            _highSecurityEscortLCommandConsumedUntilRelease = false;
        }

        if (!servicesAvailable)
        {
            // Je consomme aussi une pression commencée téléphone fermé : aucune
            // touche saisie en détention ne doit devenir un appel à la libération.
            ConsumePhoneServiceCommandKeysUntilRelease(
                cPressedNow,
                rPressedNow,
                lPressedNow);
        }

        bool phoneOpen = IsPlayerPhoneOpen(player);

        if (!phoneOpen)
        {
            _cartelPhoneKeyLatch = false;
            _enemyRaidPhoneKeyLatch = false;
            _highSecurityEscortPhoneKeyLatch = false;
            return;
        }

        DrawCartelPhoneContactOverlay(servicesAvailable);

        if (!servicesAvailable || !_playerPhoneNativeOpenThisTick)
        {
            // Je consomme les touches pendant la détention ou une grâce native :
            // aucun appel ne part si GTA ne confirme pas le téléphone ce tick.
            _cartelPhoneKeyLatch = cPressedNow;
            _enemyRaidPhoneKeyLatch = rPressedNow;
            _highSecurityEscortPhoneKeyLatch = lPressedNow;
            ConsumePhoneServiceCommandKeysUntilRelease(
                cPressedNow,
                rPressedNow,
                lPressedNow);

            return;
        }

        if (!cPressedNow)
        {
            _cartelPhoneKeyLatch = false;
        }
        else if (!_cartelPhoneKeyLatch &&
                 !_cartelPhoneCommandConsumedUntilRelease)
        {
            _cartelPhoneKeyLatch = true;
            ToggleCartelCall();
            _cartelPhoneCommandConsumedUntilRelease = true;
        }

        if (!rPressedNow)
        {
            _enemyRaidPhoneKeyLatch = false;
        }
        else if (!_enemyRaidPhoneKeyLatch &&
                 !_enemyRaidPhoneCommandConsumedUntilRelease)
        {
            _enemyRaidPhoneKeyLatch = true;
            CallEnemyRaid();
            _enemyRaidPhoneCommandConsumedUntilRelease = true;
        }

        bool lPressed = lPressedNow;

        if (!lPressed)
        {
            _highSecurityEscortPhoneKeyLatch = false;
        }
        else if (!_highSecurityEscortPhoneKeyLatch && !_highSecurityEscortLCommandConsumedUntilRelease)
        {
            _highSecurityEscortPhoneKeyLatch = true;

            ToggleHighSecurityEscortCall();

            /*
             * Important : on pose ce verrou après ToggleHighSecurityEscortCall(),
             * car Toggle peut nettoyer/réinitialiser l'état interne de l'escorte.
             */
            _highSecurityEscortRouteKeyLatch = true;
            _highSecurityEscortLCommandConsumedUntilRelease = true;
        }
    }

    private void ConsumePhoneServiceCommandKeysUntilRelease(
        bool cPressedNow,
        bool rPressedNow,
        bool lPressedNow)
    {
        if (cPressedNow)
        {
            _cartelPhoneCommandConsumedUntilRelease = true;
        }

        if (rPressedNow)
        {
            _enemyRaidPhoneCommandConsumedUntilRelease = true;
        }

        if (lPressedNow)
        {
            _highSecurityEscortRouteKeyLatch = true;
            _highSecurityEscortLCommandConsumedUntilRelease = true;
        }
    }

    private bool IsPlayerPhoneOpen(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            // Je supprime aussi la grâce à la mort : GTA peut réutiliser le même
            // handle au respawn, mais le nouveau ped doit repartir d'un état net.
            ResetPlayerPhoneOpenDetection();
            return false;
        }

        int playerHandle = player.Handle;
        if (_playerPhoneOpenPlayerHandle != playerHandle)
        {
            // Je ne transporte jamais la grâce téléphone vers un autre ped lors
            // d'un respawn ou d'un changement Franklin/Michael/Trevor.
            ResetPlayerPhoneOpenDetection();
            _playerPhoneOpenPlayerHandle = playerHandle;
        }

        bool nativePhoneOpen = false;
        try
        {
            nativePhoneOpen = Function.Call<bool>(
                (Hash)NativeIsPedRunningMobilePhoneTask,
                playerHandle);
        }
        catch
        {
            // Je laisse la courte grâce absorber une indisponibilité native
            // ponctuelle, sans masquer durablement une erreur ni l'étendre.
        }

        _playerPhoneNativeOpenThisTick = nativePhoneOpen;

        int now = Game.GameTime;
        if (nativePhoneOpen)
        {
            _playerPhoneOpenGraceActive = true;
            _playerPhoneOpenLastConfirmedAt = now;
            return true;
        }

        if (!_playerPhoneOpenGraceActive)
        {
            return false;
        }

        int elapsed = unchecked(now - _playerPhoneOpenLastConfirmedAt);
        if (elapsed >= 0 && elapsed <= PhoneOpenDetectionGraceMs)
        {
            return true;
        }

        _playerPhoneOpenGraceActive = false;
        return false;
    }

    private void ResetPlayerPhoneOpenDetection()
    {
        _playerPhoneNativeOpenThisTick = false;
        _playerPhoneOpenGraceActive = false;
        _playerPhoneOpenLastConfirmedAt = 0;
        _playerPhoneOpenPlayerHandle = 0;
    }

    private void DrawCartelPhoneContactOverlay(bool servicesAvailable)
    {
        int x = 845;
        int y = 420;
        int width = 455;
        int height = 222;

        DrawRect(x, y, width, height, Color.FromArgb(192, 0, 0, 0));
        DrawRect(x, y, width, 34, Color.FromArgb(230, 110, 15, 15));

        DrawText("Contacts téléphone", x + 14, y + 8, 0.31f, Color.White, false, true);

        DrawText(CartelContactName, x + 18, y + 45, 0.42f, Color.White, false, true);

        string cartelStatus;

        if (!servicesAvailable)
        {
            cartelStatus = "C : indisponible pendant transfert / détention";
        }
        else if (HasActiveCartelTeam())
        {
            cartelStatus = "C : rappeler / faire replier l'équipe active";
        }
        else if (_cartelConvoyDismissing)
        {
            cartelStatus = "C : appeler une nouvelle équipe";
        }
        else
        {
            cartelStatus = "C : appeler les hommes de main alliés";
        }

        DrawText(cartelStatus, x + 18, y + 76, 0.285f, Color.FromArgb(230, 230, 230), false, false);

        DrawText(EnemyRaidContactName, x + 18, y + 106, 0.42f, Color.FromArgb(235, 190, 235), false, true);

        string enemyStatus;

        if (!servicesAvailable)
        {
            enemyStatus = "R : indisponible pendant transfert / détention";
        }
        else
        {
            int liveEnemies = CountLiveEnemyRaidMembers();
            int enemyCooldownRemaining = Math.Max(
                0,
                (_nextEnemyRaidCallAllowedAt - Game.GameTime + 999) / 1000);

            if (enemyCooldownRemaining > 0)
            {
                enemyStatus = "R : ennemis disponibles dans " + enemyCooldownRemaining.ToString(CultureInfo.InvariantCulture) + " s";
            }
            else if (liveEnemies > 0)
            {
                enemyStatus = "R : appeler une autre vague ennemie (" + liveEnemies.ToString(CultureInfo.InvariantCulture) + " actifs)";
            }
            else
            {
                enemyStatus = "R : appeler des ennemis armés en véhicules";
            }
        }

        DrawText(enemyStatus, x + 18, y + 137, 0.285f, Color.FromArgb(230, 230, 230), false, false);

        DrawText(HighSecurityEscortContactName, x + 18, y + 164, 0.42f, Color.FromArgb(170, 210, 255), false, true);
        string escortStatus = servicesAvailable
            ? GetHighSecurityEscortPhoneStatus()
            : "L : indisponible pendant transfert / détention";
        DrawText(escortStatus, x + 18, y + 195, 0.285f, Color.FromArgb(230, 230, 230), false, false);
    }

    private void ToggleCartelCall()
    {
        if (Game.GameTime < _nextCartelCallAllowedAt)
        {
            int remaining = Math.Max(1, (_nextCartelCallAllowedAt - Game.GameTime) / 1000);
            ShowStatus("Cartel indisponible encore " + remaining.ToString(CultureInfo.InvariantCulture) + " seconde(s).", 3000);
            return;
        }

        _nextCartelCallAllowedAt = Game.GameTime + CartelCallCooldownMs;

        /*
         * Si une équipe active existe : on la renvoie.
         * Si seules des anciennes équipes sont encore en train de partir : on autorise un nouvel appel.
         */
        if (HasActiveCartelTeam())
        {
            DismissCartelTeam(true);
            return;
        }

        SpawnCartelConvoy();
    }

    private bool HasActiveCartelTeam()
    {
        CleanupCartelHandleSets();

        return _cartelNpcHandles.Count > 0 || _cartelVehicleHandles.Count > 0;
    }

    private void CleanupCartelHandleSets()
    {
        List<int> deadActiveNpcHandles = new List<int>();

        foreach (int handle in _cartelNpcHandles)
        {
            SpawnedNpc npc = FindSpawnedNpcByHandle(handle);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                deadActiveNpcHandles.Add(handle);
            }
        }

        for (int i = 0; i < deadActiveNpcHandles.Count; i++)
        {
            _cartelNpcHandles.Remove(deadActiveNpcHandles[i]);
            _cartelNextCombatOrderAt.Remove(deadActiveNpcHandles[i]);
            _cartelLastGuardRescueAt.Remove(deadActiveNpcHandles[i]);
            _cartelNextGuardPassiveMaintenanceAt.Remove(deadActiveNpcHandles[i]);
            _cartelNextGuardMobilityOrderAt.Remove(deadActiveNpcHandles[i]);

            if (_cartelCachedThreatPed != null &&
                Entity.Exists(_cartelCachedThreatPed) &&
                _cartelCachedThreatPed.Handle == deadActiveNpcHandles[i])
            {
                ClearCachedCartelThreat();
            }
        }

        List<int> deadActiveVehicleHandles = new List<int>();

        foreach (int handle in _cartelVehicleHandles)
        {
            if (!DoesVehicleHandleExist(handle))
            {
                deadActiveVehicleHandles.Add(handle);
            }
        }

        for (int i = 0; i < deadActiveVehicleHandles.Count; i++)
        {
            _cartelVehicleHandles.Remove(deadActiveVehicleHandles[i]);
            ClearCartelVehicleTracking(deadActiveVehicleHandles[i]);
        }

        List<int> deadDismissingNpcHandles = new List<int>();

        foreach (KeyValuePair<int, SpawnedNpc> pair in _cartelDismissingNpcRecords)
        {
            if (pair.Value == null || !Entity.Exists(pair.Value.Ped))
            {
                deadDismissingNpcHandles.Add(pair.Key);
            }
        }

        for (int i = 0; i < deadDismissingNpcHandles.Count; i++)
        {
            _cartelDismissingNpcRecords.Remove(deadDismissingNpcHandles[i]);
            _cartelNextCombatOrderAt.Remove(deadDismissingNpcHandles[i]);
            _cartelLastGuardRescueAt.Remove(deadDismissingNpcHandles[i]);
            _cartelNextGuardPassiveMaintenanceAt.Remove(deadDismissingNpcHandles[i]);
            _cartelNextGuardMobilityOrderAt.Remove(deadDismissingNpcHandles[i]);

            if (_cartelCachedThreatPed != null &&
                Entity.Exists(_cartelCachedThreatPed) &&
                _cartelCachedThreatPed.Handle == deadDismissingNpcHandles[i])
            {
                ClearCachedCartelThreat();
            }
        }

        List<int> deadDismissingVehicleHandles = new List<int>();

        foreach (int handle in _cartelDismissingVehicleHandles)
        {
            if (!DoesVehicleHandleExist(handle))
            {
                deadDismissingVehicleHandles.Add(handle);
            }
        }

        for (int i = 0; i < deadDismissingVehicleHandles.Count; i++)
        {
            _cartelDismissingVehicleHandles.Remove(deadDismissingVehicleHandles[i]);
            ClearCartelVehicleTracking(deadDismissingVehicleHandles[i]);
        }

        _cartelConvoyActive = _cartelNpcHandles.Count > 0 || _cartelVehicleHandles.Count > 0;
        _cartelConvoyDismissing = _cartelDismissingNpcRecords.Count > 0 || _cartelDismissingVehicleHandles.Count > 0;
    }

    private void SpawnCartelConvoy()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Impossible d'appeler le Cartel : joueur invalide.", 3500);
            return;
        }

        EnsureRelationshipGroups();

        CleanupCartelHandleSets();

        ModelIdentity guardModel = ResolveCartelGuardModelIdentity();
        VehicleIdentity vehicleIdentity = ResolveCartelVehicleIdentity();
        WeaponLoadout serviceCarbineLoadout = CreateCartelPrimaryLoadout();

        int createdGuards = 0;
        int createdVehicles = 0;

        for (int vehicleIndex = 0; vehicleIndex < CartelVehicleCount; vehicleIndex++)
        {
            if (createdGuards >= CartelGuardCount)
            {
                break;
            }

            Vector3 spawnPosition = FindCartelVehicleSpawnPosition(player, vehicleIndex);
            float heading = HeadingFromTo(spawnPosition, player.Position);

            Vehicle vehicle = CreateVehicleFromIdentity(vehicleIdentity, spawnPosition, heading);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            RegisterPlacedVehicle(vehicle, vehicleIdentity, spawnPosition, heading, false);
            UpgradeCartelVehicle(vehicle);

            _cartelVehicleHandles.Add(vehicle.Handle);
            createdVehicles++;

            InitializeCartelVehicleTracking(vehicle);

            int seatsForThisVehicle = GetVehicleSeatCapacityIncludingDriver(vehicle);
            int seatsFilled = 0;

            for (int localSeatIndex = 0; localSeatIndex < seatsForThisVehicle; localSeatIndex++)
            {
                if (createdGuards >= CartelGuardCount)
                {
                    break;
                }

                int seat = localSeatIndex == 0 ? -1 : localSeatIndex - 1;

                if (!IsSeatFreeSafe(vehicle, seat))
                {
                    continue;
                }

                Vector3 pedSpawnPosition = spawnPosition + GetSpawnOffsetAroundVehicle(localSeatIndex);
                Ped guard = CreatePedFromModelIdentity(guardModel, pedSpawnPosition, heading);

                if (!Entity.Exists(guard))
                {
                    continue;
                }

                SpawnedNpc spawned = RegisterSpawnedNpc(
                    guard,
                    NpcBehavior.Bodyguard,
                    true,
                    false,
                    guardModel,
                    serviceCarbineLoadout.Clone(),
                    CartelGuardHealth,
                    CartelGuardArmor,
                    pedSpawnPosition,
                    heading,
                    _selectedPatrolRadius);

                if (spawned == null || !Entity.Exists(spawned.Ped))
                {
                    DeleteEntitySafe(guard);
                    continue;
                }

                _cartelNpcHandles.Add(spawned.Ped.Handle);

                GiveCartelWeapons(spawned.Ped);
                ConfigureCartelGuard(spawned, vehicle, seat);
                PutPedIntoVehicleSafe(spawned.Ped, vehicle, seat);

                createdGuards++;
                seatsFilled++;
            }

            if (seatsFilled == 0)
            {
                _cartelVehicleHandles.Remove(vehicle.Handle);
                ClearCartelVehicleTracking(vehicle.Handle);
                DeleteEntitySafe(vehicle);
            }
        }

        if (createdGuards == 0)
        {
            CleanupCartelHandleSets();
            ShowStatus("Cartel : aucun homme de main n'a pu être créé.", 5000);
            return;
        }

        _cartelConvoyActive = true;
        _nextCartelThinkAt = 0;

        OrderCartelConvoyToPlayer(true);

        ShowStatus(
            "Cartel appelé : " +
            createdGuards.ToString(CultureInfo.InvariantCulture) +
            " hommes arrivent rapidement en " +
            createdVehicles.ToString(CultureInfo.InvariantCulture) +
            " Baller6 blindée(s).",
            6500);
    }

    private ModelIdentity ResolveCartelGuardModelIdentity()
    {
        try
        {
            for (int i = 0; i < _allModelOptions.Count; i++)
            {
                ModelOption option = _allModelOptions[i];

                if (option == null || option.IsCustom)
                {
                    continue;
                }

                if (string.Equals(option.DisplayName, "CartelGoons01GMM", StringComparison.OrdinalIgnoreCase))
                {
                    return new ModelIdentity
                    {
                        IsCustom = false,
                        Name = option.DisplayName,
                        Hash = option.Hash,
                        DisplayName = option.DisplayName
                    };
                }
            }
        }
        catch
        {
        }

        return new ModelIdentity
        {
            IsCustom = true,
            Name = "g_m_m_cartelgoons_01",
            Hash = 0,
            DisplayName = "CartelGoons01GMM"
        };
    }

    private VehicleIdentity ResolveCartelVehicleIdentity()
    {
        return new VehicleIdentity
        {
            Name = "Baller6",
            Hash = EnumToIntHash(VehicleHash.Baller6),
            DisplayName = "Baller6 blindée Cartel"
        };
    }

    private WeaponLoadout CreateCartelPrimaryLoadout()
    {
        return new WeaponLoadout
        {
            Weapon = WeaponHash.ServiceCarbine,
            Ammo = 9999,
            Tint = 0,
            Preset = WeaponUpgradePreset.Tactique,
            ExtendedClip = true,
            Suppressor = false,
            Flashlight = true,
            Grip = true,
            Scope = WeaponScopeMode.Small,
            Muzzle = false,
            ImprovedBarrel = false,
            Mk2Ammo = WeaponMk2AmmoMode.Standard
        };
    }

    private void GiveCartelWeapons(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        try
        {
            ped.Weapons.RemoveAll();

            WeaponLoadout carbine = CreateCartelPrimaryLoadout();
            GiveWeaponLoadout(ped, carbine, false);

            ped.Weapons.Give(WeaponHash.MachinePistol, 9999, false, true);

            if (ped.IsInVehicle())
            {
                ped.Weapons.Select(WeaponHash.MachinePistol, true);
            }
            else
            {
                ped.Weapons.Select(WeaponHash.ServiceCarbine, true);
            }
        }
        catch
        {
            try
            {
                ped.Weapons.Give(WeaponHash.CarbineRifle, 9999, true, true);
                ped.Weapons.Give(WeaponHash.MicroSMG, 9999, false, true);
                ped.Weapons.Select(WeaponHash.CarbineRifle, true);
            }
            catch
            {
                ped.Weapons.Select(WeaponHash.Unarmed);
            }
        }
    }

    private void ConfigureCartelGuard(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)
    {
        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            return;
        }

        spawned.BaseBehavior = NpcBehavior.Bodyguard;
        spawned.Behavior = NpcBehavior.Bodyguard;
        spawned.Activated = false;
        spawned.IsReturningHome = false;
        spawned.LastCombatActivityAt = Game.GameTime;
        spawned.NextBodyguardTaskAt = 0;

        if (Entity.Exists(assignedVehicle))
        {
            spawned.BodyguardAssignedVehicleHandle = assignedVehicle.Handle;
            spawned.BodyguardAssignedSeat = assignedSeat;
            spawned.BodyguardIsDriver = assignedSeat == -1;
        }

        spawned.Ped.Armor = CartelGuardArmor;
        spawned.Ped.MaxHealth = CartelGuardHealth;
        spawned.Ped.Health = CartelGuardHealth;
        spawned.Ped.Accuracy = 64;
        spawned.Ped.ShootRate = 900;
        spawned.Ped.IsEnemy = false;

        /*
         * Je bloque les evenements ambiants parce que le script gere deja
         * les ordres de protection, de convoi, de descente et de remontée véhicule.
         */
        spawned.Ped.BlockPermanentEvents = true;
        spawned.Ped.AlwaysKeepTask = true;

        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, spawned.Ped.Handle, _allyGroupHash);
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, spawned.Ped.Handle, 2);
        Function.Call(Hash.SET_PED_COMBAT_RANGE, spawned.Ped.Handle, 2);
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, spawned.Ped.Handle, 2);
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, spawned.Ped.Handle, 0, false);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 0, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 5, true);
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 46, true);

        int initialMaintenanceStagger = CalculateCartelHandleStaggerMs(
            spawned.Ped.Handle,
            CartelGuardPassiveMaintenanceJitterMs);

        spawned.NextThinkAt = Game.GameTime + initialMaintenanceStagger;
        spawned.NextBodyguardTaskAt = Game.GameTime + initialMaintenanceStagger;

        _cartelNextGuardPassiveMaintenanceAt[spawned.Ped.Handle] = Game.GameTime + initialMaintenanceStagger;
        _cartelNextGuardMobilityOrderAt[spawned.Ped.Handle] = Game.GameTime + initialMaintenanceStagger;

        CreateOrUpdateNpcBlip(spawned);
    }

    private void UpgradeCartelVehicle(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int handle = vehicle.Handle;

        /*
         * Très important :
         * Cette méthode peut être appelée plusieurs fois par la logique Cartel.
         * Avant, elle réappliquait les mods + la remise au sol native en boucle,
         * ce qui provoquait les petites pulsations physiques du véhicule.
         *
         * Maintenant :
         * - upgrade lourd une seule fois ;
         * - maintenance légère seulement de temps en temps ;
         * - aucune remise au sol répétée.
         */
        if (!_cartelFullyUpgradedVehicleHandles.Contains(handle))
        {
            _cartelFullyUpgradedVehicleHandles.Add(handle);

            vehicle.IsPersistent = true;

            try
            {
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);

                Function.Call(Hash.SET_VEHICLE_MOD_KIT, vehicle.Handle, 0);

                // Mods GTA :
                // 11 moteur, 12 freins, 13 transmission, 15 suspension, 16 blindage, 18 turbo.
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 11, 3, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 12, 2, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 13, 2, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 15, 3, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 16, 4, false);
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, vehicle.Handle, 18, true);

                Function.Call(Hash.SET_VEHICLE_COLOURS, vehicle.Handle, 0, 0);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, vehicle.Handle, 0, 0);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, vehicle.Handle, 1);

                Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, vehicle.Handle, false);
                Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, vehicle.Handle, 1000.0f);
                Function.Call(Hash.SET_VEHICLE_PETROL_TANK_HEALTH, vehicle.Handle, 1000.0f);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, vehicle.Handle, 0.0f);
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);

                /*
                 * Autorisé une seule fois au moment de l'initialisation.
                 * Ne jamais appeler ça en boucle : ça crée des pulsations.
                 */
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            }
            catch
            {
                // Certaines options peuvent ne pas être supportées selon le véhicule/build.
            }

            return;
        }

        MaintainCartelVehicleSoftState(vehicle);
    }

    private void MaintainCartelVehicleSoftState(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int handle = vehicle.Handle;

        int lastMaintenanceAt;

        if (_cartelLastVehicleSoftMaintenanceAt.TryGetValue(handle, out lastMaintenanceAt) &&
            Game.GameTime - lastMaintenanceAt < 3500)
        {
            return;
        }

        _cartelLastVehicleSoftMaintenanceAt[handle] = Game.GameTime;

        /*
         * Maintenance légère uniquement.
         *
         * On évite volontairement :
         * - la remise au sol native
         * - le kit de mods
         * - les mods GTA
         * - un repositionnement direct
         * - une modification de vélocité
         *
         * Ces appels peuvent modifier physiquement le véhicule ou réinitialiser son état.
         */
        try
        {
            vehicle.IsPersistent = true;

            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
            Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, vehicle.Handle, false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
        }
        catch
        {
            // État véhicule temporairement invalide, on ignore.
        }
    }

    private Vector3 FindCartelVehicleSpawnPosition(Ped player, int convoyIndex)
    {
        Vector3 roadPoint;

        if (TryFindHiddenRoadPointNearPlayer(
            player,
            convoyIndex,
            CartelSpawnMinDistance,
            CartelSpawnMaxDistance,
            out roadPoint))
        {
            return roadPoint;
        }

        Vector3 playerPos = player.Position;
        Vector3 camForward = GetGameplayCameraForwardVector();

        if (camForward.Length() < 0.001f)
        {
            camForward = Normalize(player.ForwardVector);
        }

        if (camForward.Length() < 0.001f)
        {
            camForward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 baseDirection = -camForward;
        Vector3 right = Normalize(new Vector3(baseDirection.Y, -baseDirection.X, 0.0f));

        Vector3 fallback =
            playerPos +
            baseDirection * (CartelSpawnMinDistance + convoyIndex * 10.0f) +
            right * ((convoyIndex - 1) * 12.0f);

        Vector3 safe = World.GetSafeCoordForPed(fallback, false, 16);

        if (!IsZeroVector(safe) && !IsPointInPlayerView(player, safe))
        {
            return safe + new Vector3(0.0f, 0.0f, 0.45f);
        }

        float ground = World.GetGroundHeight(new Vector3(fallback.X, fallback.Y, fallback.Z + 1000.0f));

        if (Math.Abs(ground) > 0.001f)
        {
            fallback.Z = ground + 0.45f;
        }

        return fallback;
    }

    private bool TryFindHiddenRoadPointNearPlayer(Ped player, int seedIndex, float minDistance, float maxDistance, out Vector3 roadPoint)
    {
        roadPoint = Vector3.Zero;

        if (!Entity.Exists(player))
        {
            return false;
        }

        Vector3 playerPos = player.Position;
        Vector3 camForward = GetGameplayCameraForwardVector();

        if (camForward.Length() < 0.001f)
        {
            camForward = Normalize(player.ForwardVector);
        }

        if (camForward.Length() < 0.001f)
        {
            camForward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 hiddenBase = -camForward;

        float[] angleOffsets =
        {
            0.0f, -28.0f, 28.0f, -52.0f, 52.0f,
            -75.0f, 75.0f, 110.0f, -110.0f, 145.0f, -145.0f, 180.0f
        };

        for (int attempt = 0; attempt < 28; attempt++)
        {
            float t = attempt / 27.0f;
            float distance = minDistance + (maxDistance - minDistance) * t;

            int angleIndex = (attempt + seedIndex * 3) % angleOffsets.Length;
            float angle = angleOffsets[angleIndex] + seedIndex * 11.0f;

            Vector3 direction = RotateDirection2D(hiddenBase, angle);
            Vector3 right = Normalize(new Vector3(direction.Y, -direction.X, 0.0f));
            float lateral = ((seedIndex % 3) - 1) * 9.0f;

            Vector3 desired = playerPos + direction * distance + right * lateral;

            Vector3 node;

            if (!TryGetClosestVehicleNode(desired, attempt % 6, out node))
            {
                continue;
            }

            float actualDistance = node.DistanceTo(playerPos);

            if (actualDistance < minDistance * 0.75f || actualDistance > maxDistance * 1.35f)
            {
                continue;
            }

            if (IsPointInPlayerView(player, node))
            {
                continue;
            }

            roadPoint = node + new Vector3(0.0f, 0.0f, 0.45f);
            return true;
        }

        return false;
    }

    private bool TryGetClosestVehicleNode(Vector3 desired, int nth, out Vector3 node)
    {
        node = Vector3.Zero;

        try
        {
            OutputArgument outPos = new OutputArgument();

            bool found = Function.Call<bool>(
                Hash.GET_NTH_CLOSEST_VEHICLE_NODE,
                desired.X,
                desired.Y,
                desired.Z,
                Math.Max(0, nth),
                outPos,
                1,
                3.0f,
                0.0f);

            if (!found)
            {
                return false;
            }

            node = outPos.GetResult<Vector3>();

            return !IsZeroVector(node);
        }
        catch
        {
            return false;
        }
    }

    private static Vector3 GetGameplayCameraForwardVector()
    {
        Vector3 rotation = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_ROT, 2);

        float pitch = rotation.X * (float)Math.PI / 180.0f;
        float yaw = rotation.Z * (float)Math.PI / 180.0f;

        float cosPitch = (float)Math.Cos(pitch);

        return Normalize(new Vector3(
            -(float)Math.Sin(yaw) * cosPitch,
            (float)Math.Cos(yaw) * cosPitch,
            (float)Math.Sin(pitch)));
    }

    private static Vector3 RotateDirection2D(Vector3 direction, float degrees)
    {
        float radians = degrees * (float)Math.PI / 180.0f;
        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);

        Vector3 normalized = Normalize(new Vector3(direction.X, direction.Y, 0.0f));

        return Normalize(new Vector3(
            normalized.X * cos - normalized.Y * sin,
            normalized.X * sin + normalized.Y * cos,
            0.0f));
    }

    private bool IsPointInPlayerView(Ped player, Vector3 point)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        Vector3 camPos = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_COORD);
        Vector3 camForward = GetGameplayCameraForwardVector();

        Vector3 toPoint = Normalize(point - camPos);

        if (toPoint.Length() < 0.001f || camForward.Length() < 0.001f)
        {
            return false;
        }

        float dot = camForward.X * toPoint.X + camForward.Y * toPoint.Y + camForward.Z * toPoint.Z;
        float distance = camPos.DistanceTo(point);

        if (distance < 38.0f)
        {
            return true;
        }

        return dot > 0.35f && distance < 180.0f;
    }

    private bool IsEntityLikelyVisibleToPlayer(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return false;
        }

        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return false;
        }

        float distance = player.Position.DistanceTo(entity.Position);

        if (distance < 35.0f)
        {
            return true;
        }

        try
        {
            if (Function.Call<bool>(Hash.IS_ENTITY_ON_SCREEN, entity.Handle))
            {
                return true;
            }
        }
        catch
        {
        }

        Vector3 camPos = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_COORD);
        Vector3 camForward = GetGameplayCameraForwardVector();
        Vector3 toEntity = Normalize(entity.Position - camPos);

        if (toEntity.Length() < 0.001f || camForward.Length() < 0.001f)
        {
            return false;
        }

        float dot = camForward.X * toEntity.X + camForward.Y * toEntity.Y + camForward.Z * toEntity.Z;

        if (dot < 0.28f)
        {
            return false;
        }

        if (distance > 180.0f)
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(
                Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                player.Handle,
                entity.Handle,
                17);
        }
        catch
        {
            return dot > 0.55f;
        }
    }

    private static Vector3 GetSpawnOffsetAroundVehicle(int index)
    {
        switch (index)
        {
            case 0:
                return new Vector3(0.0f, -1.5f, 0.0f);

            case 1:
                return new Vector3(1.4f, 0.8f, 0.0f);

            case 2:
                return new Vector3(-1.4f, 0.8f, 0.0f);

            case 3:
                return new Vector3(1.4f, -0.8f, 0.0f);

            default:
                return new Vector3(-1.4f, -0.8f, 0.0f);
        }
    }

    private int GetVehicleSeatCapacityIncludingDriver(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return 0;
        }

        int passengers = 3;

        try
        {
            passengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle);
        }
        catch
        {
            passengers = 3;
        }

        return Clamp(passengers + 1, 1, 8);
    }

    private bool IsSeatFreeSafe(Vehicle vehicle, int seat)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, vehicle.Handle, seat, false);
        }
        catch
        {
            return true;
        }
    }

    private bool PutPedIntoVehicleSafe(Ped ped, Vehicle vehicle, int seat)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            Function.Call(Hash.SET_PED_INTO_VEHICLE, ped.Handle, vehicle.Handle, seat);

            if (IsPedOccupyingVehicleSeatSafe(ped, vehicle, seat))
            {
                return true;
            }
        }
        catch
        {
            // Fallback ci-dessous : on garde le mod robuste si la native directe échoue.
        }

        try
        {
            Function.Call(
                Hash.TASK_ENTER_VEHICLE,
                ped.Handle,
                vehicle.Handle,
                5000,
                seat,
                2.0f,
                1,
                0);
        }
        catch
        {
        }

        /*
         * TASK_ENTER_VEHICLE est asynchrone. Pour un spawn critique, on ne considère
         * pas que le siège est acquis tant que le ped n'est pas réellement dedans.
         */
        return IsPedOccupyingVehicleSeatSafe(ped, vehicle, seat);
    }

    private bool IsPedOccupyingVehicleSeatSafe(Ped ped, Vehicle vehicle, int seat)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            int occupantHandle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, seat, false);
            return occupantHandle == ped.Handle;
        }
        catch
        {
            return false;
        }
    }

    private void OrderCartelConvoyToPlayer(bool force)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return;
        }

        foreach (int vehicleHandle in _cartelVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandle);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            IssueCartelFastFollowOrder(vehicle, player, force);
        }
    }

    private void ConfigureCartelDriver(Ped driver)
    {
        if (!Entity.Exists(driver))
        {
            return;
        }

        try
        {
            /*
             * On rend le conducteur compétent, mais on ne le rend pas "kamikaze".
             * L'ancien réglage trop agressif + vitesse forcée créait des collisions violentes.
             */
            Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1.0f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 0.42f);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, CartelRapidDrivingStyle);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, driver.Handle, false);
            Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, driver.Handle, true);
        }
        catch
        {
            // On ignore proprement : certains états véhicule/ped peuvent être temporaires.
        }
    }

    private Ped GetDriverOfVehicle(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return null;
        }

        try
        {
            int driverHandle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, -1, false);

            if (driverHandle == 0)
            {
                return null;
            }

            for (int i = 0; i < _spawnedNpcs.Count; i++)
            {
                SpawnedNpc npc = _spawnedNpcs[i];

                if (npc != null && Entity.Exists(npc.Ped) && npc.Ped.Handle == driverHandle)
                {
                    return npc.Ped;
                }
            }

            foreach (KeyValuePair<int, SpawnedNpc> pair in _cartelDismissingNpcRecords)
            {
                SpawnedNpc npc = pair.Value;

                if (npc != null && Entity.Exists(npc.Ped) && npc.Ped.Handle == driverHandle)
                {
                    return npc.Ped;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private void UpdateCartelConvoyState(Ped player)
    {
        CleanupCartelHandleSets();

        if (!_cartelConvoyActive && !_cartelConvoyDismissing)
        {
            return;
        }

        if (Game.GameTime < _nextCartelThinkAt)
        {
            return;
        }

        _nextCartelThinkAt = Game.GameTime + CartelThinkIntervalMs;

        if (!Entity.Exists(player) || player.IsDead)
        {
            DismissCartelTeam(false);
            return;
        }

        if (_cartelConvoyActive)
        {
            MaintainCartelTeamWeaponsAndDrivers(player, false);
        }

        if (_cartelConvoyDismissing)
        {
            UpdateCartelDismissal(player, false);
        }
    }

    private void MaintainCartelTeamWeaponsAndDrivers(Ped player, bool latePass)
    {
        CleanupCartelHandleSets();

        /*
         * Correction performance conservee :
         * - la passe tardive ne lance pas de scan lourd ;
         * - la menace est scannee / cachee uniquement par ResolveCartelThreat ;
         * - la synchronisation pied/vehicule ci-dessous ne parcourt pas tous les PNJ du monde.
         */
        Ped cartelThreat = ResolveCartelThreat(player, latePass);

        if (Entity.Exists(cartelThreat))
        {
            EngageCartelTeamThreat(cartelThreat, player, latePass);
            return;
        }

        List<int> activeVehicles = new List<int>(_cartelVehicleHandles);

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            if (!latePass)
            {
                UpgradeCartelVehicle(vehicle);
                RescueCartelVehicleIfNeeded(vehicle, player, i);
            }

            /*
             * Joueur à pied :
             * le véhicule approche du joueur ; quand il est arrivé, les gardes
             * descendent via SynchronizeCartelGuardWithPlayerState().
             *
             * Joueur en véhicule :
             * cet ordre redevient une escorte classique.
             */
            IssueCartelFastFollowOrder(vehicle, player, false);
        }

        List<int> activeNpcs = new List<int>(_cartelNpcHandles);

        for (int i = 0; i < activeNpcs.Count; i++)
        {
            SpawnedNpc npc = FindSpawnedNpcByHandle(activeNpcs[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            if (ShouldRunCartelGuardPassiveMaintenance(npc.Ped, false))
            {
                MaintainCartelGuardPassiveState(npc, true);
            }

            SynchronizeCartelGuardWithPlayerState(npc, player, latePass);

            if (!latePass)
            {
                RescueCartelGuardIfNeeded(npc, player, i);
            }
        }
    }

    private void MaintainCartelGuardPassiveState(SpawnedNpc npc, bool includeWeaponSelection)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        npc.BaseBehavior = NpcBehavior.Bodyguard;
        npc.Behavior = NpcBehavior.Bodyguard;
        npc.Ped.IsEnemy = false;
        npc.Ped.BlockPermanentEvents = true;
        npc.Ped.AlwaysKeepTask = true;

        try
        {
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
        }
        catch
        {
        }

        if (!includeWeaponSelection)
        {
            return;
        }

        if (npc.Ped.IsInVehicle())
        {
            TrySelectPedWeapon(npc.Ped, WeaponHash.MachinePistol);
        }
        else
        {
            TrySelectPedWeapon(npc.Ped, WeaponHash.ServiceCarbine);
        }

        if (npc.Ped.IsInVehicle() && IsPedDriverOfAnyCartelVehicle(npc.Ped))
        {
            ConfigureCartelDriver(npc.Ped);
        }
    }

    private bool ShouldRunCartelGuardPassiveMaintenance(Ped ped, bool force)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt =
            Game.GameTime +
            CartelGuardPassiveMaintenanceIntervalMs +
            CalculateCartelHandleStaggerMs(ped.Handle, CartelGuardPassiveMaintenanceJitterMs);

        if (force)
        {
            _cartelNextGuardPassiveMaintenanceAt[ped.Handle] = nextAt;
            return true;
        }

        int nextMaintenanceAt;

        if (_cartelNextGuardPassiveMaintenanceAt.TryGetValue(ped.Handle, out nextMaintenanceAt) &&
            Game.GameTime < nextMaintenanceAt)
        {
            return false;
        }

        _cartelNextGuardPassiveMaintenanceAt[ped.Handle] = nextAt;
        return true;
    }

    private static int CalculateCartelHandleStaggerMs(int handle, int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            return 0;
        }

        unchecked
        {
            uint mixed = (uint)handle;
            mixed ^= mixed >> 16;
            mixed *= 2246822519U;
            mixed ^= mixed >> 13;
            mixed *= 3266489917U;
            mixed ^= mixed >> 16;

            return (int)(mixed % (uint)maxExclusive);
        }
    }

    private void SynchronizeCartelGuardWithPlayerState(SpawnedNpc guard, Ped player, bool latePass)
    {
        if (guard == null ||
            !Entity.Exists(guard.Ped) ||
            guard.Ped.IsDead ||
            !Entity.Exists(player) ||
            player.IsDead)
        {
            return;
        }

        if (player.IsInVehicle() && Entity.Exists(player.CurrentVehicle))
        {
            ReturnCartelGuardToVehicleIfNeeded(guard, player, false);
            return;
        }

        KeepCartelGuardWithPlayerOnFoot(guard, player, false);
    }

    private void KeepCartelGuardWithPlayerOnFoot(SpawnedNpc guard, Ped player, bool combatMode)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(player))
        {
            return;
        }

        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle currentVehicle = guard.Ped.CurrentVehicle;

            if (ShouldCartelGuardLeaveVehicleForPlayerOnFoot(guard.Ped, currentVehicle, player, combatMode))
            {
                CommandCartelGuardLeaveVehicle(guard, currentVehicle, combatMode);
            }

            return;
        }

        if (!combatMode)
        {
            FollowCartelGuardOnFoot(guard, player, false);
        }
    }

    private void ReturnCartelGuardToVehicleIfNeeded(SpawnedNpc guard, Ped player, bool combatMode)
    {
        if (guard == null ||
            !Entity.Exists(guard.Ped) ||
            guard.Ped.IsDead ||
            !Entity.Exists(player))
        {
            return;
        }

        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle currentVehicle = guard.Ped.CurrentVehicle;

            if (_cartelVehicleHandles.Contains(currentVehicle.Handle))
            {
                if (IsPedDriverOfVehicle(guard.Ped, currentVehicle))
                {
                    ConfigureCartelDriver(guard.Ped);
                }

                return;
            }

            /*
             * Cas rare : le garde est dans un vehicule qui n'est pas un vehicule Cartel.
             * On le fait sortir, puis la prochaine passe le fera remonter dans une Baller Cartel.
             */
            CommandCartelGuardLeaveVehicle(guard, currentVehicle, combatMode);
            return;
        }

        Vehicle targetVehicle = FindBestActiveCartelVehicleForGuard(guard);

        if (!Entity.Exists(targetVehicle))
        {
            return;
        }

        int seat = FindBestCartelSeatForGuard(guard, targetVehicle);

        if (seat == 999)
        {
            CommandCartelGuardMoveToVehicle(guard, targetVehicle);
            return;
        }

        CommandCartelGuardEnterVehicle(guard, targetVehicle, seat);
    }

    private Vehicle FindBestActiveCartelVehicleForGuard(SpawnedNpc guard)
    {
        if (guard == null || !Entity.Exists(guard.Ped))
        {
            return null;
        }

        Vehicle assignedVehicle = FindVehicleByHandle(guard.BodyguardAssignedVehicleHandle);

        if (Entity.Exists(assignedVehicle) &&
            _cartelVehicleHandles.Contains(assignedVehicle.Handle) &&
            IsVehicleDriveable(assignedVehicle))
        {
            return assignedVehicle;
        }

        Vehicle bestVehicle = null;
        float bestDistance = float.MaxValue;

        foreach (int handle in _cartelVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            float distance = vehicle.Position.DistanceTo(guard.Ped.Position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestVehicle = vehicle;
            }
        }

        return bestVehicle;
    }

    private int FindBestCartelSeatForGuard(SpawnedNpc guard, Vehicle vehicle)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(vehicle))
        {
            return 999;
        }

        int assignedSeat = guard.BodyguardAssignedSeat;
        bool assignedAsDriver = guard.BodyguardIsDriver || assignedSeat == -1;

        if (assignedSeat != 999 && IsSeatFreeSafe(vehicle, assignedSeat))
        {
            return assignedSeat;
        }

        if (assignedAsDriver && IsSeatFreeSafe(vehicle, -1))
        {
            return -1;
        }

        int passengerSeat = FindFreePassengerSeatForCartel(vehicle);

        if (passengerSeat != 999)
        {
            return passengerSeat;
        }

        return 999;
    }

    private int FindFreePassengerSeatForCartel(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        int passengers = 3;

        try
        {
            passengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle);
        }
        catch
        {
            passengers = 3;
        }

        for (int seat = 0; seat < passengers; seat++)
        {
            if (IsSeatFreeSafe(vehicle, seat))
            {
                return seat;
            }
        }

        return 999;
    }

    private bool ShouldCartelGuardLeaveVehicleForPlayerOnFoot(Ped guard, Vehicle vehicle, Ped player, bool combatMode)
    {
        if (!Entity.Exists(guard) || !Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        if (player.IsInVehicle())
        {
            return false;
        }

        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

        /*
         * Véhicule mort / bloqué / sans conducteur :
         * les gardes ne doivent pas rester coincés dedans.
         */
        if (!IsVehicleDriveable(vehicle))
        {
            return true;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return true;
        }

        /*
         * Arrivée normale : la Baller s'approche du joueur, ralentit,
         * puis tout le monde descend pour continuer à pied.
         */
        if (distanceToPlayer <= CartelVehicleFootExitDistance &&
            vehicle.Speed <= CartelVehicleFootExitSpeed)
        {
            return true;
        }

        /*
         * Si le véhicule n'arrive pas à rejoindre le joueur à pied,
         * on autorise la sortie à pied au lieu d'attendre indéfiniment.
         */
        if (HasCartelVehicleFailedToApproachFootPlayer(vehicle, player))
        {
            return true;
        }

        /*
         * En combat, on est un peu plus permissif : si le joueur est à pied,
         * le Cartel doit rapidement devenir une protection à pied, pas rester
         * éternellement en drive-by.
         */
        if (combatMode &&
            distanceToPlayer <= CartelVehicleFootExitDistance + 10.0f &&
            vehicle.Speed <= CartelVehicleFootExitSpeed + 1.5f)
        {
            return true;
        }

        return false;
    }

    private bool HasCartelVehicleFailedToApproachFootPlayer(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player) || player.IsInVehicle())
        {
            return false;
        }

        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

        if (distanceToPlayer <= CartelVehicleFootExitDistance ||
            distanceToPlayer > CartelVehicleForcedFootExitMaxDistance)
        {
            return false;
        }

        Vector3 lastPosition;
        int lastMoveAt;

        if (!_cartelLastVehiclePositions.TryGetValue(vehicle.Handle, out lastPosition) ||
            !_cartelLastVehicleMoveAt.TryGetValue(vehicle.Handle, out lastMoveAt))
        {
            return false;
        }

        float moved = vehicle.Position.DistanceTo(lastPosition);

        return moved <= 4.0f &&
               vehicle.Speed <= 2.0f &&
               Game.GameTime - lastMoveAt >= CartelStuckTimeoutMs;
    }

    private bool CommandCartelGuardLeaveVehicle(SpawnedNpc guard, Vehicle vehicle, bool combatMode)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        if (!guard.Ped.IsInVehicle())
        {
            return false;
        }

        if (!CanIssueCartelGuardMobilityOrder(guard.Ped, false))
        {
            return false;
        }

        guard.BodyguardAssignedVehicleHandle = vehicle.Handle;

        try
        {
            int currentSeat = (int)guard.Ped.SeatIndex;

            if (currentSeat >= -1)
            {
                guard.BodyguardAssignedSeat = currentSeat;
                guard.BodyguardIsDriver = currentSeat == -1;
            }
        }
        catch
        {
        }

        try
        {
            guard.Ped.IsEnemy = false;
            guard.Ped.BlockPermanentEvents = true;
            guard.Ped.AlwaysKeepTask = true;

            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, guard.Ped.Handle, _allyGroupHash);

            Function.Call(
                Hash.TASK_LEAVE_VEHICLE,
                guard.Ped.Handle,
                vehicle.Handle,
                combatMode ? 256 : 0);
        }
        catch
        {
            return false;
        }

        /*
         * Après la sortie, on permet à l'ordre de combat à pied de repartir proprement.
         */
        _cartelNextCombatOrderAt.Remove(guard.Ped.Handle);

        return true;
    }

    private bool CommandCartelGuardEnterVehicle(SpawnedNpc guard, Vehicle vehicle, int seat)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(vehicle) || seat == 999)
        {
            return false;
        }

        if (guard.Ped.IsInVehicle(vehicle))
        {
            return true;
        }

        if (!CanIssueCartelGuardMobilityOrder(guard.Ped, false))
        {
            return false;
        }

        guard.BodyguardAssignedVehicleHandle = vehicle.Handle;
        guard.BodyguardAssignedSeat = seat;
        guard.BodyguardIsDriver = seat == -1;

        try
        {
            guard.Ped.IsEnemy = false;
            guard.Ped.BlockPermanentEvents = true;
            guard.Ped.AlwaysKeepTask = true;

            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, guard.Ped.Handle, _allyGroupHash);

            Function.Call(
                Hash.TASK_ENTER_VEHICLE,
                guard.Ped.Handle,
                vehicle.Handle,
                9000,
                seat,
                2.4f,
                1,
                0);
        }
        catch
        {
            return false;
        }

        return true;
    }

    private bool CommandCartelGuardMoveToVehicle(SpawnedNpc guard, Vehicle vehicle)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        if (!CanIssueCartelGuardMobilityOrder(guard.Ped, false))
        {
            return false;
        }

        try
        {
            Function.Call(
                Hash.TASK_GO_TO_ENTITY,
                guard.Ped.Handle,
                vehicle.Handle,
                -1,
                5.5f,
                2.0f,
                1073741824,
                0);
        }
        catch
        {
            return false;
        }

        return true;
    }

    private void FollowCartelGuardOnFoot(SpawnedNpc guard, Ped player, bool force)
    {
        if (guard == null ||
            !Entity.Exists(guard.Ped) ||
            guard.Ped.IsDead ||
            !Entity.Exists(player) ||
            player.IsDead ||
            guard.Ped.IsInVehicle())
        {
            return;
        }

        if (!CanIssueCartelGuardFootFollowOrder(guard, force))
        {
            return;
        }

        float distance = guard.Ped.Position.DistanceTo(player.Position);

        try
        {
            guard.Ped.IsEnemy = false;
            guard.Ped.BlockPermanentEvents = true;
            guard.Ped.AlwaysKeepTask = true;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, guard.Ped.Handle, _allyGroupHash);
        }
        catch
        {
        }

        if (distance > CartelGuardFootStandDistance)
        {
            int handleSeed = Math.Abs(guard.Ped.Handle);

            float offsetSide = ((handleSeed % 5) - 2) * 0.85f;
            float offsetBack = -CartelGuardFootFollowDistance - ((handleSeed % 3) * 0.75f);
            float speed = distance > 18.0f ? 3.0f : 2.0f;

            try
            {
                Function.Call(
                    Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY,
                    guard.Ped.Handle,
                    player.Handle,
                    offsetSide,
                    offsetBack,
                    0.0f,
                    speed,
                    -1,
                    1.6f,
                    true);
            }
            catch
            {
            }
        }
        else
        {
            try
            {
                Function.Call(Hash.TASK_STAND_STILL, guard.Ped.Handle, 850);
            }
            catch
            {
            }
        }
    }

    private bool CanIssueCartelGuardMobilityOrder(Ped ped, bool force)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        if (!force)
        {
            int nextAt;

            if (_cartelNextGuardMobilityOrderAt.TryGetValue(ped.Handle, out nextAt) &&
                Game.GameTime < nextAt)
            {
                return false;
            }
        }

        _cartelNextGuardMobilityOrderAt[ped.Handle] =
            Game.GameTime +
            CartelGuardMobilityOrderIntervalMs +
            CalculateCartelHandleStaggerMs(ped.Handle, CartelGuardPassiveMaintenanceJitterMs);

        return true;
    }

    private bool CanIssueCartelGuardFootFollowOrder(SpawnedNpc guard, bool force)
    {
        if (guard == null || !Entity.Exists(guard.Ped))
        {
            return false;
        }

        if (!force && Game.GameTime < guard.NextBodyguardTaskAt)
        {
            return false;
        }

        guard.NextBodyguardTaskAt =
            Game.GameTime +
            CartelGuardFootFollowIntervalMs +
            CalculateCartelHandleStaggerMs(guard.Ped.Handle, CartelGuardPassiveMaintenanceJitterMs);

        return true;
    }

    private bool ShouldCartelGuardReturnToVehicleDuringCombat(SpawnedNpc guard, Ped threat, Ped player)
    {
        if (guard == null ||
            !Entity.Exists(guard.Ped) ||
            !Entity.Exists(threat) ||
            !Entity.Exists(player) ||
            !player.IsInVehicle())
        {
            return false;
        }

        if (guard.Ped.IsInVehicle())
        {
            return false;
        }

        float threatDistance = guard.Ped.Position.DistanceTo(threat.Position);

        /*
         * Si l'ennemi est vraiment sur le garde, il se défend à pied.
         * Sinon, quand le joueur repart en véhicule, il remonte dans une Baller.
         */
        if (threatDistance <= CartelGuardImmediateThreatDistance &&
            CanPedSeeEntity(guard.Ped, threat, 55.0f))
        {
            return false;
        }

        return true;
    }

    private Ped ResolveCartelThreat(Ped player, bool latePass)
    {
        Ped cachedThreat = GetCachedCartelThreat(player);

        if (Entity.Exists(cachedThreat))
        {
            return cachedThreat;
        }

        /*
         * Tres important :
         * La passe tardive ne scanne jamais le monde.
         * Elle est appelee trop souvent pour ca.
         */
        if (latePass)
        {
            return null;
        }

        if (Game.GameTime < _nextCartelThreatScanAt)
        {
            return null;
        }

        _nextCartelThreatScanAt = Game.GameTime + CartelThreatScanIntervalMs;

        Ped scannedThreat = FindBestCartelThreat(player);

        if (Entity.Exists(scannedThreat))
        {
            CacheCartelThreat(scannedThreat);
        }

        return scannedThreat;
    }

    private Ped GetCachedCartelThreat(Ped player)
    {
        if (!Entity.Exists(player))
        {
            ClearCachedCartelThreat();
            return null;
        }

        if (_cartelCachedThreatPed == null)
        {
            return null;
        }

        if (Game.GameTime > _cartelCachedThreatUntil)
        {
            ClearCachedCartelThreat();
            return null;
        }

        if (!Entity.Exists(_cartelCachedThreatPed) ||
            _cartelCachedThreatPed.IsDead ||
            !IsValidCartelThreatCandidate(_cartelCachedThreatPed, player))
        {
            ClearCachedCartelThreat();
            return null;
        }

        /*
         * Si la menace cached est vraiment trop loin, on l'oublie.
         * Pas besoin de faire un scan complet ici.
         */
        if (_cartelCachedThreatPed.Position.DistanceTo(player.Position) > CartelThreatScanRadius + 120.0f)
        {
            ClearCachedCartelThreat();
            return null;
        }

        return _cartelCachedThreatPed;
    }

    private void CacheCartelThreat(Ped threat)
    {
        if (!Entity.Exists(threat) || threat.IsDead)
        {
            ClearCachedCartelThreat();
            return;
        }

        _cartelCachedThreatPed = threat;
        _cartelCachedThreatUntil = Game.GameTime + CartelThreatCacheLifetimeMs;
    }

    private void ClearCachedCartelThreat()
    {
        _cartelCachedThreatPed = null;
        _cartelCachedThreatUntil = 0;
        _cartelLastThreatRelationshipHandle = 0;
        _cartelLastThreatRelationshipAt = 0;
    }

    private Ped FindBestCartelThreat(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return null;
        }

        Ped bestThreat = null;
        float bestScore = float.MaxValue;

        /*
         * 1) Menaces creees par le mod.
         * Cette boucle est peu couteuse : elle parcourt notre liste interne.
         */
        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc candidateNpc = _spawnedNpcs[i];

            if (candidateNpc == null ||
                !Entity.Exists(candidateNpc.Ped) ||
                candidateNpc.Ped.IsDead)
            {
                continue;
            }

            if (IsAllyBehavior(candidateNpc.BaseBehavior) ||
                IsNeutralBehavior(candidateNpc.Behavior))
            {
                continue;
            }

            if (!IsValidCartelThreatCandidate(candidateNpc.Ped, player))
            {
                continue;
            }

            float score = ScoreCartelThreat(candidateNpc.Ped, player);

            if (score < bestScore)
            {
                bestScore = score;
                bestThreat = candidateNpc.Ped;
            }
        }

        /*
         * 2) PNJ proches du joueur.
         * Un seul GetNearbyPeds autour du joueur par scan.
         */
        Ped[] nearbyPlayerPeds = GetNearbyPedsSafe(player, CartelThreatScanRadius);

        for (int i = 0; i < nearbyPlayerPeds.Length; i++)
        {
            Ped candidate = nearbyPlayerPeds[i];

            if (!IsValidCartelThreatCandidate(candidate, player))
            {
                continue;
            }

            if (!HasCartelThreatEvidence(candidate, player))
            {
                continue;
            }

            float score = ScoreCartelThreat(candidate, player);

            if (score < bestScore)
            {
                bestScore = score;
                bestThreat = candidate;
            }
        }

        /*
         * 3) PNJ proches des gardes Cartel.
         *
         * Avant :
         * on scannait les 11 gardes a chaque appel, et parfois chaque frame.
         *
         * Maintenant :
         * on scanne seulement quelques gardes par passe.
         * Sur plusieurs passes, tous les gardes sont quand meme couverts,
         * mais on evite le pic FPS au moment de l'appel.
         */
        List<int> cartelNpcHandles = new List<int>(_cartelNpcHandles);

        if (cartelNpcHandles.Count > 0)
        {
            int scansThisPass = Math.Min(CartelMaxGuardThreatScansPerPass, cartelNpcHandles.Count);

            for (int scan = 0; scan < scansThisPass; scan++)
            {
                int handleIndex = Wrap(_cartelGuardThreatScanCursor + scan, cartelNpcHandles.Count);
                SpawnedNpc guard = FindSpawnedNpcByHandle(cartelNpcHandles[handleIndex]);

                if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
                {
                    continue;
                }

                Ped[] nearbyGuardPeds = GetNearbyPedsSafe(guard.Ped, CartelThreatScanRadius);

                for (int j = 0; j < nearbyGuardPeds.Length; j++)
                {
                    Ped candidate = nearbyGuardPeds[j];

                    if (!IsValidCartelThreatCandidate(candidate, player))
                    {
                        continue;
                    }

                    if (!HasCartelThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player))
                    {
                        continue;
                    }

                    float score = ScoreCartelThreat(candidate, player);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestThreat = candidate;
                    }
                }
            }

            _cartelGuardThreatScanCursor = Wrap(_cartelGuardThreatScanCursor + scansThisPass, cartelNpcHandles.Count);
        }

        return bestThreat;
    }

    private bool IsValidCartelThreatCandidate(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || candidate.IsDead)
        {
            return false;
        }

        if (Entity.Exists(player) && candidate.Handle == player.Handle)
        {
            return false;
        }

        if (Entity.Exists(_placementPreviewPed) && candidate.Handle == _placementPreviewPed.Handle)
        {
            return false;
        }

        /*
         * Jamais les allies du joueur.
         */
        if (IsManagedAlly(candidate))
        {
            return false;
        }

        /*
         * Jamais les gardes Cartel actifs ou en repli.
         */
        if (_cartelNpcHandles.Contains(candidate.Handle))
        {
            return false;
        }

        if (_cartelDismissingNpcRecords.ContainsKey(candidate.Handle))
        {
            return false;
        }

        int group = GetPedRelationshipGroup(candidate);

        if (group == _allyGroupHash)
        {
            return false;
        }

        return true;
    }

    private bool HasCartelThreatEvidence(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return false;
        }

        /*
         * Cas joueur :
         * ces tests sont rapides et suffisent pour la majorite des combats.
         */
        if (HasDefensiveDamageAgainstProtectedPed(candidate, player))
        {
            return true;
        }

        if (IsPedInCombatWith(candidate, player))
        {
            return true;
        }

        if (IsPedShooting(candidate) &&
            candidate.Position.DistanceTo(player.Position) <= CartelThreatEvidenceRadius &&
            HasHostileRelationshipToProtectedPed(candidate, player))
        {
            return true;
        }

        /*
         * Cas gardes Cartel :
         * on ne teste un garde que si la menace candidate est dans une distance utile.
         * Ca evite 11 series de natives couteuses pour des PNJ trop loin.
         */
        List<int> cartelNpcHandles = new List<int>(_cartelNpcHandles);

        for (int i = 0; i < cartelNpcHandles.Count; i++)
        {
            SpawnedNpc guard = FindSpawnedNpcByHandle(cartelNpcHandles[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            if (candidate.Position.DistanceTo(guard.Ped.Position) > CartelThreatEvidenceRadius)
            {
                continue;
            }

            if (HasCartelThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasCartelThreatEvidenceAgainstSpecificGuard(Ped candidate, Ped guard, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(guard))
        {
            return false;
        }

        if (candidate.Position.DistanceTo(guard.Position) > CartelThreatEvidenceRadius)
        {
            return false;
        }

        /*
         * Le candidat attaque le garde.
         */
        if (HasDefensiveDamageAgainstProtectedPed(candidate, guard))
        {
            return true;
        }

        if (IsPedInCombatWith(candidate, guard))
        {
            return true;
        }

        if (IsPedShooting(candidate) &&
            (HasHostileRelationshipToProtectedPed(candidate, guard) ||
             (Entity.Exists(player) && HasHostileRelationshipToProtectedPed(candidate, player))))
        {
            return true;
        }

        return false;
    }

    private float ScoreCartelThreat(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return float.MaxValue;
        }

        float best = candidate.Position.DistanceTo(player.Position);

        List<int> cartelNpcHandles = new List<int>(_cartelNpcHandles);

        for (int i = 0; i < cartelNpcHandles.Count; i++)
        {
            SpawnedNpc guard = FindSpawnedNpcByHandle(cartelNpcHandles[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            float distance = candidate.Position.DistanceTo(guard.Ped.Position);

            if (distance < best)
            {
                best = distance;
            }
        }

        if (IsPedShooting(candidate))
        {
            best -= 45.0f;
        }

        if (IsPedInCombatWith(candidate, player) || player.HasBeenDamagedBy(candidate))
        {
            best -= 65.0f;
        }

        return Math.Max(0.0f, best);
    }

    private void EngageCartelTeamThreat(Ped threat, Ped player, bool latePass)
    {
        if (!Entity.Exists(threat) || threat.IsDead || !Entity.Exists(player))
        {
            return;
        }

        MakeCartelAlliesHostileToThreat(threat);

        /*
         * Pendant un combat, les vehicules ne recoivent pas d'ordre de convoi classique.
         * Les conducteurs rapprochent les vehicules de la menace.
         * Les passagers tirent.
         */
        List<int> activeVehicles = new List<int>(_cartelVehicleHandles);

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            if (!latePass)
            {
                UpgradeCartelVehicle(vehicle);
                RescueCartelVehicleIfNeeded(vehicle, player, i);
            }

            CommandCartelVehicleForCombat(vehicle, threat, player);
        }

        List<int> activeNpcs = new List<int>(_cartelNpcHandles);

        for (int i = 0; i < activeNpcs.Count; i++)
        {
            SpawnedNpc guard = FindSpawnedNpcByHandle(activeNpcs[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            EngageCartelGuardThreat(guard, threat, player, latePass);

            if (!latePass)
            {
                RescueCartelGuardIfNeeded(guard, player, i);
            }
        }
    }

    private void MakeCartelAlliesHostileToThreat(Ped threat)
    {
        if (!Entity.Exists(threat))
        {
            return;
        }

        /*
         * Avant :
         * les relations pouvaient etre forcees tres souvent.
         *
         * Maintenant :
         * on ne refresh la relation contre la meme cible que toutes les quelques secondes.
         */
        if (_cartelLastThreatRelationshipHandle == threat.Handle &&
            Game.GameTime - _cartelLastThreatRelationshipAt < CartelThreatRelationshipRefreshMs)
        {
            return;
        }

        _cartelLastThreatRelationshipHandle = threat.Handle;
        _cartelLastThreatRelationshipAt = Game.GameTime;

        try
        {
            int targetGroup = GetPedRelationshipGroup(threat);

            if (targetGroup != 0 &&
                targetGroup != _allyGroupHash &&
                ShouldUseGroupHostilityForThreat(threat, targetGroup))
            {
                World.SetRelationshipBetweenGroups((Relationship)RelationshipHate, _allyGroupHash, targetGroup);
                World.SetRelationshipBetweenGroups((Relationship)RelationshipHate, targetGroup, _allyGroupHash);
            }
        }
        catch
        {
            /*
             * Meme si la relation echoue, les taches TASK_COMBAT_PED / TASK_DRIVE_BY
             * restent utilisees.
             */
        }
    }

    private void EngageCartelGuardThreat(SpawnedNpc guard, Ped threat, Ped player, bool latePass)
    {
        if (guard == null ||
            !Entity.Exists(guard.Ped) ||
            guard.Ped.IsDead ||
            !Entity.Exists(threat) ||
            threat.IsDead ||
            !Entity.Exists(player))
        {
            return;
        }

        /*
         * Si le joueur est reparti en véhicule et que le garde est à pied,
         * il remonte dans une Baller sauf s'il a une menace immédiate sur lui.
         */
        if (ShouldCartelGuardReturnToVehicleDuringCombat(guard, threat, player))
        {
            ReturnCartelGuardToVehicleIfNeeded(guard, player, true);
            return;
        }

        /*
         * Correction principale :
         * joueur à pied + garde en véhicule = le garde doit finir par descendre.
         * On fait cette vérification avant le cooldown de combat, sinon un drive-by
         * récent peut retarder inutilement la sortie du véhicule.
         */
        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle currentVehicle = guard.Ped.CurrentVehicle;

            if (!player.IsInVehicle() &&
                ShouldCartelGuardLeaveVehicleForPlayerOnFoot(guard.Ped, currentVehicle, player, true) &&
                CommandCartelGuardLeaveVehicle(guard, currentVehicle, true))
            {
                return;
            }
        }

        /*
         * Optimisation conservée :
         * on évite de relancer les ordres de combat à chaque frame.
         */
        if (!CanIssueCartelCombatOrder(guard.Ped))
        {
            return;
        }

        PrepareCartelGuardForCombat(guard, threat);

        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle vehicle = guard.Ped.CurrentVehicle;

            if (!player.IsInVehicle() &&
                ShouldCartelGuardLeaveVehicleForPlayerOnFoot(guard.Ped, vehicle, player, true) &&
                CommandCartelGuardLeaveVehicle(guard, vehicle, true))
            {
                return;
            }

            if (IsPedDriverOfVehicle(guard.Ped, vehicle))
            {
                /*
                 * Conducteur :
                 * - si le joueur est à pied, le véhicule se rapproche du joueur ;
                 * - si le joueur est en véhicule, la logique combat véhicule reste active.
                 */
                CommandCartelVehicleForCombat(vehicle, threat, player);
                return;
            }

            if (ShouldCartelPassengerExitToFight(guard.Ped, vehicle, threat, player))
            {
                CommandCartelGuardLeaveVehicle(guard, vehicle, true);
                return;
            }

            StartCartelPassengerDriveBy(guard.Ped, threat);
            return;
        }

        StartCartelOnFootCombat(guard.Ped, threat);
    }

    private void PrepareCartelGuardForCombat(SpawnedNpc guard, Ped threat)
    {
        if (guard == null || !Entity.Exists(guard.Ped) || !Entity.Exists(threat))
        {
            return;
        }

        guard.BaseBehavior = NpcBehavior.Bodyguard;
        guard.Behavior = NpcBehavior.Bodyguard;
        guard.Activated = true;
        guard.IsReturningHome = false;
        guard.LastCombatActivityAt = Game.GameTime;

        guard.Ped.IsEnemy = false;
        guard.Ped.BlockPermanentEvents = false;
        guard.Ped.AlwaysKeepTask = true;
        guard.Ped.Accuracy = 72;
        guard.Ped.ShootRate = 1000;

        try
        {
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, guard.Ped.Handle, _allyGroupHash);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, guard.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, guard.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, guard.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, guard.Ped.Handle, 0, false);

            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, guard.Ped.Handle, 0, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, guard.Ped.Handle, 5, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, guard.Ped.Handle, 46, true);

            /*
             * Important pour eviter le cas "vise mais ne tire pas".
             */
            Function.Call(Hash.SET_PED_FIRING_PATTERN, guard.Ped.Handle, CartelFullAutoFiringPattern);
        }
        catch
        {
        }

        MakeCartelAlliesHostileToThreat(threat);
    }

    private bool CanIssueCartelCombatOrder(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt;

        if (_cartelNextCombatOrderAt.TryGetValue(ped.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        _cartelNextCombatOrderAt[ped.Handle] = Game.GameTime + CartelCombatOrderIntervalMs;
        return true;
    }

    private bool ShouldCartelPassengerExitToFight(Ped passenger, Vehicle vehicle, Ped threat, Ped player)
    {
        if (!Entity.Exists(passenger) ||
            !Entity.Exists(vehicle) ||
            !Entity.Exists(threat) ||
            !Entity.Exists(player))
        {
            return false;
        }

        /*
         * Ancien comportement :
         * le passager ne sortait que si la menace était très proche.
         *
         * Nouveau comportement :
         * si le joueur est à pied, la priorité est de transformer le convoi
         * en protection à pied dès que le véhicule est arrivé ou bloqué.
         */
        if (!player.IsInVehicle())
        {
            return ShouldCartelGuardLeaveVehicleForPlayerOnFoot(passenger, vehicle, player, true);
        }

        return false;
    }

    private void StartCartelPassengerDriveBy(Ped passenger, Ped threat)
    {
        if (!Entity.Exists(passenger) || !Entity.Exists(threat) || threat.IsDead)
        {
            return;
        }

        bool offensiveOrderIssued = false;
        try
        {
            passenger.Weapons.Select(WeaponHash.MachinePistol, true);
        }
        catch
        {
            try
            {
                passenger.Weapons.Give(WeaponHash.MachinePistol, 9999, true, true);
                passenger.Weapons.Select(WeaponHash.MachinePistol, true);
            }
            catch
            {
            }
        }

        try
        {
            /*
             * Tache essentielle :
             * un passager arme ne tire pas forcement avec TASK_COMBAT_PED.
             * TASK_DRIVE_BY force le vrai tir depuis le vehicule.
             */
            Function.Call(
                Hash.TASK_DRIVE_BY,
                passenger.Handle,
                threat.Handle,
                0,
                0.0f,
                0.0f,
                0.0f,
                CartelDriveByDistance,
                90,
                true,
                CartelFullAutoFiringPattern);
            offensiveOrderIssued = true;
        }
        catch
        {
            try
            {
                Function.Call(Hash.TASK_COMBAT_PED, passenger.Handle, threat.Handle, 0, 16);
                offensiveOrderIssued = true;
            }
            catch
            {
            }
        }

        if (offensiveOrderIssued)
        {
            RecordJusticeAllyPoliceEngagement(passenger, threat, true);
        }
    }

    private void StartCartelOnFootCombat(Ped guard, Ped threat)
    {
        if (!Entity.Exists(guard) || !Entity.Exists(threat) || threat.IsDead)
        {
            return;
        }

        bool offensiveOrderIssued = false;
        try
        {
            guard.Weapons.Select(WeaponHash.ServiceCarbine, true);
        }
        catch
        {
            try
            {
                guard.Weapons.Give(WeaponHash.ServiceCarbine, 9999, true, true);
                guard.Weapons.Select(WeaponHash.ServiceCarbine, true);
            }
            catch
            {
            }
        }

        try
        {
            /*
             * TASK_COMBAT_PED donne le comportement general de combat.
             */
            Function.Call(Hash.TASK_COMBAT_PED, guard.Handle, threat.Handle, 0, 16);
            offensiveOrderIssued = true;

            /*
             * TASK_SHOOT_AT_ENTITY force le declenchement du tir si le garde voit la cible.
             * C'est la correction du symptome "il vise mais ne tire pas".
             */
            if (guard.Position.DistanceTo(threat.Position) <= CartelOnFootShootDistance &&
                CanPedSeeEntity(guard, threat, CartelOnFootShootDistance))
            {
                Function.Call(
                    Hash.TASK_SHOOT_AT_ENTITY,
                    guard.Handle,
                    threat.Handle,
                    1800,
                    CartelFullAutoFiringPattern);
            }
        }
        catch
        {
        }

        if (offensiveOrderIssued)
        {
            RecordJusticeAllyPoliceEngagement(guard, threat, true);
        }
    }

    private void CommandCartelVehicleForCombat(Vehicle vehicle, Ped threat, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(threat))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return;
        }

        ConfigureCartelDriver(driver);

        int handle = vehicle.Handle;
        int nextOrder;

        if (_cartelNextVehicleOrderAt.TryGetValue(handle, out nextOrder) &&
            Game.GameTime < nextOrder)
        {
            return;
        }

        _cartelNextVehicleOrderAt[handle] = Game.GameTime + CartelVehicleOrderIntervalMs;

        bool playerOnFoot = Entity.Exists(player) && !player.IsInVehicle();

        /*
         * Correction comportement :
         * avant, en combat, le conducteur allait vers la menace.
         * Si le joueur était à pied, ça maintenait tout le monde en drive-by
         * et le convoi pouvait partir combattre au lieu de se déployer autour du joueur.
         *
         * Maintenant :
         * - joueur à pied : le véhicule va d'abord vers le joueur pour permettre la descente ;
         * - joueur en véhicule : comportement combat véhicule conservé.
         */
        Vector3 driveTarget = playerOnFoot && Entity.Exists(player)
            ? player.Position
            : threat.Position;

        float distanceToTarget = vehicle.Position.DistanceTo(driveTarget);
        float combatDriveSpeed = ClampFloat(CartelArrivalDriveSpeed * 0.75f, 20.0f, 34.0f);

        float stoppingRange = playerOnFoot
            ? 16.0f
            : (distanceToTarget > 45.0f ? 18.0f : 28.0f);

        bool offensiveOrderIssued = false;
        try
        {
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                vehicle.Handle,
                driveTarget.X,
                driveTarget.Y,
                driveTarget.Z,
                combatDriveSpeed,
                CartelRapidDrivingStyle,
                stoppingRange);
            offensiveOrderIssued = true;
        }
        catch
        {
            try
            {
                if (Entity.Exists(player))
                {
                    Function.Call(
                        Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                        driver.Handle,
                        vehicle.Handle,
                        player.Position.X,
                        player.Position.Y,
                        player.Position.Z,
                        combatDriveSpeed,
                        CartelRapidDrivingStyle,
                        22.0f);
                }
            }
            catch
            {
            }
        }

        if (offensiveOrderIssued && !playerOnFoot)
        {
            RecordJusticeAllyPoliceEngagement(driver, threat, true);
        }
    }

    private void IssueCartelFastFollowOrder(Vehicle vehicle, Ped player, bool force)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return;
        }

        int handle = vehicle.Handle;

        /*
         * Si le véhicule est déjà proche et quasiment arrêté, on ne renvoie pas
         * sans arrêt des ordres de conduite. Sinon GTA relance la tâche régulièrement
         * et ça peut produire un micro-mouvement visuel, surtout à l'arrêt.
         */
        if (!force && IsCartelVehicleSettledNearPlayer(vehicle, player))
        {
            return;
        }

        int nextOrder;
        if (!force &&
            _cartelNextVehicleOrderAt.TryGetValue(handle, out nextOrder) &&
            Game.GameTime < nextOrder)
        {
            return;
        }

        _cartelNextVehicleOrderAt[handle] = Game.GameTime + CartelVehicleOrderIntervalMs;

        ConfigureCartelDriver(driver);

        float cruiseSpeed = CalculateCartelCruiseSpeed(player);
        float distance = vehicle.Position.DistanceTo(player.Position);
        Vector3 targetPosition = player.Position;

        if (player.IsInVehicle() && Entity.Exists(player.CurrentVehicle))
        {
            targetPosition = player.CurrentVehicle.Position;
        }

        /*
         * Si la dernière cible d'ordre est presque identique et que le véhicule
         * roule déjà correctement, on ne spam pas l'IA.
         */
        Vector3 lastTarget;

        if (!force &&
            _cartelLastVehicleOrderTarget.TryGetValue(handle, out lastTarget) &&
            lastTarget.DistanceTo(targetPosition) < 8.0f &&
            vehicle.Speed > 3.0f &&
            distance < 95.0f)
        {
            return;
        }

        _cartelLastVehicleOrderTarget[handle] = targetPosition;

        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, cruiseSpeed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, CartelRapidDrivingStyle);
        }
        catch
        {
        }

        try
        {
            /*
             * IMPORTANT :
             * Aucune vitesse forcée ici.
             * Aucune remise au sol ici.
             * Le véhicule est contrôlé uniquement par le conducteur PNJ.
             */
            if (player.IsInVehicle() && Entity.Exists(player.CurrentVehicle))
            {
                Function.Call(
                    Hash.TASK_VEHICLE_ESCORT,
                    driver.Handle,
                    vehicle.Handle,
                    player.CurrentVehicle.Handle,
                    -1,
                    cruiseSpeed,
                    CartelRapidDrivingStyle,
                    9.0f,
                    0,
                    20.0f);
            }
            else
            {
                float stoppingRange = distance > 55.0f ? 14.0f : 22.0f;

                Function.Call(
                    Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    driver.Handle,
                    vehicle.Handle,
                    player.Position.X,
                    player.Position.Y,
                    player.Position.Z,
                    cruiseSpeed,
                    CartelRapidDrivingStyle,
                    stoppingRange);
            }
        }
        catch
        {
            // L'IA peut refuser temporairement une tâche si le conducteur change d'état.
        }
    }

    private bool IsCartelVehicleSettledNearPlayer(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        float distance = vehicle.Position.DistanceTo(player.Position);

        /*
         * Joueur à pied :
         * Si le véhicule est déjà arrivé autour du joueur et qu'il est presque arrêté,
         * on ne renvoie plus d'ordre. Ça évite les micro-redémarrages / pulsations.
         */
        if (!player.IsInVehicle())
        {
            return distance <= 24.0f && vehicle.Speed <= 1.8f;
        }

        if (!Entity.Exists(player.CurrentVehicle))
        {
            return distance <= 24.0f && vehicle.Speed <= 1.8f;
        }

        /*
         * Joueur en véhicule mais à l'arrêt ou lent :
         * Si l'escorte est proche, on ne relance pas l'ordre à chaque tick.
         */
        float playerSpeed = player.CurrentVehicle.Speed;

        if (playerSpeed <= 3.0f)
        {
            return distance <= 18.0f && vehicle.Speed <= 2.2f;
        }

        return false;
    }

    private float CalculateCartelCruiseSpeed(Ped player)
    {
        /*
         * Vitesse naturelle :
         * - assez rapide pour donner l'impression de renforts efficaces ;
         * - pas assez haute pour transformer la voiture en projectile ;
         * - adaptée à la vitesse du joueur si le joueur conduit.
         */
        if (Entity.Exists(player) && player.IsInVehicle() && Entity.Exists(player.CurrentVehicle))
        {
            float playerSpeed = player.CurrentVehicle.Speed;

            return ClampFloat(playerSpeed * 1.15f + 8.0f, 22.0f, 44.0f);
        }

        return CartelArrivalDriveSpeed;
    }

    private void RescueCartelVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        int handle = vehicle.Handle;

        bool tooFar = vehicle.Position.DistanceTo(player.Position) > CartelTooFarVehicleDistance;
        bool criticallyFar = vehicle.Position.DistanceTo(player.Position) > CartelCriticalVehicleDistance;
        bool stuck = IsCartelVehicleStuck(vehicle, player);

        if (!tooFar && !stuck)
        {
            return;
        }

        int lastRescue;

        if (_cartelLastVehicleRescueAt.TryGetValue(handle, out lastRescue) &&
            Game.GameTime - lastRescue < CartelRescueCooldownMs)
        {
            return;
        }

        bool visible = IsEntityLikelyVisibleToPlayer(vehicle);

        if (visible && !criticallyFar)
        {
            return;
        }

        Vector3 point;

        if (!TryFindHiddenRoadPointNearPlayer(
            player,
            seedIndex,
            CartelRelocationMinDistance,
            CartelRelocationMaxDistance,
            out point))
        {
            return;
        }

        TeleportCartelVehicleToRoad(vehicle, player, point);
        _cartelLastVehicleRescueAt[handle] = Game.GameTime;
        InitializeCartelVehicleTracking(vehicle);

        IssueCartelFastFollowOrder(vehicle, player, true);
    }

    private bool IsCartelVehicleStuck(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        int handle = vehicle.Handle;
        Vector3 lastPosition;
        int lastMoveAt;

        if (!_cartelLastVehiclePositions.TryGetValue(handle, out lastPosition) ||
            !_cartelLastVehicleMoveAt.TryGetValue(handle, out lastMoveAt))
        {
            InitializeCartelVehicleTracking(vehicle);
            return false;
        }

        float moved = vehicle.Position.DistanceTo(lastPosition);

        if (moved > 4.0f || vehicle.Speed > 2.0f)
        {
            _cartelLastVehiclePositions[handle] = vehicle.Position;
            _cartelLastVehicleMoveAt[handle] = Game.GameTime;
            return false;
        }

        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

        return distanceToPlayer > 55.0f &&
               Game.GameTime - lastMoveAt >= CartelStuckTimeoutMs;
    }

    private void InitializeCartelVehicleTracking(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        _cartelLastVehiclePositions[vehicle.Handle] = vehicle.Position;
        _cartelLastVehicleMoveAt[vehicle.Handle] = Game.GameTime;
        _cartelNextVehicleOrderAt[vehicle.Handle] = 0;
    }

    private void ClearCartelVehicleTracking(int handle)
    {
        _cartelLastVehiclePositions.Remove(handle);
        _cartelLastVehicleMoveAt.Remove(handle);
        _cartelLastVehicleRescueAt.Remove(handle);
        _cartelNextVehicleOrderAt.Remove(handle);

        _cartelFullyUpgradedVehicleHandles.Remove(handle);
        _cartelLastVehicleSoftMaintenanceAt.Remove(handle);
        _cartelLastVehicleOrderTarget.Remove(handle);

        /*
         * Securite :
         * normalement _cartelNextCombatOrderAt est indexe par handle de ped,
         * mais on nettoie aussi ce handle pour eviter toute reference morte.
         */
        _cartelNextCombatOrderAt.Remove(handle);
    }

    private void TeleportCartelVehicleToRoad(Vehicle vehicle, Ped player, Vector3 point)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        float heading = HeadingFromTo(point, player.Position);
        Ped driver = GetDriverOfVehicle(vehicle);

        try
        {
            /*
             * La TP de secours reste autorisée uniquement quand le véhicule est bloqué
             * ou trop loin. On coupe les tâches avant de replacer le véhicule pour éviter
             * une ancienne tâche incohérente après la TP.
             */
            if (Entity.Exists(driver))
            {
                Function.Call(Hash.CLEAR_PED_TASKS, driver.Handle);
            }

            vehicle.Position = point;
            vehicle.Heading = heading;

            /*
             * Autorisé ici, car c'est une vraie téléportation de secours.
             * Cette méthode ne doit surtout pas être appelée en boucle.
             */
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);

            /*
             * On annule l'inertie résiduelle après TP.
             * Ce n'est pas une propulsion, c'est une remise à zéro de sécurité.
             */
            Function.Call(Hash.SET_ENTITY_VELOCITY, vehicle.Handle, 0.0f, 0.0f, 0.0f);
        }
        catch
        {
        }

        if (Entity.Exists(driver))
        {
            ConfigureCartelDriver(driver);
        }

        /*
         * Après une TP, on force un nouvel ordre de conduite propre,
         * puis on remet à jour les trackers anti-blocage.
         */
        InitializeCartelVehicleTracking(vehicle);
        _cartelLastVehicleOrderTarget.Remove(vehicle.Handle);
    }

    private void RescueCartelGuardIfNeeded(SpawnedNpc npc, Ped player, int seedIndex)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(player))
        {
            return;
        }

        if (npc.Ped.IsInVehicle())
        {
            return;
        }

        float distance = npc.Ped.Position.DistanceTo(player.Position);

        if (distance < CartelTooFarGuardDistance)
        {
            return;
        }

        if (IsEntityLikelyVisibleToPlayer(npc.Ped))
        {
            return;
        }

        int lastRescue;

        if (_cartelLastGuardRescueAt.TryGetValue(npc.Ped.Handle, out lastRescue) &&
            Game.GameTime - lastRescue < CartelGuardRescueCooldownMs)
        {
            return;
        }

        Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);

        if (Entity.Exists(assignedVehicle) &&
            assignedVehicle.Position.DistanceTo(player.Position) < CartelTooFarVehicleDistance)
        {
            int freeSeat = FindAnyFreeSeatForCartel(assignedVehicle);

            if (freeSeat != 999)
            {
                PutPedIntoVehicleSafe(npc.Ped, assignedVehicle, freeSeat);
                _cartelLastGuardRescueAt[npc.Ped.Handle] = Game.GameTime;
                return;
            }
        }

        Vector3 point;

        if (TryFindHiddenRoadPointNearPlayer(
            player,
            seedIndex + 6,
            CartelRelocationMinDistance * 0.75f,
            CartelRelocationMaxDistance,
            out point))
        {
            try
            {
                npc.Ped.Position = point;
                Function.Call((Hash)PlaceEntityOnGroundProperlyNative, npc.Ped.Handle);
                _cartelLastGuardRescueAt[npc.Ped.Handle] = Game.GameTime;
            }
            catch
            {
            }
        }
    }

    private bool IsPedDriverOfAnyCartelVehicle(Ped ped)
    {
        if (!Entity.Exists(ped) || !ped.IsInVehicle() || !Entity.Exists(ped.CurrentVehicle))
        {
            return false;
        }

        Vehicle currentVehicle = ped.CurrentVehicle;

        /*
         * Ancien comportement : boucle sur les handles véhicules Cartel puis
         * FindVehicleByHandle(), qui appelle World.GetAllVehicles(). Avec 11
         * gardes et 3 véhicules, cette vérification pouvait déclencher des
         * dizaines de scans de véhicules à cadence fixe.
         *
         * Ici, on vérifie directement le véhicule courant du ped : même résultat
         * logique, sans scan mondial.
         */
        if (!_cartelVehicleHandles.Contains(currentVehicle.Handle) &&
            !_cartelDismissingVehicleHandles.Contains(currentVehicle.Handle))
        {
            return false;
        }

        return IsPedDriverOfVehicle(ped, currentVehicle);
    }

    private void TrySelectPedWeapon(Ped ped, WeaponHash weapon)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        try
        {
            ped.Weapons.Select(weapon, true);
        }
        catch
        {
        }
    }

    private void DismissCartelTeam(bool announce)
    {
        CleanupCartelHandleSets();

        if (_cartelNpcHandles.Count == 0 && _cartelVehicleHandles.Count == 0)
        {
            if (announce)
            {
                ShowStatus("Cartel : aucune équipe active à rappeler.", 3000);
            }

            return;
        }

        _cartelDismissStartedAt = Game.GameTime;
        _cartelDismissCleanupAt = Game.GameTime + CartelDismissForceCleanupMs;
        _nextCartelDismissOrderAt = 0;

        List<int> activeNpcHandles = new List<int>(_cartelNpcHandles);
        List<int> activeVehicleHandles = new List<int>(_cartelVehicleHandles);

        _cartelNpcHandles.Clear();
        _cartelVehicleHandles.Clear();
        _cartelConvoyActive = false;

        for (int i = 0; i < activeNpcHandles.Count; i++)
        {
            SpawnedNpc npc = FindSpawnedNpcByHandle(activeNpcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped))
            {
                continue;
            }

            /*
             * On détache les hommes en repli de la liste IA standard.
             * Sinon UpdateBodyguard() leur redonnerait l'ordre de suivre le joueur.
             */
            _spawnedNpcs.Remove(npc);
            _cartelDismissingNpcRecords[npc.Ped.Handle] = npc;

            npc.Activated = false;
            npc.IsReturningHome = false;
            npc.Behavior = NpcBehavior.Ally;
            npc.BaseBehavior = NpcBehavior.Ally;

            npc.Ped.IsEnemy = false;
            npc.Ped.BlockPermanentEvents = false;
            npc.Ped.AlwaysKeepTask = true;

            try
            {
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
                Function.Call(Hash.CLEAR_PED_TASKS, npc.Ped.Handle);
            }
            catch
            {
            }

            TrySendCartelPedBackToVehicle(npc);
        }

        for (int i = 0; i < activeVehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicleHandles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            _cartelDismissingVehicleHandles.Add(vehicle.Handle);
            UpgradeCartelVehicle(vehicle);
        }

        _cartelConvoyDismissing = _cartelDismissingNpcRecords.Count > 0 || _cartelDismissingVehicleHandles.Count > 0;

        Ped player = Game.Player.Character;

        if (Entity.Exists(player))
        {
            UpdateCartelDismissal(player, true);
        }

        if (announce)
        {
            ShowStatus("Cartel rappelé : les hommes restent alliés et quittent rapidement le secteur.", 5500);
        }
    }

    private void TrySendCartelPedBackToVehicle(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped))
        {
            return;
        }

        if (npc.Ped.IsInVehicle())
        {
            return;
        }

        Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);

        if (!Entity.Exists(assignedVehicle))
        {
            assignedVehicle = FindNearestCartelDismissingVehicle(npc.Ped.Position);
        }

        if (!Entity.Exists(assignedVehicle))
        {
            try
            {
                Function.Call(Hash.TASK_WANDER_STANDARD, npc.Ped.Handle, 10.0f, 10);
            }
            catch
            {
            }

            return;
        }

        int seat = npc.BodyguardAssignedSeat;

        if (seat == 999 || !IsSeatFreeSafe(assignedVehicle, seat))
        {
            seat = FindAnyFreeSeatForCartel(assignedVehicle);
        }

        if (seat == 999)
        {
            try
            {
                Function.Call(Hash.TASK_GO_TO_ENTITY, npc.Ped.Handle, assignedVehicle.Handle, -1, 6.0f, 1.8f, 1073741824, 0);
            }
            catch
            {
            }

            return;
        }

        npc.BodyguardAssignedVehicleHandle = assignedVehicle.Handle;
        npc.BodyguardAssignedSeat = seat;
        npc.BodyguardIsDriver = seat == -1;

        try
        {
            Function.Call(
                Hash.TASK_ENTER_VEHICLE,
                npc.Ped.Handle,
                assignedVehicle.Handle,
                5000,
                seat,
                2.0f,
                1,
                0);
        }
        catch
        {
        }
    }

    private Vehicle FindNearestCartelDismissingVehicle(Vector3 position)
    {
        Vehicle best = null;
        float bestDistance = float.MaxValue;

        foreach (int handle in _cartelDismissingVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            float distance = vehicle.Position.DistanceTo(position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = vehicle;
            }
        }

        return best;
    }

    private int FindAnyFreeSeatForCartel(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        if (IsSeatFreeSafe(vehicle, -1))
        {
            return -1;
        }

        int passengers = 3;

        try
        {
            passengers = Function.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle.Handle);
        }
        catch
        {
            passengers = 3;
        }

        for (int seat = 0; seat < passengers; seat++)
        {
            if (IsSeatFreeSafe(vehicle, seat))
            {
                return seat;
            }
        }

        return 999;
    }

    private void UpdateCartelDismissal(Ped player, bool latePass)
    {
        if (!_cartelConvoyDismissing)
        {
            return;
        }

        /*
         * Même en late pass, on ne spam pas les ordres véhicule.
         * Les suppressions restent évaluées, mais les ordres de conduite sont espacés.
         */
        bool canIssueDriveOrders = Game.GameTime >= _nextCartelDismissOrderAt;

        if (canIssueDriveOrders)
        {
            _nextCartelDismissOrderAt = Game.GameTime + CartelDismissOrderIntervalMs;
        }

        Vector3 referencePosition = Entity.Exists(player) ? player.Position : Vector3.Zero;

        List<int> npcHandlesToDelete = new List<int>();
        List<int> vehicleHandlesToDelete = new List<int>();

        foreach (KeyValuePair<int, SpawnedNpc> pair in _cartelDismissingNpcRecords)
        {
            SpawnedNpc npc = pair.Value;

            if (npc == null || !Entity.Exists(npc.Ped))
            {
                npcHandlesToDelete.Add(pair.Key);
                continue;
            }

            try
            {
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
                npc.Ped.IsEnemy = false;
            }
            catch
            {
            }

            if (canIssueDriveOrders)
            {
                TrySendCartelPedBackToVehicle(npc);
            }

            bool vehicleVisible = Entity.Exists(npc.Ped.CurrentVehicle) && IsEntityLikelyVisibleToPlayer(npc.Ped.CurrentVehicle);
            bool pedVisible = IsEntityLikelyVisibleToPlayer(npc.Ped);
            bool oldEnough = Game.GameTime - _cartelDismissStartedAt >= CartelDismissMinLifeMs;
            bool forceCleanup = Game.GameTime >= _cartelDismissCleanupAt;
            bool farEnough = npc.Ped.Position.DistanceTo(referencePosition) >= CartelDismissDeleteDistance;

            if (forceCleanup || (oldEnough && farEnough && !pedVisible && !vehicleVisible))
            {
                DeleteEntitySafe(npc.Ped);
                npcHandlesToDelete.Add(pair.Key);
            }
        }

        foreach (int handle in _cartelDismissingVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (!Entity.Exists(vehicle))
            {
                vehicleHandlesToDelete.Add(handle);
                continue;
            }

            if (canIssueDriveOrders)
            {
                Ped driver = GetDriverOfVehicle(vehicle);

                if (Entity.Exists(driver))
                {
                    ConfigureCartelDriver(driver);

                    Vector3 retreatPoint = CalculateCartelRetreatPoint(referencePosition, vehicle.Position);

                    try
                    {
                        Function.Call(
                            Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                            driver.Handle,
                            vehicle.Handle,
                            retreatPoint.X,
                            retreatPoint.Y,
                            retreatPoint.Z,
                            CartelRetreatDriveSpeed,
                            CartelRapidDrivingStyle,
                            22.0f);
                    }
                    catch
                    {
                    }
                }
            }

            bool oldEnough = Game.GameTime - _cartelDismissStartedAt >= CartelDismissMinLifeMs;
            bool forceCleanup = Game.GameTime >= _cartelDismissCleanupAt;
            bool visible = IsEntityLikelyVisibleToPlayer(vehicle);
            bool farEnough = vehicle.Position.DistanceTo(referencePosition) >= CartelDismissDeleteDistance;

            if (forceCleanup || (oldEnough && farEnough && !visible))
            {
                DeleteDismissedVehicleAndOccupants(vehicle);
                vehicleHandlesToDelete.Add(handle);
            }
        }

        for (int i = 0; i < npcHandlesToDelete.Count; i++)
        {
            _cartelDismissingNpcRecords.Remove(npcHandlesToDelete[i]);
            _cartelNextCombatOrderAt.Remove(npcHandlesToDelete[i]);
            _cartelLastGuardRescueAt.Remove(npcHandlesToDelete[i]);
            _cartelNextGuardPassiveMaintenanceAt.Remove(npcHandlesToDelete[i]);
            _cartelNextGuardMobilityOrderAt.Remove(npcHandlesToDelete[i]);
        }

        for (int i = 0; i < vehicleHandlesToDelete.Count; i++)
        {
            _cartelDismissingVehicleHandles.Remove(vehicleHandlesToDelete[i]);
            ClearCartelVehicleTracking(vehicleHandlesToDelete[i]);
        }

        _cartelConvoyDismissing = _cartelDismissingNpcRecords.Count > 0 || _cartelDismissingVehicleHandles.Count > 0;

        if (!_cartelConvoyDismissing)
        {
            ShowStatus("Cartel : l'ancienne équipe a quitté le secteur.", 3000);
        }
    }

    private void DeleteDismissedVehicleAndOccupants(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        List<int> occupantsToDelete = new List<int>();

        foreach (KeyValuePair<int, SpawnedNpc> pair in _cartelDismissingNpcRecords)
        {
            SpawnedNpc npc = pair.Value;

            if (npc == null || !Entity.Exists(npc.Ped))
            {
                occupantsToDelete.Add(pair.Key);
                continue;
            }

            if (Entity.Exists(npc.Ped.CurrentVehicle) && npc.Ped.CurrentVehicle.Handle == vehicle.Handle)
            {
                DeleteEntitySafe(npc.Ped);
                occupantsToDelete.Add(pair.Key);
            }
        }

        for (int i = 0; i < occupantsToDelete.Count; i++)
        {
            _cartelDismissingNpcRecords.Remove(occupantsToDelete[i]);
            _cartelNextCombatOrderAt.Remove(occupantsToDelete[i]);
            _cartelLastGuardRescueAt.Remove(occupantsToDelete[i]);
            _cartelNextGuardPassiveMaintenanceAt.Remove(occupantsToDelete[i]);
            _cartelNextGuardMobilityOrderAt.Remove(occupantsToDelete[i]);
        }

        PlacedVehicle placed = FindPlacedVehicleByHandle(vehicle.Handle);

        if (placed != null)
        {
            RemovePlacedVehicleBlip(placed);
        }

        DeleteEntitySafe(vehicle);
    }

    private Vector3 CalculateCartelRetreatPoint(Vector3 playerPosition, Vector3 vehiclePosition)
    {
        Vector3 away = Normalize(vehiclePosition - playerPosition);

        if (away.Length() < 0.001f)
        {
            away = new Vector3(0.0f, -1.0f, 0.0f);
        }

        Vector3 point = vehiclePosition + away * 420.0f;

        Vector3 node;

        if (TryGetClosestVehicleNode(point, 0, out node))
        {
            return node + new Vector3(0.0f, 0.0f, 0.45f);
        }

        float ground = World.GetGroundHeight(new Vector3(point.X, point.Y, point.Z + 1000.0f));

        if (Math.Abs(ground) > 0.001f)
        {
            point.Z = ground + 0.5f;
        }

        return point;
    }

    private SpawnedNpc FindSpawnedNpcByHandle(int handle)
    {
        if (handle == 0)
        {
            return null;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc != null && Entity.Exists(npc.Ped) && npc.Ped.Handle == handle)
            {
                return npc;
            }
        }

        SpawnedNpc dismissingNpc;

        if (_cartelDismissingNpcRecords.TryGetValue(handle, out dismissingNpc))
        {
            return dismissingNpc;
        }

        return null;
    }

    private Ped FindNpcPedByHandle(int handle)
    {
        SpawnedNpc npc = FindSpawnedNpcByHandle(handle);
        return npc == null ? null : npc.Ped;
    }

    private PlacedVehicle FindPlacedVehicleByHandle(int handle)
    {
        if (handle == 0)
        {
            return null;
        }

        for (int i = 0; i < _placedVehicles.Count; i++)
        {
            PlacedVehicle placed = _placedVehicles[i];

            if (placed != null && Entity.Exists(placed.Vehicle) && placed.Vehicle.Handle == handle)
            {
                return placed;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Contact téléphone : vague ennemie Ballas
    // ---------------------------------------------------------------------
    /*
     * Touche R avec le téléphone ouvert :
     * - crée une vague ennemie indépendante du Cartel allié ;
     * - 4 à 12 ennemis aléatoires ;
     * - Ballas armés au SMG ;
     * - arrivée en véhicules depuis une route cachée hors champ ;
     * - conduite vers le joueur, tirs depuis les passagers, puis descente et combat à pied ;
     * - 100 vie + 100 armure exactement comme demandé.
     *
     * La vague utilise le groupe hostile déjà existant du mod. Les bodyguards Cartel
     * et les alliés du joueur la considéreront donc naturellement comme une menace.
     */

    private const string EnemyRaidContactName = "Ballas";

    private const int EnemyRaidMinMembers = 4;
    private const int EnemyRaidMaxMembers = 12;
    private const int EnemyRaidMaxActiveMembers = 36;
    private const int EnemyRaidMaxVehicleCount = 4;

    private const int EnemyRaidHealth = 100;
    private const int EnemyRaidArmor = 100;

    private const int EnemyRaidCallCooldownMs = 2500;
    private const int EnemyRaidThinkIntervalMs = 450;
    private const int EnemyRaidPedOrderIntervalMs = 850;
    private const int EnemyRaidVehicleOrderIntervalMs = 1300;

    private const int EnemyRaidStuckTimeoutMs = 7000;
    private const int EnemyRaidVehicleRescueCooldownMs = 10000;

    /*
     * Nettoyage post-combat :
     * - les blips véhicules rouges disparaissent dès que tous les Ballas sont morts ;
     * - les véhicules restent physiquement si le joueur les regarde encore ;
     * - ils sont supprimés proprement quand le joueur part / ne les voit plus.
     */
    private const int EnemyRaidPostCombatVehicleCleanupGraceMs = 1800;
    private const int EnemyRaidVisibleVehicleCleanupMaxMs = 45000;

    private const float EnemyRaidSpawnMinDistance = 72.0f;
    private const float EnemyRaidSpawnMaxDistance = 130.0f;
    private const float EnemyRaidRelocationMinDistance = 82.0f;
    private const float EnemyRaidRelocationMaxDistance = 135.0f;

    private const float EnemyRaidArrivalDriveSpeed = 36.0f;
    private const float EnemyRaidDriveByDistance = 105.0f;
    private const float EnemyRaidExitVehicleDistance = 42.0f;
    private const float EnemyRaidForcedExitVehicleDistance = 18.0f;
    private const float EnemyRaidOnFootShootDistance = 125.0f;
    private const float EnemyRaidTooFarVehicleDistance = 230.0f;

    private const float EnemyRaidPostCombatVehicleCleanupDistance = 135.0f;
    private const float EnemyRaidPostCombatVehicleForceCleanupDistance = 260.0f;

    private const int EnemyRaidDrivingStyle = ProfessionalDrivingStyle;
    private const int EnemyRaidFullAutoFiringPattern = unchecked((int)0xC6EE6B4C);

    private bool _enemyRaidActive;
    private int _nextEnemyRaidCallAllowedAt;
    private int _nextEnemyRaidThinkAt;

    /*
     * _enemyRaidNpcHandles = ennemis actuellement vivants/actifs.
     * _enemyRaidKnownNpcHandles = tous les peds Ballas créés par la vague,
     * utile pour les blips et pour nettoyer les records morts.
     */
    private readonly HashSet<int> _enemyRaidNpcHandles = new HashSet<int>();
    private readonly HashSet<int> _enemyRaidKnownNpcHandles = new HashSet<int>();

    /*
     * _enemyRaidVehicleHandles = véhicules de vague encore actifs.
     * _enemyRaidVehicleCleanupHandles = véhicules abandonnés après combat,
     * sans blip, à supprimer quand le joueur s'éloigne.
     */
    private readonly HashSet<int> _enemyRaidVehicleHandles = new HashSet<int>();
    private readonly HashSet<int> _enemyRaidVehicleCleanupHandles = new HashSet<int>();

    private readonly Dictionary<int, int> _enemyRaidNextPedOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _enemyRaidNextVehicleOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector3> _enemyRaidLastVehiclePositions = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> _enemyRaidLastVehicleMoveAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _enemyRaidLastVehicleRescueAt = new Dictionary<int, int>();

    private readonly Dictionary<int, int> _enemyRaidVehicleCleanupStartedAt = new Dictionary<int, int>();
    private readonly Dictionary<int, bool> _enemyRaidNpcWasInVehicle = new Dictionary<int, bool>();

    private static readonly string[] EnemyRaidPedModelNames =
    {
        "g_m_y_ballaeast_01",
        "g_m_y_ballaorig_01",
        "g_m_y_ballasout_01"
    };

    private static readonly string[] EnemyRaidVehicleModelNames =
    {
        "buccaneer",
        "chino",
        "faction",
        "moonbeam",
        "primo",
        "manana"
    };

    private void CallEnemyRaid()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Ballas : appel impossible pendant la mort/transition du joueur.", 3000);
            return;
        }

        UpdateEnemyRaidAbandonedVehicles(player);
        CleanupEnemyRaidHandleSets(true);

        if (Game.GameTime < _nextEnemyRaidCallAllowedAt)
        {
            int remaining = Math.Max(1, (_nextEnemyRaidCallAllowedAt - Game.GameTime + 999) / 1000);
            ShowStatus("Ballas indisponibles encore " + remaining.ToString(CultureInfo.InvariantCulture) + " seconde(s).", 3000);
            return;
        }

        _nextEnemyRaidCallAllowedAt = Game.GameTime + EnemyRaidCallCooldownMs;

        int liveMembers = CountLiveEnemyRaidMembers();

        if (liveMembers >= EnemyRaidMaxActiveMembers)
        {
            ShowStatus("Ballas : limite de " + EnemyRaidMaxActiveMembers.ToString(CultureInfo.InvariantCulture) + " ennemis actifs atteinte.", 4000);
            return;
        }

        int requestedMembers = _random.Next(EnemyRaidMinMembers, EnemyRaidMaxMembers + 1);
        int allowedMembers = Math.Min(requestedMembers, EnemyRaidMaxActiveMembers - liveMembers);

        if (allowedMembers <= 0)
        {
            ShowStatus("Ballas : aucune place pour une nouvelle vague.", 3500);
            return;
        }

        SpawnEnemyRaidWave(allowedMembers, requestedMembers);
    }

    private void SpawnEnemyRaidWave(int memberCount, int originalRequestedCount)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Impossible d'appeler les Ballas : joueur invalide.", 3500);
            return;
        }

        EnsureRelationshipGroups();

        WeaponLoadout smgLoadout = CreateEnemyRaidLoadout();

        int createdMembers = 0;
        int createdVehicles = 0;
        int vehicleAttempt = 0;
        int safeMemberCount = Math.Max(1, Math.Min(memberCount, EnemyRaidMaxActiveMembers));

        while (createdMembers < safeMemberCount &&
               createdVehicles < EnemyRaidMaxVehicleCount &&
               vehicleAttempt < EnemyRaidMaxVehicleCount + 4)
        {
            Vector3 spawnPosition = FindEnemyRaidVehicleSpawnPosition(player, vehicleAttempt);
            float heading = HeadingFromTo(spawnPosition, player.Position);

            VehicleIdentity vehicleIdentity = ResolveEnemyRaidVehicleIdentity(vehicleAttempt);
            Vehicle vehicle = CreateVehicleFromIdentity(vehicleIdentity, spawnPosition, heading);

            vehicleAttempt++;

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            RegisterPlacedVehicle(vehicle, vehicleIdentity, spawnPosition, heading, false);
            ConfigureEnemyRaidVehicle(vehicle);

            _enemyRaidVehicleHandles.Add(vehicle.Handle);
            _enemyRaidVehicleCleanupHandles.Remove(vehicle.Handle);
            _enemyRaidVehicleCleanupStartedAt.Remove(vehicle.Handle);
            InitializeEnemyRaidVehicleTracking(vehicle);
            createdVehicles++;

            int seatsForThisVehicle = GetVehicleSeatCapacityIncludingDriver(vehicle);
            int seatsFilled = 0;

            for (int localSeatIndex = 0; localSeatIndex < seatsForThisVehicle && createdMembers < safeMemberCount; localSeatIndex++)
            {
                int seat = localSeatIndex == 0 ? -1 : localSeatIndex - 1;

                if (!IsSeatFreeSafe(vehicle, seat))
                {
                    continue;
                }

                Vector3 pedSpawnPosition = spawnPosition + GetSpawnOffsetAroundVehicle(localSeatIndex);
                ModelIdentity pedIdentity = ResolveEnemyRaidPedModelIdentity(createdMembers + vehicleAttempt);
                Ped enemy = CreatePedFromModelIdentity(pedIdentity, pedSpawnPosition, heading);

                if (!Entity.Exists(enemy))
                {
                    continue;
                }

                SpawnedNpc spawned = RegisterSpawnedNpc(
                    enemy,
                    NpcBehavior.Attacker,
                    true,
                    false,
                    pedIdentity,
                    smgLoadout.Clone(),
                    EnemyRaidHealth,
                    EnemyRaidArmor,
                    pedSpawnPosition,
                    heading,
                    _selectedPatrolRadius,
                    false);

                if (spawned == null || !Entity.Exists(spawned.Ped))
                {
                    DeleteEntitySafe(enemy);
                    continue;
                }

                RegisterEnemyRaidNpc(spawned, true);
                ConfigureEnemyRaidPed(spawned, vehicle, seat);
                PutPedIntoVehicleSafe(spawned.Ped, vehicle, seat);
                MarkEnemyRaidNpcVehicleState(spawned, true, true);

                createdMembers++;
                seatsFilled++;
            }

            if (seatsFilled == 0)
            {
                DeleteEnemyRaidVehicleAndRecord(vehicle.Handle, true);
                createdVehicles--;
            }
        }

        int footSeed = 40;

        while (createdMembers < safeMemberCount)
        {
            if (SpawnEnemyRaidFootEnemy(player, smgLoadout, footSeed + createdMembers))
            {
                createdMembers++;
            }
            else
            {
                break;
            }
        }

        CleanupEnemyRaidHandleSets(false);

        if (createdMembers == 0)
        {
            ShowStatus("Ballas : aucun ennemi n'a pu être créé.", 5000);
            return;
        }

        _enemyRaidActive = true;
        _nextEnemyRaidThinkAt = 0;

        OrderEnemyRaidVehiclesToPlayer(true);
        ForceRefreshAllEnemyRaidNpcBlips();

        string cappedText = safeMemberCount < originalRequestedCount
            ? " (limite active atteinte)"
            : string.Empty;

        ShowStatus(
            "Ballas appelés : " +
            createdMembers.ToString(CultureInfo.InvariantCulture) +
            " ennemi(s), " +
            createdVehicles.ToString(CultureInfo.InvariantCulture) +
            " véhicule(s), SMG, 100 vie / 100 armure" + cappedText + ".",
            6500);
    }

    private bool SpawnEnemyRaidFootEnemy(Ped player, WeaponLoadout loadout, int seedIndex)
    {
        if (!Entity.Exists(player) || loadout == null)
        {
            return false;
        }

        Vector3 spawnPosition = FindEnemyRaidVehicleSpawnPosition(player, seedIndex);
        float heading = HeadingFromTo(spawnPosition, player.Position);
        ModelIdentity pedIdentity = ResolveEnemyRaidPedModelIdentity(seedIndex);
        Ped enemy = CreatePedFromModelIdentity(pedIdentity, spawnPosition, heading);

        if (!Entity.Exists(enemy))
        {
            return false;
        }

        SpawnedNpc spawned = RegisterSpawnedNpc(
            enemy,
            NpcBehavior.Attacker,
            true,
            false,
            pedIdentity,
            loadout.Clone(),
            EnemyRaidHealth,
            EnemyRaidArmor,
            spawnPosition,
            heading,
            _selectedPatrolRadius,
            false);

        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            DeleteEntitySafe(enemy);
            return false;
        }

        RegisterEnemyRaidNpc(spawned, false);
        ConfigureEnemyRaidPed(spawned, null, 999);
        MarkEnemyRaidNpcVehicleState(spawned, false, true);
        StartEnemyRaidOnFootCombat(spawned.Ped, player, true);

        return true;
    }

    private void UpdateEnemyRaidState(Ped player)
    {
        UpdateEnemyRaidAbandonedVehicles(player);

        if (!Entity.Exists(player))
        {
            return;
        }

        if (player.IsDead)
        {
            HandleEnemyRaidPlayerDeath(player);
            return;
        }

        CleanupEnemyRaidHandleSets(true);
        UpdateEnemyRaidAbandonedVehicles(player);

        if (!_enemyRaidActive)
        {
            return;
        }

        if (Game.GameTime < _nextEnemyRaidThinkAt)
        {
            return;
        }

        _nextEnemyRaidThinkAt = Game.GameTime + EnemyRaidThinkIntervalMs;

        EnsureRelationshipGroups();

        List<int> activeVehicles = new List<int>(_enemyRaidVehicleHandles);

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            UpdateEnemyRaidVehicle(vehicle, player, i);
        }

        List<int> activeNpcs = new List<int>(_enemyRaidNpcHandles);

        for (int i = 0; i < activeNpcs.Count; i++)
        {
            SpawnedNpc npc = FindEnemyRaidNpcRecordByHandle(activeNpcs[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            UpdateEnemyRaidNpc(npc, player);
        }

        CleanupEnemyRaidHandleSets(true);
        UpdateEnemyRaidAbandonedVehicles(player);
    }

    private void UpdateEnemyRaidVehicle(Vehicle vehicle, Ped player, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        ConfigureEnemyRaidVehicleSoftState(vehicle);
        RescueEnemyRaidVehicleIfNeeded(vehicle, player, seedIndex);

        Ped driver = GetDriverOfVehicle(vehicle);
        bool driverInvalid = !Entity.Exists(driver) || driver.IsDead;
        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);
        bool vehicleStuck = IsEnemyRaidVehicleStuck(vehicle);

        if (driverInvalid ||
            vehicleStuck ||
            distanceToPlayer <= EnemyRaidForcedExitVehicleDistance ||
            (!player.IsInVehicle() && distanceToPlayer <= EnemyRaidExitVehicleDistance))
        {
            CommandEnemyRaidOccupantsLeaveVehicle(vehicle, player, vehicleStuck || driverInvalid);
            return;
        }

        IssueEnemyRaidVehicleAttackOrder(vehicle, player, false);
    }

    private void UpdateEnemyRaidNpc(SpawnedNpc npc, Ped player)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || !Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        MaintainEnemyRaidPedState(npc.Ped);
        UpdateEnemyRaidNpcBlipState(npc);

        if (npc.Ped.IsInVehicle() && Entity.Exists(npc.Ped.CurrentVehicle))
        {
            Vehicle vehicle = npc.Ped.CurrentVehicle;
            float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

            if (IsPedDriverOfVehicle(npc.Ped, vehicle))
            {
                ConfigureEnemyRaidDriver(npc.Ped);
                IssueEnemyRaidVehicleAttackOrder(vehicle, player, false);
            }
            else
            {
                StartEnemyRaidPassengerDriveBy(npc.Ped, player, false);
            }

            if (distanceToPlayer <= EnemyRaidForcedExitVehicleDistance ||
                (!player.IsInVehicle() && distanceToPlayer <= EnemyRaidExitVehicleDistance))
            {
                CommandEnemyRaidPedLeaveVehicle(npc, vehicle, true);
            }

            return;
        }

        MarkEnemyRaidNpcVehicleState(npc, false, false);
        ForceRefreshEnemyRaidNpcBlip(npc, false);
        StartEnemyRaidOnFootCombat(npc.Ped, player, false);
    }

    private void CleanupEnemyRaidHandleSets()
    {
        CleanupEnemyRaidHandleSets(true);
    }

    private void CleanupEnemyRaidHandleSets(bool allowPostCombatCleanup)
    {
        List<int> deadNpcHandles = new List<int>();

        foreach (int handle in _enemyRaidNpcHandles)
        {
            SpawnedNpc npc = FindEnemyRaidNpcRecordByHandle(handle);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                deadNpcHandles.Add(handle);
            }
        }

        for (int i = 0; i < deadNpcHandles.Count; i++)
        {
            _enemyRaidNpcHandles.Remove(deadNpcHandles[i]);
            _enemyRaidNextPedOrderAt.Remove(deadNpcHandles[i]);
            _enemyRaidNpcWasInVehicle.Remove(deadNpcHandles[i]);
        }

        List<int> inactiveVehicleHandles = new List<int>();

        foreach (int handle in _enemyRaidVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (!Entity.Exists(vehicle))
            {
                inactiveVehicleHandles.Add(handle);
            }
        }

        for (int i = 0; i < inactiveVehicleHandles.Count; i++)
        {
            _enemyRaidVehicleHandles.Remove(inactiveVehicleHandles[i]);
            ClearEnemyRaidVehicleTracking(inactiveVehicleHandles[i]);
            RemoveEnemyRaidPlacedVehicleRecord(inactiveVehicleHandles[i], false);
        }

        _enemyRaidActive = _enemyRaidNpcHandles.Count > 0;

        if (!_enemyRaidActive && allowPostCombatCleanup)
        {
            BeginEnemyRaidPostCombatCleanup();
        }
    }

    private int CountLiveEnemyRaidMembers()
    {
        CleanupEnemyRaidHandleSets(false);
        return CountLiveEnemyRaidMembersWithoutCleanup();
    }

    private bool DoesEnemyRaidVehicleHaveLiveTrackedOccupant(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        foreach (int handle in _enemyRaidNpcHandles)
        {
            SpawnedNpc npc = FindSpawnedNpcByHandle(handle);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            if (npc.Ped.IsInVehicle() &&
                Entity.Exists(npc.Ped.CurrentVehicle) &&
                npc.Ped.CurrentVehicle.Handle == vehicle.Handle)
            {
                return true;
            }
        }

        return false;
    }

    private ModelIdentity ResolveEnemyRaidPedModelIdentity(int seedIndex)
    {
        string modelName = EnemyRaidPedModelNames[Wrap(seedIndex + _random.Next(EnemyRaidPedModelNames.Length), EnemyRaidPedModelNames.Length)];

        return new ModelIdentity
        {
            IsCustom = true,
            Name = modelName,
            Hash = 0,
            DisplayName = "Ballas " + modelName
        };
    }

    private VehicleIdentity ResolveEnemyRaidVehicleIdentity(int seedIndex)
    {
        string modelName = EnemyRaidVehicleModelNames[Wrap(seedIndex + _random.Next(EnemyRaidVehicleModelNames.Length), EnemyRaidVehicleModelNames.Length)];
        int hash = 0;

        try
        {
            hash = Game.GenerateHash(modelName);
        }
        catch
        {
            hash = 0;
        }

        return new VehicleIdentity
        {
            Name = modelName,
            Hash = hash,
            DisplayName = "Ballas " + modelName
        };
    }

    private WeaponLoadout CreateEnemyRaidLoadout()
    {
        return new WeaponLoadout
        {
            Weapon = WeaponHash.SMG,
            Ammo = 9999,
            Tint = 6,
            Preset = WeaponUpgradePreset.ChargeurEtendu,
            ExtendedClip = true,
            Suppressor = false,
            Flashlight = false,
            Grip = false,
            Scope = WeaponScopeMode.None,
            Muzzle = false,
            ImprovedBarrel = false,
            Mk2Ammo = WeaponMk2AmmoMode.Standard
        };
    }

    private void ConfigureEnemyRaidPed(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)
    {
        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            return;
        }

        spawned.BaseBehavior = NpcBehavior.Attacker;
        spawned.Behavior = NpcBehavior.Attacker;
        spawned.Activated = true;
        spawned.IsReturningHome = false;
        spawned.LastCombatActivityAt = Game.GameTime;
        spawned.NextThinkAt = Game.GameTime + 60000;
        spawned.NextBlipRefreshAt = 0;

        if (Entity.Exists(assignedVehicle))
        {
            spawned.BodyguardAssignedVehicleHandle = assignedVehicle.Handle;
            spawned.BodyguardAssignedSeat = assignedSeat;
            spawned.BodyguardIsDriver = assignedSeat == -1;
        }

        spawned.Ped.MaxHealth = EnemyRaidHealth;
        spawned.Ped.Health = EnemyRaidHealth;
        spawned.Ped.Armor = EnemyRaidArmor;
        spawned.Ped.Accuracy = 45;
        spawned.Ped.ShootRate = 750;
        spawned.Ped.IsEnemy = true;
        spawned.Ped.IsPersistent = true;
        spawned.Ped.BlockPermanentEvents = true;
        spawned.Ped.AlwaysKeepTask = true;
        spawned.Ped.CanSwitchWeapons = true;

        try
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, spawned.Ped.Handle, true, true);
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, spawned.Ped.Handle, _hostileGroupHash);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, spawned.Ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 0, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 5, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 46, true);
            Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, spawned.Ped.Handle, false);
        }
        catch
        {
        }

        TryEnsureEnemyRaidWeapon(spawned.Ped);
        ForceRefreshEnemyRaidNpcBlip(spawned, true);
    }

    private void MaintainEnemyRaidPedState(Ped ped)
    {
        if (!Entity.Exists(ped) || ped.IsDead)
        {
            return;
        }

        ped.IsEnemy = true;
        ped.IsPersistent = true;
        ped.BlockPermanentEvents = true;
        ped.AlwaysKeepTask = true;

        try
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, _hostileGroupHash);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
        }
        catch
        {
        }
    }

    private void ConfigureEnemyRaidVehicle(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        try
        {
            vehicle.IsPersistent = true;
            vehicle.Repair();
            vehicle.EngineHealth = 1000.0f;
            vehicle.BodyHealth = 1000.0f;
            vehicle.PetrolTankHealth = 1000.0f;

            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
            Function.Call(Hash.SET_VEHICLE_COLOURS, vehicle.Handle, 145, 145);
            Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, vehicle.Handle, 145, 0);
            Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, vehicle.Handle, 2.0f);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
        }
        catch
        {
            // Certains modèles ne supportent pas toutes les options, on garde le spawn valide.
        }
    }

    private void ConfigureEnemyRaidVehicleSoftState(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        try
        {
            vehicle.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
        }
        catch
        {
        }
    }

    private void ConfigureEnemyRaidDriver(Ped driver)
    {
        if (!Entity.Exists(driver))
        {
            return;
        }

        try
        {
            Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 0.92f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 0.78f);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, EnemyRaidDrivingStyle);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, driver.Handle, false);
            Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, driver.Handle, true);
        }
        catch
        {
        }
    }

    private void TryEnsureEnemyRaidWeapon(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        try
        {
            ped.Weapons.Select(WeaponHash.SMG, true);
            return;
        }
        catch
        {
        }

        try
        {
            ped.Weapons.Give(WeaponHash.SMG, 9999, true, true);
            ped.Weapons.Select(WeaponHash.SMG, true);
            return;
        }
        catch
        {
        }

        try
        {
            ped.Weapons.Give(WeaponHash.MicroSMG, 9999, true, true);
            ped.Weapons.Select(WeaponHash.MicroSMG, true);
        }
        catch
        {
            try
            {
                ped.Weapons.Select(WeaponHash.Unarmed);
            }
            catch
            {
            }
        }
    }

    private Vector3 FindEnemyRaidVehicleSpawnPosition(Ped player, int seedIndex)
    {
        Vector3 roadPoint;

        if (TryFindHiddenRoadPointNearPlayer(
            player,
            seedIndex + 31,
            EnemyRaidSpawnMinDistance,
            EnemyRaidSpawnMaxDistance,
            out roadPoint))
        {
            return roadPoint;
        }

        Vector3 playerPos = player.Position;
        Vector3 camForward = GetGameplayCameraForwardVector();

        if (camForward.Length() < 0.001f)
        {
            camForward = Normalize(player.ForwardVector);
        }

        if (camForward.Length() < 0.001f)
        {
            camForward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 baseDirection = -camForward;
        Vector3 right = Normalize(new Vector3(baseDirection.Y, -baseDirection.X, 0.0f));

        Vector3 fallback =
            playerPos +
            baseDirection * (EnemyRaidSpawnMinDistance + (seedIndex % 4) * 8.0f) +
            right * (((seedIndex % 5) - 2) * 10.0f);

        Vector3 safe = World.GetSafeCoordForPed(fallback, false, 16);

        if (!IsZeroVector(safe) && !IsPointInPlayerView(player, safe))
        {
            return safe + new Vector3(0.0f, 0.0f, 0.45f);
        }

        float ground = World.GetGroundHeight(new Vector3(fallback.X, fallback.Y, fallback.Z + 1000.0f));

        if (Math.Abs(ground) > 0.001f)
        {
            fallback.Z = ground + 0.45f;
        }

        return fallback;
    }

    private void OrderEnemyRaidVehiclesToPlayer(bool force)
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player))
        {
            return;
        }

        List<int> activeVehicles = new List<int>(_enemyRaidVehicleHandles);

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            IssueEnemyRaidVehicleAttackOrder(vehicle, player, force);
        }
    }

    private void IssueEnemyRaidVehicleAttackOrder(Vehicle vehicle, Ped player, bool force)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player) || player.IsDead || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return;
        }

        if (!CanIssueEnemyRaidVehicleOrder(vehicle, force))
        {
            return;
        }

        ConfigureEnemyRaidDriver(driver);

        Vector3 target = player.Position;
        float stoppingRange = player.IsInVehicle() ? 10.0f : 16.0f;

        try
        {
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                vehicle.Handle,
                target.X,
                target.Y,
                target.Z,
                EnemyRaidArrivalDriveSpeed,
                EnemyRaidDrivingStyle,
                stoppingRange);
        }
        catch
        {
            try
            {
                Function.Call(Hash.TASK_COMBAT_PED, driver.Handle, player.Handle, 0, 16);
            }
            catch
            {
            }
        }
    }

    private bool CanIssueEnemyRaidVehicleOrder(Vehicle vehicle, bool force)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        int nextAt;

        if (!force &&
            _enemyRaidNextVehicleOrderAt.TryGetValue(vehicle.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        _enemyRaidNextVehicleOrderAt[vehicle.Handle] =
            Game.GameTime + EnemyRaidVehicleOrderIntervalMs + Math.Abs(vehicle.Handle % 260);

        return true;
    }

    private bool CanIssueEnemyRaidPedOrder(Ped ped, bool force)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt;

        if (!force &&
            _enemyRaidNextPedOrderAt.TryGetValue(ped.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        _enemyRaidNextPedOrderAt[ped.Handle] =
            Game.GameTime + EnemyRaidPedOrderIntervalMs + Math.Abs(ped.Handle % 240);

        return true;
    }

    private void StartEnemyRaidPassengerDriveBy(Ped passenger, Ped player, bool force)
    {
        if (!Entity.Exists(passenger) || passenger.IsDead || !Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        if (!CanIssueEnemyRaidPedOrder(passenger, force))
        {
            return;
        }

        TryEnsureEnemyRaidWeapon(passenger);

        try
        {
            Function.Call(
                Hash.TASK_DRIVE_BY,
                passenger.Handle,
                player.Handle,
                0,
                0.0f,
                0.0f,
                0.0f,
                EnemyRaidDriveByDistance,
                80,
                true,
                EnemyRaidFullAutoFiringPattern);
        }
        catch
        {
            try
            {
                Function.Call(Hash.TASK_COMBAT_PED, passenger.Handle, player.Handle, 0, 16);
            }
            catch
            {
            }
        }
    }

    private void StartEnemyRaidOnFootCombat(Ped enemy, Ped player, bool force)
    {
        if (!Entity.Exists(enemy) || enemy.IsDead || !Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        if (!CanIssueEnemyRaidPedOrder(enemy, force))
        {
            return;
        }

        MaintainEnemyRaidPedState(enemy);
        TryEnsureEnemyRaidWeapon(enemy);

        try
        {
            Function.Call(Hash.TASK_COMBAT_PED, enemy.Handle, player.Handle, 0, 16);

            if (enemy.Position.DistanceTo(player.Position) <= EnemyRaidOnFootShootDistance &&
                CanPedSeeEntity(enemy, player, EnemyRaidOnFootShootDistance))
            {
                Function.Call(
                    Hash.TASK_SHOOT_AT_ENTITY,
                    enemy.Handle,
                    player.Handle,
                    1500,
                    EnemyRaidFullAutoFiringPattern);
            }
        }
        catch
        {
        }
    }

    private void CommandEnemyRaidOccupantsLeaveVehicle(Vehicle vehicle, Ped player, bool force)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        List<int> activeNpcs = new List<int>(_enemyRaidNpcHandles);

        for (int i = 0; i < activeNpcs.Count; i++)
        {
            SpawnedNpc npc = FindSpawnedNpcByHandle(activeNpcs[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            if (!npc.Ped.IsInVehicle(vehicle))
            {
                continue;
            }

            CommandEnemyRaidPedLeaveVehicle(npc, vehicle, force);
        }
    }

    private void CommandEnemyRaidPedLeaveVehicle(SpawnedNpc npc, Vehicle vehicle, bool force)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || !Entity.Exists(vehicle))
        {
            return;
        }

        if (!npc.Ped.IsInVehicle(vehicle))
        {
            return;
        }

        if (!CanIssueEnemyRaidPedOrder(npc.Ped, force))
        {
            return;
        }

        try
        {
            Function.Call(Hash.TASK_LEAVE_VEHICLE, npc.Ped.Handle, vehicle.Handle, 256);
        }
        catch
        {
            try
            {
                Function.Call(Hash.TASK_LEAVE_VEHICLE, npc.Ped.Handle, vehicle.Handle, 0);
            }
            catch
            {
            }
        }

        /*
         * Correction blip :
         * GTA peut garder le blip du ped masqué après la sortie véhicule.
         * On le retire maintenant, puis UpdateEnemyRaidNpc() le recrée dès que le ped est à pied.
         */
        RemoveNpcBlip(npc);
        _enemyRaidNpcWasInVehicle[npc.Ped.Handle] = true;
        _enemyRaidNextPedOrderAt[npc.Ped.Handle] = Game.GameTime + 500 + Math.Abs(npc.Ped.Handle % 220);
    }

    private void RescueEnemyRaidVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        if (vehicle.Position.DistanceTo(player.Position) < EnemyRaidTooFarVehicleDistance)
        {
            return;
        }

        if (IsEntityLikelyVisibleToPlayer(vehicle))
        {
            return;
        }

        int lastRescueAt;

        if (_enemyRaidLastVehicleRescueAt.TryGetValue(vehicle.Handle, out lastRescueAt) &&
            Game.GameTime - lastRescueAt < EnemyRaidVehicleRescueCooldownMs)
        {
            return;
        }

        Vector3 point;

        if (!TryFindHiddenRoadPointNearPlayer(
            player,
            seedIndex + 70,
            EnemyRaidRelocationMinDistance,
            EnemyRaidRelocationMaxDistance,
            out point))
        {
            point = FindEnemyRaidVehicleSpawnPosition(player, seedIndex + 70);
        }

        try
        {
            vehicle.Position = point;
            vehicle.Heading = HeadingFromTo(point, player.Position);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
        }
        catch
        {
        }

        _enemyRaidLastVehicleRescueAt[vehicle.Handle] = Game.GameTime;
        InitializeEnemyRaidVehicleTracking(vehicle);
        IssueEnemyRaidVehicleAttackOrder(vehicle, player, true);
    }

    private void InitializeEnemyRaidVehicleTracking(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        _enemyRaidLastVehiclePositions[vehicle.Handle] = vehicle.Position;
        _enemyRaidLastVehicleMoveAt[vehicle.Handle] = Game.GameTime;
        _enemyRaidNextVehicleOrderAt[vehicle.Handle] = 0;
    }

    private bool IsEnemyRaidVehicleStuck(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        Vector3 lastPosition;

        if (!_enemyRaidLastVehiclePositions.TryGetValue(vehicle.Handle, out lastPosition))
        {
            InitializeEnemyRaidVehicleTracking(vehicle);
            return false;
        }

        if (vehicle.Position.DistanceTo(lastPosition) > 2.4f)
        {
            _enemyRaidLastVehiclePositions[vehicle.Handle] = vehicle.Position;
            _enemyRaidLastVehicleMoveAt[vehicle.Handle] = Game.GameTime;
            return false;
        }

        int lastMoveAt;

        if (!_enemyRaidLastVehicleMoveAt.TryGetValue(vehicle.Handle, out lastMoveAt))
        {
            _enemyRaidLastVehicleMoveAt[vehicle.Handle] = Game.GameTime;
            return false;
        }

        return Game.GameTime - lastMoveAt >= EnemyRaidStuckTimeoutMs;
    }

    private void ClearEnemyRaidVehicleTracking(int handle)
    {
        _enemyRaidNextVehicleOrderAt.Remove(handle);
        _enemyRaidLastVehiclePositions.Remove(handle);
        _enemyRaidLastVehicleMoveAt.Remove(handle);
        _enemyRaidLastVehicleRescueAt.Remove(handle);
    }

    private void HandleEnemyRaidPlayerDeath(Ped player)
    {
        if (!_enemyRaidActive &&
            _enemyRaidNpcHandles.Count == 0 &&
            _enemyRaidKnownNpcHandles.Count == 0 &&
            _enemyRaidVehicleHandles.Count == 0 &&
            _enemyRaidVehicleCleanupHandles.Count == 0)
        {
            return;
        }

        // Je supprime toute la vague quand je meurs pour eviter que les Ballas restent en ville.
        ForceDeleteAllEnemyRaidEntitiesAndRecords(true);
        ShowStatus("Ballas : attaque annulée après ta mort.", 3500);
    }

    private void BeginEnemyRaidPostCombatCleanup()
    {
        if (_enemyRaidKnownNpcHandles.Count > 0)
        {
            List<int> npcHandles = new List<int>(_enemyRaidKnownNpcHandles);

            for (int i = 0; i < npcHandles.Count; i++)
            {
                RemoveEnemyRaidNpcRecord(npcHandles[i], true);
            }
        }

        if (_enemyRaidVehicleHandles.Count > 0)
        {
            List<int> vehicleHandles = new List<int>(_enemyRaidVehicleHandles);

            for (int i = 0; i < vehicleHandles.Count; i++)
            {
                QueueEnemyRaidVehicleForCleanup(vehicleHandles[i]);
            }
        }

        _enemyRaidActive = false;
        _nextEnemyRaidThinkAt = 0;
    }

    private void UpdateEnemyRaidAbandonedVehicles(Ped player)
    {
        if (_enemyRaidVehicleCleanupHandles.Count == 0)
        {
            return;
        }

        List<int> handles = new List<int>(_enemyRaidVehicleCleanupHandles);

        for (int i = 0; i < handles.Count; i++)
        {
            int handle = handles[i];
            Vehicle vehicle = FindVehicleByHandle(handle);
            PlacedVehicle placed = FindPlacedVehicleRecordByHandle(handle);

            if (placed != null)
            {
                RemovePlacedVehicleBlip(placed);
            }

            if (!Entity.Exists(vehicle))
            {
                RemoveEnemyRaidPlacedVehicleRecord(handle, false);
                _enemyRaidVehicleCleanupHandles.Remove(handle);
                _enemyRaidVehicleCleanupStartedAt.Remove(handle);
                ClearEnemyRaidVehicleTracking(handle);
                continue;
            }

            if (ShouldDeleteEnemyRaidAbandonedVehicle(vehicle, player, handle))
            {
                DeleteEnemyRaidVehicleAndRecord(handle, true);
            }
        }
    }

    private bool ShouldDeleteEnemyRaidAbandonedVehicle(Vehicle vehicle, Ped player, int handle)
    {
        if (!Entity.Exists(vehicle))
        {
            return true;
        }

        int cleanupStartedAt;

        if (!_enemyRaidVehicleCleanupStartedAt.TryGetValue(handle, out cleanupStartedAt))
        {
            cleanupStartedAt = Game.GameTime;
            _enemyRaidVehicleCleanupStartedAt[handle] = cleanupStartedAt;
        }

        int elapsed = Game.GameTime - cleanupStartedAt;

        if (elapsed < EnemyRaidPostCombatVehicleCleanupGraceMs)
        {
            return false;
        }

        if (!IsVehicleDriveable(vehicle))
        {
            return true;
        }

        if (!Entity.Exists(player))
        {
            return elapsed >= EnemyRaidPostCombatVehicleCleanupGraceMs;
        }

        float distance = vehicle.Position.DistanceTo(player.Position);
        bool visible = IsEntityLikelyVisibleToPlayer(vehicle);

        if (distance >= EnemyRaidPostCombatVehicleForceCleanupDistance)
        {
            return true;
        }

        if (distance >= EnemyRaidPostCombatVehicleCleanupDistance && !visible)
        {
            return true;
        }

        if (!visible && elapsed >= EnemyRaidVisibleVehicleCleanupMaxMs / 3)
        {
            return true;
        }

        return elapsed >= EnemyRaidVisibleVehicleCleanupMaxMs;
    }

    private void QueueEnemyRaidVehicleForCleanup(int handle)
    {
        if (handle == 0)
        {
            return;
        }

        _enemyRaidVehicleHandles.Remove(handle);
        ClearEnemyRaidVehicleTracking(handle);
        _enemyRaidVehicleCleanupHandles.Add(handle);

        if (!_enemyRaidVehicleCleanupStartedAt.ContainsKey(handle))
        {
            _enemyRaidVehicleCleanupStartedAt[handle] = Game.GameTime;
        }

        PlacedVehicle placed = FindPlacedVehicleRecordByHandle(handle);

        if (placed != null)
        {
            RemovePlacedVehicleBlip(placed);
        }
    }

    private void ForceDeleteAllEnemyRaidEntitiesAndRecords(bool includePendingVehicles)
    {
        List<int> npcHandles = new List<int>(_enemyRaidKnownNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            RemoveEnemyRaidNpcRecord(npcHandles[i], true);
        }

        List<int> vehicleHandles = new List<int>(_enemyRaidVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            DeleteEnemyRaidVehicleAndRecord(vehicleHandles[i], true);
        }

        if (includePendingVehicles)
        {
            List<int> cleanupHandles = new List<int>(_enemyRaidVehicleCleanupHandles);

            for (int i = 0; i < cleanupHandles.Count; i++)
            {
                DeleteEnemyRaidVehicleAndRecord(cleanupHandles[i], true);
            }
        }

        _enemyRaidActive = false;
        _nextEnemyRaidThinkAt = 0;
    }

    private void RegisterEnemyRaidNpc(SpawnedNpc spawned, bool startsInVehicle)
    {
        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            return;
        }

        int handle = spawned.Ped.Handle;

        _enemyRaidNpcHandles.Add(handle);
        _enemyRaidKnownNpcHandles.Add(handle);
        _enemyRaidNpcWasInVehicle[handle] = startsInVehicle;
        _enemyRaidNextPedOrderAt[handle] = 0;
    }

    private void RemoveEnemyRaidNpcRecord(int handle, bool deleteEntity)
    {
        _enemyRaidNpcHandles.Remove(handle);
        _enemyRaidKnownNpcHandles.Remove(handle);
        _enemyRaidNextPedOrderAt.Remove(handle);
        _enemyRaidNpcWasInVehicle.Remove(handle);

        for (int i = _spawnedNpcs.Count - 1; i >= 0; i--)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc == null)
            {
                continue;
            }

            if (GetSpawnedNpcRecordHandleSafe(npc) != handle)
            {
                continue;
            }

            RemoveNpcBlip(npc);

            if (deleteEntity && Entity.Exists(npc.Ped))
            {
                DeleteEntitySafe(npc.Ped);
            }

            _spawnedNpcs.RemoveAt(i);
            break;
        }
    }

    private void DeleteEnemyRaidVehicleAndRecord(int handle, bool deleteEntity)
    {
        Vehicle vehicle = FindVehicleByHandle(handle);

        RemoveEnemyRaidPlacedVehicleRecord(handle, false);

        if (deleteEntity && Entity.Exists(vehicle))
        {
            DeleteEntitySafe(vehicle);
        }

        _enemyRaidVehicleHandles.Remove(handle);
        _enemyRaidVehicleCleanupHandles.Remove(handle);
        _enemyRaidVehicleCleanupStartedAt.Remove(handle);
        ClearEnemyRaidVehicleTracking(handle);
    }

    private void RemoveEnemyRaidPlacedVehicleRecord(int handle, bool deleteEntity)
    {
        for (int i = _placedVehicles.Count - 1; i >= 0; i--)
        {
            PlacedVehicle placed = _placedVehicles[i];

            if (placed == null)
            {
                continue;
            }

            if (GetPlacedVehicleRecordHandleSafe(placed) != handle)
            {
                continue;
            }

            RemovePlacedVehicleBlip(placed);

            if (deleteEntity && Entity.Exists(placed.Vehicle))
            {
                DeleteEntitySafe(placed.Vehicle);
            }

            _placedVehicles.RemoveAt(i);
        }
    }

    private SpawnedNpc FindEnemyRaidNpcRecordByHandle(int handle)
    {
        if (handle == 0)
        {
            return null;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc != null && GetSpawnedNpcRecordHandleSafe(npc) == handle)
            {
                return npc;
            }
        }

        return null;
    }

    private PlacedVehicle FindPlacedVehicleRecordByHandle(int handle)
    {
        if (handle == 0)
        {
            return null;
        }

        for (int i = 0; i < _placedVehicles.Count; i++)
        {
            PlacedVehicle placed = _placedVehicles[i];

            if (placed != null && GetPlacedVehicleRecordHandleSafe(placed) == handle)
            {
                return placed;
            }
        }

        return null;
    }

    private int GetSpawnedNpcRecordHandleSafe(SpawnedNpc npc)
    {
        if (npc == null || npc.Ped == null)
        {
            return 0;
        }

        try
        {
            return npc.Ped.Handle;
        }
        catch
        {
            return 0;
        }
    }

    private int GetPlacedVehicleRecordHandleSafe(PlacedVehicle placed)
    {
        if (placed == null || placed.Vehicle == null)
        {
            return 0;
        }

        try
        {
            return placed.Vehicle.Handle;
        }
        catch
        {
            return 0;
        }
    }

    private int GetPedEntityHandleSafe(Ped ped)
    {
        if (ped == null)
        {
            return 0;
        }

        try
        {
            return ped.Handle;
        }
        catch
        {
            return 0;
        }
    }

    private int GetVehicleEntityHandleSafe(Vehicle vehicle)
    {
        if (vehicle == null)
        {
            return 0;
        }

        try
        {
            return vehicle.Handle;
        }
        catch
        {
            return 0;
        }
    }

    private int CountLiveEnemyRaidMembersWithoutCleanup()
    {
        int count = 0;
        List<int> handles = new List<int>(_enemyRaidNpcHandles);

        for (int i = 0; i < handles.Count; i++)
        {
            SpawnedNpc npc = FindEnemyRaidNpcRecordByHandle(handles[i]);

            if (npc != null && Entity.Exists(npc.Ped) && !npc.Ped.IsDead)
            {
                count++;
            }
        }

        return count;
    }

    private void UpdateEnemyRaidNpcBlipState(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            RemoveNpcBlip(npc);
            return;
        }

        bool isInVehicle = npc.Ped.IsInVehicle();
        MarkEnemyRaidNpcVehicleState(npc, isInVehicle, false);
    }

    private void MarkEnemyRaidNpcVehicleState(SpawnedNpc npc, bool isInVehicle, bool forceRecreate)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        int handle = npc.Ped.Handle;
        bool previousState;
        bool hasPreviousState = _enemyRaidNpcWasInVehicle.TryGetValue(handle, out previousState);
        bool transitioned = !hasPreviousState || previousState != isInVehicle;

        _enemyRaidNpcWasInVehicle[handle] = isInVehicle;

        if (forceRecreate || transitioned || !isInVehicle)
        {
            ForceRefreshEnemyRaidNpcBlip(npc, forceRecreate || transitioned);
        }
    }

    private void ForceRefreshEnemyRaidNpcBlip(SpawnedNpc npc, bool recreate)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            RemoveNpcBlip(npc);
            return;
        }

        if (recreate)
        {
            RemoveNpcBlip(npc);
        }

        npc.NextBlipRefreshAt = 0;
        CreateOrUpdateNpcBlip(npc);
    }

    private void ForceRefreshAllEnemyRaidNpcBlips()
    {
        List<int> handles = new List<int>(_enemyRaidNpcHandles);

        for (int i = 0; i < handles.Count; i++)
        {
            SpawnedNpc npc = FindEnemyRaidNpcRecordByHandle(handles[i]);

            if (npc != null && Entity.Exists(npc.Ped) && !npc.Ped.IsDead)
            {
                ForceRefreshEnemyRaidNpcBlip(npc, true);
            }
        }
    }
}
