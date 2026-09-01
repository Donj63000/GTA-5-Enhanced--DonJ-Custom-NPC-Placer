using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

public sealed partial class DonJEnemySpawner
{
    private const string JusticeProfileResetWalOperationKind =
        "ProfileResetResult";

    private JusticeWalRecord _justicePendingProfileResetWalRecord;
    private bool _justiceProfileResetCompletionNotificationPending;
    private Dictionary<string, long> _justiceProfileResetResultCandidates;
    private Dictionary<string, long> _justiceProfileResetResultRevisions;

    private bool BeginJusticeProfileResetWalTransaction(int profileSlot)
    {
        if (!IsJusticeCanonicalProfileSlot(profileSlot) ||
            _justicePendingProfileResetWalRecord != null)
        {
            return false;
        }

        InitializeJusticePersistenceServices();
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            _justicePersistenceServicesUnavailable ||
            _justicePendingProfileResetWalRecord != null ||
            HasOpenJusticeProfileResetWal())
        {
            return false;
        }

        EnsureJusticePlayerProfilesInitialized();
        EnsureJusticeProfilePersistenceGenerations();
        JusticePlayerProfileState target = _justicePlayerProfiles[profileSlot];
        if (target == null || HasOpenJusticeDeathFrontForProfileSlot(profileSlot) ||
            HasJusticeProfileCustodyRecovery(target))
        {
            return false;
        }

        long profileGeneration = Math.Max(
            0L,
            _justiceProfilePersistenceGenerations[profileSlot]);
        long baseRevision = Math.Max(
            Math.Max(0L, _justicePersistenceRevision),
            _justiceRepository.GetDiagnostics().DiskRevision);
        long createdAtUtcTicks = Math.Max(1L, DateTime.UtcNow.Ticks);
        JusticeWalRecord prepared = new JusticeWalRecord(
            "profile-reset-result:" +
                profileSlot.ToString(CultureInfo.InvariantCulture) + ":" +
                createdAtUtcTicks.ToString(CultureInfo.InvariantCulture),
            JusticeProfileResetWalOperationKind,
            profileSlot,
            JusticeWalState.Prepared,
            baseRevision,
            createdAtUtcTicks,
            CreateJusticeProfileResetWalFields(
                profileGeneration,
                CreateJusticeProfileIdentityKey(target)));
        if (!IsJusticeProfileResetWalRecordExact(prepared))
        {
            return false;
        }

        JusticeWalRecord attempted = null;
        try
        {
            JusticeWalRecord durable = _justiceWriteAheadLog.Append(prepared);
            attempted = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                durable.TransactionId,
                durable.OperationKind,
                durable.ProfileSlot,
                JusticeWalState.Attempted,
                durable.PersistenceRevision,
                durable.CreatedAtUtcTicks,
                durable.Fields));
        }
        catch (Exception exception)
        {
            JusticeWalRecord durable = _justiceWriteAheadLog.GetLatest(
                prepared.TransactionId);
            if (durable != null && durable.State == JusticeWalState.Attempted &&
                IsJusticeProfileResetWalRecordExact(durable))
            {
                // Je traite l'ACK perdu après Flush comme une tentative durable :
                // revenir à l'ancien profil rendrait le WAL impossible à réconcilier.
                attempted = durable;
            }
            else
            {
                if (durable != null && durable.State == JusticeWalState.Prepared)
                {
                    _justicePendingProfileResetWalRecord = durable;
                    TryRejectJusticeProfileResetPreparedWal(durable);
                }
                RegisterJusticePersistenceFailure(
                    "préparation WAL du reset de profil impossible");
                LogException("Justice.WAL.ResetProfil", exception);
                return false;
            }
        }

        _justicePendingProfileResetWalRecord = attempted;
        _justiceProfileResetCompletionNotificationPending = true;
        if (!ApplyJusticeProfileResetWalResult(attempted))
        {
            return false;
        }
        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        return true;
    }

    private bool TryResumePendingJusticeProfileResetWal()
    {
        JusticeWalRecord pending = _justicePendingProfileResetWalRecord;
        if (pending == null)
        {
            return true;
        }

        InitializeJusticePersistenceServices();
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
            pending.TransactionId);
        if (latest == null || !IsJusticeProfileResetWalRecordExact(latest))
        {
            RegisterJusticePersistenceFailure(
                "transaction WAL du reset de profil introuvable");
            return false;
        }
        if (latest.State == JusticeWalState.Rejected)
        {
            _justicePendingProfileResetWalRecord = null;
            _justiceProfileResetCompletionNotificationPending = false;
            return true;
        }
        if (latest.State == JusticeWalState.Prepared)
        {
            // Après un redémarrage, Prepared prouve que l'appelant n'a jamais reçu
            // l'autorisation de supprimer le profil.
            return TryRejectJusticeProfileResetPreparedWal(latest);
        }

        if (latest.State != JusticeWalState.Attempted &&
            latest.State != JusticeWalState.Ambiguous &&
            latest.State != JusticeWalState.Confirmed)
        {
            return false;
        }

        if (!ApplyJusticeProfileResetWalResult(latest))
        {
            return false;
        }
        // Je rends le nettoyage du stockage reconnaissance rejouable avant de
        // terminaliser le WAL Justice qui porte le reset explicite.
        if (!ClearJusticeRecognitionProfile(
            pending.ProfileSlot,
            "réinitialisation explicite du profil confirmée"))
        {
            return false;
        }
        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        latest = _justiceWriteAheadLog.GetLatest(pending.TransactionId);
        if (latest != null && latest.State == JusticeWalState.Confirmed)
        {
            _justicePendingProfileResetWalRecord = null;
            if (_justiceProfileResetCompletionNotificationPending)
            {
                ShowStatus(
                    GetJusticeProfileDisplayName(pending.ProfileSlot) +
                    " : profil Justice réinitialisé.",
                    4200);
            }
            _justiceProfileResetCompletionNotificationPending = false;
            LogInfo(
                "Justice.Profil",
                "Réinitialisation de profil confirmée dans le primaire et le backup.");
            return true;
        }

        if (_justiceStateDirty)
        {
            // La première preuve arme Ambiguous; cette seconde capture pousse le
            // résultat prouvé dans le backup avant la confirmation terminale.
            JusticeFlushStateNow();
        }
        return false;
    }

    private void RecoverJusticeProfileResetFromWal(JusticeWalRecord record)
    {
        if (!IsJusticeProfileResetWalRecordExact(record))
        {
            throw new InvalidDataException(
                "Transaction WAL de reset de profil invalide.");
        }
        if (record.State == JusticeWalState.Prepared)
        {
            _justicePendingProfileResetWalRecord = record;
            _justiceProfileResetCompletionNotificationPending = false;
            TryRejectJusticeProfileResetPreparedWal(record);
            return;
        }
        if (record.State != JusticeWalState.Attempted &&
            record.State != JusticeWalState.Ambiguous)
        {
            throw new InvalidDataException(
                "Etat WAL de reset de profil irrécupérable.");
        }

        _justicePendingProfileResetWalRecord = record;
        _justiceProfileResetCompletionNotificationPending = false;
        EnsureJusticePlayerProfilesInitialized();
        // Je capture la révision du document effectivement relu avant de rejouer
        // le WAL. Le repository n'existe pas encore pendant cette récupération
        // et Apply peut relever la révision logique sans qu'une écriture disque
        // correspondante existe déjà.
        long loadedDocumentRevision = Math.Max(0L, _justicePersistenceRevision);
        bool resultAlreadyDurable = IsJusticeProfileResetResultPresent(
            _justicePlayerProfiles[record.ProfileSlot]);
        if (!ApplyJusticeProfileResetWalResult(record))
        {
            // Je conserve le WAL ouvert tant que le héros propriétaire n'est
            // pas identifiable ou que sa mortalité ne peut pas être vérifiée.
            return;
        }
        if (resultAlreadyDurable && record.State == JusticeWalState.Ambiguous)
        {
            EnsureJusticeProfileResetResultTracker()[record.TransactionId] =
                record.PersistenceRevision;
        }
        else if (resultAlreadyDurable && record.State == JusticeWalState.Attempted &&
                 loadedDocumentRevision > record.PersistenceRevision)
        {
            // Je ne reconstruis un candidat qu'à partir du document relu qui
            // contient déjà exactement le profil vide.
            EnsureJusticeProfileResetResultCandidateTracker()[
                record.TransactionId] = loadedDocumentRevision;
        }
    }

    private bool ApplyJusticeProfileResetWalResult(JusticeWalRecord record)
    {
        if (!IsJusticeProfileResetWalRecordExact(record))
        {
            throw new InvalidDataException("Résultat WAL de reset invalide.");
        }

        EnsureJusticePlayerProfilesInitialized();
        EnsureJusticeProfilePersistenceGenerations();
        JusticePlayerProfileState target = _justicePlayerProfiles[record.ProfileSlot];
        long loadedGeneration =
            _justiceProfilePersistenceGenerations[record.ProfileSlot];
        long recordedGeneration = ReadWalLong(
            record,
            "profileGeneration",
            -1L);
        if (!EnsureJusticeActiveProfileResetPlayerIsMortal(record.ProfileSlot))
        {
            return false;
        }

        bool resetAlreadyPresent = IsJusticeProfileResetResultPresent(target);
        bool stateChanged = false;
        if (!resetAlreadyPresent)
        {
            if (target == null || loadedGeneration > recordedGeneration ||
                !IsJusticeProfileResetIdentityForSlot(
                    ReadWalString(record, "identityKey", string.Empty),
                    record.ProfileSlot))
            {
                throw new InvalidDataException(
                    "Le reset WAL ne correspond plus au profil propriétaire.");
            }
            if (!ReplaceJusticePlayerProfileWithEmptyState(record.ProfileSlot))
            {
                return false;
            }
            stateChanged = true;
        }

        if (loadedGeneration < recordedGeneration)
        {
            stateChanged = true;
        }
        _justiceProfilePersistenceGenerations[record.ProfileSlot] = Math.Max(
            loadedGeneration,
            recordedGeneration);
        _justicePersistenceRevision = Math.Max(
            _justicePersistenceRevision,
            Math.Max(record.PersistenceRevision, recordedGeneration));
        if (stateChanged)
        {
            JusticeMarkStateDirty();
        }
        return true;
    }

    private static bool IsJusticeProfileResetResultPresent(
        JusticePlayerProfileState profile)
    {
        if (profile == null || profile.CaseState == null ||
            profile.RecordState == null)
        {
            return false;
        }
        JusticeCaseState caseState = profile.CaseState;
        JusticeRecordState recordState = profile.RecordState;
        return IsJusticeProfileResetCaseResultPresent(caseState) &&
               recordState.RecidivismIndex == 0 &&
               recordState.CleanGameplaySeconds == 0 &&
               recordState.AppliedCleanDecay == 0 &&
               recordState.Convictions.Count == 0 &&
               recordState.AppliedConvictionIds.Count == 0 &&
               recordState.LedgerRevision == 0 &&
               string.IsNullOrWhiteSpace(recordState.PinnedConvictionId) &&
               !profile.CanAdvanceCustodyInBackground &&
               profile.InactiveCustodyLastTickAt == 0 &&
               profile.InactiveCustodyElapsedRemainderMs == 0 &&
               !profile.PendingDeathCapture &&
               profile.PendingDeathCapturePlayerSlot == -1 &&
               profile.PendingDeathCapturePlayerModel == 0 &&
               !profile.PendingAmnestyWantedClear &&
               !profile.PendingLegalReleaseFinalization &&
               profile.PendingLegalReleaseSite == 0 &&
               profile.PendingLegalReleaseSelectedWeapon == 0 &&
               profile.LastCanonicalPlayerModel == 0 &&
               IsJusticeProfileResetCustodyResultPresent(profile);
    }

    private static bool IsJusticeProfileResetCaseResultPresent(
        JusticeCaseState state)
    {
        return state != null && !state.Enabled && state.Charges.Count == 0 &&
               state.ActiveScore == 0 && state.FineDue == 0L &&
               state.VoluntaryFinePaid == 0L && state.FineInDispute == 0L &&
               state.SentenceSeconds == 0 &&
               state.CustodyGuardPenaltySeconds == 0L && !state.HasWarrant &&
               !state.EscapeWantedMinimumPending &&
               !state.EscapeWantedMinimumAttempted &&
               state.Phase == JusticePhase.AtLarge &&
               string.IsNullOrWhiteSpace(state.WantedEpisodeId) &&
               string.IsNullOrWhiteSpace(state.CustodyEpisodeId) &&
               !state.LastCrimeKind.HasValue &&
               string.IsNullOrWhiteSpace(state.LastCrimeLabel) &&
               state.CompletedOperationIds.Count == 0 &&
               state.ProcessedIncidentIds.Count == 0 &&
               state.FleeingChargedEpisodeIds.Count == 0 &&
               state.EscapeChargedEpisodeIds.Count == 0;
    }

    private static bool IsJusticeProfileResetCustodyResultPresent(
        JusticePlayerProfileState profile)
    {
        if (profile == null)
        {
            return false;
        }
        if (profile.CustodySnapshot != null)
        {
            return IsJusticeProfileResetCustodyResultPresent(
                profile.CustodySnapshot);
        }
        return string.Equals(
            profile.CustodyXml,
            CreateCanonicalEmptyJusticeCustodyXml(),
            StringComparison.Ordinal);
    }

    private static bool IsJusticeProfileResetCustodyResultPresent(
        JusticeCustodyPersistenceSnapshot custody)
    {
        return custody != null && !custody.Active &&
               custody.Site == (int)JusticeCustodySite.None &&
               !custody.PoliceSuppressionApplied &&
               !custody.PoliceDispatchDisabled &&
               custody.InitialSentenceSeconds == 0 &&
               custody.ActivityReductionSeconds == 0 &&
               !custody.InventoryRemoved && !custody.WeaponControlsLocked &&
               custody.InventoryState == (int)JusticeInventoryCustodyState.None &&
               custody.InventoryCaptureFailures == 0 &&
               custody.InventoryRemovalFailures == 0 &&
               !custody.DeferredInventoryRestore &&
               !custody.WaitingForRespawn && !custody.DeathRebindPending &&
               !custody.PlayerStateStored && !custody.StoredInvincible &&
               !custody.StoredFrozen && custody.StoredCanRagdoll &&
               custody.PlayerModelHash == 0 && custody.PlayerSlot == -1 &&
               custody.ReleaseSelectedWeapon == JusticeUnarmedHash &&
               !custody.LegalReleaseWantedClearAttempted &&
               !custody.AmnestyWantedClearAttempted &&
               !custody.GuardRetaliationActive &&
               custody.FineDebitIntent == null &&
               custody.VoluntaryPaymentIntent == null &&
               custody.DisciplineIntent == null &&
               custody.InventorySnapshot == null &&
               !custody.HasActivityCooldownContainer &&
               custody.Cooldowns.Count == 0;
    }

    private void TrackJusticeProfileResetResultSnapshots(
        JusticePersistenceSnapshot snapshot)
    {
        if (snapshot == null || _justiceWriteAheadLog == null)
        {
            return;
        }

        IReadOnlyList<JusticeWalRecord> open =
            _justiceWriteAheadLog.GetOpenTransactions();
        for (int index = 0; index < open.Count; index++)
        {
            JusticeWalRecord record = open[index];
            if ((record.State != JusticeWalState.Attempted &&
                 record.State != JusticeWalState.Ambiguous) ||
                !IsJusticeProfileResetWalRecordExact(record) ||
                !DoesJusticeSnapshotContainProfileResetResult(snapshot, record))
            {
                continue;
            }
            if (_justiceProfileResetResultRevisions != null &&
                _justiceProfileResetResultRevisions.ContainsKey(
                    record.TransactionId))
            {
                continue;
            }

            Dictionary<string, long> candidates =
                EnsureJusticeProfileResetResultCandidateTracker();
            long previous;
            candidates.TryGetValue(record.TransactionId, out previous);
            candidates[record.TransactionId] = Math.Max(
                previous,
                snapshot.Revision);
        }
    }

    private void AdvanceJusticeProfileResetWalResults(long diskRevision)
    {
        if (_justiceWriteAheadLog == null || diskRevision <= 0L)
        {
            return;
        }

        if (_justiceProfileResetResultCandidates != null &&
            _justiceProfileResetResultCandidates.Count > 0)
        {
            List<KeyValuePair<string, long>> candidates =
                new List<KeyValuePair<string, long>>(
                    _justiceProfileResetResultCandidates);
            for (int index = 0; index < candidates.Count; index++)
            {
                KeyValuePair<string, long> candidate = candidates[index];
                if (candidate.Value > diskRevision)
                {
                    // Le writer porte encore cette révision : je l'attends sans
                    // alimenter une course latest-wins avec de nouveaux candidats.
                    continue;
                }
                if (candidate.Value <= 0L || candidate.Value < diskRevision)
                {
                    // Le writer a sauté le candidat. Une nouvelle capture exacte
                    // est nécessaire; la révision plus récente ne prouve rien.
                    JusticeMarkStateDirty();
                    continue;
                }

                try
                {
                    JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                        candidate.Key);
                    if (latest == null ||
                        !IsJusticeProfileResetWalRecordExact(latest))
                    {
                        throw new InvalidDataException(
                            "Le résultat du reset ne retrouve plus sa transaction.");
                    }
                    if (latest.State == JusticeWalState.Attempted)
                    {
                        latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                            latest.TransactionId,
                            latest.OperationKind,
                            latest.ProfileSlot,
                            JusticeWalState.Ambiguous,
                            diskRevision,
                            latest.CreatedAtUtcTicks,
                            latest.Fields));
                    }
                    if (latest.State != JusticeWalState.Ambiguous)
                    {
                        throw new InvalidDataException(
                            "Le reset ne peut pas verrouiller sa preuve résultat.");
                    }

                    EnsureJusticeProfileResetResultTracker()[candidate.Key] =
                        diskRevision;
                    _justiceProfileResetResultCandidates.Remove(candidate.Key);
                    // Je force une rotation distincte : File.Replace placera ce
                    // primaire vide et exactement prouvé dans le backup.
                    JusticeMarkStateDirty();
                }
                catch (Exception exception)
                {
                    RegisterJusticePersistenceFailure(
                        "qualification du reset de profil impossible");
                    LogException("Justice.WAL.ResetProfil", exception);
                }
            }
        }

        if (_justiceProfileResetResultRevisions == null ||
            _justiceProfileResetResultRevisions.Count == 0)
        {
            return;
        }

        List<string> completed = new List<string>();
        List<KeyValuePair<string, long>> proofs =
            new List<KeyValuePair<string, long>>(
                _justiceProfileResetResultRevisions);
        for (int index = 0; index < proofs.Count; index++)
        {
            KeyValuePair<string, long> proof = proofs[index];
            if (proof.Value <= 0L)
            {
                continue;
            }
            try
            {
                JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(proof.Key);
                if (latest == null ||
                    !IsJusticeProfileResetWalRecordExact(latest))
                {
                    throw new InvalidDataException(
                        "La preuve du reset ne retrouve plus sa transaction.");
                }
                if (latest.State == JusticeWalState.Ambiguous)
                {
                    long resultRevision = Math.Max(
                        latest.PersistenceRevision,
                        proof.Value);
                    if (diskRevision > resultRevision)
                    {
                        latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                            latest.TransactionId,
                            latest.OperationKind,
                            latest.ProfileSlot,
                            JusticeWalState.Confirmed,
                            resultRevision,
                            latest.CreatedAtUtcTicks,
                            latest.Fields));
                    }
                    else
                    {
                        JusticeMarkStateDirty();
                    }
                }
                if (latest.IsTerminal)
                {
                    completed.Add(proof.Key);
                }
            }
            catch (Exception exception)
            {
                RegisterJusticePersistenceFailure(
                    "acquittement du reset de profil impossible");
                LogException("Justice.WAL.ResetProfil", exception);
            }
        }
        for (int index = 0; index < completed.Count; index++)
        {
            _justiceProfileResetResultRevisions.Remove(completed[index]);
            if (_justiceProfileResetResultCandidates != null)
            {
                _justiceProfileResetResultCandidates.Remove(completed[index]);
            }
        }
    }

    private static bool DoesJusticeSnapshotContainProfileResetResult(
        JusticePersistenceSnapshot snapshot,
        JusticeWalRecord record)
    {
        JusticePersistenceProfileSnapshot profile =
            FindJusticePersistenceProfile(snapshot, record.ProfileSlot);
        if (profile == null || profile.Generation <
                ReadWalLong(record, "profileGeneration", -1L) ||
            !string.Equals(
                profile.IdentityKey,
                "slot:" + record.ProfileSlot.ToString(
                    CultureInfo.InvariantCulture) + ":model:0",
                StringComparison.Ordinal))
        {
            return false;
        }

        JusticeCasePersistenceDto caseState = profile.CaseState;
        JusticeRecordPersistenceDto recordState = profile.RecordState;
        bool emptyCase = caseState != null && !caseState.Enabled &&
            caseState.Charges.Count == 0 && caseState.ActiveScore == 0 &&
            caseState.FineDue == 0L && caseState.VoluntaryFinePaid == 0L &&
            caseState.FineInDispute == 0L && caseState.SentenceSeconds == 0 &&
            caseState.CustodyGuardPenaltySeconds == 0L &&
            !caseState.HasWarrant && !caseState.EscapeWantedMinimumPending &&
            !caseState.EscapeWantedMinimumAttempted &&
            caseState.Phase == JusticePhase.AtLarge &&
            string.IsNullOrWhiteSpace(caseState.WantedEpisodeId) &&
            string.IsNullOrWhiteSpace(caseState.CustodyEpisodeId) &&
            !caseState.LastCrimeKind.HasValue &&
            string.IsNullOrWhiteSpace(caseState.LastCrimeLabel) &&
            caseState.CompletedOperationIds.Count == 0 &&
            caseState.ProcessedIncidentIds.Count == 0 &&
            caseState.FleeingChargedEpisodeIds.Count == 0 &&
            caseState.EscapeChargedEpisodeIds.Count == 0;
        bool emptyRecord = recordState != null &&
            recordState.RecidivismIndex == 0 &&
            recordState.CleanGameplaySeconds == 0 &&
            recordState.AppliedCleanDecay == 0 &&
            recordState.Convictions.Count == 0 &&
            recordState.AppliedConvictionIds.Count == 0 &&
            recordState.LedgerRevision == 0 &&
            string.IsNullOrWhiteSpace(recordState.PinnedConvictionId);
        bool emptyProfileFields =
            !ReadSnapshotFieldBool(profile, "pendingDeathCapture") &&
            ReadSnapshotFieldInt(profile, "pendingDeathCapturePlayerSlot", 0) == -1 &&
            ReadSnapshotFieldInt(profile, "pendingDeathCapturePlayerModel", -1) == 0 &&
            !ReadSnapshotFieldBool(profile, "pendingAmnestyWantedClear") &&
            !ReadSnapshotFieldBool(profile, "pendingLegalReleaseFinalization") &&
            ReadSnapshotFieldInt(profile, "pendingLegalReleaseSite", -1) == 0 &&
            ReadSnapshotFieldInt(profile, "pendingLegalReleaseSelectedWeapon", -1) == 0 &&
            ReadSnapshotFieldInt(profile, "lastCanonicalPlayerModel", -1) == 0;
        bool emptyCustody = profile.CustodyState != null
            ? IsJusticeProfileResetCustodyResultPresent(profile.CustodyState)
            : string.Equals(
                JusticeXmlPersistenceCodec.GetFieldValue(
                    profile.Fields,
                    "Custody",
                    string.Empty),
                CreateCanonicalEmptyJusticeCustodyXml(),
                StringComparison.Ordinal);
        return emptyCase && emptyRecord && emptyProfileFields && emptyCustody;
    }

    private Dictionary<string, long> EnsureJusticeProfileResetResultTracker()
    {
        if (_justiceProfileResetResultRevisions == null)
        {
            _justiceProfileResetResultRevisions =
                new Dictionary<string, long>(StringComparer.Ordinal);
        }
        return _justiceProfileResetResultRevisions;
    }

    private Dictionary<string, long>
        EnsureJusticeProfileResetResultCandidateTracker()
    {
        if (_justiceProfileResetResultCandidates == null)
        {
            _justiceProfileResetResultCandidates =
                new Dictionary<string, long>(StringComparer.Ordinal);
        }
        return _justiceProfileResetResultCandidates;
    }

    private bool HasOpenJusticeProfileResetWal()
    {
        if (_justicePendingProfileResetWalRecord != null)
        {
            return true;
        }
        if (_justiceWriteAheadLog == null)
        {
            return false;
        }
        try
        {
            // Je garde ce garde-fou dans OnTick sans construire ni trier la liste
            // complète du WAL à chaque frame. Une frame ProfileResetResult même
            // invalide reste bloquante; la récupération stricte la refusera.
            return _justiceWriteAheadLog.HasOpenTransactionKind(
                JusticeProfileResetWalOperationKind);
        }
        catch (Exception exception)
        {
            // Je ferme toute action destructive ou réactivation si le WAL ne peut
            // pas prouver qu'aucun reset n'est encore ouvert.
            RegisterJusticePersistenceFailure(
                "lecture du WAL de reset de profil impossible");
            LogException("Justice.WAL.ResetProfil", exception);
            return true;
        }
    }

    private bool TryRejectJusticeProfileResetPreparedWal(JusticeWalRecord record)
    {
        if (record == null || record.State != JusticeWalState.Prepared ||
            !IsJusticeProfileResetWalRecordExact(record) ||
            _justiceWriteAheadLog == null)
        {
            return false;
        }

        _justicePendingProfileResetWalRecord = record;
        _justiceProfileResetCompletionNotificationPending = false;
        try
        {
            RejectJusticePreparedWalBeforeEffect(record);
            JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                record.TransactionId);
            if (latest == null || latest.State != JusticeWalState.Rejected)
            {
                return false;
            }
            _justicePendingProfileResetWalRecord = null;
            return true;
        }
        catch (Exception exception)
        {
            // Prepared ne porte encore aucun effet. Je conserve néanmoins son
            // identifiant en mémoire afin de retenter le rejet sans redémarrage.
            RegisterJusticePersistenceFailure(
                "rejet du reset Prepared impossible");
            LogException("Justice.WAL.ResetProfil", exception);
            return false;
        }
    }

    private static List<JusticePersistenceField>
        CreateJusticeProfileResetWalFields(
            long profileGeneration,
            string identityKey)
    {
        return new List<JusticePersistenceField>(3)
        {
            new JusticePersistenceField(
                "profileGeneration",
                profileGeneration.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField("identityKey", identityKey),
            new JusticePersistenceField(
                "schemaMajor",
                JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                    CultureInfo.InvariantCulture))
        };
    }

    private static bool IsJusticeProfileResetWalRecordExact(
        JusticeWalRecord record)
    {
        return record != null &&
               string.Equals(
                   record.OperationKind,
                   JusticeProfileResetWalOperationKind,
                   StringComparison.Ordinal) &&
               IsJusticeCanonicalProfileSlot(record.ProfileSlot) &&
               record.PersistenceRevision >= 0L &&
               ReadWalLong(record, "profileGeneration", -1L) >= 0L &&
               IsJusticeProfileResetIdentityForSlot(
                   ReadWalString(record, "identityKey", string.Empty),
                   record.ProfileSlot) &&
               ReadWalInt(record, "schemaMajor", -1) ==
                   JusticeXmlPersistenceCodec.SchemaMajor &&
               HasExactJusticeWalFields(
                   record,
                   "profileGeneration",
                   "identityKey",
                   "schemaMajor");
    }

    private static bool IsJusticeProfileResetIdentityForSlot(
        string identityKey,
        int profileSlot)
    {
        string prefix = "slot:" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":model:";
        int ignoredModel;
        return !string.IsNullOrWhiteSpace(identityKey) &&
               identityKey.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(
                   identityKey.Substring(prefix.Length),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out ignoredModel);
    }
}
