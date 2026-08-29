using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DonJ.NibAbiValidator
{
    public sealed class AbiContract
    {
        public const int CurrentSchemaVersion = 2;
        public const long MaximumContractBytes = 16L * 1024L * 1024L;

        public AbiContract()
        {
            AcceptedAssemblyNames = new SortedSet<string>(StringComparer.Ordinal);
            TypeReferences = new SortedSet<string>(StringComparer.Ordinal);
            Types = new SortedDictionary<string, AbiTypeContract>(StringComparer.Ordinal);
            MemberReferences = new SortedDictionary<string, AbiMemberContract>(StringComparer.Ordinal);
        }

        public int SchemaVersion { get; set; }

        public string ContractId { get; set; }

        public string ContractVersion { get; set; }

        public string SourceAssemblyName { get; set; }

        public string SourceAssemblyVersion { get; set; }

        public string SourceAssemblySha256 { get; set; }

        public SortedSet<string> AcceptedAssemblyNames { get; }

        public SortedSet<string> TypeReferences { get; }

        public SortedDictionary<string, AbiTypeContract> Types { get; }

        public SortedDictionary<string, AbiMemberContract> MemberReferences { get; }

        public static AbiContract Load(string path)
        {
            string fullPath = RequireReadableFile(path, "contrat ABI");
            FileInfo file = new FileInfo(fullPath);
            if (file.Length == 0 || file.Length > MaximumContractBytes)
            {
                throw new InvalidDataException(
                    "Le contrat ABI doit contenir entre 1 octet et " +
                    MaximumContractBytes.ToString(CultureInfo.InvariantCulture) + " octets.");
            }

            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumContractBytes,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            XDocument document;
            using (XmlReader reader = XmlReader.Create(fullPath, settings))
            {
                document = XDocument.Load(reader, LoadOptions.None);
            }

            XElement root = document.Root;
            if (root == null || root.Name != "NibAbiContract")
            {
                throw new InvalidDataException("La racine du contrat ABI doit etre NibAbiContract.");
            }

            AbiContract contract = new AbiContract
            {
                SchemaVersion = ReadRequiredInt(root, "schemaVersion"),
                ContractId = ReadRequired(root, "id"),
                ContractVersion = ReadRequired(root, "version"),
                SourceAssemblyName = ReadRequired(root, "sourceAssemblyName"),
                SourceAssemblyVersion = ReadRequired(root, "sourceAssemblyVersion"),
                SourceAssemblySha256 = ReadRequired(root, "sourceAssemblySha256")
            };

            if (contract.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Version de schema ABI non prise en charge : " +
                    contract.SchemaVersion.ToString(CultureInfo.InvariantCulture) + ".");
            }

            XElement acceptedNames = RequireSingleChild(root, "AcceptedAssemblyNames");
            foreach (XElement nameElement in acceptedNames.Elements("Assembly"))
            {
                AddUnique(
                    contract.AcceptedAssemblyNames,
                    ReadRequired(nameElement, "name"),
                    "nom d'assembly accepte");
            }

            XElement typeReferences = RequireSingleChild(root, "TypeReferences");
            foreach (XElement referenceElement in typeReferences.Elements("TypeReference"))
            {
                AddUnique(
                    contract.TypeReferences,
                    ReadRequired(referenceElement, "signature"),
                    "reference de type");
            }

            XElement types = RequireSingleChild(root, "Types");
            foreach (XElement typeElement in types.Elements("Type"))
            {
                AbiTypeContract type = new AbiTypeContract
                {
                    Identity = ReadRequired(typeElement, "identity"),
                    Kind = ReadRequired(typeElement, "kind"),
                    BaseType = ReadOptional(typeElement, "base"),
                    UnderlyingType = ReadOptional(typeElement, "underlying"),
                    Visibility = ReadRequired(typeElement, "visibility"),
                    GenericArity = ReadRequiredInt(typeElement, "genericArity"),
                    IsAbstract = ReadRequiredBoolean(typeElement, "abstract"),
                    IsSealed = ReadRequiredBoolean(typeElement, "sealed")
                };

                foreach (XElement interfaceElement in typeElement.Elements("Interface"))
                {
                    AddUnique(
                        type.Interfaces,
                        ReadRequired(interfaceElement, "signature"),
                        "interface de " + type.Identity);
                }

                if (contract.Types.ContainsKey(type.Identity))
                {
                    throw new InvalidDataException("Type ABI duplique : " + type.Identity + ".");
                }

                contract.Types.Add(type.Identity, type);
            }

            XElement members = RequireSingleChild(root, "MemberReferences");
            foreach (XElement memberElement in members.Elements("MemberReference"))
            {
                AbiMemberContract member = new AbiMemberContract
                {
                    Reference = ReadRequired(memberElement, "signature"),
                    Definition = ReadRequired(memberElement, "definition")
                };

                if (contract.MemberReferences.ContainsKey(member.Reference))
                {
                    throw new InvalidDataException("Reference de membre ABI dupliquee : " + member.Reference + ".");
                }

                contract.MemberReferences.Add(member.Reference, member);
            }

            contract.ValidateStructure();
            return contract;
        }

        public void Save(string path)
        {
            ValidateStructure();
            string fullPath = Path.GetFullPath(path ?? string.Empty);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Le dossier du contrat ABI est introuvable.");
            }

            Directory.CreateDirectory(directory);

            XElement root = new XElement(
                "NibAbiContract",
                new XAttribute("schemaVersion", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("id", ContractId),
                new XAttribute("version", ContractVersion),
                new XAttribute("sourceAssemblyName", SourceAssemblyName),
                new XAttribute("sourceAssemblyVersion", SourceAssemblyVersion),
                new XAttribute("sourceAssemblySha256", SourceAssemblySha256),
                new XElement(
                    "AcceptedAssemblyNames",
                    AcceptedAssemblyNames.Select(name => new XElement("Assembly", new XAttribute("name", name)))),
                new XElement(
                    "TypeReferences",
                    TypeReferences.Select(signature =>
                        new XElement("TypeReference", new XAttribute("signature", signature)))),
                new XElement(
                    "Types",
                    Types.Values.Select(type =>
                        new XElement(
                            "Type",
                            new XAttribute("identity", type.Identity),
                            new XAttribute("kind", type.Kind),
                            new XAttribute("base", type.BaseType ?? string.Empty),
                            new XAttribute("underlying", type.UnderlyingType ?? string.Empty),
                            new XAttribute("visibility", type.Visibility),
                            new XAttribute("genericArity", type.GenericArity.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("abstract", XmlConvert.ToString(type.IsAbstract)),
                            new XAttribute("sealed", XmlConvert.ToString(type.IsSealed)),
                            type.Interfaces.Select(signature =>
                                new XElement("Interface", new XAttribute("signature", signature)))))),
                new XElement(
                    "MemberReferences",
                    MemberReferences.Values.Select(member =>
                        new XElement(
                            "MemberReference",
                            new XAttribute("signature", member.Reference),
                            new XAttribute("definition", member.Definition)))));

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false
            };

            string temporaryPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (XmlWriter writer = XmlWriter.Create(temporaryPath, settings))
                {
                    new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public void ValidateStructure()
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Le schema du contrat ABI doit etre " +
                    CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + ".");
            }

            RequireValue(ContractId, "identifiant du contrat");
            RequireValue(ContractVersion, "version du contrat");
            RequireValue(SourceAssemblyName, "assembly source");
            RequireValue(SourceAssemblyVersion, "version de l'assembly source");
            if (!Version.TryParse(SourceAssemblyVersion, out _))
            {
                throw new InvalidDataException("La version de l'assembly source est invalide.");
            }

            if (string.IsNullOrWhiteSpace(SourceAssemblySha256) ||
                SourceAssemblySha256.Length != 64 ||
                SourceAssemblySha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("Le SHA-256 de l'assembly source est invalide.");
            }

            if (AcceptedAssemblyNames.Count == 0 ||
                !AcceptedAssemblyNames.Contains(SourceAssemblyName))
            {
                throw new InvalidDataException("L'assembly source doit etre present dans les noms acceptes.");
            }

            if (TypeReferences.Count == 0 || Types.Count == 0 || MemberReferences.Count == 0)
            {
                throw new InvalidDataException("Le contrat ABI ne peut pas etre vide.");
            }

            foreach (AbiTypeContract type in Types.Values)
            {
                RequireValue(type.Identity, "identite de type");
                RequireValue(type.Kind, "nature de type");
                if (type.Kind != "class" && type.Kind != "interface" &&
                    type.Kind != "valuetype" && type.Kind != "enum")
                {
                    throw new InvalidDataException("Nature de type ABI invalide : " + type.Kind + ".");
                }

                RequireValue(type.Visibility, "visibilite du type " + type.Identity);
                if (!IsKnownTypeVisibility(type.Visibility))
                {
                    throw new InvalidDataException(
                        "Visibilite de type ABI invalide pour " + type.Identity + " : " +
                        type.Visibility + ".");
                }

                if (type.Kind == "enum")
                {
                    RequireValue(type.UnderlyingType, "type sous-jacent de l'enum " + type.Identity);
                }
                else if (!string.IsNullOrEmpty(type.UnderlyingType))
                {
                    throw new InvalidDataException(
                        "Seul un enum peut declarer un type sous-jacent ABI : " + type.Identity + ".");
                }
            }

            foreach (AbiMemberContract member in MemberReferences.Values)
            {
                RequireValue(member.Reference, "reference de membre");
                RequireValue(member.Definition, "definition de membre");
            }
        }

        public static string ComputeSha256(string path)
        {
            string fullPath = RequireReadableFile(path, "fichier a hasher");
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(fullPath))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string RequireReadableFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Le chemin du " + label + " est obligatoire.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Le " + label + " est introuvable.", fullPath);
            }

            return fullPath;
        }

        private static void AddUnique(ISet<string> values, string value, string label)
        {
            if (!values.Add(value))
            {
                throw new InvalidDataException("Valeur dupliquee pour " + label + " : " + value + ".");
            }
        }

        private static XElement RequireSingleChild(XElement parent, string name)
        {
            XElement[] children = parent.Elements(name).ToArray();
            if (children.Length != 1)
            {
                throw new InvalidDataException("Le contrat ABI exige exactement un bloc " + name + ".");
            }

            return children[0];
        }

        private static string ReadRequired(XElement element, string attributeName)
        {
            string value = ReadOptional(element, attributeName);
            RequireValue(value, "attribut " + attributeName);
            return value;
        }

        private static string ReadOptional(XElement element, string attributeName)
        {
            XAttribute attribute = element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
        }

        private static int ReadRequiredInt(XElement element, string attributeName)
        {
            string value = ReadRequired(element, attributeName);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new InvalidDataException("Entier ABI invalide pour " + attributeName + ".");
            }

            return parsed;
        }

        private static bool ReadRequiredBoolean(XElement element, string attributeName)
        {
            try
            {
                return XmlConvert.ToBoolean(ReadRequired(element, attributeName));
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Booleen ABI invalide pour " + attributeName + ".", exception);
            }
        }

        private static void RequireValue(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("Le " + label + " est obligatoire.");
            }
        }

        private static bool IsKnownTypeVisibility(string visibility)
        {
            switch (visibility)
            {
                case "public":
                case "notpublic":
                case "nestedpublic":
                case "nestedprivate":
                case "nestedfamily":
                case "nestedassembly":
                case "nestedfamandassem":
                case "nestedfamorassem":
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class AbiTypeContract
    {
        public AbiTypeContract()
        {
            Interfaces = new SortedSet<string>(StringComparer.Ordinal);
        }

        public string Identity { get; set; }

        public string Kind { get; set; }

        public string BaseType { get; set; }

        public string UnderlyingType { get; set; }

        public string Visibility { get; set; }

        public int GenericArity { get; set; }

        public bool IsAbstract { get; set; }

        public bool IsSealed { get; set; }

        public SortedSet<string> Interfaces { get; }
    }

    public sealed class AbiMemberContract
    {
        public string Reference { get; set; }

        public string Definition { get; set; }
    }
}
