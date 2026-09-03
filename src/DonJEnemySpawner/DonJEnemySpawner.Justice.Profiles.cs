using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private const int JusticePlayerProfileCount = 3;

    private JusticePlayerProfileState[] _justicePlayerProfiles;
    private int _justiceActivePlayerProfileSlot = -1;
    private int _justiceMenuSelectedProfileSlot = -1;
    private bool _justiceProfileSelectionPending;
    private bool _justiceLegacyProfileReloadPending;
    private bool _justiceProfileContextBlocked;
    private bool _justiceProfileSwitchPersistencePending;
    private long _justiceProfileSwitchPersistenceRevision;
    private long _justiceProfileSwitchPersistenceWriteFailures;
    private bool _justiceActiveProfileResetPending;
    private bool _justiceActiveProfileResetPrecommitRedundant;
    private Func<int> _justiceCanonicalPlayerSlotOverride;
    private Func<int, bool> _justicePlayerMortalityVerificationOverride = null;

    internal int JusticeActivePlayerProfileSlot
    {
        get { return _justiceActivePlayerProfileSlot; }
    }

    private void ChangeJusticeMenuSelectedProfile(int direction)
    {
        int selected = IsJusticeCanonicalProfileSlot(_justiceMenuSelectedProfileSlot)
            ? _justiceMenuSelectedProfileSlot
            : (IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : 0);
        int delta = direction < 0 ? -1 : 1;
        _justiceMenuSelectedProfileSlot =
            (selected + delta + JusticePlayerProfileCount) % JusticePlayerProfileCount;
    }

    private string GetJusticeMenuSelectedProfileDisplay()
    {
        int slot = IsJusticeCanonicalProfileSlot(_justiceMenuSelectedProfileSlot)
            ? _justiceMenuSelectedProfileSlot
            : (IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : 0);
        return GetJusticeProfileDisplayName(slot);
    }

    private static string GetJusticeProfileDisplayName(int slot)
    {
        switch (slot)
        {
            case 0:
                return "Michael";
            case 1:
                return "Franklin";
            default:
                return "Trevor";
        }
    }

    private void RequestJusticeSelectedProfileReset()
    {
        int slot = GetJusticeMenuSelectedProfileSlot();
        JusticePlayerProfileState profile = GetJusticePlayerProfileForSlot(slot);
        if (profile == null)
        {
            ShowStatus("Profil Justice indisponible.", 2800);
            return;
        }
        bool activeProfile = slot == _justiceActivePlayerProfileSlot;
        if (_justiceAmnestyPending ||
            _justiceLegalReleaseFinalizationPending ||
            _justiceCustodyTransferRollbackFinalizationPending ||
            _justiceActiveProfileResetPending ||
            HasOpenJusticeProfileResetWal() ||
            _justiceBackupRepairPending ||
            _justiceProfileSwitchPersistencePending ||
            HasOpenJusticeDeathFrontForProfileSlot(slot) ||
            (activeProfile && HasJusticeProfilePendingRecoveryWal(profile)) ||
            (!activeProfile && HasJusticeProfileCustodyRecovery(profile)) ||
            (!activeProfile && HasJusticeCustodyRecoveryState()) ||
            (activeProfile &&
             (_justiceVoluntaryFinePaymentIntent != null ||
              _justiceFineDebitIntent != null)))
        {
            ShowStatus(
                "Réinitialisation différée : transaction ou inventaire d'un autre personnage à protéger.",
                3800);
            return;
        }
        RequestDangerConfirmation(MainMenuAction.JusticeResetProfile);
    }

    private void ExecuteJusticeSelectedProfileReset()
    {
        int playerHandle;
        int playerModelHash;
        CaptureJusticeDangerActionIdentity(out playerHandle, out playerModelHash);
        ExecuteJusticeConfirmedProfileReset(
            GetJusticeMenuSelectedProfileSlot(),
            playerHandle,
            playerModelHash);
    }

    private void ExecuteJusticeConfirmedProfileReset(
        int requestedProfileSlot,
        int expectedPlayerHandle,
        int expectedPlayerModelHash)
    {
        int slot = requestedProfileSlot;
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            ShowStatus("Réinitialisation annulée : profil devenu indisponible.", 3600);
            return;
        }

        bool activeProfile = slot == _justiceActivePlayerProfileSlot;
        if (activeProfile &&
            !IsJusticeDangerActionProfileContextValid(
                slot,
                expectedPlayerHandle,
                expectedPlayerModelHash))
        {
            // Je n'applique aucun effet monde au profil actif si GTA a changé
            // de protagoniste entre les deux validations de la modale.
            ShowStatus(
                "Réinitialisation annulée : le personnage actif a changé.",
                3800);
            return;
        }

        EnsureJusticePlayerProfilesInitialized();
        if (activeProfile)
        {
            SnapshotActiveJusticePlayerProfile();
        }
        JusticePlayerProfileState selectedProfile = _justicePlayerProfiles[slot];
        if (_justiceAmnestyPending ||
            _justiceLegalReleaseFinalizationPending ||
            _justiceCustodyTransferRollbackFinalizationPending ||
            _justiceActiveProfileResetPending ||
            HasOpenJusticeProfileResetWal() ||
            _justiceBackupRepairPending ||
            _justiceProfileSwitchPersistencePending ||
            HasOpenJusticeDeathFrontForProfileSlot(slot) ||
            (activeProfile && HasJusticeProfilePendingRecoveryWal(selectedProfile)) ||
            (!activeProfile && HasJusticeProfileCustodyRecovery(selectedProfile)) ||
            (!activeProfile && HasJusticeCustodyRecoveryState()) ||
            (activeProfile &&
             (_justiceVoluntaryFinePaymentIntent != null ||
              _justiceFineDebitIntent != null)))
        {
            ShowStatus(
                "Réinitialisation différée : transaction ou inventaire à protéger.",
                4000);
            return;
        }

        if (slot == _justiceActivePlayerProfileSlot && HasJusticeCustodyRecoveryState())
        {
            // Je précommitte le reset avant de restituer l'inventaire ou de
            // téléporter le détenu. Après un crash, le même profil reprend cette
            // opération idempotente au lieu de recharger son ancien dossier.
            if (_justiceVoluntaryFinePaymentIntent != null ||
                _justiceFineDebitIntent != null ||
                !BeginJusticeActiveProfileResetTransaction(slot))
            {
                ShowStatus(
                    "Réinitialisation différée : restitution ou transaction en cours.",
                    4000);
                return;
            }

            ResumeJusticeActiveProfileResetTransaction();
            return;
        }
        if (activeProfile)
        {
            SnapshotActiveJusticePlayerProfile();
        }
        if (!BeginJusticeProfileResetWalTransaction(slot))
        {
            ShowStatus(
                "Réinitialisation différée : transaction durable indisponible.",
                4000);
            return;
        }
        if (!TryResumePendingJusticeProfileResetWal())
        {
            ShowStatus(
                "Réinitialisation enregistrée; confirmation du backup en cours…",
                4000);
            return;
        }
    }

    private bool BeginJusticeActiveProfileResetTransaction(int slot)
    {
        if (slot != _justiceActivePlayerProfileSlot || _justiceCaseState == null)
        {
            return false;
        }
        if (_justiceActiveProfileResetPending)
        {
            return EnsureJusticeActiveProfileResetPrecommitRedundant();
        }

        string episode = _justiceCaseState.CustodyEpisodeId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(episode))
        {
            return false;
        }
        JusticeOperation operation = new JusticeOperation(
            JusticePolicy.CreateOperationId(JusticeOperationKind.ResetProfile, episode),
            JusticeOperationKind.ResetProfile,
            episode);
        if (!JusticePolicy.TryRegisterOperation(_justiceCaseState, operation))
        {
            return false;
        }

        _justiceActiveProfileResetPending = true;
        _justiceActiveProfileResetPrecommitRedundant = false;
        return EnsureJusticeActiveProfileResetPrecommitRedundant();
    }

    private bool EnsureJusticeActiveProfileResetPrecommitRedundant()
    {
        if (!_justiceActiveProfileResetPending)
        {
            return false;
        }
        if (_justiceActiveProfileResetPrecommitRedundant)
        {
            return true;
        }

        // Je conserve le WAL en mémoire sur tout échec : le premier flush a pu
        // atteindre le primaire avant que la copie redondante échoue. Aucun effet
        // monde ne part tant que primaire et backup ne portent pas l'intention.
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        _justiceActiveProfileResetPrecommitRedundant = true;
        return true;
    }

    private bool ResumeJusticeActiveProfileResetTransaction()
    {
        if (!_justiceActiveProfileResetPending)
        {
            return true;
        }
        if (!IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) ||
            _justiceVoluntaryFinePaymentIntent != null ||
            _justiceFineDebitIntent != null)
        {
            return false;
        }
        if (!EnsureJusticeActiveProfileResetPrecommitRedundant())
        {
            return false;
        }
        if (!EnsureJusticeDeathFrontsDurableBeforeDestructiveTransaction())
        {
            return false;
        }
        if (_justicePursuitDeathObservedDuringSuspension)
        {
            // Je ne remplace le profil qu'après la preuve redondante du front et
            // du reset; un crash reprendra alors le reset sans ressusciter la mort.
            ClearPendingJusticeDeathCapture();
            JusticeMarkStateDirty();
        }

        int slot = _justiceActivePlayerProfileSlot;
        if (!EnsureJusticeActiveProfileResetPlayerIsMortal(slot) ||
            !JusticeAmnestyCustody())
        {
            return false;
        }

        if (!ReplaceJusticePlayerProfileWithEmptyState(slot))
        {
            return false;
        }

        // Je journalise l'effacement du module séparé avant de publier le
        // profil Justice terminal. Une coupure entre les deux XML reste ainsi
        // rejouable sans ressusciter les anciens indices.
        if (!ClearJusticeRecognitionProfile(
            slot,
            "réinitialisation explicite du profil confirmée"))
        {
            ShowStatus(
                "Réinitialisation préparée; journal de reconnaissance à reprendre…",
                4200);
            return false;
        }

        // Le remplacement retire naturellement l'opération ResetProfile. Tant
        // que ce dossier vide n'est pas durable, le latch runtime reste actif :
        // un retry ne rejoue aucun effet irréversible, et un crash relit le WAL.
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            ShowStatus("Réinitialisation appliquée; validation durable à reprendre…", 3800);
            return false;
        }

        _justiceActiveProfileResetPending = false;
        _justiceActiveProfileResetPrecommitRedundant = false;
        ShowStatus(GetJusticeProfileDisplayName(slot) + " : profil Justice réinitialisé.", 4200);
        LogInfo("Justice.Profil", "Réinitialisation transactionnelle du profil actif terminée.");
        return true;
    }

    private static bool HasPendingJusticeProfileResetOperation(JusticeCaseState state)
    {
        if (state == null)
        {
            return false;
        }

        string prefix = JusticeOperationKind.ResetProfile.ToString() + ":";
        for (int index = 0; index < state.CompletedOperationIds.Count; index++)
        {
            string operationId = state.CompletedOperationIds[index] ?? string.Empty;
            if (operationId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private int GetJusticeMenuSelectedProfileSlot()
    {
        if (!IsJusticeCanonicalProfileSlot(_justiceMenuSelectedProfileSlot))
        {
            _justiceMenuSelectedProfileSlot =
                IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                    ? _justiceActivePlayerProfileSlot
                    : 0;
        }
        return _justiceMenuSelectedProfileSlot;
    }

    private JusticeCaseState GetJusticeProfileCaseForDisplay(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            slot = IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : 0;
        }
        if (slot == _justiceActivePlayerProfileSlot && _justiceCaseState != null)
        {
            return _justiceCaseState;
        }
        EnsureJusticePlayerProfilesInitialized();
        return _justicePlayerProfiles[slot].CaseState;
    }

    private JusticeRecordState GetJusticeProfileRecordForDisplay(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            slot = IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : 0;
        }
        if (slot == _justiceActivePlayerProfileSlot && _justiceRecordState != null)
        {
            return _justiceRecordState;
        }
        EnsureJusticePlayerProfilesInitialized();
        return _justicePlayerProfiles[slot].RecordState;
    }

    private JusticeCaseState GetJusticeMenuSelectedCaseState()
    {
        return GetJusticeProfileCaseForDisplay(GetJusticeMenuSelectedProfileSlot());
    }

    private JusticeRecordState GetJusticeMenuSelectedRecordState()
    {
        return GetJusticeProfileRecordForDisplay(GetJusticeMenuSelectedProfileSlot());
    }

    private string GetJusticeMenuSelectedStatusDisplay()
    {
        int slot = GetJusticeMenuSelectedProfileSlot();
        JusticeCaseState state = GetJusticeProfileCaseForDisplay(slot);

        if (slot == _justiceActivePlayerProfileSlot)
        {
            return GetJusticeStatusDisplay();
        }

        if (state == null)
        {
            return "Désactivée";
        }

        if (!state.Enabled)
        {
            return IsLoadedJusticeCaseActive(state)
                ? "Désactivée · dossier conservé"
                : "Désactivée";
        }

        if (IsJusticeCustodyPhase(state.Phase))
        {
            return "En détention";
        }

        if (!IsLoadedJusticeCaseActive(state))
        {
            return "Aucun dossier";
        }

        return state.HasWarrant
            ? "Recherché sous mandat"
            : "Dossier actif";
    }

    private string GetJusticeMenuSelectedLastCrimeDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return state == null || string.IsNullOrWhiteSpace(state.LastCrimeLabel)
            ? "Aucune"
            : state.LastCrimeLabel;
    }

    private string GetJusticeMenuSelectedSeverityDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return JusticeSeverityDisplayName(
            JusticePolicy.GetSeverity(state == null ? 0 : state.ActiveScore));
    }

    private string GetJusticeMenuSelectedWarrantDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return state != null && state.HasWarrant ? "ACTIF" : "Aucun";
    }

    private string GetJusticeMenuSelectedChargesDisplay()
    {
        return JusticePolicy.GetRepresentedChargeCount(GetJusticeMenuSelectedCaseState())
            .ToString(CultureInfo.InvariantCulture);
    }

    private string GetJusticeMenuSelectedFineDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        if (state == null || state.FineInDispute <= 0L)
        {
            return FormatJusticeMoney(state == null ? 0L : state.FineDue);
        }

        return FormatJusticeMoney(state.FineDue) + " · litige " +
               FormatJusticeMoney(state.FineInDispute);
    }

    private string GetJusticeMenuSelectedSentenceDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return FormatJusticeDuration(GetJusticeCustodyTotalRemainingSeconds(state));
    }

    private string GetJusticeMenuSelectedRecidivismDisplay()
    {
        JusticeRecordState state = GetJusticeMenuSelectedRecordState();
        int value = state == null ? 0 : state.RecidivismIndex;
        return "R " + value.ToString(CultureInfo.InvariantCulture) + "/100";
    }

    private int GetJusticeMenuSelectedConvictionCount()
    {
        JusticeRecordState state = GetJusticeMenuSelectedRecordState();
        return state == null ? 0 : state.Convictions.Count;
    }

    internal JusticePlayerProfileState GetJusticePlayerProfileForSlot(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            return null;
        }

        EnsureJusticePlayerProfilesInitialized();
        if (slot == _justiceActivePlayerProfileSlot)
        {
            SnapshotActiveJusticePlayerProfile();
        }
        return _justicePlayerProfiles[slot];
    }

    internal bool ResetJusticePlayerProfile(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            return false;
        }

        EnsureJusticePlayerProfilesInitialized();
        JusticePlayerProfileState target = _justicePlayerProfiles[slot];
        if (slot == _justiceActivePlayerProfileSlot)
        {
            SnapshotActiveJusticePlayerProfile();
            target = _justicePlayerProfiles[slot];
        }

        // Je refuse toute remise a zero susceptible de supprimer le seul snapshot
        // d'armes encore recuperable, meme lorsque le profil vise est inactif.
        if (HasOpenJusticeProfileResetWal() ||
            HasOpenJusticeDeathFrontForProfileSlot(slot) ||
            HasJusticeProfileCustodyRecovery(target) ||
            slot != _justiceActivePlayerProfileSlot && HasJusticeCustodyRecoveryState())
        {
            return false;
        }

        return ReplaceJusticePlayerProfileWithEmptyState(slot);
    }

    private bool ReplaceJusticePlayerProfileWithEmptyState(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            return false;
        }

        if (!EnsureJusticeActiveProfileResetPlayerIsMortal(slot))
        {
            // Je ne publie jamais le profil vide tant que le héros réellement
            // propriétaire du reset conserve encore une protection résiduelle.
            return false;
        }

        // Cette primitive ne vérifie volontairement plus le snapshot : son seul
        // appel transactionnel arrive après JusticeAmnestyCustody, qui a déjà
        // restauré et validé l'inventaire sous le WAL ResetProfile. Les autres
        // appels passent d'abord par ResetJusticePlayerProfile et son garde-fou.
        JusticePlayerProfileState replacement = new JusticePlayerProfileState(slot);
        replacement.CustodyXml = CreateCanonicalEmptyJusticeCustodyXml();
        _justicePlayerProfiles[slot] = replacement;

        if (slot == _justiceActivePlayerProfileSlot)
        {
            ResetJusticeCustodyPersistentFields(false);
            _justiceCaseState = replacement.CaseState;
            _justiceRecordState = replacement.RecordState;
            _justiceEnabled = false;
            _justicePursuitDeathObservedDuringSuspension = false;
            _justiceSuspendedPursuitDeathPlayerSlot = -1;
            _justiceSuspendedPursuitDeathPlayerModelHash = 0;
            _justiceAmnestyPending = false;
            _justiceAmnestyPrecommitRedundant = false;
            _justiceLegalReleaseFinalizationPending = false;
            _justiceLegalReleaseFinalizationSite = JusticeCustodySite.None;
            _justiceLegalReleaseSelectedWeaponHash = JusticeUnarmedHash;
            _justiceLegalReleaseWantedClearAttempted = false;
            _justiceAmnestyWantedClearAttempted = false;
            _justiceCustodyTransferRollbackFinalizationPending = false;
            _justiceLastCanonicalPlayerSlot = slot;
            _justiceLastCanonicalPlayerModelHash = 0;
            ResetJusticeRuntimeFrontsForProfileChange();
        }

        JusticeMarkStateDirty();
        return true;
    }

    private static bool IsJusticeCanonicalProfileSlot(int slot)
    {
        return slot >= 0 && slot < JusticePlayerProfileCount;
    }

    private void EnsureJusticePlayerProfilesInitialized()
    {
        if (_justicePlayerProfiles == null ||
            _justicePlayerProfiles.Length != JusticePlayerProfileCount)
        {
            JusticePlayerProfileState[] profiles =
                new JusticePlayerProfileState[JusticePlayerProfileCount];
            for (int slot = 0; slot < profiles.Length; slot++)
            {
                profiles[slot] = new JusticePlayerProfileState(slot)
                {
                    CustodyXml = CreateCanonicalEmptyJusticeCustodyXml()
                };
            }
            _justicePlayerProfiles = profiles;
        }

        for (int slot = 0; slot < _justicePlayerProfiles.Length; slot++)
        {
            JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
            if (profile == null || profile.Slot != slot)
            {
                profile = new JusticePlayerProfileState(slot);
                _justicePlayerProfiles[slot] = profile;
            }
            if (profile.CaseState == null)
            {
                profile.CaseState = new JusticeCaseState();
            }
            if (profile.RecordState == null)
            {
                profile.RecordState = new JusticeRecordState();
            }
            if (string.IsNullOrWhiteSpace(profile.CustodyXml))
            {
                profile.CustodyXml = CreateCanonicalEmptyJusticeCustodyXml();
            }
        }
    }

    private int GetJusticeCanonicalPlayerSlotSafe()
    {
        try
        {
            if (_justiceCanonicalPlayerSlotOverride != null)
            {
                int overridden = _justiceCanonicalPlayerSlotOverride();
                return IsJusticeCanonicalProfileSlot(overridden) ? overridden : -1;
            }
        }
        catch
        {
            return -1;
        }

        int slot = GetCurrentSinglePlayerCashSlotSafe();
        return IsJusticeCanonicalProfileSlot(slot) ? slot : -1;
    }

    private bool IsJusticeActiveProfileResetContextSafe(int profileSlot)
    {
        return profileSlot == _justiceActivePlayerProfileSlot &&
               !_justiceProfileSelectionPending &&
               !_justiceProfileContextBlocked &&
               !_justiceProfileSwitchPersistencePending &&
               GetJusticeCanonicalPlayerSlotSafe() == profileSlot;
    }

    private bool IsJusticeActiveProfileResetPlayerIdentitySafe(int profileSlot)
    {
        if (!IsJusticeActiveProfileResetContextSafe(profileSlot))
        {
            return false;
        }

        try
        {
            Ped player = Game.Player.Character;
            return Entity.Exists(player) && !player.IsDead &&
                   GetJusticePedModelHashSafe(player) != 0;
        }
        catch
        {
            return false;
        }
    }

    private bool EnsureJusticeActiveProfileResetPlayerIsMortal(int profileSlot)
    {
        if (profileSlot != _justiceActivePlayerProfileSlot)
        {
            // Un WAL d'un autre profil reste une mutation de données pure : je ne
            // touche jamais au ped actuellement joué pour acquitter ce reset.
            return true;
        }

        if (!IsJusticeActiveProfileResetContextSafe(profileSlot))
        {
            return false;
        }
        if (_justicePlayerMortalityVerificationOverride != null)
        {
            // Je réserve ce point d'injection aux harness hors moteur. Le runtime
            // réel laisse toujours ce delegate nul et passe par le ped GTA exact.
            try
            {
                return _justicePlayerMortalityVerificationOverride(profileSlot);
            }
            catch
            {
                return false;
            }
        }

        if (!IsJusticeActiveProfileResetPlayerIdentitySafe(profileSlot))
        {
            return false;
        }

        try
        {
            // Je termine aussi un éventuel masque Justice dont les indicateurs
            // custody auraient été perdus; un autre propriétaire (placement)
            // reste respecté et maintient le reset en attente.
            return ReleaseJusticePreJudgmentInvincibilityAsMortal(
                Game.Player.Character);
        }
        catch
        {
            return false;
        }
    }

    private bool IsJusticeRuntimeProfileContextCompatible()
    {
        if (_justiceProfileSelectionPending)
        {
            // Je refuse d'attribuer le ped custom vu au démarrage au dernier
            // profil écrit dans le XML. Une transformation reste compatible
            // uniquement après qu'un héros canonique a été prouvé dans cette
            // session et que ce verrou a été levé.
            return false;
        }

        int currentSlot = GetJusticeCanonicalPlayerSlotSafe();
        if (!IsJusticeCanonicalProfileSlot(currentSlot))
        {
            // Une transformation Iron Man/custom peut masquer le slot sans
            // changer de protagoniste. Je conserve seulement le dernier profil
            // déjà prouvé ; au démarrage sans preuve, toute mutation reste gelée.
            return IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot);
        }

        return currentSlot == _justiceActivePlayerProfileSlot;
    }

    private bool IsJusticePlayedProfileContextReady()
    {
        return !_justiceProfileContextBlocked &&
               !_justiceProfileSelectionPending &&
               !_justiceProfileSwitchPersistencePending &&
               IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) &&
               IsJusticeRuntimeProfileContextCompatible();
    }

    private bool IsJusticePlayedProfileCustodyContextReady()
    {
        if (!JusticeIsCustodyActive || !IsJusticePlayedProfileContextReady())
        {
            return false;
        }

        // Je n'affiche jamais une détention sans propriétaire canonique prouvé.
        // Un slot indéterminé pendant une capture reste masqué jusqu'à sa liaison.
        if (!IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) ||
            _justiceCustodyPlayerSlot != _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        try
        {
            Ped player = Game.Player.Character;
            if (!Entity.Exists(player) || player.IsDead ||
                IsJusticeCustodyRuntimeSuspended(
                    player,
                    _justiceRuntimeSuspendedCached))
            {
                return false;
            }

            // Je contrôle le ped vivant au moment exact du rendu : un retour
            // anticipé du runtime ou un slot custom ne peut ainsi conserver le
            // bandeau de l'ancien héros. Cette lecture ne relie aucune identité.
            int currentSlot = GetJusticeCanonicalPlayerSlotSafe();
            int currentModelHash = GetJusticePedModelHashSafe(player);
            return JusticePolicy.IsCustodyLiveIdentityCompatible(
                _justiceCustodyPlayerSlot,
                currentSlot,
                _justiceCustodyPlayerHandle,
                player.Handle,
                _justiceCustodyPlayerModelHash,
                currentModelHash);
        }
        catch
        {
            return false;
        }
    }

    private void CaptureJusticeDangerActionIdentity(
        out int playerHandle,
        out int playerModelHash)
    {
        playerHandle = 0;
        playerModelHash = 0;
        try
        {
            Ped player = Game.Player.Character;
            if (!Entity.Exists(player) || player.IsDead)
            {
                return;
            }

            playerHandle = player.Handle;
            playerModelHash = GetJusticePedModelHashSafe(player);
        }
        catch
        {
            playerHandle = 0;
            playerModelHash = 0;
        }
    }

    private bool IsJusticeDangerActionProfileContextValid(
        int requestedProfileSlot,
        int expectedPlayerHandle,
        int expectedPlayerModelHash)
    {
        if (!IsJusticePlayedProfileContextReady() ||
            !IsJusticeCanonicalProfileSlot(requestedProfileSlot) ||
            requestedProfileSlot != _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        int currentSlot = GetJusticeCanonicalPlayerSlotSafe();
        if (IsJusticeCanonicalProfileSlot(currentSlot))
        {
            return currentSlot == requestedProfileSlot;
        }

        // Une armure Iron Man ou un ped custom masque parfois le slot GTA. Je
        // l'accepte seulement si le héros canonique avait déjà été prouvé et si
        // le même ped (handle + modèle disponible) confirme la modale.
        if (_justiceLastCanonicalPlayerSlot != requestedProfileSlot ||
            expectedPlayerHandle <= 0)
        {
            return false;
        }

        try
        {
            Ped player = Game.Player.Character;
            if (!Entity.Exists(player) || player.IsDead ||
                player.Handle != expectedPlayerHandle)
            {
                return false;
            }

            int currentModelHash = GetJusticePedModelHashSafe(player);
            return expectedPlayerModelHash == 0 || currentModelHash == 0 ||
                   currentModelHash == expectedPlayerModelHash;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeJusticePlayerProfiles()
    {
        _justiceCanonicalPlayerSlotOverride = null;
        EnsureJusticePlayerProfilesInitialized();
        _justiceActivePlayerProfileSlot = GetJusticeCanonicalPlayerSlotSafe();
        _justiceMenuSelectedProfileSlot = IsJusticeCanonicalProfileSlot(
            _justiceActivePlayerProfileSlot)
            ? _justiceActivePlayerProfileSlot
            : 0;
        _justiceProfileSelectionPending = !IsJusticeCanonicalProfileSlot(
            _justiceActivePlayerProfileSlot);
        _justiceProfileContextBlocked = _justiceProfileSelectionPending;
        _justiceProfileSwitchPersistencePending = false;
        _justiceProfileSwitchPersistenceRevision = 0L;
        _justiceProfileSwitchPersistenceWriteFailures = 0L;
        _justiceActiveProfileResetPending = false;
        _justiceActiveProfileResetPrecommitRedundant = false;
    }

    private void PrepareJusticeActiveProfileForPersistence()
    {
        // FormatterServices est utilise par les tests de persistance historiques.
        // Dans le runtime normal, le tableau est toujours initialise avant toute
        // capture. Si un ancien objet headless porte deja une identite de detention,
        // elle constitue la preuve la plus forte pour choisir son profil.
        if (_justicePlayerProfiles == null &&
            IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot))
        {
            _justiceActivePlayerProfileSlot = _justiceCustodyPlayerSlot;
        }
    }

    private bool EnsureJusticeProfileMatchesCanonicalPlayer(Ped player)
    {
        int slot = GetJusticeCanonicalPlayerSlotSafe();

        if (_justiceProfileSwitchPersistencePending)
        {
            // Je termine toujours la publication du profil déjà activé avant
            // d'interpréter un nouveau slot. Un switch rapide P -> Q -> R ne
            // peut donc ni écraser la révision de Q ni mélanger deux dossiers.
            if (!PersistPendingJusticeProfileSwitch())
            {
                return false;
            }

            if (IsJusticeCanonicalProfileSlot(slot) &&
                slot != _justiceActivePlayerProfileSlot)
            {
                // Je conserve un tick frontière après DiskRevision. Les fronts
                // du héros R ne seront lus qu'au passage suivant, quand Q est
                // déjà entièrement durable et que sa barrière est retombée.
                return false;
            }
        }

        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            // Un ped transforme (Iron Man, tenue custom) peut ne plus exposer de
            // slot. Je conserve alors le dernier profil prouve sans en adopter un.
            return !_justiceProfileSelectionPending &&
                   IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot);
        }

        if (_justiceLegacyProfileReloadPending)
        {
            // Je relis l'ancien XML seulement après apparition d'un slot GTA
            // fiable. Tant que cette migration n'aboutit pas, je n'écris rien et
            // conserve donc le fichier historique intact.
            if (!TryLoadJusticeState(false))
            {
                return false;
            }
            _justiceLegacyProfileReloadPending = false;
            _justiceProfileSelectionPending = false;
            return _justiceActivePlayerProfileSlot == slot;
        }

        if (slot == _justiceActivePlayerProfileSlot)
        {
            _justiceProfileSelectionPending = false;
            return true;
        }

        bool mustResumePoliceRestorationBarrier =
            _justiceCriticalBarrierRevision > 0L &&
            string.Equals(
                _justiceCriticalBarrierCaller,
                nameof(SetJusticeCustodyPoliceSuppression),
                StringComparison.Ordinal) &&
            (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
             _justicePoliceSuppressionActive ||
             _justicePoliceSuppressionRestorePending);
        if (!mustResumePoliceRestorationBarrier &&
            !TryRejectJusticeCriticalBarrierForProfileChange(slot))
        {
            _justiceProfileContextBlocked = true;
            return false;
        }

        int switchAt = GetJusticeRawGameTimeSafe();
        bool parkedCustody = false;
        if (HasJusticeCustodyRecoveryState())
        {
            // Je laisse uniquement une incarceration stable passer en arrière-plan.
            // Toute transaction ou restitution reste liée au bon héros.
            if (!TryPrepareJusticeCustodyForProfileSwitch(switchAt))
            {
                return false;
            }
            parkedCustody = true;
        }
        if (mustResumePoliceRestorationBarrier &&
            _justiceCriticalBarrierRevision > 0L)
        {
            // Je ne rejette jamais le WAL qui acquitte une restauration police
            // déjà tentée. Le même caller doit le terminer avant le basculement.
            _justiceProfileContextBlocked = true;
            return false;
        }

        FinalizeJusticePursuitStateBeforeProfileSwitch(GetJusticeWantedLevelSafe());
        SnapshotActiveJusticePlayerProfile();
        int previousSlot = _justiceActivePlayerProfileSlot;
        JusticePlayerProfileState targetProfile = _justicePlayerProfiles[slot];
        bool targetCanAdvanceCustodyInBackground =
            targetProfile.CanAdvanceCustodyInBackground;
        int targetSentenceSeconds = targetProfile.CaseState != null
            ? targetProfile.CaseState.SentenceSeconds
            : 0;
        long targetCustodyGuardPenaltySeconds = targetProfile.CaseState != null
            ? targetProfile.CaseState.CustodyGuardPenaltySeconds
            : 0L;
        int targetInactiveCustodyLastTickAt =
            targetProfile.InactiveCustodyLastTickAt;
        int targetInactiveCustodyElapsedRemainderMs =
            targetProfile.InactiveCustodyElapsedRemainderMs;
        bool stateDirtyBeforeTargetAdvance = _justiceStateDirty;
        long nextStateSaveAtBeforeTargetAdvance = _justiceNextStateSaveAtMs;
        AdvanceJusticeInactiveCustodyProfileClock(
            targetProfile,
            switchAt,
            false);
        if (!ActivateJusticePlayerProfile(slot))
        {
            // Je rends au profil cible ses horloges exactes puis je recharge le
            // snapshot typé du héros source. Une détention cible incohérente ne
            // peut ainsi laisser un demi-switch actif ni armer une publication.
            targetProfile.CanAdvanceCustodyInBackground =
                targetCanAdvanceCustodyInBackground;
            if (targetProfile.CaseState != null)
            {
                targetProfile.CaseState.SentenceSeconds = targetSentenceSeconds;
                targetProfile.CaseState.CustodyGuardPenaltySeconds =
                    targetCustodyGuardPenaltySeconds;
            }
            targetProfile.InactiveCustodyLastTickAt =
                targetInactiveCustodyLastTickAt;
            targetProfile.InactiveCustodyElapsedRemainderMs =
                targetInactiveCustodyElapsedRemainderMs;

            bool sourceRestored =
                IsJusticeCanonicalProfileSlot(previousSlot) &&
                previousSlot != slot &&
                ActivateJusticePlayerProfile(previousSlot);
            // NormalizeLoadedJusticeState recalcule ce cache pendant la
            // réactivation. Je rétablis ensuite le scheduling exact qui
            // précédait uniquement l'avance spéculative du profil cible.
            _justiceStateDirty = stateDirtyBeforeTargetAdvance;
            _justiceNextStateSaveAtMs = nextStateSaveAtBeforeTargetAdvance;
            _justiceProfileSwitchPersistencePending = false;
            _justiceProfileSwitchPersistenceRevision = 0L;
            _justiceProfileSwitchPersistenceWriteFailures = 0L;
            // Après un rollback je réclame une nouvelle preuve canonique. Un
            // slot -1 du ped cible ne doit surtout pas rouvrir le dossier source.
            _justiceProfileSelectionPending = true;
            _justiceProfileContextBlocked = true;
            LogWarning(
                "Justice.Profil",
                sourceRestored
                    ? "Activation du profil cible refusée; profil source restauré sans publication."
                    : "Activation du profil cible et restauration du profil source impossibles; contexte Justice maintenu bloqué.");
            return false;
        }

        ResetJusticeRuntimeFrontsForProfileChange();
        _justiceLastWantedLevel = GetJusticeWantedLevelSafe();
        ReconcileLoadedJusticePursuitState(_justiceLastWantedLevel);
        _justiceWasDead = IsJusticePlayerDeadSafe(player);
        _justiceProfileSelectionPending = false;
        _justiceProfileSwitchPersistencePending = true;
        _justiceProfileSwitchPersistenceRevision = 0L;
        _justiceProfileSwitchPersistenceWriteFailures = 0L;
        JusticeMarkStateDirty();
        if (!PersistPendingJusticeProfileSwitch())
        {
            _justiceProfileContextBlocked = true;
            return false;
        }
        _justiceProfileContextBlocked = false;
        LogInfo(
            "Justice.Profil",
            "Profil judiciaire actif=" + slot.ToString(CultureInfo.InvariantCulture) +
            (parkedCustody ? "; détention précédente mise en arrière-plan." : "."));
        return true;
    }

    private void FinalizeJusticePursuitStateBeforeProfileSwitch(int wantedLevel)
    {
        if (_justiceWantedLossPending)
        {
            ResolveDeferredJusticeWantedLoss(wantedLevel);
        }

        // Le latch peut avoir été perdu lors d'une transition GTA. La phase
        // persistée Wanted/Surrendering reste une preuve suffisante, tandis qu'un
        // dossier AtLarge ordinaire n'est jamais transformé en mandat.
        ReconcileLoadedJusticePursuitState(wantedLevel);
    }

    private bool PersistPendingJusticeProfileSwitch()
    {
        if (!_justiceProfileSwitchPersistencePending)
        {
            _justiceProfileSwitchPersistenceRevision = 0L;
            _justiceProfileSwitchPersistenceWriteFailures = 0L;
            return true;
        }

        InitializeJusticePersistenceServices();
        JusticeRepository repository = _justiceRepository;
        if (repository == null || _justicePersistenceServicesUnavailable)
        {
            return false;
        }

        bool firstEnqueue = _justiceProfileSwitchPersistenceRevision <= 0L;
        if (firstEnqueue)
        {
            long writeFailuresBeforeEnqueue =
                repository.GetDiagnostics().WriteFailures;
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
            {
                return false;
            }

            _justiceProfileSwitchPersistenceRevision =
                _justiceLastQueuedPersistenceRevision;
            _justiceProfileSwitchPersistenceWriteFailures =
                writeFailuresBeforeEnqueue;
            return false;
        }

        bool retryWasDue = _justiceNextStateFlushAttemptAtMs <= 0L ||
            _justiceMonotonicTimeMs >= _justiceNextStateFlushAttemptAtMs;
        ObserveJusticeRepositoryFailure();
        JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
        if (_justiceProfileSwitchPersistenceRevision > 0L &&
            diagnostics.DiskRevision >= _justiceProfileSwitchPersistenceRevision)
        {
            FinalizeJusticeWalTransactionsWhoseSnapshotIsDurable();
            _justiceProfileSwitchPersistencePending = false;
            _justiceProfileSwitchPersistenceRevision = 0L;
            _justiceProfileSwitchPersistenceWriteFailures = 0L;
            return true;
        }

        bool failedQueuedRevision =
            diagnostics.WriteFailures > _justiceProfileSwitchPersistenceWriteFailures;
        if (!failedQueuedRevision || !retryWasDue)
        {
            return false;
        }

        // Après un rejet du writer, je remplace son snapshot fautif par une
        // révision fraîche. Le premier enqueue reste toujours non bloquant et
        // le contexte du nouveau héros demeure gelé jusqu'à DiskRevision.
        if (failedQueuedRevision)
        {
            _justiceNextStateFlushAttemptAtMs = 0L;
        }
        long writeFailuresBeforeRetry = diagnostics.WriteFailures;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        _justiceProfileSwitchPersistenceRevision =
            _justiceLastQueuedPersistenceRevision;
        _justiceProfileSwitchPersistenceWriteFailures =
            writeFailuresBeforeRetry;
        return false;
    }

    private bool ActivateJusticePlayerProfile(int slot)
    {
        if (!IsJusticeCanonicalProfileSlot(slot))
        {
            return false;
        }

        // Je vide le retry wanted avant de charger le profil cible. Une ancienne
        // intention d'amnistie sera neutralisée juste après sa normalisation et
        // ne pourra donc jamais effacer les étoiles du héros que j'active.
        CancelJusticeWantedClearRetry();

        EnsureJusticePlayerProfilesInitialized();
        int[] repairArrestHoldingIntents =
            _justiceRepairArrestPreJudgmentHoldingModelHashes == null
                ? null
                : (int[])_justiceRepairArrestPreJudgmentHoldingModelHashes.Clone();
        JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
        profile.CanAdvanceCustodyInBackground = false;
        profile.InactiveCustodyLastTickAt = 0;
        profile.InactiveCustodyElapsedRemainderMs = 0;
        _justiceCaseState = profile.CaseState;
        _justiceRecordState = profile.RecordState;
        _justiceEnabled = _justiceCaseState.Enabled;
        _justicePursuitDeathObservedDuringSuspension = profile.PendingDeathCapture;
        _justiceSuspendedPursuitDeathPlayerSlot = profile.PendingDeathCapturePlayerSlot;
        _justiceSuspendedPursuitDeathPlayerModelHash = profile.PendingDeathCapturePlayerModel;
        _justiceAmnestyPending = profile.PendingAmnestyWantedClear;
        // Ce cache n'est jamais partagé entre héros ni persisté : un profil
        // rechargé réaffirme toujours son intention avant le moindre effet GTA.
        _justiceAmnestyPrecommitRedundant = false;
        _justiceLegalReleaseFinalizationPending = profile.PendingLegalReleaseFinalization;
        _justiceLegalReleaseFinalizationSite =
            (JusticeCustodySite)profile.PendingLegalReleaseSite;
        _justiceLegalReleaseSelectedWeaponHash =
            profile.PendingLegalReleaseFinalization
                ? profile.PendingLegalReleaseSelectedWeapon
                : JusticeUnarmedHash;
        _justiceLastCanonicalPlayerSlot = slot;
        _justiceLastCanonicalPlayerModelHash = profile.LastCanonicalPlayerModel;
        _justiceActivePlayerProfileSlot = slot;

        bool custodyRestored;
        try
        {
            custodyRestored = profile.CustodySnapshot != null
                ? RestoreJusticeCustodyPersistenceSnapshot(profile.CustodySnapshot)
                : ReadJusticeCustodyXmlFragment(profile.CustodyXml);
        }
        finally
        {
            // Le reset générique de la détention nettoie aussi le holding
            // physique singulier, ce qui est souhaité au changement de héros.
            // Je conserve en revanche les intents RepairArrest par slot : ils
            // représentent des fronts déjà prouvés qui survivent à l'aller-retour.
            _justiceRepairArrestPreJudgmentHoldingModelHashes =
                repairArrestHoldingIntents;
        }
        if (!custodyRestored)
        {
            return false;
        }

        NormalizeLoadedJusticeState();

        // Un profil inactif peut provenir d'une ancienne sauvegarde et contenir
        // son propre verrou d'amnistie. Il est neutralisé dès l'activation du
        // profil, avant toute reprise de son runtime.
        MigrateLegacyJusticeAmnestyState();

        _justicePoliceDeathRespawnMaskIntentPending =
            _justicePursuitDeathObservedDuringSuspension &&
            !JusticeIsCustodyActive;
        _justiceActiveProfileResetPending =
            HasPendingJusticeProfileResetOperation(_justiceCaseState);
        // Ce cache est volontairement runtime : après chaque chargement, je
        // réaffirme le WAL dans le primaire et le backup avant tout effet monde.
        _justiceActiveProfileResetPrecommitRedundant = false;
        _justiceDamageFrontPrimingPending = _justiceEnabled;
        SynchronizeJusticeRecognition(true);
        return true;
    }

    private void SnapshotActiveJusticePlayerProfile()
    {
        if (!IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) ||
            _justiceCaseState == null || _justiceRecordState == null)
        {
            return;
        }

        EnsureJusticePlayerProfilesInitialized();
        _justiceLastCanonicalPlayerSlot = _justiceActivePlayerProfileSlot;
        JusticePlayerProfileState profile =
            _justicePlayerProfiles[_justiceActivePlayerProfileSlot];
        profile.CaseState = _justiceCaseState;
        profile.RecordState = _justiceRecordState;
        // Je conserve un graphe typé profondément détaché. La matérialisation XML
        // est réservée au worker de persistance et ne bloque plus le thread GTA.
        JusticeCustodyPersistenceSnapshot capturedCustody =
            CaptureJusticeCustodyPersistenceSnapshot();
        int policyRecoveryBit = 1 << _justiceActivePlayerProfileSlot;
        if ((_justicePolicyResetRecoveryMask & policyRecoveryBit) != 0)
        {
            // Je ne remplace jamais le dernier jeton durable par un snapshot
            // momentanément vide entre deux étapes physiques. Si un retry
            // courant expose encore un état récupérable, je le réduis au même
            // contrat technique sans réintroduire de donnée judiciaire legacy.
            JusticeCustodyPersistenceSnapshot refreshedRecovery =
                CreateJusticeSentencePolicyRecoveryToken(
                    capturedCustody,
                    _justiceActivePlayerProfileSlot);
            if (refreshedRecovery != null)
            {
                profile.CustodySnapshot = refreshedRecovery;
            }
        }
        else
        {
            profile.CustodySnapshot = capturedCustody;
        }
        profile.PendingDeathCapture = _justicePursuitDeathObservedDuringSuspension;
        profile.PendingDeathCapturePlayerSlot =
            _justiceSuspendedPursuitDeathPlayerSlot;
        profile.PendingDeathCapturePlayerModel =
            _justiceSuspendedPursuitDeathPlayerModelHash;
        profile.PendingAmnestyWantedClear = _justiceAmnestyPending;
        profile.PendingLegalReleaseFinalization =
            _justiceLegalReleaseFinalizationPending;
        profile.PendingLegalReleaseSite =
            (int)_justiceLegalReleaseFinalizationSite;
        profile.PendingLegalReleaseSelectedWeapon =
            _justiceLegalReleaseFinalizationPending
                ? _justiceLegalReleaseSelectedWeaponHash
                : 0;
        profile.LastCanonicalPlayerModel = _justiceLastCanonicalPlayerModelHash;
        profile.CanAdvanceCustodyInBackground =
            CanAdvanceCurrentJusticeCustodyInBackground();
        if (!profile.CanAdvanceCustodyInBackground)
        {
            profile.InactiveCustodyLastTickAt = 0;
            profile.InactiveCustodyElapsedRemainderMs = 0;
        }
    }

    private void AdvanceJusticeInactiveCustodyProfiles(int now, bool suspended)
    {
        EnsureJusticePlayerProfilesInitialized();
        for (int slot = 0; slot < _justicePlayerProfiles.Length; slot++)
        {
            JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
            if (slot == _justiceActivePlayerProfileSlot)
            {
                profile.InactiveCustodyLastTickAt = 0;
                profile.InactiveCustodyElapsedRemainderMs = 0;
                continue;
            }

            AdvanceJusticeInactiveCustodyProfileClock(profile, now, suspended);
        }
    }

    private bool AdvanceJusticeInactiveCustodyProfileClock(
        JusticePlayerProfileState profile,
        int now,
        bool suspended)
    {
        if (!IsJusticeInactiveCustodyProfileClockEligible(profile))
        {
            if (profile != null)
            {
                profile.InactiveCustodyLastTickAt = 0;
                profile.InactiveCustodyElapsedRemainderMs = 0;
            }
            return false;
        }

        int lastTickAt = profile.InactiveCustodyLastTickAt;
        int remainderMs = profile.InactiveCustodyElapsedRemainderMs;
        long previousTotal = GetJusticeCustodyTotalRemainingSeconds(
            profile.CaseState);
        long nextTotal = AdvanceJusticeInactiveCustodyTotalClock(
            previousTotal,
            now,
            ref lastTickAt,
            ref remainderMs,
            suspended);
        profile.InactiveCustodyLastTickAt = lastTickAt;
        profile.InactiveCustodyElapsedRemainderMs = remainderMs;
        if (nextTotal == previousTotal)
        {
            return false;
        }

        long elapsedSeconds = previousTotal - nextTotal;
        ConsumeJusticeCustodySentenceSeconds(
            profile.CaseState,
            (int)Math.Min(int.MaxValue, Math.Max(0L, elapsedSeconds)));
        if (nextTotal <= 0L)
        {
            // Je conserve la phase et le snapshot : seule la reprise du bon héros
            // finalisera la libération et la restitution de son inventaire.
            profile.CanAdvanceCustodyInBackground = false;
            profile.InactiveCustodyLastTickAt = 0;
            profile.InactiveCustodyElapsedRemainderMs = 0;
        }
        JusticeMarkStateDirty();
        return true;
    }

    private static bool IsJusticeInactiveCustodyProfileClockEligible(
        JusticePlayerProfileState profile)
    {
        return profile != null && profile.CanAdvanceCustodyInBackground &&
               profile.CaseState != null && profile.CaseState.Enabled &&
               profile.CaseState.Phase == JusticePhase.Incarcerated &&
               GetJusticeCustodyTotalRemainingSeconds(profile.CaseState) > 0L &&
               !profile.PendingDeathCapture &&
               !profile.PendingAmnestyWantedClear &&
               !profile.PendingLegalReleaseFinalization &&
               !HasPendingJusticeProfileResetOperation(profile.CaseState);
    }

    internal static int AdvanceJusticeInactiveCustodySentenceClock(
        int sentenceSeconds,
        int now,
        ref int lastTickAt,
        ref int elapsedRemainderMs,
        bool suspended)
    {
        return (int)AdvanceJusticeInactiveCustodyTotalClock(
            Math.Max(0, sentenceSeconds),
            now,
            ref lastTickAt,
            ref elapsedRemainderMs,
            suspended);
    }

    internal static long AdvanceJusticeInactiveCustodyTotalClock(
        long remainingSeconds,
        int now,
        ref int lastTickAt,
        ref int elapsedRemainderMs,
        bool suspended)
    {
        long boundedSentence = Math.Max(0L, remainingSeconds);
        if (boundedSentence <= 0)
        {
            lastTickAt = 0;
            elapsedRemainderMs = 0;
            return 0L;
        }
        if (suspended)
        {
            lastTickAt = now;
            elapsedRemainderMs = 0;
            return boundedSentence;
        }
        if (lastTickAt == 0)
        {
            lastTickAt = now;
            elapsedRemainderMs = 0;
            return boundedSentence;
        }

        uint rawElapsed = unchecked((uint)(now - lastTickAt));
        lastTickAt = now;
        elapsedRemainderMs += (int)Math.Min(
            (uint)JusticeCustodyMaxFrameElapsedMs,
            rawElapsed);
        int elapsedSeconds = elapsedRemainderMs / 1000;
        if (elapsedSeconds <= 0)
        {
            return boundedSentence;
        }

        elapsedRemainderMs %= 1000;
        return Math.Max(0L, boundedSentence - elapsedSeconds);
    }

    private void ResetJusticeRuntimeFrontsForProfileChange()
    {
        // F10 ne met pas le monde en pause : un changement de héros entre les
        // deux validations annule toute action destructive préparée pour l'ancien.
        CancelPendingDangerAction();
        CancelJusticeWantedClearRetry();
        // Je retire uniquement un message porté par Justice. Le bandeau global
        // peut aussi appartenir au téléphone, au Cartel ou aux Ballas et leur
        // statut ne doit pas disparaître à cause d'un changement de héros.
        if (_statusOwnedByJusticeProfile ||
            IsJusticeProfileScopedStatus(_statusText))
        {
            _statusText = string.Empty;
            _statusUntil = 0;
        }
        _statusOwnedByJusticeProfile = false;
        FlushJusticeConsumedDamageFronts();
        _justicePendingIncidents.Clear();
        _justiceRecentVictims.Clear();
        _justiceRecentVehicles.Clear();
        _justiceAllyTokens.Clear();
        _justiceTrackedIdentities.Clear();
        _justiceSelfDefenseUntilByVictim.Clear();
        _justiceSelfDefenseThreatByVictim.Clear();
        _justiceWitnessSnapshotCount = 0;
        _justiceDamageFrontCount = 0;
        _justiceDamagePairBaselineCount = 0;
        _justiceDamagePairReplacementIndex = 0;
        _justicePursuitActive = false;
        _justiceWantedLossPending = false;
        _justiceWantedEpisodeStartedAtMs = 0L;
        _justiceCaptureRetryPending = false;
        _justiceCaptureRetryDeath = false;
        _justiceArrestCompletionProbePending = false;
        _justiceWantedRisePendingCorrelation = false;
        _justiceAimTargetHandle = 0;
        _justiceAimTargetGeneration = 0;
        _justiceAimStartedAtMs = 0L;
        _justiceAimThreatQueued = false;
        _justiceRecognitionCandidateHandle = 0;
        _justiceRecognitionCandidateGeneration = 0;
        _justiceRecognitionStartedAtMs = 0L;
        _justiceLastCleanAdvanceAtMs = 0L;
        _justiceCleanCarryMilliseconds = 0L;
        _justiceDetectionEpisodeId = _justiceCaseState == null
            ? string.Empty
            : (_justiceCaseState.WantedEpisodeId ?? string.Empty);
        _justiceDamageFrontPrimingPending = _justiceEnabled;
        _justiceDeathDetectionBarrierInitialized = false;
    }

    private static bool IsJusticeProfileScopedStatus(string statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return false;
        }

        if (statusText.IndexOf("Justice", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return statusText.StartsWith("Amnistie", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Activation", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Désactivation", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Paiement", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Évasion", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Réinitialisation", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Commissariat", StringComparison.OrdinalIgnoreCase) ||
               statusText.StartsWith("Prison de Bolingbroke", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowJusticeProfileStatus(string text, int milliseconds)
    {
        ShowStatus(text, milliseconds);
        _statusOwnedByJusticeProfile = true;
    }

    private string CaptureCurrentJusticeCustodyXml()
    {
        StringBuilder buffer = new StringBuilder(2048);
        XmlWriterSettings settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Document,
            Encoding = new UTF8Encoding(false)
        };
        using (XmlWriter writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartElement("ProfileCustody");
            JusticeWriteCustodyXml(writer);
            writer.WriteEndElement();
        }

        XmlDocument document = LoadJusticeXmlFragment(buffer.ToString());
        XmlElement custody = document.DocumentElement == null
            ? null
            : document.DocumentElement.SelectSingleNode("Custody") as XmlElement;
        return custody == null ? CreateCanonicalEmptyJusticeCustodyXml() : custody.OuterXml;
    }

    private string CaptureCurrentJusticeCustodyXmlSafe()
    {
        try
        {
            return CaptureCurrentJusticeCustodyXml();
        }
        catch
        {
            return CreateCanonicalEmptyJusticeCustodyXml();
        }
    }

    private bool ReadJusticeCustodyXmlFragment(string custodyXml)
    {
        string fragment = string.IsNullOrWhiteSpace(custodyXml)
            ? CreateCanonicalEmptyJusticeCustodyXml()
            : custodyXml;
        XmlDocument document = LoadJusticeXmlFragment(
            "<ProfileCustody>" + fragment + "</ProfileCustody>");
        return JusticeReadCustodyXml(document.DocumentElement);
    }

    private bool TryHydrateJusticeV2CustodySnapshots(
        JusticePlayerProfileState[] profiles,
        int sentencePolicyRecoveryMask)
    {
        if (profiles == null || profiles.Length != JusticePlayerProfileCount)
        {
            return false;
        }

        JusticeCaseState previousCase = _justiceCaseState;
        JusticeRecordState previousRecord = _justiceRecordState;
        string previousCustody = CaptureCurrentJusticeCustodyXmlSafe();
        bool hydrated = true;
        bool restored = true;
        try
        {
            for (int slot = 0; slot < profiles.Length; slot++)
            {
                JusticePlayerProfileState profile = profiles[slot];
                if (profile == null || profile.CaseState == null ||
                    profile.RecordState == null)
                {
                    hydrated = false;
                    break;
                }

                _justiceCaseState = profile.CaseState;
                _justiceRecordState = profile.RecordState;
                XmlElement custody = LoadJusticeXmlFragment(
                    profile.CustodyXml).DocumentElement;
                bool policyRecoveryExpected =
                    (sentencePolicyRecoveryMask & (1 << slot)) != 0;
                if (custody == null)
                {
                    hydrated = false;
                    break;
                }

                if (policyRecoveryExpected)
                {
                    JusticeCustodyPersistenceSnapshot policyRecovery;
                    if (!TryReadJusticeSentencePolicyRecoveryCustody(
                            custody,
                            slot,
                            out policyRecovery))
                    {
                        hydrated = false;
                        break;
                    }
                    profile.CustodySnapshot = policyRecovery;
                }
                else
                {
                    if (!ReadJusticeCustodyXmlFragment(profile.CustodyXml))
                    {
                        hydrated = false;
                        break;
                    }
                    profile.CustodySnapshot =
                        CaptureLoadedJusticeCustodyPersistenceSnapshot(
                            custody.SelectSingleNode("ActivityCooldowns") != null);
                }
            }
        }
        catch
        {
            hydrated = false;
        }
        finally
        {
            _justiceCaseState = previousCase;
            _justiceRecordState = previousRecord;
            try
            {
                restored = ReadJusticeCustodyXmlFragment(previousCustody);
            }
            catch
            {
                restored = false;
            }
        }

        return hydrated && restored;
    }

    private static XmlDocument LoadJusticeXmlFragment(string xml)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
        XmlDocument document = new XmlDocument { XmlResolver = null };
        using (StringReader source = new StringReader(xml ?? string.Empty))
        using (XmlReader reader = XmlReader.Create(source, settings))
        {
            document.Load(reader);
        }
        return document;
    }

    private static string CreateCanonicalEmptyJusticeCustodyXml()
    {
        return "<Custody active=\"false\" site=\"None\" " +
               "policeSuppressionApplied=\"false\" policeDispatchDisabled=\"false\" " +
               "initialSentenceSeconds=\"0\" " +
               "inventoryRemoved=\"false\" weaponControlsLocked=\"false\" " +
               "deferredInventoryRestore=\"false\" waitingForRespawn=\"false\" " +
               "deathRebindPending=\"false\" playerStateStored=\"false\" " +
               "storedInvincible=\"false\" storedFrozen=\"false\" " +
               "storedCanRagdoll=\"true\" playerModelHash=\"0\" playerSlot=\"-1\" " +
               "releaseSelectedWeapon=\"-1569615261\" />";
    }

    private static void WriteJusticeCustodyXmlFragment(XmlWriter writer, string custodyXml)
    {
        XmlDocument document = LoadJusticeXmlFragment(
            string.IsNullOrWhiteSpace(custodyXml)
                ? CreateCanonicalEmptyJusticeCustodyXml()
                : custodyXml);
        if (document.DocumentElement == null ||
            !string.Equals(document.DocumentElement.Name, "Custody", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fragment de detention Justice invalide.");
        }
        document.DocumentElement.WriteTo(writer);
    }

    private void WriteJusticePlayerProfilesXml(XmlWriter writer)
    {
        EnsureJusticePlayerProfilesInitialized();
        writer.WriteStartElement("PlayerProfiles");
        for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
        {
            JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
            writer.WriteStartElement("Profile");
            writer.WriteAttributeString("slot", slot.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "pendingDeathCapture",
                profile.PendingDeathCapture ? "true" : "false");
            writer.WriteAttributeString(
                "pendingDeathCapturePlayerSlot",
                profile.PendingDeathCapturePlayerSlot.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "pendingDeathCapturePlayerModel",
                profile.PendingDeathCapturePlayerModel.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "pendingAmnestyWantedClear",
                profile.PendingAmnestyWantedClear ? "true" : "false");
            writer.WriteAttributeString(
                "pendingLegalReleaseFinalization",
                profile.PendingLegalReleaseFinalization ? "true" : "false");
            writer.WriteAttributeString(
                "pendingLegalReleaseSite",
                profile.PendingLegalReleaseSite.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "pendingLegalReleaseSelectedWeapon",
                profile.PendingLegalReleaseSelectedWeapon.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "lastCanonicalPlayerModel",
                profile.LastCanonicalPlayerModel.ToString(CultureInfo.InvariantCulture));
            WriteJusticeCaseXml(writer, profile.CaseState);
            WriteJusticeRecordXml(writer, profile.RecordState);
            if (profile.CustodySnapshot != null)
            {
                WriteJusticeCustodyPersistenceXml(writer, profile.CustodySnapshot);
            }
            else
            {
                WriteJusticeCustodyXmlFragment(writer, profile.CustodyXml);
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static bool TryReadJusticePlayerProfilesXml(
        XmlElement root,
        out JusticePlayerProfileState[] profiles,
        out int persistedActiveSlot,
        out bool hasProfiles,
        int sentencePolicyRecoveryMask)
    {
        profiles = null;
        persistedActiveSlot = -1;
        hasProfiles = false;
        if (root == null)
        {
            return false;
        }

        XmlNodeList containers = root.SelectNodes("PlayerProfiles");
        if (containers == null || containers.Count == 0)
        {
            return true;
        }
        if (containers.Count != 1 ||
            !TryReadJusticeIntStrict(root, "activePlayerSlot", -1, 0, 2, out persistedActiveSlot))
        {
            return false;
        }

        XmlElement container = containers[0] as XmlElement;
        XmlNodeList nodes = container == null ? null : container.SelectNodes("Profile");
        if (nodes == null || nodes.Count != JusticePlayerProfileCount)
        {
            return false;
        }

        JusticePlayerProfileState[] loaded =
            new JusticePlayerProfileState[JusticePlayerProfileCount];
        for (int index = 0; index < nodes.Count; index++)
        {
            XmlElement element = nodes[index] as XmlElement;
            int slot;
            bool pendingDeath;
            int pendingSlot;
            int pendingModel;
            bool pendingAmnesty;
            bool pendingLegalRelease;
            int pendingLegalReleaseSite;
            int pendingLegalReleaseSelectedWeapon;
            int lastModel;
            if (element == null ||
                !TryReadJusticeIntStrict(element, "slot", -1, 0, 2, out slot) ||
                loaded[slot] != null ||
                !TryReadJusticeBoolStrict(element, "pendingDeathCapture", false, out pendingDeath) ||
                !TryReadJusticeIntStrict(
                    element,
                    "pendingDeathCapturePlayerSlot",
                    -1,
                    -1,
                    2,
                    out pendingSlot) ||
                !TryReadJusticeIntStrict(
                    element,
                    "pendingDeathCapturePlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out pendingModel) ||
                !TryReadJusticeBoolStrict(
                    element,
                    "pendingAmnestyWantedClear",
                    false,
                    out pendingAmnesty) ||
                !TryReadJusticeBoolStrict(
                    element,
                    "pendingLegalReleaseFinalization",
                    false,
                    out pendingLegalRelease) ||
                !TryReadJusticeIntStrict(
                    element,
                    "pendingLegalReleaseSite",
                    0,
                    0,
                    2,
                    out pendingLegalReleaseSite) ||
                !TryReadJusticeIntStrict(
                    element,
                    "pendingLegalReleaseSelectedWeapon",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out pendingLegalReleaseSelectedWeapon) ||
                !TryReadJusticeIntStrict(
                    element,
                    "lastCanonicalPlayerModel",
                    0,
                    int.MinValue,
                    int.MaxValue,
                    out lastModel))
            {
                return false;
            }

            XmlNodeList caseNodes = element.SelectNodes("Case");
            XmlNodeList recordNodes = element.SelectNodes("Record");
            XmlNodeList custodyNodes = element.SelectNodes("Custody");
            XmlElement custody = custodyNodes != null && custodyNodes.Count == 1
                ? custodyNodes[0] as XmlElement
                : null;
            if (caseNodes == null || caseNodes.Count != 1 ||
                recordNodes == null || recordNodes.Count != 1 || custody == null)
            {
                return false;
            }

            JusticeCaseState caseState = ReadJusticeCaseXml(caseNodes[0] as XmlElement);
            JusticeRecordState recordState = ReadJusticeRecordXml(recordNodes[0] as XmlElement);
            bool policyRecoveryExpected =
                (sentencePolicyRecoveryMask & (1 << slot)) != 0;
            JusticeCustodyPersistenceSnapshot policyRecoverySnapshot = null;
            bool custodyIsValid = policyRecoveryExpected
                ? TryReadJusticeSentencePolicyRecoveryCustody(
                    custody,
                    slot,
                    out policyRecoverySnapshot)
                : IsJusticeCustodyXmlSemanticallyValid(
                    element,
                    caseState,
                    recordState);
            if (caseState == null || recordState == null ||
                !IsJusticeCaseRecordLinkValid(caseState, recordState) ||
                !custodyIsValid ||
                !IsJusticeProfilePendingDeathValid(
                    caseState,
                    pendingDeath,
                    pendingSlot,
                    pendingModel) ||
                (pendingDeath && pendingSlot >= 0 && pendingSlot != slot) ||
                !IsJusticePendingLegalReleaseValid(
                    caseState,
                    pendingLegalRelease,
                    pendingLegalReleaseSite,
                    pendingLegalReleaseSelectedWeapon) ||
                !IsJusticeCustodyBoundToProfileSlot(custody, slot))
            {
                return false;
            }

            loaded[slot] = new JusticePlayerProfileState(slot)
            {
                CaseState = caseState,
                RecordState = recordState,
                CustodyXml = custody.OuterXml,
                // Je conserve la forme typée du jeton pour vérifier que son
                // contenu correspond exactement au masque avant publication.
                CustodySnapshot = policyRecoverySnapshot,
                PendingDeathCapture = pendingDeath,
                PendingDeathCapturePlayerSlot = pendingSlot,
                PendingDeathCapturePlayerModel = pendingModel,
                PendingAmnestyWantedClear = pendingAmnesty,
                PendingLegalReleaseFinalization = pendingLegalRelease,
                PendingLegalReleaseSite = pendingLegalReleaseSite,
                PendingLegalReleaseSelectedWeapon = pendingLegalReleaseSelectedWeapon,
                LastCanonicalPlayerModel = lastModel,
                CanAdvanceCustodyInBackground =
                    IsJusticeStoredCustodyEligibleForBackgroundClock(
                        caseState,
                        custody,
                        pendingDeath,
                        pendingAmnesty,
                        pendingLegalRelease)
            };
        }

        profiles = loaded;
        hasProfiles = true;
        return true;
    }

    private static bool IsJusticeStoredCustodyEligibleForBackgroundClock(
        JusticeCaseState caseState,
        XmlElement custody,
        bool pendingDeath,
        bool pendingAmnesty,
        bool pendingLegalRelease)
    {
        if (caseState == null || custody == null || !caseState.Enabled ||
            caseState.Phase != JusticePhase.Incarcerated ||
            GetJusticeCustodyTotalRemainingSeconds(caseState) <= 0L ||
            pendingDeath || pendingAmnesty ||
            pendingLegalRelease || HasPendingJusticeProfileResetOperation(caseState))
        {
            return false;
        }

        // Je dérive ce cache pendant la lecture XML déjà validée. OnTick n'a donc
        // jamais à reparcourir le fragment pour détecter un WAL bloquant.
        return string.Equals(
                   custody.GetAttribute("active"),
                   "true",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   custody.GetAttribute("deferredInventoryRestore"),
                   "true",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   custody.GetAttribute("waitingForRespawn"),
                   "true",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   custody.GetAttribute("deathRebindPending"),
                   "true",
                   StringComparison.OrdinalIgnoreCase) &&
               custody.SelectSingleNode("FineDebitIntent") == null &&
               custody.SelectSingleNode("VoluntaryFinePaymentIntent") == null &&
               custody.SelectSingleNode("DisciplineIntent") == null;
    }

    private static bool IsJusticeProfilePendingDeathValid(
        JusticeCaseState caseState,
        bool pending,
        int pendingSlot,
        int pendingModel)
    {
        if (!pending)
        {
            return pendingSlot == -1 && pendingModel == 0;
        }

        if (caseState == null || !caseState.Enabled)
        {
            return false;
        }

        bool materializedCase = IsLoadedJusticeCaseActive(caseState) &&
            (IsJusticeCustodyPhase(caseState.Phase) ||
             caseState.Phase == JusticePhase.Wanted ||
             caseState.Phase == JusticePhase.Surrendering ||
             caseState.Phase == JusticePhase.Fugitive);
        bool rawPoliceDeathAwaitingMaterialization =
            !IsLoadedJusticeCaseActive(caseState) &&
            caseState.Phase == JusticePhase.AtLarge;
        return materializedCase || rawPoliceDeathAwaitingMaterialization;
    }

    private static bool IsJusticePendingLegalReleaseValid(
        JusticeCaseState caseState,
        bool pending,
        int site,
        int selectedWeapon)
    {
        if (!pending)
        {
            return site == 0 && selectedWeapon == 0;
        }

        if (caseState == null)
        {
            return false;
        }

        bool formalitiesCommitted = !IsLoadedJusticeCaseActive(caseState) &&
            caseState.Phase == JusticePhase.AtLarge &&
            site == (int)JusticeCustodySite.None && selectedWeapon == 0;
        bool custodySiteValid =
            (site == (int)JusticeCustodySite.MissionRow ||
             site == (int)JusticeCustodySite.Bolingbroke) &&
            selectedWeapon != 0;
        bool releasedCaseCommitted = custodySiteValid &&
            !IsLoadedJusticeCaseActive(caseState) &&
            caseState.Phase == JusticePhase.AtLarge;
        bool releaseWalCommitted = custodySiteValid &&
             caseState.Enabled &&
             caseState.Phase == JusticePhase.Incarcerated &&
             GetJusticeCustodyTotalRemainingSeconds(caseState) == 0L &&
             caseState.FineDue == 0L &&
            !string.IsNullOrWhiteSpace(caseState.CustodyEpisodeId) &&
            caseState.CompletedOperationIds.Contains(
                JusticePolicy.CreateOperationId(
                    JusticeOperationKind.Release,
                    caseState.CustodyEpisodeId));
        return formalitiesCommitted || releasedCaseCommitted || releaseWalCommitted;
    }

    private static bool IsJusticeCustodyBoundToProfileSlot(XmlElement custody, int slot)
    {
        if (custody == null || !IsJusticeCanonicalProfileSlot(slot))
        {
            return false;
        }

        int playerSlot;
        if (!TryReadJusticeIntStrict(custody, "playerSlot", -1, -1, 2, out playerSlot))
        {
            return false;
        }
        bool custodyIdentityRequired =
            string.Equals(custody.GetAttribute("active"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(custody.GetAttribute("inventoryRemoved"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(custody.GetAttribute("deferredInventoryRestore"), "true", StringComparison.OrdinalIgnoreCase) ||
            custody.SelectSingleNode("InventorySnapshot") != null ||
            custody.SelectSingleNode("FineDebitIntent") != null ||
            custody.SelectSingleNode("DisciplineIntent") != null;
        if (custodyIdentityRequired && playerSlot != slot)
        {
            return false;
        }

        XmlElement voluntary =
            custody.SelectSingleNode("VoluntaryFinePaymentIntent") as XmlElement;
        int voluntarySlot;
        return voluntary == null ||
               TryReadJusticeIntStrict(voluntary, "slot", -1, 0, 2, out voluntarySlot) &&
               voluntarySlot == slot;
    }

    private static bool HasJusticeProfileCustodyRecovery(JusticePlayerProfileState profile)
    {
        if (profile == null)
        {
            return false;
        }
        if (HasJusticeProfilePendingRecoveryWal(profile))
        {
            return true;
        }
        if (profile.CustodySnapshot != null)
        {
            JusticeCustodyPersistenceSnapshot custody = profile.CustodySnapshot;
            return custody.Active || custody.PoliceSuppressionApplied ||
                   custody.PoliceDispatchDisabled || custody.InventoryRemoved ||
                   custody.DeferredInventoryRestore || custody.InventorySnapshot != null ||
                   custody.FineDebitIntent != null || custody.DisciplineIntent != null ||
                   custody.VoluntaryPaymentIntent != null;
        }
        if (string.IsNullOrWhiteSpace(profile.CustodyXml))
        {
            return false;
        }
        try
        {
            XmlElement custody = LoadJusticeXmlFragment(profile.CustodyXml).DocumentElement;
            return custody != null &&
                   (string.Equals(custody.GetAttribute("active"), "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(custody.GetAttribute("policeSuppressionApplied"), "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(custody.GetAttribute("policeDispatchDisabled"), "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(custody.GetAttribute("inventoryRemoved"), "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(custody.GetAttribute("deferredInventoryRestore"), "true", StringComparison.OrdinalIgnoreCase) ||
                    custody.SelectSingleNode("InventorySnapshot") != null ||
                    custody.SelectSingleNode("FineDebitIntent") != null ||
                    custody.SelectSingleNode("DisciplineIntent") != null ||
                    custody.SelectSingleNode("VoluntaryFinePaymentIntent") != null);
        }
        catch
        {
            return true;
        }
    }

    private static bool HasJusticeProfilePendingRecoveryWal(
        JusticePlayerProfileState profile)
    {
        if (profile == null)
        {
            return false;
        }

        JusticeCaseState state = profile.CaseState;
        return profile.PendingDeathCapture ||
               profile.PendingAmnestyWantedClear ||
               profile.PendingLegalReleaseFinalization ||
               state != null &&
               (state.EscapeWantedMinimumPending ||
                state.EscapeWantedMinimumAttempted ||
                HasPendingJusticeProfileResetOperation(state));
    }

    private void MergeJusticeInactiveProfilePoliceSuppressionRecovery()
    {
        if (_justicePlayerProfiles == null)
        {
            return;
        }

        bool ignoreRecovery = _justicePoliceIgnoreApplied;
        bool dispatchRecovery = _justicePoliceDispatchDisabled;
        for (int slot = 0; slot < _justicePlayerProfiles.Length; slot++)
        {
            if (slot == _justiceActivePlayerProfileSlot)
            {
                continue;
            }

            JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
            if (profile == null)
            {
                continue;
            }

            if (profile.CustodySnapshot != null)
            {
                ignoreRecovery |= profile.CustodySnapshot.PoliceSuppressionApplied;
                dispatchRecovery |= profile.CustodySnapshot.PoliceDispatchDisabled;
                continue;
            }
            if (string.IsNullOrWhiteSpace(profile.CustodyXml))
            {
                continue;
            }

            try
            {
                XmlElement custody =
                    LoadJusticeXmlFragment(profile.CustodyXml).DocumentElement;
                if (custody == null)
                {
                    continue;
                }
                ignoreRecovery |= string.Equals(
                    custody.GetAttribute("policeSuppressionApplied"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
                dispatchRecovery |= string.Equals(
                    custody.GetAttribute("policeDispatchDisabled"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Les fragments ont déjà été validés par le chargeur. Si leur
                // relecture échoue malgré tout, je garde un retry global fermé.
                ignoreRecovery = true;
                dispatchRecovery = true;
            }
        }

        _justicePoliceIgnoreApplied = ignoreRecovery;
        _justicePoliceDispatchDisabled = dispatchRecovery;
        _justicePoliceSuppressionActive = ignoreRecovery || dispatchRecovery;
        if (_justicePoliceSuppressionActive)
        {
            _justicePoliceSuppressionRestorePending = true;
            _justiceNextPoliceSuppressionRestoreAt = 0;
        }
    }

    private bool TryClearJusticeInactiveProfilePoliceSuppressionTokens()
    {
        if (_justicePlayerProfiles == null)
        {
            return true;
        }

        string[] replacements = new string[_justicePlayerProfiles.Length];
        JusticeCustodyPersistenceSnapshot[] typedReplacements =
            new JusticeCustodyPersistenceSnapshot[_justicePlayerProfiles.Length];
        bool[] clearTypedRecovery = new bool[_justicePlayerProfiles.Length];
        int clearedPolicyRecoveryMask = 0;
        try
        {
            for (int slot = 0; slot < _justicePlayerProfiles.Length; slot++)
            {
                if (slot == _justiceActivePlayerProfileSlot)
                {
                    continue;
                }

                JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
                if (profile == null)
                {
                    continue;
                }

                if (profile.CustodySnapshot != null)
                {
                    if (profile.CustodySnapshot.PoliceSuppressionApplied ||
                        profile.CustodySnapshot.PoliceDispatchDisabled)
                    {
                        JusticeCustodyPersistenceSnapshot replacement =
                            CloneJusticeCustodyPersistenceSnapshotWithoutPoliceTokens(
                                profile.CustodySnapshot);
                        int policyBit = 1 << slot;
                        if ((_justicePolicyResetRecoveryMask & policyBit) != 0 &&
                            !RequiresJusticeSentencePolicyRecovery(replacement))
                        {
                            // La police est globale : une fois rendue, un jeton
                            // inactif qui ne porte rien d'autre peut être acquitté.
                            clearTypedRecovery[slot] = true;
                            clearedPolicyRecoveryMask |= policyBit;
                        }
                        else
                        {
                            typedReplacements[slot] = replacement;
                        }
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(profile.CustodyXml))
                {
                    continue;
                }

                XmlElement custody =
                    LoadJusticeXmlFragment(profile.CustodyXml).DocumentElement;
                if (custody == null)
                {
                    return false;
                }
                bool hasIgnoreToken = string.Equals(
                    custody.GetAttribute("policeSuppressionApplied"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
                bool hasDispatchToken = string.Equals(
                    custody.GetAttribute("policeDispatchDisabled"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
                if (!hasIgnoreToken && !hasDispatchToken)
                {
                    continue;
                }

                custody.SetAttribute("policeSuppressionApplied", "false");
                custody.SetAttribute("policeDispatchDisabled", "false");
                replacements[slot] = custody.OuterXml;
            }
        }
        catch
        {
            return false;
        }

        for (int slot = 0; slot < replacements.Length; slot++)
        {
            if (clearTypedRecovery[slot])
            {
                _justicePlayerProfiles[slot].CustodySnapshot = null;
                _justicePlayerProfiles[slot].CustodyXml =
                    CreateCanonicalEmptyJusticeCustodyXml();
            }
            else if (typedReplacements[slot] != null)
            {
                _justicePlayerProfiles[slot].CustodySnapshot = typedReplacements[slot];
            }
            if (replacements[slot] != null)
            {
                _justicePlayerProfiles[slot].CustodyXml = replacements[slot];
            }
        }
        if (clearedPolicyRecoveryMask != 0)
        {
            _justicePolicyResetRecoveryMask &= ~clearedPolicyRecoveryMask;
            _justicePolicyResetRecoveryPublicationPending = true;
            JusticeMarkStateDirty();
        }
        return true;
    }

    private static int ResolveLegacyJusticeProfileSlot(
        XmlElement root,
        int currentCanonicalSlot,
        int lastCanonicalSlot,
        int pendingDeathSlot)
    {
        if (IsJusticeCanonicalProfileSlot(currentCanonicalSlot))
        {
            return currentCanonicalSlot;
        }
        if (IsJusticeCanonicalProfileSlot(lastCanonicalSlot))
        {
            return lastCanonicalSlot;
        }
        if (IsJusticeCanonicalProfileSlot(pendingDeathSlot))
        {
            return pendingDeathSlot;
        }

        XmlElement custody = root == null
            ? null
            : root.SelectSingleNode("Custody") as XmlElement;
        int custodySlot;
        if (custody != null &&
            IsJusticeCustodyBoundToAnyProvenSlot(custody, out custodySlot))
        {
            return custodySlot;
        }
        return -1;
    }

    private static bool IsJusticeCustodyBoundToAnyProvenSlot(
        XmlElement custody,
        out int slot)
    {
        slot = -1;
        if (custody == null ||
            !TryReadJusticeIntStrict(custody, "playerSlot", -1, -1, 2, out slot) ||
            !IsJusticeCanonicalProfileSlot(slot))
        {
            XmlElement voluntary = custody == null
                ? null
                : custody.SelectSingleNode("VoluntaryFinePaymentIntent") as XmlElement;
            return voluntary != null &&
                   TryReadJusticeIntStrict(voluntary, "slot", -1, 0, 2, out slot) &&
                   IsJusticeCanonicalProfileSlot(slot);
        }

        return string.Equals(custody.GetAttribute("active"), "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(custody.GetAttribute("inventoryRemoved"), "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(custody.GetAttribute("deferredInventoryRestore"), "true", StringComparison.OrdinalIgnoreCase) ||
               custody.SelectSingleNode("InventorySnapshot") != null ||
               custody.SelectSingleNode("FineDebitIntent") != null ||
               custody.SelectSingleNode("DisciplineIntent") != null ||
               custody.SelectSingleNode("VoluntaryFinePaymentIntent") != null;
    }

    private static bool AreJusticeProfileMirrorNodesEqual(
        XmlElement root,
        JusticePlayerProfileState[] profiles,
        int activeSlot)
    {
        if (root == null || profiles == null || !IsJusticeCanonicalProfileSlot(activeSlot))
        {
            return false;
        }
        XmlElement profileElement = root.SelectSingleNode(
            "PlayerProfiles/Profile[@slot='" + activeSlot.ToString(CultureInfo.InvariantCulture) + "']") as XmlElement;
        XmlElement rootCase = root.SelectSingleNode("Case") as XmlElement;
        XmlElement rootRecord = root.SelectSingleNode("Record") as XmlElement;
        XmlElement rootCustody = root.SelectSingleNode("Custody") as XmlElement;
        if (profileElement == null || rootCase == null || rootRecord == null || rootCustody == null)
        {
            return false;
        }

        XmlElement profileCase = profileElement.SelectSingleNode("Case") as XmlElement;
        XmlElement profileRecord = profileElement.SelectSingleNode("Record") as XmlElement;
        XmlElement profileCustody = profileElement.SelectSingleNode("Custody") as XmlElement;
        if (profileCase == null || profileRecord == null || profileCustody == null ||
            !AreJusticeXmlElementsEquivalent(rootCase, profileCase) ||
            !AreJusticeXmlElementsEquivalent(rootRecord, profileRecord) ||
            !AreJusticeXmlElementsEquivalent(rootCustody, profileCustody))
        {
            return false;
        }

        JusticePlayerProfileState active = profiles[activeSlot];
        bool rootEnabled;
        bool rootPendingDeath;
        int rootPendingSlot;
        int rootPendingModel;
        bool rootPendingAmnesty;
        bool rootPendingLegalRelease;
        int rootPendingLegalReleaseSite;
        int rootPendingLegalReleaseSelectedWeapon;
        int rootLastSlot;
        int rootLastModel;
        return TryReadJusticeBoolStrict(root, "enabled", false, out rootEnabled) &&
               TryReadJusticeBoolStrict(root, "pendingDeathCapture", false, out rootPendingDeath) &&
               TryReadJusticeIntStrict(root, "pendingDeathCapturePlayerSlot", -1, -1, 2, out rootPendingSlot) &&
               TryReadJusticeIntStrict(root, "pendingDeathCapturePlayerModel", 0, int.MinValue, int.MaxValue, out rootPendingModel) &&
               TryReadJusticeBoolStrict(root, "pendingAmnestyWantedClear", false, out rootPendingAmnesty) &&
               TryReadJusticeBoolStrict(root, "pendingLegalReleaseFinalization", false, out rootPendingLegalRelease) &&
               TryReadJusticeIntStrict(root, "pendingLegalReleaseSite", 0, 0, 2, out rootPendingLegalReleaseSite) &&
               TryReadJusticeIntStrict(root, "pendingLegalReleaseSelectedWeapon", 0, int.MinValue, int.MaxValue, out rootPendingLegalReleaseSelectedWeapon) &&
               TryReadJusticeIntStrict(root, "lastCanonicalPlayerSlot", -1, -1, 2, out rootLastSlot) &&
               TryReadJusticeIntStrict(root, "lastCanonicalPlayerModel", 0, int.MinValue, int.MaxValue, out rootLastModel) &&
               rootEnabled == active.CaseState.Enabled &&
               rootPendingDeath == active.PendingDeathCapture &&
               rootPendingSlot == active.PendingDeathCapturePlayerSlot &&
               rootPendingModel == active.PendingDeathCapturePlayerModel &&
               rootPendingAmnesty == active.PendingAmnestyWantedClear &&
               rootPendingLegalRelease == active.PendingLegalReleaseFinalization &&
               rootPendingLegalReleaseSite == active.PendingLegalReleaseSite &&
               rootPendingLegalReleaseSelectedWeapon == active.PendingLegalReleaseSelectedWeapon &&
               rootLastSlot == activeSlot &&
               rootLastModel == active.LastCanonicalPlayerModel;
    }

    private static bool AreJusticeXmlElementsEquivalent(XmlElement left, XmlElement right)
    {
        if (left == null || right == null ||
            !string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
            left.Attributes.Count != right.Attributes.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Attributes.Count; index++)
        {
            XmlAttribute attribute = left.Attributes[index];
            if (!right.HasAttribute(attribute.Name) ||
                !string.Equals(
                    attribute.Value,
                    right.GetAttribute(attribute.Name),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        XmlNode leftChild = NextJusticeSemanticXmlNode(left.FirstChild);
        XmlNode rightChild = NextJusticeSemanticXmlNode(right.FirstChild);
        while (leftChild != null && rightChild != null)
        {
            if (leftChild.NodeType != rightChild.NodeType)
            {
                return false;
            }
            XmlElement leftElement = leftChild as XmlElement;
            XmlElement rightElement = rightChild as XmlElement;
            if (leftElement != null || rightElement != null)
            {
                if (leftElement == null || rightElement == null ||
                    !AreJusticeXmlElementsEquivalent(leftElement, rightElement))
                {
                    return false;
                }
            }
            else if (!string.Equals(
                leftChild.Value ?? string.Empty,
                rightChild.Value ?? string.Empty,
                StringComparison.Ordinal))
            {
                return false;
            }
            leftChild = NextJusticeSemanticXmlNode(leftChild.NextSibling);
            rightChild = NextJusticeSemanticXmlNode(rightChild.NextSibling);
        }
        return leftChild == null && rightChild == null;
    }

    private static XmlNode NextJusticeSemanticXmlNode(XmlNode node)
    {
        while (node != null &&
               (node.NodeType == XmlNodeType.Comment ||
                node.NodeType == XmlNodeType.Whitespace ||
                node.NodeType == XmlNodeType.SignificantWhitespace))
        {
            node = node.NextSibling;
        }
        return node;
    }
}
