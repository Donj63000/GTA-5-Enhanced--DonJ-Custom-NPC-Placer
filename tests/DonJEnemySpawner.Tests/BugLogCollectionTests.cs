using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class BugLogCollectionTests
{
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [TestMethod]
    public void GitIgnore_KeepsBugReportsLocalOnly()
    {
        string gitIgnore = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".gitignore"));

        StringAssert.Contains(gitIgnore.Replace('\\', '/'), "bug-reports/");
    }

    [TestMethod]
    public void CollectorScript_KnowsGtaRootsAndPriorityLogs()
    {
        string script = File.ReadAllText(GetCollectorScriptPath());

        StringAssert.Contains(script, "Grand Theft Auto V Enhanced");
        StringAssert.Contains(script, "Grand Theft Auto V");
        StringAssert.Contains(script, "DefaultEnhancedGtaRoot");
        StringAssert.Contains(script, "Scripts");
        StringAssert.Contains(script, "*.log");
        StringAssert.Contains(script, "NIBScriptHookVDotNet.log");
        StringAssert.Contains(script, "ScriptHookV.log");
        StringAssert.Contains(script, "asiloader.log");
        StringAssert.Contains(script, "DirectStorageFix.log");
        StringAssert.Contains(script, "menyooLog.txt");
        StringAssert.Contains(script, "MapEditor.log");
        StringAssert.Contains(script, "DonJJusticeRecognition\\JusticeRecognition.log");
        StringAssert.Contains(script, "Get-WinEvent");
        StringAssert.Contains(script, "GTA5_Enhanced");
        StringAssert.Contains(script, ".NET Runtime");
        StringAssert.Contains(script, "Application Error");
    }

    [TestMethod]
    public void CollectorScript_GeneratesStructuredBugReportFromFakeGtaRoot()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "DonJBugCollector_" + Guid.NewGuid().ToString("N"));
        string fakeGtaRoot = Path.Combine(tempRoot, "Grand Theft Auto V Enhanced");
        string scriptsRoot = Path.Combine(fakeGtaRoot, "Scripts");
        string title = "unit-test-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(scriptsRoot);
            File.WriteAllText(Path.Combine(fakeGtaRoot, "GTA5_Enhanced.exe"), string.Empty);
            File.WriteAllText(Path.Combine(fakeGtaRoot, "NIBScriptHookVDotNet.log"), "NIB log test");
            File.WriteAllText(Path.Combine(fakeGtaRoot, "ScriptHookV.log"), "ScriptHookV log test");
            File.WriteAllText(Path.Combine(scriptsRoot, "DonJCustomNpcPlacer.log"), "runtime mod log test");
            string recognitionRoot = Path.Combine(scriptsRoot, "DonJJusticeRecognition");
            Directory.CreateDirectory(recognitionRoot);
            File.WriteAllText(Path.Combine(recognitionRoot, "JusticeRecognition.log"), "recognition log test");

            RunCollector(title, fakeGtaRoot);

            string reportRoot = FindNewestReport(title);

            Assert.IsTrue(File.Exists(Path.Combine(reportRoot, "summary.md")), "summary.md doit etre genere.");
            Assert.IsTrue(File.Exists(Path.Combine(reportRoot, "manifest.json")), "manifest.json doit etre genere.");
            Assert.IsTrue(File.Exists(Path.Combine(reportRoot, "repo-state.txt")), "repo-state.txt doit etre genere.");
            Assert.IsTrue(File.Exists(Path.Combine(reportRoot, "crash-list-entry.md")), "crash-list-entry.md doit etre genere.");
            Assert.IsTrue(Directory.Exists(Path.Combine(reportRoot, "raw-logs")), "raw-logs doit etre genere.");
            Assert.IsTrue(Directory.Exists(Path.Combine(reportRoot, "windows-events")), "windows-events doit etre genere.");

            string manifest = File.ReadAllText(Path.Combine(reportRoot, "manifest.json"));
            StringAssert.Contains(manifest, "NIBScriptHookVDotNet.log");
            StringAssert.Contains(manifest, "ScriptHookV.log");
            StringAssert.Contains(manifest, "DonJCustomNpcPlacer.log");
            StringAssert.Contains(manifest, "JusticeRecognition.log");

            string[] copiedLogs = Directory.GetFiles(Path.Combine(reportRoot, "raw-logs"), "*", SearchOption.TopDirectoryOnly);
            Assert.IsTrue(copiedLogs.Any(path => Path.GetFileName(path).Contains("NIBScriptHookVDotNet.log")));
            Assert.IsTrue(copiedLogs.Any(path => Path.GetFileName(path).Contains("DonJCustomNpcPlacer.log")));
            string[] recognitionCopies = copiedLogs
                .Where(path => Path.GetFileName(path).EndsWith("JusticeRecognition.log", StringComparison.Ordinal))
                .ToArray();
            Assert.IsTrue(recognitionCopies.Length >= 1, "Le journal Recognition imbrique doit etre collecte.");
            Assert.IsTrue(
                recognitionCopies.All(path => Path.GetFileName(path).Length <= 96),
                "Chaque nom collecte doit rester sous la limite sure.");

            // Je sélectionne la source injectée par le test : une installation GTA
            // réelle peut légitimement fournir un second journal du même module.
            string recognitionCopy = recognitionCopies.Single(
                path => File.ReadAllText(path) == "recognition log test");
            Assert.AreEqual("recognition log test", File.ReadAllText(recognitionCopy), "Le journal imbrique ne doit pas etre ecrase.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [TestMethod]
    public void RuntimeLogger_SanitizesNamesAndFallsBackToWritableDirectory()
    {
        string unsafeName = @"..\bad:name?.txt";
        string safeName = (string)InvokeStatic("SanitizeRuntimeLogFileName", unsafeName);

        Assert.IsFalse(safeName.Contains(".."));
        Assert.IsTrue(safeName.EndsWith(".log", StringComparison.OrdinalIgnoreCase));

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            Assert.IsFalse(safeName.Contains(invalid.ToString()), "Le nom de log contient encore un caractere interdit.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "DonJRuntimeLogger_" + Guid.NewGuid().ToString("N"));
        string blockedCandidate = Path.Combine(tempRoot, "not-a-directory");
        string writableCandidate = Path.Combine(tempRoot, "logs");

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(blockedCandidate, "ce chemin est un fichier, pas un dossier");

            bool written = (bool)InvokeStatic(
                "TryWriteRuntimeLogEntry",
                new object[]
                {
                    new[] { blockedCandidate, writableCandidate },
                    unsafeName,
                    "ERROR",
                    "TestLogger",
                    "message test",
                    new InvalidOperationException("boom")
                });

            Assert.IsTrue(written, "Le logger doit retomber sur le dossier accessible.");

            string[] logFiles = Directory.GetFiles(writableCandidate, "*.log", SearchOption.TopDirectoryOnly);
            Assert.AreEqual(1, logFiles.Length);

            string log = File.ReadAllText(logFiles[0]);
            StringAssert.Contains(log, "[ERROR]");
            StringAssert.Contains(log, "TestLogger");
            StringAssert.Contains(log, "InvalidOperationException");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [TestMethod]
    public void SafetyScript_CollectsBugLogsOnlyAfterFailure()
    {
        string script = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tools", "run-safety-checks.ps1"));

        StringAssert.Contains(script, "Invoke-SafetyFailureCollection");
        StringAssert.Contains(script, "collect-bug-logs.ps1");
        StringAssert.Contains(script, "safety-failure");
        StringAssert.Contains(script, "trap");
        StringAssert.Contains(script, "throw $_");
    }

    private static void RunCollector(string title, string gtaRoot)
    {
        string arguments =
            "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(GetCollectorScriptPath()) +
            " -Title " + QuoteArgument(title) +
            " -SinceHours 1 -GtaRoot " + QuoteArgument(gtaRoot);

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            Assert.IsNotNull(process, "Impossible de lancer PowerShell pour le collecteur.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            Assert.IsTrue(process.WaitForExit(120000), "Le collecteur de logs n'a pas termine dans le delai.");

            if (process.ExitCode != 0)
            {
                Assert.Fail("Le collecteur a echoue avec le code " + process.ExitCode + Environment.NewLine + output + Environment.NewLine + error);
            }
        }
    }

    private static string FindNewestReport(string title)
    {
        string reportsRoot = Path.Combine(GetRepositoryRoot(), "bug-reports");
        Assert.IsTrue(Directory.Exists(reportsRoot), "Le dossier bug-reports doit exister apres la collecte.");

        DirectoryInfo report = new DirectoryInfo(reportsRoot)
            .GetDirectories("*-" + title, SearchOption.TopDirectoryOnly)
            .OrderByDescending(directory => directory.Name)
            .FirstOrDefault();

        Assert.IsNotNull(report, "Le rapport de test est introuvable.");
        return report.FullName;
    }

    private static object InvokeStatic(string methodName, params object[] args)
    {
        MethodInfo method = ScriptType.GetMethod(methodName, PrivateStatic);
        Assert.IsNotNull(method, $"La methode privee statique '{methodName}' est introuvable.");
        return method.Invoke(null, args);
    }

    private static string GetCollectorScriptPath()
    {
        return Path.Combine(GetRepositoryRoot(), "tools", "collect-bug-logs.ps1");
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        // Je pars du fichier source pour que ce test reste valable lorsque MSBuild
        // place tous les binaires dans un ArtifactsPath extérieur au dépôt.
        string sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        DirectoryInfo directory = new DirectoryInfo(
            string.IsNullOrWhiteSpace(sourceDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : sourceDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "GTA5modDEV.sln");

            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du depot depuis le dossier de test.");
        return string.Empty;
    }
}
