using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

public sealed partial class DonJEnemySpawner
{
    [DataContract]
    private sealed class JusticeDiagnosticManifest
    {
        [DataMember(Name = "manifestVersion")]
        internal int ManifestVersion { get; set; }

        [DataMember(Name = "product")]
        internal string Product { get; set; }

        [DataMember(Name = "commit")]
        internal string Commit { get; set; }

        [DataMember(Name = "sourceDirty")]
        internal bool SourceDirty { get; set; }

        [DataMember(Name = "informationalVersion")]
        internal string InformationalVersion { get; set; }

        [DataMember(Name = "justiceSchemaVersion")]
        internal int JusticeSchemaVersion { get; set; }

        [DataMember(Name = "scriptApi")]
        internal JusticeDiagnosticScriptApi ScriptApi { get; set; }

        [DataMember(Name = "files")]
        internal JusticeDiagnosticManifestFiles Files { get; set; }
    }

    [DataContract]
    private sealed class JusticeDiagnosticScriptApi
    {
        [DataMember(Name = "major")]
        internal int Major { get; set; }

        [DataMember(Name = "abiContract")]
        internal JusticeDiagnosticAbiContract AbiContract { get; set; }
    }

    [DataContract]
    private sealed class JusticeDiagnosticAbiContract
    {
        [DataMember(Name = "id")]
        internal string Id { get; set; }

        [DataMember(Name = "version")]
        internal string Version { get; set; }

        [DataMember(Name = "sha256")]
        internal string Sha256 { get; set; }
    }

    [DataContract]
    private sealed class JusticeDiagnosticManifestFiles
    {
        [DataMember(Name = "binary")]
        internal JusticeDiagnosticManifestBinary Binary { get; set; }
    }

    [DataContract]
    private sealed class JusticeDiagnosticManifestBinary
    {
        [DataMember(Name = "name")]
        internal string Name { get; set; }

        [DataMember(Name = "sha256")]
        internal string Sha256 { get; set; }
    }

    private string _justiceDiagnosticAssemblySha256 = string.Empty;
    private string _justiceDiagnosticManifestSha256 = string.Empty;
    private bool _justiceDiagnosticManifestMatches;

    private string GetJusticeDiagnosticMenuDisplay()
    {
        JusticeRepositoryDiagnostics repository = _justiceRepository == null
            ? null
            : _justiceRepository.GetDiagnostics();
        JusticeWalDiagnostics wal = _justiceWriteAheadLog == null
            ? null
            : _justiceWriteAheadLog.GetDiagnostics();
        string revision = repository == null
            ? "repo indisponible"
            : "rev " + repository.MemoryRevision.ToString(CultureInfo.InvariantCulture) +
              "/" + repository.DiskRevision.ToString(CultureInfo.InvariantCulture);
        int openWal = wal == null ? 0 : wal.OpenTransactionCount;
        return GetJusticeBuildId() + " · " + revision + " · WAL " +
               openWal.ToString(CultureInfo.InvariantCulture);
    }

    private void ShowJusticeDiagnosticStatus()
    {
        try
        {
            string assemblyPath = typeof(DonJEnemySpawner).Assembly.Location;
            _justiceDiagnosticAssemblySha256 = ComputeJusticeFileSha256Hex(assemblyPath);
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(assemblyPath) ?? string.Empty,
                "DonJCustomNpcPlacer.manifest.json");
            _justiceDiagnosticManifestSha256 = ReadJusticeManifestSha256(manifestPath);
            _justiceDiagnosticManifestMatches =
                _justiceDiagnosticManifestSha256.Length == 64 &&
                string.Equals(
                    _justiceDiagnosticAssemblySha256,
                    _justiceDiagnosticManifestSha256,
                    StringComparison.OrdinalIgnoreCase);

            JusticeRepositoryDiagnostics repository = _justiceRepository == null
                ? null
                : _justiceRepository.GetDiagnostics();
            JusticeWalDiagnostics wal = _justiceWriteAheadLog == null
                ? null
                : _justiceWriteAheadLog.GetDiagnostics();
            string diagnostic = BuildJusticeDiagnosticReport(repository, wal);
            LogInfo("Justice.Diagnostic", diagnostic);
            ShowStatus(
                "Justice SHA-256 " + _justiceDiagnosticAssemblySha256 +
                (_justiceDiagnosticManifestSha256.Length == 64
                    ? (_justiceDiagnosticManifestMatches
                        ? " · manifest OK"
                        : " · MANIFEST DIFFÉRENT")
                    : " · manifest absent"),
                10000);
        }
        catch (Exception exception)
        {
            LogException("Justice.Diagnostic.Build", exception);
            ShowStatus("Justice : diagnostic du build impossible, voir le log.", 5000);
        }
    }

    private string BuildJusticeDiagnosticReport(
        JusticeRepositoryDiagnostics repository,
        JusticeWalDiagnostics wal)
    {
        StringBuilder report = new StringBuilder(768);
        report.Append("buildId=").Append(GetJusticeBuildId());
        report.Append("; assemblySha256=").Append(_justiceDiagnosticAssemblySha256);
        report.Append("; manifestSha256=").Append(_justiceDiagnosticManifestSha256);
        report.Append("; manifestMatch=").Append(_justiceDiagnosticManifestMatches ? "oui" : "non");
        report.Append("; schema=").Append(JusticeXmlPersistenceCodec.SchemaMajor);
        report.Append("; phase=").Append(_justiceCaseState == null ? "Aucune" : _justiceCaseState.Phase.ToString());
        report.Append("; slot=").Append(_justiceActivePlayerProfileSlot);
        report.Append("; inventaire=").Append(_justiceInventoryCustodyState);
        report.Append("; paiement=").Append(GetJusticePaymentDiagnosticState());
        report.Append("; police=").Append(_justicePoliceIntegrationMode);
        report.Append("; WAL ouverts=").Append(wal == null ? -1 : wal.OpenTransactionCount);
        report.Append("; rev mémoire=").Append(repository == null ? -1L : repository.MemoryRevision);
        report.Append("; rev disque=").Append(repository == null ? -1L : repository.DiskRevision);
        report.Append("; dernière sauvegarde UTC=").Append(
            _justiceLastPersistenceCompletedAtUtcTicks <= 0L
                ? "jamais"
                : new DateTime(
                    _justiceLastPersistenceCompletedAtUtcTicks,
                    DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        AppendJusticeMetricReport(report, "persistance", _justicePersistenceMetrics);
        AppendJusticeMetricReport(report, "détection", _justiceCrimeDetectionMetrics);
        AppendJusticeMetricReport(report, "incidents", _justiceIncidentProcessingMetrics);
        report.Append("; scans peds=").Append(_justiceWorldPedQueries);
        report.Append("; scans véhicules=").Append(_justiceWorldVehicleQueries);
        report.Append("; entités dernier snapshot=").Append(_justiceLastWorldEntityCount);
        report.Append("; incidents en attente=").Append(_justicePendingIncidents.Count);
        if (!string.IsNullOrWhiteSpace(_justicePersistenceLastError))
        {
            report.Append("; dernière erreur=").Append(_justicePersistenceLastError);
        }
        return report.ToString();
    }

    private static void AppendJusticeMetricReport(
        StringBuilder report,
        string name,
        JusticeMetricAccumulator metric)
    {
        report.Append("; ").Append(name).Append(" moyenne ms=").Append(
            metric.AverageMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        report.Append("; ").Append(name).Append(" p95 ms=").Append(
            metric.P95Milliseconds.ToString("F3", CultureInfo.InvariantCulture));
        report.Append("; ").Append(name).Append(" p99 ms=").Append(
            metric.P99Milliseconds.ToString("F3", CultureInfo.InvariantCulture));
        report.Append("; ").Append(name).Append(" max ms=").Append(
            metric.MaximumMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
    }

    private string GetJusticePaymentDiagnosticState()
    {
        if (_justiceVoluntaryFinePaymentIntent != null)
        {
            return "volontaire/" + _justiceVoluntaryFinePaymentIntent.Resolution;
        }
        if (_justiceFineDebitIntent != null)
        {
            return "jugement/" + _justiceFineDebitIntent.Resolution;
        }
        if (_justiceCaseState != null && _justiceCaseState.FineInDispute > 0L)
        {
            return "Ambiguous/" +
                   _justiceCaseState.FineInDispute.ToString(CultureInfo.InvariantCulture);
        }
        return "aucun";
    }

    private static string GetJusticeBuildId()
    {
        Assembly assembly = typeof(DonJEnemySpawner).Assembly;
        AssemblyInformationalVersionAttribute informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (informational != null &&
            !string.IsNullOrWhiteSpace(informational.InformationalVersion))
        {
            return informational.InformationalVersion.Trim();
        }
        Version version = assembly.GetName().Version;
        return version == null ? "build inconnu" : version.ToString();
    }

    private static string ReadJusticeManifestSha256(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(
            typeof(JusticeDiagnosticManifest));
        JusticeDiagnosticManifest manifest;
        using (FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            manifest = serializer.ReadObject(stream) as JusticeDiagnosticManifest;
        }

        JusticeDiagnosticManifestBinary binary = manifest == null || manifest.Files == null
            ? null
            : manifest.Files.Binary;
        string hash = binary == null ? string.Empty : (binary.Sha256 ?? string.Empty).Trim();
        string commit = manifest == null
            ? string.Empty
            : (manifest.Commit ?? string.Empty).Trim();
        string informationalVersion = manifest == null
            ? string.Empty
            : (manifest.InformationalVersion ?? string.Empty).Trim();
        JusticeDiagnosticAbiContract abiContract =
            manifest == null || manifest.ScriptApi == null
                ? null
                : manifest.ScriptApi.AbiContract;
        string abiContractHash = abiContract == null
            ? string.Empty
            : (abiContract.Sha256 ?? string.Empty).Trim();
        if (manifest == null || manifest.ManifestVersion != 2 ||
            !string.Equals(
                manifest.Product,
                "DonJCustomNpcPlacer",
                StringComparison.Ordinal) ||
            manifest.SourceDirty ||
            manifest.JusticeSchemaVersion != JusticeXmlPersistenceCodec.SchemaMajor ||
            manifest.ScriptApi == null ||
            manifest.ScriptApi.Major != 2 ||
            abiContract == null ||
            string.IsNullOrWhiteSpace(abiContract.Id) ||
            string.IsNullOrWhiteSpace(abiContract.Version) ||
            abiContractHash.Length != 64 ||
            !string.Equals(
                informationalVersion,
                GetJusticeBuildId(),
                StringComparison.Ordinal) ||
            commit.Length != 40 ||
            informationalVersion.IndexOf(
                commit,
                StringComparison.OrdinalIgnoreCase) < 0 ||
            binary == null ||
            !string.Equals(
                binary.Name,
                "DonJCustomNpcPlacer.ENdll",
                StringComparison.Ordinal) ||
            hash.Length != 64)
        {
            return string.Empty;
        }
        for (int index = 0; index < commit.Length; index++)
        {
            char value = commit[index];
            if (!((value >= '0' && value <= '9') ||
                  (value >= 'a' && value <= 'f') ||
                  (value >= 'A' && value <= 'F')))
            {
                return string.Empty;
            }
        }
        for (int index = 0; index < hash.Length; index++)
        {
            char value = hash[index];
            if (!((value >= '0' && value <= '9') ||
                  (value >= 'a' && value <= 'f') ||
                  (value >= 'A' && value <= 'F')))
            {
                return string.Empty;
            }
        }
        for (int index = 0; index < abiContractHash.Length; index++)
        {
            char value = abiContractHash[index];
            if (!((value >= '0' && value <= '9') ||
                  (value >= 'a' && value <= 'f') ||
                  (value >= 'A' && value <= 'F')))
            {
                return string.Empty;
            }
        }
        return hash.ToLowerInvariant();
    }
}
