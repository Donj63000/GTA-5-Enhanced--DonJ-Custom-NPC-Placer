using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public class PackagingSafetyTests
{
    private static readonly string[] RequiredJusticeTypes =
    {
        "DonJEnemySpawner",
        "JusticePolicy",
        "JusticeCaseState",
        "JusticePlayerProfileState",
        "JusticeTransition",
        "JusticeRepository",
        "JusticeWriteAheadLog",
        "JusticeXmlPersistenceCodec",
        "JusticeWorldSnapshot"
    };

    [TestMethod]
    public void GameReadyPackage_CopiesTheTestedBuildAndPublishesVerifiableMetadata()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot);
            string buildDirectory = GetReleaseBuildDirectory();
            string buildEndll = Path.Combine(buildDirectory, "DonJCustomNpcPlacer.ENdll");
            string buildPdb = Path.Combine(buildDirectory, "DonJCustomNpcPlacer.pdb");
            string packageEndll = Path.Combine(packageDirectory, "DonJCustomNpcPlacer.ENdll");
            string packagePdb = Path.Combine(packageDirectory, "DonJCustomNpcPlacer.pdb");
            string packageGuide = Path.Combine(packageDirectory, "INSTALLATION_SIMPLE.txt");
            string manifestPath = Path.Combine(packageDirectory, "manifest.json");

            Assert.AreEqual(HashFile(buildEndll), HashFile(packageEndll));
            Assert.AreEqual(HashFile(buildPdb), HashFile(packagePdb));
            Assert.IsTrue(new FileInfo(packageEndll).Length > 0);
            Assert.IsTrue(new FileInfo(packagePdb).Length > 0);
            Assert.IsTrue(File.Exists(packageGuide));

            PackageManifest manifest = ReadManifest(manifestPath);
            Assert.AreEqual(2, manifest.ManifestVersion);
            Assert.AreEqual("DonJCustomNpcPlacer", manifest.Product);
            Assert.AreEqual("Release", manifest.Configuration);
            Assert.AreEqual(GetHeadCommit(), manifest.Commit);
            Assert.AreEqual(HashFile(packageEndll), manifest.Files.Binary.Sha256);
            Assert.AreEqual(new FileInfo(packageEndll).Length, manifest.Files.Binary.SizeBytes);
            Assert.AreEqual(HashFile(packagePdb), manifest.Files.Symbols.Sha256);
            Assert.AreEqual(new FileInfo(packagePdb).Length, manifest.Files.Symbols.SizeBytes);

            AssemblyName assemblyName = AssemblyName.GetAssemblyName(packageEndll);
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(packageEndll);
            Assert.AreEqual(assemblyName.Version.ToString(), manifest.AssemblyVersion);
            Assert.AreEqual(versionInfo.ProductVersion, manifest.InformationalVersion);
            StringAssert.Contains(manifest.InformationalVersion, manifest.Commit);
            Assert.AreEqual(2, manifest.JusticeSchemaVersion);

            Type schemaType = typeof(DonJEnemySpawner).Assembly.GetType(
                "JusticeXmlPersistenceCodec",
                false);
            Assert.IsNotNull(schemaType, "Le codec de persistance Justice canonique est absent du binaire.");
            FieldInfo schemaField = schemaType.GetField(
                "SchemaMajor",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(schemaField, "La version majeure du schema Justice doit rester detectable.");
            Assert.AreEqual((int)schemaField.GetRawConstantValue(), manifest.JusticeSchemaVersion);

            CollectionAssert.AreEquivalent(RequiredJusticeTypes, manifest.ExpectedTypes);
            AssemblyName scriptApiReference;
            string[] packagedTypes = ReadExpectedTypesFromExactPackage(
                packageEndll,
                out scriptApiReference);
            CollectionAssert.AreEquivalent(RequiredJusticeTypes, packagedTypes);
            Assert.IsNotNull(manifest.ScriptApi, "Le manifest doit publier l'identité de l'API de script.");
            Assert.AreEqual(scriptApiReference.Name, manifest.ScriptApi.Name);
            Assert.AreEqual(scriptApiReference.Version.ToString(), manifest.ScriptApi.Version);
            Assert.AreEqual(2, scriptApiReference.Version.Major);
            Assert.AreEqual(scriptApiReference.Version.Major, manifest.ScriptApi.Major);
            Assert.IsNotNull(
                manifest.ScriptApi.AbiContract,
                "Le manifest doit verrouiller le contrat ABI réellement vérifié.");
            Assert.AreEqual("nib-shvdn-v2.11.6", manifest.ScriptApi.AbiContract.Id);
            Assert.AreEqual("2.11.6", manifest.ScriptApi.AbiContract.Version);
            Assert.AreEqual(
                HashFile(GetAbiContractPath()),
                manifest.ScriptApi.AbiContract.Sha256);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "DonJCustomNpcPlacer.ENdll",
                    "DonJCustomNpcPlacer.pdb",
                    "INSTALLATION_SIMPLE.txt",
                    "manifest.json"
                },
                Directory.GetFiles(packageDirectory)
                    .Select(Path.GetFileName)
                    .ToArray());
        });
    }

    [TestMethod]
    public void GameReadyPackage_RejectsDirtyGitSourceUnlessExplicitlyAllowed()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string repositoryRoot = Path.Combine(tempRoot, "source");
            string guideDirectory = Path.Combine(repositoryRoot, "Mode-pour-jeu-ici");
            Directory.CreateDirectory(guideDirectory);
            File.WriteAllText(Path.Combine(repositoryRoot, "GTA5modDEV.sln"), string.Empty);
            string guidePath = Path.Combine(guideDirectory, "INSTALLATION_SIMPLE.txt");
            File.WriteAllText(guidePath, "guide propre");

            AssertGitSuccess(repositoryRoot, "init", "--quiet");

            string rejectedOutput = Path.Combine(tempRoot, "rejected-package");
            ProcessResult rejected = RunPowerShell(
                GetPackageScriptPath(),
                "-Configuration", "Release",
                "-RepositoryRoot", repositoryRoot,
                "-BuildDirectory", GetReleaseBuildDirectory(),
                "-OutputDirectory", rejectedOutput,
                "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location),
                "-Commit", GetHeadCommit());

            Assert.AreNotEqual(0, rejected.ExitCode, "Une source Git sale devait bloquer le package.");
            StringAssert.Contains(rejected.CombinedOutput, "La source Git contient des changements non valides");
            Assert.IsFalse(Directory.Exists(rejectedOutput));

            string allowedOutput = Path.Combine(tempRoot, "allowed-package");
            ProcessResult allowed = RunPowerShellAllowDirtySource(
                GetPackageScriptPath(),
                "-Configuration", "Release",
                "-RepositoryRoot", repositoryRoot,
                "-BuildDirectory", GetReleaseBuildDirectory(),
                "-OutputDirectory", allowedOutput,
                "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location),
                "-Commit", GetHeadCommit());

            Assert.AreEqual(0, allowed.ExitCode, allowed.CombinedOutput);
            Assert.IsTrue(ReadManifest(Path.Combine(allowedOutput, "manifest.json")).SourceDirty);
        });
    }

    [TestMethod]
    public void GameReadyPackage_RequiresExactSchemaV2AndAllCriticalJusticeTypes()
    {
        string script = File.ReadAllText(GetPackageScriptPath());

        StringAssert.Contains(script, "$assemblyMetadata.JusticeSchemaVersion -ne 2");
        foreach (string requiredType in RequiredJusticeTypes)
        {
            StringAssert.Contains(script, "\"" + requiredType + "\"");
        }
    }

    [TestMethod]
    public void StubApiAndDeliveryPipeline_RequireTheRuntimeCompatibleV2Identity()
    {
        string repositoryRoot = GetRepositoryRoot();
        string stubProject = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "Stubs",
            "NIBScriptHookVDotNet2",
            "NIBScriptHookVDotNet2.csproj"));
        string packageScript = File.ReadAllText(GetPackageScriptPath());
        string deployScript = File.ReadAllText(GetDeployScriptPath());
        string safetyScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "run-safety-checks.ps1"));
        string gitAttributes = File.ReadAllText(Path.Combine(repositoryRoot, ".gitattributes"));

        StringAssert.Contains(stubProject, "<AssemblyVersion>2.11.6.0</AssemblyVersion>");
        StringAssert.Contains(stubProject, "<FileVersion>2.11.6.0</FileVersion>");
        StringAssert.Contains(packageScript, "$reference.Version.Major -ne 2");
        StringAssert.Contains(deployScript, "$reference.Version.Major -ne 2");
        StringAssert.Contains(safetyScript, "$reference.Version.Major -ne 2");
        StringAssert.Contains(packageScript, "scriptApi = [ordered]@{");
        StringAssert.Contains(packageScript, "manifestVersion = 2");
        StringAssert.Contains(packageScript, "Invoke-AbiValidator");
        StringAssert.Contains(packageScript, "--consumer\", $packageEndll");
        StringAssert.Contains(packageScript, "$packageEndllHash -ne $validatedBuildEndllHash");
        StringAssert.Contains(packageScript, "abiContract = [ordered]@{");
        StringAssert.Contains(deployScript, "$scriptApi.version");
        StringAssert.Contains(deployScript, "--runtime-api");
        StringAssert.Contains(deployScript, "avant toute ecriture");
        StringAssert.Contains(safetyScript, "$manifest.scriptApi.version");
        StringAssert.Contains(safetyScript, "verify-nib-abi");
        StringAssert.Contains(
            gitAttributes,
            "tools/NibAbiValidator/contracts/*.abi.xml text eol=lf");
    }

    [TestMethod]
    public void GameReadyPackage_ForceRefusesToReplaceAnUnrecognizedDirectory()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string protectedDirectory = Path.Combine(tempRoot, "important-files");
            string sentinelPath = Path.Combine(protectedDirectory, "sentinel.txt");
            Directory.CreateDirectory(protectedDirectory);
            File.WriteAllText(sentinelPath, "a-conserver");

            ProcessResult result = RunPowerShellWithForce(
                GetPackageScriptPath(),
                "-Configuration", "Release",
                "-RepositoryRoot", GetRepositoryRoot(),
                "-BuildDirectory", GetReleaseBuildDirectory(),
                "-OutputDirectory", protectedDirectory,
                "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location));

            Assert.AreNotEqual(0, result.ExitCode, "-Force devait refuser un dossier arbitraire non vide.");
            StringAssert.Contains(result.CombinedOutput, "n'est pas un package game-ready reconnu");
            Assert.AreEqual("a-conserver", File.ReadAllText(sentinelPath));
            CollectionAssert.AreEquivalent(
                new[] { "sentinel.txt" },
                Directory.GetFiles(protectedDirectory).Select(Path.GetFileName).ToArray());
        });
    }

    [TestMethod]
    public void GameReadyPackage_RejectsInvalidAbiBeforeCreatingOrReplacingOutput()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string invalidBuildDirectory = Path.Combine(tempRoot, "invalid-build");
            Directory.CreateDirectory(invalidBuildDirectory);
            CreateConsumerWithForbiddenObjectArrayCall(
                Path.Combine(GetReleaseBuildDirectory(), "DonJCustomNpcPlacer.ENdll"),
                Path.Combine(invalidBuildDirectory, "DonJCustomNpcPlacer.ENdll"));
            File.Copy(
                Path.Combine(GetReleaseBuildDirectory(), "DonJCustomNpcPlacer.pdb"),
                Path.Combine(invalidBuildDirectory, "DonJCustomNpcPlacer.pdb"));

            string absentOutput = Path.Combine(tempRoot, "rejected-new-package");
            ProcessResult createResult = RunPowerShellAllowDirtySource(
                GetPackageScriptPath(),
                "-Configuration", "Release",
                "-RepositoryRoot", GetRepositoryRoot(),
                "-BuildDirectory", invalidBuildDirectory,
                "-OutputDirectory", absentOutput,
                "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location));

            Assert.AreNotEqual(0, createResult.ExitCode, createResult.CombinedOutput);
            StringAssert.Contains(createResult.CombinedOutput, "System.Object[]");
            StringAssert.Contains(createResult.CombinedOutput, "ABI04");
            Assert.IsFalse(
                Directory.Exists(absentOutput),
                "Je ne dois pas créer la sortie quand le consommateur ABI est invalide.");
            Assert.AreEqual(
                0,
                Directory.GetDirectories(tempRoot, ".rejected-new-package.*").Length,
                "Je ne dois laisser aucun dossier transactionnel après le rejet ABI.");

            string existingOutput = CreateVerifiedPackage(tempRoot, true);
            Dictionary<string, string> originalPackage = SnapshotDirectoryHashes(existingOutput);
            ProcessResult replaceResult = RunPowerShellWithForce(
                GetPackageScriptPath(),
                "-Configuration", "Release",
                "-RepositoryRoot", GetRepositoryRoot(),
                "-BuildDirectory", invalidBuildDirectory,
                "-OutputDirectory", existingOutput,
                "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location));

            Assert.AreNotEqual(0, replaceResult.ExitCode, replaceResult.CombinedOutput);
            StringAssert.Contains(replaceResult.CombinedOutput, "System.Object[]");
            StringAssert.Contains(replaceResult.CombinedOutput, "ABI04");
            AssertDirectorySnapshot(existingOutput, originalPackage);
            Assert.AreEqual(
                0,
                Directory.GetDirectories(tempRoot, ".game-ready.*").Length,
                "Le rejet ABI doit précéder tout remplacement transactionnel du package existant.");
        });
    }

    [TestMethod]
    public void CiWorkflow_PublishesTheVerifiedGameReadyPackageOnlyAfterSuccess()
    {
        string workflow = File.ReadAllText(
            Path.Combine(GetRepositoryRoot(), ".github", "workflows", "safety.yml"));

        StringAssert.Contains(workflow, ".\\tools\\run-safety-checks.ps1 -Ci -UseStubApi");
        StringAssert.Contains(workflow, "-PackageOutputDirectory .\\artifacts\\game-ready");
        StringAssert.Contains(
            workflow,
            "if: success() && github.event_name == 'push' && github.ref == 'refs/heads/main'");
        StringAssert.Contains(workflow, "name: DonJCustomNpcPlacer-game-ready");
        StringAssert.Contains(workflow, "path: artifacts/game-ready/**");
        StringAssert.Contains(workflow, "if-no-files-found: error");
        Assert.IsFalse(
            workflow.Contains("-AllowDirtySource"),
            "La CI ne doit jamais contourner le garde-fou des sources Git sales.");
    }

    [TestMethod]
    public void InstallationGuides_RequireTheVerifiedMainPackageAndSafeReplacement()
    {
        string repositoryRoot = GetRepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        string simpleGuide = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Mode-pour-jeu-ici",
            "INSTALLATION_SIMPLE.txt"));

        foreach (string requiredFile in new[]
                 {
                     "DonJCustomNpcPlacer.ENdll",
                     "DonJCustomNpcPlacer.pdb",
                     "INSTALLATION_SIMPLE.txt",
                     "manifest.json"
                 })
        {
            StringAssert.Contains(readme, requiredFile);
            StringAssert.Contains(simpleGuide, requiredFile);
        }

        StringAssert.Contains(readme, "branch is `main`");
        StringAssert.Contains(readme, "event is `push`");
        StringAssert.Contains(readme, "`manifestVersion` is `2`");
        StringAssert.Contains(readme, "`sourceDirty` is `false`");
        StringAssert.Matches(
            readme,
            new Regex("`scriptApi\\.major`\\s+is\\s+`2`", RegexOptions.CultureInvariant));
        StringAssert.Contains(simpleGuide, "branche est main");
        StringAssert.Contains(simpleGuide, "l'evenement push");
        StringAssert.Contains(simpleGuide, "manifestVersion vaut 2");
        StringAssert.Contains(simpleGuide, "sourceDirty vaut false");
        StringAssert.Contains(simpleGuide, "scriptApi.major vaut 2");
        StringAssert.Contains(simpleGuide, "scriptApi.abiContract.sha256");

        StringAssert.Contains(
            readme,
            "Do not delete the installed `.ENdll` before the new");
        StringAssert.Contains(
            simpleGuide,
            "Ne supprimez jamais l'ancien ENdll avant d'avoir valide le nouveau fichier a");
        Assert.IsFalse(
            readme.Contains("Delete the old file:"),
            "Le README ne doit jamais demander de supprimer l'ENdll actif avant validation.");

        foreach (string obsoleteAlias in new[]
                 {
                     "DonJCustomNpcPlacer.dll",
                     "DonJEnemySpawner.dll",
                     "DonJEnemySpawner.ENdll",
                     "DonJEnemySpawner.pdb"
                 })
        {
            StringAssert.Contains(readme, obsoleteAlias);
            StringAssert.Contains(simpleGuide, obsoleteAlias);
        }

        StringAssert.Contains(readme, "Scripts\\DonJCustomNpcPlacer.manifest.json");
        StringAssert.Contains(simpleGuide, "DonJCustomNpcPlacer.manifest.json");
        StringAssert.Contains(readme, "Microsoft .NET Framework 4.8");
        StringAssert.Contains(simpleGuide, "Microsoft .NET Framework 4.8");
    }

    [TestMethod]
    public void SafetyScript_ObservesTheExpectedDirtyDeployFailureWithoutAbortingOnStderr()
    {
        string script = File.ReadAllText(
            Path.Combine(GetRepositoryRoot(), "tools", "run-safety-checks.ps1"));

        int relaxedErrors = script.IndexOf(
            "$ErrorActionPreference = \"Continue\"",
            StringComparison.Ordinal);
        int invocation = script.IndexOf(
            "& powershell @deployArguments *>",
            StringComparison.Ordinal);
        int capturedExitCode = script.IndexOf(
            "$dirtyDeployExitCode = $LASTEXITCODE",
            StringComparison.Ordinal);
        int restoredErrors = script.IndexOf(
            "$ErrorActionPreference = $safetyErrorActionPreference",
            StringComparison.Ordinal);

        Assert.IsTrue(relaxedErrors >= 0, "Le stderr attendu doit être temporairement non bloquant.");
        Assert.IsTrue(invocation > relaxedErrors, "Le refus doit être lancé après l'assouplissement local.");
        Assert.IsTrue(capturedExitCode > invocation, "Le code du refus attendu doit être capturé.");
        Assert.IsTrue(restoredErrors > capturedExitCode, "La politique stricte doit être restaurée en finally.");
        StringAssert.Contains(script, "if ($dirtyDeployExitCode -eq 0)");
    }

    [TestMethod]
    public void GameReadyDeployment_ReplacesVerifiedFilesThenRemovesLegacyAliases()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);

            File.WriteAllText(Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll"), "ancien-binaire");
            File.WriteAllText(Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb"), "ancien-pdb");
            File.WriteAllText(
                Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json"),
                "ancien-manifest");
            foreach (string alias in ObsoleteAliases())
            {
                File.WriteAllText(Path.Combine(scriptsDirectory, alias), "ancien-alias");
            }

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
            Assert.AreEqual(
                HashFile(Path.Combine(packageDirectory, "DonJCustomNpcPlacer.ENdll")),
                HashFile(Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll")));
            Assert.AreEqual(
                HashFile(Path.Combine(packageDirectory, "DonJCustomNpcPlacer.pdb")),
                HashFile(Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb")));
            Assert.AreEqual(
                HashFile(Path.Combine(packageDirectory, "manifest.json")),
                HashFile(Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json")));

            foreach (string alias in ObsoleteAliases())
            {
                Assert.IsFalse(
                    File.Exists(Path.Combine(scriptsDirectory, alias)),
                    "Aucun ancien alias ne doit rester dans le resultat publie: " + alias);
            }

            Assert.AreEqual(
                0,
                Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length,
                "Aucun fichier transactionnel ne doit rester apres le deploiement.");
        });
    }

    [TestMethod]
    public void GameReadyDeployment_RejectsAPackageBuiltFromDirtySources()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);
            string installed = Path.Combine(
                scriptsDirectory,
                "DonJCustomNpcPlacer.ENdll");
            File.WriteAllText(installed, "version-conservée");

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreNotEqual(0, result.ExitCode);
            Assert.AreEqual("version-conservée", File.ReadAllText(installed));
            StringAssert.Contains(result.CombinedOutput, "Manifest game-ready invalide");
        });
    }

    [TestMethod]
    public void GameReadyDeployment_RejectsAnIncompatibleScriptApiManifestWithoutTouchingTheGame()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string manifestPath = Path.Combine(packageDirectory, "manifest.json");
            string manifest = File.ReadAllText(manifestPath);
            string incompatibleManifest = Regex.Replace(
                manifest,
                "(\"major\"\\s*:\\s*)2",
                "${1}1",
                RegexOptions.CultureInvariant);
            Assert.AreNotEqual(manifest, incompatibleManifest, "Le fixture doit modifier la version majeure API.");
            File.WriteAllText(manifestPath, incompatibleManifest);

            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);
            string installed = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            File.WriteAllText(installed, "version-conservée");

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreNotEqual(0, result.ExitCode, "Une API de mauvaise version majeure devait être refusée.");
            Assert.AreEqual("version-conservée", File.ReadAllText(installed));
            StringAssert.Contains(result.CombinedOutput, "Manifest game-ready invalide");
        });
    }

    [TestMethod]
    public void GameReadyDeployment_RejectsAnotherAbiContractBeforeTouchingTheGame()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string manifestPath = Path.Combine(packageDirectory, "manifest.json");
            PackageManifest parsed = ReadManifest(manifestPath);
            string manifest = File.ReadAllText(manifestPath);
            string incompatibleHash = new string(
                parsed.ScriptApi.AbiContract.Sha256[0] == '0' ? '1' : '0',
                64);
            string incompatibleManifest = manifest.Replace(
                parsed.ScriptApi.AbiContract.Sha256,
                incompatibleHash);
            Assert.AreNotEqual(manifest, incompatibleManifest);
            File.WriteAllText(manifestPath, incompatibleManifest);

            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);
            string installed = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            File.WriteAllText(installed, "version-conservée");

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreNotEqual(0, result.ExitCode);
            Assert.AreEqual("version-conservée", File.ReadAllText(installed));
            StringAssert.Contains(
                result.CombinedOutput,
                "Le manifest ne reference pas le contrat ABI canonique attendu");
            Assert.AreEqual(
                0,
                Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length,
                "Le rejet ABI doit précéder tout staging transactionnel.");
        });
    }

    [TestMethod]
    public void GameReadyDeployment_RejectsInvalidAbiBeforeMutatingInstalledFiles()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string packageEndll = Path.Combine(packageDirectory, "DonJCustomNpcPlacer.ENdll");
            string invalidEndll = Path.Combine(tempRoot, "invalid-package.ENdll");
            CreateConsumerWithForbiddenObjectArrayCall(packageEndll, invalidEndll);
            File.Copy(invalidEndll, packageEndll, true);

            // Je rends volontairement le manifest cohérent avec le binaire altéré
            // afin que seul le contrôle ABI puisse arrêter le déploiement.
            string manifestPath = Path.Combine(packageDirectory, "manifest.json");
            PackageManifest manifest = ReadManifest(manifestPath);
            manifest.Files.Binary.SizeBytes = new FileInfo(packageEndll).Length;
            manifest.Files.Binary.Sha256 = HashFile(packageEndll);
            WriteManifest(manifestPath, manifest);

            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(
                Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll"),
                "binaire-installé-à-conserver");
            File.WriteAllText(
                Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb"),
                "pdb-installé-à-conserver");
            File.WriteAllText(
                Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json"),
                "manifest-installé-à-conserver");
            foreach (string alias in ObsoleteAliases())
            {
                File.WriteAllText(
                    Path.Combine(scriptsDirectory, alias),
                    "alias-à-conserver-" + alias);
            }
            Dictionary<string, string> installedSnapshot = SnapshotDirectoryHashes(scriptsDirectory);

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreNotEqual(0, result.ExitCode, result.CombinedOutput);
            StringAssert.Contains(result.CombinedOutput, "System.Object[]");
            StringAssert.Contains(result.CombinedOutput, "ABI04");
            AssertDirectorySnapshot(scriptsDirectory, installedSnapshot);
            Assert.AreEqual(
                0,
                Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length,
                "Le rejet ABI doit intervenir avant tout staging dans le dossier du jeu.");
        });
    }

    [TestMethod]
    public void GameReadyDeployment_RejectsCorruptedPackageWithoutTouchingInstalledFiles()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);

            string installedEndll = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            string installedPdb = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb");
            string installedManifest = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json");
            byte[] originalEndll = { 0x44, 0x4A, 0x4F, 0x4C, 0x44 };
            byte[] originalPdb = { 0x50, 0x44, 0x42 };
            const string OriginalManifest = "{\"ancien\":true}";
            File.WriteAllBytes(installedEndll, originalEndll);
            File.WriteAllBytes(installedPdb, originalPdb);
            File.WriteAllText(installedManifest, OriginalManifest);

            Dictionary<string, string> aliasContents = new Dictionary<string, string>();
            foreach (string alias in ObsoleteAliases())
            {
                string content = "conserver-" + alias;
                aliasContents.Add(alias, content);
                File.WriteAllText(Path.Combine(scriptsDirectory, alias), content);
            }

            using (FileStream stream = new FileStream(
                Path.Combine(packageDirectory, "DonJCustomNpcPlacer.ENdll"),
                FileMode.Append,
                FileAccess.Write,
                FileShare.None))
            {
                stream.WriteByte(0x00);
            }

            ProcessResult result = RunPowerShell(
                GetDeployScriptPath(),
                "-PackageDirectory", packageDirectory,
                "-GtaRoot", gtaRoot,
                "-GtaScriptsDir", scriptsDirectory);

            Assert.AreNotEqual(0, result.ExitCode, "Le package corrompu devait etre refuse.");
            CollectionAssert.AreEqual(originalEndll, File.ReadAllBytes(installedEndll));
            CollectionAssert.AreEqual(originalPdb, File.ReadAllBytes(installedPdb));
            Assert.AreEqual(OriginalManifest, File.ReadAllText(installedManifest));

            foreach (KeyValuePair<string, string> alias in aliasContents)
            {
                Assert.AreEqual(
                    alias.Value,
                    File.ReadAllText(Path.Combine(scriptsDirectory, alias.Key)),
                    "Un echec de validation ne doit pas nettoyer les anciens alias.");
            }

            Assert.AreEqual(0, Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length);
        });
    }

    [TestMethod]
    public void GameReadyDeployment_PdbReplacementFailureRollsBackTheBinary()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);

            string installedEndll = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            string installedPdb = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb");
            string installedManifest = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json");
            byte[] originalEndll = { 0x44, 0x4A, 0x4F, 0x4C, 0x44 };
            byte[] originalPdb = { 0x50, 0x44, 0x42 };
            const string OriginalManifest = "{\"ancien\":true}";
            File.WriteAllBytes(installedEndll, originalEndll);
            File.WriteAllBytes(installedPdb, originalPdb);
            File.WriteAllText(installedManifest, OriginalManifest);

            foreach (string alias in ObsoleteAliases())
            {
                File.WriteAllText(Path.Combine(scriptsDirectory, alias), "conserver-" + alias);
            }

            ProcessResult result;
            using (FileStream lockStream = new FileStream(
                installedPdb,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
                result = RunPowerShell(
                    GetDeployScriptPath(),
                    "-PackageDirectory", packageDirectory,
                    "-GtaRoot", gtaRoot,
                    "-GtaScriptsDir", scriptsDirectory);
            }

            Assert.AreNotEqual(0, result.ExitCode, "Le PDB verrouille devait interrompre le deploiement.");
            CollectionAssert.AreEqual(originalEndll, File.ReadAllBytes(installedEndll));
            CollectionAssert.AreEqual(originalPdb, File.ReadAllBytes(installedPdb));
            Assert.AreEqual(OriginalManifest, File.ReadAllText(installedManifest));
            foreach (string alias in ObsoleteAliases())
            {
                Assert.IsTrue(
                    File.Exists(Path.Combine(scriptsDirectory, alias)),
                    "Le rollback ne doit pas nettoyer les alias avant la validation globale.");
            }
            Assert.AreEqual(0, Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length);
        });
    }

    [TestMethod]
    public void GameReadyDeployment_ManifestReplacementFailureRollsBackBinaryAndPdb()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);

            string installedEndll = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            string installedPdb = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb");
            string installedManifest = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json");
            byte[] originalEndll = { 0x44, 0x4A, 0x4F, 0x4C, 0x44 };
            byte[] originalPdb = { 0x50, 0x44, 0x42 };
            const string OriginalManifest = "{\"ancien\":true}";
            File.WriteAllBytes(installedEndll, originalEndll);
            File.WriteAllBytes(installedPdb, originalPdb);
            File.WriteAllText(installedManifest, OriginalManifest);

            foreach (string alias in ObsoleteAliases())
            {
                File.WriteAllText(Path.Combine(scriptsDirectory, alias), "conserver-" + alias);
            }

            ProcessResult result;
            using (FileStream lockStream = new FileStream(
                installedManifest,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
                result = RunPowerShell(
                    GetDeployScriptPath(),
                    "-PackageDirectory", packageDirectory,
                    "-GtaRoot", gtaRoot,
                    "-GtaScriptsDir", scriptsDirectory);
            }

            Assert.AreNotEqual(0, result.ExitCode, "Le manifest verrouille devait interrompre le deploiement.");
            CollectionAssert.AreEqual(originalEndll, File.ReadAllBytes(installedEndll));
            CollectionAssert.AreEqual(originalPdb, File.ReadAllBytes(installedPdb));
            Assert.AreEqual(OriginalManifest, File.ReadAllText(installedManifest));
            foreach (string alias in ObsoleteAliases())
            {
                Assert.IsTrue(
                    File.Exists(Path.Combine(scriptsDirectory, alias)),
                    "Le rollback ne doit pas nettoyer les alias avant la validation globale.");
            }
            Assert.AreEqual(0, Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length);
        });
    }

    [TestMethod]
    public void GameReadyDeployment_LockedLegacyAliasAfterPublicationRollsBackEverything()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string deploymentSource = File.ReadAllText(GetDeployScriptPath());
            AssertTextOrdered(
                deploymentSource,
                "$manifestInstalled = $true",
                "Le manifest GTA ne correspond pas au package apres remplacement.",
                "Je publie et relis d'abord le triplet canonique",
                "[System.IO.File]::Move(",
                "Je restaure les alias avant de retirer le nouvel ENdll",
                "if ($manifestInstalled)");

            string packageDirectory = CreateVerifiedPackage(tempRoot, true);
            string gtaRoot = CreateFakeGtaRoot(tempRoot);
            string scriptsDirectory = Path.Combine(gtaRoot, "Scripts");
            Directory.CreateDirectory(scriptsDirectory);

            string installedEndll = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.ENdll");
            string installedPdb = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.pdb");
            string installedManifest = Path.Combine(scriptsDirectory, "DonJCustomNpcPlacer.manifest.json");
            byte[] originalEndll = { 0x44, 0x4A, 0x4F, 0x4C, 0x44 };
            byte[] originalPdb = { 0x50, 0x44, 0x42 };
            const string OriginalManifest = "{\"ancien\":true}";
            File.WriteAllBytes(installedEndll, originalEndll);
            File.WriteAllBytes(installedPdb, originalPdb);
            File.WriteAllText(installedManifest, OriginalManifest);

            Dictionary<string, string> aliasContents = new Dictionary<string, string>();
            foreach (string alias in ObsoleteAliases())
            {
                string content = "conserver-" + alias;
                aliasContents.Add(alias, content);
                File.WriteAllText(Path.Combine(scriptsDirectory, alias), content);
            }

            ProcessResult result;
            string lockedAlias = Path.Combine(scriptsDirectory, "DonJEnemySpawner.ENdll");
            using (FileStream lockStream = new FileStream(
                lockedAlias,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
                result = RunPowerShell(
                    GetDeployScriptPath(),
                    "-PackageDirectory", packageDirectory,
                    "-GtaRoot", gtaRoot,
                    "-GtaScriptsDir", scriptsDirectory);
            }

            Assert.AreNotEqual(0, result.ExitCode, "L'alias verrouille devait refuser le deploiement.");
            CollectionAssert.AreEqual(originalEndll, File.ReadAllBytes(installedEndll));
            CollectionAssert.AreEqual(originalPdb, File.ReadAllBytes(installedPdb));
            Assert.AreEqual(OriginalManifest, File.ReadAllText(installedManifest));
            foreach (KeyValuePair<string, string> alias in aliasContents)
            {
                Assert.AreEqual(
                    alias.Value,
                    File.ReadAllText(Path.Combine(scriptsDirectory, alias.Key)),
                    "Tous les alias doivent retrouver leur contenu apres le refus transactionnel.");
            }
            Assert.AreEqual(
                0,
                Directory.GetFiles(scriptsDirectory, ".DonJCustomNpcPlacer.*").Length,
                "Le refus sur alias verrouille ne doit laisser aucun fichier transactionnel.");
        });
    }

    private static void AssertTextOrdered(string source, params string[] markers)
    {
        int cursor = -1;
        foreach (string marker in markers)
        {
            int index = source.IndexOf(marker, cursor + 1, StringComparison.Ordinal);
            Assert.IsTrue(
                index >= 0,
                "Ordre de deploiement invalide ou marqueur absent : " + marker);
            cursor = index;
        }
    }

    private static string CreateVerifiedPackage(string tempRoot, bool publishable = false)
    {
        string packageDirectory = Path.Combine(tempRoot, "game-ready");
        ProcessResult result = RunPowerShellAllowDirtySource(
            GetPackageScriptPath(),
            "-Configuration", "Release",
            "-RepositoryRoot", GetRepositoryRoot(),
            "-BuildDirectory", GetReleaseBuildDirectory(),
            "-OutputDirectory", packageDirectory,
            "-DependencyDirectory", Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location));

        Assert.AreEqual(0, result.ExitCode, result.CombinedOutput);
        string manifestPath = Path.Combine(packageDirectory, "manifest.json");
        string manifest = File.ReadAllText(manifestPath);
        const string SourceDirtyPattern = "(\"sourceDirty\"\\s*:\\s*)(?:true|false)";
        Assert.IsTrue(
            Regex.IsMatch(manifest, SourceDirtyPattern, RegexOptions.CultureInvariant),
            "Le manifest du fixture doit exposer sourceDirty.");

        // Je fixe explicitement la nature du fixture pour que le résultat ne
        // dépende jamais de la propreté du checkout local ou de la CI.
        string fixtureManifest = Regex.Replace(
            manifest,
            SourceDirtyPattern,
            "$1" + (publishable ? "false" : "true"),
            RegexOptions.CultureInvariant);
        File.WriteAllText(manifestPath, fixtureManifest);
        Assert.AreEqual(
            !publishable,
            ReadManifest(manifestPath).SourceDirty,
            "Le fixture doit porter explicitement la politique demandée.");
        return packageDirectory;
    }

    private static void CreateConsumerWithForbiddenObjectArrayCall(
        string sourceAssembly,
        string outputAssembly)
    {
        using (Mono.Cecil.ModuleDefinition module = Mono.Cecil.ModuleDefinition.ReadModule(
            sourceAssembly,
            new Mono.Cecil.ReaderParameters { InMemory = true, ReadSymbols = false }))
        {
            Mono.Cecil.TypeReference functionType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.Function");
            Mono.Cecil.TypeReference hashType = module.GetTypeReferences().First(type =>
                type.FullName == "GTA.Native.Hash");
            Mono.Cecil.MethodReference forbiddenCall = new Mono.Cecil.MethodReference(
                "Call",
                module.TypeSystem.Void,
                functionType)
            {
                HasThis = false
            };
            forbiddenCall.Parameters.Add(new Mono.Cecil.ParameterDefinition(hashType));
            forbiddenCall.Parameters.Add(new Mono.Cecil.ParameterDefinition(
                new Mono.Cecil.ArrayType(module.TypeSystem.Object)));

            Mono.Cecil.TypeDefinition fixtureType = new Mono.Cecil.TypeDefinition(
                "DonJ.Tests",
                "InvalidPackagedNibConsumer",
                Mono.Cecil.TypeAttributes.Public |
                Mono.Cecil.TypeAttributes.Abstract |
                Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            Mono.Cecil.MethodDefinition fixtureMethod = new Mono.Cecil.MethodDefinition(
                "InvokeForbiddenCall",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            fixtureType.Methods.Add(fixtureMethod);
            module.Types.Add(fixtureType);

            Mono.Cecil.Cil.ILProcessor il = fixtureMethod.Body.GetILProcessor();
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I8, 0L));
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
            il.Append(il.Create(
                Mono.Cecil.Cil.OpCodes.Newarr,
                module.TypeSystem.Object));
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Call, forbiddenCall));
            il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));

            module.Write(outputAssembly);
        }
    }

    private static Dictionary<string, string> SnapshotDirectoryHashes(string directory)
    {
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => path.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length + 1),
                path =>
                {
                    FileInfo file = new FileInfo(path);
                    // Je verrouille aussi taille et horodatage pour prouver que
                    // le rejet ABI précède réellement toute écriture.
                    return HashFile(path) + "|" +
                           file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                           file.LastWriteTimeUtc.Ticks.ToString(
                               System.Globalization.CultureInfo.InvariantCulture);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertDirectorySnapshot(
        string directory,
        Dictionary<string, string> expected)
    {
        Dictionary<string, string> actual = SnapshotDirectoryHashes(directory);
        CollectionAssert.AreEquivalent(
            expected.Keys.ToArray(),
            actual.Keys.ToArray(),
            "Je dois conserver exactement les mêmes fichiers après le rejet ABI.");
        foreach (KeyValuePair<string, string> file in expected)
        {
            Assert.AreEqual(
                file.Value,
                actual[file.Key],
                "Le rejet ABI ne doit modifier aucun fichier: " + file.Key);
        }
    }

    private static string CreateFakeGtaRoot(string tempRoot)
    {
        string gtaRoot = Path.Combine(tempRoot, "fake-gta");
        Directory.CreateDirectory(gtaRoot);
        File.WriteAllBytes(Path.Combine(gtaRoot, "GTA5_Enhanced.exe"), new byte[0]);
        string runtimeApi = typeof(DonJEnemySpawner).BaseType.Assembly.Location;
        File.Copy(
            runtimeApi,
            Path.Combine(gtaRoot, Path.GetFileName(runtimeApi)),
            true);
        return gtaRoot;
    }

    private static string[] ObsoleteAliases()
    {
        return new[]
        {
            "DonJCustomNpcPlacer.dll",
            "DonJEnemySpawner.dll",
            "DonJEnemySpawner.ENdll",
            "DonJEnemySpawner.pdb"
        };
    }

    private static string[] ReadExpectedTypesFromExactPackage(
        string packageEndll,
        out AssemblyName scriptApiReference)
    {
        string dependencyDirectory = Path.GetDirectoryName(typeof(DonJEnemySpawner).Assembly.Location);
        ResolveEventHandler resolver = (sender, eventArgs) =>
        {
            AssemblyName requested = new AssemblyName(eventArgs.Name);
            string candidate = Path.Combine(dependencyDirectory, requested.Name + ".dll");
            if (File.Exists(candidate))
            {
                return Assembly.ReflectionOnlyLoadFrom(candidate);
            }

            return Assembly.ReflectionOnlyLoad(eventArgs.Name);
        };

        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolver;
        try
        {
            Assembly packageAssembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(packageEndll));
            scriptApiReference = ReadScriptApiReference(packageAssembly);
            return RequiredJusticeTypes
                .Where(typeName => packageAssembly.GetType(typeName, false) != null)
                .ToArray();
        }
        finally
        {
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolver;
        }
    }

    private static AssemblyName ReadScriptApiReference(Assembly packageAssembly)
    {
        AssemblyName[] references = packageAssembly.GetReferencedAssemblies()
            .Where(reference =>
                string.Equals(
                    reference.Name,
                    "NIBScriptHookVDotNet2",
                    StringComparison.Ordinal) ||
                string.Equals(
                    reference.Name,
                    "ScriptHookVDotNet2",
                    StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(
            1,
            references.Length,
            "Le package doit cibler exactement une API ScriptHookVDotNet v2.");
        return references[0];
    }

    private static PackageManifest ReadManifest(string path)
    {
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PackageManifest));
        using (FileStream stream = File.OpenRead(path))
        {
            return (PackageManifest)serializer.ReadObject(stream);
        }
    }

    private static void WriteManifest(string path, PackageManifest manifest)
    {
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PackageManifest));
        using (FileStream stream = File.Create(path))
        {
            serializer.WriteObject(stream, manifest);
        }
    }

    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }

    private static string GetHeadCommit()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse HEAD",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.IsNotNull(process);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(30000), "git rev-parse n'a pas termine.");
            Assert.AreEqual(0, process.ExitCode, error);
            return output.Trim().ToLowerInvariant();
        }
    }

    private static ProcessResult RunPowerShell(string scriptPath, params string[] arguments)
    {
        return RunPowerShellCore(scriptPath, false, false, arguments);
    }

    private static ProcessResult RunPowerShellAllowDirtySource(
        string scriptPath,
        params string[] arguments)
    {
        return RunPowerShellCore(scriptPath, false, true, arguments);
    }

    private static ProcessResult RunPowerShellWithForce(string scriptPath, params string[] arguments)
    {
        return RunPowerShellCore(scriptPath, true, true, arguments);
    }

    private static ProcessResult RunPowerShellCore(
        string scriptPath,
        bool force,
        bool allowDirtySource,
        params string[] arguments)
    {
        string allArguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(scriptPath);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            allArguments += " " + arguments[index] + " " + QuoteArgument(arguments[index + 1]);
        }
        if (force)
        {
            allArguments += " -Force";
        }
        if (allowDirtySource)
        {
            allArguments += " -AllowDirtySource";
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = allArguments,
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.IsNotNull(process, "Impossible de lancer PowerShell.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(120000), "Le script PowerShell n'a pas termine dans le delai.");
            return new ProcessResult(process.ExitCode, output, error);
        }
    }

    private static void AssertGitSuccess(string repositoryRoot, params string[] arguments)
    {
        string gitArguments = "-C " + QuoteArgument(repositoryRoot);
        foreach (string argument in arguments)
        {
            gitArguments += " " + QuoteArgument(argument);
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitArguments,
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.IsNotNull(process, "Impossible de lancer Git.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(30000), "Git n'a pas termine dans le delai.");
            Assert.AreEqual(0, process.ExitCode, output + Environment.NewLine + error);
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "DonJPackagingTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    private static string GetPackageScriptPath()
    {
        return Path.Combine(GetRepositoryRoot(), "tools", "package-game-ready.ps1");
    }

    private static string GetDeployScriptPath()
    {
        return Path.Combine(GetRepositoryRoot(), "tools", "deploy-game-ready.ps1");
    }

    private static string GetAbiContractPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tools",
            "NibAbiValidator",
            "contracts",
            "NIBScriptHookVDotNet2-2.11.6.abi.xml");
    }

    private static string GetReleaseBuildDirectory()
    {
        return Path.Combine(GetRepositoryRoot(), "src", "DonJEnemySpawner", "bin", "Release");
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GTA5modDEV.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Racine GTA5modDEV introuvable.");
    }

    private sealed class ProcessResult
    {
        internal ProcessResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
        }

        internal int ExitCode { get; private set; }
        internal string Output { get; private set; }
        internal string Error { get; private set; }
        internal string CombinedOutput
        {
            get { return Output + Environment.NewLine + Error; }
        }
    }

    [DataContract]
    private sealed class PackageManifest
    {
        [DataMember(Name = "manifestVersion")]
        internal int ManifestVersion { get; set; }

        [DataMember(Name = "product")]
        internal string Product { get; set; }

        [DataMember(Name = "configuration")]
        internal string Configuration { get; set; }

        [DataMember(Name = "generatedAtUtc")]
        internal string GeneratedAtUtc { get; set; }

        [DataMember(Name = "commit")]
        internal string Commit { get; set; }

        [DataMember(Name = "assemblyVersion")]
        internal string AssemblyVersion { get; set; }

        [DataMember(Name = "informationalVersion")]
        internal string InformationalVersion { get; set; }

        [DataMember(Name = "justiceSchemaVersion")]
        internal int JusticeSchemaVersion { get; set; }

        [DataMember(Name = "scriptApi")]
        internal PackageScriptApi ScriptApi { get; set; }

        [DataMember(Name = "sourceDirty")]
        internal bool SourceDirty { get; set; }

        [DataMember(Name = "expectedTypes")]
        internal string[] ExpectedTypes { get; set; }

        [DataMember(Name = "files")]
        internal PackageFiles Files { get; set; }
    }

    [DataContract]
    private sealed class PackageScriptApi
    {
        [DataMember(Name = "name")]
        internal string Name { get; set; }

        [DataMember(Name = "version")]
        internal string Version { get; set; }

        [DataMember(Name = "major")]
        internal int Major { get; set; }

        [DataMember(Name = "abiContract")]
        internal PackageAbiContract AbiContract { get; set; }
    }

    [DataContract]
    private sealed class PackageAbiContract
    {
        [DataMember(Name = "id")]
        internal string Id { get; set; }

        [DataMember(Name = "version")]
        internal string Version { get; set; }

        [DataMember(Name = "sha256")]
        internal string Sha256 { get; set; }
    }

    [DataContract]
    private sealed class PackageFiles
    {
        [DataMember(Name = "binary")]
        internal PackageFile Binary { get; set; }

        [DataMember(Name = "symbols")]
        internal PackageFile Symbols { get; set; }

        [DataMember(Name = "installationGuide")]
        internal PackageFile InstallationGuide { get; set; }
    }

    [DataContract]
    private sealed class PackageFile
    {
        [DataMember(Name = "name")]
        internal string Name { get; set; }

        [DataMember(Name = "sizeBytes")]
        internal long SizeBytes { get; set; }

        [DataMember(Name = "sha256")]
        internal string Sha256 { get; set; }
    }
}
