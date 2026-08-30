using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeWalRecoveryTests
{
    private static readonly long CreatedAtUtcTicks =
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

    [TestMethod]
    public void Wal_PersistsEveryLegalStateAndReloadsTheLatestTransition()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            Assert.AreEqual(1L, wal.Append(Record(JusticeWalState.Prepared, 10L)).Sequence);
            Assert.AreEqual(2L, wal.Append(Record(JusticeWalState.Attempted, 11L)).Sequence);
            Assert.AreEqual(3L, wal.Append(Record(JusticeWalState.Ambiguous, 12L)).Sequence);
            Assert.AreEqual(1, wal.GetOpenTransactions().Count);
            Assert.AreEqual(4L, wal.Append(Record(JusticeWalState.Confirmed, 13L)).Sequence);
            Assert.AreEqual(0, wal.GetOpenTransactions().Count);

            JusticeWriteAheadLog reloaded = new JusticeWriteAheadLog(path);
            JusticeWalRecord latest = reloaded.GetLatest("payment:one");
            Assert.IsNotNull(latest);
            Assert.AreEqual(JusticeWalState.Confirmed, latest.State);
            Assert.AreEqual(4L, latest.Sequence);
            Assert.AreEqual(13L, reloaded.GetDiagnostics().WalRevision);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, reloaded.GetDiagnostics().RecoveryStatus);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_HasOpenTransactionKindDistinguishesOpenAndTerminalWithoutListAllocation()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(
                "profile-reset:one",
                "ProfileResetResult",
                JusticeWalState.Prepared,
                1L));
            wal.Append(Record(
                "payment:other",
                "voluntary-fine",
                JusticeWalState.Prepared,
                1L));

            Assert.IsTrue(wal.HasOpenTransactionKind("ProfileResetResult"));
            Assert.IsTrue(wal.HasOpenTransactionKind("voluntary-fine"));
            Assert.IsFalse(wal.HasOpenTransactionKind("DeathFront"));

            wal.Append(Record(
                "profile-reset:one",
                "ProfileResetResult",
                JusticeWalState.Rejected,
                1L));
            Assert.IsFalse(
                wal.HasOpenTransactionKind("ProfileResetResult"),
                "Une transaction terminale ne doit plus bloquer son type.");
            Assert.IsTrue(
                wal.HasOpenTransactionKind("voluntary-fine"),
                "La terminaison d'un type ne doit pas masquer les autres ouverts.");

            JusticeWriteAheadLog reloaded = new JusticeWriteAheadLog(path);
            Assert.IsFalse(reloaded.HasOpenTransactionKind("ProfileResetResult"));
            Assert.IsTrue(reloaded.HasOpenTransactionKind("voluntary-fine"));

            string walSource = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Wal.cs"));
            string kindReader = ExtractSourceMethod(
                walSource,
                "internal bool HasOpenTransactionKind(string operationKind)");
            StringAssert.Contains(kindReader, "_latestByTransaction");
            Assert.IsFalse(
                kindReader.Contains("GetOpenTransactions("),
                "Le prédicat par type ne doit pas matérialiser la liste triée.");
            Assert.IsFalse(
                kindReader.Contains("new List<JusticeWalRecord>"),
                "La lecture chaude doit rester sans allocation de liste.");

            string resetSource = File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "DonJEnemySpawner",
                "DonJEnemySpawner.Justice.Persistence.ProfileReset.cs"));
            string resetGuard = ExtractSourceMethod(
                resetSource,
                "private bool HasOpenJusticeProfileResetWal()");
            StringAssert.Contains(resetGuard, "HasOpenTransactionKind(");
            StringAssert.Contains(
                resetGuard,
                "JusticeProfileResetWalOperationKind");
            Assert.IsFalse(
                resetGuard.Contains("GetOpenTransactions("),
                "Le garde appelé à chaque tick doit utiliser le prédicat WAL sans allocation.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_RejectsSkippedChangedAndPostTerminalTransitions()
    {
        string directory = CreateTempDirectory();
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(Path.Combine(directory, "justice.wal"));
            Assert.ThrowsException<InvalidOperationException>(delegate
            {
                wal.Append(Record(JusticeWalState.Attempted, 1L));
            });

            wal.Append(Record(JusticeWalState.Prepared, 1L));
            Assert.ThrowsException<InvalidOperationException>(delegate
            {
                wal.Append(new JusticeWalRecord(
                    "payment:one",
                    "different-operation",
                    0,
                    JusticeWalState.Attempted,
                    2L,
                    CreatedAtUtcTicks,
                    Fields()));
            });
            Assert.ThrowsException<InvalidOperationException>(delegate
            {
                wal.Append(Record(JusticeWalState.Confirmed, 2L));
            });

            wal.Append(Record(JusticeWalState.Rejected, 2L));
            Assert.ThrowsException<InvalidOperationException>(delegate
            {
                wal.Append(Record(JusticeWalState.Attempted, 3L));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_RepairsEveryPartialAttemptedFrameWithoutInventingTheAttempt()
    {
        string directory = CreateTempDirectory();
        try
        {
            string source = Path.Combine(directory, "source.wal");
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(source);
            wal.Append(Record(JusticeWalState.Prepared, 1L));
            int preparedLength = checked((int)new FileInfo(source).Length);
            wal.Append(Record(JusticeWalState.Attempted, 2L));
            byte[] complete = File.ReadAllBytes(source);

            for (int length = preparedLength + 1; length < complete.Length; length++)
            {
                string truncated = Path.Combine(directory, "truncated-" + length + ".wal");
                byte[] prefix = new byte[length];
                Buffer.BlockCopy(complete, 0, prefix, 0, length);
                File.WriteAllBytes(truncated, prefix);

                JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(truncated);
                Assert.AreEqual(
                    JusticeWalRecoveryStatus.TruncatedTail,
                    recovery.Status,
                    "Longueur partielle=" + length);
                Assert.AreEqual(1, recovery.Records.Count, "Longueur partielle=" + length);
                Assert.AreEqual(JusticeWalState.Prepared, recovery.Records[0].State);

                JusticeWriteAheadLog repaired = new JusticeWriteAheadLog(truncated);
                Assert.AreEqual(preparedLength, new FileInfo(truncated).Length);
                Assert.AreEqual(JusticeWalState.Prepared, repaired.GetLatest("payment:one").State);
                Assert.AreEqual(2L, repaired.Append(Record(JusticeWalState.Attempted, 2L)).Sequence);
                Assert.AreEqual(JusticeWalState.Attempted, repaired.GetLatest("payment:one").State);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_ChecksumCorruptionBlocksAnyFurtherTransition()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 1L));
            byte[] corrupted = File.ReadAllBytes(path);
            corrupted[corrupted.Length - 1] ^= 0x5A;
            File.WriteAllBytes(path, corrupted);

            JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Corrupt, recovery.Status);

            JusticeWriteAheadLog blocked = new JusticeWriteAheadLog(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Corrupt, blocked.GetDiagnostics().RecoveryStatus);
            Assert.ThrowsException<InvalidDataException>(delegate
            {
                blocked.Append(Record(JusticeWalState.Attempted, 2L));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_LostAcknowledgementAfterFlushIsIdempotentOnRetry()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        OneShotWalFaultInjector faults = new OneShotWalFaultInjector(
            JusticePersistenceFaultPoint.AfterWalFlush);
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path, faults);
            JusticeWalRecord prepared = Record(JusticeWalState.Prepared, 1L);
            Assert.ThrowsException<IOException>(delegate { wal.Append(prepared); });

            JusticeWalRecord retried = wal.Append(prepared);
            Assert.AreEqual(1L, retried.Sequence);
            JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, recovery.Status);
            Assert.AreEqual(1, recovery.Records.Count);
            Assert.AreEqual(JusticeWalState.Prepared, recovery.Records[0].State);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void DurabilityDiagnosticsExposeMemoryDiskAndWalRevisionsTogether()
    {
        JusticeRepositoryDiagnostics repository = new JusticeRepositoryDiagnostics(
            JusticeRepositoryState.Running,
            9L,
            9L,
            0L,
            7L,
            3L,
            0L,
            string.Empty);
        JusticeWalDiagnostics wal = new JusticeWalDiagnostics(
            5L,
            8L,
            1024L,
            1,
            JusticeWalRecoveryStatus.Clean,
            0L,
            string.Empty);

        JusticeDurabilityDiagnostics combined = new JusticeDurabilityDiagnostics(repository, wal);

        Assert.AreEqual(9L, combined.MemoryRevision);
        Assert.AreEqual(7L, combined.DiskRevision);
        Assert.AreEqual(8L, combined.WalRevision);
        Assert.AreEqual(5L, combined.WalSequence);
        Assert.IsFalse(combined.RepositoryCaughtUp);
        Assert.IsTrue(combined.WalHealthy);
    }

    [TestMethod]
    public void Wal_RejectsXmlAndPayloadsLargerThanTheCriticalEnvelope()
    {
        string directory = CreateTempDirectory();
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(
                Path.Combine(directory, "justice.wal"));
            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(new JusticeWalRecord(
                    "xml:one",
                    "Inventory",
                    0,
                    JusticeWalState.Prepared,
                    1L,
                    CreatedAtUtcTicks,
                    new[] { new JusticePersistenceField("Custody", "<Custody />") }));
            });
            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(new JusticeWalRecord(
                    "large:one",
                    "Inventory",
                    0,
                    JusticeWalState.Prepared,
                    1L,
                    CreatedAtUtcTicks,
                    new[] { new JusticePersistenceField("digest", new string('a', 257)) }));
            });
            string walPath = Path.Combine(directory, "justice.wal");
            Assert.IsTrue(!File.Exists(walPath) || new FileInfo(walPath).Length == 0L);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_CompactAfterDurableCheckpointLeavesAnEmptyHealthyJournal()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            wal.Append(Record(JusticeWalState.Attempted, 10L));
            wal.Append(Record(JusticeWalState.Ambiguous, 11L));
            wal.Append(Record(JusticeWalState.Confirmed, 11L));

            Assert.IsTrue(wal.CompactIfNoOpenTransactions());
            Assert.AreEqual(0L, new FileInfo(path).Length);
            Assert.AreEqual(0, wal.GetOpenTransactions().Count);

            JusticeWriteAheadLog reloaded = new JusticeWriteAheadLog(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, reloaded.GetDiagnostics().RecoveryStatus);
            Assert.AreEqual(0L, reloaded.GetDiagnostics().DurableLength);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Wal_CompactRefusesToEraseAnOpenAttempt()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            wal.Append(Record(JusticeWalState.Attempted, 10L));
            long durableLength = new FileInfo(path).Length;

            Assert.IsFalse(wal.CompactIfNoOpenTransactions());
            Assert.AreEqual(durableLength, new FileInfo(path).Length);
            Assert.AreEqual(1, wal.GetOpenTransactions().Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void InactiveProfileRecovery_RequiresACleanWalWithoutOpenTransaction()
    {
        string directory = CreateTempDirectory();
        string statePath = Path.Combine(directory, "_justice_state.xml");
        string walPath = Path.Combine(directory, "_justice_state.wal");
        try
        {
            string error;
            Assert.IsTrue(
                DonJEnemySpawner.TryProveJusticeInactiveProfileRecoveryWalClosed(
                    statePath,
                    out error),
                error);

            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(walPath);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            Assert.IsFalse(
                DonJEnemySpawner.TryProveJusticeInactiveProfileRecoveryWalClosed(
                    statePath,
                    out error));
            StringAssert.Contains(error, "transaction WAL reste ouverte");

            wal.Append(Record(JusticeWalState.Rejected, 10L));
            Assert.IsTrue(
                DonJEnemySpawner.TryProveJusticeInactiveProfileRecoveryWalClosed(
                    statePath,
                    out error),
                error);

            using (FileStream stream = new FileStream(
                walPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read))
            {
                stream.WriteByte(0x7F);
                stream.Flush(true);
            }
            Assert.IsFalse(
                DonJEnemySpawner.TryProveJusticeInactiveProfileRecoveryWalClosed(
                    statePath,
                    out error));
            StringAssert.Contains(error, "TruncatedTail");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static JusticeWalRecord Record(JusticeWalState state, long persistenceRevision)
    {
        return Record(
            "payment:one",
            "voluntary-fine",
            state,
            persistenceRevision);
    }

    private static JusticeWalRecord Record(
        string transactionId,
        string operationKind,
        JusticeWalState state,
        long persistenceRevision)
    {
        return new JusticeWalRecord(
            transactionId,
            operationKind,
            0,
            state,
            persistenceRevision,
            CreatedAtUtcTicks,
            Fields());
    }

    private static IEnumerable<JusticePersistenceField> Fields()
    {
        return new[]
        {
            new JusticePersistenceField("amount", "600"),
            new JusticePersistenceField("cashBefore", "1000")
        };
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "DonJJusticeWal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ExtractSourceMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Méthode source absente : " + signature);
        int openingBrace = source.IndexOf('{', start);
        Assert.IsTrue(openingBrace > start, "Corps source absent : " + signature);
        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }

        Assert.Fail("Corps source incomplet : " + signature);
        return string.Empty;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(
            AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GTA5modDEV.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        Assert.Fail("Racine du dépôt GTA5modDEV introuvable.");
        return string.Empty;
    }

    private sealed class OneShotWalFaultInjector : IJusticePersistenceFaultInjector
    {
        private readonly object _gate = new object();
        private JusticePersistenceFaultPoint? _point;

        internal OneShotWalFaultInjector(JusticePersistenceFaultPoint point)
        {
            _point = point;
        }

        public void Probe(JusticePersistenceFaultPoint point)
        {
            lock (_gate)
            {
                if (_point == point)
                {
                    _point = null;
                    throw new IOException("Acquittement WAL perdu apres Flush(true).");
                }
            }
        }
    }
}
