using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeWalConcurrencyTests
{
    private static readonly long CreatedAtUtcTicks =
        new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc).Ticks;

    [TestMethod]
    public void Compact_HoldsInterProcessLeaseUntilReplaceAndConcurrentAppendRemainsRetryable()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");
        BlockingWalFaultInjector faults = new BlockingWalFaultInjector(
            JusticePersistenceFaultPoint.BeforeWalCompactReplace);
        Task<bool> compaction = null;
        try
        {
            JusticeWriteAheadLog compactor = new JusticeWriteAheadLog(path, faults);
            compactor.Append(Record(
                "payment:terminal",
                JusticeWalState.Prepared,
                10L));
            compactor.Append(Record(
                "payment:terminal",
                JusticeWalState.Attempted,
                10L));
            compactor.Append(Record(
                "payment:terminal",
                JusticeWalState.Ambiguous,
                11L));
            compactor.Append(Record(
                "payment:terminal",
                JusticeWalState.Confirmed,
                11L));

            // Je charge la seconde instance avant la compaction pour reproduire
            // exactement la fenêtre historique validation -> remplacement.
            JusticeWriteAheadLog concurrentWriter = new JusticeWriteAheadLog(path);
            compaction = Task.Run(delegate { return compactor.CompactIfNoOpenTransactions(); });
            Assert.IsTrue(
                faults.Entered.Wait(TimeSpan.FromSeconds(3)),
                "La compaction n'a pas atteint le point précédant le remplacement.");

            Exception concurrentFailure = CaptureTaskFailure(Task.Run(delegate
            {
                concurrentWriter.Append(Record(
                    "payment:concurrent",
                    JusticeWalState.Prepared,
                    12L));
            }));

            Assert.IsInstanceOfType(concurrentFailure, typeof(IOException));
            StringAssert.Contains(
                concurrentFailure.Message.ToLowerInvariant(),
                "autre instance");
            Assert.IsFalse(
                compaction.IsCompleted,
                "La contention ne doit pas contourner le point de synchronisation du test.");

            faults.Release.Set();
            Assert.IsTrue(compaction.Wait(TimeSpan.FromSeconds(3)));
            Assert.IsTrue(compaction.Result);

            // Je rejoue l'append avec une vue fraîche après la contention : sa
            // frame doit devenir la seule autorité du WAL compacté, sans perte.
            JusticeWriteAheadLog retryWriter = new JusticeWriteAheadLog(path);
            JusticeWalRecord durable = retryWriter.Append(Record(
                "payment:concurrent",
                JusticeWalState.Prepared,
                12L));
            Assert.AreEqual(1L, durable.Sequence);

            JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, recovery.Status);
            Assert.AreEqual(1, recovery.Records.Count);
            Assert.AreEqual("payment:concurrent", recovery.Records[0].TransactionId);
            Assert.AreEqual(JusticeWalState.Prepared, recovery.Records[0].State);
        }
        finally
        {
            faults.Release.Set();
            if (compaction != null)
            {
                try
                {
                    compaction.Wait(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // Je laisse MSTest restituer l'échec principal du scénario.
                }
            }
            faults.Dispose();
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void Recover_CannotObserveOrRepairAFrameWhileAppendOwnsTheLease()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");
        BlockingWalFaultInjector faults = new BlockingWalFaultInjector(
            JusticePersistenceFaultPoint.BeforeWalFlush);
        Task append = null;
        try
        {
            JusticeWriteAheadLog writer = new JusticeWriteAheadLog(path, faults);
            append = Task.Run(delegate
            {
                writer.Append(Record(
                    "payment:in-flight",
                    JusticeWalState.Prepared,
                    10L));
            });
            Assert.IsTrue(
                faults.Entered.Wait(TimeSpan.FromSeconds(3)),
                "L'append n'a pas atteint le point situé entre écriture et flush.");

            Assert.ThrowsException<IOException>(delegate
            {
                JusticeWriteAheadLog.Recover(path);
            });
            Assert.ThrowsException<IOException>(delegate
            {
                new JusticeWriteAheadLog(path);
            });

            faults.Release.Set();
            Assert.IsTrue(append.Wait(TimeSpan.FromSeconds(3)));

            JusticeWalRecoveryResult recovery = JusticeWriteAheadLog.Recover(path);
            Assert.AreEqual(JusticeWalRecoveryStatus.Clean, recovery.Status);
            Assert.AreEqual(1, recovery.Records.Count);
            Assert.AreEqual("payment:in-flight", recovery.Records[0].TransactionId);
            Assert.AreEqual(
                JusticeWalState.Prepared,
                recovery.Records[0].State,
                "Recover ne doit ni tronquer ni inventer une transition en cours d'écriture.");
        }
        finally
        {
            faults.Release.Set();
            if (append != null)
            {
                try
                {
                    append.Wait(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // Je laisse MSTest restituer l'échec principal du scénario.
                }
            }
            faults.Dispose();
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void PrefixCheck_ExternalAccessFailureStaysRetryableAndDoesNotPoisonTheInstance()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");
        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(
                "payment:locked-prefix",
                JusticeWalState.Prepared,
                10L));

            using (new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                Assert.ThrowsException<IOException>(delegate
                {
                    wal.Append(Record(
                        "payment:locked-prefix",
                        JusticeWalState.Attempted,
                        11L));
                });
            }

            Assert.AreEqual(
                JusticeWalRecoveryStatus.Clean,
                wal.GetDiagnostics().RecoveryStatus,
                "Un refus d'ouverture ne doit pas être converti en corruption durable.");
            Assert.AreEqual(
                JusticeWalState.Prepared,
                wal.GetLatest("payment:locked-prefix").State);

            JusticeWalRecord retried = wal.Append(Record(
                "payment:locked-prefix",
                JusticeWalState.Attempted,
                11L));
            Assert.AreEqual(JusticeWalState.Attempted, retried.State);
            Assert.AreEqual(2L, retried.Sequence);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Exception CaptureTaskFailure(Task task)
    {
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(3)))
            {
                Assert.Fail("L'opération concurrente n'a pas terminé dans le délai borné.");
            }
            return null;
        }
        catch (AggregateException exception)
        {
            return exception.GetBaseException();
        }
    }

    private static JusticeWalRecord Record(
        string transactionId,
        JusticeWalState state,
        long persistenceRevision)
    {
        return new JusticeWalRecord(
            transactionId,
            "voluntary-fine",
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

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalConcurrency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class BlockingWalFaultInjector : IJusticePersistenceFaultInjector, IDisposable
    {
        private readonly JusticePersistenceFaultPoint _point;

        internal BlockingWalFaultInjector(JusticePersistenceFaultPoint point)
        {
            _point = point;
            Entered = new ManualResetEventSlim(false);
            Release = new ManualResetEventSlim(false);
        }

        internal ManualResetEventSlim Entered { get; private set; }

        internal ManualResetEventSlim Release { get; private set; }

        public void Probe(JusticePersistenceFaultPoint point)
        {
            if (point != _point)
            {
                return;
            }

            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new IOException(
                    "Le test n'a pas libéré le point de synchronisation WAL.");
            }
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
