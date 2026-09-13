using System;
using System.Globalization;
using GTA;
using GTA.Native;

public sealed partial class DonJEnemySpawner
{
    private const int JusticeCustodyPreservedInventoryRetryMs = 5000;
    private const ulong JusticeNativeGetWeaponGroup = 0xC3287EE3050FB74CUL;
    private const ulong JusticeNativeGetWeaponClipSize = 0x583BE370B1EC6EB4UL;
    private const ulong JusticeNativeGetMaxAmmoInClip = 0xA38DCFFCEA8962FAUL;
    private string _justiceInventoryCaptureStage = string.Empty;
    private int _justiceInventoryCaptureWeaponHash;
    private int _justiceInventoryCaptureDlcIndex = -1;
    private string _justiceInventorySelectionEpisode = string.Empty;
    private int _justiceInventorySelectionSlot = -1;
    private int _justiceInventorySelectionModel;
    private int _justiceInventorySelectionHash;

    private bool RememberJusticeCustodyWeaponSelectionBeforeLock(Ped player)
    {
        if (_justiceWeaponSnapshot != null && _justiceWeaponSnapshot.IsValidated) return true;
        int model = GetJusticePedModelHashSafe(player);
        bool sameOwner = _justiceCaseState != null &&
            _justiceInventorySelectionEpisode == _justiceCaseState.CustodyEpisodeId &&
            _justiceInventorySelectionSlot == _justiceCustodyPlayerSlot &&
            _justiceInventorySelectionModel == model && _justiceInventorySelectionHash != 0;
        if (!sameOwner)
        {
            int selected;
            try { selected = Function.Call<int>((Hash)JusticeNativeGetSelectedPedWeapon, player.Handle); }
            catch { return false; }
            if (selected == 0) return false;
            _justiceInventorySelectionEpisode = _justiceCaseState.CustodyEpisodeId;
            _justiceInventorySelectionSlot = _justiceCustodyPlayerSlot;
            _justiceInventorySelectionModel = model;
            _justiceInventorySelectionHash = _justiceCaseState.Phase != JusticePhase.Captured &&
                _justiceReleaseSelectedWeaponHash != JusticeUnarmedHash && _justiceReleaseSelectedWeaponHash != 0
                    ? _justiceReleaseSelectedWeaponHash : selected;
        }
        // Je conserve l'arme d'entrée avant d'imposer les poings. Le champ XML
        // historique n'est renseigné qu'après Captured, dont le contrat reste vide.
        if (_justiceCaseState.Phase != JusticePhase.Captured &&
            _justiceReleaseSelectedWeaponHash != _justiceInventorySelectionHash)
        {
            _justiceReleaseSelectedWeaponHash = _justiceInventorySelectionHash;
            JusticeMarkStateDirty();
        }
        return true;
    }

    private int ReadJusticeCustodySnapshotSelectedWeapon(Ped player)
    {
        if (_justiceCaseState != null && _justiceInventorySelectionEpisode == _justiceCaseState.CustodyEpisodeId &&
            _justiceInventorySelectionSlot == _justiceCustodyPlayerSlot &&
            _justiceInventorySelectionModel == GetJusticePedModelHashSafe(player) && _justiceInventorySelectionHash != 0)
            return _justiceInventorySelectionHash;
        return Function.Call<int>((Hash)JusticeNativeGetSelectedPedWeapon, player.Handle);
    }

    private void ResetJusticeCustodyInventorySelection()
    {
        _justiceInventorySelectionEpisode = string.Empty;
        _justiceInventorySelectionSlot = -1;
        _justiceInventorySelectionModel = 0;
        _justiceInventorySelectionHash = 0;
    }

    private void TraceJusticeInventoryCaptureFailure()
    {
        LogWarning("Justice.Inventaire.Capture",
            "Snapshot refusé : étape=" + _justiceInventoryCaptureStage +
            ", arme=0x" + unchecked((uint)_justiceInventoryCaptureWeaponHash).ToString("X8", CultureInfo.InvariantCulture) +
            ", indexDlc=" + _justiceInventoryCaptureDlcIndex.ToString(CultureInfo.InvariantCulture) +
            ", tentative=" + _justiceInventoryCaptureFailureCount.ToString(CultureInfo.InvariantCulture) +
            ". Je conserve le dépôt et interdis l'utilisation des armes en détention.");
    }

    private static bool IsJusticeWeaponGroupWithoutMagazine(int group)
    {
        // Je reconnais uniquement les catégories sans chargeur et les gadgets.
        // Une arme à feu inconnue ne devient jamais lisible par défaut.
        switch (unchecked((uint)group))
        {
            case 3566412244U: // Je reconnais les armes de mêlée.
            case 1548507267U: // Je reconnais les projectiles lancés.
            case 1595662460U: // Je reconnais les bidons.
            case 4257178988U: // Je reconnais les extincteurs.
            case 431593103U:  // Je reconnais les parachutes.
            case 3493187224U: // Je reconnais la vision nocturne.
            case 3539449195U: // Je reconnais les scanners.
            case 1175761940U: // Je reconnais les appareils de piratage.
            case 3759491383U: // Je reconnais les détecteurs de métaux.
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadJusticeWeaponClip(Ped player, int weaponHash, out int ammo, out bool hasClip)
    {
        ammo = 0;
        hasClip = false;
        if (!Entity.Exists(player) || player.IsDead) return false;
        try
        {
            OutputArgument output = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_AMMO_IN_CLIP, player.Handle, weaponHash, output))
            {
                ammo = output.GetResult<int>();
                hasClip = true;
                return ammo >= 0 && ammo <= 1000000;
            }

            int group = Function.Call<int>((Hash)JusticeNativeGetWeaponGroup, weaponHash);
            if (!IsJusticeWeaponGroupWithoutMagazine(group)) return false;
            int defaultCapacity = Function.Call<int>((Hash)JusticeNativeGetWeaponClipSize, weaponHash);
            int capacity = Function.Call<int>((Hash)JusticeNativeGetMaxAmmoInClip, player.Handle, weaponHash, true);
            // Je n'accepte un chargeur absent qu'avec deux capacités nulles et
            // une catégorie reconnue. Un faux retour sur un pistolet reste bloquant.
            return defaultCapacity == 0 && capacity == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool RestoreJusticeWeaponClipIfSupported(Ped player, JusticeWeaponSnapshotItem item)
    {
        int current;
        bool hasClip;
        if (!TryReadJusticeWeaponClip(player, item.WeaponHash, out current, out hasClip)) return false;
        if (!hasClip) return item.AmmoInClip == 0;
        if (current == item.AmmoInClip) return true;
        Function.Call(Hash.SET_AMMO_IN_CLIP, player.Handle, item.WeaponHash, item.AmmoInClip);
        return TryReadJusticeWeaponClip(player, item.WeaponHash, out current, out hasClip) &&
            hasClip && current == item.AmmoInClip;
    }

    private static bool TryClearJusticeWeaponClipIfSupported(Ped player, int weaponHash)
    {
        int current;
        bool hasClip;
        if (!TryReadJusticeWeaponClip(player, weaponHash, out current, out hasClip)) return false;
        if (hasClip && current != 0) Function.Call(Hash.SET_AMMO_IN_CLIP, player.Handle, weaponHash, 0);
        return true;
    }

    private static bool IsJusticePlayerUnarmedVerified(Ped player)
    {
        try
        {
            return Function.Call<int>((Hash)JusticeNativeGetSelectedPedWeapon, player.Handle) == JusticeUnarmedHash;
        }
        catch
        {
            return false;
        }
    }
}
