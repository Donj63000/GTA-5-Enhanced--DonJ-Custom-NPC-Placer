using System;
using System.IO;
using System.Linq;
using DonJ.NibAbiValidator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;

[TestClass]
[DoNotParallelize]
public sealed class NibAbiValidatorTests
{
    [TestMethod]
    public void TestedConsumerAndResolvedApiMatchTheCanonicalNibContract()
    {
        AbiValidationResult result = AbiValidator.Verify(
            typeof(DonJEnemySpawner).Assembly.Location,
            GetContractPath(),
            typeof(DonJEnemySpawner).BaseType.Assembly.Location);

        Assert.IsTrue(
            result.IsValid,
            string.Join(Environment.NewLine, result.Errors));
        Assert.IsTrue(result.RuntimeValidated);
        Assert.IsTrue(result.CheckedTypeReferences > 0);
        Assert.IsTrue(result.CheckedMemberReferences > 0);
    }

    [TestMethod]
    public void RuntimeHashWithInt32UnderlyingTypeIsRejectedBySchema2()
    {
        string temporaryRuntime = Path.Combine(
            Path.GetTempPath(),
            "DonJInvalidNibHashRuntime_" + Guid.NewGuid().ToString("N") + ".dll");

        try
        {
            CreateMutatedRuntimeApi(
                typeof(DonJEnemySpawner).BaseType.Assembly.Location,
                temporaryRuntime,
                module =>
                {
                    TypeDefinition hashType = module.GetType("GTA.Native.Hash");
                    Assert.IsNotNull(hashType, "Le runtime de test doit exposer GTA.Native.Hash.");
                    FieldDefinition underlyingField = hashType.Fields.Single(field =>
                        field.Name == "value__");
                    Assert.AreEqual("System.UInt64", underlyingField.FieldType.FullName);

                    // Je simule une API portant la bonne identité mais un enum Hash
                    // binairement incompatible avec les natives 64 bits de NIB.
                    underlyingField.FieldType = module.TypeSystem.Int32;
                });

            AbiValidationResult result = AbiValidator.Verify(
                typeof(DonJEnemySpawner).Assembly.Location,
                GetContractPath(),
                temporaryRuntime);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(
                result.Errors.Any(error =>
                    error.IndexOf("ABI021", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("GTA.Native.Hash", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("underlying=", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("System.Int32", StringComparison.Ordinal) >= 0),
                string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            if (File.Exists(temporaryRuntime))
            {
                File.Delete(temporaryRuntime);
            }
        }
    }

    [TestMethod]
    public void ReferencedRuntimeMethodWithPrivateVisibilityIsRejectedBySchema2()
    {
        string temporaryRuntime = Path.Combine(
            Path.GetTempPath(),
            "DonJInvalidNibVisibilityRuntime_" + Guid.NewGuid().ToString("N") + ".dll");

        try
        {
            CreateMutatedRuntimeApi(
                typeof(DonJEnemySpawner).BaseType.Assembly.Location,
                temporaryRuntime,
                module =>
                {
                    TypeDefinition functionType = module.GetType("GTA.Native.Function");
                    Assert.IsNotNull(functionType, "Le runtime de test doit exposer GTA.Native.Function.");
                    MethodDefinition referencedCall = functionType.Methods.Single(method =>
                        method.Name == "Call" &&
                        method.GenericParameters.Count == 1 &&
                        method.Parameters.Count == 2 &&
                        method.Parameters[0].ParameterType.FullName == "GTA.Native.Hash" &&
                        method.Parameters[1].ParameterType is ArrayType arguments &&
                        arguments.ElementType.FullName == "GTA.Native.InputArgument");
                    Assert.IsTrue(referencedCall.IsPublic);

                    // Je conserve exactement la signature consommée et ne change
                    // que sa visibilité afin d'exercer le diagnostic ABI042.
                    referencedCall.Attributes =
                        (referencedCall.Attributes & ~MethodAttributes.MemberAccessMask) |
                        MethodAttributes.Private;
                });

            AbiValidationResult result = AbiValidator.Verify(
                typeof(DonJEnemySpawner).Assembly.Location,
                GetContractPath(),
                temporaryRuntime);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(
                result.Errors.Any(error =>
                    error.IndexOf("ABI042", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("GTA.Native.Function", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("visibility=", StringComparison.Ordinal) >= 0 &&
                    error.IndexOf("visibility=private", StringComparison.Ordinal) >= 0),
                string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            if (File.Exists(temporaryRuntime))
            {
                File.Delete(temporaryRuntime);
            }
        }
    }

    [TestMethod]
    public void ObjectArrayFunctionCallIsRejectedEvenWhenAssemblyVersionIsCorrect()
    {
        string temporaryAssembly = Path.Combine(
            Path.GetTempPath(),
            "DonJInvalidNibConsumer_" + Guid.NewGuid().ToString("N") + ".dll");

        try
        {
            CreateConsumerWithForbiddenObjectArrayCall(
                typeof(DonJEnemySpawner).Assembly.Location,
                temporaryAssembly);

            AbiValidationResult result = AbiValidator.Verify(
                temporaryAssembly,
                GetContractPath(),
                typeof(DonJEnemySpawner).BaseType.Assembly.Location);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(
                result.Errors.Any(error =>
                    error.IndexOf("System.Object[]", StringComparison.Ordinal) >= 0 &&
                    (error.IndexOf("ABI040", StringComparison.Ordinal) >= 0 ||
                     error.IndexOf("ABI041", StringComparison.Ordinal) >= 0)),
                string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            if (File.Exists(temporaryAssembly))
            {
                File.Delete(temporaryAssembly);
            }
        }
    }

    [TestMethod]
    public void UInt64FunctionCallIsRejectedEvenWithValidInputArgumentArray()
    {
        string temporaryAssembly = Path.Combine(
            Path.GetTempPath(),
            "DonJInvalidNibUInt64Consumer_" + Guid.NewGuid().ToString("N") + ".dll");

        try
        {
            CreateConsumerWithForbiddenUInt64Call(
                typeof(DonJEnemySpawner).Assembly.Location,
                temporaryAssembly);

            AbiValidationResult result = AbiValidator.Verify(
                temporaryAssembly,
                GetContractPath(),
                typeof(DonJEnemySpawner).BaseType.Assembly.Location);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(
                result.Errors.Any(error =>
                    error.IndexOf("System.UInt64", StringComparison.Ordinal) >= 0 &&
                    (error.IndexOf("ABI040", StringComparison.Ordinal) >= 0 ||
                     error.IndexOf("ABI041", StringComparison.Ordinal) >= 0)),
                string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            if (File.Exists(temporaryAssembly))
            {
                File.Delete(temporaryAssembly);
            }
        }
    }

    private static void CreateConsumerWithForbiddenObjectArrayCall(
        string sourceAssembly,
        string outputAssembly)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(
            sourceAssembly,
            new ReaderParameters { InMemory = true, ReadSymbols = false }))
        {
            TypeReference functionType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.Function");
            TypeReference hashType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.Hash");
            MethodReference forbiddenCall = new MethodReference(
                "Call",
                module.TypeSystem.Void,
                functionType)
            {
                HasThis = false
            };
            forbiddenCall.Parameters.Add(new ParameterDefinition(hashType));
            forbiddenCall.Parameters.Add(new ParameterDefinition(
                new ArrayType(module.TypeSystem.Object)));

            TypeDefinition fixtureType = new TypeDefinition(
                "DonJ.Tests",
                "InvalidNibConsumer",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
                module.TypeSystem.Object);
            MethodDefinition fixtureMethod = new MethodDefinition(
                "InvokeForbiddenCall",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            fixtureType.Methods.Add(fixtureMethod);
            module.Types.Add(fixtureType);

            ILProcessor il = fixtureMethod.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I8, 0L));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Newarr, module.TypeSystem.Object));
            il.Append(il.Create(OpCodes.Call, forbiddenCall));
            il.Append(il.Create(OpCodes.Ret));

            module.Write(outputAssembly);
        }
    }

    private static void CreateMutatedRuntimeApi(
        string sourceAssembly,
        string outputAssembly,
        Action<ModuleDefinition> mutate)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(
            sourceAssembly,
            new ReaderParameters { InMemory = true, ReadSymbols = false }))
        {
            mutate(module);
            module.Write(outputAssembly);
        }
    }

    private static void CreateConsumerWithForbiddenUInt64Call(
        string sourceAssembly,
        string outputAssembly)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(
            sourceAssembly,
            new ReaderParameters { InMemory = true, ReadSymbols = false }))
        {
            TypeReference functionType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.Function");
            TypeReference inputArgumentType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.InputArgument");

            // Je conserve le tableau InputArgument valide pour isoler précisément
            // l'ancienne surcharge dont le hash était exposé en UInt64.
            MethodReference forbiddenCall = new MethodReference(
                "Call",
                module.TypeSystem.Void,
                functionType)
            {
                HasThis = false
            };
            forbiddenCall.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt64));
            forbiddenCall.Parameters.Add(new ParameterDefinition(
                new ArrayType(inputArgumentType)));

            TypeDefinition fixtureType = new TypeDefinition(
                "DonJ.Tests",
                "InvalidNibUInt64Consumer",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
                module.TypeSystem.Object);
            MethodDefinition fixtureMethod = new MethodDefinition(
                "InvokeForbiddenUInt64Call",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void);
            fixtureType.Methods.Add(fixtureMethod);
            module.Types.Add(fixtureType);

            ILProcessor il = fixtureMethod.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I8, 0L));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Newarr, inputArgumentType));
            il.Append(il.Create(OpCodes.Call, forbiddenCall));
            il.Append(il.Create(OpCodes.Ret));

            module.Write(outputAssembly);
        }
    }

    private static string GetContractPath()
    {
        DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "tools",
                "NibAbiValidator",
                "contracts",
                "NIBScriptHookVDotNet2-2.11.6.abi.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Contrat ABI NIB 2.11.6 introuvable.");
    }
}
