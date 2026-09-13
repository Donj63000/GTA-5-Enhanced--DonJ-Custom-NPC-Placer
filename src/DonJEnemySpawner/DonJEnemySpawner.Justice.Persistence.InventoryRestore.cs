using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Native;

public sealed partial class DonJEnemySpawner
{
    private const string JusticeInventoryRestoreWalKind = "InventoryRestoreResult";
    private string _justiceInventoryBarrierKey;
    private long _justiceInventoryBarrierCandidate;
    private long _justiceInventoryBarrierFirstProof;
    private string _justiceInventoryPreparedRestoreId;
    private int _justiceInventoryPreparedRestoreSlot = -1;
    private Dictionary<string, long> _justiceInventoryResultCandidates;
    private Dictionary<string, long> _justiceInventoryResultProofs;

    private bool PersistJusticeDeferredRestoreRedundantly()
    {
        if (_justiceWeaponSnapshot == null) return false;
        if (string.IsNullOrEmpty(_justiceWeaponSnapshot.RestoreId))
        {
            _justiceWeaponSnapshot.RestoreId = Guid.NewGuid().ToString("N");
            JusticeMarkStateDirty();
        }
        StringBuilder key = new StringBuilder(_justiceWeaponSnapshot.RestoreId);
        key.Append(':').Append(_justiceActivePlayerProfileSlot).Append(':').Append(_justiceDeferredInventoryRestore);
        foreach (JusticeWeaponSnapshotItem item in _justiceWeaponSnapshot.Weapons)
            key.Append(item.DeferredRestoreCompleted ? '2' : item.DeferredRestoreAttempted ? '1' : '0');
        foreach (JusticeAmmoSnapshotItem pool in _justiceWeaponSnapshot.AmmoPools)
            key.Append(pool.RestoreCompleted ? '2' : pool.RestoreAttempted ? '1' : '0');
        string currentKey = key.ToString();
        if (!string.Equals(currentKey, _justiceInventoryBarrierKey, StringComparison.Ordinal))
        {
            _justiceInventoryBarrierKey = currentKey;
            _justiceInventoryBarrierCandidate = 0L;
            _justiceInventoryBarrierFirstProof = 0L;
        }
        long disk = _justiceRepository == null ? 0L : _justiceRepository.GetDiagnostics().DiskRevision;
        if (_justiceInventoryBarrierCandidate > 0L)
        {
            if (disk < _justiceInventoryBarrierCandidate) return false;
            if (disk == _justiceInventoryBarrierCandidate)
            {
                if (_justiceInventoryBarrierFirstProof > 0L && disk > _justiceInventoryBarrierFirstProof)
                    return true;
                _justiceInventoryBarrierFirstProof = disk;
            }
            // Je recapture une révision sautée et attends une seconde rotation
            // distincte après la preuve exacte du primaire.
            _justiceInventoryBarrierCandidate = 0L;
        }
        JusticeMarkStateDirty();
        if (JusticeFlushStateNow()) _justiceInventoryBarrierCandidate = _justiceLastQueuedPersistenceRevision;
        return false;
    }

    private bool RestoreJusticeDeferredWeapons(Ped player)
    {
        if (!Entity.Exists(player) || !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot)) return false;
        if (_justiceInventoryPreparedRestoreId != _justiceWeaponSnapshot.RestoreId ||
            _justiceInventoryPreparedRestoreSlot != _justiceActivePlayerProfileSlot)
        {
            if (!PersistJusticeDeferredRestoreRedundantly()) return false;
            _justiceInventoryPreparedRestoreId = _justiceWeaponSnapshot.RestoreId;
            _justiceInventoryPreparedRestoreSlot = _justiceActivePlayerProfileSlot;
        }
        // Je prouve le snapshot initial une fois par restitution et proprietaire.
        // Le WAL protege ensuite chaque lot sans deux attentes disque par lot ;
        // la cloture attend toujours les marqueurs finaux dans les deux copies.
        int processed = 0;
        bool complete = true;
        foreach (JusticeWeaponSnapshotItem item in _justiceWeaponSnapshot.Weapons)
        {
            if (item.DeferredRestoreCompleted) continue;
            if (++processed > 4) { complete = false; continue; }
            bool owned;
            try { owned = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, item.WeaponHash, false); }
            catch { complete = false; continue; }

            if (item.DeferredRestoreAttempted)
            {
                // Une tentative interrompue est ambiguë. Je garde le snapshot
                // sans redonner à l'aveugle une arme que le joueur a pu jeter.
                if (!owned) { complete = false; continue; }
                if (!RestoreJusticeMissingWeaponComponents(player, item)) { complete = false; continue; }
                item.DeferredRestoreCompleted = true;
                JusticeMarkStateDirty();
                continue;
            }

            JusticeWalRecord operation = BeginJusticeDeferredWeaponRestore(item);
            if (operation == null) { complete = false; continue; }
            item.DeferredRestoreAttempted = true;
            JusticeMarkStateDirty();
            if (!owned)
            {
                JusticeAmmoSnapshotItem projectilePool = null;
                JusticeWalRecord projectileOperation = null;
                try
                {
                    int ammunition = item.AmmoTypeHash == 0 ? item.Ammo : 0;
                    if (!PrepareJusticeDeferredProjectileAmmo(player, item, out projectilePool,
                            out projectileOperation, out ammunition))
                    {
                        // Je sais qu'aucun GIVE n'a été appelé : seule son intention
                        // peut être rejetée. Le WAL des munitions garde sa propre preuve.
                        RejectJusticeUnappliedWeaponRestore(operation, item);
                        complete = false;
                        continue;
                    }
                    Function.Call((Hash)JusticeNativeGiveWeaponToPed, player.Handle, item.WeaponHash, ammunition, false, false);
                    owned = Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, player.Handle, item.WeaponHash, false);
                    if (!owned)
                    {
                        if (projectilePool != null && ReadJusticeAmmoPool(player, projectilePool.TypeHash) != 0)
                        {
                            // Je ne rejoue pas un GIVE dont le stock existe déjà,
                            // même si GTA ne confirme pas encore la possession.
                            complete = false;
                            continue;
                        }
                        if (projectilePool != null)
                        {
                            RejectJusticeUnappliedProjectileRestore(projectileOperation, projectilePool);
                        }
                        // Le refus vérifié reste réessayable ; aucun succès n'est inventé.
                        RejectJusticeUnappliedWeaponRestore(operation, item);
                        complete = false;
                        continue;
                    }
                    if (projectilePool != null) projectilePool.RestoreCompleted = true;
                    // Je configure seulement l'arme que je viens de rendre.
                    // Une arme déjà possédée conserve tous les choix du joueur.
                    Function.Call(Hash.SET_PED_WEAPON_TINT_INDEX, player.Handle, item.WeaponHash, item.Tint);
                    if (item.AmmoTypeHash == 0 && !RestoreJusticeWeaponClipIfSupported(player, item))
                    {
                        complete = false;
                        continue;
                    }
                }
                catch
                {
                    // Le WAL Attempted protège ce résultat incertain jusqu'à la
                    // vérification suivante ; je ne rejoue jamais le stock.
                    complete = false;
                    continue;
                }
            }
            if (!RestoreJusticeMissingWeaponComponents(player, item)) { complete = false; continue; }
            item.DeferredRestoreCompleted = true;
            JusticeMarkStateDirty();
        }
        complete &= RestoreJusticeDeferredAmmo(player);
        if (_justiceStateDirty) JusticeFlushStateNow();
        return complete;
    }

    private bool PrepareJusticeDeferredProjectileAmmo(Ped player, JusticeWeaponSnapshotItem item,
        out JusticeAmmoSnapshotItem pool, out JusticeWalRecord operation, out int ammunition)
    {
        pool = null;
        operation = null;
        ammunition = item.AmmoTypeHash == 0 ? item.Ammo : 0;
        if (item.AmmoTypeHash == 0 || item.Ammo <= 0 ||
            Function.Call<int>((Hash)JusticeNativeGetWeaponGroup, item.WeaponHash) != 1548507267)
            return true;
        foreach (JusticeAmmoSnapshotItem candidate in _justiceWeaponSnapshot.AmmoPools)
        {
            if (candidate.TypeHash != item.AmmoTypeHash) continue;
            // Je conserve les stocks déjà présents ou potentiellement rendus.
            // Un projectile sans aucun stock doit recevoir son dépôt dès GIVE,
            // car GTA peut refuser de matérialiser une grenade donnée avec zéro.
            if (candidate.RestoreAttempted || ReadJusticeAmmoPool(player, candidate.TypeHash) != 0)
                return true;
            operation = BeginJusticeDeferredAmmoRestore(candidate);
            if (operation == null) return false;
            pool = candidate;
            pool.RestoreAttempted = true;
            ammunition = pool.Ammo;
            JusticeMarkStateDirty();
            return true;
        }
        return false;
    }

    private void RejectJusticeUnappliedWeaponRestore(JusticeWalRecord operation, JusticeWeaponSnapshotItem item)
    {
        _justiceWriteAheadLog.Append(new JusticeWalRecord(operation.TransactionId,
            operation.OperationKind, operation.ProfileSlot, JusticeWalState.Rejected,
            operation.PersistenceRevision, operation.CreatedAtUtcTicks, operation.Fields));
        item.DeferredRestoreAttempted = false;
    }

    private void RejectJusticeUnappliedProjectileRestore(JusticeWalRecord operation, JusticeAmmoSnapshotItem pool)
    {
        _justiceWriteAheadLog.Append(new JusticeWalRecord(operation.TransactionId,
            operation.OperationKind, operation.ProfileSlot, JusticeWalState.Rejected,
            operation.PersistenceRevision, operation.CreatedAtUtcTicks, operation.Fields));
        pool.RestoreAttempted = false;
        pool.RestoreCompleted = false;
    }

    private static bool RestoreJusticeMissingWeaponComponents(Ped player, JusticeWeaponSnapshotItem item)
    {
        // Je reprends uniquement les composants absents d'une arme incomplete.
        // Je ne reecris ni les munitions, ni la teinte, ni la selection du joueur.
        try
        {
            foreach (int component in item.ComponentHashes)
            {
                if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, item.WeaponHash, component)) continue;
                Function.Call(Hash.GIVE_WEAPON_COMPONENT_TO_PED, player.Handle, item.WeaponHash, component);
                if (!Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON_COMPONENT, player.Handle, item.WeaponHash, component)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private JusticeWalRecord BeginJusticeDeferredWeaponRestore(JusticeWeaponSnapshotItem item)
    {
        if (_justiceWriteAheadLog == null || _justiceRepository == null ||
            !IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot)) return null;
        try
        {
            JusticeWalRecord record = new JusticeWalRecord("restore:" + Guid.NewGuid().ToString("N"),
                JusticeInventoryRestoreWalKind, _justiceActivePlayerProfileSlot, JusticeWalState.Prepared,
                _justiceRepository.GetDiagnostics().DiskRevision, DateTime.UtcNow.Ticks,
                new[]
                {
                    new JusticePersistenceField("restoreId", _justiceWeaponSnapshot.RestoreId),
                    new JusticePersistenceField("weaponHash", item.WeaponHash.ToString(CultureInfo.InvariantCulture)),
                    new JusticePersistenceField("schemaMajor", "2")
                });
            record = _justiceWriteAheadLog.Append(record);
            return _justiceWriteAheadLog.Append(new JusticeWalRecord(record.TransactionId, record.OperationKind,
                record.ProfileSlot, JusticeWalState.Attempted, record.PersistenceRevision,
                record.CreatedAtUtcTicks, record.Fields));
        }
        catch (Exception exception)
        {
            // Une perte d'ACK d'Attempted interdit aussi tout rejeu en session.
            RecoverJusticeInventoryRestoreProgressForCurrentWeapon(item);
            LogException("Justice.Inventaire.Restitution", exception);
            return null;
        }
    }

    private void RecoverJusticeInventoryRestoreProgressForCurrentWeapon(JusticeWeaponSnapshotItem item)
    {
        if (_justiceWriteAheadLog == null || !_justiceWriteAheadLog.HasOpenTransactionKind(JusticeInventoryRestoreWalKind)) return;
        foreach (JusticeWalRecord record in _justiceWriteAheadLog.GetOpenTransactions())
            if (record.OperationKind == JusticeInventoryRestoreWalKind && record.State != JusticeWalState.Prepared &&
                record.ProfileSlot == _justiceActivePlayerProfileSlot &&
                ReadWalString(record, "restoreId", "") == _justiceWeaponSnapshot.RestoreId &&
                ReadWalInt(record, "weaponHash", 0) == item.WeaponHash)
                item.DeferredRestoreAttempted = true;
    }

    private void RecoverJusticeInventoryRestoreResult(JusticeWalRecord record)
    {
        Guid id;
        bool ammoResult = HasExactJusticeWalFields(record, "restoreId", "ammoTypeHash", "schemaMajor");
        if ((!ammoResult && !HasExactJusticeWalFields(record, "restoreId", "weaponHash", "schemaMajor")) ||
            !IsJusticeCanonicalProfileSlot(record.ProfileSlot) || ReadWalInt(record, "schemaMajor", 0) != 2 ||
            !Guid.TryParseExact(ReadWalString(record, "restoreId", ""), "N", out id) ||
            ReadWalInt(record, ammoResult ? "ammoTypeHash" : "weaponHash", 0) == 0)
            throw new InvalidDataException("Résultat de restitution WAL invalide.");
        if (record.State == JusticeWalState.Prepared)
        {
            RejectJusticePreparedWalBeforeEffect(record);
            return;
        }
        JusticePlayerProfileState profile = _justicePlayerProfiles[record.ProfileSlot];
        JusticeCustodyPersistenceSnapshot custody = profile.CustodySnapshot;
        JusticeInventoryPersistenceSnapshot inventory = custody == null ? null : custody.InventorySnapshot;
        if (inventory != null && inventory.RestoreId == ReadWalString(record, "restoreId", ""))
        {
            List<JusticeWeaponPersistenceSnapshot> weapons = new List<JusticeWeaponPersistenceSnapshot>();
            foreach (JusticeWeaponPersistenceSnapshot weapon in inventory.Weapons)
                weapons.Add(new JusticeWeaponPersistenceSnapshot(weapon.WeaponHash, weapon.Ammo, weapon.AmmoInClip,
                    weapon.Tint, weapon.ComponentHashes,
                    weapon.DeferredRestoreAttempted || weapon.WeaponHash == ReadWalInt(record, "weaponHash", 0),
                    weapon.DeferredRestoreCompleted || (record.State == JusticeWalState.Ambiguous &&
                        weapon.WeaponHash == ReadWalInt(record, "weaponHash", 0)), weapon.AmmoTypeHash));
            List<JusticeAmmoPersistenceSnapshot> pools = new List<JusticeAmmoPersistenceSnapshot>();
            foreach (JusticeAmmoPersistenceSnapshot pool in inventory.AmmoPools)
            {
                bool matches = ammoResult && pool.TypeHash == ReadWalInt(record, "ammoTypeHash", 0);
                pools.Add(new JusticeAmmoPersistenceSnapshot(pool.TypeHash, pool.Ammo,
                    pool.RestoreAttempted || matches, pool.RestoreCompleted || (matches && record.State == JusticeWalState.Ambiguous)));
            }
            inventory = new JusticeInventoryPersistenceSnapshot(inventory.IsValidated, inventory.SelectedWeaponHash,
                weapons, inventory.RestoreId, pools);
            profile.CustodySnapshot = CloneJusticeCustodyWithInventory(custody, inventory);
            profile.CustodyXml = string.Empty;
            if (record.ProfileSlot == _justiceActivePlayerProfileSlot)
                _justiceWeaponSnapshot = RestoreJusticeInventorySnapshot(inventory);
        }
        JusticeMarkStateDirty();
    }

    private static bool ContainsJusticeInventoryRestoreResult(JusticePersistenceSnapshot snapshot, JusticeWalRecord record)
    {
        JusticePersistenceProfileSnapshot profile = FindJusticePersistenceProfile(snapshot, record.ProfileSlot);
        if (profile == null || profile.CustodyState == null) return false;
        JusticeInventoryPersistenceSnapshot inventory = profile.CustodyState.InventorySnapshot;
        if (inventory == null || inventory.RestoreId != ReadWalString(record, "restoreId", "")) return true;
        foreach (JusticeAmmoPersistenceSnapshot pool in inventory.AmmoPools)
            if (pool.TypeHash == ReadWalInt(record, "ammoTypeHash", 0)) return pool.RestoreCompleted;
        foreach (JusticeWeaponPersistenceSnapshot weapon in inventory.Weapons)
            if (weapon.WeaponHash == ReadWalInt(record, "weaponHash", 0))
                return weapon.DeferredRestoreCompleted;
        return false;
    }

    private void TrackJusticeInventoryRestoreResults(JusticePersistenceSnapshot snapshot)
    {
        if (_justiceWriteAheadLog == null || !_justiceWriteAheadLog.HasOpenTransactionKind(JusticeInventoryRestoreWalKind)) return;
        if (_justiceInventoryResultCandidates == null) _justiceInventoryResultCandidates = new Dictionary<string, long>();
        if (_justiceInventoryResultProofs == null) _justiceInventoryResultProofs = new Dictionary<string, long>();
        foreach (JusticeWalRecord record in _justiceWriteAheadLog.GetOpenTransactions())
            if (record.OperationKind == JusticeInventoryRestoreWalKind && record.State != JusticeWalState.Prepared &&
                !_justiceInventoryResultProofs.ContainsKey(record.TransactionId) &&
                !_justiceInventoryResultCandidates.ContainsKey(record.TransactionId) &&
                ContainsJusticeInventoryRestoreResult(snapshot, record))
                _justiceInventoryResultCandidates[record.TransactionId] = snapshot.Revision;
    }

    private void AdvanceJusticeInventoryRestoreResults(long disk)
    {
        if (_justiceWriteAheadLog == null || _justiceInventoryResultCandidates == null ||
            !_justiceWriteAheadLog.HasOpenTransactionKind(JusticeInventoryRestoreWalKind)) return;
        foreach (JusticeWalRecord record in _justiceWriteAheadLog.GetOpenTransactions())
        {
            if (record.OperationKind != JusticeInventoryRestoreWalKind) continue;
            long proof, candidate;
            if (_justiceInventoryResultProofs.TryGetValue(record.TransactionId, out proof))
            {
                if (disk > proof)
                {
                    _justiceWriteAheadLog.Append(new JusticeWalRecord(record.TransactionId, record.OperationKind,
                        record.ProfileSlot, JusticeWalState.Confirmed, record.PersistenceRevision,
                        record.CreatedAtUtcTicks, record.Fields));
                    _justiceInventoryResultProofs.Remove(record.TransactionId);
                    _justiceInventoryResultCandidates.Remove(record.TransactionId);
                }
                continue;
            }
            if (!_justiceInventoryResultCandidates.TryGetValue(record.TransactionId, out candidate) || disk < candidate) continue;
            if (disk == candidate)
            {
                if (record.State == JusticeWalState.Attempted)
                    _justiceWriteAheadLog.Append(new JusticeWalRecord(record.TransactionId, record.OperationKind,
                        record.ProfileSlot, JusticeWalState.Ambiguous, disk, record.CreatedAtUtcTicks, record.Fields));
                _justiceInventoryResultProofs[record.TransactionId] = disk;
                _justiceInventoryResultCandidates.Remove(record.TransactionId);
            }
            else _justiceInventoryResultCandidates.Remove(record.TransactionId);
            JusticeMarkStateDirty();
        }
    }
}
