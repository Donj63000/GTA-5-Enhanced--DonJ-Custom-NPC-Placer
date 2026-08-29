using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace DonJ.NibAbiValidator
{
    internal sealed class AbiRuntimeCatalog
    {
        private readonly ModuleDefinition _module;
        private readonly AbiSignatureFormatter _formatter;
        private readonly Dictionary<string, TypeDefinition> _types;

        public AbiRuntimeCatalog(ModuleDefinition module, AbiSignatureFormatter formatter)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

            foreach (TypeDefinition type in EnumerateTypes(module.Types))
            {
                _types[type.FullName] = type;
            }
        }

        public ModuleDefinition Module => _module;

        public TypeDefinition FindType(TypeReference reference)
        {
            TypeReference element = AbiSignatureFormatter.GetElementType(reference);
            return FindType(element.FullName);
        }

        public TypeDefinition FindType(string fullName)
        {
            return !string.IsNullOrWhiteSpace(fullName) && _types.TryGetValue(fullName, out TypeDefinition type)
                ? type
                : null;
        }

        public IMemberDefinition Resolve(MemberReference reference)
        {
            if (reference is MethodSpecification methodSpecification)
            {
                reference = methodSpecification.ElementMethod;
            }

            TypeDefinition declaringType = FindType(reference.DeclaringType);
            if (declaringType == null)
            {
                return null;
            }

            if (reference is MethodReference method)
            {
                string bindingSignature = _formatter.GetMethodBindingSignature(method);
                return EnumerateTypeHierarchy(declaringType)
                    .SelectMany(type => type.Methods)
                    .FirstOrDefault(candidate =>
                        string.Equals(
                            _formatter.GetMethodBindingSignature(candidate),
                            bindingSignature,
                            StringComparison.Ordinal));
            }

            if (reference is FieldReference field)
            {
                string fieldType = _formatter.GetTypeSignature(field.FieldType);
                return EnumerateTypeHierarchy(declaringType)
                    .SelectMany(type => type.Fields)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, field.Name, StringComparison.Ordinal) &&
                        string.Equals(
                            _formatter.GetTypeSignature(candidate.FieldType),
                            fieldType,
                            StringComparison.Ordinal));
            }

            return null;
        }

        private IEnumerable<TypeDefinition> EnumerateTypeHierarchy(TypeDefinition start)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            Queue<TypeDefinition> pending = new Queue<TypeDefinition>();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                TypeDefinition current = pending.Dequeue();
                if (current == null || !visited.Add(current.FullName))
                {
                    continue;
                }

                yield return current;

                if (current.BaseType != null && _formatter.IsApiType(current.BaseType))
                {
                    pending.Enqueue(FindType(current.BaseType));
                }

                foreach (InterfaceImplementation implementation in current.Interfaces)
                {
                    if (_formatter.IsApiType(implementation.InterfaceType))
                    {
                        pending.Enqueue(FindType(implementation.InterfaceType));
                    }
                }
            }
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> roots)
        {
            foreach (TypeDefinition type in roots)
            {
                yield return type;
                foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }
    }
}
