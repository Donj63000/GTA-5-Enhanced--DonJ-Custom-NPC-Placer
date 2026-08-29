using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace DonJ.NibAbiValidator
{
    public sealed class AbiValidationResult
    {
        internal AbiValidationResult(
            IEnumerable<string> errors,
            int checkedTypeReferences,
            int checkedMemberReferences,
            bool runtimeValidated)
        {
            Errors = errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(error => error, StringComparer.Ordinal)
                .ToArray();
            CheckedTypeReferences = checkedTypeReferences;
            CheckedMemberReferences = checkedMemberReferences;
            RuntimeValidated = runtimeValidated;
        }

        public bool IsValid => Errors.Count == 0;

        public IReadOnlyList<string> Errors { get; }

        public int CheckedTypeReferences { get; }

        public int CheckedMemberReferences { get; }

        public bool RuntimeValidated { get; }
    }

    public static class AbiValidator
    {
        private static readonly string[] DefaultApiAssemblyNames =
        {
            "NIBScriptHookVDotNet2",
            "ScriptHookVDotNet2"
        };

        public static AbiValidationResult Verify(
            string consumerPath,
            string contractPath,
            string runtimeApiPath = null)
        {
            AbiContract contract = AbiContract.Load(contractPath);
            string fullConsumerPath = RequireManagedFile(consumerPath, "assembly consommateur");
            string fullRuntimePath = string.IsNullOrWhiteSpace(runtimeApiPath)
                ? null
                : RequireManagedFile(runtimeApiPath, "API runtime");

            List<string> errors = new List<string>();
            AbiSignatureFormatter formatter = new AbiSignatureFormatter(contract.AcceptedAssemblyNames);

            using (ModuleDefinition consumer = ReadModule(fullConsumerPath))
            using (ModuleDefinition runtime = fullRuntimePath == null ? null : ReadModule(fullRuntimePath))
            {
                ValidateConsumerAssemblyReference(consumer, contract, errors);

                AbiRuntimeCatalog runtimeCatalog = null;
                if (runtime != null)
                {
                    runtimeCatalog = new AbiRuntimeCatalog(runtime, formatter);
                    ValidateRuntimeIdentity(runtime, contract, errors);
                    ValidateRuntimeTypeShapes(runtimeCatalog, contract, formatter, errors);
                }

                TypeReference[] apiTypeReferences = consumer.GetTypeReferences()
                    .Where(formatter.IsApiType)
                    .OrderBy(type => formatter.GetTypeSignature(type), StringComparer.Ordinal)
                    .ToArray();
                MemberReference[] apiMemberReferences = consumer.GetMemberReferences()
                    .Where(member => formatter.IsApiType(member.DeclaringType))
                    .OrderBy(member => formatter.GetMemberReferenceSignature(member), StringComparer.Ordinal)
                    .ToArray();

                ValidateTypeReferences(apiTypeReferences, contract, formatter, runtimeCatalog, errors);
                ValidateMemberReferences(apiMemberReferences, contract, formatter, runtimeCatalog, errors);

                return new AbiValidationResult(
                    errors,
                    apiTypeReferences.Length,
                    apiMemberReferences.Length,
                    runtime != null);
            }
        }

        public static AbiContract Capture(
            string consumerPath,
            string runtimeApiPath,
            string contractId,
            string contractVersion)
        {
            string fullConsumerPath = RequireManagedFile(consumerPath, "assembly consommateur");
            string fullRuntimePath = RequireManagedFile(runtimeApiPath, "API runtime");
            if (string.IsNullOrWhiteSpace(contractId))
            {
                throw new ArgumentException("L'identifiant du contrat ABI est obligatoire.", nameof(contractId));
            }

            if (string.IsNullOrWhiteSpace(contractVersion))
            {
                throw new ArgumentException("La version du contrat ABI est obligatoire.", nameof(contractVersion));
            }

            using (ModuleDefinition consumer = ReadModule(fullConsumerPath))
            using (ModuleDefinition runtime = ReadModule(fullRuntimePath))
            {
                string runtimeName = runtime.Assembly.Name.Name;
                AbiContract contract = new AbiContract
                {
                    SchemaVersion = AbiContract.CurrentSchemaVersion,
                    ContractId = contractId.Trim(),
                    ContractVersion = contractVersion.Trim(),
                    SourceAssemblyName = runtimeName,
                    SourceAssemblyVersion = runtime.Assembly.Name.Version.ToString(),
                    SourceAssemblySha256 = AbiContract.ComputeSha256(fullRuntimePath)
                };

                foreach (string name in DefaultApiAssemblyNames)
                {
                    contract.AcceptedAssemblyNames.Add(name);
                }

                contract.AcceptedAssemblyNames.Add(runtimeName);
                AbiSignatureFormatter formatter = new AbiSignatureFormatter(contract.AcceptedAssemblyNames);
                AbiRuntimeCatalog runtimeCatalog = new AbiRuntimeCatalog(runtime, formatter);
                List<string> errors = new List<string>();

                ValidateConsumerAssemblyReference(consumer, contract, errors);

                foreach (TypeReference typeReference in consumer.GetTypeReferences()
                    .Where(formatter.IsApiType)
                    .OrderBy(type => formatter.GetTypeSignature(type), StringComparer.Ordinal))
                {
                    // Je garde l'identite du TypeRef ici : le bit class/valuetype vit dans les signatures
                    // des membres, pas dans la table TypeRef elle-meme, et Cecil le decouvre paresseusement.
                    contract.TypeReferences.Add(formatter.GetTypeIdentity(typeReference));
                    AddTypeShape(typeReference, runtimeCatalog, contract, formatter, errors);
                }

                foreach (MemberReference memberReference in consumer.GetMemberReferences()
                    .Where(member => formatter.IsApiType(member.DeclaringType))
                    .OrderBy(member => formatter.GetMemberReferenceSignature(member), StringComparer.Ordinal))
                {
                    string referenceSignature = formatter.GetMemberReferenceSignature(memberReference);
                    IMemberDefinition definition = runtimeCatalog.Resolve(memberReference);
                    if (definition == null)
                    {
                        errors.Add("ABI104 Membre introuvable dans l'API live : " + referenceSignature);
                        continue;
                    }

                    string definitionSignature = formatter.GetMemberDefinitionSignature(definition);
                    if (contract.MemberReferences.TryGetValue(referenceSignature, out AbiMemberContract existing) &&
                        !string.Equals(existing.Definition, definitionSignature, StringComparison.Ordinal))
                    {
                        errors.Add("ABI105 Reference ambigue vers deux definitions : " + referenceSignature);
                        continue;
                    }

                    contract.MemberReferences[referenceSignature] = new AbiMemberContract
                    {
                        Reference = referenceSignature,
                        Definition = definitionSignature
                    };
                    AddTypeShape(memberReference.DeclaringType, runtimeCatalog, contract, formatter, errors);
                    CollectSignatureTypes(memberReference, type =>
                        AddTypeShape(type, runtimeCatalog, contract, formatter, errors));
                }

                if (errors.Count > 0)
                {
                    throw new InvalidDataException(
                        "Impossible de capturer le contrat ABI :" + Environment.NewLine +
                        string.Join(Environment.NewLine, errors.Distinct().OrderBy(error => error, StringComparer.Ordinal)));
                }

                contract.ValidateStructure();
                return contract;
            }
        }

        private static void ValidateConsumerAssemblyReference(
            ModuleDefinition consumer,
            AbiContract contract,
            ICollection<string> errors)
        {
            AssemblyNameReference[] references = consumer.AssemblyReferences
                .Where(reference => contract.AcceptedAssemblyNames.Contains(reference.Name))
                .ToArray();
            if (references.Length != 1)
            {
                errors.Add(
                    "ABI001 Le consommateur doit referencer exactement une API v2 acceptee; trouve : " +
                    references.Length.ToString(CultureInfo.InvariantCulture) + ".");
                return;
            }

            AssemblyNameReference apiReference = references[0];
            if (apiReference.Version == null || apiReference.Version.Major != 2)
            {
                errors.Add("ABI002 La reference API du consommateur doit rester en version majeure 2.");
            }

            if (!string.Equals(
                apiReference.Version == null ? string.Empty : apiReference.Version.ToString(),
                contract.SourceAssemblyVersion,
                StringComparison.Ordinal))
            {
                errors.Add(
                    "ABI003 Version API du consommateur incompatible : " +
                    (apiReference.Version == null ? "absente" : apiReference.Version.ToString()) +
                    ", attendue " + contract.SourceAssemblyVersion + ".");
            }
        }

        private static void ValidateRuntimeIdentity(
            ModuleDefinition runtime,
            AbiContract contract,
            ICollection<string> errors)
        {
            string runtimeName = runtime.Assembly.Name.Name;
            if (!contract.AcceptedAssemblyNames.Contains(runtimeName))
            {
                errors.Add("ABI010 Nom de l'API runtime non accepte : " + runtimeName + ".");
            }

            string runtimeVersion = runtime.Assembly.Name.Version == null
                ? string.Empty
                : runtime.Assembly.Name.Version.ToString();
            if (!string.Equals(runtimeVersion, contract.SourceAssemblyVersion, StringComparison.Ordinal))
            {
                errors.Add(
                    "ABI011 Version de l'API runtime incompatible : " + runtimeVersion +
                    ", attendue " + contract.SourceAssemblyVersion + ".");
            }
        }

        private static void ValidateRuntimeTypeShapes(
            AbiRuntimeCatalog runtime,
            AbiContract contract,
            AbiSignatureFormatter formatter,
            ICollection<string> errors)
        {
            foreach (AbiTypeContract expected in contract.Types.Values)
            {
                string fullName = RemoveApiScope(expected.Identity);
                TypeDefinition actualType = runtime.FindType(fullName);
                if (actualType == null)
                {
                    errors.Add("ABI020 Type contractuel absent de l'API runtime : " + expected.Identity);
                    continue;
                }

                AbiTypeContract actual = formatter.CreateTypeContract(actualType);
                if (!AreEquivalent(expected, actual))
                {
                    errors.Add(
                        "ABI021 Forme du type runtime incompatible : " + expected.Identity +
                        ". Attendu " + Describe(expected) + "; obtenu " + Describe(actual) + ".");
                }
            }
        }

        private static void ValidateTypeReferences(
            IEnumerable<TypeReference> references,
            AbiContract contract,
            AbiSignatureFormatter formatter,
            AbiRuntimeCatalog runtime,
            ICollection<string> errors)
        {
            foreach (TypeReference reference in references)
            {
                string signature = formatter.GetTypeIdentity(reference);
                if (!contract.TypeReferences.Contains(signature))
                {
                    errors.Add("ABI030 Reference de type non autorisee par le contrat : " + signature);
                }

                TypeDefinition runtimeType = runtime == null ? null : runtime.FindType(reference);
                if (runtime != null && runtimeType == null)
                {
                    errors.Add("ABI031 Reference de type absente de l'API runtime : " + signature);
                }
            }
        }

        private static void ValidateMemberReferences(
            IEnumerable<MemberReference> references,
            AbiContract contract,
            AbiSignatureFormatter formatter,
            AbiRuntimeCatalog runtime,
            ICollection<string> errors)
        {
            foreach (MemberReference reference in references)
            {
                string signature = formatter.GetMemberReferenceSignature(reference);
                if (!contract.MemberReferences.TryGetValue(signature, out AbiMemberContract expected))
                {
                    errors.Add("ABI040 Reference de membre non autorisee par le contrat : " + signature);
                    continue;
                }

                if (runtime == null)
                {
                    continue;
                }

                IMemberDefinition definition = runtime.Resolve(reference);
                if (definition == null)
                {
                    errors.Add("ABI041 Reference de membre non resolue dans l'API runtime : " + signature);
                    continue;
                }

                string actualDefinition = formatter.GetMemberDefinitionSignature(definition);
                if (!string.Equals(actualDefinition, expected.Definition, StringComparison.Ordinal))
                {
                    errors.Add(
                        "ABI042 Definition runtime incompatible pour " + signature +
                        ". Attendue " + expected.Definition + "; obtenue " + actualDefinition + ".");
                }
            }
        }

        private static void AddTypeShape(
            TypeReference reference,
            AbiRuntimeCatalog runtime,
            AbiContract contract,
            AbiSignatureFormatter formatter,
            ICollection<string> errors)
        {
            if (reference == null)
            {
                return;
            }

            foreach (TypeReference nested in EnumerateSignatureTypes(reference))
            {
                if (!formatter.IsApiType(nested))
                {
                    continue;
                }

                TypeDefinition definition = runtime.FindType(nested);
                if (definition == null)
                {
                    errors.Add("ABI100 Type introuvable dans l'API live : " + formatter.GetTypeSignature(nested));
                    continue;
                }

                AddTypeDefinitionAndAncestors(definition, runtime, contract, formatter, errors);
            }
        }

        private static void AddTypeDefinitionAndAncestors(
            TypeDefinition definition,
            AbiRuntimeCatalog runtime,
            AbiContract contract,
            AbiSignatureFormatter formatter,
            ICollection<string> errors)
        {
            string identity = formatter.GetTypeIdentity(definition);
            if (contract.Types.ContainsKey(identity))
            {
                return;
            }

            contract.Types.Add(identity, formatter.CreateTypeContract(definition));
            if (definition.BaseType != null && formatter.IsApiType(definition.BaseType))
            {
                TypeDefinition baseDefinition = runtime.FindType(definition.BaseType);
                if (baseDefinition == null)
                {
                    errors.Add("ABI101 Type de base API introuvable : " + formatter.GetTypeSignature(definition.BaseType));
                }
                else
                {
                    AddTypeDefinitionAndAncestors(baseDefinition, runtime, contract, formatter, errors);
                }
            }

            foreach (InterfaceImplementation implementation in definition.Interfaces)
            {
                if (!formatter.IsApiType(implementation.InterfaceType))
                {
                    continue;
                }

                TypeDefinition interfaceDefinition = runtime.FindType(implementation.InterfaceType);
                if (interfaceDefinition == null)
                {
                    errors.Add(
                        "ABI102 Interface API introuvable : " +
                        formatter.GetTypeSignature(implementation.InterfaceType));
                }
                else
                {
                    AddTypeDefinitionAndAncestors(interfaceDefinition, runtime, contract, formatter, errors);
                }
            }
        }

        private static void CollectSignatureTypes(MemberReference member, Action<TypeReference> collect)
        {
            if (member is MethodSpecification methodSpecification)
            {
                member = methodSpecification.ElementMethod;
            }

            if (member is MethodReference method)
            {
                collect(method.ReturnType);
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    collect(parameter.ParameterType);
                }
            }
            else if (member is FieldReference field)
            {
                collect(field.FieldType);
            }
        }

        private static IEnumerable<TypeReference> EnumerateSignatureTypes(TypeReference reference)
        {
            if (reference == null)
            {
                yield break;
            }

            yield return reference;
            if (reference is TypeSpecification specification)
            {
                foreach (TypeReference nested in EnumerateSignatureTypes(specification.ElementType))
                {
                    yield return nested;
                }
            }

            if (reference is GenericInstanceType genericInstance)
            {
                foreach (TypeReference argument in genericInstance.GenericArguments)
                {
                    foreach (TypeReference nested in EnumerateSignatureTypes(argument))
                    {
                        yield return nested;
                    }
                }
            }

            if (reference is OptionalModifierType optionalModifier)
            {
                foreach (TypeReference nested in EnumerateSignatureTypes(optionalModifier.ModifierType))
                {
                    yield return nested;
                }
            }

            if (reference is RequiredModifierType requiredModifier)
            {
                foreach (TypeReference nested in EnumerateSignatureTypes(requiredModifier.ModifierType))
                {
                    yield return nested;
                }
            }
        }

        private static bool AreEquivalent(AbiTypeContract expected, AbiTypeContract actual)
        {
            return string.Equals(expected.Identity, actual.Identity, StringComparison.Ordinal) &&
                   string.Equals(expected.Kind, actual.Kind, StringComparison.Ordinal) &&
                   string.Equals(expected.BaseType ?? string.Empty, actual.BaseType ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(expected.UnderlyingType ?? string.Empty, actual.UnderlyingType ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(expected.Visibility, actual.Visibility, StringComparison.Ordinal) &&
                   expected.GenericArity == actual.GenericArity &&
                   expected.IsAbstract == actual.IsAbstract &&
                   expected.IsSealed == actual.IsSealed &&
                   expected.Interfaces.SetEquals(actual.Interfaces);
        }

        private static string Describe(AbiTypeContract type)
        {
            return type.Kind + ", base=" + (type.BaseType ?? string.Empty) +
                   ", underlying=" + (type.UnderlyingType ?? string.Empty) +
                   ", visibility=" + (type.Visibility ?? string.Empty) +
                   ", generic=" + type.GenericArity.ToString(CultureInfo.InvariantCulture) +
                   ", abstract=" + type.IsAbstract + ", sealed=" + type.IsSealed +
                   ", interfaces=" + string.Join(",", type.Interfaces);
        }

        private static string RemoveApiScope(string identity)
        {
            const string prefix = "[api]";
            return identity != null && identity.StartsWith(prefix, StringComparison.Ordinal)
                ? identity.Substring(prefix.Length)
                : identity;
        }

        private static ModuleDefinition ReadModule(string path)
        {
            return ModuleDefinition.ReadModule(
                path,
                new ReaderParameters
                {
                    InMemory = true,
                    ReadingMode = ReadingMode.Immediate,
                    ReadSymbols = false
                });
        }

        private static string RequireManagedFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Le chemin de " + label + " est obligatoire.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Le fichier " + label + " est introuvable.", fullPath);
            }

            return fullPath;
        }
    }
}
