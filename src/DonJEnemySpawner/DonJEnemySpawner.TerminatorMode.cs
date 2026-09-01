using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GTA;
using GTA.Math;
using GTA.Native;
using Keys = System.Windows.Forms.Keys;

public sealed partial class DonJEnemySpawner
{
    private const int TerminatorMinHealth = 2000;
    private const int TerminatorArmor = 200;
    private const int TerminatorArmorRefreshThreshold = 155;
    private const int TerminatorFirstPersonViewMode = 4;

    private const int TerminatorHudWidth = 1280;
    private const int TerminatorHudHeight = 720;
    private const int TerminatorFocusRefreshIntervalMs = 90;
    private const int TerminatorFocusMemoryMs = 260;
    private const int TerminatorPushCooldownMs = 650;
    private const int TerminatorPushCacheCleanupMs = 2400;
    private const int TerminatorVisionFilterRefreshMs = 650;
    private const int TerminatorDamageFlagCleanupIntervalMs = 420;
    private const int TerminatorWeaponFireImpactBlockMs = 360;
    private const int TerminatorHealthRegenDelayAfterDamageMs = 4800;
    private const int TerminatorHealthRegenIntervalMs = 1150;
    private const int TerminatorHealthRegenAmount = 18;
    private const int TerminatorArmorRegenDelayAfterDamageMs = 1850;
    private const int TerminatorArmorRegenIntervalMs = 760;
    private const int TerminatorArmorRegenAmount = 14;
    private const int TerminatorVisionModeNone = -1;
    private const int TerminatorVisionModeRed = 0;
    private const int TerminatorVisionModeNight = 1;
    private const int TerminatorVisionModeThermal = 2;
    private const int TerminatorVisionModeCount = 3;

    private const float TerminatorFocusRadius = 90.0f;
    private const float TerminatorPedImpactRadius = 2.15f;
    private const float TerminatorVehicleImpactRadius = 2.95f;
    private const float TerminatorPedThrowSpeed = 12.8f;
    private const float TerminatorVehiclePushSpeed = 4.85f;
    private const float TerminatorImpactConeDot = -0.45f;

    private const int TerminatorControlAim = 25;
    private const int TerminatorControlMeleeLight = 140;
    private const int TerminatorControlMeleeHeavy = 141;
    private const int TerminatorControlMeleeAlternate = 142;

    private const ulong NativeGetFollowPedCamViewMode = 0x8D4D46230B2C353AUL;
    private const ulong NativeSetFollowPedCamViewMode = 0x5A4F9EDF1673F704UL;
    private const ulong NativeGetFollowVehicleCamViewMode = 0xA4FF579AC0E3AAAEUL;
    private const ulong NativeSetFollowVehicleCamViewMode = 0xAC253D7842768F48UL;
    private const ulong NativeGetScreenCoordFromWorldCoord = 0x34E82F05DF2974F5UL;

    private const ulong NativeGetEntityHealth = 0xEEF059FAD016D209UL;
    private const ulong NativeGetEntityMaxHealth = 0x15D757606D170C3CUL;
    private const ulong NativeSetEntityMaxHealth = 0x166E7CF68597D8B5UL;
    private const ulong NativeSetPedArmour = 0xCEA04D83135264CCUL;
    private const ulong NativeGetPedArmour = 0x9483AF821605B1D8UL;
    private const ulong NativeSetPedSuffersCriticalHits = 0xEBD76F2359F190ACUL;
    private const ulong NativeSetPedCanRagdoll = 0xB128377056A54E2AUL;
    private const ulong NativeSetPedCanRagdollFromPlayerImpact = 0xDF993EE5E90ABA25UL;

    private const ulong NativeIsControlPressed = 0xF3A21BCD95725A4AUL;
    private const ulong NativeIsPedPerformingMeleeAction = 0xDCCA191DF9980FD7UL;
    private const ulong NativeGetMeleeTargetForPed = 0x18A3E9EE1297FD39UL;
    private const ulong NativeSetPedToRagdoll = 0xAE99FB955581844AUL;
    private const ulong NativeApplyForceToEntity = 0xC5F68BE9613E2D18UL;
    private const ulong NativeSetEntityVelocity = 0x1C99BB7B6E96D16FUL;
    private const ulong NativeClearEntityLastDamageEntity = 0xA72CD9CA74A5ECBAUL;
    private const ulong NativeGetEntityPlayerIsFreeAimingAt = 0x2975C866E6713290UL;

    private const ulong NativeSetTimecycleModifier = 0x2C933ABF17A1DF41UL;
    private const ulong NativeSetTimecycleModifierStrength = 0x82E7FFCD5B2326B3UL;
    private const ulong NativeClearTimecycleModifier = 0x0F07E7745A236711UL;
    private const ulong NativeSetNightvision = 0x18F621F7A5B1F85DUL;
    private const ulong NativeSetSeethrough = 0x7E08924259E08CE0UL;

    private bool _terminatorModeEnabled;
    private bool _terminatorModeApplied;
    private bool _terminatorCameraStored;
    private bool _terminatorVisionFilterApplied;
    private bool _terminatorLowLightVisionApplied;
    private bool _terminatorThermalVisionApplied;
    private bool _terminatorWasMeleeActive;

    private int _terminatorStoredPedCameraViewMode;
    private int _terminatorStoredVehicleCameraViewMode;
    private int _terminatorStoredMaxHealth = 200;
    private int _terminatorStoredHealth = 200;
    private int _terminatorStoredArmor;
    private bool _terminatorStoredCanRagdoll = true;

    private int _terminatorNextFocusScanAt;
    private int _terminatorNextVisionFilterAt;
    private int _terminatorVisionMode = TerminatorVisionModeRed;
    private int _terminatorAppliedVisionMode = TerminatorVisionModeNone;
    private int _terminatorNextDamageFlagCleanupAt;
    private int _terminatorImpactFlashUntil = -1000000;
    private int _terminatorMeleeStartedAt;
    private int _terminatorNextMeleeImpactAllowedAt;
    private int _terminatorLastWeaponFireAt = -1000000;
    private int _terminatorLastObservedHealth;
    private int _terminatorLastObservedArmor;
    private int _terminatorLastDamageAt = -1000000;
    private int _terminatorNextHealthRegenAt;
    private int _terminatorNextArmorRegenAt;

    private TerminatorFocusTarget _terminatorFocusedTarget;

    private readonly Dictionary<int, int> _terminatorPushCooldowns = new Dictionary<int, int>();

    private sealed class TerminatorFocusTarget
    {
        public Entity Entity;
        public bool IsPed;
        public bool IsVehicle;
        public string Label;
        public string Type;
        public string Faction;
        public string Weapon;
        public string Model;
        public int Health;
        public int Armor;
        public float Distance;
        public int LastSeenAt;
        public bool FromFreeAim;
    }

    private void ToggleTerminatorMode()
    {
        if (_terminatorModeEnabled)
        {
            DisableTerminatorMode(true);
        }
        else
        {
            EnableTerminatorMode();
        }
    }

    private void EnableTerminatorMode()
    {
        if (_terminatorModeEnabled)
        {
            return;
        }

        if (IsJusticeTemporaryPlayerProtectionForbidden())
        {
            // Je ne laisse pas la régénération Terminator contourner la mortalité
            // imposée dès l'arrestation et pendant toute la détention.
            ShowStatus("Mode Terminator indisponible pendant la détention.", 3000);
            return;
        }

        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            ShowStatus("Mode Terminator indisponible: joueur introuvable ou mort.", 2600);
            return;
        }

        _terminatorModeEnabled = true;
        _terminatorModeApplied = false;
        _terminatorWasMeleeActive = false;
        _terminatorNextFocusScanAt = 0;
        _terminatorNextVisionFilterAt = 0;
        _terminatorVisionMode = TerminatorVisionModeRed;
        _terminatorAppliedVisionMode = TerminatorVisionModeNone;
        _terminatorNextDamageFlagCleanupAt = 0;
        _terminatorNextMeleeImpactAllowedAt = 0;
        _terminatorLastWeaponFireAt = -1000000;
        _terminatorImpactFlashUntil = Game.GameTime + 350;
        _terminatorLastObservedHealth = 0;
        _terminatorLastObservedArmor = 0;
        _terminatorLastDamageAt = Game.GameTime;
        _terminatorNextHealthRegenAt = Game.GameTime + TerminatorHealthRegenDelayAfterDamageMs;
        _terminatorNextArmorRegenAt = Game.GameTime + TerminatorArmorRegenDelayAfterDamageMs;
        _terminatorFocusedTarget = null;
        _terminatorPushCooldowns.Clear();

        ApplyTerminatorModeToPlayer(player, true);
        ForceTerminatorFirstPersonCamera();
        ApplyTerminatorVisionFilter(true);
        ClearTerminatorNearbyDamageFlags(player);

        ShowStatus("Mode Terminator ACTIVE: vision rouge T-800. B change la vision.", 3800);
    }

    private void DisableTerminatorMode(bool showStatus)
    {
        bool wasEnabled = HasTerminatorRuntimeState();

        _terminatorModeEnabled = false;

        Ped player = Game.Player.Character;
        RestoreTerminatorPlayerState(player);
        RestoreTerminatorCameraViewModes();
        ClearTerminatorVisionFilter();

        _terminatorModeApplied = false;
        _terminatorCameraStored = false;
        _terminatorWasMeleeActive = false;
        _terminatorVisionMode = TerminatorVisionModeRed;
        _terminatorAppliedVisionMode = TerminatorVisionModeNone;
        _terminatorLastObservedHealth = 0;
        _terminatorLastObservedArmor = 0;
        _terminatorFocusedTarget = null;
        _terminatorPushCooldowns.Clear();

        if (showStatus && wasEnabled)
        {
            ShowStatus("Mode Terminator DESACTIVE: camera, vision et stats restaurees.", 2600);
        }
    }

    private bool HasTerminatorRuntimeState()
    {
        return _terminatorModeEnabled ||
               _terminatorModeApplied ||
               _terminatorCameraStored ||
               _terminatorVisionFilterApplied ||
               _terminatorLowLightVisionApplied ||
               _terminatorThermalVisionApplied;
    }

    private void UpdateTerminatorMode()
    {
        if (!_terminatorModeEnabled)
        {
            return;
        }

        if (IsJusticeTemporaryPlayerProtectionForbidden())
        {
            // Je restaure les statistiques et la caméra avant que Justice ne
            // photographie ou ne maintienne l'état jouable du détenu.
            DisableTerminatorMode(false);
            return;
        }

        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        ApplyTerminatorModeToPlayer(player, false);
        MaintainTerminatorVisionFilter(player);
        UpdateTerminatorPunchPower(player);

        if (IsTerminatorFirstPersonCameraActive(player))
        {
            UpdateTerminatorFocusedTarget(player);
        }
        else
        {
            _terminatorFocusedTarget = null;
        }
    }

    private void DrawTerminatorModeHud()
    {
        if (!_terminatorModeEnabled)
        {
            return;
        }

        Ped player = Game.Player.Character;

        if (!Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        if (!IsTerminatorFirstPersonCameraActive(player))
        {
            return;
        }

        DrawTerminatorRedVisionOverlay(player);
        DrawTerminatorFocusedTargetPanel(player);
    }

    private void ApplyTerminatorModeToPlayer(Ped player, bool firstApply)
    {
        if (!Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        if (!_terminatorModeApplied)
        {
            _terminatorStoredMaxHealth = Math.Max(100, SafeGetPedMaxHealth(player));
            _terminatorStoredHealth = Math.Max(1, SafeGetPedHealth(player));
            _terminatorStoredArmor = Math.Max(0, SafeGetPedArmour(player));
            _terminatorStoredCanRagdoll = player.CanRagdoll;

            StoreTerminatorCameraViewModes();
            _terminatorModeApplied = true;
        }

        if (SafeGetPedMaxHealth(player) < TerminatorMinHealth)
        {
            SafeSetPedMaxHealth(player, TerminatorMinHealth);
        }

        int currentHealth = SafeGetPedHealth(player);

        if (firstApply && currentHealth < TerminatorMinHealth)
        {
            SafeSetPedHealth(player, TerminatorMinHealth);
            currentHealth = TerminatorMinHealth;
        }

        int currentArmor = SafeGetPedArmour(player);

        if (firstApply)
        {
            SafeSetPedArmour(player, TerminatorArmor);
            currentArmor = TerminatorArmor;
            ResetTerminatorResistanceTracking(currentHealth, currentArmor);
        }
        else
        {
            UpdateTerminatorResistanceRegeneration(player, currentHealth, currentArmor);
        }

        try
        {
            player.CanRagdoll = false;
        }
        catch
        {
        }

        TryCallNative(NativeSetPedSuffersCriticalHits, player.Handle, false);
        TryCallNative(NativeSetPedCanRagdoll, player.Handle, false);
        TryCallNative(NativeSetPedCanRagdollFromPlayerImpact, player.Handle, false);
        TryCallNative(NativeClearEntityLastDamageEntity, player.Handle);
    }

    private void ResetTerminatorResistanceTracking(int currentHealth, int currentArmor)
    {
        int now = Game.GameTime;

        _terminatorLastObservedHealth = Math.Max(0, currentHealth);
        _terminatorLastObservedArmor = Math.Max(0, currentArmor);
        _terminatorLastDamageAt = now;
        _terminatorNextHealthRegenAt = now + TerminatorHealthRegenDelayAfterDamageMs;
        _terminatorNextArmorRegenAt = now + TerminatorArmorRegenDelayAfterDamageMs;
    }

    private void UpdateTerminatorResistanceRegeneration(Ped player, int currentHealth, int currentArmor)
    {
        if (!Entity.Exists(player) || player.IsDead || currentHealth <= 0)
        {
            return;
        }

        int now = Game.GameTime;

        if (_terminatorLastObservedHealth <= 0)
        {
            _terminatorLastObservedHealth = currentHealth;
        }

        if (_terminatorLastObservedArmor < 0)
        {
            _terminatorLastObservedArmor = currentArmor;
        }

        if (currentHealth < _terminatorLastObservedHealth ||
            currentArmor < _terminatorLastObservedArmor)
        {
            _terminatorLastDamageAt = now;
            _terminatorNextHealthRegenAt = now + TerminatorHealthRegenDelayAfterDamageMs;
            _terminatorNextArmorRegenAt = now + TerminatorArmorRegenDelayAfterDamageMs;
        }

        if (currentHealth < TerminatorMinHealth &&
            now >= _terminatorNextHealthRegenAt &&
            now - _terminatorLastDamageAt >= TerminatorHealthRegenDelayAfterDamageMs)
        {
            currentHealth = Math.Min(TerminatorMinHealth, currentHealth + TerminatorHealthRegenAmount);
            SafeSetPedHealth(player, currentHealth);
            _terminatorNextHealthRegenAt = now + TerminatorHealthRegenIntervalMs;
        }

        if (currentArmor < TerminatorArmor &&
            currentArmor < TerminatorArmorRefreshThreshold &&
            now >= _terminatorNextArmorRegenAt &&
            now - _terminatorLastDamageAt >= TerminatorArmorRegenDelayAfterDamageMs)
        {
            currentArmor = Math.Min(TerminatorArmor, currentArmor + TerminatorArmorRegenAmount);
            SafeSetPedArmour(player, currentArmor);
            _terminatorNextArmorRegenAt = now + TerminatorArmorRegenIntervalMs;
        }

        _terminatorLastObservedHealth = currentHealth;
        _terminatorLastObservedArmor = currentArmor;
    }

    private void RestoreTerminatorPlayerState(Ped player)
    {
        if (!_terminatorModeApplied || !Entity.Exists(player) || player.IsDead)
        {
            return;
        }

        int restoredMaxHealth = Math.Max(100, _terminatorStoredMaxHealth);
        int restoredHealth = Clamp(_terminatorStoredHealth, 1, restoredMaxHealth);
        int restoredArmor = Clamp(_terminatorStoredArmor, 0, TerminatorArmor);

        SafeSetPedMaxHealth(player, restoredMaxHealth);
        SafeSetPedHealth(player, restoredHealth);
        SafeSetPedArmour(player, restoredArmor);

        try
        {
            player.CanRagdoll = _terminatorStoredCanRagdoll;
        }
        catch
        {
        }

        TryCallNative(NativeSetPedSuffersCriticalHits, player.Handle, true);
        TryCallNative(NativeSetPedCanRagdoll, player.Handle, _terminatorStoredCanRagdoll);
        TryCallNative(NativeSetPedCanRagdollFromPlayerImpact, player.Handle, true);
    }

    private void StoreTerminatorCameraViewModes()
    {
        if (_terminatorCameraStored)
        {
            return;
        }

        _terminatorStoredPedCameraViewMode = SafeGetCameraViewMode(NativeGetFollowPedCamViewMode, 0);
        _terminatorStoredVehicleCameraViewMode = SafeGetCameraViewMode(NativeGetFollowVehicleCamViewMode, 0);
        _terminatorCameraStored = true;
    }

    private void ForceTerminatorFirstPersonCamera()
    {
        TryCallNative(NativeSetFollowPedCamViewMode, TerminatorFirstPersonViewMode);
        TryCallNative(NativeSetFollowVehicleCamViewMode, TerminatorFirstPersonViewMode);
    }

    private bool IsTerminatorFirstPersonCameraActive(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        bool inVehicle = false;

        try
        {
            inVehicle = player.IsInVehicle();
        }
        catch
        {
        }

        ulong viewModeNative = inVehicle
            ? NativeGetFollowVehicleCamViewMode
            : NativeGetFollowPedCamViewMode;

        return SafeGetCameraViewMode(viewModeNative, -1) == TerminatorFirstPersonViewMode;
    }

    private void RestoreTerminatorCameraViewModes()
    {
        if (!_terminatorCameraStored)
        {
            return;
        }

        TryCallNative(NativeSetFollowPedCamViewMode, Clamp(_terminatorStoredPedCameraViewMode, 0, TerminatorFirstPersonViewMode));
        TryCallNative(NativeSetFollowVehicleCamViewMode, Clamp(_terminatorStoredVehicleCameraViewMode, 0, TerminatorFirstPersonViewMode));
    }

    private bool TryHandleTerminatorVisionKey(Keys keyCode)
    {
        if (!_terminatorModeEnabled || keyCode != Keys.B)
        {
            return false;
        }

        CycleTerminatorVisionMode();
        return true;
    }

    private void CycleTerminatorVisionMode()
    {
        _terminatorVisionMode = (_terminatorVisionMode + 1) % TerminatorVisionModeCount;
        _terminatorNextVisionFilterAt = 0;

        Ped player = Game.Player.Character;

        if (Entity.Exists(player) && !player.IsDead && IsTerminatorFirstPersonCameraActive(player))
        {
            ApplyTerminatorVisionFilter(true);
        }
        else
        {
            ClearTerminatorVisionFilter();
        }

        ShowStatus("Vision Terminator: " + GetTerminatorVisionModeStatusText(_terminatorVisionMode), 2200);
    }

    private void ApplyTerminatorVisionFilter(bool force)
    {
        if (!force &&
            Game.GameTime < _terminatorNextVisionFilterAt &&
            _terminatorAppliedVisionMode == _terminatorVisionMode)
        {
            return;
        }

        _terminatorNextVisionFilterAt = Game.GameTime + TerminatorVisionFilterRefreshMs;

        if (force || _terminatorAppliedVisionMode != _terminatorVisionMode)
        {
            ClearTerminatorVisionFilter();
        }

        switch (_terminatorVisionMode)
        {
            case TerminatorVisionModeNight:
                TryCallNative(NativeSetNightvision, true);
                _terminatorLowLightVisionApplied = true;
                break;

            case TerminatorVisionModeThermal:
                TryCallNative(NativeSetSeethrough, true);
                _terminatorThermalVisionApplied = true;
                break;

            case TerminatorVisionModeRed:
            default:
                TryCallNative(NativeSetTimecycleModifier, "REDMIST_blend");
                TryCallNative(NativeSetTimecycleModifierStrength, 0.42f);
                _terminatorVisionFilterApplied = true;
                break;
        }

        _terminatorAppliedVisionMode = _terminatorVisionMode;
    }

    private void MaintainTerminatorVisionFilter(Ped player)
    {
        if (IsTerminatorFirstPersonCameraActive(player))
        {
            ApplyTerminatorVisionFilter(false);
        }
        else
        {
            ClearTerminatorVisionFilter();
        }
    }

    private void ClearTerminatorVisionFilter()
    {
        if (!_terminatorVisionFilterApplied && !_terminatorLowLightVisionApplied && !_terminatorThermalVisionApplied)
        {
            _terminatorAppliedVisionMode = TerminatorVisionModeNone;
            return;
        }

        if (_terminatorVisionFilterApplied)
        {
            TryCallNative(NativeClearTimecycleModifier);
            _terminatorVisionFilterApplied = false;
        }

        if (_terminatorLowLightVisionApplied)
        {
            TryCallNative(NativeSetNightvision, false);
            _terminatorLowLightVisionApplied = false;
        }

        if (_terminatorThermalVisionApplied)
        {
            TryCallNative(NativeSetSeethrough, false);
            _terminatorThermalVisionApplied = false;
        }

        _terminatorAppliedVisionMode = TerminatorVisionModeNone;
    }

    private static string GetTerminatorVisionModeStatusText(int mode)
    {
        switch (mode)
        {
            case TerminatorVisionModeNight:
                return "NOCTURNE VERTE";

            case TerminatorVisionModeThermal:
                return "THERMIQUE";

            case TerminatorVisionModeRed:
            default:
                return "ROUGE NORMALE";
        }
    }

    private static string GetTerminatorVisionModeHudText(int mode)
    {
        switch (mode)
        {
            case TerminatorVisionModeNight:
                return "VISION NOCTURNE VERTE";

            case TerminatorVisionModeThermal:
                return "VISION THERMIQUE";

            case TerminatorVisionModeRed:
            default:
                return "VISION ROUGE ACTIVE";
        }
    }

    private static string GetTerminatorVisionModeFeedText(int mode)
    {
        switch (mode)
        {
            case TerminatorVisionModeNight:
                return "FIRST PERSON OPTICAL FEED // LOW LIGHT GREEN";

            case TerminatorVisionModeThermal:
                return "FIRST PERSON OPTICAL FEED // THERMAL SPECTRUM";

            case TerminatorVisionModeRed:
            default:
                return "FIRST PERSON OPTICAL FEED // RED SPECTRUM";
        }
    }

    private Color GetTerminatorVisionOverlayColor(bool impactPulse)
    {
        switch (_terminatorVisionMode)
        {
            case TerminatorVisionModeNight:
                return Color.FromArgb(impactPulse ? 32 : 16, 0, 255, 82);

            case TerminatorVisionModeThermal:
                return Color.FromArgb(impactPulse ? 30 : 14, 255, 96, 28);

            case TerminatorVisionModeRed:
            default:
                return Color.FromArgb(impactPulse ? 42 : 24, 255, 0, 0);
        }
    }

    private Color GetTerminatorVisionSideShadeColor()
    {
        switch (_terminatorVisionMode)
        {
            case TerminatorVisionModeNight:
                return Color.FromArgb(36, 0, 12, 2);

            case TerminatorVisionModeThermal:
                return Color.FromArgb(34, 18, 4, 0);

            case TerminatorVisionModeRed:
            default:
                return Color.FromArgb(48, 12, 0, 0);
        }
    }

    private void UpdateTerminatorPunchPower(Ped player)
    {
        if (!Entity.Exists(player) || player.IsDead || player.IsInVehicle())
        {
            _terminatorWasMeleeActive = false;
            return;
        }

        int now = Game.GameTime;

        if (IsTerminatorWeaponFireRecentlyActive(player, now))
        {
            _terminatorWasMeleeActive = false;

            if (now >= _terminatorNextDamageFlagCleanupAt)
            {
                ClearTerminatorNearbyDamageFlags(player);
                _terminatorNextDamageFlagCleanupAt = now + TerminatorDamageFlagCleanupIntervalMs;
            }

            return;
        }

        bool meleeActive = IsTerminatorMeleeActionActive(player);

        if (!meleeActive)
        {
            if (_terminatorWasMeleeActive || now >= _terminatorNextDamageFlagCleanupAt)
            {
                ClearTerminatorNearbyDamageFlags(player);
                _terminatorNextDamageFlagCleanupAt = now + TerminatorDamageFlagCleanupIntervalMs;
            }

            _terminatorWasMeleeActive = false;
            return;
        }

        if (!_terminatorWasMeleeActive)
        {
            _terminatorWasMeleeActive = true;
            _terminatorMeleeStartedAt = now;
            ClearTerminatorNearbyDamageFlags(player);
            return;
        }

        if (now - _terminatorMeleeStartedAt < 80 || now < _terminatorNextMeleeImpactAllowedAt)
        {
            return;
        }

        PruneTerminatorPushCooldowns(now);

        int explicitMeleeTargetHandle = SafeGetMeleeTargetForPed(player);

        Ped hitPed = FindTerminatorImpactedPed(player, explicitMeleeTargetHandle, now);

        if (Entity.Exists(hitPed))
        {
            ApplyTerminatorPunchToPed(player, hitPed, now);
            _terminatorNextMeleeImpactAllowedAt = now + 210;
            return;
        }

        Vehicle hitVehicle = FindTerminatorImpactedVehicle(player, explicitMeleeTargetHandle, now);

        if (Entity.Exists(hitVehicle))
        {
            ApplyTerminatorPunchToVehicle(player, hitVehicle, now);
            _terminatorNextMeleeImpactAllowedAt = now + 260;
        }
    }

    private Ped FindTerminatorImpactedPed(Ped player, int explicitMeleeTargetHandle, int now)
    {
        Ped[] nearbyPeds = GetNearbyPedsSafe(player, TerminatorPedImpactRadius + 0.9f);

        if (nearbyPeds == null || nearbyPeds.Length == 0)
        {
            return null;
        }

        Ped best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nearbyPeds.Length; i++)
        {
            Ped target = nearbyPeds[i];

            if (!Entity.Exists(target) || target.Handle == player.Handle || target.IsDead)
            {
                continue;
            }

            if (!HasFreshTerminatorMeleeImpact(player, target, TerminatorPedImpactRadius, explicitMeleeTargetHandle, now))
            {
                continue;
            }

            float distance = player.Position.DistanceTo(target.Position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        return best;
    }

    private Vehicle FindTerminatorImpactedVehicle(Ped player, int explicitMeleeTargetHandle, int now)
    {
        Vehicle[] nearbyVehicles = GetNearbyVehiclesSafe(player, TerminatorVehicleImpactRadius + 1.25f);

        if (nearbyVehicles == null || nearbyVehicles.Length == 0)
        {
            return null;
        }

        Vehicle best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nearbyVehicles.Length; i++)
        {
            Vehicle vehicle = nearbyVehicles[i];

            if (!Entity.Exists(vehicle) || vehicle.IsDead || !IsVehicleDriveable(vehicle))
            {
                continue;
            }

            if (!HasFreshTerminatorMeleeImpact(player, vehicle, TerminatorVehicleImpactRadius, explicitMeleeTargetHandle, now))
            {
                continue;
            }

            float distance = player.Position.DistanceTo(vehicle.Position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = vehicle;
            }
        }

        return best;
    }

    private bool HasFreshTerminatorMeleeImpact(Ped player, Entity target, float radius, int explicitMeleeTargetHandle, int now)
    {
        if (!Entity.Exists(player) || !Entity.Exists(target) || target.Handle == player.Handle)
        {
            return false;
        }

        if (now - _terminatorLastWeaponFireAt < TerminatorWeaponFireImpactBlockMs)
        {
            return false;
        }

        if (_terminatorPushCooldowns.ContainsKey(target.Handle) && now < _terminatorPushCooldowns[target.Handle])
        {
            return false;
        }

        float distance = player.Position.DistanceTo(target.Position);

        if (distance > radius)
        {
            return false;
        }

        if (!HasEntityBeenDamagedByEntitySafe(target, player))
        {
            return false;
        }

        bool explicitMatch = explicitMeleeTargetHandle != 0 && target.Handle == explicitMeleeTargetHandle;
        bool physicalContact = AreEntitiesTouching(player, target);

        if (explicitMatch || physicalContact)
        {
            return true;
        }

        return IsEntityInsideTerminatorPunchCone(player, target, TerminatorImpactConeDot);
    }

    private bool IsTerminatorMeleeActionActive(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        if (IsTerminatorPedShootingSafe(player))
        {
            return false;
        }

        if (IsTerminatorMeleeControlPressed())
        {
            return true;
        }

        if (!IsTerminatorSelectedWeaponMeleeCompatible(player))
        {
            return false;
        }

        if (IsTerminatorPedPerformingMeleeActionSafe(player))
        {
            return true;
        }

        return IsPedInMeleeCombatSafe(player);
    }

    private bool IsTerminatorWeaponFireRecentlyActive(Ped player, int now)
    {
        if (IsTerminatorPedShootingSafe(player))
        {
            _terminatorLastWeaponFireAt = now;
            ClearTerminatorNearbyDamageFlags(player);
            return true;
        }

        return now - _terminatorLastWeaponFireAt < TerminatorWeaponFireImpactBlockMs;
    }

    private static bool IsTerminatorPedShootingSafe(Ped player)
    {
        try
        {
            return IsPedShooting(player);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTerminatorPedPerformingMeleeActionSafe(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return false;
        }

        try
        {
            return Function.Call<bool>((Hash)NativeIsPedPerformingMeleeAction, player.Handle);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTerminatorMeleeControlPressed()
    {
        return IsTerminatorControlPressed(TerminatorControlMeleeLight) ||
               IsTerminatorControlPressed(TerminatorControlMeleeHeavy) ||
               IsTerminatorControlPressed(TerminatorControlMeleeAlternate);
    }

    private static bool IsTerminatorSelectedWeaponMeleeCompatible(Ped player)
    {
        int weaponHash = SafeGetSelectedPedWeaponHash(player);

        if (weaponHash == 0 || weaponHash == unchecked((int)WeaponHash.Unarmed))
        {
            return true;
        }

        string weaponName = ((WeaponHash)weaponHash).ToString();

        if (string.IsNullOrEmpty(weaponName))
        {
            return false;
        }

        return weaponName.IndexOf("Knife", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Nightstick", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Bat", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Crowbar", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("GolfClub", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Bottle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Dagger", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Hatchet", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Knuckle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Machete", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Flashlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("PoolCue", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("Wrench", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("BattleAxe", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("StoneHatchet", StringComparison.OrdinalIgnoreCase) >= 0 ||
               weaponName.IndexOf("CandyCane", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int SafeGetSelectedPedWeaponHash(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return 0;
        }

        try
        {
            return Function.Call<int>((Hash)NativeGetSelectedPedWeapon, ped.Handle);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsTerminatorAimControlPressed()
    {
        return IsTerminatorControlPressed(TerminatorControlAim);
    }

    private static bool IsTerminatorControlPressed(int control)
    {
        try
        {
            return Function.Call<bool>((Hash)NativeIsControlPressed, 0, control);
        }
        catch
        {
            return false;
        }
    }

    private static int SafeGetMeleeTargetForPed(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return 0;
        }

        try
        {
            return Function.Call<int>((Hash)NativeGetMeleeTargetForPed, player.Handle);
        }
        catch
        {
            return 0;
        }
    }

    private bool IsEntityInsideTerminatorPunchCone(Ped player, Entity target, float minDot)
    {
        Vector3 toTarget = target.Position - player.Position;
        toTarget.Z = 0.0f;

        Vector3 direction = Normalize(toTarget);
        Vector3 forward = Normalize(new Vector3(player.ForwardVector.X, player.ForwardVector.Y, 0.0f));

        if (IsZeroVector(direction) || IsZeroVector(forward))
        {
            return true;
        }

        float dot = (forward.X * direction.X) + (forward.Y * direction.Y);
        return dot >= minDot;
    }

    private void ApplyTerminatorPunchToPed(Ped player, Ped target, int now)
    {
        Vector3 direction = CalculateTerminatorPushDirection(player, target);
        Vector3 velocity = (direction * TerminatorPedThrowSpeed) + new Vector3(0.0f, 0.0f, 4.25f);

        TryCallNative(NativeSetPedCanRagdoll, target.Handle, true);
        TryCallNative(NativeSetPedToRagdoll, target.Handle, 1150, 1750, 0, false, false, false);
        TryCallNative(NativeSetEntityVelocity, target.Handle, velocity.X, velocity.Y, velocity.Z);
        TryCallNative(
            NativeApplyForceToEntity,
            target.Handle,
            1,
            direction.X * 56.0f,
            direction.Y * 56.0f,
            18.0f,
            0.0f,
            0.0f,
            0.0f,
            0,
            false,
            true,
            true,
            false,
            true);

        MarkTerminatorEntityPushed(target, now);
    }

    private void ApplyTerminatorPunchToVehicle(Ped player, Vehicle vehicle, int now)
    {
        Vector3 direction = CalculateTerminatorPushDirection(player, vehicle);
        Vector3 velocity = (direction * TerminatorVehiclePushSpeed) + new Vector3(0.0f, 0.0f, 0.55f);

        TryCallNative(NativeSetEntityVelocity, vehicle.Handle, velocity.X, velocity.Y, velocity.Z);
        TryCallNative(
            NativeApplyForceToEntity,
            vehicle.Handle,
            1,
            direction.X * 27.5f,
            direction.Y * 27.5f,
            3.8f,
            0.0f,
            0.0f,
            0.0f,
            0,
            false,
            true,
            true,
            false,
            true);

        MarkTerminatorEntityPushed(vehicle, now);
    }

    private Vector3 CalculateTerminatorPushDirection(Ped player, Entity target)
    {
        Vector3 direction = target.Position - player.Position;
        direction.Z = 0.0f;
        direction = Normalize(direction);

        if (IsZeroVector(direction))
        {
            direction = Normalize(new Vector3(player.ForwardVector.X, player.ForwardVector.Y, 0.0f));
        }

        if (IsZeroVector(direction))
        {
            direction = new Vector3(0.0f, 1.0f, 0.0f);
        }

        return direction;
    }

    private void MarkTerminatorEntityPushed(Entity target, int now)
    {
        if (!Entity.Exists(target))
        {
            return;
        }

        _terminatorPushCooldowns[target.Handle] = now + TerminatorPushCooldownMs;
        _terminatorImpactFlashUntil = now + 235;

        TryCallNative(NativeClearEntityLastDamageEntity, target.Handle);
    }

    private void ClearTerminatorNearbyDamageFlags(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return;
        }

        Ped[] peds = GetNearbyPedsSafe(player, 5.0f);

        if (peds != null)
        {
            for (int i = 0; i < peds.Length; i++)
            {
                Ped ped = peds[i];

                if (Entity.Exists(ped) && ped.Handle != player.Handle)
                {
                    TryCallNative(NativeClearEntityLastDamageEntity, ped.Handle);
                }
            }
        }

        Vehicle[] vehicles = GetNearbyVehiclesSafe(player, 6.0f);

        if (vehicles == null)
        {
            return;
        }

        for (int i = 0; i < vehicles.Length; i++)
        {
            Vehicle vehicle = vehicles[i];

            if (Entity.Exists(vehicle))
            {
                TryCallNative(NativeClearEntityLastDamageEntity, vehicle.Handle);
            }
        }
    }

    private void PruneTerminatorPushCooldowns(int now)
    {
        if (_terminatorPushCooldowns.Count == 0)
        {
            return;
        }

        List<int> expiredHandles = null;

        foreach (KeyValuePair<int, int> pair in _terminatorPushCooldowns)
        {
            if (now - pair.Value > TerminatorPushCacheCleanupMs)
            {
                if (expiredHandles == null)
                {
                    expiredHandles = new List<int>();
                }

                expiredHandles.Add(pair.Key);
            }
        }

        if (expiredHandles == null)
        {
            return;
        }

        for (int i = 0; i < expiredHandles.Count; i++)
        {
            _terminatorPushCooldowns.Remove(expiredHandles[i]);
        }
    }

    private void UpdateTerminatorFocusedTarget(Ped player)
    {
        int now = Game.GameTime;

        if (now < _terminatorNextFocusScanAt)
        {
            return;
        }

        _terminatorNextFocusScanAt = now + TerminatorFocusRefreshIntervalMs;

        if (!IsTerminatorAimControlPressed())
        {
            if (_terminatorFocusedTarget != null && now - _terminatorFocusedTarget.LastSeenAt > TerminatorFocusMemoryMs)
            {
                _terminatorFocusedTarget = null;
            }

            return;
        }

        TerminatorFocusTarget target = ResolveTerminatorFocusTarget(player);

        if (target != null)
        {
            target.LastSeenAt = now;
            _terminatorFocusedTarget = target;
        }
        else if (_terminatorFocusedTarget != null && now - _terminatorFocusedTarget.LastSeenAt > TerminatorFocusMemoryMs)
        {
            _terminatorFocusedTarget = null;
        }
    }

    private TerminatorFocusTarget ResolveTerminatorFocusTarget(Ped player)
    {
        int freeAimHandle = SafeGetFreeAimingEntityHandle();

        if (freeAimHandle != 0)
        {
            TerminatorFocusTarget freeAimTarget = BuildTerminatorFocusTargetFromHandle(player, freeAimHandle, true);

            if (freeAimTarget != null)
            {
                return freeAimTarget;
            }
        }

        return BuildTerminatorFocusTargetFromCrosshair(player);
    }

    private static int SafeGetFreeAimingEntityHandle()
    {
        try
        {
            OutputArgument entityHandle = new OutputArgument();

            bool found = Function.Call<bool>(
                (Hash)NativeGetEntityPlayerIsFreeAimingAt,
                Game.Player.Handle,
                entityHandle);

            if (!found)
            {
                return 0;
            }

            return entityHandle.GetResult<int>();
        }
        catch
        {
            return 0;
        }
    }

    private TerminatorFocusTarget BuildTerminatorFocusTargetFromHandle(Ped player, int handle, bool fromFreeAim)
    {
        if (!Entity.Exists(player) || handle == 0 || handle == player.Handle)
        {
            return null;
        }

        Ped[] peds = GetNearbyPedsSafe(player, TerminatorFocusRadius);

        if (peds != null)
        {
            for (int i = 0; i < peds.Length; i++)
            {
                Ped ped = peds[i];

                if (Entity.Exists(ped) && ped.Handle == handle && !ped.IsDead)
                {
                    return CreateTerminatorPedFocusTarget(player, ped, fromFreeAim);
                }
            }
        }

        Vehicle[] vehicles = GetNearbyVehiclesSafe(player, TerminatorFocusRadius);

        if (vehicles == null)
        {
            return null;
        }

        for (int i = 0; i < vehicles.Length; i++)
        {
            Vehicle vehicle = vehicles[i];

            if (Entity.Exists(vehicle) && vehicle.Handle == handle && !vehicle.IsDead)
            {
                return CreateTerminatorVehicleFocusTarget(player, vehicle, fromFreeAim);
            }
        }

        return null;
    }

    private TerminatorFocusTarget BuildTerminatorFocusTargetFromCrosshair(Ped player)
    {
        if (!Entity.Exists(player))
        {
            return null;
        }

        Vector3 cameraPosition;

        try
        {
            cameraPosition = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_COORD);
        }
        catch
        {
            cameraPosition = player.Position + new Vector3(0.0f, 0.0f, 0.8f);
        }

        float bestScore = float.MaxValue;
        TerminatorFocusTarget bestTarget = null;

        Ped[] peds = GetNearbyPedsSafe(player, TerminatorFocusRadius);

        if (peds != null)
        {
            for (int i = 0; i < peds.Length; i++)
            {
                Ped ped = peds[i];

                if (!Entity.Exists(ped) || ped.Handle == player.Handle || ped.IsDead)
                {
                    continue;
                }

                float score;

                if (!TryScoreTerminatorCrosshairCandidate(cameraPosition, ped, false, out score))
                {
                    continue;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = CreateTerminatorPedFocusTarget(player, ped, false);
                }
            }
        }

        Vehicle[] vehicles = GetNearbyVehiclesSafe(player, TerminatorFocusRadius);

        if (vehicles == null)
        {
            return bestTarget;
        }

        Vehicle playerVehicle = null;

        try
        {
            playerVehicle = player.CurrentVehicle;
        }
        catch
        {
        }

        for (int i = 0; i < vehicles.Length; i++)
        {
            Vehicle vehicle = vehicles[i];

            if (!Entity.Exists(vehicle) || vehicle.IsDead)
            {
                continue;
            }

            if (Entity.Exists(playerVehicle) && vehicle.Handle == playerVehicle.Handle)
            {
                continue;
            }

            float score;

            if (!TryScoreTerminatorCrosshairCandidate(cameraPosition, vehicle, true, out score))
            {
                continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = CreateTerminatorVehicleFocusTarget(player, vehicle, false);
            }
        }

        return bestTarget;
    }

    private bool TryScoreTerminatorCrosshairCandidate(Vector3 cameraPosition, Entity entity, bool isVehicle, out float score)
    {
        score = float.MaxValue;

        if (!Entity.Exists(entity))
        {
            return false;
        }

        Vector3 targetPosition = entity.Position + new Vector3(0.0f, 0.0f, isVehicle ? 0.95f : 0.85f);

        int screenX;
        int screenY;

        if (!TryTerminatorWorldToScreen(targetPosition, out screenX, out screenY))
        {
            return false;
        }

        float dx = screenX - (TerminatorHudWidth * 0.5f);
        float dy = screenY - (TerminatorHudHeight * 0.5f);
        float pixelDistance = (float)Math.Sqrt((dx * dx) + (dy * dy));
        float maxPixelDistance = isVehicle ? 58.0f : 42.0f;

        if (pixelDistance > maxPixelDistance)
        {
            return false;
        }

        float distance = cameraPosition.DistanceTo(targetPosition);

        if (distance > TerminatorFocusRadius)
        {
            return false;
        }

        Vector3 forward = GetGameplayCameraForwardVector();
        Vector3 toTarget = Normalize(targetPosition - cameraPosition);

        if (!IsZeroVector(forward) && !IsZeroVector(toTarget))
        {
            float dot = (forward.X * toTarget.X) + (forward.Y * toTarget.Y) + (forward.Z * toTarget.Z);

            if (dot < 0.82f)
            {
                return false;
            }
        }

        score = pixelDistance + (distance * 0.055f);
        return true;
    }

    private TerminatorFocusTarget CreateTerminatorPedFocusTarget(Ped player, Ped ped, bool fromFreeAim)
    {
        if (!Entity.Exists(player) || !Entity.Exists(ped))
        {
            return null;
        }

        return new TerminatorFocusTarget
        {
            Entity = ped,
            IsPed = true,
            IsVehicle = false,
            Label = "PNJ",
            Type = ResolveTerminatorPedType(ped),
            Faction = ResolveTerminatorPedFaction(ped),
            Weapon = SafeGetSelectedPedWeaponName(ped),
            Model = ResolveTerminatorPedModelName(ped),
            Health = SafeGetEntityHealth(ped),
            Armor = SafeGetPedArmour(ped),
            Distance = player.Position.DistanceTo(ped.Position),
            FromFreeAim = fromFreeAim
        };
    }

    private TerminatorFocusTarget CreateTerminatorVehicleFocusTarget(Ped player, Vehicle vehicle, bool fromFreeAim)
    {
        if (!Entity.Exists(player) || !Entity.Exists(vehicle))
        {
            return null;
        }

        return new TerminatorFocusTarget
        {
            Entity = vehicle,
            IsPed = false,
            IsVehicle = true,
            Label = "VEHICULE",
            Type = "VEHICULE",
            Faction = "N/A",
            Weapon = "N/A",
            Model = ResolveTerminatorEntityModelName(vehicle),
            Health = SafeGetEntityHealth(vehicle),
            Armor = 0,
            Distance = player.Position.DistanceTo(vehicle.Position),
            FromFreeAim = fromFreeAim
        };
    }

    private string ResolveTerminatorPedFaction(Ped ped)
    {
        SpawnedNpc managedNpc = FindTerminatorManagedNpc(ped);

        if (managedNpc != null)
        {
            if (IsAllyBehavior(managedNpc.BaseBehavior))
            {
                return "ALLIE";
            }

            if (IsNeutralBehavior(managedNpc.BaseBehavior))
            {
                return "NEUTRE";
            }

            if (IsHostileBehavior(managedNpc.BaseBehavior))
            {
                return "HOSTILE";
            }

            return "PLACE";
        }

        try
        {
            if (IsManagedAlly(ped))
            {
                return "ALLIE";
            }
        }
        catch
        {
        }

        return "CIVIL / INCONNU";
    }

    private string ResolveTerminatorPedType(Ped ped)
    {
        SpawnedNpc managedNpc = FindTerminatorManagedNpc(ped);

        if (managedNpc != null)
        {
            string behavior = managedNpc.BaseBehavior.ToString().ToUpperInvariant();

            if (IsPatrolBehavior(managedNpc.BaseBehavior))
            {
                return "PATROUILLE " + FitText(behavior, 18);
            }

            return FitText(behavior, 22);
        }

        try
        {
            if (ped.IsInVehicle())
            {
                return "PNJ EN VEHICULE";
            }
        }
        catch
        {
        }

        return "PIETON";
    }

    private string ResolveTerminatorPedModelName(Ped ped)
    {
        SpawnedNpc managedNpc = FindTerminatorManagedNpc(ped);

        if (managedNpc != null && managedNpc.ModelIdentity != null)
        {
            if (!string.IsNullOrWhiteSpace(managedNpc.ModelIdentity.DisplayName))
            {
                return managedNpc.ModelIdentity.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(managedNpc.ModelIdentity.Name))
            {
                return managedNpc.ModelIdentity.Name;
            }
        }

        return ResolveTerminatorEntityModelName(ped);
    }

    private string ResolveTerminatorEntityModelName(Entity entity)
    {
        int modelHash = SafeGetEntityModelHash(entity);

        if (modelHash == 0)
        {
            return "UNKNOWN";
        }

        return "0x" + unchecked((uint)modelHash).ToString("X8", CultureInfo.InvariantCulture);
    }

    private SpawnedNpc FindTerminatorManagedNpc(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return null;
        }

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            SpawnedNpc npc = _spawnedNpcs[i];

            if (npc == null || !Entity.Exists(npc.Ped))
            {
                continue;
            }

            if (npc.Ped.Handle == ped.Handle)
            {
                return npc;
            }
        }

        return null;
    }

    private static int SafeGetEntityHealth(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return 0;
        }

        try
        {
            return Math.Max(0, Function.Call<int>((Hash)NativeGetEntityHealth, entity.Handle));
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeGetEntityModelHash(Entity entity)
    {
        if (!Entity.Exists(entity))
        {
            return 0;
        }

        try
        {
            return Function.Call<int>(Hash.GET_ENTITY_MODEL, entity.Handle);
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeGetSelectedPedWeaponName(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return "UNKNOWN";
        }

        try
        {
            int weaponHash = Function.Call<int>((Hash)NativeGetSelectedPedWeapon, ped.Handle);
            string enumName = ((WeaponHash)weaponHash).ToString();

            if (!string.IsNullOrEmpty(enumName) &&
                !enumName.Equals(weaponHash.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return FitText(enumName.ToUpperInvariant(), 24);
            }

            return "0x" + unchecked((uint)weaponHash).ToString("X8", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "UNKNOWN";
        }
    }

    private static int SafeGetCameraViewMode(ulong nativeHash, int fallback)
    {
        try
        {
            int value = Function.Call<int>((Hash)nativeHash);

            if (value >= 0 && value <= TerminatorFirstPersonViewMode)
            {
                return value;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static int SafeGetPedHealth(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return 0;
        }

        try
        {
            int nativeHealth = Function.Call<int>((Hash)NativeGetEntityHealth, ped.Handle);

            if (nativeHealth > 0)
            {
                return nativeHealth;
            }
        }
        catch
        {
        }

        try
        {
            return Math.Max(0, ped.Health);
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeGetPedMaxHealth(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return 100;
        }

        try
        {
            int nativeMaxHealth = Function.Call<int>((Hash)NativeGetEntityMaxHealth, ped.Handle);

            if (nativeMaxHealth > 0)
            {
                return nativeMaxHealth;
            }
        }
        catch
        {
        }

        try
        {
            return Math.Max(100, ped.MaxHealth);
        }
        catch
        {
            return 100;
        }
    }

    private static int SafeGetPedArmour(Ped ped)
    {
        if (!Entity.Exists(ped))
        {
            return 0;
        }

        try
        {
            int nativeArmor = Function.Call<int>((Hash)NativeGetPedArmour, ped.Handle);

            if (nativeArmor >= 0)
            {
                return nativeArmor;
            }
        }
        catch
        {
        }

        try
        {
            return Math.Max(0, ped.Armor);
        }
        catch
        {
            return 0;
        }
    }

    private static void SafeSetPedMaxHealth(Ped ped, int maxHealth)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        int safeMaxHealth = Math.Max(100, maxHealth);

        try
        {
            ped.MaxHealth = safeMaxHealth;
        }
        catch
        {
        }

        TryCallNative(NativeSetEntityMaxHealth, ped.Handle, safeMaxHealth);
    }

    private static void SafeSetPedHealth(Ped ped, int health)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        int safeHealth = Math.Max(1, health);

        try
        {
            ped.Health = safeHealth;
        }
        catch
        {
        }
    }

    private static void SafeSetPedArmour(Ped ped, int armor)
    {
        if (!Entity.Exists(ped))
        {
            return;
        }

        int safeArmor = Math.Max(0, armor);

        try
        {
            ped.Armor = safeArmor;
        }
        catch
        {
        }

        TryCallNative(NativeSetPedArmour, ped.Handle, safeArmor);
    }

    private void DrawTerminatorRedVisionOverlay(Ped player)
    {
        int now = Game.GameTime;
        bool impactPulse = now < _terminatorImpactFlashUntil;

        DrawRect(0, 0, TerminatorHudWidth, TerminatorHudHeight, GetTerminatorVisionOverlayColor(impactPulse));

        DrawRect(0, 0, 88, TerminatorHudHeight, GetTerminatorVisionSideShadeColor());
        DrawRect(TerminatorHudWidth - 88, 0, 88, TerminatorHudHeight, GetTerminatorVisionSideShadeColor());

        int sweepX = (now / 18) % TerminatorHudWidth;
        DrawRect(sweepX, 0, 1, TerminatorHudHeight, Color.FromArgb(impactPulse ? 96 : 38, 255, 80, 80));
        DrawRect(0, 344, TerminatorHudWidth, 1, Color.FromArgb(24, 255, 90, 90));

        DrawTerminatorSideRulers();
        DrawTerminatorCentralReticle();
        DrawTerminatorAssessmentBlock();
        DrawTerminatorRightCodeColumn();
        DrawTerminatorPlayerStatus(player);
    }

    private void DrawTerminatorSideRulers()
    {
        Color red = Color.FromArgb(210, 255, 70, 70);
        Color softRed = Color.FromArgb(124, 255, 74, 74);

        DrawRect(70, 40, 2, 620, red);
        DrawRect(92, 150, 2, 458, softRed);
        DrawRect(1208, 40, 2, 620, red);
        DrawRect(1186, 150, 2, 458, softRed);

        for (int i = 0; i <= 10; i++)
        {
            int y = 64 + (i * 56);
            int tick = (i % 2 == 0) ? 24 : 13;

            DrawRect(60, y, tick, 2, red);
            DrawRect(1208, y, tick, 2, red);

            if (i > 0 && i < 10)
            {
                DrawText(i.ToString(CultureInfo.InvariantCulture), 42, y - 8, 0.180f, Color.FromArgb(160, 255, 112, 112), false, false);
                DrawText(i.ToString(CultureInfo.InvariantCulture), 1232, y - 8, 0.180f, Color.FromArgb(160, 255, 112, 112), false, false);
            }
        }

        DrawRect(102, 160, 34, 2, red);
        DrawRect(102, 160, 2, 56, red);
        DrawRect(102, 536, 34, 2, red);
        DrawRect(102, 482, 2, 56, red);

        DrawRect(1144, 160, 34, 2, red);
        DrawRect(1176, 160, 2, 56, red);
        DrawRect(1144, 536, 34, 2, red);
        DrawRect(1176, 482, 2, 56, red);
    }

    private void DrawTerminatorCentralReticle()
    {
        Color red = Color.FromArgb(215, 255, 60, 60);
        Color soft = Color.FromArgb(112, 255, 96, 96);

        int centerX = TerminatorHudWidth / 2;
        int centerY = TerminatorHudHeight / 2;

        DrawRect(centerX - 42, centerY, 26, 2, red);
        DrawRect(centerX + 16, centerY, 26, 2, red);
        DrawRect(centerX, centerY - 42, 2, 26, red);
        DrawRect(centerX, centerY + 16, 2, 26, red);

        DrawRect(centerX - 7, centerY - 1, 14, 2, soft);
        DrawRect(centerX - 1, centerY - 7, 2, 14, soft);

        DrawRect(centerX - 84, centerY - 62, 30, 2, soft);
        DrawRect(centerX - 84, centerY - 62, 2, 24, soft);
        DrawRect(centerX + 54, centerY - 62, 30, 2, soft);
        DrawRect(centerX + 82, centerY - 62, 2, 24, soft);
        DrawRect(centerX - 84, centerY + 60, 30, 2, soft);
        DrawRect(centerX - 84, centerY + 36, 2, 24, soft);
        DrawRect(centerX + 54, centerY + 60, 30, 2, soft);
        DrawRect(centerX + 82, centerY + 36, 2, 24, soft);
    }

    private void DrawTerminatorAssessmentBlock()
    {
        int x = 126;
        int y = 82;

        DrawText(">> VISUAL ASSESSMENT", x, y, 0.230f, Color.FromArgb(230, 255, 104, 104), false, true);

        string[] labels = { "SCAN", "LEVEL", "READ", "TRACK", "MASK", "BANK", "RUN", "RAM" };

        for (int i = 0; i < labels.Length; i++)
        {
            int valueA = TerminatorPseudoNumber(i, 0, 9973);
            int valueB = TerminatorPseudoNumber(i, 1, 7919);
            DrawText(labels[i], x, y + 28 + (i * 17), 0.190f, Color.FromArgb(206, 255, 136, 136), false, false);
            DrawText(valueA.ToString("0000", CultureInfo.InvariantCulture), x + 108, y + 28 + (i * 17), 0.190f, Color.FromArgb(218, 255, 170, 170), false, false);
            DrawText(valueB.ToString("0000", CultureInfo.InvariantCulture), x + 158, y + 28 + (i * 17), 0.190f, Color.FromArgb(186, 255, 126, 126), false, false);
        }

        DrawRect(x, y + 178, 238, 2, Color.FromArgb(185, 255, 70, 70));

        for (int row = 0; row < 10; row++)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:00000000}   {1:000}   {2:00}   {3:0000}",
                TerminatorPseudoNumber(row, 2, 100000000),
                TerminatorPseudoNumber(row, 3, 1000),
                TerminatorPseudoNumber(row, 4, 100),
                TerminatorPseudoNumber(row, 5, 10000));

            DrawText(line, x, y + 194 + (row * 17), 0.180f, Color.FromArgb(184, 255, 130, 130), false, false);
        }
    }

    private void DrawTerminatorRightCodeColumn()
    {
        int x = 1012;
        int y = 92;

        DrawText("<< COURT >>", x + 78, y, 0.190f, Color.FromArgb(210, 255, 118, 118), false, true);
        DrawText("DATE  33 85 44", x + 78, y + 18, 0.170f, Color.FromArgb(170, 255, 150, 150), false, false);
        DrawText("7MM   12 84 96", x + 78, y + 35, 0.170f, Color.FromArgb(170, 255, 150, 150), false, false);

        DrawRect(x, y + 82, 214, 2, Color.FromArgb(170, 255, 72, 72));

        string[] tags = { "LONG", "RAMP", "TRIP", "READ", "HOME", "LAT", "SHIFT", "HEAD", "BANK", "COPE", "FIELD", "RATE" };

        for (int i = 0; i < tags.Length; i++)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}  {1,-5}  {2:0000}",
                TerminatorPseudoNumber(i, 6, 99),
                tags[i],
                TerminatorPseudoNumber(i, 7, 10000));

            DrawText(line, x + 8, y + 98 + (i * 17), 0.175f, Color.FromArgb(175, 255, 132, 132), false, false);
        }

        DrawRect(x, y + 315, 214, 2, Color.FromArgb(150, 255, 72, 72));

        for (int row = 0; row < 12; row++)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:000}   AE{1:00}   B{2:00}   {3:00000000}",
                172 + row,
                TerminatorPseudoNumber(row, 8, 90),
                TerminatorPseudoNumber(row, 9, 20),
                TerminatorPseudoNumber(row, 10, 100000000));

            DrawText(line, x + 8, y + 332 + (row * 17), 0.170f, Color.FromArgb(154, 255, 118, 118), false, false);
        }
    }

    private void DrawTerminatorPlayerStatus(Ped player)
    {
        int hp = SafeGetPedHealth(player);
        int maxHp = Math.Max(TerminatorMinHealth, SafeGetPedMaxHealth(player));
        int armor = SafeGetPedArmour(player);
        string weapon = SafeGetSelectedPedWeaponName(player);

        DrawTerminatorPanel(146, TerminatorHudHeight - 92, 622, 48);
        DrawText("CYBERDYNE SYSTEMS MODEL T-800", 162, TerminatorHudHeight - 82, 0.230f, Color.FromArgb(230, 255, 96, 96), false, true);
        DrawText(
            "HP " + hp.ToString(CultureInfo.InvariantCulture) + "/" + maxHp.ToString(CultureInfo.InvariantCulture) +
            "   ARMURE " + armor.ToString(CultureInfo.InvariantCulture) +
            "   ARME " + FitText(weapon, 18) +
            "   " + GetTerminatorVisionModeHudText(_terminatorVisionMode),
            162,
            TerminatorHudHeight - 56,
            0.200f,
            Color.FromArgb(204, 255, 156, 156),
            false,
            false);

        DrawText("MODE TERMINATOR", TerminatorHudWidth / 2, 42, 0.310f, Color.FromArgb(220, 255, 80, 80), true, true);
        DrawText(GetTerminatorVisionModeFeedText(_terminatorVisionMode), TerminatorHudWidth / 2, 68, 0.185f, Color.FromArgb(172, 255, 132, 132), true, false);
    }

    private void DrawTerminatorFocusedTargetPanel(Ped player)
    {
        if (_terminatorFocusedTarget == null || !Entity.Exists(_terminatorFocusedTarget.Entity))
        {
            return;
        }

        int now = Game.GameTime;

        if (now - _terminatorFocusedTarget.LastSeenAt > TerminatorFocusMemoryMs)
        {
            return;
        }

        TerminatorFocusTarget target = _terminatorFocusedTarget;

        DrawTerminatorPanel(846, 246, 360, target.IsPed ? 212 : 166);
        DrawText(">> TARGET PROFILE", 866, 260, 0.245f, Color.FromArgb(235, 255, 92, 92), false, true);
        DrawText("LOCK: " + (target.FromFreeAim ? "FREE AIM" : "RETICLE"), 866, 286, 0.185f, Color.FromArgb(180, 255, 150, 150), false, false);

        DrawTerminatorInfoRow("TYPE", target.Type, 866, 318);
        DrawTerminatorInfoRow("FACTION", target.Faction, 866, 341);
        DrawTerminatorInfoRow("VIE", target.Health.ToString(CultureInfo.InvariantCulture), 866, 364);

        if (target.IsPed)
        {
            DrawTerminatorInfoRow("ARMURE", target.Armor.ToString(CultureInfo.InvariantCulture), 866, 387);
            DrawTerminatorInfoRow("ARME", target.Weapon, 866, 410);
            DrawTerminatorInfoRow("MODELE", target.Model, 866, 433);
            DrawTerminatorInfoRow("DISTANCE", target.Distance.ToString("0.0", CultureInfo.InvariantCulture) + " M", 866, 456);
        }
        else
        {
            DrawTerminatorInfoRow("MODELE", target.Model, 866, 387);
            DrawTerminatorInfoRow("DISTANCE", target.Distance.ToString("0.0", CultureInfo.InvariantCulture) + " M", 866, 410);
        }

        DrawRect(866, target.IsPed ? 482 : 436, 316, 2, Color.FromArgb(155, 255, 72, 72));
        DrawText("ASSESSMENT: " + (target.IsPed ? "BIOLOGICAL UNIT" : "MOBILE OBJECT"), 866, target.IsPed ? 492 : 446, 0.178f, Color.FromArgb(172, 255, 132, 132), false, false);
    }

    private void DrawTerminatorInfoRow(string label, string value, int x, int y)
    {
        DrawText(label + " ....", x, y, 0.195f, Color.FromArgb(178, 255, 120, 120), false, false);
        DrawText(FitText(value ?? "UNKNOWN", 28), x + 108, y, 0.195f, Color.FromArgb(222, 255, 174, 174), false, false);
    }

    private void DrawTerminatorPanel(int x, int y, int width, int height)
    {
        DrawRect(x, y, width, height, Color.FromArgb(64, 0, 0, 0));
        DrawRect(x, y, width, 2, Color.FromArgb(196, 255, 56, 56));
        DrawRect(x, y + height - 2, width, 2, Color.FromArgb(120, 255, 56, 56));
        DrawRect(x, y, 2, height, Color.FromArgb(158, 255, 56, 56));
        DrawRect(x + width - 2, y, 2, height, Color.FromArgb(98, 255, 56, 56));
    }

    private static int TerminatorPseudoNumber(int row, int column, int modulo)
    {
        if (modulo <= 1)
        {
            return 0;
        }

        unchecked
        {
            uint tick = (uint)(Game.GameTime / 320);
            uint value =
                ((uint)(row + 3) * 1103515245u) ^
                ((uint)(column + 11) * 12345u) ^
                (tick * 2654435761u);

            return (int)(value % (uint)modulo);
        }
    }

    private static bool TryTerminatorWorldToScreen(Vector3 worldPosition, out int screenX, out int screenY)
    {
        screenX = 0;
        screenY = 0;

        try
        {
            OutputArgument screenXArg = new OutputArgument();
            OutputArgument screenYArg = new OutputArgument();

            bool success = Function.Call<bool>(
                (Hash)NativeGetScreenCoordFromWorldCoord,
                worldPosition.X,
                worldPosition.Y,
                worldPosition.Z,
                screenXArg,
                screenYArg);

            if (!success)
            {
                return false;
            }

            float normalizedX = screenXArg.GetResult<float>();
            float normalizedY = screenYArg.GetResult<float>();

            if (normalizedX < 0.0f || normalizedX > 1.0f || normalizedY < 0.0f || normalizedY > 1.0f)
            {
                return false;
            }

            screenX = (int)Math.Round(normalizedX * TerminatorHudWidth);
            screenY = (int)Math.Round(normalizedY * TerminatorHudHeight);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCallNative(ulong nativeHash)
    {
        try
        {
            Function.Call((Hash)nativeHash);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, string arg0)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, float arg0)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, bool arg0)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, int arg0)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, int arg0, int arg1)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0, arg1);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, int arg0, bool arg1)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0, arg1);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, int arg0, float arg1, float arg2, float arg3)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0, arg1, arg2, arg3);
        }
        catch
        {
        }
    }

    private static void TryCallNative(ulong nativeHash, int arg0, int arg1, int arg2, int arg3, bool arg4, bool arg5, bool arg6)
    {
        try
        {
            Function.Call((Hash)nativeHash, arg0, arg1, arg2, arg3, arg4, arg5, arg6);
        }
        catch
        {
        }
    }

    private static void TryCallNative(
        ulong nativeHash,
        int arg0,
        int arg1,
        float arg2,
        float arg3,
        float arg4,
        float arg5,
        float arg6,
        float arg7,
        int arg8,
        bool arg9,
        bool arg10,
        bool arg11,
        bool arg12,
        bool arg13)
    {
        try
        {
            Function.Call(
                (Hash)nativeHash,
                arg0,
                arg1,
                arg2,
                arg3,
                arg4,
                arg5,
                arg6,
                arg7,
                arg8,
                arg9,
                arg10,
                arg11,
                arg12,
                arg13);
        }
        catch
        {
        }
    }
}
