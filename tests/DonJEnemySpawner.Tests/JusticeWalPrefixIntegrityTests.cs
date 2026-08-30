using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeWalPrefixIntegrityTests
{
    private static readonly long CreatedAtUtcTicks =
        new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc).Ticks;

    [TestMethod]
    public void Append_DetectsSameLengthDurablePrefixModificationBeforeAttemptedTransition()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DonJJusticeWalPrefix-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "justice.wal");
        Directory.CreateDirectory(directory);

        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));

            byte[] modifiedPrefix = File.ReadAllBytes(path);
            long durableLength = modifiedPrefix.LongLength;
            modifiedPrefix[modifiedPrefix.Length - 1] ^= 0x01;
            File.WriteAllBytes(path, modifiedPrefix);
            Assert.AreEqual(durableLength, new FileInfo(path).Length);

            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(Record(JusticeWalState.Attempted, 11L));
            });

            CollectionAssert.AreEqual(
                modifiedPrefix,
                File.ReadAllBytes(path),
                "L'append Attempted ne doit écrire aucun octet après la détection du préfixe modifié.");
            Assert.AreEqual(
                JusticeWalState.Prepared,
                wal.GetLatest("payment:prefix-integrity").State,
                "La transition refusée ne doit pas modifier l'autorité WAL en mémoire.");
            Assert.AreEqual(
                JusticeWalRecoveryStatus.Corrupt,
                wal.GetDiagnostics().RecoveryStatus);
            StringAssert.Contains(
                wal.GetDiagnostics().LastError.ToLowerInvariant(),
                "préfixe durable");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void Append_RejectsDeletedDurableWalWithoutRecreatingItOrForgettingAttemptedState()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");

        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            wal.Append(Record(JusticeWalState.Attempted, 11L));
            long durableLength = wal.GetDiagnostics().DurableLength;

            File.Delete(path);
            Assert.IsFalse(File.Exists(path));

            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(Record(JusticeWalState.Confirmed, 12L));
            });

            Assert.IsFalse(
                File.Exists(path),
                "L'append refusé ne doit pas recréer le WAL supprimé hors de l'instance.");
            AssertRejectedStateWasPreserved(
                wal,
                JusticeWalState.Attempted,
                durableLength);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void Append_RejectsShrinkToAValidPrefixWithoutRepairingOrForgettingAttemptedState()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");

        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            byte[] validPreparedPrefix = File.ReadAllBytes(path);
            wal.Append(Record(JusticeWalState.Attempted, 11L));
            long durableLength = wal.GetDiagnostics().DurableLength;
            Assert.IsTrue(validPreparedPrefix.LongLength < durableLength);

            File.WriteAllBytes(path, validPreparedPrefix);

            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(Record(JusticeWalState.Confirmed, 12L));
            });

            CollectionAssert.AreEqual(
                validPreparedPrefix,
                File.ReadAllBytes(path),
                "L'append refusé ne doit ni réparer ni rallonger le préfixe externe valide.");
            AssertRejectedStateWasPreserved(
                wal,
                JusticeWalState.Attempted,
                durableLength);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void Append_RejectsGrownWalWithoutTruncatingItOrAdvancingPreparedState()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "justice.wal");

        try
        {
            JusticeWriteAheadLog wal = new JusticeWriteAheadLog(path);
            wal.Append(Record(JusticeWalState.Prepared, 10L));
            long durableLength = wal.GetDiagnostics().DurableLength;
            byte[] durablePrefix = File.ReadAllBytes(path);
            byte[] grownWal = new byte[durablePrefix.Length + 3];
            Buffer.BlockCopy(durablePrefix, 0, grownWal, 0, durablePrefix.Length);
            grownWal[grownWal.Length - 3] = 0x44;
            grownWal[grownWal.Length - 2] = 0x4A;
            grownWal[grownWal.Length - 1] = 0x57;
            File.WriteAllBytes(path, grownWal);

            Assert.ThrowsException<InvalidDataException>(delegate
            {
                wal.Append(Record(JusticeWalState.Attempted, 11L));
            });

            CollectionAssert.AreEqual(
                grownWal,
                File.ReadAllBytes(path),
                "L'append refusé ne doit ni tronquer ni compléter le WAL allongé hors instance.");
            AssertRejectedStateWasPreserved(
                wal,
                JusticeWalState.Prepared,
                durableLength);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static JusticeWalRecord Record(
        JusticeWalState state,
        long persistenceRevision)
    {
        return new JusticeWalRecord(
            "payment:prefix-integrity",
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
            "DonJJusticeWalPrefix-" + Guid.NewGuid().ToString("N"));
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

    private static void AssertRejectedStateWasPreserved(
        JusticeWriteAheadLog wal,
        JusticeWalState expectedState,
        long expectedDurableLength)
    {
        JusticeWalRecord latest = wal.GetLatest("payment:prefix-integrity");
        Assert.IsNotNull(latest);
        Assert.AreEqual(
            expectedState,
            latest.State,
            "Le rejet externe doit conserver la dernière transition acquittée en mémoire.");

        JusticeWalDiagnostics diagnostics = wal.GetDiagnostics();
        Assert.AreEqual(
            expectedDurableLength,
            diagnostics.DurableLength,
            "Le rejet externe ne doit pas réacquérir la longueur modifiée.");
        Assert.AreEqual(
            JusticeWalRecoveryStatus.Corrupt,
            diagnostics.RecoveryStatus);
        StringAssert.Contains(
            diagnostics.LastError.ToLowerInvariant(),
            "préfixe durable");
    }
}
