using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeDomainTests
{
    [TestMethod]
    public void CrimeCatalog_ContainsTheExactBalancedGrid()
    {
        var expected = new Dictionary<JusticeCrimeKind, Tuple<int, long, int>>
        {
            { JusticeCrimeKind.ReportedViolentAct, Tuple.Create(5, 250L, 0) },
            { JusticeCrimeKind.RecklessDischarge, Tuple.Create(6, 300L, 0) },
            { JusticeCrimeKind.VehicleDamage, Tuple.Create(8, 500L, 0) },
            { JusticeCrimeKind.ArmedThreat, Tuple.Create(10, 600L, 0) },
            { JusticeCrimeKind.VehicleTheft, Tuple.Create(12, 750L, 0) },
            { JusticeCrimeKind.VehicleDestruction, Tuple.Create(18, 1250L, 60) },
            { JusticeCrimeKind.SimpleAssault, Tuple.Create(18, 1000L, 90) },
            { JusticeCrimeKind.HitAndRun, Tuple.Create(18, 1200L, 90) },
            { JusticeCrimeKind.EvadingPolice, Tuple.Create(20, 1500L, 120) },
            { JusticeCrimeKind.AccessoryAssaultOfficer, Tuple.Create(22, 2000L, 120) },
            { JusticeCrimeKind.Carjacking, Tuple.Create(24, 1750L, 120) },
            { JusticeCrimeKind.ResistingArrest, Tuple.Create(30, 2500L, 180) },
            { JusticeCrimeKind.AggravatedAssault, Tuple.Create(34, 3000L, 240) },
            { JusticeCrimeKind.AssaultOfficer, Tuple.Create(48, 5000L, 360) },
            { JusticeCrimeKind.AccessoryMurderOfficer, Tuple.Create(52, 7500L, 420) },
            { JusticeCrimeKind.Manslaughter, Tuple.Create(55, 6000L, 480) },
            { JusticeCrimeKind.MurderCivilian, Tuple.Create(75, 10000L, 720) },
            { JusticeCrimeKind.Escape, Tuple.Create(90, 10000L, 900) },
            { JusticeCrimeKind.MurderOfficer, Tuple.Create(100, 15000L, 1080) }
        };

        Assert.AreEqual(expected.Count, JusticePolicy.Catalog.Count);
        foreach (KeyValuePair<JusticeCrimeKind, Tuple<int, long, int>> pair in expected)
        {
            JusticeCrimeDefinition definition = JusticePolicy.GetDefinition(pair.Key);
            Assert.AreEqual(pair.Key, definition.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.AreEqual(pair.Value.Item1, definition.BasePoints, pair.Key.ToString());
            Assert.AreEqual(pair.Value.Item2, definition.BaseFine, pair.Key.ToString());
            Assert.AreEqual(pair.Value.Item3, definition.BaseSentenceSeconds, pair.Key.ToString());
        }
    }

    [TestMethod]
    public void Incident_WithNoCredibleWitness_ExpiresWithoutCharge()
    {
        JusticeIncident incident = CreateIncident(JusticeCrimeKind.VehicleTheft, "secret", 1000L, 22, 1);
        incident.Evidence = new JusticeEvidence();

        Assert.IsFalse(incident.TryConfirm(6999L, true));
        Assert.IsTrue(incident.IsExpired(7001L));

        JusticeCaseState state = NewEnabledCase();
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, incident, new JusticeRecordState()));
        Assert.AreEqual(0, state.Charges.Count);
        Assert.AreEqual(0, state.ActiveScore);
    }

    [TestMethod]
    public void Evidence_PoliceIsImmediate_CivilianWaitsAndMustSurvive()
    {
        JusticeIncident police = CreateIncident(JusticeCrimeKind.SimpleAssault, "police", 1000L, 20, 1);
        police.Evidence = Evidence(JusticeEvidenceKind.PoliceWitness, 1000L, 0L);
        Assert.IsTrue(police.TryConfirm(1000L, true));

        JusticeIncident civilian = CreateIncident(JusticeCrimeKind.SimpleAssault, "civilian", 1000L, 21, 1);
        civilian.Evidence = Evidence(JusticeEvidenceKind.CivilianWitness, 1000L, 4000L);
        Assert.IsFalse(civilian.TryConfirm(3999L, true));
        Assert.IsFalse(civilian.TryConfirm(4000L, false));
        Assert.IsTrue(civilian.TryConfirm(4000L, true));
    }

    [TestMethod]
    public void Evidence_WantedRiseNeedsAPlausibleObserver()
    {
        JusticeEvidence evidence = Evidence(JusticeEvidenceKind.CorrelatedWantedRise, 100L, 100L);
        evidence.HasPlausibleObserver = false;
        Assert.IsFalse(evidence.HasCredibleSource);
        Assert.IsFalse(evidence.IsConfirmed(100L, true));

        evidence.HasPlausibleObserver = true;
        Assert.IsTrue(evidence.HasCredibleSource);
        Assert.IsTrue(evidence.IsConfirmed(100L, true));
    }

    [TestMethod]
    public void ProportionalSelfDefense_IsNeverCharged_AndExcessiveDefenseIsReduced()
    {
        JusticeIncident lawful = ConfirmedIncident(
            JusticeCrimeKind.AggravatedAssault,
            "lawful",
            31,
            1,
            JusticeCircumstances.Armed | JusticeCircumstances.ProportionalSelfDefense);

        JusticeSanction lawfulSanction = JusticePolicy.Evaluate(lawful, new JusticeRecordState());
        Assert.IsFalse(lawfulSanction.IsChargeable);
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(NewEnabledCase(), lawful, new JusticeRecordState()));

        JusticeIncident excessive = ConfirmedIncident(
            JusticeCrimeKind.SimpleAssault,
            "excessive",
            32,
            1,
            JusticeCircumstances.ExcessiveSelfDefense);
        JusticeSanction excessiveSanction = JusticePolicy.Evaluate(excessive, new JusticeRecordState());
        Assert.AreEqual(6000, excessiveSanction.CircumstanceBasisPoints);
        Assert.AreEqual(11, excessiveSanction.Points);
        Assert.AreEqual(600L, excessiveSanction.Fine);
        Assert.AreEqual(60, excessiveSanction.SentenceSeconds);
    }

    [TestMethod]
    public void SelfDefense_NeverCancelsOfficerCrimeOrResistanceToAnActiveWarrant()
    {
        JusticeIncident officer = ConfirmedIncident(
            JusticeCrimeKind.MurderOfficer,
            "officer-defense",
            33,
            1,
            JusticeCircumstances.ProportionalSelfDefense);
        JusticeSanction officerSanction = JusticePolicy.Evaluate(officer, new JusticeRecordState());
        Assert.IsTrue(officerSanction.IsChargeable);
        Assert.AreEqual(100, officerSanction.Points);

        JusticeIncident resisting = ConfirmedIncident(
            JusticeCrimeKind.ResistingArrest,
            "warrant-defense",
            34,
            1,
            JusticeCircumstances.ActiveWarrant | JusticeCircumstances.ProportionalSelfDefense);
        JusticeSanction resistingSanction = JusticePolicy.Evaluate(resisting, new JusticeRecordState());
        Assert.IsTrue(resistingSanction.IsChargeable);
        Assert.AreEqual(35, resistingSanction.Points);
        Assert.AreEqual(2900L, resistingSanction.Fine);
        Assert.AreEqual(210, resistingSanction.SentenceSeconds);
    }

    [TestMethod]
    public void Circumstances_AreCombinedAndClampedWithoutDoubleCountingTheGroup()
    {
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.MurderOfficer,
            "maximum",
            40,
            2,
            JusticeCircumstances.Armed |
            JusticeCircumstances.ExplosiveOrIncendiary |
            JusticeCircumstances.ActiveWarrant |
            JusticeCircumstances.InCustody |
            JusticeCircumstances.MultipleVictims |
            JusticeCircumstances.GroupCrime |
            JusticeCircumstances.OrganizedBand);
        incident.AdditionalVictimCount = 3;

        JusticeSanction sanction = JusticePolicy.Evaluate(incident, new JusticeRecordState());
        Assert.AreEqual(23000, sanction.CircumstanceBasisPoints);
        Assert.AreEqual(230, sanction.Points);
        Assert.AreEqual(34500L, sanction.Fine);
        Assert.AreEqual(1800, sanction.SentenceSeconds);

        JusticeIncident collective = ConfirmedIncident(
            JusticeCrimeKind.SimpleAssault,
            "collective",
            41,
            1,
            JusticeCircumstances.GroupCrime | JusticeCircumstances.OrganizedBand);
        Assert.AreEqual(12500, JusticePolicy.Evaluate(collective, new JusticeRecordState()).CircumstanceBasisPoints);
    }

    [TestMethod]
    public void Recidivism_UsesTheThreeIndependentCappedMultipliers()
    {
        JusticeRecordState record = new JusticeRecordState { RecidivismIndex = 100 };
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.SimpleAssault,
            "repeat",
            50,
            1,
            JusticeCircumstances.None);

        JusticeSanction sanction = JusticePolicy.Evaluate(incident, record);
        Assert.AreEqual(24, sanction.Points);
        Assert.AreEqual(1500L, sanction.Fine);
        Assert.AreEqual(165, sanction.SentenceSeconds);
    }

    [TestMethod]
    public void Sanction_RoundsPointsOnlyOnceAfterCircumstancesAndRecidivism()
    {
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.ReportedViolentAct,
            "single-rounding",
            51,
            1,
            JusticeCircumstances.Armed);

        JusticeSanction sanction = JusticePolicy.Evaluate(
            incident,
            new JusticeRecordState { RecidivismIndex = 1 });

        Assert.AreEqual(6, sanction.Points);
        Assert.AreEqual(300L, sanction.Fine);
        Assert.AreEqual(0, sanction.SentenceSeconds);
    }

    [TestMethod]
    public void Severity_FollowsEveryBoundaryWithoutOwningGtaWanted()
    {
        AssertSeverity(0, JusticeSeverity.None);
        AssertSeverity(1, JusticeSeverity.Minor);
        AssertSeverity(9, JusticeSeverity.Minor);
        AssertSeverity(10, JusticeSeverity.Misdemeanor);
        AssertSeverity(24, JusticeSeverity.Misdemeanor);
        AssertSeverity(25, JusticeSeverity.Serious);
        AssertSeverity(49, JusticeSeverity.Serious);
        AssertSeverity(50, JusticeSeverity.Crime);
        AssertSeverity(79, JusticeSeverity.Crime);
        AssertSeverity(80, JusticeSeverity.Major);
        AssertSeverity(119, JusticeSeverity.Major);
        AssertSeverity(120, JusticeSeverity.Critical);
        AssertSeverity(int.MaxValue, JusticeSeverity.Critical);
    }

    [TestMethod]
    public void ConvictionFine_RemovesOnlyProvenVoluntaryPayments()
    {
        Assert.AreEqual(1000L, JusticePolicy.CalculateRemainingConvictionFine(1000L, 0L));
        Assert.AreEqual(200L, JusticePolicy.CalculateRemainingConvictionFine(1000L, 800L));
        Assert.AreEqual(0L, JusticePolicy.CalculateRemainingConvictionFine(1000L, 1000L));
        Assert.AreEqual(0L, JusticePolicy.CalculateRemainingConvictionFine(1000L, 5000L));
        Assert.AreEqual(1000L, JusticePolicy.CalculateRemainingConvictionFine(1000L, -1L));
        Assert.AreEqual(0L, JusticePolicy.CalculateRemainingConvictionFine(-1L, 100L));
    }

    [TestMethod]
    public void FineDebitReconciliation_UsesABoundedPersistentAtMostOnceWindow()
    {
        long attemptedAt = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc).Ticks;
        Assert.IsFalse(JusticePolicy.HasFineDebitAttemptTimedOut(
            attemptedAt,
            attemptedAt + JusticePolicy.FineDebitAmbiguityTimeoutTicks - 1L));
        Assert.IsTrue(JusticePolicy.HasFineDebitAttemptTimedOut(
            attemptedAt,
            attemptedAt + JusticePolicy.FineDebitAmbiguityTimeoutTicks));
        Assert.IsTrue(JusticePolicy.HasFineDebitAttemptTimedOut(0L, attemptedAt),
            "Un intent v1 déjà Attempted ne doit jamais réémettre le débit.");
        Assert.IsTrue(JusticePolicy.HasFineDebitAttemptTimedOut(attemptedAt, attemptedAt - 1L),
            "Un recul d'horloge ne doit pas créer un soft-lock persistant.");

        Assert.IsFalse(JusticePolicy.HasFineDebitPreparationTimedOut(
            attemptedAt,
            attemptedAt + JusticePolicy.FineDebitAmbiguityTimeoutTicks - 1L));
        Assert.IsTrue(JusticePolicy.HasFineDebitPreparationTimedOut(
            attemptedAt,
            attemptedAt + JusticePolicy.FineDebitAmbiguityTimeoutTicks));
        Assert.IsTrue(JusticePolicy.HasFineDebitPreparationTimedOut(0L, attemptedAt));
        Assert.IsTrue(JusticePolicy.HasFineDebitPreparationTimedOut(
            attemptedAt,
            attemptedAt - 1L));
    }

    [TestMethod]
    public void DamageFront_AcceptsOnlyAnExplicitInitialSignalOrANewFalseToTrueEdge()
    {
        Assert.IsFalse(JusticePolicy.ShouldAcceptDamageFront(true, false, true, false),
            "Un flag déjà vrai lors de la création du baseline reste un historique non daté.");
        Assert.IsTrue(JusticePolicy.ShouldAcceptDamageFront(true, false, true, true),
            "Un signal GTA récent explicite peut rattacher le premier flag au nouvel acte.");
        Assert.IsTrue(JusticePolicy.ShouldAcceptDamageFront(false, false, true, false),
            "Un passage observé de faux à vrai constitue un front causal frais.");
        Assert.IsFalse(JusticePolicy.ShouldAcceptDamageFront(false, true, true, true),
            "Un flag resté vrai ne doit jamais être redaté, même avec un signal global récent.");
        Assert.IsFalse(JusticePolicy.ShouldAcceptDamageFront(false, true, false, true));
    }

    [TestMethod]
    public void AttributedDeathFront_AcceptsTheLethalFrameBeforeTheDeathClockAppears()
    {
        Assert.IsTrue(JusticePolicy.ShouldAcceptAttributedDeathFront(true, false, false));
        Assert.IsTrue(JusticePolicy.ShouldAcceptAttributedDeathFront(false, true, false));
        Assert.IsTrue(JusticePolicy.ShouldAcceptAttributedDeathFront(false, false, true));
        Assert.IsFalse(
            JusticePolicy.ShouldAcceptAttributedDeathFront(false, false, false),
            "Sans timer de mort ni front d'attaque explicite, un ancien cadavre doit rester ignoré.");
    }

    [TestMethod]
    public void ApplyingAnIncident_IsExactlyOnceAndStartsAWantedEpisode()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.VehicleTheft,
            "theft-1",
            60,
            3,
            JusticeCircumstances.None);

        JusticeCharge first = JusticePolicy.ApplyConfirmedIncident(state, incident, new JusticeRecordState());
        JusticeCharge duplicate = JusticePolicy.ApplyConfirmedIncident(state, incident, new JusticeRecordState());

        Assert.IsNotNull(first);
        Assert.IsNull(duplicate);
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(12, state.ActiveScore);
        Assert.AreEqual(750L, state.FineDue);
        Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
        Assert.IsFalse(state.HasWarrant);
        Assert.AreEqual("episode", state.WantedEpisodeId);
    }

    [TestMethod]
    public void HomicideSupersedesAssault_AndDestructionSupersedesDamage()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.SimpleAssault, "assault", 70, 4, JusticeCircumstances.None),
            record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.MurderCivilian, "murder", 70, 4, JusticeCircumstances.None),
            record));

        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.MurderCivilian, state.Charges[0].Kind);
        Assert.AreEqual(75, state.ActiveScore);

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.VehicleDamage, "damage", 71, 1, JusticeCircumstances.None),
            record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.VehicleDestruction, "destroy", 71, 1, JusticeCircumstances.None),
            record));

        Assert.AreEqual(2, state.Charges.Count);
        Assert.AreEqual(93, state.ActiveScore);
    }

    [TestMethod]
    public void HitAndRun_RemainsIndependentFromHomicide_InBothApplicationOrders()
    {
        JusticeRecordState record = new JusticeRecordState();
        JusticeCaseState hitAndRunFirst = NewEnabledCase();

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            hitAndRunFirst,
            ConfirmedIncident(JusticeCrimeKind.HitAndRun, "hit-and-run-first", 72, 1, JusticeCircumstances.None),
            record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            hitAndRunFirst,
            ConfirmedIncident(JusticeCrimeKind.MurderCivilian, "murder-second", 72, 1, JusticeCircumstances.None),
            record));

        Assert.AreEqual(2, hitAndRunFirst.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.HitAndRun, hitAndRunFirst.Charges[0].Kind);
        Assert.AreEqual(JusticeCrimeKind.MurderCivilian, hitAndRunFirst.Charges[1].Kind);
        Assert.AreEqual(93, hitAndRunFirst.ActiveScore);

        JusticeCaseState homicideFirst = NewEnabledCase();

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            homicideFirst,
            ConfirmedIncident(JusticeCrimeKind.MurderCivilian, "murder-first", 73, 1, JusticeCircumstances.None),
            record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            homicideFirst,
            ConfirmedIncident(JusticeCrimeKind.HitAndRun, "hit-and-run-second", 73, 1, JusticeCircumstances.None),
            record));

        Assert.AreEqual(2, homicideFirst.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.MurderCivilian, homicideFirst.Charges[0].Kind);
        Assert.AreEqual(JusticeCrimeKind.HitAndRun, homicideFirst.Charges[1].Kind);
        Assert.AreEqual(93, homicideFirst.ActiveScore);
    }

    [TestMethod]
    public void DirectOfficerCrimeReplacesAccessory_AndKeepsCollectiveAggravation()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident accessory = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "accessory",
            80,
            2,
            JusticeCircumstances.None);
        accessory.IsAlliedAction = true;
        accessory.AllyHandle = 501;

        JusticeIncident direct = ConfirmedIncident(
            JusticeCrimeKind.AssaultOfficer,
            "direct",
            80,
            2,
            JusticeCircumstances.None);

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, accessory, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, direct, record));
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.AssaultOfficer, state.Charges[0].Kind);
        Assert.IsFalse(state.Charges[0].IsAlliedAction);
        Assert.AreEqual(
            JusticeCircumstances.GroupCrime,
            state.Charges[0].Circumstances & JusticeCircumstances.GroupCrime);
        Assert.AreEqual(53, state.Charges[0].Points);
        Assert.AreEqual(5500L, state.Charges[0].Fine);
        Assert.AreEqual(405, state.Charges[0].SentenceSeconds);
        CollectionAssert.AreEqual(new[] { 501 }, state.Charges[0].AlliedContributorHandles);
    }

    [TestMethod]
    public void TwoDistinctAllies_UpgradeOneAccessoryChargeToOrganizedBand()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident first = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "ally-one",
            81,
            3,
            JusticeCircumstances.None);
        first.IsAlliedAction = true;
        first.AllyHandle = 601;

        JusticeIncident sameAlly = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "ally-one-repeat",
            81,
            3,
            JusticeCircumstances.None);
        sameAlly.IsAlliedAction = true;
        sameAlly.AllyHandle = 601;

        JusticeIncident second = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "ally-two",
            81,
            3,
            JusticeCircumstances.None);
        second.IsAlliedAction = true;
        second.AllyHandle = 602;

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, first, record));
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, sameAlly, record));
        JusticeCharge upgraded = JusticePolicy.ApplyConfirmedIncident(state, second, record);

        Assert.IsNotNull(upgraded);
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(
            JusticeCircumstances.OrganizedBand,
            upgraded.Circumstances & JusticeCircumstances.OrganizedBand);
        Assert.AreEqual(28, upgraded.Points);
        Assert.AreEqual(2500L, upgraded.Fine);
        Assert.AreEqual(150, upgraded.SentenceSeconds);
        CollectionAssert.AreEquivalent(new[] { 601, 602 }, upgraded.AlliedContributorHandles);
    }

    [TestMethod]
    public void ReusedAllyHandle_WithANewGenerationCountsAsADistinctContributor()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident first = ConfirmedIncident(
            JusticeCrimeKind.AccessoryMurderOfficer,
            "ally-generation-one",
            811,
            4,
            JusticeCircumstances.None);
        first.IsAlliedAction = true;
        first.AllyHandle = 610;
        first.AllyGeneration = 21;

        JusticeIncident duplicateIdentity = ConfirmedIncident(
            JusticeCrimeKind.AccessoryMurderOfficer,
            "ally-generation-one-repeat",
            811,
            4,
            JusticeCircumstances.None);
        duplicateIdentity.IsAlliedAction = true;
        duplicateIdentity.AllyHandle = 610;
        duplicateIdentity.AllyGeneration = 21;

        JusticeIncident recycledHandle = ConfirmedIncident(
            JusticeCrimeKind.AccessoryMurderOfficer,
            "ally-generation-two",
            811,
            4,
            JusticeCircumstances.None);
        recycledHandle.IsAlliedAction = true;
        recycledHandle.AllyHandle = 610;
        recycledHandle.AllyGeneration = 22;

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, first, record));
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, duplicateIdentity, record));
        JusticeCharge upgraded = JusticePolicy.ApplyConfirmedIncident(state, recycledHandle, record);

        Assert.IsNotNull(upgraded);
        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(2, upgraded.AlliedContributors.Count);
        Assert.AreEqual(
            JusticeCircumstances.OrganizedBand,
            upgraded.Circumstances & JusticeCircumstances.OrganizedBand);
        CollectionAssert.AreEqual(new[] { 610 }, upgraded.AlliedContributorHandles);
        Assert.IsTrue(upgraded.HasAlliedContributor(610, 21));
        Assert.IsTrue(upgraded.HasAlliedContributor(610, 22));
    }

    [TestMethod]
    public void ExistingDirectCrime_AbsorbsLaterAccessoryWithoutDoubleCharging()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident direct = ConfirmedIncident(
            JusticeCrimeKind.AssaultOfficer,
            "direct-first",
            82,
            1,
            JusticeCircumstances.None);
        JusticeIncident accessory = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "accessory-after-direct",
            82,
            1,
            JusticeCircumstances.None);
        accessory.IsAlliedAction = true;
        accessory.AllyHandle = 603;

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, direct, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, accessory, record));

        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.AssaultOfficer, state.Charges[0].Kind);
        Assert.AreEqual(
            JusticeCircumstances.GroupCrime,
            state.Charges[0].Circumstances & JusticeCircumstances.GroupCrime);
    }

    [TestMethod]
    public void AccessoryOfficerMurder_RemainsSeparateFromWeakerDirectAssault_InBothOrders()
    {
        foreach (bool accessoryFirst in new[] { false, true })
        {
            JusticeCaseState state = NewEnabledCase();
            JusticeRecordState record = new JusticeRecordState();
            JusticeIncident directAssault = ConfirmedIncident(
                JusticeCrimeKind.AssaultOfficer,
                "direct-assault-" + accessoryFirst,
                83,
                4,
                JusticeCircumstances.None);
            JusticeIncident accessoryMurder = ConfirmedIncident(
                JusticeCrimeKind.AccessoryMurderOfficer,
                "accessory-murder-" + accessoryFirst,
                83,
                4,
                JusticeCircumstances.None);
            accessoryMurder.IsAlliedAction = true;
            accessoryMurder.AllyHandle = 604;

            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
                state,
                accessoryFirst ? accessoryMurder : directAssault,
                record));
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
                state,
                accessoryFirst ? directAssault : accessoryMurder,
                record));

            Assert.AreEqual(2, state.Charges.Count, "L'homicide allié ne doit pas disparaître derrière une agression personnelle.");
            CollectionAssert.AreEquivalent(
                new[] { JusticeCrimeKind.AssaultOfficer, JusticeCrimeKind.AccessoryMurderOfficer },
                state.Charges.Select(charge => charge.Kind).ToArray());
            JusticeCharge homicide = state.Charges.Single(
                charge => charge.Kind == JusticeCrimeKind.AccessoryMurderOfficer);
            CollectionAssert.AreEqual(new[] { 604 }, homicide.AlliedContributorHandles);
            Assert.AreEqual(
                JusticeCircumstances.GroupCrime,
                homicide.Circumstances & JusticeCircumstances.GroupCrime);
        }
    }

    [TestMethod]
    public void DirectOfficerMurder_ReplacesAccessoryMurderAndKeepsItsCollectiveAggravation()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident accessory = ConfirmedIncident(
            JusticeCrimeKind.AccessoryMurderOfficer,
            "accessory-murder-before-direct",
            84,
            5,
            JusticeCircumstances.None);
        accessory.IsAlliedAction = true;
        accessory.AllyHandle = 605;
        JusticeIncident direct = ConfirmedIncident(
            JusticeCrimeKind.MurderOfficer,
            "direct-murder-after-accessory",
            84,
            5,
            JusticeCircumstances.None);

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, accessory, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, direct, record));

        Assert.AreEqual(1, state.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.MurderOfficer, state.Charges[0].Kind);
        Assert.IsFalse(state.Charges[0].IsAlliedAction);
        CollectionAssert.AreEqual(new[] { 605 }, state.Charges[0].AlliedContributorHandles);
        Assert.AreEqual(
            JusticeCircumstances.GroupCrime,
            state.Charges[0].Circumstances & JusticeCircumstances.GroupCrime);
    }

    [TestMethod]
    public void ReusedHandleWithANewGeneration_DoesNotCollide()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.SimpleAssault, "old-ped", 90, 1, JusticeCircumstances.None),
            record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(JusticeCrimeKind.SimpleAssault, "new-ped", 90, 2, JusticeCircumstances.None),
            record));

        Assert.AreEqual(2, state.Charges.Count);
    }

    [TestMethod]
    public void SeveralVictimlessShots_CreateOneChargePerEpisode()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident first = ConfirmedIncident(
            JusticeCrimeKind.RecklessDischarge,
            "shot-one",
            0,
            0,
            JusticeCircumstances.Armed);
        JusticeIncident second = ConfirmedIncident(
            JusticeCrimeKind.RecklessDischarge,
            "shot-two",
            0,
            0,
            JusticeCircumstances.Armed);

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, first, record));
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, second, record));
        Assert.AreEqual(1, state.Charges.Count);
    }

    [TestMethod]
    public void FleeingAndEscape_AreChargedOnceForEachDistinctEpisode()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident fleeOne = ConfirmedIncident(
            JusticeCrimeKind.EvadingPolice, "flee-1", 0, 0, JusticeCircumstances.None);
        JusticeIncident fleeDuplicate = ConfirmedIncident(
            JusticeCrimeKind.EvadingPolice, "flee-2", 0, 0, JusticeCircumstances.None);
        JusticeIncident fleeNewEpisode = ConfirmedIncident(
            JusticeCrimeKind.EvadingPolice, "flee-3", 0, 0, JusticeCircumstances.None);
        fleeOne.EpisodeId = "pursuit-a";
        fleeDuplicate.EpisodeId = "pursuit-a";
        fleeNewEpisode.EpisodeId = "pursuit-b";

        JusticeIncident escapeOne = ConfirmedIncident(
            JusticeCrimeKind.Escape, "escape-1", 0, 0, JusticeCircumstances.InCustody);
        JusticeIncident escapeDuplicate = ConfirmedIncident(
            JusticeCrimeKind.Escape, "escape-2", 0, 0, JusticeCircumstances.InCustody);
        JusticeIncident escapeNewEpisode = ConfirmedIncident(
            JusticeCrimeKind.Escape, "escape-3", 0, 0, JusticeCircumstances.InCustody);
        escapeOne.EpisodeId = "custody-a";
        escapeDuplicate.EpisodeId = "custody-a";
        escapeNewEpisode.EpisodeId = "custody-b";

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, fleeOne, record));
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, fleeDuplicate, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, fleeNewEpisode, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, escapeOne, record));
        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, escapeDuplicate, record));
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, escapeNewEpisode, record));

        Assert.IsTrue(state.IsFleeingChargedForEpisode("pursuit-a"));
        Assert.IsTrue(state.IsFleeingChargedForEpisode("pursuit-b"));
        Assert.IsTrue(state.IsEscapeChargedForEpisode("custody-a"));
        Assert.IsTrue(state.IsEscapeChargedForEpisode("custody-b"));
        Assert.AreEqual(4, state.Charges.Count);
    }

    [TestMethod]
    public void ChargedEpisodeCache_IsBoundedWithoutForgettingActiveCharges()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        for (int index = 0; index < JusticePolicy.MaxChargedEpisodeIds + 3; index++)
        {
            JusticeIncident incident = ConfirmedIncident(
                JusticeCrimeKind.EvadingPolice,
                "bounded-flee-" + index,
                0,
                0,
                JusticeCircumstances.None);
            incident.EpisodeId = "bounded-pursuit-" + index;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, incident, record));
        }

        Assert.AreEqual(JusticePolicy.MaxChargedEpisodeIds, state.FleeingChargedEpisodeIds.Count);
        Assert.IsTrue(state.IsFleeingChargedForEpisode("bounded-pursuit-0"));
        Assert.IsTrue(state.IsFleeingChargedForEpisode(
            "bounded-pursuit-" + (JusticePolicy.MaxChargedEpisodeIds + 2)));
    }

    [TestMethod]
    public void ConfirmedIncident_IsIgnoredWhileJusticeIsDisabled()
    {
        JusticeCaseState state = new JusticeCaseState { Enabled = false };
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.VehicleTheft,
            "disabled-crime",
            104,
            1,
            JusticeCircumstances.None);

        Assert.IsNull(JusticePolicy.ApplyConfirmedIncident(state, incident, new JusticeRecordState()));
        Assert.AreEqual(0, state.Charges.Count);
        Assert.AreEqual(0, state.ProcessedIncidentIds.Count);
        Assert.AreEqual(JusticePhase.AtLarge, state.Phase);
    }

    [TestMethod]
    public void CaseTotals_AreSaturatingAndNeverBecomeNegative()
    {
        JusticeCaseState state = NewEnabledCase();
        for (int index = 0; index < 1000; index++)
        {
            state.Charges.Add(new JusticeCharge
            {
                Points = int.MaxValue,
                Fine = long.MaxValue,
                SentenceSeconds = int.MaxValue
            });
        }

        state.RecalculateTotals();
        Assert.AreEqual(JusticePolicy.MaxActiveScore, state.ActiveScore);
        Assert.AreEqual(JusticePolicy.MaxActiveFine, state.FineDue);
        Assert.AreEqual(JusticePolicy.MaxActiveSentenceSeconds, state.SentenceSeconds);

        state.Charges.Clear();
        state.Charges.Add(new JusticeCharge { Points = -1, Fine = -1L, SentenceSeconds = -1 });
        state.RecalculateTotals();
        Assert.AreEqual(0, state.ActiveScore);
        Assert.AreEqual(0L, state.FineDue);
        Assert.AreEqual(0, state.SentenceSeconds);
    }

    [TestMethod]
    public void ActiveCharges_ConsolidateBeyondFiveHundredTwelveWithoutLosingSanctions()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState { RecidivismIndex = 100 };
        const int confirmedFacts = 4000;
        int scoreAtCapacity = 0;

        for (int index = 0; index < confirmedFacts; index++)
        {
            JusticeIncident incident = ConfirmedIncident(
                JusticeCrimeKind.MurderOfficer,
                "charge-cap-" + index,
                1000 + index,
                index + 1,
                JusticeCircumstances.Armed |
                JusticeCircumstances.ExplosiveOrIncendiary |
                JusticeCircumstances.ActiveWarrant |
                JusticeCircumstances.InCustody |
                JusticeCircumstances.OrganizedBand);
            incident.EpisodeId = "charge-cap-episode-" + index;

            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, incident, record));
            Assert.IsTrue(state.Charges.Count <= JusticePolicy.MaxActiveCharges);
            if (index + 1 == JusticePolicy.MaxActiveCharges)
            {
                scoreAtCapacity = state.ActiveScore;
            }
        }

        Assert.AreEqual(JusticePolicy.MaxActiveCharges, state.Charges.Count);
        Assert.IsTrue(state.Charges.Any(charge => charge.IsAggregate));
        Assert.AreEqual(
            confirmedFacts,
            JusticePolicy.GetRepresentedChargeCount(state),
            "Chaque fait confirmé doit rester représenté après consolidation.");
        Assert.IsTrue(scoreAtCapacity > 0 && scoreAtCapacity < JusticePolicy.MaxActiveScore);
        Assert.AreEqual(JusticePolicy.MaxActiveScore, state.ActiveScore);
        Assert.IsTrue(state.FineDue > 250000L);
        Assert.IsTrue(state.FineDue < JusticePolicy.MaxActiveFine);
        Assert.AreEqual(JusticePolicy.MaxActiveSentenceSeconds, state.SentenceSeconds);
        AssertCaseTotalsMatchCharges(state);
    }

    [TestMethod]
    public void FineDebt_CanGrowFarBeyondTheFormerTwoHundredFiftyThousandLimit()
    {
        JusticeCaseState state = NewEnabledCase();
        for (int index = 0; index < 20; index++)
        {
            state.Charges.Add(new JusticeCharge
            {
                Points = 1,
                Fine = 100000L,
                SentenceSeconds = 0
            });
        }

        state.RecalculateTotals();

        Assert.AreEqual(2000000L, state.FineDue);
        Assert.IsTrue(JusticePolicy.MaxActiveFine > 250000L);
    }

    [TestMethod]
    public void ActiveChargeLimit_StillAllowsSupersessionAtFullCapacity()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        for (int index = 0; index < JusticePolicy.MaxActiveCharges; index++)
        {
            JusticeIncident damage = ConfirmedIncident(
                JusticeCrimeKind.VehicleDamage,
                "full-damage-" + index,
                2000 + index,
                index + 1,
                JusticeCircumstances.None);
            damage.EpisodeId = "full-episode-" + index;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, damage, record));
        }

        int scoreBeforeReplacement = state.ActiveScore;
        JusticeIncident destruction = ConfirmedIncident(
            JusticeCrimeKind.VehicleDestruction,
            "full-destruction",
            2000,
            1,
            JusticeCircumstances.None);
        destruction.EpisodeId = "full-episode-0";

        JusticeCharge replacement = JusticePolicy.ApplyConfirmedIncident(state, destruction, record);

        Assert.IsNotNull(replacement);
        Assert.AreEqual(JusticePolicy.MaxActiveCharges, state.Charges.Count);
        Assert.IsFalse(state.Charges.Any(charge => charge.IsAggregate));
        Assert.AreEqual(scoreBeforeReplacement + 10, state.ActiveScore);
        Assert.IsFalse(state.Charges.Any(charge =>
            charge.Kind == JusticeCrimeKind.VehicleDamage &&
            charge.VictimHandle == 2000 &&
            charge.VictimGeneration == 1));
        AssertCaseTotalsMatchCharges(state);
    }

    [TestMethod]
    public void ActiveChargeLimit_StillAllowsCollectiveMergeAtFullCapacity()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident firstContribution = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "full-collective-first",
            9001,
            1,
            JusticeCircumstances.None);
        firstContribution.EpisodeId = "full-collective-episode";
        firstContribution.IsAlliedAction = true;
        firstContribution.AllyHandle = 701;
        firstContribution.AllyGeneration = 11;
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, firstContribution, record));

        for (int index = 1; index < JusticePolicy.MaxActiveCharges; index++)
        {
            JusticeIncident filler = ConfirmedIncident(
                JusticeCrimeKind.VehicleDamage,
                "full-collective-filler-" + index,
                10000 + index,
                index,
                JusticeCircumstances.None);
            filler.EpisodeId = "full-collective-filler-episode-" + index;
            Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, filler, record));
        }

        JusticeIncident secondContribution = ConfirmedIncident(
            JusticeCrimeKind.AccessoryAssaultOfficer,
            "full-collective-second",
            9001,
            1,
            JusticeCircumstances.None);
        secondContribution.EpisodeId = "full-collective-episode";
        secondContribution.IsAlliedAction = true;
        secondContribution.AllyHandle = 702;
        secondContribution.AllyGeneration = 12;

        JusticeCharge merged = JusticePolicy.ApplyConfirmedIncident(state, secondContribution, record);

        Assert.IsNotNull(merged);
        Assert.AreEqual(JusticePolicy.MaxActiveCharges, state.Charges.Count);
        Assert.IsFalse(state.Charges.Any(charge => charge.IsAggregate));
        Assert.IsTrue(merged.HasAlliedContributor(701, 11));
        Assert.IsTrue(merged.HasAlliedContributor(702, 12));
        Assert.IsTrue((merged.Circumstances & JusticeCircumstances.OrganizedBand) != 0);
        AssertCaseTotalsMatchCharges(state);
    }

    [TestMethod]
    public void ConvictionsKeepTwentyEntries_AndRecidivismUsesSeverity()
    {
        JusticeRecordState record = new JusticeRecordState();
        for (int index = 0; index < 25; index++)
        {
            JusticeCaseState state = NewEnabledCase();
            state.Charges.Add(new JusticeCharge
            {
                IncidentId = "crime-" + index,
                Points = index == 0 ? 5 : 120,
                Fine = 100L,
                SentenceSeconds = 15
            });
            state.CustodyEpisodeId = "custody-" + index;

            Assert.IsNotNull(JusticePolicy.ApplyConviction(
                state,
                record,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index)));
        }

        Assert.AreEqual(20, record.Convictions.Count);
        Assert.AreEqual(100, record.RecidivismIndex);
        Assert.AreEqual("conviction:custody-5", record.Convictions[0].ConvictionId);
        Assert.AreEqual(25, record.AppliedConvictionIds.Count);
    }

    [TestMethod]
    public void Conviction_IsIdempotentAndKeepsAChargeSummary()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();
        state.CustodyEpisodeId = "custody-idempotent";
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(
                JusticeCrimeKind.SimpleAssault,
                "conviction-charge",
                110,
                1,
                JusticeCircumstances.Armed),
            record));

        JusticeConviction first = JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc));
        JusticeConviction replay = JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 2, 1, 10, 5, 0, DateTimeKind.Utc));

        Assert.AreSame(first, replay);
        Assert.AreEqual(1, record.Convictions.Count);
        Assert.AreEqual(5, record.RecidivismIndex);
        Assert.AreEqual(1, record.AppliedConvictionIds.Count);
        Assert.AreEqual(1, first.Charges.Count);
        Assert.AreEqual(JusticeCrimeKind.SimpleAssault, first.Charges[0].Kind);
        Assert.AreEqual("Agression simple", first.Charges[0].DisplayName);
        Assert.AreEqual(state.ActiveScore, first.Score);
    }

    [DataTestMethod]
    [DataRow(5, 2)]
    [DataRow(10, 5)]
    [DataRow(25, 10)]
    [DataRow(50, 18)]
    [DataRow(80, 28)]
    [DataRow(120, 35)]
    public void Conviction_UsesTheExpectedRecidivismIncreaseForEverySeverity(
        int score,
        int expectedIncrease)
    {
        JusticeCaseState state = NewEnabledCase();
        state.CustodyEpisodeId = "severity-" + score;
        state.Charges.Add(new JusticeCharge
        {
            ChargeId = "severity-charge-" + score,
            IncidentId = "severity-incident-" + score,
            EpisodeId = state.CustodyEpisodeId,
            DisplayName = "Qualification test",
            Points = score
        });
        JusticeRecordState record = new JusticeRecordState();

        Assert.IsNotNull(JusticePolicy.ApplyConviction(state, record, DateTime.UtcNow));
        Assert.AreEqual(expectedIncrease, record.RecidivismIndex);
    }

    [TestMethod]
    public void NewConfirmedCharge_ResetsCleanRecidivismProgress()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState
        {
            RecidivismIndex = 40,
            CleanGameplaySeconds = 5399,
            AppliedCleanDecay = 5
        };

        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(
            state,
            ConfirmedIncident(
                JusticeCrimeKind.VehicleDamage,
                "break-clean-time",
                111,
                1,
                JusticeCircumstances.None),
            record));

        Assert.AreEqual(0, record.CleanGameplaySeconds);
        Assert.AreEqual(0, record.AppliedCleanDecay);
    }

    [TestMethod]
    public void CleanRecidivismDecay_UsesProgressiveInGameBandsAndFreezesWhenIneligible()
    {
        JusticeRecordState record = new JusticeRecordState { RecidivismIndex = 100 };

        Assert.AreEqual(0, JusticePolicy.AdvanceCleanTime(record, 1800, true));
        Assert.AreEqual(0, JusticePolicy.AdvanceCleanTime(record, 599, true));
        Assert.AreEqual(1, JusticePolicy.AdvanceCleanTime(record, 1, true));
        Assert.AreEqual(0, JusticePolicy.AdvanceCleanTime(record, 600, false));
        Assert.AreEqual(2400, record.CleanGameplaySeconds);

        Assert.AreEqual(5, JusticePolicy.AdvanceCleanTime(record, 3000, true));
        Assert.AreEqual(2, JusticePolicy.AdvanceCleanTime(record, 600, true));
        Assert.AreEqual(16, JusticePolicy.AdvanceCleanTime(record, 4800, true));
        Assert.AreEqual(3, JusticePolicy.AdvanceCleanTime(record, 600, true));
        Assert.AreEqual(73, record.RecidivismIndex);
    }

    [TestMethod]
    public void PhaseMachine_CoversCaptureTransportCustodyEscapeAndRelease()
    {
        JusticeCaseState state = NewEnabledCase();

        AssertTransition(state, JusticePhase.AtLarge, JusticeSignal.ConfirmedCharge, JusticeOperationKind.None);
        AssertTransition(state, JusticePhase.AtLarge, JusticeSignal.WarrantRecognized, JusticeOperationKind.None);
        state.Phase = JusticePhase.Wanted;
        AssertTransition(state, JusticePhase.Surrendering, JusticeSignal.ArrestStarted, JusticeOperationKind.None);
        AssertTransition(state, JusticePhase.Captured, JusticeSignal.ArrestCompleted, JusticeOperationKind.Capture);
        AssertTransition(state, JusticePhase.Transporting, JusticeSignal.TransferReady, JusticeOperationKind.Transport);
        AssertTransition(state, JusticePhase.Incarcerated, JusticeSignal.TransferCompleted, JusticeOperationKind.EnterCustody);
        AssertTransition(state, JusticePhase.Escaping, JusticeSignal.LeftCustody, JusticeOperationKind.None);
        AssertTransition(state, JusticePhase.Incarcerated, JusticeSignal.Restrained, JusticeOperationKind.None);
        AssertTransition(state, JusticePhase.Escaping, JusticeSignal.LeftCustody, JusticeOperationKind.None);
        AssertTransition(state, JusticePhase.Fugitive, JusticeSignal.EscapeConfirmed, JusticeOperationKind.RegisterEscape);
        Assert.IsTrue(state.HasWarrant);
        Assert.IsFalse(state.IsEscapeChargedForEpisode("episode-transition"));

        JusticeIncident escape = ConfirmedIncident(
            JusticeCrimeKind.Escape,
            "transition-escape-charge",
            0,
            0,
            JusticeCircumstances.InCustody);
        escape.EpisodeId = "episode-transition";
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, escape, new JusticeRecordState()));
        Assert.IsTrue(state.IsEscapeChargedForEpisode("episode-transition"));

        state.Phase = JusticePhase.Incarcerated;
        AssertTransition(state, JusticePhase.AtLarge, JusticeSignal.SentenceCompleted, JusticeOperationKind.Release);
    }

    [TestMethod]
    public void TransportTimeout_FallsBackToCustodyInsteadOfSoftLocking()
    {
        JusticeCaseState state = NewEnabledCase();
        state.Phase = JusticePhase.Transporting;

        JusticeTransition transition = JusticePolicy.Transition(state, new JusticeTickInput
        {
            EpisodeId = "timeout",
            Signals = JusticeSignal.TransferTimedOut
        });

        Assert.AreEqual(JusticePhase.Incarcerated, transition.NextPhase);
        Assert.AreEqual(JusticeOperationKind.EnterCustody, transition.Operation.Kind);
    }

    [TestMethod]
    public void Operations_AreCanonicalExactlyOnceAndNeverEvictedDuringActiveCase()
    {
        JusticeCaseState state = NewEnabledCase();
        JusticeOperation first = Operation(JusticeOperationKind.ConfiscateInventory, "custody-1");

        Assert.IsTrue(JusticePolicy.TryRegisterOperation(state, first));
        Assert.IsFalse(JusticePolicy.TryRegisterOperation(state, first));
        Assert.IsFalse(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation("ApplyFine:forged", JusticeOperationKind.RestoreInventory, "forged")));
        Assert.IsFalse(JusticePolicy.TryRegisterOperation(
            state,
            new JusticeOperation(string.Empty, JusticeOperationKind.ApplyFine, string.Empty)));
        Assert.IsFalse(
            JusticePolicy.TryRegisterOperation(
                state,
                Operation(JusticeOperationKind.ApplyWantedFloor, "legacy-wanted")),
            "La valeur v1 reste lisible mais aucun nouveau plancher wanted ne peut être enregistré.");

        for (int index = 0; index < JusticePolicy.MaxRememberedOperations + 20; index++)
        {
            JusticeOperation operation = Operation(JusticeOperationKind.ApplyFine, "episode-" + index);
            Assert.IsTrue(JusticePolicy.TryRegisterOperation(state, operation));
        }

        Assert.AreEqual(JusticePolicy.MaxRememberedOperations + 21, state.CompletedOperationIds.Count);
        Assert.IsTrue(state.CompletedOperationIds.Contains(first.OperationId));
        Assert.IsFalse(JusticePolicy.TryRegisterOperation(state, first));
    }

    [TestMethod]
    public void DisabledState_DoesNotAdvanceThePhase()
    {
        JusticeCaseState state = new JusticeCaseState
        {
            Enabled = false,
            Phase = JusticePhase.Wanted
        };

        JusticeTransition transition = JusticePolicy.Transition(state, new JusticeTickInput
        {
            Signals = JusticeSignal.ArrestCompleted,
            EpisodeId = "disabled"
        });

        Assert.IsFalse(transition.Changed);
        Assert.IsNull(transition.Operation);
        Assert.AreEqual(JusticePhase.Wanted, state.Phase);
    }

    [TestMethod]
    public void DeterministicFuzz_NeverBreaksDomainInvariants()
    {
        Random random = new Random(741852);
        JusticeCaseState state = NewEnabledCase();
        JusticeRecordState record = new JusticeRecordState();

        for (int index = 0; index < 3000; index++)
        {
            JusticeCrimeKind kind = (JusticeCrimeKind)random.Next(0, JusticePolicy.Catalog.Count);
            JusticeCircumstances circumstances = JusticeCircumstances.None;
            if (random.Next(2) == 0) circumstances |= JusticeCircumstances.Armed;
            if (random.Next(5) == 0) circumstances |= JusticeCircumstances.ActiveWarrant;
            if (random.Next(8) == 0) circumstances |= JusticeCircumstances.OrganizedBand;

            JusticeIncident incident = ConfirmedIncident(
                kind,
                "fuzz-" + index,
                random.Next(1, 64),
                index + 1,
                circumstances);
            incident.EpisodeId = "fuzz-episode-" + (index / 100);
            incident.AdditionalVictimCount = random.Next(0, 4);
            JusticePolicy.ApplyConfirmedIncident(state, incident, record);

            Assert.IsTrue(state.Charges.Count <= JusticePolicy.MaxActiveCharges);
            Assert.IsTrue(state.ActiveScore >= 0 && state.ActiveScore <= JusticePolicy.MaxActiveScore);
            Assert.IsTrue(state.FineDue >= 0L && state.FineDue <= JusticePolicy.MaxActiveFine);
            Assert.IsTrue(state.SentenceSeconds >= 0 && state.SentenceSeconds <= JusticePolicy.MaxActiveSentenceSeconds);
            Assert.IsTrue(Enum.IsDefined(typeof(JusticeSeverity), JusticePolicy.GetSeverity(state.ActiveScore)));
            AssertCaseTotalsMatchCharges(state);
        }
    }

    [TestMethod]
    public void ArrestCompletionProbe_AbsorbsNativeBackoffWithoutMaskingAnOlderArrest()
    {
        Assert.IsTrue(JusticePolicy.IsArrestCompletionWithinProbeWindow(2000, 0L));
        Assert.IsTrue(JusticePolicy.IsArrestCompletionWithinProbeWindow(5000, 5000L));
        Assert.IsTrue(JusticePolicy.IsArrestCompletionWithinProbeWindow(7100, 5000L));
        Assert.IsFalse(JusticePolicy.IsArrestCompletionWithinProbeWindow(60000, 5000L));
        Assert.IsFalse(JusticePolicy.IsArrestCompletionWithinProbeWindow(-1, 5000L));
    }

    [TestMethod]
    public void VehicleImpact_RequiresContactAndHostileSpeed()
    {
        Assert.IsFalse(JusticePolicy.IsVehicleImpactSevere(2.0f, true, 7.5f));
        Assert.IsFalse(JusticePolicy.IsVehicleImpactSevere(12.0f, false, 7.5f));
        Assert.IsTrue(JusticePolicy.IsVehicleImpactSevere(7.5f, true, 7.5f));
        Assert.IsTrue(JusticePolicy.IsVehicleImpactSevere(18.0f, true, 7.5f));
    }

    [TestMethod]
    public void Conviction_PreservesCircumstancesForTheConsultableRecord()
    {
        JusticeCaseState state = NewEnabledCase();
        state.CustodyEpisodeId = "custody:record-circumstances";
        JusticeRecordState record = new JusticeRecordState();
        JusticeIncident incident = ConfirmedIncident(
            JusticeCrimeKind.AggravatedAssault,
            "incident:record-circumstances",
            81,
            1,
            JusticeCircumstances.Armed | JusticeCircumstances.VehicleUsedAsWeapon);
        Assert.IsNotNull(JusticePolicy.ApplyConfirmedIncident(state, incident, record));

        JusticeConviction conviction = JusticePolicy.ApplyConviction(
            state,
            record,
            new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc));

        Assert.IsNotNull(conviction);
        Assert.AreEqual(1, conviction.Charges.Count);
        Assert.AreEqual(
            JusticeCircumstances.Armed | JusticeCircumstances.VehicleUsedAsWeapon,
            conviction.Charges[0].Circumstances);
    }

    private static void AssertCaseTotalsMatchCharges(JusticeCaseState state)
    {
        long expectedScore = 0L;
        long expectedFine = 0L;
        long expectedSentence = 0L;
        foreach (JusticeCharge charge in state.Charges)
        {
            Assert.IsNotNull(charge);
            expectedScore = JusticePolicy.SaturatingAdd(
                expectedScore,
                Math.Max(0, charge.Points),
                JusticePolicy.MaxActiveScore);
            expectedFine = JusticePolicy.SaturatingAdd(
                expectedFine,
                Math.Max(0L, charge.Fine),
                JusticePolicy.MaxActiveFine);
            expectedSentence = JusticePolicy.SaturatingAdd(
                expectedSentence,
                Math.Max(0, charge.SentenceSeconds),
                JusticePolicy.MaxActiveSentenceSeconds);
        }

        Assert.AreEqual((int)expectedScore, state.ActiveScore);
        Assert.AreEqual(expectedFine, state.FineDue);
        Assert.AreEqual((int)expectedSentence, state.SentenceSeconds);
    }

    private static JusticeCaseState NewEnabledCase()
    {
        return new JusticeCaseState
        {
            Enabled = true,
            Phase = JusticePhase.AtLarge
        };
    }

    private static JusticeEvidence Evidence(JusticeEvidenceKind kind, long observedAtMs, long reportDueAtMs)
    {
        return new JusticeEvidence
        {
            Kind = kind,
            WitnessHandle = 999,
            WitnessGeneration = 1,
            ObservedAtMs = observedAtMs,
            ReportDueAtMs = reportDueAtMs,
            HasPlausibleObserver = true
        };
    }

    private static JusticeIncident CreateIncident(
        JusticeCrimeKind kind,
        string id,
        long createdAtMs,
        int victimHandle,
        int victimGeneration)
    {
        return new JusticeIncident
        {
            IncidentId = id,
            EpisodeId = "episode",
            Kind = kind,
            VictimHandle = victimHandle,
            VictimGeneration = victimGeneration,
            CreatedAtMs = createdAtMs,
            ExpiresAtMs = createdAtMs + JusticePolicy.PendingIncidentLifetimeMs,
            Evidence = Evidence(JusticeEvidenceKind.PoliceWitness, createdAtMs, createdAtMs)
        };
    }

    private static JusticeIncident ConfirmedIncident(
        JusticeCrimeKind kind,
        string id,
        int victimHandle,
        int victimGeneration,
        JusticeCircumstances circumstances)
    {
        JusticeIncident incident = CreateIncident(kind, id, 1000L, victimHandle, victimGeneration);
        incident.Circumstances = circumstances;
        Assert.IsTrue(incident.TryConfirm(1000L, true));
        return incident;
    }

    private static void AssertSeverity(int score, JusticeSeverity expectedSeverity)
    {
        Assert.AreEqual(expectedSeverity, JusticePolicy.GetSeverity(score), "score=" + score);
    }

    private static void AssertTransition(
        JusticeCaseState state,
        JusticePhase expectedPhase,
        JusticeSignal signal,
        JusticeOperationKind expectedOperation)
    {
        JusticeTransition transition = JusticePolicy.Transition(state, new JusticeTickInput
        {
            Signals = signal,
            EpisodeId = "episode-transition"
        });

        Assert.AreEqual(expectedPhase, transition.NextPhase);
        Assert.AreEqual(expectedPhase, state.Phase);
        if (expectedOperation == JusticeOperationKind.None)
        {
            Assert.IsNull(transition.Operation);
        }
        else
        {
            Assert.IsNotNull(transition.Operation);
            Assert.AreEqual(expectedOperation, transition.Operation.Kind);
        }
    }

    private static JusticeOperation Operation(JusticeOperationKind kind, string episodeId)
    {
        return new JusticeOperation(
            JusticePolicy.CreateOperationId(kind, episodeId),
            kind,
            episodeId);
    }
}
