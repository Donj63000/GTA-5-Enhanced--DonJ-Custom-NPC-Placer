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
        internal long FineInDisputeBefore;
        internal long PreparedAtUtcTicks;
        internal bool DebitAttempted;
        internal long AttemptedAtUtcTicks;
        internal JusticeCashWriteResult CashWriteResult = JusticeCashWriteResult.Unknown;
        internal JusticePaymentResolution Resolution =
            JusticePaymentResolution.Prepared;
        internal long AmbiguousAmount;
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
            FineInDisputeBefore = Math.Max(0L, _justiceCaseState.FineInDispute),
            PreparedAtUtcTicks = DateTime.UtcNow.Ticks
        };
        JusticeMarkStateDirty();

        // Je rends d'abord le snapshot Prepared durable. Le petit WAL financier
        // ne sera armé qu'à la reprise, immédiatement avant l'unique SET.
        if (!EnsureJusticeFinancialPreparedSnapshot("VoluntaryFinePayment"))
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
            if (!intent.DebitAttempted &&
                intent.Resolution == JusticePaymentResolution.Rejected &&
                !PersistJusticeFinancialOutcomeWithoutEffect(
                    "VoluntaryFinePayment"))
            {
                return false;
            }
            return FinalizeJusticeVoluntaryPaymentIntent(intent);
        }

        // Je ne consulte le cash qu'après confirmation disque du snapshot Prepared.
        JusticeMarkStateDirty();
        if (!EnsureJusticeFinancialPreparedSnapshot("VoluntaryFinePayment"))
        {
            ShowStatus(
                "Paiement en attente : sauvegarde Justice indisponible, aucun débit effectué.",
                4200);
            return false;
        }

        if (!intent.DebitAttempted &&
            intent.Resolution == JusticePaymentResolution.Rejected)
        {
            intent.DebtCommitted = true;
            JusticeMarkStateDirty();
            if (!PersistJusticeFinancialOutcomeWithoutEffect(
                    "VoluntaryFinePayment"))
            {
                return false;
            }
            return FinalizeJusticeVoluntaryPaymentIntent(intent);
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

            bool attemptWasAlreadyDurable;
            if (!TryArmJusticeFinancialAttempt(
                    "VoluntaryFinePayment",
                    out attemptWasAlreadyDurable))
            {
                return false;
            }

            intent.DebitAttempted = true;
            intent.AttemptedAtUtcTicks = DateTime.UtcNow.Ticks;
            intent.CashWriteResult = JusticeCashWriteResult.Unknown;
            intent.Resolution = JusticePaymentResolution.Attempted;
            JusticeMarkStateDirty();

            if (!attemptWasAlreadyDurable)
            {
                intent.CashWriteResult = TryWriteJusticeSinglePlayerCash(
                    intent.Slot,
                    intent.CashAfter);
            }
            if (intent.CashWriteResult == JusticeCashWriteResult.Succeeded)
            {
                intent.Resolution = JusticePaymentResolution.Confirmed;
            }
            else if (intent.CashWriteResult == JusticeCashWriteResult.Rejected)
            {
                intent.Resolution = JusticePaymentResolution.Rejected;
            }
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
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

                // Je ne rejoue jamais une écriture déjà tentée. Sans preuve du
                // solde final, le montant quitte la dette exigible mais reste
                // explicitement litigieux dans le dossier.
                intent.Resolution = JusticePaymentResolution.Ambiguous;
                intent.AmbiguousAmount = intent.DebitAmount;
                LogWarning(
                    "Justice.Paiement",
                    "Réconciliation expirée sans lecture cash; paiement volontaire marqué ambigu.");
            }
            else if (observedCash == intent.CashAfter)
            {
                intent.CashWriteResult = JusticeCashWriteResult.Succeeded;
                intent.Resolution = JusticePaymentResolution.Confirmed;
            }
            else if (observedCash == intent.CashBefore)
            {
                intent.CashWriteResult = JusticeCashWriteResult.Rejected;
                intent.Resolution = JusticePaymentResolution.Rejected;
            }
            else
            {
                // Une écriture tentée n'est jamais rejouée. Un troisième solde
                // ne prouve cependant pas que notre STAT_SET_INT a réussi.
                intent.Resolution = JusticePaymentResolution.Ambiguous;
                intent.AmbiguousAmount = intent.DebitAmount;
                LogWarning(
                    "Justice.Paiement",
                    "Solde ambigu après STAT_SET_INT; montant isolé sans nouvelle écriture.");
            }
            JusticeMarkStateDirty();
            if (!JusticeFlushStateNow())
            {
                return false;
            }
        }

        if (intent.Resolution == JusticePaymentResolution.Confirmed)
        {
            _justiceCaseState.VoluntaryFinePaid = JusticePolicy.SaturatingAdd(
                _justiceCaseState.VoluntaryFinePaid,
                intent.DebitAmount,
                JusticePolicy.MaxActiveFine);
            _justiceCaseState.FineDue = Math.Max(
                0L,
                _justiceCaseState.FineDue - intent.DebitAmount);
        }
        else if (intent.Resolution == JusticePaymentResolution.Ambiguous)
        {
            intent.AmbiguousAmount = JusticePolicy.MoveFineToDispute(
                _justiceCaseState,
                intent.AmbiguousAmount);
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

        // Je transforme l'abandon en décision terminale sans fabriquer de tentative
        // cash. Un éventuel WAL Prepared est rejeté après le snapshot de résultat.
        intent.DebitAttempted = false;
        intent.AttemptedAtUtcTicks = 0L;
        intent.CashWriteResult = JusticeCashWriteResult.Rejected;
        intent.Resolution = JusticePaymentResolution.Rejected;
        intent.DebtCommitted = true;
        JusticeMarkStateDirty();
        if (!PersistJusticeFinancialOutcomeWithoutEffect(
                "VoluntaryFinePayment"))
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
        if (!JusticeFlushStateNow())
        {
            _justiceVoluntaryFinePaymentIntent = intent;
            JusticeMarkStateDirty();
            return false;
        }

        if (intent.Resolution == JusticePaymentResolution.Confirmed)
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
        else if (intent.Resolution == JusticePaymentResolution.Ambiguous)
        {
            ShowStatus(
                "Justice : " + FormatJusticeMoney(intent.AmbiguousAmount) +
                " en litige · aucun nouveau débit automatique.",
                5200);
            LogWarning(
                "Justice.Paiement",
                "Paiement volontaire ambigu id=" + intent.PaymentId +
                ", montant=" + intent.AmbiguousAmount.ToString(
                    CultureInfo.InvariantCulture) + ".");
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
            "fineInDisputeBefore",
            Math.Max(0L, intent.FineInDisputeBefore).ToString(
                CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "preparedAtUtcTicks",
            Math.Max(0L, intent.PreparedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("debitAttempted", intent.DebitAttempted ? "true" : "false");
        writer.WriteAttributeString(
            "attemptedAtUtcTicks",
            Math.Max(0L, intent.AttemptedAtUtcTicks).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("cashWriteResult", intent.CashWriteResult.ToString());
        writer.WriteAttributeString("resolution", intent.Resolution.ToString());
        writer.WriteAttributeString(
            "ambiguousAmount",
            Math.Max(0L, intent.AmbiguousAmount).ToString(
                CultureInfo.InvariantCulture));
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
        long fineInDisputeBefore = 0L;
        long preparedAtUtcTicks = 0L;
        bool debitAttempted = false;
        long attemptedAtUtcTicks = 0L;
        JusticeCashWriteResult cashWriteResult = JusticeCashWriteResult.Unknown;
        JusticePaymentResolution resolution = JusticePaymentResolution.Prepared;
        long ambiguousAmount = 0L;
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
                "fineInDisputeBefore",
                0L,
                0L,
                JusticePolicy.MaxActiveFine,
                out fineInDisputeBefore) &&
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
            TryReadJusticePaymentResolution(
                element,
                debitAttempted,
                cashWriteResult,
                out resolution) &&
            TryReadJusticeLongStrict(
                element,
                "ambiguousAmount",
                0L,
                0L,
                JusticePolicy.MaxActiveFine,
                out ambiguousAmount) &&
            TryReadJusticeBoolStrict(element, "debtCommitted", false, out debtCommitted);

        valid &= debitAmount <= cashBefore && cashAfter == cashBefore - debitAmount &&
                 (!debitAttempted
                     ? attemptedAtUtcTicks == 0L &&
                       cashWriteResult == JusticeCashWriteResult.Unknown && !debtCommitted
                     : attemptedAtUtcTicks > 0L) &&
                 (!debtCommitted ||
                  resolution == JusticePaymentResolution.Confirmed ||
                  resolution == JusticePaymentResolution.Rejected ||
                  resolution == JusticePaymentResolution.Ambiguous) &&
                 (resolution != JusticePaymentResolution.Ambiguous ||
                  (cashWriteResult == JusticeCashWriteResult.Unknown &&
                   ambiguousAmount == debitAmount)) &&
                 (resolution == JusticePaymentResolution.Ambiguous ||
                  ambiguousAmount == 0L);

        long expectedFine = fineBefore;
        long expectedDispute = fineInDisputeBefore;
        if (debtCommitted &&
            (resolution == JusticePaymentResolution.Confirmed ||
             resolution == JusticePaymentResolution.Ambiguous))
        {
            expectedFine = Math.Max(0L, fineBefore - debitAmount);
        }
        if (debtCommitted && resolution == JusticePaymentResolution.Ambiguous)
        {
            expectedDispute = JusticePolicy.SaturatingAdd(
                fineInDisputeBefore,
                ambiguousAmount,
                JusticePolicy.MaxActiveFine);
        }
        valid &= caseState.FineDue == expectedFine &&
                 caseState.FineInDispute == expectedDispute;
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
            FineInDisputeBefore = fineInDisputeBefore,
            PreparedAtUtcTicks = preparedAtUtcTicks,
            DebitAttempted = debitAttempted,
            AttemptedAtUtcTicks = attemptedAtUtcTicks,
            CashWriteResult = cashWriteResult,
            Resolution = resolution,
            AmbiguousAmount = ambiguousAmount,
            DebtCommitted = debtCommitted
        };
    }

    private static bool TryReadJusticePaymentResolution(
        XmlElement element,
        bool debitAttempted,
        JusticeCashWriteResult cashWriteResult,
        out JusticePaymentResolution resolution)
    {
        resolution = JusticePaymentResolution.Prepared;
        string text = (element.GetAttribute("resolution") ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            // Le lecteur v1 conserve les transactions historiques : leur ancien
            // résultat explicite est migré sans inventer un litige rétroactif.
            resolution = !debitAttempted
                ? JusticePaymentResolution.Prepared
                : cashWriteResult == JusticeCashWriteResult.Succeeded
                    ? JusticePaymentResolution.Confirmed
                    : cashWriteResult == JusticeCashWriteResult.Rejected
                        ? JusticePaymentResolution.Rejected
                        : JusticePaymentResolution.Attempted;
            return true;
        }

        return Enum.TryParse(text, true, out resolution) &&
               Enum.IsDefined(typeof(JusticePaymentResolution), resolution);
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

    private string GetJusticeSelectedFineDisputeDisplay()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return state == null || state.FineInDispute <= 0L
            ? "Aucun"
            : FormatJusticeMoney(state.FineInDispute) + " · résolution requise";
    }

    private bool CanJusticeResolveSelectedFineDispute()
    {
        JusticeCaseState state = GetJusticeMenuSelectedCaseState();
        return state != null && state.FineInDispute > 0L;
    }

    private void ResolveJusticeFineDisputeInPlayerFavor(
        int profileSlot,
        long confirmedAmount)
    {
        if (!IsJusticeCanonicalProfileSlot(profileSlot))
        {
            ShowStatus("Justice : profil du litige invalide.", 3500);
            return;
        }
        EnsureJusticePlayerProfilesInitialized();
        JusticeCaseState state = _justicePlayerProfiles[profileSlot].CaseState;
        long currentDispute = state == null ? 0L : Math.Max(0L, state.FineInDispute);
        if (state == null || currentDispute <= 0L ||
            confirmedAmount <= 0L || confirmedAmount != currentDispute)
        {
            ShowStatus("Justice : le litige a changé, confirmation annulée.", 4200);
            return;
        }
        if (profileSlot == _justiceActivePlayerProfileSlot &&
            (_justiceFineDebitIntent != null ||
             _justiceVoluntaryFinePaymentIntent != null))
        {
            ShowStatus("Justice : une transaction de paiement est encore ouverte.", 4200);
            return;
        }

        // Je matérialise ici la politique explicitement choisie « favoriser le
        // joueur ». Le montant quitte le litige sans nouveau débit ; il est
        // comptabilisé comme réglé uniquement pour empêcher sa renaissance.
        state.FineInDispute = 0L;
        state.VoluntaryFinePaid = JusticePolicy.SaturatingAdd(
            state.VoluntaryFinePaid,
            currentDispute,
            JusticePolicy.MaxActiveFine);
        JusticePolicy.NormalizeFineLedger(state);
        state.RecalculateTotals();
        JusticeMarkStateDirty();
        bool persisted = JusticeFlushStateNow();
        LogWarning(
            "Justice.Paiement.Litige",
            "Résolution explicite en faveur du joueur; profil=" +
            profileSlot.ToString(CultureInfo.InvariantCulture) +
            ", montant=" + currentDispute.ToString(CultureInfo.InvariantCulture) +
            ", persistance=" + (persisted ? "confirmée" : "en attente") + ".");
        ShowStatus(
            persisted
                ? "Justice : litige annulé explicitement, aucun nouveau débit."
                : "Justice : litige résolu en mémoire, sauvegarde à retenter.",
            5500);
    }
}
