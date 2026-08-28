using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;
using GtaControl = GTA.Control;
using Keys = System.Windows.Forms.Keys;

public sealed partial class DonJEnemySpawner
{
    // Je garde la détention dans ce partial : Justice.cs décide du jugement et
    // m'appelle uniquement via les hooks privés documentés en fin de fichier.
    private const ulong JusticeNativeGiveWeaponToPed = 0xBF0FD6E56C964FCBUL;
    private const ulong JusticeNativeSetBlockingOfNonTemporaryEvents = 0x9F8AA94D6D97DBF4UL;
    private const ulong JusticeNativeSetPedKeepTask = 0x971D38760FBC02EFUL;
    private const ulong JusticeNativeTaskWanderStandard = 0xBB9CE077274F6A1BUL;
    private const ulong JusticeNativeTaskStartScenarioInPlace = 0x142A02425FF02BD9UL;
    private const ulong JusticeNativeIsPedUsingAnyScenario = 0x57AB4A3080F85143UL;
    private const ulong JusticeNativeClearPedTasksImmediately = 0xAAA34F8A7CB32098UL;
    private const ulong JusticeNativeClearPedLastWeaponDamage = 0x0E98F88A24C5F4B8UL;
    private const ulong JusticeNativeGetNumDlcWeapons = 0xEE47635F352DA367UL;
    private const ulong JusticeNativeGetDlcWeaponData = 0x79923CD21BECE14EUL;
    private const ulong JusticeNativeSetPoliceIgnorePlayer = 0x32C62AA929C2DA6AUL;
    private const ulong JusticeNativeSetDispatchCopsForPlayer = 0xDB172424876553F4UL;
    private const ulong JusticeNativeSetPedAmmo = 0x14E56BC5B5DB6A19UL;

    private const int JusticeCustodySceneRefreshMs = 1500;
    private const int JusticeCustodyDisciplineScanMs = 180;
    private const int JusticeCustodyDisciplineDurationMs = 3500;
    private const int JusticeCustodyDisciplineCooldownMs = 8000;
    private const int JusticeCustodySelfDefenseWindowMs = 8000;
    private const int JusticeCustodyTrackedAggressorCapacity = 8;
    private const int JusticeCustodyDisciplineRetryInitialMs = 750;
    private const int JusticeCustodyDisciplineRetryMaximumMs = 5000;
    private const int JusticeCustodyDisciplineReturnTimeoutMs = 30000;
    private const int JusticeCustodyPoliceSuppressionIntervalMs = 1000;
    private const int JusticeCustodyTransferInitialRetryMs = 750;
    private const int JusticeCustodyTransferMaximumRetryMs = 5000;
    private const int JusticeCustodyTransferTimeoutMs = 30000;
    private const int JusticeCustodyReleaseTeleportTimeoutMs = 30000;
    private const int JusticeCustodyDeferredRestoreDelayMs = 15000;
    private const int JusticeCustodyDeferredRestoreRetryMs = 5000;
    private const int JusticeCustodyEscapeGraceMs = 3000;
    private const int JusticeCustodyActivityScenarioGraceMs = 1500;
    private const int JusticeCustodyActivityScenarioCheckMs = 120;
    private const int JusticeCustodyMaxFrameElapsedMs = 2000;
    private const int JusticeCustodyModelTimeoutMs = 75;
    private const int JusticeCustodyModelRetryMs = 7500;
    private const int JusticeCustodyMaxWeapons = 160;
    private const int JusticeCustodyMaxDlcWeaponDefinitions = 512;
    private const int JusticeCustodyMaxComponentsPerWeapon = 128;
    private const int JusticeCustodyMaximumSentenceSeconds = 30 * 60;
    private const int JusticeCustodyPrisonThresholdSeconds = 5 * 60;
    private const int JusticeCustodyFineConversionMaximumSeconds = 5 * 60;
    private const int JusticeCustodyFineDollarsPerSecond = 50;
    private const int JusticeCustodyFineCashReadRetryMs = 750;
    private const int JusticeCustodyDeathPersistenceRetryMs = 1000;
    private const int JusticeDlcWeaponDataSize = 312;
    private const int JusticeDlcWeaponHashOffset = 8;
    private const float JusticeCustodyActivityUseDistance = 2.4f;
    private const float JusticeCustodyActivityCancelDistance = 4.0f;

    private const int JusticeStunGunHash = unchecked((int)0x3656C8C1);
    private const int JusticeNightstickHash = unchecked((int)0x678B81B1);
    private const int JusticeUnarmedHash = unchecked((int)0xA2719263);

    private enum JusticeCustodySite
    {
        None,
        MissionRow,
        Bolingbroke
    }

    private enum JusticeCashWriteResult
    {
        Unknown,
        Succeeded,
        Rejected
    }

    private sealed class JusticeCustodyVolume
    {
        private readonly float[] _perimeterXY;

        internal JusticeCustodyVolume(Vector3 minimum, Vector3 maximum)
            : this(minimum, maximum, null)
        {
        }

        internal JusticeCustodyVolume(
            Vector3 minimum,
            Vector3 maximum,
            float[] perimeterXY)
        {
            Minimum = minimum;
            Maximum = maximum;
            _perimeterXY = perimeterXY != null && perimeterXY.Length >= 6 &&
                           perimeterXY.Length % 2 == 0
                ? perimeterXY
                : null;
        }

        internal Vector3 Minimum { get; private set; }

        internal Vector3 Maximum { get; private set; }

        internal bool Contains(Vector3 position)
        {
            if (position.X < Minimum.X || position.X > Maximum.X ||
                position.Y < Minimum.Y || position.Y > Maximum.Y ||
                position.Z < Minimum.Z || position.Z > Maximum.Z)
            {
                return false;
            }

            if (_perimeterXY == null)
            {
                return true;
            }

            // Je découpe les quatre grands angles du rectangle historique. Le
            // joueur reste libre dans toute l'enceinte, mais une téléportation
            // au-delà des vrais murs ne bénéficie plus d'un coin artificiel.
            bool inside = false;
            int pointCount = _perimeterXY.Length / 2;
            int previous = pointCount - 1;
            for (int current = 0; current < pointCount; current++)
            {
                float currentX = _perimeterXY[current * 2];
                float currentY = _perimeterXY[current * 2 + 1];
                float previousX = _perimeterXY[previous * 2];
                float previousY = _perimeterXY[previous * 2 + 1];
                if (IsJusticePointOnCustodyBoundary(
                        position.X,
                        position.Y,
                        previousX,
                        previousY,
                        currentX,
                        currentY))
                {
                    return true;
                }

                bool crosses = (currentY > position.Y) != (previousY > position.Y);
                if (crosses)
                {
                    float intersectionX = currentX +
                        (position.Y - currentY) *
                        (previousX - currentX) /
                        (previousY - currentY);
                    if (position.X < intersectionX)
                    {
                        inside = !inside;
                    }
                }
                previous = current;
            }

            return inside;
        }

        private static bool IsJusticePointOnCustodyBoundary(
            float x,
            float y,
            float startX,
            float startY,
            float endX,
            float endY)
        {
            const float tolerance = 0.02f;
            float cross = (x - startX) * (endY - startY) -
                          (y - startY) * (endX - startX);
            if (Math.Abs(cross) > tolerance)
            {
                return false;
            }

            return x >= Math.Min(startX, endX) - tolerance &&
                   x <= Math.Max(startX, endX) + tolerance &&
                   y >= Math.Min(startY, endY) - tolerance &&
                   y <= Math.Max(startY, endY) + tolerance;
        }
    }

    private sealed class JusticeCustodyActivityDefinition
    {
        internal JusticeCustodyActivityDefinition(
            string id,
            string displayName,
            Vector3 position,
            int durationSeconds,
            int reductionSeconds,
            int cooldownSeconds,
            string scenarioName)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Position = position;
            DurationSeconds = Math.Max(1, durationSeconds);
            ReductionSeconds = Math.Max(0, reductionSeconds);
            CooldownSeconds = Math.Max(0, cooldownSeconds);
            ScenarioName = scenarioName ?? string.Empty;
        }

        internal string Id { get; private set; }

        internal string DisplayName { get; private set; }

        internal Vector3 Position { get; private set; }

        internal int DurationSeconds { get; private set; }

        internal int ReductionSeconds { get; private set; }

        internal int CooldownSeconds { get; private set; }

        internal string ScenarioName { get; private set; }
    }

    private sealed class JusticeCustodyLayout
    {
        internal JusticeCustodySite Site;
        internal string DisplayName;
        internal Vector3 ArrivalPosition;
        internal float ArrivalHeading;
        internal Vector3 CellPosition;
        internal float CellHeading;
        internal Vector3 ReleasePosition;
        internal float ReleaseHeading;
        internal JusticeCustodyVolume[] AllowedVolumes;
        internal Vector3[] GuardPositions;
        internal float[] GuardHeadings;
        internal Vector3[] InmatePositions;
        internal JusticeCustodyActivityDefinition[] Activities;
        internal int MaximumActivityReductionSeconds;
    }

    private sealed class JusticeWeaponSnapshotItem
    {
        internal int WeaponHash;
        internal int Ammo;
        internal int AmmoInClip;
        internal int Tint;
        internal readonly List<int> ComponentHashes = new List<int>();
    }

    private sealed class JusticeWeaponSnapshot
    {
        internal bool IsValidated;
        internal int SelectedWeaponHash = JusticeUnarmedHash;
        internal readonly List<JusticeWeaponSnapshotItem> Weapons = new List<JusticeWeaponSnapshotItem>();
    }

    private sealed class JusticeFineDebitIntent
    {
        internal string EpisodeId = string.Empty;
        internal int Slot = -1;
        internal long FineAmount;
        internal bool CashPlanPrepared = true;
        internal long PreparedAtUtcTicks;
        internal int DebitAmount;
        internal int CashBefore;
        internal int CashAfter;
        internal int SentenceIfDebited;
        internal int SentenceIfConverted;
        internal bool StationPlanned;
        internal bool DebitAttempted;
        internal long AttemptedAtUtcTicks;
        internal JusticeCashWriteResult CashWriteResult = JusticeCashWriteResult.Unknown;
    }

    private sealed class JusticeDisciplineIntent
    {
        internal string IncidentId = string.Empty;
        internal JusticeCrimeKind CrimeKind = JusticeCrimeKind.ReportedViolentAct;
        internal int PenaltySeconds;
    }

    [StructLayout(LayoutKind.Explicit, Size = JusticeDlcWeaponDataSize)]
    private struct JusticeDlcWeaponData
    {
        // Je ne lis que le hash à l'offset contractuel de GET_DLC_WEAPON_DATA.
        [FieldOffset(JusticeDlcWeaponHashOffset)]
        internal int WeaponHash;
    }

    private static readonly JusticeCustodyLayout JusticeMissionRowLayout = BuildJusticeMissionRowLayout();
    private static readonly JusticeCustodyLayout JusticeBolingbrokeLayout = BuildJusticeBolingbrokeLayout();
    private static readonly string[] JusticeCustodyInmateModels =
    {
        "g_m_m_prisoners_01",
        "s_m_y_prismuscl_01",
        "s_m_y_prisoner_01",
        "u_m_y_prisoner_01"
    };

    // Je possède ces entités séparément : aucun garde ou détenu Justice n'entre
    // dans _spawnedNpcs, une scène XML ou un nettoyage générique du menu.
    private readonly List<Ped> _justiceCustodyGuards = new List<Ped>(4);
    private readonly List<Ped> _justiceCustodyInmates = new List<Ped>(8);
    // Je lie chaque handle à sa génération observée au spawn : un handle GTA
    // recyclé ne suffit jamais à transférer la propriété d'un ped à Justice.
    private Dictionary<int, int> _justiceCustodyPedGenerationByHandle =
        new Dictionary<int, int>();
    private readonly Dictionary<string, int> _justiceActivityCooldownUntil =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _justiceLoadedActivityCooldownSeconds =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private int[] _justiceCustodyAggressorHandles =
        new int[JusticeCustodyTrackedAggressorCapacity];
    private int[] _justiceCustodyAggressorGenerations =
        new int[JusticeCustodyTrackedAggressorCapacity];
    private long[] _justiceCustodyAggressorUntilMs =
        new long[JusticeCustodyTrackedAggressorCapacity];

    private JusticeCustodySite _justiceCustodySite;
    private bool _justiceCustodyRuntimeActive;
    private bool _justiceCustodyTransferPending;
    private bool _justiceCustodyTransferRollbackFinalizationPending;
    private bool _justiceCustodyTransferRollbackPrecommitRedundant;
    private bool _justiceCustodyResumePending;
    private bool _justiceCustodyWaitingForRespawn;
    private bool _justiceCustodyDeathRebindPending;
    private bool _justiceCustodyDeathStatePersistencePending;
    private int _justiceNextCustodyDeathPersistenceRetryAt;
    private bool _justiceCustodyPlayerStateStored;
    private bool _justiceCustodyStoredInvincible;
    private bool _justiceCustodyStoredFrozen;
    private bool _justiceCustodyStoredCanRagdoll = true;
    private int _justiceCustodyPlayerHandle;
    private int _justiceCustodyPlayerModelHash;
    private int _justiceCustodyPlayerSlot = -1;
    private int _justiceCustodyLastTickAt;
    private int _justiceCustodyElapsedRemainderMs;
    private int _justiceCustodyInitialSentenceSeconds;
    private int _justiceActivityReductionGrantedSeconds;
    private int _justiceNextCustodySceneRefreshAt;
    private int _justiceNextCustodyModelRetryAt;
    private int _justiceNextDisciplineScanAt;
    private int _justiceOutsideCustodySinceAt;
    private int _justiceNextPoliceSuppressionAt;
    private bool _justicePoliceSuppressionActive;
    private bool _justicePoliceIgnoreApplied;
    private bool _justicePoliceDispatchDisabled;
    private bool _justicePoliceSuppressionRestorePending;
    private bool _justicePoliceSuppressionFailureLogged;
    private int _justiceNextPoliceSuppressionRestoreAt;
    private int _justiceCustodyTransferStartedAt;
    private int _justiceNextCustodyTransferAttemptAt;
    private int _justiceCustodyTransferFailureCount;
    private bool _justiceCustodyTransferTimeoutLogged;

    private JusticeWeaponSnapshot _justiceWeaponSnapshot;
    private bool _justiceDeferredInventoryRestore;
    private int _justiceNextDeferredInventoryRestoreAt;
    private JusticeFineDebitIntent _justiceFineDebitIntent;
    private int _justiceNextFineCashReadAttemptAt;
    private bool _justiceFineCashReadFailureLogged;
    private Func<int, int?> _justiceCashReadOverride = null;
    private Func<int, int, bool?> _justiceCashWriteOverride = null;
    private JusticeDisciplineIntent _justiceDisciplineIntent;
    private bool _justiceInventoryRemoved;
    private bool _justiceWeaponControlsLocked;
    private int _justiceNextInventoryPersistenceRetryAt;

    private string _justiceActiveActivityId = string.Empty;
    private int _justiceActivityLastTickAt;
    private int _justiceActivityElapsedMs;
    private int _justiceNextActivityScenarioCheckAt;
    private bool _justiceActivityScenarioValidationPending;
    private bool _justiceActivityTaskClearPending;
    private int _justiceNextActivityTaskClearAt;
    private int _justiceEscapePersistenceRetryAt;

    private bool _justiceDisciplineActive;
    private int _justiceDisciplineEndsAt;
    private int _justiceDisciplineCooldownUntil;
    private int _justiceDisciplineReturnStartedAt;
    private int _justiceNextDisciplineReturnAttemptAt;
    private int _justiceDisciplineReturnFailureCount;
    private bool _justiceDisciplineStoredInvincible;
    private bool _justiceDisciplineInvincibilityRestorePending;
    private JusticeCrimeKind _justiceDisciplineCrimeKind = JusticeCrimeKind.ReportedViolentAct;
    private string _justiceDisciplineIncidentId = string.Empty;
    private int _justiceReleaseRestoreStartedAt;
    private int _justiceReleaseRestoreRetryAt;
    private int _justiceReleaseTeleportStartedAt;
    private int _justiceNextReleaseTeleportAttemptAt;
    private int _justiceReleaseTeleportFailureCount;
    private int _justiceNextLegalReleaseWantedClearAt;
    private int _justiceReleaseSelectedWeaponHash = JusticeUnarmedHash;
    private bool _justiceLegalReleaseFinalizationPending;
    private bool _justiceLegalReleaseWantedClearAttempted;
    private bool _justiceLegalReleaseWeaponSelectionApplied;
    private JusticeCustodySite _justiceLegalReleaseFinalizationSite;
    private int _justiceLegalReleaseSelectedWeaponHash = JusticeUnarmedHash;

    private bool JusticeIsCustodyActive
    {
        get
        {
            if (_justiceCustodyRuntimeActive || _justiceCustodyTransferPending || _justiceCustodyResumePending)
            {
                return true;
            }

            return _justiceCaseState != null &&
                   (_justiceCaseState.Phase == JusticePhase.Captured ||
                    _justiceCaseState.Phase == JusticePhase.Transporting ||
                    _justiceCaseState.Phase == JusticePhase.Incarcerated ||
                    _justiceCaseState.Phase == JusticePhase.Escaping);
        }
    }

    private static JusticeCustodyLayout BuildJusticeMissionRowLayout()
    {
        return new JusticeCustodyLayout
        {
            Site = JusticeCustodySite.MissionRow,
            DisplayName = "Commissariat de Mission Row",
            ArrivalPosition = new Vector3(459.86f, -994.38f, 24.91f),
            ArrivalHeading = 88.0f,
            CellPosition = new Vector3(459.86f, -994.38f, 24.91f),
            CellHeading = 88.0f,
            ReleasePosition = new Vector3(425.13f, -979.54f, 30.71f),
            ReleaseHeading = 88.0f,
            AllowedVolumes = new[]
            {
                new JusticeCustodyVolume(
                    new Vector3(438.0f, -1005.0f, 20.0f),
                    new Vector3(482.0f, -974.0f, 34.5f))
            },
            GuardPositions = new[]
            {
                new Vector3(457.10f, -991.08f, 24.91f),
                new Vector3(463.45f, -991.10f, 24.91f)
            },
            GuardHeadings = new[] { 178.0f, 182.0f },
            InmatePositions = new Vector3[0],
            Activities = new[]
            {
                new JusticeCustodyActivityDefinition(
                    "station_formalites",
                    "Formalités administratives",
                    new Vector3(461.05f, -989.20f, 24.91f),
                    20,
                    20,
                    45,
                    "WORLD_HUMAN_CLIPBOARD"),
                new JusticeCustodyActivityDefinition(
                    "station_nettoyage",
                    "Nettoyage de la cellule",
                    new Vector3(463.30f, -994.25f, 24.91f),
                    40,
                    30,
                    60,
                    "WORLD_HUMAN_JANITOR")
            },
            MaximumActivityReductionSeconds = 60
        };
    }

    private static JusticeCustodyLayout BuildJusticeBolingbrokeLayout()
    {
        return new JusticeCustodyLayout
        {
            Site = JusticeCustodySite.Bolingbroke,
            DisplayName = "Prison de Bolingbroke",
            ArrivalPosition = new Vector3(1690.86f, 2565.12f, 45.56f),
            ArrivalHeading = 178.0f,
            CellPosition = new Vector3(1690.86f, 2565.12f, 45.56f),
            CellHeading = 178.0f,
            ReleasePosition = new Vector3(1848.62f, 2585.83f, 45.67f),
            ReleaseHeading = 272.0f,
            AllowedVolumes = new[]
            {
                // Je suis le périmètre extérieur de Bolingbroke au lieu d'une
                // boîte géante. Toute la cour et les ailes restent autorisées,
                // tandis que les quatre coins hors des murs comptent comme sortis.
                new JusticeCustodyVolume(
                    new Vector3(1500.0f, 2380.0f, 25.0f),
                    new Vector3(1815.0f, 2725.0f, 85.0f),
                    new[]
                    {
                        1600.0f, 2380.0f,
                        1740.0f, 2410.0f,
                        1815.0f, 2500.0f,
                        1815.0f, 2640.0f,
                        1740.0f, 2725.0f,
                        1580.0f, 2725.0f,
                        1500.0f, 2640.0f,
                        1500.0f, 2500.0f
                    })
            },
            GuardPositions = new[]
            {
                new Vector3(1678.80f, 2561.70f, 45.56f),
                new Vector3(1702.80f, 2561.50f, 45.56f),
                new Vector3(1654.00f, 2527.20f, 45.56f),
                new Vector3(1724.20f, 2511.20f, 45.56f)
            },
            GuardHeadings = new[] { 92.0f, 268.0f, 8.0f, 184.0f },
            InmatePositions = new[]
            {
                new Vector3(1670.0f, 2522.0f, 45.56f),
                new Vector3(1680.0f, 2535.0f, 45.56f),
                new Vector3(1692.0f, 2514.0f, 45.56f),
                new Vector3(1704.0f, 2530.0f, 45.56f),
                new Vector3(1716.0f, 2518.0f, 45.56f),
                new Vector3(1660.0f, 2544.0f, 45.56f),
                new Vector3(1688.0f, 2550.0f, 45.56f),
                new Vector3(1710.0f, 2546.0f, 45.56f)
            },
            Activities = new[]
            {
                new JusticeCustodyActivityDefinition(
                    "prison_tour",
                    "Tour de cour",
                    new Vector3(1684.0f, 2525.0f, 45.56f),
                    60,
                    60,
                    90,
                    "WORLD_HUMAN_JOG_STANDING"),
                new JusticeCustodyActivityDefinition(
                    "prison_exercice",
                    "Exercice physique",
                    new Vector3(1646.2f, 2527.7f, 45.56f),
                    40,
                    45,
                    75,
                    "WORLD_HUMAN_MUSCLE_FREE_WEIGHTS"),
                new JusticeCustodyActivityDefinition(
                    "prison_travail",
                    "Travail pénitentiaire",
                    new Vector3(1677.0f, 2550.0f, 45.56f),
                    75,
                    90,
                    120,
                    "WORLD_HUMAN_HAMMERING"),
                new JusticeCustodyActivityDefinition(
                    "prison_rassemblement",
                    "Rassemblement",
                    new Vector3(1714.0f, 2503.0f, 45.56f),
                    30,
                    30,
                    60,
                    "WORLD_HUMAN_STAND_IMPATIENT")
            },
            MaximumActivityReductionSeconds = 5 * 60
        };
    }

    private void JusticeBeginCustodyTransfer(bool deathCapture)
    {
        if (_justiceCaseState == null || !_justiceEnabled)
        {
            return;
        }

        EnsureJusticeCustodyEpisodeId();
        bool waitForRespawn = _justiceCustodyWaitingForRespawn || deathCapture;
        if (waitForRespawn)
        {
            // Je persiste cette intention avant l'amende et avant tout retrait :
            // un respawn qui remplace une tenue ou un ped custom doit pouvoir
            // relier une seule fois le vrai protagoniste vivant au transfert.
            _justiceCustodyWaitingForRespawn = true;
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
            {
                return;
            }

            Ped waitingPlayer = Game.Player.Character;
            int waitingPlayerSlot = GetCurrentSinglePlayerCashSlotSafe();
            bool provenCustomRespawn = waitingPlayerSlot == -1 &&
                !_justiceCustodyDeathRebindPending &&
                _justiceCustodyPlayerSlot == _justiceActivePlayerProfileSlot &&
                _justiceLastCanonicalPlayerSlot == _justiceCustodyPlayerSlot &&
                IsJusticeCustodyPlayerIdentityCompatible(waitingPlayer);
            if (JusticePolicy.ShouldDeferCustodyFinancialMutationUntilRespawn(
                    true,
                    Entity.Exists(waitingPlayer),
                    Entity.Exists(waitingPlayer) && waitingPlayer.IsDead,
                    _justiceCustodyPlayerSlot,
                    waitingPlayerSlot,
                    provenCustomRespawn))
            {
                // Je ne calcule ni débit ni conversion sur un cadavre ou un ped
                // non prouvé. Après un rebind custom sûr, la branche slot -1
                // convertit l'amende sans toucher au cash d'un protagoniste.
                return;
            }
        }
        bool stationPlanned =
            _justiceCaseState.SentenceSeconds < JusticeCustodyPrisonThresholdSeconds;
        if (!JusticeCollectFineAndConvertDetention(stationPlanned, string.Empty))
        {
            return;
        }

        // Une amende intégralement payée se termine aux formalités, mais je fais
        // toujours le débit avant de décider si une cellule est nécessaire.
        if (_justiceCaseState.SentenceSeconds <= 0)
        {
            ResetJusticeCustodyPersistentFields();
            JusticePrepareLegalReleaseState();
            _justiceLegalReleaseFinalizationPending = true;
            _justiceLegalReleaseFinalizationSite = JusticeCustodySite.None;
            _justiceLegalReleaseSelectedWeaponHash = 0;
            _justiceLegalReleaseWantedClearAttempted = false;
            _justiceLegalReleaseWeaponSelectionApplied = false;
            JusticeMarkStateDirty();
            ResumeJusticeLegalReleaseFinalization(
                Game.Player.Character,
                GetJusticeRawGameTimeSafe());
            return;
        }

        if (_placementMode)
        {
            // Je rends d'abord les flags et la caméra du placement. Le snapshot
            // Justice capture ainsi l'état réel du héros, et UpdatePlacementMode
            // ne peut plus le regeler après le téléport de détention.
            StopPlacementMode(false);
        }

        // Je reclasse après la conversion de l'amende : une peine ayant atteint
        // exactement cinq minutes part directement à Bolingbroke.
        _justiceCustodySite = GetJusticeCustodySiteForSentence(
            _justiceCaseState.SentenceSeconds);
        _justiceCustodyInitialSentenceSeconds = Math.Max(
            _justiceCustodyInitialSentenceSeconds,
            _justiceCaseState.SentenceSeconds);
        _justiceActivityReductionGrantedSeconds = 0;
        _justiceCustodyRuntimeActive = true;
        _justiceCustodyTransferPending = true;
        _justiceCustodyResumePending = false;
        ResetJusticeCustodyTransferRetryState();
        _justiceCustodyWaitingForRespawn = waitForRespawn;
        _justiceOutsideCustodySinceAt = 0;
        _justiceCaseState.Phase = JusticePhase.Transporting;
        JusticeMarkStateDirty();

        Ped player = Game.Player.Character;
        if (!waitForRespawn && Entity.Exists(player) && !player.IsDead)
        {
            CompleteJusticeCustodyTransfer(player, Game.GameTime);
        }
    }

    private void JusticeUpdateCustody(Ped player, int now)
    {
        if (_justiceCaseState == null)
        {
            return;
        }

        if (!PersistJusticeCustodyDeathStateBeforeRespawn(now))
        {
            // Je n'accepte ni nouveau ped ni progression de peine tant que le
            // droit de rebind après décès n'existe pas durablement sur disque.
            ResetJusticeCustodyClock(now);
            return;
        }

        if (Entity.Exists(player) && player.IsDead)
        {
            if (IsJusticeRuntimeSuspended(player))
            {
                if (IsJusticeCustodyDeathIdentityCompatible(player))
                {
                    ObserveJusticeCustodyDeathDuringSuspension(player);
                }
                ResetJusticeCustodyClock(now);
                return;
            }

            ObserveJusticeCustodyDeath(player, now);
            return;
        }

        if (_justiceCustodyWaitingForRespawn && Entity.Exists(player) && !player.IsDead &&
            (_justiceCustodyDeathRebindPending ||
             !IsJusticeCustodyPlayerIdentityCompatible(player)))
        {
            if (!JusticeCustodyCanMutateWorld(player) ||
                !TryRebindJusticeCustodyIdentityAfterRespawn(player))
            {
                ResetJusticeCustodyClock(now);
                return;
            }
        }

        if (_justiceCustodyTransferRollbackFinalizationPending ||
            HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback))
        {
            // Je reprends ce WAL avant tout débit, retrait d'inventaire ou
            // nouveau téléport. Le XML intermédiaire peut déjà contenir un
            // inventaire rendu sous une phase Transporting après un crash.
            bool rollbackPreparedInMemory =
                _justiceCustodyTransferRollbackFinalizationPending &&
                _justiceCaseState.Phase == JusticePhase.AtLarge &&
                string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId);
            if ((!rollbackPreparedInMemory &&
                 !JusticeCustodyCanMutateWorld(player)) ||
                !ResumeJusticeCustodyTransferRollback(player, now))
            {
                ResetJusticeCustodyClock(now);
            }
            return;
        }

        if (_justiceFineDebitIntent != null)
        {
            // Je résous toujours l'intention financière avant de restaurer une
            // détention ou d'en faire avancer l'horloge. Les deux peines stockées
            // dans l'intention sont des valeurs absolues calculées au précommit :
            // les appliquer après avoir déjà purgé du temps doublerait ce temps.
            // Resume contrôle d'abord le slot monétaire porté par l'intention,
            // puis seulement le modèle. Je ne pré-lie donc jamais un autre héros.
            if (!JusticeCustodyCanMutateWorld(player) || !ResumeJusticeFineDebitIntent())
            {
                ResetJusticeCustodyClock(now);
                return;
            }
        }

        if (_justiceDisciplineInvincibilityRestorePending)
        {
            TryRestoreJusticeDisciplineInvincibility(player);
        }

        if (!_justiceCustodyRuntimeActive && _justiceCaseState.Phase == JusticePhase.Captured)
        {
            // Je reprends aussi une capture « amende seule » interrompue entre le
            // commit du jugement et son débit. Sans cette branche, Capture étant
            // déjà idempotente, ce dossier resterait bloqué définitivement.
            if (!JusticeCustodyCanMutateWorld(player) ||
                !IsJusticeCustodyPlayerIdentityCompatible(player))
            {
                ResetJusticeCustodyClock(now);
                return;
            }

            JusticeBeginCustodyTransfer(false);
            if (_justiceCaseState.Phase == JusticePhase.Captured || !JusticeIsCustodyActive)
            {
                return;
            }
        }

        if (HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory))
        {
            // L'intention d'évasion est durable avant le RemoveAll. Après un
            // crash dans cette fenêtre, je la termine sur le bon protagoniste.
            if (!JusticeCustodyCanMutateWorld(player) ||
                !IsJusticeCustodyPlayerIdentityCompatible(player))
            {
                ResetJusticeCustodyClock(now);
                return;
            }

            CompleteJusticeCustodyEscape(player);
            return;
        }

        if (!_justiceCustodyRuntimeActive && !_justiceCustodyTransferPending &&
            !_justiceCustodyResumePending &&
            _justiceCaseState.Phase == JusticePhase.Incarcerated &&
            _justiceCaseState.SentenceSeconds <= 0)
        {
            // Une peine terminée sur un autre protagoniste ne produit aucun effet
            // monde hors écran. Je finalise maintenant, sur le héros détenu revenu,
            // avant qu'une reprise ne le téléporte inutilement dans sa cellule.
            if (!JusticeCustodyCanMutateWorld(player) ||
                !IsJusticeCustodyPlayerIdentityCompatible(player))
            {
                ResetJusticeCustodyClock(now);
                return;
            }

            CompleteJusticeLegalRelease(player);
            return;
        }

        if (!_justiceCustodyRuntimeActive &&
            (_justiceCaseState.Phase == JusticePhase.Transporting ||
             _justiceCaseState.Phase == JusticePhase.Incarcerated ||
             _justiceCaseState.Phase == JusticePhase.Escaping))
        {
            RestoreJusticeCustodyRuntimeFromCase();
        }

        if (!JusticeIsCustodyActive)
        {
            return;
        }

        if (!Entity.Exists(player))
        {
            ResetJusticeCustodyClock(now);
            return;
        }

        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            // Je suspends sur un vrai changement de protagoniste pour ne jamais
            // rendre le loadout de Michael à Franklin (ou inversement).
            CancelJusticeCustodyActivity(false, now);
            ResetJusticeCustodyClock(now);
            return;
        }

        if (_justiceCustodyWaitingForRespawn)
        {
            _justiceCustodyWaitingForRespawn = false;
            _justiceCustodyTransferPending = true;
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
            {
                _justiceCustodyWaitingForRespawn = true;
                _justiceCustodyTransferPending = false;
                JusticeMarkStateDirty();
                ResetJusticeCustodyClock(now);
                return;
            }
        }

        if (_justiceCustodyTransferPending || _justiceCustodyResumePending)
        {
            if (!JusticeCustodyCanMutateWorld(player))
            {
                ResetJusticeCustodyClock(now);
                return;
            }

            CompleteJusticeCustodyTransfer(player, now);
            if (_justiceCustodyTransferPending || _justiceCustodyResumePending)
            {
                return;
            }
        }

        RetryJusticeCustodyActivityTaskClear(player, now);
        EnforceJusticeCustodyWeaponLock(player);

        if (!JusticeCustodyCanMutateWorld(player))
        {
            // GameTime continue pendant certains chargements/cinématiques : je
            // supprime l'activité afin qu'aucun bonus ne mûrisse hors gameplay.
            CancelJusticeCustodyActivity(false, now);
            ResetJusticeCustodyClock(now);
            return;
        }

        if (!EnsureJusticeCustodyPlayerMobility(player))
        {
            // Je suspends aussi la peine tant que GTA refuse de rendre le ped
            // mobile : le joueur ne doit jamais purger du temps bloqué sur place.
            ResetJusticeCustodyClock(now);
            return;
        }

        if (ScheduleJusticeBolingbrokeTransferIfRequired(now))
        {
            ResetJusticeCustodyClock(now);
            return;
        }

        // Je ne modifie les flags globaux police qu'après le gate gameplay et
        // après la validation physique du transfert en détention.
        MaintainJusticeCustodyPoliceSuppression(player, now);
        RetryJusticeInventoryConfiscationIfDue(player, now);

        // Je qualifie une faute avant de confirmer l'évasion. Les trois secondes
        // hors volume restent donc surveillées : tuer un garde à la dernière
        // frame ne peut pas faire disparaître la victime avec la scène.
        UpdateJusticeCustodyDiscipline(player, now);
        if (_justiceDisciplineActive || _justiceDisciplineIntent != null)
        {
            EnsureJusticeCustodyScene(now);
            ResetJusticeCustodyClock(now);
            return;
        }

        // Je traite la sortie avant le gate Incarcerated : la phase Escaping
        // doit continuer à accumuler ses trois secondes de grâce.
        UpdateJusticeCustodyEscape(player, now);
        if (!JusticeIsCustodyActive ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated)
        {
            ResetJusticeCustodyClock(now);
            return;
        }

        AdvanceJusticeCustodyClock(now);
        UpdateJusticeCustodyActivity(player, now);

        EnsureJusticeCustodyScene(now);

        if (_justiceCaseState.SentenceSeconds <= 0 &&
            _justiceCaseState.Phase == JusticePhase.Incarcerated &&
            !_justiceDisciplineActive)
        {
            CompleteJusticeLegalRelease(player);
        }
    }

    private void ObserveJusticeCustodyDeath(Ped player, int now)
    {
        bool stateChanged = false;
        if (IsJusticeCustodyDeathIdentityCompatible(player) &&
            !_justiceCustodyDeathRebindPending)
        {
            // Je n'autorise une nouvelle identité qu'après avoir réellement
            // observé la mort du détenu lié. Un simple changement de héros
            // ou de modèle vivant reste donc bloqué.
            _justiceCustodyDeathRebindPending = true;
            stateChanged |= RememberJusticeCustodyPlayerSlot();
            stateChanged = true;
        }
        if (!_justiceCustodyWaitingForRespawn)
        {
            _justiceCustodyWaitingForRespawn = true;
            stateChanged = true;
        }
        if (stateChanged)
        {
            JusticeMarkStateDirty();
            _justiceCustodyDeathStatePersistencePending = true;
            _justiceNextCustodyDeathPersistenceRetryAt = 0;
            PersistJusticeCustodyDeathStateBeforeRespawn(now);
        }

        CancelJusticeCustodyActivity(false, now);
        ResetJusticeCustodyClock(now);
    }

    private void ObserveJusticeCustodyDeathDuringSuspension(Ped player)
    {
        if (!IsJusticeCustodyDeathIdentityCompatible(player))
        {
            return;
        }

        bool stateChanged = false;
        if (!_justiceCustodyDeathRebindPending)
        {
            _justiceCustodyDeathRebindPending = true;
            stateChanged = true;
        }
        if (!_justiceCustodyWaitingForRespawn)
        {
            _justiceCustodyWaitingForRespawn = true;
            stateChanged = true;
        }
        stateChanged |= RememberJusticeCustodyPlayerSlot();
        if (stateChanged)
        {
            // Je n'appelle ici aucune native de monde : seul le droit de relier
            // le prochain ped est durci sur disque pendant l'écran de mort.
            JusticeMarkStateDirty();
            _justiceCustodyDeathStatePersistencePending = true;
            _justiceNextCustodyDeathPersistenceRetryAt = 0;
            PersistJusticeCustodyDeathStateBeforeRespawn(GetJusticeRawGameTimeSafe());
        }
    }

    private bool PersistJusticeCustodyDeathStateBeforeRespawn(int now)
    {
        if (!_justiceCustodyDeathStatePersistencePending)
        {
            return true;
        }
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyDeathPersistenceRetryAt))
        {
            return false;
        }

        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            _justiceNextCustodyDeathPersistenceRetryAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyDeathPersistenceRetryMs);
            return false;
        }

        _justiceCustodyDeathStatePersistencePending = false;
        _justiceNextCustodyDeathPersistenceRetryAt = 0;
        return true;
    }

    private void CompleteJusticeCustodyTransfer(Ped player, int now)
    {
        if (!Entity.Exists(player) || player.IsDead || _justiceCaseState == null)
        {
            return;
        }
        bool resumingCustody = _justiceCustodyResumePending;
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyTransferAttemptAt))
        {
            return;
        }
        if (_justiceCustodyTransferStartedAt == 0)
        {
            _justiceCustodyTransferStartedAt = now;
        }

        JusticeCustodySite requiredSite = GetJusticeCustodySiteForSentence(
            _justiceCaseState.SentenceSeconds);
        if (_justiceCustodySite == JusticeCustodySite.None ||
            (_justiceCustodySite == JusticeCustodySite.MissionRow &&
             requiredSite == JusticeCustodySite.Bolingbroke))
        {
            _justiceCustodySite = requiredSite;
        }
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();

        if (layout == null)
        {
            LogWarning("Justice.Detention", "Aucun lieu de détention valide n'a été trouvé.");
            return;
        }

        bool returnToCell = JusticePolicy.ShouldReturnCustodyTransferToCell(
            _justiceCaseState.Phase);
        Vector3 transferPosition = returnToCell
            ? layout.CellPosition
            : layout.ArrivalPosition;
        float transferHeading = returnToCell
            ? layout.CellHeading
            : layout.ArrivalHeading;

        if (resumingCustody)
        {
            // Je tente le nettoyage avant le téléport : un ancien scénario GTA
            // ne doit pas pouvoir lutter contre le déplacement vers la cellule.
            TryClearJusticeCustodyPlayerTasks(player, now);
        }

        if (!StoreJusticeCustodyPlayerState(player))
        {
            HandleJusticeCustodyTransferFailure(player, now);
            return;
        }
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            // Je durcis le snapshot transitoire avant toute confiscation ou
            // téléportation. Une reprise ne peut donc pas adopter nos mutations.
            HandleJusticeCustodyTransferFailure(player, now);
            return;
        }

        if (!_justiceInventoryRemoved && _justiceWeaponSnapshot == null)
        {
            PrepareJusticeInventoryConfiscation(player);
        }
        else if (!_justiceInventoryRemoved)
        {
            // Je reprends un snapshot validé dont le retrait n'avait pas encore
            // été commis. Le verrou et le retry restent actifs après un unload.
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = 0;
            SelectJusticeUnarmedSafe(player);
            JusticeMarkStateDirty();
        }
        else if (_justiceInventoryRemoved)
        {
            // Je réapplique au chargement : RemoveAll est idempotent et évite
            // qu'un arrêt de script ayant rendu provisoirement les armes les garde.
            if (!RemoveJusticePlayerWeaponsSafe(player))
            {
                _justiceWeaponControlsLocked = true;
                _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(now, 1000);
            }
        }

        bool transferred = false;
        try
        {
            _activeInteriorSession = null;
            ClearInteriorRenderingFocusSafe(player);
            TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading);
            transferred = IsJusticeTeleportVerified(player, transferPosition, 8.0f);
        }
        catch (Exception ex)
        {
            LogException("Justice.Transfert", ex);
        }

        if (!transferred)
        {
            // Je vérifie aussi les téléports silencieusement ignorés par GTA :
            // aucune phase Incarcerated ne peut être validée hors de la cour.
            transferred = TryJusticeEmergencyTeleport(
                player,
                transferPosition,
                transferHeading);
        }
        if (transferred && !EnsureJusticeCustodyPlayerMobility(player))
        {
            // Je ne valide jamais une détention dont le ped reste gelé par la
            // transition d'arrestation ou de respawn encore active côté GTA.
            transferred = false;
        }
        if (!transferred)
        {
            bool rollbackRequired = HandleJusticeCustodyTransferFailure(player, now);
            if (!rollbackRequired)
            {
                RestoreJusticeCustodyPlayerTransientState(player);
            }
            return;
        }

        _justiceCustodyPlayerHandle = player.Handle;
        _justiceCustodyPlayerModelHash = GetJusticePedModelHashSafe(player);
        RememberJusticeCustodyPlayerSlot();
        if (_justiceCustodyInitialSentenceSeconds <= 0)
        {
            _justiceCustodyInitialSentenceSeconds = Math.Max(1, _justiceCaseState.SentenceSeconds);
        }
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        bool transferTimedOut = unchecked((uint)(now - _justiceCustodyTransferStartedAt)) >=
                                (uint)JusticeCustodyTransferTimeoutMs;
        ResetJusticeCustodyTransferRetryState();
        _justiceCustodyRuntimeActive = true;
        _justiceCustodyLastTickAt = now;
        _justiceCustodyElapsedRemainderMs = 0;
        _justiceOutsideCustodySinceAt = 0;
        ApplyJusticeTransition(
            transferTimedOut ? JusticeSignal.TransferTimedOut : JusticeSignal.TransferCompleted,
            _justiceCaseState.CustodyEpisodeId);
        if (_justiceCaseState.Phase != JusticePhase.Incarcerated)
        {
            _justiceCaseState.Phase = JusticePhase.Incarcerated;
        }

        if (resumingCustody && _justiceActivityTaskClearPending)
        {
            // Je retente après le déplacement si la première native a échoué.
            RetryJusticeCustodyActivityTaskClear(player, now);
        }

        // Une capture met fin à la poursuite en cours. Je ne maintiens jamais
        // d'étoiles en détention, afin que les forces ambiantes n'exécutent pas
        // le joueur dans la cour avant même un éventuel incident disciplinaire.
        ClearJusticeWantedLevelOnce();
        SetJusticeCustodyPoliceSuppression(true);
        ApplyLoadedJusticeActivityCooldowns(now);
        EnsureJusticeCustodyRelationshipGroups();
        EnsureJusticeCustodyScene(now);

        JusticeOperation enterOperation = CreateJusticeCustodyOperation(JusticeOperationKind.EnterCustody);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, enterOperation);
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        ShowStatus(layout.DisplayName + " : peine à purger, activités disponibles avec E.", 5500);
        LogInfo("Justice.Detention", "Entrée dans " + layout.DisplayName + ".");
    }

    private void RestoreJusticeCustodyRuntimeFromCase()
    {
        _justiceCustodyRuntimeActive = true;
        _justiceCustodyResumePending = true;
        _justiceCustodyTransferPending = false;
        ResetJusticeCustodyTransferRetryState();

        if (_justiceCustodySite == JusticeCustodySite.None)
        {
            _justiceCustodySite = GetJusticeCustodySiteForSentence(
                _justiceCaseState.SentenceSeconds);
        }

        if (_justiceCustodyInitialSentenceSeconds <= 0)
        {
            _justiceCustodyInitialSentenceSeconds = Math.Max(1, _justiceCaseState.SentenceSeconds);
        }
    }

    private static JusticeCustodySite GetJusticeCustodySiteForSentence(int sentenceSeconds)
    {
        return sentenceSeconds >= JusticeCustodyPrisonThresholdSeconds
            ? JusticeCustodySite.Bolingbroke
            : JusticeCustodySite.MissionRow;
    }

    private bool ScheduleJusticeBolingbrokeTransferIfRequired(int now)
    {
        if (_justiceCaseState == null ||
            _justiceCustodySite != JusticeCustodySite.MissionRow ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated ||
            _justiceCaseState.SentenceSeconds < JusticeCustodyPrisonThresholdSeconds ||
            _justiceDisciplineIntent != null || _justiceDisciplineActive ||
            _justiceCustodyTransferPending || _justiceCustodyResumePending)
        {
            return false;
        }

        CancelJusticeCustodyActivity(false, now);
        Dictionary<string, int> previousCooldowns =
            new Dictionary<string, int>(_justiceActivityCooldownUntil, StringComparer.Ordinal);
        Dictionary<string, int> previousLoadedCooldowns =
            new Dictionary<string, int>(_justiceLoadedActivityCooldownSeconds, StringComparer.Ordinal);
        _justiceActivityCooldownUntil.Clear();
        _justiceLoadedActivityCooldownSeconds.Clear();
        JusticePhase previousPhase = _justiceCaseState.Phase;
        _justiceCustodySite = JusticeCustodySite.Bolingbroke;
        _justiceCustodyTransferPending = true;
        _justiceCustodyResumePending = false;
        _justiceOutsideCustodySinceAt = 0;
        _justiceCaseState.Phase = JusticePhase.Transporting;
        ResetJusticeCustodyTransferRetryState();
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceCustodySite = JusticeCustodySite.MissionRow;
            _justiceCustodyTransferPending = false;
            _justiceCaseState.Phase = previousPhase;
            foreach (KeyValuePair<string, int> pair in previousCooldowns)
            {
                _justiceActivityCooldownUntil[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, int> pair in previousLoadedCooldowns)
            {
                _justiceLoadedActivityCooldownSeconds[pair.Key] = pair.Value;
            }
            JusticeMarkStateDirty();
            return false;
        }

        // Je retire seulement la scène du poste après le précommit. La suppression
        // police reste active et suivie pendant le transfert vers la prison.
        CleanupJusticeCustodySceneEntitiesAndGroups();
        ShowStatus("Justice : peine portée à cinq minutes, transfert à Bolingbroke.", 4200);
        LogInfo("Justice.Transfert", "Mission Row reclassé vers Bolingbroke à cinq minutes ou plus.");
        return true;
    }

    private void ResetJusticeCustodyTransferRetryState()
    {
        _justiceCustodyTransferStartedAt = 0;
        _justiceNextCustodyTransferAttemptAt = 0;
        _justiceCustodyTransferFailureCount = 0;
        _justiceCustodyTransferTimeoutLogged = false;
    }

    private void RegisterJusticeCustodyTransferFailure(int now)
    {
        _justiceCustodyTransferFailureCount = Math.Min(16, _justiceCustodyTransferFailureCount + 1);
        int exponent = Math.Min(3, Math.Max(0, _justiceCustodyTransferFailureCount - 1));
        int retryDelay = Math.Min(
            JusticeCustodyTransferMaximumRetryMs,
            JusticeCustodyTransferInitialRetryMs * (1 << exponent));
        _justiceNextCustodyTransferAttemptAt = JusticeCustodyFutureTime(now, retryDelay);

        uint elapsed = unchecked((uint)(now - _justiceCustodyTransferStartedAt));
        if (elapsed >= (uint)JusticeCustodyTransferTimeoutMs &&
            !_justiceCustodyTransferTimeoutLogged)
        {
            _justiceCustodyTransferTimeoutLogged = true;
            ShowStatus(
                "Justice : transfert retardé, nouvelle tentative sécurisée dans quelques secondes.",
                4500);
            LogWarning(
                "Justice.Transfert",
                "Délai de transfert dépassé; secours vérifié et retries bornés actifs.");
        }
    }

    private bool HandleJusticeCustodyTransferFailure(Ped player, int now)
    {
        RegisterJusticeCustodyTransferFailure(now);
        uint elapsed = unchecked((uint)(now - _justiceCustodyTransferStartedAt));
        if (elapsed < (uint)JusticeCustodyTransferTimeoutMs)
        {
            return false;
        }

        // Je déclenche le même rollback durable quel que soit le palier qui a
        // échoué : snapshot, précommit ou téléportation. Si le disque bloque
        // encore la transaction, je rends néanmoins le ped mobile dans cette
        // session et le WAL reprendra avant toute autre mutation de détention.
        if (!TryRollbackJusticeCustodyTransfer(player, now))
        {
            EnsureJusticeCustodyPlayerMobility(player);
        }
        return true;
    }

    private bool TryRollbackJusticeCustodyTransfer(Ped player, int now)
    {
        if (_justiceCaseState == null || !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        JusticeOperation rollback = CreateJusticeCustodyOperation(JusticeOperationKind.TransferRollback);
        if (!HasJusticeOperation(rollback.Kind, rollback.EpisodeId))
        {
            if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, rollback))
            {
                return false;
            }
            _justiceCustodyTransferRollbackPrecommitRedundant = false;
        }

        _justiceCustodyTransferRollbackFinalizationPending = true;
        if (!EnsureJusticeCustodyTransferRollbackPrecommitRedundant())
        {
            return false;
        }
        return ResumeJusticeCustodyTransferRollback(player, now);
    }

    private bool EnsureJusticeCustodyTransferRollbackPrecommitRedundant()
    {
        if (!_justiceCustodyTransferRollbackFinalizationPending ||
            _justiceCaseState == null)
        {
            return false;
        }
        if (_justiceCustodyTransferRollbackPrecommitRedundant)
        {
            return true;
        }

        // Si seule la première écriture a réussi, je conserve l'opération et
        // reprends sa duplication. Je ne restitue jamais l'inventaire sous un
        // WAL présent uniquement dans le primaire.
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        _justiceCustodyTransferRollbackPrecommitRedundant = true;
        return true;
    }

    private bool ResumeJusticeCustodyTransferRollback(Ped player, int now)
    {
        if (_justiceCaseState == null)
        {
            return false;
        }
        if (!_justiceCustodyTransferRollbackFinalizationPending &&
            HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback))
        {
            // Après chargement, l'opération XML recrée le latch runtime. Le
            // cache redondant reste faux pour forcer une nouvelle duplication.
            _justiceCustodyTransferRollbackFinalizationPending = true;
            _justiceCustodyTransferRollbackPrecommitRedundant = false;
        }

        bool alreadyPreparedInMemory =
            _justiceCustodyTransferRollbackFinalizationPending &&
            _justiceCaseState.Phase == JusticePhase.AtLarge &&
            string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId);
        if (!alreadyPreparedInMemory &&
            (!Entity.Exists(player) || player.IsDead ||
             !IsJusticeCustodyPlayerIdentityCompatible(player)))
        {
            return false;
        }
        if (!alreadyPreparedInMemory &&
            !HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback))
        {
            return false;
        }
        if (!EnsureJusticeCustodyTransferRollbackPrecommitRedundant())
        {
            return false;
        }

        // Je rends d'abord l'inventaire sous le dossier encore en phase transport.
        // Le XML intermédiaire reste donc cohérent même si le processus s'arrête
        // avant la création du mandat de reprise.
        if (!alreadyPreparedInMemory &&
            (!RestoreJusticeInventoryForLegalRelease(player, now) ||
             !JusticeFlushStateNow()))
        {
            return false;
        }

        if (!alreadyPreparedInMemory)
        {
            bool disciplineEnded = EndJusticeCustodyDiscipline(player);
            bool transientStateRestored =
                RestoreJusticeCustodyPlayerTransientStateForRollback(player);
            if (!disciplineEnded || !transientStateRestored)
            {
                return false;
            }
        }

        if (!alreadyPreparedInMemory)
        {
            CleanupJusticeCustodyEntitiesAndGroups();
            _justiceCustodyPlayerStateStored = false;
            string closedCustodyEpisode = _justiceCaseState.CustodyEpisodeId;
            ResetJusticeCustodyPersistentFields();
            JusticePolicy.PruneClosedCustodyOperations(_justiceCaseState, closedCustodyEpisode);
            _justiceCaseState.Phase = JusticePhase.AtLarge;
            _justiceCaseState.HasWarrant = true;
            _justiceCaseState.CustodyEpisodeId = string.Empty;
            _justicePursuitActive = false;
            _justiceWantedEpisodeStartedAtMs = 0L;
            OpenJusticeDetectionEpisodeAfterPursuitLoss();
            _justiceCustodyTransferRollbackFinalizationPending = true;
        }

        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        _justiceCustodyTransferRollbackFinalizationPending = false;
        _justiceCustodyTransferRollbackPrecommitRedundant = false;
        ClearJusticeWantedLevelOnce();
        ShowStatus(
            "Justice : transfert impossible, remise en liberté technique sous mandat.",
            5500);
        LogWarning(
            "Justice.Transfert",
            "Transfert annulé après timeout; inventaire rendu et dossier conservé sous mandat.");
        return true;
    }

    private void EnsureJusticeCustodyEpisodeId()
    {
        if (_justiceCaseState == null || !string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
        {
            return;
        }

        string wantedEpisode = _justiceCaseState.WantedEpisodeId;
        _justiceCaseState.CustodyEpisodeId = string.IsNullOrWhiteSpace(wantedEpisode)
            ? "custody:" + Guid.NewGuid().ToString("N")
            : "custody:" + wantedEpisode.Trim();
    }

    private JusticeOperation CreateJusticeCustodyOperation(JusticeOperationKind kind)
    {
        string episode = _justiceCaseState == null
            ? string.Empty
            : _justiceCaseState.CustodyEpisodeId;
        string operationId = JusticePolicy.CreateOperationId(kind, episode);
        return new JusticeOperation(operationId, kind, episode);
    }

    private bool HasJusticeCustodyOperation(JusticeOperationKind kind)
    {
        if (_justiceCaseState == null || string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
        {
            return false;
        }

        string operationId = JusticePolicy.CreateOperationId(
            kind,
            _justiceCaseState.CustodyEpisodeId);
        for (int index = 0; index < _justiceCaseState.CompletedOperationIds.Count; index++)
        {
            if (string.Equals(
                _justiceCaseState.CompletedOperationIds[index],
                operationId,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasJusticeOperation(JusticeOperationKind kind, string episodeId)
    {
        if (_justiceCaseState == null || string.IsNullOrWhiteSpace(episodeId))
        {
            return false;
        }

        string operationId = JusticePolicy.CreateOperationId(kind, episodeId);
        for (int index = 0; index < _justiceCaseState.CompletedOperationIds.Count; index++)
        {
            if (string.Equals(
                _justiceCaseState.CompletedOperationIds[index],
                operationId,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool JusticeCollectFineAndConvertDetention(bool stationPlanned, string operationSuffix)
    {
        if (_justiceCaseState == null)
        {
            return false;
        }

        if (_justiceFineDebitIntent != null)
        {
            return ResumeJusticeFineDebitIntent();
        }

        if (_justiceCaseState.FineDue <= 0L)
        {
            ResetJusticeFineCashReadRetry();
            return true;
        }

        JusticeOperation operation;
        if (string.IsNullOrWhiteSpace(operationSuffix))
        {
            operation = CreateJusticeCustodyOperation(JusticeOperationKind.ApplyFine);
        }
        else
        {
            // TryRegisterOperation n'accepte que les identifiants canoniques.
            // Je donne donc à chaque nouvelle amende disciplinaire son propre
            // sous-épisode persistant au lieu de bricoler l'identifiant final.
            string fineEpisode = _justiceCaseState.CustodyEpisodeId +
                ":fine:" + operationSuffix.Trim();
            operation = new JusticeOperation(
                JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, fineEpisode),
                JusticeOperationKind.ApplyFine,
                fineEpisode);
        }

        if (HasJusticeOperation(operation.Kind, operation.EpisodeId))
        {
            _justiceCaseState.FineDue = 0L;
            JusticeMarkStateDirty();
            return JusticeFlushStateNow();
        }

        long fine = Math.Min(JusticePolicy.MaxActiveFine, Math.Max(0L, _justiceCaseState.FineDue));
        int slot = GetCurrentSinglePlayerCashSlotSafe();
        if (slot < 0)
        {
            // Slot inconnu : je ne touche à aucun des trois protagonistes et je
            // convertis toute l'amende, comme le contrat Justice le demande.
            ResetJusticeFineCashReadRetry();
            _justiceCaseState.FineDue = 0L;
            _justiceCaseState.SentenceSeconds = CalculateJusticeSentenceAfterFineConversion(
                _justiceCaseState.SentenceSeconds,
                fine,
                stationPlanned);
            JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
            JusticeMarkStateDirty();
            return JusticeFlushStateNow();
        }

        int now = Game.GameTime;
        if (!JusticeCustodyHasReached(now, _justiceNextFineCashReadAttemptAt))
        {
            return false;
        }
        _justiceNextFineCashReadAttemptAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyFineCashReadRetryMs);

        int currentCash = 0;
        bool cashPlanPrepared = TryReadJusticeSinglePlayerCash(slot, out currentCash);
        long preparedAtUtcTicks = DateTime.UtcNow.Ticks;
        if (!cashPlanPrepared)
        {
            // Je persiste aussi l'attente de lecture : après reload, son délai
            // reste borné et aucune écriture cash n'est autorisée entre-temps.
            if (!_justiceFineCashReadFailureLogged)
            {
                _justiceFineCashReadFailureLogged = true;
                ShowStatus("Justice : lecture de l'amende différée, nouvelle tentative…", 3200);
                LogWarning(
                    "Justice.Amende",
                    "Lecture du cash momentanément indisponible; aucun débit ni conversion appliqué.");
            }
        }
        else
        {
            ResetJusticeFineCashReadRetry();
        }

        long plannedDebit = cashPlanPrepared
            ? Math.Min(fine, Math.Max(0, currentCash))
            : 0L;
        long unpaid = fine - plannedDebit;

        _justiceFineDebitIntent = new JusticeFineDebitIntent
        {
            EpisodeId = operation.EpisodeId,
            Slot = slot,
            FineAmount = fine,
            CashPlanPrepared = cashPlanPrepared,
            PreparedAtUtcTicks = preparedAtUtcTicks,
            DebitAmount = (int)plannedDebit,
            CashBefore = currentCash,
            CashAfter = currentCash - (int)plannedDebit,
            SentenceIfDebited = CalculateJusticeSentenceAfterFineConversion(
                _justiceCaseState.SentenceSeconds,
                unpaid,
                stationPlanned),
            SentenceIfConverted = CalculateJusticeSentenceAfterFineConversion(
                _justiceCaseState.SentenceSeconds,
                fine,
                stationPlanned),
            StationPlanned = stationPlanned
        };
        JusticeMarkStateDirty();

        // Je persiste l'intention avant tout effet externe. Chaque reprise refait
        // ce flush avant de lire ou d'écrire le cash, y compris dans ce même tick.
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        return ResumeJusticeFineDebitIntent();
    }

    private bool ResumeJusticeFineDebitIntent()
    {
        JusticeFineDebitIntent intent = _justiceFineDebitIntent;
        if (_justiceCaseState == null || intent == null ||
            string.IsNullOrWhiteSpace(intent.EpisodeId))
        {
            return intent == null;
        }

        JusticeOperation operation = new JusticeOperation(
            JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, intent.EpisodeId),
            JusticeOperationKind.ApplyFine,
            intent.EpisodeId);
        if (HasJusticeOperation(operation.Kind, operation.EpisodeId))
        {
            // Le commit final est autoritaire. Je le reflushe avant de nettoyer
            // l'intention afin qu'un précédent échec disque ne soit jamais masqué.
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
            {
                return false;
            }

            _justiceFineDebitIntent = null;
            ResetJusticeFineCashReadRetry();
            JusticeMarkStateDirty();
            return true;
        }

        Ped player = Game.Player.Character;
        if (!Entity.Exists(player) || player.IsDead ||
            GetCurrentSinglePlayerCashSlotSafe() != intent.Slot ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        int fineReadNow = Game.GameTime;
        if (!JusticeCustodyHasReached(fineReadNow, _justiceNextFineCashReadAttemptAt))
        {
            return false;
        }
        _justiceNextFineCashReadAttemptAt = JusticeCustodyFutureTime(
            fineReadNow,
            JusticeCustodyFineCashReadRetryMs);

        // Je réaffirme le précommit à chaque reprise. Si le primaire et son backup
        // sont indisponibles, aucun débit GTA ne peut partir.
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        int finalSentence = intent.SentenceIfDebited;
        int cash = 0;
        bool resolvedWithoutCash = false;
        bool cashRead = false;
        if (intent.DebitAttempted &&
            intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
        {
            // Je fais confiance au résultat natif déjà durci plutôt qu'à une
            // variation de solde ultérieure qui pourrait créer un faux échec.
            finalSentence = intent.SentenceIfDebited;
            resolvedWithoutCash = true;
        }
        else if (intent.DebitAttempted &&
                 intent.CashWriteResult == JusticeCashWriteResult.Rejected)
        {
            // Un rejet explicite n'est jamais assimilé à un paiement : toute
            // l'amende reste due et est convertie sans réémettre STAT_SET_INT.
            finalSentence = intent.SentenceIfConverted;
            resolvedWithoutCash = true;
        }
        else
        {
            cashRead = TryReadJusticeSinglePlayerCash(intent.Slot, out cash);
        }
        if (!intent.CashPlanPrepared)
        {
            if (!cashRead)
            {
                if (!JusticePolicy.HasFineDebitPreparationTimedOut(
                    intent.PreparedAtUtcTicks,
                    DateTime.UtcNow.Ticks))
                {
                    return false;
                }

                // Après un délai durable, je choisis le fallback sans SET : le
                // jugement avance, mais aucun compte de protagoniste n'est touché.
                finalSentence = intent.SentenceIfConverted;
                resolvedWithoutCash = true;
                ResetJusticeFineCashReadRetry();
                ShowStatus(
                    "Justice : cash inaccessible, amende convertie sans débit.",
                    4200);
                LogWarning(
                    "Justice.Amende",
                    "Préparation cash expirée; conversion complète sans écriture GTA.");
            }
            else
            {
                long plannedDebit = Math.Min(intent.FineAmount, Math.Max(0, cash));
                intent.CashPlanPrepared = true;
                intent.DebitAmount = (int)plannedDebit;
                intent.CashBefore = Math.Max(0, cash);
                intent.CashAfter = intent.CashBefore - intent.DebitAmount;
                intent.SentenceIfDebited = CalculateJusticeSentenceAfterFineConversion(
                    _justiceCaseState.SentenceSeconds,
                    intent.FineAmount - plannedDebit,
                    intent.StationPlanned);
                intent.SentenceIfConverted = CalculateJusticeSentenceAfterFineConversion(
                    _justiceCaseState.SentenceSeconds,
                    intent.FineAmount,
                    intent.StationPlanned);
                finalSentence = intent.SentenceIfDebited;
                JusticeMarkStateDirty();
                if (!PersistJusticeCriticalPrecommitRedundantly())
                {
                    return false;
                }
                ResetJusticeFineCashReadRetry();
            }
        }

        if (!resolvedWithoutCash && !cashRead && !intent.DebitAttempted)
        {
            if (!JusticePolicy.HasFineDebitPreparationTimedOut(
                intent.PreparedAtUtcTicks,
                DateTime.UtcNow.Ticks))
            {
                return false;
            }
            finalSentence = intent.SentenceIfConverted;
            resolvedWithoutCash = true;
            ResetJusticeFineCashReadRetry();
            ShowStatus("Justice : cash inaccessible, amende convertie sans débit.", 4200);
            LogWarning(
                "Justice.Amende",
                "Lecture cash expirée avant SET; conversion complète sans écriture GTA.");
        }
        else if (!resolvedWithoutCash && !cashRead)
        {
            if (!JusticePolicy.HasFineDebitAttemptTimedOut(
                intent.AttemptedAtUtcTicks,
                DateTime.UtcNow.Ticks))
            {
                return false;
            }
            ShowStatus(
                "Justice : débit impossible à relire, considéré déjà appliqué pour éviter un double paiement.",
                5200);
            LogWarning(
                "Justice.Amende",
                "Réconciliation expirée sans lecture; débit présumé appliqué (at-most-once)." );
            ResetJusticeFineCashReadRetry();
        }
        else if (!resolvedWithoutCash && intent.DebitAttempted && cash == intent.CashAfter)
        {
            // CashAfter ne prouve le débit qu'après le précommit Attempted. Avant
            // celui-ci, une variation externe identique doit encore être rebasée.
            intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
            finalSentence = intent.SentenceIfDebited;
            resolvedWithoutCash = true;
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }
        }
        else if (!resolvedWithoutCash && !intent.DebitAttempted)
        {
            ResetJusticeFineCashReadRetry();
            if (cash != intent.CashBefore)
            {
                // Tant qu'aucune écriture n'est autorisée, un solde tiers est sûr à
                // rebaser. Je persiste le nouveau plan avant son unique tentative.
                int previousDebit = intent.DebitAmount;
                int previousCashBefore = intent.CashBefore;
                int previousCashAfter = intent.CashAfter;
                int previousSentenceIfDebited = intent.SentenceIfDebited;
                int previousSentenceIfConverted = intent.SentenceIfConverted;
                long plannedDebit = Math.Min(intent.FineAmount, Math.Max(0, cash));
                intent.DebitAmount = (int)plannedDebit;
                intent.CashBefore = Math.Max(0, cash);
                intent.CashAfter = intent.CashBefore - intent.DebitAmount;
                intent.SentenceIfDebited = CalculateJusticeSentenceAfterFineConversion(
                    _justiceCaseState.SentenceSeconds,
                    intent.FineAmount - plannedDebit,
                    intent.StationPlanned);
                intent.SentenceIfConverted = CalculateJusticeSentenceAfterFineConversion(
                    _justiceCaseState.SentenceSeconds,
                    intent.FineAmount,
                    intent.StationPlanned);
                JusticeMarkStateDirty();
                if (!PersistJusticeCriticalPrecommitRedundantly())
                {
                    intent.DebitAmount = previousDebit;
                    intent.CashBefore = previousCashBefore;
                    intent.CashAfter = previousCashAfter;
                    intent.SentenceIfDebited = previousSentenceIfDebited;
                    intent.SentenceIfConverted = previousSentenceIfConverted;
                    JusticeMarkStateDirty();
                    return false;
                }
            }

            if (intent.DebitAmount <= 0)
            {
                finalSentence = intent.SentenceIfDebited;
            }
            else
            {
                // DebitAttempted est le jeton at-most-once. Je le durcis avant la
                // seule écriture externe; aucune reprise Attempted ne réémettra SET.
                intent.DebitAttempted = true;
                intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks;
                intent.CashWriteResult = JusticeCashWriteResult.Unknown;
                JusticeMarkStateDirty();
                if (!PersistJusticeCriticalPrecommitRedundantly())
                {
                    intent.DebitAttempted = false;
                    intent.AttemptedAtUtcTicks = 0L;
                    intent.CashWriteResult = JusticeCashWriteResult.Unknown;
                    JusticeMarkStateDirty();
                    return false;
                }

                intent.CashWriteResult = TryWriteJusticeSinglePlayerCash(intent.Slot, intent.CashAfter);
                JusticeMarkStateDirty();
                if (!PersistJusticeCriticalPrecommitRedundantly())
                {
                    // Je conserve le résultat en mémoire : le tick suivant le
                    // durcira avant toute résolution et ne réémettra jamais SET.
                    return false;
                }

                if (intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
                {
                    finalSentence = intent.SentenceIfDebited;
                    resolvedWithoutCash = true;
                }
                else if (intent.CashWriteResult == JusticeCashWriteResult.Rejected)
                {
                    finalSentence = intent.SentenceIfConverted;
                    resolvedWithoutCash = true;
                    ShowStatus(
                        "Justice : débit refusé, amende convertie en détention.",
                        4200);
                    LogWarning(
                        "Justice.Amende",
                        "STAT_SET_INT a rejeté le débit; conversion complète sans paiement présumé.");
                }
                else
                {
                    int cashAfterWrite;
                    bool readAccepted = TryReadJusticeSinglePlayerCash(
                        intent.Slot,
                        out cashAfterWrite);
                    if (!readAccepted || cashAfterWrite != intent.CashAfter)
                    {
                        // Une exception native laisse un résultat Unknown. Je ne
                        // rejoue jamais l'écriture : la réconciliation bornée
                        // décidera ensuite sans double débit.
                        _justiceNextFineCashReadAttemptAt = JusticeCustodyFutureTime(
                            Game.GameTime,
                            JusticeCustodyFineCashReadRetryMs);
                        return false;
                    }

                    intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
                    finalSentence = intent.SentenceIfDebited;
                    resolvedWithoutCash = true;
                    JusticeMarkStateDirty();
                    if (!PersistJusticeCriticalPrecommitRedundantly())
                    {
                        return false;
                    }
                }
            }
        }
        else if (!resolvedWithoutCash)
        {
            // Attempted est irréversible : CashBefore peut être un ABA après crash.
            // Je n'écris plus jamais et j'attends un délai persistant, puis je
            // privilégie explicitement l'absence de double débit/double peine.
            if (!JusticePolicy.HasFineDebitAttemptTimedOut(
                intent.AttemptedAtUtcTicks,
                DateTime.UtcNow.Ticks))
            {
                return false;
            }
            ResetJusticeFineCashReadRetry();
            ShowStatus(
                "Justice : solde ambigu, débit considéré déjà appliqué pour éviter un double paiement.",
                5200);
            LogWarning(
                "Justice.Amende",
                "Réconciliation expirée sur solde ambigu; débit présumé appliqué (at-most-once)." );
        }

        _justiceCaseState.FineDue = 0L;
        _justiceCaseState.SentenceSeconds = Math.Max(
            0,
            Math.Min(JusticeCustodyMaximumSentenceSeconds, finalSentence));
        JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        _justiceFineDebitIntent = null;
        ResetJusticeFineCashReadRetry();
        JusticeMarkStateDirty();
        return true;
    }

    private void ResetJusticeFineCashReadRetry()
    {
        _justiceNextFineCashReadAttemptAt = 0;
        _justiceFineCashReadFailureLogged = false;
    }

    private bool TryReadJusticeSinglePlayerCash(int slot, out int cash)
    {
        cash = 0;
        if (slot < 0 || slot > 2)
        {
            return false;
        }

        try
        {
            if (_justiceCashReadOverride != null)
            {
                int? overridden = _justiceCashReadOverride(slot);
                if (!overridden.HasValue)
                {
                    return false;
                }
                cash = Math.Max(0, overridden.Value);
                return true;
            }
        }
        catch
        {
            cash = 0;
            return false;
        }

        try
        {
            int statHash = Game.GenerateHash(
                "SP" + slot.ToString(CultureInfo.InvariantCulture) + "_TOTAL_CASH");
            OutputArgument output = new OutputArgument();
            bool result = Function.Call<bool>((Hash)NativeStatGetInt, statHash, output, -1);
            if (!result)
            {
                return false;
            }

            cash = Math.Max(0, output.GetResult<int>());
            return true;
        }
        catch
        {
            cash = 0;
            return false;
        }
    }

    private JusticeCashWriteResult TryWriteJusticeSinglePlayerCash(int slot, int cash)
    {
        if (slot < 0 || slot > 2)
        {
            return JusticeCashWriteResult.Rejected;
        }

        try
        {
            if (_justiceCashWriteOverride != null)
            {
                bool? overridden = _justiceCashWriteOverride(slot, Math.Max(0, cash));
                return !overridden.HasValue
                    ? JusticeCashWriteResult.Unknown
                    : (overridden.Value
                        ? JusticeCashWriteResult.Succeeded
                        : JusticeCashWriteResult.Rejected);
            }
        }
        catch
        {
            return JusticeCashWriteResult.Unknown;
        }

        try
        {
            int statHash = Game.GenerateHash(
                "SP" + slot.ToString(CultureInfo.InvariantCulture) + "_TOTAL_CASH");
            bool accepted = Function.Call<bool>(
                (Hash)NativeStatSetInt,
                statHash,
                Math.Max(0, cash),
                true);
            return accepted
                ? JusticeCashWriteResult.Succeeded
                : JusticeCashWriteResult.Rejected;
        }
        catch
        {
            return JusticeCashWriteResult.Unknown;
        }
    }

    private void AddJusticeFineConversionTime(long unpaidFine, bool stationPlanned)
    {
        if (_justiceCaseState == null || unpaidFine <= 0L)
        {
            return;
        }

        _justiceCaseState.SentenceSeconds = CalculateJusticeSentenceAfterFineConversion(
            _justiceCaseState.SentenceSeconds,
            unpaidFine,
            stationPlanned);
    }

    private static int CalculateJusticeSentenceAfterFineConversion(
        int currentSentence,
        long unpaidFine,
        bool stationPlanned)
    {
        if (unpaidFine <= 0L)
        {
            return Math.Max(0, currentSentence);
        }

        long seconds = (unpaidFine + JusticeCustodyFineDollarsPerSecond - 1L) /
                       JusticeCustodyFineDollarsPerSecond;
        seconds = RoundJusticeCustodySecondsUp(seconds, 15);
        seconds = Math.Max(30L, Math.Min(JusticeCustodyFineConversionMaximumSeconds, seconds));
        int maximumSentence = stationPlanned
            ? 5 * 60
            : JusticeCustodyMaximumSentenceSeconds;
        return JusticeCustodySaturatingAdd(
            currentSentence,
            (int)seconds,
            maximumSentence);
    }

    private static long RoundJusticeCustodySecondsUp(long seconds, int quantum)
    {
        if (seconds <= 0L || quantum <= 1)
        {
            return Math.Max(0L, seconds);
        }

        long remainder = seconds % quantum;
        return remainder == 0L ? seconds : seconds + quantum - remainder;
    }

    private static int JusticeCustodySaturatingAdd(int current, int addition, int maximum)
    {
        current = Math.Max(0, current);
        if (addition <= 0)
        {
            return Math.Min(maximum, current);
        }

        return current >= maximum || addition > maximum - current
            ? maximum
            : current + addition;
    }

    private static bool JusticeCustodyHasReached(int now, int target)
    {
        return target == 0 || unchecked(now - target) >= 0;
    }

    private static int JusticeCustodyFutureTime(int now, int delayMilliseconds)
    {
        return unchecked(now + Math.Max(0, delayMilliseconds));
    }

    private static int JusticeCustodyMillisecondsUntil(int now, int target)
    {
        if (JusticeCustodyHasReached(now, target))
        {
            return 0;
        }

        uint remaining = unchecked((uint)(target - now));
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }

    private bool StoreJusticeCustodyPlayerState(Ped player)
    {
        if (_justiceCustodyPlayerStateStored)
        {
            return true;
        }
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            int modelHash = GetJusticePedModelHashSafe(player);
            if (modelHash == 0)
            {
                return false;
            }
            _justiceCustodyStoredInvincible = player.IsInvincible;
            _justiceCustodyStoredFrozen = player.FreezePosition;
            _justiceCustodyStoredCanRagdoll = player.CanRagdoll;
            _justiceCustodyPlayerHandle = player.Handle;
            _justiceCustodyPlayerModelHash = modelHash;
            RememberJusticeCustodyPlayerSlot();
            _justiceCustodyPlayerStateStored = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryBindJusticeCustodyPlayerIdentityForCapture(
        Ped player,
        bool deathCapture)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int trustedSlot = JusticePolicy.ResolveTrustedCanonicalPlayerSlot(
            currentSlot,
            _justiceLastCanonicalPlayerSlot);
        if (trustedSlot < 0)
        {
            // Le modèle custom seul ne prouve jamais quel compte de protagoniste
            // pourrait être débité ou quel inventaire pourrait être désarmé.
            return false;
        }
        if (_justiceCustodyPlayerSlot >= 0 &&
            trustedSlot != _justiceCustodyPlayerSlot)
        {
            return false;
        }

        int modelHash = GetJusticePedModelHashSafe(player);
        if (modelHash == 0)
        {
            return false;
        }

        if (_justiceCustodyPlayerModelHash != 0 &&
            _justiceCustodyPlayerModelHash != modelHash)
        {
            return false;
        }

        _justiceCustodyPlayerHandle = player.Handle;
        _justiceCustodyPlayerModelHash = modelHash;
        if (trustedSlot >= 0)
        {
            _justiceCustodyPlayerSlot = trustedSlot;
        }
        return true;
    }

    private bool TryRebindJusticeCustodyIdentityAfterRespawn(Ped player)
    {
        if (!CanRebindJusticeCustodyIdentityAfterInitialRespawn() ||
            !Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (_justiceFineDebitIntent != null &&
            !JusticePolicy.CanRebindCustodyFineIntentSlot(
                currentSlot,
                _justiceFineDebitIntent.Slot,
                _justiceCustodyPlayerSlot))
        {
            // Une intention financière déjà liée à un slot interdit tout passage
            // silencieux vers le compte d'un autre protagoniste. Le -1 d'une
            // tenue custom reste permis seulement si l'intention vise le slot
            // canonique déjà attaché à cette détention.
            return false;
        }

        if (!JusticePolicy.CanRebindCustodyRespawnSlot(
                _justiceCustodyPlayerSlot,
                currentSlot,
                _justiceLastCanonicalPlayerSlot,
                _justiceActivePlayerProfileSlot,
                _justiceCustodyDeathRebindPending))
        {
            // Je peux reconnaître une tenue custom après une mort uniquement
            // grâce au profil canonique déjà observé. Un autre héros connu ne
            // reçoit jamais la peine, le snapshot ou la dette de son voisin.
            return false;
        }

        int modelHash = GetJusticePedModelHashSafe(player);
        if (modelHash == 0)
        {
            return false;
        }

        if (_justiceCustodyDeathRebindPending)
        {
            // Je réaffirme l'intention avec l'ancienne identité avant d'accepter
            // un nouveau ped. Un debounce ou un échec disque ne peut donc pas
            // transformer une liaison mémoire en mutation GTA non durable.
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }
        }

        int previousHandle = _justiceCustodyPlayerHandle;
        int previousModelHash = _justiceCustodyPlayerModelHash;
        int previousSlot = _justiceCustodyPlayerSlot;
        bool previousDeathRebindPending = _justiceCustodyDeathRebindPending;
        _justiceCustodyPlayerHandle = player.Handle;
        _justiceCustodyPlayerModelHash = modelHash;
        _justiceCustodyDeathRebindPending = false;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            _justiceCustodyPlayerHandle = previousHandle;
            _justiceCustodyPlayerModelHash = previousModelHash;
            _justiceCustodyPlayerSlot = previousSlot;
            _justiceCustodyDeathRebindPending = previousDeathRebindPending;
            JusticeMarkStateDirty();
            return false;
        }

        LogInfo("Justice.Detention", "Identité du protagoniste reliée après son respawn.");
        return true;
    }

    private bool CanRebindJusticeCustodyIdentityAfterInitialRespawn()
    {
        return _justiceCustodyWaitingForRespawn &&
               (_justiceCustodyDeathRebindPending ||
                (!_justiceInventoryRemoved &&
                 !_justiceWeaponControlsLocked &&
                 _justiceWeaponSnapshot == null &&
                 !_justiceCustodyPlayerStateStored));
    }

    private bool RememberJusticeCustodyPlayerSlot()
    {
        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (currentSlot >= 0 && currentSlot != _justiceCustodyPlayerSlot)
        {
            _justiceCustodyPlayerSlot = currentSlot;
            return true;
        }
        return false;
    }

    private bool RestoreJusticeCustodyPlayerTransientState(Ped player)
    {
        if (!_justiceCustodyPlayerStateStored)
        {
            return true;
        }
        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        bool restored = true;
        try
        {
            player.IsInvincible = _justiceCustodyStoredInvincible;
            restored &= player.IsInvincible == _justiceCustodyStoredInvincible;
        }
        catch
        {
            restored = false;
        }
        try
        {
            player.FreezePosition = _justiceCustodyStoredFrozen;
            restored &= player.FreezePosition == _justiceCustodyStoredFrozen;
        }
        catch
        {
            restored = false;
        }
        try
        {
            player.CanRagdoll = _justiceCustodyStoredCanRagdoll;
            restored &= player.CanRagdoll == _justiceCustodyStoredCanRagdoll;
        }
        catch
        {
            restored = false;
        }
        return restored;
    }

    private bool RestoreJusticeCustodyPlayerTransientStateForRollback(Ped player)
    {
        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        if (_justiceCustodyPlayerStateStored && _justiceCustodyStoredFrozen)
        {
            // Je ne réinjecte jamais le gel capturé pendant l'arrestation dans
            // une remise en liberté technique : ce verrou appartient à la
            // transition GTA, pas à l'état durable du protagoniste.
            _justiceCustodyStoredFrozen = false;
            JusticeMarkStateDirty();
        }

        bool transientStateRestored = RestoreJusticeCustodyPlayerTransientState(player);
        bool mobilityRestored = EnsureJusticeCustodyPlayerMobility(player);
        return transientStateRestored && mobilityRestored;
    }

    private static int GetJusticePedModelHashSafe(Ped ped)
    {
        try
        {
            if (!Entity.Exists(ped))
            {
                return 0;
            }

            int modelHash = ped.Model.Hash;
            return modelHash != 0
                ? modelHash
                : Function.Call<int>((Hash)JusticeNativeGetEntityModel, ped.Handle);
        }
        catch
        {
            return 0;
        }
    }

    private bool IsJusticeCustodyPlayerIdentityCompatible(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        int modelHash = GetJusticePedModelHashSafe(player);
        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (JusticePolicy.IsCustodyLiveIdentityCompatible(
                _justiceCustodyPlayerSlot,
                currentSlot,
                _justiceCustodyPlayerHandle,
                player.Handle,
                _justiceCustodyPlayerModelHash,
                modelHash))
        {
            _justiceCustodyPlayerHandle = player.Handle;
            return true;
        }

        return false;
    }

    private bool IsJusticeCustodyDeathIdentityCompatible(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int modelHash = GetJusticePedModelHashSafe(player);
        return JusticePolicy.IsCustodyDeathIdentityCompatible(
            _justiceCustodyPlayerSlot,
            currentSlot,
            _justiceCustodyPlayerHandle,
            player.Handle,
            _justiceCustodyPlayerModelHash,
            modelHash);
    }

    private bool JusticeCustodyCanMutateWorld(Ped player)
    {
        return Entity.Exists(player) &&
               !player.IsDead &&
               !IsJusticeRuntimeSuspended(player);
    }

    private bool EnsureJusticeCustodyPlayerMobility(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        try
        {
            if (player.FreezePosition)
            {
                player.FreezePosition = false;
            }

            if (player.FreezePosition)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        if (_justiceCustodyPlayerStateStored && _justiceCustodyStoredFrozen)
        {
            // Je retire un gel de transition dès que Justice doit garantir la
            // mobilité, pendant la détention comme pendant un rollback technique.
            _justiceCustodyStoredFrozen = false;
            JusticeMarkStateDirty();
            LogInfo(
                "Justice.Detention",
                "Verrou de déplacement transitoire retiré du snapshot de détention.");
        }

        return true;
    }

    private void ResetJusticeCustodyClock(int now)
    {
        _justiceCustodyLastTickAt = now;
        _justiceCustodyElapsedRemainderMs = 0;
    }

    private void MaintainJusticeCustodyPoliceSuppression(Ped player, int now)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            _justiceCaseState == null ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated ||
            _justiceCustodyTransferPending || _justiceCustodyResumePending ||
            !JusticeCustodyHasReached(now, _justiceNextPoliceSuppressionAt))
        {
            return;
        }

        _justiceNextPoliceSuppressionAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyPoliceSuppressionIntervalMs);
        SetJusticeCustodyPoliceSuppression(true);
        if (GetJusticeWantedLevelSafe() > 0)
        {
            ClearJusticeWantedLevelOnce();
        }
    }

    private void SetJusticeCustodyPoliceSuppression(bool suppress)
    {
        bool restorationWasTracked = _justicePoliceIgnoreApplied ||
            _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending;
        if (suppress &&
            (!_justicePoliceIgnoreApplied || !_justicePoliceDispatchDisabled))
        {
            bool previousIgnoreApplied = _justicePoliceIgnoreApplied;
            bool previousDispatchDisabled = _justicePoliceDispatchDisabled;
            _justicePoliceIgnoreApplied = true;
            _justicePoliceDispatchDisabled = true;
            _justicePoliceSuppressionActive = true;
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                // Je n'applique aucun flag global si son intention de restauration
                // n'existe pas déjà dans le primaire et son backup.
                _justicePoliceIgnoreApplied = previousIgnoreApplied;
                _justicePoliceDispatchDisabled = previousDispatchDisabled;
                _justicePoliceSuppressionActive =
                    previousIgnoreApplied || previousDispatchDisabled;
                JusticeMarkStateDirty();
                return;
            }
        }

        bool failed = false;
        try
        {
            Function.Call(
                (Hash)JusticeNativeSetPoliceIgnorePlayer,
                Game.Player.Handle,
                suppress);
            if (!suppress)
            {
                _justicePoliceIgnoreApplied = false;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            if (!_justicePoliceSuppressionFailureLogged)
            {
                LogWarning(
                    "Justice.Detention",
                    "Restauration du statut policier à retenter : " + ex.GetType().Name + ".");
            }
        }

        try
        {
            Function.Call(
                (Hash)JusticeNativeSetDispatchCopsForPlayer,
                Game.Player.Handle,
                !suppress);
            if (!suppress)
            {
                _justicePoliceDispatchDisabled = false;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            if (!_justicePoliceSuppressionFailureLogged)
            {
                LogWarning(
                    "Justice.Detention",
                    "Restauration du dispatch policier à retenter : " + ex.GetType().Name + ".");
            }
        }

        _justicePoliceSuppressionActive =
            _justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled;
        _justicePoliceSuppressionRestorePending = !suppress && _justicePoliceSuppressionActive;
        _justicePoliceSuppressionFailureLogged = failed;
        if (_justicePoliceSuppressionRestorePending)
        {
            _justiceNextPoliceSuppressionRestoreAt = JusticeCustodyFutureTime(
                Game.GameTime,
                JusticeCustodyPoliceSuppressionIntervalMs);
        }
        else if (!suppress && restorationWasTracked)
        {
            // Je n'efface les deux jetons qu'après avoir réellement restauré les
            // natives, puis je durcis ce nettoyage. En cas d'échec disque je les
            // réarme en mémoire pour imposer une restauration idempotente.
            if (!TryClearJusticeInactiveProfilePoliceSuppressionTokens())
            {
                _justicePoliceIgnoreApplied = true;
                _justicePoliceDispatchDisabled = true;
                _justicePoliceSuppressionActive = true;
                _justicePoliceSuppressionRestorePending = true;
                _justiceNextPoliceSuppressionRestoreAt = JusticeCustodyFutureTime(
                    Game.GameTime,
                    JusticeCustodyPoliceSuppressionIntervalMs);
                JusticeMarkStateDirty();
                LogWarning(
                    "Justice.Detention",
                    "Nettoyage des jetons police d'un profil inactif à retenter.");
                return;
            }
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                _justicePoliceIgnoreApplied = true;
                _justicePoliceDispatchDisabled = true;
                _justicePoliceSuppressionActive = true;
                _justicePoliceSuppressionRestorePending = true;
                _justiceNextPoliceSuppressionRestoreAt = JusticeCustodyFutureTime(
                    Game.GameTime,
                    JusticeCustodyPoliceSuppressionIntervalMs);
                JusticeMarkStateDirty();
                return;
            }

            _justiceNextPoliceSuppressionRestoreAt = 0;
            _justicePoliceSuppressionFailureLogged = false;
        }
    }

    private void RetryJusticePoliceSuppressionRestore(Ped player, int now)
    {
        if (!_justicePoliceSuppressionRestorePending || !Entity.Exists(player) || player.IsDead ||
            !JusticeCustodyHasReached(now, _justiceNextPoliceSuppressionRestoreAt) ||
            IsJusticeRuntimeSuspended(player))
        {
            return;
        }

        _justiceNextPoliceSuppressionRestoreAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyPoliceSuppressionIntervalMs);
        SetJusticeCustodyPoliceSuppression(false);
    }

    private bool TryJusticeEmergencyTeleport(
        Ped player,
        Vector3 targetPosition,
        float heading)
    {
        bool moved = false;
        try
        {
            if (!Entity.Exists(player))
            {
                return false;
            }

            SetEntityCoordsNoOffsetSafe(
                player,
                targetPosition + new Vector3(0.0f, 0.0f, 0.35f));
            player.Heading = NormalizeHeading(heading);
            Function.Call(Hash.SET_ENTITY_VELOCITY, player.Handle, 0.0f, 0.0f, 0.0f);
            moved = IsJusticeTeleportVerified(player, targetPosition, 8.0f);
        }
        catch (Exception ex)
        {
            LogException("Justice.TeleportSecours", ex);
        }

        if (!moved)
        {
            try
            {
                // La propriété v2 fournit un second chemin indépendant de la
                // native brute lorsqu'un loader refuse silencieusement SET_COORDS.
                player.Position = targetPosition + new Vector3(0.0f, 0.0f, 0.35f);
                player.Heading = NormalizeHeading(heading);
                moved = IsJusticeTeleportVerified(player, targetPosition, 8.0f);
            }
            catch (Exception ex)
            {
                LogException("Justice.TeleportSecoursV2", ex);
            }
        }

        try
        {
            Function.Call(Hash.DO_SCREEN_FADE_IN, 250);
        }
        catch
        {
        }
        return moved;
    }

    private static bool IsJusticeTeleportVerified(Ped player, Vector3 targetPosition, float tolerance)
    {
        try
        {
            return Entity.Exists(player) && !player.IsDead &&
                   player.Position.DistanceTo(targetPosition) <= Math.Max(1.0f, tolerance);
        }
        catch
        {
            return false;
        }
    }

    private void AdvanceJusticeCustodyClock(int now)
    {
        if (_justiceCustodyLastTickAt == 0)
        {
            _justiceCustodyLastTickAt = now;
            return;
        }

        uint elapsed = unchecked((uint)(now - _justiceCustodyLastTickAt));
        _justiceCustodyLastTickAt = now;
        int boundedElapsed = (int)Math.Min((uint)JusticeCustodyMaxFrameElapsedMs, elapsed);
        _justiceCustodyElapsedRemainderMs += boundedElapsed;

        int elapsedSeconds = _justiceCustodyElapsedRemainderMs / 1000;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        _justiceCustodyElapsedRemainderMs %= 1000;
        _justiceCaseState.SentenceSeconds = Math.Max(
            0,
            _justiceCaseState.SentenceSeconds - elapsedSeconds);
        JusticeMarkStateDirty();
    }

    private JusticeCustodyLayout GetJusticeCustodyLayout()
    {
        return GetJusticeCustodyLayoutForSite(_justiceCustodySite);
    }

    private static JusticeCustodyLayout GetJusticeCustodyLayoutForSite(
        JusticeCustodySite site)
    {
        switch (site)
        {
            case JusticeCustodySite.MissionRow:
                return JusticeMissionRowLayout;
            case JusticeCustodySite.Bolingbroke:
                return JusticeBolingbrokeLayout;
            default:
                return null;
        }
    }

    private bool IsInsideJusticeCustody(Vector3 position)
    {
        return IsInsideJusticeCustodyLayout(GetJusticeCustodyLayout(), position);
    }

    private static bool IsInsideJusticeCustodyLayout(
        JusticeCustodyLayout layout,
        Vector3 position)
    {
        if (layout == null || layout.AllowedVolumes == null)
        {
            return false;
        }

        for (int index = 0; index < layout.AllowedVolumes.Length; index++)
        {
            JusticeCustodyVolume volume = layout.AllowedVolumes[index];
            if (volume != null && volume.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateJusticeCustodyEscape(Ped player, int now)
    {
        if (_justiceCaseState == null || !Entity.Exists(player))
        {
            return;
        }

        if (IsInsideJusticeCustody(player.Position))
        {
            _justiceOutsideCustodySinceAt = 0;
            if (_justiceCaseState.Phase == JusticePhase.Escaping)
            {
                _justiceCaseState.Phase = JusticePhase.Incarcerated;
                JusticeMarkStateDirty();
            }

            return;
        }

        if (_justiceOutsideCustodySinceAt == 0)
        {
            _justiceOutsideCustodySinceAt = now;
            _justiceCaseState.Phase = JusticePhase.Escaping;
            CancelJusticeCustodyActivity(false, now);
            JusticeMarkStateDirty();
            return;
        }

        uint elapsed = unchecked((uint)(now - _justiceOutsideCustodySinceAt));
        if (elapsed < JusticeCustodyEscapeGraceMs)
        {
            return;
        }

        CompleteJusticeCustodyEscape(player);
    }

    private void CompleteJusticeCustodyEscape(Ped player)
    {
        if (_justiceCaseState == null)
        {
            return;
        }

        int now = Game.GameTime;
        if (!JusticeCustodyHasReached(now, _justiceEscapePersistenceRetryAt))
        {
            return;
        }

        if (!FinalizeJusticePendingDisciplineBeforeCustodyExit(player, now))
        {
            return;
        }

        JusticeOperation discard = CreateJusticeCustodyOperation(JusticeOperationKind.DiscardInventory);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, discard);
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            // Je ne touche physiquement à aucune arme tant que l'intention de
            // confiscation définitive n'est pas reprise par le XML ou son backup.
            _justiceEscapePersistenceRetryAt = JusticeCustodyFutureTime(now, 1000);
            ShowStatus("Évasion en attente : sécurisation de l'inventaire…", 2200);
            return;
        }
        _justiceEscapePersistenceRetryAt = 0;

        // Je retire aussi toute arme ramassée pendant la détention. L'intention
        // de confiscation est déjà persistée, et l'identité du joueur a été
        // validée avant d'entrer dans cette branche.
        if (Entity.Exists(player) && IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            if (!RemoveJusticePlayerWeaponsSafe(player))
            {
                _justiceInventoryRemoved = true;
                _justiceWeaponControlsLocked = true;
                _justiceEscapePersistenceRetryAt = JusticeCustodyFutureTime(now, 1000);
                ShowStatus("Évasion en attente : confiscation des armes à retenter…", 2200);
                return;
            }
        }

        CancelJusticeCustodyActivity(false, now);
        if (!EndJusticeCustodyDiscipline(player) ||
            !RestoreJusticeCustodyPlayerTransientState(player))
        {
            _justiceEscapePersistenceRetryAt = JusticeCustodyFutureTime(now, 500);
            return;
        }

        _justiceWeaponSnapshot = null;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        _justiceCustodyRuntimeActive = false;
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        _justiceOutsideCustodySinceAt = 0;
        CleanupJusticeCustodyEntitiesAndGroups();
        _justiceCustodyPlayerStateStored = false;
        JusticeRegisterEscape();

        // Je ferme l'ancien épisode de détention après avoir enregistré
        // l'évasion. Une recapture crée ainsi un épisode neuf et ne peut jamais
        // rejouer les opérations de la première incarcération.
        JusticePolicy.PruneClosedCustodyOperations(
            _justiceCaseState,
            _justiceCaseState.CustodyEpisodeId);
        _justiceCaseState.CustodyEpisodeId = string.Empty;
        ResetJusticeCustodyPersistentFields(false);
        JusticeMarkStateDirty();
        if (JusticeFlushStateNow())
        {
            // La demande d'étoiles propre à l'évasion n'est exécutée qu'après
            // le commit du dossier fugitif et possède son propre WAL at-most-once.
            RetryJusticeEscapeWantedMinimum(GetJusticeWantedLevelSafe());
        }
        LogInfo("Justice.Evasion", "Évasion confirmée après sortie continue de la zone autorisée.");
    }

    private void CompleteJusticeLegalRelease(Ped player)
    {
        if (_justiceCaseState == null)
        {
            return;
        }

        int now = Game.GameTime;
        if (!FinalizeJusticePendingDisciplineBeforeCustodyExit(player, now) ||
            _justiceCaseState.SentenceSeconds > 0)
        {
            return;
        }

        if (_justiceCaseState.FineDue > 0L)
        {
            string fineStage = BuildJusticeReleaseFineStage();
            if (!JusticeCollectFineAndConvertDetention(
                _justiceCustodySite == JusticeCustodySite.MissionRow,
                fineStage))
            {
                return;
            }
            if (_justiceCaseState.SentenceSeconds > 0)
            {
                ShowStatus("Justice : amende disciplinaire impayée convertie en détention.", 3600);
                return;
            }
        }

        if (!JusticeCustodyHasReached(now, _justiceReleaseRestoreRetryAt))
        {
            return;
        }

        JusticeCustodySite releaseSite = _justiceCustodySite;
        int selectedWeaponToRestore = _justiceWeaponSnapshot == null ||
            _justiceWeaponSnapshot.SelectedWeaponHash == 0
                ? _justiceReleaseSelectedWeaponHash
                : _justiceWeaponSnapshot.SelectedWeaponHash;
        if (selectedWeaponToRestore == 0)
        {
            selectedWeaponToRestore = JusticeUnarmedHash;
        }

        JusticeOperation release = CreateJusticeCustodyOperation(JusticeOperationKind.Release);
        if (!HasJusticeOperation(release.Kind, release.EpisodeId))
        {
            if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, release))
            {
                return;
            }
        }

        // Je précommitte le WAL de sortie avec le snapshot encore intact. Si le
        // processus s'arrête pendant la restitution, le prochain chargement
        // rejoue seulement cette transaction idempotente au lieu de réincarcérer.
        _justiceLegalReleaseFinalizationPending = true;
        _justiceLegalReleaseFinalizationSite = releaseSite;
        _justiceLegalReleaseSelectedWeaponHash = selectedWeaponToRestore;
        _justiceLegalReleaseWantedClearAttempted = false;
        _justiceLegalReleaseWeaponSelectionApplied = false;
        _justiceNextLegalReleaseWantedClearAt = 0;
        JusticeMarkStateDirty();
        ResumeJusticeLegalReleaseFinalization(player, now);
    }

    private bool ResumeJusticeLegalReleaseFinalization(Ped player, int now)
    {
        if (!_justiceLegalReleaseFinalizationPending)
        {
            return true;
        }

        if (Entity.Exists(player) && player.IsDead)
        {
            if (IsJusticeCustodyDeathIdentityCompatible(player))
            {
                ObserveJusticeCustodyDeathDuringSuspension(player);
            }
            return false;
        }

        // Ce premier flush est volontaire même après un reload : il garantit que
        // le WAL de sortie et son snapshot sont durables avant la restitution.
        if (!PersistJusticeLegalReleaseBarrier())
        {
            return false;
        }
        if (!Entity.Exists(player) ||
            !IsJusticeRuntimeProfileContextCompatible())
        {
            return false;
        }

        if (_justiceCustodyWaitingForRespawn &&
            (_justiceCustodyDeathRebindPending ||
             !IsJusticeCustodyPlayerIdentityCompatible(player)) &&
            !TryRebindJusticeCustodyIdentityAfterRespawn(player))
        {
            return false;
        }

        if (IsJusticeLegalReleasePrecommitState())
        {
            if (!JusticeCustodyHasReached(now, _justiceReleaseRestoreRetryAt))
            {
                return false;
            }
            if (!TryClearJusticeCustodyPlayerTasks(player, now))
            {
                // Je ne rends pas le contrôle avec un scénario pénitentiaire
                // encore attaché au bon héros, notamment après une peine finie
                // hors écran pendant qu'un autre protagoniste était joué.
                _justiceReleaseRestoreRetryAt = JusticeCustodyFutureTime(now, 500);
                return false;
            }
            if (!RestoreJusticeInventoryForLegalRelease(player, now))
            {
                _justiceReleaseRestoreRetryAt = JusticeCustodyFutureTime(now, 750);
                return false;
            }

            _justiceReleaseRestoreStartedAt = 0;
            _justiceReleaseRestoreRetryAt = 0;
            CancelJusticeCustodyActivity(false, now);
            if (!EndJusticeCustodyDiscipline(player) ||
                !RestoreJusticeCustodyPlayerTransientState(player))
            {
                _justiceReleaseRestoreRetryAt = JusticeCustodyFutureTime(now, 500);
                return false;
            }

            _justiceCustodyRuntimeActive = false;
            _justiceCustodyTransferPending = false;
            _justiceCustodyResumePending = false;
            _justiceOutsideCustodySinceAt = 0;
            CleanupJusticeCustodyEntitiesAndGroups();
            _justiceCustodyPlayerStateStored = false;
            ResetJusticeCustodyPersistentFields();
            JusticePrepareLegalReleaseState();

            // Le dossier libéré et la détention vide doivent être durables avant
            // la téléportation. En cas d'échec, le latch reste armé et le tick
            // suivant reprend exactement à ce palier.
            if (!PersistJusticeLegalReleaseBarrier())
            {
                return false;
            }
        }
        else if (_justiceCaseState == null ||
                 IsLoadedJusticeCaseActive(_justiceCaseState) ||
                 _justiceCaseState.Phase != JusticePhase.AtLarge)
        {
            LogWarning(
                "Justice.Liberation",
                "Finalisation suspendue : état durable de libération incohérent.");
            return false;
        }

        JusticeCustodyLayout layout = GetJusticeCustodyLayoutForSite(
            _justiceLegalReleaseFinalizationSite);
        if (layout != null && IsInsideJusticeCustodyLayout(layout, player.Position))
        {
            if (!JusticeCustodyHasReached(now, _justiceNextReleaseTeleportAttemptAt))
            {
                return false;
            }
            if (_justiceReleaseTeleportStartedAt == 0)
            {
                _justiceReleaseTeleportStartedAt = now;
            }

            bool releasedOutside = false;
            try
            {
                _activeInteriorSession = null;
                ClearInteriorRenderingFocusSafe(player);
                TeleportPlayerWithFadeSafe(player, layout.ReleasePosition, layout.ReleaseHeading);
                releasedOutside = IsJusticeTeleportVerified(player, layout.ReleasePosition, 8.0f);
            }
            catch (Exception ex)
            {
                LogException("Justice.Liberation", ex);
            }
            if (!releasedOutside)
            {
                releasedOutside = TryJusticeEmergencyTeleport(
                    player,
                    layout.ReleasePosition,
                    layout.ReleaseHeading);
            }

            if (!releasedOutside)
            {
                _justiceReleaseTeleportFailureCount = Math.Min(
                    16,
                    _justiceReleaseTeleportFailureCount + 1);
                int exponent = Math.Min(3, Math.Max(0, _justiceReleaseTeleportFailureCount - 1));
                int retryDelay = Math.Min(5000, 750 * (1 << exponent));
                _justiceNextReleaseTeleportAttemptAt = JusticeCustodyFutureTime(now, retryDelay);
                bool timedOut = unchecked((uint)(now - _justiceReleaseTeleportStartedAt)) >=
                                (uint)JusticeCustodyReleaseTeleportTimeoutMs;
                if (!timedOut)
                {
                    return false;
                }

                ShowStatus(
                    "Justice : sortie technique sur place, quitte calmement la zone.",
                    5200);
                LogWarning(
                    "Justice.Liberation",
                    "Téléportation de sortie abandonnée après timeout; libération finalisée sans soft-lock.");
            }
        }

        if (!_justiceLegalReleaseWantedClearAttempted)
        {
            if (!JusticeCustodyHasReached(now, _justiceNextLegalReleaseWantedClearAt))
            {
                return false;
            }

            // Je précommitte l'essai avant les deux natives externes. Si le jeu
            // ou le disque s'arrête ensuite, la reprise acquitte seulement le
            // WAL : elle ne peut ni forcer l'arme chaque frame, ni effacer les
            // étoiles d'un nouveau crime commis après la sortie.
            _justiceLegalReleaseWantedClearAttempted = true;
            JusticeMarkStateDirty();
            if (!PersistJusticeLegalReleaseBarrier())
            {
                _justiceLegalReleaseWantedClearAttempted = false;
                JusticeMarkStateDirty();
                _justiceNextLegalReleaseWantedClearAt =
                    JusticeCustodyFutureTime(now, JusticeCustodyFineCashReadRetryMs);
                return false;
            }

            if (!_justiceLegalReleaseWeaponSelectionApplied)
            {
                try
                {
                    if (_justiceLegalReleaseSelectedWeaponHash != 0)
                    {
                        Function.Call(
                            Hash.SET_CURRENT_PED_WEAPON,
                            player.Handle,
                            _justiceLegalReleaseSelectedWeaponHash,
                            true);
                    }
                }
                catch
                {
                }
                _justiceLegalReleaseWeaponSelectionApplied = true;
            }

            JusticeWantedClearResult clearResult =
                ClearJusticeWantedLevelOnceDetailed();
            if (clearResult == JusticeWantedClearResult.Rejected)
            {
                LogWarning(
                    "Justice.Liberation",
                    "Wanted GTA resté non nul après l'unique tentative de libération; aucun retry tardif ne sera appliqué.");
            }
            if (clearResult == JusticeWantedClearResult.Unknown)
            {
                LogWarning(
                    "Justice.Liberation",
                    "Résultat wanted ambigu; reprise at-most-once sans nouvelle écriture GTA.");
            }
        }
        _justiceNextLegalReleaseWantedClearAt = 0;
        _justiceLegalReleaseWeaponSelectionApplied = false;

        if (!CommitJusticeLegalReleaseFinalizationAcknowledgement())
        {
            return false;
        }

        _justiceReleaseTeleportStartedAt = 0;
        _justiceNextReleaseTeleportAttemptAt = 0;
        _justiceReleaseTeleportFailureCount = 0;
        _justiceNextLegalReleaseWantedClearAt = 0;
        ShowStatus("Justice : peine purgée, inventaire rendu et dossier actif clos.", 5200);
        LogInfo("Justice.Liberation", "Libération légale durable terminée.");
        return true;
    }

    private bool IsJusticeLegalReleasePrecommitState()
    {
        if (_justiceCaseState == null || !_justiceCaseState.Enabled ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated ||
            _justiceCaseState.SentenceSeconds > 0 || _justiceCaseState.FineDue > 0L ||
            string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
        {
            return false;
        }

        return HasJusticeOperation(
            JusticeOperationKind.Release,
            _justiceCaseState.CustodyEpisodeId);
    }

    private bool PersistJusticeLegalReleaseBarrier()
    {
        JusticeMarkStateDirty();
        return PersistJusticeCriticalPrecommitRedundantly();
    }

    private bool CommitJusticeLegalReleaseFinalizationAcknowledgement()
    {
        JusticeCustodySite completedSite = _justiceLegalReleaseFinalizationSite;
        int completedWeapon = _justiceLegalReleaseSelectedWeaponHash;
        bool completedWantedClearAttempted =
            _justiceLegalReleaseWantedClearAttempted;
        _justiceLegalReleaseFinalizationPending = false;
        _justiceLegalReleaseFinalizationSite = JusticeCustodySite.None;
        _justiceLegalReleaseSelectedWeaponHash = JusticeUnarmedHash;
        _justiceLegalReleaseWantedClearAttempted = false;
        if (PersistJusticeLegalReleaseBarrier())
        {
            return true;
        }

        // Le XML contient encore le latch=true. Je le restaure aussi en mémoire
        // afin que le tick répète uniquement les effets de sortie idempotents.
        _justiceLegalReleaseFinalizationPending = true;
        _justiceLegalReleaseFinalizationSite = completedSite;
        _justiceLegalReleaseSelectedWeaponHash = completedWeapon;
        _justiceLegalReleaseWantedClearAttempted =
            completedWantedClearAttempted;
        JusticeMarkStateDirty();
        return false;
    }

    private string BuildJusticeReleaseFineStage()
    {
        if (_justiceCaseState != null)
        {
            for (int index = _justiceCaseState.Charges.Count - 1; index >= 0; index--)
            {
                JusticeCharge charge = _justiceCaseState.Charges[index];
                if (charge != null && charge.Fine > 0L &&
                    !string.IsNullOrWhiteSpace(charge.IncidentId))
                {
                    // L'incident disciplinaire est persistant et unique. Le
                    // nombre de lignes ou le plafond d'amende, eux, peuvent se
                    // répéter après consolidation et ne forment pas une clé sûre.
                    return "release:" + charge.IncidentId.Trim();
                }
            }
        }

        return "release:fallback:" +
               (_justiceCaseState == null ? "none" :
                _justiceCaseState.CustodyEpisodeId ?? string.Empty);
    }

    private bool JusticeAmnestyCustody()
    {
        if (!HasJusticeCustodyRecoveryState())
        {
            // Un mandat ou un dossier en jeu libre n'a ni détenu ni inventaire à
            // restaurer. Je nettoie seulement d'éventuelles entités Justice puis
            // laisse l'appelant appliquer l'amnistie explicite au dossier.
            CleanupJusticeCustodyEntitiesAndGroups();
            ResetJusticeCustodyPersistentFields();
            JusticeMarkStateDirty();
            return true;
        }

        Ped player = Game.Player.Character;
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();

        if (!JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            ShowStatus(
                "Amnistie en attente : reprends le personnage placé en détention.",
                4200);
            return false;
        }

        if (_justiceCaseState != null &&
            !string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
        {
            JusticeOperation release = CreateJusticeCustodyOperation(JusticeOperationKind.Release);
            if (!HasJusticeOperation(release.Kind, release.EpisodeId))
            {
                if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, release))
                {
                    return false;
                }
                JusticeMarkStateDirty();
                if (!JusticeFlushStateNow())
                {
                    _justiceCaseState.CompletedOperationIds.Remove(release.OperationId);
                    JusticeMarkStateDirty();
                    return false;
                }
            }
        }

        if (!RestoreJusticeInventoryForLegalRelease(player, Game.GameTime))
        {
            ShowStatus("Amnistie en attente : restitution sécurisée de l'inventaire impossible.", 4200);
            return false;
        }

        CancelJusticeCustodyActivity(false, Game.GameTime);
        if (!EndJusticeCustodyDiscipline(player) ||
            !RestoreJusticeCustodyPlayerTransientState(player))
        {
            ShowStatus("Amnistie en attente : restauration de l'état du joueur…", 3600);
            return false;
        }
        _justiceCustodyRuntimeActive = false;
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        _justiceOutsideCustodySinceAt = 0;
        CleanupJusticeCustodyEntitiesAndGroups();

        if (layout != null && Entity.Exists(player) && IsInsideJusticeCustody(player.Position))
        {
            bool amnestyReleasedOutside = false;
            try
            {
                _activeInteriorSession = null;
                ClearInteriorRenderingFocusSafe(player);
                TeleportPlayerWithFadeSafe(player, layout.ReleasePosition, layout.ReleaseHeading);
                amnestyReleasedOutside = IsJusticeTeleportVerified(player, layout.ReleasePosition, 8.0f);
            }
            catch (Exception ex)
            {
                LogException("Justice.Amnistie", ex);
            }
            if (!amnestyReleasedOutside)
            {
                amnestyReleasedOutside = TryJusticeEmergencyTeleport(
                    player,
                    layout.ReleasePosition,
                    layout.ReleaseHeading);
            }

            if (!amnestyReleasedOutside)
            {
                // L'utilisateur vient de confirmer explicitement l'amnistie.
                // Si GTA refuse les deux chemins de sortie, je termine sur place
                // plutôt que de recréer une détention à peine nulle.
                ShowStatus(
                    "Amnistie validée sur place : quitte librement la zone.",
                    4800);
                LogWarning(
                    "Justice.Amnistie",
                    "Téléportation de sortie refusée; amnistie finalisée sans soft-lock.");
            }
        }

        _justiceCustodyPlayerStateStored = false;
        ResetJusticeCustodyPersistentFields();
        JusticeMarkStateDirty();
        return true;
    }

    private bool HasJusticeCustodyRecoveryState()
    {
        return _justiceLegalReleaseFinalizationPending ||
               _justiceCustodyTransferRollbackFinalizationPending ||
               JusticeIsCustodyActive ||
               _justicePoliceSuppressionActive ||
               _justicePoliceIgnoreApplied ||
               _justicePoliceDispatchDisabled ||
               _justicePoliceSuppressionRestorePending ||
               _justiceFineDebitIntent != null ||
               _justiceVoluntaryFinePaymentIntent != null ||
               _justiceDisciplineIntent != null ||
               _justiceWeaponSnapshot != null ||
               _justiceInventoryRemoved ||
               _justiceWeaponControlsLocked ||
               _justiceCustodyPlayerStateStored;
    }

    private bool CanParkCurrentJusticeCustodyForProfileSwitch()
    {
        return _justiceEnabled && _justiceCaseState != null &&
               _justiceCaseState.Phase == JusticePhase.Incarcerated &&
               _justiceCaseState.SentenceSeconds >= 0 &&
               (_justiceCustodyRuntimeActive ||
                _justiceCaseState.SentenceSeconds == 0) &&
               !_justiceCustodyTransferPending &&
               !_justiceCustodyWaitingForRespawn &&
               !_justiceCustodyDeathRebindPending &&
               !_justiceCustodyDeathStatePersistencePending &&
               !_justiceCustodyTransferRollbackFinalizationPending &&
               !_justiceLegalReleaseFinalizationPending &&
               !_justiceAmnestyPending && !_justiceActiveProfileResetPending &&
               !_justicePursuitDeathObservedDuringSuspension &&
               _justiceFineDebitIntent == null &&
               _justiceVoluntaryFinePaymentIntent == null &&
               _justiceDisciplineIntent == null && !_justiceDisciplineActive &&
               !_justiceDisciplineInvincibilityRestorePending &&
               !_justiceDeferredInventoryRestore &&
               _justiceCustodySite != JusticeCustodySite.None &&
               _justiceCustodyPlayerModelHash != 0 &&
               IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) &&
               _justiceCustodyPlayerSlot == _justiceActivePlayerProfileSlot &&
               !HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback) &&
               !HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory);
    }

    private bool CanAdvanceCurrentJusticeCustodyInBackground()
    {
        return CanParkCurrentJusticeCustodyForProfileSwitch() &&
               _justiceCaseState.SentenceSeconds > 0 &&
               !IsJusticeRuntimeProfileContextCompatible();
    }

    private bool TryPrepareJusticeCustodyForProfileSwitch(int now)
    {
        bool canPark = CanParkCurrentJusticeCustodyForProfileSwitch();

        // Une reprise chargée avant l'identification du héros conserve encore
        // ses délais sous forme de secondes. Je les matérialise avant toute
        // restauration police susceptible de déclencher un commit immédiat.
        ApplyLoadedJusticeActivityCooldowns(now);

        // Les deux natives police sont globales au joueur GTA. Dès qu'un autre
        // slot canonique est prouvé, je les restaure même si une transaction de
        // l'ancien détenu impose encore de différer le basculement de son dossier.
        SetJusticeCustodyPoliceSuppression(false);
        if (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending)
        {
            return false;
        }
        if (!canPark)
        {
            return false;
        }

        // Je n'accorde jamais le bonus d'une activité abandonnée au changement de
        // héros et je ne tente pas de nettoyer les tâches sur le ped entrant.
        CancelJusticeCustodyActivity(false, now);

        CleanupJusticeCustodySceneEntitiesAndGroups();
        _justiceNextPoliceSuppressionAt = 0;
        _justiceOutsideCustodySinceAt = 0;
        ResetJusticeCustodyClock(now);
        return true;
    }

    private void PrepareJusticeInventoryConfiscation(Ped player)
    {
        JusticeWeaponSnapshot snapshot;
        if (!TryCaptureJusticeWeaponSnapshot(player, out snapshot) ||
            !ValidateJusticeWeaponSnapshot(snapshot))
        {
            _justiceWeaponSnapshot = null;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = 0;
            SelectJusticeUnarmedSafe(player);
            LogWarning(
                "Justice.Inventaire",
                "Snapshot non validable : inventaire préservé et contrôles d'arme bloqués.");
            return;
        }

        _justiceWeaponSnapshot = snapshot;
        _justiceInventoryRemoved = true;
        _justiceWeaponControlsLocked = false;
        JusticeOperation operation = CreateJusticeCustodyOperation(JusticeOperationKind.ConfiscateInventory);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        JusticeMarkStateDirty();

        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceCaseState.CompletedOperationIds.Remove(operation.OperationId);
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(Game.GameTime, 5000);
            JusticeMarkStateDirty();
            SelectJusticeUnarmedSafe(player);
            LogWarning(
                "Justice.Inventaire",
                "Snapshot non persisté : aucun retrait destructif n'a été effectué.");
            return;
        }

        _justiceNextInventoryPersistenceRetryAt = 0;
        if (!RemoveJusticePlayerWeaponsSafe(player))
        {
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(Game.GameTime, 1000);
            LogWarning("Justice.Inventaire", "Confiscation refusée par GTA; retry sécurisé actif.");
        }
    }

    private void RetryJusticeInventoryConfiscationIfDue(Ped player, int now)
    {
        if (!_justiceWeaponControlsLocked ||
            !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) ||
            !JusticeCustodyHasReached(now, _justiceNextInventoryPersistenceRetryAt))
        {
            return;
        }

        JusticeOperation operation = CreateJusticeCustodyOperation(JusticeOperationKind.ConfiscateInventory);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        _justiceInventoryRemoved = true;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceCaseState.CompletedOperationIds.Remove(operation.OperationId);
            _justiceInventoryRemoved = false;
            _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(now, 5000);
            return;
        }

        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        if (!RemoveJusticePlayerWeaponsSafe(player))
        {
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(now, 1000);
            return;
        }
        LogInfo("Justice.Inventaire", "Snapshot persisté au retry, confiscation appliquée.");
    }

    private bool PersistJusticeCriticalPrecommitRedundantly()
    {
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        // Je force une seconde écriture identique avant tout effet externe : le
        // primaire et son .bak portent alors tous deux l'intention validée. Une
        // corruption ultérieure ne peut ni perdre un snapshot, ni rejouer un débit.
        JusticeMarkStateDirty();
        return JusticeFlushStateNow();
    }

    private bool TryCaptureJusticeWeaponSnapshot(Ped player, out JusticeWeaponSnapshot snapshot)
    {
        snapshot = null;
        if (!Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        try
        {
            JusticeWeaponSnapshot candidate = new JusticeWeaponSnapshot
            {
                SelectedWeaponHash = Function.Call<int>(
                    (Hash)NativeGetSelectedPedWeapon,
                    player.Handle)
            };
            HashSet<int> seenWeaponHashes = new HashSet<int>();
            List<int> weaponHashes = new List<int>();
            if (!TryCollectJusticeWeaponHashes(seenWeaponHashes, weaponHashes))
            {
                return false;
            }

            for (int index = 0; index < weaponHashes.Count; index++)
            {
                int weaponHash = weaponHashes[index];

                bool ownsWeapon = Function.Call<bool>(
                    Hash.HAS_PED_GOT_WEAPON,
                    player.Handle,
                    weaponHash,
                    false);
                if (!ownsWeapon)
                {
                    continue;
                }

                if (candidate.Weapons.Count >= JusticeCustodyMaxWeapons)
                {
                    return false;
                }

                JusticeWeaponSnapshotItem item = new JusticeWeaponSnapshotItem
                {
                    WeaponHash = weaponHash,
                    Ammo = Math.Max(0, Function.Call<int>(
                        Hash.GET_AMMO_IN_PED_WEAPON,
                        player.Handle,
                        weaponHash)),
                    Tint = Math.Max(0, Function.Call<int>(
                        Hash.GET_PED_WEAPON_TINT_INDEX,
                        player.Handle,
                        weaponHash))
                };

                OutputArgument clipOutput = new OutputArgument();
                bool clipRead = Function.Call<bool>(
                    Hash.GET_AMMO_IN_CLIP,
                    player.Handle,
                    weaponHash,
                    clipOutput);
                if (!clipRead)
                {
                    // Un chargeur inconnu rend le snapshot entier non fidèle :
                    // je conserve alors l'inventaire physique et verrouille l'arme.
                    return false;
                }
                item.AmmoInClip = Math.Max(0, clipOutput.GetResult<int>());

                if (!CaptureJusticeWeaponComponents(player, item))
                {
                    return false;
                }
                candidate.Weapons.Add(item);
            }

            candidate.IsValidated = true;
            snapshot = candidate;
            return true;
        }
        catch (Exception ex)
        {
            LogException("Justice.SnapshotArmes", ex);
            snapshot = null;
            return false;
        }
    }

    private bool TryCollectJusticeWeaponHashes(HashSet<int> seen, List<int> destination)
    {
        if (seen == null || destination == null)
        {
            return false;
        }

        IntPtr dlcWeaponBuffer = IntPtr.Zero;
        try
        {
            Array weaponValues = Enum.GetValues(typeof(WeaponHash));
            for (int index = 0; index < weaponValues.Length; index++)
            {
                AddJusticeWeaponHashIfUnique(
                    JusticeEnumValueToInt(weaponValues.GetValue(index)),
                    seen,
                    destination);
            }

            int dlcCount = Function.Call<int>((Hash)JusticeNativeGetNumDlcWeapons);
            if (dlcCount < 0 || dlcCount > JusticeCustodyMaxDlcWeaponDefinitions)
            {
                return false;
            }

            int nativeDataSize = Marshal.SizeOf(typeof(JusticeDlcWeaponData));
            if (nativeDataSize != JusticeDlcWeaponDataSize)
            {
                // Je refuse toute confiscation si le contrat de structure dérive :
                // une liste partielle ferait perdre une arme DLC au RemoveAll.
                return false;
            }

            dlcWeaponBuffer = Marshal.AllocCoTaskMem(nativeDataSize);
            InputArgument dlcWeaponPointer = new InputArgument(
                unchecked((ulong)dlcWeaponBuffer.ToInt64()));
            for (int index = 0; index < dlcCount; index++)
            {
                ZeroJusticeUnmanagedBuffer(dlcWeaponBuffer, nativeDataSize);
                if (!Function.Call<bool>(
                    (Hash)JusticeNativeGetDlcWeaponData,
                    index,
                    dlcWeaponPointer))
                {
                    return false;
                }

                AddJusticeWeaponHashIfUnique(
                    Marshal.ReadInt32(dlcWeaponBuffer, JusticeDlcWeaponHashOffset),
                    seen,
                    destination);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogException("Justice.ListeArmes", ex);
            return false;
        }
        finally
        {
            if (dlcWeaponBuffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(dlcWeaponBuffer);
            }
        }
    }

    private static void ZeroJusticeUnmanagedBuffer(IntPtr buffer, int size)
    {
        if (buffer == IntPtr.Zero || size <= 0)
        {
            return;
        }

        int offset = 0;
        for (; offset + sizeof(long) <= size; offset += sizeof(long))
        {
            Marshal.WriteInt64(buffer, offset, 0L);
        }
        for (; offset < size; offset++)
        {
            Marshal.WriteByte(buffer, offset, 0);
        }
    }

    private static void AddJusticeWeaponHashIfUnique(
        int weaponHash,
        HashSet<int> seen,
        List<int> destination)
    {
        if (weaponHash != 0 && weaponHash != JusticeUnarmedHash && seen.Add(weaponHash))
        {
            destination.Add(weaponHash);
        }
    }

    private static int JusticeEnumValueToInt(object value)
    {
        if (value == null)
        {
            return 0;
        }

        long signed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return unchecked((int)signed);
    }

    private bool CaptureJusticeWeaponComponents(Ped player, JusticeWeaponSnapshotItem item)
    {
        if (!Entity.Exists(player) || item == null)
        {
            return false;
        }

        try
        {
            Type weaponType = typeof(WeaponHash).Assembly.GetType("GTA.Weapon", false);
            MethodInfo componentMethod = weaponType == null
                ? null
                : weaponType.GetMethod(
                    "GetComponentsFromHash",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(WeaponHash) },
                    null);
            if (componentMethod == null)
            {
                return false;
            }

            IEnumerable components = componentMethod.Invoke(
                null,
                new object[] { (WeaponHash)item.WeaponHash }) as IEnumerable;
            if (components == null)
            {
                return false;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (object component in components)
            {
                if (item.ComponentHashes.Count >= JusticeCustodyMaxComponentsPerWeapon)
                {
                    return false;
                }

                int componentHash = JusticeEnumValueToInt(component);
                if (componentHash == 0 || !seen.Add(componentHash))
                {
                    continue;
                }

                if (Function.Call<bool>(
                    Hash.HAS_PED_GOT_WEAPON_COMPONENT,
                    player.Handle,
                    item.WeaponHash,
                    componentHash))
                {
                    item.ComponentHashes.Add(componentHash);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // Je préfère le verrou non destructif : déclarer ce snapshot valide
            // ferait perdre silencieusement les accessoires que je n'ai pas lus.
            LogException("Justice.ComposantsArmes", ex);
            return false;
        }
    }

    private static bool ValidateJusticeWeaponSnapshot(JusticeWeaponSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.IsValidated ||
            snapshot.Weapons.Count > JusticeCustodyMaxWeapons)
        {
            return false;
        }

        HashSet<int> weaponHashes = new HashSet<int>();
        bool selectedIsPresent = snapshot.SelectedWeaponHash == 0 ||
                                 snapshot.SelectedWeaponHash == JusticeUnarmedHash;
        for (int index = 0; index < snapshot.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = snapshot.Weapons[index];
            if (item == null || item.WeaponHash == 0 || item.WeaponHash == JusticeUnarmedHash ||
                !weaponHashes.Add(item.WeaponHash) || item.Ammo < 0 || item.Ammo > 1000000 ||
                item.AmmoInClip < 0 || item.AmmoInClip > 1000000 || item.AmmoInClip > item.Ammo ||
                item.Tint < 0 || item.Tint > 64 ||
                item.ComponentHashes.Count > JusticeCustodyMaxComponentsPerWeapon)
            {
                return false;
            }

            HashSet<int> components = new HashSet<int>();
            for (int componentIndex = 0; componentIndex < item.ComponentHashes.Count; componentIndex++)
            {
                int componentHash = item.ComponentHashes[componentIndex];
                if (componentHash == 0 || !components.Add(componentHash))
                {
                    return false;
                }
            }

            if (item.WeaponHash == snapshot.SelectedWeaponHash)
            {
                selectedIsPresent = true;
            }
        }

        return selectedIsPresent;
    }

    private bool RemoveJusticePlayerWeaponsSafe(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, player.Handle, true);
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, JusticeUnarmedHash, true);
        }
        catch
        {
        }

        if (VerifyJusticePlayerHasNoWeapons(player))
        {
            return true;
        }

        try
        {
            player.Weapons.RemoveAll();
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, JusticeUnarmedHash, true);
        }
        catch
        {
        }
        return VerifyJusticePlayerHasNoWeapons(player);
    }

    private bool VerifyJusticePlayerHasNoWeapons(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            HashSet<int> seenWeaponHashes = new HashSet<int>();
            List<int> weaponHashes = new List<int>();
            if (!TryCollectJusticeWeaponHashes(seenWeaponHashes, weaponHashes))
            {
                return false;
            }

            for (int index = 0; index < weaponHashes.Count; index++)
            {
                int weaponHash = weaponHashes[index];
                if (weaponHash != JusticeUnarmedHash && Function.Call<bool>(
                    Hash.HAS_PED_GOT_WEAPON,
                    player.Handle,
                    weaponHash,
                    false))
                {
                    return false;
                }
            }

            try
            {
                int selected = Function.Call<int>(
                    (Hash)JusticeNativeGetSelectedPedWeapon,
                    player.Handle);
                return selected == 0 || selected == JusticeUnarmedHash;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void SelectJusticeUnarmedSafe(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        try
        {
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, JusticeUnarmedHash, true);
        }
        catch
        {
        }
    }

    private void EnforceJusticeCustodyWeaponLock(Ped player)
    {
        if (!Entity.Exists(player) || (!_justiceInventoryRemoved && !_justiceWeaponControlsLocked))
        {
            return;
        }

        bool canUseUnarmedCombat = JusticePolicy.CanUseCustodyUnarmedCombat(
            _justiceInventoryRemoved,
            _justiceWeaponControlsLocked);
        if (!canUseUnarmedCombat)
        {
            Game.DisableControlThisFrame(0, GtaControl.Attack);
            Game.DisableControlThisFrame(0, GtaControl.Aim);
            Game.DisableControlThisFrame(0, (GtaControl)140);
            Game.DisableControlThisFrame(0, (GtaControl)141);
            Game.DisableControlThisFrame(0, (GtaControl)142);
        }

        // Je garde les changements d'arme interdits dans tous les cas. Quand le
        // retrait est vérifié, les poings et le verrouillage de cible restent
        // néanmoins disponibles pour se défendre face aux autres détenus.
        Game.DisableControlThisFrame(0, GtaControl.SelectWeapon);
        Game.DisableControlThisFrame(0, GtaControl.Reload);
        Game.DisableControlThisFrame(0, GtaControl.WeaponWheelLeftRight);
        Game.DisableControlThisFrame(0, GtaControl.WeaponWheelUpDown);
        SelectJusticeUnarmedSafe(player);
    }

    private bool RestoreJusticeWeaponSnapshot(Ped player)
    {
        if (!Entity.Exists(player) || !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return false;
        }

        // Je repars d'un inventaire vide afin qu'une reprise soit idempotente,
        // puis j'isole chaque arme et chaque composant : une entrée refusée par
        // GTA ne doit jamais empêcher la restitution des autres éléments.
        if (!RemoveJusticePlayerWeaponsSafe(player))
        {
            return false;
        }
        bool fullyRestored = true;
        for (int index = 0; index < _justiceWeaponSnapshot.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = _justiceWeaponSnapshot.Weapons[index];
            try
            {
                Function.Call(
                    (Hash)JusticeNativeGiveWeaponToPed,
                    player.Handle,
                    item.WeaponHash,
                    item.Ammo,
                    false,
                    false);
            }
            catch
            {
                fullyRestored = false;
                continue;
            }

            for (int componentIndex = 0; componentIndex < item.ComponentHashes.Count; componentIndex++)
            {
                try
                {
                    Function.Call(
                        Hash.GIVE_WEAPON_COMPONENT_TO_PED,
                        player.Handle,
                        item.WeaponHash,
                        item.ComponentHashes[componentIndex]);
                }
                catch
                {
                    fullyRestored = false;
                }
            }

            try
            {
                Function.Call(
                    Hash.SET_PED_WEAPON_TINT_INDEX,
                    player.Handle,
                    item.WeaponHash,
                    item.Tint);
                Function.Call(
                    Hash.SET_AMMO_IN_CLIP,
                    player.Handle,
                    item.WeaponHash,
                    item.AmmoInClip);
            }
            catch
            {
                fullyRestored = false;
            }
        }

        try
        {
            int selected = _justiceWeaponSnapshot.SelectedWeaponHash;
            if (selected == 0)
            {
                selected = JusticeUnarmedHash;
            }

            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, selected, true);
        }
        catch
        {
            fullyRestored = false;
        }

        return fullyRestored && RestoreJusticeWeaponSnapshotMergeSafe(player, true, true);
    }

    private bool RestoreJusticeInventoryForLegalRelease(Ped player, int now)
    {
        if ((_justiceInventoryRemoved || _justiceWeaponSnapshot != null) &&
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        if (_justiceInventoryRemoved && _justiceWeaponSnapshot != null)
        {
            bool restored = RestoreJusticeWeaponSnapshot(player);
            if (!restored)
            {
                if (_justiceReleaseRestoreStartedAt == 0)
                {
                    _justiceReleaseRestoreStartedAt = now;
                }

                uint retryElapsed = unchecked((uint)(now - _justiceReleaseRestoreStartedAt));
                if (retryElapsed >= 10000U)
                {
                    restored = RestoreJusticeWeaponSnapshotWithApiFallback(player);
                }

                if (!restored)
                {
                    uint totalElapsed = unchecked((uint)(now - _justiceReleaseRestoreStartedAt));
                    if (totalElapsed >= JusticeCustodyDeferredRestoreDelayMs)
                    {
                        _justiceDeferredInventoryRestore = true;
                        _justiceInventoryRemoved = false;
                        _justiceWeaponControlsLocked = false;
                        _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
                            now,
                            JusticeCustodyDeferredRestoreRetryMs);
                        JusticeMarkStateDirty();
                        if (!PersistJusticeDeferredRestoreRedundantly())
                        {
                            _justiceInventoryRemoved = true;
                            _justiceWeaponControlsLocked = true;
                            return false;
                        }

                        LogWarning(
                            "Justice.Inventaire",
                            "Libération poursuivie avec restitution différée et snapshot conservé.");
                        return true;
                    }

                    _justiceWeaponControlsLocked = true;
                    return false;
                }
            }

            JusticeOperation operation = CreateJusticeCustodyOperation(JusticeOperationKind.RestoreInventory);
            JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
            _justiceDeferredInventoryRestore = false;
            _justiceNextDeferredInventoryRestoreAt = 0;
        }

        if (!_justiceDeferredInventoryRestore)
        {
            _justiceWeaponSnapshot = null;
        }
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        JusticeMarkStateDirty();
        return true;
    }

    private bool RestoreJusticeWeaponSnapshotWithApiFallback(Ped player)
    {
        if (!Entity.Exists(player) || !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return false;
        }

        if (!RemoveJusticePlayerWeaponsSafe(player))
        {
            return false;
        }
        bool fullyRestored = true;
        for (int index = 0; index < _justiceWeaponSnapshot.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = _justiceWeaponSnapshot.Weapons[index];
            try
            {
                player.Weapons.Give((WeaponHash)item.WeaponHash, item.Ammo, false, true);
            }
            catch
            {
                fullyRestored = false;
                continue;
            }

            for (int componentIndex = 0; componentIndex < item.ComponentHashes.Count; componentIndex++)
            {
                try
                {
                    Function.Call(
                        Hash.GIVE_WEAPON_COMPONENT_TO_PED,
                        player.Handle,
                        item.WeaponHash,
                        item.ComponentHashes[componentIndex]);
                }
                catch
                {
                    fullyRestored = false;
                }
            }

            try
            {
                Function.Call(
                    Hash.SET_PED_WEAPON_TINT_INDEX,
                    player.Handle,
                    item.WeaponHash,
                    item.Tint);
                Function.Call(
                    Hash.SET_AMMO_IN_CLIP,
                    player.Handle,
                    item.WeaponHash,
                    item.AmmoInClip);
            }
            catch
            {
                fullyRestored = false;
            }
        }

        try
        {
            player.Weapons.Select((WeaponHash)_justiceWeaponSnapshot.SelectedWeaponHash, true);
        }
        catch
        {
            fullyRestored = false;
        }

        return fullyRestored && RestoreJusticeWeaponSnapshotMergeSafe(player, true, true);
    }

    private bool PersistJusticeDeferredRestoreRedundantly()
    {
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        JusticeMarkStateDirty();
        return JusticeFlushStateNow();
    }

    private void RetryJusticeDeferredInventoryRestore(Ped player, int now)
    {
        if (!_justiceDeferredInventoryRestore || _justiceWeaponSnapshot == null ||
            !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            !JusticeCustodyHasReached(now, _justiceNextDeferredInventoryRestoreAt) ||
            IsJusticeRuntimeSuspended(player))
        {
            return;
        }

        _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyDeferredRestoreRetryMs);
        if (!RestoreJusticeWeaponSnapshotMergeSafe(player, true, true))
        {
            return;
        }

        if (!CommitJusticeDeferredInventoryRestore())
        {
            return;
        }

        LogInfo("Justice.Inventaire", "Restitution différée exacte terminée et durcie.");
    }

    private bool CommitJusticeDeferredInventoryRestore()
    {
        JusticeWeaponSnapshot restoredSnapshot = _justiceWeaponSnapshot;
        int restoredPlayerHandle = _justiceCustodyPlayerHandle;
        int restoredModelHash = _justiceCustodyPlayerModelHash;
        int restoredPlayerSlot = _justiceCustodyPlayerSlot;

        _justiceDeferredInventoryRestore = false;
        _justiceWeaponSnapshot = null;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextDeferredInventoryRestoreAt = 0;
        _justiceCustodyPlayerHandle = 0;
        _justiceCustodyPlayerModelHash = 0;
        _justiceCustodyPlayerSlot = -1;
        JusticeMarkStateDirty();
        if (PersistJusticeDeferredRestoreRedundantly())
        {
            return true;
        }

        // Je garde le snapshot en mémoire tant que son effacement n'est pas
        // durable. Le merge exact est idempotent et ne supprime aucune arme.
        _justiceDeferredInventoryRestore = true;
        _justiceWeaponSnapshot = restoredSnapshot;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceCustodyPlayerHandle = restoredPlayerHandle;
        _justiceCustodyPlayerModelHash = restoredModelHash;
        _justiceCustodyPlayerSlot = restoredPlayerSlot;
        _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
            Game.GameTime,
            JusticeCustodyDeferredRestoreRetryMs);
        JusticeMarkStateDirty();
        return false;
    }

    private bool RestoreJusticeWeaponSnapshotMergeSafe(
        Ped player,
        bool requireExactDetails,
        bool restoreSelection)
    {
        if (!Entity.Exists(player) || !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return false;
        }

        bool everyWeaponRestored = true;
        for (int index = 0; index < _justiceWeaponSnapshot.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = _justiceWeaponSnapshot.Weapons[index];
            bool weaponRestored;
            bool alreadyOwned;
            try
            {
                alreadyOwned = Function.Call<bool>(
                    Hash.HAS_PED_GOT_WEAPON,
                    player.Handle,
                    item.WeaponHash,
                    false);
            }
            catch
            {
                // Sans lecture fiable de propriété je n'ajoute rien : un GIVE
                // rejoué à l'aveugle peut dupliquer les munitions à chaque retry.
                everyWeaponRestored = false;
                continue;
            }

            if (!alreadyOwned)
            {
                try
                {
                    // En reprise différée je donne une arme absente une seule
                    // fois avec son stock. Une arme déjà possédée n'est jamais
                    // rechargée, reteintée ou resélectionnée après libération.
                    Function.Call(
                        (Hash)JusticeNativeGiveWeaponToPed,
                        player.Handle,
                        item.WeaponHash,
                        item.Ammo,
                        false,
                        false);
                }
                catch
                {
                    everyWeaponRestored = false;
                    continue;
                }
            }

            try
            {
                weaponRestored = Function.Call<bool>(
                    Hash.HAS_PED_GOT_WEAPON,
                    player.Handle,
                    item.WeaponHash,
                    false);
            }
            catch
            {
                everyWeaponRestored = false;
                continue;
            }

            if (!weaponRestored)
            {
                everyWeaponRestored = false;
                continue;
            }

            if (!requireExactDetails)
            {
                // Le délai exact est épuisé : la présence de l'arme est le seul
                // contrat restant. Je préserve toutes les modifications faites
                // par le joueur depuis sa sortie.
                continue;
            }

            try
            {
                int currentAmmo = Math.Max(0, Function.Call<int>(
                    Hash.GET_AMMO_IN_PED_WEAPON,
                    player.Handle,
                    item.WeaponHash));
                if (currentAmmo < item.Ammo)
                {
                    Function.Call(
                        (Hash)JusticeNativeSetPedAmmo,
                        player.Handle,
                        item.WeaponHash,
                        item.Ammo,
                        false);
                }
                int verifiedAmmo = Math.Max(0, Function.Call<int>(
                    Hash.GET_AMMO_IN_PED_WEAPON,
                    player.Handle,
                    item.WeaponHash));
                if (verifiedAmmo < item.Ammo)
                {
                    everyWeaponRestored = false;
                }
            }
            catch
            {
                everyWeaponRestored = false;
            }

            for (int componentIndex = 0; componentIndex < item.ComponentHashes.Count; componentIndex++)
            {
                try
                {
                    int componentHash = item.ComponentHashes[componentIndex];
                    Function.Call(
                        Hash.GIVE_WEAPON_COMPONENT_TO_PED,
                        player.Handle,
                        item.WeaponHash,
                        componentHash);
                    if (!Function.Call<bool>(
                        Hash.HAS_PED_GOT_WEAPON_COMPONENT,
                        player.Handle,
                        item.WeaponHash,
                        componentHash))
                    {
                        if (requireExactDetails)
                        {
                            everyWeaponRestored = false;
                        }
                    }
                }
                catch
                {
                    if (requireExactDetails)
                    {
                        everyWeaponRestored = false;
                    }
                }
            }

            try
            {
                Function.Call(
                    Hash.SET_PED_WEAPON_TINT_INDEX,
                    player.Handle,
                    item.WeaponHash,
                    item.Tint);
                int restoredTint = Function.Call<int>(
                    Hash.GET_PED_WEAPON_TINT_INDEX,
                    player.Handle,
                    item.WeaponHash);
                if (restoredTint != item.Tint)
                {
                    if (requireExactDetails)
                    {
                        everyWeaponRestored = false;
                    }
                }

                OutputArgument clipOutput = new OutputArgument();
                bool clipRead = Function.Call<bool>(
                    Hash.GET_AMMO_IN_CLIP,
                    player.Handle,
                    item.WeaponHash,
                    clipOutput);
                int currentClip = clipRead ? Math.Max(0, clipOutput.GetResult<int>()) : 0;
                if (!clipRead || currentClip < item.AmmoInClip)
                {
                    Function.Call(
                        Hash.SET_AMMO_IN_CLIP,
                        player.Handle,
                        item.WeaponHash,
                        item.AmmoInClip);
                    OutputArgument verifyClipOutput = new OutputArgument();
                    bool verified = Function.Call<bool>(
                        Hash.GET_AMMO_IN_CLIP,
                        player.Handle,
                        item.WeaponHash,
                        verifyClipOutput);
                    if (!verified || verifyClipOutput.GetResult<int>() < item.AmmoInClip)
                    {
                        if (requireExactDetails)
                        {
                            everyWeaponRestored = false;
                        }
                    }
                }
            }
            catch
            {
                if (requireExactDetails)
                {
                    everyWeaponRestored = false;
                }
            }
        }

        if (everyWeaponRestored && restoreSelection)
        {
            try
            {
                Function.Call(
                    Hash.SET_CURRENT_PED_WEAPON,
                    player.Handle,
                    _justiceWeaponSnapshot.SelectedWeaponHash,
                    true);
                int selectedWeaponHash = Function.Call<int>(
                    (Hash)NativeGetSelectedPedWeapon,
                    player.Handle);
                if (selectedWeaponHash != _justiceWeaponSnapshot.SelectedWeaponHash)
                {
                    everyWeaponRestored = false;
                }
            }
            catch
            {
                everyWeaponRestored = false;
            }
        }
        return everyWeaponRestored;
    }

    private bool JusticeHandleCustodyWorldKey(Keys key)
    {
        if (!IsJusticePlayedProfileCustodyContextReady() || key != Keys.E ||
            _justiceCaseState == null ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated || _justiceDisciplineActive)
        {
            return false;
        }

        Ped player = Game.Player.Character;
        if (!JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return true;
        }

        int now = Game.GameTime;
        if (!string.IsNullOrEmpty(_justiceActiveActivityId))
        {
            ShowStatus("Activité déjà en cours : reste dans la zone jusqu'à la fin.", 2400);
            return true;
        }

        JusticeCustodyActivityDefinition activity = FindNearestJusticeCustodyActivity(
            player.Position,
            JusticeCustodyActivityUseDistance);
        if (activity == null)
        {
            return false;
        }

        int cooldownUntil;
        if (_justiceActivityCooldownUntil.TryGetValue(activity.Id, out cooldownUntil) &&
            !JusticeCustodyHasReached(now, cooldownUntil))
        {
            int seconds = Math.Max(1, (JusticeCustodyMillisecondsUntil(now, cooldownUntil) + 999) / 1000);
            ShowStatus(
                activity.DisplayName + " disponible dans " +
                seconds.ToString(CultureInfo.InvariantCulture) + " s.",
                2400);
            return true;
        }

        StartJusticeCustodyActivity(player, activity, now);
        return true;
    }

    private void StartJusticeCustodyActivity(Ped player, JusticeCustodyActivityDefinition activity, int now)
    {
        if (!Entity.Exists(player) || activity == null)
        {
            return;
        }

        _justiceActiveActivityId = activity.Id;
        _justiceActivityLastTickAt = now;
        _justiceActivityElapsedMs = 0;
        _justiceActivityScenarioValidationPending = false;
        _justiceNextActivityScenarioCheckAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyActivityScenarioGraceMs);

        try
        {
            Function.Call((Hash)JusticeNativeClearPedTasksImmediately, player.Handle);
            _justiceActivityTaskClearPending = false;
            Function.Call(
                (Hash)JusticeNativeTaskStartScenarioInPlace,
                player.Handle,
                activity.ScenarioName,
                -1,
                true);
        }
        catch
        {
            _justiceActivityTaskClearPending = true;
            _justiceNextActivityTaskClearAt = JusticeCustodyFutureTime(now, 750);
        }

        ShowStatus(
            activity.DisplayName + " : " +
            activity.DurationSeconds.ToString(CultureInfo.InvariantCulture) + " s, reste dans la zone.",
            3200);
    }

    private void UpdateJusticeCustodyActivity(Ped player, int now)
    {
        DrawJusticeCustodyActivityMarker(player, now);

        if (string.IsNullOrEmpty(_justiceActiveActivityId))
        {
            return;
        }

        JusticeCustodyActivityDefinition activity = FindJusticeCustodyActivityById(_justiceActiveActivityId);
        if (activity == null || !Entity.Exists(player) || player.IsDead || player.IsInCombat ||
            player.Position.DistanceTo(activity.Position) > JusticeCustodyActivityCancelDistance)
        {
            CancelJusticeCustodyActivity(true, now);
            return;
        }

        if (_justiceActivityScenarioValidationPending &&
            !JusticeCustodyHasReached(now, _justiceNextActivityScenarioCheckAt))
        {
            // Je gèle la progression pendant le backoff : une native indisponible
            // n'annule pas l'activité et ne fait pas gagner du temps gratuitement.
            _justiceActivityElapsedMs = AdvanceJusticeActivityClock(
                _justiceActivityElapsedMs,
                now,
                ref _justiceActivityLastTickAt,
                activity.DurationSeconds * 1000,
                true);
            return;
        }

        if (JusticeCustodyHasReached(now, _justiceNextActivityScenarioCheckAt))
        {
            _justiceNextActivityScenarioCheckAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyActivityScenarioCheckMs);
            bool scenarioActive;
            bool scenarioStateValid = TryCallJusticeBooleanNativeWithCircuit(
                JusticeNativeIsPedUsingAnyScenario,
                JusticeCircuitActivityScenario,
                out scenarioActive,
                player.Handle);
            if (!scenarioStateValid)
            {
                _justiceActivityScenarioValidationPending = true;
                _justiceNextActivityScenarioCheckAt = JusticeCustodyFutureTime(
                    now,
                    JusticeNativeCircuitRetryMs);
                _justiceActivityElapsedMs = AdvanceJusticeActivityClock(
                    _justiceActivityElapsedMs,
                    now,
                    ref _justiceActivityLastTickAt,
                    activity.DurationSeconds * 1000,
                    true);
                return;
            }
            _justiceActivityScenarioValidationPending = false;
            if (!scenarioActive)
            {
                CancelJusticeCustodyActivity(true, now);
                return;
            }
        }

        _justiceActivityElapsedMs = AdvanceJusticeActivityClock(
            _justiceActivityElapsedMs,
            now,
            ref _justiceActivityLastTickAt,
            activity.DurationSeconds * 1000,
            false);
        if (_justiceActivityElapsedMs < activity.DurationSeconds * 1000)
        {
            return;
        }

        int maximumReduction = GetJusticeCustodyMaximumActivityReduction();
        int remainingAllowance = Math.Max(0, maximumReduction - _justiceActivityReductionGrantedSeconds);
        int granted = Math.Min(
            Math.Min(activity.ReductionSeconds, remainingAllowance),
            Math.Max(0, _justiceCaseState.SentenceSeconds));

        _justiceCaseState.SentenceSeconds = Math.Max(0, _justiceCaseState.SentenceSeconds - granted);
        _justiceActivityReductionGrantedSeconds += granted;
        _justiceActivityCooldownUntil[activity.Id] = JusticeCustodyFutureTime(
            now,
            activity.CooldownSeconds * 1000);
        _justiceActiveActivityId = string.Empty;
        _justiceActivityLastTickAt = 0;
        _justiceActivityElapsedMs = 0;
        _justiceNextActivityScenarioCheckAt = 0;
        _justiceActivityScenarioValidationPending = false;

        try
        {
            Function.Call((Hash)JusticeNativeClearPedTasksImmediately, player.Handle);
            _justiceActivityTaskClearPending = false;
        }
        catch
        {
            _justiceActivityTaskClearPending = true;
            _justiceNextActivityTaskClearAt = JusticeCustodyFutureTime(now, 750);
        }

        JusticeMarkStateDirty();
        ShowStatus(
            granted > 0
                ? activity.DisplayName + " terminée : -" + granted.ToString(CultureInfo.InvariantCulture) + " s."
                : "Plafond de réduction d'activités déjà atteint.",
            3200);
    }

    internal static int AdvanceJusticeActivityClock(
        int elapsedMs,
        int now,
        ref int lastTickAt,
        int durationMs,
        bool frozen)
    {
        if (frozen)
        {
            // Je replace uniquement le point de départ pendant une vraie pause de
            // validation native. Une sonde valide ne consomme donc aucune frame.
            lastTickAt = now;
            return Math.Max(0, Math.Min(Math.Max(0, durationMs), elapsedMs));
        }

        uint rawElapsed = unchecked((uint)(now - lastTickAt));
        lastTickAt = now;
        return JusticeCustodySaturatingAdd(
            elapsedMs,
            (int)Math.Min((uint)JusticeCustodyMaxFrameElapsedMs, rawElapsed),
            Math.Max(0, durationMs));
    }

    private int GetJusticeCustodyMaximumActivityReduction()
    {
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        int siteMaximum = layout == null ? 0 : layout.MaximumActivityReductionSeconds;
        int sentenceMaximum = Math.Max(0, _justiceCustodyInitialSentenceSeconds / 4);
        return Math.Min(siteMaximum, sentenceMaximum);
    }

    private void CancelJusticeCustodyActivity(bool interrupted, int now)
    {
        if (string.IsNullOrEmpty(_justiceActiveActivityId))
        {
            return;
        }

        JusticeCustodyActivityDefinition activity = FindJusticeCustodyActivityById(_justiceActiveActivityId);
        if (activity != null && interrupted)
        {
            _justiceActivityCooldownUntil[activity.Id] = JusticeCustodyFutureTime(now, 15000);
        }

        _justiceActiveActivityId = string.Empty;
        _justiceActivityLastTickAt = 0;
        _justiceActivityElapsedMs = 0;
        _justiceNextActivityScenarioCheckAt = 0;
        _justiceActivityScenarioValidationPending = false;

        Ped player = Game.Player.Character;
        TryClearJusticeCustodyPlayerTasks(player, now);

        if (interrupted)
        {
            ShowStatus("Activité interrompue : aucune réduction accordée.", 2500);
        }
    }

    private bool TryClearJusticeCustodyPlayerTasks(Ped player, int now)
    {
        if (!JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            _justiceActivityTaskClearPending = true;
            _justiceNextActivityTaskClearAt = 0;
            return false;
        }

        try
        {
            Function.Call((Hash)JusticeNativeClearPedTasksImmediately, player.Handle);
            _justiceActivityTaskClearPending = false;
            _justiceNextActivityTaskClearAt = 0;
            return true;
        }
        catch
        {
            _justiceActivityTaskClearPending = true;
            _justiceNextActivityTaskClearAt = JusticeCustodyFutureTime(now, 750);
            return false;
        }
    }

    private void RetryJusticeCustodyActivityTaskClear(Ped player, int now)
    {
        if (!_justiceActivityTaskClearPending ||
            !JusticeCustodyHasReached(now, _justiceNextActivityTaskClearAt) ||
            !JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return;
        }

        TryClearJusticeCustodyPlayerTasks(player, now);
    }

    private void DrawJusticeCustodyActivityMarker(Ped player, int now)
    {
        if (!Entity.Exists(player) || !string.IsNullOrEmpty(_justiceActiveActivityId))
        {
            return;
        }

        JusticeCustodyActivityDefinition activity = FindNearestJusticeCustodyActivity(player.Position, 22.0f);
        if (activity == null)
        {
            return;
        }

        int cooldownUntil;
        bool coolingDown = _justiceActivityCooldownUntil.TryGetValue(activity.Id, out cooldownUntil) &&
                           !JusticeCustodyHasReached(now, cooldownUntil);
        Color color = coolingDown
            ? Color.FromArgb(120, 120, 130, 140)
            : Color.FromArgb(185, 48, 190, 220);
        World.DrawMarker(
            MarkerType.VerticalCylinder,
            activity.Position + new Vector3(0.0f, 0.0f, -0.95f),
            Vector3.Zero,
            Vector3.Zero,
            new Vector3(0.55f, 0.55f, 0.22f),
            color);
    }

    private JusticeCustodyActivityDefinition FindNearestJusticeCustodyActivity(
        Vector3 position,
        float maximumDistance)
    {
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null || layout.Activities == null)
        {
            return null;
        }

        JusticeCustodyActivityDefinition nearest = null;
        float nearestDistance = maximumDistance;
        for (int index = 0; index < layout.Activities.Length; index++)
        {
            JusticeCustodyActivityDefinition candidate = layout.Activities[index];
            if (candidate == null)
            {
                continue;
            }

            float distance = position.DistanceTo(candidate.Position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private JusticeCustodyActivityDefinition FindJusticeCustodyActivityById(string id)
    {
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null || layout.Activities == null || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        for (int index = 0; index < layout.Activities.Length; index++)
        {
            JusticeCustodyActivityDefinition activity = layout.Activities[index];
            if (activity != null && string.Equals(activity.Id, id, StringComparison.Ordinal))
            {
                return activity;
            }
        }

        return null;
    }

    private void UpdateJusticeCustodyDiscipline(Ped player, int now)
    {
        if (_justiceDisciplineIntent != null && !_justiceDisciplineActive)
        {
            if (!Entity.Exists(player) || player.IsDead ||
                !IsJusticeCustodyPlayerIdentityCompatible(player) ||
                !JusticeCustodyHasReached(now, _justiceNextDisciplineReturnAttemptAt))
            {
                return;
            }

            // Je finalise immédiatement une intention relue depuis le XML. Le
            // même incidentId rend cette reprise idempotente si le précédent
            // processus avait déjà ajouté la charge avant de s'arrêter.
            _justiceDisciplineStoredInvincible = _justiceCustodyPlayerStateStored
                ? _justiceCustodyStoredInvincible
                : false;
            _justiceDisciplineCrimeKind = _justiceDisciplineIntent.CrimeKind;
            _justiceDisciplineIncidentId = _justiceDisciplineIntent.IncidentId;
            _justiceDisciplineActive = true;
            _justiceDisciplineEndsAt = now;
            CompleteJusticeCustodyDiscipline(player, now);
            return;
        }

        if (_justiceDisciplineActive)
        {
            EnforceJusticeCustodyWeaponLock(player);
            if (player.IsBeingStunned || JusticeCustodyHasReached(now, _justiceDisciplineEndsAt))
            {
                CompleteJusticeCustodyDiscipline(player, now);
            }

            return;
        }

        if (!JusticeCustodyHasReached(now, _justiceNextDisciplineScanAt))
        {
            return;
        }

        _justiceNextDisciplineScanAt = JusticeCustodyFutureTime(now, JusticeCustodyDisciplineScanMs);
        JusticeCrimeKind crimeKind;
        if (!TryGetJusticeCustodyMisconduct(player, out crimeKind))
        {
            return;
        }

        bool homicide = crimeKind == JusticeCrimeKind.MurderOfficer ||
                         crimeKind == JusticeCrimeKind.MurderCivilian;
        if (!homicide && !JusticeCustodyHasReached(now, _justiceDisciplineCooldownUntil))
        {
            return;
        }

        BeginJusticeCustodyDiscipline(player, now, crimeKind);
    }

    private bool TryBeginJusticeCustodyDisciplineFromCurrentEvidence(Ped player, int now)
    {
        if (_justiceDisciplineIntent != null || _justiceDisciplineActive ||
            !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        JusticeCrimeKind crimeKind;
        if (!TryGetJusticeCustodyMisconduct(player, out crimeKind))
        {
            return false;
        }

        bool homicide = crimeKind == JusticeCrimeKind.MurderOfficer ||
                         crimeKind == JusticeCrimeKind.MurderCivilian;
        if (!homicide && !JusticeCustodyHasReached(now, _justiceDisciplineCooldownUntil))
        {
            return false;
        }

        BeginJusticeCustodyDiscipline(player, now, crimeKind);
        return true;
    }

    private bool FinalizeJusticePendingDisciplineBeforeCustodyExit(Ped player, int now)
    {
        // Je force un dernier front avant tout WAL de sortie : la cadence normale
        // ne peut pas faire disparaître une victime tuée à la dernière frame.
        TryBeginJusticeCustodyDisciplineFromCurrentEvidence(player, now);
        if (_justiceDisciplineIntent == null)
        {
            return true;
        }

        if (!_justiceDisciplineActive)
        {
            if (!Entity.Exists(player) || player.IsDead ||
                !IsJusticeCustodyPlayerIdentityCompatible(player))
            {
                return false;
            }

            _justiceDisciplineStoredInvincible = _justiceCustodyPlayerStateStored
                ? _justiceCustodyStoredInvincible
                : false;
            _justiceDisciplineCrimeKind = _justiceDisciplineIntent.CrimeKind;
            _justiceDisciplineIncidentId = _justiceDisciplineIntent.IncidentId;
            _justiceDisciplineActive = true;
            _justiceDisciplineEndsAt = now;
        }

        CompleteJusticeCustodyDiscipline(player, now);
        return _justiceDisciplineIntent == null;
    }

    private bool TryGetJusticeCustodyMisconduct(Ped player, out JusticeCrimeKind crimeKind)
    {
        crimeKind = JusticeCrimeKind.ReportedViolentAct;
        if (!Entity.Exists(player))
        {
            return false;
        }

        for (int index = 0; index < _justiceCustodyGuards.Count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard))
            {
                continue;
            }
            bool damagedByPlayer = TryCaptureJusticeDamageFront(guard, player);
            long causalDamageAtMs = damagedByPlayer ? _justiceMonotonicTimeMs : -1L;
            if (guard.IsDead && IsJusticeDeathAttributedTo(guard, player, null, causalDamageAtMs))
            {
                crimeKind = JusticeCrimeKind.MurderOfficer;
                return true;
            }
            if (damagedByPlayer)
            {
                crimeKind = JusticeCrimeKind.AssaultOfficer;
                return true;
            }
        }

        for (int index = 0; index < _justiceCustodyInmates.Count; index++)
        {
            Ped inmate = _justiceCustodyInmates[index];
            if (!IsJusticeCustodyPedOwnershipValid(inmate))
            {
                continue;
            }
            bool playerDamagedByInmate = TryCaptureJusticeDamageFront(player, inmate);
            if (playerDamagedByInmate && !inmate.IsDead)
            {
                RememberJusticeCustodyAggressor(inmate);
            }

            bool damagedByPlayer = TryCaptureJusticeDamageFront(inmate, player);
            long causalDamageAtMs = damagedByPlayer ? _justiceMonotonicTimeMs : -1L;
            if (inmate.IsDead && IsJusticeDeathAttributedTo(inmate, player, null, causalDamageAtMs))
            {
                crimeKind = JusticeCrimeKind.MurderCivilian;
                return true;
            }
            if (damagedByPlayer)
            {
                bool canUseUnarmedCombat = JusticePolicy.CanUseCustodyUnarmedCombat(
                    _justiceInventoryRemoved,
                    _justiceWeaponControlsLocked);
                if (HasFreshJusticeCustodyAggression(inmate, canUseUnarmedCombat))
                {
                    // Je laisse une riposte non létale aux poings contre le
                    // détenu qui vient réellement d'attaquer le joueur.
                    continue;
                }
                crimeKind = JusticeCrimeKind.SimpleAssault;
                return true;
            }
        }

        return false;
    }

    private void EnsureJusticeCustodyAggressorBuffers()
    {
        if (_justiceCustodyAggressorHandles == null ||
            _justiceCustodyAggressorHandles.Length != JusticeCustodyTrackedAggressorCapacity)
        {
            _justiceCustodyAggressorHandles =
                new int[JusticeCustodyTrackedAggressorCapacity];
        }
        if (_justiceCustodyAggressorGenerations == null ||
            _justiceCustodyAggressorGenerations.Length != JusticeCustodyTrackedAggressorCapacity)
        {
            _justiceCustodyAggressorGenerations =
                new int[JusticeCustodyTrackedAggressorCapacity];
        }
        if (_justiceCustodyAggressorUntilMs == null ||
            _justiceCustodyAggressorUntilMs.Length != JusticeCustodyTrackedAggressorCapacity)
        {
            _justiceCustodyAggressorUntilMs =
                new long[JusticeCustodyTrackedAggressorCapacity];
        }
    }

    private void RememberJusticeCustodyAggressor(Ped inmate)
    {
        if (!Entity.Exists(inmate) || inmate.IsDead)
        {
            return;
        }

        EnsureJusticeCustodyAggressorBuffers();
        int handle = inmate.Handle;
        int generation = GetJusticeEntityGeneration(inmate);
        if (handle == 0 || generation <= 0)
        {
            return;
        }

        int selectedIndex = -1;
        long oldestExpiry = long.MaxValue;
        for (int index = 0; index < JusticeCustodyTrackedAggressorCapacity; index++)
        {
            if (_justiceCustodyAggressorHandles[index] == handle &&
                _justiceCustodyAggressorGenerations[index] == generation)
            {
                selectedIndex = index;
                break;
            }
            if (_justiceCustodyAggressorUntilMs[index] <= _justiceMonotonicTimeMs)
            {
                selectedIndex = index;
                break;
            }
            if (_justiceCustodyAggressorUntilMs[index] < oldestExpiry)
            {
                oldestExpiry = _justiceCustodyAggressorUntilMs[index];
                selectedIndex = index;
            }
        }

        _justiceCustodyAggressorHandles[selectedIndex] = handle;
        _justiceCustodyAggressorGenerations[selectedIndex] = generation;
        _justiceCustodyAggressorUntilMs[selectedIndex] =
            _justiceMonotonicTimeMs + JusticeCustodySelfDefenseWindowMs;
    }

    private bool HasFreshJusticeCustodyAggression(Ped inmate, bool canUseUnarmedCombat)
    {
        if (!Entity.Exists(inmate))
        {
            return false;
        }

        EnsureJusticeCustodyAggressorBuffers();
        int handle = inmate.Handle;
        int generation = GetJusticeEntityGeneration(inmate);
        for (int index = 0; index < JusticeCustodyTrackedAggressorCapacity; index++)
        {
            if (_justiceCustodyAggressorHandles[index] != handle ||
                _justiceCustodyAggressorGenerations[index] != generation)
            {
                continue;
            }

            return IsJusticeCustodySelfDefenseWindowActive(
                _justiceMonotonicTimeMs,
                _justiceCustodyAggressorUntilMs[index],
                inmate.IsDead,
                canUseUnarmedCombat);
        }

        return false;
    }

    private static bool IsJusticeCustodySelfDefenseWindowActive(
        long nowMs,
        long expiresAtMs,
        bool inmateDead,
        bool canUseUnarmedCombat)
    {
        return canUseUnarmedCombat && !inmateDead &&
               expiresAtMs > 0L && nowMs >= 0L && nowMs < expiresAtMs;
    }

    private void BeginJusticeCustodyDiscipline(Ped player, int now, JusticeCrimeKind crimeKind)
    {
        if (!Entity.Exists(player) || _justiceDisciplineIntent != null)
        {
            return;
        }

        CancelJusticeCustodyActivity(false, now);
        _justiceDisciplineIntent = new JusticeDisciplineIntent
        {
            CrimeKind = crimeKind,
            PenaltySeconds = _justiceCustodySite == JusticeCustodySite.Bolingbroke ? 120 : 60,
            IncidentId = "discipline:" +
            (_justiceCaseState == null ? string.Empty : _justiceCaseState.CustodyEpisodeId) + ":" +
            Guid.NewGuid().ToString("N")
        };
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            ShowStatus("Discipline en attente : sécurisation de l'incident…", 2200);
            return;
        }

        _justiceDisciplineActive = true;
        _justiceDisciplineEndsAt = JusticeCustodyFutureTime(now, JusticeCustodyDisciplineDurationMs);
        try
        {
            _justiceDisciplineStoredInvincible = player.IsInvincible;
        }
        catch
        {
            _justiceDisciplineStoredInvincible = _justiceCustodyPlayerStateStored
                ? _justiceCustodyStoredInvincible
                : false;
        }
        _justiceDisciplineCrimeKind = _justiceDisciplineIntent.CrimeKind;
        _justiceDisciplineIncidentId = _justiceDisciplineIntent.IncidentId;
        _justiceDisciplineReturnStartedAt = 0;
        _justiceNextDisciplineReturnAttemptAt = 0;
        _justiceDisciplineReturnFailureCount = 0;
        bool nonLethalProtectionVerified = false;
        bool invincibilityMutationMayHaveStarted = false;
        try
        {
            player.IsInvincible = true;
            invincibilityMutationMayHaveStarted = true;
            nonLethalProtectionVerified = player.IsInvincible;
        }
        catch
        {
            // Le setter peut avoir réussi avant que le getter de vérification ne
            // lève. Je restaure donc toujours la valeur capturée dans ce cas.
            invincibilityMutationMayHaveStarted = true;
            nonLethalProtectionVerified = false;
        }
        if (!nonLethalProtectionVerified)
        {
            // Sans invulnérabilité vérifiée, aucun garde ne reçoit d'ordre de
            // combat. L'intention durable sera jugée sans exposer le joueur.
            _justiceDisciplineActive = false;
            _justiceDisciplineEndsAt = 0;
            if (invincibilityMutationMayHaveStarted)
            {
                _justiceDisciplineInvincibilityRestorePending = true;
                TryRestoreJusticeDisciplineInvincibility(player);
            }
            _justiceNextDisciplineReturnAttemptAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyDisciplineRetryInitialMs);
            ShowStatus("Discipline différée : protection non létale indisponible.", 2800);
            return;
        }

        for (int index = 0; index < _justiceCustodyGuards.Count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard) || guard.IsDead)
            {
                continue;
            }

            try
            {
                guard.Weapons.Select((WeaponHash)JusticeStunGunHash, true);
                Function.Call(
                    Hash.TASK_COMBAT_PED,
                    guard.Handle,
                    player.Handle,
                    0,
                    16);
            }
            catch
            {
                try
                {
                    Function.Call(
                        Hash.TASK_COMBAT_PED,
                        guard.Handle,
                        player.Handle,
                        0,
                        16);
                }
                catch
                {
                }
            }
        }

        ShowStatus("Discipline : les gardiens utilisent une riposte non létale.", 3000);
    }

    private void CompleteJusticeCustodyDiscipline(Ped player, int now)
    {
        if (!JusticeCustodyHasReached(now, _justiceNextDisciplineReturnAttemptAt))
        {
            EndJusticeCustodyDiscipline(player);
            return;
        }
        if (_justiceDisciplineReturnStartedAt == 0)
        {
            _justiceDisciplineReturnStartedAt = now;
        }

        if (_justiceDisciplineIntent == null)
        {
            _justiceDisciplineIntent = new JusticeDisciplineIntent
            {
                CrimeKind = _justiceDisciplineCrimeKind,
                IncidentId = _justiceDisciplineIncidentId,
                PenaltySeconds = _justiceCustodySite == JusticeCustodySite.Bolingbroke ? 120 : 60
            };
            JusticeMarkStateDirty();
        }

        JusticeCrimeKind finalKind;
        if (TryGetJusticeCustodyMisconduct(player, out finalKind) &&
            GetJusticeCustodyCrimePriority(finalKind) >
                GetJusticeCustodyCrimePriority(_justiceDisciplineIntent.CrimeKind))
        {
            _justiceDisciplineIntent.CrimeKind = finalKind;
            JusticeMarkStateDirty();
        }

        // Je précommitte aussi un éventuel surclassement en homicide avant la
        // téléportation et avant toute mutation du dossier.
        if (!JusticeFlushStateNow())
        {
            ShowStatus("Discipline en attente : reprise sécurisée…", 2200);
            StandDownJusticeCustodyDisciplineForRetry(player, now);
            return;
        }

        _justiceDisciplineCrimeKind = _justiceDisciplineIntent.CrimeKind;
        _justiceDisciplineIncidentId = _justiceDisciplineIntent.IncidentId;
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout != null && Entity.Exists(player))
        {
            bool returnedToCell = false;
            try
            {
                TeleportPlayerWithFadeSafe(player, layout.CellPosition, layout.CellHeading);
                returnedToCell = IsJusticeTeleportVerified(player, layout.CellPosition, 8.0f);
            }
            catch (Exception ex)
            {
                LogException("Justice.Discipline", ex);
            }
            if (!returnedToCell)
            {
                returnedToCell = TryJusticeEmergencyTeleport(
                    player,
                    layout.CellPosition,
                    layout.CellHeading);
            }

            if (!returnedToCell)
            {
                _justiceDisciplineReturnFailureCount = Math.Min(
                    16,
                    _justiceDisciplineReturnFailureCount + 1);
                int exponent = Math.Min(
                    3,
                    Math.Max(0, _justiceDisciplineReturnFailureCount - 1));
                int retryDelay = Math.Min(
                    JusticeCustodyDisciplineRetryMaximumMs,
                    JusticeCustodyDisciplineRetryInitialMs * (1 << exponent));
                _justiceNextDisciplineReturnAttemptAt = JusticeCustodyFutureTime(
                    now,
                    retryDelay);
                bool timedOut = unchecked((uint)(now - _justiceDisciplineReturnStartedAt)) >=
                                (uint)JusticeCustodyDisciplineReturnTimeoutMs;
                EndJusticeCustodyDiscipline(player);
                if (!timedOut)
                {
                    return;
                }

                // Je ne garde jamais le joueur invincible ou les gardes en combat
                // indéfiniment si GTA refuse tous les chemins de téléportation.
                ShowStatus(
                    "Justice : retour cellule impossible, sanction appliquée sur place.",
                    4200);
                LogWarning(
                    "Justice.Discipline",
                    "Téléportation cellule abandonnée après timeout; discipline finalisée sans soft-lock.");
            }
        }

        if (!JusticeRegisterCustodyDisciplineCharge(
            _justiceDisciplineIntent.CrimeKind,
            _justiceDisciplineIntent.PenaltySeconds,
            "Incident disciplinaire en détention",
            _justiceDisciplineIntent.IncidentId))
        {
            StandDownJusticeCustodyDisciplineForRetry(player, now);
            return;
        }

        if (_justiceCaseState != null && _justiceCaseState.Phase == JusticePhase.Escaping)
        {
            ApplyJusticeTransition(
                JusticeSignal.Restrained,
                _justiceCaseState.CustodyEpisodeId);
        }

        if (!EndJusticeCustodyDiscipline(player))
        {
            // Je conserve l'intention durable tant que l'invulnérabilité
            // temporaire n'a pas été réellement restaurée. Au reload, ce même
            // incident reprend sans ajouter une seconde charge.
            JusticeMarkStateDirty();
            JusticeFlushStateNow();
            return;
        }

        JusticeDisciplineIntent completedIntent = _justiceDisciplineIntent;
        _justiceDisciplineIntent = null;
        _justiceDisciplineReturnStartedAt = 0;
        _justiceNextDisciplineReturnAttemptAt = 0;
        _justiceDisciplineReturnFailureCount = 0;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            _justiceDisciplineIntent = completedIntent;
            JusticeMarkStateDirty();
            return;
        }
        _justiceDisciplineCooldownUntil = JusticeCustodyFutureTime(now, JusticeCustodyDisciplineCooldownMs);
        _justiceOutsideCustodySinceAt = 0;
        ClearJusticeCustodyDamageMemory(player);
        JusticeMarkStateDirty();
    }

    private void StandDownJusticeCustodyDisciplineForRetry(Ped player, int now)
    {
        if (_justiceDisciplineReturnStartedAt == 0)
        {
            _justiceDisciplineReturnStartedAt = now;
        }
        _justiceDisciplineReturnFailureCount = Math.Min(
            16,
            _justiceDisciplineReturnFailureCount + 1);
        int exponent = Math.Min(3, Math.Max(0, _justiceDisciplineReturnFailureCount - 1));
        _justiceNextDisciplineReturnAttemptAt = JusticeCustodyFutureTime(
            now,
            Math.Min(
                JusticeCustodyDisciplineRetryMaximumMs,
                JusticeCustodyDisciplineRetryInitialMs * (1 << exponent)));
        EndJusticeCustodyDiscipline(player);
    }

    private bool EndJusticeCustodyDiscipline(Ped player)
    {
        if (!_justiceDisciplineActive &&
            !_justiceDisciplineInvincibilityRestorePending)
        {
            return true;
        }

        _justiceDisciplineInvincibilityRestorePending = true;
        _justiceDisciplineActive = false;
        _justiceDisciplineEndsAt = 0;
        _justiceDisciplineCrimeKind = JusticeCrimeKind.ReportedViolentAct;
        _justiceDisciplineIncidentId = string.Empty;
        bool playerRestored = TryRestoreJusticeDisciplineInvincibility(player);

        for (int index = 0; index < _justiceCustodyGuards.Count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard))
            {
                continue;
            }

            try
            {
                Function.Call((Hash)JusticeNativeClearPedTasksImmediately, guard.Handle);
                guard.Weapons.Select((WeaponHash)JusticeStunGunHash, true);
            }
            catch
            {
            }
        }
        return playerRestored;
    }

    private bool TryRestoreJusticeDisciplineInvincibility(Ped player)
    {
        if (!_justiceDisciplineInvincibilityRestorePending)
        {
            return true;
        }
        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        try
        {
            player.IsInvincible = _justiceDisciplineStoredInvincible;
            if (player.IsInvincible != _justiceDisciplineStoredInvincible)
            {
                return false;
            }
            _justiceDisciplineInvincibilityRestorePending = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetJusticeCustodyCrimePriority(JusticeCrimeKind kind)
    {
        switch (kind)
        {
            case JusticeCrimeKind.MurderOfficer:
                return 6;
            case JusticeCrimeKind.MurderCivilian:
                return 5;
            case JusticeCrimeKind.AssaultOfficer:
                return 4;
            case JusticeCrimeKind.SimpleAssault:
                return 3;
            case JusticeCrimeKind.RecklessDischarge:
                return 2;
            case JusticeCrimeKind.ReportedViolentAct:
            default:
                return 1;
        }
    }

    private void ClearJusticeCustodyDamageMemory(Ped player)
    {
        try
        {
            if (Entity.Exists(player))
            {
                Function.Call((Hash)JusticeNativeClearPedLastWeaponDamage, player.Handle);
                Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle);
            }

            for (int index = 0; index < _justiceCustodyGuards.Count; index++)
            {
                Ped guard = _justiceCustodyGuards[index];
                if (IsJusticeCustodyPedOwnershipValid(guard))
                {
                    Function.Call((Hash)JusticeNativeClearPedLastWeaponDamage, guard.Handle);
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, guard.Handle);
                }
            }

            for (int index = 0; index < _justiceCustodyInmates.Count; index++)
            {
                Ped inmate = _justiceCustodyInmates[index];
                if (IsJusticeCustodyPedOwnershipValid(inmate))
                {
                    Function.Call((Hash)JusticeNativeClearPedLastWeaponDamage, inmate.Handle);
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, inmate.Handle);
                }
            }
        }
        catch
        {
        }
    }

    private int _justiceCustodyGuardGroupHash;
    private int _justiceCustodyInmateGroupHash;

    private void EnsureJusticeCustodyRelationshipGroups()
    {
        try
        {
            if (_justiceCustodyGuardGroupHash == 0)
            {
                _justiceCustodyGuardGroupHash = World.AddRelationshipGroup("DONJ_JUSTICE_GUARD");
            }

            if (_justiceCustodyInmateGroupHash == 0)
            {
                _justiceCustodyInmateGroupHash = World.AddRelationshipGroup("DONJ_JUSTICE_INMATE");
            }

            int playerGroup = GetPlayerRelationshipGroup();
            if (_justiceCustodyGuardGroupHash != 0)
            {
                SetRelationshipBothWays(
                    (Relationship)RelationshipCompanion,
                    _justiceCustodyGuardGroupHash,
                    _justiceCustodyGuardGroupHash);
            }

            if (_justiceCustodyInmateGroupHash != 0)
            {
                SetRelationshipBothWays(
                    (Relationship)RelationshipCompanion,
                    _justiceCustodyInmateGroupHash,
                    _justiceCustodyInmateGroupHash);
            }

            if (_justiceCustodyGuardGroupHash != 0 && _justiceCustodyInmateGroupHash != 0)
            {
                SetRelationshipBothWays(
                    (Relationship)RelationshipNeutral,
                    _justiceCustodyGuardGroupHash,
                    _justiceCustodyInmateGroupHash);
            }

            if (playerGroup != 0)
            {
                if (_justiceCustodyGuardGroupHash != 0)
                {
                    SetRelationshipBothWays(
                        (Relationship)RelationshipNeutral,
                        _justiceCustodyGuardGroupHash,
                        playerGroup);
                }

                if (_justiceCustodyInmateGroupHash != 0)
                {
                    SetRelationshipBothWays(
                        (Relationship)RelationshipNeutral,
                        _justiceCustodyInmateGroupHash,
                        playerGroup);
                }
            }
        }
        catch (Exception ex)
        {
            LogException("Justice.RelationsDetention", ex);
        }
    }

    private void EnsureJusticeCustodyScene(int now)
    {
        if (!JusticeCustodyHasReached(now, _justiceNextCustodySceneRefreshAt))
        {
            return;
        }

        _justiceNextCustodySceneRefreshAt = JusticeCustodyFutureTime(now, JusticeCustodySceneRefreshMs);
        if (_justiceDisciplineIntent != null || _justiceDisciplineActive)
        {
            // Je conserve les victimes et la scène tant que l'incident durable
            // n'est pas entièrement jugé et committé.
            return;
        }

        Ped player = Game.Player.Character;
        if (Entity.Exists(player) && !player.IsDead &&
            IsJusticeCustodyPlayerIdentityCompatible(player) &&
            TryBeginJusticeCustodyDisciplineFromCurrentEvidence(player, now))
        {
            // Je diffère toute compaction après avoir capturé le front final.
            return;
        }

        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null)
        {
            return;
        }

        EnsureJusticeCustodyRelationshipGroups();
        CompactJusticeCustodyPedList(_justiceCustodyGuards);
        CompactJusticeCustodyPedList(_justiceCustodyInmates);

        int guardTarget = _justiceCustodySite == JusticeCustodySite.Bolingbroke ? 4 : 2;
        if (_justiceCustodyGuards.Count < guardTarget)
        {
            int index = _justiceCustodyGuards.Count;
            Vector3 position = layout.GuardPositions[Math.Min(index, layout.GuardPositions.Length - 1)];
            float heading = layout.GuardHeadings[Math.Min(index, layout.GuardHeadings.Length - 1)];
            Ped guard = CreateJusticeCustodyPed(
                "s_m_m_prisguard_01",
                position,
                heading,
                true);
            if (!Entity.Exists(guard))
            {
                return;
            }
            if (!RememberJusticeCustodyPedOwnership(guard))
            {
                DeleteEntitySafe(guard);
                return;
            }

            _justiceCustodyGuards.Add(guard);
            // Je limite la création à une seule entité par rafraîchissement afin
            // que le streaming d'un modèle ne provoque jamais une longue saccade.
            return;
        }

        int inmateTarget = _justiceCustodySite == JusticeCustodySite.Bolingbroke ? 8 : 0;
        if (_justiceCustodyInmates.Count < inmateTarget)
        {
            int index = _justiceCustodyInmates.Count;
            Vector3 position = layout.InmatePositions[Math.Min(index, layout.InmatePositions.Length - 1)];
            Ped inmate = CreateJusticeCustodyPed(
                JusticeCustodyInmateModels[index % JusticeCustodyInmateModels.Length],
                position,
                (index * 47.0f) % 360.0f,
                false);
            if (!Entity.Exists(inmate))
            {
                return;
            }
            if (!RememberJusticeCustodyPedOwnership(inmate))
            {
                DeleteEntitySafe(inmate);
                return;
            }

            _justiceCustodyInmates.Add(inmate);
        }
    }

    private Ped CreateJusticeCustodyPed(
        string modelName,
        Vector3 position,
        float heading,
        bool guard)
    {
        int now = Game.GameTime;
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyModelRetryAt))
        {
            return null;
        }

        Model model = new Model(modelName);
        Ped ped = null;
        try
        {
            if (!model.IsValid || !model.IsInCdImage || !model.IsPed ||
                !model.Request(JusticeCustodyModelTimeoutMs))
            {
                _justiceNextCustodyModelRetryAt = JusticeCustodyFutureTime(
                    now,
                    JusticeCustodyModelRetryMs);
                return null;
            }

            ped = World.CreatePed(model, position, NormalizeHeading(heading));
            if (!Entity.Exists(ped))
            {
                _justiceNextCustodyModelRetryAt = JusticeCustodyFutureTime(
                    now,
                    JusticeCustodyModelRetryMs);
                return null;
            }

            _justiceNextCustodyModelRetryAt = 0;

            ped.IsPersistent = true;
            ped.AlwaysKeepTask = true;
            ped.BlockPermanentEvents = true;
            ped.MaxHealth = guard ? 350 : 250;
            ped.Health = guard ? 350 : 250;
            ped.Armor = guard ? 100 : 0;
            ped.Accuracy = guard ? 25 : 10;
            ped.CanRagdoll = true;
            ped.CanBeTargetted = true;
            ped.IsEnemy = false;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, false);
            Function.Call(Hash.SET_PED_DROPS_WEAPONS_WHEN_DEAD, ped.Handle, false);
            Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, false);
            Function.Call((Hash)JusticeNativeSetBlockingOfNonTemporaryEvents, ped.Handle, true);
            Function.Call((Hash)JusticeNativeSetPedKeepTask, ped.Handle, true);
            Function.Call(
                Hash.SET_PED_RELATIONSHIP_GROUP_HASH,
                ped.Handle,
                guard ? _justiceCustodyGuardGroupHash : _justiceCustodyInmateGroupHash);

            if (guard)
            {
                // Je retire d'abord l'équipement natif du modèle : un gardien
                // de détention ne doit jamais conserver un pistolet caché en
                // plus du taser et de la matraque autorisés.
                ped.Weapons.RemoveAll();
                ped.Weapons.Give((WeaponHash)JusticeStunGunHash, 9999, true, true);
                ped.Weapons.Give((WeaponHash)JusticeNightstickHash, 1, false, true);
                ped.Weapons.Select((WeaponHash)JusticeStunGunHash, true);
                Function.Call(Hash.TASK_STAND_STILL, ped.Handle, -1);
            }
            else
            {
                ped.Weapons.RemoveAll();
                Function.Call((Hash)JusticeNativeTaskWanderStandard, ped.Handle, 10.0f, 10);
            }

            return ped;
        }
        catch (Exception ex)
        {
            LogException("Justice.SpawnDetention", ex);
            if (Entity.Exists(ped))
            {
                DeleteEntitySafe(ped);
            }
            return null;
        }
        finally
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

    private void EnsureJusticeCustodyPedGenerationMap()
    {
        if (_justiceCustodyPedGenerationByHandle == null)
        {
            _justiceCustodyPedGenerationByHandle = new Dictionary<int, int>();
        }
    }

    private bool RememberJusticeCustodyPedOwnership(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            int handle = ped.Handle;
            int generation = GetJusticeEntityGeneration(ped);
            if (handle == 0 || generation <= 0)
            {
                return false;
            }

            EnsureJusticeCustodyPedGenerationMap();
            _justiceCustodyPedGenerationByHandle[handle] = generation;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsJusticeCustodyPedOwnershipValid(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            EnsureJusticeCustodyPedGenerationMap();
            int expectedGeneration;
            if (!_justiceCustodyPedGenerationByHandle.TryGetValue(
                    ped.Handle,
                    out expectedGeneration))
            {
                return false;
            }

            return IsJusticeCustodyPedGenerationCompatible(
                expectedGeneration,
                GetJusticeEntityGeneration(ped));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeCustodyPedGenerationCompatible(
        int expectedGeneration,
        int currentGeneration)
    {
        return expectedGeneration > 0 && currentGeneration == expectedGeneration;
    }

    private void ForgetJusticeCustodyPedOwnership(int handle)
    {
        if (handle != 0 && _justiceCustodyPedGenerationByHandle != null)
        {
            _justiceCustodyPedGenerationByHandle.Remove(handle);
        }
    }

    private void CompactJusticeCustodyPedList(List<Ped> peds)
    {
        if (peds == null)
        {
            return;
        }

        for (int index = peds.Count - 1; index >= 0; index--)
        {
            Ped ped = peds[index];
            int handle = Entity.Exists(ped) ? ped.Handle : 0;
            bool ownedPed = IsJusticeCustodyPedOwnershipValid(ped);
            if (ownedPed && !ped.IsDead)
            {
                continue;
            }

            if (ownedPed)
            {
                DeleteEntitySafe(ped);
            }

            ForgetJusticeCustodyPedOwnership(handle);
            peds.RemoveAt(index);
        }
    }

    private bool JusticeIsCustodyOwnedPed(Ped ped)
    {
        return JusticeIsCustodyGuard(ped) || JusticeIsCustodyInmate(ped);
    }

    private bool JusticeIsCustodyGuard(Ped ped)
    {
        return JusticeCustodyListContainsPed(_justiceCustodyGuards, ped);
    }

    private bool JusticeIsCustodyInmate(Ped ped)
    {
        return JusticeCustodyListContainsPed(_justiceCustodyInmates, ped);
    }

    private bool JusticeCustodyListContainsPed(List<Ped> peds, Ped ped)
    {
        if (peds == null || !IsJusticeCustodyPedOwnershipValid(ped))
        {
            return false;
        }

        int generation = GetJusticeEntityGeneration(ped);
        for (int index = 0; index < peds.Count; index++)
        {
            Ped candidate = peds[index];
            if (IsJusticeCustodyPedOwnershipValid(candidate) &&
                candidate.Handle == ped.Handle &&
                IsJusticeCustodyPedGenerationCompatible(
                    generation,
                    GetJusticeEntityGeneration(candidate)))
            {
                return true;
            }
        }

        return false;
    }

    private void CleanupJusticeCustodyEntitiesAndGroups()
    {
        SetJusticeCustodyPoliceSuppression(false);
        _justiceNextPoliceSuppressionAt = 0;
        CleanupJusticeCustodySceneEntitiesAndGroups();
    }

    private void CleanupJusticeCustodySceneEntitiesAndGroups()
    {
        DeleteJusticeCustodyPedList(_justiceCustodyGuards);
        DeleteJusticeCustodyPedList(_justiceCustodyInmates);
        if (_justiceCustodyPedGenerationByHandle != null)
        {
            _justiceCustodyPedGenerationByHandle.Clear();
        }
        ResetJusticeCustodyAggressorBuffers();

        try
        {
            if (_justiceCustodyGuardGroupHash != 0)
            {
                World.RemoveRelationshipGroup(_justiceCustodyGuardGroupHash);
            }

            if (_justiceCustodyInmateGroupHash != 0)
            {
                World.RemoveRelationshipGroup(_justiceCustodyInmateGroupHash);
            }
        }
        catch
        {
        }

        _justiceCustodyGuardGroupHash = 0;
        _justiceCustodyInmateGroupHash = 0;
        _justiceNextCustodySceneRefreshAt = 0;
    }

    private void ResetJusticeCustodyAggressorBuffers()
    {
        EnsureJusticeCustodyAggressorBuffers();
        Array.Clear(_justiceCustodyAggressorHandles, 0, _justiceCustodyAggressorHandles.Length);
        Array.Clear(
            _justiceCustodyAggressorGenerations,
            0,
            _justiceCustodyAggressorGenerations.Length);
        Array.Clear(_justiceCustodyAggressorUntilMs, 0, _justiceCustodyAggressorUntilMs.Length);
    }

    private void DeleteJusticeCustodyPedList(List<Ped> peds)
    {
        if (peds == null)
        {
            return;
        }

        for (int index = peds.Count - 1; index >= 0; index--)
        {
            Ped ped = peds[index];
            int handle = Entity.Exists(ped) ? ped.Handle : 0;
            if (IsJusticeCustodyPedOwnershipValid(ped))
            {
                DeleteEntitySafe(ped);
            }
            ForgetJusticeCustodyPedOwnership(handle);
        }

        peds.Clear();
    }

    private string JusticeGetCustodyLocationDisplay()
    {
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        return layout == null ? "Détention" : layout.DisplayName;
    }

    private string JusticeGetCustodyActivityDisplay()
    {
        if (!JusticeIsCustodyActive)
        {
            return string.Empty;
        }

        JusticeCustodyActivityDefinition active = FindJusticeCustodyActivityById(_justiceActiveActivityId);
        if (active != null)
        {
            int remaining = Math.Max(
                0,
                (active.DurationSeconds * 1000 - _justiceActivityElapsedMs + 999) / 1000);
            return active.DisplayName + " · " + remaining.ToString(CultureInfo.InvariantCulture) + " s";
        }

        Ped player = Game.Player.Character;
        JusticeCustodyActivityDefinition nearby = Entity.Exists(player)
            ? FindNearestJusticeCustodyActivity(player.Position, JusticeCustodyActivityUseDistance)
            : null;
        return nearby == null ? "Activités signalées par les marqueurs cyan" : "E · " + nearby.DisplayName;
    }

    private void JusticeWriteCustodyXml(XmlWriter writer)
    {
        if (writer == null)
        {
            return;
        }

        writer.WriteStartElement("Custody");
        writer.WriteAttributeString("active", JusticeIsCustodyActive ? "true" : "false");
        writer.WriteAttributeString("site", _justiceCustodySite.ToString());
        writer.WriteAttributeString(
            "policeSuppressionApplied",
            _justicePoliceIgnoreApplied ? "true" : "false");
        writer.WriteAttributeString(
            "policeDispatchDisabled",
            _justicePoliceDispatchDisabled ? "true" : "false");
        writer.WriteAttributeString(
            "initialSentenceSeconds",
            Math.Max(0, _justiceCustodyInitialSentenceSeconds).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "activityReductionSeconds",
            Math.Max(0, _justiceActivityReductionGrantedSeconds).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("inventoryRemoved", _justiceInventoryRemoved ? "true" : "false");
        writer.WriteAttributeString("weaponControlsLocked", _justiceWeaponControlsLocked ? "true" : "false");
        writer.WriteAttributeString(
            "deferredInventoryRestore",
            _justiceDeferredInventoryRestore ? "true" : "false");
        writer.WriteAttributeString("waitingForRespawn", _justiceCustodyWaitingForRespawn ? "true" : "false");
        writer.WriteAttributeString("deathRebindPending", _justiceCustodyDeathRebindPending ? "true" : "false");
        writer.WriteAttributeString(
            "playerStateStored",
            _justiceCustodyPlayerStateStored ? "true" : "false");
        writer.WriteAttributeString(
            "storedInvincible",
            _justiceCustodyPlayerStateStored && _justiceCustodyStoredInvincible ? "true" : "false");
        writer.WriteAttributeString(
            "storedFrozen",
            _justiceCustodyPlayerStateStored && _justiceCustodyStoredFrozen ? "true" : "false");
        writer.WriteAttributeString(
            "storedCanRagdoll",
            !_justiceCustodyPlayerStateStored || _justiceCustodyStoredCanRagdoll ? "true" : "false");
        writer.WriteAttributeString(
            "playerModelHash",
            _justiceCustodyPlayerModelHash.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "playerSlot",
            _justiceCustodyPlayerSlot.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "releaseSelectedWeapon",
            _justiceReleaseSelectedWeaponHash.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "legalReleaseWantedClearAttempted",
            _justiceLegalReleaseWantedClearAttempted ? "true" : "false");
        writer.WriteAttributeString(
            "amnestyWantedClearAttempted",
            _justiceAmnestyWantedClearAttempted ? "true" : "false");

        WriteJusticeFineDebitIntentXml(writer);
        WriteJusticeVoluntaryFinePaymentIntentXml(writer);
        WriteJusticeDisciplineIntentXml(writer);
        WriteJusticeWeaponSnapshotXml(writer);
        WriteJusticeActivityCooldownsXml(writer);
        writer.WriteEndElement();
    }

    private static bool IsJusticeCustodyXmlSemanticallyValid(
        XmlElement root,
        JusticeCaseState caseState,
        JusticeRecordState recordState)
    {
        if (root == null || caseState == null || recordState == null)
        {
            return false;
        }

        XmlNodeList custodyNodes = root.SelectNodes("Custody");
        if (custodyNodes == null || custodyNodes.Count != 1)
        {
            return false;
        }
        XmlElement custody = custodyNodes[0] as XmlElement;
        if (custody == null)
        {
            return false;
        }

        bool savedActive;
        bool inventoryRemoved;
        bool weaponControlsLocked;
        bool deferredInventoryRestore;
        bool waitingForRespawn;
        bool deathRebindPending;
        bool playerStateStored;
        bool storedInvincible;
        bool storedFrozen;
        bool storedCanRagdoll;
        bool policeSuppressionApplied;
        bool policeDispatchDisabled;
        bool legalReleaseWantedClearAttempted;
        bool amnestyWantedClearAttempted;
        int initialSentence;
        int activityReduction;
        int playerModelHash;
        int playerSlot;
        int releaseSelectedWeaponHash;
        bool hasAnyPlayerStateAttribute =
            custody.HasAttribute("playerStateStored") ||
            custody.HasAttribute("storedInvincible") ||
            custody.HasAttribute("storedFrozen") ||
            custody.HasAttribute("storedCanRagdoll");
        bool hasAllPlayerStateAttributes =
            custody.HasAttribute("playerStateStored") &&
            custody.HasAttribute("storedInvincible") &&
            custody.HasAttribute("storedFrozen") &&
            custody.HasAttribute("storedCanRagdoll");
        string siteText = (custody.GetAttribute("site") ?? string.Empty).Trim();
        JusticeCustodySite site;
        if (!Enum.TryParse(siteText, true, out site) ||
            !Enum.IsDefined(typeof(JusticeCustodySite), site) ||
             !TryReadJusticeBoolStrict(custody, "active", false, out savedActive) ||
             !TryReadJusticeBoolStrict(
                 custody,
                 "policeSuppressionApplied",
                 false,
                 out policeSuppressionApplied) ||
             !TryReadJusticeBoolStrict(
                 custody,
                 "policeDispatchDisabled",
                 false,
                 out policeDispatchDisabled) ||
             !TryReadJusticeBoolStrict(
                 custody,
                 "legalReleaseWantedClearAttempted",
                 false,
                 out legalReleaseWantedClearAttempted) ||
             !TryReadJusticeBoolStrict(
                 custody,
                 "amnestyWantedClearAttempted",
                 false,
                 out amnestyWantedClearAttempted) ||
            !TryReadJusticeIntStrict(
                custody,
                "initialSentenceSeconds",
                0,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out initialSentence) ||
             !TryReadJusticeIntStrict(
                 custody,
                 "activityReductionSeconds",
                0,
                0,
                 5 * 60,
                 out activityReduction) ||
             !TryReadJusticeBoolStrict(custody, "inventoryRemoved", false, out inventoryRemoved) ||
            !TryReadJusticeBoolStrict(custody, "weaponControlsLocked", false, out weaponControlsLocked) ||
            !TryReadJusticeBoolStrict(
                custody,
                "deferredInventoryRestore",
                false,
                out deferredInventoryRestore) ||
            !TryReadJusticeBoolStrict(custody, "waitingForRespawn", false, out waitingForRespawn) ||
            !TryReadJusticeBoolStrict(custody, "deathRebindPending", false, out deathRebindPending) ||
            (hasAnyPlayerStateAttribute && !hasAllPlayerStateAttributes) ||
            !TryReadJusticeBoolStrict(custody, "playerStateStored", false, out playerStateStored) ||
            !TryReadJusticeBoolStrict(custody, "storedInvincible", false, out storedInvincible) ||
            !TryReadJusticeBoolStrict(custody, "storedFrozen", false, out storedFrozen) ||
            !TryReadJusticeBoolStrict(custody, "storedCanRagdoll", true, out storedCanRagdoll) ||
            !TryReadJusticeIntStrict(
                custody,
                "playerModelHash",
                0,
                int.MinValue,
                int.MaxValue,
                out playerModelHash) ||
            !TryReadJusticeIntStrict(custody, "playerSlot", -1, -1, 2, out playerSlot) ||
            !TryReadJusticeIntStrict(
                custody,
                "releaseSelectedWeapon",
                JusticeUnarmedHash,
                int.MinValue,
                int.MaxValue,
                out releaseSelectedWeaponHash))
        {
            return false;
        }

        XmlNodeList fineIntentNodes = custody.SelectNodes("FineDebitIntent");
        XmlNodeList voluntaryPaymentNodes = custody.SelectNodes("VoluntaryFinePaymentIntent");
        XmlNodeList disciplineIntentNodes = custody.SelectNodes("DisciplineIntent");
        XmlNodeList snapshotNodes = custody.SelectNodes("InventorySnapshot");
        XmlNodeList cooldownContainers = custody.SelectNodes("ActivityCooldowns");
        if (fineIntentNodes == null || fineIntentNodes.Count > 1 ||
            voluntaryPaymentNodes == null || voluntaryPaymentNodes.Count > 1 ||
            disciplineIntentNodes == null || disciplineIntentNodes.Count > 1 ||
            snapshotNodes == null || snapshotNodes.Count > 1 ||
            cooldownContainers == null || cooldownContainers.Count > 1)
        {
            return false;
        }

        JusticeFineDebitIntent fineIntent = fineIntentNodes.Count == 0
            ? null
            : ParseJusticeFineDebitIntentXmlPure(
                fineIntentNodes[0] as XmlElement,
                caseState,
                site);
        JusticeVoluntaryFinePaymentIntent voluntaryPayment =
            voluntaryPaymentNodes.Count == 0
                ? null
                : ParseJusticeVoluntaryFinePaymentIntentXmlPure(
                    voluntaryPaymentNodes[0] as XmlElement,
                    caseState);
        JusticeDisciplineIntent disciplineIntent = disciplineIntentNodes.Count == 0
            ? null
            : ParseJusticeDisciplineIntentXmlPure(
                disciplineIntentNodes[0] as XmlElement,
                caseState,
                site);
        JusticeWeaponSnapshot snapshot = snapshotNodes.Count == 0
            ? null
            : ReadJusticeWeaponSnapshotXml(custody);
        if ((fineIntentNodes.Count == 1 && fineIntent == null) ||
            (voluntaryPaymentNodes.Count == 1 && voluntaryPayment == null) ||
            (disciplineIntentNodes.Count == 1 && disciplineIntent == null) ||
            (snapshotNodes.Count == 1 && snapshot == null) ||
            (disciplineIntent != null &&
             !IsJusticeDisciplineIntentWalConsistent(caseState, recordState, disciplineIntent)) ||
            !AreJusticeActivityCooldownsSemanticallyValid(cooldownContainers, site) ||
            (inventoryRemoved && !ValidateJusticeWeaponSnapshot(snapshot)) ||
            (deferredInventoryRestore &&
             (!ValidateJusticeWeaponSnapshot(snapshot) || inventoryRemoved || weaponControlsLocked)) ||
            (voluntaryPayment != null &&
             (savedActive || !caseState.Enabled || IsJusticeCustodyPhase(caseState.Phase))) ||
            (deathRebindPending &&
             (!waitingForRespawn || !IsJusticeCustodyPhase(caseState.Phase))))
        {
            return false;
        }

        bool custodyPhase = IsJusticeCustodyPhase(caseState.Phase);
        bool capturedPhase = caseState.Phase == JusticePhase.Captured;
        bool placedCustodyPhase = caseState.Phase == JusticePhase.Transporting ||
                                  caseState.Phase == JusticePhase.Incarcerated ||
                                  caseState.Phase == JusticePhase.Escaping;
        if (!hasAnyPlayerStateAttribute)
        {
            // Migration v1 : l'ancien format ne conservait pas ce snapshot. Une
            // détention déjà placée reprend des valeurs vanilla sûres et durables.
            playerStateStored = placedCustodyPhase && playerModelHash != 0;
            storedInvincible = false;
            storedFrozen = false;
            storedCanRagdoll = true;
        }
        bool nonDeferredRecoveryState = fineIntent != null || disciplineIntent != null ||
            inventoryRemoved || weaponControlsLocked ||
            playerStateStored || (snapshot != null && !deferredInventoryRestore);
        if (savedActive != custodyPhase ||
            (custodyPhase && string.IsNullOrWhiteSpace(caseState.CustodyEpisodeId)) ||
            (!custodyPhase && nonDeferredRecoveryState) ||
            (placedCustodyPhase && (site == JusticeCustodySite.None || initialSentence <= 0)) ||
            (!playerStateStored &&
             (storedInvincible || storedFrozen || !storedCanRagdoll)))
        {
            return false;
        }

        if (!savedActive)
        {
            bool inactiveCanonical = site == JusticeCustodySite.None &&
                initialSentence == 0 && activityReduction == 0 &&
                fineIntent == null && disciplineIntent == null &&
                !inventoryRemoved && !weaponControlsLocked &&
                !playerStateStored &&
                !waitingForRespawn && !deathRebindPending &&
                releaseSelectedWeaponHash == JusticeUnarmedHash &&
                cooldownContainers.Count == 0;
            if (!deferredInventoryRestore)
            {
                inactiveCanonical &= snapshot == null && playerModelHash == 0 && playerSlot == -1;
            }
            if (!inactiveCanonical)
            {
                return false;
            }
        }

        if (capturedPhase &&
            (site != JusticeCustodySite.None || initialSentence != 0 || activityReduction != 0 ||
             disciplineIntent != null || snapshot != null || inventoryRemoved ||
             weaponControlsLocked || deferredInventoryRestore || playerStateStored ||
             releaseSelectedWeaponHash != JusticeUnarmedHash || cooldownContainers.Count != 0))
        {
            return false;
        }

        bool identityRequired = savedActive || fineIntent != null || disciplineIntent != null ||
            snapshot != null || inventoryRemoved || weaponControlsLocked || playerStateStored;
        if (identityRequired && playerModelHash == 0)
        {
            return false;
        }
        if (!caseState.Enabled &&
            (nonDeferredRecoveryState || (savedActive && !deferredInventoryRestore)))
        {
            return false;
        }
        return true;
    }

    private static bool IsJusticeCustodyPhase(JusticePhase phase)
    {
        return phase == JusticePhase.Captured ||
               phase == JusticePhase.Transporting ||
               phase == JusticePhase.Incarcerated ||
               phase == JusticePhase.Escaping;
    }

    private static bool TryReadJusticeCashWriteResult(
        XmlElement element,
        out JusticeCashWriteResult result)
    {
        result = JusticeCashWriteResult.Unknown;
        if (element == null || !element.HasAttribute("cashWriteResult"))
        {
            // Je migre les fichiers v1 antérieurs comme état ambigu : ils ne
            // peuvent ainsi jamais provoquer une nouvelle écriture de cash.
            return element != null;
        }

        return Enum.TryParse(element.GetAttribute("cashWriteResult"), true, out result) &&
               Enum.IsDefined(typeof(JusticeCashWriteResult), result);
    }

    private static bool IsJusticeFineSentenceCompatibleWithCashWriteResult(
        JusticeCashWriteResult result,
        int actualSentence,
        int sentenceIfDebited,
        int sentenceIfConverted)
    {
        switch (result)
        {
            case JusticeCashWriteResult.Succeeded:
                return actualSentence == sentenceIfDebited;
            case JusticeCashWriteResult.Rejected:
                return actualSentence == sentenceIfConverted;
            default:
                return actualSentence == sentenceIfDebited ||
                       actualSentence == sentenceIfConverted;
        }
    }

    private static JusticeDisciplineIntent ParseJusticeDisciplineIntentXmlPure(
        XmlElement element,
        JusticeCaseState caseState,
        JusticeCustodySite site)
    {
        if (element == null || caseState == null)
        {
            return null;
        }

        string incidentId = (element.GetAttribute("incidentId") ?? string.Empty).Trim();
        JusticeCrimeKind crimeKind;
        int penaltySeconds;
        string custodyEpisode = (caseState.CustodyEpisodeId ?? string.Empty).Trim();
        string expectedPrefix = custodyEpisode.Length == 0
            ? string.Empty
            : "discipline:" + custodyEpisode + ":";
        string suffix = expectedPrefix.Length > 0 && incidentId.StartsWith(
            expectedPrefix,
            StringComparison.Ordinal)
            ? incidentId.Substring(expectedPrefix.Length)
            : string.Empty;
        Guid disciplineId;
        int expectedPenaltySeconds = site == JusticeCustodySite.Bolingbroke
            ? 120
            : (site == JusticeCustodySite.MissionRow ? 60 : 0);
        if (!TryReadJusticeIntStrict(element, "penaltySeconds", -1, 1, 10 * 60, out penaltySeconds) ||
            incidentId.Length == 0 || incidentId.Length > 256 ||
            !Enum.TryParse(element.GetAttribute("crimeKind"), true, out crimeKind) ||
            !IsJusticeCustodyDisciplineCrime(crimeKind) ||
            penaltySeconds != expectedPenaltySeconds ||
            !Guid.TryParseExact(suffix, "N", out disciplineId) ||
            (caseState.Phase != JusticePhase.Incarcerated &&
             caseState.Phase != JusticePhase.Escaping))
        {
            return null;
        }
        return new JusticeDisciplineIntent
        {
            IncidentId = incidentId,
            CrimeKind = crimeKind,
            PenaltySeconds = penaltySeconds
        };
    }

    private static JusticeFineDebitIntent ParseJusticeFineDebitIntentXmlPure(
        XmlElement element,
        JusticeCaseState caseState,
        JusticeCustodySite site)
    {
        if (element == null || caseState == null)
        {
            return null;
        }

        string episodeId = (element.GetAttribute("episodeId") ?? string.Empty).Trim();
        int slot;
        long fineAmount;
        int debitAmount;
        int cashBefore;
        int cashAfter;
        int sentenceIfDebited;
        int sentenceIfConverted;
        bool stationPlanned;
        bool cashPlanPrepared = true;
        long preparedAtUtcTicks = 0L;
        bool debitAttempted = !element.HasAttribute("debitAttempted");
        long attemptedAtUtcTicks = 0L;
        JusticeCashWriteResult cashWriteResult = JusticeCashWriteResult.Unknown;
        if (!element.HasAttribute("episodeId") ||
            !TryReadJusticeIntStrict(element, "slot", -1, 0, 2, out slot) ||
            !TryReadJusticeLongStrict(element, "fineAmount", -1L, 1L, JusticePolicy.MaxActiveFine, out fineAmount) ||
            !TryReadJusticeIntStrict(element, "debitAmount", -1, 0, int.MaxValue, out debitAmount) ||
            !TryReadJusticeIntStrict(element, "cashBefore", -1, 0, int.MaxValue, out cashBefore) ||
            !TryReadJusticeIntStrict(element, "cashAfter", -1, 0, int.MaxValue, out cashAfter) ||
            !TryReadJusticeIntStrict(
                element,
                "sentenceIfDebited",
                -1,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out sentenceIfDebited) ||
            !TryReadJusticeIntStrict(
                element,
                "sentenceIfConverted",
                -1,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out sentenceIfConverted) ||
            !TryReadJusticeBoolStrict(element, "stationPlanned", false, out stationPlanned) ||
            !TryReadJusticeBoolStrict(
                element,
                "cashPlanPrepared",
                true,
                out cashPlanPrepared) ||
            !TryReadJusticeLongStrict(
                element,
                "preparedAtUtcTicks",
                0L,
                0L,
                DateTime.MaxValue.Ticks,
                out preparedAtUtcTicks) ||
            !TryReadJusticeBoolStrict(element, "debitAttempted", debitAttempted, out debitAttempted) ||
             !TryReadJusticeLongStrict(
                 element,
                 "attemptedAtUtcTicks",
                 0L,
                 0L,
                 DateTime.MaxValue.Ticks,
                 out attemptedAtUtcTicks) ||
             !TryReadJusticeCashWriteResult(element, out cashWriteResult))
        {
            return null;
        }

        int expectedDebit = (int)Math.Min(fineAmount, (long)cashBefore);
        string operationId = JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, episodeId);
        bool operationCommitted = caseState.CompletedOperationIds.Contains(operationId);
        bool matchesPrecommit = !operationCommitted && caseState.FineDue == fineAmount;
        if (matchesPrecommit)
        {
            bool expectedStationPlanned = site == JusticeCustodySite.None
                ? caseState.SentenceSeconds < JusticeCustodyPrisonThresholdSeconds
                : site == JusticeCustodySite.MissionRow;
            long unpaid = fineAmount - expectedDebit;
            matchesPrecommit = stationPlanned == expectedStationPlanned &&
                sentenceIfDebited == CalculateJusticeSentenceAfterFineConversion(
                    caseState.SentenceSeconds,
                    unpaid,
                    stationPlanned) &&
                sentenceIfConverted == CalculateJusticeSentenceAfterFineConversion(
                    caseState.SentenceSeconds,
                    fineAmount,
                    stationPlanned);
        }
        bool matchesCommitted = operationCommitted && caseState.FineDue == 0L &&
            IsJusticeFineSentenceCompatibleWithCashWriteResult(
                cashWriteResult,
                caseState.SentenceSeconds,
                sentenceIfDebited,
                sentenceIfConverted);
        bool valid = episodeId.Length > 0 && debitAmount == expectedDebit &&
            debitAmount <= cashBefore && cashAfter == cashBefore - debitAmount &&
            sentenceIfConverted >= sentenceIfDebited &&
            (!debitAttempted || cashPlanPrepared) &&
            (cashPlanPrepared ||
             (!debitAttempted && attemptedAtUtcTicks == 0L && preparedAtUtcTicks > 0L &&
              debitAmount == 0 && cashBefore == 0 && cashAfter == 0 &&
              sentenceIfDebited == sentenceIfConverted)) &&
            (debitAttempted || cashWriteResult == JusticeCashWriteResult.Unknown) &&
            (debitAttempted || attemptedAtUtcTicks == 0L) &&
            IsJusticeFineOperationEpisodeValid(caseState, episodeId) &&
            (matchesPrecommit || matchesCommitted);
        if (!valid)
        {
            return null;
        }
        return new JusticeFineDebitIntent
        {
            EpisodeId = episodeId,
            Slot = slot,
            FineAmount = fineAmount,
            CashPlanPrepared = cashPlanPrepared,
            PreparedAtUtcTicks = preparedAtUtcTicks,
            DebitAmount = debitAmount,
            CashBefore = cashBefore,
            CashAfter = cashAfter,
            SentenceIfDebited = sentenceIfDebited,
            SentenceIfConverted = sentenceIfConverted,
            StationPlanned = stationPlanned,
            DebitAttempted = debitAttempted,
            AttemptedAtUtcTicks = attemptedAtUtcTicks,
            CashWriteResult = cashWriteResult
        };
    }

    private static bool AreJusticeActivityCooldownsSemanticallyValid(
        XmlNodeList containers,
        JusticeCustodySite site)
    {
        if (containers == null || containers.Count == 0)
        {
            return true;
        }
        XmlElement container = containers[0] as XmlElement;
        XmlNodeList nodes = container == null ? null : container.SelectNodes("Cooldown");
        if (nodes == null || nodes.Count > 16 || site == JusticeCustodySite.None)
        {
            return false;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < nodes.Count; index++)
        {
            XmlElement element = nodes[index] as XmlElement;
            string id = element == null ? string.Empty : (element.GetAttribute("id") ?? string.Empty).Trim();
            int remaining;
            if (id.Length == 0 || !ids.Add(id) ||
                !IsKnownJusticeCustodyActivityId(site, id) ||
                !TryReadJusticeIntStrict(element, "remainingSeconds", 0, 1, 300, out remaining))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsKnownJusticeCustodyActivityId(JusticeCustodySite site, string id)
    {
        JusticeCustodyLayout layout = site == JusticeCustodySite.MissionRow
            ? JusticeMissionRowLayout
            : (site == JusticeCustodySite.Bolingbroke ? JusticeBolingbrokeLayout : null);
        if (layout == null || layout.Activities == null)
        {
            return false;
        }
        for (int index = 0; index < layout.Activities.Length; index++)
        {
            JusticeCustodyActivityDefinition activity = layout.Activities[index];
            if (activity != null && string.Equals(activity.Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void WriteJusticeFineDebitIntentXml(XmlWriter writer)
    {
        JusticeFineDebitIntent intent = _justiceFineDebitIntent;
        if (writer == null || intent == null)
        {
            return;
        }

        writer.WriteStartElement("FineDebitIntent");
        writer.WriteAttributeString("episodeId", intent.EpisodeId ?? string.Empty);
        writer.WriteAttributeString("slot", intent.Slot.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fineAmount", intent.FineAmount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "cashPlanPrepared",
            intent.CashPlanPrepared ? "true" : "false");
        writer.WriteAttributeString(
            "preparedAtUtcTicks",
            Math.Max(0L, intent.PreparedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("debitAmount", intent.DebitAmount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashBefore", intent.CashBefore.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashAfter", intent.CashAfter.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "sentenceIfDebited",
            intent.SentenceIfDebited.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "sentenceIfConverted",
            intent.SentenceIfConverted.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("stationPlanned", intent.StationPlanned ? "true" : "false");
        writer.WriteAttributeString("debitAttempted", intent.DebitAttempted ? "true" : "false");
        writer.WriteAttributeString("cashWriteResult", intent.CashWriteResult.ToString());
        writer.WriteAttributeString(
            "attemptedAtUtcTicks",
            Math.Max(0L, intent.AttemptedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private void WriteJusticeDisciplineIntentXml(XmlWriter writer)
    {
        JusticeDisciplineIntent intent = _justiceDisciplineIntent;
        if (writer == null || intent == null)
        {
            return;
        }

        writer.WriteStartElement("DisciplineIntent");
        writer.WriteAttributeString("incidentId", intent.IncidentId ?? string.Empty);
        writer.WriteAttributeString("crimeKind", intent.CrimeKind.ToString());
        writer.WriteAttributeString(
            "penaltySeconds",
            intent.PenaltySeconds.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private void WriteJusticeWeaponSnapshotXml(XmlWriter writer)
    {
        if (writer == null || _justiceWeaponSnapshot == null)
        {
            return;
        }

        writer.WriteStartElement("InventorySnapshot");
        writer.WriteAttributeString("validated", _justiceWeaponSnapshot.IsValidated ? "true" : "false");
        writer.WriteAttributeString(
            "selectedWeapon",
            _justiceWeaponSnapshot.SelectedWeaponHash.ToString(CultureInfo.InvariantCulture));

        for (int index = 0; index < _justiceWeaponSnapshot.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = _justiceWeaponSnapshot.Weapons[index];
            if (item == null)
            {
                continue;
            }

            writer.WriteStartElement("Weapon");
            writer.WriteAttributeString("hash", item.WeaponHash.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("ammo", item.Ammo.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("clip", item.AmmoInClip.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("tint", item.Tint.ToString(CultureInfo.InvariantCulture));
            for (int componentIndex = 0; componentIndex < item.ComponentHashes.Count; componentIndex++)
            {
                writer.WriteStartElement("Component");
                writer.WriteAttributeString(
                    "hash",
                    item.ComponentHashes[componentIndex].ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private void WriteJusticeActivityCooldownsXml(XmlWriter writer)
    {
        if (writer == null || _justiceActivityCooldownUntil.Count == 0)
        {
            return;
        }

        int now = GetJusticeRawGameTimeSafe();
        writer.WriteStartElement("ActivityCooldowns");
        foreach (KeyValuePair<string, int> pair in _justiceActivityCooldownUntil)
        {
            int remainingSeconds = Math.Max(
                0,
                (JusticeCustodyMillisecondsUntil(now, pair.Value) + 999) / 1000);
            if (remainingSeconds <= 0)
            {
                continue;
            }

            writer.WriteStartElement("Cooldown");
            writer.WriteAttributeString("id", pair.Key);
            writer.WriteAttributeString(
                "remainingSeconds",
                remainingSeconds.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private bool JusticeReadCustodyXml(XmlElement root)
    {
        if (root == null)
        {
            return false;
        }

        XmlElement custody = root.SelectSingleNode("Custody") as XmlElement;
        if (custody == null)
        {
            return false;
        }

        ResetJusticeCustodyPersistentFields(false);
        _justicePoliceIgnoreApplied = false;
        _justicePoliceDispatchDisabled = false;
        _justicePoliceSuppressionActive = false;
        _justicePoliceSuppressionRestorePending = false;
        _justicePoliceSuppressionFailureLogged = false;
        _justiceNextPoliceSuppressionAt = 0;
        _justiceNextPoliceSuppressionRestoreAt = 0;

        string siteText = (custody.GetAttribute("site") ?? string.Empty).Trim();
        JusticeCustodySite site = JusticeCustodySite.None;
        if (siteText.Length > 0 &&
            (!Enum.TryParse(siteText, true, out site) ||
             !Enum.IsDefined(typeof(JusticeCustodySite), site)))
        {
            LogWarning("Justice.Chargement", "Etat de détention rejeté : lieu inconnu.");
            return false;
        }
        if (siteText.Length > 0 && site != JusticeCustodySite.None)
        {
            _justiceCustodySite = site;
        }

        int initialSentence;
        int activityReduction;
        bool inventoryRemoved;
        bool weaponControlsLocked;
        bool deferredInventoryRestore;
        bool waitingForRespawn;
        bool deathRebindPending;
        bool playerStateStored;
        bool storedInvincible;
        bool storedFrozen;
        bool storedCanRagdoll;
        bool policeSuppressionApplied;
        bool policeDispatchDisabled;
        bool legalReleaseWantedClearAttempted;
        bool amnestyWantedClearAttempted;
        int playerModelHash;
        int playerSlot;
        int releaseSelectedWeaponHash;
        bool hasAnyPlayerStateAttribute =
            custody.HasAttribute("playerStateStored") ||
            custody.HasAttribute("storedInvincible") ||
            custody.HasAttribute("storedFrozen") ||
            custody.HasAttribute("storedCanRagdoll");
        bool hasAllPlayerStateAttributes =
            custody.HasAttribute("playerStateStored") &&
            custody.HasAttribute("storedInvincible") &&
            custody.HasAttribute("storedFrozen") &&
            custody.HasAttribute("storedCanRagdoll");
        if (!TryReadJusticeIntStrict(
                custody,
                "initialSentenceSeconds",
                0,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out initialSentence) ||
            !TryReadJusticeIntStrict(
                custody,
                "activityReductionSeconds",
                0,
                0,
                5 * 60,
                out activityReduction) ||
            !TryReadJusticeBoolStrict(
                custody,
                "policeSuppressionApplied",
                false,
                out policeSuppressionApplied) ||
            !TryReadJusticeBoolStrict(
                custody,
                "policeDispatchDisabled",
                false,
                out policeDispatchDisabled) ||
            !TryReadJusticeBoolStrict(
                custody,
                "legalReleaseWantedClearAttempted",
                false,
                out legalReleaseWantedClearAttempted) ||
            !TryReadJusticeBoolStrict(
                custody,
                "amnestyWantedClearAttempted",
                false,
                out amnestyWantedClearAttempted) ||
            !TryReadJusticeBoolStrict(custody, "inventoryRemoved", false, out inventoryRemoved) ||
            !TryReadJusticeBoolStrict(custody, "weaponControlsLocked", false, out weaponControlsLocked) ||
            !TryReadJusticeBoolStrict(
                custody,
                "deferredInventoryRestore",
                false,
                out deferredInventoryRestore) ||
            !TryReadJusticeBoolStrict(custody, "waitingForRespawn", false, out waitingForRespawn) ||
            !TryReadJusticeBoolStrict(custody, "deathRebindPending", false, out deathRebindPending) ||
            (hasAnyPlayerStateAttribute && !hasAllPlayerStateAttributes) ||
            !TryReadJusticeBoolStrict(custody, "playerStateStored", false, out playerStateStored) ||
            !TryReadJusticeBoolStrict(custody, "storedInvincible", false, out storedInvincible) ||
            !TryReadJusticeBoolStrict(custody, "storedFrozen", false, out storedFrozen) ||
            !TryReadJusticeBoolStrict(custody, "storedCanRagdoll", true, out storedCanRagdoll) ||
            !TryReadJusticeIntStrict(
                custody,
                "playerModelHash",
                0,
                int.MinValue,
                int.MaxValue,
                out playerModelHash) ||
            !TryReadJusticeIntStrict(custody, "playerSlot", -1, -1, 2, out playerSlot) ||
            !TryReadJusticeIntStrict(
                custody,
                "releaseSelectedWeapon",
                JusticeUnarmedHash,
                int.MinValue,
                int.MaxValue,
                out releaseSelectedWeaponHash))
        {
            LogWarning("Justice.Chargement", "Etat de détention rejeté : attribut invalide.");
            return false;
        }
        _justiceCustodyInitialSentenceSeconds = initialSentence;
        _justiceActivityReductionGrantedSeconds = activityReduction;
        _justiceInventoryRemoved = inventoryRemoved;
        _justiceWeaponControlsLocked = weaponControlsLocked;
        _justiceDeferredInventoryRestore = deferredInventoryRestore;
        _justiceCustodyWaitingForRespawn = waitingForRespawn;
        _justiceCustodyDeathRebindPending = deathRebindPending;
        _justiceLegalReleaseWantedClearAttempted =
            legalReleaseWantedClearAttempted;
        _justiceAmnestyWantedClearAttempted =
            amnestyWantedClearAttempted;
        _justiceCustodyPlayerModelHash = playerModelHash;
        _justiceCustodyPlayerSlot = playerSlot;
        _justiceReleaseSelectedWeaponHash = releaseSelectedWeaponHash;
        XmlElement fineIntentElement = custody.SelectSingleNode("FineDebitIntent") as XmlElement;
        XmlElement voluntaryPaymentElement =
            custody.SelectSingleNode("VoluntaryFinePaymentIntent") as XmlElement;
        XmlElement disciplineIntentElement = custody.SelectSingleNode("DisciplineIntent") as XmlElement;
        XmlElement snapshotElement = custody.SelectSingleNode("InventorySnapshot") as XmlElement;
        _justiceFineDebitIntent = ReadJusticeFineDebitIntentXml(custody);
        _justiceVoluntaryFinePaymentIntent = voluntaryPaymentElement == null
            ? null
            : ParseJusticeVoluntaryFinePaymentIntentXmlPure(
                voluntaryPaymentElement,
                _justiceCaseState);
        _justiceDisciplineIntent = ReadJusticeDisciplineIntentXml(custody);
        _justiceWeaponSnapshot = ReadJusticeWeaponSnapshotXml(custody);
        ReadJusticeActivityCooldownsXml(custody);

        if ((fineIntentElement != null && _justiceFineDebitIntent == null) ||
            (voluntaryPaymentElement != null &&
             _justiceVoluntaryFinePaymentIntent == null) ||
            (disciplineIntentElement != null && _justiceDisciplineIntent == null) ||
            (_justiceDisciplineIntent != null &&
             !IsJusticeDisciplineIntentWalConsistent(_justiceDisciplineIntent)) ||
            (snapshotElement != null && _justiceWeaponSnapshot == null) ||
            (_justiceInventoryRemoved && !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot)) ||
            (_justiceDeferredInventoryRestore &&
             (!ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) ||
              _justiceInventoryRemoved || _justiceWeaponControlsLocked)) ||
            (_justiceVoluntaryFinePaymentIntent != null &&
             (_justiceCaseState == null || !_justiceCaseState.Enabled ||
              IsJusticeCustodyPhase(_justiceCaseState.Phase))) ||
            (_justiceCustodyDeathRebindPending &&
             (!_justiceCustodyWaitingForRespawn ||
               _justiceCaseState == null ||
               (_justiceCaseState.Phase != JusticePhase.Captured &&
                _justiceCaseState.Phase != JusticePhase.Transporting &&
                _justiceCaseState.Phase != JusticePhase.Incarcerated &&
               _justiceCaseState.Phase != JusticePhase.Escaping))))
        {
            // Je rejette tout le primaire sémantiquement incohérent afin que le
            // chargeur tente le .bak contenant le précommit durable.
            LogWarning(
                "Justice.Chargement",
                "Etat de détention rejeté : intention financière ou snapshot invalide.");
            ResetJusticeCustodyPersistentFields(false);
            return false;
        }

        bool savedActive;
        if (!TryReadJusticeBoolStrict(custody, "active", false, out savedActive))
        {
            ResetJusticeCustodyPersistentFields(false);
            return false;
        }
        _justicePoliceIgnoreApplied = policeSuppressionApplied;
        _justicePoliceDispatchDisabled = policeDispatchDisabled;
        _justicePoliceSuppressionActive =
            policeSuppressionApplied || policeDispatchDisabled;
        _justicePoliceSuppressionRestorePending =
            _justicePoliceSuppressionActive &&
            (!savedActive ||
             (_justiceCaseState != null && _justiceCaseState.Phase == JusticePhase.Captured));
        _justicePoliceSuppressionFailureLogged = false;
        _justiceNextPoliceSuppressionAt = 0;
        _justiceNextPoliceSuppressionRestoreAt = 0;
        bool custodyPhase = _justiceCaseState != null &&
            (_justiceCaseState.Phase == JusticePhase.Captured ||
             _justiceCaseState.Phase == JusticePhase.Transporting ||
             _justiceCaseState.Phase == JusticePhase.Incarcerated ||
             _justiceCaseState.Phase == JusticePhase.Escaping);
        bool capturedPhase = _justiceCaseState != null &&
            _justiceCaseState.Phase == JusticePhase.Captured;
        bool placedCustodyPhase = _justiceCaseState != null &&
            (_justiceCaseState.Phase == JusticePhase.Transporting ||
             _justiceCaseState.Phase == JusticePhase.Incarcerated ||
             _justiceCaseState.Phase == JusticePhase.Escaping);
        if (!hasAnyPlayerStateAttribute)
        {
            playerStateStored = placedCustodyPhase && playerModelHash != 0;
            storedInvincible = false;
            storedFrozen = false;
            storedCanRagdoll = true;
        }
        _justiceCustodyPlayerStateStored = playerStateStored;
        _justiceCustodyStoredInvincible = storedInvincible;
        _justiceCustodyStoredFrozen = storedFrozen;
        _justiceCustodyStoredCanRagdoll = storedCanRagdoll;
        bool nonDeferredRecoveryState = fineIntentElement != null ||
            disciplineIntentElement != null || _justiceInventoryRemoved ||
            _justiceWeaponControlsLocked || _justiceCustodyPlayerStateStored ||
            (snapshotElement != null && !_justiceDeferredInventoryRestore);
        bool inactiveStateIsCanonical = true;
        if (!savedActive)
        {
            inactiveStateIsCanonical = _justiceCustodySite == JusticeCustodySite.None &&
                _justiceCustodyInitialSentenceSeconds == 0 &&
                _justiceActivityReductionGrantedSeconds == 0 &&
                _justiceFineDebitIntent == null && _justiceDisciplineIntent == null &&
                !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
                !_justiceCustodyPlayerStateStored &&
                !_justiceCustodyWaitingForRespawn && !_justiceCustodyDeathRebindPending &&
                _justiceReleaseSelectedWeaponHash == JusticeUnarmedHash &&
                _justiceLoadedActivityCooldownSeconds.Count == 0;
            if (!_justiceDeferredInventoryRestore)
            {
                inactiveStateIsCanonical &= _justiceWeaponSnapshot == null &&
                    _justiceCustodyPlayerModelHash == 0 && _justiceCustodyPlayerSlot == -1;
            }
        }
        bool capturedStateIsCanonical = !capturedPhase ||
            (_justiceCustodySite == JusticeCustodySite.None &&
             _justiceCustodyInitialSentenceSeconds == 0 &&
             _justiceActivityReductionGrantedSeconds == 0 &&
             _justiceDisciplineIntent == null &&
             _justiceVoluntaryFinePaymentIntent == null &&
             _justiceWeaponSnapshot == null &&
             !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
             !_justiceDeferredInventoryRestore && !_justiceCustodyPlayerStateStored &&
             _justiceReleaseSelectedWeaponHash == JusticeUnarmedHash &&
             _justiceLoadedActivityCooldownSeconds.Count == 0);
        if (savedActive != custodyPhase ||
            (custodyPhase && string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId)) ||
            (!custodyPhase && nonDeferredRecoveryState) ||
             !inactiveStateIsCanonical ||
             !capturedStateIsCanonical ||
             (!_justiceCustodyPlayerStateStored &&
              (_justiceCustodyStoredInvincible || _justiceCustodyStoredFrozen ||
               !_justiceCustodyStoredCanRagdoll)) ||
            (placedCustodyPhase &&
             (_justiceCustodySite == JusticeCustodySite.None ||
              _justiceCustodyInitialSentenceSeconds <= 0)))
        {
            LogWarning("Justice.Chargement", "Etat de détention rejeté : phase incohérente.");
            ResetJusticeCustodyPersistentFields(false);
            return false;
        }
        bool custodyIdentityRequired = savedActive || fineIntentElement != null ||
            disciplineIntentElement != null ||
            snapshotElement != null || _justiceInventoryRemoved || _justiceWeaponControlsLocked ||
            _justiceCustodyPlayerStateStored ||
            (_justiceCaseState != null &&
             (_justiceCaseState.Phase == JusticePhase.Captured ||
              _justiceCaseState.Phase == JusticePhase.Transporting ||
              _justiceCaseState.Phase == JusticePhase.Incarcerated ||
              _justiceCaseState.Phase == JusticePhase.Escaping));
        if (custodyIdentityRequired && _justiceCustodyPlayerModelHash == 0)
        {
            LogWarning(
                "Justice.Chargement",
                "Etat de détention rejeté : identité persistée du protagoniste absente.");
            ResetJusticeCustodyPersistentFields(false);
            return false;
        }

        if (!_justiceInventoryRemoved && !_justiceDeferredInventoryRestore &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            // Un snapshot présent sans commit RemoveAll correspond à une reprise
            // de préconfiscation : je réactive toujours son verrou idempotent.
            _justiceWeaponControlsLocked = true;
            _justiceNextInventoryPersistenceRetryAt = 0;
        }

        if (savedActive && _justiceCaseState != null &&
            _justiceCaseState.Phase != JusticePhase.Captured &&
            _justiceCaseState.SentenceSeconds > 0)
        {
            _justiceCustodyRuntimeActive = true;
            _justiceCustodyResumePending = true;
        }

        return true;
    }

    private JusticeDisciplineIntent ReadJusticeDisciplineIntentXml(XmlElement custody)
    {
        XmlElement element = custody == null
            ? null
            : custody.SelectSingleNode("DisciplineIntent") as XmlElement;
        if (element == null)
        {
            return null;
        }

        string incidentId = (element.GetAttribute("incidentId") ?? string.Empty).Trim();
        JusticeCrimeKind crimeKind;
        int penaltySeconds;
        if (!TryReadJusticeIntStrict(
            element,
            "penaltySeconds",
            -1,
            1,
            10 * 60,
            out penaltySeconds))
        {
            return null;
        }
        string custodyEpisode = _justiceCaseState == null
            ? string.Empty
            : (_justiceCaseState.CustodyEpisodeId ?? string.Empty).Trim();
        string expectedPrefix = custodyEpisode.Length == 0
            ? string.Empty
            : "discipline:" + custodyEpisode + ":";
        string suffix = expectedPrefix.Length > 0 && incidentId.StartsWith(
            expectedPrefix,
            StringComparison.Ordinal)
            ? incidentId.Substring(expectedPrefix.Length)
            : string.Empty;
        Guid disciplineId;
        bool belongsToCurrentCustody = expectedPrefix.Length > 0 &&
            Guid.TryParseExact(suffix, "N", out disciplineId) &&
            (_justiceCaseState.Phase == JusticePhase.Incarcerated ||
             _justiceCaseState.Phase == JusticePhase.Escaping);
        int expectedPenaltySeconds = _justiceCustodySite == JusticeCustodySite.Bolingbroke
            ? 120
            : (_justiceCustodySite == JusticeCustodySite.MissionRow ? 60 : 0);
        if (incidentId.Length == 0 || incidentId.Length > 256 ||
            !Enum.TryParse(element.GetAttribute("crimeKind"), true, out crimeKind) ||
            !IsJusticeCustodyDisciplineCrime(crimeKind) ||
            penaltySeconds != expectedPenaltySeconds ||
            !belongsToCurrentCustody)
        {
            return null;
        }

        return new JusticeDisciplineIntent
        {
            IncidentId = incidentId,
            CrimeKind = crimeKind,
            PenaltySeconds = penaltySeconds
        };
    }

    private static bool IsJusticeCustodyDisciplineCrime(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.ReportedViolentAct ||
               kind == JusticeCrimeKind.RecklessDischarge ||
               kind == JusticeCrimeKind.SimpleAssault ||
               kind == JusticeCrimeKind.AssaultOfficer ||
               kind == JusticeCrimeKind.MurderCivilian ||
               kind == JusticeCrimeKind.MurderOfficer;
    }

    private bool IsJusticeDisciplineIntentWalConsistent(JusticeDisciplineIntent intent)
    {
        return IsJusticeDisciplineIntentWalConsistent(
            _justiceCaseState,
            _justiceRecordState,
            intent);
    }

    private static bool IsJusticeDisciplineIntentWalConsistent(
        JusticeCaseState caseState,
        JusticeRecordState recordState,
        JusticeDisciplineIntent intent)
    {
        if (intent == null || caseState == null || recordState == null)
        {
            return false;
        }

        string incidentId = (intent.IncidentId ?? string.Empty).Trim();
        string custodyEpisode = (caseState.CustodyEpisodeId ?? string.Empty).Trim();
        if (incidentId.Length == 0 || custodyEpisode.Length == 0)
        {
            return false;
        }

        string disciplineEpisode = custodyEpisode + ":discipline:" + incidentId;
        string convictionId = "conviction:" + disciplineEpisode;
        int processedCount = 0;
        int matchingChargeCount = 0;
        int appliedConvictionCount = 0;
        int visibleConvictionCount = 0;
        JusticeCharge matchingCharge = null;
        JusticeConviction matchingConviction = null;

        for (int index = 0; index < caseState.ProcessedIncidentIds.Count; index++)
        {
            if (string.Equals(
                caseState.ProcessedIncidentIds[index],
                incidentId,
                StringComparison.Ordinal))
            {
                processedCount++;
            }
        }

        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge == null || !string.Equals(
                charge.IncidentId,
                incidentId,
                StringComparison.Ordinal))
            {
                continue;
            }

            if (charge.IsAggregate ||
                !string.Equals(charge.EpisodeId, disciplineEpisode, StringComparison.Ordinal) ||
                charge.Kind != intent.CrimeKind ||
                (charge.Circumstances & JusticeCircumstances.InCustody) == 0)
            {
                return false;
            }
            matchingCharge = charge;
            matchingChargeCount++;
        }

        for (int index = 0; index < recordState.AppliedConvictionIds.Count; index++)
        {
            if (string.Equals(
                recordState.AppliedConvictionIds[index],
                convictionId,
                StringComparison.Ordinal))
            {
                appliedConvictionCount++;
            }
        }

        for (int index = 0; index < recordState.Convictions.Count; index++)
        {
            JusticeConviction conviction = recordState.Convictions[index];
            if (conviction != null && string.Equals(
                conviction.ConvictionId,
                convictionId,
                StringComparison.Ordinal))
            {
                visibleConvictionCount++;
                matchingConviction = conviction;
                if (index != recordState.Convictions.Count - 1)
                {
                    return false;
                }
            }
        }

        bool naturalPrecommit = processedCount == 0 && matchingChargeCount == 0 &&
            appliedConvictionCount == 0 && visibleConvictionCount == 0;
        bool committedBeforeIntentCleanup = processedCount == 1 && matchingChargeCount == 1 &&
            appliedConvictionCount == 1 && visibleConvictionCount == 1;
        if (committedBeforeIntentCleanup)
        {
            JusticeConvictionChargeSummary summary = matchingConviction != null &&
                matchingConviction.Charges.Count == 1
                ? matchingConviction.Charges[0]
                : null;
            committedBeforeIntentCleanup = matchingCharge != null && summary != null &&
                summary.Kind == matchingCharge.Kind &&
                string.Equals(
                    summary.DisplayName,
                    matchingCharge.DisplayName,
                    StringComparison.Ordinal) &&
                summary.Points == matchingCharge.Points &&
                summary.Fine == matchingCharge.Fine &&
                summary.SentenceSeconds == matchingCharge.SentenceSeconds &&
                (!summary.CircumstancesWerePersisted ||
                 summary.Circumstances == matchingCharge.Circumstances) &&
                summary.IsAggregate == matchingCharge.IsAggregate &&
                summary.AggregatedChargeCount == matchingCharge.AggregatedChargeCount &&
                matchingConviction.Score == matchingCharge.Points &&
                matchingConviction.Fine == matchingCharge.Fine &&
                matchingConviction.SentenceSeconds == matchingCharge.SentenceSeconds &&
                caseState.FineDue >= matchingCharge.Fine &&
                caseState.SentenceSeconds >= matchingCharge.SentenceSeconds;
        }
        return naturalPrecommit || committedBeforeIntentCleanup;
    }

    private JusticeFineDebitIntent ReadJusticeFineDebitIntentXml(XmlElement custody)
    {
        XmlElement element = custody == null
            ? null
            : custody.SelectSingleNode("FineDebitIntent") as XmlElement;
        if (element == null)
        {
            return null;
        }

        string episodeId = (element.GetAttribute("episodeId") ?? string.Empty).Trim();
        int slot = -1;
        long fineAmount = -1L;
        int debitAmount = -1;
        int cashBefore = -1;
        int cashAfter = -1;
        int sentenceIfDebited = -1;
        int sentenceIfConverted = -1;
        bool stationPlanned = false;
        bool cashPlanPrepared = true;
        long preparedAtUtcTicks = 0L;
        // Les premiers fichiers v1 n'avaient pas cette étape WAL. Je considère
        // leur état comme déjà tenté : une mise à niveau ne doit jamais redébiter
        // un cash revenu par ailleurs à CashBefore.
        bool debitAttempted = !element.HasAttribute("debitAttempted");
        long attemptedAtUtcTicks = 0L;
        JusticeCashWriteResult cashWriteResult = JusticeCashWriteResult.Unknown;
        bool attributesValid = element.HasAttribute("episodeId") &&
            TryReadJusticeIntStrict(element, "slot", -1, 0, 2, out slot) &&
            TryReadJusticeLongStrict(
                element,
                "fineAmount",
                -1L,
                1L,
                JusticePolicy.MaxActiveFine,
                out fineAmount) &&
            TryReadJusticeIntStrict(
                element,
                "debitAmount",
                -1,
                0,
                int.MaxValue,
                out debitAmount) &&
            TryReadJusticeIntStrict(
                element,
                "cashBefore",
                -1,
                0,
                int.MaxValue,
                out cashBefore) &&
            TryReadJusticeIntStrict(
                element,
                "cashAfter",
                -1,
                0,
                int.MaxValue,
                out cashAfter) &&
            TryReadJusticeIntStrict(
                element,
                "sentenceIfDebited",
                -1,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out sentenceIfDebited) &&
            TryReadJusticeIntStrict(
                element,
                "sentenceIfConverted",
                -1,
                0,
                JusticeCustodyMaximumSentenceSeconds,
                out sentenceIfConverted) &&
            TryReadJusticeBoolStrict(
                element,
                "stationPlanned",
                false,
                out stationPlanned) &&
            TryReadJusticeBoolStrict(
                element,
                "cashPlanPrepared",
                true,
                out cashPlanPrepared) &&
            TryReadJusticeLongStrict(
                element,
                "preparedAtUtcTicks",
                0L,
                0L,
                DateTime.MaxValue.Ticks,
                out preparedAtUtcTicks) &&
            TryReadJusticeBoolStrict(
                element,
                "debitAttempted",
                debitAttempted,
                out debitAttempted) &&
             TryReadJusticeLongStrict(
                 element,
                 "attemptedAtUtcTicks",
                 0L,
                 0L,
                 DateTime.MaxValue.Ticks,
                 out attemptedAtUtcTicks) &&
             TryReadJusticeCashWriteResult(element, out cashWriteResult);

        bool valid = attributesValid && episodeId.Length > 0 && slot >= 0 && slot <= 2 &&
                     fineAmount > 0L && fineAmount <= JusticePolicy.MaxActiveFine &&
                     debitAmount >= 0 && debitAmount <= cashBefore &&
                     cashBefore >= 0 && cashAfter >= 0 && cashAfter == cashBefore - debitAmount &&
                      sentenceIfDebited >= 0 && sentenceIfDebited <= JusticeCustodyMaximumSentenceSeconds &&
                      sentenceIfConverted >= sentenceIfDebited &&
                      sentenceIfConverted <= JusticeCustodyMaximumSentenceSeconds &&
                      (!debitAttempted || cashPlanPrepared) &&
                       (cashPlanPrepared ||
                        (!debitAttempted && attemptedAtUtcTicks == 0L && preparedAtUtcTicks > 0L &&
                         debitAmount == 0 && cashBefore == 0 && cashAfter == 0 &&
                         sentenceIfDebited == sentenceIfConverted)) &&
                       (debitAttempted || cashWriteResult == JusticeCashWriteResult.Unknown) &&
                       (debitAttempted || attemptedAtUtcTicks == 0L);
        int expectedDebit = (int)Math.Min(fineAmount, (long)Math.Max(0, cashBefore));
        valid &= debitAmount == expectedDebit;
        string custodyEpisode = _justiceCaseState == null
            ? string.Empty
            : (_justiceCaseState.CustodyEpisodeId ?? string.Empty).Trim();
        bool belongsToCurrentCustody = custodyEpisode.Length > 0 &&
            (string.Equals(episodeId, custodyEpisode, StringComparison.Ordinal) ||
             episodeId.StartsWith(custodyEpisode + ":fine:", StringComparison.Ordinal));
        bool operationEpisodeIsCanonical = _justiceCaseState != null &&
            IsJusticeFineOperationEpisodeValid(_justiceCaseState, episodeId);
        string operationId = JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyFine,
            episodeId);
        bool operationCommitted = _justiceCaseState != null &&
            _justiceCaseState.CompletedOperationIds.Contains(operationId);
        bool matchesPrecommit = !operationCommitted && _justiceCaseState != null &&
            _justiceCaseState.FineDue == fineAmount;
        if (matchesPrecommit)
        {
            bool expectedStationPlanned = _justiceCustodySite == JusticeCustodySite.None
                ? _justiceCaseState.SentenceSeconds < JusticeCustodyPrisonThresholdSeconds
                : _justiceCustodySite == JusticeCustodySite.MissionRow;
            long unpaid = fineAmount - expectedDebit;
            valid &= stationPlanned == expectedStationPlanned &&
                     sentenceIfDebited == CalculateJusticeSentenceAfterFineConversion(
                         _justiceCaseState.SentenceSeconds,
                         unpaid,
                         stationPlanned) &&
                     sentenceIfConverted == CalculateJusticeSentenceAfterFineConversion(
                         _justiceCaseState.SentenceSeconds,
                         fineAmount,
                         stationPlanned);
        }
        bool matchesCommittedState = operationCommitted && _justiceCaseState != null &&
            _justiceCaseState.FineDue == 0L &&
            IsJusticeFineSentenceCompatibleWithCashWriteResult(
                cashWriteResult,
                _justiceCaseState.SentenceSeconds,
                sentenceIfDebited,
                sentenceIfConverted);
        valid &= belongsToCurrentCustody && operationEpisodeIsCanonical &&
                 (matchesPrecommit || matchesCommittedState);
        if (!valid)
        {
            LogWarning(
                "Justice.Amende",
                "Intention de débit corrompue ou étrangère au dossier ignorée; aucun cash ne sera modifié depuis ces données.");
            return null;
        }

        return new JusticeFineDebitIntent
        {
            EpisodeId = episodeId,
            Slot = slot,
            FineAmount = fineAmount,
            CashPlanPrepared = cashPlanPrepared,
            PreparedAtUtcTicks = preparedAtUtcTicks,
            DebitAmount = debitAmount,
            CashBefore = cashBefore,
            CashAfter = cashAfter,
            SentenceIfDebited = sentenceIfDebited,
            SentenceIfConverted = sentenceIfConverted,
            StationPlanned = stationPlanned,
            DebitAttempted = debitAttempted,
            AttemptedAtUtcTicks = attemptedAtUtcTicks,
            CashWriteResult = cashWriteResult
        };
    }

    private static JusticeWeaponSnapshot ReadJusticeWeaponSnapshotXml(XmlElement custody)
    {
        XmlElement snapshotElement = custody == null
            ? null
            : custody.SelectSingleNode("InventorySnapshot") as XmlElement;
        if (snapshotElement == null)
        {
            return null;
        }

        bool isValidated;
        int selectedWeaponHash;
        if (!snapshotElement.HasAttribute("validated") ||
            !snapshotElement.HasAttribute("selectedWeapon") ||
            !TryReadJusticeBoolStrict(
                snapshotElement,
                "validated",
                false,
                out isValidated) ||
            !TryReadJusticeIntStrict(
                snapshotElement,
                "selectedWeapon",
                JusticeUnarmedHash,
                int.MinValue,
                int.MaxValue,
                out selectedWeaponHash))
        {
            return null;
        }

        JusticeWeaponSnapshot snapshot = new JusticeWeaponSnapshot
        {
            IsValidated = isValidated,
            SelectedWeaponHash = selectedWeaponHash
        };

        XmlNodeList weaponNodes = snapshotElement.SelectNodes("Weapon");
        if (weaponNodes == null || weaponNodes.Count > JusticeCustodyMaxWeapons)
        {
            return null;
        }

        for (int index = 0; index < weaponNodes.Count; index++)
        {
            XmlElement weaponElement = weaponNodes[index] as XmlElement;
            if (weaponElement == null)
            {
                return null;
            }

            int weaponHash;
            int ammo;
            int ammoInClip;
            int tint;
            if (!weaponElement.HasAttribute("hash") ||
                !weaponElement.HasAttribute("ammo") ||
                !weaponElement.HasAttribute("clip") ||
                !weaponElement.HasAttribute("tint") ||
                !TryReadJusticeIntStrict(
                    weaponElement,
                    "hash",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out weaponHash) ||
                !TryReadJusticeIntStrict(
                    weaponElement,
                    "ammo",
                    0,
                    0,
                    1000000,
                    out ammo) ||
                !TryReadJusticeIntStrict(
                    weaponElement,
                    "clip",
                    0,
                    0,
                    1000000,
                    out ammoInClip) ||
                !TryReadJusticeIntStrict(
                    weaponElement,
                    "tint",
                    0,
                    0,
                    64,
                    out tint) ||
                ammoInClip > ammo)
            {
                return null;
            }

            JusticeWeaponSnapshotItem item = new JusticeWeaponSnapshotItem
            {
                WeaponHash = weaponHash,
                Ammo = ammo,
                AmmoInClip = ammoInClip,
                Tint = tint
            };

            XmlNodeList componentNodes = weaponElement.SelectNodes("Component");
            if (componentNodes == null || componentNodes.Count > JusticeCustodyMaxComponentsPerWeapon)
            {
                return null;
            }

            for (int componentIndex = 0; componentIndex < componentNodes.Count; componentIndex++)
            {
                XmlElement componentElement = componentNodes[componentIndex] as XmlElement;
                if (componentElement == null)
                {
                    return null;
                }

                int componentHash;
                if (!componentElement.HasAttribute("hash") ||
                    !TryReadJusticeIntStrict(
                        componentElement,
                        "hash",
                        0,
                        int.MinValue,
                        int.MaxValue,
                        out componentHash))
                {
                    return null;
                }

                item.ComponentHashes.Add(componentHash);
            }

            snapshot.Weapons.Add(item);
        }

        return ValidateJusticeWeaponSnapshot(snapshot) ? snapshot : null;
    }

    private void ReadJusticeActivityCooldownsXml(XmlElement custody)
    {
        XmlNodeList cooldownNodes = custody == null
            ? null
            : custody.SelectNodes("ActivityCooldowns/Cooldown");
        if (cooldownNodes == null)
        {
            return;
        }

        for (int index = 0; index < cooldownNodes.Count && index < 16; index++)
        {
            XmlElement cooldown = cooldownNodes[index] as XmlElement;
            if (cooldown == null)
            {
                continue;
            }

            string id = (cooldown.GetAttribute("id") ?? string.Empty).Trim();
            if (id.Length == 0 || FindJusticeCustodyActivityById(id) == null)
            {
                continue;
            }

            int remaining = JusticeReadBoundedIntAttribute(cooldown, "remainingSeconds", 0, 300);
            if (remaining > 0)
            {
                _justiceLoadedActivityCooldownSeconds[id] = remaining;
            }
        }
    }

    private void ApplyLoadedJusticeActivityCooldowns(int now)
    {
        if (_justiceLoadedActivityCooldownSeconds.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, int> pair in _justiceLoadedActivityCooldownSeconds)
        {
            _justiceActivityCooldownUntil[pair.Key] = JusticeCustodyFutureTime(
                now,
                pair.Value * 1000);
        }

        _justiceLoadedActivityCooldownSeconds.Clear();
    }

    private static bool JusticeReadBoolAttribute(XmlElement element, string name)
    {
        bool value;
        return element != null &&
               bool.TryParse(element.GetAttribute(name), out value) &&
               value;
    }

    private static int JusticeReadIntAttribute(XmlElement element, string name, int fallback)
    {
        int value;
        return element != null &&
               int.TryParse(
                   element.GetAttribute(name),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value)
            ? value
            : fallback;
    }

    private static int JusticeReadBoundedIntAttribute(
        XmlElement element,
        string name,
        int minimum,
        int maximum)
    {
        int value = JusticeReadIntAttribute(element, name, minimum);
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private void ResetJusticeCustodyPersistentFields(bool preserveDeferredRestore = true)
    {
        JusticeWeaponSnapshot deferredSnapshot = preserveDeferredRestore &&
            _justiceDeferredInventoryRestore
            ? _justiceWeaponSnapshot
            : null;
        int deferredPlayerHandle = _justiceCustodyPlayerHandle;
        int deferredModelHash = _justiceCustodyPlayerModelHash;
        int deferredPlayerSlot = _justiceCustodyPlayerSlot;
        int deferredRetryAt = _justiceNextDeferredInventoryRestoreAt;
        bool shouldPreserveDeferredRestore = deferredSnapshot != null;
        bool preserveLegalReleaseWantedClearAttempt =
            _justiceLegalReleaseFinalizationPending &&
            _justiceLegalReleaseWantedClearAttempted;
        bool preserveAmnestyWantedClearAttempt =
            _justiceAmnestyPending && _justiceAmnestyWantedClearAttempted;

        _justiceCustodySite = JusticeCustodySite.None;
        _justiceCustodyRuntimeActive = false;
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        _justiceCustodyWaitingForRespawn = false;
        _justiceCustodyDeathRebindPending = false;
        _justiceCustodyDeathStatePersistencePending = false;
        _justiceNextCustodyDeathPersistenceRetryAt = 0;
        _justiceCustodyPlayerStateStored = false;
        _justiceCustodyStoredInvincible = false;
        _justiceCustodyStoredFrozen = false;
        _justiceCustodyStoredCanRagdoll = true;
        _justiceCustodyPlayerHandle = 0;
        _justiceCustodyInitialSentenceSeconds = 0;
        _justiceActivityReductionGrantedSeconds = 0;
        _justiceNextCustodySceneRefreshAt = 0;
        _justiceNextCustodyModelRetryAt = 0;
        ResetJusticeCustodyTransferRetryState();
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        _justiceWeaponSnapshot = null;
        _justiceDeferredInventoryRestore = false;
        _justiceNextDeferredInventoryRestoreAt = 0;
        _justiceFineDebitIntent = null;
        _justiceVoluntaryFinePaymentIntent = null;
        _justiceNextVoluntaryPaymentResumeAt = 0;
        ResetJusticeFineCashReadRetry();
        _justiceDisciplineIntent = null;
        _justiceDisciplineActive = false;
        _justiceDisciplineInvincibilityRestorePending = false;
        _justiceDisciplineEndsAt = 0;
        _justiceDisciplineReturnStartedAt = 0;
        _justiceNextDisciplineReturnAttemptAt = 0;
        _justiceDisciplineReturnFailureCount = 0;
        _justiceDisciplineCrimeKind = JusticeCrimeKind.ReportedViolentAct;
        _justiceDisciplineIncidentId = string.Empty;
        _justiceCustodyPlayerModelHash = 0;
        _justiceCustodyPlayerSlot = -1;
        _justiceEscapePersistenceRetryAt = 0;
        _justiceReleaseRestoreStartedAt = 0;
        _justiceReleaseRestoreRetryAt = 0;
        _justiceReleaseTeleportStartedAt = 0;
        _justiceNextReleaseTeleportAttemptAt = 0;
        _justiceReleaseTeleportFailureCount = 0;
        _justiceReleaseSelectedWeaponHash = JusticeUnarmedHash;
        _justiceLegalReleaseWantedClearAttempted =
            preserveLegalReleaseWantedClearAttempt;
        _justiceAmnestyWantedClearAttempted =
            preserveAmnestyWantedClearAttempt;
        _justiceNextActivityScenarioCheckAt = 0;
        _justiceActivityScenarioValidationPending = false;
        _justiceActivityTaskClearPending = false;
        _justiceNextActivityTaskClearAt = 0;
        _justiceActivityCooldownUntil.Clear();
        _justiceLoadedActivityCooldownSeconds.Clear();

        if (shouldPreserveDeferredRestore)
        {
            _justiceWeaponSnapshot = deferredSnapshot;
            _justiceDeferredInventoryRestore = true;
            // Je conserve ce jeton uniquement en mémoire pour que le même ped
            // custom puisse terminer sa restitution dans la session courante.
            // Le writer XML ne sérialise volontairement jamais un handle GTA.
            _justiceCustodyPlayerHandle = deferredPlayerHandle;
            _justiceCustodyPlayerModelHash = deferredModelHash;
            _justiceCustodyPlayerSlot = deferredPlayerSlot;
            _justiceNextDeferredInventoryRestoreAt = deferredRetryAt;
        }
    }

    private void JusticeShutdownCustody()
    {
        Ped player = Game.Player.Character;
        CancelJusticeCustodyActivity(false, Game.GameTime);
        EndJusticeCustodyDiscipline(player);

        // Je rends provisoirement le snapshot à l'arrêt pour qu'un unload ou un
        // crash du loader ne puisse jamais laisser les armes perdues. Les drapeaux
        // persistés restent inchangés : le prochain chargement reconfisquera.
        if (_justiceWeaponSnapshot != null && Entity.Exists(player) && !player.IsDead &&
            IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            if (_justiceInventoryRemoved || _justiceDeferredInventoryRestore)
            {
                // Je ne passe jamais par RemoveAll pendant OnAborted : je fusionne
                // le snapshot complet et vérifié avec l'inventaire présent.
                bool restored = false;
                for (int attempt = 0; attempt < 3 && !restored; attempt++)
                {
                    restored = RestoreJusticeWeaponSnapshotMergeSafe(player, true, true);
                }
                if (!restored)
                {
                    LogWarning(
                        "Justice.Inventaire",
                        "Restitution provisoire incomplète à l'arrêt; le snapshot durable reste disponible.");
                }
            }
        }

        for (int attempt = 0;
             attempt < 3 &&
             (_justiceDisciplineInvincibilityRestorePending ||
              _justiceCustodyPlayerStateStored);
             attempt++)
        {
            EndJusticeCustodyDiscipline(player);
            RestoreJusticeCustodyPlayerTransientState(player);
        }
        CleanupJusticeCustodyEntitiesAndGroups();
        RestoreJusticePoliceSuppressionOnShutdown();
        _justiceCustodyRuntimeActive = false;
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        ResetJusticeCustodyTransferRetryState();
    }

    private void RestoreJusticePoliceSuppressionOnShutdown()
    {
        for (int attempt = 0;
             attempt < 3 && (_justicePoliceSuppressionActive ||
                             _justicePoliceSuppressionRestorePending ||
                             _justicePoliceIgnoreApplied ||
                             _justicePoliceDispatchDisabled);
             attempt++)
        {
            SetJusticeCustodyPoliceSuppression(false);
        }

        if (_justicePoliceSuppressionActive || _justicePoliceSuppressionRestorePending ||
            _justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled)
        {
            LogWarning(
                "Justice.Detention",
                "Restauration policière incomplète à l'arrêt; GTA pourra la rétablir au prochain chargement.");
        }
    }

    /*
     * Dépendances attendues de DonJEnemySpawner.Justice.cs :
     * - champs _justiceEnabled et _justiceCaseState ;
     * - JusticeMarkStateDirty() et JusticeFlushStateNow() ;
     * - JusticePrepareLegalReleaseState(), JusticeRegisterEscape() ;
     * - JusticeRegisterCustodyDisciplineCharge(kind, min, raison, incidentId).
     *
     * Justice.cs appelle Begin au jugement, Update à chaque tick, HandleKey
     * depuis son routeur clavier, les deux hooks XML dans sa racine persistée,
     * Amnesty avant de vider le dossier, puis Shutdown lors de OnAborted.
     */
}
