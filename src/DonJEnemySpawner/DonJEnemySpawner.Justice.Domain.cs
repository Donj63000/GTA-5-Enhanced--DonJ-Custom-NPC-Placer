using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DonJEnemySpawner.Tests")]

// Je garde ce fichier totalement indépendant de GTA afin de pouvoir valider la
// justice, ses calculs et ses transitions sans charger le jeu ni l'API NIB.

internal enum JusticeCrimeKind
{
    ReportedViolentAct,
    RecklessDischarge,
    VehicleDamage,
    ArmedThreat,
    VehicleTheft,
    VehicleDestruction,
    SimpleAssault,
    HitAndRun,
    EvadingPolice,
    AccessoryAssaultOfficer,
    Carjacking,
    ResistingArrest,
    AggravatedAssault,
    AssaultOfficer,
    AccessoryMurderOfficer,
    Manslaughter,
    MurderCivilian,
    Escape,
    MurderOfficer
}

[Flags]
internal enum JusticeEvidenceKind
{
    None = 0,
    VictimWitness = 1,
    CivilianWitness = 2,
    PoliceWitness = 4,
    CorrelatedWantedRise = 8,
    DirectGameReport = 16
}

[Flags]
internal enum JusticeCircumstances
{
    None = 0,
    Armed = 1,
    ExplosiveOrIncendiary = 2,
    ActiveWarrant = 4,
    InCustody = 8,
    MultipleVictims = 16,
    GroupCrime = 32,
    OrganizedBand = 64,
    ProportionalSelfDefense = 128,
    ExcessiveSelfDefense = 256,
    VehicleUsedAsWeapon = 512
}

internal enum JusticeSeverity
{
    None,
    Minor,
    Misdemeanor,
    Serious,
    Crime,
    Major,
    Critical
}

internal enum JusticePhase
{
    AtLarge,
    Wanted,
    Surrendering,
    Captured,
    Transporting,
    Incarcerated,
    Escaping,
    Fugitive
}

[Flags]
internal enum JusticeSignal
{
    None = 0,
    ConfirmedCharge = 1,
    WarrantRecognized = 2,
    ArrestStarted = 4,
    ArrestCancelled = 8,
    ArrestCompleted = 16,
    PlayerDiedDuringPolicePursuit = 32,
    TransferReady = 64,
    TransferCompleted = 128,
    TransferTimedOut = 256,
    LeftCustody = 512,
    EscapeConfirmed = 1024,
    Restrained = 2048,
    SentenceCompleted = 4096
}

internal enum JusticeOperationKind
{
    None,
    // Je conserve cette valeur uniquement pour relire les opérations v1. Le
    // runtime ne fabrique plus aucun plancher wanted à partir d'une infraction.
    ApplyWantedFloor,
    ApplyFine,
    ApplyConviction,
    Capture,
    ConfiscateInventory,
    Transport,
    EnterCustody,
    RegisterEscape,
    Release,
    RestoreInventory,
    DiscardInventory,
    TransferRollback,
    ResetProfile
}

// Je distingue l'etat metier d'un paiement du resultat brut renvoye par la
// native GTA. Une ecriture ambigue ne peut ainsi plus etre confondue avec un
// debit confirme pendant la reprise d'une transaction.
internal enum JusticePaymentResolution
{
    Prepared,
    Attempted,
    Confirmed,
    Rejected,
    Ambiguous
}

internal sealed class JusticeCrimeDefinition
{
    internal JusticeCrimeDefinition(
        JusticeCrimeKind kind,
        string displayName,
        int basePoints,
        long baseFine,
        int baseSentenceSeconds)
    {
        Kind = kind;
        DisplayName = displayName ?? string.Empty;
        BasePoints = Math.Max(0, basePoints);
        BaseFine = Math.Max(0L, baseFine);
        BaseSentenceSeconds = Math.Max(0, baseSentenceSeconds);
    }

    internal JusticeCrimeKind Kind { get; private set; }

    internal string DisplayName { get; private set; }

    internal int BasePoints { get; private set; }

    internal long BaseFine { get; private set; }

    internal int BaseSentenceSeconds { get; private set; }
}

internal sealed class JusticeEvidence
{
    internal JusticeEvidence()
    {
        Kind = JusticeEvidenceKind.None;
        WitnessHandle = 0;
        WitnessGeneration = 0;
        ObservedAtMs = 0L;
        ReportDueAtMs = 0L;
        HasPlausibleObserver = false;
        ReportCompleted = false;
    }

    internal JusticeEvidenceKind Kind { get; set; }

    internal int WitnessHandle { get; set; }

    internal int WitnessGeneration { get; set; }

    internal long ObservedAtMs { get; set; }

    internal long ReportDueAtMs { get; set; }

    internal bool HasPlausibleObserver { get; set; }

    internal bool ReportCompleted { get; set; }

    internal bool HasCredibleSource
    {
        get
        {
            JusticeEvidenceKind directWitnesses =
                JusticeEvidenceKind.VictimWitness |
                JusticeEvidenceKind.CivilianWitness |
                JusticeEvidenceKind.PoliceWitness;

            if ((Kind & directWitnesses) != 0)
            {
                return HasPlausibleObserver;
            }

            if ((Kind & JusticeEvidenceKind.DirectGameReport) != 0)
            {
                return HasPlausibleObserver;
            }

            // Je n'accepte une hausse d'étoiles que si le runtime a déjà associé
            // un observateur plausible à l'incident précis.
            return (Kind & JusticeEvidenceKind.CorrelatedWantedRise) != 0 && HasPlausibleObserver;
        }
    }

    internal bool IsConfirmed(long nowMs, bool witnessAlive)
    {
        if (!HasCredibleSource)
        {
            return false;
        }

        if (ReportCompleted)
        {
            return true;
        }

        if ((Kind & JusticeEvidenceKind.PoliceWitness) != 0 ||
            (Kind & JusticeEvidenceKind.DirectGameReport) != 0 ||
            (Kind & JusticeEvidenceKind.CorrelatedWantedRise) != 0)
        {
            // Un policier observateur et un signal GTA corrélé confirment au
            // moment de l'observation. Leur décès ultérieur n'efface pas le dépôt.
            ReportCompleted = true;
            return true;
        }

        if ((Kind & (JusticeEvidenceKind.VictimWitness | JusticeEvidenceKind.CivilianWitness)) != 0)
        {
            if (witnessAlive && nowMs >= ReportDueAtMs)
            {
                ReportCompleted = true;
                return true;
            }
            return false;
        }

        return false;
    }
}

internal sealed class JusticeIncident
{
    internal JusticeIncident()
    {
        IncidentId = string.Empty;
        EpisodeId = string.Empty;
        DetectionBatchId = string.Empty;
        CausalEventId = string.Empty;
        Evidence = new JusticeEvidence();
        Circumstances = JusticeCircumstances.None;
    }

    internal string IncidentId { get; set; }

    internal string EpisodeId { get; set; }

    internal string DetectionBatchId { get; set; }

    internal string CausalEventId { get; set; }

    internal JusticeCrimeKind Kind { get; set; }

    internal int VictimHandle { get; set; }

    internal int VictimGeneration { get; set; }

    internal int AllyHandle { get; set; }

    internal int AllyGeneration { get; set; }

    internal long CreatedAtMs { get; set; }

    internal long ExpiresAtMs { get; set; }

    internal JusticeEvidence Evidence { get; set; }

    internal JusticeCircumstances Circumstances { get; set; }

    internal int AdditionalVictimCount { get; set; }

    internal bool IsAlliedAction { get; set; }

    internal bool IsConfirmed { get; set; }

    internal bool IsExpired(long nowMs)
    {
        return ExpiresAtMs > 0L && nowMs > ExpiresAtMs;
    }

    internal bool TryConfirm(long nowMs, bool witnessAlive)
    {
        if (IsConfirmed)
        {
            return true;
        }

        if (IsExpired(nowMs) || Evidence == null || !Evidence.IsConfirmed(nowMs, witnessAlive))
        {
            return false;
        }

        IsConfirmed = true;
        return true;
    }
}

internal struct JusticeEntityIdentity : IEquatable<JusticeEntityIdentity>
{
    internal JusticeEntityIdentity(int handle, int generation)
    {
        Handle = handle;
        Generation = generation;
    }

    internal int Handle { get; private set; }

    internal int Generation { get; private set; }

    public bool Equals(JusticeEntityIdentity other)
    {
        return Handle == other.Handle && Generation == other.Generation;
    }

    public override bool Equals(object obj)
    {
        return obj is JusticeEntityIdentity && Equals((JusticeEntityIdentity)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Handle * 397) ^ Generation;
        }
    }
}

internal sealed class JusticeCharge
{
    internal JusticeCharge()
    {
        ChargeId = string.Empty;
        IncidentId = string.Empty;
        EpisodeId = string.Empty;
        DetectionBatchId = string.Empty;
        CausalEventId = string.Empty;
        DisplayName = string.Empty;
        AlliedContributorHandles = new List<int>();
        AlliedContributors = new List<JusticeEntityIdentity>();
    }

    internal string ChargeId { get; set; }

    internal string IncidentId { get; set; }

    internal string EpisodeId { get; set; }

    internal string DetectionBatchId { get; set; }

    internal string CausalEventId { get; set; }

    internal JusticeCrimeKind Kind { get; set; }

    internal string DisplayName { get; set; }

    internal int VictimHandle { get; set; }

    internal int VictimGeneration { get; set; }

    internal int Points { get; set; }

    internal long Fine { get; set; }

    internal int SentenceSeconds { get; set; }

    internal long ConfirmedAtMs { get; set; }

    internal bool IsAlliedAction { get; set; }

    internal JusticeCircumstances Circumstances { get; set; }

    internal int AdditionalVictimCount { get; set; }

    internal bool IsAggregate { get; set; }

    internal int AggregatedChargeCount { get; set; }

    internal List<int> AlliedContributorHandles { get; private set; }

    internal List<JusticeEntityIdentity> AlliedContributors { get; private set; }

    internal void AddAlliedContributor(int handle, int generation)
    {
        if (handle <= 0)
        {
            return;
        }

        int normalizedGeneration = Math.Max(0, generation);
        for (int index = 0; index < AlliedContributors.Count; index++)
        {
            JusticeEntityIdentity existing = AlliedContributors[index];
            if (existing.Handle != handle)
            {
                continue;
            }

            if (existing.Generation == normalizedGeneration || normalizedGeneration == 0)
            {
                return;
            }

            if (existing.Generation == 0)
            {
                // Je remplace l'identité legacy sans génération dès que GTA me
                // fournit la génération réelle du ped. Le même allié ne peut
                // ainsi jamais devenir artificiellement une seconde personne.
                AlliedContributors[index] = new JusticeEntityIdentity(handle, normalizedGeneration);
                if (!AlliedContributorHandles.Contains(handle))
                {
                    AlliedContributorHandles.Add(handle);
                }
                return;
            }
        }

        AlliedContributors.Add(new JusticeEntityIdentity(handle, normalizedGeneration));
        if (!AlliedContributorHandles.Contains(handle))
        {
            AlliedContributorHandles.Add(handle);
        }
    }

    internal void ImportLegacyAlliedContributorHandles()
    {
        for (int index = 0; index < AlliedContributorHandles.Count; index++)
        {
            int handle = AlliedContributorHandles[index];
            if (handle <= 0)
            {
                continue;
            }

            bool represented = false;
            for (int identityIndex = 0; identityIndex < AlliedContributors.Count; identityIndex++)
            {
                if (AlliedContributors[identityIndex].Handle == handle)
                {
                    represented = true;
                    break;
                }
            }
            if (!represented)
            {
                AlliedContributors.Add(new JusticeEntityIdentity(handle, 0));
            }
        }
    }

    internal bool HasAlliedContributor(int handle, int generation)
    {
        ImportLegacyAlliedContributorHandles();
        int normalizedGeneration = Math.Max(0, generation);
        for (int index = 0; index < AlliedContributors.Count; index++)
        {
            JusticeEntityIdentity existing = AlliedContributors[index];
            if (existing.Handle != handle)
            {
                continue;
            }

            if (existing.Generation == normalizedGeneration || normalizedGeneration == 0)
            {
                return true;
            }

            if (existing.Generation == 0)
            {
                AlliedContributors[index] = new JusticeEntityIdentity(handle, normalizedGeneration);
                return true;
            }
        }

        return false;
    }

    internal bool IsAdjudicated { get; set; }
}

internal sealed class JusticeConvictionChargeSummary
{
    internal JusticeConvictionChargeSummary()
    {
        DisplayName = string.Empty;
        CircumstancesWerePersisted = true;
    }

    internal JusticeCrimeKind Kind { get; set; }

    internal string DisplayName { get; set; }

    internal int Points { get; set; }

    internal long Fine { get; set; }

    internal int SentenceSeconds { get; set; }

    internal JusticeCircumstances Circumstances { get; set; }

    // Je distingue un ancien résumé v1 sans cet attribut d'un résumé moderne
    // qui porte volontairement la valeur None.
    internal bool CircumstancesWerePersisted { get; set; }

    internal bool IsAggregate { get; set; }

    internal int AggregatedChargeCount { get; set; }
}

internal sealed class JusticeConviction
{
    internal JusticeConviction()
    {
        ConvictionId = string.Empty;
        JudgedAtUtc = DateTime.MinValue;
        Charges = new List<JusticeConvictionChargeSummary>();
    }

    internal string ConvictionId { get; set; }

    internal DateTime JudgedAtUtc { get; set; }

    internal JusticeSeverity Severity { get; set; }

    internal int Score { get; set; }

    internal long Fine { get; set; }

    internal int SentenceSeconds { get; set; }

    internal List<JusticeConvictionChargeSummary> Charges { get; private set; }
}

internal sealed class JusticeRecordState
{
    internal JusticeRecordState()
    {
        Convictions = new List<JusticeConviction>();
        AppliedConvictionIds = new List<string>();
        PinnedConvictionId = string.Empty;
    }

    internal int RecidivismIndex { get; set; }

    internal int CleanGameplaySeconds { get; set; }

    internal int AppliedCleanDecay { get; set; }

    internal List<JusticeConviction> Convictions { get; private set; }

    internal List<string> AppliedConvictionIds { get; private set; }

    // Je fournis au rendu une révision O(1) : il n'a plus à reparcourir les
    // condamnations et toutes leurs charges à chaque frame pour détecter un ajout.
    internal int LedgerRevision { get; private set; }

    internal void MarkLedgerChanged()
    {
        LedgerRevision = LedgerRevision == int.MaxValue ? 1 : LedgerRevision + 1;
    }

    // Je garde la condamnation de la détention courante hors de la politique
    // d'éviction afin que sa libération ne perde jamais son ancrage WAL.
    internal string PinnedConvictionId { get; set; }
}

internal sealed class JusticeCaseState
{
    internal JusticeCaseState()
    {
        Charges = new List<JusticeCharge>();
        CompletedOperationIds = new List<string>();
        ProcessedIncidentIds = new List<string>();
        FleeingChargedEpisodeIds = new List<string>();
        EscapeChargedEpisodeIds = new List<string>();
        WantedEpisodeId = string.Empty;
        CustodyEpisodeId = string.Empty;
        LastCrimeLabel = string.Empty;
        Phase = JusticePhase.AtLarge;
    }

    internal bool Enabled { get; set; }

    internal List<JusticeCharge> Charges { get; private set; }

    internal int ActiveScore { get; set; }

    internal long FineDue { get; set; }

    // Je conserve séparément les dollars déjà réglés avant jugement. Le solde
    // peut ainsi rester inférieur aux amendes brutes des charges sans renaître
    // à la prochaine infraction ou après un rechargement.
    internal long VoluntaryFinePaid { get; set; }

    // Je sors du solde exigible tout debit dont le resultat est impossible a
    // prouver. Cette somme reste visible et persistable, mais elle ne doit etre
    // ni rejouee ni comptee comme un paiement confirme.
    internal long FineInDispute { get; set; }

    internal int SentenceSeconds { get; set; }

    internal bool HasWarrant { get; set; }

    // Je porte dans le dossier le petit WAL du minimum d'étoiles propre à
    // l'évasion. Il reste distinct des infractions ordinaires, qui ne modifient
    // jamais le wanted GTA.
    internal bool EscapeWantedMinimumPending { get; set; }

    internal bool EscapeWantedMinimumAttempted { get; set; }

    internal JusticePhase Phase { get; set; }

    internal string WantedEpisodeId { get; set; }

    internal string CustodyEpisodeId { get; set; }

    internal JusticeCrimeKind? LastCrimeKind { get; set; }

    internal string LastCrimeLabel { get; set; }

    internal List<string> CompletedOperationIds { get; private set; }

    internal List<string> ProcessedIncidentIds { get; private set; }

    internal List<string> FleeingChargedEpisodeIds { get; private set; }

    internal List<string> EscapeChargedEpisodeIds { get; private set; }

    // Je garde ces deux accesseurs pendant la migration du runtime et des
    // sauvegardes v1. Une écriture vraie sans épisode ne doit plus pré-marquer
    // une infraction avant que le domaine l'ait réellement acceptée.
    internal bool FleeingCharged
    {
        get { return FleeingChargedEpisodeIds.Count > 0; }
        set
        {
            if (!value)
            {
                FleeingChargedEpisodeIds.Clear();
            }
        }
    }

    internal bool EscapeCharged
    {
        get { return EscapeChargedEpisodeIds.Count > 0; }
        set
        {
            if (!value)
            {
                EscapeChargedEpisodeIds.Clear();
            }
        }
    }

    internal bool IsFleeingChargedForEpisode(string episodeId)
    {
        return JusticePolicy.ContainsEpisodeId(FleeingChargedEpisodeIds, episodeId) ||
               HasChargeForEpisode(JusticeCrimeKind.EvadingPolice, episodeId);
    }

    internal bool IsEscapeChargedForEpisode(string episodeId)
    {
        return JusticePolicy.ContainsEpisodeId(EscapeChargedEpisodeIds, episodeId) ||
               HasChargeForEpisode(JusticeCrimeKind.Escape, episodeId);
    }

    private bool HasChargeForEpisode(JusticeCrimeKind kind, string episodeId)
    {
        string normalizedEpisode = JusticePolicy.NormalizeEpisodeId(episodeId);
        if (normalizedEpisode.Length == 0)
        {
            return false;
        }

        for (int index = 0; index < Charges.Count; index++)
        {
            JusticeCharge charge = Charges[index];
            if (charge != null && !charge.IsAggregate && charge.Kind == kind && string.Equals(
                charge.EpisodeId,
                normalizedEpisode,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal void RecalculateTotals()
    {
        long score = 0L;
        long fine = 0L;
        long sentence = 0L;

        for (int index = 0; index < Charges.Count; index++)
        {
            JusticeCharge charge = Charges[index];
            if (charge == null)
            {
                continue;
            }

            score = JusticePolicy.SaturatingAdd(score, Math.Max(0, charge.Points), JusticePolicy.MaxActiveScore);
            fine = JusticePolicy.SaturatingAdd(fine, Math.Max(0L, charge.Fine), JusticePolicy.MaxActiveFine);
            sentence = JusticePolicy.SaturatingAdd(sentence, Math.Max(0, charge.SentenceSeconds), JusticePolicy.MaxActiveSentenceSeconds);
        }

        ActiveScore = (int)Math.Min(JusticePolicy.MaxActiveScore, score);
        long boundedFine = Math.Min(JusticePolicy.MaxActiveFine, fine);
        long accountedFine = JusticePolicy.SaturatingAdd(
            Math.Max(0L, Math.Min(JusticePolicy.MaxActiveFine, VoluntaryFinePaid)),
            Math.Max(0L, Math.Min(JusticePolicy.MaxActiveFine, FineInDispute)),
            JusticePolicy.MaxActiveFine);
        FineDue = Math.Max(0L, boundedFine - Math.Min(accountedFine, boundedFine));
        SentenceSeconds = (int)Math.Min(JusticePolicy.MaxActiveSentenceSeconds, sentence);
    }

    internal void ClearActiveCase(bool preserveCompletedOperations)
    {
        Charges.Clear();
        ProcessedIncidentIds.Clear();
        ActiveScore = 0;
        FineDue = 0L;
        VoluntaryFinePaid = 0L;
        FineInDispute = 0L;
        SentenceSeconds = 0;
        HasWarrant = false;
        EscapeWantedMinimumPending = false;
        EscapeWantedMinimumAttempted = false;
        WantedEpisodeId = string.Empty;
        CustodyEpisodeId = string.Empty;
        LastCrimeKind = null;
        LastCrimeLabel = string.Empty;
        FleeingChargedEpisodeIds.Clear();
        EscapeChargedEpisodeIds.Clear();
        Phase = JusticePhase.AtLarge;

        if (!preserveCompletedOperations)
        {
            CompletedOperationIds.Clear();
        }
    }
}

// Je conserve pour chaque protagoniste son dossier complet et la detention
// serialisee. Le runtime ne branche qu'un profil a la fois sur les champs
// historiques : aucun casier, dette ou inventaire ne peut ainsi migrer vers un
// autre heros lors d'un changement de personnage.
internal sealed class JusticePlayerProfileState
{
    internal JusticePlayerProfileState(int slot)
    {
        Slot = slot;
        CaseState = new JusticeCaseState();
        RecordState = new JusticeRecordState();
        CustodyXml = string.Empty;
        CustodySnapshot = null;
        PendingDeathCapturePlayerSlot = -1;
    }

    internal int Slot { get; private set; }

    internal JusticeCaseState CaseState { get; set; }

    internal JusticeRecordState RecordState { get; set; }

    internal string CustodyXml { get; set; }

    // Une capture runtime reste typée et immuable. CustodyXml n'est conservé que
    // pour les profils venant d'un fichier historique ou v2 déjà matérialisé.
    internal JusticeCustodyPersistenceSnapshot CustodySnapshot { get; set; }

    internal bool PendingDeathCapture { get; set; }

    internal int PendingDeathCapturePlayerSlot { get; set; }

    internal int PendingDeathCapturePlayerModel { get; set; }

    internal bool PendingAmnestyWantedClear { get; set; }

    internal bool PendingLegalReleaseFinalization { get; set; }

    internal int PendingLegalReleaseSite { get; set; }

    internal int PendingLegalReleaseSelectedWeapon { get; set; }

    internal int LastCanonicalPlayerModel { get; set; }

    // Je garde ces trois valeurs uniquement en mémoire : elles cadencent la peine
    // d'un autre protagoniste pendant le gameplay sans créer de temps hors ligne.
    internal bool CanAdvanceCustodyInBackground { get; set; }

    internal int InactiveCustodyLastTickAt { get; set; }

    internal int InactiveCustodyElapsedRemainderMs { get; set; }
}

internal sealed class JusticeSanction
{
    private static readonly JusticeSanction EmptySanction = new JusticeSanction(false, 0, 0L, 0, 0);

    internal JusticeSanction(bool isChargeable, int points, long fine, int sentenceSeconds, int circumstanceBasisPoints)
    {
        IsChargeable = isChargeable;
        Points = Math.Max(0, points);
        Fine = Math.Max(0L, fine);
        SentenceSeconds = Math.Max(0, sentenceSeconds);
        CircumstanceBasisPoints = circumstanceBasisPoints;
    }

    internal static JusticeSanction None { get { return EmptySanction; } }

    internal bool IsChargeable { get; private set; }

    internal int Points { get; private set; }

    internal long Fine { get; private set; }

    internal int SentenceSeconds { get; private set; }

    internal int CircumstanceBasisPoints { get; private set; }
}

internal sealed class JusticeTickInput
{
    internal JusticeTickInput()
    {
        EpisodeId = string.Empty;
    }

    internal JusticeSignal Signals { get; set; }

    internal string EpisodeId { get; set; }
}

internal sealed class JusticeOperation
{
    internal JusticeOperation()
    {
        OperationId = string.Empty;
        EpisodeId = string.Empty;
        Kind = JusticeOperationKind.None;
    }

    internal JusticeOperation(string operationId, JusticeOperationKind kind, string episodeId)
    {
        OperationId = operationId ?? string.Empty;
        Kind = kind;
        EpisodeId = episodeId ?? string.Empty;
    }

    internal string OperationId { get; set; }

    internal JusticeOperationKind Kind { get; set; }

    internal string EpisodeId { get; set; }
}

internal sealed class JusticeTransition
{
    internal JusticeTransition(JusticePhase previousPhase, JusticePhase nextPhase, JusticeOperation operation)
    {
        PreviousPhase = previousPhase;
        NextPhase = nextPhase;
        Operation = operation;
    }

    internal JusticePhase PreviousPhase { get; private set; }

    internal JusticePhase NextPhase { get; private set; }

    internal JusticeOperation Operation { get; private set; }

    internal bool Changed { get { return PreviousPhase != NextPhase; } }
}

internal sealed class JusticePolicy
{
    internal const int PendingIncidentLifetimeMs = 6000;
    internal const int CivilianReportDelayMs = 3000;
    internal const int WantedCorrelationWindowMs = 4000;
    internal const int MaxActiveScore = 1000000;
    // Je retire le plafond gameplay de 250 000 $. Cette borne technique d'un
    // billion protège seulement le XML et les additions saturées; elle reste
    // hors d'atteinte d'une partie normale et autorise une dette très élevée.
    internal const long MaxActiveFine = 1000000000000L;
    internal const int MaxActiveSentenceSeconds = 10 * 60;
    internal const int SentenceRoundingQuantumSeconds = 5;
    internal const long FineDebitAmbiguityTimeoutTicks = 5L * TimeSpan.TicksPerSecond;
    internal const int MaxConvictions = 20;
    internal const int MaxActiveCharges = 512;
    internal const int MaxRememberedOperations = 128;
    internal const int MaxAppliedConvictionIds = 128;
    internal const int MaxRememberedIncidents = 512;
    internal const int MaxChargedEpisodeIds = 128;
    internal const int MaxAlliedContributorsPerCharge = 24;
    internal const int EscapeMinimumWantedLevel = 3;

    private const int BasisPointScale = 10000;
    private const int MinimumCircumstanceBasisPoints = 6000;
    private const int MaximumCircumstanceBasisPoints = 25000;

    private static readonly ReadOnlyDictionary<JusticeCrimeKind, JusticeCrimeDefinition> CrimeCatalog =
        new ReadOnlyDictionary<JusticeCrimeKind, JusticeCrimeDefinition>(CreateCatalog());

    private static readonly JusticePolicy DefaultPolicy = new JusticePolicy();

    private JusticePolicy()
    {
    }

    internal static JusticePolicy Default { get { return DefaultPolicy; } }

    internal static IReadOnlyDictionary<JusticeCrimeKind, JusticeCrimeDefinition> Catalog
    {
        get { return CrimeCatalog; }
    }

    internal static JusticeCrimeDefinition GetDefinition(JusticeCrimeKind kind)
    {
        JusticeCrimeDefinition definition;
        if (!CrimeCatalog.TryGetValue(kind, out definition))
        {
            throw new ArgumentOutOfRangeException("kind", kind, "Infraction Justice inconnue.");
        }

        return definition;
    }

    internal static JusticeSanction Evaluate(JusticeIncident incident, JusticeRecordState record)
    {
        if (incident == null)
        {
            throw new ArgumentNullException("incident");
        }

        JusticeCircumstances normalizedCircumstances =
            NormalizeCircumstancesForCrime(incident.Kind, incident.Circumstances);
        if ((normalizedCircumstances & JusticeCircumstances.ProportionalSelfDefense) != 0)
        {
            return JusticeSanction.None;
        }

        JusticeCrimeDefinition definition = GetDefinition(incident.Kind);
        int circumstanceBasisPoints = CalculateCircumstanceBasisPoints(
            normalizedCircumstances,
            incident.AdditionalVictimCount);
        int recidivism = Clamp(record == null ? 0 : record.RecidivismIndex, 0, 100);

        // Je n'arrondis qu'après avoir appliqué les deux coefficients afin qu'un
        // petit indice de récidive ne crée pas artificiellement un point entier.
        long points = MultiplyCombinedCeiling(
            definition.BasePoints,
            circumstanceBasisPoints,
            BasisPointScale + (recidivism * 30));
        long fine = MultiplyCombinedCeiling(
            definition.BaseFine,
            circumstanceBasisPoints,
            BasisPointScale + (recidivism * 50));
        long sentence = MultiplyCombinedCeiling(
            definition.BaseSentenceSeconds,
            circumstanceBasisPoints,
            BasisPointScale + (recidivism * 75));

        int boundedPoints = (int)Math.Min(MaxActiveScore, points);
        long roundedFine = Math.Min(MaxActiveFine, RoundUp(fine, 50L));
        int roundedSentence = (int)Math.Min(
            MaxActiveSentenceSeconds,
            RoundUp(sentence, SentenceRoundingQuantumSeconds));

        return new JusticeSanction(true, boundedPoints, roundedFine, roundedSentence, circumstanceBasisPoints);
    }

    internal static JusticeCharge ApplyConfirmedIncident(
        JusticeCaseState caseState,
        JusticeIncident incident,
        JusticeRecordState record)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        if (incident == null)
        {
            throw new ArgumentNullException("incident");
        }

        if (!caseState.Enabled || !incident.IsConfirmed || incident.Evidence == null ||
            !incident.Evidence.HasCredibleSource)
        {
            return null;
        }

        string incidentId = NormalizeIdentifier(incident.IncidentId);
        if (incidentId.Length == 0 || ContainsOrdinal(caseState.ProcessedIncidentIds, incidentId))
        {
            return null;
        }

        string episodeId = ResolveIncidentEpisodeId(caseState, incident, incidentId);

        // Je ne retiens la fuite et l'évasion qu'une seule fois par épisode,
        // même si plusieurs détecteurs runtime observent ensuite le même état.
        if ((incident.Kind == JusticeCrimeKind.EvadingPolice &&
             caseState.IsFleeingChargedForEpisode(episodeId)) ||
            (incident.Kind == JusticeCrimeKind.Escape &&
             caseState.IsEscapeChargedForEpisode(episodeId)))
        {
            RememberBounded(caseState.ProcessedIncidentIds, incidentId, MaxRememberedIncidents);
            return null;
        }

        RememberBounded(caseState.ProcessedIncidentIds, incidentId, MaxRememberedIncidents);

        JusticeSanction sanction = Evaluate(incident, record);
        if (!sanction.IsChargeable)
        {
            return null;
        }

        long fineDueBeforeMutation = caseState.FineDue;
        int sentenceBeforeMutation = caseState.SentenceSeconds;
        long pendingFineBeforeMutation = CalculatePendingFine(caseState);
        int pendingSentenceBeforeMutation = CalculatePendingSentence(caseState);

        // Je ne calcule l'aggravant multi-victimes qu'avec des faits déjà
        // confirmés. Un incident provisoire dont le témoin disparaît ne peut
        // donc jamais alourdir une autre charge.
        ApplyConfirmedBatchMultiplicity(caseState, incident, episodeId, record);
        sanction = Evaluate(incident, record);

        JusticeCrimeDefinition definition = GetDefinition(incident.Kind);
        JusticeCharge candidate = new JusticeCharge
        {
            ChargeId = "charge:" + incidentId,
            IncidentId = incidentId,
            EpisodeId = episodeId,
            DetectionBatchId = NormalizeIdentifier(incident.DetectionBatchId),
            CausalEventId = NormalizeIdentifier(incident.CausalEventId),
            Kind = incident.Kind,
            DisplayName = definition.DisplayName,
            VictimHandle = incident.VictimHandle,
            VictimGeneration = incident.VictimGeneration,
            Points = sanction.Points,
            Fine = sanction.Fine,
            SentenceSeconds = sanction.SentenceSeconds,
            ConfirmedAtMs = Math.Max(0L, incident.CreatedAtMs),
            IsAlliedAction = incident.IsAlliedAction,
            Circumstances = NormalizeCircumstancesForCrime(incident.Kind, incident.Circumstances),
            AdditionalVictimCount = Math.Max(0, incident.AdditionalVictimCount)
        };
        AddAlliedContributor(candidate, incident.AllyHandle, incident.AllyGeneration);
        NormalizeChargeCollectiveCircumstance(candidate);
        RecalculateChargeSanction(candidate, record);

        for (int index = caseState.Charges.Count - 1; index >= 0; index--)
        {
            JusticeCharge existing = caseState.Charges[index];
            if (existing == null || existing.IsAggregate || existing.IsAdjudicated)
            {
                continue;
            }

            if (DoesRelatedViolenceSupersedeRecklessDischarge(candidate, existing))
            {
                caseState.Charges.RemoveAt(index);
                continue;
            }

            if (DoesRelatedViolenceSupersedeRecklessDischarge(existing, candidate))
            {
                RecalculateCaseAfterChargeMutation(
                    caseState,
                    fineDueBeforeMutation,
                    sentenceBeforeMutation,
                    pendingFineBeforeMutation,
                    pendingSentenceBeforeMutation);
                return null;
            }

            if (!IsSameVictimEpisode(existing, candidate))
            {
                continue;
            }

            MigrateLegacyVictimGeneration(existing, candidate);

            if (ShouldMergeCollectiveContributionIntoExisting(existing, candidate))
            {
                if (!MergeCollectiveContribution(existing, candidate, record))
                {
                    RecalculateCaseAfterChargeMutation(
                        caseState,
                        fineDueBeforeMutation,
                        sentenceBeforeMutation,
                        pendingFineBeforeMutation,
                        pendingSentenceBeforeMutation);
                    return null;
                }

                caseState.LastCrimeKind = existing.Kind;
                caseState.LastCrimeLabel = existing.DisplayName;
                existing.IncidentId = candidate.IncidentId;
                // Je garde l'identifiant de charge canonique synchronisé avec le
                // dernier incident collectif fusionné. Le codec v2 exige cette
                // paire exacte pour pouvoir relire puis publier le profil.
                existing.ChargeId = "charge:" + existing.IncidentId;
                existing.ConfirmedAtMs = candidate.ConfirmedAtMs;
                RecalculateCaseAfterChargeMutation(
                    caseState,
                    fineDueBeforeMutation,
                    sentenceBeforeMutation,
                    pendingFineBeforeMutation,
                    pendingSentenceBeforeMutation);
                ResetCleanGameplayProgress(record);
                return existing;
            }

            if (AreDuplicateCharges(existing, candidate) || Supersedes(existing, candidate))
            {
                RecalculateCaseAfterChargeMutation(
                    caseState,
                    fineDueBeforeMutation,
                    sentenceBeforeMutation,
                    pendingFineBeforeMutation,
                    pendingSentenceBeforeMutation);
                return null;
            }

            if (Supersedes(candidate, existing))
            {
                MergeCollectiveContribution(candidate, existing, record);
                caseState.Charges.RemoveAt(index);
            }
        }

        RecalculateChargeSanction(candidate, record);
        CompactActiveCharges(caseState, MaxActiveCharges - 1);
        caseState.Charges.Add(candidate);
        caseState.LastCrimeKind = candidate.Kind;
        caseState.LastCrimeLabel = candidate.DisplayName;
        if (candidate.Kind == JusticeCrimeKind.EvadingPolice)
        {
            RememberUnique(
                caseState.FleeingChargedEpisodeIds,
                candidate.EpisodeId,
                MaxChargedEpisodeIds);
        }
        if (candidate.Kind == JusticeCrimeKind.Escape)
        {
            RememberUnique(
                caseState.EscapeChargedEpisodeIds,
                candidate.EpisodeId,
                MaxChargedEpisodeIds);
        }
        RecalculateCaseAfterChargeMutation(
            caseState,
            fineDueBeforeMutation,
            sentenceBeforeMutation,
            pendingFineBeforeMutation,
            pendingSentenceBeforeMutation);
        ResetCleanGameplayProgress(record);

        if (caseState.WantedEpisodeId.Length == 0)
        {
            // Je conserve un identifiant de dossier pour la déduplication, mais
            // une infraction confirmée ne devient une poursuite que lorsque GTA
            // fournit réellement des étoiles au pont runtime.
            caseState.WantedEpisodeId = candidate.EpisodeId.Length == 0
                ? "wanted:" + candidate.IncidentId
                : candidate.EpisodeId;
        }

        return candidate;
    }

    private static void ApplyConfirmedBatchMultiplicity(
        JusticeCaseState caseState,
        JusticeIncident incident,
        string episodeId,
        JusticeRecordState record)
    {
        incident.Circumstances &= ~JusticeCircumstances.MultipleVictims;
        incident.AdditionalVictimCount = 0;
        if (incident.VictimHandle == 0)
        {
            return;
        }

        int distinctVictims = 1;
        bool currentVictimAlreadyRepresented = false;
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge existing = caseState.Charges[index];
            if (!IsSameConfirmedBatchContributor(existing, incident, episodeId))
            {
                continue;
            }

            if (existing.VictimHandle == incident.VictimHandle &&
                existing.VictimGeneration == incident.VictimGeneration)
            {
                currentVictimAlreadyRepresented = true;
                continue;
            }

            bool alreadyCounted = false;
            for (int previousIndex = 0; previousIndex < index; previousIndex++)
            {
                JusticeCharge previous = caseState.Charges[previousIndex];
                if (IsSameConfirmedBatchContributor(previous, incident, episodeId) &&
                    previous.VictimHandle == existing.VictimHandle &&
                    previous.VictimGeneration == existing.VictimGeneration)
                {
                    alreadyCounted = true;
                    break;
                }
            }

            if (!alreadyCounted)
            {
                distinctVictims++;
            }
        }

        if (currentVictimAlreadyRepresented || distinctVictims <= 1)
        {
            return;
        }

        int additionalVictims = Math.Min(3, distinctVictims - 1);
        incident.AdditionalVictimCount = additionalVictims;
        incident.Circumstances |= JusticeCircumstances.MultipleVictims;

        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge existing = caseState.Charges[index];
            if (!IsSameConfirmedBatchContributor(existing, incident, episodeId))
            {
                continue;
            }

            existing.AdditionalVictimCount = Math.Max(
                existing.AdditionalVictimCount,
                additionalVictims);
            existing.Circumstances |= JusticeCircumstances.MultipleVictims;
            RecalculateChargeSanction(existing, record);
        }
    }

    private static bool IsSameConfirmedBatchContributor(
        JusticeCharge charge,
        JusticeIncident incident,
        string episodeId)
    {
        string batchId = NormalizeIdentifier(incident.DetectionBatchId);
        if (charge == null || charge.IsAggregate || charge.IsAdjudicated || charge.VictimHandle == 0 || batchId.Length == 0 ||
            !string.Equals(charge.DetectionBatchId, batchId, StringComparison.Ordinal) ||
            !string.Equals(charge.EpisodeId, episodeId, StringComparison.Ordinal) ||
            charge.IsAlliedAction != incident.IsAlliedAction)
        {
            return false;
        }

        return !incident.IsAlliedAction ||
               incident.AllyHandle > 0 &&
               charge.HasAlliedContributor(incident.AllyHandle, incident.AllyGeneration);
    }

    internal static JusticeSeverity GetSeverity(int score)
    {
        if (score <= 0) return JusticeSeverity.None;
        if (score <= 9) return JusticeSeverity.Minor;
        if (score <= 24) return JusticeSeverity.Misdemeanor;
        if (score <= 49) return JusticeSeverity.Serious;
        if (score <= 79) return JusticeSeverity.Crime;
        if (score <= 119) return JusticeSeverity.Major;
        return JusticeSeverity.Critical;
    }

    internal static long CalculateRemainingConvictionFine(
        long convictionFine,
        long voluntaryFinePaid)
    {
        long boundedFine = Math.Max(0L, Math.Min(MaxActiveFine, convictionFine));
        long boundedPaid = Math.Max(0L, Math.Min(boundedFine, voluntaryFinePaid));
        return boundedFine - boundedPaid;
    }

    internal static bool CanRebindCustodyRespawnSlot(
        int storedSlot,
        int currentSlot,
        int lastCanonicalSlot,
        int activeProfileSlot,
        bool deathRebindPending)
    {
        if (storedSlot < 0 || storedSlot > 2 || activeProfileSlot != storedSlot)
        {
            return false;
        }
        if (currentSlot >= 0)
        {
            return currentSlot <= 2 && currentSlot == storedSlot;
        }

        // Je n'accepte un ped custom sans slot qu'après une mort observée et
        // seulement si la dernière identité canonique prouve le même héros.
        return currentSlot == -1 && deathRebindPending &&
               lastCanonicalSlot == storedSlot;
    }

    internal static bool CanRebindCustodyFineIntentSlot(
        int currentSlot,
        int intentSlot,
        int storedSlot)
    {
        if (intentSlot < 0 || intentSlot > 2 || storedSlot != intentSlot)
        {
            return false;
        }

        // Le débit reste attaché au profil persistant. Un modèle custom peut
        // masquer temporairement le slot, mais un autre héros connu est refusé.
        return currentSlot == -1 || currentSlot == intentSlot;
    }

    internal static bool ShouldReturnCustodyTransferToCell(JusticePhase phase)
    {
        // Je distingue une première admission d'un retour après la mort ou une
        // reprise : une peine déjà commencée repart toujours de sa cellule.
        return phase == JusticePhase.Incarcerated ||
               phase == JusticePhase.Escaping;
    }

    internal static bool CanUseCustodyUnarmedCombat(
        bool inventoryRemoved,
        bool weaponControlsLocked)
    {
        // Je ne rends les commandes de combat que lorsque la confiscation a été
        // vérifiée. Le verrou de secours continue de bloquer un inventaire dont
        // le retrait a échoué ou dont le snapshot n'est pas fiable.
        return inventoryRemoved && !weaponControlsLocked;
    }

    internal static bool HasFineDebitAttemptTimedOut(long attemptedAtUtcTicks, long nowUtcTicks)
    {
        if (attemptedAtUtcTicks <= 0L)
        {
            // Un v1 sans horodatage a déjà pu émettre l'écriture : je privilégie
            // immédiatement l'at-most-once pour ne jamais redébiter ce joueur.
            return true;
        }
        if (nowUtcTicks <= 0L || attemptedAtUtcTicks > nowUtcTicks)
        {
            // Un recul d'horloge ne doit pas transformer la reprise en soft-lock.
            return attemptedAtUtcTicks > nowUtcTicks;
        }
        return nowUtcTicks - attemptedAtUtcTicks >= FineDebitAmbiguityTimeoutTicks;
    }

    internal static bool HasFineDebitPreparationTimedOut(long preparedAtUtcTicks, long nowUtcTicks)
    {
        if (preparedAtUtcTicks <= 0L)
        {
            return true;
        }
        if (nowUtcTicks <= 0L || preparedAtUtcTicks > nowUtcTicks)
        {
            // Je refuse qu'un recul d'horloge bloque indéfiniment le jugement.
            return preparedAtUtcTicks > nowUtcTicks;
        }
        return nowUtcTicks - preparedAtUtcTicks >= FineDebitAmbiguityTimeoutTicks;
    }

    internal static bool ShouldAcceptDamageFront(
        bool baselineCreated,
        bool baselineWasDamaged,
        bool isDamaged,
        bool hasExplicitRecentSignal)
    {
        if (!isDamaged)
        {
            return false;
        }

        // La première valeur vraie est historique, sauf si un front GTA récent
        // et explicite la rattache à l'acte courant. Ensuite je n'accepte qu'un
        // passage propre de faux à vrai.
        return baselineCreated
            ? hasExplicitRecentSignal
            : !baselineWasDamaged;
    }

    internal static bool ShouldAcceptAttributedDeathFront(
        bool nativeDeathIsFresh,
        bool hasRecentHitPedSignal,
        bool hasActiveDischargeSignal)
    {
        // Je n'appelle ce helper qu'après avoir prouvé le tueur. Le timer de mort
        // reste prioritaire, mais les deux fronts GTA explicites couvrent la frame
        // où un coup létal précède la disponibilité de GET_PED_TIME_OF_DEATH.
        return nativeDeathIsFresh || hasRecentHitPedSignal || hasActiveDischargeSignal;
    }

    internal static bool CanCorrelateWantedRise(
        bool hasPlausibleObserver,
        bool observerStillCredibleAtReport)
    {
        // Une hausse GTA n'invente jamais un fait et ne ressuscite jamais le
        // seul témoin civil mort avant son signalement.
        return hasPlausibleObserver && observerStillCredibleAtReport;
    }

    internal static bool IsWantedCorrelationCandidate(
        long nowMs,
        long incidentCreatedAtMs,
        bool hasPlausibleObserver,
        bool observerStillCredibleAtReport)
    {
        long age = nowMs - incidentCreatedAtMs;
        return age >= 0L && age <= WantedCorrelationWindowMs &&
               CanCorrelateWantedRise(
                   hasPlausibleObserver,
                   observerStillCredibleAtReport);
    }

    internal static bool IsArrestCompletionWithinProbeWindow(
        int millisecondsSinceArrest,
        long pendingProbeElapsedMs)
    {
        if (millisecondsSinceArrest < 0)
        {
            return false;
        }

        long permittedAge = SaturatingAdd(
            2200L,
            Math.Max(0L, pendingProbeElapsedMs),
            int.MaxValue);
        return millisecondsSinceArrest <= permittedAge;
    }

    internal static bool IsVehicleImpactSevere(
        float speedMetersPerSecond,
        bool isTouchingVictim,
        float minimumHostileSpeed)
    {
        return isTouchingVictim && minimumHostileSpeed > 0.0f &&
               speedMetersPerSecond >= minimumHostileSpeed;
    }

    internal static JusticeCircumstances ClassifySelfDefenseResponse(
        bool aggressorArmed,
        bool aggressorUsedVehicle,
        bool responseArmed,
        bool responseUsedVehicle,
        bool responseLethal)
    {
        bool aggressorPresentedLethalThreat = aggressorArmed || aggressorUsedVehicle;
        bool responseEscalatedToVehicle = responseUsedVehicle && !aggressorUsedVehicle;
        bool responseEscalatedToWeapon = responseArmed && !aggressorPresentedLethalThreat;
        bool responseEscalatedToLethal = responseLethal && !aggressorPresentedLethalThreat;

        return responseEscalatedToVehicle || responseEscalatedToWeapon || responseEscalatedToLethal
            ? JusticeCircumstances.ExcessiveSelfDefense
            : JusticeCircumstances.ProportionalSelfDefense;
    }

    internal static bool IsCanonicalPlayerIdentityCompatible(
        int pendingSlot,
        int lastCanonicalSlot,
        int pendingModelHash,
        int currentSlot,
        int currentModelHash)
    {
        if (pendingSlot >= 0)
        {
            return currentSlot == pendingSlot;
        }
        if (lastCanonicalSlot >= 0)
        {
            return currentSlot == lastCanonicalSlot;
        }
        if (currentSlot >= 0)
        {
            return false;
        }

        return pendingModelHash != 0 && currentModelHash == pendingModelHash;
    }

    internal static bool IsPoliceDeathRespawnIdentityCompatible(
        int currentSlot,
        int currentModelHash,
        int ownerSlot,
        int ownerModelHash)
    {
        if (ownerSlot < 0 || ownerSlot > 2 || ownerModelHash == 0 ||
            currentModelHash == 0)
        {
            return false;
        }

        // Je privilégie le slot canonique après un respawn : GTA peut rendre le
        // modèle normal du héros qui est mort sous une tenue custom. Sans slot,
        // je conserve l'exigence stricte du modèle pour ne jamais confondre deux
        // personnages transformés.
        if (currentSlot >= 0 && currentSlot <= 2)
        {
            return currentSlot == ownerSlot;
        }

        return currentSlot == -1 && currentModelHash == ownerModelHash;
    }

    internal static int ResolveTrustedCanonicalPlayerSlot(
        int currentSlot,
        int lastCanonicalSlot)
    {
        // Je préfère toujours le slot prouvé par le ped canonique courant. Pour
        // une tenue custom, je ne réutilise que le dernier slot canonique connu.
        if (currentSlot >= 0 && currentSlot <= 2)
        {
            return currentSlot;
        }

        return lastCanonicalSlot >= 0 && lastCanonicalSlot <= 2
            ? lastCanonicalSlot
            : -1;
    }

    internal static int ResolvePoliceDeathFrontOwnerSlot(
        int currentSlot,
        int activeProfileSlot,
        int lastCanonicalSlot)
    {
        // Je donne la priorité au slot que GTA prouve sur le ped mort. Pendant
        // un changement de héros, il peut déjà différer du profil encore actif.
        if (currentSlot >= 0 && currentSlot <= 2)
        {
            return currentSlot;
        }
        if (activeProfileSlot >= 0 && activeProfileSlot <= 2)
        {
            return activeProfileSlot;
        }
        return ResolveTrustedCanonicalPlayerSlot(currentSlot, lastCanonicalSlot);
    }

    internal static bool IsPoliceDeathFrontAdmissionAllowed(
        bool ownerEnabled,
        bool ownerIsActiveProfile,
        int currentWantedLevel,
        int lastWantedLevel,
        bool pursuitActive,
        bool killedByPolice)
    {
        if (!ownerEnabled)
        {
            return false;
        }

        // Je n'attribue jamais au nouveau héros les latches de poursuite encore
        // attachés à l'ancien profil pendant un changement de personnage. Dans
        // ce cas, seules les étoiles du ped courant ou son tueur policier prouvent
        // le front. Le profil déjà actif conserve la tolérance à la frame où GTA
        // efface les étoiles avant WASTED.
        return currentWantedLevel > 0 || killedByPolice ||
            (ownerIsActiveProfile &&
             (lastWantedLevel > 0 || pursuitActive));
    }

    internal static bool IsDeferredRuntimeFrontLatchOwnerCompatible(
        int latchPlayerSlot,
        int latchPlayerModel,
        int currentPlayerSlot,
        int currentPlayerModel)
    {
        // Je lie chaque arête scalaire au couple slot/modèle qui a produit son
        // état précédent. Le slot interdit P -> Q et le modèle ferme aussi un
        // remplacement de ped non observé pendant une réparation du primaire.
        return latchPlayerSlot >= 0 && latchPlayerSlot <= 2 &&
               currentPlayerSlot == latchPlayerSlot &&
               currentPlayerModel == latchPlayerModel;
    }

    internal static bool IsDeferredArrestFrontAdmissionAllowed(
        bool ownerEnabled,
        bool latchOwnerCompatible,
        bool arrested,
        bool wasBeingArrested,
        bool endedFront)
    {
        if (!ownerEnabled)
        {
            return false;
        }

        // Une fin d'arrestation n'existe que si le même protagoniste portait le
        // latch montant. Le niveau natif arrested=true est en revanche une preuve
        // directe du héros courant : au premier échantillon de Q je peux garder
        // son arrestation sans consulter la valeur précédente appartenant à P.
        return endedFront
            ? latchOwnerCompatible && !arrested && wasBeingArrested
            : arrested && (!latchOwnerCompatible || !wasBeingArrested);
    }

    internal static bool ShouldDeferCustodyFinancialMutationUntilRespawn(
        bool waitingForRespawn,
        bool playerAvailable,
        bool playerDead,
        int expectedSlot,
        int currentSlot,
        bool provenCustomRespawn = false)
    {
        return waitingForRespawn &&
               (!playerAvailable || playerDead || expectedSlot < 0 ||
                (currentSlot != expectedSlot &&
                 !(currentSlot == -1 && provenCustomRespawn)));
    }

    internal static bool IsCustodyDeathIdentityCompatible(
        int storedSlot,
        int currentSlot,
        int storedHandle,
        int currentHandle,
        int storedModelHash,
        int currentModelHash)
    {
        if (currentSlot >= 0)
        {
            return storedSlot >= 0 && currentSlot == storedSlot;
        }

        if (storedSlot >= 0)
        {
            // Sous une tenue custom, le slot n'est plus lisible : je demande
            // alors le même ped déjà lié, par handle et modèle non nul.
            return storedHandle != 0 && currentHandle == storedHandle &&
                   storedModelHash != 0 && currentModelHash == storedModelHash;
        }

        return storedModelHash != 0 && currentModelHash == storedModelHash;
    }

    internal static bool IsCustodyLiveIdentityCompatible(
        int storedSlot,
        int currentSlot,
        int storedHandle,
        int currentHandle,
        int storedModelHash,
        int currentModelHash)
    {
        if (currentSlot >= 0)
        {
            // Le slot d'un héros canonique est la preuve forte et autorise le
            // retour d'une tenue custom vers son modèle de scénario après reload.
            return storedSlot >= 0 && currentSlot == storedSlot;
        }

        // Un ped custom vivant reste prouvé par le handle capturé dans cette
        // session. Après reload, un handle absent ne permet jamais le modèle seul.
        return storedModelHash != 0 && currentModelHash == storedModelHash &&
               storedHandle != 0 && currentHandle == storedHandle;
    }

    internal static bool IsWantedOnlyRepairRecovery(
        bool justiceEnabled,
        bool hasActiveCase,
        bool custodyActive)
    {
        // Je distingue l'annulation hors ligne d'un dossier bloqué d'une vraie
        // amnistie demandée dans F10 : la réparation doit garder Justice active.
        return justiceEnabled && !hasActiveCase && !custodyActive;
    }

    internal static bool TryNormalizePersistedChargeIdentity(
        JusticeCharge charge,
        string fallbackEpisodeId)
    {
        if (charge == null)
        {
            return false;
        }

        string chargeId = NormalizeIdentifier(charge.ChargeId);
        string incidentId = NormalizeIdentifier(charge.IncidentId);
        string episodeId = NormalizeIdentifier(charge.EpisodeId);
        string fallbackEpisode = NormalizeIdentifier(fallbackEpisodeId);
        if (episodeId.Length == 0)
        {
            episodeId = fallbackEpisode;
        }
        if (episodeId.Length == 0)
        {
            return false;
        }

        if (!charge.IsAggregate)
        {
            if (incidentId.Length == 0 && chargeId.StartsWith("charge:", StringComparison.Ordinal) &&
                !chargeId.StartsWith("charge:aggregate:", StringComparison.Ordinal))
            {
                incidentId = chargeId.Substring("charge:".Length);
            }
            if (incidentId.Length == 0)
            {
                return false;
            }

            string canonicalId = "charge:" + incidentId;
            string legacySuffix = incidentId.StartsWith("incident:", StringComparison.Ordinal)
                ? incidentId.Substring("incident:".Length)
                : incidentId;
            string legacyV1Id = "charge:" + legacySuffix;
            if (chargeId.Length > 0 &&
                !string.Equals(chargeId, canonicalId, StringComparison.Ordinal) &&
                !string.Equals(chargeId, legacyV1Id, StringComparison.Ordinal))
            {
                return false;
            }
            if (charge.AggregatedChargeCount != 0)
            {
                return false;
            }

            charge.ChargeId = canonicalId;
            charge.IncidentId = incidentId;
            charge.EpisodeId = episodeId;
            return true;
        }

        if (charge.AggregatedChargeCount <= 0)
        {
            return false;
        }
        const string pendingLegacyId = "charge:aggregate:pending";
        const string adjudicatedLegacyId = "charge:aggregate:adjudicated";
        if (incidentId.Length == 0)
        {
            string pendingPrefix = pendingLegacyId + ":";
            string adjudicatedPrefix = adjudicatedLegacyId + ":";
            if (chargeId.StartsWith(pendingPrefix, StringComparison.Ordinal))
            {
                incidentId = chargeId.Substring(pendingPrefix.Length);
            }
            else if (chargeId.StartsWith(adjudicatedPrefix, StringComparison.Ordinal))
            {
                incidentId = chargeId.Substring(adjudicatedPrefix.Length);
            }
        }
        if (incidentId.Length == 0)
        {
            return false;
        }

        string pendingCanonicalId = pendingLegacyId + ":" + incidentId;
        string adjudicatedCanonicalId = adjudicatedLegacyId + ":" + incidentId;
        bool recognizedLegacyOrCanonical = chargeId.Length == 0 ||
            string.Equals(chargeId, pendingLegacyId, StringComparison.Ordinal) ||
            string.Equals(chargeId, adjudicatedLegacyId, StringComparison.Ordinal) ||
            string.Equals(chargeId, pendingCanonicalId, StringComparison.Ordinal) ||
            string.Equals(chargeId, adjudicatedCanonicalId, StringComparison.Ordinal);
        if (!recognizedLegacyOrCanonical)
        {
            return false;
        }

        charge.ChargeId = charge.IsAdjudicated
            ? adjudicatedCanonicalId
            : pendingCanonicalId;
        charge.IncidentId = incidentId;
        charge.EpisodeId = episodeId;
        return true;
    }

    internal static int CompareIncidentResolutionPriority(
        JusticeIncident left,
        JusticeIncident right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }
        if (left == null)
        {
            return -1;
        }
        if (right == null)
        {
            return 1;
        }

        if (DoesConfirmedViolenceSupersedeRecklessDischarge(left, right) ||
            DoesConfirmedViolenceSupersedeRecklessDischarge(right, left))
        {
            bool leftViolence = IsDirectVictimViolence(left.Kind);
            bool rightViolence = IsDirectVictimViolence(right.Kind);
            if (leftViolence != rightViolence)
            {
                return leftViolence ? 1 : -1;
            }
        }

        int createdComparison = left.CreatedAtMs.CompareTo(right.CreatedAtMs);
        if (createdComparison != 0)
        {
            return createdComparison;
        }

        int pointsComparison = GetDefinition(left.Kind).BasePoints.CompareTo(
            GetDefinition(right.Kind).BasePoints);
        if (pointsComparison != 0)
        {
            return pointsComparison;
        }

        bool leftHasVictim = left.VictimHandle != 0;
        bool rightHasVictim = right.VictimHandle != 0;
        if (leftHasVictim != rightHasVictim)
        {
            return leftHasVictim ? 1 : -1;
        }

        return -string.Compare(
            left.IncidentId ?? string.Empty,
            right.IncidentId ?? string.Empty,
            StringComparison.Ordinal);
    }

    internal static bool DoesConfirmedViolenceSupersedeRecklessDischarge(
        JusticeIncident violence,
        JusticeIncident reckless)
    {
        return violence != null && reckless != null &&
               IsDirectVictimViolence(violence.Kind) &&
               reckless.Kind == JusticeCrimeKind.RecklessDischarge &&
               !string.IsNullOrWhiteSpace(violence.CausalEventId) &&
               string.Equals(
                   violence.CausalEventId,
                   reckless.CausalEventId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   violence.EpisodeId,
                   reckless.EpisodeId,
                   StringComparison.Ordinal);
    }

    internal static JusticeConviction ApplyConviction(
        JusticeCaseState caseState,
        JusticeRecordState record,
        DateTime judgedAtUtc)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        if (record == null)
        {
            throw new ArgumentNullException("record");
        }

        string episode = ResolveConvictionEpisodeId(caseState);
        if (episode.Length == 0)
        {
            return null;
        }

        string convictionId = "conviction:" + episode;
        for (int index = 0; index < record.Convictions.Count; index++)
        {
            JusticeConviction existing = record.Convictions[index];
            if (existing != null && string.Equals(
                existing.ConvictionId,
                convictionId,
                StringComparison.Ordinal))
            {
                return existing;
            }
        }

        // Je conserve aussi les identifiants sortis de l'historique visible de
        // vingt condamnations afin qu'une reprise ancienne ne réaugmente jamais R.
        if (ContainsOrdinal(record.AppliedConvictionIds, convictionId))
        {
            return null;
        }

        long judgmentScore = 0L;
        long judgmentFine = 0L;
        long judgmentSentence = 0L;
        int eligibleChargeCount = 0;
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge == null || charge.IsAdjudicated)
            {
                continue;
            }

            judgmentScore = SaturatingAdd(judgmentScore, Math.Max(0, charge.Points), MaxActiveScore);
            judgmentFine = SaturatingAdd(judgmentFine, Math.Max(0L, charge.Fine), MaxActiveFine);
            judgmentSentence = SaturatingAdd(
                judgmentSentence,
                Math.Max(0, charge.SentenceSeconds),
                MaxActiveSentenceSeconds);
            eligibleChargeCount++;
        }

        JusticeSeverity severity = GetSeverity((int)judgmentScore);
        if (severity == JusticeSeverity.None || eligibleChargeCount == 0)
        {
            return null;
        }

        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = convictionId,
            JudgedAtUtc = judgedAtUtc.ToUniversalTime(),
            Severity = severity,
            Score = (int)judgmentScore,
            Fine = judgmentFine,
            SentenceSeconds = (int)judgmentSentence
        };

        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge == null || charge.IsAdjudicated)
            {
                continue;
            }

            conviction.Charges.Add(new JusticeConvictionChargeSummary
            {
                Kind = charge.Kind,
                DisplayName = charge.DisplayName ?? string.Empty,
                Points = Math.Max(0, charge.Points),
                Fine = Math.Max(0L, charge.Fine),
                SentenceSeconds = Math.Max(0, charge.SentenceSeconds),
                Circumstances = charge.Circumstances,
                IsAggregate = charge.IsAggregate,
                AggregatedChargeCount = charge.IsAggregate
                    ? Math.Max(1, charge.AggregatedChargeCount)
                    : 0
            });
        }

        if (!string.IsNullOrWhiteSpace(caseState.CustodyEpisodeId) &&
            caseState.CustodyEpisodeId.IndexOf(":discipline:", StringComparison.Ordinal) < 0)
        {
            record.PinnedConvictionId = convictionId;
        }
        RememberAppliedConvictionId(record, convictionId);
        record.Convictions.Add(conviction);
        TrimVisibleConvictions(record);
        record.MarkLedgerChanged();

        record.RecidivismIndex = Clamp(
            record.RecidivismIndex + GetConvictionRecidivismIncrease(severity),
            0,
            100);
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge != null && !charge.IsAdjudicated)
            {
                charge.IsAdjudicated = true;
            }
        }
        record.CleanGameplaySeconds = 0;
        record.AppliedCleanDecay = 0;
        return conviction;
    }

    internal static int AdvanceCleanTime(JusticeRecordState record, int elapsedSeconds, bool eligible)
    {
        if (record == null)
        {
            throw new ArgumentNullException("record");
        }

        if (!eligible || elapsedSeconds <= 0 || record.RecidivismIndex <= 0)
        {
            return 0;
        }

        record.CleanGameplaySeconds = SaturatingAddInt(record.CleanGameplaySeconds, elapsedSeconds);
        int targetDecay = CalculateCleanDecay(record.CleanGameplaySeconds);
        int newDecay = Math.Max(0, targetDecay - Math.Max(0, record.AppliedCleanDecay));
        int applied = Math.Min(record.RecidivismIndex, newDecay);
        record.RecidivismIndex -= applied;
        record.AppliedCleanDecay = targetDecay;
        return applied;
    }

    internal static JusticeTransition Transition(JusticeCaseState caseState, JusticeTickInput input)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        if (input == null)
        {
            throw new ArgumentNullException("input");
        }

        JusticePhase previous = caseState.Phase;
        JusticePhase next = previous;
        JusticeOperationKind operationKind = JusticeOperationKind.None;
        JusticeSignal signals = input.Signals;

        if (!caseState.Enabled)
        {
            return new JusticeTransition(previous, previous, null);
        }

        switch (previous)
        {
            case JusticePhase.AtLarge:
                // Le domaine enregistre le dossier, mais ne fabrique jamais une
                // poursuite à partir d'une charge ou d'une reconnaissance. Le
                // niveau wanted vanilla observé reste l'unique autorité.
                break;

            case JusticePhase.Wanted:
            case JusticePhase.Fugitive:
                if (HasAny(signals, JusticeSignal.ArrestCompleted | JusticeSignal.PlayerDiedDuringPolicePursuit))
                {
                    next = JusticePhase.Captured;
                    operationKind = JusticeOperationKind.Capture;
                }
                else if (HasAny(signals, JusticeSignal.ArrestStarted))
                {
                    next = JusticePhase.Surrendering;
                }
                break;

            case JusticePhase.Surrendering:
                if (HasAny(signals, JusticeSignal.ArrestCompleted | JusticeSignal.PlayerDiedDuringPolicePursuit))
                {
                    next = JusticePhase.Captured;
                    operationKind = JusticeOperationKind.Capture;
                }
                else if (HasAny(signals, JusticeSignal.ArrestCancelled))
                {
                    next = JusticePhase.Wanted;
                }
                break;

            case JusticePhase.Captured:
                if (HasAny(signals, JusticeSignal.TransferReady))
                {
                    next = JusticePhase.Transporting;
                    operationKind = JusticeOperationKind.Transport;
                }
                break;

            case JusticePhase.Transporting:
                if (HasAny(signals, JusticeSignal.TransferCompleted | JusticeSignal.TransferTimedOut))
                {
                    next = JusticePhase.Incarcerated;
                    operationKind = JusticeOperationKind.EnterCustody;
                }
                break;

            case JusticePhase.Incarcerated:
                if (HasAny(signals, JusticeSignal.SentenceCompleted))
                {
                    next = JusticePhase.AtLarge;
                    operationKind = JusticeOperationKind.Release;
                }
                else if (HasAny(signals, JusticeSignal.LeftCustody))
                {
                    next = JusticePhase.Escaping;
                }
                break;

            case JusticePhase.Escaping:
                if (HasAny(signals, JusticeSignal.Restrained))
                {
                    next = JusticePhase.Incarcerated;
                }
                else if (HasAny(signals, JusticeSignal.EscapeConfirmed))
                {
                    next = JusticePhase.Fugitive;
                    operationKind = JusticeOperationKind.RegisterEscape;
                    caseState.HasWarrant = true;
                }
                break;
        }

        caseState.Phase = next;
        JusticeOperation operation = null;
        if (operationKind != JusticeOperationKind.None)
        {
            string episode = NormalizeIdentifier(input.EpisodeId);
            if (episode.Length == 0)
            {
                episode = NormalizeIdentifier(caseState.CustodyEpisodeId);
            }
            if (episode.Length == 0)
            {
                episode = NormalizeIdentifier(caseState.WantedEpisodeId);
            }

            operation = new JusticeOperation(CreateOperationId(operationKind, episode), operationKind, episode);
        }

        return new JusticeTransition(previous, next, operation);
    }

    internal static bool TryRegisterOperation(JusticeCaseState caseState, JusticeOperation operation)
    {
        if (caseState == null || operation == null)
        {
            return false;
        }

        string episodeId = NormalizeIdentifier(operation.EpisodeId);
        string operationId = NormalizeIdentifier(operation.OperationId);
        string canonicalId = CreateOperationId(operation.Kind, episodeId);
        if (operation.Kind == JusticeOperationKind.None ||
            operation.Kind == JusticeOperationKind.ApplyWantedFloor ||
            episodeId.Length == 0 ||
            operationId.Length == 0 ||
            !string.Equals(operationId, canonicalId, StringComparison.Ordinal) ||
            ContainsOrdinal(caseState.CompletedOperationIds, operationId))
        {
            return false;
        }

        caseState.CompletedOperationIds.Add(operationId);
        return true;
    }

    internal static void PruneClosedCustodyOperations(
        JusticeCaseState caseState,
        string custodyEpisodeId)
    {
        if (caseState == null)
        {
            return;
        }

        string closedEpisode = NormalizeIdentifier(custodyEpisodeId);
        if (closedEpisode.Length == 0)
        {
            return;
        }

        for (int index = caseState.CompletedOperationIds.Count - 1; index >= 0; index--)
        {
            string operationId = caseState.CompletedOperationIds[index] ?? string.Empty;
            int separatorIndex = operationId.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= operationId.Length - 1)
            {
                continue;
            }

            string operationEpisode = operationId.Substring(separatorIndex + 1);
            if (string.Equals(operationEpisode, closedEpisode, StringComparison.Ordinal) ||
                operationEpisode.StartsWith(closedEpisode + ":fine:", StringComparison.Ordinal))
            {
                caseState.CompletedOperationIds.RemoveAt(index);
            }
        }
    }

    internal static string CreateOperationId(JusticeOperationKind kind, string episodeId)
    {
        string normalizedEpisode = NormalizeIdentifier(episodeId);
        if (kind == JusticeOperationKind.None || normalizedEpisode.Length == 0)
        {
            return string.Empty;
        }

        return kind.ToString() + ":" + normalizedEpisode;
    }

    internal static bool ContainsEpisodeId(IList<string> episodeIds, string episodeId)
    {
        if (episodeIds == null)
        {
            return false;
        }

        string normalizedEpisode = NormalizeIdentifier(episodeId);
        return normalizedEpisode.Length > 0 && ContainsOrdinal(episodeIds, normalizedEpisode);
    }

    internal static long SaturatingAdd(long current, long addition, long maximum)
    {
        if (current < 0L) current = 0L;
        if (addition <= 0L) return Math.Min(maximum, current);
        if (current >= maximum || addition > maximum - current) return maximum;
        return current + addition;
    }

    internal static void EnforceActiveChargeLimit(JusticeCaseState caseState)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        CompactActiveCharges(caseState, MaxActiveCharges);
    }

    private static void CompactActiveCharges(JusticeCaseState caseState, int maximum)
    {
        int boundedMaximum = Math.Max(1, maximum);
        for (int index = caseState.Charges.Count - 1; index >= 0; index--)
        {
            if (caseState.Charges[index] == null)
            {
                caseState.Charges.RemoveAt(index);
            }
        }

        while (caseState.Charges.Count > boundedMaximum)
        {
            if (!TryCompactOneActiveCharge(caseState))
            {
                throw new InvalidOperationException("Impossible de borner les charges actives Justice.");
            }
        }
    }

    private static bool TryCompactOneActiveCharge(JusticeCaseState caseState)
    {
        // Je réutilise d'abord un agrégat du même statut judiciaire afin de
        // libérer exactement un emplacement sans mélanger jugé et non jugé.
        for (int aggregateIndex = 0; aggregateIndex < caseState.Charges.Count; aggregateIndex++)
        {
            JusticeCharge aggregate = caseState.Charges[aggregateIndex];
            if (aggregate == null || !aggregate.IsAggregate)
            {
                continue;
            }

            for (int sourceIndex = 0; sourceIndex < caseState.Charges.Count; sourceIndex++)
            {
                JusticeCharge source = caseState.Charges[sourceIndex];
                if (sourceIndex == aggregateIndex || source == null ||
                    source.IsAdjudicated != aggregate.IsAdjudicated)
                {
                    continue;
                }

                MergeChargeIntoAggregate(aggregate, source);
                caseState.Charges.RemoveAt(sourceIndex);
                return true;
            }
        }

        // Sans agrégat compatible, je consolide les deux plus anciennes
        // charges partageant le même statut. Avec plus de 511 entrées, cette
        // paire existe toujours puisque le statut est binaire.
        for (int firstIndex = 0; firstIndex < caseState.Charges.Count; firstIndex++)
        {
            JusticeCharge first = caseState.Charges[firstIndex];
            if (first == null || first.IsAggregate)
            {
                continue;
            }

            for (int secondIndex = firstIndex + 1; secondIndex < caseState.Charges.Count; secondIndex++)
            {
                JusticeCharge second = caseState.Charges[secondIndex];
                if (second == null || second.IsAggregate ||
                    second.IsAdjudicated != first.IsAdjudicated)
                {
                    continue;
                }

                JusticeCharge aggregate = CreateChargeAggregate(first, second);
                caseState.Charges[firstIndex] = aggregate;
                caseState.Charges.RemoveAt(secondIndex);
                return true;
            }
        }

        return false;
    }

    private static JusticeCharge CreateChargeAggregate(JusticeCharge first, JusticeCharge second)
    {
        JusticeCharge aggregate = new JusticeCharge
        {
            ChargeId = first.IsAdjudicated
                ? "charge:aggregate:adjudicated"
                : "charge:aggregate:pending",
            IncidentId = first.IncidentId ?? string.Empty,
            EpisodeId = first.EpisodeId ?? string.Empty,
            Kind = JusticeCrimeKind.ReportedViolentAct,
            DisplayName = "Infractions consolidées",
            ConfirmedAtMs = Math.Max(0L, first.ConfirmedAtMs),
            IsAdjudicated = first.IsAdjudicated,
            IsAggregate = true,
            AggregatedChargeCount = GetRepresentedChargeCount(first)
        };
        aggregate.Points = (int)SaturatingAdd(0L, Math.Max(0, first.Points), MaxActiveScore);
        aggregate.Fine = SaturatingAdd(0L, Math.Max(0L, first.Fine), MaxActiveFine);
        aggregate.SentenceSeconds = (int)SaturatingAdd(
            0L,
            Math.Max(0, first.SentenceSeconds),
            MaxActiveSentenceSeconds);
        MergeChargeIntoAggregate(aggregate, second);
        return aggregate;
    }

    private static void MergeChargeIntoAggregate(JusticeCharge aggregate, JusticeCharge source)
    {
        if (aggregate == null || source == null || !aggregate.IsAggregate ||
            aggregate.IsAdjudicated != source.IsAdjudicated)
        {
            throw new InvalidOperationException("Agrégat de charges Justice incompatible.");
        }

        aggregate.Points = (int)SaturatingAdd(
            aggregate.Points,
            Math.Max(0, source.Points),
            MaxActiveScore);
        aggregate.Fine = SaturatingAdd(
            aggregate.Fine,
            Math.Max(0L, source.Fine),
            MaxActiveFine);
        aggregate.SentenceSeconds = (int)SaturatingAdd(
            aggregate.SentenceSeconds,
            Math.Max(0, source.SentenceSeconds),
            MaxActiveSentenceSeconds);
        aggregate.AggregatedChargeCount = SaturatingAddInt(
            GetRepresentedChargeCount(aggregate),
            GetRepresentedChargeCount(source));
        aggregate.ConfirmedAtMs = Math.Max(aggregate.ConfirmedAtMs, source.ConfirmedAtMs);
        if (string.IsNullOrWhiteSpace(aggregate.EpisodeId))
        {
            aggregate.EpisodeId = source.EpisodeId ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(aggregate.IncidentId))
        {
            aggregate.IncidentId = source.IncidentId ?? string.Empty;
        }

        aggregate.DetectionBatchId = string.Empty;
        aggregate.CausalEventId = string.Empty;
        aggregate.VictimHandle = 0;
        aggregate.VictimGeneration = 0;
        aggregate.IsAlliedAction = false;
        aggregate.Circumstances = JusticeCircumstances.None;
        aggregate.AdditionalVictimCount = 0;
        aggregate.AlliedContributorHandles.Clear();
        aggregate.AlliedContributors.Clear();
    }

    internal static int GetRepresentedChargeCount(JusticeCaseState caseState)
    {
        if (caseState == null)
        {
            return 0;
        }

        int represented = 0;
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge != null)
            {
                represented = SaturatingAddInt(represented, GetRepresentedChargeCount(charge));
            }
        }

        return represented;
    }

    internal static int GetRepresentedChargeCount(JusticeCharge charge)
    {
        return charge != null && charge.IsAggregate
            ? Math.Max(1, charge.AggregatedChargeCount)
            : 1;
    }

    private static Dictionary<JusticeCrimeKind, JusticeCrimeDefinition> CreateCatalog()
    {
        Dictionary<JusticeCrimeKind, JusticeCrimeDefinition> catalog =
            new Dictionary<JusticeCrimeKind, JusticeCrimeDefinition>();

        AddDefinition(catalog, JusticeCrimeKind.ReportedViolentAct, "Acte violent signalé", 5, 250L, 0);
        AddDefinition(catalog, JusticeCrimeKind.RecklessDischarge, "Tir dangereux sans victime", 6, 300L, 0);
        AddDefinition(catalog, JusticeCrimeKind.VehicleDamage, "Dégradation volontaire de véhicule", 8, 500L, 0);
        AddDefinition(catalog, JusticeCrimeKind.ArmedThreat, "Menace armée soutenue", 10, 600L, 0);
        AddDefinition(catalog, JusticeCrimeKind.VehicleTheft, "Vol de véhicule vide", 12, 750L, 0);
        AddDefinition(catalog, JusticeCrimeKind.VehicleDestruction, "Destruction volontaire de véhicule", 18, 1250L, 20);
        AddDefinition(catalog, JusticeCrimeKind.SimpleAssault, "Agression simple", 18, 1000L, 30);
        AddDefinition(catalog, JusticeCrimeKind.HitAndRun, "Délit de fuite après blessure", 18, 1200L, 30);
        AddDefinition(catalog, JusticeCrimeKind.EvadingPolice, "Refus d'obtempérer", 20, 1500L, 40);
        AddDefinition(catalog, JusticeCrimeKind.AccessoryAssaultOfficer, "Complicité d'agression sur agent", 22, 2000L, 40);
        AddDefinition(catalog, JusticeCrimeKind.Carjacking, "Carjacking", 24, 1750L, 40);
        AddDefinition(catalog, JusticeCrimeKind.ResistingArrest, "Résistance à une arrestation", 30, 2500L, 60);
        AddDefinition(catalog, JusticeCrimeKind.AggravatedAssault, "Agression aggravée", 34, 3000L, 80);
        AddDefinition(catalog, JusticeCrimeKind.AssaultOfficer, "Agression sur policier ou gardien", 48, 5000L, 120);
        AddDefinition(catalog, JusticeCrimeKind.AccessoryMurderOfficer, "Complicité d'homicide sur agent", 52, 7500L, 140);
        AddDefinition(catalog, JusticeCrimeKind.Manslaughter, "Homicide involontaire", 55, 6000L, 160);
        AddDefinition(catalog, JusticeCrimeKind.MurderCivilian, "Meurtre d'un civil", 75, 10000L, 240);
        AddDefinition(catalog, JusticeCrimeKind.Escape, "Évasion", 90, 10000L, 300);
        AddDefinition(catalog, JusticeCrimeKind.MurderOfficer, "Meurtre d'un policier ou gardien", 100, 15000L, 360);

        return catalog;
    }

    private static void AddDefinition(
        IDictionary<JusticeCrimeKind, JusticeCrimeDefinition> catalog,
        JusticeCrimeKind kind,
        string displayName,
        int points,
        long fine,
        int sentenceSeconds)
    {
        catalog.Add(kind, new JusticeCrimeDefinition(kind, displayName, points, fine, sentenceSeconds));
    }

    private static int CalculateCircumstanceBasisPoints(
        JusticeCircumstances circumstances,
        int additionalVictimCount)
    {
        int basisPoints = BasisPointScale;

        if ((circumstances & JusticeCircumstances.Armed) != 0) basisPoints += 1500;
        if ((circumstances & JusticeCircumstances.ExplosiveOrIncendiary) != 0) basisPoints += 3000;
        if ((circumstances & JusticeCircumstances.ActiveWarrant) != 0) basisPoints += 1500;
        if ((circumstances & JusticeCircumstances.InCustody) != 0) basisPoints += 2500;

        int additionalVictims = Math.Max(0, additionalVictimCount);
        if (additionalVictims > 0 || (circumstances & JusticeCircumstances.MultipleVictims) != 0)
        {
            basisPoints += additionalVictims >= 2 ? 2000 : 1000;
        }

        if ((circumstances & JusticeCircumstances.OrganizedBand) != 0)
        {
            basisPoints += 2500;
        }
        else if ((circumstances & JusticeCircumstances.GroupCrime) != 0)
        {
            basisPoints += 1000;
        }

        if ((circumstances & JusticeCircumstances.ExcessiveSelfDefense) != 0)
        {
            basisPoints -= 4000;
        }

        return Clamp(basisPoints, MinimumCircumstanceBasisPoints, MaximumCircumstanceBasisPoints);
    }

    private static JusticeCircumstances NormalizeCollectiveCircumstances(JusticeCircumstances circumstances)
    {
        if ((circumstances & JusticeCircumstances.OrganizedBand) != 0)
        {
            circumstances &= ~JusticeCircumstances.GroupCrime;
        }

        if ((circumstances & JusticeCircumstances.ProportionalSelfDefense) != 0)
        {
            circumstances &= ~JusticeCircumstances.ExcessiveSelfDefense;
        }

        return circumstances;
    }

    private static JusticeCircumstances NormalizeCircumstancesForCrime(
        JusticeCrimeKind kind,
        JusticeCircumstances circumstances)
    {
        circumstances = NormalizeCollectiveCircumstances(circumstances);
        if (!AllowsSelfDefense(kind))
        {
            circumstances &= ~JusticeCircumstances.ProportionalSelfDefense;
            circumstances &= ~JusticeCircumstances.ExcessiveSelfDefense;
        }

        return circumstances;
    }

    private static bool AllowsSelfDefense(JusticeCrimeKind kind)
    {
        // Je limite la légitime défense aux violences contre une victime civile.
        // Les agents, la résistance, la fuite et les infractions patrimoniales en
        // restent toujours exclus, même si le runtime reçoit un signal incohérent.
        return kind == JusticeCrimeKind.ReportedViolentAct ||
               kind == JusticeCrimeKind.SimpleAssault ||
               kind == JusticeCrimeKind.AggravatedAssault ||
               kind == JusticeCrimeKind.Manslaughter ||
               kind == JusticeCrimeKind.MurderCivilian;
    }

    private static bool IsSameVictimEpisode(JusticeCharge left, JusticeCharge right)
    {
        if (!string.Equals(left.EpisodeId, right.EpisodeId, StringComparison.Ordinal))
        {
            return false;
        }

        // Je garde la génération avec le handle pour empêcher qu'un nouveau ped
        // hérite de la charge d'une entité que GTA a déjà recyclée.
        if (left.VictimHandle != 0 || right.VictimHandle != 0)
        {
            return left.VictimHandle == right.VictimHandle &&
                   (left.VictimGeneration == right.VictimGeneration ||
                    left.VictimGeneration == 0 ||
                    right.VictimGeneration == 0);
        }

        // Sans victime, l'épisode et la qualification forment la clé stable :
        // plusieurs tirs détectés séparément ne doivent créer qu'une charge.
        if (left.EpisodeId.Length > 0)
        {
            return true;
        }

        return string.Equals(left.IncidentId, right.IncidentId, StringComparison.Ordinal);
    }

    private static void MigrateLegacyVictimGeneration(JusticeCharge existing, JusticeCharge candidate)
    {
        if (existing == null || candidate == null || existing.VictimHandle <= 0 ||
            existing.VictimHandle != candidate.VictimHandle)
        {
            return;
        }

        if (existing.VictimGeneration == 0 && candidate.VictimGeneration > 0)
        {
            // Je remplace la génération absente des sauvegardes antérieures dès
            // la première observation fiable pour ne garder le wildcard qu'une fois.
            existing.VictimGeneration = candidate.VictimGeneration;
        }
        else if (candidate.VictimGeneration == 0 && existing.VictimGeneration > 0)
        {
            candidate.VictimGeneration = existing.VictimGeneration;
        }
    }

    internal static long CalculatePendingFine(JusticeCaseState caseState)
    {
        long total = 0L;
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge != null && !charge.IsAdjudicated)
            {
                total = SaturatingAdd(total, Math.Max(0L, charge.Fine), MaxActiveFine);
            }
        }

        return total;
    }

    internal static int CalculatePendingSentence(JusticeCaseState caseState)
    {
        long total = 0L;
        for (int index = 0; index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge != null && !charge.IsAdjudicated)
            {
                total = SaturatingAdd(
                    total,
                    Math.Max(0, charge.SentenceSeconds),
                    MaxActiveSentenceSeconds);
            }
        }

        return (int)Math.Min(MaxActiveSentenceSeconds, total);
    }

    internal static long MoveFineToDispute(JusticeCaseState caseState, long requestedAmount)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        NormalizeFineLedger(caseState);
        long availableCapacity = Math.Max(
            0L,
            MaxActiveFine - caseState.VoluntaryFinePaid - caseState.FineInDispute);
        long moved = Math.Min(
            Math.Max(0L, requestedAmount),
            Math.Min(caseState.FineDue, availableCapacity));
        caseState.FineDue -= moved;
        caseState.FineInDispute += moved;
        return moved;
    }

    internal static void NormalizeFineLedger(JusticeCaseState caseState)
    {
        if (caseState == null)
        {
            throw new ArgumentNullException("caseState");
        }

        caseState.VoluntaryFinePaid = Math.Max(
            0L,
            Math.Min(MaxActiveFine, caseState.VoluntaryFinePaid));
        caseState.FineInDispute = Math.Max(
            0L,
            Math.Min(
                MaxActiveFine - caseState.VoluntaryFinePaid,
                caseState.FineInDispute));
        caseState.FineDue = Math.Max(
            0L,
            Math.Min(
                MaxActiveFine - caseState.VoluntaryFinePaid - caseState.FineInDispute,
                caseState.FineDue));
    }

    internal static bool IsFineLedgerValid(JusticeCaseState caseState)
    {
        if (caseState == null || caseState.FineDue < 0L ||
            caseState.FineDue > MaxActiveFine || caseState.VoluntaryFinePaid < 0L ||
            caseState.VoluntaryFinePaid > MaxActiveFine || caseState.FineInDispute < 0L ||
            caseState.FineInDispute > MaxActiveFine)
        {
            return false;
        }

        return caseState.VoluntaryFinePaid <= MaxActiveFine - caseState.FineInDispute &&
               caseState.FineDue <= MaxActiveFine -
                   caseState.VoluntaryFinePaid - caseState.FineInDispute;
    }

    private static void RecalculateCaseAfterChargeMutation(
        JusticeCaseState caseState,
        long fineDueBeforeMutation,
        int sentenceBeforeMutation,
        long pendingFineBeforeMutation,
        int pendingSentenceBeforeMutation)
    {
        long pendingFineAfterMutation = CalculatePendingFine(caseState);
        int pendingSentenceAfterMutation = CalculatePendingSentence(caseState);

        // Je recalcule le score sur tout le dossier, mais je traite amende et
        // détention comme un solde : seules les charges non encore jugées ajoutent
        // leur delta. Une condamnation déjà payée ou purgée ne peut donc renaître.
        caseState.RecalculateTotals();
        long updatedFine = fineDueBeforeMutation +
            (pendingFineAfterMutation - pendingFineBeforeMutation);
        long updatedSentence = (long)sentenceBeforeMutation +
            (pendingSentenceAfterMutation - pendingSentenceBeforeMutation);
        caseState.FineDue = Math.Max(0L, Math.Min(MaxActiveFine, updatedFine));
        caseState.SentenceSeconds = (int)Math.Max(
            0L,
            Math.Min(MaxActiveSentenceSeconds, updatedSentence));
    }

    private static bool ShouldMergeCollectiveContributionIntoExisting(
        JusticeCharge existing,
        JusticeCharge candidate)
    {
        if (existing.IsAlliedAction && candidate.IsAlliedAction && existing.Kind == candidate.Kind)
        {
            return true;
        }

        return !existing.IsAlliedAction && candidate.IsAlliedAction &&
               CanDirectCrimeReplaceAccessory(existing.Kind, candidate.Kind);
    }

    private static bool MergeCollectiveContribution(
        JusticeCharge target,
        JusticeCharge source,
        JusticeRecordState record)
    {
        bool changed = false;
        JusticeCircumstances previousCircumstances = target.Circumstances;
        source.ImportLegacyAlliedContributorHandles();
        target.ImportLegacyAlliedContributorHandles();
        for (int index = 0; index < source.AlliedContributors.Count; index++)
        {
            JusticeEntityIdentity identity = source.AlliedContributors[index];
            if (identity.Handle > 0 &&
                target.AlliedContributors.Count < MaxAlliedContributorsPerCharge &&
                !target.AlliedContributors.Contains(identity))
            {
                target.AddAlliedContributor(identity.Handle, identity.Generation);
                changed = true;
            }
        }

        JusticeCircumstances previousCollective = target.Circumstances &
            (JusticeCircumstances.GroupCrime | JusticeCircumstances.OrganizedBand);
        JusticeCircumstances sourceCollective = source.Circumstances &
            (JusticeCircumstances.GroupCrime | JusticeCircumstances.OrganizedBand);
        if ((sourceCollective & JusticeCircumstances.OrganizedBand) != 0)
        {
            target.Circumstances |= JusticeCircumstances.OrganizedBand;
        }
        else if ((sourceCollective & JusticeCircumstances.GroupCrime) != 0)
        {
            target.Circumstances |= JusticeCircumstances.GroupCrime;
        }

        NormalizeChargeCollectiveCircumstance(target);
        JusticeCircumstances currentCollective = target.Circumstances &
            (JusticeCircumstances.GroupCrime | JusticeCircumstances.OrganizedBand);
        changed |= previousCollective != currentCollective;
        int previousAdditionalVictims = target.AdditionalVictimCount;
        target.AdditionalVictimCount = Math.Max(
            target.AdditionalVictimCount,
            source.AdditionalVictimCount);
        if (target.AdditionalVictimCount > 0 ||
            (source.Circumstances & JusticeCircumstances.MultipleVictims) != 0)
        {
            target.Circumstances |= JusticeCircumstances.MultipleVictims;
        }
        changed |= previousAdditionalVictims != target.AdditionalVictimCount;
        changed |= previousCircumstances != target.Circumstances;

        if (changed)
        {
            RecalculateChargeSanction(target, record);
        }

        return changed;
    }

    private static void AddAlliedContributor(
        JusticeCharge charge,
        int allyHandle,
        int allyGeneration)
    {
        if (charge == null || !charge.IsAlliedAction)
        {
            return;
        }

        charge.ImportLegacyAlliedContributorHandles();
        if (allyHandle > 0 &&
            charge.AlliedContributors.Count < MaxAlliedContributorsPerCharge)
        {
            charge.AddAlliedContributor(allyHandle, allyGeneration);
        }

        if ((charge.Circumstances & JusticeCircumstances.OrganizedBand) == 0)
        {
            charge.Circumstances |= JusticeCircumstances.GroupCrime;
        }
    }

    private static void NormalizeChargeCollectiveCircumstance(JusticeCharge charge)
    {
        charge.ImportLegacyAlliedContributorHandles();
        if (charge.AlliedContributors.Count >= 2 ||
            (charge.Circumstances & JusticeCircumstances.OrganizedBand) != 0)
        {
            charge.Circumstances |= JusticeCircumstances.OrganizedBand;
            charge.Circumstances &= ~JusticeCircumstances.GroupCrime;
        }
        else if (charge.IsAlliedAction || charge.AlliedContributors.Count == 1)
        {
            charge.Circumstances |= JusticeCircumstances.GroupCrime;
        }

        charge.Circumstances = NormalizeCircumstancesForCrime(charge.Kind, charge.Circumstances);
    }

    private static void RecalculateChargeSanction(JusticeCharge charge, JusticeRecordState record)
    {
        if (charge == null || charge.IsAggregate)
        {
            return;
        }

        JusticeSanction sanction = Evaluate(new JusticeIncident
        {
            Kind = charge.Kind,
            Circumstances = charge.Circumstances,
            AdditionalVictimCount = charge.AdditionalVictimCount
        }, record);
        charge.Points = sanction.Points;
        charge.Fine = sanction.Fine;
        charge.SentenceSeconds = sanction.SentenceSeconds;
    }

    private static bool AreDuplicateCharges(JusticeCharge left, JusticeCharge right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        // Je remplace une complicité par l'action directe du joueur, jamais
        // l'inverse, afin de conserver la qualification la plus précise.
        return !left.IsAlliedAction || right.IsAlliedAction;
    }

    private static bool Supersedes(JusticeCharge stronger, JusticeCharge weaker)
    {
        if (!stronger.IsAlliedAction && weaker.IsAlliedAction &&
            CanDirectCrimeReplaceAccessory(stronger.Kind, weaker.Kind))
        {
            return true;
        }

        if (stronger.Kind == weaker.Kind)
        {
            return !stronger.IsAlliedAction && weaker.IsAlliedAction;
        }

        if (stronger.Kind == JusticeCrimeKind.VehicleDestruction &&
            weaker.Kind == JusticeCrimeKind.VehicleDamage)
        {
            return true;
        }

        if (stronger.Kind == JusticeCrimeKind.MurderOfficer)
        {
            return IsOfficerAssaultOrHomicide(weaker.Kind) || IsGenericAssaultOrHomicide(weaker.Kind);
        }

        if (stronger.Kind == JusticeCrimeKind.AccessoryMurderOfficer)
        {
            return weaker.Kind == JusticeCrimeKind.AccessoryAssaultOfficer;
        }

        if (stronger.Kind == JusticeCrimeKind.MurderCivilian ||
            stronger.Kind == JusticeCrimeKind.Manslaughter)
        {
            return IsGenericAssault(weaker.Kind);
        }

        if (stronger.Kind == JusticeCrimeKind.AssaultOfficer)
        {
            return IsGenericAssault(weaker.Kind) || weaker.Kind == JusticeCrimeKind.AccessoryAssaultOfficer;
        }

        if (stronger.Kind == JusticeCrimeKind.AggravatedAssault)
        {
            return weaker.Kind == JusticeCrimeKind.SimpleAssault;
        }

        return false;
    }

    private static bool DoesRelatedViolenceSupersedeRecklessDischarge(
        JusticeCharge stronger,
        JusticeCharge weaker)
    {
        if (stronger == null || weaker == null || stronger.IsAlliedAction ||
            weaker.Kind != JusticeCrimeKind.RecklessDischarge ||
            !IsDirectVictimViolence(stronger.Kind) ||
            string.IsNullOrWhiteSpace(stronger.CausalEventId) ||
            !string.Equals(stronger.CausalEventId, weaker.CausalEventId, StringComparison.Ordinal) ||
            !string.Equals(stronger.EpisodeId, weaker.EpisodeId, StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static bool IsDirectVictimViolence(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.SimpleAssault ||
               kind == JusticeCrimeKind.AggravatedAssault ||
               kind == JusticeCrimeKind.AssaultOfficer ||
               kind == JusticeCrimeKind.Manslaughter ||
               kind == JusticeCrimeKind.MurderCivilian ||
               kind == JusticeCrimeKind.MurderOfficer;
    }

    private static bool IsOfficerAssaultOrHomicide(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.AssaultOfficer ||
               kind == JusticeCrimeKind.AccessoryAssaultOfficer ||
               kind == JusticeCrimeKind.AccessoryMurderOfficer;
    }

    private static bool IsAccessoryOfficerCrime(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.AccessoryAssaultOfficer ||
               kind == JusticeCrimeKind.AccessoryMurderOfficer;
    }

    private static bool CanDirectCrimeReplaceAccessory(
        JusticeCrimeKind directKind,
        JusticeCrimeKind accessoryKind)
    {
        // Je ne laisse jamais une agression directe effacer l'homicide commis
        // par un allié. Une action personnelle ne remplace que la complicité de
        // même niveau ou d'un niveau inférieur sur la même victime.
        if (directKind == JusticeCrimeKind.MurderOfficer)
        {
            return IsAccessoryOfficerCrime(accessoryKind);
        }

        return directKind == JusticeCrimeKind.AssaultOfficer &&
               accessoryKind == JusticeCrimeKind.AccessoryAssaultOfficer;
    }

    private static bool IsGenericAssaultOrHomicide(JusticeCrimeKind kind)
    {
        return IsGenericAssault(kind) ||
               kind == JusticeCrimeKind.Manslaughter ||
               kind == JusticeCrimeKind.MurderCivilian;
    }

    private static bool IsGenericAssault(JusticeCrimeKind kind)
    {
        return kind == JusticeCrimeKind.SimpleAssault ||
               kind == JusticeCrimeKind.AggravatedAssault;
    }

    internal static int GetConvictionRecidivismIncrease(JusticeSeverity severity)
    {
        switch (severity)
        {
            case JusticeSeverity.Minor: return 2;
            case JusticeSeverity.Misdemeanor: return 5;
            case JusticeSeverity.Serious: return 10;
            case JusticeSeverity.Crime: return 18;
            case JusticeSeverity.Major: return 28;
            case JusticeSeverity.Critical: return 35;
            default: return 0;
        }
    }

    internal static int CalculateCleanDecay(int cleanSeconds)
    {
        if (cleanSeconds <= 1800)
        {
            return 0;
        }

        long decay = Math.Min(3600, cleanSeconds - 1800) / 600;
        if (cleanSeconds > 5400)
        {
            decay += (Math.Min(5400, cleanSeconds - 5400) / 600) * 2L;
        }
        if (cleanSeconds > 10800)
        {
            decay += ((long)cleanSeconds - 10800L) / 600L * 3L;
        }

        return (int)Math.Min(int.MaxValue, decay);
    }

    private static long MultiplyCombinedCeiling(
        long value,
        int firstBasisPoints,
        int secondBasisPoints)
    {
        if (value <= 0L || firstBasisPoints <= 0 || secondBasisPoints <= 0)
        {
            return 0L;
        }

        decimal scaled = (decimal)value * firstBasisPoints * secondBasisPoints /
                         ((decimal)BasisPointScale * BasisPointScale);
        if (scaled >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Ceiling(scaled);
    }

    private static string ResolveIncidentEpisodeId(
        JusticeCaseState caseState,
        JusticeIncident incident,
        string incidentId)
    {
        string episode = NormalizeIdentifier(incident.EpisodeId);
        if (episode.Length == 0 && incident.Kind == JusticeCrimeKind.Escape)
        {
            episode = NormalizeIdentifier(caseState.CustodyEpisodeId);
        }
        if (episode.Length == 0)
        {
            episode = NormalizeIdentifier(caseState.WantedEpisodeId);
        }

        return episode.Length == 0 ? "incident:" + incidentId : episode;
    }

    private static string ResolveConvictionEpisodeId(JusticeCaseState caseState)
    {
        string episode = NormalizeIdentifier(caseState.CustodyEpisodeId);
        if (episode.Length == 0)
        {
            episode = NormalizeIdentifier(caseState.WantedEpisodeId);
        }

        for (int index = 0; episode.Length == 0 && index < caseState.Charges.Count; index++)
        {
            JusticeCharge charge = caseState.Charges[index];
            if (charge == null)
            {
                continue;
            }

            episode = NormalizeIdentifier(charge.EpisodeId);
            if (episode.Length == 0)
            {
                episode = NormalizeIdentifier(charge.IncidentId);
            }
        }

        return episode;
    }

    private static void ResetCleanGameplayProgress(JusticeRecordState record)
    {
        if (record == null)
        {
            return;
        }

        record.CleanGameplaySeconds = 0;
        record.AppliedCleanDecay = 0;
    }

    private static long RoundUp(long value, long quantum)
    {
        if (value <= 0L || quantum <= 1L)
        {
            return Math.Max(0L, value);
        }

        long remainder = value % quantum;
        if (remainder == 0L)
        {
            return value;
        }

        long addition = quantum - remainder;
        return value > long.MaxValue - addition ? long.MaxValue : value + addition;
    }

    private static int SaturatingAddInt(int current, int addition)
    {
        if (current < 0) current = 0;
        if (addition <= 0) return current;
        return addition > int.MaxValue - current ? int.MaxValue : current + addition;
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    private static bool HasAny(JusticeSignal value, JusticeSignal candidates)
    {
        return (value & candidates) != 0;
    }

    private static bool ContainsOrdinal(IList<string> values, string candidate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void RememberBounded(List<string> values, string candidate, int maximumCount)
    {
        values.Add(candidate);
        while (values.Count > maximumCount)
        {
            values.RemoveAt(0);
        }
    }

    private static void RememberAppliedConvictionId(
        JusticeRecordState record,
        string convictionId)
    {
        if (record == null || string.IsNullOrWhiteSpace(convictionId) ||
            ContainsOrdinal(record.AppliedConvictionIds, convictionId))
        {
            return;
        }

        record.AppliedConvictionIds.Add(convictionId);
        while (record.AppliedConvictionIds.Count > MaxAppliedConvictionIds)
        {
            int removableIndex = -1;
            for (int index = 0; index < record.AppliedConvictionIds.Count; index++)
            {
                if (!string.Equals(
                    record.AppliedConvictionIds[index],
                    record.PinnedConvictionId,
                    StringComparison.Ordinal))
                {
                    removableIndex = index;
                    break;
                }
            }

            if (removableIndex < 0)
            {
                throw new InvalidOperationException(
                    "Impossible de borner les condamnations appliquées Justice.");
            }
            record.AppliedConvictionIds.RemoveAt(removableIndex);
        }
    }

    internal static void TrimVisibleConvictions(JusticeRecordState record)
    {
        if (record == null)
        {
            throw new ArgumentNullException("record");
        }

        while (record.Convictions.Count > MaxConvictions)
        {
            int removableIndex = -1;
            for (int index = 0; index < record.Convictions.Count; index++)
            {
                JusticeConviction candidate = record.Convictions[index];
                bool isPinned = candidate != null &&
                    !string.IsNullOrWhiteSpace(record.PinnedConvictionId) &&
                    string.Equals(
                        candidate.ConvictionId,
                        record.PinnedConvictionId,
                        StringComparison.Ordinal);
                if (!isPinned)
                {
                    removableIndex = index;
                    break;
                }
            }

            if (removableIndex < 0)
            {
                throw new InvalidOperationException(
                    "Impossible de borner l'historique visible des condamnations Justice.");
            }

            record.Convictions.RemoveAt(removableIndex);
        }
    }

    private static void RememberUnique(
        List<string> values,
        string candidate,
        int maximumCount)
    {
        string normalizedCandidate = NormalizeIdentifier(candidate);
        if (normalizedCandidate.Length == 0 || ContainsOrdinal(values, normalizedCandidate))
        {
            return;
        }

        RememberBounded(values, normalizedCandidate, maximumCount);
    }

    internal static string NormalizeEpisodeId(string value)
    {
        return NormalizeIdentifier(value);
    }

    private static string NormalizeIdentifier(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
