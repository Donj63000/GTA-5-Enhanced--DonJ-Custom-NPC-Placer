using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using GTA;
using GTA.Math;
using GTA.Native;

namespace DonJ.JusticeRecognition
{
    /*
     * DonJ Justice Recognition
     *
     * Compatible :
     * - GTA V Enhanced
     * - ScriptHookVDotNet 2
     * - .NET Framework 4.8
     *
     * Fonctionnalités :
     * - véhicules signalés ;
     * - tenues signalées ;
     * - zone de recherche ;
     * - reconnaissance progressive par policiers et témoins ;
     * - persistance séparée Michael / Franklin / Trevor ;
     * - HUD avec trois PNG et fallback ;
     * - protection contre les boucles de wanted.
     */

    public static class JusticeRecognitionBridge
    {
        private static readonly object SyncRoot = new object();

        private static DonJJusticeRecognitionScript _instance;
        private static bool? _desiredEnabled;
        private static bool? _desiredRuntimeSuspended;
        private static string _desiredActiveProfileId;
        private static Func<int, bool> _wantedMinimumHandler;

        private static long _nextCriticalCommandId;

        private static BridgeCriticalCommand _pendingCurrentProfileCapture;
        private static readonly Dictionary<string, BridgeCriticalCommand>
            PendingProfileCaptureReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);

        private static BridgeCriticalCommand _pendingCurrentProfileClear;
        private static readonly Dictionary<string, BridgeCriticalCommand>
            PendingProfileClearReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);

        private static BridgeCriticalCommand _pendingGlobalClear;

        private static RecognitionCriticalIntentStore _criticalIntentStore;
        private static bool _criticalIntentsLoaded;
        private static string _criticalIntentPathOverride;

        internal static void Attach(DonJJusticeRecognitionScript instance)
        {
            lock (SyncRoot)
            {
                // Je recharge les intentions durables avant de livrer les
                // commandes : un arrêt entre Justice et le prochain tick ne
                // doit jamais faire réapparaître les anciens signalements.
                EnsureCriticalIntentsLoaded();
                _instance = instance;

                if (_desiredEnabled.HasValue)
                {
                    instance.QueueSetEnabled(_desiredEnabled.Value);
                }

                if (_desiredRuntimeSuspended.HasValue)
                {
                    instance.QueueSetRuntimeSuspended(
                        _desiredRuntimeSuspended.Value);
                }

                if (_desiredActiveProfileId != null)
                {
                    instance.QueueSetActiveProfile(
                        _desiredActiveProfileId);
                }

                // Je livre chaque commande critique conservée pendant l'absence
                // du script une seule fois, sous le même verrou que l'attache.
                if (_pendingCurrentProfileCapture != null)
                {
                    instance.QueuePlayerCaptured(
                        null,
                        _pendingCurrentProfileCapture.Reason,
                        _pendingCurrentProfileCapture.CommandId);
                }

                foreach (KeyValuePair<string, BridgeCriticalCommand> pendingCapture in
                         PendingProfileCaptureReasons)
                {
                    instance.QueuePlayerCaptured(
                        pendingCapture.Key,
                        pendingCapture.Value.Reason,
                        pendingCapture.Value.CommandId);
                }

                if (_pendingCurrentProfileClear != null)
                {
                    instance.QueueClearCurrentProfile(
                        _pendingCurrentProfileClear.Reason,
                        _pendingCurrentProfileClear.CommandId);
                }

                foreach (KeyValuePair<string, BridgeCriticalCommand> pendingClear in
                         PendingProfileClearReasons)
                {
                    instance.QueueClearProfile(
                        pendingClear.Key,
                        pendingClear.Value.Reason,
                        pendingClear.Value.CommandId);
                }

                if (_pendingGlobalClear != null)
                {
                    instance.QueueClearAllProfiles(
                        _pendingGlobalClear.Reason,
                        _pendingGlobalClear.CommandId);
                }
            }
        }

        internal static void Detach(DonJJusticeRecognitionScript instance)
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(_instance, instance))
                {
                    _instance = null;
                }
            }
        }

        /// <summary>
        /// Active ou désactive uniquement le module de reconnaissance.
        /// Cette méthode n'efface jamais les dossiers ni les indices persistés.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                _desiredEnabled = enabled;
                instance = _instance;
            }

            if (instance != null)
            {
                instance.QueueSetEnabled(enabled);
            }
        }

        /// <summary>
        /// Suspend les détections monde pendant une détention ou une reprise
        /// technique, sans modifier la préférence ON/OFF ni les indices.
        /// </summary>
        public static void SetRuntimeSuspended(bool suspended)
        {
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                _desiredRuntimeSuspended = suspended;
                instance = _instance;
            }

            if (instance != null)
            {
                instance.QueueSetRuntimeSuspended(suspended);
            }
        }

        /// <summary>
        /// Lie l'identité canonique Justice au module, y compris si le héros
        /// utilise temporairement un modèle personnalisé.
        /// </summary>
        public static void SetActiveProfile(string profileId)
        {
            string normalizedProfileId =
                DonJJusticeRecognitionScript.NormalizeProfileId(profileId);
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                _desiredActiveProfileId = normalizedProfileId;
                instance = _instance;
            }

            if (instance != null)
            {
                instance.QueueSetActiveProfile(normalizedProfileId);
            }
        }

        /// <summary>
        /// Relie le module au setter wanted sécurisé déjà présent dans Justice avancée.
        ///
        /// Exemple :
        /// JusticeRecognitionBridge.BindWantedMinimum(
        ///     delegate(int level) { return SetJusticeWantedMinimum(level); });
        /// </summary>
        public static void BindWantedMinimum(Func<int, bool> handler)
        {
            lock (SyncRoot)
            {
                _wantedMinimumHandler = handler;
            }
        }

        public static void UnbindWantedMinimum()
        {
            lock (SyncRoot)
            {
                _wantedMinimumHandler = null;
            }
        }

        internal static WantedMinimumApplicationResult
            ApplyWantedMinimumAtomically(int level)
        {
            Func<int, bool> handler;

            lock (SyncRoot)
            {
                handler = _wantedMinimumHandler;

                if (handler == null)
                {
                    return WantedMinimumApplicationResult.MissingHandler;
                }

                try
                {
                    return new WantedMinimumApplicationResult(
                        true,
                        handler(level));
                }
                catch
                {
                    return new WantedMinimumApplicationResult(
                        true,
                        false);
                }
            }
        }

        internal static bool TryApplyWantedMinimum(int level)
        {
            return ApplyWantedMinimumAtomically(level).Applied;
        }

        internal static bool HasWantedMinimumHandler()
        {
            lock (SyncRoot)
            {
                return _wantedMinimumHandler != null;
            }
        }

        /// <summary>
        /// À appeler avant une suppression volontaire des étoiles qui ne représente
        /// pas une fuite réussie.
        /// </summary>
        public static void SuppressNextWantedLoss(string reason)
        {
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                instance = _instance;
            }

            if (instance != null)
            {
                instance.QueueSuppressNextWantedLoss(reason);
            }
        }

        /// <summary>
        /// À appeler lorsqu'une arrestation, une capture ou une détention a abouti.
        /// Les signalements transitoires du profil actif sont alors supprimés.
        /// </summary>
        public static bool NotifyPlayerCaptured(string reason)
        {
            lock (SyncRoot)
            {
                EnsureCriticalIntentsLoaded();

                string profileId =
                    DonJJusticeRecognitionScript.NormalizeProfileId(
                        _desiredActiveProfileId);
                BridgeCriticalCommand command =
                    CreateCriticalCommand(reason);

                if (profileId != null)
                {
                    PendingProfileCaptureReasons[profileId] =
                        command;
                }
                else
                {
                    _pendingCurrentProfileCapture = command;
                }

                bool durablyRecorded = PersistCriticalIntents();

                if (_instance != null)
                {
                    _instance.QueuePlayerCaptured(
                        profileId,
                        command.Reason,
                        command.CommandId);
                }

                return durablyRecorded;
            }
        }

        /// <summary>
        /// Nettoie après une capture le profil canonique qui possédait la procédure.
        /// </summary>
        public static bool NotifyPlayerCaptured(
            string profileId,
            string reason)
        {
            string normalizedProfileId =
                DonJJusticeRecognitionScript.NormalizeProfileId(profileId);

            lock (SyncRoot)
            {
                EnsureCriticalIntentsLoaded();

                if (normalizedProfileId != null)
                {
                    BridgeCriticalCommand command =
                        CreateCriticalCommand(reason);
                    PendingProfileCaptureReasons[normalizedProfileId] =
                        command;

                    bool durablyRecorded = PersistCriticalIntents();

                    if (_instance != null)
                    {
                        _instance.QueuePlayerCaptured(
                            normalizedProfileId,
                            command.Reason,
                            command.CommandId);
                    }

                    return durablyRecorded;
                }

                return false;
            }
        }

        /// <summary>
        /// Efface les indices du personnage actif.
        /// Réservé à une amnistie, une clôture définitive ou un reset explicite.
        /// </summary>
        public static bool ClearCurrentProfile(string reason)
        {
            lock (SyncRoot)
            {
                EnsureCriticalIntentsLoaded();

                string profileId =
                    DonJJusticeRecognitionScript.NormalizeProfileId(
                        _desiredActiveProfileId);
                BridgeCriticalCommand command =
                    CreateCriticalCommand(reason);

                if (profileId != null)
                {
                    PendingProfileClearReasons[profileId] =
                        command;
                }
                else
                {
                    _pendingCurrentProfileClear = command;
                }

                bool durablyRecorded = PersistCriticalIntents();

                if (_instance != null)
                {
                    if (profileId != null)
                    {
                        _instance.QueueClearProfile(
                            profileId,
                            command.Reason,
                            command.CommandId);
                    }
                    else
                    {
                        _instance.QueueClearCurrentProfile(
                            command.Reason,
                            command.CommandId);
                    }
                }

                return durablyRecorded;
            }
        }

        /// <summary>
        /// Efface un protagoniste précis après son reset explicite et durable.
        /// </summary>
        public static bool ClearProfile(
            string profileId,
            string reason)
        {
            string normalizedProfileId =
                DonJJusticeRecognitionScript.NormalizeProfileId(profileId);

            if (normalizedProfileId == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                EnsureCriticalIntentsLoaded();

                BridgeCriticalCommand command =
                    CreateCriticalCommand(reason);
                PendingProfileClearReasons[normalizedProfileId] =
                    command;

                bool durablyRecorded = PersistCriticalIntents();

                if (_instance != null)
                {
                    _instance.QueueClearProfile(
                        normalizedProfileId,
                        command.Reason,
                        command.CommandId);
                }

                return durablyRecorded;
            }
        }

        /// <summary>
        /// Efface les indices des trois protagonistes.
        /// Réservé à un reset global explicitement demandé.
        /// </summary>
        public static bool ClearAllProfiles(string reason)
        {
            lock (SyncRoot)
            {
                EnsureCriticalIntentsLoaded();

                BridgeCriticalCommand command =
                    CreateCriticalCommand(reason);

                // Je fais du reset global la commande dominante : tout ciblage
                // plus ancien est déjà couvert et ne doit pas être rejoué après.
                _pendingCurrentProfileCapture = null;
                PendingProfileCaptureReasons.Clear();
                _pendingCurrentProfileClear = null;
                PendingProfileClearReasons.Clear();
                _pendingGlobalClear = command;

                bool durablyRecorded = PersistCriticalIntents();

                if (_instance != null)
                {
                    _instance.QueueClearAllProfiles(
                        command.Reason,
                        command.CommandId);
                }

                return durablyRecorded;
            }
        }

        internal static bool AcknowledgePlayerCaptured(
            string profileId,
            long commandId)
        {
            lock (SyncRoot)
            {
                return AcknowledgeTargetedCommand(
                    PendingProfileCaptureReasons,
                    profileId,
                    commandId,
                    ref _pendingCurrentProfileCapture);
            }
        }

        internal static bool AcknowledgeProfileClear(
            string profileId,
            long commandId)
        {
            lock (SyncRoot)
            {
                return AcknowledgeTargetedCommand(
                    PendingProfileClearReasons,
                    profileId,
                    commandId,
                    ref _pendingCurrentProfileClear);
            }
        }

        internal static bool AcknowledgeGlobalClear(long commandId)
        {
            if (commandId <= 0L)
            {
                return true;
            }

            lock (SyncRoot)
            {
                if (!EnsureCriticalIntentsLoaded())
                {
                    return false;
                }

                if (_pendingGlobalClear != null &&
                    _pendingGlobalClear.CommandId == commandId)
                {
                    BridgeCriticalCommand acknowledged =
                        _pendingGlobalClear;
                    _pendingGlobalClear = null;

                    if (!PersistCriticalIntents())
                    {
                        _pendingGlobalClear = acknowledged;
                        return false;
                    }
                }

                return true;
            }
        }

        private static bool AcknowledgeTargetedCommand(
            Dictionary<string, BridgeCriticalCommand> commands,
            string profileId,
            long commandId,
            ref BridgeCriticalCommand currentCommand)
        {
            if (commandId <= 0L)
            {
                return true;
            }

            if (!EnsureCriticalIntentsLoaded())
            {
                return false;
            }

            string normalizedProfileId =
                DonJJusticeRecognitionScript.NormalizeProfileId(profileId);

            if (normalizedProfileId == null)
            {
                if (currentCommand != null &&
                    currentCommand.CommandId == commandId)
                {
                    BridgeCriticalCommand acknowledged = currentCommand;
                    currentCommand = null;

                    if (!PersistCriticalIntents())
                    {
                        currentCommand = acknowledged;
                        return false;
                    }
                }

                return true;
            }

            BridgeCriticalCommand pending;
            if (commands.TryGetValue(normalizedProfileId, out pending) &&
                pending != null &&
                pending.CommandId == commandId)
            {
                commands.Remove(normalizedProfileId);

                if (!PersistCriticalIntents())
                {
                    commands[normalizedProfileId] = pending;
                    return false;
                }
            }

            return true;
        }

        private static BridgeCriticalCommand CreateCriticalCommand(
            string reason)
        {
            long utcCandidate = DateTime.UtcNow.Ticks;

            if (_nextCriticalCommandId >= long.MaxValue)
            {
                _nextCriticalCommandId = 1L;
            }
            else if (utcCandidate > _nextCriticalCommandId)
            {
                // Je pars de l'horloge UTC pour éviter qu'un redémarrage
                // réutilise l'identifiant d'une intention encore sur disque.
                _nextCriticalCommandId = utcCandidate;
            }
            else
            {
                _nextCriticalCommandId++;
            }

            return new BridgeCriticalCommand(
                _nextCriticalCommandId,
                DonJJusticeRecognitionScript.SafeReason(reason));
        }

        private static bool EnsureCriticalIntentsLoaded()
        {
            if (_criticalIntentsLoaded)
            {
                return true;
            }

            try
            {
                if (_criticalIntentStore == null)
                {
                    string path = _criticalIntentPathOverride;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        string directory =
                            DonJJusticeRecognitionScript
                                .ResolveWritableDataDirectoryForBridge();
                        path = Path.Combine(
                            directory,
                            "JusticeRecognition.critical-intents.xml");
                    }

                    _criticalIntentStore =
                        new RecognitionCriticalIntentStore(path);
                }

                RecognitionCriticalIntentJournalData persisted;
                if (!_criticalIntentStore.TryLoad(out persisted))
                {
                    return false;
                }

                MergeCriticalIntents(persisted);
                _criticalIntentsLoaded = true;
                return true;
            }
            catch
            {
                // Je garde les commandes en mémoire et je laisse le prochain
                // appel retenter le journal sans bloquer la détention.
                return false;
            }
        }

        private static bool PersistCriticalIntents()
        {
            if (!EnsureCriticalIntentsLoaded() ||
                _criticalIntentStore == null)
            {
                return false;
            }

            return _criticalIntentStore.ForceSave(
                CreateCriticalIntentSnapshot());
        }

        private static RecognitionCriticalIntentJournalData
            CreateCriticalIntentSnapshot()
        {
            RecognitionCriticalIntentJournalData data =
                new RecognitionCriticalIntentJournalData
                {
                    NextCommandId = _nextCriticalCommandId
                };

            AddCriticalIntent(
                data,
                RecognitionCriticalIntentKinds.CaptureCurrent,
                null,
                _pendingCurrentProfileCapture);

            foreach (KeyValuePair<string, BridgeCriticalCommand> command in
                     PendingProfileCaptureReasons)
            {
                AddCriticalIntent(
                    data,
                    RecognitionCriticalIntentKinds.CaptureProfile,
                    command.Key,
                    command.Value);
            }

            AddCriticalIntent(
                data,
                RecognitionCriticalIntentKinds.ClearCurrent,
                null,
                _pendingCurrentProfileClear);

            foreach (KeyValuePair<string, BridgeCriticalCommand> command in
                     PendingProfileClearReasons)
            {
                AddCriticalIntent(
                    data,
                    RecognitionCriticalIntentKinds.ClearProfile,
                    command.Key,
                    command.Value);
            }

            AddCriticalIntent(
                data,
                RecognitionCriticalIntentKinds.ClearAll,
                null,
                _pendingGlobalClear);

            return data;
        }

        private static void AddCriticalIntent(
            RecognitionCriticalIntentJournalData data,
            string kind,
            string profileId,
            BridgeCriticalCommand command)
        {
            if (data == null || command == null)
            {
                return;
            }

            data.Intents.Add(
                new RecognitionCriticalIntentRecord
                {
                    CommandId = command.CommandId,
                    Kind = kind,
                    ProfileId = profileId,
                    Reason = command.Reason
                });
        }

        private static void MergeCriticalIntents(
            RecognitionCriticalIntentJournalData persisted)
        {
            if (persisted == null)
            {
                return;
            }

            if (persisted.NextCommandId > _nextCriticalCommandId)
            {
                _nextCriticalCommandId = persisted.NextCommandId;
            }

            List<RecognitionCriticalIntentRecord> intents =
                persisted.Intents ??
                new List<RecognitionCriticalIntentRecord>();

            intents.Sort(
                delegate(
                    RecognitionCriticalIntentRecord left,
                    RecognitionCriticalIntentRecord right)
                {
                    long leftId = left == null ? 0L : left.CommandId;
                    long rightId = right == null ? 0L : right.CommandId;
                    return leftId.CompareTo(rightId);
                });

            for (int index = 0; index < intents.Count; index++)
            {
                RecognitionCriticalIntentRecord intent = intents[index];
                if (intent == null || intent.CommandId <= 0L)
                {
                    continue;
                }

                if (intent.CommandId > _nextCriticalCommandId)
                {
                    _nextCriticalCommandId = intent.CommandId;
                }

                BridgeCriticalCommand command =
                    new BridgeCriticalCommand(
                        intent.CommandId,
                        DonJJusticeRecognitionScript.SafeReason(intent.Reason));
                string profileId =
                    DonJJusticeRecognitionScript.NormalizeProfileId(
                        intent.ProfileId);

                if (string.Equals(
                    intent.Kind,
                    RecognitionCriticalIntentKinds.ClearAll,
                    StringComparison.Ordinal))
                {
                    RemoveCriticalCommandAtOrBefore(
                        ref _pendingCurrentProfileCapture,
                        command.CommandId);
                    RemoveCriticalCommandsAtOrBefore(
                        PendingProfileCaptureReasons,
                        command.CommandId);
                    RemoveCriticalCommandAtOrBefore(
                        ref _pendingCurrentProfileClear,
                        command.CommandId);
                    RemoveCriticalCommandsAtOrBefore(
                        PendingProfileClearReasons,
                        command.CommandId);
                    KeepNewestCriticalCommand(
                        ref _pendingGlobalClear,
                        command);
                }
                else if (string.Equals(
                    intent.Kind,
                    RecognitionCriticalIntentKinds.CaptureCurrent,
                    StringComparison.Ordinal))
                {
                    KeepNewestCriticalCommand(
                        ref _pendingCurrentProfileCapture,
                        command);
                }
                else if (string.Equals(
                    intent.Kind,
                    RecognitionCriticalIntentKinds.CaptureProfile,
                    StringComparison.Ordinal) &&
                    profileId != null)
                {
                    KeepNewestCriticalCommand(
                        PendingProfileCaptureReasons,
                        profileId,
                        command);
                }
                else if (string.Equals(
                    intent.Kind,
                    RecognitionCriticalIntentKinds.ClearCurrent,
                    StringComparison.Ordinal))
                {
                    KeepNewestCriticalCommand(
                        ref _pendingCurrentProfileClear,
                        command);
                }
                else if (string.Equals(
                    intent.Kind,
                    RecognitionCriticalIntentKinds.ClearProfile,
                    StringComparison.Ordinal) &&
                    profileId != null)
                {
                    KeepNewestCriticalCommand(
                        PendingProfileClearReasons,
                        profileId,
                        command);
                }
            }
        }

        private static void KeepNewestCriticalCommand(
            ref BridgeCriticalCommand current,
            BridgeCriticalCommand candidate)
        {
            if (candidate != null &&
                (current == null ||
                 candidate.CommandId > current.CommandId))
            {
                current = candidate;
            }
        }

        private static void KeepNewestCriticalCommand(
            Dictionary<string, BridgeCriticalCommand> commands,
            string profileId,
            BridgeCriticalCommand candidate)
        {
            BridgeCriticalCommand current;
            if (candidate != null &&
                (!commands.TryGetValue(profileId, out current) ||
                 current == null ||
                 candidate.CommandId > current.CommandId))
            {
                commands[profileId] = candidate;
            }
        }

        private static void RemoveCriticalCommandAtOrBefore(
            ref BridgeCriticalCommand command,
            long maximumCommandId)
        {
            if (command != null &&
                command.CommandId <= maximumCommandId)
            {
                command = null;
            }
        }

        private static void RemoveCriticalCommandsAtOrBefore(
            Dictionary<string, BridgeCriticalCommand> commands,
            long maximumCommandId)
        {
            List<string> removals = new List<string>();

            foreach (KeyValuePair<string, BridgeCriticalCommand> command in
                     commands)
            {
                if (command.Value == null ||
                    command.Value.CommandId <= maximumCommandId)
                {
                    removals.Add(command.Key);
                }
            }

            for (int index = 0; index < removals.Count; index++)
            {
                commands.Remove(removals[index]);
            }
        }

        internal static void ConfigureCriticalIntentJournalForTests(
            string path)
        {
            lock (SyncRoot)
            {
                _criticalIntentPathOverride = path;
                _criticalIntentStore = null;
                _criticalIntentsLoaded = false;
            }
        }

        /// <summary>
        /// Retourne des lignes prêtes à afficher dans le menu Justice avancée.
        /// </summary>
        public static string[] GetStatusLines()
        {
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                instance = _instance;
            }

            if (instance == null)
            {
                return new[]
                {
                    "Reconnaissance policière : module non chargé",
                    "Plaques signalées : état indisponible",
                    "Tenues signalées : état indisponible",
                    "Mandat local : état indisponible",
                    "Distance du centre : état indisponible"
                };
            }

            return instance.GetStatusLines();
        }

        /// <summary>
        /// Indique, sans lire le monde GTA, si le profil actif possède une
        /// vraie zone locale actuellement pilotée par ce module.
        /// </summary>
        public static bool HasActiveSearchZone()
        {
            DonJJusticeRecognitionScript instance;

            lock (SyncRoot)
            {
                instance = _instance;
            }

            return instance != null &&
                   instance.HasActiveSearchZoneCached();
        }
    }

    internal struct WantedMinimumApplicationResult
    {
        public static readonly WantedMinimumApplicationResult MissingHandler =
            new WantedMinimumApplicationResult(false, false);

        public WantedMinimumApplicationResult(
            bool handlerPresent,
            bool applied)
        {
            HandlerPresent = handlerPresent;
            Applied = applied;
        }

        public bool HandlerPresent { get; private set; }

        public bool Applied { get; private set; }
    }

    internal sealed class BridgeCriticalCommand
    {
        public BridgeCriticalCommand(
            long commandId,
            string reason)
        {
            CommandId = commandId;
            Reason = reason;
        }

        public long CommandId { get; private set; }

        public string Reason { get; private set; }
    }

    internal static class RecognitionCriticalIntentKinds
    {
        public const string CaptureCurrent = "capture-current";
        public const string CaptureProfile = "capture-profile";
        public const string ClearCurrent = "clear-current";
        public const string ClearProfile = "clear-profile";
        public const string ClearAll = "clear-all";

        public static bool IsSupported(string kind)
        {
            return string.Equals(kind, CaptureCurrent, StringComparison.Ordinal) ||
                   string.Equals(kind, CaptureProfile, StringComparison.Ordinal) ||
                   string.Equals(kind, ClearCurrent, StringComparison.Ordinal) ||
                   string.Equals(kind, ClearProfile, StringComparison.Ordinal) ||
                   string.Equals(kind, ClearAll, StringComparison.Ordinal);
        }
    }

    [XmlRoot("DonJJusticeRecognitionCriticalIntents")]
    public sealed class RecognitionCriticalIntentJournalData
    {
        public RecognitionCriticalIntentJournalData()
        {
            SchemaVersion = 1;
            Intents = new List<RecognitionCriticalIntentRecord>();
        }

        [XmlAttribute("schemaVersion")]
        public int SchemaVersion { get; set; }

        [XmlAttribute("nextCommandId")]
        public long NextCommandId { get; set; }

        [XmlElement("Intent")]
        public List<RecognitionCriticalIntentRecord> Intents { get; set; }
    }

    public sealed class RecognitionCriticalIntentRecord
    {
        [XmlAttribute("commandId")]
        public long CommandId { get; set; }

        [XmlAttribute("kind")]
        public string Kind { get; set; }

        [XmlAttribute("profileId")]
        public string ProfileId { get; set; }

        [XmlAttribute("reason")]
        public string Reason { get; set; }
    }

    internal sealed class RecognitionCriticalIntentStore
    {
        private const long MaximumJournalBytes = 256L * 1024L;
        private const int MaximumIntentCount = 32;

        private readonly string _path;
        private readonly string _backupPath;
        private readonly string _temporaryPath;
        private readonly string _backupTemporaryPath;
        private readonly string _primaryRollbackPath;
        private readonly string _backupRollbackPath;
        private readonly string _quarantineDirectoryPath;

        public RecognitionCriticalIntentStore(string path)
        {
            _path = path;
            _backupPath = path + ".bak";
            _temporaryPath = path + ".tmp";
            _backupTemporaryPath = path + ".bak.tmp";
            _primaryRollbackPath = path + ".rollback";
            _backupRollbackPath = path + ".bak.rollback";
            _quarantineDirectoryPath = path + ".corrupt-quarantine";
        }

        public bool TryLoad(
            out RecognitionCriticalIntentJournalData data)
        {
            data = null;

            RecognitionCriticalIntentJournalData primary;
            RecognitionCriticalIntentJournalData backup;
            RecognitionCriticalIntentJournalData temporary;
            RecognitionCriticalIntentJournalData backupTemporary;
            RecognitionCriticalIntentJournalData primaryRollback;
            RecognitionCriticalIntentJournalData backupRollback;

            bool hasPrimary = TryLoadFile(_path, out primary);
            bool hasBackup = TryLoadFile(_backupPath, out backup);
            bool hasTemporary = TryLoadFile(_temporaryPath, out temporary);
            bool hasBackupTemporary =
                TryLoadFile(_backupTemporaryPath, out backupTemporary);
            bool hasPrimaryRollback =
                TryLoadFile(_primaryRollbackPath, out primaryRollback);
            bool hasBackupRollback =
                TryLoadFile(_backupRollbackPath, out backupRollback);

            string selectedPath = null;
            DateTime selectedWrite = DateTime.MinValue;

            SelectNewestValid(
                hasPrimary,
                _path,
                primary,
                ref selectedPath,
                ref selectedWrite,
                ref data);
            SelectNewestValid(
                hasBackup,
                _backupPath,
                backup,
                ref selectedPath,
                ref selectedWrite,
                ref data);
            SelectNewestValid(
                hasTemporary,
                _temporaryPath,
                temporary,
                ref selectedPath,
                ref selectedWrite,
                ref data);
            SelectNewestValid(
                hasBackupTemporary,
                _backupTemporaryPath,
                backupTemporary,
                ref selectedPath,
                ref selectedWrite,
                ref data);
            SelectNewestValid(
                hasPrimaryRollback,
                _primaryRollbackPath,
                primaryRollback,
                ref selectedPath,
                ref selectedWrite,
                ref data);
            SelectNewestValid(
                hasBackupRollback,
                _backupRollbackPath,
                backupRollback,
                ref selectedPath,
                ref selectedWrite,
                ref data);

            if (data == null)
            {
                bool hasCorruptVariant =
                    HasAnyJournalVariant();

                if (hasCorruptVariant &&
                    !QuarantineCorruptVariants())
                {
                    return false;
                }

                data = new RecognitionCriticalIntentJournalData();

                // Je rends le journal neuf redondant avant de laisser Justice
                // enregistrer une nouvelle intention. Une coupure après la
                // quarantaine reprend ainsi sur un primaire et un backup vides,
                // sans jamais recharger un XML corrompu.
                return ForceSave(data);
            }

            bool redundantPairCurrent =
                hasPrimary &&
                hasBackup &&
                RecognitionStore.FilesEqual(_path, _backupPath) &&
                (string.Equals(
                     selectedPath,
                     _path,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     selectedPath,
                     _backupPath,
                     StringComparison.OrdinalIgnoreCase));

            if (!redundantPairCurrent)
            {
                // Je réédite le dernier intent valide afin que primaire et
                // backup redeviennent immédiatement identiques.
                ForceSave(data);
            }

            return true;
        }

        private bool HasAnyJournalVariant()
        {
            return File.Exists(_path) ||
                   File.Exists(_backupPath) ||
                   File.Exists(_temporaryPath) ||
                   File.Exists(_backupTemporaryPath) ||
                   File.Exists(_primaryRollbackPath) ||
                   File.Exists(_backupRollbackPath);
        }

        private bool QuarantineCorruptVariants()
        {
            string[] variants =
            {
                _path,
                _backupPath,
                _temporaryPath,
                _backupTemporaryPath,
                _primaryRollbackPath,
                _backupRollbackPath
            };

            try
            {
                Directory.CreateDirectory(_quarantineDirectoryPath);

                string incidentId =
                    DateTime.UtcNow.ToString(
                        "yyyyMMddHHmmssfffffff",
                        CultureInfo.InvariantCulture) +
                    "-" +
                    Guid.NewGuid().ToString("N");

                for (int index = 0; index < variants.Length; index++)
                {
                    string sourcePath = variants[index];
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(
                        _quarantineDirectoryPath,
                        Path.GetFileName(sourcePath) +
                        "." +
                        incidentId +
                        ".corrupt");

                    // Je déplace chaque variante sur le même volume : aucun XML
                    // illisible n'est écrasé et la quarantaine reste exploitable
                    // pour un diagnostic ultérieur.
                    File.Move(sourcePath, destinationPath);
                }

                return !HasAnyJournalVariant();
            }
            catch
            {
                // Je reste fermé si une variante ne peut pas sortir des chemins
                // de chargement. Le prochain appel reprend les déplacements déjà
                // accomplis sans acquitter la commande Justice en attente.
                return false;
            }
        }

        public bool ForceSave(
            RecognitionCriticalIntentJournalData data)
        {
            if (data == null)
            {
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                SerializeToFile(data, _temporaryPath);
                SerializeToFile(data, _backupTemporaryPath);

                RecognitionCriticalIntentJournalData validated;
                RecognitionCriticalIntentJournalData validatedBackup;
                if (!TryLoadFile(_temporaryPath, out validated) ||
                    !TryLoadFile(_backupTemporaryPath, out validatedBackup) ||
                    !RecognitionStore.FilesEqual(
                        _temporaryPath,
                        _backupTemporaryPath))
                {
                    return false;
                }

                RecognitionStore.PublishValidatedTemporary(
                    _temporaryPath,
                    _path,
                    _primaryRollbackPath);
                RecognitionStore.PublishValidatedTemporary(
                    _backupTemporaryPath,
                    _backupPath,
                    _backupRollbackPath);

                RecognitionCriticalIntentJournalData published;
                RecognitionCriticalIntentJournalData publishedBackup;
                return TryLoadFile(_path, out published) &&
                       TryLoadFile(_backupPath, out publishedBackup) &&
                       RecognitionStore.FilesEqual(_path, _backupPath);
            }
            catch
            {
                return false;
            }
        }

        private static void SelectNewestValid(
            bool valid,
            string path,
            RecognitionCriticalIntentJournalData candidate,
            ref string selectedPath,
            ref DateTime selectedWrite,
            ref RecognitionCriticalIntentJournalData selected)
        {
            if (!valid || candidate == null)
            {
                return;
            }

            DateTime write = RecognitionStore.GetLastWriteTimeUtcSafe(path);
            if (selected == null || write > selectedWrite)
            {
                selectedPath = path;
                selectedWrite = write;
                selected = candidate;
            }
        }

        private static void SerializeToFile(
            RecognitionCriticalIntentJournalData data,
            string path)
        {
            XmlSerializer serializer =
                new XmlSerializer(
                    typeof(RecognitionCriticalIntentJournalData));
            XmlWriterSettings settings =
                new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = new UTF8Encoding(false),
                    CloseOutput = false
                };

            using (FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    serializer.Serialize(writer, data);
                    writer.Flush();
                }

                stream.Flush(true);
            }
        }

        private static bool TryLoadFile(
            string path,
            out RecognitionCriticalIntentJournalData data)
        {
            data = null;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Length <= 0L ||
                    file.Length > MaximumJournalBytes)
                {
                    return false;
                }

                XmlSerializer serializer =
                    new XmlSerializer(
                        typeof(RecognitionCriticalIntentJournalData));
                XmlReaderSettings settings =
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        CloseInput = true
                    };

                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (XmlReader reader = XmlReader.Create(stream, settings))
                {
                    data = serializer.Deserialize(reader)
                        as RecognitionCriticalIntentJournalData;
                }

                return Sanitize(data);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        private static bool Sanitize(
            RecognitionCriticalIntentJournalData data)
        {
            if (data == null || data.SchemaVersion != 1)
            {
                return false;
            }

            if (data.Intents == null)
            {
                data.Intents = new List<RecognitionCriticalIntentRecord>();
            }

            if (data.Intents.Count > MaximumIntentCount)
            {
                return false;
            }

            long highestId = data.NextCommandId;
            for (int index = 0; index < data.Intents.Count; index++)
            {
                RecognitionCriticalIntentRecord intent = data.Intents[index];
                if (intent == null ||
                    intent.CommandId <= 0L ||
                    !RecognitionCriticalIntentKinds.IsSupported(intent.Kind))
                {
                    return false;
                }

                bool profileRequired =
                    string.Equals(
                        intent.Kind,
                        RecognitionCriticalIntentKinds.CaptureProfile,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        intent.Kind,
                        RecognitionCriticalIntentKinds.ClearProfile,
                        StringComparison.Ordinal);
                string profileId =
                    DonJJusticeRecognitionScript.NormalizeProfileId(
                        intent.ProfileId);
                if (profileRequired && profileId == null)
                {
                    return false;
                }

                intent.ProfileId = profileId;
                intent.Reason =
                    DonJJusticeRecognitionScript.SafeReason(intent.Reason);
                if (intent.CommandId > highestId)
                {
                    highestId = intent.CommandId;
                }
            }

            data.NextCommandId = highestId;
            return true;
        }
    }

    public sealed class DonJJusticeRecognitionScript : Script
    {
        private const int IdentityRefreshMilliseconds = 600;
        private const int PursuitCaptureMilliseconds = 600;
        private const int RepaintCheckMilliseconds = 750;
        private const int ExpirationCheckMilliseconds = 1000;
        private const int RecognitionScanMilliseconds = 350;
        private const int WantedTransitionStabilizationMilliseconds = 220;
        private const int WantedLossStabilizationMilliseconds = 900;
        private const int WantedWriteGuardMilliseconds = 1500;
        private const int MaximumTrackedObservers = 12;

        private readonly object _commandSync = new object();
        private readonly object _statusSync = new object();

        private RecognitionLogger _logger;
        private RecognitionStore _store;
        private JusticeRecognitionSaveData _saveData;
        private JusticeRecognitionHud _hud;
        private RadiusBlipController _radiusBlip;

        private bool _initialized;
        private bool _initializationFailed;
        private bool _enabled;
        private bool _runtimeSuspended;

        private RecognitionProfileData _currentProfile;
        private string _currentProfileId;
        private string _authoritativeProfileId;

        private int _lastWantedLevel;
        private int _lastReliableWantedLevel;
        private int _nextIdentityRefresh;
        private int _nextPursuitCapture;
        private int _nextRepaintCheck;
        private int _nextExpirationCheck;
        private int _nextRecognitionScan;
        private int _lastRecognitionScanTime;
        private int _wantedWriteGuardUntil;
        private int _skipNaturalEscalationUntil;

        // Je garde ce lecteur injectable uniquement pour pouvoir prouver que
        // plusieurs échecs GTA consécutifs ne fabriquent jamais une fuite.
        private Func<int> _wantedLevelReaderOverride = null;

        private IdentitySnapshot _identityCache;
        private int _identityCachePedHandle;

        private PursuitEpisodeRuntime _currentEpisode;
        private PendingWantedLossRuntime _pendingWantedLoss;
        private PendingWantedEscalationRuntime _pendingWantedEscalation;

        private bool _suppressNextWantedLoss;
        private string _suppressNextWantedLossReason;
        private long _suppressedWantedLossEpisodeId;
        private int _suppressWantedLossUntil;

        private bool _hasLastPlayerPosition;
        private Vector3 _lastPlayerPosition;

        private readonly Dictionary<int, ObserverExposureRuntime> _observerExposures =
            new Dictionary<int, ObserverExposureRuntime>();

        private readonly List<int> _observerRemovalBuffer = new List<int>();

        private MethodInfo _getNearbyPedsMethod;
        private bool _nearbyPedsMethodResolved;

        private bool _insideSearchZone;
        private int _recognitionScanSequence;

        private bool _hasQueuedEnabledState;
        private bool _queuedEnabledState;

        private bool _hasQueuedRuntimeSuspendedState;
        private bool _queuedRuntimeSuspendedState;

        private bool _hasQueuedActiveProfile;
        private string _queuedActiveProfileId;

        private bool _queuedSuppressWantedLoss;
        private string _queuedSuppressReason;

        private BridgeCriticalCommand _queuedCurrentProfileCaptured;

        private readonly Dictionary<string, BridgeCriticalCommand>
            _queuedProfileCaptureReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);

        private BridgeCriticalCommand _queuedClearCurrentProfile;

        private readonly Dictionary<string, BridgeCriticalCommand>
            _queuedProfileClearReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);

        private BridgeCriticalCommand _queuedClearAllProfiles;
        private int _criticalCommandRetryNotBefore;

        private int _lastRuntimeErrorNotificationTime;
        private int _nextStatusRefresh;
        private string[] _statusLines =
        {
            "Reconnaissance policière : initialisation",
            "Plaques signalées : état indisponible",
            "Tenues signalées : état indisponible",
            "Mandat local : état indisponible",
            "Distance du centre : état indisponible"
        };
        private bool _hasActiveSearchZoneStatus;

        public DonJJusticeRecognitionScript()
        {
            Interval = 0;

            Tick += OnTick;
            Aborted += OnAborted;

            JusticeRecognitionBridge.Attach(this);
        }

        internal void QueueSetEnabled(bool enabled)
        {
            lock (_commandSync)
            {
                _hasQueuedEnabledState = true;
                _queuedEnabledState = enabled;
            }
        }

        internal void QueueSetRuntimeSuspended(bool suspended)
        {
            lock (_commandSync)
            {
                _hasQueuedRuntimeSuspendedState = true;
                _queuedRuntimeSuspendedState = suspended;
            }
        }

        internal void QueueSetActiveProfile(string profileId)
        {
            lock (_commandSync)
            {
                _hasQueuedActiveProfile = true;
                _queuedActiveProfileId = NormalizeProfileId(profileId);
            }
        }

        internal void QueueSuppressNextWantedLoss(string reason)
        {
            lock (_commandSync)
            {
                _queuedSuppressWantedLoss = true;
                _queuedSuppressReason = SafeReason(reason);
            }
        }

        internal void QueuePlayerCaptured(
            string profileId,
            string reason)
        {
            QueuePlayerCaptured(profileId, reason, 0L);
        }

        internal void QueuePlayerCaptured(
            string profileId,
            string reason,
            long commandId)
        {
            string normalizedProfileId = NormalizeProfileId(profileId);
            BridgeCriticalCommand command =
                new BridgeCriticalCommand(
                    commandId,
                    SafeReason(reason));

            lock (_commandSync)
            {
                if (normalizedProfileId == null)
                {
                    _queuedCurrentProfileCaptured = command;
                }
                else
                {
                    _queuedProfileCaptureReasons[normalizedProfileId] =
                        command;
                }
            }
        }

        internal void QueueClearCurrentProfile(string reason)
        {
            QueueClearCurrentProfile(reason, 0L);
        }

        internal void QueueClearCurrentProfile(
            string reason,
            long commandId)
        {
            lock (_commandSync)
            {
                _queuedClearCurrentProfile =
                    new BridgeCriticalCommand(
                        commandId,
                        SafeReason(reason));
            }
        }

        internal void QueueClearProfile(
            string profileId,
            string reason)
        {
            QueueClearProfile(profileId, reason, 0L);
        }

        internal void QueueClearProfile(
            string profileId,
            string reason,
            long commandId)
        {
            string normalizedProfileId = NormalizeProfileId(profileId);
            if (normalizedProfileId == null)
            {
                return;
            }

            lock (_commandSync)
            {
                _queuedProfileClearReasons[normalizedProfileId] =
                    new BridgeCriticalCommand(
                        commandId,
                        SafeReason(reason));
            }
        }

        internal void QueueClearAllProfiles(string reason)
        {
            QueueClearAllProfiles(reason, 0L);
        }

        internal void QueueClearAllProfiles(
            string reason,
            long commandId)
        {
            lock (_commandSync)
            {
                // Je supprime les commandes plus anciennes déjà couvertes par
                // ce reset global, y compris celles livrées avant son arrivée.
                _queuedCurrentProfileCaptured = null;
                _queuedProfileCaptureReasons.Clear();
                _queuedClearCurrentProfile = null;
                _queuedProfileClearReasons.Clear();
                _queuedClearAllProfiles =
                    new BridgeCriticalCommand(
                        commandId,
                        SafeReason(reason));
            }
        }

        private void OnTick(object sender, EventArgs eventArgs)
        {
            try
            {
                if (!EnsureInitialized())
                {
                    return;
                }

                int nowGameTime = Game.GameTime;
                DateTime nowUtc = DateTime.UtcNow;

                Ped playerPed = GetUsablePlayerPed();
                bool profileResolved = false;

                if (playerPed != null)
                {
                    profileResolved =
                        UpdateActiveProfile(playerPed, nowGameTime);
                }
                else
                {
                    PauseForUnknownProfile();
                }

                DrainQueuedCommands(nowGameTime);

                if (!profileResolved ||
                    _currentProfile == null ||
                    playerPed == null)
                {
                    _store.FlushIfDue(nowGameTime);
                    return;
                }

                if (nowGameTime >= _nextExpirationCheck)
                {
                    _nextExpirationCheck =
                        SafeGameTimeAdd(nowGameTime, ExpirationCheckMilliseconds);

                    CleanupExpiredEvidence(nowUtc, nowGameTime);
                }

                int currentWanted = SafeGetWantedLevel();

                if (!_enabled)
                {
                    _lastWantedLevel = currentWanted;
                    _currentEpisode = null;
                    _pendingWantedLoss = null;
                    _pendingWantedEscalation = null;
                    _observerExposures.Clear();
                    _insideSearchZone = false;

                    RemoveSearchZoneBlip();
                    _store.FlushIfDue(nowGameTime);
                    return;
                }

                if (_runtimeSuspended)
                {
                    _lastWantedLevel = currentWanted;
                    ResetCurrentProfileRuntimeState();
                    RemoveSearchZoneBlip();
                    _store.FlushIfDue(nowGameTime);
                    return;
                }

                EnsureSearchZoneBlip(nowUtc);

                ProcessWantedState(
                    playerPed,
                    currentWanted,
                    nowGameTime,
                    nowUtc);

                if (nowGameTime >= _nextRepaintCheck)
                {
                    _nextRepaintCheck =
                        SafeGameTimeAdd(nowGameTime, RepaintCheckMilliseconds);

                    CheckVehicleRepaint(playerPed, nowGameTime, nowUtc);
                }

                UpdateSearchZoneRecognition(
                    playerPed,
                    currentWanted,
                    nowGameTime,
                    nowUtc);

                IdentitySnapshot identity =
                    GetCurrentIdentity(playerPed, nowGameTime, false);

                DrawHud(playerPed, identity, nowUtc);

                if (nowGameTime >= _nextStatusRefresh)
                {
                    _nextStatusRefresh = SafeGameTimeAdd(
                        nowGameTime,
                        IdentityRefreshMilliseconds);

                    RefreshStatusLinesCache();
                }

                _lastPlayerPosition = playerPed.Position;
                _hasLastPlayerPosition = true;

                _store.FlushIfDue(nowGameTime);
            }
            catch (Exception exception)
            {
                HandleRuntimeException(exception);
            }
        }

        private void OnAborted(object sender, EventArgs eventArgs)
        {
            try
            {
                RemoveSearchZoneBlip();

                if (_store != null)
                {
                    _store.ForceSave(_saveData);
                }

                if (_hud != null)
                {
                    _hud.Dispose();
                }
            }
            catch
            {
                // Ne jamais propager une exception pendant l'arrêt d'un script.
            }
            finally
            {
                JusticeRecognitionBridge.Detach(this);
            }
        }

        private bool EnsureInitialized()
        {
            if (_initialized)
            {
                return true;
            }

            if (_initializationFailed)
            {
                return false;
            }

            try
            {
                string assemblyPath =
                    Assembly.GetExecutingAssembly().Location;

                string assemblyDirectory =
                    string.IsNullOrWhiteSpace(assemblyPath)
                        ? AppDomain.CurrentDomain.BaseDirectory
                        : Path.GetDirectoryName(assemblyPath);

                if (string.IsNullOrWhiteSpace(assemblyDirectory))
                {
                    assemblyDirectory =
                        AppDomain.CurrentDomain.BaseDirectory;
                }

                string runtimeDirectory =
                    global::DonJEnemySpawner
                        .GetJusticeRecognitionRuntimeDirectorySafe();

                if (string.IsNullOrWhiteSpace(runtimeDirectory))
                {
                    runtimeDirectory = assemblyDirectory;
                }

                string dataDirectory =
                    ResolveWritableDataDirectory(
                        runtimeDirectory,
                        assemblyDirectory);

                string assetsDirectory =
                    ResolveAssetsDirectory(
                        runtimeDirectory,
                        assemblyDirectory);

                _logger = new RecognitionLogger(
                    Path.Combine(
                        dataDirectory,
                        "JusticeRecognition.log"));

                _store = new RecognitionStore(
                    Path.Combine(
                        dataDirectory,
                        "JusticeRecognition.xml"),
                    _logger);

                _saveData = _store.Load();

                if (_saveData == null)
                {
                    _saveData = new JusticeRecognitionSaveData();
                }

                RecognitionDataSanitizer.SanitizeSaveData(
                    _saveData,
                    DateTime.UtcNow,
                    _logger);

                _hud = new JusticeRecognitionHud(
                    assetsDirectory,
                    _logger);

                _radiusBlip = new RadiusBlipController(_logger);

                _lastWantedLevel = SafeGetWantedLevel();
                _lastRecognitionScanTime = Game.GameTime;

                _initialized = true;

                _logger.Info(
                    "module_initialized",
                    "Module de reconnaissance policière initialisé.");

                return true;
            }
            catch (Exception exception)
            {
                _initializationFailed = true;

                try
                {
                    NativeUi.Notify(
                        "~r~Justice avancée : échec d'initialisation " +
                        "du module de reconnaissance.");
                }
                catch
                {
                    // Ignoré.
                }

                if (_logger != null)
                {
                    _logger.Error(
                        "module_initialization_failed",
                        exception);
                }

                return false;
            }
        }

        private static string ResolveWritableDataDirectory(
            string runtimeDirectory,
            string assemblyDirectory)
        {
            string localAppDataDirectory = null;

            try
            {
                localAppDataDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
            }
            catch
            {
                // Je laisse le résolveur essayer ses autres candidats sûrs.
            }

            return ResolveWritableDataDirectoryCore(
                runtimeDirectory,
                assemblyDirectory,
                localAppDataDirectory);
        }

        internal static string ResolveWritableDataDirectoryForBridge()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory =
                string.IsNullOrWhiteSpace(assemblyPath)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : Path.GetDirectoryName(assemblyPath);

            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                assemblyDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            string runtimeDirectory =
                global::DonJEnemySpawner
                    .GetJusticeRecognitionRuntimeDirectorySafe();
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                runtimeDirectory = assemblyDirectory;
            }

            return ResolveWritableDataDirectory(
                runtimeDirectory,
                assemblyDirectory);
        }

        internal static string ResolveWritableDataDirectoryForTests(
            string runtimeDirectory,
            string assemblyDirectory,
            string localAppDataDirectory)
        {
            // Je permets aux tests d'injecter des racines temporaires sans
            // consulter ni modifier le vrai répertoire GTA ou LocalAppData.
            return ResolveWritableDataDirectoryCore(
                runtimeDirectory,
                assemblyDirectory,
                localAppDataDirectory);
        }

        private static string ResolveWritableDataDirectoryCore(
            string runtimeDirectory,
            string assemblyDirectory,
            string localAppDataDirectory)
        {
            List<string> candidates = new List<string>();

            AddUniquePath(
                candidates,
                Path.Combine(
                    runtimeDirectory ?? string.Empty,
                    "DonJJusticeRecognition"));

            if (!string.IsNullOrWhiteSpace(localAppDataDirectory))
            {
                AddUniquePath(
                    candidates,
                    Path.Combine(
                        localAppDataDirectory,
                        "DonJEnemySpawner",
                        "JusticeRecognition"));
            }

            AddUniquePath(
                candidates,
                Path.Combine(
                    assemblyDirectory ?? string.Empty,
                    "DonJJusticeRecognition"));

            for (int index = 0; index < candidates.Count; index++)
            {
                if (TryProbeWritableDirectory(candidates[index]))
                {
                    return candidates[index];
                }
            }

            throw new IOException(
                "Aucun dossier inscriptible pour JusticeRecognition.xml.");
        }

        private static bool TryProbeWritableDirectory(string directory)
        {
            string probePath = null;
            bool writeSucceeded = false;

            try
            {
                Directory.CreateDirectory(directory);

                probePath = Path.Combine(
                    directory,
                    ".donj-recognition-write-probe-" +
                    Guid.NewGuid().ToString("N") +
                    ".tmp");

                using (FileStream stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough))
                {
                    stream.WriteByte(0x5A);
                    stream.Flush(true);
                }

                writeSucceeded = true;
            }
            catch
            {
                writeSucceeded = false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(probePath))
                {
                    try
                    {
                        if (File.Exists(probePath))
                        {
                            File.Delete(probePath);
                        }
                    }
                    catch
                    {
                        // Je refuse un candidat dont le probe ne peut pas être nettoyé.
                        writeSucceeded = false;
                    }
                }
            }

            return writeSucceeded &&
                   (string.IsNullOrWhiteSpace(probePath) ||
                    !File.Exists(probePath));
        }

        private static string ResolveAssetsDirectory(
            string runtimeDirectory,
            string assemblyDirectory)
        {
            List<string> candidates = new List<string>();

            AddUniquePath(
                candidates,
                Path.Combine(
                    runtimeDirectory ?? string.Empty,
                    "Assets",
                    "Justice"));

            AddUniquePath(
                candidates,
                Path.Combine(
                    assemblyDirectory ?? string.Empty,
                    "Assets",
                    "Justice"));

            for (int index = 0; index < candidates.Count; index++)
            {
                string candidate = candidates[index];
                if (File.Exists(Path.Combine(candidate, "immatriculation.png")) &&
                    File.Exists(Path.Combine(candidate, "tenue.png")) &&
                    File.Exists(Path.Combine(candidate, "mandat.png")))
                {
                    return candidate;
                }
            }

            return candidates.Count > 0
                ? candidates[0]
                : string.Empty;
        }

        private static void AddUniquePath(
            List<string> candidates,
            string path)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                if (string.Equals(
                    candidates[index],
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(fullPath);
        }

        private void DrainQueuedCommands(int nowGameTime)
        {
            bool hasEnabledState;
            bool enabledState;

            bool hasRuntimeSuspendedState;
            bool runtimeSuspendedState;

            bool hasActiveProfile;
            string activeProfileId;

            bool suppressWantedLoss;
            string suppressReason;

            BridgeCriticalCommand currentProfileCaptured = null;
            Dictionary<string, BridgeCriticalCommand> profileCaptureReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);
            BridgeCriticalCommand clearCurrentProfile = null;
            Dictionary<string, BridgeCriticalCommand> profileClearReasons =
                new Dictionary<string, BridgeCriticalCommand>(StringComparer.Ordinal);
            BridgeCriticalCommand clearAllProfiles = null;

            lock (_commandSync)
            {
                hasEnabledState = _hasQueuedEnabledState;
                enabledState = _queuedEnabledState;
                _hasQueuedEnabledState = false;

                hasRuntimeSuspendedState =
                    _hasQueuedRuntimeSuspendedState;
                runtimeSuspendedState =
                    _queuedRuntimeSuspendedState;
                _hasQueuedRuntimeSuspendedState = false;

                hasActiveProfile = _hasQueuedActiveProfile;
                activeProfileId = _queuedActiveProfileId;
                _hasQueuedActiveProfile = false;
                _queuedActiveProfileId = null;

                suppressWantedLoss = _queuedSuppressWantedLoss;
                suppressReason = _queuedSuppressReason;
                _queuedSuppressWantedLoss = false;
                _queuedSuppressReason = null;

                if (nowGameTime >= _criticalCommandRetryNotBefore)
                {
                    currentProfileCaptured = _queuedCurrentProfileCaptured;
                    _queuedCurrentProfileCaptured = null;

                    profileCaptureReasons =
                        new Dictionary<string, BridgeCriticalCommand>(
                            _queuedProfileCaptureReasons,
                            StringComparer.Ordinal);
                    _queuedProfileCaptureReasons.Clear();

                    clearCurrentProfile = _queuedClearCurrentProfile;
                    _queuedClearCurrentProfile = null;

                    profileClearReasons =
                        new Dictionary<string, BridgeCriticalCommand>(
                            _queuedProfileClearReasons,
                            StringComparer.Ordinal);
                    _queuedProfileClearReasons.Clear();

                    clearAllProfiles = _queuedClearAllProfiles;
                    _queuedClearAllProfiles = null;
                }
            }

            if (hasEnabledState)
            {
                ApplyEnabledState(enabledState, nowGameTime);
            }

            if (hasRuntimeSuspendedState)
            {
                ApplyRuntimeSuspendedState(runtimeSuspendedState);
            }

            if (hasActiveProfile)
            {
                ApplyActiveProfile(activeProfileId);
            }

            if (suppressWantedLoss)
            {
                ApplySuppressNextWantedLoss(
                    suppressReason,
                    nowGameTime);
            }

            if (clearAllProfiles != null)
            {
                if (!ApplyClearAllProfiles(
                    clearAllProfiles.Reason,
                    clearAllProfiles.CommandId,
                    nowGameTime))
                {
                    RequeueCriticalCommands(
                        currentProfileCaptured,
                        profileCaptureReasons,
                        clearCurrentProfile,
                        profileClearReasons,
                        clearAllProfiles,
                        nowGameTime);
                    return;
                }
            }

            if (currentProfileCaptured != null &&
                !ApplyPlayerCaptured(
                    null,
                    currentProfileCaptured.Reason,
                    currentProfileCaptured.CommandId,
                    nowGameTime))
            {
                RequeueCurrentProfileCapture(
                    currentProfileCaptured,
                    nowGameTime);
            }

            foreach (KeyValuePair<string, BridgeCriticalCommand> capturedProfile in
                     profileCaptureReasons)
            {
                if (!ApplyPlayerCaptured(
                    capturedProfile.Key,
                    capturedProfile.Value.Reason,
                    capturedProfile.Value.CommandId,
                    nowGameTime))
                {
                    RequeueProfileCapture(
                        capturedProfile.Key,
                        capturedProfile.Value,
                        nowGameTime);
                }
            }

            if (clearCurrentProfile != null &&
                !ApplyClearCurrentProfile(
                    clearCurrentProfile.Reason,
                    clearCurrentProfile.CommandId,
                    nowGameTime))
            {
                RequeueCurrentProfileClear(
                    clearCurrentProfile,
                    nowGameTime);
            }

            foreach (KeyValuePair<string, BridgeCriticalCommand> clearProfile in
                     profileClearReasons)
            {
                if (!ApplyClearProfile(
                    clearProfile.Key,
                    clearProfile.Value.Reason,
                    clearProfile.Value.CommandId,
                    nowGameTime))
                {
                    RequeueProfileClear(
                        clearProfile.Key,
                        clearProfile.Value,
                        nowGameTime);
                }
            }
        }

        private void RequeueCriticalCommands(
            BridgeCriticalCommand currentCapture,
            Dictionary<string, BridgeCriticalCommand> profileCaptures,
            BridgeCriticalCommand currentClear,
            Dictionary<string, BridgeCriticalCommand> profileClears,
            BridgeCriticalCommand globalClear,
            int nowGameTime)
        {
            lock (_commandSync)
            {
                KeepNewestCommand(
                    ref _queuedClearAllProfiles,
                    globalClear);
                KeepNewestCommand(
                    ref _queuedCurrentProfileCaptured,
                    currentCapture);
                KeepNewestCommands(
                    _queuedProfileCaptureReasons,
                    profileCaptures);
                KeepNewestCommand(
                    ref _queuedClearCurrentProfile,
                    currentClear);
                KeepNewestCommands(
                    _queuedProfileClearReasons,
                    profileClears);
                ArmCriticalCommandRetry(nowGameTime);
            }
        }

        private void RequeueCurrentProfileCapture(
            BridgeCriticalCommand command,
            int nowGameTime)
        {
            lock (_commandSync)
            {
                KeepNewestCommand(
                    ref _queuedCurrentProfileCaptured,
                    command);
                ArmCriticalCommandRetry(nowGameTime);
            }
        }

        private void RequeueProfileCapture(
            string profileId,
            BridgeCriticalCommand command,
            int nowGameTime)
        {
            lock (_commandSync)
            {
                KeepNewestCommand(
                    _queuedProfileCaptureReasons,
                    profileId,
                    command);
                ArmCriticalCommandRetry(nowGameTime);
            }
        }

        private void RequeueCurrentProfileClear(
            BridgeCriticalCommand command,
            int nowGameTime)
        {
            lock (_commandSync)
            {
                KeepNewestCommand(
                    ref _queuedClearCurrentProfile,
                    command);
                ArmCriticalCommandRetry(nowGameTime);
            }
        }

        private void RequeueProfileClear(
            string profileId,
            BridgeCriticalCommand command,
            int nowGameTime)
        {
            lock (_commandSync)
            {
                KeepNewestCommand(
                    _queuedProfileClearReasons,
                    profileId,
                    command);
                ArmCriticalCommandRetry(nowGameTime);
            }
        }

        private void ArmCriticalCommandRetry(int nowGameTime)
        {
            _criticalCommandRetryNotBefore =
                SafeGameTimeAdd(nowGameTime, 5000);
        }

        private static void KeepNewestCommands(
            Dictionary<string, BridgeCriticalCommand> target,
            Dictionary<string, BridgeCriticalCommand> source)
        {
            foreach (KeyValuePair<string, BridgeCriticalCommand> command in source)
            {
                KeepNewestCommand(
                    target,
                    command.Key,
                    command.Value);
            }
        }

        private static void KeepNewestCommand(
            Dictionary<string, BridgeCriticalCommand> commands,
            string profileId,
            BridgeCriticalCommand command)
        {
            if (command == null)
            {
                return;
            }

            BridgeCriticalCommand existing;
            if (!commands.TryGetValue(profileId, out existing) ||
                existing == null ||
                existing.CommandId <= command.CommandId)
            {
                commands[profileId] = command;
            }
        }

        private static void KeepNewestCommand(
            ref BridgeCriticalCommand target,
            BridgeCriticalCommand command)
        {
            if (command != null &&
                (target == null ||
                 target.CommandId <= command.CommandId))
            {
                target = command;
            }
        }

        private void ApplyEnabledState(bool enabled, int nowGameTime)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;

            _currentEpisode = null;
            _pendingWantedLoss = null;
            _pendingWantedEscalation = null;
            _observerExposures.Clear();
            _insideSearchZone = false;
            ClearWantedLossSuppression();

            _lastWantedLevel = SafeGetWantedLevel();

            if (!enabled)
            {
                /*
                 * Important :
                 * la désactivation n'efface aucune donnée persistée.
                 * Ce n'est pas une amnistie.
                 */
                RemoveSearchZoneBlip();

                _logger.Info(
                    "module_disabled",
                    "Reconnaissance policière désactivée sans amnistie.");
            }
            else
            {
                EnsureSearchZoneBlip(DateTime.UtcNow);

                _logger.Info(
                    "module_enabled",
                    "Reconnaissance policière réactivée.");
            }

            _store.MarkDirty(_saveData, nowGameTime);
            RefreshStatusLinesCache();
        }

        private void ApplyRuntimeSuspendedState(bool suspended)
        {
            if (_runtimeSuspended == suspended)
            {
                return;
            }

            _runtimeSuspended = suspended;
            ResetCurrentProfileRuntimeState();
            _lastWantedLevel = SafeGetWantedLevel();

            if (suspended)
            {
                RemoveSearchZoneBlip();
            }
            else if (_enabled)
            {
                EnsureSearchZoneBlip(DateTime.UtcNow);
            }

            _logger.Info(
                suspended
                    ? "module_runtime_suspended"
                    : "module_runtime_resumed",
                suspended
                    ? "Reconnaissance suspendue pendant une transition Justice."
                    : "Reconnaissance reprise après la transition Justice.");

            RefreshStatusLinesCache();
        }

        private void ApplyActiveProfile(string profileId)
        {
            string normalizedProfileId = NormalizeProfileId(profileId);
            if (string.Equals(
                _authoritativeProfileId,
                normalizedProfileId,
                StringComparison.Ordinal))
            {
                return;
            }

            _authoritativeProfileId = normalizedProfileId;
            PauseForUnknownProfile();
        }

        private void ApplySuppressNextWantedLoss(
            string reason,
            int nowGameTime)
        {
            if (_pendingWantedLoss != null)
            {
                _pendingWantedLoss.Suppressed = true;
                _pendingWantedLoss.Reason =
                    SafeReason(reason);

                ClearWantedLossSuppression();
            }
            else if (_currentEpisode != null &&
                     (_lastWantedLevel > 0 || SafeGetWantedLevel() > 0))
            {
                _suppressNextWantedLoss = true;
                _suppressNextWantedLossReason = SafeReason(reason);
                _suppressedWantedLossEpisodeId =
                    _currentEpisode.EpisodeId;
                _suppressWantedLossUntil =
                    SafeGameTimeAdd(nowGameTime, 2500);
            }
            else
            {
                ClearWantedLossSuppression();
                _logger.Info(
                    "wanted_loss_suppression_ignored",
                    "Aucune poursuite courante à supprimer : " +
                    SafeReason(reason));
                return;
            }

            _logger.Info(
                "wanted_loss_suppression_armed",
                "Suppression de la prochaine perte d'étoiles : " +
                SafeReason(reason));
        }

        private bool ApplyPlayerCaptured(
            string profileId,
            string reason,
            long commandId,
            int nowGameTime)
        {
            string targetProfileId =
                NormalizeProfileId(profileId) ??
                NormalizeProfileId(_currentProfileId);

            RecognitionProfileData targetProfile =
                FindProfile(targetProfileId);

            if (targetProfile == null)
            {
                /*
                 * Je considère un profil canonique absent comme déjà nettoyé,
                 * mais seulement après avoir republié l'état courant puis
                 * acquitté durablement l'intention critique.
                 */
                return CompleteAbsentProfileCommand(
                    targetProfileId,
                    reason,
                    commandId,
                    nowGameTime,
                    true,
                    NormalizeProfileId(profileId) == null);
            }

            bool persisted = false;

            ClearProfileRecognitionData(targetProfile);
            _store.MarkDirty(_saveData, nowGameTime);
            persisted = _store.ForceSave(_saveData);

            if (string.Equals(
                targetProfileId,
                _currentProfileId,
                StringComparison.Ordinal))
            {
                ResetCurrentProfileRuntimeState();
                RemoveSearchZoneBlip();
            }

            ClearWantedLossSuppression();
            RefreshStatusLinesCache();

            bool acknowledged =
                persisted &&
                JusticeRecognitionBridge.AcknowledgePlayerCaptured(
                    profileId,
                    commandId);

            _logger.Info(
                acknowledged
                    ? "player_capture_persisted"
                    : "player_capture_persistence_pending",
                (acknowledged
                    ? "Indices transitoires persistés après capture pour "
                    : "Indices supprimés en mémoire, persistance à réessayer pour ") +
                SafeReason(targetProfileId) + " : " +
                SafeReason(reason));

            return acknowledged;
        }

        private bool ApplyClearCurrentProfile(
            string reason,
            long commandId,
            int nowGameTime)
        {
            return ApplyClearProfileCore(
                _currentProfileId,
                reason,
                commandId,
                nowGameTime,
                true);
        }

        private bool ApplyClearProfile(
            string profileId,
            string reason,
            long commandId,
            int nowGameTime)
        {
            return ApplyClearProfileCore(
                profileId,
                reason,
                commandId,
                nowGameTime,
                false);
        }

        private bool ApplyClearProfileCore(
            string profileId,
            string reason,
            long commandId,
            int nowGameTime,
            bool acknowledgeCurrentCommand)
        {
            string targetProfileId = NormalizeProfileId(profileId);
            RecognitionProfileData targetProfile =
                FindProfile(targetProfileId);

            if (targetProfile == null)
            {
                // Je termine aussi un clear déjà satisfait sans créer un profil
                // vide uniquement pour pouvoir supprimer son intention.
                return CompleteAbsentProfileCommand(
                    targetProfileId,
                    reason,
                    commandId,
                    nowGameTime,
                    false,
                    acknowledgeCurrentCommand);
            }

            ClearProfileRecognitionData(targetProfile);
            _store.MarkDirty(_saveData, nowGameTime);
            bool persisted = _store.ForceSave(_saveData);

            if (string.Equals(
                targetProfileId,
                _currentProfileId,
                StringComparison.Ordinal))
            {
                ResetCurrentProfileRuntimeState();
                RemoveSearchZoneBlip();
            }

            RefreshStatusLinesCache();

            bool acknowledged =
                persisted &&
                JusticeRecognitionBridge.AcknowledgeProfileClear(
                    acknowledgeCurrentCommand ? null : profileId,
                    commandId);

            _logger.Info(
                acknowledged
                    ? "profile_clear_persisted"
                    : "profile_clear_persistence_pending",
                "Profil " + targetProfileId +
                (acknowledged
                    ? " effacé et persisté : "
                    : " effacé en mémoire, persistance à réessayer : ") +
                SafeReason(reason));

            return acknowledged;
        }

        private bool CompleteAbsentProfileCommand(
            string targetProfileId,
            string reason,
            long commandId,
            int nowGameTime,
            bool playerCaptured,
            bool acknowledgeCurrentCommand)
        {
            if (NormalizeProfileId(targetProfileId) == null ||
                _saveData == null ||
                _store == null)
            {
                return false;
            }

            // Je publie d'abord l'absence dans le primaire et son backup : le
            // journal critique ne peut être acquitté avant cette preuve durable.
            _store.MarkDirty(_saveData, nowGameTime);
            bool persisted = _store.ForceSave(_saveData);

            bool acknowledged =
                persisted &&
                (playerCaptured
                    ? JusticeRecognitionBridge.AcknowledgePlayerCaptured(
                        acknowledgeCurrentCommand ? null : targetProfileId,
                        commandId)
                    : JusticeRecognitionBridge.AcknowledgeProfileClear(
                        acknowledgeCurrentCommand ? null : targetProfileId,
                        commandId));

            _logger.Info(
                acknowledged
                    ? "absent_profile_command_persisted"
                    : "absent_profile_command_persistence_pending",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Commande={0}; Profil={1}; Etat={2}; Raison={3}",
                    playerCaptured ? "capture" : "clear",
                    targetProfileId,
                    acknowledged ? "déjà nettoyé" : "à réessayer",
                    SafeReason(reason)));

            return acknowledged;
        }

        private bool ApplyClearAllProfiles(
            string reason,
            long commandId,
            int nowGameTime)
        {
            int index;
            bool persisted = false;

            if (_saveData != null)
            {
                if (_saveData.Profiles != null)
                {
                    for (index = 0;
                         index < _saveData.Profiles.Count;
                         index++)
                    {
                        RecognitionProfileData profile =
                            _saveData.Profiles[index];

                        if (profile != null)
                        {
                            ClearProfileRecognitionData(profile);
                        }
                    }
                }

                _store.MarkDirty(_saveData, nowGameTime);
                persisted = _store.ForceSave(_saveData);
            }

            _currentEpisode = null;
            _pendingWantedLoss = null;
            _pendingWantedEscalation = null;
            _observerExposures.Clear();
            _insideSearchZone = false;
            ClearWantedLossSuppression();

            RemoveSearchZoneBlip();
            RefreshStatusLinesCache();

            bool acknowledged =
                persisted &&
                JusticeRecognitionBridge.AcknowledgeGlobalClear(commandId);

            _logger.Info(
                acknowledged
                    ? "all_profiles_clear_persisted"
                    : "all_profiles_clear_persistence_pending",
                (acknowledged
                    ? "Tous les profils ont été effacés et persistés : "
                    : "Tous les profils sont effacés en mémoire, persistance à réessayer : ") +
                SafeReason(reason));

            return acknowledged;
        }

        private static void ClearProfileRecognitionData(
            RecognitionProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.VehicleEvidence == null)
            {
                profile.VehicleEvidence =
                    new List<VehicleEvidenceState>();
            }
            else
            {
                profile.VehicleEvidence.Clear();
            }

            if (profile.OutfitEvidence == null)
            {
                profile.OutfitEvidence =
                    new List<OutfitEvidenceState>();
            }
            else
            {
                profile.OutfitEvidence.Clear();
            }

            profile.AppearanceEvidence =
                new AppearanceEvidenceState();

            profile.SearchZone =
                new SearchZoneState();
        }

        private RecognitionProfileData FindProfile(string profileId)
        {
            string normalizedProfileId = NormalizeProfileId(profileId);
            if (normalizedProfileId == null ||
                _saveData == null ||
                _saveData.Profiles == null)
            {
                return null;
            }

            for (int index = 0;
                 index < _saveData.Profiles.Count;
                 index++)
            {
                RecognitionProfileData profile =
                    _saveData.Profiles[index];

                if (profile != null &&
                    string.Equals(
                        profile.ProfileId,
                        normalizedProfileId,
                        StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            return null;
        }

        private void ResetCurrentProfileRuntimeState()
        {
            _currentEpisode = null;
            _pendingWantedLoss = null;
            _pendingWantedEscalation = null;
            _observerExposures.Clear();
            _insideSearchZone = false;
            ClearWantedLossSuppression();
        }

        private void ClearWantedLossSuppression()
        {
            _suppressNextWantedLoss = false;
            _suppressNextWantedLossReason = null;
            _suppressedWantedLossEpisodeId = 0L;
            _suppressWantedLossUntil = 0;
        }

        private bool UpdateActiveProfile(
            Ped playerPed,
            int nowGameTime)
        {
            string modelProfileId =
                ResolveProfileId(playerPed);

            string profileId =
                NormalizeProfileId(_authoritativeProfileId);

            if (profileId != null &&
                modelProfileId != null &&
                !string.Equals(
                    profileId,
                    modelProfileId,
                    StringComparison.Ordinal))
            {
                PauseForUnknownProfile();
                return false;
            }

            if (profileId != null && IsPlayerSwitchInProgress())
            {
                PauseForUnknownProfile();
                return false;
            }

            if (profileId == null)
            {
                profileId = modelProfileId;
            }

            if (string.IsNullOrWhiteSpace(profileId))
            {
                /*
                 * Le Ped est probablement transitoire pendant un changement
                 * de personnage. On ne rattache surtout pas ses données au
                 * mauvais protagoniste.
                */
                PauseForUnknownProfile();
                return false;
            }

            if (string.Equals(
                profileId,
                _currentProfileId,
                StringComparison.Ordinal))
            {
                return true;
            }

            string previousProfileId =
                _currentProfileId;

            _currentEpisode = null;
            _pendingWantedLoss = null;
            _pendingWantedEscalation = null;
            _observerExposures.Clear();
            _insideSearchZone = false;
            _identityCache = null;
            _identityCachePedHandle = 0;
            _hasLastPlayerPosition = false;
            ClearWantedLossSuppression();

            RemoveSearchZoneBlip();

            _currentProfileId = profileId;
            _currentProfile =
                _saveData.GetOrCreateProfile(profileId);

            RecognitionDataSanitizer.SanitizeProfile(
                _currentProfile,
                DateTime.UtcNow,
                _logger);

            _lastWantedLevel = SafeGetWantedLevel();

            _store.MarkDirty(_saveData, nowGameTime);

            if (_enabled)
            {
                EnsureSearchZoneBlip(DateTime.UtcNow);
            }

            _logger.Info(
                "profile_changed",
                "Profil changé : " +
                SafeReason(previousProfileId) +
                " -> " +
                profileId);

            return true;
        }

        private void PauseForUnknownProfile()
        {
            _currentEpisode = null;
            _pendingWantedLoss = null;
            _pendingWantedEscalation = null;
            _observerExposures.Clear();
            _insideSearchZone = false;
            _identityCache = null;
            _identityCachePedHandle = 0;
            _hasLastPlayerPosition = false;
            _currentProfile = null;
            _currentProfileId = null;
            ClearWantedLossSuppression();
            SetActiveSearchZoneStatusCache(false);

            RemoveSearchZoneBlip();
        }

        private static string ResolveProfileId(Ped playerPed)
        {
            if (!EntityExists(playerPed))
            {
                return null;
            }

            int modelHash;

            try
            {
                modelHash = playerPed.Model.Hash;
            }
            catch
            {
                return null;
            }

            if (modelHash == Game.GenerateHash("player_zero"))
            {
                return "Michael";
            }

            if (modelHash == Game.GenerateHash("player_one"))
            {
                return "Franklin";
            }

            if (modelHash == Game.GenerateHash("player_two"))
            {
                return "Trevor";
            }

            return null;
        }

        private void ProcessWantedState(
            Ped playerPed,
            int currentWanted,
            int nowGameTime,
            DateTime nowUtc)
        {
            if (_pendingWantedLoss != null)
            {
                if (currentWanted > 0)
                {
                    /*
                     * Le wanted est revenu avant le délai de stabilisation :
                     * ce n'était qu'un bref passage à zéro.
                     */
                    _pendingWantedLoss = null;

                    if (_currentEpisode != null)
                    {
                        UpdatePursuitEpisode(
                            playerPed,
                            currentWanted,
                            nowGameTime);
                    }

                    _lastWantedLevel = currentWanted;
                    ProcessPendingWantedEscalation(
                        playerPed,
                        currentWanted,
                        nowGameTime,
                        nowUtc);

                    return;
                }

                if (nowGameTime >=
                    _pendingWantedLoss.FinalizeAtGameTime)
                {
                    FinalizeWantedLoss(
                        playerPed,
                        nowGameTime,
                        nowUtc);
                }

                _lastWantedLevel = currentWanted;
                return;
            }

            if (currentWanted > 0)
            {
                if (_lastWantedLevel <= 0 ||
                    _currentEpisode == null)
                {
                    BeginPursuitEpisode(
                        playerPed,
                        currentWanted,
                        nowGameTime);
                }
                else
                {
                    UpdatePursuitEpisode(
                        playerPed,
                        currentWanted,
                        nowGameTime);
                }

                ProcessPendingWantedEscalation(
                    playerPed,
                    currentWanted,
                    nowGameTime,
                    nowUtc);
            }
            else if (_lastWantedLevel > 0 &&
                     _currentEpisode != null)
            {
                BeginPendingWantedLoss(
                    playerPed,
                    nowGameTime);
            }

            _lastWantedLevel = currentWanted;
        }

        private void BeginPursuitEpisode(
            Ped playerPed,
            int currentWanted,
            int nowGameTime)
        {
            _currentProfile.LastEpisodeId =
                Math.Max(
                    0L,
                    _currentProfile.LastEpisodeId) + 1L;

            IdentitySnapshot identity =
                GetCurrentIdentity(
                    playerPed,
                    nowGameTime,
                    true);

            _currentEpisode =
                new PursuitEpisodeRuntime
                {
                    EpisodeId =
                        _currentProfile.LastEpisodeId,

                    PeakWantedLevel =
                        RecognitionPolicy.ClampWantedLevel(
                            currentWanted),

                    LastKnownPosition =
                        PositionData.FromVector3(
                            playerPed.Position),

                    LastVehicle =
                        identity != null
                            ? CloneVehicle(identity.Vehicle)
                            : null,

                    LastOutfit =
                        identity != null
                            ? CloneOutfit(identity.Outfit)
                            : null,

                    LastAppearance =
                        identity != null
                            ? CloneAppearance(identity.Appearance)
                            : null,

                    StartedAtGameTime =
                        nowGameTime
                };

            _nextPursuitCapture =
                SafeGameTimeAdd(
                    nowGameTime,
                    PursuitCaptureMilliseconds);

            if (nowGameTime >= _skipNaturalEscalationUntil &&
                nowGameTime >= _wantedWriteGuardUntil)
            {
                _pendingWantedEscalation =
                    new PendingWantedEscalationRuntime
                    {
                        EpisodeId =
                            _currentEpisode.EpisodeId,

                        EvaluateAtGameTime =
                            SafeGameTimeAdd(
                                nowGameTime,
                                WantedTransitionStabilizationMilliseconds)
                    };
            }
            else
            {
                _pendingWantedEscalation = null;
            }

            _logger.Info(
                "pursuit_episode_started",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Episode={0}; Wanted={1}; Profil={2}",
                    _currentEpisode.EpisodeId,
                    _currentEpisode.PeakWantedLevel,
                    _currentProfileId));
        }

        private void UpdatePursuitEpisode(
            Ped playerPed,
            int currentWanted,
            int nowGameTime)
        {
            if (_currentEpisode == null)
            {
                return;
            }

            int clampedWanted =
                RecognitionPolicy.ClampWantedLevel(
                    currentWanted);

            if (clampedWanted >
                _currentEpisode.PeakWantedLevel)
            {
                _currentEpisode.PeakWantedLevel =
                    clampedWanted;

                _logger.Info(
                    "pursuit_peak_changed",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Episode={0}; Peak={1}",
                        _currentEpisode.EpisodeId,
                        _currentEpisode.PeakWantedLevel));
            }

            _currentEpisode.LastKnownPosition =
                PositionData.FromVector3(
                    playerPed.Position);

            if (nowGameTime < _nextPursuitCapture)
            {
                return;
            }

            _nextPursuitCapture =
                SafeGameTimeAdd(
                    nowGameTime,
                    PursuitCaptureMilliseconds);

            IdentitySnapshot identity =
                GetCurrentIdentity(
                    playerPed,
                    nowGameTime,
                    false);

            if (identity == null)
            {
                return;
            }

            if (identity.Vehicle != null &&
                identity.Vehicle.IsValid)
            {
                _currentEpisode.LastVehicle =
                    CloneVehicle(identity.Vehicle);
            }

            if (identity.Outfit != null &&
                identity.Outfit.IsValid)
            {
                _currentEpisode.LastOutfit =
                    CloneOutfit(identity.Outfit);
            }

            if (identity.Appearance != null &&
                identity.Appearance.IsValid)
            {
                _currentEpisode.LastAppearance =
                    CloneAppearance(identity.Appearance);
            }
        }

        private void BeginPendingWantedLoss(
            Ped playerPed,
            int nowGameTime)
        {
            bool suppressed = false;
            string reason = null;

            if (_suppressNextWantedLoss &&
                _currentEpisode != null &&
                _suppressedWantedLossEpisodeId ==
                    _currentEpisode.EpisodeId &&
                nowGameTime <= _suppressWantedLossUntil)
            {
                suppressed = true;
                reason = _suppressNextWantedLossReason;
            }

            ClearWantedLossSuppression();

            if (IsPlayerDead(playerPed))
            {
                suppressed = true;
                reason = "mort du joueur";
            }
            else if (IsPlayerBeingArrested())
            {
                suppressed = true;
                reason = "arrestation";
            }
            else if (IsPlayerSwitchInProgress())
            {
                suppressed = true;
                reason = "changement de personnage";
            }
            else if (HasRecentTeleport(playerPed))
            {
                suppressed = true;
                reason = "déplacement ou téléportation interne";
            }

            _pendingWantedLoss =
                new PendingWantedLossRuntime
                {
                    FinalizeAtGameTime =
                        SafeGameTimeAdd(
                            nowGameTime,
                            WantedLossStabilizationMilliseconds),

                    Suppressed = suppressed,
                    Reason = SafeReason(reason)
                };

            _pendingWantedEscalation = null;
        }

        private void FinalizeWantedLoss(
            Ped playerPed,
            int nowGameTime,
            DateTime nowUtc)
        {
            PendingWantedLossRuntime pending =
                _pendingWantedLoss;

            _pendingWantedLoss = null;

            if (_currentEpisode == null)
            {
                return;
            }

            if (pending != null &&
                pending.Suppressed)
            {
                _logger.Info(
                    "wanted_loss_suppressed",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Episode={0}; Raison={1}",
                        _currentEpisode.EpisodeId,
                        SafeReason(pending.Reason)));

                _currentEpisode = null;
                return;
            }

            if (IsPlayerDead(playerPed) ||
                IsPlayerBeingArrested() ||
                IsPlayerSwitchInProgress())
            {
                _logger.Info(
                    "wanted_loss_suppressed",
                    "La perte des étoiles n'est pas une fuite valide.");

                _currentEpisode = null;
                return;
            }

            CreateEvidenceFromSuccessfulEscape(
                _currentEpisode,
                playerPed,
                nowGameTime,
                nowUtc);

            _currentEpisode = null;
        }

        private void CreateEvidenceFromSuccessfulEscape(
            PursuitEpisodeRuntime episode,
            Ped playerPed,
            int nowGameTime,
            DateTime nowUtc)
        {
            if (episode == null ||
                _currentProfile == null)
            {
                return;
            }

            int wantedFloor =
                RecognitionPolicy.ClampWantedLevel(
                    episode.PeakWantedLevel);

            if (episode.LastVehicle != null &&
                episode.LastVehicle.IsValid)
            {
                UpsertVehicleEvidence(
                    episode,
                    wantedFloor,
                    nowUtc);
            }

            if (episode.LastOutfit != null &&
                episode.LastOutfit.IsValid)
            {
                UpsertOutfitEvidence(
                    episode,
                    wantedFloor,
                    nowUtc);
            }

            AppearanceSignatureData appearance =
                episode.LastAppearance;

            if (appearance == null ||
                !appearance.IsValid)
            {
                IdentitySnapshot currentIdentity =
                    GetCurrentIdentity(
                        playerPed,
                        nowGameTime,
                        true);

                if (currentIdentity != null)
                {
                    appearance =
                        currentIdentity.Appearance;
                }
            }

            _currentProfile.AppearanceEvidence =
                new AppearanceEvidenceState
                {
                    Active =
                        appearance != null &&
                        appearance.IsValid,

                    SourceEpisodeId =
                        episode.EpisodeId,

                    Signature =
                        CloneAppearance(appearance),

                    OutfitReference =
                        CloneOutfit(
                            episode.LastOutfit)
                };

            int zoneFloor = wantedFloor;

            if (_currentProfile.SearchZone != null &&
                _currentProfile.SearchZone.Active &&
                _currentProfile.SearchZone.ExpiresUtc > nowUtc)
            {
                zoneFloor =
                    Math.Max(
                        zoneFloor,
                        _currentProfile.SearchZone.WantedFloor);
            }

            zoneFloor =
                RecognitionPolicy.ClampWantedLevel(
                    zoneFloor);

            PositionData center =
                episode.LastKnownPosition;

            if (center == null ||
                !center.IsFinite())
            {
                center =
                    PositionData.FromVector3(
                        playerPed.Position);
            }

            _currentProfile.SearchZone =
                new SearchZoneState
                {
                    Active = true,

                    SourceEpisodeId =
                        episode.EpisodeId,

                    WantedFloor =
                        zoneFloor,

                    Center =
                        center.Clone(),

                    Radius =
                        RecognitionPolicy.GetZoneRadius(
                            zoneFloor),

                    CreatedUtc =
                        nowUtc,

                    ExpiresUtc =
                        nowUtc.AddSeconds(
                            RecognitionPolicy.GetZoneDurationSeconds(
                                zoneFloor)),

                    GraceUntilUtc =
                        nowUtc.AddSeconds(
                            RecognitionPolicy.ZoneGraceSeconds),

                    LastRecognitionUtc =
                        DateTime.MinValue
                };

            _observerExposures.Clear();
            _insideSearchZone = false;

            _store.MarkDirty(
                _saveData,
                nowGameTime);

            RecreateSearchZoneBlip(nowUtc);
            RefreshStatusLinesCache();

            NativeUi.Notify(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "~b~Recherche active~s~ : niveau mémorisé {0}.",
                    zoneFloor));

            _logger.Info(
                "search_evidence_created",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Episode={0}; WantedFloor={1}; Rayon={2:0}",
                    episode.EpisodeId,
                    zoneFloor,
                    _currentProfile.SearchZone.Radius));
        }

        private void UpsertVehicleEvidence(
            PursuitEpisodeRuntime episode,
            int wantedFloor,
            DateTime nowUtc)
        {
            EnsureEvidenceCollections(_currentProfile);

            VehicleSignatureData signature =
                CloneVehicle(episode.LastVehicle);

            VehicleEvidenceState previous = null;
            int index;

            for (index = 0;
                 index < _currentProfile.VehicleEvidence.Count;
                 index++)
            {
                VehicleEvidenceState candidate =
                    _currentProfile.VehicleEvidence[index];

                if (candidate == null ||
                    candidate.Signature == null)
                {
                    continue;
                }

                if (VehicleSignatureComparer.IsSamePersistentIdentity(
                    candidate.Signature,
                    signature))
                {
                    previous = candidate;
                    break;
                }
            }

            if (previous != null)
            {
                wantedFloor =
                    Math.Max(
                        wantedFloor,
                        previous.WantedFloor);

                _currentProfile.VehicleEvidence.Remove(
                    previous);
            }

            VehicleEvidenceState state =
                new VehicleEvidenceState
                {
                    Active = true,
                    SourceEpisodeId =
                        episode.EpisodeId,
                    WantedFloor =
                        RecognitionPolicy.ClampWantedLevel(
                            wantedFloor),
                    CreatedUtc =
                        nowUtc,
                    ExpiresUtc =
                        nowUtc.AddSeconds(
                            RecognitionPolicy.GetVehicleEvidenceDurationSeconds(
                                wantedFloor)),
                    Neutralized =
                        false,
                    NeutralizationNotified =
                        false,
                    Signature =
                        signature
                };

            _currentProfile.VehicleEvidence.Add(state);

            TrimVehicleEvidence(
                _currentProfile.VehicleEvidence);
        }

        private void UpsertOutfitEvidence(
            PursuitEpisodeRuntime episode,
            int wantedFloor,
            DateTime nowUtc)
        {
            EnsureEvidenceCollections(_currentProfile);

            OutfitSignatureData signature =
                CloneOutfit(episode.LastOutfit);

            OutfitEvidenceState previous = null;
            int index;

            for (index = 0;
                 index < _currentProfile.OutfitEvidence.Count;
                 index++)
            {
                OutfitEvidenceState candidate =
                    _currentProfile.OutfitEvidence[index];

                if (candidate == null ||
                    candidate.Signature == null)
                {
                    continue;
                }

                float difference =
                    OutfitSignatureComparer.GetDifferenceScore(
                        candidate.Signature,
                        signature);

                if (difference <= 0.001f)
                {
                    previous = candidate;
                    break;
                }
            }

            if (previous != null)
            {
                wantedFloor =
                    Math.Max(
                        wantedFloor,
                        previous.WantedFloor);

                _currentProfile.OutfitEvidence.Remove(
                    previous);
            }

            OutfitEvidenceState state =
                new OutfitEvidenceState
                {
                    Active = true,
                    SourceEpisodeId =
                        episode.EpisodeId,
                    WantedFloor =
                        RecognitionPolicy.ClampWantedLevel(
                            wantedFloor),
                    CreatedUtc =
                        nowUtc,
                    ExpiresUtc =
                        nowUtc.AddSeconds(
                            RecognitionPolicy.GetOutfitEvidenceDurationSeconds(
                                wantedFloor)),
                    Signature =
                        signature
                };

            _currentProfile.OutfitEvidence.Add(state);

            TrimOutfitEvidence(
                _currentProfile.OutfitEvidence);
        }

        private static void TrimVehicleEvidence(
            List<VehicleEvidenceState> evidence)
        {
            while (evidence.Count >
                   RecognitionPolicy.MaximumVehicleEvidenceRecords)
            {
                int oldestIndex = FindOldestVehicleEvidenceIndex(
                    evidence);

                if (oldestIndex < 0)
                {
                    evidence.RemoveAt(0);
                }
                else
                {
                    evidence.RemoveAt(oldestIndex);
                }
            }
        }

        private static void TrimOutfitEvidence(
            List<OutfitEvidenceState> evidence)
        {
            while (evidence.Count >
                   RecognitionPolicy.MaximumOutfitEvidenceRecords)
            {
                int oldestIndex = FindOldestOutfitEvidenceIndex(
                    evidence);

                if (oldestIndex < 0)
                {
                    evidence.RemoveAt(0);
                }
                else
                {
                    evidence.RemoveAt(oldestIndex);
                }
            }
        }

        private static int FindOldestVehicleEvidenceIndex(
            List<VehicleEvidenceState> evidence)
        {
            int oldestIndex = -1;
            DateTime oldestDate = DateTime.MaxValue;
            int index;

            for (index = 0;
                 index < evidence.Count;
                 index++)
            {
                VehicleEvidenceState state =
                    evidence[index];

                if (state != null &&
                    state.CreatedUtc < oldestDate)
                {
                    oldestDate = state.CreatedUtc;
                    oldestIndex = index;
                }
            }

            return oldestIndex;
        }

        private static int FindOldestOutfitEvidenceIndex(
            List<OutfitEvidenceState> evidence)
        {
            int oldestIndex = -1;
            DateTime oldestDate = DateTime.MaxValue;
            int index;

            for (index = 0;
                 index < evidence.Count;
                 index++)
            {
                OutfitEvidenceState state =
                    evidence[index];

                if (state != null &&
                    state.CreatedUtc < oldestDate)
                {
                    oldestDate = state.CreatedUtc;
                    oldestIndex = index;
                }
            }

            return oldestIndex;
        }

        private void ProcessPendingWantedEscalation(
            Ped playerPed,
            int currentWanted,
            int nowGameTime,
            DateTime nowUtc)
        {
            if (_pendingWantedEscalation == null ||
                _currentEpisode == null)
            {
                return;
            }

            if (_pendingWantedEscalation.EpisodeId !=
                _currentEpisode.EpisodeId)
            {
                _pendingWantedEscalation = null;
                return;
            }

            if (nowGameTime <
                _pendingWantedEscalation.EvaluateAtGameTime)
            {
                return;
            }

            int attemptCount =
                _pendingWantedEscalation.AttemptCount;
            _pendingWantedEscalation = null;

            IdentitySnapshot identity =
                GetCurrentIdentity(
                    playerPed,
                    nowGameTime,
                    false);

            if (identity == null)
            {
                return;
            }

            bool evidenceChanged;
            int vehicleFloor =
                GetMatchingVehicleWantedFloor(
                    identity.Vehicle,
                    nowUtc,
                    true,
                    out evidenceChanged);

            int outfitFloor =
                GetMatchingOutfitWantedFloor(
                    identity.Outfit,
                    nowUtc);

            int targetWanted =
                Math.Max(
                    currentWanted,
                    Math.Max(
                        vehicleFloor,
                        outfitFloor));

            targetWanted =
                RecognitionPolicy.ClampWantedLevel(
                    targetWanted);

            if (evidenceChanged)
            {
                _store.MarkDirty(
                    _saveData,
                    nowGameTime);
            }

            if (targetWanted <= currentWanted)
            {
                return;
            }

            string cause;

            if (vehicleFloor > 0 &&
                outfitFloor > 0)
            {
                cause =
                    "plaque et tenue reconnues";
            }
            else if (vehicleFloor > 0)
            {
                cause =
                    "véhicule signalé";
            }
            else
            {
                cause =
                    "tenue reconnue";
            }

            if (!ApplyWantedMinimum(
                targetWanted,
                cause,
                nowGameTime))
            {
                if (attemptCount < 2 &&
                    _currentEpisode != null &&
                    currentWanted > 0)
                {
                    _pendingWantedEscalation =
                        new PendingWantedEscalationRuntime
                        {
                            EpisodeId = _currentEpisode.EpisodeId,
                            EvaluateAtGameTime = SafeGameTimeAdd(
                                nowGameTime,
                                500),
                            AttemptCount = attemptCount + 1
                        };
                }

                return;
            }

            NativeUi.Notify(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "~b~Identification policière~s~ : " +
                    "niveau de recherche restauré à {0}.",
                    targetWanted));
        }

        private bool ApplyWantedMinimum(
            int targetWanted,
            string cause,
            int nowGameTime)
        {
            targetWanted =
                RecognitionPolicy.ClampWantedLevel(
                    targetWanted);

            int currentWanted =
                SafeGetWantedLevel();

            if (targetWanted <= currentWanted)
            {
                return false;
            }

            WantedMinimumApplicationResult application =
                JusticeRecognitionBridge.ApplyWantedMinimumAtomically(
                    targetWanted);

            if (!application.HandlerPresent)
            {
                _logger.Info(
                    "wanted_write_unavailable",
                    "Le setter Justice est absent : hausse wanted refusée par sécurité.");
                return false;
            }

            if (!application.Applied)
            {
                _logger.Info(
                    "wanted_write_rejected",
                    "Le setter Justice a refusé la hausse wanted.");
                return false;
            }

            _wantedWriteGuardUntil =
                SafeGameTimeAdd(
                    nowGameTime,
                    WantedWriteGuardMilliseconds);

            _skipNaturalEscalationUntil =
                SafeGameTimeAdd(
                    nowGameTime,
                    WantedWriteGuardMilliseconds);

            // Le setter Justice a confirmé ce plancher : je relève le cache de
            // lecture sans modifier l'état qui détecte les transitions GTA.
            _lastReliableWantedLevel =
                Math.Max(
                    _lastReliableWantedLevel,
                    targetWanted);

            _logger.Info(
                "wanted_minimum_applied",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Profil={0}; Avant={1}; Cible={2}; Cause={3}",
                    _currentProfileId,
                    currentWanted,
                    targetWanted,
                    SafeReason(cause)));

            return true;
        }

        private void CheckVehicleRepaint(
            Ped playerPed,
            int nowGameTime,
            DateTime nowUtc)
        {
            if (_currentProfile == null ||
                _currentProfile.VehicleEvidence == null ||
                _currentProfile.VehicleEvidence.Count == 0)
            {
                return;
            }

            IdentitySnapshot identity =
                GetCurrentIdentity(
                    playerPed,
                    nowGameTime,
                    false);

            if (identity == null ||
                identity.Vehicle == null ||
                !identity.Vehicle.IsValid)
            {
                return;
            }

            bool changed;
            GetMatchingVehicleWantedFloor(
                identity.Vehicle,
                nowUtc,
                true,
                out changed);

            if (changed)
            {
                _store.MarkDirty(
                    _saveData,
                    nowGameTime);
            }
        }

        private int GetMatchingVehicleWantedFloor(
            VehicleSignatureData currentVehicle,
            DateTime nowUtc,
            bool allowNeutralization,
            out bool evidenceChanged)
        {
            evidenceChanged = false;

            if (_currentProfile == null ||
                currentVehicle == null ||
                !currentVehicle.IsValid ||
                _currentProfile.VehicleEvidence == null)
            {
                return 0;
            }

            int maximumFloor = 0;
            bool notified = false;
            int index;

            for (index = 0;
                 index < _currentProfile.VehicleEvidence.Count;
                 index++)
            {
                VehicleEvidenceState evidence =
                    _currentProfile.VehicleEvidence[index];

                if (!IsVehicleEvidenceUsable(
                    evidence,
                    nowUtc))
                {
                    continue;
                }

                VehicleMatchKind matchKind =
                    VehicleSignatureComparer.Compare(
                        evidence.Signature,
                        currentVehicle);

                if (matchKind == VehicleMatchKind.Exact)
                {
                    if (!evidence.Neutralized)
                    {
                        maximumFloor =
                            Math.Max(
                                maximumFloor,
                                evidence.WantedFloor);
                    }
                }
                else if (matchKind ==
                         VehicleMatchKind.SameIdentityDifferentPaint &&
                         allowNeutralization &&
                         !evidence.Neutralized)
                {
                    evidence.Neutralized = true;
                    evidenceChanged = true;

                    if (!evidence.NeutralizationNotified)
                    {
                        evidence.NeutralizationNotified = true;
                        notified = true;
                    }

                    _logger.Info(
                        "vehicle_evidence_neutralized",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Profil={0}; Episode={1}; Plaque={2}",
                            _currentProfileId,
                            evidence.SourceEpisodeId,
                            evidence.Signature != null
                                ? evidence.Signature.NormalizedPlate
                                : string.Empty));
                }
            }

            if (notified)
            {
                NativeUi.Notify(
                    "~b~Véhicule repeint~s~ : " +
                    "signalement visuel neutralisé.");
            }

            return RecognitionPolicy.ClampOptionalWantedLevel(
                maximumFloor);
        }

        private int GetMatchingOutfitWantedFloor(
            OutfitSignatureData currentOutfit,
            DateTime nowUtc)
        {
            if (_currentProfile == null ||
                currentOutfit == null ||
                !currentOutfit.IsValid ||
                _currentProfile.OutfitEvidence == null)
            {
                return 0;
            }

            int maximumFloor = 0;
            int index;

            for (index = 0;
                 index < _currentProfile.OutfitEvidence.Count;
                 index++)
            {
                OutfitEvidenceState evidence =
                    _currentProfile.OutfitEvidence[index];

                if (!IsOutfitEvidenceUsable(
                    evidence,
                    nowUtc))
                {
                    continue;
                }

                if (OutfitSignatureComparer.IsRecognizedMatch(
                    evidence.Signature,
                    currentOutfit))
                {
                    maximumFloor =
                        Math.Max(
                            maximumFloor,
                            evidence.WantedFloor);
                }
            }

            return RecognitionPolicy.ClampOptionalWantedLevel(
                maximumFloor);
        }

        private void UpdateSearchZoneRecognition(
            Ped playerPed,
            int currentWanted,
            int nowGameTime,
            DateTime nowUtc)
        {
            SearchZoneState zone =
                _currentProfile != null
                    ? _currentProfile.SearchZone
                    : null;

            if (!IsSearchZoneUsable(zone, nowUtc))
            {
                _insideSearchZone = false;
                _observerExposures.Clear();
                _lastRecognitionScanTime = nowGameTime;
                return;
            }

            float distanceSquared =
                DistanceSquared2D(
                    playerPed.Position,
                    zone.Center);

            bool inside =
                distanceSquared <=
                zone.Radius * zone.Radius;

            if (inside != _insideSearchZone)
            {
                _insideSearchZone = inside;
                _observerExposures.Clear();
                _lastRecognitionScanTime = nowGameTime;

                _logger.Info(
                    inside
                        ? "search_zone_entered"
                        : "search_zone_exited",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Profil={0}; Distance={1:0}; Rayon={2:0}",
                        _currentProfileId,
                        Math.Sqrt(
                            Math.Max(
                                0.0,
                                distanceSquared)),
                        zone.Radius));
            }

            if (!inside ||
                currentWanted >= zone.WantedFloor ||
                _pendingWantedLoss != null ||
                nowUtc < zone.GraceUntilUtc ||
                IsPlayerDead(playerPed) ||
                IsPlayerBeingArrested() ||
                IsPlayerSwitchInProgress())
            {
                _observerExposures.Clear();
                _lastRecognitionScanTime = nowGameTime;
                return;
            }

            if (zone.LastRecognitionUtc != DateTime.MinValue &&
                nowUtc <
                zone.LastRecognitionUtc.AddSeconds(
                    RecognitionPolicy.ZoneRecognitionCooldownSeconds))
            {
                _observerExposures.Clear();
                _lastRecognitionScanTime = nowGameTime;
                return;
            }

            if (nowGameTime < _nextRecognitionScan)
            {
                return;
            }

            _nextRecognitionScan =
                SafeGameTimeAdd(
                    nowGameTime,
                    RecognitionScanMilliseconds);

            float deltaSeconds =
                Math.Max(
                    0.05f,
                    Math.Min(
                        1.0f,
                        (nowGameTime -
                         _lastRecognitionScanTime) / 1000.0f));

            _lastRecognitionScanTime =
                nowGameTime;

            ScanObservers(
                playerPed,
                zone,
                deltaSeconds,
                nowGameTime,
                nowUtc);
        }

        private void ScanObservers(
            Ped playerPed,
            SearchZoneState zone,
            float deltaSeconds,
            int nowGameTime,
            DateTime nowUtc)
        {
            IdentitySnapshot identity =
                GetCurrentIdentity(
                    playerPed,
                    nowGameTime,
                    false);

            if (identity == null)
            {
                return;
            }

            bool vehicleEvidenceChanged;

            int vehicleFloor =
                GetMatchingVehicleWantedFloor(
                    identity.Vehicle,
                    nowUtc,
                    true,
                    out vehicleEvidenceChanged);

            int outfitFloor =
                GetMatchingOutfitWantedFloor(
                    identity.Outfit,
                    nowUtc);

            if (vehicleEvidenceChanged)
            {
                _store.MarkDirty(
                    _saveData,
                    nowGameTime);
            }

            bool vehicleRecognized =
                vehicleFloor > 0 &&
                identity.Vehicle != null;

            // Je résous le véhicule une seule fois par balayage, quel que soit
            // le nombre d'observateurs traités dans cette tranche.
            Vehicle currentVehicle = GetCurrentVehicle(playerPed);
            bool playerInVehicle = EntityExists(currentVehicle);

            float disguiseMultiplier =
                GetCurrentDisguiseMultiplier(
                    identity);

            bool bodyRecognitionAvailable =
                disguiseMultiplier > 0.0f;

            Ped[] candidates =
                GetCandidatePeds(
                    playerPed,
                    RecognitionPolicy.ObserverMaximumDistance);

            _recognitionScanSequence++;

            int processedObservers = 0;
            int index;

            for (index = 0;
                 index < candidates.Length &&
                 processedObservers <
                 MaximumTrackedObservers;
                 index++)
            {
                Ped observer =
                    candidates[index];

                if (!IsValidObserver(
                    observer,
                    playerPed))
                {
                    continue;
                }

                bool lawOfficer =
                    IsLawOfficer(observer);

                float maximumDistance =
                    lawOfficer
                        ? RecognitionPolicy.PoliceObserverMaximumDistance
                        : RecognitionPolicy.CivilianObserverMaximumDistance;

                float bodyRate = 0.0f;
                float vehicleRate = 0.0f;

                float bodyDistanceFactor;

                if (bodyRecognitionAvailable &&
                    TryGetVisibilityFactor(
                    observer,
                    playerPed,
                    maximumDistance,
                    lawOfficer,
                    out bodyDistanceFactor))
                {
                    bodyRate =
                        RecognitionPolicy.BaseIdentityExposurePerSecond *
                        disguiseMultiplier *
                        bodyDistanceFactor;

                    if (playerInVehicle)
                    {
                        bodyRate *=
                            RecognitionPolicy.InVehicleIdentityMultiplier;
                    }
                }

                float vehicleDistanceFactor;

                if (vehicleRecognized &&
                    EntityExists(currentVehicle) &&
                    TryGetVisibilityFactor(
                        observer,
                        currentVehicle,
                        maximumDistance,
                        lawOfficer,
                        out vehicleDistanceFactor))
                {
                    vehicleRate =
                        RecognitionPolicy.VehicleExposurePerSecond *
                        vehicleDistanceFactor;
                }

                float combinedRate =
                    Math.Min(
                        RecognitionPolicy.MaximumExposurePerSecond,
                        bodyRate + vehicleRate);

                if (!lawOfficer)
                {
                    combinedRate *=
                        RecognitionPolicy.CivilianExposureMultiplier;
                }

                UpdateObserverExposure(
                    observer,
                    lawOfficer,
                    combinedRate,
                    deltaSeconds,
                    nowGameTime);

                processedObservers++;
            }

            int triggeringObserverHandle = 0;
            bool triggeringObserverIsLaw = false;

            _observerRemovalBuffer.Clear();

            foreach (KeyValuePair<int, ObserverExposureRuntime> pair
                     in _observerExposures)
            {
                ObserverExposureRuntime state =
                    pair.Value;

                if (state == null ||
                    !IsValidRuntimeObserver(state))
                {
                    _observerRemovalBuffer.Add(pair.Key);
                    continue;
                }

                if (state.LastScanSequence !=
                    _recognitionScanSequence)
                {
                    state.Exposure =
                        Math.Max(
                            0.0f,
                            state.Exposure -
                            RecognitionPolicy.ExposureDecayPerSecond *
                            deltaSeconds);
                }

                if (state.IsReporting &&
                    nowGameTime >= state.ReportAtGameTime)
                {
                    triggeringObserverHandle =
                        pair.Key;

                    triggeringObserverIsLaw =
                        false;

                    break;
                }

                if (state.IsLawOfficer &&
                    state.Exposure >=
                    RecognitionPolicy.RecognitionThreshold)
                {
                    triggeringObserverHandle =
                        pair.Key;

                    triggeringObserverIsLaw =
                        true;

                    break;
                }

                if (nowGameTime -
                    state.LastRelevantGameTime >
                    RecognitionPolicy.ObserverRuntimeExpirationMilliseconds)
                {
                    _observerRemovalBuffer.Add(pair.Key);
                }
            }

            for (index = 0;
                 index < _observerRemovalBuffer.Count;
                 index++)
            {
                _observerExposures.Remove(
                    _observerRemovalBuffer[index]);
            }

            if (triggeringObserverHandle != 0)
            {
                TryReacquireWantedFromZone(
                    zone,
                    vehicleFloor,
                    outfitFloor,
                    triggeringObserverIsLaw
                        ? "policier"
                        : "témoin civil",
                    nowGameTime,
                    nowUtc);
            }
        }

        private void UpdateObserverExposure(
            Ped observer,
            bool lawOfficer,
            float exposureRate,
            float deltaSeconds,
            int nowGameTime)
        {
            int handle = observer.Handle;
            int modelHash = GetObserverModelHashSafe(observer);
            long memoryAddress = GetObserverMemoryAddressSafe(observer);

            ObserverExposureRuntime state;
            bool stateFound =
                _observerExposures.TryGetValue(
                    handle,
                    out state);

            if (!stateFound ||
                !CanReuseObserverExposure(
                    state,
                    observer,
                    modelHash,
                    memoryAddress))
            {
                if (!stateFound &&
                    _observerExposures.Count >=
                    MaximumTrackedObservers)
                {
                    return;
                }

                /*
                 * Je remplace tout l'état si GTA recycle le handle : aucune
                 * exposition ni dénonciation de l'ancien PNJ ne peut passer à
                 * la nouvelle génération d'entité.
                 */
                state =
                    new ObserverExposureRuntime
                    {
                        Ped = observer,
                        Handle = handle,
                        ModelHash = modelHash,
                        MemoryAddress = memoryAddress,
                        IsLawOfficer = lawOfficer,
                        Exposure = 0.0f,
                        LastRelevantGameTime = nowGameTime
                    };

                _observerExposures[handle] = state;
            }

            state.Ped = observer;
            state.IsLawOfficer = lawOfficer;
            state.LastScanSequence =
                _recognitionScanSequence;

            if (exposureRate > 0.0f)
            {
                state.Exposure =
                    Math.Min(
                        RecognitionPolicy.MaximumStoredExposure,
                        state.Exposure +
                        exposureRate *
                        deltaSeconds);

                state.LastRelevantGameTime =
                    nowGameTime;

                if (!lawOfficer &&
                    !state.IsReporting &&
                    state.Exposure >=
                    RecognitionPolicy.RecognitionThreshold)
                {
                    state.IsReporting = true;
                    state.ReportAtGameTime =
                        SafeGameTimeAdd(
                            nowGameTime,
                            RecognitionPolicy.CivilianReportDelayMilliseconds);
                }
            }
            else
            {
                state.Exposure =
                    Math.Max(
                        0.0f,
                        state.Exposure -
                        RecognitionPolicy.ExposureDecayPerSecond *
                        deltaSeconds);
            }
        }

        private void TryReacquireWantedFromZone(
            SearchZoneState zone,
            int vehicleFloor,
            int outfitFloor,
            string observerType,
            int nowGameTime,
            DateTime nowUtc)
        {
            if (zone == null ||
                !zone.Active)
            {
                return;
            }

            int currentWanted = SafeGetWantedLevel();

            if (currentWanted >= zone.WantedFloor)
            {
                return;
            }

            int targetWanted =
                Math.Max(
                    zone.WantedFloor,
                    Math.Max(
                        vehicleFloor,
                        outfitFloor));

            targetWanted =
                RecognitionPolicy.ClampWantedLevel(
                    targetWanted);

            _skipNaturalEscalationUntil =
                SafeGameTimeAdd(
                    nowGameTime,
                    WantedWriteGuardMilliseconds);

            bool applied =
                ApplyWantedMinimum(
                    targetWanted,
                    "reconnaissance dans la zone par " +
                    SafeReason(observerType),
                    nowGameTime);

            if (!applied)
            {
                return;
            }

            zone.LastRecognitionUtc = nowUtc;

            _observerExposures.Clear();

            _store.MarkDirty(
                _saveData,
                nowGameTime);

            NativeUi.Notify(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "~b~Vous avez été identifié~s~ dans la zone " +
                    "de recherche. Niveau {0}.",
                    targetWanted));

            _logger.Info(
                "search_zone_reacquired",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Profil={0}; Wanted={1}; Observateur={2}",
                    _currentProfileId,
                    targetWanted,
                    SafeReason(observerType)));
        }

        private float GetCurrentDisguiseMultiplier(
            IdentitySnapshot identity)
        {
            AppearanceEvidenceState evidence =
                _currentProfile != null
                    ? _currentProfile.AppearanceEvidence
                    : null;

            if (evidence == null ||
                !evidence.Active ||
                evidence.Signature == null ||
                !evidence.Signature.IsValid ||
                identity == null)
            {
                return 0.0f;
            }

            if (identity.Appearance == null ||
                !identity.Appearance.IsValid)
            {
                // Sans visage courant comparable, je laisse uniquement la
                // plaque visible contribuer à une reconnaissance.
                return 0.0f;
            }

            bool outfitSame =
                evidence.OutfitReference != null &&
                identity.Outfit != null &&
                OutfitSignatureComparer.IsRecognizedMatch(
                    evidence.OutfitReference,
                    identity.Outfit);

            bool appearanceSame =
                identity.Appearance != null &&
                AppearanceSignatureComparer.IsRecognizedMatch(
                    evidence.Signature,
                    identity.Appearance);

            float multiplier;

            if (outfitSame && appearanceSame)
            {
                multiplier = 1.0f;
            }
            else if (!outfitSame && appearanceSame)
            {
                multiplier =
                    RecognitionPolicy.ChangedOutfitRecognitionMultiplier;
            }
            else if (outfitSame && !appearanceSame)
            {
                multiplier =
                    RecognitionPolicy.ChangedAppearanceRecognitionMultiplier;
            }
            else
            {
                multiplier =
                    RecognitionPolicy.ChangedOutfitAndAppearanceMultiplier;
            }

            if (identity.Outfit != null &&
                OutfitSignatureComparer.HasFaceMask(
                    identity.Outfit))
            {
                multiplier *=
                    RecognitionPolicy.FaceMaskRecognitionMultiplier;
            }

            return Math.Max(
                RecognitionPolicy.MinimumRecognitionMultiplier,
                Math.Min(
                    1.0f,
                    multiplier));
        }

        private bool TryGetVisibilityFactor(
            Ped observer,
            Entity target,
            float maximumDistance,
            bool lawOfficer,
            out float factor)
        {
            factor = 0.0f;

            if (!EntityExists(observer) ||
                !EntityExists(target))
            {
                return false;
            }

            Vector3 observerPosition =
                observer.Position;

            Vector3 targetPosition =
                target.Position;

            float deltaX =
                targetPosition.X -
                observerPosition.X;

            float deltaY =
                targetPosition.Y -
                observerPosition.Y;

            float deltaZ =
                targetPosition.Z -
                observerPosition.Z;

            float distanceSquared =
                deltaX * deltaX +
                deltaY * deltaY +
                deltaZ * deltaZ;

            if (distanceSquared < 1.0f ||
                distanceSquared >
                maximumDistance * maximumDistance)
            {
                return false;
            }

            float distance =
                (float)Math.Sqrt(
                    distanceSquared);

            float inverseDistance =
                1.0f / distance;

            float directionX =
                deltaX * inverseDistance;

            float directionY =
                deltaY * inverseDistance;

            float directionZ =
                deltaZ * inverseDistance;

            Vector3 forward =
                observer.ForwardVector;

            float dot =
                forward.X * directionX +
                forward.Y * directionY +
                forward.Z * directionZ;

            float minimumDot =
                lawOfficer
                    ? RecognitionPolicy.PoliceMinimumViewDot
                    : RecognitionPolicy.CivilianMinimumViewDot;

            if (dot < minimumDot)
            {
                return false;
            }

            bool clearLineOfSight;

            try
            {
                clearLineOfSight =
                    Function.Call<bool>(
                        Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                        observer.Handle,
                        target.Handle,
                        17);
            }
            catch
            {
                clearLineOfSight = false;
            }

            if (!clearLineOfSight)
            {
                return false;
            }

            float normalizedDistance =
                Math.Max(
                    0.0f,
                    Math.Min(
                        1.0f,
                        distance / maximumDistance));

            factor =
                Math.Max(
                    RecognitionPolicy.MinimumDistanceFactor,
                    1.0f -
                    normalizedDistance *
                    RecognitionPolicy.DistancePenalty);

            return true;
        }

        private Ped[] GetCandidatePeds(
            Ped playerPed,
            float radius)
        {
            try
            {
                if (!_nearbyPedsMethodResolved)
                {
                    _nearbyPedsMethodResolved = true;

                    _getNearbyPedsMethod =
                        typeof(World).GetMethod(
                            "GetNearbyPeds",
                            BindingFlags.Public |
                            BindingFlags.Static,
                            null,
                            new[]
                            {
                                typeof(Ped),
                                typeof(float)
                            },
                            null);
                }

                if (_getNearbyPedsMethod != null)
                {
                    object result =
                        _getNearbyPedsMethod.Invoke(
                            null,
                            new object[]
                            {
                                playerPed,
                                radius
                            });

                    Ped[] nearby =
                        result as Ped[];

                    if (nearby != null)
                    {
                        return nearby;
                    }
                }
            }
            catch
            {
                // Fallback ci-dessous.
            }

            // Je refuse un scan global du monde en fallback : la reconnaissance
            // reste bornée aux API de proximité disponibles sur le runtime v2.
            return new Ped[0];
        }

        private static bool IsValidObserver(
            Ped observer,
            Ped playerPed)
        {
            if (!EntityExists(observer) ||
                !EntityExists(playerPed))
            {
                return false;
            }

            if (observer.Handle ==
                playerPed.Handle)
            {
                return false;
            }

            if (IsPlayerDead(observer))
            {
                return false;
            }

            try
            {
                if (!Function.Call<bool>(
                    Hash.IS_PED_HUMAN,
                    observer.Handle))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool IsValidRuntimeObserver(
            ObserverExposureRuntime state)
        {
            if (state == null ||
                !EntityExists(state.Ped) ||
                IsPlayerDead(state.Ped))
            {
                return false;
            }

            return CanReuseObserverExposure(
                state,
                state.Ped,
                GetObserverModelHashSafe(state.Ped),
                GetObserverMemoryAddressSafe(state.Ped));
        }

        private static bool CanReuseObserverExposure(
            ObserverExposureRuntime state,
            Ped currentObserver,
            int modelHash,
            long memoryAddress)
        {
            if (state == null ||
                !EntityExists(state.Ped) ||
                !EntityExists(currentObserver) ||
                state.Handle != currentObserver.Handle ||
                state.ModelHash != modelHash)
            {
                return false;
            }

            if (state.MemoryAddress != 0L ||
                memoryAddress != 0L)
            {
                return state.MemoryAddress != 0L &&
                       memoryAddress != 0L &&
                       state.MemoryAddress == memoryAddress;
            }

            /*
             * Je n'accepte le repli sans adresse native qu'avec la même
             * enveloppe Ped : le handle seul peut déjà appartenir à un autre
             * PNJ entre deux balayages.
             */
            return ReferenceEquals(
                state.Ped,
                currentObserver);
        }

        private static int GetObserverModelHashSafe(
            Ped observer)
        {
            if (!EntityExists(observer))
            {
                return 0;
            }

            try
            {
                return observer.Model.Hash;
            }
            catch
            {
                return 0;
            }
        }

        private static unsafe long GetObserverMemoryAddressSafe(
            Ped observer)
        {
            if (!EntityExists(observer))
            {
                return 0L;
            }

            try
            {
                return (long)(IntPtr)observer.MemoryAddress;
            }
            catch
            {
                return 0L;
            }
        }

        private static bool IsLawOfficer(
            Ped observer)
        {
            if (!EntityExists(observer))
            {
                return false;
            }

            int relationshipGroup;

            try
            {
                relationshipGroup =
                    Function.Call<int>(
                        Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
                        observer.Handle);
            }
            catch
            {
                return false;
            }

            return relationshipGroup ==
                       Game.GenerateHash("COP") ||
                   relationshipGroup ==
                       Game.GenerateHash("SECURITY_GUARD") ||
                   relationshipGroup ==
                       Game.GenerateHash("PRIVATE_SECURITY") ||
                   relationshipGroup ==
                       Game.GenerateHash("ARMY");
        }

        private void CleanupExpiredEvidence(
            DateTime nowUtc,
            int nowGameTime)
        {
            if (_currentProfile == null)
            {
                return;
            }

            EnsureEvidenceCollections(_currentProfile);

            bool changed = false;
            int index;

            for (index =
                    _currentProfile.VehicleEvidence.Count - 1;
                 index >= 0;
                 index--)
            {
                VehicleEvidenceState state =
                    _currentProfile.VehicleEvidence[index];

                if (!IsVehicleEvidenceUsable(
                    state,
                    nowUtc))
                {
                    _currentProfile.VehicleEvidence.RemoveAt(
                        index);

                    changed = true;
                }
            }

            for (index =
                    _currentProfile.OutfitEvidence.Count - 1;
                 index >= 0;
                 index--)
            {
                OutfitEvidenceState state =
                    _currentProfile.OutfitEvidence[index];

                if (!IsOutfitEvidenceUsable(
                    state,
                    nowUtc))
                {
                    _currentProfile.OutfitEvidence.RemoveAt(
                        index);

                    changed = true;
                }
            }

            if (_currentProfile.SearchZone != null &&
                _currentProfile.SearchZone.Active &&
                _currentProfile.SearchZone.ExpiresUtc <= nowUtc)
            {
                _currentProfile.SearchZone =
                    new SearchZoneState();

                _currentProfile.AppearanceEvidence =
                    new AppearanceEvidenceState();

                _observerExposures.Clear();
                _insideSearchZone = false;

                RemoveSearchZoneBlip();
                SetActiveSearchZoneStatusCache(false);

                changed = true;

                _logger.Info(
                    "search_zone_expired",
                    "La zone de recherche a expiré.");
            }

            if (changed)
            {
                _store.MarkDirty(
                    _saveData,
                    nowGameTime);
            }
        }

        private void EnsureSearchZoneBlip(
            DateTime nowUtc)
        {
            if (!_enabled ||
                _currentProfile == null ||
                !IsSearchZoneUsable(
                    _currentProfile.SearchZone,
                    nowUtc))
            {
                RemoveSearchZoneBlip();
                return;
            }

            SearchZoneState zone =
                _currentProfile.SearchZone;

            if (_radiusBlip != null &&
                _radiusBlip.IsFor(
                    zone.SourceEpisodeId,
                    zone.Center,
                    zone.Radius))
            {
                return;
            }

            RecreateSearchZoneBlip(nowUtc);
        }

        private void RecreateSearchZoneBlip(
            DateTime nowUtc)
        {
            RemoveSearchZoneBlip();

            if (!_enabled ||
                _currentProfile == null ||
                !IsSearchZoneUsable(
                    _currentProfile.SearchZone,
                    nowUtc))
            {
                return;
            }

            SearchZoneState zone =
                _currentProfile.SearchZone;

            _radiusBlip.Create(
                zone.SourceEpisodeId,
                zone.Center,
                zone.Radius,
                RecognitionPolicy.SearchZoneBlipColor,
                RecognitionPolicy.SearchZoneBlipAlpha);
        }

        private void RemoveSearchZoneBlip()
        {
            if (_radiusBlip != null)
            {
                _radiusBlip.Remove();
            }
        }

        private void DrawHud(
            Ped playerPed,
            IdentitySnapshot identity,
            DateTime nowUtc)
        {
            if (_hud == null ||
                _currentProfile == null)
            {
                return;
            }

            bool evidenceChanged;
            int vehicleFloor =
                GetMatchingVehicleWantedFloor(
                    identity != null
                        ? identity.Vehicle
                        : null,
                    nowUtc,
                    false,
                    out evidenceChanged);

            int outfitFloor =
                GetMatchingOutfitWantedFloor(
                    identity != null
                        ? identity.Outfit
                        : null,
                    nowUtc);

            int activeVehicleCount =
                CountActiveVehicleEvidence(nowUtc);

            int neutralizedVehicleCount =
                CountNeutralizedVehicleEvidence(nowUtc);

            int activeOutfitCount =
                CountActiveOutfitEvidence(nowUtc);

            bool zoneActive =
                IsSearchZoneUsable(
                    _currentProfile.SearchZone,
                    nowUtc);

            bool insideZone = false;

            if (zoneActive &&
                playerPed != null)
            {
                insideZone =
                    DistanceSquared2D(
                        playerPed.Position,
                        _currentProfile.SearchZone.Center) <=
                    _currentProfile.SearchZone.Radius *
                    _currentProfile.SearchZone.Radius;
            }

            _hud.Draw(
                activeVehicleCount,
                neutralizedVehicleCount,
                vehicleFloor > 0,
                activeOutfitCount,
                outfitFloor > 0,
                zoneActive,
                insideZone,
                zoneActive
                    ? _currentProfile.SearchZone.ExpiresUtc
                    : DateTime.MinValue,
                nowUtc);
        }

        internal string[] GetStatusLines()
        {
            lock (_statusSync)
            {
                return (string[])_statusLines.Clone();
            }
        }

        internal bool HasActiveSearchZoneCached()
        {
            lock (_statusSync)
            {
                return _hasActiveSearchZoneStatus;
            }
        }

        private void SetActiveSearchZoneStatusCache(bool active)
        {
            lock (_statusSync)
            {
                _hasActiveSearchZoneStatus = active;
            }
        }

        private void RefreshStatusLinesCache()
        {
            string[] refreshedStatus = BuildStatusLines();
            bool hasActiveSearchZone =
                _initialized &&
                _enabled &&
                !_runtimeSuspended &&
                _currentProfile != null &&
                IsSearchZoneUsable(
                    _currentProfile.SearchZone,
                    DateTime.UtcNow);

            lock (_statusSync)
            {
                _statusLines = refreshedStatus;
                _hasActiveSearchZoneStatus = hasActiveSearchZone;
            }
        }

        private string[] BuildStatusLines()
        {
            try
            {
                if (!_initialized ||
                    _currentProfile == null)
                {
                    return new[]
                    {
                        "Reconnaissance policière : profil indisponible",
                        "Plaques signalées : état indisponible",
                        "Tenues signalées : état indisponible",
                        "Mandat local : état indisponible",
                        "Distance du centre : état indisponible"
                    };
                }

                DateTime nowUtc = DateTime.UtcNow;
                Ped playerPed = GetUsablePlayerPed();
                int nowGameTime = Game.GameTime;

                IdentitySnapshot identity =
                    playerPed != null
                        ? GetCurrentIdentity(
                            playerPed,
                            nowGameTime,
                            false)
                        : null;

                bool changed;

                int vehicleFloor =
                    GetMatchingVehicleWantedFloor(
                        identity != null
                            ? identity.Vehicle
                            : null,
                        nowUtc,
                        false,
                        out changed);

                int outfitFloor =
                    GetMatchingOutfitWantedFloor(
                        identity != null
                            ? identity.Outfit
                            : null,
                        nowUtc);

                List<string> lines =
                    new List<string>();

                lines.Add(
                    "Reconnaissance policière : " +
                    (_enabled
                        ? (_runtimeSuspended
                            ? "suspendue pendant la détention"
                            : "activée")
                        : "désactivée"));

                int vehicleCount =
                    CountActiveVehicleEvidence(nowUtc);

                lines.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Plaques signalées : {0} — véhicule actuel : {1}",
                        vehicleCount,
                        vehicleFloor > 0
                            ? "reconnu, niveau " + vehicleFloor
                            : "non reconnu"));

                int outfitCount =
                    CountActiveOutfitEvidence(nowUtc);

                lines.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Tenues signalées : {0} — tenue actuelle : {1}",
                        outfitCount,
                        outfitFloor > 0
                            ? "reconnue, niveau " + outfitFloor
                            : "non reconnue"));

                SearchZoneState zone =
                    _currentProfile.SearchZone;

                if (IsSearchZoneUsable(
                    zone,
                    nowUtc))
                {
                    double secondsRemaining =
                        Math.Max(
                            0.0,
                            (zone.ExpiresUtc -
                             nowUtc).TotalSeconds);

                    float distance = 0.0f;

                    if (playerPed != null)
                    {
                        distance =
                            (float)Math.Sqrt(
                                Math.Max(
                                    0.0f,
                                    DistanceSquared2D(
                                        playerPed.Position,
                                        zone.Center)));
                    }

                    bool inside =
                        distance <= zone.Radius;

                    float riskMultiplier =
                        identity != null
                            ? GetCurrentDisguiseMultiplier(identity)
                            : 0.0f;

                    string risk;

                    if (riskMultiplier >= 0.75f)
                    {
                        risk = "normal";
                    }
                    else if (riskMultiplier >= 0.18f)
                    {
                        risk = "réduit";
                    }
                    else
                    {
                        risk = "fortement réduit";
                    }

                    lines.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Mandat local : actif — rayon {0:0} m — " +
                            "{1} — reste {2}",
                            zone.Radius,
                            inside
                                ? "vous êtes dans la zone"
                                : "vous êtes hors zone",
                            FormatDuration(secondsRemaining)));

                    lines.Add(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Distance du centre : {0:0} m — " +
                            "risque d'identification : {1}",
                            distance,
                            risk));
                }
                else
                {
                    lines.Add(
                        "Mandat local : aucun");

                    // Je garde cinq lignes stables pour que le menu ne se
                    // décale pas quand la zone apparaît ou expire.
                    lines.Add(
                        "Distance du centre : sans objet — " +
                        "risque d'identification : aucun");
                }

                return lines.ToArray();
            }
            catch
            {
                return new[]
                {
                    "Reconnaissance policière : état indisponible",
                    "Plaques signalées : état indisponible",
                    "Tenues signalées : état indisponible",
                    "Mandat local : état indisponible",
                    "Distance du centre : état indisponible"
                };
            }
        }

        private int CountActiveVehicleEvidence(
            DateTime nowUtc)
        {
            if (_currentProfile == null ||
                _currentProfile.VehicleEvidence == null)
            {
                return 0;
            }

            int count = 0;
            int index;

            for (index = 0;
                 index < _currentProfile.VehicleEvidence.Count;
                 index++)
            {
                if (IsVehicleEvidenceUsable(
                    _currentProfile.VehicleEvidence[index],
                    nowUtc))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountNeutralizedVehicleEvidence(
            DateTime nowUtc)
        {
            if (_currentProfile == null ||
                _currentProfile.VehicleEvidence == null)
            {
                return 0;
            }

            int count = 0;
            int index;

            for (index = 0;
                 index < _currentProfile.VehicleEvidence.Count;
                 index++)
            {
                VehicleEvidenceState state =
                    _currentProfile.VehicleEvidence[index];

                if (IsVehicleEvidenceUsable(
                    state,
                    nowUtc) &&
                    state.Neutralized)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActiveOutfitEvidence(
            DateTime nowUtc)
        {
            if (_currentProfile == null ||
                _currentProfile.OutfitEvidence == null)
            {
                return 0;
            }

            int count = 0;
            int index;

            for (index = 0;
                 index < _currentProfile.OutfitEvidence.Count;
                 index++)
            {
                if (IsOutfitEvidenceUsable(
                    _currentProfile.OutfitEvidence[index],
                    nowUtc))
                {
                    count++;
                }
            }

            return count;
        }

        private IdentitySnapshot GetCurrentIdentity(
            Ped playerPed,
            int nowGameTime,
            bool forceRefresh)
        {
            if (!EntityExists(playerPed))
            {
                return null;
            }

            if (!forceRefresh &&
                _identityCache != null &&
                _identityCachePedHandle ==
                playerPed.Handle &&
                nowGameTime <
                _nextIdentityRefresh)
            {
                return _identityCache;
            }

            _identityCache =
                IdentityCapture.Capture(playerPed);

            _identityCachePedHandle =
                playerPed.Handle;

            _nextIdentityRefresh =
                SafeGameTimeAdd(
                    nowGameTime,
                    IdentityRefreshMilliseconds);

            return _identityCache;
        }

        private bool HasRecentTeleport(
            Ped playerPed)
        {
            if (!_hasLastPlayerPosition ||
                !EntityExists(playerPed))
            {
                return false;
            }

            Vector3 current =
                playerPed.Position;

            float deltaX =
                current.X -
                _lastPlayerPosition.X;

            float deltaY =
                current.Y -
                _lastPlayerPosition.Y;

            float deltaZ =
                current.Z -
                _lastPlayerPosition.Z;

            float distanceSquared =
                deltaX * deltaX +
                deltaY * deltaY +
                deltaZ * deltaZ;

            return distanceSquared >
                   RecognitionPolicy.InternalTeleportSuppressionDistance *
                   RecognitionPolicy.InternalTeleportSuppressionDistance;
        }

        private static bool IsVehicleEvidenceUsable(
            VehicleEvidenceState state,
            DateTime nowUtc)
        {
            return state != null &&
                   state.Active &&
                   state.Signature != null &&
                   state.Signature.IsValid &&
                   state.WantedFloor >= 1 &&
                   state.WantedFloor <= 5 &&
                   state.ExpiresUtc > nowUtc;
        }

        private static bool IsOutfitEvidenceUsable(
            OutfitEvidenceState state,
            DateTime nowUtc)
        {
            return state != null &&
                   state.Active &&
                   state.Signature != null &&
                   state.Signature.IsValid &&
                   state.WantedFloor >= 1 &&
                   state.WantedFloor <= 5 &&
                   state.ExpiresUtc > nowUtc;
        }

        private static bool IsSearchZoneUsable(
            SearchZoneState state,
            DateTime nowUtc)
        {
            return state != null &&
                   state.Active &&
                   state.Center != null &&
                   state.Center.IsFinite() &&
                   state.WantedFloor >= 1 &&
                   state.WantedFloor <= 5 &&
                   state.Radius >=
                       RecognitionPolicy.MinimumValidZoneRadius &&
                   state.Radius <=
                       RecognitionPolicy.MaximumValidZoneRadius &&
                   state.ExpiresUtc > nowUtc;
        }

        private static void EnsureEvidenceCollections(
            RecognitionProfileData profile)
        {
            if (profile.VehicleEvidence == null)
            {
                profile.VehicleEvidence =
                    new List<VehicleEvidenceState>();
            }

            if (profile.OutfitEvidence == null)
            {
                profile.OutfitEvidence =
                    new List<OutfitEvidenceState>();
            }
        }

        private static Ped GetUsablePlayerPed()
        {
            try
            {
                Ped ped =
                    Game.Player.Character;

                return EntityExists(ped)
                    ? ped
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static Vehicle GetCurrentVehicle(
            Ped playerPed)
        {
            if (!EntityExists(playerPed))
            {
                return null;
            }

            try
            {
                if (!playerPed.IsInVehicle())
                {
                    return null;
                }

                Vehicle vehicle =
                    playerPed.CurrentVehicle;

                return EntityExists(vehicle)
                    ? vehicle
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPedInVehicle(
            Ped playerPed)
        {
            if (!EntityExists(playerPed))
            {
                return false;
            }

            try
            {
                return playerPed.IsInVehicle();
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerDead(
            Ped ped)
        {
            if (!EntityExists(ped))
            {
                return true;
            }

            try
            {
                return ped.IsDead;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsPlayerBeingArrested()
        {
            try
            {
                return Function.Call<bool>(
                    Hash.IS_PLAYER_BEING_ARRESTED,
                    Game.Player.Handle,
                    true);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerSwitchInProgress()
        {
            try
            {
                return Function.Call<bool>(
                    (Hash)RecognitionNativeHashes.IsPlayerSwitchInProgress);
            }
            catch
            {
                return false;
            }
        }

        private int SafeGetWantedLevel()
        {
            try
            {
                int wantedLevel =
                    _wantedLevelReaderOverride != null
                        ? _wantedLevelReaderOverride()
                        : Game.Player.WantedLevel;

                int clampedWantedLevel =
                    RecognitionPolicy.ClampOptionalWantedLevel(
                    wantedLevel);

                _lastReliableWantedLevel = clampedWantedLevel;
                return clampedWantedLevel;
            }
            catch
            {
                // Une lecture indisponible ne prouve jamais une perte d'étoiles.
                // Je conserve donc la dernière lecture ou écriture confirmée.
                return RecognitionPolicy.ClampOptionalWantedLevel(
                    _lastReliableWantedLevel);
            }
        }

        private static bool EntityExists(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                return Function.Call<bool>(
                    (Hash)RecognitionNativeHashes.DoesEntityExist,
                    entity.Handle);
            }
            catch
            {
                return false;
            }
        }

        private static float DistanceSquared2D(
            Vector3 position,
            PositionData center)
        {
            if (center == null)
            {
                return float.MaxValue;
            }

            float deltaX =
                position.X -
                center.X;

            float deltaY =
                position.Y -
                center.Y;

            return deltaX * deltaX +
                   deltaY * deltaY;
        }

        private static int SafeGameTimeAdd(
            int gameTime,
            int milliseconds)
        {
            if (milliseconds < 0)
            {
                milliseconds = 0;
            }

            if (gameTime >
                int.MaxValue - milliseconds)
            {
                return int.MaxValue;
            }

            return gameTime + milliseconds;
        }

        internal static string SafeReason(
            string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "non précisée";
            }

            reason = reason.Trim();

            if (reason.Length > 160)
            {
                reason =
                    reason.Substring(0, 160);
            }

            return reason;
        }

        internal static string NormalizeProfileId(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            string value = profileId.Trim();

            if (string.Equals(
                value,
                "Michael",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Michael";
            }

            if (string.Equals(
                value,
                "Franklin",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Franklin";
            }

            if (string.Equals(
                value,
                "Trevor",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Trevor";
            }

            return null;
        }

        private static string FormatDuration(
            double totalSeconds)
        {
            int seconds =
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        totalSeconds));

            int minutes =
                seconds / 60;

            int remainingSeconds =
                seconds % 60;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}",
                minutes,
                remainingSeconds);
        }

        private void HandleRuntimeException(
            Exception exception)
        {
            if (_logger != null)
            {
                _logger.ErrorRateLimited(
                    "runtime_error",
                    exception,
                    5000);
            }

            int now =
                Game.GameTime;

            if (now -
                _lastRuntimeErrorNotificationTime >=
                10000)
            {
                _lastRuntimeErrorNotificationTime =
                    now;

                NativeUi.Notify(
                    "~r~Justice avancée~s~ : erreur de reconnaissance. " +
                    "Consulte JusticeRecognition.log.");
            }
        }

        private static VehicleSignatureData CloneVehicle(
            VehicleSignatureData source)
        {
            return source != null
                ? source.Clone()
                : null;
        }

        private static OutfitSignatureData CloneOutfit(
            OutfitSignatureData source)
        {
            return source != null
                ? source.Clone()
                : null;
        }

        private static AppearanceSignatureData CloneAppearance(
            AppearanceSignatureData source)
        {
            return source != null
                ? source.Clone()
                : null;
        }
    }

    internal static class RecognitionNativeHashes
    {
        internal const ulong GetPedDrawableVariation = 0x67F3780DD425D4FCUL;
        internal const ulong GetPedPaletteVariation = 0xE3DD5F2A84B42281UL;
        internal const ulong GetPedPropIndex = 0x898CC20EA75BACD8UL;
        internal const ulong GetPedPropTextureIndex = 0xE131A28626F81AB2UL;
        internal const ulong GetPedTextureVariation = 0x04A355E041E004E6UL;
        internal const ulong GetVehicleColours = 0xA19435F193E081ACUL;
        internal const ulong GetVehicleNumberPlateText = 0x7CE1CCB9B293020EUL;
        internal const ulong IsPlayerSwitchInProgress = 0xD9D2CFFF49FAB35FUL;
        internal const ulong DoesEntityExist = 0x7239B21A38F536BAUL;
        internal const ulong SetPlayerWantedLevel = 0x39FF19C64EF7DA5BUL;
        internal const ulong SetPlayerWantedLevelNow = 0xE0A7D1E497FFCD6FUL;
    }

    internal static class RecognitionPolicy
    {
        public const int SchemaVersion = 1;

        public const int MaximumVehicleEvidenceRecords = 4;
        public const int MaximumOutfitEvidenceRecords = 5;

        public const float MinimumValidZoneRadius = 100.0f;
        public const float MaximumValidZoneRadius = 2000.0f;

        public const int ZoneGraceSeconds = 8;
        public const int ZoneRecognitionCooldownSeconds = 20;

        public const float ObserverMaximumDistance = 95.0f;
        public const float PoliceObserverMaximumDistance = 90.0f;
        public const float CivilianObserverMaximumDistance = 60.0f;

        public const float PoliceMinimumViewDot = 0.12f;
        public const float CivilianMinimumViewDot = 0.30f;

        public const float MinimumDistanceFactor = 0.20f;
        public const float DistancePenalty = 0.78f;

        public const float VehicleExposurePerSecond = 0.52f;
        public const float BaseIdentityExposurePerSecond = 0.32f;
        public const float MaximumExposurePerSecond = 0.85f;
        public const float CivilianExposureMultiplier = 0.35f;
        public const float ExposureDecayPerSecond = 0.30f;

        public const float RecognitionThreshold = 1.0f;
        public const float MaximumStoredExposure = 1.25f;

        public const float ChangedOutfitRecognitionMultiplier = 0.30f;
        public const float ChangedAppearanceRecognitionMultiplier = 0.40f;
        public const float ChangedOutfitAndAppearanceMultiplier = 0.08f;
        public const float FaceMaskRecognitionMultiplier = 0.35f;
        public const float InVehicleIdentityMultiplier = 0.45f;
        public const float MinimumRecognitionMultiplier = 0.015f;

        public const int CivilianReportDelayMilliseconds = 2500;
        public const int ObserverRuntimeExpirationMilliseconds = 8000;

        public const float InternalTeleportSuppressionDistance = 220.0f;

        public const int SearchZoneBlipColor = 3;
        public const int SearchZoneBlipAlpha = 48;

        public static int ClampWantedLevel(int wantedLevel)
        {
            if (wantedLevel < 1)
            {
                return 1;
            }

            if (wantedLevel > 5)
            {
                return 5;
            }

            return wantedLevel;
        }

        public static int ClampOptionalWantedLevel(
            int wantedLevel)
        {
            if (wantedLevel <= 0)
            {
                return 0;
            }

            return ClampWantedLevel(wantedLevel);
        }

        public static float GetZoneRadius(
            int wantedLevel)
        {
            switch (ClampWantedLevel(wantedLevel))
            {
                case 1:
                    return 350.0f;

                case 2:
                    return 500.0f;

                case 3:
                    return 700.0f;

                case 4:
                    return 900.0f;

                default:
                    return 1200.0f;
            }
        }

        public static int GetZoneDurationSeconds(
            int wantedLevel)
        {
            switch (ClampWantedLevel(wantedLevel))
            {
                case 1:
                    return 180;

                case 2:
                    return 300;

                case 3:
                    return 480;

                case 4:
                    return 720;

                default:
                    return 1080;
            }
        }

        public static int GetVehicleEvidenceDurationSeconds(
            int wantedLevel)
        {
            switch (ClampWantedLevel(wantedLevel))
            {
                case 1:
                    return 8 * 60;

                case 2:
                    return 12 * 60;

                case 3:
                    return 18 * 60;

                case 4:
                    return 25 * 60;

                default:
                    return 35 * 60;
            }
        }

        public static int GetOutfitEvidenceDurationSeconds(
            int wantedLevel)
        {
            switch (ClampWantedLevel(wantedLevel))
            {
                case 1:
                    return 6 * 60;

                case 2:
                    return 10 * 60;

                case 3:
                    return 15 * 60;

                case 4:
                    return 20 * 60;

                default:
                    return 30 * 60;
            }
        }
    }

    internal sealed class IdentitySnapshot
    {
        public VehicleSignatureData Vehicle;
        public OutfitSignatureData Outfit;
        public AppearanceSignatureData Appearance;
    }

    internal static class IdentityCapture
    {
        private const ulong GetPedHeadOverlayValueHash =
            0xA60EF3B6461A4D43UL;

        public static IdentitySnapshot Capture(
            Ped playerPed)
        {
            IdentitySnapshot snapshot =
                new IdentitySnapshot();

            snapshot.Vehicle =
                CaptureVehicle(
                    GetCurrentVehicle(playerPed));

            snapshot.Outfit =
                CaptureOutfit(playerPed);

            snapshot.Appearance =
                CaptureAppearance(
                    playerPed,
                    snapshot.Outfit);

            return snapshot;
        }

        public static VehicleSignatureData CaptureVehicle(
            Vehicle vehicle)
        {
            if (!EntityExists(vehicle))
            {
                return null;
            }

            VehicleSignatureData signature =
                new VehicleSignatureData();

            try
            {
                signature.ModelHash =
                    vehicle.Model.Hash;

                string plate =
                    Function.Call<string>(
                        (Hash)RecognitionNativeHashes.GetVehicleNumberPlateText,
                        vehicle.Handle);

                signature.NormalizedPlate =
                    NormalizePlate(plate);

                signature.HasUsablePlate =
                    !string.IsNullOrWhiteSpace(
                        signature.NormalizedPlate);

                OutputArgument primary =
                    new OutputArgument();

                OutputArgument secondary =
                    new OutputArgument();

                Function.Call(
                    (Hash)RecognitionNativeHashes.GetVehicleColours,
                    vehicle.Handle,
                    primary,
                    secondary);

                signature.PrimaryColor =
                    primary.GetResult<int>();

                signature.SecondaryColor =
                    secondary.GetResult<int>();

                signature.SignatureVersion = 1;

                signature.IsValid =
                    signature.ModelHash != 0;

                return signature;
            }
            catch
            {
                return null;
            }
        }

        public static OutfitSignatureData CaptureOutfit(
            Ped ped)
        {
            if (!EntityExists(ped))
            {
                return null;
            }

            OutfitSignatureData signature =
                new OutfitSignatureData();

            try
            {
                signature.PedModelHash =
                    ped.Model.Hash;

                signature.Components =
                    new List<DrawableVariationData>();

                signature.Props =
                    new List<PropVariationData>();

                int componentIndex;

                for (componentIndex = 0;
                     componentIndex <= 11;
                     componentIndex++)
                {
                    DrawableVariationData component =
                        new DrawableVariationData
                        {
                            Slot = componentIndex,

                            Drawable =
                                Function.Call<int>(
                                    (Hash)RecognitionNativeHashes.GetPedDrawableVariation,
                                    ped.Handle,
                                    componentIndex),

                            Texture =
                                Function.Call<int>(
                                    (Hash)RecognitionNativeHashes.GetPedTextureVariation,
                                    ped.Handle,
                                    componentIndex),

                            Palette =
                                Function.Call<int>(
                                    (Hash)RecognitionNativeHashes.GetPedPaletteVariation,
                                    ped.Handle,
                                    componentIndex)
                        };

                    signature.Components.Add(component);
                }

                int propIndex;

                for (propIndex = 0;
                     propIndex <= 7;
                     propIndex++)
                {
                    PropVariationData prop =
                        new PropVariationData
                        {
                            Slot = propIndex,

                            Drawable =
                                Function.Call<int>(
                                    (Hash)RecognitionNativeHashes.GetPedPropIndex,
                                    ped.Handle,
                                    propIndex),

                            Texture =
                                Function.Call<int>(
                                    (Hash)RecognitionNativeHashes.GetPedPropTextureIndex,
                                    ped.Handle,
                                    propIndex)
                        };

                    signature.Props.Add(prop);
                }

                signature.SignatureVersion = 1;
                signature.IsValid =
                    signature.PedModelHash != 0;

                return signature;
            }
            catch
            {
                return null;
            }
        }

        public static AppearanceSignatureData CaptureAppearance(
            Ped ped,
            OutfitSignatureData outfit)
        {
            if (!EntityExists(ped))
            {
                return null;
            }

            AppearanceSignatureData signature =
                new AppearanceSignatureData();

            try
            {
                signature.PedModelHash =
                    ped.Model.Hash;

                signature.HairDrawable =
                    Function.Call<int>(
                        (Hash)RecognitionNativeHashes.GetPedDrawableVariation,
                        ped.Handle,
                        2);

                signature.HairTexture =
                    Function.Call<int>(
                        (Hash)RecognitionNativeHashes.GetPedTextureVariation,
                        ped.Handle,
                        2);

                signature.FaceDrawable =
                    Function.Call<int>(
                        (Hash)RecognitionNativeHashes.GetPedDrawableVariation,
                        ped.Handle,
                        0);

                signature.FaceTexture =
                    Function.Call<int>(
                        (Hash)RecognitionNativeHashes.GetPedTextureVariation,
                        ped.Handle,
                        0);

                signature.BeardOverlay =
                    TryGetHeadOverlayValue(
                        ped,
                        1);

                signature.HasMask =
                    outfit != null &&
                    OutfitSignatureComparer.HasFaceMask(
                        outfit);

                signature.SignatureVersion = 1;
                signature.IsValid =
                    signature.PedModelHash != 0;

                return signature;
            }
            catch
            {
                return null;
            }
        }

        private static int TryGetHeadOverlayValue(
            Ped ped,
            int overlayId)
        {
            try
            {
                int value =
                    Function.Call<int>(
                        (Hash)GetPedHeadOverlayValueHash,
                        ped.Handle,
                        overlayId);

                if (value < 0 ||
                    value > 255)
                {
                    return -1;
                }

                return value;
            }
            catch
            {
                return -1;
            }
        }

        private static string NormalizePlate(
            string plate)
        {
            if (string.IsNullOrWhiteSpace(plate))
            {
                return string.Empty;
            }

            StringBuilder builder =
                new StringBuilder(12);

            int index;

            for (index = 0;
                 index < plate.Length &&
                 builder.Length < 12;
                 index++)
            {
                char character =
                    char.ToUpperInvariant(
                        plate[index]);

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static Vehicle GetCurrentVehicle(
            Ped ped)
        {
            if (!EntityExists(ped))
            {
                return null;
            }

            try
            {
                if (!ped.IsInVehicle())
                {
                    return null;
                }

                Vehicle vehicle =
                    ped.CurrentVehicle;

                return EntityExists(vehicle)
                    ? vehicle
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool EntityExists(
            Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                return Function.Call<bool>(
                    (Hash)RecognitionNativeHashes.DoesEntityExist,
                    entity.Handle);
            }
            catch
            {
                return false;
            }
        }
    }

    internal enum VehicleMatchKind
    {
        None = 0,
        Exact = 1,
        SameIdentityDifferentPaint = 2
    }

    internal static class VehicleSignatureComparer
    {
        public static VehicleMatchKind Compare(
            VehicleSignatureData evidence,
            VehicleSignatureData current)
        {
            if (evidence == null ||
                current == null ||
                !evidence.IsValid ||
                !current.IsValid)
            {
                return VehicleMatchKind.None;
            }

            if (evidence.ModelHash !=
                current.ModelHash)
            {
                return VehicleMatchKind.None;
            }

            if (evidence.HasUsablePlate ||
                current.HasUsablePlate)
            {
                if (!evidence.HasUsablePlate ||
                    !current.HasUsablePlate)
                {
                    return VehicleMatchKind.None;
                }

                if (!string.Equals(
                    evidence.NormalizedPlate,
                    current.NormalizedPlate,
                    StringComparison.Ordinal))
                {
                    return VehicleMatchKind.None;
                }

                if (HasSamePaint(
                    evidence,
                    current))
                {
                    return VehicleMatchKind.Exact;
                }

                return
                    VehicleMatchKind.SameIdentityDifferentPaint;
            }

            /*
             * Véhicule sans plaque :
             * le modèle seul n'est pas suffisant.
             * On exige également la peinture.
             */
            return HasSamePaint(
                evidence,
                current)
                    ? VehicleMatchKind.Exact
                    : VehicleMatchKind.None;
        }

        public static bool IsSamePersistentIdentity(
            VehicleSignatureData left,
            VehicleSignatureData right)
        {
            if (left == null ||
                right == null ||
                left.ModelHash != right.ModelHash)
            {
                return false;
            }

            if (left.HasUsablePlate ||
                right.HasUsablePlate)
            {
                if (!left.HasUsablePlate ||
                    !right.HasUsablePlate)
                {
                    return false;
                }

                return string.Equals(
                    left.NormalizedPlate,
                    right.NormalizedPlate,
                    StringComparison.Ordinal);
            }

            return HasSamePaint(left, right);
        }

        private static bool HasSamePaint(
            VehicleSignatureData left,
            VehicleSignatureData right)
        {
            return left.PrimaryColor ==
                       right.PrimaryColor &&
                   left.SecondaryColor ==
                       right.SecondaryColor;
        }
    }

    internal static class OutfitSignatureComparer
    {
        private const float RecognitionDifferenceThreshold =
            2.0f;

        public static bool IsRecognizedMatch(
            OutfitSignatureData evidence,
            OutfitSignatureData current)
        {
            return GetDifferenceScore(
                evidence,
                current) <
                RecognitionDifferenceThreshold;
        }

        public static float GetDifferenceScore(
            OutfitSignatureData left,
            OutfitSignatureData right)
        {
            if (left == null ||
                right == null ||
                !left.IsValid ||
                !right.IsValid ||
                left.PedModelHash !=
                right.PedModelHash)
            {
                return 1000.0f;
            }

            float difference = 0.0f;
            int slot;

            for (slot = 0;
                 slot <= 11;
                 slot++)
            {
                /*
                 * La coiffure, composant 2, est comparée dans
                 * AppearanceSignatureComparer et non comme une tenue.
                 */
                if (slot == 2)
                {
                    continue;
                }

                DrawableVariationData leftComponent =
                    FindComponent(
                        left.Components,
                        slot);

                DrawableVariationData rightComponent =
                    FindComponent(
                        right.Components,
                        slot);

                if (!SameComponent(
                    leftComponent,
                    rightComponent))
                {
                    difference +=
                        GetComponentWeight(slot);
                }
            }

            for (slot = 0;
                 slot <= 7;
                 slot++)
            {
                PropVariationData leftProp =
                    FindProp(
                        left.Props,
                        slot);

                PropVariationData rightProp =
                    FindProp(
                        right.Props,
                        slot);

                if (!SameProp(
                    leftProp,
                    rightProp))
                {
                    difference +=
                        GetPropWeight(slot);
                }
            }

            return difference;
        }

        public static bool HasFaceMask(
            OutfitSignatureData signature)
        {
            if (signature == null)
            {
                return false;
            }

            DrawableVariationData mask =
                FindComponent(
                    signature.Components,
                    1);

            return mask != null &&
                   mask.Drawable > 0;
        }

        private static float GetComponentWeight(
            int slot)
        {
            switch (slot)
            {
                case 0:
                    return 0.50f;

                case 1:
                    return 1.50f;

                case 3:
                    return 1.50f;

                case 4:
                    return 2.00f;

                case 5:
                    return 0.50f;

                case 6:
                    return 1.00f;

                case 7:
                    return 0.50f;

                case 8:
                    return 1.00f;

                case 9:
                    return 1.00f;

                case 10:
                    return 0.50f;

                case 11:
                    return 2.00f;

                default:
                    return 0.25f;
            }
        }

        private static float GetPropWeight(
            int slot)
        {
            switch (slot)
            {
                case 0:
                    return 0.75f;

                case 1:
                    return 0.25f;

                case 2:
                    return 0.25f;

                default:
                    return 0.20f;
            }
        }

        private static DrawableVariationData FindComponent(
            List<DrawableVariationData> components,
            int slot)
        {
            if (components == null)
            {
                return null;
            }

            int index;

            for (index = 0;
                 index < components.Count;
                 index++)
            {
                DrawableVariationData component =
                    components[index];

                if (component != null &&
                    component.Slot == slot)
                {
                    return component;
                }
            }

            return null;
        }

        private static PropVariationData FindProp(
            List<PropVariationData> props,
            int slot)
        {
            if (props == null)
            {
                return null;
            }

            int index;

            for (index = 0;
                 index < props.Count;
                 index++)
            {
                PropVariationData prop =
                    props[index];

                if (prop != null &&
                    prop.Slot == slot)
                {
                    return prop;
                }
            }

            return null;
        }

        private static bool SameComponent(
            DrawableVariationData left,
            DrawableVariationData right)
        {
            if (left == null &&
                right == null)
            {
                return true;
            }

            if (left == null ||
                right == null)
            {
                return false;
            }

            return left.Drawable ==
                       right.Drawable &&
                   left.Texture ==
                       right.Texture &&
                   left.Palette ==
                       right.Palette;
        }

        private static bool SameProp(
            PropVariationData left,
            PropVariationData right)
        {
            if (left == null &&
                right == null)
            {
                return true;
            }

            if (left == null ||
                right == null)
            {
                return false;
            }

            return left.Drawable ==
                       right.Drawable &&
                   left.Texture ==
                       right.Texture;
        }
    }

    internal static class AppearanceSignatureComparer
    {
        public static bool IsRecognizedMatch(
            AppearanceSignatureData evidence,
            AppearanceSignatureData current)
        {
            if (evidence == null ||
                current == null ||
                !evidence.IsValid ||
                !current.IsValid ||
                evidence.PedModelHash !=
                current.PedModelHash)
            {
                return false;
            }

            bool sameHair =
                evidence.HairDrawable ==
                    current.HairDrawable &&
                evidence.HairTexture ==
                    current.HairTexture;

            bool sameFace =
                evidence.FaceDrawable ==
                    current.FaceDrawable &&
                evidence.FaceTexture ==
                    current.FaceTexture;

            bool sameBeard =
                evidence.BeardOverlay < 0 ||
                current.BeardOverlay < 0 ||
                evidence.BeardOverlay ==
                    current.BeardOverlay;

            return sameHair &&
                   sameFace &&
                   sameBeard;
        }
    }

    internal sealed class PursuitEpisodeRuntime
    {
        public long EpisodeId;
        public int PeakWantedLevel;
        public int StartedAtGameTime;
        public PositionData LastKnownPosition;
        public VehicleSignatureData LastVehicle;
        public OutfitSignatureData LastOutfit;
        public AppearanceSignatureData LastAppearance;
    }

    internal sealed class PendingWantedLossRuntime
    {
        public int FinalizeAtGameTime;
        public bool Suppressed;
        public string Reason;
    }

    internal sealed class PendingWantedEscalationRuntime
    {
        public long EpisodeId;
        public int EvaluateAtGameTime;
        public int AttemptCount;
    }

    internal sealed class ObserverExposureRuntime
    {
        public Ped Ped;
        public int Handle;
        public int ModelHash;
        public long MemoryAddress;
        public bool IsLawOfficer;
        public float Exposure;
        public int LastRelevantGameTime;
        public int LastScanSequence;
        public bool IsReporting;
        public int ReportAtGameTime;
    }

    [Serializable]
    [XmlRoot("DonJJusticeRecognition")]
    public sealed class JusticeRecognitionSaveData
    {
        public JusticeRecognitionSaveData()
        {
            SchemaVersion =
                RecognitionPolicy.SchemaVersion;

            Profiles =
                new List<RecognitionProfileData>();
        }

        public int SchemaVersion { get; set; }

        [XmlArray("Profiles")]
        [XmlArrayItem("Profile")]
        public List<RecognitionProfileData> Profiles
        {
            get;
            set;
        }

        public RecognitionProfileData GetOrCreateProfile(
            string profileId)
        {
            if (Profiles == null)
            {
                Profiles =
                    new List<RecognitionProfileData>();
            }

            int index;

            for (index = 0;
                 index < Profiles.Count;
                 index++)
            {
                RecognitionProfileData profile =
                    Profiles[index];

                if (profile != null &&
                    string.Equals(
                        profile.ProfileId,
                        profileId,
                        StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            RecognitionProfileData created =
                new RecognitionProfileData
                {
                    ProfileId = profileId
                };

            Profiles.Add(created);

            return created;
        }
    }

    [Serializable]
    public sealed class RecognitionProfileData
    {
        public RecognitionProfileData()
        {
            ProfileId = string.Empty;

            VehicleEvidence =
                new List<VehicleEvidenceState>();

            OutfitEvidence =
                new List<OutfitEvidenceState>();

            AppearanceEvidence =
                new AppearanceEvidenceState();

            SearchZone =
                new SearchZoneState();
        }

        public string ProfileId { get; set; }

        public long LastEpisodeId { get; set; }

        [XmlArray("VehicleEvidence")]
        [XmlArrayItem("Vehicle")]
        public List<VehicleEvidenceState> VehicleEvidence
        {
            get;
            set;
        }

        [XmlArray("OutfitEvidence")]
        [XmlArrayItem("Outfit")]
        public List<OutfitEvidenceState> OutfitEvidence
        {
            get;
            set;
        }

        public AppearanceEvidenceState AppearanceEvidence
        {
            get;
            set;
        }

        public SearchZoneState SearchZone
        {
            get;
            set;
        }
    }

    [Serializable]
    public sealed class VehicleEvidenceState
    {
        public bool Active { get; set; }
        public long SourceEpisodeId { get; set; }
        public int WantedFloor { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public bool Neutralized { get; set; }
        public bool NeutralizationNotified { get; set; }
        public VehicleSignatureData Signature { get; set; }
    }

    [Serializable]
    public sealed class OutfitEvidenceState
    {
        public bool Active { get; set; }
        public long SourceEpisodeId { get; set; }
        public int WantedFloor { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public OutfitSignatureData Signature { get; set; }
    }

    [Serializable]
    public sealed class AppearanceEvidenceState
    {
        public AppearanceEvidenceState()
        {
            Signature =
                new AppearanceSignatureData();

            OutfitReference =
                new OutfitSignatureData();
        }

        public bool Active { get; set; }
        public long SourceEpisodeId { get; set; }
        public AppearanceSignatureData Signature { get; set; }
        public OutfitSignatureData OutfitReference { get; set; }
    }

    [Serializable]
    public sealed class SearchZoneState
    {
        public SearchZoneState()
        {
            Center =
                new PositionData();
        }

        public bool Active { get; set; }
        public long SourceEpisodeId { get; set; }
        public int WantedFloor { get; set; }
        public PositionData Center { get; set; }
        public float Radius { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime GraceUntilUtc { get; set; }
        public DateTime LastRecognitionUtc { get; set; }
    }

    [Serializable]
    public sealed class VehicleSignatureData
    {
        public bool IsValid { get; set; }
        public int SignatureVersion { get; set; }
        public int ModelHash { get; set; }
        public string NormalizedPlate { get; set; }
        public bool HasUsablePlate { get; set; }
        public int PrimaryColor { get; set; }
        public int SecondaryColor { get; set; }

        public VehicleSignatureData Clone()
        {
            return new VehicleSignatureData
            {
                IsValid = IsValid,
                SignatureVersion =
                    SignatureVersion,
                ModelHash =
                    ModelHash,
                NormalizedPlate =
                    NormalizedPlate ?? string.Empty,
                HasUsablePlate =
                    HasUsablePlate,
                PrimaryColor =
                    PrimaryColor,
                SecondaryColor =
                    SecondaryColor
            };
        }
    }

    [Serializable]
    public sealed class OutfitSignatureData
    {
        public OutfitSignatureData()
        {
            Components =
                new List<DrawableVariationData>();

            Props =
                new List<PropVariationData>();
        }

        public bool IsValid { get; set; }
        public int SignatureVersion { get; set; }
        public int PedModelHash { get; set; }

        [XmlArray("Components")]
        [XmlArrayItem("Component")]
        public List<DrawableVariationData> Components
        {
            get;
            set;
        }

        [XmlArray("Props")]
        [XmlArrayItem("Prop")]
        public List<PropVariationData> Props
        {
            get;
            set;
        }

        public OutfitSignatureData Clone()
        {
            OutfitSignatureData clone =
                new OutfitSignatureData
                {
                    IsValid = IsValid,
                    SignatureVersion =
                        SignatureVersion,
                    PedModelHash =
                        PedModelHash
                };

            int index;

            if (Components != null)
            {
                for (index = 0;
                     index < Components.Count;
                     index++)
                {
                    DrawableVariationData component =
                        Components[index];

                    if (component != null)
                    {
                        clone.Components.Add(
                            component.Clone());
                    }
                }
            }

            if (Props != null)
            {
                for (index = 0;
                     index < Props.Count;
                     index++)
                {
                    PropVariationData prop =
                        Props[index];

                    if (prop != null)
                    {
                        clone.Props.Add(
                            prop.Clone());
                    }
                }
            }

            return clone;
        }
    }

    [Serializable]
    public sealed class AppearanceSignatureData
    {
        public bool IsValid { get; set; }
        public int SignatureVersion { get; set; }
        public int PedModelHash { get; set; }
        public int HairDrawable { get; set; }
        public int HairTexture { get; set; }
        public int FaceDrawable { get; set; }
        public int FaceTexture { get; set; }

        /*
         * -1 signifie que l'overlay de barbe n'est pas lisible
         * avec la version/native disponible.
         */
        public int BeardOverlay { get; set; }

        public bool HasMask { get; set; }

        public AppearanceSignatureData Clone()
        {
            return new AppearanceSignatureData
            {
                IsValid = IsValid,
                SignatureVersion =
                    SignatureVersion,
                PedModelHash =
                    PedModelHash,
                HairDrawable =
                    HairDrawable,
                HairTexture =
                    HairTexture,
                FaceDrawable =
                    FaceDrawable,
                FaceTexture =
                    FaceTexture,
                BeardOverlay =
                    BeardOverlay,
                HasMask =
                    HasMask
            };
        }
    }

    [Serializable]
    public sealed class DrawableVariationData
    {
        public int Slot { get; set; }
        public int Drawable { get; set; }
        public int Texture { get; set; }
        public int Palette { get; set; }

        public DrawableVariationData Clone()
        {
            return new DrawableVariationData
            {
                Slot = Slot,
                Drawable = Drawable,
                Texture = Texture,
                Palette = Palette
            };
        }
    }

    [Serializable]
    public sealed class PropVariationData
    {
        public int Slot { get; set; }
        public int Drawable { get; set; }
        public int Texture { get; set; }

        public PropVariationData Clone()
        {
            return new PropVariationData
            {
                Slot = Slot,
                Drawable = Drawable,
                Texture = Texture
            };
        }
    }

    [Serializable]
    public sealed class PositionData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static PositionData FromVector3(
            Vector3 vector)
        {
            return new PositionData
            {
                X = vector.X,
                Y = vector.Y,
                Z = vector.Z
            };
        }

        public Vector3 ToVector3()
        {
            return new Vector3(
                X,
                Y,
                Z);
        }

        public bool IsFinite()
        {
            return IsFiniteNumber(X) &&
                   IsFiniteNumber(Y) &&
                   IsFiniteNumber(Z);
        }

        public PositionData Clone()
        {
            return new PositionData
            {
                X = X,
                Y = Y,
                Z = Z
            };
        }

        private static bool IsFiniteNumber(
            float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    internal static class RecognitionDataSanitizer
    {
        public static void SanitizeSaveData(
            JusticeRecognitionSaveData saveData,
            DateTime nowUtc,
            RecognitionLogger logger)
        {
            if (saveData == null)
            {
                return;
            }

            saveData.SchemaVersion =
                RecognitionPolicy.SchemaVersion;

            if (saveData.Profiles == null)
            {
                saveData.Profiles =
                    new List<RecognitionProfileData>();
            }

            HashSet<string> acceptedProfiles =
                new HashSet<string>(
                    StringComparer.Ordinal);

            int index;

            for (index =
                    saveData.Profiles.Count - 1;
                 index >= 0;
                 index--)
            {
                RecognitionProfileData profile =
                    saveData.Profiles[index];

                if (profile == null ||
                    !IsSupportedProfile(
                        profile.ProfileId) ||
                    acceptedProfiles.Contains(
                        profile.ProfileId))
                {
                    saveData.Profiles.RemoveAt(index);
                    continue;
                }

                acceptedProfiles.Add(
                    profile.ProfileId);

                SanitizeProfile(
                    profile,
                    nowUtc,
                    logger);
            }
        }

        public static void SanitizeProfile(
            RecognitionProfileData profile,
            DateTime nowUtc,
            RecognitionLogger logger)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.LastEpisodeId < 0)
            {
                profile.LastEpisodeId = 0;
            }

            if (profile.VehicleEvidence == null)
            {
                profile.VehicleEvidence =
                    new List<VehicleEvidenceState>();
            }

            if (profile.OutfitEvidence == null)
            {
                profile.OutfitEvidence =
                    new List<OutfitEvidenceState>();
            }

            int index;

            for (index =
                    profile.VehicleEvidence.Count - 1;
                 index >= 0;
                 index--)
            {
                VehicleEvidenceState state =
                    profile.VehicleEvidence[index];

                if (!SanitizeVehicleEvidence(
                    state,
                    nowUtc))
                {
                    profile.VehicleEvidence.RemoveAt(
                        index);
                }
            }

            while (profile.VehicleEvidence.Count >
                   RecognitionPolicy.MaximumVehicleEvidenceRecords)
            {
                profile.VehicleEvidence.RemoveAt(0);
            }

            for (index =
                    profile.OutfitEvidence.Count - 1;
                 index >= 0;
                 index--)
            {
                OutfitEvidenceState state =
                    profile.OutfitEvidence[index];

                if (!SanitizeOutfitEvidence(
                    state,
                    nowUtc))
                {
                    profile.OutfitEvidence.RemoveAt(
                        index);
                }
            }

            while (profile.OutfitEvidence.Count >
                   RecognitionPolicy.MaximumOutfitEvidenceRecords)
            {
                profile.OutfitEvidence.RemoveAt(0);
            }

            if (!SanitizeSearchZone(
                profile.SearchZone,
                nowUtc))
            {
                profile.SearchZone =
                    new SearchZoneState();

                profile.AppearanceEvidence =
                    new AppearanceEvidenceState();
            }

            if (profile.AppearanceEvidence == null)
            {
                profile.AppearanceEvidence =
                    new AppearanceEvidenceState();
            }

            SanitizeAppearanceEvidence(
                profile.AppearanceEvidence);
        }

        private static bool SanitizeVehicleEvidence(
            VehicleEvidenceState state,
            DateTime nowUtc)
        {
            if (state == null ||
                !state.Active ||
                state.Signature == null ||
                !state.Signature.IsValid)
            {
                return false;
            }

            if (state.WantedFloor < 1 ||
                state.WantedFloor > 5)
            {
                return false;
            }

            state.WantedFloor =
                RecognitionPolicy.ClampWantedLevel(
                    state.WantedFloor);

            state.CreatedUtc =
                NormalizeUtc(state.CreatedUtc);

            state.ExpiresUtc =
                NormalizeUtc(state.ExpiresUtc);

            if (!IsPlausibleEvidenceDates(
                state.CreatedUtc,
                state.ExpiresUtc,
                nowUtc))
            {
                return false;
            }

            if (state.ExpiresUtc <= nowUtc)
            {
                return false;
            }

            state.Signature.NormalizedPlate =
                SanitizeText(
                    state.Signature.NormalizedPlate,
                    12);

            state.Signature.HasUsablePlate =
                !string.IsNullOrWhiteSpace(
                    state.Signature.NormalizedPlate);

            state.Signature.SignatureVersion = 1;

            return state.Signature.ModelHash != 0;
        }

        private static bool SanitizeOutfitEvidence(
            OutfitEvidenceState state,
            DateTime nowUtc)
        {
            if (state == null ||
                !state.Active ||
                state.Signature == null ||
                !state.Signature.IsValid)
            {
                return false;
            }

            if (state.WantedFloor < 1 ||
                state.WantedFloor > 5)
            {
                return false;
            }

            state.WantedFloor =
                RecognitionPolicy.ClampWantedLevel(
                    state.WantedFloor);

            state.CreatedUtc =
                NormalizeUtc(state.CreatedUtc);

            state.ExpiresUtc =
                NormalizeUtc(state.ExpiresUtc);

            if (!IsPlausibleEvidenceDates(
                state.CreatedUtc,
                state.ExpiresUtc,
                nowUtc))
            {
                return false;
            }

            if (state.ExpiresUtc <= nowUtc)
            {
                return false;
            }

            SanitizeOutfitSignature(
                state.Signature);

            return state.Signature.PedModelHash != 0;
        }

        private static bool SanitizeSearchZone(
            SearchZoneState zone,
            DateTime nowUtc)
        {
            if (zone == null ||
                !zone.Active ||
                zone.Center == null ||
                !zone.Center.IsFinite())
            {
                return false;
            }

            if (zone.WantedFloor < 1 ||
                zone.WantedFloor > 5)
            {
                return false;
            }

            zone.WantedFloor =
                RecognitionPolicy.ClampWantedLevel(
                    zone.WantedFloor);

            zone.Radius =
                Math.Max(
                    RecognitionPolicy.MinimumValidZoneRadius,
                    Math.Min(
                        RecognitionPolicy.MaximumValidZoneRadius,
                        zone.Radius));

            zone.CreatedUtc =
                NormalizeUtc(zone.CreatedUtc);

            zone.ExpiresUtc =
                NormalizeUtc(zone.ExpiresUtc);

            zone.GraceUntilUtc =
                NormalizeUtc(zone.GraceUntilUtc);

            zone.LastRecognitionUtc =
                NormalizeUtcAllowMin(
                    zone.LastRecognitionUtc);

            if (!IsPlausibleEvidenceDates(
                zone.CreatedUtc,
                zone.ExpiresUtc,
                nowUtc))
            {
                return false;
            }

            return zone.ExpiresUtc > nowUtc;
        }

        private static void SanitizeAppearanceEvidence(
            AppearanceEvidenceState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.Signature == null)
            {
                state.Signature =
                    new AppearanceSignatureData();
            }

            if (state.OutfitReference == null)
            {
                state.OutfitReference =
                    new OutfitSignatureData();
            }

            state.Signature.SignatureVersion = 1;

            SanitizeOutfitSignature(
                state.OutfitReference);

            if (state.Signature.PedModelHash == 0)
            {
                state.Active = false;
            }
        }

        private static void SanitizeOutfitSignature(
            OutfitSignatureData signature)
        {
            if (signature == null)
            {
                return;
            }

            signature.SignatureVersion = 1;

            if (signature.Components == null)
            {
                signature.Components =
                    new List<DrawableVariationData>();
            }

            if (signature.Props == null)
            {
                signature.Props =
                    new List<PropVariationData>();
            }

            while (signature.Components.Count > 12)
            {
                signature.Components.RemoveAt(
                    signature.Components.Count - 1);
            }

            while (signature.Props.Count > 8)
            {
                signature.Props.RemoveAt(
                    signature.Props.Count - 1);
            }
        }

        private static bool IsPlausibleEvidenceDates(
            DateTime createdUtc,
            DateTime expiresUtc,
            DateTime nowUtc)
        {
            if (createdUtc == DateTime.MinValue ||
                expiresUtc == DateTime.MinValue ||
                expiresUtc <= createdUtc)
            {
                return false;
            }

            if (createdUtc <
                nowUtc.AddDays(-30.0))
            {
                return false;
            }

            if (expiresUtc >
                nowUtc.AddDays(7.0))
            {
                return false;
            }

            return true;
        }

        private static DateTime NormalizeUtc(
            DateTime value)
        {
            if (value == DateTime.MinValue)
            {
                return DateTime.MinValue;
            }

            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc);
        }

        private static DateTime NormalizeUtcAllowMin(
            DateTime value)
        {
            return value == DateTime.MinValue
                ? DateTime.MinValue
                : NormalizeUtc(value);
        }

        private static string SanitizeText(
            string value,
            int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();

            if (value.Length > maximumLength)
            {
                value =
                    value.Substring(
                        0,
                        maximumLength);
            }

            return value;
        }

        private static bool IsSupportedProfile(
            string profileId)
        {
            return string.Equals(
                       profileId,
                       "Michael",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       profileId,
                       "Franklin",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       profileId,
                       "Trevor",
                       StringComparison.Ordinal);
        }
    }

    internal sealed class RecognitionStore
    {
        private const long MaximumSaveBytes = 4L * 1024L * 1024L;

        private readonly string _path;
        private readonly string _backupPath;
        private readonly string _temporaryPath;
        private readonly string _backupTemporaryPath;
        private readonly string _primaryRollbackPath;
        private readonly string _backupRollbackPath;
        private readonly string _quarantineDirectoryPath;
        private readonly RecognitionLogger _logger;
        private readonly Action<string, string> _quarantineMover;

        private bool _dirty;
        private int _saveAtGameTime;

        public RecognitionStore(
            string path,
            RecognitionLogger logger)
            : this(
                path,
                logger,
                null)
        {
        }

        internal RecognitionStore(
            string path,
            RecognitionLogger logger,
            Action<string, string> quarantineMover)
        {
            _path = path;
            _backupPath = path + ".bak";
            _temporaryPath = path + ".tmp";
            _backupTemporaryPath = path + ".bak.tmp";
            _primaryRollbackPath = path + ".rollback";
            _backupRollbackPath = path + ".bak.rollback";
            _quarantineDirectoryPath = path + ".corrupt-quarantine";
            _logger = logger;
            _quarantineMover = quarantineMover ?? File.Move;
        }

        public JusticeRecognitionSaveData Load()
        {
            JusticeRecognitionSaveData primary;
            JusticeRecognitionSaveData backup;
            JusticeRecognitionSaveData temporary;
            JusticeRecognitionSaveData backupTemporary;
            JusticeRecognitionSaveData primaryRollback;
            JusticeRecognitionSaveData backupRollback;

            bool hasPrimary = TryLoadFile(_path, out primary);
            bool hasBackup = TryLoadFile(_backupPath, out backup);
            bool hasTemporary = TryLoadFile(_temporaryPath, out temporary);
            bool hasBackupTemporary =
                TryLoadFile(_backupTemporaryPath, out backupTemporary);
            bool hasPrimaryRollback =
                TryLoadFile(_primaryRollbackPath, out primaryRollback);
            bool hasBackupRollback =
                TryLoadFile(_backupRollbackPath, out backupRollback);

            string selectedPath = null;
            DateTime selectedWrite = DateTime.MinValue;
            JusticeRecognitionSaveData selected = null;

            SelectNewestValid(
                hasPrimary,
                _path,
                primary,
                ref selectedPath,
                ref selectedWrite,
                ref selected);
            SelectNewestValid(
                hasBackup,
                _backupPath,
                backup,
                ref selectedPath,
                ref selectedWrite,
                ref selected);
            SelectNewestValid(
                hasTemporary,
                _temporaryPath,
                temporary,
                ref selectedPath,
                ref selectedWrite,
                ref selected);
            SelectNewestValid(
                hasBackupTemporary,
                _backupTemporaryPath,
                backupTemporary,
                ref selectedPath,
                ref selectedWrite,
                ref selected);
            SelectNewestValid(
                hasPrimaryRollback,
                _primaryRollbackPath,
                primaryRollback,
                ref selectedPath,
                ref selectedWrite,
                ref selected);
            SelectNewestValid(
                hasBackupRollback,
                _backupRollbackPath,
                backupRollback,
                ref selectedPath,
                ref selectedWrite,
                ref selected);

            if (selected == null)
            {
                bool hasCorruptVariant = HasAnySaveVariant();
                bool hasPendingQuarantine =
                    Directory.Exists(_quarantineDirectoryPath);

                if (!hasCorruptVariant && !hasPendingQuarantine)
                {
                    return new JusticeRecognitionSaveData();
                }

                if (hasCorruptVariant && !QuarantineCorruptVariants())
                {
                    // Je refuse de démarrer tant qu'une variante illisible
                    // reste dans un chemin que le prochain chargement relira.
                    throw new InvalidDataException(
                        "La quarantaine de la sauvegarde reconnaissance est incomplète.");
                }

                JusticeRecognitionSaveData fresh =
                    new JusticeRecognitionSaveData();

                if (!ForceSave(fresh))
                {
                    // Je n'annonce jamais un reset technique tant que le
                    // primaire et son backup neuf ne sont pas tous les deux sûrs.
                    throw new InvalidDataException(
                        "La paire neuve de sauvegarde reconnaissance n'a pas pu être publiée.");
                }

                _logger.Info(
                    "save_corrupt_variants_quarantined",
                    "Toutes les variantes illisibles ont été isolées et une paire neuve redondante a été publiée.");

                return fresh;
            }

            bool redundantPairCurrent =
                hasPrimary &&
                hasBackup &&
                FilesEqual(_path, _backupPath) &&
                (string.Equals(
                     selectedPath,
                     _path,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     selectedPath,
                     _backupPath,
                     StringComparison.OrdinalIgnoreCase));

            if (!redundantPairCurrent)
            {
                LogRecoverySource(selectedPath);

                // Je republie la meilleure copie valide avant de l'exposer au
                // gameplay : le primaire et le backup redeviennent identiques.
                if (!ForceSave(selected))
                {
                    throw new InvalidDataException(
                        "La meilleure sauvegarde reconnaissance valide n'a pas pu être republiée.");
                }
            }

            return selected;
        }

        private static void SelectNewestValid(
            bool valid,
            string path,
            JusticeRecognitionSaveData candidate,
            ref string selectedPath,
            ref DateTime selectedWrite,
            ref JusticeRecognitionSaveData selected)
        {
            if (!valid || candidate == null)
            {
                return;
            }

            DateTime write = GetLastWriteTimeUtcSafe(path);
            if (selected == null || write > selectedWrite)
            {
                selectedPath = path;
                selectedWrite = write;
                selected = candidate;
            }
        }

        private void LogRecoverySource(string selectedPath)
        {
            if (string.Equals(
                selectedPath,
                _backupPath,
                StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    "save_loaded_from_backup",
                    "Sauvegarde de secours valide récupérée et republiée.");
                return;
            }

            if (string.Equals(
                    selectedPath,
                    _temporaryPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    selectedPath,
                    _backupTemporaryPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    "save_loaded_from_temporary",
                    "Sauvegarde temporaire valide récupérée et republiée.");
                return;
            }

            if (string.Equals(
                    selectedPath,
                    _primaryRollbackPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    selectedPath,
                    _backupRollbackPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    "save_loaded_from_rollback",
                    "Rollback valide récupéré et republié.");
            }
        }

        private bool HasAnySaveVariant()
        {
            return File.Exists(_path) ||
                   File.Exists(_backupPath) ||
                   File.Exists(_temporaryPath) ||
                   File.Exists(_backupTemporaryPath) ||
                   File.Exists(_primaryRollbackPath) ||
                   File.Exists(_backupRollbackPath);
        }

        private bool QuarantineCorruptVariants()
        {
            string[] variants =
            {
                _path,
                _backupPath,
                _temporaryPath,
                _backupTemporaryPath,
                _primaryRollbackPath,
                _backupRollbackPath
            };

            try
            {
                Directory.CreateDirectory(_quarantineDirectoryPath);

                string incidentId =
                    DateTime.UtcNow.ToString(
                        "yyyyMMddHHmmssfffffff",
                        CultureInfo.InvariantCulture) +
                    "-" +
                    Guid.NewGuid().ToString("N");

                for (int index = 0; index < variants.Length; index++)
                {
                    string sourcePath = variants[index];
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(
                        _quarantineDirectoryPath,
                        Path.GetFileName(sourcePath) +
                        "." +
                        incidentId +
                        ".corrupt");

                    // Je déplace sans écraser et sur le même volume. Après une
                    // coupure, le prochain Load reprend seulement les restants.
                    _quarantineMover(
                        sourcePath,
                        destinationPath);
                }

                return !HasAnySaveVariant();
            }
            catch (Exception exception)
            {
                _logger.Error(
                    "save_corrupt_quarantine_failed",
                    exception);

                return false;
            }
        }

        public void MarkDirty(
            JusticeRecognitionSaveData data,
            int nowGameTime)
        {
            if (data == null)
            {
                return;
            }

            _lastData = data;
            _dirty = true;
            _saveAtGameTime =
                SafeAdd(
                    nowGameTime,
                    500);
        }

        public void FlushIfDue(
            int nowGameTime)
        {
            if (!_dirty ||
                nowGameTime <
                _saveAtGameTime)
            {
                return;
            }

            /*
             * La donnée courante est fournie à ForceSave depuis le script.
             * Ici, le serializer garde la dernière référence passée à
             * MarkDirty via le champ suivant.
             */
            if (_lastData != null)
            {
                ForceSave(_lastData);
            }
        }

        private JusticeRecognitionSaveData _lastData;

        public bool ForceSave(
            JusticeRecognitionSaveData data)
        {
            if (data == null)
            {
                return false;
            }

            _lastData = data;

            try
            {
                string directory =
                    Path.GetDirectoryName(_path);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                SerializeToFile(data, _temporaryPath);
                SerializeToFile(data, _backupTemporaryPath);

                JusticeRecognitionSaveData validatedTemporary;
                if (!TryLoadFile(
                    _temporaryPath,
                    out validatedTemporary))
                {
                    throw new InvalidDataException(
                        "Le XML temporaire de reconnaissance n'est pas relisible.");
                }

                JusticeRecognitionSaveData validatedBackupTemporary;
                if (!TryLoadFile(
                    _backupTemporaryPath,
                    out validatedBackupTemporary) ||
                    !FilesEqual(_temporaryPath, _backupTemporaryPath))
                {
                    throw new InvalidDataException(
                        "Les deux XML temporaires de reconnaissance divergent.");
                }

                PublishValidatedTemporary(
                    _temporaryPath,
                    _path,
                    _primaryRollbackPath);

                PublishValidatedTemporary(
                    _backupTemporaryPath,
                    _backupPath,
                    _backupRollbackPath);

                JusticeRecognitionSaveData validatedPrimary;
                if (!TryLoadFile(
                    _path,
                    out validatedPrimary))
                {
                    throw new InvalidDataException(
                        "Le XML primaire de reconnaissance publié n'est pas relisible.");
                }

                JusticeRecognitionSaveData validatedBackup;
                if (!TryLoadFile(
                    _backupPath,
                    out validatedBackup) ||
                    !FilesEqual(_path, _backupPath))
                {
                    throw new InvalidDataException(
                        "Le backup reconnaissance n'est pas valide et à jour.");
                }

                _dirty = false;
                return true;
            }
            catch (Exception exception)
            {
                _dirty = true;
                _saveAtGameTime = SafeAdd(
                    GetCurrentGameTimeForRetry(),
                    5000);
                _logger.ErrorRateLimited(
                    "save_failed",
                    exception,
                    5000);

                return false;
            }
        }

        internal bool IsDirty
        {
            get { return _dirty; }
        }

        private static void SerializeToFile(
            JusticeRecognitionSaveData data,
            string path)
        {
            XmlSerializer serializer =
                new XmlSerializer(
                    typeof(JusticeRecognitionSaveData));

            XmlWriterSettings settings =
                new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = new UTF8Encoding(false),
                    CloseOutput = false
                };

            using (FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    serializer.Serialize(writer, data);
                    writer.Flush();
                }

                // Je force réellement les octets avant toute bascule de nom.
                stream.Flush(true);
            }
        }

        internal static void PublishValidatedTemporary(
            string temporaryPath,
            string destinationPath,
            string rollbackPath)
        {
            // Je publie uniquement par bascules atomiques sur le même volume.
            // Le rollback reste disponible jusqu'à validation du renommage.
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }

            if (File.Exists(rollbackPath))
            {
                File.Delete(rollbackPath);
            }

            try
            {
                File.Replace(
                    temporaryPath,
                    destinationPath,
                    rollbackPath,
                    true);
            }
            catch
            {
                if (!File.Exists(temporaryPath) &&
                    File.Exists(destinationPath))
                {
                    return;
                }

                File.Move(destinationPath, rollbackPath);

                try
                {
                    File.Move(temporaryPath, destinationPath);
                }
                catch
                {
                    if (!File.Exists(destinationPath) &&
                        File.Exists(rollbackPath))
                    {
                        File.Move(rollbackPath, destinationPath);
                    }

                    throw;
                }
            }

            if (File.Exists(rollbackPath))
            {
                File.Delete(rollbackPath);
            }
        }

        internal static bool FilesEqual(string firstPath, string secondPath)
        {
            FileInfo first = new FileInfo(firstPath);
            FileInfo second = new FileInfo(secondPath);

            if (first.Length != second.Length)
            {
                return false;
            }

            const int bufferSize = 8192;
            byte[] firstBuffer = new byte[bufferSize];
            byte[] secondBuffer = new byte[bufferSize];

            using (FileStream firstStream = new FileStream(
                firstPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (FileStream secondStream = new FileStream(
                secondPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                while (true)
                {
                    int firstRead = firstStream.Read(
                        firstBuffer,
                        0,
                        firstBuffer.Length);
                    int secondRead = secondStream.Read(
                        secondBuffer,
                        0,
                        secondBuffer.Length);

                    if (firstRead != secondRead)
                    {
                        return false;
                    }

                    if (firstRead == 0)
                    {
                        return true;
                    }

                    for (int index = 0; index < firstRead; index++)
                    {
                        if (firstBuffer[index] != secondBuffer[index])
                        {
                            return false;
                        }
                    }
                }
            }
        }

        internal static DateTime GetLastWriteTimeUtcSafe(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private bool TryLoadFile(
            string path,
            out JusticeRecognitionSaveData data)
        {
            data = null;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Length <= 0L ||
                    file.Length > MaximumSaveBytes)
                {
                    throw new InvalidDataException(
                        "Taille de sauvegarde reconnaissance invalide.");
                }

                XmlSerializer serializer =
                    new XmlSerializer(
                        typeof(JusticeRecognitionSaveData));

                XmlReaderSettings settings =
                    new XmlReaderSettings
                    {
                        DtdProcessing =
                            DtdProcessing.Prohibit,
                        XmlResolver = null,
                        CloseInput = true
                    };

                using (FileStream stream =
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                using (XmlReader reader =
                    XmlReader.Create(
                        stream,
                        settings))
                {
                    data =
                        serializer.Deserialize(reader)
                        as JusticeRecognitionSaveData;
                }

                if (data == null ||
                    data.SchemaVersion != RecognitionPolicy.SchemaVersion)
                {
                    throw new InvalidDataException(
                        "Version de sauvegarde reconnaissance non prise en charge.");
                }

                return true;
            }
            catch (Exception exception)
            {
                _logger.Error(
                    "save_load_failed_" +
                    Path.GetFileName(path),
                    exception);

                data = null;
                return false;
            }
        }

        private static int GetCurrentGameTimeForRetry()
        {
            try
            {
                return Game.GameTime;
            }
            catch
            {
                return Environment.TickCount;
            }
        }

        private static int SafeAdd(
            int value,
            int delta)
        {
            if (value >
                int.MaxValue - delta)
            {
                return int.MaxValue;
            }

            return value + delta;
        }
    }

    internal sealed class RecognitionLogger
    {
        private readonly string _path;
        private readonly Dictionary<string, int> _lastErrorTimes =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        public RecognitionLogger(
            string path)
        {
            _path = path;
        }

        public void Info(
            string eventName,
            string message)
        {
            Write(
                "INFO",
                eventName,
                message);
        }

        public void Error(
            string eventName,
            Exception exception)
        {
            Write(
                "ERROR",
                eventName,
                exception != null
                    ? exception.ToString()
                    : "Exception inconnue");
        }

        public void ErrorRateLimited(
            string eventName,
            Exception exception,
            int intervalMilliseconds)
        {
            int now;

            try
            {
                now = Game.GameTime;
            }
            catch
            {
                now =
                    Environment.TickCount &
                    int.MaxValue;
            }

            int previous;

            if (_lastErrorTimes.TryGetValue(
                eventName,
                out previous) &&
                now - previous <
                intervalMilliseconds)
            {
                return;
            }

            _lastErrorTimes[eventName] = now;

            Error(
                eventName,
                exception);
        }

        private void Write(
            string level,
            string eventName,
            string message)
        {
            try
            {
                string line =
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:O} [{1}] {2} — {3}{4}",
                        DateTime.UtcNow,
                        level,
                        eventName ?? "event",
                        message ?? string.Empty,
                        Environment.NewLine);

                File.AppendAllText(
                    _path,
                    line,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Le logger ne doit jamais casser le gameplay.
            }
        }
    }

    internal sealed class RadiusBlipController
    {
        private const ulong AddBlipForRadiusHash =
            0x46818D79B1F7499AUL;

        private const ulong SetBlipColourHash =
            0x03D7FB09E75D6B7EUL;

        private const ulong SetBlipAlphaHash =
            0x45FF974EEE1C8734UL;

        private const ulong SetBlipAsShortRangeHash =
            0xBE8BE4FE60E27B72UL;

        private readonly RecognitionLogger _logger;

        private int _handle;
        private object _blipObject;
        private long _episodeId;
        private PositionData _center;
        private float _radius;

        public RadiusBlipController(
            RecognitionLogger logger)
        {
            _logger = logger;
        }

        public bool IsFor(
            long episodeId,
            PositionData center,
            float radius)
        {
            if (_handle == 0 ||
                !IsBlipAlive(_blipObject) ||
                center == null ||
                _center == null ||
                _episodeId != episodeId)
            {
                return false;
            }

            return Math.Abs(
                       _radius - radius) < 0.1f &&
                   Math.Abs(
                       _center.X - center.X) < 0.1f &&
                   Math.Abs(
                       _center.Y - center.Y) < 0.1f &&
                   Math.Abs(
                       _center.Z - center.Z) < 0.1f;
        }

        public void Create(
            long episodeId,
            PositionData center,
            float radius,
            int color,
            int alpha)
        {
            Remove();

            if (center == null ||
                !center.IsFinite() ||
                radius <= 0.0f)
            {
                return;
            }

            try
            {
                _handle =
                    Function.Call<int>(
                        (Hash)AddBlipForRadiusHash,
                        new InputArgument[]
                        {
                            center.X,
                            center.Y,
                            center.Z,
                            radius
                        });

                if (_handle == 0)
                {
                    return;
                }

                Function.Call(
                    (Hash)SetBlipColourHash,
                    _handle,
                    color);

                Function.Call(
                    (Hash)SetBlipAlphaHash,
                    _handle,
                    alpha);

                Function.Call(
                    (Hash)SetBlipAsShortRangeHash,
                    _handle,
                    false);

                _blipObject =
                    CreateBlipWrapper(_handle);

                _episodeId = episodeId;
                _center = center.Clone();
                _radius = radius;
            }
            catch (Exception exception)
            {
                _logger.Error(
                    "radius_blip_create_failed",
                    exception);

                // Je retire aussi le handle déjà créé lorsque la configuration
                // d'une propriété échoue après ADD_BLIP_FOR_RADIUS.
                if (_blipObject == null && _handle != 0)
                {
                    _blipObject = CreateBlipWrapper(_handle);
                }
                Remove();
            }
        }

        private static bool IsBlipAlive(object blipObject)
        {
            if (blipObject == null)
            {
                return false;
            }

            try
            {
                MethodInfo existsMethod = blipObject.GetType().GetMethod(
                    "Exists",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (existsMethod == null)
                {
                    return true;
                }

                object result = existsMethod.Invoke(blipObject, null);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        public void Remove()
        {
            if (_handle == 0 &&
                _blipObject == null)
            {
                Reset();
                return;
            }

            try
            {
                if (_blipObject != null)
                {
                    MethodInfo deleteMethod =
                        _blipObject.GetType().GetMethod(
                            "Delete",
                            BindingFlags.Public |
                            BindingFlags.Instance);

                    if (deleteMethod == null)
                    {
                        deleteMethod =
                            _blipObject.GetType().GetMethod(
                                "Remove",
                                BindingFlags.Public |
                                BindingFlags.Instance);
                    }

                    if (deleteMethod != null)
                    {
                        deleteMethod.Invoke(
                            _blipObject,
                            null);
                    }
                    else if (_handle != 0)
                    {
                        Function.Call(
                            (Hash)SetBlipAlphaHash,
                            _handle,
                            0);
                    }
                }
                else if (_handle != 0)
                {
                    Function.Call(
                        (Hash)SetBlipAlphaHash,
                        _handle,
                        0);
                }
            }
            catch (Exception exception)
            {
                _logger.ErrorRateLimited(
                    "radius_blip_remove_failed",
                    exception,
                    5000);
            }
            finally
            {
                Reset();
            }
        }

        private static object CreateBlipWrapper(
            int handle)
        {
            Type blipType =
                FindType("GTA.Blip");

            if (blipType == null)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(
                    blipType,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance,
                    null,
                    new object[]
                    {
                        handle
                    },
                    CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private void Reset()
        {
            _handle = 0;
            _blipObject = null;
            _episodeId = 0L;
            _center = null;
            _radius = 0.0f;
        }

        private static Type FindType(
            string fullName)
        {
            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();

            int index;

            for (index = 0;
                 index < assemblies.Length;
                 index++)
            {
                try
                {
                    Type type =
                        assemblies[index].GetType(
                            fullName,
                            false);

                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Ignoré.
                }
            }

            return null;
        }
    }

    internal sealed class JusticeRecognitionHud : IDisposable
    {
        private readonly RecognitionLogger _logger;

        private readonly ReflectionPngSprite _vehicleSprite;
        private readonly ReflectionPngSprite _outfitSprite;
        private readonly ReflectionPngSprite _warrantSprite;

        public JusticeRecognitionHud(
            string assetsDirectory,
            RecognitionLogger logger)
        {
            _logger = logger;

            _vehicleSprite =
                ReflectionPngSprite.TryCreate(
                    Path.Combine(
                        assetsDirectory,
                        "immatriculation.png"),
                    logger);

            _outfitSprite =
                ReflectionPngSprite.TryCreate(
                    Path.Combine(
                        assetsDirectory,
                        "tenue.png"),
                    logger);

            _warrantSprite =
                ReflectionPngSprite.TryCreate(
                    Path.Combine(
                        assetsDirectory,
                        "mandat.png"),
                    logger);
        }

        public void Draw(
            int activeVehicleCount,
            int neutralizedVehicleCount,
            bool currentVehicleMatches,
            int activeOutfitCount,
            bool currentOutfitMatches,
            bool zoneActive,
            bool insideZone,
            DateTime zoneExpiresUtc,
            DateTime nowUtc)
        {
            int screenWidth;
            int screenHeight;

            NativeUi.GetScreenResolution(
                out screenWidth,
                out screenHeight);

            int iconSize =
                Math.Max(
                    34,
                    Math.Min(
                        48,
                        screenHeight / 22));

            int gap =
                Math.Max(
                    8,
                    iconSize / 4);

            int top =
                Math.Max(
                    24,
                    screenHeight / 35);

            int startX =
                screenWidth -
                iconSize * 3 -
                gap * 2 -
                Math.Max(
                    25,
                    screenWidth / 70);

            int index = 0;

            if (activeVehicleCount > 0)
            {
                byte alpha;

                if (currentVehicleMatches)
                {
                    alpha = 255;
                }
                else if (neutralizedVehicleCount >=
                         activeVehicleCount)
                {
                    alpha = 65;
                }
                else
                {
                    alpha = 125;
                }

                DrawIcon(
                    _vehicleSprite,
                    "P",
                    startX +
                    index * (iconSize + gap),
                    top,
                    iconSize,
                    alpha,
                    screenWidth,
                    screenHeight);

                if (activeVehicleCount > 1)
                {
                    NativeUi.DrawText(
                        "x" +
                        activeVehicleCount.ToString(
                            CultureInfo.InvariantCulture),
                        startX +
                        index * (iconSize + gap) +
                        iconSize - 4,
                        top + iconSize - 10,
                        screenWidth,
                        screenHeight,
                        0.27f,
                        255);
                }

                index++;
            }

            if (activeOutfitCount > 0)
            {
                DrawIcon(
                    _outfitSprite,
                    "T",
                    startX +
                    index * (iconSize + gap),
                    top,
                    iconSize,
                    currentOutfitMatches
                        ? (byte)255
                        : (byte)125,
                    screenWidth,
                    screenHeight);

                if (activeOutfitCount > 1)
                {
                    NativeUi.DrawText(
                        "x" +
                        activeOutfitCount.ToString(
                            CultureInfo.InvariantCulture),
                        startX +
                        index * (iconSize + gap) +
                        iconSize - 4,
                        top + iconSize - 10,
                        screenWidth,
                        screenHeight,
                        0.27f,
                        255);
                }

                index++;
            }

            if (zoneActive)
            {
                int warrantX =
                    startX +
                    index * (iconSize + gap);

                DrawIcon(
                    _warrantSprite,
                    "M",
                    warrantX,
                    top,
                    iconSize,
                    insideZone
                        ? (byte)255
                        : (byte)130,
                    screenWidth,
                    screenHeight);

                double remaining =
                    Math.Max(
                        0.0,
                        (zoneExpiresUtc -
                         nowUtc).TotalSeconds);

                NativeUi.DrawText(
                    FormatDuration(remaining),
                    warrantX +
                    iconSize / 2,
                    top +
                    iconSize +
                    4,
                    screenWidth,
                    screenHeight,
                    0.27f,
                    insideZone
                        ? 255
                        : 180);
            }
        }

        private static void DrawIcon(
            ReflectionPngSprite sprite,
            string fallbackText,
            int x,
            int y,
            int size,
            byte alpha,
            int screenWidth,
            int screenHeight)
        {
            NativeUi.DrawRectanglePixels(
                x - 3,
                y - 3,
                size + 6,
                size + 6,
                screenWidth,
                screenHeight,
                7,
                19,
                35,
                Math.Max(
                    60,
                    alpha / 2));

            if (sprite != null &&
                sprite.Draw(
                    x,
                    y,
                    size,
                    size,
                    alpha,
                    screenWidth,
                    screenHeight))
            {
                return;
            }

            NativeUi.DrawRectanglePixels(
                x,
                y,
                size,
                size,
                screenWidth,
                screenHeight,
                22,
                85,
                145,
                alpha);

            NativeUi.DrawText(
                fallbackText,
                x + size / 2,
                y + size / 4,
                screenWidth,
                screenHeight,
                0.42f,
                alpha);
        }

        private static string FormatDuration(
            double totalSeconds)
        {
            int seconds =
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        totalSeconds));

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}",
                seconds / 60,
                seconds % 60);
        }

        public void Dispose()
        {
            if (_vehicleSprite != null)
            {
                _vehicleSprite.Dispose();
            }

            if (_outfitSprite != null)
            {
                _outfitSprite.Dispose();
            }

            if (_warrantSprite != null)
            {
                _warrantSprite.Dispose();
            }
        }
    }

    internal sealed class ReflectionPngSprite : IDisposable
    {
        private const int MaximumConsecutiveDrawFailures = 3;

        private readonly object _instance;
        private readonly PropertyInfo _positionProperty;
        private readonly PropertyInfo _sizeProperty;
        private readonly PropertyInfo _colorProperty;
        private readonly PropertyInfo _centeredProperty;
        private readonly MethodInfo _drawMethod;
        private readonly MethodInfo _disposeMethod;
        private readonly RecognitionLogger _logger;
        private int _consecutiveDrawFailures;
        private bool _drawDisabled;

        private ReflectionPngSprite(
            object instance,
            PropertyInfo positionProperty,
            PropertyInfo sizeProperty,
            PropertyInfo colorProperty,
            PropertyInfo centeredProperty,
            MethodInfo drawMethod,
            MethodInfo disposeMethod,
            RecognitionLogger logger)
        {
            _instance = instance;
            _positionProperty = positionProperty;
            _sizeProperty = sizeProperty;
            _colorProperty = colorProperty;
            _centeredProperty = centeredProperty;
            _drawMethod = drawMethod;
            _disposeMethod = disposeMethod;
            _logger = logger;
        }

        public static ReflectionPngSprite TryCreate(
            string path,
            RecognitionLogger logger)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                logger.Info(
                    "hud_asset_missing",
                    "Asset absent, fallback utilisé : " +
                    (path ?? string.Empty));

                return null;
            }

            try
            {
                Type spriteType =
                    FindType(
                        "GTA.UI.CustomSprite");

                if (spriteType == null)
                {
                    logger.Info(
                        "custom_sprite_unavailable",
                        "GTA.UI.CustomSprite indisponible, fallback HUD utilisé.");

                    return null;
                }

                object instance =
                    CreateSpriteInstance(
                        spriteType,
                        path);

                if (instance == null)
                {
                    return null;
                }

                PropertyInfo positionProperty =
                    spriteType.GetProperty(
                        "Position",
                        BindingFlags.Public |
                        BindingFlags.Instance);

                PropertyInfo sizeProperty =
                    spriteType.GetProperty(
                        "Size",
                        BindingFlags.Public |
                        BindingFlags.Instance);

                PropertyInfo colorProperty =
                    spriteType.GetProperty(
                        "Color",
                        BindingFlags.Public |
                        BindingFlags.Instance);

                PropertyInfo centeredProperty =
                    spriteType.GetProperty(
                        "Centered",
                        BindingFlags.Public |
                        BindingFlags.Instance);

                MethodInfo drawMethod =
                    FindDrawMethod(spriteType);

                if (!IsWritablePointProperty(positionProperty) ||
                    !IsWritableSizeProperty(sizeProperty) ||
                    drawMethod == null)
                {
                    IDisposable disposable = instance as IDisposable;
                    if (disposable != null)
                    {
                        disposable.Dispose();
                    }
                    return null;
                }

                MethodInfo disposeMethod =
                    spriteType.GetMethod(
                        "Dispose",
                        BindingFlags.Public |
                        BindingFlags.Instance);

                ReflectionPngSprite sprite = new ReflectionPngSprite(
                    instance,
                    positionProperty,
                    sizeProperty,
                    colorProperty,
                    centeredProperty,
                    drawMethod,
                    disposeMethod,
                    logger);

                logger.Info(
                    "hud_asset_loaded",
                    "Asset PNG chargé par GTA.UI.CustomSprite : " + path);

                return sprite;
            }
            catch (Exception exception)
            {
                logger.Error(
                    "hud_asset_load_failed",
                    exception);

                return null;
            }
        }

        public bool Draw(
            int x,
            int y,
            int width,
            int height,
            byte alpha,
            int screenWidth,
            int screenHeight)
        {
            if (_drawDisabled)
            {
                return false;
            }

            try
            {
                PointF logicalPosition = ConvertPixelPointToLogical(
                    x,
                    y,
                    screenWidth,
                    screenHeight);

                SizeF logicalSize = ConvertPixelSizeToLogical(
                    width,
                    height,
                    screenWidth,
                    screenHeight);

                SetPointValue(
                    _positionProperty,
                    logicalPosition.X,
                    logicalPosition.Y);

                SetSizeValue(
                    _sizeProperty,
                    logicalSize.Width,
                    logicalSize.Height);

                if (_colorProperty != null &&
                    _colorProperty.CanWrite &&
                    _colorProperty.PropertyType ==
                    typeof(Color))
                {
                    _colorProperty.SetValue(
                        _instance,
                        Color.FromArgb(
                            alpha,
                            255,
                            255,
                            255),
                        null);
                }

                if (_centeredProperty != null &&
                    _centeredProperty.CanWrite &&
                    _centeredProperty.PropertyType ==
                    typeof(bool))
                {
                    _centeredProperty.SetValue(
                        _instance,
                        false,
                        null);
                }

                ParameterInfo[] parameters =
                    _drawMethod.GetParameters();

                if (parameters.Length == 0)
                {
                    _drawMethod.Invoke(
                        _instance,
                        null);
                }
                else
                {
                    object argument =
                        CreateDefaultArgument(
                            parameters[0].ParameterType);

                    _drawMethod.Invoke(
                        _instance,
                        new[]
                        {
                            argument
                        });
                }

                _consecutiveDrawFailures = 0;
                return true;
            }
            catch (Exception exception)
            {
                _consecutiveDrawFailures++;
                if (_consecutiveDrawFailures >=
                    MaximumConsecutiveDrawFailures)
                {
                    _drawDisabled = true;
                    if (_logger != null)
                    {
                        _logger.ErrorRateLimited(
                            "hud_asset_draw_disabled",
                            exception,
                            5000);
                    }
                }
                return false;
            }
        }

        internal static PointF ConvertPixelPointToLogical(
            int x,
            int y,
            int screenWidth,
            int screenHeight)
        {
            int safeWidth = Math.Max(1, screenWidth);
            int safeHeight = Math.Max(1, screenHeight);

            return new PointF(
                x * 1280.0f / safeWidth,
                y * 720.0f / safeHeight);
        }

        internal static SizeF ConvertPixelSizeToLogical(
            int width,
            int height,
            int screenWidth,
            int screenHeight)
        {
            int safeWidth = Math.Max(1, screenWidth);
            int safeHeight = Math.Max(1, screenHeight);

            return new SizeF(
                Math.Max(0, width) * 1280.0f / safeWidth,
                Math.Max(0, height) * 720.0f / safeHeight);
        }

        public void Dispose()
        {
            try
            {
                if (_disposeMethod != null)
                {
                    _disposeMethod.Invoke(
                        _instance,
                        null);
                }
            }
            catch
            {
                // Ignoré.
            }
        }

        private void SetPointValue(
            PropertyInfo property,
            float x,
            float y)
        {
            if (property == null ||
                !property.CanWrite)
            {
                return;
            }

            Type type =
                property.PropertyType;

            if (type == typeof(Point))
            {
                property.SetValue(
                    _instance,
                    new Point(
                        (int)Math.Round(x),
                        (int)Math.Round(y)),
                    null);
            }
            else if (type == typeof(PointF))
            {
                property.SetValue(
                    _instance,
                    new PointF(x, y),
                    null);
            }
        }

        private void SetSizeValue(
            PropertyInfo property,
            float width,
            float height)
        {
            if (property == null ||
                !property.CanWrite)
            {
                return;
            }

            Type type =
                property.PropertyType;

            if (type == typeof(Size))
            {
                property.SetValue(
                    _instance,
                    new Size(
                        Math.Max(1, (int)Math.Round(width)),
                        Math.Max(1, (int)Math.Round(height))),
                    null);
            }
            else if (type == typeof(SizeF))
            {
                property.SetValue(
                    _instance,
                    new SizeF(width, height),
                    null);
            }
        }

        private static object CreateSpriteInstance(
            Type spriteType,
            string path)
        {
            ConstructorInfo[] constructors =
                spriteType.GetConstructors(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

            int constructorIndex;

            for (constructorIndex = 0;
                 constructorIndex < constructors.Length;
                 constructorIndex++)
            {
                ConstructorInfo constructor =
                    constructors[constructorIndex];

                ParameterInfo[] parameters =
                    constructor.GetParameters();

                if (parameters.Length == 0 ||
                    parameters[0].ParameterType !=
                    typeof(string))
                {
                    continue;
                }

                object[] arguments =
                    new object[parameters.Length];

                arguments[0] = path;

                int parameterIndex;
                bool supported = true;

                for (parameterIndex = 1;
                     parameterIndex < parameters.Length;
                     parameterIndex++)
                {
                    Type parameterType =
                        parameters[parameterIndex].ParameterType;

                    object argument =
                        CreateDefaultArgument(
                            parameterType);

                    if (argument == null &&
                        parameterType.IsValueType)
                    {
                        supported = false;
                        break;
                    }

                    arguments[parameterIndex] =
                        argument;
                }

                if (!supported)
                {
                    continue;
                }

                try
                {
                    return constructor.Invoke(arguments);
                }
                catch
                {
                    // Essayer le constructeur suivant.
                }
            }

            return null;
        }

        private static MethodInfo FindDrawMethod(
            Type spriteType)
        {
            MethodInfo[] methods =
                spriteType.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Instance);

            for (int index = 0;
                 index < methods.Length;
                 index++)
            {
                MethodInfo method =
                    methods[index];

                if (string.Equals(
                    method.Name,
                    "Draw",
                    StringComparison.Ordinal) &&
                    method.GetParameters().Length == 0)
                {
                    return method;
                }
            }

            for (int index = 0;
                 index < methods.Length;
                 index++)
            {
                MethodInfo method =
                    methods[index];

                if (string.Equals(
                    method.Name,
                    "Draw",
                    StringComparison.Ordinal) &&
                    method.GetParameters().Length == 1)
                {
                    return method;
                }
            }

            return null;
        }

        private static bool IsWritablePointProperty(
            PropertyInfo property)
        {
            return property != null &&
                   property.CanWrite &&
                   (property.PropertyType == typeof(Point) ||
                    property.PropertyType == typeof(PointF));
        }

        private static bool IsWritableSizeProperty(
            PropertyInfo property)
        {
            return property != null &&
                   property.CanWrite &&
                   (property.PropertyType == typeof(Size) ||
                    property.PropertyType == typeof(SizeF));
        }

        private static object CreateDefaultArgument(
            Type type)
        {
            if (type == typeof(Point))
            {
                return new Point(0, 0);
            }

            if (type == typeof(PointF))
            {
                return new PointF(0.0f, 0.0f);
            }

            if (type == typeof(Size))
            {
                return new Size(1, 1);
            }

            if (type == typeof(SizeF))
            {
                return new SizeF(1.0f, 1.0f);
            }

            if (type == typeof(Color))
            {
                return Color.White;
            }

            if (type == typeof(float))
            {
                return 0.0f;
            }

            if (type == typeof(double))
            {
                return 0.0;
            }

            if (type == typeof(int))
            {
                return 0;
            }

            if (type == typeof(bool))
            {
                return false;
            }

            if (!type.IsValueType)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(
            string fullName)
        {
            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();

            int index;

            for (index = 0;
                 index < assemblies.Length;
                 index++)
            {
                try
                {
                    AssemblyName assemblyName = assemblies[index].GetName();
                    if (!string.Equals(
                            assemblyName.Name,
                            "NIBScriptHookVDotNet3",
                            StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.Version == null ||
                        assemblyName.Version.Major < 3)
                    {
                        continue;
                    }

                    Type type =
                        assemblies[index].GetType(
                            fullName,
                            false);

                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Ignoré.
                }
            }

            return null;
        }
    }

    internal static class NativeUi
    {
        private const ulong DrawRectHash =
            0x3A618A217E5154F0UL;

        private const ulong SetTextFontHash =
            0x66E0276CC5F6B9DAUL;

        private const ulong SetTextScaleHash =
            0x07C837F9A01C34C9UL;

        private const ulong SetTextColourHash =
            0xBE6B23FFA53FB442UL;

        private const ulong SetTextCentreHash =
            0xC02F4DBFB51D988BUL;

        private const ulong SetTextOutlineHash =
            0x2513DFB0FB8400FEUL;

        private const ulong BeginTextCommandDisplayTextHash =
            0x25FBB336DF1804CBUL;

        private const ulong AddTextComponentSubstringPlayerNameHash =
            0x6C188BE134E074AAUL;

        private const ulong EndTextCommandDisplayTextHash =
            0xCD015E5BB0D96A57UL;

        private const ulong BeginTextCommandThefeedPostHash =
            0x202709F4C58A0424UL;

        private const ulong EndTextCommandThefeedPostTickerHash =
            0x2ED7843F8F801023UL;

        private const ulong GetActiveScreenResolutionHash =
            0x873C9F3104101DD3UL;

        public static void Notify(
            string text)
        {
            try
            {
                Function.Call(
                    (Hash)BeginTextCommandThefeedPostHash,
                    "STRING");

                Function.Call(
                    (Hash)AddTextComponentSubstringPlayerNameHash,
                    text ?? string.Empty);

                Function.Call<int>(
                    (Hash)EndTextCommandThefeedPostTickerHash,
                    false,
                    false);
            }
            catch
            {
                // Une notification ne doit jamais casser le script.
            }
        }

        public static void GetScreenResolution(
            out int width,
            out int height)
        {
            width = 1920;
            height = 1080;

            try
            {
                OutputArgument outputWidth =
                    new OutputArgument();

                OutputArgument outputHeight =
                    new OutputArgument();

                Function.Call(
                    (Hash)GetActiveScreenResolutionHash,
                    outputWidth,
                    outputHeight);

                int detectedWidth =
                    outputWidth.GetResult<int>();

                int detectedHeight =
                    outputHeight.GetResult<int>();

                if (detectedWidth >= 640 &&
                    detectedHeight >= 480)
                {
                    width = detectedWidth;
                    height = detectedHeight;
                }
            }
            catch
            {
                // Résolution de secours 1920x1080.
            }
        }

        public static void DrawRectanglePixels(
            int x,
            int y,
            int width,
            int height,
            int screenWidth,
            int screenHeight,
            int red,
            int green,
            int blue,
            int alpha)
        {
            if (screenWidth <= 0 ||
                screenHeight <= 0 ||
                width <= 0 ||
                height <= 0)
            {
                return;
            }

            float normalizedWidth =
                width /
                (float)screenWidth;

            float normalizedHeight =
                height /
                (float)screenHeight;

            float centerX =
                (x + width / 2.0f) /
                screenWidth;

            float centerY =
                (y + height / 2.0f) /
                screenHeight;

            try
            {
                Function.Call(
                    (Hash)DrawRectHash,
                    centerX,
                    centerY,
                    normalizedWidth,
                    normalizedHeight,
                    ClampByte(red),
                    ClampByte(green),
                    ClampByte(blue),
                    ClampByte(alpha),
                    false);
            }
            catch
            {
                // Ignoré.
            }
        }

        public static void DrawText(
            string text,
            int centerX,
            int topY,
            int screenWidth,
            int screenHeight,
            float scale,
            int alpha)
        {
            if (screenWidth <= 0 ||
                screenHeight <= 0)
            {
                return;
            }

            try
            {
                Function.Call(
                    (Hash)SetTextFontHash,
                    0);

                Function.Call(
                    (Hash)SetTextScaleHash,
                    0.0f,
                    scale);

                Function.Call(
                    (Hash)SetTextColourHash,
                    255,
                    255,
                    255,
                    ClampByte(alpha));

                Function.Call(
                    (Hash)SetTextCentreHash,
                    true);

                Function.Call(
                    (Hash)SetTextOutlineHash);

                Function.Call(
                    (Hash)BeginTextCommandDisplayTextHash,
                    "STRING");

                Function.Call(
                    (Hash)AddTextComponentSubstringPlayerNameHash,
                    text ?? string.Empty);

                Function.Call(
                    (Hash)EndTextCommandDisplayTextHash,
                    centerX /
                    (float)screenWidth,
                    topY /
                    (float)screenHeight,
                    0);
            }
            catch
            {
                // Ignoré.
            }
        }

        private static int ClampByte(
            int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }
    }
}
