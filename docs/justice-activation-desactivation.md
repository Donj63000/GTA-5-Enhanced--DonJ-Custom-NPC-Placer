# Correctif du cycle d’activation de la Justice avancée

**Projet :** GTA 5 Enhanced — DonJ Custom NPC Placer
**Date :** 19 septembre 2026
**Base analysée :** `GTA-5-Enhanced--DonJ-Custom-NPC-Placer-main (10).zip`
**Révision indiquée dans le ZIP :** `12eb7e5382d9a5e0fabd45703fc6b5a17a311451`
**SHA-256 du ZIP :** `4b4a65dee70ecd35ddb333257a07b5aa96a58a792660e0b424487ccd8c6e6e63`

## 1. Conclusion et niveau de validation

Le code présentait plusieurs chemins insuffisamment protégés entre le bouton ON/OFF, le script indépendant de reconnaissance policière et le contrôleur de détention. Le correctif ferme les demandes de wanted de la reconnaissance à la désactivation, empêche les tâches d’un ancien état de reprendre après un ON rapide et limite le contrôleur de détention aux opérations qui lui appartiennent réellement.

**La validation réalisée ici est une validation de sources et d’applicabilité du patch, pas une certification d’exécution.** Le système utilisé ne possède ni compilateur/runtime .NET, ni GTA, ni environnement ScriptHookVDotNet exécutable. La compilation, les tests MSTest et les essais en jeu n’ont donc pas été exécutés. Ils restent nécessaires avant de déclarer la correction validée en jeu.

Le dépôt reçoit 11 nouvelles méthodes de test MSTest, soit 23 cas avec les paramètres, ainsi qu’un contrôle structurel Python reproductible. Les scénarios C# sont fournis, pas annoncés comme réussis.

## 2. Ce qui n’est pas un défaut d’initialisation

Dans `DonJEnemySpawner.Justice.cs`, `InitializeJusticeSystem()` charge d’abord une sauvegarde exploitable. Sans sauvegarde, il impose déjà `_justiceCaseState.Enabled = false` et `_justiceEnabled = false`.

Dans `DonJEnemySpawner.Justice.Profiles.cs`, `ActivateJusticePlayerProfile()` reprend ensuite `_justiceEnabled = _justiceCaseState.Enabled`. La préférence est volontairement propre à chaque protagoniste. Un fichier sauvegardé sur ON ou le passage à un personnage dont le dossier est ON peut donc expliquer l’impression d’une activation par défaut.

Cette séparation existante n’a pas été remplacée par un interrupteur global. OFF pour Michael ne veut pas dire OFF pour Franklin et Trevor. Le message de désactivation et l’aide du menu précisent désormais le protagoniste concerné et la portée du bouton.

Autre distinction essentielle : **désactiver la Justice avancée n’est pas désactiver la police normale de GTA et n’est pas une amnistie**. Le correctif ne met pas arbitrairement les étoiles à zéro, ne supprime pas les infractions et ne vide pas les sauvegardes. Une poursuite native déjà présente peut continuer.

## 3. Défauts identifiés dans les sources

### 3.1. Le wanted pouvait être demandé sans revérifier ON/OFF

Dans `JusticeRecognition/DonJJusticeRecognition.cs`, `JusticeRecognitionBridge.ApplyWantedMinimumAtomically()` vérifiait la présence du callback et son résultat, mais pas l’autorisation courante de la reconnaissance. Le callback de `DonJEnemySpawner.Justice.Recognition.cs` appelait directement `SetJusticeWantedMinimum()`.

La garde `_enabled` du script de reconnaissance n’était évaluée qu’au début de son tick. Elle ne suffisait pas à protéger une demande différée lorsque le contrôleur principal avait entre-temps changé d’état.

**Correction :** contrôle de l’état désiré dans le bridge, puis seconde garde dans `TryApplyJusticeRecognitionWantedMinimum()` côté propriétaire du wanted. Cette dernière vérifie aussi le dossier, le profil, l’état du joueur et les transitions techniques. Le setter bas niveau n’est pas globalement neutralisé, car les restitutions et les transitions de détention peuvent encore en avoir besoin.

### 3.2. Publications non atomiques et commandes potentiellement dépassées

Les anciens setters copiaient l’instance sous `SyncRoot`, puis publiaient leur commande après avoir relâché ce verrou. L’ordre effectif des commandes pouvait donc diverger de celui de l’état désiré en cas d’entrelacement. Le contrôleur principal publiait séparément le profil, l’activation et la suspension.

**Correction :** publication de chaque setter sous le même verrou que la décision. Le chemin principal utilise désormais un seul `SetRuntimeState(enabled, suspended, profileId)`, relayé par une mise à jour unique de la boîte de commandes du script.

L’ordre des verrous est `SyncRoot` puis `_commandSync`. La consommation copie les commandes sous `_commandSync`, puis relâche ce verrou avant de traiter les commandes ou d’appeler le bridge. Aucune native n’a été ajoutée aux méthodes de publication des états.

### 3.3. Une bascule OFF → ON rapide pouvait faire disparaître le nettoyage OFF

La boîte de commandes ne retenait que le dernier booléen. Un OFF suivi d’un ON avant consommation pouvait n’appliquer que ON. Si le module était déjà localement ON, `ApplyEnabledState()` sortait immédiatement et les événements différés pouvaient survivre.

**Correction :** ajout d’un indicateur cumulatif `_queuedRuntimeReset`. OFF, une suspension ou une publication complète conservent l’obligation de nettoyer les données temporaires, même si la valeur finale redevient ON.

Un compteur de génération, `_runtimeEpoch`, invalide en plus le travail commencé sous une ancienne autorisation. Le chemin de production doit présenter simultanément la bonne instance, le bon profil et la génération du tick. Il ne peut ni obtenir ni utiliser une autorisation pendant qu’une nouvelle synchronisation reste à consommer. Ainsi, un ancien tick n’obtient pas accidentellement une autorisation neuve entre OFF et ON.

Le nettoyage porte sur les épisodes temporaires, les demandes wanted différées, les expositions des observateurs, le cache d’identité et les temporisateurs. Les indices persistants de reconnaissance restent conservés.

### 3.4. Restitution technique et détention étaient insuffisamment séparées

Le tick tardif laisse volontairement certaines restaurations fonctionner même sous OFF : police, inventaire, apparence et état du joueur ne doivent pas rester bloqués après une opération interrompue.

Cependant, `HasJusticeCustodyRecoveryState()` couvre aussi de simples reliquats de restitution. `JusticeUpdateCustody()` pouvait alors entrer dans son contrôleur et appeler `StopJusticeConcurrentPlayerProtectionModes()` avant de vérifier l’existence d’une vraie détention. Cela pouvait arrêter Placement ou Terminator hors d’une détention réelle, y compris dans un chemin de récupération sous OFF.

**Correction :** `HasJusticeCustodyControllerWork()` distingue les détentions et opérations transactionnelles appartenant à ce contrôleur. L’arrêt de Placement/Terminator est aussi conditionné par `IsJusticeTemporaryPlayerProtectionForbidden()`. Les trois méthodes de restitution dédiées restent dans le tick tardif, avant le contrôleur de détention.

### 3.5. Nettoyage incomplet des données temporaires du contrôleur principal

`PauseJusticeRuntimeWithoutErasingCase()` ne vidait pas le compteur des dégâts différés à consommer, certains jetons d’autodéfense et les marqueurs d’écriture wanted.

**Correction :** invalidation de ces données et des temporisateurs de scan, sans désallouer les buffers ni supprimer le dossier. Une sortie OFF explicite a aussi été ajoutée dans `UpdateJusticeEarly()`, après les reprises de persistance/profil/paiement mais avant le traitement gameplay des captures et transitions policières.

Le cache public de zone de recherche et les deux chemins de création de blip vérifient également l’autorisation effective. La fermeture du droit d’écriture wanted est synchrone avec la publication OFF ; le nettoyage visuel reste exécuté par le script sur son tick, et non depuis le bridge.

**Limite du diagnostic :** ces chemins et absences de garde sont constatés dans les sources. L’archive ne permet pas de reproduire la session décrite ni d’attribuer avec certitude son symptôme à un seul chemin, sans sauvegarde de cette session, logs et essai GTA.

## 4. Comportements intentionnellement conservés

- Le démarrage sans sauvegarde reste OFF ; une préférence sauvegardée reste restaurée par protagoniste.
- Les étoiles et la police natives ne sont pas annulées par le bouton. Le dossier, le casier, les amendes, les peines et le mandat ne sont pas effacés par la pause.
- Une demande OFF dangereuse pendant une capture, une détention ou une transaction protégée reste refusée avec le mécanisme existant. Le patch ne prétend pas désactiver une opération qu’il serait dangereux d’abandonner.
- Les restitutions appartenant à une opération déjà engagée conservent leurs chemins de reprise. Les commandes durables de capture et de reset du module de reconnaissance ne sont pas supprimées de sa boîte de traitement.
- Un échec de sauvegarde ne réactive pas silencieusement la Justice dans la session : la pause reste effective et le writer garde une nouvelle tentative à effectuer. La survie du choix après redémarrage exige évidemment qu’une écriture finisse par réussir.

## 5. Vérifications effectuées et tests fournis

| Vérification | Résultat / portée |
|---|---|
| Lecture des chemins ON/OFF, profils, bridge, tick reconnaissance, détention et persistance | Effectuée sur l’archive fournie |
| Contrats de sources Python | **34/34 réussis sur les sources corrigées** |
| Même contrôle sur la base non corrigée | 7/34 réussis ; 27 échecs attendus, notamment méthodes/gardes absentes. Ce n’est pas un taux d’échec de tests runtime. |
| Délimiteurs C# des huit fichiers modifiés | Contrôle lexical réussi ; ne prouve pas la compilation |
| Noms des nouveaux champs référencés par réflexion dans les tests | Présence vérifiée dans les sources |
| `git diff --check` | Réussi |
| Application et inversion sur une copie propre de la base | Contrôlées lors de l’export du patch |
| Compilation C# et exécution MSTest | **Non exécutées dans cet environnement** |
| Exécution GTA / ScriptHookVDotNet | **Non exécutée dans cet environnement** |

Les nouvelles méthodes de `JusticeRecognitionRuntimeTests.cs` couvrent notamment : état inconnu, OFF avant consommation de la commande, suspension, profil invalide, ON/OFF rapide, génération périmée, instance détachée, snapshot complet et cache de zone. Le harnais publie désormais une autorité cohérente avec celle du vrai contrôleur, sans consommer prématurément les commandes durables préparées par les tests.

Les ajouts de `JusticeVoluntaryPaymentTests.cs` couvrent la seconde garde côté Justice, neuf transitions bloquantes et la séparation entre restitution seule et vraie détention. Le test existant de désactivation avec échec disque vérifie aussi les nouveaux nettoyages. Le test du résultat booléen du bridge reste conservé et configure explicitement ON pour tester son chemin autorisé.

### Reproduire le contrôle structurel

Depuis la racine du projet, avec Python 3 :

```powershell
python .\tools\verify-justice-activation-contracts.py
```

Ce script n’a aucune dépendance Python externe. Il inspecte le code hors commentaires et littéraux ; il ne remplace pas un compilateur, un analyseur sémantique ou les tests ci-dessous.

### Exécuter les tests C# sous Windows

Le dépôt possède déjà sa chaîne de vérification. Avec ses prérequis .NET/Framework et PowerShell installés :

```powershell
.\tools\run-safety-checks.ps1 -Ci -UseStubApi
```

Le mode stub simule l’API pour les tests. **Ne pas installer dans GTA une sortie de test compilée avec le stub.** Recompiler ensuite contre les véritables bibliothèques du dossier GTA :

```powershell
dotnet build .\src\DonJEnemySpawner\DonJEnemySpawner.csproj -t:Rebuild -c Release /p:GtaRoot="D:\Jeux\Grand Theft Auto V Enhanced" /p:DeployToGta=false
```

Adapter le chemin au dossier contenant réellement l’exécutable et l’API v2. Le projet produit sa DLL et son fichier `.ENdll` dans `src\DonJEnemySpawner\bin\Release`. Utiliser ensuite le circuit habituel de packaging/déploiement du dépôt, sans laisser deux copies du même script chargées.

## 6. Application du patch

Garder une copie des sources modifiées localement, du binaire installé et des sauvegardes Justice avant remplacement. Placer `justice_activation_desactivation.patch` à la racine du projet correspondant à l’archive analysée.

```powershell
git apply --check .\justice_activation_desactivation.patch
git apply .\justice_activation_desactivation.patch
```

Ne pas forcer l’application si le premier contrôle échoue : le patch cible cette archive, pas une version ultérieure arbitraire. Recompiler après application ; modifier le C# ne remplace pas automatiquement le binaire déjà installé.

Retour arrière du code, tant que les mêmes zones n’ont pas été modifiées à nouveau :

```powershell
git apply --reverse --check .\justice_activation_desactivation.patch
git apply --reverse .\justice_activation_desactivation.patch
```

Le retour arrière des sources ne remplace pas le binaire et ne touche pas aux sauvegardes.

## 7. Recette en jeu à terminer

| Scénario | Résultat attendu |
|---|---|
| Démarrage dans un environnement de sauvegarde de test vierge | Justice OFF pour le protagoniste, reconnaissance non autorisée avant synchronisation |
| Chargement d’un dossier précédemment sauvegardé ON | ON restauré ; comportement volontaire et inchangé |
| OFF en jeu libre avec une ancienne zone de recherche | Plus de nouvelle écriture wanted par la reconnaissance ; blip retiré sur le tick de traitement ; indices persistants conservés |
| OFF alors que GTA possède déjà des étoiles | Les étoiles natives ne sont pas effacées artificiellement ; la poursuite GTA peut continuer |
| Alternances rapides ON/OFF, terminées par OFF | Pas de retour tardif à ON, pas de reprise d’une ancienne demande différée de reconnaissance |
| Michael OFF, Franklin sauvegardé ON, changement de personnage | Franklin ON ; retour sur Michael OFF ; aucun dossier attribué au mauvais personnage |
| Nouvelle infraction native sous OFF | GTA peut donner ses étoiles normales, sans ajout d’une nouvelle sanction par la Justice avancée du protagoniste désactivé |
| Pas de détention, simple restitution police en cours, Placement/Terminator actif | La restitution n’arrête plus ces modes via le contrôleur de détention |
| Tentative OFF pendant une capture/détention ou une transaction dangereuse | Refus explicite existant conservé ; aucun abandon d’inventaire, de contrôles ou de transaction |
| OFF, sauvegarde réussie, arrêt et redémarrage du jeu | OFF retrouvé pour le même protagoniste, dossier conservé |
| Déchargement/rechargement des scripts | Ancienne instance et anciens droits de tick inutilisables ; reprise à partir de la nouvelle synchronisation |

Pour le diagnostic en jeu, distinguer une poursuite GTA normale d’un effet du mod : hausse wanted demandée par la reconnaissance, zone bleue, ajout de charge au dossier, notification de sanction ou transfert en cellule. Conserver les logs du mod et préciser le protagoniste, la présence d’une sauvegarde ON et le scénario exact si un comportement persiste.

## 8. Qualification locale du 19 septembre 2026

Les limites de compilation mentionnées plus haut décrivent l'environnement de
création du patch. Son intégration sur le poste Windows a ensuite été vérifiée :

- 34/34 contrôles structurels Python réussis ;
- compilation Release avec l'API NIB réelle et validation ABI réussies ;
- 712/712 tests avec l'API réelle (rapport `TestResults/safety-20260919-034202/safety-tests.trx`) ;
- 973/973 tests avec l'API simulée et suite de sécurité réussie (rapport `TestResults/safety-20260919-181450/summary.txt`).

Deux anciennes fixtures ont été adaptées : les assertions des textes OFF et
d'aide suivent maintenant les textes du patch, et le test du mandat initialise
puis restaure l'autorité du bridge. Le code de production fourni est conservé.
Le package local marqué `sourceDirty=true` a été refusé au déploiement comme
prévu. La livraison exige une reconstruction propre contre l'API réelle après
commit. Les essais manuels en jeu de la section 7 restent à effectuer.
