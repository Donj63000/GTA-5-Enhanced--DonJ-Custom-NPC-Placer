using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using GTA;
using GTA.Native;

internal sealed class JusticeAmmoPersistenceSnapshot
{
    internal JusticeAmmoPersistenceSnapshot(int typeHash, int ammo, bool attempted = false, bool completed = false)
    { TypeHash = typeHash; Ammo = ammo; RestoreAttempted = attempted; RestoreCompleted = completed; }
    internal int TypeHash { get; }
    internal int Ammo { get; }
    internal bool RestoreAttempted { get; }
    internal bool RestoreCompleted { get; }
}

public sealed partial class DonJEnemySpawner
{
    private const ulong JusticeNativeGetAmmoType = 0x7FEAD38B326B9F74UL;
    private const ulong JusticeNativeGetAmmoByType = 0x39D22031557946C1UL;
    private const ulong JusticeNativeSetAmmoByType = 0x5FD1E1F011E76D7EUL;
    private int _justiceNextCustodyPersonalEffectsAt;

    private sealed class JusticeAmmoSnapshotItem
    {
        internal int TypeHash;
        internal int Ammo;
        internal bool RestoreAttempted;
        internal bool RestoreCompleted;
    }

    private static int ReadJusticeAmmoPool(Ped player, int type)
    {
        int ammo = Function.Call<int>((Hash)JusticeNativeGetAmmoByType, player.Handle, type);
        if (ammo < 0 || ammo > 1000000) throw new InvalidOperationException("Réserve de munitions illisible.");
        return ammo;
    }

    private static bool CaptureJusticeAmmoPools(Ped player, JusticeWeaponSnapshot snapshot, List<int> weaponHashes)
    {
        HashSet<int> seen = new HashSet<int>();
        // Je capture aussi les réserves des armes absentes. Le type effectif
        // tient compte des chargeurs spéciaux Mk II déjà montés sur les armes.
        foreach (int weapon in weaponHashes)
        {
            int type = Function.Call<int>((Hash)JusticeNativeGetAmmoType, player.Handle, weapon);
            foreach (JusticeWeaponSnapshotItem item in snapshot.Weapons)
                if (item.WeaponHash == weapon) item.AmmoTypeHash = type;
            if (type == 0 || !seen.Add(type)) continue;
            int ammo = ReadJusticeAmmoPool(player, type);
            snapshot.AmmoPools.Add(new JusticeAmmoSnapshotItem { TypeHash = type, Ammo = ammo });
            if (snapshot.AmmoPools.Count > JusticeCustodyMaxWeapons) return false;
        }
        return true;
    }

    private static bool ValidateJusticeAmmoPools(JusticeWeaponSnapshot snapshot)
    {
        if (snapshot.AmmoPools.Count > JusticeCustodyMaxWeapons) return false;
        HashSet<int> types = new HashSet<int>();
        foreach (JusticeAmmoSnapshotItem pool in snapshot.AmmoPools)
            if (pool == null || pool.TypeHash == 0 || !types.Add(pool.TypeHash) || pool.Ammo < 0 || pool.Ammo > 1000000 ||
                (pool.RestoreCompleted && !pool.RestoreAttempted) ||
                (pool.RestoreAttempted && string.IsNullOrEmpty(snapshot.RestoreId))) return false;
        foreach (JusticeWeaponSnapshotItem weapon in snapshot.Weapons)
            if (weapon != null && weapon.AmmoTypeHash != 0 && !types.Contains(weapon.AmmoTypeHash)) return false;
        return true;
    }

    private bool VerifyJusticeConfiscatedAmmo(Ped player)
    {
        if (_justiceWeaponSnapshot == null) return true;
        try
        {
            foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
                if (ReadJusticeAmmoPool(player, pool.TypeHash) != 0) return false;
            return true;
        }
        catch { return false; }
    }

    private void ClearJusticeConfiscatedAmmo(Ped player)
    {
        foreach (JusticeWeaponSnapshotItem item in _justiceWeaponSnapshot.Weapons)
            if (item.AmmoTypeHash != 0 && Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, item.WeaponHash, false))
            {
                // Je laisse RemoveAll et la vérification finale garantir le retrait
                // même si la lecture d'un chargeur est devenue indisponible.
                TryClearJusticeWeaponClipIfSupported(player, item.WeaponHash);
                Function.Call((Hash)JusticeNativeSetPedAmmo, player.Handle, item.WeaponHash, 0, false);
            }
        foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
            if (ReadJusticeAmmoPool(player, pool.TypeHash) != 0)
                Function.Call((Hash)JusticeNativeSetAmmoByType, player.Handle, pool.TypeHash, 0);
    }

    private void MaintainJusticeCustodyPersonalEffects(Ped player, int now)
    {
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyPersonalEffectsAt)) return;
        if (!JusticeIsCustodyActive || _justiceCaseState == null || _justiceCaseState.Phase != JusticePhase.Incarcerated ||
            _justiceInventoryCustodyState != JusticeInventoryCustodyState.RemovedVerified || !_justiceInventoryRemoved ||
            !Entity.Exists(player) || player.IsDead || !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            !JusticeCustodyHasReached(now, _justiceNextCustodyPersonalEffectsAt)) return;
        _justiceNextCustodyPersonalEffectsAt = JusticeCustodyFutureTime(now, JusticeCustodyPersonalEffectsCheckMs);
        try
        {
            List<int> weapons = new List<int>();
            if (!TryCollectJusticeWeaponHashes(new HashSet<int>(), weapons)) return;
            bool foundWeapon = false;
            HashSet<int> pools = new HashSet<int>();
            foreach (int weapon in weapons)
            {
                if (weapon == JusticeUnarmedHash) continue;
                int type = Function.Call<int>((Hash)JusticeNativeGetAmmoType, player.Handle, weapon);
                if (type != 0) pools.Add(type);
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, weapon, false)) continue;
                foundWeapon = true;
                if (type != 0)
                {
                    Function.Call(Hash.SET_AMMO_IN_CLIP, player.Handle, weapon, 0);
                    Function.Call((Hash)JusticeNativeSetPedAmmo, player.Handle, weapon, 0, false);
                }
            }
            foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools) pools.Add(pool.TypeHash);
            foreach (int pool in pools)
                if (ReadJusticeAmmoPool(player, pool) != 0)
                    Function.Call((Hash)JusticeNativeSetAmmoByType, player.Handle, pool, 0);
            // Je ne remplace jamais le dépôt initial par une arme récupérée en prison.
            // Un inventaire vide ne reçoit aucun nouvel appel destructif.
            if (foundWeapon)
            {
                JusticeInventoryRemovalResult result = RemoveJusticePlayerWeaponsSafe(player);
                if (result != JusticeInventoryRemovalResult.RemovedVerified)
                {
                    RegisterJusticeInventoryRemovalFailure(result, now);
                    LogWarning("Justice.Inventaire.Maintien",
                        "Arme acquise pendant la détention : retrait non vérifié, dépôt conservé et utilisation interdite.");
                }
            }
            foreach (int pool in pools)
                if (ReadJusticeAmmoPool(player, pool) != 0)
                    throw new InvalidOperationException("Retrait des munitions de détention non vérifié.");
        }
        catch (Exception ex) { LogException("Justice.Inventaire.Maintien", ex); }
    }

    private bool RestoreJusticeAmmoPoolsExact(Ped player)
    {
        bool restored = true;
        foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
        {
            try
            {
                if (ReadJusticeAmmoPool(player, pool.TypeHash) != pool.Ammo)
                    Function.Call((Hash)JusticeNativeSetAmmoByType, player.Handle, pool.TypeHash, pool.Ammo);
                restored &= ReadJusticeAmmoPool(player, pool.TypeHash) == pool.Ammo;
            }
            catch { restored = false; }
        }
        return restored;
    }

    private static List<JusticeAmmoPersistenceSnapshot> CaptureJusticeAmmoPersistence(JusticeWeaponSnapshot source)
    {
        List<JusticeAmmoPersistenceSnapshot> result = new List<JusticeAmmoPersistenceSnapshot>();
        foreach (JusticeAmmoSnapshotItem pool in source.AmmoPools)
            result.Add(new JusticeAmmoPersistenceSnapshot(pool.TypeHash, pool.Ammo, pool.RestoreAttempted, pool.RestoreCompleted));
        return result;
    }

    private static void WriteJusticeAmmoPoolsXml(XmlWriter writer, IEnumerable<JusticeAmmoPersistenceSnapshot> pools)
    {
        foreach (JusticeAmmoPersistenceSnapshot pool in pools)
        {
            writer.WriteStartElement("AmmoPool");
            WriteJusticePersistenceAttribute(writer, "type", pool.TypeHash);
            WriteJusticePersistenceAttribute(writer, "ammo", pool.Ammo);
            WriteJusticePersistenceAttribute(writer, "restoreAttempted", pool.RestoreAttempted);
            WriteJusticePersistenceAttribute(writer, "restoreCompleted", pool.RestoreCompleted);
            writer.WriteEndElement();
        }
    }

    private static bool ReadJusticeAmmoPoolsXml(XmlElement element, JusticeWeaponSnapshot snapshot)
    {
        XmlNodeList nodes = element.SelectNodes("AmmoPool");
        if (nodes.Count > JusticeCustodyMaxWeapons) return false;
        foreach (XmlElement node in nodes)
        {
            int type, ammo; bool attempted, completed;
            if (!node.HasAttribute("type") || !node.HasAttribute("ammo") ||
                !TryReadJusticeIntStrict(node, "type", 0, int.MinValue, int.MaxValue, out type) ||
                !TryReadJusticeIntStrict(node, "ammo", 0, 0, 1000000, out ammo) ||
                !TryReadJusticeBoolStrict(node, "restoreAttempted", false, out attempted) ||
                !TryReadJusticeBoolStrict(node, "restoreCompleted", false, out completed)) return false;
            snapshot.AmmoPools.Add(new JusticeAmmoSnapshotItem { TypeHash = type, Ammo = ammo,
                RestoreAttempted = attempted, RestoreCompleted = completed });
        }
        return ValidateJusticeAmmoPools(snapshot);
    }

    private bool RestoreJusticeDeferredAmmo(Ped player)
    {
        bool complete = true;
        int budget = 4;
        foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
        {
            if (pool.RestoreCompleted) continue;
            if (budget-- <= 0) { complete = false; continue; }
            if (pool.RestoreAttempted)
            {
                // Je ne recharge jamais une réserve potentiellement déjà rendue
                // puis consommée. Le WAL interdit le rejeu après perte d'ACK.
                pool.RestoreCompleted = true;
                JusticeMarkStateDirty();
                continue;
            }
            bool weaponsReady = true;
            foreach (JusticeWeaponSnapshotItem weapon in _justiceWeaponSnapshot.Weapons)
                if (weapon.AmmoTypeHash == pool.TypeHash && !weapon.DeferredRestoreCompleted) weaponsReady = false;
            if (!weaponsReady) { complete = false; continue; }
            try
            {
                int current = ReadJusticeAmmoPool(player, pool.TypeHash);
                JusticeWalRecord operation = BeginJusticeDeferredAmmoRestore(pool);
                if (operation == null) { complete = false; continue; }
                pool.RestoreAttempted = true;
                JusticeMarkStateDirty();
                // Je conserve les munitions déjà présentes après une restitution
                // partielle; seuls les dépôts encore vides sont rendus une fois.
                if (current == 0)
                {
                    Function.Call((Hash)JusticeNativeSetAmmoByType, player.Handle, pool.TypeHash, pool.Ammo);
                    if (ReadJusticeAmmoPool(player, pool.TypeHash) != pool.Ammo)
                    {
                        _justiceWriteAheadLog.Append(new JusticeWalRecord(operation.TransactionId, operation.OperationKind,
                            operation.ProfileSlot, JusticeWalState.Rejected, operation.PersistenceRevision,
                            operation.CreatedAtUtcTicks, operation.Fields));
                        pool.RestoreAttempted = false;
                        complete = false;
                        continue;
                    }
                    foreach (JusticeWeaponSnapshotItem weapon in _justiceWeaponSnapshot.Weapons)
                        if (weapon.AmmoTypeHash == pool.TypeHash &&
                            Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, weapon.WeaponHash, false))
                            if (!RestoreJusticeWeaponClipIfSupported(player, weapon))
                                throw new InvalidOperationException("Chargeur rendu non vérifié.");
                    // Je normalise une seule fois le total partagé après les chargeurs.
                    if (ReadJusticeAmmoPool(player, pool.TypeHash) != pool.Ammo)
                        Function.Call((Hash)JusticeNativeSetAmmoByType, player.Handle, pool.TypeHash, pool.Ammo);
                }
                pool.RestoreCompleted = true;
                JusticeMarkStateDirty();
            }
            catch (Exception ex) { complete = false; LogException("Justice.Munitions.Restitution", ex); }
        }
        return complete;
    }

    private void RememberJusticeAmmoAlreadyReturned(Ped player)
    {
        if (_justiceWeaponSnapshot == null) return;
        if (string.IsNullOrEmpty(_justiceWeaponSnapshot.RestoreId))
            _justiceWeaponSnapshot.RestoreId = Guid.NewGuid().ToString("N");
        foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
        {
            try
            {
                if (ReadJusticeAmmoPool(player, pool.TypeHash) == 0) continue;
            }
            catch
            {
                // Je conserve une réserve illisible comme déjà tentée pour
                // empêcher un remplissage aveugle après déverrouillage du joueur.
            }
            pool.RestoreAttempted = true;
            pool.RestoreCompleted = true;
        }
    }

    private JusticeWalRecord BeginJusticeDeferredAmmoRestore(JusticeAmmoSnapshotItem pool)
    {
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)) return null;
        try
        {
            JusticeWalRecord record = _justiceWriteAheadLog.Append(new JusticeWalRecord(
                "restore:" + Guid.NewGuid().ToString("N"), JusticeInventoryRestoreWalKind,
                _justiceActivePlayerProfileSlot, JusticeWalState.Prepared,
                _justiceRepository.GetDiagnostics().DiskRevision, DateTime.UtcNow.Ticks,
                new[] { new JusticePersistenceField("restoreId", _justiceWeaponSnapshot.RestoreId),
                    new JusticePersistenceField("ammoTypeHash", pool.TypeHash.ToString(CultureInfo.InvariantCulture)),
                    new JusticePersistenceField("schemaMajor", "2") }));
            return _justiceWriteAheadLog.Append(new JusticeWalRecord(record.TransactionId, record.OperationKind,
                record.ProfileSlot, JusticeWalState.Attempted, record.PersistenceRevision, record.CreatedAtUtcTicks, record.Fields));
        }
        catch (Exception ex)
        {
            foreach (JusticeWalRecord record in _justiceWriteAheadLog.GetOpenTransactions())
                if (record.OperationKind == JusticeInventoryRestoreWalKind && record.State != JusticeWalState.Prepared &&
                    record.ProfileSlot == _justiceActivePlayerProfileSlot &&
                    ReadWalString(record, "restoreId", "") == _justiceWeaponSnapshot.RestoreId &&
                    ReadWalInt(record, "ammoTypeHash", 0) == pool.TypeHash) pool.RestoreAttempted = true;
            LogException("Justice.Munitions.WAL", ex);
            return null;
        }
    }
}
