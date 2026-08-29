using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DonJ.NibAbiValidator
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitIncompatible = 2;
        private const int ExitUsage = 64;
        private const int ExitSoftware = 70;

        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length == 0)
                {
                    WriteUsage();
                    return ExitUsage;
                }

                string command = args[0].Trim().ToLowerInvariant();
                Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());
                switch (command)
                {
                    case "verify":
                        return RunVerify(options);
                    case "capture":
                        return RunCapture(options);
                    case "info":
                        return RunInfo(options);
                    case "help":
                    case "--help":
                    case "-h":
                        WriteUsage();
                        return ExitSuccess;
                    default:
                        Console.Error.WriteLine("Commande inconnue : " + command + ".");
                        WriteUsage();
                        return ExitUsage;
                }
            }
            catch (UsageException exception)
            {
                Console.Error.WriteLine(exception.Message);
                WriteUsage();
                return ExitUsage;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Echec du verificateur ABI : " + exception.Message);
                return ExitSoftware;
            }
        }

        private static int RunVerify(IReadOnlyDictionary<string, string> options)
        {
            RejectUnknownOptions(options, "consumer", "contract", "runtime-api");
            string consumer = RequireOption(options, "consumer");
            string contractPath = RequireOption(options, "contract");
            options.TryGetValue("runtime-api", out string runtimeApi);

            AbiContract contract = AbiContract.Load(contractPath);
            AbiValidationResult result = AbiValidator.Verify(consumer, contractPath, runtimeApi);
            if (!result.IsValid)
            {
                foreach (string error in result.Errors)
                {
                    Console.Error.WriteLine(error);
                }

                return ExitIncompatible;
            }

            Console.Out.WriteLine(
                "{\"valid\":true,\"contractId\":" + Json(contract.ContractId) +
                ",\"contractVersion\":" + Json(contract.ContractVersion) +
                ",\"checkedTypeReferences\":" +
                result.CheckedTypeReferences.ToString(CultureInfo.InvariantCulture) +
                ",\"checkedMemberReferences\":" +
                result.CheckedMemberReferences.ToString(CultureInfo.InvariantCulture) +
                ",\"runtimeValidated\":" + (result.RuntimeValidated ? "true" : "false") + "}");
            return ExitSuccess;
        }

        private static int RunCapture(IReadOnlyDictionary<string, string> options)
        {
            RejectUnknownOptions(options, "consumer", "runtime-api", "output", "id", "version", "force");
            string consumer = RequireOption(options, "consumer");
            string runtimeApi = RequireOption(options, "runtime-api");
            string output = Path.GetFullPath(RequireOption(options, "output"));
            string id = RequireOption(options, "id");
            string version = RequireOption(options, "version");
            bool force = ReadBooleanFlag(options, "force");

            if (File.Exists(output) && !force)
            {
                throw new UsageException(
                    "Le contrat existe deja. Utilise --force uniquement apres une revue intentionnelle de l'API live.");
            }

            AbiContract contract = AbiValidator.Capture(consumer, runtimeApi, id, version);
            contract.Save(output);
            WriteContractInfo(contract, output);
            return ExitSuccess;
        }

        private static int RunInfo(IReadOnlyDictionary<string, string> options)
        {
            RejectUnknownOptions(options, "contract");
            string contractPath = RequireOption(options, "contract");
            AbiContract contract = AbiContract.Load(contractPath);
            WriteContractInfo(contract, contractPath);
            return ExitSuccess;
        }

        private static void WriteContractInfo(AbiContract contract, string path)
        {
            Console.Out.WriteLine(
                "{\"contractId\":" + Json(contract.ContractId) +
                ",\"contractVersion\":" + Json(contract.ContractVersion) +
                ",\"sha256\":" + Json(AbiContract.ComputeSha256(path)) + "}");
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string token = args[index];
                if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException("Option attendue, recue : " + token + ".");
                }

                string name = token.Substring(2);
                if (string.IsNullOrWhiteSpace(name) || options.ContainsKey(name))
                {
                    throw new UsageException("Option invalide ou dupliquee : " + token + ".");
                }

                if (string.Equals(name, "force", StringComparison.OrdinalIgnoreCase))
                {
                    options.Add(name, "true");
                    continue;
                }

                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException("Valeur manquante pour " + token + ".");
                }

                options.Add(name, args[++index]);
            }

            return options;
        }

        private static string RequireOption(IReadOnlyDictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string value) || string.IsNullOrWhiteSpace(value))
            {
                throw new UsageException("L'option --" + name + " est obligatoire.");
            }

            return value;
        }

        private static bool ReadBooleanFlag(IReadOnlyDictionary<string, string> options, string name)
        {
            return options.TryGetValue(name, out string value) &&
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void RejectUnknownOptions(
            IReadOnlyDictionary<string, string> options,
            params string[] accepted)
        {
            HashSet<string> acceptedSet = new HashSet<string>(accepted, StringComparer.OrdinalIgnoreCase);
            string unknown = options.Keys.FirstOrDefault(option => !acceptedSet.Contains(option));
            if (unknown != null)
            {
                throw new UsageException("Option non prise en charge : --" + unknown + ".");
            }
        }

        private static string Json(string value)
        {
            if (value == null)
            {
                return "null";
            }

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static void WriteUsage()
        {
            Console.Error.WriteLine("Usage :");
            Console.Error.WriteLine(
                "  DonJ.NibAbiValidator.exe verify --consumer <ENdll> --contract <abi.xml> [--runtime-api <NIB.dll>]");
            Console.Error.WriteLine(
                "  DonJ.NibAbiValidator.exe capture --consumer <ENdll live> --runtime-api <NIB.dll> --output <abi.xml> --id <id> --version <version> [--force]");
            Console.Error.WriteLine(
                "  DonJ.NibAbiValidator.exe info --contract <abi.xml>");
        }

        private sealed class UsageException : Exception
        {
            public UsageException(string message)
                : base(message)
            {
            }
        }
    }
}
