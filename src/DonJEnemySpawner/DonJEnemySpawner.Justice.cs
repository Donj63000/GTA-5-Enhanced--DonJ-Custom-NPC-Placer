using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;

public sealed partial class DonJEnemySpawner
{
    [Flags]
    private enum JusticeDeferredRuntimeFront
    {
        None = 0,
        DeathStarted = 1 << 0,
        ArrestStarted = 1 << 1,
        ArrestEnded = 1 << 2,
        WantedLost = 1 << 3,
        WantedRaised = 1 << 4,
        IdentityChanged = 1 << 5
    }

    private enum JusticeWantedClearResult
    {
        Succeeded,
        Rejected,
        Unknown
    }

    private const string JusticeStateFileName = "_justice_state.xml";
    private const int JusticeStateVersion = 1;
    // Je dimensionne la borne pour trois profils remplis au maximum, plus le
    // miroir racine du profil actif. Elle reste finie pour rejeter un XML abusif.
    private const long JusticeStateMaximumFileBytes = 16L * 1024L * 1024L;
    private const int JusticeStateSaveDebounceMs = 2000;
    private const int JusticeStateCheckpointMs = 15000;
    private const int JusticeStateFailureRetryMs = 1000;
    private const int JusticeStateFailureLogCooldownMs = 10000;
    private const int JusticeScalarScanIntervalMs = 120;
    private const int JusticeCrimeScanIntervalMs = 120;
    private const int JusticeIncidentProcessingIntervalMs = 120;
    private const int JusticeCrimeScanWindowMs = 1600;
    private const int JusticeMaximumWitnessesPerEvent = 24;
    private const int JusticeMaximumVictimCandidatesPerEvent = 8;
    private const int JusticeMaximumVehiclesPerEvent = 16;
    private const int JusticeMaximumPendingIncidents = 32;
    private const int JusticeMaximumConfirmedIncidentsPerTick = 6;
    private const int JusticeMaximumRecentVictims = 32;
    private const int JusticeMaximumAllyTokens = 48;
    private const int JusticeMaximumTrackedIdentities = 160;
    private const float JusticeWitnessRadius = 80.0f;
    private const float JusticeWarrantRecognitionRadius = 60.0f;
    private const int JusticeWarrantRecognitionMs = 750;
    private const int JusticeWarrantRecognitionNotificationCooldownMs = 30000;
    private const int JusticeWarrantScanIntervalMs = 250;
    private const int JusticeAllyAttributionLifetimeMs = 12000;
    private const float JusticeAllyAttributionRadius = 120.0f;
    private const int JusticeAllyAttributionScanIntervalMs = 120;
    private const int JusticeAllyCustodyHoldMs = 60 * 60 * 1000;
    private const int JusticeEvadingPoliceDelayMs = 12000;
    private const int JusticeAimingThreatDelayMs = 1500;
    private const int JusticeNotificationMs = 7000;
    private const int JusticeSelfDefenseWindowMs = 8000;
    private const int JusticeIdentityLifetimeMs = 30000;
    private const int JusticeHitAndRunMinimumDelayMs = 2000;
    private const float JusticeHitAndRunDepartureDistance = 35.0f;
    private const int JusticeWantedWriteSuppressionMs = 1000;
    private const int JusticeWantedClearRetryMs = 500;
    private const int JusticeWantedClearRetryWindowMs = 10000;
    private const int JusticeNativeCircuitRetryMs = 5000;
    private const int JusticeMaskedArrestProbeMaximumMs = 12000;
    private const int JusticeMaximumDamageFrontsPerTick = 96;
    private const int JusticeMaximumWitnessActorSnapshots = 24;
    private const int JusticeMaximumDamagePairBaselines = 256;
    private const int JusticeKnownCircumstanceMask =
        (int)(JusticeCircumstances.Armed |
              JusticeCircumstances.ExplosiveOrIncendiary |
              JusticeCircumstances.ActiveWarrant |
              JusticeCircumstances.InCustody |
              JusticeCircumstances.MultipleVictims |
              JusticeCircumstances.GroupCrime |
              JusticeCircumstances.OrganizedBand |
              JusticeCircumstances.ProportionalSelfDefense |
              JusticeCircumstances.ExcessiveSelfDefense |
              JusticeCircumstances.VehicleUsedAsWeapon);

    // Je garde les natives dans ce partial pour ne dépendre d'aucun membre v3.
    private const ulong JusticeNativeGetMissionFlag = 0xA33CDCCDA663159EUL;
    private const ulong JusticeNativeGetIsLoadingScreenActive = 0x10D0A8F259E93EC9UL;
    private const ulong JusticeNativeIsCutsceneActive = 0x991251AFC3981F84UL;
    private const ulong JusticeNativeIsPlayerSwitchInProgress = 0xD9D2CFFF49FAB35FUL;
    private const ulong JusticeNativeIsPlayerBeingArrested = 0x388A47C51ABDAC8EUL;
    private const ulong JusticeNativeGetTimeSinceLastArrest = 0x5063F92F07C2A316UL;
    private const ulong JusticeNativeGetTimeSincePlayerHitPed = 0xE36A25322DC35F42UL;
    private const ulong JusticeNativeGetTimeSincePlayerHitVehicle = 0x5D35ECF3A81A0EE0UL;
    private const ulong JusticeNativeHasPlayerBeenSpottedInStolenVehicle = 0xD705740BB0A1CF4CUL;
    private const ulong JusticeNativeHasEntityClearLosInFront = 0x0267D00AF114F17AUL;
    private const ulong JusticeNativeGetEntityModel = 0x9F47B058362C84B5UL;
    private const ulong JusticeNativeGetPedTimeOfDeath = 0x1E98817B311AE98AUL;

    private const int JusticeCircuitLoading = 1 << 0;
    private const int JusticeCircuitMission = 1 << 1;
    private const int JusticeCircuitCutscene = 1 << 2;
    private const int JusticeCircuitPlayerSwitch = 1 << 3;
    private const int JusticeCircuitArrestState = 1 << 4;
    private const int JusticeCircuitLastArrest = 1 << 5;
    private const int JusticeCircuitStolenVehicleReport = 1 << 6;
    private const int JusticeCircuitHitPedTimer = 1 << 7;
    private const int JusticeCircuitHitVehicleTimer = 1 << 8;
    private const int JusticeCircuitLineOfSight = 1 << 9;
    private const int JusticeCircuitCanSeeEntity = 1 << 10;
    private const int JusticeCircuitClearDamage = 1 << 11;
    private const int JusticeCircuitActivityScenario = 1 << 12;
    private const int JusticeCircuitPedTimeOfDeath = 1 << 13;
    private const ulong JusticeNativeGetSelectedPedWeapon = 0x0A6DB4965674D243UL;
    private const ulong JusticeNativeClearPlayerWantedLevel = 0xB302540597885499UL;

    private sealed class JusticePendingRuntimeIncident
    {
        public JusticeIncident Incident;
        public Ped VictimPed;
        public Entity VictimEntity;
        public readonly List<JusticeRuntimeWitness> Witnesses =
            new List<JusticeRuntimeWitness>(JusticeMaximumWitnessesPerEvent);
    }

    private sealed class JusticeRuntimeWitness
    {
        public Ped Ped;
        public int Generation;
        public JusticeEvidenceKind Kind;
        public long ReportDueAtMs;
    }

    private sealed class JusticeRecentVictim
    {
        public Ped Ped;
        public int Generation;
        public string CausalEventId;
        public long LastPlayerAttackAtMs;
        public bool HomicideQueued;
        public bool HitAndRunQueued;
        public bool VehicleWasWeapon;
        public bool DirectPlayerDamage;
        public JusticeCircumstances Circumstances;
    }

    private sealed class JusticeRecentVehicle
    {
        public Vehicle Vehicle;
        public int Generation;
        public long LastPlayerDamageAtMs;
        public bool DestructionQueued;
        public JusticeCircumstances Circumstances;
    }

    private sealed class JusticeTrackedIdentity
    {
        public Entity Entity;
        public int ModelHash;
        public long MemoryAddress;
        public int Generation;
        public long LastSeenAtMs;
    }

    private sealed class JusticeDamagePairBaseline
    {
        public int VictimHandle;
        public int VictimGeneration;
        public int AttackerHandle;
        public int AttackerGeneration;
        public bool WasDamaged;
        public long LastSeenAtMs;
    }

    private sealed class JusticeAllyCausalToken
    {
        public Ped Ally;
        public Ped PoliceTarget;
        public int AllyGeneration;
        public int TargetGeneration;
        public bool WasDonJOwnedAtCreation;
        public string EpisodeId;
        public long CreatedAtMs;
        public long ExpiresAtMs;
        public bool Structured;
        public bool AssaultQueued;
        public bool HomicideQueued;
        public long LastObservedDamageAtMs;
    }

    private sealed class JusticeDamageFrontConsumption
    {
        public Entity Entity;
        public int Generation;
    }

    private sealed class JusticeActorWitnessSnapshot
    {
        public Ped Actor;
        public int Generation;
        public Ped[] Candidates;
    }

    private sealed class JusticeSelfDefenseThreat
    {
        public long ExpiresAtMs;
        public bool Armed;
        public bool VehicleThreat;
    }

    private readonly List<JusticePendingRuntimeIncident> _justicePendingIncidents =
        new List<JusticePendingRuntimeIncident>(JusticeMaximumPendingIncidents);
    private readonly List<JusticeRecentVictim> _justiceRecentVictims =
        new List<JusticeRecentVictim>(JusticeMaximumRecentVictims);
    private readonly List<JusticeRecentVehicle> _justiceRecentVehicles =
        new List<JusticeRecentVehicle>(JusticeMaximumRecentVictims);
    private readonly List<JusticeAllyCausalToken> _justiceAllyTokens =
        new List<JusticeAllyCausalToken>(JusticeMaximumAllyTokens);
    private readonly Dictionary<int, JusticeTrackedIdentity> _justiceTrackedIdentities =
        new Dictionary<int, JusticeTrackedIdentity>();
    private readonly Dictionary<string, long> _justiceSelfDefenseUntilByVictim =
        new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly Dictionary<string, JusticeSelfDefenseThreat> _justiceSelfDefenseThreatByVictim =
        new Dictionary<string, JusticeSelfDefenseThreat>(StringComparer.Ordinal);
    private readonly List<JusticeDamageFrontConsumption> _justiceDamageFrontsToConsume =
        new List<JusticeDamageFrontConsumption>(JusticeMaximumDamageFrontsPerTick);
    private readonly List<JusticeDamagePairBaseline> _justiceDamagePairBaselines =
        new List<JusticeDamagePairBaseline>(JusticeMaximumDamagePairBaselines);
    private readonly List<JusticeActorWitnessSnapshot> _justiceWitnessSnapshots =
        new List<JusticeActorWitnessSnapshot>(JusticeMaximumWitnessActorSnapshots);
    private readonly List<int> _justiceReleasedAllyHandles =
        new List<int>(JusticeMaximumAllyTokens);
    private static readonly Ped[] JusticeEmptyPedCandidates = new Ped[0];
    private readonly JusticePendingRuntimeIncident[] _justiceConfirmedIncidentBuffer =
        new JusticePendingRuntimeIncident[JusticeMaximumPendingIncidents];

    private JusticeCaseState _justiceCaseState;
    private JusticeRecordState _justiceRecordState;
    private bool _justiceEnabled;
    private bool _justiceInitialized;
    private bool _justiceStateDirty;
    private bool _justiceWasShooting;
    private bool _justiceWasInMelee;
    private bool _justiceWasInCombat;
    private bool _justiceWasJacking;
    private bool _justiceWasSpottedInStolenVehicle;
    private bool _justiceWasDead;
    private bool _justiceWasBeingArrested;
    private bool _justiceArrestCompletionProbePending;
    private bool _justicePursuitActive;
    private bool _justiceAimThreatQueued;
    private bool _justiceDamageFrontPrimingPending;
    private bool _justicePlayerVitalityBaselineInitialized;
    private bool _justiceWantedLossPending;
    private bool _justiceWantedClearPending;
    private bool _justiceAmnestyWantedClearAttempted;
    private bool _justiceCaptureRetryPending;
    private bool _justiceCaptureRetryDeath;
    private bool _justicePursuitDeathObservedDuringSuspension;
    private int _justiceSuspendedPursuitDeathPlayerSlot = -1;
    private int _justiceSuspendedPursuitDeathPlayerModelHash;
    private bool _justiceRuntimeSuspendedCached;
    private int _justiceLastRawGameTime;
    private int _justiceLastWantedLevel;
    private int _justiceAimTargetHandle;
    private int _justiceAimTargetGeneration;
    private int _justiceRecognitionCandidateHandle;
    private int _justiceRecognitionCandidateGeneration;
    private int _justiceNextIdentityGeneration;
    private int _justiceIncidentSequence;
    private int _justiceEpisodeSequence;
    private int _justiceRecognitionSequence;
    private int _justiceWitnessSnapshotCount;
    private int _justiceDamageFrontCount;
    private int _justiceDamagePairBaselineCount;
    private int _justiceDamagePairReplacementIndex;
    private int _justiceWrittenWantedLevel;
    private int _justicePlayerHealthBaseline;
    private int _justicePlayerArmorBaseline;
    private int _justiceDeathDetectionBarrierAtRawGameTime;
    private bool _justiceDeathDetectionBarrierInitialized;
    private long _justiceMonotonicTimeMs;
    private long _justiceNextStateSaveAtMs;
    private long _justiceNextCheckpointAtMs;
    private long _justiceNextStateFlushAttemptAtMs;
    private long _justiceNextStateFailureLogAtMs;
    private int _justiceSuppressedStateFailureLogs;
    private Func<int, bool> _justiceStateFlushFailureOverride;
    private int _justiceStateFlushAttemptSequence;
    private long _justiceNextEarlyScanAtMs;
    private long _justiceNextFrontScanAtMs;
    private long _justiceNextSuspensionCheckAtMs;
    private long _justiceCrimeScanUntilMs;
    private long _justiceNextCrimeScanAtMs;
    private long _justiceNextIncidentProcessingAtMs;
    private long _justiceNextWarrantScanAtMs;
    private long _justiceNextAllyAttributionScanAtMs;
    private long _justiceWantedEpisodeStartedAtMs;
    private long _justiceRecognitionStartedAtMs;
    private long _justiceAimStartedAtMs;
    private long _justiceLastCleanAdvanceAtMs;
    private long _justiceCleanCarryMilliseconds;
    private long _justiceWrittenWantedExpiresAtMs;
    private long _justiceArrestCompletionProbeStartedAtMs;
    private long _justiceNextWantedClearRetryAtMs;
    private long _justiceWantedClearRetryUntilMs;
    private string _justiceDetectionEpisodeId = string.Empty;
    private string _justiceSessionId = string.Empty;
    private string _justiceActiveDischargeCausalId = string.Empty;
    private long _justiceActiveDischargeExpiresAtMs;
    private int _justiceUnavailableNativeCircuits;
    private int _justiceLoggedUnavailableNativeCircuits;
    private long[] _justiceNativeCircuitRetryAtMs;
    private Func<int, bool> _justiceWantedWriteOverride;
    private Func<int?> _justiceWantedClearObservationOverride;
    private bool _justiceBackupRepairPending;
    private bool _justiceBackupRepairFailureLogged;
    private long _justiceNextBackupRepairAtMs;
    private string _justiceBackupRepairPrimaryPath = string.Empty;
    private string _justiceBackupRepairSourcePath = string.Empty;
    private bool _justiceWantedRisePendingCorrelation;
    private long _justiceWantedRiseObservedAtMs;
    private int _justiceWantedRiseDetectionPass;
    private int _justiceEventDetectionPass;
    private bool _justiceAmnestyPending;
    private bool _justiceAmnestyPrecommitRedundant;
    private int _justiceLastCanonicalPlayerSlot = -1;
    private int _justiceLastCanonicalPlayerModelHash;
    private JusticeDeferredRuntimeFront _justiceDeferredRuntimeFronts;
    private int _justiceDeferredRuntimeFrontPlayerSlot = -1;
    private int _justiceDeferredRuntimeFrontPlayerModelHash;
    private bool _justiceDeferredRuntimeFrontHadPursuit;

    private void InitializeJusticeSystem()
    {
        // Je laisse ce crochet désactivé au runtime ; les tests headless peuvent
        // l'injecter par réflexion sans que la build Release signale un champ non initialisé.
        _justiceWantedWriteOverride = null;
        _justiceStateFlushFailureOverride = null;
        _justiceWantedClearObservationOverride = null;
        _justiceStateFlushAttemptSequence = 0;
        _justiceAmnestyPrecommitRedundant = false;
        _justiceNativeCircuitRetryAtMs = new long[32];
        _justiceSessionId = Guid.NewGuid().ToString("N");
        _justiceCaseState = new JusticeCaseState();
        _justiceRecordState = new JusticeRecordState();
        InitializeJusticePlayerProfiles();
        if (IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot))
        {
            _justicePlayerProfiles[_justiceActivePlayerProfileSlot].CaseState = _justiceCaseState;
            _justicePlayerProfiles[_justiceActivePlayerProfileSlot].RecordState = _justiceRecordState;
            _justiceLastCanonicalPlayerSlot = _justiceActivePlayerProfileSlot;
        }
        _justiceLastRawGameTime = GetJusticeRawGameTimeSafe();
        _justiceMonotonicTimeMs = 0L;
        _justiceLastCleanAdvanceAtMs = 0L;

        bool loaded = TryLoadJusticeState(false);
        if (!loaded)
        {
            _justiceCaseState.Enabled = false;
            _justiceEnabled = false;
        }

        NormalizeLoadedJusticeState();
        InitializeJusticePersistenceServices();
        PrewarmJusticeRuntimeBuffers();
        _justiceLastWantedLevel = GetJusticeWantedLevelSafe();
        if (loaded)
        {
            ReconcileLoadedJusticePursuitState(_justiceLastWantedLevel);
        }
        _justiceDamageFrontPrimingPending = _justiceEnabled;
        _justiceWasDead = IsJusticePlayerDeadSafe(Game.Player.Character);
        _justiceNextCheckpointAtMs = JusticeStateCheckpointMs;
        _justiceInitialized = true;

        LogInfo(
            "Justice",
            loaded
                ? "Etat judiciaire charge, schéma=" +
                  _justiceLoadedSchemaMajor.ToString(CultureInfo.InvariantCulture) +
                  ", activation=" + (_justiceEnabled ? "oui" : "non") + "."
                : "Aucun etat judiciaire exploitable; Justice reste desactivee par defaut.");
    }

    private void UpdateJusticeEarly()
    {
        if (!_justiceInitialized)
        {
            return;
        }

        AdvanceJusticeMonotonicClock();

        if (_justiceMonotonicTimeMs < _justiceNextEarlyScanAtMs)
        {
            return;
        }
        _justiceNextEarlyScanAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs;

        Ped player = Game.Player.Character;
        int wantedLevel = GetJusticeWantedLevelSafe();
        bool dead = IsJusticePlayerDeadSafe(player);
        bool arrested;
        bool arrestStateValid = TryGetJusticePlayerBeingArrestedSafe(out arrested);
        bool preserveArrestLatch = false;

        if (_justiceBackupRepairPending)
        {
            ObserveJusticeFrontsWhilePersistenceBlocked(
                player,
                wantedLevel,
                dead,
                arrestStateValid,
                arrested);
            if (_justiceMonotonicTimeMs < _justiceNextBackupRepairAtMs ||
                !TryRepairJusticePrimaryFromLoadedBackup())
            {
                // Je bloque les mutations métier, mais les fronts scalaires sont
                // désormais mémorisés avec l'identité du héros qui les a produits.
                _justiceProfileContextBlocked = true;
                return;
            }

            ReconcileJusticeFrontsAfterPersistenceRepair(player, wantedLevel);
        }

        if (IsJusticeRuntimeSuspended(player))
        {
            // Je synchronise seulement les fronts bruts pendant une mission, un
            // chargement, une cinématique ou un changement de personnage. Ainsi
            // la reprise ne fabrique ni mandat, ni arrestation, ni hausse wanted.
            if (dead && !_justiceWasDead && _justiceEnabled && HasActiveJusticeCase())
            {
                if (JusticeIsCustodyActive && IsJusticeCustodyDeathIdentityCompatible(player))
                {
                    // Je persiste le droit de relier le ped de respawn avant de
                    // quitter cette frame. Un crash pendant l'écran de mort ne
                    // doit jamais transformer le décès en changement de héros.
                    ObserveJusticeCustodyDeathDuringSuspension(player);
                }
                else if (!JusticeIsCustodyActive &&
                         (_justicePursuitActive || _justiceLastWantedLevel > 0))
                {
                    if (_justiceCaseState.Phase == JusticePhase.AtLarge)
                    {
                        _justiceCaseState.Phase = JusticePhase.Wanted;
                    }
                    _justicePursuitDeathObservedDuringSuspension = true;
                    int observedSlot = GetCurrentSinglePlayerCashSlotSafe();
                    _justiceSuspendedPursuitDeathPlayerSlot = observedSlot >= 0
                        ? observedSlot
                        : _justiceLastCanonicalPlayerSlot;
                    _justiceSuspendedPursuitDeathPlayerModelHash =
                        GetJusticePedModelHashSafe(player);
                    JusticeMarkStateDirty();
                    JusticeFlushStateNow();
                }
            }
            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            if (_justiceEnabled && HasActiveJusticeCase() && !JusticeIsCustodyActive &&
                _justiceLastWantedLevel > 0 && wantedLevel == 0)
            {
                _justiceWantedLossPending = true;
            }
            else if (wantedLevel > 0)
            {
                _justiceWantedLossPending = false;
            }
            _justiceLastWantedLevel = wantedLevel;
            return;
        }

        if (!EnsureJusticeProfileMatchesCanonicalPlayer(player))
        {
            _justiceProfileContextBlocked = true;
            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = wantedLevel;
            return;
        }

        _justiceProfileContextBlocked = false;

        ObserveJusticeCanonicalPlayerIdentity(player);
        if (_justiceActiveProfileResetPending)
        {
            ObserveJusticeCriticalFrontsBeforeTransactionReturn(
                player,
                wantedLevel,
                dead,
                arrestStateValid,
                arrested,
                false);
            // Je termine le WAL du reset avant tout crime, paiement ou capture du
            // profil. Un redémarrage après restitution ne peut donc jamais
            // ressusciter l'ancien dossier durable.
            ResumeJusticeActiveProfileResetTransaction();
            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = wantedLevel;
            return;
        }
        if (_justiceVoluntaryFinePaymentIntent != null)
        {
            bool transactionPreservesArrest =
                ObserveJusticeCriticalFrontsBeforeTransactionReturn(
                    player,
                    wantedLevel,
                    dead,
                    arrestStateValid,
                    arrested,
                    true);
            // Je termine ou réconcilie le WAL financier avant toute nouvelle
            // détection, capture ou mutation de dossier pour ce personnage.
            ResumeJusticeVoluntaryFinePayment();
            if (arrestStateValid && !transactionPreservesArrest)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = wantedLevel;
            return;
        }
        if (_justiceAmnestyPending)
        {
            ObserveJusticeCriticalFrontsBeforeTransactionReturn(
                player,
                wantedLevel,
                dead,
                arrestStateValid,
                arrested,
                false);
            ResumeJusticeAmnestyTransaction();
            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = GetJusticeWantedLevelSafe();
            return;
        }

        // Je ne rejoue l'effacement explicitement demandé par l'amnistie qu'en
        // gameplay libre. Une mission ou un chargement ne reçoit aucune mutation.
        RetryJusticeWantedClearAfterAmnesty();
        wantedLevel = GetJusticeWantedLevelSafe();

        if (_justiceEnabled && _justicePursuitDeathObservedDuringSuspension &&
            !JusticeIsCustodyActive)
        {
            if (IsPendingJusticeDeathCaptureIdentityCompatible(player))
            {
                if (_justiceCaseState.Phase == JusticePhase.AtLarge)
                {
                    _justiceCaseState.Phase = JusticePhase.Wanted;
                }
                if (BeginJusticeCapture(true))
                {
                    ClearPendingJusticeDeathCapture();
                    JusticeMarkStateDirty();
                    JusticeFlushStateNow();
                }
                else
                {
                    _justiceCaptureRetryPending = false;
                    _justiceCaptureRetryDeath = false;
                }

                if (arrestStateValid)
                {
                    _justiceWasBeingArrested = arrested;
                }
                _justiceWasDead = dead;
                _justiceLastWantedLevel = wantedLevel;
                return;
            }
            if (ShouldConvertUnknownJusticeDeathCaptureToWarrant(player))
            {
                FinalizeUnknownJusticeCaptureAsWarrant(
                    "Mort en ped custom sans slot canonique : aucun jugement, débit ou inventaire modifié.");
                if (arrestStateValid)
                {
                    _justiceWasBeingArrested = arrested;
                }
                _justiceWasDead = dead;
                _justiceLastWantedLevel = wantedLevel;
                return;
            }
        }

        if (_justiceEnabled && _justiceCaptureRetryPending &&
            !_justicePursuitDeathObservedDuringSuspension)
        {
            if (BeginJusticeCapture(_justiceCaptureRetryDeath))
            {
                _justiceCaptureRetryPending = false;
                _justiceCaptureRetryDeath = false;
            }

            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = wantedLevel;
            return;
        }

        bool policePursuitDeath = _justiceEnabled && HasActiveJusticeCase() && !JusticeIsCustodyActive &&
                                  dead && !_justiceWasDead &&
                                  (_justicePursuitActive || _justiceLastWantedLevel > 0);

        if (!policePursuitDeath &&
            TryResolveJusticeMaskedArrestOnWantedLoss(
                wantedLevel,
                arrestStateValid))
        {
            if (arrestStateValid)
            {
                _justiceWasBeingArrested = arrested;
            }
            _justiceWasDead = dead;
            _justiceLastWantedLevel = wantedLevel;
            return;
        }

        ResolveDeferredJusticeWantedLoss(wantedLevel);

        if (_justiceEnabled)
        {
            if (policePursuitDeath)
            {
                // GTA retire souvent les étoiles dès la frame de mort. Je conserve
                // le front de poursuite précédent avant de traiter cette baisse.
                if (_justiceCaseState.Phase == JusticePhase.AtLarge)
                {
                    _justiceCaseState.Phase = JusticePhase.Wanted;
                }
                if (!BeginJusticeCapture(true) &&
                    !_justicePursuitDeathObservedDuringSuspension)
                {
                    _justiceCaptureRetryPending = true;
                    _justiceCaptureRetryDeath = true;
                }
            }
            else if (!JusticeIsCustodyActive)
            {
                UpdateJusticeWantedEdges(wantedLevel);
            }

            if (!policePursuitDeath && HasActiveJusticeCase() && !JusticeIsCustodyActive)
            {
                if (arrestStateValid && arrested && !_justiceWasBeingArrested)
                {
                    _justiceArrestCompletionProbePending = false;
                    _justiceArrestCompletionProbeStartedAtMs = 0L;
                    ApplyJusticeTransition(JusticeSignal.ArrestStarted, CurrentJusticeEpisodeId());
                }

                bool completedArrest = false;
                bool completionStateValid = true;
                if (arrestStateValid && (arrested || _justiceWasBeingArrested))
                {
                    completionStateValid = TryGetJusticeArrestConfirmedSafe(out completedArrest);
                }
                if (completedArrest)
                {
                    if (!BeginJusticeCapture(false))
                    {
                        _justiceCaptureRetryPending = true;
                        _justiceCaptureRetryDeath = false;
                    }
                }
                else if (arrestStateValid && completionStateValid && !arrested &&
                         _justiceWasBeingArrested &&
                         _justiceCaseState.Phase == JusticePhase.Surrendering)
                {
                    // Je qualifie uniquement l'annulation d'une arrestation déjà
                    // commencée : une simple étoile ne fabrique jamais cette charge.
                    QueueJusticeDirectGameReport(JusticeCrimeKind.ResistingArrest, null);
                    ApplyJusticeTransition(JusticeSignal.ArrestCancelled, CurrentJusticeEpisodeId());
                }
                else if (arrestStateValid && !completionStateValid &&
                         !arrested && _justiceWasBeingArrested)
                {
                    if (!_justiceArrestCompletionProbePending)
                    {
                        // Je mémorise le front descendant : si la native de fin
                        // d'arrestation revient après son backoff, sa minuterie
                        // aura avancé d'autant et ne doit pas créer une résistance.
                        _justiceArrestCompletionProbePending = true;
                        _justiceArrestCompletionProbeStartedAtMs = _justiceMonotonicTimeMs;
                    }
                    preserveArrestLatch = true;
                }
            }
        }

        if (arrestStateValid && !preserveArrestLatch)
        {
            _justiceWasBeingArrested = arrested;
            if (!arrested && !_justiceArrestCompletionProbePending)
            {
                _justiceArrestCompletionProbeStartedAtMs = 0L;
            }
        }
        _justiceWasDead = dead;
        _justiceLastWantedLevel = wantedLevel;
    }

    private void ObserveJusticeFrontsWhilePersistenceBlocked(
        Ped player,
        int wantedLevel,
        bool dead,
        bool arrestStateValid,
        bool arrested)
    {
        JusticeDeferredRuntimeFront observed = JusticeDeferredRuntimeFront.None;
        if (dead && !_justiceWasDead)
        {
            observed |= JusticeDeferredRuntimeFront.DeathStarted;
        }
        if (arrestStateValid)
        {
            if (arrested && !_justiceWasBeingArrested)
            {
                observed |= JusticeDeferredRuntimeFront.ArrestStarted;
            }
            else if (!arrested && _justiceWasBeingArrested)
            {
                observed |= JusticeDeferredRuntimeFront.ArrestEnded;
            }
        }
        if (_justiceLastWantedLevel > 0 && wantedLevel == 0)
        {
            observed |= JusticeDeferredRuntimeFront.WantedLost;
        }
        else if (_justiceLastWantedLevel == 0 && wantedLevel > 0)
        {
            observed |= JusticeDeferredRuntimeFront.WantedRaised;
        }

        if (observed != JusticeDeferredRuntimeFront.None)
        {
            int observedSlot = GetCurrentSinglePlayerCashSlotSafe();
            if (observedSlot < 0)
            {
                observedSlot = _justiceLastCanonicalPlayerSlot;
            }
            int observedModel = GetJusticePedModelHashSafe(player);
            if (_justiceDeferredRuntimeFronts != JusticeDeferredRuntimeFront.None &&
                (_justiceDeferredRuntimeFrontPlayerSlot != observedSlot ||
                 _justiceDeferredRuntimeFrontPlayerModelHash != observedModel))
            {
                _justiceDeferredRuntimeFronts |=
                    JusticeDeferredRuntimeFront.IdentityChanged;
            }
            else
            {
                _justiceDeferredRuntimeFrontPlayerSlot = observedSlot;
                _justiceDeferredRuntimeFrontPlayerModelHash = observedModel;
            }
            _justiceDeferredRuntimeFronts |= observed;
            _justiceDeferredRuntimeFrontHadPursuit |=
                _justicePursuitActive || _justiceLastWantedLevel > 0;
        }

        if (arrestStateValid)
        {
            _justiceWasBeingArrested = arrested;
        }
        _justiceWasDead = dead;
        _justiceLastWantedLevel = wantedLevel;
        _justiceDamageFrontPrimingPending = true;
    }

    private void ReconcileJusticeFrontsAfterPersistenceRepair(
        Ped player,
        int wantedLevel)
    {
        JusticeDeferredRuntimeFront fronts = _justiceDeferredRuntimeFronts;
        if (fronts == JusticeDeferredRuntimeFront.None)
        {
            return;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        if (currentSlot < 0)
        {
            currentSlot = _justiceLastCanonicalPlayerSlot;
        }
        int currentModel = GetJusticePedModelHashSafe(player);
        bool identityCompatible =
            (fronts & JusticeDeferredRuntimeFront.IdentityChanged) == 0 &&
            IsJusticeCanonicalProfileSlot(currentSlot) &&
            currentSlot == _justiceDeferredRuntimeFrontPlayerSlot &&
            currentModel != 0 &&
            currentModel == _justiceDeferredRuntimeFrontPlayerModelHash;

        if (identityCompatible && _justiceEnabled && HasActiveJusticeCase() &&
            !JusticeIsCustodyActive)
        {
            if ((fronts & JusticeDeferredRuntimeFront.DeathStarted) != 0 &&
                _justiceDeferredRuntimeFrontHadPursuit)
            {
                if (_justiceCaseState.Phase == JusticePhase.AtLarge)
                {
                    _justiceCaseState.Phase = JusticePhase.Wanted;
                }
                _justicePursuitDeathObservedDuringSuspension = true;
                _justiceSuspendedPursuitDeathPlayerSlot = currentSlot;
                _justiceSuspendedPursuitDeathPlayerModelHash = currentModel;
                JusticeMarkStateDirty();
            }

            bool arrestWasObserved =
                (fronts & (JusticeDeferredRuntimeFront.ArrestStarted |
                           JusticeDeferredRuntimeFront.ArrestEnded)) != 0;
            if (arrestWasObserved)
            {
                // Je demande une preuve BUSTED avant toute capture. Si elle reste
                // illisible, le chemin borné existant conservera seulement le mandat.
                _justiceArrestCompletionProbePending = true;
                _justiceArrestCompletionProbeStartedAtMs = _justiceMonotonicTimeMs;
            }
            if ((fronts & JusticeDeferredRuntimeFront.WantedLost) != 0 ||
                (fronts & JusticeDeferredRuntimeFront.ArrestEnded) != 0)
            {
                _justiceWantedLossPending = true;
            }
        }
        else
        {
            LogWarning(
                "Justice.Reparation",
                "Fronts différés ignorés : identité du protagoniste ambiguë ou dossier inactif.");
        }

        _justiceDeferredRuntimeFronts = JusticeDeferredRuntimeFront.None;
        _justiceDeferredRuntimeFrontPlayerSlot = -1;
        _justiceDeferredRuntimeFrontPlayerModelHash = 0;
        _justiceDeferredRuntimeFrontHadPursuit = false;
        _justiceLastWantedLevel = wantedLevel;
    }

    private bool ObserveJusticeCriticalFrontsBeforeTransactionReturn(
        Ped player,
        int wantedLevel,
        bool dead,
        bool arrestStateValid,
        bool arrested,
        bool preserveActivePoliceCase)
    {
        if (!_justiceEnabled)
        {
            return false;
        }

        if (JusticeIsCustodyActive || _justiceLegalReleaseFinalizationPending)
        {
            if (dead && !_justiceWasDead &&
                IsJusticeCustodyDeathIdentityCompatible(player))
            {
                // Je rends le droit de rebind durable avant qu'un WAL de reset,
                // d'amnistie ou de libération puisse consommer le front de mort.
                ObserveJusticeCustodyDeathDuringSuspension(player);
            }
            return false;
        }

        if (!HasActiveJusticeCase())
        {
            return false;
        }

        if (!preserveActivePoliceCase)
        {
            return false;
        }

        bool changed = false;
        if (dead && !_justiceWasDead &&
            (_justicePursuitActive || _justiceLastWantedLevel > 0))
        {
            if (_justiceCaseState.Phase == JusticePhase.AtLarge)
            {
                _justiceCaseState.Phase = JusticePhase.Wanted;
            }
            _justicePursuitDeathObservedDuringSuspension = true;
            int observedSlot = GetCurrentSinglePlayerCashSlotSafe();
            _justiceSuspendedPursuitDeathPlayerSlot = observedSlot >= 0
                ? observedSlot
                : _justiceLastCanonicalPlayerSlot;
            _justiceSuspendedPursuitDeathPlayerModelHash =
                GetJusticePedModelHashSafe(player);
            changed = true;
        }

        bool preserveArrest = _justiceArrestCompletionProbePending;
        if (arrestStateValid && arrested)
        {
            if (!_justiceArrestCompletionProbePending)
            {
                _justiceArrestCompletionProbePending = true;
                _justiceArrestCompletionProbeStartedAtMs =
                    _justiceMonotonicTimeMs;
            }
            if (_justiceCaseState.Phase != JusticePhase.Surrendering)
            {
                ApplyJusticeTransition(
                    JusticeSignal.ArrestStarted,
                    CurrentJusticeEpisodeId());
            }
            _justiceWasBeingArrested = true;
            preserveArrest = true;
            changed = true;
        }

        if (_justiceLastWantedLevel > 0 && wantedLevel == 0)
        {
            if (preserveArrest || _justiceWasBeingArrested)
            {
                // La sonde BUSTED reste prioritaire : elle décidera entre
                // capture et mandat dès que le WAL financier sera terminé.
                _justiceWantedLossPending = true;
                preserveArrest = true;
            }
            else
            {
                _justiceWantedLossPending = true;
                ResolveDeferredJusticeWantedLoss(wantedLevel);
                changed = true;
            }
        }
        else if (wantedLevel > 0)
        {
            _justiceWantedLossPending = false;
        }

        if (changed)
        {
            JusticeMarkStateDirty();
            JusticeFlushStateNow();
        }
        return preserveArrest;
    }

    private void UpdateJusticeSystem()
    {
        if (!_justiceInitialized)
        {
            return;
        }

        Ped player = Game.Player.Character;
        int nowRaw = GetJusticeRawGameTimeSafe();
        if (_justiceBackupRepairPending)
        {
            // UpdateJusticeEarly porte seul le retry cadencé de réparation. Je
            // suspends ici même les reprises de détention afin qu'aucun effet
            // externe ne précède le retour d'une persistance fiable.
            AdvanceJusticeInactiveCustodyProfiles(nowRaw, true);
            return;
        }
        bool profileContextCompatible = !_justiceProfileContextBlocked &&
            IsJusticeRuntimeProfileContextCompatible();
        bool runtimeSuspended = IsJusticeRuntimeSuspended(player);
        if ((runtimeSuspended || !profileContextCompatible) &&
            (_justicePoliceSuppressionActive || _justicePoliceIgnoreApplied ||
             _justicePoliceDispatchDisabled))
        {
            // Je rends mes flags globaux avant de laisser une mission, une
            // cinématique ou un autre protagoniste prendre la main.
            SetJusticeCustodyPoliceSuppression(false);
        }
        AdvanceJusticeInactiveCustodyProfiles(
            nowRaw,
            runtimeSuspended || IsJusticePlayerDeadSafe(player) ||
            !profileContextCompatible || _justiceProfileSwitchPersistencePending ||
            _justicePoliceSuppressionRestorePending);

        RetryJusticePoliceSuppressionRestore(player, nowRaw);
        RetryJusticeDeferredInventoryRestore(player, nowRaw);

        if (profileContextCompatible &&
            _justiceLegalReleaseFinalizationPending &&
            !runtimeSuspended)
        {
            ResumeJusticeLegalReleaseFinalization(player, nowRaw);
        }

        bool legalReleasePending = _justiceLegalReleaseFinalizationPending;
        if (profileContextCompatible && !legalReleasePending &&
            (_justiceEnabled || HasJusticeCustodyRecoveryState()))
        {
            JusticeUpdateCustody(player, nowRaw);
        }

        bool suspended = runtimeSuspended ||
                         _justiceAmnestyPending ||
                         _justiceVoluntaryFinePaymentIntent != null ||
                         legalReleasePending ||
                         !profileContextCompatible;
        bool custodyActive = JusticeIsCustodyActive;
        bool processIncidents = _justiceEnabled && !_justiceAmnestyPending && !suspended &&
                                ShouldProcessJusticeRuntimeIncidents();

        if (_justiceEnabled && !suspended && !custodyActive)
        {
            RetryJusticeEscapeWantedMinimum(GetJusticeWantedLevelSafe());
        }

        if (_justiceEnabled && (suspended || custodyActive))
        {
            // Je ne lis que les latches scalaires pendant une suspension ou une
            // détention. Les scans du monde et CLEAR_DAMAGE attendent la reprise.
            SynchronizeJusticeScalarFronts(player, false);
            _justiceDamageFrontPrimingPending = true;
        }
        else if (_justiceEnabled && _justiceDamageFrontPrimingPending)
        {
            // Une unique passe photographie puis purge les historiques accumulés
            // avant de rouvrir la détection du jeu libre.
            PrimeJusticeEventFronts(player);
        }

        if (_justiceEnabled && !_justiceDamageFrontPrimingPending &&
            !suspended && !custodyActive && Entity.Exists(player) && !player.IsDead)
        {
            ResetJusticeWitnessSnapshots();
            DetectJusticeEventFronts(player);
            if (_justiceWantedRisePendingCorrelation)
            {
                CorrelateJusticeWantedRise();
            }
            if (processIncidents)
            {
                ProcessJusticePendingIncidents();
                ProcessJusticeRecentVictimUpgrades(player);
                ProcessJusticeRecentVehicleUpgrades(player);
            }
            ProcessJusticeAllyAttributionTokens(player);
            UpdateJusticeWarrantRecognition(player);
            UpdateJusticeEvadingPoliceCharge(player);
            AdvanceJusticeCleanRecord(true);
        }
        else
        {
            AdvanceJusticeCleanRecord(false);

            if (processIncidents)
            {
                ProcessJusticePendingIncidents();
            }
        }

        PersistJusticeStateIfDue();
    }

    private void UpdateJusticeFailSafeMaintenance()
    {
        if (!_justiceInitialized)
        {
            return;
        }

        Ped player = Game.Player.Character;
        int now = GetJusticeRawGameTimeSafe();
        RepairJusticeOrphanedCustodyControls(player);
        RetryJusticePoliceSuppressionRestore(player, now);
        RetryJusticeDeferredInventoryRestore(player, now);

        // Je ne fais progresser ni dossier, ni peine, ni détection ici. Seuls
        // les états déjà préparés et les restaurations de sécurité sont persistés.
        PersistJusticeStateIfDue();
    }

    private bool ShouldProcessJusticeRuntimeIncidents()
    {
        if (_justiceMonotonicTimeMs < _justiceNextIncidentProcessingAtMs)
        {
            return false;
        }

        _justiceNextIncidentProcessingAtMs =
            _justiceMonotonicTimeMs + JusticeIncidentProcessingIntervalMs;
        return true;
    }

    private void ShutdownJusticeSystem()
    {
        if (!_justiceInitialized)
        {
            return;
        }

        try
        {
            JusticeShutdownCustody();
        }
        catch (Exception ex)
        {
            LogException("Justice.ArretDetention", ex);
        }

        try
        {
            SnapshotActiveJusticePlayerProfile();
            if (!JusticeFlushStateNow())
            {
                LogWarning(
                    "Justice.Arret",
                    "La sauvegarde finale Justice a échoué; le WAL et l'état sale restent récupérables.");
            }
        }
        catch (Exception ex)
        {
            LogException("Justice.ArretSauvegarde", ex);
        }
        try
        {
            ShutdownJusticePersistenceServices();
        }
        catch (Exception ex)
        {
            LogException("Justice.ArretRepository", ex);
        }
        try
        {
            FlushJusticeConsumedDamageFronts();
        }
        catch (Exception ex)
        {
            LogException("Justice.ArretDegats", ex);
        }
        _justicePendingIncidents.Clear();
        _justiceRecentVictims.Clear();
        _justiceRecentVehicles.Clear();
        _justiceAllyTokens.Clear();
        _justiceTrackedIdentities.Clear();
        _justiceSelfDefenseUntilByVictim.Clear();
        _justiceSelfDefenseThreatByVictim.Clear();
        _justiceWitnessSnapshotCount = 0;
        _justiceDamagePairBaselineCount = 0;
        _justiceDamagePairReplacementIndex = 0;
        _justiceDeferredRuntimeFronts = JusticeDeferredRuntimeFront.None;
        _justiceDeferredRuntimeFrontPlayerSlot = -1;
        _justiceDeferredRuntimeFrontPlayerModelHash = 0;
        _justiceDeferredRuntimeFrontHadPursuit = false;
        _justiceDamageFrontPrimingPending = false;
        _justiceDeathDetectionBarrierInitialized = false;
        _justiceAimTargetHandle = 0;
        _justiceAimTargetGeneration = 0;
        _justiceAimStartedAtMs = 0L;
        _justiceAimThreatQueued = false;
        _justiceWantedLossPending = false;
        _justiceCaptureRetryPending = false;
        _justiceCaptureRetryDeath = false;
        _justicePursuitDeathObservedDuringSuspension = false;
        _justiceSuspendedPursuitDeathPlayerSlot = -1;
        _justiceSuspendedPursuitDeathPlayerModelHash = 0;
        _justiceArrestCompletionProbePending = false;
        _justiceArrestCompletionProbeStartedAtMs = 0L;
        _justiceWantedRisePendingCorrelation = false;
        _justiceWantedRiseObservedAtMs = 0L;
        _justiceWantedRiseDetectionPass = 0;
        _justiceInitialized = false;
    }

    private bool IsPendingJusticeDeathCaptureIdentityCompatible(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int currentModelHash = GetJusticePedModelHashSafe(player);
        // Le slot GTA reste l'identité forte. Sans slot préexistant, je refuse
        // d'adopter arbitrairement le premier protagoniste chargé au respawn.
        return JusticePolicy.IsCanonicalPlayerIdentityCompatible(
            _justiceSuspendedPursuitDeathPlayerSlot,
            _justiceLastCanonicalPlayerSlot,
            _justiceSuspendedPursuitDeathPlayerModelHash,
            currentSlot,
            currentModelHash);
    }

    private bool ShouldConvertUnknownJusticeDeathCaptureToWarrant(Ped player)
    {
        if (!Entity.Exists(player) ||
            _justiceSuspendedPursuitDeathPlayerSlot >= 0 ||
            _justiceLastCanonicalPlayerSlot >= 0)
        {
            return false;
        }

        // Le retour d'un héros canonique ne prouve pas qu'il était le ped custom
        // décédé. Je conserve donc l'affaire sous mandat, sans jugement ni saisie.
        return GetCurrentSinglePlayerCashSlotSafe() >= 0;
    }

    private void FinalizeUnknownJusticeCaptureAsWarrant(string logMessage)
    {
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        _justiceAllyTokens.Clear();
        _justiceCaseState.HasWarrant = true;
        if (_justiceCaseState.Phase == JusticePhase.Wanted ||
            _justiceCaseState.Phase == JusticePhase.Surrendering)
        {
            _justiceCaseState.Phase = JusticePhase.AtLarge;
        }
        _justiceCaptureRetryPending = false;
        _justiceCaptureRetryDeath = false;
        ClearPendingJusticeDeathCapture();
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        ShowStatus(
            "Justice : identité du suspect non prouvée, dossier conservé sous mandat.",
            4200);
        LogWarning(
            "Justice.Capture",
            logMessage);
    }

    private void ObserveJusticeCanonicalPlayerIdentity(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            _justicePursuitDeathObservedDuringSuspension || JusticeIsCustodyActive)
        {
            return;
        }

        int slot = GetCurrentSinglePlayerCashSlotSafe();
        if (slot < 0 || slot > 2 || slot != _justiceActivePlayerProfileSlot)
        {
            return;
        }

        int modelHash = GetJusticePedModelHashSafe(player);
        if (_justiceLastCanonicalPlayerSlot == slot &&
            (_justiceLastCanonicalPlayerModelHash == modelHash || modelHash == 0))
        {
            return;
        }

        _justiceLastCanonicalPlayerSlot = slot;
        if (modelHash != 0)
        {
            _justiceLastCanonicalPlayerModelHash = modelHash;
        }
        JusticeMarkStateDirty();
    }

    private void ClearPendingJusticeDeathCapture()
    {
        _justicePursuitDeathObservedDuringSuspension = false;
        _justiceSuspendedPursuitDeathPlayerSlot = -1;
        _justiceSuspendedPursuitDeathPlayerModelHash = 0;
    }

    private bool HandleJusticeWorldKey(Keys key)
    {
        if (!_justiceInitialized || !_justiceEnabled || !JusticeIsCustodyActive)
        {
            return false;
        }

        return JusticeHandleCustodyWorldKey(key);
    }

    private void RequestJusticeToggle()
    {
        if (!_justiceInitialized)
        {
            return;
        }

        if (!IsJusticePlayedProfileContextReady())
        {
            ShowStatus(
                "Justice : identification ou changement de personnage en cours.",
                3600);
            return;
        }

        if (_justiceAmnestyPending)
        {
            ResumeJusticeAmnestyTransaction();
            ShowStatus("Amnistie déjà engagée : finalisation sécurisée en cours…", 3200);
            return;
        }

        if (_justiceLegalReleaseFinalizationPending)
        {
            ResumeJusticeLegalReleaseFinalization(
                Game.Player.Character,
                GetJusticeRawGameTimeSafe());
            ShowStatus(
                "Justice : termine d'abord la libération en cours avant de changer l'activation.",
                4000);
            return;
        }

        if (!_justiceEnabled && HasJusticeCustodyRecoveryState())
        {
            if (_justiceLegalReleaseFinalizationPending)
            {
                ResumeJusticeLegalReleaseFinalization(
                    Game.Player.Character,
                    GetJusticeRawGameTimeSafe());
            }
            ShowStatus(
                "Justice : restauration de sécurité en cours avant réactivation.",
                3800);
            return;
        }

        if (!_justiceEnabled)
        {
            bool previousEnabled = _justiceEnabled;
            bool previousCaseEnabled = _justiceCaseState.Enabled;
            _justiceEnabled = true;
            _justiceCaseState.Enabled = true;
            JusticeMarkStateDirty();

            if (!JusticeFlushStateNow())
            {
                // Je restaure l'état visible tant que son commit atomique n'a pas
                // abouti. Les fronts et le wanted restent donc strictement intacts.
                _justiceEnabled = previousEnabled;
                _justiceCaseState.Enabled = previousCaseEnabled;
                JusticeMarkStateDirty();
                ShowStatus(
                    "Activation impossible : sauvegarde Justice indisponible, aucun changement appliqué.",
                    4200);
                LogWarning("Justice", "Activation refusée faute de commit durable.");
                return;
            }

            CancelJusticeWantedClearRetry();
            _justiceDamagePairBaselineCount = 0;
            _justiceDamagePairReplacementIndex = 0;
            _justiceDamageFrontPrimingPending = true;
            CancelJusticeAmnestyConfirmation();
            ShowStatus("Justice avancée ACTIVÉE. Seuls les faits vus ou signalés seront retenus.", 5200);
            LogInfo("Justice", "Systeme active par le joueur.");
            return;
        }

        if (!HasActiveJusticeCase() && HasJusticeCustodyRecoveryState())
        {
            if (_justiceLegalReleaseFinalizationPending)
            {
                ResumeJusticeLegalReleaseFinalization(
                    Game.Player.Character,
                    GetJusticeRawGameTimeSafe());
            }
            ShowStatus(
                "Désactivation différée : la libération et l'inventaire doivent d'abord être acquittés.",
                4200);
            return;
        }

        if (!HasActiveJusticeCase() && !JusticeIsCustodyActive)
        {
            DisableJusticeWithoutAmnesty();
            return;
        }

        // Je réutilise la confirmation Obsidienne et son verrou de relâchement
        // Entrée/Num5 : aucun auto-repeat ne peut donc valider une amnistie.
        RequestDangerConfirmation(MainMenuAction.JusticeEnabled);
    }

    private void DisableJusticeWithoutAmnesty()
    {
        bool previousEnabled = _justiceEnabled;
        bool previousCaseEnabled = _justiceCaseState.Enabled;
        _justiceEnabled = false;
        _justiceCaseState.Enabled = false;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            // Je ne nettoie aucun incident ni cache avant que la désactivation
            // soit durable : un échec disque laisse le système exactement actif.
            _justiceEnabled = previousEnabled;
            _justiceCaseState.Enabled = previousCaseEnabled;
            JusticeMarkStateDirty();
            ShowStatus(
                "Désactivation impossible : sauvegarde Justice indisponible, aucun changement appliqué.",
                4200);
            LogWarning("Justice", "Désactivation refusée faute de commit durable.");
            return;
        }

        _justiceDamageFrontPrimingPending = false;
        _justiceDeathDetectionBarrierInitialized = false;
        _justiceDamagePairBaselineCount = 0;
        _justiceAimTargetHandle = 0;
        _justiceAimTargetGeneration = 0;
        _justiceAimStartedAtMs = 0L;
        _justiceAimThreatQueued = false;
        _justiceWantedLossPending = false;
        _justiceCaptureRetryPending = false;
        _justiceCaptureRetryDeath = false;
        _justicePendingIncidents.Clear();
        _justiceRecentVictims.Clear();
        _justiceRecentVehicles.Clear();
        _justiceAllyTokens.Clear();
        ClearLatchedJusticeWantedRise();
        CancelJusticeAmnestyConfirmation();
        ShowStatus("Justice avancée DÉSACTIVÉE. Le casier purgé reste mémorisé.", 4200);
        LogInfo("Justice", "Systeme desactive sans dossier actif.");
    }

    private void ExecuteJusticeConfirmedAmnestyAndDisable(
        int requestedProfileSlot,
        int expectedPlayerHandle,
        int expectedPlayerModelHash)
    {
        if (!IsJusticeDangerActionProfileContextValid(
                requestedProfileSlot,
                expectedPlayerHandle,
                expectedPlayerModelHash))
        {
            // Je revalide l'identité au second Entrée : un changement de héros
            // entre l'ouverture et la confirmation ne peut jamais amnistier
            // l'ancien dossier tout en effaçant le wanted du nouveau personnage.
            CancelJusticeAmnestyConfirmation();
            ShowStatus(
                "Amnistie annulée : le personnage actif a changé.",
                3800);
            return;
        }

        ExecuteJusticeAmnestyAndDisable();
    }

    private void ExecuteJusticeAmnestyAndDisable()
    {
        if (!_justiceAmnestyPending)
        {
            // Je rends l'intention durable avant l'inventaire, le dossier et le
            // wanted. Une reprise peut ainsi terminer l'amnistie sans ambiguïté.
            _justiceAmnestyPending = true;
            _justiceAmnestyWantedClearAttempted = false;
            _justiceAmnestyPrecommitRedundant = false;
            if (!EnsureJusticeAmnestyPrecommitRedundant())
            {
                CancelJusticeAmnestyConfirmation();
                ShowStatus(
                    "Amnistie en attente : sauvegarde redondante à reprendre, aucun effet appliqué.",
                    4400);
                return;
            }
        }

        CancelJusticeAmnestyConfirmation();
        ResumeJusticeAmnestyTransaction();
    }

    private bool EnsureJusticeAmnestyPrecommitRedundant()
    {
        if (!_justiceAmnestyPending)
        {
            return false;
        }
        if (_justiceAmnestyPrecommitRedundant)
        {
            return true;
        }

        // Je ne peux pas distinguer un échec avant le primaire d'un échec de la
        // seconde écriture. Je garde donc l'intention et je la réaffirme sans
        // effet GTA jusqu'à ce que primaire et backup soient tous deux durables.
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        _justiceAmnestyPrecommitRedundant = true;
        return true;
    }

    private bool ResumeJusticeAmnestyTransaction()
    {
        if (!_justiceAmnestyPending)
        {
            return true;
        }
        if (!EnsureJusticeAmnestyPrecommitRedundant())
        {
            return false;
        }

        if (_justiceFineDebitIntent != null)
        {
            if (!ResumeJusticeFineDebitIntent() ||
                _justiceFineDebitIntent != null)
            {
                ShowStatus(
                    "Amnistie différée : transaction financière de capture à réconcilier.",
                    4000);
                return false;
            }
        }
        if (_justiceVoluntaryFinePaymentIntent != null)
        {
            if (!ResumeJusticeVoluntaryFinePayment() ||
                _justiceVoluntaryFinePaymentIntent != null)
            {
                ShowStatus(
                    "Amnistie différée : paiement volontaire à réconcilier.",
                    4000);
                return false;
            }
        }

        if (JusticePolicy.IsWantedOnlyRepairRecovery(
            _justiceEnabled,
            HasActiveJusticeCase(),
            JusticeIsCustodyActive))
        {
            return ResumeJusticeWantedOnlyRepair();
        }

        try
        {
            if (!JusticeAmnestyCustody())
            {
                ShowStatus("Amnistie différée : restitution de l'inventaire en cours.", 3800);
                return false;
            }
        }
        catch (Exception ex)
        {
            LogException("Justice.AmnistieDetention", ex);
            return false;
        }

        if (_justiceEnabled || HasActiveJusticeCase())
        {
            _justiceCaseState.ClearActiveCase(false);
            _justiceCaseState.Enabled = false;
            _justiceEnabled = false;
            _justiceRecordState.PinnedConvictionId = string.Empty;
            _justiceDamageFrontPrimingPending = false;
            _justiceDeathDetectionBarrierInitialized = false;
            _justiceDamagePairBaselineCount = 0;
            _justiceAimTargetHandle = 0;
            _justiceAimTargetGeneration = 0;
            _justiceAimStartedAtMs = 0L;
            _justiceAimThreatQueued = false;
            _justiceWantedLossPending = false;
            _justiceCaptureRetryPending = false;
            _justiceCaptureRetryDeath = false;
            _justicePursuitActive = false;
            _justiceWantedEpisodeStartedAtMs = 0L;
            _justiceDetectionEpisodeId = string.Empty;
            _justicePendingIncidents.Clear();
            _justiceRecentVictims.Clear();
            _justiceRecentVehicles.Clear();
            _justiceAllyTokens.Clear();
            _justiceWantedRisePendingCorrelation = false;
        }

        // Je persiste d'abord le dossier vide tout en gardant le précommit. Un
        // crash à partir d'ici reprendra seulement l'effacement idempotent du wanted.
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            ShowStatus("Amnistie préparée; sauvegarde finale à reprendre…", 3800);
            return false;
        }

        if (!TryApplyJusticeAmnestyWantedClear())
        {
            return false;
        }

        _justiceAmnestyPending = false;
        _justiceAmnestyWantedClearAttempted = false;
        JusticeMarkStateDirty();
        if (JusticeFlushStateNow())
        {
            _justiceAmnestyPrecommitRedundant = false;
            ShowStatus("Amnistie confirmée : dossier actif effacé, casier historique conservé.", 5200);
            LogInfo("Justice", "Amnistie confirmee et systeme desactive.");
        }
        else
        {
            // Le disque conserve le précommit « clear déjà essayé ». Je restaure
            // donc les deux latches en mémoire sans répéter la native GTA.
            _justiceAmnestyPending = true;
            _justiceAmnestyWantedClearAttempted = true;
            JusticeMarkStateDirty();
            ShowStatus("Amnistie appliquée; acquittement du précommit en cours…", 4200);
            return false;
        }
        return true;
    }

    private bool ResumeJusticeWantedOnlyRepair()
    {
        if (!TryApplyJusticeAmnestyWantedClear())
        {
            return false;
        }

        // Le dossier a déjà été annulé hors ligne et le casier est intact. Je
        // acquitte seulement le jeton d'effacement wanted, sans désactiver Justice.
        _justiceAmnestyPending = false;
        _justiceAmnestyWantedClearAttempted = false;
        JusticeMarkStateDirty();
        bool persisted = JusticeFlushStateNow();
        if (!persisted)
        {
            _justiceAmnestyPending = true;
            _justiceAmnestyWantedClearAttempted = true;
            JusticeMarkStateDirty();
        }
        else
        {
            _justiceAmnestyPrecommitRedundant = false;
        }
        ShowStatus(
            persisted
                ? "Justice : dossier bloqué annulé, système conservé actif."
                : "Justice : dossier annulé; acquittement de la réparation à reprendre.",
            4800);
        LogInfo(
            "Justice.Reparation",
            persisted
                ? "Effacement wanted de réparation terminé; Justice reste active."
                : "Effacement wanted appliqué; acquittement XML différé.");
        return persisted;
    }

    private bool TryApplyJusticeAmnestyWantedClear()
    {
        if (_justiceAmnestyWantedClearAttempted)
        {
            // Le précommit prouve qu'un appel a pu avoir lieu. Une reprise ne
            // touche plus au wanted et passe directement à l'acquittement XML.
            return true;
        }
        if (_justiceWantedClearPending &&
            _justiceMonotonicTimeMs < _justiceNextWantedClearRetryAtMs)
        {
            return false;
        }

        _justiceAmnestyWantedClearAttempted = true;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceAmnestyWantedClearAttempted = false;
            JusticeMarkStateDirty();
            return false;
        }

        JusticeWantedClearResult result = ClearJusticeWantedLevelOnceDetailed();
        if (result == JusticeWantedClearResult.Rejected)
        {
            LogWarning(
                "Justice.Amnistie",
                "Wanted GTA resté non nul après l'unique tentative; aucun retry tardif ne sera appliqué.");
        }

        _justiceWantedClearPending = false;
        _justiceNextWantedClearRetryAtMs = 0L;
        _justiceWantedClearRetryUntilMs = 0L;
        if (result == JusticeWantedClearResult.Unknown)
        {
            LogWarning(
                "Justice.Amnistie",
                "Résultat wanted ambigu; acquittement at-most-once sans nouvelle écriture GTA.");
        }
        return true;
    }

    private void CancelJusticeAmnestyConfirmation()
    {
        if (_pendingDangerAction == MainMenuAction.JusticeEnabled)
        {
            CancelPendingDangerAction();
        }
    }

    private bool HasActiveJusticeCase()
    {
        return _justiceCaseState != null &&
               (_justiceCaseState.Charges.Count > 0 ||
                _justiceCaseState.ActiveScore > 0 ||
                _justiceCaseState.FineDue > 0L ||
                _justiceCaseState.SentenceSeconds > 0 ||
                _justiceCaseState.HasWarrant);
    }

    private void DetectJusticeEventFronts(Ped player)
    {
        if (_justiceMonotonicTimeMs < _justiceNextFrontScanAtMs)
        {
            return;
        }
        long metricStartedAt = BeginJusticeMetric();
        _justiceNextFrontScanAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs;
        CaptureJusticeWorldSnapshot(player);
        _justiceEventDetectionPass++;
        if (_justiceEventDetectionPass <= 0)
        {
            _justiceEventDetectionPass = 1;
        }

        bool shooting = IsPedShooting(player);
        bool melee = IsPedInMeleeCombatSafe(player);
        bool inCombat = IsJusticePedInCombatSafe(player);
        bool jacking = IsJusticePedJackingSafe(player);
        bool spottedInStolenVehicle = IsJusticePlayerSpottedInStolenVehicleSafe();
        bool hitPedRecently = IsJusticeRecentNativeTimer(
            JusticeNativeGetTimeSincePlayerHitPed,
            JusticeCircuitHitPedTimer,
            260);
        bool hitVehicleRecently = IsJusticeRecentNativeTimer(
            JusticeNativeGetTimeSincePlayerHitVehicle,
            JusticeCircuitHitVehicleTimer,
            260);
        bool playerVitalityDropped = CaptureJusticePlayerVitalityDrop(player);

        if (ShouldKeepJusticeCrimeScanOpen(
                shooting,
                _justiceWasShooting,
                melee,
                inCombat,
                _justiceWasInCombat,
                hitPedRecently,
                hitVehicleRecently,
                playerVitalityDropped))
        {
            _justiceCrimeScanUntilMs = Math.Max(_justiceCrimeScanUntilMs, _justiceMonotonicTimeMs + JusticeCrimeScanWindowMs);
        }

        if (shooting && !_justiceWasShooting)
        {
            _justiceIncidentSequence++;
            _justiceActiveDischargeCausalId = "discharge:" + _justiceSessionId + ":" +
                _justiceIncidentSequence.ToString(CultureInfo.InvariantCulture);
            _justiceActiveDischargeExpiresAtMs =
                _justiceMonotonicTimeMs + JusticePolicy.PendingIncidentLifetimeMs;
            QueueJusticeIncident(
                JusticeCrimeKind.RecklessDischarge,
                null,
                null,
                0,
                GetJusticeWeaponCircumstances(player),
                false,
                0,
                causalEventId: _justiceActiveDischargeCausalId);
        }

        if (jacking && !_justiceWasJacking)
        {
            Ped jackTarget = GetJusticeJackTargetSafe(player);
            if (Entity.Exists(jackTarget))
            {
                QueueJusticeIncident(
                    JusticeCrimeKind.Carjacking,
                    jackTarget,
                    jackTarget,
                    0,
                    GetJusticeBaseCircumstances() | GetJusticeWeaponCircumstances(player),
                    false,
                    0);
            }
        }

        if (spottedInStolenVehicle && !_justiceWasSpottedInStolenVehicle)
        {
            QueueJusticeDirectGameReport(JusticeCrimeKind.VehicleTheft, GetJusticeCurrentVehicleSafe(player));
        }

        UpdateJusticeArmedThreatFront(player);

        if (_justiceMonotonicTimeMs <= _justiceCrimeScanUntilMs &&
            _justiceMonotonicTimeMs >= _justiceNextCrimeScanAtMs)
        {
            _justiceNextCrimeScanAtMs = _justiceMonotonicTimeMs + JusticeCrimeScanIntervalMs;
            ScanJusticeEventVictims(
                player,
                hitPedRecently,
                hitVehicleRecently,
                playerVitalityDropped,
                shooting);
        }

        _justiceWasShooting = shooting;
        _justiceWasInMelee = melee;
        _justiceWasInCombat = inCombat;
        _justiceWasJacking = jacking;
        _justiceWasSpottedInStolenVehicle = spottedInStolenVehicle;
        CompleteJusticeMetric(_justiceCrimeDetectionMetrics, metricStartedAt);
    }

    private static bool ShouldKeepJusticeCrimeScanOpen(
        bool shooting,
        bool wasShooting,
        bool melee,
        bool inCombat,
        bool wasInCombat,
        bool hitPedRecently,
        bool hitVehicleRecently,
        bool playerVitalityDropped)
    {
        // Je garde le scan actif pendant tout le combat rapproché. Un homicide
        // au couteau peut arriver bien après le premier coup et la native du
        // timer de dégâts peut être momentanément indisponible ; l'ancien front
        // de 1,6 seconde ratait alors précisément la mort de la victime.
        return (shooting && !wasShooting) ||
               melee ||
               (inCombat && !wasInCombat) ||
               hitPedRecently ||
               hitVehicleRecently ||
               playerVitalityDropped;
    }

    private void SynchronizeJusticeEventFronts(Ped player, bool force)
    {
        if (!force && _justiceMonotonicTimeMs < _justiceNextFrontScanAtMs)
        {
            return;
        }
        _justiceNextFrontScanAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs;

        SynchronizeJusticeScalarFrontsCore(player);
        CaptureJusticeWorldSnapshot(player);
        SynchronizeJusticeDamageFronts(player);
    }

    private void SynchronizeJusticeScalarFronts(Ped player, bool force)
    {
        if (!force && _justiceMonotonicTimeMs < _justiceNextFrontScanAtMs)
        {
            return;
        }
        _justiceNextFrontScanAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs;

        SynchronizeJusticeScalarFrontsCore(player);
    }

    private void SynchronizeJusticeScalarFrontsCore(Ped player)
    {

        if (!Entity.Exists(player))
        {
            _justiceWasShooting = false;
            _justiceWasInMelee = false;
            _justiceWasInCombat = false;
            _justiceWasJacking = false;
            _justiceWasSpottedInStolenVehicle = false;
            _justicePlayerVitalityBaselineInitialized = false;
        }
        else
        {
            _justiceWasShooting = IsPedShooting(player);
            _justiceWasInMelee = IsPedInMeleeCombatSafe(player);
            _justiceWasInCombat = IsJusticePedInCombatSafe(player);
            _justiceWasJacking = IsJusticePedJackingSafe(player);
            _justiceWasSpottedInStolenVehicle = IsJusticePlayerSpottedInStolenVehicleSafe();
            SynchronizeJusticePlayerVitalityBaseline(player);
        }

        _justiceAimTargetHandle = 0;
        _justiceAimTargetGeneration = 0;
        _justiceAimStartedAtMs = 0L;
        _justiceAimThreatQueued = false;
        _justiceActiveDischargeCausalId = string.Empty;
        _justiceActiveDischargeExpiresAtMs = 0L;
        _justiceCrimeScanUntilMs = 0L;
        _justiceRecentVictims.Clear();
        _justiceRecentVehicles.Clear();
        ResetJusticeWitnessSnapshots();
    }

    private bool CaptureJusticePlayerVitalityDrop(Ped player)
    {
        int currentHealth;
        int currentArmor;
        if (!TryReadJusticePlayerVitality(player, out currentHealth, out currentArmor))
        {
            _justicePlayerVitalityBaselineInitialized = false;
            return false;
        }

        bool dropped = _justicePlayerVitalityBaselineInitialized &&
            DidJusticePlayerVitalityDrop(
                _justicePlayerHealthBaseline,
                _justicePlayerArmorBaseline,
                currentHealth,
                currentArmor);
        _justicePlayerHealthBaseline = currentHealth;
        _justicePlayerArmorBaseline = currentArmor;
        _justicePlayerVitalityBaselineInitialized = true;
        return dropped;
    }

    private void SynchronizeJusticePlayerVitalityBaseline(Ped player)
    {
        int health;
        int armor;
        _justicePlayerVitalityBaselineInitialized =
            TryReadJusticePlayerVitality(player, out health, out armor);
        if (_justicePlayerVitalityBaselineInitialized)
        {
            _justicePlayerHealthBaseline = health;
            _justicePlayerArmorBaseline = armor;
        }
    }

    private static bool TryReadJusticePlayerVitality(
        Ped player,
        out int health,
        out int armor)
    {
        health = 0;
        armor = 0;
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            health = Math.Max(0, player.Health);
            armor = Math.Max(0, player.Armor);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DidJusticePlayerVitalityDrop(
        int previousHealth,
        int previousArmor,
        int currentHealth,
        int currentArmor)
    {
        return currentHealth < previousHealth || currentArmor < previousArmor;
    }

    private void PrimeJusticeEventFronts(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        if (!TryRecoverJusticeClearDamageCircuit(player))
        {
            return;
        }

        // Je photographie puis je purge les dégâts historiques avant d'autoriser
        // la première détection d'une session chargée ou d'une réactivation.
        SynchronizeJusticeEventFronts(player, true);
        FlushJusticeConsumedDamageFronts();
        _justiceDeathDetectionBarrierAtRawGameTime = GetJusticeRawGameTimeSafe();
        _justiceDeathDetectionBarrierInitialized = true;
        _justiceDamageFrontPrimingPending = false;
    }

    private void ResetJusticeWitnessSnapshots()
    {
        _justiceWitnessSnapshotCount = 0;
    }

    private void PrewarmJusticeRuntimeBuffers()
    {
        while (_justiceWitnessSnapshots.Count < JusticeMaximumWitnessActorSnapshots)
        {
            _justiceWitnessSnapshots.Add(new JusticeActorWitnessSnapshot
            {
                Candidates = new Ped[JusticeMaximumWitnessesPerEvent]
            });
        }

        while (_justiceDamageFrontsToConsume.Count < JusticeMaximumDamageFrontsPerTick)
        {
            _justiceDamageFrontsToConsume.Add(new JusticeDamageFrontConsumption());
        }

        while (_justiceDamagePairBaselines.Count < JusticeMaximumDamagePairBaselines)
        {
            _justiceDamagePairBaselines.Add(new JusticeDamagePairBaseline());
        }

        _justiceWitnessSnapshotCount = 0;
        _justiceDamageFrontCount = 0;
        _justiceDamagePairBaselineCount = 0;
        _justiceDamagePairReplacementIndex = 0;
    }

    private Ped[] GetJusticeWitnessCandidatesForActor(Ped actor)
    {
        if (!Entity.Exists(actor))
        {
            return JusticeEmptyPedCandidates;
        }

        int generation = GetJusticeEntityGeneration(actor);
        for (int index = 0; index < _justiceWitnessSnapshotCount; index++)
        {
            JusticeActorWitnessSnapshot snapshot = _justiceWitnessSnapshots[index];
            if (snapshot != null && snapshot.Actor != null &&
                snapshot.Actor.Handle == actor.Handle && snapshot.Generation == generation)
            {
                return snapshot.Candidates ?? JusticeEmptyPedCandidates;
            }
        }

        if (_justiceWitnessSnapshotCount >= JusticeMaximumWitnessActorSnapshots)
        {
            return JusticeEmptyPedCandidates;
        }

        Ped[] nearby = GetJusticeSnapshotPeds();
        JusticeActorWitnessSnapshot target;
        if (_justiceWitnessSnapshotCount < _justiceWitnessSnapshots.Count)
        {
            target = _justiceWitnessSnapshots[_justiceWitnessSnapshotCount];
        }
        else
        {
            target = new JusticeActorWitnessSnapshot();
            _justiceWitnessSnapshots.Add(target);
        }

        if (target.Candidates == null || target.Candidates.Length != JusticeMaximumWitnessesPerEvent)
        {
            target.Candidates = new Ped[JusticeMaximumWitnessesPerEvent];
        }
        else
        {
            Array.Clear(target.Candidates, 0, target.Candidates.Length);
        }

        int candidateCount = 0;
        int victimCount = 0;
        for (int pass = 0; pass < 3 && candidateCount < JusticeMaximumWitnessesPerEvent; pass++)
        {
            // Je réserve quelques places aux victimes mortes, puis aux policiers
            // vivants et enfin aux autres témoins. Une foule ne peut ainsi plus
            // évincer précisément la victime dont l'homicide vient d'avoir lieu.
            for (int index = 0;
                 index < nearby.Length && candidateCount < JusticeMaximumWitnessesPerEvent;
                 index++)
            {
                Ped candidate = nearby[index];
                if (!IsJusticeSnapshotEntityWithin(candidate, actor, JusticeWitnessRadius) ||
                    !IsJusticePotentialVictimCandidate(candidate, actor))
                {
                    continue;
                }

                bool dead;
                try
                {
                    dead = candidate.IsDead;
                }
                catch
                {
                    continue;
                }

                bool selected = pass == 0
                    ? dead && victimCount < JusticeMaximumVictimCandidatesPerEvent
                    : pass == 1
                        ? !dead && IsJusticePolicePed(candidate)
                        : !dead && !IsJusticePolicePed(candidate);
                if (!selected)
                {
                    continue;
                }
                target.Candidates[candidateCount++] = candidate;
                if (dead)
                {
                    victimCount++;
                }
            }
        }

        target.Actor = actor;
        target.Generation = generation;
        _justiceWitnessSnapshotCount++;
        return target.Candidates;
    }

    private void SynchronizeJusticeDamageFronts(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        Ped[] nearbyPeds = GetJusticeSnapshotPeds();
        int humans = 0;
        Vehicle playerVehicle = GetJusticeCurrentVehicleSafe(player);
        Vehicle lastVehicle = GetJusticeLastVehicleSafe(player);
        for (int index = 0; index < nearbyPeds.Length && humans < JusticeMaximumWitnessesPerEvent; index++)
        {
            Ped candidate = nearbyPeds[index];
            if (!IsJusticeSnapshotEntityWithin(candidate, player, JusticeWitnessRadius) ||
                !IsJusticePotentialVictimCandidate(candidate, player))
            {
                continue;
            }

            humans++;
            SynchronizeJusticeDamagePair(player, candidate);
            SynchronizeJusticeDamagePair(candidate, player);
            if (Entity.Exists(playerVehicle))
            {
                SynchronizeJusticeDamagePair(candidate, playerVehicle);
            }
            if (Entity.Exists(lastVehicle) &&
                (!Entity.Exists(playerVehicle) || lastVehicle.Handle != playerVehicle.Handle))
            {
                SynchronizeJusticeDamagePair(candidate, lastVehicle);
            }
        }

        Vehicle[] vehicles = GetJusticeSnapshotVehicles();

        int vehicleCount = Math.Min(vehicles.Length, JusticeMaximumVehiclesPerEvent);
        for (int index = 0; index < vehicleCount; index++)
        {
            Vehicle vehicle = vehicles[index];
            if (IsJusticeSnapshotEntityWithin(vehicle, player, JusticeWitnessRadius) &&
                (!Entity.Exists(playerVehicle) || vehicle.Handle != playerVehicle.Handle) &&
                (!Entity.Exists(lastVehicle) || vehicle.Handle != lastVehicle.Handle))
            {
                SynchronizeJusticeDamagePair(vehicle, player);
                if (Entity.Exists(playerVehicle))
                {
                    SynchronizeJusticeDamagePair(vehicle, playerVehicle);
                }
                if (Entity.Exists(lastVehicle) &&
                    (!Entity.Exists(playerVehicle) || lastVehicle.Handle != playerVehicle.Handle))
                {
                    SynchronizeJusticeDamagePair(vehicle, lastVehicle);
                }
            }
        }

        for (int index = 0; index < _justiceAllyTokens.Count; index++)
        {
            JusticeAllyCausalToken token = _justiceAllyTokens[index];
            if (token != null && Entity.Exists(token.Ally) && Entity.Exists(token.PoliceTarget))
            {
                SynchronizeJusticeDamagePair(token.PoliceTarget, token.Ally);
            }
        }
    }

    private bool TryCaptureJusticePlayerAttackFront(
        Ped player,
        Ped victim,
        bool acceptUnbaselinedDirectDamage,
        out bool directPlayerDamage,
        out bool vehicleWasWeapon)
    {
        directPlayerDamage = false;
        vehicleWasWeapon = false;
        if (!Entity.Exists(player) || !Entity.Exists(victim))
        {
            return false;
        }

        directPlayerDamage = TryCaptureJusticeDamageFront(
            victim,
            player,
            acceptUnbaselinedDirectDamage);
        Vehicle playerVehicle = GetJusticeCurrentVehicleSafe(player);
        Vehicle lastVehicle = GetJusticeLastVehicleSafe(player);
        bool explicitCurrentVehicleContact = IsJusticeVehicleAttack(player, victim);
        bool currentVehicleDamage = explicitCurrentVehicleContact &&
                             TryCaptureJusticeDamageFront(
                                 victim,
                                 playerVehicle,
                                 true);
        bool lastVehicleImpact = Entity.Exists(lastVehicle) &&
                                 (!Entity.Exists(playerVehicle) ||
                                  lastVehicle.Handle != playerVehicle.Handle) &&
                                 IsJusticeVehicleImpactAttack(lastVehicle, victim);
        bool lastVehicleDamage = lastVehicleImpact &&
                                 TryCaptureJusticeDamageFront(
                                     victim,
                                     lastVehicle,
                                     true);
        vehicleWasWeapon = currentVehicleDamage || lastVehicleDamage;
        return directPlayerDamage || vehicleWasWeapon;
    }

    private bool TryCaptureJusticeDamageFront(
        Entity victim,
        Entity attacker,
        bool acceptUnbaselinedDamage = false)
    {
        if ((_justiceUnavailableNativeCircuits & JusticeCircuitClearDamage) != 0)
        {
            // Si GTA refuse la consommation, je coupe ce front plutôt que de
            // réinterpréter indéfiniment un historique impossible à effacer.
            return false;
        }

        if (!Entity.Exists(victim) || !Entity.Exists(attacker))
        {
            return false;
        }

        int victimGeneration = GetJusticeEntityGeneration(victim);
        int attackerGeneration = GetJusticeEntityGeneration(attacker);
        bool damaged = HasJusticeEntityBeenDamagedBy(victim, attacker);
        bool created;
        JusticeDamagePairBaseline baseline = GetOrCreateJusticeDamagePairBaseline(
            victim.Handle,
            victimGeneration,
            attacker.Handle,
            attackerGeneration,
            out created);
        if (baseline == null)
        {
            return false;
        }

        baseline.LastSeenAtMs = _justiceMonotonicTimeMs;
        bool wasDamaged = baseline.WasDamaged;
        if (!damaged)
        {
            baseline.WasDamaged = false;
            return false;
        }

        ScheduleJusticeDamageFrontConsumption(victim);
        baseline.WasDamaged = true;
        return JusticePolicy.ShouldAcceptDamageFront(
            created,
            wasDamaged,
            damaged,
            acceptUnbaselinedDamage);
    }

    private void SynchronizeJusticeDamagePair(Entity victim, Entity attacker)
    {
        if (!Entity.Exists(victim) || !Entity.Exists(attacker) ||
            (_justiceUnavailableNativeCircuits & JusticeCircuitClearDamage) != 0)
        {
            return;
        }

        int victimGeneration = GetJusticeEntityGeneration(victim);
        int attackerGeneration = GetJusticeEntityGeneration(attacker);
        bool created;
        JusticeDamagePairBaseline baseline = GetOrCreateJusticeDamagePairBaseline(
            victim.Handle,
            victimGeneration,
            attacker.Handle,
            attackerGeneration,
            out created);
        if (baseline == null)
        {
            return;
        }

        baseline.WasDamaged = HasJusticeEntityBeenDamagedBy(victim, attacker);
        baseline.LastSeenAtMs = _justiceMonotonicTimeMs;
        if (baseline.WasDamaged)
        {
            ScheduleJusticeDamageFrontConsumption(victim);
        }
    }

    private JusticeDamagePairBaseline GetOrCreateJusticeDamagePairBaseline(
        int victimHandle,
        int victimGeneration,
        int attackerHandle,
        int attackerGeneration,
        out bool created)
    {
        created = false;
        for (int index = 0; index < _justiceDamagePairBaselineCount; index++)
        {
            JusticeDamagePairBaseline existing = _justiceDamagePairBaselines[index];
            if (existing.VictimHandle == victimHandle &&
                existing.VictimGeneration == victimGeneration &&
                existing.AttackerHandle == attackerHandle &&
                existing.AttackerGeneration == attackerGeneration)
            {
                return existing;
            }
        }

        JusticeDamagePairBaseline target;
        if (_justiceDamagePairBaselineCount < JusticeMaximumDamagePairBaselines)
        {
            target = _justiceDamagePairBaselines[_justiceDamagePairBaselineCount++];
        }
        else
        {
            int replacement = _justiceDamagePairReplacementIndex++ %
                JusticeMaximumDamagePairBaselines;
            target = _justiceDamagePairBaselines[replacement];
        }

        target.VictimHandle = victimHandle;
        target.VictimGeneration = victimGeneration;
        target.AttackerHandle = attackerHandle;
        target.AttackerGeneration = attackerGeneration;
        target.WasDamaged = false;
        target.LastSeenAtMs = _justiceMonotonicTimeMs;
        created = true;
        return target;
    }

    private void ResetJusticeDamagePairBaselinesForVictim(int handle, int generation)
    {
        for (int index = 0; index < _justiceDamagePairBaselineCount; index++)
        {
            JusticeDamagePairBaseline baseline = _justiceDamagePairBaselines[index];
            if (baseline.VictimHandle == handle && baseline.VictimGeneration == generation)
            {
                baseline.WasDamaged = false;
            }
        }
    }

    private void ScheduleJusticeDamageFrontConsumption(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return;
        }

        int generation = GetJusticeEntityGeneration(entity);
        for (int index = 0; index < _justiceDamageFrontCount; index++)
        {
            JusticeDamageFrontConsumption existing = _justiceDamageFrontsToConsume[index];
            if (existing != null && existing.Entity != null &&
                existing.Entity.Handle == entity.Handle && existing.Generation == generation)
            {
                return;
            }
        }

        if (_justiceDamageFrontCount >= JusticeMaximumDamageFrontsPerTick)
        {
            return;
        }

        JusticeDamageFrontConsumption target;
        if (_justiceDamageFrontCount < _justiceDamageFrontsToConsume.Count)
        {
            target = _justiceDamageFrontsToConsume[_justiceDamageFrontCount];
        }
        else
        {
            target = new JusticeDamageFrontConsumption();
            _justiceDamageFrontsToConsume.Add(target);
        }

        target.Entity = entity;
        target.Generation = generation;
        _justiceDamageFrontCount++;
    }

    private void FlushJusticeConsumedDamageFronts()
    {
        if (_justiceDamageFrontCount == 0)
        {
            return;
        }

        bool circuitOpen = (_justiceUnavailableNativeCircuits & JusticeCircuitClearDamage) != 0;
        for (int index = 0; index < _justiceDamageFrontCount && !circuitOpen; index++)
        {
            JusticeDamageFrontConsumption front = _justiceDamageFrontsToConsume[index];
            if (front == null || !Entity.Exists(front.Entity) ||
                GetJusticeEntityGeneration(front.Entity) != front.Generation)
            {
                continue;
            }

            try
            {
                Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, front.Entity.Handle);
                ResetJusticeDamagePairBaselinesForVictim(
                    front.Entity.Handle,
                    front.Generation);
            }
            catch (Exception ex)
            {
                MarkJusticeCircuitFailure(
                    JusticeCircuitClearDamage,
                    "CLEAR_ENTITY_LAST_DAMAGE_ENTITY",
                    ex);
                circuitOpen = true;
                _justiceDamageFrontPrimingPending = true;
            }
        }

        _justiceDamageFrontCount = 0;
    }

    private bool TryRecoverJusticeClearDamageCircuit(Ped player)
    {
        if ((_justiceUnavailableNativeCircuits & JusticeCircuitClearDamage) == 0)
        {
            return true;
        }
        if (!CanAttemptJusticeCircuit(JusticeCircuitClearDamage) || !Entity.Exists(player))
        {
            return false;
        }

        try
        {
            // Je vérifie d'abord que GTA accepte de nouveau la purge, puis je
            // reprime toutes les paires avant de rouvrir la moindre détection.
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player.Handle);
            MarkJusticeCircuitRecovered(
                JusticeCircuitClearDamage,
                "CLEAR_ENTITY_LAST_DAMAGE_ENTITY");
            _justiceDamagePairBaselineCount = 0;
            _justiceDamagePairReplacementIndex = 0;
            return true;
        }
        catch (Exception ex)
        {
            MarkJusticeCircuitFailure(
                JusticeCircuitClearDamage,
                "CLEAR_ENTITY_LAST_DAMAGE_ENTITY",
                ex);
            return false;
        }
    }

    private bool IsJusticeCausalDamageFresh(long observedAtMs)
    {
        return observedAtMs >= 0L && _justiceMonotonicTimeMs >= observedAtMs &&
               _justiceMonotonicTimeMs - observedAtMs <= JusticePolicy.PendingIncidentLifetimeMs;
    }

    private void UpdateJusticeArmedThreatFront(Ped player)
    {
        Ped target = null;

        try
        {
            if (Game.Player.IsAiming && Game.Player.IsTargettingAnything)
            {
                target = Game.Player.GetTargetedEntity() as Ped;
            }
        }
        catch
        {
            target = null;
        }

        if (!Entity.Exists(target) || target.IsDead || target.Handle == player.Handle || IsJusticeOwnedAlly(target))
        {
            _justiceAimTargetHandle = 0;
            _justiceAimTargetGeneration = 0;
            _justiceAimStartedAtMs = 0L;
            _justiceAimThreatQueued = false;
            return;
        }

        if (!IsJusticePlayerArmed(player))
        {
            _justiceAimTargetHandle = 0;
            _justiceAimTargetGeneration = 0;
            _justiceAimStartedAtMs = 0L;
            _justiceAimThreatQueued = false;
            return;
        }

        int targetGeneration = GetJusticeEntityGeneration(target);
        if (!IsSameJusticeTrackedTarget(
            _justiceAimTargetHandle,
            _justiceAimTargetGeneration,
            target.Handle,
            targetGeneration))
        {
            _justiceAimTargetHandle = target.Handle;
            _justiceAimTargetGeneration = targetGeneration;
            _justiceAimStartedAtMs = _justiceMonotonicTimeMs;
            _justiceAimThreatQueued = false;
            return;
        }

        if (!_justiceAimThreatQueued &&
            _justiceMonotonicTimeMs - _justiceAimStartedAtMs >= JusticeAimingThreatDelayMs)
        {
            _justiceAimThreatQueued = QueueJusticeIncident(
                JusticeCrimeKind.ArmedThreat,
                target,
                target,
                0,
                GetJusticeBaseCircumstances() | JusticeCircumstances.Armed,
                false,
                0);
        }
    }

    private static bool IsSameJusticeTrackedTarget(
        int rememberedHandle,
        int rememberedGeneration,
        int currentHandle,
        int currentGeneration)
    {
        return rememberedHandle != 0 && rememberedGeneration != 0 &&
               rememberedHandle == currentHandle &&
               rememberedGeneration == currentGeneration;
    }

    private void ScanJusticeEventVictims(
        Ped player,
        bool hitPedRecently,
        bool hitVehicleRecently,
        bool playerVitalityDropped,
        bool weaponDischargeCausal)
    {
        Ped[] nearbyPeds = GetJusticeWitnessCandidatesForActor(player);
        int processedHumans = 0;

        for (int index = 0; index < nearbyPeds.Length && processedHumans < JusticeMaximumWitnessesPerEvent; index++)
        {
            Ped victim = nearbyPeds[index];
            if (!IsJusticePotentialVictimCandidate(victim, player))
            {
                continue;
            }

            processedHumans++;
            int generation = GetJusticeEntityGeneration(victim);
            if (victim.IsDead)
            {
                bool deathByVehicle = WasJusticeDeathCausedByPlayerVehicle(victim, player);
                string activeDischargeCausalId = GetJusticeActiveDischargeCausalId(
                    weaponDischargeCausal);
                bool nativeDeathIsFresh = IsJusticePedDeathFresh(victim);
                bool freshDeath = JusticePolicy.ShouldAcceptAttributedDeathFront(
                    nativeDeathIsFresh,
                    hitPedRecently,
                    activeDischargeCausalId.Length > 0);
                bool lethalDirectPlayerDamage;
                bool detectedVehicleDamage;
                bool causalDamage = TryCaptureJusticePlayerAttackFront(
                    player,
                    victim,
                    freshDeath,
                    out lethalDirectPlayerDamage,
                    out detectedVehicleDamage);
                bool attributedDeath = IsJusticeDeathAttributedTo(
                    victim,
                    player,
                    null,
                    causalDamage ? _justiceMonotonicTimeMs : -1L);
                if (!attributedDeath || (!freshDeath && !causalDamage))
                {
                    // GET_PED_SOURCE_OF_DEATH peut être vide durant la frame
                    // létale. Le front de dégâts joueur fraîchement capturé sert
                    // alors de preuve causale, sans accepter un ancien cadavre.
                    continue;
                }
                deathByVehicle |= detectedVehicleDamage;
                lethalDirectPlayerDamage |= freshDeath && !deathByVehicle;
                JusticeCrimeKind homicideKind = IsJusticePolicePed(victim)
                    ? JusticeCrimeKind.MurderOfficer
                    : deathByVehicle
                        ? JusticeCrimeKind.Manslaughter
                        : JusticeCrimeKind.MurderCivilian;
                JusticeCircumstances homicideCircumstances = BuildJusticeAssaultCircumstances(
                    victim,
                    player,
                    generation,
                    lethalDirectPlayerDamage && !deathByVehicle,
                    deathByVehicle,
                    true);
                string homicideCausalEventId = lethalDirectPlayerDamage && !deathByVehicle
                    ? activeDischargeCausalId
                    : string.Empty;
                bool homicideQueued = QueueJusticeIncident(
                    homicideKind,
                    victim,
                    victim,
                    0,
                    homicideCircumstances,
                    false,
                    0,
                    nearbyPeds,
                    causalEventId: homicideCausalEventId);
                JusticeRecentVictim lethalRecent = RememberJusticeRecentVictim(
                    victim,
                    generation,
                    lethalDirectPlayerDamage && !deathByVehicle,
                    deathByVehicle,
                    homicideCircumstances,
                    homicideCausalEventId);
                lethalRecent.HomicideQueued |= homicideQueued;
                continue;
            }

            ObserveJusticeHostileInitiation(victim, player, generation);

            if (TryCaptureJusticeDamageFront(player, victim, playerVitalityDropped))
            {
                RememberJusticePotentialAggressor(
                    victim,
                    player,
                    generation,
                    _justiceMonotonicTimeMs);
            }

            bool directPlayerDamage;
            bool vehicleWasWeapon;
            if (!TryCaptureJusticePlayerAttackFront(
                player,
                victim,
                hitPedRecently,
                out directPlayerDamage,
                out vehicleWasWeapon))
            {
                continue;
            }

            JusticeCrimeKind kind = ClassifyJusticeAssault(victim, player, vehicleWasWeapon);
            JusticeCircumstances circumstances = BuildJusticeAssaultCircumstances(
                victim,
                player,
                generation,
                directPlayerDamage,
                vehicleWasWeapon,
                false);
            string assaultCausalEventId = GetJusticeActiveDischargeCausalId(
                directPlayerDamage && !vehicleWasWeapon && weaponDischargeCausal);

            QueueJusticeIncident(
                kind,
                victim,
                victim,
                0,
                circumstances,
                false,
                0,
                nearbyPeds,
                causalEventId: assaultCausalEventId);
            RememberJusticeRecentVictim(
                victim,
                generation,
                directPlayerDamage,
                vehicleWasWeapon,
                circumstances,
                assaultCausalEventId);
        }

        if (hitVehicleRecently)
        {
            ScanJusticeDamagedVehicles(player, nearbyPeds, hitVehicleRecently);
        }
    }

    private void ScanJusticeDamagedVehicles(
        Ped player,
        Ped[] witnessCandidates,
        bool acceptUnbaselinedDamage)
    {
        Vehicle[] vehicles = GetJusticeSnapshotVehicles();

        Vehicle currentVehicle = GetJusticeCurrentVehicleSafe(player);
        Vehicle lastVehicle = GetJusticeLastVehicleSafe(player);
        int count = Math.Min(vehicles.Length, JusticeMaximumVehiclesPerEvent);
        for (int index = 0; index < count; index++)
        {
            Vehicle vehicle = vehicles[index];
            if (!IsJusticeSnapshotEntityWithin(vehicle, player, JusticeWitnessRadius) ||
                (Entity.Exists(currentVehicle) && vehicle.Handle == currentVehicle.Handle) ||
                (Entity.Exists(lastVehicle) && vehicle.Handle == lastVehicle.Handle))
            {
                continue;
            }

            bool directDamage = TryCaptureJusticeDamageFront(
                vehicle,
                player,
                acceptUnbaselinedDamage);
            bool currentVehicleImpact = IsJusticeVehicleImpactAttack(
                currentVehicle,
                vehicle);
            bool currentVehicleDamage = currentVehicleImpact &&
                TryCaptureJusticeDamageFront(
                    vehicle,
                    currentVehicle,
                    true);
            bool lastVehicleImpact = Entity.Exists(lastVehicle) &&
                (!Entity.Exists(currentVehicle) || lastVehicle.Handle != currentVehicle.Handle) &&
                IsJusticeVehicleImpactAttack(lastVehicle, vehicle);
            bool lastVehicleDamage = lastVehicleImpact &&
                TryCaptureJusticeDamageFront(
                    vehicle,
                    lastVehicle,
                    true);
            if (!directDamage && !currentVehicleDamage && !lastVehicleDamage)
            {
                continue;
            }

            int generation = GetJusticeEntityGeneration(vehicle);
            bool destroyed = IsJusticeVehicleDestroyed(vehicle);
            JusticeCrimeKind kind = destroyed ? JusticeCrimeKind.VehicleDestruction : JusticeCrimeKind.VehicleDamage;
            JusticeCircumstances circumstances = GetJusticeBaseCircumstances();
            if (directDamage)
            {
                circumstances |= GetJusticeWeaponCircumstances(player);
            }
            if (currentVehicleDamage || lastVehicleDamage)
            {
                circumstances |= JusticeCircumstances.VehicleUsedAsWeapon;
            }
            QueueJusticeIncident(
                kind,
                vehicle,
                null,
                0,
                circumstances,
                false,
                0,
                witnessCandidates);
            RememberJusticeRecentVehicle(vehicle, generation, circumstances);
        }
    }

    private void ProcessJusticeRecentVictimUpgrades(Ped player)
    {
        for (int index = _justiceRecentVictims.Count - 1; index >= 0; index--)
        {
            JusticeRecentVictim recent = _justiceRecentVictims[index];
            if (recent == null || !Entity.Exists(recent.Ped) ||
                GetJusticeEntityGeneration(recent.Ped) != recent.Generation ||
                _justiceMonotonicTimeMs - recent.LastPlayerAttackAtMs > JusticePolicy.PendingIncidentLifetimeMs)
            {
                _justiceRecentVictims.RemoveAt(index);
                continue;
            }

            if (!recent.HomicideQueued && recent.Ped.IsDead &&
                IsJusticeDeathAttributedTo(recent.Ped, player, null, recent.LastPlayerAttackAtMs))
            {
                JusticeCrimeKind kind = IsJusticePolicePed(recent.Ped)
                    ? JusticeCrimeKind.MurderOfficer
                    : recent.VehicleWasWeapon ? JusticeCrimeKind.Manslaughter : JusticeCrimeKind.MurderCivilian;
                JusticeCircumstances circumstances = BuildJusticeAssaultCircumstances(
                    recent.Ped,
                    player,
                    recent.Generation,
                    recent.DirectPlayerDamage,
                    recent.VehicleWasWeapon,
                    true);
                circumstances |= recent.Circumstances &
                    (JusticeCircumstances.Armed |
                     JusticeCircumstances.ExplosiveOrIncendiary);
                recent.HomicideQueued = QueueJusticeIncident(
                    kind,
                    recent.Ped,
                    recent.Ped,
                    0,
                    circumstances,
                    false,
                    0,
                    causalEventId: recent.CausalEventId);
            }

            UpdateJusticeHitAndRunUpgrades(recent, player);
        }
    }

    private void UpdateJusticeHitAndRunUpgrades(JusticeRecentVictim recent, Ped player)
    {
        if (recent == null || !Entity.Exists(recent.Ped) || !Entity.Exists(player) ||
            recent.Ped.IsDead || !recent.VehicleWasWeapon || recent.HitAndRunQueued ||
            _justiceMonotonicTimeMs - recent.LastPlayerAttackAtMs < JusticeHitAndRunMinimumDelayMs ||
            !IsJusticeCausalDamageFresh(recent.LastPlayerAttackAtMs) ||
            recent.Ped.Position.DistanceTo(player.Position) < JusticeHitAndRunDepartureDistance)
        {
            return;
        }

        // Je ne qualifie le délit de fuite qu'après la blessure et un
        // éloignement réel. L'impact initial reste une agression aggravée.
        recent.HitAndRunQueued = QueueJusticeIncident(
            JusticeCrimeKind.HitAndRun,
            recent.Ped,
            recent.Ped,
            0,
            recent.Circumstances | JusticeCircumstances.VehicleUsedAsWeapon,
            false,
            0);
    }

    private void ProcessJusticeRecentVehicleUpgrades(Ped player)
    {
        for (int index = _justiceRecentVehicles.Count - 1; index >= 0; index--)
        {
            JusticeRecentVehicle recent = _justiceRecentVehicles[index];
            if (recent == null || !Entity.Exists(recent.Vehicle) ||
                GetJusticeEntityGeneration(recent.Vehicle) != recent.Generation ||
                _justiceMonotonicTimeMs - recent.LastPlayerDamageAtMs > JusticePolicy.PendingIncidentLifetimeMs)
            {
                _justiceRecentVehicles.RemoveAt(index);
                continue;
            }

            if (!recent.DestructionQueued && IsJusticeVehicleDestroyed(recent.Vehicle))
            {
                recent.DestructionQueued = QueueJusticeIncident(
                    JusticeCrimeKind.VehicleDestruction,
                    recent.Vehicle,
                    null,
                    0,
                    recent.Circumstances,
                    false,
                    0);
            }
        }
    }

    private bool EnsureJusticePendingCapacity(JusticeCrimeKind incomingKind, bool directReport)
    {
        for (int index = _justicePendingIncidents.Count - 1; index >= 0; index--)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            if (pending == null || pending.Incident == null ||
                pending.Incident.IsExpired(_justiceMonotonicTimeMs))
            {
                _justicePendingIncidents.RemoveAt(index);
            }
        }

        if (_justicePendingIncidents.Count < JusticeMaximumPendingIncidents)
        {
            return true;
        }

        int evictionIndex = FindJusticePendingEvictionIndex(incomingKind, directReport);
        if (evictionIndex < 0)
        {
            return false;
        }

        _justicePendingIncidents.RemoveAt(evictionIndex);
        return true;
    }

    private int FindJusticePendingEvictionIndex(JusticeCrimeKind incomingKind, bool directReport)
    {
        int incomingPriority = JusticePolicy.GetDefinition(incomingKind).BasePoints;
        int selectedIndex = -1;
        long oldestCreatedAt = long.MaxValue;

        for (int index = 0; index < _justicePendingIncidents.Count; index++)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            JusticeIncident incident = pending == null ? null : pending.Incident;
            if (incident == null || incident.IsConfirmed || IsProtectedJusticePendingIncident(incident))
            {
                continue;
            }

            int candidatePriority = JusticePolicy.GetDefinition(incident.Kind).BasePoints;
            if (candidatePriority >= incomingPriority || incident.CreatedAtMs >= oldestCreatedAt)
            {
                continue;
            }

            selectedIndex = index;
            oldestCreatedAt = incident.CreatedAtMs;
        }

        // Le booléen explicite documente le contrat d'appel : un signal GTA
        // direct reste prioritaire à l'entrée, mais n'autorise jamais l'éviction
        // d'un autre signal direct, d'un homicide ou d'un fait sur agent.
        return directReport || selectedIndex >= 0 ? selectedIndex : -1;
    }

    private static bool IsProtectedJusticePendingIncident(JusticeIncident incident)
    {
        if (incident == null)
        {
            return false;
        }

        if (incident.Evidence != null &&
            (incident.Evidence.Kind & JusticeEvidenceKind.DirectGameReport) != 0)
        {
            return true;
        }

        return incident.Kind == JusticeCrimeKind.MurderCivilian ||
               incident.Kind == JusticeCrimeKind.Manslaughter ||
               incident.Kind == JusticeCrimeKind.MurderOfficer ||
               incident.Kind == JusticeCrimeKind.AssaultOfficer ||
               incident.Kind == JusticeCrimeKind.AccessoryAssaultOfficer ||
               incident.Kind == JusticeCrimeKind.AccessoryMurderOfficer;
    }

    private bool QueueJusticeIncident(
        JusticeCrimeKind kind,
        Entity victimEntity,
        Ped victimPed,
        int allyHandle,
        JusticeCircumstances circumstances,
        bool alliedAction,
        int additionalVictims,
        Ped[] witnessCandidates = null,
        int allyGeneration = 0,
        Ped alliedActor = null,
        bool wasDonJOwnedAtCreation = false,
        string causalEventId = null)
    {
        if (!_justiceEnabled)
        {
            return false;
        }

        Ped actor = Game.Player.Character;
        if (alliedAction)
        {
            actor = Entity.Exists(alliedActor) ? alliedActor : FindNpcPedByHandle(allyHandle);
            if (!Entity.Exists(actor) || actor.Handle != allyHandle ||
                !HasJusticeValidAllyOwnership(
                    IsJusticeOwnedAlly(actor),
                    actor.IsDead,
                    wasDonJOwnedAtCreation))
            {
                return false;
            }

            int currentAllyGeneration = GetJusticeEntityGeneration(actor);
            if (allyGeneration <= 0)
            {
                allyGeneration = currentAllyGeneration;
            }
            else if (currentAllyGeneration != allyGeneration)
            {
                return false;
            }
        }

        int victimHandle = Entity.Exists(victimEntity) ? victimEntity.Handle : 0;
        int victimGeneration = Entity.Exists(victimEntity) ? GetJusticeEntityGeneration(victimEntity) : 0;
        string episodeId = GetJusticeDetectionEpisodeId();
        string incidentId = BuildJusticeIncidentId(
            kind,
            episodeId,
            victimHandle,
            victimGeneration,
            allyHandle,
            allyGeneration);

        if (IsJusticeIncidentAlreadyKnown(incidentId) || !EnsureJusticePendingCapacity(kind, false))
        {
            return false;
        }

        JusticePendingRuntimeIncident pending = new JusticePendingRuntimeIncident();
        pending.VictimPed = victimPed;
        pending.VictimEntity = victimEntity;
        pending.Incident = new JusticeIncident
        {
            IncidentId = incidentId,
            EpisodeId = episodeId,
            DetectionBatchId = BuildJusticeDetectionBatchId(),
            CausalEventId = (causalEventId ?? string.Empty).Trim(),
            Kind = kind,
            VictimHandle = victimHandle,
            VictimGeneration = victimGeneration,
            AllyHandle = allyHandle,
            AllyGeneration = allyGeneration,
            CreatedAtMs = _justiceMonotonicTimeMs,
            ExpiresAtMs = _justiceMonotonicTimeMs + JusticePolicy.PendingIncidentLifetimeMs,
            Circumstances = circumstances,
            AdditionalVictimCount = Math.Max(0, additionalVictims),
            IsAlliedAction = alliedAction,
            IsConfirmed = false
        };
        pending.Incident.Evidence = BuildJusticeEvidence(
            actor,
            victimPed,
            victimEntity,
            pending,
            witnessCandidates);
        _justicePendingIncidents.Add(pending);
        return true;
    }

    private void QueueJusticeDirectGameReport(JusticeCrimeKind kind, Entity victim)
    {
        int handle = Entity.Exists(victim) ? victim.Handle : 0;
        int generation = Entity.Exists(victim) ? GetJusticeEntityGeneration(victim) : 0;
        string episode = GetJusticeDetectionEpisodeId();
        string incidentId = BuildJusticeIncidentId(kind, episode, handle, generation, 0);

        if (IsJusticeIncidentAlreadyKnown(incidentId) || !EnsureJusticePendingCapacity(kind, true))
        {
            return;
        }

        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = incidentId,
            EpisodeId = episode,
            DetectionBatchId = BuildJusticeDetectionBatchId(),
            Kind = kind,
            VictimHandle = handle,
            VictimGeneration = generation,
            CreatedAtMs = _justiceMonotonicTimeMs,
            ExpiresAtMs = _justiceMonotonicTimeMs + JusticePolicy.PendingIncidentLifetimeMs,
            Circumstances = GetJusticeBaseCircumstances(),
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ObservedAtMs = _justiceMonotonicTimeMs,
                ReportDueAtMs = _justiceMonotonicTimeMs,
                ReportCompleted = true
            }
        };
        _justicePendingIncidents.Add(new JusticePendingRuntimeIncident { Incident = incident, VictimEntity = victim });
    }

    private JusticeEvidence BuildJusticeEvidence(
        Ped actor,
        Ped victimPed,
        Entity eventEntity,
        JusticePendingRuntimeIncident pending,
        Ped[] witnessCandidates)
    {
        JusticeEvidence evidence = new JusticeEvidence
        {
            ObservedAtMs = _justiceMonotonicTimeMs,
            ReportDueAtMs = _justiceMonotonicTimeMs + JusticePolicy.CivilianReportDelayMs
        };

        if (!Entity.Exists(actor))
        {
            return evidence;
        }

        if (Entity.Exists(victimPed) && !victimPed.IsDead &&
            (victimPed.Position.DistanceTo(actor.Position) <= 12.0f || CanPedSeeJusticeEvent(victimPed, actor, eventEntity)))
        {
            bool policeVictim = IsJusticePolicePed(victimPed);
            long reportDueAtMs = policeVictim
                ? _justiceMonotonicTimeMs
                : _justiceMonotonicTimeMs + JusticePolicy.CivilianReportDelayMs;
            evidence.Kind = policeVictim
                ? JusticeEvidenceKind.PoliceWitness
                : JusticeEvidenceKind.VictimWitness;
            evidence.WitnessHandle = victimPed.Handle;
            evidence.WitnessGeneration = GetJusticeEntityGeneration(victimPed);
            evidence.HasPlausibleObserver = true;
            evidence.ReportDueAtMs = reportDueAtMs;
            AddJusticeRuntimeWitness(pending, victimPed, evidence.Kind, reportDueAtMs);
        }

        Ped[] nearby = witnessCandidates ?? GetJusticeWitnessCandidatesForActor(actor);
        Ped player = Game.Player.Character;
        int humans = 0;
        for (int index = 0; index < nearby.Length && humans < JusticeMaximumWitnessesPerEvent; index++)
        {
            Ped witness = nearby[index];
            if (!IsJusticeHumanCandidate(witness, actor) ||
                (Entity.Exists(player) && witness.Handle == player.Handle) ||
                (Entity.Exists(victimPed) && witness.Handle == victimPed.Handle) ||
                IsJusticeOwnedAlly(witness))
            {
                continue;
            }

            humans++;
            if (!CanPedSeeJusticeEvent(witness, actor, eventEntity))
            {
                continue;
            }

            if (IsJusticePolicePed(witness))
            {
                AddJusticeRuntimeWitness(
                    pending,
                    witness,
                    JusticeEvidenceKind.PoliceWitness,
                    _justiceMonotonicTimeMs);
                continue;
            }

            AddJusticeRuntimeWitness(
                pending,
                witness,
                JusticeEvidenceKind.CivilianWitness,
                _justiceMonotonicTimeMs + JusticePolicy.CivilianReportDelayMs);
        }

        JusticeRuntimeWitness selected = SelectBestJusticeRuntimeWitness(pending);
        if (selected != null)
        {
            evidence.Kind = selected.Kind;
            evidence.WitnessHandle = selected.Ped.Handle;
            evidence.WitnessGeneration = selected.Generation;
            evidence.HasPlausibleObserver = true;
            evidence.ReportDueAtMs = selected.ReportDueAtMs;
            evidence.ReportCompleted = selected.Kind == JusticeEvidenceKind.PoliceWitness;
        }

        return evidence;
    }

    private void AddJusticeRuntimeWitness(
        JusticePendingRuntimeIncident pending,
        Ped witness,
        JusticeEvidenceKind kind,
        long reportDueAtMs)
    {
        if (pending == null || !Entity.Exists(witness) ||
            pending.Witnesses.Count >= JusticeMaximumWitnessesPerEvent)
        {
            return;
        }

        int generation = GetJusticeEntityGeneration(witness);
        for (int index = 0; index < pending.Witnesses.Count; index++)
        {
            JusticeRuntimeWitness existing = pending.Witnesses[index];
            if (existing.Ped.Handle == witness.Handle && existing.Generation == generation)
            {
                if (kind == JusticeEvidenceKind.PoliceWitness)
                {
                    existing.Kind = kind;
                    existing.ReportDueAtMs = reportDueAtMs;
                }
                return;
            }
        }

        pending.Witnesses.Add(new JusticeRuntimeWitness
        {
            Ped = witness,
            Generation = generation,
            Kind = kind,
            ReportDueAtMs = reportDueAtMs
        });
    }

    private static JusticeRuntimeWitness SelectBestJusticeRuntimeWitness(JusticePendingRuntimeIncident pending)
    {
        JusticeRuntimeWitness fallback = null;
        if (pending == null)
        {
            return null;
        }

        for (int index = 0; index < pending.Witnesses.Count; index++)
        {
            JusticeRuntimeWitness witness = pending.Witnesses[index];
            if (witness == null)
            {
                continue;
            }
            if (witness.Kind == JusticeEvidenceKind.PoliceWitness)
            {
                return witness;
            }
            if (fallback == null || witness.Kind == JusticeEvidenceKind.VictimWitness)
            {
                fallback = witness;
            }
        }
        return fallback;
    }

    private void ProcessJusticePendingIncidents()
    {
        long metricStartedAt = BeginJusticeMetric();
        int confirmedCount = 0;
        for (int index = _justicePendingIncidents.Count - 1; index >= 0; index--)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            if (pending == null || pending.Incident == null)
            {
                _justicePendingIncidents.RemoveAt(index);
                continue;
            }

            if (pending.Incident.IsExpired(_justiceMonotonicTimeMs))
            {
                _justicePendingIncidents.RemoveAt(index);
                continue;
            }

            if (confirmedCount >= JusticeMaximumConfirmedIncidentsPerTick)
            {
                // Je reporte le surplus au tick suivant : une foule ne peut pas
                // concentrer toutes les mutations judiciaires sur une frame.
                continue;
            }

            bool witnessAlive = SelectLiveJusticeWitnessForConfirmation(pending);
            if (!pending.Incident.TryConfirm(_justiceMonotonicTimeMs, witnessAlive))
            {
                continue;
            }

            if (confirmedCount < _justiceConfirmedIncidentBuffer.Length)
            {
                _justiceConfirmedIncidentBuffer[confirmedCount++] = pending;
            }
        }

        // Je sépare la découverte de l'application : aucun callback de charge
        // ne peut ainsi modifier la liste que je suis encore en train d'itérer.
        for (int index = 1; index < confirmedCount; index++)
        {
            JusticePendingRuntimeIncident candidate = _justiceConfirmedIncidentBuffer[index];
            int insertionIndex = index - 1;
            while (insertionIndex >= 0 &&
                   JusticePolicy.CompareIncidentResolutionPriority(
                       _justiceConfirmedIncidentBuffer[insertionIndex].Incident,
                       candidate.Incident) < 0)
            {
                _justiceConfirmedIncidentBuffer[insertionIndex + 1] =
                    _justiceConfirmedIncidentBuffer[insertionIndex];
                insertionIndex--;
            }
            _justiceConfirmedIncidentBuffer[insertionIndex + 1] = candidate;
        }

        for (int pendingIndex = _justicePendingIncidents.Count - 1;
             pendingIndex >= 0;
             pendingIndex--)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[pendingIndex];
            for (int confirmedIndex = 0; confirmedIndex < confirmedCount; confirmedIndex++)
            {
                if (ReferenceEquals(pending, _justiceConfirmedIncidentBuffer[confirmedIndex]))
                {
                    _justicePendingIncidents.RemoveAt(pendingIndex);
                    break;
                }
            }
        }

        for (int index = 0; index < confirmedCount; index++)
        {
            JusticePendingRuntimeIncident pending = _justiceConfirmedIncidentBuffer[index];
            bool supersededRecklessDischarge = false;
            for (int priorIndex = 0; priorIndex < index; priorIndex++)
            {
                JusticePendingRuntimeIncident prior = _justiceConfirmedIncidentBuffer[priorIndex];
                if (prior != null &&
                    JusticePolicy.DoesConfirmedViolenceSupersedeRecklessDischarge(
                        prior.Incident,
                        pending.Incident))
                {
                    supersededRecklessDischarge = true;
                    break;
                }
            }
            if (supersededRecklessDischarge)
            {
                continue;
            }

            JusticeCharge charge = JusticePolicy.ApplyConfirmedIncident(
                _justiceCaseState,
                pending.Incident,
                _justiceRecordState);

            if (charge == null)
            {
                continue;
            }

            OnJusticeChargeConfirmed(charge);
        }

        for (int index = 0; index < confirmedCount; index++)
        {
            _justiceConfirmedIncidentBuffer[index] = null;
        }
        CompleteJusticeMetric(_justiceIncidentProcessingMetrics, metricStartedAt);
    }

    private void OnJusticeChargeConfirmed(JusticeCharge charge)
    {
        RemovePendingRecklessDischargeForConfirmedViolence(charge);
        _justiceCleanCarryMilliseconds = 0L;
        _justiceCaseState.Enabled = true;
        _justiceDetectionEpisodeId = charge.EpisodeId;
        JusticeMarkStateDirty();

        // Je laisse GTA décider seul du niveau de recherche provoqué par les
        // délits. Justice observe ces étoiles mais n'en ajoute aucune.

        string notificationDetail = GetJusticeSeverityDisplay() + "  •  " +
                                    FormatJusticeMoney(_justiceCaseState.FineDue) + "  •  " +
                                    FormatJusticeDuration(_justiceCaseState.SentenceSeconds);
        ShowStatus(
            "Justice · " + charge.DisplayName + " · " + notificationDetail,
            JusticeNotificationMs);
        LogInfo(
            "Justice.Infraction",
            charge.DisplayName + " | score=" + _justiceCaseState.ActiveScore.ToString(CultureInfo.InvariantCulture) +
            " | témoin confirmé | allié=" + (charge.IsAlliedAction ? "oui" : "non") + ".");
    }

    private void UpdateJusticeWantedEdges(int wantedLevel)
    {
        bool justiceAuthoredRise = wantedLevel > _justiceLastWantedLevel &&
                                   _justiceMonotonicTimeMs <= _justiceWrittenWantedExpiresAtMs &&
                                   wantedLevel <= _justiceWrittenWantedLevel;
        if (wantedLevel > _justiceLastWantedLevel)
        {
            if (!justiceAuthoredRise)
            {
                ClearLatchedJusticeWantedRise();
                CorrelateJusticeWantedRise();
            }

            if (wantedLevel > 0)
            {
                StartJusticePursuitEpisodeIfNeeded();
            }
        }

        if (wantedLevel > 0)
        {
            bool hasActiveCase = HasActiveJusticeCase();
            if (hasActiveCase)
            {
                // Je passe toujours par l'ouverture centralisée de la poursuite.
                // Une étoile peut précéder la confirmation différée du premier
                // incident ; le tick qui voit enfin le dossier démarre alors ses
                // douze secondes au lieu de réutiliser zéro ou un ancien profil.
                StartJusticePursuitEpisodeIfNeeded();
            }
            else
            {
                _justicePursuitActive = false;
                _justiceWantedEpisodeStartedAtMs = 0L;
            }
            if (_justiceCaseState.Phase == JusticePhase.AtLarge && hasActiveCase)
            {
                _justiceCaseState.Phase = JusticePhase.Wanted;
                JusticeMarkStateDirty();
            }
        }
        else if (_justiceLastWantedLevel > 0 && HasActiveJusticeCase() && !JusticeIsCustodyActive)
        {
            _justicePursuitActive = false;
            _justiceWantedEpisodeStartedAtMs = 0L;
            _justiceAllyTokens.Clear();
            _justiceCaseState.HasWarrant = true;
            OpenJusticeDetectionEpisodeAfterPursuitLoss();
            if (_justiceCaseState.Phase == JusticePhase.Wanted || _justiceCaseState.Phase == JusticePhase.Surrendering)
            {
                _justiceCaseState.Phase = JusticePhase.AtLarge;
            }
            JusticeMarkStateDirty();
            ShowStatus("Justice : poursuite perdue, mandat toujours actif.", 4200);
        }

        if (_justiceMonotonicTimeMs > _justiceWrittenWantedExpiresAtMs ||
            wantedLevel > _justiceWrittenWantedLevel)
        {
            _justiceWrittenWantedLevel = 0;
            _justiceWrittenWantedExpiresAtMs = 0L;
        }
    }

    private void ResolveDeferredJusticeWantedLoss(int wantedLevel)
    {
        if (!_justiceWantedLossPending)
        {
            return;
        }

        _justiceWantedLossPending = false;
        if (!_justiceEnabled || wantedLevel > 0 || !HasActiveJusticeCase() ||
            JusticeIsCustodyActive)
        {
            return;
        }

        // Une mission ou un chargement peut effacer les étoiles. Je matérialise
        // cette perte une fois au retour, avant qu'une mort soit interprétée.
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        _justiceAllyTokens.Clear();
        _justiceCaseState.HasWarrant = true;
        OpenJusticeDetectionEpisodeAfterPursuitLoss();
        if (_justiceCaseState.Phase == JusticePhase.Wanted ||
            _justiceCaseState.Phase == JusticePhase.Surrendering)
        {
            _justiceCaseState.Phase = JusticePhase.AtLarge;
        }
        JusticeMarkStateDirty();
        ShowStatus("Justice : poursuite perdue pendant la transition, mandat actif.", 4200);
    }

    private bool TryResolveJusticeMaskedArrestOnWantedLoss(
        int wantedLevel,
        bool arrestStateValid)
    {
        bool wantedWasLost = wantedLevel == 0 &&
            (_justiceLastWantedLevel > 0 || _justiceWantedLossPending);
        if (!_justiceEnabled || !HasActiveJusticeCase() || JusticeIsCustodyActive ||
            !wantedWasLost ||
            (arrestStateValid && !_justiceArrestCompletionProbePending &&
             !_justiceWantedLossPending))
        {
            return false;
        }

        if (!_justiceArrestCompletionProbePending)
        {
            // Je garde la perte d'étoiles en suspens quand l'état BUSTED est
            // illisible. Le timer indépendant pourra encore prouver la capture
            // après le backoff de la native, sans inventer une résistance.
            _justiceArrestCompletionProbePending = true;
            _justiceArrestCompletionProbeStartedAtMs = _justiceMonotonicTimeMs;
        }
        _justiceWantedLossPending = true;

        bool completedArrest;
        bool completionStateValid = TryGetJusticeArrestConfirmedSafe(
            true,
            out completedArrest);
        if (completionStateValid && completedArrest)
        {
            _justiceWantedLossPending = false;
            if (!BeginJusticeCapture(false))
            {
                _justiceCaptureRetryPending = true;
                _justiceCaptureRetryDeath = false;
            }
            return true;
        }

        long elapsed = Math.Max(
            0L,
            _justiceMonotonicTimeMs - _justiceArrestCompletionProbeStartedAtMs);
        if (elapsed < JusticeMaskedArrestProbeMaximumMs)
        {
            return true;
        }

        // Je borne l'incertitude : après douze secondes sans preuve de BUSTED,
        // la perte wanted reprend son chemin normal vers un mandat, sans charge
        // de résistance fondée sur un état inconnu.
        _justiceArrestCompletionProbePending = false;
        _justiceArrestCompletionProbeStartedAtMs = 0L;
        return false;
    }

    private void OpenJusticeDetectionEpisodeAfterPursuitLoss()
    {
        if (_justiceCaseState == null)
        {
            return;
        }

        _justiceEpisodeSequence++;
        string episode = "warrant-gap:" + _justiceSessionId + ":" +
                         _justiceEpisodeSequence.ToString(CultureInfo.InvariantCulture);
        _justiceCaseState.WantedEpisodeId = episode;
        _justiceDetectionEpisodeId = episode;
        _justiceWantedEpisodeStartedAtMs = 0L;
    }

    private void CorrelateJusticeWantedRise()
    {
        if (!_justiceWantedRisePendingCorrelation)
        {
            // Le premier appel vient du front wanted et ne confirme rien. Il
            // arme uniquement la passe qui suivra la prochaine détection monde.
            _justiceWantedRisePendingCorrelation = true;
            _justiceWantedRiseObservedAtMs = _justiceMonotonicTimeMs;
            _justiceWantedRiseDetectionPass = _justiceEventDetectionPass == int.MaxValue
                ? 1
                : _justiceEventDetectionPass + 1;
            if (_justiceEventDetectionPass == int.MaxValue)
            {
                _justiceEventDetectionPass = 0;
            }
            return;
        }
        if (_justiceMonotonicTimeMs - _justiceWantedRiseObservedAtMs >
            JusticePolicy.WantedCorrelationWindowMs)
        {
            ClearLatchedJusticeWantedRise();
            return;
        }
        if (_justiceEventDetectionPass < _justiceWantedRiseDetectionPass)
        {
            return;
        }

        JusticeIncident bestMatch = null;
        for (int index = 0; index < _justicePendingIncidents.Count; index++)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            JusticeIncident incident = pending == null ? null : pending.Incident;
            if (incident == null || incident.Evidence == null ||
                incident.CreatedAtMs > _justiceWantedRiseObservedAtMs ||
                !JusticePolicy.IsWantedCorrelationCandidate(
                    _justiceWantedRiseObservedAtMs,
                    incident.CreatedAtMs,
                    incident.Evidence.HasPlausibleObserver,
                    HasCredibleJusticeObserverAtWantedRise(pending)))
            {
                continue;
            }

            bool moreRecent = bestMatch != null &&
                incident.CreatedAtMs > bestMatch.CreatedAtMs;
            int priority = JusticePolicy.CompareIncidentResolutionPriority(
                incident,
                bestMatch);
            if (bestMatch == null || priority > 0 ||
                (moreRecent && priority == 0))
            {
                bestMatch = incident;
            }
        }

        if (bestMatch != null)
        {
            // Je ne crée jamais un crime depuis les étoiles seules et je ne
            // confirme qu'un seul fait précis : le plus récent déjà rattaché à
            // un observateur plausible dans la fenêtre de quatre secondes.
            bestMatch.Evidence.Kind |= JusticeEvidenceKind.CorrelatedWantedRise;
            bestMatch.Evidence.ReportDueAtMs = _justiceMonotonicTimeMs;
            bestMatch.Evidence.ReportCompleted = true;
        }

        // Une seule passe post-détection peut consommer une hausse. Je ne laisse
        // jamais une étoile ancienne confirmer un incident créé plus tard.
        ClearLatchedJusticeWantedRise();
    }

    private void ClearLatchedJusticeWantedRise()
    {
        _justiceWantedRisePendingCorrelation = false;
        _justiceWantedRiseObservedAtMs = 0L;
        _justiceWantedRiseDetectionPass = 0;
    }

    private bool HasCredibleJusticeObserverAtWantedRise(
        JusticePendingRuntimeIncident pending)
    {
        if (pending == null || pending.Incident == null ||
            pending.Incident.Evidence == null)
        {
            return false;
        }

        for (int index = 0; index < pending.Witnesses.Count; index++)
        {
            JusticeRuntimeWitness witness = pending.Witnesses[index];
            if (witness == null || !Entity.Exists(witness.Ped) ||
                GetJusticeEntityGeneration(witness.Ped) != witness.Generation)
            {
                continue;
            }

            // Un policier observateur confirme dès la vision directe. Pour une
            // victime ou un civil, le témoin doit encore être vivant lorsque
            // la hausse wanted matérialise son signalement.
            if (witness.Kind == JusticeEvidenceKind.PoliceWitness ||
                !witness.Ped.IsDead)
            {
                return true;
            }
        }
        return false;
    }

    private void StartJusticePursuitEpisodeIfNeeded(string preferredEpisode = null)
    {
        if (!HasActiveJusticeCase())
        {
            return;
        }

        if (_justicePursuitActive)
        {
            if (_justiceWantedEpisodeStartedAtMs <= 0L ||
                _justiceWantedEpisodeStartedAtMs > _justiceMonotonicTimeMs)
            {
                // Je répare les anciens chemins runtime qui avaient activé la
                // poursuite sans horodatage fiable. Je ne crédite jamais du temps
                // écoulé avant le dossier courant.
                _justiceWantedEpisodeStartedAtMs = _justiceMonotonicTimeMs;
            }
            return;
        }

        {
            string episode = (preferredEpisode ?? string.Empty).Trim();
            if (episode.Length == 0 && !_justiceCaseState.HasWarrant)
            {
                episode = (_justiceCaseState.WantedEpisodeId ?? string.Empty).Trim();
            }
            if (episode.Length == 0)
            {
                _justiceEpisodeSequence++;
                episode = "pursuit:" + _justiceSessionId + ":" +
                          _justiceEpisodeSequence.ToString(CultureInfo.InvariantCulture);
            }
            _justiceCaseState.WantedEpisodeId = episode;
            _justiceDetectionEpisodeId = episode;
            _justiceWantedEpisodeStartedAtMs = _justiceMonotonicTimeMs;
            _justicePursuitActive = true;
            JusticeMarkStateDirty();
        }
    }

    private void UpdateJusticeWarrantRecognition(Ped player)
    {
        if (!_justiceCaseState.HasWarrant || _justiceLastWantedLevel > 0)
        {
            _justiceRecognitionCandidateHandle = 0;
            _justiceRecognitionCandidateGeneration = 0;
            _justiceRecognitionStartedAtMs = 0L;
            return;
        }

        if (_justiceMonotonicTimeMs < _justiceNextWarrantScanAtMs)
        {
            return;
        }

        _justiceNextWarrantScanAtMs = _justiceMonotonicTimeMs + JusticeWarrantScanIntervalMs;
        Ped[] nearby = GetJusticeSnapshotPeds();
        Ped recognizer = null;
        int humans = 0;

        for (int index = 0; index < nearby.Length && humans < JusticeMaximumWitnessesPerEvent; index++)
        {
            Ped candidate = nearby[index];
            if (!IsJusticeSnapshotEntityWithin(
                    candidate,
                    player,
                    JusticeWarrantRecognitionRadius) ||
                !IsJusticeHumanCandidate(candidate, player))
            {
                continue;
            }

            humans++;
            if (IsJusticePolicePed(candidate) && CanPedSeeJusticeEvent(candidate, player, player))
            {
                recognizer = candidate;
                break;
            }
        }

        if (!Entity.Exists(recognizer))
        {
            _justiceRecognitionCandidateHandle = 0;
            _justiceRecognitionCandidateGeneration = 0;
            _justiceRecognitionStartedAtMs = 0L;
            return;
        }

        int recognizerGeneration = GetJusticeEntityGeneration(recognizer);
        if (_justiceRecognitionCandidateHandle != recognizer.Handle ||
            _justiceRecognitionCandidateGeneration != recognizerGeneration)
        {
            _justiceRecognitionCandidateHandle = recognizer.Handle;
            _justiceRecognitionCandidateGeneration = recognizerGeneration;
            _justiceRecognitionStartedAtMs = _justiceMonotonicTimeMs;
            return;
        }

        if (_justiceMonotonicTimeMs - _justiceRecognitionStartedAtMs < JusticeWarrantRecognitionMs)
        {
            return;
        }

        _justiceRecognitionSequence++;
        _justiceRecognitionCandidateHandle = 0;
        _justiceRecognitionCandidateGeneration = 0;
        _justiceRecognitionStartedAtMs = 0L;
        _justiceNextWarrantScanAtMs =
            _justiceMonotonicTimeMs + JusticeWarrantRecognitionNotificationCooldownMs;
        ShowStatus(
            "Justice : mandat reconnu par une patrouille. GTA gère seul la recherche.",
            3600);
        LogInfo(
            "Justice.Mandat",
            "Mandat reconnu sans écriture du niveau wanted; GTA reste autoritaire.");
    }

    private void UpdateJusticeEvadingPoliceCharge(Ped player)
    {
        string pursuitEpisode = CurrentJusticeEpisodeId();
        if (!_justicePursuitActive || _justiceLastWantedLevel <= 0 ||
            _justiceCaseState.IsFleeingChargedForEpisode(pursuitEpisode) ||
            _justiceWantedEpisodeStartedAtMs <= 0L ||
            _justiceWantedEpisodeStartedAtMs > _justiceMonotonicTimeMs ||
            _justiceMonotonicTimeMs - _justiceWantedEpisodeStartedAtMs < JusticeEvadingPoliceDelayMs)
        {
            return;
        }

        QueueJusticeDirectGameReport(JusticeCrimeKind.EvadingPolice, null);
    }

    private bool BeginJusticeCapture(bool deathCapture)
    {
        if (!HasActiveJusticeCase())
        {
            return false;
        }

        if (_justiceDeferredInventoryRestore)
        {
            // Je termine d'abord la restitution de l'épisode précédent. Réutiliser
            // son snapshot comme précommit d'une nouvelle capture ferait perdre
            // les armes acquises entre les deux affaires.
            return false;
        }

        if (JusticeIsCustodyActive)
        {
            if (_justiceCaseState.Phase != JusticePhase.Captured ||
                _justiceCustodyRuntimeActive || _justiceCustodyTransferPending)
            {
                return true;
            }

            // Je reprends le palier exact situé après le jugement mais avant le
            // transfert. Tant que son XML n'est pas durable, je ne supprime pas
            // le wanted et je ne détache aucune cible policière des alliés.
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }

            CompleteJusticeCaptureAfterCommit(
                deathCapture || _justiceCustodyWaitingForRespawn);
            return true;
        }

        Ped capturedPlayer = Game.Player.Character;
        int captureCurrentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int captureTrustedSlot = JusticePolicy.ResolveTrustedCanonicalPlayerSlot(
            captureCurrentSlot,
            _justiceLastCanonicalPlayerSlot);
        if (!TryBindJusticeCustodyPlayerIdentityForCapture(capturedPlayer, deathCapture))
        {
            if (deathCapture)
            {
                int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
                _justicePursuitDeathObservedDuringSuspension = true;
                _justiceSuspendedPursuitDeathPlayerSlot =
                    JusticePolicy.ResolveTrustedCanonicalPlayerSlot(
                        currentSlot,
                        _justiceLastCanonicalPlayerSlot);
                _justiceSuspendedPursuitDeathPlayerModelHash =
                    GetJusticePedModelHashSafe(capturedPlayer);
                _justiceCaptureRetryPending = false;
                _justiceCaptureRetryDeath = false;
                if (_justiceCaseState.Phase == JusticePhase.AtLarge)
                {
                    _justiceCaseState.Phase = JusticePhase.Wanted;
                }
                JusticeMarkStateDirty();
                JusticeFlushStateNow();
            }
            else if (captureTrustedSlot < 0)
            {
                FinalizeUnknownJusticeCaptureAsWarrant(
                    "Arrestation d'un ped custom sans slot canonique : aucun jugement, débit ou inventaire modifié.");
                return true;
            }
            // Je refuse de commettre une capture sans identité persistable : un
            // changement de protagoniste pendant un crash ne doit jamais déplacer,
            // débiter ou désarmer le mauvais personnage.
            ShowStatus("Justice : identification du détenu en attente…", 2800);
            LogWarning("Justice.Capture", "Capture différée : modèle du protagoniste indisponible.");
            return false;
        }

        // Je ferme tous les signalements encore provisoires avant le jugement :
        // aucun témoin retardé ne peut modifier après coup un dossier condamné.
        _justicePendingIncidents.Clear();

        if (string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId) ||
            _justiceCaseState.Phase == JusticePhase.Fugitive)
        {
            _justiceCaseState.CustodyEpisodeId = "custody:" + _justiceSessionId + ":" +
                                                 (++_justiceEpisodeSequence).ToString(CultureInfo.InvariantCulture);
        }

        JusticeSignal signal = deathCapture
            ? JusticeSignal.PlayerDiedDuringPolicePursuit
            : JusticeSignal.ArrestCompleted;
        JusticeTransition transition = ApplyJusticeTransition(signal, _justiceCaseState.CustodyEpisodeId);
        JusticeOperation capture = transition == null ? null : transition.Operation;

        if (capture == null)
        {
            capture = new JusticeOperation(
                JusticePolicy.CreateOperationId(JusticeOperationKind.Capture, _justiceCaseState.CustodyEpisodeId),
                JusticeOperationKind.Capture,
                _justiceCaseState.CustodyEpisodeId);
            _justiceCaseState.Phase = JusticePhase.Captured;
        }

        if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, capture))
        {
            return JusticeIsCustodyActive;
        }

        JusticeOperation convictionOperation = new JusticeOperation(
            JusticePolicy.CreateOperationId(JusticeOperationKind.ApplyConviction, _justiceCaseState.CustodyEpisodeId),
            JusticeOperationKind.ApplyConviction,
            _justiceCaseState.CustodyEpisodeId);

        if (JusticePolicy.TryRegisterOperation(_justiceCaseState, convictionOperation))
        {
            JusticePolicy.ApplyConviction(_justiceCaseState, _justiceRecordState, DateTime.UtcNow);
        }

        if (deathCapture)
        {
            // Je commets l'attente du nouveau ped dans le même XML que la
            // condamnation. Un arrêt avant JusticeBeginCustodyTransfer ne peut
            // ainsi jamais figer une tenue custom devenue un autre protagoniste.
            _justiceCustodyDeathRebindPending = true;
            _justiceCustodyWaitingForRespawn = true;
        }
        _justiceCaseState.HasWarrant = false;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            LogWarning(
                "Justice.Capture",
                "Transfert différé : précommit du jugement indisponible.");
            return false;
        }

        CompleteJusticeCaptureAfterCommit(deathCapture);
        return true;
    }

    private void CompleteJusticeCaptureAfterCommit(bool deathCapture)
    {
        ReleaseJusticeAllyPoliceTargetsForTransfer();
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        LogInfo("Justice.Capture", deathCapture ? "Capture apres mort en poursuite." : "Arrestation confirmee.");
        ClearJusticeWantedLevelOnce();
        JusticeBeginCustodyTransfer(deathCapture);
    }

    private void ReleaseJusticeAllyPoliceTargetsForTransfer()
    {
        Ped player = Game.Player.Character;
        _justiceReleasedAllyHandles.Clear();
        for (int index = 0; index < _justiceAllyTokens.Count; index++)
        {
            JusticeAllyCausalToken token = _justiceAllyTokens[index];
            if (!IsJusticeAllyTokenValidForTransfer(token, player) ||
                _justiceReleasedAllyHandles.Contains(token.Ally.Handle))
            {
                continue;
            }

            if (TryReleaseJusticeAllyPoliceTargetForTransfer(token, player))
            {
                // Le helper revalide le combat courant, les distances et les
                // générations avant de couper uniquement cette cible policière.
                _justiceReleasedAllyHandles.Add(token.Ally.Handle);
            }
        }
        _justiceAllyTokens.Clear();
    }

    private bool IsJusticeAllyTokenValidForTransfer(
        JusticeAllyCausalToken token,
        Ped player)
    {
        return _justiceEnabled && _justiceCaseState != null &&
               _justiceCaseState.Phase == JusticePhase.Captured &&
               token != null &&
               string.Equals(token.EpisodeId, CurrentJusticeEpisodeId(), StringComparison.Ordinal) &&
               Entity.Exists(player) && Entity.Exists(token.Ally) &&
               Entity.Exists(token.PoliceTarget) &&
               HasJusticeValidAllyOwnership(
                   IsJusticeOwnedAlly(token.Ally),
                   token.Ally.IsDead,
                   token.WasDonJOwnedAtCreation) &&
               IsJusticePolicePed(token.PoliceTarget) &&
               GetJusticeEntityGeneration(token.Ally) == token.AllyGeneration &&
               GetJusticeEntityGeneration(token.PoliceTarget) == token.TargetGeneration;
    }

    private JusticeTransition ApplyJusticeTransition(JusticeSignal signal, string episodeId)
    {
        JusticeTransition transition = JusticePolicy.Transition(
            _justiceCaseState,
            new JusticeTickInput { Signals = signal, EpisodeId = episodeId ?? string.Empty });
        if (transition != null && transition.Changed)
        {
            JusticeMarkStateDirty();
        }
        return transition;
    }

    private void JusticePrepareLegalReleaseState()
    {
        _justiceRecordState.PinnedConvictionId = string.Empty;
        _justiceCaseState.ClearActiveCase(false);
        _justiceCaseState.Enabled = _justiceEnabled;
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        _justiceDetectionEpisodeId = string.Empty;
        _justicePendingIncidents.Clear();
        _justiceRecentVictims.Clear();
        _justiceRecentVehicles.Clear();
        _justiceAllyTokens.Clear();
        JusticeMarkStateDirty();
    }

    private void JusticeRegisterEscape()
    {
        int remainingSentence = Math.Max(0, _justiceCaseState.SentenceSeconds);
        long remainingFine = Math.Max(0L, _justiceCaseState.FineDue);
        string episode = string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId)
            ? CurrentJusticeEpisodeId()
            : _justiceCaseState.CustodyEpisodeId;
        JusticeOperation operation = new JusticeOperation(
            JusticePolicy.CreateOperationId(JusticeOperationKind.RegisterEscape, episode),
            JusticeOperationKind.RegisterEscape,
            episode);
        if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, operation))
        {
            return;
        }

        _justiceRecordState.PinnedConvictionId = string.Empty;

        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = "escape:" + episode,
            EpisodeId = episode,
            Kind = JusticeCrimeKind.Escape,
            CreatedAtMs = _justiceMonotonicTimeMs,
            ExpiresAtMs = _justiceMonotonicTimeMs + JusticePolicy.PendingIncidentLifetimeMs,
            Circumstances = JusticeCircumstances.InCustody | GetJusticeBaseCircumstances(),
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ObservedAtMs = _justiceMonotonicTimeMs,
                ReportDueAtMs = _justiceMonotonicTimeMs,
                ReportCompleted = true
            },
            IsConfirmed = true
        };
        JusticeCharge charge = JusticePolicy.ApplyConfirmedIncident(_justiceCaseState, incident, _justiceRecordState);
        // Le domaine recalcule normalement un dossier encore non jugé. Ici les
        // anciennes amendes ont déjà été prélevées et une partie de la peine a
        // déjà été purgée : je ne rajoute donc que la nouvelle charge d'évasion.
        _justiceCaseState.FineDue = JusticePolicy.SaturatingAdd(
            remainingFine,
            charge == null ? 0L : Math.Max(0L, charge.Fine),
            JusticePolicy.MaxActiveFine);
        _justiceCaseState.SentenceSeconds = (int)Math.Min(
            JusticePolicy.MaxActiveSentenceSeconds,
            (long)remainingSentence + (charge == null ? 0 : Math.Max(0, charge.SentenceSeconds)));
        _justiceCaseState.HasWarrant = true;
        _justiceCaseState.Phase = JusticePhase.Fugitive;
        _justiceCaseState.EscapeWantedMinimumPending = true;
        _justiceCaseState.EscapeWantedMinimumAttempted = false;
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        JusticeMarkStateDirty();

        ShowStatus(
            "Justice : ÉVASION confirmée. Mandat fugitif, recherche 3 étoiles demandée.",
            6000);
        LogInfo("Justice.Evasion", "Evasion enregistree exactement une fois pour " + episode + ".");
    }

    private void RetryJusticeEscapeWantedMinimum(int wantedLevel)
    {
        if (_justiceCaseState == null ||
            !_justiceCaseState.EscapeWantedMinimumPending)
        {
            return;
        }

        if (_justiceCaseState.Phase != JusticePhase.Fugitive ||
            !_justiceCaseState.HasWarrant)
        {
            _justiceCaseState.EscapeWantedMinimumPending = false;
            _justiceCaseState.EscapeWantedMinimumAttempted = false;
            JusticeMarkStateDirty();
            return;
        }

        if (wantedLevel >= JusticePolicy.EscapeMinimumWantedLevel)
        {
            _justiceCaseState.EscapeWantedMinimumPending = false;
            _justiceCaseState.EscapeWantedMinimumAttempted = false;
            StartJusticePursuitEpisodeIfNeeded();
            JusticeMarkStateDirty();
            JusticeFlushStateNow();
            return;
        }

        if (_justiceCaseState.EscapeWantedMinimumAttempted &&
            !IsJusticeCriticalBarrierPending(nameof(RetryJusticeEscapeWantedMinimum)))
        {
            // Après un redémarrage, un essai précommitté mais non acquitté est
            // ambigu : je privilégie at-most-once et ne remonte jamais des
            // étoiles que GTA aurait déjà fait redescendre naturellement.
            _justiceCaseState.EscapeWantedMinimumPending = false;
            _justiceCaseState.EscapeWantedMinimumAttempted = false;
            JusticeMarkStateDirty();
            JusticeFlushStateNow();
            LogWarning(
                "Justice.Evasion",
                "Essai wanted ambigu repris sans nouvelle écriture GTA.");
            return;
        }

        if (!_justiceCaseState.EscapeWantedMinimumAttempted)
        {
            _justiceCaseState.EscapeWantedMinimumAttempted = true;
            JusticeMarkStateDirty();
        }
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return;
        }

        bool applied = SetJusticeWantedMinimum(JusticePolicy.EscapeMinimumWantedLevel);
        if (applied)
        {
            _justiceCaseState.EscapeWantedMinimumPending = false;
            _justiceCaseState.EscapeWantedMinimumAttempted = false;
            StartJusticePursuitEpisodeIfNeeded();
            JusticeMarkStateDirty();
            JusticeFlushStateNow();
            return;
        }

        // Une erreur native explicitement connue peut être retentée, mais je
        // rends d'abord ce droit durable. Un crash avant ce commit reste donc
        // at-most-once et n'écrira pas des étoiles à tort au prochain lancement.
        _justiceCaseState.EscapeWantedMinimumAttempted = false;
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
    }

    private bool JusticeRegisterCustodyDisciplineCharge(
        JusticeCrimeKind kind,
        int minimumPenaltySeconds,
        string reason,
        string incidentId)
    {
        if (minimumPenaltySeconds <= 0 || _justiceCaseState == null ||
            string.IsNullOrWhiteSpace(incidentId))
        {
            return false;
        }

        int remainingSentence = Math.Max(0, _justiceCaseState.SentenceSeconds);
        long remainingFine = Math.Max(0L, _justiceCaseState.FineDue);
        string episode = string.IsNullOrWhiteSpace(_justiceCaseState.CustodyEpisodeId)
            ? CurrentJusticeEpisodeId()
            : _justiceCaseState.CustodyEpisodeId;
        string normalizedIncidentId = incidentId.Trim();
        for (int index = 0; index < _justiceCaseState.ProcessedIncidentIds.Count; index++)
        {
            if (string.Equals(
                _justiceCaseState.ProcessedIncidentIds[index],
                normalizedIncidentId,
                StringComparison.Ordinal))
            {
                // Je peux reprendre après un arrêt situé entre l'ajout de la
                // charge et l'effacement de l'intention sans rejuger la faute.
                bool resumesPersistedIntent = _justiceDisciplineIntent != null &&
                    string.Equals(
                        _justiceDisciplineIntent.IncidentId,
                        normalizedIncidentId,
                        StringComparison.Ordinal);
                return resumesPersistedIntent &&
                       IsJusticeDisciplineIntentWalConsistent(_justiceDisciplineIntent) &&
                       JusticeFlushStateNow();
            }
        }

        string disciplineEpisode = episode + ":discipline:" + normalizedIncidentId;
        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = normalizedIncidentId,
            // Je donne à chaque faute prouvée son propre sous-épisode. Deux
            // fautes distinctes restent donc sanctionnées, tandis qu'un rejeu
            // du même incident conserve exactement la même clé idempotente.
            EpisodeId = disciplineEpisode,
            Kind = kind,
            CreatedAtMs = _justiceMonotonicTimeMs,
            ExpiresAtMs = _justiceMonotonicTimeMs + JusticePolicy.PendingIncidentLifetimeMs,
            Circumstances = JusticeCircumstances.InCustody,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ObservedAtMs = _justiceMonotonicTimeMs,
                ReportDueAtMs = _justiceMonotonicTimeMs,
                ReportCompleted = true
            },
            IsConfirmed = true
        };
        JusticeCharge charge = JusticePolicy.ApplyConfirmedIncident(
            _justiceCaseState,
            incident,
            _justiceRecordState);
        if (charge == null)
        {
            return false;
        }

        int addedSentence = Math.Max(minimumPenaltySeconds, Math.Max(0, charge.SentenceSeconds));
        charge.SentenceSeconds = addedSentence;
        _justiceCaseState.SentenceSeconds = (int)Math.Min(
            JusticePolicy.MaxActiveSentenceSeconds,
            (long)remainingSentence + addedSentence);
        _justiceCaseState.FineDue = JusticePolicy.SaturatingAdd(
            remainingFine,
            Math.Max(0L, charge.Fine),
            JusticePolicy.MaxActiveFine);
        _justiceCaseState.LastCrimeLabel = string.IsNullOrWhiteSpace(reason)
            ? charge.DisplayName
            : reason.Trim();

        // Je juge uniquement la nouvelle faute dans un dossier temporaire : le
        // casier progresse sans condamner une seconde fois tout le dossier initial.
        JusticeCaseState disciplineCase = new JusticeCaseState
        {
            Enabled = true,
            // Je donne à chaque faute disciplinaire son propre jugement
            // idempotent, distinct de la condamnation qui a ouvert la détention.
            CustodyEpisodeId = disciplineEpisode
        };
        disciplineCase.Charges.Add(charge);
        disciplineCase.RecalculateTotals();
        JusticePolicy.ApplyConviction(disciplineCase, _justiceRecordState, DateTime.UtcNow);

        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }
        ShowStatus(
            "Justice : " + charge.DisplayName + " en détention, +" +
            FormatJusticeDuration(addedSentence) + ".",
            3600);
        LogInfo(
            "Justice.Discipline",
            charge.DisplayName + " | incident=" + incident.IncidentId +
            " | peine ajoutée=" + addedSentence.ToString(CultureInfo.InvariantCulture) + " s.");
        return true;
    }

    private void RecordJusticeAllyPoliceEngagement(Ped ally, Ped target, bool structured)
    {
        if (!_justiceEnabled || !_justicePursuitActive || !HasActiveJusticeCase() ||
            !Entity.Exists(ally) || ally.IsDead || !Entity.Exists(target) || target.IsDead ||
            !IsJusticeOwnedAlly(ally) || !IsJusticePolicePed(target))
        {
            return;
        }

        Ped player = Game.Player.Character;
        if (!Entity.Exists(player) || JusticeIsCustodyActive || IsJusticeRuntimeSuspended(player) ||
            ally.Position.DistanceTo(player.Position) > JusticeAllyAttributionRadius ||
            target.Position.DistanceTo(player.Position) > JusticeAllyAttributionRadius)
        {
            return;
        }

        int allyGeneration = GetJusticeEntityGeneration(ally);
        int targetGeneration = GetJusticeEntityGeneration(target);
        string episode = CurrentJusticeEpisodeId();

        for (int index = 0; index < _justiceAllyTokens.Count; index++)
        {
            JusticeAllyCausalToken existing = _justiceAllyTokens[index];
            if (existing.Ally.Handle == ally.Handle && existing.AllyGeneration == allyGeneration &&
                existing.PoliceTarget.Handle == target.Handle && existing.TargetGeneration == targetGeneration &&
                string.Equals(existing.EpisodeId, episode, StringComparison.Ordinal))
            {
                existing.ExpiresAtMs = _justiceMonotonicTimeMs + JusticeAllyAttributionLifetimeMs;
                existing.Structured |= structured;
                return;
            }
        }

        if (_justiceAllyTokens.Count >= JusticeMaximumAllyTokens)
        {
            _justiceAllyTokens.RemoveAt(0);
        }

        // Je photographie le couple avant le premier ordre causal : un dégât
        // autonome plus ancien de cet allié ne peut pas être redaté par le jeton.
        SynchronizeJusticeDamagePair(target, ally);

        _justiceAllyTokens.Add(new JusticeAllyCausalToken
        {
            Ally = ally,
            PoliceTarget = target,
            AllyGeneration = allyGeneration,
            TargetGeneration = targetGeneration,
            WasDonJOwnedAtCreation = true,
            EpisodeId = episode,
            CreatedAtMs = _justiceMonotonicTimeMs,
            ExpiresAtMs = _justiceMonotonicTimeMs + JusticeAllyAttributionLifetimeMs,
            LastObservedDamageAtMs = -1L,
            Structured = structured
        });
    }

    private void ProcessJusticeAllyAttributionTokens(Ped player)
    {
        if (_justiceMonotonicTimeMs < _justiceNextAllyAttributionScanAtMs)
        {
            return;
        }

        _justiceNextAllyAttributionScanAtMs = _justiceMonotonicTimeMs + JusticeAllyAttributionScanIntervalMs;

        for (int index = _justiceAllyTokens.Count - 1; index >= 0; index--)
        {
            JusticeAllyCausalToken token = _justiceAllyTokens[index];
            if (!IsJusticeAllyTokenValid(token, player))
            {
                _justiceAllyTokens.RemoveAt(index);
                continue;
            }

            bool damaged = TryCaptureJusticeDamageFront(token.PoliceTarget, token.Ally);
            if (damaged)
            {
                token.LastObservedDamageAtMs = _justiceMonotonicTimeMs;
            }
            bool killed = token.PoliceTarget.IsDead && IsJusticeDeathAttributedTo(
                token.PoliceTarget,
                player,
                token.Ally,
                token.LastObservedDamageAtMs,
                token.AllyGeneration);
            JusticeCircumstances collective = GetJusticeCollectiveCircumstances(token, player);

            if (killed && !token.HomicideQueued)
            {
                token.HomicideQueued = QueueJusticeIncident(
                    JusticeCrimeKind.AccessoryMurderOfficer,
                    token.PoliceTarget,
                    token.PoliceTarget,
                    token.Ally.Handle,
                    collective,
                    true,
                    0,
                    null,
                    token.AllyGeneration,
                    token.Ally,
                    token.WasDonJOwnedAtCreation);
            }
            else if (damaged && !token.AssaultQueued)
            {
                token.AssaultQueued = QueueJusticeIncident(
                    JusticeCrimeKind.AccessoryAssaultOfficer,
                    token.PoliceTarget,
                    token.PoliceTarget,
                    token.Ally.Handle,
                    collective,
                true,
                0,
                null,
                token.AllyGeneration,
                token.Ally,
                token.WasDonJOwnedAtCreation);
            }
        }
    }

    private JusticeCircumstances GetJusticeCollectiveCircumstances(
        JusticeAllyCausalToken token,
        Ped player)
    {
        int alliesOnTarget = 0;
        for (int index = 0; index < _justiceAllyTokens.Count; index++)
        {
            JusticeAllyCausalToken candidate = _justiceAllyTokens[index];
            if (candidate != null && candidate.PoliceTarget != null && token.PoliceTarget != null &&
                candidate.PoliceTarget.Handle == token.PoliceTarget.Handle &&
                candidate.TargetGeneration == token.TargetGeneration &&
                string.Equals(candidate.EpisodeId, token.EpisodeId, StringComparison.Ordinal) &&
                IsJusticeAllyTokenValid(candidate, player))
            {
                alliesOnTarget++;
            }
        }

        JusticeCircumstances result = GetJusticeBaseCircumstances();
        if (token.Structured || alliesOnTarget >= 2)
        {
            result |= JusticeCircumstances.OrganizedBand;
        }
        else
        {
            result |= JusticeCircumstances.GroupCrime;
        }
        return result;
    }

    private bool IsJusticeAllyTokenValid(JusticeAllyCausalToken token, Ped player)
    {
        return _justiceEnabled && _justicePursuitActive && !JusticeIsCustodyActive &&
               !IsJusticeRuntimeSuspended(player) &&
               token != null && token.ExpiresAtMs >= _justiceMonotonicTimeMs &&
               string.Equals(token.EpisodeId, CurrentJusticeEpisodeId(), StringComparison.Ordinal) &&
               Entity.Exists(token.Ally) &&
               Entity.Exists(token.PoliceTarget) &&
               HasJusticeValidAllyOwnership(
                   IsJusticeOwnedAlly(token.Ally),
                   token.Ally.IsDead,
                   token.WasDonJOwnedAtCreation) &&
               IsJusticePolicePed(token.PoliceTarget) &&
               GetJusticeEntityGeneration(token.Ally) == token.AllyGeneration &&
               GetJusticeEntityGeneration(token.PoliceTarget) == token.TargetGeneration &&
               token.Ally.Position.DistanceTo(player.Position) <= JusticeAllyAttributionRadius &&
               token.PoliceTarget.Position.DistanceTo(player.Position) <= JusticeAllyAttributionRadius;
    }

    private static bool HasJusticeValidAllyOwnership(
        bool currentlyOwnedByDonJ,
        bool allyIsDead,
        bool wasDonJOwnedAtCreation)
    {
        // Je conserve la preuve d'appartenance prise à la création du jeton
        // uniquement pour le cadavre de ce même allié. Une entité vivante ou
        // autonome doit toujours appartenir actuellement aux entités DonJ.
        return currentlyOwnedByDonJ || allyIsDead && wasDonJOwnedAtCreation;
    }

    private bool IsJusticeRuntimeSuspended(Ped player)
    {
        if (_justiceMonotonicTimeMs < _justiceNextSuspensionCheckAtMs)
        {
            return _justiceRuntimeSuspendedCached;
        }

        _justiceNextSuspensionCheckAtMs = _justiceMonotonicTimeMs + JusticeScalarScanIntervalMs;
        _justiceRuntimeSuspendedCached = ComputeJusticeRuntimeSuspended(player);
        return _justiceRuntimeSuspendedCached;
    }

    private bool ComputeJusticeRuntimeSuspended(Ped player)
    {
        if (_justiceProfileSelectionPending &&
            !IsJusticeCanonicalProfileSlot(GetJusticeCanonicalPlayerSlotSafe()))
        {
            return true;
        }

        if (_justiceBackupRepairPending &&
            (_justiceMonotonicTimeMs < _justiceNextBackupRepairAtMs ||
             !TryRepairJusticePrimaryFromLoadedBackup()))
        {
            return true;
        }

        if (!Entity.Exists(player))
        {
            return true;
        }

        try
        {
            if (Game.IsPaused)
            {
                return true;
            }
        }
        catch
        {
        }

        if (CallJusticeBooleanNativeWithCircuit(JusticeNativeGetIsLoadingScreenActive, JusticeCircuitLoading, true) ||
            CallJusticeBooleanNativeWithCircuit(JusticeNativeGetMissionFlag, JusticeCircuitMission, true) ||
            CallJusticeBooleanNativeWithCircuit(JusticeNativeIsCutsceneActive, JusticeCircuitCutscene, true) ||
            CallJusticeBooleanNativeWithCircuit(JusticeNativeIsPlayerSwitchInProgress, JusticeCircuitPlayerSwitch, true))
        {
            return true;
        }

        return false;
    }

    private bool CallJusticeBooleanNativeWithCircuit(
        ulong nativeHash,
        int circuit,
        bool failureFallback,
        params InputArgument[] arguments)
    {
        int circuitIndex = GetJusticeCircuitIndex(circuit);
        if (_justiceNativeCircuitRetryAtMs == null)
        {
            _justiceNativeCircuitRetryAtMs = new long[32];
        }
        bool circuitWasOpen = (_justiceUnavailableNativeCircuits & circuit) != 0;
        if (circuitWasOpen &&
            circuitIndex >= 0 &&
            _justiceMonotonicTimeMs < _justiceNativeCircuitRetryAtMs[circuitIndex])
        {
            return failureFallback;
        }
        _justiceUnavailableNativeCircuits &= ~circuit;

        try
        {
            bool value = Function.Call<bool>((Hash)nativeHash, arguments);
            if (circuitIndex >= 0)
            {
                _justiceNativeCircuitRetryAtMs[circuitIndex] = 0L;
            }
            if ((_justiceLoggedUnavailableNativeCircuits & circuit) != 0)
            {
                _justiceLoggedUnavailableNativeCircuits &= ~circuit;
                LogInfo(
                    "Justice.Native",
                    "Native 0x" + nativeHash.ToString("X16", CultureInfo.InvariantCulture) +
                    " de nouveau disponible.");
            }
            return value;
        }
        catch (Exception ex)
        {
            _justiceUnavailableNativeCircuits |= circuit;
            if (circuitIndex >= 0)
            {
                _justiceNativeCircuitRetryAtMs[circuitIndex] =
                    _justiceMonotonicTimeMs + JusticeNativeCircuitRetryMs;
            }
            if ((_justiceLoggedUnavailableNativeCircuits & circuit) == 0)
            {
                _justiceLoggedUnavailableNativeCircuits |= circuit;
                LogWarning(
                    "Justice.Native",
                    "Coupe-circuit activé pour 0x" + nativeHash.ToString("X16", CultureInfo.InvariantCulture) +
                    " : " + ex.GetType().Name + ".");
            }
            return failureFallback;
        }
    }

    private bool TryCallJusticeBooleanNativeWithCircuit(
        ulong nativeHash,
        int circuit,
        out bool value,
        params InputArgument[] arguments)
    {
        value = CallJusticeBooleanNativeWithCircuit(
            nativeHash,
            circuit,
            false,
            arguments);
        return (_justiceUnavailableNativeCircuits & circuit) == 0;
    }

    private bool CanAttemptJusticeCircuit(int circuit)
    {
        int circuitIndex = GetJusticeCircuitIndex(circuit);
        if (_justiceNativeCircuitRetryAtMs == null)
        {
            _justiceNativeCircuitRetryAtMs = new long[32];
        }
        return (_justiceUnavailableNativeCircuits & circuit) == 0 ||
               circuitIndex < 0 ||
               _justiceMonotonicTimeMs >= _justiceNativeCircuitRetryAtMs[circuitIndex];
    }

    private void MarkJusticeCircuitFailure(int circuit, string operation, Exception exception)
    {
        int circuitIndex = GetJusticeCircuitIndex(circuit);
        if (_justiceNativeCircuitRetryAtMs == null)
        {
            _justiceNativeCircuitRetryAtMs = new long[32];
        }
        _justiceUnavailableNativeCircuits |= circuit;
        if (circuitIndex >= 0)
        {
            _justiceNativeCircuitRetryAtMs[circuitIndex] =
                _justiceMonotonicTimeMs + JusticeNativeCircuitRetryMs;
        }
        if ((_justiceLoggedUnavailableNativeCircuits & circuit) != 0)
        {
            return;
        }
        _justiceLoggedUnavailableNativeCircuits |= circuit;
        LogWarning(
            "Justice.Native",
            "Coupe-circuit activé pour " + operation + " : " +
            (exception == null ? "erreur inconnue" : exception.GetType().Name) + ".");
    }

    private void MarkJusticeCircuitRecovered(int circuit, string operation)
    {
        int circuitIndex = GetJusticeCircuitIndex(circuit);
        _justiceUnavailableNativeCircuits &= ~circuit;
        if (_justiceNativeCircuitRetryAtMs != null && circuitIndex >= 0)
        {
            _justiceNativeCircuitRetryAtMs[circuitIndex] = 0L;
        }
        if ((_justiceLoggedUnavailableNativeCircuits & circuit) == 0)
        {
            return;
        }
        _justiceLoggedUnavailableNativeCircuits &= ~circuit;
        LogInfo("Justice.Native", operation + " de nouveau disponible.");
    }

    private static int GetJusticeCircuitIndex(int circuit)
    {
        if (circuit <= 0 || (circuit & (circuit - 1)) != 0)
        {
            return -1;
        }

        int index = 0;
        while ((circuit >>= 1) != 0)
        {
            index++;
        }
        return index;
    }

    private int CallJusticeIntegerNativeWithCircuit(
        ulong nativeHash,
        int circuit,
        int fallback,
        params InputArgument[] arguments)
    {
        int value;
        TryCallJusticeIntegerNativeWithCircuit(
            nativeHash,
            circuit,
            fallback,
            out value,
            arguments);
        return value;
    }

    private bool TryCallJusticeIntegerNativeWithCircuit(
        ulong nativeHash,
        int circuit,
        int fallback,
        out int value,
        params InputArgument[] arguments)
    {
        int circuitIndex = GetJusticeCircuitIndex(circuit);
        if (_justiceNativeCircuitRetryAtMs == null)
        {
            _justiceNativeCircuitRetryAtMs = new long[32];
        }
        bool circuitWasOpen = (_justiceUnavailableNativeCircuits & circuit) != 0;
        if (circuitWasOpen && circuitIndex >= 0 &&
            _justiceMonotonicTimeMs < _justiceNativeCircuitRetryAtMs[circuitIndex])
        {
            value = fallback;
            return false;
        }
        _justiceUnavailableNativeCircuits &= ~circuit;

        try
        {
            value = Function.Call<int>((Hash)nativeHash, arguments);
            if (circuitIndex >= 0)
            {
                _justiceNativeCircuitRetryAtMs[circuitIndex] = 0L;
            }
            if ((_justiceLoggedUnavailableNativeCircuits & circuit) != 0)
            {
                _justiceLoggedUnavailableNativeCircuits &= ~circuit;
                LogInfo(
                    "Justice.Native",
                    "Native 0x" + nativeHash.ToString("X16", CultureInfo.InvariantCulture) +
                    " de nouveau disponible.");
            }
            return true;
        }
        catch (Exception ex)
        {
            _justiceUnavailableNativeCircuits |= circuit;
            if (circuitIndex >= 0)
            {
                _justiceNativeCircuitRetryAtMs[circuitIndex] =
                    _justiceMonotonicTimeMs + JusticeNativeCircuitRetryMs;
            }
            if ((_justiceLoggedUnavailableNativeCircuits & circuit) == 0)
            {
                _justiceLoggedUnavailableNativeCircuits |= circuit;
                LogWarning(
                    "Justice.Native",
                    "Coupe-circuit activé pour 0x" + nativeHash.ToString("X16", CultureInfo.InvariantCulture) +
                    " : " + ex.GetType().Name + ".");
            }
            value = fallback;
            return false;
        }
    }

    private void AdvanceJusticeMonotonicClock()
    {
        int raw = GetJusticeRawGameTimeSafe();
        uint elapsed = unchecked((uint)(raw - _justiceLastRawGameTime));

        // Je rejette un saut de plus de dix minutes : il correspond à un loader,
        // une reprise ou une horloge invalide, jamais à une peine réellement jouée.
        if (elapsed <= 600000U)
        {
            _justiceMonotonicTimeMs += elapsed;
        }

        _justiceLastRawGameTime = raw;
    }

    private static int GetJusticeRawGameTimeSafe()
    {
        try
        {
            return Game.GameTime;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsJusticePlayerDeadSafe(Ped player)
    {
        try
        {
            return !Entity.Exists(player) || player.IsDead || Game.Player.IsDead;
        }
        catch
        {
            return !Entity.Exists(player) || player.IsDead;
        }
    }

    private int GetJusticeWantedLevelSafe()
    {
        try
        {
            return Math.Max(0, Math.Min(5, Game.Player.WantedLevel));
        }
        catch
        {
            // Une lecture indisponible conserve le dernier niveau connu : elle ne
            // peut donc jamais fabriquer une perte de poursuite ou un mandat.
            return Math.Max(0, Math.Min(5, _justiceLastWantedLevel));
        }
    }

    private bool SetJusticeWantedMinimum(int wantedFloor)
    {
        int bounded = Math.Max(0, Math.Min(5, wantedFloor));
        if (bounded <= 0)
        {
            return false;
        }

        if (_justiceWantedWriteOverride != null)
        {
            bool accepted = _justiceWantedWriteOverride(bounded);
            if (accepted)
            {
                _justiceLastWantedLevel = Math.Max(_justiceLastWantedLevel, bounded);
            }
            return accepted;
        }

        try
        {
            int current = Game.Player.WantedLevel;
            if (current < bounded)
            {
                Game.Player.WantedLevel = bounded;
                current = Game.Player.WantedLevel;
                if (current < bounded)
                {
                    return false;
                }
                _justiceWrittenWantedLevel = bounded;
                _justiceWrittenWantedExpiresAtMs =
                    _justiceMonotonicTimeMs + JusticeWantedWriteSuppressionMs;
            }
            _justiceLastWantedLevel = Math.Max(
                _justiceLastWantedLevel,
                Math.Max(current, bounded));
            return true;
        }
        catch
        {
            // Une écriture wanted refusée ne doit jamais casser le tick du mod.
            return false;
        }
    }

    private bool ClearJusticeWantedLevelOnce()
    {
        return ClearJusticeWantedLevelOnceDetailed() ==
               JusticeWantedClearResult.Succeeded;
    }

    private JusticeWantedClearResult ClearJusticeWantedLevelOnceDetailed()
    {
        if (_justiceWantedClearObservationOverride != null)
        {
            try
            {
                int? simulated = _justiceWantedClearObservationOverride();
                if (!simulated.HasValue)
                {
                    return JusticeWantedClearResult.Unknown;
                }
                if (simulated.Value != 0)
                {
                    return JusticeWantedClearResult.Rejected;
                }

                _justiceLastWantedLevel = 0;
                _justiceWrittenWantedLevel = 0;
                _justiceWrittenWantedExpiresAtMs = 0L;
                _justiceWantedClearPending = false;
                _justiceNextWantedClearRetryAtMs = 0L;
                _justiceWantedClearRetryUntilMs = 0L;
                return JusticeWantedClearResult.Succeeded;
            }
            catch
            {
                return JusticeWantedClearResult.Unknown;
            }
        }

        try
        {
            Function.Call((Hash)JusticeNativeClearPlayerWantedLevel, Game.Player.Handle);
        }
        catch
        {
        }

        int observed;
        if (!TryReadJusticeWantedLevel(out observed) || observed != 0)
        {
            try
            {
                Game.Player.WantedLevel = 0;
            }
            catch
            {
            }
        }

        bool finalReadSucceeded = TryReadJusticeWantedLevel(out observed);
        if (!finalReadSucceeded || observed != 0)
        {
            return finalReadSucceeded
                ? JusticeWantedClearResult.Rejected
                : JusticeWantedClearResult.Unknown;
        }
        _justiceLastWantedLevel = 0;
        _justiceWrittenWantedLevel = 0;
        _justiceWrittenWantedExpiresAtMs = 0L;
        _justiceWantedClearPending = false;
        _justiceNextWantedClearRetryAtMs = 0L;
        _justiceWantedClearRetryUntilMs = 0L;
        return JusticeWantedClearResult.Succeeded;
    }

    private void RetryJusticeWantedClearAfterAmnesty()
    {
        if (!_justiceWantedClearPending ||
            _justiceMonotonicTimeMs < _justiceNextWantedClearRetryAtMs)
        {
            return;
        }

        if (_justiceEnabled || HasActiveJusticeCase() || JusticeIsCustodyActive)
        {
            // Un nouveau dossier rend le jeton d'amnistie obsolète. Il ne doit
            // jamais effacer les étoiles d'une poursuite créée après sa validation.
            CancelJusticeWantedClearRetry();
            return;
        }

        if (_justiceMonotonicTimeMs > _justiceWantedClearRetryUntilMs)
        {
            CancelJusticeWantedClearRetry();
            LogWarning(
                "Justice.Amnistie",
                "Le wanted GTA n'a pas pu être vérifié à zéro dans la fenêtre de reprise.");
            return;
        }

        if (ClearJusticeWantedLevelOnce())
        {
            LogInfo("Justice.Amnistie", "Wanted GTA effacé lors de la reprise bornée.");
            return;
        }
        _justiceNextWantedClearRetryAtMs =
            _justiceMonotonicTimeMs + JusticeWantedClearRetryMs;
    }

    private void CancelJusticeWantedClearRetry()
    {
        _justiceWantedClearPending = false;
        _justiceNextWantedClearRetryAtMs = 0L;
        _justiceWantedClearRetryUntilMs = 0L;
    }

    private bool TryReadJusticeWantedLevel(out int wantedLevel)
    {
        wantedLevel = 0;
        try
        {
            wantedLevel = Math.Max(0, Math.Min(5, Game.Player.WantedLevel));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetJusticePlayerBeingArrestedSafe(out bool arrested)
    {
        arrested = CallJusticeBooleanNativeWithCircuit(
            JusticeNativeIsPlayerBeingArrested,
            JusticeCircuitArrestState,
            _justiceWasBeingArrested,
            Game.Player.Handle,
            true);
        return (_justiceUnavailableNativeCircuits & JusticeCircuitArrestState) == 0;
    }

    private bool TryGetJusticeArrestConfirmedSafe(out bool confirmed)
    {
        return TryGetJusticeArrestConfirmedSafe(false, out confirmed);
    }

    private bool TryGetJusticeArrestConfirmedSafe(
        bool preservePendingWhenUnconfirmed,
        out bool confirmed)
    {
        int sinceArrest;
        bool valid = TryCallJusticeIntegerNativeWithCircuit(
            JusticeNativeGetTimeSinceLastArrest,
            JusticeCircuitLastArrest,
            -1,
            out sinceArrest);
        long pendingProbeElapsed = _justiceArrestCompletionProbePending
            ? Math.Max(0L, _justiceMonotonicTimeMs - _justiceArrestCompletionProbeStartedAtMs)
            : 0L;
        confirmed = valid && JusticePolicy.IsArrestCompletionWithinProbeWindow(
            sinceArrest,
            pendingProbeElapsed);
        if (valid && (!preservePendingWhenUnconfirmed || confirmed))
        {
            _justiceArrestCompletionProbePending = false;
            _justiceArrestCompletionProbeStartedAtMs = 0L;
        }
        return valid;
    }

    private bool IsJusticeRecentNativeTimer(ulong nativeHash, int circuit, int maximumMilliseconds)
    {
        int elapsed = CallJusticeIntegerNativeWithCircuit(
            nativeHash,
            circuit,
            -1,
            Game.Player.Handle);
        return elapsed >= 0 && elapsed <= maximumMilliseconds;
    }

    private static bool IsJusticePedJackingSafe(Ped player)
    {
        try
        {
            return Entity.Exists(player) && player.IsJacking;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticePedInCombatSafe(Ped ped)
    {
        try
        {
            return Entity.Exists(ped) && ped.IsInCombat;
        }
        catch
        {
            return false;
        }
    }

    private static Ped GetJusticeJackTargetSafe(Ped player)
    {
        try
        {
            return Entity.Exists(player) ? player.GetJackTarget() : null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsJusticePlayerSpottedInStolenVehicleSafe()
    {
        return CallJusticeBooleanNativeWithCircuit(
            JusticeNativeHasPlayerBeenSpottedInStolenVehicle,
            JusticeCircuitStolenVehicleReport,
            false,
            Game.Player.Handle);
    }

    private static Vehicle GetJusticeCurrentVehicleSafe(Ped player)
    {
        try
        {
            return Entity.Exists(player) && player.IsInVehicle() ? player.CurrentVehicle : null;
        }
        catch
        {
            return null;
        }
    }

    private static Vehicle GetJusticeLastVehicleSafe(Ped player)
    {
        try
        {
            return Entity.Exists(player) ? player.LastVehicle : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasJusticeEntityBeenDamagedBy(Entity victim, Entity attacker)
    {
        if (!Entity.Exists(victim) || !Entity.Exists(attacker))
        {
            return false;
        }

        try
        {
            return victim.HasBeenDamagedBy(attacker) ||
                   Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, victim.Handle, attacker.Handle, true);
        }
        catch
        {
            try
            {
                return victim.HasBeenDamagedBy(attacker);
            }
            catch
            {
                return false;
            }
        }
    }

    private bool IsJusticeHumanCandidate(Ped candidate, Ped player)
    {
        return IsJusticePotentialVictimCandidate(candidate, player) && !candidate.IsDead;
    }

    private bool IsJusticePotentialVictimCandidate(Ped candidate, Ped player)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player) ||
            candidate.Handle == player.Handle || IsJusticeOwnedAlly(candidate))
        {
            return false;
        }

        try
        {
            return candidate.IsHuman;
        }
        catch
        {
            try
            {
                return Function.Call<bool>(Hash.IS_PED_HUMAN, candidate.Handle);
            }
            catch
            {
                return false;
            }
        }
    }

    private bool IsJusticeOwnedAlly(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int handle = ped.Handle;
        if (_cartelNpcHandles.Contains(handle) || _cartelDismissingNpcRecords.ContainsKey(handle) ||
            _highSecurityEscortKnownNpcHandles.Contains(handle))
        {
            return true;
        }

        SpawnedNpc record = FindSpawnedNpcByHandle(handle);
        return record != null &&
               (record.Behavior == NpcBehavior.Ally ||
                record.Behavior == NpcBehavior.Bodyguard ||
                record.Behavior == NpcBehavior.AllyPatrol ||
                record.BaseBehavior == NpcBehavior.Ally ||
                record.BaseBehavior == NpcBehavior.Bodyguard ||
                record.BaseBehavior == NpcBehavior.AllyPatrol);
    }

    private bool IsJusticePolicePed(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        int group = 0;
        try
        {
            group = GetPedRelationshipGroup(ped);
        }
        catch
        {
        }

        if (group == Game.GenerateHash("COP") ||
            group == Game.GenerateHash("SECURITY_GUARD") ||
            group == Game.GenerateHash("ARMY"))
        {
            return true;
        }

        int model = GetJusticeEntityModelHashSafe(ped);
        return model == Game.GenerateHash("s_m_y_cop_01") ||
               model == Game.GenerateHash("s_f_y_cop_01") ||
               model == Game.GenerateHash("s_m_y_hwaycop_01") ||
               model == Game.GenerateHash("s_m_y_sheriff_01") ||
               model == Game.GenerateHash("s_f_y_sheriff_01") ||
               model == Game.GenerateHash("s_m_y_swat_01") ||
               model == Game.GenerateHash("s_m_m_prisguard_01");
    }

    private bool CanPedSeeJusticeEvent(Ped witness, Ped actor, Entity eventEntity)
    {
        if (!Entity.Exists(witness) || witness.IsDead || !Entity.Exists(actor) ||
            witness.Position.DistanceTo(actor.Position) > JusticeWitnessRadius)
        {
            return false;
        }

        bool seesActor = CanJusticePedSeeEntitySafe(witness, actor) &&
                          HasJusticeEntityInFront(witness, actor);
        if (!seesActor)
        {
            return false;
        }

        if (!Entity.Exists(eventEntity) || eventEntity.Handle == actor.Handle ||
            witness.Position.DistanceTo(eventEntity.Position) <= 14.0f)
        {
            return true;
        }

        return CanJusticePedSeeEntitySafe(witness, eventEntity) &&
               HasJusticeEntityInFront(witness, eventEntity);
    }

    private bool CanJusticePedSeeEntitySafe(Ped witness, Entity target)
    {
        if (!Entity.Exists(witness) || !Entity.Exists(target) ||
            !CanAttemptJusticeCircuit(JusticeCircuitCanSeeEntity))
        {
            return false;
        }

        try
        {
            bool visible = CanPedSeeEntity(witness, target, JusticeWitnessRadius);
            MarkJusticeCircuitRecovered(JusticeCircuitCanSeeEntity, "test de visibilité");
            return visible;
        }
        catch (Exception ex)
        {
            MarkJusticeCircuitFailure(JusticeCircuitCanSeeEntity, "test de visibilité", ex);
            return false;
        }
    }

    private bool HasJusticeEntityInFront(Ped witness, Entity target)
    {
        if (!Entity.Exists(witness) || !Entity.Exists(target))
        {
            return false;
        }

        return CallJusticeBooleanNativeWithCircuit(
            JusticeNativeHasEntityClearLosInFront,
            JusticeCircuitLineOfSight,
            false,
            witness.Handle,
            target.Handle);
    }

    private static bool IsJusticePlayerArmed(Ped player)
    {
        return IsJusticePedArmed(player);
    }

    private static bool IsJusticeExplosiveOrIncendiaryWeapon(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            int selected = Function.Call<int>((Hash)JusticeNativeGetSelectedPedWeapon, player.Handle);
            return selected == Game.GenerateHash("WEAPON_GRENADE") ||
                   selected == Game.GenerateHash("WEAPON_MOLOTOV") ||
                   selected == Game.GenerateHash("WEAPON_RPG") ||
                   selected == Game.GenerateHash("WEAPON_HOMINGLAUNCHER") ||
                   selected == Game.GenerateHash("WEAPON_GRENADELAUNCHER") ||
                   selected == Game.GenerateHash("WEAPON_RAILGUN");
        }
        catch
        {
            return false;
        }
    }

    private JusticeCircumstances GetJusticeBaseCircumstances()
    {
        JusticeCircumstances result = JusticeCircumstances.None;
        if (_justiceCaseState != null && _justiceCaseState.HasWarrant)
        {
            result |= JusticeCircumstances.ActiveWarrant;
        }
        if (JusticeIsCustodyActive)
        {
            result |= JusticeCircumstances.InCustody;
        }
        return result;
    }

    private JusticeCircumstances GetJusticeWeaponCircumstances(Ped player)
    {
        JusticeCircumstances result = JusticeCircumstances.None;
        if (IsJusticePlayerArmed(player))
        {
            result |= JusticeCircumstances.Armed;
        }
        if (IsJusticeExplosiveOrIncendiaryWeapon(player))
        {
            result |= JusticeCircumstances.ExplosiveOrIncendiary;
        }
        return result;
    }

    private JusticeCircumstances BuildJusticeAssaultCircumstances(
        Ped victim,
        Ped player,
        int victimGeneration,
        bool directPlayerDamage,
        bool vehicleWasWeapon,
        bool lethal)
    {
        JusticeCircumstances result = GetJusticeBaseCircumstances();
        if (directPlayerDamage)
        {
            result |= GetJusticeWeaponCircumstances(player);
        }
        if (vehicleWasWeapon)
        {
            result |= JusticeCircumstances.VehicleUsedAsWeapon;
        }

        string victimKey = BuildJusticeEntityKey(victim.Handle, victimGeneration);
        long defenseUntil;
        if (!IsJusticePolicePed(victim) &&
            _justiceSelfDefenseUntilByVictim.TryGetValue(victimKey, out defenseUntil) &&
            defenseUntil >= _justiceMonotonicTimeMs)
        {
            JusticeSelfDefenseThreat threat;
            bool hasThreat = _justiceSelfDefenseThreatByVictim.TryGetValue(victimKey, out threat) &&
                threat != null && threat.ExpiresAtMs >= _justiceMonotonicTimeMs;
            result |= JusticePolicy.ClassifySelfDefenseResponse(
                hasThreat && threat.Armed,
                hasThreat && threat.VehicleThreat,
                IsJusticePlayerArmed(player),
                vehicleWasWeapon,
                lethal);
        }

        return result;
    }

    private void RememberJusticePotentialAggressor(
        Ped candidate,
        Ped player,
        int generation,
        long causalDamageAtMs)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(player))
        {
            return;
        }

        try
        {
            if (IsJusticeCausalDamageFresh(causalDamageAtMs) && candidate.IsInCombatAgainst(player))
            {
                string key = BuildJusticeEntityKey(candidate.Handle, generation);
                // Une nouvelle attaque réarme toujours la fenêtre de huit
                // secondes ; une ancienne clé expirée ne bloque jamais une
                // future légitime défense réellement observée.
                _justiceSelfDefenseUntilByVictim[key] =
                    _justiceMonotonicTimeMs + JusticeSelfDefenseWindowMs;
                _justiceSelfDefenseThreatByVictim[key] = new JusticeSelfDefenseThreat
                {
                    ExpiresAtMs = _justiceMonotonicTimeMs + JusticeSelfDefenseWindowMs,
                    Armed = IsJusticePedArmed(candidate),
                    VehicleThreat = IsJusticePedUsingVehicleAsThreat(candidate, player)
                };
                PruneJusticeSelfDefenseMemory();
            }
        }
        catch
        {
        }
    }

    private void ObserveJusticeHostileInitiation(Ped candidate, Ped player, int generation)
    {
        if (!Entity.Exists(candidate) || candidate.IsDead || !Entity.Exists(player) ||
            IsPedShooting(player) || IsPedInMeleeCombatSafe(player))
        {
            return;
        }

        try
        {
            bool explicitAttack = candidate.IsInCombatAgainst(player) &&
                (IsPedShooting(candidate) || IsPedInMeleeCombatSafe(candidate));
            if (explicitAttack)
            {
                // Je peux ainsi mémoriser un tir manqué observé pendant le front
                // de combat, sans transformer une simple hostilité ambiante en preuve.
                RememberJusticePotentialAggressor(
                    candidate,
                    player,
                    generation,
                    _justiceMonotonicTimeMs);
            }
        }
        catch
        {
        }
    }

    private static bool IsJusticePedArmed(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return false;
        }

        try
        {
            int selected = Function.Call<int>((Hash)JusticeNativeGetSelectedPedWeapon, ped.Handle);
            return selected != 0 && selected != Game.GenerateHash("WEAPON_UNARMED");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticePedUsingVehicleAsThreat(Ped aggressor, Ped player)
    {
        if (!Entity.Exists(aggressor) || !Entity.Exists(player))
        {
            return false;
        }

        try
        {
            return IsJusticeVehicleImpactAttack(aggressor.CurrentVehicle, player);
        }
        catch
        {
            return false;
        }
    }

    private void PruneJusticeSelfDefenseMemory()
    {
        while (_justiceSelfDefenseUntilByVictim.Count > JusticeMaximumTrackedIdentities)
        {
            string candidateKey = null;
            long oldestExpiry = long.MaxValue;
            foreach (KeyValuePair<string, long> pair in _justiceSelfDefenseUntilByVictim)
            {
                if (pair.Value < oldestExpiry)
                {
                    oldestExpiry = pair.Value;
                    candidateKey = pair.Key;
                }
            }

            if (candidateKey == null)
            {
                break;
            }
            _justiceSelfDefenseUntilByVictim.Remove(candidateKey);
            _justiceSelfDefenseThreatByVictim.Remove(candidateKey);
        }
    }

    private JusticeCrimeKind ClassifyJusticeAssault(Ped victim, Ped player, bool vehicleWasWeapon)
    {
        if (IsJusticePolicePed(victim))
        {
            return JusticeCrimeKind.AssaultOfficer;
        }
        if (vehicleWasWeapon)
        {
            return JusticeCrimeKind.AggravatedAssault;
        }
        return IsJusticePlayerArmed(player)
            ? JusticeCrimeKind.AggravatedAssault
            : JusticeCrimeKind.SimpleAssault;
    }

    private static bool IsJusticeVehicleAttack(Ped player, Ped victim)
    {
        try
        {
            return IsJusticeVehicleImpactAttack(player.CurrentVehicle, victim);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeVehicleImpactAttack(Vehicle vehicle, Entity victim)
    {
        if (!Entity.Exists(vehicle) || !Entity.Exists(victim))
        {
            return false;
        }
        try
        {
            return JusticePolicy.IsVehicleImpactSevere(
                vehicle.Speed,
                vehicle.IsTouching(victim),
                PlayerVehicleHostilityMinSpeed);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeVehicleDestroyed(Vehicle vehicle)
    {
        if (!Entity.Exists(vehicle))
        {
            return false;
        }

        try
        {
            return vehicle.IsDead || !vehicle.IsDriveable || vehicle.EngineHealth <= 0.0f || vehicle.BodyHealth <= 0.0f;
        }
        catch
        {
            return vehicle.IsDead;
        }
    }

    private bool IsJusticeDeathAttributedTo(
        Ped victim,
        Ped player,
        Ped ally,
        long causalDamageAtMs = -1L,
        int allyGeneration = 0)
    {
        if (!Entity.Exists(victim) || !victim.IsDead)
        {
            return false;
        }

        try
        {
            Entity killer = victim.GetKiller();
            if (Entity.Exists(killer))
            {
                if (Entity.Exists(ally) && killer.Handle == ally.Handle &&
                    (allyGeneration <= 0 ||
                     GetJusticeEntityGeneration(killer) == allyGeneration))
                {
                    return true;
                }
                if (Entity.Exists(player) && killer.Handle == player.Handle)
                {
                    return true;
                }
                Vehicle current = Entity.Exists(player) ? player.CurrentVehicle : null;
                Vehicle last = Entity.Exists(player) ? player.LastVehicle : null;
                if ((Entity.Exists(current) && killer.Handle == current.Handle) ||
                    (Entity.Exists(last) && killer.Handle == last.Handle))
                {
                    return true;
                }

                Vehicle allyCurrent = Entity.Exists(ally) ? ally.CurrentVehicle : null;
                Vehicle allyLast = Entity.Exists(ally) ? ally.LastVehicle : null;
                if ((Entity.Exists(allyCurrent) && killer.Handle == allyCurrent.Handle) ||
                    (Entity.Exists(allyLast) && killer.Handle == allyLast.Handle))
                {
                    return true;
                }

                // Un tueur valide tiers est une preuve négative : je ne retombe
                // jamais sur un ancien flag de dégâts pour l'imputer au joueur.
                return false;
            }
        }
        catch
        {
        }

        // Si GTA ne fournit plus le tueur, je n'utilise jamais son historique de
        // dégâts persistant : seul le front causal capturé depuis moins de six
        // secondes peut encore prouver l'homicide.
        return IsJusticeCausalDamageFresh(causalDamageAtMs) &&
               (Entity.Exists(ally) || Entity.Exists(player));
    }

    private static bool WasJusticeDeathCausedByPlayerVehicle(Ped victim, Ped player)
    {
        if (!Entity.Exists(victim) || !Entity.Exists(player))
        {
            return false;
        }

        try
        {
            Entity killer = victim.GetKiller();
            Vehicle current = GetJusticeCurrentVehicleSafe(player);
            Vehicle last = GetJusticeLastVehicleSafe(player);
            return Entity.Exists(killer) &&
                   ((Entity.Exists(current) && killer.Handle == current.Handle) ||
                    (Entity.Exists(last) && killer.Handle == last.Handle));
        }
        catch
        {
            return false;
        }
    }

    private bool IsJusticePedDeathFresh(Ped victim)
    {
        if (!Entity.Exists(victim) || !victim.IsDead ||
            !_justiceDeathDetectionBarrierInitialized)
        {
            return false;
        }

        int deathAt;
        if (!TryCallJusticeIntegerNativeWithCircuit(
            JusticeNativeGetPedTimeOfDeath,
            JusticeCircuitPedTimeOfDeath,
            0,
            out deathAt,
            victim.Handle))
        {
            return false;
        }
        int now = GetJusticeRawGameTimeSafe();
        int age = unchecked(now - deathAt);
        int sinceDetectionBarrier = unchecked(
            deathAt - _justiceDeathDetectionBarrierAtRawGameTime);
        return deathAt != 0 && age >= 0 &&
               age <= JusticePolicy.PendingIncidentLifetimeMs &&
               sinceDetectionBarrier >= 0;
    }

    private bool SelectLiveJusticeWitnessForConfirmation(JusticePendingRuntimeIncident pending)
    {
        JusticeEvidence evidence = pending.Incident.Evidence;
        if (evidence == null)
        {
            return false;
        }
        if (evidence.ReportCompleted)
        {
            return true;
        }
        if (evidence.WitnessHandle == 0)
        {
            return true;
        }

        bool correlatedWanted =
            (evidence.Kind & JusticeEvidenceKind.CorrelatedWantedRise) != 0;

        JusticeRuntimeWitness delayedWitness = null;
        for (int index = 0; index < pending.Witnesses.Count; index++)
        {
            JusticeRuntimeWitness witness = pending.Witnesses[index];
            if (witness == null || !Entity.Exists(witness.Ped) ||
                GetJusticeEntityGeneration(witness.Ped) != witness.Generation)
            {
                continue;
            }

            bool witnessAlive = !witness.Ped.IsDead;
            bool delayedReportCompleted =
                _justiceMonotonicTimeMs >= witness.ReportDueAtMs &&
                (witnessAlive || DidJusticeWitnessSurviveUntilReport(witness));

            if (witness.Kind == JusticeEvidenceKind.PoliceWitness || correlatedWanted ||
                delayedReportCompleted)
            {
                evidence.Kind = witness.Kind |
                    (correlatedWanted ? JusticeEvidenceKind.CorrelatedWantedRise : JusticeEvidenceKind.None);
                evidence.WitnessHandle = witness.Ped.Handle;
                evidence.WitnessGeneration = witness.Generation;
                evidence.ReportDueAtMs = correlatedWanted
                    ? _justiceMonotonicTimeMs
                    : witness.ReportDueAtMs;
                evidence.HasPlausibleObserver = true;
                evidence.ReportCompleted = true;
                return true;
            }

            if (witnessAlive && delayedWitness == null)
            {
                delayedWitness = witness;
            }
        }

        if (delayedWitness != null)
        {
            evidence.Kind = delayedWitness.Kind;
            evidence.WitnessHandle = delayedWitness.Ped.Handle;
            evidence.WitnessGeneration = delayedWitness.Generation;
            evidence.ReportDueAtMs = delayedWitness.ReportDueAtMs;
            evidence.HasPlausibleObserver = true;
        }
        return false;
    }

    private bool DidJusticeWitnessSurviveUntilReport(JusticeRuntimeWitness witness)
    {
        if (witness == null || !Entity.Exists(witness.Ped) || !witness.Ped.IsDead)
        {
            return false;
        }

        int deathAt;
        if (!TryCallJusticeIntegerNativeWithCircuit(
            JusticeNativeGetPedTimeOfDeath,
            JusticeCircuitPedTimeOfDeath,
            0,
            out deathAt,
            witness.Ped.Handle))
        {
            return false;
        }
        int rawNow = GetJusticeRawGameTimeSafe();
        int deathAge = unchecked(rawNow - deathAt);
        if (deathAt == 0 || deathAge < 0 || deathAge > JusticePolicy.PendingIncidentLifetimeMs)
        {
            return false;
        }

        long estimatedDeathAtMs = _justiceMonotonicTimeMs - deathAge;
        return estimatedDeathAtMs >= witness.ReportDueAtMs;
    }

    private JusticeRecentVictim RememberJusticeRecentVictim(
        Ped victim,
        int generation,
        bool directPlayerDamage,
        bool vehicleWasWeapon,
        JusticeCircumstances circumstances,
        string causalEventId)
    {
        for (int index = 0; index < _justiceRecentVictims.Count; index++)
        {
            JusticeRecentVictim existing = _justiceRecentVictims[index];
            if (existing.Ped.Handle == victim.Handle && existing.Generation == generation)
            {
                existing.LastPlayerAttackAtMs = _justiceMonotonicTimeMs;
                existing.DirectPlayerDamage |= directPlayerDamage;
                existing.VehicleWasWeapon |= vehicleWasWeapon;
                existing.Circumstances |= circumstances;
                if (!string.IsNullOrWhiteSpace(causalEventId))
                {
                    existing.CausalEventId = causalEventId;
                }
                return existing;
            }
        }

        if (_justiceRecentVictims.Count >= JusticeMaximumRecentVictims)
        {
            _justiceRecentVictims.RemoveAt(0);
        }
        JusticeRecentVictim recent = new JusticeRecentVictim
        {
            Ped = victim,
            Generation = generation,
            CausalEventId = causalEventId ?? string.Empty,
            LastPlayerAttackAtMs = _justiceMonotonicTimeMs,
            DirectPlayerDamage = directPlayerDamage,
            VehicleWasWeapon = vehicleWasWeapon,
            Circumstances = circumstances
        };
        _justiceRecentVictims.Add(recent);
        return recent;
    }

    private JusticeRecentVehicle RememberJusticeRecentVehicle(
        Vehicle vehicle,
        int generation,
        JusticeCircumstances circumstances)
    {
        for (int index = 0; index < _justiceRecentVehicles.Count; index++)
        {
            JusticeRecentVehicle existing = _justiceRecentVehicles[index];
            if (existing.Vehicle.Handle == vehicle.Handle && existing.Generation == generation)
            {
                existing.LastPlayerDamageAtMs = _justiceMonotonicTimeMs;
                existing.Circumstances |= circumstances;
                return existing;
            }
        }

        if (_justiceRecentVehicles.Count >= JusticeMaximumRecentVictims)
        {
            _justiceRecentVehicles.RemoveAt(0);
        }
        JusticeRecentVehicle recent = new JusticeRecentVehicle
        {
            Vehicle = vehicle,
            Generation = generation,
            LastPlayerDamageAtMs = _justiceMonotonicTimeMs,
            Circumstances = circumstances
        };
        _justiceRecentVehicles.Add(recent);
        return recent;
    }

    private void RemovePendingRecklessDischargeForConfirmedViolence(JusticeCharge charge)
    {
        if (charge == null || charge.IsAlliedAction ||
            !IsJusticeDirectVictimViolence(charge.Kind))
        {
            return;
        }

        for (int index = _justicePendingIncidents.Count - 1; index >= 0; index--)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            if (pending != null && pending.Incident != null &&
                pending.Incident.Kind == JusticeCrimeKind.RecklessDischarge &&
                !pending.Incident.IsConfirmed &&
                !string.IsNullOrWhiteSpace(charge.CausalEventId) &&
                string.Equals(
                    pending.Incident.CausalEventId,
                    charge.CausalEventId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    pending.Incident.EpisodeId,
                    charge.EpisodeId,
                    StringComparison.Ordinal))
            {
                _justicePendingIncidents.RemoveAt(index);
            }
        }
    }

    private string GetJusticeActiveDischargeCausalId(bool explicitWeaponDischarge)
    {
        if (!explicitWeaponDischarge ||
            string.IsNullOrWhiteSpace(_justiceActiveDischargeCausalId) ||
            _justiceMonotonicTimeMs > _justiceActiveDischargeExpiresAtMs)
        {
            return string.Empty;
        }

        return _justiceActiveDischargeCausalId;
    }

    private static bool IsJusticeDirectVictimViolence(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.SimpleAssault ||
               kind == JusticeCrimeKind.AggravatedAssault ||
               kind == JusticeCrimeKind.AssaultOfficer ||
               kind == JusticeCrimeKind.Manslaughter ||
               kind == JusticeCrimeKind.MurderCivilian ||
               kind == JusticeCrimeKind.MurderOfficer;
    }

    private bool IsJusticeIncidentAlreadyKnown(string incidentId)
    {
        for (int index = 0; index < _justicePendingIncidents.Count; index++)
        {
            JusticePendingRuntimeIncident pending = _justicePendingIncidents[index];
            if (pending != null && pending.Incident != null &&
                string.Equals(pending.Incident.IncidentId, incidentId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        for (int index = 0; index < _justiceCaseState.ProcessedIncidentIds.Count; index++)
        {
            if (string.Equals(_justiceCaseState.ProcessedIncidentIds[index], incidentId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private string BuildJusticeIncidentId(
        JusticeCrimeKind kind,
        string episode,
        int victimHandle,
        int victimGeneration,
        int allyHandle,
        int allyGeneration = 0)
    {
        string prefix = "incident:" + (episode ?? string.Empty) + ":" + kind.ToString() + ":";
        if (victimHandle != 0 || kind == JusticeCrimeKind.EvadingPolice || kind == JusticeCrimeKind.Escape)
        {
            return prefix + victimHandle.ToString(CultureInfo.InvariantCulture) + ":" +
                   victimGeneration.ToString(CultureInfo.InvariantCulture) + ":" +
                   allyHandle.ToString(CultureInfo.InvariantCulture) + ":" +
                   Math.Max(0, allyGeneration).ToString(CultureInfo.InvariantCulture);
        }

        _justiceIncidentSequence++;
        return prefix + "event:" + _justiceSessionId + ":" +
               _justiceIncidentSequence.ToString(CultureInfo.InvariantCulture);
    }

    private string BuildJusticeDetectionBatchId()
    {
        return "batch:" + _justiceSessionId + ":" +
               _justiceMonotonicTimeMs.ToString(CultureInfo.InvariantCulture);
    }

    private string GetJusticeDetectionEpisodeId()
    {
        if (!string.IsNullOrWhiteSpace(_justiceCaseState.WantedEpisodeId))
        {
            return _justiceCaseState.WantedEpisodeId;
        }
        if (string.IsNullOrWhiteSpace(_justiceDetectionEpisodeId))
        {
            _justiceEpisodeSequence++;
            _justiceDetectionEpisodeId = "wanted:" + _justiceSessionId + ":" +
                                         _justiceEpisodeSequence.ToString(CultureInfo.InvariantCulture);
        }
        return _justiceDetectionEpisodeId;
    }

    private string CurrentJusticeEpisodeId()
    {
        if (_justiceCaseState != null && !string.IsNullOrWhiteSpace(_justiceCaseState.WantedEpisodeId))
        {
            return _justiceCaseState.WantedEpisodeId;
        }
        return GetJusticeDetectionEpisodeId();
    }

    private int GetJusticeEntityGeneration(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return 0;
        }

        int handle = entity.Handle;
        int modelHash = GetJusticeEntityModelHashSafe(entity);
        long memoryAddress = GetJusticeEntityMemoryAddressSafe(entity);
        JusticeTrackedIdentity tracked;
        if (_justiceTrackedIdentities.TryGetValue(handle, out tracked) &&
            CanReuseJusticeTrackedIdentity(tracked, entity, modelHash, memoryAddress))
        {
            tracked.Entity = entity;
            tracked.LastSeenAtMs = _justiceMonotonicTimeMs;
            return tracked.Generation;
        }

        _justiceNextIdentityGeneration++;
        if (_justiceNextIdentityGeneration <= 0)
        {
            _justiceNextIdentityGeneration = 1;
        }
        _justiceTrackedIdentities[handle] = new JusticeTrackedIdentity
        {
            Entity = entity,
            ModelHash = modelHash,
            MemoryAddress = memoryAddress,
            Generation = _justiceNextIdentityGeneration,
            LastSeenAtMs = _justiceMonotonicTimeMs
        };
        PruneJusticeTrackedIdentities();
        return _justiceNextIdentityGeneration;
    }

    private bool CanReuseJusticeTrackedIdentity(
        JusticeTrackedIdentity tracked,
        Entity currentEntity,
        int modelHash,
        long memoryAddress)
    {
        if (tracked == null || tracked.ModelHash != modelHash ||
            !Entity.Exists(tracked.Entity) || !Entity.Exists(currentEntity))
        {
            return false;
        }

        if (tracked.MemoryAddress != 0L || memoryAddress != 0L)
        {
            if (tracked.MemoryAddress == 0L || memoryAddress == 0L ||
                tracked.MemoryAddress != memoryAddress)
            {
                return false;
            }
        }
        else if (!ReferenceEquals(tracked.Entity, currentEntity))
        {
            // Le stub n'a pas d'adresse native : seule la même enveloppe objet
            // peut alors représenter la même identité, jamais le handle seul.
            return false;
        }

        long age = _justiceMonotonicTimeMs - tracked.LastSeenAtMs;
        return age >= 0L && age <= JusticeIdentityLifetimeMs;
    }

    private void PruneJusticeTrackedIdentities()
    {
        while (_justiceTrackedIdentities.Count > JusticeMaximumTrackedIdentities)
        {
            int oldestHandle = 0;
            long oldestAt = long.MaxValue;
            foreach (KeyValuePair<int, JusticeTrackedIdentity> pair in _justiceTrackedIdentities)
            {
                JusticeTrackedIdentity identity = pair.Value;
                if (identity == null || !Entity.Exists(identity.Entity) ||
                    _justiceMonotonicTimeMs - identity.LastSeenAtMs > JusticeIdentityLifetimeMs)
                {
                    oldestHandle = pair.Key;
                    break;
                }
                if (identity.LastSeenAtMs < oldestAt)
                {
                    oldestAt = identity.LastSeenAtMs;
                    oldestHandle = pair.Key;
                }
            }

            if (oldestHandle == 0)
            {
                break;
            }
            _justiceTrackedIdentities.Remove(oldestHandle);
        }
    }

    private static int GetJusticeEntityModelHashSafe(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return 0;
        }

        try
        {
            Ped ped = entity as Ped;
            if (ped != null)
            {
                return ped.Model.Hash;
            }
            return Function.Call<int>((Hash)JusticeNativeGetEntityModel, entity.Handle);
        }
        catch
        {
            return 0;
        }
    }

    private static unsafe long GetJusticeEntityMemoryAddressSafe(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return 0L;
        }

        try
        {
            return (long)(IntPtr)entity.MemoryAddress;
        }
        catch
        {
            return 0L;
        }
    }

    private static string BuildJusticeEntityKey(int handle, int generation)
    {
        return handle.ToString(CultureInfo.InvariantCulture) + ":" +
               generation.ToString(CultureInfo.InvariantCulture);
    }

    private void AdvanceJusticeCleanRecord(bool runtimeEligible)
    {
        if (_justiceLastCleanAdvanceAtMs <= 0L)
        {
            _justiceLastCleanAdvanceAtMs = _justiceMonotonicTimeMs;
            return;
        }

        long elapsed = Math.Max(0L, _justiceMonotonicTimeMs - _justiceLastCleanAdvanceAtMs);
        _justiceLastCleanAdvanceAtMs = _justiceMonotonicTimeMs;
        bool eligible = runtimeEligible && _justiceEnabled && !HasActiveJusticeCase() &&
                        !JusticeIsCustodyActive && _justiceLastWantedLevel == 0;
        if (!eligible || elapsed <= 0L)
        {
            return;
        }

        if (_justiceRecordState == null || _justiceRecordState.RecidivismIndex <= 0)
        {
            _justiceCleanCarryMilliseconds = 0L;
            return;
        }

        _justiceCleanCarryMilliseconds = Math.Min(60000L, _justiceCleanCarryMilliseconds + elapsed);
        if (_justiceCleanCarryMilliseconds < 10000L)
        {
            return;
        }

        int seconds = (int)(_justiceCleanCarryMilliseconds / 1000L);
        _justiceCleanCarryMilliseconds %= 1000L;
        int oldRecidivism = _justiceRecordState.RecidivismIndex;
        int oldCleanSeconds = _justiceRecordState.CleanGameplaySeconds;
        int oldAppliedDecay = _justiceRecordState.AppliedCleanDecay;
        JusticePolicy.AdvanceCleanTime(_justiceRecordState, seconds, true);
        if (_justiceRecordState.RecidivismIndex != oldRecidivism ||
            _justiceRecordState.CleanGameplaySeconds != oldCleanSeconds ||
            _justiceRecordState.AppliedCleanDecay != oldAppliedDecay)
        {
            JusticeMarkStateDirty();
        }

        if (_justiceRecordState.RecidivismIndex < oldRecidivism)
        {
            LogInfo(
                "Justice.Recidive",
                "Indice reduit a " + _justiceRecordState.RecidivismIndex.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private void JusticeMarkStateDirty()
    {
        if (!_justiceInitialized && _justiceCaseState == null)
        {
            return;
        }

        if (!_justiceStateDirty)
        {
            _justiceNextStateSaveAtMs = _justiceMonotonicTimeMs + JusticeStateSaveDebounceMs;
        }
        _justiceStateDirty = true;
    }

    private void PersistJusticeStateIfDue()
    {
        if (!_justiceStateDirty)
        {
            return;
        }

        if (_justiceMonotonicTimeMs < _justiceNextStateSaveAtMs &&
            _justiceMonotonicTimeMs < _justiceNextCheckpointAtMs)
        {
            return;
        }

        QueueJusticeStateCheckpoint();
    }

    private bool JusticeFlushStateSynchronouslyLegacy()
    {
        if (_justiceInitialized &&
            _justiceMonotonicTimeMs < _justiceNextStateFlushAttemptAtMs)
        {
            // Je ne recrée pas un XML temporaire et une exception à chaque frame
            // lorsqu'un état ou le disque refuse momentanément la sauvegarde.
            return false;
        }

        if (_justiceStateFlushFailureOverride != null)
        {
            _justiceStateFlushAttemptSequence++;
            if (_justiceStateFlushAttemptSequence <= 0)
            {
                _justiceStateFlushAttemptSequence = 1;
            }

            bool forceFailure;
            try
            {
                forceFailure = _justiceStateFlushFailureOverride(
                    _justiceStateFlushAttemptSequence);
            }
            catch
            {
                // Je ferme le circuit de test en échec : une instrumentation
                // défaillante ne doit jamais autoriser un effet externe.
                forceFailure = true;
            }

            if (forceFailure)
            {
                _justiceStateDirty = true;
                _justiceNextStateSaveAtMs =
                    _justiceMonotonicTimeMs + JusticeStateCheckpointMs;
                _justiceNextStateFlushAttemptAtMs =
                    _justiceMonotonicTimeMs + JusticeStateFailureRetryMs;
                return false;
            }
        }

        PrepareJusticeActiveProfileForPersistence();
        if (_justiceLegacyProfileReloadPending)
        {
            return false;
        }
        if (_justiceCaseState == null || _justiceRecordState == null)
        {
            return false;
        }
        if (!IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot))
        {
            // Sans protagoniste prouve, je conserve le dernier XML valide au lieu
            // d'attribuer arbitrairement un dossier ou un inventaire.
            return false;
        }
        if (_justiceBackupRepairPending &&
            (_justiceMonotonicTimeMs < _justiceNextBackupRepairAtMs ||
             !TryRepairJusticePrimaryFromLoadedBackup()))
        {
            return false;
        }

        NormalizeJusticePinnedConvictionForCurrentCase();
        SnapshotActiveJusticePlayerProfile();
        JusticePlayerProfileState activeProfile =
            _justicePlayerProfiles[_justiceActivePlayerProfileSlot];

        string path = Path.Combine(GetSaveDirectory(), JusticeStateFileName);
        string tempPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineHandling = NewLineHandling.Entitize
            };

            using (XmlWriter writer = XmlWriter.Create(tempPath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("JusticeState");
                writer.WriteAttributeString("version", JusticeStateVersion.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("enabled", _justiceEnabled ? "true" : "false");
                writer.WriteAttributeString(
                    "policeIntegrationMode",
                    ((int)_justicePoliceIntegrationMode).ToString(
                        CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "activePlayerSlot",
                    _justiceActivePlayerProfileSlot.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "nextIdentityGeneration",
                    Math.Max(0, _justiceNextIdentityGeneration).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "pendingDeathCapture",
                    _justicePursuitDeathObservedDuringSuspension ? "true" : "false");
                writer.WriteAttributeString(
                    "pendingDeathCapturePlayerSlot",
                    _justiceSuspendedPursuitDeathPlayerSlot.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "pendingDeathCapturePlayerModel",
                    _justiceSuspendedPursuitDeathPlayerModelHash.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "pendingAmnestyWantedClear",
                    _justiceAmnestyPending ? "true" : "false");
                writer.WriteAttributeString(
                    "pendingLegalReleaseFinalization",
                    _justiceLegalReleaseFinalizationPending ? "true" : "false");
                writer.WriteAttributeString(
                    "pendingLegalReleaseSite",
                    ((int)_justiceLegalReleaseFinalizationSite).ToString(
                        CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "pendingLegalReleaseSelectedWeapon",
                    (_justiceLegalReleaseFinalizationPending
                        ? _justiceLegalReleaseSelectedWeaponHash
                        : 0).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "lastCanonicalPlayerSlot",
                    _justiceLastCanonicalPlayerSlot.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "lastCanonicalPlayerModel",
                    _justiceLastCanonicalPlayerModelHash.ToString(CultureInfo.InvariantCulture));
                WriteJusticeCaseXml(writer, activeProfile.CaseState);
                WriteJusticeRecordXml(writer, activeProfile.RecordState);
                WriteJusticeCustodyXmlFragment(writer, activeProfile.CustodyXml);
                WriteJusticePlayerProfilesXml(writer);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            FileInfo tempFile = new FileInfo(tempPath);
            if (!tempFile.Exists || tempFile.Length <= 0L ||
                tempFile.Length > JusticeStateMaximumFileBytes)
            {
                throw new InvalidDataException(
                    "Etat Justice temporaire vide ou supérieur à la limite de 16 Mio.");
            }
            if (!IsJusticeTemporaryStateSemanticallyValid(tempPath))
            {
                throw new InvalidDataException(
                    "Etat Justice temporaire incohérent; le primaire valide est conservé.");
            }

            ReplaceFileAtomically(tempPath, path);
            tempPath = null;
            _justiceStateDirty = false;
            _justiceNextStateSaveAtMs = 0L;
            _justiceNextCheckpointAtMs = _justiceMonotonicTimeMs + JusticeStateCheckpointMs;
            _justiceNextStateFlushAttemptAtMs = 0L;
            _justiceNextStateFailureLogAtMs = 0L;
            if (_justiceSuppressedStateFailureLogs > 0)
            {
                LogInfo(
                    "Justice.Sauvegarde",
                    _justiceSuppressedStateFailureLogs.ToString(CultureInfo.InvariantCulture) +
                    " échec(s) répétitif(s) masqué(s) avant reprise de la sauvegarde.");
                _justiceSuppressedStateFailureLogs = 0;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!_justiceInitialized ||
                _justiceMonotonicTimeMs >= _justiceNextStateFailureLogAtMs)
            {
                LogException("Justice.Sauvegarde", ex);
                _justiceNextStateFailureLogAtMs =
                    _justiceMonotonicTimeMs + JusticeStateFailureLogCooldownMs;
            }
            else if (_justiceSuppressedStateFailureLogs < int.MaxValue)
            {
                _justiceSuppressedStateFailureLogs++;
            }
            _justiceStateDirty = true;
            _justiceNextStateSaveAtMs = _justiceMonotonicTimeMs + JusticeStateCheckpointMs;
            _justiceNextStateFlushAttemptAtMs =
                _justiceMonotonicTimeMs + JusticeStateFailureRetryMs;
            return false;
        }
        finally
        {
            DeleteFileIfExistsSafe(tempPath);
        }
    }

    private static bool IsJusticeTemporaryStateSemanticallyValid(string path)
    {
        try
        {
            byte[] serialized = File.ReadAllBytes(path);
            JusticePersistenceSnapshot v2Snapshot;
            string v2Error;
            if (new JusticeXmlPersistenceCodec().TryDeserialize(
                    serialized,
                    out v2Snapshot,
                    out v2Error))
            {
                string semanticError;
                return v2Snapshot != null &&
                       TryValidateJusticePersistenceSnapshotSemantics(
                           v2Snapshot,
                           out semanticError);
            }

            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                document.Load(reader);
            }

            XmlElement root = document.DocumentElement;
            XmlNodeList caseNodes = root == null ? null : root.SelectNodes("Case");
            XmlNodeList recordNodes = root == null ? null : root.SelectNodes("Record");
            if (root == null || caseNodes == null || caseNodes.Count != 1 ||
                recordNodes == null || recordNodes.Count != 1)
            {
                return false;
            }
            XmlElement caseElement = caseNodes[0] as XmlElement;
            XmlElement recordElement = recordNodes[0] as XmlElement;
            JusticeCaseState caseState = ReadJusticeCaseXml(caseElement);
            JusticeRecordState recordState = ReadJusticeRecordXml(recordElement);
            bool rootEnabled;
            int policeIntegrationMode;
            int nextIdentityGeneration;
            bool pendingDeathCapture;
            int pendingDeathCaptureSlot;
            int pendingDeathCaptureModel;
            bool pendingAmnestyWantedClear;
            int lastCanonicalPlayerSlot;
            int lastCanonicalPlayerModel;
            if (!string.Equals(root.Name, "JusticeState", StringComparison.Ordinal) ||
                ReadJusticeInt(root, "version", -1) != JusticeStateVersion ||
                !TryReadJusticeIntStrict(
                    root,
                    "nextIdentityGeneration",
                    0,
                    0,
                    int.MaxValue - 1,
                    out nextIdentityGeneration) ||
                !TryReadJusticeBoolStrict(
                    root,
                    "enabled",
                    caseState == null ? false : caseState.Enabled,
                    out rootEnabled) ||
                !TryReadJusticeIntStrict(
                    root,
                    "policeIntegrationMode",
                    (int)JusticePoliceIntegrationMode.FreeroamBestEffort,
                    (int)JusticePoliceIntegrationMode.Disabled,
                    (int)JusticePoliceIntegrationMode.Force,
                    out policeIntegrationMode) ||
                !TryReadJusticeBoolStrict(
                    root,
                    "pendingDeathCapture",
                    false,
                    out pendingDeathCapture) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingDeathCapturePlayerSlot",
                    -1,
                    -1,
                    2,
                    out pendingDeathCaptureSlot) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingDeathCapturePlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out pendingDeathCaptureModel) ||
                !TryReadJusticeBoolStrict(
                    root,
                    "pendingAmnestyWantedClear",
                    false,
                    out pendingAmnestyWantedClear) ||
                !TryReadJusticeIntStrict(
                    root,
                    "lastCanonicalPlayerSlot",
                    -1,
                    -1,
                    2,
                    out lastCanonicalPlayerSlot) ||
                !TryReadJusticeIntStrict(
                    root,
                    "lastCanonicalPlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out lastCanonicalPlayerModel) ||
                caseState == null || recordState == null ||
                rootEnabled != caseState.Enabled ||
                (!rootEnabled && IsLoadedJusticeCaseActive(caseState)) ||
                !IsJusticeCaseRecordLinkValid(caseState, recordState))
            {
                return false;
            }

            bool custodyPhase = IsJusticeCustodyPhase(caseState.Phase);
            if (pendingDeathCapture)
            {
                // Je rends durable le front de mort même pendant les quelques frames où GTA
                // ne fournit encore ni slot canonique ni modèle. Les mutations restent bloquées
                // ensuite tant que l'identité du protagoniste n'est pas prouvée.
                if (!rootEnabled || !IsLoadedJusticeCaseActive(caseState) ||
                    (!custodyPhase && caseState.Phase != JusticePhase.Wanted &&
                     caseState.Phase != JusticePhase.Surrendering &&
                     caseState.Phase != JusticePhase.Fugitive))
                {
                    return false;
                }
            }
            else if (pendingDeathCaptureSlot != -1 || pendingDeathCaptureModel != 0)
            {
                return false;
            }
            if (lastCanonicalPlayerSlot < 0 && lastCanonicalPlayerModel != 0)
            {
                return false;
            }

            if (!IsJusticeCustodyXmlSemanticallyValid(root, caseState, recordState))
            {
                return false;
            }

            JusticePlayerProfileState[] profiles;
            int persistedActiveSlot;
            bool hasProfiles;
            if (!TryReadJusticePlayerProfilesXml(
                    root,
                    out profiles,
                    out persistedActiveSlot,
                    out hasProfiles))
            {
                return false;
            }

            // Un fichier v1 historique reste accepté. Dès que PlayerProfiles est
            // présent, son profil actif doit être l'exact miroir rétrocompatible
            // des trois noeuds racine afin d'éviter tout état partagé ambigu.
            return !hasProfiles || AreJusticeProfileMirrorNodesEqual(
                root,
                profiles,
                persistedActiveSlot);
        }
        catch
        {
            return false;
        }
    }

    private void WriteJusticeCaseXml(XmlWriter writer)
    {
        WriteJusticeCaseXml(writer, _justiceCaseState);
    }

    private static void WriteJusticeCaseXml(XmlWriter writer, JusticeCaseState state)
    {
        if (writer == null || state == null)
        {
            return;
        }
        JusticePolicy.EnforceActiveChargeLimit(state);
        writer.WriteStartElement("Case");
        writer.WriteAttributeString("enabled", state.Enabled ? "true" : "false");
        writer.WriteAttributeString("activeScore", state.ActiveScore.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fineDue", state.FineDue.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "voluntaryFinePaid",
            Math.Max(0L, state.VoluntaryFinePaid).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "fineInDispute",
            Math.Max(0L, state.FineInDispute).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("sentenceSeconds", state.SentenceSeconds.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("hasWarrant", state.HasWarrant ? "true" : "false");
        writer.WriteAttributeString(
            "escapeWantedMinimumPending",
            state.EscapeWantedMinimumPending ? "true" : "false");
        writer.WriteAttributeString(
            "escapeWantedMinimumAttempted",
            state.EscapeWantedMinimumAttempted ? "true" : "false");
        writer.WriteAttributeString("phase", state.Phase.ToString());
        writer.WriteAttributeString("wantedEpisodeId", state.WantedEpisodeId ?? string.Empty);
        writer.WriteAttributeString("custodyEpisodeId", state.CustodyEpisodeId ?? string.Empty);
        writer.WriteAttributeString("lastCrimeKind", state.LastCrimeKind.HasValue
            ? state.LastCrimeKind.Value.ToString()
            : string.Empty);
        writer.WriteAttributeString("lastCrimeLabel", state.LastCrimeLabel ?? string.Empty);
        // Je conserve les attributs historiques pour lire les premiers états v1,
        // mais les listes d'épisodes ci-dessous sont désormais autoritaires.
        writer.WriteAttributeString("fleeingCharged", state.FleeingCharged ? "true" : "false");
        writer.WriteAttributeString("escapeCharged", state.EscapeCharged ? "true" : "false");

        writer.WriteStartElement("Charges");
        for (int index = 0; index < state.Charges.Count; index++)
        {
            JusticeCharge charge = state.Charges[index];
            if (charge == null)
            {
                continue;
            }
            writer.WriteStartElement("Charge");
            writer.WriteAttributeString("id", charge.ChargeId ?? string.Empty);
            writer.WriteAttributeString("incidentId", charge.IncidentId ?? string.Empty);
            writer.WriteAttributeString("episodeId", charge.EpisodeId ?? string.Empty);
            writer.WriteAttributeString("detectionBatchId", charge.DetectionBatchId ?? string.Empty);
            writer.WriteAttributeString("causalEventId", charge.CausalEventId ?? string.Empty);
            writer.WriteAttributeString("kind", charge.Kind.ToString());
            writer.WriteAttributeString("displayName", charge.DisplayName ?? string.Empty);
            writer.WriteAttributeString("victimHandle", charge.VictimHandle.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("victimGeneration", charge.VictimGeneration.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("points", charge.Points.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("fine", charge.Fine.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sentenceSeconds", charge.SentenceSeconds.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "confirmedAtMs",
                Math.Max(0L, charge.ConfirmedAtMs).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("allied", charge.IsAlliedAction ? "true" : "false");
            writer.WriteAttributeString("adjudicated", charge.IsAdjudicated ? "true" : "false");
            writer.WriteAttributeString("aggregate", charge.IsAggregate ? "true" : "false");
            writer.WriteAttributeString(
                "aggregatedChargeCount",
                (charge.IsAggregate ? Math.Max(1, charge.AggregatedChargeCount) : 0)
                    .ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("circumstances", ((int)charge.Circumstances).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("additionalVictims", Math.Max(0, charge.AdditionalVictimCount).ToString(CultureInfo.InvariantCulture));
            charge.ImportLegacyAlliedContributorHandles();
            writer.WriteStartElement("AlliedContributors");
            for (int contributorIndex = 0;
                 contributorIndex < charge.AlliedContributors.Count && contributorIndex < JusticeMaximumWitnessesPerEvent;
                 contributorIndex++)
            {
                JusticeEntityIdentity identity = charge.AlliedContributors[contributorIndex];
                int handle = identity.Handle;
                if (handle <= 0)
                {
                    continue;
                }
                writer.WriteStartElement("Ally");
                writer.WriteAttributeString("handle", handle.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "generation",
                    Math.Max(0, identity.Generation).ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        WriteJusticeIdAttributeList(writer, "FleeingEpisodes", "Episode", "id", state.FleeingChargedEpisodeIds);
        WriteJusticeIdAttributeList(writer, "EscapeEpisodes", "Episode", "id", state.EscapeChargedEpisodeIds);
        WriteJusticeStringList(writer, "ProcessedIncidents", "Incident", state.ProcessedIncidentIds);
        WriteJusticeStringList(writer, "CompletedOperations", "Operation", state.CompletedOperationIds);
        writer.WriteEndElement();
    }

    private void WriteJusticeRecordXml(XmlWriter writer)
    {
        WriteJusticeRecordXml(writer, _justiceRecordState);
    }

    private static void WriteJusticeRecordXml(XmlWriter writer, JusticeRecordState state)
    {
        if (writer == null || state == null)
        {
            return;
        }
        writer.WriteStartElement("Record");
        writer.WriteAttributeString("recidivism", state.RecidivismIndex.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cleanGameplaySeconds", state.CleanGameplaySeconds.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("appliedCleanDecay", state.AppliedCleanDecay.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("pinnedConvictionId", state.PinnedConvictionId ?? string.Empty);
        writer.WriteStartElement("Convictions");
        for (int index = 0; index < state.Convictions.Count; index++)
        {
            JusticeConviction conviction = state.Convictions[index];
            if (conviction == null)
            {
                continue;
            }
            writer.WriteStartElement("Conviction");
            writer.WriteAttributeString("id", conviction.ConvictionId ?? string.Empty);
            writer.WriteAttributeString("judgedAtUtc", conviction.JudgedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("severity", conviction.Severity.ToString());
            writer.WriteAttributeString("score", conviction.Score.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("fine", conviction.Fine.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sentenceSeconds", conviction.SentenceSeconds.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("ChargeSummaries");
            for (int chargeIndex = 0; chargeIndex < conviction.Charges.Count; chargeIndex++)
            {
                JusticeConvictionChargeSummary summary = conviction.Charges[chargeIndex];
                if (summary == null)
                {
                    continue;
                }
                writer.WriteStartElement("Charge");
                writer.WriteAttributeString("kind", summary.Kind.ToString());
                writer.WriteAttributeString("label", summary.DisplayName ?? string.Empty);
                writer.WriteAttributeString("points", Math.Max(0, summary.Points).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("fine", Math.Max(0L, summary.Fine).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("sentence", Math.Max(0, summary.SentenceSeconds).ToString(CultureInfo.InvariantCulture));
                if (summary.CircumstancesWerePersisted)
                {
                    writer.WriteAttributeString(
                        "circumstances",
                        ((int)summary.Circumstances).ToString(CultureInfo.InvariantCulture));
                }
                writer.WriteAttributeString("aggregate", summary.IsAggregate ? "true" : "false");
                writer.WriteAttributeString(
                    "aggregatedChargeCount",
                    (summary.IsAggregate ? Math.Max(1, summary.AggregatedChargeCount) : 0)
                        .ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        WriteJusticeIdAttributeList(
            writer,
            "AppliedConvictions",
            "ConvictionId",
            "id",
            state.AppliedConvictionIds);
        writer.WriteEndElement();
    }

    private static void WriteJusticeIdAttributeList(
        XmlWriter writer,
        string containerName,
        string itemName,
        string attributeName,
        List<string> values)
    {
        writer.WriteStartElement(containerName);
        if (values != null)
        {
            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                writer.WriteStartElement(itemName);
                writer.WriteAttributeString(attributeName, value.Trim());
                writer.WriteEndElement();
            }
        }
        writer.WriteEndElement();
    }

    private static void WriteJusticeStringList(XmlWriter writer, string containerName, string itemName, List<string> values)
    {
        writer.WriteStartElement(containerName);
        if (values != null)
        {
            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    writer.WriteElementString(itemName, value);
                }
            }
        }
        writer.WriteEndElement();
    }

    private bool TryLoadJusticeState(bool backupOnly)
    {
        List<string> directories = GetSaveSearchDirectories();
        for (int index = 0; index < directories.Count; index++)
        {
            string primary = Path.Combine(directories[index], JusticeStateFileName);
            bool primaryExists = File.Exists(primary);
            string backup = primary + ".bak";
            bool backupExists = File.Exists(backup);
            if (!backupOnly && primaryExists && TryReadJusticeStateFile(primary))
            {
                return true;
            }
            if (!backupOnly && primaryExists && backupExists &&
                TryRecoverJusticeInactiveProfiles(primary, backup))
            {
                return true;
            }

            if (backupExists && TryReadJusticeStateFile(backup))
            {
                LogWarning("Justice.Chargement", "Backup Justice restauré depuis " + backup + ".");
                _justiceBackupRepairPending = true;
                string canonicalPrimary = Path.Combine(GetSaveDirectory(), JusticeStateFileName);
                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(backup));
                string canonicalDirectory = Path.GetDirectoryName(Path.GetFullPath(canonicalPrimary));
                _justiceBackupRepairPrimaryPath = string.Equals(
                    sourceDirectory,
                    canonicalDirectory,
                    StringComparison.OrdinalIgnoreCase)
                    ? primary
                    : canonicalPrimary;
                _justiceBackupRepairSourcePath = backup;
                if (!TryRepairJusticePrimaryFromLoadedBackup())
                {
                    LogWarning(
                        "Justice.Chargement",
                        "Réparation du primaire différée; les mutations Justice restent suspendues.");
                }
                return true;
            }

            if (!ShouldContinueJusticeStateSearch(index, primaryExists, backupExists))
            {
                // Je considère le premier dossier comme canonique. Dès qu'il
                // contient un état Justice, même corrompu, je n'adopte jamais une
                // copie legacy sans génération connue qui pourrait ressusciter une
                // ancienne peine ou une transaction déjà terminée.
                LogWarning(
                    "Justice.Chargement",
                    "Etat Justice canonique invalide; fallback legacy refusé pour éviter une restauration obsolète.");
                return false;
            }
        }
        return false;
    }

    private bool TryRecoverJusticeInactiveProfiles(string primary, string backup)
    {
        try
        {
            FileInfo primaryInfo = new FileInfo(primary);
            FileInfo backupInfo = new FileInfo(backup);
            if (!primaryInfo.Exists || !backupInfo.Exists ||
                primaryInfo.Length <= 0L || backupInfo.Length <= 0L ||
                primaryInfo.Length > JusticeStateMaximumFileBytes ||
                backupInfo.Length > JusticeStateMaximumFileBytes)
            {
                return false;
            }

            string walProofError;
            if (!TryProveJusticeInactiveProfileRecoveryWalClosed(
                    primary,
                    out walProofError))
            {
                LogWarning(
                    "Justice.Chargement",
                    "Isolation d'un profil inactif refusée : " + walProofError);
                return false;
            }

            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            JusticePersistenceSnapshot recovered;
            string recoveryError;
            if (!codec.TryRecoverInactiveProfiles(
                    File.ReadAllBytes(primary),
                    File.ReadAllBytes(backup),
                    out recovered,
                    out recoveryError) ||
                recovered == null)
            {
                return false;
            }

            string semanticError;
            if (!TryValidateJusticePersistenceSnapshotSemantics(
                    recovered,
                    out semanticError))
            {
                LogWarning(
                    "Justice.Chargement",
                    "Profil inactif isolé mais snapshot fusionné incohérent : " +
                    semanticError);
                return false;
            }

            byte[] repaired = codec.Serialize(recovered);
            if (!TryProveJusticeInactiveProfileRecoveryWalClosed(
                    primary,
                    out walProofError))
            {
                LogWarning(
                    "Justice.Chargement",
                    "Isolation d'un profil inactif annulée avant publication : " +
                    walProofError);
                return false;
            }

            string persistenceError;
            if (!TryWriteAndVerifyJusticeRecoveredPrimary(
                    primary,
                    repaired,
                    new JusticeAtomicFileStore(),
                    out persistenceError))
            {
                LogWarning(
                    "Justice.Chargement",
                    "Snapshot fusionné non confirmé après relecture : " +
                    persistenceError);
                return false;
            }
            if (!TryReadJusticeStateFile(primary))
            {
                return false;
            }

            LogWarning(
                "Justice.Chargement",
                "Profil Justice inactif corrompu isolé et restauré depuis le backup; " +
                "le profil actif du primaire a été conservé.");
            return true;
        }
        catch (Exception exception)
        {
            LogWarning(
                "Justice.Chargement",
                "Isolation d'un profil inactif impossible : " +
                exception.GetType().Name + ".");
            return false;
        }
    }

    internal static bool TryProveJusticeInactiveProfileRecoveryWalClosed(
        string primaryPath,
        out string error)
    {
        error = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(primaryPath))
            {
                throw new ArgumentException("Le chemin primaire Justice est absent.", "primaryPath");
            }

            string primary = Path.GetFullPath(primaryPath);
            string directory = Path.GetDirectoryName(primary);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException("Le dossier du primaire Justice est introuvable.");
            }

            string walPath = Path.Combine(directory, JusticeWalFileName);
            JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(walPath);
            if (recovery.Status != JusticeWalRecoveryStatus.Clean)
            {
                throw new InvalidDataException(
                    "le WAL n'est pas intégralement prouvé (" +
                    recovery.Status.ToString() + ").");
            }

            Dictionary<string, JusticeWalRecord> latest =
                new Dictionary<string, JusticeWalRecord>(StringComparer.Ordinal);
            for (int index = 0; index < recovery.Records.Count; index++)
            {
                JusticeWalRecord record = recovery.Records[index];
                latest[record.TransactionId] = record;
            }
            foreach (JusticeWalRecord record in latest.Values)
            {
                if (!record.IsTerminal)
                {
                    throw new InvalidDataException(
                        "une transaction WAL reste ouverte (" +
                        record.TransactionId + ").");
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    internal static bool TryWriteAndVerifyJusticeRecoveredPrimary(
        string primaryPath,
        byte[] repaired,
        IJusticeAtomicFileStore fileStore,
        out string error)
    {
        error = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(primaryPath))
            {
                throw new ArgumentException("Le chemin primaire Justice est absent.", "primaryPath");
            }
            if (repaired == null || repaired.Length == 0)
            {
                throw new InvalidDataException("Le snapshot Justice réparé est vide.");
            }
            if (fileStore == null)
            {
                throw new ArgumentNullException("fileStore");
            }

            fileStore.WriteAtomically(
                primaryPath,
                null,
                repaired,
                JusticeNoOpPersistenceFaultInjector.Instance);
            byte[] persisted = fileStore.ReadAllBytes(primaryPath);
            bool exactBytes = AreJusticePersistenceBytesEqual(repaired, persisted);
            bool exactHash = persisted != null && string.Equals(
                JusticeXmlPersistenceCodec.ComputeSha256Hex(repaired),
                JusticeXmlPersistenceCodec.ComputeSha256Hex(persisted),
                StringComparison.OrdinalIgnoreCase);
            if (!exactBytes || !exactHash)
            {
                throw new InvalidDataException(
                    "les octets ou le SHA-256 relus diffèrent du snapshot fusionné validé.");
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    internal static bool ShouldContinueJusticeStateSearch(
        int directoryIndex,
        bool primaryExists,
        bool backupExists)
    {
        // Le premier dossier est toujours GetSaveDirectory(). Les emplacements
        // historiques ne servent donc qu'à une migration lorsque ce dossier ne
        // possède encore aucun état Justice.
        return directoryIndex > 0 || (!primaryExists && !backupExists);
    }

    private bool TryRepairJusticePrimaryFromLoadedBackup()
    {
        if (!_justiceBackupRepairPending)
        {
            return true;
        }

        string primary = _justiceBackupRepairPrimaryPath ?? string.Empty;
        string backup = _justiceBackupRepairSourcePath ?? string.Empty;
        string tempPath = null;
        try
        {
            if (primary.Length == 0 || backup.Length == 0 || !File.Exists(backup))
            {
                ScheduleJusticeBackupRepairRetry("source de backup absente");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(primary));
            tempPath = primary + "." + Guid.NewGuid().ToString("N") + ".repair.tmp";
            File.Copy(backup, tempPath, true);
            if (!IsJusticeTemporaryStateSemanticallyValid(tempPath) ||
                !string.Equals(
                    ComputeJusticeFileSha256Hex(tempPath),
                    ComputeJusticeFileSha256Hex(backup),
                    StringComparison.OrdinalIgnoreCase))
            {
                ScheduleJusticeBackupRepairRetry("copie temporaire invalide");
                return false;
            }
            if (File.Exists(primary))
            {
                // Je remplace le primaire sans générer de nouveau backup : la
                // copie validée ne reçoit jamais l'ancien primaire corrompu.
                File.Replace(tempPath, primary, null, true);
            }
            else
            {
                File.Move(tempPath, primary);
            }
            tempPath = null;

            if (!File.Exists(primary) ||
                !IsJusticeTemporaryStateSemanticallyValid(primary) ||
                !string.Equals(
                    ComputeJusticeFileSha256Hex(primary),
                    ComputeJusticeFileSha256Hex(backup),
                    StringComparison.OrdinalIgnoreCase))
            {
                ScheduleJusticeBackupRepairRetry("copie primaire invalide après relecture");
                return false;
            }

            _justiceBackupRepairPending = false;
            _justiceBackupRepairFailureLogged = false;
            _justiceNextBackupRepairAtMs = 0L;
            _justiceBackupRepairPrimaryPath = string.Empty;
            _justiceBackupRepairSourcePath = string.Empty;
            LogInfo("Justice.Chargement", "Primaire Justice réparé depuis le backup validé.");
            return true;
        }
        catch (Exception ex)
        {
            ScheduleJusticeBackupRepairRetry(ex.GetType().Name);
            return false;
        }
        finally
        {
            DeleteFileIfExistsSafe(tempPath);
        }
    }

    private static string ComputeJusticeFileSha256Hex(string path)
    {
        using (FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }

    private void ScheduleJusticeBackupRepairRetry(string reason)
    {
        _justiceNextBackupRepairAtMs = _justiceMonotonicTimeMs + 5000L;
        if (_justiceBackupRepairFailureLogged)
        {
            return;
        }

        _justiceBackupRepairFailureLogged = true;
        LogWarning(
            "Justice.Chargement",
            "Réparation du primaire différée (" + (reason ?? "inconnue") + ").");
    }

    private bool TryReadJusticeStateFile(string path)
    {
        JusticeCaseState oldCase = _justiceCaseState;
        JusticeRecordState oldRecord = _justiceRecordState;
        bool oldEnabled = _justiceEnabled;
        JusticePoliceIntegrationMode oldPoliceIntegrationMode =
            _justicePoliceIntegrationMode;
        int oldNextIdentityGeneration = _justiceNextIdentityGeneration;
        bool oldPendingDeathCapture = _justicePursuitDeathObservedDuringSuspension;
        int oldPendingDeathCaptureSlot = _justiceSuspendedPursuitDeathPlayerSlot;
        int oldPendingDeathCaptureModel = _justiceSuspendedPursuitDeathPlayerModelHash;
        bool oldPendingAmnesty = _justiceAmnestyPending;
        bool oldPendingLegalRelease = _justiceLegalReleaseFinalizationPending;
        JusticeCustodySite oldPendingLegalReleaseSite =
            _justiceLegalReleaseFinalizationSite;
        int oldPendingLegalReleaseSelectedWeapon =
            _justiceLegalReleaseSelectedWeaponHash;
        int oldLastCanonicalPlayerSlot = _justiceLastCanonicalPlayerSlot;
        int oldLastCanonicalPlayerModel = _justiceLastCanonicalPlayerModelHash;
        JusticePlayerProfileState[] oldProfiles = _justicePlayerProfiles;
        int oldActiveProfileSlot = _justiceActivePlayerProfileSlot;
        bool oldProfileSelectionPending = _justiceProfileSelectionPending;
        bool oldLegacyProfileReloadPending = _justiceLegacyProfileReloadPending;
        string oldCustodyXml = CaptureCurrentJusticeCustodyXmlSafe();
        JusticePersistenceSnapshot loadedV2Snapshot = null;

        try
        {
            FileInfo stateFile = new FileInfo(path);
            if (!stateFile.Exists || stateFile.Length <= 0L || stateFile.Length > JusticeStateMaximumFileBytes)
            {
                return false;
            }

            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                document.Load(reader);
            }

            XmlElement root = document.DocumentElement;
            string v2Error;
            if (root != null && root.HasAttribute("schemaMajor"))
            {
                if (!TryNormalizeJusticeV2DocumentForLegacyReader(
                        document,
                        out root,
                        out loadedV2Snapshot,
                        out v2Error))
                {
                    return false;
                }
            }
            if (root == null || !string.Equals(root.Name, "JusticeState", StringComparison.Ordinal) ||
                ReadJusticeInt(root, "version", -1) != JusticeStateVersion)
            {
                return false;
            }
            int loadedNextIdentityGeneration;
            if (!TryReadJusticeIntStrict(
                root,
                "nextIdentityGeneration",
                0,
                0,
                int.MaxValue - 1,
                out loadedNextIdentityGeneration))
            {
                return false;
            }

            XmlNodeList caseNodes = root.SelectNodes("Case");
            XmlNodeList recordNodes = root.SelectNodes("Record");
            if (caseNodes == null || caseNodes.Count != 1 ||
                recordNodes == null || recordNodes.Count != 1)
            {
                return false;
            }
            XmlElement caseElement = caseNodes[0] as XmlElement;
            XmlElement recordElement = recordNodes[0] as XmlElement;
            if (caseElement == null || recordElement == null)
            {
                return false;
            }

            JusticeCaseState loadedCase = ReadJusticeCaseXml(caseElement);
            JusticeRecordState loadedRecord = ReadJusticeRecordXml(recordElement);
            if (loadedCase == null || loadedRecord == null ||
                !IsJusticeCaseRecordLinkValid(loadedCase, loadedRecord))
            {
                return false;
            }

            bool loadedEnabled;
            if (!TryReadJusticeBoolStrict(
                root,
                "enabled",
                loadedCase.Enabled,
                out loadedEnabled))
            {
                return false;
            }
            if (loadedEnabled != loadedCase.Enabled ||
                (!loadedEnabled && IsLoadedJusticeCaseActive(loadedCase)))
            {
                return false;
            }
            int loadedPoliceIntegrationMode;
            if (!TryReadJusticeIntStrict(
                    root,
                    "policeIntegrationMode",
                    (int)JusticePoliceIntegrationMode.FreeroamBestEffort,
                    (int)JusticePoliceIntegrationMode.Disabled,
                    (int)JusticePoliceIntegrationMode.Force,
                    out loadedPoliceIntegrationMode))
            {
                return false;
            }

            bool loadedPendingDeathCapture;
            int loadedPendingDeathCaptureSlot;
            int loadedPendingDeathCaptureModel;
            bool loadedPendingAmnesty;
            bool loadedPendingLegalRelease;
            int loadedPendingLegalReleaseSite;
            int loadedPendingLegalReleaseSelectedWeapon;
            int loadedLastCanonicalPlayerSlot;
            int loadedLastCanonicalPlayerModel;
            if (!TryReadJusticeBoolStrict(
                    root,
                    "pendingDeathCapture",
                    false,
                    out loadedPendingDeathCapture) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingDeathCapturePlayerSlot",
                    -1,
                    -1,
                    2,
                    out loadedPendingDeathCaptureSlot) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingDeathCapturePlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out loadedPendingDeathCaptureModel) ||
                !TryReadJusticeBoolStrict(
                    root,
                    "pendingAmnestyWantedClear",
                    false,
                    out loadedPendingAmnesty) ||
                !TryReadJusticeBoolStrict(
                    root,
                    "pendingLegalReleaseFinalization",
                    false,
                    out loadedPendingLegalRelease) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingLegalReleaseSite",
                    0,
                    0,
                    2,
                    out loadedPendingLegalReleaseSite) ||
                !TryReadJusticeIntStrict(
                    root,
                    "pendingLegalReleaseSelectedWeapon",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out loadedPendingLegalReleaseSelectedWeapon) ||
                !TryReadJusticeIntStrict(
                    root,
                    "lastCanonicalPlayerSlot",
                    -1,
                    -1,
                    2,
                    out loadedLastCanonicalPlayerSlot) ||
                !TryReadJusticeIntStrict(
                    root,
                    "lastCanonicalPlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out loadedLastCanonicalPlayerModel))
            {
                return false;
            }
            if (loadedLastCanonicalPlayerSlot < 0 && loadedLastCanonicalPlayerModel != 0)
            {
                return false;
            }
            bool loadedCustodyPhase = loadedCase.Phase == JusticePhase.Captured ||
                loadedCase.Phase == JusticePhase.Transporting ||
                loadedCase.Phase == JusticePhase.Incarcerated ||
                loadedCase.Phase == JusticePhase.Escaping;
            if (loadedPendingDeathCapture)
            {
                // Je recharge aussi ce latch sans identité : le prochain héros canonique ne sera
                // jamais adopté implicitement et convertira l'affaire inconnue en mandat.
                if (!loadedEnabled || !IsLoadedJusticeCaseActive(loadedCase) ||
                    (!loadedCustodyPhase &&
                     loadedCase.Phase != JusticePhase.Wanted &&
                     loadedCase.Phase != JusticePhase.Surrendering &&
                     loadedCase.Phase != JusticePhase.Fugitive))
                {
                    return false;
                }
            }
            else if (loadedPendingDeathCaptureSlot != -1 || loadedPendingDeathCaptureModel != 0)
            {
                return false;
            }
            if (!IsJusticePendingLegalReleaseValid(
                    loadedCase,
                    loadedPendingLegalRelease,
                    loadedPendingLegalReleaseSite,
                    loadedPendingLegalReleaseSelectedWeapon))
            {
                return false;
            }
            if (!IsJusticeCustodyXmlSemanticallyValid(root, loadedCase, loadedRecord))
            {
                return false;
            }

            JusticePlayerProfileState[] loadedProfiles;
            int persistedActiveSlot;
            bool hasProfiles;
            if (!TryReadJusticePlayerProfilesXml(
                    root,
                    out loadedProfiles,
                    out persistedActiveSlot,
                    out hasProfiles) ||
                (hasProfiles && !AreJusticeProfileMirrorNodesEqual(
                    root,
                    loadedProfiles,
                    persistedActiveSlot)))
            {
                return false;
            }
            if (loadedV2Snapshot != null &&
                !TryHydrateJusticeV2CustodySnapshots(loadedProfiles))
            {
                return false;
            }

            int currentCanonicalSlot = GetJusticeCanonicalPlayerSlotSafe();
            if (oldProfiles == null && _justiceCanonicalPlayerSlotOverride == null)
            {
                // Un objet headless non initialise n'a pas de protagoniste runtime
                // prouve, meme si le stub GTA expose un ped factice par defaut.
                currentCanonicalSlot = -1;
            }
            if (!IsJusticeCanonicalProfileSlot(currentCanonicalSlot) &&
                IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) &&
                !_justiceProfileSelectionPending &&
                (!hasProfiles ||
                 (oldProfiles != null &&
                  _justiceLastCanonicalPlayerSlot == _justiceActivePlayerProfileSlot)))
            {
                // Le slot actif n'est renseigné qu'après une preuve canonique. Ce
                // fallback couvre aussi les migrations headless sans ped GTA.
                currentCanonicalSlot = _justiceActivePlayerProfileSlot;
            }
            int selectedProfileSlot;
            bool selectionPending = !IsJusticeCanonicalProfileSlot(currentCanonicalSlot);
            if (hasProfiles)
            {
                // Le slot GTA courant est autoritaire. Sans slot disponible, je
                // garde seulement le miroir persiste en sommeil jusqu'a preuve.
                selectedProfileSlot = IsJusticeCanonicalProfileSlot(currentCanonicalSlot)
                    ? currentCanonicalSlot
                    : persistedActiveSlot;
            }
            else
            {
                selectedProfileSlot = ResolveLegacyJusticeProfileSlot(
                    root,
                    currentCanonicalSlot,
                    loadedLastCanonicalPlayerSlot,
                    loadedPendingDeathCaptureSlot);
                if (!IsJusticeCanonicalProfileSlot(selectedProfileSlot))
                {
                    // Je ne migre jamais un ancien dossier vers le premier heros
                    // qui apparait sans indice canonique persiste.
                    _justiceProfileSelectionPending = true;
                    _justiceLegacyProfileReloadPending = true;
                    return false;
                }

                loadedProfiles = new JusticePlayerProfileState[JusticePlayerProfileCount];
                for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
                {
                    loadedProfiles[slot] = new JusticePlayerProfileState(slot)
                    {
                        CustodyXml = CreateCanonicalEmptyJusticeCustodyXml()
                    };
                }
                XmlElement legacyCustody = root.SelectSingleNode("Custody") as XmlElement;
                loadedProfiles[selectedProfileSlot] = new JusticePlayerProfileState(selectedProfileSlot)
                {
                    CaseState = loadedCase,
                    RecordState = loadedRecord,
                    CustodyXml = legacyCustody == null
                        ? CreateCanonicalEmptyJusticeCustodyXml()
                        : legacyCustody.OuterXml,
                    PendingDeathCapture = loadedPendingDeathCapture,
                    PendingDeathCapturePlayerSlot = loadedPendingDeathCaptureSlot,
                    PendingDeathCapturePlayerModel = loadedPendingDeathCaptureModel,
                    PendingAmnestyWantedClear = loadedPendingAmnesty,
                    PendingLegalReleaseFinalization = loadedPendingLegalRelease,
                    PendingLegalReleaseSite = loadedPendingLegalReleaseSite,
                    PendingLegalReleaseSelectedWeapon =
                        loadedPendingLegalReleaseSelectedWeapon,
                    LastCanonicalPlayerModel = loadedLastCanonicalPlayerModel
                };
            }

            _justicePlayerProfiles = loadedProfiles;
            _justiceActivePlayerProfileSlot = selectedProfileSlot;
            _justiceProfileSelectionPending = selectionPending;
            _justiceLegacyProfileReloadPending = false;
            _justiceNextIdentityGeneration = loadedNextIdentityGeneration;
            _justicePoliceIntegrationMode =
                (JusticePoliceIntegrationMode)loadedPoliceIntegrationMode;
            if (!ActivateJusticePlayerProfile(selectedProfileSlot))
            {
                ResetJusticeCustodyPersistentFields(false);
                _justiceCaseState = oldCase;
                _justiceRecordState = oldRecord;
                _justiceEnabled = oldEnabled;
                _justicePoliceIntegrationMode = oldPoliceIntegrationMode;
                _justiceNextIdentityGeneration = oldNextIdentityGeneration;
                _justicePursuitDeathObservedDuringSuspension = oldPendingDeathCapture;
                _justiceSuspendedPursuitDeathPlayerSlot = oldPendingDeathCaptureSlot;
                _justiceSuspendedPursuitDeathPlayerModelHash = oldPendingDeathCaptureModel;
                _justiceAmnestyPending = oldPendingAmnesty;
                _justiceLegalReleaseFinalizationPending = oldPendingLegalRelease;
                _justiceLegalReleaseFinalizationSite = oldPendingLegalReleaseSite;
                _justiceLegalReleaseSelectedWeaponHash =
                    oldPendingLegalReleaseSelectedWeapon;
                _justiceLastCanonicalPlayerSlot = oldLastCanonicalPlayerSlot;
                _justiceLastCanonicalPlayerModelHash = oldLastCanonicalPlayerModel;
                _justicePlayerProfiles = oldProfiles;
                _justiceActivePlayerProfileSlot = oldActiveProfileSlot;
                _justiceProfileSelectionPending = oldProfileSelectionPending;
                _justiceLegacyProfileReloadPending = oldLegacyProfileReloadPending;
                ReadJusticeCustodyXmlFragment(oldCustodyXml);
                return false;
            }
            MergeJusticeInactiveProfilePoliceSuppressionRecovery();
            _justiceDamageFrontPrimingPending = _justiceEnabled;
            if (loadedV2Snapshot != null)
            {
                _justicePersistenceRevision = loadedV2Snapshot.Revision;
                _justiceLoadedSchemaMajor = JusticeXmlPersistenceCodec.SchemaMajor;
                _justiceV1MigrationSourcePath = string.Empty;
                LoadJusticeProfilePersistenceGenerations(loadedV2Snapshot);
            }
            else
            {
                _justicePersistenceRevision = 0L;
                _justiceLoadedSchemaMajor = JusticeStateVersion;
                _justiceV1MigrationSourcePath = Path.GetFullPath(path);
                _justiceProfilePersistenceGenerations =
                    new long[JusticePlayerProfileCount];
            }
            if (!hasProfiles)
            {
                // La lecture v1 reste immédiate, puis le prochain palier durable
                // l'actualise vers les trois profils sans toucher aux scènes XML.
                JusticeMarkStateDirty();
            }
            return true;
        }
        catch (Exception ex)
        {
            ResetJusticeCustodyPersistentFields(false);
            _justiceCaseState = oldCase;
            _justiceRecordState = oldRecord;
            _justiceEnabled = oldEnabled;
            _justicePoliceIntegrationMode = oldPoliceIntegrationMode;
            _justiceNextIdentityGeneration = oldNextIdentityGeneration;
            _justicePursuitDeathObservedDuringSuspension = oldPendingDeathCapture;
            _justiceSuspendedPursuitDeathPlayerSlot = oldPendingDeathCaptureSlot;
            _justiceSuspendedPursuitDeathPlayerModelHash = oldPendingDeathCaptureModel;
            _justiceAmnestyPending = oldPendingAmnesty;
            _justiceLegalReleaseFinalizationPending = oldPendingLegalRelease;
            _justiceLegalReleaseFinalizationSite = oldPendingLegalReleaseSite;
            _justiceLegalReleaseSelectedWeaponHash =
                oldPendingLegalReleaseSelectedWeapon;
            _justiceLastCanonicalPlayerSlot = oldLastCanonicalPlayerSlot;
            _justiceLastCanonicalPlayerModelHash = oldLastCanonicalPlayerModel;
            _justicePlayerProfiles = oldProfiles;
            _justiceActivePlayerProfileSlot = oldActiveProfileSlot;
            _justiceProfileSelectionPending = oldProfileSelectionPending;
            _justiceLegacyProfileReloadPending = oldLegacyProfileReloadPending;
            ReadJusticeCustodyXmlFragment(oldCustodyXml);
            LogWarning("Justice.Chargement", "Etat ignoré (" + Path.GetFileName(path) + ") : " + ex.Message);
            return false;
        }
    }

    private static JusticeCaseState ReadJusticeCaseXml(XmlElement element)
    {
        if (element == null)
        {
            return new JusticeCaseState();
        }

        bool enabled;
        bool hasWarrant;
        bool escapeWantedMinimumPending;
        bool escapeWantedMinimumAttempted;
        int activeScore;
        long fineDue;
        long voluntaryFinePaid;
        long fineInDispute;
        int sentenceSeconds;
        if (!TryReadJusticeBoolStrict(element, "enabled", false, out enabled) ||
            !TryReadJusticeIntStrict(
                element,
                "activeScore",
                0,
                0,
                JusticePolicy.MaxActiveScore,
                out activeScore) ||
            !TryReadJusticeLongStrict(
                element,
                "fineDue",
                0L,
                0L,
                JusticePolicy.MaxActiveFine,
                out fineDue) ||
            !TryReadJusticeLongStrict(
                element,
                "voluntaryFinePaid",
                0L,
                0L,
                JusticePolicy.MaxActiveFine,
                out voluntaryFinePaid) ||
            !TryReadJusticeLongStrict(
                element,
                "fineInDispute",
                0L,
                0L,
                JusticePolicy.MaxActiveFine,
                out fineInDispute) ||
            !TryReadJusticeIntStrict(
                element,
                "sentenceSeconds",
                0,
                0,
                JusticePolicy.MaxActiveSentenceSeconds,
                out sentenceSeconds) ||
            !TryReadJusticeBoolStrict(element, "hasWarrant", false, out hasWarrant) ||
            !TryReadJusticeBoolStrict(
                element,
                "escapeWantedMinimumPending",
                false,
                out escapeWantedMinimumPending) ||
            !TryReadJusticeBoolStrict(
                element,
                "escapeWantedMinimumAttempted",
                false,
                out escapeWantedMinimumAttempted))
        {
            return null;
        }

        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = enabled,
            ActiveScore = activeScore,
            FineDue = fineDue,
            VoluntaryFinePaid = voluntaryFinePaid,
            FineInDispute = fineInDispute,
            SentenceSeconds = sentenceSeconds,
            HasWarrant = hasWarrant,
            EscapeWantedMinimumPending = escapeWantedMinimumPending,
            EscapeWantedMinimumAttempted = escapeWantedMinimumAttempted,
            Phase = ReadJusticeEnum(element, "phase", JusticePhase.AtLarge),
            WantedEpisodeId = ReadJusticeString(element, "wantedEpisodeId"),
            CustodyEpisodeId = ReadJusticeString(element, "custodyEpisodeId"),
            LastCrimeLabel = ReadJusticeString(element, "lastCrimeLabel")
        };

        string lastKindText = ReadJusticeString(element, "lastCrimeKind");
        JusticeCrimeKind lastKind = JusticeCrimeKind.ReportedViolentAct;
        if (lastKindText.Length > 0 && !TryParseDefinedJusticeEnum(lastKindText, out lastKind))
        {
            return null;
        }
        if (lastKindText.Length > 0)
        {
            state.LastCrimeKind = lastKind;
        }

        XmlNodeList chargeNodes = element.SelectNodes("Charges/Charge");
        if (chargeNodes != null)
        {
            if (chargeNodes.Count > JusticePolicy.MaxActiveCharges)
            {
                return null;
            }
            HashSet<string> chargeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> incidentIds = new HashSet<string>(StringComparer.Ordinal);
            string fallbackEpisodeId = string.IsNullOrWhiteSpace(state.WantedEpisodeId)
                ? state.CustodyEpisodeId
                : state.WantedEpisodeId;
            for (int index = 0; index < chargeNodes.Count; index++)
            {
                XmlElement chargeElement = chargeNodes[index] as XmlElement;
                JusticeCrimeKind kind;
                if (chargeElement == null ||
                    !TryParseDefinedJusticeEnum(ReadJusticeString(chargeElement, "kind"), out kind))
                {
                    return null;
                }
                int victimHandle;
                int victimGeneration;
                int points;
                long fine;
                int chargeSentenceSeconds;
                long confirmedAtMs;
                bool isAllied;
                bool isAdjudicated;
                bool isAggregate;
                int aggregateCount;
                int rawCircumstances;
                int additionalVictims;
                if (!TryReadJusticeIntStrict(
                        chargeElement,
                        "victimHandle",
                        0,
                        int.MinValue,
                        int.MaxValue,
                        out victimHandle) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "victimGeneration",
                        0,
                        0,
                        int.MaxValue - 1,
                        out victimGeneration) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "points",
                        0,
                        0,
                        JusticePolicy.MaxActiveScore,
                        out points) ||
                    !TryReadJusticeLongStrict(
                        chargeElement,
                        "fine",
                        0L,
                        0L,
                        JusticePolicy.MaxActiveFine,
                        out fine) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "sentenceSeconds",
                        0,
                        0,
                        JusticePolicy.MaxActiveSentenceSeconds,
                        out chargeSentenceSeconds) ||
                    !TryReadJusticeLongStrict(
                        chargeElement,
                        "confirmedAtMs",
                        0L,
                        0L,
                        long.MaxValue,
                        out confirmedAtMs) ||
                    !TryReadJusticeBoolStrict(chargeElement, "allied", false, out isAllied) ||
                    !TryReadJusticeBoolStrict(chargeElement, "adjudicated", false, out isAdjudicated) ||
                    !TryReadJusticeBoolStrict(chargeElement, "aggregate", false, out isAggregate) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "aggregatedChargeCount",
                        0,
                        0,
                        int.MaxValue,
                        out aggregateCount) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "circumstances",
                        0,
                        0,
                        JusticeKnownCircumstanceMask,
                        out rawCircumstances) ||
                    !TryReadJusticeIntStrict(
                        chargeElement,
                        "additionalVictims",
                        0,
                        0,
                        JusticeMaximumWitnessesPerEvent,
                        out additionalVictims) ||
                    (rawCircumstances & ~JusticeKnownCircumstanceMask) != 0)
                {
                    return null;
                }
                JusticeCharge charge = new JusticeCharge
                {
                    ChargeId = ReadJusticeString(chargeElement, "id"),
                    IncidentId = ReadJusticeString(chargeElement, "incidentId"),
                    EpisodeId = ReadJusticeString(chargeElement, "episodeId"),
                    DetectionBatchId = ReadJusticeString(chargeElement, "detectionBatchId"),
                    CausalEventId = ReadJusticeString(chargeElement, "causalEventId"),
                    Kind = kind,
                    DisplayName = ReadJusticeString(chargeElement, "displayName"),
                    VictimHandle = victimHandle,
                    VictimGeneration = victimGeneration,
                    Points = points,
                    Fine = fine,
                    SentenceSeconds = chargeSentenceSeconds,
                    ConfirmedAtMs = confirmedAtMs,
                    IsAlliedAction = isAllied,
                    IsAdjudicated = isAdjudicated,
                    IsAggregate = isAggregate,
                    AggregatedChargeCount = aggregateCount,
                    Circumstances = (JusticeCircumstances)rawCircumstances,
                    AdditionalVictimCount = additionalVictims
                };
                XmlNodeList contributorNodes = chargeElement.SelectNodes("AlliedContributors/Ally");
                if (contributorNodes != null)
                {
                    for (int contributorIndex = 0;
                         contributorIndex < contributorNodes.Count && contributorIndex < JusticeMaximumWitnessesPerEvent;
                         contributorIndex++)
                    {
                        XmlElement contributor = contributorNodes[contributorIndex] as XmlElement;
                        int handle;
                        int generation;
                        if (!TryReadJusticeIntStrict(
                                contributor,
                                "handle",
                                0,
                                1,
                                int.MaxValue,
                                out handle) ||
                            !TryReadJusticeIntStrict(
                                contributor,
                                "generation",
                                0,
                                0,
                                int.MaxValue - 1,
                                out generation))
                        {
                            return null;
                        }
                        if (handle > 0)
                        {
                            // Je lis aussi les premiers XML v1 sans génération :
                            // la valeur zéro conserve leur identité historique.
                            charge.AddAlliedContributor(handle, generation);
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(charge.ChargeId) &&
                    string.IsNullOrWhiteSpace(charge.IncidentId))
                {
                    // Les premiers v1 ne persistaient pas toujours ces deux
                    // identifiants. Je leur donne une clé déterministe par rang
                    // avant d'appliquer les validations strictes modernes.
                    charge.IncidentId = "legacy:v1:charge:" +
                        index.ToString(CultureInfo.InvariantCulture) + ":" + kind;
                }
                if (!JusticePolicy.TryNormalizePersistedChargeIdentity(
                        charge,
                        fallbackEpisodeId) ||
                    !chargeIds.Add(charge.ChargeId) ||
                    !incidentIds.Add(charge.IncidentId))
                {
                    return null;
                }
                if (charge.IsAggregate &&
                    (charge.VictimHandle != 0 || charge.VictimGeneration != 0 ||
                     charge.IsAlliedAction || charge.Circumstances != JusticeCircumstances.None ||
                     charge.AdditionalVictimCount != 0 ||
                     charge.AlliedContributors.Count != 0 ||
                     charge.DetectionBatchId.Length != 0 || charge.CausalEventId.Length != 0))
                {
                    return null;
                }
                state.Charges.Add(charge);
            }
        }

        ReadJusticeIdAttributeList(
            element,
            "FleeingEpisodes/Episode",
            "id",
            state.FleeingChargedEpisodeIds,
            JusticePolicy.MaxChargedEpisodeIds);
        ReadJusticeIdAttributeList(
            element,
            "EscapeEpisodes/Episode",
            "id",
            state.EscapeChargedEpisodeIds,
            JusticePolicy.MaxChargedEpisodeIds);

        // Je migre les tout premiers fichiers v1 qui ne mémorisaient qu'un booléen.
        if (state.FleeingChargedEpisodeIds.Count == 0 && ReadJusticeBool(element, "fleeingCharged", false))
        {
            AddJusticeEpisodeFallback(state.FleeingChargedEpisodeIds, state.WantedEpisodeId);
        }
        if (state.EscapeChargedEpisodeIds.Count == 0 && ReadJusticeBool(element, "escapeCharged", false))
        {
            AddJusticeEpisodeFallback(
                state.EscapeChargedEpisodeIds,
                string.IsNullOrWhiteSpace(state.CustodyEpisodeId) ? state.WantedEpisodeId : state.CustodyEpisodeId);
        }

        ReadJusticeStringList(element, "ProcessedIncidents/Incident", state.ProcessedIncidentIds, JusticePolicy.MaxRememberedIncidents);
        ReadJusticeStringList(element, "CompletedOperations/Operation", state.CompletedOperationIds, int.MaxValue);
        HashSet<string> persistedOperationIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < state.CompletedOperationIds.Count; index++)
        {
            if (!IsValidPersistedJusticeOperationId(state.CompletedOperationIds[index]) ||
                !persistedOperationIds.Add(state.CompletedOperationIds[index]))
            {
                return null;
            }
        }
        int wantedFloorOperations = 0;
        int committedFineOperations = 0;
        for (int index = state.CompletedOperationIds.Count - 1; index >= 0; index--)
        {
            string operationId = state.CompletedOperationIds[index];
            JusticeOperationKind operationKind;
            string operationEpisode;
            if (!TryParsePersistedJusticeOperationId(
                    operationId,
                    out operationKind,
                    out operationEpisode))
            {
                return null;
            }

            if (operationKind == JusticeOperationKind.ApplyWantedFloor)
            {
                wantedFloorOperations++;
                if (wantedFloorOperations > JusticePolicy.MaxRememberedOperations)
                {
                    state.CompletedOperationIds.RemoveAt(index);
                }
                continue;
            }

            string custodyEpisode = state.CustodyEpisodeId ?? string.Empty;
            bool belongsToCurrentCustody = custodyEpisode.Length > 0 &&
                (string.Equals(operationEpisode, custodyEpisode, StringComparison.Ordinal) ||
                 operationEpisode.StartsWith(custodyEpisode + ":fine:", StringComparison.Ordinal));
            if (!belongsToCurrentCustody)
            {
                // Je migre les opérations closes laissées par les premiers XML
                // v1. Leur dossier ou condamnation reste porté par les charges
                // et le Record, tandis que la liste active demeure bornée.
                state.CompletedOperationIds.RemoveAt(index);
                continue;
            }

            if (operationKind == JusticeOperationKind.DiscardInventory &&
                state.Phase != JusticePhase.Escaping)
            {
                return null;
            }
            if (operationKind == JusticeOperationKind.RegisterEscape)
            {
                return null;
            }
            if (operationKind == JusticeOperationKind.ApplyFine)
            {
                bool impossibleCapturedFineCommit = fineDue > 0L &&
                    string.Equals(
                        operationEpisode,
                        state.CustodyEpisodeId,
                        StringComparison.Ordinal) &&
                    (state.Phase == JusticePhase.Captured ||
                     state.Phase == JusticePhase.Transporting);
                if (impossibleCapturedFineCommit ||
                    !IsJusticeFineOperationEpisodeValid(state, operationEpisode))
                {
                    return null;
                }
                committedFineOperations++;
            }
        }

        long computedScore = 0L;
        long computedFine = 0L;
        long computedSentence = 0L;
        for (int index = 0; index < state.Charges.Count; index++)
        {
            JusticeCharge charge = state.Charges[index];
            if (charge != null)
            {
                computedScore = JusticePolicy.SaturatingAdd(
                    computedScore,
                    Math.Max(0, charge.Points),
                    JusticePolicy.MaxActiveScore);
                computedFine = JusticePolicy.SaturatingAdd(
                    computedFine,
                    Math.Max(0L, charge.Fine),
                    JusticePolicy.MaxActiveFine);
                computedSentence = JusticePolicy.SaturatingAdd(
                    computedSentence,
                    Math.Max(0, charge.SentenceSeconds),
                    JusticePolicy.MaxActiveSentenceSeconds);
            }
        }
        long maximumPersistedSentence = JusticePolicy.SaturatingAdd(
            computedSentence,
            Math.Min(
                JusticePolicy.MaxActiveSentenceSeconds,
                (long)committedFineOperations * 5L * 60L),
            JusticePolicy.MaxActiveSentenceSeconds);
        long pendingFine = JusticePolicy.CalculatePendingFine(state);
        int pendingSentence = JusticePolicy.CalculatePendingSentence(state);
        long settledFine = JusticePolicy.SaturatingAdd(
            voluntaryFinePaid,
            fineInDispute,
            JusticePolicy.MaxActiveFine);
        long maximumFineDueAfterVoluntaryPayments = Math.Max(
            0L,
            computedFine - Math.Min(computedFine, settledFine));
        long minimumPendingFineAfterVoluntaryPayments = Math.Max(
            0L,
            pendingFine - Math.Min(pendingFine, settledFine));
        if (!JusticePolicy.IsFineLedgerValid(state) ||
            settledFine > computedFine ||
            activeScore != (int)computedScore ||
            fineDue > maximumFineDueAfterVoluntaryPayments ||
            sentenceSeconds > maximumPersistedSentence ||
            fineDue < minimumPendingFineAfterVoluntaryPayments ||
            sentenceSeconds < pendingSentence)
        {
            // Le score est un agrégat dérivable. Le rejeter empêche un XML
            // édité/corrompu d'augmenter un wanted ou d'effacer la dette d'une
            // charge qui n'a pas encore été jugée.
            return null;
        }

        bool hasDossier = state.Charges.Count > 0 || state.ActiveScore > 0 ||
                          state.FineDue > 0L || state.FineInDispute > 0L ||
                          state.SentenceSeconds > 0;
        bool custodyPhase = state.Phase == JusticePhase.Captured ||
                            state.Phase == JusticePhase.Transporting ||
                            state.Phase == JusticePhase.Incarcerated ||
                            state.Phase == JusticePhase.Escaping;
        if (state.Charges.Count == 0 && hasDossier)
        {
            return null;
        }
        if (!hasDossier)
        {
            if (state.Phase != JusticePhase.AtLarge || state.HasWarrant ||
                !string.IsNullOrWhiteSpace(state.WantedEpisodeId) ||
                !string.IsNullOrWhiteSpace(state.CustodyEpisodeId))
            {
                return null;
            }
        }
        else if (string.IsNullOrWhiteSpace(state.WantedEpisodeId) ||
                 (custodyPhase && string.IsNullOrWhiteSpace(state.CustodyEpisodeId)) ||
                 (!custodyPhase && !string.IsNullOrWhiteSpace(state.CustodyEpisodeId)))
        {
            return null;
        }
        if (state.Phase == JusticePhase.Fugitive && !state.HasWarrant)
        {
            return null;
        }
        if ((state.EscapeWantedMinimumPending ||
             state.EscapeWantedMinimumAttempted) &&
            (state.Phase != JusticePhase.Fugitive || !state.HasWarrant))
        {
            return null;
        }
        if (state.EscapeWantedMinimumAttempted &&
            !state.EscapeWantedMinimumPending)
        {
            return null;
        }
        if (custodyPhase && state.HasWarrant)
        {
            return null;
        }
        return state;
    }

    private static bool IsJusticeFineOperationEpisodeValid(
        JusticeCaseState state,
        string operationEpisode)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.CustodyEpisodeId))
        {
            return false;
        }

        string custodyEpisode = state.CustodyEpisodeId.Trim();
        if (string.Equals(operationEpisode, custodyEpisode, StringComparison.Ordinal))
        {
            return true;
        }

        string prefix = custodyEpisode + ":fine:release:";
        if (!operationEpisode.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string incidentId = operationEpisode.Substring(prefix.Length);
        for (int index = 0; index < state.Charges.Count; index++)
        {
            JusticeCharge charge = state.Charges[index];
            if (charge != null && string.Equals(
                charge.IncidentId,
                incidentId,
                StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsLoadedJusticeCaseActive(JusticeCaseState state)
    {
        return state != null &&
            (state.Charges.Count > 0 || state.ActiveScore > 0 || state.FineDue > 0L ||
             state.SentenceSeconds > 0 || state.HasWarrant ||
             state.Phase != JusticePhase.AtLarge ||
             !string.IsNullOrWhiteSpace(state.WantedEpisodeId) ||
             !string.IsNullOrWhiteSpace(state.CustodyEpisodeId));
    }

    private static bool IsJusticeCaseRecordLinkValid(
        JusticeCaseState caseState,
        JusticeRecordState recordState)
    {
        if (caseState == null || recordState == null)
        {
            return false;
        }

        bool custodyPhase = caseState.Phase == JusticePhase.Captured ||
                            caseState.Phase == JusticePhase.Transporting ||
                            caseState.Phase == JusticePhase.Incarcerated ||
                            caseState.Phase == JusticePhase.Escaping;
        if (!custodyPhase)
        {
            // Un v1 réparé hors ligne peut encore porter l'ancien lien. Il ne
            // donne aucun droit en jeu libre et sera retiré au prochain flush.
            return true;
        }

        string custodyEpisode = caseState.CustodyEpisodeId ?? string.Empty;
        string convictionOperation = JusticePolicy.CreateOperationId(
            JusticeOperationKind.ApplyConviction,
            custodyEpisode);
        string convictionId = "conviction:" + custodyEpisode;
        bool currentConvictionExists = recordState.AppliedConvictionIds.Contains(convictionId);
        bool convictionOperationCommitted =
            caseState.CompletedOperationIds.Contains(convictionOperation);
        if (currentConvictionExists != convictionOperationCommitted)
        {
            return false;
        }
        if (currentConvictionExists)
        {
            if (string.IsNullOrWhiteSpace(recordState.PinnedConvictionId))
            {
                // Je migre les premiers XML v1 : l'opération et l'identifiant
                // appliqué prouvent sans ambiguïté la condamnation à épingler.
                recordState.PinnedConvictionId = convictionId;
            }
            else if (!string.Equals(
                recordState.PinnedConvictionId,
                convictionId,
                StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(recordState.PinnedConvictionId))
        {
            return false;
        }

        bool captureStillOwesInitialFine =
            (caseState.Phase == JusticePhase.Captured ||
             caseState.Phase == JusticePhase.Transporting) &&
            convictionOperationCommitted &&
            !caseState.CompletedOperationIds.Contains(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.ApplyFine,
                    custodyEpisode));
        if (captureStillOwesInitialFine)
        {
            JusticeConviction currentConviction = null;
            int visibleMatches = 0;
            for (int index = 0; index < recordState.Convictions.Count; index++)
            {
                JusticeConviction candidate = recordState.Convictions[index];
                if (candidate != null && string.Equals(
                    candidate.ConvictionId,
                    convictionId,
                    StringComparison.Ordinal))
                {
                    currentConviction = candidate;
                    visibleMatches++;
                }
            }

            long remainingConvictionFine = currentConviction == null
                ? long.MaxValue
                : JusticePolicy.CalculateRemainingConvictionFine(
                    currentConviction.Fine,
                    JusticePolicy.SaturatingAdd(
                        caseState.VoluntaryFinePaid,
                        caseState.FineInDispute,
                        JusticePolicy.MaxActiveFine));
            if (visibleMatches != 1 || currentConviction == null ||
                caseState.FineDue < remainingConvictionFine ||
                caseState.SentenceSeconds < currentConviction.SentenceSeconds)
            {
                // Avant le débit initial, la condamnation courante prouve le
                // minimum encore dû après les paiements volontaires déjà
                // précommittés. Je refuse tout XML qui élude ce reliquat.
                return false;
            }
        }

        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge != null && !charge.IsAdjudicated)
            {
                return false;
            }
        }
        return true;
    }

    private static JusticeRecordState ReadJusticeRecordXml(XmlElement element)
    {
        JusticeRecordState state = new JusticeRecordState();
        if (element == null)
        {
            return state;
        }

        int recidivism;
        int cleanGameplaySeconds;
        int appliedCleanDecay;
        if (!TryReadJusticeIntStrict(element, "recidivism", 0, 0, 100, out recidivism) ||
            !TryReadJusticeIntStrict(
                element,
                "cleanGameplaySeconds",
                0,
                0,
                int.MaxValue,
                out cleanGameplaySeconds) ||
            !TryReadJusticeIntStrict(
                element,
                "appliedCleanDecay",
                0,
                0,
                int.MaxValue,
                out appliedCleanDecay))
        {
            return null;
        }
        state.RecidivismIndex = recidivism;
        state.CleanGameplaySeconds = cleanGameplaySeconds;
        state.AppliedCleanDecay = appliedCleanDecay;
        state.PinnedConvictionId = ReadJusticeString(element, "pinnedConvictionId");
        if (state.PinnedConvictionId.Length > 0 &&
            !IsCanonicalJusticeConvictionId(state.PinnedConvictionId))
        {
            return null;
        }
        if (state.AppliedCleanDecay > JusticePolicy.CalculateCleanDecay(state.CleanGameplaySeconds))
        {
            return null;
        }
        XmlNodeList nodes = element.SelectNodes("Convictions/Conviction");
        if (nodes == null)
        {
            return state;
        }

        int start = Math.Max(0, nodes.Count - JusticePolicy.MaxConvictions);
        HashSet<string> convictionIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = start; index < nodes.Count; index++)
        {
            XmlElement convictionElement = nodes[index] as XmlElement;
            if (convictionElement == null)
            {
                continue;
            }

            DateTime judgedAt;
            if (!DateTime.TryParse(
                    ReadJusticeString(convictionElement, "judgedAtUtc"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out judgedAt))
            {
                return null;
            }
            JusticeSeverity severity;
            int convictionScore;
            long convictionFine;
            int convictionSentence;
            if (!TryParseDefinedJusticeEnum(
                    ReadJusticeString(convictionElement, "severity"),
                    out severity) ||
                !TryReadJusticeIntStrict(
                    convictionElement,
                    "score",
                    0,
                    0,
                    JusticePolicy.MaxActiveScore,
                    out convictionScore) ||
                !TryReadJusticeLongStrict(
                    convictionElement,
                    "fine",
                    0L,
                    0L,
                    JusticePolicy.MaxActiveFine,
                    out convictionFine) ||
                !TryReadJusticeIntStrict(
                    convictionElement,
                    "sentenceSeconds",
                    0,
                    0,
                    JusticePolicy.MaxActiveSentenceSeconds,
                    out convictionSentence))
            {
                return null;
            }
            string convictionId = ReadJusticeString(convictionElement, "id");
            if (!IsCanonicalJusticeConvictionId(convictionId) ||
                !convictionIds.Add(convictionId))
            {
                return null;
            }
            state.Convictions.Add(new JusticeConviction
            {
                ConvictionId = convictionId,
                JudgedAtUtc = judgedAt,
                Severity = severity,
                Score = convictionScore,
                Fine = convictionFine,
                SentenceSeconds = convictionSentence
            });

            JusticeConviction loadedConviction = state.Convictions[state.Convictions.Count - 1];
            XmlNodeList summaryNodes = convictionElement.SelectNodes("ChargeSummaries/Charge");
            if (summaryNodes == null || summaryNodes.Count == 0 ||
                summaryNodes.Count > JusticePolicy.MaxActiveCharges)
            {
                return null;
            }
            long summaryScoreTotal = 0L;
            long summaryFineTotal = 0L;
            long summarySentenceTotal = 0L;
            if (summaryNodes != null)
            {
                for (int summaryIndex = 0;
                     summaryIndex < summaryNodes.Count && summaryIndex < JusticePolicy.MaxActiveCharges;
                     summaryIndex++)
                {
                    XmlElement summaryElement = summaryNodes[summaryIndex] as XmlElement;
                    JusticeCrimeKind summaryKind;
                    if (summaryElement == null ||
                        !TryParseDefinedJusticeEnum(
                            ReadJusticeString(summaryElement, "kind"),
                            out summaryKind))
                    {
                        return null;
                    }
                    int summaryPoints;
                    long summaryFine;
                    int summarySentence;
                    int summaryCircumstances = 0;
                    bool summaryAggregate;
                    int summaryAggregateCount;
                    if (!TryReadJusticeIntStrict(
                            summaryElement,
                            "points",
                            0,
                            0,
                            JusticePolicy.MaxActiveScore,
                            out summaryPoints) ||
                        !TryReadJusticeLongStrict(
                            summaryElement,
                            "fine",
                            0L,
                            0L,
                            JusticePolicy.MaxActiveFine,
                            out summaryFine) ||
                        !TryReadJusticeIntStrict(
                            summaryElement,
                            "sentence",
                            0,
                            0,
                            JusticePolicy.MaxActiveSentenceSeconds,
                            out summarySentence) ||
                        !TryReadJusticeBoolStrict(
                            summaryElement,
                            "aggregate",
                            false,
                            out summaryAggregate) ||
                        !TryReadJusticeIntStrict(
                            summaryElement,
                            "aggregatedChargeCount",
                            0,
                            0,
                            int.MaxValue,
                            out summaryAggregateCount))
                    {
                        return null;
                    }
                    if (summaryElement.HasAttribute("circumstances") &&
                        (!TryReadJusticeIntStrict(
                            summaryElement,
                            "circumstances",
                            0,
                            0,
                            JusticeKnownCircumstanceMask,
                            out summaryCircumstances) ||
                         (summaryCircumstances & ~JusticeKnownCircumstanceMask) != 0))
                    {
                        return null;
                    }
                    bool circumstancesWerePersisted = summaryElement.HasAttribute("circumstances");
                    loadedConviction.Charges.Add(new JusticeConvictionChargeSummary
                    {
                        Kind = summaryKind,
                        DisplayName = ReadJusticeString(summaryElement, "label"),
                        Points = summaryPoints,
                        Fine = summaryFine,
                        SentenceSeconds = summarySentence,
                        Circumstances = (JusticeCircumstances)summaryCircumstances,
                        CircumstancesWerePersisted = circumstancesWerePersisted,
                        IsAggregate = summaryAggregate,
                        AggregatedChargeCount = summaryAggregateCount
                    });
                    if ((summaryAggregate && summaryAggregateCount <= 0) ||
                        (!summaryAggregate && summaryAggregateCount != 0))
                    {
                        return null;
                    }
                    summaryScoreTotal = JusticePolicy.SaturatingAdd(
                        summaryScoreTotal,
                        summaryPoints,
                        JusticePolicy.MaxActiveScore);
                    summaryFineTotal = JusticePolicy.SaturatingAdd(
                        summaryFineTotal,
                        summaryFine,
                        JusticePolicy.MaxActiveFine);
                    summarySentenceTotal = JusticePolicy.SaturatingAdd(
                        summarySentenceTotal,
                        summarySentence,
                        JusticePolicy.MaxActiveSentenceSeconds);
                }
            }
            if (loadedConviction.Severity != JusticePolicy.GetSeverity(loadedConviction.Score) ||
                loadedConviction.Severity == JusticeSeverity.None ||
                loadedConviction.Score != (int)summaryScoreTotal ||
                loadedConviction.Fine != summaryFineTotal ||
                loadedConviction.SentenceSeconds != (int)summarySentenceTotal)
            {
                return null;
            }
        }
        XmlNodeList appliedNodes = element.SelectNodes("AppliedConvictions/ConvictionId");
        if (appliedNodes == null || appliedNodes.Count > JusticePolicy.MaxAppliedConvictionIds)
        {
            return null;
        }
        HashSet<string> appliedIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < appliedNodes.Count; index++)
        {
            XmlElement appliedElement = appliedNodes[index] as XmlElement;
            string appliedId = ReadJusticeString(appliedElement, "id");
            if (!IsCanonicalJusticeConvictionId(appliedId) ||
                !appliedIds.Add(appliedId))
            {
                return null;
            }
            state.AppliedConvictionIds.Add(appliedId);
        }
        for (int index = 0; index < state.Convictions.Count; index++)
        {
            JusticeConviction conviction = state.Convictions[index];
            if (conviction == null ||
                !state.AppliedConvictionIds.Contains(conviction.ConvictionId))
            {
                return null;
            }
        }
        if ((state.RecidivismIndex > 0 && state.AppliedConvictionIds.Count == 0) ||
            (state.AppliedConvictionIds.Count > 0 && state.Convictions.Count == 0))
        {
            return null;
        }
        long maximumReachableRecidivism = 0L;
        for (int index = 0; index < state.Convictions.Count; index++)
        {
            maximumReachableRecidivism = JusticePolicy.SaturatingAdd(
                maximumReachableRecidivism,
                JusticePolicy.GetConvictionRecidivismIncrease(
                    state.Convictions[index].Severity),
                100L);
        }
        int hiddenAppliedConvictions = Math.Max(
            0,
            state.AppliedConvictionIds.Count - state.Convictions.Count);
        maximumReachableRecidivism = JusticePolicy.SaturatingAdd(
            maximumReachableRecidivism,
            (long)hiddenAppliedConvictions * 35L,
            100L);
        if (state.RecidivismIndex > maximumReachableRecidivism)
        {
            return null;
        }
        if (state.PinnedConvictionId.Length > 0 &&
            !state.AppliedConvictionIds.Contains(state.PinnedConvictionId))
        {
            return null;
        }
        return state;
    }

    private void NormalizeJusticePinnedConvictionForCurrentCase()
    {
        if (_justiceCaseState == null || _justiceRecordState == null)
        {
            return;
        }

        if (!IsJusticeCustodyPhase(_justiceCaseState.Phase))
        {
            _justiceRecordState.PinnedConvictionId = string.Empty;
            return;
        }

        string custodyEpisode = (_justiceCaseState.CustodyEpisodeId ?? string.Empty).Trim();
        if (custodyEpisode.Length == 0)
        {
            _justiceRecordState.PinnedConvictionId = string.Empty;
            return;
        }

        string convictionId = "conviction:" + custodyEpisode;
        if (_justiceRecordState.AppliedConvictionIds.Contains(convictionId))
        {
            _justiceRecordState.PinnedConvictionId = convictionId;
        }
    }

    private static void AddJusticeEpisodeFallback(List<string> destination, string episodeId)
    {
        string normalized = JusticePolicy.NormalizeEpisodeId(episodeId);
        if (normalized.Length > 0 && !destination.Contains(normalized))
        {
            destination.Add(normalized);
        }
    }

    private static void ReadJusticeIdAttributeList(
        XmlElement root,
        string xpath,
        string attributeName,
        List<string> destination,
        int maximum)
    {
        if (root == null || destination == null || maximum <= 0)
        {
            return;
        }
        XmlNodeList nodes = root.SelectNodes(xpath);
        if (nodes == null)
        {
            return;
        }
        int start = maximum == int.MaxValue ? 0 : Math.Max(0, nodes.Count - maximum);
        for (int index = start; index < nodes.Count && destination.Count < maximum; index++)
        {
            XmlElement item = nodes[index] as XmlElement;
            string value = ReadJusticeString(item, attributeName);
            if (value.Length > 0 && !destination.Contains(value))
            {
                destination.Add(value);
            }
        }
    }

    private static void ReadJusticeStringList(XmlElement root, string xpath, List<string> destination, int maximum)
    {
        XmlNodeList nodes = root.SelectNodes(xpath);
        if (nodes == null)
        {
            return;
        }
        int start = Math.Max(0, nodes.Count - maximum);
        for (int index = start; index < nodes.Count; index++)
        {
            string value = nodes[index] == null ? string.Empty : (nodes[index].InnerText ?? string.Empty).Trim();
            if (value.Length > 0)
            {
                destination.Add(value);
            }
        }
    }

    private void NormalizeLoadedJusticeState()
    {
        bool normalizedPendingDeathCapture = false;
        if (_justiceCaseState == null)
        {
            _justiceCaseState = new JusticeCaseState();
        }
        if (_justiceRecordState == null)
        {
            _justiceRecordState = new JusticeRecordState();
        }

        _justiceCaseState.Enabled = _justiceEnabled;
        _justiceCaseState.ActiveScore = ClampJusticeInt(_justiceCaseState.ActiveScore, 0, JusticePolicy.MaxActiveScore);
        _justiceCaseState.FineDue = ClampJusticeLong(_justiceCaseState.FineDue, 0L, JusticePolicy.MaxActiveFine);
        _justiceCaseState.VoluntaryFinePaid = ClampJusticeLong(
            _justiceCaseState.VoluntaryFinePaid,
            0L,
            JusticePolicy.MaxActiveFine);
        JusticePolicy.NormalizeFineLedger(_justiceCaseState);
        _justiceCaseState.SentenceSeconds = ClampJusticeInt(
            _justiceCaseState.SentenceSeconds,
            0,
            JusticePolicy.MaxActiveSentenceSeconds);
        _justiceRecordState.RecidivismIndex = ClampJusticeInt(_justiceRecordState.RecidivismIndex, 0, 100);

        for (int index = 0; index < _justiceCaseState.Charges.Count; index++)
        {
            JusticeCharge charge = _justiceCaseState.Charges[index];
            if (charge == null)
            {
                continue;
            }

            _justiceNextIdentityGeneration = Math.Max(
                _justiceNextIdentityGeneration,
                Math.Min(int.MaxValue - 1, Math.Max(0, charge.VictimGeneration)));
            for (int contributorIndex = 0;
                 contributorIndex < charge.AlliedContributors.Count;
                 contributorIndex++)
            {
                _justiceNextIdentityGeneration = Math.Max(
                    _justiceNextIdentityGeneration,
                    Math.Min(
                        int.MaxValue - 1,
                        Math.Max(0, charge.AlliedContributors[contributorIndex].Generation)));
            }
        }

        if (_justiceFineDebitIntent == null &&
            _justiceCaseState.SentenceSeconds > 0 &&
            (_justiceCaseState.Phase == JusticePhase.Transporting ||
             _justiceCaseState.Phase == JusticePhase.Escaping))
        {
            _justiceCaseState.Phase = JusticePhase.Incarcerated;
        }
        else if (!HasActiveJusticeCase() && _justiceCaseState.Phase != JusticePhase.Fugitive)
        {
            _justiceCaseState.Phase = JusticePhase.AtLarge;
        }

        if (_justicePursuitDeathObservedDuringSuspension && JusticeIsCustodyActive)
        {
            // Le précommit Capture a gagné la course contre l'effacement du
            // marqueur. La détention est autoritaire et rend ce latch obsolète.
            ClearPendingJusticeDeathCapture();
            normalizedPendingDeathCapture = true;
        }

        _justiceDetectionEpisodeId = _justiceCaseState.WantedEpisodeId ?? string.Empty;
        _justiceStateDirty = normalizedPendingDeathCapture;
        if (normalizedPendingDeathCapture)
        {
            _justiceNextStateSaveAtMs = 0L;
        }
    }

    private void ReconcileLoadedJusticePursuitState(int wantedLevel)
    {
        if (!_justiceEnabled || _justicePursuitDeathObservedDuringSuspension ||
            wantedLevel > 0 || !HasActiveJusticeCase() ||
            (_justiceCaseState.Phase != JusticePhase.Wanted &&
             _justiceCaseState.Phase != JusticePhase.Surrendering))
        {
            return;
        }

        // GTA ne conserve pas forcément son wanted entre deux sessions. Je
        // transforme alors la poursuite persistée en mandat sans inventer une
        // nouvelle charge ni relancer les étoiles pendant le chargement.
        _justiceCaseState.HasWarrant = true;
        _justiceCaseState.Phase = JusticePhase.AtLarge;
        _justicePursuitActive = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        JusticeMarkStateDirty();
        LogInfo("Justice.Chargement", "Poursuite sans wanted convertie en mandat actif.");
    }

    private static string ReadJusticeString(XmlElement element, string attributeName)
    {
        return element == null ? string.Empty : (element.GetAttribute(attributeName) ?? string.Empty).Trim();
    }

    private static int ReadJusticeInt(XmlElement element, string attributeName, int fallback)
    {
        int value;
        return int.TryParse(ReadJusticeString(element, attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : fallback;
    }

    private static long ReadJusticeLong(XmlElement element, string attributeName, long fallback)
    {
        long value;
        return long.TryParse(ReadJusticeString(element, attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : fallback;
    }

    private static bool ReadJusticeBool(XmlElement element, string attributeName, bool fallback)
    {
        bool value;
        return bool.TryParse(ReadJusticeString(element, attributeName), out value) ? value : fallback;
    }

    private static bool TryReadJusticeIntStrict(
        XmlElement element,
        string attributeName,
        int fallback,
        int minimum,
        int maximum,
        out int value)
    {
        value = fallback;
        if (element == null || !element.HasAttribute(attributeName))
        {
            return fallback >= minimum && fallback <= maximum;
        }

        int parsed;
        if (!int.TryParse(
                element.GetAttribute(attributeName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed) ||
            parsed < minimum || parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadJusticeLongStrict(
        XmlElement element,
        string attributeName,
        long fallback,
        long minimum,
        long maximum,
        out long value)
    {
        value = fallback;
        if (element == null || !element.HasAttribute(attributeName))
        {
            return fallback >= minimum && fallback <= maximum;
        }

        long parsed;
        if (!long.TryParse(
                element.GetAttribute(attributeName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed) ||
            parsed < minimum || parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadJusticeBoolStrict(
        XmlElement element,
        string attributeName,
        bool fallback,
        out bool value)
    {
        value = fallback;
        if (element == null || !element.HasAttribute(attributeName))
        {
            return true;
        }

        return bool.TryParse(element.GetAttribute(attributeName), out value);
    }

    private static T ReadJusticeEnum<T>(XmlElement element, string attributeName, T fallback) where T : struct
    {
        string rawValue = ReadJusticeString(element, attributeName);
        if (rawValue.Length == 0)
        {
            return fallback;
        }

        T value;
        if (!TryParseDefinedJusticeEnum(rawValue, out value))
        {
            throw new InvalidDataException(
                "Valeur d'énumération Justice invalide pour " + attributeName + ".");
        }

        return value;
    }

    private static bool TryParseDefinedJusticeEnum<T>(string rawValue, out T value) where T : struct
    {
        return Enum.TryParse(rawValue, true, out value) &&
               Enum.IsDefined(typeof(T), value);
    }

    private static bool IsValidPersistedJusticeOperationId(string operationId)
    {
        JusticeOperationKind kind;
        string episodeId;
        return TryParsePersistedJusticeOperationId(operationId, out kind, out episodeId);
    }

    private static bool IsCanonicalJusticeConvictionId(string convictionId)
    {
        const string prefix = "conviction:";
        if (string.IsNullOrWhiteSpace(convictionId) ||
            convictionId.Length <= prefix.Length ||
            convictionId.Length > 512 ||
            !convictionId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = prefix.Length; index < convictionId.Length; index++)
        {
            if (char.IsWhiteSpace(convictionId[index]) || char.IsControl(convictionId[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParsePersistedJusticeOperationId(
        string operationId,
        out JusticeOperationKind kind,
        out string episodeId)
    {
        kind = JusticeOperationKind.None;
        episodeId = string.Empty;
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return false;
        }

        int separatorIndex = operationId.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= operationId.Length - 1)
        {
            return false;
        }

        if (!TryParseDefinedJusticeEnum(operationId.Substring(0, separatorIndex), out kind) ||
            kind == JusticeOperationKind.None)
        {
            return false;
        }

        episodeId = operationId.Substring(separatorIndex + 1);
        return string.Equals(
            operationId,
            JusticePolicy.CreateOperationId(kind, episodeId),
            StringComparison.Ordinal);
    }

    private static int ClampJusticeInt(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    private static long ClampJusticeLong(long value, long minimum, long maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    private string GetJusticeStatusDisplay()
    {
        if (!_justiceEnabled)
        {
            return "Désactivée";
        }
        if (JusticeIsCustodyActive)
        {
            return "En détention";
        }
        if (_justiceCaseState == null || !HasActiveJusticeCase())
        {
            return "Aucun dossier";
        }
        if (_justiceLastWantedLevel > 0 || _justicePursuitActive)
        {
            return "Poursuite active";
        }
        if (_justiceCaseState.HasWarrant)
        {
            return "Recherché sous mandat";
        }
        return "Dossier actif";
    }

    private string GetJusticeLastCrimeDisplay()
    {
        if (_justiceCaseState == null || string.IsNullOrWhiteSpace(_justiceCaseState.LastCrimeLabel))
        {
            return "Aucune";
        }
        return _justiceCaseState.LastCrimeLabel;
    }

    private string GetJusticeSeverityDisplay()
    {
        JusticeSeverity severity = JusticePolicy.GetSeverity(_justiceCaseState == null ? 0 : _justiceCaseState.ActiveScore);
        return JusticeSeverityDisplayName(severity);
    }

    private static string JusticeSeverityDisplayName(JusticeSeverity severity)
    {
        switch (severity)
        {
            case JusticeSeverity.Minor: return "Mineur";
            case JusticeSeverity.Misdemeanor: return "Délit";
            case JusticeSeverity.Serious: return "Grave";
            case JusticeSeverity.Crime: return "Crime";
            case JusticeSeverity.Major: return "Majeur";
            case JusticeSeverity.Critical: return "Critique";
            default: return "Aucune";
        }
    }

    private string GetJusticeWarrantDisplay()
    {
        return _justiceCaseState != null && _justiceCaseState.HasWarrant ? "ACTIF" : "Aucun";
    }

    private string GetJusticeChargesDisplay()
    {
        int count = JusticePolicy.GetRepresentedChargeCount(_justiceCaseState);
        return count.ToString(CultureInfo.InvariantCulture);
    }

    private string GetJusticeFineDisplay()
    {
        return FormatJusticeMoney(_justiceCaseState == null ? 0L : _justiceCaseState.FineDue);
    }

    private string GetJusticeSentenceDisplay()
    {
        return FormatJusticeDuration(_justiceCaseState == null ? 0 : _justiceCaseState.SentenceSeconds);
    }

    private string GetJusticeRecidivismDisplay()
    {
        int recidivism = _justiceRecordState == null ? 0 : _justiceRecordState.RecidivismIndex;
        return "R " + recidivism.ToString(CultureInfo.InvariantCulture) + "/100";
    }

    private static string FormatJusticeMoney(long amount)
    {
        long bounded = Math.Max(0L, Math.Min(JusticePolicy.MaxActiveFine, amount));
        return bounded.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") + "$";
    }

    private static string FormatJusticeDuration(int seconds)
    {
        int bounded = Math.Max(0, Math.Min(JusticePolicy.MaxActiveSentenceSeconds, seconds));
        int minutes = bounded / 60;
        int remainingSeconds = bounded % 60;
        return minutes.ToString(CultureInfo.InvariantCulture) + ":" +
               remainingSeconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private Rectangle GetRuntimeJusticeHudBounds()
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
        if (_runtimeJusticeHudBounds == Rectangle.Empty ||
            width != _runtimeJusticeHudScreenWidth ||
            height != _runtimeJusticeHudScreenHeight ||
            Math.Abs(safe - _runtimeJusticeHudSafeZone) > 0.001f)
        {
            _runtimeJusticeHudBounds = CalculateJusticeHudBounds(width, height, safe);
            _runtimeJusticeHudScreenWidth = width;
            _runtimeJusticeHudScreenHeight = height;
            _runtimeJusticeHudSafeZone = safe;
        }
        return _runtimeJusticeHudBounds;
    }

    private static Rectangle CalculateJusticeHudBounds(int screenWidth, int screenHeight, float safeZone)
    {
        MenuViewport viewport = CalculateMenuViewport(screenWidth, screenHeight, safeZone);
        float logicalWidth = Math.Max(1.0f, viewport.LogicalWidth);
        float logicalX = viewport.SafeLeft + 12.0f;
        float logicalY = viewport.SafeTop + 12.0f;
        float availableWidth = Math.Max(1.0f, viewport.SafeLogicalWidth - 24.0f);
        float availableHeight = Math.Max(1.0f, viewport.SafeLogicalHeight - 24.0f);
        float xFactor = 1280.0f / logicalWidth;
        return LogicalRectangleToUi(
            logicalX,
            logicalY,
            Math.Min(JusticeHudLogicalWidth, availableWidth),
            Math.Min(JusticeHudLogicalHeight, availableHeight),
            xFactor);
    }

    private void JusticeHudRectangle(int x, int y, int width, int height, Color color)
    {
        if (_justiceHudRectangleCursor >= _justiceHudRectanglePool.Count)
        {
            return;
        }
        UIRectangle rectangle = _justiceHudRectanglePool[_justiceHudRectangleCursor++];
        rectangle.Enabled = true;
        rectangle.Position = new Point(x, y);
        rectangle.Size = new Size(Math.Max(1, width), Math.Max(1, height));
        rectangle.Color = color;
        rectangle.Draw();
    }

    private void JusticeHudText(string caption, int x, int y, float scale, Color color, bool outline)
    {
        if (_justiceHudTextCursor >= _justiceHudTextPool.Count)
        {
            return;
        }
        UIText text = _justiceHudTextPool[_justiceHudTextCursor++];
        text.Enabled = true;
        text.Caption = caption ?? string.Empty;
        text.Position = new Point(x, y);
        text.Scale = scale;
        text.Color = color;
        text.Font = GTA.Font.ChaletLondon;
        text.Centered = false;
        text.Shadow = false;
        text.Outline = outline;
        text.Draw();
    }
}
