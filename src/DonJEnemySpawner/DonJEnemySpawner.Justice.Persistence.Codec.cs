using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

// Je garde le codec v2 entièrement pur : il ne connaît aucune native GTA et
// ne reçoit que le snapshot immuable capturé par le thread du script.
internal sealed class JusticeXmlPersistenceCodec : IJusticePersistenceCodec
{
    internal const int SchemaMajor = 2;
    internal const int SchemaMinor = 0;

    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

    public byte[] Serialize(JusticePersistenceSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException("snapshot");
        }
        if (snapshot.SchemaVersion != SchemaMajor)
        {
            throw new InvalidDataException("Version de snapshot Justice non prise en charge.");
        }

        string recoveryHash;
        string payload = BuildPayload(snapshot, out recoveryHash);
        string payloadHash = ComputePayloadHash(snapshot.Revision, payload);
        using (MemoryStream document = new MemoryStream(payload.Length + 256))
        {
            using (XmlWriter writer = XmlWriter.Create(document, CreateWriterSettings(false)))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("JusticeState");
                writer.WriteAttributeString("schemaMajor", SchemaMajor.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("schemaMinor", SchemaMinor.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("generation", snapshot.Revision.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("payloadSha256", payloadHash);
                writer.WriteAttributeString("recoverySha256", recoveryHash);
                writer.WriteRaw(payload);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return document.ToArray();
        }
    }

    public bool TryDeserialize(
        byte[] document,
        out JusticePersistenceSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        error = string.Empty;
        try
        {
            if (document == null || document.Length == 0)
            {
                throw new InvalidDataException("Document Justice v2 vide.");
            }

            XmlDocument xml = LoadDocument(document);
            XmlElement root = xml.DocumentElement;
            int schemaMajor;
            int schemaMinor;
            long generation;
            if (root == null ||
                !string.Equals(root.Name, "JusticeState", StringComparison.Ordinal) ||
                !TryReadInt(root, "schemaMajor", out schemaMajor) ||
                !TryReadInt(root, "schemaMinor", out schemaMinor) ||
                schemaMajor != SchemaMajor || schemaMinor != SchemaMinor ||
                !TryReadLong(root, "generation", out generation) || generation <= 0L)
            {
                throw new InvalidDataException("Entête Justice v2 invalide.");
            }

            string expectedPayloadHash = root.GetAttribute("payloadSha256");
            string actualPayloadHash = ComputePayloadHash(generation, root.InnerXml);
            if (!IsSha256(expectedPayloadHash) ||
                !string.Equals(
                    expectedPayloadHash,
                    actualPayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 du payload Justice v2 invalide.");
            }

            XmlNodeList profileContainers = root.SelectNodes("Profiles");
            XmlNodeList recoveryContainers = root.SelectNodes("RuntimeRecovery");
            if (profileContainers == null || profileContainers.Count != 1 ||
                recoveryContainers == null || recoveryContainers.Count != 1 ||
                CountElementChildren(root) != 2)
            {
                throw new InvalidDataException("Structure Justice v2 ambiguë.");
            }

            XmlElement recovery = recoveryContainers[0] as XmlElement;
            if (recovery == null || CountElementChildren(recovery) != 0)
            {
                throw new InvalidDataException("RuntimeRecovery Justice v2 invalide.");
            }

            List<JusticePersistenceField> globalFields = ReadAttributeFields(recovery, null);
            int activeSlot;
            if (!TryReadFieldInt(globalFields, "activePlayerSlot", out activeSlot) ||
                activeSlot < -1)
            {
                throw new InvalidDataException("Slot actif Justice v2 invalide.");
            }

            XmlElement profilesElement = profileContainers[0] as XmlElement;
            XmlNodeList profileNodes = profilesElement == null
                ? null
                : profilesElement.SelectNodes("Profile");
            if (profileNodes == null || CountElementChildren(profilesElement) != profileNodes.Count)
            {
                throw new InvalidDataException("Conteneur de profils Justice v2 invalide.");
            }

            List<JusticePersistenceProfileSnapshot> profiles =
                new List<JusticePersistenceProfileSnapshot>(profileNodes.Count);
            HashSet<int> seenSlots = new HashSet<int>();
            for (int index = 0; index < profileNodes.Count; index++)
            {
                XmlElement profile = profileNodes[index] as XmlElement;
                int slot;
                long profileGeneration;
                if (profile == null ||
                    !TryReadInt(profile, "slot", out slot) || slot < 0 ||
                    !seenSlots.Add(slot) ||
                    !TryReadLong(profile, "generation", out profileGeneration) ||
                    profileGeneration < 0L)
                {
                    throw new InvalidDataException("Métadonnées de profil Justice v2 invalides.");
                }

                string identityKey = profile.GetAttribute("identityKey");
                string expectedProfileHash = profile.GetAttribute("sha256");
                string[] reserved = { "slot", "generation", "identityKey", "sha256" };
                List<JusticePersistenceField> fields = ReadAttributeFields(profile, reserved);
                AppendRequiredFragment(profile, "Case", fields);
                AppendRequiredFragment(profile, "Record", fields);
                AppendRequiredFragment(profile, "Custody", fields);
                if (CountElementChildren(profile) != 3)
                {
                    throw new InvalidDataException("Profil Justice v2 contenant des nœuds inconnus.");
                }

                JusticePersistenceProfileSnapshot decoded =
                    new JusticePersistenceProfileSnapshot(
                        slot,
                        profileGeneration,
                        identityKey,
                        fields);
                string actualProfileHash = ComputeProfileHash(decoded);
                if (!IsSha256(expectedProfileHash) ||
                    !string.Equals(
                        expectedProfileHash,
                        actualProfileHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA-256 d'un profil Justice v2 invalide.");
                }
                profiles.Add(decoded);
            }

            string expectedRecoveryHash = root.GetAttribute("recoverySha256");
            if (!string.IsNullOrEmpty(expectedRecoveryHash))
            {
                JusticePersistenceProfileSnapshot activeProfile =
                    FindProfileBySlot(profiles, activeSlot);
                string activeProfileHash = activeProfile == null
                    ? string.Empty
                    : ComputeProfileHash(activeProfile);
                string actualRecoveryHash = ComputeRecoveryHash(
                    generation,
                    activeSlot,
                    globalFields,
                    activeProfileHash);
                if (!IsSha256(expectedRecoveryHash) ||
                    !string.Equals(
                        expectedRecoveryHash,
                        actualRecoveryHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "SHA-256 de récupération Justice v2 invalide.");
                }
            }

            snapshot = new JusticePersistenceSnapshot(
                generation,
                schemaMajor,
                DateTime.UtcNow.Ticks,
                activeSlot,
                globalFields,
                profiles);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            snapshot = null;
            return false;
        }
    }

    internal bool TryRecoverInactiveProfiles(
        byte[] primaryDocument,
        byte[] backupDocument,
        out JusticePersistenceSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        error = string.Empty;
        try
        {
            JusticePersistenceSnapshot backup;
            string backupError;
            if (!TryDeserialize(backupDocument, out backup, out backupError) ||
                backup == null)
            {
                throw new InvalidDataException(
                    "Backup Justice v2 inexploitable pour isoler un profil : " +
                    (backupError ?? "erreur inconnue"));
            }

            XmlDocument xml = LoadDocument(primaryDocument);
            XmlElement root = xml.DocumentElement;
            int schemaMajor;
            int schemaMinor;
            long generation;
            if (root == null ||
                !string.Equals(root.Name, "JusticeState", StringComparison.Ordinal) ||
                !TryReadInt(root, "schemaMajor", out schemaMajor) ||
                !TryReadInt(root, "schemaMinor", out schemaMinor) ||
                schemaMajor != SchemaMajor || schemaMinor != SchemaMinor ||
                !TryReadLong(root, "generation", out generation) || generation <= 0L ||
                !IsSha256(root.GetAttribute("payloadSha256")))
            {
                throw new InvalidDataException(
                    "Entête primaire incompatible avec une récupération par profil.");
            }

            XmlNodeList profileContainers = root.SelectNodes("Profiles");
            XmlNodeList recoveryContainers = root.SelectNodes("RuntimeRecovery");
            if (profileContainers == null || profileContainers.Count != 1 ||
                recoveryContainers == null || recoveryContainers.Count != 1 ||
                CountElementChildren(root) != 2)
            {
                throw new InvalidDataException(
                    "Structure primaire ambiguë pendant la récupération par profil.");
            }

            XmlElement recovery = recoveryContainers[0] as XmlElement;
            if (recovery == null || CountElementChildren(recovery) != 0)
            {
                throw new InvalidDataException("RuntimeRecovery primaire invalide.");
            }
            List<JusticePersistenceField> globalFields = ReadAttributeFields(recovery, null);
            int activeSlot;
            if (!TryReadFieldInt(globalFields, "activePlayerSlot", out activeSlot) ||
                activeSlot < 0)
            {
                throw new InvalidDataException("Slot actif primaire invalide.");
            }

            XmlElement profilesElement = profileContainers[0] as XmlElement;
            XmlNodeList profileNodes = profilesElement == null
                ? null
                : profilesElement.SelectNodes("Profile");
            if (profileNodes == null ||
                profileNodes.Count != backup.Profiles.Count ||
                CountElementChildren(profilesElement) != profileNodes.Count)
            {
                throw new InvalidDataException(
                    "Nombre de profils primaire incompatible avec le backup.");
            }

            List<JusticePersistenceProfileSnapshot> recovered =
                new List<JusticePersistenceProfileSnapshot>(profileNodes.Count);
            HashSet<int> seenSlots = new HashSet<int>();
            int isolatedProfileCount = 0;
            for (int index = 0; index < profileNodes.Count; index++)
            {
                XmlElement profileElement = profileNodes[index] as XmlElement;
                int slot;
                if (profileElement == null ||
                    !TryReadInt(profileElement, "slot", out slot) || slot < 0 ||
                    !seenSlots.Add(slot))
                {
                    throw new InvalidDataException(
                        "Slot de profil illisible ou dupliqué dans le primaire.");
                }

                JusticePersistenceProfileSnapshot decoded;
                string profileError;
                if (TryDecodeProfile(profileElement, out decoded, out profileError))
                {
                    recovered.Add(decoded);
                    continue;
                }

                if (slot == activeSlot)
                {
                    throw new InvalidDataException(
                        "Le profil actif est corrompu et ne peut pas être remplacé silencieusement.");
                }

                JusticePersistenceProfileSnapshot fallback =
                    FindProfileBySlot(backup.Profiles, slot);
                if (fallback == null)
                {
                    throw new InvalidDataException(
                        "Le backup ne contient pas le profil inactif " +
                        slot.ToString(CultureInfo.InvariantCulture) + ".");
                }
                recovered.Add(fallback);
                isolatedProfileCount++;
            }

            if (isolatedProfileCount == 0 ||
                FindProfileBySlot(recovered, activeSlot) == null)
            {
                throw new InvalidDataException(
                    "Aucune corruption strictement limitée à un profil inactif n'a été prouvée.");
            }

            JusticePersistenceProfileSnapshot activeProfile =
                FindProfileBySlot(recovered, activeSlot);
            string expectedRecoveryHash = root.GetAttribute("recoverySha256");
            string actualRecoveryHash = ComputeRecoveryHash(
                generation,
                activeSlot,
                globalFields,
                ComputeProfileHash(activeProfile));
            if (!IsSha256(expectedRecoveryHash) ||
                !string.Equals(
                    expectedRecoveryHash,
                    actualRecoveryHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "L'enveloppe de récupération ne prouve pas la génération, " +
                    "les champs globaux et le profil actif du primaire.");
            }

            JusticePersistenceSnapshot candidate = new JusticePersistenceSnapshot(
                generation,
                schemaMajor,
                DateTime.UtcNow.Ticks,
                activeSlot,
                globalFields,
                recovered);
            byte[] repaired = Serialize(candidate);
            JusticePersistenceSnapshot verified;
            string verificationError;
            if (!TryDeserialize(repaired, out verified, out verificationError) || verified == null)
            {
                throw new InvalidDataException(
                    "Le snapshot réparé par profil n'est pas revalidable : " +
                    (verificationError ?? "erreur inconnue"));
            }

            snapshot = verified;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            snapshot = null;
            return false;
        }
    }

    private static bool TryDecodeProfile(
        XmlElement profile,
        out JusticePersistenceProfileSnapshot decoded,
        out string error)
    {
        decoded = null;
        error = string.Empty;
        try
        {
            int slot;
            long generation;
            if (profile == null ||
                !TryReadInt(profile, "slot", out slot) || slot < 0 ||
                !TryReadLong(profile, "generation", out generation) || generation < 0L)
            {
                throw new InvalidDataException("Métadonnées de profil invalides.");
            }

            string[] reserved = { "slot", "generation", "identityKey", "sha256" };
            List<JusticePersistenceField> fields = ReadAttributeFields(profile, reserved);
            AppendRequiredFragment(profile, "Case", fields);
            AppendRequiredFragment(profile, "Record", fields);
            AppendRequiredFragment(profile, "Custody", fields);
            if (CountElementChildren(profile) != 3)
            {
                throw new InvalidDataException("Nœuds de profil inconnus.");
            }

            decoded = new JusticePersistenceProfileSnapshot(
                slot,
                generation,
                profile.GetAttribute("identityKey"),
                fields);
            string expectedHash = profile.GetAttribute("sha256");
            if (!IsSha256(expectedHash) ||
                !string.Equals(
                    expectedHash,
                    ComputeProfileHash(decoded),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 du profil invalide.");
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            decoded = null;
            return false;
        }
    }

    private static JusticePersistenceProfileSnapshot FindProfileBySlot(
        IReadOnlyList<JusticePersistenceProfileSnapshot> profiles,
        int slot)
    {
        if (profiles != null)
        {
            for (int index = 0; index < profiles.Count; index++)
            {
                JusticePersistenceProfileSnapshot profile = profiles[index];
                if (profile != null && profile.Slot == slot)
                {
                    return profile;
                }
            }
        }
        return null;
    }

    internal static string GetFieldValue(
        IReadOnlyList<JusticePersistenceField> fields,
        string path,
        string fallback)
    {
        if (fields != null)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                JusticePersistenceField field = fields[index];
                if (field != null &&
                    string.Equals(field.Path, path, StringComparison.Ordinal))
                {
                    return field.Value;
                }
            }
        }
        return fallback ?? string.Empty;
    }

    internal static string ComputeSha256Hex(byte[] value)
    {
        if (value == null)
        {
            throw new ArgumentNullException("value");
        }
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(value);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }

    private static string ComputePayloadHash(long generation, string payload)
    {
        string boundPayload = generation.ToString(CultureInfo.InvariantCulture) +
                              ":" + (payload ?? string.Empty);
        return ComputeSha256Hex(Utf8WithoutBom.GetBytes(boundPayload));
    }

    private static string BuildPayload(
        JusticePersistenceSnapshot snapshot,
        out string recoveryHash)
    {
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>(snapshot.Profiles.Count);
        for (int index = 0; index < snapshot.Profiles.Count; index++)
        {
            profiles.Add(MaterializeTypedProfile(snapshot.Profiles[index]));
        }
        profiles.Sort(delegate(
            JusticePersistenceProfileSnapshot left,
            JusticePersistenceProfileSnapshot right)
        {
            return left.Slot.CompareTo(right.Slot);
        });

        ValidateFields(snapshot.GlobalFields, false);
        JusticePersistenceProfileSnapshot activeProfile =
            FindProfileBySlot(profiles, snapshot.ActiveProfileSlot);
        if (snapshot.ActiveProfileSlot >= 0 && activeProfile == null)
        {
            throw new InvalidDataException("Profil actif absent du snapshot Justice v2.");
        }
        recoveryHash = ComputeRecoveryHash(
            snapshot.Revision,
            snapshot.ActiveProfileSlot,
            snapshot.GlobalFields,
            activeProfile == null ? string.Empty : ComputeProfileHash(activeProfile));

        StringBuilder payload = new StringBuilder(16384);
        using (XmlWriter writer = XmlWriter.Create(payload, CreateWriterSettings(true)))
        {
            writer.WriteStartElement("Profiles");
            for (int index = 0; index < profiles.Count; index++)
            {
                WriteProfile(writer, profiles[index]);
            }
            writer.WriteEndElement();

            writer.WriteStartElement("RuntimeRecovery");
            WriteAttributeFields(writer, snapshot.GlobalFields, false);
            writer.WriteEndElement();
        }
        return payload.ToString();
    }

    private static void WriteProfile(
        XmlWriter writer,
        JusticePersistenceProfileSnapshot profile)
    {
        ValidateFields(profile.Fields, true);
        writer.WriteStartElement("Profile");
        writer.WriteAttributeString("slot", profile.Slot.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "generation",
            profile.Generation.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("identityKey", profile.IdentityKey ?? string.Empty);
        writer.WriteAttributeString("sha256", ComputeProfileHash(profile));
        WriteAttributeFields(writer, profile.Fields, true);
        WriteFragment(writer, GetRequiredField(profile.Fields, "Case"), "Case");
        WriteFragment(writer, GetRequiredField(profile.Fields, "Record"), "Record");
        WriteFragment(writer, GetRequiredField(profile.Fields, "Custody"), "Custody");
        writer.WriteEndElement();
    }

    private static JusticePersistenceProfileSnapshot MaterializeTypedProfile(
        JusticePersistenceProfileSnapshot profile)
    {
        if (profile == null)
        {
            throw new InvalidDataException("Profil Justice absent du snapshot.");
        }
        if (!profile.HasTypedFragments)
        {
            return profile;
        }
        if (profile.CaseState == null || profile.RecordState == null)
        {
            throw new InvalidDataException(
                "Le snapshot typé doit contenir ensemble le dossier et le casier.");
        }

        List<JusticePersistenceField> fields =
            new List<JusticePersistenceField>(profile.Fields.Count + 3);
        for (int index = 0; index < profile.Fields.Count; index++)
        {
            if (IsFragmentPath(profile.Fields[index].Path))
            {
                if (string.Equals(profile.Fields[index].Path, "Custody", StringComparison.Ordinal) &&
                    profile.CustodyState == null)
                {
                    fields.Add(profile.Fields[index]);
                }
                continue;
            }
            fields.Add(profile.Fields[index]);
        }

        // Cette méthode n'est appelée que par JusticeRepository sur son worker.
        // La sérialisation, les XmlDocument et les hash restent ainsi hors GTA.
        fields.Add(new JusticePersistenceField(
            "Case",
            SerializeTypedFragment(delegate(XmlWriter fragmentWriter)
            {
                DonJEnemySpawner.WriteJusticeCaseXml(fragmentWriter, profile.CaseState);
            })));
        fields.Add(new JusticePersistenceField(
            "Record",
            SerializeTypedFragment(delegate(XmlWriter fragmentWriter)
            {
                DonJEnemySpawner.WriteJusticeRecordXml(fragmentWriter, profile.RecordState);
            })));
        if (profile.CustodyState != null)
        {
            fields.Add(new JusticePersistenceField(
                "Custody",
                SerializeTypedFragment(delegate(XmlWriter fragmentWriter)
                {
                    DonJEnemySpawner.WriteJusticeCustodyPersistenceXml(
                        fragmentWriter,
                        profile.CustodyState);
                })));
        }

        return new JusticePersistenceProfileSnapshot(
            profile.Slot,
            profile.Generation,
            profile.IdentityKey,
            fields);
    }

    private static string SerializeTypedFragment(Action<XmlWriter> write)
    {
        StringBuilder fragment = new StringBuilder(4096);
        using (XmlWriter writer = XmlWriter.Create(fragment, CreateWriterSettings(true)))
        {
            write(writer);
        }
        return fragment.ToString();
    }

    private static void WriteAttributeFields(
        XmlWriter writer,
        IReadOnlyList<JusticePersistenceField> fields,
        bool skipFragments)
    {
        ValidateFields(fields, skipFragments);
        List<JusticePersistenceField> ordered = new List<JusticePersistenceField>();
        for (int index = 0; index < fields.Count; index++)
        {
            JusticePersistenceField field = fields[index];
            if (!skipFragments || !IsFragmentPath(field.Path))
            {
                ordered.Add(field);
            }
        }
        ordered.Sort(delegate(JusticePersistenceField left, JusticePersistenceField right)
        {
            return string.CompareOrdinal(left.Path, right.Path);
        });
        for (int index = 0; index < ordered.Count; index++)
        {
            writer.WriteAttributeString(ordered[index].Path, ordered[index].Value);
        }
    }

    private static void ValidateFields(
        IReadOnlyList<JusticePersistenceField> fields,
        bool allowFragments)
    {
        if (fields == null)
        {
            throw new InvalidDataException("Collection de champs Justice absente.");
        }
        HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < fields.Count; index++)
        {
            JusticePersistenceField field = fields[index];
            if (field == null || !paths.Add(field.Path) ||
                (!allowFragments && IsFragmentPath(field.Path)) ||
                (!IsFragmentPath(field.Path) && !IsValidXmlName(field.Path)))
            {
                throw new InvalidDataException("Champ Justice dupliqué ou invalide.");
            }
        }
    }

    private static string ComputeProfileHash(JusticePersistenceProfileSnapshot profile)
    {
        List<JusticePersistenceField> ordered =
            new List<JusticePersistenceField>(profile.Fields);
        ordered.Sort(delegate(JusticePersistenceField left, JusticePersistenceField right)
        {
            return string.CompareOrdinal(left.Path, right.Path);
        });
        StringBuilder canonical = new StringBuilder();
        AppendLengthPrefixed(canonical, profile.Slot.ToString(CultureInfo.InvariantCulture));
        AppendLengthPrefixed(canonical, profile.Generation.ToString(CultureInfo.InvariantCulture));
        AppendLengthPrefixed(canonical, profile.IdentityKey ?? string.Empty);
        for (int index = 0; index < ordered.Count; index++)
        {
            AppendLengthPrefixed(canonical, ordered[index].Path);
            AppendLengthPrefixed(canonical, ordered[index].Value);
        }
        return ComputeSha256Hex(Utf8WithoutBom.GetBytes(canonical.ToString()));
    }

    private static string ComputeRecoveryHash(
        long generation,
        int activeSlot,
        IReadOnlyList<JusticePersistenceField> globalFields,
        string activeProfileHash)
    {
        List<JusticePersistenceField> ordered =
            new List<JusticePersistenceField>(globalFields);
        ordered.Sort(delegate(JusticePersistenceField left, JusticePersistenceField right)
        {
            return string.CompareOrdinal(left.Path, right.Path);
        });

        StringBuilder canonical = new StringBuilder();
        AppendLengthPrefixed(
            canonical,
            generation.ToString(CultureInfo.InvariantCulture));
        AppendLengthPrefixed(
            canonical,
            activeSlot.ToString(CultureInfo.InvariantCulture));
        AppendLengthPrefixed(canonical, activeProfileHash ?? string.Empty);
        for (int index = 0; index < ordered.Count; index++)
        {
            AppendLengthPrefixed(canonical, ordered[index].Path);
            AppendLengthPrefixed(canonical, ordered[index].Value);
        }
        return ComputeSha256Hex(Utf8WithoutBom.GetBytes(canonical.ToString()));
    }

    private static void AppendLengthPrefixed(StringBuilder builder, string value)
    {
        string safe = value ?? string.Empty;
        builder.Append(safe.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(safe);
        builder.Append(';');
    }

    private static void WriteFragment(XmlWriter writer, string fragment, string expectedName)
    {
        XmlDocument document = LoadDocument(Utf8WithoutBom.GetBytes(fragment));
        if (document.DocumentElement == null ||
            !string.Equals(document.DocumentElement.Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fragment Justice v2 invalide : " + expectedName + ".");
        }
        document.DocumentElement.WriteTo(writer);
    }

    private static void AppendRequiredFragment(
        XmlElement profile,
        string name,
        List<JusticePersistenceField> fields)
    {
        XmlNodeList nodes = profile.SelectNodes(name);
        XmlElement element = nodes != null && nodes.Count == 1
            ? nodes[0] as XmlElement
            : null;
        if (element == null)
        {
            throw new InvalidDataException("Fragment Justice v2 manquant : " + name + ".");
        }
        fields.Add(new JusticePersistenceField(name, element.OuterXml));
    }

    private static List<JusticePersistenceField> ReadAttributeFields(
        XmlElement element,
        string[] reserved)
    {
        List<JusticePersistenceField> fields = new List<JusticePersistenceField>();
        for (int index = 0; index < element.Attributes.Count; index++)
        {
            XmlAttribute attribute = element.Attributes[index];
            if (!ContainsOrdinal(reserved, attribute.Name))
            {
                fields.Add(new JusticePersistenceField(attribute.Name, attribute.Value));
            }
        }
        return fields;
    }

    private static bool ContainsOrdinal(string[] values, string candidate)
    {
        if (values == null)
        {
            return false;
        }
        for (int index = 0; index < values.Length; index++)
        {
            if (string.Equals(values[index], candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetRequiredField(
        IReadOnlyList<JusticePersistenceField> fields,
        string path)
    {
        string value = GetFieldValue(fields, path, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Champ Justice v2 obligatoire absent : " + path + ".");
        }
        return value;
    }

    private static bool TryReadFieldInt(
        IReadOnlyList<JusticePersistenceField> fields,
        string path,
        out int value)
    {
        return int.TryParse(
            GetFieldValue(fields, path, string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadInt(XmlElement element, string name, out int value)
    {
        return int.TryParse(
            element.GetAttribute(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadLong(XmlElement element, string name, out long value)
    {
        return long.TryParse(
            element.GetAttribute(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static int CountElementChildren(XmlElement element)
    {
        int count = 0;
        if (element != null)
        {
            for (XmlNode node = element.FirstChild; node != null; node = node.NextSibling)
            {
                if (node.NodeType == XmlNodeType.Element)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static XmlDocument LoadDocument(byte[] document)
    {
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
        XmlDocument xml = new XmlDocument { XmlResolver = null };
        using (MemoryStream source = new MemoryStream(document, false))
        using (XmlReader reader = XmlReader.Create(source, settings))
        {
            xml.Load(reader);
        }
        return xml;
    }

    private static XmlWriterSettings CreateWriterSettings(bool fragment)
    {
        return new XmlWriterSettings
        {
            OmitXmlDeclaration = fragment,
            ConformanceLevel = fragment ? ConformanceLevel.Fragment : ConformanceLevel.Document,
            Encoding = Utf8WithoutBom,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        };
    }

    private static bool IsFragmentPath(string path)
    {
        return string.Equals(path, "Case", StringComparison.Ordinal) ||
               string.Equals(path, "Record", StringComparison.Ordinal) ||
               string.Equals(path, "Custody", StringComparison.Ordinal);
    }

    private static bool IsValidXmlName(string value)
    {
        try
        {
            return string.Equals(XmlConvert.VerifyName(value), value, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSha256(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64)
        {
            return false;
        }
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}
