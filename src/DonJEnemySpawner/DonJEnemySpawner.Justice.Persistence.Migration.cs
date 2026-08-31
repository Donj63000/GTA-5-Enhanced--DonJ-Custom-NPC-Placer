using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

public sealed partial class DonJEnemySpawner
{
    private static bool TryNormalizeJusticeV2DocumentForLegacyReader(
        XmlDocument document,
        out XmlElement legacyRoot,
        out JusticePersistenceSnapshot snapshot,
        out string error)
    {
        legacyRoot = null;
        snapshot = null;
        error = string.Empty;
        try
        {
            if (document == null || document.DocumentElement == null)
            {
                error = "Document Justice v2 absent.";
                return false;
            }

            byte[] serialized = new UTF8Encoding(false).GetBytes(document.OuterXml);
            if (!new JusticeXmlPersistenceCodec().TryDeserialize(
                    serialized,
                    out snapshot,
                    out error) ||
                snapshot == null)
            {
                return false;
            }

            return TryCreateLegacyJusticeRootFromSnapshot(
                snapshot,
                out legacyRoot,
                out error);
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            legacyRoot = null;
            snapshot = null;
            return false;
        }
    }

    private static bool TryCreateLegacyJusticeRootFromSnapshot(
        JusticePersistenceSnapshot snapshot,
        out XmlElement legacyRoot,
        out string error)
    {
        legacyRoot = null;
        error = string.Empty;
        try
        {
            if (snapshot == null)
            {
                error = "Snapshot Justice v2 absent.";
                return false;
            }

            JusticePersistenceProfileSnapshot active =
                FindJusticePersistenceProfile(snapshot, snapshot.ActiveProfileSlot);
            if (active == null)
            {
                error = "Profil actif Justice v2 absent.";
                return false;
            }

            XmlDocument legacy = new XmlDocument { XmlResolver = null };
            XmlElement root = legacy.CreateElement("JusticeState");
            legacy.AppendChild(root);
            root.SetAttribute("version", JusticeStateVersion.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute(
                "activePlayerSlot",
                snapshot.ActiveProfileSlot.ToString(CultureInfo.InvariantCulture));
            CopySnapshotFieldToAttribute(
                root,
                snapshot.GlobalFields,
                "nextIdentityGeneration",
                "0");
            CopySnapshotFieldToAttribute(
                root,
                snapshot.GlobalFields,
                "policeIntegrationMode",
                ((int)JusticePoliceIntegrationMode.FreeroamBestEffort).ToString(
                    CultureInfo.InvariantCulture));

            string caseXml = JusticeXmlPersistenceCodec.GetFieldValue(
                active.Fields,
                "Case",
                string.Empty);
            XmlDocument activeCase = LoadJusticeXmlFragment(caseXml);
            if (activeCase.DocumentElement == null ||
                !string.Equals(activeCase.DocumentElement.Name, "Case", StringComparison.Ordinal))
            {
                error = "Dossier actif Justice v2 invalide.";
                return false;
            }
            root.SetAttribute("enabled", activeCase.DocumentElement.GetAttribute("enabled"));
            CopyProfileRecoveryToLegacyRoot(root, active, snapshot.ActiveProfileSlot);
            AppendJusticeSnapshotFragment(legacy, root, active.Fields, "Case");
            AppendJusticeSnapshotFragment(legacy, root, active.Fields, "Record");
            AppendJusticeSnapshotFragment(legacy, root, active.Fields, "Custody");

            XmlElement playerProfiles = legacy.CreateElement("PlayerProfiles");
            root.AppendChild(playerProfiles);
            List<JusticePersistenceProfileSnapshot> ordered =
                new List<JusticePersistenceProfileSnapshot>(snapshot.Profiles);
            ordered.Sort(delegate(
                JusticePersistenceProfileSnapshot left,
                JusticePersistenceProfileSnapshot right)
            {
                return left.Slot.CompareTo(right.Slot);
            });
            for (int index = 0; index < ordered.Count; index++)
            {
                JusticePersistenceProfileSnapshot source = ordered[index];
                XmlElement profile = legacy.CreateElement("Profile");
                profile.SetAttribute("slot", source.Slot.ToString(CultureInfo.InvariantCulture));
                for (int fieldIndex = 0; fieldIndex < source.Fields.Count; fieldIndex++)
                {
                    JusticePersistenceField field = source.Fields[fieldIndex];
                    if (!IsJusticePersistenceFragment(field.Path))
                    {
                        profile.SetAttribute(field.Path, field.Value);
                    }
                }
                AppendJusticeSnapshotFragment(legacy, profile, source.Fields, "Case");
                AppendJusticeSnapshotFragment(legacy, profile, source.Fields, "Record");
                AppendJusticeSnapshotFragment(legacy, profile, source.Fields, "Custody");
                playerProfiles.AppendChild(profile);
            }

            legacyRoot = root;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            legacyRoot = null;
            return false;
        }
    }

    internal static bool TryValidateJusticePersistenceSnapshotSemantics(
        JusticePersistenceSnapshot snapshot,
        out string error)
    {
        error = string.Empty;
        try
        {
            XmlElement legacyRoot;
            if (!TryCreateLegacyJusticeRootFromSnapshot(
                    snapshot,
                    out legacyRoot,
                    out error) ||
                legacyRoot == null)
            {
                return false;
            }
            int sentencePolicyVersion;
            int sentencePolicyRecoveryMask = 0;
            if (!TryReadJusticeSentencePolicyVersionStrict(
                    snapshot,
                    out sentencePolicyVersion) ||
                (sentencePolicyVersion == JusticeSentencePolicyVersion &&
                 (sentencePolicyRecoveryMask =
                     ReadJusticePolicyResetRecoveryMask(snapshot)) < 0))
            {
                error = "Marqueur de politique Justice v2 invalide.";
                return false;
            }
            if (sentencePolicyVersion == JusticeSentencePolicyVersion &&
                ContainsJusticeRemovedSentencePolicyCustodyFields(legacyRoot))
            {
                error =
                    "Le snapshot policy v2 contient une ancienne activité ou discipline.";
                return false;
            }

            int nextIdentityGeneration;
            int policeIntegrationMode;
            int lastCanonicalSlot;
            int lastCanonicalModel;
            if (!TryReadJusticeSnapshotInt(
                    snapshot.GlobalFields,
                    "nextIdentityGeneration",
                    0,
                    int.MaxValue - 1,
                    out nextIdentityGeneration) ||
                !TryReadJusticeSnapshotInt(
                    snapshot.GlobalFields,
                    "policeIntegrationMode",
                    (int)JusticePoliceIntegrationMode.Disabled,
                    (int)JusticePoliceIntegrationMode.Force,
                    out policeIntegrationMode) ||
                !TryReadJusticeSnapshotInt(
                    snapshot.GlobalFields,
                    "lastCanonicalPlayerSlot",
                    -1,
                    JusticePlayerProfileCount - 1,
                    out lastCanonicalSlot) ||
                !TryReadJusticeSnapshotInt(
                    snapshot.GlobalFields,
                    "lastCanonicalPlayerModel",
                    int.MinValue,
                    int.MaxValue,
                    out lastCanonicalModel) ||
                lastCanonicalSlot != snapshot.ActiveProfileSlot)
            {
                error = "Champs globaux Justice v2 incohérents.";
                return false;
            }

            JusticePlayerProfileState[] profiles;
            int activeSlot;
            bool hasProfiles;
            if (!TryReadJusticePlayerProfilesXml(
                    legacyRoot,
                    out profiles,
                    out activeSlot,
                    out hasProfiles,
                    sentencePolicyRecoveryMask) ||
                !hasProfiles || profiles == null ||
                activeSlot != snapshot.ActiveProfileSlot ||
                !AreJusticeProfileMirrorNodesEqual(
                    legacyRoot,
                    profiles,
                    activeSlot))
            {
                error = "Invariants métier des profils Justice v2 invalides.";
                return false;
            }
            if (sentencePolicyVersion == JusticeSentencePolicyVersion &&
                !AreJusticeSentencePolicyRecoveryTokensValid(
                    profiles,
                    sentencePolicyRecoveryMask))
            {
                error =
                    "Jetons techniques de récupération Justice v2 invalides.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static bool TryReadJusticeSnapshotInt(
        IReadOnlyList<JusticePersistenceField> fields,
        string path,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        string text = JusticeXmlPersistenceCodec.GetFieldValue(
            fields,
            path,
            string.Empty);
        return int.TryParse(
                   text,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value >= minimum && value <= maximum;
    }

    private static void CopyProfileRecoveryToLegacyRoot(
        XmlElement root,
        JusticePersistenceProfileSnapshot profile,
        int activeSlot)
    {
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingDeathCapture",
            "false");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingDeathCapturePlayerSlot",
            "-1");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingDeathCapturePlayerModel",
            "0");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingAmnestyWantedClear",
            "false");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingLegalReleaseFinalization",
            "false");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingLegalReleaseSite",
            "0");
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "pendingLegalReleaseSelectedWeapon",
            "0");
        root.SetAttribute(
            "lastCanonicalPlayerSlot",
            activeSlot.ToString(CultureInfo.InvariantCulture));
        CopySnapshotFieldToAttribute(
            root,
            profile.Fields,
            "lastCanonicalPlayerModel",
            "0");
    }

    private static void CopySnapshotFieldToAttribute(
        XmlElement target,
        IReadOnlyList<JusticePersistenceField> fields,
        string path,
        string fallback)
    {
        target.SetAttribute(
            path,
            JusticeXmlPersistenceCodec.GetFieldValue(fields, path, fallback));
    }

    private static void AppendJusticeSnapshotFragment(
        XmlDocument targetDocument,
        XmlElement target,
        IReadOnlyList<JusticePersistenceField> fields,
        string path)
    {
        string xml = JusticeXmlPersistenceCodec.GetFieldValue(fields, path, string.Empty);
        XmlDocument fragment = LoadJusticeXmlFragment(xml);
        if (fragment.DocumentElement == null ||
            !string.Equals(fragment.DocumentElement.Name, path, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Fragment Justice v2 invalide : " + path + ".");
        }
        target.AppendChild(targetDocument.ImportNode(fragment.DocumentElement, true));
    }

    private static bool IsJusticePersistenceFragment(string path)
    {
        return string.Equals(path, "Case", StringComparison.Ordinal) ||
               string.Equals(path, "Record", StringComparison.Ordinal) ||
               string.Equals(path, "Custody", StringComparison.Ordinal);
    }
}
