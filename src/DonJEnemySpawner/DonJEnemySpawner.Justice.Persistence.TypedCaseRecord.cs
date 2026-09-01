using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml;

// Je fige toute l'identité d'un contributeur avant de quitter le thread GTA.
// Ce DTO ne contient ni handle vivant, ni référence vers une entité du jeu.
internal sealed class JusticeAlliedContributorPersistenceDto
{
    internal JusticeAlliedContributorPersistenceDto(int handle, int generation)
    {
        Handle = handle;
        Generation = generation;
    }

    internal int Handle { get; }

    internal int Generation { get; }
}

// Je transporte une charge sous forme de valeurs uniquement. Les deux listes
// sont recopiées et exposées en lecture seule afin qu'aucune mutation du dossier
// runtime ne puisse atteindre le writer de persistance.
internal sealed class JusticeChargePersistenceDto
{
    private readonly ReadOnlyCollection<int> _alliedContributorHandles;
    private readonly ReadOnlyCollection<JusticeAlliedContributorPersistenceDto> _alliedContributors;

    internal JusticeChargePersistenceDto(
        string chargeId,
        string incidentId,
        string episodeId,
        string detectionBatchId,
        string causalEventId,
        JusticeCrimeKind kind,
        string displayName,
        int victimHandle,
        int victimGeneration,
        int points,
        long fine,
        int sentenceSeconds,
        long confirmedAtMs,
        bool isAlliedAction,
        JusticeCircumstances circumstances,
        int additionalVictimCount,
        bool isAggregate,
        int aggregatedChargeCount,
        IEnumerable<int> alliedContributorHandles,
        IEnumerable<JusticeAlliedContributorPersistenceDto> alliedContributors,
        bool isAdjudicated)
    {
        ChargeId = chargeId;
        IncidentId = incidentId;
        EpisodeId = episodeId;
        DetectionBatchId = detectionBatchId;
        CausalEventId = causalEventId;
        Kind = kind;
        DisplayName = displayName;
        VictimHandle = victimHandle;
        VictimGeneration = victimGeneration;
        Points = points;
        Fine = fine;
        SentenceSeconds = sentenceSeconds;
        ConfirmedAtMs = confirmedAtMs;
        IsAlliedAction = isAlliedAction;
        Circumstances = circumstances;
        AdditionalVictimCount = additionalVictimCount;
        IsAggregate = isAggregate;
        AggregatedChargeCount = aggregatedChargeCount;
        IsAdjudicated = isAdjudicated;
        _alliedContributorHandles = CopyValues(alliedContributorHandles);
        _alliedContributors = CopyContributors(alliedContributors);
    }

    internal string ChargeId { get; }

    internal string IncidentId { get; }

    internal string EpisodeId { get; }

    internal string DetectionBatchId { get; }

    internal string CausalEventId { get; }

    internal JusticeCrimeKind Kind { get; }

    internal string DisplayName { get; }

    internal int VictimHandle { get; }

    internal int VictimGeneration { get; }

    internal int Points { get; }

    internal long Fine { get; }

    internal int SentenceSeconds { get; }

    internal long ConfirmedAtMs { get; }

    internal bool IsAlliedAction { get; }

    internal JusticeCircumstances Circumstances { get; }

    internal int AdditionalVictimCount { get; }

    internal bool IsAggregate { get; }

    internal int AggregatedChargeCount { get; }

    internal IReadOnlyList<int> AlliedContributorHandles
    {
        get { return _alliedContributorHandles; }
    }

    internal IReadOnlyList<JusticeAlliedContributorPersistenceDto> AlliedContributors
    {
        get { return _alliedContributors; }
    }

    internal bool IsAdjudicated { get; }

    private static ReadOnlyCollection<int> CopyValues(IEnumerable<int> values)
    {
        return new ReadOnlyCollection<int>(
            values == null ? new List<int>() : new List<int>(values));
    }

    private static ReadOnlyCollection<JusticeAlliedContributorPersistenceDto> CopyContributors(
        IEnumerable<JusticeAlliedContributorPersistenceDto> contributors)
    {
        List<JusticeAlliedContributorPersistenceDto> copy =
            new List<JusticeAlliedContributorPersistenceDto>();
        if (contributors != null)
        {
            foreach (JusticeAlliedContributorPersistenceDto contributor in contributors)
            {
                if (contributor == null)
                {
                    throw new ArgumentException(
                        "Une charge persistée ne peut pas contenir de contributeur nul.",
                        "contributors");
                }

                copy.Add(new JusticeAlliedContributorPersistenceDto(
                    contributor.Handle,
                    contributor.Generation));
            }
        }

        return new ReadOnlyCollection<JusticeAlliedContributorPersistenceDto>(copy);
    }
}

// Je capture le dossier complet, y compris les listes de déduplication et les
// indicateurs legacy qui influencent encore le XML courant.
internal sealed class JusticeCasePersistenceDto
{
    private readonly ReadOnlyCollection<JusticeChargePersistenceDto> _charges;
    private readonly ReadOnlyCollection<string> _completedOperationIds;
    private readonly ReadOnlyCollection<string> _processedIncidentIds;
    private readonly ReadOnlyCollection<string> _fleeingChargedEpisodeIds;
    private readonly ReadOnlyCollection<string> _escapeChargedEpisodeIds;

    internal JusticeCasePersistenceDto(
        bool enabled,
        IEnumerable<JusticeChargePersistenceDto> charges,
        int activeScore,
        long fineDue,
        long voluntaryFinePaid,
        long fineInDispute,
        int sentenceSeconds,
        long custodyGuardPenaltySeconds,
        bool hasWarrant,
        bool escapeWantedMinimumPending,
        bool escapeWantedMinimumAttempted,
        JusticePhase phase,
        string wantedEpisodeId,
        string custodyEpisodeId,
        JusticeCrimeKind? lastCrimeKind,
        string lastCrimeLabel,
        IEnumerable<string> completedOperationIds,
        IEnumerable<string> processedIncidentIds,
        IEnumerable<string> fleeingChargedEpisodeIds,
        IEnumerable<string> escapeChargedEpisodeIds,
        bool fleeingCharged,
        bool escapeCharged)
    {
        Enabled = enabled;
        _charges = CopyCharges(charges);
        ActiveScore = activeScore;
        FineDue = fineDue;
        VoluntaryFinePaid = voluntaryFinePaid;
        FineInDispute = fineInDispute;
        SentenceSeconds = sentenceSeconds;
        CustodyGuardPenaltySeconds = Math.Max(0L, custodyGuardPenaltySeconds);
        HasWarrant = hasWarrant;
        EscapeWantedMinimumPending = escapeWantedMinimumPending;
        EscapeWantedMinimumAttempted = escapeWantedMinimumAttempted;
        Phase = phase;
        WantedEpisodeId = wantedEpisodeId;
        CustodyEpisodeId = custodyEpisodeId;
        LastCrimeKind = lastCrimeKind;
        LastCrimeLabel = lastCrimeLabel;
        _completedOperationIds = CopyStrings(completedOperationIds);
        _processedIncidentIds = CopyStrings(processedIncidentIds);
        _fleeingChargedEpisodeIds = CopyStrings(fleeingChargedEpisodeIds);
        _escapeChargedEpisodeIds = CopyStrings(escapeChargedEpisodeIds);
        FleeingCharged = fleeingCharged;
        EscapeCharged = escapeCharged;
    }

    internal bool Enabled { get; }

    internal IReadOnlyList<JusticeChargePersistenceDto> Charges
    {
        get { return _charges; }
    }

    internal int ActiveScore { get; }

    internal long FineDue { get; }

    internal long VoluntaryFinePaid { get; }

    internal long FineInDispute { get; }

    internal int SentenceSeconds { get; }

    internal long CustodyGuardPenaltySeconds { get; }

    internal bool HasWarrant { get; }

    internal bool EscapeWantedMinimumPending { get; }

    internal bool EscapeWantedMinimumAttempted { get; }

    internal JusticePhase Phase { get; }

    internal string WantedEpisodeId { get; }

    internal string CustodyEpisodeId { get; }

    internal JusticeCrimeKind? LastCrimeKind { get; }

    internal string LastCrimeLabel { get; }

    internal IReadOnlyList<string> CompletedOperationIds
    {
        get { return _completedOperationIds; }
    }

    internal IReadOnlyList<string> ProcessedIncidentIds
    {
        get { return _processedIncidentIds; }
    }

    internal IReadOnlyList<string> FleeingChargedEpisodeIds
    {
        get { return _fleeingChargedEpisodeIds; }
    }

    internal IReadOnlyList<string> EscapeChargedEpisodeIds
    {
        get { return _escapeChargedEpisodeIds; }
    }

    internal bool FleeingCharged { get; }

    internal bool EscapeCharged { get; }

    private static ReadOnlyCollection<JusticeChargePersistenceDto> CopyCharges(
        IEnumerable<JusticeChargePersistenceDto> charges)
    {
        List<JusticeChargePersistenceDto> copy = new List<JusticeChargePersistenceDto>();
        if (charges != null)
        {
            foreach (JusticeChargePersistenceDto charge in charges)
            {
                if (charge == null)
                {
                    throw new ArgumentException(
                        "Un dossier persisté ne peut pas contenir de charge nulle.",
                        "charges");
                }

                copy.Add(charge);
            }
        }

        return new ReadOnlyCollection<JusticeChargePersistenceDto>(copy);
    }

    private static ReadOnlyCollection<string> CopyStrings(IEnumerable<string> values)
    {
        return new ReadOnlyCollection<string>(
            values == null ? new List<string>() : new List<string>(values));
    }
}

// Je fige le détail judiciaire d'une charge déjà jugée sans conserver le
// JusticeConvictionChargeSummary mutable qui appartient au runtime.
internal sealed class JusticeConvictionChargeSummaryPersistenceDto
{
    internal JusticeConvictionChargeSummaryPersistenceDto(
        JusticeCrimeKind kind,
        string displayName,
        int points,
        long fine,
        int sentenceSeconds,
        JusticeCircumstances circumstances,
        bool circumstancesWerePersisted,
        bool isAggregate,
        int aggregatedChargeCount)
    {
        Kind = kind;
        DisplayName = displayName;
        Points = points;
        Fine = fine;
        SentenceSeconds = sentenceSeconds;
        Circumstances = circumstances;
        CircumstancesWerePersisted = circumstancesWerePersisted;
        IsAggregate = isAggregate;
        AggregatedChargeCount = aggregatedChargeCount;
    }

    internal JusticeCrimeKind Kind { get; }

    internal string DisplayName { get; }

    internal int Points { get; }

    internal long Fine { get; }

    internal int SentenceSeconds { get; }

    internal JusticeCircumstances Circumstances { get; }

    internal bool CircumstancesWerePersisted { get; }

    internal bool IsAggregate { get; }

    internal int AggregatedChargeCount { get; }
}

// Je détache chaque condamnation et tous ses résumés avant la sérialisation.
internal sealed class JusticeConvictionPersistenceDto
{
    private readonly ReadOnlyCollection<JusticeConvictionChargeSummaryPersistenceDto> _charges;

    internal JusticeConvictionPersistenceDto(
        string convictionId,
        DateTime judgedAtUtc,
        JusticeSeverity severity,
        int score,
        long fine,
        int sentenceSeconds,
        IEnumerable<JusticeConvictionChargeSummaryPersistenceDto> charges)
    {
        ConvictionId = convictionId;
        JudgedAtUtc = judgedAtUtc;
        Severity = severity;
        Score = score;
        Fine = fine;
        SentenceSeconds = sentenceSeconds;
        _charges = CopyCharges(charges);
    }

    internal string ConvictionId { get; }

    internal DateTime JudgedAtUtc { get; }

    internal JusticeSeverity Severity { get; }

    internal int Score { get; }

    internal long Fine { get; }

    internal int SentenceSeconds { get; }

    internal IReadOnlyList<JusticeConvictionChargeSummaryPersistenceDto> Charges
    {
        get { return _charges; }
    }

    private static ReadOnlyCollection<JusticeConvictionChargeSummaryPersistenceDto> CopyCharges(
        IEnumerable<JusticeConvictionChargeSummaryPersistenceDto> charges)
    {
        List<JusticeConvictionChargeSummaryPersistenceDto> copy =
            new List<JusticeConvictionChargeSummaryPersistenceDto>();
        if (charges != null)
        {
            foreach (JusticeConvictionChargeSummaryPersistenceDto charge in charges)
            {
                if (charge == null)
                {
                    throw new ArgumentException(
                        "Une condamnation persistée ne peut pas contenir de résumé nul.",
                        "charges");
                }

                copy.Add(charge);
            }
        }

        return new ReadOnlyCollection<JusticeConvictionChargeSummaryPersistenceDto>(copy);
    }
}

// Je capture aussi les identifiants de condamnations évincées et la révision du
// casier. La révision reste hors XML, comme dans le writer historique.
internal sealed class JusticeRecordPersistenceDto
{
    private readonly ReadOnlyCollection<JusticeConvictionPersistenceDto> _convictions;
    private readonly ReadOnlyCollection<string> _appliedConvictionIds;

    internal JusticeRecordPersistenceDto(
        int recidivismIndex,
        int cleanGameplaySeconds,
        int appliedCleanDecay,
        IEnumerable<JusticeConvictionPersistenceDto> convictions,
        IEnumerable<string> appliedConvictionIds,
        int ledgerRevision,
        string pinnedConvictionId)
    {
        RecidivismIndex = recidivismIndex;
        CleanGameplaySeconds = cleanGameplaySeconds;
        AppliedCleanDecay = appliedCleanDecay;
        _convictions = CopyConvictions(convictions);
        _appliedConvictionIds = CopyStrings(appliedConvictionIds);
        LedgerRevision = ledgerRevision;
        PinnedConvictionId = pinnedConvictionId;
    }

    internal int RecidivismIndex { get; }

    internal int CleanGameplaySeconds { get; }

    internal int AppliedCleanDecay { get; }

    internal IReadOnlyList<JusticeConvictionPersistenceDto> Convictions
    {
        get { return _convictions; }
    }

    internal IReadOnlyList<string> AppliedConvictionIds
    {
        get { return _appliedConvictionIds; }
    }

    internal int LedgerRevision { get; }

    internal string PinnedConvictionId { get; }

    private static ReadOnlyCollection<JusticeConvictionPersistenceDto> CopyConvictions(
        IEnumerable<JusticeConvictionPersistenceDto> convictions)
    {
        List<JusticeConvictionPersistenceDto> copy =
            new List<JusticeConvictionPersistenceDto>();
        if (convictions != null)
        {
            foreach (JusticeConvictionPersistenceDto conviction in convictions)
            {
                if (conviction == null)
                {
                    throw new ArgumentException(
                        "Un casier persisté ne peut pas contenir de condamnation nulle.",
                        "convictions");
                }

                copy.Add(conviction);
            }
        }

        return new ReadOnlyCollection<JusticeConvictionPersistenceDto>(copy);
    }

    private static ReadOnlyCollection<string> CopyStrings(IEnumerable<string> values)
    {
        return new ReadOnlyCollection<string>(
            values == null ? new List<string>() : new List<string>(values));
    }
}

// Je groupe le dossier et le casier issus de la même capture du thread GTA.
internal sealed class JusticeCaseRecordPersistenceDto
{
    internal JusticeCaseRecordPersistenceDto(
        JusticeCasePersistenceDto caseState,
        JusticeRecordPersistenceDto recordState)
    {
        Case = caseState ?? throw new ArgumentNullException("caseState");
        Record = recordState ?? throw new ArgumentNullException("recordState");
    }

    internal JusticeCasePersistenceDto Case { get; }

    internal JusticeRecordPersistenceDto Record { get; }
}

public sealed partial class DonJEnemySpawner
{
    // Je dois appeler cette méthode sur le thread GTA. Après son retour, le
    // worker peut conserver le graphe sans dépendre du moindre objet runtime.
    internal static JusticeCaseRecordPersistenceDto CaptureJusticeCaseRecordPersistenceDto(
        JusticeCaseState caseState,
        JusticeRecordState recordState)
    {
        return new JusticeCaseRecordPersistenceDto(
            CaptureJusticeCasePersistenceDto(caseState),
            CaptureJusticeRecordPersistenceDto(recordState));
    }

    internal static JusticeCasePersistenceDto CaptureJusticeCasePersistenceDto(
        JusticeCaseState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException("state");
        }

        // Je conserve le contrat du writer v1 : il borne et consolide d'abord
        // les charges sur le thread propriétaire, jamais depuis le worker.
        JusticePolicy.EnforceActiveChargeLimit(state);

        List<JusticeChargePersistenceDto> charges =
            new List<JusticeChargePersistenceDto>(state.Charges.Count);
        for (int index = 0; index < state.Charges.Count; index++)
        {
            JusticeCharge charge = state.Charges[index];
            if (charge == null)
            {
                continue;
            }

            charges.Add(CaptureJusticeChargePersistenceDto(charge));
        }

        return new JusticeCasePersistenceDto(
            state.Enabled,
            charges,
            state.ActiveScore,
            state.FineDue,
            state.VoluntaryFinePaid,
            state.FineInDispute,
            state.SentenceSeconds,
            state.CustodyGuardPenaltySeconds,
            state.HasWarrant,
            state.EscapeWantedMinimumPending,
            state.EscapeWantedMinimumAttempted,
            state.Phase,
            state.WantedEpisodeId,
            state.CustodyEpisodeId,
            state.LastCrimeKind,
            state.LastCrimeLabel,
            state.CompletedOperationIds,
            state.ProcessedIncidentIds,
            state.FleeingChargedEpisodeIds,
            state.EscapeChargedEpisodeIds,
            state.FleeingCharged,
            state.EscapeCharged);
    }

    internal static JusticeRecordPersistenceDto CaptureJusticeRecordPersistenceDto(
        JusticeRecordState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException("state");
        }

        List<JusticeConvictionPersistenceDto> convictions =
            new List<JusticeConvictionPersistenceDto>(state.Convictions.Count);
        for (int index = 0; index < state.Convictions.Count; index++)
        {
            JusticeConviction conviction = state.Convictions[index];
            if (conviction == null)
            {
                continue;
            }

            List<JusticeConvictionChargeSummaryPersistenceDto> summaries =
                new List<JusticeConvictionChargeSummaryPersistenceDto>(conviction.Charges.Count);
            for (int summaryIndex = 0; summaryIndex < conviction.Charges.Count; summaryIndex++)
            {
                JusticeConvictionChargeSummary summary = conviction.Charges[summaryIndex];
                if (summary == null)
                {
                    continue;
                }

                summaries.Add(new JusticeConvictionChargeSummaryPersistenceDto(
                    summary.Kind,
                    summary.DisplayName,
                    summary.Points,
                    summary.Fine,
                    summary.SentenceSeconds,
                    summary.Circumstances,
                    summary.CircumstancesWerePersisted,
                    summary.IsAggregate,
                    summary.AggregatedChargeCount));
            }

            convictions.Add(new JusticeConvictionPersistenceDto(
                conviction.ConvictionId,
                conviction.JudgedAtUtc,
                conviction.Severity,
                conviction.Score,
                conviction.Fine,
                conviction.SentenceSeconds,
                summaries));
        }

        return new JusticeRecordPersistenceDto(
            state.RecidivismIndex,
            state.CleanGameplaySeconds,
            state.AppliedCleanDecay,
            convictions,
            state.AppliedConvictionIds,
            state.LedgerRevision,
            state.PinnedConvictionId);
    }

    private static JusticeChargePersistenceDto CaptureJusticeChargePersistenceDto(
        JusticeCharge charge)
    {
        List<int> legacyHandles = new List<int>(charge.AlliedContributorHandles);
        List<JusticeAlliedContributorPersistenceDto> contributors =
            new List<JusticeAlliedContributorPersistenceDto>(charge.AlliedContributors.Count);

        for (int index = 0; index < charge.AlliedContributors.Count; index++)
        {
            JusticeEntityIdentity identity = charge.AlliedContributors[index];
            contributors.Add(new JusticeAlliedContributorPersistenceDto(
                identity.Handle,
                identity.Generation));
        }

        // Je reproduis l'import legacy sans muter la charge source : un handle
        // historique absent devient une identité de génération zéro, dans le
        // même ordre que le writer existant.
        for (int handleIndex = 0; handleIndex < legacyHandles.Count; handleIndex++)
        {
            int handle = legacyHandles[handleIndex];
            if (handle <= 0 || ContainsJusticeContributorHandle(contributors, handle))
            {
                continue;
            }

            contributors.Add(new JusticeAlliedContributorPersistenceDto(handle, 0));
        }

        return new JusticeChargePersistenceDto(
            charge.ChargeId,
            charge.IncidentId,
            charge.EpisodeId,
            charge.DetectionBatchId,
            charge.CausalEventId,
            charge.Kind,
            charge.DisplayName,
            charge.VictimHandle,
            charge.VictimGeneration,
            charge.Points,
            charge.Fine,
            charge.SentenceSeconds,
            charge.ConfirmedAtMs,
            charge.IsAlliedAction,
            charge.Circumstances,
            charge.AdditionalVictimCount,
            charge.IsAggregate,
            charge.AggregatedChargeCount,
            legacyHandles,
            contributors,
            charge.IsAdjudicated);
    }

    private static bool ContainsJusticeContributorHandle(
        List<JusticeAlliedContributorPersistenceDto> contributors,
        int handle)
    {
        for (int index = 0; index < contributors.Count; index++)
        {
            if (contributors[index].Handle == handle)
            {
                return true;
            }
        }

        return false;
    }

    // Ces deux writers peuvent s'exécuter sur le worker : ils ne lisent que le
    // DTO figé et l'XmlWriter fourni, sans domaine mutable ni API GTA.
    internal static void WriteJusticeCaseXml(
        XmlWriter writer,
        JusticeCasePersistenceDto state)
    {
        if (writer == null || state == null)
        {
            return;
        }

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
        writer.WriteAttributeString(
            "custodyGuardPenaltySeconds",
            state.CustodyGuardPenaltySeconds.ToString(CultureInfo.InvariantCulture));
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
        writer.WriteAttributeString(
            "lastCrimeKind",
            state.LastCrimeKind.HasValue ? state.LastCrimeKind.Value.ToString() : string.Empty);
        writer.WriteAttributeString("lastCrimeLabel", state.LastCrimeLabel ?? string.Empty);
        writer.WriteAttributeString("fleeingCharged", state.FleeingCharged ? "true" : "false");
        writer.WriteAttributeString("escapeCharged", state.EscapeCharged ? "true" : "false");

        writer.WriteStartElement("Charges");
        for (int index = 0; index < state.Charges.Count; index++)
        {
            JusticeChargePersistenceDto charge = state.Charges[index];
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
            writer.WriteAttributeString(
                "circumstances",
                ((int)charge.Circumstances).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "additionalVictims",
                Math.Max(0, charge.AdditionalVictimCount).ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("AlliedContributors");
            for (int contributorIndex = 0;
                 contributorIndex < charge.AlliedContributors.Count &&
                 contributorIndex < JusticeMaximumWitnessesPerEvent;
                 contributorIndex++)
            {
                JusticeAlliedContributorPersistenceDto identity =
                    charge.AlliedContributors[contributorIndex];
                if (identity.Handle <= 0)
                {
                    continue;
                }

                writer.WriteStartElement("Ally");
                writer.WriteAttributeString(
                    "handle",
                    identity.Handle.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "generation",
                    Math.Max(0, identity.Generation).ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        WriteJusticePersistenceIdAttributeList(
            writer,
            "FleeingEpisodes",
            "Episode",
            "id",
            state.FleeingChargedEpisodeIds);
        WriteJusticePersistenceIdAttributeList(
            writer,
            "EscapeEpisodes",
            "Episode",
            "id",
            state.EscapeChargedEpisodeIds);
        WriteJusticePersistenceStringList(
            writer,
            "ProcessedIncidents",
            "Incident",
            state.ProcessedIncidentIds);
        WriteJusticePersistenceStringList(
            writer,
            "CompletedOperations",
            "Operation",
            state.CompletedOperationIds);
        writer.WriteEndElement();
    }

    internal static void WriteJusticeRecordXml(
        XmlWriter writer,
        JusticeRecordPersistenceDto state)
    {
        if (writer == null || state == null)
        {
            return;
        }

        writer.WriteStartElement("Record");
        writer.WriteAttributeString("recidivism", state.RecidivismIndex.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "cleanGameplaySeconds",
            state.CleanGameplaySeconds.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "appliedCleanDecay",
            state.AppliedCleanDecay.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("pinnedConvictionId", state.PinnedConvictionId ?? string.Empty);
        writer.WriteStartElement("Convictions");
        for (int index = 0; index < state.Convictions.Count; index++)
        {
            JusticeConvictionPersistenceDto conviction = state.Convictions[index];
            writer.WriteStartElement("Conviction");
            writer.WriteAttributeString("id", conviction.ConvictionId ?? string.Empty);
            writer.WriteAttributeString(
                "judgedAtUtc",
                conviction.JudgedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("severity", conviction.Severity.ToString());
            writer.WriteAttributeString("score", conviction.Score.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("fine", conviction.Fine.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "sentenceSeconds",
                conviction.SentenceSeconds.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("ChargeSummaries");
            for (int chargeIndex = 0; chargeIndex < conviction.Charges.Count; chargeIndex++)
            {
                JusticeConvictionChargeSummaryPersistenceDto summary = conviction.Charges[chargeIndex];
                writer.WriteStartElement("Charge");
                writer.WriteAttributeString("kind", summary.Kind.ToString());
                writer.WriteAttributeString("label", summary.DisplayName ?? string.Empty);
                writer.WriteAttributeString(
                    "points",
                    Math.Max(0, summary.Points).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "fine",
                    Math.Max(0L, summary.Fine).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString(
                    "sentence",
                    Math.Max(0, summary.SentenceSeconds).ToString(CultureInfo.InvariantCulture));
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
        WriteJusticePersistenceIdAttributeList(
            writer,
            "AppliedConvictions",
            "ConvictionId",
            "id",
            state.AppliedConvictionIds);
        writer.WriteEndElement();
    }

    private static void WriteJusticePersistenceIdAttributeList(
        XmlWriter writer,
        string containerName,
        string itemName,
        string attributeName,
        IReadOnlyList<string> values)
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

    private static void WriteJusticePersistenceStringList(
        XmlWriter writer,
        string containerName,
        string itemName,
        IReadOnlyList<string> values)
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
}
