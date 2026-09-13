using System;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;

public sealed partial class DonJEnemySpawner
{
    private const int JusticeCustodyStreamingPollMs = 250;
    private const int JusticeCustodyStreamingTimeoutMs = 30000;
    private const float JusticeCustodyStreamingProbeHalfHeight = 2.0f;
    private const float JusticeCustodyStreamingFloorMinimumOffset = -1.5f;
    private const float JusticeCustodyStreamingFloorMaximumOffset = 0.25f;
    private const float JusticeCustodyStreamingMinimumNormalZ = 0.5f;
    private const ulong JusticeNativeUnpinInterior = 0x261CCE7EED010641UL;

    private enum JusticeCustodyStreamingResult
    {
        Pending,
        Ready,
        TimedOut
    }

    private enum JusticeCustodyStreamingFailure
    {
        None,
        InvalidDestination,
        MultiplayerMap,
        Focus,
        InteriorUnknown,
        InteriorInvalid,
        InteriorNotReady,
        FloorUnavailable,
        FloorInvalid,
        CollisionUnavailable,
        PlayerOutsideDestination,
        NativeFailure
    }

    private bool _justiceCustodyStreamingActive;
    private int _justiceCustodyStreamingPlayerHandle;
    private int _justiceCustodyStreamingPlayerModelHash;
    private int _justiceCustodyStreamingOwnerSlot = -1;
    private JusticeCustodySite _justiceCustodyStreamingSite;
    private Vector3 _justiceCustodyStreamingTarget;
    private int _justiceCustodyStreamingStartedAt;
    private int _justiceCustodyStreamingNextProbeAt;
    private int _justiceCustodyStreamingLastProbeAt;
    private bool _justiceCustodyStreamingHasProbe;
    private bool _justiceCustodyStreamingGeometryReady;
    private bool _justiceCustodyStreamingHdAreaRequested;
    private bool _justiceCustodyStreamingFocusRequested;
    private bool _justiceCustodyStreamingSceneRequested;
    private int _justiceCustodyStreamingPinnedInteriorId;
    private bool _justiceCustodyStreamingInteriorRefreshPending;
    private float _justiceCustodyStreamingObservedFloorZ = float.NaN;
    private JusticeCustodyStreamingFailure _justiceCustodyStreamingFailure;

    private JusticeCustodyStreamingResult PrepareJusticeCustodyDestinationStreaming(
        Ped player,
        JusticeCustodySite site,
        Vector3 targetPosition,
        int now)
    {
        if (!IsJusticeCustodyStreamingDestinationValid(player, site, targetPosition))
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.InvalidDestination;
            return JusticeCustodyStreamingResult.Pending;
        }

        if (!IsJusticeCustodyStreamingLeaseFor(player, site, targetPosition))
        {
            ResetJusticeCustodyDestinationStreaming();
            _justiceCustodyStreamingActive = true;
            _justiceCustodyStreamingPlayerHandle = player.Handle;
            _justiceCustodyStreamingPlayerModelHash = GetJusticePedModelHashSafe(player);
            _justiceCustodyStreamingOwnerSlot = GetJusticeCustodyStreamingOwnerSlot();
            _justiceCustodyStreamingSite = site;
            _justiceCustodyStreamingTarget = targetPosition;
            _justiceCustodyStreamingStartedAt = now;
            // Je quitte l'ancien portail une seule fois. Les prochains essais
            // gardent le focus et le chargement de la destination en cours.
            _activeInteriorSession = null;
            ClearInteriorRenderingFocusSafe(player);
        }

        bool deadlineReached = HasJusticeCustodyDestinationStreamingTimedOut(
            player, site, targetPosition, now);
        // Je ne décide pas le secours depuis une sonde négative mise en cache
        // juste avant l'échéance : la dernière chance relit le moteur.
        if (ProbeJusticeCustodyDestinationGeometry(player, now, deadlineReached))
        {
            return JusticeCustodyStreamingResult.Ready;
        }
        return deadlineReached
            ? JusticeCustodyStreamingResult.TimedOut
            : JusticeCustodyStreamingResult.Pending;
    }

    private bool IsJusticeCustodyDestinationStreamingReadyForPlayer(
        Ped player,
        JusticeCustodySite site,
        Vector3 targetPosition,
        int now)
    {
        if (!IsJusticeCustodyStreamingLeaseFor(player, site, targetPosition) ||
            !IsJusticeCustodyStreamingDestinationValid(player, site, targetPosition))
        {
            return false;
        }
        // Je relis le plancher et l'intérieur au passage de la frontière visuelle,
        // sans réinitialiser les trente secondes de cette même destination.
        if (!ProbeJusticeCustodyDestinationGeometry(player, now, true))
        {
            return false;
        }
        if (!IsJusticeTeleportVerified(player, targetPosition, 2.0f))
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.PlayerOutsideDestination;
            return false;
        }
        try
        {
            if (!Function.Call<bool>((Hash)JusticeNativeHasCollisionLoadedAroundEntity, player.Handle))
            {
                _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.CollisionUnavailable;
                return false;
            }
        }
        catch
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.NativeFailure;
            return false;
        }
        _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.None;
        return true;
    }

    private bool HasJusticeCustodyDestinationStreamingTimedOut(
        Ped player,
        JusticeCustodySite site,
        Vector3 targetPosition,
        int now)
    {
        return IsJusticeCustodyStreamingLeaseFor(player, site, targetPosition) &&
            unchecked((uint)(now - _justiceCustodyStreamingStartedAt)) >=
                (uint)JusticeCustodyStreamingTimeoutMs;
    }

    private bool ProbeJusticeCustodyDestinationGeometry(Ped player, int now, bool force)
    {
        if (!force && _justiceCustodyStreamingHasProbe &&
            (now == _justiceCustodyStreamingLastProbeAt ||
             unchecked((int)(now - _justiceCustodyStreamingNextProbeAt)) < 0))
        {
            return _justiceCustodyStreamingGeometryReady;
        }
        _justiceCustodyStreamingHasProbe = true;
        _justiceCustodyStreamingLastProbeAt = now;
        _justiceCustodyStreamingNextProbeAt = unchecked(now + JusticeCustodyStreamingPollMs);
        _justiceCustodyStreamingGeometryReady = false;
        _justiceCustodyStreamingObservedFloorZ = float.NaN;

        if (_justiceCustodyStreamingSite == JusticeCustodySite.MissionRow &&
            !TryEnsureMultiplayerInteriorMapLoadedSafe(now))
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.MultiplayerMap;
            return false;
        }
        if (!EnsureJusticeCustodyDestinationFocus())
        {
            return false;
        }

        try
        {
            Vector3 target = _justiceCustodyStreamingTarget;
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, target.X, target.Y, target.Z);
            if (_justiceCustodyStreamingSite == JusticeCustodySite.MissionRow)
            {
                // Je n'utilise pas les wrappers tolérants des portails : une
                // native refusée ne prouve jamais que la cellule existe.
                int interiorId = Function.Call<int>((Hash)AdvancedNativeGetInteriorAtCoords,
                    target.X, target.Y, target.Z);
                if (interiorId == 0)
                {
                    _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.InteriorUnknown;
                    return false;
                }
                if (!Function.Call<bool>((Hash)AdvancedNativeIsValidInterior, interiorId))
                {
                    _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.InteriorInvalid;
                    return false;
                }
                if (_justiceCustodyStreamingPinnedInteriorId != interiorId)
                {
                    ReleaseJusticeCustodyStreamingInterior();
                    Function.Call((Hash)AdvancedNativePinInteriorInMemory, interiorId);
                    _justiceCustodyStreamingPinnedInteriorId = interiorId;
                    _justiceCustodyStreamingInteriorRefreshPending = true;
                }
                if (_justiceCustodyStreamingInteriorRefreshPending)
                {
                    Function.Call((Hash)AdvancedNativeRefreshInterior, interiorId);
                    _justiceCustodyStreamingInteriorRefreshPending = false;
                }
                if (!Function.Call<bool>((Hash)AdvancedNativeIsInteriorReady, interiorId))
                {
                    _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.InteriorNotReady;
                    return false;
                }
            }

            RaycastResult floor = World.Raycast(
                target + new Vector3(0.0f, 0.0f, JusticeCustodyStreamingProbeHalfHeight),
                target - new Vector3(0.0f, 0.0f, JusticeCustodyStreamingProbeHalfHeight),
                IntersectOptions.Map,
                player);
            if (!floor.DitHitAnything)
            {
                _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.FloorUnavailable;
                return false;
            }
            _justiceCustodyStreamingObservedFloorZ = floor.HitCoords.Z;
            if (!IsJusticeCustodyStreamingFloorValid(target, floor.HitCoords, floor.SurfaceNormal))
            {
                _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.FloorInvalid;
                return false;
            }
            _justiceCustodyStreamingGeometryReady = true;
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.None;
            return true;
        }
        catch
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.NativeFailure;
            return false;
        }
    }

    private bool EnsureJusticeCustodyDestinationFocus()
    {
        Vector3 target = _justiceCustodyStreamingTarget;
        try
        {
            if (!_justiceCustodyStreamingHdAreaRequested)
            {
                Function.Call((Hash)AdvancedNativeSetHdArea, target.X, target.Y, target.Z,
                    AdvancedInteriorHdAreaRadius);
                _justiceCustodyStreamingHdAreaRequested = true;
            }
            if (!_justiceCustodyStreamingFocusRequested)
            {
                Function.Call((Hash)AdvancedNativeSetFocusPosAndVel, target.X, target.Y,
                    target.Z, 0.0f, 0.0f, 0.0f);
                _justiceCustodyStreamingFocusRequested = true;
            }
            if (!_justiceCustodyStreamingSceneRequested)
            {
                Function.Call((Hash)AdvancedNativeNewLoadSceneStart, target.X, target.Y,
                    target.Z, 0.0f, 0.0f, 0.0f, AdvancedInteriorNewSceneRadius, 0);
                _justiceCustodyStreamingSceneRequested = true;
            }
            return true;
        }
        catch
        {
            _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.Focus;
            return false;
        }
    }

    private static bool IsJusticeCustodyStreamingFloorValid(Vector3 target, Vector3 hit, Vector3 normal)
    {
        return IsJusticeCustodyStreamingVectorFinite(target) &&
            IsJusticeCustodyStreamingVectorFinite(hit) &&
            IsJusticeCustodyStreamingVectorFinite(normal) &&
            Math.Abs(hit.X - target.X) <= 0.1f && Math.Abs(hit.Y - target.Y) <= 0.1f &&
            hit.Z >= target.Z + JusticeCustodyStreamingFloorMinimumOffset &&
            hit.Z <= target.Z + JusticeCustodyStreamingFloorMaximumOffset &&
            normal.Z >= JusticeCustodyStreamingMinimumNormalZ;
    }

    private static bool IsJusticeCustodyLocalFloorReady(Ped player, Vector3 targetPosition)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            !IsJusticeCustodyStreamingVectorFinite(targetPosition))
        {
            return false;
        }
        try
        {
            RaycastResult floor = World.Raycast(
                targetPosition + new Vector3(0.0f, 0.0f, JusticeCustodyStreamingProbeHalfHeight),
                targetPosition - new Vector3(0.0f, 0.0f, JusticeCustodyStreamingProbeHalfHeight),
                IntersectOptions.Map,
                player);
            return floor.DitHitAnything &&
                IsJusticeCustodyStreamingFloorValid(targetPosition, floor.HitCoords, floor.SurfaceNormal);
        }
        catch
        {
            return false;
        }
    }

    private bool IsJusticeCustodyInteriorStreamingReadyAt(Ped player, JusticeCustodySite site)
    {
        if (!Entity.Exists(player) || player.IsDead ||
            (site != JusticeCustodySite.MissionRow && site != JusticeCustodySite.Bolingbroke))
        {
            return false;
        }
        try
        {
            if (site == JusticeCustodySite.MissionRow)
            {
                Vector3 position = player.Position;
                int interiorId = Function.Call<int>((Hash)AdvancedNativeGetInteriorAtCoords,
                    position.X, position.Y, position.Z);
                if (interiorId == 0 ||
                    !Function.Call<bool>((Hash)AdvancedNativeIsValidInterior, interiorId) ||
                    !Function.Call<bool>((Hash)AdvancedNativeIsInteriorReady, interiorId))
                {
                    return false;
                }
            }
            return Function.Call<bool>((Hash)JusticeNativeHasCollisionLoadedAroundEntity, player.Handle);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeCustodyStreamingVectorFinite(Vector3 value)
    {
        return !float.IsNaN(value.X) && !float.IsInfinity(value.X) &&
            !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) &&
            !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
    }

    private static bool IsJusticeCustodyStreamingDestinationValid(
        Ped player, JusticeCustodySite site, Vector3 targetPosition)
    {
        return Entity.Exists(player) && !player.IsDead &&
            (site == JusticeCustodySite.MissionRow || site == JusticeCustodySite.Bolingbroke) &&
            IsJusticeCustodyStreamingVectorFinite(targetPosition);
    }

    private int GetJusticeCustodyStreamingOwnerSlot()
    {
        return IsJusticeCanonicalProfileSlot(_justicePoliceDeathPreJudgmentHoldingOwnerSlot)
            ? _justicePoliceDeathPreJudgmentHoldingOwnerSlot
            : _justiceCustodyPlayerSlot;
    }

    private bool IsJusticeCustodyStreamingLeaseFor(Ped player, JusticeCustodySite site, Vector3 target)
    {
        return _justiceCustodyStreamingActive && Entity.Exists(player) &&
            player.Handle == _justiceCustodyStreamingPlayerHandle &&
            GetJusticePedModelHashSafe(player) == _justiceCustodyStreamingPlayerModelHash &&
            GetJusticeCustodyStreamingOwnerSlot() == _justiceCustodyStreamingOwnerSlot &&
            site == _justiceCustodyStreamingSite &&
            target.DistanceTo(_justiceCustodyStreamingTarget) <= 0.05f;
    }

    private string GetJusticeCustodyDestinationStreamingDiagnostic()
    {
        return "site=" + _justiceCustodyStreamingSite +
            ", cause=" + _justiceCustodyStreamingFailure +
            ", intérieur=" + _justiceCustodyStreamingPinnedInteriorId.ToString(CultureInfo.InvariantCulture) +
            ", solZ=" + _justiceCustodyStreamingObservedFloorZ.ToString(CultureInfo.InvariantCulture);
    }

    private void ReleaseJusticeCustodyStreamingInterior()
    {
        if (_justiceCustodyStreamingPinnedInteriorId == 0)
        {
            return;
        }
        try
        {
            Function.Call((Hash)JusticeNativeUnpinInterior, _justiceCustodyStreamingPinnedInteriorId);
        }
        catch
        {
            // Je ne retiens jamais les contrôles ou l'écran pour un nettoyage
            // refusé par le moteur ; aucun intérieur n'est désactivé globalement.
        }
        _justiceCustodyStreamingPinnedInteriorId = 0;
        _justiceCustodyStreamingInteriorRefreshPending = false;
    }

    private void CompleteJusticeCustodyDestinationStreaming()
    {
        if (!_justiceCustodyStreamingActive ||
            (!_justiceCustodyStreamingHdAreaRequested &&
             !_justiceCustodyStreamingFocusRequested &&
             !_justiceCustodyStreamingSceneRequested))
        {
            return;
        }
        // Je rends le streaming normal à la caméra après admission, tout en
        // gardant la cellule en mémoire pendant le reste de la détention.
        if (_activeInteriorSession == null)
        {
            ClearInteriorRenderingFocusSafe(null);
        }
        _justiceCustodyStreamingHdAreaRequested = false;
        _justiceCustodyStreamingFocusRequested = false;
        _justiceCustodyStreamingSceneRequested = false;
        _justiceCustodyStreamingHasProbe = false;
        _justiceCustodyStreamingGeometryReady = false;
    }

    private void ResetJusticeCustodyDestinationStreaming()
    {
        ReleaseJusticeCustodyStreamingInterior();
        if (_justiceCustodyStreamingActive && _activeInteriorSession == null)
        {
            // Je ne supprime pas le focus qu'un nouveau portail aurait repris.
            ClearInteriorRenderingFocusSafe(null);
        }
        _justiceCustodyStreamingActive = false;
        _justiceCustodyStreamingPlayerHandle = 0;
        _justiceCustodyStreamingPlayerModelHash = 0;
        _justiceCustodyStreamingOwnerSlot = -1;
        _justiceCustodyStreamingSite = JusticeCustodySite.None;
        _justiceCustodyStreamingTarget = Vector3.Zero;
        _justiceCustodyStreamingStartedAt = 0;
        _justiceCustodyStreamingNextProbeAt = 0;
        _justiceCustodyStreamingLastProbeAt = 0;
        _justiceCustodyStreamingHasProbe = false;
        _justiceCustodyStreamingGeometryReady = false;
        _justiceCustodyStreamingHdAreaRequested = false;
        _justiceCustodyStreamingFocusRequested = false;
        _justiceCustodyStreamingSceneRequested = false;
        _justiceCustodyStreamingObservedFloorZ = float.NaN;
        _justiceCustodyStreamingFailure = JusticeCustodyStreamingFailure.None;
    }
}
