using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public sealed partial class DonJEnemySpawner
{
    private const string RuntimeLogFileName = "DonJCustomNpcPlacer.log";
    private static readonly object RuntimeLogLock = new object();

    private static void LogInfo(string context, string message)
    {
        WriteRuntimeLog("INFO", context, message, null);
    }

    private static void LogWarning(string context, string message)
    {
        WriteRuntimeLog("WARN", context, message, null);
    }

    private static void LogException(string context, Exception exception)
    {
        WriteRuntimeLog("ERROR", context, exception == null ? "Exception inconnue." : exception.Message, exception);
    }

    private static void WriteRuntimeLog(string level, string context, string message, Exception exception)
    {
        try
        {
            TryWriteRuntimeLogEntry(
                BuildRuntimeLogDirectoryCandidates(),
                RuntimeLogFileName,
                level,
                context,
                message,
                exception);
        }
        catch
        {
            // Le log ne doit jamais casser le mod, meme si Windows refuse le dossier Scripts.
        }
    }

    private static bool TryWriteRuntimeLogEntry(
        IEnumerable<string> directoryCandidates,
        string fileName,
        string level,
        string context,
        string message,
        Exception exception)
    {
        if (directoryCandidates == null)
        {
            return false;
        }

        string safeFileName = SanitizeRuntimeLogFileName(fileName);
        string entry = FormatRuntimeLogEntry(level, context, message, exception);

        lock (RuntimeLogLock)
        {
            foreach (string candidate in directoryCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                    string logPath = Path.Combine(candidate, safeFileName);
                    File.AppendAllText(logPath, entry, Encoding.UTF8);
                    return true;
                }
                catch
                {
                    // Je tente le dossier suivant, le log reste optionnel.
                }
            }
        }

        return false;
    }

    private static List<string> BuildRuntimeLogDirectoryCandidates()
    {
        List<string> candidates = new List<string>();

        // Je privilégie les emplacements stables avant le répertoire d'assembly,
        // car NIB peut charger le script depuis une copie temporaire difficile à retrouver.
        AddUniqueDirectory(candidates, GetProcessScriptsDirectorySafe());
        if (LooksLikeGtaRoot(DefaultEnhancedGtaRoot))
        {
            AddUniqueDirectory(candidates, Path.Combine(DefaultEnhancedGtaRoot, "Scripts"));
        }
        AddUniqueDirectory(candidates, GetConfiguredSaveDirectorySafe());
        AddUniqueDirectory(candidates, GetLocalAppDataRuntimeLogDirectorySafe());
        AddUniqueDirectory(candidates, GetLocalAppDataSaveDirectorySafe());
        AddUniqueDirectory(candidates, GetAssemblyDirectorySafe());
        AddUniqueDirectory(candidates, AppDomain.CurrentDomain.BaseDirectory);

        return candidates;
    }

    private static string GetLocalAppDataRuntimeLogDirectorySafe()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return string.Empty;
            }

            return Path.Combine(localAppData, "DonJEnemySpawner", "Logs");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SanitizeRuntimeLogFileName(string fileName)
    {
        string safe = string.IsNullOrWhiteSpace(fileName) ? RuntimeLogFileName : fileName.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '-');
        }

        safe = safe.Replace("..", "-").Trim(' ', '.', '-');

        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = RuntimeLogFileName;
        }

        if (!safe.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            safe += ".log";
        }

        if (safe.Length <= 96)
        {
            return safe;
        }

        string extension = ".log";
        return safe.Substring(0, 96 - extension.Length).Trim(' ', '.', '-') + extension;
    }

    private static string FormatRuntimeLogEntry(string level, string context, string message, Exception exception)
    {
        StringBuilder builder = new StringBuilder();
        string safeLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
        string safeContext = string.IsNullOrWhiteSpace(context) ? "General" : context.Trim();
        string safeMessage = string.IsNullOrWhiteSpace(message) ? "(aucun message)" : message.Trim();

        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(safeLevel);
        builder.Append("] ");
        builder.Append(safeContext);
        builder.Append(" - ");
        builder.AppendLine(safeMessage);

        if (exception != null)
        {
            builder.AppendLine(exception.ToString());
        }

        return builder.ToString();
    }
}
