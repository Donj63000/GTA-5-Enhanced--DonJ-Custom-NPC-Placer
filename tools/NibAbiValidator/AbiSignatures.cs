using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;

namespace DonJ.NibAbiValidator
{
    internal sealed class AbiSignatureFormatter
    {
        private readonly ISet<string> _apiAssemblyNames;

        public AbiSignatureFormatter(IEnumerable<string> apiAssemblyNames)
        {
            if (apiAssemblyNames == null)
            {
                throw new ArgumentNullException(nameof(apiAssemblyNames));
            }

            _apiAssemblyNames = new HashSet<string>(apiAssemblyNames, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsApiType(TypeReference type)
        {
            TypeReference element = GetElementType(type);
            if (element is GenericParameter)
            {
                return false;
            }

            string scopeName = GetScopeName(element);
            return !string.IsNullOrWhiteSpace(scopeName) && _apiAssemblyNames.Contains(scopeName);
        }

        public string GetTypeIdentity(TypeReference type)
        {
            TypeReference element = GetElementType(type);
            return GetScopePrefix(element) + element.FullName;
        }

        public string GetTypeSignature(TypeReference type)
        {
            if (type == null)
            {
                return "<null>";
            }

            if (type is GenericParameter genericParameter)
            {
                string marker = genericParameter.Type == GenericParameterType.Method ? "!!" : "!";
                return marker + genericParameter.Position.ToString(CultureInfo.InvariantCulture);
            }

            if (type is ByReferenceType byReference)
            {
                return GetTypeSignature(byReference.ElementType) + "&";
            }

            if (type is PointerType pointer)
            {
                return GetTypeSignature(pointer.ElementType) + "*";
            }

            if (type is PinnedType pinned)
            {
                return "pinned(" + GetTypeSignature(pinned.ElementType) + ")";
            }

            if (type is SentinelType sentinel)
            {
                return "sentinel(" + GetTypeSignature(sentinel.ElementType) + ")";
            }

            if (type is OptionalModifierType optionalModifier)
            {
                return "modopt(" + GetTypeSignature(optionalModifier.ModifierType) + ")" +
                       GetTypeSignature(optionalModifier.ElementType);
            }

            if (type is RequiredModifierType requiredModifier)
            {
                return "modreq(" + GetTypeSignature(requiredModifier.ModifierType) + ")" +
                       GetTypeSignature(requiredModifier.ElementType);
            }

            if (type is ArrayType array)
            {
                if (array.IsVector)
                {
                    return GetTypeSignature(array.ElementType) + "[]";
                }

                return GetTypeSignature(array.ElementType) + "[" +
                       string.Join(",", array.Dimensions.Select(FormatDimension)) + "]";
            }

            if (type is GenericInstanceType genericInstance)
            {
                return GetReferenceKind(genericInstance.ElementType) +
                       GetScopePrefix(genericInstance.ElementType) +
                       genericInstance.ElementType.FullName + "<" +
                       string.Join(",", genericInstance.GenericArguments.Select(GetTypeSignature)) + ">";
            }

            if (type is FunctionPointerType functionPointer)
            {
                return "fnptr(" + GetMethodCoreSignature(functionPointer) + ")";
            }

            return GetReferenceKind(type) + GetScopePrefix(type) + type.FullName;
        }

        public string GetMemberReferenceSignature(MemberReference member)
        {
            if (member is MethodSpecification methodSpecification)
            {
                member = methodSpecification.ElementMethod;
            }

            if (member is MethodReference method)
            {
                return "method|" + GetTypeSignature(method.DeclaringType) + "|" +
                       Escape(method.Name) + "|" + GetMethodCoreSignature(method);
            }

            if (member is FieldReference field)
            {
                return "field|" + GetTypeSignature(field.DeclaringType) + "|" +
                       Escape(field.Name) + "|" + GetTypeSignature(field.FieldType);
            }

            throw new NotSupportedException("Type de membre Cecil non pris en charge : " + member.GetType().FullName + ".");
        }

        public string GetMemberDefinitionSignature(IMemberDefinition member)
        {
            if (member is MethodDefinition method)
            {
                return "methoddef|" + GetTypeSignature(method.DeclaringType) + "|" +
                       Escape(method.Name) + "|" + GetMethodCoreSignature(method) +
                       "|visibility=" + GetMethodVisibility(method) +
                       "|abstract=" + FormatBoolean(method.IsAbstract) +
                       "|virtual=" + FormatBoolean(method.IsVirtual) +
                       "|final=" + FormatBoolean(method.IsFinal) +
                       "|newslot=" + FormatBoolean(method.IsNewSlot);
            }

            if (member is FieldDefinition field)
            {
                return "fielddef|" + GetTypeSignature(field.DeclaringType) + "|" +
                       Escape(field.Name) + "|" + GetTypeSignature(field.FieldType) +
                       "|visibility=" + GetFieldVisibility(field) +
                       "|static=" + FormatBoolean(field.IsStatic) +
                       "|literal=" + FormatBoolean(field.IsLiteral) +
                       "|initonly=" + FormatBoolean(field.IsInitOnly);
            }

            throw new NotSupportedException("Definition Cecil non prise en charge : " + member.GetType().FullName + ".");
        }

        public string GetMethodBindingSignature(MethodReference method)
        {
            return Escape(method.Name) + "|" + GetMethodCoreSignature(method);
        }

        public AbiTypeContract CreateTypeContract(TypeDefinition type)
        {
            AbiTypeContract contract = new AbiTypeContract
            {
                Identity = GetTypeIdentity(type),
                Kind = GetTypeKind(type),
                BaseType = type.BaseType == null ? string.Empty : GetTypeSignature(type.BaseType),
                UnderlyingType = GetEnumUnderlyingType(type),
                Visibility = GetTypeVisibility(type),
                GenericArity = type.GenericParameters.Count,
                IsAbstract = type.IsAbstract,
                IsSealed = type.IsSealed
            };

            foreach (string interfaceSignature in type.Interfaces
                .Select(implementation => GetTypeSignature(implementation.InterfaceType))
                .OrderBy(signature => signature, StringComparer.Ordinal))
            {
                contract.Interfaces.Add(interfaceSignature);
            }

            return contract;
        }

        public static string GetTypeKind(TypeDefinition type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsInterface)
            {
                return "interface";
            }

            return type.IsValueType ? "valuetype" : "class";
        }

        public static TypeReference GetElementType(TypeReference type)
        {
            TypeReference current = type;
            while (current is TypeSpecification specification)
            {
                current = specification.ElementType;
            }

            return current;
        }

        private string GetMethodCoreSignature(IMethodSignature method)
        {
            MethodReference methodReference = method as MethodReference;
            int genericArity = methodReference == null ? 0 : methodReference.GenericParameters.Count;
            return "this=" + FormatBoolean(method.HasThis) +
                   "|explicit=" + FormatBoolean(method.ExplicitThis) +
                   "|call=" + ((int)method.CallingConvention).ToString(CultureInfo.InvariantCulture) +
                   "|generic=" + genericArity.ToString(CultureInfo.InvariantCulture) +
                   "|return=" + GetTypeSignature(method.ReturnType) +
                   "|params=" + string.Join(",", method.Parameters.Select(parameter => GetTypeSignature(parameter.ParameterType)));
        }

        private string GetScopePrefix(TypeReference type)
        {
            string scopeName = GetScopeName(type);
            if (!string.IsNullOrWhiteSpace(scopeName) && _apiAssemblyNames.Contains(scopeName))
            {
                return "[api]";
            }

            return "[" + (scopeName ?? "module") + "]";
        }

        private static string GetScopeName(TypeReference type)
        {
            TypeReference root = type;
            while (root.DeclaringType != null)
            {
                root = root.DeclaringType;
            }

            if (root.Scope is AssemblyNameReference assemblyReference)
            {
                return assemblyReference.Name;
            }

            if (root.Scope is ModuleDefinition moduleDefinition)
            {
                return moduleDefinition.Assembly == null ? moduleDefinition.Name : moduleDefinition.Assembly.Name.Name;
            }

            if (root.Scope is ModuleReference moduleReference)
            {
                return "module:" + moduleReference.Name;
            }

            return root.Scope == null ? null : root.Scope.Name;
        }

        private static string GetReferenceKind(TypeReference type)
        {
            return type.IsValueType ? "valuetype " : "class ";
        }

        private static string FormatDimension(ArrayDimension dimension)
        {
            string lower = dimension.LowerBound.HasValue
                ? dimension.LowerBound.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            string upper = dimension.UpperBound.HasValue
                ? dimension.UpperBound.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return lower + "..." + upper;
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private string GetEnumUnderlyingType(TypeDefinition type)
        {
            if (!type.IsEnum)
            {
                return string.Empty;
            }

            FieldDefinition valueField = type.Fields.FirstOrDefault(field =>
                string.Equals(field.Name, "value__", StringComparison.Ordinal));
            if (valueField == null)
            {
                throw new InvalidOperationException(
                    "L'enum " + type.FullName + " ne declare aucun champ value__.");
            }

            return GetTypeSignature(valueField.FieldType);
        }

        private static string GetTypeVisibility(TypeDefinition type)
        {
            switch (type.Attributes & TypeAttributes.VisibilityMask)
            {
                case TypeAttributes.Public:
                    return "public";
                case TypeAttributes.NotPublic:
                    return "notpublic";
                case TypeAttributes.NestedPublic:
                    return "nestedpublic";
                case TypeAttributes.NestedPrivate:
                    return "nestedprivate";
                case TypeAttributes.NestedFamily:
                    return "nestedfamily";
                case TypeAttributes.NestedAssembly:
                    return "nestedassembly";
                case TypeAttributes.NestedFamANDAssem:
                    return "nestedfamandassem";
                case TypeAttributes.NestedFamORAssem:
                    return "nestedfamorassem";
                default:
                    throw new InvalidOperationException(
                        "Visibilite CLR inconnue pour le type " + type.FullName + ".");
            }
        }

        private static string GetMethodVisibility(MethodDefinition method)
        {
            switch (method.Attributes & MethodAttributes.MemberAccessMask)
            {
                case MethodAttributes.CompilerControlled:
                    return "privatescope";
                case MethodAttributes.Private:
                    return "private";
                case MethodAttributes.FamANDAssem:
                    return "famandassem";
                case MethodAttributes.Assembly:
                    return "assembly";
                case MethodAttributes.Family:
                    return "family";
                case MethodAttributes.FamORAssem:
                    return "famorassem";
                case MethodAttributes.Public:
                    return "public";
                default:
                    throw new InvalidOperationException(
                        "Visibilite CLR inconnue pour la methode " + method.FullName + ".");
            }
        }

        private static string GetFieldVisibility(FieldDefinition field)
        {
            switch (field.Attributes & FieldAttributes.FieldAccessMask)
            {
                case FieldAttributes.CompilerControlled:
                    return "privatescope";
                case FieldAttributes.Private:
                    return "private";
                case FieldAttributes.FamANDAssem:
                    return "famandassem";
                case FieldAttributes.Assembly:
                    return "assembly";
                case FieldAttributes.Family:
                    return "family";
                case FieldAttributes.FamORAssem:
                    return "famorassem";
                case FieldAttributes.Public:
                    return "public";
                default:
                    throw new InvalidOperationException(
                        "Visibilite CLR inconnue pour le champ " + field.FullName + ".");
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("%", "%25")
                .Replace("|", "%7C")
                .Replace(",", "%2C");
        }
    }
}
