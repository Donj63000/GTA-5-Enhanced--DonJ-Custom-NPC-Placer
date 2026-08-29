using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Xml;

// Je fige chaque intention avant de la confier au writer. Ces DTO ne portent
// aucune reference vers les objets mutables de la detention ou vers GTA.
internal sealed class JusticeFineDebitPersistenceSnapshot
{
    internal JusticeFineDebitPersistenceSnapshot(
        string episodeId,
        int slot,
        long fineAmount,
        bool cashPlanPrepared,
        long preparedAtUtcTicks,
        int debitAmount,
        int cashBefore,
        int cashAfter,
        int sentenceIfDebited,
        int sentenceIfConverted,
        bool stationPlanned,
        bool debitAttempted,
        int cashWriteResult,
        int resolution,
        long fineInDisputeBefore,
        long ambiguousAmount,
        long attemptedAtUtcTicks)
    {
        EpisodeId = episodeId ?? string.Empty;
        Slot = slot;
        FineAmount = fineAmount;
        CashPlanPrepared = cashPlanPrepared;
        PreparedAtUtcTicks = Math.Max(0L, preparedAtUtcTicks);
        DebitAmount = debitAmount;
        CashBefore = cashBefore;
        CashAfter = cashAfter;
        SentenceIfDebited = sentenceIfDebited;
        SentenceIfConverted = sentenceIfConverted;
        StationPlanned = stationPlanned;
        DebitAttempted = debitAttempted;
        CashWriteResult = cashWriteResult;
        Resolution = resolution;
        FineInDisputeBefore = Math.Max(0L, fineInDisputeBefore);
        AmbiguousAmount = Math.Max(0L, ambiguousAmount);
        AttemptedAtUtcTicks = Math.Max(0L, attemptedAtUtcTicks);
    }

    internal string EpisodeId { get; }
    internal int Slot { get; }
    internal long FineAmount { get; }
    internal bool CashPlanPrepared { get; }
    internal long PreparedAtUtcTicks { get; }
    internal int DebitAmount { get; }
    internal int CashBefore { get; }
    internal int CashAfter { get; }
    internal int SentenceIfDebited { get; }
    internal int SentenceIfConverted { get; }
    internal bool StationPlanned { get; }
    internal bool DebitAttempted { get; }
    internal int CashWriteResult { get; }
    internal int Resolution { get; }
    internal long FineInDisputeBefore { get; }
    internal long AmbiguousAmount { get; }
    internal long AttemptedAtUtcTicks { get; }
}

internal sealed class JusticeVoluntaryPaymentPersistenceSnapshot
{
    internal JusticeVoluntaryPaymentPersistenceSnapshot(
        string paymentId,
        int slot,
        long fineBefore,
        int debitAmount,
        int cashBefore,
        int cashAfter,
        long fineInDisputeBefore,
        long preparedAtUtcTicks,
        bool debitAttempted,
        long attemptedAtUtcTicks,
        int cashWriteResult,
        int resolution,
        long ambiguousAmount,
        bool debtCommitted)
    {
        PaymentId = paymentId ?? string.Empty;
        Slot = slot;
        FineBefore = fineBefore;
        DebitAmount = debitAmount;
        CashBefore = cashBefore;
        CashAfter = cashAfter;
        FineInDisputeBefore = Math.Max(0L, fineInDisputeBefore);
        PreparedAtUtcTicks = Math.Max(0L, preparedAtUtcTicks);
        DebitAttempted = debitAttempted;
        AttemptedAtUtcTicks = Math.Max(0L, attemptedAtUtcTicks);
        CashWriteResult = cashWriteResult;
        Resolution = resolution;
        AmbiguousAmount = Math.Max(0L, ambiguousAmount);
        DebtCommitted = debtCommitted;
    }

    internal string PaymentId { get; }
    internal int Slot { get; }
    internal long FineBefore { get; }
    internal int DebitAmount { get; }
    internal int CashBefore { get; }
    internal int CashAfter { get; }
    internal long FineInDisputeBefore { get; }
    internal long PreparedAtUtcTicks { get; }
    internal bool DebitAttempted { get; }
    internal long AttemptedAtUtcTicks { get; }
    internal int CashWriteResult { get; }
    internal int Resolution { get; }
    internal long AmbiguousAmount { get; }
    internal bool DebtCommitted { get; }
}

internal sealed class JusticeDisciplinePersistenceSnapshot
{
    internal JusticeDisciplinePersistenceSnapshot(
        string incidentId,
        int crimeKind,
        int penaltySeconds)
    {
        IncidentId = incidentId ?? string.Empty;
        CrimeKind = crimeKind;
        PenaltySeconds = penaltySeconds;
    }

    internal string IncidentId { get; }
    internal int CrimeKind { get; }
    internal int PenaltySeconds { get; }
}

internal sealed class JusticeWeaponPersistenceSnapshot
{
    private readonly ReadOnlyCollection<int> _componentHashes;

    internal JusticeWeaponPersistenceSnapshot(
        int weaponHash,
        int ammo,
        int ammoInClip,
        int tint,
        IEnumerable<int> componentHashes)
    {
        WeaponHash = weaponHash;
        Ammo = ammo;
        AmmoInClip = ammoInClip;
        Tint = tint;
        _componentHashes = new ReadOnlyCollection<int>(
            componentHashes == null
                ? new List<int>()
                : new List<int>(componentHashes));
    }

    internal int WeaponHash { get; }
    internal int Ammo { get; }
    internal int AmmoInClip { get; }
    internal int Tint { get; }
    internal IReadOnlyList<int> ComponentHashes => _componentHashes;
}

internal sealed class JusticeInventoryPersistenceSnapshot
{
    private readonly ReadOnlyCollection<JusticeWeaponPersistenceSnapshot> _weapons;

    internal JusticeInventoryPersistenceSnapshot(
        bool isValidated,
        int selectedWeaponHash,
        IEnumerable<JusticeWeaponPersistenceSnapshot> weapons)
    {
        IsValidated = isValidated;
        SelectedWeaponHash = selectedWeaponHash;
        List<JusticeWeaponPersistenceSnapshot> copy =
            new List<JusticeWeaponPersistenceSnapshot>();
        if (weapons != null)
        {
            foreach (JusticeWeaponPersistenceSnapshot weapon in weapons)
            {
                if (weapon == null)
                {
                    throw new ArgumentException(
                        "Le snapshot d'inventaire ne peut pas contenir d'arme nulle.",
                        "weapons");
                }

                copy.Add(new JusticeWeaponPersistenceSnapshot(
                    weapon.WeaponHash,
                    weapon.Ammo,
                    weapon.AmmoInClip,
                    weapon.Tint,
                    weapon.ComponentHashes));
            }
        }
        _weapons = new ReadOnlyCollection<JusticeWeaponPersistenceSnapshot>(copy);
    }

    internal bool IsValidated { get; }
    internal int SelectedWeaponHash { get; }
    internal IReadOnlyList<JusticeWeaponPersistenceSnapshot> Weapons => _weapons;
}

internal sealed class JusticeActivityCooldownPersistenceSnapshot
{
    internal JusticeActivityCooldownPersistenceSnapshot(string id, int remainingSeconds)
    {
        Id = id ?? string.Empty;
        RemainingSeconds = Math.Max(0, remainingSeconds);
    }

    internal string Id { get; }
    internal int RemainingSeconds { get; }
}

internal sealed class JusticeCustodyPersistenceSnapshot
{
    private readonly ReadOnlyCollection<JusticeActivityCooldownPersistenceSnapshot> _cooldowns;

    internal JusticeCustodyPersistenceSnapshot(
        bool active,
        int site,
        bool policeSuppressionApplied,
        bool policeDispatchDisabled,
        int initialSentenceSeconds,
        int activityReductionSeconds,
        bool inventoryRemoved,
        bool weaponControlsLocked,
        int inventoryState,
        int inventoryCaptureFailures,
        int inventoryRemovalFailures,
        bool deferredInventoryRestore,
        bool waitingForRespawn,
        bool deathRebindPending,
        bool playerStateStored,
        bool storedInvincible,
        bool storedFrozen,
        bool storedCanRagdoll,
        int playerModelHash,
        int playerSlot,
        int releaseSelectedWeapon,
        bool legalReleaseWantedClearAttempted,
        bool amnestyWantedClearAttempted,
        JusticeFineDebitPersistenceSnapshot fineDebitIntent,
        JusticeVoluntaryPaymentPersistenceSnapshot voluntaryPaymentIntent,
        JusticeDisciplinePersistenceSnapshot disciplineIntent,
        JusticeInventoryPersistenceSnapshot inventorySnapshot,
        bool hasActivityCooldownContainer,
        IEnumerable<JusticeActivityCooldownPersistenceSnapshot> cooldowns)
    {
        Active = active;
        Site = site;
        PoliceSuppressionApplied = policeSuppressionApplied;
        PoliceDispatchDisabled = policeDispatchDisabled;
        InitialSentenceSeconds = Math.Max(0, initialSentenceSeconds);
        ActivityReductionSeconds = Math.Max(0, activityReductionSeconds);
        InventoryRemoved = inventoryRemoved;
        WeaponControlsLocked = weaponControlsLocked;
        InventoryState = inventoryState;
        InventoryCaptureFailures = Math.Max(0, inventoryCaptureFailures);
        InventoryRemovalFailures = Math.Max(0, inventoryRemovalFailures);
        DeferredInventoryRestore = deferredInventoryRestore;
        WaitingForRespawn = waitingForRespawn;
        DeathRebindPending = deathRebindPending;
        PlayerStateStored = playerStateStored;
        StoredInvincible = playerStateStored && storedInvincible;
        StoredFrozen = playerStateStored && storedFrozen;
        StoredCanRagdoll = !playerStateStored || storedCanRagdoll;
        PlayerModelHash = playerModelHash;
        PlayerSlot = playerSlot;
        ReleaseSelectedWeapon = releaseSelectedWeapon;
        LegalReleaseWantedClearAttempted = legalReleaseWantedClearAttempted;
        AmnestyWantedClearAttempted = amnestyWantedClearAttempted;
        FineDebitIntent = fineDebitIntent;
        VoluntaryPaymentIntent = voluntaryPaymentIntent;
        DisciplineIntent = disciplineIntent;
        InventorySnapshot = inventorySnapshot;
        HasActivityCooldownContainer = hasActivityCooldownContainer;

        List<JusticeActivityCooldownPersistenceSnapshot> copy =
            new List<JusticeActivityCooldownPersistenceSnapshot>();
        if (cooldowns != null)
        {
            foreach (JusticeActivityCooldownPersistenceSnapshot cooldown in cooldowns)
            {
                if (cooldown == null)
                {
                    throw new ArgumentException(
                        "Le snapshot de detention ne peut pas contenir de cooldown nul.",
                        "cooldowns");
                }

                copy.Add(new JusticeActivityCooldownPersistenceSnapshot(
                    cooldown.Id,
                    cooldown.RemainingSeconds));
            }
        }
        _cooldowns = new ReadOnlyCollection<JusticeActivityCooldownPersistenceSnapshot>(copy);
    }

    internal bool Active { get; }
    internal int Site { get; }
    internal bool PoliceSuppressionApplied { get; }
    internal bool PoliceDispatchDisabled { get; }
    internal int InitialSentenceSeconds { get; }
    internal int ActivityReductionSeconds { get; }
    internal bool InventoryRemoved { get; }
    internal bool WeaponControlsLocked { get; }
    internal int InventoryState { get; }
    internal int InventoryCaptureFailures { get; }
    internal int InventoryRemovalFailures { get; }
    internal bool DeferredInventoryRestore { get; }
    internal bool WaitingForRespawn { get; }
    internal bool DeathRebindPending { get; }
    internal bool PlayerStateStored { get; }
    internal bool StoredInvincible { get; }
    internal bool StoredFrozen { get; }
    internal bool StoredCanRagdoll { get; }
    internal int PlayerModelHash { get; }
    internal int PlayerSlot { get; }
    internal int ReleaseSelectedWeapon { get; }
    internal bool LegalReleaseWantedClearAttempted { get; }
    internal bool AmnestyWantedClearAttempted { get; }
    internal JusticeFineDebitPersistenceSnapshot FineDebitIntent { get; }
    internal JusticeVoluntaryPaymentPersistenceSnapshot VoluntaryPaymentIntent { get; }
    internal JusticeDisciplinePersistenceSnapshot DisciplineIntent { get; }
    internal JusticeInventoryPersistenceSnapshot InventorySnapshot { get; }
    internal bool HasActivityCooldownContainer { get; }
    internal IReadOnlyList<JusticeActivityCooldownPersistenceSnapshot> Cooldowns => _cooldowns;
}

public sealed partial class DonJEnemySpawner
{
    // Je lis GameTime une seule fois sur le thread GTA. Le writer ne recoit que
    // des secondes restantes et n'a donc jamais besoin d'appeler le jeu.
    private JusticeCustodyPersistenceSnapshot CaptureJusticeCustodyPersistenceSnapshot()
    {
        int capturedGameTime = GetJusticeRawGameTimeSafe();
        return CaptureJusticeCustodyPersistenceSnapshot(capturedGameTime);
    }

    private JusticeCustodyPersistenceSnapshot CaptureJusticeCustodyPersistenceSnapshot(
        int capturedGameTime)
    {
        List<JusticeActivityCooldownPersistenceSnapshot> cooldowns =
            new List<JusticeActivityCooldownPersistenceSnapshot>(
                _justiceActivityCooldownUntil.Count);
        foreach (KeyValuePair<string, int> pair in _justiceActivityCooldownUntil)
        {
            int remainingSeconds = Math.Max(
                0,
                (JusticeCustodyMillisecondsUntil(capturedGameTime, pair.Value) + 999) / 1000);
            if (remainingSeconds > 0)
            {
                cooldowns.Add(new JusticeActivityCooldownPersistenceSnapshot(
                    pair.Key,
                    remainingSeconds));
            }
        }

        return new JusticeCustodyPersistenceSnapshot(
            JusticeIsCustodyActive,
            (int)_justiceCustodySite,
            _justicePoliceIgnoreApplied,
            _justicePoliceDispatchDisabled,
            _justiceCustodyInitialSentenceSeconds,
            _justiceActivityReductionGrantedSeconds,
            _justiceInventoryRemoved,
            _justiceWeaponControlsLocked,
            (int)_justiceInventoryCustodyState,
            _justiceInventoryCaptureFailureCount,
            _justiceInventoryRemovalFailureCount,
            _justiceDeferredInventoryRestore,
            _justiceCustodyWaitingForRespawn,
            _justiceCustodyDeathRebindPending,
            _justiceCustodyPlayerStateStored,
            _justiceCustodyStoredInvincible,
            _justiceCustodyStoredFrozen,
            _justiceCustodyStoredCanRagdoll,
            _justiceCustodyPlayerModelHash,
            _justiceCustodyPlayerSlot,
            _justiceReleaseSelectedWeaponHash,
            _justiceLegalReleaseWantedClearAttempted,
            _justiceAmnestyWantedClearAttempted,
            CaptureJusticeFineDebitPersistenceSnapshot(),
            CaptureJusticeVoluntaryPaymentPersistenceSnapshot(),
            CaptureJusticeDisciplinePersistenceSnapshot(),
            CaptureJusticeInventoryPersistenceSnapshot(),
            _justiceActivityCooldownUntil.Count > 0,
            cooldowns);
    }

    private JusticeCustodyPersistenceSnapshot CaptureLoadedJusticeCustodyPersistenceSnapshot(
        bool hasActivityCooldownContainer)
    {
        List<JusticeActivityCooldownPersistenceSnapshot> cooldowns =
            new List<JusticeActivityCooldownPersistenceSnapshot>(
                _justiceLoadedActivityCooldownSeconds.Count);
        foreach (KeyValuePair<string, int> pair in _justiceLoadedActivityCooldownSeconds)
        {
            if (pair.Value > 0)
            {
                cooldowns.Add(new JusticeActivityCooldownPersistenceSnapshot(
                    pair.Key,
                    pair.Value));
            }
        }

        // Je matérialise le fragment validé en DTO sans convertir ses durées
        // restantes en GameTime. Un profil inactif conserve ainsi exactement la
        // preuve relue jusqu'à sa prochaine activation.
        return new JusticeCustodyPersistenceSnapshot(
            JusticeIsCustodyActive,
            (int)_justiceCustodySite,
            _justicePoliceIgnoreApplied,
            _justicePoliceDispatchDisabled,
            _justiceCustodyInitialSentenceSeconds,
            _justiceActivityReductionGrantedSeconds,
            _justiceInventoryRemoved,
            _justiceWeaponControlsLocked,
            (int)_justiceInventoryCustodyState,
            _justiceInventoryCaptureFailureCount,
            _justiceInventoryRemovalFailureCount,
            _justiceDeferredInventoryRestore,
            _justiceCustodyWaitingForRespawn,
            _justiceCustodyDeathRebindPending,
            _justiceCustodyPlayerStateStored,
            _justiceCustodyStoredInvincible,
            _justiceCustodyStoredFrozen,
            _justiceCustodyStoredCanRagdoll,
            _justiceCustodyPlayerModelHash,
            _justiceCustodyPlayerSlot,
            _justiceReleaseSelectedWeaponHash,
            _justiceLegalReleaseWantedClearAttempted,
            _justiceAmnestyWantedClearAttempted,
            CaptureJusticeFineDebitPersistenceSnapshot(),
            CaptureJusticeVoluntaryPaymentPersistenceSnapshot(),
            CaptureJusticeDisciplinePersistenceSnapshot(),
            CaptureJusticeInventoryPersistenceSnapshot(),
            hasActivityCooldownContainer,
            cooldowns);
    }

    private JusticeFineDebitPersistenceSnapshot CaptureJusticeFineDebitPersistenceSnapshot()
    {
        JusticeFineDebitIntent intent = _justiceFineDebitIntent;
        return intent == null
            ? null
            : new JusticeFineDebitPersistenceSnapshot(
                intent.EpisodeId,
                intent.Slot,
                intent.FineAmount,
                intent.CashPlanPrepared,
                intent.PreparedAtUtcTicks,
                intent.DebitAmount,
                intent.CashBefore,
                intent.CashAfter,
                intent.SentenceIfDebited,
                intent.SentenceIfConverted,
                intent.StationPlanned,
                intent.DebitAttempted,
                (int)intent.CashWriteResult,
                (int)intent.Resolution,
                intent.FineInDisputeBefore,
                intent.AmbiguousAmount,
                intent.AttemptedAtUtcTicks);
    }

    private JusticeVoluntaryPaymentPersistenceSnapshot
        CaptureJusticeVoluntaryPaymentPersistenceSnapshot()
    {
        JusticeVoluntaryFinePaymentIntent intent = _justiceVoluntaryFinePaymentIntent;
        return intent == null
            ? null
            : new JusticeVoluntaryPaymentPersistenceSnapshot(
                intent.PaymentId,
                intent.Slot,
                intent.FineBefore,
                intent.DebitAmount,
                intent.CashBefore,
                intent.CashAfter,
                intent.FineInDisputeBefore,
                intent.PreparedAtUtcTicks,
                intent.DebitAttempted,
                intent.AttemptedAtUtcTicks,
                (int)intent.CashWriteResult,
                (int)intent.Resolution,
                intent.AmbiguousAmount,
                intent.DebtCommitted);
    }

    private JusticeDisciplinePersistenceSnapshot CaptureJusticeDisciplinePersistenceSnapshot()
    {
        JusticeDisciplineIntent intent = _justiceDisciplineIntent;
        return intent == null
            ? null
            : new JusticeDisciplinePersistenceSnapshot(
                intent.IncidentId,
                (int)intent.CrimeKind,
                intent.PenaltySeconds);
    }

    private JusticeInventoryPersistenceSnapshot CaptureJusticeInventoryPersistenceSnapshot()
    {
        JusticeWeaponSnapshot source = _justiceWeaponSnapshot;
        if (source == null)
        {
            return null;
        }

        List<JusticeWeaponPersistenceSnapshot> weapons =
            new List<JusticeWeaponPersistenceSnapshot>(source.Weapons.Count);
        for (int index = 0; index < source.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem item = source.Weapons[index];
            if (item != null)
            {
                weapons.Add(new JusticeWeaponPersistenceSnapshot(
                    item.WeaponHash,
                    item.Ammo,
                    item.AmmoInClip,
                    item.Tint,
                    item.ComponentHashes));
            }
        }

        return new JusticeInventoryPersistenceSnapshot(
            source.IsValidated,
            source.SelectedWeaponHash,
            weapons);
    }

    private static JusticeCustodyPersistenceSnapshot
        CloneJusticeCustodyPersistenceSnapshotWithoutPoliceTokens(
            JusticeCustodyPersistenceSnapshot source)
    {
        if (source == null)
        {
            return null;
        }

        return new JusticeCustodyPersistenceSnapshot(
            source.Active,
            source.Site,
            false,
            false,
            source.InitialSentenceSeconds,
            source.ActivityReductionSeconds,
            source.InventoryRemoved,
            source.WeaponControlsLocked,
            source.InventoryState,
            source.InventoryCaptureFailures,
            source.InventoryRemovalFailures,
            source.DeferredInventoryRestore,
            source.WaitingForRespawn,
            source.DeathRebindPending,
            source.PlayerStateStored,
            source.StoredInvincible,
            source.StoredFrozen,
            source.StoredCanRagdoll,
            source.PlayerModelHash,
            source.PlayerSlot,
            source.ReleaseSelectedWeapon,
            source.LegalReleaseWantedClearAttempted,
            source.AmnestyWantedClearAttempted,
            source.FineDebitIntent,
            source.VoluntaryPaymentIntent,
            source.DisciplineIntent,
            source.InventorySnapshot,
            source.HasActivityCooldownContainer,
            source.Cooldowns);
    }

    private bool RestoreJusticeCustodyPersistenceSnapshot(
        JusticeCustodyPersistenceSnapshot snapshot)
    {
        if (snapshot == null ||
            !Enum.IsDefined(typeof(JusticeCustodySite), snapshot.Site) ||
            !Enum.IsDefined(typeof(JusticeInventoryCustodyState), snapshot.InventoryState) ||
            snapshot.PlayerSlot < -1 || snapshot.PlayerSlot >= JusticePlayerProfileCount)
        {
            return false;
        }

        ResetJusticeCustodyPersistentFields(false);
        _justiceCustodySite = (JusticeCustodySite)snapshot.Site;
        _justicePoliceIgnoreApplied = snapshot.PoliceSuppressionApplied;
        _justicePoliceDispatchDisabled = snapshot.PoliceDispatchDisabled;
        _justicePoliceSuppressionActive =
            snapshot.PoliceSuppressionApplied || snapshot.PoliceDispatchDisabled;
        _justicePoliceSuppressionRestorePending =
            _justicePoliceSuppressionActive &&
            (!snapshot.Active ||
             (_justiceCaseState != null && _justiceCaseState.Phase == JusticePhase.Captured));
        _justicePoliceSuppressionFailureLogged = false;
        _justiceNextPoliceSuppressionAt = 0;
        _justiceNextPoliceSuppressionRestoreAt = 0;

        _justiceCustodyInitialSentenceSeconds = snapshot.InitialSentenceSeconds;
        _justiceActivityReductionGrantedSeconds = snapshot.ActivityReductionSeconds;
        _justiceInventoryRemoved = snapshot.InventoryRemoved;
        _justiceWeaponControlsLocked = snapshot.WeaponControlsLocked;
        _justiceInventoryCustodyState =
            (JusticeInventoryCustodyState)snapshot.InventoryState;
        _justiceInventoryCaptureFailureCount = snapshot.InventoryCaptureFailures;
        _justiceInventoryRemovalFailureCount = snapshot.InventoryRemovalFailures;
        _justiceDeferredInventoryRestore = snapshot.DeferredInventoryRestore;
        _justiceCustodyWaitingForRespawn = snapshot.WaitingForRespawn;
        _justiceCustodyDeathRebindPending = snapshot.DeathRebindPending;
        _justiceCustodyPlayerStateStored = snapshot.PlayerStateStored;
        _justiceCustodyStoredInvincible = snapshot.StoredInvincible;
        _justiceCustodyStoredFrozen = snapshot.StoredFrozen;
        _justiceCustodyStoredCanRagdoll = snapshot.StoredCanRagdoll;
        _justiceCustodyPlayerModelHash = snapshot.PlayerModelHash;
        _justiceCustodyPlayerSlot = snapshot.PlayerSlot;
        _justiceReleaseSelectedWeaponHash = snapshot.ReleaseSelectedWeapon;
        _justiceLegalReleaseWantedClearAttempted =
            snapshot.LegalReleaseWantedClearAttempted;
        _justiceAmnestyWantedClearAttempted = snapshot.AmnestyWantedClearAttempted;

        _justiceFineDebitIntent = RestoreJusticeFineDebitIntent(snapshot.FineDebitIntent);
        _justiceVoluntaryFinePaymentIntent =
            RestoreJusticeVoluntaryPaymentIntent(snapshot.VoluntaryPaymentIntent);
        _justiceDisciplineIntent = RestoreJusticeDisciplineIntent(snapshot.DisciplineIntent);
        _justiceWeaponSnapshot = RestoreJusticeInventorySnapshot(snapshot.InventorySnapshot);

        _justiceLoadedActivityCooldownSeconds.Clear();
        for (int index = 0; index < snapshot.Cooldowns.Count && index < 16; index++)
        {
            JusticeActivityCooldownPersistenceSnapshot cooldown = snapshot.Cooldowns[index];
            if (cooldown.RemainingSeconds > 0 &&
                cooldown.RemainingSeconds <= 300 &&
                FindJusticeCustodyActivityById(cooldown.Id) != null)
            {
                _justiceLoadedActivityCooldownSeconds[cooldown.Id] = cooldown.RemainingSeconds;
            }
        }

        // Un précommit de confiscation n'acquiert jamais les contrôles avant que
        // RemoveAll soit confirmé par GTA.
        if (!_justiceInventoryRemoved && !_justiceDeferredInventoryRestore &&
            ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot))
        {
            _justiceWeaponControlsLocked = false;
            _justiceNextInventoryPersistenceRetryAt = 0;
        }

        if (!ValidateJusticeInventoryCustodyStateInvariant())
        {
            ResetJusticeCustodyPersistentFields(false);
            return false;
        }

        bool resumeCustody = snapshot.Active && _justiceCaseState != null &&
            _justiceCaseState.Phase != JusticePhase.Captured &&
            _justiceCaseState.SentenceSeconds > 0;
        _justiceCustodyRuntimeActive = resumeCustody;
        _justiceCustodyResumePending = resumeCustody;
        return true;
    }

    private static JusticeFineDebitIntent RestoreJusticeFineDebitIntent(
        JusticeFineDebitPersistenceSnapshot source)
    {
        return source == null
            ? null
            : new JusticeFineDebitIntent
            {
                EpisodeId = source.EpisodeId,
                Slot = source.Slot,
                FineAmount = source.FineAmount,
                CashPlanPrepared = source.CashPlanPrepared,
                PreparedAtUtcTicks = source.PreparedAtUtcTicks,
                DebitAmount = source.DebitAmount,
                CashBefore = source.CashBefore,
                CashAfter = source.CashAfter,
                SentenceIfDebited = source.SentenceIfDebited,
                SentenceIfConverted = source.SentenceIfConverted,
                StationPlanned = source.StationPlanned,
                DebitAttempted = source.DebitAttempted,
                CashWriteResult = (JusticeCashWriteResult)source.CashWriteResult,
                Resolution = (JusticePaymentResolution)source.Resolution,
                FineInDisputeBefore = source.FineInDisputeBefore,
                AmbiguousAmount = source.AmbiguousAmount,
                AttemptedAtUtcTicks = source.AttemptedAtUtcTicks
            };
    }

    private static JusticeVoluntaryFinePaymentIntent RestoreJusticeVoluntaryPaymentIntent(
        JusticeVoluntaryPaymentPersistenceSnapshot source)
    {
        return source == null
            ? null
            : new JusticeVoluntaryFinePaymentIntent
            {
                PaymentId = source.PaymentId,
                Slot = source.Slot,
                FineBefore = source.FineBefore,
                DebitAmount = source.DebitAmount,
                CashBefore = source.CashBefore,
                CashAfter = source.CashAfter,
                FineInDisputeBefore = source.FineInDisputeBefore,
                PreparedAtUtcTicks = source.PreparedAtUtcTicks,
                DebitAttempted = source.DebitAttempted,
                AttemptedAtUtcTicks = source.AttemptedAtUtcTicks,
                CashWriteResult = (JusticeCashWriteResult)source.CashWriteResult,
                Resolution = (JusticePaymentResolution)source.Resolution,
                AmbiguousAmount = source.AmbiguousAmount,
                DebtCommitted = source.DebtCommitted
            };
    }

    private static JusticeDisciplineIntent RestoreJusticeDisciplineIntent(
        JusticeDisciplinePersistenceSnapshot source)
    {
        return source == null
            ? null
            : new JusticeDisciplineIntent
            {
                IncidentId = source.IncidentId,
                CrimeKind = (JusticeCrimeKind)source.CrimeKind,
                PenaltySeconds = source.PenaltySeconds
            };
    }

    private static JusticeWeaponSnapshot RestoreJusticeInventorySnapshot(
        JusticeInventoryPersistenceSnapshot source)
    {
        if (source == null)
        {
            return null;
        }

        JusticeWeaponSnapshot restored = new JusticeWeaponSnapshot
        {
            IsValidated = source.IsValidated,
            SelectedWeaponHash = source.SelectedWeaponHash
        };
        for (int index = 0; index < source.Weapons.Count; index++)
        {
            JusticeWeaponPersistenceSnapshot weapon = source.Weapons[index];
            JusticeWeaponSnapshotItem item = new JusticeWeaponSnapshotItem
            {
                WeaponHash = weapon.WeaponHash,
                Ammo = weapon.Ammo,
                AmmoInClip = weapon.AmmoInClip,
                Tint = weapon.Tint
            };
            for (int componentIndex = 0;
                 componentIndex < weapon.ComponentHashes.Count;
                 componentIndex++)
            {
                item.ComponentHashes.Add(weapon.ComponentHashes[componentIndex]);
            }
            restored.Weapons.Add(item);
        }
        return restored;
    }

    internal static string SerializeJusticeCustodyPersistenceSnapshot(
        JusticeCustodyPersistenceSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }

        StringBuilder buffer = new StringBuilder(2048);
        XmlWriterSettings settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        };
        using (XmlWriter writer = XmlWriter.Create(buffer, settings))
        {
            WriteJusticeCustodyPersistenceXml(writer, snapshot);
        }
        return buffer.ToString();
    }

    internal static void WriteJusticeCustodyPersistenceXml(
        XmlWriter writer,
        JusticeCustodyPersistenceSnapshot snapshot)
    {
        if (writer == null)
        {
            throw new ArgumentNullException("writer");
        }
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }

        writer.WriteStartElement("Custody");
        WriteJusticePersistenceAttribute(writer, "active", snapshot.Active);
        writer.WriteAttributeString("site", ((JusticeCustodySite)snapshot.Site).ToString());
        WriteJusticePersistenceAttribute(
            writer,
            "policeSuppressionApplied",
            snapshot.PoliceSuppressionApplied);
        WriteJusticePersistenceAttribute(
            writer,
            "policeDispatchDisabled",
            snapshot.PoliceDispatchDisabled);
        WriteJusticePersistenceAttribute(
            writer,
            "initialSentenceSeconds",
            snapshot.InitialSentenceSeconds);
        WriteJusticePersistenceAttribute(
            writer,
            "activityReductionSeconds",
            snapshot.ActivityReductionSeconds);
        WriteJusticePersistenceAttribute(writer, "inventoryRemoved", snapshot.InventoryRemoved);
        WriteJusticePersistenceAttribute(
            writer,
            "weaponControlsLocked",
            snapshot.WeaponControlsLocked);
        WriteJusticePersistenceAttribute(writer, "inventoryState", snapshot.InventoryState);
        WriteJusticePersistenceAttribute(
            writer,
            "inventoryCaptureFailures",
            snapshot.InventoryCaptureFailures);
        WriteJusticePersistenceAttribute(
            writer,
            "inventoryRemovalFailures",
            snapshot.InventoryRemovalFailures);
        WriteJusticePersistenceAttribute(
            writer,
            "deferredInventoryRestore",
            snapshot.DeferredInventoryRestore);
        WriteJusticePersistenceAttribute(writer, "waitingForRespawn", snapshot.WaitingForRespawn);
        WriteJusticePersistenceAttribute(writer, "deathRebindPending", snapshot.DeathRebindPending);
        WriteJusticePersistenceAttribute(writer, "playerStateStored", snapshot.PlayerStateStored);
        WriteJusticePersistenceAttribute(writer, "storedInvincible", snapshot.StoredInvincible);
        WriteJusticePersistenceAttribute(writer, "storedFrozen", snapshot.StoredFrozen);
        WriteJusticePersistenceAttribute(writer, "storedCanRagdoll", snapshot.StoredCanRagdoll);
        WriteJusticePersistenceAttribute(writer, "playerModelHash", snapshot.PlayerModelHash);
        WriteJusticePersistenceAttribute(writer, "playerSlot", snapshot.PlayerSlot);
        WriteJusticePersistenceAttribute(
            writer,
            "releaseSelectedWeapon",
            snapshot.ReleaseSelectedWeapon);
        WriteJusticePersistenceAttribute(
            writer,
            "legalReleaseWantedClearAttempted",
            snapshot.LegalReleaseWantedClearAttempted);
        WriteJusticePersistenceAttribute(
            writer,
            "amnestyWantedClearAttempted",
            snapshot.AmnestyWantedClearAttempted);

        WriteJusticeFineDebitPersistenceXml(writer, snapshot.FineDebitIntent);
        WriteJusticeVoluntaryPaymentPersistenceXml(writer, snapshot.VoluntaryPaymentIntent);
        WriteJusticeDisciplinePersistenceXml(writer, snapshot.DisciplineIntent);
        WriteJusticeInventoryPersistenceXml(writer, snapshot.InventorySnapshot);
        WriteJusticeActivityCooldownPersistenceXml(writer, snapshot);
        writer.WriteEndElement();
    }

    private static void WriteJusticeFineDebitPersistenceXml(
        XmlWriter writer,
        JusticeFineDebitPersistenceSnapshot intent)
    {
        if (intent == null)
        {
            return;
        }

        writer.WriteStartElement("FineDebitIntent");
        writer.WriteAttributeString("episodeId", intent.EpisodeId);
        WriteJusticePersistenceAttribute(writer, "slot", intent.Slot);
        WriteJusticePersistenceAttribute(writer, "fineAmount", intent.FineAmount);
        WriteJusticePersistenceAttribute(writer, "cashPlanPrepared", intent.CashPlanPrepared);
        WriteJusticePersistenceAttribute(writer, "preparedAtUtcTicks", intent.PreparedAtUtcTicks);
        WriteJusticePersistenceAttribute(writer, "debitAmount", intent.DebitAmount);
        WriteJusticePersistenceAttribute(writer, "cashBefore", intent.CashBefore);
        WriteJusticePersistenceAttribute(writer, "cashAfter", intent.CashAfter);
        WriteJusticePersistenceAttribute(writer, "sentenceIfDebited", intent.SentenceIfDebited);
        WriteJusticePersistenceAttribute(writer, "sentenceIfConverted", intent.SentenceIfConverted);
        WriteJusticePersistenceAttribute(writer, "stationPlanned", intent.StationPlanned);
        WriteJusticePersistenceAttribute(writer, "debitAttempted", intent.DebitAttempted);
        writer.WriteAttributeString(
            "cashWriteResult",
            ((JusticeCashWriteResult)intent.CashWriteResult).ToString());
        writer.WriteAttributeString(
            "resolution",
            ((JusticePaymentResolution)intent.Resolution).ToString());
        WriteJusticePersistenceAttribute(
            writer,
            "fineInDisputeBefore",
            intent.FineInDisputeBefore);
        WriteJusticePersistenceAttribute(writer, "ambiguousAmount", intent.AmbiguousAmount);
        WriteJusticePersistenceAttribute(writer, "attemptedAtUtcTicks", intent.AttemptedAtUtcTicks);
        writer.WriteEndElement();
    }

    private static void WriteJusticeVoluntaryPaymentPersistenceXml(
        XmlWriter writer,
        JusticeVoluntaryPaymentPersistenceSnapshot intent)
    {
        if (intent == null)
        {
            return;
        }

        writer.WriteStartElement("VoluntaryFinePaymentIntent");
        writer.WriteAttributeString("paymentId", intent.PaymentId);
        WriteJusticePersistenceAttribute(writer, "slot", intent.Slot);
        WriteJusticePersistenceAttribute(writer, "fineBefore", intent.FineBefore);
        WriteJusticePersistenceAttribute(writer, "debitAmount", intent.DebitAmount);
        WriteJusticePersistenceAttribute(writer, "cashBefore", intent.CashBefore);
        WriteJusticePersistenceAttribute(writer, "cashAfter", intent.CashAfter);
        WriteJusticePersistenceAttribute(
            writer,
            "fineInDisputeBefore",
            intent.FineInDisputeBefore);
        WriteJusticePersistenceAttribute(writer, "preparedAtUtcTicks", intent.PreparedAtUtcTicks);
        WriteJusticePersistenceAttribute(writer, "debitAttempted", intent.DebitAttempted);
        WriteJusticePersistenceAttribute(writer, "attemptedAtUtcTicks", intent.AttemptedAtUtcTicks);
        writer.WriteAttributeString(
            "cashWriteResult",
            ((JusticeCashWriteResult)intent.CashWriteResult).ToString());
        writer.WriteAttributeString(
            "resolution",
            ((JusticePaymentResolution)intent.Resolution).ToString());
        WriteJusticePersistenceAttribute(writer, "ambiguousAmount", intent.AmbiguousAmount);
        WriteJusticePersistenceAttribute(writer, "debtCommitted", intent.DebtCommitted);
        writer.WriteEndElement();
    }

    private static void WriteJusticeDisciplinePersistenceXml(
        XmlWriter writer,
        JusticeDisciplinePersistenceSnapshot intent)
    {
        if (intent == null)
        {
            return;
        }

        writer.WriteStartElement("DisciplineIntent");
        writer.WriteAttributeString("incidentId", intent.IncidentId);
        writer.WriteAttributeString(
            "crimeKind",
            ((JusticeCrimeKind)intent.CrimeKind).ToString());
        WriteJusticePersistenceAttribute(writer, "penaltySeconds", intent.PenaltySeconds);
        writer.WriteEndElement();
    }

    private static void WriteJusticeInventoryPersistenceXml(
        XmlWriter writer,
        JusticeInventoryPersistenceSnapshot inventory)
    {
        if (inventory == null)
        {
            return;
        }

        writer.WriteStartElement("InventorySnapshot");
        WriteJusticePersistenceAttribute(writer, "validated", inventory.IsValidated);
        WriteJusticePersistenceAttribute(
            writer,
            "selectedWeapon",
            inventory.SelectedWeaponHash);
        for (int index = 0; index < inventory.Weapons.Count; index++)
        {
            JusticeWeaponPersistenceSnapshot weapon = inventory.Weapons[index];
            writer.WriteStartElement("Weapon");
            WriteJusticePersistenceAttribute(writer, "hash", weapon.WeaponHash);
            WriteJusticePersistenceAttribute(writer, "ammo", weapon.Ammo);
            WriteJusticePersistenceAttribute(writer, "clip", weapon.AmmoInClip);
            WriteJusticePersistenceAttribute(writer, "tint", weapon.Tint);
            for (int componentIndex = 0;
                 componentIndex < weapon.ComponentHashes.Count;
                 componentIndex++)
            {
                writer.WriteStartElement("Component");
                WriteJusticePersistenceAttribute(
                    writer,
                    "hash",
                    weapon.ComponentHashes[componentIndex]);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteJusticeActivityCooldownPersistenceXml(
        XmlWriter writer,
        JusticeCustodyPersistenceSnapshot snapshot)
    {
        if (!snapshot.HasActivityCooldownContainer)
        {
            return;
        }

        writer.WriteStartElement("ActivityCooldowns");
        for (int index = 0; index < snapshot.Cooldowns.Count; index++)
        {
            JusticeActivityCooldownPersistenceSnapshot cooldown = snapshot.Cooldowns[index];
            if (cooldown.RemainingSeconds <= 0)
            {
                continue;
            }

            writer.WriteStartElement("Cooldown");
            writer.WriteAttributeString("id", cooldown.Id);
            WriteJusticePersistenceAttribute(
                writer,
                "remainingSeconds",
                cooldown.RemainingSeconds);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteJusticePersistenceAttribute(
        XmlWriter writer,
        string name,
        bool value)
    {
        writer.WriteAttributeString(name, value ? "true" : "false");
    }

    private static void WriteJusticePersistenceAttribute(
        XmlWriter writer,
        string name,
        int value)
    {
        writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteJusticePersistenceAttribute(
        XmlWriter writer,
        string name,
        long value)
    {
        writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
    }
}
