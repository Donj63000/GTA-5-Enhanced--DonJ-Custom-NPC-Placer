using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class JusticeStateRepairTests
{
    [TestMethod]
    public void RepairJusticeState_AnnuleLeDossierEtPreserveLeCasier()
    {
        string root = CreateTemporaryState(false);
        try
        {
            string statePath = Path.Combine(root, "_justice_state.xml");
            XmlDocument before = LoadXml(statePath);
            string recordBefore = before.DocumentElement.SelectSingleNode("Record").OuterXml;

            RunRepair(statePath, true);

            XmlDocument after = LoadXml(statePath);
            XmlElement documentRoot = after.DocumentElement;
            XmlElement caseElement = (XmlElement)documentRoot.SelectSingleNode("Case");
            XmlElement recordElement = (XmlElement)documentRoot.SelectSingleNode("Record");
            XmlElement custodyElement = (XmlElement)documentRoot.SelectSingleNode("Custody");
            Assert.AreEqual("true", documentRoot.GetAttribute("enabled"));
            Assert.AreEqual("true", documentRoot.GetAttribute("pendingAmnestyWantedClear"));
            Assert.AreEqual("AtLarge", caseElement.GetAttribute("phase"));
            Assert.AreEqual("0", caseElement.GetAttribute("activeScore"));
            Assert.AreEqual("0", caseElement.GetAttribute("sentenceSeconds"));
            Assert.AreEqual("false", custodyElement.GetAttribute("active"));
            Assert.AreEqual("None", custodyElement.GetAttribute("site"));
            Assert.AreEqual(recordBefore, recordElement.OuterXml);
            Assert.AreEqual(
                HashFile(statePath),
                HashFile(statePath + ".bak"),
                "Le primaire et le backup réparés doivent être identiques.");
            Assert.IsTrue(
                Directory.Exists(Path.Combine(root, "_justice_recovery_backups")),
                "La réparation doit conserver une sauvegarde horodatée indépendante.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RepairJusticeState_RefuseUnInventaireDejaRetire()
    {
        string root = CreateTemporaryState(true);
        try
        {
            string statePath = Path.Combine(root, "_justice_state.xml");
            string hashBefore = HashFile(statePath);

            RunRepair(statePath, false);

            Assert.AreEqual(hashBefore, HashFile(statePath));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "_justice_recovery_backups")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryState(bool inventoryRemoved)
    {
        string root = Path.Combine(Path.GetTempPath(), "DonJJusticeRepair_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string statePath = Path.Combine(root, "_justice_state.xml");
        string removed = inventoryRemoved ? "true" : "false";
        string snapshot = inventoryRemoved ? "<InventorySnapshot selectedWeapon='0' />" : string.Empty;
        string xml =
            "<JusticeState version='1' enabled='true' nextIdentityGeneration='8' " +
            "pendingDeathCapture='false' pendingDeathCapturePlayerSlot='-1' pendingDeathCapturePlayerModel='0'>" +
            "<Case enabled='true' activeScore='75' fineDue='1000' sentenceSeconds='420' " +
            "hasWarrant='false' phase='Transporting' wantedEpisodeId='wanted:test' " +
            "custodyEpisodeId='custody:test' lastCrimeKind='SimpleAssault' " +
            "lastCrimeLabel='Agression' fleeingCharged='false' escapeCharged='false'>" +
            "<Charges/><FleeingEpisodes/><EscapeEpisodes/><ProcessedIncidents/><CompletedOperations/>" +
            "</Case>" +
            "<Record recidivism='77' cleanGameplaySeconds='12' appliedCleanDecay='3'>" +
            "<Convictions><Conviction id='conviction:keep' /></Convictions>" +
            "<AppliedConvictions><Id value='conviction:keep' /></AppliedConvictions>" +
            "</Record>" +
            "<Custody active='true' site='MissionRow' initialSentenceSeconds='420' " +
            "activityReductionSeconds='0' inventoryRemoved='" + removed + "' " +
            "weaponControlsLocked='false' deferredInventoryRestore='false' waitingForRespawn='false' " +
            "deathRebindPending='false' playerStateStored='true' storedInvincible='false' " +
            "storedFrozen='false' storedCanRagdoll='true' playerModelHash='1234' playerSlot='1' " +
            "releaseSelectedWeapon='-1569615261'>" + snapshot + "</Custody>" +
            "</JusticeState>";
        File.WriteAllText(statePath, xml, new UTF8Encoding(false));
        File.Copy(statePath, statePath + ".bak");
        return root;
    }

    private static void RunRepair(string statePath, bool expectSuccess)
    {
        string scriptPath = Path.Combine(GetRepositoryRoot(), "tools", "repair-justice-state.ps1");
        string command =
            "$ErrorActionPreference='Stop'; & '" + EscapePowerShellLiteral(scriptPath) +
            "' -StatePath '" + EscapePowerShellLiteral(statePath) + "' -Confirm:$false";
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (Process process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (expectSuccess)
            {
                Assert.AreEqual(0, process.ExitCode, output + Environment.NewLine + error);
            }
            else
            {
                Assert.AreNotEqual(0, process.ExitCode, "La réparation dangereuse aurait dû être refusée.");
                StringAssert.Contains(output + error, "JUSTICE_REPAIR_UNSAFE_INVENTORY");
            }
        }
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    private static XmlDocument LoadXml(string path)
    {
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        return document;
    }

    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GTA5modDEV.sln")))
        {
            directory = directory.Parent;
        }
        Assert.IsNotNull(directory, "Racine du dépôt introuvable.");
        return directory.FullName;
    }
}
