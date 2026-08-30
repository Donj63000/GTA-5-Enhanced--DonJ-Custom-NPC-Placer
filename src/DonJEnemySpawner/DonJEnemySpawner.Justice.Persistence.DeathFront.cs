using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private const string JusticeDeathFrontOperationKind = "DeathFront";
    private const string JusticePoliceDeathFrontMode = "PoliceCapture";
    private const string JusticePoliceArrestFrontMode = "PoliceArrest";
    private const string JusticeCustodyDeathFrontMode = "CustodyRebind";

    private JusticeWalRecord _justicePendingDeathFrontWalRecord;
    private Dictionary<string, long> _justiceDeathFrontResultCandidates;
    private Dictionary<string, long> _justiceDeathFrontResultRevisions;

    private bool TryPersistJusticePoliceDeathFrontToWal(Ped player)
    {
        int currentSlot = GetCurrentSinglePlayerCashSlotSafe();
        int observedSlot = JusticePolicy.ResolveTrustedCanonicalPlayerSlot(
            currentSlot,
            _justiceLastCanonicalPlayerSlot);
        int ownerSlot = JusticePolicy.ResolvePoliceDeathFrontOwnerSlot(
            currentSlot,
            _justiceActivePlayerProfileSlot,
            _justiceLastCanonicalPlayerSlot);
        return TryPersistJusticeDeferredPoliceFrontToWal(
            JusticePoliceDeathFrontMode,
            ownerSlot,
            observedSlot,
            GetJusticePedModelHashSafe(player));
    }

    private bool TryPersistJusticeDeferredPoliceFrontToWal(
        string mode,
        int ownerSlot,
        int playerModel)
    {
        return TryPersistJusticeDeferredPoliceFrontToWal(
            mode,
            ownerSlot,
            ownerSlot,
            playerModel);
    }

    private bool TryPersistJusticeDeferredPoliceFrontToWal(
        string mode,
        int ownerSlot,
        int playerSlot,
        int playerModel)
    {
        if (mode != JusticePoliceDeathFrontMode &&
            mode != JusticePoliceArrestFrontMode)
        {
            return false;
        }

        EnsureJusticePlayerProfilesInitialized();
        JusticeCaseState ownerCase = IsJusticeCanonicalProfileSlot(ownerSlot) &&
            ownerSlot < _justicePlayerProfiles.Length &&
            _justicePlayerProfiles[ownerSlot] != null
                ? _justicePlayerProfiles[ownerSlot].CaseState
                : null;
        if (ownerCase == null || !ownerCase.Enabled)
        {
            // Je ferme aussi la méthode de persistance elle-même : un appelant
            // ne peut jamais réactiver implicitement le profil propriétaire.
            return false;
        }
        return TryPersistJusticeDeathFrontToWal(
            mode,
            ownerSlot,
            ownerCase.WantedEpisodeId,
            (int)JusticeCustodySite.None,
            playerSlot,
            playerModel);
    }

    private bool TryPersistJusticeCustodyDeathFrontToWal(Ped player)
    {
        int ownerSlot = IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
            ? _justiceActivePlayerProfileSlot
            : _justiceCustodyPlayerSlot;
        int playerModel = _justiceCustodyPlayerModelHash != 0
            ? _justiceCustodyPlayerModelHash
            : GetJusticePedModelHashSafe(player);
        return TryPersistJusticeDeathFrontToWal(
            JusticeCustodyDeathFrontMode,
            ownerSlot,
            _justiceCaseState == null
                ? string.Empty
                : _justiceCaseState.CustodyEpisodeId,
            (int)_justiceCustodySite,
            _justiceCustodyPlayerSlot,
            playerModel);
    }

    private bool TryPersistJusticeDeathFrontToWal(
        string mode,
        int ownerSlot,
        string episodeId,
        int custodySite,
        int playerSlot,
        int playerModel)
    {
        if ((mode == JusticeCustodyDeathFrontMode &&
             string.IsNullOrWhiteSpace(episodeId)) ||
            (mode != JusticePoliceDeathFrontMode &&
             mode != JusticePoliceArrestFrontMode &&
             mode != JusticeCustodyDeathFrontMode))
        {
            return false;
        }

        JusticeWalRecord prepared = _justicePendingDeathFrontWalRecord;
        if (prepared == null)
        {
            EnsureJusticePlayerProfilesInitialized();
            EnsureJusticeProfilePersistenceGenerations();
            JusticePlayerProfileState owner =
                IsJusticeCanonicalProfileSlot(ownerSlot) &&
                ownerSlot < _justicePlayerProfiles.Length
                    ? _justicePlayerProfiles[ownerSlot]
                    : null;
            long profileGeneration = owner == null
                ? 0L
                : Math.Max(0L, _justiceProfilePersistenceGenerations[ownerSlot]);
            string identityKey = owner == null
                ? string.Empty
                : CreateJusticeProfileIdentityKey(owner);
            long createdAtUtcTicks = Math.Max(1L, DateTime.UtcNow.Ticks);
            if (mode == JusticePoliceArrestFrontMode &&
                string.IsNullOrWhiteSpace(episodeId))
            {
                // Une arrestation sans dossier actif reçoit un épisode figé dans
                // le WAL. Sa matérialisation restera ainsi idempotente après un
                // crash, y compris avant le snapshot de changement de profil.
                episodeId = "arrest-front:" +
                    ownerSlot.ToString(CultureInfo.InvariantCulture) + ":" +
                    createdAtUtcTicks.ToString(CultureInfo.InvariantCulture);
            }
            long baseRevision = Math.Max(
                0L,
                _justiceRepository == null
                    ? _justicePersistenceRevision
                    : _justiceRepository.GetDiagnostics().DiskRevision);
            bool policeFront = mode == JusticePoliceDeathFrontMode ||
                mode == JusticePoliceArrestFrontMode;
            int deathCanonicalSlot = policeFront &&
                IsJusticeCanonicalProfileSlot(playerSlot)
                    ? playerSlot
                    : _justiceLastCanonicalPlayerSlot;
            int deathCanonicalModel = policeFront &&
                IsJusticeCanonicalProfileSlot(playerSlot) && playerModel != 0
                    ? playerModel
                    : _justiceLastCanonicalPlayerModelHash;
            List<JusticePersistenceField> fields =
                CreateJusticeDeathFrontWalFields(
                    mode,
                    baseRevision,
                    profileGeneration,
                    identityKey,
                    episodeId,
                    custodySite,
                    playerSlot,
                    playerModel,
                    deathCanonicalSlot,
                    deathCanonicalModel);
            prepared = new JusticeWalRecord(
                "death-front:" +
                    ownerSlot.ToString(CultureInfo.InvariantCulture) + ":" +
                    createdAtUtcTicks.ToString(CultureInfo.InvariantCulture),
                JusticeDeathFrontOperationKind,
                ownerSlot,
                JusticeWalState.Prepared,
                baseRevision,
                createdAtUtcTicks,
                fields);
            if (!IsJusticeDeathFrontWalRecordExact(prepared))
            {
                return false;
            }
            _justicePendingDeathFrontWalRecord = prepared;
        }
        else if (!IsJusticeDeathFrontWalRecordExact(prepared) ||
                 !string.Equals(
                     ReadWalString(prepared, "mode", string.Empty),
                     mode,
                     StringComparison.Ordinal) ||
                 prepared.ProfileSlot != ownerSlot ||
                 !string.Equals(
                     ReadWalString(prepared, "episodeId", string.Empty),
                     episodeId,
                     StringComparison.Ordinal))
        {
            // Je ne mélange jamais deux morts dans la même transaction. Le front
            // déjà durable doit être repris avant d'en accepter un second.
            return false;
        }

        InitializeJusticePersistenceServices();
        if (_justiceRepository == null || _justiceWriteAheadLog == null ||
            _justicePersistenceServicesUnavailable)
        {
            // La preuve reste en mémoire et UpdateJusticeEarly la reprendra avant
            // toute autre mutation. L'arête de mort n'est donc jamais consommée
            // silencieusement pendant une panne du service de persistance.
            return false;
        }

        if (!TryEnsureJusticeDeathFrontAttempted(prepared))
        {
            return false;
        }

        JusticeWalRecord durable = _justiceWriteAheadLog.GetLatest(
            prepared.TransactionId);
        if (durable == null || !IsJusticeDeathFrontWalRecordExact(durable))
        {
            return false;
        }

        ApplyJusticeDeathFrontToRuntime(durable, true);
        _justicePendingDeathFrontWalRecord = null;
        return true;
    }

    private bool TryResumePendingJusticeDeathFrontWal()
    {
        JusticeWalRecord pending = _justicePendingDeathFrontWalRecord;
        if (pending == null)
        {
            return true;
        }
        InitializeJusticePersistenceServices();
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            _justicePersistenceServicesUnavailable ||
            !TryEnsureJusticeDeathFrontAttempted(pending))
        {
            // Je conserve le Prepared en mémoire tant que le repository n'est
            // pas lui aussi disponible : un WAL seul ne prouve aucun XML résultat.
            return false;
        }

        JusticeWalRecord durable = _justiceWriteAheadLog.GetLatest(
            pending.TransactionId);
        if (durable == null || !IsJusticeDeathFrontWalRecordExact(durable))
        {
            return false;
        }
        ApplyJusticeDeathFrontToRuntime(durable, true);
        _justicePendingDeathFrontWalRecord = null;
        return true;
    }

    private bool TryEnsureJusticeDeathFrontAttempted(JusticeWalRecord prepared)
    {
        if (prepared == null || _justiceWriteAheadLog == null ||
            !IsJusticeDeathFrontWalRecordExact(prepared))
        {
            return false;
        }

        try
        {
            JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(
                prepared.TransactionId);
            if (latest != null && !IsJusticeDeathFrontWalRecordExact(latest))
            {
                throw new InvalidDataException(
                    "Le front de mort WAL ne correspond plus à sa preuve runtime.");
            }
            if (latest == null)
            {
                latest = _justiceWriteAheadLog.Append(prepared);
            }
            if (latest.State == JusticeWalState.Prepared)
            {
                latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                    latest.TransactionId,
                    latest.OperationKind,
                    latest.ProfileSlot,
                    JusticeWalState.Attempted,
                    latest.PersistenceRevision,
                    latest.CreatedAtUtcTicks,
                    latest.Fields));
            }
            return latest.State == JusticeWalState.Attempted ||
                   latest.State == JusticeWalState.Ambiguous ||
                   latest.State == JusticeWalState.Confirmed;
        }
        catch (Exception exception)
        {
            RegisterJusticePersistenceFailure(
                "front de mort WAL refusé: " + exception.GetType().Name);
            LogException("Justice.WAL.FrontMort", exception);
            return false;
        }
    }

    private void ApplyJusticeDeathFrontToRuntime(
        JusticeWalRecord record,
        bool queueSnapshot)
    {
        if (record == null || !IsJusticeDeathFrontWalRecordExact(record))
        {
            throw new InvalidDataException("Front de mort WAL invalide.");
        }

        string mode = ReadWalString(record, "mode", string.Empty);
        string episodeId = ReadWalString(record, "episodeId", string.Empty);
        int playerSlot = ReadWalInt(record, "playerSlot", -1);
        int playerModel = ReadWalInt(record, "playerModel", 0);
        EnsureJusticePlayerProfilesInitialized();
        EnsureJusticeProfilePersistenceGenerations();

        JusticePlayerProfileState owner =
            IsJusticeCanonicalProfileSlot(record.ProfileSlot) &&
            record.ProfileSlot < _justicePlayerProfiles.Length
                ? _justicePlayerProfiles[record.ProfileSlot]
                : null;
        if (owner == null || owner.CaseState == null || owner.RecordState == null ||
            !IsJusticeDeathFrontOwnerIdentityCompatible(record, owner))
        {
            throw new InvalidDataException(
                "Le front de mort WAL ne possède plus son profil propriétaire.");
        }

        long loadedGeneration =
            _justiceProfilePersistenceGenerations[record.ProfileSlot];
        long recordedGeneration = ReadWalLong(
            record,
            "profileGeneration",
            -1L);
        bool ownerIsActive = record.ProfileSlot == _justiceActivePlayerProfileSlot;
        if (mode == JusticePoliceDeathFrontMode ||
            mode == JusticePoliceArrestFrontMode)
        {
            bool arrestFront = mode == JusticePoliceArrestFrontMode;
            bool rawFront = !arrestFront && string.IsNullOrWhiteSpace(episodeId);
            if ((!rawFront &&
                 !string.IsNullOrWhiteSpace(owner.CaseState.WantedEpisodeId) &&
                 !string.Equals(
                     owner.CaseState.WantedEpisodeId,
                     episodeId,
                     StringComparison.Ordinal)) ||
                (playerSlot != -1 && playerSlot != record.ProfileSlot))
            {
                throw new InvalidDataException(
                    "Le front de mort policière ne correspond plus à son épisode.");
            }

            owner.CaseState.Enabled = true;
            if (!rawFront &&
                string.IsNullOrWhiteSpace(owner.CaseState.WantedEpisodeId))
            {
                // Je restaure l'épisode exact figé avant le crash. La charge
                // minimale sera ensuite matérialisée idempotemment dans ce même
                // épisode, sans en inventer un second.
                owner.CaseState.WantedEpisodeId = episodeId;
            }
            if (!rawFront &&
                !TryMaterializeJusticePoliceDeathFrontCase(
                    owner,
                    record,
                    episodeId))
            {
                throw new InvalidDataException(
                    "Le front de mort policière ne peut pas restaurer son dossier minimal.");
            }

            if (arrestFront)
            {
                owner.CaseState.Phase = JusticePhase.Surrendering;
                if (ownerIsActive)
                {
                    _justiceEnabled = true;
                    _justiceArrestCompletionProbePending = true;
                    _justiceArrestCompletionProbeStartedAtMs =
                        _justiceMonotonicTimeMs;
                    _justiceWantedLossPending = true;
                }
            }
            else
            {
                owner.PendingDeathCapture = true;
                owner.PendingDeathCapturePlayerSlot = playerSlot;
                owner.PendingDeathCapturePlayerModel = playerModel;
            }
            if (IsLoadedJusticeCaseActive(owner.CaseState) &&
                owner.CaseState.Phase == JusticePhase.AtLarge)
            {
                // Je normalise aussi un profil inactif : son épisode restauré
                // doit rester sémantiquement sauvegardable avant sa réactivation.
                owner.CaseState.Phase = JusticePhase.Wanted;
            }
            if (ownerIsActive && !arrestFront)
            {
                _justiceEnabled = true;
                _justicePursuitDeathObservedDuringSuspension = true;
                _justiceSuspendedPursuitDeathPlayerSlot = playerSlot;
                _justiceSuspendedPursuitDeathPlayerModelHash = playerModel;
                // Le WAL est déjà durable ici. J'arme le masque avant d'attendre
                // les rotations XML : l'hôpital vanilla ne doit jamais apparaître
                // pendant la confirmation primaire + backup. Le replay WAL
                // repasse par cette branche et restaure donc le même intent.
                _justicePoliceDeathRespawnMaskIntentPending = true;
            }
        }
        else if (mode == JusticeCustodyDeathFrontMode)
        {
            JusticeCustodyPersistenceSnapshot custody = owner.CustodySnapshot;
            int site = ReadWalInt(record, "custodySite", -1);
            bool custodyContextMatches = custody != null && custody.Active &&
                custody.Site == site && custody.PlayerSlot == playerSlot &&
                string.Equals(
                    owner.CaseState.CustodyEpisodeId,
                    episodeId,
                    StringComparison.Ordinal);
            bool modelMatches = custodyContextMatches &&
                custody.PlayerModelHash == playerModel;
            bool canAdoptObservedRespawnModel = custodyContextMatches &&
                !modelMatches && playerModel != 0 &&
                loadedGeneration < recordedGeneration &&
                playerSlot == record.ProfileSlot &&
                ReadWalInt(record, "lastCanonicalSlot", -1) ==
                    record.ProfileSlot;
            if (!custodyContextMatches ||
                (!modelMatches && !canAdoptObservedRespawnModel))
            {
                throw new InvalidDataException(
                    "Le front de décès en détention ne correspond plus à sa peine.");
            }
            owner.CustodySnapshot =
                CloneJusticeCustodyPersistenceSnapshotForDeathRebind(
                    custody,
                    playerModel);
            owner.CustodyXml = string.Empty;
            if (ownerIsActive)
            {
                _justiceCustodyWaitingForRespawn = true;
                _justiceCustodyDeathRebindPending = true;
                _justiceCustodyPlayerSlot = playerSlot;
                _justiceCustodyPlayerModelHash = playerModel;
                _justiceCustodyContainmentEstablished = false;
                _justiceCustodyRespawnMaskNeedsRearm |=
                    _justiceCustodyRespawnTransferPending;
                _justiceCustodyDeathStatePersistencePending = true;
                _justiceCustodyDeathPersistenceRevision = 0L;
                _justiceCustodyDeathPersistenceWriteFailures = 0L;
                _justiceCustodyDeathPersistenceWriterFailureObserved = false;
                _justiceNextCustodyDeathPersistenceRetryAt = 0;
            }
        }
        else
        {
            throw new InvalidDataException("Mode de front de mort WAL inconnu.");
        }

        JusticeMarkStateDirty();
        if (queueSnapshot)
        {
            if (mode == JusticeCustodyDeathFrontMode && ownerIsActive)
            {
                PersistJusticeCustodyDeathStateBeforeRespawn(
                    GetJusticeRawGameTimeSafe());
            }
            else
            {
                JusticeFlushStateNow();
            }
        }
    }

    private static bool TryMaterializeJusticePoliceDeathFrontCase(
        JusticePlayerProfileState owner,
        JusticeWalRecord record,
        string episodeId)
    {
        if (owner == null || owner.CaseState == null || owner.RecordState == null ||
            record == null || string.IsNullOrWhiteSpace(episodeId))
        {
            return false;
        }
        if (owner.CaseState.Charges.Count > 0 &&
            owner.CaseState.SentenceSeconds > 0)
        {
            return true;
        }

        string incidentId = "incident:" + episodeId.Trim() +
            ":EvadingPolice:0:0:0:0";
        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = incidentId,
            EpisodeId = episodeId.Trim(),
            DetectionBatchId = "batch:" + record.TransactionId,
            Kind = JusticeCrimeKind.EvadingPolice,
            CreatedAtMs = 0L,
            ExpiresAtMs = JusticePolicy.PendingIncidentLifetimeMs,
            Circumstances = JusticeCircumstances.None,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ObservedAtMs = 0L,
                ReportDueAtMs = 0L,
                ReportCompleted = true
            },
            IsConfirmed = true
        };
        if (!owner.CaseState.ProcessedIncidentIds.Contains(incidentId))
        {
            JusticePolicy.ApplyConfirmedIncident(
                owner.CaseState,
                incident,
                owner.RecordState);
        }
        return owner.CaseState.Charges.Count > 0 &&
               owner.CaseState.SentenceSeconds > 0 &&
               owner.CaseState.ProcessedIncidentIds.Contains(incidentId);
    }

    private void TrackJusticeDeathFrontResultSnapshots(
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
            if ((record.State != JusticeWalState.Prepared &&
                 record.State != JusticeWalState.Attempted &&
                 record.State != JusticeWalState.Ambiguous) ||
                !IsJusticeDeathFrontWalRecordExact(record) ||
                !DoesJusticeSnapshotContainDeathFront(snapshot, record))
            {
                continue;
            }
            if (_justiceDeathFrontResultRevisions != null &&
                _justiceDeathFrontResultRevisions.ContainsKey(
                    record.TransactionId))
            {
                // Une preuve déjà observée comme DiskRevision est immuable. Les
                // rotations suivantes servent uniquement à la pousser au backup.
                continue;
            }
            Dictionary<string, long> tracker =
                EnsureJusticeDeathFrontResultCandidateTracker();
            long trackedRevision;
            tracker.TryGetValue(record.TransactionId, out trackedRevision);
            tracker[record.TransactionId] = Math.Max(
                trackedRevision,
                snapshot.Revision);
        }
    }

    private void AdvanceJusticeDeathFrontWalResults(long diskRevision)
    {
        if (_justiceWriteAheadLog == null || diskRevision <= 0L)
        {
            return;
        }

        if (_justiceDeathFrontResultCandidates != null &&
            _justiceDeathFrontResultCandidates.Count > 0)
        {
            List<KeyValuePair<string, long>> candidates =
                new List<KeyValuePair<string, long>>(
                    _justiceDeathFrontResultCandidates);
            for (int index = 0; index < candidates.Count; index++)
            {
                KeyValuePair<string, long> pair = candidates[index];
                if (pair.Value > diskRevision)
                {
                    // Je laisse le writer terminer le candidat déjà accepté. Le
                    // republier à chaque tick pourrait l'empêcher d'atteindre le
                    // disque avec la politique latest-wins du repository.
                    continue;
                }
                if (pair.Value <= 0L || pair.Value < diskRevision)
                {
                    // Une révision acceptée peut être coalescée par le writer.
                    // Je n'en fais une preuve que si je l'observe exactement sur
                    // disque; sinon je programme un nouveau snapshot candidat.
                    JusticeMarkStateDirty();
                    continue;
                }
                try
                {
                    JusticeWalRecord latest =
                        _justiceWriteAheadLog.GetLatest(pair.Key);
                    if (latest == null ||
                        !IsJusticeDeathFrontWalRecordExact(latest))
                    {
                        throw new InvalidDataException(
                            "Le résultat de front de mort ne retrouve plus sa transaction.");
                    }
                    if (latest.State == JusticeWalState.Prepared)
                    {
                        latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                            latest.TransactionId,
                            latest.OperationKind,
                            latest.ProfileSlot,
                            JusticeWalState.Attempted,
                            latest.PersistenceRevision,
                            latest.CreatedAtUtcTicks,
                            latest.Fields));
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
                            "Le front de mort ne peut pas verrouiller sa preuve résultat.");
                    }

                    EnsureJusticeDeathFrontResultTracker()[pair.Key] =
                        diskRevision;
                    _justiceDeathFrontResultCandidates.Remove(pair.Key);
                    // Je force une rotation strictement suivante : elle place le
                    // primaire prouvé dans le backup avant Confirmed.
                    JusticeMarkStateDirty();
                }
                catch (Exception exception)
                {
                    RegisterJusticePersistenceFailure(
                        "qualification du front de mort impossible");
                    LogException("Justice.WAL.FrontMort", exception);
                }
            }
        }

        if (_justiceDeathFrontResultRevisions == null ||
            _justiceDeathFrontResultRevisions.Count == 0)
        {
            return;
        }
        List<string> completed = new List<string>();
        List<KeyValuePair<string, long>> proofs =
            new List<KeyValuePair<string, long>>(
                _justiceDeathFrontResultRevisions);
        for (int index = 0; index < proofs.Count; index++)
        {
            KeyValuePair<string, long> pair = proofs[index];
            if (pair.Value <= 0L)
            {
                continue;
            }
            try
            {
                JusticeWalRecord latest = _justiceWriteAheadLog.GetLatest(pair.Key);
                if (latest == null || !IsJusticeDeathFrontWalRecordExact(latest))
                {
                    throw new InvalidDataException(
                        "Le résultat de front de mort ne retrouve plus sa transaction.");
                }
                if (latest.State == JusticeWalState.Ambiguous)
                {
                    long backupProofRevision = Math.Max(
                        latest.PersistenceRevision,
                        pair.Value);
                    if (diskRevision > backupProofRevision)
                    {
                        latest = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                            latest.TransactionId,
                            latest.OperationKind,
                            latest.ProfileSlot,
                            JusticeWalState.Confirmed,
                            backupProofRevision,
                            latest.CreatedAtUtcTicks,
                            latest.Fields));
                    }
                    else
                    {
                        // Je conserve le tracker jusqu'à la rotation qui prouve
                        // le résultat dans le backup, même si l'ancien primaire
                        // perdu portait une révision plus avancée.
                        JusticeMarkStateDirty();
                    }
                }
                if (latest.IsTerminal)
                {
                    completed.Add(pair.Key);
                }
            }
            catch (Exception exception)
            {
                RegisterJusticePersistenceFailure(
                    "acquittement du front de mort impossible");
                LogException("Justice.WAL.FrontMort", exception);
            }
        }
        for (int index = 0; index < completed.Count; index++)
        {
            _justiceDeathFrontResultRevisions.Remove(completed[index]);
            if (_justiceDeathFrontResultCandidates != null)
            {
                _justiceDeathFrontResultCandidates.Remove(completed[index]);
            }
        }
    }

    private void RecoverJusticeDeathFrontFromWal(JusticeWalRecord record)
    {
        if (!IsJusticeDeathFrontWalRecordExact(record) ||
            (record.State != JusticeWalState.Prepared &&
             record.State != JusticeWalState.Attempted &&
             record.State != JusticeWalState.Ambiguous))
        {
            throw new InvalidDataException("Front de mort WAL irrécupérable.");
        }

        JusticePlayerProfileState owner =
            _justicePlayerProfiles[record.ProfileSlot];
        if (owner == null || owner.CaseState == null ||
            !IsJusticeDeathFrontOwnerIdentityCompatible(record, owner))
        {
            throw new InvalidDataException(
                "Le front de mort WAL ne possède plus son profil propriétaire.");
        }
        bool alreadyDurable = DoesJusticeProfileContainDeathFront(owner, record);
        long recordedGeneration = ReadWalLong(
            record,
            "profileGeneration",
            -1L);
        if (!alreadyDurable)
        {
            ApplyJusticeDeathFrontToRuntime(record, false);
        }

        // Je restaure la monotonie logique portée par le WAL avant le prochain
        // snapshot. Le writer latest-wins peut avoir durci une génération plus
        // ancienne sans que les générations intermédiaires existent en XML.
        _justiceProfilePersistenceGenerations[record.ProfileSlot] = Math.Max(
            _justiceProfilePersistenceGenerations[record.ProfileSlot],
            recordedGeneration);
        _justicePersistenceRevision = Math.Max(
            _justicePersistenceRevision,
            Math.Max(record.PersistenceRevision, recordedGeneration));

        if (alreadyDurable && record.State == JusticeWalState.Ambiguous)
        {
            EnsureJusticeDeathFrontResultTracker()[record.TransactionId] =
                record.PersistenceRevision;
        }
        else if (alreadyDurable && _justicePersistenceRevision > 0L)
        {
            EnsureJusticeDeathFrontResultCandidateTracker()[record.TransactionId] =
                _justicePersistenceRevision;
        }
    }

    private bool IsJusticePoliceDeathFrontResultDurable()
    {
        return IsJusticeDeathFrontResultDurable(JusticePoliceDeathFrontMode);
    }

    private bool IsJusticePoliceArrestFrontResultDurable()
    {
        return IsJusticeDeathFrontResultDurable(JusticePoliceArrestFrontMode);
    }

    private bool IsJusticeCustodyDeathFrontResultDurable()
    {
        return IsJusticeDeathFrontResultDurable(JusticeCustodyDeathFrontMode);
    }

    private bool HasOpenJusticeDeathFrontForProfileSlot(int profileSlot)
    {
        if (!IsJusticeCanonicalProfileSlot(profileSlot))
        {
            return true;
        }
        JusticeWalRecord pending = _justicePendingDeathFrontWalRecord;
        if (pending != null && IsJusticeDeathFrontWalRecordExact(pending) &&
            pending.ProfileSlot == profileSlot)
        {
            return true;
        }
        if (_justiceWriteAheadLog == null)
        {
            return false;
        }

        try
        {
            IReadOnlyList<JusticeWalRecord> open =
                _justiceWriteAheadLog.GetOpenTransactions();
            for (int index = 0; index < open.Count; index++)
            {
                JusticeWalRecord record = open[index];
                if (record.ProfileSlot == profileSlot &&
                    IsJusticeDeathFrontWalRecordExact(record))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception)
        {
            // Je ferme le reset en cas de lecture WAL ambiguë. Effacer le profil
            // serait irréversible alors que le front peut encore être rejoué.
            RegisterJusticePersistenceFailure(
                "lecture du front de mort avant reset impossible");
            LogException("Justice.WAL.FrontMort", exception);
            return true;
        }
    }

    private bool EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction()
    {
        InitializeJusticePersistenceServices();
        bool policeFrontDurable = IsJusticePoliceDeathFrontResultDurable();
        bool arrestFrontDurable = IsJusticePoliceArrestFrontResultDurable();
        bool custodyFrontDurable = IsJusticeCustodyDeathFrontResultDurable();
        if (policeFrontDurable && arrestFrontDurable && custodyFrontDurable)
        {
            return true;
        }

        // Je laisse le latch intact jusqu'à Confirmed. Le checkpoint périodique
        // poursuivra les deux rotations avant tout reset ou toute amnistie.
        JusticeMarkStateDirty();
        return false;
    }

    private bool IsJusticeDeathFrontResultDurable(string mode)
    {
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            _justicePersistenceServicesUnavailable)
        {
            // Je ferme le circuit : l'absence de service ne doit jamais être
            // interprétée comme une confirmation primaire + backup.
            return false;
        }
        FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
        IReadOnlyList<JusticeWalRecord> open =
            _justiceWriteAheadLog.GetOpenTransactions();
        for (int index = 0; index < open.Count; index++)
        {
            JusticeWalRecord record = open[index];
            if (!IsJusticeDeathFrontWalRecordExact(record) ||
                !string.Equals(
                    ReadWalString(record, "mode", string.Empty),
                    mode,
                    StringComparison.Ordinal) ||
                record.ProfileSlot != _justiceActivePlayerProfileSlot)
            {
                continue;
            }
            // Le gameplay ne consomme le latch qu'après Confirmed. Un WAL encore
            // ouvert signifie que le primaire et le backup ne sont pas tous deux
            // prouvés, même si le premier snapshot résultat est déjà durable.
            return false;
        }
        JusticeWalRecord pending = _justicePendingDeathFrontWalRecord;
        return pending == null ||
               !IsJusticeDeathFrontWalRecordExact(pending) ||
               pending.ProfileSlot != _justiceActivePlayerProfileSlot ||
               !string.Equals(
                   ReadWalString(pending, "mode", string.Empty),
                   mode,
                   StringComparison.Ordinal);
    }

    private bool DoesJusticeSnapshotContainDeathFront(
        JusticePersistenceSnapshot snapshot,
        JusticeWalRecord record)
    {
        JusticePersistenceProfileSnapshot owner =
            FindJusticePersistenceProfile(snapshot, record.ProfileSlot);
        if (owner == null || owner.Generation <
                ReadWalLong(record, "profileGeneration", -1L) ||
            !IsJusticeDeathFrontOwnerIdentityCompatible(
                record,
                owner.IdentityKey))
        {
            return false;
        }

        string mode = ReadWalString(record, "mode", string.Empty);
        int playerSlot = ReadWalInt(record, "playerSlot", -1);
        int playerModel = ReadWalInt(record, "playerModel", 0);
        string episodeId = ReadWalString(record, "episodeId", string.Empty);
        if (mode == JusticePoliceDeathFrontMode)
        {
            return owner.CaseState != null &&
                   (string.IsNullOrWhiteSpace(episodeId) ||
                    string.Equals(
                        owner.CaseState.WantedEpisodeId,
                        episodeId,
                        StringComparison.Ordinal)) &&
                   ReadSnapshotFieldBool(owner, "pendingDeathCapture") &&
                   ReadSnapshotFieldInt(owner, "pendingDeathCapturePlayerSlot", -1) ==
                       playerSlot &&
                   ReadSnapshotFieldInt(owner, "pendingDeathCapturePlayerModel", 0) ==
                       playerModel;
        }

        if (mode == JusticePoliceArrestFrontMode)
        {
            return owner.CaseState != null &&
                   string.Equals(
                       owner.CaseState.WantedEpisodeId,
                       episodeId,
                       StringComparison.Ordinal) &&
                   IsJusticeArrestFrontResultPhase(owner.CaseState);
        }

        JusticeCustodyPersistenceSnapshot custody = owner.CustodyState;
        return mode == JusticeCustodyDeathFrontMode &&
               owner.CaseState != null && custody != null && custody.Active &&
               string.Equals(
                   owner.CaseState.CustodyEpisodeId,
                   episodeId,
                   StringComparison.Ordinal) &&
               custody.Site == ReadWalInt(record, "custodySite", -1) &&
               custody.PlayerSlot == playerSlot &&
               custody.PlayerModelHash == playerModel &&
               custody.WaitingForRespawn && custody.DeathRebindPending;
    }

    private bool DoesJusticeProfileContainDeathFront(
        JusticePlayerProfileState owner,
        JusticeWalRecord record)
    {
        if (owner == null || record == null)
        {
            return false;
        }
        string mode = ReadWalString(record, "mode", string.Empty);
        if (mode == JusticePoliceDeathFrontMode)
        {
            return owner.PendingDeathCapture &&
                   owner.PendingDeathCapturePlayerSlot ==
                       ReadWalInt(record, "playerSlot", -1) &&
                   owner.PendingDeathCapturePlayerModel ==
                       ReadWalInt(record, "playerModel", 0) &&
                   (string.IsNullOrWhiteSpace(
                        ReadWalString(record, "episodeId", string.Empty)) ||
                    (owner.CaseState != null &&
                     string.Equals(
                         owner.CaseState.WantedEpisodeId,
                         ReadWalString(record, "episodeId", string.Empty),
                         StringComparison.Ordinal)));
        }
        if (mode == JusticePoliceArrestFrontMode)
        {
            return owner.CaseState != null &&
                   string.Equals(
                       owner.CaseState.WantedEpisodeId,
                       ReadWalString(record, "episodeId", string.Empty),
                       StringComparison.Ordinal) &&
                   IsJusticeArrestFrontResultPhase(owner.CaseState);
        }
        JusticeCustodyPersistenceSnapshot custody = owner.CustodySnapshot;
        return mode == JusticeCustodyDeathFrontMode && custody != null &&
               custody.WaitingForRespawn && custody.DeathRebindPending &&
               owner.CaseState != null &&
               string.Equals(
                   owner.CaseState.CustodyEpisodeId,
                   ReadWalString(record, "episodeId", string.Empty),
                   StringComparison.Ordinal) &&
               custody.Site == ReadWalInt(record, "custodySite", -1) &&
               custody.PlayerSlot == ReadWalInt(record, "playerSlot", -1) &&
               custody.PlayerModelHash == ReadWalInt(record, "playerModel", 0);
    }

    private static bool IsJusticeArrestFrontResultPhase(JusticeCaseState state)
    {
        return state != null && IsJusticeArrestFrontResultPhase(
            state.Enabled,
            state.Phase,
            state.HasWarrant);
    }

    private static bool IsJusticeArrestFrontResultPhase(
        JusticeCasePersistenceDto state)
    {
        return state != null && IsJusticeArrestFrontResultPhase(
            state.Enabled,
            state.Phase,
            state.HasWarrant);
    }

    private static bool IsJusticeArrestFrontResultPhase(
        bool enabled,
        JusticePhase phase,
        bool hasWarrant)
    {
        if (!enabled)
        {
            return false;
        }

        // Le premier résultat est Surrendering. Une capture ou la conversion
        // bornée en mandat sont des résultats plus avancés du même front et ne
        // doivent pas empêcher l'acquittement primaire + backup du WAL.
        return phase == JusticePhase.Surrendering ||
               phase == JusticePhase.Captured ||
               phase == JusticePhase.Transporting ||
               phase == JusticePhase.Incarcerated ||
               (phase == JusticePhase.AtLarge && hasWarrant);
    }

    private static bool ReadSnapshotFieldBool(
        JusticePersistenceProfileSnapshot profile,
        string path)
    {
        bool value;
        return profile != null && bool.TryParse(
            JusticeXmlPersistenceCodec.GetFieldValue(
                profile.Fields,
                path,
                string.Empty),
            out value) && value;
    }

    private static int ReadSnapshotFieldInt(
        JusticePersistenceProfileSnapshot profile,
        string path,
        int fallback)
    {
        int value;
        return profile != null && int.TryParse(
            JusticeXmlPersistenceCodec.GetFieldValue(
                profile.Fields,
                path,
                string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
                ? value
                : fallback;
    }

    private Dictionary<string, long> EnsureJusticeDeathFrontResultTracker()
    {
        if (_justiceDeathFrontResultRevisions == null)
        {
            _justiceDeathFrontResultRevisions =
                new Dictionary<string, long>(StringComparer.Ordinal);
        }
        return _justiceDeathFrontResultRevisions;
    }

    private Dictionary<string, long>
        EnsureJusticeDeathFrontResultCandidateTracker()
    {
        if (_justiceDeathFrontResultCandidates == null)
        {
            _justiceDeathFrontResultCandidates =
                new Dictionary<string, long>(StringComparer.Ordinal);
        }
        return _justiceDeathFrontResultCandidates;
    }

    private bool HasJusticeDeathFrontPersistenceWork()
    {
        return (_justiceDeathFrontResultCandidates != null &&
                _justiceDeathFrontResultCandidates.Count > 0) ||
               (_justiceDeathFrontResultRevisions != null &&
                _justiceDeathFrontResultRevisions.Count > 0);
    }

    private bool IsJusticeDeathFrontOwnerIdentityCompatible(
        JusticeWalRecord record,
        JusticePlayerProfileState owner)
    {
        return owner != null && IsJusticeDeathFrontOwnerIdentityCompatible(
            record,
            CreateJusticeProfileIdentityKey(owner));
    }

    private static bool IsJusticeDeathFrontOwnerIdentityCompatible(
        JusticeWalRecord record,
        string identityKey)
    {
        if (record == null ||
            !IsJusticeDeathFrontIdentityKeyForProfile(
                ReadWalString(record, "identityKey", string.Empty),
                record.ProfileSlot) ||
            !IsJusticeDeathFrontIdentityKeyForProfile(
                identityKey,
                record.ProfileSlot))
        {
            return false;
        }
        int canonicalSlot = ReadWalInt(record, "lastCanonicalSlot", -1);
        return canonicalSlot == -1 || canonicalSlot == record.ProfileSlot;
    }

    private static bool IsJusticeDeathFrontIdentityKeyForProfile(
        string identityKey,
        int profileSlot)
    {
        string prefix = "slot:" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ":model:";
        int model;
        return !string.IsNullOrWhiteSpace(identityKey) &&
               identityKey.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(
                   identityKey.Substring(prefix.Length),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out model);
    }

    private static List<JusticePersistenceField> CreateJusticeDeathFrontWalFields(
        string mode,
        long baseRevision,
        long profileGeneration,
        string identityKey,
        string episodeId,
        int custodySite,
        int playerSlot,
        int playerModel,
        int lastCanonicalSlot,
        int lastCanonicalModel)
    {
        return new List<JusticePersistenceField>(11)
        {
            new JusticePersistenceField("mode", mode),
            new JusticePersistenceField(
                "baseRevision",
                baseRevision.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "profileGeneration",
                profileGeneration.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField("identityKey", identityKey),
            new JusticePersistenceField("episodeId", episodeId),
            new JusticePersistenceField(
                "custodySite",
                custodySite.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "playerSlot",
                playerSlot.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "playerModel",
                playerModel.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "lastCanonicalSlot",
                lastCanonicalSlot.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "lastCanonicalModel",
                lastCanonicalModel.ToString(CultureInfo.InvariantCulture)),
            new JusticePersistenceField(
                "schemaMajor",
                JusticeXmlPersistenceCodec.SchemaMajor.ToString(
                    CultureInfo.InvariantCulture))
        };
    }

    private static bool IsJusticeDeathFrontWalRecordExact(JusticeWalRecord record)
    {
        if (record == null ||
            !string.Equals(
                record.OperationKind,
                JusticeDeathFrontOperationKind,
                StringComparison.Ordinal) ||
            !IsJusticeCanonicalProfileSlot(record.ProfileSlot) ||
            !HasExactJusticeWalFields(
                record,
                "mode",
                "baseRevision",
                "profileGeneration",
                "identityKey",
                "episodeId",
                "custodySite",
                "playerSlot",
                "playerModel",
                "lastCanonicalSlot",
                "lastCanonicalModel",
                "schemaMajor"))
        {
            return false;
        }

        string mode = ReadWalString(record, "mode", string.Empty);
        long baseRevision = ReadWalLong(record, "baseRevision", -1L);
        long profileGeneration = ReadWalLong(
            record,
            "profileGeneration",
            -1L);
        int playerSlot = ReadWalInt(record, "playerSlot", -2);
        bool policeFront = mode == JusticePoliceDeathFrontMode ||
            mode == JusticePoliceArrestFrontMode;
        return (policeFront ||
                mode == JusticeCustodyDeathFrontMode) &&
               baseRevision >= 0L && baseRevision < long.MaxValue &&
               record.PersistenceRevision >= baseRevision &&
               record.PersistenceRevision < long.MaxValue &&
               profileGeneration >= 0L &&
               profileGeneration < long.MaxValue &&
               IsJusticeDeathFrontIdentityKeyForProfile(
                   ReadWalString(record, "identityKey", string.Empty),
                   record.ProfileSlot) &&
               (mode == JusticePoliceDeathFrontMode ||
                !string.IsNullOrWhiteSpace(
                    ReadWalString(record, "episodeId", string.Empty))) &&
               ReadWalInt(record, "schemaMajor", -1) ==
                   JusticeXmlPersistenceCodec.SchemaMajor &&
               playerSlot >= -1 && playerSlot < JusticePlayerProfileCount &&
               (!policeFront ||
                playerSlot == -1 || playerSlot == record.ProfileSlot) &&
               (ReadWalInt(record, "lastCanonicalSlot", -2) == -1 ||
                ReadWalInt(record, "lastCanonicalSlot", -2) ==
                    record.ProfileSlot);
    }
}
