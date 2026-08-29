using System;
using GTA;

public sealed partial class DonJEnemySpawner
{
    [Flags]
    private enum PlayerInvincibilityOwner
    {
        None = 0,
        Placement = 1,
        JusticeDiscipline = 2
    }

    /*
     * Plusieurs fonctions du mod protègent temporairement le joueur. Elles ne
     * doivent jamais mémoriser/restaurer IsInvincible chacune de leur côté :
     * un propriétaire démarré pendant un autre capturerait alors "true" comme
     * état initial et pourrait rendre cette valeur permanente à sa sortie.
     *
     * Ce petit gestionnaire fonctionne comme un compteur de propriétaires :
     * - le premier capture l'état externe réel ;
     * - les suivants partagent ce même état initial ;
     * - seul le dernier à sortir le restaure ;
     * - une restauration qui échoue reste en attente et est retentée au tick.
     *
     * Je ne force donc jamais arbitrairement IsInvincible à false. Si un autre
     * mod avait déjà rendu le joueur invincible avant DonJ, sa valeur true est
     * conservée après la fin de nos protections.
     */
    private PlayerInvincibilityOwner _playerInvincibilityOwners;
    private Ped _playerInvincibilityPed;
    private int _playerInvincibilityPedHandle;
    private bool _playerInvincibilityBaseline;
    private bool _playerInvincibilityBaselineCaptured;
    private bool _playerInvincibilityRestorePending;

    private bool TryAcquirePlayerInvincibility(
        Ped player,
        PlayerInvincibilityOwner owner,
        out bool originalInvincibility)
    {
        originalInvincibility = false;
        if (owner == PlayerInvincibilityOwner.None || !IsExistingPlayerEntity(player))
        {
            return false;
        }

        int playerHandle;
        try
        {
            playerHandle = player.Handle;
        }
        catch
        {
            return false;
        }

        if (playerHandle == 0)
        {
            return false;
        }

        bool hasSharedState = _playerInvincibilityBaselineCaptured ||
                              _playerInvincibilityRestorePending ||
                              _playerInvincibilityOwners != PlayerInvincibilityOwner.None;
        if (hasSharedState)
        {
            if (!IsTrackedPlayerInvincibilityPed(player))
            {
                return false;
            }

            originalInvincibility = _playerInvincibilityBaseline;
            if (!TryWritePlayerInvincibility(player, true))
            {
                return false;
            }

            _playerInvincibilityOwners |= owner;
            _playerInvincibilityRestorePending = false;
            return true;
        }

        bool baseline;
        try
        {
            baseline = player.IsInvincible;
        }
        catch
        {
            return false;
        }

        originalInvincibility = baseline;
        _playerInvincibilityPed = player;
        _playerInvincibilityPedHandle = playerHandle;
        _playerInvincibilityBaseline = baseline;
        _playerInvincibilityBaselineCaptured = true;
        _playerInvincibilityRestorePending = false;

        // Le setter peut avoir modifié le ped avant de lever. L'état partagé est
        // donc installé avant l'écriture afin de toujours pouvoir revenir en arrière.
        if (TryWritePlayerInvincibility(player, true))
        {
            _playerInvincibilityOwners = owner;
            return true;
        }

        _playerInvincibilityOwners = PlayerInvincibilityOwner.None;
        if (TryWritePlayerInvincibility(player, baseline))
        {
            ClearPlayerInvincibilityProtectionState();
        }
        else
        {
            _playerInvincibilityRestorePending = true;
        }

        return false;
    }

    private bool TryReleasePlayerInvincibility(
        Ped preferredPlayer,
        PlayerInvincibilityOwner owner,
        bool fallbackBaseline,
        bool allowUntrackedFallback)
    {
        bool ownerWasRegistered =
            (_playerInvincibilityOwners & owner) == owner &&
            owner != PlayerInvincibilityOwner.None;
        if (ownerWasRegistered)
        {
            _playerInvincibilityOwners &= ~owner;
        }

        if (_playerInvincibilityOwners != PlayerInvincibilityOwner.None)
        {
            // Un autre domaine DonJ protège encore le même joueur. La sortie du
            // propriétaire demandé est terminée, mais l'invincibilité doit rester.
            Ped sharedPlayer = ResolveTrackedPlayerInvincibilityPed(preferredPlayer);
            return !object.ReferenceEquals(sharedPlayer, null) &&
                   TryWritePlayerInvincibility(sharedPlayer, true);
        }

        if (_playerInvincibilityBaselineCaptured ||
            _playerInvincibilityRestorePending)
        {
            return TryRestoreSharedPlayerInvincibility(preferredPlayer);
        }

        if (!ownerWasRegistered && allowUntrackedFallback)
        {
            // Reprise après rechargement : Justice peut avoir une intention
            // persistée sans propriétaire runtime. Sa valeur durable reste alors
            // le seul état initial fiable disponible. Ce chemin est explicitement
            // interdit au placement : si sa capture a échoué avant toute écriture,
            // sa valeur par défaut ne doit jamais écraser l'état d'un autre mod.
            bool existenceKnown = TryGetPlayerEntityExistence(
                preferredPlayer,
                out bool playerExists);
            return (existenceKnown && !playerExists) ||
                   TryWritePlayerInvincibility(preferredPlayer, fallbackBaseline);
        }

        return true;
    }

    private bool HasPlayerInvincibilityOwner(PlayerInvincibilityOwner owner)
    {
        return owner != PlayerInvincibilityOwner.None &&
               (_playerInvincibilityOwners & owner) == owner;
    }

    private bool IsPlayerInvincibilityRecoveryPending()
    {
        return _playerInvincibilityRestorePending;
    }

    private void MaintainPlayerInvincibilityProtection()
    {
        if (_playerInvincibilityOwners != PlayerInvincibilityOwner.None)
        {
            Ped player = ResolveTrackedPlayerInvincibilityPed(null);
            if (!object.ReferenceEquals(player, null))
            {
                TryWritePlayerInvincibility(player, true);
            }
            else if (IsKnownMissingPlayerEntity(_playerInvincibilityPed))
            {
                // Le ped protégé n'existe plus : aucun drapeau runtime ne peut
                // subsister sur cette entité détruite et le handle ne doit pas
                // être réutilisé plus tard comme s'il s'agissait du même joueur.
                ClearPlayerInvincibilityProtectionState();
            }

            return;
        }

        if (_playerInvincibilityRestorePending ||
            _playerInvincibilityBaselineCaptured)
        {
            TryRestoreSharedPlayerInvincibility(null);
        }
    }

    private void ShutdownPlayerInvincibilityProtection()
    {
        _playerInvincibilityOwners = PlayerInvincibilityOwner.None;
        for (int attempt = 0;
             attempt < 3 &&
             (_playerInvincibilityBaselineCaptured ||
              _playerInvincibilityRestorePending);
             attempt++)
        {
            if (TryRestoreSharedPlayerInvincibility(null))
            {
                break;
            }
        }
    }

    private bool TryRestoreSharedPlayerInvincibility(Ped preferredPlayer)
    {
        if (!_playerInvincibilityBaselineCaptured &&
            !_playerInvincibilityRestorePending)
        {
            return true;
        }

        Ped player = ResolveTrackedPlayerInvincibilityPed(preferredPlayer);
        if (object.ReferenceEquals(player, null))
        {
            if (IsKnownMissingPlayerEntity(_playerInvincibilityPed))
            {
                ClearPlayerInvincibilityProtectionState();
                return true;
            }

            _playerInvincibilityRestorePending = true;
            return false;
        }

        if (!TryWritePlayerInvincibility(player, _playerInvincibilityBaseline))
        {
            _playerInvincibilityRestorePending = true;
            return false;
        }

        ClearPlayerInvincibilityProtectionState();
        return true;
    }

    private Ped ResolveTrackedPlayerInvincibilityPed(Ped preferredPlayer)
    {
        if (IsTrackedPlayerInvincibilityPed(preferredPlayer))
        {
            return preferredPlayer;
        }

        if (IsTrackedPlayerInvincibilityPed(_playerInvincibilityPed))
        {
            return _playerInvincibilityPed;
        }

        try
        {
            Ped currentPlayer = Game.Player.Character;
            if (IsTrackedPlayerInvincibilityPed(currentPlayer))
            {
                return currentPlayer;
            }
        }
        catch
        {
        }

        return null;
    }

    private bool IsTrackedPlayerInvincibilityPed(Ped player)
    {
        if (!IsExistingPlayerEntity(player) || _playerInvincibilityPedHandle == 0)
        {
            return false;
        }

        try
        {
            return player.Handle == _playerInvincibilityPedHandle;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWritePlayerInvincibility(Ped player, bool value)
    {
        if (!IsExistingPlayerEntity(player))
        {
            return false;
        }

        try
        {
            player.IsInvincible = value;
            return player.IsInvincible == value;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPlayerEntityExistence(Ped player, out bool exists)
    {
        exists = false;
        if (object.ReferenceEquals(player, null))
        {
            return true;
        }

        try
        {
            exists = Entity.Exists(player);
            return true;
        }
        catch
        {
            // Une erreur native transitoire n'est pas une preuve que le ped a
            // disparu. Les restaurations doivent alors être retentées, jamais
            // abandonnées en effaçant leur baseline.
            return false;
        }
    }

    private static bool IsExistingPlayerEntity(Ped player)
    {
        return TryGetPlayerEntityExistence(player, out bool exists) && exists;
    }

    private static bool IsKnownMissingPlayerEntity(Ped player)
    {
        return TryGetPlayerEntityExistence(player, out bool exists) && !exists;
    }

    private void ClearPlayerInvincibilityProtectionState()
    {
        _playerInvincibilityOwners = PlayerInvincibilityOwner.None;
        _playerInvincibilityPed = null;
        _playerInvincibilityPedHandle = 0;
        _playerInvincibilityBaseline = false;
        _playerInvincibilityBaselineCaptured = false;
        _playerInvincibilityRestorePending = false;
    }
}
