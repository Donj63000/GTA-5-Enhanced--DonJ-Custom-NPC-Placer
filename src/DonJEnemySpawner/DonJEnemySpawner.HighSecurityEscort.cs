using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Keys = System.Windows.Forms.Keys;

public sealed partial class DonJEnemySpawner : Script
{
    // ---------------------------------------------------------------------
    // Contact téléphone : Escorte haute sécurité
    // ---------------------------------------------------------------------
    /*
     * Objectif : ajouter une escorte blindée sans casser les appels existants.
     * - C reste réservé au Cartel.
     * - R reste réservé aux Ballas.
     * - L appelle / renvoie uniquement cette escorte haute sécurité.
     *
     * L'IA est volontairement séparée du Cartel et du comportement Bodyguard
     * générique : les handles ci-dessous permettent de court-circuiter les
     * ordres génériques et d'éviter les micro-coupures avec 20+ PNJ.
     */

    private const string HighSecurityEscortContactName = "Escorte haute sécurité";

    private const int HighSecurityEscortBallerCount = 4;
    private const int HighSecurityEscortBallerOccupantCount = 4;
    private const int HighSecurityEscortLimousineGuardCount = 4;

    private const int HighSecurityEscortGuardHealth = CartelGuardHealth;
    private const int HighSecurityEscortGuardArmor = CartelGuardArmor;

    private const int HighSecurityEscortCallCooldownMs = 1800;
    private const int HighSecurityEscortThinkIntervalMs = 550;

    /*
     * Conduite limousine : cadence volontairement proche du Cartel.
     * L'ancien mode renvoyait des ordres véhicules toutes les 850 ms, ce qui
     * relançait sans arrêt l'IA conducteur et rendait le convoi nerveux.
     * Ici on laisse le conducteur garder sa tâche comme un taxi, puis on ne
     * recale que si la cible change, si le véhicule ralentit trop, ou en force.
     */
    private const int HighSecurityEscortVehicleOrderIntervalMs = 1650;
    private const int HighSecurityEscortPedOrderIntervalMs = 850;
    private const int HighSecurityEscortDismissOrderIntervalMs = 1500;
    private const int HighSecurityEscortDismissForceCleanupMs = 22000;

    private const int HighSecurityEscortCombatOrderIntervalMs = CartelCombatOrderIntervalMs;
    private const int HighSecurityEscortThreatScanIntervalMs = CartelThreatScanIntervalMs;
    private const int HighSecurityEscortThreatCacheLifetimeMs = CartelThreatCacheLifetimeMs;
    private const int HighSecurityEscortMaxGuardThreatScansPerPass = CartelMaxGuardThreatScansPerPass;
    private const int HighSecurityEscortThreatRelationshipRefreshMs = CartelThreatRelationshipRefreshMs;
    private const int HighSecurityEscortGuardPassiveMaintenanceIntervalMs = CartelGuardPassiveMaintenanceIntervalMs;
    private const int HighSecurityEscortGuardPassiveMaintenanceJitterMs = CartelGuardPassiveMaintenanceJitterMs;
    private const int HighSecurityEscortGuardMobilityOrderIntervalMs = CartelGuardMobilityOrderIntervalMs;
    private const int HighSecurityEscortGuardFootFollowIntervalMs = CartelGuardFootFollowIntervalMs;

    private const float HighSecurityEscortSpawnMinDistance = 72.0f;
    private const float HighSecurityEscortSpawnMaxDistance = 128.0f;

    /*
     * Vitesses naturelles. GTA exprime ces vitesses en unités proches du m/s.
     * On garde une limousine stable : rapide en ligne droite, douce en ville,
     * lente à l'arrivée. Les Baller rattrapent sans pousser la limousine.
     */
    private const float HighSecurityEscortArrivalDriveSpeed = 24.0f;
    private const float HighSecurityEscortConvoyDriveSpeed = 21.5f;
    private const float HighSecurityEscortConvoyCloseDriveSpeed = 8.5f;
    private const float HighSecurityEscortFormationCatchupSpeed = 28.0f;

    /*
     * Limites effectives du mode taxi.
     * Les constantes historiques ci-dessus restent stables pour le contrat/tests
     * du mod, mais les ordres envoyés aux chauffeurs sont maintenant plafonnés
     * pour obtenir un convoi propre : approche douce, moins de dépassements et
     * freinage plus tôt autour du joueur.
     */
    private const float HighSecurityEscortSmoothArrivalDriveSpeed = 18.0f;
    private const float HighSecurityEscortSmoothConvoyDriveSpeed = 16.0f;
    private const float HighSecurityEscortSmoothConvoyCloseDriveSpeed = 6.5f;
    private const float HighSecurityEscortSmoothFormationCatchupSpeed = 22.0f;
    private const float HighSecurityEscortPassengerFootExitDistance = 13.5f;
    private const float HighSecurityEscortPassengerCombatFootExitDistance = 18.0f;
    private const float HighSecurityEscortOnFootReturnToVehicleDistance = 24.0f;
    private const float HighSecurityEscortLimousineEntryAssistDistance = 10.0f;
    private const float HighSecurityEscortPickupParkedReturnDistance = 16.0f;
    private const float HighSecurityEscortDepartureGuardReturnDistance = 30.0f;
    private const int HighSecurityEscortDepartureReadyTimeoutMs = 2600;
    private const int HighSecurityEscortEmergencyFleeCacheMs = 6500;
    private const int HighSecurityEscortEmergencyFleeMaxCandidateChecks = 10;
    private const float HighSecurityEscortEmergencyFleeDistance = 155.0f;

    /*
     * Convoi limousine V3 : une seule file propre sur route.
     * Ces distances évitent les spawns dispersés, les SUV qui se mettent en
     * parallèle sur le trottoir, et les freinages trop tardifs autour du joueur.
     */
    private const float HighSecurityEscortConvoyLineSpawnSpacing = 13.5f;
    private const float HighSecurityEscortArrivalLimoRoadStopDistance = 7.5f;
    private const float HighSecurityEscortArrivalConvoySpacing = 13.0f;
    private const float HighSecurityEscortRushRouteSpeed = 25.5f;
    private const float HighSecurityEscortRushFormationCatchupSpeed = 31.0f;
    private const float HighSecurityEscortRushCloseSpeed = 11.0f;

    /*
     * Récupération convoi V4.
     * - Lost escort : les Baller trop loin sont replacés derrière la limousine
     *   uniquement hors champ et uniquement pendant un trajet.
     * - Road rejoin : avant toute téléportation, un véhicule coincé recule puis
     *   reprend un node routier proche pour sortir des barrières/décors.
     */
    private const int HighSecurityEscortLostEscortTeleportCooldownMs = 9500;
    private const int HighSecurityEscortRoadRejoinAfterMs = 6100;
    private const int HighSecurityEscortRoadRejoinCooldownMs = 4300;
    private const int HighSecurityEscortRoadRejoinOrderMs = 2600;
    private const float HighSecurityEscortLostEscortTeleportDistance = 142.0f;
    private const float HighSecurityEscortLostEscortTargetMinPlayerDistance = 39.0f;
    private const float HighSecurityEscortLostEscortExtraBackDistance = 32.0f;
    private const float HighSecurityEscortLostEscortRoadSnapDistance = 22.0f;
    private const float HighSecurityEscortRoadRejoinProbeDistance = 15.5f;
    private const float HighSecurityEscortRoadRejoinDriveSpeed = 8.8f;

    /*
     * Discipline route principale V5.
     * Ces valeurs ne changent pas le contrat public du mode L : elles servent
     * uniquement au scoring interne pour refuser les ruelles et garder les SUV
     * collés à une file lisible derrière la limousine.
     */
    private const int HighSecurityEscortMajorRoadSearchAttempts = 14;
    private const int HighSecurityEscortMajorRoadNodeProbeCount = 2;
    private const int HighSecurityEscortPickupRoadCacheMs = 6200;
    private const int HighSecurityEscortPickupRoadFailedRetryMs = 1800;
    private const int HighSecurityEscortPickupRoadMaxCandidateChecks = 28;
    private const float HighSecurityEscortPickupRoadPlayerRefreshDistance = 18.0f;
    private const float HighSecurityEscortRuntimeRoadSnapDistance = 18.0f;
    private const float HighSecurityEscortRuntimeRoadDirectionProbeDistance = 20.0f;
    private const float HighSecurityEscortSpawnLineMaxNodeSnapDistance = 19.0f;
    private const float HighSecurityEscortSpawnLineMinSlotDistance = 7.25f;
    private const float HighSecurityEscortMajorRoadSideProbeDistance = 5.25f;
    private const float HighSecurityEscortMajorRoadGoodSideProbeDistance = 7.75f;
    private const float HighSecurityEscortMajorRoadForwardProbeDistance = 18.0f;
    private const float HighSecurityEscortMajorRoadMinimumLineScore = 14.0f;
    private const float HighSecurityEscortPickupRoadMinPlayerDistance = 7.0f;
    private const float HighSecurityEscortPickupRoadMaxPlayerDistance = 68.0f;
    private const float HighSecurityEscortPickupRoadSearchDistance = 54.0f;
    private const float HighSecurityEscortConvoyMaxVisibleGap = 78.0f;
    private const float HighSecurityEscortConvoySoftCatchupGap = 38.0f;
    private const float HighSecurityEscortConvoyHardCatchupGap = 62.0f;
    private const int HighSecurityEscortFormationTargetCacheMs = 850;
    private const float HighSecurityEscortFormationTargetCacheReuseDistance = 9.0f;
    private const float HighSecurityEscortFormationDirectCorrectionDistance = 34.0f;
    private const float HighSecurityEscortFormationHardCorrectionDistance = 48.0f;
    private const float HighSecurityEscortFormationAheadCorrectionDistance = 4.0f;
    private const float HighSecurityEscortFormationEscortRetaskTargetShift = 12.5f;

    private const float HighSecurityEscortDestinationArriveDistance = 10.5f;
    private const float HighSecurityEscortFootExitDistance = CartelVehicleFootExitDistance;
    private const float HighSecurityEscortVehicleApproachDistance = 72.0f;
    private const float HighSecurityEscortGuardFootFollowDistance = CartelGuardFootFollowDistance;
    private const float HighSecurityEscortGuardFootStandDistance = CartelGuardFootStandDistance;
    private const float HighSecurityEscortThreatScanRadius = CartelThreatScanRadius;
    private const float HighSecurityEscortThreatEvidenceRadius = CartelThreatEvidenceRadius;
    private const float HighSecurityEscortDriveByDistance = CartelDriveByDistance;
    private const float HighSecurityEscortPassengerExitCombatDistance = CartelPassengerExitCombatDistance;
    private const float HighSecurityEscortOnFootShootDistance = CartelOnFootShootDistance;
    private const float HighSecurityEscortVehicleTooFarDistance = 235.0f;
    private const float HighSecurityEscortDismissDeleteDistance = 46.0f;

    private const int HighSecurityEscortDrivingStyle = ProfessionalDrivingStyle;

    /*
     * Conduite convoi V3.
     * - Normal : style professionnel, respect du trafic et des feux, comportement taxi.
     * - Urgence (Espace) : style taxi rapide, dépassements propres, feux ignorés.
     * - Combat : style escorte agressive, réservé aux embuscades.
     */
    private const int HighSecurityEscortCalmTaxiDrivingStyle = ProfessionalDrivingStyle;
    private const int HighSecurityEscortFastTaxiDrivingStyle = 786469;
    private const int HighSecurityEscortCombatDrivingStyle = 2883621;

    private const int HighSecurityEscortCombatMemoryMs = 6500;
    private const int HighSecurityEscortGuardCombatFootLockMs = 11500;
    private const int HighSecurityEscortSoftUnstuckAfterMs = 3600;
    private const int HighSecurityEscortSoftUnstuckCooldownMs = 5200;
    private const int HighSecurityEscortHardRescueAfterMs = 19000;
    private const int HighSecurityEscortSoftReverseMs = 1250;
    private const int HighSecurityEscortImmediateStopHoldMs = 1400;
    private const int HighSecurityEscortParkedPickupStopHoldMs = 5200;
    private const int HighSecurityEscortImmediateBrakeAction = 1;
    private const int HighSecurityEscortSoftReverseAction = 2;
    private const int HighSecurityEscortSoftReverseLeftAction = 3;
    private const int HighSecurityEscortSoftReverseRightAction = 4;

    private const float HighSecurityEscortCombatRouteSpeed = 24.0f;
    private const float HighSecurityEscortCombatFormationCatchupSpeed = 27.0f;
    private const float HighSecurityEscortCombatCloseSpeed = 10.0f;
    private const float HighSecurityEscortLimoGuardExitThreatDistance = 31.0f;
    private const float HighSecurityEscortBlockedLimoGuardExitThreatDistance = 52.0f;
    private const float HighSecurityEscortObstacleProbeDistance = 8.5f;

    private const int HighSecurityEscortFullAutoFiringPattern = CartelFullAutoFiringPattern;

    private const int HighSecurityEscortModeNone = 0;
    private const int HighSecurityEscortModeArriving = 1;
    private const int HighSecurityEscortModeStandby = 2;
    private const int HighSecurityEscortModeConvoyRoute = 3;
    private const int HighSecurityEscortModeFootFollow = 4;
    private const int HighSecurityEscortModePlayerVehicleFollow = 5;
    private const int HighSecurityEscortModeDismissing = 6;

    private const int HighSecurityEscortVehicleRoleLimousine = -100;
    private const int HighSecurityEscortVehicleRoleFrontLeft = 0;
    private const int HighSecurityEscortVehicleRoleFrontRight = 1;
    private const int HighSecurityEscortVehicleRoleRearLeft = 2;
    private const int HighSecurityEscortVehicleRoleRearRight = 3;

    private const int HighSecurityWaypointBlipSprite = 8;
    private const int HighSecurityEscortEnterVehicleControl = 23;
    private const ulong NativeGetFirstBlipInfoId = 0x1BEDE233E6CD2A1FUL;
    private const ulong NativeDoesBlipExist = 0xA6DB27D19ECBB7DAUL;
    private const ulong NativeGetBlipCoords = 0x586AFE3FF72D996EUL;
    private const ulong NativeDisableControlAction = 0xFE99B66D079CF6BCUL;
    private const ulong NativeTaskVehicleShootAtPed = 0x10AB107B887214D8UL;
    private const ulong NativeStartVehicleHorn = 0x9C8C6504B5B63D2CUL;
    private const ulong NativeTaskVehicleTempAction = 0xC429DCEEB339E129UL;

    private bool _highSecurityEscortPhoneKeyLatch;
    private bool _highSecurityEscortRouteKeyLatch;
    private bool _highSecurityEscortEnterKeyLatch;
    private bool _highSecurityEscortRushKeyLatch;
    private bool _highSecurityEscortPlayerDeathDismissed;
    private bool _highSecurityEscortRushMode;
    private bool _highSecurityEscortArrivalAnnounced;
    private bool _highSecurityEscortPickupParked;
    private bool _highSecurityEscortRoutePaused;
    private bool _highSecurityEscortStopKeyLatch;
    private bool _highSecurityEscortLCommandConsumedUntilRelease;
    private bool _highSecurityEscortLimousineLostAnnounced;

    private bool _highSecurityEscortActive;
    private bool _highSecurityEscortDismissing;

    private int _highSecurityEscortMode = HighSecurityEscortModeNone;
    private int _nextHighSecurityEscortCallAllowedAt;
    private int _nextHighSecurityEscortThinkAt;
    private int _nextHighSecurityEscortDismissOrderAt;
    private int _highSecurityEscortDismissStartedAt;
    private int _highSecurityEscortDismissCleanupAt;
    private int _highSecurityEscortLimousineHandle;
    private int _highSecurityEscortLimousineTurretGuardHandle;
    private int _highSecurityEscortPlayerSeat = 1;
    private int _highSecurityEscortDepartureHoldUntil;
    private int _highSecurityEscortEmergencyFleeUntil;
    private int _highSecurityEscortPickupRoadCacheUntil;
    private int _highSecurityEscortPickupRoadFailedUntil;
    private Vector3 _highSecurityEscortEmergencyFleeDestination;
    private Vector3 _highSecurityEscortCachedPickupRoadPoint;
    private Vector3 _highSecurityEscortCachedPickupDirection;
    private Vector3 _highSecurityEscortCachedPickupPlayerPosition;
    private Vector3[] _highSecurityEscortCachedArrivalTargets;

    private bool _highSecurityEscortDestinationActive;
    private Vector3 _highSecurityEscortDestination;

    private Ped _highSecurityEscortCachedThreatPed;
    private int _highSecurityEscortCachedThreatUntil;
    private int _nextHighSecurityEscortThreatScanAt;
    private int _highSecurityEscortGuardThreatScanCursor;
    private int _highSecurityEscortLastThreatRelationshipHandle;
    private int _highSecurityEscortLastThreatRelationshipAt;
    private int _highSecurityEscortCombatModeUntil;

    private readonly HashSet<int> _highSecurityEscortNpcHandles = new HashSet<int>();
    private readonly HashSet<int> _highSecurityEscortKnownNpcHandles = new HashSet<int>();
    private readonly HashSet<int> _highSecurityEscortVehicleHandles = new HashSet<int>();
    private readonly HashSet<int> _highSecurityEscortFullyUpgradedVehicleHandles = new HashSet<int>();

    private readonly Dictionary<int, int> _highSecurityEscortVehicleRoles = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortNextVehicleOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortNextPedOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortNextCombatOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortNextGuardPassiveMaintenanceAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortNextGuardMobilityOrderAt = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector3> _highSecurityEscortLastVehiclePositions = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> _highSecurityEscortLastVehicleMoveAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortLastVehicleRescueAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortLastVehicleSoftMaintenanceAt = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector3> _highSecurityEscortLastVehicleOrderTarget = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Vector3> _highSecurityEscortCachedFormationTargets = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> _highSecurityEscortCachedFormationTargetUntil = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortGuardCombatFootLockUntil = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortVehicleStuckSinceAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortLastVehicleSoftUnstuckAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortLastVehicleRoadRejoinAt = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _highSecurityEscortVehicleRecoveryUntil = new Dictionary<int, int>();

    private static readonly string[] HighSecurityEscortLimousineModelNames =
    {
        "limo2",
        "stretch"
    };

    private static readonly string[] HighSecurityEscortBallerModelNames =
    {
        "baller8",
        "baller6",
        "baller5"
    };

    private sealed class HighSecurityEscortConvoySpawnSlot
    {
        public Vector3 Position;
        public float Heading;
    }

    private bool IsHighSecurityEscortPedHandle(int handle)
    {
        return handle != 0 && _highSecurityEscortKnownNpcHandles.Contains(handle);
    }

    private string GetHighSecurityEscortPhoneStatus()
    {
        CleanupHighSecurityEscortHandleSets();

        int cooldownRemaining = Math.Max(0, (_nextHighSecurityEscortCallAllowedAt - Game.GameTime + 999) / 1000);

        if (_highSecurityEscortActive && HasLiveHighSecurityEscortTeamWithoutCleanup())
        {
            return "L : renvoyer l'escorte blindée active";
        }

        if (cooldownRemaining > 0)
        {
            return "L : escorte disponible dans " + cooldownRemaining.ToString(CultureInfo.InvariantCulture) + " s";
        }

        if (_highSecurityEscortDismissing)
        {
            return "L : rappeler une nouvelle escorte blindée";
        }

        return "L : appeler limousine + 4 Baller8 haute sécurité";
    }

    private void ToggleHighSecurityEscortCall()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Escorte haute sécurité : appel impossible pendant la mort/transition du joueur.", 3500);
            return;
        }

        CleanupHighSecurityEscortHandleSets();

        if (Game.GameTime < _nextHighSecurityEscortCallAllowedAt)
        {
            int remaining = Math.Max(1, (_nextHighSecurityEscortCallAllowedAt - Game.GameTime + 999) / 1000);
            ShowStatus("Escorte haute sécurité indisponible encore " + remaining.ToString(CultureInfo.InvariantCulture) + " seconde(s).", 3000);
            return;
        }

        _nextHighSecurityEscortCallAllowedAt = Game.GameTime + HighSecurityEscortCallCooldownMs;

        if (_highSecurityEscortActive && HasLiveHighSecurityEscortTeamWithoutCleanup())
        {
            DismissHighSecurityEscort(true);
            return;
        }

        if (_highSecurityEscortDismissing)
        {
            /*
             * Comme pour le Cartel, un ancien groupe en repli ne doit pas bloquer
             * un nouvel appel. Ici je nettoie l'ancien groupe avant de créer le
             * nouveau pour éviter 40 PNJ simultanés et garder les performances.
             */
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
        }

        SpawnHighSecurityEscortConvoy();
    }

    private void SpawnHighSecurityEscortConvoy()
    {
        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Impossible d'appeler l'escorte : joueur invalide.", 3500);
            return;
        }

        EnsureRelationshipGroups();
        ForceDeleteHighSecurityEscortEntitiesAndRecords(true);

        int createdVehicles = 0;
        int createdBallerVehicles = 0;
        int createdGuards = 0;

        HighSecurityEscortConvoySpawnSlot[] spawnSlots = BuildHighSecurityEscortConvoySpawnLayout(player);

        if (spawnSlots == null || spawnSlots.Length == 0)
        {
            ShowStatus("Escorte haute sécurité : aucun point de spawn valide trouvé.", 5000);
            return;
        }

        HighSecurityEscortConvoySpawnSlot limoSlot = spawnSlots[0];
        VehicleIdentity limoIdentity;

        Vehicle limousine = CreateHighSecurityEscortVehicle(
            HighSecurityEscortLimousineModelNames,
            "Limousine blindée haute sécurité",
            limoSlot.Position,
            limoSlot.Heading,
            out limoIdentity);

        if (!Entity.Exists(limousine))
        {
            ShowStatus("Escorte haute sécurité : limousine introuvable ou impossible à créer.", 5000);
            return;
        }

        /*
         * Véhicule runtime : enregistré pour le tracking, mais jamais sauvegardé.
         */
        RegisterPlacedVehicle(limousine, limoIdentity, limoSlot.Position, limoSlot.Heading, false, false, false);
        RegisterHighSecurityEscortVehicle(limousine, HighSecurityEscortVehicleRoleLimousine);
        ConfigureHighSecurityEscortVehicle(limousine, true);

        _highSecurityEscortLimousineHandle = limousine.Handle;
        _highSecurityEscortPlayerSeat = FindHighSecurityEscortPlayerSeat(limousine);

        createdVehicles++;

        /*
         * Le chauffeur limousine est critique. On le crée en premier et on refuse
         * l'appel si le siège conducteur n'est pas réellement occupé.
         */
        if (!SpawnHighSecurityEscortGuardIntoVehicle(limousine, -1, createdGuards) ||
            !HasLiveHighSecurityEscortDriver(limousine))
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
            ShowStatus("Escorte haute sécurité : chauffeur limousine impossible à créer.", 5000);
            return;
        }

        createdGuards++;

        List<int> limoGuardSeats = BuildHighSecurityEscortLimousineGuardSeats(limousine, _highSecurityEscortPlayerSeat);
        bool turretPassengerSelected = false;

        for (int i = 0; i < limoGuardSeats.Count && createdGuards < HighSecurityEscortLimousineGuardCount; i++)
        {
            int seat = limoGuardSeats[i];

            if (seat == -1)
            {
                continue;
            }

            if (!IsHighSecurityEscortSeatSupported(limousine, seat) || !IsSeatFreeSafe(limousine, seat))
            {
                continue;
            }

            if (SpawnHighSecurityEscortGuardIntoVehicle(limousine, seat, createdGuards))
            {
                if (!turretPassengerSelected)
                {
                    int occupantHandle;

                    if (TryGetHighSecurityEscortSeatOccupantHandle(limousine, seat, out occupantHandle) && occupantHandle != 0)
                    {
                        _highSecurityEscortLimousineTurretGuardHandle = occupantHandle;
                        turretPassengerSelected = true;
                    }
                }

                createdGuards++;
            }
        }

        for (int ballerIndex = 0; ballerIndex < HighSecurityEscortBallerCount; ballerIndex++)
        {
            int role = HighSecurityEscortVehicleRoleFrontLeft + ballerIndex;
            HighSecurityEscortConvoySpawnSlot slot = spawnSlots[Math.Min(ballerIndex + 1, spawnSlots.Length - 1)];
            VehicleIdentity ballerIdentity;
            Vehicle baller = CreateHighSecurityEscortVehicle(
                HighSecurityEscortBallerModelNames,
                "Baller8 noir haute sécurité",
                slot.Position,
                slot.Heading,
                out ballerIdentity);

            if (!Entity.Exists(baller))
            {
                continue;
            }

            /*
             * Véhicule runtime : enregistré pour le tracking, mais jamais sauvegardé.
             */
            RegisterPlacedVehicle(baller, ballerIdentity, slot.Position, slot.Heading, false, false, false);
            RegisterHighSecurityEscortVehicle(baller, role);
            ConfigureHighSecurityEscortVehicle(baller, false);

            createdVehicles++;

            int[] seats = { -1, 0, 1, 2 };
            int seatsFilled = 0;

            for (int seatIndex = 0; seatIndex < seats.Length && seatsFilled < HighSecurityEscortBallerOccupantCount; seatIndex++)
            {
                int seat = seats[seatIndex];

                if (!IsHighSecurityEscortSeatSupported(baller, seat) || !IsSeatFreeSafe(baller, seat))
                {
                    continue;
                }

                if (SpawnHighSecurityEscortGuardIntoVehicle(baller, seat, createdGuards + ballerIndex * 10 + seatIndex))
                {
                    createdGuards++;
                    seatsFilled++;
                }
            }

            if (seatsFilled == 0)
            {
                DeleteHighSecurityEscortVehicleAndRecord(baller.Handle, true);
                createdVehicles--;
                continue;
            }

            createdBallerVehicles++;
        }

        CleanupHighSecurityEscortHandleSets();

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine) || !HasLiveHighSecurityEscortDriver(limousine))
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
            ShowStatus("Escorte haute sécurité : limousine créée sans chauffeur valide, appel annulé.", 5000);
            return;
        }

        if (createdBallerVehicles == 0)
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
            ShowStatus("Escorte haute sécurité : aucun 4x4 d'escorte n'a pu être créé.", 5000);
            return;
        }

        if (createdGuards == 0)
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
            ShowStatus("Escorte haute sécurité : aucun homme de main n'a pu être créé.", 5000);
            return;
        }

        _highSecurityEscortActive = true;
        _highSecurityEscortDismissing = false;
        _highSecurityEscortMode = HighSecurityEscortModeArriving;
        _highSecurityEscortDestinationActive = false;
        _highSecurityEscortDestination = Vector3.Zero;
        _highSecurityEscortRushMode = false;
        _highSecurityEscortRushKeyLatch = false;
        _highSecurityEscortArrivalAnnounced = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortStopKeyLatch = false;
        _highSecurityEscortDepartureHoldUntil = 0;
        _highSecurityEscortEmergencyFleeDestination = Vector3.Zero;
        _highSecurityEscortEmergencyFleeUntil = 0;
        _highSecurityEscortPlayerDeathDismissed = false;
        _highSecurityEscortLimousineLostAnnounced = false;
        _nextHighSecurityEscortThinkAt = 0;
        _highSecurityEscortCachedThreatPed = null;
        _highSecurityEscortCachedThreatUntil = 0;
        _nextHighSecurityEscortThreatScanAt = 0;
        _highSecurityEscortCombatModeUntil = 0;
        _highSecurityEscortGuardCombatFootLockUntil.Clear();

        OrderHighSecurityEscortArrivalToPlayer(player, true);

        ShowStatus(
            "Escorte haute sécurité appelée : convoi aligné sur route, limousine blindée + " +
            createdBallerVehicles.ToString(CultureInfo.InvariantCulture) +
            " Baller noirs, " +
            createdVehicles.ToString(CultureInfo.InvariantCulture) +
            " véhicules, " +
            createdGuards.ToString(CultureInfo.InvariantCulture) +
            " hommes Cartel. Monte à l'arrière avec F.",
            8000);
    }

    private Vehicle CreateHighSecurityEscortVehicle(string[] modelNames, string displayName, Vector3 position, float heading, out VehicleIdentity identity)
    {
        identity = null;

        if (modelNames == null || modelNames.Length == 0)
        {
            LogWarning("HighSecurityEscortVehicleSpawn", "Aucun modèle véhicule fourni pour " + displayName + ".");
            return null;
        }

        for (int i = 0; i < modelNames.Length; i++)
        {
            string modelName = modelNames[i];

            if (string.IsNullOrWhiteSpace(modelName))
            {
                continue;
            }

            Model model = new Model(modelName);
            bool modelRequested = false;

            try
            {
                if (!model.IsValid)
                {
                    LogWarning("HighSecurityEscortVehicleSpawn", "Modèle invalide : " + modelName + ".");
                    continue;
                }

                if (!model.IsInCdImage)
                {
                    LogWarning("HighSecurityEscortVehicleSpawn", "Modèle absent du CD image : " + modelName + ".");
                    continue;
                }

                if (!model.IsVehicle)
                {
                    LogWarning("HighSecurityEscortVehicleSpawn", "Modèle non véhicule : " + modelName + ".");
                    continue;
                }

                if (!model.Request(2500))
                {
                    LogWarning("HighSecurityEscortVehicleSpawn", "Chargement modèle timeout : " + modelName + ".");
                    continue;
                }

                modelRequested = true;

                Vehicle vehicle = World.CreateVehicle(model, position, NormalizeHeading(heading));

                if (!Entity.Exists(vehicle))
                {
                    LogWarning(
                        "HighSecurityEscortVehicleSpawn",
                        "World.CreateVehicle a échoué pour " + modelName +
                        " à " + position.ToString() + ".");
                    continue;
                }

                int hash = 0;

                try
                {
                    hash = Game.GenerateHash(modelName);
                }
                catch
                {
                    hash = 0;
                }

                identity = new VehicleIdentity
                {
                    Name = modelName,
                    Hash = hash,
                    DisplayName = displayName + " (" + modelName + ")"
                };

                LogInfo("HighSecurityEscortVehicleSpawn", "Véhicule créé : " + identity.DisplayName + ".");

                return vehicle;
            }
            catch (Exception ex)
            {
                LogException("HighSecurityEscortVehicleSpawn/" + modelName, ex);
            }
            finally
            {
                if (modelRequested)
                {
                    try
                    {
                        model.MarkAsNoLongerNeeded();
                    }
                    catch
                    {
                    }
                }
            }
        }

        LogWarning("HighSecurityEscortVehicleSpawn", "Aucun modèle utilisable pour " + displayName + ".");
        return null;
    }

    private void RegisterHighSecurityEscortVehicle(Vehicle vehicle, int role)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int handle = vehicle.Handle;

        _highSecurityEscortVehicleHandles.Add(handle);
        _highSecurityEscortVehicleRoles[handle] = role;
        _highSecurityEscortNextVehicleOrderAt[handle] = 0;
        _highSecurityEscortLastVehiclePositions[handle] = vehicle.Position;
        _highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;
        _highSecurityEscortLastVehicleRescueAt[handle] = 0;
        _highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;
        _highSecurityEscortCachedFormationTargets.Remove(handle);
        _highSecurityEscortCachedFormationTargetUntil.Remove(handle);
        _highSecurityEscortVehicleStuckSinceAt[handle] = 0;
        _highSecurityEscortLastVehicleSoftUnstuckAt[handle] = 0;
        _highSecurityEscortLastVehicleRoadRejoinAt[handle] = 0;
        _highSecurityEscortVehicleRecoveryUntil[handle] = 0;
    }

    private bool SpawnHighSecurityEscortGuardIntoVehicle(Vehicle vehicle, int seat, int seedIndex)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        if (!IsHighSecurityEscortSeatSupported(vehicle, seat) || !IsSeatFreeSafe(vehicle, seat))
        {
            return false;
        }

        Vector3 spawnPosition = vehicle.Position + GetSpawnOffsetAroundVehicle(Math.Abs(seedIndex % 5));
        float heading = vehicle.Heading;
        ModelIdentity identity = ResolveHighSecurityEscortGuardModelIdentity(seedIndex);
        Ped guard = CreatePedFromModelIdentity(identity, spawnPosition, heading);

        if (!Entity.Exists(guard))
        {
            return false;
        }

        WeaponLoadout loadout = CreateHighSecurityEscortLoadout();

        SpawnedNpc spawned = RegisterSpawnedNpc(
            guard,
            NpcBehavior.Bodyguard,
            true,
            false,
            identity,
            loadout.Clone(),
            HighSecurityEscortGuardHealth,
            HighSecurityEscortGuardArmor,
            spawnPosition,
            heading,
            _selectedPatrolRadius,
            false);

        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            DeleteEntitySafe(guard);
            return false;
        }

        ConfigureHighSecurityEscortGuard(spawned, vehicle, seat);
        RegisterHighSecurityEscortNpc(spawned);

        if (!PutPedIntoVehicleSafe(spawned.Ped, vehicle, seat))
        {
            /*
             * Si le ped n'est pas réellement dans le siège demandé, on annule son
             * enregistrement pour éviter une escorte active avec des rôles cassés.
             */
            RemoveHighSecurityEscortNpcRecord(spawned.Ped.Handle, true);
            return false;
        }

        if (seat == -1)
        {
            ConfigureHighSecurityEscortDriver(spawned.Ped);
        }

        GiveHighSecurityEscortWeapons(spawned.Ped);
        CreateOrUpdateNpcBlip(spawned);

        return true;
    }

    private bool HasLiveHighSecurityEscortDriver(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        return Entity.Exists(driver) &&
               !driver.IsDead &&
               IsHighSecurityEscortPedHandle(driver.Handle);
    }

    private ModelIdentity ResolveHighSecurityEscortGuardModelIdentity(int seedIndex)
    {
        /*
         * Demande utilisateur : les hommes du convoi limousine doivent être
         * les mêmes que le Cartel. On réutilise donc exactement le resolveur
         * Cartel, y compris son fallback g_m_m_cartelgoons_01.
         */
        return ResolveCartelGuardModelIdentity();
    }

    private WeaponLoadout CreateHighSecurityEscortLoadout()
    {
        /* Même loadout que Cartel : ServiceCarbine tactique + pistolet mitrailleur en véhicule. */
        return CreateCartelPrimaryLoadout();
    }

    private WeaponHash ResolveHighSecurityEscortPrimaryWeapon()
    {
        return WeaponHash.ServiceCarbine;
    }

    private void GiveHighSecurityEscortWeapons(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        /*
         * Même armement que Cartel. La sélection arme est rappelée juste après
         * pour éviter qu'un passager garde le fusil long en drive-by.
         */
        GiveCartelWeapons(ped);

        try
        {
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
        }
    }

    private void ConfigureHighSecurityEscortGuard(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)
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
        spawned.Ped.IsPersistent = true;
        spawned.Ped.BlockPermanentEvents = true;
        spawned.Ped.AlwaysKeepTask = true;
        spawned.Ped.CanSwitchWeapons = true;

        try
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, spawned.Ped.Handle, true, true);
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, spawned.Ped.Handle, _allyGroupHash);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, spawned.Ped.Handle, 2);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, spawned.Ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 0, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 5, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawned.Ped.Handle, 46, true);
            Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, spawned.Ped.Handle, false);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, spawned.Ped.Handle, false);
            Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, spawned.Ped.Handle, true);
        }
        catch
        {
        }

        int initialMaintenanceStagger = CalculateCartelHandleStaggerMs(
            spawned.Ped.Handle,
            HighSecurityEscortGuardPassiveMaintenanceJitterMs);

        spawned.NextThinkAt = Game.GameTime + initialMaintenanceStagger;
        spawned.NextBodyguardTaskAt = Game.GameTime + initialMaintenanceStagger;

        _highSecurityEscortNextGuardPassiveMaintenanceAt[spawned.Ped.Handle] = Game.GameTime + initialMaintenanceStagger;
        _highSecurityEscortNextGuardMobilityOrderAt[spawned.Ped.Handle] = Game.GameTime + initialMaintenanceStagger;
    }

    private void RegisterHighSecurityEscortNpc(SpawnedNpc spawned)
    {
        if (spawned == null || !Entity.Exists(spawned.Ped))
        {
            return;
        }

        int handle = spawned.Ped.Handle;
        _highSecurityEscortNpcHandles.Add(handle);
        _highSecurityEscortKnownNpcHandles.Add(handle);
        _highSecurityEscortNextPedOrderAt[handle] = 0;
        _highSecurityEscortNextCombatOrderAt[handle] = 0;
        _highSecurityEscortNextGuardPassiveMaintenanceAt[handle] = 0;
        _highSecurityEscortNextGuardMobilityOrderAt[handle] = 0;
    }

    private void ConfigureHighSecurityEscortVehicle(Vehicle vehicle, bool limousine)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int handle = vehicle.Handle;

        /*
         * Différenciation utile :
         * - limousine : suspension moins agressive pour rester stable et ne pas racler ;
         * - Baller : suspension plus ferme pour suivre le convoi.
         *
         * Le blindage reste maximal pour les deux types.
         */
        int suspensionMod = limousine ? 2 : 3;
        int armorMod = 4;
        float vehicleHealth = 1000.0f;

        if (!_highSecurityEscortFullyUpgradedVehicleHandles.Contains(handle))
        {
            _highSecurityEscortFullyUpgradedVehicleHandles.Add(handle);

            try
            {
                vehicle.IsPersistent = true;
                vehicle.Repair();
                vehicle.EngineHealth = vehicleHealth;
                vehicle.BodyHealth = vehicleHealth;
                vehicle.PetrolTankHealth = vehicleHealth;

                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
                Function.Call(Hash.SET_VEHICLE_MOD_KIT, vehicle.Handle, 0);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 11, 3, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 12, 2, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 13, 2, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 15, suspensionMod, false);
                Function.Call(Hash.SET_VEHICLE_MOD, vehicle.Handle, 16, armorMod, false);
                Function.Call(Hash.TOGGLE_VEHICLE_MOD, vehicle.Handle, 18, true);
                Function.Call(Hash.SET_VEHICLE_COLOURS, vehicle.Handle, 0, 0);
                Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, vehicle.Handle, 0, 0);
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, vehicle.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, vehicle.Handle, false);
                Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, vehicle.Handle, vehicleHealth);
                Function.Call(Hash.SET_VEHICLE_PETROL_TANK_HEALTH, vehicle.Handle, vehicleHealth);
                Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, vehicle.Handle, 0.0f);
                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            }
            catch (Exception ex)
            {
                LogException("ConfigureHighSecurityEscortVehicle", ex);
            }

            return;
        }

        MaintainHighSecurityEscortVehicleSoftState(vehicle);
    }

    private void MaintainHighSecurityEscortVehicleSoftState(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int lastAt;

        if (_highSecurityEscortLastVehicleSoftMaintenanceAt.TryGetValue(vehicle.Handle, out lastAt) &&
            Game.GameTime - lastAt < 2500)
        {
            return;
        }

        _highSecurityEscortLastVehicleSoftMaintenanceAt[vehicle.Handle] = Game.GameTime;

        try
        {
            vehicle.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, vehicle.Handle, false);
        }
        catch
        {
        }
    }

    private Vector3 FindHighSecurityEscortVehicleSpawnPosition(Ped player, int seedIndex)
    {
        Vector3 roadPoint;

        if (TryFindHiddenRoadPointNearPlayer(
            player,
            seedIndex + 91,
            HighSecurityEscortSpawnMinDistance,
            HighSecurityEscortSpawnMaxDistance,
            out roadPoint))
        {
            return roadPoint;
        }

        Vector3 playerPos = Entity.Exists(player) ? player.Position : Vector3.Zero;
        Vector3 camForward = GetGameplayCameraForwardVector();

        if (camForward.Length() < 0.001f && Entity.Exists(player))
        {
            camForward = Normalize(player.ForwardVector);
        }

        if (camForward.Length() < 0.001f)
        {
            camForward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 baseDirection = -camForward;
        Vector3 right = Normalize(new Vector3(baseDirection.Y, -baseDirection.X, 0.0f));
        Vector3 fallback = playerPos + baseDirection * (HighSecurityEscortSpawnMinDistance + seedIndex * 9.0f) + right * (((seedIndex % 5) - 2) * 8.0f);
        Vector3 safe = World.GetSafeCoordForPed(fallback, false, 16);

        if (!IsZeroVector(safe) && (!Entity.Exists(player) || !IsPointInPlayerView(player, safe)))
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

    private HighSecurityEscortConvoySpawnSlot[] BuildHighSecurityEscortConvoySpawnLayout(Ped player)
    {
        int slotCount = HighSecurityEscortBallerCount + 1;
        HighSecurityEscortConvoySpawnSlot[] slots = new HighSecurityEscortConvoySpawnSlot[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            slots[i] = new HighSecurityEscortConvoySpawnSlot();
        }

        if (!Entity.Exists(player))
        {
            return slots;
        }

        Vector3 baseRoadPoint;
        Vector3 travelDirection;
        float heading;

        if (TryFindHighSecurityEscortConvoyRoadLine(player, out baseRoadPoint, out travelDirection, out heading))
        {
            for (int i = 0; i < slotCount; i++)
            {
                Vector3 desired = baseRoadPoint - travelDirection * (HighSecurityEscortConvoyLineSpawnSpacing * i);
                Vector3 snapped;

                if (TryResolveHighSecurityEscortRoadSlot(desired, travelDirection, i, HighSecurityEscortSpawnLineMaxNodeSnapDistance, out snapped))
                {
                    desired = snapped;
                }

                slots[i].Position = desired + new Vector3(0.0f, 0.0f, 0.45f);
                slots[i].Heading = heading;
            }

            return slots;
        }

        /*
         * Dernier filet de sécurité : on garde le fallback existant, mais on
         * resnappe chaque véhicule sur route et on conserve une file, pas une
         * apparition éparpillée dans des ruelles. Ce cas devient rare car le
         * scoring ci-dessus teste beaucoup plus de points qu'avant.
         */
        Vector3 fallbackBase = FindHighSecurityEscortVehicleSpawnPosition(player, 0);
        Vector3 fallbackDirection = Normalize(player.Position - fallbackBase);

        if (fallbackDirection.Length() < 0.001f)
        {
            fallbackDirection = DirectionFromHeading(player.Heading);
        }

        if (fallbackDirection.Length() < 0.001f)
        {
            fallbackDirection = new Vector3(0.0f, 1.0f, 0.0f);
        }

        heading = HeadingFromDirection(fallbackDirection);

        for (int i = 0; i < slotCount; i++)
        {
            Vector3 desired = fallbackBase - fallbackDirection * (HighSecurityEscortConvoyLineSpawnSpacing * i);
            Vector3 snapped;

            if (TryResolveHighSecurityEscortRoadSlot(desired, fallbackDirection, i, 24.0f, out snapped))
            {
                desired = snapped + new Vector3(0.0f, 0.0f, 0.45f);
            }

            slots[i].Position = desired;
            slots[i].Heading = heading;
        }

        return slots;
    }

    private bool TryFindHighSecurityEscortConvoyRoadLine(Ped player, out Vector3 baseRoadPoint, out Vector3 travelDirection, out float heading)
    {
        baseRoadPoint = Vector3.Zero;
        travelDirection = Vector3.Zero;
        heading = 0.0f;

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
            0.0f, -18.0f, 18.0f, -34.0f, 34.0f, -54.0f, 54.0f,
            -78.0f, 78.0f, -108.0f, 108.0f, -142.0f, 142.0f, 180.0f
        };

        bool found = false;
        float bestScore = -9999.0f;
        Vector3 bestPoint = Vector3.Zero;
        Vector3 bestDirection = Vector3.Zero;

        for (int attempt = 0; attempt < HighSecurityEscortMajorRoadSearchAttempts; attempt++)
        {
            float t = attempt / (float)Math.Max(1, HighSecurityEscortMajorRoadSearchAttempts - 1);
            float distance = HighSecurityEscortSpawnMinDistance + (HighSecurityEscortSpawnMaxDistance - HighSecurityEscortSpawnMinDistance) * t;
            float angle = angleOffsets[(attempt * 5 + 3) % angleOffsets.Length] + attempt * 7.0f;
            Vector3 probeDirection = RotateDirection2D(hiddenBase, angle);
            Vector3 probeRight = Normalize(new Vector3(probeDirection.Y, -probeDirection.X, 0.0f));
            float lateral = (((attempt + 1) % 5) - 2) * 8.0f;
            Vector3 desired = playerPos + probeDirection * distance + probeRight * lateral;

            for (int nth = 0; nth < HighSecurityEscortMajorRoadNodeProbeCount; nth++)
            {
                Vector3 candidate;

                if (!TryGetClosestVehicleNode(desired, nth, out candidate))
                {
                    continue;
                }

                float actualDistance = candidate.DistanceTo(playerPos);

                if (actualDistance < HighSecurityEscortSpawnMinDistance * 0.78f ||
                    actualDistance > HighSecurityEscortSpawnMaxDistance * 1.42f ||
                    Math.Abs(candidate.Z - playerPos.Z) > 22.0f)
                {
                    continue;
                }

                if (IsPointInPlayerView(player, candidate))
                {
                    continue;
                }

                Vector3 candidateDirection;
                float candidateScore;

                if (!TryBuildHighSecurityEscortRoadLineFromCandidate(
                    player,
                    candidate,
                    true,
                    out candidateDirection,
                    out candidateScore))
                {
                    continue;
                }

                /*
                 * Plus le score est haut, plus la route est large, la file est
                 * régulière et le point reste loin / hors champ du joueur.
                 */
                candidateScore += ClampFloat((actualDistance - HighSecurityEscortSpawnMinDistance) * 0.035f, 0.0f, 4.0f);

                if (!found || candidateScore > bestScore)
                {
                    found = true;
                    bestScore = candidateScore;
                    bestPoint = candidate;
                    bestDirection = candidateDirection;
                }
            }
        }

        if (!found || bestScore < HighSecurityEscortMajorRoadMinimumLineScore)
        {
            return false;
        }

        baseRoadPoint = bestPoint;
        travelDirection = bestDirection;
        heading = HeadingFromDirection(bestDirection);
        return true;
    }

    private bool TryBuildHighSecurityEscortRoadLineFromCandidate(
        Ped player,
        Vector3 candidate,
        bool requireHidden,
        out Vector3 lineDirection,
        out float score)
    {
        lineDirection = Vector3.Zero;
        score = -9999.0f;

        if (!Entity.Exists(player))
        {
            return false;
        }

        Vector3 towardPlayer = Normalize(player.Position - candidate);

        if (towardPlayer.Length() < 0.001f)
        {
            towardPlayer = DirectionFromHeading(player.Heading);
        }

        if (towardPlayer.Length() < 0.001f)
        {
            towardPlayer = new Vector3(0.0f, 1.0f, 0.0f);
        }

        float[] directionAngles =
        {
            0.0f, -32.0f, 32.0f, 72.0f, -72.0f
        };

        bool found = false;
        float bestScore = -9999.0f;
        Vector3 bestDirection = Vector3.Zero;

        for (int i = 0; i < directionAngles.Length; i++)
        {
            Vector3 direction = RotateDirection2D(towardPlayer, directionAngles[i]);

            if (direction.Length() < 0.001f)
            {
                continue;
            }

            if ((direction.X * towardPlayer.X + direction.Y * towardPlayer.Y) < 0.0f)
            {
                direction = -direction;
            }

            float lineScore;

            if (!TryScoreHighSecurityEscortRoadLine(player, candidate, direction, requireHidden, out lineScore))
            {
                continue;
            }

            if (!found || lineScore > bestScore)
            {
                found = true;
                bestScore = lineScore;
                bestDirection = direction;
            }
        }

        if (!found)
        {
            return false;
        }

        lineDirection = bestDirection;
        score = bestScore;
        return true;
    }

    private bool TryScoreHighSecurityEscortRoadLine(
        Ped player,
        Vector3 baseRoadPoint,
        Vector3 direction,
        bool requireHidden,
        out float score)
    {
        score = 0.0f;

        if (!Entity.Exists(player) || direction.Length() < 0.001f)
        {
            return false;
        }

        Vector3 previousSlot = Vector3.Zero;
        int slotCount = HighSecurityEscortBallerCount + 1;

        for (int slot = 0; slot < slotCount; slot++)
        {
            Vector3 desired = baseRoadPoint - direction * (HighSecurityEscortConvoyLineSpawnSpacing * slot);
            Vector3 snapped;

            if (!TryResolveHighSecurityEscortRoadSlot(
                desired,
                direction,
                slot,
                HighSecurityEscortSpawnLineMaxNodeSnapDistance,
                out snapped))
            {
                return false;
            }

            if (requireHidden)
            {
                if (IsPointInPlayerView(player, snapped) ||
                    snapped.DistanceTo(player.Position) < HighSecurityEscortSpawnMinDistance * 0.68f)
                {
                    return false;
                }
            }

            if (slot > 0 && previousSlot.DistanceTo(snapped) < HighSecurityEscortSpawnLineMinSlotDistance)
            {
                return false;
            }

            float snapPenalty = snapped.DistanceTo(desired) * 0.28f;
            float clearanceScore = ScoreHighSecurityEscortRoadClearance(snapped, direction);

            if (slot == 0 && clearanceScore < -1.5f)
            {
                return false;
            }

            if (slot > 0 && clearanceScore < -3.25f)
            {
                return false;
            }

            score += 8.0f + clearanceScore - snapPenalty;
            previousSlot = snapped;
        }

        return score >= 0.0f;
    }

    private bool TryResolveHighSecurityEscortRoadSlot(Vector3 desired, Vector3 direction, int slotIndex, float maxSnapDistance, out Vector3 snapped)
    {
        snapped = Vector3.Zero;

        int[] nthCandidates = { 0 };
        bool found = false;
        float bestScore = -9999.0f;
        Vector3 best = Vector3.Zero;

        for (int i = 0; i < nthCandidates.Length; i++)
        {
            Vector3 node;

            if (!TryGetClosestVehicleNode(desired, nthCandidates[i], out node))
            {
                continue;
            }

            float snapDistance = node.DistanceTo(desired);

            if (snapDistance > maxSnapDistance)
            {
                continue;
            }

            float score = ScoreHighSecurityEscortRoadClearance(node, direction) - snapDistance * 0.45f - i * 0.15f;

            if (!found || score > bestScore)
            {
                found = true;
                bestScore = score;
                best = node;
            }
        }

        if (!found)
        {
            return false;
        }

        snapped = best;
        return true;
    }

    private float ScoreHighSecurityEscortRoadClearance(Vector3 position, Vector3 direction)
    {
        /*
         * Version performante : surtout pas de Raycast ici.
         * Cette fonction est appelée pendant le scoring de spawn et, avant le
         * correctif, elle déclenchait 6 raycasts par slot candidat. Dans GTA V,
         * ces raycasts sur Map/Objects/Vegetation peuvent bloquer le thread du
         * jeu quand on les répète en rafale.
         *
         * On garde donc une estimation "route large / route continue" via les
         * vehicle nodes uniquement. C'est beaucoup moins cher et suffisant pour
         * éviter les ruelles : une bonne route a des nodes proches devant,
         * derrière, et souvent plusieurs nodes voisins au centre.
         */
        Vector3 forward = Normalize(new Vector3(direction.X, direction.Y, 0.0f));

        if (forward.Length() < 0.001f)
        {
            return 0.0f;
        }

        Vector3 right = Normalize(new Vector3(forward.Y, -forward.X, 0.0f));
        float score = 0.0f;
        Vector3 node;

        score += ScoreHighSecurityEscortNodeProbe(position + forward * 18.0f, 0, 12.0f, 3.2f, -3.4f);
        score += ScoreHighSecurityEscortNodeProbe(position - forward * 14.0f, 0, 12.0f, 2.4f, -2.6f);
        score += ScoreHighSecurityEscortNodeProbe(position, 1, 10.0f, 1.8f, -0.7f);
        score += ScoreHighSecurityEscortNodeProbe(position + right * 7.5f, 0, 13.0f, 1.2f, -0.2f);
        score += ScoreHighSecurityEscortNodeProbe(position - right * 7.5f, 0, 13.0f, 1.2f, -0.2f);

        if (TryGetClosestVehicleNode(position, 2, out node) && node.DistanceTo(position) <= 18.0f)
        {
            score += 1.1f;
        }

        return score;
    }

    private float ScoreHighSecurityEscortNodeProbe(Vector3 probe, int nth, float goodDistance, float goodScore, float badScore)
    {
        Vector3 node;

        if (!TryGetClosestVehicleNode(probe, nth, out node))
        {
            return badScore;
        }

        float distance = node.DistanceTo(probe);

        if (distance <= goodDistance)
        {
            return goodScore;
        }

        if (distance <= goodDistance * 1.75f)
        {
            return goodScore * 0.35f;
        }

        return badScore;
    }

    private static float HeadingFromDirection(Vector3 direction)
    {
        Vector3 normalized = Normalize(new Vector3(direction.X, direction.Y, 0.0f));

        if (normalized.Length() < 0.001f)
        {
            return 0.0f;
        }

        return NormalizeHeading((float)(Math.Atan2(normalized.X, normalized.Y) * 180.0 / Math.PI));
    }

    private int FindHighSecurityEscortPlayerSeat(Vehicle limousine)
    {
        if (!Entity.Exists(limousine))
        {
            return 1;
        }

        int passengerCount = Math.Max(0, GetVehicleSeatCapacityIncludingDriver(limousine) - 1);
        int[] preferredSeats = { 1, 2, 3, 0 };

        for (int i = 0; i < preferredSeats.Length; i++)
        {
            int seat = preferredSeats[i];

            if (seat >= 0 && seat < passengerCount)
            {
                return seat;
            }
        }

        return 0;
    }

    private List<int> BuildHighSecurityEscortLimousineGuardSeats(Vehicle limousine, int playerSeat)
    {
        List<int> seats = new List<int>();

        /*
         * Le chauffeur est toujours créé en premier. Ensuite on privilégie une
         * place haute/arrière libre pour le garde "tourelle", puis les autres
         * places passager sans jamais prendre le siège réservé au joueur.
         */
        seats.Add(-1);

        int passengerCount = Entity.Exists(limousine) ? Math.Max(0, GetVehicleSeatCapacityIncludingDriver(limousine) - 1) : 3;
        int[] preferred = { 2, 0, 3, 4, 5, 6, 7, 1 };

        for (int i = 0; i < preferred.Length && seats.Count < HighSecurityEscortLimousineGuardCount; i++)
        {
            int seat = preferred[i];

            if (seat == playerSeat || seat < 0 || seat >= passengerCount)
            {
                continue;
            }

            seats.Add(seat);
        }

        return seats;
    }

    private bool IsHighSecurityEscortSeatSupported(Vehicle vehicle, int seat)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        if (seat == -1)
        {
            return true;
        }

        int passengerCount = Math.Max(0, GetVehicleSeatCapacityIncludingDriver(vehicle) - 1);
        return seat >= 0 && seat < passengerCount;
    }

    private void UpdateHighSecurityEscortState(Ped player)
    {
        try
        {
            if (_highSecurityEscortDismissing)
            {
                UpdateHighSecurityEscortDismissing(player);
            }

            if (!Entity.Exists(player))
            {
                return;
            }

            if (player.IsDead)
            {
                if ((_highSecurityEscortActive || _highSecurityEscortDismissing) && !_highSecurityEscortPlayerDeathDismissed)
                {
                    _highSecurityEscortPlayerDeathDismissed = true;
                    DismissHighSecurityEscort(false);
                    ShowStatus("Escorte haute sécurité : le joueur est mort, l'équipe se replie.", 4500);
                }

                return;
            }

            _highSecurityEscortPlayerDeathDismissed = false;
            CleanupHighSecurityEscortHandleSets();

            if (!_highSecurityEscortActive)
            {
                return;
            }

            if (!EnsureHighSecurityEscortLimousineAvailable())
            {
                return;
            }

            AssistPlayerEnterHighSecurityLimousine(player);
            HandleHighSecurityEscortRouteValidationInput(player);
            HandleHighSecurityEscortRushInput(player);
            HandleHighSecurityEscortImmediateStopInput(player);
            DrawHighSecurityEscortRuntimeOverlay(player);

            if (Game.GameTime < _nextHighSecurityEscortThinkAt)
            {
                return;
            }

            _nextHighSecurityEscortThinkAt = Game.GameTime + HighSecurityEscortThinkIntervalMs;

            EnsureRelationshipGroups();
            MaintainHighSecurityEscortTeam(player);

            Ped threat = ResolveHighSecurityEscortThreat(player);

            if (Entity.Exists(threat))
            {
                EngageHighSecurityEscortThreat(threat, player);
                CleanupHighSecurityEscortHandleSets();
                return;
            }

            bool playerInLimousine = IsPlayerInHighSecurityEscortLimousine(player);

            if (playerInLimousine)
            {
                ReturnHighSecurityEscortGuardsToVehicles(true);

                if (_highSecurityEscortDestinationActive)
                {
                    _highSecurityEscortMode = HighSecurityEscortModeConvoyRoute;
                    UpdateHighSecurityEscortRoute(player);
                }
                else
                {
                    _highSecurityEscortMode = HighSecurityEscortModeStandby;
                    UpdateHighSecurityEscortStandby(player);
                }
            }
            else if (player.IsInVehicle() && Entity.Exists(player.CurrentVehicle))
            {
                _highSecurityEscortMode = HighSecurityEscortModePlayerVehicleFollow;
                UpdateHighSecurityEscortPlayerVehicleFollow(player);
            }
            else
            {
                _highSecurityEscortMode = HighSecurityEscortModeFootFollow;
                UpdateHighSecurityEscortFootFollow(player);
            }

            CleanupHighSecurityEscortHandleSets();
        }
        catch (Exception ex)
        {
            LogException("UpdateHighSecurityEscortState", ex);
        }
    }

    private bool EnsureHighSecurityEscortLimousineAvailable()
    {
        if (!_highSecurityEscortActive)
        {
            return true;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (Entity.Exists(limousine) && IsVehicleDriveable(limousine))
        {
            return true;
        }

        HandleHighSecurityEscortLimousineUnavailable();
        return false;
    }

    private void HandleHighSecurityEscortLimousineUnavailable()
    {
        _highSecurityEscortDestinationActive = false;
        _highSecurityEscortDestination = Vector3.Zero;
        _highSecurityEscortRushMode = false;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortDepartureHoldUntil = 0;
        _nextHighSecurityEscortThinkAt = 0;

        if (!_highSecurityEscortLimousineLostAnnounced)
        {
            _highSecurityEscortLimousineLostAnnounced = true;
            ShowStatus("Escorte haute sécurité : limousine perdue ou détruite, repli du convoi.", 5000);
            LogWarning("HighSecurityEscort", "Limousine indisponible pendant une escorte active. Repli forcé du convoi.");
        }

        DismissHighSecurityEscort(false);
    }

    private void AssistPlayerEnterHighSecurityLimousine(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead || player.IsInVehicle())
        {
            _highSecurityEscortRouteKeyLatch = false;
            _highSecurityEscortEnterKeyLatch = false;
            return;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            _highSecurityEscortEnterKeyLatch = false;
            return;
        }

        float distance = player.Position.DistanceTo(limousine.Position);

        if (distance > HighSecurityEscortLimousineEntryAssistDistance)
        {
            _highSecurityEscortEnterKeyLatch = false;
            return;
        }

        /*
         * Important : près de la limousine on bloque l'entrée vanilla de GTA.
         * Sans ça, la touche F peut lancer en parallèle un car-jack / entrée
         * conducteur si le chauffeur a bougé une frame, puis notre TASK_ENTER
         * passager arrive trop tard. Ici F devient explicitement "monter à
         * l'arrière de la limousine".
         */
        DisableHighSecurityEscortDefaultVehicleEntryControl();

        if (_highSecurityEscortPickupParked)
        {
            StopHighSecurityEscortParkedLimousine(limousine);
        }

        MaintainHighSecurityEscortLimousineCabin(player, true);
        PrepareHighSecurityLimousineForPlayerEntry(limousine, player);

        bool fPressed = IsHighSecurityEscortEnterVehiclePressed();

        if (!fPressed)
        {
            _highSecurityEscortEnterKeyLatch = false;
            return;
        }

        if (_highSecurityEscortEnterKeyLatch)
        {
            return;
        }

        _highSecurityEscortEnterKeyLatch = true;

        int seat = _highSecurityEscortPlayerSeat;

        if (!IsHighSecurityEscortSeatSupported(limousine, seat) || !IsSeatFreeSafe(limousine, seat))
        {
            seat = FindFreeHighSecurityEscortPassengerSeat(limousine);
        }

        if (seat == 999)
        {
            ShowStatus("Escorte haute sécurité : aucune place arrière libre dans la limousine.", 3000);
            return;
        }

        _highSecurityEscortPlayerSeat = seat;

        try
        {
            Function.Call(Hash.CLEAR_PED_TASKS, player.Handle);
        }
        catch
        {
        }

        try
        {
            Function.Call(
                Hash.TASK_ENTER_VEHICLE,
                player.Handle,
                limousine.Handle,
                10000,
                seat,
                1.15f,
                1,
                0);
        }
        catch
        {
        }
    }

    private void DisableHighSecurityEscortDefaultVehicleEntryControl()
    {
        try
        {
            Function.Call((Hash)NativeDisableControlAction, 0, HighSecurityEscortEnterVehicleControl, true);
        }
        catch
        {
        }
    }

    private bool IsHighSecurityEscortEnterVehiclePressed()
    {
        bool disabledControlPressed = false;

        try
        {
            disabledControlPressed = Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, HighSecurityEscortEnterVehicleControl);
        }
        catch
        {
            disabledControlPressed = false;
        }

        return disabledControlPressed || Game.IsKeyPressed(Keys.F);
    }

    private void PrepareHighSecurityLimousineForPlayerEntry(Vehicle limousine, Ped player)
    {
        if (!Entity.Exists(limousine))
        {
            return;
        }

        try
        {
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, limousine.Handle, 1);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, limousine.Handle, true, true, false);
        }
        catch
        {
        }

        /*
         * Si un garde de la limousine s'est retrouvé par erreur sur le siège
         * réservé au joueur, on le remet sur sa place assignée avant de lancer
         * l'entrée du joueur. Cela évite le fallback GTA vers le siège conducteur.
         */
        ReleaseHighSecurityEscortPlayerSeatIfOccupiedByLimoGuard(limousine, player);
    }

    private void ReleaseHighSecurityEscortPlayerSeatIfOccupiedByLimoGuard(Vehicle limousine, Ped player)
    {
        if (!Entity.Exists(limousine) || !IsHighSecurityEscortSeatSupported(limousine, _highSecurityEscortPlayerSeat))
        {
            return;
        }

        int occupantHandle;

        if (!TryGetHighSecurityEscortSeatOccupantHandle(limousine, _highSecurityEscortPlayerSeat, out occupantHandle))
        {
            return;
        }

        if (occupantHandle == 0 || (Entity.Exists(player) && occupantHandle == player.Handle))
        {
            return;
        }

        SpawnedNpc guard = FindHighSecurityEscortNpcRecordByHandle(occupantHandle);

        if (guard == null || !Entity.Exists(guard.Ped) || !IsHighSecurityEscortNpcAssignedToLimousine(guard))
        {
            return;
        }

        int reassignedSeat = guard.BodyguardAssignedSeat;

        if (reassignedSeat == _highSecurityEscortPlayerSeat ||
            !IsHighSecurityEscortSeatSupported(limousine, reassignedSeat) ||
            (!IsSeatFreeSafe(limousine, reassignedSeat) && !IsPedOccupyingVehicleSeat(guard.Ped, limousine, reassignedSeat)))
        {
            reassignedSeat = FindFreeHighSecurityEscortSeatForGuard(limousine, false);
        }

        if (reassignedSeat == 999 || reassignedSeat == _highSecurityEscortPlayerSeat)
        {
            return;
        }

        guard.BodyguardAssignedSeat = reassignedSeat;
        guard.BodyguardIsDriver = reassignedSeat == -1;
        PutPedIntoVehicleSafe(guard.Ped, limousine, reassignedSeat);
    }

    private int FindFreeHighSecurityEscortPassengerSeat(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        /*
         * Priorité stricte aux places arrière. Le siège 0 (passager avant) n'est
         * utilisé qu'en dernier recours afin que la limousine reste un taxi VIP,
         * pas un véhicule que le joueur vole ou conduit.
         */
        int[] preferredSeats =
        {
            _highSecurityEscortPlayerSeat,
            2,
            1,
            3,
            4,
            5,
            6,
            7,
            0
        };

        HashSet<int> checkedSeats = new HashSet<int>();

        for (int i = 0; i < preferredSeats.Length; i++)
        {
            int seat = preferredSeats[i];

            if (seat < 0 || checkedSeats.Contains(seat))
            {
                continue;
            }

            checkedSeats.Add(seat);

            if (IsHighSecurityEscortSeatSupported(vehicle, seat) && IsSeatFreeSafe(vehicle, seat))
            {
                return seat;
            }
        }

        int passengerCount = Math.Max(0, GetVehicleSeatCapacityIncludingDriver(vehicle) - 1);

        for (int seat = 0; seat < passengerCount; seat++)
        {
            if (checkedSeats.Contains(seat))
            {
                continue;
            }

            if (IsSeatFreeSafe(vehicle, seat))
            {
                return seat;
            }
        }

        return 999;
    }

    private void HandleHighSecurityEscortRouteValidationInput(Ped player)
    {
        if (!Entity.Exists(player) || !IsPlayerInHighSecurityEscortLimousine(player))
        {
            if (!Game.IsKeyPressed(Keys.L))
            {
                _highSecurityEscortRouteKeyLatch = false;
                _highSecurityEscortLCommandConsumedUntilRelease = false;
            }

            return;
        }

        bool pressed = Game.IsKeyPressed(Keys.L);

        if (!pressed)
        {
            _highSecurityEscortRouteKeyLatch = false;
            _highSecurityEscortLCommandConsumedUntilRelease = false;
            return;
        }

        if (_highSecurityEscortRouteKeyLatch ||
            _highSecurityEscortLCommandConsumedUntilRelease ||
            IsPlayerPhoneOpen(player))
        {
            return;
        }

        _highSecurityEscortRouteKeyLatch = true;
        _highSecurityEscortLCommandConsumedUntilRelease = true;

        Vector3 destination;

        if (!TryGetHighSecurityEscortWaypoint(out destination))
        {
            ShowStatus("Escorte haute sécurité : pose d'abord un marqueur sur la carte, puis appuie sur L dans la limousine.", 5000);
            return;
        }

        StartHighSecurityEscortRoute(destination);
    }

    private void HandleHighSecurityEscortRushInput(Ped player)
    {
        if (!Entity.Exists(player) ||
            !IsPlayerInHighSecurityEscortLimousine(player) ||
            !_highSecurityEscortDestinationActive)
        {
            if (!Game.IsKeyPressed(Keys.Space))
            {
                _highSecurityEscortRushKeyLatch = false;
            }

            return;
        }

        bool pressed = Game.IsKeyPressed(Keys.Space);

        if (!pressed)
        {
            _highSecurityEscortRushKeyLatch = false;
            return;
        }

        if (_highSecurityEscortRushKeyLatch || IsPlayerPhoneOpen(player))
        {
            return;
        }

        _highSecurityEscortRushKeyLatch = true;
        _highSecurityEscortRushMode = !_highSecurityEscortRushMode;
        ResetHighSecurityEscortVehicleOrderCache();
        _nextHighSecurityEscortThinkAt = 0;

        if (!_highSecurityEscortRoutePaused)
        {
            OrderHighSecurityConvoyToDestination(true);
        }

        ShowStatus(
            _highSecurityEscortRushMode
                ? "Escorte haute sécurité : mode urgence activé, le chauffeur se dépêche."
                : "Escorte haute sécurité : conduite normale réactivée, le chauffeur respecte le trafic.",
            4200);
    }

    private void HandleHighSecurityEscortImmediateStopInput(Ped player)
    {
        if (!Entity.Exists(player) || !IsPlayerInHighSecurityEscortLimousine(player))
        {
            if (!Game.IsKeyPressed(Keys.E))
            {
                _highSecurityEscortStopKeyLatch = false;
            }

            return;
        }

        bool pressed = Game.IsKeyPressed(Keys.E);

        if (!pressed)
        {
            _highSecurityEscortStopKeyLatch = false;
            return;
        }

        if (_highSecurityEscortStopKeyLatch || IsPlayerPhoneOpen(player))
        {
            return;
        }

        _highSecurityEscortStopKeyLatch = true;

        if (!_highSecurityEscortDestinationActive)
        {
            StopHighSecurityEscortConvoyImmediately(true);
            _highSecurityEscortPickupParked = true;
            _highSecurityEscortDepartureHoldUntil = 0;
            ShowStatus("Escorte haute sécurité : convoi arrêté sur place.", 3600);
            return;
        }

        _highSecurityEscortRoutePaused = !_highSecurityEscortRoutePaused;
        ResetHighSecurityEscortVehicleOrderCache();
        _nextHighSecurityEscortThinkAt = 0;

        if (_highSecurityEscortRoutePaused)
        {
            StopHighSecurityEscortConvoyImmediately(true);
            ShowStatus("Escorte haute sécurité : trajet en pause, convoi arrêté immédiatement.", 4200);
            return;
        }

        _highSecurityEscortDepartureHoldUntil = Game.GameTime + HighSecurityEscortDepartureReadyTimeoutMs;
        ReturnHighSecurityEscortGuardsToVehicles(true);
        ShowStatus("Escorte haute sécurité : reprise du trajet, les 4x4 se remettent en convoi.", 4200);
    }

    private void ResetHighSecurityEscortVehicleOrderCache()
    {
        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            int handle = vehicleHandles[i];
            _highSecurityEscortNextVehicleOrderAt[handle] = 0;
            _highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;
            _highSecurityEscortCachedFormationTargets.Remove(handle);
            _highSecurityEscortCachedFormationTargetUntil.Remove(handle);
        }
    }

    private bool TryGetHighSecurityEscortWaypoint(out Vector3 destination)
    {
        destination = Vector3.Zero;

        try
        {
            int blip = Function.Call<int>((Hash)NativeGetFirstBlipInfoId, HighSecurityWaypointBlipSprite);

            if (blip == 0 || !Function.Call<bool>((Hash)NativeDoesBlipExist, blip))
            {
                return false;
            }

            destination = Function.Call<Vector3>((Hash)NativeGetBlipCoords, blip);

            if (IsZeroVector(destination))
            {
                return false;
            }

            float ground = World.GetGroundHeight(new Vector3(destination.X, destination.Y, destination.Z + 1000.0f));

            if (Math.Abs(ground) > 0.001f)
            {
                destination.Z = ground + 0.45f;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartHighSecurityEscortRoute(Vector3 destination)
    {
        _highSecurityEscortDestination = destination;
        _highSecurityEscortDestinationActive = true;
        _highSecurityEscortRushMode = false;
        _highSecurityEscortRushKeyLatch = false;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortEmergencyFleeDestination = Vector3.Zero;
        _highSecurityEscortEmergencyFleeUntil = 0;
        _highSecurityEscortDepartureHoldUntil = Game.GameTime + HighSecurityEscortDepartureReadyTimeoutMs;
        _highSecurityEscortMode = HighSecurityEscortModeConvoyRoute;
        _nextHighSecurityEscortThinkAt = 0;

        ReturnHighSecurityEscortGuardsToVehicles(true);

        Ped player = Game.Player.Character;

        if (PrepareHighSecurityEscortConvoyDeparture(player, true))
        {
            OrderHighSecurityConvoyToDestination(true);
        }

        ShowStatus(
            "Escorte haute sécurité : destination validée, départ du convoi blindé.",
            4500);
    }

    private bool IsPlayerInHighSecurityEscortLimousine(Ped player)
    {
        if (!Entity.Exists(player) || !player.IsInVehicle())
        {
            return false;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !Entity.Exists(player.CurrentVehicle))
        {
            return false;
        }

        if (player.CurrentVehicle.Handle != limousine.Handle)
        {
            return false;
        }

        return !IsPedDriverOfVehicle(player, limousine);
    }

    private bool IsHighSecurityEscortLimousineVehicle(Vehicle vehicle)
    {
        return Entity.Exists(vehicle) && vehicle.Handle == _highSecurityEscortLimousineHandle;
    }

    private bool IsHighSecurityEscortLimousineVehicleHandle(int vehicleHandle)
    {
        return vehicleHandle != 0 && vehicleHandle == _highSecurityEscortLimousineHandle;
    }

    private bool IsHighSecurityEscortNpcAssignedToLimousine(SpawnedNpc npc)
    {
        return npc != null && IsHighSecurityEscortLimousineVehicleHandle(npc.BodyguardAssignedVehicleHandle);
    }

    private bool TryGetHighSecurityEscortSeatOccupantHandle(Vehicle vehicle, int seat, out int pedHandle)
    {
        pedHandle = 0;

        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            pedHandle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle.Handle, seat, false);
            return pedHandle != 0;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPedOccupyingVehicleSeat(Ped ped, Vehicle vehicle, int seat)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        int occupantHandle;

        if (!TryGetHighSecurityEscortSeatOccupantHandle(vehicle, seat, out occupantHandle))
        {
            return false;
        }

        return occupantHandle == ped.Handle;
    }

    private void MaintainHighSecurityEscortLimousineCabin(Ped player, bool force)
    {
        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return;
        }

        ConfigureHighSecurityEscortVehicle(limousine, true);

        bool combatActive = IsHighSecurityEscortCombatActive();
        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || !IsHighSecurityEscortNpcAssignedToLimousine(npc))
            {
                continue;
            }

            /* Je garde les hommes de la limousine dans la cabine, même pendant une embuscade. */
            MaintainHighSecurityEscortGuardPassiveState(npc, true);

            if (npc.BodyguardAssignedSeat == _highSecurityEscortPlayerSeat)
            {
                int fixedSeat = FindFreeHighSecurityEscortSeatForGuard(limousine, false);

                if (fixedSeat != 999 && fixedSeat != _highSecurityEscortPlayerSeat)
                {
                    npc.BodyguardAssignedSeat = fixedSeat;
                    npc.BodyguardIsDriver = fixedSeat == -1;
                }
            }

            if (!npc.Ped.IsInVehicle(limousine))
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, limousine, true, true);
                continue;
            }

            if (npc.BodyguardAssignedSeat != 999 &&
                IsHighSecurityEscortSeatSupported(limousine, npc.BodyguardAssignedSeat) &&
                !IsPedOccupyingVehicleSeat(npc.Ped, limousine, npc.BodyguardAssignedSeat) &&
                (IsSeatFreeSafe(limousine, npc.BodyguardAssignedSeat) || npc.BodyguardAssignedSeat == -1))
            {
                PutPedIntoVehicleSafe(npc.Ped, limousine, npc.BodyguardAssignedSeat);
            }

            if (IsPedDriverOfVehicle(npc.Ped, limousine))
            {
                ConfigureHighSecurityEscortDriver(npc.Ped, combatActive);
            }
        }

        ReleaseHighSecurityEscortPlayerSeatIfOccupiedByLimoGuard(limousine, player);
        EnsureHighSecurityEscortVehicleHasDriver(limousine, true);
    }

    private void EnsureHighSecurityEscortVehicleHasDriver(Vehicle vehicle, bool limousine)
    {
        if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (Entity.Exists(driver) && !driver.IsDead)
        {
            ConfigureHighSecurityEscortDriver(driver);
            return;
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc candidate = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (candidate == null || !Entity.Exists(candidate.Ped) || candidate.Ped.IsDead)
            {
                continue;
            }

            if (candidate.BodyguardAssignedVehicleHandle != vehicle.Handle)
            {
                continue;
            }

            if (limousine && candidate.BodyguardAssignedSeat == _highSecurityEscortPlayerSeat)
            {
                continue;
            }

            if (!IsSeatFreeSafe(vehicle, -1))
            {
                return;
            }

            candidate.BodyguardAssignedSeat = -1;
            candidate.BodyguardIsDriver = true;

            if (CanTeleportHighSecurityEscortGuardIntoVehicleWithoutBeingSeen(candidate.Ped, vehicle))
            {
                PutPedIntoVehicleSafe(candidate.Ped, vehicle, -1);
                ConfigureHighSecurityEscortDriver(candidate.Ped);
            }
            else
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(candidate, vehicle, true, false);
            }

            return;
        }
    }

    private void DrawHighSecurityEscortRuntimeOverlay(Ped player)
    {
        if (!Entity.Exists(player) || !_highSecurityEscortActive || _highSecurityEscortMode == HighSecurityEscortModeNone)
        {
            return;
        }

        string line;

        if (IsPlayerInHighSecurityEscortLimousine(player))
        {
            if (_highSecurityEscortDestinationActive)
            {
                float distance = player.Position.DistanceTo(_highSecurityEscortDestination);
                string rushText = _highSecurityEscortRushMode ? "urgence active | Espace : calmer" : "conduite normale | Espace : urgence";
                string stopText = _highSecurityEscortRoutePaused ? "E : reprendre" : "E : arrêt immédiat";
                line = "Escorte haute sécurité : " + (_highSecurityEscortRoutePaused ? "convoi en pause" : "convoi en route") + " | " + rushText + " | " + stopText + " | distance " + distance.ToString("0", CultureInfo.InvariantCulture) + " m";
            }
            else
            {
                line = "Escorte haute sécurité : pose un marqueur puis appuie sur L dans la limousine.";
            }
        }
        else
        {
            Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);
            float distance = Entity.Exists(limousine) ? player.Position.DistanceTo(limousine.Position) : 0.0f;
            line = "Escorte haute sécurité : F près de la limousine pour monter à l'arrière";

            if (Entity.Exists(limousine))
            {
                line += " (" + distance.ToString("0", CultureInfo.InvariantCulture) + " m)";
            }
        }

        int x = 420;
        int y = 708;
        DrawRect(x - 10, y - 6, 820, 34, Color.FromArgb(160, 0, 0, 0));
        DrawText(line, x, y, 0.31f, Color.FromArgb(225, 235, 255), false, true);
    }

    private void MaintainHighSecurityEscortTeam(Ped player)
    {
        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            bool isLimousine = vehicle.Handle == _highSecurityEscortLimousineHandle;
            ConfigureHighSecurityEscortVehicle(vehicle, isLimousine);
            EnsureHighSecurityEscortVehicleHasDriver(vehicle, isLimousine);

            if (!isLimousine && _highSecurityEscortDestinationActive)
            {
                Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

                if (TryRecoverLostHighSecurityEscortVehicleBehindLimousine(vehicle, limousine, player, GetHighSecurityEscortVehicleRole(vehicle.Handle), false))
                {
                    continue;
                }
            }

            RescueHighSecurityEscortVehicleIfNeeded(vehicle, player, i);
        }

        MaintainHighSecurityEscortLimousineCabin(player, false);

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            MaintainHighSecurityEscortGuard(npc);
        }
    }

    private void MaintainHighSecurityEscortGuard(SpawnedNpc npc)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        if (ShouldRunHighSecurityEscortGuardPassiveMaintenance(npc.Ped, false))
        {
            MaintainHighSecurityEscortGuardPassiveState(npc, true);
        }
    }

    private void MaintainHighSecurityEscortGuardPassiveState(SpawnedNpc npc, bool includeWeaponSelection)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
        {
            return;
        }

        npc.BaseBehavior = NpcBehavior.Bodyguard;
        npc.Behavior = NpcBehavior.Bodyguard;
        npc.Ped.IsEnemy = false;
        npc.Ped.IsPersistent = true;
        npc.Ped.BlockPermanentEvents = true;
        npc.Ped.AlwaysKeepTask = true;

        try
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, npc.Ped.Handle, true, true);
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, npc.Ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, npc.Ped.Handle, 46, true);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, npc.Ped.Handle, false);
            Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, npc.Ped.Handle, true);
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

        if (npc.Ped.IsInVehicle() && IsPedDriverOfAnyHighSecurityEscortVehicle(npc.Ped))
        {
            ConfigureHighSecurityEscortDriver(npc.Ped);
        }
    }

    private bool ShouldRunHighSecurityEscortGuardPassiveMaintenance(Ped ped, bool force)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt =
            Game.GameTime +
            HighSecurityEscortGuardPassiveMaintenanceIntervalMs +
            CalculateCartelHandleStaggerMs(ped.Handle, HighSecurityEscortGuardPassiveMaintenanceJitterMs);

        if (force)
        {
            _highSecurityEscortNextGuardPassiveMaintenanceAt[ped.Handle] = nextAt;
            return true;
        }

        int nextMaintenanceAt;

        if (_highSecurityEscortNextGuardPassiveMaintenanceAt.TryGetValue(ped.Handle, out nextMaintenanceAt) &&
            Game.GameTime < nextMaintenanceAt)
        {
            return false;
        }

        _highSecurityEscortNextGuardPassiveMaintenanceAt[ped.Handle] = nextAt;
        return true;
    }

    private bool IsPedDriverOfAnyHighSecurityEscortVehicle(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        foreach (int handle in _highSecurityEscortVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (Entity.Exists(vehicle) && IsPedDriverOfVehicle(ped, vehicle))
            {
                return true;
            }
        }

        return false;
    }

    private Ped ResolveHighSecurityEscortThreat(Ped player)
    {
        Ped cachedThreat = GetCachedHighSecurityEscortThreat(player);

        if (Entity.Exists(cachedThreat))
        {
            return cachedThreat;
        }

        if (Game.GameTime < _nextHighSecurityEscortThreatScanAt)
        {
            return null;
        }

        _nextHighSecurityEscortThreatScanAt = Game.GameTime + HighSecurityEscortThreatScanIntervalMs;

        Ped scannedThreat = FindBestHighSecurityEscortThreat(player);

        if (Entity.Exists(scannedThreat))
        {
            CacheHighSecurityEscortThreat(scannedThreat);
        }

        return scannedThreat;
    }

    private Ped GetCachedHighSecurityEscortThreat(Ped player)
    {
        if (!Entity.Exists(player))
        {
            ClearCachedHighSecurityEscortThreat();
            return null;
        }

        if (_highSecurityEscortCachedThreatPed == null)
        {
            return null;
        }

        if (Game.GameTime > _highSecurityEscortCachedThreatUntil)
        {
            ClearCachedHighSecurityEscortThreat();
            return null;
        }

        if (!Entity.Exists(_highSecurityEscortCachedThreatPed) ||
            _highSecurityEscortCachedThreatPed.IsDead ||
            !IsValidHighSecurityEscortThreatCandidate(_highSecurityEscortCachedThreatPed, player))
        {
            ClearCachedHighSecurityEscortThreat();
            return null;
        }

        if (_highSecurityEscortCachedThreatPed.Position.DistanceTo(player.Position) > HighSecurityEscortThreatScanRadius + 120.0f)
        {
            ClearCachedHighSecurityEscortThreat();
            return null;
        }

        return _highSecurityEscortCachedThreatPed;
    }

    private void CacheHighSecurityEscortThreat(Ped threat)
    {
        if (!Entity.Exists(threat) || threat.IsDead)
        {
            ClearCachedHighSecurityEscortThreat();
            return;
        }

        _highSecurityEscortCachedThreatPed = threat;
        _highSecurityEscortCachedThreatUntil = Game.GameTime + HighSecurityEscortThreatCacheLifetimeMs;
    }

    private void ClearCachedHighSecurityEscortThreat()
    {
        _highSecurityEscortCachedThreatPed = null;
        _highSecurityEscortCachedThreatUntil = 0;
        _highSecurityEscortLastThreatRelationshipHandle = 0;
        _highSecurityEscortLastThreatRelationshipAt = 0;
    }

    private Ped FindBestHighSecurityEscortThreat(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return null;
        }

        Ped bestThreat = null;
        float bestScore = float.MaxValue;

        /*
         * 1) Menaces créées par le mod : même priorité que le Cartel.
         * Les PNJ alliés/neutres sont exclus avant toute promotion.
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

            if (!IsValidHighSecurityEscortThreatCandidate(candidateNpc.Ped, player))
            {
                continue;
            }

            float score = ScoreHighSecurityEscortThreat(candidateNpc.Ped, player);

            if (score < bestScore)
            {
                bestScore = score;
                bestThreat = candidateNpc.Ped;
            }
        }

        /*
         * 2) PNJ proches du joueur : un tir seul ne suffit pas. Il faut une
         * preuve de menace contre le joueur ou un garde du convoi, comme Cartel.
         */
        Ped[] nearbyPlayerPeds = GetNearbyPedsSafe(player, HighSecurityEscortThreatScanRadius);

        for (int i = 0; i < nearbyPlayerPeds.Length; i++)
        {
            Ped candidate = nearbyPlayerPeds[i];

            if (!IsValidHighSecurityEscortThreatCandidate(candidate, player))
            {
                continue;
            }

            if (!HasHighSecurityEscortThreatEvidence(candidate, player))
            {
                continue;
            }

            float score = ScoreHighSecurityEscortThreat(candidate, player);

            if (score < bestScore)
            {
                bestScore = score;
                bestThreat = candidate;
            }
        }

        /*
         * 3) PNJ proches des gardes : scan par tranches, pas tous les gardes
         * à chaque tick, pour garder le mode L fluide même avec 20+ PNJ.
         */
        List<int> escortNpcHandles = new List<int>(_highSecurityEscortNpcHandles);

        if (escortNpcHandles.Count > 0)
        {
            int scansThisPass = Math.Min(HighSecurityEscortMaxGuardThreatScansPerPass, escortNpcHandles.Count);

            for (int scan = 0; scan < scansThisPass; scan++)
            {
                int handleIndex = Wrap(_highSecurityEscortGuardThreatScanCursor + scan, escortNpcHandles.Count);
                SpawnedNpc guard = FindHighSecurityEscortNpcRecordByHandle(escortNpcHandles[handleIndex]);

                if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
                {
                    continue;
                }

                Ped[] nearbyGuardPeds = GetNearbyPedsSafe(guard.Ped, HighSecurityEscortThreatScanRadius);

                for (int j = 0; j < nearbyGuardPeds.Length; j++)
                {
                    Ped candidate = nearbyGuardPeds[j];

                    if (!IsValidHighSecurityEscortThreatCandidate(candidate, player))
                    {
                        continue;
                    }

                    if (!HasHighSecurityEscortThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player))
                    {
                        continue;
                    }

                    float score = ScoreHighSecurityEscortThreat(candidate, player);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestThreat = candidate;
                    }
                }
            }

            _highSecurityEscortGuardThreatScanCursor = Wrap(_highSecurityEscortGuardThreatScanCursor + scansThisPass, escortNpcHandles.Count);
        }

        return bestThreat;
    }

    private bool IsValidHighSecurityEscortThreatCandidate(Ped candidate, Ped player)
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

        /* Jamais les alliés gérés par le mod, donc jamais les gardes du convoi eux-mêmes. */
        if (IsManagedAlly(candidate))
        {
            return false;
        }

        if (_highSecurityEscortKnownNpcHandles.Contains(candidate.Handle))
        {
            return false;
        }

        if (_cartelNpcHandles.Contains(candidate.Handle) || _cartelDismissingNpcRecords.ContainsKey(candidate.Handle))
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

    private bool HasHighSecurityEscortThreatEvidence(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return false;
        }

        if (HasDefensiveDamageAgainstProtectedPed(candidate, player))
        {
            return true;
        }

        if (IsPedInCombatWith(candidate, player))
        {
            return true;
        }

        if (IsPedShooting(candidate) &&
            candidate.Position.DistanceTo(player.Position) <= HighSecurityEscortThreatEvidenceRadius &&
            HasHostileRelationshipToProtectedPed(candidate, player))
        {
            return true;
        }

        List<int> escortNpcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < escortNpcHandles.Count; i++)
        {
            SpawnedNpc guard = FindHighSecurityEscortNpcRecordByHandle(escortNpcHandles[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            if (candidate.Position.DistanceTo(guard.Ped.Position) > HighSecurityEscortThreatEvidenceRadius)
            {
                continue;
            }

            if (HasHighSecurityEscortThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasHighSecurityEscortThreatEvidenceAgainstSpecificGuard(Ped candidate, Ped guard, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(guard))
        {
            return false;
        }

        if (candidate.Position.DistanceTo(guard.Position) > HighSecurityEscortThreatEvidenceRadius)
        {
            return false;
        }

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

    private float ScoreHighSecurityEscortThreat(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return float.MaxValue;
        }

        float best = candidate.Position.DistanceTo(player.Position);
        List<int> escortNpcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < escortNpcHandles.Count; i++)
        {
            SpawnedNpc guard = FindHighSecurityEscortNpcRecordByHandle(escortNpcHandles[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            best = Math.Min(best, candidate.Position.DistanceTo(guard.Ped.Position));
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

    private void MakeHighSecurityEscortAlliesHostileToThreat(Ped threat)
    {
        if (!Entity.Exists(threat))
        {
            return;
        }

        if (_highSecurityEscortLastThreatRelationshipHandle == threat.Handle &&
            Game.GameTime - _highSecurityEscortLastThreatRelationshipAt < HighSecurityEscortThreatRelationshipRefreshMs)
        {
            return;
        }

        _highSecurityEscortLastThreatRelationshipHandle = threat.Handle;
        _highSecurityEscortLastThreatRelationshipAt = Game.GameTime;

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
        }
    }

    private bool EnsureHighSecurityEscortCombatEscapeRoute(Ped player, Vehicle limousine)
    {
        if (!Entity.Exists(player) || !Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return false;
        }

        if (_highSecurityEscortDestinationActive)
        {
            bool wasPaused = _highSecurityEscortRoutePaused;
            _highSecurityEscortRoutePaused = false;
            _highSecurityEscortPickupParked = false;

            if (wasPaused)
            {
                ResetHighSecurityEscortVehicleOrderCache();
            }

            return true;
        }

        if (Game.GameTime <= _highSecurityEscortEmergencyFleeUntil &&
            _highSecurityEscortEmergencyFleeDestination.Length() > 0.001f)
        {
            ActivateHighSecurityEscortEmergencyFleeDestination(_highSecurityEscortEmergencyFleeDestination);
            return true;
        }

        Vector3 escapeDestination;

        if (!TryCalculateHighSecurityEscortEmergencyFleeDestination(limousine, player, out escapeDestination))
        {
            return false;
        }

        _highSecurityEscortEmergencyFleeDestination = escapeDestination;
        _highSecurityEscortEmergencyFleeUntil = Game.GameTime + HighSecurityEscortEmergencyFleeCacheMs;
        ActivateHighSecurityEscortEmergencyFleeDestination(escapeDestination);
        return true;
    }

    private void ActivateHighSecurityEscortEmergencyFleeDestination(Vector3 destination)
    {
        _highSecurityEscortDestination = destination;
        _highSecurityEscortDestinationActive = true;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortDepartureHoldUntil = 0;
        _highSecurityEscortMode = HighSecurityEscortModeConvoyRoute;
        _nextHighSecurityEscortThinkAt = 0;
        ResetHighSecurityEscortVehicleOrderCache();
    }

    private bool TryCalculateHighSecurityEscortEmergencyFleeDestination(Vehicle limousine, Ped player, out Vector3 target)
    {
        target = Vector3.Zero;

        if (!Entity.Exists(limousine))
        {
            return false;
        }

        Vector3 forward = Normalize(limousine.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(limousine.Heading);
        }

        if (forward.Length() < 0.001f && Entity.Exists(player))
        {
            forward = Normalize(player.ForwardVector);
        }

        if (forward.Length() < 0.001f)
        {
            forward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 origin = limousine.Position;
        float[] distances =
        {
            HighSecurityEscortEmergencyFleeDistance * 0.72f,
            HighSecurityEscortEmergencyFleeDistance,
            HighSecurityEscortEmergencyFleeDistance * 1.25f
        };
        float[] angles =
        {
            0.0f, -24.0f, 24.0f, -46.0f, 46.0f
        };

        bool found = false;
        float bestScore = -9999.0f;
        Vector3 bestTarget = Vector3.Zero;
        int checkedCandidates = 0;

        for (int d = 0; d < distances.Length && checkedCandidates < HighSecurityEscortEmergencyFleeMaxCandidateChecks; d++)
        {
            for (int a = 0; a < angles.Length && checkedCandidates < HighSecurityEscortEmergencyFleeMaxCandidateChecks; a++)
            {
                checkedCandidates++;
                Vector3 direction = RotateDirection2D(forward, angles[a]);
                Vector3 probe = origin + direction * distances[d];
                Vector3 roadPoint;

                if (!TryGetClosestVehicleNode(probe, 0, out roadPoint))
                {
                    continue;
                }

                float snapDistance = roadPoint.DistanceTo(probe);

                if (snapDistance > HighSecurityEscortRuntimeRoadSnapDistance * 2.0f)
                {
                    continue;
                }

                float distanceFromLimo = roadPoint.DistanceTo(origin);

                if (distanceFromLimo < 65.0f)
                {
                    continue;
                }

                float alignment = direction.X * forward.X + direction.Y * forward.Y;
                float score =
                    ScoreHighSecurityEscortRoadClearance(roadPoint, direction) +
                    ClampFloat(alignment, -0.2f, 1.0f) * 7.0f +
                    distanceFromLimo * 0.025f -
                    snapDistance * 0.35f -
                    a * 0.22f;

                if (!found || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestTarget = roadPoint;
                }
            }
        }

        if (found)
        {
            target = bestTarget + new Vector3(0.0f, 0.0f, 0.35f);
            return true;
        }

        target = origin + forward * HighSecurityEscortEmergencyFleeDistance;

        Vector3 fallbackRoad;

        if (TryGetClosestVehicleNode(target, 0, out fallbackRoad) &&
            fallbackRoad.DistanceTo(target) <= HighSecurityEscortRuntimeRoadSnapDistance * 2.5f)
        {
            target = fallbackRoad + new Vector3(0.0f, 0.0f, 0.35f);
        }

        return true;
    }

    private void EngageHighSecurityEscortThreat(Ped threat, Ped player)
    {
        if (!Entity.Exists(threat) || threat.IsDead || !Entity.Exists(player))
        {
            return;
        }

        MarkHighSecurityEscortCombatActive();
        MakeHighSecurityEscortAlliesHostileToThreat(threat);

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);
        bool playerInLimousine = Entity.Exists(limousine) && IsPlayerInHighSecurityEscortLimousine(player);
        bool playerInLimousineRoute = playerInLimousine && EnsureHighSecurityEscortCombatEscapeRoute(player, limousine);
        List<int> activeVehicles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(activeVehicles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            ConfigureHighSecurityEscortVehicle(vehicle, vehicle.Handle == _highSecurityEscortLimousineHandle);

            /*
             * En embuscade, le convoi ne doit pas abandonner la route pour foncer
             * bêtement sur l'assaillant. La limousine continue vers la destination,
             * mais avec un style plus nerveux et sans attendre les feux. Les Baller
             * se replacent autour d'elle avec plus de vitesse de rattrapage.
             */
            if (playerInLimousineRoute && Entity.Exists(limousine))
            {
                if (vehicle.Handle == limousine.Handle)
                {
                    IssueHighSecurityLimousineDriveOrder(limousine, _highSecurityEscortDestination, false, true);
                }
                else
                {
                    int role = GetHighSecurityEscortVehicleRole(vehicle.Handle);
                    IssueHighSecurityFormationDriveOrder(vehicle, limousine, role, false, true);
                }

                continue;
            }

            CommandHighSecurityEscortVehicleForCombat(vehicle, threat, player);
        }

        List<int> activeNpcs = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < activeNpcs.Count; i++)
        {
            SpawnedNpc guard = FindHighSecurityEscortNpcRecordByHandle(activeNpcs[i]);

            if (guard == null || !Entity.Exists(guard.Ped) || guard.Ped.IsDead)
            {
                continue;
            }

            EngageHighSecurityEscortGuardThreat(guard, threat, player);
        }
    }

    private void EngageHighSecurityEscortGuardThreat(SpawnedNpc guard, Ped threat, Ped player)
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

        if (IsHighSecurityEscortLimousineTurretGuard(guard.Ped))
        {
            Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

            if (Entity.Exists(limousine) && !guard.Ped.IsInVehicle(limousine))
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(guard, limousine, true, true);
                return;
            }
        }

        Vehicle assignedLimousine = FindVehicleByHandle(guard.BodyguardAssignedVehicleHandle);

        if (IsHighSecurityEscortLimousineVehicle(assignedLimousine) &&
            !guard.Ped.IsInVehicle(assignedLimousine))
        {
            CommandHighSecurityEscortGuardEnterAssignedVehicle(guard, assignedLimousine, true, true);
            return;
        }

        if (ShouldHighSecurityEscortGuardReturnToVehicleDuringCombat(guard, threat, player))
        {
            ReturnHighSecurityEscortGuardsToVehicles(false);
            return;
        }

        if (!player.IsInVehicle() && !guard.Ped.IsInVehicle())
        {
            Vehicle assignedVehicle = FindVehicleByHandle(guard.BodyguardAssignedVehicleHandle);

            if (ShouldHighSecurityEscortGuardReturnToVehicleWhilePlayerOnFoot(guard, assignedVehicle, player) &&
                !IsImmediateHighSecurityEscortThreatForGuard(guard.Ped, threat))
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(
                    guard,
                    assignedVehicle,
                    IsHighSecurityEscortLimousineVehicle(assignedVehicle),
                    IsHighSecurityEscortLimousineVehicle(assignedVehicle));
                return;
            }
        }

        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle currentVehicle = guard.Ped.CurrentVehicle;

            if (!IsHighSecurityEscortLimousineTurretGuard(guard.Ped) &&
                !player.IsInVehicle() &&
                ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(guard.Ped, currentVehicle, player, true))
            {
                CommandHighSecurityEscortGuardLeaveVehicle(guard, currentVehicle, true);
                return;
            }
        }

        if (!CanIssueHighSecurityEscortCombatOrder(guard.Ped))
        {
            return;
        }

        PrepareHighSecurityEscortGuardForCombat(guard, threat);

        if (guard.Ped.IsInVehicle() && Entity.Exists(guard.Ped.CurrentVehicle))
        {
            Vehicle vehicle = guard.Ped.CurrentVehicle;

            if (!IsHighSecurityEscortLimousineTurretGuard(guard.Ped) &&
                !player.IsInVehicle() &&
                ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(guard.Ped, vehicle, player, true))
            {
                CommandHighSecurityEscortGuardLeaveVehicle(guard, vehicle, true);
                return;
            }

            if (IsPedDriverOfVehicle(guard.Ped, vehicle))
            {
                CommandHighSecurityEscortVehicleForCombat(vehicle, threat, player);
                return;
            }

            if (ShouldHighSecurityEscortPassengerExitToFight(guard.Ped, vehicle, threat, player))
            {
                MarkHighSecurityEscortGuardCombatFootLock(guard.Ped);
                CommandHighSecurityEscortGuardLeaveVehicle(guard, vehicle, true);
                return;
            }

            StartHighSecurityEscortPassengerDriveBy(guard.Ped, threat);
            return;
        }

        StartHighSecurityEscortOnFootCombat(guard.Ped, threat);
    }

    private void PrepareHighSecurityEscortGuardForCombat(SpawnedNpc guard, Ped threat)
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

        bool turretGuard = IsHighSecurityEscortLimousineTurretGuard(guard.Ped);

        guard.Ped.IsEnemy = false;
        guard.Ped.BlockPermanentEvents = turretGuard;
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
            Function.Call(Hash.SET_PED_FIRING_PATTERN, guard.Ped.Handle, HighSecurityEscortFullAutoFiringPattern);

            if (turretGuard)
            {
                Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, guard.Ped.Handle, false);
                Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, guard.Ped.Handle, true);
            }
        }
        catch
        {
        }

        MakeHighSecurityEscortAlliesHostileToThreat(threat);
    }

    private bool CanIssueHighSecurityEscortCombatOrder(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt;

        if (_highSecurityEscortNextCombatOrderAt.TryGetValue(ped.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        _highSecurityEscortNextCombatOrderAt[ped.Handle] = Game.GameTime + HighSecurityEscortCombatOrderIntervalMs;
        return true;
    }

    private bool ShouldHighSecurityEscortGuardReturnToVehicleDuringCombat(SpawnedNpc guard, Ped threat, Ped player)
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

        if (IsHighSecurityEscortGuardCombatFootLocked(guard.Ped))
        {
            return false;
        }

        float threatDistance = guard.Ped.Position.DistanceTo(threat.Position);

        if (threatDistance <= CartelGuardImmediateThreatDistance ||
            CanPedSeeEntity(guard.Ped, threat, 55.0f))
        {
            return false;
        }

        return true;
    }

    private bool ShouldHighSecurityEscortPassengerExitToFight(Ped passenger, Vehicle vehicle, Ped threat, Ped player)
    {
        if (!Entity.Exists(passenger) ||
            !Entity.Exists(vehicle) ||
            !Entity.Exists(threat) ||
            !Entity.Exists(player))
        {
            return false;
        }

        if (IsPedDriverOfVehicle(passenger, vehicle))
        {
            return false;
        }

        if (IsHighSecurityEscortLimousineTurretGuard(passenger))
        {
            return false;
        }

        bool isLimousine = IsHighSecurityEscortLimousineVehicle(vehicle);

        if (isLimousine)
        {
            return false;
        }

        bool vehicleBlocked = IsHighSecurityEscortVehicleBlockedOrStuckForCombat(vehicle);
        float threatToVehicle = threat.Position.DistanceTo(vehicle.Position);
        float threatToPlayer = threat.Position.DistanceTo(player.Position);

        if (!player.IsInVehicle())
        {
            return ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(passenger, vehicle, player, true);
        }

        bool closeThreat = threatToVehicle <= HighSecurityEscortLimoGuardExitThreatDistance ||
                           threatToPlayer <= HighSecurityEscortLimoGuardExitThreatDistance;
        bool blockedThreat = vehicleBlocked &&
                             (threatToVehicle <= HighSecurityEscortBlockedLimoGuardExitThreatDistance ||
                              threatToPlayer <= HighSecurityEscortBlockedLimoGuardExitThreatDistance);

        if (!blockedThreat)
        {
            return false;
        }

        if (vehicle.Speed > 4.2f && !vehicleBlocked)
        {
            return false;
        }

        if (!closeThreat && !blockedThreat)
        {
            return false;
        }

        if (!CanPedSeeEntity(passenger, threat, HighSecurityEscortBlockedLimoGuardExitThreatDistance + 18.0f) && !vehicleBlocked)
        {
            return false;
        }

        return true;
    }

    private bool ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(Ped guard, Vehicle vehicle, Ped player, bool combatMode)
    {
        if (!Entity.Exists(guard) || !Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        if (player.IsInVehicle())
        {
            return false;
        }

        if (IsHighSecurityEscortLimousineTurretGuard(guard))
        {
            return false;
        }

        if (IsHighSecurityEscortLimousineVehicle(vehicle))
        {
            return false;
        }

        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

        if (!IsVehicleDriveable(vehicle))
        {
            return distanceToPlayer <= HighSecurityEscortOnFootReturnToVehicleDistance + 10.0f;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return distanceToPlayer <= HighSecurityEscortPassengerCombatFootExitDistance;
        }

        float allowedDistance = combatMode ? HighSecurityEscortPassengerCombatFootExitDistance : HighSecurityEscortPassengerFootExitDistance;
        float allowedSpeed = combatMode ? 3.8f : 2.6f;

        return distanceToPlayer <= allowedDistance && vehicle.Speed <= allowedSpeed;
    }

    private bool IsImmediateHighSecurityEscortThreatForGuard(Ped guard, Ped threat)
    {
        if (!Entity.Exists(guard) || !Entity.Exists(threat) || threat.IsDead)
        {
            return false;
        }

        return guard.Position.DistanceTo(threat.Position) <= CartelGuardImmediateThreatDistance &&
               CanPedSeeEntity(guard, threat, 55.0f);
    }

    private void StartHighSecurityEscortPassengerDriveBy(Ped passenger, Ped threat)
    {
        if (!Entity.Exists(passenger) || !Entity.Exists(threat) || threat.IsDead)
        {
            return;
        }

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

        if (IsHighSecurityEscortLimousineTurretGuard(passenger) && StartHighSecurityEscortLimousineTurretFire(passenger, threat))
        {
            RecordJusticeAllyPoliceEngagement(passenger, threat, true);
            return;
        }

        bool offensiveOrderIssued = false;
        try
        {
            Function.Call(
                Hash.TASK_DRIVE_BY,
                passenger.Handle,
                threat.Handle,
                0,
                0.0f,
                0.0f,
                0.0f,
                HighSecurityEscortDriveByDistance,
                90,
                true,
                HighSecurityEscortFullAutoFiringPattern);
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

    private bool IsHighSecurityEscortLimousineTurretGuard(Ped ped)
    {
        return Entity.Exists(ped) && _highSecurityEscortLimousineTurretGuardHandle != 0 && ped.Handle == _highSecurityEscortLimousineTurretGuardHandle;
    }

    private bool StartHighSecurityEscortLimousineTurretFire(Ped passenger, Ped threat)
    {
        if (!Entity.Exists(passenger) || !Entity.Exists(threat) || threat.IsDead)
        {
            return false;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !passenger.IsInVehicle(limousine))
        {
            return false;
        }

        try
        {
            Function.Call((Hash)NativeTaskVehicleShootAtPed, passenger.Handle, threat.Handle, 25.0f);
            return true;
        }
        catch
        {
        }

        try
        {
            Function.Call(
                Hash.TASK_DRIVE_BY,
                passenger.Handle,
                threat.Handle,
                0,
                0.0f,
                0.0f,
                0.0f,
                HighSecurityEscortDriveByDistance + 22.0f,
                100,
                true,
                HighSecurityEscortFullAutoFiringPattern);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartHighSecurityEscortOnFootCombat(Ped guard, Ped threat)
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
            Function.Call(Hash.TASK_COMBAT_PED, guard.Handle, threat.Handle, 0, 16);
            offensiveOrderIssued = true;

            if (guard.Position.DistanceTo(threat.Position) <= HighSecurityEscortOnFootShootDistance &&
                CanPedSeeEntity(guard, threat, HighSecurityEscortOnFootShootDistance))
            {
                Function.Call(
                    Hash.TASK_SHOOT_AT_ENTITY,
                    guard.Handle,
                    threat.Handle,
                    1800,
                    HighSecurityEscortFullAutoFiringPattern);
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

    private void CommandHighSecurityEscortVehicleForCombat(Vehicle vehicle, Ped threat, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(threat) || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        if (_highSecurityEscortPickupParked &&
            IsHighSecurityEscortLimousineVehicle(vehicle) &&
            (!Entity.Exists(player) || !IsPlayerInHighSecurityEscortLimousine(player)))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return;
        }

        if (IsHighSecurityEscortVehicleInSoftRecovery(vehicle) && !IsHighSecurityEscortVehicleBlockedOrStuckForCombat(vehicle))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return;
        }

        bool playerOnFoot = Entity.Exists(player) && !player.IsInVehicle();
        Vector3 driveTarget = playerOnFoot && Entity.Exists(player) ? player.Position : threat.Position;

        if (!CanIssueHighSecurityEscortVehicleOrder(vehicle, false))
        {
            return;
        }

        if (ShouldSkipHighSecurityEscortRepeatedVehicleOrder(
            vehicle,
            driveTarget,
            false,
            5.0f,
            120.0f,
            1.2f))
        {
            return;
        }

        ConfigureHighSecurityEscortDriver(driver, true);
        RecordHighSecurityEscortVehicleOrderTarget(vehicle, driveTarget);

        float distanceToTarget = vehicle.Position.DistanceTo(driveTarget);
        float combatDriveSpeed = playerOnFoot
            ? CalculateHighSecurityEscortSmoothApproachSpeed(HighSecurityEscortCombatRouteSpeed, distanceToTarget)
            : ClampFloat(HighSecurityEscortSmoothConvoyDriveSpeed + 5.0f, HighSecurityEscortCombatCloseSpeed, HighSecurityEscortCombatFormationCatchupSpeed);
        float stoppingRange = playerOnFoot ? 14.0f : (distanceToTarget > 45.0f ? 14.0f : 22.0f);
        int style = GetHighSecurityEscortDrivingStyle(true);

        bool offensiveOrderIssued = false;
        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, combatDriveSpeed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                vehicle.Handle,
                driveTarget.X,
                driveTarget.Y,
                driveTarget.Z,
                combatDriveSpeed,
                style,
                stoppingRange);
            offensiveOrderIssued = true;
        }
        catch
        {
        }

        if (offensiveOrderIssued && !playerOnFoot)
        {
            RecordJusticeAllyPoliceEngagement(driver, threat, true);
        }
    }

    private bool PrepareHighSecurityEscortConvoyDeparture(Ped player, bool forceReturn)
    {
        if (!_highSecurityEscortDestinationActive ||
            _highSecurityEscortRoutePaused ||
            IsHighSecurityEscortCombatActive() ||
            !Entity.Exists(player) ||
            !IsPlayerInHighSecurityEscortLimousine(player))
        {
            return true;
        }

        if (_highSecurityEscortDepartureHoldUntil <= 0)
        {
            return true;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return true;
        }

        MaintainHighSecurityEscortLimousineCabin(player, true);
        ReturnHighSecurityEscortGuardsToVehicles(forceReturn);

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            EnsureHighSecurityEscortVehicleHasDriver(vehicle, vehicle.Handle == _highSecurityEscortLimousineHandle);
        }

        if (AreHighSecurityEscortVehiclesReadyForDeparture(player) ||
            Game.GameTime >= _highSecurityEscortDepartureHoldUntil)
        {
            _highSecurityEscortDepartureHoldUntil = 0;
            ResetHighSecurityEscortVehicleOrderCache();
            return true;
        }

        StopHighSecurityEscortConvoyImmediately(true);
        return false;
    }

    private bool AreHighSecurityEscortVehiclesReadyForDeparture(Ped player)
    {
        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            Ped driver = GetDriverOfVehicle(vehicle);

            if (!Entity.Exists(driver) || driver.IsDead)
            {
                return false;
            }
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);

            if (!Entity.Exists(assignedVehicle) || !IsVehicleDriveable(assignedVehicle))
            {
                continue;
            }

            if (IsHighSecurityEscortLimousineVehicle(assignedVehicle))
            {
                if (!npc.Ped.IsInVehicle(assignedVehicle))
                {
                    return false;
                }

                continue;
            }

            if (npc.BodyguardIsDriver && !npc.Ped.IsInVehicle(assignedVehicle))
            {
                return false;
            }

            if (!npc.Ped.IsInVehicle(assignedVehicle) &&
                npc.Ped.Position.DistanceTo(assignedVehicle.Position) <= HighSecurityEscortDepartureGuardReturnDistance)
            {
                return false;
            }

            if (Entity.Exists(player) &&
                !npc.Ped.IsInVehicle(assignedVehicle) &&
                npc.Ped.Position.DistanceTo(player.Position) <= HighSecurityEscortDepartureGuardReturnDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateHighSecurityEscortRoute(Ped player)
    {
        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            HandleHighSecurityEscortLimousineUnavailable();
            return;
        }

        if (_highSecurityEscortRoutePaused && !IsHighSecurityEscortCombatActive())
        {
            StopHighSecurityEscortConvoyImmediately(true);
            return;
        }

        if (!PrepareHighSecurityEscortConvoyDeparture(player, false))
        {
            return;
        }

        float distance = limousine.Position.DistanceTo(_highSecurityEscortDestination);

        if (distance <= HighSecurityEscortDestinationArriveDistance)
        {
            _highSecurityEscortDestinationActive = false;
            _highSecurityEscortRushMode = false;
            _highSecurityEscortRushKeyLatch = false;
            _highSecurityEscortRoutePaused = false;
            _highSecurityEscortPickupParked = true;
            _highSecurityEscortDepartureHoldUntil = 0;
            _highSecurityEscortMode = HighSecurityEscortModeStandby;
            StopHighSecurityEscortConvoyAtDestination();
            ShowStatus("Escorte haute sécurité : destination atteinte.", 4500);
            return;
        }

        OrderHighSecurityConvoyToDestination(false);
    }

    private void OrderHighSecurityConvoyToDestination(bool force)
    {
        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);
        Ped player = Game.Player.Character;

        if (_highSecurityEscortRoutePaused && !IsHighSecurityEscortCombatActive())
        {
            return;
        }

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return;
        }

        IssueHighSecurityLimousineDriveOrder(limousine, _highSecurityEscortDestination, force);

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || vehicle.Handle == limousine.Handle || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            int role = GetHighSecurityEscortVehicleRole(vehicle.Handle);

            if (TryRecoverLostHighSecurityEscortVehicleBehindLimousine(vehicle, limousine, player, role, force))
            {
                continue;
            }

            IssueHighSecurityFormationDriveOrder(vehicle, limousine, role, force);
        }
    }

    private void IssueHighSecurityLimousineDriveOrder(Vehicle limousine, Vector3 target, bool force)
    {
        IssueHighSecurityLimousineDriveOrder(limousine, target, force, IsHighSecurityEscortCombatActive());
    }

    private void IssueHighSecurityLimousineDriveOrder(Vehicle limousine, Vector3 target, bool force, bool combatMode)
    {
        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return;
        }

        if (!force && IsHighSecurityEscortVehicleInSoftRecovery(limousine))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(limousine);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return;
        }

        if (!force && IsHighSecurityEscortVehicleOrderCooldownActive(limousine))
        {
            return;
        }

        target = ResolveHighSecurityEscortDriveTargetOnRoad(limousine, target, combatMode);
        float distance = limousine.Position.DistanceTo(target);

        if (!force && !combatMode && IsHighSecurityEscortVehicleSettledNearTarget(limousine, target, distance <= 40.0f ? 7.0f : 13.0f))
        {
            return;
        }

        if (!CanIssueHighSecurityEscortVehicleOrder(limousine, force))
        {
            return;
        }

        if (ShouldSkipHighSecurityEscortRepeatedVehicleOrder(
            limousine,
            target,
            force,
            combatMode ? 4.0f : (distance <= 45.0f ? 4.5f : 10.0f),
            320.0f,
            combatMode ? 1.1f : 3.0f))
        {
            return;
        }

        ConfigureHighSecurityEscortDriver(driver, combatMode);
        RecordHighSecurityEscortVehicleOrderTarget(limousine, target);

        float speed = CalculateHighSecurityEscortTaxiSpeed(limousine, target, combatMode);
        float stoppingRange = distance <= 35.0f ? 4.0f : (combatMode ? 7.0f : 9.0f);
        int style = GetHighSecurityEscortDrivingStyle(combatMode);

        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, limousine.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, speed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);

            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                limousine.Handle,
                target.X,
                target.Y,
                target.Z,
                speed,
                style,
                stoppingRange);
        }
        catch
        {
        }
    }

    private void IssueHighSecurityFormationDriveOrder(Vehicle vehicle, Vehicle targetVehicle, int role, bool force)
    {
        IssueHighSecurityFormationDriveOrder(vehicle, targetVehicle, role, force, IsHighSecurityEscortCombatActive());
    }

    private void IssueHighSecurityFormationDriveOrder(Vehicle vehicle, Vehicle targetVehicle, int role, bool force, bool combatMode)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(targetVehicle) || !IsVehicleDriveable(vehicle) || !IsVehicleDriveable(targetVehicle))
        {
            return;
        }

        if (!force && IsHighSecurityEscortVehicleInSoftRecovery(vehicle))
        {
            return;
        }

        if (!force && _highSecurityEscortPickupParked && IsHighSecurityEscortLimousineVehicle(vehicle))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return;
        }

        float distanceToTargetVehicle = vehicle.Position.DistanceTo(targetVehicle.Position);
        bool emergencyCatchup = !combatMode && distanceToTargetVehicle >= HighSecurityEscortConvoyHardCatchupGap;

        if (!force && !emergencyCatchup && IsHighSecurityEscortVehicleOrderCooldownActive(vehicle))
        {
            return;
        }

        Vector3 desired = CalculateHighSecurityFormationPosition(targetVehicle, vehicle, role, combatMode);
        float distanceToFormation = vehicle.Position.DistanceTo(desired);

        if (!force && !combatMode && IsHighSecurityEscortVehicleSettledNearTarget(vehicle, desired, 6.5f))
        {
            return;
        }

        bool softCatchup = !combatMode && distanceToTargetVehicle >= HighSecurityEscortConvoySoftCatchupGap;
        bool directCorrection = force || emergencyCatchup || ShouldUseHighSecurityEscortDirectFormationCorrection(
            vehicle,
            targetVehicle,
            role,
            combatMode,
            distanceToFormation,
            distanceToTargetVehicle);

        if (!CanIssueHighSecurityEscortVehicleOrder(vehicle, force || emergencyCatchup))
        {
            return;
        }

        if (ShouldSkipHighSecurityEscortRepeatedVehicleOrder(
            vehicle,
            desired,
            force || emergencyCatchup,
            directCorrection ? (combatMode ? 5.0f : 6.0f) : HighSecurityEscortFormationEscortRetaskTargetShift,
            softCatchup ? 95.0f : 130.0f,
            directCorrection ? (combatMode ? 1.0f : 1.7f) : 2.4f))
        {
            return;
        }

        ConfigureHighSecurityEscortDriver(driver, combatMode);
        RecordHighSecurityEscortVehicleOrderTarget(vehicle, desired);

        float convoySpeed = CalculateHighSecurityEscortFormationDriveSpeed(
            targetVehicle,
            combatMode,
            distanceToFormation,
            distanceToTargetVehicle);
        float escortSpacing = GetHighSecurityEscortFormationSpacing(role, combatMode);
        int style = GetHighSecurityEscortDrivingStyle(combatMode);

        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, convoySpeed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);

            if (directCorrection)
            {
                Function.Call(
                    Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    driver.Handle,
                    vehicle.Handle,
                    desired.X,
                    desired.Y,
                    desired.Z,
                    convoySpeed,
                    style,
                    distanceToFormation > 36.0f ? 7.0f : 4.5f);
                return;
            }

            Function.Call(
                Hash.TASK_VEHICLE_ESCORT,
                driver.Handle,
                vehicle.Handle,
                targetVehicle.Handle,
                -1,
                convoySpeed,
                style,
                escortSpacing,
                0,
                combatMode ? 18.0f : 24.0f);
        }
        catch
        {
        }
    }

    private bool ShouldUseHighSecurityEscortDirectFormationCorrection(
        Vehicle vehicle,
        Vehicle targetVehicle,
        int role,
        bool combatMode,
        float distanceToFormation,
        float distanceToTargetVehicle)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(targetVehicle))
        {
            return false;
        }

        if (distanceToFormation >= HighSecurityEscortFormationHardCorrectionDistance)
        {
            return true;
        }

        if (combatMode && distanceToFormation >= 24.0f)
        {
            return true;
        }

        Vector3 forward = Normalize(targetVehicle.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(targetVehicle.Heading);
        }

        Vector3 relative = vehicle.Position - targetVehicle.Position;
        float ahead = relative.X * forward.X + relative.Y * forward.Y;

        if (ahead > HighSecurityEscortFormationAheadCorrectionDistance)
        {
            return true;
        }

        float expectedBack = GetHighSecurityEscortFormationBackDistance(role, combatMode);
        bool clearlyBehindButTooLoose = distanceToTargetVehicle > expectedBack + HighSecurityEscortFormationDirectCorrectionDistance;

        if (clearlyBehindButTooLoose && distanceToFormation >= HighSecurityEscortFormationDirectCorrectionDistance)
        {
            return true;
        }

        if (vehicle.Speed < 0.9f &&
            distanceToFormation >= HighSecurityEscortFormationDirectCorrectionDistance &&
            targetVehicle.Speed > 4.0f)
        {
            return true;
        }

        return false;
    }

    private float CalculateHighSecurityEscortFormationDriveSpeed(
        Vehicle targetVehicle,
        bool combatMode,
        float distanceToFormation,
        float distanceToTargetVehicle)
    {
        bool rushMode = IsHighSecurityEscortRushModeActive(combatMode);
        float targetSpeed = Entity.Exists(targetVehicle) ? Math.Max(0.0f, targetVehicle.Speed) : 0.0f;
        float maxCatchup = combatMode
            ? HighSecurityEscortCombatFormationCatchupSpeed
            : (rushMode ? HighSecurityEscortRushFormationCatchupSpeed : HighSecurityEscortSmoothFormationCatchupSpeed + 3.5f);
        float baseSpeed = combatMode
            ? HighSecurityEscortSmoothConvoyDriveSpeed + 3.8f
            : (rushMode ? HighSecurityEscortSmoothConvoyDriveSpeed + 4.8f : 10.8f);
        float catchupBonus = combatMode ? 5.8f : (rushMode ? 5.6f : 3.8f);

        if (distanceToTargetVehicle > HighSecurityEscortConvoyHardCatchupGap)
        {
            catchupBonus += rushMode ? 7.5f : 6.0f;
        }
        else if (distanceToTargetVehicle > HighSecurityEscortConvoySoftCatchupGap)
        {
            catchupBonus += rushMode ? 4.8f : 3.2f;
        }
        else if (distanceToFormation > 24.0f)
        {
            catchupBonus += rushMode ? 2.6f : 1.8f;
        }

        float convoySpeed = ClampFloat(targetSpeed + catchupBonus, baseSpeed, maxCatchup);

        if (distanceToFormation <= 10.0f)
        {
            float closeCap = combatMode ? HighSecurityEscortCombatCloseSpeed : (rushMode ? HighSecurityEscortRushCloseSpeed : 8.2f);
            closeCap = Math.Min(maxCatchup, Math.Max(closeCap, targetSpeed + 1.1f));
            return ClampFloat(targetSpeed + (combatMode ? 1.0f : 0.7f), HighSecurityEscortSmoothConvoyCloseDriveSpeed, closeCap);
        }

        if (distanceToFormation <= 24.0f)
        {
            float mediumCap = combatMode ? 15.0f : (rushMode ? 16.5f : 12.4f);
            mediumCap = Math.Min(maxCatchup, Math.Max(mediumCap, targetSpeed + 2.0f));
            return ClampFloat(targetSpeed + (combatMode ? 2.3f : 1.6f), HighSecurityEscortSmoothConvoyCloseDriveSpeed, mediumCap);
        }

        return convoySpeed;
    }

    private Vector3 ResolveHighSecurityEscortDriveTargetOnRoad(Vehicle vehicle, Vector3 target, bool combatMode)
    {
        if (!Entity.Exists(vehicle))
        {
            return target;
        }

        float maxSnap = combatMode ? 34.0f : 58.0f;
        Vector3 roadPoint;

        if (TryGetClosestVehicleNode(target, 0, out roadPoint) && roadPoint.DistanceTo(target) <= maxSnap)
        {
            return roadPoint + new Vector3(0.0f, 0.0f, 0.35f);
        }

        return target;
    }

    private Vector3 CalculateHighSecurityFormationPosition(Vehicle targetVehicle, int role)
    {
        return CalculateHighSecurityFormationPosition(targetVehicle, role, IsHighSecurityEscortCombatActive());
    }

    private Vector3 CalculateHighSecurityFormationPosition(Vehicle targetVehicle, int role, bool combatMode)
    {
        return CalculateHighSecurityFormationPosition(targetVehicle, null, role, combatMode);
    }

    private Vector3 CalculateHighSecurityFormationPosition(Vehicle targetVehicle, Vehicle followerVehicle, int role, bool combatMode)
    {
        if (!Entity.Exists(targetVehicle))
        {
            return Vector3.Zero;
        }

        Vector3 forward = Normalize(targetVehicle.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(targetVehicle.Heading);
        }

        /*
         * File propre derrière la limousine : pas de placement latéral forcé.
         * Les anciennes positions gauche/droite poussaient les SUV à sortir de
         * voie ou à couper par le trottoir dans les rues étroites.
         */
        float backDistance = GetHighSecurityEscortFormationBackDistance(role, combatMode);
        Vector3 desired = targetVehicle.Position - forward * backDistance;

        return ResolveHighSecurityEscortCachedFormationTarget(
            followerVehicle,
            desired,
            combatMode ? 12.0f : 18.0f);
    }

    private Vector3 ResolveHighSecurityEscortCachedFormationTarget(Vehicle followerVehicle, Vector3 desired, float maxSnapDistance)
    {
        if (!Entity.Exists(followerVehicle))
        {
            return SnapHighSecurityEscortConvoyTargetToRoad(desired, maxSnapDistance);
        }

        int handle = followerVehicle.Handle;
        Vector3 cached;
        int cacheUntil;

        if (_highSecurityEscortCachedFormationTargets.TryGetValue(handle, out cached) &&
            _highSecurityEscortCachedFormationTargetUntil.TryGetValue(handle, out cacheUntil) &&
            Game.GameTime < cacheUntil &&
            cached.DistanceTo(desired) <= HighSecurityEscortFormationTargetCacheReuseDistance)
        {
            return cached;
        }

        Vector3 snapped = SnapHighSecurityEscortConvoyTargetToRoad(desired, maxSnapDistance);

        _highSecurityEscortCachedFormationTargets[handle] = snapped;
        _highSecurityEscortCachedFormationTargetUntil[handle] = Game.GameTime + HighSecurityEscortFormationTargetCacheMs;

        return snapped;
    }

    private Vector3 SnapHighSecurityEscortConvoyTargetToRoad(Vector3 desired, float maxSnapDistance)
    {
        Vector3 roadPoint;

        if (TryGetClosestVehicleNode(desired, 0, out roadPoint) && roadPoint.DistanceTo(desired) <= maxSnapDistance)
        {
            return roadPoint + new Vector3(0.0f, 0.0f, 0.35f);
        }

        return desired;
    }

    private float GetHighSecurityEscortFormationSpacing(int role)
    {
        return GetHighSecurityEscortFormationSpacing(role, IsHighSecurityEscortCombatActive());
    }

    private float GetHighSecurityEscortFormationSpacing(int role, bool combatMode)
    {
        return GetHighSecurityEscortFormationBackDistance(role, combatMode);
    }

    private float GetHighSecurityEscortFormationBackDistance(int role, bool combatMode)
    {
        float multiplier = combatMode ? 0.84f : 1.0f;

        switch (role)
        {
            case HighSecurityEscortVehicleRoleFrontLeft:
                return 9.5f * multiplier;

            case HighSecurityEscortVehicleRoleFrontRight:
                return 18.5f * multiplier;

            case HighSecurityEscortVehicleRoleRearLeft:
                return 28.0f * multiplier;

            case HighSecurityEscortVehicleRoleRearRight:
                return 37.5f * multiplier;

            default:
                return 24.0f * multiplier;
        }
    }

    private static Vector3 DirectionFromHeading(float heading)
    {
        float radians = heading * (float)Math.PI / 180.0f;
        return Normalize(new Vector3((float)Math.Sin(radians), (float)Math.Cos(radians), 0.0f));
    }

    private void StopHighSecurityEscortConvoyImmediately(bool keepEnginesOn)
    {
        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            StopHighSecurityEscortVehicleInPlace(vehicle, true, keepEnginesOn);
        }
    }

    private void StopHighSecurityEscortParkedLimousine(Vehicle limousine)
    {
        StopHighSecurityEscortParkedLimousine(limousine, false);
    }

    private void StopHighSecurityEscortParkedLimousine(Vehicle limousine, bool force)
    {
        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return;
        }

        ResetHighSecurityEscortParkedLimousineRecoveryTracking(limousine);

        /*
         * Un ordre de stationnement doit rester prioritaire si la limousine roule.
         * Sinon le cooldown peut laisser le chauffeur continuer quelques secondes.
         */
        bool limousineStillMoving = Math.Abs(limousine.Speed) > 1.0f;
        bool forceStop = force || limousineStillMoving;

        if (!forceStop && IsHighSecurityEscortVehicleOrderCooldownActive(limousine))
        {
            return;
        }

        StopHighSecurityEscortVehicleInPlace(
            limousine,
            forceStop,
            true,
            HighSecurityEscortParkedPickupStopHoldMs);
    }

    private void ResetHighSecurityEscortParkedLimousineRecoveryTracking(Vehicle limousine)
    {
        if (!Entity.Exists(limousine))
        {
            return;
        }

        int handle = limousine.Handle;
        _highSecurityEscortLastVehiclePositions[handle] = limousine.Position;
        _highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;
        _highSecurityEscortVehicleStuckSinceAt[handle] = 0;
        _highSecurityEscortVehicleRecoveryUntil[handle] = 0;
    }

    private void StopHighSecurityEscortVehicleInPlace(Vehicle vehicle, bool force, bool keepEngineOn)
    {
        StopHighSecurityEscortVehicleInPlace(vehicle, force, keepEngineOn, HighSecurityEscortImmediateStopHoldMs);
    }

    private void StopHighSecurityEscortVehicleInPlace(Vehicle vehicle, bool force, bool keepEngineOn, int holdMs)
    {
        if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        if (!force && !CanIssueHighSecurityEscortVehicleOrder(vehicle, false))
        {
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return;
        }

        int handle = vehicle.Handle;
        int effectiveHoldMs = Math.Max(HighSecurityEscortImmediateStopHoldMs, holdMs);
        _highSecurityEscortNextVehicleOrderAt[handle] = Game.GameTime + effectiveHoldMs;
        _highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;
        _highSecurityEscortCachedFormationTargets.Remove(handle);
        _highSecurityEscortCachedFormationTargetUntil.Remove(handle);

        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, keepEngineOn, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, 0.0f);
            Function.Call(Hash.CLEAR_PED_TASKS, driver.Handle);
            Function.Call((Hash)NativeTaskVehicleTempAction, driver.Handle, vehicle.Handle, HighSecurityEscortImmediateBrakeAction, effectiveHoldMs);
            Function.Call(Hash.TASK_STAND_STILL, driver.Handle, effectiveHoldMs);
        }
        catch
        {
        }
    }

    private void StopHighSecurityEscortConvoyAtDestination()
    {
        StopHighSecurityEscortConvoyImmediately(true);
    }

    private void UpdateHighSecurityEscortStandby(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine))
        {
            return;
        }

        if (_highSecurityEscortPickupParked &&
            player.Position.DistanceTo(limousine.Position) <= HighSecurityEscortPickupParkedReturnDistance)
        {
            ReturnHighSecurityEscortGuardsToVehicles(true);
        }

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            if (vehicle.Handle == limousine.Handle)
            {
                if (_highSecurityEscortPickupParked)
                {
                    StopHighSecurityEscortParkedLimousine(limousine);
                    continue;
                }

                ContinueHighSecurityVehicleNearPlayer(vehicle, player, HighSecurityEscortArrivalDriveSpeed, 5.0f, false);
                continue;
            }

            int role = GetHighSecurityEscortVehicleRole(vehicle.Handle);
            IssueHighSecurityFormationDriveOrder(vehicle, limousine, role, false);
        }

        MaybeAnnounceHighSecurityEscortArrival(player);
    }

    private void UpdateHighSecurityEscortPlayerVehicleFollow(Ped player)
    {
        if (!Entity.Exists(player) || !player.IsInVehicle() || !Entity.Exists(player.CurrentVehicle))
        {
            return;
        }

        Vehicle playerVehicle = player.CurrentVehicle;
        ReturnHighSecurityEscortGuardsToVehicles(false);

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || vehicle.Handle == playerVehicle.Handle || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            int role = GetHighSecurityEscortVehicleRole(vehicle.Handle);
            IssueHighSecurityFormationDriveOrder(vehicle, playerVehicle, role, false);
        }
    }

    private void UpdateHighSecurityEscortFootFollow(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        MaintainHighSecurityEscortLimousineCabin(player, false);

        Vehicle parkedLimousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (_highSecurityEscortPickupParked && Entity.Exists(parkedLimousine))
        {
            UpdateHighSecurityEscortParkedPickupFootSupport(player, parkedLimousine);
            return;
        }

        MoveHighSecurityVehiclesNearFootPlayer(player);

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            MaintainHighSecurityEscortGuard(npc);

            Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);
            bool assignedToLimousine = IsHighSecurityEscortLimousineVehicle(assignedVehicle);

            if (npc.Ped.IsInVehicle() && Entity.Exists(npc.Ped.CurrentVehicle))
            {
                Vehicle currentVehicle = npc.Ped.CurrentVehicle;

                if (IsHighSecurityEscortLimousineVehicle(currentVehicle))
                {
                    if (IsPedDriverOfVehicle(npc.Ped, currentVehicle))
                    {
                        ContinueHighSecurityVehicleNearPlayer(currentVehicle, player, HighSecurityEscortSmoothArrivalDriveSpeed, 7.0f, false);
                    }

                    /* Les hommes de la limousine restent à bord, même joueur à pied. */
                    continue;
                }

                if (IsPedDriverOfVehicle(npc.Ped, currentVehicle))
                {
                    ContinueHighSecurityVehicleNearPlayer(currentVehicle, player, HighSecurityEscortSmoothArrivalDriveSpeed, 12.0f, false);
                    continue;
                }

                if (ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(npc.Ped, currentVehicle, player, false))
                {
                    CommandHighSecurityEscortGuardLeaveVehicle(npc, currentVehicle, false);
                }

                continue;
            }

            if (assignedToLimousine)
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, assignedVehicle, true, true);
                continue;
            }

            if (ShouldHighSecurityEscortGuardReturnToVehicleWhilePlayerOnFoot(npc, assignedVehicle, player))
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, assignedVehicle, false, false);
                continue;
            }

            FollowHighSecurityEscortGuardOnFoot(npc, player, false);
        }
    }

    private void UpdateHighSecurityEscortParkedPickupFootSupport(Ped player, Vehicle limousine)
    {
        if (!Entity.Exists(player) || !Entity.Exists(limousine))
        {
            return;
        }

        StopHighSecurityEscortParkedLimousine(limousine);

        bool playerNearLimousine = player.Position.DistanceTo(limousine.Position) <= HighSecurityEscortPickupParkedReturnDistance;

        if (playerNearLimousine)
        {
            ReturnHighSecurityEscortGuardsToVehicles(true);
            return;
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            MaintainHighSecurityEscortGuard(npc);

            Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);
            bool assignedToLimousine = IsHighSecurityEscortLimousineVehicle(assignedVehicle);

            if (assignedToLimousine)
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, assignedVehicle, true, true);
                continue;
            }

            if (npc.BodyguardIsDriver)
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, assignedVehicle, false, false);
                continue;
            }

            if (npc.Ped.IsInVehicle() && Entity.Exists(npc.Ped.CurrentVehicle))
            {
                Vehicle currentVehicle = npc.Ped.CurrentVehicle;

                if (!IsHighSecurityEscortLimousineVehicle(currentVehicle) &&
                    currentVehicle.Position.DistanceTo(player.Position) <= HighSecurityEscortOnFootReturnToVehicleDistance &&
                    currentVehicle.Speed <= 2.8f)
                {
                    CommandHighSecurityEscortGuardLeaveVehicle(npc, currentVehicle, false);
                }

                continue;
            }

            if (ShouldHighSecurityEscortGuardReturnToVehicleWhilePlayerOnFoot(npc, assignedVehicle, player))
            {
                CommandHighSecurityEscortGuardEnterAssignedVehicle(npc, assignedVehicle, false, false);
                continue;
            }

            FollowHighSecurityEscortGuardOnFoot(npc, player, false);
        }
    }

    private bool ShouldHighSecurityEscortGuardReturnToVehicleWhilePlayerOnFoot(SpawnedNpc npc, Vehicle assignedVehicle, Ped player)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(assignedVehicle) || !Entity.Exists(player))
        {
            return false;
        }

        if (!IsVehicleDriveable(assignedVehicle))
        {
            return false;
        }

        if (IsHighSecurityEscortGuardCombatFootLocked(npc.Ped))
        {
            return false;
        }

        if (IsHighSecurityEscortLimousineVehicle(assignedVehicle))
        {
            return true;
        }

        float vehicleDistanceToPlayer = assignedVehicle.Position.DistanceTo(player.Position);
        float guardDistanceToPlayer = npc.Ped.Position.DistanceTo(player.Position);

        if (npc.BodyguardIsDriver)
        {
            return true;
        }

        if (vehicleDistanceToPlayer > HighSecurityEscortOnFootReturnToVehicleDistance)
        {
            return true;
        }

        if (guardDistanceToPlayer > HighSecurityEscortOnFootReturnToVehicleDistance + 8.0f &&
            vehicleDistanceToPlayer > HighSecurityEscortPassengerFootExitDistance)
        {
            return true;
        }

        return false;
    }

    private void MoveHighSecurityVehiclesNearFootPlayer(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        if (_highSecurityEscortPickupParked)
        {
            Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

            if (Entity.Exists(limousine))
            {
                StopHighSecurityEscortParkedLimousine(limousine);
            }

            MaybeAnnounceHighSecurityEscortArrival(player);
            return;
        }

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            bool isLimousine = IsHighSecurityEscortLimousineVehicle(vehicle);

            if (_highSecurityEscortPickupParked && isLimousine)
            {
                StopHighSecurityEscortParkedLimousine(vehicle);
                continue;
            }

            float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);
            float desiredStopRange = isLimousine ? 7.0f : 12.0f;

            if (distanceToPlayer <= desiredStopRange + 4.0f && vehicle.Speed <= 1.6f)
            {
                continue;
            }

            ContinueHighSecurityVehicleNearPlayer(
                vehicle,
                player,
                HighSecurityEscortSmoothArrivalDriveSpeed,
                desiredStopRange,
                false);
        }
    }

    private void ContinueHighSecurityVehicleNearPlayer(Vehicle vehicle, Ped player, float speed, float stoppingRange, bool force)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player) || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        if (!force && IsHighSecurityEscortVehicleInSoftRecovery(vehicle))
        {
            return;
        }

        if (!force && _highSecurityEscortPickupParked && IsHighSecurityEscortLimousineVehicle(vehicle))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return;
        }

        bool combatMode = IsHighSecurityEscortCombatActive();

        if (!force && IsHighSecurityEscortVehicleOrderCooldownActive(vehicle))
        {
            return;
        }

        Vector3 target = CalculateHighSecurityArrivalTarget(vehicle, player);
        float distance = vehicle.Position.DistanceTo(target);

        if (!force && !combatMode && IsHighSecurityEscortVehicleSettledNearTarget(vehicle, target, stoppingRange + 3.5f))
        {
            return;
        }

        if (!CanIssueHighSecurityEscortVehicleOrder(vehicle, force))
        {
            return;
        }

        if (ShouldSkipHighSecurityEscortRepeatedVehicleOrder(
            vehicle,
            target,
            force,
            distance <= 40.0f ? 5.5f : 10.0f,
            140.0f,
            combatMode ? 1.0f : 2.5f))
        {
            return;
        }

        ConfigureHighSecurityEscortDriver(driver, combatMode);
        RecordHighSecurityEscortVehicleOrderTarget(vehicle, target);

        float cruiseSpeed = CalculateHighSecurityEscortSmoothApproachSpeed(combatMode ? Math.Max(speed, HighSecurityEscortCombatRouteSpeed) : speed, distance);
        int style = GetHighSecurityEscortDrivingStyle(combatMode);

        try
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, cruiseSpeed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                vehicle.Handle,
                target.X,
                target.Y,
                target.Z,
                cruiseSpeed,
                style,
                stoppingRange);
        }
        catch
        {
        }
    }

    private Vector3 CalculateHighSecurityArrivalTarget(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(player))
        {
            return Vector3.Zero;
        }

        Vector3 roadTarget;

        if (TryCalculateHighSecurityArrivalRoadTarget(vehicle, player, out roadTarget))
        {
            return roadTarget;
        }

        Vector3 forward = Normalize(player.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(player.Heading);
        }

        int role = Entity.Exists(vehicle) ? GetHighSecurityEscortVehicleRole(vehicle.Handle) : HighSecurityEscortVehicleRoleLimousine;
        return player.Position - forward * GetHighSecurityEscortArrivalBackDistance(role);
    }

    private bool TryCalculateHighSecurityArrivalRoadTarget(Vehicle vehicle, Ped player, out Vector3 target)
    {
        target = Vector3.Zero;

        if (!Entity.Exists(player))
        {
            return false;
        }

        if (!EnsureHighSecurityEscortPickupRoadCache(player, vehicle))
        {
            return false;
        }

        if (_highSecurityEscortCachedArrivalTargets == null || _highSecurityEscortCachedArrivalTargets.Length < 5)
        {
            return false;
        }

        int role = Entity.Exists(vehicle) ? GetHighSecurityEscortVehicleRole(vehicle.Handle) : HighSecurityEscortVehicleRoleLimousine;
        int cacheIndex = GetHighSecurityEscortArrivalTargetCacheIndex(role);

        if (cacheIndex < 0 || cacheIndex >= _highSecurityEscortCachedArrivalTargets.Length)
        {
            return false;
        }

        target = _highSecurityEscortCachedArrivalTargets[cacheIndex];
        return target.Length() > 0.001f;
    }

    private bool EnsureHighSecurityEscortPickupRoadCache(Ped player, Vehicle requestingVehicle)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        if (IsHighSecurityEscortPickupRoadCacheValid(player))
        {
            return true;
        }

        if (Game.GameTime < _highSecurityEscortPickupRoadFailedUntil)
        {
            return false;
        }

        Vector3 limoStop;
        Vector3 approachDirection;

        if (!TryFindHighSecurityEscortPickupRoadLine(player, requestingVehicle, out limoStop, out approachDirection))
        {
            _highSecurityEscortPickupRoadFailedUntil = Game.GameTime + HighSecurityEscortPickupRoadFailedRetryMs;
            return false;
        }

        CacheHighSecurityEscortArrivalTargets(player, limoStop, approachDirection);
        return true;
    }

    private bool IsHighSecurityEscortPickupRoadCacheValid(Ped player)
    {
        if (!Entity.Exists(player) || Game.GameTime >= _highSecurityEscortPickupRoadCacheUntil)
        {
            return false;
        }

        if (_highSecurityEscortCachedPickupRoadPoint.Length() < 0.001f ||
            _highSecurityEscortCachedPickupDirection.Length() < 0.001f ||
            _highSecurityEscortCachedArrivalTargets == null ||
            _highSecurityEscortCachedArrivalTargets.Length < 5)
        {
            return false;
        }

        return _highSecurityEscortCachedPickupPlayerPosition.DistanceTo(player.Position) <= HighSecurityEscortPickupRoadPlayerRefreshDistance;
    }

    private void CacheHighSecurityEscortArrivalTargets(Ped player, Vector3 limoStop, Vector3 approachDirection)
    {
        Vector3 normalizedDirection = Normalize(new Vector3(approachDirection.X, approachDirection.Y, 0.0f));

        if (normalizedDirection.Length() < 0.001f && Entity.Exists(player))
        {
            normalizedDirection = Normalize(player.ForwardVector);
        }

        if (normalizedDirection.Length() < 0.001f)
        {
            normalizedDirection = new Vector3(0.0f, 1.0f, 0.0f);
        }

        _highSecurityEscortCachedPickupRoadPoint = limoStop;
        _highSecurityEscortCachedPickupDirection = normalizedDirection;
        _highSecurityEscortCachedPickupPlayerPosition = Entity.Exists(player) ? player.Position : limoStop;
        _highSecurityEscortPickupRoadCacheUntil = Game.GameTime + HighSecurityEscortPickupRoadCacheMs;
        _highSecurityEscortPickupRoadFailedUntil = 0;

        _highSecurityEscortCachedArrivalTargets = new Vector3[5];
        _highSecurityEscortCachedArrivalTargets[0] = CalculateHighSecurityEscortArrivalSlotFast(limoStop, normalizedDirection, HighSecurityEscortVehicleRoleLimousine);
        _highSecurityEscortCachedArrivalTargets[1] = CalculateHighSecurityEscortArrivalSlotFast(limoStop, normalizedDirection, HighSecurityEscortVehicleRoleFrontLeft);
        _highSecurityEscortCachedArrivalTargets[2] = CalculateHighSecurityEscortArrivalSlotFast(limoStop, normalizedDirection, HighSecurityEscortVehicleRoleFrontRight);
        _highSecurityEscortCachedArrivalTargets[3] = CalculateHighSecurityEscortArrivalSlotFast(limoStop, normalizedDirection, HighSecurityEscortVehicleRoleRearLeft);
        _highSecurityEscortCachedArrivalTargets[4] = CalculateHighSecurityEscortArrivalSlotFast(limoStop, normalizedDirection, HighSecurityEscortVehicleRoleRearRight);
    }

    private Vector3 CalculateHighSecurityEscortArrivalSlotFast(Vector3 limoStop, Vector3 approachDirection, int role)
    {
        Vector3 slot = limoStop - approachDirection * GetHighSecurityEscortArrivalBackDistance(role);
        Vector3 roadSlot;

        if (TrySnapHighSecurityEscortRoadSlotFast(slot, out roadSlot))
        {
            slot = roadSlot;
        }

        return slot + new Vector3(0.0f, 0.0f, 0.35f);
    }

    private bool TrySnapHighSecurityEscortRoadSlotFast(Vector3 desired, out Vector3 snapped)
    {
        snapped = Vector3.Zero;

        Vector3 node;

        if (!TryGetClosestVehicleNode(desired, 0, out node))
        {
            return false;
        }

        if (node.DistanceTo(desired) > HighSecurityEscortRuntimeRoadSnapDistance)
        {
            return false;
        }

        snapped = node;
        return true;
    }

    private int GetHighSecurityEscortArrivalTargetCacheIndex(int role)
    {
        switch (role)
        {
            case HighSecurityEscortVehicleRoleLimousine:
                return 0;

            case HighSecurityEscortVehicleRoleFrontLeft:
                return 1;

            case HighSecurityEscortVehicleRoleFrontRight:
                return 2;

            case HighSecurityEscortVehicleRoleRearLeft:
                return 3;

            case HighSecurityEscortVehicleRoleRearRight:
                return 4;

            default:
                return 0;
        }
    }

    private bool TryFindHighSecurityEscortPickupRoadLine(Ped player, Vehicle requestingVehicle, out Vector3 limoStop, out Vector3 approachDirection)
    {
        limoStop = Vector3.Zero;
        approachDirection = Vector3.Zero;

        if (!Entity.Exists(player))
        {
            return false;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);
        Vehicle referenceVehicle = Entity.Exists(limousine) ? limousine : requestingVehicle;
        Vector3 referencePosition = Entity.Exists(referenceVehicle)
            ? referenceVehicle.Position
            : player.Position - DirectionFromHeading(player.Heading) * 24.0f;

        Vector3 playerForward = Normalize(player.ForwardVector);

        if (playerForward.Length() < 0.001f)
        {
            playerForward = DirectionFromHeading(player.Heading);
        }

        if (playerForward.Length() < 0.001f)
        {
            playerForward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 camForward = GetGameplayCameraForwardVector();

        if (camForward.Length() < 0.001f)
        {
            camForward = playerForward;
        }

        Vector3 camRight = Normalize(new Vector3(camForward.Y, -camForward.X, 0.0f));
        Vector3 playerRight = Normalize(new Vector3(playerForward.Y, -playerForward.X, 0.0f));

        Vector3[] baseDirections =
        {
            playerForward,
            -playerForward,
            playerRight,
            -playerRight,
            camForward,
            -camForward,
            camRight,
            -camRight
        };

        float[] distances =
        {
            HighSecurityEscortPickupRoadMinPlayerDistance + 5.0f,
            22.0f,
            38.0f,
            HighSecurityEscortPickupRoadSearchDistance
        };

        bool found = false;
        float bestScore = -9999.0f;
        Vector3 bestStop = Vector3.Zero;
        Vector3 bestDirection = Vector3.Zero;
        int checkedCandidates = 0;

        for (int d = 0; d < distances.Length && checkedCandidates < HighSecurityEscortPickupRoadMaxCandidateChecks; d++)
        {
            for (int b = 0; b < baseDirections.Length && checkedCandidates < HighSecurityEscortPickupRoadMaxCandidateChecks; b++)
            {
                Vector3 dir = Normalize(baseDirections[b]);

                if (dir.Length() < 0.001f)
                {
                    continue;
                }

                Vector3 probe = player.Position + dir * distances[d];

                for (int nth = 0; nth < HighSecurityEscortMajorRoadNodeProbeCount && checkedCandidates < HighSecurityEscortPickupRoadMaxCandidateChecks; nth++)
                {
                    checkedCandidates++;

                    Vector3 candidate;

                    if (!TryGetClosestVehicleNode(probe, nth, out candidate))
                    {
                        continue;
                    }

                    float distanceToPlayer = candidate.DistanceTo(player.Position);

                    if (distanceToPlayer < HighSecurityEscortPickupRoadMinPlayerDistance ||
                        distanceToPlayer > HighSecurityEscortPickupRoadMaxPlayerDistance ||
                        Math.Abs(candidate.Z - player.Position.Z) > 16.0f)
                    {
                        continue;
                    }

                    Vector3 approachFromReference = Normalize(candidate - referencePosition);

                    if (approachFromReference.Length() < 0.001f)
                    {
                        approachFromReference = playerForward;
                    }

                    Vector3 lineDirection;

                    if (!TryEstimateHighSecurityEscortRoadDirectionFast(candidate, approachFromReference, out lineDirection))
                    {
                        lineDirection = approachFromReference;
                    }

                    if (lineDirection.Length() < 0.001f)
                    {
                        continue;
                    }

                    if ((lineDirection.X * approachFromReference.X + lineDirection.Y * approachFromReference.Y) < 0.0f)
                    {
                        lineDirection = -lineDirection;
                    }

                    float referenceAlignment = lineDirection.X * approachFromReference.X + lineDirection.Y * approachFromReference.Y;
                    float roadScore = ScoreHighSecurityEscortRoadClearance(candidate, lineDirection);

                    float score =
                        roadScore * 1.65f +
                        ClampFloat(referenceAlignment, -0.3f, 1.0f) * 4.0f -
                        Math.Abs(distanceToPlayer - 22.0f) * 0.08f -
                        nth * 0.55f;

                    if (!found || score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestStop = candidate;
                        bestDirection = lineDirection;
                    }
                }
            }
        }

        if (!found)
        {
            return false;
        }

        limoStop = bestStop;
        approachDirection = bestDirection;

        if (approachDirection.Length() < 0.001f)
        {
            approachDirection = Normalize(bestStop - referencePosition);
        }

        if (approachDirection.Length() < 0.001f)
        {
            approachDirection = playerForward;
        }

        return true;
    }

    private bool TryEstimateHighSecurityEscortRoadDirectionFast(Vector3 candidate, Vector3 preferredDirection, out Vector3 roadDirection)
    {
        roadDirection = Normalize(new Vector3(preferredDirection.X, preferredDirection.Y, 0.0f));

        if (roadDirection.Length() < 0.001f)
        {
            roadDirection = new Vector3(0.0f, 1.0f, 0.0f);
        }

        float[] angles =
        {
            0.0f, -35.0f, 35.0f, 70.0f, -70.0f, 90.0f
        };

        bool found = false;
        float bestScore = -9999.0f;
        Vector3 bestDirection = roadDirection;

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 axis = Normalize(RotateDirection2D(roadDirection, angles[i]));

            if (axis.Length() < 0.001f)
            {
                continue;
            }

            Vector3 forwardNode;
            Vector3 backwardNode;

            if (!TryGetClosestVehicleNode(candidate + axis * HighSecurityEscortRuntimeRoadDirectionProbeDistance, 0, out forwardNode) ||
                !TryGetClosestVehicleNode(candidate - axis * HighSecurityEscortRuntimeRoadDirectionProbeDistance, 0, out backwardNode))
            {
                continue;
            }

            float forwardSnap = forwardNode.DistanceTo(candidate + axis * HighSecurityEscortRuntimeRoadDirectionProbeDistance);
            float backwardSnap = backwardNode.DistanceTo(candidate - axis * HighSecurityEscortRuntimeRoadDirectionProbeDistance);
            float nodeSeparation = forwardNode.DistanceTo(backwardNode);
            float preferredAlignment = Math.Abs(axis.X * roadDirection.X + axis.Y * roadDirection.Y);
            float score = nodeSeparation - forwardSnap * 0.55f - backwardSnap * 0.55f + preferredAlignment * 4.0f;

            if (!found || score > bestScore)
            {
                found = true;
                bestScore = score;
                bestDirection = axis;
            }
        }

        if (!found)
        {
            return roadDirection.Length() >= 0.001f;
        }

        if ((bestDirection.X * roadDirection.X + bestDirection.Y * roadDirection.Y) < 0.0f)
        {
            bestDirection = -bestDirection;
        }

        roadDirection = bestDirection;
        return true;
    }

    private float GetHighSecurityEscortArrivalBackDistance(int role)
    {
        switch (role)
        {
            case HighSecurityEscortVehicleRoleLimousine:
                return 0.0f;

            case HighSecurityEscortVehicleRoleFrontLeft:
                return HighSecurityEscortArrivalConvoySpacing;

            case HighSecurityEscortVehicleRoleFrontRight:
                return HighSecurityEscortArrivalConvoySpacing * 2.0f;

            case HighSecurityEscortVehicleRoleRearLeft:
                return HighSecurityEscortArrivalConvoySpacing * 3.0f;

            case HighSecurityEscortVehicleRoleRearRight:
                return HighSecurityEscortArrivalConvoySpacing * 4.0f;

            default:
                return HighSecurityEscortArrivalConvoySpacing * 2.0f;
        }
    }

    private void OrderHighSecurityEscortArrivalToPlayer(Ped player, bool force)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle))
            {
                continue;
            }

            ContinueHighSecurityVehicleNearPlayer(vehicle, player, HighSecurityEscortArrivalDriveSpeed, 8.0f, force);
        }

        MaybeAnnounceHighSecurityEscortArrival(player);
    }

    private void MaybeAnnounceHighSecurityEscortArrival(Ped player)
    {
        if (_highSecurityEscortArrivalAnnounced || !Entity.Exists(player) || player.IsInVehicle())
        {
            return;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (!Entity.Exists(limousine) || !IsVehicleDriveable(limousine))
        {
            return;
        }

        Vector3 target = CalculateHighSecurityArrivalTarget(limousine, player);
        float distanceToTarget = limousine.Position.DistanceTo(target);
        float distanceToPlayer = limousine.Position.DistanceTo(player.Position);

        if (distanceToTarget > 10.5f || distanceToPlayer > 18.0f)
        {
            return;
        }

        if (!_highSecurityEscortPickupParked)
        {
            _highSecurityEscortPickupParked = true;
            StopHighSecurityEscortConvoyImmediately(true);
        }

        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortDepartureHoldUntil = 0;

        if (limousine.Speed > 2.6f)
        {
            StopHighSecurityEscortParkedLimousine(limousine);
            return;
        }

        _highSecurityEscortArrivalAnnounced = true;
        StopHighSecurityEscortConvoyImmediately(true);

        try
        {
            Function.Call((Hash)NativeStartVehicleHorn, limousine.Handle, 450, Game.GenerateHash("HELDDOWN"), false);
        }
        catch
        {
        }

        ShowStatus("Escorte haute sécurité : limousine arrivée sur la route. Monte à l'arrière avec F.", 5200);
    }

    private void ReturnHighSecurityEscortGuardsToVehicles(bool force)
    {
        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                continue;
            }

            Vehicle assignedVehicle = FindVehicleByHandle(npc.BodyguardAssignedVehicleHandle);

            if (!Entity.Exists(assignedVehicle) || !IsVehicleDriveable(assignedVehicle))
            {
                continue;
            }

            if (npc.Ped.IsInVehicle(assignedVehicle))
            {
                continue;
            }

            if (!force && IsHighSecurityEscortGuardCombatFootLocked(npc.Ped))
            {
                continue;
            }

            CommandHighSecurityEscortGuardEnterAssignedVehicle(
                npc,
                assignedVehicle,
                force || IsHighSecurityEscortLimousineVehicle(assignedVehicle),
                IsHighSecurityEscortLimousineVehicle(assignedVehicle));
        }
    }

    private void CommandHighSecurityEscortGuardEnterAssignedVehicle(SpawnedNpc npc, Vehicle assignedVehicle, bool force, bool teleportIfFar)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || !Entity.Exists(assignedVehicle) || !IsVehicleDriveable(assignedVehicle))
        {
            return;
        }

        if (npc.Ped.IsInVehicle(assignedVehicle))
        {
            return;
        }

        if (!force && IsHighSecurityEscortGuardCombatFootLocked(npc.Ped))
        {
            return;
        }

        if (!force && !CanIssueHighSecurityEscortPedOrder(npc.Ped, false))
        {
            return;
        }

        int seat = npc.BodyguardAssignedSeat;
        bool guardWasDriver = npc.BodyguardIsDriver || npc.BodyguardAssignedSeat == -1;

        if (!IsHighSecurityEscortSeatSupported(assignedVehicle, seat))
        {
            seat = FindFreeHighSecurityEscortSeatForGuard(assignedVehicle, guardWasDriver);
        }

        if (seat == _highSecurityEscortPlayerSeat && IsHighSecurityEscortLimousineVehicle(assignedVehicle))
        {
            seat = FindFreeHighSecurityEscortSeatForGuard(assignedVehicle, false);
        }

        if (seat == 999)
        {
            return;
        }

        if (!IsSeatFreeSafe(assignedVehicle, seat) && !IsPedOccupyingVehicleSeat(npc.Ped, assignedVehicle, seat))
        {
            return;
        }

        if (teleportIfFar &&
            npc.Ped.Position.DistanceTo(assignedVehicle.Position) > 18.0f &&
            CanTeleportHighSecurityEscortGuardIntoVehicleWithoutBeingSeen(npc.Ped, assignedVehicle))
        {
            PutPedIntoVehicleSafe(npc.Ped, assignedVehicle, seat);
        }
        else
        {
            try
            {
                Function.Call(
                    Hash.TASK_ENTER_VEHICLE,
                    npc.Ped.Handle,
                    assignedVehicle.Handle,
                    12000,
                    seat,
                    1.8f,
                    1,
                    0);
            }
            catch
            {
            }
        }

        npc.BodyguardAssignedSeat = seat;
        npc.BodyguardIsDriver = seat == -1;
    }

    private bool CanTeleportHighSecurityEscortGuardIntoVehicleWithoutBeingSeen(Ped ped, Vehicle vehicle)
    {
        if (!Entity.Exists(ped) || !Entity.Exists(vehicle))
        {
            return false;
        }

        Ped player = Game.Player.Character;

        if (Entity.Exists(player))
        {
            if (ped.Position.DistanceTo(player.Position) < 58.0f || vehicle.Position.DistanceTo(player.Position) < 58.0f)
            {
                return false;
            }
        }

        if (IsEntityLikelyVisibleToPlayer(ped) || IsEntityLikelyVisibleToPlayer(vehicle))
        {
            return false;
        }

        return true;
    }

    private int FindFreeHighSecurityEscortSeatForGuard(Vehicle vehicle)
    {
        return FindFreeHighSecurityEscortSeatForGuard(vehicle, true);
    }

    private int FindFreeHighSecurityEscortSeatForGuard(Vehicle vehicle, bool allowDriverSeat)
    {
        if (!Entity.Exists(vehicle))
        {
            return 999;
        }

        if (allowDriverSeat && IsSeatFreeSafe(vehicle, -1))
        {
            return -1;
        }

        int passengerCount = Math.Max(0, GetVehicleSeatCapacityIncludingDriver(vehicle) - 1);

        for (int seat = 0; seat < passengerCount; seat++)
        {
            if (vehicle.Handle == _highSecurityEscortLimousineHandle && seat == _highSecurityEscortPlayerSeat)
            {
                continue;
            }

            if (IsSeatFreeSafe(vehicle, seat))
            {
                return seat;
            }
        }

        return 999;
    }

    private void FollowHighSecurityEscortGuardOnFoot(SpawnedNpc npc, Ped player, bool force)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(player))
        {
            return;
        }

        if (!CanIssueHighSecurityEscortPedOrder(npc.Ped, force))
        {
            return;
        }

        float distance = npc.Ped.Position.DistanceTo(player.Position);

        if (distance <= 2.1f)
        {
            try
            {
                Function.Call(Hash.TASK_STAND_STILL, npc.Ped.Handle, 900);
            }
            catch
            {
            }

            return;
        }

        float offsetSide = ((Math.Abs(npc.Ped.Handle) % 7) - 3) * 0.72f;
        float offsetBack = -HighSecurityEscortGuardFootFollowDistance - ((Math.Abs(npc.Ped.Handle) % 4) * 0.55f);

        try
        {
            npc.Ped.Weapons.Select(ResolveHighSecurityEscortPrimaryWeapon(), true);
            Function.Call(
                Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY,
                npc.Ped.Handle,
                player.Handle,
                offsetSide,
                offsetBack,
                0.0f,
                2.1f,
                -1,
                1.35f,
                true);
        }
        catch
        {
        }
    }

    private void CommandHighSecurityEscortGuardLeaveVehicle(SpawnedNpc npc, Vehicle vehicle, bool force)
    {
        if (npc == null || !Entity.Exists(npc.Ped) || !Entity.Exists(vehicle) || !npc.Ped.IsInVehicle(vehicle))
        {
            return;
        }

        bool isLimousine = IsHighSecurityEscortLimousineVehicle(vehicle);

        if (IsHighSecurityEscortLimousineTurretGuard(npc.Ped))
        {
            return;
        }

        if (isLimousine)
        {
            return;
        }

        if (!CanIssueHighSecurityEscortPedOrder(npc.Ped, force))
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
    }

    private bool IsHighSecurityEscortVehicleOrderCooldownActive(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return true;
        }

        int nextAt;
        return _highSecurityEscortNextVehicleOrderAt.TryGetValue(vehicle.Handle, out nextAt) && Game.GameTime < nextAt;
    }

    private bool CanIssueHighSecurityEscortVehicleOrder(Vehicle vehicle, bool force)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        int nextAt;

        if (!force &&
            _highSecurityEscortNextVehicleOrderAt.TryGetValue(vehicle.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        int interval = IsHighSecurityEscortCombatActive() ? 950 : HighSecurityEscortVehicleOrderIntervalMs;
        _highSecurityEscortNextVehicleOrderAt[vehicle.Handle] =
            Game.GameTime + interval + Math.Abs(vehicle.Handle % 230);

        return true;
    }

    private bool IsHighSecurityEscortVehicleSettledNearTarget(Vehicle vehicle, Vector3 target, float distanceTolerance)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        float distance = vehicle.Position.DistanceTo(target);

        return distance <= distanceTolerance && vehicle.Speed <= 1.8f;
    }

    private bool ShouldSkipHighSecurityEscortRepeatedVehicleOrder(
        Vehicle vehicle,
        Vector3 target,
        bool force,
        float targetShiftTolerance,
        float maxVehicleDistance,
        float minMovingSpeed)
    {
        if (force || !Entity.Exists(vehicle))
        {
            return false;
        }

        if (IsHighSecurityEscortVehicleBlockedOrStuckForCombat(vehicle) || IsHighSecurityEscortVehicleInSoftRecovery(vehicle))
        {
            return false;
        }

        Vector3 lastTarget;

        if (!_highSecurityEscortLastVehicleOrderTarget.TryGetValue(vehicle.Handle, out lastTarget))
        {
            return false;
        }

        if (lastTarget.Length() < 0.001f)
        {
            return false;
        }

        return lastTarget.DistanceTo(target) <= targetShiftTolerance &&
               vehicle.Position.DistanceTo(target) <= maxVehicleDistance &&
               vehicle.Speed >= minMovingSpeed;
    }

    private void RecordHighSecurityEscortVehicleOrderTarget(Vehicle vehicle, Vector3 target)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        _highSecurityEscortLastVehicleOrderTarget[vehicle.Handle] = target;
    }

    private float CalculateHighSecurityEscortSmoothApproachSpeed(float requestedSpeed, float distance)
    {
        float maxSpeed;

        if (distance <= 16.0f)
        {
            maxSpeed = HighSecurityEscortSmoothConvoyCloseDriveSpeed;
        }
        else if (distance <= 38.0f)
        {
            maxSpeed = 11.5f;
        }
        else if (distance <= 95.0f)
        {
            maxSpeed = HighSecurityEscortSmoothArrivalDriveSpeed;
        }
        else
        {
            maxSpeed = HighSecurityEscortSmoothFormationCatchupSpeed;
        }

        return ClampFloat(requestedSpeed, HighSecurityEscortSmoothConvoyCloseDriveSpeed, maxSpeed);
    }

    private float CalculateHighSecurityEscortTaxiSpeed(Vehicle vehicle, Vector3 target)
    {
        return CalculateHighSecurityEscortTaxiSpeed(vehicle, target, IsHighSecurityEscortCombatActive());
    }

    private float CalculateHighSecurityEscortTaxiSpeed(Vehicle vehicle, Vector3 target, bool combatMode)
    {
        bool rushMode = IsHighSecurityEscortRushModeActive(combatMode);

        if (!Entity.Exists(vehicle))
        {
            return combatMode ? HighSecurityEscortCombatRouteSpeed : (rushMode ? HighSecurityEscortRushRouteSpeed : HighSecurityEscortSmoothConvoyDriveSpeed);
        }

        float distance = vehicle.Position.DistanceTo(target);

        if (distance <= 18.0f)
        {
            return combatMode ? HighSecurityEscortCombatCloseSpeed : HighSecurityEscortSmoothConvoyCloseDriveSpeed;
        }

        if (distance <= 45.0f)
        {
            return combatMode ? 14.0f : 11.5f;
        }

        if (distance >= 160.0f)
        {
            return combatMode ? HighSecurityEscortCombatFormationCatchupSpeed : (rushMode ? HighSecurityEscortRushFormationCatchupSpeed : HighSecurityEscortSmoothFormationCatchupSpeed);
        }

        return combatMode ? HighSecurityEscortCombatRouteSpeed : (rushMode ? HighSecurityEscortRushRouteSpeed : HighSecurityEscortSmoothConvoyDriveSpeed);
    }

    private bool CanIssueHighSecurityEscortPedOrder(Ped ped, bool force)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int nextAt;

        if (!force &&
            _highSecurityEscortNextPedOrderAt.TryGetValue(ped.Handle, out nextAt) &&
            Game.GameTime < nextAt)
        {
            return false;
        }

        _highSecurityEscortNextPedOrderAt[ped.Handle] =
            Game.GameTime + HighSecurityEscortPedOrderIntervalMs + Math.Abs(ped.Handle % 210);

        return true;
    }

    private void ConfigureHighSecurityEscortDriver(Ped driver)
    {
        ConfigureHighSecurityEscortDriver(driver, IsHighSecurityEscortCombatActive());
    }

    private void ConfigureHighSecurityEscortDriver(Ped driver, bool combatMode)
    {
        if (!Entity.Exists(driver))
        {
            return;
        }

        try
        {
            bool rushMode = IsHighSecurityEscortRushModeActive(combatMode);
            Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1.0f);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, combatMode ? 0.34f : (rushMode ? 0.28f : 0.08f));
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, GetHighSecurityEscortDrivingStyle(combatMode));
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, driver.Handle, false);
            Function.Call(Hash.SET_PED_STAY_IN_VEHICLE_WHEN_JACKED, driver.Handle, true);
        }
        catch
        {
        }
    }

    private bool IsHighSecurityEscortRushModeActive(bool combatMode)
    {
        return !combatMode && _highSecurityEscortRushMode && _highSecurityEscortDestinationActive;
    }

    private bool ShouldKeepHighSecurityEscortPickupLimousineParked(Vehicle vehicle)
    {
        return _highSecurityEscortPickupParked && IsHighSecurityEscortLimousineVehicle(vehicle);
    }

    private int GetHighSecurityEscortDrivingStyle(bool combatMode)
    {
        if (combatMode)
        {
            return HighSecurityEscortCombatDrivingStyle;
        }

        return IsHighSecurityEscortRushModeActive(false)
            ? HighSecurityEscortFastTaxiDrivingStyle
            : HighSecurityEscortCalmTaxiDrivingStyle;
    }

    private void RescueHighSecurityEscortVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player) || !IsVehicleDriveable(vehicle))
        {
            return;
        }

        if (ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return;
        }

        int handle = vehicle.Handle;
        Vector3 lastPosition;
        int lastMoveAt;

        if (!_highSecurityEscortLastVehiclePositions.TryGetValue(handle, out lastPosition))
        {
            _highSecurityEscortLastVehiclePositions[handle] = vehicle.Position;
            _highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;
            _highSecurityEscortVehicleStuckSinceAt[handle] = 0;
            return;
        }

        if (!_highSecurityEscortLastVehicleMoveAt.TryGetValue(handle, out lastMoveAt))
        {
            lastMoveAt = Game.GameTime;
        }

        if (vehicle.Position.DistanceTo(lastPosition) > 1.4f || vehicle.Speed > 1.2f)
        {
            _highSecurityEscortLastVehiclePositions[handle] = vehicle.Position;
            _highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;
            _highSecurityEscortVehicleStuckSinceAt[handle] = 0;
            return;
        }

        int stuckFor = Game.GameTime - lastMoveAt;
        bool obstacleAhead = HasHighSecurityEscortObstacleAhead(vehicle, HighSecurityEscortObstacleProbeDistance);
        bool combatActive = IsHighSecurityEscortCombatActive();

        if (stuckFor < HighSecurityEscortSoftUnstuckAfterMs && !obstacleAhead)
        {
            return;
        }

        int stuckSince;

        if (!_highSecurityEscortVehicleStuckSinceAt.TryGetValue(handle, out stuckSince) || stuckSince == 0)
        {
            _highSecurityEscortVehicleStuckSinceAt[handle] = Game.GameTime - stuckFor;
        }

        if ((combatActive || obstacleAhead || stuckFor >= HighSecurityEscortSoftUnstuckAfterMs) && TrySoftUnstuckHighSecurityEscortVehicle(vehicle, seedIndex))
        {
            return;
        }

        if (stuckFor >= HighSecurityEscortRoadRejoinAfterMs && TryRoadRejoinHighSecurityEscortVehicle(vehicle, seedIndex))
        {
            return;
        }

        float distanceToPlayer = vehicle.Position.DistanceTo(player.Position);

        if (stuckFor < HighSecurityEscortHardRescueAfterMs)
        {
            return;
        }

        if (distanceToPlayer < HighSecurityEscortVehicleTooFarDistance && IsEntityLikelyVisibleToPlayer(vehicle))
        {
            return;
        }

        int lastRescueAt;

        if (_highSecurityEscortLastVehicleRescueAt.TryGetValue(handle, out lastRescueAt) &&
            Game.GameTime - lastRescueAt < 9000)
        {
            return;
        }

        Vehicle limousine = FindVehicleByHandle(_highSecurityEscortLimousineHandle);

        if (_highSecurityEscortDestinationActive &&
            vehicle.Handle != _highSecurityEscortLimousineHandle &&
            TryRecoverLostHighSecurityEscortVehicleBehindLimousine(vehicle, limousine, player, GetHighSecurityEscortVehicleRole(handle), true))
        {
            return;
        }

        Vector3 rescuePoint;

        if (!TryFindHiddenRoadPointNearPlayer(player, seedIndex + 141, 58.0f, 96.0f, out rescuePoint))
        {
            return;
        }

        try
        {
            vehicle.Position = rescuePoint;
            vehicle.Heading = HeadingFromTo(rescuePoint, player.Position);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
        }
        catch
        {
        }

        ResetHighSecurityEscortVehicleRecoveryState(vehicle);
        _highSecurityEscortLastVehicleRescueAt[handle] = Game.GameTime;
    }

    private bool TryRecoverLostHighSecurityEscortVehicleBehindLimousine(Vehicle vehicle, Vehicle limousine, Ped player, int role, bool force)
    {
        if (!Entity.Exists(vehicle) ||
            !Entity.Exists(limousine) ||
            !Entity.Exists(player) ||
            !IsVehicleDriveable(vehicle) ||
            !IsVehicleDriveable(limousine) ||
            vehicle.Handle == limousine.Handle ||
            !_highSecurityEscortDestinationActive ||
            !IsPlayerInHighSecurityEscortLimousine(player))
        {
            return false;
        }

        float distanceToLimousine = vehicle.Position.DistanceTo(limousine.Position);
        float recoverGap = force ? HighSecurityEscortConvoyHardCatchupGap : HighSecurityEscortConvoyMaxVisibleGap;

        if (!force && distanceToLimousine < recoverGap)
        {
            return false;
        }

        if (!force && vehicle.Speed > 8.5f && distanceToLimousine < recoverGap + 28.0f)
        {
            return false;
        }

        int handle = vehicle.Handle;
        int lastRescueAt;

        if (_highSecurityEscortLastVehicleRescueAt.TryGetValue(handle, out lastRescueAt) &&
            Game.GameTime - lastRescueAt < HighSecurityEscortLostEscortTeleportCooldownMs)
        {
            return false;
        }

        if (!CanTeleportHighSecurityEscortVehicleWithoutBeingSeen(vehicle, player))
        {
            return false;
        }

        Vector3 target;
        float heading;

        if (!TryCalculateHighSecurityEscortLostVehicleTeleportSlot(limousine, player, role, out target, out heading))
        {
            return false;
        }

        if (target.DistanceTo(player.Position) < HighSecurityEscortLostEscortTargetMinPlayerDistance || IsPointInPlayerView(player, target))
        {
            return false;
        }

        if (!CanTeleportHighSecurityEscortVehicleCrewWithoutBeingSeen(vehicle, player))
        {
            return false;
        }

        try
        {
            vehicle.Position = target;
            vehicle.Heading = heading;
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);

            Ped driver = GetDriverOfVehicle(vehicle);

            if (Entity.Exists(driver))
            {
                Function.Call(Hash.CLEAR_PED_TASKS, driver.Handle);
            }
        }
        catch
        {
            return false;
        }

        ReattachHighSecurityEscortHiddenCrewToVehicle(vehicle, player);
        ResetHighSecurityEscortVehicleRecoveryState(vehicle);
        _highSecurityEscortLastVehicleRescueAt[handle] = Game.GameTime;

        IssueHighSecurityFormationDriveOrder(vehicle, limousine, role, true, false);
        return true;
    }

    private bool TryCalculateHighSecurityEscortLostVehicleTeleportSlot(Vehicle limousine, Ped player, int role, out Vector3 target, out float heading)
    {
        target = Vector3.Zero;
        heading = 0.0f;

        if (!Entity.Exists(limousine) || !Entity.Exists(player))
        {
            return false;
        }

        Vector3 forward = Normalize(limousine.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(limousine.Heading);
        }

        if (forward.Length() < 0.001f)
        {
            return false;
        }

        float backDistance = GetHighSecurityEscortFormationBackDistance(role, false) + Math.Min(HighSecurityEscortLostEscortExtraBackDistance, 14.0f);
        Vector3 desired = limousine.Position - forward * backDistance;
        Vector3 roadPoint;

        if (TryGetClosestVehicleNode(desired, 0, out roadPoint) && roadPoint.DistanceTo(desired) <= HighSecurityEscortLostEscortRoadSnapDistance)
        {
            desired = roadPoint + new Vector3(0.0f, 0.0f, 0.45f);
        }
        else
        {
            desired += new Vector3(0.0f, 0.0f, 0.45f);
        }

        if (desired.DistanceTo(limousine.Position) < 24.0f)
        {
            return false;
        }

        target = desired;
        heading = HeadingFromDirection(forward);
        return true;
    }

    private bool CanTeleportHighSecurityEscortVehicleWithoutBeingSeen(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        if (vehicle.Position.DistanceTo(player.Position) < 58.0f)
        {
            return false;
        }

        if (IsEntityLikelyVisibleToPlayer(vehicle) || IsPointInPlayerView(player, vehicle.Position))
        {
            return false;
        }

        return true;
    }

    private bool CanTeleportHighSecurityEscortVehicleCrewWithoutBeingSeen(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return false;
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || npc.BodyguardAssignedVehicleHandle != vehicle.Handle)
            {
                continue;
            }

            if (npc.Ped.IsInVehicle(vehicle))
            {
                continue;
            }

            if (npc.Ped.Position.DistanceTo(player.Position) < 58.0f || IsEntityLikelyVisibleToPlayer(npc.Ped))
            {
                return false;
            }
        }

        return true;
    }

    private void ReattachHighSecurityEscortHiddenCrewToVehicle(Vehicle vehicle, Ped player)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(player))
        {
            return;
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || npc.BodyguardAssignedVehicleHandle != vehicle.Handle)
            {
                continue;
            }

            if (npc.Ped.IsInVehicle(vehicle))
            {
                continue;
            }

            if (npc.Ped.Position.DistanceTo(player.Position) < 58.0f || IsEntityLikelyVisibleToPlayer(npc.Ped))
            {
                continue;
            }

            int seat = npc.BodyguardAssignedSeat;

            if (!IsHighSecurityEscortSeatSupported(vehicle, seat) ||
                (IsHighSecurityEscortLimousineVehicle(vehicle) && seat == _highSecurityEscortPlayerSeat))
            {
                bool guardWasDriver = npc.BodyguardIsDriver || npc.BodyguardAssignedSeat == -1;
                seat = FindFreeHighSecurityEscortSeatForGuard(vehicle, guardWasDriver);
            }

            if (seat == 999 || (!IsSeatFreeSafe(vehicle, seat) && !IsPedOccupyingVehicleSeat(npc.Ped, vehicle, seat)))
            {
                continue;
            }

            PutPedIntoVehicleSafe(npc.Ped, vehicle, seat);
            npc.BodyguardAssignedSeat = seat;
            npc.BodyguardIsDriver = seat == -1;
        }
    }

    private bool TryRoadRejoinHighSecurityEscortVehicle(Vehicle vehicle, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
        {
            return false;
        }

        if (ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return true;
        }

        int handle = vehicle.Handle;
        int lastRejoinAt;

        if (_highSecurityEscortLastVehicleRoadRejoinAt.TryGetValue(handle, out lastRejoinAt) &&
            Game.GameTime - lastRejoinAt < HighSecurityEscortRoadRejoinCooldownMs)
        {
            return false;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return false;
        }

        Vector3 target;

        if (!TryFindHighSecurityEscortRoadRejoinTarget(vehicle, seedIndex, out target))
        {
            return false;
        }

        bool combatMode = IsHighSecurityEscortCombatActive();
        int style = GetHighSecurityEscortDrivingStyle(combatMode);

        try
        {
            ConfigureHighSecurityEscortDriver(driver, combatMode);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver.Handle, HighSecurityEscortRoadRejoinDriveSpeed);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                driver.Handle,
                vehicle.Handle,
                target.X,
                target.Y,
                target.Z,
                HighSecurityEscortRoadRejoinDriveSpeed,
                style,
                5.0f);
        }
        catch
        {
            return false;
        }

        _highSecurityEscortLastVehicleRoadRejoinAt[handle] = Game.GameTime;
        _highSecurityEscortVehicleRecoveryUntil[handle] = Game.GameTime + HighSecurityEscortRoadRejoinOrderMs;
        _highSecurityEscortNextVehicleOrderAt[handle] = Game.GameTime + HighSecurityEscortRoadRejoinOrderMs;
        _highSecurityEscortLastVehicleOrderTarget[handle] = target;
        return true;
    }

    private bool TryFindHighSecurityEscortRoadRejoinTarget(Vehicle vehicle, int seedIndex, out Vector3 target)
    {
        target = Vector3.Zero;

        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        Vector3 forward = Normalize(vehicle.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(vehicle.Heading);
        }

        if (forward.Length() < 0.001f)
        {
            forward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 right = Normalize(new Vector3(forward.Y, -forward.X, 0.0f));
        float side = Math.Abs(seedIndex + vehicle.Handle) % 2 == 0 ? 1.0f : -1.0f;

        Vector3[] probes =
        {
            vehicle.Position - forward * HighSecurityEscortRoadRejoinProbeDistance,
            vehicle.Position - forward * (HighSecurityEscortRoadRejoinProbeDistance + 5.0f) + right * (6.0f * side),
            vehicle.Position - forward * (HighSecurityEscortRoadRejoinProbeDistance + 5.0f) - right * (6.0f * side),
            vehicle.Position + right * (8.0f * side),
            vehicle.Position - right * (8.0f * side),
            vehicle.Position + forward * 10.0f
        };

        for (int i = 0; i < probes.Length; i++)
        {
            Vector3 roadPoint;

            if (!TryGetClosestVehicleNode(probes[i], i % 3, out roadPoint))
            {
                continue;
            }

            float distance = roadPoint.DistanceTo(vehicle.Position);

            if (distance < 4.0f || distance > 38.0f)
            {
                continue;
            }

            target = roadPoint + new Vector3(0.0f, 0.0f, 0.35f);
            return true;
        }

        return false;
    }

    private void ResetHighSecurityEscortVehicleRecoveryState(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return;
        }

        int handle = vehicle.Handle;
        _highSecurityEscortLastVehiclePositions[handle] = vehicle.Position;
        _highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;
        _highSecurityEscortVehicleStuckSinceAt[handle] = 0;
        _highSecurityEscortNextVehicleOrderAt[handle] = 0;
        _highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;
        _highSecurityEscortCachedFormationTargets.Remove(handle);
        _highSecurityEscortCachedFormationTargetUntil.Remove(handle);
        _highSecurityEscortVehicleRecoveryUntil[handle] = 0;
    }

    private bool TrySoftUnstuckHighSecurityEscortVehicle(Vehicle vehicle, int seedIndex)
    {
        if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
        {
            return false;
        }

        if (ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle))
        {
            StopHighSecurityEscortParkedLimousine(vehicle);
            return true;
        }

        int handle = vehicle.Handle;
        int lastSoftAt;

        if (_highSecurityEscortLastVehicleSoftUnstuckAt.TryGetValue(handle, out lastSoftAt) &&
            Game.GameTime - lastSoftAt < HighSecurityEscortSoftUnstuckCooldownMs)
        {
            return false;
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver) || driver.IsDead)
        {
            return false;
        }

        int reverseAction;

        switch (Math.Abs(seedIndex + handle) % 3)
        {
            case 0:
                reverseAction = HighSecurityEscortSoftReverseLeftAction;
                break;

            case 1:
                reverseAction = HighSecurityEscortSoftReverseRightAction;
                break;

            default:
                reverseAction = HighSecurityEscortSoftReverseAction;
                break;
        }

        try
        {
            ConfigureHighSecurityEscortDriver(driver, IsHighSecurityEscortCombatActive());
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);
            Function.Call((Hash)NativeTaskVehicleTempAction, driver.Handle, vehicle.Handle, reverseAction, HighSecurityEscortSoftReverseMs);
        }
        catch
        {
            return false;
        }

        _highSecurityEscortLastVehicleSoftUnstuckAt[handle] = Game.GameTime;
        _highSecurityEscortVehicleRecoveryUntil[handle] = Game.GameTime + HighSecurityEscortSoftReverseMs + 450;
        _highSecurityEscortNextVehicleOrderAt[handle] = Game.GameTime + HighSecurityEscortSoftReverseMs + 250;
        _highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;
        _highSecurityEscortCachedFormationTargets.Remove(handle);
        _highSecurityEscortCachedFormationTargetUntil.Remove(handle);
        return true;
    }

    private bool IsHighSecurityEscortVehicleInSoftRecovery(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        int until;
        return _highSecurityEscortVehicleRecoveryUntil.TryGetValue(vehicle.Handle, out until) && Game.GameTime < until;
    }

    private bool IsHighSecurityEscortVehicleBlockedOrStuckForCombat(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        int lastMoveAt;

        if (_highSecurityEscortLastVehicleMoveAt.TryGetValue(vehicle.Handle, out lastMoveAt) &&
            Game.GameTime - lastMoveAt >= HighSecurityEscortSoftUnstuckAfterMs)
        {
            return true;
        }

        return vehicle.Speed <= 1.4f && HasHighSecurityEscortObstacleAhead(vehicle, HighSecurityEscortObstacleProbeDistance);
    }

    private bool HasHighSecurityEscortObstacleAhead(Vehicle vehicle, float distance)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        Vector3 forward = Normalize(vehicle.ForwardVector);

        if (forward.Length() < 0.001f)
        {
            forward = DirectionFromHeading(vehicle.Heading);
        }

        Vector3 origin = vehicle.Position + new Vector3(0.0f, 0.0f, 0.85f) + forward * 1.8f;

        try
        {
            IntersectOptions options = (IntersectOptions)(
                (int)IntersectOptions.Map |
                (int)IntersectOptions.Objects |
                (int)IntersectOptions.Vegetation);

            RaycastResult ray = World.Raycast(origin, forward, distance, options, vehicle);

            if (ray.DitHitAnything)
            {
                return true;
            }
        }
        catch
        {
        }

        Ped driver = GetDriverOfVehicle(vehicle);

        if (!Entity.Exists(driver))
        {
            return false;
        }

        Vehicle[] nearbyVehicles = GetNearbyVehiclesSafe(driver, distance + 4.0f);

        for (int i = 0; i < nearbyVehicles.Length; i++)
        {
            Vehicle other = nearbyVehicles[i];

            if (!Entity.Exists(other) || other.Handle == vehicle.Handle)
            {
                continue;
            }

            Vector3 delta = other.Position - vehicle.Position;
            float z = Math.Abs(delta.Z);

            if (z > 3.0f)
            {
                continue;
            }

            float forwardDistance = delta.X * forward.X + delta.Y * forward.Y;

            if (forwardDistance <= 0.0f || forwardDistance > distance + 2.0f)
            {
                continue;
            }

            float lateralX = delta.X - forward.X * forwardDistance;
            float lateralY = delta.Y - forward.Y * forwardDistance;
            float lateralDistance = (float)Math.Sqrt(lateralX * lateralX + lateralY * lateralY);

            if (lateralDistance <= 3.6f)
            {
                return true;
            }
        }

        return false;
    }

    private int GetHighSecurityEscortVehicleRole(int vehicleHandle)
    {
        int role;

        if (_highSecurityEscortVehicleRoles.TryGetValue(vehicleHandle, out role))
        {
            return role;
        }

        return HighSecurityEscortVehicleRoleRearRight;
    }

    private void MarkHighSecurityEscortCombatActive()
    {
        _highSecurityEscortCombatModeUntil = Math.Max(_highSecurityEscortCombatModeUntil, Game.GameTime + HighSecurityEscortCombatMemoryMs);
    }

    private bool IsHighSecurityEscortCombatActive()
    {
        return _highSecurityEscortCombatModeUntil > 0 && Game.GameTime <= _highSecurityEscortCombatModeUntil;
    }

    private void PrepareHighSecurityEscortGuardServiceResumeAfterJustice(SpawnedNpc guard)
    {
        if (guard == null || !Entity.Exists(guard.Ped) ||
            !IsHighSecurityEscortPedHandle(guard.Ped.Handle))
        {
            return;
        }

        int now = Game.GameTime;
        int handle = guard.Ped.Handle;
        _highSecurityEscortGuardCombatFootLockUntil.Remove(handle);
        _highSecurityEscortNextCombatOrderAt[handle] = 0;
        _highSecurityEscortNextPedOrderAt[handle] = 0;
        _highSecurityEscortNextGuardPassiveMaintenanceAt[handle] = now;
        _highSecurityEscortNextGuardMobilityOrderAt[handle] = now;

        Vehicle assignedVehicle = FindVehicleByHandle(guard.BodyguardAssignedVehicleHandle);
        if (Entity.Exists(assignedVehicle))
        {
            _highSecurityEscortNextVehicleOrderAt[assignedVehicle.Handle] = 0;
            _highSecurityEscortLastVehicleOrderTarget[assignedVehicle.Handle] = Vector3.Zero;
            _highSecurityEscortCachedFormationTargets.Remove(assignedVehicle.Handle);
            _highSecurityEscortCachedFormationTargetUntil.Remove(assignedVehicle.Handle);
        }

        // Le transfert a déjà remplacé le combat par un freinage ou une garde
        // locale. Je prépare ici la reprise normale après détention ou évasion.
        guard.NextThinkAt = now;
        guard.NextBodyguardTaskAt = now;
        guard.Activated = false;
        guard.IsReturningHome = false;
    }

    private void MarkHighSecurityEscortGuardCombatFootLock(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        _highSecurityEscortGuardCombatFootLockUntil[ped.Handle] = Game.GameTime + HighSecurityEscortGuardCombatFootLockMs;
    }

    private bool IsHighSecurityEscortGuardCombatFootLocked(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int until;

        if (!_highSecurityEscortGuardCombatFootLockUntil.TryGetValue(ped.Handle, out until))
        {
            return false;
        }

        if (Game.GameTime <= until)
        {
            return true;
        }

        _highSecurityEscortGuardCombatFootLockUntil.Remove(ped.Handle);
        return false;
    }

    private void CleanupHighSecurityEscortCombatLocks()
    {
        if (_highSecurityEscortGuardCombatFootLockUntil.Count == 0)
        {
            return;
        }

        List<int> expired = new List<int>();

        foreach (KeyValuePair<int, int> entry in _highSecurityEscortGuardCombatFootLockUntil)
        {
            if (Game.GameTime > entry.Value || !_highSecurityEscortKnownNpcHandles.Contains(entry.Key))
            {
                expired.Add(entry.Key);
            }
        }

        for (int i = 0; i < expired.Count; i++)
        {
            _highSecurityEscortGuardCombatFootLockUntil.Remove(expired[i]);
        }
    }

    private void DismissHighSecurityEscort(bool showStatus)
    {
        CleanupHighSecurityEscortHandleSets();

        if (!_highSecurityEscortActive && !_highSecurityEscortDismissing && !HasLiveHighSecurityEscortTeamWithoutCleanup())
        {
            return;
        }

        _highSecurityEscortActive = false;
        _highSecurityEscortDismissing = true;
        _highSecurityEscortMode = HighSecurityEscortModeDismissing;
        _highSecurityEscortDestinationActive = false;
        _highSecurityEscortRushMode = false;
        _highSecurityEscortRushKeyLatch = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortStopKeyLatch = false;
        _highSecurityEscortDepartureHoldUntil = 0;
        _highSecurityEscortEmergencyFleeDestination = Vector3.Zero;
        _highSecurityEscortEmergencyFleeUntil = 0;
        _highSecurityEscortDestination = Vector3.Zero;
        _highSecurityEscortDismissStartedAt = Game.GameTime;
        _highSecurityEscortDismissCleanupAt = Game.GameTime + HighSecurityEscortDismissForceCleanupMs;
        _nextHighSecurityEscortDismissOrderAt = 0;
        _nextHighSecurityEscortThinkAt = 0;

        if (showStatus)
        {
            ShowStatus("Escorte haute sécurité : ordre de repli envoyé. Tu peux rappeler avec L.", 4500);
        }
    }

    private void UpdateHighSecurityEscortDismissing(Ped player)
    {
        CleanupHighSecurityEscortHandleSets();

        if (!_highSecurityEscortDismissing)
        {
            return;
        }

        if (_highSecurityEscortKnownNpcHandles.Count == 0 && _highSecurityEscortVehicleHandles.Count == 0)
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(false);
            return;
        }

        if (Game.GameTime >= _highSecurityEscortDismissCleanupAt)
        {
            ForceDeleteHighSecurityEscortEntitiesAndRecords(true);
            return;
        }

        if (Entity.Exists(player) && Game.GameTime >= _nextHighSecurityEscortDismissOrderAt)
        {
            _nextHighSecurityEscortDismissOrderAt = Game.GameTime + HighSecurityEscortDismissOrderIntervalMs;
            OrderHighSecurityEscortDismissal(player);
        }

        DeleteHighSecurityEscortDismissedEntitiesIfSafe(player);
    }

    private void OrderHighSecurityEscortDismissal(Ped player)
    {
        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle) || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            Ped driver = GetDriverOfVehicle(vehicle);

            if (!Entity.Exists(driver) || driver.IsDead)
            {
                continue;
            }

            ConfigureHighSecurityEscortDriver(driver);
            Vector3 target = FindHighSecurityEscortDismissPoint(player, i);

            try
            {
                Function.Call(
                    Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    driver.Handle,
                    vehicle.Handle,
                    target.X,
                    target.Y,
                    target.Z,
                    24.0f,
                    HighSecurityEscortDrivingStyle,
                    15.0f);
            }
            catch
            {
            }
        }

        List<int> npcHandles = new List<int>(_highSecurityEscortKnownNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead || npc.Ped.IsInVehicle())
            {
                continue;
            }

            if (!CanIssueHighSecurityEscortPedOrder(npc.Ped, false))
            {
                continue;
            }

            Vector3 target = FindHighSecurityEscortDismissPoint(player, i + 31);

            try
            {
                Function.Call(
                    Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                    npc.Ped.Handle,
                    target.X,
                    target.Y,
                    target.Z,
                    2.0f,
                    -1,
                    4.0f,
                    true,
                    HeadingFromTo(npc.Ped.Position, target));
            }
            catch
            {
            }
        }
    }

    private Vector3 FindHighSecurityEscortDismissPoint(Ped player, int seedIndex)
    {
        Vector3 point;

        if (Entity.Exists(player) && TryFindHiddenRoadPointNearPlayer(player, seedIndex + 211, 95.0f, 165.0f, out point))
        {
            return point;
        }

        Vector3 basePosition = Entity.Exists(player) ? player.Position : Vector3.Zero;
        Vector3 forward = Entity.Exists(player) ? Normalize(player.ForwardVector) : new Vector3(0.0f, 1.0f, 0.0f);

        if (forward.Length() < 0.001f)
        {
            forward = new Vector3(0.0f, 1.0f, 0.0f);
        }

        Vector3 right = Normalize(new Vector3(forward.Y, -forward.X, 0.0f));
        return basePosition - forward * (115.0f + seedIndex * 4.0f) + right * (((seedIndex % 5) - 2) * 12.0f);
    }

    private void DeleteHighSecurityEscortDismissedEntitiesIfSafe(Ped player)
    {
        List<int> npcHandles = new List<int>(_highSecurityEscortKnownNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(npcHandles[i]);

            if (npc == null || !Entity.Exists(npc.Ped))
            {
                RemoveHighSecurityEscortNpcRecord(npcHandles[i], false);
                continue;
            }

            if (ShouldDeleteHighSecurityEscortDismissedEntity(npc.Ped, player))
            {
                RemoveHighSecurityEscortNpcRecord(npcHandles[i], true);
            }
        }

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            Vehicle vehicle = FindVehicleByHandle(vehicleHandles[i]);

            if (!Entity.Exists(vehicle))
            {
                DeleteHighSecurityEscortVehicleAndRecord(vehicleHandles[i], false);
                continue;
            }

            if (ShouldDeleteHighSecurityEscortDismissedEntity(vehicle, player))
            {
                DeleteHighSecurityEscortVehicleAndRecord(vehicleHandles[i], true);
            }
        }
    }

    private bool ShouldDeleteHighSecurityEscortDismissedEntity(Entity entity, Ped player)
    {
        if (!Entity.Exists(entity))
        {
            return true;
        }

        if (!Entity.Exists(player) || player.IsDead)
        {
            return Game.GameTime - _highSecurityEscortDismissStartedAt > 2500;
        }

        float distance = entity.Position.DistanceTo(player.Position);

        if (distance >= HighSecurityEscortDismissDeleteDistance && !IsEntityLikelyVisibleToPlayer(entity))
        {
            return true;
        }

        return false;
    }

    private void CleanupHighSecurityEscortHandleSets()
    {
        CleanupHighSecurityEscortCombatLocks();

        List<int> deadNpcHandles = new List<int>();

        foreach (int handle in _highSecurityEscortNpcHandles)
        {
            SpawnedNpc npc = FindHighSecurityEscortNpcRecordByHandle(handle);

            if (npc == null || !Entity.Exists(npc.Ped) || npc.Ped.IsDead)
            {
                deadNpcHandles.Add(handle);
            }
        }

        for (int i = 0; i < deadNpcHandles.Count; i++)
        {
            RemoveHighSecurityEscortNpcRecord(deadNpcHandles[i], false);
        }

        List<int> missingVehicleHandles = new List<int>();

        foreach (int handle in _highSecurityEscortVehicleHandles)
        {
            Vehicle vehicle = FindVehicleByHandle(handle);

            if (!Entity.Exists(vehicle))
            {
                missingVehicleHandles.Add(handle);
            }
        }

        for (int i = 0; i < missingVehicleHandles.Count; i++)
        {
            DeleteHighSecurityEscortVehicleAndRecord(missingVehicleHandles[i], false);
        }

        if (!_highSecurityEscortDismissing && _highSecurityEscortActive && _highSecurityEscortNpcHandles.Count == 0 && _highSecurityEscortVehicleHandles.Count == 0)
        {
            _highSecurityEscortActive = false;
            _highSecurityEscortMode = HighSecurityEscortModeNone;
            _highSecurityEscortDestinationActive = false;
        }
    }

    private bool HasLiveHighSecurityEscortTeamWithoutCleanup()
    {
        return _highSecurityEscortNpcHandles.Count > 0 || _highSecurityEscortVehicleHandles.Count > 0;
    }

    private void ForceDeleteHighSecurityEscortEntitiesAndRecords(bool deleteEntities)
    {
        List<int> npcHandles = new List<int>(_highSecurityEscortKnownNpcHandles);

        for (int i = 0; i < npcHandles.Count; i++)
        {
            RemoveHighSecurityEscortNpcRecord(npcHandles[i], deleteEntities);
        }

        List<int> vehicleHandles = new List<int>(_highSecurityEscortVehicleHandles);

        for (int i = 0; i < vehicleHandles.Count; i++)
        {
            DeleteHighSecurityEscortVehicleAndRecord(vehicleHandles[i], deleteEntities);
        }

        _highSecurityEscortNpcHandles.Clear();
        _highSecurityEscortKnownNpcHandles.Clear();
        _highSecurityEscortVehicleHandles.Clear();
        _highSecurityEscortVehicleRoles.Clear();
        _highSecurityEscortFullyUpgradedVehicleHandles.Clear();
        _highSecurityEscortNextVehicleOrderAt.Clear();
        _highSecurityEscortNextPedOrderAt.Clear();
        _highSecurityEscortNextCombatOrderAt.Clear();
        _highSecurityEscortNextGuardPassiveMaintenanceAt.Clear();
        _highSecurityEscortNextGuardMobilityOrderAt.Clear();
        _highSecurityEscortLastVehiclePositions.Clear();
        _highSecurityEscortLastVehicleMoveAt.Clear();
        _highSecurityEscortLastVehicleRescueAt.Clear();
        _highSecurityEscortLastVehicleSoftMaintenanceAt.Clear();
        _highSecurityEscortLastVehicleOrderTarget.Clear();
        _highSecurityEscortCachedFormationTargets.Clear();
        _highSecurityEscortCachedFormationTargetUntil.Clear();
        _highSecurityEscortGuardCombatFootLockUntil.Clear();
        _highSecurityEscortVehicleStuckSinceAt.Clear();
        _highSecurityEscortLastVehicleSoftUnstuckAt.Clear();
        _highSecurityEscortLastVehicleRoadRejoinAt.Clear();
        _highSecurityEscortVehicleRecoveryUntil.Clear();

        _highSecurityEscortActive = false;
        _highSecurityEscortDismissing = false;
        _highSecurityEscortMode = HighSecurityEscortModeNone;
        _highSecurityEscortDestinationActive = false;
        _highSecurityEscortDestination = Vector3.Zero;
        _highSecurityEscortRushMode = false;
        _highSecurityEscortArrivalAnnounced = false;
        _highSecurityEscortPickupParked = false;
        _highSecurityEscortRoutePaused = false;
        _highSecurityEscortStopKeyLatch = false;
        _highSecurityEscortLimousineLostAnnounced = false;
        _highSecurityEscortLimousineHandle = 0;
        _highSecurityEscortLimousineTurretGuardHandle = 0;
        _highSecurityEscortPlayerSeat = 1;
        _highSecurityEscortDepartureHoldUntil = 0;
        _highSecurityEscortEmergencyFleeDestination = Vector3.Zero;
        _highSecurityEscortEmergencyFleeUntil = 0;
        _highSecurityEscortPickupRoadCacheUntil = 0;
        _highSecurityEscortPickupRoadFailedUntil = 0;
        _highSecurityEscortCachedPickupRoadPoint = Vector3.Zero;
        _highSecurityEscortCachedPickupDirection = Vector3.Zero;
        _highSecurityEscortCachedPickupPlayerPosition = Vector3.Zero;
        _highSecurityEscortCachedArrivalTargets = null;
        _highSecurityEscortCachedThreatPed = null;
        _highSecurityEscortCachedThreatUntil = 0;
        _nextHighSecurityEscortThreatScanAt = 0;
        _highSecurityEscortGuardThreatScanCursor = 0;
        _highSecurityEscortLastThreatRelationshipHandle = 0;
        _highSecurityEscortLastThreatRelationshipAt = 0;
        _highSecurityEscortCombatModeUntil = 0;
        _highSecurityEscortPhoneKeyLatch = false;
        _highSecurityEscortRouteKeyLatch = false;
        _highSecurityEscortEnterKeyLatch = false;
        _highSecurityEscortRushKeyLatch = false;
        _highSecurityEscortStopKeyLatch = false;
        _highSecurityEscortLCommandConsumedUntilRelease = false;
    }

    private void RemoveHighSecurityEscortNpcRecord(int handle, bool deleteEntity)
    {
        _highSecurityEscortNpcHandles.Remove(handle);
        _highSecurityEscortKnownNpcHandles.Remove(handle);
        _highSecurityEscortNextPedOrderAt.Remove(handle);
        _highSecurityEscortNextCombatOrderAt.Remove(handle);
        _highSecurityEscortNextGuardPassiveMaintenanceAt.Remove(handle);
        _highSecurityEscortNextGuardMobilityOrderAt.Remove(handle);
        _highSecurityEscortGuardCombatFootLockUntil.Remove(handle);

        if (handle == _highSecurityEscortLimousineTurretGuardHandle)
        {
            _highSecurityEscortLimousineTurretGuardHandle = 0;
        }

        if (Entity.Exists(_highSecurityEscortCachedThreatPed) && _highSecurityEscortCachedThreatPed.Handle == handle)
        {
            ClearCachedHighSecurityEscortThreat();
        }

        for (int i = _spawnedNpcs.Count - 1; i >= 0; i--)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc == null || GetSpawnedNpcRecordHandleSafe(npc) != handle)
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

    private void DeleteHighSecurityEscortVehicleAndRecord(int handle, bool deleteEntity)
    {
        Vehicle vehicle = FindVehicleByHandle(handle);
        RemoveHighSecurityEscortPlacedVehicleRecord(handle, false);

        if (deleteEntity && Entity.Exists(vehicle))
        {
            DeleteEntitySafe(vehicle);
        }

        _highSecurityEscortVehicleHandles.Remove(handle);
        _highSecurityEscortVehicleRoles.Remove(handle);
        _highSecurityEscortFullyUpgradedVehicleHandles.Remove(handle);
        _highSecurityEscortNextVehicleOrderAt.Remove(handle);
        _highSecurityEscortLastVehiclePositions.Remove(handle);
        _highSecurityEscortLastVehicleMoveAt.Remove(handle);
        _highSecurityEscortLastVehicleRescueAt.Remove(handle);
        _highSecurityEscortLastVehicleSoftMaintenanceAt.Remove(handle);
        _highSecurityEscortLastVehicleOrderTarget.Remove(handle);
        _highSecurityEscortCachedFormationTargets.Remove(handle);
        _highSecurityEscortCachedFormationTargetUntil.Remove(handle);
        _highSecurityEscortVehicleStuckSinceAt.Remove(handle);
        _highSecurityEscortLastVehicleSoftUnstuckAt.Remove(handle);
        _highSecurityEscortLastVehicleRoadRejoinAt.Remove(handle);
        _highSecurityEscortVehicleRecoveryUntil.Remove(handle);

        if (handle == _highSecurityEscortLimousineHandle)
        {
            _highSecurityEscortLimousineHandle = 0;
        }
    }

    private void RemoveHighSecurityEscortPlacedVehicleRecord(int handle, bool deleteEntity)
    {
        for (int i = _placedVehicles.Count - 1; i >= 0; i--)
        {
            PlacedVehicle placed = _placedVehicles[i];

            if (placed == null || GetPlacedVehicleRecordHandleSafe(placed) != handle)
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

    private SpawnedNpc FindHighSecurityEscortNpcRecordByHandle(int handle)
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
}
