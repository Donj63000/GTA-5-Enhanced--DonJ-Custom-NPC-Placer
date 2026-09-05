using System;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private Ped[] _justiceVictimCandidates;
    private Vehicle[] _justiceVehicleCandidates;
    private int _justiceVictimScanCursor;
    private int _justiceVehicleScanCursor;

    private Ped[] GetJusticeVictimCandidatesForActor(Ped player)
    {
        if (_justiceVictimCandidates == null)
            _justiceVictimCandidates = new Ped[JusticeMaximumWitnessesPerEvent];
        Array.Clear(_justiceVictimCandidates, 0, _justiceVictimCandidates.Length);
        Ped[] nearby = GetJusticeSnapshotPeds();
        if (!Entity.Exists(player) || nearby.Length == 0) return _justiceVictimCandidates;
        int start = _justiceVictimScanCursor % nearby.Length;
        int count = 0;
        int deadCount = 0;
        // Je traite les victimes connues en premier, puis fais tourner les autres.
        // Les policiers témoins ne peuvent plus évincer toutes les victimes civiles.
        for (int pass = 0; pass < 4 && count < _justiceVictimCandidates.Length; pass++)
        {
            for (int offset = 0; offset < nearby.Length && count < _justiceVictimCandidates.Length; offset++)
            {
                Ped ped = nearby[(start + offset) % nearby.Length];
                if (!IsJusticeSnapshotEntityWithin(ped, player, JusticeWitnessRadius) ||
                    !IsJusticePotentialVictimCandidate(ped, player)) continue;
                bool dead;
                try { dead = ped.IsDead; } catch { continue; }
                if (dead != (pass < 2) || (dead && deadCount >= JusticeMaximumVictimCandidatesPerEvent)) continue;
                bool recent = false;
                for (int i = 0; i < _justiceRecentVictims.Count; i++)
                {
                    JusticeRecentVictim victim = _justiceRecentVictims[i];
                    if (victim != null && ReferenceEquals(victim.Ped, ped) &&
                        IsJusticeCausalDamageFresh(victim.LastPlayerAttackAtMs)) { recent = true; break; }
                }
                if (recent != (pass % 2 == 0)) continue;
                _justiceVictimCandidates[count++] = ped;
                if (dead) deadCount++;
            }
        }
        _justiceVictimScanCursor = (start + 1) % nearby.Length;
        return _justiceVictimCandidates;
    }

    private Vehicle[] GetJusticeVehicleCandidates(Ped player)
    {
        if (_justiceVehicleCandidates == null)
            _justiceVehicleCandidates = new Vehicle[JusticeMaximumVehiclesPerEvent];
        Array.Clear(_justiceVehicleCandidates, 0, _justiceVehicleCandidates.Length);
        Vehicle[] nearby = GetJusticeSnapshotVehicles();
        if (!Entity.Exists(player) || nearby.Length == 0) return _justiceVehicleCandidates;
        Vehicle current = GetJusticeCurrentVehicleSafe(player);
        Vehicle last = GetJusticeLastVehicleSafe(player);
        int start = _justiceVehicleScanCursor % nearby.Length;
        int count = 0;
        for (int offset = 0; offset < nearby.Length && count < _justiceVehicleCandidates.Length; offset++)
        {
            Vehicle vehicle = nearby[(start + offset) % nearby.Length];
            if (!IsJusticeSnapshotEntityWithin(vehicle, player, JusticeWitnessRadius) ||
                (Entity.Exists(current) && current.Handle == vehicle.Handle) ||
                (Entity.Exists(last) && last.Handle == vehicle.Handle)) continue;
            _justiceVehicleCandidates[count++] = vehicle;
        }
        _justiceVehicleScanCursor = (start + 1) % nearby.Length;
        return _justiceVehicleCandidates;
    }
}
