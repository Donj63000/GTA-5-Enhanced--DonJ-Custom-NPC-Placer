using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeDomainPersistenceInvariantTests
{
    [TestMethod]
    public void VisibleConvictions_KeepThePinnedCustodyConvictionPastTheBound()
    {
        JusticeRecordState record = new JusticeRecordState();
        JusticeConviction pinned = JusticePolicy.ApplyConviction(
            ConvictionCase("custody:main"),
            record,
            DateTime.UtcNow);
        Assert.IsNotNull(pinned);

        for (int index = 0; index < JusticePolicy.MaxConvictions + 20; index++)
        {
            Assert.IsNotNull(JusticePolicy.ApplyConviction(
                ConvictionCase("custody:main:discipline:" + index),
                record,
                DateTime.UtcNow.AddMinutes(index + 1)));
        }

        Assert.AreEqual(JusticePolicy.MaxConvictions, record.Convictions.Count);
        Assert.IsTrue(record.Convictions.Any(conviction =>
            conviction != null && string.Equals(
                conviction.ConvictionId,
                record.PinnedConvictionId,
                StringComparison.Ordinal)));
        Assert.IsFalse(record.Convictions.Any(conviction =>
            conviction != null && conviction.ConvictionId ==
            "conviction:custody:main:discipline:0"));
    }

    [TestMethod]
    public void VisibleConvictions_ReleaseTheOldPinWhenANewCustodyEpisodeIsJudged()
    {
        JusticeRecordState record = new JusticeRecordState();
        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            ConvictionCase("custody:old"),
            record,
            DateTime.UtcNow));

        for (int index = 0; index < JusticePolicy.MaxConvictions + 5; index++)
        {
            JusticePolicy.ApplyConviction(
                ConvictionCase("custody:old:discipline:" + index),
                record,
                DateTime.UtcNow.AddMinutes(index + 1));
        }

        Assert.IsNotNull(JusticePolicy.ApplyConviction(
            ConvictionCase("custody:new"),
            record,
            DateTime.UtcNow.AddHours(2)));
        for (int index = 0; index < JusticePolicy.MaxConvictions + 5; index++)
        {
            JusticePolicy.ApplyConviction(
                ConvictionCase("custody:new:discipline:" + index),
                record,
                DateTime.UtcNow.AddHours(3).AddMinutes(index));
        }

        Assert.AreEqual("conviction:custody:new", record.PinnedConvictionId);
        Assert.IsFalse(record.Convictions.Any(conviction =>
            conviction != null && conviction.ConvictionId == "conviction:custody:old"));
        Assert.IsTrue(record.Convictions.Any(conviction =>
            conviction != null && conviction.ConvictionId == record.PinnedConvictionId));
    }

    [TestMethod]
    public void FineDispute_IsBoundedExcludedFromDueAndClearedWithTheCase()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            VoluntaryFinePaid = 100L,
            FineInDispute = 400L
        };
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:fine-dispute",
            IncidentId = "fine-dispute",
            EpisodeId = "episode:fine-dispute",
            Points = 10,
            Fine = 1000L
        });

        state.RecalculateTotals();
        Assert.AreEqual(500L, state.FineDue);
        Assert.IsTrue(JusticePolicy.IsFineLedgerValid(state));
        Assert.AreEqual(500L, JusticePolicy.MoveFineToDispute(state, 900L));
        Assert.AreEqual(0L, state.FineDue);
        Assert.AreEqual(900L, state.FineInDispute);
        Assert.IsTrue(JusticePolicy.IsFineLedgerValid(state));

        state.ClearActiveCase(false);
        Assert.AreEqual(0L, state.FineInDispute);
        Assert.AreEqual(0L, state.FineDue);
    }

    [TestMethod]
    public void FineDispute_NormalizationPreventsLedgerOverflow()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            FineDue = long.MaxValue,
            VoluntaryFinePaid = JusticePolicy.MaxActiveFine,
            FineInDispute = long.MaxValue
        };

        JusticePolicy.NormalizeFineLedger(state);

        Assert.AreEqual(0L, state.FineDue);
        Assert.AreEqual(JusticePolicy.MaxActiveFine, state.VoluntaryFinePaid);
        Assert.AreEqual(0L, state.FineInDispute);
        Assert.IsTrue(JusticePolicy.IsFineLedgerValid(state));
    }

    private static JusticeCaseState ConvictionCase(string episode)
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            CustodyEpisodeId = episode
        };
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:" + episode,
            IncidentId = "incident:" + episode,
            EpisodeId = episode,
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Test",
            Points = 25,
            Fine = 100L,
            SentenceSeconds = 15
        });
        state.RecalculateTotals();
        return state;
    }
}

[TestClass]
public sealed class JusticeRepositoryTests
{
    [TestMethod]
    public void Snapshot_DeepCopiesEveryProfileAndFieldCollection()
    {
        List<JusticePersistenceField> profileFields = new List<JusticePersistenceField>
        {
            new JusticePersistenceField("case.fineDue", "1250")
        };
        JusticePersistenceProfileSnapshot profile = new JusticePersistenceProfileSnapshot(
            0,
            7L,
            "MICHAEL",
            profileFields);
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot> { profile };
        List<JusticePersistenceField> globals = new List<JusticePersistenceField>
        {
            new JusticePersistenceField("activeSlot", "0")
        };

        JusticePersistenceSnapshot snapshot = new JusticePersistenceSnapshot(
            11L,
            2,
            DateTime.UtcNow.Ticks,
            0,
            globals,
            profiles);
        profileFields.Clear();
        profiles.Clear();
        globals.Clear();

        Assert.AreEqual(1, snapshot.GlobalFields.Count);
        Assert.AreEqual(1, snapshot.Profiles.Count);
        Assert.AreEqual(1, snapshot.Profiles[0].Fields.Count);
        Assert.AreNotSame(profile, snapshot.Profiles[0]);
        Assert.AreEqual("1250", snapshot.Profiles[0].Fields[0].Value);
    }

    [TestMethod]
    public void Repository_WritesOnDedicatedThreadAndKeepsOnlyTheLatestPendingRevision()
    {
        RecordingCodec codec = new RecordingCodec();
        BlockingMemoryStore store = new BlockingMemoryStore();
        JusticeRepository repository = new JusticeRepository(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "state.xml"),
            null,
            codec,
            0L,
            store,
            null,
            10);

        try
        {
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            Assert.AreEqual(
                JusticeRepositoryEnqueueResult.Accepted,
                repository.Enqueue(Snapshot(1L)));
            Assert.IsTrue(store.FirstWriteStarted.WaitOne(TimeSpan.FromSeconds(3)));

            Assert.AreEqual(
                JusticeRepositoryEnqueueResult.Accepted,
                repository.Enqueue(Snapshot(2L)));
            Assert.AreEqual(
                JusticeRepositoryEnqueueResult.Accepted,
                repository.Enqueue(Snapshot(3L)));
            store.ReleaseFirstWrite.Set();

            Assert.IsTrue(repository.Flush(3L, TimeSpan.FromSeconds(5)));
            CollectionAssert.AreEqual(new long[] { 1L, 3L }, store.WrittenRevisions.ToArray());
            Assert.IsTrue(codec.SerializationThreadIds.All(id => id != callerThread));

            JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
            Assert.AreEqual(3L, diagnostics.MemoryRevision);
            Assert.AreEqual(3L, diagnostics.DiskRevision);
            Assert.AreEqual(0L, diagnostics.PendingRevision);
            Assert.IsTrue(diagnostics.IsCaughtUp);
        }
        finally
        {
            store.ReleaseFirstWrite.Set();
            repository.Dispose();
            store.Dispose();
        }
    }

    [TestMethod]
    public void Repository_ReplaceFailureKeepsOldTargetAndRetriesTheSameRevision()
    {
        string directory = CreateTempDirectory();
        string statePath = Path.Combine(directory, "justice.xml");
        string backupPath = statePath + ".bak";
        RecordingCodec codec = new RecordingCodec();
        OneShotFaultInjector faults = new OneShotFaultInjector();
        JusticeRepository repository = new JusticeRepository(
            statePath,
            backupPath,
            codec,
            0L,
            new JusticeAtomicFileStore(),
            faults,
            10);

        try
        {
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Accepted, repository.Enqueue(Snapshot(1L)));
            Assert.IsTrue(repository.Flush(1L, TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1L, codec.ReadRevision(File.ReadAllBytes(statePath)));

            faults.Arm(JusticePersistenceFaultPoint.BeforeAtomicReplace);
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Accepted, repository.Enqueue(Snapshot(2L)));
            Assert.IsTrue(repository.Flush(2L, TimeSpan.FromSeconds(5)));

            Assert.AreEqual(2L, codec.ReadRevision(File.ReadAllBytes(statePath)));
            Assert.IsTrue(File.Exists(backupPath));
            Assert.AreEqual(1L, codec.ReadRevision(File.ReadAllBytes(backupPath)));
            Assert.IsTrue(repository.GetDiagnostics().WriteFailures >= 1L);
        }
        finally
        {
            repository.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Repository_StopIsBoundedAndReportsAnUnpersistedRevision()
    {
        AlwaysFailStore store = new AlwaysFailStore();
        JusticeRepository repository = new JusticeRepository(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "state.xml"),
            null,
            new RecordingCodec(),
            0L,
            store,
            null,
            5000);

        try
        {
            repository.Enqueue(Snapshot(1L));
            Assert.IsTrue(store.Attempted.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.IsFalse(repository.Stop(TimeSpan.FromSeconds(1)));

            JusticeRepositoryDiagnostics diagnostics = repository.GetDiagnostics();
            Assert.AreEqual(JusticeRepositoryState.Stopped, diagnostics.State);
            Assert.AreEqual(1L, diagnostics.MemoryRevision);
            Assert.AreEqual(0L, diagnostics.DiskRevision);
            Assert.IsFalse(diagnostics.IsCaughtUp);
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostics.LastError));
        }
        finally
        {
            repository.Dispose();
            store.Dispose();
        }
    }

    [TestMethod]
    public void Repository_StopTimeoutNeverWaitsForAHungDiskWriter()
    {
        HangingMemoryStore store = new HangingMemoryStore();
        JusticeRepository repository = new JusticeRepository(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "state.xml"),
            null,
            new RecordingCodec(),
            0L,
            store,
            null,
            10);

        try
        {
            repository.Enqueue(Snapshot(1L));
            Assert.IsTrue(store.Attempted.WaitOne(TimeSpan.FromSeconds(3)));

            Stopwatch stopwatch = Stopwatch.StartNew();
            Assert.IsFalse(repository.Stop(TimeSpan.FromMilliseconds(100)));
            stopwatch.Stop();
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                "L'arret borne ne doit pas attendre le backend disque bloque.");

            store.Release.Set();
            Assert.IsTrue(repository.Stop(TimeSpan.FromSeconds(3)));
            Assert.AreEqual(1L, repository.GetDiagnostics().DiskRevision);
        }
        finally
        {
            store.Release.Set();
            repository.Dispose();
            store.Dispose();
        }
    }

    [TestMethod]
    public void Repository_RejectsDuplicateStaleAndPostStopSnapshots()
    {
        BlockingMemoryStore store = new BlockingMemoryStore { BlockFirstWrite = false };
        JusticeRepository repository = new JusticeRepository(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "state.xml"),
            null,
            new RecordingCodec(),
            0L,
            store,
            null,
            10);

        try
        {
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Accepted, repository.Enqueue(Snapshot(2L)));
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Duplicate, repository.Enqueue(Snapshot(2L)));
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Stale, repository.Enqueue(Snapshot(1L)));
            Assert.IsTrue(repository.Flush(2L, TimeSpan.FromSeconds(3)));
            Assert.IsTrue(repository.Stop(TimeSpan.FromSeconds(3)));
            Assert.AreEqual(JusticeRepositoryEnqueueResult.Stopped, repository.Enqueue(Snapshot(3L)));
        }
        finally
        {
            repository.Dispose();
            store.Dispose();
        }
    }

    private static JusticePersistenceSnapshot Snapshot(long revision)
    {
        return new JusticePersistenceSnapshot(
            revision,
            2,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            -1,
            new[] { new JusticePersistenceField("revision", revision.ToString()) },
            new JusticePersistenceProfileSnapshot[0]);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "DonJJusticeRepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingCodec : IJusticePersistenceCodec
    {
        private readonly object _gate = new object();
        private readonly List<int> _threadIds = new List<int>();

        internal IList<int> SerializationThreadIds
        {
            get
            {
                lock (_gate)
                {
                    return _threadIds.ToArray();
                }
            }
        }

        public byte[] Serialize(JusticePersistenceSnapshot snapshot)
        {
            lock (_gate)
            {
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
            }

            return Encoding.UTF8.GetBytes(
                snapshot.Revision + "|" + snapshot.SchemaVersion + "|" +
                snapshot.CapturedAtUtcTicks + "|" + snapshot.ActiveProfileSlot);
        }

        public bool TryDeserialize(
            byte[] document,
            out JusticePersistenceSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            try
            {
                string[] values = Encoding.UTF8.GetString(document).Split('|');
                if (values.Length != 4)
                {
                    throw new InvalidDataException("Document de test invalide.");
                }

                snapshot = new JusticePersistenceSnapshot(
                    long.Parse(values[0]),
                    int.Parse(values[1]),
                    long.Parse(values[2]),
                    int.Parse(values[3]),
                    null,
                    null);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal long ReadRevision(byte[] document)
        {
            JusticePersistenceSnapshot snapshot;
            string error;
            Assert.IsTrue(TryDeserialize(document, out snapshot, out error), error);
            return snapshot.Revision;
        }
    }

    private sealed class BlockingMemoryStore : IJusticeAtomicFileStore, IDisposable
    {
        private readonly object _gate = new object();
        private byte[] _document;
        private int _writeCount;

        internal BlockingMemoryStore()
        {
            FirstWriteStarted = new ManualResetEvent(false);
            ReleaseFirstWrite = new ManualResetEvent(false);
            WrittenRevisions = new List<long>();
            BlockFirstWrite = true;
        }

        internal ManualResetEvent FirstWriteStarted { get; private set; }

        internal ManualResetEvent ReleaseFirstWrite { get; private set; }

        internal List<long> WrittenRevisions { get; private set; }

        internal bool BlockFirstWrite { get; set; }

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            int call = Interlocked.Increment(ref _writeCount);
            if (call == 1)
            {
                FirstWriteStarted.Set();
                if (BlockFirstWrite && !ReleaseFirstWrite.WaitOne(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Le test n'a pas libere la premiere ecriture.");
                }
            }

            byte[] copy = (byte[])document.Clone();
            long revision = long.Parse(Encoding.UTF8.GetString(copy).Split('|')[0]);
            lock (_gate)
            {
                _document = copy;
                WrittenRevisions.Add(revision);
            }
        }

        public byte[] ReadAllBytes(string path)
        {
            lock (_gate)
            {
                return _document == null ? null : (byte[])_document.Clone();
            }
        }

        public void Dispose()
        {
            FirstWriteStarted.Dispose();
            ReleaseFirstWrite.Dispose();
        }
    }

    private sealed class AlwaysFailStore : IJusticeAtomicFileStore, IDisposable
    {
        internal AlwaysFailStore()
        {
            Attempted = new ManualResetEvent(false);
        }

        internal ManualResetEvent Attempted { get; private set; }

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            Attempted.Set();
            throw new IOException("Panne disque injectee.");
        }

        public byte[] ReadAllBytes(string path)
        {
            throw new FileNotFoundException();
        }

        public void Dispose()
        {
            Attempted.Dispose();
        }
    }

    private sealed class HangingMemoryStore : IJusticeAtomicFileStore, IDisposable
    {
        private byte[] _document;

        internal HangingMemoryStore()
        {
            Attempted = new ManualResetEvent(false);
            Release = new ManualResetEvent(false);
        }

        internal ManualResetEvent Attempted { get; private set; }

        internal ManualResetEvent Release { get; private set; }

        public void WriteAtomically(
            string targetPath,
            string backupPath,
            byte[] document,
            IJusticePersistenceFaultInjector faultInjector)
        {
            Attempted.Set();
            if (!Release.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Backend disque de test toujours bloque.");
            }

            _document = (byte[])document.Clone();
        }

        public byte[] ReadAllBytes(string path)
        {
            return _document == null ? null : (byte[])_document.Clone();
        }

        public void Dispose()
        {
            Attempted.Dispose();
            Release.Dispose();
        }
    }

    private sealed class OneShotFaultInjector : IJusticePersistenceFaultInjector
    {
        private readonly object _gate = new object();
        private JusticePersistenceFaultPoint? _armed;

        internal void Arm(JusticePersistenceFaultPoint point)
        {
            lock (_gate)
            {
                _armed = point;
            }
        }

        public void Probe(JusticePersistenceFaultPoint point)
        {
            lock (_gate)
            {
                if (_armed == point)
                {
                    _armed = null;
                    throw new IOException("Panne injectee a " + point + ".");
                }
            }
        }
    }
}
