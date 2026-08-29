using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeTypedPersistenceDtoTests
{
    [TestMethod]
    public void TypedSnapshot_MatchesLegacyXmlAndIgnoresEveryLaterRuntimeMutation()
    {
        JusticeCaseState sourceCase = CreateRichCase();
        JusticeRecordState sourceRecord = CreateRichRecord();

        JusticeCaseRecordPersistenceDto snapshot =
            DonJEnemySpawner.CaptureJusticeCaseRecordPersistenceDto(sourceCase, sourceRecord);
        string capturedCaseXml = WriteCase(snapshot.Case);
        string capturedRecordXml = WriteRecord(snapshot.Record);

        Assert.AreEqual(WriteLegacyCase(sourceCase), capturedCaseXml);
        Assert.AreEqual(WriteLegacyRecord(sourceRecord), capturedRecordXml);

        // Je modifie ensuite chaque niveau du graphe runtime pour prouver que le
        // worker ne conserve aucun alias vers le dossier, les charges ou le casier.
        sourceCase.Enabled = false;
        sourceCase.ActiveScore = 999;
        sourceCase.WantedEpisodeId = "wanted:mutated";
        sourceCase.CompletedOperationIds.Clear();
        sourceCase.ProcessedIncidentIds.Add("incident:mutated");
        sourceCase.FleeingChargedEpisodeIds.Clear();
        sourceCase.EscapeChargedEpisodeIds.Add("escape:mutated");
        sourceCase.Charges[0].DisplayName = "Mutation tardive";
        sourceCase.Charges[0].AlliedContributorHandles.Add(999);
        sourceCase.Charges[0].AlliedContributors.Clear();
        sourceCase.Charges.Clear();

        sourceRecord.RecidivismIndex = 99;
        sourceRecord.PinnedConvictionId = "conviction:mutated";
        sourceRecord.AppliedConvictionIds.Clear();
        sourceRecord.Convictions[0].Charges[0].DisplayName = "Résumé muté";
        sourceRecord.Convictions[0].Charges.Clear();
        sourceRecord.Convictions.Clear();

        Assert.AreEqual(capturedCaseXml, WriteCase(snapshot.Case));
        Assert.AreEqual(capturedRecordXml, WriteRecord(snapshot.Record));
        StringAssert.Contains(capturedCaseXml, "handle=\"82\" generation=\"0\"");
        StringAssert.Contains(capturedRecordXml, "label=\"Résumé historique\"");
    }

    [TestMethod]
    public void TypedSnapshot_ExposesOnlyReadonlyCollectionsAndNoGtaTypes()
    {
        Type[] dtoTypes =
        {
            typeof(JusticeAlliedContributorPersistenceDto),
            typeof(JusticeChargePersistenceDto),
            typeof(JusticeCasePersistenceDto),
            typeof(JusticeConvictionChargeSummaryPersistenceDto),
            typeof(JusticeConvictionPersistenceDto),
            typeof(JusticeRecordPersistenceDto),
            typeof(JusticeCaseRecordPersistenceDto)
        };

        foreach (Type dtoType in dtoTypes)
        {
            foreach (PropertyInfo property in dtoType.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(
                    property.CanWrite,
                    dtoType.Name + "." + property.Name + " ne doit pas être modifiable.");
                AssertHasNoGtaType(property.PropertyType, dtoType.Name + "." + property.Name);

                if (property.PropertyType != typeof(string) &&
                    typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    Assert.IsTrue(
                        property.PropertyType.IsGenericType &&
                        property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>),
                        dtoType.Name + "." + property.Name +
                        " doit exposer seulement IReadOnlyList<T>.");
                }
            }

            foreach (FieldInfo field in dtoType.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsTrue(
                    field.IsInitOnly,
                    dtoType.Name + "." + field.Name + " doit être readonly.");
                Assert.IsFalse(field.FieldType.IsArray, dtoType.Name + "." + field.Name);
                AssertHasNoGtaType(field.FieldType, dtoType.Name + "." + field.Name);
            }
        }

        JusticeCaseRecordPersistenceDto snapshot =
            DonJEnemySpawner.CaptureJusticeCaseRecordPersistenceDto(
                CreateRichCase(),
                CreateRichRecord());
        IList<JusticeChargePersistenceDto> charges =
            (IList<JusticeChargePersistenceDto>)snapshot.Case.Charges;
        IList<string> operationIds = (IList<string>)snapshot.Case.CompletedOperationIds;
        IList<JusticeConvictionPersistenceDto> convictions =
            (IList<JusticeConvictionPersistenceDto>)snapshot.Record.Convictions;

        Assert.ThrowsException<NotSupportedException>(() => charges.Clear());
        Assert.ThrowsException<NotSupportedException>(() => operationIds.Add("operation:mutation"));
        Assert.ThrowsException<NotSupportedException>(() => convictions.Clear());
    }

    [TestMethod]
    public void TypedXml_RoundTripsThroughExistingStrictReaders()
    {
        JusticeRecordState record = new JusticeRecordState();
        JusticeCaseState caseState = new JusticeCaseState { Enabled = true };
        JusticeIncident incident = new JusticeIncident
        {
            IncidentId = "incident:typed-roundtrip",
            EpisodeId = "wanted:typed-roundtrip",
            DetectionBatchId = "batch:typed-roundtrip",
            CausalEventId = "causal:typed-roundtrip",
            Kind = JusticeCrimeKind.SimpleAssault,
            VictimHandle = 41,
            VictimGeneration = 3,
            AllyHandle = 42,
            AllyGeneration = 4,
            CreatedAtMs = 1234L,
            Evidence = new JusticeEvidence
            {
                Kind = JusticeEvidenceKind.DirectGameReport,
                HasPlausibleObserver = true,
                ReportCompleted = true
            },
            Circumstances = JusticeCircumstances.Armed,
            IsAlliedAction = true,
            IsConfirmed = true
        };
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(caseState, incident, record));

        JusticeConvictionChargeSummary summary = new JusticeConvictionChargeSummary
        {
            Kind = JusticeCrimeKind.SimpleAssault,
            DisplayName = "Violences simples",
            Points = 18,
            Fine = 1000L,
            SentenceSeconds = 90,
            Circumstances = JusticeCircumstances.None,
            CircumstancesWerePersisted = true
        };
        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = "conviction:custody:typed-roundtrip",
            JudgedAtUtc = new DateTime(2026, 8, 29, 9, 10, 11, DateTimeKind.Utc),
            Severity = JusticePolicy.GetSeverity(summary.Points),
            Score = summary.Points,
            Fine = summary.Fine,
            SentenceSeconds = summary.SentenceSeconds
        };
        conviction.Charges.Add(summary);
        record.Convictions.Add(conviction);
        record.AppliedConvictionIds.Add(conviction.ConvictionId);
        record.RecidivismIndex = JusticePolicy.GetConvictionRecidivismIncrease(conviction.Severity);

        JusticeCaseRecordPersistenceDto snapshot =
            DonJEnemySpawner.CaptureJusticeCaseRecordPersistenceDto(caseState, record);
        JusticeCaseState loadedCase = ReadLegacyFragment<JusticeCaseState>(
            "ReadJusticeCaseXml",
            WriteCase(snapshot.Case));
        JusticeRecordState loadedRecord = ReadLegacyFragment<JusticeRecordState>(
            "ReadJusticeRecordXml",
            WriteRecord(snapshot.Record));

        Assert.IsNotNull(loadedCase);
        Assert.AreEqual(caseState.ActiveScore, loadedCase.ActiveScore);
        Assert.AreEqual(caseState.Charges.Count, loadedCase.Charges.Count);
        Assert.AreEqual(42, loadedCase.Charges[0].AlliedContributors[0].Handle);
        Assert.AreEqual("causal:typed-roundtrip", loadedCase.Charges[0].CausalEventId);

        Assert.IsNotNull(loadedRecord);
        Assert.AreEqual(record.RecidivismIndex, loadedRecord.RecidivismIndex);
        Assert.AreEqual(1, loadedRecord.Convictions.Count);
        Assert.AreEqual(conviction.ConvictionId, loadedRecord.Convictions[0].ConvictionId);
        Assert.AreEqual("Violences simples", loadedRecord.Convictions[0].Charges[0].DisplayName);
    }

    private static JusticeCaseState CreateRichCase()
    {
        JusticeCharge charge = new JusticeCharge
        {
            ChargeId = "charge:incident:typed-dto",
            IncidentId = "incident:typed-dto",
            EpisodeId = "wanted:typed-dto",
            DetectionBatchId = "batch:typed-dto",
            CausalEventId = "causal:typed-dto",
            Kind = JusticeCrimeKind.AggravatedAssault,
            DisplayName = "Agression aggravée",
            VictimHandle = 71,
            VictimGeneration = 5,
            Points = 34,
            Fine = 3000L,
            SentenceSeconds = 240,
            ConfirmedAtMs = -4L,
            IsAlliedAction = true,
            Circumstances = JusticeCircumstances.Armed | JusticeCircumstances.GroupCrime,
            AdditionalVictimCount = -2,
            IsAggregate = false,
            AggregatedChargeCount = 7,
            IsAdjudicated = false
        };
        charge.AddAlliedContributor(81, 6);
        charge.AlliedContributorHandles.Add(81);
        charge.AlliedContributorHandles.Add(82);

        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = true,
            ActiveScore = 34,
            FineDue = 2800L,
            VoluntaryFinePaid = 150L,
            FineInDispute = 50L,
            SentenceSeconds = 240,
            HasWarrant = true,
            EscapeWantedMinimumPending = false,
            EscapeWantedMinimumAttempted = false,
            Phase = JusticePhase.Wanted,
            WantedEpisodeId = "wanted:typed-dto",
            CustodyEpisodeId = string.Empty,
            LastCrimeKind = JusticeCrimeKind.AggravatedAssault,
            LastCrimeLabel = "Agression aggravée"
        };
        state.Charges.Add(charge);
        state.FleeingChargedEpisodeIds.Add("  wanted:fleeing  ");
        state.EscapeChargedEpisodeIds.Add("wanted:escape");
        state.ProcessedIncidentIds.Add(" incident:typed-dto ");
        state.ProcessedIncidentIds.Add(null);
        state.CompletedOperationIds.Add("ApplyWantedFloor:wanted:typed-dto");
        state.CompletedOperationIds.Add("   ");
        return state;
    }

    private static JusticeRecordState CreateRichRecord()
    {
        JusticeConvictionChargeSummary summary = new JusticeConvictionChargeSummary
        {
            Kind = JusticeCrimeKind.VehicleDamage,
            DisplayName = "Résumé historique",
            Points = -2,
            Fine = -3L,
            SentenceSeconds = -4,
            Circumstances = JusticeCircumstances.MultipleVictims,
            CircumstancesWerePersisted = false,
            IsAggregate = true,
            AggregatedChargeCount = 0
        };
        JusticeConviction conviction = new JusticeConviction
        {
            ConvictionId = "conviction:custody:typed-dto",
            JudgedAtUtc = new DateTime(2026, 8, 29, 10, 11, 12, DateTimeKind.Local),
            Severity = JusticeSeverity.Serious,
            Score = 42,
            Fine = 4400L,
            SentenceSeconds = 360
        };
        conviction.Charges.Add(summary);

        JusticeRecordState state = new JusticeRecordState
        {
            RecidivismIndex = 9,
            CleanGameplaySeconds = 1200,
            AppliedCleanDecay = 1,
            PinnedConvictionId = conviction.ConvictionId
        };
        state.Convictions.Add(conviction);
        state.AppliedConvictionIds.Add("  " + conviction.ConvictionId + "  ");
        state.MarkLedgerChanged();
        return state;
    }

    private static string WriteCase(JusticeCasePersistenceDto state)
    {
        return WriteFragment(writer => DonJEnemySpawner.WriteJusticeCaseXml(writer, state));
    }

    private static string WriteRecord(JusticeRecordPersistenceDto state)
    {
        return WriteFragment(writer => DonJEnemySpawner.WriteJusticeRecordXml(writer, state));
    }

    private static string WriteLegacyCase(JusticeCaseState state)
    {
        return WriteLegacyFragment("WriteJusticeCaseXml", typeof(JusticeCaseState), state);
    }

    private static string WriteLegacyRecord(JusticeRecordState state)
    {
        return WriteLegacyFragment("WriteJusticeRecordXml", typeof(JusticeRecordState), state);
    }

    private static string WriteLegacyFragment(string methodName, Type stateType, object state)
    {
        MethodInfo writerMethod = typeof(DonJEnemySpawner).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(XmlWriter), stateType },
            null);
        Assert.IsNotNull(writerMethod, methodName);
        return WriteFragment(writer => writerMethod.Invoke(null, new[] { writer, state }));
    }

    private static T ReadLegacyFragment<T>(string methodName, string xml) where T : class
    {
        MethodInfo readerMethod = typeof(DonJEnemySpawner).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(XmlElement) },
            null);
        Assert.IsNotNull(readerMethod, methodName);
        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        return readerMethod.Invoke(null, new object[] { document.DocumentElement }) as T;
    }

    private static string WriteFragment(Action<XmlWriter> write)
    {
        StringBuilder builder = new StringBuilder(4096);
        XmlWriterSettings settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        };
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
        {
            write(writer);
        }
        return builder.ToString();
    }

    private static void AssertHasNoGtaType(Type type, string path)
    {
        Type inspected = type;
        if (inspected.IsGenericType)
        {
            foreach (Type argument in inspected.GetGenericArguments())
            {
                AssertHasNoGtaType(argument, path);
            }
            inspected = inspected.GetGenericTypeDefinition();
        }

        string fullName = inspected.FullName ?? inspected.Name;
        Assert.IsFalse(
            fullName.Equals("GTA", StringComparison.Ordinal) ||
            fullName.StartsWith("GTA.", StringComparison.Ordinal),
            path + " référence un type GTA : " + fullName);
    }
}
