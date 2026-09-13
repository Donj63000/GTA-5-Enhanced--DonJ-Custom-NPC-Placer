using System;
using GTA;
using GTA.Math;
using GTA.Native;

public sealed partial class DonJEnemySpawner
{
    private bool _justiceCustodyStreamingFallbackToPrison;
    private bool _justiceCustodyStreamingTechnicalFailure;
    private int _justiceCustodyRecoveryOwnerSlot = -1;
    private int _justiceCustodyRecoveryPlayerHandle;
    private int _justiceCustodyRecoveryPlayerModel;
    private string _justiceCustodyRecoveryEpisodeId = string.Empty;
    private int _justiceNextCustodyGeometryCheckAt;
    private bool _justiceCustodyHoldingFreezeCaptured;
    private bool _justiceCustodyHoldingStoredFrozen;

    private static JusticeCustodySite GetJusticeCustodyStreamingSiteForTarget(Vector3 target)
    {
        if (target.DistanceTo(JusticeMissionRowLayout.CellPosition) <= 1.0f ||
            target.DistanceTo(JusticeMissionRowLayout.ArrivalPosition) <= 1.0f)
        {
            return JusticeCustodySite.MissionRow;
        }
        if (target.DistanceTo(JusticeBolingbrokeLayout.CellPosition) <= 1.0f ||
            target.DistanceTo(JusticeBolingbrokeLayout.ArrivalPosition) <= 1.0f)
        {
            return JusticeCustodySite.Bolingbroke;
        }
        return JusticeCustodySite.None;
    }

    private bool HasJusticeCustodyStreamingRecoveryOwner()
    {
        if (!IsJusticeCanonicalProfileSlot(_justiceCustodyRecoveryOwnerSlot))
        {
            return false;
        }
        try
        {
            Ped player = Game.Player.Character;
            int ownerSlot;
            JusticeCaseState ownerCase;
            if (!TryResolveJusticeCustodyStreamingRecoveryOwner(player, out ownerSlot, out ownerCase) ||
                ownerSlot != _justiceCustodyRecoveryOwnerSlot)
            {
                return false;
            }
            string episode = ownerCase.CustodyEpisodeId ?? string.Empty;
            return
                player.Handle == _justiceCustodyRecoveryPlayerHandle &&
                GetJusticePedModelHashSafe(player) == _justiceCustodyRecoveryPlayerModel &&
                (string.IsNullOrEmpty(_justiceCustodyRecoveryEpisodeId) ||
                 string.Equals(_justiceCustodyRecoveryEpisodeId, episode, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private bool RememberJusticeCustodyStreamingRecoveryOwner(Ped player)
    {
        int ownerSlot;
        JusticeCaseState ownerCase;
        if (!TryResolveJusticeCustodyStreamingRecoveryOwner(player, out ownerSlot, out ownerCase))
        {
            return false;
        }
        _justiceCustodyRecoveryOwnerSlot = ownerSlot;
        _justiceCustodyRecoveryPlayerHandle = player.Handle;
        _justiceCustodyRecoveryPlayerModel = GetJusticePedModelHashSafe(player);
        _justiceCustodyRecoveryEpisodeId = ownerCase.CustodyEpisodeId ?? string.Empty;
        return _justiceCustodyRecoveryPlayerModel != 0;
    }

    private bool TryResolveJusticeCustodyStreamingRecoveryOwner(
        Ped player, out int ownerSlot, out JusticeCaseState ownerCase)
    {
        ownerSlot = -1;
        ownerCase = null;
        try
        {
            if (!Entity.Exists(player) || player.IsDead) return false;
            if (_justicePreJudgmentHoldingSource != JusticePreJudgmentHoldingSource.None &&
                IsJusticeCanonicalProfileSlot(_justicePoliceDeathPreJudgmentHoldingOwnerSlot) &&
                IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible(player))
            {
                // Je lie le secours au propriétaire du holding de réparation,
                // même si son profil n'est pas encore devenu le profil actif.
                ownerSlot = _justicePoliceDeathPreJudgmentHoldingOwnerSlot;
                ownerCase = GetJusticePreJudgmentHoldingOwnerCase(ownerSlot);
            }
            else if (IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) &&
                _justiceCustodyPlayerSlot == _justiceActivePlayerProfileSlot &&
                IsJusticeCustodyPlayerIdentityCompatible(player))
            {
                ownerSlot = _justiceCustodyPlayerSlot;
                ownerCase = _justiceCaseState;
            }
            return ownerCase != null;
        }
        catch
        {
            ownerSlot = -1;
            ownerCase = null;
            return false;
        }
    }

    private JusticeCustodySite GetJusticeCustodyPhysicalDestinationSite(JusticeCustodySite plannedSite)
    {
        return _justiceCustodyStreamingFallbackToPrison &&
            HasJusticeCustodyStreamingRecoveryOwner()
                ? JusticeCustodySite.Bolingbroke : plannedSite;
    }

    private bool ApplyJusticeCustodyStreamingFallbackSite()
    {
        if (!_justiceCustodyStreamingFallbackToPrison ||
            !HasJusticeCustodyStreamingRecoveryOwner() ||
            _justiceCustodySite == JusticeCustodySite.Bolingbroke)
        {
            return true;
        }
        if (_justiceCustodyRecoveryOwnerSlot != _justiceActivePlayerProfileSlot ||
            _justiceCaseState == null || _justiceCaseState.Phase == JusticePhase.Captured ||
            _justiceFineDebitIntent != null ||
            (_justicePreJudgmentHoldingSource == JusticePreJudgmentHoldingSource.PendingWalCustodyRebind &&
             !IsJusticeCustodyDeathFrontResultDurable()))
        {
            // Je laisse le site légal et le plan d'amende inchangés tant que la
            // transaction précédente n'est pas acquittée; le holding reste physique.
            return false;
        }
        _justiceCustodySite = JusticeCustodySite.Bolingbroke;
        _justiceCustodyTransferPrecommitConfirmed = false;
        _justiceCustodyAdmissionPositionEstablished = false;
        _justiceCustodyAdmissionFadeInRequested = false;
        JusticeMarkStateDirty();
        return true;
    }

    private void HandleJusticeCustodyDestinationStreamingTimeout(
        Ped player, JusticeCustodySite site, int now)
    {
        if (!HasJusticeCustodyStreamingRecoveryOwner())
        {
            _justiceCustodyStreamingFallbackToPrison = false;
            _justiceCustodyStreamingTechnicalFailure = false;
            if (!RememberJusticeCustodyStreamingRecoveryOwner(player)) return;
        }
        if (_justiceCustodyStreamingTechnicalFailure) return;

        if (site == JusticeCustodySite.MissionRow && !_justiceCustodyStreamingFallbackToPrison)
        {
            _justiceCustodyStreamingFallbackToPrison = true;
            _justicePoliceDeathPreJudgmentHoldingEstablished = false;
            _justicePreJudgmentHoldingPositionApplied = false;
            _justiceCustodyAdmissionPositionEstablished = false;
            _justiceCustodyAdmissionFadeInRequested = false;
            _justiceNextPoliceDeathPreJudgmentHoldingAttemptAt = now;
            _justiceNextCustodyTransferAttemptAt = now;
            ResetJusticeCustodyAdmissionWantedStability(now);
            LogWarning("Justice.ChargementDetention",
                "Cellule Mission Row indisponible après 30 secondes : destination physique Bolingbroke, peine et amende conservées. " +
                GetJusticeCustodyDestinationStreamingDiagnostic());
            ResetJusticeCustodyDestinationStreaming();
            return;
        }

        _justiceCustodyStreamingTechnicalFailure = true;
        _justiceCustodyContainmentEstablished = false;
        InterruptJusticeCustodyEscapeObservation();
        ResetJusticeCustodyClock(now);
        LogWarning("Justice.ChargementDetention.Erreur",
            "Destination " + site + " indisponible après 30 secondes : transfert suspendu en erreur technique, aucune évasion ni relance automatique. " +
            GetJusticeCustodyDestinationStreamingDiagnostic());
        CompleteJusticeCustodyDestinationStreaming();
        ShowStatus("Justice : erreur de chargement de la détention, diagnostic enregistré.", 6000);
        MaintainJusticeCustodyStreamingTechnicalFailure(player, now);
    }

    private bool MaintainJusticeCustodyStreamingTechnicalFailure(Ped player, int now)
    {
        if (!_justiceCustodyStreamingTechnicalFailure ||
            !HasJusticeCustodyStreamingRecoveryOwner()) return false;
        // Je conserve le dernier emplacement sous protection. Une erreur moteur
        // ne devient ni une admission fictive ni une remise en liberté judiciaire.
        try
        {
            if (!player.FreezePosition) player.FreezePosition = true;
            Game.DisableAllControlsThisFrame(0);
        }
        catch (Exception ex)
        {
            LogException("Justice.ChargementDetention.Protection", ex);
        }
        ResetJusticeCustodyClock(now);
        _justiceOutsideCustodySinceAt = 0;
        return true;
    }

    private static bool IsJusticeCustodyPositionBelowPlayableFloor(
        JusticeCustodyLayout layout, Vector3 position)
    {
        if (layout == null || layout.AllowedVolumes == null) return false;
        foreach (JusticeCustodyVolume volume in layout.AllowedVolumes)
        {
            if (volume != null && volume.ContainsHorizontal(position) &&
                position.Z < volume.Minimum.Z) return true;
        }
        return false;
    }

    private bool TryRecoverJusticeCustodyMissingGeometry(Ped player, int now)
    {
        if (_justiceCaseState == null || !JusticeIsCustodyActive ||
            _justiceCustodyTransferPending || _justiceCustodyResumePending ||
            !Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            HasJusticeCustodyOperation(JusticeOperationKind.DiscardInventory) ||
            (_justiceCaseState.Phase != JusticePhase.Incarcerated &&
             _justiceCaseState.Phase != JusticePhase.Escaping)) return false;

        JusticeCustodyLayout layout = GetJusticeCustodyLayout();
        if (layout == null) return false;
        Vector3 position = player.Position;
        bool missingGeometry = IsJusticeCustodyPositionBelowPlayableFloor(layout, position);
        if (!missingGeometry && layout.Site == JusticeCustodySite.MissionRow &&
            IsInsideJusticeCustodyLayout(layout, position) &&
            Math.Abs(position.Z - layout.CellPosition.Z) <= 1.5f &&
            JusticeCustodyHasReached(now, _justiceNextCustodyGeometryCheckAt))
        {
            _justiceNextCustodyGeometryCheckAt = JusticeCustodyFutureTime(now, 1000);
            missingGeometry = !IsJusticeCustodyInteriorStreamingReadyAt(player, layout.Site) &&
                !IsJusticeCustodyLocalFloorReady(player, position);
        }
        if (!missingGeometry) return false;

        InterruptJusticeCustodyEscapeObservation();
        ResetJusticeCustodyClock(now);
        _justiceCustodyContainmentEstablished = false;
        _justiceCustodyResumePending = true;
        ResetJusticeCustodyTransferRetryState();
        ResetJusticeCustodyDestinationStreaming();
        _justiceCustodyRespawnTransferPending = true;
        _justiceCustodyRespawnRestorePending = false;
        SetJusticePreJudgmentHoldingIntent(
            GetJusticeCustodyAdmissionHoldingSource(),
            _justiceCustodyPlayerSlot, _justiceCustodyPlayerModelHash);
        EnsureJusticePreJudgmentHoldingStreamingState(
            player, layout.CellPosition + new Vector3(0.0f, 0.0f, 0.35f), layout.CellHeading);
        player.FreezePosition = true;
        ReassertJusticeCustodyRespawnTransferMask();
        EnforceJusticePreJudgmentHoldingControlLock(player);
        LogWarning("Justice.ChargementDetention.Reprise",
            "Plancher de détention perdu : reprise physique du même épisode, sans nouvelle peine ni évasion.");
        return true;
    }

    private void ResetJusticeCustodyStreamingRecovery()
    {
        ResetJusticeCustodyDestinationStreaming();
        _justiceCustodyStreamingFallbackToPrison = false;
        _justiceCustodyStreamingTechnicalFailure = false;
        _justiceCustodyRecoveryOwnerSlot = -1;
        _justiceCustodyRecoveryPlayerHandle = 0;
        _justiceCustodyRecoveryPlayerModel = 0;
        _justiceCustodyRecoveryEpisodeId = string.Empty;
        _justiceNextCustodyGeometryCheckAt = 0;
    }
}
