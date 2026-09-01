using DonJ.JusticeRecognition;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private bool? _justiceRecognitionSynchronizedEnabled;
    private bool? _justiceRecognitionSynchronizedSuspended;
    private int _justiceRecognitionSynchronizedProfileSlot = -2;
    private bool _justiceRecognitionCaptureResetPersistenceFailureLogged;

    private void InitializeJusticeRecognitionFailClosed()
    {
        _justiceRecognitionSynchronizedEnabled = null;
        _justiceRecognitionSynchronizedSuspended = null;
        _justiceRecognitionSynchronizedProfileSlot = -2;
        _justiceRecognitionCaptureResetPersistenceFailureLogged = false;

        JusticeRecognitionBridge.SetActiveProfile(null);
        JusticeRecognitionBridge.SetRuntimeSuspended(true);
        JusticeRecognitionBridge.SetEnabled(false);
    }

    private void BindAndSynchronizeJusticeRecognition()
    {
        JusticeRecognitionBridge.BindWantedMinimum(
            delegate(int level)
            {
                return SetJusticeWantedMinimum(level);
            });

        SynchronizeJusticeRecognition(true);
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

        bool suspended = !enabled ||
                         gameplaySuspended ||
                         _justiceProfileContextBlocked ||
                         JusticeIsCustodyActive ||
                         _justicePolicyResetPublicationPending ||
                         _justicePolicyResetRecoveryPublicationPending ||
                         _justicePolicyResetRecoveryMask != 0 ||
                         _justiceActiveProfileResetPending ||
                         _justiceAmnestyPending ||
                         _justiceLegalReleaseFinalizationPending ||
                         _justiceCustodyTransferRollbackFinalizationPending;

        if (force ||
            _justiceRecognitionSynchronizedProfileSlot != profileSlot)
        {
            JusticeRecognitionBridge.SetActiveProfile(
                GetJusticeRecognitionProfileId(profileSlot));
            _justiceRecognitionSynchronizedProfileSlot = profileSlot;
        }

        if (force ||
            !_justiceRecognitionSynchronizedEnabled.HasValue ||
            _justiceRecognitionSynchronizedEnabled.Value != enabled)
        {
            JusticeRecognitionBridge.SetEnabled(enabled);
            _justiceRecognitionSynchronizedEnabled = enabled;
        }

        if (force ||
            !_justiceRecognitionSynchronizedSuspended.HasValue ||
            _justiceRecognitionSynchronizedSuspended.Value != suspended)
        {
            JusticeRecognitionBridge.SetRuntimeSuspended(suspended);
            _justiceRecognitionSynchronizedSuspended = suspended;
        }
    }

    private void ShutdownJusticeRecognition()
    {
        JusticeRecognitionBridge.SetRuntimeSuspended(true);
        JusticeRecognitionBridge.SetEnabled(false);
        JusticeRecognitionBridge.SetActiveProfile(null);
        JusticeRecognitionBridge.UnbindWantedMinimum();

        _justiceRecognitionSynchronizedEnabled = false;
        _justiceRecognitionSynchronizedSuspended = true;
        _justiceRecognitionSynchronizedProfileSlot = -1;
        _justiceRecognitionCaptureResetPersistenceFailureLogged = false;
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
        if (NotifyJusticeRecognitionPlayerCaptured(reason))
        {
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
