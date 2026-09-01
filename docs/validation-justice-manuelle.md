# Validation manuelle — Justice avancée

## Objet

Cette matrice complète les tests automatisés des correctifs JUS-001 à JUS-014 et du module de reconnaissance policière Justice. Elle doit être exécutée avec le package `game-ready` issu d'une seule exécution réussie de `tools/run-safety-checks.ps1`. Un résultat sans preuve exploitable ne vaut pas validation.

Valeurs autorisées dans la colonne **Résultat** : `PASS`, `FAIL`, `BLOQUÉ`, `NON EXÉCUTÉ`. Toute ligne `FAIL` ou `BLOQUÉ` doit référencer une entrée horodatée de `crash-list.md`.

## Identification de la build testée

| Élément | Valeur relevée | Résultat | Preuve |
|---|---|---|---|
| Commit du `manifest.json` | À renseigner | NON EXÉCUTÉ | Chemin/copie du manifest |
| Version informative du binaire | À renseigner | NON EXÉCUTÉ | Capture diagnostic + manifest |
| Version du schéma Justice | À renseigner | NON EXÉCUTÉ | Manifest + en-tête XML |
| SHA-256 ENdll du package | À renseigner | NON EXÉCUTÉ | `Get-FileHash` + manifest |
| SHA-256 ENdll installé | À renseigner | NON EXÉCUTÉ | `Get-FileHash` sous `Scripts` |
| SHA-256 affiché par le diagnostic | À renseigner | NON EXÉCUTÉ | Capture F10 |
| SHA-256 `immatriculation.png` | À renseigner | NON EXÉCUTÉ | `Get-FileHash` package/installation + manifest |
| SHA-256 `tenue.png` | À renseigner | NON EXÉCUTÉ | `Get-FileHash` package/installation + manifest |
| SHA-256 `mandat.png` | À renseigner | NON EXÉCUTÉ | `Get-FileHash` package/installation + manifest |
| Version du schéma reconnaissance | À renseigner | NON EXÉCUTÉ | `JusticeRecognition.xml` |
| Version GTA / NIB v2 | À renseigner | NON EXÉCUTÉ | Logs de démarrage |
| Provider HUD NIB v3 optionnel | Présent/absent à renseigner | NON EXÉCUTÉ | Version majeure + forme `GTA.UI.CustomSprite`, ou fallback natif |
| Mods actifs pendant l'essai | À renseigner | NON EXÉCUTÉ | Liste de fichiers / capture |

## Préparation obligatoire

1. Je ferme GTA et ses loaders. Je vérifie aussi que `GTA5_Enhanced`, `GTA5` et `PlayGTAV` ne tournent plus; le déployeur doit refuser sans les terminer si l'un reste actif.
2. Je conserve une copie du dossier `DonJEnemySpawnerSaves`, notamment `_justice_state.xml`, son `.bak`, `_justice_state.wal` et toute quarantaine de reset. Cette copie legacy est obligatoire pour vérifier la remise à zéro unique sans risquer un profil réel. Je copie séparément le dossier de reconnaissance réellement sélectionné, normalement `Scripts\DonJJusticeRecognition`, avec `JusticeRecognition.xml`, son `.bak` et son log.
3. Je pars d'une source Git propre et je génère le package avec `tools/run-safety-checks.ps1`; je n'utilise aucun binaire maintenu manuellement. Un contrôle local lancé depuis une source modifiée produit uniquement un package `sourceDirty=true`, non publiable.
4. Je vérifie `manifestVersion=2`, le commit, `sourceDirty=false`, le schéma Justice exactement égal à 2, la référence `NIBScriptHookVDotNet2`/`ScriptHookVDotNet2` de version majeure 2, l'identifiant/version/SHA-256 du contrat ABI, les autres versions, les tailles et les SHA-256 du `manifest.json`. `files.justiceAssets.{immatriculation,outfit,warrant}` contient exactement les trois chemins relatifs `Assets/Justice/immatriculation.png`, `Assets/Justice/tenue.png` et `Assets/Justice/mandat.png`, chacun avec sa taille et son SHA-256. `hudRenderer` impose `optional=true`, `fallback=native`, `minimumMajor=3`, `typeName=GTA.UI.CustomSprite` et `contractVersion=1`; si `available=true`, le vrai nom et la version du provider validé sont déclarés. L'assembly DonJ ne référence aucune API v3.
5. Si `NIBScriptHookVDotNet3.dll` est présente sous `GtaRoot`, je vérifie sa majeure et la forme réfléchie attendue de `GTA.UI.CustomSprite`. Si elle est absente, je confirme le fallback HUD natif. Ce provider externe n'est jamais copié depuis le package DonJ.
6. J'installe uniquement ce package propre par le chemin de déploiement explicite, puis je recalcule les SHA-256 du binaire et des trois PNG sous `Scripts`. `deploy-game-ready.ps1` doit refuser tout manifest `sourceDirty=true`, asset Justice absent/corrompu ou provider HUD v3 présent mais incompatible; son absence ne bloque pas.
7. J'active un enregistrement de frametime et je lance le collecteur après chaque anomalie. Je vérifie qu'il a pris `DonJCustomNpcPlacer.log`, `Scripts\DonJJusticeRecognition\JusticeRecognition.log`, `NIBScriptHookVDotNet.log` et `ScriptHookV.log`; si la reconnaissance utilise un dossier de repli, je joins manuellement son journal réel.
8. Pour les scénarios destructifs, je travaille sur une copie dédiée des sauvegardes et un profil de test GTA.

## A. Package, build et déploiement — JUS-001, JUS-002, JUS-013

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| PKG-01 | Build Release ordinaire | Compiler sans `DeployToGta`; aucun fichier sous le vrai dossier GTA ne change. | NON EXÉCUTÉ | Horodatages et hashes avant/après |
| PKG-02 | Package canonique | Le package contient uniquement ENdll, PDB, guide, manifest et `Assets\Justice` avec exactement `immatriculation.png`, `tenue.png`, `mandat.png`; chaque fichier correspond à la sortie du build testé. | NON EXÉCUTÉ | Log safety + listing + hashes |
| PKG-03 | Métadonnées | Commit, `sourceDirty=false`, version d'assembly, version informative, référence unique API majeure 2 et schéma exact 2 du manifest correspondent au binaire chargé. `files.justiceAssets.{immatriculation,outfit,warrant}` décrit exactement les trois chemins relatifs normalisés, tailles et SHA-256, sans chemin absolu ni traversée. | NON EXÉCUTÉ | Manifest + réflexion/diagnostic + hashes PNG |
| PKG-04 | Installation explicite | ENdll, PDB, manifest et trois PNG sont validés avant toute écriture, puis stageés, publiés et relus avant le retrait des alias historiques. Le binaire précédent n'est jamais supprimé avant validation du nouveau; un échec restaure les alias et rollback aussi les assets. Le manifest installé porte le nom stable `DonJCustomNpcPlacer.manifest.json`. | NON EXÉCUTÉ | Log de déploiement + hashes des six fichiers |
| PKG-05 | Package corrompu | Altérer successivement l'ENdll puis chacun des trois PNG d'une copie du package; le déploiement échoue avant écriture et conserve intégralement le binaire, le manifest et les assets installés. | NON EXÉCUTÉ | Hashes avant/après + sortie PowerShell |
| PKG-06 | Alias historiques | Après validation de tout le nouvel ensemble seulement, aucun `DonJCustomNpcPlacer.dll`, `DonJEnemySpawner.dll`, `.ENdll` ou `.pdb` obsolète ne subsiste. Simuler un alias verrouillé : ENdll, PDB, manifest, assets et tous les alias reviennent à leur état initial, sans fenêtre volontairement dépourvue d'ENdll. | NON EXÉCUTÉ | Listing `Scripts` + hashes avant/après |
| PKG-07 | Build ID en jeu | Le diagnostic F10 affiche le commit/build ID et le SHA-256 correspondant au manifest publié. | NON EXÉCUTÉ | Capture F10 + manifest |
| PKG-08 | Source locale modifiée | Générer explicitement avec `-AllowDirtySource`; le manifest porte `sourceDirty=true`, le diagnostic ne le reconnaît pas comme publié et `deploy-game-ready.ps1` refuse l'installation sans toucher au binaire actif. | NON EXÉCUTÉ | Manifest + sortie PowerShell + hash avant/après |
| PKG-09 | Contrat ABI NIB | Le manifest v2 porte l'identifiant, la version et le SHA-256 du contrat ABI; altérer une référence membre ou présenter une DLL runtime incompatible doit être refusé avant toute écriture dans `Scripts`. | NON EXÉCUTÉ | Sortie du validateur + hashes de l'ensemble avant/après |
| PKG-10 | Provider HUD réfléchi optionnel | Vérifier que l'assembly DonJ ne référence que l'API v2. Sans `NIBScriptHookVDotNet3.dll`, packaging et déploiement réussissent avec `optional=true`, `fallback=native`, `available=false`. Avec une forme compatible, le vrai nom/version est déclaré; avec une forme incompatible, le préflight refuse avant toute écriture. La DLL v3 n'est jamais copiée dans le package. | NON EXÉCUTÉ | Manifest + références IL + sortie préflight + hashes avant/après |
| PKG-11 | Jeu ou host actif | Lancer un processus de test nommé `GTA5_Enhanced`, `GTA5` puis `PlayGTAV` dans un arbre isolé. Le déploiement refuse avant toute création, staging, backup, remplacement ou suppression dans `Scripts`, conserve les hashes et ne termine jamais le processus. | NON EXÉCUTÉ | Sortie préflight + PID toujours actif + hashes avant/après |

## B. Isolation du tick et arrêt — JUS-004, JUS-008

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| RUN-01 | Erreur Cartel injectée | L'erreur est journalisée avec un cooldown; Justice, menu, objets et portails continuent sur les ticks suivants. | NON EXÉCUTÉ | Log stages + vidéo |
| RUN-02 | Erreur UI injectée | La détection et les retries Justice continuent; le log UI ne spamme pas chaque frame. | NON EXÉCUTÉ | Log stages + compteurs |
| RUN-03 | Erreur Terminator injectée | Justice n'est pas neutralisée et la phase judiciaire reste cohérente. | NON EXÉCUTÉ | Log + capture diagnostic |
| RUN-04 | Erreur `JusticeEarly` injectée | Seule la maintenance de sécurité s'exécute: contrôles/police/inventaire récupérables, aucune peine ni charge ne progresse. | NON EXÉCUTÉ | État avant/après + log |
| RUN-05 | Arrêt normal du script | Justice est arrêtée avant les autres domaines; police, mobilité et contrôles sont restaurés. L'enfilement final ne bloque pas le thread GTA et `Stop()` attend la dernière révision au plus 2,5 secondes. | NON EXÉCUTÉ | Log ordonné + vidéo |
| RUN-06 | Une étape d'arrêt échoue | Les étapes suivantes s'exécutent quand même, y compris le nettoyage des blips et groupes. | NON EXÉCUTÉ | Log `Shutdown.*` |
| RUN-07 | Enfilement final ou arrêt refusé | Le refus d'enfilement ou l'expiration du délai borné de `Stop()` est explicitement journalisé et le WAL/état sale reste récupérable au redémarrage. | NON EXÉCUTÉ | Log + reprise suivante |
| RUN-08 | Arrêt pendant libération | Aucun contrôle ni flag police ne reste imposé; la libération reprend sans double effet au reload. | NON EXÉCUTÉ | Vidéo + XML/WAL + log |
| RUN-09 | Transition ou fail-safe Justice | Pendant identification, sauvegarde de switch et suspension, F10 affiche trois causes distinctes. Si `JusticeEarly` ou `JusticeLate` échoue, la mini-ligne de peine disparaît au lieu de rester figée; le HUD Terminator reste indépendant. | NON EXÉCUTÉ | Captures F10/HUD + log stage |

## C. Inventaire et intégration police — JUS-005, JUS-006

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| INV-01 | Armes standard | Capture validée et durable avant `RemoveAll`; restitution exacte après libération. | NON EXÉCUTÉ | Inventaire avant/après + WAL |
| INV-02 | Armes DLC | Armes, munitions, chargeurs, composants, teintes et sélection sont restitués. | NON EXÉCUTÉ | Captures atelier avant/après |
| INV-03 | Arme add-on incompatible | Aucun retrait destructif; fallback explicite; attaque/visée ne restent jamais bloquées. | NON EXÉCUTÉ | Vidéo + log inventaire |
| INV-04 | Échec transitoire de capture | Les armes restent présentes, le transfert attend et les retries sont bornés à trois. | NON EXÉCUTÉ | Log des essais + vidéo |
| INV-05 | Suppression refusée ou faux négatif | Avant effet, le snapshot durable est conservé et les retries restent bornés. Si `RemoveAll` a pu agir malgré un retour faux/une exception, l'état devient `RestoreAmbiguous`, la preuve n'est jamais effacée et une restitution est programmée. | NON EXÉCUTÉ | XML/WAL + inventaire avant/après + log |
| INV-06 | Reload pendant confiscation | Aucun double retrait; le snapshot et l'état explicite permettent une reprise ou un fallback sûr. | NON EXÉCUTÉ | XML/WAL avant/après |
| INV-07 | Restitution partielle | Le snapshot n'est pas effacé; l'état reste `RestorePending` ou `RestoreAmbiguous` et récupérable. | NON EXÉCUTÉ | Diagnostic + XML |
| INV-08 | Ancien v1: snapshot nul + lock vrai | Au chargement, l'état migre vers `UnsupportedPreserved`, armes conservées et contrôles libérés. | NON EXÉCUTÉ | XML de fixture + vidéo |
| INV-09 | Récupération manuelle | L'action restaure contrôles/police, fusionne seulement un snapshot valide, ne retire aucune arme et écrit un log. | NON EXÉCUTÉ | Vidéo + log `Justice.Diagnostic` |
| INV-10 | `RestoreAmbiguous` actif | Simuler un `RemoveAll` partiel : aucune arme potentiellement restante n'est utilisable pendant la détention. Après libération, le verrou dérivé disparaît et la restitution différée peut aboutir. | NON EXÉCUTÉ | Vidéo contrôles + XML/WAL |
| POL-01 | Mode `Disabled` | Justice ne pose aucun flag global police; la détention reste jouable avec la limite documentée. | NON EXÉCUTÉ | Trace des natives + capture F10 |
| POL-02 | Mode `FreeroamBestEffort` | Valeur par défaut; application unique en jeu libre, pas de réaffirmation permanente. | NON EXÉCUTÉ | Diagnostic + trace native |
| POL-03 | Mode `Force` | Réaffirmation cadencée uniquement dans le contexte de détention compatible et hors suspension. | NON EXÉCUTÉ | Diagnostic + trace native |
| POL-04 | Mission active | Aucune nouvelle mutation des flags globaux; les jetons possédés sont rendus avant la mission. | NON EXÉCUTÉ | Vidéo + trace native |
| POL-05 | Cinématique | Les flags possédés sont restaurés avant la suspension; aucun nouvel appel de suppression. | NON EXÉCUTÉ | Trace native + vidéo |
| POL-06 | Trainer modifiant le dispatch | Tester `Disabled` puis `FreeroamBestEffort`; aucun écran ne prétend connaître/restaurer l'état antérieur, faute de getter fiable. | NON EXÉCUTÉ | Réglage trainer + log |
| POL-07 | Changement de protagoniste | Restauration globale avant activation de l'autre profil; une barrière police déjà tentée est reprise jusqu'au WAL terminal, jamais rejetée/recréée en boucle. Le nouveau slot reste bloqué jusqu'à sa `DiskRevision`; aucun jeton ne fuit entre héros. | NON EXÉCUTÉ | Diagnostic slots/révisions + trace native/WAL |

## D. Persistance, WAL, réparation et fronts — JUS-003, JUS-007, JUS-010

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| PER-01 | Sauvegarde normale | Le thread GTA capture des DTO typés profonds `Case`, `Record` et `Custody`, sans entité GTA ni sérialisation XML. `JusticeFlushStateNow` enfile sans attente disque; le repository `latest-wins` sérialise, valide, relit et converge hors thread vers la dernière révision. | NON EXÉCUTÉ | Révisions mémoire/disque + frametime |
| PER-02 | WAL critique général | Avant un effet inventaire/police/wanted, le DTO typé est d'abord durable. La barrière est vérifiée sans blocage sur les ticks suivants, puis le WAL écrit `Prepared`/`Attempted` avec seulement cinq références avant l'effet. | NON EXÉCUTÉ | Révisions + décodage WAL + logs |
| PER-03 | Ancienne politique valide | Charger un v1 puis un v2 sans `sentencePolicyVersion`. Les deux sont lus uniquement pour ON/OFF et les récupérations techniques; primaire, backup et ancien WAL passent en quarantaine, puis un état v2.0 neuf marqué `sentencePolicyVersion="2"` est prouvé dans le primaire et son `.bak`. Aucun ancien dossier n'est migré. | NON EXÉCUTÉ | Fichiers/quarantaine et hashes avant/après + XML neuf |
| PER-04 | Sauvegarde corrompue | Le primaire corrompu est refusé sans lecture partielle; aucun effet externe ne part avant récupération. | NON EXÉCUTÉ | Fixture + log |
| PER-05 | Backup valide | Réparation, relecture sémantique et vérification du hash; aucune simple comparaison de taille. | NON EXÉCUTÉ | Hashes + log de réparation |
| PER-06 | Backup de même taille corrompu | La réparation est refusée malgré la taille identique. | NON EXÉCUTÉ | Deux fixtures + log |
| PER-07 | Dossier en lecture seule | Aucun effet critique sans WAL durable; le jeu reste contrôlable et le retry est borné. | NON EXÉCUTÉ | ACL + log + vidéo |
| PER-08 | Disque plein simulé | Aucun primaire valide n'est supprimé; état de durabilité dégradé explicite; pas de faux succès. | NON EXÉCUTÉ | Volume de test + hashes |
| PER-09 | Mort pendant réparation | Le front est mémorisé avec slot/modèle, puis réconcilié seulement avec la même identité. | NON EXÉCUTÉ | Diagnostic fronts + log |
| PER-10 | Arrestation pendant réparation | Les fronts début/fin ne sont pas perdus; une observation ambiguë ne crée jamais directement une condamnation. | NON EXÉCUTÉ | Vidéo + état avant/après |
| PER-11 | Wanted pendant réparation | Les fronts montée/perte sont conservés et rapprochés sans inventer une causalité. | NON EXÉCUTÉ | Log + diagnostic |
| PER-12 | Changement d'identité pendant réparation | Le bit `IdentityChanged` ferme la reprise destructive; aucun front n'est attribué à l'autre héros. | NON EXÉCUTÉ | Slots/modèles + log |
| PER-13 | Pause pendant écriture | Aucun temps de peine hors gameplay; le writer termine ou reprend sans toucher au monde GTA. | NON EXÉCUTÉ | Frametime + révisions |
| PER-14 | Génération v2 falsifiée | Modifier uniquement `JusticeState/@generation` sans recalculer le digest; le primaire est refusé, car le SHA-256 lie génération et payload. | NON EXÉCUTÉ | Fixture avant/après + log de chargement |
| PER-15 | Profil v2 inactif corrompu | Avec un backup v2 entièrement valide, un `recoverySha256` primaire valide et un WAL `Clean` sans transaction ouverte, altérer uniquement le hash ou le fragment d'un profil inactif. Justice conserve le profil actif du primaire, remplace seulement le profil inactif depuis le backup, revalide le snapshot fusionné puis relit exactement ses octets et son SHA-256 après remplacement atomique. | NON EXÉCUTÉ | Fixtures primaire/backup + WAL + hashes + log |
| PER-16 | WAL financier borné | Un débit de jugement ou volontaire utilise un identifiant stable et un plan immuable. Le snapshot complet contenant `Prepared` doit atteindre `DiskRevision`; juste avant l'appel cash, les petites frames `Prepared` puis `Attempted` sont flushées. Chaque frame reste sous 1 024 octets et vingt champs, sans `Case`, `Record`, `Custody`, inventaire complet ni XML. Le lecteur refuse tout slot, génération, identité, schéma ou épisode différent. | NON EXÉCUTÉ | Révisions + décodage WAL + taille des frames + trace cash |
| PER-17 | Barrières d'attente | Aucun chemin gameplay n'appelle la barrière de test. Seul `Stop()` attend au plus 2,5 secondes à l'arrêt; `JusticeAwaitQueuedPersistenceForTests` peut attendre 30 secondes uniquement hors jeu. | NON EXÉCUTÉ | Trace d'appels + chronométrage arrêt |
| PER-18 | Isolation inactive refusée | Répéter PER-15 en corrompant successivement le profil actif, les champs globaux, `recoverySha256`, le backup, puis avec un WAL corrompu, tronqué/réparé ou ouvert. L'isolation est toujours refusée; seul un backup complet valide peut ensuite être chargé. | NON EXÉCUTÉ | Fixtures + WAL + choix primaire/backup + log |
| PER-19 | Preuve v2 absente | Retirer seulement `recoverySha256` d'un primaire v2 : le primaire est refusé et le backup v2 intact reste lisible. | NON EXÉCUTÉ | Fixtures + erreur codec |
| PER-20 | WAL financier d'un profil inactif | Couper après `Attempted` sur Michael, redémarrer avec Franklin, sauvegarder, puis revenir à Michael : Franklin reste intact et aucun second `STAT_SET_INT` n'est émis. Répéter pour jugement et paiement volontaire. | NON EXÉCUTÉ | XML/WAL + compteurs cash |
| PER-21 | Préfixe WAL modifié hors instance | Après acquisition du WAL, modifier un octet sans changer la taille, puis répéter par suppression, troncature et allongement. Tout nouvel append est refusé sans octet supplémentaire, l'autorité mémoire reste inchangée et le diagnostic devient `Corrupt`. | NON EXÉCUTÉ | Copies byte-for-byte + diagnostic |
| PER-22 | Plusieurs WAL financiers | Préparer des opérations sur deux héros puis plusieurs générations du même héros. Au redémarrage, les WAL supersédés sont terminalisés, les propriétaires sont restaurés dans l'ordre causal et un doublon de même génération est refusé avant toute mutation. Aucun cash n'est rejoué. | NON EXÉCUTÉ | XML/WAL des trois profils + compteurs cash |
| PER-23 | Primaire N perdu, backup N-1 | Supprimer uniquement le primaire après un WAL N. Avec `Attempted`, l'intention propriétaire est reconstruite mais `DiskRevision` reste N-1 jusqu'au checkpoint N+1; avec `Prepared`, l'opération est rejetée sans effet et la révision N n'est pas réutilisée. Répéter pour les deux paiements. | NON EXÉCUTÉ | Révisions logique/disque + WAL + compteur cash |
| PER-24 | Verrou ou refus d'accès WAL transitoire | Verrouiller le fichier pendant Recover, pendant la terminalisation d'un WAL supersédé, puis pendant le contrôle du préfixe. Justice applique son backoff sans panne permanente; après déverrouillage, le même état reprend et progresse. | NON EXÉCUTÉ | Diagnostic retry + WAL avant/après |
| PER-25 | Deux instances sur le même WAL | Bloquer une compaction juste avant `File.Replace`, puis tenter un append depuis une seconde instance; répéter Recover pendant une frame écrite non flushée. La seconde opération reçoit une I/O retryable et aucune frame n'est effacée ou tronquée. | NON EXÉCUTÉ | Trace mutex + WAL décodé |

### Coupures transactionnelles obligatoires

Chaque ligne est exécutée pour un paiement, une confiscation/restitution et, quand applicable, une mutation police.

| ID | Point de coupure injecté | Invariant attendu | Résultat | Preuve |
|---|---|---|---|---|
| CUT-01 | Avant `WAL Prepared` | Aucun effet GTA; dette/inventaire/police inchangés. | NON EXÉCUTÉ | WAL + état avant/après |
| CUT-02 | Après snapshot `Prepared` durable, avant WAL `Attempted` | Si le snapshot référencé existe, la barrière exacte est réhydratée sans nouvelle génération puis peut progresser. Si le primaire a disparu et que seul N-1 subsiste, `Prepared` est rejeté sans effet ni débit supposé. | NON EXÉCUTÉ | XML/WAL rechargés + compteur native |
| CUT-03 | Après WAL `Attempted` flushé ou effet GTA tenté | Aucun replay, y compris si l'acquittement WAL a été perdu ou sa queue tronquée; état `Attempted`/`Ambiguous` conservateur. | NON EXÉCUTÉ | Compteur native + WAL |
| CUT-04 | Après résultat GTA inconnu | Pas de double débit/confiscation; litige ou restauration récupérable. | NON EXÉCUTÉ | WAL + état métier |
| CUT-05 | Après snapshot écrit | Révision disque cohérente et effet exactement une fois. | NON EXÉCUTÉ | Révisions + hashes |
| CUT-06 | Après backup remplacé | Primaire ou backup valide et sélectionné par génération/hash. | NON EXÉCUTÉ | Fichiers + hashes |
| CUT-07 | Pendant compaction WAL | Le mutex reste possédé depuis la validation jusqu'au remplacement. Un append concurrent échoue de façon retryable puis réussit sur une vue fraîche; aucune entrée ouverte n'est effacée ou inventée. | NON EXÉCUTÉ | WAL + log contention |

## E. Paiements — JUS-011

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| PAY-01 | Débit confirmé | Le cash baisse une fois; seule la somme confirmée alimente `VoluntaryFinePaid`. | NON EXÉCUTÉ | Cash/dette avant/après + WAL |
| PAY-02 | Débit rejeté | La dette exigible reste; aucun affichage de succès. | NON EXÉCUTÉ | Cash/dette + log |
| PAY-03 | Résultat inconnu, cash final attendu | Résolution `Confirmed`, jamais de second `STAT_SET_INT`. | NON EXÉCUTÉ | Compteur native + WAL |
| PAY-04 | Résultat inconnu, cash initial | Résolution `Rejected`, dette inchangée et aucun replay. | NON EXÉCUTÉ | Compteur native + WAL |
| PAY-05 | Troisième solde | Résolution `Ambiguous`; montant déplacé vers `FineInDispute`, visible et non converti en peine. | NON EXÉCUTÉ | Diagnostic/casier + WAL |
| PAY-06 | Cash illisible jusqu'au timeout | Résolution `Ambiguous`; aucun faux « payé » et aucun nouveau débit automatique. | NON EXÉCUTÉ | Log timeout + diagnostic |
| PAY-07 | Reload d'un litige | `FineInDispute` survit; le débit n'est jamais rejoué et reste distinct de la dette exigible. | NON EXÉCUTÉ | XML/WAL avant/après |

## F. Scans monde et performances — JUS-012

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| PERF-01 | Jeu libre calme, 10 min | Relever moyenne, p95, p99 et maximum des domaines Justice, entités, requêtes et file d'incidents. | NON EXÉCUTÉ | Export métriques + frametime |
| PERF-02 | Foule dense, 10 min | Au plus une requête peds et une véhicules par passe; aucun scan par acteur; absence de pic corrélé visible. | NON EXÉCUTÉ | Compteurs + graphe frametime |
| PERF-03 | Fusillade dense | Six incidents confirmés maximum par tick; le surplus reste en file et est traité ensuite. | NON EXÉCUTÉ | Compteur file + logs bornés |
| PERF-04 | Faible FPS | Les budgets restent bornés, aucun rattrapage massif sur une seule frame. | NON EXÉCUTÉ | Capture frametime + métriques |
| PERF-05 | Peine maximale | Pas de sérialisation complète toutes les deux secondes sur le thread GTA; horloge correcte en pause/reprise. | NON EXÉCUTÉ | Révisions + frametime |
| PERF-06 | Sauvegarde au casier maximal | Mesurer durée, allocations observables et révision disque sans hitch gameplay. | NON EXÉCUTÉ | Métriques persistence + vidéo |

## G. Profils, casier et limites — JUS-009, JUS-014

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| DOM-01 | Michael | Arrestation avec étoiles, paiement, libération et reload restent dans le slot 0. | NON EXÉCUTÉ | XML/diagnostic + vidéo |
| DOM-02 | Franklin | Arrestation avec étoiles, paiement, libération et reload restent dans le slot 1. | NON EXÉCUTÉ | XML/diagnostic + vidéo |
| DOM-03 | Trevor | Arrestation avec étoiles, paiement, libération et reload restent dans le slot 2. | NON EXÉCUTÉ | XML/diagnostic + vidéo |
| DOM-04 | Arrestation sans étoiles | Aucun dossier/condamnation n'est inventé sans causalité Justice valide. | NON EXÉCUTÉ | État avant/après |
| DOM-05 | Mort pendant poursuite | Filmer sans coupure une mort policière avec dossier actif. Le masque noir part pendant le ped mort, aucune frame jouable n'apparaît à l'hôpital, le même héros est maintenu dans l'enceinte pendant un WAL encore `Ambiguous`, puis une seule condamnation/capture l'envoie au bon site après confirmation primaire+backup. | NON EXÉCUTÉ | Vidéo complète mort/maintien/prison + WAL/XML/log |
| DOM-06 | Mort pendant détention | Mourir à Mission Row puis à Bolingbroke hors riposte garde. Après le respawn GTA, seul le même protagoniste est masqué et replacé dans la cellule du bon site, sans seconde condamnation, ajout de peine ni nouveau snapshot d'inventaire. Répéter après avoir agressé un garde et se faire tuer par lui : le retour cellule ajoute alors exactement 60 secondes une fois. | NON EXÉCUTÉ | Casier/inventaire/temps avant-après + vidéo complète |
| DOM-07 | Switch pendant poursuite | Le dossier reste au héros sortant; aucun wanted/cash/inventaire n'est muté sur l'entrant. | NON EXÉCUTÉ | Diagnostics des deux slots |
| DOM-08 | Switch pendant détention | Inventaire et casier ne changent pas de propriétaire; restauration police et son WAL sont achevés avant le switch, puis la révision du nouveau profil devient durable avant déblocage. | NON EXÉCUTÉ | Diagnostics révisions/slots + trace native/WAL |
| DOM-09 | Plus de vingt condamnations | L'historique visible reste borné à vingt et la condamnation de détention épinglée reste visible. | NON EXÉCUTÉ | Casier avant/après |
| DOM-10 | Plafond de peine | Accumuler des infractions et une conversion d'amende au-delà de dix minutes : la base judiciaire reste plafonnée à `600`. Se faire ensuite tuer une fois par un garde pendant sa riposte : l'extension vaut `60` et le total HUD/XML vaut `660`, sans modifier la condamnation de base. | NON EXÉCUTÉ | Détail F10 + HUD + XML |
| DOM-11 | Invariant impossible injecté | Un état `RemovedVerified` sans snapshot est refusé; aucune mutation monde ne suit. | NON EXÉCUTÉ | Build diagnostic + log |
| DOM-12 | Isolation des trois profils | Modifier successivement casier, dette et inventaire de chaque héros; aucune valeur ne fuit vers les deux autres. | NON EXÉCUTÉ | Diff des trois profils |
| DOM-13 | Mort policière sous modèle custom | Mourir avec des étoiles sous une tenue/ped custom : si GTA restaure le modèle canonique du même héros, le transfert reprend au poste ou en prison selon la peine. Un autre héros n'hérite jamais du front. | NON EXÉCUTÉ | Vidéo + diagnostic slot/modèle |
| DOM-14 | Switch pendant grâce d'évasion | Sortir de l'enceinte moins de six secondes puis changer de héros : aucune charge d'évasion ni étoile n'est créée. Au retour, la présence doit être revalidée et une nouvelle sortie exige six secondes continues. | NON EXÉCUTÉ | Vidéo + casier + timer |
| DOM-15 | Switch après intention d'évasion | Couper le changement une fois le discard engagé : le switch reste fermé jusqu'à la finalisation fail-closed et ne transforme jamais l'évasion durable en simple pause. | NON EXÉCUTÉ | WAL + diagnostic switch |
| DOM-16 | Maintenance naturelle de la scène | Déclencher un combat impliquant gardes et détenus : aucun ordre de retour ne coupe combat, fuite, taser ou ragdoll. Après au moins dix secondes de calme, les survivants peuvent regagner leur poste/volume par navmesh sans téléportation ni spam. Un PNJ mort n'est pas remplacé avant le démontage de la scène. | NON EXÉCUTÉ | Vidéo horodatée + trace tâches/cadences |
| DOM-17 | Agression d'un garde | Frapper un garde possédé : le wanted devient au moins deux sans diminuer 3–5, tous les gardes vivants de la scène ripostent sans renfort extérieur ni spam de tâches, tandis que la position, les charges et la peine restent inchangées tant que le joueur vit. La riposte cesse à la mort, la libération ou l'évasion. | NON EXÉCUTÉ | Vidéo continue + dossier/temps avant-après + trace wanted/tâches |
| DOM-18 | Agression d'un détenu | Frapper un détenu puis se défendre contre sa réaction : aucun traitement disciplinaire Justice n'intervient et les événements GTA du garde/détenu restent actifs. | NON EXÉCUTÉ | Vidéo + trace événements/relations |
| DOM-19 | Évasion après combat | Après une bagarre, sortir de toute l'enveloppe de confinement. Avant six secondes, aucune évasion n'est confirmée; à six secondes continues, une seule charge d'évasion est créée et le wanted est porté à trois étoiles minimum sans réduire un niveau supérieur. | NON EXÉCUTÉ | Vidéo avec timer + dossier + trace wanted |
| DOM-20 | Activités retirées | Parcourir tous les anciens points de Mission Row et Bolingbroke : aucun cylindre cyan, texte d'activité, interaction `E`, scénario, cooldown ou réduction de peine n'apparaît. Les marqueurs des portails d'intérieur hors Justice restent fonctionnels. | NON EXÉCUTÉ | Vidéo des six anciens points + vidéo d'un portail intérieur |
| DOM-21 | Seuls retours autorisés | Sans mourir, combattre, quitter/revenir dans les zones et rester en détention : aucune téléportation vers l'entrée ou la cellule. Vérifier séparément que le transfert initial, la reprise après reload, la mort et la libération légale conservent leurs déplacements attendus. | NON EXÉCUTÉ | Vidéos continues + positions/logs de transition |
| DOM-22 | Mortalité du joueur | Entrer avec `IsInvincible=true`, purger une peine, mourir en détention puis sortir par libération, évasion, amnistie et arrêt du script sur des copies dédiées. Le joueur est mortel dès que chaque écran de transfert est rendu et reste mortel après chaque sortie; aucune ancienne valeur `storedInvincible=true` ne revient au reload. | NON EXÉCUTÉ | Vidéo dégâts + XML avant/après + lecture native |
| DOM-23 | Stabilité visuelle du maintien durable | Reproduire DOM-05 jusqu'à obtenir la source `DurablePoliceDeath`. Filmer sans coupure le FadeOut initial, le placement vérifié et le premier FadeIn, puis rester 60 secondes dans l'enceinte sans provoquer une nouvelle mort ni en sortir. À 15 secondes, ouvrir F10, laisser le menu visible 10 secondes, puis le fermer et poursuivre jusqu'à 60 secondes. Le menu et le monde restent continuellement visibles : exactement un armement et une restitution sont admis, sans nouveau fondu ni pulse noir de `0,4–0,5 s` tant que le maintien reste valide. Le WAL peut demeurer durable sans nouvelle mutation de peine, cash, inventaire, wanted ou phase avant confirmation. | NON EXÉCUTÉ | Vidéo continue ≥ 60 s + chronologie FadeOut/FadeIn + WAL/log source + capture F10 |

## H. Nouveau barème et conversion

Les essais BAL utilisent des profils propres après le reset de politique. Chaque valeur doit être relevée dans F10 et dans le XML afin de distinguer le calcul métier de son affichage.

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| BAL-01 | Barème de base exact | Produire séparément chaque infraction non nulle et vérifier les bases : destruction véhicule 20 s; agression simple et délit de fuite 30 s; fuite police, complicité agression agent et car-jacking 40 s; résistance 60 s; agression aggravée 80 s; agression agent 120 s; complicité meurtre agent 140 s; homicide involontaire 160 s; meurtre civil 240 s; évasion 300 s; meurtre agent 360 s. | NON EXÉCUTÉ | Détails F10 + XML de chaque cas |
| BAL-02 | Quantum et modificateurs | Produire des circonstances et de la récidive connues : appliquer les multiplicateurs existants, puis vérifier un résultat arrondi au multiple de cinq secondes supérieur, jamais à quinze. | NON EXÉCUTÉ | Calcul attendu + détail F10/XML |
| BAL-03 | Conversion d'amende | Forcer successivement 1 $, 1 500 $, 1 501 $ puis une dette très élevée sans cash : les ajouts attendus sont 10 s, 10 s, 15 s puis 100 s maximum, au taux de 150 $/s et au quantum de cinq secondes. | NON EXÉCUTÉ | Cash/plan de débit + condamnation/XML |
| BAL-04 | Seuil du site | Produire exactement 295 s puis 300 s : 295 s utilise Mission Row; 300 s utilise Bolingbroke. Vérifier le même choix au maintien pré-jugement et au transfert final. | NON EXÉCUTÉ | Vidéo des deux transferts + log de sélection |
| BAL-05 | Saturation | Partir d'une peine de 590 s et convertir une dette élevée : le résultat est exactement 600 s. Charger aussi une fixture courante au-dessus de 600 s avec amende impayée nulle : elle est normalisée à 600 s. | NON EXÉCUTÉ | XML avant/après + HUD `10:00` |
| BAL-06 | Infractions sans détention | Vérifier que les infractions historiquement à zéro seconde restent sans détention et n'acquièrent pas un minimum artificiel. | NON EXÉCUTÉ | Détails F10 + XML |

## I. Pause, reprise et reset legacy du bouton ON/OFF

Pour ces neuf scénarios, je conserve une copie des sauvegardes avant et après chaque étape. Le reset automatique d'une ancienne politique est destructif pour tout le judiciaire mais conserve ON/OFF; une fois `sentencePolicyVersion="2"` installé, le bouton ON/OFF ne doit jamais appeler une native d'effacement wanted, vider un dossier ou ouvrir la confirmation danger. Les variations naturelles du wanted produites par GTA restent possibles; la preuve doit donc inclure une trace native ou une observation immédiate avant/après le toggle.

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| TGL-A | Reset global de l'ancienne sauvegarde | Sans supprimer `_justice_state.xml`, installer la build sur une sauvegarde sans `sentencePolicyVersion="2"` contenant des données différentes et des préférences ON/OFF différentes sur les trois slots, dont le verrou `Amnistie préparée`. Après chargement, chaque `Case`, `Record` et `Custody` est neuf; charges, peine, amende, mandat, casier, récidive, activité et discipline ont disparu, tandis que chaque valeur `Enabled` est identique à l'ancienne. Le message d'amnistie ne revient jamais. | NON EXÉCUTÉ | Primaire/backup/WAL/quarantaine avant-après + diff des trois profils + captures F10 |
| TGL-B | Pause avec dossier actif | Activer Justice, créer au moins une charge avec score, amende et mandat observables, puis relever le wanted GTA. Désactiver depuis F10. Le statut devient `Désactivée · dossier conservé`; charges, score, amende, peine, mandat, dernière infraction, casier et récidive sont inchangés. Aucune confirmation d'amnistie, aucun `CLEAR_PLAYER_WANTED_LEVEL` et aucune écriture `WantedLevel = 0` ne sont émis. | NON EXÉCUTÉ | Captures F10 avant/après + XML diff + trace native wanted |
| TGL-C | Redémarrage en pause | Laisser Justice désactivée avec le dossier actif de TGL-B, quitter proprement, relancer GTA et reprendre le même protagoniste. Justice reste désactivée, le statut signale le dossier conservé et toutes les données du dossier/casier restent identiques. Aucun ancien latch d'amnistie ne réapparaît. | NON EXÉCUTÉ | XML primaire/backup + captures avant/après reload + log |
| TGL-D | Reprise après pause | Pendant la pause, produire un fait qui aurait normalement pu devenir une infraction, puis réactiver Justice. Aucun événement de la période de pause n'est ajouté rétroactivement. Si le wanted GTA existe encore, la poursuite reprend sans nouvelle charge; s'il a disparu naturellement, l'ancienne phase persistante devient un mandat sans recréer d'étoiles. Commettre ensuite une nouvelle infraction : elle est enregistrée normalement. | NON EXÉCUTÉ | Casier avant/après + trace wanted + log de détection |
| TGL-E | Trois protagonistes isolés | Pour Michael, Franklin et Trevor : activer Justice, créer un dossier distinct, désactiver, changer de protagoniste, revenir, vérifier le statut et réactiver. Chaque profil conserve indépendamment son ON/OFF, ses charges, sa dette, son mandat et son casier, sans toucher aux deux autres profils ni au wanted du héros entrant. | NON EXÉCUTÉ | XML des trois profils + captures des trois statuts + trace slot/modèle/wanted |
| TGL-F | Reset volontaire ciblé | Après la migration automatique, créer un dossier puis vérifier que ON/OFF commute immédiatement sans fenêtre rouge et sans effacement. Exécuter ensuite `Justice → Réinitialiser ce personnage` : cette action volontaire ouvre la confirmation danger et, après validation, efface le seul profil ciblé selon son protocole WAL sans toucher aux deux autres profils. | NON EXÉCUTÉ | Vidéo F10 + XML/WAL avant/après + diagnostics des trois slots |
| TGL-G | Ancien détenu actif | Démarrer le reset sur le héros actif avec inventaire retiré, contrôles/état transitoire et suppression police persistés. Le profil judiciaire devient vide, mais son jeton reste durable jusqu'à fusion sûre de l'ancien inventaire avec les armes éventuellement acquises depuis, restitution des contrôles, de la mobilité, de l'invincibilité/ragdoll temporaires, des flags police et d'une sortie sûre; il est ensuite supprimé dans le primaire et le backup. La récupération n'appelle jamais `RemoveAll` et ne rejoue aucune ancienne peine, écriture cash ou mutation wanted. | NON EXÉCUTÉ | Inventaire/état/natives avant-après + XML/WAL/quarantaine + vidéo |
| TGL-H | Récupération d'un héros inactif | Placer un snapshot/flag récupérable sur Michael puis lancer avec Franklin. Le dossier de Michael est immédiatement vide mais un seul `policyResetRecovery` reste lié au slot 0; Franklin ne reçoit aucun effet. En revenant sur Michael, la récupération s'applique une fois puis disparaît durablement. | NON EXÉCUTÉ | Diagnostics des slots + XML/WAL + inventaires avant-après |
| TGL-I | Coupures et idempotence | Couper successivement après mise en quarantaine, après création du WAL neuf, après publication du primaire et après publication du backup. Chaque relance reprend sans restaurer les vieux dossiers ni rejouer un effet externe. Une fois la version 2 prouvée, créer un nouveau dossier, redémarrer deux fois et vérifier qu'il reste intact. | NON EXÉCUTÉ | Jeux de fichiers à chaque coupure + compteurs natives/cash + XML final |

## J. Reconnaissance policière — plaques, tenues et mandat local

Chaque scénario REC est exécuté séparément avec Michael, puis les scénarios
d'isolation sont répétés avec Franklin et Trevor. Je conserve avant/après le
wanted GTA, les captures HUD/F10, `JusticeRecognition.xml`, son `.bak`,
`JusticeRecognition.critical-intents.xml`, son `.bak`, `JusticeRecognition.log`
et `_justice_state.xml`. Le « mandat local » désigne ici la zone temporaire de
reconnaissance ; il ne doit jamais être confondu avec le mandat judiciaire du
dossier Justice.

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| REC-01 | Création après fuite réelle | Démarrer une poursuite, laisser le module capturer véhicule/tenue/apparence puis perdre naturellement les étoiles sans mourir, être arrêté, changer de héros ni se téléporter. Après la stabilisation, un épisode unique persiste le niveau maximal, les signatures disponibles et une zone centrée sur la dernière position. La même perte wanted annoncée comme capture, libération, changement de héros ou suppression interne ne crée aucun signalement. | NON EXÉCUTÉ | Vidéo continue + trace wanted/bridge + XML/log |
| REC-02 | Plaque reconnue | Avec un véhicule plaqué signalé, reprendre exactement le même modèle, la même plaque normalisée et les deux mêmes peintures. Le HUD affiche l'icône d'immatriculation active et F10 indique le véhicule courant reconnu au niveau mémorisé; une reconnaissance valable peut restaurer ce minimum sans le diminuer s'il est déjà supérieur. Un autre modèle, une autre plaque ou l'absence de plaque d'un seul côté ne correspond pas. | NON EXÉCUTÉ | Plaque/couleurs avant-après + HUD/F10 + trace wanted |
| REC-03 | Repeinture et véhicule sans plaque | Repeindre le même véhicule en conservant sa plaque : le signalement visuel devient neutralisé une seule fois, l'icône s'atténue et ce véhicule ne fournit plus de plancher wanted. Tester aussi un véhicule sans plaque utilisable : modèle + peinture doivent tous deux correspondre; le modèle seul est refusé. | NON EXÉCUTÉ | Vidéo atelier/trainer + XML `Neutralized` + log |
| REC-04 | Tenue, apparence et masque | Reprendre exactement le modèle, les composants/textures/palettes et les props signalés : la tenue est reconnue. Changer successivement vêtements, coiffure/visage/barbe, les deux, puis ajouter un masque : le risque affiché et la vitesse d'exposition diminuent selon le cas, sans immunité absolue ni changement de profil. Sur une copie dédiée, rendre ensuite `AppearanceEvidence` absent/invalide puis rendre la signature d'apparence courante indisponible : dans les deux cas la reconnaissance corporelle reste à zéro, sans dénonciation; seul un véhicule indépendamment reconnu peut encore exposer le joueur. | NON EXÉCUTÉ | Captures tenues/HUD/F10 + XML + chronométrage exposition |
| REC-05 | Dimensions du mandat local | Créer des épisodes de niveau 1 à 5. Vérifier les couples rayon/durée exacts : `350 m/180 s`, `500 m/300 s`, `700 m/480 s`, `900 m/720 s`, `1 200 m/1 080 s`. Le blip circulaire reste centré sur la dernière position valide, survit au reload, expire à l'heure UTC prévue et ne modifie aucune charge de `_justice_state.xml`. | NON EXÉCUTÉ | Carte/positions + XML avant-après + casier inchangé |
| REC-06 | Reconnaissance progressive et génération d'observateur | Dans une zone au plancher 4, tester successivement un wanted courant de 0, 1, 3 puis 4 après les huit secondes de grâce. Une simple entrée ne suffit pas : l'exposition continue pour 0, 1 et 3 étoiles, puis s'arrête et se vide dès que 4 est atteint. Maintenir puis casser la ligne de vue : l'exposition monte et redescend. Un policier applique le wanted au seuil; un civil ne le fait qu'après son délai de signalement de 2,5 s. Le cooldown de vingt secondes empêche une réacquisition immédiate répétée. Faire ensuite disparaître un observateur partiellement exposé ou déjà en délai civil et provoquer la réutilisation de son handle : une différence de modèle ou de `MemoryAddress`, ou un nouveau wrapper lorsque les deux adresses sont indisponibles, crée un état neuf à exposition zéro sans ancien signalement. | NON EXÉCUTÉ | Vidéo chronométrée + trace handle/modèle/adresse/exposition + wanted |
| REC-07 | Bornes et cadence | En foule dense, confirmer un scan au plus tous les 350 ms, douze observateurs suivis au maximum et expiration des états runtime hors vue. Aucun scan ou rattrapage massif ne survient pendant mission, chargement, mort, arrestation, changement de héros ou lorsque Justice est OFF. | NON EXÉCUTÉ | Frametime + compteurs/logs instrumentés |
| REC-08 | Durées et limites des indices | Pour les niveaux 1 à 5, vérifier les durées plaque `8/12/18/25/35 min` et tenue `6/10/15/20/30 min`. Générer plus de quatre véhicules et cinq tenues : seules les bornes documentées subsistent, les plus anciennes expirent/se retirent sans toucher au dossier judiciaire. | NON EXÉCUTÉ | Série XML horodatée + HUD/F10 |
| REC-09 | Profils isolés | Créer des plaques, tenues et zones différentes pour Michael, Franklin et Trevor, puis alterner rapidement les héros. Chaque profil nommé retrouve uniquement ses indices et son blip; un ped transitoire/custom non attribuable suspend la reconnaissance au lieu d'adopter les données du héros précédent ou suivant. | NON EXÉCUTÉ | XML des trois profils + vidéo switch + log identité |
| REC-10 | XML séparé, variantes récupérables et corruption totale | Vérifier que `Scripts\DonJJusticeRecognition\JusticeRecognition.xml` porte le schéma 1 et uniquement les profils/signatures/zones de reconnaissance. Pour le store principal, placer successivement la copie valide la plus récente dans `.bak`, `.tmp`, `.bak.tmp`, `.rollback` puis `.bak.rollback` : elle est sélectionnée et republiée dans un primaire + backup identique. Corrompre ensuite les six variantes principales; elles sont déplacées dans `JusticeRecognition.xml.corrupt-quarantine` et une paire neuve identique est créée, sans recharger un ancien XML. Répéter la corruption totale sur les six variantes de `JusticeRecognition.critical-intents.xml` pendant une amnistie ou un reset préparé : elles passent dans sa propre `.corrupt-quarantine`, un couple neuf est publié, puis l'intention Justice courante y est réenregistrée avant toute terminalisation. Aucun nœud de reconnaissance ou d'intention n'apparaît dans `_justice_state.xml`, son WAL, `_last_save.txt` ni une scène. Dans une installation isolée où le dossier prioritaire est inutilisable, données, journal, backups, quarantaines et log utilisent ensemble le repli `%LOCALAPPDATA%`, puis seulement le repli d'assembly. | NON EXÉCUTÉ | Variantes datées + listings des deux quarantaines + hashes des couples neufs + ID réenregistré + log |
| REC-11 | Toggle non destructif | Avec plaque, tenue et mandat local actifs, relever le wanted puis passer Justice sur OFF. Le blip et les expositions runtime disparaissent, mais les indices, dates et zone restent inchangés dans le XML, tandis que le wanted GTA reste lui aussi inchangé. Aucun observateur ne progresse pendant la pause. Repasser ON recrée le blip et reprend les seuls indices persistés, sans rejouer les observations produites pendant OFF. | NON EXÉCUTÉ | Vidéo/HUD + XML diff + trace native wanted |
| REC-12 | Capture, amnistie et reset explicites | Après toute arrestation confirmée ou mort policière capturée, les icônes plaque, tenue et mandat local disparaissent et les preuves plaque/tenue/apparence/zone du seul protagoniste capturé sont supprimées sans toucher aux deux autres. Vérifier séparément un transfert en cellule et une arrestation dont l'amende intégralement payée ramène la peine à zéro : les deux doivent effacer les indices. Injecter un refus du journal Recognition avant le transfert : le joueur reste masqué/bloqué, l'inventaire n'est pas retiré et le téléport ne part pas avant le retry durable. Pour la capture, l'amnistie puis le reset ciblé, vérifier que le bridge ne confirme l'inscription durable qu'après apparition du même identifiant de commande dans le primaire et le backup `JusticeRecognition.critical-intents.xml`. L'amnistie et le reset ne deviennent pas terminaux dans Justice avant cette preuve. L'intention disparaît seulement après présence prouvée de l'effacement dans le primaire et le backup `JusticeRecognition.xml`, puis republication identique des deux copies du journal. Supprimer ensuite sur une copie dédiée le profil ciblé avant de rejouer une capture puis un clear : l'absence est republiée dans les deux copies principales, la commande est acquittée sans créer de profil vide, et un second replay reste sans effet. Le toggle ON/OFF seul ne suit jamais ces chemins d'effacement. | NON EXÉCUTÉ | Vidéo HUD + chronologie des quatre XML + profils avant/après + IDs commande + log |
| REC-13 | Reload, coupures et acquittement | Quitter proprement avec des indices actifs : `JusticeRecognition.xml` et son backup restent lisibles, le HUD et le mandat local reviennent pour le bon héros, sans double épisode ni hausse wanted au chargement. Sur des copies dédiées, interrompre successivement après publication de l'intention critique, après effacement du primaire de données, après mise à jour des deux copies de données et avant mise à jour du journal. Chaque reprise recharge et rejoue le même identifiant, n'acquitte rien tant que les deux copies de données ne prouvent pas l'effacement, puis retire l'intention des deux copies du journal. Un second redémarrage ne rejoue plus la commande acquittée et aucun ancien indice ne réapparaît. | NON EXÉCUTÉ | Fichiers/hashes à chaque coupure + IDs stables + log de replay/acquittement |
| REC-14 | Garde anti-boucle et lecture wanted fail-closed | Déclencher une réacquisition par plaque/tenue puis par observateur dans la zone. L'écriture wanted du module ne devient pas une fausse escalade naturelle, ne crée pas un nouvel épisode et ne renouvelle pas indéfiniment les expirations. Injecter ensuite une erreur de lecture wanted après un dernier niveau fiable non nul : le module conserve ce niveau, ne fabrique ni perte à zéro ni nouvel épisode et n'ouvre pas le scan si le plancher est déjà atteint. Une libération ou capture qui efface volontairement les étoiles arme la suppression correspondante et ne fabrique pas une fuite. | NON EXÉCUTÉ | Trace lectures/appels wanted + dernier niveau fiable + IDs/dates XML + log |
| REC-15 | Assets requis et provider optionnel | Vérifier les trois PNG exacts sous `Scripts\Assets\Justice`. Sans provider v3, le HUD natif P/T/M fonctionne; avec un provider compatible prévalidé, les PNG immatriculation/tenue/mandat sont rendus; avec un provider présent mais incompatible, le préflight refuse avant écriture. Retirer ou corrompre un PNG fait toujours refuser le package ou le déploiement. | NON EXÉCUTÉ | Hashes/manifest + sortie préflight/déploiement + captures des deux rendus |

## Décision de release

| Critère final issu de l'audit | Résultat | Preuve |
|---|---|---|
| Package dérivé du binaire testé et hashes identiques | NON EXÉCUTÉ | — |
| Build Release sans déploiement implicite | NON EXÉCUTÉ | — |
| Une erreur d'un domaine ne bloque pas Justice | NON EXÉCUTÉ | — |
| Une erreur Justice ne bloque pas les restaurations critiques | NON EXÉCUTÉ | — |
| Inventaire incompatible sans verrouillage combat | NON EXÉCUTÉ | — |
| Aucun retrait sans snapshot validé et durable | NON EXÉCUTÉ | — |
| Restitution partielle récupérable après reload | NON EXÉCUTÉ | — |
| Arrêt restaurant contrôles et police | NON EXÉCUTÉ | — |
| Backup réparé par relecture et hash | NON EXÉCUTÉ | — |
| Front d'arrestation conservé pendant réparation | NON EXÉCUTÉ | — |
| Condamnation épinglée visible au-delà de vingt sanctions | NON EXÉCUTÉ | — |
| Paiement ambigu explicitement litigieux | NON EXÉCUTÉ | — |
| Sauvegarde complète hors thread GTA | NON EXÉCUTÉ | — |
| Aucun pic Justice mesurable en foule dense | NON EXÉCUTÉ | — |
| Profils Michael/Franklin/Trevor isolés | NON EXÉCUTÉ | — |
| Combat entre détenus ignoré; attaque d'un garde donnant deux étoiles minimum et riposte locale bornée | NON EXÉCUTÉ | — |
| Réactions GTA naturelles, tâches garde non spammées et maintenance après dix secondes de calme | NON EXÉCUTÉ | — |
| Mort renvoyant le détenu en cellule, avec +60 s exactement une fois uniquement si un garde ripostant le tue | NON EXÉCUTÉ | — |
| Évasion après six secondes avec minimum de trois étoiles | NON EXÉCUTÉ | — |
| Aucun rond cyan, interaction `E`, scénario ou réduction d'activité Justice | NON EXÉCUTÉ | — |
| Barème divisé par trois, conversion 150 $/s, base plafonnée à dix minutes et extension garde séparée | NON EXÉCUTÉ | — |
| Pause/reprise ON/OFF non destructive avec dossier et wanted conservés | NON EXÉCUTÉ | — |
| Reset legacy des trois profils conservant uniquement ON/OFF et les récupérations techniques | NON EXÉCUTÉ | — |
| Reset interrompu repris sans replay puis jamais répété après la version 2 | NON EXÉCUTÉ | — |
| `Réinitialiser ce personnage` reste le seul effacement volontaire exposé dans F10 | NON EXÉCUTÉ | — |
| Trois assets Justice présents, hashés, installés et rollbackés comme un seul contrat | NON EXÉCUTÉ | — |
| Provider HUD v3 externe prévalidé sans référence de compilation ni copie dans le package | NON EXÉCUTÉ | — |
| Plaques et tenues reconnues/neutralisées conformément aux signatures persistées | NON EXÉCUTÉ | — |
| Mandat local progressif sans charge judiciaire inventée ni boucle wanted | NON EXÉCUTÉ | — |
| Profils reconnaissance Michael/Franklin/Trevor et XML séparé sans fuite | NON EXÉCUTÉ | — |
| Toggle OFF/ON conservant indices, zone et wanted GTA sans replay d'observateur | NON EXÉCUTÉ | — |
| Handle observateur recyclé sans transfert d'exposition ni délai de dénonciation | NON EXÉCUTÉ | — |
| Intentions critiques redondantes rejouées puis acquittées seulement après les deux copies de données | NON EXÉCUTÉ | — |
| Corruption des six variantes mise en quarantaine puis intention courante réenregistrée avant terminalisation | NON EXÉCUTÉ | — |
| Store principal récupérant `.bak.tmp`/rollbacks et quarantainant ses six variantes illisibles | NON EXÉCUTÉ | — |
| Wanted illisible conservant le dernier niveau fiable et mandat actif sous son plancher uniquement | NON EXÉCUTÉ | — |
| Aucune reconnaissance corporelle sans apparence persistée et courante probante | NON EXÉCUTÉ | — |
| Capture/clear d'un profil absent acquitté après preuve redondante sans recréation | NON EXÉCUTÉ | — |
| Arrestation/capture retirant durablement les icônes plaque, tenue et mandat du seul profil capturé | NON EXÉCUTÉ | — |
| Détention et sorties garantissant `IsInvincible=false`, y compris depuis une ancienne baseline true | NON EXÉCUTÉ | — |
| Reconnaissance bornée sans pic mesurable en foule dense | NON EXÉCUTÉ | — |
| Chaque frontière transactionnelle possède une reprise testée | NON EXÉCUTÉ | — |
| SHA-256 du diagnostic identique au manifest publié | NON EXÉCUTÉ | — |
| Manifest publié propre (`sourceDirty=false`) et package sale non déployable | NON EXÉCUTÉ | — |

La release reste bloquée tant qu'une ligne de cette décision vaut `FAIL`, `BLOQUÉ` ou `NON EXÉCUTÉ`, sauf dérogation écrite qui décrit précisément le risque résiduel.
