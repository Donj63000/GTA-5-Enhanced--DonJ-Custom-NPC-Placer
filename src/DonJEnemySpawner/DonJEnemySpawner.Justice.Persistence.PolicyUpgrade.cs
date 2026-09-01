using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using GTA;

public sealed partial class DonJEnemySpawner
{
    internal const int JusticeSentencePolicyVersion = 2;

    private const string JusticeSentencePolicyQuarantineDirectoryName =
        "_justice_policy_v1.quarantine";
    private const string JusticeSentencePolicyQuarantinePrimaryName =
        "legacy-primary.xml";
    private const string JusticeSentencePolicyQuarantineBackupName =
        "legacy-backup.xml";
    private const string JusticeSentencePolicyQuarantineWalName =
        "legacy.wal";
    private const string JusticeSentencePolicyQuarantineSourcePrimaryName =
        "legacy-source-primary.xml";
    private const string JusticeSentencePolicyQuarantineSourceBackupName =
        "legacy-source-backup.xml";
    private const string JusticeSentencePolicyQuarantineSourceWalName =
        "legacy-source.wal";
    private const string JusticeSentencePolicyQuarantineCompleteName =
        "quarantine.complete";
    private const string JusticeSentencePolicyQuarantineCompleteTempName =
        "quarantine.complete.tmp";

    private int _justiceSentencePolicyVersion = JusticeSentencePolicyVersion;
    private int _justicePolicyResetRecoveryMask;
    private bool _justicePolicyResetPublicationPending;
    private bool _justicePolicyResetRecoveryPublicationPending;
    private bool _justicePolicyResetLegacyIdentityProofPending;
    private int _justicePolicyResetWorldRecoveryAppliedMask;
    private string _justicePolicyResetLegacySourcePath = string.Empty;

    private static int ReadJusticeSentencePolicyVersion(
        JusticePersistenceSnapshot snapshot)
    {
        int version;
        string text = snapshot == null
            ? string.Empty
            : JusticeXmlPersistenceCodec.GetFieldValue(
                snapshot.GlobalFields,
                "sentencePolicyVersion",
                string.Empty);
        return int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out version)
            ? version
            : 0;
    }

    private static bool TryReadJusticeSentencePolicyVersionStrict(
        JusticePersistenceSnapshot snapshot,
        out int version)
    {
        version = 0;
        if (snapshot == null)
        {
            // Je réserve l'absence du marqueur aux sauvegardes historiques.
            return true;
        }

        for (int index = 0; index < snapshot.GlobalFields.Count; index++)
        {
            JusticePersistenceField field = snapshot.GlobalFields[index];
            if (field == null || !string.Equals(
                    field.Path,
                    "sentencePolicyVersion",
                    StringComparison.Ordinal))
            {
                continue;
            }

            // Je refuse une valeur présente mais invalide. Elle ne doit jamais
            // être confondue avec l'absence legacy qui autorise le reset unique.
            return int.TryParse(
                       field.Value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out version) &&
                   version > 0;
        }

        return true;
    }

    private static int ReadJusticePolicyResetRecoveryMask(
        JusticePersistenceSnapshot snapshot)
    {
        int mask;
        string text = snapshot == null
            ? string.Empty
            : JusticeXmlPersistenceCodec.GetFieldValue(
                snapshot.GlobalFields,
                "policyResetRecoveryMask",
                "0");
        return int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out mask) && mask >= 0 && mask < (1 << JusticePlayerProfileCount)
            ? mask
            : -1;
    }

    private static bool ContainsJusticeRemovedSentencePolicyCustodyFields(
        XmlElement root)
    {
        if (root == null)
        {
            return false;
        }

        XmlNodeList custodyNodes = root.SelectNodes(
            "Custody | PlayerProfiles/Profile/Custody");
        if (custodyNodes == null)
        {
            return false;
        }

        for (int index = 0; index < custodyNodes.Count; index++)
        {
            XmlElement custody = custodyNodes[index] as XmlElement;
            if (custody != null &&
                (custody.HasAttribute("activityReductionSeconds") ||
                 custody.SelectSingleNode("DisciplineIntent") != null ||
                 custody.SelectSingleNode("ActivityCooldowns") != null))
            {
                return true;
            }
        }

        return false;
    }

    private int PrepareJusticeProfilesForSentencePolicyUpgrade(
        JusticePlayerProfileState[] loadedProfiles)
    {
        if (loadedProfiles == null ||
            loadedProfiles.Length != JusticePlayerProfileCount)
        {
            throw new InvalidDataException(
                "Les trois profils Justice sont requis pour le reset de barème.");
        }

        int recoveryMask = 0;
        for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
        {
            JusticePlayerProfileState source = loadedProfiles[slot];
            if (source == null || source.CaseState == null ||
                source.RecordState == null)
            {
                throw new InvalidDataException(
                    "Profil Justice legacy incomplet pendant le reset de barème.");
            }

            bool enabled = source.CaseState.Enabled;
            JusticeCustodyPersistenceSnapshot recovery =
                CreateJusticeSentencePolicyRecoveryToken(
                    source.CustodySnapshot,
                    slot);
            JusticePlayerProfileState replacement =
                CreateEmptyJusticeProfilePreservingEnabled(slot, enabled);
            if (recovery != null)
            {
                recoveryMask |= 1 << slot;
                replacement.CustodySnapshot = recovery;
                replacement.CustodyXml = string.Empty;
                replacement.LastCanonicalPlayerModel = recovery.PlayerModelHash;
            }
            loadedProfiles[slot] = replacement;
        }

        return recoveryMask;
    }

    private static JusticePlayerProfileState
        CreateEmptyJusticeProfilePreservingEnabled(int slot, bool enabled)
    {
        JusticeCaseState cleanCase = new JusticeCaseState
        {
            Enabled = enabled
        };
        return new JusticePlayerProfileState(slot)
        {
            CaseState = cleanCase,
            RecordState = new JusticeRecordState(),
            CustodyXml = CreateCanonicalEmptyJusticeCustodyXml(),
            PendingDeathCapture = false,
            PendingDeathCapturePlayerSlot = -1,
            PendingDeathCapturePlayerModel = 0,
            PendingAmnestyWantedClear = false,
            PendingLegalReleaseFinalization = false,
            PendingLegalReleaseSite = 0,
            PendingLegalReleaseSelectedWeapon = 0,
            LastCanonicalPlayerModel = 0,
            CanAdvanceCustodyInBackground = false,
            InactiveCustodyLastTickAt = 0,
            InactiveCustodyElapsedRemainderMs = 0
        };
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateJusticeSentencePolicyRecoveryToken(
            JusticeCustodyPersistenceSnapshot source,
            int profileSlot)
    {
        if (source == null || source.PlayerSlot != profileSlot ||
            !RequiresJusticeSentencePolicyRecovery(source))
        {
            return null;
        }

        bool inventoryRecoveryRequired = source.InventorySnapshot != null;

        // Je ne conserve que les éléments indispensables pour rendre le monde
        // et l'inventaire au bon protagoniste. Je transforme toute confiscation
        // legacy en merge différé : ce jeton ne peut donc jamais réarmer RemoveAll.
        JusticeCustodyPersistenceSnapshot token =
            new JusticeCustodyPersistenceSnapshot(
            source.Active,
            source.Site,
            source.PoliceSuppressionApplied,
            source.PoliceDispatchDisabled,
            0,
            0,
            false,
            false,
            inventoryRecoveryRequired
                ? (int)JusticeInventoryCustodyState.RestorePending
                : (int)JusticeInventoryCustodyState.None,
            0,
            0,
            inventoryRecoveryRequired,
            false,
            false,
            source.PlayerStateStored,
            source.StoredInvincible,
            source.StoredFrozen,
            source.StoredCanRagdoll,
            source.PlayerModelHash,
            profileSlot,
            JusticeUnarmedHash,
            false,
            false,
            null,
            null,
            null,
            source.InventorySnapshot,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
        return RequiresJusticeSentencePolicyRecovery(token) ? token : null;
    }

    private static bool RequiresJusticeSentencePolicyRecovery(
        JusticeCustodyPersistenceSnapshot source)
    {
        return source != null &&
               (source.Active || source.PoliceSuppressionApplied ||
                source.PoliceDispatchDisabled || source.InventoryRemoved ||
                source.WeaponControlsLocked || source.DeferredInventoryRestore ||
                source.InventorySnapshot != null || source.PlayerStateStored);
    }

    private bool LoadJusticeLegacySentencePolicyForReset(
        XmlElement root,
        JusticePersistenceSnapshot loadedV2Snapshot,
        string sourcePath)
    {
        _justicePolicyResetLegacyIdentityProofPending = false;
        int currentCanonicalSlot = GetJusticeCanonicalPlayerSlotSafe();
        if (_justicePlayerProfiles == null &&
            _justiceCanonicalPlayerSlotOverride == null)
        {
            currentCanonicalSlot = -1;
        }
        JusticePlayerProfileState[] profiles;
        int persistedActiveSlot;
        if (!TryExtractJusticeLegacyPolicyProfiles(
                root,
                currentCanonicalSlot,
                out profiles,
                out persistedActiveSlot))
        {
            _justicePolicyResetLegacyIdentityProofPending =
                IsJusticeLegacyPolicyIdentityProofMissing(
                    root,
                    currentCanonicalSlot);
            return false;
        }

        int selectedSlot = IsJusticeCanonicalProfileSlot(currentCanonicalSlot)
            ? currentCanonicalSlot
            : persistedActiveSlot;
        if (!IsJusticeCanonicalProfileSlot(selectedSlot))
        {
            // Je refuse d'attribuer l'ancien dossier sans protagoniste prouvé.
            // Le point d'appel conservera le XML intact et réarmera sa relecture
            // dès qu'un slot canonique apparaîtra.
            _justicePolicyResetLegacyIdentityProofPending = true;
            return false;
        }
        _justicePolicyResetLegacyIdentityProofPending = false;

        int recoveryMask = PrepareJusticeProfilesForSentencePolicyUpgrade(
            profiles);
        _justicePlayerProfiles = profiles;
        _justiceActivePlayerProfileSlot = selectedSlot;
        _justiceProfileSelectionPending =
            !IsJusticeCanonicalProfileSlot(currentCanonicalSlot);
        _justiceLegacyProfileReloadPending = false;
        _justiceNextIdentityGeneration = 0;
        _justicePoliceIntegrationMode =
            JusticePoliceIntegrationMode.FreeroamBestEffort;
        _justiceSentencePolicyVersion = JusticeSentencePolicyVersion;
        _justicePolicyResetRecoveryMask = recoveryMask;
        _justicePolicyResetPublicationPending = true;
        _justicePolicyResetRecoveryPublicationPending = false;
        _justicePolicyResetWorldRecoveryAppliedMask = 0;
        _justicePolicyResetLegacySourcePath = Path.GetFullPath(sourcePath);
        _justiceV1MigrationSourcePath = string.Empty;
        _justicePersistenceRevision = 0L;
        _justiceProfilePersistenceGenerations =
            new long[JusticePlayerProfileCount];
        _justiceLoadedSchemaMajor = loadedV2Snapshot == null
            ? JusticeStateVersion
            : JusticeXmlPersistenceCodec.SchemaMajor;

        if (!ActivateJusticePlayerProfile(selectedSlot))
        {
            return false;
        }
        MergeJusticeInactiveProfilePoliceSuppressionRecovery();
        _justiceDamageFrontPrimingPending = _justiceEnabled;
        JusticeMarkStateDirty();
        LogWarning(
            "Justice.Migration.Barème",
            "Anciennes données judiciaires supprimées; préférences ON/OFF conservées.");
        return true;
    }

    private static bool IsJusticeLegacyPolicyIdentityProofMissing(
        XmlElement root,
        int currentCanonicalSlot)
    {
        if (root == null || IsJusticeCanonicalProfileSlot(currentCanonicalSlot))
        {
            return false;
        }

        XmlNodeList profileNodes = root.SelectNodes("PlayerProfiles/Profile");
        if (profileNodes != null && profileNodes.Count > 0)
        {
            // Le format à trois profils porte sa propre identité. Un échec de
            // lecture dans cette branche est une corruption, pas une attente GTA.
            return false;
        }

        int lastCanonicalSlot;
        int custodySlot;
        XmlElement custody = root.SelectSingleNode("Custody") as XmlElement;
        TryReadJusticeIntStrict(
            root,
            "lastCanonicalPlayerSlot",
            -1,
            -1,
            JusticePlayerProfileCount - 1,
            out lastCanonicalSlot);
        if (custody == null)
        {
            return !IsJusticeCanonicalProfileSlot(lastCanonicalSlot);
        }
        TryReadJusticeIntStrict(
            custody,
            "playerSlot",
            -1,
            -1,
            JusticePlayerProfileCount - 1,
            out custodySlot);
        return !IsJusticeCanonicalProfileSlot(lastCanonicalSlot) &&
               !IsJusticeCanonicalProfileSlot(custodySlot);
    }

    private static bool TryExtractJusticeLegacyPolicyProfiles(
        XmlElement root,
        int currentCanonicalSlot,
        out JusticePlayerProfileState[] profiles,
        out int persistedActiveSlot)
    {
        profiles = null;
        persistedActiveSlot = -1;
        if (root == null)
        {
            return false;
        }

        JusticePlayerProfileState[] extracted =
            new JusticePlayerProfileState[JusticePlayerProfileCount];
        XmlNodeList profileNodes = root.SelectNodes("PlayerProfiles/Profile");
        if (profileNodes != null && profileNodes.Count > 0)
        {
            if (profileNodes.Count != JusticePlayerProfileCount ||
                !TryReadJusticeIntStrict(
                    root,
                    "activePlayerSlot",
                    -1,
                    0,
                    JusticePlayerProfileCount - 1,
                    out persistedActiveSlot))
            {
                return false;
            }
            for (int index = 0; index < profileNodes.Count; index++)
            {
                XmlElement profile = profileNodes[index] as XmlElement;
                int slot;
                if (profile == null ||
                    !TryReadJusticeIntStrict(
                        profile,
                        "slot",
                        -1,
                        0,
                        JusticePlayerProfileCount - 1,
                        out slot) ||
                    extracted[slot] != null ||
                    !TryExtractJusticeLegacyPolicyProfile(
                        profile,
                        slot,
                        out extracted[slot]))
                {
                    return false;
                }
            }
        }
        else
        {
            XmlElement custody = root.SelectSingleNode("Custody") as XmlElement;
            int lastSlot;
            int custodySlot;
            TryReadJusticeIntStrict(
                root,
                "lastCanonicalPlayerSlot",
                -1,
                -1,
                JusticePlayerProfileCount - 1,
                out lastSlot);
            if (custody == null ||
                !TryReadJusticeIntStrict(
                    custody,
                    "playerSlot",
                    -1,
                    -1,
                    JusticePlayerProfileCount - 1,
                    out custodySlot))
            {
                return false;
            }
            persistedActiveSlot = IsJusticeCanonicalProfileSlot(lastSlot)
                ? lastSlot
                : (IsJusticeCanonicalProfileSlot(custodySlot)
                    ? custodySlot
                    : currentCanonicalSlot);
            if (!IsJusticeCanonicalProfileSlot(persistedActiveSlot))
            {
                return false;
            }

            for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
            {
                extracted[slot] = CreateEmptyJusticeProfilePreservingEnabled(
                    slot,
                    false);
                extracted[slot].CustodySnapshot = CreateEmptyJusticePolicyCustody(
                    slot);
            }
            if (!TryExtractJusticeLegacyPolicyProfile(
                    root,
                    persistedActiveSlot,
                    out extracted[persistedActiveSlot]))
            {
                return false;
            }
        }

        profiles = extracted;
        return true;
    }

    private static bool TryExtractJusticeLegacyPolicyProfile(
        XmlElement container,
        int slot,
        out JusticePlayerProfileState profile)
    {
        profile = null;
        XmlElement caseElement = container == null
            ? null
            : container.SelectSingleNode("Case") as XmlElement;
        XmlElement custodyElement = container == null
            ? null
            : container.SelectSingleNode("Custody") as XmlElement;
        bool enabled;
        JusticeCustodyPersistenceSnapshot custody;
        if (caseElement == null || custodyElement == null ||
            !TryReadJusticeBoolStrict(
                caseElement,
                "enabled",
                false,
                out enabled) ||
            !TryReadJusticeLegacyPolicyCustody(
                custodyElement,
                slot,
                out custody))
        {
            return false;
        }

        profile = new JusticePlayerProfileState(slot)
        {
            CaseState = new JusticeCaseState { Enabled = enabled },
            RecordState = new JusticeRecordState(),
            CustodySnapshot = custody,
            CustodyXml = string.Empty,
            LastCanonicalPlayerModel = custody.PlayerModelHash
        };
        return true;
    }

    private static JusticeCustodyPersistenceSnapshot
        CreateEmptyJusticePolicyCustody(int slot)
    {
        return new JusticeCustodyPersistenceSnapshot(
            false,
            (int)JusticeCustodySite.None,
            false,
            false,
            0,
            0,
            false,
            false,
            (int)JusticeInventoryCustodyState.None,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            0,
            -1,
            JusticeUnarmedHash,
            false,
            false,
            null,
            null,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
    }

    private static bool TryReadJusticeLegacyPolicyCustody(
        XmlElement custody,
        int profileSlot,
        out JusticeCustodyPersistenceSnapshot snapshot)
    {
        snapshot = null;
        bool active;
        bool policeSuppression;
        bool policeDispatch;
        bool inventoryRemoved;
        bool controlsLocked;
        bool deferredRestore;
        bool waitingForRespawn;
        bool deathRebindPending;
        bool playerStateStored;
        bool storedInvincible;
        bool storedFrozen;
        bool storedCanRagdoll;
        int initialSentence;
        int inventoryState;
        int captureFailures;
        int removalFailures;
        int playerModel;
        int playerSlot;
        int releaseWeapon;
        JusticeCustodySite site;
        if (custody == null ||
            !Enum.TryParse(custody.GetAttribute("site"), true, out site) ||
            !Enum.IsDefined(typeof(JusticeCustodySite), site) ||
            !TryReadJusticeBoolStrict(custody, "active", false, out active) ||
            !TryReadJusticeBoolStrict(
                custody,
                "policeSuppressionApplied",
                false,
                out policeSuppression) ||
            !TryReadJusticeBoolStrict(
                custody,
                "policeDispatchDisabled",
                false,
                out policeDispatch) ||
            !TryReadJusticeIntStrict(
                custody,
                "initialSentenceSeconds",
                0,
                0,
                30 * 60,
                out initialSentence) ||
            !TryReadJusticeBoolStrict(
                custody,
                "inventoryRemoved",
                false,
                out inventoryRemoved) ||
            !TryReadJusticeBoolStrict(
                custody,
                "weaponControlsLocked",
                false,
                out controlsLocked) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryState",
                -1,
                -1,
                (int)JusticeInventoryCustodyState.RestoreAmbiguous,
                out inventoryState) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryCaptureFailures",
                0,
                0,
                100,
                out captureFailures) ||
            !TryReadJusticeIntStrict(
                custody,
                "inventoryRemovalFailures",
                0,
                0,
                100,
                out removalFailures) ||
            !TryReadJusticeBoolStrict(
                custody,
                "deferredInventoryRestore",
                false,
                out deferredRestore) ||
            !TryReadJusticeBoolStrict(
                custody,
                "waitingForRespawn",
                false,
                out waitingForRespawn) ||
            !TryReadJusticeBoolStrict(
                custody,
                "deathRebindPending",
                false,
                out deathRebindPending) ||
            !TryReadJusticeBoolStrict(
                custody,
                "playerStateStored",
                false,
                out playerStateStored) ||
            !TryReadJusticeBoolStrict(
                custody,
                "storedInvincible",
                false,
                out storedInvincible) ||
            !TryReadJusticeBoolStrict(
                custody,
                "storedFrozen",
                false,
                out storedFrozen) ||
            !TryReadJusticeBoolStrict(
                custody,
                "storedCanRagdoll",
                true,
                out storedCanRagdoll) ||
            !TryReadJusticeIntStrict(
                custody,
                "playerModelHash",
                0,
                int.MinValue,
                int.MaxValue,
                out playerModel) ||
            !TryReadJusticeIntStrict(
                custody,
                "playerSlot",
                -1,
                -1,
                JusticePlayerProfileCount - 1,
                out playerSlot) ||
            !TryReadJusticeIntStrict(
                custody,
                "releaseSelectedWeapon",
                JusticeUnarmedHash,
                int.MinValue,
                int.MaxValue,
                out releaseWeapon))
        {
            return false;
        }

        JusticeWeaponSnapshot legacyInventory =
            ReadJusticeWeaponSnapshotXml(custody);
        bool hasInventoryElement =
            custody.SelectSingleNode("InventorySnapshot") != null;
        if (hasInventoryElement && legacyInventory == null)
        {
            return false;
        }
        JusticeInventoryPersistenceSnapshot inventory =
            CreateJusticeInventoryPersistenceSnapshot(legacyInventory);
        if (inventoryState < 0)
        {
            inventoryState = deferredRestore
                ? (int)JusticeInventoryCustodyState.RestorePending
                : (inventoryRemoved
                    ? (int)JusticeInventoryCustodyState.RemovedVerified
                    : (inventory != null
                        ? (int)JusticeInventoryCustodyState.SnapshotPersisted
                        : (int)JusticeInventoryCustodyState.None));
        }

        if (!IsJusticeInventoryCustodyStateSemanticallyValid(
                (JusticeInventoryCustodyState)inventoryState,
                inventoryRemoved,
                controlsLocked,
                deferredRestore,
                legacyInventory))
        {
            // Je refuse d'acquitter une ancienne confiscation sans preuve de
            // restitution. Le XML reste intact jusqu'à récupération possible.
            return false;
        }

        bool identityRequired = active || policeSuppression || policeDispatch ||
            inventoryRemoved || controlsLocked || deferredRestore ||
            inventory != null || playerStateStored;
        if (identityRequired &&
            (playerSlot != profileSlot || playerModel == 0))
        {
            return false;
        }
        if (!playerStateStored &&
            (storedInvincible || storedFrozen || !storedCanRagdoll))
        {
            return false;
        }

        snapshot = new JusticeCustodyPersistenceSnapshot(
            active,
            (int)site,
            policeSuppression,
            policeDispatch,
            initialSentence,
            0,
            inventoryRemoved,
            controlsLocked,
            inventoryState,
            captureFailures,
            removalFailures,
            deferredRestore,
            waitingForRespawn,
            deathRebindPending,
            playerStateStored,
            storedInvincible,
            storedFrozen,
            storedCanRagdoll,
            playerModel,
            playerSlot,
            releaseWeapon,
            false,
            false,
            null,
            null,
            null,
            inventory,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0]);
        return true;
    }

    private static bool TryReadJusticeSentencePolicyRecoveryCustody(
        XmlElement custody,
        int profileSlot,
        out JusticeCustodyPersistenceSnapshot snapshot)
    {
        snapshot = null;
        if (custody == null ||
            custody.SelectSingleNode("FineDebitIntent") != null ||
            custody.SelectSingleNode("VoluntaryFinePaymentIntent") != null ||
            custody.SelectSingleNode("DisciplineIntent") != null ||
            custody.SelectSingleNode("ActivityCooldowns") != null)
        {
            return false;
        }

        bool legalReleaseAttempted;
        bool amnestyAttempted;
        if (!TryReadJusticeBoolStrict(
                custody,
                "legalReleaseWantedClearAttempted",
                false,
                out legalReleaseAttempted) ||
            !TryReadJusticeBoolStrict(
                custody,
                "amnestyWantedClearAttempted",
                false,
                out amnestyAttempted) ||
            legalReleaseAttempted || amnestyAttempted)
        {
            return false;
        }

        // Le lecteur courant autorise cette forme technique uniquement pour un
        // bit policy prouvé. Le lecteur de détention ordinaire doit continuer à
        // refuser un état physique attaché à un dossier déjà remis à zéro.
        return TryReadJusticeLegacyPolicyCustody(
            custody,
            profileSlot,
            out snapshot);
    }

    private static bool IsJusticeSentencePolicyRecoveryCustodyXmlValid(
        XmlElement container,
        int profileSlot)
    {
        XmlNodeList custodyNodes = container == null
            ? null
            : container.SelectNodes("Custody");
        if (custodyNodes == null || custodyNodes.Count != 1)
        {
            return false;
        }

        JusticeCustodyPersistenceSnapshot ignored;
        return TryReadJusticeSentencePolicyRecoveryCustody(
            custodyNodes[0] as XmlElement,
            profileSlot,
            out ignored);
    }

    private static JusticeInventoryPersistenceSnapshot
        CreateJusticeInventoryPersistenceSnapshot(JusticeWeaponSnapshot source)
    {
        if (source == null)
        {
            return null;
        }
        List<JusticeWeaponPersistenceSnapshot> weapons =
            new List<JusticeWeaponPersistenceSnapshot>(source.Weapons.Count);
        for (int index = 0; index < source.Weapons.Count; index++)
        {
            JusticeWeaponSnapshotItem weapon = source.Weapons[index];
            weapons.Add(new JusticeWeaponPersistenceSnapshot(
                weapon.WeaponHash,
                weapon.Ammo,
                weapon.AmmoInClip,
                weapon.Tint,
                weapon.ComponentHashes));
        }
        return new JusticeInventoryPersistenceSnapshot(
            source.IsValidated,
            source.SelectedWeaponHash,
            weapons);
    }

    private bool IsJusticeSentencePolicyRecoveryBlockingActiveProfile()
    {
        return _justicePolicyResetPublicationPending ||
               _justicePolicyResetRecoveryPublicationPending ||
               IsJusticeCanonicalProfileSlot(_justiceActivePlayerProfileSlot) &&
               (_justicePolicyResetRecoveryMask &
                (1 << _justiceActivePlayerProfileSlot)) != 0;
    }

    private static bool AreJusticeSentencePolicyRecoveryTokensValid(
        JusticePlayerProfileState[] profiles,
        int recoveryMask)
    {
        if (profiles == null || profiles.Length != JusticePlayerProfileCount ||
            recoveryMask < 0 || recoveryMask >= (1 << JusticePlayerProfileCount))
        {
            return false;
        }

        for (int slot = 0; slot < JusticePlayerProfileCount; slot++)
        {
            bool tokenExpected = (recoveryMask & (1 << slot)) != 0;
            JusticePlayerProfileState profile = profiles[slot];
            JusticeCustodyPersistenceSnapshot token = profile == null
                ? null
                : profile.CustodySnapshot;
            if (!tokenExpected)
            {
                if (IsJusticeSentencePolicyRecoveryToken(profile, slot))
                {
                    // Je refuse un jeton technique orphelin : sans son bit, le
                    // contrôleur ne pourrait ni le restaurer ni l'acquitter.
                    return false;
                }
                continue;
            }
            if (!IsJusticeSentencePolicyRecoveryToken(profile, slot) ||
                token.InitialSentenceSeconds != 0 ||
                token.ActivityReductionSeconds != 0 ||
                token.InventoryRemoved || token.WeaponControlsLocked ||
                token.InventoryCaptureFailures != 0 ||
                token.InventoryRemovalFailures != 0 ||
                token.WaitingForRespawn || token.DeathRebindPending ||
                token.LegalReleaseWantedClearAttempted ||
                token.AmnestyWantedClearAttempted ||
                (token.InventorySnapshot == null &&
                 token.DeferredInventoryRestore) ||
                (token.InventorySnapshot != null &&
                 (!token.DeferredInventoryRestore ||
                  token.InventoryState !=
                      (int)JusticeInventoryCustodyState.RestorePending)) ||
                token.FineDebitIntent != null ||
                token.VoluntaryPaymentIntent != null ||
                token.DisciplineIntent != null ||
                token.HasActivityCooldownContainer || token.Cooldowns.Count != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsJusticeSentencePolicyRecoveryToken(
        JusticePlayerProfileState profile,
        int slot)
    {
        if (profile == null || profile.CaseState == null ||
            profile.RecordState == null || profile.CustodySnapshot == null)
        {
            return false;
        }

        JusticeCaseState state = profile.CaseState;
        JusticeRecordState record = profile.RecordState;
        JusticeCustodyPersistenceSnapshot token = profile.CustodySnapshot;
        bool resetCaseIsEmpty = state.Phase == JusticePhase.AtLarge &&
            state.Charges.Count == 0 && state.ActiveScore == 0 &&
            state.FineDue == 0L && state.VoluntaryFinePaid == 0L &&
            state.FineInDispute == 0L && state.SentenceSeconds == 0 &&
            state.CustodyGuardPenaltySeconds == 0L &&
            !state.HasWarrant && !state.EscapeWantedMinimumPending &&
            !state.EscapeWantedMinimumAttempted &&
            string.IsNullOrWhiteSpace(state.WantedEpisodeId) &&
            string.IsNullOrWhiteSpace(state.CustodyEpisodeId) &&
            !state.LastCrimeKind.HasValue &&
            string.IsNullOrWhiteSpace(state.LastCrimeLabel) &&
            state.CompletedOperationIds.Count == 0 &&
            state.ProcessedIncidentIds.Count == 0 &&
            state.FleeingChargedEpisodeIds.Count == 0 &&
            state.EscapeChargedEpisodeIds.Count == 0;
        bool resetRecordIsEmpty = record.RecidivismIndex == 0 &&
            record.CleanGameplaySeconds == 0 && record.AppliedCleanDecay == 0 &&
            record.Convictions.Count == 0 &&
            record.AppliedConvictionIds.Count == 0 &&
            string.IsNullOrWhiteSpace(record.PinnedConvictionId);
        return profile.Slot == slot && token.PlayerSlot == slot &&
               resetCaseIsEmpty && resetRecordIsEmpty &&
               !profile.PendingDeathCapture &&
               !profile.PendingAmnestyWantedClear &&
               !profile.PendingLegalReleaseFinalization &&
               RequiresJusticeSentencePolicyRecovery(token);
    }

    private bool HasJusticeSentencePolicyPoliceRecoveryToken()
    {
        if (_justicePlayerProfiles == null)
        {
            return false;
        }

        for (int slot = 0; slot < _justicePlayerProfiles.Length; slot++)
        {
            int bit = 1 << slot;
            JusticePlayerProfileState profile = _justicePlayerProfiles[slot];
            JusticeCustodyPersistenceSnapshot token = profile == null
                ? null
                : profile.CustodySnapshot;
            if ((_justicePolicyResetRecoveryMask & bit) != 0 && token != null &&
                (token.PoliceSuppressionApplied ||
                 token.PoliceDispatchDisabled))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldRestoreJusticeSentencePolicyPoliceState(
        bool policyTokenRequiresPoliceRecovery,
        bool policyRecoveryRetryPending)
    {
        // Je n'utilise jamais les seuls flags globaux comme preuve : une
        // détention créée après le reset possède légitimement les mêmes flags.
        return policyTokenRequiresPoliceRecovery ||
               policyRecoveryRetryPending;
    }

    private bool ResumeJusticeSentencePolicyUpgradeIfRequired()
    {
        if (_justiceSentencePolicyVersion != JusticeSentencePolicyVersion)
        {
            return false;
        }

        int activeBit = IsJusticeCanonicalProfileSlot(
            _justiceActivePlayerProfileSlot)
                ? 1 << _justiceActivePlayerProfileSlot
                : 0;
        int expectedDurableMask = _justicePolicyResetRecoveryMask;
        bool policyPoliceRecoveryRequired =
            HasJusticeSentencePolicyPoliceRecoveryToken();
        bool policyPoliceRecoveryRetryPending =
            _justicePolicyResetRecoveryPublicationPending &&
            _justicePoliceSuppressionRestorePending;
        if (_justicePolicyResetPublicationPending ||
            _justicePolicyResetRecoveryPublicationPending ||
            (activeBit != 0 && (expectedDurableMask & activeBit) != 0))
        {
            if (!EnsureJusticeSentencePolicySnapshotRedundant(
                    expectedDurableMask))
            {
                return false;
            }

            _justicePolicyResetPublicationPending = false;
            _justicePolicyResetRecoveryPublicationPending = false;
            DeleteJusticeSentencePolicyQuarantineIfPresent();
        }

        if (ShouldRestoreJusticeSentencePolicyPoliceState(
                policyPoliceRecoveryRequired,
                policyPoliceRecoveryRetryPending) &&
            (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
             _justicePoliceSuppressionActive ||
             _justicePoliceSuppressionRestorePending))
        {
            // Ces natives sont globales au joueur GTA. Je les rends dès le
            // premier héros prouvé, même si le jeton qui les portait appartient
            // à un profil inactif qui n'a aucune autre restitution à attendre.
            SetJusticeCustodyPoliceSuppression(false);
            if (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
                _justicePoliceSuppressionActive ||
                _justicePoliceSuppressionRestorePending)
            {
                _justiceProfileContextBlocked = true;
                return false;
            }
        }
        if (_justicePolicyResetRecoveryPublicationPending)
        {
            return false;
        }

        if (activeBit == 0 ||
            (_justicePolicyResetRecoveryMask & activeBit) == 0)
        {
            return true;
        }

        EnsureJusticePlayerProfilesInitialized();
        JusticePlayerProfileState active =
            _justicePlayerProfiles[_justiceActivePlayerProfileSlot];
        JusticeCustodyPersistenceSnapshot token = active == null
            ? null
            : active.CustodySnapshot;
        if (token == null || token.PlayerSlot != _justiceActivePlayerProfileSlot)
        {
            throw new InvalidDataException(
                "Le jeton de restitution du reset Justice a perdu son identité.");
        }

        bool worldRecoveryAlreadyApplied =
            (_justicePolicyResetWorldRecoveryAppliedMask & activeBit) != 0;
        int recoveryPlayerModel = token.PlayerModelHash != 0
            ? token.PlayerModelHash
            : _justiceCustodyPlayerModelHash;
        int recoveryPlayerSlot = token.PlayerSlot;
        if (!worldRecoveryAlreadyApplied)
        {
            if (!TryRestoreJusticeSentencePolicyActivePlayer(token))
            {
                _justiceProfileContextBlocked = true;
                return false;
            }

            // Ce latch est volontairement runtime. Après un crash, le jeton
            // durable rejoue l'opération idempotente; dans la session courante,
            // il évite de réarmer une détention déjà démontée pendant un retry
            // d'inventaire ou de police.
            _justicePolicyResetWorldRecoveryAppliedMask |= activeBit;
        }

        if (_justiceDeferredInventoryRestore &&
            !TryFinalizeJusticeSentencePolicyDeferredInventoryRecovery())
        {
            PreserveJusticeSentencePolicyRecoveryIdentity(
                recoveryPlayerModel,
                recoveryPlayerSlot);
            _justiceProfileContextBlocked = true;
            return false;
        }

        // Le reset de politique peut avoir terminé la sortie tout en laissant une native
        // police ou le masque de respawn en retry. Je les réaffirme ici, sous le
        // même bit durable, avant d'autoriser l'acquittement du jeton.
        if (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending)
        {
            SetJusticeCustodyPoliceSuppression(false);
        }
        if (_justiceCustodyRespawnTransferPending ||
            _justiceCustodyRespawnRestorePending ||
            _justiceCustodyRespawnMaskNeedsRearm)
        {
            TryRestoreJusticeCustodyRespawnTransferMask();
        }
        if (HasJusticeSentencePolicyPhysicalRecoveryState())
        {
            PreserveJusticeSentencePolicyRecoveryIdentity(
                recoveryPlayerModel,
                recoveryPlayerSlot);
            _justiceProfileContextBlocked = true;
            return false;
        }

        // Je nettoie enfin l'identité technique que la restitution différée a
        // pu conserver. Aucun état physique ne subsiste à ce point.
        ResetJusticeCustodyPersistentFields(false);
        if (HasJusticeSentencePolicyPhysicalRecoveryState())
        {
            PreserveJusticeSentencePolicyRecoveryIdentity(
                recoveryPlayerModel,
                recoveryPlayerSlot);
            _justiceProfileContextBlocked = true;
            return false;
        }

        bool enabled = active.CaseState != null && active.CaseState.Enabled;
        JusticePlayerProfileState replacement =
            CreateEmptyJusticeProfilePreservingEnabled(
                _justiceActivePlayerProfileSlot,
                enabled);
        _justicePlayerProfiles[_justiceActivePlayerProfileSlot] = replacement;
        _justiceCaseState = replacement.CaseState;
        _justiceRecordState = replacement.RecordState;
        _justiceEnabled = enabled;
        _justicePolicyResetRecoveryMask &= ~activeBit;
        _justicePolicyResetWorldRecoveryAppliedMask &= ~activeBit;
        _justicePolicyResetRecoveryPublicationPending = true;
        JusticeMarkStateDirty();
        ShowJusticeProfileStatus(
            "Justice : ancien dossier effacé, restauration terminée.",
            4200);
        LogInfo(
            "Justice.Migration.Barème",
            "Jeton technique de restauration acquitté pour " +
            GetJusticeProfileDisplayName(_justiceActivePlayerProfileSlot) + ".");
        return false;
    }

    private bool TryRestoreJusticeSentencePolicyActivePlayer(
        JusticeCustodyPersistenceSnapshot token)
    {
        if (token == null ||
            token.PlayerSlot != _justiceActivePlayerProfileSlot)
        {
            return false;
        }

        Ped player = Game.Player.Character;
        if (!JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player))
        {
            return false;
        }

        bool inventoryRecoveryRequired = token.InventorySnapshot != null;
        if (inventoryRecoveryRequired &&
            (_justiceWeaponSnapshot == null ||
             !ValidateJusticeWeaponSnapshot(_justiceWeaponSnapshot) ||
             !RestoreJusticeWeaponSnapshotMergeSafe(player, true, true)))
        {
            return false;
        }

        // Je restaure le protagoniste sans passer par une libération judiciaire :
        // aucun RemoveAll, mouvement d'argent, wanted ou opération legacy ne peut
        // ainsi être rejoué par ce reset technique.
        if (!RestoreJusticeCustodyPlayerTransientStateForRollback(player))
        {
            return false;
        }
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceDeferredInventoryRestore = false;
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
        _justiceNextDeferredInventoryRestoreAt = 0;
        _justiceNextInventoryPersistenceRetryAt = 0;

        if ((_justiceCustodyRespawnTransferPending ||
             _justiceCustodyRespawnRestorePending ||
             _justiceCustodyRespawnMaskNeedsRearm) &&
            !TryRestoreJusticeCustodyRespawnTransferMask())
        {
            return false;
        }

        JusticeCustodyLayout layout = GetJusticeCustodyLayoutForSite(
            (JusticeCustodySite)token.Site);
        if (layout != null &&
            IsInsideJusticeCustodyLayout(layout, player.Position))
        {
            bool releasedOutside = false;
            try
            {
                _activeInteriorSession = null;
                ClearInteriorRenderingFocusSafe(player);
                TeleportPlayerWithFadeSafe(
                    player,
                    layout.ReleasePosition,
                    layout.ReleaseHeading);
                releasedOutside = IsJusticeTeleportVerified(
                    player,
                    layout.ReleasePosition,
                    8.0f);
            }
            catch (Exception exception)
            {
                LogException("Justice.Migration.Barème.Sortie", exception);
            }
            if (!releasedOutside)
            {
                releasedOutside = TryJusticeEmergencyTeleport(
                    player,
                    layout.ReleasePosition,
                    layout.ReleaseHeading);
            }
            if (!releasedOutside)
            {
                return false;
            }
        }

        SetJusticeCustodyPoliceSuppression(false);
        if (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending)
        {
            return false;
        }

        _justiceCustodyRuntimeActive = false;
        _justiceCustodyTransferPending = false;
        _justiceCustodyResumePending = false;
        _justiceCustodyWaitingForRespawn = false;
        _justiceOutsideCustodySinceAt = 0;
        CleanupJusticeCustodyEntitiesAndGroups();
        if (_justicePoliceIgnoreApplied || _justicePoliceDispatchDisabled ||
            _justicePoliceSuppressionActive ||
            _justicePoliceSuppressionRestorePending)
        {
            return false;
        }

        _justiceCustodyPlayerStateStored = false;
        ResetJusticeCustodyPersistentFields(false);
        JusticeMarkStateDirty();
        return !HasJusticeSentencePolicyPhysicalRecoveryState();
    }

    private bool TryFinalizeJusticeSentencePolicyDeferredInventoryRecovery()
    {
        if (!_justiceDeferredInventoryRestore)
        {
            return true;
        }

        Ped player = Game.Player.Character;
        if (_justiceWeaponSnapshot == null ||
            !JusticeCustodyCanMutateWorld(player) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player) ||
            !RestoreJusticeWeaponSnapshotMergeSafe(player, true, true))
        {
            return false;
        }

        // Le jeton policy reste la preuve durable jusqu'au commit final du
        // masque. Je peux donc acquitter le merge en mémoire sans créer un état
        // intermédiaire « masque présent / snapshot absent » sur disque.
        _justiceDeferredInventoryRestore = false;
        _justiceWeaponSnapshot = null;
        _justiceInventoryRemoved = false;
        _justiceWeaponControlsLocked = false;
        _justiceInventoryCustodyState = JusticeInventoryCustodyState.None;
        _justiceNextDeferredInventoryRestoreAt = 0;
        JusticeMarkStateDirty();
        return true;
    }

    private bool HasJusticeSentencePolicyPhysicalRecoveryState()
    {
        return HasJusticeCustodyRecoveryState() ||
               _justiceDeferredInventoryRestore ||
               _justiceCustodyRuntimeActive ||
               _justiceCustodyTransferPending ||
               _justiceCustodyResumePending ||
               _justiceCustodyWaitingForRespawn ||
               _justiceCustodyDeathRebindPending ||
               _justiceCustodyDeathStatePersistencePending ||
               _justiceCustodyRespawnTransferPending ||
               _justiceCustodyRespawnRestorePending ||
               _justiceCustodyRespawnMaskNeedsRearm ||
               _justiceInventoryCustodyState != JusticeInventoryCustodyState.None;
    }

    private void PreserveJusticeSentencePolicyRecoveryIdentity(
        int playerModel,
        int playerSlot)
    {
        if (_justiceCustodyPlayerModelHash == 0 && playerModel != 0)
        {
            _justiceCustodyPlayerModelHash = playerModel;
        }
        if (!IsJusticeCanonicalProfileSlot(_justiceCustodyPlayerSlot) &&
            IsJusticeCanonicalProfileSlot(playerSlot))
        {
            _justiceCustodyPlayerSlot = playerSlot;
        }
    }

    private bool EnsureJusticeSentencePolicySnapshotRedundant(int expectedMask)
    {
        string directory = GetSaveDirectory();
        string primary = Path.Combine(directory, JusticeStateFileName);
        string backup = primary + ".bak";
        if (IsJusticeSentencePolicyResetDocument(primary, expectedMask) &&
            IsJusticeSentencePolicyResetDocument(backup, expectedMask))
        {
            return true;
        }

        JusticeMarkStateDirty();
        JusticeFlushStateNow();
        return false;
    }

    private static bool IsJusticeSentencePolicyResetDocument(
        string path,
        int expectedMask)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }
            JusticePersistenceSnapshot snapshot;
            string error;
            JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
            return codec.TryDeserialize(
                       File.ReadAllBytes(path),
                       out snapshot,
                       out error) &&
                   snapshot != null &&
                   ReadJusticeSentencePolicyVersion(snapshot) ==
                       JusticeSentencePolicyVersion &&
                   ReadJusticePolicyResetRecoveryMask(snapshot) == expectedMask &&
                   snapshot.Profiles.Count == JusticePlayerProfileCount;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJusticeSentencePolicySnapshotPairRedundant(
        string loadedPath,
        int expectedMask)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(loadedPath);
        string primary = fullPath.EndsWith(
            ".bak",
            StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(0, fullPath.Length - 4)
                : fullPath;
        return IsJusticeSentencePolicyResetDocument(primary, expectedMask) &&
               IsJusticeSentencePolicyResetDocument(
                   primary + ".bak",
                   expectedMask);
    }

    private void PrepareJusticeSentencePolicyQuarantine(string canonicalDirectory)
    {
        if (!_justicePolicyResetPublicationPending)
        {
            return;
        }

        string fullCanonicalDirectory = Path.GetFullPath(canonicalDirectory);
        string quarantineDirectory = Path.Combine(
            fullCanonicalDirectory,
            JusticeSentencePolicyQuarantineDirectoryName);
        Directory.CreateDirectory(quarantineDirectory);
        string completionMarker = Path.Combine(
            quarantineDirectory,
            JusticeSentencePolicyQuarantineCompleteName);
        if (File.Exists(completionMarker))
        {
            // Le marker est publié seulement après l'isolement complet. Dès cet
            // instant les chemins canoniques appartiennent au nouveau repository.
            return;
        }

        string source = string.IsNullOrWhiteSpace(
            _justicePolicyResetLegacySourcePath)
                ? string.Empty
                : Path.GetFullPath(_justicePolicyResetLegacySourcePath);
        string quarantinePrefix = Path.GetFullPath(quarantineDirectory) +
            Path.DirectorySeparatorChar;
        bool sourceAlreadyQuarantined = source.Length > 0 &&
            source.StartsWith(
                quarantinePrefix,
                StringComparison.OrdinalIgnoreCase);
        string canonicalPrimary = Path.Combine(
            fullCanonicalDirectory,
            JusticeStateFileName);
        string quarantinedPrimary = Path.Combine(
            quarantineDirectory,
            JusticeSentencePolicyQuarantinePrimaryName);
        string quarantinedBackup = Path.Combine(
            quarantineDirectory,
            JusticeSentencePolicyQuarantineBackupName);
        bool canonicalGenerationIsCurrent =
            IsJusticeSentencePolicyResetDocument(
                canonicalPrimary,
                _justicePolicyResetRecoveryMask) ||
            IsJusticeSentencePolicyResetDocument(
                canonicalPrimary + ".bak",
                _justicePolicyResetRecoveryMask);

        if (!canonicalGenerationIsCurrent)
        {
            // Je remplis deux emplacements de preuve sans supposer que le
            // loader n'a pas réparé le primaire depuis le backup entre deux
            // crashs. Un doublon est supprimé; une seconde révision est gardée.
            MoveJusticeSentencePolicyLegacyEvidence(
                canonicalPrimary,
                quarantinedPrimary,
                quarantinedBackup);
            MoveJusticeSentencePolicyLegacyEvidence(
                canonicalPrimary + ".bak",
                quarantinedBackup,
                quarantinedPrimary);
            MoveJusticeSentencePolicyLegacyFile(
                Path.Combine(
                    fullCanonicalDirectory,
                    JusticeWalFileName),
                Path.Combine(
                    quarantineDirectory,
                    JusticeSentencePolicyQuarantineWalName));
        }

        if (!sourceAlreadyQuarantined && source.Length > 0)
        {
            string sourcePrimary = source.EndsWith(
                ".bak",
                StringComparison.OrdinalIgnoreCase)
                    ? source.Substring(0, source.Length - 4)
                    : source;
            if (!string.Equals(
                    Path.GetFullPath(sourcePrimary),
                    Path.GetFullPath(canonicalPrimary),
                    StringComparison.OrdinalIgnoreCase))
            {
                string sourceQuarantinedPrimary = Path.Combine(
                    quarantineDirectory,
                    JusticeSentencePolicyQuarantineSourcePrimaryName);
                string sourceQuarantinedBackup = Path.Combine(
                    quarantineDirectory,
                    JusticeSentencePolicyQuarantineSourceBackupName);
                // Une ancienne sauvegarde provenant d'un dossier de recherche
                // legacy reste séparée des preuves canoniques.
                MoveJusticeSentencePolicyLegacyEvidence(
                    sourcePrimary,
                    sourceQuarantinedPrimary,
                    sourceQuarantinedBackup);
                MoveJusticeSentencePolicyLegacyEvidence(
                    sourcePrimary + ".bak",
                    sourceQuarantinedBackup,
                    sourceQuarantinedPrimary);
                MoveJusticeSentencePolicyLegacyFile(
                    Path.Combine(
                        Path.GetDirectoryName(sourcePrimary),
                        JusticeWalFileName),
                    Path.Combine(
                        quarantineDirectory,
                        JusticeSentencePolicyQuarantineSourceWalName));
            }
        }

        WriteJusticeSentencePolicyQuarantineCompletionMarker(
            quarantineDirectory);
    }

    private static void MoveJusticeSentencePolicyLegacyEvidence(
        string source,
        string preferredDestination,
        string alternateDestination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return;
        }

        if (!File.Exists(preferredDestination))
        {
            // Je conserve bien les deux noms primaire/backup, même quand leurs
            // octets sont identiques : chacun prouve son chemin legacy d'origine.
            File.Move(source, preferredDestination);
            return;
        }

        byte[] sourceBytes = File.ReadAllBytes(source);
        if (AreJusticePersistenceBytesEqual(
                sourceBytes,
                File.ReadAllBytes(preferredDestination)))
        {
            File.Delete(source);
            return;
        }
        if (!File.Exists(alternateDestination))
        {
            File.Move(source, alternateDestination);
            return;
        }
        if (AreJusticePersistenceBytesEqual(
                sourceBytes,
                File.ReadAllBytes(alternateDestination)))
        {
            File.Delete(source);
            return;
        }

        throw new InvalidDataException(
            "Deux preuves différentes occupent déjà la quarantaine Justice.");
    }

    private static void WriteJusticeSentencePolicyQuarantineCompletionMarker(
        string quarantineDirectory)
    {
        string marker = Path.Combine(
            quarantineDirectory,
            JusticeSentencePolicyQuarantineCompleteName);
        if (File.Exists(marker))
        {
            return;
        }

        string temporary = Path.Combine(
            quarantineDirectory,
            JusticeSentencePolicyQuarantineCompleteTempName);
        byte[] content = new UTF8Encoding(false).GetBytes(
            "sentencePolicyVersion=2\n");
        using (FileStream stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(content, 0, content.Length);
            stream.Flush(true);
        }

        if (File.Exists(marker))
        {
            File.Delete(temporary);
            return;
        }
        File.Move(temporary, marker);
    }

    private static void MoveJusticeSentencePolicyLegacyFile(
        string source,
        string destination)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(destination))
        {
            return;
        }
        if (string.Equals(
                Path.GetFullPath(source),
                Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!File.Exists(source))
        {
            return;
        }
        if (File.Exists(destination))
        {
            byte[] sourceBytes = File.ReadAllBytes(source);
            byte[] destinationBytes = File.ReadAllBytes(destination);
            if (!AreJusticePersistenceBytesEqual(sourceBytes, destinationBytes))
            {
                throw new InvalidDataException(
                    "Une quarantaine Justice différente existe déjà.");
            }
            File.Delete(source);
            return;
        }
        File.Move(source, destination);
    }

    private string FindJusticeSentencePolicyQuarantineState()
    {
        string directory = Path.Combine(
            GetSaveDirectory(),
            JusticeSentencePolicyQuarantineDirectoryName);
        string primary = Path.Combine(
            directory,
            JusticeSentencePolicyQuarantinePrimaryName);
        if (File.Exists(primary))
        {
            return primary;
        }
        string backup = Path.Combine(
            directory,
            JusticeSentencePolicyQuarantineBackupName);
        if (File.Exists(backup))
        {
            return backup;
        }
        string sourcePrimary = Path.Combine(
            directory,
            JusticeSentencePolicyQuarantineSourcePrimaryName);
        if (File.Exists(sourcePrimary))
        {
            return sourcePrimary;
        }
        string sourceBackup = Path.Combine(
            directory,
            JusticeSentencePolicyQuarantineSourceBackupName);
        return File.Exists(sourceBackup) ? sourceBackup : string.Empty;
    }

    private bool HasJusticeSentencePolicyQuarantine()
    {
        string directory = Path.Combine(
            GetSaveDirectory(),
            JusticeSentencePolicyQuarantineDirectoryName);
        try
        {
            return Directory.Exists(directory) &&
                   Directory.GetFileSystemEntries(directory).Length > 0;
        }
        catch
        {
            // Je bloque le runtime en cas d'accès refusé : une quarantaine dont
            // l'état est inconnu ne doit jamais être considérée comme nettoyée.
            return true;
        }
    }

    private void DeleteJusticeSentencePolicyQuarantineIfPresent()
    {
        string directory = Path.Combine(
            GetSaveDirectory(),
            JusticeSentencePolicyQuarantineDirectoryName);
        if (!Directory.Exists(directory))
        {
            return;
        }

        string[] knownFiles =
        {
            JusticeSentencePolicyQuarantinePrimaryName,
            JusticeSentencePolicyQuarantineBackupName,
            JusticeSentencePolicyQuarantineWalName,
            JusticeSentencePolicyQuarantineSourcePrimaryName,
            JusticeSentencePolicyQuarantineSourceBackupName,
            JusticeSentencePolicyQuarantineSourceWalName,
            JusticeSentencePolicyQuarantineCompleteName,
            JusticeSentencePolicyQuarantineCompleteTempName
        };
        for (int index = 0; index < knownFiles.Length; index++)
        {
            string path = Path.Combine(directory, knownFiles[index]);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        if (Directory.GetFileSystemEntries(directory).Length == 0)
        {
            Directory.Delete(directory, false);
        }
    }
}
