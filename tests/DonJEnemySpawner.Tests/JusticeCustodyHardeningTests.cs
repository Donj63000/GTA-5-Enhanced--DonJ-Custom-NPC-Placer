using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using GTA;
using GTA.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeCustodyHardeningTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void DlcWeaponEnumeration_UsesA312ByteReusableUnmanagedBuffer()
    {
        Type dataType = GetNestedType("JusticeDlcWeaponData");
        Assert.AreEqual(312, Marshal.SizeOf(dataType));
        FieldInfo hashField = dataType.GetField(
            "WeaponHash",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(hashField);
        FieldOffsetAttribute offset = hashField.GetCustomAttribute<FieldOffsetAttribute>();
        Assert.IsNotNull(offset);
        Assert.AreEqual(8, offset.Value);

        Assert.IsNotNull(
            typeof(InputArgument).GetConstructor(new[] { typeof(ulong) }),
            "L'API NIB/stub doit accepter l'adresse x64 sans OutputArgument sous-dimensionné.");

        string collect = ExtractMethodBody(ReadCustodySource(), "TryCollectJusticeWeaponHashes");
        Assert.AreEqual(1, CountOccurrences(collect, "Marshal.AllocCoTaskMem"));
        Assert.AreEqual(1, CountOccurrences(collect, "Marshal.FreeCoTaskMem"));
        Assert.IsFalse(collect.Contains("new OutputArgument()"));
        AssertOrdered(
            collect,
            "Marshal.SizeOf(typeof(JusticeDlcWeaponData))",
            "Marshal.AllocCoTaskMem(nativeDataSize)",
            "new InputArgument(",
            "for (int index = 0; index < dlcCount; index++)",
            "ZeroJusticeUnmanagedBuffer",
            "JusticeNativeGetDlcWeaponData",
            "Marshal.ReadInt32",
            "finally",
            "Marshal.FreeCoTaskMem");
        StringAssert.Contains(collect, "return false;");
    }

    [TestMethod]
    public void DeferredAndShutdownRestore_AreExactDurableAndNeverRemoveAll()
    {
        string source = ReadCustodySource();
        string retry = ExtractMethodBody(source, "RetryJusticeDeferredInventoryRestore");
        AssertOrdered(
            retry,
            "RestoreJusticeWeaponSnapshotMergeSafe(player, true, true)",
            "CommitJusticeDeferredInventoryRestore()");

        string commit = ExtractMethodBody(source, "CommitJusticeDeferredInventoryRestore");
        AssertOrdered(
            commit,
            "JusticeWeaponSnapshot restoredSnapshot",
            "_justiceWeaponSnapshot = null",
            "PersistJusticeDeferredRestoreRedundantly()",
            "_justiceWeaponSnapshot = restoredSnapshot");

        string merge = ExtractMethodBody(source, "RestoreJusticeWeaponSnapshotMergeSafe");
        Assert.IsFalse(merge.Contains("RemoveJusticePlayerWeaponsSafe"));
        StringAssert.Contains(merge, "JusticeNativeSetPedAmmo");
        StringAssert.Contains(merge, "GIVE_WEAPON_COMPONENT_TO_PED");
        StringAssert.Contains(merge, "SET_PED_WEAPON_TINT_INDEX");
        StringAssert.Contains(merge, "SET_AMMO_IN_CLIP");
        AssertOrdered(
            merge,
            "SET_CURRENT_PED_WEAPON",
            "NativeGetSelectedPedWeapon",
            "selectedWeaponHash != _justiceWeaponSnapshot.SelectedWeaponHash");

        string provisional = ExtractMethodBody(
            source,
            "RestoreJusticeInventoryProvisionallyOnShutdown");
        Assert.IsFalse(provisional.Contains("RemoveJusticePlayerWeaponsSafe"));
        StringAssert.Contains(provisional, "RestoreJusticeWeaponSnapshotMergeSafe(player, true, true)");
        StringAssert.Contains(provisional, "attempt < 3 && !restored");
        Assert.IsFalse(
            provisional.Contains("_justiceInventoryRemoved ="),
            "La restitution d'arrêt reste provisoire : l'état durable doit demander une reconfiscation au reload.");

        string shutdown = ExtractMethodBody(source, "JusticeShutdownCustody");
        Assert.IsFalse(shutdown.Contains("RestoreJusticeWeaponSnapshot(player)"));
        Assert.IsFalse(shutdown.Contains("RemoveJusticePlayerWeaponsSafe"));
        Assert.AreEqual(
            6,
            CountOccurrences(shutdown, "RunJusticeCustodyShutdownStep("),
            "Chaque domaine de nettoyage doit être isolé, police comprise dans le finally.");
        AssertOrdered(
            shutdown,
            "\"Activite\"",
            "\"Discipline\"",
            "\"Inventaire\"",
            "RestoreJusticeInventoryProvisionallyOnShutdown(player)",
            "\"EtatJoueur\"",
            "\"Scene\"",
            "finally",
            "\"Police\"",
            "_justiceWeaponControlsLocked = false");

        string isolatedStep = ExtractMethodBody(source, "RunJusticeCustodyShutdownStep");
        AssertOrdered(isolatedStep, "try", "action();", "catch (Exception ex)", "LogException");
    }

    [TestMethod]
    public void FineDebit_PersistsSucceededRejectedAndUnknownOutcomes()
    {
        Type resultType = GetNestedType("JusticeCashWriteResult");
        CollectionAssert.AreEquivalent(
            new[] { "Unknown", "Succeeded", "Rejected" },
            Enum.GetNames(resultType));

        MethodInfo compatibility = ScriptType.GetMethod(
            "IsJusticeFineSentenceCompatibleWithCashWriteResult",
            PrivateStatic);
        Assert.IsNotNull(compatibility);
        object succeeded = Enum.Parse(resultType, "Succeeded");
        object rejected = Enum.Parse(resultType, "Rejected");
        object unknown = Enum.Parse(resultType, "Unknown");
        Assert.IsTrue((bool)compatibility.Invoke(null, new[] { succeeded, (object)120, 120, 300 }));
        Assert.IsFalse((bool)compatibility.Invoke(null, new[] { succeeded, (object)300, 120, 300 }));
        Assert.IsTrue((bool)compatibility.Invoke(null, new[] { rejected, (object)300, 120, 300 }));
        Assert.IsFalse((bool)compatibility.Invoke(null, new[] { rejected, (object)120, 120, 300 }));
        Assert.IsTrue((bool)compatibility.Invoke(null, new[] { unknown, (object)120, 120, 300 }));
        Assert.IsTrue((bool)compatibility.Invoke(null, new[] { unknown, (object)300, 120, 300 }));

        string source = ReadCustodySource();
        string resume = ExtractMethodBody(source, "ResumeJusticeFineDebitIntent");
        AssertOrdered(
            resume,
            "TryArmJusticeFinancialAttempt(",
            "intent.DebitAttempted = true",
            "intent.CashWriteResult = JusticeCashWriteResult.Unknown",
            "if (!attemptWasAlreadyDurable)",
            "intent.CashWriteResult = TryWriteJusticeSinglePlayerCash",
            "JusticeFlushStateNow()");
        StringAssert.Contains(resume, "JusticeCashWriteResult.Succeeded");
        StringAssert.Contains(resume, "finalSentence = intent.SentenceIfDebited");
        StringAssert.Contains(resume, "JusticeCashWriteResult.Rejected");
        StringAssert.Contains(resume, "finalSentence = intent.SentenceIfConverted");
        StringAssert.Contains(source, "writer.WriteAttributeString(\"cashWriteResult\"");
        StringAssert.Contains(source, "TryReadJusticeCashWriteResult");
    }

    [TestMethod]
    public void Discipline_RequiresDamageEvidenceInsteadOfCombatState()
    {
        string misconduct = ExtractMethodBody(
            ReadCustodySource(),
            "TryGetJusticeCustodyMisconduct");
        Assert.IsFalse(misconduct.Contains("IsInCombatAgainst"));
        Assert.IsFalse(misconduct.Contains("player.IsShooting"));
        Assert.IsFalse(misconduct.Contains("player.IsInMeleeCombat"));
        AssertOrdered(
            misconduct,
            "TryCaptureJusticeDamageFront(guard, player)",
            "guard.IsDead && IsJusticeDeathAttributedTo",
            "if (damagedByPlayer)",
            "JusticeCrimeKind.AssaultOfficer",
            "TryCaptureJusticeDamageFront(player, inmate)",
            "RememberJusticeCustodyAggressor(inmate)",
            "TryCaptureJusticeDamageFront(inmate, player)",
            "inmate.IsDead && IsJusticeDeathAttributedTo",
            "if (damagedByPlayer)",
            "HasFreshJusticeCustodyAggression(inmate, canUseUnarmedCombat)",
            "JusticeCrimeKind.SimpleAssault");
    }

    [TestMethod]
    public void CustodySelfDefense_AllowsOnlyFreshNonLethalVerifiedUnarmedResponse()
    {
        MethodInfo helper = ScriptType.GetMethod(
            "IsJusticeCustodySelfDefenseWindowActive",
            PrivateStatic);
        Assert.IsNotNull(helper);

        Assert.IsTrue((bool)helper.Invoke(null, new object[] { 1000L, 9000L, false, true }));
        Assert.IsFalse((bool)helper.Invoke(null, new object[] { 9000L, 9000L, false, true }));
        Assert.IsFalse((bool)helper.Invoke(null, new object[] { 1000L, 9000L, true, true }));
        Assert.IsFalse((bool)helper.Invoke(null, new object[] { 1000L, 9000L, false, false }));
        Assert.IsFalse((bool)helper.Invoke(null, new object[] { -1L, 9000L, false, true }));
    }

    [TestMethod]
    public void CustodyRespawn_CustomPedRequiresTheSameProvenCanonicalProfile()
    {
        Assert.IsTrue(JusticePolicy.CanRebindCustodyRespawnSlot(1, 1, 1, 1, false));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyRespawnSlot(1, 2, 1, 1, true));
        Assert.IsTrue(JusticePolicy.CanRebindCustodyRespawnSlot(1, -1, 1, 1, true));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyRespawnSlot(1, -1, 1, 1, false));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyRespawnSlot(1, -1, 0, 1, true));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyRespawnSlot(1, -1, 1, 0, true));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyRespawnSlot(-1, -1, 1, 1, true));
        Assert.IsTrue(JusticePolicy.CanRebindCustodyFineIntentSlot(1, 1, 1));
        Assert.IsTrue(JusticePolicy.CanRebindCustodyFineIntentSlot(-1, 1, 1));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyFineIntentSlot(2, 1, 1));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyFineIntentSlot(-1, 0, 1));
        Assert.IsFalse(JusticePolicy.CanRebindCustodyFineIntentSlot(-2, 1, 1));

        string rebind = ExtractMethodBody(
            ReadCustodySource(),
            "TryRebindJusticeCustodyIdentityAfterRespawn");
        AssertOrdered(
            rebind,
            "int currentSlot = GetCurrentSinglePlayerCashSlotSafe()",
            "JusticePolicy.CanRebindCustodyFineIntentSlot(",
            "JusticePolicy.CanRebindCustodyRespawnSlot(",
            "_justiceCustodyPlayerHandle = player.Handle");
    }

    [TestMethod]
    public void CustodyRespawn_ReturnsAnExistingSentenceToItsCellWithoutReapplyingIt()
    {
        Assert.IsFalse(JusticePolicy.ShouldReturnCustodyTransferToCell(JusticePhase.Captured));
        Assert.IsFalse(JusticePolicy.ShouldReturnCustodyTransferToCell(JusticePhase.Transporting));
        Assert.IsTrue(JusticePolicy.ShouldReturnCustodyTransferToCell(JusticePhase.Incarcerated));
        Assert.IsTrue(JusticePolicy.ShouldReturnCustodyTransferToCell(JusticePhase.Escaping));

        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Incarcerated,
            SentenceSeconds = 720,
            CustodyEpisodeId = "custody:respawn"
        };
        JusticeTickInput duplicateCompletion = new JusticeTickInput
        {
            EpisodeId = state.CustodyEpisodeId,
            Signals = JusticeSignal.TransferCompleted
        };

        JusticeTransition first = JusticePolicy.Transition(state, duplicateCompletion);
        JusticeTransition second = JusticePolicy.Transition(state, duplicateCompletion);

        Assert.AreEqual(JusticePhase.Incarcerated, first.NextPhase);
        Assert.AreEqual(JusticePhase.Incarcerated, second.NextPhase);
        Assert.IsNull(first.Operation);
        Assert.IsNull(second.Operation);
        Assert.AreEqual(720, state.SentenceSeconds);

        string transfer = ExtractMethodBody(ReadCustodySource(), "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "ShouldReturnCustodyTransferToCell",
            "transferPosition",
            "StoreJusticeCustodyPlayerState(player)",
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)",
            "IsJusticeTeleportVerified(player, transferPosition",
            "TryJusticeEmergencyTeleport(");
        Assert.IsFalse(transfer.Contains("_justiceCaseState.SentenceSeconds ="));
    }

    [TestMethod]
    public void CustodyWeaponLock_AllowsFistsOnlyAfterVerifiedConfiscation()
    {
        Assert.IsFalse(JusticePolicy.CanUseCustodyUnarmedCombat(false, false));
        Assert.IsFalse(JusticePolicy.CanUseCustodyUnarmedCombat(false, true));
        Assert.IsFalse(JusticePolicy.CanUseCustodyUnarmedCombat(true, true));
        Assert.IsTrue(JusticePolicy.CanUseCustodyUnarmedCombat(true, false));

        string weaponLock = ExtractMethodBody(
            ReadCustodySource(),
            "EnforceJusticeCustodyWeaponLock");
        AssertOrdered(
            weaponLock,
            "CanUseCustodyUnarmedCombat",
            "if (!canUseUnarmedCombat)",
            "GtaControl.Attack",
            "GtaControl.Aim",
            "GtaControl.SelectWeapon",
            "GtaControl.Reload",
            "SelectJusticeUnarmedSafe(player)");
        Assert.AreEqual(1, CountOccurrences(weaponLock, "GtaControl.Attack"));
        Assert.AreEqual(1, CountOccurrences(weaponLock, "GtaControl.SelectWeapon"));
    }

    [TestMethod]
    public void EscapeContract_UsesExactlyThreeAsItsMinimumWantedLevel()
    {
        Assert.AreEqual(3, JusticePolicy.EscapeMinimumWantedLevel);

        string runtime = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));
        string escape = ExtractMethodBody(runtime, "JusticeRegisterEscape")
            .Replace(" ", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
        string retry = ExtractMethodBody(runtime, "RetryJusticeEscapeWantedMinimum")
            .Replace(" ", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
        StringAssert.Contains(
            retry,
            "SetJusticeWantedMinimum(JusticePolicy.EscapeMinimumWantedLevel)");
        StringAssert.Contains(escape, "EscapeWantedMinimumPending=true");
        StringAssert.Contains(escape, "3étoilesdemandée");
    }

    [TestMethod]
    public void MissionRow_IsPromotedToBolingbrokeAtFiveMinutes()
    {
        MethodInfo selector = ScriptType.GetMethod(
            "GetJusticeCustodySiteForSentence",
            PrivateStatic);
        Assert.IsNotNull(selector);
        Assert.AreEqual("MissionRow", selector.Invoke(null, new object[] { 299 }).ToString());
        Assert.AreEqual("Bolingbroke", selector.Invoke(null, new object[] { 300 }).ToString());

        string source = ReadCustodySource();
        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "if (!JusticeCustodyCanMutateWorld(player))",
            "ScheduleJusticeBolingbrokeTransferIfRequired(now)",
            "MaintainJusticeCustodyPoliceSuppression(player, now)");

        string promotion = ExtractMethodBody(
            source,
            "ScheduleJusticeBolingbrokeTransferIfRequired");
        AssertOrdered(
            promotion,
            "_justiceCaseState.SentenceSeconds < JusticeCustodyPrisonThresholdSeconds",
            "_justiceCustodySite = JusticeCustodySite.Bolingbroke",
            "_justiceCaseState.Phase = JusticePhase.Transporting",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "CleanupJusticeCustodySceneEntitiesAndGroups()");
        Assert.IsFalse(promotion.Contains("CleanupJusticeCustodyEntitiesAndGroups()"));
    }

    [TestMethod]
    public void PoliceSuppression_IsGatedAndItsTwoRestoreTokensArePersisted()
    {
        string source = ReadCustodySource();
        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "if (!JusticeCustodyCanMutateWorld(player))",
            "MaintainJusticeCustodyPoliceSuppression(player, now)");

        string suppression = ExtractMethodBody(
            source,
            "SetJusticeCustodyPoliceSuppression");
        AssertOrdered(
            suppression,
            "_justicePoliceIgnoreApplied = true",
            "_justicePoliceDispatchDisabled = true",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "JusticeNativeSetPoliceIgnorePlayer",
            "JusticeNativeSetDispatchCopsForPlayer");
        StringAssert.Contains(source, "\"policeSuppressionApplied\"");
        StringAssert.Contains(source, "\"policeDispatchDisabled\"");
        StringAssert.Contains(
            ExtractMethodBody(source, "JusticeReadCustodyXml"),
            "_justicePoliceSuppressionRestorePending");
    }

    [TestMethod]
    public void CustodyMobility_IsVerifiedAfterTeleportAndRepairedBeforeSentenceProgress()
    {
        string source = ReadCustodySource();
        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");
        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        string mobility = ExtractMethodBody(source, "EnsureJusticeCustodyPlayerMobility");

        AssertOrdered(
            transfer,
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)",
            "IsJusticeTeleportVerified(player, transferPosition, 8.0f)",
            "TryJusticeEmergencyTeleport(",
            "if (transferred && !EnsureJusticeCustodyPlayerMobility(player))",
            "if (!transferred)",
            "_justiceCustodyTransferPending = false");
        AssertOrdered(
            update,
            "if (!JusticeCustodyCanMutateWorld(player))",
            "if (!EnsureJusticeCustodyPlayerMobility(player))",
            "ScheduleJusticeBolingbrokeTransferIfRequired(now)",
            "AdvanceJusticeCustodyClock(now)");
        AssertOrdered(
            mobility,
            "IsJusticeCustodyPlayerIdentityCompatible(player)",
            "player.FreezePosition = false",
            "if (player.FreezePosition)",
            "_justiceCustodyStoredFrozen = false",
            "JusticeMarkStateDirty()",
            "return true");
        Assert.IsFalse(mobility.Contains("IsInvincible ="));
        Assert.IsFalse(mobility.Contains("CanRagdoll ="));
    }

    [TestMethod]
    public void CustodyTransfer_AllFailureStagesReachTheSameTimeoutRollback()
    {
        string source = ReadCustodySource();
        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");
        string failure = ExtractMethodBody(source, "HandleJusticeCustodyTransferFailure");

        Assert.AreEqual(
            4,
            CountOccurrences(transfer, "HandleJusticeCustodyTransferFailure(player, now)"),
            "Le snapshot joueur, le WAL, l'inventaire et le téléport doivent partager le même timeout.");
        Assert.IsFalse(transfer.Contains("RegisterJusticeCustodyTransferFailure(now)"));
        AssertOrdered(
            failure,
            "RegisterJusticeCustodyTransferFailure(now)",
            "JusticeCustodyTransferTimeoutMs",
            "TryRollbackJusticeCustodyTransfer(player, now)",
            "EnsureJusticeCustodyPlayerMobility(player)");
    }

    [TestMethod]
    public void CustodyTransferRollback_NeverRestoresATransitionFreezeAndClosesPursuitEpoch()
    {
        string source = ReadCustodySource();
        string rollback = ExtractMethodBody(source, "ResumeJusticeCustodyTransferRollback");
        string transientRestore = ExtractMethodBody(
            source,
            "RestoreJusticeCustodyPlayerTransientStateForRollback");

        AssertOrdered(
            rollback,
            "bool disciplineEnded = EndJusticeCustodyDiscipline(player)",
            "RestoreJusticeCustodyPlayerTransientStateForRollback(player)",
            "CleanupJusticeCustodyEntitiesAndGroups()",
            "_justicePursuitActive = false",
            "_justiceWantedEpisodeStartedAtMs = 0L");
        AssertOrdered(
            transientRestore,
            "_justiceCustodyStoredFrozen = false",
            "RestoreJusticeCustodyPlayerTransientState(player)",
            "EnsureJusticeCustodyPlayerMobility(player)");
    }

    [TestMethod]
    public void CustodyStart_StopsPlacementBeforeJusticeSnapshotsThePlayer()
    {
        string source = ReadCustodySource();
        string begin = ExtractMethodBody(source, "JusticeBeginCustodyTransfer");
        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");

        AssertOrdered(
            begin,
            "if (_justiceCaseState.SentenceSeconds <= 0)",
            "if (_placementMode)",
            "StopPlacementMode(false)",
            "_justiceCustodySite = GetJusticeCustodySiteForSentence",
            "CompleteJusticeCustodyTransfer(player, Game.GameTime)");
        AssertOrdered(
            transfer,
            "StoreJusticeCustodyPlayerState(player)",
            "PersistJusticeCriticalPrecommitRedundantly()",
            "JusticeInventoryPreparationResult inventoryPreparation",
            "EnsureJusticeInventoryReadyForCustodyTransfer(player, now)",
            "inventoryPreparation != JusticeInventoryPreparationResult.Ready",
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)");

        CollectionAssert.AreEqual(
            new[]
            {
                "None",
                "CapturePending",
                "SnapshotPersisted",
                "RemovalPending",
                "RemovedVerified",
                "UnsupportedPreserved",
                "RestorePending",
                "RestoreAmbiguous"
            },
            Enum.GetNames(GetNestedType("JusticeInventoryCustodyState")));
    }

    [TestMethod]
    public void AmbiguousInventoryRemoval_PreservesTheSnapshotForDeferredRestore()
    {
        Type removalResultType = GetNestedType("JusticeInventoryRemovalResult");
        CollectionAssert.AreEqual(
            new[] { "NotAttempted", "RemovedVerified", "EffectMayHaveApplied" },
            Enum.GetNames(removalResultType));

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        object snapshot = CreateValidatedEmptyWeaponSnapshot();
        SetField(script, "_justiceWeaponSnapshot", snapshot);
        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(GetNestedType("JusticeInventoryCustodyState"), "RemovalPending"));

        object preparationResult = Invoke(
            script,
            "RegisterJusticeInventoryRemovalFailure",
            Enum.Parse(removalResultType, "EffectMayHaveApplied"),
            1000);

        Assert.AreEqual("UnsupportedLoadout", preparationResult.ToString());
        Assert.AreSame(snapshot, GetField<object>(script, "_justiceWeaponSnapshot"));
        Assert.AreEqual(
            "RestoreAmbiguous",
            GetField<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsTrue(GetField<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.AreEqual(0, GetField<int>(script, "_justiceNextInventoryPersistenceRetryAt"));
        Assert.AreNotEqual(0, GetField<int>(script, "_justiceNextDeferredInventoryRestoreAt"));
        Assert.IsTrue((bool)Invoke(script, "ValidateJusticeInventoryCustodyStateInvariant"));

        string source = ReadCustodySource();
        string removal = ExtractMethodBody(source, "RemoveJusticePlayerWeaponsSafe");
        AssertOrdered(
            removal,
            "effectMayHaveApplied = true",
            "Function.Call(Hash.REMOVE_ALL_PED_WEAPONS",
            "VerifyJusticePlayerHasNoWeapons(player)",
            "JusticeInventoryRemovalResult.EffectMayHaveApplied");

        string fallback = ExtractMethodBody(
            source,
            "EnterJusticeNonDestructiveCustodyFallback");
        AssertOrdered(
            fallback,
            "bool ambiguousRestorePending",
            "if (!ambiguousRestorePending)",
            "JusticeInventoryCustodyState.UnsupportedPreserved");

        string reset = ExtractMethodBody(source, "ResetJusticeCustodyPersistentFields");
        AssertOrdered(
            reset,
            "JusticeInventoryCustodyState deferredInventoryState",
            "if (shouldPreserveDeferredRestore)",
            "_justiceWeaponSnapshot = deferredSnapshot",
            "_justiceInventoryCustodyState = deferredInventoryState");
    }

#if DONJ_STUB_API
    [TestMethod]
    public void RemoveAllAppliedButPostCheckFails_ReturnsAmbiguousInsteadOfPreserved()
    {
        StubRuntime.Reset();
        bool removeAllWasApplied = false;
        StubRuntime.NativeCallHandler = (hash, arguments) =>
        {
            if (hash == (ulong)Hash.REMOVE_ALL_PED_WEAPONS)
            {
                removeAllWasApplied = true;
                return null;
            }
            if (hash == (ulong)Hash.HAS_PED_GOT_WEAPON)
            {
                // Je simule une lecture GTA obsolète après un effet réellement appliqué.
                return true;
            }

            return null;
        };

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        object snapshot = CreateValidatedEmptyWeaponSnapshot();
        SetField(script, "_justiceWeaponSnapshot", snapshot);
        Ped player = new Ped { Handle = 91 };
        object removalResult = Invoke(
            script,
            "RemoveJusticePlayerWeaponsSafe",
            player);

        Assert.IsTrue(removeAllWasApplied);
        Assert.AreEqual("EffectMayHaveApplied", removalResult.ToString());

        Invoke(script, "RegisterJusticeInventoryRemovalFailure", removalResult, 2000);

        Assert.AreSame(snapshot, GetField<object>(script, "_justiceWeaponSnapshot"));
        Assert.AreEqual(
            "RestoreAmbiguous",
            GetField<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsTrue(GetField<bool>(script, "_justiceDeferredInventoryRestore"));
        Assert.IsFalse(GetField<bool>(script, "_justiceInventoryRemoved"));
    }
#endif

    [TestMethod]
    public void DeferredInventoryRestore_PreservesOnlyTheSameSessionCustomPedHandle()
    {
        string source = ReadCustodySource();
        string reset = ExtractMethodBody(source, "ResetJusticeCustodyPersistentFields");
        string writer = ExtractMethodBody(source, "JusticeWriteCustodyXml");

        AssertOrdered(
            reset,
            "int deferredPlayerHandle = _justiceCustodyPlayerHandle",
            "_justiceCustodyPlayerHandle = 0",
            "if (shouldPreserveDeferredRestore)",
            "_justiceCustodyPlayerHandle = deferredPlayerHandle");
        Assert.IsFalse(
            writer.Contains("playerHandle"),
            "Un handle GTA ne doit jamais survivre à un reload XML.");
    }

    [TestMethod]
    public void CustodyOwnedPeds_RequireTheirSpawnGenerationForUseAndDeletion()
    {
        MethodInfo compatibility = ScriptType.GetMethod(
            "IsJusticeCustodyPedGenerationCompatible",
            PrivateStatic);
        Assert.IsNotNull(compatibility);
        Assert.IsTrue((bool)compatibility.Invoke(null, new object[] { 12, 12 }));
        Assert.IsFalse((bool)compatibility.Invoke(null, new object[] { 12, 13 }));
        Assert.IsFalse((bool)compatibility.Invoke(null, new object[] { 0, 0 }));

        string source = ReadCustodySource();
        string scene = ExtractMethodBody(source, "EnsureJusticeCustodyScene");
        string membership = ExtractMethodBody(source, "JusticeCustodyListContainsPed");
        string compaction = ExtractMethodBody(source, "CompactJusticeCustodyPedList");
        string deletion = ExtractMethodBody(source, "DeleteJusticeCustodyPedList");

        AssertOrdered(
            scene,
            "RememberJusticeCustodyPedOwnership(guard)",
            "_justiceCustodyGuards.Add(guard)",
            "RememberJusticeCustodyPedOwnership(inmate)",
            "_justiceCustodyInmates.Add(inmate)");
        StringAssert.Contains(membership, "IsJusticeCustodyPedOwnershipValid(ped)");
        StringAssert.Contains(membership, "IsJusticeCustodyPedGenerationCompatible");
        AssertOrdered(
            compaction,
            "bool ownedPed = IsJusticeCustodyPedOwnershipValid(ped)",
            "if (ownedPed)",
            "DeleteEntitySafe(ped)");
        AssertOrdered(
            deletion,
            "IsJusticeCustodyPedOwnershipValid(ped)",
            "DeleteEntitySafe(ped)");
    }

    [TestMethod]
    public void CustodyDiscipline_ForcesAFinalEvidenceScanBeforeExitOrSceneCompaction()
    {
        string source = ReadCustodySource();
        string update = ExtractMethodBody(source, "UpdateJusticeCustodyDiscipline");
        string finalizer = ExtractMethodBody(
            source,
            "FinalizeJusticePendingDisciplineBeforeCustodyExit");
        string scene = ExtractMethodBody(source, "EnsureJusticeCustodyScene");

        AssertOrdered(
            update,
            "_justiceNextDisciplineScanAt = JusticeCustodyFutureTime",
            "TryGetJusticeCustodyMisconduct(player, out crimeKind)",
            "bool homicide",
            "BeginJusticeCustodyDiscipline(player, now, crimeKind)");
        AssertOrdered(
            finalizer,
            "TryBeginJusticeCustodyDisciplineFromCurrentEvidence(player, now)",
            "if (_justiceDisciplineIntent == null)");
        AssertOrdered(
            scene,
            "_justiceDisciplineIntent != null || _justiceDisciplineActive",
            "TryBeginJusticeCustodyDisciplineFromCurrentEvidence(player, now)",
            "CompactJusticeCustodyPedList(_justiceCustodyGuards)",
            "CompactJusticeCustodyPedList(_justiceCustodyInmates)");
    }

    [TestMethod]
    public void CustodyProfileSwitch_ParksWorldEffectsAndDefersReleaseToTheReturningHero()
    {
        string source = ReadCustodySource();
        string parking = ExtractMethodBody(
            source,
            "TryPrepareJusticeCustodyForProfileSwitch");
        AssertOrdered(
            parking,
            "CanParkCurrentJusticeCustodyForProfileSwitch()",
            "ApplyLoadedJusticeActivityCooldowns(now)",
            "SetJusticeCustodyPoliceSuppression(false)",
            "_justicePoliceSuppressionRestorePending",
            "if (!canPark)",
            "CancelJusticeCustodyActivity(false, now)",
            "CleanupJusticeCustodySceneEntitiesAndGroups()",
            "ResetJusticeCustodyClock(now)");
        Assert.IsFalse(parking.Contains("RestoreJusticeInventory"));
        Assert.IsFalse(parking.Contains("RestoreJusticeCustodyPlayerTransientState"));

        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "_justiceCaseState.SentenceSeconds <= 0",
            "CompleteJusticeLegalRelease(player)",
            "RestoreJusticeCustodyRuntimeFromCase()");

        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "bool resumingCustody = _justiceCustodyResumePending",
            "TryClearJusticeCustodyPlayerTasks(player, now)",
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)",
            "_justiceCustodyResumePending = false");

        string release = ExtractMethodBody(source, "ResumeJusticeLegalReleaseFinalization");
        AssertOrdered(
            release,
            "IsJusticeLegalReleasePrecommitState()",
            "TryClearJusticeCustodyPlayerTasks(player, now)",
            "RestoreJusticeInventoryForLegalRelease(player, now)",
            "ResetJusticeCustodyPersistentFields()");
    }

    private static Type GetNestedType(string name)
    {
        Type type = ScriptType.GetNestedType(name, BindingFlags.NonPublic);
        Assert.IsNotNull(type, "Type privé introuvable : " + name);
        return type;
    }

    private static object Invoke(object instance, string methodName, params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateInstance);
        Assert.IsNotNull(method, "Méthode privée introuvable : " + methodName);
        return method.Invoke(instance, arguments);
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(instance, value);
    }

    private static T GetField<T>(object instance, string fieldName)
    {
        FieldInfo field = ScriptType.GetField(fieldName, PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        return (T)field.GetValue(instance);
    }

    private static object CreateValidatedEmptyWeaponSnapshot()
    {
        Type snapshotType = GetNestedType("JusticeWeaponSnapshot");
        object snapshot = Activator.CreateInstance(snapshotType, true);
        FieldInfo validated = snapshotType.GetField(
            "IsValidated",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(validated, "Marqueur de validation du snapshot introuvable.");
        validated.SetValue(snapshot, true);
        return snapshot;
    }

    private static string ReadCustodySource()
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.Custody.cs"));
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        string marker = methodName + "(";
        int nameIndex = -1;
        int searchAt = 0;
        while (searchAt < source.Length)
        {
            int candidate = source.IndexOf(marker, searchAt, StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string declarationPrefix = source.Substring(lineStart, candidate - lineStart);
            if (declarationPrefix.Contains("private "))
            {
                nameIndex = candidate;
                break;
            }
            searchAt = candidate + marker.Length;
        }

        Assert.IsTrue(nameIndex >= 0, "Méthode source introuvable : " + methodName);
        int openingBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openingBrace >= 0, "Corps source introuvable : " + methodName);
        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0)
            {
                return source.Substring(openingBrace, index - openingBrace + 1);
            }
        }

        Assert.Fail("Corps source non fermé : " + methodName);
        return string.Empty;
    }

    private static int CountOccurrences(string source, string marker)
    {
        int count = 0;
        int position = 0;
        while ((position = source.IndexOf(marker, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += marker.Length;
        }
        return count;
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        int previous = -1;
        foreach (string marker in markers)
        {
            int current = source.IndexOf(marker, previous + 1, StringComparison.Ordinal);
            Assert.IsTrue(current > previous, "Ordre invalide ou marqueur absent : " + marker);
            previous = current;
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
        {
            current = current.Parent;
        }

        Assert.IsNotNull(current, "Racine du dépôt introuvable.");
        return current.FullName;
    }
}
