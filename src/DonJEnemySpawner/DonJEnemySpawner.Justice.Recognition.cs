using System;
using DonJ.JusticeRecognition;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private bool? _justiceRecognitionSynchronizedEnabled;
    private bool? _justiceRecognitionSynchronizedSuspended;
    private int _justiceRecognitionSynchronizedProfileSlot = -2;
    private bool _justiceRecognitionCaptureResetPersistenceFailureLogged;
    private int _justiceRecognitionCaptureResetConfirmedProfileSlot = -1;
    private string _justiceRecognitionCaptureResetConfirmedEpisodeId =
        string.Empty;

    private void InitializeJusticeRecognitionFailClosed()
    {
        _justiceRecognitionSynchronizedEnabled = null;
        _justiceRecognitionSynchronizedSuspended = null;
        _justiceRecognitionSynchronizedProfileSlot = -2;
        _justiceRecognitionCaptureResetPersistenceFailureLogged = false;
        ResetJusticeRecognitionCaptureResetConfirmation();

        JusticeRecognitionBridge.SetActiveProfile(null);
        JusticeRecognitionBridge.SetRuntimeSuspended(true);
        JusticeRecognitionBridge.SetEnabled(false);
    }

    private void BindAndSynchronizeJusticeRecognition()
    {
        JusticeRecognitionBridge.BindObserverExclusion(IsJusticeOwnedAlly);
        JusticeRecognitionBridge.BindWantedMinimum(
            delegate(int level)
            {
                return TryApplyJusticeRecognitionWantedMinimum(level);
            });

        SynchronizeJusticeRecognition(true);
    }

    private bool IsJusticeRecognitionTransitionBlocked()
    {
        return _justiceProfileContextBlocked || _justiceProfileSelectionPending ||
               _justiceProfileSwitchPersistencePending || _justiceBackupRepairPending ||
               _justicePersistenceServicesUnavailable || _justiceCriticalBarrierRevision > 0L ||
               JusticeIsCustodyActive || _justicePolicyResetPublicationPending ||
               _justicePolicyResetRecoveryPublicationPending || _justicePolicyResetRecoveryMask != 0 ||
               _justiceActiveProfileResetPending || _justiceAmnestyPending ||
               _justiceLegalReleaseFinalizationPending ||
               _justicePoliceDeathNoCellReleaseProtectionRestorePending ||
               _justiceCustodyTransferRollbackFinalizationPending ||
               _justicePursuitDeathObservedDuringSuspension ||
               _justicePendingDeathFrontWalRecord != null || _justiceCaptureRetryPending ||
               _justiceArrestCompletionProbePending || _justiceFineDebitIntent != null ||
               _justiceVoluntaryFinePaymentIntent != null || HasJusticeDeferredRuntimeFronts();
    }

    private bool TryApplyJusticeRecognitionWantedMinimum(int level)
    {
        // Deuxième barrière, côté propriétaire du wanted. Le bridge peut être
        // appelé avant que sa suspension ait été synchronisée dans cette frame.
        if (!_justiceInitialized || !_justiceEnabled || _justiceCaseState == null ||
            !_justiceCaseState.Enabled ||
            !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) ||
            IsJusticeRecognitionTransitionBlocked())
            return false;

        try
        {
            Ped player = Game.Player.Character;
            if (!Entity.Exists(player) || IsJusticePlayerDeadSafe(player) ||
                !IsJusticeRuntimeProfileContextCompatible() || IsJusticeRuntimeSuspended(player))
                return false;

            return SetJusticeWantedMinimum(level);
        }
        catch
        {
            return false;
        }
    }

    private void SynchronizeJusticeRecognition(bool force = false)
    {
        int profileSlot = IsJusticeCanonicalProfileSlot(
            _justiceActivePlayerProfileSlot)
                ? _justiceActivePlayerProfileSlot
                : -1;

        bool enabled = _justiceInitialized &&
                       profileSlot >= 0 &&
                       _justiceEnabled;

        bool gameplaySuspended = true;
        if (_justiceInitialized)
        {
            try
            {
                gameplaySuspended = IsJusticeRuntimeSuspended(
                    Game.Player.Character);
            }
            catch
            {
                // Je ferme la reconnaissance si GTA ne permet pas de qualifier
                // proprement une mission, un chargement ou une cinématique.
                gameplaySuspended = true;
            }
        }

        bool suspended = !enabled || gameplaySuspended ||
                         IsJusticeRecognitionTransitionBlocked();

        if (force || _justiceRecognitionSynchronizedProfileSlot != profileSlot ||
            _justiceRecognitionSynchronizedEnabled != enabled ||
            _justiceRecognitionSynchronizedSuspended != suspended)
        {
            // État complet publié atomiquement, sans fenêtre profil/ON incohérente.
            JusticeRecognitionBridge.SetRuntimeState(
                enabled, suspended, GetJusticeRecognitionProfileId(profileSlot));
            _justiceRecognitionSynchronizedProfileSlot = profileSlot;
            _justiceRecognitionSynchronizedEnabled = enabled;
            _justiceRecognitionSynchronizedSuspended = suspended;
        }
    }

    private void ShutdownJusticeRecognition()
    {
        JusticeRecognitionBridge.SetRuntimeSuspended(true);
        JusticeRecognitionBridge.SetEnabled(false);
        JusticeRecognitionBridge.SetActiveProfile(null);
        JusticeRecognitionBridge.UnbindWantedMinimum();
        JusticeRecognitionBridge.BindObserverExclusion(null);

        _justiceRecognitionSynchronizedEnabled = false;
        _justiceRecognitionSynchronizedSuspended = true;
        _justiceRecognitionSynchronizedProfileSlot = -1;
        _justiceRecognitionCaptureResetPersistenceFailureLogged = false;
        ResetJusticeRecognitionCaptureResetConfirmation();
    }

    private void SuppressJusticeRecognitionWantedLoss(string reason)
    {
        JusticeRecognitionBridge.SuppressNextWantedLoss(reason);
    }

    private bool NotifyJusticeRecognitionPlayerCaptured(string reason)
    {
        return JusticeRecognitionBridge.NotifyPlayerCaptured(
            GetJusticeRecognitionProfileId(_justiceActivePlayerProfileSlot),
            reason);
    }

    private bool EnsureJusticeRecognitionCaptureResetDurable(string reason)
    {
        string episodeId = _justiceCaseState == null
            ? string.Empty
            : _justiceCaseState.CustodyEpisodeId;
        int profileSlot = _justiceActivePlayerProfileSlot;
        if (IsJusticeCanonicalProfileSlot(profileSlot) &&
            !string.IsNullOrWhiteSpace(episodeId) &&
            _justiceRecognitionCaptureResetConfirmedProfileSlot == profileSlot &&
            string.Equals(
                _justiceRecognitionCaptureResetConfirmedEpisodeId,
                episodeId,
                System.StringComparison.Ordinal))
        {
            // Je ne recrée ni commande ni ForceSave pendant les retries wanted,
            // inventaire, persistance ou FadeIn du même épisode de capture.
            return true;
        }

        if (NotifyJusticeRecognitionPlayerCaptured(reason))
        {
            _justiceRecognitionCaptureResetConfirmedProfileSlot = profileSlot;
            _justiceRecognitionCaptureResetConfirmedEpisodeId =
                episodeId ?? string.Empty;
            if (_justiceRecognitionCaptureResetPersistenceFailureLogged)
            {
                LogInfo(
                    "Justice.RecognitionCapture",
                    "Journal critique rétabli; reset plaque/tenue/mandat confirmé.");
            }
            _justiceRecognitionCaptureResetPersistenceFailureLogged = false;
            return true;
        }

        if (!_justiceRecognitionCaptureResetPersistenceFailureLogged)
        {
            _justiceRecognitionCaptureResetPersistenceFailureLogged = true;
            LogWarning(
                "Justice.RecognitionCapture",
                "Reset plaque/tenue/mandat non durable; frontière d'arrestation suspendue et retry armé.");
        }
        return false;
    }

    private void ResetJusticeRecognitionCaptureResetConfirmation()
    {
        _justiceRecognitionCaptureResetConfirmedProfileSlot = -1;
        _justiceRecognitionCaptureResetConfirmedEpisodeId = string.Empty;
    }

    private bool ClearJusticeRecognitionProfile(
        int profileSlot,
        string reason)
    {
        string profileId = GetJusticeRecognitionProfileId(profileSlot);
        if (profileId == null)
        {
            return false;
        }

        return JusticeRecognitionBridge.ClearProfile(profileId, reason);
    }

    private static string GetJusticeRecognitionProfileId(int profileSlot)
    {
        switch (profileSlot)
        {
            case 0:
                return "Michael";
            case 1:
                return "Franklin";
            case 2:
                return "Trevor";
            default:
                return null;
        }
    }

    private static string[] GetJusticeRecognitionStatusLines()
    {
        string[] lines = JusticeRecognitionBridge.GetStatusLines();
        if (lines == null || lines.Length == 0)
        {
            return new[]
            {
                "Reconnaissance policière : état indisponible"
            };
        }

        return lines;
    }
}
