using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeGuardPenaltyPersistenceTests
{
    private const BindingFlags PrivateStatic =
        BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type ScriptType = typeof(DonJEnemySpawner);

    [TestMethod]
    public void CasePenalty_IsDetachedSerializedAndClearedWithTheActiveCase()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            SentenceSeconds = JusticePolicy.MaxActiveSentenceSeconds,
            CustodyGuardPenaltySeconds = 120L
        };

        JusticeCasePersistenceDto snapshot =
            DonJEnemySpawner.CaptureJusticeCasePersistenceDto(state);
        state.CustodyGuardPenaltySeconds = 180L;

        Assert.AreEqual(120L, snapshot.CustodyGuardPenaltySeconds);
        XElement serialized = SerializeCase(snapshot);
        Assert.AreEqual(
            "120",
            (string)serialized.Attribute("custodyGuardPenaltySeconds"));
        Assert.AreEqual(
            JusticePolicy.MaxActiveSentenceSeconds.ToString(
                CultureInfo.InvariantCulture),
            (string)serialized.Attribute("sentenceSeconds"));

        state.ClearActiveCase(false);
        Assert.AreEqual(0L, state.CustodyGuardPenaltySeconds);
    }

    [TestMethod]
    public void CasePenaltyReader_AcceptsTheOptionalV20AttributeAndDefaultsLegacyToZero()
    {
        JusticeCaseState source = new JusticeCaseState
        {
            Enabled = true,
            ActiveScore = 42,
            FineDue = 2400L,
            SentenceSeconds = 180,
            CustodyGuardPenaltySeconds = 120L,
            WantedEpisodeId = "episode:guard-penalty-reader",
            LastCrimeKind = JusticeCrimeKind.SimpleAssault,
            LastCrimeLabel = "Agression test"
        };
        source.ProcessedIncidentIds.Add("incident:guard-penalty-reader");
        source.Charges.Add(new JusticeCharge
        {
            ChargeId = "charge:guard-penalty-reader",
            IncidentId = "incident:guard-penalty-reader",
            EpisodeId = "episode:guard-penalty-reader",
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Agression test",
            Points = 42,
            Fine = 2400L,
            SentenceSeconds = 180
        });
        XElement current = SerializeCase(
            DonJEnemySpawner.CaptureJusticeCasePersistenceDto(source));

        JusticeCaseState loaded = ReadCase(current);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(120L, loaded.CustodyGuardPenaltySeconds);

        current.Attribute("custodyGuardPenaltySeconds").Remove();
        JusticeCaseState legacy = ReadCase(current);
        Assert.IsNotNull(legacy);
        Assert.AreEqual(0L, legacy.CustodyGuardPenaltySeconds);

        current.SetAttributeValue("custodyGuardPenaltySeconds", "-1");
        Assert.IsNull(ReadCase(current));
    }

    [TestMethod]
    public void TypedCustody_RoundTripContractPersistsRetaliationAndDeathCloneClearsIt()
    {
        JusticeCustodyPersistenceSnapshot active = CreateCustodySnapshot(true, true);
        string xml = DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(active);
        XElement custody = XElement.Parse(xml);

        Assert.IsTrue(active.GuardRetaliationActive);
        Assert.AreEqual("true", (string)custody.Attribute("guardRetaliationActive"));

        JusticeCustodyPersistenceSnapshot rebound =
            (JusticeCustodyPersistenceSnapshot)InvokePrivateStatic(
                "CloneJusticeCustodyPersistenceSnapshotForDeathRebind",
                2,
                active,
                456);
        Assert.IsFalse(rebound.GuardRetaliationActive);
        Assert.IsTrue(rebound.WaitingForRespawn);
        Assert.IsTrue(rebound.DeathRebindPending);

        JusticeCustodyPersistenceSnapshot inactive = CreateCustodySnapshot(false, true);
        Assert.IsFalse(
            inactive.GuardRetaliationActive,
            "Un snapshot hors détention ne doit jamais conserver la riposte.");
    }

    [TestMethod]
    public void TypedCustody_NormalizesLegacyStoredInvincibilityBeforeEveryWrite()
    {
        JusticeCustodyPersistenceSnapshot legacyTrue = CreateCustodySnapshot(
            true,
            false,
            true,
            true);
        XElement custody = XElement.Parse(
            DonJEnemySpawner.SerializeJusticeCustodyPersistenceSnapshot(legacyTrue));

        Assert.IsTrue(legacyTrue.PlayerStateStored);
        Assert.IsFalse(
            legacyTrue.StoredInvincible,
            "Je normalise l'ancienne valeur true dès la construction du DTO.");
        Assert.AreEqual(
            "false",
            (string)custody.Attribute("storedInvincible"),
            "Toute nouvelle sauvegarde doit sérialiser un joueur mortel.");
    }

    [TestMethod]
    public void DeathFrontValidator_AcceptsLegacyAndCurrentContractsButRejectsForgedPenalty()
    {
        JusticeWalRecord current = CreateDeathFrontRecord(
            "CustodyRebind",
            300L,
            360L,
            false);
        JusticeWalRecord nonGuard = CreateDeathFrontRecord(
            "CustodyRebind",
            300L,
            300L,
            false);
        JusticeWalRecord legacy = CreateDeathFrontRecord(
            "CustodyRebind",
            0L,
            0L,
            true);
        JusticeWalRecord forgedCustody = CreateDeathFrontRecord(
            "CustodyRebind",
            300L,
            361L,
            false);
        JusticeWalRecord forgedPolice = CreateDeathFrontRecord(
            "PoliceCapture",
            300L,
            360L,
            false);
        JusticeWalRecord validPolice = CreateDeathFrontRecord(
            "PoliceCapture",
            300L,
            300L,
            false);

        Assert.IsTrue(IsExactDeathFront(current));
        Assert.IsTrue(IsExactDeathFront(nonGuard));
        Assert.IsTrue(IsExactDeathFront(legacy));
        Assert.IsTrue(IsExactDeathFront(validPolice));
        Assert.IsFalse(IsExactDeathFront(forgedCustody));
        Assert.IsFalse(IsExactDeathFront(forgedPolice));
        Assert.AreEqual(13, current.Fields.Count);
        Assert.AreEqual(11, legacy.Fields.Count);
    }

    [TestMethod]
    public void DeathFrontReplay_AppliesTheFrozenAbsolutePenaltyExactlyOnce()
    {
        JusticeWalRecord record = CreateDeathFrontRecord(
            "CustodyRebind",
            600L,
            660L,
            false);
        JusticeCaseState state = new JusticeCaseState
        {
            CustodyGuardPenaltySeconds = 600L
        };

        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            record);
        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            record);

        Assert.AreEqual(660L, state.CustodyGuardPenaltySeconds);

        JusticeWalRecord secondDeath = CreateDeathFrontRecord(
            "CustodyRebind",
            660L,
            720L,
            false);
        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            secondDeath);
        Assert.AreEqual(720L, state.CustodyGuardPenaltySeconds);

        JusticeCaseState incompatible = new JusticeCaseState
        {
            CustodyGuardPenaltySeconds = 601L
        };
        TargetInvocationException failure = Assert.ThrowsException<TargetInvocationException>(
            () => InvokePrivateStatic(
                "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
                2,
                incompatible,
                record));
        Assert.IsInstanceOfType(failure.InnerException, typeof(InvalidDataException));
    }

    [TestMethod]
    public void DeathFrontReplay_InvalidCustodyContextNeverMutatesThePenalty()
    {
        JusticeWalRecord record = CreateDeathFrontRecord(
            "CustodyRebind",
            600L,
            660L,
            false);
        JusticePlayerProfileState owner = new JusticePlayerProfileState(0);
        owner.LastCanonicalPlayerModel = 123;
        owner.CaseState.CustodyEpisodeId = "custody:episode";
        owner.CaseState.CustodyGuardPenaltySeconds = 600L;
        owner.CustodySnapshot = CreateCustodySnapshot(false, false);

        object script = FormatterServices.GetUninitializedObject(ScriptType);
        SetPrivateInstanceField(
            script,
            "_justicePlayerProfiles",
            new[]
            {
                owner,
                new JusticePlayerProfileState(1),
                new JusticePlayerProfileState(2)
            });
        SetPrivateInstanceField(
            script,
            "_justiceProfilePersistenceGenerations",
            new[] { 7L, 0L, 0L });
        SetPrivateInstanceField(script, "_justiceActivePlayerProfileSlot", -1);

        MethodInfo apply = ScriptType.GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == "ApplyJusticeDeathFrontToRuntime" &&
                candidate.GetParameters().Length == 2);
        TargetInvocationException failure =
            Assert.ThrowsException<TargetInvocationException>(
                () => apply.Invoke(script, new object[] { record, false }));

        Assert.IsInstanceOfType(failure.InnerException, typeof(InvalidDataException));
        Assert.AreEqual(
            600L,
            owner.CaseState.CustodyGuardPenaltySeconds,
            "Un front périmé ne doit produire aucune mutation partielle.");
    }

    [TestMethod]
    public void SuccessiveGuardDeathFronts_AddExactlySixtyEachAndRemainIdempotent()
    {
        JusticeWalRecord first = CreateDeathFrontRecord(
            "CustodyRebind",
            0L,
            60L,
            false);
        JusticeWalRecord second = CreateDeathFrontRecord(
            "CustodyRebind",
            60L,
            120L,
            false);
        JusticeCaseState state = new JusticeCaseState();

        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            first);
        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            first);
        Assert.AreEqual(60L, state.CustodyGuardPenaltySeconds);

        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            second);
        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            second);
        Assert.AreEqual(120L, state.CustodyGuardPenaltySeconds);
    }

    [TestMethod]
    public void LegacyDeathFrontReplay_PreservesAnExistingPenaltyAndAdditionSaturates()
    {
        JusticeWalRecord legacy = CreateDeathFrontRecord(
            "CustodyRebind",
            0L,
            0L,
            true);
        JusticeCaseState state = new JusticeCaseState
        {
            CustodyGuardPenaltySeconds = 75L
        };

        InvokePrivateStatic(
            "ApplyJusticeCustodyGuardPenaltyFromDeathFront",
            2,
            state,
            legacy);

        Assert.AreEqual(75L, state.CustodyGuardPenaltySeconds);
        Assert.AreEqual(
            long.MaxValue,
            (long)InvokePrivateStatic(
                "CalculateJusticeCustodyGuardPenaltyAfterDeath",
                1,
                long.MaxValue - 10L));
    }

    private static XElement SerializeCase(JusticeCasePersistenceDto state)
    {
        StringBuilder buffer = new StringBuilder();
        using (XmlWriter writer = XmlWriter.Create(
            buffer,
            new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment
            }))
        {
            DonJEnemySpawner.WriteJusticeCaseXml(writer, state);
        }
        return XElement.Parse(buffer.ToString());
    }

    private static JusticeCaseState ReadCase(XElement element)
    {
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(element.ToString(SaveOptions.DisableFormatting));
        return (JusticeCaseState)InvokePrivateStatic(
            "ReadJusticeCaseXml",
            1,
            document.DocumentElement);
    }

    private static JusticeCustodyPersistenceSnapshot CreateCustodySnapshot(
        bool active,
        bool guardRetaliationActive,
        bool playerStateStored = false,
        bool storedInvincible = false)
    {
        return new JusticeCustodyPersistenceSnapshot(
            active,
            active ? 1 : 0,
            false,
            false,
            active ? 300 : 0,
            0,
            false,
            false,
            0,
            0,
            0,
            false,
            false,
            false,
            playerStateStored,
            storedInvincible,
            false,
            true,
            active ? 123 : 0,
            active ? 0 : -1,
            0,
            false,
            false,
            null,
            null,
            null,
            null,
            false,
            new JusticeActivityCooldownPersistenceSnapshot[0],
            guardRetaliationActive);
    }

    private static JusticeWalRecord CreateDeathFrontRecord(
        string mode,
        long penaltyBefore,
        long penaltyAfter,
        bool legacy)
    {
        string episode = mode == "PoliceCapture" ? string.Empty : "custody:episode";
        IEnumerable<JusticePersistenceField> fields =
            (IEnumerable<JusticePersistenceField>)InvokePrivateStatic(
                "CreateJusticeDeathFrontWalFieldsWithCustodyPenalty",
                12,
                mode,
                5L,
                7L,
                "slot:0:model:123",
                episode,
                mode == "CustodyRebind" ? 1 : 0,
                0,
                123,
                0,
                123,
                penaltyBefore,
                penaltyAfter);
        if (legacy)
        {
            fields = fields.Where(field =>
                field.Path != "custodyGuardPenaltyBefore" &&
                field.Path != "custodyGuardPenaltyAfter");
        }

        return new JusticeWalRecord(
            "death-front:test:" + mode + ":" + penaltyAfter.ToString(
                CultureInfo.InvariantCulture),
            "DeathFront",
            0,
            JusticeWalState.Prepared,
            5L,
            DateTime.UtcNow.Ticks,
            fields);
    }

    private static bool IsExactDeathFront(JusticeWalRecord record)
    {
        return (bool)InvokePrivateStatic(
            "IsJusticeDeathFrontWalRecordExact",
            1,
            record);
    }

    private static object InvokePrivateStatic(
        string name,
        int parameterCount,
        params object[] arguments)
    {
        MethodInfo method = ScriptType.GetMethods(PrivateStatic).Single(candidate =>
            candidate.Name == name &&
            candidate.GetParameters().Length == parameterCount);
        return method.Invoke(null, arguments);
    }

    private static void SetPrivateInstanceField(
        object target,
        string name,
        object value)
    {
        FieldInfo field = ScriptType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, name);
        field.SetValue(target, value);
    }
}
