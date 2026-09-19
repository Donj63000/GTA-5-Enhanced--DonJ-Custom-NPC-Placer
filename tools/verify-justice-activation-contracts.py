#!/usr/bin/env python3
"""Vérification STRUCTURELLE du correctif ON/OFF (Python 3, bibliothèque standard).

Ce contrôle inspecte le C# : il ne compile ni n'exécute C#/MSTest/GTA.
Usage : python tools/verify-justice-activation-contracts.py [--root REPOSITORY]
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

# Conserver les positions tout en excluant commentaires, chaînes et caractères.
NON_CODE = re.compile(
    r'//[^\n]*|/\*[\s\S]*?\*/|(?:\$@|@\$|@)"(?:""|[^"])*"|'
    r'\$?"(?:\\[\s\S]|[^"\\])*"|\'(?:\\[\s\S]|[^\'\\])*\''
)


def executable(text: str) -> str:
    return NON_CODE.sub(lambda m: ''.join('\n' if c == '\n' else ' ' for c in m[0]), text)


def compact(text: str) -> str:
    return re.sub(r'\s+', '', text)


def block(text: str, start: int) -> str:
    opening = text.index('{', start)
    depth = 1
    for index in range(opening + 1, len(text)):
        depth += (text[index] == '{') - (text[index] == '}')
        if depth == 0:
            return text[opening + 1:index]
    raise ValueError('Bloc C# non fermé')


def method(text: str, name: str) -> str:
    pattern = r'\b(?:private|internal|public|protected)\s+(?:static\s+)?[\w<>?,]+\s+' + re.escape(name) + r'\s*\('
    match = re.search(pattern, text)
    if match is None:
        raise ValueError('Méthode absente : ' + name)
    return block(text, match.end())


def ordered(text: str, *parts: str) -> bool:
    text = compact(text)
    position = 0
    for part in parts:
        found = text.find(compact(part), position)
        if found < 0:
            return False
        position = found + len(compact(part))
    return True


def balanced(text: str) -> bool:
    stack: list[str] = []
    pairs = {')': '(', ']': '[', '}': '{'}
    for char in text:
        if char in '([{':
            stack.append(char)
        elif char in pairs and (not stack or stack.pop() != pairs[char]):
            return False
    return not stack


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    source = root / 'src' / 'DonJEnemySpawner'
    files = {
        'bridge': source / 'JusticeRecognition' / 'DonJJusticeRecognition.cs',
        'owner': source / 'DonJEnemySpawner.Justice.Recognition.cs',
        'justice': source / 'DonJEnemySpawner.Justice.cs',
        'custody': source / 'DonJEnemySpawner.Justice.Custody.cs',
        'menu': source / 'DonJEnemySpawner.MenuUi.cs',
        'profiles': source / 'DonJEnemySpawner.Justice.Profiles.cs',
    }
    try:
        code = {key: executable(path.read_text(encoding='utf-8-sig')) for key, path in files.items()}
    except (OSError, UnicodeError) as error:
        print('ERREUR :', error, file=sys.stderr)
        return 2

    checks: list[tuple[str, object]] = []
    def check(name, action):
        checks.append((name, action))
    def m(file, name):
        return method(code[file], name)
    def has(file, name, text):
        return compact(text) in compact(m(file, name))

    check('Délimiteurs C# équilibrés (six sources)', lambda: all(balanced(t) for t in code.values()))
    check('Démarrage sans sauvegarde conservé sur OFF', lambda: ordered(m('justice', 'InitializeJusticeSystem'),
        'bool loaded = TryLoadJusticeState(false);', 'if (!loaded)', '_justiceCaseState.Enabled = false;', '_justiceEnabled = false;'))
    check('Préférence toujours propre à chaque protagoniste', lambda: has('profiles', 'ActivateJusticePlayerProfile', '_justiceEnabled = _justiceCaseState.Enabled;'))
    check('Bridge fermé pour les états inconnus', lambda: ordered(m('bridge', 'IsRuntimeAllowedUnsafe'),
        '_desiredEnabled == true', '_desiredRuntimeSuspended == false', '_desiredActiveProfileId != null'))
    check('Callback refusé avant invocation en état OFF', lambda: ordered(m('bridge', 'ApplyWantedMinimumAtomically'),
        'if (!IsRuntimeAllowedUnsafe())', 'return new WantedMinimumApplicationResult(true, false);', 'handler('))
    for setter, assignment, queue in (
        ('SetEnabled', '_desiredEnabled = enabled;', '_instance.QueueSetEnabled(enabled);'),
        ('SetRuntimeSuspended', '_desiredRuntimeSuspended = suspended;', '_instance.QueueSetRuntimeSuspended(suspended);'),
        ('SetActiveProfile', '_desiredActiveProfileId = normalized;', '_instance.QueueSetActiveProfile(normalized);'),
    ):
        check('Publication sérialisée : ' + setter,
              lambda s=setter, a=assignment, q=queue: ordered(block(m('bridge', s), m('bridge', s).index('lock (SyncRoot)')), a, q))
    check('Snapshot profil/ON/suspension atomique', lambda: ordered(m('bridge', 'SetRuntimeState'),
        'lock (SyncRoot)', '_desiredEnabled = enabled;', '_desiredRuntimeSuspended = suspended;',
        '_desiredActiveProfileId = normalized;', '_instance.QueueRuntimeState('))
    check('Le contrôleur principal publie un snapshot unique', lambda: has('owner', 'SynchronizeJusticeRecognition',
        'JusticeRecognitionBridge.SetRuntimeState(') and not has('owner', 'SynchronizeJusticeRecognition', 'JusticeRecognitionBridge.SetEnabled('))
    check('Reset mémorisé sur OFF malgré un ON suivant', lambda: has('bridge', 'QueueSetEnabled', '_queuedRuntimeReset |= !enabled;'))
    check('Reset mémorisé sur suspension', lambda: has('bridge', 'QueueSetRuntimeSuspended', '_queuedRuntimeReset |= suspended;'))
    check('Reset consommé avant application du dernier état', lambda: ordered(m('bridge', 'DrainQueuedCommands'),
        'resetRuntime = _queuedRuntimeReset;', '_queuedRuntimeReset = false;', 'if (resetRuntime)',
        'ResetCurrentProfileRuntimeState();', 'ApplyEnabledState(enabledState, nowGameTime);'))
    check('Pas de jeton neuf avant consommation du snapshot', lambda: has('bridge', 'TryGetRuntimeEpoch', '!source.HasQueuedRuntimeState()'))
    check('Écriture liée à instance + profil + génération + boîte vide', lambda: all(
        has('bridge', 'ApplyWantedMinimumForProfileAtomically', token) for token in (
            'ReferenceEquals(_instance, source)', 'epoch != _runtimeEpoch', '!IsRuntimeAllowedUnsafe()',
            'source.HasQueuedRuntimeState()', 'string.Equals(_desiredActiveProfileId, profileId, StringComparison.Ordinal)')))
    check('Le chemin de production utilise le jeton du tick', lambda: has('bridge', 'ApplyWantedMinimum',
        'JusticeRecognitionBridge.ApplyWantedMinimumForProfileAtomically(targetWanted, this, _currentProfileId, _activeRuntimeEpoch)'))
    check('Le tick valide son droit avant de traiter le wanted', lambda: ordered(m('bridge', 'OnTick'),
        'DrainQueuedCommands(nowGameTime)', 'JusticeRecognitionBridge.TryGetRuntimeEpoch(', 'ProcessWantedState('))
    check('Chaque publication complète invalide le travail en vol', lambda: ordered(m('bridge', 'SetRuntimeState'),
        'lock (SyncRoot)', '_runtimeEpoch = unchecked(_runtimeEpoch + 1L);', '_instance.QueueRuntimeState('))
    check('Attach et detach invalident la génération', lambda: all(
        has('bridge', name, '_runtimeEpoch = unchecked(_runtimeEpoch + 1L);') for name in ('Attach', 'Detach')))
    check('Le callback principal contrôle ON et le dossier', lambda: ordered(m('owner', 'TryApplyJusticeRecognitionWantedMinimum'),
        '!_justiceEnabled', '!_justiceCaseState.Enabled', 'IsJusticeRecognitionTransitionBlocked()', 'return false;', 'SetJusticeWantedMinimum(level)'))
    check('Le callback principal contrôle profil et suspension GTA', lambda: all(
        has('owner', 'TryApplyJusticeRecognitionWantedMinimum', t) for t in (
            '!IsJusticeRuntimeProfileContextCompatible()', 'IsJusticeRuntimeSuspended(player)', 'IsJusticePlayerDeadSafe(player)')))
    check('Barrière Early après les reprises et avant nouvelles captures', lambda: ordered(m('justice', 'UpdateJusticeEarly'),
        'ResumeJusticeVoluntaryFinePayment();', 'if (!_justiceEnabled)', 'return;', 'ArmJusticeCapturePrecommitRetryIfRequired();'))
    check('OFF invalide les dégâts différés et les marqueurs wanted', lambda: all(
        has('justice', 'PauseJusticeRuntimeWithoutErasingCase', t) for t in (
            '_justiceDamageFrontCount = 0;', '_justiceSelfDefenseUntilByVictim.Clear();',
            '_justiceWrittenWantedLevel = 0;', '_justiceWrittenWantedExpiresAtMs = 0L;')))
    check('Le reset runtime conserve les indices persistants', lambda:
        not re.search(r'_currentProfile\s*\.', m('bridge', 'ResetCurrentProfileRuntimeState')) and
        all(has('bridge', 'ResetCurrentProfileRuntimeState', t) for t in (
            '_currentEpisode = null;', '_pendingWantedLoss = null;', '_pendingWantedEscalation = null;', '_observerExposures.Clear();')))
    check('Le toggle ne vide ni dossier ni wanted GTA', lambda: all(
        all(forbidden not in m('justice', name) for forbidden in ('ClearActiveJusticeCase', 'ClearJusticeWantedLevel(', 'ClearProfileRecognitionData('))
        for name in ('RequestJusticeToggle', 'PauseJusticeRuntimeWithoutErasingCase', 'PrepareJusticeRuntimeAfterResume')))
    check('Le toggle refuse toujours les pauses transactionnellement dangereuses', lambda: ordered(m('justice', 'RequestJusticeToggle'),
        'IsJusticePauseTemporarilyUnsafe()', '_justiceEnabled = targetEnabled;', 'SynchronizeJusticeRecognition(true);'))
    check('Le contrôleur détention refuse les seuls restes de restitution', lambda: ordered(m('custody', 'JusticeUpdateCustody'),
        '!HasJusticeCustodyControllerWork()', 'return;', 'EnforceJusticeCustodyWeaponLock(player)'))
    check('Les restitutions dédiées restent avant le contrôleur détention', lambda: ordered(m('justice', 'UpdateJusticeSystem'),
        'RetryJusticePoliceSuppressionRestore(', 'RetryJusticeDeferredInventoryRestore(',
        'RetryJusticeCustodyAppearanceRestore(', 'JusticeUpdateCustody('))
    check('La reprise de vraie détention reste indépendante de ON', lambda:
        has('custody', 'HasJusticeCustodyControllerWork', 'JusticeIsCustodyActive') and
        '_justiceEnabled' not in m('custody', 'HasJusticeCustodyControllerWork'))
    check('Placement/Terminator arrêtés seulement sous protection interdite', lambda: has('custody', 'JusticeUpdateCustody',
        'if (IsJusticeTemporaryPlayerProtectionForbidden() && !StopJusticeConcurrentPlayerProtectionModes())'))
    check('Cache de zone immédiatement fermé sur OFF/suspension', lambda: ordered(m('bridge', 'HasActiveSearchZone'),
        'IsRuntimeAllowedUnsafe()', '!instance.HasQueuedRuntimeState()', 'instance.HasActiveSearchZoneCached()'))
    for name in ('EnsureSearchZoneBlip', 'RecreateSearchZoneBlip'):
        check('Blip protégé : ' + name, lambda n=name: has('bridge', n, '_runtimeSuspended') and has('bridge', n, 'JusticeRecognitionBridge.TryGetRuntimeEpoch('))
    check('Module reconnaissance suspendu avant synchronisation initiale', lambda: 'privatebool_runtimeSuspended=true;' in compact(code['bridge']))

    failures = 0
    print('CONTROLES STRUCTURELS UNIQUEMENT — aucune compilation/exécution C#/GTA')
    for name, action in checks:
        try:
            passed = bool(action())
            detail = ''
        except (ValueError, KeyError, IndexError) as error:
            passed, detail = False, ' : ' + str(error)
        print(('OK   ' if passed else 'ECHEC') + ' ' + name + detail)
        failures += not passed
    print(f'{len(checks) - failures}/{len(checks)} contrôles réussis ; {failures} échec(s).')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
