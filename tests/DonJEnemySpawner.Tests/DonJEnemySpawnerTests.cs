using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class DonJEnemySpawnerTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private enum UnsignedHashExample : uint
    {
        Value = 0xF1234567U
    }

    [TestMethod]
    public void StableConstants_KeepCurrentMenuAndSpawnBounds()
    {
        Assert.AreEqual("DonJ Custom NPC Placer", GetStaticFieldValue<string>("TrainerTitle"));
        Assert.AreEqual("Placement propre pour NPC, vehicules et objets", GetStaticFieldValue<string>("TrainerSubtitle"));
        Assert.AreEqual(121, Convert.ToInt32(GetStaticFieldValue<object>("MenuToggleKey"), CultureInfo.InvariantCulture));
        Assert.AreEqual("F10", GetStaticFieldValue<string>("MenuToggleKeyLabel"));
        Assert.AreEqual("DonJEnemySpawnerSaves", GetStaticFieldValue<string>("SaveFolderName"));
        Assert.AreEqual("_last_save.txt", GetStaticFieldValue<string>("LastSaveFileMarkerName"));
        Assert.AreEqual("DONJ_ENEMY_SPAWNER_SAVE_DIR", GetStaticFieldValue<string>("SaveDirectoryEnvironmentVariable"));
        Assert.AreEqual(@"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced", GetStaticFieldValue<string>("DefaultEnhancedGtaRoot"));
        Assert.AreEqual(96, GetStaticFieldValue<int>("MaxSaveFileNameLength"));
        Assert.AreEqual(1, GetStaticFieldValue<int>("MinHealth"));
        Assert.AreEqual(5000, GetStaticFieldValue<int>("MaxHealth"));
        Assert.AreEqual(0, GetStaticFieldValue<int>("MinArmor"));
        Assert.AreEqual(200, GetStaticFieldValue<int>("MaxArmor"));
        Assert.AreEqual(25, GetStaticFieldValue<int>("MinDistance"));
        Assert.AreEqual(2500, GetStaticFieldValue<int>("MaxDistance"));
        Assert.AreEqual(25, GetStaticFieldValue<int>("DistanceStep"));
        Assert.AreEqual(9, GetStaticFieldValue<int>("MenuItemCount"));
        Assert.AreEqual(0, GetStaticFieldValue<int>("RelationshipCompanion"));
        Assert.AreEqual(3, GetStaticFieldValue<int>("RelationshipNeutral"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("RelationshipDislike"));
        Assert.AreEqual(5, GetStaticFieldValue<int>("RelationshipHate"));
        Assert.AreEqual(700, GetStaticFieldValue<int>("ThinkIntervalMs"));
        Assert.AreEqual(750, GetStaticFieldValue<int>("NpcThinkJitterMs"));
        Assert.AreEqual(6, GetStaticFieldValue<int>("MaxNpcBrainsPerTick"));
        Assert.AreEqual(2400, GetStaticFieldValue<int>("PassiveHoldRefreshMs"));
        Assert.AreEqual(900, GetStaticFieldValue<int>("PassiveHoldJitterMs"));
        Assert.AreEqual(1200, GetStaticFieldValue<int>("NpcBlipRefreshIntervalMs"));
        Assert.AreEqual(700, GetStaticFieldValue<int>("NpcBlipRefreshJitterMs"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("MaxNpcBlipRefreshPerTick"));
        Assert.AreEqual(260, GetStaticFieldValue<int>("AllyThreatScanIntervalMs"));
        Assert.AreEqual(950, GetStaticFieldValue<int>("AllyThreatCacheLifetimeMs"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("AllyThreatGuardScansPerPass"));
        Assert.AreEqual(2500.0f, GetStaticFieldValue<float>("StaticSightDistance"), 0.001f);
        Assert.AreEqual(3000.0f, GetStaticFieldValue<float>("AttackRefreshDistance"), 0.001f);
        Assert.AreEqual(95.0f, GetStaticFieldValue<float>("NeutralAssistRadius"), 0.001f);
        Assert.AreEqual(140.0f, GetStaticFieldValue<float>("NeutralWitnessSightDistance"), 0.001f);
        Assert.AreEqual(55.0f, GetStaticFieldValue<float>("NeutralShootingReactionDistance"), 0.001f);
        Assert.AreEqual(150.0f, GetStaticFieldValue<float>("AllyDefenseRadius"), 0.001f);
        Assert.AreEqual(180.0f, GetStaticFieldValue<float>("AllySightDistance"), 0.001f);
        Assert.AreEqual(45.0f, GetStaticFieldValue<float>("AllyShootingThreatDistance"), 0.001f);
        Assert.AreEqual(170, GetStaticFieldValue<int>("PlacementPreviewAlpha"));
        Assert.AreEqual(350, GetStaticFieldValue<int>("PlacementSpawnCooldownMs"));
        Assert.AreEqual(650, GetStaticFieldValue<int>("PreviewRetryIntervalMs"));
        Assert.AreEqual(0x58A850EAEE20FAA3UL, GetStaticFieldValue<ulong>("PlaceEntityOnGroundProperlyNative"));
    }

    [TestMethod]
    public void ExpandedConstants_KeepPlacementMenuAndPatrolBounds()
    {
        Assert.AreEqual(24, GetStaticFieldValue<int>("MainMenuItemCount"));
        Assert.AreEqual(24, GetStaticFieldValue<int>("MainMenuVisibleRowLimit"));
        Assert.AreEqual(16, GetStaticFieldValue<int>("MainMenuCompactVisibleRowLimit"));
        Assert.AreEqual(1000, GetStaticFieldValue<int>("AutoRespawnCheckIntervalMs"));
        Assert.AreEqual(6000, GetStaticFieldValue<int>("AutoRespawnMinDelayMs"));
        Assert.AreEqual(15000, GetStaticFieldValue<int>("AutoRespawnRetryDelayMs"));
        Assert.AreEqual(3, GetStaticFieldValue<int>("AutoRespawnMaxPerTick"));
        Assert.AreEqual(220.0f, GetStaticFieldValue<float>("AutoRespawnLeaveDistance"), 0.001f);
        Assert.AreEqual(70.0f, GetStaticFieldValue<float>("AutoRespawnNearSafetyDistance"), 0.001f);
        Assert.AreEqual(12, GetStaticFieldValue<int>("WeaponEditorItemCount"));
        Assert.AreEqual(5, GetStaticFieldValue<int>("MinPatrolRadius"));
        Assert.AreEqual(500, GetStaticFieldValue<int>("MaxPatrolRadius"));
        Assert.AreEqual(5, GetStaticFieldValue<int>("PatrolRadiusStep"));
        Assert.AreEqual(3000.0f, GetStaticFieldValue<float>("CombatRefreshDistance"), 0.001f);
        Assert.AreEqual(105.0f, GetStaticFieldValue<float>("RuntimeNeutralAssistRadius"), 0.001f);
        Assert.AreEqual(165.0f, GetStaticFieldValue<float>("RuntimeAllyDefenseRadius"), 0.001f);
        Assert.AreEqual(2000, GetStaticFieldValue<int>("TerminatorMinHealth"));
        Assert.AreEqual(200, GetStaticFieldValue<int>("TerminatorArmor"));
        Assert.AreEqual(155, GetStaticFieldValue<int>("TerminatorArmorRefreshThreshold"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("TerminatorFirstPersonViewMode"));
        Assert.AreEqual(90, GetStaticFieldValue<int>("TerminatorFocusRefreshIntervalMs"));
        Assert.AreEqual(260, GetStaticFieldValue<int>("TerminatorFocusMemoryMs"));
        Assert.AreEqual(650, GetStaticFieldValue<int>("TerminatorPushCooldownMs"));
        Assert.AreEqual(650, GetStaticFieldValue<int>("TerminatorVisionFilterRefreshMs"));
        Assert.AreEqual(420, GetStaticFieldValue<int>("TerminatorDamageFlagCleanupIntervalMs"));
        Assert.AreEqual(360, GetStaticFieldValue<int>("TerminatorWeaponFireImpactBlockMs"));
        Assert.AreEqual(4800, GetStaticFieldValue<int>("TerminatorHealthRegenDelayAfterDamageMs"));
        Assert.AreEqual(1150, GetStaticFieldValue<int>("TerminatorHealthRegenIntervalMs"));
        Assert.AreEqual(18, GetStaticFieldValue<int>("TerminatorHealthRegenAmount"));
        Assert.AreEqual(1850, GetStaticFieldValue<int>("TerminatorArmorRegenDelayAfterDamageMs"));
        Assert.AreEqual(760, GetStaticFieldValue<int>("TerminatorArmorRegenIntervalMs"));
        Assert.AreEqual(14, GetStaticFieldValue<int>("TerminatorArmorRegenAmount"));
        Assert.AreEqual(-1, GetStaticFieldValue<int>("TerminatorVisionModeNone"));
        Assert.AreEqual(0, GetStaticFieldValue<int>("TerminatorVisionModeRed"));
        Assert.AreEqual(1, GetStaticFieldValue<int>("TerminatorVisionModeNight"));
        Assert.AreEqual(2, GetStaticFieldValue<int>("TerminatorVisionModeThermal"));
        Assert.AreEqual(3, GetStaticFieldValue<int>("TerminatorVisionModeCount"));
        Assert.AreEqual(90.0f, GetStaticFieldValue<float>("TerminatorFocusRadius"), 0.001f);
        Assert.AreEqual(2.15f, GetStaticFieldValue<float>("TerminatorPedImpactRadius"), 0.001f);
        Assert.AreEqual(2.95f, GetStaticFieldValue<float>("TerminatorVehicleImpactRadius"), 0.001f);
        Assert.AreEqual(12.8f, GetStaticFieldValue<float>("TerminatorPedThrowSpeed"), 0.001f);
        Assert.AreEqual(4.85f, GetStaticFieldValue<float>("TerminatorVehiclePushSpeed"), 0.001f);
        Assert.AreEqual(0x18F621F7A5B1F85DUL, GetStaticFieldValue<ulong>("NativeSetNightvision"));
        Assert.AreEqual(0x7E08924259E08CE0UL, GetStaticFieldValue<ulong>("NativeSetSeethrough"));
    }

    [TestMethod]
    public void MainMenuVisibleRowCount_ClampsDynamicMenuRows()
    {
        Assert.AreEqual(1, (int)InvokeStatic("GetMainMenuVisibleRowCount", 0));
        Assert.AreEqual(12, (int)InvokeStatic("GetMainMenuVisibleRowCount", 12));
        Assert.AreEqual(24, (int)InvokeStatic("GetMainMenuVisibleRowCount", 24));
        Assert.AreEqual(24, (int)InvokeStatic("GetMainMenuVisibleRowCount", 40));

        Assert.AreEqual(1, (int)InvokeStatic("GetMainMenuCompactVisibleRowCount", 0));
        Assert.AreEqual(12, (int)InvokeStatic("GetMainMenuCompactVisibleRowCount", 12));
        Assert.AreEqual(16, (int)InvokeStatic("GetMainMenuCompactVisibleRowCount", 24));
        Assert.AreEqual(16, (int)InvokeStatic("GetMainMenuCompactVisibleRowCount", 40));
    }

    [TestMethod]
    public void CartelConstants_KeepPhoneContactContract()
    {
        Assert.AreEqual("Cartel", GetStaticFieldValue<string>("CartelContactName"));
        Assert.AreEqual(11, GetStaticFieldValue<int>("CartelGuardCount"));
        Assert.AreEqual(3, GetStaticFieldValue<int>("CartelVehicleCount"));
        Assert.AreEqual(500, GetStaticFieldValue<int>("CartelGuardHealth"));
        Assert.AreEqual(200, GetStaticFieldValue<int>("CartelGuardArmor"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("CartelCallCooldownMs"));
        Assert.AreEqual(700, GetStaticFieldValue<int>("CartelThinkIntervalMs"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("CartelVehicleOrderIntervalMs"));
        Assert.AreEqual(2200, GetStaticFieldValue<int>("CartelDismissOrderIntervalMs"));
        Assert.AreEqual(6500, GetStaticFieldValue<int>("CartelStuckTimeoutMs"));
        Assert.AreEqual(6500, GetStaticFieldValue<int>("CartelRescueCooldownMs"));
        Assert.AreEqual(5500, GetStaticFieldValue<int>("CartelGuardRescueCooldownMs"));
        Assert.AreEqual(2200, GetStaticFieldValue<int>("CartelDismissMinLifeMs"));
        Assert.AreEqual(18000, GetStaticFieldValue<int>("CartelDismissForceCleanupMs"));
        Assert.AreEqual(68.0f, GetStaticFieldValue<float>("CartelSpawnMinDistance"), 0.001f);
        Assert.AreEqual(118.0f, GetStaticFieldValue<float>("CartelSpawnMaxDistance"), 0.001f);
        Assert.AreEqual(68.0f, GetStaticFieldValue<float>("CartelRelocationMinDistance"), 0.001f);
        Assert.AreEqual(118.0f, GetStaticFieldValue<float>("CartelRelocationMaxDistance"), 0.001f);
        Assert.AreEqual(38.0f, GetStaticFieldValue<float>("CartelArrivalDriveSpeed"), 0.001f);
        Assert.AreEqual(34.0f, GetStaticFieldValue<float>("CartelRetreatDriveSpeed"), 0.001f);
        Assert.AreEqual(185.0f, GetStaticFieldValue<float>("CartelTooFarVehicleDistance"), 0.001f);
        Assert.AreEqual(285.0f, GetStaticFieldValue<float>("CartelCriticalVehicleDistance"), 0.001f);
        Assert.AreEqual(165.0f, GetStaticFieldValue<float>("CartelTooFarGuardDistance"), 0.001f);
        Assert.AreEqual(28.0f, GetStaticFieldValue<float>("CartelDismissDeleteDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<int>("ProfessionalDrivingStyle"), GetStaticFieldValue<int>("CartelRapidDrivingStyle"));
        Assert.AreEqual(0x2AFE52F782F25775UL, GetStaticFieldValue<ulong>("NativeIsPedRunningMobilePhoneTask"));
    }

    [TestMethod]
    public void EnemyRaidConstants_KeepPhoneRaidContract()
    {
        Assert.AreEqual("Ballas", GetStaticFieldValue<string>("EnemyRaidContactName"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("EnemyRaidMinMembers"));
        Assert.AreEqual(12, GetStaticFieldValue<int>("EnemyRaidMaxMembers"));
        Assert.AreEqual(36, GetStaticFieldValue<int>("EnemyRaidMaxActiveMembers"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("EnemyRaidMaxVehicleCount"));
        Assert.AreEqual(100, GetStaticFieldValue<int>("EnemyRaidHealth"));
        Assert.AreEqual(100, GetStaticFieldValue<int>("EnemyRaidArmor"));
        Assert.AreEqual(2500, GetStaticFieldValue<int>("EnemyRaidCallCooldownMs"));
        Assert.AreEqual(450, GetStaticFieldValue<int>("EnemyRaidThinkIntervalMs"));
        Assert.AreEqual(850, GetStaticFieldValue<int>("EnemyRaidPedOrderIntervalMs"));
        Assert.AreEqual(1300, GetStaticFieldValue<int>("EnemyRaidVehicleOrderIntervalMs"));
        Assert.AreEqual(7000, GetStaticFieldValue<int>("EnemyRaidStuckTimeoutMs"));
        Assert.AreEqual(10000, GetStaticFieldValue<int>("EnemyRaidVehicleRescueCooldownMs"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("EnemyRaidPostCombatVehicleCleanupGraceMs"));
        Assert.AreEqual(45000, GetStaticFieldValue<int>("EnemyRaidVisibleVehicleCleanupMaxMs"));
        Assert.AreEqual(72.0f, GetStaticFieldValue<float>("EnemyRaidSpawnMinDistance"), 0.001f);
        Assert.AreEqual(130.0f, GetStaticFieldValue<float>("EnemyRaidSpawnMaxDistance"), 0.001f);
        Assert.AreEqual(82.0f, GetStaticFieldValue<float>("EnemyRaidRelocationMinDistance"), 0.001f);
        Assert.AreEqual(135.0f, GetStaticFieldValue<float>("EnemyRaidRelocationMaxDistance"), 0.001f);
        Assert.AreEqual(36.0f, GetStaticFieldValue<float>("EnemyRaidArrivalDriveSpeed"), 0.001f);
        Assert.AreEqual(105.0f, GetStaticFieldValue<float>("EnemyRaidDriveByDistance"), 0.001f);
        Assert.AreEqual(42.0f, GetStaticFieldValue<float>("EnemyRaidExitVehicleDistance"), 0.001f);
        Assert.AreEqual(18.0f, GetStaticFieldValue<float>("EnemyRaidForcedExitVehicleDistance"), 0.001f);
        Assert.AreEqual(125.0f, GetStaticFieldValue<float>("EnemyRaidOnFootShootDistance"), 0.001f);
        Assert.AreEqual(230.0f, GetStaticFieldValue<float>("EnemyRaidTooFarVehicleDistance"), 0.001f);
        Assert.AreEqual(135.0f, GetStaticFieldValue<float>("EnemyRaidPostCombatVehicleCleanupDistance"), 0.001f);
        Assert.AreEqual(260.0f, GetStaticFieldValue<float>("EnemyRaidPostCombatVehicleForceCleanupDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<int>("ProfessionalDrivingStyle"), GetStaticFieldValue<int>("EnemyRaidDrivingStyle"));
        Assert.AreEqual(unchecked((int)0xC6EE6B4C), GetStaticFieldValue<int>("EnemyRaidFullAutoFiringPattern"));

        CollectionAssert.AreEqual(
            new[] { "g_m_y_ballaeast_01", "g_m_y_ballaorig_01", "g_m_y_ballasout_01" },
            GetStaticFieldValue<string[]>("EnemyRaidPedModelNames"));
        CollectionAssert.AreEqual(
            new[] { "buccaneer", "chino", "faction", "moonbeam", "primo", "manana" },
            GetStaticFieldValue<string[]>("EnemyRaidVehicleModelNames"));
    }

    [TestMethod]
    public void HighSecurityEscortConstants_KeepPhoneEscortContract()
    {
        Assert.AreEqual("Escorte haute sécurité", GetStaticFieldValue<string>("HighSecurityEscortContactName"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("HighSecurityEscortBallerCount"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("HighSecurityEscortBallerOccupantCount"));
        Assert.AreEqual(4, GetStaticFieldValue<int>("HighSecurityEscortLimousineGuardCount"));
        Assert.AreEqual(500, GetStaticFieldValue<int>("HighSecurityEscortGuardHealth"));
        Assert.AreEqual(200, GetStaticFieldValue<int>("HighSecurityEscortGuardArmor"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("HighSecurityEscortCallCooldownMs"));
        Assert.AreEqual(550, GetStaticFieldValue<int>("HighSecurityEscortThinkIntervalMs"));
        Assert.AreEqual(1650, GetStaticFieldValue<int>("HighSecurityEscortVehicleOrderIntervalMs"));
        Assert.AreEqual(850, GetStaticFieldValue<int>("HighSecurityEscortPedOrderIntervalMs"));
        Assert.AreEqual(1500, GetStaticFieldValue<int>("HighSecurityEscortDismissOrderIntervalMs"));
        Assert.AreEqual(22000, GetStaticFieldValue<int>("HighSecurityEscortDismissForceCleanupMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelCombatOrderIntervalMs"), GetStaticFieldValue<int>("HighSecurityEscortCombatOrderIntervalMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelThreatScanIntervalMs"), GetStaticFieldValue<int>("HighSecurityEscortThreatScanIntervalMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelThreatCacheLifetimeMs"), GetStaticFieldValue<int>("HighSecurityEscortThreatCacheLifetimeMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelMaxGuardThreatScansPerPass"), GetStaticFieldValue<int>("HighSecurityEscortMaxGuardThreatScansPerPass"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelThreatRelationshipRefreshMs"), GetStaticFieldValue<int>("HighSecurityEscortThreatRelationshipRefreshMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelGuardPassiveMaintenanceIntervalMs"), GetStaticFieldValue<int>("HighSecurityEscortGuardPassiveMaintenanceIntervalMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelGuardPassiveMaintenanceJitterMs"), GetStaticFieldValue<int>("HighSecurityEscortGuardPassiveMaintenanceJitterMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelGuardMobilityOrderIntervalMs"), GetStaticFieldValue<int>("HighSecurityEscortGuardMobilityOrderIntervalMs"));
        Assert.AreEqual(GetStaticFieldValue<int>("CartelGuardFootFollowIntervalMs"), GetStaticFieldValue<int>("HighSecurityEscortGuardFootFollowIntervalMs"));
        Assert.AreEqual(72.0f, GetStaticFieldValue<float>("HighSecurityEscortSpawnMinDistance"), 0.001f);
        Assert.AreEqual(128.0f, GetStaticFieldValue<float>("HighSecurityEscortSpawnMaxDistance"), 0.001f);
        Assert.AreEqual(24.0f, GetStaticFieldValue<float>("HighSecurityEscortArrivalDriveSpeed"), 0.001f);
        Assert.AreEqual(21.5f, GetStaticFieldValue<float>("HighSecurityEscortConvoyDriveSpeed"), 0.001f);
        Assert.AreEqual(8.5f, GetStaticFieldValue<float>("HighSecurityEscortConvoyCloseDriveSpeed"), 0.001f);
        Assert.AreEqual(28.0f, GetStaticFieldValue<float>("HighSecurityEscortFormationCatchupSpeed"), 0.001f);
        Assert.AreEqual(13.5f, GetStaticFieldValue<float>("HighSecurityEscortConvoyLineSpawnSpacing"), 0.001f);
        Assert.AreEqual(7.5f, GetStaticFieldValue<float>("HighSecurityEscortArrivalLimoRoadStopDistance"), 0.001f);
        Assert.AreEqual(13.0f, GetStaticFieldValue<float>("HighSecurityEscortArrivalConvoySpacing"), 0.001f);
        Assert.AreEqual(25.5f, GetStaticFieldValue<float>("HighSecurityEscortRushRouteSpeed"), 0.001f);
        Assert.AreEqual(31.0f, GetStaticFieldValue<float>("HighSecurityEscortRushFormationCatchupSpeed"), 0.001f);
        Assert.AreEqual(11.0f, GetStaticFieldValue<float>("HighSecurityEscortRushCloseSpeed"), 0.001f);
        Assert.AreEqual(10.5f, GetStaticFieldValue<float>("HighSecurityEscortDestinationArriveDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelVehicleFootExitDistance"), GetStaticFieldValue<float>("HighSecurityEscortFootExitDistance"), 0.001f);
        Assert.AreEqual(72.0f, GetStaticFieldValue<float>("HighSecurityEscortVehicleApproachDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelGuardFootFollowDistance"), GetStaticFieldValue<float>("HighSecurityEscortGuardFootFollowDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelGuardFootStandDistance"), GetStaticFieldValue<float>("HighSecurityEscortGuardFootStandDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelThreatScanRadius"), GetStaticFieldValue<float>("HighSecurityEscortThreatScanRadius"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelThreatEvidenceRadius"), GetStaticFieldValue<float>("HighSecurityEscortThreatEvidenceRadius"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelDriveByDistance"), GetStaticFieldValue<float>("HighSecurityEscortDriveByDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelPassengerExitCombatDistance"), GetStaticFieldValue<float>("HighSecurityEscortPassengerExitCombatDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<float>("CartelOnFootShootDistance"), GetStaticFieldValue<float>("HighSecurityEscortOnFootShootDistance"), 0.001f);
        Assert.AreEqual(235.0f, GetStaticFieldValue<float>("HighSecurityEscortVehicleTooFarDistance"), 0.001f);
        Assert.AreEqual(46.0f, GetStaticFieldValue<float>("HighSecurityEscortDismissDeleteDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<int>("ProfessionalDrivingStyle"), GetStaticFieldValue<int>("HighSecurityEscortDrivingStyle"));
        Assert.AreEqual(GetStaticFieldValue<int>("ProfessionalDrivingStyle"), GetStaticFieldValue<int>("HighSecurityEscortCalmTaxiDrivingStyle"));
        Assert.AreEqual(786469, GetStaticFieldValue<int>("HighSecurityEscortFastTaxiDrivingStyle"));
        Assert.AreEqual(2883621, GetStaticFieldValue<int>("HighSecurityEscortCombatDrivingStyle"));
        Assert.AreEqual(6500, GetStaticFieldValue<int>("HighSecurityEscortCombatMemoryMs"));
        Assert.AreEqual(11500, GetStaticFieldValue<int>("HighSecurityEscortGuardCombatFootLockMs"));
        Assert.AreEqual(3600, GetStaticFieldValue<int>("HighSecurityEscortSoftUnstuckAfterMs"));
        Assert.AreEqual(5200, GetStaticFieldValue<int>("HighSecurityEscortSoftUnstuckCooldownMs"));
        Assert.AreEqual(19000, GetStaticFieldValue<int>("HighSecurityEscortHardRescueAfterMs"));
        Assert.AreEqual(1250, GetStaticFieldValue<int>("HighSecurityEscortSoftReverseMs"));
        Assert.AreEqual(24.0f, GetStaticFieldValue<float>("HighSecurityEscortCombatRouteSpeed"), 0.001f);
        Assert.AreEqual(27.0f, GetStaticFieldValue<float>("HighSecurityEscortCombatFormationCatchupSpeed"), 0.001f);
        Assert.AreEqual(10.0f, GetStaticFieldValue<float>("HighSecurityEscortCombatCloseSpeed"), 0.001f);
        Assert.AreEqual(31.0f, GetStaticFieldValue<float>("HighSecurityEscortLimoGuardExitThreatDistance"), 0.001f);
        Assert.AreEqual(52.0f, GetStaticFieldValue<float>("HighSecurityEscortBlockedLimoGuardExitThreatDistance"), 0.001f);
        Assert.AreEqual(8.5f, GetStaticFieldValue<float>("HighSecurityEscortObstacleProbeDistance"), 0.001f);
        Assert.AreEqual(GetStaticFieldValue<int>("CartelFullAutoFiringPattern"), GetStaticFieldValue<int>("HighSecurityEscortFullAutoFiringPattern"));
        Assert.AreEqual(8, GetStaticFieldValue<int>("HighSecurityWaypointBlipSprite"));
        Assert.AreEqual(0x1BEDE233E6CD2A1FUL, GetStaticFieldValue<ulong>("NativeGetFirstBlipInfoId"));
        Assert.AreEqual(0xA6DB27D19ECBB7DAUL, GetStaticFieldValue<ulong>("NativeDoesBlipExist"));
        Assert.AreEqual(0x586AFE3FF72D996EUL, GetStaticFieldValue<ulong>("NativeGetBlipCoords"));
        Assert.AreEqual(0xFE99B66D079CF6BCUL, GetStaticFieldValue<ulong>("NativeDisableControlAction"));
        Assert.AreEqual(0x10AB107B887214D8UL, GetStaticFieldValue<ulong>("NativeTaskVehicleShootAtPed"));
        Assert.AreEqual(0x9C8C6504B5B63D2CUL, GetStaticFieldValue<ulong>("NativeStartVehicleHorn"));

        CollectionAssert.AreEqual(
            new[] { "limo2", "stretch" },
            GetStaticFieldValue<string[]>("HighSecurityEscortLimousineModelNames"));
        CollectionAssert.AreEqual(
            new[] { "baller8", "baller6", "baller5" },
            GetStaticFieldValue<string[]>("HighSecurityEscortBallerModelNames"));
    }

    [TestMethod]
    public void CartelCombatConstants_KeepDedicatedThreatAndFireContract()
    {
        Assert.AreEqual(750, GetStaticFieldValue<int>("CartelCombatOrderIntervalMs"));
        Assert.AreEqual(210.0f, GetStaticFieldValue<float>("CartelThreatScanRadius"), 0.001f);
        Assert.AreEqual(230.0f, GetStaticFieldValue<float>("CartelThreatEvidenceRadius"), 0.001f);
        Assert.AreEqual(135.0f, GetStaticFieldValue<float>("CartelDriveByDistance"), 0.001f);
        Assert.AreEqual(45.0f, GetStaticFieldValue<float>("CartelPassengerExitCombatDistance"), 0.001f);
        Assert.AreEqual(145.0f, GetStaticFieldValue<float>("CartelOnFootShootDistance"), 0.001f);
        Assert.AreEqual(unchecked((int)0xC6EE6B4C), GetStaticFieldValue<int>("CartelFullAutoFiringPattern"));
        Assert.AreEqual(1250, GetStaticFieldValue<int>("CartelThreatScanIntervalMs"));
        Assert.AreEqual(1800, GetStaticFieldValue<int>("CartelThreatCacheLifetimeMs"));
        Assert.AreEqual(500, GetStaticFieldValue<int>("CartelLateMaintenanceIntervalMs"));
        Assert.AreEqual(2, GetStaticFieldValue<int>("CartelMaxGuardThreatScansPerPass"));
        Assert.AreEqual(2500, GetStaticFieldValue<int>("CartelThreatRelationshipRefreshMs"));
    }

    [TestMethod]
    public void CartelMobilityConstants_KeepFootVehicleSynchronizationContract()
    {
        Assert.AreEqual(900, GetStaticFieldValue<int>("CartelGuardMobilityOrderIntervalMs"));
        Assert.AreEqual(850, GetStaticFieldValue<int>("CartelGuardFootFollowIntervalMs"));
        Assert.AreEqual(30.0f, GetStaticFieldValue<float>("CartelVehicleFootExitDistance"), 0.001f);
        Assert.AreEqual(5.0f, GetStaticFieldValue<float>("CartelVehicleFootExitSpeed"), 0.001f);
        Assert.AreEqual(125.0f, GetStaticFieldValue<float>("CartelVehicleForcedFootExitMaxDistance"), 0.001f);
        Assert.AreEqual(3.4f, GetStaticFieldValue<float>("CartelGuardFootFollowDistance"), 0.001f);
        Assert.AreEqual(2.4f, GetStaticFieldValue<float>("CartelGuardFootStandDistance"), 0.001f);
        Assert.AreEqual(26.0f, GetStaticFieldValue<float>("CartelGuardImmediateThreatDistance"), 0.001f);
    }

    [TestMethod]
    public void SourceFile_CartelMobilityLayerSyncsFootAndVehicleWithoutHeavyScans()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string maintainBlock = ExtractSourceSection(
            source,
            "private void MaintainCartelTeamWeaponsAndDrivers(Ped player, bool latePass)",
            "private void MaintainCartelGuardPassiveState(SpawnedNpc npc, bool includeWeaponSelection)");
        string mobilityBlock = ExtractSourceSection(
            source,
            "private void SynchronizeCartelGuardWithPlayerState(SpawnedNpc guard, Ped player, bool latePass)",
            "private Ped ResolveCartelThreat(Ped player, bool latePass)");

        Assert.IsTrue(
            source.IndexOf("private readonly Dictionary<int, int> _cartelNextGuardMobilityOrderAt = new Dictionary<int, int>();", StringComparison.Ordinal) >= 0,
            "Le Cartel doit conserver un anti-spam dedie aux ordres pied/vehicule.");
        Assert.IsTrue(
            maintainBlock.IndexOf("SynchronizeCartelGuardWithPlayerState(npc, player, latePass);", StringComparison.Ordinal) >= 0,
            "La maintenance Cartel doit synchroniser les gardes avec l'etat pied/vehicule du joueur.");
        Assert.IsTrue(
            mobilityBlock.IndexOf("ReturnCartelGuardToVehicleIfNeeded(guard, player, false);", StringComparison.Ordinal) >= 0,
            "Un joueur en vehicule doit faire remonter les gardes dans les Baller Cartel.");
        Assert.IsTrue(
            mobilityBlock.IndexOf("CommandCartelGuardLeaveVehicle(guard, currentVehicle, combatMode);", StringComparison.Ordinal) >= 0,
            "Un joueur a pied doit pouvoir faire descendre les gardes Cartel.");
        Assert.IsTrue(
            mobilityBlock.IndexOf("FollowCartelGuardOnFoot(guard, player, false);", StringComparison.Ordinal) >= 0,
            "Les gardes Cartel sortis doivent recevoir un ordre de suivi a pied.");
        Assert.IsTrue(
            mobilityBlock.IndexOf("HasCartelVehicleFailedToApproachFootPlayer(vehicle, player)", StringComparison.Ordinal) >= 0,
            "Un vehicule Cartel bloque doit declencher la sortie a pied.");
        Assert.IsFalse(
            mobilityBlock.IndexOf("World.GetNearbyPeds", StringComparison.Ordinal) >= 0,
            "La couche de mobilite Cartel ne doit pas scanner tous les PNJ du monde.");
        Assert.IsFalse(
            mobilityBlock.IndexOf("FindThreatForAlly(", StringComparison.Ordinal) >= 0,
            "La couche de mobilite Cartel ne doit pas revenir vers la detection lourde Bodyguard.");
    }

    [TestMethod]
    public void SourceFile_CartelCombatPrioritizesFootDeploymentWhenPlayerIsOnFoot()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string guardCombatBlock = ExtractSourceSection(
            source,
            "private void EngageCartelGuardThreat(SpawnedNpc guard, Ped threat, Ped player, bool latePass)",
            "private void PrepareCartelGuardForCombat(SpawnedNpc guard, Ped threat)");
        string passengerExitBlock = ExtractSourceSection(
            source,
            "private bool ShouldCartelPassengerExitToFight(Ped passenger, Vehicle vehicle, Ped threat, Ped player)",
            "private void StartCartelPassengerDriveBy(Ped passenger, Ped threat)");
        string vehicleCombatBlock = ExtractSourceSection(
            source,
            "private void CommandCartelVehicleForCombat(Vehicle vehicle, Ped threat, Ped player)",
            "private void IssueCartelFastFollowOrder(Vehicle vehicle, Ped player, bool force)");

        int forcedExitIndex = guardCombatBlock.IndexOf(
            "ShouldCartelGuardLeaveVehicleForPlayerOnFoot(guard.Ped, currentVehicle, player, true)",
            StringComparison.Ordinal);
        int combatCooldownIndex = guardCombatBlock.IndexOf(
            "if (!CanIssueCartelCombatOrder(guard.Ped))",
            StringComparison.Ordinal);

        Assert.IsTrue(
            forcedExitIndex >= 0 && combatCooldownIndex > forcedExitIndex,
            "La sortie vehicule a pied doit etre evaluee avant le cooldown de combat.");
        Assert.IsTrue(
            guardCombatBlock.IndexOf("ShouldCartelGuardReturnToVehicleDuringCombat(guard, threat, player)", StringComparison.Ordinal) >= 0,
            "Un garde a pied doit pouvoir remonter si le joueur repart en vehicule.");
        Assert.IsTrue(
            passengerExitBlock.IndexOf("return ShouldCartelGuardLeaveVehicleForPlayerOnFoot(passenger, vehicle, player, true);", StringComparison.Ordinal) >= 0,
            "Les passagers Cartel doivent reutiliser la sortie pied/vehicule dediee.");
        Assert.IsFalse(
            passengerExitBlock.IndexOf("distanceToThreat", StringComparison.Ordinal) >= 0,
            "La sortie passager ne doit plus dependre seulement de la menace proche.");
        Assert.IsTrue(
            vehicleCombatBlock.IndexOf("bool playerOnFoot = Entity.Exists(player) && !player.IsInVehicle();", StringComparison.Ordinal) >= 0,
            "Le conducteur Cartel doit distinguer joueur a pied et joueur en vehicule.");
        Assert.IsTrue(
            vehicleCombatBlock.IndexOf("Vector3 driveTarget = playerOnFoot && Entity.Exists(player)", StringComparison.Ordinal) >= 0,
            "En combat, le vehicule Cartel doit viser le joueur si celui-ci est a pied.");
        Assert.IsTrue(
            vehicleCombatBlock.IndexOf("? 16.0f", StringComparison.Ordinal) >= 0,
            "La distance d'arret doit permettre la descente proche du joueur a pied.");
    }

    [TestMethod]
    public void SourceFile_RelationshipRulesProtectAmbientGroupsFromGlobalHate()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string relationshipBlock = ExtractSourceSection(
            source,
            "private void ApplyRelationshipRules()",
            "private void UpdateNpcs()");

        Assert.IsTrue(
            source.IndexOf("private static readonly HashSet<int> ProtectedAmbientRelationshipGroups = BuildProtectedAmbientRelationshipGroups();", StringComparison.Ordinal) >= 0,
            "Le garde-fou des groupes ambiants proteges doit rester present.");
        Assert.IsTrue(
            relationshipBlock.IndexOf("ResetAllyRelationsWithProtectedAmbientGroups();", StringComparison.Ordinal) >= 0,
            "Les relations alliees doivent renettoyer les groupes ambiants proteges.");
        Assert.IsTrue(
            relationshipBlock.IndexOf("SetRelationshipBothWays((Relationship)RelationshipNeutral, _allyGroupHash, protectedGroup);", StringComparison.Ordinal) >= 0,
            "Les groupes ambiants proteges doivent revenir a neutre cote allies.");
        Assert.IsTrue(
            relationshipBlock.IndexOf("\"CIVMALE\"", StringComparison.Ordinal) >= 0 &&
            relationshipBlock.IndexOf("\"FIREMAN\"", StringComparison.Ordinal) >= 0 &&
            relationshipBlock.IndexOf("\"MEDIC\"", StringComparison.Ordinal) >= 0 &&
            relationshipBlock.IndexOf("\"COP\"", StringComparison.Ordinal) >= 0,
            "La liste des groupes ambiants proteges doit couvrir les civils et les services.");
    }

    [TestMethod]
    public void SourceFile_BodyguardsStayScriptControlledWithoutRealThreat()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string bodyguardBlock = ExtractSourceSection(
            source,
            "private void UpdateBodyguard(SpawnedNpc bodyguard, Ped player)",
            "private bool HasPlayerProvokedNeutralGuard(Ped guard, Ped player)");
        string cartelMaintainBlock = ExtractSourceSection(
            source,
            "private void MaintainCartelTeamWeaponsAndDrivers(Ped player, bool latePass)",
            "private Ped ResolveCartelThreat(Ped player, bool latePass)");
        string cartelGuardBlock = ExtractSourceSection(
            source,
            "private void ConfigureCartelGuard(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)",
            "private void UpgradeCartelVehicle(Vehicle vehicle)");

        Assert.IsTrue(
            bodyguardBlock.IndexOf("bodyguard.Ped.BlockPermanentEvents = true;", StringComparison.Ordinal) >= 0,
            "Un bodyguard sans menace doit rester bloque contre les evenements ambiants.");
        Assert.IsTrue(
            bodyguardBlock.IndexOf("Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, bodyguard.Ped.Handle, _allyGroupHash);", StringComparison.Ordinal) >= 0,
            "Un bodyguard sans menace doit revenir explicitement dans le groupe allie.");
        Assert.IsTrue(
            cartelMaintainBlock.IndexOf("npc.Ped.BlockPermanentEvents = true;", StringComparison.Ordinal) >= 0,
            "Les gardes Cartel au repos doivent rester controles par le script.");
        Assert.IsTrue(
            cartelMaintainBlock.IndexOf("Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, npc.Ped.Handle, _allyGroupHash);", StringComparison.Ordinal) >= 0,
            "Les gardes Cartel au repos doivent rester rattaches au groupe allie.");
        Assert.IsTrue(
            cartelGuardBlock.IndexOf("spawned.Ped.BlockPermanentEvents = true;", StringComparison.Ordinal) >= 0,
            "La configuration initiale des gardes Cartel doit bloquer les reactions ambiantes.");
    }

    [TestMethod]
    public void SourceFile_AllyThreatDetectionRequiresPersonalHostilityForAmbientShooters()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string threatBlock = ExtractSourceSection(
            source,
            "private Ped FindThreatForAlly(Ped allyPed, Ped player)",
            "private Ped FindManagedHostileThreatForAlly(Ped allyPed, Ped player)");

        Assert.IsTrue(
            threatBlock.IndexOf("HasDefensiveDamageAgainstProtectedPed(candidate, protectedPed)", StringComparison.Ordinal) >= 0,
            "La detection alliee doit confirmer les degats defensifs avant de reagir.");
        Assert.IsTrue(
            threatBlock.IndexOf("HasHostileRelationshipToProtectedPed(candidate, protectedPed) ||", StringComparison.Ordinal) >= 0,
            "Un tir proche ne doit plus suffire sans hostilite personnelle contre la cible protegee.");
        Assert.IsTrue(
            threatBlock.IndexOf("(Entity.Exists(player) && HasHostileRelationshipToProtectedPed(candidate, player))", StringComparison.Ordinal) >= 0,
            "Un tir proche doit aussi pouvoir proteger le joueur si la relation hostile le vise.");
        Assert.IsFalse(
            threatBlock.IndexOf("player.HasBeenDamagedBy(candidate)", StringComparison.Ordinal) >= 0,
            "La detection alliee ne doit plus s'appuyer directement sur le test large de degat du joueur.");
    }

    [TestMethod]
    public void SourceFile_NpcAiUpdateSpreadsBrainsBlipsAndPassiveOrders()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string updateBlock = ExtractSourceSection(
            source,
            "private void UpdateNpcs()",
            "private void MarkNpcForAutoRespawn(SpawnedNpc npc)");
        string spawnBlock = ExtractSourceSection(
            source,
            "private SpawnedNpc RegisterSpawnedNpc(",
            "private PlacedVehicle RegisterPlacedVehicle");
        string respawnBlock = ExtractSourceSection(
            source,
            "private void ResetNpcRuntimeAfterAutoRespawn(SpawnedNpc npc)",
            "private bool CanAutoRespawnAt(Ped player, Vector3 spawnPosition, Entity oldEntity, int eligibleAt)");

        StringAssert.Contains(updateBlock, "int brainsBudget = MaxNpcBrainsPerTick;");
        StringAssert.Contains(updateBlock, "int blipBudget = MaxNpcBlipRefreshPerTick;");
        StringAssert.Contains(updateBlock, "RefreshNpcBlipIfNeeded(npc, now, ref blipBudget);");
        StringAssert.Contains(updateBlock, "if (brainsBudget <= 0)");
        StringAssert.Contains(updateBlock, "npc.NextThinkAt = GetNextNpcThinkTime();");
        StringAssert.Contains(updateBlock, "CreateOrUpdateNpcBlip(npc);");
        Assert.IsFalse(
            updateBlock.IndexOf("CreateOrUpdateNpcBlip(npc);\r\n\r\n            if (Game.GameTime < npc.NextThinkAt)", StringComparison.Ordinal) >= 0,
            "La boucle PNJ ne doit plus rafraichir les blips et cerveaux en salve synchronisee.");

        StringAssert.Contains(spawnBlock, "NextThinkAt = GetInitialNpcThinkTime(),");
        StringAssert.Contains(spawnBlock, "NextPassiveTaskAt = GetInitialPassiveTaskTime(),");
        StringAssert.Contains(spawnBlock, "NextBlipRefreshAt = GetInitialNpcBlipRefreshTime(),");
        StringAssert.Contains(respawnBlock, "npc.NextThinkAt = GetInitialNpcThinkTime();");
        StringAssert.Contains(respawnBlock, "npc.NextPassiveTaskAt = GetInitialPassiveTaskTime();");
        StringAssert.Contains(respawnBlock, "npc.NextBlipRefreshAt = GetInitialNpcBlipRefreshTime();");

        StringAssert.Contains(source, "private bool ShouldRefreshPassiveTask(SpawnedNpc npc)");
        StringAssert.Contains(source, "HoldStaticPositionThrottled(npc, player);");
        StringAssert.Contains(source, "HoldGuardPositionThrottled(npc);");
        StringAssert.Contains(source, "HoldAllyPositionThrottled(ally);");
    }

    [TestMethod]
    public void SourceFile_AllyThreatScansAreSharedAndCached()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string threatBlock = ExtractSourceSection(
            source,
            "private Ped FindThreatForAlly(Ped allyPed, Ped player)",
            "private Ped FindManagedHostileThreatForAlly(Ped allyPed, Ped player)");

        StringAssert.Contains(source, "private Ped _allyCachedThreatPed;");
        StringAssert.Contains(source, "private int _allyCachedThreatUntil;");
        StringAssert.Contains(source, "private int _nextAllyThreatScanAt;");
        StringAssert.Contains(source, "private int _allyThreatScanCursor;");
        StringAssert.Contains(threatBlock, "CacheAllyThreat(managedThreat);");
        StringAssert.Contains(threatBlock, "Ped cachedThreat = GetUsableCachedAllyThreat(allyPed, player);");
        StringAssert.Contains(threatBlock, "if (now < _nextAllyThreatScanAt)");
        StringAssert.Contains(threatBlock, "_nextAllyThreatScanAt = now + AllyThreatScanIntervalMs;");
        StringAssert.Contains(threatBlock, "Ped ambientThreat = FindBestAmbientThreatForAllies(player);");
        StringAssert.Contains(threatBlock, "Ped[] nearPlayer = GetNearbyPedsSafe(player, RuntimeAllyDefenseRadius);");
        StringAssert.Contains(threatBlock, "int scansThisPass = Math.Min(AllyThreatGuardScansPerPass, allies.Count);");
        StringAssert.Contains(threatBlock, "_allyThreatScanCursor = Wrap(_allyThreatScanCursor + scansThisPass, allies.Count);");
        Assert.IsFalse(
            threatBlock.IndexOf("GetUniqueNearbyPeds(allyPed, player, RuntimeAllyDefenseRadius)", StringComparison.Ordinal) >= 0,
            "La detection alliee ne doit plus lancer deux scans monde par allie.");
    }

    [TestMethod]
    public void SourceFile_CartelThreatEvidenceAndGroupHostilityStayScoped()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string evidenceBlock = ExtractSourceSection(
            source,
            "private bool HasCartelThreatEvidence(Ped candidate, Ped player)",
            "private float ScoreCartelThreat(Ped candidate, Ped player)");
        string allyCombatBlock = ExtractSourceSection(
            source,
            "private void ActivateAllyCombat(SpawnedNpc ally, Ped target)",
            "private void ActivateCombatAgainstBestTarget(SpawnedNpc npc, bool stationary)");
        string cartelHostilityBlock = ExtractSourceSection(
            source,
            "private void MakeCartelAlliesHostileToThreat(Ped threat)",
            "private void EngageCartelGuardThreat(SpawnedNpc guard, Ped threat, Ped player, bool latePass)");

        Assert.IsTrue(
            evidenceBlock.IndexOf("HasDefensiveDamageAgainstProtectedPed(candidate, player)", StringComparison.Ordinal) >= 0,
            "Le Cartel doit confirmer une agression defensive contre le joueur.");
        Assert.IsTrue(
            evidenceBlock.IndexOf("HasDefensiveDamageAgainstProtectedPed(candidate, guard)", StringComparison.Ordinal) >= 0,
            "Le Cartel doit confirmer une agression defensive contre un garde precis.");
        Assert.IsTrue(
            evidenceBlock.IndexOf("HasHostileRelationshipToProtectedPed(candidate, player)", StringComparison.Ordinal) >= 0,
            "Le Cartel doit exiger un indice d'hostilite personnelle avant de retenir un simple tir proche.");
        Assert.IsFalse(
            evidenceBlock.IndexOf("candidate.HasBeenDamagedBy(player)", StringComparison.Ordinal) >= 0,
            "Le Cartel ne doit plus transformer automatiquement une cible touchee par le joueur en menace valide.");
        Assert.IsTrue(
            allyCombatBlock.IndexOf("ShouldUseGroupHostilityForThreat(target, targetGroup)", StringComparison.Ordinal) >= 0,
            "Les allies doivent filtrer la haine de groupe avant de l'appliquer.");
        Assert.IsTrue(
            cartelHostilityBlock.IndexOf("ShouldUseGroupHostilityForThreat(threat, targetGroup)", StringComparison.Ordinal) >= 0,
            "Le Cartel doit filtrer la haine de groupe avant de l'appliquer.");
    }

    [TestMethod]
    public void SourceFile_CartelNoLongerUsesForcedVehicleForwardSpeed()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        Assert.IsFalse(
            source.IndexOf("SET_VEHICLE_FORWARD_SPEED", StringComparison.Ordinal) >= 0,
            "La logique Cartel ne doit plus réintroduire de propulsion scriptée de véhicule.");
    }

    [TestMethod]
    public void SourceFile_CartelVehicleUpgradeUsesOneShotHeavyPass()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string upgradeBlock = ExtractSourceSection(
            source,
            "private void UpgradeCartelVehicle(Vehicle vehicle)",
            "private Vector3 FindCartelVehicleSpawnPosition");

        Assert.IsTrue(
            source.IndexOf("private readonly HashSet<int> _cartelFullyUpgradedVehicleHandles = new HashSet<int>();", StringComparison.Ordinal) >= 0,
            "Le tracker des upgrades lourdes Cartel doit rester présent.");
        Assert.IsTrue(
            source.IndexOf("private readonly Dictionary<int, int> _cartelLastVehicleSoftMaintenanceAt = new Dictionary<int, int>();", StringComparison.Ordinal) >= 0,
            "Le tracker de maintenance légère Cartel doit rester présent.");
        Assert.IsTrue(
            source.IndexOf("private readonly Dictionary<int, Vector3> _cartelLastVehicleOrderTarget = new Dictionary<int, Vector3>();", StringComparison.Ordinal) >= 0,
            "Le tracker de dernière cible d'ordre Cartel doit rester présent.");
        Assert.IsTrue(
            upgradeBlock.IndexOf("if (!_cartelFullyUpgradedVehicleHandles.Contains(handle))", StringComparison.Ordinal) >= 0,
            "L'upgrade lourd Cartel doit rester protégé par un passage unique.");
        Assert.IsTrue(
            upgradeBlock.IndexOf("MaintainCartelVehicleSoftState(vehicle);", StringComparison.Ordinal) >= 0,
            "Les appels suivants doivent basculer sur la maintenance légère.");
        Assert.AreEqual(
            1,
            CountOccurrences(upgradeBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "Le bloc d'upgrade Cartel ne doit remettre le véhicule au sol qu'une seule fois.");
    }

    [TestMethod]
    public void SourceFile_CartelSoftMaintenanceAvoidsHeavyVehicleResets()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string maintenanceBlock = ExtractSourceSection(
            source,
            "private void MaintainCartelVehicleSoftState(Vehicle vehicle)",
            "private Vector3 FindCartelVehicleSpawnPosition");

        Assert.IsTrue(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, vehicle.Handle, true, true);", StringComparison.Ordinal) >= 0,
            "La maintenance légère doit conserver l'état mission du véhicule.");
        Assert.IsTrue(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, vehicle.Handle, false);", StringComparison.Ordinal) >= 0,
            "La maintenance légère doit conserver les pneus protégés.");
        Assert.IsTrue(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);", StringComparison.Ordinal) >= 0,
            "La maintenance légère doit garder le moteur actif.");
        Assert.IsTrue(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle.Handle, 1);", StringComparison.Ordinal) >= 0,
            "La maintenance légère doit garder le verrouillage voulu.");
        Assert.IsFalse(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY", StringComparison.Ordinal) >= 0,
            "La maintenance légère ne doit jamais remettre le véhicule au sol.");
        Assert.IsFalse(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_MOD_KIT", StringComparison.Ordinal) >= 0,
            "La maintenance légère ne doit pas réappliquer le kit de mods.");
        Assert.IsFalse(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_MOD,", StringComparison.Ordinal) >= 0,
            "La maintenance légère ne doit pas réappliquer les mods GTA.");
        Assert.IsFalse(
            maintenanceBlock.IndexOf("Function.Call(Hash.SET_ENTITY_VELOCITY", StringComparison.Ordinal) >= 0,
            "La maintenance légère ne doit pas toucher à la vélocité du véhicule.");
    }

    [TestMethod]
    public void SourceFile_CartelFollowOrdersAvoidRedundantTaskSpam()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string followBlock = ExtractSourceSection(
            source,
            "private void IssueCartelFastFollowOrder(Vehicle vehicle, Ped player, bool force)",
            "private float CalculateCartelCruiseSpeed(Ped player)");

        Assert.IsTrue(
            followBlock.IndexOf("IsCartelVehicleSettledNearPlayer(vehicle, player)", StringComparison.Ordinal) >= 0,
            "Les ordres Cartel doivent ignorer les véhicules déjà posés près du joueur.");
        Assert.IsTrue(
            followBlock.IndexOf("_cartelLastVehicleOrderTarget.TryGetValue(handle, out lastTarget)", StringComparison.Ordinal) >= 0,
            "Les ordres Cartel doivent mémoriser la dernière cible envoyée.");
        Assert.IsTrue(
            followBlock.IndexOf("lastTarget.DistanceTo(targetPosition) < 8.0f", StringComparison.Ordinal) >= 0,
            "Les ordres Cartel doivent filtrer les cibles quasi identiques.");
        Assert.IsFalse(
            followBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED", StringComparison.Ordinal) >= 0,
            "Le suivi Cartel ne doit plus forcer de vitesse scriptée.");
        Assert.IsFalse(
            followBlock.IndexOf("Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY", StringComparison.Ordinal) >= 0,
            "Le suivi Cartel ne doit pas remettre le véhicule au sol pendant les ordres.");
    }

    [TestMethod]
    public void SourceFile_UpdateCartelConvoyLateLimitsHeavyMaintenance()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string lateBlock = ExtractSourceSection(
            source,
            "private void UpdateCartelConvoyLate()",
            "private void UpdateCartelPhoneContact(Ped player)");

        Assert.IsTrue(
            lateBlock.IndexOf("Game.GameTime >= _nextCartelLateMaintenanceAt", StringComparison.Ordinal) >= 0,
            "La passe tardive Cartel doit etre cadencee par un cooldown dedie.");
        Assert.IsTrue(
            lateBlock.IndexOf("_nextCartelLateMaintenanceAt = Game.GameTime + CartelLateMaintenanceIntervalMs;", StringComparison.Ordinal) >= 0,
            "La passe tardive Cartel doit memoriser sa prochaine execution.");
        Assert.IsTrue(
            lateBlock.IndexOf("MaintainCartelTeamWeaponsAndDrivers(player, true);", StringComparison.Ordinal) >= 0,
            "La passe tardive Cartel doit conserver l'entretien leger des gardes et conducteurs.");
    }

    [TestMethod]
    public void SourceFile_CartelCombatModePrioritizesThreatOrders()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string maintainBlock = ExtractSourceSection(
            source,
            "private void MaintainCartelTeamWeaponsAndDrivers(Ped player, bool latePass)",
            "private Ped ResolveCartelThreat(Ped player, bool latePass)");

        Assert.IsTrue(
            maintainBlock.IndexOf("Ped cartelThreat = ResolveCartelThreat(player, latePass);", StringComparison.Ordinal) >= 0,
            "Le maintien Cartel doit passer par la resolution de menace optimisee.");
        Assert.IsFalse(
            maintainBlock.IndexOf("FindBestCartelThreat(player)", StringComparison.Ordinal) >= 0,
            "Le maintien Cartel ne doit plus lancer directement le scan lourd de menace.");
        Assert.IsTrue(
            maintainBlock.IndexOf("if (Entity.Exists(cartelThreat))", StringComparison.Ordinal) >= 0,
            "Le maintien Cartel doit basculer en mode combat si une menace existe.");
        Assert.IsTrue(
            maintainBlock.IndexOf("EngageCartelTeamThreat(cartelThreat, player, latePass);", StringComparison.Ordinal) >= 0,
            "Le maintien Cartel doit prioriser la couche combat dediee.");
    }

    [TestMethod]
    public void SourceFile_CartelThreatResolutionCachesAndLimitsHeavyScans()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string resolutionBlock = ExtractSourceSection(
            source,
            "private Ped ResolveCartelThreat(Ped player, bool latePass)",
            "private bool IsValidCartelThreatCandidate(Ped candidate, Ped player)");

        Assert.IsTrue(
            resolutionBlock.IndexOf("Ped cachedThreat = GetCachedCartelThreat(player);", StringComparison.Ordinal) >= 0,
            "La resolution Cartel doit d'abord reutiliser la menace mise en cache.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("if (latePass)", StringComparison.Ordinal) >= 0,
            "La resolution Cartel doit interdire le scan lourd pendant la passe tardive.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("Game.GameTime < _nextCartelThreatScanAt", StringComparison.Ordinal) >= 0,
            "La resolution Cartel doit respecter un intervalle minimum entre scans lourds.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("_nextCartelThreatScanAt = Game.GameTime + CartelThreatScanIntervalMs;", StringComparison.Ordinal) >= 0,
            "La resolution Cartel doit reprogrammer le prochain scan lourd.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("CacheCartelThreat(scannedThreat);", StringComparison.Ordinal) >= 0,
            "La resolution Cartel doit memoriser la menace trouvee.");
        Assert.AreEqual(
            1,
            CountOccurrences(source, "FindBestCartelThreat(player)"),
            "Le scan lourd principal ne doit plus etre appele qu'au travers de ResolveCartelThreat.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("int scansThisPass = Math.Min(CartelMaxGuardThreatScansPerPass, cartelNpcHandles.Count);", StringComparison.Ordinal) >= 0,
            "Le scan des gardes Cartel doit etre limite par passe.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("HasCartelThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player)", StringComparison.Ordinal) >= 0,
            "Le scan Cartel doit reutiliser une verification ciblee pour chaque garde inspecte.");
        Assert.IsTrue(
            resolutionBlock.IndexOf("_cartelGuardThreatScanCursor = Wrap(_cartelGuardThreatScanCursor + scansThisPass, cartelNpcHandles.Count);", StringComparison.Ordinal) >= 0,
            "Le scan Cartel doit repartir progressivement les gardes inspectes.");
    }

    [TestMethod]
    public void SourceFile_CartelCombatModeForcesVehicleAndOnFootFireOrders()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string combatBlock = ExtractSourceSection(
            source,
            "private Ped FindBestCartelThreat(Ped player)",
            "private void IssueCartelFastFollowOrder(Vehicle vehicle, Ped player, bool force)");

        Assert.IsTrue(
            source.IndexOf("private readonly Dictionary<int, int> _cartelNextCombatOrderAt = new Dictionary<int, int>();", StringComparison.Ordinal) >= 0,
            "Le Cartel doit conserver un anti-spam dedie pour les ordres de combat.");
        Assert.IsTrue(
            combatBlock.IndexOf("Hash.TASK_DRIVE_BY", StringComparison.Ordinal) >= 0,
            "Les passagers Cartel doivent utiliser TASK_DRIVE_BY pour forcer le tir vehicule.");
        Assert.IsTrue(
            combatBlock.IndexOf("Hash.TASK_SHOOT_AT_ENTITY", StringComparison.Ordinal) >= 0,
            "Les gardes Cartel a pied doivent pouvoir forcer le tir direct.");
        Assert.IsTrue(
            combatBlock.IndexOf("Hash.SET_PED_FIRING_PATTERN", StringComparison.Ordinal) >= 0,
            "Le mode combat Cartel doit forcer un firing pattern full-auto.");
        Assert.IsTrue(
            combatBlock.IndexOf("World.SetRelationshipBetweenGroups((Relationship)RelationshipHate, _allyGroupHash, targetGroup);", StringComparison.Ordinal) >= 0,
            "Le mode combat Cartel doit verrouiller l'hostilite envers la cible.");
        Assert.IsTrue(
            combatBlock.IndexOf("Game.GameTime - _cartelLastThreatRelationshipAt < CartelThreatRelationshipRefreshMs", StringComparison.Ordinal) >= 0,
            "Le mode combat Cartel doit amortir les refreshs de relation contre la meme cible.");
    }

    [TestMethod]
    public void SourceFile_ActivateAllyCombatRoutesActiveCartelGuardsToDedicatedLayer()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string activateAllyBlock = ExtractSourceSection(
            source,
            "private void ActivateAllyCombat(SpawnedNpc ally, Ped target)",
            "private void ActivateCombatAgainstBestTarget(SpawnedNpc npc, bool stationary)");

        Assert.IsTrue(
            activateAllyBlock.IndexOf("_cartelNpcHandles.Contains(ally.Ped.Handle)", StringComparison.Ordinal) >= 0,
            "ActivateAllyCombat doit reconnaitre les gardes Cartel actifs.");
        Assert.IsTrue(
            activateAllyBlock.IndexOf("EngageCartelGuardThreat(ally, target, player, false);", StringComparison.Ordinal) >= 0,
            "ActivateAllyCombat doit rediriger les gardes Cartel vers la couche combat dediee.");
    }

    [TestMethod]
    public void SourceFile_RegularNpcCombatLetsGameManageWeaponDistance()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string combatBlock = ExtractSourceSection(
            source,
            "private void ActivateCombatAgainstTarget(SpawnedNpc npc, Ped target, bool stationary)",
            "private void StartOrContinuePatrol(SpawnedNpc npc, bool forceNewTarget)");

        StringAssert.Contains(combatBlock, "Hash.TASK_COMBAT_PED");
        Assert.IsFalse(combatBlock.Contains("Hash.TASK_GO_TO_ENTITY"), "Le combat standard ne doit plus forcer une approche avant tir selon l'arme.");
        Assert.IsFalse(source.Contains("ShouldApproachBeforeShooting("), "Le garde-fou d'approche par arme doit rester supprime.");
        Assert.IsFalse(source.Contains("DesiredApproachDistanceForWeapon("), "Les distances custom pistolet/SMG ne doivent plus remplacer l'IA GTA.");
    }

    [TestMethod]
    public void SourceFile_CartelHandleCleanupRemovesCombatTrackersForNpcHandles()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string cleanupBlock = ExtractSourceSection(
            source,
            "private void CleanupCartelHandleSets()",
            "private void SpawnCartelConvoy()");

        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelNextCombatOrderAt.Remove(deadActiveNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer les cooldowns de combat des gardes actifs supprimes.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelLastGuardRescueAt.Remove(deadActiveNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer le suivi de rescue des gardes actifs supprimes.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelNextGuardMobilityOrderAt.Remove(deadActiveNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer les cooldowns de mobilite des gardes actifs supprimes.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelNextCombatOrderAt.Remove(deadDismissingNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer les cooldowns de combat des gardes en repli supprimes.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelLastGuardRescueAt.Remove(deadDismissingNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer le suivi de rescue des gardes en repli supprimes.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelNextGuardMobilityOrderAt.Remove(deadDismissingNpcHandles[i]);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit liberer les cooldowns de mobilite des gardes en repli supprimes.");
    }

    [TestMethod]
    public void SourceFile_CartelDismissalCleanupRemovesMobilityTrackers()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string dismissalBlock = ExtractSourceSection(
            source,
            "private void UpdateCartelDismissal(Ped player, bool latePass)",
            "private void DeleteDismissedVehicleAndOccupants(Vehicle vehicle)");
        string deleteVehicleBlock = ExtractSourceSection(
            source,
            "private void DeleteDismissedVehicleAndOccupants(Vehicle vehicle)",
            "private Vector3 CalculateCartelRetreatPoint(Vector3 playerPosition, Vector3 vehiclePosition)");

        Assert.IsTrue(
            dismissalBlock.IndexOf("_cartelNextGuardMobilityOrderAt.Remove(npcHandlesToDelete[i]);", StringComparison.Ordinal) >= 0,
            "La suppression de gardes Cartel en repli doit nettoyer le cooldown de mobilite.");
        Assert.IsTrue(
            deleteVehicleBlock.IndexOf("_cartelNextGuardMobilityOrderAt.Remove(occupantsToDelete[i]);", StringComparison.Ordinal) >= 0,
            "La suppression des occupants de vehicule Cartel doit nettoyer le cooldown de mobilite.");
    }

    [TestMethod]
    public void SourceFile_CartelHandleCleanupClearsCachedThreatWhenTrackedHandleDies()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string cleanupBlock = ExtractSourceSection(
            source,
            "private void CleanupCartelHandleSets()",
            "private void SpawnCartelConvoy()");

        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelCachedThreatPed != null", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit verifier si la menace mise en cache correspond a un handle supprime.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("ClearCachedCartelThreat();", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit vider la menace mise en cache quand son handle est retire.");
    }

    [TestMethod]
    public void SourceFile_CartelVehicleTrackingCleanupRemovesAntiPulseTrackers()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string cleanupBlock = ExtractSourceSection(
            source,
            "private void ClearCartelVehicleTracking(int handle)",
            "private void TeleportCartelVehicleToRoad(Vehicle vehicle, Ped player, Vector3 point)");

        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelFullyUpgradedVehicleHandles.Remove(handle);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit libérer le tracker d'upgrade lourd.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelLastVehicleSoftMaintenanceAt.Remove(handle);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit libérer le tracker de maintenance légère.");
        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelLastVehicleOrderTarget.Remove(handle);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit libérer le tracker de cible d'ordre.");
    }

    [TestMethod]
    public void SourceFile_CartelVehicleTrackingCleanupPurgesCombatTrackerDefensively()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string cleanupBlock = ExtractSourceSection(
            source,
            "private void ClearCartelVehicleTracking(int handle)",
            "private void TeleportCartelVehicleToRoad(Vehicle vehicle, Ped player, Vector3 point)");

        Assert.IsTrue(
            cleanupBlock.IndexOf("_cartelNextCombatOrderAt.Remove(handle);", StringComparison.Ordinal) >= 0,
            "Le nettoyage Cartel doit aussi purger defensivement le tracker d'ordres de combat.");
    }

    [TestMethod]
    public void SourceFile_CartelGroundingCallsStayLimitedToPlacementUpgradeAndRescueTeleport()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string placementVehicleBlock = ExtractSourceSection(
            source,
            "private void ConfigurePlacedVehicleEntity(Vehicle vehicle, float heading)",
            "private void ConfigurePlacedObjectEntity(Prop prop, Vector3 position, float heading)");
        string cartelUpgradeBlock = ExtractSourceSection(
            source,
            "private void UpgradeCartelVehicle(Vehicle vehicle)",
            "private void MaintainCartelVehicleSoftState(Vehicle vehicle)");
        string cartelRescueBlock = ExtractSourceSection(
            source,
            "private void TeleportCartelVehicleToRoad(Vehicle vehicle, Ped player, Vector3 point)",
            "private void RescueCartelGuardIfNeeded(SpawnedNpc npc, Ped player, int seedIndex)");
        string enemyVehicleConfigureBlock = ExtractSourceSection(
            source,
            "private void ConfigureEnemyRaidVehicle(Vehicle vehicle)",
            "private void ConfigureEnemyRaidVehicleSoftState(Vehicle vehicle)");
        string enemyVehicleRescueBlock = ExtractSourceSection(
            source,
            "private void RescueEnemyRaidVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)",
            "private void InitializeEnemyRaidVehicleTracking(Vehicle vehicle)");

        Assert.AreEqual(
            5,
            CountOccurrences(source, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "Le projet doit limiter SET_VEHICLE_ON_GROUND_PROPERLY au placement initial, au Cartel et aux deux opérations véhicule de la vague ennemie.");
        Assert.AreEqual(
            1,
            CountOccurrences(placementVehicleBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "Le placement véhicule doit garder un seul grounding initial.");
        Assert.AreEqual(
            1,
            CountOccurrences(cartelUpgradeBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "Le Cartel doit garder un seul grounding pendant l'upgrade initial.");
        Assert.AreEqual(
            1,
            CountOccurrences(cartelRescueBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "Le Cartel doit garder un seul grounding pendant la téléportation de secours.");
        Assert.AreEqual(
            1,
            CountOccurrences(enemyVehicleConfigureBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "La vague ennemie doit garder un seul grounding pendant la configuration initiale du véhicule.");
        Assert.AreEqual(
            1,
            CountOccurrences(enemyVehicleRescueBlock, "Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY"),
            "La vague ennemie doit garder un seul grounding pendant la relocalisation de secours.");
    }

    [TestMethod]
    public void SourceFile_PhoneContactKeepsCartelOnCEnemyRaidOnRAndEscortOnL()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string contactBlock = ExtractSourceSection(
            source,
            "private void UpdateCartelPhoneContact(Ped player)",
            "private bool IsPlayerPhoneOpen(Ped player)");
        string overlayBlock = ExtractSourceSection(
            source,
            "private void DrawCartelPhoneContactOverlay()",
            "private void ToggleCartelCall()");

        StringAssert.Contains(contactBlock, "_cartelPhoneKeyLatch = false;");
        StringAssert.Contains(contactBlock, "_enemyRaidPhoneKeyLatch = false;");
        StringAssert.Contains(contactBlock, "_highSecurityEscortPhoneKeyLatch = false;");
        StringAssert.Contains(contactBlock, "bool cPressed = Game.IsKeyPressed(Keys.C);");
        StringAssert.Contains(contactBlock, "ToggleCartelCall();");
        StringAssert.Contains(contactBlock, "bool rPressed = Game.IsKeyPressed(Keys.R);");
        StringAssert.Contains(contactBlock, "CallEnemyRaid();");
        StringAssert.Contains(contactBlock, "bool lPressedNow = Game.IsKeyPressed(Keys.L);");
        StringAssert.Contains(contactBlock, "bool lPressed = lPressedNow;");
        StringAssert.Contains(contactBlock, "ToggleHighSecurityEscortCall();");
        StringAssert.Contains(overlayBlock, "DrawText(\"Contacts téléphone\"");
        StringAssert.Contains(overlayBlock, "DrawText(CartelContactName");
        StringAssert.Contains(overlayBlock, "DrawText(EnemyRaidContactName");
        StringAssert.Contains(overlayBlock, "int liveEnemies = CountLiveEnemyRaidMembers();");
        StringAssert.Contains(overlayBlock, "_nextEnemyRaidCallAllowedAt - Game.GameTime");
        StringAssert.Contains(overlayBlock, "DrawText(HighSecurityEscortContactName");
        StringAssert.Contains(overlayBlock, "DrawText(GetHighSecurityEscortPhoneStatus()");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortUsesPartialDedicatedAiAndCleanup()
    {
        string mainSource = File.ReadAllText(GetSourceFilePath());
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());
        string updateNpcsBlock = ExtractSourceSection(
            mainSource,
            "private void UpdateNpcs()",
            "private void RefreshNpcBlipIfNeeded(SpawnedNpc npc, int now, ref int blipBudget)");

        StringAssert.Contains(mainSource, "UpdateHighSecurityEscortState(player);");
        StringAssert.Contains(mainSource, "ForceDeleteHighSecurityEscortEntitiesAndRecords(true);");
        StringAssert.Contains(updateNpcsBlock, "IsHighSecurityEscortPedHandle(npc.Ped.Handle)");
        StringAssert.Contains(escortSource, "public sealed partial class DonJEnemySpawner : Script");
        StringAssert.Contains(escortSource, "private void ToggleHighSecurityEscortCall()");
        StringAssert.Contains(escortSource, "DismissHighSecurityEscort(true);");
        StringAssert.Contains(escortSource, "private void UpdateHighSecurityEscortState(Ped player)");
        StringAssert.Contains(escortSource, "player.IsDead");
        StringAssert.Contains(escortSource, "DismissHighSecurityEscort(false);");
        StringAssert.Contains(escortSource, "private void AssistPlayerEnterHighSecurityLimousine(Ped player)");
        StringAssert.Contains(escortSource, "Game.IsKeyPressed(Keys.F)");
        StringAssert.Contains(escortSource, "private void HandleHighSecurityEscortRouteValidationInput(Ped player)");
        StringAssert.Contains(escortSource, "Game.IsKeyPressed(Keys.L)");
        StringAssert.Contains(escortSource, "TryGetHighSecurityEscortWaypoint(out destination)");
        StringAssert.Contains(escortSource, "UpdateHighSecurityEscortFootFollow(player);");
        StringAssert.Contains(escortSource, "UpdateHighSecurityEscortPlayerVehicleFollow(player);");
        StringAssert.Contains(escortSource, "ReturnHighSecurityEscortGuardsToVehicles(false);");
        StringAssert.Contains(escortSource, "_highSecurityEscortKnownNpcHandles.Add(handle);");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortMirrorsCartelGuardModelLoadoutAndThreatEvidence()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());
        string modelBlock = ExtractSourceSection(
            escortSource,
            "private ModelIdentity ResolveHighSecurityEscortGuardModelIdentity(int seedIndex)",
            "private WeaponLoadout CreateHighSecurityEscortLoadout()");
        string loadoutBlock = ExtractSourceSection(
            escortSource,
            "private WeaponLoadout CreateHighSecurityEscortLoadout()",
            "private void ConfigureHighSecurityEscortGuard(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)");
        string threatBlock = ExtractSourceSection(
            escortSource,
            "private Ped FindBestHighSecurityEscortThreat(Ped player)",
            "private bool IsValidHighSecurityEscortThreatCandidate(Ped candidate, Ped player)");
        string candidateBlock = ExtractSourceSection(
            escortSource,
            "private bool IsValidHighSecurityEscortThreatCandidate(Ped candidate, Ped player)",
            "private bool HasHighSecurityEscortThreatEvidence(Ped candidate, Ped player)");

        StringAssert.Contains(modelBlock, "return ResolveCartelGuardModelIdentity();");
        StringAssert.Contains(loadoutBlock, "return CreateCartelPrimaryLoadout();");
        StringAssert.Contains(loadoutBlock, "GiveCartelWeapons(ped);");
        StringAssert.Contains(loadoutBlock, "WeaponHash.MachinePistol");
        StringAssert.Contains(loadoutBlock, "WeaponHash.ServiceCarbine");
        Assert.IsFalse(
            escortSource.Contains("private static readonly string[] HighSecurityEscortGuardModelNames"),
            "Le mode L ne doit plus garder de tableau de skins haute securite.");
        StringAssert.Contains(threatBlock, "HasHighSecurityEscortThreatEvidence(candidate, player)");
        StringAssert.Contains(threatBlock, "HasHighSecurityEscortThreatEvidenceAgainstSpecificGuard(candidate, guard.Ped, player)");
        StringAssert.Contains(candidateBlock, "IsManagedAlly(candidate)");
        StringAssert.Contains(candidateBlock, "_highSecurityEscortKnownNpcHandles.Contains(candidate.Handle)");
        StringAssert.Contains(candidateBlock, "_cartelNpcHandles.Contains(candidate.Handle)");
        StringAssert.Contains(candidateBlock, "group == _allyGroupHash");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortCombatOrdersAreDedicatedAndNotOverwritten()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());
        string stateBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortState(Ped player)",
            "private void AssistPlayerEnterHighSecurityLimousine(Ped player)");
        string combatBlock = ExtractSourceSection(
            escortSource,
            "private void EngageHighSecurityEscortThreat(Ped threat, Ped player)",
            "private void UpdateHighSecurityEscortRoute(Ped player)");
        string cabinBlock = ExtractSourceSection(
            escortSource,
            "private void MaintainHighSecurityEscortLimousineCabin(Ped player, bool force)",
            "private void EnsureHighSecurityEscortVehicleHasDriver(Vehicle vehicle, bool limousine)");
        string returnBlock = ExtractSourceSection(
            escortSource,
            "private void ReturnHighSecurityEscortGuardsToVehicles(bool force)",
            "private int FindFreeHighSecurityEscortSeatForGuard(Vehicle vehicle)");

        int engageIndex = stateBlock.IndexOf("EngageHighSecurityEscortThreat(threat, player);", StringComparison.Ordinal);
        int returnIndex = stateBlock.IndexOf("return;", engageIndex, StringComparison.Ordinal);
        int routeModeIndex = stateBlock.IndexOf("bool playerInLimousine = IsPlayerInHighSecurityEscortLimousine(player);", StringComparison.Ordinal);

        Assert.IsTrue(engageIndex >= 0, "L'etat escorte doit appeler la couche combat dediee.");
        Assert.IsTrue(returnIndex > engageIndex && returnIndex < routeModeIndex, "Une menace valide ne doit plus etre ecrasee ensuite par standby/convoi.");
        StringAssert.Contains(combatBlock, "MakeHighSecurityEscortAlliesHostileToThreat(threat);");
        StringAssert.Contains(combatBlock, "CanIssueHighSecurityEscortCombatOrder(guard.Ped)");
        StringAssert.Contains(combatBlock, "MarkHighSecurityEscortCombatActive();");
        StringAssert.Contains(combatBlock, "MarkHighSecurityEscortGuardCombatFootLock(guard.Ped);");
        StringAssert.Contains(combatBlock, "IssueHighSecurityLimousineDriveOrder(limousine, _highSecurityEscortDestination, false, true);");
        StringAssert.Contains(combatBlock, "IssueHighSecurityFormationDriveOrder(vehicle, limousine, role, false, true);");
        StringAssert.Contains(combatBlock, "StartHighSecurityEscortPassengerDriveBy(guard.Ped, threat);");
        StringAssert.Contains(combatBlock, "StartHighSecurityEscortOnFootCombat(guard.Ped, threat);");
        StringAssert.Contains(combatBlock, "Hash.TASK_DRIVE_BY");
        StringAssert.Contains(combatBlock, "Hash.TASK_SHOOT_AT_ENTITY");
        StringAssert.Contains(combatBlock, "EnsureHighSecurityEscortCombatEscapeRoute(player, limousine)");
        StringAssert.Contains(combatBlock, "CommandHighSecurityEscortVehicleForCombat(vehicle, threat, player);");
        Assert.IsFalse(cabinBlock.Contains("IsHighSecurityEscortGuardCombatFootLocked(npc.Ped)"), "Les hommes de la limousine ne doivent plus rester dehors a cause d'un lock combat.");
        StringAssert.Contains(cabinBlock, "ConfigureHighSecurityEscortDriver(npc.Ped, combatActive);");
        StringAssert.Contains(returnBlock, "!force && IsHighSecurityEscortGuardCombatFootLocked(npc.Ped)");
        StringAssert.Contains(returnBlock, "CommandHighSecurityEscortGuardEnterAssignedVehicle(");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortVehicleOrdersUseCachedTaxiFormationAndCleanup()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());
        string convoyBlock = ExtractSourceSection(
            escortSource,
            "private void IssueHighSecurityLimousineDriveOrder(Vehicle limousine, Vector3 target, bool force)",
            "private static Vector3 DirectionFromHeading(float heading)");
        string standbyBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortStandby(Ped player)",
            "private void UpdateHighSecurityEscortPlayerVehicleFollow(Ped player)");
        string rescueBlock = ExtractSourceSection(
            escortSource,
            "private void RescueHighSecurityEscortVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)",
            "private int GetHighSecurityEscortVehicleRole(int vehicleHandle)");
        string cleanupBlock = ExtractSourceSection(
            escortSource,
            "private void ForceDeleteHighSecurityEscortEntitiesAndRecords(bool deleteEntities)",
            "private void RemoveHighSecurityEscortPlacedVehicleRecord(int handle, bool deleteEntity)");
        string roadClearanceBlock = ExtractSourceSection(
            escortSource,
            "private float ScoreHighSecurityEscortRoadClearance(Vector3 position, Vector3 direction)",
            "private static float HeadingFromDirection(Vector3 direction)");

        StringAssert.Contains(convoyBlock, "ShouldSkipHighSecurityEscortRepeatedVehicleOrder(");
        StringAssert.Contains(convoyBlock, "RecordHighSecurityEscortVehicleOrderTarget(limousine, target);");
        StringAssert.Contains(convoyBlock, "ResolveHighSecurityEscortDriveTargetOnRoad(limousine, target, combatMode);");
        StringAssert.Contains(convoyBlock, "IsHighSecurityEscortVehicleOrderCooldownActive(limousine)");
        StringAssert.Contains(convoyBlock, "CalculateHighSecurityEscortTaxiSpeed(limousine, target, combatMode)");
        StringAssert.Contains(convoyBlock, "float backDistance = GetHighSecurityEscortFormationBackDistance(role, combatMode);");
        StringAssert.Contains(convoyBlock, "IsHighSecurityEscortVehicleOrderCooldownActive(vehicle)");
        StringAssert.Contains(convoyBlock, "ResolveHighSecurityEscortCachedFormationTarget(");
        StringAssert.Contains(convoyBlock, "_highSecurityEscortCachedFormationTargets.TryGetValue(handle, out cached)");
        StringAssert.Contains(convoyBlock, "HighSecurityEscortFormationTargetCacheMs");
        StringAssert.Contains(convoyBlock, "HighSecurityEscortFormationTargetCacheReuseDistance");
        StringAssert.Contains(convoyBlock, "ShouldUseHighSecurityEscortDirectFormationCorrection(");
        StringAssert.Contains(convoyBlock, "CalculateHighSecurityEscortFormationDriveSpeed(");
        StringAssert.Contains(convoyBlock, "directCorrection");
        StringAssert.Contains(convoyBlock, "Hash.TASK_VEHICLE_ESCORT");
        StringAssert.Contains(convoyBlock, "combatMode ? 18.0f : 24.0f");
        StringAssert.Contains(convoyBlock, "HighSecurityEscortConvoySoftCatchupGap");
        StringAssert.Contains(convoyBlock, "HighSecurityEscortConvoyHardCatchupGap");
        StringAssert.Contains(convoyBlock, "IsHighSecurityEscortRushModeActive(combatMode)");
        StringAssert.Contains(convoyBlock, "GetHighSecurityEscortFormationSpacing(role, combatMode)");
        StringAssert.Contains(convoyBlock, "GetHighSecurityEscortDrivingStyle(combatMode)");
        StringAssert.Contains(convoyBlock, "IsHighSecurityEscortVehicleInSoftRecovery(vehicle)");
        StringAssert.Contains(escortSource, "HighSecurityEscortMajorRoadSearchAttempts = 14");
        StringAssert.Contains(escortSource, "HighSecurityEscortMajorRoadNodeProbeCount = 2");
        StringAssert.Contains(escortSource, "HighSecurityEscortPickupRoadCacheMs = 6200");
        StringAssert.Contains(escortSource, "HighSecurityEscortPickupRoadMaxCandidateChecks = 28");
        StringAssert.Contains(escortSource, "TryResolveHighSecurityEscortRoadSlot(");
        StringAssert.Contains(escortSource, "ScoreHighSecurityEscortRoadClearance(");
        StringAssert.Contains(escortSource, "ScoreHighSecurityEscortNodeProbe(");
        StringAssert.Contains(escortSource, "EnsureHighSecurityEscortPickupRoadCache(");
        StringAssert.Contains(escortSource, "CacheHighSecurityEscortArrivalTargets(");
        StringAssert.Contains(escortSource, "TryFindHighSecurityEscortPickupRoadLine(");
        StringAssert.Contains(escortSource, "TryEstimateHighSecurityEscortRoadDirectionFast(");
        StringAssert.Contains(escortSource, "_highSecurityEscortPickupRoadCacheUntil = Game.GameTime + HighSecurityEscortPickupRoadCacheMs;");
        StringAssert.Contains(escortSource, "_highSecurityEscortCachedArrivalTargets = new Vector3[5];");
        Assert.IsFalse(roadClearanceBlock.Contains("World.Raycast"), "Le scoring route rapide ne doit plus faire de raycast pendant l'arrivee.");
        Assert.IsFalse(escortSource.Contains("HasHighSecurityEscortStaticObstacle("), "La methode obstacle statique inutilisee doit rester supprimee.");
        Assert.IsFalse(convoyBlock.Contains("right * side"), "Les Baller doivent rester en file taxi et ne plus forcer une formation gauche/droite.");
        Assert.IsFalse(convoyBlock.Contains("targetVehicle.Position + forward * 18.0f"), "Les Baller ne doivent plus viser une position devant la limousine.");
        StringAssert.Contains(standbyBlock, "ContinueHighSecurityVehicleNearPlayer(vehicle, player, HighSecurityEscortArrivalDriveSpeed, 5.0f, false);");
        StringAssert.Contains(standbyBlock, "MaybeAnnounceHighSecurityEscortArrival(player);");
        StringAssert.Contains(rescueBlock, "TrySoftUnstuckHighSecurityEscortVehicle(vehicle, seedIndex)");
        StringAssert.Contains(rescueBlock, "TryRoadRejoinHighSecurityEscortVehicle(vehicle, seedIndex)");
        StringAssert.Contains(rescueBlock, "TryRecoverLostHighSecurityEscortVehicleBehindLimousine(");
        StringAssert.Contains(rescueBlock, "NativeTaskVehicleTempAction");
        StringAssert.Contains(rescueBlock, "HasHighSecurityEscortObstacleAhead(vehicle, HighSecurityEscortObstacleProbeDistance)");
        StringAssert.Contains(rescueBlock, "_highSecurityEscortLastVehicleOrderTarget[handle] = Vector3.Zero;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortLastVehicleOrderTarget.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortGuardCombatFootLockUntil.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortVehicleStuckSinceAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortLastVehicleSoftUnstuckAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortLastVehicleRoadRejoinAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortVehicleRecoveryUntil.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedFormationTargets.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedFormationTargetUntil.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortPickupRoadCacheUntil = 0;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortPickupRoadFailedUntil = 0;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedPickupRoadPoint = Vector3.Zero;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedPickupDirection = Vector3.Zero;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedPickupPlayerPosition = Vector3.Zero;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortCachedArrivalTargets = null;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortNextCombatOrderAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortNextGuardPassiveMaintenanceAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortNextGuardMobilityOrderAt.Clear();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortPickupParked = false;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortRoutePaused = false;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortStopKeyLatch = false;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortEmergencyFleeDestination = Vector3.Zero;");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortEmergencyFleeUntil = 0;");
        StringAssert.Contains(cleanupBlock, "ClearCachedHighSecurityEscortThreat();");
        StringAssert.Contains(cleanupBlock, "_highSecurityEscortLastVehicleOrderTarget.Remove(handle);");
    }

    [TestMethod]
    public void SourceFiles_DoNotContainMojibakeCharacters()
    {
        string mainSource = File.ReadAllText(GetSourceFilePath());
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        Assert.IsFalse(mainSource.Contains("Ã"), "Le fichier principal contient du texte UTF-8 corrompu.");
        Assert.IsFalse(escortSource.Contains("Ã"), "Le fichier escorte contient du texte UTF-8 corrompu.");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortVehiclesAreRuntimeOnlyAndNotSaved()
    {
        string mainSource = File.ReadAllText(GetSourceFilePath());
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        StringAssert.Contains(mainSource, "public bool PersistentInSave = true;");
        StringAssert.Contains(mainSource, "bool persistentInSave = true");
        StringAssert.Contains(mainSource, "PersistentInSave = persistentInSave");
        StringAssert.Contains(mainSource, "if (!placed.PersistentInSave)");

        StringAssert.Contains(
            escortSource,
            "RegisterPlacedVehicle(limousine, limoIdentity, limoSlot.Position, limoSlot.Heading, false, false, false);");

        StringAssert.Contains(
            escortSource,
            "RegisterPlacedVehicle(baller, ballerIdentity, slot.Position, slot.Heading, false, false, false);");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortRequiresLimousineDriverBeforeActivation()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        string spawnBlock = ExtractSourceSection(
            escortSource,
            "private void SpawnHighSecurityEscortConvoy()",
            "private Vehicle CreateHighSecurityEscortVehicle");

        StringAssert.Contains(escortSource, "private bool HasLiveHighSecurityEscortDriver(Vehicle vehicle)");
        StringAssert.Contains(spawnBlock, "SpawnHighSecurityEscortGuardIntoVehicle(limousine, -1, createdGuards)");
        StringAssert.Contains(spawnBlock, "!HasLiveHighSecurityEscortDriver(limousine)");
        StringAssert.Contains(spawnBlock, "_highSecurityEscortActive = true");

        int driverCheckIndex = spawnBlock.IndexOf("!HasLiveHighSecurityEscortDriver(limousine)", StringComparison.Ordinal);
        int activeIndex = spawnBlock.IndexOf("_highSecurityEscortActive = true", StringComparison.Ordinal);

        Assert.IsTrue(
            driverCheckIndex >= 0 && activeIndex > driverCheckIndex,
            "Le chauffeur limousine doit être validé avant l'activation de l'escorte.");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortConsumesLKeyUntilRelease()
    {
        string mainSource = File.ReadAllText(GetSourceFilePath());
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        string phoneBlock = ExtractSourceSection(
            mainSource,
            "private void UpdateCartelPhoneContact(Ped player)",
            "private bool IsPlayerPhoneOpen(Ped player)");

        string routeBlock = ExtractSourceSection(
            escortSource,
            "private void HandleHighSecurityEscortRouteValidationInput(Ped player)",
            "private void HandleHighSecurityEscortRushInput(Ped player)");

        StringAssert.Contains(escortSource, "private bool _highSecurityEscortLCommandConsumedUntilRelease;");
        StringAssert.Contains(phoneBlock, "_highSecurityEscortLCommandConsumedUntilRelease = false;");
        StringAssert.Contains(phoneBlock, "!_highSecurityEscortLCommandConsumedUntilRelease");
        StringAssert.Contains(phoneBlock, "_highSecurityEscortLCommandConsumedUntilRelease = true;");
        StringAssert.Contains(routeBlock, "_highSecurityEscortLCommandConsumedUntilRelease");
        StringAssert.Contains(routeBlock, "_highSecurityEscortLCommandConsumedUntilRelease = true;");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortFreeSeatHelperCanRejectDriverSeat()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        StringAssert.Contains(
            escortSource,
            "private int FindFreeHighSecurityEscortSeatForGuard(Vehicle vehicle, bool allowDriverSeat)");

        StringAssert.Contains(
            escortSource,
            "if (allowDriverSeat && IsSeatFreeSafe(vehicle, -1))");

        StringAssert.Contains(
            escortSource,
            "FindFreeHighSecurityEscortSeatForGuard(limousine, false)");

        StringAssert.Contains(
            escortSource,
            "bool guardWasDriver = npc.BodyguardIsDriver || npc.BodyguardAssignedSeat == -1;");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortDismissesWhenLimousineIsUnavailable()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());

        StringAssert.Contains(escortSource, "private bool _highSecurityEscortLimousineLostAnnounced;");
        StringAssert.Contains(escortSource, "private bool EnsureHighSecurityEscortLimousineAvailable()");
        StringAssert.Contains(escortSource, "private void HandleHighSecurityEscortLimousineUnavailable()");
        StringAssert.Contains(escortSource, "DismissHighSecurityEscort(false);");
        StringAssert.Contains(escortSource, "Escorte haute sécurité : limousine perdue ou détruite, repli du convoi.");
    }

    [TestMethod]
    public void SourceFile_HighSecurityEscortParkedPickupPauseAndCombatEscapeStayBounded()
    {
        string escortSource = File.ReadAllText(GetHighSecurityEscortSourceFilePath());
        string stateBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortState(Ped player)",
            "private void AssistPlayerEnterHighSecurityLimousine(Ped player)");
        string stopInputBlock = ExtractSourceSection(
            escortSource,
            "private void HandleHighSecurityEscortImmediateStopInput(Ped player)",
            "private void ResetHighSecurityEscortVehicleOrderCache()");
        string assistBlock = ExtractSourceSection(
            escortSource,
            "private void AssistPlayerEnterHighSecurityLimousine(Ped player)",
            "private void DisableHighSecurityEscortDefaultVehicleEntryControl()");
        string standbyBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortStandby(Ped player)",
            "private void UpdateHighSecurityEscortPlayerVehicleFollow(Ped player)");
        string footBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortFootFollow(Ped player)",
            "private void UpdateHighSecurityEscortParkedPickupFootSupport(Ped player, Vehicle limousine)");
        string parkedFootBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortParkedPickupFootSupport(Ped player, Vehicle limousine)",
            "private bool ShouldHighSecurityEscortGuardReturnToVehicleWhilePlayerOnFoot(SpawnedNpc npc, Vehicle assignedVehicle, Ped player)");
        string routeBlock = ExtractSourceSection(
            escortSource,
            "private void UpdateHighSecurityEscortRoute(Ped player)",
            "private void OrderHighSecurityConvoyToDestination(bool force)");
        string escapeBlock = ExtractSourceSection(
            escortSource,
            "private bool EnsureHighSecurityEscortCombatEscapeRoute(Ped player, Vehicle limousine)",
            "private void EngageHighSecurityEscortThreat(Ped threat, Ped player)");
        string passengerExitBlock = ExtractSourceSection(
            escortSource,
            "private bool ShouldHighSecurityEscortPassengerExitToFight(Ped passenger, Vehicle vehicle, Ped threat, Ped player)",
            "private bool ShouldHighSecurityEscortGuardLeaveVehicleForPlayerOnFoot(Ped guard, Vehicle vehicle, Ped player, bool combatMode)");
        string leaveBlock = ExtractSourceSection(
            escortSource,
            "private void CommandHighSecurityEscortGuardLeaveVehicle(SpawnedNpc npc, Vehicle vehicle, bool force)",
            "private bool IsHighSecurityEscortVehicleOrderCooldownActive(Vehicle vehicle)");
        string vehicleCombatBlock = ExtractSourceSection(
            escortSource,
            "private void CommandHighSecurityEscortVehicleForCombat(Vehicle vehicle, Ped threat, Ped player)",
            "private bool PrepareHighSecurityEscortConvoyDeparture(Ped player, bool forceReturn)");
        string arrivalBlock = ExtractSourceSection(
            escortSource,
            "private void MaybeAnnounceHighSecurityEscortArrival(Ped player)",
            "private void ReturnHighSecurityEscortGuardsToVehicles(bool force)");
        string stopParkedBlock = ExtractSourceSection(
            escortSource,
            "private void StopHighSecurityEscortParkedLimousine(Vehicle limousine)",
            "private void StopHighSecurityEscortConvoyAtDestination()");
        string rescueBlock = ExtractSourceSection(
            escortSource,
            "private void RescueHighSecurityEscortVehicleIfNeeded(Vehicle vehicle, Ped player, int seedIndex)",
            "private bool TryRecoverLostHighSecurityEscortVehicleBehindLimousine(Vehicle vehicle, Vehicle limousine, Ped player, int role, bool force)");
        string roadRejoinBlock = ExtractSourceSection(
            escortSource,
            "private bool TryRoadRejoinHighSecurityEscortVehicle(Vehicle vehicle, int seedIndex)",
            "private bool TryFindHighSecurityEscortRoadRejoinTarget(Vehicle vehicle, int seedIndex, out Vector3 target)");
        string softUnstuckBlock = ExtractSourceSection(
            escortSource,
            "private bool TrySoftUnstuckHighSecurityEscortVehicle(Vehicle vehicle, int seedIndex)",
            "private bool IsHighSecurityEscortVehicleInSoftRecovery(Vehicle vehicle)");

        StringAssert.Contains(stateBlock, "HandleHighSecurityEscortImmediateStopInput(player);");
        StringAssert.Contains(stopInputBlock, "Game.IsKeyPressed(Keys.E)");
        StringAssert.Contains(stopInputBlock, "_highSecurityEscortRoutePaused = !_highSecurityEscortRoutePaused;");
        StringAssert.Contains(stopInputBlock, "StopHighSecurityEscortConvoyImmediately(true);");
        StringAssert.Contains(stopInputBlock, "ReturnHighSecurityEscortGuardsToVehicles(true);");
        StringAssert.Contains(assistBlock, "if (_highSecurityEscortPickupParked)");
        StringAssert.Contains(assistBlock, "StopHighSecurityEscortParkedLimousine(limousine);");
        StringAssert.Contains(standbyBlock, "_highSecurityEscortPickupParked");
        StringAssert.Contains(standbyBlock, "StopHighSecurityEscortParkedLimousine(limousine);");
        StringAssert.Contains(footBlock, "UpdateHighSecurityEscortParkedPickupFootSupport(player, parkedLimousine);");
        StringAssert.Contains(parkedFootBlock, "ReturnHighSecurityEscortGuardsToVehicles(true);");
        StringAssert.Contains(parkedFootBlock, "FollowHighSecurityEscortGuardOnFoot(npc, player, false);");
        StringAssert.Contains(routeBlock, "_highSecurityEscortRoutePaused && !IsHighSecurityEscortCombatActive()");
        StringAssert.Contains(routeBlock, "PrepareHighSecurityEscortConvoyDeparture(player, false)");
        StringAssert.Contains(routeBlock, "_highSecurityEscortPickupParked = true;");
        StringAssert.Contains(escapeBlock, "TryCalculateHighSecurityEscortEmergencyFleeDestination(limousine, player, out escapeDestination)");
        StringAssert.Contains(escapeBlock, "HighSecurityEscortEmergencyFleeMaxCandidateChecks");
        StringAssert.Contains(escapeBlock, "ScoreHighSecurityEscortRoadClearance(roadPoint, direction)");
        Assert.IsFalse(escapeBlock.Contains("World.Raycast"), "La fuite d'urgence ne doit pas ajouter de raycast lourd.");
        StringAssert.Contains(passengerExitBlock, "if (isLimousine)");
        StringAssert.Contains(passengerExitBlock, "return false;");
        StringAssert.Contains(leaveBlock, "if (isLimousine)");
        StringAssert.Contains(leaveBlock, "return;");
        StringAssert.Contains(vehicleCombatBlock, "_highSecurityEscortPickupParked");
        StringAssert.Contains(vehicleCombatBlock, "StopHighSecurityEscortParkedLimousine(vehicle);");
        StringAssert.Contains(arrivalBlock, "if (!_highSecurityEscortPickupParked)");
        StringAssert.Contains(arrivalBlock, "_highSecurityEscortPickupParked = true;");
        StringAssert.Contains(arrivalBlock, "if (limousine.Speed > 2.6f)");
        StringAssert.Contains(arrivalBlock, "StopHighSecurityEscortParkedLimousine(limousine);");
        Assert.IsTrue(
            arrivalBlock.IndexOf("_highSecurityEscortPickupParked = true;", StringComparison.Ordinal) <
            arrivalBlock.IndexOf("if (limousine.Speed > 2.6f)", StringComparison.Ordinal),
            "La limousine doit passer en pickup gare avant d'attendre sa vitesse nulle.");
        StringAssert.Contains(stopParkedBlock, "HighSecurityEscortParkedPickupStopHoldMs");
        StringAssert.Contains(stopParkedBlock, "ResetHighSecurityEscortParkedLimousineRecoveryTracking(limousine);");
        StringAssert.Contains(stopParkedBlock, "_highSecurityEscortLastVehicleMoveAt[handle] = Game.GameTime;");
        StringAssert.Contains(rescueBlock, "ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle)");
        Assert.IsTrue(
            rescueBlock.IndexOf("ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle)", StringComparison.Ordinal) <
            rescueBlock.IndexOf("TrySoftUnstuckHighSecurityEscortVehicle(vehicle, seedIndex)", StringComparison.Ordinal),
            "La limousine pickup garee doit sortir du deblocage avant les ordres de marche arriere.");
        StringAssert.Contains(roadRejoinBlock, "ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle)");
        StringAssert.Contains(softUnstuckBlock, "ShouldKeepHighSecurityEscortPickupLimousineParked(vehicle)");
    }

    [TestMethod]
    public void SourceFile_EnemyRaidUsesDedicatedAiAndHostileGroup()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string updateNpcsBlock = ExtractSourceSection(
            source,
            "private void UpdateNpcs()",
            "private void RefreshNpcBlipIfNeeded(SpawnedNpc npc, int now, ref int blipBudget)");
        string callBlock = ExtractSourceSection(
            source,
            "private void CallEnemyRaid()",
            "private void SpawnEnemyRaidWave(int memberCount, int originalRequestedCount)");
        string spawnBlock = ExtractSourceSection(
            source,
            "private void SpawnEnemyRaidWave(int memberCount, int originalRequestedCount)",
            "private bool SpawnEnemyRaidFootEnemy(Ped player, WeaponLoadout loadout, int seedIndex)");
        string footSpawnBlock = ExtractSourceSection(
            source,
            "private bool SpawnEnemyRaidFootEnemy(Ped player, WeaponLoadout loadout, int seedIndex)",
            "private void UpdateEnemyRaidState(Ped player)");
        string configurePedBlock = ExtractSourceSection(
            source,
            "private void ConfigureEnemyRaidPed(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)",
            "private void MaintainEnemyRaidPedState(Ped ped)");
        string updateRaidNpcBlock = ExtractSourceSection(
            source,
            "private void UpdateEnemyRaidNpc(SpawnedNpc npc, Ped player)",
            "private void CleanupEnemyRaidHandleSets()");

        int raidBypassIndex = updateNpcsBlock.IndexOf("_enemyRaidKnownNpcHandles.Contains(npc.Ped.Handle)", StringComparison.Ordinal);
        int genericThinkIndex = updateNpcsBlock.IndexOf("if (now < npc.NextThinkAt)", StringComparison.Ordinal);

        Assert.IsTrue(raidBypassIndex >= 0, "UpdateNpcs doit ignorer tous les PNJ connus de vague ennemie.");
        Assert.IsTrue(genericThinkIndex > raidBypassIndex, "Le bypass vague ennemie doit passer avant l'IA générique.");
        StringAssert.Contains(callBlock, "_random.Next(EnemyRaidMinMembers, EnemyRaidMaxMembers + 1)");
        StringAssert.Contains(callBlock, "EnemyRaidMaxActiveMembers");
        StringAssert.Contains(spawnBlock, "RegisterSpawnedNpc(");
        StringAssert.Contains(spawnBlock, "RegisterEnemyRaidNpc(spawned, true);");
        StringAssert.Contains(footSpawnBlock, "RegisterEnemyRaidNpc(spawned, false);");
        StringAssert.Contains(spawnBlock, "NpcBehavior.Attacker");
        StringAssert.Contains(spawnBlock, "EnemyRaidHealth");
        StringAssert.Contains(spawnBlock, "EnemyRaidArmor");
        StringAssert.Contains(spawnBlock, "PutPedIntoVehicleSafe(spawned.Ped, vehicle, seat);");
        StringAssert.Contains(configurePedBlock, "Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, spawned.Ped.Handle, _hostileGroupHash);");
        StringAssert.Contains(configurePedBlock, "TryEnsureEnemyRaidWeapon(spawned.Ped);");
        StringAssert.Contains(configurePedBlock, "ForceRefreshEnemyRaidNpcBlip(spawned, true);");
        StringAssert.Contains(updateRaidNpcBlock, "UpdateEnemyRaidNpcBlipState(npc);");
        StringAssert.Contains(updateRaidNpcBlock, "StartEnemyRaidPassengerDriveBy(npc.Ped, player, false);");
        StringAssert.Contains(updateRaidNpcBlock, "CommandEnemyRaidPedLeaveVehicle(npc, vehicle, true);");
        StringAssert.Contains(updateRaidNpcBlock, "StartEnemyRaidOnFootCombat(npc.Ped, player, false);");
    }

    [TestMethod]
    public void SourceFile_EnemyRaidVehiclesUseRedBallasBlipsAndSmgDriveBy()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string npcBlipBlock = ExtractSourceSection(
            source,
            "private void CreateOrUpdateNpcBlip(SpawnedNpc npc)",
            "private void RemoveNpcBlip(SpawnedNpc npc)");
        string blipBlock = ExtractSourceSection(
            source,
            "private void CreateOrUpdatePlacedVehicleBlip(PlacedVehicle placed)",
            "private void RemovePlacedVehicleBlip(PlacedVehicle placed)");
        string loadoutBlock = ExtractSourceSection(
            source,
            "private WeaponLoadout CreateEnemyRaidLoadout()",
            "private void ConfigureEnemyRaidPed(SpawnedNpc spawned, Vehicle assignedVehicle, int assignedSeat)");
        string driveByBlock = ExtractSourceSection(
            source,
            "private void StartEnemyRaidPassengerDriveBy(Ped passenger, Ped player, bool force)",
            "private void StartEnemyRaidOnFootCombat(Ped enemy, Ped player, bool force)");
        string vehicleOrderBlock = ExtractSourceSection(
            source,
            "private void IssueEnemyRaidVehicleAttackOrder(Vehicle vehicle, Ped player, bool force)",
            "private bool CanIssueEnemyRaidVehicleOrder(Vehicle vehicle, bool force)");

        StringAssert.Contains(npcBlipBlock, "bool isEnemyRaidNpc = pedHandle != 0 && _enemyRaidKnownNpcHandles.Contains(pedHandle);");
        StringAssert.Contains(npcBlipBlock, "npc.Blip.Name = \"Ballas Ennemi\";");
        StringAssert.Contains(npcBlipBlock, "npc.Blip.Scale = 0.82f;");
        StringAssert.Contains(blipBlock, "_enemyRaidVehicleCleanupHandles.Contains(vehicleHandle)");
        StringAssert.Contains(blipBlock, "if (vehicleHandle != 0 && _enemyRaidVehicleHandles.Contains(vehicleHandle))");
        StringAssert.Contains(blipBlock, "placed.Blip.Color = BlipColor.Red;");
        StringAssert.Contains(blipBlock, "placed.Blip.IsFriendly = false;");
        StringAssert.Contains(blipBlock, "placed.Blip.Name = \"Ballas Vehicule\";");
        StringAssert.Contains(blipBlock, "placed.Blip.Color = BlipColor.Blue;");
        StringAssert.Contains(blipBlock, "placed.Blip.IsFriendly = true;");
        StringAssert.Contains(loadoutBlock, "Weapon = WeaponHash.SMG");
        StringAssert.Contains(loadoutBlock, "Ammo = 9999");
        StringAssert.Contains(driveByBlock, "Hash.TASK_DRIVE_BY");
        StringAssert.Contains(driveByBlock, "EnemyRaidDriveByDistance");
        StringAssert.Contains(vehicleOrderBlock, "Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE");
        StringAssert.Contains(vehicleOrderBlock, "EnemyRaidArrivalDriveSpeed");
    }

    [TestMethod]
    public void SourceFile_EnemyRaidCleansAbandonedVehiclesAndDeletesEverythingAfterPlayerDeath()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string updateStateBlock = ExtractSourceSection(
            source,
            "private void UpdateEnemyRaidState(Ped player)",
            "private void UpdateEnemyRaidVehicle(Vehicle vehicle, Ped player, int seedIndex)");
        string cleanupBlock = ExtractSourceSection(
            source,
            "private void CleanupEnemyRaidHandleSets()",
            "private bool DoesEnemyRaidVehicleHaveLiveTrackedOccupant(Vehicle vehicle)");
        string deathBlock = ExtractSourceSection(
            source,
            "private void HandleEnemyRaidPlayerDeath(Ped player)",
            "private void BeginEnemyRaidPostCombatCleanup()");
        string postCombatBlock = ExtractSourceSection(
            source,
            "private void BeginEnemyRaidPostCombatCleanup()",
            "private void RegisterEnemyRaidNpc(SpawnedNpc spawned, bool startsInVehicle)");

        StringAssert.Contains(updateStateBlock, "UpdateEnemyRaidAbandonedVehicles(player);");
        StringAssert.Contains(updateStateBlock, "HandleEnemyRaidPlayerDeath(player);");
        Assert.IsFalse(updateStateBlock.Contains("HandleEnemyRaidPlayerAliveAfterDeath(player);"));
        StringAssert.Contains(cleanupBlock, "CleanupEnemyRaidHandleSets(bool allowPostCombatCleanup)");
        StringAssert.Contains(cleanupBlock, "BeginEnemyRaidPostCombatCleanup();");
        StringAssert.Contains(deathBlock, "ForceDeleteAllEnemyRaidEntitiesAndRecords(true);");
        StringAssert.Contains(deathBlock, "Ballas : attaque annulée après ta mort.");
        Assert.IsFalse(source.Contains("EnemyRaidPlayerDeathRestoreDelayMs"));
        Assert.IsFalse(source.Contains("EnemyRaidRebuildAfterDeathDistance"));
        Assert.IsFalse(source.Contains("HandleEnemyRaidPlayerAliveAfterDeath"));
        Assert.IsFalse(source.Contains("MaintainEnemyRaidEntitiesDuringPlayerDeath"));
        Assert.IsFalse(source.Contains("SpawnEnemyRaidWave(restoreCount"));
        StringAssert.Contains(postCombatBlock, "QueueEnemyRaidVehicleForCleanup(vehicleHandles[i]);");
        StringAssert.Contains(postCombatBlock, "ShouldDeleteEnemyRaidAbandonedVehicle(vehicle, player, handle)");
        StringAssert.Contains(postCombatBlock, "_enemyRaidVehicleCleanupHandles.Add(handle);");
        StringAssert.Contains(postCombatBlock, "RemovePlacedVehicleBlip(placed);");
    }

    [TestMethod]
    public void ResolveCartelGuardModelIdentity_FallsBackToRequestedCartelAssetName()
    {
        object script = CreateScript();

        object identity = InvokeInstance(script, "ResolveCartelGuardModelIdentity");

        Assert.IsTrue(GetFieldValue<bool>(identity, "IsCustom"));
        Assert.AreEqual("g_m_m_cartelgoons_01", GetFieldValue<string>(identity, "Name"));
        Assert.AreEqual("CartelGoons01GMM", GetFieldValue<string>(identity, "DisplayName"));
    }

    [TestMethod]
    public void CartelLoadoutAndVehicleIdentity_UseRequestedDefaults()
    {
        object script = CreateScript();

        object loadout = InvokeInstance(script, "CreateCartelPrimaryLoadout");

        Assert.AreEqual("ServiceCarbine", GetFieldValue<object>(loadout, "Weapon").ToString());
        Assert.AreEqual(9999, GetFieldValue<int>(loadout, "Ammo"));
        Assert.AreEqual("Tactique", GetFieldValue<object>(loadout, "Preset").ToString());
        Assert.IsTrue(GetFieldValue<bool>(loadout, "ExtendedClip"));
        Assert.IsFalse(GetFieldValue<bool>(loadout, "Suppressor"));
        Assert.IsTrue(GetFieldValue<bool>(loadout, "Flashlight"));
        Assert.IsTrue(GetFieldValue<bool>(loadout, "Grip"));
        Assert.AreEqual("Small", GetFieldValue<object>(loadout, "Scope").ToString());
        Assert.IsFalse(GetFieldValue<bool>(loadout, "Muzzle"));
        Assert.IsFalse(GetFieldValue<bool>(loadout, "ImprovedBarrel"));
        Assert.AreEqual("Standard", GetFieldValue<object>(loadout, "Mk2Ammo").ToString());

        object vehicleIdentity = InvokeInstance(script, "ResolveCartelVehicleIdentity");

        Assert.AreEqual("Baller6", GetFieldValue<string>(vehicleIdentity, "Name"));
        Assert.AreEqual((int)InvokeStatic("EnumToIntHash", VehicleHash.Baller6), GetFieldValue<int>(vehicleIdentity, "Hash"));
        Assert.AreEqual("Baller6 blindée Cartel", GetFieldValue<string>(vehicleIdentity, "DisplayName"));
    }

    [TestMethod]
    public void SourceFile_OnTickRefreshesCartelContactAfterRelationshipUpdate()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        int refreshIndex = source.IndexOf("RefreshPlayerRelationshipIfNeeded();", StringComparison.Ordinal);
        Assert.IsTrue(refreshIndex >= 0, "L'appel au rafraichissement des relations doit rester present dans OnTick.");

        int updateIndex = source.IndexOf("UpdateCartelContactAndConvoy();", refreshIndex, StringComparison.Ordinal);
        Assert.IsTrue(updateIndex > refreshIndex, "OnTick doit appeler la mise a jour Cartel juste apres les relations joueur.");

        int customInputIndex = source.IndexOf("if (_customModelInputRequested)", updateIndex, StringComparison.Ordinal);
        Assert.IsTrue(customInputIndex > updateIndex, "L'appel Cartel doit rester avant la gestion d'entree de modele custom.");
    }

    [TestMethod]
    public void SourceFile_OnTickRunsCartelLateUpdateAfterNpcUpdate()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        int updateNpcsIndex = source.IndexOf("UpdateNpcs();", StringComparison.Ordinal);
        Assert.IsTrue(updateNpcsIndex >= 0, "L'appel UpdateNpcs doit rester present dans OnTick.");

        int lateUpdateIndex = source.IndexOf("UpdateCartelConvoyLate();", updateNpcsIndex, StringComparison.Ordinal);
        Assert.IsTrue(lateUpdateIndex > updateNpcsIndex, "OnTick doit appeler la passe tardive Cartel juste apres UpdateNpcs.");

        int placedVehiclesIndex = source.IndexOf("UpdatePlacedVehicles();", lateUpdateIndex, StringComparison.Ordinal);
        Assert.IsTrue(placedVehiclesIndex > lateUpdateIndex, "La passe tardive Cartel doit rester avant la mise a jour des vehicules places.");
    }

    [DataTestMethod]
    [DataRow(-1, 8, 7)]
    [DataRow(0, 8, 0)]
    [DataRow(8, 8, 0)]
    [DataRow(17, 8, 1)]
    [DataRow(5, 0, 0)]
    public void Wrap_ReturnsExpectedValue(int value, int count, int expected)
    {
        int actual = (int)InvokeStatic("Wrap", value, count);
        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow(-5, 1, 10, 1)]
    [DataRow(5, 1, 10, 5)]
    [DataRow(15, 1, 10, 10)]
    public void Clamp_ReturnsExpectedValue(int value, int min, int max, int expected)
    {
        int actual = (int)InvokeStatic("Clamp", value, min, max);
        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow(-5.0f, -1.0f, 1.0f, -1.0f)]
    [DataRow(0.5f, -1.0f, 1.0f, 0.5f)]
    [DataRow(3.0f, -1.0f, 1.0f, 1.0f)]
    public void ClampFloat_ReturnsExpectedValue(float value, float min, float max, float expected)
    {
        float actual = (float)InvokeStatic("ClampFloat", value, min, max);
        Assert.AreEqual(expected, actual, 0.0001f);
    }

    [DataTestMethod]
    [DataRow(113, 25, 125)]
    [DataRow(100, 25, 100)]
    [DataRow(99, 0, 99)]
    [DataRow(12, -5, 12)]
    public void RoundToStep_ReturnsExpectedValue(int value, int step, int expected)
    {
        int actual = (int)InvokeStatic("RoundToStep", value, step);
        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow("Static", 1, "Attacker")]
    [DataRow("Attacker", 1, "Neutral")]
    [DataRow("Neutral", 1, "Ally")]
    [DataRow("Ally", 1, "Static")]
    [DataRow("Static", -1, "Ally")]
    [DataRow("Ally", -1, "Neutral")]
    [DataRow("Neutral", -1, "Attacker")]
    public void CycleBehavior_WrapsAcrossStableBehaviorOrder(string currentName, int direction, string expectedName)
    {
        Type behaviorType = GetNestedType("EnemyBehavior");
        object current = Enum.Parse(behaviorType, currentName);

        object actual = InvokeStatic("CycleBehavior", current, direction);

        Assert.AreEqual(expectedName, actual.ToString());
    }

    [DataTestMethod]
    [DataRow("Static", "Statique / hostile \u00E0 vue")]
    [DataRow("Attacker", "Attaquer / agressif")]
    [DataRow("Neutral", "Neutre / garde passif")]
    [DataRow("Ally", "Alli\u00E9 / garde d\u00E9fense")]
    public void BehaviorDisplayName_ReturnsExpectedLabel(string behaviorName, string expected)
    {
        Type behaviorType = GetNestedType("EnemyBehavior");
        object behavior = Enum.Parse(behaviorType, behaviorName);

        string actual = (string)InvokeStatic("BehaviorDisplayName", behavior);

        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow("Npc", "NPC")]
    [DataRow("Vehicle", "Vehicule")]
    [DataRow("Object", "Objet")]
    public void PlacementTypeDisplayName_ReturnsExpectedLabel(string placementTypeName, string expected)
    {
        Type placementType = GetNestedType("PlacementEntityType");
        object placement = Enum.Parse(placementType, placementTypeName);

        string actual = (string)InvokeStatic("PlacementTypeDisplayName", placement);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void BuildObjectCategories_AddsLootAndUtilityObjectGroups()
    {
        IList all = (IList)InvokeStatic("BuildAllObjectOptions");
        IList categories = (IList)InvokeStatic("BuildObjectCategories", all);

        Assert.AreEqual(14, categories.Count, "Le menu objets doit exposer les categories utiles plus Tous les objets.");

        AssertObjectOption(all, "Billets 10 000$ - liasse plate", "prop_cash_pile_01", "ArgentButin");
        AssertObjectOption(all, "Chariot cash", "prop_cash_trolly", "ArgentButin");
        AssertObjectOption(all, "Pack munitions 1", "prop_ld_ammo_pack_01", "MaterielTactique");
        AssertObjectOption(all, "Kit de soin", "prop_ld_health_pack", "SoinSurvie");
        AssertObjectOption(all, "Ordinateur portable", "prop_laptop_01a", "BureauInformatique");
        AssertObjectOption(all, "Boite a outils", "prop_tool_box_04", "AtelierOutils");

        AssertCategoryContainsOption(categories, "Argent / butin", "Billets 10 000$ - liasse plate");
        AssertCategoryContainsOption(categories, "Materiel tactique", "Pack munitions 1");
        AssertCategoryContainsOption(categories, "Soins / survie", "Kit de soin");
        AssertCategoryContainsOption(categories, "Bureau / informatique", "Ordinateur portable");
        AssertCategoryContainsOption(categories, "Atelier / outils", "Boite a outils");
        AssertCategoryContainsOption(categories, "Tous les objets", "Chariot cash");
    }

    [TestMethod]
    public void ObjectInteractions_InferUsefulPlacedObjects()
    {
        object cash = CreateObjectIdentity("prop_cash_pile_01", "Billets 10 000$ - liasse plate");
        object health = CreateObjectIdentity("prop_ld_health_pack", "Kit de soin");
        object ammo = CreateObjectIdentity("prop_ld_ammo_pack_01", "Pack munitions 1");
        object armor = CreateObjectIdentity("prop_armour_pickup", "Gilet pare-balles");
        object decorative = CreateObjectIdentity("prop_chair_01a", "Chaise simple");

        InvokeStatic("ApplyDefaultObjectInteractionIfNeeded", cash);
        InvokeStatic("ApplyDefaultObjectInteractionIfNeeded", health);
        InvokeStatic("ApplyDefaultObjectInteractionIfNeeded", ammo);
        InvokeStatic("ApplyDefaultObjectInteractionIfNeeded", armor);
        InvokeStatic("ApplyDefaultObjectInteractionIfNeeded", decorative);

        AssertObjectInteraction(cash, "Cash", 10000, 0, 0, 0);
        AssertObjectInteraction(health, "Health", 0, 75, 0, 0);
        AssertObjectInteraction(ammo, "Ammo", 0, 0, 0, 90);
        AssertObjectInteraction(armor, "Armor", 0, 0, 50, 0);
        AssertObjectInteraction(decorative, "None", 0, 0, 0, 0);
        Assert.IsTrue((bool)InvokeStatic("HasObjectInteraction", cash));
        Assert.IsFalse((bool)InvokeStatic("HasObjectInteraction", decorative));
        Assert.AreEqual("Ramasser Billets 10 000$ - liasse plate (+10 000$)", (string)InvokeStatic("ObjectInteractionPromptText", cash));
        Assert.AreEqual("munitions +90", (string)InvokeStatic("ObjectInteractionDisplayName", ammo));

        IList options = (IList)InvokeStatic("BuildAllObjectOptions");
        object cashTrolley = options.Cast<object>().First(option => GetFieldValue<string>(option, "DisplayName") == "Chariot cash");
        object chair = options.Cast<object>().First(option => GetFieldValue<string>(option, "DisplayName") == "Chaise simple");

        Assert.AreEqual("Chariot cash | +200 000$", (string)InvokeStatic("ObjectOptionMenuDisplayName", cashTrolley));
        Assert.AreEqual("Chaise simple", (string)InvokeStatic("ObjectOptionMenuDisplayName", chair));
    }

    [TestMethod]
    public void ObjectInteractions_ReadLegacyAndExplicitXmlContracts()
    {
        XmlDocument legacyDocument = new XmlDocument();
        legacyDocument.LoadXml("<Object modelName=\"prop_cash_trolly\" displayName=\"Chariot cash\" />");

        object legacy = InvokeStatic("ReadObjectIdentityXml", legacyDocument.DocumentElement);

        AssertObjectInteraction(legacy, "Cash", 200000, 0, 0, 0);

        XmlDocument explicitDocument = new XmlDocument();
        explicitDocument.LoadXml("<Object modelName=\"prop_custom_reward\" displayName=\"Prime\" interactionKind=\"Cash\" cashValue=\"777\" healAmount=\"0\" armorAmount=\"0\" ammoAmount=\"0\" />");

        object explicitIdentity = InvokeStatic("ReadObjectIdentityXml", explicitDocument.DocumentElement);

        AssertObjectInteraction(explicitIdentity, "Cash", 777, 0, 0, 0);
    }

    [TestMethod]
    public void ObjectInteractions_WriteXmlAttributesForPersistence()
    {
        object identity = CreateObjectIdentity("prop_ld_health_pack", "Kit de soin");
        SetObjectInteraction(identity, "Health", 0, 75, 0, 0);

        StringWriter text = new StringWriter(CultureInfo.InvariantCulture);

        using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("Object");
            InvokeStatic("WriteObjectInteractionXmlAttributes", writer, identity);
            writer.WriteEndElement();
        }

        XElement element = XElement.Parse(text.ToString());

        Assert.AreEqual("Health", (string)element.Attribute("interactionKind"));
        Assert.AreEqual("0", (string)element.Attribute("cashValue"));
        Assert.AreEqual("75", (string)element.Attribute("healAmount"));
        Assert.AreEqual("0", (string)element.Attribute("armorAmount"));
        Assert.AreEqual("0", (string)element.Attribute("ammoAmount"));
    }

    [TestMethod]
    public void SourceFile_ObjectInteractionsRunBetweenPlacedObjectsAndPortals()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string tickBlock = ExtractSourceSection(
            source,
            "private void OnTick(object sender, EventArgs e)",
            "private void OnKeyDown(object sender, KeyEventArgs e)");

        int updateObjectsIndex = tickBlock.IndexOf("UpdatePlacedObjects();", StringComparison.Ordinal);
        int interactionIndex = tickBlock.IndexOf("UpdatePlacedObjectInteractions();", StringComparison.Ordinal);
        int updatePortalsIndex = tickBlock.IndexOf("UpdateInteriorPortals();", StringComparison.Ordinal);

        Assert.IsTrue(updateObjectsIndex >= 0 && interactionIndex > updateObjectsIndex, "Les interactions doivent utiliser l'etat objet deja nettoye.");
        Assert.IsTrue(updatePortalsIndex > interactionIndex, "Les interactions doivent etre mises a jour avant les portails et le statut.");
        StringAssert.Contains(source, "private const ulong NativeStatGetInt = 0x767FBC2AC802EF3DUL;");
        StringAssert.Contains(source, "private const ulong NativeStatSetInt = 0xB3271D7AB655B441UL;");
        StringAssert.Contains(source, "Function.Call((Hash)NativeAddAmmoToPed, player.Handle, weaponHash, amount);");
        StringAssert.Contains(source, "WriteObjectInteractionXmlAttributes(writer, placed.Identity);");
    }

    [TestMethod]
    public void HeadingFromTo_UsesGtaHeadingConvention()
    {
        float north = (float)InvokeStatic("HeadingFromTo", new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 10.0f, 0.0f));
        float east = (float)InvokeStatic("HeadingFromTo", new Vector3(0.0f, 0.0f, 0.0f), new Vector3(10.0f, 0.0f, 0.0f));
        float south = (float)InvokeStatic("HeadingFromTo", new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -10.0f, 0.0f));
        float west = (float)InvokeStatic("HeadingFromTo", new Vector3(0.0f, 0.0f, 0.0f), new Vector3(-10.0f, 0.0f, 0.0f));

        Assert.AreEqual(0.0f, north, 0.001f);
        Assert.AreEqual(90.0f, east, 0.001f);
        Assert.AreEqual(180.0f, south, 0.001f);
        Assert.AreEqual(270.0f, west, 0.001f);
    }

    [DataTestMethod]
    [DataRow(-10.0f, 350.0f)]
    [DataRow(0.0f, 0.0f)]
    [DataRow(45.0f, 45.0f)]
    [DataRow(361.0f, 1.0f)]
    [DataRow(720.0f, 0.0f)]
    public void NormalizeHeading_WrapsIntoCurrentRange(float value, float expected)
    {
        float actual = (float)InvokeStatic("NormalizeHeading", value);

        Assert.AreEqual(expected, actual, 0.001f);
    }

    [TestMethod]
    public void Normalize_ReturnsUnitVectorForNonZeroInput()
    {
        Vector3 actual = (Vector3)InvokeStatic("Normalize", new Vector3(3.0f, 4.0f, 0.0f));

        Assert.AreEqual(0.6f, actual.X, 0.0001f);
        Assert.AreEqual(0.8f, actual.Y, 0.0001f);
        Assert.AreEqual(0.0f, actual.Z, 0.0001f);
        Assert.AreEqual(1.0f, actual.Length(), 0.0001f);
    }

    [TestMethod]
    public void Normalize_ReturnsZeroVectorForNearZeroInput()
    {
        Vector3 actual = (Vector3)InvokeStatic("Normalize", new Vector3(0.00001f, 0.0f, 0.0f));

        Assert.AreEqual(Vector3.Zero, actual);
    }

    [TestMethod]
    public void IsZeroVector_UsesCurrentTolerance()
    {
        bool nearZero = (bool)InvokeStatic("IsZeroVector", new Vector3(0.0009f, -0.0009f, 0.0009f));
        bool outsideTolerance = (bool)InvokeStatic("IsZeroVector", new Vector3(0.0011f, 0.0f, 0.0f));

        Assert.IsTrue(nearZero);
        Assert.IsFalse(outsideTolerance);
    }

    [TestMethod]
    public void FormatVector_UsesInvariantFormatting()
    {
        string actual = (string)InvokeStatic("FormatVector", new Vector3(12.34f, -5.67f, 89.01f));
        Assert.AreEqual("X 12.3 | Y -5.7 | Z 89.0", actual);
    }

    [TestMethod]
    public void FitText_ReturnsEmptyForNull()
    {
        string actual = (string)InvokeStatic("FitText", null, 10);
        Assert.AreEqual(string.Empty, actual);
    }

    [DataTestMethod]
    [DataRow("", 10, "")]
    [DataRow("Test", 10, "Test")]
    [DataRow("abcdef", 3, "abc")]
    [DataRow("abcdef", 5, "ab...")]
    public void FitText_ReturnsExpectedValue(string text, int maxLength, string expected)
    {
        string actual = (string)InvokeStatic("FitText", text, maxLength);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EnumToIntHash_HandlesUnsignedUnderlyingEnums()
    {
        int actual = (int)InvokeStatic("EnumToIntHash", UnsignedHashExample.Value);
        int expected = unchecked((int)0xF1234567U);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void BuildModelOptions_KeepCustomFirstSortRemainingEntriesAndRemoveDuplicateHashes()
    {
        IList modelOptions = (IList)InvokeStatic("BuildModelOptions");

        Assert.IsTrue(modelOptions.Count > 1, "Le catalogue de modeles doit contenir l'entree custom et des peds du jeu.");

        HashSet<int> seenHashes = new HashSet<int>();
        string previousName = null;

        for (int index = 0; index < modelOptions.Count; index++)
        {
            object option = modelOptions[index];
            bool isCustom = GetFieldValue<bool>(option, "IsCustom");
            string displayName = GetFieldValue<string>(option, "DisplayName");
            int hash = GetFieldValue<int>(option, "Hash");

            Assert.IsFalse(string.IsNullOrWhiteSpace(displayName), "Chaque entree doit avoir un libelle exploitable.");

            if (index == 0)
            {
                Assert.IsTrue(isCustom);
                Assert.AreEqual("Custom", displayName);
                continue;
            }

            Assert.IsFalse(isCustom, "Seule la premiere entree doit representer le modele custom.");
            Assert.IsTrue(seenHashes.Add(hash), "Chaque hash de ped doit apparaitre une seule fois.");

            if (previousName != null)
            {
                Assert.IsTrue(
                    StringComparer.OrdinalIgnoreCase.Compare(previousName, displayName) <= 0,
                    "Les modeles non custom doivent rester tries par nom.");
            }

            previousName = displayName;
        }
    }

    [TestMethod]
    public void BuildWeaponOptions_KeepUnarmedFirstSortRemainingEntriesAndRemoveDuplicateHashes()
    {
        IList weaponOptions = (IList)InvokeStatic("BuildWeaponOptions");

        Assert.IsTrue(weaponOptions.Count > 1, "Le catalogue d'armes doit contenir au moins une entree exploitable.");

        HashSet<int> seenHashes = new HashSet<int>();
        string previousName = null;

        for (int index = 0; index < weaponOptions.Count; index++)
        {
            object option = weaponOptions[index];
            string displayName = GetFieldValue<string>(option, "DisplayName");
            int hash = (int)InvokeStatic("EnumToIntHash", (Enum)GetFieldValue<object>(option, "Hash"));

            Assert.IsFalse(string.IsNullOrWhiteSpace(displayName), "Chaque entree d'arme doit avoir un libelle exploitable.");
            Assert.IsTrue(seenHashes.Add(hash), "Chaque hash d'arme doit apparaitre une seule fois.");

            if (index == 0)
            {
                Assert.AreEqual("Unarmed", displayName);
                continue;
            }

            if (previousName != null)
            {
                Assert.IsTrue(
                    StringComparer.OrdinalIgnoreCase.Compare(previousName, displayName) <= 0,
                    "Les armes apres Unarmed doivent rester triees par nom.");
            }

            previousName = displayName;
        }
    }

    [TestMethod]
    public void FindDefaultModelIndex_PrefersSwatEntry()
    {
        object script = CreateScriptWithField(
            "_modelOptions",
            CreateModelOptionsList(
                CreateModelOption("Custom", true, 0),
                CreateModelOption("StreetCop", false, 10),
                CreateModelOption("Swat01SMY", false, 20),
                CreateModelOption("Worker", false, 30)));

        int actual = (int)InvokeInstance(script, "FindDefaultModelIndex");

        Assert.AreEqual(2, actual);
    }

    [TestMethod]
    public void FindDefaultModelIndex_FallsBackToCopWhenSwatIsMissing()
    {
        object script = CreateScriptWithField(
            "_modelOptions",
            CreateModelOptionsList(
                CreateModelOption("Custom", true, 0),
                CreateModelOption("BeachGuy", false, 10),
                CreateModelOption("RoadCop", false, 20)));

        int actual = (int)InvokeInstance(script, "FindDefaultModelIndex");

        Assert.AreEqual(2, actual);
    }

    [TestMethod]
    public void FindDefaultModelIndex_ReturnsZeroWhenNoPreferredModelExists()
    {
        object script = CreateScriptWithField(
            "_modelOptions",
            CreateModelOptionsList(
                CreateModelOption("Custom", true, 0),
                CreateModelOption("BeachGuy", false, 10),
                CreateModelOption("Worker", false, 20)));

        int actual = (int)InvokeInstance(script, "FindDefaultModelIndex");

        Assert.AreEqual(0, actual);
    }

    [TestMethod]
    public void FindDefaultWeaponIndex_PrefersCarbineRifle()
    {
        object script = CreateScriptWithField(
            "_weaponOptions",
            CreateWeaponOptionsList(
                CreateWeaponOption("Knife", WeaponHash.Knife),
                CreateWeaponOption("Pistol", WeaponHash.Pistol),
                CreateWeaponOption("CarbineRifle", WeaponHash.CarbineRifle)));

        int actual = (int)InvokeInstance(script, "FindDefaultWeaponIndex");

        Assert.AreEqual(2, actual);
    }

    [TestMethod]
    public void FindDefaultWeaponIndex_FallsBackToPistolWhenCarbineIsMissing()
    {
        object script = CreateScriptWithField(
            "_weaponOptions",
            CreateWeaponOptionsList(
                CreateWeaponOption("Knife", WeaponHash.Knife),
                CreateWeaponOption("Pistol", WeaponHash.Pistol),
                CreateWeaponOption("SMG", WeaponHash.SMG)));

        int actual = (int)InvokeInstance(script, "FindDefaultWeaponIndex");

        Assert.AreEqual(1, actual);
    }

    [TestMethod]
    public void FindDefaultWeaponIndex_ReturnsZeroWhenNoPreferredWeaponExists()
    {
        object script = CreateScriptWithField(
            "_weaponOptions",
            CreateWeaponOptionsList(
                CreateWeaponOption("Knife", WeaponHash.Knife),
                CreateWeaponOption("SMG", WeaponHash.SMG)));

        int actual = (int)InvokeInstance(script, "FindDefaultWeaponIndex");

        Assert.AreEqual(0, actual);
    }

    [TestMethod]
    public void GetRelationshipGroupForBehavior_MapsCurrentGroups()
    {
        Type behaviorType = GetNestedType("EnemyBehavior");
        object script = CreateScript();

        SetFieldValue(script, "_hostileGroupHash", 11);
        SetFieldValue(script, "_neutralGroupHash", 22);
        SetFieldValue(script, "_allyGroupHash", 33);

        Assert.AreEqual(11, (int)InvokeInstance(script, "GetRelationshipGroupForBehavior", Enum.Parse(behaviorType, "Static")));
        Assert.AreEqual(11, (int)InvokeInstance(script, "GetRelationshipGroupForBehavior", Enum.Parse(behaviorType, "Attacker")));
        Assert.AreEqual(22, (int)InvokeInstance(script, "GetRelationshipGroupForBehavior", Enum.Parse(behaviorType, "Neutral")));
        Assert.AreEqual(33, (int)InvokeInstance(script, "GetRelationshipGroupForBehavior", Enum.Parse(behaviorType, "Ally")));
    }

    [TestMethod]
    public void CurrentModelKey_UsesNormalizedCustomModelName()
    {
        object script = CreateScript();

        SetFieldValue(
            script,
            "_modelOptions",
            CreateModelOptionsList(
                CreateModelOption("Custom", true, 0),
                CreateModelOption("Swat01SMY", false, 123)));
        SetFieldValue(script, "_selectedModelIndex", 0);
        SetFieldValue(script, "_customModelName", "  S_M_Y_SWAT_01  ");

        string actual = (string)InvokeInstance(script, "CurrentModelKey");

        Assert.AreEqual("custom:s_m_y_swat_01", actual);
    }

    [TestMethod]
    public void CurrentModelKey_UsesHashForBuiltInModel()
    {
        object script = CreateScript();

        SetFieldValue(
            script,
            "_modelOptions",
            CreateModelOptionsList(
                CreateModelOption("Custom", true, 0),
                CreateModelOption("Swat01SMY", false, 123)));
        SetFieldValue(script, "_selectedModelIndex", 1);

        string actual = (string)InvokeInstance(script, "CurrentModelKey");

        Assert.AreEqual("hash:123", actual);
    }

    [TestMethod]
    public void PlacementEntityType_KeepsInteriorCycleOrder()
    {
        Type placementType = GetNestedType("PlacementEntityType");

        CollectionAssert.AreEqual(
            new[] { "Npc", "Vehicle", "Object", "Entrance", "Exit" },
            Enum.GetNames(placementType));
    }

    [TestMethod]
    public void PlacementTypeDisplayName_ReturnsInteriorPortalLabels()
    {
        Type placementType = GetNestedType("PlacementEntityType");

        Assert.AreEqual("Entree", (string)InvokeStatic("PlacementTypeDisplayName", Enum.Parse(placementType, "Entrance")));
        Assert.AreEqual("Sortie", (string)InvokeStatic("PlacementTypeDisplayName", Enum.Parse(placementType, "Exit")));
    }

    [TestMethod]
    public void BuildInteriorCategories_ContainsKnownEntriesAndSkipsCayoPerico()
    {
        IList categories = (IList)InvokeStatic("BuildInteriorCategories");

        bool foundBunker = false;
        bool foundFacility = false;
        bool foundCasino = false;
        bool foundCayo = false;

        foreach (object category in categories)
        {
            string categoryName = GetFieldValue<string>(category, "Name");
            IList options = (IList)GetFieldValue<object>(category, "Options");

            if (categoryName.IndexOf("cayo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                categoryName.IndexOf("perico", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foundCayo = true;
            }

            foreach (object option in options)
            {
                string id = GetFieldValue<string>(option, "Id");
                string displayName = GetFieldValue<string>(option, "DisplayName");

                if (string.Equals(id, "bunker_generic", StringComparison.Ordinal))
                {
                    foundBunker = true;
                }

                if (string.Equals(id, "facility", StringComparison.Ordinal))
                {
                    foundFacility = true;
                }

                if (string.Equals(id, "casino_main", StringComparison.Ordinal))
                {
                    foundCasino = true;
                }

                if (id.IndexOf("cayo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("perico", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    displayName.IndexOf("cayo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    displayName.IndexOf("perico", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foundCayo = true;
                }
            }
        }

        Assert.IsTrue(foundBunker, "Le catalogue des interieurs doit inclure le bunker generique.");
        Assert.IsTrue(foundFacility, "Le catalogue des interieurs doit inclure la facility.");
        Assert.IsTrue(foundCasino, "Le catalogue des interieurs doit inclure le casino.");
        Assert.IsFalse(foundCayo, "Le catalogue des interieurs ne doit pas inclure Cayo Perico par defaut.");
    }

    [TestMethod]
    public void BuildInteriorCategories_UsesUpdatedCriminalBaseCoordinates()
    {
        IList categories = (IList)InvokeStatic("BuildInteriorCategories");

        object facility = FindInteriorOption(categories, "facility");
        object iaaFacility = FindInteriorOption(categories, "iaa_facility");
        object serverFarm = FindInteriorOption(categories, "server_farm");
        object smugglersHangar = FindInteriorOption(categories, "smugglers_hangar");
        object submarine = FindInteriorOption(categories, "submarine");
        object bunker = FindInteriorOption(categories, "bunker_generic");

        Assert.IsNotNull(bunker, "Le bunker generique doit rester present.");
        AssertVector3Equals(new Vector3(892.6384f, -3245.8664f, -98.2645f), GetFieldValue<Vector3>(bunker, "Position"), 0.001f);
        Assert.AreEqual(180.0f, GetFieldValue<float>(bunker, "Heading"), 0.001f);

        Assert.IsNotNull(facility, "La facility Doomsday doit rester presente.");
        Assert.AreEqual("Doomsday Facility", GetFieldValue<string>(facility, "DisplayName"));
        AssertVector3Equals(new Vector3(483.2006f, 4810.5405f, -58.91929f), GetFieldValue<Vector3>(facility, "Position"), 0.001f);
        Assert.AreEqual(18.04706f, GetFieldValue<float>(facility, "Heading"), 0.001f);

        Assert.IsNotNull(iaaFacility, "IAA Facility doit rester presente.");
        AssertVector3Equals(new Vector3(2151.137f, 2921.3303f, -61.90187f), GetFieldValue<Vector3>(iaaFacility, "Position"), 0.001f);
        Assert.AreEqual(85.82783f, GetFieldValue<float>(iaaFacility, "Heading"), 0.001f);

        Assert.IsNotNull(serverFarm, "IAA Server Farm doit rester present.");
        Assert.AreEqual("IAA Server Farm", GetFieldValue<string>(serverFarm, "DisplayName"));
        AssertVector3Equals(new Vector3(2158.1184f, 2920.9382f, -81.07539f), GetFieldValue<Vector3>(serverFarm, "Position"), 0.001f);
        Assert.AreEqual(270.48007f, GetFieldValue<float>(serverFarm, "Heading"), 0.001f);

        Assert.IsNotNull(smugglersHangar, "Le hangar Smuggler's Run doit rester present.");
        AssertVector3Equals(new Vector3(-1266.9995f, -3014.6135f, -49.51799f), GetFieldValue<Vector3>(smugglersHangar, "Position"), 0.001f);
        Assert.AreEqual(359.93738f, GetFieldValue<float>(smugglersHangar, "Heading"), 0.001f);

        Assert.IsNotNull(submarine, "Le sous-marin / Kosatka doit rester present.");
        AssertVector3Equals(new Vector3(514.29266f, 4885.8706f, -62.58986f), GetFieldValue<Vector3>(submarine, "Position"), 0.001f);
        Assert.AreEqual(180.25909f, GetFieldValue<float>(submarine, "Heading"), 0.001f);
    }

    [TestMethod]
    public void BuildRuntimeInteriorOptionForEntry_OverridesLegacyBunkerArrivalOnly()
    {
        object oldBunker = CreateInteriorOption("bunker_generic", "Online - bases criminelles", "Bunker interieur generique", new Vector3(899.5518f, -3246.038f, -98.04907f), 0.0f);
        object customBunker = CreateInteriorOption("custom_bunker_hideout", "Custom", "Bunker custom", new Vector3(100.0f, 200.0f, 300.0f), 42.0f);
        object facility = CreateInteriorOption("facility", "Online - bases criminelles", "Doomsday Facility", new Vector3(483.2006f, 4810.5405f, -58.91929f), 18.04706f);

        object runtimeOldBunker = InvokeStatic("BuildRuntimeInteriorOptionForEntry", oldBunker);
        object runtimeCustomBunker = InvokeStatic("BuildRuntimeInteriorOptionForEntry", customBunker);
        object runtimeFacility = InvokeStatic("BuildRuntimeInteriorOptionForEntry", facility);

        AssertVector3Equals(new Vector3(892.6384f, -3245.8664f, -98.2645f), GetFieldValue<Vector3>(runtimeOldBunker, "Position"), 0.001f);
        Assert.AreEqual(180.0f, GetFieldValue<float>(runtimeOldBunker, "Heading"), 0.001f);

        AssertVector3Equals(new Vector3(892.6384f, -3245.8664f, -98.2645f), GetFieldValue<Vector3>(runtimeCustomBunker, "Position"), 0.001f);
        Assert.AreEqual(180.0f, GetFieldValue<float>(runtimeCustomBunker, "Heading"), 0.001f);

        AssertVector3Equals(new Vector3(483.2006f, 4810.5405f, -58.91929f), GetFieldValue<Vector3>(runtimeFacility, "Position"), 0.001f);
        Assert.AreEqual(18.04706f, GetFieldValue<float>(runtimeFacility, "Heading"), 0.001f);
    }

    [TestMethod]
    public void SourceFile_EnterInteriorPortalUsesRuntimeInteriorOverrides()
    {
        string interiorsSource = File.ReadAllText(GetInteriorsSourceFilePath());
        string enterBlock = ExtractSourceSection(
            interiorsSource,
            "private void EnterInteriorPortal(PlacedInteriorPortal portal, Ped player)",
            "private void ExitInteriorPortal(PlacedInteriorPortal portal, Ped player)");

        StringAssert.Contains(interiorsSource, "private static readonly Vector3 BunkerInteriorSafeArrivalPosition = new Vector3(892.6384f, -3245.8664f, -98.2645f);");
        StringAssert.Contains(interiorsSource, "private const float BunkerInteriorSafeArrivalHeading = 180.0f;");
        StringAssert.Contains(enterBlock, "InteriorOption runtimeInterior = BuildRuntimeInteriorOptionForEntry(portal.Interior);");
        StringAssert.Contains(enterBlock, "TeleportPlayerWithFadeSafe(player, runtimeInterior.Position, runtimeInterior.Heading);");
        StringAssert.Contains(enterBlock, "ApplyInteriorEntitySetsSafe(runtimeInterior);");
    }

    [TestMethod]
    public void SourceFile_MainMenuUsesContextualPlacementSlotsAndPortalSpawnHooks()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        StringAssert.Contains(source, "DrawMainMenuRow(x, width, rowY + rowHeight * 8, 8, PlacementSlotCategoryLabel(), PlacementSlotCategoryValue());");
        StringAssert.Contains(source, "DrawMainMenuRow(x, width, rowY + rowHeight * 9, 9, PlacementSlotOptionLabel(), PlacementSlotOptionValue());");
        StringAssert.Contains(source, "UpdateInteriorPortals();");
        StringAssert.Contains(source, "return TryPlaceInteriorEntrance(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);");
        StringAssert.Contains(source, "return TryPlaceInteriorExit(requestedPosition, surfaceNormal, precise, hasHeadingOverride, headingOverride);");
        StringAssert.Contains(source, "ConfirmInteriorEntrancePlacementSpawn();");
        StringAssert.Contains(source, "ConfirmInteriorExitPlacementSpawn();");
    }

    [TestMethod]
    public void SourceFile_MainMenuAddsInteriorPortalCleanupAction()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string menuBlock = ExtractSourceSection(
            source,
            "private void ActivateMainMenuItem()",
            "private void DrawMenu()");

        StringAssert.Contains(source, "DrawMainMenuRow(x, width, rowY + rowHeight * 23, 23, \"Nettoyer entrees/sorties\", \"Supprimer les reperes interieurs\");");
        StringAssert.Contains(menuBlock, "case 23:");
        StringAssert.Contains(menuBlock, "CleanAllInteriorPortals();");
    }

    [TestMethod]
    public void SourceFile_MainMenuUsesCustomNpcPlacerVisualFrame()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        StringAssert.Contains(source, "DrawText(TrainerSubtitle, x + 31, y + 42");
        StringAssert.Contains(source, "DrawMainSummaryPanel(x + width + MainMenuSummaryGap, y, MainMenuSummaryWidth, MainMenuSummaryHeight);");
        StringAssert.Contains(source, "private void DrawPanelFrame(int x, int y, int width, int height, Color accentColor)");
        StringAssert.Contains(source, "private void DrawBadge(int x, int y, int width, string text, Color background, Color accentColor)");
        StringAssert.Contains(source, "private void DrawHeaderStat(int x, int y, int width, string label, string value, Color accentColor)");
        StringAssert.Contains(source, "private void DrawSelectedMainMenuCard(int x, int y, int width, int height, MainMenuEntry entry)");
        StringAssert.Contains(source, "private void DrawMainSummaryContextLines(int x, int width, int lineY, Color accent)");
        StringAssert.Contains(source, "private void DrawSummaryMetric(int x, int y, int width, string label, string value, Color accentColor)");
        StringAssert.Contains(source, "private Color GetMainMenuAccent(int index)");
        StringAssert.Contains(source, "private const int MainMenuPanelX = 34;");
        StringAssert.Contains(source, "private const int MainMenuValueColumnX = 344;");
        StringAssert.Contains(source, "FitText(_statusText, StatusTextMaxLength)");
    }

    [TestMethod]
    public void SourceFile_MainMenuUsesDynamicCollapsibleSections()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        StringAssert.Contains(source, "private List<MainMenuEntry> BuildMainMenuEntries()");
        StringAssert.Contains(source, "private int _mainMenuScrollOffset;");
        StringAssert.Contains(source, "private bool _mainMenuNpcExpanded = true;");
        StringAssert.Contains(source, "private void EnsureMainMenuSelectionVisible(int entryCount)");
        StringAssert.Contains(source, "private void DrawMainMenuScrollbar(int x, int y, int width, int height, int entryCount, int visibleRows)");
        StringAssert.Contains(source, "case Keys.PageUp:");
        StringAssert.Contains(source, "case Keys.PageDown:");
        StringAssert.Contains(source, "case Keys.Tab:");
        StringAssert.Contains(source, "MoveMainMenuSectionFocus(entries, e.Shift ? -1 : 1);");
        StringAssert.Contains(source, "private void MoveMainMenuSectionFocus(List<MainMenuEntry> entries, int direction)");

        int placementTypeIndex = source.IndexOf("MainMenuAction.PlacementType, \"Type de placement\"", StringComparison.Ordinal);
        int precisePlacementIndex = source.IndexOf("MainMenuAction.PrecisePlacement, \"Placement camera precis\"", StringComparison.Ordinal);
        int distancePlacementIndex = source.IndexOf("MainMenuAction.DistancePlacement, \"Placement direct\"", StringComparison.Ordinal);
        int placementDistanceIndex = source.IndexOf("MainMenuAction.PlacementDistance, \"Distance placement direct\"", StringComparison.Ordinal);

        Assert.IsTrue(placementTypeIndex >= 0, "La ligne Type de placement doit rester presente.");
        Assert.IsTrue(precisePlacementIndex > placementTypeIndex, "Le placement camera precis doit rester en deuxieme position.");
        Assert.IsTrue(distancePlacementIndex > precisePlacementIndex, "Le placement direct doit rester apres le placement camera precis.");
        Assert.IsTrue(placementDistanceIndex > distancePlacementIndex, "La distance du placement direct doit rester apres l'action directe.");

        StringAssert.Contains(source, "MainMenuAction.SectionNpc");
        StringAssert.Contains(source, "\"NPC\"");
        StringAssert.Contains(source, "MainMenuAction.SectionVehicle");
        StringAssert.Contains(source, "\"Vehicules\"");
        StringAssert.Contains(source, "MainMenuAction.SectionObject");
        StringAssert.Contains(source, "\"Objets\"");
        StringAssert.Contains(source, "MainMenuAction.SectionInterior");
        StringAssert.Contains(source, "\"Entrees / sorties\"");
        StringAssert.Contains(source, "MainMenuAction.SectionSave");
        StringAssert.Contains(source, "\"Sauvegarde\"");
        StringAssert.Contains(source, "MainMenuAction.SectionCleanup");
        StringAssert.Contains(source, "\"Nettoyage\"");
        StringAssert.Contains(source, "return GetPlacementTypeColor(_selectedPlacementType);");
        StringAssert.Contains(source, "return Color.FromArgb(245, 60, 220, 150);");
    }

    [TestMethod]
    public void SourceFile_MainMenuUsesContextualSummaryBadgesAndNoRedundantSelectionRebuild()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string drawMenuBlock = ExtractSourceSection(
            source,
            "private void DrawMainMenu()",
            "private void DrawWeaponEditorMenu()");
        string summaryBlock = ExtractSourceSection(
            source,
            "private void DrawMainSummaryContextLines(int x, int width, int lineY, Color accent)",
            "private void DrawSummaryLine(int x, int width, int y, string label, string value)");
        string keyBlock = ExtractSourceSection(
            source,
            "private void HandleMainMenuKey(KeyEventArgs e)",
            "private void MoveMainMenuSectionFocus");

        StringAssert.Contains(drawMenuBlock, "DrawBadge(");
        StringAssert.Contains(drawMenuBlock, "\"TAB sections\"");
        StringAssert.Contains(drawMenuBlock, "_selectedAutoRespawn ? \"Respawn ON\" : \"Respawn OFF\"");
        StringAssert.Contains(drawMenuBlock, "MainMenuEntry selectedEntry = entries.Count > 0");
        Assert.IsFalse(drawMenuBlock.Contains("GetSelectedMainMenuEntry();"), "Le rendu ne doit pas reconstruire les entrees juste pour connaitre la selection.");

        StringAssert.Contains(summaryBlock, "case PlacementEntityType.Vehicle:");
        StringAssert.Contains(summaryBlock, "case PlacementEntityType.Object:");
        StringAssert.Contains(summaryBlock, "case PlacementEntityType.Entrance:");
        StringAssert.Contains(summaryBlock, "ObjectInteractionDisplayName(objectPreview)");
        StringAssert.Contains(summaryBlock, "PV / Armure");

        StringAssert.Contains(keyBlock, "ChangeMainMenuValue(-1, entries);");
        StringAssert.Contains(keyBlock, "ChangeMainMenuValue(1, entries);");
        StringAssert.Contains(keyBlock, "ActivateMainMenuItem(entries);");
        StringAssert.Contains(source, "private static ObjectIdentity CreateObjectIdentityPreview(ObjectOption option)");
    }

    [TestMethod]
    public void SourceFile_MainMenuHintsCoverCriticalCategoriesAndActions()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string hintBlock = ExtractSourceSection(
            source,
            "private string MainMenuActionHint(MainMenuEntry entry)",
            "private static bool IsMainMenuValueEditable(MainMenuAction action)");

        StringAssert.Contains(hintBlock, "case MainMenuAction.NpcCategory:");
        StringAssert.Contains(hintBlock, "case MainMenuAction.NpcWeaponCategory:");
        StringAssert.Contains(hintBlock, "case MainMenuAction.VehicleCategory:");
        StringAssert.Contains(hintBlock, "case MainMenuAction.ObjectCategory:");
        StringAssert.Contains(hintBlock, "case MainMenuAction.InteriorCategory:");
        StringAssert.Contains(hintBlock, "Les butins affichent leur valeur utile.");
        StringAssert.Contains(hintBlock, "Entree ouvre/ferme la section. Droite ouvre, Gauche ferme.");
    }

    [TestMethod]
    public void SourceFiles_TerminatorModeIsIsolatedAndHookedIntoMenuTickHudAndShutdown()
    {
        string source = File.ReadAllText(GetSourceFilePath());
        string terminatorSource = File.ReadAllText(GetTerminatorModeSourceFilePath());
        string tickBlock = ExtractSourceSection(
            source,
            "private void OnTick(object sender, EventArgs e)",
            "private void OnKeyDown(object sender, KeyEventArgs e)");
        string abortBlock = ExtractSourceSection(
            source,
            "private void OnAborted(object sender, EventArgs e)",
            "private void HandleMainMenuKey(KeyEventArgs e)");
        string buildMenuBlock = ExtractSourceSection(
            source,
            "private List<MainMenuEntry> BuildMainMenuEntries()",
            "private void AddMainMenuSection");
        string updateTerminatorBlock = ExtractSourceSection(
            terminatorSource,
            "private void UpdateTerminatorMode()",
            "private void DrawTerminatorModeHud()");
        string drawTerminatorBlock = ExtractSourceSection(
            terminatorSource,
            "private void DrawTerminatorModeHud()",
            "private void ApplyTerminatorModeToPlayer(Ped player, bool firstApply)");
        string redVisionBlock = ExtractSourceSection(
            terminatorSource,
            "private void DrawTerminatorRedVisionOverlay(Ped player)",
            "private void DrawTerminatorSideRulers()");
        string maintainFilterBlock = ExtractSourceSection(
            terminatorSource,
            "private void MaintainTerminatorVisionFilter(Ped player)",
            "private void ClearTerminatorVisionFilter()");
        string applyFilterBlock = ExtractSourceSection(
            terminatorSource,
            "private void ApplyTerminatorVisionFilter(bool force)",
            "private void MaintainTerminatorVisionFilter(Ped player)");
        string enableTerminatorBlock = ExtractSourceSection(
            terminatorSource,
            "private void EnableTerminatorMode()",
            "private void DisableTerminatorMode(bool showStatus)");

        StringAssert.Contains(source, "MainMenuAction.TerminatorMode");
        StringAssert.Contains(source, "TryHandleTerminatorVisionKey(e.KeyCode)");
        StringAssert.Contains(tickBlock, "UpdateTerminatorMode();");
        StringAssert.Contains(tickBlock, "DrawTerminatorModeHud();");
        StringAssert.Contains(abortBlock, "DisableTerminatorMode(false);");
        StringAssert.Contains(buildMenuBlock, "\"Mode Terminator\"");
        StringAssert.Contains(buildMenuBlock, "_terminatorModeEnabled ? \"ACTIVE - vision rouge T-800\" : \"DESACTIVE\"");

        StringAssert.Contains(terminatorSource, "private void ToggleTerminatorMode()");
        StringAssert.Contains(terminatorSource, "private void EnableTerminatorMode()");
        StringAssert.Contains(terminatorSource, "private void DisableTerminatorMode(bool showStatus)");
        StringAssert.Contains(terminatorSource, "private void ForceTerminatorFirstPersonCamera()");
        StringAssert.Contains(terminatorSource, "private void RestoreTerminatorCameraViewModes()");
        StringAssert.Contains(terminatorSource, "private bool IsTerminatorFirstPersonCameraActive(Ped player)");
        StringAssert.Contains(terminatorSource, "private void ApplyTerminatorVisionFilter(bool force)");
        StringAssert.Contains(terminatorSource, "private void ClearTerminatorVisionFilter()");
        StringAssert.Contains(terminatorSource, "private bool _terminatorLowLightVisionApplied;");
        StringAssert.Contains(terminatorSource, "private bool _terminatorThermalVisionApplied;");
        StringAssert.Contains(terminatorSource, "private bool TryHandleTerminatorVisionKey(Keys keyCode)");
        StringAssert.Contains(terminatorSource, "keyCode != Keys.B");
        Assert.IsFalse(terminatorSource.Contains("keyCode != Keys.N"), "La touche N ne doit plus etre utilisee par le cycle de vision Terminator.");
        StringAssert.Contains(terminatorSource, "B change la vision.");
        Assert.IsFalse(terminatorSource.Contains("N change la vision."), "Le texte d'aide ne doit plus annoncer N pour changer la vision.");
        StringAssert.Contains(terminatorSource, "private void CycleTerminatorVisionMode()");
        StringAssert.Contains(terminatorSource, "private void DrawTerminatorRedVisionOverlay(Ped player)");
        StringAssert.Contains(terminatorSource, "private void DrawTerminatorFocusedTargetPanel(Ped player)");
        StringAssert.Contains(terminatorSource, "private void UpdateTerminatorPunchPower(Ped player)");
        StringAssert.Contains(terminatorSource, "private void UpdateTerminatorResistanceRegeneration(Ped player, int currentHealth, int currentArmor)");
        StringAssert.Contains(terminatorSource, "private int _terminatorLastWeaponFireAt = -1000000;");
        StringAssert.Contains(terminatorSource, "if (firstApply && currentHealth < TerminatorMinHealth)");
        Assert.IsFalse(
            terminatorSource.Contains("if (SafeGetPedHealth(player) < TerminatorMinHealth)"),
            "Le mode Terminator ne doit plus bloquer la vie a 2000 HP a chaque tick.");
        Assert.IsFalse(
            terminatorSource.Contains("if (firstApply || currentArmor < TerminatorArmorRefreshThreshold)"),
            "L'armure Terminator ne doit plus etre remplie instantanement a chaque tick.");
        StringAssert.Contains(terminatorSource, "now - _terminatorLastDamageAt >= TerminatorHealthRegenDelayAfterDamageMs");
        StringAssert.Contains(terminatorSource, "currentHealth = Math.Min(TerminatorMinHealth, currentHealth + TerminatorHealthRegenAmount);");
        StringAssert.Contains(terminatorSource, "currentArmor = Math.Min(TerminatorArmor, currentArmor + TerminatorArmorRegenAmount);");
        StringAssert.Contains(terminatorSource, "private bool HasFreshTerminatorMeleeImpact");
        StringAssert.Contains(terminatorSource, "if (IsTerminatorWeaponFireRecentlyActive(player, now))");
        StringAssert.Contains(terminatorSource, "private bool IsTerminatorWeaponFireRecentlyActive(Ped player, int now)");
        StringAssert.Contains(terminatorSource, "private static bool IsTerminatorSelectedWeaponMeleeCompatible(Ped player)");
        StringAssert.Contains(terminatorSource, "if (!HasEntityBeenDamagedByEntitySafe(target, player))");
        Assert.IsFalse(
            terminatorSource.Contains("DrawTerminatorTargetMarkers"),
            "Le HUD Terminator ne doit plus afficher des cadres autour de toutes les cibles.");
        Assert.IsFalse(
            terminatorSource.Contains("TerminatorScanRadius"),
            "Le mode Terminator ne doit plus scanner/afficher toutes les cibles en permanence.");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetTimecycleModifier, \"REDMIST_blend\");");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetTimecycleModifierStrength, 0.42f);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetNightvision, true);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetNightvision, false);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetSeethrough, true);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetSeethrough, false);");
        StringAssert.Contains(applyFilterBlock, "case TerminatorVisionModeNight:");
        StringAssert.Contains(applyFilterBlock, "case TerminatorVisionModeThermal:");
        StringAssert.Contains(applyFilterBlock, "case TerminatorVisionModeRed:");
        StringAssert.Contains(enableTerminatorBlock, "_terminatorVisionMode = TerminatorVisionModeRed;");
        Assert.IsFalse(
            enableTerminatorBlock.Contains("TryCallNative(NativeSetNightvision, true);"),
            "La vision nocturne ne doit pas etre activee automatiquement a l'activation du mode Terminator.");
        StringAssert.Contains(terminatorSource, "UpdateTerminatorFocusedTarget(player);");
        StringAssert.Contains(terminatorSource, "SafeGetFreeAimingEntityHandle()");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetPedSuffersCriticalHits, player.Handle, false);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetFollowPedCamViewMode, TerminatorFirstPersonViewMode);");
        StringAssert.Contains(terminatorSource, "TryCallNative(NativeSetFollowVehicleCamViewMode, TerminatorFirstPersonViewMode);");
        StringAssert.Contains(updateTerminatorBlock, "MaintainTerminatorVisionFilter(player);");
        StringAssert.Contains(updateTerminatorBlock, "if (IsTerminatorFirstPersonCameraActive(player))");
        Assert.IsFalse(updateTerminatorBlock.Contains("ForceTerminatorFirstPersonCamera();"), "Le mode Terminator ne doit pas verrouiller la vue 1ere personne a chaque tick.");
        StringAssert.Contains(drawTerminatorBlock, "if (!IsTerminatorFirstPersonCameraActive(player))");
        Assert.IsFalse(
            redVisionBlock.Contains("DrawRect(0, 0, TerminatorHudWidth, 60"),
            "Le HUD Terminator ne doit pas assombrir toute la bande haute.");
        Assert.IsFalse(
            redVisionBlock.Contains("DrawRect(0, TerminatorHudHeight - 58, TerminatorHudWidth, 58"),
            "Le HUD Terminator ne doit pas assombrir toute la bande basse.");
        StringAssert.Contains(redVisionBlock, "GetTerminatorVisionOverlayColor(impactPulse)");
        StringAssert.Contains(maintainFilterBlock, "ApplyTerminatorVisionFilter(false);");
        StringAssert.Contains(maintainFilterBlock, "ClearTerminatorVisionFilter();");

        int damageCheckIndex = terminatorSource.IndexOf("if (!HasEntityBeenDamagedByEntitySafe(target, player))", StringComparison.Ordinal);
        int coneCheckIndex = terminatorSource.IndexOf("return IsEntityInsideTerminatorPunchCone(player, target, TerminatorImpactConeDot);", StringComparison.Ordinal);
        int fireBlockIndex = terminatorSource.IndexOf("if (IsTerminatorWeaponFireRecentlyActive(player, now))", StringComparison.Ordinal);
        int meleeActiveIndex = terminatorSource.IndexOf("bool meleeActive = IsTerminatorMeleeActionActive(player);", StringComparison.Ordinal);
        int weaponCompatibleIndex = terminatorSource.IndexOf("if (!IsTerminatorSelectedWeaponMeleeCompatible(player))", StringComparison.Ordinal);
        int performingMeleeIndex = terminatorSource.IndexOf("if (IsTerminatorPedPerformingMeleeActionSafe(player))", StringComparison.Ordinal);

        Assert.IsTrue(damageCheckIndex >= 0 && coneCheckIndex > damageCheckIndex, "Le cone ne doit servir qu'apres confirmation d'un impact GTA reel.");
        Assert.IsTrue(fireBlockIndex >= 0 && meleeActiveIndex > fireBlockIndex, "Un tir recent doit bloquer la fenetre de propulsion avant toute detection de melee.");
        Assert.IsTrue(weaponCompatibleIndex >= 0 && performingMeleeIndex > weaponCompatibleIndex, "L'etat melee generique ne doit compter que pour une arme compatible melee.");
    }

    [TestMethod]
    public void SourceFile_AutoRespawnPersistsAndRequiresPlayerToLeaveArea()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        StringAssert.Contains(source, "DrawMainMenuRow(x, width, rowY + rowHeight * 15, 15, \"Reapparition auto\", BoolText(_selectedAutoRespawn));");
        StringAssert.Contains(source, "writer.WriteAttributeString(\"autoRespawn\",");
        StringAssert.Contains(source, "ReadBoolAttribute(node, \"autoRespawn\", false)");
        StringAssert.Contains(source, "MainMenuAction.VehicleAutoRespawn");
        StringAssert.Contains(source, "MainMenuAction.ObjectAutoRespawn");
        StringAssert.Contains(source, "CanAutoRespawnAt(player");
        StringAssert.Contains(source, "distance < AutoRespawnLeaveDistance");
        StringAssert.Contains(source, "TryProcessNpcAutoRespawn");
        StringAssert.Contains(source, "TryProcessPlacedVehicleAutoRespawn");
        StringAssert.Contains(source, "TryProcessPlacedObjectAutoRespawn");
        StringAssert.Contains(source, "MarkPlacedObjectForAutoRespawn(placed);");
        StringAssert.Contains(source, "DeleteEntitySafe(placed.Prop);");
    }

    [TestMethod]
    public void SourceFiles_SaveLoadAndInteriorLabelsKeepPortalContract()
    {
        string mainSource = File.ReadAllText(GetSourceFilePath());
        string interiorsSource = File.ReadAllText(GetInteriorsSourceFilePath());

        StringAssert.Contains(mainSource, "writer.WriteAttributeString(\"version\", \"5\")");
        StringAssert.Contains(mainSource, "savedPortals = WriteInteriorPortalsXml(writer);");
        StringAssert.Contains(mainSource, "loadedPortals = LoadInteriorPortalsFromXml(doc);");

        StringAssert.Contains(interiorsSource, "return \"Categorie interieur\";");
        StringAssert.Contains(interiorsSource, "return \"Sortie active\";");
        StringAssert.Contains(interiorsSource, "return \"Destination sortie\";");
        StringAssert.Contains(interiorsSource, "\"Retour au marqueur d'entree\"");
    }

    [TestMethod]
    public void SourceFiles_InteriorPortalsUseAdvancedLoadingAndSafeTeleport()
    {
        string interiorsSource = File.ReadAllText(GetInteriorsSourceFilePath());
        string advancedSource = File.ReadAllText(GetAdvancedInteriorsSourceFilePath());

        StringAssert.Contains(interiorsSource, "MaintainActiveInteriorVisualsSafe(player);");
        StringAssert.Contains(interiorsSource, "bool prepared = PrepareInteriorForTeleportSafe(runtimeInterior);");
        StringAssert.Contains(interiorsSource, "TeleportPlayerWithFadeSafe(player, runtimeInterior.Position, runtimeInterior.Heading);");
        StringAssert.Contains(interiorsSource, "ApplyInteriorEntitySetsSafe(runtimeInterior);");
        StringAssert.Contains(interiorsSource, "TeleportPlayerWithFadeSafe(player, returnPosition, returnHeading);");
        StringAssert.Contains(interiorsSource, "JoinInteriorIpls(BuildEffectiveInteriorIplList(portal.Interior))");

        StringAssert.Contains(advancedSource, "private bool PrepareInteriorForTeleportSafe(InteriorOption interior)");
        StringAssert.Contains(advancedSource, "private static List<string> BuildEffectiveInteriorIplList(InteriorOption interior)");
        StringAssert.Contains(advancedSource, "private void TeleportPlayerWithFadeSafe(Ped player, Vector3 targetPosition, float heading)");
        StringAssert.Contains(advancedSource, "private void StabilizeInteriorViewportAfterTeleportSafe(Ped player, InteriorOption interior, float heading)");
        StringAssert.Contains(advancedSource, "private void MaintainActiveInteriorVisualsSafe(Ped player)");
        StringAssert.Contains(advancedSource, "private void CleanAllInteriorPortals()");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeOnEnterMp = 0x0888C3502DBBEEF5UL;");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeRefreshInterior = 0x41F37C3427C75AE0UL;");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeDeactivateInteriorEntitySet = 0x420BD37289EEE162UL;");
        StringAssert.Contains(advancedSource, "Function.Call((Hash)AdvancedNativeDeactivateInteriorEntitySet, interiorId, set.Name);");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeForceRoomForEntity = 0x52923C4710DD9907UL;");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeForceRoomForGameViewport = 0x920D853F3E17F1DAUL;");
        StringAssert.Contains(advancedSource, "private const ulong AdvancedNativeSetFocusPosAndVel = 0xBB7454BAFF08FE25UL;");
        StringAssert.Contains(advancedSource, "private const int AdvancedInteriorMaintainIntervalMs = 250;");
    }

    [TestMethod]
    public void BuildDefaultInteriorEntitySetsSafe_DeactivatesBunkerBasicSetsAndAddsUpgrades()
    {
        object bunker = CreateInteriorOption("bunker_generic", "Online - bases criminelles", "Bunker interieur generique", new Vector3(899.5518f, -3246.038f, -98.04907f), 0.0f);

        IList entitySets = (IList)InvokeStatic("BuildDefaultInteriorEntitySetsSafe", bunker);

        AssertEntitySetState(entitySets, "standard_bunker_set", false);
        AssertEntitySetState(entitySets, "standard_security_set", false);
        AssertEntitySetState(entitySets, "Office_blocker_set", false);
        AssertEntitySetState(entitySets, "office_blocker_set", false);
        AssertEntitySetState(entitySets, "gun_range_blocker_set", false);
        AssertEntitySetState(entitySets, "Bunker_Style_C", true);
        AssertEntitySetState(entitySets, "bunker_style_c", true);
        AssertEntitySetState(entitySets, "upgrade_bunker_set", true);
        AssertEntitySetState(entitySets, "security_upgrade", true);
        AssertEntitySetState(entitySets, "office_upgrade_set", true);
        AssertEntitySetState(entitySets, "gun_range_lights", true);
        AssertEntitySetState(entitySets, "gun_locker_upgrade", true);
        AssertEntitySetState(entitySets, "Gun_schematic_set", true);
    }

    [TestMethod]
    public void BuildDefaultInteriorEntitySetsSafe_AddsExpandedBusinessAndOfficeSets()
    {
        object facility = CreateInteriorOption("facility", "Online - bases criminelles", "Doomsday Facility", new Vector3(483.2006f, 4810.5405f, -58.91929f), 18.04706f);
        object nightclub = CreateInteriorOption("nightclub", "Online - business", "Nightclub", new Vector3(-1604.664f, -3012.583f, -78.000f), 0.0f);
        object vehicleWarehouse = CreateInteriorOption("vehicle_warehouse", "Online - business", "Vehicle warehouse", new Vector3(994.5925f, -3002.594f, -39.64699f), 0.0f);
        object ceoOffice = CreateInteriorOption("maze_west_1", "Online - bureaux", "Maze Bank West", new Vector3(-1392.667f, -480.4736f, 72.04217f), 0.0f);

        IList facilitySets = (IList)InvokeStatic("BuildDefaultInteriorEntitySetsSafe", facility);
        IList nightclubSets = (IList)InvokeStatic("BuildDefaultInteriorEntitySetsSafe", nightclub);
        IList vehicleWarehouseSets = (IList)InvokeStatic("BuildDefaultInteriorEntitySetsSafe", vehicleWarehouse);
        IList ceoOfficeSets = (IList)InvokeStatic("BuildDefaultInteriorEntitySetsSafe", ceoOffice);

        AssertEntitySetState(facilitySets, "set_int_02_shell", true, true, 1);
        AssertEntitySetState(facilitySets, "set_Int_02_outfit_serverfarm", true, true, 1);
        AssertEntitySetState(nightclubSets, "Int01_ba_security_upgrade", true);
        AssertEntitySetState(nightclubSets, "Int01_ba_equipment_upgrade", true);
        AssertEntitySetState(vehicleWarehouseSets, "basic_style_set", false);
        AssertEntitySetState(vehicleWarehouseSets, "urban_style_set", true);
        AssertEntitySetState(ceoOfficeSets, "cash_set_24", true);
        AssertEntitySetState(ceoOfficeSets, "swag_guns3", true);
    }

    [TestMethod]
    public void BuildEffectiveInteriorIplList_AddsAutomaticDlcIpls()
    {
        object facility = CreateInteriorOption("facility", "Online - bases criminelles", "Doomsday Facility", new Vector3(483.2006f, 4810.5405f, -58.91929f), 18.04706f);
        object smugglers = CreateInteriorOption("smugglers_hangar", "Online - bases criminelles", "Hangar Smuggler's Run", new Vector3(-1266.9995f, -3014.6135f, -49.51799f), 359.93738f);
        object apartment = CreateInteriorOption("apt_modern_1", "Appartements online IPL", "Modern 1 Apartment", new Vector3(-786.8663f, 315.7642f, 217.6385f), 0.0f, "apa_v_mp_h_01_a");

        IList facilityIpls = (IList)InvokeStatic("BuildEffectiveInteriorIplList", facility);
        IList smugglersIpls = (IList)InvokeStatic("BuildEffectiveInteriorIplList", smugglers);
        IList apartmentIpls = (IList)InvokeStatic("BuildEffectiveInteriorIplList", apartment);

        AssertListContains(facilityIpls, "xm_x17dlc_int_placement");
        AssertListContains(facilityIpls, "xm_x17dlc_int_placement_interior_4_x17dlc_int_facility_milo_");
        AssertListContains(smugglersIpls, "sm_smugdlc_interior_placement");
        AssertListContains(smugglersIpls, "sm_smugdlc_interior_placement_interior_0_smugdlc_int_01_milo_");
        AssertListContains(apartmentIpls, "apa_v_mp_h_01_a");
        Assert.AreEqual(1, CountStringOccurrences(apartmentIpls, "apa_v_mp_h_01_a"), "La liste d'IPLs effective ne doit pas dupliquer un IPL deja present.");
    }

    [TestMethod]
    public void AdvancedInteriorFlags_KeepApartmentViewportPreparationContract()
    {
        object apartment = CreateInteriorOption("apt_modern_1", "Appartements online IPL", "Modern 1 Apartment", new Vector3(-786.8663f, 315.7642f, 217.6385f), 0.0f, "apa_v_mp_h_01_a");
        object bunker = CreateInteriorOption("bunker_generic", "Online - bases criminelles", "Bunker interieur generique", new Vector3(899.5518f, -3246.038f, -98.04907f), 0.0f);
        object legacy = CreateInteriorOption("maison_safe", "Maisons", "Maison safe", new Vector3(1.0f, 2.0f, 3.0f), 90.0f);

        bool apartmentNeedsMpMap = (bool)InvokeStatic("ShouldLoadMultiplayerMapSafe", apartment);
        bool apartmentNeedsReadyWait = (bool)InvokeStatic("ShouldWaitForInteriorReadySafe", apartment);
        bool bunkerNeedsReadyWait = (bool)InvokeStatic("ShouldWaitForInteriorReadySafe", bunker);
        bool legacyNeedsReadyWait = (bool)InvokeStatic("ShouldWaitForInteriorReadySafe", legacy);

        Assert.IsTrue(apartmentNeedsMpMap, "Les appartements online doivent continuer a demander le chargement de la map multi.");
        Assert.IsTrue(apartmentNeedsReadyWait, "Les appartements online doivent continuer a attendre un interieur pret apres teleportation.");
        Assert.IsTrue(bunkerNeedsReadyWait, "Le bunker doit continuer a attendre un interieur pret.");
        Assert.IsFalse(legacyNeedsReadyWait, "Un interieur legacy simple ne doit pas bloquer le flux sur un interior id pret.");
    }

    [DataTestMethod]
    [DataRow("maison", "maison.xml")]
    [DataRow("setup.XML", "setup.XML")]
    [DataRow("  escorte  ", "escorte.xml")]
    public void NormalizeSaveFileName_AppendsXmlAndTrimsInput(string input, string expected)
    {
        string actual = (string)InvokeStatic("NormalizeSaveFileName", input);

        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow("..", "maison.xml")]
    [DataRow(@"..\villa", "villa.xml")]
    [DataRow("bad*name", "bad_name.xml")]
    [DataRow("safe\0name", "safename.xml")]
    public void NormalizeSaveFileName_RewritesUnsafeInput(string input, string expected)
    {
        string actual = (string)InvokeStatic("NormalizeSaveFileName", input);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void NormalizeSaveFileName_LimitsLongNames()
    {
        string input = new string('a', 160) + ".xml";

        string actual = (string)InvokeStatic("NormalizeSaveFileName", input);

        Assert.IsTrue(actual.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(actual.Length <= GetStaticFieldValue<int>("MaxSaveFileNameLength"));
    }

    [TestMethod]
    public void TryResolveSavePathForLoad_UsesConfiguredDirectoryAndBackup()
    {
        string previousDirectory = Environment.GetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DonJEnemySpawnerTests_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDirectory);
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", tempDirectory);

            string backupPath = Path.Combine(tempDirectory, "villa.xml.bak");
            File.WriteAllText(backupPath, "<DonJEnemySpawnerSave />");

            object script = CreateScript();
            object[] args = { "villa", null, null };

            bool resolved = (bool)InvokeInstance(script, "TryResolveSavePathForLoad", args);

            Assert.IsTrue(resolved, "Le chargement doit retrouver le backup si le XML principal manque.");
            Assert.AreEqual(Path.GetFullPath(backupPath), Path.GetFullPath((string)args[1]));
            Assert.AreEqual(Path.GetFullPath(tempDirectory), Path.GetFullPath((string)args[2]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONJ_ENEMY_SPAWNER_SAVE_DIR", previousDirectory);

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void ReplaceFileAtomically_ReplacesTargetAndKeepsBackup()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DonJEnemySpawnerTests_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDirectory);

            string targetPath = Path.Combine(tempDirectory, "base.xml");
            string tempPath = Path.Combine(tempDirectory, "base.xml.tmp");
            string backupPath = targetPath + ".bak";

            File.WriteAllText(targetPath, "old");
            File.WriteAllText(tempPath, "new");

            InvokeStatic("ReplaceFileAtomically", tempPath, targetPath);

            Assert.AreEqual("new", File.ReadAllText(targetPath));
            Assert.AreEqual("old", File.ReadAllText(backupPath));
            Assert.IsFalse(File.Exists(tempPath), "Le fichier temporaire doit etre consomme par le remplacement.");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void SourceFile_SaveSystemPersistsLastFileAndUsesStableFallbacks()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        StringAssert.Contains(source, "InitializePersistentSaveState();");
        StringAssert.Contains(source, "private const string LastSaveFileMarkerName = \"_last_save.txt\";");
        StringAssert.Contains(source, "private const string SaveDirectoryEnvironmentVariable = \"DONJ_ENEMY_SPAWNER_SAVE_DIR\";");
        StringAssert.Contains(source, "private const string DefaultEnhancedGtaRoot = @\"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Grand Theft Auto V Enhanced\";");
        StringAssert.Contains(source, "writer.WriteAttributeString(\"saveFile\", normalizedFileName);");
        StringAssert.Contains(source, "writer.WriteAttributeString(\"saveDirectory\", saveDirectory);");
        StringAssert.Contains(source, "ReplaceFileAtomically(tempPath, path);");
        StringAssert.Contains(source, "PersistLastSaveFileNameSafe(normalizedFileName);");
        StringAssert.Contains(source, "TryResolveSavePathForLoad(normalizedFileName, out path, out searchedDirectory)");
        StringAssert.Contains(source, "MigrateLoadedSaveToCanonicalLocationSafe(path, normalizedFileName);");
        StringAssert.Contains(source, "GetDocumentsSaveDirectorySafe()");
        StringAssert.Contains(source, "GetLocalAppDataSaveDirectorySafe()");
        StringAssert.Contains(source, "File.Replace(tempPath, targetPath, backupPath, true);");
        StringAssert.Contains(source, "DonJCustomNpcPlacer.ENdll");
        StringAssert.Contains(source, "DonJCustomNpcPlacer.dll");
    }

    [TestMethod]
    public void ProjectFile_UsesStableFrameworkAndEnhancedOutputLayout()
    {
        XDocument document = XDocument.Load(GetProjectFilePath());

        Assert.AreEqual("net48", GetPropertyValue(document, "TargetFramework"));
        Assert.AreEqual("Library", GetPropertyValue(document, "OutputType"));
        Assert.AreEqual("DonJCustomNpcPlacer", GetPropertyValue(document, "AssemblyName"));
        Assert.AreEqual("true", GetPropertyValue(document, "UseWindowsForms"));
        Assert.AreEqual("false", GetPropertyValue(document, "AppendTargetFrameworkToOutputPath"));
        Assert.AreEqual(
            @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced",
            GetPropertyValue(document, "DefaultEnhancedGtaRoot"));
        Assert.AreEqual(@"$(GtaRoot)\Scripts", GetPropertyValue(document, "GtaScriptsDir"));
    }

    [TestMethod]
    public void SourceFile_NeutralGuardsReactToNearbyPlayerHostilityWithShortMemory()
    {
        string source = File.ReadAllText(GetSourceFilePath());

        string tickBlock = ExtractSourceSection(
            source,
            "private void OnTick(object sender, EventArgs e)",
            "private void OnKeyDown(object sender, KeyEventArgs e)");

        string neutralHostilityBlock = ExtractSourceSection(
            source,
            "private void UpdatePlayerHostilityMemory(Ped player)",
            "private void AlertNearbyNeutralGuards(Vector3 eventPosition, Ped player, Entity witnessedEntity)");

        string alertBlock = ExtractSourceSection(
            source,
            "private void AlertNearbyNeutralGuards(Vector3 eventPosition, Ped player, Entity witnessedEntity)",
            "private void ConvertNeutralToHostile(SpawnedNpc npc, Ped player)");

        Assert.IsTrue(
            source.IndexOf("private const int PlayerHostilityMemoryMs = 2200;", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent garder une memoire courte des actes hostiles du joueur.");

        Assert.IsTrue(
            tickBlock.IndexOf("UpdatePlayerHostilityMemory(Game.Player.Character);", StringComparison.Ordinal) >= 0,
            "La memoire d'hostilite joueur doit etre mise a jour avant UpdateNpcs.");

        Assert.IsTrue(
            neutralHostilityBlock.IndexOf("Hash.IS_BULLET_IN_AREA", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent pouvoir reagir a une balle proche.");

        Assert.IsTrue(
            neutralHostilityBlock.IndexOf("Hash.GET_PED_LAST_WEAPON_IMPACT_COORD", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent pouvoir reagir a un impact de balle proche.");

        Assert.IsTrue(
            neutralHostilityBlock.IndexOf("Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent detecter les degats du joueur sur des peds ou vehicules proches.");

        Assert.IsTrue(
            neutralHostilityBlock.IndexOf("Hash.IS_PED_IN_MELEE_COMBAT", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent detecter le combat au corps-a-corps proche du joueur.");

        Assert.IsTrue(
            neutralHostilityBlock.IndexOf("NeutralNearbyVehicleAttackReactionDistance", StringComparison.Ordinal) >= 0,
            "Les gardes neutres doivent couvrir les vehicules attaques proches.");

        Assert.IsTrue(
            alertBlock.IndexOf("HasRecentPlayerGunfireNearGuard(candidate.Ped, player, out heardShotPosition)", StringComparison.Ordinal) >= 0,
            "L'alerte des gardes neutres proches doit utiliser la memoire de tir, pas seulement l'instant IS_PED_SHOOTING.");
    }

    [TestMethod]
    public void ProjectFile_DeploysReleaseBuildAsEndll()
    {
        XDocument document = XDocument.Load(GetProjectFilePath());

        XElement localEndllTarget = FindTarget(document, "CreateLocalEndll");
        XElement deployTarget = FindTarget(document, "DeployAsEndll");

        Assert.IsNotNull(localEndllTarget, "La cible MSBuild CreateLocalEndll est introuvable.");
        Assert.IsNotNull(deployTarget, "La cible MSBuild DeployAsEndll est introuvable.");

        string localTargetXml = localEndllTarget.ToString(SaveOptions.DisableFormatting);
        string targetXml = deployTarget.ToString(SaveOptions.DisableFormatting);

        StringAssert.Contains(localTargetXml, "$(TargetDir)$(AssemblyName).ENdll");
        StringAssert.Contains(localTargetXml, "$(TargetPath)");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\$(AssemblyName).ENdll");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\$(AssemblyName).dll");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\$(AssemblyName).pdb");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\DonJEnemySpawner.ENdll");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\DonJEnemySpawner.dll");
        StringAssert.Contains(targetXml, "$(GtaScriptsDir)\\DonJEnemySpawner.pdb");
        StringAssert.Contains(targetXml, "DonJ Custom NPC Placer deploye vers");
        StringAssert.Contains(targetXml, "SkipUnchangedFiles=\"false\"");
    }

    [TestMethod]
    public void ProjectFile_ValidatesEnhancedRootAndKeepsApiReferencePrivateFalse()
    {
        XDocument document = XDocument.Load(GetProjectFilePath());

        Assert.IsNull(FindTarget(document, "CopyGameDll"), "L'ancienne cible CopyGameDll ne doit plus exister.");

        XElement validateTarget = FindTarget(document, "ValidateGtaReference");
        Assert.IsNotNull(validateTarget, "La cible MSBuild ValidateGtaReference est introuvable.");

        string validateTargetXml = validateTarget.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(validateTargetXml, "GTA5_Enhanced.exe");
        StringAssert.Contains(validateTargetXml, "NIBScriptHookVDotNet2.dll");
        StringAssert.Contains(validateTargetXml, "ScriptHookVDotNet2.dll");

        XElement reference = FindReference(document, "$(ShvdnApiReferenceName)");
        Assert.IsNotNull(reference, "La reference API v2 resolue dynamiquement est introuvable.");

        XElement hintPath = reference.Element("HintPath");
        Assert.IsNotNull(hintPath, "La reference API v2 doit definir un HintPath.");
        Assert.AreEqual("$(ShvdnApiPath)", hintPath.Value);

        XElement privateElement = reference.Element("Private");
        Assert.IsNotNull(privateElement, "La reference ScriptHookVDotNet2 doit declarer Private=false.");
        Assert.AreEqual("false", privateElement.Value);
    }

    [TestMethod]
    public void TestProjectFile_UsesStableRuntimeAndCopiesApiReferenceLocally()
    {
        XDocument document = XDocument.Load(GetTestProjectFilePath());

        Assert.AreEqual("net48", GetPropertyValue(document, "TargetFramework"));
        Assert.AreEqual("false", GetPropertyValue(document, "AppendTargetFrameworkToOutputPath"));
        Assert.AreEqual("false", GetPropertyValue(document, "IsPackable"));

        XElement reference = FindReference(document, "$(ShvdnApiReferenceName)");
        Assert.IsNotNull(reference, "Le projet de tests doit aussi referencer l'API v2 resolue dynamiquement.");

        XElement privateElement = reference.Element("Private");
        Assert.IsNotNull(privateElement, "Le projet de tests doit copier l'API v2 pour VSTest.");
        Assert.AreEqual("true", privateElement.Value);
    }

    [TestMethod]
    public void TestProjectFile_KeepsMSTestPackagesAndProjectReference()
    {
        XDocument document = XDocument.Load(GetTestProjectFilePath());

        Assert.IsNotNull(FindPackageReference(document, "Microsoft.NET.Test.Sdk"));
        Assert.IsNotNull(FindPackageReference(document, "MSTest.TestAdapter"));
        Assert.IsNotNull(FindPackageReference(document, "MSTest.TestFramework"));
        Assert.IsNotNull(
            FindProjectReference(document, @"..\..\src\DonJEnemySpawner\DonJEnemySpawner.csproj"),
            "Le projet de tests doit continuer a referencer le mod principal.");
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateStatic);
        Assert.IsNotNull(method, $"La methode privee statique '{methodName}' est introuvable.");
        return method.Invoke(null, args);
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, $"La methode privee d'instance '{methodName}' est introuvable.");
        return method.Invoke(target, args);
    }

    private static T GetStaticFieldValue<T>(string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateStatic);
        Assert.IsNotNull(field, $"Le champ prive statique '{fieldName}' est introuvable.");

        object rawValue = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return (T)rawValue;
    }

    private static T GetFieldValue<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Le champ '{fieldName}' est introuvable sur '{target.GetType().FullName}'.");
        return (T)field.GetValue(target);
    }

    private static void SetFieldValue(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"Le champ '{fieldName}' est introuvable sur '{target.GetType().FullName}'.");
        field.SetValue(target, value);
    }

    private static Type GetNestedType(string nestedTypeName)
    {
        Type nestedType = ScriptType.GetNestedType(nestedTypeName, BindingFlags.NonPublic);
        Assert.IsNotNull(nestedType, $"Le type imbrique prive '{nestedTypeName}' est introuvable.");
        return nestedType;
    }

    private static object CreateScriptWithField(string fieldName, object value)
    {
        object script = CreateScript();
        SetFieldValue(script, fieldName, value);
        return script;
    }

    private static object CreateScript()
    {
        return FormatterServices.GetUninitializedObject(ScriptType);
    }

    private static object CreateModelOption(string displayName, bool isCustom, int hash)
    {
        object option = Activator.CreateInstance(GetNestedType("ModelOption"), true);
        SetFieldValue(option, "DisplayName", displayName);
        SetFieldValue(option, "IsCustom", isCustom);
        SetFieldValue(option, "Hash", hash);
        return option;
    }

    private static object CreateWeaponOption(string displayName, WeaponHash hash)
    {
        object option = Activator.CreateInstance(GetNestedType("WeaponOption"), true);
        SetFieldValue(option, "DisplayName", displayName);
        SetFieldValue(option, "Hash", hash);
        return option;
    }

    private static object CreateInteriorOption(string id, string category, string displayName, Vector3 position, float heading, params string[] ipls)
    {
        object option = Activator.CreateInstance(GetNestedType("InteriorOption"), true);
        SetFieldValue(option, "Id", id);
        SetFieldValue(option, "Category", category);
        SetFieldValue(option, "DisplayName", displayName);
        SetFieldValue(option, "Position", position);
        SetFieldValue(option, "Heading", heading);
        SetFieldValue(option, "Ipls", new List<string>(ipls ?? Array.Empty<string>()));
        return option;
    }

    private static object CreateObjectIdentity(string modelName, string displayName)
    {
        object identity = Activator.CreateInstance(GetNestedType("ObjectIdentity"), true);
        SetFieldValue(identity, "ModelName", modelName);
        SetFieldValue(identity, "DisplayName", displayName);
        SetObjectInteraction(identity, "None", 0, 0, 0, 0);
        return identity;
    }

    private static void SetObjectInteraction(object identity, string kindName, int cashValue, int healAmount, int armorAmount, int ammoAmount)
    {
        Type interactionKindType = GetNestedType("ObjectInteractionKind");

        SetFieldValue(identity, "InteractionKind", Enum.Parse(interactionKindType, kindName));
        SetFieldValue(identity, "CashValue", cashValue);
        SetFieldValue(identity, "HealAmount", healAmount);
        SetFieldValue(identity, "ArmorAmount", armorAmount);
        SetFieldValue(identity, "AmmoAmount", ammoAmount);
    }

    private static object CreateModelOptionsList(params object[] options)
    {
        return CreateTypedList(GetNestedType("ModelOption"), options);
    }

    private static object CreateWeaponOptionsList(params object[] options)
    {
        return CreateTypedList(GetNestedType("WeaponOption"), options);
    }

    private static object CreateTypedList(Type itemType, params object[] items)
    {
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));

        foreach (object item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static string GetProjectFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.csproj");
    }

    private static string GetSourceFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.cs");
    }

    private static string GetHighSecurityEscortSourceFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.HighSecurityEscort.cs");
    }

    private static string GetTerminatorModeSourceFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.TerminatorMode.cs");
    }

    private static string GetInteriorsSourceFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.Interiors.cs");
    }

    private static string GetAdvancedInteriorsSourceFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "DonJEnemySpawner.Interiors.AdvancedLoading.cs");
    }

    private static object FindInteriorOption(IList categories, string id)
    {
        foreach (object category in categories)
        {
            IList options = (IList)GetFieldValue<object>(category, "Options");

            foreach (object option in options)
            {
                if (string.Equals(GetFieldValue<string>(option, "Id"), id, StringComparison.Ordinal))
                {
                    return option;
                }
            }
        }

        return null;
    }

    private static void AssertVector3Equals(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance);
        Assert.AreEqual(expected.Y, actual.Y, tolerance);
        Assert.AreEqual(expected.Z, actual.Z, tolerance);
    }

    private static void AssertListContains(IList list, string expected)
    {
        foreach (object item in list)
        {
            if (string.Equals(item as string, expected, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"La liste ne contient pas '{expected}'.");
    }

    private static void AssertObjectOption(IList options, string displayName, string modelName, string categoryName)
    {
        foreach (object option in options)
        {
            if (!string.Equals(GetFieldValue<string>(option, "DisplayName"), displayName, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.AreEqual(modelName, GetFieldValue<string>(option, "ModelName"));
            Assert.AreEqual(categoryName, GetFieldValue<object>(option, "Category").ToString());
            return;
        }

        Assert.Fail($"L'objet '{displayName}' est introuvable.");
    }

    private static void AssertObjectInteraction(object identity, string kindName, int cashValue, int healAmount, int armorAmount, int ammoAmount)
    {
        Assert.AreEqual(kindName, GetFieldValue<object>(identity, "InteractionKind").ToString());
        Assert.AreEqual(cashValue, GetFieldValue<int>(identity, "CashValue"));
        Assert.AreEqual(healAmount, GetFieldValue<int>(identity, "HealAmount"));
        Assert.AreEqual(armorAmount, GetFieldValue<int>(identity, "ArmorAmount"));
        Assert.AreEqual(ammoAmount, GetFieldValue<int>(identity, "AmmoAmount"));
    }

    private static void AssertCategoryContainsOption(IList categories, string categoryName, string displayName)
    {
        IList options = FindCategoryOptions(categories, categoryName);

        Assert.IsNotNull(options, $"La categorie '{categoryName}' est introuvable.");

        foreach (object option in options)
        {
            if (string.Equals(GetFieldValue<string>(option, "DisplayName"), displayName, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"La categorie '{categoryName}' ne contient pas '{displayName}'.");
    }

    private static IList FindCategoryOptions(IList categories, string categoryName)
    {
        foreach (object category in categories)
        {
            if (string.Equals(GetFieldValue<string>(category, "Name"), categoryName, StringComparison.Ordinal))
            {
                return (IList)GetFieldValue<object>(category, "Options");
            }
        }

        return null;
    }

    private static void AssertEntitySetState(IList entitySets, string name, bool enabled)
    {
        AssertEntitySetState(entitySets, name, enabled, false, 0);
    }

    private static void AssertEntitySetState(IList entitySets, string name, bool enabled, bool hasTint, int tintIndex)
    {
        foreach (object entitySet in entitySets)
        {
            if (!string.Equals(GetFieldValue<string>(entitySet, "Name"), name, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.AreEqual(enabled, GetFieldValue<bool>(entitySet, "Enabled"), $"Etat Enabled incorrect pour '{name}'.");
            Assert.AreEqual(hasTint, GetFieldValue<bool>(entitySet, "HasTint"), $"Etat HasTint incorrect pour '{name}'.");
            Assert.AreEqual(tintIndex, GetFieldValue<int>(entitySet, "TintIndex"), $"TintIndex incorrect pour '{name}'.");
            return;
        }

        Assert.Fail($"L'entity set '{name}' est introuvable.");
    }

    private static int CountStringOccurrences(IList list, string expected)
    {
        int count = 0;

        foreach (object item in list)
        {
            if (string.Equals(item as string, expected, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string ExtractSourceSection(string source, string startMarker, string endMarker)
    {
        int startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0, $"Le marqueur de début '{startMarker}' est introuvable dans la source.");

        int endIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.IsTrue(endIndex > startIndex, $"Le marqueur de fin '{endMarker}' est introuvable dans la source.");

        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int searchIndex = 0;

        while (true)
        {
            int foundIndex = source.IndexOf(value, searchIndex, StringComparison.Ordinal);

            if (foundIndex < 0)
            {
                return count;
            }

            count++;
            searchIndex = foundIndex + value.Length;
        }
    }

    private static string GetTestProjectFilePath()
    {
        return Path.Combine(GetRepositoryRoot(), "tests", "DonJEnemySpawner.Tests", "DonJEnemySpawner.Tests.csproj");
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "GTA5modDEV.sln");

            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du depot depuis le dossier de test.");
        return string.Empty;
    }

    private static string GetPropertyValue(XDocument document, string propertyName)
    {
        foreach (XElement propertyGroup in document.Root.Elements("PropertyGroup"))
        {
            XElement property = propertyGroup.Element(propertyName);

            if (property != null)
            {
                return property.Value;
            }
        }

        Assert.Fail($"La propriete '{propertyName}' est introuvable.");
        return string.Empty;
    }

    private static XElement FindTarget(XDocument document, string name)
    {
        foreach (XElement target in document.Root.Elements("Target"))
        {
            XAttribute nameAttribute = target.Attribute("Name");

            if (nameAttribute != null && string.Equals(nameAttribute.Value, name, StringComparison.Ordinal))
            {
                return target;
            }
        }

        return null;
    }

    private static XElement FindReference(XDocument document, string includeValue)
    {
        foreach (XElement itemGroup in document.Root.Elements("ItemGroup"))
        {
            foreach (XElement reference in itemGroup.Elements("Reference"))
            {
                XAttribute includeAttribute = reference.Attribute("Include");

                if (includeAttribute != null &&
                    string.Equals(includeAttribute.Value, includeValue, StringComparison.Ordinal))
                {
                    return reference;
                }
            }
        }

        return null;
    }

    private static XElement FindProjectReference(XDocument document, string includeValue)
    {
        foreach (XElement itemGroup in document.Root.Elements("ItemGroup"))
        {
            foreach (XElement projectReference in itemGroup.Elements("ProjectReference"))
            {
                XAttribute includeAttribute = projectReference.Attribute("Include");

                if (includeAttribute != null &&
                    string.Equals(
                        NormalizePathLikeValue(includeAttribute.Value),
                        NormalizePathLikeValue(includeValue),
                        StringComparison.Ordinal))
                {
                    return projectReference;
                }
            }
        }

        return null;
    }

    private static XElement FindPackageReference(XDocument document, string includeValue)
    {
        foreach (XElement itemGroup in document.Root.Elements("ItemGroup"))
        {
            foreach (XElement packageReference in itemGroup.Elements("PackageReference"))
            {
                XAttribute includeAttribute = packageReference.Attribute("Include");

                if (includeAttribute != null &&
                    string.Equals(includeAttribute.Value, includeValue, StringComparison.Ordinal))
                {
                    return packageReference;
                }
            }
        }

        return null;
    }

    private static string NormalizePathLikeValue(string value)
    {
        string normalized = value.Replace('/', '\\');

        while (normalized.Contains("\\\\"))
        {
            normalized = normalized.Replace("\\\\", "\\");
        }

        return normalized;
    }
}
