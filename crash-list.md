# Crash List

Ce fichier conserve une trace ecrite de tous les crashs, erreurs, regressions et incidents observes pendant le developpement, la validation ou les tests en jeu.

## Regles
- Je cree une nouvelle entree pour chaque occurrence importante.
- Je n'efface pas l'historique.
- Si aucun log utile n'est trouve, je trace quand meme l'incident avec les chemins verifies.
- Si le meme probleme revient plus tard, je cree une nouvelle entree horodatee.

## Sources de logs prioritaires
- `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
- `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
- `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
- `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\*.log`
- Logs mod-specifiques si le contexte le justifie, par exemple `menyooLog.txt` ou `scripts\MapEditor.log`

## Format d'entree

```md
## 2026-04-19 08:42:11 +02:00 - Titre court de l'incident
- Statut: Ouvert
- Contexte: Commande lancee, action en jeu ou etape de reproduction.
- Symptome: Ce qui ne marche pas, message d'erreur, crash ou regression observee.
- Sources verifiees:
  - `chemin\\vers\\log-1`
  - `chemin\\vers\\log-2`
- Extraits utiles:
  - `log-source`: ligne ou resume court pertinent.
  - `log-source`: ligne ou resume court pertinent.
- Analyse / hypothese: Cause probable ou piste technique a investiguer.
- Action menee: Correctif applique, contournement ou etat de l'investigation.
- Verification: Build, tests, reproduction en jeu, ou constat d'absence de verification.
- Resolution: Resolu, non resolu, ou a revoir.
```

## Historique

## 2026-04-20 00:27:26 +02:00 - Echec des tests net48 apres passage sur l'API NIB
- Statut: Ferme
- Contexte: Execution de `dotnet test GTA5modDEV.sln -c Release` juste apres la mise a jour du pipeline de build/deploiement `.ENdll` pour GTA Enhanced.
- Symptome: Les tests unitaires echouent au demarrage avec `System.IO.FileNotFoundException` car `NIBScriptHookVDotNet2.dll` n'est pas present dans le dossier de sortie des tests.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\bin\Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\TestResults\Deploy_nodig 20260420T002726_31516`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `Impossible de charger le fichier ou l'assembly 'NIBScriptHookVDotNet2, Version=2.11.6.0'`.
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\bin\Release`: le dossier contient `DonJEnemySpawner.dll` et `DonJEnemySpawner.Tests.dll`, mais pas `NIBScriptHookVDotNet2.dll`.
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\TestResults\Deploy_nodig 20260420T002726_31516`: aucun log exploitable supplementaire n'a ete genere dans `In` / `Out`.
- Analyse / hypothese: Le projet principal doit conserver `Private=false` pour ne pas embarquer l'API dans le mod, mais le projet de tests avait herite de la meme politique alors que VSTest a besoin de la DLL API a l'execution.
- Action menee: J'ai modifie `tests\DonJEnemySpawner.Tests\DonJEnemySpawner.Tests.csproj` pour copier l'API v2 resolue dynamiquement dans la sortie de tests avec `Private=true`.
- Verification: Rebuild Release puis relance complete de `dotnet test GTA5modDEV.sln -c Release` apres correction.
- Resolution: Resolue.

## 2026-04-20 00:59:05 +02:00 - Faux positifs dans la nouvelle batterie de tests anti-regression
- Statut: Ferme
- Contexte: Execution de `dotnet build GTA5modDEV.sln -c Release` puis `dotnet test GTA5modDEV.sln -c Release` juste apres l'ajout de nouveaux tests unitaires et la mise a jour de `AGENTS.md` pour verrouiller l'etat stable du mod.
- Symptome: La build du projet de tests echoue d'abord sur des references de test (`System.Windows.Forms` et `WeaponHash`), puis la relance des tests met en evidence trois assertions trop fragiles ou trop strictes (`MenuToggleKey`, conversion de `WeaponHash`, normalisation du `ProjectReference`).
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `System.Windows.Forms` introuvable dans le projet de tests et `WeaponHash` non resolu.
  - `console dotnet test GTA5modDEV.sln -c Release`: `Attendu : <F10>, Reel : <121>` pour `MenuToggleKey`.
  - `console dotnet test GTA5modDEV.sln -c Release`: `System.OverflowException` pendant la conversion d'un `WeaponHash` vers `Int32`.
  - `console dotnet test GTA5modDEV.sln -c Release`: echec sur la recherche du `ProjectReference` du mod principal a cause d'une comparaison de chemin trop litterale.
- Analyse / hypothese: Les nouveaux tests protegeaient bien le contrat souhaite, mais certains verrous etaient couples a des details d'implementation du runner de tests ou du XML au lieu de verifier le contrat reel du projet.
- Action menee: J'ai retire la dependance inutile a `System.Windows.Forms` dans les tests, aligne `WeaponHash` sur `GTA.Native`, compare `MenuToggleKey` via sa valeur stable, reutilise `EnumToIntHash` pour les hashes d'armes, et normalise les chemins de `ProjectReference` avant comparaison.
- Verification: Nouvelle execution complete de `dotnet build GTA5modDEV.sln -c Release` puis `dotnet test GTA5modDEV.sln -c Release`, toutes deux reussies.
- Resolution: Resolue.

## 2026-04-20 02:50:58 +02:00 - Suite de tests obsolete apres ajout du comportement Allie et du placement persistant
- Statut: Ferme
- Contexte: Execution de `dotnet build GTA5modDEV.sln -c Release` puis verification de `dotnet test GTA5modDEV.sln -c Release --no-build` apres confirmation utilisateur que le mod fonctionne correctement avec la nouvelle version du script principal.
- Symptome: La suite de tests echoue sur l'ancien contrat du mod, avec une attente de `MenuItemCount = 8` et un cycle de comportements encore limite a `Static -> Attacker -> Neutral`, alors que le code en place expose maintenant `9` entrees de menu et un quatrieme comportement `Ally`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release --no-build`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release --no-build`: `Attendu : <8>, Reel : <9>` sur `StableConstants_KeepCurrentMenuAndSpawnBounds`.
  - `console dotnet test GTA5modDEV.sln -c Release --no-build`: `Attendu : <Static>, Reel : <Ally>` et `Attendu : <Neutral>, Reel : <Ally>` sur `CycleBehavior_WrapsAcrossStableBehaviorOrder`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: le code definit `MenuItemCount = 9`, ajoute les constantes du placement persistant, et etend `EnemyBehavior` avec `Ally`.
- Analyse / hypothese: Le mod etait sain, mais la suite de tests etait restee accrochee a l'ancienne topologie du menu et de l'enum de comportements. Les echecs provenaient donc d'attentes devenues obsoletes, pas d'une regression du runtime.
- Action menee: J'ai mis a jour `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs` pour aligner le contrat teste sur la version actuelle qui marche, puis j'ai ajoute des tests cibles pour les nouveaux labels de comportement, `NormalizeHeading`, le mapping des groupes de relation, et `CurrentModelKey` utilise par l'apercu de placement.
- Verification: `dotnet build GTA5modDEV.sln -c Release` reussi, puis `dotnet test GTA5modDEV.sln -c Release --no-build` reussi avec `59` tests verts. J'ai aussi confirme que la source de tests reference bien les nouvelles attentes avant la relance sequentielle.
- Resolution: Resolue.

## 2026-04-20 02:58:47 +02:00 - Crash en jeu apres action avec deux modes actifs simultanement
- Statut: Ferme
- Contexte: Investigation demandee juste apres un crash en jeu, l'utilisateur suspectant une action effectuee avec deux modes / mods actifs en meme temps dans GTA V Enhanced.
- Symptome: Arret / crash observe en jeu, sans message d'erreur exploitable remonte directement dans la conversation.
- Sources verifiees:
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\menyooLog.txt`
  - `C:\ProgramData\Microsoft\Windows\WER\ReportArchive`
  - `C:\ProgramData\Microsoft\Windows\WER\ReportQueue`
  - `journal Application Windows` via `Get-WinEvent`
- Extraits utiles:
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\ScriptHookV.log`: chargement propre de `Menyoo.asi`, `NativeTrainer.asi`, `NIBScriptHookVDotNet.asi` et `pc_trainer.asi`, puis creation des threads sans erreur explicite jusqu'a `02:25:44`.
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`: `Loading assembly DonJEnemySpawner.ENdll ...` puis `Started script DonJEnemySpawner.` a `02:25:43`, sans ligne `error`, `exception`, `fatal` ou `crash` ensuite.
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\asiloader.log`: chargement termine des plugins `.asi`, sans echec de chargement.
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\menyooLog.txt`: journal de demarrage / chargement de textures, sans erreur recente liee au crash.
  - `C:\ProgramData\Microsoft\Windows\WER\ReportArchive`: presence de rapports `AppCrash_GTA5_Enhanced.exe`, mais les plus recents dates du `2026-04-06`, donc pas de rapport correspondant au crash de maintenant.
  - `journal Application Windows` et `C:\ProgramData\Microsoft\Windows\WER\ReportQueue`: aucune entree recente exploitable mentionnant `GTA5_Enhanced.exe`, `NIBScriptHook`, `Menyoo` ou `ScriptHookV` pour cet incident precis.
- Analyse / hypothese: Les traces disponibles montrent un lancement propre des loaders et scripts, mais aucun log de crash recent n'a ete genere pour l'incident observe. L'hypothese la plus probable reste un conflit ou un etat invalide provoque par l'utilisation simultanee de deux modes / mods en jeu, sans preuve suffisante pour attribuer la faute a `DonJEnemySpawner` seul.
- Action menee: J'ai inspecte les logs prioritaires du jeu / loaders et les traces Windows recentes, puis j'ai documente l'incident sans modifier le code du mod.
- Verification: Les logs cites ci-dessus ont ete relus manuellement; aucune stacktrace ni rapport WER recent exploitable n'a ete trouve pour ce crash. Verification supplementaire du depot via `dotnet build GTA5modDEV.sln -c Release` puis `dotnet test GTA5modDEV.sln -c Release` apres mise a jour de ce journal.
- Resolution: A revoir. Aucun log de crash recent exploitable n'a ete trouve pour cet incident precis.

## 2026-04-20 05:47:39 +02:00 - Echec de validation du mod cause par une execution parallele build/test
- Statut: Ferme
- Contexte: Verification post-remplacement de `src\DonJEnemySpawner\DonJEnemySpawner.cs` avec lancement en parallele de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: La build `Release` du mod reussit, mais l'execution des tests echoue pendant une recompilation concurrente avec un verrou sur `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `DonJEnemySpawner -> C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\bin\Release\DonJEnemySpawner.dll` puis deploiement `.ENdll` reussi.
  - `console dotnet test GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en écriture ... because it is being used by another process.`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire cible par la recompilation est celui utilise simultanement par l'autre commande.
- Analyse / hypothese: L'echec n'indique pas une regression du code du mod mais un conflit de verrouillage introduit par l'execution simultanee de deux commandes .NET qui recompilent le meme projet en `Release`.
- Action menee: J'ai arrete la validation en parallele, j'ai consigne l'incident ici, puis j'ai prevu une relance sequentielle `build` puis `test` pour obtenir un resultat fiable.
- Verification: Relance sequentielle de `dotnet build GTA5modDEV.sln -c Release`, puis `dotnet test GTA5modDEV.sln -c Release` apres liberation du verrou.
- Resolution: Resolue. Incident d'outillage uniquement, sans anomalie fonctionnelle identifiee dans le code.

## 2026-04-20 23:24:00 +02:00 - Echec transitoire des tests cause par des libelles historiques desaccentues
- Statut: Ferme
- Contexte: Verification post-remplacement complet de `src\DonJEnemySpawner\DonJEnemySpawner.cs`, avec execution de `dotnet test GTA5modDEV.sln -c Release` apres ajout de nouveaux tests de couverture sur le menu de placement et la sauvegarde XML.
- Symptome: La suite de tests a echoue sur deux assertions de `BehaviorDisplayName`, car les libelles historiques retournaient `a` / `Allie` au lieu des formes accentuees attendues par le contrat de tests.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `Attendu : <Statique / hostile à vue>, Réel : <Statique / hostile a vue>`.
  - `console dotnet test GTA5modDEV.sln -c Release`: `Attendu : <Allié / garde défense>, Réel : <Allie / garde defense>`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: les methodes `NpcBehaviorDisplayName` et `BehaviorDisplayName` utilisaient des libelles ASCII pour ces deux cas.
- Analyse / hypothese: Le comportement du mod n'etait pas en regression fonctionnelle, mais la couche de compatibilite historique introduite dans le remplacement complet avait trop simplifie ces libelles, ce qui cassait le contrat textuel deja verrouille par les tests.
- Action menee: J'ai remis les libelles accentues dans `DonJEnemySpawner.cs` via des sequences Unicode C# (`\u00E0`, `\u00E9`) pour conserver un fichier source ASCII-safe tout en restituant les chaines exactes attendues.
- Verification: `dotnet build GTA5modDEV.sln -c Release` reussi, puis `dotnet test GTA5modDEV.sln -c Release --no-build` reussi avec `66` tests verts.
- Resolution: Resolue.

## 2026-04-21 03:02:17 +02:00 - Echec transitoire de `dotnet test` cause par une validation parallele
- Statut: Ferme
- Contexte: Verification de l'integration du contact telephone Cartel avec lancement simultane de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: La build `Release` a reussi, mais `dotnet test` a echoue pendant la recompilation avec un verrou d'ecriture sur `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `DonJEnemySpawner -> C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\bin\Release\DonJEnemySpawner.dll` puis deploiement `.ENdll` reussi.
  - `console dotnet test GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en ecriture ... because it is being used by another process.`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire vise etait partage entre les deux commandes .NET lancees en meme temps.
- Analyse / hypothese: L'echec provenait d'un conflit de verrouillage introduit par ma validation parallele, pas d'une regression fonctionnelle du mod ni de la suite de tests.
- Action menee: J'ai arrete la validation parallele, puis j'ai relance la verification de facon sequentielle avec `dotnet build GTA5modDEV.sln -c Release` suivi de `dotnet test GTA5modDEV.sln -c Release`.
- Verification: La relance sequentielle a reussi completement, avec build `Release` verte et `70` tests passes.
- Resolution: Resolue. Incident d'outillage uniquement.

## 2026-04-21 05:25:35 +02:00 - Echec transitoire de `dotnet test` cause par une validation parallele
- Statut: Ferme
- Contexte: Verification finale de la mise a jour ciblee du systeme Cartel, avec lancement simultane de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: La build `Release` a reussi, mais `dotnet test` a echoue pendant une recompilation concurrente avec un verrou d'ecriture sur `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawner.ENdll`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `DonJEnemySpawner -> C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\bin\Release\DonJEnemySpawner.dll` puis deploiement `.ENdll` reussi.
  - `console dotnet test GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en ecriture ... because it is being used by another process.`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire cible etait partage entre deux commandes .NET lancees en meme temps.
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawner.ENdll`: la DLL deploiée par la build `Release` avait bien ete regeneree avant la relance sequentielle.
- Analyse / hypothese: L'echec provenait uniquement du parallélisme de validation que j'ai lance, pas d'une regression fonctionnelle du code Cartel ni d'un probleme de pipeline MSBuild du projet.
- Action menee: J'ai stoppe la validation parallele, puis j'ai relance les commandes de facon sequentielle pour obtenir un resultat fiable.
- Verification: `dotnet build GTA5modDEV.sln -c Release` a reussi, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `71` tests verts.
- Resolution: Resolue. Incident d'outillage uniquement.

## 2026-04-22 00:43:38 +02:00 - Echec de build Release sur une API string non disponible en net48
- Statut: Ferme
- Contexte: Verification finale apres la mise en place de la correction Cartel supprimant la propulsion scriptée des Baller6.
- Symptome: `dotnet build GTA5modDEV.sln -c Release` a echoue pendant la compilation du projet de tests sur `DonJEnemySpawnerTests.cs`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawner.Tests.csproj`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs(108,20): error CS1501: Aucune surcharge pour la methode 'Contains' n'accepte les arguments 2`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawner.Tests.csproj`: le projet de tests cible `.NET Framework 4.8`.
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`: le nouveau test utilisait `string.Contains(..., StringComparison.Ordinal)`, API non disponible sur cette cible.
- Analyse / hypothese: L'echec venait de mon nouveau test anti-regression, pas du code du mod. La logique de verification etait correcte, mais l'API choisie n'etait pas compatible avec la cible historique du projet.
- Action menee: J'ai remplace cet appel par `IndexOf(..., StringComparison.Ordinal) >= 0`, compatible avec `.NET Framework 4.8`, puis j'ai prepare une nouvelle validation complete.
- Verification: Correction appliquee ; verification complete relancee juste apres cette entree.
- Resolution: Resolue.

## 2026-04-22 02:19:45 +02:00 - Echec transitoire de test sur une chaine native laissee dans un commentaire Cartel
- Statut: Ferme
- Contexte: Verification complete apres integration du correctif anti-pulsation des vehicules Cartel dans `src\DonJEnemySpawner\DonJEnemySpawner.cs`, avec execution de `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: La suite de tests a echoue sur `SourceFile_CartelNoLongerUsesForcedVehicleForwardSpeed`, alors que la logique executable n'appelait plus aucune propulsion scriptee.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `Echec de Assert.IsFalse. La logique Cartel ne doit plus reintroduire de propulsion scriptee de vehicule.`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: le commentaire du bloc `IssueCartelFastFollowOrder` contenait encore la chaine `SET_VEHICLE_FORWARD_SPEED`.
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`: le test historique controle toute la source et non uniquement les appels `Function.Call(...)`.
- Analyse / hypothese: L'echec venait d'un detail textuel que j'avais laisse dans un commentaire du bloc Cartel. Le comportement du code etait deja correct, mais la verification source du projet impose qu'aucune occurrence de cette chaine ne subsiste.
- Action menee: J'ai remplace les commentaires Cartel qui citaient encore les noms natifs exacts par des formulations fonctionnelles (`vitesse forcee`, `remise au sol native`) afin de conserver la verification voulue sans reintroduire de faux positif.
- Verification: `dotnet test GTA5modDEV.sln -c Release` relance juste apres correction et suite complete verte avec `77` tests passes.
- Resolution: Resolue.

## 2026-04-22 03:45:55 +02:00 - Echec transitoire de build Release cause par une verification parallele
- Statut: Ferme
- Contexte: Verification finale du correctif de tir Cartel avec lancement simultane de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: `dotnet build` a echoue sur un verrou d'ecriture de `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll` pendant que `dotnet test` recompilait et executait la solution.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en ecriture ... because it is being used by another process.`
  - `console dotnet test GTA5modDEV.sln -c Release`: build du mod, deploiement `.ENdll` et `83` tests reussis pendant la meme fenetre.
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`: deux processus `dotnet.exe` actifs au moment du diagnostic, coherents avec un conflit d'acces concurrent.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire cible etait partage par les deux commandes .NET lancees en meme temps.
- Analyse / hypothese: L'echec venait du parallelisme de ma verification locale, pas d'une regression du code Cartel ni du pipeline Release du projet.
- Action menee: J'ai consigne l'incident, puis j'ai bascule la verification finale sur une execution sequentielle des commandes `dotnet build` puis `dotnet test`.
- Verification: Relance sequentielle executee juste apres cette entree pour confirmer un resultat final fiable.
- Resolution: Resolue. Incident d'outillage uniquement.

## 2026-04-23 00:20:29 +02:00 - Echec transitoire de test sur une assertion source trop stricte pour les libelles interieurs
- Statut: Ferme
- Contexte: Verification complete apres l'integration des portails d'interieurs (`Entree` / `Sortie`) avec relance de `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: La suite de tests a echoue sur `SourceFiles_SaveLoadAndInteriorLabelsKeepPortalContract` alors que la build du mod etait deja verte.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.Interiors.cs`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\*.log`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `La chaîne ... ne contient pas la chaîne 'return "Retour au marqueur d'entree";'`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`: mon nouveau test cherchait la forme exacte `return "Retour au marqueur d'entree";`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.Interiors.cs`: le code utilise un ternaire et contient bien le libelle `"Retour au marqueur d'entree"` sans la forme `return` isolee.
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`, `ScriptHookV.log`, `asiloader.log`, `scripts\*.log`: aucun log exploitable consulte pour cet incident de tests hors jeu.
- Analyse / hypothese: L'echec venait uniquement d'une assertion source trop stricte dans le projet de tests, pas d'une regression fonctionnelle dans l'integration des portails interieurs.
- Action menee: J'ai assoupli l'assertion pour verifier la presence du libelle utile sans imposer une forme syntaxique precise, puis j'ai prepare une nouvelle relance complete.
- Verification: Correction appliquee ; verification complete relancee juste apres cette entree.
- Resolution: Resolue.

## 2026-04-23 02:38:15 +02:00 - Echec transitoire de build Release cause par une verification parallele
- Statut: Ferme
- Contexte: Verification finale apres la correction d'escalade d'hostilite des gardes Cartel, avec lancement simultane de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: `dotnet build` a echoue sur un verrou d'ecriture de `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll` pendant que `dotnet test` recompilait la solution.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en ecriture ... because it is being used by another process.`
  - `console dotnet test GTA5modDEV.sln -c Release`: build du mod, deploiement `.ENdll` et `95` tests reussis pendant la meme fenetre.
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`: deux processus `dotnet.exe` visibles, coherents avec un conflit d'acces concurrent sur les sorties intermediaires.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire cible correspond bien au fichier mentionne par l'erreur CS2012.
- Analyse / hypothese: L'echec venait du parallelisme de ma verification locale, pas d'une regression du correctif Cartel ni du pipeline Release du projet.
- Action menee: J'ai arrete la validation parallele, relance `dotnet build GTA5modDEV.sln -c Release` de facon sequentielle, puis relance `dotnet test GTA5modDEV.sln -c Release` pour obtenir un resultat final fiable.
- Verification: `dotnet build GTA5modDEV.sln -c Release` a reussi, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `95` tests verts.
- Resolution: Resolue. Incident d'outillage uniquement.

## 2026-04-23 04:01:04 +02:00 - Echec transitoire de compilation apres factorisation de la reapparition auto
- Statut: Ferme
- Contexte: Verification Release juste apres l'integration du patch de reapparition auto dans `src\DonJEnemySpawner\DonJEnemySpawner.cs` et l'ajout des tests associes dans `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`.
- Symptome: `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` ont echoue sur trois erreurs `CS0103` indiquant que `ped` n'existait plus dans `StartNpcRuntimeBehavior`.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\crash-list.md`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `DonJEnemySpawner.cs(2020,36): error CS0103: Le nom 'ped' n'existe pas dans le contexte actuel`
  - `console dotnet test GTA5modDEV.sln -c Release`: meme erreur de compilation reproduite avant l'execution des tests.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: le bloc factorise `StartNpcRuntimeBehavior` appelait encore `HoldStaticPosition(ped)`, `HoldGuardPosition(ped)` et `HoldAllyPosition(ped)` alors que la methode ne manipulait plus cette variable locale.
  - `C:\Users\nodig\GTA5modDEV\crash-list.md`: aucun incident ouvert en cours sur cette integration ; j'ai ajoute une nouvelle occurrence dediee comme le contrat le demande.
- Analyse / hypothese: L'echec venait de ma factorisation du demarrage de comportement NPC. J'avais correctement remplace la branche `switch` d'origine, mais j'avais laisse trois appels orphelins avec l'ancien identifiant local.
- Action menee: J'ai remplace ces trois appels par `spawned.Ped`, puis j'ai relance la verification de facon sequentielle pour eviter un faux negatif lie a un conflit de build parallele deja connu dans ce depot.
- Verification: `dotnet build GTA5modDEV.sln -c Release` a reussi sans avertissement, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `100` tests verts.
- Resolution: Resolue.

## 2026-04-24 03:11:36 +02:00 - Echec transitoire de test Release cause par une verification parallele
- Statut: Ferme
- Contexte: Verification finale apres l'application du correctif de maintenance passive Cartel, avec lancement simultane de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release`.
- Symptome: Le premier `dotnet test` a echoue sur `CS2012` en indiquant que `src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll` etait verrouille en ecriture par un autre processus.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `CSC : error CS2012: Nous ne pouvons pas ouvrir "C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release\DonJEnemySpawner.dll" en ecriture ... because it is being used by another process.`
  - `console dotnet build GTA5modDEV.sln -c Release`: la build Release du mod et du projet de tests s'est terminee avec succes pendant la meme fenetre.
  - `console Get-Process dotnet,VBCSCompiler -ErrorAction SilentlyContinue`: deux processus `dotnet.exe` etaient visibles, coherents avec un conflit d'acces concurrent sur les sorties intermediaires.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\obj\Release`: le binaire intermediaire mentionne par l'erreur correspond bien a la sortie Release du projet.
- Analyse / hypothese: L'echec venait de ma verification locale lancee en parallele, pas d'une regression fonctionnelle du correctif Cartel ni d'un probleme durable du pipeline Release.
- Action menee: J'ai relance la verification de facon sequentielle avec `dotnet build GTA5modDEV.sln -c Release`, puis `dotnet test GTA5modDEV.sln -c Release`.
- Verification: La build Release a reussi sans avertissement, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `102` tests verts.
- Resolution: Resolue. Incident d'outillage uniquement.

## 2026-04-24 03:37:10 +02:00 - Echec transitoire de test sur une assertion de normalisation trop stricte
- Statut: Ferme
- Contexte: Verification Release apres l'integration du correctif de sauvegarde persistante et l'ajout des tests anti-regression associes.
- Symptome: `dotnet test GTA5modDEV.sln -c Release` a echoue sur `NormalizeSaveFileName_RewritesUnsafeInput("bad:name","bad_name.xml")`.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `Attendu : <bad_name.xml>, Reel : <name.xml>`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: `NormalizeSaveFileName` applique bien `Path.GetFileName(raw)` avant de remplacer les caracteres interdits, comme dans le patch demande.
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`: mon test utilisait `bad:name`, cas que Windows traite comme un chemin avec lecteur plutot que comme un simple caractere invalide a remplacer.
- Analyse / hypothese: L'echec venait uniquement d'une attente de test mal choisie. Le correctif demande etait compile, mais l'assertion ne refletait pas le comportement Windows de `Path.GetFileName`.
- Action menee: J'ai remplace le cas `bad:name` par `bad*name` pour tester la sanitisation d'un vrai caractere invalide conserve dans le nom de fichier.
- Verification: Correction appliquee ; verification complete relancee juste apres cette entree.
- Resolution: Resolue. Incident de test uniquement.

## 2026-04-28 20:25:23 +02:00 - Echec transitoire de test apres refonte compacte du menu
- Statut: Ferme
- Contexte: Verification Release apres application du nouveau rendu compact du menu principal dans `src\DonJEnemySpawner\DonJEnemySpawner.cs`.
- Symptome: Le premier `dotnet test GTA5modDEV.sln -c Release` a echoue sur `SourceFile_MainMenuUsesCustomNpcPlacerVisualFrame`.
- Sources verifiees:
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\*.log`
- Extraits utiles:
  - `console dotnet test GTA5modDEV.sln -c Release`: `Echoue SourceFile_MainMenuUsesCustomNpcPlacerVisualFrame` puis `La chaine ... ne contient pas la chaine 'DrawText(TrainerSubtitle, x + 24, y + 42'.`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`: le test verifiait encore les coordonnees et dimensions de l'ancien rendu.
  - Logs GTA verifies par presence/date: les logs existants sont anciens et sans lien exploitable avec cet incident de test hors jeu.
- Analyse / hypothese: L'echec venait d'une assertion source obsolette apres remplacement volontaire du rendu UI, pas d'une regression de compilation ni d'un incident runtime GTA.
- Action menee: J'ai mis a jour le test pour proteger le nouveau contrat compact: `MainMenuCompactVisibleRowLimit`, clamp compact, panneau resume compact, carte de l'option selectionnee et metriques NPC/vehicules/objets/interieurs.
- Verification: `dotnet build GTA5modDEV.sln -c Release` a reussi sans avertissement, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `119` tests verts.
- Resolution: Resolue. Incident de test uniquement.

## 2026-04-30 00:26:27 +02:00 - Echec transitoire de verification apres ajout de la vague ennemie Ballas
- Statut: Ferme
- Contexte: Verification Release apres integration de la deuxieme couche IA telephone pour appeler une vague ennemie Ballas dans `src\DonJEnemySpawner\DonJEnemySpawner.cs`.
- Symptome: Un lancement parallele de `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` a d'abord verrouille `tests\DonJEnemySpawner.Tests\obj\Release\DonJEnemySpawner.Tests.dll`. Le test a ensuite echoue sur `SourceFile_CartelGroundingCallsStayLimitedToPlacementUpgradeAndRescueTeleport`, car il comptait globalement `SET_VEHICLE_ON_GROUND_PROPERLY` et ne distinguait pas encore les deux nouveaux appels vehicule Ballas.
- Sources verifiees:
  - `console dotnet build .\src\DonJEnemySpawner\DonJEnemySpawner.csproj -c Release`
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\*.log`
- Extraits utiles:
  - `console dotnet build GTA5modDEV.sln -c Release`: `CS2012 ... DonJEnemySpawner.Tests.dll ... because it is being used by another process`.
  - `console dotnet test GTA5modDEV.sln -c Release`: `Attendu : <3>, Reel : <5>. Le projet doit limiter SET_VEHICLE_ON_GROUND_PROPERLY...`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`: la vague ennemie ajoute volontairement un grounding a la configuration initiale du vehicule Ballas et un grounding a sa relocalisation de secours.
  - Logs GTA verifies par presence/date: les logs existants datent de 2025 et ne contiennent pas d'information exploitable pour cet incident de build/test hors jeu.
- Analyse / hypothese: Le verrouillage venait de ma verification locale lancee en parallele. L'echec de test venait d'une assertion source trop globale apres l'ajout volontaire de la couche vehicule Ballas, pas d'une regression du Cartel.
- Action menee: J'ai relance les validations en sequence, puis j'ai ajuste le test pour compter separement le placement initial, l'upgrade Cartel, la TP secours Cartel, la configuration vehicule Ballas et la relocalisation secours Ballas. J'ai aussi ajoute des tests du contrat Ballas: constantes, touche R telephone, bypass de l'IA generique, groupe hostile, blips rouges et SMG/drive-by.
- Verification: `dotnet build GTA5modDEV.sln -c Release` a reussi sans avertissement, puis `dotnet test GTA5modDEV.sln -c Release` a reussi avec `123` tests verts.
- Resolution: Resolue. Incident d'outillage et de test uniquement.

## 2026-05-01 02:34:56 +02:00 - Echec transitoire de test apres correction runtime du point bunker
- Statut: Ferme
- Contexte: Verification Release apres modification du point d'arrivee `bunker_generic` et ajout de la correction runtime des anciens portails bunker sauvegardes.
- Symptome: Le premier `dotnet test .\GTA5modDEV.sln -c Release` a echoue sur `SourceFiles_InteriorPortalsUseAdvancedLoadingAndSafeTeleport`.
- Sources verifiees:
  - `console dotnet test .\GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.Interiors.cs`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\MapEditor.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\menyooLog.txt`
- Extraits utiles:
  - `console dotnet test .\GTA5modDEV.sln -c Release`: `Echoue SourceFiles_InteriorPortalsUseAdvancedLoadingAndSafeTeleport` puis `ne contient pas la chaine 'bool prepared = PrepareInteriorForTeleportSafe(portal.Interior);'`.
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.Interiors.cs`: `EnterInteriorPortal` utilise maintenant `runtimeInterior`, ce qui est le nouveau comportement voulu pour corriger les anciens portails bunker.
  - Logs GTA verifies par presence/date: les logs existants datent de 2025 et ne contiennent pas d'information exploitable pour cet incident de test hors jeu.
- Analyse / hypothese: L'echec venait d'une assertion source obsolette qui verifiait encore l'appel direct a `portal.Interior` apres le changement volontaire vers `runtimeInterior`.
- Action menee: J'ai mis a jour le test pour proteger le nouveau contrat: preparation, teleportation et application des entity sets sur `runtimeInterior`.
- Verification: `dotnet build .\GTA5modDEV.sln -c Release` a reussi sans avertissement, puis `dotnet test .\GTA5modDEV.sln -c Release` a reussi avec `133` tests verts.
- Resolution: Resolue. Incident de test uniquement.

## 2026-05-02 04:17:35 +02:00 - Echec transitoire du collecteur de logs bugs pendant validation
- Statut: Ferme
- Contexte: Premiere execution de `tools\collect-bug-logs.ps1` apres ajout du systeme de recolte locale des logs.
- Symptome: PowerShell a refuse de parser le script a cause de backticks Markdown mal echappes dans les chaines de generation `summary.md` et `crash-list-entry.md`.
- Sources verifiees:
  - `console powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-bug-logs.ps1 -Title "test-collecteur" -SinceHours 24`
  - `C:\Users\nodig\GTA5modDEV\tools\collect-bug-logs.ps1`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260502-041545-test-collecteur\summary.md`
- Extraits utiles:
  - `console collect-bug-logs`: `Vous devez indiquer une expression de valeur apres l'operateur "-"` puis `Le terminateur " est manquant dans la chaine`.
  - `tools\collect-bug-logs.ps1`: les backticks Markdown sont maintenant doubles dans les chaines PowerShell, par exemple ````$reportRoot````.
  - `bug-reports\20260502-041545-test-collecteur\summary.md`: le rapport final contient `Logs copies: 10` et `Evenements Windows: copied`.
- Analyse / hypothese: L'echec venait uniquement de l'echappement PowerShell dans le nouveau script de collecte, pas du mod ni des logs GTA sources.
- Action menee: J'ai corrige l'echappement Markdown et la conversion des listes generiques dans `manifest.json`.
- Verification: `collect-bug-logs.ps1` a reussi, puis `run-safety-checks.ps1`, `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` ont tous reussi avec `144` tests verts.
- Resolution: Resolue. Incident de script uniquement.

## 2026-05-08 12:28:05 +02:00 - Echec de compilation pendant validation du patch escorte haute securite
- Statut: Ferme
- Contexte: Premiere execution de `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` apres integration du patch limite a `src\DonJEnemySpawner\DonJEnemySpawner.HighSecurityEscort.cs`.
- Symptome: La verification `build-release` a echoue avec `CS0103` sur une variable `vehicle` hors portee dans `FollowHighSecurityEscortGuardOnFoot`.
- Sources verifiees:
  - `console run-safety-checks.ps1`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260508-122729\logs\build-release.log`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.HighSecurityEscort.cs`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\*.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\menyooLog.txt`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\scripts\MapEditor.log`
- Extraits utiles:
  - `build-release.log`: `DonJEnemySpawner.HighSecurityEscort.cs(3272,50): error CS0103: Le nom 'vehicle' n'existe pas dans le contexte actuel`.
  - Logs GTA verifies par presence/date: les logs existants datent de 2025 ou du 2025-05-02 et ne contiennent pas d'information exploitable pour cet incident de compilation hors jeu.
- Analyse / hypothese: La garde qui empeche les occupants de la limousine de sortir a ete inseree par erreur dans `FollowHighSecurityEscortGuardOnFoot`, ou aucune variable `vehicle` n'existe.
- Action menee: J'ai retire cette garde du bloc de suivi a pied et je l'ai placee dans `CommandHighSecurityEscortGuardLeaveVehicle`, qui possede bien le parametre `Vehicle vehicle`.
- Verification: `run-safety-checks.ps1` a reussi, puis `dotnet build GTA5modDEV.sln -c Release` a reussi sans avertissement et `dotnet test GTA5modDEV.sln -c Release` a reussi avec `149` tests verts.
- Resolution: Resolue. Incident de compilation transitoire corrige pendant l'intervention.

## 2026-05-12 21:24:02 +02:00 - Echec de compilation pendant validation du verrou pickup limousine
- Statut: Ferme
- Contexte: Premiere execution de `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi` apres verrouillage de la limousine en attente pickup.
- Symptome: La verification `build-release` a echoue avec `CS0117` parce que `Hash.TASK_VEHICLE_TEMP_ACTION` n'existe pas dans l'API v2/stub cible.
- Sources verifiees:
  - `console run-safety-checks.ps1 -UseStubApi`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260512-212245\logs\build-release.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260512-212254-safety-failure\crash-list-entry.md`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.HighSecurityEscort.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `build-release.log`: `error CS0117: 'Hash' ne contient pas de definition pour 'TASK_VEHICLE_TEMP_ACTION'`.
  - `DonJEnemySpawner.HighSecurityEscort.cs`: deux appels d'action temporaire vehicule utilisaient l'enum `Hash` au lieu d'une constante native compatible v2.
  - `bug-reports\20260512-212254-safety-failure`: le collecteur a copie les logs GTA/loader, sans element runtime exploitable pour cet incident de compilation hors jeu.
- Analyse / hypothese: L'echec venait d'une native GTA non exposee dans l'enum `Hash` de l'API ciblee, pas du verrou pickup lui-meme.
- Action menee: J'ai ajoute `NativeTaskVehicleTempAction = 0xC429DCEEB339E129UL` et remplace les appels par `Function.Call((Hash)NativeTaskVehicleTempAction, ...)`, puis ajuste le test source associe.
- Verification: `run-safety-checks.ps1 -UseStubApi` a reussi, puis `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` ont reussi avec `151` tests verts.
- Resolution: Resolue. Incident de compilation transitoire corrige pendant l'intervention.

## 2026-05-14 12:58:38 +02:00 - Echec de compilation transitoire pendant ajout PersistentInSave
- Statut: Ferme
- Contexte: Premiere execution de `dotnet build GTA5modDEV.sln -c Release` apres ajout du filtre de sauvegarde des vehicules runtime de l'escorte haute securite.
- Symptome: La build a echoue avec `CS0117` et `CS1061` car le champ `PersistentInSave` avait ete ajoute dans le mauvais type interne.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260514-125848-build-persistentinsave`
- Extraits utiles:
  - `console dotnet build`: `DonJEnemySpawner.cs(3276,13): error CS0117: 'DonJEnemySpawner.PlacedVehicle' ne contient pas de definition pour 'PersistentInSave'`.
  - `console dotnet build`: `DonJEnemySpawner.cs(8438,33): error CS1061: 'DonJEnemySpawner.PlacedVehicle' ne contient pas de definition pour 'PersistentInSave'`.
- Analyse / hypothese: Le patch avait insere le champ dans `SpawnedNpc`, qui partage les champs `AutoRespawn`, `RespawnPending`, `RespawnEligibleAt` et `NextRespawnCheckAt` avec `PlacedVehicle`.
- Action menee: J'ai retire le champ du type `SpawnedNpc` et je l'ai ajoute dans la classe interne `PlacedVehicle`, puis j'ai relance la validation.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont tous reussi avec `157` tests verts.
- Resolution: Resolue. Incident de compilation transitoire corrige pendant l'intervention.

## 2026-05-23 23:13:30 +02:00 - Echec transitoire de tests pendant refonte UI du menu F10
- Statut: Ferme
- Contexte: Validation de la refonte UI du menu F10 apres ajout de la navigation par sections, du resume contextuel et des tests headless associes.
- Symptome: La premiere suite `run-safety-checks.ps1 -UseStubApi` a echoue sur `HeadlessMainMenuSimulation_LeftRightOpenAndCloseSection` avec `AmbiguousMatchException`. Le premier `dotnet test GTA5modDEV.sln -c Release` standard a ensuite echoue sur le meme test car `ChangeMainMenuValue` appelait `IsShiftHeld()` avant de savoir si la ligne selectionnee avait besoin du pas rapide.
- Sources verifiees:
  - `console powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260523-231011\logs\test-release.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260523-231041-safety-failure`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\SafetySimulationTests.cs`
- Extraits utiles:
  - `test-release.log`: `System.Reflection.AmbiguousMatchException: Correspondance ambiguë trouvée` sur `InvokeInstance`.
  - `dotnet test`: `System.IO.FileNotFoundException: Impossible de charger le fichier ou l'assembly 'NIBScriptHookVDotNet, Version=3.9.0.0'` via `GTA.Game.IsKeyPressed(Keys key)`.
  - `DonJEnemySpawner.cs`: le calcul du pas rapide a ete deplace dans `GetMainMenuFastStep()` et n'est plus appele pour ouvrir/fermer une section.
- Analyse / hypothese: Le premier echec venait du helper de reflection des tests qui ne distinguait pas les overloads prives. Le second venait d'un appel trop precoce a l'API GTA dans un chemin headless, pas d'une regression runtime du menu.
- Action menee: J'ai fait choisir au helper de test la surcharge par nombre d'arguments, puis j'ai rendu le calcul `Shift` paresseux dans `ChangeMainMenuValue` uniquement pour les reglages qui utilisent un pas rapide.
- Verification: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi`, `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` ont reussi avec `161` tests verts.
- Resolution: Resolue. Incident de test headless corrige pendant l'intervention.

## 2026-05-24 15:51:58 +02:00 - Echec de compilation transitoire pendant ajout du mode Terminator
- Statut: Ferme
- Contexte: Premiere execution de `dotnet build GTA5modDEV.sln -c Release` apres ajout de `DonJEnemySpawner.TerminatorMode.cs` et des hooks menu/tick/HUD.
- Symptome: La build a echoue avec trois incompatibilites API v2: lecture de `Ped.IsEnemy`, appel `Entity.ClearLastWeaponDamage()` absent, et lecture de `Ped.Speed` absente.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `tests\DonJEnemySpawner.Tests\SafetySimulationTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs(507,26): error CS0154: Impossible d'utiliser la propriete ou l'indexeur 'Ped.IsEnemy' dans ce contexte, car il lui manque l'accesseur get`.
  - `DonJEnemySpawner.TerminatorMode.cs(850,20): error CS1061: 'Entity' ne contient pas de definition pour 'ClearLastWeaponDamage'`.
  - `DonJEnemySpawner.TerminatorMode.cs(958,28): error CS1061: 'Ped' ne contient pas de definition pour 'Speed'`.
- Analyse / hypothese: Le code fourni ciblait des membres non lisibles ou absents dans l'API ScriptHookVDotNet v2 disponible localement, alors que le projet doit rester compatible avec NIB/SHVDN2.
- Action menee: J'ai remplace la lecture `Ped.IsEnemy` par `HasHostileRelationshipToProtectedPed(ped, player)`, garde le nettoyage des degats via `CLEAR_ENTITY_LAST_DAMAGE_ENTITY`, et remplace `Ped.Speed` par la native `GET_ENTITY_SPEED`.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue. Incident de compilation transitoire corrige pendant l'intervention.

## 2026-05-24 17:05:45 +02:00 - Vie bloquee a 2000 HP en mode Terminator
- Statut: Ferme
- Contexte: Test en jeu du mode Terminator apres ajout du filtre T-800 et de la resistance joueur.
- Symptome: En mode Terminator, la vie restait bloquee a `2000` et le joueur devenait immortel au lieu d'etre tres resistant mais tuable sous bombardement.
- Sources verifiees:
  - `tools\collect-bug-logs.ps1 -Title "bug-terminator-health-locked" -SinceHours 6`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-170325-bug-terminator-health-locked`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-170325-bug-terminator-health-locked\summary.md`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\ScriptHookV.log`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs`: le bloc `if (SafeGetPedHealth(player) < TerminatorMinHealth) { SafeSetPedHealth(player, TerminatorMinHealth); }` s'executait dans `ApplyTerminatorModeToPlayer` a chaque tick.
  - `summary.md`: 10 logs GTA/loader copies; aucun extrait runtime ne pointe vers un crash, le symptome vient de la logique source de regeneration.
- Analyse / hypothese: La sante etait traitee comme un minimum permanent au lieu d'un boost initial avec regeneration lente. En plus, l'armure etait remplie instantanement des qu'elle passait sous le seuil, ce qui renforcait l'effet immortel.
- Action menee: J'ai limite le soin a 2000 HP a l'activation, ajoute un suivi de degats, une regeneration lente de vie apres delai, et une regeneration d'armure par paliers au lieu d'un remplissage instantane a chaque tick. J'ai ajoute des tests source pour interdire le retour du verrou `HP = 2000`.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue cote code. A valider en jeu sous tirs lourds/explosifs pour ajuster les valeurs de regeneration si necessaire.

## 2026-05-24 17:17:59 +02:00 - Barres sombres haut/bas dans le HUD Terminator
- Statut: Ferme
- Contexte: Test visuel en jeu du HUD Terminator en premiere personne.
- Symptome: Deux bandes sombres plein ecran apparaissaient en haut et en bas de l'image, ce qui assombrissait la vue et genait la lisibilite.
- Sources verifiees:
  - Capture utilisateur du HUD en jeu.
  - `tools\collect-bug-logs.ps1 -Title "bug-terminator-hud-dark-bars" -SinceHours 3`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-171619-bug-terminator-hud-dark-bars`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-171619-bug-terminator-hud-dark-bars\summary.md`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs`: `DrawRect(0, 0, TerminatorHudWidth, 60, Color.FromArgb(118, 12, 0, 0));`.
  - `DonJEnemySpawner.TerminatorMode.cs`: `DrawRect(0, TerminatorHudHeight - 58, TerminatorHudWidth, 58, Color.FromArgb(112, 12, 0, 0));`.
  - `summary.md`: 10 logs GTA/loader copies; aucun crash runtime associe, bug visuel cause par le rendu HUD.
- Analyse / hypothese: Les deux rectangles HUD avaient ete ajoutes pour cadrer l'affichage, mais ils formaient des barres noires trop presentes et masquaient inutilement la vue.
- Action menee: J'ai supprime les deux rectangles plein ecran haut/bas et ajoute un test source pour empecher leur retour.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue cote code. A valider en jeu sur la meme scene pour confirmer que seules les donnees HUD utiles restent visibles.

## 2026-05-24 16:41:02 +02:00 - Echec de compilation transitoire pendant refonte impact/HUD Terminator
- Statut: Ferme
- Contexte: Premiere execution de `dotnet build GTA5modDEV.sln -c Release` apres remplacement complet de `DonJEnemySpawner.TerminatorMode.cs` pour corriger l'effet telekinesie et refondre la vision T-800.
- Symptome: La build a echoue avec `CS0102` parce que le nouveau fichier redeclarait `NativeGetSelectedPedWeapon`, deja defini dans la classe partial principale.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs(60,25): error CS0102: Le type 'DonJEnemySpawner' contient deja une definition pour 'NativeGetSelectedPedWeapon'`.
  - `DonJEnemySpawner.cs`: `private const ulong NativeGetSelectedPedWeapon = 0x0A6DB4965674D243UL;`.
- Analyse / hypothese: Le fichier partial partage le meme type C# que le fichier principal; la constante native fournie etait donc en conflit avec la constante existante.
- Action menee: J'ai retire la redeclaration dans le partial Terminator et conserve l'utilisation de la constante native existante du fichier principal.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue. Incident de compilation transitoire corrige pendant l'intervention.

## 2026-05-24 17:24:52 +02:00 - Vision Terminator trop sombre la nuit
- Statut: Ferme
- Contexte: Test en jeu du mode Terminator en zones sombres ou de nuit.
- Symptome: La vision rouge restait trop dependante de l'eclairage ambiant; dans l'obscurite, le joueur pouvait encore etre gene par le manque de lumiere.
- Sources verifiees:
  - `tools\collect-bug-logs.ps1 -Title "bug-terminator-night-vision-dark" -SinceHours 3`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-172128-bug-terminator-night-vision-dark`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-172128-bug-terminator-night-vision-dark\summary.md`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs`: `TryCallNative(NativeSetTimecycleModifier, "REDMIST_blend");` appliquait le rendu rouge, mais sans assistance native de basse lumiere.
  - `summary.md`: 10 logs GTA/loader copies; aucun crash runtime associe, le probleme est un ajustement visuel de gameplay.
- Analyse / hypothese: Le timecycle rouge donne l'identite visuelle T-800, mais ne garantit pas une meilleure lisibilite en scene sombre. La native `SET_NIGHTVISION` est plus adaptee pour compenser l'obscurite, a condition de la limiter a la vue Terminator en premiere personne.
- Action menee: J'ai ajoute l'activation de `SET_NIGHTVISION` avec le filtre Terminator, sa coupure en sortie de premiere personne ou a la desactivation du mode, et j'ai reduit l'opacite du filtre rouge pour garder une image plus lisible.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue cote code. A valider en jeu de nuit pour confirmer que le rendu reste rouge et lisible sans etre trop clair.

## 2026-05-24 17:34:53 +02:00 - Tir au contact declenche la propulsion Terminator
- Statut: Ferme
- Contexte: Test en jeu du mode Terminator contre un vehicule ou un PNJ lorsque le joueur est colle ou presque a la cible.
- Symptome: En tirant sur une cible a tres courte distance, le script pouvait la propulser comme si un coup de poing avait ete porte.
- Sources verifiees:
  - `tools\collect-bug-logs.ps1 -Title "bug-terminator-close-shot-propels-target" -SinceHours 3`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-173117-bug-terminator-close-shot-propels-target`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260524-173117-bug-terminator-close-shot-propels-target\summary.md`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `src\DonJEnemySpawner\DonJEnemySpawner.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs`: `HasFreshTerminatorMeleeImpact` utilisait `HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY` pour confirmer l'impact, ce qui peut aussi etre vrai apres un tir du joueur.
  - `DonJEnemySpawner.TerminatorMode.cs`: la proximite et `AreEntitiesTouching(player, target)` rendaient le cas visible quand le joueur etait colle a la cible.
  - `summary.md`: 10 logs GTA/loader copies; aucun crash runtime associe, le probleme vient de la logique source de classification des degats.
- Analyse / hypothese: La confirmation "endommagé par le joueur" etait correcte pour eviter la telekinesie, mais pas suffisante pour distinguer une balle tiree au contact d'un vrai coup de melee.
- Action menee: J'ai ajoute une fenetre de blocage apres tir du joueur, un nettoyage des flags de degats proches pendant cette fenetre, et un garde-fou qui n'accepte l'etat melee generique que si l'arme selectionnee est compatible melee. Les commandes de melee directes restent acceptees pour garder le comportement voulu quand le joueur frappe vraiment.
- Verification: `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue cote code. A valider en jeu en tirant au contact puis en donnant un vrai coup pour verifier les deux chemins.

## 2026-05-24 17:34:53 +02:00 - Echecs transitoires pendant correction tir/impact Terminator
- Statut: Ferme
- Contexte: Verification locale apres ajout du blocage des tirs proches dans `DonJEnemySpawner.TerminatorMode.cs`.
- Symptome: Une premiere build a echoue avec `CS0221`, puis une execution de tests a echoue sur une assertion source trop stricte.
- Sources verifiees:
  - `console dotnet build GTA5modDEV.sln -c Release`
  - `console dotnet test GTA5modDEV.sln -c Release`
  - `src\DonJEnemySpawner\DonJEnemySpawner.TerminatorMode.cs`
  - `tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
- Extraits utiles:
  - `DonJEnemySpawner.TerminatorMode.cs(768,46): error CS0221: Impossible de convertir la valeur de constante '2725352035' en 'int'`.
  - `SourceFiles_TerminatorModeIsIsolatedAndHookedIntoMenuTickHudAndShutdown`: `Un tir recent doit bloquer la fenetre de propulsion avant toute detection de melee`.
- Analyse / hypothese: `WeaponHash.Unarmed` depasse la plage constante signee de `int` et doit etre compare avec `unchecked`. Le test utilisait le bloc `UpdateTerminatorMode` au lieu du bloc `UpdateTerminatorPunchPower`.
- Action menee: J'ai corrige la comparaison avec `unchecked((int)WeaponHash.Unarmed)` et ajuste l'assertion de test pour chercher l'ordre dans le source Terminator complet.
- Verification: Les relances de `dotnet build GTA5modDEV.sln -c Release`, `dotnet test GTA5modDEV.sln -c Release` et `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1` ont reussi avec `163` tests verts.
- Resolution: Resolue. Incidents transitoires corriges pendant l'intervention.

## 2026-08-24 01:08:06 +02:00 - Echec NuGet sous sandbox pendant la validation avec stub
- Statut: Ferme
- Contexte: Premiere execution de `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi` avant la preparation du nouveau livrable DonJ.
- Symptome: La restauration du projet de tests a echoue avec `NU1301` lors de l'acces a `https://api.nuget.org/v3/index.json`.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-010747\logs\restore.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-010747\safety-failure.txt`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-010806-safety-failure\summary.md`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-013147\logs\restore.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-013147\logs\build-release.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-013147\logs\test-release.log`
- Extraits utiles:
  - `restore.log`: `error NU1301: Impossible de charger l'index de service pour la source https://api.nuget.org/v3/index.json`.
  - `restore.log`: `Une tentative d'acces a un socket de maniere interdite par ses autorisations d'acces a ete tentee. (api.nuget.org:443)`.
  - `summary.md`: cinq anciens logs GTA Legacy ont ete copies; aucun log runtime Enhanced n'etait exploitable pour cet echec de restauration hors jeu.
- Analyse / hypothese: L'echec venait du blocage reseau de la sandbox vers NuGet, pas du code DonJ, du stub ni d'une version de package invalide.
- Action menee: J'ai collecte le rapport d'incident, puis relance la meme suite avec l'acces reseau autorise, sans modifier les versions des dependances.
- Verification: La restauration suivante a reussi; `run-safety-checks.ps1 -UseStubApi` a ensuite produit une build avec zero erreur et zero avertissement, valide le contrat `.ENdll` et execute `164` tests avec zero echec.
- Resolution: Resolue. Incident d'environnement isole apres autorisation de l'acces NuGet necessaire.

## 2026-08-24 01:08:46 +02:00 - Constante GET_ENTITY_MODEL absente du stub NIB v2
- Statut: Ferme
- Contexte: Deuxieme execution de `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi`, apres resolution de l'acces NuGet.
- Symptome: La build Release a echoue avec `CS0117` parce que l'enum `GTA.Native.Hash` du stub de test ne declarait pas `GET_ENTITY_MODEL`, pourtant utilisee par le code runtime et exposee par l'API NIB v2 reelle.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-010832\logs\build-release.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-010846-safety-failure\summary.md`
  - `C:\Users\nodig\GTA5modDEV\tools\Stubs\NIBScriptHookVDotNet2\StubApi.cs`
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\DonJEnemySpawnerTests.cs`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-013147\logs\build-release.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-013147\logs\test-release.log`
- Extraits utiles:
  - `build-release.log`: `DonJEnemySpawner.TerminatorMode.cs(1648,44): error CS0117: 'Hash' ne contient pas de definition pour 'GET_ENTITY_MODEL'`.
  - `StubApi.cs`: la constante de test est maintenant `GET_ENTITY_MODEL = 0x9F47B058362C84B5`.
  - `DonJEnemySpawnerTests.cs`: `NativeHash_GetEntityModelKeepsExpectedValue` protege desormais cette valeur exacte.
- Analyse / hypothese: Le banc de tests etait en retard sur le contrat de l'API NIB v2 installee. Le code runtime et son interface publique n'etaient pas en cause.
- Action menee: J'ai ajoute uniquement la constante manquante au stub et un test de contrat ciblant sa valeur `ulong`; je n'ai modifie ni le comportement runtime ni l'interface publique de DonJ.
- Verification: `run-safety-checks.ps1 -UseStubApi` a reussi avec zero erreur, zero avertissement et `164/164` tests verts; la sortie validee est bien `DonJCustomNpcPlacer.ENdll` et aucune DLL du stub n'a ete deployee dans GTA.
- Resolution: Resolue. Le stub de test est aligne avec la native NIB v2 reelle.

## 2026-08-24 01:47:47 +02:00 - Erreurs de syntaxe PowerShell pendant les controles preparatoires
- Statut: Ferme
- Contexte: Deux commandes de lecture preparatoires, l'une pour l'inventaire initial et l'autre pour le controle des signatures/version des DLL, ont ete composees avec un pipe place directement apres un bloc `foreach`.
- Symptome: PowerShell a retourne un `ParserError` avant d'executer chaque commande; aucun inventaire ni controle de signature n'a ete produit lors de ces deux premieres tentatives.
- Sources verifiees:
  - `console PowerShell` de la commande d'inventaire initiale
  - `console PowerShell` de la commande de controle des signatures et versions
  - sorties des deux commandes read-only corrigees
- Extraits utiles:
  - `console PowerShell`: le parseur signalait le caractere `|` place immediatement apres la fermeture du bloc `foreach`.
  - Relances corrigees: les tableaux d'inventaire et les metadonnees de signature/version ont ete produits sans nouvelle erreur.
- Analyse / hypothese: Il s'agissait d'une erreur de composition de pipeline PowerShell; le resultat du statement `foreach` devait d'abord etre affecte ou encapsule avant d'etre envoye a `Format-Table`.
- Action menee: J'ai reformule les deux commandes en stockant le resultat du `foreach` dans une variable dediee, puis en appliquant le pipe dans une instruction separee.
- Verification: Les deux commandes corrigees se sont terminees avec un code de sortie nul et ont permis de poursuivre l'audit. Elles etaient strictement en lecture seule et n'ont modifie aucun fichier GTA ou du depot.
- Resolution: Resolue. Incident limite aux commandes de diagnostic; aucun log GTA n'etait pertinent ou disponible pour une erreur de parsing survenue avant execution.

## 2026-08-24 01:40:29 +02:00 - Telechargement Script Hook V retourne en HTML puis corrige
- Statut: Ferme
- Contexte: Telechargement en staging de Script Hook V officiel `v3889.0` pour GTA V Enhanced `1.0.1158.13` depuis `https://www.dev-c.com/gtav/scripthookv/`.
- Symptome: Le clic de telechargement dans le navigateur a d'abord expire derriere la fenetre de consentement aux cookies, puis une requete directe sans en-tete `Referer` a enregistre une page HTML de `10 851` octets au lieu de l'archive ZIP.
- Sources verifiees:
  - `https://www.dev-c.com/gtav/scripthookv/`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\downloads\ScriptHookV_3889.0_1158.13.invalid.html`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\downloads\ScriptHookV_3889.0_1158.13.zip`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\packages\ScriptHookV\bin\ScriptHookV.dll`
- Extraits utiles:
  - Fichier invalide preserve: type de contenu HTML, taille `10 851` octets; il n'a jamais ete traite comme une archive ou copie dans GTA.
  - Archive valide: taille `1 722 210` octets, SHA-256 `B64C97C3353906F14621E7E9511E4AEC2A7D436ECC21ED124D3816585E2E6188`.
  - `ScriptHookV.dll`: version `3889.0.1158.13`, SHA-256 `126BE57CA9DCA00E471F9DE611766009C900AAD88F89AE6C2926EC62E5AAEB4F`.
- Analyse / hypothese: La page officielle protege le telechargement contre les requetes sans contexte de navigation; le consentement bloquait le clic et l'absence de `Referer` a renvoye la page web au lieu du binaire.
- Action menee: J'ai accepte le consentement fonctionnel, relance le telechargement avec le `Referer` officiel attendu, conserve le HTML invalide comme preuve et extrait l'archive uniquement apres controle.
- Verification: La taille, le SHA-256, la structure ZIP et la version interne correspondent au package officiel vise. A ce stade, aucun fichier Script Hook V n'avait encore ete copie dans la racine GTA.
- Resolution: Resolue. Le package officiel correct est present et verifie dans le staging.

## 2026-08-24 01:33:45 +02:00 - Echec du premier controle vanilla lance hors du bouton Steam
- Statut: Ouvert
- Contexte: Premier lancement de controle de GTA V Enhanced vanilla avant toute installation de mod. La tentative a ete initiee par `PlayGTAV.exe`, puis une relance a ete demandee avec l'option Steam `-nobattleye`.
- Symptome: Le lancement direct a affiche une demande d'installation BattlEye parce qu'il ne reprenait pas l'option Steam; apres annulation de cette demande, Rockstar Games Launcher a affiche `Grand Theft Auto V Version amelioree a quitte inopinement`.
- Sources verifiees:
  - capture utilisateur `C:\Users\nodig\AppData\Local\Temp\codex-clipboard-f147a5a1-0239-4ae1-848b-e4a63edc4f5d.png`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-013343-bug-report\summary.md`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-013343-bug-report\manifest.json`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-013343-bug-report\windows-events\application-events.json`
  - inventaire de `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced`
- Extraits utiles:
  - Capture Rockstar: `Grand Theft Auto V Version amelioree a quitte inopinement` avec les choix `Reessayer` et `Mode sans echec`.
  - `summary.md`: aucun log ScriptHookV, NIB, ASI loader, DirectStorageFix ou Menyoo actuel n'existait sous la racine Enhanced vanilla.
  - `manifest.json`: les cinq fichiers collectes provenaient uniquement de l'ancienne installation GTA V Legacy et dataient de 2025; ils ne permettent pas d'expliquer ce lancement Enhanced.
  - `application-events.json`: aucun evenement cible GTA/Rockstar exploitable n'a ete trouve dans la collecte.
- Analyse / hypothese: Aucun mod n'etait present ou charge, donc cet echec ne peut pas provenir de la reinstallation projetee. Le lancement hors du parcours Steam a au minimum explique l'invite BattlEye; le message Rockstar restant doit etre valide par un vrai lancement depuis Steam avant de conclure.
- Action menee: J'ai annule l'installation BattlEye, n'ai copie aucun fichier dans GTA, ai collecte le rapport `20260824-013343-bug-report`, puis ai suspendu l'installation jusqu'au controle vanilla depuis Steam.
- Verification: L'inventaire apres incident confirmait une racine Enhanced encore vanilla, sans `Scripts`, `mods`, chargeur ASI ou fichier de cache restaure.
- Resolution: Non resolue au moment de cette entree. Je ne tire aucune conclusion sur le lancement Steam ulterieur tant que l'utilisateur n'a pas confirme s'il a atteint le mode Histoire et ferme le jeu lui-meme ou si le jeu s'est arrete de nouveau.

## 2026-08-24 01:54:30 +02:00 - Incidents d'outillage supplementaires sans incidence sur GTA
- Statut: Ferme
- Contexte: Preparation read-only du staging OIV, inspection de la fenetre GTA/Steam, controle de l'option de lancement Steam et revue du diff DonJ depuis le compte sandbox.
- Symptome: Quatre anomalies independantes ont ete rencontrees: `Invoke-WebRequest` a ete bloque par les droits socket de la sandbox; le premier appel `get_window` a recu un entier au lieu de l'objet attendu; `rg` a signale trois dossiers Steam Blueprint disparus pendant son scan; enfin, Git a refuse `status`/`diff --check` pour `dubious ownership` et un premier `git diff -- ...` hors contexte de depot a produit un diff no-index tres volumineux.
- Sources verifiees:
  - sortie console du premier `Invoke-WebRequest` OIV et de sa relance autorisee
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\downloads\OIVPackageInstaller-2.1.1.zip`
  - sortie du premier appel computer-use `get_window` et de la relance avec `{ id, app }`
  - `C:\Program Files (x86)\Steam\userdata\89981903\config\localconfig.vdf`
  - sortie `rg` du scan de `C:\Program Files (x86)\Steam\userdata`
  - sorties `git diff`, `git status` et `git diff --check`, puis leurs relances avec `-c safe.directory=C:/Users/nodig/GTA5modDEV`
- Extraits utiles:
  - `Invoke-WebRequest`: l'acces initial au CDN OIV a echoue avec une erreur de socket interdite par les autorisations de la sandbox.
  - Archive OIV obtenue apres approbation: taille `134 063 821` octets, SHA-256 `9B781B080D40AF66D2A738835F49021D5E1170607544ADED817E30CA0A82038A`.
  - `get_window`: le premier appel a refuse l'identifiant entier; la forme conforme `{ id, app }` a fonctionne immediatement.
  - `rg`: trois erreurs `os error 2` visaient `BASE SPATIALE`, `NASA Blue space` et `Platforme petroliere`, mais `localconfig.vdf:1007` a bien retourne `"LaunchOptions" "-nobattleye"`.
  - Git: `fatal: detected dubious ownership in repository at 'C:/Users/nodig/GTA5modDEV'`; les relances avec `git -c safe.directory=C:/Users/nodig/GTA5modDEV` ont reconnu le depot et affiche uniquement les deux ajouts attendus au stub et a son test.
- Analyse / hypothese: Ces anomalies provenaient respectivement du confinement reseau, d'un argument ne respectant pas le schema de l'outil, de chemins Blueprint references mais absents pendant un scan recursif Steam, et de la difference de proprietaire entre le compte utilisateur et le compte sandbox. Elles ne signalent aucune anomalie GTA ou DonJ.
- Action menee: J'ai relance le telechargement avec l'approbation reseau requise, corrige `get_window` avec l'objet `{ id, app }`, conserve le resultat positif de `localconfig.vdf` malgre les trois chemins sans rapport, puis relance toutes les commandes Git avec `safe.directory` limite a ce depot.
- Verification: Le ZIP OIV est present uniquement dans le staging et son hash a ete recalcule; l'inspection de fenetre a reussi; l'option `-nobattleye` est confirmee a la ligne `1007`; `git status`, le diff cible et `git diff --check` ont ensuite reussi et n'ont revele aucune modification inattendue.
- Resolution: Resolue. Aucun de ces incidents n'a modifie la racine GTA, lance l'installateur OIV ou affecte le jeu; cette entree ne tire aucune conclusion sur l'issue du lancement Steam en cours de validation.

## 2026-08-24 01:58:34 +02:00 - Validation vanilla reussie depuis Steam
- Statut: Ferme
- Contexte: Reprise du controle vanilla apres l'echec du lancement direct documente a `2026-08-24 01:33:45 +02:00`, en utilisant cette fois le bouton Steam et l'option persistante `-nobattleye`.
- Symptome: Aucun nouveau symptome. `GTA5_Enhanced.exe` est reste actif et reactif pendant plus de vingt minutes, puis l'utilisateur a confirme avoir ferme le jeu normalement.
- Sources verifiees:
  - processus Windows `GTA5_Enhanced.exe`, PID `2340`, demarre le `2026-08-24 01:34:53 +02:00`
  - `C:\Program Files (x86)\Steam\userdata\89981903\config\localconfig.vdf`
  - journal Rockstar Games Launcher contenant la ligne de commande effective avec `-nobattleye`, `-useSteam` et `-steamAppId=3240220`
  - controle des processus GTA, Rockstar, OpenIV et CodeWalker apres fermeture
- Extraits utiles:
  - `localconfig.vdf:1007`: `"LaunchOptions" "-nobattleye"`.
  - Controle runtime: `GtaRunning=True`, `Responding=True`, duree observee superieure a `20` minutes.
  - Controle final: `Aucun processus GTA/Rockstar/OpenIV/CodeWalker cible actif.`
- Analyse / hypothese: L'invite BattlEye et l'echec initial etaient lies au lancement hors du parcours Steam configure. La base GTA V Enhanced `1.0.1158.13` est stable lorsqu'elle est lancee par Steam avec son option persistante.
- Action menee: Je n'ai installe aucun mod pendant ce controle. J'ai attendu la confirmation de l'utilisateur et la fermeture normale avant d'autoriser le premier palier de copie.
- Verification: La racine GTA est restee vanilla pendant tout le test; le processus est demeure reactif, l'utilisateur a confirme la fermeture volontaire et aucun outil susceptible de verrouiller les RPF n'est encore actif.
- Resolution: Resolue. La validation vanilla est acquise; l'installation peut reprendre par paliers reversibles.

## 2026-08-24 01:59:55 +02:00 - Nouvelle occurrence NU1301 pendant la relance interrompue
- Statut: Ferme
- Contexte: Relance de `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1 -UseStubApi` apres la validation vanilla, encore executee dans l'ancien environnement reseau restreint au moment du demarrage.
- Symptome: L'etape `restore` a echoue avec `NU1301` avant la build principale; le tour a ensuite ete interrompu alors que le collecteur automatique venait de produire son rapport.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-015931\safety-failure.txt`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-015931\logs\restore.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-015931\logs\collect-bug-logs.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-015955-safety-failure`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-020026`
- Extraits utiles:
  - `restore.log`: `error NU1301: Impossible de charger l'index de service pour la source https://api.nuget.org/v3/index.json`.
  - `restore.log`: `Une tentative d'acces a un socket de maniere interdite par ses autorisations d'acces a ete tentee. (api.nuget.org:443)`.
  - Relance suivante: `Suite securite OK`, build avec `0` erreur et `0` avertissement, puis `164/164` tests reussis.
- Analyse / hypothese: Cette occurrence reproduisait exactement le blocage reseau de la sandbox deja documente; elle ne provenait ni du stub corrige ni du code runtime. L'interruption n'a laisse qu'un restore incomplet et des processus serveur MSBuild reutilisables, sans copie dans GTA.
- Action menee: J'ai inspecte `safety-failure.txt`, le journal de restauration, le rapport genere et les lignes de commande des processus restants, puis j'ai relance la meme suite des que le contexte reseau complet etait actif.
- Verification: La relance `safety-20260824-020026` a reussi de bout en bout, valide le contrat `DonJCustomNpcPlacer.ENdll` et execute `164` tests sans echec. Aucun fichier n'a ete deploye dans la racine GTA pendant ces deux executions.
- Resolution: Resolue. Incident d'environnement ferme par une relance identique reussie avec l'acces NuGet disponible.

## 2026-08-24 02:02:42 +02:00 - Nouvelle erreur de pipeline PowerShell pendant le hash DonJ
- Statut: Ferme
- Contexte: Controle read-only des tailles, dates et SHA-256 du livrable DonJ apres la suite de securite reussie.
- Symptome: La premiere commande a de nouveau place un pipe directement apres le bloc `foreach` et PowerShell a retourne `ParserError: An empty pipe element is not allowed` avant tout calcul.
- Sources verifiees:
  - sortie console de la commande de hash initiale
  - sortie console de la commande corrigee
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.ENdll`
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.pdb`
- Extraits utiles:
  - Commande initiale: `ParserError` sur le pipe suivant immediatement la fermeture de `foreach`.
  - `DonJCustomNpcPlacer.ENdll`: `379904` octets, SHA-256 `D31EC9FAB2E9AC9D73E8AE77FFE411CA84FFFFBFB03E067360B5EA823536A116`.
  - `DonJCustomNpcPlacer.pdb`: `116164` octets, SHA-256 `D70673C378AC64A96B06D39BBEFDCCA185DC6C14B84BBD4A6A64B66D7D66CAFC`.
- Analyse / hypothese: Il s'agissait uniquement d'une erreur de syntaxe dans une commande de diagnostic; aucun fichier n'avait encore ete ouvert pour ecriture ou copie.
- Action menee: J'ai affecte la sortie du `foreach` a une variable `$rows`, puis applique `Format-List` dans une instruction separee.
- Verification: La commande corrigee s'est terminee avec un code nul et a confirme que `.dll` et `.ENdll` portent le meme hash attendu; le PDB valide a egalement ete identifie.
- Resolution: Resolue. Aucun effet sur le depot, le binaire DonJ ou la racine GTA.

## 2026-08-24 02:16:40 +02:00 - Quatre avertissements de patterns NIB au palier de base stable
- Statut: Ferme
- Contexte: Premier lancement intermediaire apres installation du palier de base: chargeur Enhanced, Script Hook V, NIBMods, OpenRPF, DirectStorageFix, Menyoo, PC Trainer et livrable DonJ, avant toute installation OIV.
- Symptome: `NIBScriptHookVDotNet.log` a emis exactement quatre avertissements `Memory pattern not found` pendant l'initialisation de `NativeMemory`.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-021639-base-loader-warnings\summary.md`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-021639-base-loader-warnings\raw-logs\Grand-Theft-Auto-V-Enhanced__NIBScriptHookVDotNet.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-021639-base-loader-warnings\raw-logs\Grand-Theft-Auto-V-Enhanced__ScriptHookV.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-021639-base-loader-warnings\raw-logs\Grand-Theft-Auto-V-Enhanced__asiloader.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-021639-base-loader-warnings\raw-logs\Grand-Theft-Auto-V-Enhanced__DirectStorageFix.log`
- Extraits utiles:
  - `NIBScriptHookVDotNet.log` entre `02:11:45` et `02:11:46`: quatre lignes `[WARNING] Memory pattern not found`, et aucune cinquieme occurrence.
  - `NIBScriptHookVDotNet.log`: `Found 1 script(s) in DonJCustomNpcPlacer.ENdll resolved to API version 2.11.6 (target API version: 2.11.6)` puis `Started script DonJEnemySpawner`.
  - `NIBScriptHookVDotNet.log`: `Found 1 script(s) in NIBMods.net.ENdll resolved to API version 2.11.6 (target API version: 2.11.6)` puis le script NIB a ete instancie et demarre.
  - `ScriptHookV.log`: `INIT: Success, game version is VER_EN_1_0_1158_13`; les scripts ont ensuite ete desinscrits proprement a `02:15:04`.
- Analyse / hypothese: Les quatre signatures memoire non trouvees concernent des membres auxiliaires de `NativeMemory` qui ne correspondent pas a cette build. Au palier teste, elles sont non bloquantes: les hooks NIB principaux ont ete crees, les assemblies DonJ et NIBMods ont ete resolues sur la bonne API, leurs scripts ont demarre et le jeu est reste stable.
- Action menee: J'ai collecte le rapport `20260824-021639-base-loader-warnings`, compte les avertissements et controle les journaux ScriptHookV, ASI loader et DirectStorageFix avant de qualifier le palier. Je n'ai effectue aucun rollback puisque le runtime teste fonctionnait.
- Verification: Le jeu a atteint le mode Histoire, DonJ et NIBMods ont demarre, aucun crash n'est survenu pendant le palier et la fermeture a produit une desinscription normale des scripts ASI.
- Resolution: Resolue comme avertissement non bloquant pour le palier de base. Les quatre occurrences restent tracees et devront etre surveillees apres chaque palier; toute regression fonctionnelle ou tout crash imposera l'arret et une nouvelle collecte.

## 2026-08-24 02:19:49 +02:00 - Dialogue OIV inaccessible a l'automatisation UI
- Statut: Ferme
- Contexte: Preparation du premier package OIV dans CodeWalker OIV Package Installer, avant selection de `1 - IronmanV-EndGame.oiv` et avant toute transaction d'installation.
- Symptome: Le dialogue Windows de selection de fichier etait visible, mais ses elements UIA n'acceptaient pas la valeur du chemin et la fenetre modale ne pouvait pas etre activee de facon fiable par Computer Use.
- Sources verifiees:
  - captures et arbre d'accessibilite Computer Use de la fenetre CodeWalker OIV Package Installer
  - captures et arbre d'accessibilite du dialogue Windows de selection de fichier
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\oiv\1 - IronmanV-EndGame.oiv`
  - controle de l'etat de la racine `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced` avant et apres la tentative
- Extraits utiles:
  - Computer Use: les elements exposes par le dialogue etaient detectes mais non settable pour renseigner le chemin du package.
  - Computer Use: la tentative d'activation de la fenetre modale n'a pas etabli un contexte interactif fiable.
  - Etat OIV: aucun package n'a ete selectionne, aucun bouton d'installation n'a ete valide et aucune transaction OIV n'a commence.
- Analyse / hypothese: L'echec appartient uniquement a la couche d'automatisation UIA du dialogue commun Windows; il ne revele ni erreur dans le package Ironman, ni incompatibilite OIV, ni verrouillage d'un RPF GTA.
- Action menee: J'ai interrompu l'automatisation avant toute validation destructive et n'ai pas tente de contourner le dialogue par une saisie non verifiable.
- Verification: Aucun journal ou backup de transaction OIV n'a ete cree et aucun fichier GTA n'a ete ajoute, remplace ou supprime par cette tentative.
- Resolution: Resolue comme incident d'outillage sans effet sur les donnees. L'installation OIV reste a effectuer par un chemin d'interface fiable et verifiable; elle n'est pas consideree comme commencee.

## 2026-08-24 02:26:01 +02:00 - Echecs PowerShell pendant la superposition Ironman
- Statut: Ferme
- Contexte: Copie selective des `122` fichiers de configuration et ressources Ironman depuis `payload-B-ironman-20260824-0214`, apres la reussite de l'installation OIV Ironman.
- Symptome: La premiere tentative a appele `Split-Path -LiteralPath ... -Parent`; ses erreurs non terminales ont laisse absents les deux fichiers du sous-dossier `Scripts\nibmods\Teams`. La seconde tentative a utilise `New-Item -LiteralPath`, parametre non pris en charge par le cmdlet disponible, et n'a donc pas corrige ces deux absences.
- Sources verifiees:
  - sorties console des deux premieres tentatives de superposition
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\payload-B-ironman-20260824-0214\payload-manifest.json`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\payload-B-ironman-20260824-0214\payload-manifest.csv`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\nibmods\Teams\IronmanV3EG_AI.ini`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\nibmods\Teams\IronmanV3EG.enemy`
  - controle SHA-256 final des `122` entrees possedant un `TargetPath`
- Extraits utiles:
  - Premiere verification de superposition: `120` cibles conformes et deux cibles absentes sous `Scripts\nibmods\Teams` apres les erreurs non terminales de `Split-Path`.
  - Deuxieme tentative: `New-Item` a refuse le parametre nomme `LiteralPath` avant de pouvoir creer le dossier parent manquant.
  - `IronmanV3EG_AI.ini`: `58` octets, SHA-256 `40F505FD5D60DD0289B41F7F78056D78CB483EFC6398760FF9B5C542328CC7C4`.
  - `IronmanV3EG.enemy`: `7` octets, SHA-256 `55768B03C41254CE2DA0A8A4017D48E302C1F08EF3B9BDD9D73FC6529A9F694D`.
  - Verification finale: `TargetEntries=122`, `Exists=122`, `Matched=122`, `Mismatched=0`.
- Analyse / hypothese: Les deux echecs venaient uniquement de la gestion des chemins et des jeux de parametres PowerShell. Les erreurs non terminales ont permis a la premiere boucle de continuer, ce qui explique une superposition partielle mais non corrompue; l'installation OIV Ironman deja terminee n'a pas ete annulee ni alteree.
- Action menee: J'ai remplace la resolution et la creation du parent par `[System.IO.Path]::GetDirectoryName(...)` et `[System.IO.Directory]::CreateDirectory(...)`, puis relance la copie. Les deux fichiers Teams initialement manquants ont ainsi ete installes. Lors du controle documentaire, j'ai aussi corrige une commande read-only qui reproduisait le pipe invalide apres `foreach`, puis filtre les deux lignes source OIV sans `TargetPath` qui avaient provoque deux erreurs `Test-Path` nulles, sans aucune ecriture.
- Verification: J'ai recalcule le SHA-256 de chaque cible du manifeste: les `122/122` fichiers existent et correspondent exactement a leur source. Les deux fichiers Teams portent les hashes attendus, et le resultat OIV Ironman reste present et reussi.
- Resolution: Resolue. La superposition Ironman est complete, les deux fichiers manquants sont finalement installes et aucune corruption ou regression de l'installation OIV n'a ete observee.

## 2026-08-24 02:31:09 +02:00 - Incidents d'outillage pendant la fermeture du smoke test Ironman
- Statut: Ferme
- Contexte: Fermeture controlee de GTA V Enhanced apres le smoke test du palier Ironman OIV et de sa superposition selective.
- Symptome: Le premier appel Computer Use etait invalide parce qu'il ne precisait ni `include_text` ni `include_screenshot`. Apres correction, `Alt+F4` a affiche la confirmation de sortie, mais l'envoi de `Return` a repondu `foreground window did not report a process id`; la tentative de secours PowerShell a alors constate que `GTA5_Enhanced` etait deja ferme.
- Sources verifiees:
  - sorties et captures Computer Use avant et apres `Alt+F4`
  - sortie PowerShell de la tentative de fermeture de secours
  - verification finale `Get-CimInstance Win32_Process` des processus GTA, Rockstar, SocialClub et BattlEye
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\ScriptHookV.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\asiloader.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\DirectStorageFix.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\menyooLog.txt`
- Extraits utiles:
  - Premier appel Computer Use: validation refusee faute de `include_text` ou `include_screenshot`; la relance avec les options requises a fonctionne.
  - Envoi de confirmation: `foreground window did not report a process id` apres affichage du dialogue de sortie.
  - Secours PowerShell: aucun processus `GTA5_Enhanced` a fermer, car la fermeture demandee etait deja effective.
  - `NIBScriptHookVDotNet.log`: DonJ, Ironman et NIBMods ont chacun ete trouves avec `API version 2.11.6 (target API version: 2.11.6)`, puis leurs scripts ont demarre.
  - Controle des cinq journaux du palier: `0` ligne contenant `error`, `exception`, `fatal`, `crash` ou `failed`.
  - `ScriptHookV.log` a `02:31:09`: desinscription normale de `pc_trainer.asi`, `NIBScriptHookVDotNet.asi`, `NativeTrainer.asi` et `Menyoo.asi`.
- Analyse / hypothese: La fenetre GTA s'est fermee entre l'affichage de la confirmation et la tentative d'envoi de `Return`; Computer Use a perdu l'identite de processus de la fenetre au moment ou le processus quittait. Il s'agit d'une course de fermeture de l'outil, pas d'un crash GTA ni d'une regression Ironman.
- Action menee: J'ai corrige les parametres du premier appel Computer Use, controle l'etat apres `Alt+F4`, puis utilise la verification de processus plutot que de forcer une fermeture devenue inutile. Une contre-verification CIM lancee ensuite depuis le compte sandbox de documentation a renvoye `Acces refuse`; le controle CIM final execute dans le contexte autorise a donc ete conserve comme source de verite.
- Verification: Avant la fermeture, `GTA5_Enhanced` etait repondant et le smoke test avait confirme le chargement d'Ironman, DonJ et NIBMods. Le controle CIM final n'a trouve aucun processus GTA, Rockstar, SocialClub ou BattlEye, et les journaux ne contiennent aucune signature d'erreur ou de crash.
- Resolution: Resolue. La fermeture a reellement reussi; le smoke test Ironman reste valide et aucun crash, aucune corruption ni aucune perte de donnees liee a l'installation n'a ete observe.

## 2026-08-24 02:51:26 +02:00 - Browse OIV traite le DLC direct comme un package RPF
- Statut: Ferme
- Contexte: Tentative d'ajout du premier DLC autorise, `im_mark50ff_main`, avec CodeWalker OIV Package Installer `2.1.1` en selectionnant son `dlc.rpf` par le bouton Browse.
- Symptome: L'installateur a refuse le fichier avec `assembly.xml not found in RPF package` avant d'afficher ou d'executer une operation d'installation.
- Sources verifiees:
  - capture et sortie de CodeWalker OIV Package Installer `2.1.1`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\dlc-stage-20260824-023101\DLC-direct\im_mark50ff_main\dlc.rpf`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\dlc-stage-20260824-023101\manifests\current-dlclist-readonly-inspection.json`
  - page officielle `https://www.gta5-mods.com/tools/oiv-package-installer`, description et changelog `1.9`
  - source officielle `https://github.com/crxhvrd/CodeWalkerProjects`, documentation OIV Package Installer
- Extraits utiles:
  - OIV Installer: `assembly.xml not found in RPF package` lors du Browse de `im_mark50ff_main\dlc.rpf`.
  - Changelog officiel `1.9`: l'installation d'un DLC sans package OIV est prevue en glissant-deposant le dossier contenant `dlc.rpf` ou le fichier `dlc.rpf` lui-meme dans l'installateur.
  - Documentation officielle: un `.rpf` generique est accepte comme conteneur de package et passe par la lecture de `assembly.xml`, tandis qu'un `dlc.rpf` direct constitue le type add-on DLC distinct.
  - Inspection a `02:39:17`: aucun `mods\update\update.rpf`, aucune entree custom et aucun des dossiers DLC cibles n'existaient apres le refus.
- Analyse / hypothese: Le bouton Browse a route le fichier `.rpf` vers le lecteur de package OIV/RPF, qui exige logiquement `assembly.xml`; le chemin special d'installation DLC directe est reserve au DragDrop. Le `dlc.rpf` source n'est ni invalide ni corrompu.
- Action menee: J'ai annule la tentative sans forcer l'installateur et confirme le comportement dans la documentation officielle. Pour conserver une installation automatisable et transactionnelle, j'ai retenu la creation en staging de wrappers OIV minimaux, chacun contenant un `assembly.xml` valide et son `dlc.rpf`, avec validation des hashes avant utilisation.
- Verification: Aucun bouton d'installation n'a ete execute, aucune session transactionnelle n'a commence et le controle read-only a confirme que la racine GTA et son dossier `mods` n'avaient recu aucun fichier de ce DLC.
- Resolution: Resolue cote diagnostic et methode. L'installation directe par Browse est abandonnee; les wrappers OIV minimaux transactionnels seront construits et verifies en staging avant tout nouveau palier, avec zero perte de donnees.

## 2026-08-24 02:51:27 +02:00 - Incidents d'outillage non destructifs pendant la preparation des DLC
- Statut: Ferme
- Contexte: Commandes de preparation PowerShell, relecture des instructions Computer Use et tentative d'acheminement du `dlc.rpf` vers l'installateur au moyen de l'Explorateur Windows.
- Symptome: Une interpolation PowerShell contenant `$n:` a provoque un `ParserError`; une premiere lecture de la documentation de competence a cible un mauvais chemin; enfin, la navigation Explorer et `set_value` via UIA n'ont pas permis de renseigner ou transferer le chemin du fichier.
- Sources verifiees:
  - sorties console de la commande PowerShell initiale et de sa relance avec l'operateur de format `-f`
  - chemin de documentation corrige `C:\Users\nodig\.codex\plugins\cache\openai-bundled\computer-use\26.818.41509\skills\computer-use\docs`
  - captures et arbre d'accessibilite Computer Use de l'Explorateur Windows et du dialogue OIV
  - inventaire read-only de `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\mods`
- Extraits utiles:
  - PowerShell: la reference `$n:` a ete interpretee comme un nom de variable invalide a cause du deux-points; la chaine equivalente construite avec `-f` a ete analysee correctement.
  - Lecture de competence: le fichier n'existait pas au premier chemin tente; la ressource attendue a ete trouvee sous le sous-dossier `docs` du skill Computer Use.
  - Explorer/UIA: les elements de navigation etaient visibles mais non settable de facon fiable; aucun glisser-deposer ni aucune validation OIV n'a ete produit.
- Analyse / hypothese: Ces trois incidents sont independants de GTA et des packages: syntaxe d'interpolation PowerShell, resolution incorrecte d'un chemin documentaire, puis limite UIA sur les controles Explorer/dialogue modal.
- Action menee: J'ai remplace l'interpolation ambigue par une chaine formatee avec `-f`, relu la documentation depuis le chemin `docs` correct, puis abandonne la voie Explorer/UIA des qu'elle s'est averee non fiable, sans simuler un depot incertain.
- Verification: Les commandes PowerShell corrigees et la lecture de la documentation ont reussi. Aucun package n'a ete lance depuis Explorer, aucun fichier n'a ete copie dans GTA, aucune entree `dlclist.xml` n'a ete ajoutee et aucun backup de transaction n'a ete consomme.
- Resolution: Resolue. Incidents limites a l'outillage preparatoire, sans ecriture GTA, corruption ou perte de donnees.

## 2026-08-24 02:59:12 +02:00 - Incidents d'outillage pendant la validation et l'installation Mark 50/MK85
- Statut: Ferme
- Contexte: Validation des deux wrappers OIV minimaux, installation CLI de `im_mark50ff_main` puis `mk85_main`, et controle read-only de leur etat dans Manage Mods.
- Symptome: La premiere validation cherchait l'entree ZIP du payload avec le separateur `\` alors que l'archive exposait `/`. La premiere invocation CLI Mark 50 avec l'operateur `&` sur l'executable GUI a ensuite laisse `$LASTEXITCODE` a `null`, ce que le wrapper PowerShell a signale a tort comme un echec bien que l'installation soit terminee. Enfin, le clic UIA sur Manage Mods a retourne `coordinate input geometry is unavailable`.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\dlc-oiv-wrappers-20260824-025159\manifests\wrapper-validation.json`
  - sorties console des invocations CLI Mark 50 et MK85
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\OIV_CW_Logs\2026-08-24_02-54-16_Verified DLC Wrapper - im_mark50ff_main.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\OIV_CW_Logs\2026-08-24_02-55-48_Verified DLC Wrapper - mk85_main.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\OIV_CW_Uninstall_Data\Verified DLC Wrapper - im_mark50ff_main_20260824_025416`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\OIV_CW_Uninstall_Data\Verified DLC Wrapper - mk85_main_20260824_025548`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\backups\pre-suits-20260824-023232\manifest.json`
  - snapshot Computer Use de Manage Mods apres installation
- Extraits utiles:
  - Validation corrigee: normalisation des noms d'entrees ZIP vers `/`; `payloadExact=true` pour `content/im_mark50ff_main/dlc.rpf` et `content/mk85_main/dlc.rpf`, avec exactement deux entrees par wrapper.
  - Mark 50: `$LASTEXITCODE=null` apres `& CodeWalker.OIVInstaller.exe`, mais la cible, le log `02-54-16` et la transaction `..._20260824_025416` confirmaient deja `installation completed successfully`; aucune reinstallation n'a ete lancee.
  - MK85: lancement avec `ProcessStartInfo` puis `WaitForExit`, code de sortie `0`; le log `02-55-48` et la transaction `..._20260824_025548` confirment le succes.
  - Manage Mods: le premier clic UIA a echoue avec `coordinate input geometry is unavailable`; apres un nouveau snapshot, un clic visuel cible a ouvert la vue sans ecriture et affiche `2 / 2` add-ons, tous deux `Enabled`.
  - `im_mark50ff_main\dlc.rpf`: SHA-256 `2F76960CF35A727155C7E12435FB1E98361A89D6FD4487C363140BF7922A9C6E`; `mk85_main\dlc.rpf`: SHA-256 `041977B0E8B8CCC706DD12A10B1981BA403F3B3C88CA3C87DB72CC84275F3A00`.
- Analyse / hypothese: Les trois anomalies venaient de conventions d'outillage distinctes: separateur logique ZIP, absence de code de sortie fiable quand PowerShell invoque directement une application GUI, puis geometrie UIA indisponible pour un controle graphique. Aucune ne correspond a un echec OIV ou a une alteration GTA.
- Action menee: J'ai normalise les chemins ZIP avant comparaison, diagnostique Mark 50 a partir de la cible, du journal et de la transaction existants sans rejouer l'installation, utilise `ProcessStartInfo`/`WaitForExit` pour MK85, puis remplace uniquement le clic UIA impossible par un clic visuel fonde sur un snapshot frais.
- Verification: Les deux payloads installes portent exactement les hashes audites, les deux journaux OIV se terminent par un succes, les deux transactions de desinstallation existent, et Manage Mods indique `2/2 Enabled`. Le `update\update.rpf` vanilla conserve le SHA-256 `AAE1703738DCD45400292804AAC5126357E0D2CAC5A240B392F682D5A82B2450`; seul le `mods\update\update.rpf` transactionnel a evolue comme prevu.
- Resolution: Resolue. Mark 50 et MK85 sont installes une seule fois, valides et reversibles; tous les incidents etaient non destructifs et aucune corruption, reinstallation double ou perte de donnees n'a eu lieu.

## 2026-08-24 03:03:26 +02:00 - Crash au chargement du mode Histoire avec Mark 50 et MK85
- Statut: Resolu par rollback cible du dernier palier `mk85_main`; `im_mark50ff_main` conserve et valide en jeu.
- Contexte: Premier smoke test lance depuis Steam apres l'installation transactionnelle des deux DLC de combinaison `im_mark50ff_main` puis `mk85_main`, sur GTA V Enhanced `1.0.1158.13`. Le palier Iron Man sans ces deux DLC avait auparavant quitte proprement avec le code `0x0`.
- Symptome: Deux lancements successifs atteignent l'ecran de chargement du mode Histoire puis quittent fatalement avant l'initialisation des scripts NIB. Les deux sorties sont identiques: `0x80000003` (`EXCEPTION_BREAKPOINT`).
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-030408-bug-report\raw-logs\Grand-Theft-Auto-V-Enhanced__DirectStorageFix.log`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-030408-bug-report\windows-events\application-events.txt`
  - `C:\Users\nodig\Documents\Rockstar Games\Launcher\launcher.02.log`
  - `C:\Users\nodig\Documents\Rockstar Games\Launcher\launcher.01.log`
  - `C:\Users\nodig\Documents\Rockstar Games\Launcher\launcher.03.log`
  - `C:\Users\nodig\AppData\Local\Rockstar Games\GTAV Enhanced\CrashLogs\3f7fde95-7999-4182-9d84-03ffa773b0d5.dmp`
  - `C:\Users\nodig\AppData\Local\Rockstar Games\GTAV Enhanced\CrashLogs\1e48fe98-8a8f-4119-a25d-3a3f52b8f58a.dmp`
  - `C:\Users\nodig\AppData\Local\Rockstar Games\GTAV Enhanced\CrashLogs\crashcontext.log`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\dlc-stage-20260824-023101\manifests\rpf-structure-validation.json`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\backups\pre-suits-20260824-023232\manifest.json`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\DirectStorageFix.log`
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`
- Extraits utiles:
  - Palier precedent sain: `launcher.03.log:192-198` lance GTA a `02:27:53`, puis enregistre a `02:31:10` un arret propre `0x0`.
  - Chargement des deux nouveaux packs: rapport `DirectStorageFix.log:7-10`, avec `mods\update\update.rpf`, `im_mark50ff_main\dlc.rpf` et `mk85_main\dlc.rpf` enregistres a `03:02:04-03:02:05`.
  - Premier crash: `launcher.02.log:196-208`, lancement a `03:01:54`, sortie fatale `0x80000003` a `03:03:26`.
  - Reproduction: `launcher.01.log:192-204`, lancement a `03:04:27`, meme sortie `0x80000003` a `03:06:04`.
  - Analyse des deux minidumps: les dumps crees a `03:03:23` et `03:06:01` portent tous deux `EXCEPTION_BREAKPOINT` a `GTA5_Enhanced.exe+0x1585E0`, ce qui confirme un arret deterministe au meme point.
  - `crashcontext.log:4-10,30,48-50,65,3673-3689`: crash pendant `LOADINGSCREEN_STARTUP`, `Loading : 9`, aucun ped/vehicule/objet encore charge, un seul thread `landing_pre_startup`, et `MetaDataStore, 3200, 3200`.
  - `rpf-structure-validation.json:29-52`: `mk85_main` est un DLC de ped complet contenant notamment `componentpeds.rpf`, `streamedpeds.rpf` et `peds.meta`; son payload verifie fait `23 271 936` octets, SHA-256 `041977B0E8B8CCC706DD12A10B1981BA403F3B3C88CA3C87DB72CC84275F3A00`.
  - Evenement Windows concomitant: `application-events.txt:3-25`, WER 1001 `LiveKernelEvent`, signature `141`, avec `WATCHDOG-20260824-0302.dmp` a `03:02:40`.
  - Apres rollback, le `DirectStorageFix.log` courant ne reference plus que `im_mark50ff_main` a la ligne 8; `NIBScriptHookVDotNet.log:11-22` charge et demarre DonJ, Iron Man et NIBMods a `03:10:46-03:10:47`.
- Analyse / hypothese: L'attribution a `mk85_main` est de forte confiance. Les deux crashs surviennent uniquement apres son ajout, au meme point du chargement, avant l'execution des scripts; le contexte Rockstar montre le pool `MetaDataStore` arrive a sa limite exacte `3200/3200`. Comme `mk85_main` ajoute son propre `peds.meta`, son enregistrement constitue le declencheur le plus probable de la saturation de metadonnees sur le build `1158.13`. L'evenement graphique `LiveKernelEvent 141` et la pression memoire systeme observee (`94 %`, `937 Mio` libres) sont des facteurs secondaires a surveiller, mais ne suffisent pas a expliquer le comportement: le second crash est identique et le jeu devient stable apres le seul rollback de MK85. Le payload MK85 est structurellement complet et son hash est conforme; le probleme est sa compatibilite effective avec la capacite de ce build, pas une copie tronquee.
- Action menee: Rollback strict du dernier palier uniquement: restauration de `mods\update\update.rpf` depuis le checkpoint `pre-mk85\mods-update.rpf` (SHA-256 `64F3A35C388E143451C2B3F0251B1D5AA5AC7BD6834C1C65669F6FF9F90EC746`) et retrait de `mods\update\x64\dlcpacks\mk85_main`, qui etait absent avant installation. `im_mark50ff_main`, Iron Man, Green Beams, les chargeurs et les scripts ont ete laisses intacts. Aucun Added Peds Limit Fix, ancien gameconfig ou correctif interdit n'a ete ajoute.
- Verification: L'etat post-rollback correspond exactement au checkpoint `pre-mk85`: hash `mods\update\update.rpf` `64F3A35C...`, `mk85_main` absent, `im_mark50ff_main\dlc.rpf` present avec son hash attendu `2F76960CF35A727155C7E12435FB1E98361A89D6FD4487C363140BF7922A9C6E`. Le lancement suivant, demarre a `03:07:34`, est reste vivant et repondant au-dela de quatre minutes, a depasse le point des deux crashs et a demarre DonJ, Iron Man et NIBMods. Aucun nouveau dump GTA n'a ete cree apres celui de `03:06:01`.
- Resolution: Conserver Mark 50 seul pour ce palier. Reporter MK85 jusqu'a disposer d'une version explicitement compatible GTA V Enhanced `1158.13` ou d'une strategie actuelle et validee de capacite des pools; ne pas reintroduire l'ancien Added Peds Limit Fix ni un ancien correctif de crash.

## 2026-08-24 03:12:10 +02:00 - Incidents d'outillage pendant le diagnostic et le rollback MK85
- Statut: Ferme.
- Contexte: Controles read-only des versions, des journaux et des evenements, puis rollback par fichier du dernier palier `mk85_main` apres les deux crashs de chargement.
- Symptome: Plusieurs commandes PowerShell de diagnostic ont place un pipe directement apres `foreach` et ont retourne `ParserError: An empty pipe element is not allowed`; une recherche initiale de l'option Steam a traite `3240220|-nobattleye` comme une chaine litterale; la commande monolithique de rollback contenant une suppression recursive a ete refusee par la politique d'execution avant demarrage. Pendant les transitions GTA, Computer Use a aussi detecte les saisies de l'utilisateur et un snapshot a retourne `foreground window did not report a process id`.
- Sources verifiees:
  - sorties console des commandes PowerShell initiales et corrigees
  - sorties Computer Use et snapshots du menu GTA et des ecrans de chargement
  - `C:\Program Files (x86)\Steam\userdata\89981903\config\localconfig.vdf`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\backups\pre-suits-20260824-023232\pre-mk85\mods-update.rpf`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\backups\pre-suits-20260824-023232\rolled-back-dlc\mk85_main\dlc.rpf`
  - `C:\Users\nodig\GTA5modDEV\TestResults\mod-reinstall-20260824\backups\pre-suits-20260824-023232\rolled-back-oiv-transactions\Verified DLC Wrapper - mk85_main_20260824_025548`
- Extraits utiles:
  - Les erreurs de pipe sont survenues avant toute ecriture; les relances ont affecte le resultat du `foreach` a une variable avant `Sort-Object` ou `Format-Table`.
  - La recherche Steam corrigee a confirme `LaunchOptions` = `-nobattleye` pour l'application `3240220`.
  - Le rollback monolithique a ete refuse avec `blocked by policy`; le hash de `mods\update\update.rpf` et la presence de MK85 etaient donc encore inchanges juste apres ce refus.
  - Le rollback fractionne a restaure le SHA-256 `64F3A35C388E143451C2B3F0251B1D5AA5AC7BD6834C1C65669F6FF9F90EC746`, puis a deplace sans suppression le DLC MK85 et sa transaction OIV dans le backup.
  - Apres chaque detection de saisie utilisateur, aucun clic n'a ete rejoue a l'aveugle: un nouveau snapshot a confirme que le mode Histoire etait deja en chargement.
- Analyse / hypothese: Il s'agissait de fautes de syntaxe dans des commandes ad hoc, d'un mauvais mode de recherche textuelle, d'un garde-fou normal contre une commande destructive trop composee et de courses d'observation pendant les interactions utilisateur. Aucun de ces incidents n'explique le crash GTA ou n'indique une corruption de fichier.
- Action menee: J'ai corrige les pipelines PowerShell, utilise la recherche regex adaptee, abandonne la suppression recursive au profit de deux deplacements recuperables apres validation des chemins absolus, puis respecte les saisies utilisateur en rafraichissant l'etat sans repeter l'action.
- Verification: Le checkpoint, le DLC Mark 50 actif et le DLC MK85 archive portent tous les hashes attendus. Le lancement suivant est stable, l'utilisateur confirme que Mark 50 fonctionne et aucun nouveau crash n'est apparu.
- Resolution: Resolue. Tous les incidents sont limites a l'outillage de controle; le rollback final est exact, recuperable et documente.

## 2026-08-24 04:15:27 +02:00 - Echecs de validation pendant la refonte du menu F10 Obsidienne
- Statut: Resolu.
- Contexte: Compilation et tests headless de la nouvelle console DonJ Obsidienne dans un dossier de staging, sans deploiement dans le jeu encore ouvert.
- Symptome: La premiere compilation a revele des references residuelles du renderer historique, puis le projet de tests ne referencait pas Windows Forms. Les premieres simulations ont ensuite rencontre l'absence normale de l'assembly runtime NIB lors des acces a `Game.GameTime` et a l'etat de la touche Maj. Enfin, une assertion source ciblee utilisait un nom de parametre incorrect pour `ShowStatus`.
- Sources verifiees:
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-040156-menu-obsidienne-build-signatures`
  - `C:\Users\nodig\GTA5modDEV\bug-reports\20260824-041402-menu-obsidienne-test-source-marker`
  - sorties `dotnet build GTA5modDEV.sln -c Release` et `dotnet test GTA5modDEV.sln -c Release` avec `GtaScriptsDir` dirige vers le staging
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-041436\safety-tests.trx`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-041451\safety-tests.trx`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-042808\safety-tests.trx`
  - `C:\Users\nodig\GTA5modDEV\TestResults\safety-20260824-042823\safety-tests.trx`
- Extraits utiles:
  - Compilation initiale: constantes de geometrie historiques et noms d'affichage d'accessoires devenus incoherents apres l'extraction du renderer.
  - Tests initiaux: chargement impossible de `NIBScriptHookVDotNet` depuis les simulations qui appelaient directement `Game.GameTime` ou `Game.IsKeyPressed`.
  - Dernier test isole: `Le marqueur de fin 'private void ShowStatus(string text, int durationMs)' est introuvable dans la source.`
  - Validation finale apres durcissement: `171` tests reussis, `0` echec, build avec `0` avertissement et `0` erreur, dans les pipelines stub et API NIB installee.
- Analyse / hypothese: Les echecs provenaient exclusivement de l'integration progressive du nouveau partial et des contraintes headless; aucun comportement GTA, fichier de sauvegarde ou assembly installe n'a ete modifie pendant ces tentatives.
- Action menee: J'ai retire le renderer historique restant, corrige les noms internes, active la reference Windows Forms du projet de tests, centralise les acces menu au temps et a la touche Maj derriere des fallbacks headless, puis aligne le marqueur du test sur la signature reelle. Chaque relance a continue de deployer uniquement vers le staging.
- Verification: `run-safety-checks.ps1 -UseStubApi` et `run-safety-checks.ps1` reussissent tous deux avec `171/171`; le livrable staging `DonJCustomNpcPlacer.ENdll` est un assembly valide et aucun fichier du dossier GTA n'a ete remplace.
- Resolution: Resolu sans regression et sans impact sur la session GTA en cours. Le deploiement live reste volontairement differe jusqu'a la fermeture du jeu.

## 2026-08-25 01:34:20 +02:00 - Erreur de syntaxe pendant le relevé documentaire des versions locales
- Statut: Résolu.
- Contexte: Contrôle strictement read-only des versions de GTA V Enhanced, ScriptHookV, du chargeur Enhanced et de NIB avant leur mise à jour dans la documentation.
- Symptôme: La première commande PowerShell a placé un pipe directement après un bloc `foreach` et le parseur a retourné `An empty pipe element is not allowed` avant toute lecture des métadonnées.
- Sources vérifiées:
  - sortie console de la première commande PowerShell ;
  - sortie console de la commande corrigée ;
  - métadonnées de version des cinq fichiers sous `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced`.
- Extraits utiles:
  - première tentative: `ParserError` sur le caractère `|` suivant immédiatement la fermeture du `foreach` ;
  - relance: `GTA5_Enhanced.exe 1.0.1158.13`, `ScriptHookV.dll 3889.0.1158.13`, `xinput1_4.dll 1.0.0.2`, `NIBScriptHookVDotNet2.dll 2.11.6` et `NIBScriptHookVDotNet.asi 3.9.0.0`.
- Analyse / hypothèse: L'erreur venait uniquement de la composition du pipeline PowerShell. Elle est survenue avant l'exécution et ne concernait ni les binaires GTA ni le projet.
- Action menée: J'ai affecté le résultat du `foreach` à une variable dédiée, puis appliqué `Format-Table` dans une instruction séparée.
- Vérification: La commande corrigée s'est terminée avec le code `0` et a confirmé les versions locales attendues sans aucune écriture dans GTA.
- Résolution: Résolu. Incident de diagnostic sans modification, corruption ni perte de données.

## 2026-08-25 01:35:40 +02:00 - Incidents de validation headless de Justice avancée
- Statut: Résolus.
- Contexte: Ajout et exécution des contrats comportementaux Justice couvrant le runtime borné, le HUD, la persistance et la détention, avec déploiement dirigé uniquement vers des dossiers de staging.
- Symptôme: Trois échecs de développement successifs ont interrompu des relances ciblées: une assertion HUD demandait `SafeBounds` au viewport qui ne possède pas ce membre, une assertion d'animation attendait exactement `0,72` alors que la lecture suivante observait `0,755`, puis la build avec stub ne trouvait pas le type `GTA.Native.InputArgument` désormais employé par les wrappers de natives.
- Sources vérifiées:
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `C:\Users\nodig\GTA5modDEV\tests\DonJEnemySpawner.Tests\SafetySimulationTests.cs` ;
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.MenuUi.cs` ;
  - `C:\Users\nodig\GTA5modDEV\src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `C:\Users\nodig\GTA5modDEV\tools\Stubs\NIBScriptHookVDotNet2\StubApi.cs` ;
  - sorties des tests ciblés `JusticeRuntimeContractTests` avec l'API NIB installée et avec le stub ;
  - sortie de `dotnet test GTA5modDEV.sln -c Release --no-restore` avec `GtaScriptsDir` redirigé vers le staging.
- Extraits utiles:
  - HUD: `MenuViewport` expose `SafeLeft`, `SafeTop`, `SafeLogicalWidth` et `SafeLogicalHeight`; `SafeBounds` appartient au layout complet et ne pouvait pas être lu sur ce type ;
  - animation: la valeur initiale est `0,72`, puis progresse légalement pendant les quelques millisecondes entre deux lectures de temps, d'où la mesure `0,755` ;
  - stub: la compilation signalait le type `InputArgument` manquant après l'introduction des appels natifs protégés par coupe-circuit ;
  - validation finale: `20/20` contrats Justice avec l'API installée, `20/20` avec le stub, puis `228/228` tests Release complets.
- Analyse / hypothèse: Les deux premiers échecs provenaient d'assertions headless trop liées à une représentation ou à une valeur instantanée, pas d'un débordement réel du HUD ni d'une animation invalide. Le troisième révélait un retard du stub sur la signature NIB v2 réellement utilisée; le runtime installé exposait déjà ce contrat.
- Action menée: J'ai reconstruit la zone sûre du HUD depuis les champs réels du viewport et conservé les contrôles de confinement, remplacé l'égalité instantanée par une borne étroite compatible avec la progression monotone, puis ajouté au stub `InputArgument` avec les conversions implicites `int` et `bool` nécessaires aux appels existants.
- Vérification: Les deux configurations ciblées réussissent chacune `20/20` tests Justice. La suite complète Release réussit `228/228`, sans échec; la build du stub annonce zéro avertissement et zéro erreur. Tous les livrables de ces validations ont été dirigés vers `%TEMP%`, sans remplacement de l'assembly GTA installé.
- Résolution: Résolus. Les contrats testent désormais le comportement réel sans fragilité temporelle, et le stub est aligné sur l'API NIB v2 employée par Justice avancée.

## 2026-08-25 01:46:00 +02:00 - Signature homicide incohérente pendant l'audit Justice
- Statut: Résolu.
- Contexte: Compilation Release intermédiaire du runtime Justice vers `artifacts\justice-compile-stage`, après le remplacement de l'historique de dégâts GTA par un horodatage causal borné.
- Symptôme: La build a échoué avec deux erreurs `CS7036` dans `DonJEnemySpawner.Justice.Custody.cs`: les appels disciplinaires à `IsJusticeDeathAttributedTo` ne transmettaient pas encore le nouveau paramètre `causalDamageAtMs`.
- Sources vérifiées:
  - sortie de `dotnet build GTA5modDEV.sln -c Release --no-restore /p:GtaScriptsDir=...\artifacts\justice-compile-stage` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs`.
- Extraits utiles: `CS7036` aux anciennes lignes `1887` et `1906`; aucune autre erreur et aucun avertissement n'étaient signalés.
- Analyse / hypothèse: Le défaut était une incohérence de signature introduite pendant une édition atomique entre deux partials. Il n'a jamais atteint le dossier GTA, toutes les sorties étant redirigées vers le staging.
- Action menée: J'ai rendu l'horodatage causal optionnel avec une valeur sûre `-1` pour refuser tout fallback historique, puis le code de discipline photographie explicitement le front joueur→garde/détenu et transmet son heure réelle à la qualification homicide.
- Vérification: Deux builds Release successifs vers le staging se terminent avec `0` erreur et `0` avertissement.
- Résolution: Résolu sans déploiement live. Les homicides en détention utilisent désormais le tueur GTA valide ou un front causal frais, jamais un ancien flag de dégâts.

## 2026-08-25 01:48:00 +02:00 - Assertion discipline obsolète après consommation des dégâts
- Statut: Résolu.
- Contexte: Première exécution de la suite Release complète après la sécurisation des fronts de dégâts en détention.
- Symptôme: Un test sur `242` a échoué: `CustodyDiscipline_QualifiesProvenDeathsBeforeGenericAssaults` cherchait encore les anciens fragments `HasBeenDamagedBy` et l'ancienne signature homicide à trois arguments.
- Sources vérifiées:
  - sortie de `dotnet test GTA5modDEV.sln -c Release --no-restore /p:GtaScriptsDir=...\artifacts\justice-full-test-stage` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs`.
- Extraits utiles: `échec: 1, réussite: 241, total: 242`; le marqueur absent était `guard.IsDead && IsJusticeDeathAttributedTo(guard, player, null)`.
- Analyse / hypothèse: Le runtime compilait correctement; seule l'assertion source décrivait l'ancienne implémentation non consommée au lieu du nouveau contrat causal plus sûr.
- Action menée: J'ai conservé l'ordre comportemental homicide avant agression, mais mis le test à jour pour exiger `TryCaptureJusticeDamageFront`, la transmission du timestamp causal, puis le fallback agression sur ce même front.
- Vérification: La relance Release complète réussit `242/242` tests en environ cinq secondes, avec `0` échec, `0` erreur de build et `0` avertissement.
- Résolution: Résolu dans le staging. Aucun binaire GTA n'a été modifié pendant l'incident.

## 2026-08-25 01:53:00 +02:00 - Déploiement Release prématuré pendant un test auxiliaire
- Statut: Contenu sauvegardé; remplacement final encore différé jusqu'à la fin de l'audit.
- Contexte: Vérification des timestamps et hashes du livrable GTA avant la sauvegarde obligatoire et le déploiement final de Justice avancée.
- Symptôme: Le `.ENdll` et le PDB live portaient l'heure `01:46:24`, alors que l'assembly Justice n'aurait dû exister que dans les dossiers de staging à ce palier.
- Sources vérifiées:
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.ENdll` ;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.pdb` ;
  - commande auxiliaire rapportée: `dotnet test GTA5modDEV.sln -c Release --filter FullyQualifiedName~JusticeRuntimeEdgeContractTests --no-restore` sans surcharge de `GtaScriptsDir` ;
  - staging Obsidienne validé `TestResults\safety-20260824-042823\Scripts` ;
  - processus Windows GTA/Rockstar/OpenIV/CodeWalker.
- Extraits utiles:
  - live prématuré: ENdll `513536` octets, SHA-256 `F23F4D2092F5AAC846E3BB343D555B62BE1037B6F0374E9422A22F0F97B03B9A`; PDB SHA-256 `476E472BAEADC0245097DA51AEBD0A9FE4F3EE2B616B9D005CCCBD5B8C51788B` ;
  - dernier Obsidienne pré-Justice validé: ENdll SHA-256 `1C1798CA6303C5CD067F1CBCA02B035971F527443A5C5948B757D99374792F82`, PDB SHA-256 `33BD95AE89EE7528287177BD759BCC96DA318514A9190B867114C76960FF6FF5` ;
  - GTA et les outils de modding étaient fermés; seul Steam restait actif.
- Analyse / hypothèse: La cible MSBuild `DeployAsEndll` s'exécute après toute build Release. La commande auxiliaire n'ayant pas redirigé `GtaScriptsDir`, elle a utilisé le dossier GTA par défaut. Le jeu fermé n'a chargé aucun de ces octets et aucune sauvegarde de jeu n'a été touchée.
- Action menée: J'ai arrêté toute nouvelle copie live et sauvegardé séparément l'état prématuré ainsi que le dernier couple Obsidienne validé sous `C:\Users\nodig\GTA5modDEV-backups\justice-advanced-20260825-0153`. Une recherche de sauvegarde a d'abord rencontré un `ParserError` read-only dû à un pipe après `foreach`; la relance a affecté les résultats à une variable avant tri et s'est terminée sans écriture destructive.
- Vérification: Les quatre fichiers sauvegardés existent, leurs tailles et hashes correspondent aux sources. Les pipelines suivants utilisent tous un `GtaScriptsDir` de staging explicite ou celui créé par `run-safety-checks.ps1`.
- Résolution: Incident de déploiement contenu et réversible. Le déploiement final ne sera effectué qu'après validation complète, puis le hash live sera comparé byte pour byte au staging final.

## 2026-08-25 02:18:00 +02:00 - Incidents de l'audit transactionnel final Justice
- Statut: Résolus dans le staging, sans nouvelle écriture live.
- Contexte: Dernier audit de reprise après crash sur la confiscation, les amendes, l'identité du protagoniste, les activités et le fallback XML, avec `GtaScriptsDir` explicitement redirigé vers `%TEMP%`.
- Symptôme: Deux recherches `rg` ont utilisé un glob Windows directement dans le chemin et ont retourné `os error 123`; une compilation a ensuite signalé `CS0103` pour le helper `JusticeReadLongAttribute` inexistant. La première suite après les transactions a enfin trouvé deux assertions obsolètes (un seul flush d'évasion et l'ancien débit d'amende monolithique), puis le durcissement exigeant le nœud `Custody` a fait échouer un backup synthétique de test qui ne le contenait pas.
- Sources vérifiées:
  - sorties des commandes `rg`, `dotnet build` et `dotnet test` de l'audit ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeEdgeContractTests.cs` ;
  - staging `C:\Users\nodig\AppData\Local\Temp\donj-justice-compile-audit`.
- Extraits utiles:
  - recherches: `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` ;
  - compilation: `CS0103: Le nom 'JusticeReadLongAttribute' n'existe pas dans le contexte actuel` ;
  - première suite: `2` échecs, `243` réussites sur `245` ;
  - validation après mise à jour des contrats: `251/251` tests réussis, build `0` erreur et `0` avertissement ;
  - test XML intermédiaire: `250` réussites sur `251`, le backup de test était incomplet car dépourvu de `<Custody>`.
- Analyse / hypothèse: Les recherches échouées relevaient uniquement de la syntaxe des globs PowerShell. Le symbole C# était un nom de helper incorrect lors de l'ajout du parseur `long`. Les trois tests décrivaient des fixtures ou ordres antérieurs aux nouveaux contrats crash-safe et non une régression du runtime.
- Action menée: J'ai utilisé `rg -g` avec un répertoire réel, remplacé le helper par `ReadJusticeLong`, actualisé les assertions pour exiger le précommit avant toute mutation externe, puis rendu le backup synthétique structurellement conforme et ajouté le cas explicite d'un XML sans `Custody`.
- Vérification: La build Release et la suite complète se terminent avec `0` avertissement, `0` erreur et `251/251` tests dans le staging. Aucun processus GTA n'a été lancé et aucune nouvelle copie n'a été faite vers `Scripts` pendant ces corrections.
- Résolution: Résolu. Les échecs étaient limités aux outils et aux tests de développement; les chemins live sont restés inchangés depuis la sauvegarde documentée à `01:53`.

## 2026-08-26 00:15:01 +02:00 - Recherches Windows invalides pendant la passe finale Justice
- Statut: Résolu, sans écriture GTA.
- Contexte: Relecture locale des partials Justice, du menu consultable et des tests avant toute validation Release ou copie live.
- Symptôme: Plusieurs recherches `rg` ont de nouveau reçu un glob `DonJEnemySpawner.Justice*.cs` directement dans un chemin Windows et ont retourné `os error 123`. Une recherche composée a aussi produit `regex parse error: unclosed group` à cause d'une expression échappée de façon incorrecte dans PowerShell.
- Sources vérifiées:
  - sorties console des recherches `rg` fautives puis corrigées ;
  - `src\DonJEnemySpawner` ;
  - `tests\DonJEnemySpawner.Tests`.
- Extraits utiles:
  - `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` ;
  - `regex parse error: unclosed group` ;
  - les relances avec un répertoire réel et `-g "*.cs"`, puis avec plusieurs motifs `-e`, se terminent avec le code `0`.
- Analyse / hypothèse: Ces erreurs provenaient uniquement de la syntaxe de recherche sous Windows et sont survenues avant toute mutation de fichier. Elles ne révèlent aucune anomalie du mod ni de GTA.
- Action menée: J'ai remplacé les globs de chemin par le filtre `-g`, séparé les motifs complexes avec `-e` et poursuivi l'audit sur les mêmes sources.
- Vérification: Toutes les occurrences recherchées ont été retrouvées; la build Debug suivante réussit avec zéro avertissement et zéro erreur.
- Résolution: Résolu. Aucun fichier GTA, binaire installé, sauvegarde ou état Justice n'a été touché par ces erreurs d'outillage.

## 2026-08-26 00:15:02 +02:00 - Défauts transactionnels et assertions intermédiaires de la passe finale Justice
- Statut: Défauts corrigés et tests Debug verts; validation Release finale encore requise avant déploiement.
- Contexte: Audit croisé du domaine, du runtime, de la détention et des nouvelles vues `Délits du dossier` / `Casier judiciaire`, exclusivement dans le dépôt et les sorties Debug.
- Symptôme: La relecture a trouvé avant livraison plusieurs fenêtres rares: une amende pouvait être tenue pour payée après un crash situé entre le marqueur `DebitAttempted` et l'écriture absolue du cash; un décès suspendu pouvait être préfiltré par l'identité stricte; un `BUSTED` entièrement masqué par une panne native pouvait devenir un mandat; une panne de scénario annulait une activité; le wanted d'amnistie et les états temporaires n'avaient pas de reprise vérifiée. Le curseur des listes Justice lisait aussi l'offset du menu principal. Deux relances intermédiaires ont chacune signalé deux tests devenus obsolètes ou une fixture headless incomplète.
- Sources vérifiées:
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Domain.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.MenuUi.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeDomainTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeEdgeContractTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\SafetySimulationTests.cs` ;
  - sorties `dotnet build GTA5modDEV.sln -c Debug --no-restore` et `dotnet test GTA5modDEV.sln -c Debug --no-restore`.
- Extraits utiles:
  - première relance: `2` échecs, `289` réussites sur `291`, dus à l'ancien ordre attendu pour la perte wanted et au contrat de précommit désormais réconcilié ;
  - seconde relance: `2` échecs, `295` réussites sur `297`, dus à une recherche de première occurrence dans le test et au tableau readonly non initialisé par `FormatterServices` ;
  - relance corrigée: `297/297` tests réussis en Debug ; build `0` avertissement, `0` erreur.
- Analyse / hypothèse: Les défauts runtime étaient des cas de reprise ou de panne native non exercés par les premiers contrats, et n'avaient pas été lancés dans GTA. Les quatre échecs de tests provenaient des assertions/fixtures qui décrivaient l'implémentation antérieure, pas d'un échec de compilation ou d'une copie live.
- Action menée: J'ai réconcilié l'amende par `CashBefore/CashAfter`, ajouté les retries bornés et lectures tri-state, rendu chaque restauration vérifiable, corrigé l'identité death-only, conservé la compatibilité XML v1, séparé l'offset de scrollbar Justice et ajouté des tests comportementaux ciblés pour chaque reprise ainsi que pour les listes intégrales.
- Vérification: La suite Debug complète réussit `297/297`; `git diff --check` ne signale aucune erreur d'espace. Aucun processus GTA n'a été lancé et aucun nouveau livrable n'a été copié dans `Scripts` pendant cette passe.
- Résolution: Corrigé dans le dépôt. Le déploiement reste volontairement bloqué jusqu'aux deux safety checks, à la build/test Release en staging et à la sauvegarde hashée du couple live.

## 2026-08-26 00:35:54 +02:00 - Derniers incidents d'outillage et contrat d'activité Justice
- Statut: Résolus dans le dépôt, sans écriture GTA.
- Contexte: Relecture finale des invariants XML, de l'amende transactionnelle et de l'horloge des activités avant les validations Release en staging.
- Symptôme: Une commande d'inspection PowerShell a tenté de soustraire une collection `Object[]` issue de `Select-String`; une recherche `rg` a reçu un glob Windows directement dans le chemin et a retourné `os error 123`. Après la correction de l'horloge d'activité, un ancien contrat source attendait encore l'affectation `_justiceActivityLastTickAt = now` et a provoqué un échec sur 300 tests.
- Sources vérifiées:
  - sorties console des commandes PowerShell et `rg` fautives puis corrigées ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeEdgeContractTests.cs` ;
  - sortie `dotnet test GTA5modDEV.sln -c Debug --no-restore`.
- Extraits utiles:
  - PowerShell: opération de soustraction impossible sur la collection retournée par `Select-String` ;
  - `rg`: `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` ;
  - première relance après correction runtime: `1` échec, `299` réussites sur `300`, marqueur obsolète `_justiceActivityLastTickAt = now` ;
  - relance corrigée: `300/300` tests Debug réussis, zéro échec.
- Analyse / hypothèse: Les deux premières erreurs relevaient uniquement de commandes de lecture ad hoc. L'échec MSTest décrivait l'ancienne implémentation qui perdait une frame à chaque sonde de scénario, alors que le nouveau contrat centralise le gel et l'avancement dans une horloge déterministe.
- Action menée: J'ai remplacé les recherches fragiles par des lectures bornées, actualisé le contrat pour exiger `AdvanceJusticeActivityClock`, ajouté une simulation à 30/60/120 FPS et le gel explicite sur état natif inconnu. La passe a aussi séparé un slot de cash inconnu d'une lecture native transitoirement indisponible, persisté l'étape `CashPlanPrepared` avec une échéance de reprise, et interdit à `CashAfter` de prouver un débit avant `DebitAttempted`.
- Vérification: La suite Debug complète réussit `300/300`. Les activités atteignent la même durée aux trois framerates simulés. Une panne de lecture cash sur un protagoniste connu ne produit aucune mutation pendant la fenêtre persistante puis, seulement à son expiration, convertit l'amende sans effectuer de `SET` cash et sans bloquer le transfert.
- Résolution: Résolu. Aucun binaire installé, sauvegarde GTA ou état Justice live n'a été modifié pendant ces incidents.

## 2026-08-26 00:43:43 +02:00 - Validation et déploiement final Justice avancée
- Statut: Build et déploiement terminés; smoke test GTA différé pour préserver la session utilisateur active.
- Contexte: Validation Release finale du système Justice, sauvegarde hashée du livrable installé, puis remplacement strict de l'ENdll et du PDB DonJ.
- Symptôme: Aucun défaut de build, de test ou de hash. Le contrôle Windows préalable au lancement Steam a montré une partie de `Ready Or Not` actuellement active; GTA n'a donc pas été lancé afin de ne pas interrompre le joueur.
- Sources vérifiées:
  - `TestResults\safety-20260826-004113` avec le stub NIB v2 ;
  - `TestResults\safety-20260826-004129` avec l'API NIB installée ;
  - `TestResults\justice-final-20260826-0042\Scripts` ;
  - `C:\Users\nodig\GTA5modDEV-backups\justice-advanced-20260826-0043\pre-final-live` ;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts` ;
  - processus et fenêtre Windows observés avant lancement.
- Extraits utiles:
  - safety stub: `300/300`, zéro avertissement, zéro erreur ;
  - safety API installée: `300/300`, zéro avertissement, zéro erreur ;
  - build/test explicites: `300/300`, zéro avertissement, zéro erreur ;
  - ENdll final: `586240` octets, SHA-256 `80AF3F4EAB80E7C30D2B8D41A41664037B9BFFCE8ABA81914AB7CF5058CC26ED` ;
  - PDB final: `194768` octets, SHA-256 `9127A2504732A4F54337B5BC999F74BB77144759C6A1BF0ABF80B9BB1912BE86` ;
  - les hashes live correspondent exactement au staging final.
- Analyse / hypothèse: Le livrable est statiquement et comportementalement validé hors jeu. Les logs GTA encore datés du 25 août ne peuvent pas valider le nouvel assembly, puisqu'aucun lancement n'a suivi le déploiement.
- Action menée: J'ai conservé la session utilisateur, sauvegardé le précédent ENdll/PDB avec manifeste et hashes, puis copié uniquement `DonJCustomNpcPlacer.ENdll` et `DonJCustomNpcPlacer.pdb` depuis le staging validé.
- Vérification: `git diff --check` ne signale aucune erreur d'espace; les deux hashes live ont été relus après copie et correspondent au staging. Aucun fichier de scène, RPF, DLC, configuration de mod ou sauvegarde GTA n'a été modifié.
- Résolution: Déploiement réussi et rollback disponible. Le seul contrôle restant est le smoke test mode Histoire/F10 et la lecture des nouveaux logs après fermeture de `Ready Or Not`.

## 2026-08-26 00:50:13 +02:00 - Crash du tas à la mort en poursuite et résolution concurrente des incidents Justice
- Statut: Résolu dans le dépôt et non reproduit lors du smoke de mort suivant.
- Contexte: Premier lancement GTA V Enhanced `1.0.1158.13` après le déploiement Justice de `00:43`, Justice activée, plusieurs infractions confirmées puis mort du joueur pendant la poursuite policière.
- Symptôme: Deux ticks Justice ont d'abord levé `ArgumentOutOfRangeException` dans `ProcessJusticePendingIncidents`. Après la capture sur mort et la reconnexion du protagoniste au respawn, GTA s'est fermé brutalement avec une corruption du tas dans `ntdll.dll`.
- Sources vérifiées:
  - `bug-reports\20260826-015542-justice-death-heap-crash-20260826-005013\windows-events\application-events.txt` ;
  - `bug-reports\20260826-015542-justice-death-heap-crash-20260826-005013\raw-logs\DonJ-Runtime-Legacy__DonJCustomNpcPlacer.log` ;
  - `bug-reports\20260826-015542-justice-death-heap-crash-20260826-005013\raw-logs\Grand-Theft-Auto-V-Enhanced__Scripts__DonJCustomNpcPlacer.log` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` et `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeCustodyHardeningTests.cs`, `StubRuntimeBehaviorTests.cs` et `JusticeEnginePersistenceRegressionTests.cs`.
- Extraits utiles:
  - événement Windows à `00:50:13`: `GTA5_Enhanced.exe`, module `ntdll.dll`, code `0xc0000374` ;
  - journal DonJ à `00:49:43.123` puis `00:49:47.023`: `System.ArgumentOutOfRangeException` dans `ProcessJusticePendingIncidents()` ;
  - journal DonJ à `00:49:55.516`: `Justice.Capture - Capture apres mort en poursuite` ;
  - journal DonJ à `00:50:05.691`: `Justice.Detention - Identité du protagoniste reliée après son respawn`.
- Analyse / hypothèse: La cause P0 est confirmée. `GET_DLC_WEAPON_DATA` écrit une structure native de `312` octets, alors que l'ancien appel lui fournissait un `OutputArgument` NIB v2 limité à `24` octets; l'écriture hors tampon corrompait le tas et le crash différé apparaissait dans `ntdll.dll`. Indépendamment, la boucle des incidents continuait à indexer `_justicePendingIncidents` pendant qu'une résolution ou une déduplication en retirait d'autres éléments.
- Action menée: L'énumération DLC utilise désormais un tampon unmanaged réutilisable de `312` octets, vidé avant appel, transmis comme adresse x64 via `InputArgument(ulong)`, lu uniquement à l'offset `8` et libéré en `finally`; toute panne ferme la confiscation sans retirer l'inventaire. La résolution des incidents collecte et classe d'abord les candidats, puis applique les suppressions et notifications dans une phase séparée, avec priorité à la violence corrélée sur le tir dangereux.
- Vérification: Les contrats `InventaireDlc_EcritTouteLaStructureNativeSansOutputArgument`, `IncidentResolution_PrioritizesRelatedViolenceBeforeRecklessDischarge` et les tests d'échec fermé couvrent les deux régressions. Les safety checks du `2026-08-26 02:04` ont réussi avec le stub (`329/329`) et l'API installée (`324/324`). Lors du smoke suivant, la mort en poursuite de `02:11:53` a atteint Bolingbroke sans nouveau `0xc0000374` ni fermeture de GTA.
- Résolution: Crash natif et exceptions concurrentes corrigés. Le binaire vulnérable ne doit pas être réutilisé; le correctif conserve un chemin de rollback par assembly.

## 2026-08-26 02:11:53 +02:00 - Précommit XML refusé pendant le smoke de capture sur mort
- Statut: Résolu dans le dépôt; le primaire valide a été préservé pendant l'incident.
- Contexte: Smoke en mode Histoire après réparation hors ligne de l'affaire bloquée. Trois infractions confirmées ont créé un dossier actif, puis le joueur est mort pendant la poursuite afin de tester la persistance, le respawn et le transfert en prison.
- Symptôme: Au front de mort, `JusticeFlushStateNow` a rejeté le XML temporaire comme sémantiquement incohérent. L'ancien runtime a néanmoins poursuivi la capture, relié le protagoniste après respawn, conservé l'inventaire par échec fermé puis téléporté le joueur à Bolingbroke.
- Sources vérifiées:
  - `bug-reports\20260826-021421-justice-smoke-death-persistence-validation-20260826-021153\raw-logs\Grand-Theft-Auto-V-Enhanced__Scripts__DonJCustomNpcPlacer.log` ;
  - `bug-reports\20260826-021421-justice-smoke-death-persistence-validation-20260826-021153\windows-events\application-events.txt` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` et `DonJEnemySpawner.Justice.Custody.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` et `JusticeEnginePersistenceRegressionTests.cs`.
- Extraits utiles:
  - `02:11:53.139 [ERROR] Justice.Sauvegarde - Etat Justice temporaire incohérent; le primaire valide est conservé` ;
  - `02:11:53.196 [INFO] Justice.Capture - Capture apres mort en poursuite` ;
  - `02:12:04.043 [INFO] Justice.Detention - Identité du protagoniste reliée après son respawn` ;
  - `02:12:04.232 [WARN] Justice.Inventaire - Snapshot non validable : inventaire préservé et contrôles d'arme bloqués` ;
  - `02:12:05.417 [INFO] Justice.Detention - Entrée dans Prison de Bolingbroke`.
- Analyse / hypothèse: Le palier `Captured` et l'attente du nouveau ped n'étaient pas commis ensemble avant les effets externes. Le validateur voyait donc temporairement une phase de détention sans miroir de reprise cohérent, refusait le `.tmp`, mais l'ancien flux continuait quand même le transfert. Le remplacement atomique a correctement conservé le dernier primaire valide; aucune corruption du XML principal n'a été observée.
- Action menée: `BeginJusticeCapture` positionne maintenant l'attente de respawn dans le même état que la condamnation, marque l'état sale, exige la réussite de `JusticeFlushStateNow()` avant de libérer les cibles policières, effacer le wanted ou commencer le transfert, puis reprend ce palier de façon idempotente au chargement. Une identité inconnue reste en attente ou redevient un mandat et ne peut ni condamner, ni débiter, ni désarmer un autre protagoniste.
- Vérification: Les tests `DeathCapture_PersistsRespawnWaitBeforeItsFirstFlushAndBeforeTransfer`, `DeathCapture_UnknownIdentityPersistsUntilAProtagonistCanBeProven`, les round-trips XML v1, le fallback `.bak` et la reprise `Captured` verrouillent l'ordre du précommit. Le smoke a confirmé l'absence de nouveau crash natif, mais les ajustements finaux exigent encore un nouveau smoke de mort après le dernier déploiement.
- Résolution: Fenêtre de persistance fermée; toute mutation externe de capture est désormais bloquée tant que le XML cohérent n'est pas durable.

## 2026-08-26 02:24:21 +02:00 - Écarts gameplay révélés par le smoke Justice
- Statut: Corrigés dans le dépôt; validation GTA finale encore requise pour l'ensemble des ajustements visuels et de détention.
- Contexte: Retours utilisateur et audit du smoke couvrant la qualification des crimes, les témoins, la mort en détention, l'enceinte de Bolingbroke, le niveau de recherche d'évasion et les contrôles de combat du détenu.
- Symptôme: Un homicide pouvait être masqué par un tir dangereux corrélé ou par le quota de peds avant que les témoins vivants soient retenus. Une mort en prison pouvait laisser GTA réapparaître le joueur à l'hôpital malgré une peine active. La zone autorisée de Bolingbroke était trop proche de la cour centrale, l'évasion imposait quatre étoiles au lieu des trois demandées, et le verrou d'armes empêchait aussi la défense à mains nues face aux détenus.
- Sources vérifiées:
  - retour utilisateur associé au smoke du `2026-08-26` ;
  - `bug-reports\20260826-021421-justice-smoke-death-persistence-validation-20260826-021153\raw-logs\Grand-Theft-Auto-V-Enhanced__Scripts__DonJCustomNpcPlacer.log` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs`, `DonJEnemySpawner.Justice.Domain.cs` et `DonJEnemySpawner.Justice.Custody.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeEnginePersistenceRegressionTests.cs`, `JusticeCustodyHardeningTests.cs` et `JusticeRuntimeContractTests.cs`.
- Extraits utiles:
  - le smoke a bien enregistré les infractions à `02:11:02`, `02:11:14` et `02:11:40`, puis une évasion unique à `02:14:20` ;
  - `IncidentResolution_PrioritizesRelatedViolenceBeforeRecklessDischarge` protège la qualification homicide ;
  - `WitnessSnapshot_ReservesVictimsBeforePoliceAndLivingWitnesses` protège le quota de preuves ;
  - `CustodyRespawn_ReturnsAnExistingSentenceToItsCellWithoutReapplyingIt` protège le retour de l'hôpital ;
  - `JusticePolicy.EscapeMinimumWantedLevel` vaut exactement `3`.
- Analyse / hypothèse: Les défauts venaient de priorités et de volumes trop génériques: le buffer borné favorisait l'ordre de scan au lieu de la valeur probante, le respawn GTA n'était pas traité comme une reprise du même épisode de détention, la cour servait de quasi-frontière d'évasion et le verrou de sécurité confondait usage d'arme et mêlée sans arme.
- Action menée: La résolution privilégie désormais homicide et violence sur victime; la collecte réserve les victimes nécessaires à la qualification, puis les policiers vivants et les autres témoins crédibles vivants. Une mort avec peine active renvoie idempotemment le même profil dans la cellule du bon site sans rejouer condamnation, amende ou confiscation. Le volume autorisé couvre l'enceinte réelle de Bolingbroke et exige trois secondes continues à l'extérieur. L'évasion applique un minimum exact de trois étoiles sans diminuer un wanted GTA supérieur. Après confiscation vérifiée, le joueur peut combattre à mains nues; la discipline exige un nouveau dommage attribué ou un homicide prouvé.
- Vérification: Les tests comportementaux cités couvrent priorité/déduplication, témoins, retour cellule, volume intérieur/extérieur, délai continu, idempotence d'évasion, minimum de trois étoiles, mêlée sans arme et absence de discipline sur une simple posture de combat.
- Résolution: Les contrats gameplay signalés sont intégrés. Il reste à les confirmer ensemble lors d'un smoke manuel Bolingbroke/Mission Row sur le livrable final.

## 2026-08-26 02:57:26 +02:00 - Dossier Justice partagé entre protagonistes et commandes F10 incomplètes
- Statut: Corrigé dans le dépôt; build et smoke finaux à exécuter après stabilisation de toutes les branches partagées.
- Contexte: Relecture fonctionnelle après le smoke et demande utilisateur de pouvoir gérer Justice sans contaminer Michael, Franklin et Trevor entre eux.
- Symptôme: L'état v1 historique exposait un unique dossier actif au runtime; il fallait empêcher une affaire, une dette, une récidive ou une détention d'être adoptée par un autre protagoniste. La page F10 ne proposait pas encore le sélecteur de personnage, le paiement volontaire ni la remise à zéro ciblée. L'activation devait rester actionnable, et Justice ne devait plus fabriquer ou maintenir des étoiles lors d'un crime ordinaire.
- Sources vérifiées:
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs` et `DonJEnemySpawner.Justice.Payment.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.MenuUi.cs`, `DonJEnemySpawner.cs` et `DonJEnemySpawner.Justice.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`, `JusticeVoluntaryPaymentTests.cs`, `JusticeUiIntegrationObservabilityTests.cs` et `SafetySimulationTests.cs` ;
  - `README.md` et `docs\documentation-developpeur.md` pour les contrats finaux relus.
- Extraits utiles:
  - le XML v1 enrichi contient `PlayerProfiles/Profile` pour les slots canoniques `0`, `1` et `2`, avec miroir racine du seul profil actif ;
  - F10 expose `JusticeProfile`, `JusticePayFine` et `JusticeResetProfile` en plus de `JusticeEnabled` ;
  - l'unique appel restant à `SetJusticeWantedMinimum(...)` est celui de l'évasion et utilise `JusticePolicy.EscapeMinimumWantedLevel` ;
  - `JusticePolicy.MaxActiveFine = 1000000000000L` est une saturation technique, pas un plafond de gameplay.
- Analyse / hypothèse: Un état partagé ou une migration sans preuve canonique pouvait attribuer le passé judiciaire et les mutations externes au mauvais héros. Un paiement direct depuis le menu sans intention persistée aurait aussi créé une fenêtre de double débit. Enfin, l'ancien plancher wanted Justice doublonnait l'autorité native de GTA et produisait des étoiles artificielles.
- Action menée: Trois profils indépendants conservent dossier, casier, récidive, dette et détention. Une migration legacy n'est attribuée qu'au slot canonique prouvé; sinon elle reste non adoptée. Le sélecteur F10 ne change que la vue. Le paiement volontaire est lié au personnage canonique actuellement joué et utilise une intention durable/reprise idempotente. La réinitialisation demande une confirmation dédiée, cible uniquement le profil sélectionné et refuse d'effacer une récupération d'inventaire ou de détention encore nécessaire. Les crimes ordinaires et la reconnaissance d'un mandat lisent le wanted GTA mais ne l'écrivent jamais; seules l'évasion et l'amnistie explicite conservent leurs mutations séparées.
- Vérification: Les tests de round-trip/migration/refus sans preuve, isolation des trois profils, paiement réussi/rejeté/ambigu/repris, navigation F10, activation toujours accessible et confirmation de reset couvrent ces contrats. La relecture source confirme qu'aucun plancher wanted n'est appliqué à une infraction ordinaire.
- Résolution: Isolation par protagoniste, paiement, reset ciblé, toggle et propriété du wanted corrigés dans le dépôt; une validation complète du livrable et un smoke de changement de personnage restent requis.

## 2026-08-26 02:58:21 +02:00 - Incidents de recherche et premier patch refusé pendant la consolidation du journal
- Statut: Résolus sans mutation de données hors du présent ajout documentaire.
- Contexte: Audit read-only des appels wanted et du writer XML, puis tentative d'ajout append-only des entrées ci-dessus.
- Symptôme: Une commande a passé `DonJEnemySpawner.Justice*.cs` comme chemin Windows et a retourné `os error 123`; une seconde expression régulière mal protégée dans PowerShell a retourné `regex parse error: unclosed group`. Le premier `apply_patch` a ensuite été refusé parce que son contexte cherchait `Resolution` au lieu de l'intitulé accentué `Résolution` réellement présent.
- Sources vérifiées:
  - sorties console des commandes fautives et du patch refusé ;
  - relances corrigées sur `src\DonJEnemySpawner` ;
  - relecture de la fin de `crash-list.md` après l'échec du patch.
- Extraits utiles:
  - `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` ;
  - `regex parse error: unclosed group` ;
  - `apply_patch verification failed: Failed to find expected lines`.
- Analyse / hypothèse: Les recherches relevaient uniquement de la composition des commandes de lecture sous Windows. Le patch n'a trouvé aucune ancre à cause de l'accent manquant dans le contexte; la relecture a confirmé que le fichier n'avait pas évolué et qu'aucune écriture partielle n'avait eu lieu.
- Action menée: Le glob a été déplacé dans `rg -g 'DonJEnemySpawner.Justice*.cs'`, la seconde recherche a été relancée en chaînes fixes avec `-F -e`, puis le patch a été réancré sur `Résolution`.
- Vérification: Les relances ont retrouvé l'unique `SetJusticeWantedMinimum` d'évasion et le writer `Custody active` attendu. Le patch corrigé n'a touché que `crash-list.md`; aucun code, test, XML live ou binaire n'a été modifié.
- Résolution: Incidents d'outillage clos; les résultats corrigés ont été utilisés pour cette consolidation.

## 2026-08-26 03:21:19 +02:00 - Déploiement Release auxiliaire contenu et contrôle XML corrigé
- Statut: Contenu et restauré avant tout lancement GTA.
- Contexte: Test Release ciblé de la clarification UI héros joué/dossier consulté, alors que la validation globale Justice et les transactions de détention étaient encore en cours. GTA, Rockstar Launcher, OpenIV et CodeWalker étaient fermés.
- Symptôme: Une commande `dotnet test -c Release` auxiliaire a utilisé le `GtaScriptsDir` par défaut et la cible MSBuild `DeployAsEndll` a remplacé prématurément l'ENdll/PDB live par le build de travail. Une commande de contrôle read-only des XML a par ailleurs composé `$state+'.bak'` directement dans un littéral de tableau PowerShell, ce qui a produit une seconde entrée égale au primaire puis une recherche erronée de `.bak` sous le dépôt.
- Sources vérifiées:
  - sortie du test Release auxiliaire et staging corrigé `TestResults\ui-ledger-staging-20260826-0415\Scripts` ;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.ENdll` et son PDB ;
  - staging de référence `TestResults\justice-hardening-final-20260826-0205\Scripts` ;
  - XML primaire et backup sous `Scripts\DonJEnemySpawnerSaves` ;
  - liste des processus GTA/Rockstar et outils de modding.
- Extraits utiles:
  - live avant l'incident: ENdll `D66CA2B5B32487503AF226144A424415FE213C84D5AFACE5507AF122C8A6A511`, PDB `2DEEC1DD8B88749E2325E560A70F6B47683134691E288F6D1E1A91D10F75B017` ;
  - build auxiliaire copié: ENdll `362A2DEF2AE566DBC499395D4AAF82E31778CB83707B4785A31B723337C518FD`, PDB `0ECC20ECC7A602288FEEBB8AE437DC745E375801DAE3A61C18E3FF92C520992B` ;
  - erreur PowerShell read-only: `Cannot find path '.bak' because it does not exist` ;
  - XML primaire et `.bak` après correction: SHA-256 identique `3EA76ECC1C06020A5B7132363DA0B5566E8A4C8C9D2CBEBDC3FBA9E9C7852293`.
- Analyse / hypothèse: Le remplacement prématuré venait uniquement du hook Release du `.csproj`, pas d'une copie demandée par le correctif UI. Aucun processus n'a chargé ce binaire. L'erreur XML venait de la précédence d'expression PowerShell et n'a exécuté aucune écriture.
- Action menée: J'ai recherché le couple live antérieur par hash dans les stagings validés, restauré exclusivement l'ENdll et le PDB depuis `justice-hardening-final-20260826-0205`, puis comparé les hashes live. J'ai séparé le chemin backup dans `$backupState = "$state.bak"` avant de relire les deux XML. Tous les tests Release suivants sont désormais obligatoirement redirigés avec un `GtaScriptsDir` absolu de staging.
- Vérification: Les hashes live relus correspondent exactement à `D66CA2...A6A511` et `2DEEC1...5B017`; les XML primaire/backup n'ont changé ni de taille, ni d'heure, ni de hash. Aucun processus GTA ou outil de modding n'était actif pendant la restauration.
- Résolution: Incident sans impact en jeu, entièrement réversible et clos. Le futur déploiement final repartira d'une nouvelle sauvegarde hashée de ce couple restauré et ne copiera que l'ENdll/PDB après les validations globales.

## 2026-08-26 04:47:58 +02:00 - Échecs intermédiaires de tests et commandes de lecture pendant le durcissement final
- Statut: Résolus dans le dépôt; aucun fichier GTA ni XML live modifié.
- Contexte: Ajout des reprises transactionnelles, des profils séparés et des confirmations F10 avant la validation Release finale. Tous les builds concernés utilisaient Debug ou un staging local.
- Symptôme: Plusieurs recherches `rg` ont encore reçu un glob directement dans un chemin Windows et retourné `os error 123`; une expression régulière a produit `unclosed group`. Deux `apply_patch` atomiques ont été refusés sur un contexte devenu obsolète. Les suites intermédiaires ont successivement signalé des assertions source anciennes ou des helpers de réflexion ambigus : `2/367`, `5/370`, trois tests ciblés initiaux, `1/20`, `8/377`, `1/377` puis `2/100` en échec. Le test de restauration policière a aussi tenté une native via l'API NIB réelle hors GTA et reçu `FileNotFoundException` sur `NIBScriptHookVDotNet`.
- Sources vérifiées:
  - sorties `rg`, `apply_patch`, `dotnet build` et `dotnet test` de la session ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeVoluntaryPaymentTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeUiIntegrationObservabilityTests.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs`, `Justice.Profiles.cs`, `Justice.Payment.cs`, `Justice.Custody.cs` et `MenuUi.cs`.
- Extraits utiles:
  - recherche: `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` et `regex parse error: unclosed group` ;
  - patch: `apply_patch verification failed: Failed to find expected lines` ;
  - native headless: `Impossible de charger le fichier ou l'assembly 'NIBScriptHookVDotNet, Version=3.9.0.0'` lors de `Game.GameTime` ;
  - réflexion: `System.Reflection.AmbiguousMatchException` après l'ajout temporaire de surcharges portant le même nom ;
  - dernière suite corrigée: `378/378` tests Debug réussis, build avec `0` avertissement et `0` erreur.
- Analyse / hypothèse: Les erreurs `rg` et patch relevaient uniquement de commandes de développement. Les échecs MSTest décrivaient soit l'ancien appel simple `JusticeFlushStateNow`, soit l'ancienne modale dynamique, soit une invocation par nom devenue ambiguë. La panne NIB venait d'un test qui ne devait pas appeler une native réelle hors jeu.
- Action menée: J'ai remplacé les globs de chemin par `rg -g`, relu les contextes avant chaque nouveau patch, donné des noms uniques aux chemins confirmés, mis les contrats à jour pour le précommit redondant et remplacé l'appel natif headless par une vérification comportementale du round-trip XML et du routage runtime.
- Vérification: `dotnet build GTA5modDEV.sln -c Debug` puis `dotnet test GTA5modDEV.sln -c Debug --no-build` terminent avec zéro avertissement, zéro erreur et `378/378` tests réussis.
- Résolution: Incidents de développement clos. Aucun binaire installé, sauvegarde GTA, scène, RPF ou état Justice live n'a été écrit pendant ces échecs.

## 2026-08-26 04:47:59 +02:00 - Courses de profil et WAL incomplets découverts par la revue finale
- Statut: Corrigés et couverts en Debug; validation Release et déploiement encore requis.
- Contexte: Revue croisée read-only des confirmations F10, du reset en détention, des mutations wanted et des barrières primaire/backup avant effets GTA.
- Symptôme: Une confirmation d'amnistie pouvait viser Michael puis effacer le wanted de Franklin si le changement arrivait avant le tick; le reset actif en détention repassait par un garde recovery après avoir déjà restauré l'inventaire et pouvait boucler. Un paiement ouvert sur un dossier Franklin consulté capturait à tort le slot actif Michael. Enfin, les précommits globaux de capture, amnistie et reset ainsi que les tentatives wanted d'amnistie/évasion n'étaient présents que dans le primaire avant leurs effets externes; un fallback `.bak` pouvait donc perdre l'intention ou rejouer une mutation.
- Sources vérifiées:
  - `src\DonJEnemySpawner\DonJEnemySpawner.MenuUi.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Payment.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` ;
  - tests de profils, paiement, persistance, runtime et observabilité UI.
- Extraits utiles:
  - les modales persistent désormais `_pendingDangerJusticeProfileSlot`, le handle/modèle du ped joué, le nom affiché et la dette affichée ;
  - `ResumeJusticeActiveProfileResetTransaction` appelle `JusticeAmnestyCustody()` puis `ReplaceJusticePlayerProfileWithEmptyState(slot)` sans réutiliser le garde recovery obsolète ;
  - `BeginJusticeCapture`, `ExecuteJusticeAmnestyAndDisable`, `BeginJusticeActiveProfileResetTransaction`, `TryApplyJusticeAmnestyWantedClear` et `RetryJusticeEscapeWantedMinimum` passent par `PersistJusticeCriticalPrecommitRedundantly()` avant tout effet externe ;
  - le paiement consulté refuse d'ouvrir sa modale tant que `CanJusticeMenuPaySelectedProfile` n'identifie pas exactement le héros joué.
- Analyse / hypothèse: Ces fenêtres étaient trop courtes pour les tests gameplay ordinaires mais pouvaient mélanger état judiciaire, effets monde et fallback backup après un crash au mauvais instant. Le reset détenu avait en plus une contradiction logique : la restitution vidait les champs Custody sans sortir la phase judiciaire, puis le garde refusait le remplacement attendu.
- Action menée: La première validation capture une cible immuable et la seconde revalide slot canonique ou même ped custom déjà prouvé. Les transactions incompatibles bloquent reset/paiement. Le reset détenu utilise une primitive de remplacement interne seulement après restitution sous WAL. Chaque barrière précédant inventaire, téléportation, wanted, cible alliée ou transfert est écrite deux fois afin que primaire et `.bak` portent la même intention at-most-once.
- Vérification: Des tests couvrent le switch entre les deux Entrée, la cible consultée immuable, l'échec de commit du reset, le reset détenu, les WAL incompatibles, la restauration policière profil désactivé et le paiement du bon héros. La suite Debug finale réussit `378/378`.
- Résolution: Courses et fenêtres de reprise fermées dans le code. Aucun effet n'a été testé sur le jeu tant que le binaire final n'est pas validé et déployé.

## 2026-08-26 05:02:57 +02:00 - Ambiguïté entre précommit primaire et backup lors des reprises finales
- Statut: Corrigé et couvert par 380 tests Debug; validation Release et déploiement encore requis.
- Contexte: Dernière revue croisée du reset du détenu actif, de l'amnistie et du rollback de transfert après généralisation des deux écritures atomiques primaire/`.bak`.
- Symptôme: `PersistJusticeCriticalPrecommitRedundantly()` retournait le même `false` si la première écriture échouait ou si le primaire réussissait puis le backup échouait. Le reset retirait alors son opération et l'amnistie annonçait « non engagée » alors qu'un primaire durable pouvait ordonner leur reprise après redémarrage. Le rollback de transfert retirait de la même façon son opération en mémoire. Une recherche de contrôle a aussi répété le glob Windows invalide `DonJEnemySpawner.Justice*.cs` et retourné `os error 123`. Les premiers tests mis à jour ont signalé `4/378` échecs de contrats anciens, puis un test ciblé a refusé un XML intermédiaire artificiellement incohérent.
- Sources vérifiées:
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeEnginePersistenceRegressionTests.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs` ;
  - sortie Debug et `Scripts\DonJCustomNpcPlacer.log`.
- Extraits utiles:
  - recherche: `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)` ;
  - première suite: `échec : 4, réussite : 374, total : 378` ;
  - test ciblé: `Etat Justice temporaire incohérent; le primaire valide est conservé` ;
  - suite corrigée: `échec : 0, réussite : 380, total : 380`.
- Analyse / hypothèse: Annuler un latch après tout `false` contredisait l'état potentiellement déjà écrit au primaire. Le test ciblé avait en plus simulé les effets monde avant de réaffirmer le WAL après chargement, état désormais volontairement refusé par le validateur sémantique.
- Action menée: Les trois transactions conservent dorénavant leur intention sur tout échec et utilisent un cache runtime non persisté qui n'autorise les effets qu'après deux écritures réussies. Chaque chargement remet ce cache à faux et réaffirme primaire puis backup. Le rollback ne supprime plus son opération ambiguë. Deux tests comportementaux injectent maintenant un échec exact de la seconde écriture et vérifient la conservation du WAL, l'absence d'effet prématuré, puis l'accord du primaire et de `.bak`. Les assertions source ont été alignées sur ces nouvelles barrières.
- Vérification: `dotnet build GTA5modDEV.sln -c Debug` termine avec zéro avertissement et zéro erreur; `dotnet test GTA5modDEV.sln -c Debug --no-build` réussit `380/380`. `git diff --check` ne relève aucune erreur de whitespace, seulement les avertissements de conversion LF/CRLF existants.
- Résolution: Ambiguïté P1 fermée dans le code et les tests. Aucun binaire GTA ni XML Justice live n'a été modifié pendant l'incident.

## 2026-08-27 00:49:04 +02:00 - Incidents de contrôle pré-déploiement et livraison Justice validée
- Statut: Incidents d'outillage résolus; livrable sauvegardé, hashé et déployé.
- Contexte: Contrôle final des hashes de staging, des processus GTA et des quatre fichiers live avant la copie exclusive de l'ENdll/PDB.
- Symptôme: Une commande PowerShell de lecture a placé directement la sortie d'un `foreach` devant un pipe et a produit `An empty pipe element is not allowed`. Une recherche d'espaces a encore passé un glob dans le chemin Windows et est sortie en erreur malgré le succès préalable de `git diff --check`. Trois relances parallèles ont ensuite été interrompues pendant un changement de contexte de session et ont retourné des codes anormaux sans sortie.
- Sources vérifiées:
  - sorties des commandes PowerShell et `rg` concernées ;
  - staging `TestResults\justice-final-20260826-050530\Scripts` ;
  - dossier live `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts` ;
  - sauvegarde `C:\Users\nodig\GTA5modDEV-backups\justice-hardening-20260827-004810\pre-deploy`.
- Extraits utiles:
  - PowerShell: `ParserError: An empty pipe element is not allowed` ;
  - `rg`: glob Windows invalide, sortie `1` ;
  - anciens live: ENdll `D66CA2B5...A6A511`, PDB `2DEEC1DD...5B017` ;
  - nouveaux live: ENdll `023EBB5B...DE0359`, PDB `5DD6B3E2...D13409` ;
  - XML primaire et backup inchangés: `3EA76ECC...852293`.
- Analyse / hypothèse: Les deux premières erreurs relevaient uniquement de la composition de commandes read-only. Les sorties anormales suivantes correspondaient à l'interruption du shell par la transition de contexte, sans copie ni écriture partielle.
- Action menée: Les contrôles ont été relancés séquentiellement avec une collection PowerShell matérialisée et `rg -g`. L'ancien ENdll/PDB et les deux XML ont été copiés dans un dossier de sauvegarde borné, puis un manifeste SHA-256 a été créé. Après une nouvelle vérification d'absence de processus GTA/Rockstar/OpenIV/CodeWalker, seuls `DonJCustomNpcPlacer.ENdll` et son PDB validés ont remplacé leurs homologues live.
- Vérification: `run-safety-checks.ps1 -UseStubApi` réussit `385/385`; la suite contre l'API NIB installée et le staging final réussissent chacune `380/380`, avec zéro avertissement et zéro erreur. Les hashes live sont identiques au staging. Les deux XML conservent exactement leur taille de 5 505 octets et leur hash antérieur.
- Résolution: Déploiement terminé sans toucher aux sauvegardes Justice, aux scènes, aux RPF/OIV ni aux autres mods. Le rollback exact reste disponible dans le dossier de sauvegarde indiqué.

## 2026-08-27 01:45:53 +02:00 - Joueur gelé à l'entrée de Bolingbroke après capture sur mort
- Statut: Corrigé, testé et déployé dans le dépôt; smoke GTA de confirmation encore requis.
- Contexte: Test en mode Histoire sur le livrable Justice final. Après plusieurs infractions et une mort pendant une poursuite, Franklin a réapparu puis a été transféré à la prison de Bolingbroke.
- Symptôme: À l'arrivée en prison, le joueur joue l'animation de course mais reste immobile et ne peut pas se déplacer.
- Sources vérifiées:
  - `bug-reports\20260827-014540-bug-prison-course-sur-place\summary.md` ;
  - `bug-reports\20260827-014540-bug-prison-course-sur-place\raw-logs\Grand-Theft-Auto-V-Enhanced__Scripts__DonJCustomNpcPlacer.log` ;
  - `bug-reports\20260827-014540-bug-prison-course-sur-place\raw-logs\Grand-Theft-Auto-V-Enhanced__NIBScriptHookVDotNet.log` et `Grand-Theft-Auto-V-Enhanced__ScriptHookV.log` ;
  - `bug-reports\20260827-014540-bug-prison-course-sur-place\windows-events\application-events.json` ;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves\_justice_state.xml` et son `.bak` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` et le téléporteur partagé de `DonJEnemySpawner.Interiors.AdvancedLoading.cs`.
- Extraits utiles:
  - `01:44:21.275 [INFO] Justice.Capture - Capture apres mort en poursuite` ;
  - `01:44:26.114 [INFO] Justice.Detention - Identité du protagoniste reliée après son respawn` ;
  - `01:44:26.511 [WARN] Justice.Inventaire - Snapshot non validable : inventaire préservé et contrôles d'arme bloqués` ;
  - `01:44:28.165 [INFO] Justice.Detention - Entrée dans Prison de Bolingbroke` ;
  - le primaire et le backup XML portaient `phase="Incarcerated"`, `site="Bolingbroke"`, `playerStateStored="true"`, `storedInvincible="true"`, `storedFrozen="true"` et `storedCanRagdoll="false"` ;
  - NIB 2.11.6 et ScriptHookV ont chargé le mod sans exception, et aucun des 80 événements Windows collectés ne cible GTA, DonJ, NIB ou ScriptHook.
- Analyse / hypothèse: Le transfert démarrait dès que le nouveau ped redevenait vivant après le respawn. `StoreJusticeCustodyPlayerState` photographiait alors le drapeau transitoire `FreezePosition=true` encore imposé par GTA. `TeleportPlayerWithFadeSafe` gelait correctement le ped pendant le déplacement puis restaurait cet état d'entrée, tandis que Justice validait l'incarcération sans rétablir la mobilité. Le verrou d'inventaire concomitant ne désactive que le combat et les commandes d'armes; il n'était pas la cause du blocage locomoteur.
- Action menée: Un garde spécifique à la détention vérifie maintenant le dégel après un téléport réussi, avant de valider `Incarcerated`. Il corrige ensuite durablement `storedFrozen` afin que la libération ne réapplique pas le verrou, puis réaffirme la mobilité à chaque tick de détention et suspend la peine si GTA refuse le dégel. L'identité du ped est revérifiée et les drapeaux d'invincibilité/ragdoll restent intacts. Le téléporteur partagé des intérieurs n'a pas été modifié. Un premier patch composite a été refusé sans écriture faute de contexte exact; les trois changements ciblés ont ensuite été appliqués et relus séparément.
- Vérification: Les tests ciblés passent `12/12`. `tools\run-safety-checks.ps1` réussit avec `381/381` tests et le contrat `.ENdll`; sa variante `-UseStubApi` réussit `387/387`, dont le test comportemental du dégel. `dotnet build GTA5modDEV.sln -c Release` termine avec zéro avertissement et zéro erreur, puis `dotnet test GTA5modDEV.sln -c Release` réussit `381/381`. Aucun processus GTA/Rockstar/OpenIV/CodeWalker n'était actif lors du déploiement. Le livrable Release et l'ENdll live ont le même SHA-256 `F843A0CBDFFAF9A96733C9954820CA1074B6D16463356F3F7FD419A5BB0214AA`.
- Résolution: Le défaut est fermé dans le code et le binaire corrigé est déployé. Au prochain lancement, la reprise de la détention active doit défiger le joueur et réécrire le snapshot gelé; un smoke manuel à Bolingbroke puis Mission Row reste nécessaire pour confirmer le comportement dans GTA.

## 2026-08-27 02:01:16 +02:00 - Échec initial du test stub d'identité après dégel
- Statut: Résolu; aucune régression runtime ni mutation de sauvegarde GTA.
- Contexte: Validation `tools\run-safety-checks.ps1 -UseStubApi` du nouveau garde de mobilité de détention.
- Symptôme: La première suite stub a échoué sur `Detention_DegèleLeJoueurEtRépareLeSnapshotAprèsLeTransfert` avec une assertion attendant le rejet d'un ped dont seul le handle avait changé.
- Sources vérifiées:
  - `TestResults\safety-20260827-020031\safety-tests.trx` et `logs\test-release.log` ;
  - `bug-reports\20260827-020107-safety-failure\summary.md` ;
  - `tests\DonJEnemySpawner.Tests\StubRuntimeBehaviorTests.cs` ;
  - `JusticePolicy.IsCustodyLiveIdentityCompatible` dans `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Domain.cs`.
- Extraits utiles:
  - `Échoué! - échec : 1, réussite : 386, total : 387` ;
  - `Échec de Assert.IsFalse` dans `Detention_DegèleLeJoueurEtRépareLeSnapshotAprèsLeTransfert` ;
  - le contrat d'identité accepte volontairement un nouveau handle lorsque le slot canonique courant reste celui déjà détenu.
- Analyse / hypothèse: Le test assimilait à tort un changement de handle au remplacement de Franklin par un autre héros. Pour un protagoniste canonique, le slot `1` est la preuve forte qui autorise précisément le nouveau ped créé par un respawn; le garde a donc correctement accepté puis dégelé ce ped.
- Action menée: Le scénario négatif conserve le nouveau handle mais remplace aussi le modèle par `player_two`, ce qui expose le slot canonique Trevor `2` face au slot détenu Franklin `1`. Le test vérifie alors réellement qu'un autre protagoniste reste rejeté et gelé. Le collecteur de sécurité a conservé automatiquement les traces de l'échec initial.
- Vérification: La relance complète `tools\run-safety-checks.ps1 -UseStubApi` réussit `387/387`. Une reconstruction finale contre l'API NIB réelle termine avec zéro avertissement et zéro erreur, puis la suite standard réussit `381/381`.
- Résolution: Hypothèse de test corrigée sans modifier le garde runtime; le contrat de respawn canonique et l'isolation entre Franklin et Trevor sont tous deux couverts.

## 2026-08-27 02:08:54 +02:00 - Peine figée et HUD de prison conservé après un changement de personnage
- Statut: Corrigé, testé et déployé; smoke GTA de confirmation encore requis.
- Contexte: Franklin était incarcéré à Bolingbroke, puis le joueur a utilisé le changement de protagoniste du mode Histoire.
- Symptôme: Le temps de peine de Franklin restait affiché en haut à gauche et paraissait figé sur Michael ou Trevor. Les autres protagonistes ne voyaient donc pas uniquement leur propre casier judiciaire.
- Sources vérifiées:
  - `bug-reports\20260827-020845-bug-switch-personnage-hud-peine\summary.md` et les logs collectés associés ;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves\_justice_state.xml` et son `.bak` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs`, `DonJEnemySpawner.Justice.Custody.cs`, `DonJEnemySpawner.Justice.Domain.cs`, `DonJEnemySpawner.Justice.cs` et `DonJEnemySpawner.MenuUi.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`, `JusticeCustodyHardeningTests.cs`, `JusticeRuntimeEdgeContractTests.cs` et `JusticeUiIntegrationObservabilityTests.cs`.
- Extraits utiles:
  - le XML live contenait bien trois profils distincts: Michael et Trevor sans détention active, Franklin en phase `Incarcerated` à Bolingbroke avec un score de `1052`, `16` chefs et une peine restante de `1771` secondes dans le primaire (`1772` dans le backup immédiatement antérieur) ;
  - le log gameplay ne contenait aucune activation d'un nouveau profil judiciaire après le changement de héros ;
  - le HUD vérifiait seulement `_justiceEnabled && JusticeIsCustodyActive`, tandis que la mise à jour de la peine s'arrêtait dès que le héros joué ne correspondait plus au profil actif.
- Analyse / hypothèse: La détention active empêchait entièrement la bascule de profil judiciaire. L'ancien profil Franklin restait donc chargé: le HUD continuait à lire sa peine, mais le garde d'identité bloquait ensuite son horloge pour ne pas agir sur Michael ou Trevor. Cette combinaison expliquait exactement un compteur visible mais figé.
- Action menée: Une détention stable peut désormais être garée dans son profil lorsque GTA confirme un autre protagoniste. Son inventaire, son casier et son contexte de libération restent isolés. Sa peine avance en arrière-plan uniquement pendant le gameplay valide, sans rattrapage hors ligne, puis sa libération est finalisée quand ce protagoniste revient. Le HUD est rendu seulement pour le héros détenu actuellement joué. Le transfert restaure aussi les contrôles policiers globaux, matérialise les cooldowns, nettoie les tâches de scénario et récupère les jetons de suppression policière laissés par un crash avant d'activer l'autre profil.
- Vérification: La revue indépendante ne relève aucun défaut P0/P1/P2 et ses `50/50` tests ciblés passent. Les tests ciblés élargis passent `70/70`. `tools\run-safety-checks.ps1` réussit `388/388` dans `TestResults\safety-20260827-025751`; sa variante `-UseStubApi` réussit `395/395` dans `TestResults\safety-20260827-025716`. `dotnet build GTA5modDEV.sln -c Release` termine avec zéro avertissement et zéro erreur, puis `dotnet test GTA5modDEV.sln -c Release --no-build` réussit `388/388`.
- Résolution: Les trois protagonistes conservent chacun leur propre casier. La peine d'un détenu absent continue pendant une session de jeu valide sans apparaître sur les autres héros. Le livrable Release et l'ENdll live mesurent `648192` octets et partagent le SHA-256 `3D1DD2E352CABF25A13BE88FD1CF1FB15B5320A181DFE7E4148783DE230FBAA2`.

## 2026-08-27 02:32:20 +02:00 - Assertion source obsolète après centralisation de la suspension Justice
- Statut: Résolu; aucun impact runtime ni mutation de sauvegarde GTA.
- Contexte: Première suite complète après l'ajout de l'horloge des profils incarcérés non joués.
- Symptôme: Un test d'inspection source échouait parce qu'il exigeait encore l'affectation textuelle exacte `bool suspended = IsJusticeRuntimeSuspended(player)` alors que le calcul de suspension avait été ordonné plus tôt et mis en cache. Plusieurs recherches read-only ont aussi utilisé par erreur le glob Windows `DonJEnemySpawner.Justice*.cs`; `rg` a retourné `os error 123`, et une recherche a visé un ancien dossier `tests\...\Stubs` inexistant.
- Sources vérifiées:
  - `bug-reports\20260827-023213-test-runtime-suspension-horloge-profils\summary.md` ;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeEdgeContractTests.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs`.
- Extraits utiles:
  - première suite: `échec : 1, réussite : 385, total : 386` ;
  - recherche Windows: `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)`.
- Analyse / hypothèse: Le comportement était correct, mais le test était inutilement couplé à une forme locale du code. Les erreurs `rg` provenaient uniquement de la syntaxe de chemin et n'ont exécuté aucune écriture.
- Action menée: Le contrat vérifie maintenant l'ordre et l'utilisation de la suspension sans imposer le nom ni la déclaration exacte de la variable. Les recherches suivantes utilisent `rg -g` ou une liste explicite de fichiers.
- Vérification: Les suites finales standard et stub réussissent respectivement `388/388` et `395/395`; `git diff --check` est relancé au contrôle final.
- Résolution: Test aligné sur le comportement réel; incidents d'outillage read-only sans effet sur le dépôt ni le jeu.

## 2026-08-27 02:50:27 +02:00 - Échecs headless lors du renforcement des scénarios de retour de profil
- Statut: Résolu; aucun impact sur le jeu ni sur le XML live.
- Contexte: Ajout d'un scénario complet Michael détenu, bascule vers Franklin, persistance/rechargement, progression hors écran, puis retour vers Michael.
- Symptôme: Le premier passage a déclenché une `NullReferenceException` dans `ResetJusticeRuntimeFrontsForProfileChange`, puis un `FileNotFoundException` vers l'API NIB quand le sérialiseur de cooldown lisait directement `Game.GameTime` dans le runner headless.
- Sources vérifiées:
  - `bug-reports\20260827-025020-tests-switch-profils-retour-cooldowns\summary.md` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` et `DonJEnemySpawner.Justice.Profiles.cs`.
- Extraits utiles:
  - le second lecteur de test n'avait pas initialisé toutes les collections runtime attendues ;
  - `WriteJusticeActivityCooldownsXml` accédait directement à `Game.GameTime`, indisponible sans l'assembly GTA chargé.
- Analyse / hypothèse: Le scénario renforcé atteignait pour la première fois ces deux chemins avec une instance construite sans le constructeur ScriptHook. Le premier défaut appartenait au montage du test; le second révélait une dépendance headless évitable dans la sérialisation défensive.
- Action menée: Le harness initialise les collections runtime du second lecteur. Le sérialiseur utilise désormais `GetJusticeRawGameTimeSafe()`, déjà conçu pour retourner une base sûre quand l'API GTA ne peut pas être chargée.
- Vérification: Le scénario complet passe, puis les suites finales réussissent `388/388` en standard et `395/395` avec le stub.
- Résolution: Rechargement et conservation des cooldowns couverts sans dépendance directe à GTA dans les tests.

## 2026-08-27 02:51:56 +02:00 - Simulation artificielle d'un wrap d'horloge au retour du détenu
- Statut: Résolu dans le test; aucun défaut runtime observé.
- Contexte: Validation de la peine qui continue sur un profil inactif puis s'arrête dès que ce profil redevient actif.
- Symptôme: Le test attendait `599` secondes mais observait `597`, car il avait fixé manuellement le tick à `2500` avant de réactiver un lecteur headless dont l'horloge sûre revenait à `0`.
- Sources vérifiées:
  - `bug-reports\20260827-025151-test-retour-profil-horloge-headless\summary.md` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs` ;
  - logique de wrap dans `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs`.
- Extraits utiles:
  - assertion initiale: valeur attendue `599`, valeur réelle `597` ;
  - l'écart correspondait au budget plafonné appliqué volontairement lors d'un retour apparent de l'horloge entière.
- Analyse / hypothèse: Le test mélangeait deux instances et deux bases de temps sans fermer l'intervalle de la première. Le garde de wrap a donc traité la transition comme prévu au lieu de simuler le retour normal demandé.
- Action menée: Le test ferme l'intervalle actif et réinitialise explicitement la base avant la réactivation du profil.
- Vérification: Le scénario corrigé et les suites finales standard/stub passent intégralement.
- Résolution: Simulation réalignée sur un changement de profil normal; la couverture distincte du wrap entier reste conservée.

## 2026-08-27 02:52:53 +02:00 - Cache d'horloge arrière-plan réarmé sur le profil redevenu actif
- Statut: Corrigé et couvert; aucun état live modifié pendant la découverte.
- Contexte: Dernière assertion du scénario de retour vers un protagoniste dont la détention avait avancé hors écran.
- Symptôme: Après activation du détenu revenu à l'écran, `CanAdvanceCustodyInBackground` pouvait redevenir vrai pendant que la reprise de détention était encore en attente, ce qui risquait une décrémentation concurrente de sa peine active.
- Sources vérifiées:
  - `bug-reports\20260827-025248-cache-detention-arriere-plan-profil-actif\summary.md` ;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` et `DonJEnemySpawner.Justice.Profiles.cs` ;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`.
- Extraits utiles:
  - l'activation vidait correctement le cache, mais le snapshot suivant le recalculait à partir du seul état stable `Incarcerated` ;
  - le profil était déjà compatible avec le protagoniste joué et ne devait plus être éligible à l'horloge inactive.
- Analyse / hypothèse: L'éligibilité décrivait la stabilité de la détention mais omettait le critère décisif « ce profil n'est pas celui actuellement joué ».
- Action menée: `CanAdvanceCurrentJusticeCustodyInBackground` exige maintenant une incompatibilité de contexte runtime en plus de la stabilité de l'incarcération. L'activation et le retour du détenu ne peuvent donc plus réarmer ce cache.
- Vérification: Le test de retour vérifie explicitement que le cache reste faux. Les suites finales standard et stub réussissent respectivement `388/388` et `395/395`.
- Résolution: Une seule horloge peut désormais décrémenter la peine d'un profil donné: l'horloge de détention quand le héros est joué, ou l'horloge de profil inactif quand il ne l'est pas.

## 2026-08-28 03:31:30 +02:00 - Interruption externe de la première suite finale Justice
- Statut: Résolu; aucun test défaillant ni défaut runtime identifié.
- Contexte: Première exécution de `dotnet test GTA5modDEV.sln -c Release --no-build` après le dernier garde-fou de paiement volontaire.
- Symptôme: Le processus de test s'est arrêté après la découverte de l'assembly, sans résultat de test, avec le code Windows `1073807364` (`0x40010004`, terminaison externe du processus).
- Sources vérifiées:
  - sortie de la commande `dotnet test GTA5modDEV.sln -c Release --no-build` ;
  - `bug-reports\20260828-032947-dotnet-test-interrompu-40010004\summary.md` et journaux collectés associés ;
  - processus `dotnet`, `testhost` et `vstest.console` après l'interruption ;
  - relance complète avec `--logger "console;verbosity=normal"`.
- Extraits utiles:
  - première exécution: arrêt avant tout bilan avec `exit code 1073807364` ;
  - aucun processus de test résiduel après l'interruption ;
  - relance détaillée: `Nombre total de tests : 404`, `Réussi(s) : 404`.
- Analyse / hypothèse: Le code `0x40010004` indique une terminaison externe et non une assertion ou une exception d'un test. La relance détaillée a exécuté chaque test, y compris les nouveaux scénarios Justice, sans échec; les journaux collectés ne relient donc pas l'arrêt initial au code du mod.
- Action menée: Collecte complète via `tools\collect-bug-logs.ps1`, contrôle des processus résiduels, puis relance de la suite avec une sortie par test pour exclure un test fautif.
- Vérification: La relance complète réussit `404/404`. La suite standard réussit `404/404` dans `TestResults\safety-20260828-033154`, puis la suite stub réussit `412/412` dans `TestResults\safety-20260828-033308`. La reconstruction finale contre l'API NIB réelle termine avec zéro avertissement et zéro erreur.
- Résolution: Interruption d'outillage transitoire circonscrite; aucune modification fonctionnelle supplémentaire nécessaire.

## 2026-08-29 02:47:27 +02:00 - Échecs intermédiaires pendant la remédiation de l'audit Justice avancée
- Statut: Résolus; validations globales standard et stub réussies en fin d'intervention.
- Contexte: Remplacement de la persistance synchrone par des snapshots DTO hors thread, réduction du WAL, durcissement de la migration v1, de l'isolation de profil et du packaging game-ready.
- Symptôme: La première suite après conversion typée comptait `29` échecs sur `452`, principalement des tests encore couplés aux anciens flushs synchrones et au WAL XML complet. Les passages ciblés ont ensuite signalé un WAL refusé sans fichier à relire (`8/9`), un ancien manifeste de diagnostic (`25/26`), deux attentes obsolètes de paiement (`13/15`), un timeout de `2,5 s` avec le ledger maximal, une erreur de compilation dans le test de diagnostic, puis quatre fixtures de déploiement qui ne remplaçaient pas `sourceDirty` dans le JSON PowerShell indenté (`32/36`). Plusieurs recherches `rg` ont aussi utilisé un glob Windows invalide et renvoyé `os error 123`.
- Sources vérifiées:
  - `tests\DonJEnemySpawner.Tests\TestResults\full-after-typed.trx`;
  - `tests\DonJEnemySpawner.Tests\TestResults\targeted-packaging-wal.trx`;
  - tests Justice WAL, paiement, diagnostic, profils et packaging;
  - sources `DonJEnemySpawner.Justice.Persistence.*.cs`, `DonJEnemySpawner.Justice.Wal.cs` et scripts `tools\*-game-ready.ps1`.
- Extraits utiles:
  - packaging initial: `échec : 4, réussite : 32, total : 36`;
  - cause du fixture: le manifeste contenait `"sourceDirty":  true` alors que le remplacement exigeait exactement `"sourceDirty": true`;
  - relance ciblée: `échec : 0, réussite : 36, total : 36`;
  - build après correction: `0 Avertissement(s)`, `0 Erreur(s)`.
- Analyse / hypothèse: Les principaux échecs reflétaient des contrats de test devenus obsolètes après l'architecture asynchrone. Le timeout du ledger était spécifique à la barrière headless de test et non au thread gameplay. Le dernier lot packaging provenait exclusivement d'une hypothèse fragile sur les espaces du sérialiseur PowerShell; le garde-fou de déploiement rejetait correctement les sources sales.
- Action menée: Les tests sont réalignés sur les barrières de révision disque, le DTO de garde à vue et le WAL compact; la barrière réservée aux tests dispose d'un budget de `30 s`; les fixtures de manifeste utilisent une expression régulière tolérant l'indentation tout en exigeant le booléen `sourceDirty`; les recherches suivantes utilisent `rg -g` ou des chemins explicites.
- Vérification: Build Release réussi sans avertissement. Les `36` tests ciblés audit/WAL/packaging passent après correction; les suites de sécurité finales réussissent `467/467` en standard et `477/477` avec stub.
- Résolution: Incidents intermédiaires circonscrits et corrigés sans écrire dans les sauvegardes GTA ni déployer un package marqué non publiable.

## 2026-08-29 02:50:00 +02:00 - Glob `rg` invalide pendant la vérification documentaire Justice
- Statut: Résolu; erreur d'outillage sans impact sur le projet.
- Contexte: Relecture des appels à `PersistJusticeCriticalPrecommitToWal` avant d'aligner la documentation développeur et la matrice manuelle sur l'implémentation actuelle.
- Symptôme: La recherche `rg` a reçu le chemin `src/DonJEnemySpawner/*.cs`, que PowerShell/Windows n'a pas développé, et a renvoyé `os error 123`.
- Sources vérifiées:
  - sortie de la commande de relecture documentaire ;
  - fichiers source sous `src\DonJEnemySpawner` ;
  - état Git du dépôt.
- Extraits utiles:
  - `rg: src/DonJEnemySpawner/*.cs: IO error` ;
  - `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)`.
- Analyse / hypothèse: Le glob Unix placé dans un argument de chemin n'est pas valide pour cette invocation Windows de `rg`; aucun build, test, fichier runtime ou sauvegarde GTA n'a été touché.
- Action menée: La recherche est relancée sur le dossier explicite avec un filtre `-g '*.cs'`.
- Vérification: Le diff documentaire reste valide via `git diff --check`; la relance compatible Windows confirme les appelants recherchés.
- Résolution: Incident limité à la commande de recherche, sans modification fonctionnelle ni risque runtime résiduel.

## 2026-08-29 03:23:57 +02:00 - Échecs ciblés de persistance pendant la remédiation Justice avancée
- Statut: Résolu et validé globalement.
- Contexte: Vérification des profils typés, du retry du writer asynchrone et des changements de protagoniste après le passage au schéma Justice 2 et au repository hors thread GTA.
- Symptôme: La suite de sécurité stub a d'abord réussi `466/468`: le snapshot `Custody` typé d'un profil inactif restait nul après rechargement et un échec injecté du writer empêchait le retry d'une intention disciplinaire. Une suite ciblée profils a ensuite échoué `5` fois parce que quatre tests attendaient encore une bascule immédiatement committée et qu'un test cherchait une frame WAL déjà compactée. Une commande directe `dotnet test -p:UseStubApi=true`, lancée sans préparer le faux `GtaRoot` et les assemblies du stub, a produit des erreurs de compilation de harness sans révéler de défaut produit. Enfin, une build lancée pendant une édition parallèle de l'inventaire a momentanément trouvé six appels encore typés comme des booléens.
- Sources vérifiées:
  - `bug-reports\20260829-030000-safety-failure` et `TestResults\safety-20260829-025701\logs\test-release.log`;
  - `bug-reports\20260829-032357-remediation-audit-justice-echecs-intermediaires-2`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Persistence.Runtime.cs`, `DonJEnemySpawner.Justice.Persistence.TypedCustody.cs` et `DonJEnemySpawner.Justice.Profiles.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`, `JusticeRuntimeContractTests.cs` et `JusticeCustodyHardeningTests.cs`.
- Extraits utiles:
  - sécurité stub initiale: `échec : 2, réussite : 466, total : 468`;
  - profils ciblés intermédiaires: `échec : 5`, puis `réussite : 32/32` après réalignement et correction;
  - build concurrente intermédiaire: six erreurs de conversion de `JusticeInventoryRemovalResult` vers `bool`;
  - la commande stub directe ne disposait ni du runtime simulé préparé par `run-safety-checks.ps1`, ni des références GTA factices attendues.
- Analyse / hypothèse: Le lecteur v2 validait correctement les fragments XML mais ne matérialisait pas encore le DTO de garde à vue des profils inactifs. Le circuit de retry observait l'échec avant de conserver le fait qu'une nouvelle tentative était déjà due, ce qui repoussait indéfiniment la révision réparée. Les autres échecs provenaient de contrats de tests synchrones obsolètes, d'une inspection trop tardive après compaction, d'une invocation stub incomplète et d'un état transitoire d'édition parallèle.
- Action menée: Le chargement v2 hydrate désormais chaque `Custody` typé sous son propre profil puis restaure l'état runtime courant. Le writer mémorise si le retry était dû avant d'observer l'échec et autorise alors la révision réparée à remplacer le snapshot fautif. Les changements de profil attendent explicitement leur `DiskRevision`; les tests utilisent cette barrière et vérifient le rejet avant compaction. L'état ternaire de retrait d'inventaire a été propagé à tous les appelants. Les validations stub finales passent exclusivement par `tools\run-safety-checks.ps1 -UseStubApi`.
- Vérification: Les profils passent finalement `35/35`, les DTO/custody `6/6`, l'inventaire stub `21/21`, puis les suites de sécurité complètes `467/467` en standard et `477/477` avec stub.
- Résolution: Les causes sont circonscrites et couvertes; aucune sauvegarde GTA ni installation live n'a été modifiée par ces échecs intermédiaires.

## 2026-08-29 03:23:58 +02:00 - Erreurs d'outillage read-only pendant la revue Justice avancée
- Statut: Résolu; aucun fichier fonctionnel ni état GTA affecté.
- Contexte: Recherches croisées et lecture de plages source pendant l'analyse des correctifs de persistance, d'inventaire et de packaging.
- Symptôme: Plusieurs commandes `rg` ont reçu des globs de chemin Windows non développés (`DonJEnemySpawner.Justice.Persistence.*.cs`, `src/DonJEnemySpawner/*.cs` et `tests/*.cs`) et ont renvoyé `os error 123`. Une lecture PowerShell a calculé `Select-Object -Skip -1` après avoir recherché le symbole dans le mauvais fichier. Une première tentative `apply_patch` de l'agent inventaire n'a pas trouvé son contexte et n'a appliqué aucune modification.
- Sources vérifiées:
  - sorties terminal des commandes de recherche et de lecture;
  - `bug-reports\20260829-032357-remediation-audit-justice-echecs-intermediaires-2`;
  - `bug-reports\20260829-033231-revue-inventaire-packaging-glob-rg-invalide`;
  - `bug-reports\20260829-035958-rg-backreference-non-supportee-controle-docs`;
  - état Git et diffs des sources/tests concernés.
- Extraits utiles:
  - `La syntaxe du nom de fichier, de répertoire ou de volume est incorrecte. (os error 123)`;
  - `Cannot validate argument on parameter 'Skip'. The -1 argument is less than the minimum allowed range of 0`;
  - la tentative de patch a signalé un contexte introuvable sans écrire de hunk.
- Analyse / hypothèse: Ces incidents viennent uniquement de syntaxes de recherche incompatibles avec la résolution des globs sous Windows et d'un offset absent. Aucun processus de build, aucune native et aucune sauvegarde n'étaient impliqués.
- Action menée: Les recherches suivantes utilisent des dossiers explicites avec `-g '*.cs'` ou des listes de chemins littéraux; les plages PowerShell sont bornées après vérification de la position; le patch inventaire a été recalé sur le contenu réellement relu.
- Vérification: Les tests inventaire passent `20/20` en standard et `21/21` avec stub, les tests packaging passent `11/11`, et `git diff --check` sera relancé au contrôle final.
- Résolution: Incidents d'outillage documentés et sans risque runtime résiduel.
- Occurrences supplémentaires: Les revues read-only finales inventaire/packaging et profils ont reproduit le même `os error 123` avec `src/DonJEnemySpawner/*.cs`, puis `tests/DonJEnemySpawner.Tests/*.cs`; aucun fichier n'a été modifié et les recherches ont été reprises sur les dossiers avec un filtre `-g`.
- Contrôle documentaire final: une expression `rg` a utilisé une backreference `\1`, non supportée par le moteur par défaut. La recherche a été simplifiée sans `--pcre2`; aucun fichier n'a été modifié.

## 2026-08-29 03:26:19 +02:00 - Build ciblée interrompue par une édition concurrente du WAL financier
- Statut: Résolu après stabilisation du fichier partagé.
- Contexte: La revue indépendante venait d'ajouter un test stub reproduisant un livelock de bascule de protagoniste quand une restauration de suppression policière est déjà engagée.
- Symptôme: La compilation ciblée s'est arrêtée dans `DonJEnemySpawner.Justice.Persistence.Runtime.cs(989,47)` avec `CS0103`, car la variable locale `diskRevision` n'existait pas encore dans une portion simultanément remaniée pour le WAL financier.
- Sources vérifiées:
  - sortie de la commande de test ciblée de la revue profils;
  - `bug-reports\20260829-032646-build-concurrent-wal-financier-cs0103`;
  - diff courant de `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Persistence.Runtime.cs`;
  - diff indépendant de `DonJEnemySpawner.Justice.Profiles.cs` et `JusticePlayerProfilePersistenceTests.cs`.
- Extraits utiles:
  - `error CS0103: Le nom 'diskRevision' n'existe pas dans le contexte actuel`;
  - le défaut de compilation est extérieur aux deux fichiers du correctif de profil et correspond à un hunk encore incomplet de l'agent financier.
- Analyse / hypothèse: Il s'agit d'un état transitoire du worktree partagé, pas d'un échec du scénario runtime. Lancer le test avant la fin de l'édition concurrente ne permet pas d'évaluer le correctif.
- Action menée: Aucune modification de contournement n'est appliquée dans le test ou les profils. La revue attend l'intégration complète du WAL financier, puis relancera la compilation et le scénario stub sur l'état cohérent.
- Vérification: Les deux nouveaux scénarios profils passent `2/2`, la classe complète `35/35`, le build Release final termine avec zéro avertissement/zéro erreur, puis les suites standard/stub passent intégralement.
- Résolution: État transitoire supprimé; aucun effet sur GTA, ses sauvegardes ou le package.

## 2026-08-29 03:30:08 +02:00 - Attente synchrone obsolète dans le test de restauration policière
- Statut: Résolu et validé.
- Contexte: Validation complète des profils après correction du livelock de bascule et de la course sur le compteur d'échecs du repository.
- Symptôme: Les deux nouveaux scénarios stub passent `2/2`, mais la classe profils réussit `34/35`: `PlayerProfiles_InactivePoliceTokensAreRestoredAndClearedAfterCrash` attend encore que le premier appel de restauration efface immédiatement les jetons, alors que l'architecture asynchrone doit d'abord rendre son snapshot durable puis finaliser le WAL au tick suivant.
- Sources vérifiées:
  - `bug-reports\20260829-033008-test-profils-police-wal-asynchrone`;
  - `bug-reports\20260829-033048-test-profils-wal-compacte-avant-assertion`;
  - sortie de la suite ciblée `JusticePlayerProfilePersistenceTests`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Profiles.cs`, `DonJEnemySpawner.Justice.Custody.cs` et `DonJEnemySpawner.Justice.Persistence.Runtime.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`.
- Extraits utiles:
  - suite ciblée: `échec : 1, réussite : 34, total : 35`;
  - le premier appel réarme volontairement les jetons runtime tant que la barrière `SetJusticeCustodyPoliceSuppression` n'a pas atteint sa `DiskRevision`.
- Une seconde passe a encore réussi `34/35`: le nouveau scénario lisait obligatoirement la dernière frame après la bascule, mais le writer avait déjà confirmé puis compacté le WAL; le slot `1` était pourtant correctement actif et aucune frame `Rejected` n'avait été observée.
- Analyse / hypothèse: Le test conservait le contrat de l'ancien précommit XML synchrone. Le runtime actuel ne doit ni bloquer GTA sur le writer, ni prétendre la restauration durable avant sa révision disque.
- Action menée: Le scénario historique est réaligné sur le protocole réel: premier appel, attente de la révision disque réservée au harness, puis second appel qui finalise le petit WAL et acquitte les jetons. Le nouveau test accepte également qu'une transaction terminale ait déjà été compactée; si une frame subsiste, il interdit toujours explicitement l'état `Rejected`.
- Vérification: Les deux nouveaux tests de livelock/course passent `2/2`; la classe profils passe `35/35`, puis les suites globales standard/stub réussissent.
- Résolution: Écart de test sans mutation GTA; le contrat asynchrone reste inchangé.

## 2026-08-29 03:35:02 +02:00 - Contrat source de débit obsolète après durcissement du WAL financier
- Statut: Résolu et validé.
- Contexte: Revue finale read-only combinant les garde-fous d'inventaire et de packaging pendant l'intégration du nouveau protocole de débit.
- Symptôme: La sélection ciblée réussit `30/31`; seul `FineDebit_PersistsSucceededRejectedAndUnknownOutcomes` échoue parce qu'il exige encore le marqueur `PersistJusticeCriticalPrecommitRedundantly()`, supprimé du chemin financier au profit de la nouvelle barrière snapshot durable puis WAL `Attempted` juste avant l'effet cash.
- Sources vérifiées:
  - `bug-reports\20260829-033502-test-custody-contrat-precommit-financier-obsolete`;
  - sortie de la sélection `JusticeCustodyHardeningTests|PackagingSafetyTests`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs` et `DonJEnemySpawner.Justice.Persistence.Runtime.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticeCustodyHardeningTests.cs`.
- Extraits utiles:
  - `échec : 1, réussite : 30, total : 31`;
  - assertion manquante: ancien appel textuel `PersistJusticeCriticalPrecommitRedundantly()`.
- Analyse / hypothèse: Le test inspecte une implémentation remplacée volontairement. Conserver cet appel pour satisfaire le texte réintroduirait précisément le précommit XML synchrone dénoncé par JUS-003.
- Action menée: Le contrat doit vérifier l'ordre effectif `snapshot durable -> WAL Prepared/Attempted -> SET cash`, l'identité transactionnelle stable et l'absence de replay après une frame `Attempted`, sans dépendre du nom de l'ancien helper.
- Vérification: Le build Release passe avec zéro avertissement et zéro erreur; paiements + contrat FineDebit passent `18/18`, inventaire + packaging `31/31`, puis les suites de sécurité globales réussissent.
- Résolution: Aucun effet GTA ni déploiement; écart de test circonscrit au contrat source historique.

## 2026-08-29 03:36:23 +02:00 - Fenêtre sans binaire chargeable lors du retrait anticipé des alias historiques
- Statut: Corrigé, testé et revu sans finding P0–P2 résiduel.
- Contexte: Relecture indépendante de `tools\deploy-game-ready.ps1` après l'ajout du rollback des alias verrouillés.
- Symptôme: Les anciens alias étaient déplacés vers des backups cachés avant la publication du nouveau triplet. Une interruption brutale du processus ou de Windows à cette frontière, sur une installation ne contenant que `DonJEnemySpawner.ENdll`, pouvait donc laisser temporairement zéro ENdll chargeable; le `catch` PowerShell ne peut pas réparer un processus tué.
- Sources vérifiées:
  - `bug-reports\20260829-033623-deploy-interruption-avant-publication-alias`;
  - `tools\deploy-game-ready.ps1`, séquence de staging, remplacement et alias;
  - `tests\DonJEnemySpawner.Tests\PackagingSafetyTests.cs`;
  - critères JUS-001/JUS-002 de l'audit Justice avancée.
- Extraits utiles:
  - l'ancienne séquence déplaçait les alias aux lignes de transaction précédant `Install-StagedFile` pour `DonJCustomNpcPlacer.ENdll`;
  - les tests couvraient une exception normale sur alias verrouillé, mais pas l'ordre garantissant qu'un nouveau binaire vérifié existe avant leur retrait.
- Analyse / hypothèse: Le rollback était correct pour une exception interceptable, mais l'ordre de commit ne résistait pas à une coupure de processus. La priorité est qu'un ENdll valide reste présent; un intervalle où nouveau nom et alias coexistent pendant GTA fermé est moins dangereux qu'un intervalle sans aucun binaire.
- Action menée: Le déploiement publie et relit d'abord ENdll, PDB et manifest. Il ne met les alias à l'abri qu'après validation du nouveau triplet. Un alias verrouillé restaure les alias déjà déplacés puis déclenche le rollback inverse des trois nouveaux fichiers. Un test de contrat vérifie cet ordre en plus du scénario de verrou.
- Vérification: `PackagingSafetyTests` passe `12/12` après ajout du contrat du harness; le groupe inventaire + packaging passe `31/31`, les hashes build/package sont identiques et les deux chaînes de sécurité réussissent.
- Résolution: Ordre de publication corrigé; aucun déploiement live n'a été exécuté pendant la découverte.

## 2026-08-29 03:44:43 +02:00 - Reprise financière depuis un backup Prepared et contrats runtime obsolètes
- Statut: Corrigé et validé globalement.
- Contexte: Tests déterministes du nouveau protocole financier `snapshot Prepared durable -> WAL Attempted -> effet cash`, puis régression complète des contrats Justice runtime.
- Symptôme: La première passe paiement réussissait `13/15`: `VoluntaryPayment_CancelledPreparedIntentCannotBeResurrectedFromBackup` et l'ancien `VoluntaryPayment_SecondPrecommitFailureReloadsPersistedIntentIdempotently` supposaient encore un WAL immédiat/une reprise en un seul appel. Le scénario backup a révélé un risque réel: un abandon avant armement ne laissait aucune frame terminale; si le primaire final devenait corrompu, son `.bak` encore `Prepared` pouvait ressusciter l'intention et débiter. La passe `JusticeRuntimeContractTests` réussissait ensuite `67/70`: deux marqueurs FineDebit historiques et `JusticeEscape_PersistsDiscardIntentBeforeRemovalThenCommitsFugitiveState` attendaient des helpers remplacés.
- Sources vérifiées:
  - `bug-reports\20260829-033933-tests-paiement-wal-backup-prepared`;
  - `bug-reports\20260829-034443-runtime-contracts-finance-evasion-inventaire`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Payment.cs`, `DonJEnemySpawner.Justice.Custody.cs` et `DonJEnemySpawner.Justice.Persistence.Runtime.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticeVoluntaryPaymentTests.cs`, `JusticeRuntimeContractTests.cs`, `JusticeCustodyHardeningTests.cs` et `JusticeWalRecoveryTests.cs`.
- Extraits utiles:
  - paiement initial: `échec : 2, réussite : 13, total : 15`;
  - contrats runtime intermédiaires: `échec : 3, réussite : 67, total : 70`;
  - le chemin d'abandon avant `TryArm` possédait un snapshot `Prepared` mais aucun tombstone WAL empêchant sa reprise depuis le backup.
- Analyse / hypothèse: La durabilité du primaire ne suffit pas si le backup précédent reste sémantiquement ouvert. Par ailleurs, les trois inspections source décrivaient l'ancien précommit redondant et un reset d'évasion inconditionnel, incompatibles avec la persistance asynchrone et la conservation d'un inventaire `EffectMayHaveApplied`.
- Action menée: Chaque effet financier utilise un identifiant stable et des champs immuables liés au slot, à la génération, à l'identité, au schéma et à l'épisode. Le snapshot `Prepared` doit atteindre `DiskRevision`; les frames `Prepared` puis `Attempted` précèdent immédiatement `STAT_SET_INT`; une reprise `Attempted/Ambiguous` ne rejoue jamais le SET. Même une annulation sans effet écrit désormais `Prepared -> Rejected`, et les tombstones `Rejected/Confirmed` restent présents jusqu'à un remplacement disque supplémentaire qui avance aussi le backup. Le test perdu après ACK est renommé `VoluntaryPayment_LostPreparedAcknowledgementRetriesWithoutDoubleDebit`; des coupures sur ACK Prepared, troncature Attempted et précondition `FineDue` durable sont ajoutées. L'évasion conserve et restaure tout snapshot dont l'effet a pu s'appliquer au lieu de le jeter.
- Vérification: Build Release `0` avertissement/`0` erreur; paiements + contrat FineDebit `18/18`; WAL/Custody/Audit `46/46`; cinq scénarios financiers critiques `5/5`; évasion + inventaire `23/23`; classe `JusticeRuntimeContractTests` `70/70`.
- Résolution: Résurrection de débit depuis backup fermée, aucun replay après `Attempted`, ambiguïté d'inventaire préservée; aucun cash ni fichier GTA live touché pendant les tests.

## 2026-08-29 03:49:48 +02:00 - Refus attendu d'un package sale traité comme échec fatal par le harness
- Statut: Corrigé et validé par les deux chaînes de sécurité.
- Contexte: Première validation globale après intégration complète des correctifs de l'audit Justice avancée depuis un worktree volontairement modifié.
- Symptôme: La build stub réussit sans avertissement et les tests passent `476/476`. Le package local `sourceDirty=true` est correctement refusé par `deploy-game-ready.ps1`, mais son stderr natif devient un `NativeCommandError` sous `$ErrorActionPreference = "Stop"`; `run-safety-checks.ps1` s'arrête donc avant de vérifier que le code non nul était précisément le résultat attendu.
- Sources vérifiées:
  - `bug-reports\20260829-034935-safety-failure`;
  - `TestResults\safety-20260829-034734\safety-tests.trx`;
  - `TestResults\safety-20260829-034734\game-ready\manifest.json` et `deploy-dirty-refusal.log`;
  - `tools\run-safety-checks.ps1` et `tools\deploy-game-ready.ps1`.
- Extraits utiles:
  - tests: `échec : 0, réussite : 476, total : 476`;
  - manifest: `sourceDirty: true`, schéma Justice `2`, SHA-256 ENdll `0B3F584A48B722A06C3C380F105672013307F204022124E79FC2038F6A070029`;
  - sortie finale: `NativeCommandError` sur `Manifest game-ready invalide`, qui est le refus de sécurité attendu.
- Analyse / hypothèse: Le produit, le package et le garde de déploiement se comportent correctement. Seul le harness confondait le stderr d'un test négatif avec une panne avant de pouvoir lire `$LASTEXITCODE`.
- Action menée: La branche locale attendue capture temporairement ce sous-processus sous `ErrorActionPreference=Continue`, mémorise son code, restaure impérativement la politique stricte en `finally`, puis échoue uniquement si le package sale a été accepté. Un test de contrat vérifie l'ordre assouplissement local, invocation, capture et restauration.
- Vérification: Test packaging ciblé `12/12`; relance stub `477/477` dans `TestResults\safety-20260829-035146`; relance standard `467/467` dans `TestResults\safety-20260829-035409`; build Release séparé zéro avertissement/zéro erreur et test séparé `467/467`.
- Résolution: Défaut limité au harness et corrigé; aucun déploiement live ni mutation des sauvegardes GTA.

## 2026-08-29 04:06:06 +02:00 - Fixtures packaging dépendants de la propreté du checkout CI
- Statut: Résolu; validations locales et workflow GitHub réussis.
- Contexte: Premier workflow `Safety` déclenché sur la branche principale après le commit `d8650a3` de remédiation Justice avancée.
- Symptôme: La build CI réussit sans avertissement ni erreur, mais la suite termine à `471/477`. Six tests de déploiement fabriquent leur package depuis un checkout GitHub propre: le manifest porte donc déjà `sourceDirty=false`. Le helper de fixture supposait au contraire un worktree local sale; ses remplacements `true -> false` ne changeaient rien et le scénario censé vérifier le refus d'un package sale déployait un package propre.
- Sources vérifiées:
  - workflow GitHub Actions `Safety` n° `33227958962`, job `99035245792`;
  - log distant de l'étape `Run safety suite` et TRX publié par la CI;
  - `bug-reports\20260829-040606-ci-packaging-fixtures-source-propre`;
  - `tests\DonJEnemySpawner.Tests\PackagingSafetyTests.cs`, helper `CreateVerifiedPackage`.
- Extraits utiles:
  - build CI: `0 Warning(s)`, `0 Error(s)`;
  - tests CI: `Failed: 6, Passed: 471, Total: 477`;
  - assertion récurrente: `Le fixture local doit être explicitement marqué publiable pour tester le déploiement` alors que le manifest affichait déjà `sourceDirty: false`.
- Analyse / hypothèse: Le produit et le script de déploiement ne sont pas en cause. Le fixture dérivait implicitement son état sale/propre du dépôt hôte, ce qui rendait le test non déterministe entre le worktree de développement et un checkout CI propre.
- Action menée: `CreateVerifiedPackage` remplace désormais explicitement `sourceDirty=true|false` par la valeur demandée pour chaque scénario, vérifie la présence du champ puis redécode le manifest pour confirmer la politique. Les tests de package sale et publiable deviennent indépendants de l'état Git réel.
- Vérification: `PackagingSafetyTests` réussit `12/12`; `tools\run-safety-checks.ps1 -UseStubApi` réussit `477/477` dans `TestResults\safety-20260829-040723`, avec build zéro avertissement/zéro erreur et package local vérifié. La build Release standard termine aussi à zéro avertissement/zéro erreur et la suite standard réussit `467/467`. Le workflow GitHub `Safety` n° `33228416152` valide le checkout propre, la suite complète et la publication du package prêt pour le jeu.
- Résolution: Incident clos après validation CI; aucun déploiement GTA live ni donnée joueur affecté, et la correction reste limitée au déterminisme des tests.

## 2026-08-29 04:40:56 +02:00 - F10 inactif après déploiement du package CI
- Statut: Cause corrigée, CI validée et binaire compatible redéployé; preuve au prochain lancement GTA en attente.
- Contexte: GTA V Enhanced lancé après installation du package `DonJCustomNpcPlacer-game-ready` du workflow `Safety` n° `33228580705`, commit `d6de9d20e01181156acc812c3a28d043a050bf88`.
- Symptôme: La touche F10 n'ouvre plus le menu et aucune nouvelle ligne de chargement DonJ n'apparaît dans le log runtime.
- Sources vérifiées:
  - `bug-reports\20260829-044041-f10-menu-ne-souvre-plus`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`;
  - ENdll/PDB/manifest installés sous `Grand Theft Auto V Enhanced\Scripts`;
  - identité de l'API live `NIBScriptHookVDotNet2.dll` et références d'assembly des builds local/CI;
  - `.github\workflows\safety.yml`, `tools\run-safety-checks.ps1`, `tools\package-game-ready.ps1` et projet du stub v2.
- Extraits utiles:
  - NIB à `04:38:45`: `Failed to load assembly DonJCustomNpcPlacer.ENdll: System.Collections.Generic.KeyNotFoundException` dans `RegisterScriptTypesInAssembly`;
  - binaire CI installé: référence `NIBScriptHookVDotNet2, Version=1.0.0.0`, SHA-256 `DD45213F95F89E45F644A2C3408D4D321E3FE87CA4BCE7BFD45B29351AA5018F`;
  - API live: `NIBScriptHookVDotNet2, Version=2.11.6.0`, SHA-256 `DBF8FC318730D7101E945D0F4B6552E34C8559BEEE6978826D5067329358CB71`;
  - Ironman et NIBMods sont instanciés normalement, ce qui exclut une panne générale du loader.
- Analyse / hypothèse: Le workflow publiait le binaire compilé avec `-UseStubApi`, mais le projet du stub ne fixait aucune `AssemblyVersion` et produisait donc implicitement `1.0.0.0`. Le chargeur NIB indexe les types GTA par version majeure de l'API référencée; la clé `1` n'existe pas lorsque seules les API `2` et `3` sont chargées. Le script est rejeté avant constructeur, `OnTick` et handler F10. Les hashes du manifest prouvaient uniquement que ce mauvais binaire avait été copié intact.
- Action menée: Le stub porte désormais l'identité `2.11.6.0`. Packaging, suite de sécurité et déploiement inspectent indépendamment la référence NIB/SHVDN, exigent exactement une API de version majeure `2` et publient son identité dans le manifest. Deux tests couvrent l'identité du stub, les métadonnées du binaire et le refus d'un manifest API incompatible.
- Vérification: Build du stub réussi sans avertissement ni erreur et identité relue `2.11.6.0`; build Release stub réussi sans avertissement ni erreur; `PackagingSafetyTests` réussit `14/14`. Une première exécution ciblée à `13/14` a révélé un double chargement ReflectionOnly limité au test; la lecture a été fusionnée puis la suite ciblée a été relancée avec succès. `tools\run-safety-checks.ps1 -UseStubApi` réussit `479/479` dans `TestResults\safety-20260829-045037` et relit l'API `2.11.6.0`. La build Release contre l'API GTA réelle réussit à zéro avertissement/zéro erreur, son ENdll référence `2.11.6.0`, puis la suite standard réussit `469/469`. Le workflow GitHub `Safety` n° `33230266514` du commit `894459172a32bb678e7d71fe702eb7d7325d1264` réussit et publie un package propre. L'artefact relu référence une unique API `NIBScriptHookVDotNet2 2.11.6.0`; son ENdll de SHA-256 `9AEF6FD659F3B3760E04DACE4C13DEC7EDD984ED6C70E4163B3C748B2A886A1E` est déployé avec son PDB et son manifest pendant que GTA est fermé. Les hashes installés correspondent au manifest; aucun alias obsolète ni fichier de transaction ne subsiste.
- Résolution: Le binaire incompatible `1.0.0.0` a été remplacé par l'artefact CI compatible v2 et les pipelines refusent désormais cette régression avant publication ou copie. La preuve finale dépend du prochain lancement GTA: le log NIB doit instancier `DonJEnemySpawner` et F10 doit rouvrir le menu.

## 2026-08-29 05:14:14 +02:00 - Assertion documentaire sensible au retour à la ligne Markdown
- Statut: Corrigé et test ciblé validé.
- Contexte: Première exécution de `PackagingSafetyTests` après ajout du contrat qui verrouille les instructions d'installation et la publication du package uniquement depuis un push sur `main`.
- Symptôme: La suite ciblée termine à `14/15`; `InstallationGuides_RequireTheVerifiedMainPackageAndSafeReplacement` cherche la chaîne `` `scriptApi.major` is `2` `` sur une seule ligne alors que le README coupe volontairement la phrase entre les deux lignes Markdown.
- Sources vérifiées:
  - `bug-reports\20260829-051355-test-guide-scriptapi-linebreak`;
  - sortie de `dotnet test tests\DonJEnemySpawner.Tests\DonJEnemySpawner.Tests.csproj -c Release --filter FullyQualifiedName~PackagingSafetyTests`;
  - `README.md` et `tests\DonJEnemySpawner.Tests\PackagingSafetyTests.cs`.
- Extraits utiles: `échec : 1, réussite : 14, total : 15`; l'assertion signale uniquement l'absence de la chaîne contiguë alors que les deux fragments et la valeur correcte sont présents dans le guide.
- Analyse / hypothèse: Défaut limité au test source: la conformité documentaire ne doit pas dépendre de la mise en forme ou d'un retour à la ligne entre les mots.
- Action menée: L'assertion utilise une expression régulière bornée avec `\s+` entre `` `scriptApi.major` ``, `is` et `` `2` ``; aucun comportement du mod ni fichier GTA n'a été modifié.
- Vérification: La classe `PackagingSafetyTests` est relancée avec `--no-restore` et réussit `15/15`.
- Résolution: Contrat documentaire conservé et test rendu robuste aux retours à la ligne Markdown.

## 2026-08-29 20:32:45 +02:00 - Premières passes du contrat ABI complet
- Statut: Corrigé et validation globale locale réussie.
- Contexte: Deux premières exécutions de `tools\run-safety-checks.ps1 -UseStubApi` pendant l'alignement du stub et l'enrichissement atomique du contrat ABI NIB.
- Symptôme: La passe `safety-20260829-202239` termine à `483/487` avec quatre échecs: deux liés aux formes CLR encore inexactes dans le stub et son déploiement simulé, plus deux attentes source/documentaires devenues obsolètes. La passe `safety-20260829-203054` croise ensuite le remplacement concurrent du validateur schema 1 par le schema 2 avant la régénération du XML et termine à `477/490`; les treize échecs packaging portent tous `Version de schema ABI non prise en charge : 1`.
- Sources vérifiées:
  - `TestResults\safety-20260829-202239\safety-tests.trx` et `bug-reports\20260829-202508-safety-failure`;
  - `TestResults\safety-20260829-203054\safety-tests.trx` et `bug-reports\20260829-203245-safety-failure`;
  - `tools\Stubs\NIBScriptHookVDotNet2\StubApi.cs`;
  - `tools\NibAbiValidator\AbiContract.cs`, `AbiSignatures.cs`, `AbiValidator.cs` et le contrat XML canonique;
  - `README.md`, `Mode-pour-jeu-ici\INSTALLATION_SIMPLE.txt` et les tests source concernés.
- Extraits utiles: Première passe: `échec : 4, réussite : 483, total : 487`; le validateur détaillait notamment `GTA.IHandleable` absent et plusieurs écarts `virtual/final/newslot`. Seconde passe: `échec : 13, réussite : 477, total : 490`; chaque échec packaging provenait uniquement du court intervalle schema-code `2` / contrat-XML `1`.
- Analyse / hypothèse: La première passe a correctement révélé que l'égalité de version d'assembly ne suffit pas: héritages, interfaces et attributs CLR devaient aussi correspondre. La seconde panne était limitée à l'orchestration de développement, un exécutable reconstruit pendant une suite déjà lancée ayant lu l'ancien XML; aucun binaire GTA ni sauvegarde joueur n'a été touché.
- Action menée: Le stub a été aligné sur les types, héritages, interfaces, accesseurs, visibilités et types sous-jacents d'enums réellement consommés. Le contrat schema 2 capture aussi ces invariants; les tests source ont été adaptés au dispatcher de démarrage et les guides conservent leurs exigences exactes. Les builds et tests ont ensuite été relancés sans écriture concurrente.
- Vérification: `TestResults\safety-20260829-203725` réussit `493/493`, build stub et solution à zéro avertissement/zéro erreur, package local conforme et refus attendu de `sourceDirty=true`. La build Release réelle réussit à zéro avertissement/zéro erreur; `dotnet test GTA5modDEV.sln -c Release` réussit `479/479`. Le validateur relit enfin 32 références de types et 189 références de membres contre la DLL NIB 2.11.6 live avec zéro incompatibilité.
- Résolution: Les écarts détectés et le défaut transitoire de schema sont clos; la chaîne locale stable est entièrement verte.

## 2026-08-29 05:17:57 +02:00 - F10 inactif par incompatibilité ABI avec NIB 2.11.6
- Statut: Corrigé, validé localement et en CI, artefact exact déployé et cycle F10 confirmé en jeu.
- Contexte: Nouveau lancement de GTA V Enhanced après la première correction d'identité d'assembly NIB. Le livrable CI était reconnu comme API `2.11.6`, mais le menu restait inactif lorsque j'appuyais sur F10.
- Symptôme: `DonJEnemySpawner` était trouvé puis rejeté pendant son constructeur. L'appel natif d'initialisation des relations levait une `MissingMethodException` avant l'enregistrement de `KeyDown += OnKeyDown`; F10 ne pouvait donc recevoir aucun événement.
- Sources vérifiées:
  - `bug-reports\20260829-051929-f10-toujours-inactif-apres-correctif-nib\summary.md`;
  - `bug-reports\20260829-051929-f10-toujours-inactif-apres-correctif-nib\raw-logs\Grand-Theft-Auto-V-Enhanced__NIBScriptHookVDotNet.log`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.cs` et `src\DonJEnemySpawner\DonJEnemySpawner.RuntimeSafety.cs`;
  - stub `tools\Stubs\NIBScriptHookVDotNet2`, validateur `tools\NibAbiValidator`, contrat ABI canonique et tests associés;
  - `TestResults\safety-20260829-204809`;
  - workflow GitHub `Safety` n° `33269599747`, commit `13e3f64b8e0b0945ffce24b15409300493a1c606`;
  - artefact exact `DonJCustomNpcPlacer-game-ready` téléchargé sous `C:\Users\nodig\AppData\Local\Temp\DonJ-ci-33269599747-13e3f64`;
  - API live `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet2.dll`;
  - ENdll, PDB, manifest et journaux live sous `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts`.
- Extraits utiles:
  - log NIB historique à `05:17:57`: `Failed to instantiate script DonJEnemySpawner because constructor threw an exception: System.MissingMethodException: Méthode introuvable : '!!0 GTA.Native.Function.Call(GTA.Native.Hash, System.Object[])'.`, puis `GetPlayerRelationshipGroup -> InitializeRelationshipGroups -> DonJEnemySpawner..ctor`;
  - l'API NIB réelle expose `Function.Call(Hash, InputArgument[])`, pas la surcharge permissive `Object[]` fournie par l'ancien stub; l'audit complet a identifié au moins neuf formes incompatibles;
  - manifest déployé: `manifestVersion=2`, commit `13e3f64b8e0b0945ffce24b15409300493a1c606`, `sourceDirty=false`, API `NIBScriptHookVDotNet2 2.11.6.0`, contrat `nib-shvdn-v2.11.6`, SHA-256 du contrat `F1D70E6BE8D12178CEBAADC2E4B5EE30926A9C850C1A9F1F2C25900C355BD3BD`;
  - SHA-256 ENdll artefact/installé: `2316D390A12876CF3443AF344676541EB689570B7577A5EEBB2D279380E9BC3D`; PDB: `0C5192AFB4F72155B15E11B125C1F744A8EE46534D1AE4A3ABEC165CD3B5E612`; API live: `DBF8FC318730D7101E945D0F4B6552E34C8559BEEE6978826D5067329358CB71`;
  - log NIB live à `21:05:14–21:05:16`: `Found 1 script(s) in DonJCustomNpcPlacer.ENdll resolved to API version 2.11.6`, `Instantiating script DonJEnemySpawner`, puis `Started script DonJEnemySpawner.`;
  - log DonJ live à `21:05:16.040`: `Chargement - DonJ Custom NPC Placer charge.`, sans nouvelle `MissingMethodException`.
- Analyse / hypothèse: Le stub CI autorisait des signatures absentes de NIB 2.11.6. Le compilateur avait donc lié le livrable à `Function.Call(Hash, Object[])`, méthode impossible à résoudre dans le jeu. Comme l'initialisation native des relations précédait l'abonnement clavier, cette incompatibilité arrêtait le constructeur avant F10. Le raccourci et sa logique n'étaient pas défectueux.
- Action menée: Le stub a été aligné sur toute l'interface NIB 2.11.6 consommée, notamment `InputArgument[]`, les héritages, types valeur/référence, retours, opérateurs et emplacements des membres; les surcharges permissives `Object[]`/`ulong` ont été supprimées. Un validateur net48 basé sur Mono.Cecil et un contrat ABI schema 2 contrôlent désormais chaque référence du livrable avant packaging et avant toute mutation du dossier GTA. Le manifest v2 publie l'identité et le SHA-256 du contrat. Le constructeur enregistre les événements runtime avant les initialisations optionnelles, lesquelles sont isolées et journalisées; une panne Relations ou Justice ne peut plus neutraliser F10, et Relations conserve sa reprise cadencée. Des tests couvrent le consommateur ABI volontairement invalide, le refus transactionnel avant mutation, l'instanciation réelle sous stub, l'erreur Relations, le basculement F10 et F10 pendant le placement.
- Vérification: `tools\run-safety-checks.ps1 -UseStubApi` réussit `493/493` dans `TestResults\safety-20260829-204809`; `dotnet build GTA5modDEV.sln -c Release` termine avec zéro avertissement/zéro erreur; `dotnet test GTA5modDEV.sln -c Release` réussit `479/479`. Le validateur contrôle `32` références de types et `189` références de membres avec zéro incompatibilité contre l'API NIB live. Le workflow GitHub `33269599747` du commit `13e3f64` est vert et publie l'artefact exact ensuite revalidé et déployé. Les hashes installés correspondent à l'artefact, aucun alias historique ni fichier transactionnel ne subsiste. Le lancement réel du jeu confirme `Started script DonJEnemySpawner` et le message de chargement DonJ. Pendant la session live de `21:05` à `21:08`, le menu DonJ a été observé ouvert, F10 l'a fermé, F10 l'a rouvert, puis F10 l'a refermé, sans `MissingMethodException`.
- Résolution: L'incompatibilité ABI qui interrompait le constructeur est supprimée et verrouillée dans la compilation, le packaging et le déploiement. Le mod est chargé par NIB 2.11.6 et F10 ouvre, ferme puis rouvre normalement le menu en jeu.

## 2026-08-29 21:09:13 +02:00 - Faux échecs d'outillage pendant la validation finale F10
- Statut: Résolus, sans impact sur la CI, le livrable, Git ou GTA.
- Contexte: Récupération de l'artefact exact du workflow `Safety` n° `33269599747`, présentation de ses métadonnées et lancement de GTA V Enhanced pour la preuve finale.
- Symptôme: `gh run view ... --json artifacts` a refusé un champ JSON non pris en charge; une commande PowerShell de diagnostic a placé un pipeline directement après un bloc `foreach` et levé un `ParserError`; la première demande de lancement GTA n'exposait pas encore de fenêtre ciblable, puis une seconde demande a affiché une erreur Steam alors que le lancement initial continuait. Une revue read-only a reproduit le même défaut de pipeline et une comparaison `DateTime`/`DateTimeOffset` invalide en filtrant des événements.
- Sources vérifiées:
  - sorties des commandes `gh run view` et PowerShell concernées;
  - endpoint GitHub Actions des artefacts du run `33269599747`;
  - dossier `C:\Users\nodig\AppData\Local\Temp\DonJ-ci-33269599747-13e3f64`;
  - `bug-reports\20260829-210303-gta-launch-failure-during-f10-validation`;
  - `C:\Users\nodig\Documents\Rockstar Games\Launcher\launcher.log`;
  - processus et fenêtre uniques de `GTA5_Enhanced.exe` observés pendant la validation.
- Extraits utiles:
  - `gh`: champ JSON `artifacts` non reconnu; l'API dédiée a ensuite résolu l'artefact id `9719718129`, nom `DonJCustomNpcPlacer-game-ready`;
  - PowerShell: `ParserError: An empty pipe element is not allowed`; la relance a matérialisé les lignes dans une variable avant formatage;
  - launcher à `21:03:24.169`: `Second external launch requested from SCUI. Discarding`, puis à `21:03:30.356`: lancement de `GTA5_Enhanced.exe`.
- Analyse / hypothèse: Les deux premières erreurs étaient limitées aux interfaces des outils de lecture. Pour GTA, le premier lancement était déjà engagé dans Steam/Rockstar, mais aucune fenêtre n'était encore exposée; le launcher a correctement écarté la demande en doublon. Aucun symptôme n'était lié au mod ou au correctif ABI.
- Action menée: L'endpoint GitHub Actions dédié a servi à sélectionner et télécharger l'artefact exact. Les résultats PowerShell ont été capturés avant leur mise en forme et les diagnostics DateTime ont été abandonnés au profit des journaux horodatés. Les relances GTA ont cessé; l'état des fenêtres a été rafraîchi jusqu'à l'apparition de l'unique processus du jeu.
- Vérification: L'artefact exact a été téléchargé, son manifest et son contrat ABI ont été validés, puis ses hashes ont été relus après déploiement. `launcher.log` confirme que le lancement initial a produit `GTA5_Enhanced.exe` six secondes après le rejet du doublon. La session réelle a ensuite chargé DonJ et validé le cycle F10 complet.
- Résolution: Incidents d'outillage clos; les commandes fautives étaient read-only et le faux échec de lancement n'a interrompu ni le jeu ni la validation.

## 2026-08-29 21:13:46 +02:00 - Joueur signalé immortel pendant la validation en jeu
- Statut: Ouvert; analyse interrompue à la demande de l'utilisateur, aucun correctif appliqué.
- Contexte: Session GTA V Enhanced utilisée pour valider le correctif F10 après chargement réussi du mod. Après le cycle ouvrir, fermer et rouvrir du menu, l'utilisateur signale que le personnage ne peut plus mourir.
- Symptôme: Le joueur paraît immortel en jeu. La nature exacte reste à confirmer entre un drapeau d'invincibilité, une santé anormalement élevée, une régénération ou l'effet d'un autre mod chargé.
- Sources vérifiées:
  - `bug-reports\20260829-211336-joueur-immortel-pendant-validation-f10`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.log`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves\_justice_state.xml`;
  - recherches read-only des mutations d'invincibilité et de santé dans `src\DonJEnemySpawner` et des contrats associés dans `tests\DonJEnemySpawner.Tests`.
- Extraits utiles:
  - log DonJ à `21:05:38.900`: `Justice.Transfert - Transfert annulé après timeout; inventaire rendu et dossier conservé sous mandat.`;
  - log DonJ à `21:14:09`: deux récupérations manuelles des contrôles et de la police;
  - snapshot Justice relu à `21:14:25`: profil actif hors détention, `playerStateStored="false"` et `storedInvincible="false"`;
  - le projet contient plusieurs protections temporaires distinctes qui doivent encore être départagées: placement, discipline de détention, téléportation d'intérieur et mode Terminator.
- Analyse / hypothèse: Les données persistées Justice ne revendiquent plus d'invincibilité au moment de la collecte, mais elles ne prouvent pas la valeur runtime du ped ni l'état d'un autre système. L'analyse a été arrêtée avant attribution certaine de la cause.
- Action menée: Les logs ont été collectés avec `tools\collect-bug-logs.ps1`; le snapshot live et les chemins de code susceptibles de modifier l'invincibilité ont été inspectés en lecture seule. Aucun fichier source, binaire installé, état de sauvegarde GTA ou réglage en jeu n'a été modifié.
- Vérification: Aucune suite de tests ni reproduction supplémentaire n'est lancée, conformément à la demande d'arrêt et de publication de l'état actuel sans test.
- Résolution: Non résolu. L'incident est conservé pour reprise manuelle ultérieure depuis l'état publié sur `main`.

## 2026-08-29 23:22:18 +02:00 - Contrat ABI Camera refusé après application du correctif d'immortalité
- Statut: Corrigé, validé localement et déployé comme build de test.
- Contexte: Première exécution de `tools\run-safety-checks.ps1` juste après l'application de `fix-player-immortality.patch`.
- Symptôme: La compilation Release réussissait, mais le validateur ABI refusait le livrable avant les tests avec `ABI040 Reference de membre non autorisée par le contrat : method|class [api]GTA.Camera|op_Inequality`.
- Sources vérifiées:
  - `TestResults\safety-20260829-231210\logs\verify-nib-abi.log`;
  - `bug-reports\20260829-231224-safety-failure` et son `crash-list-entry.md` généré par `tools\collect-bug-logs.ps1`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.cs`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.PlayerProtection.cs`;
  - `tests\DonJEnemySpawner.Tests\PlayerInvincibilityRegressionTests.cs`.
- Extraits utiles: `GTA.Camera.op_Inequality` était appelé par `_placementCamera != null`; le contrat NIB v2.11.6 ne référence pas cet opérateur.
- Analyse / hypothèse: Le patch introduisait dix comparaisons `null` directes sur des wrappers GTA (`Camera`, `Ped`, `Vehicle`, `Prop`). Ces opérateurs CLR ne sont pas une API v2 autorisée, même si l'existence effective de l'entité doit continuer à être contrôlée par les natives prévues.
- Action menée: Les dix comparaisons sont remplacées par `object.ReferenceEquals`, ce qui conserve la détection des états de placement partiels sans appeler un opérateur GTA. Le test de régression vérifie explicitement ce garde-fou pour la caméra de placement.
- Vérification: `tools\run-safety-checks.ps1` réussit dans `TestResults\safety-20260829-231422` avec `481/481`; `dotnet build GTA5modDEV.sln -c Release` termine avec zéro avertissement/zéro erreur; `dotnet test GTA5modDEV.sln -c Release` réussit `481/481`. Le validateur ABI contrôle le livrable installé contre `NIBScriptHookVDotNet2.dll` avec `runtimeValidated=true`, `32` références de types et `189` références de membres.
- Résolution: Le correctif d'immortalité reste appliqué et le livrable de test valide a remplacé l'ENdll, le PDB et le manifest GTA. Le manifest installé indique volontairement `sourceDirty=true`; la version précédente reste sauvegardée dans `TestResults\safety-20260829-231422\gta-predeploy-backup-20260829-231422`.

## 2026-08-30 01:18:02 +02:00 - Assertion de contrat Justice sensible au retour à la ligne C#
- Statut: Corrigé et validation complète réussie.
- Contexte: Première exécution ciblée de `JusticeRuntimeContractTests` juste après l'application de `DonJ_GTA5_Justice_Prison_Respawn_Escape.patch`.
- Symptôme: `RuntimeJustice_PoliceCustodyMaterializesExactlyOneMinimalCaseBeforeCapture` échouait car son assertion cherchait l'expression `HasJusticePoliceCustodyEvidence(...) || liveArrestEvidence` sur une seule ligne, alors que le code C# la met volontairement en forme sur deux lignes.
- Sources vérifiées:
  - `bug-reports\20260830-011749-echec-test-justice-prison-respawn-escape`;
  - sortie de `dotnet test GTA5modDEV.sln -c Release --no-build --filter "FullyQualifiedName~JusticeRuntimeContractTests"`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticeRuntimeContractTests.cs`.
- Extraits utiles: la première exécution signalait uniquement l'absence de la chaîne contiguë attendue; l'expression réellement présente est `HasJusticePoliceCustodyEvidence(wantedLevel, player, dead) ||` suivie de `liveArrestEvidence` à la ligne suivante.
- Analyse / hypothèse: Défaut limité au test d'inspection source; la logique de preuve de capture policière et le code gameplay du patch ne sont pas en cause.
- Action menée: L'assertion textuelle fragile est remplacée par une expression régulière qui accepte les espaces et retours à la ligne autour de l'opérateur `||`, sans relâcher le contrat fonctionnel contrôlé.
- Vérification: Test ciblé réussi `72/72`; `dotnet build GTA5modDEV.sln -c Release` réussi sans avertissement ni erreur; `dotnet test GTA5modDEV.sln -c Release --no-build` réussi `483/483`; `tools\run-safety-checks.ps1` réussi `483/483` avec ABI NIB v2 valide.
- Résolution: Le contrat reste strict sur la preuve policière et n'échoue plus à cause d'un formatage C# équivalent.

## 2026-08-30 01:43:37 +02:00 - Libération technique après une mort policière avec peine de prison
- Statut: Corrigé et validation complète locale réussie.
- Contexte: Test réel de GTA V Enhanced après le correctif de respawn Justice. Le joueur est mort pendant une poursuite policière avec des étoiles et une peine calculée de 1 800 secondes.
- Symptôme: Après le respawn GTA à l'hôpital, le mod affichait `transfert impossible, remise en liberté technique sous mandat` au lieu de transférer le joueur à Bolingbroke.
- Sources vérifiées:
  - `bug-reports\20260830-013408-justice-respawn-prison-evasion`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.log`;
  - `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves\_justice_state.xml`;
  - `src\DonJEnemySpawner\DonJEnemySpawner.Justice.Custody.cs`;
  - `tests\DonJEnemySpawner.Tests\JusticeCustodyHardeningTests.cs` et `JusticeRuntimeContractTests.cs`.
- Extraits utiles: Le runtime journalise successivement `Capture apres mort en poursuite`, trois snapshots d'inventaire indisponibles, `Inventaire incompatible après trois essais`, puis `Transfert annulé après timeout; inventaire rendu et dossier conservé sous mandat`. Le XML final portait `phase=AtLarge`, `warrant=true`, `sentenceSeconds=1800` et aucune détention active.
- Analyse / hypothèse: La détection de la mort policière, le jugement et le choix de peine fonctionnaient. L'échec venait du fallback inventaire : un snapshot non lisible appelait explicitement le rollback de transfert, et le timeout générique créait le même rollback. Une panne technique annulait donc à tort la détention avant tout téléport vérifié.
- Action menée: Le premier échec de snapshot entièrement non destructif bascule désormais vers un inventaire préservé, précommité avant le téléport. Les états préservés ou ambigus ne rejouent jamais `RemoveAll`; une restitution ambiguë attend la libération réelle. Le handler de transfert conserve la phase `Transporting`, rend le joueur mobile et retente avec un délai borné à cinq secondes sans créer de nouveau `TransferRollback`. La reprise des anciens WAL de rollback reste compatible. L'évasion demeure impossible tant que le joueur se trouve dans l'enveloppe extérieure de l'enceinte et exige six secondes continues réellement hors prison.
- Vérification: Tests Justice ciblés réussis `93/93`, puis tests de durcissement `22/22`; `dotnet build GTA5modDEV.sln -c Release` réussi avec zéro avertissement et zéro erreur; `dotnet test GTA5modDEV.sln -c Release --no-build` réussi `485/485`; `tools\run-safety-checks.ps1` réussi `485/485` dans `TestResults\safety-20260830-015013`, avec contrat ABI NIB v2 valide (`32` types, `189` membres) et paquet `.ENdll` vérifié.
- Résolution: Une mort ou arrestation policière ne peut plus devenir une remise en liberté technique à cause de l'inventaire ou du timeout. Le transfert reste obligatoire vers Mission Row ou Bolingbroke selon la peine, sous retries sécurisés jusqu'à confirmation physique.

## 2026-08-30 04:38:50 +02:00 - Échec intermittent de persistance Justice pendant la validation du guide README
- Statut: Instable, non corrigé hors périmètre; la dernière validation complète est réussie.
- Contexte: Validation de la documentation d'installation bilingue dans un worktree propre basé sur `origin/main`, sans modification du code Justice.
- Symptôme: La suite `safety-20260830-043159` a échoué une fois sur `PlayerProfiles_SuccessfulResetWritesTheEmptyProfileToPrimaryAndBackup`: après corruption volontaire du primaire, le chargement depuis le `.bak` a retrouvé `recidivism=2` au lieu de `0`.
- Sources vérifiées:
  - `TestResults\safety-20260830-043159\logs\test-release.log` et `safety-tests.trx`;
  - `bug-reports\20260830-043422-safety-failure\crash-list-entry.md` et les logs collectés automatiquement;
  - `tests\DonJEnemySpawner.Tests\JusticePlayerProfilePersistenceTests.cs`, test et helpers de persistance concernés;
  - trois relances isolées du test, puis `TestResults\safety-20260830-043607\safety-tests.trx`.
- Extraits utiles: l'échec signale `Attendu : <0>, Réel : <2>` à la ligne 750 du test; les trois relances isolées réussissent ensuite `1/1`, et la relance complète Safety réussit `507/507` avec ABI NIB v2 valide.
- Analyse / hypothèse: Le symptôme est compatible avec une course intermittente entre l'écriture asynchrone du reset et la copie de sauvegarde. Il n'est pas lié au README, au test documentaire ou au package de DonJ Custom NPC Placer.
- Action menée: Les logs ont été collectés via la suite de sécurité; aucune modification Justice hors demande n'a été appliquée. Le nouveau guide et son test restent isolés de cet incident.
- Vérification: Test Justice concerné réussi trois fois de suite; `tools\run-safety-checks.ps1 -UseStubApi` relancé avec succès (`507/507`, package vérifié, refus attendu de la source sale).
- Résolution: L'incident est consigné pour une investigation Justice dédiée. La documentation demandée est validée par la dernière passe complète verte.
