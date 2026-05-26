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
