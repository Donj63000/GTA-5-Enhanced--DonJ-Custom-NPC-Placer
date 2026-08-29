# Documentation développeur - DonJ Custom NPC Placer / GTA V Enhanced

## 1. Objectif du projet

Le projet est un mod solo pour GTA V Enhanced sur Windows x64.

Le mod s'appelle :

DonJ Custom NPC Placer

Le fichier livré au jeu s'appelle :

DonJCustomNpcPlacer.ENdll

Son but est de permettre au joueur de créer des scènes personnalisées en mode histoire :

- placement de PNJ ;
- placement de véhicules ;
- placement d'objets ;
- placement d'entrées/sorties d'intérieurs ;
- sauvegarde et chargement XML ;
- respawn automatique ;
- gardes alliés ;
- patrouilles ;
- appels téléphoniques Cartel ;
- attaques Ballas ;
- escorte haute sécurité avec limousine blindée ;
- système optionnel Justice avancée : preuves, casier, mandats, amendes et détention ;
- gestion d'objets interactifs comme argent, soin, armure, munitions ;
- debug/logs ;
- tests de non-régression.

Le projet ne doit jamais être pensé pour GTA Online. Il est fait pour le mode histoire uniquement.

## 2. Contexte technique exact

Configuration cible actuelle :

Plateforme : Windows x64
Jeu : Grand Theft Auto V Enhanced version Steam
Exécutable : GTA5_Enhanced.exe
Version jeu sur le poste : 1.0.1158.13
Dossier jeu :
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced

Loader / runtime côté jeu :

ScriptHookV.dll 3889.0.1158.13
xinput1_4.dll 1.0.0.2 (chargeur Enhanced)
NIBScriptHookVDotNet.asi
NIBScriptHookVDotNet2.dll 2.11.6

Point critique :

Le projet cible l'API v2 via NIBScriptHookVDotNet2.dll.
Ne pas coder avec l'API ScriptHookVDotNet v3.
Ne pas supposer que ScriptHookVDotNet2.dll classique est présent.

Dossier scripts chargé par le jeu :

C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts

Fichier attendu dans Scripts :

DonJCustomNpcPlacer.ENdll

Fichier optionnel mais utile pour debug :

DonJCustomNpcPlacer.pdb

Mods déjà présents à connaître :

Menyoo.asi
NativeTrainer.asi
pc_trainer.asi
OpenRPF.asi
DirectStorageFix.asi
NIBMods.net.ENdll
IronmanV3EG.ENdll
Superman V2.ENdll
DonJEnemySpawner.ENdll

Logs utiles :

C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\ScriptHookV.log
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\menyooLog.txt
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\*.log

Stack build locale :

C#
.NET Framework 4.8
.NET SDK 9.0.312
MSBuild 17.14

## 3. Structure du dépôt

Structure importante :

GTA5modDEV.sln

AGENTS.md
README.md
LICENSE
crash-list.md

Mode-pour-jeu-ici\
  INSTALLATION_SIMPLE.txt

Le dépôt ne conserve plus de binaire de release dans ce dossier. Le livrable
canonique est le package `game-ready` généré depuis la build effectivement testée.

src\
  DonJEnemySpawner\
    DonJEnemySpawner.cs
    DonJEnemySpawner.MenuUi.cs
    DonJEnemySpawner.Justice.Domain.cs
    DonJEnemySpawner.Justice.Profiles.cs
    DonJEnemySpawner.Justice.Payment.cs
    DonJEnemySpawner.Justice.Persistence.Model.cs
    DonJEnemySpawner.Justice.Persistence.Codec.cs
    DonJEnemySpawner.Justice.Persistence.Migration.cs
    DonJEnemySpawner.Justice.Persistence.Runtime.cs
    DonJEnemySpawner.Justice.Repository.cs
    DonJEnemySpawner.Justice.Wal.cs
    DonJEnemySpawner.Justice.WorldSnapshot.cs
    DonJEnemySpawner.Justice.Diagnostics.cs
    DonJEnemySpawner.Justice.cs
    DonJEnemySpawner.Justice.Custody.cs
    DonJEnemySpawner.RuntimeSafety.cs
    DonJEnemySpawner.HighSecurityEscort.cs
    DonJEnemySpawner.Interiors.cs
    DonJEnemySpawner.Interiors.AdvancedLoading.cs
    DonJEnemySpawner.InteriorCatalog.cs
    DonJEnemySpawner.Logging.cs
    DonJEnemySpawner.csproj

tests\
  DonJEnemySpawner.Tests\
    DonJEnemySpawnerTests.cs
    SafetySimulationTests.cs
    JusticeDomainTests.cs
    JusticePlayerProfilePersistenceTests.cs
    JusticeRuntimeContractTests.cs
    JusticeRuntimeEdgeContractTests.cs
    JusticeCustodyHardeningTests.cs
    JusticeEnginePersistenceRegressionTests.cs
    JusticeStateRepairTests.cs
    JusticeUiIntegrationObservabilityTests.cs
    JusticeAuditRemediationTests.cs
    JusticeRepositoryTests.cs
    JusticeWalRecoveryTests.cs
    PackagingSafetyTests.cs
    RuntimeStageIsolationTests.cs
    StubRuntimeBehaviorTests.cs
    BugLogCollectionTests.cs
    DonJEnemySpawner.Tests.csproj

tools\
  run-safety-checks.ps1
  package-game-ready.ps1
  deploy-game-ready.ps1
  collect-bug-logs.ps1
  repair-justice-state.ps1
  Stubs\NIBScriptHookVDotNet2\StubApi.cs

Rôle des fichiers source :

DonJEnemySpawner.cs

C'est le cœur du mod. Il contient :

- la classe principale DonJEnemySpawner : Script ;
- l'état, la navigation et les actions gameplay du menu F10 ;
- la logique de placement ;
- les modèles PNJ/véhicules/objets ;
- les armes ;
- les comportements PNJ ;
- les relations ;
- les blips ;
- les sauvegardes XML ;
- le Cartel ;
- les Ballas ;
- les objets interactifs ;
- une grande partie du runtime principal.

DonJEnemySpawner.MenuUi.cs

Contient la présentation de la console F10 Obsidienne :

- calcul du viewport responsive et de la safe-zone ;
- rail des catégories, panneau d'actions et panneau de contexte ;
- thème, primitives graphiques, monogramme et icônes dessinés en code ;
- animations d'ouverture et de sélection ;
- pool des objets UI et modèle de page mis en cache ;
- safe-zone cadencée à 250 ms avec coupe-circuit, et cache du casier invalidé par sa révision ;
- ligne de détention Justice discrète, seul HUD Justice persistant hors F10 ;
- rendu cohérent de l'atelier d'armes.

Ce fichier ne doit pas muter directement les entités du monde. Les actions gameplay restent traitées dans `DonJEnemySpawner.cs`.

DonJEnemySpawner.Justice.Domain.cs

Contient le domaine déterministe Justice sans dépendance GTA : catalogue des infractions, preuves, circonstances, sanctions, dossier actif, casier, récidive, déduplication et machine d'états. Les types restent `internal` et sont exposés uniquement au projet de tests par `InternalsVisibleTo`.

DonJEnemySpawner.Justice.Profiles.cs

Contient les trois profils indépendants Michael, Franklin et Trevor, leur activation selon le slot canonique, leur persistance XML, le sélecteur F10 et la réinitialisation protégée du profil choisi.

DonJEnemySpawner.Justice.Payment.cs

Contient le paiement volontaire et son intention durable reprise de façon idempotente. Le rendu F10 n'écrit jamais directement dans l'argent du protagoniste. Une écriture impossible à prouver devient `Ambiguous` et son montant est isolé dans `FineInDispute` au lieu d'être présenté comme payé.

DonJEnemySpawner.Justice.Persistence.Model.cs, Codec.cs, Migration.cs, Runtime.cs, Repository.cs et Wal.cs

Définissent la frontière immuable de persistance, les DTO métier typés, le schéma XML 2.0 et ses hashes, l'adaptateur de migration v1, le raccordement runtime, le writer `latest-wins`, ses diagnostics de révision et le WAL borné des effets critiques. Aucun objet GTA ne franchit cette frontière et le thread de persistance ne doit appeler aucune native.

DonJEnemySpawner.Justice.WorldSnapshot.cs

Contient le snapshot spatial partagé d'une passe Justice, les filtres par distance au carré et les accumulateurs moyenne/p95/p99/maximum. Une passe réalise au plus une requête peds et une requête véhicules puis réutilise ces tableaux.

DonJEnemySpawner.Justice.Diagnostics.cs

Construit la ligne de diagnostic F10 et le rapport journalisé : build ID, SHA-256 de l'assembly réellement chargé, comparaison au manifest installé, schéma, phase, slot, états inventaire/paiement/police, WAL, révisions mémoire/disque, dernière sauvegarde, métriques et compteurs du snapshot monde. La lecture du manifest est déclenchée à la demande et non à chaque frame.

DonJEnemySpawner.Justice.cs

Contient le pont runtime Justice : détection événementielle, témoins bornés et priorisés, résolution des incidents en deux phases, lecture/corrélation du wanted GTA, mandats, reconnaissance, causalité des alliés DonJ, migration/persistance Justice, notifications GTA natives et intégration au tick. Les natives scalaires sont cadencées, les fronts observés pendant une réparation sont associés au slot et au modèle du protagoniste, et les scans du monde passent par le snapshot partagé. Une infraction ordinaire ne produit aucune écriture wanted Justice : GTA reste seul responsable des étoiles liées aux crimes.

DonJEnemySpawner.Justice.Custody.cs

Contient la détention Justice : transfert vers Mission Row ou Bolingbroke, identité canonique du protagoniste, transactions cash explicites, machine d'état d'inventaire, intégration police configurable, volumes autorisés, activités, gardes privés non létaux, discipline, évasion, libération et reprise après chargement. Il encapsule aussi le tampon unmanaged réutilisable de 312 octets nécessaire à `GET_DLC_WEAPON_DATA`.

DonJEnemySpawner.RuntimeSafety.cs

Isole les étapes du tick et de l'arrêt, applique un cooldown de log par domaine et fournit la maintenance Justice de sécurité lorsque `JusticeEarly` échoue. Une panne Cartel, UI ou Terminator ne doit plus annuler les domaines suivants du même tick.

DonJEnemySpawner.HighSecurityEscort.cs

Contient toute la partie :

- limousine blindée ;
- convoi VIP ;
- appel avec L ;
- gardes haute sécurité ;
- formation véhicules ;
- trajet waypoint ;
- combat de convoi ;
- IA conducteur ;
- déblocage véhicules ;
- entrée du joueur dans la limousine ;
- nettoyage/retrait du convoi.

Quand on travaille sur la limousine, on modifie principalement ce fichier.

DonJEnemySpawner.Interiors.cs

Contient :

- portails d'entrée ;
- portails de sortie ;
- session intérieure active ;
- téléportation entrée/sortie ;
- sauvegarde des portails.

DonJEnemySpawner.Interiors.AdvancedLoading.cs

Contient :

- chargement avancé IPL/intérieurs ;
- pin interior ;
- focus zone ;
- HD area ;
- room forcing ;
- entity sets ;
- stabilisation caméra/viewport.

DonJEnemySpawner.InteriorCatalog.cs

Contient :

- catalogue des intérieurs disponibles ;
- noms affichés ;
- coordonnées ;
- configurations d'entités intérieures.

DonJEnemySpawner.Logging.cs

Contient :

- logger runtime ;
- écriture dans DonJCustomNpcPlacer.log ;
- emplacements stables `Scripts` puis LocalAppData avant tout fallback de shadow-copy ;
- sanitation des noms de fichiers ;
- protections pour ne jamais crasher à cause du logger.

## 4. Build et déploiement

Le projet principal cible :

net48

La sortie assembly est :

DonJCustomNpcPlacer.dll

Mais le livrable réellement chargé par NIB est :

DonJCustomNpcPlacer.ENdll

Commande build normale :

dotnet build GTA5modDEV.sln -c Release

Commande test normale :

dotnet test GTA5modDEV.sln -c Release

Commande de déploiement explicite vers un dossier GTA validé :

dotnet build GTA5modDEV.sln -c Release /p:DeployToGta=true /p:GtaRoot="C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced"

Commande validation complète :

.\tools\run-safety-checks.ps1

Si PowerShell bloque :

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1

Si l'API GTA locale n'est pas disponible mais qu'on veut tester avec stub :

.\tools\run-safety-checks.ps1 -UseStubApi

Sorties attendues après build :

src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.dll
src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.ENdll
src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.pdb

Une build `Release` ordinaire ne modifie jamais GTA. Le déploiement n'est exécuté que si :

`/p:DeployToGta=true`

Le chemin explicite valide d'abord `GTA5_Enhanced.exe`, fabrique un package avec
`tools\package-game-ready.ps1`, vérifie ses hashes, stage les fichiers sur le volume
de destination, puis remplace transactionnellement l'ENdll, le PDB et le manifest
avec `tools\deploy-game-ready.ps1`. Dans `Scripts`, le manifest prend le nom stable
`DonJCustomNpcPlacer.manifest.json`; il alimente le diagnostic runtime de la build.
Le nouveau triplet est publié puis relu avant que les anciens alias soient déplacés
vers leurs backups transactionnels. Ainsi, une coupure du processus ne crée jamais
volontairement une fenêtre sans ENdll chargeable. Un alias verrouillé restaure ceux
déjà déplacés, puis déclenche le rollback inverse du triplet. Les backups ne sont
supprimés qu'après validation globale. Après un remplacement réussi seulement, le
déploiement retire les anciens noms pour éviter un double chargement :

DonJEnemySpawner.dll
DonJEnemySpawner.ENdll
DonJEnemySpawner.pdb

Règle importante : ne jamais livrer seulement le .dll. Le jeu/NIB charge ici le .ENdll.

Le package canonique contient exactement :

- `DonJCustomNpcPlacer.ENdll` ;
- `DonJCustomNpcPlacer.pdb` ;
- `INSTALLATION_SIMPLE.txt` ;
- `manifest.json` avec commit, identité exacte de la référence API v2, versions,
  schéma Justice, tailles et SHA-256.

Par défaut, `tools\package-game-ready.ps1` refuse une source Git modifiée. Le
commutateur `-AllowDirtySource` sert uniquement à produire localement un artefact
de contrôle portant `sourceDirty=true`; cet artefact est explicitement non
publiable et `tools\deploy-game-ready.ps1` le refuse. Un manifest déployable doit
porter `sourceDirty=false` et une version de schéma Justice exactement égale à 2.
Le package, la suite de sécurité et le déploiement relisent aussi les références
de l'assembly : exactement une référence `NIBScriptHookVDotNet2` ou
`ScriptHookVDotNet2` doit exister et sa version majeure doit être `2`. Le stub CI
porte l'identité `2.11.6.0` de l'API NIB live afin que son binaire reste chargeable
par le dictionnaire de versions du runtime.

`tools\run-safety-checks.ps1` vérifie la chaîne de hashes build → package →
installation temporaire pour une source propre. Sur une source locale modifiée,
il génère le package non publiable puis prouve que le déploiement le rejette. La CI
reste stricte et ne publie le package que si toute la suite réussit sur une source
propre.

## 5. Architecture runtime

La classe principale est :

public sealed partial class DonJEnemySpawner : Script

Elle est découpée en plusieurs fichiers avec partial.

Le runtime repose sur :

Tick += OnTick;
KeyDown += OnKeyDown;
Aborted += OnAborted;

OnTick est le cœur vivant du mod. Il doit rester léger. Toute logique coûteuse doit être cadencée. Les domaines sont isolés par `RuntimeTickStage` : une exception est journalisée avec un cooldown de dix secondes et n'empêche pas les étapes indépendantes suivantes. Si `JusticeEarly` échoue, `JusticeLate` ne progresse pas et seule `UpdateJusticeFailSafeMaintenance` peut restaurer les contrôles, la police, un inventaire différé et persister un état déjà préparé.

`OnAborted` restaure Justice en premier, puis isole Terminator, placement, menu,
confirmation danger, escorte, blips et groupes de relation. Le résultat de
l'enfilement Justice final est vérifié et journalisé, puis l'arrêt du repository
attend sa dernière révision au plus 2,5 secondes. Une étape défaillante ne doit
jamais empêcher les nettoyages suivants.

À éviter absolument dans OnTick :

- scanner tous les PNJ du monde à chaque frame ;
- envoyer TASK_* à chaque frame ;
- créer des allocations LINQ inutiles ;
- appeler ClearAllTasks en boucle ;
- forcer des téléportations visibles ;
- créer/supprimer des entités en masse sans budget ;
- manipuler des entités sans Entity.Exists ;
- écrire dans un fichier à chaque frame.

Les systèmes importants utilisent déjà :

Game.GameTime
intervalles en millisecondes
jitter par handle
dictionnaires de cache
budgets max par tick

Ce pattern doit être conservé.

Exemple conceptuel :

if (Game.GameTime < _nextSomethingAt)
{
    return;
}

_nextSomethingAt = Game.GameTime + IntervalMs;

Pour un groupe de PNJ, on évite de tous les mettre à jour au même moment. On utilise un curseur ou un jitter.

## 6. Règles générales C# / ScriptHook

Le code doit rester compatible :

C#
.NET Framework 4.8
ScriptHookVDotNet API v2
NIBScriptHookVDotNet2.dll

Ne pas utiliser :

- API v3 ;
- async/await qui touche le monde GTA ;
- thread de fond qui manipule Ped/Vehicle/Prop/Blip ;
- classes/méthodes inexistantes en API v2 ;
- dépendances NuGet inutiles ;
- refactor massif hors sujet ;
- renommage de fichiers ou classes sans nécessité.

Chaque accès à une entité doit être protégé :

if (!Entity.Exists(ped) || ped.IsDead)
{
    return;
}

Pour un véhicule :

if (!Entity.Exists(vehicle) || !vehicle.IsDriveable)
{
    return;
}

Pour un handle stocké :

Ped ped = FindPedByHandle(handle);

if (!Entity.Exists(ped))
{
    // nettoyer la référence interne
}

Ne jamais faire confiance à un handle stocké. GTA peut supprimer des entités à tout moment.

## 7. Natives GTA

Quand une fonction n'est pas exposée par l'API v2, le projet utilise des natives GTA.

Pattern existant :

private const ulong NativeSomething = 0x123456789ABCDEF0UL;

Puis appel :

Function.Call((Hash)NativeSomething, arg1, arg2, arg3);

Ou avec retour :

bool result = Function.Call<bool>((Hash)NativeSomething, arg1, arg2);

Règles :

- garder les constantes native en haut du fichier concerné ;
- utiliser ulong ;
- caster vers Hash au moment de l'appel ;
- entourer les natives risquées avec try/catch si elles peuvent varier selon version ;
- ne pas spammer une native lourde à chaque frame ;
- utiliser NativeDB Enhanced pour vérifier les signatures ;
- commenter en français l'intention gameplay.

Exemple de commentaire attendu :

// Je force une route propre vers le waypoint sans réassigner l'ordre à chaque frame.

Style commentaire du projet : en français, souvent à la première personne avec "Je".

## 8. Menu principal F10

Le menu est ouvert/fermé avec :

F10

Constantes stables :

TrainerTitle = "DonJ Custom NPC Placer"
TrainerSubtitle = "Placement propre pour NPC, vehicules et objets"
MenuToggleKey = Keys.F10
MenuToggleKeyLabel = "F10"

La console Obsidienne utilise trois zones sans masquer tout le monde du jeu :

- rail gauche : monogramme DonJ et navigation entre catégories ;
- panneau central : actions et réglages de la catégorie active ;
- panneau droit : valeur sélectionnée, aide contextuelle, compteurs de scène, sauvegarde active et notifications.

Catégories stables :

- NPC ;
- Véhicules ;
- Objets ;
- Intérieurs ;
- Scène ;
- Justice avancée ;
- Outils.

NPC est la catégorie initiale. Chaque catégorie mémorise sa dernière action sélectionnée par `MainMenuAction`, et non par un index global dépendant du nombre de lignes. Les catégories NPC, Véhicules et Objets sélectionnent automatiquement leur type de placement. Intérieurs conserve le choix explicite Entrée/Sortie. Scène regroupe sauvegarde et chargement ; Justice avancée regroupe l'activation du héros actuellement joué, le sélecteur de dossier Michael/Franklin/Trevor, le casier, le paiement volontaire et la réinitialisation protégée d'un profil ; Outils regroupe le mode Terminator et les quatre nettoyages.

Les commandes de placement caméra précis, de placement direct et de distance sont affichées en tête des catégories compatibles.

Rendu :

- hauteur logique de référence de 720, largeur calculée selon le ratio réel ;
- résolution obtenue via `Game.ScreenResolution` et marges via `GET_SAFE_ZONE_SIZE` ;
- valeur de safe-zone mise en cache au moins 250 ms, avec coupe-circuit de 5 s après une native défaillante ;
- fallback 16:9 borné si une information d'écran n'est pas exploitable ;
- monogramme, icônes, cadres et décorations dessinés uniquement avec les primitives GTA ;
- aucun PNG, YTD, OIV, RPF ou Scaleform requis ;
- pool de `UIRectangle` et `UIText` réutilisé après initialisation ;
- modèle de page mis en cache pour éviter les allocations à chaque frame ;
- animations courtes et bornées pour l'ouverture et le déplacement de la sélection.

Quand la console est ouverte, les notifications sont intégrées au panneau droit. Seul l'affichage HUD du mode Terminator est masqué ; le mode lui-même reste actif.

Contrôles menu :

F10 : ouvrir/fermer
Haut/Bas ou NumPad 8/2 : naviguer
Gauche/Droite ou NumPad 4/6 : modifier valeur
Entrée ou NumPad 5 : valider
Tab ou Shift+Tab : catégorie suivante/précédente
PageUp/PageDown : scroll rapide
Home/End : début/fin
Esc/Backspace/NumPad 0 : annuler une confirmation, fermer ou retour
T : saisir un modèle custom si le modèle sélectionné est Custom

La navigation du menu reste exclusivement au clavier. La refonte n'ajoute aucune gestion souris ou manette.

Sécurité des nettoyages :

1. Le premier appui sur Entrée ou NumPad 5 arme l'action et affiche une confirmation stylée.
2. Le relâchement de la touche est obligatoire avant qu'un second appui exécute uniquement le nettoyage sélectionné ; la répétition clavier ne peut pas confirmer.
3. Esc, Backspace, NumPad 0 ou la fermeture du menu annule l'action sans retirer d'entité.

### 8.1 Justice avancée

Justice avancée est désactivée par défaut. Michael, Franklin et Trevor possèdent chacun un profil indépendant contenant son choix d'activation, son dossier actif, son casier, sa récidive, sa dette, sa peine et son état de détention. Ces trois profils sont stockés séparément des scènes dans :

`DonJEnemySpawnerSaves\_justice_state.xml`

Le format courant est le schéma Justice `2.0`. Sa racine porte `schemaMajor="2"`, `schemaMinor="0"`, une `generation` monotone, `payloadSha256` et `recoverySha256`. Le digest du payload lie explicitement la génération au contenu : modifier l'un sans recalculer l'autre invalide le document. Le digest de récupération lie la génération, les champs globaux, le slot actif et le hash du profil actif; il ne permet donc pas de substituer silencieusement l'autorité runtime pendant l'isolation d'un profil inactif. La racine contient exactement une autorité `Profiles` et un bloc `RuntimeRecovery`; elle ne duplique plus `Case`, `Record` ou `Custody`. `Profiles` contient les slots canoniques 0, 1 et 2. Chaque profil possède sa génération, sa clé d'identité, son SHA-256 et exactement un fragment `Case`, `Record` et `Custody`.

Le thread GTA capture uniquement un snapshot profond immuable composé de DTO typés pour `Case`, `Record` et `Custody`. La sérialisation XML, les hashes, la validation, les I/O, la relecture et le retry sont exécutés par le writer dédié du `JusticeRepository`; sa case d'attente est `latest-wins`, donc une révision plus récente remplace un checkpoint normal encore en attente. `QueueJusticeStateCheckpoint` et `JusticeFlushStateNow` ne bloquent pas le gameplay sur le disque : ils rendent seulement le résultat de la capture et de l'enfilement. Un snapshot refusé par le writer peut être remplacé dès que son retry est dû, sans affamer une intention critique valide. Une bascule de protagoniste garde le nouveau contexte bloqué jusqu'à ce que sa révision atteigne `DiskRevision`; sa baseline `WriteFailures` est capturée avant chaque enqueue afin qu'un rejet très rapide ne soit jamais absorbé. Seul `Stop()` attend de façon bornée, au plus 2,5 secondes pendant `OnAborted`. `JusticeAwaitQueuedPersistenceForTests` est une barrière hors jeu réservée aux tests et attend au plus 30 secondes; elle ne fait pas partie du chemin de production. Avant de publier une révision disque, le repository valide le document avant écriture, relit les octets réellement installés, les compare puis redécode la même génération. Le remplacement d'un primaire existant utilise `File.Replace` et ne possède aucun fallback `Delete`/`Move` faussement atomique. Si l'opération n'est pas disponible, l'ancien primaire reste en place, la révision reste en attente de retry et le diagnostic montre que mémoire et disque n'ont pas convergé.

Le fichier `_justice_state.wal` n'embarque jamais `Case`, `Record`, `Custody`, l'inventaire complet ni un fragment XML. Une frame est limitée à 1 024 octets, vingt champs, et des tailles bornées pour l'identifiant, le type d'opération, les chemins et les valeurs. Pour une frontière critique générale, le runtime enfile d'abord le DTO typé, mémorise une barrière non bloquante, puis attend sur les ticks suivants que sa révision soit durable avant d'écrire seulement cinq références (`snapshotRevision`, génération du profil, identité, frontière et schéma). Les deux débits utilisent un identifiant stable par effet et un plan immuable : l'intention `Prepared` doit d'abord exister dans un snapshot complet dont `DiskRevision` a convergé. Juste avant `STAT_SET_INT`, le thread GTA écrit alors les petites frames financières `Prepared`, puis `Attempted`, et marque seulement ensuite l'intention mémoire comme tentée. Une reprise `Prepared` peut réévaluer ou annuler un plan devenu obsolète; une reprise `Attempted` ou `Ambiguous` ne rejoue jamais le débit. Le lecteur du WAL exige le bon état, slot actif, schéma, génération, identité et épisode avant de reconstruire l'intention. Une transaction terminale reste conservée jusqu'à une rotation disque supplémentaire afin qu'un backup contenant encore `Prepared` ne puisse pas ressusciter l'effet. Les frames checksummées suivent `Prepared`, `Attempted`, puis `Confirmed`, `Rejected` ou `Ambiguous`; une queue finale tronquée est détectée/réparée sans accepter une corruption interne, et la compaction n'est autorisée que lorsqu'aucune transaction ne reste ouverte et que le backup ne peut plus réintroduire le plan.

Lorsqu'un primaire v2 échoue uniquement à cause d'un ou plusieurs profils inactifs, Justice peut les remplacer depuis un backup v2 entièrement valide tout en conservant le profil actif du primaire. Cette isolation exige un `recoverySha256` valide, une structure et des slots non ambigus, un profil actif intact, puis un WAL intégralement `Clean` sans transaction ouverte, contrôlé une première fois avant la fusion et une seconde fois juste avant publication. Le snapshot fusionné est sérialisé, redécodé, validé métier, remplacé atomiquement, puis ses octets et son SHA-256 sont relus. Au chargement normal, les trois fragments `Custody` validés sont aussi hydratés en DTO typés sous leur propre profil; un profil inactif ne dépend donc plus d'un XML mutable ou de l'état du héros actif pour restaurer inventaire et jetons police. Une corruption du profil actif, des champs globaux ou de la preuve de récupération, un backup invalide, un WAL corrompu, tronqué/réparé ou encore ouvert interdit cette isolation; seul le chargement complet d'un backup valide reste alors possible.

La version 1 n'est plus un format d'écriture courant. Elle est lue pour migration, normalisée en mémoire vers l'adaptateur historique, puis le premier remplacement v2 conserve l'original byte pour byte sous `_justice_state.v1.bak` après vérification SHA-256. Ce backup de migration distinct n'est jamais écrasé. Une version inconnue ou un XML corrompu n'est jamais interprété partiellement; le backup valide est essayé, sinon Justice repart désactivée sans toucher aux scènes version 5. Un état canonique présent mais invalide interdit tout fallback legacy sans génération, afin qu'une ancienne peine ou transaction ne ressuscite jamais silencieusement.

Découpage des responsabilités :

- `Justice.Domain.cs` ne référence pas GTA et reste testable de façon déterministe ;
- `Justice.Profiles.cs` isole les trois états judiciaires et les changements de protagoniste ;
- `Justice.Payment.cs` précommitte et reprend le paiement volontaire sans laisser le menu toucher au cash ;
- `Justice.cs` transforme des fronts runtime bornés en incidents et preuves ;
- `Justice.Custody.cs` est seul responsable des transferts, de l'inventaire et des entités de détention ;
- `Justice.Persistence.Model.cs`, `Justice.Persistence.Codec.cs`, `Justice.Persistence.Migration.cs`, `Justice.Persistence.Runtime.cs`, `Justice.Repository.cs` et `Justice.Wal.cs` portent le schéma 2.0, le DTO, la migration v1, le writer et le journal critique ;
- `Justice.WorldSnapshot.cs` porte le snapshot spatial partagé et les métriques bornées ;
- `MenuUi.cs` présente les actions `MainMenuAction` Justice, les deux registres consultables, le mode police, la récupération manuelle, le diagnostic de build et les confirmations d'amnistie ou de réinitialisation.

La page Justice commence par `Justice du héros joué`, qui cible toujours le profil canonique actuellement incarné et affiche explicitement `ACTIVÉE/DÉSACTIVÉE · MICHAEL/FRANKLIN/TREVOR` hors transition. Pendant l'identification ou un basculement non committé, elle refuse temporairement l'action. La ligne suivante, `Personnage`, est un sélecteur de dossier : Gauche/Droite ou NumPad 4/6 fait défiler Michael, Franklin et Trevor sans fusionner leurs données et ajoute `JOUÉ` ou `CONSULTATION` à la valeur. Elle détermine les informations consultées et la cible du paiement ou de la réinitialisation, mais ne redirige jamais l'activation. `Payer la dette` n'autorise le débit que si le dossier sélectionné correspond au profil canonique actuellement joué ; aucun autre compte de protagoniste ne peut être débité, et le débit exige désormais la confirmation danger complète. Le montant numérique présenté au premier Entrée est conservé jusqu'au second ; si la dette augmente entre les deux, le paiement est refusé et une nouvelle confirmation est exigée. `Réinitialiser ce personnage` capture le slot ciblé au premier Entrée et annonce explicitement l'effacement du casier, du dossier, de la récidive, de la dette et de la détention. Un profil inactif portant une récupération est refusé. Hors détention, le profil vide est écrit deux fois afin que le primaire et le `.bak` portent tous deux la remise à zéro ; un échec de la seconde génération conserve le reset appliqué et sale pour retry. Pour le héros effectivement détenu, un WAL `ResetProfile` doit être présent dans le primaire et `.bak` avant de restaurer inventaire, état transitoire, police et sortie, puis il remplace directement le profil vide sans repasser par le garde recovery devenu obsolète. Un échec de la première ou de la seconde écriture conserve le latch et l'opération sans appliquer d'effet ; le chargement réaffirme systématiquement la barrière redondante. Les WAL de mort, d'amnistie, de libération, d'évasion, de rollback de transfert, de paiement, de réparation du backup, de switch ou de reset déjà engagé bloquent toute nouvelle réinitialisation.

Une frontière réellement pré-effet appartenant à l'ancien profil peut être rejetée proprement avant la bascule. En revanche, une restauration `SetJusticeCustodyPoliceSuppression` déjà tentée est reprise par le même caller jusqu'à son WAL terminal; elle n'est jamais rejetée puis recréée en boucle. Le nouveau profil reste bloqué jusqu'au commit disque de sa propre révision.

`Compatibilité police` fait défiler les trois modes documentés. `Récupération contrôles / inventaire` est une action de secours non destructive : elle libère les contrôles, tente de rendre les flags police possédés et fusionne seulement un snapshot d'armes déjà validé. `Diagnostic Justice` affiche directement le build ID informationnel, les révisions mémoire/disque et le nombre de WAL ouverts. Sur Entrée, il calcule le SHA-256 de l'assembly réellement chargé, lit `DonJCustomNpcPlacer.manifest.json` à côté de celui-ci, signale `manifest OK`, `MANIFEST DIFFÉRENT` ou `manifest absent`, puis journalise le rapport complet. Un manifest `sourceDirty=true`, d'un schéma différent de 2 ou dont le commit/version informative ne correspondent pas au binaire n'est jamais reconnu comme publié. Ce rapport contient aussi le schéma, la phase, le slot, les états inventaire/paiement/police, la dernière sauvegarde, les moyennes/p95/p99/maxima des domaines persistance/détection/incidents, les requêtes monde, le nombre d'entités du dernier snapshot et la file d'incidents. Aucun hash de fichier ni lecture du manifest ne s'exécute dans le simple rendu de chaque frame.

La catégorie expose deux vues de consultation en lecture seule :

- `Délits du dossier` parcourt chaque ligne de charge conservée, y compris une éventuelle ligne d'agrégat `Infractions consolidées · xN` ;
- `Casier judiciaire` aplatit chaque ligne conservée dans les vingt dernières condamnations, de la condamnation la plus récente à la plus ancienne, sans masquer les lignes supplémentaires d'une même affaire.

Le panneau droit détaille l'infraction sélectionnée, sa gravité, son amende, sa peine, les circonstances prouvées lorsqu'elles restent individualisées et, pour le casier, la date et les totaux de la condamnation. Une ligne agrégée affiche explicitement `xN faits` ; le compteur d'en-tête additionne les faits représentés et non les seules lignes, sans prétendre restituer les libellés ou circonstances individuelles déjà consolidés. Haut/Bas et les alias pavé numérique naviguent ligne par ligne ; PageUp/PageDown, Home/End et le scroll borné rendent toute la liste accessible ; Échap, Retour arrière ou NumPad0 revient à la catégorie Justice sans mutation du dossier. Gauche, Droite et Entrée sont consommées dans ces vues en lecture seule afin qu'aucune action gameplay ne soit déclenchée accidentellement. La vue aplatie du casier est mise en cache et n'est reconstruite que lorsque `LedgerRevision` change ; aucun parcours massif du registre ni nouvel objet UI n'est créé à chaque frame après le préchauffage du pool Obsidienne.

Hors F10, les infractions confirmées, mandats et évasions passent par le bandeau GTA natif existant. L'ancienne grande fenêtre Justice et son ancien bloc compact permanent ont été supprimés. Pendant une détention seulement, `MenuUi.cs` dessine une unique ligne discrète contenant le lieu, le temps restant et l'activité proche. `IsJusticePlayedProfileCustodyContextReady` exige que cette détention appartienne au protagoniste réellement joué : la ligne et la touche d'activité `E` disparaissent immédiatement sur les deux autres héros. Le détail judiciaire complet reste dans F10.

Règle de preuve :

1. Un acte détecté crée un incident provisoire de six secondes, sans HUD ni charge.
2. Un policier avec ligne de vue confirme immédiatement.
3. Une victime ou un civil crédible confirme après trois secondes s'il est encore valide et vivant.
4. Une hausse de wanted dans les quatre secondes peut corroborer uniquement cet incident déjà détecté avec observateur plausible.
5. Une hausse de wanted seule ne fabrique jamais d'infraction ; si aucune preuve ne survit, l'incident expire silencieusement.

La résolution n'altère jamais `_justicePendingIncidents` pendant son parcours. Une première phase collecte et qualifie dans des buffers réutilisés ; la deuxième résout les conflits et supersessions, puis applique les mutations et notifications sur le résultat stabilisé. Une violence corrélée remplace donc le tir dangereux provisoire sans retrait déclenché depuis un callback ni décalage d'index.

Les files bornées protègent les homicides et les faits graves sur victime ou agent contre l'éviction par une infraction mineure. Dans le quota de témoins, le runtime réserve d'abord les victimes mortes utiles à la qualification d'un homicide, puis les policiers vivants et enfin les autres témoins crédibles vivants. Une foule ou des cadavres voisins ne peuvent donc plus masquer l'homicide ni évincer tous les témoins capables de le signaler.

Une passe Justice capture au maximum une fois les peds et une fois les véhicules dans un rayon de 160 mètres autour du joueur. Témoins, victimes, véhicules et attribution alliée réutilisent ce snapshot puis filtrent en mémoire par distance au carré. Les observations finales restent limitées à 24 humains dans 80 mètres et ne s'ouvrent que dans la fenêtre d'un nouvel acte. Une mêlée encore active prolonge cette même fenêtre : un décès tardif au couteau reste qualifiable même si la native du timer de coup récent est momentanément indisponible. Un simple état `inCombat` prolongé n'ouvre la fenêtre que sur son front initial. Six incidents confirmés au maximum sont appliqués par tick ; le surplus reste dans la file bornée. Ne jamais remplacer ce mécanisme par `World.GetAllPeds`, du LINQ sur le monde, un scan par acteur ou un scan à chaque frame.

Les fronts persistants de dégâts GTA sont protégés par un baseline circulaire borné sur la paire `(victime, génération, auteur, génération)`. Une valeur déjà vraie lors de sa première observation est consommée sans infraction, sauf si un signal GTA récent et explicite prouve l'acte courant. La génération combine le handle, le modèle et `Entity.MemoryAddress`; quand cette adresse n'est pas disponible dans le stub, seule la même enveloppe `Entity` peut conserver son identité. Les témoins, les policiers reconnaissant un mandat et les victimes réutilisent tous cette identité renforcée.

Pendant mission, cinématique, chargement, changement de protagoniste ou détention, le runtime synchronise uniquement les latches scalaires. Il ne parcourt ni les peds ni les véhicules et ne purge aucun historique de dégâts à répétition. À la reprise du jeu libre, une passe unique photographie et consomme l'historique accumulé avant de rouvrir la détection. Une perte de wanted survenue pendant cette suspension est matérialisée une seule fois comme mandat, avant toute interprétation d'une mort éventuelle.

Le domaine déduplique par épisode, victime et génération de handle. Il remplace une agression par l'homicide correspondant, une dégradation par la destruction et une complicité par l'action directe, tout en conservant l'aggravant collectif prouvé. La fuite et l'évasion sont uniques par épisode, pas uniques pour toute la vie du dossier.

Le dossier actif persiste au maximum 512 lignes de charges. Au-delà, les faits les plus anciens de même statut judiciaire sont consolidés dans une charge d'agrégat : leur nombre et leurs sanctions saturées restent conservés, tandis que leurs libellés et circonstances individuels ne sont plus disponibles. Les nouvelles charges détaillées et les remplacements de qualification continuent d'être enregistrés. L'UI signale cette borne avec `Infractions consolidées · xN`, compte tous les faits représentés dans son total, et ne promet donc pas une fausse liste individuelle illimitée. L'agrégat ne participe jamais aux règles de victime, de doublon ou de supersession.

Une confirmation d'infraction ordinaire et la reconnaissance d'un mandat n'écrivent jamais le wanted. Justice observe le niveau GTA pour corréler un signalement, suivre une poursuite et conserver un mandat, mais GTA reste seul responsable de créer, augmenter ou retirer les étoiles liées aux crimes. Le casier purgé seul ne déclenche jamais une poursuite. Les seules mutations explicites restent les contrats séparés et volontaires : minimum de trois étoiles après une évasion confirmée, ou effacement unique lors d'une amnistie confirmée.

Les alliés DonJ continuent de défendre le joueur. Un jeton causal n'est créé qu'après la réussite réelle d'un ordre offensif. Une attaque sur policier n'est imputée que si l'auteur est une entité DonJ enregistrée, si son jeton causal de défense a moins de 12 secondes, si les deux entités sont à moins de 120 mètres du joueur et si l'acte possède sa propre preuve. Un allié implique un crime en réunion ; deux alliés ou une équipe Cartel/escorte structurée impliquent une bande organisée. À la capture, la cible est revérifiée avec son identité, sa distance et la fraîcheur du jeton avant de remplacer uniquement le combat policier courant par une tâche d'attente ou de freinage. Justice n'appelle jamais `CLEAR_PED_TASKS`, qui interromprait aussi la conduite et l'escorte. Aucun allié, garde ou véhicule n'est retiré, supprimé ou renvoyé : leurs handles et leur service actif sont conservés. Pendant la détention, leurs boucles de suivi sont suspendues ; ces systèmes reprennent leur fonctionnement normal après la détention.

L'identité d'un contributeur allié est la paire `handle + génération`, conservée dans l'incident, la charge et le fragment `Case` du profil v2. Les anciens XML v1 qui ne stockaient que le handle restent lisibles avec une génération historique zéro. Un handle recyclé avec une nouvelle génération constitue donc un contributeur distinct. Si un allié DonJ meurt dans l'échange qui tue aussi l'agent, son jeton peut encore prouver la complicité tant que sa génération, sa causalité, ses distances et sa preuve restent valides ; ce snapshot d'appartenance ne peut jamais autoriser un ped vivant autonome ou non-DonJ.

Le dernier slot canonique est mémorisé avant toute transformation en ped Iron Man ou autre modèle custom. Une capture ne peut utiliser que le slot canonique courant ou ce dernier slot prouvé dans la session ; si l'identité reste inconnue après une mort, elle attend un rebinding sûr ou redevient un mandat. Après la mort qui crée la capture comme après une mort en détention, un ped custom sans slot n'est relié que si le latch de mort, le profil actif et le dernier slot canonique désignent tous le même héros. Une fois ce rebind durable, l'absence persistante de slot cash suit le fallback contractuel : aucune écriture `STAT_SET_INT`, conversion complète de l'amende en peine, puis poursuite du transfert. Le runtime n'adopte jamais automatiquement Franklin, Michael ou Trevor parce qu'un autre protagoniste réapparaît, et ne désarme ni ne débite ce personnage sans preuve. La même identité persistée protège les reprises de détention après chargement.

La détention choisit : amende seule pour zéro seconde, Mission Row sous cinq minutes, Bolingbroke dès qu'une peine atteint cinq minutes. Les amendes ne possèdent aucun plafond de gameplay : chaque charge continue d'ajouter sa valeur complète. `JusticePolicy.MaxActiveFine = 1 000 000 000 000` dollars est uniquement une saturation technique anti-overflow et une borne de validation XML, pas un équilibrage destiné à réduire une dette. Le débit vise exclusivement le slot canonique prouvé. Si l'identité est inconnue, aucune mutation d'inventaire ou d'argent n'est effectuée avant résolution sûre ; si l'identité custom est prouvée mais son slot cash reste masqué, l'amende est convertie sans écriture d'argent. Les paiements volontaires précommittés sont déduits du minimum exigé par la condamnation lors de la capture : le validateur compare `FineDue` à `max(0, Conviction.Fine - VoluntaryFinePaid)` et conserve la peine brute. La transaction métier utilise `Prepared`, `Attempted`, `Confirmed`, `Rejected` ou `Ambiguous`, distinctement du retour brut `Succeeded`, `Rejected` ou `Unknown` de `STAT_SET_INT`. Après une écriture déjà tentée, le débit n'est jamais rejoué. Un troisième solde ou une lecture toujours indisponible au timeout produit `Ambiguous` : le montant quitte `FineDue`, rejoint `FineInDispute`, reste visible et n'alimente jamais `VoluntaryFinePaid` ni une conversion immédiate en peine. Une annulation avant débit devient terminale sans écriture d'argent. Un manque d'argent prouvé est converti à raison d'une seconde par 50 dollars, arrondi à 15 secondes et borné par le site. Les débits de jugement et de paiement volontaire restent idempotents : un crash ne peut ni débiter deux fois ni présenter un litige comme un succès certain.

Avant chaque débit, le plan `Prepared` est inclus dans un snapshot complet durable. Le WAL financier ne porte ensuite que ses champs immuables et ses preuves d'identité; `Attempted` est flushé juste avant la native cash. Après résolution, le WAL terminal n'est compacté qu'une fois une génération supplémentaire publiée, afin que le `.bak` ne puisse plus contenir une intention `Prepared` capable de ressusciter le débit.

Le snapshot d'inventaire validé est persisté avant `RemoveAll`. Pour `GET_DLC_WEAPON_DATA`, `Justice.Custody.cs` alloue une seule fois un tampon unmanaged de 312 octets, le remet à zéro avant chaque appel, le passe comme `InputArgument(ulong)`, ne lit que le hash à l'offset 8, puis le libère en `finally`. Il ne faut jamais réintroduire `OutputArgument` pour cette structure : NIB v2 n'y réserve que 24 octets et une écriture native de 312 octets corrompt le tas du jeu. L'état explicite distingue `CapturePending`, `SnapshotPersisted`, `RemovalPending`, `RemovedVerified`, `UnsupportedPreserved`, `RestorePending` et `RestoreAmbiguous`. Le résultat physique du retrait distingue aussi `NotAttempted`, `RemovedVerified` et `EffectMayHaveApplied`. Le marqueur d'effet est posé avant la native : si `RemoveAll` retire réellement les armes puis retourne un faux négatif ou lève après l'effet, Justice conserve le snapshot validé, passe en `RestoreAmbiguous` et programme une restitution, au lieu de détruire la seule preuve. Un échec transitoire avant effet conserve les armes, ne verrouille pas le combat et retente la capture au plus trois fois avant fallback. Un inventaire incompatible n'entre jamais dans la prison interactive : le dossier reste cohérent mais aucun retrait n'est effectué. Une restitution différée conserve en mémoire le handle, le modèle et le slot du ped custom jusqu'à son commit dans la même session, sans sérialiser ce handle GTA instable.

L'énumération combine l'enum NIB v2 et les définitions d'armes DLC chargées. Une lecture incomplète invalide le snapshot et déclenche le fallback `UnsupportedPreserved`, sans suppression ni verrou permanent. Ce fallback, l'évasion, le reset et la récupération orpheline ne peuvent jamais écraser un `RestoreAmbiguous` issu d'un effet possiblement appliqué. La récupération manuelle du menu ne sait que libérer les contrôles, sélectionner les poings, fusionner un snapshot validé, restaurer la police et journaliser le diagnostic ; elle n'appelle jamais `RemoveAll`. La restitution normale fusionne sans destruction puis vérifie exactement armes, munitions, composants, teintes, chargeurs et arme sélectionnée ; elle ne supprime le snapshot qu'après validation et commit durables. Une restitution partielle reste persistée et récupérable. `OnAborted` tente la même restauration fusionnée et conserve l'état différé si GTA ne peut pas encore la confirmer. Une évasion précommitte l'intention de discard puis supprime le snapshot sans restitution seulement lorsqu'aucune ambiguïté de retrait ne doit d'abord être résolue.

Après une confiscation vérifiée, le joueur reste forcé à mains nues mais peut attaquer, viser et se défendre contre les détenus. Le changement d'arme, le rechargement et la roue restent bloqués. Tant que le retrait physique des armes n'est pas prouvé, le verrou de secours bloque aussi le combat afin qu'une arme conservée par erreur ne soit jamais utilisable. La discipline ne se déclenche que sur un nouveau front de dommage attribué au joueur ou un homicide causalement prouvé, jamais sur le simple état `IsInCombat`, un tir sans impact ou une animation de mêlée. Un front détenu→joueur ouvre une fenêtre de légitime défense de huit secondes, liée au handle et à sa génération : une riposte non létale à mains nues pendant cette fenêtre est ignorée, tandis qu'une riposte tardive, un autre détenu ou un homicide restent sanctionnés. Un dernier scan non cadencé précède toute libération, évasion ou compaction de scène ; les gardes et détenus sont possédés par la paire `handle + génération`, afin qu'un handle GTA recyclé ne puisse jamais être adopté ou supprimé.

Le fichier `_justice_state.xml` v2 reste borné à 16 Mio. Cette limite couvre le pire état métier autorisé des trois profils — vingt condamnations visibles et jusqu'à 512 résumés consolidés par condamnation — sans miroir du profil actif. L'écriture temporaire et la lecture refusent toujours un fichier vide ou supérieur à cette borne.

L'effacement wanted d'amnistie et le minimum wanted d'évasion exigent une frame `Prepared` du petit WAL, vidée durablement par `Flush(true)`, avant l'appel GTA. La tentative passe ensuite à `Attempted`; la confirmation n'intervient qu'après durabilité du snapshot v2 correspondant. Après ce point, une reprise est strictement at-most-once et ne rejoue jamais la native. Aucun essai amorcé pour Trevor ne peut donc écrire le wanted de Franklin ou Michael, et une ancienne évasion ne peut pas réappliquer trois étoiles après leur disparition naturelle.

L'intégration police propose `Disabled`, `FreeroamBestEffort` et `Force`; le défaut est `FreeroamBestEffort`. `Disabled` ne pose jamais de nouveau flag global. Le mode par défaut applique au plus une fois en jeu libre compatible, alors que `Force` peut réaffirmer les natives au cadenceur de détention. Aucun mode ne doit appliquer les flags pendant mission, cinématique, chargement ou changement de protagoniste. Sans getter GTA fiable, Justice ne connaît pas l'état antérieur réel : une restauration vers les valeurs vanilla supposées reste best-effort et ne doit jamais être présentée comme exacte. Pour une installation fortement modifiée ou un trainer qui possède le dispatch, utiliser `Disabled`.

Les quatre indicateurs de suppression policière (`active`, ignore appliqué, dispatch désactivé et restauration en attente) font partie de `HasJusticeCustodyRecoveryState`. Les attributs `policeSuppressionApplied` et `policeDispatchDisabled` sont aussi reconnus par `HasJusticeProfileCustodyRecovery`. Leur retry s'exécute même si Justice est désactivée. Une incarcération stable peut être mise en arrière-plan lors d'un changement GTA, mais seulement après annulation de l'activité courante, restauration durable des deux natives globales et suppression de la scène Justice. Tant que cette restauration n'est pas réellement réussie puis commise, le basculement du profil Justice reste différé. Même lorsqu'un WAL interdit encore ce basculement, la restauration globale police est tentée dès que le nouveau slot canonique est prouvé. Après un crash, les jetons retrouvés dans n'importe quel profil inactif sont fusionnés dans le retry global, retirés de leur fragment XML puis durcis uniquement après le succès des deux natives. Les WAL financiers, disciplinaires, de mort, d'amnistie, de rollback, de reset ou de libération continuent eux aussi de bloquer le basculement. Aucun inventaire ni état transitoire du détenu n'est restauré sur le protagoniste entrant.

Pendant une réparation du primaire depuis le backup, les mutations métier restent suspendues mais les fronts `DeathStarted`, `ArrestStarted`, `ArrestEnded`, `WantedLost` et `WantedRaised` sont mémorisés. Leur slot et leur modèle sont capturés ; toute divergence ajoute `IdentityChanged`. Après réparation, seuls les fronts compatibles avec le même protagoniste sont rapprochés et une observation ambiguë ne crée jamais directement une condamnation.

`ResetJusticeRuntimeFrontsForProfileChange` appelle aussi `CancelPendingDangerAction`. En complément, la modale capture au premier Entrée le slot Justice et, pour le héros joué, le handle et le modèle du ped. Au second Entrée, un slot canonique différent est refusé immédiatement, même avant le tick de switch. Un ped Iron Man/custom déjà prouvé reste accepté seulement si son slot actif historique, son handle et son modèle disponible correspondent encore ; changer la ligne `Personnage` ne redirige jamais un reset préparé.

`IsJusticePlayedProfileContextReady` centralise le gate des mutations F10 liées au héros joué. Il exige l'absence de sélection/switch bloqué, un profil actif canonique et la compatibilité runtime ; `RequestJusticeToggle`, le paiement et les libellés `JOUÉ` l'utilisent tous. `IsJusticePlayedProfileCustodyContextReady` ajoute l'appartenance de la détention au même slot et l'absence de suspension runtime mise en cache pour le HUD et les interactions monde. La ligne de peine disparaît donc dès l'animation de changement GTA, sans nouvelle native dans le rendu. Une armure Iron Man déjà rattachée reste compatible, tandis qu'un changement GTA pas encore réconcilié affiche `IDENTIFICATION / CHANGEMENT EN COURS` et ne modifie aucun profil.

Le paiement ajoute le prédicat plus strict `CanJusticeMenuPaySelectedProfile` : le slot canonique courant doit être visible et identique au profil actif/sélectionné. Sous un ped custom correctement rattaché, les autres fonctions restent disponibles mais la ligne de dette affiche `indisponible` et l'aide demande de reprendre brièvement le héros GTA, au lieu d'afficher un faux `payer`.

Le temps de peine avance uniquement pendant le gameplay actif. La transition GTA elle-même reste suspendue pendant pause, chargement, mission, cinématique, mort et changement de personnage. Une fois un autre héros jouable, chaque profil stable incarcéré et inactif possède toutefois son propre dernier tick et son reliquat en millisecondes pendant chaque intervalle continu de gameplay : sa `SentenceSeconds` continue de diminuer sans HUD, scène, police, inventaire, téléportation ni effet monde. Une suspension ferme l'intervalle et remet ce reliquat sous-seconde à zéro. Ce cache runtime n'utilise jamais l'heure UTC et ne rattrape donc aucun temps passé hors jeu. Une peine arrivée à zéro hors écran conserve sa phase et son snapshot ; la transaction WAL de libération et la restitution ne s'exécutent qu'au retour du bon slot, avant tout transfert inutile en cellule. Au retour, toute ancienne tâche de scénario est retirée sur le bon ped avant la reprise ou la libération. Les phases `Captured`, `Transporting`, `Escaping` et tout profil portant une transaction bloquante ne progressent jamais en arrière-plan. Après une grâce de démarrage, chaque activité vérifie à cadence bornée que son scénario GTA reste réellement actif ; une interruption, un combat ou une sortie de zone annule tout bonus. L'horloge d'activité est indépendante du framerate : une sonde valide ne consomme pas la frame courante et seule une réponse native inconnue gèle la progression. La réduction totale reste plafonnée à 25 % de la peine initiale, une minute au poste ou cinq minutes en prison.

Si le détenu meurt et que GTA le fait réapparaître à l'hôpital, le front de mort persisté relie uniquement le même profil canonique puis le renvoie dans la cellule du bon site. Ce retour est idempotent : il ne recrée pas la condamnation, ne rajoute pas la peine et ne rejoue pas les opérations d'inventaire déjà commises.

Avant un transfert de détention, un éventuel mode placement est fermé et sa caméra ainsi que ses flags sont restaurés avant le snapshot Justice. Après chaque téléport physiquement vérifié, Justice garantit que le protagoniste est mobile avant de valider ou de faire progresser la peine. Le drapeau `FreezePosition` transitoire encore imposé par GTA après une arrestation ou un respawn est retiré. Tous les échecs de transfert — snapshot, précommit ou téléportation — déclenchent après trente secondes le même rollback durable ; une remise en liberté technique ne réinjecte jamais le gel transitoire et repasse explicitement par le garde de mobilité. Le même garde léger s'exécute pendant la détention pour réparer les anciens XML actifs et suspend l'horloge tant que GTA refuse le dégel. Le téléporteur partagé des intérieurs continue, lui, de restaurer exactement son état d'entrée.

La libération légale conserve son WAL jusqu'à restitution, restauration transitoire, sortie et précommit durable de l'unique tentative d'effacement wanted. Cette tentative est at-most-once : `Rejected` ou `Unknown` est journalisé, puis aucune écriture tardive n'est autorisée, car elle pourrait effacer les étoiles d'une nouvelle infraction commise après la reprise de contrôle. Si l'acquittement v2 échoue, la reprise conserve `Attempted=true`, n'impose plus l'arme et ne rappelle plus la native GTA.

À Bolingbroke, le volume autorisé suit un périmètre fixe de huit sommets autour de toute l'enceinte, et non la seule cour centrale ni un grand rectangle englobant des coins situés hors des murs. Une évasion est confirmée après trois secondes continues réellement passées hors de ce périmètre. Toute téléportation, y compris depuis F10, suit la même règle. L'opération est idempotente, conserve la dette et le temps restant, ajoute une seule nouvelle charge d'évasion et applique un minimum exact de trois étoiles sans diminuer un wanted GTA déjà supérieur.

La désactivation avec dossier actif utilise le modal destructif Obsidienne et son verrou de relâchement de touche. L'intention d'amnistie est d'abord écrite dans une frame `Prepared` du petit WAL puis reprise de façon idempotente après un crash. Tout échec de ce précommit conserve `_justiceAmnestyPending` et le cache runtime reste non validé; inventaire, dossier, wanted et cash restent intacts. L'amnistie attend aussi la résolution de tout débit de capture ou paiement volontaire. Elle n'efface l'état actif qu'après restitution réussie de l'inventaire et conserve l'historique ainsi que l'indice de récidive. L'unique tentative wanted passe à `Attempted` avant son application; une reprise ambiguë ne la répète pas. Le rollback de transfert suit la même règle et ne retire jamais son opération avant la durabilité de la révision v2 qui l'acquitte.

L'outil `tools\repair-justice-state.ps1` répond uniquement à la récupération hors ligne du dossier Justice v1 bloqué identifié lors du crash du 26 août 2026. Il exige exactement un `Case`, un `Record` et un `Custody`, refuse tout inventaire retiré, verrouillé, différé ou accompagné d'un snapshot, sauvegarde et hash les fichiers primaire/backup, préserve intégralement `Record` et la récidive, puis vide seulement affaire, peine, mandat, capture et détention actifs. Justice reste activée et `pendingAmnestyWantedClear=true` demande un unique effacement du wanted au prochain lancement. Ce script est un réparateur ciblé de cet état canonique, pas un validateur sémantique exhaustif ni un réparateur XML universel.

Je l'exécute uniquement jeu et loaders fermés, après avoir vérifié le chemin exact :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\repair-justice-state.ps1 -StatePath "C:\chemin\vers\DonJEnemySpawnerSaves\_justice_state.xml"
```

Le `ShouldProcess` demande confirmation par défaut. Le dossier horodaté `_justice_recovery_backups` et son `manifest.json` doivent être conservés jusqu'à validation en jeu.

Types de placement :

private enum PlacementEntityType
{
    Npc,
    Vehicle,
    Object,
    Entrance,
    Exit
}

Comportements PNJ :

private enum NpcBehavior
{
    Static,
    Attacker,
    Neutral,
    Ally,
    Bodyguard,
    NeutralPatrol,
    HostilePatrol,
    AllyPatrol
}

Objets interactifs :

private enum ObjectInteractionKind
{
    None,
    Cash,
    Health,
    Armor,
    Ammo
}

Quand on ajoute une nouvelle option menu :

1. Ajouter l'entrée dans MainMenuAction si nécessaire.
2. L'affecter explicitement à l'une des catégories dans le modèle de page mis en cache, sans introduire de nombre magique.
3. Gérer les changements dans ChangeMainMenuValue().
4. Gérer l'action dans ActivateMainMenuItem().
5. Garder la sélection par action et la normaliser dans la catégorie concernée.
6. Si l'action est destructive, conserver le passage obligatoire par la confirmation.
7. Ajouter les tests de navigation, de disposition et de comportement correspondants.

## 9. Placement PNJ

Le placement PNJ passe par :

TrySpawnNpc
CreatePedFromModelIdentity
RegisterSpawnedNpc
ConfigureSpawnedPed
StartNpcRuntimeBehavior

Un PNJ placé peut avoir :

- modèle ;
- arme ;
- attachments ;
- teinte ;
- munitions ;
- santé ;
- armure ;
- comportement ;
- rayon de patrouille ;
- respawn automatique ;
- blip ;
- relation avec joueur et groupes.

Bornes stables :

Santé : 1 à 5000
Armure : 0 à 200
Distance placement : 25 à 2500, pas de 25
Rayon patrouille : 5 à 500, pas de 5

Règle importante : les modèles doivent être demandés et vérifiés avant création.

Pattern :

Model model = identity.ToModel();

if (!model.IsValid || !model.IsPed)
{
    return null;
}

model.Request(timeout);

if (!model.IsLoaded)
{
    return null;
}

Après création, relâcher le modèle si le code existant le fait dans ce flux :

model.MarkAsNoLongerNeeded();

## 10. Placement véhicules

Le placement véhicule passe par :

TrySpawnVehicle
CreateVehicleFromIdentity
RegisterPlacedVehicle
ConfigurePlacedVehicleEntity

Un véhicule placé peut être :

- persisté dans la scène ;
- sauvegardé XML ;
- respawn automatiquement ;
- nettoyé via menu ;
- utilisé par des bodyguards selon logique IA.

Règles véhicules :

- poser au sol correctement ;
- régler heading ;
- nettoyer proprement les blips ;
- éviter le spam d'upgrades ;
- éviter SetOnGroundProperly en boucle ;
- protéger IsDriveable ;
- ne pas contrôler deux fois le même véhicule avec deux systèmes différents.

Pour un véhicule de convoi, ne jamais envoyer une tâche conducteur chaque frame. Utiliser une cadence :

CartelVehicleOrderIntervalMs
HighSecurityEscortVehicleOrderIntervalMs
EnemyRaidVehicleOrderIntervalMs

## 11. Placement objets

Le placement objet passe par :

TrySpawnObject
CreatePropFromIdentity
RegisterPlacedObject
ConfigurePlacedObjectEntity

Catégories :

Sécurité
Couverture
Argent / butin
Matériel tactique
Soin / survie
Bureau / informatique
Atelier / outils
Mobilier
Caisse / stockage
Décoration
Lumière
Extérieur
Divers

Interactions possibles :

None
Cash
Health
Armor
Ammo

Quand le joueur s'approche d'un objet interactif :

- marker / hint ;
- touche E ;
- gain ou effet ;
- suppression ou désactivation selon logique ;
- sauvegarde compatible si nécessaire.

## 12. Relations et IA

Relations importantes :

RelationshipCompanion = 0
RelationshipNeutral = 3
RelationshipDislike = 4
RelationshipHate = 5

Le projet initialise plusieurs groupes relationnels :

- joueur ;
- alliés ;
- neutres ;
- hostiles ;
- Cartel ;
- Ballas ;
- escortes.

Règle critique : ne pas créer une haine globale contre des groupes GTA ambiants sans garde-fou.

Bon comportement :

- identifier une menace réelle ;
- vérifier distance ;
- vérifier relation ;
- vérifier agression ou tir ;
- appliquer la relation seulement sur les groupes concernés ;
- limiter les refresh relationnels.

Le mod distingue :

- PNJ statique hostile ;
- attaquant ;
- neutre ;
- allié ;
- bodyguard ;
- patrouille neutre ;
- patrouille hostile ;
- patrouille alliée.

Les alliés ne doivent pas spammer TASK_COMBAT_PED. Le mod utilise des caches de menace et des intervalles.

## 13. Système de sauvegarde XML

Dossier principal :

Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves

Fallbacks possibles :

Documents\Rockstar Games\GTA V Enhanced\DonJEnemySpawnerSaves
%LOCALAPPDATA%\DonJEnemySpawner\Saves

Variable d'environnement possible :

DONJ_ENEMY_SPAWNER_SAVE_DIR

Fichier marqueur dernière sauvegarde :

_last_save.txt

Les XML contiennent :

- PNJ ;
- modèles custom ;
- armes ;
- attachments ;
- comportements ;
- santé ;
- armure ;
- véhicules ;
- objets ;
- entrées/sorties d'intérieurs ;
- options de respawn ;
- positions ;
- headings ;
- données compatibles anciennes versions.

Règles XML :

- garder CultureInfo.InvariantCulture pour les nombres ;
- ne pas casser les anciens fichiers ;
- accepter les attributs manquants ;
- garder les .bak si le système les utilise ;
- sanitariser les noms de sauvegarde ;
- éviter les chemins arbitraires non contrôlés.

## 14. Respawn automatique

Le respawn automatique permet de recréer :

- PNJ ;
- véhicules ;
- objets.

Constantes importantes :

AutoRespawnCheckIntervalMs = 1000
AutoRespawnMinDelayMs = 6000
AutoRespawnRetryDelayMs = 15000
AutoRespawnMaxPerTick = 3
AutoRespawnLeaveDistance = 220.0f
AutoRespawnNearSafetyDistance = 70.0f

Règles gameplay :

- ne jamais respawn sous les yeux du joueur ;
- attendre que le joueur soit assez loin ;
- éviter de respawn trop proche ;
- limiter le nombre de respawns par tick ;
- réessayer plus tard si le spawn échoue ;
- préserver position, rotation, modèle et options.

## 15. Contacts téléphone

Le téléphone ajoute trois systèmes gameplay :

C : Cartel allié
R : attaque Ballas hostile
L : escorte haute sécurité

Ces touches doivent être actives uniquement dans le contexte prévu, principalement quand le téléphone joueur est ouvert ou quand le système d'escorte demande une validation route.

Le mod vérifie l'état téléphone avec native :

NativeIsPedRunningMobilePhoneTask = 0x2AFE52F782F25775UL

Règles :

- utiliser des latches pour éviter plusieurs appels sur une pression ;
- garder des cooldowns courts mais réels ;
- ne pas relancer une équipe active sans gérer son retrait ;
- afficher un statut clair au joueur ;
- ne pas bloquer les autres systèmes.

## 16. Système Cartel

Contact :

Cartel

Touche :

C

Rôle :

Appeler une équipe alliée de protection.

Configuration actuelle :

11 gardes
3 véhicules
500 santé
200 armure
Service Carbine + Machine Pistol
spawn entre 68 m et 118 m
conduite professionnelle
combat drive-by ou à pied
retrait propre si rappel

Le Cartel :

- protège le joueur ;
- suit à pied si joueur à pied ;
- suit en véhicule si joueur en véhicule ;
- engage les menaces réelles ;
- peut tirer depuis les véhicules ;
- descend si menace proche ;
- rejoint les véhicules si nécessaire ;
- se retire proprement quand rappelé.

Règle performance :

Le Cartel ne doit pas scanner tout le monde trop souvent.
Il utilise des caches de menace et un nombre limité de scans par passe.

## 17. Système Ballas

Contact :

Ballas

Touche :

R

Rôle :

Créer une attaque hostile dynamique autour du joueur.

Configuration actuelle :

4 à 12 ennemis par appel
max 36 ennemis actifs
max 4 véhicules
100 santé
100 armure
spawn entre 72 m et 130 m
arrivée en véhicules
drive-by puis combat à pied
nettoyage post-combat
restauration après mort du joueur

Les Ballas :

- sont hostiles ;
- arrivent en véhicules ;
- tirent en drive-by ;
- descendent pour combattre ;
- sont nettoyés après combat ;
- peuvent être reconstruits après mort du joueur si GTA les supprime.

Règles :

- ne pas laisser des blips rouges véhicules après combat terminé ;
- ne pas supprimer un véhicule visible immédiatement ;
- nettoyer quand joueur s'éloigne ou ne regarde plus ;
- limiter le nombre d'actifs.

## 18. Système escorte haute sécurité / limousine blindée

Fichier principal :

src\DonJEnemySpawner\DonJEnemySpawner.HighSecurityEscort.cs

Contact :

Escorte haute sécurité

Touche :

L

Rôle :

Créer un convoi VIP allié avec limousine blindée et véhicules d'escorte.

Configuration actuelle de base :

1 limousine blindée
4 Baller noirs
gardes Cartel renforcés
500 santé
200 armure
Service Carbine + Machine Pistol
IA dédiée convoi
trajet waypoint
combat de protection
retrait propre

Modes internes :

HighSecurityEscortModeNone
HighSecurityEscortModeArriving
HighSecurityEscortModeStandby
HighSecurityEscortModeConvoyRoute
HighSecurityEscortModeFootFollow
HighSecurityEscortModePlayerVehicleFollow
HighSecurityEscortModeDismissing

Rôles véhicules :

HighSecurityEscortVehicleRoleLimousine = -100
HighSecurityEscortVehicleRoleFrontLeft = 0
HighSecurityEscortVehicleRoleFrontRight = 1
HighSecurityEscortVehicleRoleRearLeft = 2
HighSecurityEscortVehicleRoleRearRight = 3

Même si les noms historiques disent "FrontLeft/FrontRight", la logique peut être adaptée pour faire une file propre derrière la limousine.

Flux gameplay :

1. Le joueur ouvre le téléphone.
2. Il appuie sur L.
3. Le convoi spawn hors champ, si possible sur route.
4. La limousine arrive près du joueur.
5. Le joueur monte à l'arrière avec F.
6. Le joueur place un waypoint.
7. Le joueur appuie sur L dans la limousine.
8. Le convoi part vers le waypoint.
9. Les Baller suivent et protègent.
10. En cas d'attaque, les gardes réagissent.
11. À destination, le convoi repasse en standby.
12. Si le joueur rappelle/rejette, le convoi se retire.

Règles importantes pour travailler sur la limousine :

- ne pas casser la conduite existante ;
- ne pas spammer TASK_VEHICLE_DRIVE_TO_COORD ;
- garder les véhicules route-based ;
- éviter les spawns visibles ;
- éviter les téléportations visibles ;
- garder la place joueur libre ;
- protéger le chauffeur ;
- garder l'entrée F assistée ;
- bloquer les bugs où la limousine écrase le joueur ;
- ne pas envoyer tout le convoi sur la même coordonnée ;
- faire des offsets propres en file ou formation ;
- garder un fallback si aucun node route n'est trouvé.

Interaction avec Justice avancée :

- lors d'une capture confirmée, la cible policière courante est revérifiée puis remplacée par une tâche d'attente/freinage sans `CLEAR_PED_TASKS` ;
- le convoi, ses gardes et ses véhicules ne sont jamais retirés, supprimés ou renvoyés par Justice ;
- l'IA de route, de formation et de combat du convoi est suspendue pendant toute la détention ;
- les handles, le mode actif et les entités sont conservés afin que le service reprenne après la détention.

Bon pattern convoi :

- trouver un point de route hors champ ;
- calculer une direction d'approche ;
- placer la limousine devant ;
- placer les Baller derrière avec un spacing stable ;
- snapper sur vehicle nodes si possible ;
- garder une hauteur Z sûre ;
- donner un heading cohérent ;
- enregistrer chaque véhicule avec son rôle ;
- donner des ordres de conduite cadencés.

Combat escorte :

- menace scannée avec cache ;
- passagers tirent depuis véhicule si possible ;
- descente seulement si menace proche ou véhicule bloqué ;
- limousine garde priorité route si joueur dedans ;
- Baller peuvent devenir plus agressifs ;
- conducteurs reçoivent un style adapté ;
- les gardes reviennent au véhicule si situation calme.

Déblocage véhicule :

- soft unstuck après quelques secondes bloqué ;
- petite marche arrière possible ;
- hard rescue seulement si très loin ou hors champ ;
- jamais de téléportation visible ;
- ne pas repositionner une entité que le joueur regarde de près.

## 19. Conduite IA GTA

Le projet utilise des styles de conduite numériques.

Constante générale :

private const int ProfessionalDrivingStyle = 786603;

Dans l'escorte :

HighSecurityEscortFastTaxiDrivingStyle = 786469
HighSecurityEscortCombatDrivingStyle = 2883621

Principe :

- conduite normale : propre, route, évitement, trafic ;
- conduite rapide : taxi pressé, dépassements, moins de respect feux ;
- conduite combat : plus agressive mais à limiter aux menaces.

GTA ne donne pas toujours une lecture propre des panneaux de vitesse via ScriptHookVDotNet v2. Pour simuler une conduite respectueuse, il faut régler :

- driving style ;
- vitesse max ;
- fréquence de retask ;
- distance d'arrivée ;
- target route node ;
- comportement proche destination.

Ne pas utiliser des vitesses délirantes. Dans GTA, une vitesse scriptée trop haute rend les véhicules violents et instables.

Bonnes pratiques :

- arrivée convoi : vitesse modérée ;
- route VIP normale : vitesse taxi réaliste ;
- urgence : vitesse plus haute mais pas projectile ;
- combat : vitesse plus agressive mais toujours contrôlée ;
- proche destination : limiter vitesse ;
- proche joueur : limiter fortement.

## 20. Intérieurs

Les intérieurs sont gérés dans :

DonJEnemySpawner.Interiors.cs
DonJEnemySpawner.Interiors.AdvancedLoading.cs
DonJEnemySpawner.InteriorCatalog.cs

Le mod peut placer :

- une entrée ;
- une sortie ;
- un couple entrée/sortie ;
- des portails persistants sauvegardés XML.

Le chargement avancé peut utiliser :

- SET_FOCUS_POS_AND_VEL ;
- SET_HD_AREA ;
- NEW_LOAD_SCENE_START ;
- GET_INTERIOR_AT_COORDS ;
- PIN_INTERIOR_IN_MEMORY ;
- ACTIVATE_INTERIOR_ENTITY_SET ;
- REFRESH_INTERIOR ;
- FORCE_ROOM_FOR_ENTITY ;
- FORCE_ROOM_FOR_GAME_VIEWPORT.

Règles :

- téléporter avec une petite marge Z ;
- stabiliser collision et viewport ;
- éviter d'enfermer le joueur dans un intérieur non prêt ;
- garder un cooldown portail ;
- sauvegarder les portails ;
- nettoyer focus/HD area quand session terminée.

## 21. Logging runtime

Fichier log runtime :

DonJCustomNpcPlacer.log

Le logger doit :

- ne jamais crasher le mod ;
- sanitariser les noms ;
- chercher d'abord un dossier stable sous `Scripts`, puis les emplacements configurés et LocalAppData ;
- ne considérer le dossier de l'assembly/shadow-copy qu'après les emplacements stables ;
- écrire des messages courts ;
- garder stack trace utile en cas d'exception ;
- être utilisé pour erreurs importantes, pas pour chaque frame.

Le fichier :

DonJEnemySpawner.Logging.cs

contient les helpers.

Règle :

Si une erreur peut être utile pour debug mais ne doit pas casser le jeu, logger puis continuer proprement.

## 22. Collecte de bugs

Script :

tools\collect-bug-logs.ps1

Il collecte :

- logs GTA ;
- logs NIB ;
- logs ScriptHookV ;
- logs Scripts ;
- dernier journal DonJ historique trouvé dans le cache shadow-copy `.NET` sous `%LocalAppData%\assembly` ;
- DirectStorageFix.log ;
- menyooLog.txt ;
- MapEditor.log ;
- événements Application Windows ;
- état Git ;
- résumé ;
- manifest JSON ;
- entrée prête pour crash-list.md.

Commande type :

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-bug-logs.ps1 -Title "bug-limousine" -SinceHours 24

Avec dossier GTA forcé :

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-bug-logs.ps1 -Title "bug-limousine" -SinceHours 24 -GtaRoot "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced"

Chaque bug/crash doit être documenté dans :

crash-list.md

Chaque entrée doit contenir :

- date ;
- statut ;
- contexte ;
- symptôme ;
- sources vérifiées ;
- extraits utiles ;
- analyse/hypothèse ;
- action menée ;
- vérification ;
- résolution.

## 23. Tests

Projet tests :

tests\DonJEnemySpawner.Tests

Types de tests :

DonJEnemySpawnerTests.cs

Teste surtout :

- constantes stables ;
- contrats menu ;
- contrats Cartel ;
- contrats Ballas ;
- contrats escorte ;
- XML ;
- compatibilité hash ;
- structure attendue.

SafetySimulationTests.cs

Teste :

- scénarios simulés hors jeu ;
- comportements menu ;
- catégories, mémorisation de sélection et confirmation des nettoyages ;
- dispositions responsive/safe-zone et stabilité du pool UI ;
- garde-fous ;
- logique testable sans lancer GTA.

JusticeDomainTests.cs

Teste sans GTA :

- catalogue des infractions, preuves et circonstances ;
- calcul des points, amendes, peines, plafonds et récidive ;
- déduplication, remplacements de charges et machine d'états ;
- condamnations idempotentes et transitions de capture, détention, libération et évasion.

JusticePlayerProfilePersistenceTests.cs

Teste les contrats propres aux trois protagonistes :

- casiers, dossiers et activation isolés par slot dans le schéma v2, plus migration contrôlée des XML v1 ;
- mise en arrière-plan d'une incarcération stable avec snapshot d'inventaire conservé sur le bon héros ;
- horloge inactive, pause, reliquat, wrap, retour en cellule et libération à zéro différée ;
- reprise après chargement avec slot initialement inconnu et conservation des cooldowns.

JusticeRuntimeContractTests.cs

Teste par comportement, réflexion et inspection structurée :

- cadence runtime, buffers bornés et absence de scan global du monde ;
- témoins priorisés, corrélation wanted, mandats et absence d'écriture wanted pour les crimes ordinaires ;
- corruption, versions inconnues et contrats runtime communs de persistance Justice ;
- amendes sans plafond de gameplay, paiements, conversion en temps, activités, zones, snapshot d'armes et opérations idempotentes ;
- sept catégories, actions Justice, distinction héros joué/dossier consulté, compteurs d'agrégats, notifications GTA natives, mini-ligne de détention, stabilité des pools et confirmations d'amnistie/réinitialisation ;
- capture, discipline, évasion puis recapture sans double condamnation.

JusticeRuntimeEdgeContractTests.cs

Verrouille les fronts runtime les plus sensibles :

- consommation différée et bornée des dégâts GTA ;
- causalité fraîche des homicides et légitime défense ;
- délit de fuite différé, témoins partagés et file d'incidents priorisée ;
- renouvellement des générations lors de la réutilisation d'un handle.

JusticeCustodyHardeningTests.cs

Verrouille les corrections de crash et de perte de données :

- structure unmanaged réutilisable de 312 octets pour `GET_DLC_WEAPON_DATA` et libération en `finally` ;
- échec fermé sans confiscation si l'inventaire ne peut pas être lu exactement ;
- restitution fusionnée et durable, incluant composants, teinte, chargeur et arme sélectionnée ;
- résultats cash `Succeeded`, `Rejected`, `Unknown`, transition poste/prison et suppression policière persistée ;
- retour idempotent en cellule après respawn, libération hors écran différée au bon héros, enceinte complète de Bolingbroke et évasion à trois étoiles ;
- défense à mains nues uniquement après confiscation vérifiée et discipline sur nouveau dommage attribué ;
- absence de `RemoveAll` dans le chemin `OnAborted`.

JusticeEnginePersistenceRegressionTests.cs

Teste les régressions d'intégration :

- résolution des incidents en deux phases sans mutation concurrente ;
- identité canonique après mort ou transformation en ped custom ;
- amnistie précommittée et reprise idempotente ;
- condamnation active épinglée au-delà de 128 opérations ;
- reprise ciblée du wanted après réparation hors ligne.

JusticeStateRepairTests.cs

Teste `tools\repair-justice-state.ps1` sur des copies temporaires : sauvegardes et hashes, conservation du casier et de la récidive, effacement du seul état actif et refus d'un inventaire retiré/verrouillé/en reprise. Ces tests ne transforment pas l'outil ciblé en réparateur XML universel.

JusticeUiIntegrationObservabilityTests.cs

Teste le contrat HUD sans grande fenêtre hors F10, la mini-ligne de détention réservée au héros réellement joué, la distinction entre activation du héros joué et dossier consulté, le câblage sélecteur/paiement/réinitialisation, le comptage des faits représentés par les agrégats, les caches safe-zone/casier, l'ordre des chemins du logger et la collecte du dernier journal caché.

StubRuntimeBehaviorTests.cs

Teste le backend configurable du stub NIB v2. Celui-ci enregistre appels natives, wanted, dégâts, tâches, monde, inventaire et argent ; il expose aussi `InputArgument(ulong)` afin d'exercer le passage de pointeur x64 sans lancer GTA.

BugLogCollectionTests.cs

Teste :

- collecteur de logs ;
- génération bug-reports ;
- fallback logger ;
- scripts PowerShell.

JusticeAuditRemediationTests.cs

Teste par comportement et réflexion IL ciblée :

- migration d'un ancien verrou d'armes sans snapshot vers un état non destructif ;
- invariants de la machine d'état inventaire ;
- association des fronts différés à l'identité du protagoniste ;
- isolation d'un paiement ambigu dans `FineInDispute` ;
- distance au carré et budget de six incidents ;
- mode police par défaut ;
- consommation du résultat de sauvegarde finale à l'arrêt ;
- round-trip v2, autorité unique `Profiles` et rejet des falsifications de hash ou génération ;
- normalisation v2 vers le lecteur historique, raccordement repository/WAL et backup v1 distinct ;
- diagnostic F10 du build, du manifest, des révisions, du WAL et des métriques moyenne/p95/p99/max.

JusticeRepositoryTests.cs et JusticeWalRecoveryTests.cs

Testent les DTO métier profonds et immuables, l'enfilement `latest-wins`, la
convergence des révisions, les pannes atomiques, les transitions WAL, les bornes
de payload/champs, le refus de fragments XML, les queues tronquées, la compaction
sans transaction ouverte et l'absence de replay après perte d'acquittement.

PackagingSafetyTests.cs

Charge le binaire réellement packagé, compare les SHA-256 build/package/déploiement,
vérifie ses métadonnées et prouve qu'un package corrompu conserve le binaire installé.
Il vérifie aussi qu'un package local `sourceDirty=true` reste identifiable mais ne
peut jamais franchir `deploy-game-ready.ps1`.

RuntimeStageIsolationTests.cs

Vérifie l'isolation et le cooldown des stages du tick, l'ordre transactionnel de
l'arrêt, le nettoyage borné après erreur et l'absence de fallback de remplacement
qui supprimerait le primaire avant le nouveau fichier.

La matrice en jeu obligatoire est `docs/validation-justice-manuelle.md`. Un test
automatisé vert ne remplace pas cette validation GTA, notamment pour les natives,
les conflits avec trainers, les armes add-on et les frametimes en foule dense.

### 23.1 Traçabilité des correctifs JUS-003 à JUS-014

| ID | Contrat actuellement implémenté | Limite ou preuve requise |
|---|---|---|
| JUS-003 | Tous les chemins gameplay capturent des DTO typés profonds pour `Case`, `Record` et `Custody`, puis `JusticeRepository` `latest-wins` exécute XML, hashes, validation, I/O, relecture et retry sur son writer. `QueueJusticeStateCheckpoint` et `JusticeFlushStateNow` enfilent sans attendre le disque. Les frontières générales et financières attendent par polling que leur snapshot préparé soit durable; juste avant l'effet, le WAL financier écrit son plan immuable borné en `Prepared` puis `Attempted`. Seul `Stop()` attend au plus 2,5 s pendant `OnAborted`; le helper hors jeu réservé aux tests attend au plus 30 s. | Mesurer en jeu la durée de capture des DTO et les métriques de persistance au casier maximal. Les seules I/O synchrones du gameplay sont les petites frames WAL bornées à 1 024 octets et vingt champs avant un effet irréversible. |
| JUS-004 | `OnTick` exécute des `RuntimeTickStage` isolés avec cooldown de log. Un échec `JusticeEarly` interdit la progression tardive et appelle seulement `UpdateJusticeFailSafeMaintenance`. | Les injections stub et les essais GTA doivent prouver qu'aucun domaine défaillant n'affame les autres stages. |
| JUS-005 | L'inventaire utilise `None`, `CapturePending`, `SnapshotPersisted`, `RemovalPending`, `RemovedVerified`, `UnsupportedPreserved`, `RestorePending` et `RestoreAmbiguous`. Un snapshot nul ou incompatible conserve les armes, libère les contrôles et retente au plus trois fois. Un retour faux ou une exception après `RemoveAll` produit `EffectMayHaveApplied`, conserve la preuve et force une restauration ambiguë; la récupération manuelle fusionne uniquement un snapshot validé et ne supprime rien. | Les armes DLC/add-on, le faux négatif natif et une restitution partielle restent à valider en jeu avec les lignes INV de la matrice. |
| JUS-006 | Les modes police sont `Disabled`, `FreeroamBestEffort` par défaut et `Force`. Les mutations sont refusées pendant mission, cinématique, chargement ou changement de héros. | GTA v2 ne fournit pas ici de getter fiable de l'état global précédent. La remise aux valeurs vanilla supposées est donc best-effort; utiliser `Disabled` si un trainer possède le dispatch. |
| JUS-007 | Pendant une réparation du primaire, les fronts mort, arrestation et wanted sont mémorisés avec slot et modèle. Toute divergence ajoute `IdentityChanged` et ferme la reprise destructive. | La matrice PER-09 à PER-12 doit vérifier les transitions GTA réelles. |
| JUS-008 | Les étapes de `OnAborted` sont isolées. Justice restaure détention, inventaire, contrôles et police, vérifie le booléen de l'enfilement final, puis arrête le repository avec un délai de 2,5 s avant de laisser les autres domaines se nettoyer même après une erreur. | Un refus d'enfilement ou un arrêt hors délai laisse WAL/état sale récupérable et doit produire une preuve dans le log. |
| JUS-009 | La condamnation de la détention active reste épinglée lors du trim du casier et n'est libérée qu'au jugement d'un nouvel épisode de détention. | Tester plus de vingt condamnations et fautes disciplinaires avec DOM-09/DOM-10. |
| JUS-010 | Le format courant est `2.0`: une autorité `Profiles`, un `RuntimeRecovery`, générations, `payloadSha256`, `recoverySha256` et hash par profil. Le v1 n'est qu'une entrée de migration conservée exactement dans `_justice_state.v1.bak`. Le repository relit octets, SHA-256 et codec après remplacement. Une corruption strictement limitée à un profil inactif peut être isolée depuis un backup v2 entièrement valide tout en conservant le profil actif primaire, après preuve `recoverySha256` et double contrôle d'un WAL `Clean` sans transaction ouverte. | Toute corruption active/globale ou ambiguë, preuve absente, backup invalide, WAL corrompu, tronqué/réparé ou ouvert ferme l'isolation. Le backup entièrement valide reste alors le seul secours automatique; aucun fallback suppression/déplacement non atomique n'existe. |
| JUS-011 | Chaque débit lie un identifiant stable, un plan immuable et un snapshot `Prepared` durable à son slot, sa génération, son identité et son épisode avant d'écrire le WAL `Attempted` puis d'appeler le cash. `JusticePaymentResolution.Ambiguous` interdit tout replay, retire uniquement la somme non prouvée de `FineDue` et l'isole dans `FineInDispute`; elle n'augmente ni `VoluntaryFinePaid` ni la peine. Le terminal reste protégé jusqu'à ce que le backup ne puisse plus ressusciter `Prepared`. | GTA ne fournit pas d'identifiant transactionnel bancaire; le litige reste explicitement visible jusqu'à une politique de résolution future. |
| JUS-012 | Une passe capture au plus une requête peds et une véhicules, réutilise le snapshot, filtre par distance au carré et applique au plus six incidents confirmés par tick. Les accumulateurs exposent moyenne, p95, p99 et maximum, plus les compteurs d'entités, requêtes et file. | Les seuils d'acceptation sont ceux de la section Performance et doivent être mesurés en foule dense/faible FPS. |
| JUS-013 | Le package est créé depuis le build testé, hashé et décrit par manifest; le déploiement opt-in remplace et relit ENdll, PDB et manifest de façon transactionnelle avant de retirer les alias, avec rollback complet sur échec. Une source propre produit `sourceDirty=false`; l'option locale `-AllowDirtySource` marque au contraire un package non publiable, systématiquement refusé au déploiement. Les nouveaux tests privilégient codec, repository, WAL, pannes et binaire packagé; la matrice GTA exige une colonne résultat et une preuve. | Les tests headless ne prouvent pas les natives, le frametime ni les conflits avec trainers. Seul un manifest propre au schéma exact 2 peut être déployé; la matrice manuelle reste bloquante. |
| JUS-014 | L'extraction est progressive: états explicites d'inventaire et de paiement, DTO/codec/repository/WAL, snapshot monde/métriques et isolation runtime sont maintenant séparés. | `DonJEnemySpawner` reste une grande classe partielle; `JusticeEngine`, les contrôleurs inventaire/police/détention et l'adaptateur GTA complet ne sont pas encore extraits. Ne pas annoncer la refonte architecturale comme terminée. |

Règle :

Quand on change une constante stable, il faut mettre à jour le test correspondant.
Quand on ajoute une fonctionnalité testable hors jeu, il faut ajouter un test.
Ne jamais supprimer un test juste pour faire passer la suite.

## 24. Performance et stabilité

Le mod tourne en jeu. Il faut donc penser FPS et stabilité.

Règles strictes :

- OnTick léger.
- Pas de scans monde permanents.
- Pas d'ordres IA chaque frame.
- Pas de LINQ dans les chemins très fréquents si évitable.
- Pas de création d'objets inutiles à chaque tick.
- Pas d'écriture disque à chaque tick.
- Pas de thread qui touche GTA.
- Pas de téléportation visible sauf cas assumé et hors champ.
- Pas de suppression visible brutale si le joueur regarde.
- Toujours nettoyer handles, blips, dictionnaires.
- Au plus une requête monde peds et une véhicules par passe Justice.
- Six incidents confirmés au maximum par tick ; le surplus attend la passe suivante.
- Mesurer moyenne, p95, p99, maximum, entités scannées, requêtes monde, file
  d'incidents et durée de persistance avant toute décision de release Justice.

Bon pattern :

if (Game.GameTime < nextUpdateAt)
{
    return;
}

nextUpdateAt = Game.GameTime + intervalMs + jitter;

Pour les groupes :

- curseur de scan ;
- max N entités par passe ;
- cache menace ;
- cache dernière position ;
- cache dernière tâche ;
- retask seulement si target a changé ou délai expiré.

## 25. Blips et UI

Les blips doivent être :

- créés seulement si nécessaire ;
- supprimés au cleanup ;
- refresh avec intervalle ;
- pas recréés chaque frame ;
- cohérents par couleur/type.

Les messages joueur utilisent :

ShowStatus(...)

Règle :

Un message doit expliquer une action gameplay, pas spammer.

Exemples corrects :

Escorte haute sécurité appelée.
Monte à l'arrière avec F.
Waypoint introuvable.
Convoi en route.
Mode urgence activé.

## 26. Ce que le mod peut faire techniquement

Avec ScriptHook/NIBScriptHookVDotNet v2 et les natives GTA, le mod peut faire beaucoup de choses en solo :

Créer des PNJ.
Créer des véhicules.
Créer des objets.
Placer des entités précisément.
Donner des armes.
Ajouter des composants d'armes.
Changer santé/armure.
Créer des relations entre groupes.
Créer des blips.
Afficher des markers.
Afficher du texte/status.
Gérer des menus.
Lire les touches clavier.
Détecter téléphone ouvert.
Détecter waypoint.
Téléporter joueur/entités.
Demander modèles.
Supprimer entités.
Commander IA : marcher, suivre, combattre, entrer véhicule, conduire.
Créer drive-by.
Faire patrouiller.
Faire garder une zone.
Forcer des scénarios simples.
Sauvegarder/charger XML.
Lire/écrire fichiers.
Charger certains intérieurs.
Appeler des natives non exposées.
Gérer respawn automatique.
Créer des convois.
Nettoyer les entités hors champ.
Collecter logs côté dev.

Ce que le mod ne doit pas faire ou ne peut pas garantir proprement :

Fonctionner en GTA Online.
Utiliser des API v3 inexistantes côté runtime actuel.
Lire parfaitement tous les panneaux de vitesse.
Garantir une pathfinding parfaite dans tous les cas.
Contrôler la circulation GTA à 100 %.
Empêcher tous les bugs d'IA natifs.
Téléporter sous les yeux du joueur sans casser l'immersion.
Manipuler le monde GTA depuis un thread externe.
Faire confiance aux handles stockés.

## 27. Guide de modification pour Codex

Avant chaque modification :

1. Lire AGENTS.md.
2. Vérifier git status --short.
3. Ne jamais écraser les changements utilisateur.
4. Lire les fichiers concernés.
5. Identifier précisément le système touché.
6. Faire un changement limité au besoin.
7. Ajouter tests si possible.
8. Relire le code.
9. Lancer build/tests/safety si environnement disponible.
10. Signaler clairement ce qui n'a pas pu être vérifié.

Commandes utiles :

git status --short
rg "HighSecurityEscort" src
rg "Cartel" src
rg "EnemyRaid" src
rg "ShowStatus" src
dotnet build GTA5modDEV.sln -c Release
dotnet test GTA5modDEV.sln -c Release
.\tools\run-safety-checks.ps1

Pour un changement limousine :

Fichier principal :
src\DonJEnemySpawner\DonJEnemySpawner.HighSecurityEscort.cs

Lire avant modification :

- constantes HighSecurityEscort ;
- SpawnHighSecurityEscortConvoy ;
- UpdateHighSecurityEscortState ;
- OrderHighSecurityEscortArrivalToPlayer ;
- OrderHighSecurityConvoyToDestination ;
- CalculateHighSecurityFormationTarget ;
- CalculateHighSecurityArrivalTarget ;
- UpdateHighSecurityEscortCombat ;
- CommandHighSecurityEscortGuardEnterAssignedVehicle ;
- CleanupHighSecurityEscort ;

À protéger :

- place joueur dans limousine ;
- chauffeur ;
- mode arrivée ;
- mode route ;
- mode dismiss ;
- caches d'ordres véhicules ;
- handles gardes/véhicules ;
- blips ;
- anti-spam téléphone ;
- logs ;
- compatibilité API v2.

## 28. Style de code attendu

Commentaires :

- français ;
- clairs ;
- pas de roman inutile ;
- expliquer l'intention gameplay ;
- style existant : "Je ...".

Exemple :

// Je cadence l'ordre conducteur pour éviter de casser l'IA native à chaque frame.

Noms :

- garder les préfixes existants ;
- HighSecurityEscort... pour la limousine ;
- Cartel... pour le Cartel ;
- EnemyRaid... pour Ballas ;
- Interior... pour portails ;
- AdvancedInterior... pour chargement avancé.

Ne pas mélanger les systèmes. Exemple : ne pas mettre une logique limousine dans DonJEnemySpawner.cs si elle appartient à DonJEnemySpawner.HighSecurityEscort.cs.

## 29. Checklist avant livraison

Avant de livrer un patch :

- Le changement répond exactement à la demande.
- Pas de refactor hors sujet.
- Pas de fichier généré modifié inutilement.
- Pas de binaire modifié sauf demande explicite.
- Pas de vieux DonJEnemySpawner.ENdll réintroduit.
- Le build produit DonJCustomNpcPlacer.ENdll.
- Les tests passent ou l'échec est expliqué.
- Les limites sont honnêtement indiquées.
- Les risques en jeu sont expliqués.

Commandes finales idéales :

dotnet build GTA5modDEV.sln -c Release
dotnet test GTA5modDEV.sln -c Release
.\tools\run-safety-checks.ps1

Si l'environnement n'a pas GTA/NIB local :

.\tools\run-safety-checks.ps1 -UseStubApi

## 30. Prompt prêt à donner à Codex

Tu peux donner ce bloc à Codex avec le projet :

Tu travailles sur le projet GTA5modDEV / DonJ Custom NPC Placer.

Lis d'abord AGENTS.md, README.md et les fichiers source concernés avant toute modification.

Contexte cible :
- GTA V Enhanced Steam Windows x64.
- GTA5_Enhanced.exe 1.0.1158.13.
- ScriptHookV.dll 3889.0.1158.13 avec le chargeur Enhanced xinput1_4.dll 1.0.0.2.
- Runtime .NET côté jeu : NIBScriptHookVDotNet.asi + NIBScriptHookVDotNet2.dll 2.11.6.
- API cible : ScriptHookVDotNet API v2 via NIBScriptHookVDotNet2.dll.
- Ne pas utiliser API v3.
- Projet C# .NET Framework 4.8.
- Livrable chargé par le jeu : DonJCustomNpcPlacer.ENdll.
- Dossier scripts GTA : Grand Theft Auto V Enhanced\Scripts.

Architecture :
- src/DonJEnemySpawner/DonJEnemySpawner.cs : cœur du mod, état/actions du menu, placements, sauvegardes, PNJ, véhicules, objets, Cartel/Ballas.
- src/DonJEnemySpawner/DonJEnemySpawner.MenuUi.cs : rendu responsive de la console F10 Obsidienne, atelier d'armes, mini-ligne de détention et caches UI.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Domain.cs : domaine déterministe des crimes, preuves, sanctions, dossier et casier.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Profiles.cs : profils séparés Michael/Franklin/Trevor, activation canonique, sélection F10 et persistance.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Payment.cs : paiement volontaire durable et lié au bon protagoniste.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Persistence.*.cs, DonJEnemySpawner.Justice.Repository.cs et DonJEnemySpawner.Justice.Wal.cs : schéma v2, migration v1, DTO immuable, writer latest-wins et WAL critique.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.WorldSnapshot.cs : snapshot monde partagé, distance au carré et métriques.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Diagnostics.cs : build ID, comparaison SHA-256 au manifest, durabilité et métriques dans F10/log.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.cs : pont runtime, incidents en deux phases, témoins bornés/priorisés, lecture du wanted GTA, alliés et notifications natives.
- src/DonJEnemySpawner/DonJEnemySpawner.Justice.Custody.cs : poste, prison, identité canonique, transactions cash, inventaire exact, activités, discipline, libération et évasion.
- src/DonJEnemySpawner/DonJEnemySpawner.HighSecurityEscort.cs : limousine blindée, convoi haute sécurité, trajet VIP, combat convoi.
- src/DonJEnemySpawner/DonJEnemySpawner.Interiors.cs : portails d'intérieurs.
- src/DonJEnemySpawner/DonJEnemySpawner.Interiors.AdvancedLoading.cs : chargement avancé des intérieurs.
- src/DonJEnemySpawner/DonJEnemySpawner.InteriorCatalog.cs : catalogue intérieur.
- src/DonJEnemySpawner/DonJEnemySpawner.Logging.cs : logs runtime.
- tests/DonJEnemySpawner.Tests : tests MSTest.
- tools/run-safety-checks.ps1 : validation build/tests/livrable.
- tools/collect-bug-logs.ps1 : collecte logs bug/crash.
- tools/repair-justice-state.ps1 : récupération ciblée d'un état Justice v1 bloqué, jamais réparation XML universelle.

Règles :
- Ne jamais modifier hors sujet.
- Ne jamais écraser les changements utilisateur.
- Ne jamais manipuler Ped/Vehicle/Prop/Blip sans Entity.Exists.
- Ne jamais spammer TASK_* dans OnTick.
- Cadencer les IA avec Game.GameTime.
- Garder OnTick léger.
- Ne pas téléporter sous les yeux du joueur.
- Garder les commentaires en français.
- Ajouter ou ajuster les tests si un contrat stable change.
- Après modification, lancer dotnet build, dotnet test et si possible tools/run-safety-checks.ps1.
- Si une commande ne peut pas être lancée, expliquer précisément pourquoi.

Pour la limousine/convoi :
- Travailler principalement dans DonJEnemySpawner.HighSecurityEscort.cs.
- Garder la place joueur libre.
- Garder la conduite route-based.
- Spawn hors champ et propre.
- Pas de TP visible.
- Pas d'ordres véhicules à chaque frame.
- Préserver la conduite actuelle si elle fonctionne.
- Toute conduite agressive doit rester contrôlée et limitée au mode urgence/combat.
