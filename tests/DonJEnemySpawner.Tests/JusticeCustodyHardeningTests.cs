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
            4,
            CountOccurrences(shutdown, "RunJusticeCustodyShutdownStep("),
            "Chaque domaine de nettoyage doit être isolé, police comprise dans le finally.");
        AssertOrdered(
            shutdown,
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
    public void CustodyMisconduct_UsesOnlyTheBoundedOwnedGuardRetaliationPath()
    {
        string source = ReadCustodySource();
        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        string escape = ExtractMethodBody(source, "UpdateJusticeCustodyEscape");

        Assert.IsFalse(source.Contains("UpdateJusticeCustodyDiscipline"));
        Assert.IsFalse(source.Contains("BeginJusticeCustodyDiscipline"));
        Assert.IsFalse(source.Contains("CompleteJusticeCustodyDiscipline"));
        Assert.IsFalse(source.Contains("JusticeRegisterCustodyDisciplineCharge"));
        string retaliation = ExtractMethodBody(
            source,
            "CommandJusticeCustodyGuardCombatIfDue");
        StringAssert.Contains(retaliation, "Hash.TASK_COMBAT_PED");
        StringAssert.Contains(retaliation, "JusticeCustodyGuardCombatRetryMs");
        Assert.IsFalse(update.Contains("TryAcquirePlayerInvincibility"));
        AssertOrdered(
            update,
            "MaintainJusticeCustodyPoliceSuppression(player, now)",
            "UpdateJusticeCustodyEscape(player, now)",
            "AdvanceJusticeCustodyClock(now)",
            "EnsureJusticeCustodyScene(now)");
        Assert.IsFalse(escape.Contains("TeleportPlayerWithFadeSafe"));
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
    public void PoliceDeathRespawnIdentity_AcceptsTheSameCanonicalSlotOrTheExactCustomModel()
    {
        const int ownerSlot = 0;
        const int customModel = 0x123456;
        const int canonicalModel = 0x654321;

        // Je reconnais le protagoniste canonique par son slot, même si GTA lui
        // rend son modèle d'origine après une mort survenue sous un ped custom.
        Assert.IsTrue(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            ownerSlot,
            canonicalModel,
            ownerSlot,
            customModel));

        // Je refuse toujours un autre protagoniste canonique et je conserve la
        // preuve forte du modèle exact lorsqu'aucun slot GTA n'est disponible.
        Assert.IsFalse(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            1,
            canonicalModel,
            ownerSlot,
            customModel));
        Assert.IsTrue(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            -1,
            customModel,
            ownerSlot,
            customModel));
        Assert.IsFalse(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            -1,
            canonicalModel,
            ownerSlot,
            customModel));
        Assert.IsFalse(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            ownerSlot,
            0,
            ownerSlot,
            customModel));
        Assert.IsFalse(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            ownerSlot,
            canonicalModel,
            -1,
            customModel));
        Assert.IsFalse(JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(
            ownerSlot,
            canonicalModel,
            ownerSlot,
            0));

        string source = ReadCustodySource();
        string pendingWalResolver = ExtractMethodBody(
            source,
            "TryResolveJusticePendingWalPoliceDeathHoldingIntent");
        string holdingCompatibility = ExtractMethodBody(
            source,
            "IsJusticePoliceDeathPreJudgmentHoldingOwnerCompatible");
        string respawnMaskCompatibility = ExtractMethodBody(
            source,
            "CanMaskJusticePoliceDeathRespawnOrigin");
        StringAssert.Contains(
            pendingWalResolver,
            "JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(");
        StringAssert.Contains(
            holdingCompatibility,
            "JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(");
        StringAssert.Contains(
            respawnMaskCompatibility,
            "JusticePolicy.IsPoliceDeathRespawnIdentityCompatible(");
        Assert.IsFalse(
            pendingWalResolver.Contains("currentIdentityExact"),
            "Le resolver WAL ne doit plus imposer le modèle custom à un slot canonique prouvé.");
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
            SentenceSeconds = 540,
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
        Assert.AreEqual(540, state.SentenceSeconds);

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
    public void CustodyWeaponLock_BlocksAnAmbiguousPartialRemovalOnlyDuringCustody()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Type inventoryStateType = GetNestedType("JusticeInventoryCustodyState");
        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(inventoryStateType, "RestoreAmbiguous"));
        SetField(script, "_justiceWeaponSnapshot", CreateValidatedEmptyWeaponSnapshot());
        SetField(script, "_justiceDeferredInventoryRestore", true);
        SetField(script, "_justiceInventoryRemoved", false);
        SetField(script, "_justiceWeaponControlsLocked", false);

        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Incarcerated,
            SentenceSeconds = 120,
            CustodyEpisodeId = "custody:ambiguous-lock"
        };
        SetField(script, "_justiceCaseState", state);

        Assert.IsTrue(
            (bool)Invoke(script, "ShouldEnforceJusticeCustodyWeaponLock"),
            "Une arme potentiellement restée après RemoveAll doit être inutilisable en prison.");

        state.Phase = JusticePhase.AtLarge;
        Assert.IsFalse(
            (bool)Invoke(script, "ShouldEnforceJusticeCustodyWeaponLock"),
            "Le snapshot différé ne doit jamais verrouiller les contrôles après la libération.");

        SetField(script, "_justiceInventoryRemoved", true);
        Assert.IsTrue(
            (bool)Invoke(script, "ShouldEnforceJusticeCustodyWeaponLock"),
            "Une confiscation vérifiée conserve le verrou de sélection d'arme.");

        string weaponLock = ExtractMethodBody(
            ReadCustodySource(),
            "EnforceJusticeCustodyWeaponLock");
        string weaponLockPredicate = ExtractMethodBody(
            ReadCustodySource(),
            "ShouldEnforceJusticeCustodyWeaponLock");
        AssertOrdered(
            weaponLock,
            "ShouldEnforceJusticeCustodyWeaponLock()",
            "CanUseCustodyUnarmedCombat");
        Assert.IsFalse(
            weaponLockPredicate.Contains("ValidateJusticeWeaponSnapshot"),
            "Le verrou exécuté à chaque frame doit rester O(1), sans HashSet ni validation profonde.");
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
            "GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <",
            "JusticeCustodyPrisonThresholdSeconds",
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
            "(!CompleteJusticePreJudgmentHoldingStreamingProtection(player) ||",
            "!EnsureJusticeCustodyPlayerMobility(player))",
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
    public void CustodyTransfer_AllFailureStagesKeepCustodyPendingAndRetry()
    {
        string source = ReadCustodySource();
        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");
        string failure = ExtractMethodBody(source, "HandleJusticeCustodyTransferFailure");

        Assert.AreEqual(
            6,
            CountOccurrences(transfer, "HandleJusticeCustodyTransferFailure(player, now)"),
            "Le snapshot joueur, les deux reprises WAL, le fallback durable, l'inventaire et le téléport doivent partager le même retry.");
        Assert.IsFalse(transfer.Contains("RegisterJusticeCustodyTransferFailure(now)"));
        AssertOrdered(
            failure,
            "RegisterJusticeCustodyTransferFailure(now)",
            "EnsureJusticeCustodyPlayerMobility(player)");
        Assert.IsFalse(
            failure.Contains("TryRollbackJusticeCustodyTransfer"),
            "Une panne technique ne doit jamais libérer le détenu sous mandat.");
        Assert.IsFalse(failure.Contains("JusticePhase.AtLarge"));
        Assert.IsFalse(failure.Contains("_justiceCustodyTransferPrecommitConfirmed"));
        Assert.IsFalse(failure.Contains("_justiceCustodyFallbackPrecommitPending"));
        Assert.AreEqual(
            1,
            CountOccurrences(source, "TryRollbackJusticeCustodyTransfer("),
            "Le rollback historique ne doit plus avoir aucun appelant runtime.");
        AssertOrdered(
            transfer,
            "if (!_justiceCustodyTransferPrecommitConfirmed)",
            "PersistJusticeCriticalPrecommitRedundantly(",
            "\"CompleteJusticeCustodyTransfer\"",
            "_justiceCustodyTransferPrecommitConfirmed = true",
            "if (_justiceCustodyFallbackPrecommitPending)",
            "// Je reprends exactement la frontière du fallback",
            "_justiceCustodyFallbackPrecommitPending = false",
            "JusticeInventoryPreparationResult inventoryPreparation");
        AssertOrdered(
            transfer,
            "EnsureJusticeInventoryReadyForCustodyTransfer(player, now)",
            "CanContinueJusticeCustodyTransferWithoutInventoryConfiscation(",
            "EnterJusticeNonDestructiveCustodyFallback(player, now)",
            "_justiceCustodyFallbackPrecommitPending = true",
            "\"CompleteJusticeCustodyTransfer\"",
            "_justiceCustodyFallbackPrecommitPending = false",
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)");

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        SetField(script, "_justiceCustodyFallbackPrecommitPending", true);
        Invoke(script, "ResetJusticeCustodyTransferRetryState");
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyTransferPrecommitConfirmed"));
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyFallbackPrecommitPending"));
    }

    [TestMethod]
    public void CustodyTransferTimeout_KeepsTheSentenceAndSchedulesAnotherBoundedAttempt()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Transporting,
            CustodyEpisodeId = "custody:timeout"
        };
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceEnabled", true);
        SetField(script, "_justiceCustodyRuntimeActive", true);
        SetField(script, "_justiceCustodyTransferPending", true);
        SetField(script, "_justiceCustodyTransferStartedAt", 1);
        SetField(script, "_justiceNextCustodyTransferAttemptAt", 0);
        SetField(script, "_justiceCustodyTransferFailureCount", 0);
        SetField(script, "_justiceCustodyTransferTimeoutLogged", false);
        SetField(script, "_justiceCustodyTransferPrecommitConfirmed", true);
        SetField(script, "_justiceCustodyFallbackPrecommitPending", true);

        const int now = 30001;
        Invoke(script, "HandleJusticeCustodyTransferFailure", null, now);

        Assert.AreEqual(JusticePhase.Transporting, state.Phase);
        Assert.AreEqual("custody:timeout", state.CustodyEpisodeId);
        Assert.AreEqual(0, state.CompletedOperationIds.Count,
            "Le timeout ne doit créer aucune opération de remise en liberté.");
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPending"));
        Assert.AreEqual(1, GetField<int>(script, "_justiceCustodyTransferFailureCount"));
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferTimeoutLogged"));
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyTransferPrecommitConfirmed"));
        Assert.IsTrue(GetField<bool>(script, "_justiceCustodyFallbackPrecommitPending"));
        int retryDelay = unchecked(
            GetField<int>(script, "_justiceNextCustodyTransferAttemptAt") - now);
        Assert.IsTrue(retryDelay > 0 && retryDelay <= 5000,
            "Le retry doit rester cadencé et borné à cinq secondes.");
    }

    [TestMethod]
    public void InventorySnapshotFailure_ImmediatelyUsesTheSafePreservedFallback()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Type preparationType = GetNestedType("JusticeInventoryPreparationResult");
        Type inventoryStateType = GetNestedType("JusticeInventoryCustodyState");
        object retryableFailure = Enum.Parse(preparationType, "RetryableFailure");

        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(inventoryStateType, "CapturePending"));
        SetField(script, "_justiceWeaponSnapshot", null);
        SetField(script, "_justiceInventoryRemoved", false);
        SetField(script, "_justiceWeaponControlsLocked", false);
        SetField(script, "_justiceDeferredInventoryRestore", false);

        Assert.IsTrue((bool)Invoke(
            script,
            "CanContinueJusticeCustodyTransferWithoutInventoryConfiscation",
            retryableFailure));

        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(inventoryStateType, "RemovalPending"));
        Assert.IsFalse((bool)Invoke(
            script,
            "CanContinueJusticeCustodyTransferWithoutInventoryConfiscation",
            retryableFailure),
            "Un retrait déjà engagé doit rester dans le chemin de retry durable.");

        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(inventoryStateType, "UnsupportedPreserved"));
        object preservedPreparation = Invoke(
            script,
            "EnsureJusticeInventoryReadyForCustodyTransfer",
            null,
            1000);
        Assert.AreEqual("Ready", preservedPreparation.ToString(),
            "Un fallback préservé rechargé ne doit pas relancer le snapshot.");

        object snapshot = CreateValidatedEmptyWeaponSnapshot();
        SetField(
            script,
            "_justiceInventoryCustodyState",
            Enum.Parse(inventoryStateType, "RestoreAmbiguous"));
        SetField(script, "_justiceWeaponSnapshot", snapshot);
        SetField(script, "_justiceDeferredInventoryRestore", true);
        object ambiguousPreparation = Invoke(
            script,
            "EnsureJusticeInventoryReadyForCustodyTransfer",
            null,
            1000);
        Assert.AreEqual("Ready", ambiguousPreparation.ToString(),
            "Un état ambigu durable ne doit jamais rejouer RemoveAll.");

        JusticeCaseState custodyState = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Transporting,
            CustodyEpisodeId = "custody:ambiguous"
        };
        SetField(script, "_justiceCaseState", custodyState);
        SetField(script, "_justiceCustodyPlayerHandle", 42);
        SetField(script, "_justiceCustodyPlayerModelHash", 84);
        SetField(script, "_justiceCustodyPlayerSlot", 1);
        SetField(script, "_justiceNextDeferredInventoryRestoreAt", 0);
        Invoke(script, "RetryJusticeDeferredInventoryRestore", null, 1000);
        Assert.AreEqual(42, GetField<int>(script, "_justiceCustodyPlayerHandle"));
        Assert.AreEqual(84, GetField<int>(script, "_justiceCustodyPlayerModelHash"));
        Assert.AreEqual(1, GetField<int>(script, "_justiceCustodyPlayerSlot"));
        Assert.IsTrue(GetField<bool>(script, "_justiceDeferredInventoryRestore"),
            "La restitution ambiguë doit attendre la libération réelle.");

        string source = ReadCustodySource();
        string fallback = ExtractMethodBody(
            source,
            "EnterJusticeNonDestructiveCustodyFallback");
        Assert.IsFalse(fallback.Contains("TryRollbackJusticeCustodyTransfer"));
        StringAssert.Contains(fallback, "détention maintenue");

        string ensure = ExtractMethodBody(
            source,
            "EnsureJusticeInventoryReadyForCustodyTransfer");
        AssertOrdered(
            ensure,
            "bool preservedInventoryReady",
            "bool ambiguousInventoryReady",
            "if (preservedInventoryReady || ambiguousInventoryReady)",
            "return JusticeInventoryPreparationResult.Ready");

        string confiscationRetry = ExtractMethodBody(
            source,
            "RetryJusticeInventoryConfiscationIfDue");
        AssertOrdered(
            confiscationRetry,
            "JusticeInventoryCustodyState.UnsupportedPreserved",
            "JusticeInventoryCustodyState.RestoreAmbiguous",
            "JusticeInventoryCustodyState.RestorePending",
            "return JusticeInventoryPreparationResult.Ready",
            "RemoveJusticePlayerWeaponsSafe(player)");

        string deferredRestore = ExtractMethodBody(
            source,
            "RetryJusticeDeferredInventoryRestore");
        AssertOrdered(
            deferredRestore,
            "JusticeIsCustodyActive",
            "return;",
            "RestoreJusticeWeaponSnapshotMergeSafe(player, true, true)");
    }

    [TestMethod]
    public void RemovedVerifiedInventory_IsTerminalDuringTheCustodyTick()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        Type inventoryStateType = GetNestedType("JusticeInventoryCustodyState");
        object removedVerified = Enum.Parse(inventoryStateType, "RemovedVerified");
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Incarcerated,
            CustodyEpisodeId = "custody:inventory-terminal"
        };
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceWeaponSnapshot", CreateValidatedEmptyWeaponSnapshot());
        SetField(script, "_justiceInventoryCustodyState", removedVerified);
        SetField(script, "_justiceInventoryRemoved", true);
        SetField(script, "_justiceWeaponControlsLocked", false);
        SetField(script, "_justiceNextInventoryPersistenceRetryAt", 0);
        SetField(script, "_justiceStateDirty", false);

        object result = Invoke(
            script,
            "RetryJusticeInventoryConfiscationIfDue",
            null,
            1000);

        Assert.AreEqual("Ready", result.ToString());
        Assert.AreEqual(
            "RemovedVerified",
            GetField<object>(script, "_justiceInventoryCustodyState").ToString());
        Assert.IsTrue(GetField<bool>(script, "_justiceInventoryRemoved"));
        Assert.IsFalse(GetField<bool>(script, "_justiceWeaponControlsLocked"));
        Assert.IsFalse(
            GetField<bool>(script, "_justiceStateDirty"),
            "Le tick stable ne doit ni réarmer une barrière ni rejouer RemoveAll.");
    }

    [TestMethod]
    public void CustodyTransferRollback_MigratesToRetryWithoutTechnicalRelease()
    {
        string source = ReadCustodySource();
        string rollback = ExtractMethodBody(source, "ResumeJusticeCustodyTransferRollback");

        AssertOrdered(
            rollback,
            "HasJusticeCustodyOperation(JusticeOperationKind.TransferRollback)",
            "CompletedOperationIds.Remove(rollbackId)",
            "_justiceCaseState.Phase = JusticePhase.Transporting",
            "_justiceCaseState.HasWarrant = false",
            "_justiceCustodyRuntimeActive = true",
            "_justiceCustodyTransferPending = true",
            "_justiceCustodyResumePending = true",
            "ResetJusticeCustodyTransferRetryState()",
            "EnsureJusticeCustodyTransferRollbackPrecommitRedundant()",
            "_justiceCustodyTransferRollbackFinalizationPending = false");
        Assert.IsFalse(
            rollback.Contains("RestoreJusticeInventoryForLegalRelease"),
            "La migration historique ne doit jamais restituer l'inventaire d'un détenu.");
        Assert.IsFalse(
            rollback.Contains("RestoreJusticeCustodyPlayerTransientStateForRollback"),
            "La migration ne doit jamais remettre le détenu en liberté dans le monde.");
        Assert.IsFalse(
            rollback.Contains("remise en liberté technique"),
            "Le message et le comportement historiques doivent être supprimés.");
    }

    [TestMethod]
    public void CustodyStart_StopsPlacementBeforeJusticeSnapshotsThePlayer()
    {
        string source = ReadCustodySource();
        string begin = ExtractMethodBody(source, "JusticeBeginCustodyTransfer");
        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");

        AssertOrdered(
            begin,
            "if (GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L)",
            "if (HasPlacementSessionState())",
            "StopPlacementMode(false)",
            "if (_placementPlayerStateStored ||",
            "HasPlayerInvincibilityOwner(PlayerInvincibilityOwner.Placement)",
            "return;",
            "_justiceCustodySite = GetJusticeCustodySiteForSentence",
            "CompleteJusticeCustodyTransfer(player, Game.GameTime)");
        AssertOrdered(
            transfer,
            "StoreJusticeCustodyPlayerState(player)",
            "_justiceCustodyTransferPrecommitConfirmed = true",
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
            "continue;");
        Assert.IsFalse(compaction.Contains("DeleteEntitySafe(ped)"));
        AssertOrdered(
            deletion,
            "IsJusticeCustodyPedOwnershipValid(ped)",
            "DeleteEntitySafe(ped)");
    }

    [TestMethod]
    public void CustodyScene_NeverScansMisconductBeforeCompaction()
    {
        string source = ReadCustodySource();
        string scene = ExtractMethodBody(source, "EnsureJusticeCustodyScene");

        AssertOrdered(
            scene,
            "CompactJusticeCustodyPedList(_justiceCustodyGuards)",
            "CompactJusticeCustodyPedList(_justiceCustodyInmates)");
        Assert.IsFalse(scene.Contains("Discipline"));
        Assert.IsFalse(scene.Contains("Misconduct"));
    }

    [TestMethod]
    public void CustodyEscapeObservation_IsInterruptedBeforeParkingAndWhileRuntimeIsSuspended()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.Escaping,
            SentenceSeconds = 180,
            CustodyEpisodeId = "custody:escape-interruption"
        };
        SetField(script, "_justiceCaseState", state);
        SetField(script, "_justiceOutsideCustodySinceAt", 1200);
        SetField(script, "_justiceStateDirty", false);

        Invoke(script, "InterruptJusticeCustodyEscapeObservation");

        Assert.AreEqual(
            JusticePhase.Incarcerated,
            state.Phase,
            "Une interruption non observable annule la continuité de l'évasion.");
        Assert.AreEqual(0, GetField<int>(script, "_justiceOutsideCustodySinceAt"));
        Assert.IsTrue(GetField<bool>(script, "_justiceStateDirty"));
        Assert.AreEqual(
            0,
            state.CompletedOperationIds.Count,
            "L'interruption ne doit enregistrer ni évasion, ni nouvelle opération judiciaire.");

        string source = ReadCustodySource();
        string interruption = ExtractMethodBody(
            source,
            "InterruptJusticeCustodyEscapeObservation");
        AssertOrdered(
            interruption,
            "_justiceCaseState.Phase == JusticePhase.Escaping",
            "JusticeSignal.Restrained",
            "JusticeMarkStateDirty()",
            "_justiceOutsideCustodySinceAt = 0");

        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "EnforceJusticeCustodyWeaponLock(player)",
            "if (!JusticeCustodyCanMutateWorld(player))",
            "InterruptJusticeCustodyEscapeObservation()",
            "ResetJusticeCustodyClock(now)",
            "return;");
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
            "InterruptJusticeCustodyEscapeObservation()",
            "CanParkCurrentJusticeCustodyForProfileSwitch()",
            "SetJusticeCustodyPoliceSuppression(false)",
            "_justicePoliceSuppressionRestorePending",
            "if (!canPark)",
            "CleanupJusticeCustodySceneEntitiesAndGroups()",
            "ResetJusticeCustodyClock(now)");
        Assert.IsFalse(parking.Contains("RestoreJusticeInventory"));
        Assert.IsFalse(parking.Contains("RestoreJusticeCustodyPlayerTransientState"));

        string update = ExtractMethodBody(source, "JusticeUpdateCustody");
        AssertOrdered(
            update,
            "GetJusticeCustodyTotalRemainingSeconds(_justiceCaseState) <= 0L",
            "CompleteJusticeLegalRelease(player)",
            "RestoreJusticeCustodyRuntimeFromCase()");

        string transfer = ExtractMethodBody(source, "CompleteJusticeCustodyTransfer");
        AssertOrdered(
            transfer,
            "bool resumingCustody = _justiceCustodyResumePending",
            "TeleportPlayerWithFadeSafe(player, transferPosition, transferHeading)",
            "_justiceCustodyResumePending = false");

        string release = ExtractMethodBody(source, "ResumeJusticeLegalReleaseFinalization");
        AssertOrdered(
            release,
            "IsJusticeLegalReleasePrecommitState()",
            "RestoreJusticeInventoryForLegalRelease(player, now)",
            "ResetJusticeCustodyPersistentFields()");
    }

    [TestMethod]
    public void CustodyClock_ResetPreservesOnlyAValidObservedRemainder()
    {
        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justiceCustodyElapsedRemainderMs", 875);

        Invoke(script, "ResetJusticeCustodyClock", 4200);

        Assert.AreEqual(4200, GetField<int>(script, "_justiceCustodyLastTickAt"));
        Assert.AreEqual(
            875,
            GetField<int>(script, "_justiceCustodyElapsedRemainderMs"),
            "Je conserve uniquement le temps de jeu déjà observé avant un micro-gate.");

        SetField(script, "_justiceCustodyElapsedRemainderMs", -1);
        Invoke(script, "ResetJusticeCustodyClock", 4300);
        Assert.AreEqual(0, GetField<int>(script, "_justiceCustodyElapsedRemainderMs"));

        SetField(script, "_justiceCustodyElapsedRemainderMs", 1000);
        Invoke(script, "ResetJusticeCustodyClock", 4400);
        Assert.AreEqual(
            0,
            GetField<int>(script, "_justiceCustodyElapsedRemainderMs"),
            "Un reste impossible ne doit jamais créer un rattrapage de peine.");
    }

    [TestMethod]
    public void CustodyResidualMissionFlag_BypassIsObservedOnlyInsideItsWindow()
    {
        FieldInfo windowField = ScriptType.GetField(
            "JusticeCustodyResidualMissionFlagObservationWindowMs",
            PrivateStatic);
        Assert.IsNotNull(windowField);
        Assert.AreEqual(15000, (int)windowField.GetRawConstantValue());

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetField(script, "_justiceMonotonicTimeMs", 1000L);
        Invoke(script, "ArmJusticeCustodyResidualMissionFlagBypass");
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyResidualMissionFlagBypassArmed"));
        Assert.AreEqual(
            16000L,
            GetField<long>(
                script,
                "_justiceCustodyResidualMissionFlagObservationDeadlineMs"));

        SetField(script, "_justiceMonotonicTimeMs", 15999L);
        SetField(script, "_justiceRuntimeSuspendedByMissionFlagOnlyCached", true);
        Invoke(script, "UpdateJusticeCustodyResidualMissionFlagBypass", true);
        Assert.AreEqual(
            0L,
            GetField<long>(
                script,
                "_justiceCustodyResidualMissionFlagObservationDeadlineMs"),
            "Je mémorise le latch BUSTED uniquement lorsqu'il apparaît dans la fenêtre bornée.");

        Invoke(script, "UpdateJusticeCustodyResidualMissionFlagBypass", true);
        Assert.IsTrue(GetField<bool>(
            script,
            "_justiceCustodyResidualMissionFlagBypassArmed"));

        SetField(script, "_justiceRuntimeSuspendedByMissionFlagOnlyCached", false);
        Invoke(script, "UpdateJusticeCustodyResidualMissionFlagBypass", false);
        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyResidualMissionFlagBypassArmed"));

        SetField(script, "_justiceMonotonicTimeMs", 20000L);
        Invoke(script, "ArmJusticeCustodyResidualMissionFlagBypass");
        SetField(script, "_justiceMonotonicTimeMs", 35000L);
        SetField(script, "_justiceRuntimeSuspendedByMissionFlagOnlyCached", true);
        Invoke(script, "UpdateJusticeCustodyResidualMissionFlagBypass", true);

        Assert.IsFalse(GetField<bool>(
            script,
            "_justiceCustodyResidualMissionFlagBypassArmed"));
        Assert.AreEqual(
            0L,
            GetField<long>(
                script,
                "_justiceCustodyResidualMissionFlagObservationDeadlineMs"),
            "Un flag mission tardif doit rester une suspension normale.");
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
