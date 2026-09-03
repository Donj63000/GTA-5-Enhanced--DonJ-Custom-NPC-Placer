using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;
using GtaControl = GTA.Control;

internal enum JusticePoliceIntegrationMode
{
    Disabled,
    FreeroamBestEffort,
    Force
}

public sealed partial class DonJEnemySpawner
{
    // Je garde la détention dans ce partial : Justice.cs décide du jugement et
    // m'appelle uniquement via les hooks privés documentés en fin de fichier.
    private const ulong JusticeNativeGiveWeaponToPed = 0xBF0FD6E56C964FCBUL;
    private const ulong JusticeNativeSetBlockingOfNonTemporaryEvents = 0x9F8AA94D6D97DBF4UL;
    private const ulong JusticeNativeSetPedKeepTask = 0x971D38760FBC02EFUL;
    private const ulong JusticeNativeTaskWanderStandard = 0xBB9CE077274F6A1BUL;
    private const ulong JusticeNativeIsPedFleeing = 0xBBCCE00B381F8482UL;
    private const ulong JusticeNativeIsPedRagdoll = 0x47E4E977581C5B55UL;
    private const ulong JusticeNativeGetNumDlcWeapons = 0xEE47635F352DA367UL;
    private const ulong JusticeNativeGetDlcWeaponData = 0x79923CD21BECE14EUL;
    private const ulong JusticeNativeSetPoliceIgnorePlayer = 0x32C62AA929C2DA6AUL;
    private const ulong JusticeNativeSetDispatchCopsForPlayer = 0xDB172424876553F4UL;
    private const ulong JusticeNativeSetPedAmmo = 0x14E56BC5B5DB6A19UL;
    private const ulong JusticeNativeGetGroundZFor3DCoord = 0xC906A7DAB05C8D2BUL;
    private const ulong JusticeNativeHasCollisionLoadedAroundEntity = 0xE9676F61BC0B3321UL;
    private const ulong JusticeNativeIsScreenFadedIn = 0x5A859503B0C08678UL;
    private const ulong JusticeNativeIsScreenFadedOut = 0xB16FCE9DDC7BA182UL;
    private const ulong JusticeNativeIsScreenFadingIn = 0x5C544BC6C57AC575UL;
    private const ulong JusticeNativeIsScreenFadingOut = 0x797AC7CB535BA28FUL;

    private const int JusticeCustodySceneRefreshMs = 1500;
    private const int JusticeCustodySceneReturnRetryMs = 5000;
    private const int JusticeCustodySceneCalmDelayMs = 10000;
    private const int JusticeCustodyMaximumGuardCount = 4;
    private const int JusticeCustodyMaximumInmateCount = 8;
    private const int JusticeCustodyGuardRetaliationScanMs = 175;
    private const int JusticeCustodyGuardCombatRetryMs = 1500;
    private const int JusticeCustodyGuardWantedMinimum = 2;
    private const int JusticeCustodyPoliceSuppressionIntervalMs = 1000;
    private const int JusticeCustodyTransferInitialRetryMs = 750;
    private const int JusticeCustodyTransferMaximumRetryMs = 5000;
    private const int JusticeCustodyTransferTimeoutMs = 30000;
    private const int JusticeCustodyReleaseTeleportTimeoutMs = 30000;
    private const int JusticeCustodyDeferredRestoreDelayMs = 15000;
    private const int JusticeCustodyDeferredRestoreRetryMs = 5000;
    private const int JusticeCustodyEscapeGraceMs = 6000;
    private const int JusticeCustodyMaxFrameElapsedMs = 2000;
    private const int JusticeCustodyResidualMissionFlagObservationWindowMs = 15000;
    private const int JusticeCustodyModelTimeoutMs = 75;
    private const int JusticeCustodyModelRetryMs = 7500;
    private const int JusticeCustodyMaxWeapons = 160;
    private const int JusticeCustodyMaxDlcWeaponDefinitions = 512;
    private const int JusticeCustodyMaxComponentsPerWeapon = 128;
    private const int JusticeCustodyInventoryCaptureMaximumAttempts = 3;
    private const int JusticeCustodyInventoryRemovalMaximumAttempts = 5;
    private const int JusticeCustodyMaximumSentenceSeconds = JusticePolicy.MaxActiveSentenceSeconds;
    private const int JusticeCustodyPrisonThresholdSeconds = 5 * 60;
    private const int JusticeCustodyFineConversionMaximumSeconds = 100;
    private const int JusticeCustodyFineDollarsPerSecond = 150;
    private const int JusticeCustodyFineCashReadRetryMs = 750;
    private const int JusticeCustodyDeathPersistenceRetryMs = 1000;
    private const int JusticeDlcWeaponDataSize = 312;
    private const int JusticeDlcWeaponHashOffset = 8;
    private const float JusticeCustodyGuardPostReturnDistanceSquared = 6.25f;

    private const int JusticeStunGunHash = unchecked((int)0x3656C8C1);
    private const int JusticeNightstickHash = unchecked((int)0x678B81B1);
    private const int JusticeUnarmedHash = unchecked((int)0xA2719263);

    private enum JusticeCustodySite
    {
        None,
        MissionRow,
        Bolingbroke
    }

    private enum JusticePreJudgmentHoldingSource
    {
        None,
        DurablePoliceDeath,
        PendingWalPoliceDeath,
        PendingWalCustodyRebind,
        Captured,
        RepairPoliceDeath,
        RepairPoliceArrest
    }

    private enum JusticeCashWriteResult
    {
        Unknown,
        Succeeded,
        Rejected
    }

    private enum JusticeInventoryCustodyState
    {
        None,
        CapturePending,
        SnapshotPersisted,
        RemovalPending,
        RemovedVerified,
        UnsupportedPreserved,
        RestorePending,
        RestoreAmbiguous
    }

    private enum JusticeInventoryPreparationResult
    {
        Ready,
        RetryableFailure,
        UnsupportedLoadout
    }

    private enum JusticeInventoryRemovalResult
    {
        NotAttempted,
        RemovedVerified,
        EffectMayHaveApplied
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
        internal JusticeCustodyVolume[] ContainmentVolumes;
        internal Vector3[] GuardPositions;
        internal float[] GuardHeadings;
        internal Vector3[] InmatePositions;
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
        internal JusticePaymentResolution Resolution =
            JusticePaymentResolution.Prepared;
        internal long FineInDisputeBefore;
        internal long AmbiguousAmount;
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
    private readonly List<Ped> _justiceCustodyGuards =
        new List<Ped>(JusticeCustodyMaximumGuardCount);
    private readonly List<Ped> _justiceCustodyInmates =
        new List<Ped>(JusticeCustodyMaximumInmateCount);
    // Je lie chaque handle à sa génération observée au spawn : un handle GTA
    // recyclé ne suffit jamais à transférer la propriété d'un ped à Justice.
    private Dictionary<int, int> _justiceCustodyPedGenerationByHandle =
        new Dictionary<int, int>();
    private int[] _justiceCustodyGuardReturnRetryAt =
        new int[JusticeCustodyMaximumGuardCount];
    private int[] _justiceCustodyGuardCombatRetryAt =
        new int[JusticeCustodyMaximumGuardCount];
    private int[] _justiceCustodyInmateReturnRetryAt =
        new int[JusticeCustodyMaximumInmateCount];
    private int[] _justiceCustodyGuardCalmUntil =
        new int[JusticeCustodyMaximumGuardCount];
    private int[] _justiceCustodyInmateCalmUntil =
        new int[JusticeCustodyMaximumInmateCount];
    private bool[] _justiceCustodyGuardWasNaturallyBusy =
        new bool[JusticeCustodyMaximumGuardCount];
    private bool[] _justiceCustodyInmateWasNaturallyBusy =
        new bool[JusticeCustodyMaximumInmateCount];
    private bool _justiceCustodyGuardRetaliationActive;
    private int _justiceNextCustodyGuardRetaliationScanAt;
    private int _justiceCustodyLastDamagingGuardHandle;
    private int _justiceCustodyLastDamagingGuardGeneration;
    private long _justiceCustodyLastGuardDamageAtMs = -1L;
    private bool _justiceCustodyGuardDeathCauseEvaluated;
    private bool _justiceCustodyGuardDeathPenaltyPending;

    private JusticeCustodySite _justiceCustodySite;
    private bool _justiceCustodyRuntimeActive;
    private bool _justiceCustodyTransferPending;
    private bool _justiceCustodyTransferRollbackFinalizationPending;
    private bool _justiceCustodyTransferRollbackPrecommitRedundant;
    private long _justiceCustodyTransferRollbackFinalizationRevision;
    private long _justiceCustodyTransferRollbackFinalizationWriteFailures;
    private int _justiceNextCustodyTransferRollbackFinalizationRetryAt;
    private bool _justiceCustodyResumePending;
    private bool _justiceCustodyWaitingForRespawn;
    private bool _justiceCustodyRespawnTransferPending;
    private bool _justiceCustodyRespawnMaskNeedsRearm;
    private bool _justiceCustodyRespawnRestorePending;
    private bool _justicePoliceDeathRespawnMaskIntentPending;
    private JusticePreJudgmentHoldingSource _justicePreJudgmentHoldingSource;
    private bool _justicePoliceDeathPreJudgmentHoldingEstablished;
    private JusticeCustodySite _justicePoliceDeathPreJudgmentHoldingSite;
    private int _justicePoliceDeathPreJudgmentHoldingOwnerSlot = -1;
    private int _justicePoliceDeathPreJudgmentHoldingOwnerModelHash;
    private int[] _justiceRepairArrestPreJudgmentHoldingModelHashes;
    private int _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt;
    private int _justicePoliceDeathPreJudgmentHoldingFailureCount;
    private int _justicePoliceDeathPreJudgmentHoldingStartedAt;
    private bool _justicePoliceDeathPreJudgmentHoldingFallbackLogged;
    private bool _justicePreJudgmentHoldingStreamingPending;
    private bool _justicePreJudgmentHoldingPositionApplied;
    private bool _justicePreJudgmentHoldingProtectionOwned;
    private bool _justicePreJudgmentHoldingCanRagdollCaptured;
    private bool _justicePreJudgmentHoldingStoredCanRagdoll;
    private int _justicePreJudgmentHoldingStreamingPlayerHandle;
    private int _justicePreJudgmentHoldingStreamingPlayerModelHash;
    private int _justicePreJudgmentHoldingStreamingOwnerSlot = -1;
    private int _justicePreJudgmentHoldingStreamingOwnerModelHash;
    private Vector3 _justicePreJudgmentHoldingStreamingTarget;
    private float _justicePreJudgmentHoldingStreamingHeading;
    private bool _justiceCapturePrecommitConfirmed;
    private int _justiceCapturePrecommitConfirmedOwnerSlot = -1;
    private int _justiceCapturePrecommitConfirmedOwnerModelHash;
    private string _justiceCapturePrecommitConfirmedEpisodeId = string.Empty;
    private bool _justiceCustodyPersistenceOutageHoldingEstablished;
    private bool _justiceCustodyDeathRebindPending;
    private bool _justiceCustodyDeathStatePersistencePending;
    private long _justiceCustodyDeathPersistenceRevision;
    private long _justiceCustodyDeathPersistenceWriteFailures;
    private bool _justiceCustodyDeathPersistenceWriterFailureObserved;
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
    private bool _justiceCustodyResidualMissionFlagBypassArmed;
    private long _justiceCustodyResidualMissionFlagObservationDeadlineMs;
    private int _justiceCustodyInitialSentenceSeconds;
    private int _justiceNextCustodySceneRefreshAt;
    private int _justiceNextCustodyModelRetryAt;
    private int _justiceOutsideCustodySinceAt;
    private bool _justiceCustodyContainmentEstablished;
    private int _justiceNextPoliceSuppressionAt;
    private bool _justicePoliceSuppressionActive;
    private bool _justicePoliceIgnoreApplied;
    private bool _justicePoliceDispatchDisabled;
    private bool _justicePoliceSuppressionRestorePending;
    private bool _justicePoliceSuppressionFailureLogged;
    private int _justiceNextPoliceSuppressionRestoreAt;
    private JusticePoliceIntegrationMode _justicePoliceIntegrationMode =
        JusticePoliceIntegrationMode.FreeroamBestEffort;
    private int _justiceCustodyTransferStartedAt;
    private int _justiceNextCustodyTransferAttemptAt;
    private int _justiceCustodyTransferFailureCount;
    private bool _justiceCustodyTransferTimeoutLogged;
    private bool _justiceCustodyTransferPrecommitConfirmed;
    private bool _justiceCustodyFallbackPrecommitPending;

    private JusticeWeaponSnapshot _justiceWeaponSnapshot;
    private bool _justiceDeferredInventoryRestore;
    private int _justiceNextDeferredInventoryRestoreAt;
    private JusticeFineDebitIntent _justiceFineDebitIntent;
    private int _justiceNextFineCashReadAttemptAt;
    private bool _justiceFineCashReadFailureLogged;
    private Func<int, int?> _justiceCashReadOverride = null;
    private Func<int, int, bool?> _justiceCashWriteOverride = null;
    private bool _justiceInventoryRemoved;
    private bool _justiceWeaponControlsLocked;
    private int _justiceNextInventoryPersistenceRetryAt;
    private JusticeInventoryCustodyState _justiceInventoryCustodyState;
    private int _justiceInventoryCaptureFailureCount;
    private int _justiceInventoryRemovalFailureCount;

    private int _justiceEscapePersistenceRetryAt;
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
            ContainmentVolumes = new[]
            {
                // Le volume de jeu reste précis, mais l'évasion utilise une
                // enveloppe extérieure tolérante aux portes, escaliers et petits
                // décalages de streaming du commissariat.
                new JusticeCustodyVolume(
                    new Vector3(432.0f, -1012.0f, 15.0f),
                    new Vector3(490.0f, -966.0f, 45.0f))
            },
            GuardPositions = new[]
            {
                new Vector3(457.10f, -991.08f, 24.91f),
                new Vector3(463.45f, -991.10f, 24.91f)
            },
            GuardHeadings = new[] { 178.0f, 182.0f },
            InmatePositions = new Vector3[0]
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
            ContainmentVolumes = new[]
            {
                // L'évasion ne s'arme qu'après franchissement clair de l'enceinte.
                // Cette seconde enveloppe couvre les murs, tours, cours, bâtiments
                // et les imprécisions de coordonnées sans englober la sortie légale.
                new JusticeCustodyVolume(
                    new Vector3(1465.0f, 2345.0f, 0.0f),
                    new Vector3(1840.0f, 2760.0f, 120.0f),
                    new[]
                    {
                        1580.0f, 2350.0f,
                        1760.0f, 2385.0f,
                        1840.0f, 2480.0f,
                        1840.0f, 2660.0f,
                        1760.0f, 2755.0f,
                        1555.0f, 2755.0f,
                        1470.0f, 2660.0f,
                        1470.0f, 2480.0f
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
            }
        };
    }

    private void JusticeBeginCustodyTransfer(bool deathCapture)
    {
        if (_justiceCaseState == null || !_justiceEnabled)
        {
            return;
        }

        if (HasTerminatorRuntimeState())
        {
            // Je termine la protection Terminator avant toute photographie du
            // joueur : sa régénération ne doit jamais survivre à l'arrestation.
            DisableTerminatorMode(false);
            if (HasTerminatorRuntimeState())
            {
                return;
            }
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
        bool stationPlanned = GetJusticeCustodyTotalRemainingSeconds(
            _justiceCaseState) < JusticeCustodyPrisonThresholdSeconds;
        if (!JusticeCollectFineAndConvertDetention(stationPlanned, string.Empty))
        {
            return;
        }

        // Une amende intégralement payée se termine aux formalités, mais je fais
        // toujours le débit avant de décider si une cellule est nécessaire.
        if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L)
        {
            // Je considère aussi cette issue comme une arrestation confirmée :
            // la plaque, la tenue et le mandat Recognition ne doivent pas
            // survivre au seul motif que l'amende a évité le passage en cellule.
            if (!EnsureJusticeRecognitionCaptureResetDurable(
                    "arrestation confirmée et amende intégralement acquittée"))
            {
                // Je garde Captured et reprends cette frontière au prochain tick :
                // une panne du journal Recognition ne devient jamais une remise à
                // zéro perdue après un crash.
                return;
            }
            SuppressJusticeRecognitionWantedLoss(
                "arrestation confirmée sans placement en cellule");
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

        if (HasPlacementSessionState())
        {
            // Je rends d'abord les flags et la caméra du placement, y compris
            // après un démarrage interrompu avant _placementMode = true.
            StopPlacementMode(false);
        }
        if (_placementPlayerStateStored ||
            HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement))
        {
            // Le snapshot Justice ne doit surtout pas capturer notre valeur
            // temporaire true. La phase Captured retentera ce transfert au tick
            // suivant, après la restauration différée du joueur.
            ShowStatus(
                "Justice : transfert différé pendant la restauration du placement.",
                3000);
            return;
        }

        // Je reclasse après la conversion de l'amende : une peine ayant atteint
        // exactement cinq minutes part directement à Bolingbroke.
        _justiceCustodySite = GetJusticeCustodySiteForSentence(
            GetJusticeCustodyTotalRemainingSecondsForRuntime(_justiceCaseState));
        _justiceCustodyInitialSentenceSeconds = Math.Max(
            _justiceCustodyInitialSentenceSeconds,
            GetJusticeCustodyTotalRemainingSecondsForRuntime(_justiceCaseState));
        _justiceCustodyRuntimeActive = true;
        _justiceCustodyTransferPending = true;
        _justiceCustodyResumePending = false;
        _justiceCustodyPersistenceOutageHoldingEstablished = false;
        if (!waitForRespawn)
        {
            if (_justiceCustodyRespawnTransferPending)
            {
                if (!TryRestoreJusticeCustodyRespawnTransferMask())
                {
                    return;
                }
            }
            else
            {
                _justiceCustodyRespawnMaskNeedsRearm = false;
                _justiceCustodyRespawnRestorePending = false;
            }
        }
        ResetJusticeCustodyTransferRetryState();
        _justiceCustodyWaitingForRespawn = waitForRespawn;
        _justiceOutsideCustodySinceAt = 0;
        _justiceCustodyContainmentEstablished = false;
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

        if (!StopJusticeConcurrentPlayerProtectionModes())
        {
            // Je ne fais progresser ni transfert ni peine tant qu'un autre mode
            // possède encore une protection ou un gel du joueur.
            ResetJusticeCustodyClock(now);
            return;
        }

        // Je masque l'origine GTA dès que le ped vivant est attribuable au bon
        // détenu, avant toute attente repository/WAL. L'hôpital ne doit jamais
        // redevenir visible pendant les retries qui précèdent le transfert.
        UpdateJusticeCustodyRespawnTransferMask(player);

        if (!PersistJusticeCustodyDeathStateBeforeRespawn(now))
        {
            TryMaintainJusticeCustodyDuringPermanentPersistenceOutage(
                player,
                now);
            // Je n'accepte ni nouveau ped ni progression de peine tant que le
            // droit de rebind après décès n'existe pas durablement sur disque.
            ResetJusticeCustodyClock(now);
            return;
        }

        UpdateJusticeCustodyGuardRetaliation(player, now);

        if (Entity.Exists(player) && player.IsDead)
        {
            if (IsJusticeRuntimeSuspended(player))
            {
                if (IsJusticeCustodyDeathIdentityCompatible(player))
                {
                    ObserveJusticeCustodyDeathDuringSuspension(player);
                }
                InterruptJusticeCustodyEscapeObservation();
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

        if (!_justiceCustodyRuntimeActive && _justiceCaseState.Phase == JusticePhase.Captured)
        {
            if (!IsJusticeCapturePrecommitConfirmedForCurrentEpisode() ||
                _justiceCaptureRetryPending)
            {
                // Je laisse exclusivement BeginJusticeCapture reprendre le
                // précommit du jugement. JusticeBeginCustodyTransfer ne peut
                // jamais contourner cette preuve, même si le holding échoue.
                ResetJusticeCustodyClock(now);
                return;
            }

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
            GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L)
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
            InterruptJusticeCustodyEscapeObservation();
            ResetJusticeCustodyClock(now);
            return;
        }

        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            // Je suspends sur un vrai changement de protagoniste pour ne jamais
            // rendre le loadout de Michael à Franklin (ou inversement).
            InterruptJusticeCustodyEscapeObservation();
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

            // Le helper l'a normalement armé dès le premier ped vivant. Je garde
            // ce fallback runtime pour une native d'identité devenue disponible
            // seulement après le rebind durable.
            _justiceCustodyRespawnTransferPending = true;
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

        EnforceJusticeCustodyWeaponLock(player);

        if (!JusticeCustodyCanMutateWorld(player))
        {
            InterruptJusticeCustodyEscapeObservation();
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

        // Je traite la sortie avant le gate Incarcerated : la phase Escaping
        // doit continuer à accumuler ses six secondes de grâce.
        UpdateJusticeCustodyEscape(player, now);
        if (!JusticeIsCustodyActive ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated)
        {
            ResetJusticeCustodyClock(now);
            return;
        }

        AdvanceJusticeCustodyClock(now);
        EnsureJusticeCustodyScene(now);

        if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L &&
            _justiceCaseState.Phase == JusticePhase.Incarcerated)
        {
            CompleteJusticeLegalRelease(player);
        }
    }

    private void ObserveJusticeCustodyDeath(Ped player, int now)
    {
        _justiceCustodyRespawnMaskNeedsRearm |=
            _justiceCustodyRespawnTransferPending;
        bool identityCompatible =
            IsJusticeCustodyDeathIdentityCompatible(player);
        if (identityCompatible)
        {
            CaptureJusticeCustodyGuardDamageFrontsAtDeath(player);
            FreezeJusticeCustodyGuardDeathPenalty(player);
        }
        if (identityCompatible &&
            (!_justiceCustodyDeathRebindPending ||
             _justicePendingDeathFrontWalRecord != null ||
             _justiceCustodyGuardDeathPenaltyPending))
        {
            // Je tente d'abord le front exact, mais son indisponibilité ne doit
            // jamais laisser le prochain ped vivant sans propriétaire fail-closed.
            if (TryPersistJusticeCustodyDeathFrontToWal(
                    player,
                    _justiceCustodyGuardDeathPenaltyPending))
            {
                _justiceCustodyGuardDeathPenaltyPending = false;
            }
        }
        if (identityCompatible)
        {
            ResetJusticeCustodyGuardRetaliation(player, true, true);
            ArmJusticeCustodyDeathFailClosedState(player, now);
        }

        ResetJusticeCustodyClock(now);
        _justiceCustodyElapsedRemainderMs = 0;
    }

    private void ObserveJusticeCustodyDeathDuringSuspension(Ped player)
    {
        _justiceCustodyRespawnMaskNeedsRearm |=
            _justiceCustodyRespawnTransferPending;
        if (!IsJusticeCustodyDeathIdentityCompatible(player))
        {
            return;
        }
        CaptureJusticeCustodyGuardDamageFrontsAtDeath(player);
        FreezeJusticeCustodyGuardDeathPenalty(player);
        if (!_justiceCustodyDeathRebindPending ||
            _justicePendingDeathFrontWalRecord != null ||
            _justiceCustodyGuardDeathPenaltyPending)
        {
            if (TryPersistJusticeCustodyDeathFrontToWal(
                    player,
                    _justiceCustodyGuardDeathPenaltyPending))
            {
                _justiceCustodyGuardDeathPenaltyPending = false;
            }
        }
        ResetJusticeCustodyGuardRetaliation(player, true, true);
        ArmJusticeCustodyDeathFailClosedState(
            player,
            GetJusticeRawGameTimeSafe());
    }

    private void ArmJusticeCustodyDeathFailClosedState(Ped player, int now)
    {
        _justiceCustodyContainmentEstablished = false;
        _justiceOutsideCustodySinceAt = 0;
        bool stateChanged = false;
        if (!_justiceCustodyDeathRebindPending)
        {
            // Je n'autorise une nouvelle identité qu'après avoir réellement
            // observé la mort du détenu lié. Un changement de héros vivant ne
            // passe jamais par ce helper.
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
            JusticeMarkStateDirty();
            _justiceCustodyDeathStatePersistencePending = true;
            _justiceCustodyDeathPersistenceRevision = 0L;
            _justiceCustodyDeathPersistenceWriteFailures = 0L;
            _justiceCustodyDeathPersistenceWriterFailureObserved = false;
            _justiceNextCustodyDeathPersistenceRetryAt = 0;
            PersistJusticeCustodyDeathStateBeforeRespawn(now);
        }
    }

    private bool PersistJusticeCustodyDeathStateBeforeRespawn(int now)
    {
        if (!_justiceCustodyDeathStatePersistencePending)
        {
            _justiceCustodyDeathPersistenceRevision = 0L;
            _justiceCustodyDeathPersistenceWriteFailures = 0L;
            _justiceCustodyDeathPersistenceWriterFailureObserved = false;
            return true;
        }

        InitializeJusticePersistenceServices();
        JusticeRepository repository = _justiceRepository;
        if (repository == null || _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        if (_justiceCustodyDeathPersistenceRevision > 0L)
        {
            ObserveJusticeRepositoryFailure();
            JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
            if (diagnostics.DiskRevision >=
                _justiceCustodyDeathPersistenceRevision)
            {
                FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
                if (!IsJusticeCustodyDeathFrontResultDurable())
                {
                    return false;
                }
                _justiceCustodyDeathStatePersistencePending = false;
                _justiceCustodyDeathPersistenceRevision = 0L;
                _justiceCustodyDeathPersistenceWriteFailures = 0L;
                _justiceCustodyDeathPersistenceWriterFailureObserved = false;
                _justiceNextCustodyDeathPersistenceRetryAt = 0;
                return true;
            }

            bool queuedWriteFailed = diagnostics.WriteFailures >
                _justiceCustodyDeathPersistenceWriteFailures;
            if (queuedWriteFailed)
            {
                // Je mémorise ce rejet avant de réenfiler une révision. Sans ce
                // latch, le nouveau baseline masquerait la panne précisément au
                // tick où le joueur doit être replacé hors de l'hôpital.
                _justiceCustodyDeathPersistenceWriterFailureObserved = true;
            }
            if (!queuedWriteFailed ||
                !JusticeCustodyHasReached(
                    now,
                    _justiceNextCustodyDeathPersistenceRetryAt))
            {
                return false;
            }

            // Je remplace seulement une révision effectivement rejetée par le
            // writer. Tant qu'elle est encore en vol, je la sonde sans créer de
            // snapshots concurrents ni bloquer le thread GTA.
            _justiceNextStateFlushAttemptAtMs = 0L;
        }
        else if (!JusticeCustodyHasReached(
                     now,
                     _justiceNextCustodyDeathPersistenceRetryAt))
        {
            return false;
        }

        if (!TryRejectJusticeCriticalBarrierBeforeCustodyDeath())
        {
            _justiceNextCustodyDeathPersistenceRetryAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyDeathPersistenceRetryMs);
            return false;
        }

        long writeFailuresBeforeEnqueue =
            repository.GetDiagnostics().WriteFailures;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            _justiceNextCustodyDeathPersistenceRetryAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyDeathPersistenceRetryMs);
            return false;
        }

        _justiceCustodyDeathPersistenceRevision =
            _justiceLastQueuedPersistenceRevision;
        _justiceCustodyDeathPersistenceWriteFailures =
            writeFailuresBeforeEnqueue;
        _justiceNextCustodyDeathPersistenceRetryAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyDeathPersistenceRetryMs);
        if (_justiceCustodyDeathPersistenceRevision <= 0L)
        {
            RegisterJusticePersistenceFailure(
                "révision du décès absente après enqueue");
            return false;
        }

        // Même si le writer finit dans cette frame, je confirme DiskRevision au
        // tick suivant. Le rebind ne dépend jamais d'un simple enqueue mémoire.
        return false;
    }

    private bool TryMaintainJusticeCustodyDuringPermanentPersistenceOutage(
        Ped player,
        int now)
    {
        bool permanentInitializationOutage =
            _justicePersistenceInitializationFailurePermanent &&
            _justicePersistenceServicesUnavailable;
        bool runtimeWriterOutage =
            _justiceCustodyDeathPersistenceWriterFailureObserved ||
            _justiceCustodyPersistenceOutageHoldingEstablished;
        if ((!permanentInitializationOutage && !runtimeWriterOutage) ||
            !_justiceCustodyWaitingForRespawn ||
            _justiceCaseState == null ||
            !_justiceEnabled ||
            !IsJusticeCustodyPhase(_justiceCaseState.Phase) ||
            string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId) ||
            !Entity.Exists(player) || player.IsDead ||
            (!CanMaskJusticeCustodyRespawnOrigin(player) &&
             !IsJusticeCustodyPlayerIdentityCompatible(player)))
        {
            return false;
        }
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyTransferAttemptAt))
        {
            return false;
        }

        JusticeCustodySite requiredSite = GetJusticeCustodySiteForSentence(
            GetJusticeCustodyTotalRemainingSecondsForRuntime(_justiceCaseState));
        if (_justiceCustodySite == JusticeCustodySite.None ||
            (_justiceCustodySite == JusticeCustodySite.MissionRow &&
             requiredSite == JusticeCustodySite.Bolingbroke))
        {
            _justiceCustodySite = requiredSite;
        }
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null)
        {
            return false;
        }

        bool insideContainment = IsInsideJusticeCustodyLayout(
            layout,
            player.Position);
        bool moved = insideContainment;
        if (!moved)
        {
            if (_justiceCustodyRespawnTransferPending)
            {
                ReassertJusticeCustodyRespawnTransferMask();
            }
            // Je réutilise le déplacement non bloquant du holding : il conserve
            // le masque pendant le streaming et ne contient aucun FadeIn interne.
            moved = TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(
                player,
                layout.CellPosition,
                layout.CellHeading);
        }

        if (!moved ||
            !IsInsideJusticeCustodyLayout(layout, player.Position))
        {
            if (_justiceCustodyRespawnTransferPending)
            {
                // Le streaming ou la collision restent incomplets. Je garde le
                // joueur masqué jusqu'au prochain essai de maintien.
                ReassertJusticeCustodyRespawnTransferMask();
            }
            _justiceNextCustodyTransferAttemptAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyTransferInitialRetryMs);
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int modelHash = GetJusticePedModelHashSafe(player);
        _justiceCustodyPlayerHandle = player.Handle;
        _justiceCustodyPlayerModelHash = modelHash;
        if (IsJusticeCanonicalProfileSlot(currentSlot))
        {
            _justiceCustodyPlayerSlot = currentSlot;
        }
        if (!CompleteJusticePreJudgmentHoldingStreamingProtection(player) ||
            !EnsureJusticeCustodyPlayerMobility(player))
        {
            if (_justiceCustodyRespawnTransferPending)
            {
                ReassertJusticeCustodyRespawnTransferMask();
            }
            _justiceNextCustodyTransferAttemptAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyTransferInitialRetryMs);
            return false;
        }

        bool firstHolding = !_justiceCustodyPersistenceOutageHoldingEstablished;
        _justiceCustodyPersistenceOutageHoldingEstablished = true;
        _justicePoliceDeathRespawnMaskIntentPending = false;
        _justiceCustodyContainmentEstablished = true;
        _justiceOutsideCustodySinceAt = 0;
        _justiceNextCustodyTransferAttemptAt = 0;
        if (_justiceCustodyRespawnTransferPending)
        {
            TryRestoreJusticeCustodyRespawnTransferMask();
        }

        if (firstHolding)
        {
            // Je ne libère jamais un détenu parce que le XML est corrompu. Le
            // joueur reste dans l'enceinte, sans progression ni confiscation,
            // jusqu'à la réparation de la persistance au prochain chargement.
            ShowStatus(
                "Justice : maintien en cellule, sauvegarde à réparer; peine suspendue.",
                6000);
            LogWarning(
                "Justice.MaintienDetention",
                permanentInitializationOutage
                    ? "Persistance définitivement indisponible : maintien physique en détention sans libération ni progression."
                    : "Writer de persistance en échec : maintien physique en détention sans libération ni progression.");
        }
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
            GetJusticeCustodyTotalRemainingSecondsForRuntime(_justiceCaseState));
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

        if (!StoreJusticeCustodyPlayerState(player))
        {
            HandleJusticeCustodyTransferFailure(player, now);
            return;
        }
        if (!_justiceCustodyTransferPrecommitConfirmed)
        {
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly(
                    "CompleteJusticeCustodyTransfer"))
            {
                // Je durcis une seule fois le snapshot transitoire avant toute
                // confiscation. Le retry inventaire peut ensuite reprendre sa
                // propre barrière sans être bloqué par un nouvel appelant.
                HandleJusticeCustodyTransferFailure(player, now);
                return;
            }
            _justiceCustodyTransferPrecommitConfirmed = true;
        }

        if (!EnsureJusticeRecognitionCaptureResetDurable(
                "arrestation et placement en détention"))
        {
            // Je n'entre pas dans la frontière irréversible d'inventaire tant que
            // le reset plaque/tenue/mandat n'est pas lui-même durable. Le joueur
            // reste sous le masque et les contrôles fail-closed pendant le retry.
            if (_justiceCustodyRespawnTransferPending)
            {
                ReassertJusticeCustodyRespawnTransferMask();
            }
            EnforceJusticePreJudgmentHoldingControlLock(player);
            _justiceNextCustodyTransferAttemptAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyTransferInitialRetryMs);
            return;
        }

        if (_justiceCustodyFallbackPrecommitPending)
        {
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly(
                    "CompleteJusticeCustodyTransfer"))
            {
                // Je reprends exactement la frontière du fallback avant de
                // réévaluer l'inventaire ou d'autoriser le téléport.
                HandleJusticeCustodyTransferFailure(player, now);
                return;
            }
            _justiceCustodyFallbackPrecommitPending = false;
        }

        JusticeInventoryPreparationResult inventoryPreparation =
            EnsureJusticeInventoryReadyForCustodyTransfer(player, now);
        if (inventoryPreparation != JusticeInventoryPreparationResult.Ready)
        {
            if (CanContinueJusticeCustodyTransferWithoutInventoryConfiscation(
                    inventoryPreparation))
            {
                EnterJusticeNonDestructiveCustodyFallback(player, now);
                _justiceCustodyFallbackPrecommitPending = true;
                if (!PersistJusticeCriticalPrecommitRedundantly(
                        "CompleteJusticeCustodyTransfer"))
                {
                    // Je rends le fallback durable avant le téléport. Au tick
                    // suivant, son état prêt évite toute nouvelle confiscation.
                    HandleJusticeCustodyTransferFailure(player, now);
                    return;
                }
                _justiceCustodyFallbackPrecommitPending = false;
            }
            else
            {
                HandleJusticeCustodyTransferFailure(player, now);
                return;
            }
        }

        bool transferred = false;
        bool maskRespawnOrigin = _justiceCustodyRespawnTransferPending;
        try
        {
            if (maskRespawnOrigin)
            {
                ReassertJusticeCustodyRespawnTransferMask();
            }
            _activeInteriorSession = null;
            ClearInteriorRenderingFocusSafe(player);
            TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading);
            transferred = IsJusticeTeleportVerified(player, transferPosition, 8.0f);
        }
        catch (Exception ex)
        {
            // Je conserve le masque pendant tout retry du même détenu. Le rendre
            // ici exposerait de nouveau l'hôpital entre deux essais de transfert.
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
        if (transferred && !IsInsideJusticeCustodyLayout(layout, player.Position))
        {
            // Une native peut annoncer le déplacement alors qu'un autre script a
            // replacé le ped dans la même frame. Je refuse alors d'armer l'évasion.
            transferred = false;
        }
        if (transferred &&
            (!CompleteJusticePreJudgmentHoldingStreamingProtection(player) ||
             !EnsureJusticeCustodyPlayerMobility(player)))
        {
            // Je ne valide jamais une détention dont le ped reste gelé par la
            // transition d'arrestation ou de respawn encore active côté GTA.
            transferred = false;
        }
        if (!transferred)
        {
            if (maskRespawnOrigin)
            {
                // Les deux téléporteurs rendent leur propre fade même lorsqu'ils
                // échouent. Je remasque l'origine GTA avant tout nettoyage : une
                // exception avalée par le stage ne peut plus exposer l'hôpital.
                ReassertJusticeCustodyRespawnTransferMask();
            }
            HandleJusticeCustodyTransferFailure(player, now);
            RestoreJusticeCustodyPlayerTransientState(player);
            return;
        }

        // Le masque de respawn reste armé à travers les retries et n'est consommé
        // qu'après un transfert physiquement vérifié. Un premier téléport refusé
        // par le moteur ne peut donc pas révéler l'hôpital au passage suivant.
        if (maskRespawnOrigin)
        {
            // Le téléport principal rend normalement l'écran, mais une exception
            // suivie d'un fallback d'urgence réussi peut contourner son fade-in.
            // Je réaffirme donc explicitement la restitution avant de consommer
            // le latch; répéter cette native après le chemin normal est sans effet.
            TryRestoreJusticeCustodyRespawnTransferMask();
        }
        ClearJusticeRepairArrestPreJudgmentHoldingIntent(
            _justiceCustodyPlayerSlot,
            _justiceCustodyPlayerModelHash);
        ResetJusticePoliceDeathPreJudgmentHoldingState();
        ResetJusticeCapturePrecommitConfirmation();
        _justicePoliceDeathRespawnMaskIntentPending = false;
        _justiceCustodyPersistenceOutageHoldingEstablished = false;
        _justiceCustodyPlayerHandle = player.Handle;
        _justiceCustodyPlayerModelHash = GetJusticePedModelHashSafe(player);
        RememberJusticeCustodyPlayerSlot();
        if (_justiceCustodyInitialSentenceSeconds <= 0)
        {
            _justiceCustodyInitialSentenceSeconds = Math.Max(
                1,
                GetJusticeCustodyTotalRemainingSecondsForRuntime(
                    _justiceCaseState));
        }
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        bool transferTimedOut = unchecked((uint)(now - _justiceCustodyTransferStartedAt)) >=
                                (uint)JusticeCustodyTransferTimeoutMs;
        ResetJusticeCustodyTransferRetryState();
        _justiceCustodyRuntimeActive = true;
        ArmJusticeCustodyResidualMissionFlagBypass();
        _justiceCustodyLastTickAt = now;
        _justiceCustodyElapsedRemainderMs = 0;
        _justiceOutsideCustodySinceAt = 0;
        _justiceCustodyContainmentEstablished = true;
        ApplyJusticeTransition(
            transferTimedOut ? JusticeSignal.TransferTimedOut : JusticeSignal.TransferCompleted,
            _justiceCaseState.CustodyEpisodeId);
        if (_justiceCaseState.Phase != JusticePhase.Incarcerated)
        {
            _justiceCaseState.Phase = JusticePhase.Incarcerated;
        }

        // Une capture met fin à la poursuite ambiante. Seule une riposte locale
        // déjà restaurée peut conserver son plancher de deux étoiles, sans
        // réactiver le dispatch extérieur dans l'enceinte.
        SuppressJusticeRecognitionWantedLoss(
            "entrée en détention confirmée");
        if (_justiceCustodyGuardRetaliationActive)
        {
            SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum);
        }
        else
        {
            ClearJusticeWantedLevelOnce();
        }
        SynchronizeJusticeRecognition(true);
        SetJusticeCustodyPoliceSuppression(true);
        EnsureJusticeCustodyRelationshipGroups();
        EnsureJusticeCustodyScene(now);

        JusticeOperation enterOperation = CreateJusticeCustodyOperation(JusticeOperationKind.EnterCustody);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, enterOperation);
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        ShowStatus(layout.DisplayName + " : peine à purger.", 5500);
        LogInfo("Justice.Detention", "Entrée dans " + layout.DisplayName + ".");
    }

    private static bool BeginJusticeCustodyRespawnTransferMask()
    {
        try
        {
            // Le fade immédiat empêche l'hôpital GTA d'apparaître pendant les
            // 250 ms de fondu normal du téléport sécurisé.
            Function.Call(Hash.DO_SCREEN_FADE_OUT, 0);
            return IsJusticeCustodyRespawnTransferMaskActive();
        }
        catch
        {
            return false;
        }
    }

    private static bool RestoreJusticeCustodyRespawnTransferMask()
    {
        try
        {
            Function.Call(Hash.DO_SCREEN_FADE_IN, 350);
            return IsJusticeCustodyRespawnTransferMaskRestoring();
        }
        catch
        {
            return false;
        }
    }

    private void ReassertJusticeCustodyRespawnTransferMask()
    {
        bool failureWasAlreadyKnown = _justiceCustodyRespawnMaskNeedsRearm;
        _justiceCustodyRespawnMaskNeedsRearm =
            !BeginJusticeCustodyRespawnTransferMask();
        if (_justiceCustodyRespawnMaskNeedsRearm && !failureWasAlreadyKnown)
        {
            LogWarning(
                "Justice.MasqueRespawn",
                "Fondu noir refusé par GTA; maintien des contrôles et nouvel essai armé.");
        }
    }

    private bool IsJusticeTemporaryPlayerProtectionForbidden()
    {
        return JusticeIsCustodyActive ||
               _justicePreJudgmentHoldingSource !=
                   JusticePreJudgmentHoldingSource.None ||
               _justicePoliceDeathRespawnMaskIntentPending ||
               _justiceCustodyRespawnTransferPending ||
               _justiceLegalReleaseFinalizationPending ||
               _justiceAmnestyPending ||
               _justiceActiveProfileResetPending;
    }

    private bool StopJusticeConcurrentPlayerProtectionModes()
    {
        if (HasPlacementSessionState())
        {
            // Je rends la caméra, le gel et l'owner Placement avant que Justice
            // vérifie la mortalité du détenu ou du suspect maintenu.
            StopPlacementMode(false);
        }
        if (HasTerminatorRuntimeState())
        {
            DisableTerminatorMode(false);
        }

        return !HasPlacementSessionState() &&
               !HasTerminatorRuntimeState();
    }

    private static bool IsJusticeCustodyRespawnTransferMaskActive()
    {
        try
        {
            return Function.Call<bool>(
                       (Hash)JusticeNativeIsScreenFadedOut) ||
                   Function.Call<bool>(
                       (Hash)JusticeNativeIsScreenFadingOut);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeCustodyRespawnTransferMaskRestoring()
    {
        try
        {
            return Function.Call<bool>(
                       (Hash)JusticeNativeIsScreenFadedIn) ||
                   Function.Call<bool>(
                       (Hash)JusticeNativeIsScreenFadingIn);
        }
        catch
        {
            return false;
        }
    }

    private void ArmJusticePoliceDeathRespawnMaskForAcceptedFront(
        int ownerSlot,
        int ownerModel)
    {
        if (!IsJusticeCanonicalProfileSlot(ownerSlot) || ownerModel == 0 ||
            ownerSlot != _justiceActivePlayerProfileSlot)
        {
            return;
        }

        Ped player;
        try
        {
            player = Game.Player.Character;
        }
        catch
        {
            player = null;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        if ((IsJusticeCanonicalProfileSlot(currentSlot) &&
             currentSlot != ownerSlot) ||
            (currentModel != 0 &&
             !JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
                 currentSlot,
                 currentModel,
                 ownerSlot,
                 ownerModel)))
        {
            // Je conserve le front sur son profil propriétaire sans masquer un
            // véritable changement de protagoniste déjà visible.
            return;
        }

        _justicePursuitDeathObservedDuringSuspension = true;
        _justiceSuspendedPursuitDeathPlayerSlot = ownerSlot;
        _justiceSuspendedPursuitDeathPlayerModelHash = ownerModel;
        _justicePoliceDeathRespawnMaskIntentPending = true;
        SetJusticePreJudgmentHoldingIntent(
            JusticePreJudgmentHoldingSource.DurablePoliceDeath,
            ownerSlot,
            ownerModel);

        bool playerAlive = Entity.Exists(player) && !player.IsDead;
        bool holdingOwnerCompatible = playerAlive &&
            IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(player);
        bool insideHolding = playerAlive &&
            IsInsideJusticePoliceDeathPreJudgmentHolding(player.Position);
        if (ShouldKeepJusticePreJudgmentHoldingVisible(
                _justicePoliceDeathPreJudgmentHoldingEstablished,
                _justicePreJudgmentHoldingStreamingPending,
                playerAlive,
                holdingOwnerCompatible,
                insideHolding))
        {
            // Je conserve le front durable sans remasquer un suspect dont le
            // maintien est déjà entièrement vérifié et jouable dans l'enceinte.
            // Je ne touche ici à aucun latch : cette branche est sans effet.
            return;
        }

        if (_justiceCustodyRespawnRestorePending)
        {
            // Je laisse le contrôleur retenter le même FADE_IN refusé. Alterner
            // avec un nouveau FADE_OUT créerait précisément le clignotement évité.
            return;
        }

        if (_justiceCustodyRespawnTransferPending &&
            !_justiceCustodyRespawnMaskNeedsRearm)
        {
            // Le contrôleur de masque vérifie déjà son état réel à chaque tick.
            // Je ne renvoie donc pas la même native depuis chaque replay WAL.
            return;
        }

        // Je pose et tente le masque même sur le ped mort. Le latch reste armé
        // si GTA refuse la native et sera réaffirmé dès le premier ped vivant.
        _justiceCustodyRespawnTransferPending = true;
        _justiceCustodyRespawnRestorePending = false;
        ReassertJusticeCustodyRespawnTransferMask();
    }

    private bool TryRestoreJusticeCustodyRespawnTransferMask()
    {
        if (!RestoreJusticeCustodyRespawnTransferMask())
        {
            _justiceCustodyRespawnRestorePending = true;
            return false;
        }

        _justiceCustodyRespawnTransferPending = false;
        _justiceCustodyRespawnMaskNeedsRearm = false;
        _justiceCustodyRespawnRestorePending = false;
        return true;
    }

    private void CancelJusticePoliceDeathRespawnMaskIntentIfUnclaimed()
    {
        if (!_justicePoliceDeathRespawnMaskIntentPending ||
            _justiceCustodyWaitingForRespawn || JusticeIsCustodyActive)
        {
            return;
        }

        ResetJusticePoliceDeathPreJudgmentHoldingState();
        _justicePoliceDeathRespawnMaskIntentPending = false;
        if (_justiceCustodyRespawnTransferPending ||
            _justiceCustodyRespawnRestorePending)
        {
            TryRestoreJusticeCustodyRespawnTransferMask();
        }
    }

    private void RestoreJusticeCustodyRuntimeFromCase()
    {
        _justiceCustodyRuntimeActive = true;
        _justiceCustodyResumePending = true;
        _justiceCustodyTransferPending = false;
        _justiceCustodyContainmentEstablished = false;
        ResetJusticeCustodyTransferRetryState();

        if (_justiceCustodySite == JusticeCustodySite.None)
        {
            _justiceCustodySite = GetJusticeCustodySiteForSentence(
                GetJusticeCustodyTotalRemainingSecondsForRuntime(
                    _justiceCaseState));
        }

        if (_justiceCustodyInitialSentenceSeconds <= 0)
        {
            _justiceCustodyInitialSentenceSeconds = Math.Max(
                1,
                GetJusticeCustodyTotalRemainingSecondsForRuntime(
                    _justiceCaseState));
        }
    }

    private static JusticeCustodySite GetJusticeCustodySiteForSentence(int sentenceSeconds)
    {
        return sentenceSeconds >= JusticeCustodyPrisonThresholdSeconds
            ? JusticeCustodySite.Bolingbroke
            : JusticeCustodySite.MissionRow;
    }

    internal static long GetJusticeCustodyTotalRemainingSeconds(
        JusticeCaseState caseState)
    {
        if (caseState == null)
        {
            return 0L;
        }

        long baseSentence = Math.Max(0, caseState.SentenceSeconds);
        long guardPenalty = Math.Max(0L, caseState.CustodyGuardPenaltySeconds);
        return guardPenalty > long.MaxValue - baseSentence
            ? long.MaxValue
            : guardPenalty + baseSentence;
    }

    private static int GetJusticeCustodyTotalRemainingSecondsForRuntime(
        JusticeCaseState caseState)
    {
        return (int)Math.Min(
            int.MaxValue,
            GetJusticeCustodyTotalRemainingSeconds(caseState));
    }

    internal static void ConsumeJusticeCustodySentenceSeconds(
        JusticeCaseState caseState,
        int elapsedSeconds)
    {
        if (caseState == null || elapsedSeconds <= 0)
        {
            return;
        }

        long remainingElapsed = elapsedSeconds;
        long normalizedPenalty = Math.Max(
            0L,
            caseState.CustodyGuardPenaltySeconds);
        long consumedPenalty = Math.Min(normalizedPenalty, remainingElapsed);
        caseState.CustodyGuardPenaltySeconds =
            normalizedPenalty - consumedPenalty;
        remainingElapsed -= consumedPenalty;
        if (remainingElapsed > 0L)
        {
            caseState.SentenceSeconds = Math.Max(
                0,
                caseState.SentenceSeconds - (int)Math.Min(
                    int.MaxValue,
                    remainingElapsed));
        }
    }

    private bool ScheduleJusticeBolingbrokeTransferIfRequired(int now)
    {
        if (_justiceCaseState == null ||
            _justiceCustodySite != JusticeCustodySite.MissionRow ||
            _justiceCaseState.Phase != JusticePhase.Incarcerated ||
            GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <
                JusticeCustodyPrisonThresholdSeconds ||
            _justiceCustodyTransferPending || _justiceCustodyResumePending)
        {
            return false;
        }
        if ((_justiceCustodyRespawnTransferPending ||
             _justiceCustodyRespawnRestorePending) &&
            !TryRestoreJusticeCustodyRespawnTransferMask())
        {
            return false;
        }

        JusticePhase previousPhase = _justiceCaseState.Phase;
        _justiceCustodySite = JusticeCustodySite.Bolingbroke;
        _justiceCustodyTransferPending = true;
        _justiceCustodyResumePending = false;
        _justiceCustodyRespawnTransferPending = false;
        _justiceCustodyRespawnMaskNeedsRearm = false;
        _justiceCustodyRespawnRestorePending = false;
        _justicePoliceDeathRespawnMaskIntentPending = false;
        _justiceCustodyPersistenceOutageHoldingEstablished = false;
        _justiceOutsideCustodySinceAt = 0;
        _justiceCustodyContainmentEstablished = false;
        _justiceCaseState.Phase = JusticePhase.Transporting;
        ResetJusticeCustodyTransferRetryState();
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceCustodySite = JusticeCustodySite.MissionRow;
            _justiceCustodyTransferPending = false;
            _justiceCaseState.Phase = previousPhase;
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
        _justiceCustodyTransferPrecommitConfirmed = false;
        _justiceCustodyFallbackPrecommitPending = false;
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

    private void HandleJusticeCustodyTransferFailure(Ped player, int now)
    {
        RegisterJusticeCustodyTransferFailure(now);

        // Je ne transforme jamais une panne technique en remise en liberté :
        // le dossier reste en transport et le retry borné continue jusqu'à un
        // transfert vérifié. Je rends seulement le ped mobile entre deux essais.
        EnsureJusticeCustodyPlayerMobility(player);
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
            _justiceCustodyTransferRollbackFinalizationRevision = 0L;
            _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
            _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
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
            _justiceCustodyTransferRollbackFinalizationRevision = 0L;
            _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
            _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
        }

        if (_justiceCustodyTransferRollbackFinalizationPending &&
            _justiceCaseState.Phase == JusticePhase.AtLarge &&
            string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
        {
            // Un ancien runtime a déjà consommé le rollback avant cette version.
            // Je ne peux plus réinventer l'épisode fermé : je retire seulement le
            // latch résiduel, sans nouvel effacement wanted ni nouveau message.
            _justiceCustodyTransferRollbackFinalizationPending = false;
            _justiceCustodyTransferRollbackPrecommitRedundant = false;
            _justiceCustodyTransferRollbackFinalizationRevision = 0L;
            _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
            _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
            return true;
        }

        bool migrationPrepared =
            _justiceCustodyTransferRollbackFinalizationPending &&
            !HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback) &&
            _justiceCaseState.Phase == JusticePhase.Transporting &&
            !string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId);
        if (!migrationPrepared)
        {
            if (!HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback) ||
                string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId))
            {
                return false;
            }

            // Je convertis le rollback historique en reprise de transfert. Aucun
            // inventaire n'est rendu et aucun effet monde n'est nécessaire pour
            // cette migration, qui reste donc sûre même pendant l'écran de mort.
            string rollbackId = JusticePolicy.CreateOperationId(
                JusticeOperationKind.TransferRollback,
                _justiceCaseState.CustodyEpisodeId);
            _justiceCaseState.CompletedOperationIds.Remove(rollbackId);
            _justiceCaseState.Phase = JusticePhase.Transporting;
            _justiceCaseState.HasWarrant = false;
            _justiceCustodyRuntimeActive = true;
            _justiceCustodyTransferPending = true;
            _justiceCustodyResumePending = true;
            _justiceCustodyTransferRollbackFinalizationPending = true;
            _justiceCustodyTransferRollbackPrecommitRedundant = false;
            _justiceCustodyTransferRollbackFinalizationRevision = 0L;
            _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
            _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
            ResetJusticeCustodyTransferRetryState();
            JusticeMarkStateDirty();
        }

        if (!EnsureJusticeCustodyTransferRollbackPrecommitRedundant())
        {
            return false;
        }
        if ((_justiceCustodyRespawnTransferPending ||
             _justiceCustodyRespawnRestorePending) &&
            !TryRestoreJusticeCustodyRespawnTransferMask())
        {
            return false;
        }

        if (!PersistJusticeCustodyTransferRollbackFinalization(now))
        {
            return false;
        }

        _justiceCustodyTransferRollbackFinalizationPending = false;
        _justiceCustodyTransferRollbackPrecommitRedundant = false;
        ShowStatus(
            "Justice : ancien transfert sécurisé, reprise vers le lieu de détention.",
            4200);
        LogWarning(
            "Justice.Transfert",
            "Rollback historique neutralisé; détention et inventaire conservés, transfert repris.");
        return true;
    }

    private bool PersistJusticeCustodyTransferRollbackFinalization(int now)
    {
        InitializeJusticePersistenceServices();
        JusticeRepository repository = _justiceRepository;
        if (repository == null || _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        if (_justiceCustodyTransferRollbackFinalizationRevision > 0L)
        {
            ObserveJusticeRepositoryFailure();
            JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
            if (diagnostics.DiskRevision >=
                _justiceCustodyTransferRollbackFinalizationRevision)
            {
                // Je n'acquitte le latch qu'après une seconde rotation durable :
                // le primaire et le backup ne peuvent alors plus ressusciter
                // l'ancien TransferRollback ni sa remise en liberté technique.
                FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
                _justiceCustodyTransferRollbackFinalizationRevision = 0L;
                _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
                _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
                return true;
            }

            bool queuedWriteFailed = diagnostics.WriteFailures >
                _justiceCustodyTransferRollbackFinalizationWriteFailures;
            if (!queuedWriteFailed ||
                !JusticeCustodyHasReached(
                    now,
                    _justiceNextCustodyTransferRollbackFinalizationRetryAt))
            {
                return false;
            }

            // Je remplace uniquement une révision rejetée par le writer. Une
            // écriture encore en vol reste sondée sans multiplier les snapshots.
            _justiceNextStateFlushAttemptAtMs = 0L;
        }
        else if (!JusticeCustodyHasReached(
                     now,
                     _justiceNextCustodyTransferRollbackFinalizationRetryAt))
        {
            return false;
        }

        long writeFailuresBeforeEnqueue =
            repository.GetDiagnostics().WriteFailures;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            _justiceNextCustodyTransferRollbackFinalizationRetryAt =
                JusticeCustodyFutureTime(
                    now,
                    JusticeCustodyDeathPersistenceRetryMs);
            return false;
        }

        _justiceCustodyTransferRollbackFinalizationRevision =
            _justiceLastQueuedPersistenceRevision;
        _justiceCustodyTransferRollbackFinalizationWriteFailures =
            writeFailuresBeforeEnqueue;
        _justiceNextCustodyTransferRollbackFinalizationRetryAt =
            JusticeCustodyFutureTime(
                now,
                JusticeCustodyDeathPersistenceRetryMs);
        if (_justiceCustodyTransferRollbackFinalizationRevision <= 0L)
        {
            RegisterJusticePersistenceFailure(
                "révision de finalisation du rollback de transfert absente après enqueue");
        }

        // Même si le writer termine immédiatement, je relis DiskRevision au tick
        // suivant avant de rendre la détention au contrôleur normal de transfert.
        return false;
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
            // Je donne donc à chaque règlement différé son propre
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
            StationPlanned = stationPlanned,
            FineInDisputeBefore = Math.Max(
                0L,
                _justiceCaseState.FineInDispute)
        };
        JusticeMarkStateDirty();

        // Je rends le snapshot Prepared durable avant de pouvoir armer le WAL qui
        // précède l'unique écriture cash.
        if (!EnsureJusticeFinancialPreparedSnapshot("FineDebit"))
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
            bool finalCommitPersisted = !intent.DebitAttempted &&
                intent.Resolution == JusticePaymentResolution.Rejected
                    ? PersistJusticeFinancialOutcomeWithoutEffect("FineDebit")
                    : JusticeFlushStateNow();
            if (!finalCommitPersisted)
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

        // Une intention non tentée ne peut même pas relire son plan cash tant que
        // le snapshot Prepared correspondant n'est pas confirmé sur disque.
        if (!intent.DebitAttempted &&
            !EnsureJusticeFinancialPreparedSnapshot("FineDebit"))
        {
            return false;
        }

        int finalSentence = intent.SentenceIfDebited;
        int cash = 0;
        bool resolvedWithoutCash = !intent.DebitAttempted &&
            intent.Resolution == JusticePaymentResolution.Rejected;
        bool cashRead = false;
        if (resolvedWithoutCash)
        {
            finalSentence = intent.SentenceIfConverted;
        }
        else if (intent.DebitAttempted &&
            intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
        {
            // Je fais confiance au résultat natif déjà durci plutôt qu'à une
            // variation de solde ultérieure qui pourrait créer un faux échec.
            finalSentence = intent.SentenceIfDebited;
            intent.Resolution = JusticePaymentResolution.Confirmed;
            resolvedWithoutCash = true;
        }
        else if (intent.DebitAttempted &&
                 intent.CashWriteResult == JusticeCashWriteResult.Rejected)
        {
            // Un rejet explicite n'est jamais assimilé à un paiement : toute
            // l'amende reste due et est convertie sans réémettre STAT_SET_INT.
            finalSentence = intent.SentenceIfConverted;
            intent.Resolution = JusticePaymentResolution.Rejected;
            resolvedWithoutCash = true;
        }
        else
        {
            cashRead = TryReadJusticeSinglePlayerCash(intent.Slot, out cash);
        }
        if (!resolvedWithoutCash && !intent.CashPlanPrepared)
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
                intent.Resolution = JusticePaymentResolution.Rejected;
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
                InvalidateJusticeFinancialPreparedSnapshot("FineDebit");
                long plannedDebit = Math.Min(intent.FineAmount, Math.Max(0, cash));
                intent.CashPlanPrepared = true;
                intent.PreparedAtUtcTicks = DateTime.UtcNow.Ticks;
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
                ResetJusticeFineCashReadRetry();
                EnsureJusticeFinancialPreparedSnapshot("FineDebit");
                return false;
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
            intent.Resolution = JusticePaymentResolution.Rejected;
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
            intent.Resolution = JusticePaymentResolution.Ambiguous;
            intent.AmbiguousAmount = intent.DebitAmount;
            finalSentence = intent.SentenceIfDebited;
            resolvedWithoutCash = true;
            ShowStatus(
                "Justice : débit impossible à relire, montant placé en litige.",
                5200);
            LogWarning(
                "Justice.Amende",
                "Réconciliation expirée sans lecture; paiement marqué ambigu (at-most-once)." );
            ResetJusticeFineCashReadRetry();
        }
        else if (!resolvedWithoutCash && intent.DebitAttempted && cash == intent.CashAfter)
        {
            // CashAfter ne prouve le débit qu'après le précommit Attempted. Avant
            // celui-ci, une variation externe identique doit encore être rebasée.
            intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
            intent.Resolution = JusticePaymentResolution.Confirmed;
            finalSentence = intent.SentenceIfDebited;
            resolvedWithoutCash = true;
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
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
                // rebaser tant qu'aucune frame WAL n'existe. Après Prepared, je
                // rejette au contraire ce plan immuable sans jamais appeler SET.
                if (HasJusticePreparedFinancialWal("FineDebit"))
                {
                    finalSentence = intent.SentenceIfConverted;
                    intent.Resolution = JusticePaymentResolution.Rejected;
                    resolvedWithoutCash = true;
                    JusticeMarkStateDirty();
                }
                else
                {
                    InvalidateJusticeFinancialPreparedSnapshot("FineDebit");
                    long plannedDebit = Math.Min(intent.FineAmount, Math.Max(0, cash));
                    intent.PreparedAtUtcTicks = DateTime.UtcNow.Ticks;
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
                    EnsureJusticeFinancialPreparedSnapshot("FineDebit");
                    return false;
                }
            }

            if (resolvedWithoutCash)
            {
                finalSentence = intent.SentenceIfConverted;
            }
            else if (intent.DebitAmount <= 0)
            {
                finalSentence = intent.SentenceIfDebited;
                intent.Resolution = JusticePaymentResolution.Rejected;
            }
            else
            {
                // Le WAL Attempted est le jeton at-most-once. Une reprise qui le
                // retrouve durable ne réémet jamais STAT_SET_INT.
                bool attemptWasAlreadyDurable;
                if (!TryArmJusticeFinancialAttempt(
                        "FineDebit",
                        out attemptWasAlreadyDurable))
                {
                    return false;
                }

                intent.DebitAttempted = true;
                intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks;
                intent.CashWriteResult = JusticeCashWriteResult.Unknown;
                intent.Resolution = JusticePaymentResolution.Attempted;
                JusticeMarkStateDirty();

                if (!attemptWasAlreadyDurable)
                {
                    intent.CashWriteResult = TryWriteJusticeSinglePlayerCash(
                        intent.Slot,
                        intent.CashAfter);
                }
                if (intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
                {
                    intent.Resolution = JusticePaymentResolution.Confirmed;
                }
                else if (intent.CashWriteResult == JusticeCashWriteResult.Rejected)
                {
                    intent.Resolution = JusticePaymentResolution.Rejected;
                }
                JusticeMarkStateDirty();
                if (!JusticeFlushStateNow())
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
                    intent.Resolution = JusticePaymentResolution.Confirmed;
                    finalSentence = intent.SentenceIfDebited;
                    resolvedWithoutCash = true;
                    JusticeMarkStateDirty();
                    if (!JusticeFlushStateNow())
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
            intent.Resolution = JusticePaymentResolution.Ambiguous;
            intent.AmbiguousAmount = intent.DebitAmount;
            finalSentence = intent.SentenceIfDebited;
            resolvedWithoutCash = true;
            ShowStatus(
                "Justice : solde ambigu, montant placé en litige sans nouveau débit.",
                5200);
            LogWarning(
                "Justice.Amende",
                "Réconciliation expirée sur solde ambigu; paiement marqué ambigu (at-most-once)." );
        }

        if (intent.Resolution == JusticePaymentResolution.Ambiguous &&
            intent.AmbiguousAmount > 0L)
        {
            intent.AmbiguousAmount = JusticePolicy.MoveFineToDispute(
                _justiceCaseState,
                intent.AmbiguousAmount);
        }
        _justiceCaseState.FineDue = 0L;
        _justiceCaseState.SentenceSeconds = Math.Max(
            0,
            Math.Min(JusticeCustodyMaximumSentenceSeconds, finalSentence));
        JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        JusticeMarkStateDirty();
        bool outcomePersisted = !intent.DebitAttempted &&
            intent.Resolution == JusticePaymentResolution.Rejected
                ? PersistJusticeFinancialOutcomeWithoutEffect("FineDebit")
                : JusticeFlushStateNow();
        if (!outcomePersisted)
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
        if (_justiceCaseState == null)
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
        int maximumSentence = stationPlanned
            ? JusticeCustodyPrisonThresholdSeconds
            : JusticeCustodyMaximumSentenceSeconds;
        int normalizedSentence = Math.Min(maximumSentence, Math.Max(0, currentSentence));
        if (unpaidFine <= 0L)
        {
            // Je borne aussi les anciennes valeurs chargées lorsqu'aucune amende
            // ne reste à convertir : aucune reprise ne peut dépasser dix minutes.
            return normalizedSentence;
        }

        long seconds = unpaidFine / JusticeCustodyFineDollarsPerSecond;
        if (unpaidFine % JusticeCustodyFineDollarsPerSecond != 0L)
        {
            seconds++;
        }
        seconds = RoundJusticeCustodySecondsUp(
            seconds,
            JusticePolicy.SentenceRoundingQuantumSeconds);
        seconds = Math.Max(10L, Math.Min(JusticeCustodyFineConversionMaximumSeconds, seconds));
        return JusticeCustodySaturatingAdd(
            normalizedSentence,
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
            if (modelHash == 0 || !EnsureJusticePlayerIsMortal(player))
            {
                return false;
            }
            // Je ne conserve jamais une protection héritée dans la détention.
            // Les anciennes sauvegardes restent lisibles, mais toute nouvelle
            // sortie doit garantir un joueur mortel.
            _justiceCustodyStoredInvincible = false;
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
        if (!EnsureJusticePlayerIsMortal(player))
        {
            return false;
        }
        if (trustedSlot >= 0)
        {
            _justiceCustodyPlayerSlot = trustedSlot;
        }
        return true;
    }

    private void UpdateJusticeCustodyRespawnTransferMask(Ped player)
    {
        if (_justiceCustodyRespawnRestorePending)
        {
            // Un refus transitoire de FADE_IN ne consomme jamais le latch. Je
            // retente avant toute décision d'identité ou nouveau FADE_OUT.
            TryRestoreJusticeCustodyRespawnTransferMask();
            return;
        }

        if (_justiceCustodyRespawnTransferPending)
        {
            if (HasJusticeCustodyRespawnChangedCanonicalPlayer(player))
            {
                // Je ne laisse jamais le noir d'un détenu suivre un autre héros
                // canonique. Le front reste sauvegardé sur son profil propriétaire.
                ResetJusticePoliceDeathPreJudgmentHoldingState();
                TryRestoreJusticeCustodyRespawnTransferMask();
                return;
            }
            _justiceCustodyRespawnMaskNeedsRearm =
                !IsJusticeCustodyRespawnTransferMaskActive();
            if (_justiceCustodyRespawnMaskNeedsRearm)
            {
                // Je vérifie le vrai état à chaque tick : GTA peut réouvrir son
                // écran pendant WASTED, une pause ou le premier tick de respawn.
                // Le vrai changement de héros a déjà été exclu juste au-dessus.
                ReassertJusticeCustodyRespawnTransferMask();
            }
            return;
        }

        if (!CanMaskJusticeCustodyRespawnOrigin(player))
        {
            return;
        }

        // Je pose d'abord le latch : même si la native de fondu est momentanément
        // indisponible, chaque tentative de téléport réaffirmera le masque.
        _justiceCustodyRespawnTransferPending = true;
        _justiceCustodyRespawnRestorePending = false;
        ReassertJusticeCustodyRespawnTransferMask();
    }

    private bool UpdateJusticePoliceDeathPreJudgmentHolding(Ped player, int now)
    {
        RefreshJusticePreJudgmentHoldingIntent(player);
        bool mustBlockLate = MustBlockJusticeLateForPreJudgmentHolding();
        if (IsJusticeTemporaryPlayerProtectionForbidden() &&
            !StopJusticeConcurrentPlayerProtectionModes())
        {
            EnforceJusticePreJudgmentHoldingControlLock(player);
            return mustBlockLate;
        }
        if (HasJusticePoliceDeathPreJudgmentHoldingChangedCanonicalPlayer(player))
        {
            // Je rends immédiatement l'écran au héros entrant sans consommer le
            // front durable de celui qui attend encore son jugement sur son profil.
            ResetJusticePoliceDeathPreJudgmentHoldingState();
            if (_justiceCustodyRespawnTransferPending ||
                _justiceCustodyRespawnRestorePending)
            {
                TryRestoreJusticeCustodyRespawnTransferMask();
            }
            return false;
        }

        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.None)
        {
            return false;
        }

        if (!IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(player))
        {
            // Je garde un ped custom ambigu masqué sans jamais le téléporter; le
            // contrôleur d'identité décidera ensuite reprise ou mandat.
            EnforceJusticePreJudgmentHoldingControlLock(player);
            return mustBlockLate;
        }

        JusticeCustodySite requiredSite =
            GetJusticePreJudgmentHoldingRequiredSite();
        _justicePoliceDeathPreJudgmentHoldingSite = requiredSite;
        JusticeCustodyLayout layout = GetJusticeCustodyLayoutForSite(requiredSite);
        if (layout == null)
        {
            EnforceJusticePreJudgmentHoldingControlLock(player);
            return mustBlockLate;
        }

        bool insideContainment = IsInsideJusticeCustodyLayout(
            layout,
            player.Position);
        if (!_justicePoliceDeathPreJudgmentHoldingEstablished ||
            !insideContainment ||
            _justicePreJudgmentHoldingStreamingPending)
        {
            EnforceJusticePreJudgmentHoldingControlLock(player);
        }

        if (_justicePreJudgmentHoldingStreamingPending)
        {
            if (!JusticeCustodyHasReached(
                    now,
                    _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt))
            {
                return mustBlockLate;
            }
            if (!TryMoveJusticePoliceDeathPreJudgmentHoldingPlayerWithFallback(
                    player,
                    layout.CellPosition,
                    layout.CellHeading,
                    now))
            {
                RegisterJusticePoliceDeathPreJudgmentHoldingFailure(now);
                return mustBlockLate;
            }
        }

        insideContainment = IsInsideJusticeCustodyLayout(
            layout,
            player.Position);
        if (insideContainment)
        {
            if (!CompleteJusticePreJudgmentHoldingStreamingProtection(player) ||
                !EnsureJusticePlayerIsMortal(player) ||
                !EnsureJusticePlayerMobilityCore(player))
            {
                RegisterJusticePoliceDeathPreJudgmentHoldingFailure(now);
                return mustBlockLate;
            }

            bool firstVerifiedHolding =
                !_justicePoliceDeathPreJudgmentHoldingEstablished;
            _justicePoliceDeathPreJudgmentHoldingEstablished = true;
            ResetJusticePoliceDeathPreJudgmentHoldingRetryState();
            if (_justiceCustodyRespawnTransferPending ||
                _justiceCustodyRespawnRestorePending)
            {
                TryRestoreJusticeCustodyRespawnTransferMask();
            }
            if (firstVerifiedHolding)
            {
                LogInfo(
                    "Justice.MaintienAvantJugement",
                    "Suspect maintenu dans l'enceinte avant jugement, sans mutation du dossier; source=" +
                    _justicePreJudgmentHoldingSource.ToString() + ", slot=" +
                    _justicePoliceDeathPreJudgmentHoldingOwnerSlot.ToString(
                        CultureInfo.InvariantCulture) + ", modèle=" +
                    _justicePoliceDeathPreJudgmentHoldingOwnerModelHash.ToString(
                        CultureInfo.InvariantCulture) + ".");
            }
            return mustBlockLate;
        }

        if (!JusticeCustodyHasReached(
                now,
                _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt))
        {
            return mustBlockLate;
        }

        // Je n'arme ici que le tout premier masque d'une source de maintien qui
        // n'aurait pas déjà été vue par le contrôleur de respawn. Dès que le latch
        // existe, ce contrôleur reste l'unique propriétaire des réarmements : une
        // sortie d'enceinte ne peut donc plus envoyer deux FADE_OUT dans le tick.
        if (!_justiceCustodyRespawnTransferPending &&
            !_justiceCustodyRespawnRestorePending)
        {
            _justiceCustodyRespawnTransferPending = true;
            ReassertJusticeCustodyRespawnTransferMask();
        }
        if (_justiceCustodyRespawnMaskNeedsRearm)
        {
            RegisterJusticePoliceDeathPreJudgmentHoldingFailure(now);
            return mustBlockLate;
        }

        bool moved = TryMoveJusticePoliceDeathPreJudgmentHoldingPlayerWithFallback(
            player,
            layout.CellPosition,
            layout.CellHeading,
            now);
        if (!moved ||
            !IsInsideJusticeCustodyLayout(layout, player.Position) ||
            !CompleteJusticePreJudgmentHoldingStreamingProtection(player) ||
            !EnsureJusticePlayerIsMortal(player) ||
            !EnsureJusticePlayerMobilityCore(player))
        {
            RegisterJusticePoliceDeathPreJudgmentHoldingFailure(now);
            return mustBlockLate;
        }

        bool firstHolding = !_justicePoliceDeathPreJudgmentHoldingEstablished;
        _justicePoliceDeathPreJudgmentHoldingEstablished = true;
        ResetJusticePoliceDeathPreJudgmentHoldingRetryState();
        // Je rends l'image seulement après les deux preuves : position dans
        // l'enceinte complète et ped effectivement mobile.
        TryRestoreJusticeCustodyRespawnTransferMask();
        if (firstHolding)
        {
            ShowStatus(
                requiredSite == JusticeCustodySite.Bolingbroke
                    ? "Justice : maintien provisoire à Bolingbroke avant jugement."
                    : "Justice : maintien provisoire à Mission Row avant jugement.",
                4200);
            LogInfo(
                "Justice.MaintienAvantJugement",
                "Transfert provisoire vérifié avant jugement, sans effet métier; source=" +
                _justicePreJudgmentHoldingSource.ToString() + ", slot=" +
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot.ToString(
                    CultureInfo.InvariantCulture) + ", modèle=" +
                _justicePoliceDeathPreJudgmentHoldingOwnerModelHash.ToString(
                    CultureInfo.InvariantCulture) + ".");
        }
        return mustBlockLate;
    }

    private void RefreshJusticePreJudgmentHoldingIntent(Ped player)
    {
        int ownerSlot;
        int ownerModel;
        if (TryResolveJusticeCapturedPreTransferHoldingIntent(
                out ownerSlot,
                out ownerModel))
        {
            ClearJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel);
            SetJusticePreJudgmentHoldingIntent(
                JusticePreJudgmentHoldingSource.Captured,
                ownerSlot,
                ownerModel);
            return;
        }

        JusticePreJudgmentHoldingSource repairSource;
        if (TryResolveJusticeStoredRepairHoldingIntent(
                player,
                out repairSource,
                out ownerSlot,
                out ownerModel))
        {
            // Je résous d'abord le propriétaire physique courant : un héros Q
            // prouvé ne doit jamais hériter du DeathFront durable du profil P
            // encore actif pendant la réparation de sauvegarde.
            SetJusticePreJudgmentHoldingIntent(
                repairSource,
                ownerSlot,
                ownerModel);
            return;
        }

        if (HasJusticeActivePoliceDeathPreJudgmentIntent())
        {
            SetJusticePreJudgmentHoldingIntent(
                JusticePreJudgmentHoldingSource.DurablePoliceDeath,
                _justiceSuspendedPursuitDeathPlayerSlot,
                _justiceSuspendedPursuitDeathPlayerModelHash);
            return;
        }

        if (TryResolveJusticePendingWalPoliceDeathHoldingIntent(
                player,
                out ownerSlot,
                out ownerModel))
        {
            bool preserveRepairSource = _justicePreJudgmentHoldingSource ==
                    JusticePreJudgmentHoldingSource.RepairPoliceDeath ||
                _justicePreJudgmentHoldingSource ==
                    JusticePreJudgmentHoldingSource.RepairPoliceArrest;
            if (preserveRepairSource &&
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot == ownerSlot &&
                _justicePoliceDeathPreJudgmentHoldingOwnerModelHash == ownerModel)
            {
                // Je garde la source Repair tant que le branchement de profil
                // n'est pas achevé; elle seule autorise légitimement Q avec P actif.
                return;
            }
            SetJusticePreJudgmentHoldingIntent(
                JusticePreJudgmentHoldingSource.PendingWalPoliceDeath,
                ownerSlot,
                ownerModel);
            return;
        }

        if (TryResolveJusticePendingWalCustodyRebindHoldingIntent(
                player,
                out ownerSlot,
                out ownerModel))
        {
            SetJusticePreJudgmentHoldingIntent(
                JusticePreJudgmentHoldingSource.PendingWalCustodyRebind,
                ownerSlot,
                ownerModel);
            return;
        }

        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.Captured &&
            JusticeIsCustodyActive)
        {
            // Je conserve l'enceinte provisoire pendant Transporting et ses
            // retries. Seul le transfert normal physiquement vérifié la consomme.
            return;
        }

        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceArrest)
        {
            if (ShouldCancelJusticeRepairArrestPreJudgmentHolding())
            {
                ClearJusticeRepairArrestPreJudgmentHoldingIntent(
                    _justicePoliceDeathPreJudgmentHoldingOwnerSlot,
                    _justicePoliceDeathPreJudgmentHoldingOwnerModelHash);
                ResetJusticePoliceDeathPreJudgmentHoldingState();
            }
            return;
        }

        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceDeath ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalPoliceDeath ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalCustodyRebind)
        {
            // Je garde ces preuves runtime jusqu'à leur relais durable ou une
            // frontière explicite de reset/switch. Une panne WAL ne devient pas
            // une remise en liberté technique.
            return;
        }

        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.DurablePoliceDeath)
        {
            // ClearPendingJusticeDeathCapture et les resets de cycle appellent
            // déjà le reset explicite. Ce filet évite seulement un intent orphelin.
            ResetJusticePoliceDeathPreJudgmentHoldingState();
        }
    }

    private bool MustBlockJusticeLateForPreJudgmentHolding()
    {
        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.None)
        {
            return false;
        }
        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalCustodyRebind)
        {
            JusticeWalRecord pending = _justicePendingDeathFrontWalRecord;
            bool custodyRebindWalPending = pending != null &&
                IsJusticeDeathFrontWalRecordExact(pending) &&
                pending.ProfileSlot ==
                    _justicePoliceDeathPreJudgmentHoldingOwnerSlot &&
                string.Equals(
                    ReadWalString(pending, "mode", string.Empty),
                    JusticeCustodyDeathFrontMode,
                    StringComparison.Ordinal);
            // Je bloque tant que le Prepared exact existe ou si son application
            // n'a pas posé les deux latches. Une fois ceux-ci armés, le contrôleur
            // de détention reprend, toujours après la preuve physique du holding.
            return custodyRebindWalPending ||
                   !_justiceCustodyWaitingForRespawn ||
                   !_justiceCustodyDeathRebindPending;
        }
        if (_justicePreJudgmentHoldingSource !=
                JusticePreJudgmentHoldingSource.Captured)
        {
            return true;
        }

        // Je laisse le contrôleur normal reprendre dès que son précommit exact
        // est confirmé, mais le holding physique reste actif jusqu'au transfert.
        return IsJusticeCapturedAwaitingPrecommit() &&
               !IsJusticeCapturePrecommitConfirmedForCurrentEpisode();
    }

    private bool HasJusticeActivePoliceDeathPreJudgmentIntent()
    {
        bool exactPersistentIntent =
            _justicePursuitDeathObservedDuringSuspension &&
            IsJusticeCanonicalProfileSlot(
                _justiceSuspendedPursuitDeathPlayerSlot) &&
            _justiceSuspendedPursuitDeathPlayerSlot ==
                _justiceActivePlayerProfileSlot &&
            _justiceSuspendedPursuitDeathPlayerModelHash != 0;
        if (exactPersistentIntent)
        {
            // Le profil PendingDeathCapture reconstruit ces champs au reload. Je
            // réarme aussi le fondu sur le ped mort sans dépendre d'un pointeur
            // WAL en mémoire. Le helper ne rappelle jamais ce résolveur.
            if (!_justiceCustodyRespawnTransferPending ||
                _justiceCustodyRespawnMaskNeedsRearm)
            {
                ArmJusticePoliceDeathRespawnMaskForAcceptedFront(
                    _justiceSuspendedPursuitDeathPlayerSlot,
                    _justiceSuspendedPursuitDeathPlayerModelHash);
            }
            else
            {
                _justicePoliceDeathRespawnMaskIntentPending = true;
            }
        }
        return exactPersistentIntent;
    }

    private bool IsJusticeCapturedAwaitingPrecommit()
    {
        return _justiceCaseState != null &&
               _justiceCaseState.Phase == JusticePhase.Captured &&
               !string.IsNullOrWhiteSpace(
                   _justiceCaseState.CustodyEpisodeId) &&
               !_justiceCustodyRuntimeActive &&
               !_justiceCustodyTransferPending &&
               !_justiceCustodyResumePending;
    }

    private bool TryResolveJusticeCapturedPreTransferHoldingIntent(
        out int ownerSlot,
        out int ownerModel)
    {
        ownerSlot = _justiceCustodyPlayerSlot;
        ownerModel = _justiceCustodyPlayerModelHash;
        return IsJusticeCapturedAwaitingPrecommit() &&
            IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) &&
            _justiceCustodyPlayerSlot == _justiceActivePlayerProfileSlot &&
            _justiceCustodyPlayerModelHash != 0;
    }

    private bool TryResolveJusticePendingWalPoliceDeathHoldingIntent(
        Ped player,
        out int ownerSlot,
        out int ownerModel)
    {
        ownerSlot = -1;
        ownerModel = 0;
        JusticeWalRecord record = _justicePendingDeathFrontWalRecord;
        if (record == null || !IsJusticeDeathFrontWalRecordExact(record) ||
            !string.Equals(
                ReadWalString(record, "mode", string.Empty),
                JusticePoliceDeathFrontMode,
                StringComparison.Ordinal))
        {
            return false;
        }

        int recordSlot = ReadWalInt(record, "playerSlot", -1);
        int recordModel = ReadWalInt(record, "playerModel", 0);
        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        bool currentIdentityCompatible =
            JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
                currentSlot,
                currentModel,
                record.ProfileSlot,
                recordModel);
        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            record.ProfileSlot);
        if (!IsJusticeCanonicalProfileSlot(record.ProfileSlot) ||
            recordSlot != record.ProfileSlot || recordModel == 0 ||
            ownerCase == null || !ownerCase.Enabled ||
            !Entity.Exists(player) ||
            !currentIdentityCompatible)
        {
            return false;
        }

        // Je ne qualifie jamais ce record de durable : il reste seulement une
        // preuve Prepared/Attempted exacte que le resume WAL devra conserver.
        ownerSlot = record.ProfileSlot;
        ownerModel = recordModel;
        return true;
    }

    private bool TryResolveJusticePendingWalCustodyRebindHoldingIntent(
        Ped player,
        out int ownerSlot,
        out int ownerModel)
    {
        ownerSlot = -1;
        ownerModel = 0;
        JusticeWalRecord record = _justicePendingDeathFrontWalRecord;
        if (record == null || !IsJusticeDeathFrontWalRecordExact(record) ||
            !string.Equals(
                ReadWalString(record, "mode", string.Empty),
                JusticeCustodyDeathFrontMode,
                StringComparison.Ordinal))
        {
            return false;
        }

        int recordSlot = ReadWalInt(record, "playerSlot", -1);
        int recordModel = ReadWalInt(record, "playerModel", 0);
        int recordSite = ReadWalInt(record, "custodySite", -1);
        string recordEpisode = ReadWalString(
            record,
            "episodeId",
            string.Empty);
        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            record.ProfileSlot);
        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        bool canonicalRespawnIdentity =
            currentSlot == record.ProfileSlot && currentModel != 0;
        bool customIdentityExact = currentSlot == -1 &&
            currentModel != 0 && currentModel == recordModel;
        bool validSite = recordSite == (int)JusticeCustodySite.MissionRow ||
            recordSite == (int)JusticeCustodySite.Bolingbroke;
        if (!IsJusticeCanonicalProfileSlot(record.ProfileSlot) ||
            record.ProfileSlot != _justiceActivePlayerProfileSlot ||
            recordSlot != record.ProfileSlot ||
            recordSlot != _justiceCustodyPlayerSlot || recordModel == 0 ||
            recordModel != _justiceCustodyPlayerModelHash || !validSite ||
            recordSite != (int)_justiceCustodySite ||
            ownerCase == null || !ownerCase.Enabled ||
            !IsJusticeCustodyPhase(ownerCase.Phase) ||
            !string.Equals(
                ownerCase.CustodyEpisodeId,
                recordEpisode,
                StringComparison.Ordinal) ||
            !Entity.Exists(player) || player.IsDead ||
            (!canonicalRespawnIdentity && !customIdentityExact))
        {
            return false;
        }

        // Un slot canonique constitue la preuve du protagoniste malgré le modèle
        // de respawn choisi par GTA. Un ped custom exige toujours le modèle exact.
        ownerSlot = record.ProfileSlot;
        ownerModel = recordModel;
        return true;
    }

    private void ArmJusticeRepairPreJudgmentHoldingIntent(
        Ped player,
        int ownerSlot,
        int ownerModel,
        JusticeDeferredRuntimeFront observed,
        bool hadPursuit)
    {
        JusticeDeferredRuntimeFront storedFronts;
        bool storedHadPursuit;
        if (TryGetJusticeDeferredRuntimeFrontLot(
                ownerSlot,
                ownerModel,
                out storedFronts,
                out storedHadPursuit))
        {
            // Je relis le lot après son stockage : ArrestEnded hérite ainsi de
            // la preuve de poursuite portée par son ArrestStarted précédent.
            observed = storedFronts;
            hadPursuit = storedHadPursuit;
        }

        bool deathEnded =
            (observed & JusticeDeferredRuntimeFront.DeathStarted) != 0;
        bool arrestEnded =
            (observed & JusticeDeferredRuntimeFront.ArrestEnded) != 0;
        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            ownerSlot);
        if (!hadPursuit || (!deathEnded && !arrestEnded) ||
            !IsJusticeCanonicalProfileSlot(ownerSlot) || ownerModel == 0 ||
            ownerCase == null || !ownerCase.Enabled ||
            !Entity.Exists(player) || player.IsDead)
        {
            // ArrestStarted reste révocable : je ne confine qu'après son front
            // descendant BUSTED, ou dès une mort policière exacte.
            return;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        bool exactCurrentIdentity =
            (currentSlot == ownerSlot || currentSlot == -1) &&
            currentModel == ownerModel;
        if (!exactCurrentIdentity)
        {
            return;
        }

        bool arrestIntentRemembered = !arrestEnded ||
            RememberJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel);
        if (!deathEnded && !arrestIntentRemembered)
        {
            return;
        }

        JusticePreJudgmentHoldingSource source = deathEnded
            ? JusticePreJudgmentHoldingSource.RepairPoliceDeath
            : JusticePreJudgmentHoldingSource.RepairPoliceArrest;
        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceDeath &&
            _justicePoliceDeathPreJudgmentHoldingOwnerSlot == ownerSlot &&
            _justicePoliceDeathPreJudgmentHoldingOwnerModelHash == ownerModel)
        {
            source = JusticePreJudgmentHoldingSource.RepairPoliceDeath;
        }
        SetJusticePreJudgmentHoldingIntent(source, ownerSlot, ownerModel);
    }

    private bool TryResolveJusticeStoredRepairHoldingIntent(
        Ped player,
        out JusticePreJudgmentHoldingSource source,
        out int ownerSlot,
        out int ownerModel)
    {
        source = JusticePreJudgmentHoldingSource.None;
        ownerSlot = -1;
        ownerModel = 0;
        if (!Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        if (currentModel == 0 ||
            (currentSlot != -1 &&
             !IsJusticeCanonicalProfileSlot(currentSlot)))
        {
            return false;
        }

        int firstSlot = IsJusticeCanonicalProfileSlot(currentSlot)
            ? currentSlot
            : 0;
        int lastSlot = IsJusticeCanonicalProfileSlot(currentSlot)
            ? currentSlot
            : JusticePlayerProfileCount - 1;
        int matchCount = 0;
        bool rememberArrest = false;
        for (int slot = firstSlot; slot <= lastSlot; slot++)
        {
            JusticePreJudgmentHoldingSource candidateSource;
            bool candidateRememberArrest;
            if (!TryResolveJusticeStoredRepairHoldingCandidate(
                    slot,
                    currentModel,
                    out candidateSource,
                    out candidateRememberArrest))
            {
                continue;
            }

            matchCount++;
            source = candidateSource;
            ownerSlot = slot;
            ownerModel = currentModel;
            rememberArrest = candidateRememberArrest;
            if (matchCount > 1)
            {
                // Je refuse un modèle custom ambigu entre deux profils : aucun
                // lot ne peut alors revendiquer le ped sans slot cash canonique.
                source = JusticePreJudgmentHoldingSource.None;
                ownerSlot = -1;
                ownerModel = 0;
                return false;
            }
        }

        if (matchCount != 1)
        {
            return false;
        }
        if (rememberArrest &&
            !RememberJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel) &&
            source == JusticePreJudgmentHoldingSource.RepairPoliceArrest)
        {
            source = JusticePreJudgmentHoldingSource.None;
            ownerSlot = -1;
            ownerModel = 0;
            return false;
        }
        return true;
    }

    private bool TryResolveJusticeStoredRepairHoldingCandidate(
        int ownerSlot,
        int ownerModel,
        out JusticePreJudgmentHoldingSource source,
        out bool rememberArrest)
    {
        source = JusticePreJudgmentHoldingSource.None;
        rememberArrest = false;
        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            ownerSlot);
        if (ownerCase == null || !ownerCase.Enabled)
        {
            ClearJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel);
            return false;
        }

        JusticeDeferredRuntimeFront fronts;
        bool hadPursuit;
        bool hasExactLot = TryGetJusticeDeferredRuntimeFrontLot(
            ownerSlot,
            ownerModel,
            out fronts,
            out hadPursuit);
        bool deathObserved = hasExactLot && hadPursuit &&
            (fronts & JusticeDeferredRuntimeFront.DeathStarted) != 0;
        bool arrestEnded = hasExactLot && hadPursuit &&
            (fronts & JusticeDeferredRuntimeFront.ArrestEnded) != 0;
        if (deathObserved)
        {
            source = JusticePreJudgmentHoldingSource.RepairPoliceDeath;
            rememberArrest = arrestEnded;
            return true;
        }

        bool rememberedArrest =
            HasJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel);
        if (!arrestEnded && !rememberedArrest)
        {
            return false;
        }
        if (rememberedArrest &&
            ShouldCancelJusticeRepairArrestPreJudgmentHolding(
                ownerSlot,
                ownerModel))
        {
            ClearJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel);
            return false;
        }

        source = JusticePreJudgmentHoldingSource.RepairPoliceArrest;
        rememberArrest = arrestEnded;
        return true;
    }

    private bool RememberJusticeRepairArrestPreJudgmentHoldingIntent(
        int ownerSlot,
        int ownerModel)
    {
        if (!IsJusticeCanonicalProfileSlot(ownerSlot) || ownerModel == 0)
        {
            return false;
        }
        if (_justiceRepairArrestPreJudgmentHoldingModelHashes == null ||
            _justiceRepairArrestPreJudgmentHoldingModelHashes.Length !=
                JusticePlayerProfileCount)
        {
            _justiceRepairArrestPreJudgmentHoldingModelHashes =
                new int[JusticePlayerProfileCount];
        }

        int storedModel =
            _justiceRepairArrestPreJudgmentHoldingModelHashes[ownerSlot];
        if (storedModel != 0 && storedModel != ownerModel)
        {
            return false;
        }
        _justiceRepairArrestPreJudgmentHoldingModelHashes[ownerSlot] =
            ownerModel;
        return true;
    }

    private bool HasJusticeRepairArrestPreJudgmentHoldingIntent(
        int ownerSlot,
        int ownerModel)
    {
        return IsJusticeCanonicalProfileSlot(ownerSlot) && ownerModel != 0 &&
               _justiceRepairArrestPreJudgmentHoldingModelHashes != null &&
               _justiceRepairArrestPreJudgmentHoldingModelHashes.Length ==
                   JusticePlayerProfileCount &&
               _justiceRepairArrestPreJudgmentHoldingModelHashes[ownerSlot] ==
                   ownerModel;
    }

    private void ClearJusticeRepairArrestPreJudgmentHoldingIntent(
        int ownerSlot,
        int ownerModel)
    {
        if (!HasJusticeRepairArrestPreJudgmentHoldingIntent(
                ownerSlot,
                ownerModel))
        {
            return;
        }
        _justiceRepairArrestPreJudgmentHoldingModelHashes[ownerSlot] = 0;
    }

    private void ClearAllJusticeRepairArrestPreJudgmentHoldingIntents()
    {
        if (_justiceRepairArrestPreJudgmentHoldingModelHashes == null)
        {
            return;
        }
        Array.Clear(
            _justiceRepairArrestPreJudgmentHoldingModelHashes,
            0,
            _justiceRepairArrestPreJudgmentHoldingModelHashes.Length);
    }

    private void SetJusticePreJudgmentHoldingIntent(
        JusticePreJudgmentHoldingSource source,
        int ownerSlot,
        int ownerModel)
    {
        if (source == JusticePreJudgmentHoldingSource.None ||
            !IsJusticeCanonicalProfileSlot(ownerSlot) || ownerModel == 0)
        {
            return;
        }

        bool firstIntent = _justicePreJudgmentHoldingSource ==
            JusticePreJudgmentHoldingSource.None;
        bool ownerChanged =
            _justicePreJudgmentHoldingSource !=
                JusticePreJudgmentHoldingSource.None &&
            (_justicePoliceDeathPreJudgmentHoldingOwnerSlot != ownerSlot ||
             _justicePoliceDeathPreJudgmentHoldingOwnerModelHash != ownerModel);
        if (ownerChanged)
        {
            // Je ne transporte jamais une position ni un backoff physique vers
            // un autre couple slot/modèle, même au milieu d'une réparation.
            ResetJusticePreJudgmentHoldingStreamingState(null);
            _justicePoliceDeathPreJudgmentHoldingEstablished = false;
            _justicePoliceDeathPreJudgmentHoldingSite = JusticeCustodySite.None;
            ResetJusticePoliceDeathPreJudgmentHoldingRetryState();
        }

        _justicePreJudgmentHoldingSource = source;
        _justicePoliceDeathPreJudgmentHoldingOwnerSlot = ownerSlot;
        _justicePoliceDeathPreJudgmentHoldingOwnerModelHash = ownerModel;
        if (firstIntent || ownerChanged)
        {
            _justicePoliceDeathPreJudgmentHoldingStartedAt =
                GetJusticeRawGameTimeSafe();
            _justicePoliceDeathPreJudgmentHoldingFallbackLogged = false;
        }
    }

    private bool ShouldCancelJusticeRepairArrestPreJudgmentHolding()
    {
        return _justicePreJudgmentHoldingSource ==
                   JusticePreJudgmentHoldingSource.RepairPoliceArrest &&
               ShouldCancelJusticeRepairArrestPreJudgmentHolding(
                   _justicePoliceDeathPreJudgmentHoldingOwnerSlot,
                   _justicePoliceDeathPreJudgmentHoldingOwnerModelHash);
    }

    private bool ShouldCancelJusticeRepairArrestPreJudgmentHolding(
        int ownerSlot,
        int ownerModel)
    {
        if (ownerSlot != _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            ownerSlot);
        if (ownerCase == null || !ownerCase.Enabled)
        {
            return true;
        }
        if (_justiceBackupRepairPending ||
            _justiceArrestCompletionProbePending ||
            _justiceCaptureRetryPending || JusticeIsCustodyActive)
        {
            return false;
        }

        JusticeDeferredRuntimeFront fronts;
        bool hadPursuit;
        if (TryGetJusticeDeferredRuntimeFrontLot(
                ownerSlot,
                ownerModel,
                out fronts,
                out hadPursuit))
        {
            return false;
        }

        JusticeWalRecord pending = _justicePendingDeathFrontWalRecord;
        if (pending != null && IsJusticeDeathFrontWalRecordExact(pending) &&
            pending.ProfileSlot ==
                ownerSlot &&
            string.Equals(
                ReadWalString(pending, "mode", string.Empty),
                JusticePoliceArrestFrontMode,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Je reconnais ici la seule annulation métier de ce latch runtime : la
        // sonde BUSTED est terminée et le dossier est reparti sous mandat.
        return ownerCase.HasWarrant &&
               ownerCase.Phase == JusticePhase.AtLarge;
    }

    private JusticeCaseState GetJusticePreJudgmentHoldingOwnerCase(int ownerSlot)
    {
        if (!IsJusticeCanonicalProfileSlot(ownerSlot))
        {
            return null;
        }
        if (ownerSlot == _justiceActivePlayerProfileSlot)
        {
            return _justiceCaseState;
        }
        if (_justicePlayerProfiles == null ||
            ownerSlot >= _justicePlayerProfiles.Length ||
            _justicePlayerProfiles[ownerSlot] == null)
        {
            return null;
        }
        return _justicePlayerProfiles[ownerSlot].CaseState;
    }

    private int GetJusticePreJudgmentHoldingSentenceSeconds()
    {
        JusticeCaseState ownerCase = GetJusticePreJudgmentHoldingOwnerCase(
            _justicePoliceDeathPreJudgmentHoldingOwnerSlot);
        return GetJusticeCustodyTotalRemainingSecondsForRuntime(ownerCase);
    }

    private JusticeCustodySite GetJusticePreJudgmentHoldingRequiredSite()
    {
        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalCustodyRebind)
        {
            JusticeWalRecord record = _justicePendingDeathFrontWalRecord;
            int recordSite = record != null &&
                IsJusticeDeathFrontWalRecordExact(record) &&
                string.Equals(
                    ReadWalString(record, "mode", string.Empty),
                    JusticeCustodyDeathFrontMode,
                    StringComparison.Ordinal)
                    ? ReadWalInt(record, "custodySite", -1)
                    : -1;
            if (recordSite == (int)JusticeCustodySite.MissionRow ||
                recordSite == (int)JusticeCustodySite.Bolingbroke)
            {
                // Le site du front est figé au décès : une peine tombée sous le
                // seuil pendant sa purge ne doit pas déplacer le détenu ailleurs.
                return (JusticeCustodySite)recordSite;
            }
            if (_justicePoliceDeathPreJudgmentHoldingSite !=
                    JusticeCustodySite.None)
            {
                return _justicePoliceDeathPreJudgmentHoldingSite;
            }
            if (_justiceCustodySite != JusticeCustodySite.None)
            {
                return _justiceCustodySite;
            }
        }

        return GetJusticeCustodySiteForSentence(
            GetJusticePreJudgmentHoldingSentenceSeconds());
    }

    private void ResetJusticeCapturePrecommitConfirmation()
    {
        _justiceCapturePrecommitConfirmed = false;
        _justiceCapturePrecommitConfirmedOwnerSlot = -1;
        _justiceCapturePrecommitConfirmedOwnerModelHash = 0;
        _justiceCapturePrecommitConfirmedEpisodeId = string.Empty;
    }

    private void ConfirmJusticeCapturePrecommit()
    {
        if (_justiceCaseState == null ||
            _justiceCaseState.Phase != JusticePhase.Captured ||
            string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId) ||
            !IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) ||
            _justiceCustodyPlayerSlot != _justiceActivePlayerProfileSlot ||
            _justiceCustodyPlayerModelHash == 0)
        {
            ResetJusticeCapturePrecommitConfirmation();
            return;
        }

        _justiceCapturePrecommitConfirmed = true;
        _justiceCapturePrecommitConfirmedOwnerSlot =
            _justiceCustodyPlayerSlot;
        _justiceCapturePrecommitConfirmedOwnerModelHash =
            _justiceCustodyPlayerModelHash;
        _justiceCapturePrecommitConfirmedEpisodeId =
            _justiceCaseState.CustodyEpisodeId;
    }

    private bool IsJusticeCapturePrecommitConfirmedForCurrentEpisode()
    {
        return _justiceCapturePrecommitConfirmed &&
               _justiceCaseState != null &&
               _justiceCaseState.Phase == JusticePhase.Captured &&
               _justiceCapturePrecommitConfirmedOwnerSlot ==
                   _justiceCustodyPlayerSlot &&
               _justiceCapturePrecommitConfirmedOwnerSlot ==
                   _justiceActivePlayerProfileSlot &&
               _justiceCapturePrecommitConfirmedOwnerModelHash != 0 &&
               _justiceCapturePrecommitConfirmedOwnerModelHash ==
                   _justiceCustodyPlayerModelHash &&
               string.Equals(
                   _justiceCapturePrecommitConfirmedEpisodeId,
                   _justiceCaseState.CustodyEpisodeId,
                   StringComparison.Ordinal);
    }

    private void ArmJusticeCapturePrecommitRetryIfRequired()
    {
        if (!IsJusticeCapturedAwaitingPrecommit() ||
            IsJusticeCapturePrecommitConfirmedForCurrentEpisode())
        {
            return;
        }

        // Je considère Captured chargé comme non confirmé tant que ce runtime
        // n'a pas repassé la barrière redondante pour l'épisode et son identité.
        ResetJusticeCapturePrecommitConfirmation();
        _justiceCaptureRetryPending = true;
        _justiceCaptureRetryDeath = _justiceCustodyWaitingForRespawn ||
            _justiceCustodyDeathRebindPending;
    }

    private bool IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            !IsJusticeCanonicalProfileSlot(
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot))
        {
            return false;
        }

        bool repairSource = _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceDeath ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceArrest;
        bool inactiveOwnerAllowed = repairSource ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalPoliceDeath;
        if (!inactiveOwnerAllowed &&
            _justicePoliceDeathPreJudgmentHoldingOwnerSlot !=
                _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        bool exactModel = currentModel != 0 &&
            _justicePoliceDeathPreJudgmentHoldingOwnerModelHash != 0 &&
            currentModel ==
                _justicePoliceDeathPreJudgmentHoldingOwnerModelHash;
        bool respawnIdentityCompatible =
            JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
                currentSlot,
                currentModel,
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot,
                _justicePoliceDeathPreJudgmentHoldingOwnerModelHash);
        if (repairSource)
        {
            return respawnIdentityCompatible;
        }
        if (_justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalCustodyRebind)
        {
            if (IsJusticeCanonicalProfileSlot(currentSlot))
            {
                return currentSlot ==
                    _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
            }
            return currentSlot == -1 && exactModel;
        }
        bool exactIdentitySource = _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalPoliceDeath ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.Captured;
        if (exactIdentitySource)
        {
            return respawnIdentityCompatible;
        }

        if (IsJusticeCanonicalProfileSlot(currentSlot))
        {
            // Je conserve exactement le gate DeathFront existant : un slot
            // canonique correspondant suffit malgré un modèle de respawn GTA.
            return currentSlot ==
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
        }

        // Un ped custom reste admissible uniquement avec son modèle exact.
        return currentSlot == -1 && exactModel;
    }

    private bool HasJusticePoliceDeathPreJudgmentHoldingChangedCanonicalPlayer(
        Ped player)
    {
        if (!IsJusticeCanonicalProfileSlot(
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot))
        {
            return false;
        }

        bool repairSource = _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceDeath ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.RepairPoliceArrest;
        bool inactiveOwnerAllowed = repairSource ||
            _justicePreJudgmentHoldingSource ==
                JusticePreJudgmentHoldingSource.PendingWalPoliceDeath;
        if (!inactiveOwnerAllowed &&
            IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) &&
            _justiceActivePlayerProfileSlot !=
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot)
        {
            return true;
        }
        if (!Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (repairSource)
        {
            // Pendant la réparation, activeProfile peut encore être P alors que
            // le front stocké appartient exactement au héros courant Q.
            if (currentSlot == -1)
            {
                return GetJusticePedModelHashSafe(player) !=
                    _justicePoliceDeathPreJudgmentHoldingOwnerModelHash;
            }
            return currentSlot !=
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
        }
        return IsJusticeCanonicalProfileSlot(currentSlot) &&
               currentSlot !=
                   _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
    }

    private bool IsInsideJusticePoliceDeathPreJudgmentHolding(Vector3 position)
    {
        if (!_justicePoliceDeathPreJudgmentHoldingEstablished)
        {
            return false;
        }

        JusticeCustodyLayout layout = GetJusticeCustodyLayoutForSite(
            _justicePoliceDeathPreJudgmentHoldingSite);
        return IsInsideJusticeCustodyLayout(layout, position);
    }

    private bool TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(
        Ped player,
        Vector3 targetPosition,
        float heading)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        Vector3 safeTarget = targetPosition + new Vector3(0.0f, 0.0f, 0.35f);
        if (!EnsureJusticePreJudgmentHoldingStreamingState(
                player,
                safeTarget,
                heading))
        {
            return false;
        }

        try
        {
            // Je n'appelle pas TeleportPlayerWithFadeSafe ici : son FadeIn interne
            // précède la preuve de mobilité exigée par ce holding fail-closed.
            _activeInteriorSession = null;
            ClearInteriorRenderingFocusSafe(player);
            Function.Call(
                Hash.REQUEST_COLLISION_AT_COORD,
                safeTarget.X,
                safeTarget.Y,
                safeTarget.Z);
            SetEntityLoadCollisionFlagSafe(player, true);
            if (!_justicePreJudgmentHoldingPositionApplied)
            {
                if (!IsJusticePreJudgmentHoldingGroundReady(safeTarget))
                {
                    // Je garde l'écran noir sans Wait : le tick suivant retente
                    // le streaming au lieu de placer le ped au-dessus du vide.
                    return false;
                }

                player.FreezePosition = true;
                SetEntityCoordsNoOffsetSafe(player, safeTarget);
                player.Heading = NormalizeHeading(heading);
                Function.Call(
                    Hash.SET_ENTITY_VELOCITY,
                    player.Handle,
                    0.0f,
                    0.0f,
                    0.0f);
                if (!IsJusticeTeleportVerified(player, targetPosition, 8.0f))
                {
                    // Je garde la propriété v2 comme fallback indépendant,
                    // toujours sous masque et protection temporaire.
                    player.Position = safeTarget;
                    player.Heading = NormalizeHeading(heading);
                }
                _justicePreJudgmentHoldingPositionApplied =
                    IsJusticeTeleportVerified(player, targetPosition, 8.0f);
                if (!_justicePreJudgmentHoldingPositionApplied)
                {
                    return false;
                }
            }

            Function.Call(
                Hash.REQUEST_COLLISION_AT_COORD,
                safeTarget.X,
                safeTarget.Y,
                safeTarget.Z);
            bool collisionReady = Function.Call<bool>(
                (Hash)JusticeNativeHasCollisionLoadedAroundEntity,
                player.Handle);
            return collisionReady &&
                   IsJusticeTeleportVerified(player, targetPosition, 8.0f);
        }
        catch (Exception ex)
        {
            LogException("Justice.MaintienAvantJugement", ex);
            return false;
        }
    }

    private bool TryMoveJusticePoliceDeathPreJudgmentHoldingPlayerWithFallback(
        Ped player,
        Vector3 targetPosition,
        float heading,
        int now)
    {
        if (TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(
                player,
                targetPosition,
                heading))
        {
            return true;
        }

        if (unchecked((uint)(now -
                _justicePoliceDeathPreJudgmentHoldingStartedAt)) <
            (uint)JusticeCustodyTransferTimeoutMs)
        {
            return false;
        }

        if (!_justicePoliceDeathPreJudgmentHoldingFallbackLogged)
        {
            _justicePoliceDeathPreJudgmentHoldingFallbackLogged = true;
            LogWarning(
                "Justice.MaintienAvantJugement",
                "Streaming non confirmé après 30 secondes : passage au téléport de secours sous masque.");
        }

        Vector3 safeTarget = targetPosition +
            new Vector3(0.0f, 0.0f, 0.35f);
        if (!EnsureJusticePreJudgmentHoldingStreamingState(
                player,
                safeTarget,
                heading) ||
            !TryJusticeEmergencyTeleport(
                player,
                targetPosition,
                heading,
                false))
        {
            return false;
        }

        _justicePreJudgmentHoldingPositionApplied =
            IsJusticeTeleportVerified(player, targetPosition, 8.0f);
        return _justicePreJudgmentHoldingPositionApplied &&
            TryMoveJusticePoliceDeathPreJudgmentHoldingPlayer(
                player,
                targetPosition,
                heading);
    }

    private void EnforceJusticePreJudgmentHoldingControlLock(Ped player)
    {
        if (!_justiceCustodyRespawnTransferPending &&
            !_justiceCustodyRespawnRestorePending &&
            !_justicePreJudgmentHoldingStreamingPending)
        {
            return;
        }

        try
        {
            Game.DisableAllControlsThisFrame(0);
            if (_justiceCustodyRespawnMaskNeedsRearm &&
                Entity.Exists(player) && !player.IsDead)
            {
                // Si le noir échoue, je bloque physiquement le respawn vanilla
                // jusqu'au déplacement vérifié au lieu d'exposer l'hôpital.
                player.FreezePosition = true;
            }
        }
        catch (Exception ex)
        {
            LogException("Justice.MaintienAvantJugementControle", ex);
        }
    }

    private bool EnsureJusticePreJudgmentHoldingStreamingState(
        Ped player,
        Vector3 safeTarget,
        float heading)
    {
        int playerHandle;
        int playerModel;
        try
        {
            playerHandle = player.Handle;
            playerModel = GetJusticePedModelHashSafe(player);
        }
        catch
        {
            return false;
        }

        bool sameStreamingIntent =
            _justicePreJudgmentHoldingStreamingPending &&
            _justicePreJudgmentHoldingStreamingPlayerHandle == playerHandle &&
            _justicePreJudgmentHoldingStreamingPlayerModelHash == playerModel &&
            _justicePreJudgmentHoldingStreamingOwnerSlot ==
                _justicePoliceDeathPreJudgmentHoldingOwnerSlot &&
            _justicePreJudgmentHoldingStreamingOwnerModelHash ==
                _justicePoliceDeathPreJudgmentHoldingOwnerModelHash &&
            _justicePreJudgmentHoldingStreamingTarget.DistanceTo(safeTarget) <=
                0.1f &&
            Math.Abs(
                _justicePreJudgmentHoldingStreamingHeading -
                NormalizeHeading(heading)) <= 0.1f;
        if (sameStreamingIntent)
        {
            return true;
        }
        if (_justicePreJudgmentHoldingStreamingPending)
        {
            ResetJusticePreJudgmentHoldingStreamingState(player);
        }

        bool baselineInvincibility;
        if (!TryAcquirePlayerInvincibility(
                player,
                PlayerInvincibilityOwner.JusticePreJudgmentHolding,
                out baselineInvincibility))
        {
            return false;
        }

        try
        {
            _justicePreJudgmentHoldingStoredCanRagdoll = player.CanRagdoll;
            _justicePreJudgmentHoldingCanRagdollCaptured = true;
            player.CanRagdoll = false;
        }
        catch
        {
            TryReleasePlayerInvincibility(
                player,
                PlayerInvincibilityOwner.JusticePreJudgmentHolding,
                baselineInvincibility,
                false);
            _justicePreJudgmentHoldingCanRagdollCaptured = false;
            return false;
        }

        _justicePreJudgmentHoldingStreamingPending = true;
        _justicePreJudgmentHoldingPositionApplied = false;
        _justicePreJudgmentHoldingProtectionOwned = true;
        _justicePreJudgmentHoldingStreamingPlayerHandle = playerHandle;
        _justicePreJudgmentHoldingStreamingPlayerModelHash = playerModel;
        _justicePreJudgmentHoldingStreamingOwnerSlot =
            _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
        _justicePreJudgmentHoldingStreamingOwnerModelHash =
            _justicePoliceDeathPreJudgmentHoldingOwnerModelHash;
        _justicePreJudgmentHoldingStreamingTarget = safeTarget;
        _justicePreJudgmentHoldingStreamingHeading = NormalizeHeading(heading);
        return true;
    }

    private bool IsJusticePreJudgmentHoldingGroundReady(Vector3 safeTarget)
    {
        try
        {
            using (OutputArgument groundZ = new OutputArgument())
            {
                // Je passe le lot explicitement pour rester lié à la surcharge
                // params InputArgument[] réellement validée par NIB v2, quel que
                // soit le nombre d'arguments de cette native.
                InputArgument[] arguments =
                {
                    safeTarget.X,
                    safeTarget.Y,
                    safeTarget.Z + 50.0f,
                    groundZ,
                    false,
                    false
                };
                return Function.Call<bool>(
                    (Hash)JusticeNativeGetGroundZFor3DCoord,
                    arguments);
            }
        }
        catch (Exception ex)
        {
            LogException("Justice.MaintienAvantJugementSol", ex);
            return false;
        }
    }

    private bool CompleteJusticePreJudgmentHoldingStreamingProtection(
        Ped player)
    {
        if (!_justicePreJudgmentHoldingStreamingPending)
        {
            return true;
        }
        if (!Entity.Exists(player) || player.IsDead ||
            player.Handle !=
                _justicePreJudgmentHoldingStreamingPlayerHandle ||
            GetJusticePedModelHashSafe(player) !=
                _justicePreJudgmentHoldingStreamingPlayerModelHash)
        {
            return false;
        }

        try
        {
            player.FreezePosition = false;
            if (player.FreezePosition)
            {
                return false;
            }
            if (_justicePreJudgmentHoldingCanRagdollCaptured)
            {
                player.CanRagdoll =
                    _justicePreJudgmentHoldingStoredCanRagdoll;
                if (player.CanRagdoll !=
                    _justicePreJudgmentHoldingStoredCanRagdoll)
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }

        if (_justicePreJudgmentHoldingProtectionOwned &&
            !ReleaseJusticePreJudgmentInvincibilityAsMortal(player))
        {
            return false;
        }
        ClearJusticePreJudgmentHoldingStreamingFields();
        return true;
    }

    private void ResetJusticePreJudgmentHoldingStreamingState(
        Ped preferredPlayer)
    {
        Ped player = preferredPlayer;
        if (object.ReferenceEquals(player, null))
        {
            try
            {
                player = Game.Player.Character;
            }
            catch
            {
                player = null;
            }
        }

        bool exactPlayer = Entity.Exists(player) &&
            player.Handle == _justicePreJudgmentHoldingStreamingPlayerHandle &&
            GetJusticePedModelHashSafe(player) ==
                _justicePreJudgmentHoldingStreamingPlayerModelHash;
        if (exactPlayer)
        {
            try
            {
                if (_justicePreJudgmentHoldingPositionApplied)
                {
                    player.FreezePosition = false;
                }
                if (_justicePreJudgmentHoldingCanRagdollCaptured)
                {
                    player.CanRagdoll =
                        _justicePreJudgmentHoldingStoredCanRagdoll;
                }
            }
            catch
            {
                // Je poursuis la libération partagée de l'invincibilité même si
                // GTA refuse ponctuellement une propriété du ped remplacé.
            }
        }
        if (_justicePreJudgmentHoldingProtectionOwned)
        {
            ReleaseJusticePreJudgmentInvincibilityAsMortal(
                exactPlayer ? player : null);
        }
        ClearJusticePreJudgmentHoldingStreamingFields();
    }

    private void ClearJusticePreJudgmentHoldingStreamingFields()
    {
        _justicePreJudgmentHoldingStreamingPending = false;
        _justicePreJudgmentHoldingPositionApplied = false;
        _justicePreJudgmentHoldingProtectionOwned = false;
        _justicePreJudgmentHoldingCanRagdollCaptured = false;
        _justicePreJudgmentHoldingStoredCanRagdoll = true;
        _justicePreJudgmentHoldingStreamingPlayerHandle = 0;
        _justicePreJudgmentHoldingStreamingPlayerModelHash = 0;
        _justicePreJudgmentHoldingStreamingOwnerSlot = -1;
        _justicePreJudgmentHoldingStreamingOwnerModelHash = 0;
        _justicePreJudgmentHoldingStreamingTarget = Vector3.Zero;
        _justicePreJudgmentHoldingStreamingHeading = 0.0f;
    }

    private void RegisterJusticePoliceDeathPreJudgmentHoldingFailure(int now)
    {
        _justicePoliceDeathPreJudgmentHoldingFailureCount = Math.Min(
            16,
            _justicePoliceDeathPreJudgmentHoldingFailureCount + 1);
        if (_justicePoliceDeathPreJudgmentHoldingFailureCount == 1)
        {
            LogWarning(
                "Justice.MaintienAvantJugement",
                "Streaming, sol ou collision non confirmé; maintien sous masque et retry cadencé.");
        }
        int exponent = Math.Min(
            3,
            Math.Max(
                0,
                _justicePoliceDeathPreJudgmentHoldingFailureCount - 1));
        int retryDelay = Math.Min(
            JusticeCustodyTransferMaximumRetryMs,
            JusticeCustodyTransferInitialRetryMs * (1 << exponent));
        _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt =
            JusticeCustodyFutureTime(now, retryDelay);
    }

    private void ResetJusticePoliceDeathPreJudgmentHoldingRetryState()
    {
        _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt = 0;
        _justicePoliceDeathPreJudgmentHoldingFailureCount = 0;
    }

    private void ResetJusticePoliceDeathPreJudgmentHoldingState()
    {
        ResetJusticePreJudgmentHoldingStreamingState(null);
        _justicePreJudgmentHoldingSource =
            JusticePreJudgmentHoldingSource.None;
        _justicePoliceDeathPreJudgmentHoldingEstablished = false;
        _justicePoliceDeathPreJudgmentHoldingSite = JusticeCustodySite.None;
        _justicePoliceDeathPreJudgmentHoldingOwnerSlot = -1;
        _justicePoliceDeathPreJudgmentHoldingOwnerModelHash = 0;
        _justicePoliceDeathPreJudgmentHoldingStartedAt = 0;
        _justicePoliceDeathPreJudgmentHoldingFallbackLogged = false;
        ResetJusticePoliceDeathPreJudgmentHoldingRetryState();
    }

    private bool CanMaskJusticeCustodyRespawnOrigin(Ped player)
    {
        bool playerAlive = Entity.Exists(player) && !player.IsDead;
        if (!playerAlive)
        {
            return false;
        }
        bool holdingOwnerCompatible =
            _justicePoliceDeathPreJudgmentHoldingEstablished &&
            IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(player);
        bool insideHolding =
            _justicePoliceDeathPreJudgmentHoldingEstablished &&
            IsInsideJusticePoliceDeathPreJudgmentHolding(player.Position);
        if (ShouldKeepJusticePreJudgmentHoldingVisible(
                _justicePoliceDeathPreJudgmentHoldingEstablished,
                _justicePreJudgmentHoldingStreamingPending,
                playerAlive,
                holdingOwnerCompatible,
                insideHolding))
        {
            // Je peux déjà avoir Captured/waiting durable tandis que le transfert
            // normal attend encore; le suspect reste visible dans toute l'enceinte
            // provisoire jusqu'à sa vérification physique.
            return false;
        }
        if (CanMaskJusticePoliceDeathRespawnOrigin(player))
        {
            return true;
        }
        if (!CanRebindJusticeCustodyIdentityAfterInitialRespawn())
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
            return false;
        }
        if (!JusticePolicy.CanRebindCustodyRespawnSlot(
                _justiceCustodyPlayerSlot,
                currentSlot,
                _justiceLastCanonicalPlayerSlot,
                _justiceActivePlayerProfileSlot,
                _justiceCustodyDeathRebindPending))
        {
            return false;
        }

        if (_justiceCustodyPersistenceOutageHoldingEstablished &&
            IsInsideJusticeCustody(player.Position))
        {
            // La corruption durable suspend la peine, mais ne doit pas remettre
            // l'écran au noir tant que le même détenu reste dans son enceinte.
            return false;
        }

        return GetJusticePedModelHashSafe(player) != 0;
    }

    internal static bool ShouldKeepJusticePreJudgmentHoldingVisible(
        bool holdingEstablished,
        bool streamingPending,
        bool playerAlive,
        bool ownerCompatible,
        bool insideHolding)
    {
        // Je garde cette décision sans accès GTA afin que l'armement du front et
        // le contrôleur de respawn partagent exactement le même invariant.
        return holdingEstablished && !streamingPending && playerAlive &&
               ownerCompatible && insideHolding;
    }

    private bool CanMaskJusticePoliceDeathRespawnOrigin(Ped player)
    {
        if (!_justicePoliceDeathRespawnMaskIntentPending ||
            !_justicePursuitDeathObservedDuringSuspension ||
            !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCanonicalProfileSlot(
                _justiceSuspendedPursuitDeathPlayerSlot) ||
            _justiceSuspendedPursuitDeathPlayerSlot !=
                _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModel = GetJusticePedModelHashSafe(player);
        if (!JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
                currentSlot,
                currentModel,
                _justiceSuspendedPursuitDeathPlayerSlot,
                _justiceSuspendedPursuitDeathPlayerModelHash))
        {
            return false;
        }

        // Je ne réarme le masque d'un suspect déjà visible dans toute l'enceinte
        // provisoire que s'il en sort réellement.
        return !IsInsideJusticePoliceDeathPreJudgmentHolding(player.Position);
    }

    private bool HasJusticeCustodyRespawnChangedCanonicalPlayer(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (!IsJusticeCanonicalProfileSlot(currentSlot))
        {
            // Un modèle custom peut rester le même détenu; sans slot canonique
            // contradictoire je garde donc le masque et la preuve propriétaire.
            return false;
        }

        int ownerSlot = _justicePreJudgmentHoldingSource !=
                            JusticePreJudgmentHoldingSource.None &&
                        IsJusticeCanonicalProfileSlot(
                            _justicePoliceDeathPreJudgmentHoldingOwnerSlot)
            ? _justicePoliceDeathPreJudgmentHoldingOwnerSlot
            : (IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot)
            ? _justiceCustodyPlayerSlot
            : (_justicePoliceDeathRespawnMaskIntentPending &&
               IsJusticeCanonicalProfileSlot(
                   _justiceSuspendedPursuitDeathPlayerSlot)
                ? _justiceSuspendedPursuitDeathPlayerSlot
                : (IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : _justiceLastCanonicalPlayerSlot)));
        return IsJusticeCanonicalProfileSlot(ownerSlot) &&
               currentSlot != ownerSlot;
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
        if (!EnsureJusticePlayerIsMortal(player))
        {
            _justiceCustodyPlayerHandle = previousHandle;
            _justiceCustodyPlayerModelHash = previousModelHash;
            _justiceCustodyPlayerSlot = previousSlot;
            _justiceCustodyDeathRebindPending = previousDeathRebindPending;
            return false;
        }
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

        _justiceCustodyGuardDeathCauseEvaluated = false;
        _justiceCustodyGuardDeathPenaltyPending = false;
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
            return !Entity.Exists(player) || EnsureJusticePlayerIsMortal(player);
        }
        if (!IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        bool restored = true;
        _justiceCustodyStoredInvincible = false;
        restored &= EnsureJusticePlayerIsMortal(player);
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

    private void ArmJusticeCustodyResidualMissionFlagBypass()
    {
        // Le flag mission de la séquence BUSTED peut apparaître juste après le
        // téléport validé. J'ouvre une courte fenêtre d'observation : dès qu'il
        // est vu, le bypass reste valable uniquement jusqu'à sa première chute.
        _justiceCustodyResidualMissionFlagBypassArmed = true;
        _justiceCustodyResidualMissionFlagObservationDeadlineMs =
            _justiceMonotonicTimeMs +
            JusticeCustodyResidualMissionFlagObservationWindowMs;
    }

    private void UpdateJusticeCustodyResidualMissionFlagBypass(
        bool runtimeSuspended)
    {
        if (!_justiceCustodyResidualMissionFlagBypassArmed)
        {
            return;
        }

        bool residualLatchWasObserved =
            _justiceCustodyResidualMissionFlagObservationDeadlineMs == 0L;
        bool observationWindowOpen =
            !residualLatchWasObserved &&
            _justiceMonotonicTimeMs <
                _justiceCustodyResidualMissionFlagObservationDeadlineMs;
        if (runtimeSuspended &&
            _justiceRuntimeSuspendedByMissionFlagOnlyCached &&
            (residualLatchWasObserved || observationWindowOpen))
        {
            // Zéro signifie : le latch résiduel a réellement été observé. Il peut
            // durer longtemps, mais il ne sera jamais réutilisé après être retombé.
            _justiceCustodyResidualMissionFlagObservationDeadlineMs = 0L;
            return;
        }

        bool observationWindowExpired =
            !residualLatchWasObserved && !observationWindowOpen;
        if (residualLatchWasObserved || observationWindowExpired)
        {
            // Avant la première observation, une courte cinématique BUSTED peut
            // coexister avec le flag mission : elle garde seulement la fenêtre
            // armée, sans aucun bypass. Après observation, la première chute ou
            // toute suspension forte ferme définitivement le droit d'ignorer.
            _justiceCustodyResidualMissionFlagBypassArmed = false;
            _justiceCustodyResidualMissionFlagObservationDeadlineMs = 0L;
        }
    }

    private bool IsJusticeCustodyRuntimeSuspended(Ped player)
    {
        return IsJusticeCustodyRuntimeSuspended(
            player,
            IsJusticeRuntimeSuspended(player));
    }

    private bool IsJusticeCustodyRuntimeSuspended(
        Ped player,
        bool runtimeSuspended)
    {
        if (!runtimeSuspended)
        {
            return false;
        }

        // Une erreur native, un chargement, une cinématique, une pause ou un vrai
        // changement de héros restent toujours fail-closed. Le seul assouplissement
        // admis est le latch mission BUSTED observé juste après un transfert réussi.
        return !_justiceRuntimeSuspendedByMissionFlagOnlyCached ||
               !CanIgnoreJusticeMissionFlagForCustody(player);
    }

    private bool CanIgnoreJusticeMissionFlagForCustody(Ped player)
    {
        if (!_justiceRuntimeSuspendedByMissionFlagOnlyCached ||
            !_justiceCustodyResidualMissionFlagBypassArmed ||
            _justiceCustodyResidualMissionFlagObservationDeadlineMs != 0L ||
            _justiceCaseState == null ||
            _justiceProfileContextBlocked ||
            _justiceProfileSelectionPending ||
            _justiceProfileSwitchPersistencePending ||
            !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) ||
            _justiceCustodyWaitingForRespawn ||
            _justiceCustodyDeathRebindPending ||
            _justiceCustodyDeathStatePersistencePending ||
            _justiceCustodyTransferRollbackFinalizationPending ||
            !Entity.Exists(player) || player.IsDead)
        {
            return false;
        }

        bool stableCustody =
            _justiceCustodyRuntimeActive &&
            !_justiceCustodyTransferPending &&
            !_justiceCustodyResumePending &&
            !_justiceCustodyRespawnTransferPending &&
            !_justiceCustodyRespawnRestorePending &&
            _justiceCustodyContainmentEstablished &&
            (_justiceCaseState.Phase == JusticePhase.Incarcerated ||
             _justiceCaseState.Phase == JusticePhase.Escaping);
        bool releaseFinalization =
            _justiceLegalReleaseFinalizationPending &&
            _justiceLegalReleaseFinalizationSite != JusticeCustodySite.None;
        if (!stableCustody && !releaseFinalization)
        {
            return false;
        }

        int currentSlot = GetJusticeCanonicalPlayerSlotSafe();
        int currentModelHash = GetJusticePedModelHashSafe(player);
        if (IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot))
        {
            return _justiceCustodyPlayerSlot == _justiceActivePlayerProfileSlot &&
                   JusticePolicy.IsCustodyLiveIdentityCompatible(
                       _justiceCustodyPlayerSlot,
                       currentSlot,
                       _justiceCustodyPlayerHandle,
                       player.Handle,
                       _justiceCustodyPlayerModelHash,
                       currentModelHash);
        }

        // La première étape d'une libération durable efface volontairement le
        // snapshot de détention avant le téléport extérieur. Si son second flush
        // doit être repris, un héros canonique reste prouvable par le profil actif.
        return releaseFinalization &&
               IsJusticeCanonicalProfileSlot(currentSlot) &&
               currentSlot == _justiceActivePlayerProfileSlot;
    }

    private bool JusticeCustodyCanMutateWorld(Ped player)
    {
        return Entity.Exists(player) &&
               !player.IsDead &&
               !IsJusticeCustodyRuntimeSuspended(player);
    }

    private bool EnsureJusticeCustodyPlayerMobility(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        if (!EnsureJusticePlayerIsMortal(player))
        {
            return false;
        }
        if (!EnsureJusticePlayerMobilityCore(player))
        {
            try
            {
                // Je retente localement après la preuve d'identité de détention :
                // une propriété GTA transitoirement illisible ne doit pas faire
                // avancer la peine avec un joueur encore gelé.
                player.FreezePosition = false;
                if (player.FreezePosition)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
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

    private static bool EnsureJusticePlayerMobilityCore(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead)
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

        return true;
    }

    private void ResetJusticeCustodyClock(int now)
    {
        // Je rebascule l'origine sur le tick courant sans récupérer le temps passé
        // dans le gate. En revanche je conserve les millisecondes de gameplay déjà
        // réellement observées avant la suspension. Les effacer à chaque micro-gate
        // pouvait empêcher indéfiniment d'atteindre une seconde complète.
        _justiceCustodyLastTickAt = now;
        if (_justiceCustodyElapsedRemainderMs < 0 ||
            _justiceCustodyElapsedRemainderMs >= 1000)
        {
            // Un état runtime impossible ne doit jamais produire un rattrapage.
            _justiceCustodyElapsedRemainderMs = 0;
        }
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
        if (_justiceCustodyGuardRetaliationActive)
        {
            // Je réaffirme seulement un plancher : un wanted 3 à 5 n'est jamais
            // diminué par la riposte interne des gardiens.
            SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum);
        }
        else if (GetJusticeWantedLevelSafe() > 0)
        {
            SuppressJusticeRecognitionWantedLoss(
                "maintien de la suppression policière en détention");
            ClearJusticeWantedLevelOnce();
        }
    }

    private void SetJusticeCustodyPoliceSuppression(bool suppress)
    {
        bool restorationWasTracked = _justicePoliceIgnoreApplied ||
            _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending;
        if (suppress && _justicePoliceIntegrationMode ==
                JusticePoliceIntegrationMode.Disabled)
        {
            if (restorationWasTracked)
            {
                SetJusticeCustodyPoliceSuppression(false);
            }
            return;
        }
        if (suppress && !CanJusticeMutateGlobalPoliceState())
        {
            // Une mission, une cinématique ou un changement de héros peut déjà
            // posséder ces flags globaux. Je rends d'abord notre dernière écriture.
            if (restorationWasTracked)
            {
                SetJusticeCustodyPoliceSuppression(false);
            }
            return;
        }
        if (suppress &&
            _justicePoliceIntegrationMode ==
                JusticePoliceIntegrationMode.FreeroamBestEffort &&
            _justicePoliceIgnoreApplied && _justicePoliceDispatchDisabled &&
            !_justicePoliceSuppressionRestorePending)
        {
            // Le mode par défaut applique une fois puis laisse les autres mods
            // reprendre la main; seul Force réaffirme les natives au cadenceur.
            return;
        }
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
            !JusticeCustodyHasReached(now, _justiceNextPoliceSuppressionRestoreAt))
        {
            return;
        }

        _justiceNextPoliceSuppressionRestoreAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyPoliceSuppressionIntervalMs);
        SetJusticeCustodyPoliceSuppression(false);
    }

    private bool CanJusticeMutateGlobalPoliceState()
    {
        Ped player = Game.Player.Character;
        return Entity.Exists(player) && !player.IsDead &&
               _justiceCaseState != null &&
               _justiceCaseState.Phase == JusticePhase.Incarcerated &&
               !_justiceCustodyTransferPending && !_justiceCustodyResumePending &&
               IsJusticeCustodyPlayerIdentityCompatible(player) &&
               !IsJusticeCustodyRuntimeSuspended(player);
    }

    private string GetJusticePoliceIntegrationModeDisplay()
    {
        switch (_justicePoliceIntegrationMode)
        {
            case JusticePoliceIntegrationMode.Disabled:
                return "Désactivée";
            case JusticePoliceIntegrationMode.Force:
                return "Forcée (jeu libre)";
            default:
                return "Jeu libre · best-effort";
        }
    }

    private void CycleJusticePoliceIntegrationMode(int direction)
    {
        int count = Enum.GetValues(typeof(JusticePoliceIntegrationMode)).Length;
        int next = ((int)_justicePoliceIntegrationMode +
                    (direction < 0 ? -1 : 1) + count) % count;
        JusticePoliceIntegrationMode selected =
            (JusticePoliceIntegrationMode)next;
        if (selected == _justicePoliceIntegrationMode)
        {
            return;
        }

        _justicePoliceIntegrationMode = selected;
        if (selected == JusticePoliceIntegrationMode.Disabled)
        {
            SetJusticeCustodyPoliceSuppression(false);
        }
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        ShowStatus(
            "Justice · intégration police : " +
            GetJusticePoliceIntegrationModeDisplay() + ".",
            4200);
    }

    private void RecoverJusticeControlsAndInventoryFromMenu()
    {
        Ped player = Game.Player.Character;
        bool inventoryRestored = false;
        bool hasSnapshot = ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        if (hasSnapshot && Entity.Exists(player) && !player.IsDead &&
            IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            // Le merge ne supprime aucune arme : cette commande de diagnostic ne
            // peut donc jamais aggraver un inventaire déjà partiellement restauré.
            inventoryRestored = RestoreJusticeWeaponSnapshotMergeSafe(
                player,
                true,
                true);
            if (inventoryRestored)
            {
                _justiceDeferredInventoryRestore = true;
                _justiceInventoryRemoved = false;
                _justiceInventoryCustodyState =
                    JusticeInventoryCustodyState.RestorePending;
                _justiceNextDeferredInventoryRestoreAt = 0;
                CommitJusticeDeferredInventoryRestore();
            }
            else
            {
                _justiceDeferredInventoryRestore = true;
                _justiceInventoryCustodyState =
                    JusticeInventoryCustodyState.RestoreAmbiguous;
                _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
                    Game.GameTime,
                    JusticeCustodyDeferredRestoreRetryMs);
            }
        }
        else if (!hasSnapshot)
        {
            _justiceInventoryCustodyState =
                JusticeInventoryCustodyState.UnsupportedPreserved;
        }

        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        EnsureJusticeCustodyPlayerMobility(player);
        SetJusticeCustodyPoliceSuppression(false);
        SelectJusticeUnarmedSafe(player);
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        LogWarning(
            "Justice.Diagnostic",
            inventoryRestored
                ? "Récupération manuelle : contrôles, police et inventaire fusionné."
                : "Récupération manuelle : contrôles et police restaurés, aucun retrait effectué.");
        ShowStatus(
            inventoryRestored
                ? "Justice : inventaire et contrôles restaurés."
                : "Justice : contrôles libérés; snapshot conservé si disponible.",
            5000);
    }

    private bool TryJusticeEmergencyTeleport(
        Ped player,
        Vector3 targetPosition,
        float heading)
    {
        return TryJusticeEmergencyTeleport(
            player,
            targetPosition,
            heading,
            true);
    }

    private bool TryJusticeEmergencyTeleport(
        Ped player,
        Vector3 targetPosition,
        float heading,
        bool restoreScreen)
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
            if (restoreScreen)
            {
                Function.Call(Hash.DO_SCREEN_FADE_IN, 250);
            }
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
        // Je consomme d'abord la minute disciplinaire distincte. Le plafond légal
        // de la peine de base reste ainsi inchangé, même à dix minutes pleines.
        ConsumeJusticeCustodySentenceSeconds(_justiceCaseState, elapsedSeconds);
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
        if (layout == null)
        {
            return false;
        }

        JusticeCustodyVolume[] containmentVolumes =
            layout.ContainmentVolumes ?? layout.AllowedVolumes;
        if (containmentVolumes == null)
        {
            return false;
        }

        for (int index = 0; index < containmentVolumes.Length; index++)
        {
            JusticeCustodyVolume volume = containmentVolumes[index];
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

        bool insideContainment = IsInsideJusticeCustody(player.Position);
        if (!_justiceCustodyContainmentEstablished)
        {
            // Aucune évasion ne peut être créée avant un transfert vérifié ou
            // avant qu'une reprise constate ce protagoniste dans l'enceinte.
            // Cela neutralise les spawns et changements de héros hors site.
            _justiceOutsideCustodySinceAt = 0;
            if (!insideContainment)
            {
                return;
            }
            _justiceCustodyContainmentEstablished = true;
        }

        if (insideContainment)
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

    private void InterruptJusticeCustodyEscapeObservation()
    {
        if (_justiceCaseState != null &&
            _justiceCaseState.Phase == JusticePhase.Escaping &&
            !HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory))
        {
            // Je n'enregistre une évasion qu'après six secondes réellement
            // observées. Une pause, un chargement ou un switch annule donc la
            // tentative provisoire sans créer de charge ni de mandat.
            ApplyJusticeTransition(
                JusticeSignal.Restrained,
                _justiceCaseState.CustodyEpisodeId);
            JusticeMarkStateDirty();
        }

        _justiceOutsideCustodySinceAt = 0;
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
        bool preserveAmbiguousInventoryRecovery = false;
        if (Entity.Exists(player) && IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            JusticeInventoryRemovalResult removalResult =
                ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot)
                    ? RemoveJusticePlayerWeaponsSafe(player)
                    : JusticeInventoryRemovalResult.NotAttempted;
            if (removalResult == JusticeInventoryRemovalResult.EffectMayHaveApplied)
            {
                RegisterJusticeInventoryRemovalFailure(removalResult, now);
                preserveAmbiguousInventoryRecovery = true;
                ShowStatus(
                    "Évasion : confiscation non vérifiable, restitution du snapshot planifiée.",
                    4200);
            }
            else if (removalResult != JusticeInventoryRemovalResult.RemovedVerified)
            {
                if (!RegisterJusticeEscapeInventoryRemovalFailure(now))
                {
                    _justiceEscapePersistenceRetryAt = JusticeCustodyFutureTime(now, 1000);
                    ShowStatus("Évasion en attente : confiscation des armes à retenter…", 2200);
                    return;
                }

                // Après la borne, je termine l'évasion sans mentir sur RemoveAll :
                // les armes et les contrôles restent au joueur, puis le mandat
                // fugitif prend le relais sans créer de soft-lock permanent.
                ShowStatus(
                    "Évasion : inventaire préservé après échec de confiscation; mandat maintenu.",
                    4200);
            }
        }

        if (!RestoreJusticeCustodyPlayerTransientState(player))
        {
            _justiceEscapePersistenceRetryAt = JusticeCustodyFutureTime(now, 500);
            return;
        }

        if (!preserveAmbiguousInventoryRecovery)
        {
            _justiceWeaponSnapshot = null;
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
        }
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
        ResetJusticeCustodyPersistentFields(preserveAmbiguousInventoryRecovery);
        JusticeMarkStateDirty();
        if (JusticeFlushStateNow())
        {
            // La demande d'étoiles propre à l'évasion n'est exécutée qu'après
            // le commit du dossier fugitif et possède son propre WAL at-most-once.
            RetryJusticeEscapeWantedMinimum(GetJusticeWantedLevelSafe());
        }
        LogInfo("Justice.Evasion", "Évasion confirmée après sortie continue de l'enceinte extérieure.");
    }

    private bool RegisterJusticeEscapeInventoryRemovalFailure(int now)
    {
        _justiceInventoryRemovalFailureCount = Math.Min(
            JusticeCustodyInventoryRemovalMaximumAttempts,
            _justiceInventoryRemovalFailureCount + 1);
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;

        bool fallbackRequired =
            _justiceInventoryRemovalFailureCount >=
                JusticeCustodyInventoryRemovalMaximumAttempts ||
            !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        _justiceInventoryCustodyState = fallbackRequired
            ? JusticeInventoryCustodyState.UnsupportedPreserved
            : JusticeInventoryCustodyState.RemovalPending;
        _justiceNextInventoryPersistenceRetryAt = fallbackRequired
            ? 0
            : JusticeCustodyFutureTime(now, 1000);
        JusticeMarkStateDirty();
        if (fallbackRequired)
        {
            LogWarning(
                "Justice.Inventaire",
                "Confiscation d'évasion abandonnée après la borne; inventaire et contrôles préservés.");
        }
        return fallbackRequired;
    }

    private void CompleteJusticeLegalRelease(Ped player)
    {
        if (_justiceCaseState == null)
        {
            return;
        }

        int now = Game.GameTime;
        if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) > 0L)
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
            if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) > 0L)
            {
                ShowStatus("Justice : amende impayée convertie en détention.", 3600);
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
            if (!RestoreJusticeInventoryForLegalRelease(player, now))
            {
                _justiceReleaseRestoreRetryAt = JusticeCustodyFutureTime(now, 750);
                return false;
            }

            _justiceReleaseRestoreStartedAt = 0;
            _justiceReleaseRestoreRetryAt = 0;
            if (!RestoreJusticeCustodyPlayerTransientState(player))
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

        // Je n'acquitte jamais la sortie tant qu'un téléporteur ou une ancienne
        // sauvegarde laisse encore le joueur invincible.
        if (!EnsureJusticePlayerIsMortal(player))
        {
            _justiceNextReleaseTeleportAttemptAt = JusticeCustodyFutureTime(
                now,
                500);
            return false;
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

            SuppressJusticeRecognitionWantedLoss(
                "libération judiciaire légitime");
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
            GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) > 0L ||
            _justiceCaseState.FineDue > 0L ||
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
                    // L'incident de la dernière charge est persistant et unique. Le
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
            if (!EnsureJusticeActiveProfileResetPlayerIsMortal(
                    _justiceActivePlayerProfileSlot))
            {
                return false;
            }
            JusticeMarkStateDirty();
            return true;
        }

        Ped player;
        try
        {
            player = Game.Player.Character;
        }
        catch
        {
            // Je garde le reset ouvert si le monde GTA ne permet pas de prouver
            // l'identité et la mortalité du protagoniste propriétaire.
            return false;
        }

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

        if (!RestoreJusticeCustodyPlayerTransientState(player))
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


        if (!EnsureJusticePlayerIsMortal(player))
        {
            ShowStatus(
                "Amnistie en attente : protection du joueur encore active…",
                3600);
            return false;
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
                GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) == 0L) &&
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
               GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) > 0L &&
               !IsJusticeRuntimeProfileContextCompatible();
    }

    private bool TryPrepareJusticeCustodyForProfileSwitch(int now)
    {
        bool interruptedEscape = _justiceCaseState != null &&
            _justiceCaseState.Phase == JusticePhase.Escaping &&
            !HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory);
        InterruptJusticeCustodyEscapeObservation();
        if (_justiceCaseState != null &&
            _justiceCaseState.Phase == JusticePhase.Escaping)
        {
            // L'intention de confiscation est déjà durable : je ne peux plus
            // transformer cette évasion en simple changement de personnage.
            return false;
        }
        if (interruptedEscape)
        {
            // Au retour du détenu, je vérifie à nouveau sa cellule avant de
            // rouvrir les horloges et le détecteur d'évasion.
            _justiceCustodyResumePending = true;
            _justiceCustodyContainmentEstablished = false;
        }
        bool canPark = CanParkCurrentJusticeCustodyForProfileSwitch();

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

        CleanupJusticeCustodySceneEntitiesAndGroups();
        _justiceNextPoliceSuppressionAt = 0;
        _justiceOutsideCustodySinceAt = 0;
        ResetJusticeCustodyClock(now);
        _justiceCustodyElapsedRemainderMs = 0;
        return true;
    }

    private JusticeInventoryPreparationResult EnsureJusticeInventoryReadyForCustodyTransfer(
        Ped player,
        int now)
    {
        bool preservedInventoryReady =
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.UnsupportedPreserved &&
            !_justiceInventoryRemoved &&
            !_justiceWeaponControlsLocked &&
            !_justiceDeferredInventoryRestore;
        bool ambiguousInventoryReady =
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RestoreAmbiguous &&
            !_justiceInventoryRemoved &&
            !_justiceWeaponControlsLocked &&
            _justiceDeferredInventoryRestore &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        if (preservedInventoryReady || ambiguousInventoryReady)
        {
            // Je reprends directement un fallback déjà précommité. Je ne relance
            // ni le snapshot ni RemoveAll après un reload ou un téléport refusé.
            return JusticeInventoryPreparationResult.Ready;
        }

        if (_justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RemovedVerified &&
            _justiceInventoryRemoved &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            // Après un reload, je réapplique le retrait idempotent avant le
            // téléport. Une restitution provisoire d'OnAborted ne fuit pas en prison.
            JusticeInventoryRemovalResult removalResult =
                RemoveJusticePlayerWeaponsSafe(player);
            if (removalResult == JusticeInventoryRemovalResult.RemovedVerified)
            {
                return JusticeInventoryPreparationResult.Ready;
            }

            return RegisterJusticeInventoryRemovalFailure(removalResult, now);
        }

        if (ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return RetryJusticeInventoryConfiscationIfDue(player, now);
        }

        return PrepareJusticeInventoryConfiscation(player);
    }

    private JusticeInventoryPreparationResult PrepareJusticeInventoryConfiscation(Ped player)
    {
        JusticeWeaponSnapshot snapshot;
        if (!TryCaptureJusticeWeaponSnapshot(player, out snapshot) ||
            !ValidateJusticeWeaponSnapshot(snapshot))
        {
            _justiceWeaponSnapshot = null;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            _justiceInventoryCaptureFailureCount++;
            bool unsupported = _justiceInventoryCaptureFailureCount >=
                JusticeCustodyInventoryCaptureMaximumAttempts;
            _justiceInventoryCustodyState = unsupported
                ? JusticeInventoryCustodyState.UnsupportedPreserved
                : JusticeInventoryCustodyState.CapturePending;
            _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(
                Game.GameTime,
                unsupported ? 0 : 1000);
            JusticeMarkStateDirty();
            LogWarning(
                "Justice.Inventaire",
                unsupported
                    ? "Inventaire incompatible après trois essais : aucune arme retirée, fallback non destructif."
                    : "Snapshot momentanément indisponible : inventaire et contrôles préservés avant retry.");
            return unsupported
                ? JusticeInventoryPreparationResult.UnsupportedLoadout
                : JusticeInventoryPreparationResult.RetryableFailure;
        }

        _justiceWeaponSnapshot = snapshot;
        _justiceInventoryCaptureFailureCount = 0;
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.SnapshotPersisted;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        JusticeOperation operation =
            CreateJusticeCustodyOperation(JusticeOperationKind.ConfiscateInventory);
        JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        JusticeMarkStateDirty();

        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceNextInventoryPersistenceRetryAt =
                JusticeCustodyFutureTime(Game.GameTime, 100);
            JusticeMarkStateDirty();
            LogWarning(
                "Justice.Inventaire",
                "Snapshot en attente de confirmation disque : aucun retrait destructif n'a été effectué.");
            return JusticeInventoryPreparationResult.RetryableFailure;
        }

        _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovalPending;
        _justiceNextInventoryPersistenceRetryAt = 0;
        JusticeInventoryRemovalResult removalResult =
            RemoveJusticePlayerWeaponsSafe(player);
        if (removalResult != JusticeInventoryRemovalResult.RemovedVerified)
        {
            LogWarning(
                "Justice.Inventaire",
                removalResult == JusticeInventoryRemovalResult.EffectMayHaveApplied
                    ? "Confiscation non vérifiable : snapshot conservé pour restitution différée."
                    : "Confiscation refusée par GTA; le joueur reste hors prison avec ses contrôles.");
            return RegisterJusticeInventoryRemovalFailure(
                removalResult,
                Game.GameTime);
        }

        _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovedVerified;
        _justiceInventoryRemoved = true;
        _justiceWeaponControlsLocked = false;
        _justiceInventoryRemovalFailureCount = 0;
        JusticeMarkStateDirty();
        return JusticeInventoryPreparationResult.Ready;
    }

    private JusticeInventoryPreparationResult RetryJusticeInventoryConfiscationIfDue(
        Ped player,
        int now)
    {
        if (_justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RemovedVerified &&
            _justiceInventoryRemoved &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            // Je traite le retrait déjà vérifié comme terminal pendant la peine.
            // Seul un transfert explicite peut le réappliquer, notamment après
            // un reload, un respawn ou un téléport refusé.
            return JusticeInventoryPreparationResult.Ready;
        }

        if (_justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.UnsupportedPreserved ||
            ((_justiceInventoryCustodyState ==
                  JusticeInventoryCustodyState.RestoreAmbiguous ||
              _justiceInventoryCustodyState ==
                  JusticeInventoryCustodyState.RestorePending) &&
             _justiceDeferredInventoryRestore))
        {
            // Je ne réarme jamais RemoveAll après l'adoption du fallback. Un
            // snapshot ambigu reste réservé au merge post-libération.
            return JusticeInventoryPreparationResult.Ready;
        }

        if (!ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.UnsupportedPreserved;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            return JusticeInventoryPreparationResult.UnsupportedLoadout;
        }
        if (!JusticeCustodyHasReached(now, _justiceNextInventoryPersistenceRetryAt))
        {
            return JusticeInventoryPreparationResult.RetryableFailure;
        }
        if (_justiceInventoryRemovalFailureCount >=
            JusticeCustodyInventoryRemovalMaximumAttempts)
        {
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.UnsupportedPreserved;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            return JusticeInventoryPreparationResult.UnsupportedLoadout;
        }

        JusticeOperation operation =
            CreateJusticeCustodyOperation(JusticeOperationKind.ConfiscateInventory);
        if (!HasJusticeOperation(operation.Kind, operation.EpisodeId))
        {
            JusticePolicy.TryRegisterOperation(_justiceCaseState, operation);
        }
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.SnapshotPersisted;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceNextInventoryPersistenceRetryAt =
                JusticeCustodyFutureTime(now, 100);
            return JusticeInventoryPreparationResult.RetryableFailure;
        }

        _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovalPending;
        JusticeInventoryRemovalResult removalResult =
            RemoveJusticePlayerWeaponsSafe(player);
        if (removalResult != JusticeInventoryRemovalResult.RemovedVerified)
        {
            return RegisterJusticeInventoryRemovalFailure(removalResult, now);
        }

        _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovedVerified;
        _justiceInventoryRemoved = true;
        _justiceWeaponControlsLocked = false;
        _justiceInventoryRemovalFailureCount = 0;
        _justiceNextInventoryPersistenceRetryAt = 0;
        JusticeMarkStateDirty();
        LogInfo("Justice.Inventaire", "Snapshot persisté au retry, confiscation appliquée.");
        return JusticeInventoryPreparationResult.Ready;
    }

    private JusticeInventoryPreparationResult RegisterJusticeInventoryRemovalFailure(
        JusticeInventoryRemovalResult removalResult,
        int now)
    {
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;

        if (removalResult == JusticeInventoryRemovalResult.EffectMayHaveApplied)
        {
            if (!ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
            {
                // Ce cas est fermé par RemoveJusticePlayerWeaponsSafe, mais je
                // refuse tout de même de le dégrader en UnsupportedPreserved.
                _justiceInventoryCustodyState =
                    JusticeInventoryCustodyState.RemovalPending;
                _justiceNextInventoryPersistenceRetryAt = JusticeCustodyFutureTime(
                    now,
                    1000);
                JusticeMarkStateDirty();
                return JusticeInventoryPreparationResult.RetryableFailure;
            }

            // Je ne prétends jamais que l'inventaire est préservé après un
            // RemoveAll potentiellement exécuté. Le snapshot durable devient la
            // preuve de restitution et le merge pourra être rejoué sans supprimer.
            _justiceDeferredInventoryRestore = true;
            _justiceInventoryCustodyState =
                JusticeInventoryCustodyState.RestoreAmbiguous;
            _justiceInventoryRemovalFailureCount = 0;
            _justiceNextInventoryPersistenceRetryAt = 0;
            _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
                now,
                JusticeCustodyDeferredRestoreRetryMs);
            JusticeMarkStateDirty();
            return JusticeInventoryPreparationResult.UnsupportedLoadout;
        }

        _justiceInventoryRemovalFailureCount = Math.Min(
            JusticeCustodyInventoryRemovalMaximumAttempts,
            _justiceInventoryRemovalFailureCount + 1);
        bool unsupported = _justiceInventoryRemovalFailureCount >=
            JusticeCustodyInventoryRemovalMaximumAttempts;
        _justiceInventoryCustodyState = unsupported
            ? JusticeInventoryCustodyState.UnsupportedPreserved
            : JusticeInventoryCustodyState.RemovalPending;
        _justiceNextInventoryPersistenceRetryAt = unsupported
            ? 0
            : JusticeCustodyFutureTime(now, 1000);
        JusticeMarkStateDirty();
        return unsupported
            ? JusticeInventoryPreparationResult.UnsupportedLoadout
            : JusticeInventoryPreparationResult.RetryableFailure;
    }

    private bool CanContinueJusticeCustodyTransferWithoutInventoryConfiscation(
        JusticeInventoryPreparationResult preparationResult)
    {
        if (preparationResult == JusticeInventoryPreparationResult.UnsupportedLoadout)
        {
            bool preservedInventory =
                _justiceInventoryCustodyState ==
                    JusticeInventoryCustodyState.UnsupportedPreserved &&
                !_justiceInventoryRemoved &&
                !_justiceWeaponControlsLocked &&
                !_justiceDeferredInventoryRestore;
            bool ambiguousInventory =
                _justiceInventoryCustodyState ==
                    JusticeInventoryCustodyState.RestoreAmbiguous &&
                !_justiceInventoryRemoved &&
                !_justiceWeaponControlsLocked &&
                _justiceDeferredInventoryRestore &&
                ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
            return preservedInventory || ambiguousInventory;
        }

        // Je bascule dès le premier échec de capture entièrement non destructif.
        // Attendre trois essais laisserait GTA afficher l'hôpital alors qu'aucune
        // arme n'a été touchée et que la détention peut commencer sans risque.
        return preparationResult == JusticeInventoryPreparationResult.RetryableFailure &&
               _justiceInventoryCustodyState == JusticeInventoryCustodyState.CapturePending &&
               !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) &&
               !_justiceInventoryRemoved &&
               !_justiceWeaponControlsLocked &&
               !_justiceDeferredInventoryRestore;
    }

    private void EnterJusticeNonDestructiveCustodyFallback(Ped player, int now)
    {
        bool ambiguousRestorePending =
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RestoreAmbiguous &&
            _justiceDeferredInventoryRestore &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        if (!ambiguousRestorePending)
        {
            _justiceInventoryCustodyState =
                JusticeInventoryCustodyState.UnsupportedPreserved;
        }
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        JusticeMarkStateDirty();
        ShowStatus(
            ambiguousRestorePending
                ? "Justice : confiscation incertaine, restitution différée et détention maintenue."
                : "Justice : inventaire conservé, détention maintenue sans confiscation.",
            5500);
        LogWarning(
            "Justice.Inventaire",
            ambiguousRestorePending
                ? "Confiscation ambiguë : snapshot conservé, transfert en détention maintenu."
                : "Snapshot incompatible : inventaire préservé, transfert en détention maintenu.");
    }

    private bool PersistJusticeCriticalPrecommitRedundantly(
        [CallerMemberName] string caller = "")
    {
        // Je laisse le writer confirmer le snapshot complet sans bloquer GTA,
        // puis je rends seulement les petites frames WAL durables sur ce thread.
        return PersistJusticeCriticalPrecommitToWal(caller);
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

    private JusticeInventoryRemovalResult RemoveJusticePlayerWeaponsSafe(Ped player)
    {
        if (!Entity.Exists(player) ||
            !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return JusticeInventoryRemovalResult.NotAttempted;
        }

        bool effectMayHaveApplied = false;
        try
        {
            // Je marque l'effet avant l'appel : une exception native peut remonter
            // après que GTA a déjà retiré les armes.
            effectMayHaveApplied = true;
            Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, player.Handle, true);
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, JusticeUnarmedHash, true);
        }
        catch
        {
        }

        if (VerifyJusticePlayerHasNoWeapons(player))
        {
            return JusticeInventoryRemovalResult.RemovedVerified;
        }

        try
        {
            effectMayHaveApplied = true;
            player.Weapons.RemoveAll();
            Function.Call(Hash.SET_CURRENT_PED_WEAPON, player.Handle, JusticeUnarmedHash, true);
        }
        catch
        {
        }
        if (VerifyJusticePlayerHasNoWeapons(player))
        {
            return JusticeInventoryRemovalResult.RemovedVerified;
        }

        return effectMayHaveApplied
            ? JusticeInventoryRemovalResult.EffectMayHaveApplied
            : JusticeInventoryRemovalResult.NotAttempted;
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
        if (!Entity.Exists(player) || !ShouldEnforceJusticeCustodyWeaponLock())
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

    private bool ShouldEnforceJusticeCustodyWeaponLock()
    {
        if (_justiceInventoryRemoved || _justiceWeaponControlsLocked)
        {
            return true;
        }

        // Je dérive le verrou de l'état durable lorsque RemoveAll a pu ne
        // retirer qu'une partie des armes. Le même snapshot redevient libre dès
        // la sortie réelle de détention, afin que sa restitution puisse aboutir.
        return JusticeIsCustodyActive &&
               _justiceInventoryCustodyState ==
                   JusticeInventoryCustodyState.RestoreAmbiguous &&
               _justiceDeferredInventoryRestore;
    }

    private void RepairJusticeOrphanedCustodyControls(Ped player)
    {
        if (!_justiceWeaponControlsLocked)
        {
            return;
        }

        bool validOwner = JusticeIsCustodyActive &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) &&
            IsJusticeCustodyPlayerIdentityCompatible(player);
        if (validOwner)
        {
            return;
        }

        // Je ne retire jamais d'arme dans ce chemin de secours. Je libère le
        // combat et conserve tout snapshot valide pour une restitution ultérieure.
        _justiceWeaponControlsLocked = false;
        _justiceInventoryRemoved = false;
        bool hasRecoverySnapshot = ValidateJusticeWeaponSnapshot(
            _justiceWeaponSnapshot);
        _justiceDeferredInventoryRestore = hasRecoverySnapshot;
        _justiceInventoryCustodyState = hasRecoverySnapshot
            ? JusticeInventoryCustodyState.RestoreAmbiguous
            : JusticeInventoryCustodyState.UnsupportedPreserved;
        _justiceNextDeferredInventoryRestoreAt = hasRecoverySnapshot
            ? JusticeCustodyFutureTime(
                Game.GameTime,
                JusticeCustodyDeferredRestoreRetryMs)
            : 0;
        _justiceNextInventoryPersistenceRetryAt = 0;
        SelectJusticeUnarmedSafe(player);
        JusticeMarkStateDirty();
        LogWarning(
            "Justice.Inventaire",
            "Verrou de contrôles orphelin libéré sans suppression d'arme.");
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
        if (RemoveJusticePlayerWeaponsSafe(player) !=
            JusticeInventoryRemovalResult.RemovedVerified)
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
                        _justiceInventoryCustodyState =
                            JusticeInventoryCustodyState.RestorePending;
                        _justiceNextDeferredInventoryRestoreAt = JusticeCustodyFutureTime(
                            now,
                            JusticeCustodyDeferredRestoreRetryMs);
                        JusticeMarkStateDirty();
                        if (!PersistJusticeDeferredRestoreRedundantly())
                        {
                            _justiceInventoryRemoved = true;
                            _justiceWeaponControlsLocked = true;
                            _justiceInventoryCustodyState =
                                JusticeInventoryCustodyState.RemovedVerified;
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
        _justiceInventoryCustodyState = _justiceDeferredInventoryRestore
            ? JusticeInventoryCustodyState.RestorePending
            : JusticeInventoryCustodyState.None;
        JusticeMarkStateDirty();
        return true;
    }

    private bool RestoreJusticeWeaponSnapshotWithApiFallback(Ped player)
    {
        if (!Entity.Exists(player) || !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            return false;
        }

        if (RemoveJusticePlayerWeaponsSafe(player) !=
            JusticeInventoryRemovalResult.RemovedVerified)
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
            JusticeIsCustodyActive ||
            !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            !JusticeCustodyHasReached(now, _justiceNextDeferredInventoryRestoreAt) ||
            IsJusticeRuntimeSuspended(player))
        {
            return;
        }

        // Je ne rends jamais un snapshot ambigu au milieu d'une détention. Le
        // merge non destructif reprend seulement après la libération effective.

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
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
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
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.RestorePending;
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

    private void UpdateJusticeCustodyGuardRetaliation(Ped player, int now)
    {
        if (_justiceCaseState == null || !_justiceCustodyRuntimeActive ||
            (_justiceCaseState.Phase != JusticePhase.Incarcerated &&
             _justiceCaseState.Phase != JusticePhase.Escaping) ||
            !Entity.Exists(player) ||
            !IsJusticeCustodyDeathIdentityCompatible(player))
        {
            return;
        }

        if (player.IsDead)
        {
            // Je prends une dernière photo bornée des fronts garde vers joueur :
            // le coup fatal peut tomber pendant les 175 ms séparant deux scans.
            CaptureJusticeCustodyGuardDamageFrontsAtDeath(player);
            // Je fige ensuite le tueur : un tiers valide reste une preuve
            // négative, même si un garde avait aussi infligé des dégâts récents.
            FreezeJusticeCustodyGuardDeathPenalty(player);
            return;
        }
        if (!JusticeCustodyHasReached(
                now,
                _justiceNextCustodyGuardRetaliationScanAt))
        {
            return;
        }

        _justiceNextCustodyGuardRetaliationScanAt = JusticeCustodyFutureTime(
            now,
            JusticeCustodyGuardRetaliationScanMs);
        int count = _justiceCustodyGuards == null
            ? 0
            : Math.Min(
                JusticeCustodyMaximumGuardCount,
                _justiceCustodyGuards.Count);
        for (int index = 0; index < count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard))
            {
                continue;
            }

            // Je lis le front avant IsDead : un seul tir peut tuer le garde entre
            // deux scans, mais cette attaque doit quand même armer la riposte.
            bool playerDamagedGuard =
                TryCaptureJusticeDamageFront(guard, player);
            if (!_justiceCustodyGuardRetaliationActive && playerDamagedGuard)
            {
                BeginJusticeCustodyGuardRetaliation(player, now);
            }

            if (!_justiceCustodyGuardRetaliationActive || guard.IsDead)
            {
                continue;
            }

            if (TryCaptureJusticeDamageFront(player, guard))
            {
                _justiceCustodyLastDamagingGuardHandle = guard.Handle;
                _justiceCustodyLastDamagingGuardGeneration =
                    GetJusticeEntityGeneration(guard);
                _justiceCustodyLastGuardDamageAtMs = _justiceMonotonicTimeMs;
            }
            CommandJusticeCustodyGuardCombatIfDue(guard, player, index, now);
        }

        // Je consomme uniquement les fronts des gardes inspectés. Les détenus ne
        // sont jamais scannés : leurs bagarres restent des événements GTA naturels.
        FlushJusticeConsumedDamageFronts();
        if (_justiceCustodyGuardRetaliationActive)
        {
            SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum);
        }
    }

    private void BeginJusticeCustodyGuardRetaliation(Ped player, int now)
    {
        if (_justiceCustodyGuardRetaliationActive)
        {
            return;
        }

        _justiceCustodyGuardRetaliationActive = true;
        _justiceCustodyGuardDeathCauseEvaluated = false;
        _justiceCustodyGuardDeathPenaltyPending = false;
        _justiceCustodyLastDamagingGuardHandle = 0;
        _justiceCustodyLastDamagingGuardGeneration = 0;
        _justiceCustodyLastGuardDamageAtMs = -1L;
        SetJusticeCustodyPoliceSuppression(true);
        SetJusticeWantedMinimum(JusticeCustodyGuardWantedMinimum);
        JusticeMarkStateDirty();

        int count = Math.Min(
            JusticeCustodyMaximumGuardCount,
            _justiceCustodyGuards.Count);
        for (int index = 0; index < count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (IsJusticeCustodyPedOwnershipValid(guard) && !guard.IsDead)
            {
                CommandJusticeCustodyGuardCombatIfDue(
                    guard,
                    player,
                    index,
                    now,
                    true);
            }
        }

        ShowStatus(
            "Justice : agression d'un garde, riposte immédiate.",
            3600);
        LogInfo(
            "Justice.RiposteGardiens",
            "Agression d'un garde détenu prouvée; wanted GTA maintenu à deux étoiles minimum.");
    }

    private void CommandJusticeCustodyGuardCombatIfDue(
        Ped guard,
        Ped player,
        int index,
        int now,
        bool force = false)
    {
        if (!_justiceCustodyGuardRetaliationActive ||
            !IsJusticeCustodyPedOwnershipValid(guard) || guard.IsDead ||
            !Entity.Exists(player) || player.IsDead || index < 0 ||
            index >= JusticeCustodyMaximumGuardCount)
        {
            return;
        }

        EnsureJusticeCustodySceneMaintenanceBuffers();
        if (!force &&
            !JusticeCustodyHasReached(now, _justiceCustodyGuardCombatRetryAt[index]))
        {
            return;
        }

        bool alreadyFighting = false;
        try
        {
            alreadyFighting = guard.IsInCombatAgainst(player);
        }
        catch
        {
        }
        if (alreadyFighting)
        {
            _justiceCustodyGuardCombatRetryAt[index] = JusticeCustodyFutureTime(
                now,
                JusticeCustodyGuardCombatRetryMs);
            return;
        }

        try
        {
            Function.Call(
                Hash.TASK_COMBAT_PED,
                guard.Handle,
                player.Handle,
                0,
                16);
            _justiceCustodyGuardCombatRetryAt[index] = JusticeCustodyFutureTime(
                now,
                JusticeCustodyGuardCombatRetryMs);
        }
        catch (Exception ex)
        {
            _justiceCustodyGuardCombatRetryAt[index] = JusticeCustodyFutureTime(
                now,
                JusticeCustodyGuardCombatRetryMs);
            LogException("Justice.RiposteGardiens", ex);
        }
    }

    private void CaptureJusticeCustodyGuardDamageFrontsAtDeath(Ped player)
    {
        if (_justiceCustodyGuardDeathCauseEvaluated ||
            !Entity.Exists(player) || !player.IsDead)
        {
            return;
        }

        int count = _justiceCustodyGuards == null
            ? 0
            : Math.Min(
                JusticeCustodyMaximumGuardCount,
                _justiceCustodyGuards.Count);
        for (int index = 0; index < count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard))
            {
                continue;
            }

            // Je lis d'abord l'agression du garde : si les deux coups ont eu lieu
            // entre deux scans, la mort ne doit pas figer la cause avant la riposte.
            bool playerDamagedGuard =
                TryCaptureJusticeDamageFront(guard, player);
            if (!_justiceCustodyGuardRetaliationActive && playerDamagedGuard)
            {
                BeginJusticeCustodyGuardRetaliation(
                    player,
                    GetJusticeRawGameTimeSafe());
            }
            if (!_justiceCustodyGuardRetaliationActive)
            {
                continue;
            }

            int generation = GetJusticeEntityGeneration(guard);
            if (generation <= 0 ||
                !TryCaptureJusticeDamageFront(player, guard))
            {
                continue;
            }

            // Je garde le couple exact observé avant que GTA recycle le handle
            // pendant le respawn; l'attribution le revalide encore dans la scène.
            _justiceCustodyLastDamagingGuardHandle = guard.Handle;
            _justiceCustodyLastDamagingGuardGeneration = generation;
            _justiceCustodyLastGuardDamageAtMs = _justiceMonotonicTimeMs;
        }

        // Je consomme dans ce même passage les fronts inspectés : un flag létal
        // GTA ne doit jamais être relu au décès ou à la détention suivante.
        FlushJusticeConsumedDamageFronts();
    }

    private void FreezeJusticeCustodyGuardDeathPenalty(Ped player)
    {
        if (_justiceCustodyGuardDeathCauseEvaluated ||
            !Entity.Exists(player) || !player.IsDead)
        {
            return;
        }

        _justiceCustodyGuardDeathCauseEvaluated = true;
        _justiceCustodyGuardDeathPenaltyPending =
            _justiceCustodyGuardRetaliationActive &&
            IsJusticeCustodyDeathCausedByOwnedGuard(player);
    }

    private bool IsJusticeCustodyDeathCausedByOwnedGuard(Ped player)
    {
        try
        {
            Entity killer = player.GetKiller();
            if (Entity.Exists(killer))
            {
                // Un tueur tiers existant interdit explicitement le fallback par
                // historique : je ne crédite que le garde possédé exact.
                return IsJusticeExactOwnedCustodyGuard(
                    killer.Handle,
                    GetJusticeEntityGeneration(killer));
            }
        }
        catch
        {
        }

        long guardDamageAge = _justiceMonotonicTimeMs -
            _justiceCustodyLastGuardDamageAtMs;
        return _justiceCustodyLastGuardDamageAtMs >= 0L &&
               guardDamageAge >= 0L &&
               guardDamageAge < JusticePolicy.PendingIncidentLifetimeMs &&
               IsJusticeExactOwnedCustodyGuard(
                   _justiceCustodyLastDamagingGuardHandle,
                   _justiceCustodyLastDamagingGuardGeneration);
    }

    private bool IsJusticeExactOwnedCustodyGuard(int handle, int generation)
    {
        if (handle == 0 || generation <= 0)
        {
            return false;
        }

        int count = Math.Min(
            JusticeCustodyMaximumGuardCount,
            _justiceCustodyGuards.Count);
        for (int index = 0; index < count; index++)
        {
            Ped guard = _justiceCustodyGuards[index];
            if (IsJusticeCustodyPedOwnershipValid(guard) &&
                guard.Handle == handle &&
                GetJusticeEntityGeneration(guard) == generation)
            {
                return true;
            }
        }
        return false;
    }

    private void ResetJusticeCustodyGuardRetaliation(
        Ped player,
        bool clearWanted,
        bool preserveDeathDecision)
    {
        bool wasActive = _justiceCustodyGuardRetaliationActive;
        _justiceCustodyGuardRetaliationActive = false;
        _justiceNextCustodyGuardRetaliationScanAt = 0;
        _justiceCustodyLastDamagingGuardHandle = 0;
        _justiceCustodyLastDamagingGuardGeneration = 0;
        _justiceCustodyLastGuardDamageAtMs = -1L;
        EnsureJusticeCustodySceneMaintenanceBuffers();

        int count = _justiceCustodyGuards == null
            ? 0
            : Math.Min(
                JusticeCustodyMaximumGuardCount,
                _justiceCustodyGuards.Count);
        for (int index = 0; index < count; index++)
        {
            _justiceCustodyGuardCombatRetryAt[index] = 0;
            Ped guard = _justiceCustodyGuards[index];
            if (!IsJusticeCustodyPedOwnershipValid(guard) || guard.IsDead)
            {
                continue;
            }
            try
            {
                Function.Call(Hash.CLEAR_PED_TASKS, guard.Handle);
                Function.Call(Hash.TASK_STAND_STILL, guard.Handle, -1);
            }
            catch
            {
            }
        }

        if (!preserveDeathDecision)
        {
            _justiceCustodyGuardDeathCauseEvaluated = false;
            _justiceCustodyGuardDeathPenaltyPending = false;
        }
        if (clearWanted && wasActive && GetJusticeWantedLevelSafe() > 0)
        {
            // Je n'efface que les étoiles réellement possédées par cette riposte.
            // Un nettoyage technique sans agression ne doit jamais modifier un
            // wanted extérieur préexistant (migration, reset ou autre système).
            SuppressJusticeRecognitionWantedLoss(
                "fin de la riposte des gardiens");
            ClearJusticeWantedLevelOnce();
        }
        if (wasActive)
        {
            JusticeMarkStateDirty();
        }
    }

    private void EnsureJusticeCustodyScene(int now)
    {
        if (!JusticeCustodyHasReached(now, _justiceNextCustodySceneRefreshAt))
        {
            return;
        }

        _justiceNextCustodySceneRefreshAt = JusticeCustodyFutureTime(now, JusticeCustodySceneRefreshMs);
        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null)
        {
            return;
        }

        EnsureJusticeCustodyRelationshipGroups();
        CompactJusticeCustodyPedList(_justiceCustodyGuards);
        CompactJusticeCustodyPedList(_justiceCustodyInmates);
        MaintainJusticeCustodyScenePositions(layout, now);

        int guardTarget = _justiceCustodySite == JusticeCustodySite.Bolingbroke
            ? JusticeCustodyMaximumGuardCount
            : 2;
        int guardIndex = FindJusticeCustodyVacantPedSlot(
            _justiceCustodyGuards,
            guardTarget);
        if (guardIndex >= 0)
        {
            Vector3 position = layout.GuardPositions[Math.Min(
                guardIndex,
                layout.GuardPositions.Length - 1)];
            float heading = layout.GuardHeadings[Math.Min(
                guardIndex,
                layout.GuardHeadings.Length - 1)];
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

            if (guardIndex == _justiceCustodyGuards.Count)
            {
                _justiceCustodyGuards.Add(guard);
            }
            else
            {
                _justiceCustodyGuards[guardIndex] = guard;
            }
            Ped player = Game.Player.Character;
            if (Entity.Exists(player))
            {
                // Je photographie les deux directions avant le premier combat :
                // aucun vieux flag de dégâts ne peut déclencher ou attribuer une
                // riposte sur un handle GTA fraîchement créé.
                SynchronizeJusticeDamagePair(guard, player);
                SynchronizeJusticeDamagePair(player, guard);
                if (_justiceCustodyGuardRetaliationActive && !player.IsDead)
                {
                    CommandJusticeCustodyGuardCombatIfDue(
                        guard,
                        player,
                        guardIndex,
                        now,
                        true);
                }
            }
            // Je limite la création à une seule entité par rafraîchissement afin
            // que le streaming d'un modèle ne provoque jamais une longue saccade.
            return;
        }

        int inmateTarget = _justiceCustodySite == JusticeCustodySite.Bolingbroke
            ? JusticeCustodyMaximumInmateCount
            : 0;
        int inmateIndex = FindJusticeCustodyVacantPedSlot(
            _justiceCustodyInmates,
            inmateTarget);
        if (inmateIndex >= 0)
        {
            Vector3 position = layout.InmatePositions[Math.Min(
                inmateIndex,
                layout.InmatePositions.Length - 1)];
            Ped inmate = CreateJusticeCustodyPed(
                JusticeCustodyInmateModels[
                    inmateIndex % JusticeCustodyInmateModels.Length],
                position,
                (inmateIndex * 47.0f) % 360.0f,
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

            if (inmateIndex == _justiceCustodyInmates.Count)
            {
                _justiceCustodyInmates.Add(inmate);
            }
            else
            {
                _justiceCustodyInmates[inmateIndex] = inmate;
            }
        }
    }

    private int FindJusticeCustodyVacantPedSlot(List<Ped> peds, int targetCount)
    {
        if (peds == null)
        {
            return -1;
        }

        return SelectJusticeCustodyReplacementSlot(
            peds.Count,
            Math.Max(0, targetCount),
            -1);
    }

    internal static int SelectJusticeCustodyReplacementSlot(
        int currentCount,
        int targetCount,
        int firstVacantSlot)
    {
        int boundedCount = Math.Max(0, currentCount);
        int boundedTarget = Math.Max(0, targetCount);
        // Je ne remplace pas un PNJ mort ou perdu pendant la détention. Le
        // nombre de postes déjà créés reste le tombstone de la scène jusqu'au
        // teardown complet, même si GTA retire ensuite le cadavre.
        return boundedCount < boundedTarget ? boundedCount : -1;
    }

    private void MaintainJusticeCustodyScenePositions(
        JusticeCustodyLayout layout,
        int now)
    {
        if (layout == null)
        {
            return;
        }

        EnsureJusticeCustodySceneMaintenanceBuffers();
        MaintainJusticeCustodyPedPosts(
            _justiceCustodyGuards,
            layout.GuardPositions,
            layout.GuardHeadings,
            _justiceCustodyGuardReturnRetryAt,
            _justiceCustodyGuardCalmUntil,
            _justiceCustodyGuardWasNaturallyBusy,
            layout,
            true,
            now);
        MaintainJusticeCustodyPedPosts(
            _justiceCustodyInmates,
            layout.InmatePositions,
            null,
            _justiceCustodyInmateReturnRetryAt,
            _justiceCustodyInmateCalmUntil,
            _justiceCustodyInmateWasNaturallyBusy,
            layout,
            false,
            now);
    }

    private void MaintainJusticeCustodyPedPosts(
        List<Ped> peds,
        Vector3[] positions,
        float[] headings,
        int[] retryAt,
        int[] calmUntil,
        bool[] wasNaturallyBusy,
        JusticeCustodyLayout layout,
        bool guard,
        int now)
    {
        if (peds == null || positions == null || retryAt == null ||
            calmUntil == null || wasNaturallyBusy == null)
        {
            return;
        }

        int count = Math.Min(
            Math.Min(
                Math.Min(Math.Min(peds.Count, positions.Length), retryAt.Length),
                calmUntil.Length),
            wasNaturallyBusy.Length);
        for (int index = 0; index < count; index++)
        {
            Ped ped = peds[index];
            if (!IsJusticeCustodyPedOwnershipValid(ped) || ped.IsDead)
            {
                retryAt[index] = 0;
                calmUntil[index] = 0;
                wasNaturallyBusy[index] = false;
                continue;
            }

            bool naturallyBusy = IsJusticeCustodyPedNaturallyBusy(ped);
            if (ShouldDelayJusticeCustodyPedReturn(
                    naturallyBusy,
                    now,
                    ref calmUntil[index],
                    ref wasNaturallyBusy[index]))
            {
                // Je laisse GTA terminer combat, mêlée, fuite, taser ou ragdoll
                // sans qu'un ordre de poste écrase la réaction naturelle.
                retryAt[index] = 0;
                continue;
            }

            Vector3 target = positions[index];
            bool insideAllowedVolume = guard ||
                IsInsideJusticeCustodyAllowedArea(layout, ped.Position);
            float distanceSquared = GetJusticeCustodyDistanceSquared(
                ped.Position,
                target);
            if (!ShouldCommandJusticeCustodyPedReturn(
                    guard,
                    insideAllowedVolume,
                    distanceSquared))
            {
                retryAt[index] = 0;
                continue;
            }
            if (!JusticeCustodyHasReached(now, retryAt[index]))
            {
                continue;
            }

            retryAt[index] = JusticeCustodyFutureTime(
                now,
                JusticeCustodySceneReturnRetryMs);
            float heading = headings != null && index < headings.Length
                ? headings[index]
                : 0.0f;
            try
            {
                // Je laisse le navmesh ramener le PNJ sans téléport visible et
                // je ne réémets cet ordre qu'après le backoff de scène.
                Function.Call(
                    Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                    ped.Handle,
                    target.X,
                    target.Y,
                    target.Z,
                    1.0f,
                    -1,
                    1.25f,
                    true,
                    heading);
            }
            catch
            {
            }
        }
    }

    private static bool IsJusticeCustodyPedNaturallyBusy(Ped ped)
    {
        if (!Entity.Exists(ped) || ped.IsDead)
        {
            return false;
        }

        try
        {
            if (ped.IsInCombat || ped.IsBeingStunned ||
                Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, ped.Handle))
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        try
        {
            if (Function.Call<bool>((Hash)JusticeNativeIsPedFleeing, ped.Handle))
            {
                return true;
            }
        }
        catch
        {
            // Je n'écrase jamais une réaction naturelle si GTA ne sait pas me
            // confirmer son état : l'absence d'ordre est le repli le plus sûr.
            return true;
        }

        try
        {
            return Function.Call<bool>((Hash)JusticeNativeIsPedRagdoll, ped.Handle);
        }
        catch
        {
            return true;
        }
    }

    internal static bool ShouldDelayJusticeCustodyPedReturn(
        bool naturallyBusy,
        int now,
        ref int calmUntil,
        ref bool wasNaturallyBusy)
    {
        if (naturallyBusy)
        {
            // Je mémorise l'activité sans lancer le délai trop tôt : les dix
            // secondes commencent seulement quand GTA confirme le retour au calme.
            wasNaturallyBusy = true;
            calmUntil = 0;
            return true;
        }

        if (wasNaturallyBusy)
        {
            wasNaturallyBusy = false;
            calmUntil = JusticeCustodyFutureTime(
                now,
                JusticeCustodySceneCalmDelayMs);
            return true;
        }

        if (calmUntil == 0 || JusticeCustodyHasReached(now, calmUntil))
        {
            calmUntil = 0;
            return false;
        }

        return true;
    }

    private static bool IsInsideJusticeCustodyAllowedArea(
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

    internal static bool ShouldCommandJusticeCustodyPedReturn(
        bool guard,
        bool insideAllowedVolume,
        float distanceSquared)
    {
        if (float.IsNaN(distanceSquared) || distanceSquared < 0.0f)
        {
            return false;
        }

        return guard
            ? distanceSquared > JusticeCustodyGuardPostReturnDistanceSquared
            : !insideAllowedVolume;
    }

    private static float GetJusticeCustodyDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        float x = first.X - second.X;
        float y = first.Y - second.Y;
        float z = first.Z - second.Z;
        return x * x + y * y + z * z;
    }

    private void EnsureJusticeCustodySceneMaintenanceBuffers()
    {
        if (_justiceCustodyGuardReturnRetryAt == null ||
            _justiceCustodyGuardReturnRetryAt.Length !=
                JusticeCustodyMaximumGuardCount)
        {
            _justiceCustodyGuardReturnRetryAt =
                new int[JusticeCustodyMaximumGuardCount];
        }
        if (_justiceCustodyInmateReturnRetryAt == null ||
            _justiceCustodyInmateReturnRetryAt.Length !=
                JusticeCustodyMaximumInmateCount)
        {
            _justiceCustodyInmateReturnRetryAt =
                new int[JusticeCustodyMaximumInmateCount];
        }
        if (_justiceCustodyGuardCombatRetryAt == null ||
            _justiceCustodyGuardCombatRetryAt.Length !=
                JusticeCustodyMaximumGuardCount)
        {
            _justiceCustodyGuardCombatRetryAt =
                new int[JusticeCustodyMaximumGuardCount];
        }
        if (_justiceCustodyGuardCalmUntil == null ||
            _justiceCustodyGuardCalmUntil.Length !=
                JusticeCustodyMaximumGuardCount)
        {
            _justiceCustodyGuardCalmUntil =
                new int[JusticeCustodyMaximumGuardCount];
        }
        if (_justiceCustodyInmateCalmUntil == null ||
            _justiceCustodyInmateCalmUntil.Length !=
                JusticeCustodyMaximumInmateCount)
        {
            _justiceCustodyInmateCalmUntil =
                new int[JusticeCustodyMaximumInmateCount];
        }
        if (_justiceCustodyGuardWasNaturallyBusy == null ||
            _justiceCustodyGuardWasNaturallyBusy.Length !=
                JusticeCustodyMaximumGuardCount)
        {
            _justiceCustodyGuardWasNaturallyBusy =
                new bool[JusticeCustodyMaximumGuardCount];
        }
        if (_justiceCustodyInmateWasNaturallyBusy == null ||
            _justiceCustodyInmateWasNaturallyBusy.Length !=
                JusticeCustodyMaximumInmateCount)
        {
            _justiceCustodyInmateWasNaturallyBusy =
                new bool[JusticeCustodyMaximumInmateCount];
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
            ped.AlwaysKeepTask = false;
            ped.BlockPermanentEvents = false;
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
            Function.Call((Hash)JusticeNativeSetBlockingOfNonTemporaryEvents, ped.Handle, false);
            Function.Call((Hash)JusticeNativeSetPedKeepTask, ped.Handle, false);
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
            if (ownedPed)
            {
                // Je garde aussi les cadavres possédés dans leur slot : aucune
                // vague de remplacement ne doit apparaître pendant la peine.
                continue;
            }

            ForgetJusticeCustodyPedOwnership(handle);
            // Je conserve le slot comme tombstone jusqu'au teardown de scène.
            peds[index] = null;
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
        Ped player = null;
        try
        {
            player = Game.Player.Character;
        }
        catch
        {
        }
        ResetJusticeCustodyGuardRetaliation(player, true, false);
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
        ResetJusticeCustodySceneMaintenanceBuffers();

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

    private void ResetJusticeCustodySceneMaintenanceBuffers()
    {
        EnsureJusticeCustodySceneMaintenanceBuffers();
        Array.Clear(
            _justiceCustodyGuardReturnRetryAt,
            0,
            _justiceCustodyGuardReturnRetryAt.Length);
        Array.Clear(
            _justiceCustodyInmateReturnRetryAt,
            0,
            _justiceCustodyInmateReturnRetryAt.Length);
        Array.Clear(
            _justiceCustodyGuardCombatRetryAt,
            0,
            _justiceCustodyGuardCombatRetryAt.Length);
        Array.Clear(
            _justiceCustodyGuardCalmUntil,
            0,
            _justiceCustodyGuardCalmUntil.Length);
        Array.Clear(
            _justiceCustodyInmateCalmUntil,
            0,
            _justiceCustodyInmateCalmUntil.Length);
        Array.Clear(
            _justiceCustodyGuardWasNaturallyBusy,
            0,
            _justiceCustodyGuardWasNaturallyBusy.Length);
        Array.Clear(
            _justiceCustodyInmateWasNaturallyBusy,
            0,
            _justiceCustodyInmateWasNaturallyBusy.Length);
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

    private void JusticeWriteCustodyXml(XmlWriter writer)
    {
        if (writer == null)
        {
            return;
        }

        writer.WriteStartElement("Custody");
        writer.WriteAttributeString("active", JusticeIsCustodyActive ? "true" : "false");
        writer.WriteAttributeString(
            "guardRetaliationActive",
            _justiceCustodyGuardRetaliationActive ? "true" : "false");
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
        writer.WriteAttributeString("inventoryRemoved", _justiceInventoryRemoved ? "true" : "false");
        writer.WriteAttributeString("weaponControlsLocked", _justiceWeaponControlsLocked ? "true" : "false");
        writer.WriteAttributeString(
            "inventoryState",
            ((int)_justiceInventoryCustodyState).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "inventoryCaptureFailures",
            Math.Max(0, _justiceInventoryCaptureFailureCount).ToString(
                CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "inventoryRemovalFailures",
            Math.Max(0, _justiceInventoryRemovalFailureCount).ToString(
                CultureInfo.InvariantCulture));
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
            "false");
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
        WriteJusticeWeaponSnapshotXml(writer);
        writer.WriteEndElement();
    }

    private void MigrateLegacyJusticeInventoryCustodyState()
    {
        bool hasSnapshot = ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        if (_justiceDeferredInventoryRestore && hasSnapshot)
        {
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.RestorePending;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            return;
        }
        if (_justiceInventoryRemoved && hasSnapshot)
        {
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovedVerified;
            _justiceWeaponControlsLocked = false;
            return;
        }
        if (_justiceWeaponControlsLocked && hasSnapshot)
        {
            // L'ancien format pouvait recharger un retrait précommité. Je le
            // reprends hors prison sans bloquer le combat avant la vérification.
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.RemovalPending;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            return;
        }
        if (_justiceWeaponControlsLocked && !hasSnapshot)
        {
            // Je répare explicitement l'état v1 non récupérable relevé par l'audit.
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.UnsupportedPreserved;
            _justiceInventoryRemoved = false;
            _justiceWeaponControlsLocked = false;
            return;
        }
        if (hasSnapshot)
        {
            _justiceInventoryCustodyState = JusticeInventoryCustodyState.SnapshotPersisted;
            return;
        }

        _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
    }

    private bool ValidateJusticeInventoryCustodyStateInvariant()
    {
        bool hasSnapshot = ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot);
        switch (_justiceInventoryCustodyState)
        {
            case JusticeInventoryCustodyState.None:
                return !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
                       !hasSnapshot;

            case JusticeInventoryCustodyState.CapturePending:
                return !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
                       !hasSnapshot;

            case JusticeInventoryCustodyState.SnapshotPersisted:
            case JusticeInventoryCustodyState.RemovalPending:
                return hasSnapshot && !_justiceInventoryRemoved &&
                       !_justiceWeaponControlsLocked;

            case JusticeInventoryCustodyState.RemovedVerified:
                return hasSnapshot && _justiceInventoryRemoved;

            case JusticeInventoryCustodyState.UnsupportedPreserved:
                return !_justiceInventoryRemoved && !_justiceWeaponControlsLocked;

            case JusticeInventoryCustodyState.RestorePending:
            case JusticeInventoryCustodyState.RestoreAmbiguous:
                return hasSnapshot && !_justiceWeaponControlsLocked;

            default:
                return false;
        }
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
        bool guardRetaliationActive;
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
        int inventoryStateValue;
        int inventoryCaptureFailures;
        int inventoryRemovalFailures;
        bool hasInventoryState = custody.HasAttribute("inventoryState");
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
                 "guardRetaliationActive",
                 false,
                 out guardRetaliationActive) ||
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
                int.MaxValue,
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
            !TryReadJusticeIntStrict(
                custody,
                "inventoryState",
                -1,
                -1,
                (int)JusticeInventoryCustodyState.RestoreAmbiguous,
                out inventoryStateValue) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryCaptureFailures",
                0,
                0,
                JusticeCustodyInventoryCaptureMaximumAttempts,
                out inventoryCaptureFailures) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryRemovalFailures",
                0,
                0,
                JusticeCustodyInventoryRemovalMaximumAttempts,
                out inventoryRemovalFailures) ||
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
            (hasInventoryState &&
             !IsJusticeInventoryCustodyStateSemanticallyValid(
                 (JusticeInventoryCustodyState)inventoryStateValue,
                 inventoryRemoved,
                 weaponControlsLocked,
                 deferredInventoryRestore,
                 snapshot)) ||
            (inventoryRemoved && !ValidateJusticeWeaponSnapshot(snapshot)) ||
            (deferredInventoryRestore &&
             (!ValidateJusticeWeaponSnapshot(snapshot) || inventoryRemoved || weaponControlsLocked)) ||
            (voluntaryPayment != null &&
             (savedActive || !caseState.Enabled || IsJusticeCustodyPhase(caseState.Phase))) ||
            (guardRetaliationActive &&
             (!savedActive || waitingForRespawn || deathRebindPending ||
              (caseState.Phase != JusticePhase.Incarcerated &&
               caseState.Phase != JusticePhase.Escaping))) ||
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
        bool nonDeferredRecoveryState = fineIntent != null ||
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
                initialSentence == 0 && fineIntent == null &&
                !guardRetaliationActive &&
                !inventoryRemoved && !weaponControlsLocked &&
                !playerStateStored &&
                !waitingForRespawn && !deathRebindPending &&
                releaseSelectedWeaponHash == JusticeUnarmedHash;
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
            (site != JusticeCustodySite.None || initialSentence != 0 ||
             guardRetaliationActive ||
             snapshot != null || inventoryRemoved ||
             weaponControlsLocked || deferredInventoryRestore || playerStateStored ||
             releaseSelectedWeaponHash != JusticeUnarmedHash))
        {
            return false;
        }

        bool identityRequired = savedActive || fineIntent != null ||
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

    private static bool IsJusticeInventoryCustodyStateSemanticallyValid(
        JusticeInventoryCustodyState state,
        bool inventoryRemoved,
        bool weaponControlsLocked,
        bool deferredInventoryRestore,
        JusticeWeaponSnapshot snapshot)
    {
        bool hasSnapshot = ValidateJusticeWeaponSnapshot(snapshot);
        switch (state)
        {
            case JusticeInventoryCustodyState.None:
            case JusticeInventoryCustodyState.CapturePending:
                return !inventoryRemoved && !weaponControlsLocked &&
                       !deferredInventoryRestore && !hasSnapshot;

            case JusticeInventoryCustodyState.SnapshotPersisted:
            case JusticeInventoryCustodyState.RemovalPending:
                return hasSnapshot && !inventoryRemoved &&
                       !weaponControlsLocked && !deferredInventoryRestore;

            case JusticeInventoryCustodyState.RemovedVerified:
                return hasSnapshot && inventoryRemoved && !deferredInventoryRestore;

            case JusticeInventoryCustodyState.UnsupportedPreserved:
                return !inventoryRemoved && !weaponControlsLocked &&
                       !deferredInventoryRestore;

            case JusticeInventoryCustodyState.RestorePending:
            case JusticeInventoryCustodyState.RestoreAmbiguous:
                return hasSnapshot && !inventoryRemoved &&
                       !weaponControlsLocked && deferredInventoryRestore;

            default:
                return false;
        }
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

    private static bool IsJusticeFineSentenceCompatibleWithResolution(
        JusticePaymentResolution resolution,
        int actualSentence,
        int sentenceIfDebited,
        int sentenceIfConverted)
    {
        switch (resolution)
        {
            case JusticePaymentResolution.Confirmed:
            case JusticePaymentResolution.Ambiguous:
                return actualSentence == sentenceIfDebited;
            case JusticePaymentResolution.Rejected:
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
        JusticePaymentResolution resolution = JusticePaymentResolution.Prepared;
        long fineInDisputeBefore = 0L;
        long ambiguousAmount = 0L;
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
             !TryReadJusticeCashWriteResult(element, out cashWriteResult) ||
             !TryReadJusticePaymentResolution(
                 element,
                 debitAttempted,
                 cashWriteResult,
                 out resolution) ||
             !TryReadJusticeLongStrict(
                 element,
                 "fineInDisputeBefore",
                 0L,
                 0L,
                 JusticePolicy.MaxActiveFine,
                 out fineInDisputeBefore) ||
             !TryReadJusticeLongStrict(
                 element,
                 "ambiguousAmount",
                 0L,
                 0L,
                 JusticePolicy.MaxActiveFine,
                 out ambiguousAmount))
        {
            return null;
        }

        int expectedDebit = (int)Math.Min(fineAmount, (long)cashBefore);
        string operationId = JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyFine, episodeId);
        bool operationCommitted = caseState.CompletedOperationIds.Contains(operationId);
        bool matchesPrecommit = !operationCommitted &&
            caseState.FineDue == fineAmount &&
            caseState.FineInDispute == fineInDisputeBefore;
        if (matchesPrecommit)
        {
            bool expectedStationPlanned = site == JusticeCustodySite.None
                ? GetJusticeCustodyTotalRemainingSeconds(caseState) <
                    JusticeCustodyPrisonThresholdSeconds
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
            IsJusticeFineSentenceCompatibleWithResolution(
                resolution,
                caseState.SentenceSeconds,
                sentenceIfDebited,
                sentenceIfConverted) &&
            caseState.FineInDispute == JusticePolicy.SaturatingAdd(
                fineInDisputeBefore,
                resolution == JusticePaymentResolution.Ambiguous
                    ? ambiguousAmount
                    : 0L,
                JusticePolicy.MaxActiveFine);
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
            (resolution != JusticePaymentResolution.Ambiguous ||
             (debitAttempted && cashWriteResult == JusticeCashWriteResult.Unknown &&
              ambiguousAmount == debitAmount)) &&
            (resolution == JusticePaymentResolution.Ambiguous ||
             ambiguousAmount == 0L) &&
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
            CashWriteResult = cashWriteResult,
            Resolution = resolution,
            FineInDisputeBefore = fineInDisputeBefore,
            AmbiguousAmount = ambiguousAmount
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
                !TryReadJusticeIntStrict(element, "remainingSeconds", 0, 1, 300, out remaining))
            {
                return false;
            }
        }
        return true;
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
        writer.WriteAttributeString("resolution", intent.Resolution.ToString());
        writer.WriteAttributeString(
            "fineInDisputeBefore",
            Math.Max(0L, intent.FineInDisputeBefore).ToString(
                CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "ambiguousAmount",
            Math.Max(0L, intent.AmbiguousAmount).ToString(
                CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "attemptedAtUtcTicks",
            Math.Max(0L, intent.AttemptedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
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
        int inventoryStateValue;
        int inventoryCaptureFailures;
        int inventoryRemovalFailures;
        bool savedActive;
        bool guardRetaliationActive;
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
        if (!TryReadJusticeBoolStrict(
                custody,
                "active",
                false,
                out savedActive) ||
            !TryReadJusticeIntStrict(
                custody,
                "initialSentenceSeconds",
                0,
                0,
                int.MaxValue,
                out initialSentence) ||
            !TryReadJusticeBoolStrict(
                custody,
                "guardRetaliationActive",
                false,
                out guardRetaliationActive) ||
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
            !TryReadJusticeIntStrict(
                custody,
                "inventoryState",
                -1,
                -1,
                (int)JusticeInventoryCustodyState.RestoreAmbiguous,
                out inventoryStateValue) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryCaptureFailures",
                0,
                0,
                JusticeCustodyInventoryCaptureMaximumAttempts,
                out inventoryCaptureFailures) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryRemovalFailures",
                0,
                0,
                JusticeCustodyInventoryRemovalMaximumAttempts,
                out inventoryRemovalFailures) ||
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
        _justiceInventoryRemoved = inventoryRemoved;
        _justiceWeaponControlsLocked = weaponControlsLocked;
        _justiceInventoryCaptureFailureCount = inventoryCaptureFailures;
        _justiceInventoryRemovalFailureCount = inventoryRemovalFailures;
        _justiceDeferredInventoryRestore = deferredInventoryRestore;
        _justiceCustodyWaitingForRespawn = waitingForRespawn;
        _justiceCustodyDeathRebindPending = deathRebindPending;
        _justiceCustodyGuardRetaliationActive = guardRetaliationActive;
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
        JusticeDisciplineIntent legacyDisciplineIntent =
            ReadJusticeDisciplineIntentXml(custody);
        _justiceWeaponSnapshot = ReadJusticeWeaponSnapshotXml(custody);
        if (inventoryStateValue >= 0)
        {
            _justiceInventoryCustodyState =
                (JusticeInventoryCustodyState)inventoryStateValue;
        }
        else
        {
            MigrateLegacyJusticeInventoryCustodyState();
        }
        if ((fineIntentElement != null && _justiceFineDebitIntent == null) ||
            (voluntaryPaymentElement != null &&
             _justiceVoluntaryFinePaymentIntent == null) ||
            (disciplineIntentElement != null && legacyDisciplineIntent == null) ||
            (legacyDisciplineIntent != null &&
             !IsJusticeDisciplineIntentWalConsistent(legacyDisciplineIntent)) ||
            (snapshotElement != null && _justiceWeaponSnapshot == null) ||
            !ValidateJusticeInventoryCustodyStateInvariant() ||
            (_justiceInventoryRemoved && !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot)) ||
            (_justiceDeferredInventoryRestore &&
             (!ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) ||
              _justiceInventoryRemoved || _justiceWeaponControlsLocked)) ||
            (_justiceVoluntaryFinePaymentIntent != null &&
             (_justiceCaseState == null || !_justiceCaseState.Enabled ||
              IsJusticeCustodyPhase(_justiceCaseState.Phase))) ||
            (_justiceCustodyGuardRetaliationActive &&
             (_justiceCaseState == null || !savedActive ||
              _justiceCustodyWaitingForRespawn ||
              _justiceCustodyDeathRebindPending ||
              (_justiceCaseState.Phase != JusticePhase.Incarcerated &&
               _justiceCaseState.Phase != JusticePhase.Escaping))) ||
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
        // Je lis encore l'ancien attribut true pour garder la compatibilité,
        // puis je le normalise immédiatement : Justice ne restaure plus jamais
        // une invincibilité de transition après la peine.
        _justiceCustodyStoredInvincible = false;
        _justiceCustodyStoredFrozen = storedFrozen;
        _justiceCustodyStoredCanRagdoll = storedCanRagdoll;
        bool nonDeferredRecoveryState = fineIntentElement != null ||
            _justiceInventoryRemoved ||
            _justiceWeaponControlsLocked || _justiceCustodyPlayerStateStored ||
            (snapshotElement != null && !_justiceDeferredInventoryRestore);
        bool inactiveStateIsCanonical = true;
        if (!savedActive)
        {
            inactiveStateIsCanonical = _justiceCustodySite == JusticeCustodySite.None &&
                _justiceCustodyInitialSentenceSeconds == 0 &&
                !_justiceCustodyGuardRetaliationActive &&
                _justiceFineDebitIntent == null &&
                !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
                !_justiceCustodyPlayerStateStored &&
                !_justiceCustodyWaitingForRespawn && !_justiceCustodyDeathRebindPending &&
                _justiceReleaseSelectedWeaponHash == JusticeUnarmedHash;
            if (!_justiceDeferredInventoryRestore)
            {
                inactiveStateIsCanonical &= _justiceWeaponSnapshot == null &&
                    _justiceCustodyPlayerModelHash == 0 && _justiceCustodyPlayerSlot == -1;
            }
        }
        bool capturedStateIsCanonical = !capturedPhase ||
            (_justiceCustodySite == JusticeCustodySite.None &&
             _justiceCustodyInitialSentenceSeconds == 0 &&
             !_justiceCustodyGuardRetaliationActive &&
             _justiceVoluntaryFinePaymentIntent == null &&
             _justiceWeaponSnapshot == null &&
             !_justiceInventoryRemoved && !_justiceWeaponControlsLocked &&
             !_justiceDeferredInventoryRestore && !_justiceCustodyPlayerStateStored &&
             _justiceReleaseSelectedWeaponHash == JusticeUnarmedHash);
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
            // de préconfiscation : je garde le joueur libre tant que le retrait
            // n'est pas vérifié, conformément à l'état RemovalPending.
            _justiceWeaponControlsLocked = false;
            _justiceNextInventoryPersistenceRetryAt = 0;
        }

        if (savedActive && _justiceCaseState != null &&
            _justiceCaseState.Phase != JusticePhase.Captured &&
            GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) > 0L)
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
        JusticePaymentResolution resolution = JusticePaymentResolution.Prepared;
        long fineInDisputeBefore = 0L;
        long ambiguousAmount = 0L;
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
             TryReadJusticeCashWriteResult(element, out cashWriteResult) &&
             TryReadJusticePaymentResolution(
                 element,
                 debitAttempted,
                 cashWriteResult,
                 out resolution) &&
             TryReadJusticeLongStrict(
                 element,
                 "fineInDisputeBefore",
                 0L,
                 0L,
                 JusticePolicy.MaxActiveFine,
                 out fineInDisputeBefore) &&
             TryReadJusticeLongStrict(
                 element,
                 "ambiguousAmount",
                 0L,
                 0L,
                 JusticePolicy.MaxActiveFine,
                 out ambiguousAmount);

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
                       (debitAttempted || attemptedAtUtcTicks == 0L) &&
                       (resolution != JusticePaymentResolution.Ambiguous ||
                        (debitAttempted &&
                         cashWriteResult == JusticeCashWriteResult.Unknown &&
                         ambiguousAmount == debitAmount)) &&
                       (resolution == JusticePaymentResolution.Ambiguous ||
                        ambiguousAmount == 0L);
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
            _justiceCaseState.FineDue == fineAmount &&
            _justiceCaseState.FineInDispute == fineInDisputeBefore;
        if (matchesPrecommit)
        {
            bool expectedStationPlanned = _justiceCustodySite == JusticeCustodySite.None
                ? GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <
                    JusticeCustodyPrisonThresholdSeconds
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
            IsJusticeFineSentenceCompatibleWithResolution(
                resolution,
                _justiceCaseState.SentenceSeconds,
                sentenceIfDebited,
                sentenceIfConverted) &&
            _justiceCaseState.FineInDispute == JusticePolicy.SaturatingAdd(
                fineInDisputeBefore,
                resolution == JusticePaymentResolution.Ambiguous
                    ? ambiguousAmount
                    : 0L,
                JusticePolicy.MaxActiveFine);
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
            CashWriteResult = cashWriteResult,
            Resolution = resolution,
            FineInDisputeBefore = fineInDisputeBefore,
            AmbiguousAmount = ambiguousAmount
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
        JusticeInventoryCustodyState deferredInventoryState =
            _justiceInventoryCustodyState ==
                JusticeInventoryCustodyState.RestoreAmbiguous
                ? JusticeInventoryCustodyState.RestoreAmbiguous
                : JusticeInventoryCustodyState.RestorePending;
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
        if (_justiceCustodyRespawnTransferPending ||
            _justiceCustodyRespawnRestorePending)
        {
            // Une amnistie, une libération ou l'arrêt du script ne doit jamais
            // laisser GTA sous le fondu qui protégeait l'origine du respawn.
            TryRestoreJusticeCustodyRespawnTransferMask();
        }
        if (!_justiceCustodyRespawnRestorePending)
        {
            _justiceCustodyRespawnTransferPending = false;
            _justiceCustodyRespawnMaskNeedsRearm = false;
        }
        _justicePoliceDeathRespawnMaskIntentPending = false;
        ClearAllJusticeRepairArrestPreJudgmentHoldingIntents();
        ResetJusticePoliceDeathPreJudgmentHoldingState();
        ResetJusticeCapturePrecommitConfirmation();
        _justiceCustodyPersistenceOutageHoldingEstablished = false;
        _justiceCustodyContainmentEstablished = false;
        _justiceCustodyDeathRebindPending = false;
        _justiceCustodyDeathStatePersistencePending = false;
        _justiceCustodyDeathPersistenceRevision = 0L;
        _justiceCustodyDeathPersistenceWriteFailures = 0L;
        _justiceCustodyDeathPersistenceWriterFailureObserved = false;
        _justiceNextCustodyDeathPersistenceRetryAt = 0;
        _justiceCustodyTransferRollbackFinalizationRevision = 0L;
        _justiceCustodyTransferRollbackFinalizationWriteFailures = 0L;
        _justiceNextCustodyTransferRollbackFinalizationRetryAt = 0;
        _justiceCustodyPlayerStateStored = false;
        _justiceCustodyStoredInvincible = false;
        _justiceCustodyStoredFrozen = false;
        _justiceCustodyStoredCanRagdoll = true;
        _justiceCustodyGuardRetaliationActive = false;
        _justiceNextCustodyGuardRetaliationScanAt = 0;
        _justiceCustodyLastDamagingGuardHandle = 0;
        _justiceCustodyLastDamagingGuardGeneration = 0;
        _justiceCustodyLastGuardDamageAtMs = -1L;
        _justiceCustodyGuardDeathCauseEvaluated = false;
        _justiceCustodyGuardDeathPenaltyPending = false;
        _justiceCustodyPlayerHandle = 0;
        _justiceCustodyInitialSentenceSeconds = 0;
        _justiceCustodyLastTickAt = 0;
        _justiceCustodyElapsedRemainderMs = 0;
        _justiceNextCustodySceneRefreshAt = 0;
        _justiceNextCustodyModelRetryAt = 0;
        ResetJusticeCustodyTransferRetryState();
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceNextInventoryPersistenceRetryAt = 0;
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
        _justiceInventoryCaptureFailureCount = 0;
        _justiceInventoryRemovalFailureCount = 0;
        _justiceWeaponSnapshot = null;
        _justiceDeferredInventoryRestore = false;
        _justiceNextDeferredInventoryRestoreAt = 0;
        _justiceFineDebitIntent = null;
        _justiceVoluntaryFinePaymentIntent = null;
        _justiceNextVoluntaryPaymentResumeAt = 0;
        ResetJusticeFineCashReadRetry();
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

        if (shouldPreserveDeferredRestore)
        {
            _justiceWeaponSnapshot = deferredSnapshot;
            _justiceDeferredInventoryRestore = true;
            _justiceInventoryCustodyState = deferredInventoryState;
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
        Ped player = TryGetJusticeShutdownPlayer();
        try
        {
            RunJusticeCustodyShutdownStep(
                "Inventaire",
                () => RestoreJusticeInventoryProvisionallyOnShutdown(player));
            RunJusticeCustodyShutdownStep(
                "EtatJoueur",
                () => RestoreJusticeTransientStateOnShutdown(player));
            RunJusticeCustodyShutdownStep(
                "Scene",
                CleanupJusticeCustodyEntitiesAndGroups);
        }
        finally
        {
            // Les flags police sont globaux : même une panne inventaire ou scène
            // ne peut empêcher leur restauration best-effort.
            RunJusticeCustodyShutdownStep(
                "Police",
                RestoreJusticePoliceSuppressionOnShutdown);
            _justiceWeaponControlsLocked = false;
            _justiceCustodyRuntimeActive = false;
            _justiceCustodyTransferPending = false;
            _justiceCustodyResumePending = false;
            if (_justiceCustodyRespawnTransferPending ||
                _justiceCustodyRespawnRestorePending)
            {
                // Un reload ou un unload pendant un retry WAL doit toujours rendre
                // l'écran avant d'abandonner le latch runtime de cette session.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (RestoreJusticeCustodyRespawnTransferMask())
                    {
                        break;
                    }
                }
            }
            _justiceCustodyRespawnTransferPending = false;
            _justiceCustodyRespawnMaskNeedsRearm = false;
            _justiceCustodyRespawnRestorePending = false;
            _justicePoliceDeathRespawnMaskIntentPending = false;
            ClearAllJusticeRepairArrestPreJudgmentHoldingIntents();
            ResetJusticePoliceDeathPreJudgmentHoldingState();
            ResetJusticeCapturePrecommitConfirmation();
            _justiceCustodyPersistenceOutageHoldingEstablished = false;
            _justiceCustodyContainmentEstablished = false;
            ResetJusticeCustodyTransferRetryState();
        }
    }

    private Ped TryGetJusticeShutdownPlayer()
    {
        try
        {
            return Game.Player.Character;
        }
        catch (Exception ex)
        {
            // Je garde la lecture du ped isolée sans ajouter un septième domaine
            // de restauration : chaque étape suivante accepte déjà un ped absent.
            LogException("Justice.ArretDetention.Joueur", ex);
            return null;
        }
    }

    private void RestoreJusticeInventoryProvisionallyOnShutdown(Ped player)
    {
        // Je rends provisoirement le snapshot à l'arrêt pour qu'un unload ou un
        // crash du loader ne puisse jamais laisser les armes perdues. Les drapeaux
        // persistés restent inchangés : le prochain chargement reconfisquera.
        if (_justiceWeaponSnapshot == null || !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            (!_justiceInventoryRemoved && !_justiceDeferredInventoryRestore))
        {
            return;
        }

        // Je ne passe jamais par RemoveAll pendant OnAborted : je fusionne le
        // snapshot complet et vérifié avec l'inventaire présent.
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

    private void RestoreJusticeTransientStateOnShutdown(Ped player)
    {
        for (int attempt = 0;
             attempt < 3 &&
             _justiceCustodyPlayerStateStored;
             attempt++)
        {
            RestoreJusticeCustodyPlayerTransientState(player);
        }

        // Justice s'arrête avant le gestionnaire partagé : je normalise aussi sa
        // baseline afin que son propre shutdown ne réactive pas l'invincibilité.
        PrepareJusticePlayerMortalityForShutdown(player);
    }

    private void RunJusticeCustodyShutdownStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogException("Justice.ArretDetention." + name, ex);
        }
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
     * - JusticePrepareLegalReleaseState(), JusticeRegisterEscape().
     *
     * Justice.cs appelle Begin au jugement, Update à chaque tick, HandleKey
     * depuis son routeur clavier, les deux hooks XML dans sa racine persistée,
     * Amnesty avant de vider le dossier, puis Shutdown lors de OnAborted.
     */
}
