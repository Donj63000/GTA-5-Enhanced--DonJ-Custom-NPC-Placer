using System;
using System.Globalization;
using System.Xml;

public sealed partial class DonJEnemySpawner
{
    private const int JusticeVoluntaryPaymentRetryMs = 750;

    private sealed class JusticeVoluntaryFinePaymentIntent
    {
        internal string PaymentId = string.Empty;
        internal int Slot = -1;
        internal long FineBefore;
        internal int DebitAmount;
        internal int CashBefore;
        internal int CashAfter;
        internal long PreparedAtUtcTicks;
        internal bool DebitAttempted;
        internal long AttemptedAtUtcTicks;
        internal JusticeCashWriteResult CashWriteResult = JusticeCashWriteResult.Unknown;
        internal bool DebtCommitted;
    }

    private JusticeVoluntaryFinePaymentIntent _justiceVoluntaryFinePaymentIntent;
    private int _justiceNextVoluntaryPaymentResumeAt;

    private bool CanJusticeMenuPaySelectedProfile()
    {
        return CanJusticeMenuPaySelectedProfile(GetJusticeMenuSelectedProfileSlot());
    }

    private bool CanJusticeMenuPaySelectedProfile(int selectedSlot)
    {
        return IsJusticePlayedProfileContextReady() &&
               IsJusticeCanonicalProfileSlot(selectedSlot) &&
               selectedSlot == _justiceActivePlayerProfileSlot &&
               GetJusticeCanonicalPlayerSlotSafe() == selectedSlot;
    }

    // Je garde le menu hors de la transaction : il demande un paiement, puis ce
    // moteur valide l'identité, précommitte le débit et le reprend idempotemment.
    private void RequestJusticeSelectedProfileFinePaymentConfirmation()
    {
        int selectedSlot = GetJusticeMenuSelectedProfileSlot();
        if (!CanJusticeMenuPaySelectedProfile(selectedSlot))
        {
            ShowStatus(
                "Paiement disponible uniquement pour le héros GTA actuellement joué.",
                3600);
            return;
        }

        if (_justiceVoluntaryFinePaymentIntent != null)
        {
            ResumeJusticeVoluntaryFinePayment();
            ShowStatus("Paiement Justice déjà engagé : reprise sécurisée en cours…", 3400);
            return;
        }

        if (_justiceCaseState == null || _justiceCaseState.FineDue <= 0L)
        {
            ShowStatus("Justice : aucune dette à payer pour ce personnage.", 3200);
            return;
        }

        if (_justiceBackupRepairPending || JusticeIsCustodyActive ||
            _justiceFineDebitIntent != null || _justiceAmnestyPending ||
            _justiceLegalReleaseFinalizationPending ||
            _justiceCustodyTransferRollbackFinalizationPending)
        {
            ShowStatus("Paiement indisponible pendant une transaction Justice.", 3600);
            return;
        }

        RequestDangerConfirmation(MainMenuAction.JusticePayFine);
    }

    private void RequestJusticeVoluntaryFinePayment()
    {
        RequestJusticeConfirmedVoluntaryFinePayment(
            GetJusticeMenuSelectedProfileSlot(),
            _justiceCaseState == null ? 0L : Math.Max(0L, _justiceCaseState.FineDue));
    }

    private void RequestJusticeConfirmedVoluntaryFinePayment(
        int requestedProfileSlot,
        long confirmedFineAmount)
    {
        int selectedSlot = requestedProfileSlot;
        if (!CanJusticeMenuPaySelectedProfile(selectedSlot))
        {
            ShowStatus(
                "Paiement disponible uniquement avec le héros GTA actuellement identifié.",
                3600);
            return;
        }

        if (_justiceVoluntaryFinePaymentIntent != null)
        {
            ResumeJusticeVoluntaryFinePayment();
            ShowStatus("Paiement Justice déjà engagé : reprise sécurisée en cours…", 3400);
            return;
        }

        if (_justiceCaseState == null || _justiceCaseState.FineDue <= 0L)
        {
            ShowStatus("Justice : aucune dette à payer pour ce personnage.", 3200);
            return;
        }

        if (JusticeIsCustodyActive || _justiceFineDebitIntent != null ||
            _justiceAmnestyPending)
        {
            ShowStatus("Paiement indisponible pendant une transaction Justice.", 3600);
            return;
        }

        long currentFine = Math.Max(0L, _justiceCaseState.FineDue);
        long consentedFine = Math.Max(0L, confirmedFineAmount);
        if (consentedFine <= 0L)
        {
            ShowStatus("Justice : aucun montant confirmé, aucun débit effectué.", 3400);
            return;
        }
        if (currentFine > consentedFine)
        {
            // Je n'étends jamais un consentement déjà affiché : une dette qui a
            // augmenté doit repasser par une nouvelle confirmation explicite.
            ShowStatus(
                "Justice : dette modifiée à " + FormatJusticeMoney(currentFine) +
                ", confirmez à nouveau le paiement.",
                4600);
            return;
        }

        int cash;
        if (!TryReadJusticeSinglePlayerCash(selectedSlot, out cash))
        {
            ShowStatus("Justice : argent indisponible, aucun débit effectué.", 3600);
            return;
        }

        long planned = Math.Min(currentFine, consentedFine);
        planned = Math.Min(planned, Math.Max(0, cash));
        planned = Math.Min(planned, int.MaxValue);
        if (planned <= 0L)
        {
            ShowStatus("Justice : fonds insuffisants, aucun débit effectué.", 3400);
            return;
        }

        _justiceVoluntaryFinePaymentIntent = new JusticeVoluntaryFinePaymentIntent
        {
            PaymentId = "payment:" + Guid.NewGuid().ToString("N"),
            Slot = selectedSlot,
            FineBefore = _justiceCaseState.FineDue,
            DebitAmount = (int)planned,
            CashBefore = cash,
            CashAfter = cash - (int)planned,
            PreparedAtUtcTicks = DateTime.UtcNow.Ticks
        };
        JusticeMarkStateDirty();

        // Les deux écritures placent l'intention dans le primaire puis son .bak
        // avant le moindre STAT_SET_INT. Si le disque refuse, aucun dollar ne part.
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            // Je conserve l'intention en mémoire car la première écriture a pu
            // atteindre le primaire avant l'échec de la copie redondante. La
            // reprise idempotente réaffirmera le WAL avant tout débit ; annoncer
            // une annulation ici rendrait au contraire ce primaire dangereux.
            ShowStatus(
                "Paiement en attente : sauvegarde Justice indisponible, aucun débit effectué.",
                4200);
            return;
        }

        ResumeJusticeVoluntaryFinePayment();
    }

    private bool ResumeJusticeVoluntaryFinePayment()
    {
        JusticeVoluntaryFinePaymentIntent intent = _justiceVoluntaryFinePaymentIntent;
        if (intent == null)
        {
            return true;
        }

        if (_justiceCaseState == null ||
            !IsJusticePlayedProfileContextReady() ||
            !IsJusticeCanonicalProfileSlot(intent.Slot) ||
            _justiceActivePlayerProfileSlot != intent.Slot ||
            GetJusticeCanonicalPlayerSlotSafe() != intent.Slot ||
            JusticeIsCustodyActive || _justiceFineDebitIntent != null)
        {
            return false;
        }

        int now = GetMenuGameTimeSafe();
        if (_justiceNextVoluntaryPaymentResumeAt != 0 &&
            !JusticeCustodyHasReached(now, _justiceNextVoluntaryPaymentResumeAt))
        {
            return false;
        }
        _justiceNextVoluntaryPaymentResumeAt = JusticeCustodyFutureTime(
            now,
            JusticeVoluntaryPaymentRetryMs);

        if (intent.DebtCommitted)
        {
            return FinalizeJusticeVoluntaryPaymentIntent(intent);
        }

        // Je réaffirme toujours le WAL avant de consulter ou modifier le cash.
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        if (!intent.DebitAttempted)
        {
            int observedCash;
            if (!TryReadJusticeSinglePlayerCash(intent.Slot, out observedCash) ||
                observedCash != intent.CashBefore)
            {
                // Le solde a changé depuis le clic : je n'utilise jamais un plan
                // devenu obsolète et je n'effectue aucune écriture financière.
                return AbortJusticeVoluntaryPaymentIntent(
                    intent,
                    "solde modifié avant débit, paiement annulé.");
            }

            intent.DebitAttempted = true;
            intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks;
            intent.CashWriteResult = JusticeCashWriteResult.Unknown;
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }

            intent.CashWriteResult = TryWriteJusticeSinglePlayerCash(
                intent.Slot,
                intent.CashAfter);
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }
        }

        if (intent.CashWriteResult == JusticeCashWriteResult.Unknown)
        {
            int observedCash;
            if (!TryReadJusticeSinglePlayerCash(intent.Slot, out observedCash))
            {
                if (!JusticePolicy.HasFineDebitAttemptTimedOut(
                    intent.AttemptedAtUtcTicks,
                    DateTime.UtcNow.Ticks))
                {
                    return false;
                }

                // Je ne rejoue jamais une écriture déjà tentée. Après le délai
                // persistant, je présume le débit appliqué afin d'éviter tout
                // double prélèvement et de sortir durablement de la reprise.
                intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
                LogWarning(
                    "Justice.Paiement",
                    "Réconciliation expirée sans lecture cash; débit volontaire présumé appliqué (at-most-once).");
            }
            else if (observedCash == intent.CashAfter)
            {
                intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
            }
            else if (observedCash == intent.CashBefore)
            {
                intent.CashWriteResult = JusticeCashWriteResult.Rejected;
            }
            else
            {
                // Une écriture tentée n'est jamais rejouée. Si le solde a encore
                // bougé, je considère le débit appliqué pour garantir at-most-once.
                intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
                LogWarning(
                    "Justice.Paiement",
                    "Solde ambigu après STAT_SET_INT; débit présumé appliqué sans nouvelle écriture.");
            }
            JusticeMarkStateDirty();
            if (!PersistJusticeCriticalPrecommitRedundantly())
            {
                return false;
            }
        }

        if (intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
        {
            _justiceCaseState.VoluntaryFinePaid = JusticePolicy.SaturatingAdd(
                _justiceCaseState.VoluntaryFinePaid,
                intent.DebitAmount,
                JusticePolicy.MaxActiveFine);
            _justiceCaseState.FineDue = Math.Max(
                0L,
                _justiceCaseState.FineDue - intent.DebitAmount);
        }

        intent.DebtCommitted = true;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        return FinalizeJusticeVoluntaryPaymentIntent(intent);
    }

    private bool AbortJusticeVoluntaryPaymentIntent(
        JusticeVoluntaryFinePaymentIntent intent,
        string reason)
    {
        if (intent == null || intent.DebitAttempted)
        {
            return false;
        }

        // Je transforme d'abord l'abandon en décision financière terminale. Le
        // primaire et le backup savent ainsi qu'aucun STAT_SET_INT ne doit être
        // émis, même si le primaire est ensuite corrompu avant l'effacement final.
        intent.DebitAttempted = true;
        intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks;
        intent.CashWriteResult = JusticeCashWriteResult.Rejected;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            return false;
        }

        intent.DebtCommitted = true;
        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        bool finalized = FinalizeJusticeVoluntaryPaymentIntent(intent);
        if (finalized && !string.IsNullOrWhiteSpace(reason))
        {
            ShowStatus("Justice : " + reason, 3800);
        }
        return finalized;
    }

    private bool FinalizeJusticeVoluntaryPaymentIntent(
        JusticeVoluntaryFinePaymentIntent intent)
    {
        if (intent == null || !intent.DebtCommitted)
        {
            return false;
        }

        JusticeMarkStateDirty();
        if (!JusticeFlushStateNow())
        {
            return false;
        }

        _justiceVoluntaryFinePaymentIntent = null;
        _justiceNextVoluntaryPaymentResumeAt = 0;
        JusticeMarkStateDirty();
        if (!PersistJusticeCriticalPrecommitRedundantly())
        {
            _justiceVoluntaryFinePaymentIntent = intent;
            JusticeMarkStateDirty();
            return false;
        }

        if (intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
        {
            ShowStatus(
                "Justice : " + FormatJusticeMoney(intent.DebitAmount) +
                " payé · reste " + FormatJusticeMoney(_justiceCaseState.FineDue) + ".",
                4800);
            LogInfo(
                "Justice.Paiement",
                "Paiement volontaire confirmé id=" + intent.PaymentId +
                ", montant=" + intent.DebitAmount.ToString(CultureInfo.InvariantCulture) + ".");
        }
        else
        {
            ShowStatus("Justice : paiement annulé ou débit refusé, la dette reste inchangée.", 4200);
            LogWarning("Justice.Paiement", "Paiement volontaire annulé ou refusé sans débit validé.");
        }
        return true;
    }

    private void WriteJusticeVoluntaryFinePaymentIntentXml(XmlWriter writer)
    {
        JusticeVoluntaryFinePaymentIntent intent = _justiceVoluntaryFinePaymentIntent;
        if (writer == null || intent == null)
        {
            return;
        }

        writer.WriteStartElement("VoluntaryFinePaymentIntent");
        writer.WriteAttributeString("paymentId", intent.PaymentId ?? string.Empty);
        writer.WriteAttributeString("slot", intent.Slot.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fineBefore", intent.FineBefore.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("debitAmount", intent.DebitAmount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashBefore", intent.CashBefore.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashAfter", intent.CashAfter.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "preparedAtUtcTicks",
            Math.Max(0L, intent.PreparedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("debitAttempted", intent.DebitAttempted ? "true" : "false");
        writer.WriteAttributeString(
            "attemptedAtUtcTicks",
            Math.Max(0L, intent.AttemptedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashWriteResult", intent.CashWriteResult.ToString());
        writer.WriteAttributeString("debtCommitted", intent.DebtCommitted ? "true" : "false");
        writer.WriteEndElement();
    }

    private static JusticeVoluntaryFinePaymentIntent ParseJusticeVoluntaryFinePaymentIntentXmlPure(
        XmlElement element,
        JusticeCaseState caseState)
    {
        if (element == null || caseState == null)
        {
            return null;
        }

        string paymentId = (element.GetAttribute("paymentId") ?? string.Empty).Trim();
        int slot = -1;
        long fineBefore = -1L;
        int debitAmount = -1;
        int cashBefore = -1;
        int cashAfter = -1;
        long preparedAtUtcTicks = 0L;
        bool debitAttempted = false;
        long attemptedAtUtcTicks = 0L;
        JusticeCashWriteResult cashWriteResult = JusticeCashWriteResult.Unknown;
        bool debtCommitted = false;
        bool valid = IsCanonicalJusticeVoluntaryPaymentId(paymentId) &&
            TryReadJusticeIntStrict(element, "slot", -1, 0, 2, out slot) &&
            TryReadJusticeLongStrict(
                element,
                "fineBefore",
                -1L,
                1L,
                JusticePolicy.MaxActiveFine,
                out fineBefore) &&
            TryReadJusticeIntStrict(element, "debitAmount", -1, 1, int.MaxValue, out debitAmount) &&
            TryReadJusticeIntStrict(element, "cashBefore", -1, 0, int.MaxValue, out cashBefore) &&
            TryReadJusticeIntStrict(element, "cashAfter", -1, 0, int.MaxValue, out cashAfter) &&
            TryReadJusticeLongStrict(
                element,
                "preparedAtUtcTicks",
                0L,
                1L,
                DateTime.MaxValue.Ticks,
                out preparedAtUtcTicks) &&
            TryReadJusticeBoolStrict(element, "debitAttempted", false, out debitAttempted) &&
            TryReadJusticeLongStrict(
                element,
                "attemptedAtUtcTicks",
                0L,
                0L,
                DateTime.MaxValue.Ticks,
                out attemptedAtUtcTicks) &&
            TryReadJusticeCashWriteResult(element, out cashWriteResult) &&
            TryReadJusticeBoolStrict(element, "debtCommitted", false, out debtCommitted);

        valid &= debitAmount <= cashBefore && cashAfter == cashBefore - debitAmount &&
                 (!debitAttempted
                     ? attemptedAtUtcTicks == 0L &&
                       cashWriteResult == JusticeCashWriteResult.Unknown && !debtCommitted
                     : attemptedAtUtcTicks > 0L) &&
                 (!debtCommitted || cashWriteResult != JusticeCashWriteResult.Unknown);

        long expectedFine = fineBefore;
        if (debtCommitted && cashWriteResult == JusticeCashWriteResult.Succeeded)
        {
            expectedFine = Math.Max(0L, fineBefore - debitAmount);
        }
        valid &= caseState.FineDue == expectedFine;
        if (!valid)
        {
            return null;
        }

        return new JusticeVoluntaryFinePaymentIntent
        {
            PaymentId = paymentId,
            Slot = slot,
            FineBefore = fineBefore,
            DebitAmount = debitAmount,
            CashBefore = cashBefore,
            CashAfter = cashAfter,
            PreparedAtUtcTicks = preparedAtUtcTicks,
            DebitAttempted = debitAttempted,
            AttemptedAtUtcTicks = attemptedAtUtcTicks,
            CashWriteResult = cashWriteResult,
            DebtCommitted = debtCommitted
        };
    }

    private static bool IsCanonicalJusticeVoluntaryPaymentId(string paymentId)
    {
        const string prefix = "payment:";
        if (string.IsNullOrWhiteSpace(paymentId) ||
            paymentId.Length != prefix.Length + 32 ||
            !paymentId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        Guid parsed;
        return Guid.TryParseExact(paymentId.Substring(prefix.Length), "N", out parsed);
    }
}
