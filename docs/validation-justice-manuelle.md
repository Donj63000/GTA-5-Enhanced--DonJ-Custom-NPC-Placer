# Validation manuelle — Justice avancée

## Objet

Cette matrice complète les tests automatisés des correctifs JUS-001 à JUS-014. Elle doit être exécutée avec le package `game-ready` issu d'une seule exécution réussie de `tools/run-safety-checks.ps1`. Un résultat sans preuve exploitable ne vaut pas validation.

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
| Version GTA / NIB v2 | À renseigner | NON EXÉCUTÉ | Logs de démarrage |
| Mods actifs pendant l'essai | À renseigner | NON EXÉCUTÉ | Liste de fichiers / capture |

## Préparation obligatoire

1. Je ferme GTA et ses loaders.
2. Je conserve une copie du dossier `DonJEnemySpawnerSaves`, notamment `_justice_state.xml`, son `.bak`, `_justice_state.wal` et `_justice_state.v1.bak` s'il existe.
3. Je pars d'une source Git propre et je génère le package avec `tools/run-safety-checks.ps1`; je n'utilise aucun binaire maintenu manuellement. Un contrôle local lancé depuis une source modifiée produit uniquement un package `sourceDirty=true`, non publiable.
4. Je vérifie `manifestVersion=2`, le commit, `sourceDirty=false`, le schéma Justice exactement égal à 2, la référence `NIBScriptHookVDotNet2`/`ScriptHookVDotNet2` de version majeure 2, l'identifiant/version/SHA-256 du contrat ABI, les autres versions, les tailles et les SHA-256 du `manifest.json`.
5. J'installe uniquement ce package propre par le chemin de déploiement explicite, puis je recalcule le SHA-256 sous `Scripts`. `deploy-game-ready.ps1` doit refuser tout manifest `sourceDirty=true`.
6. J'active un enregistrement de frametime et je conserve `DonJCustomNpcPlacer.log`, `NIBScriptHookVDotNet.log` et `ScriptHookV.log` après chaque anomalie.
7. Pour les scénarios destructifs, je travaille sur une copie dédiée des sauvegardes et un profil de test GTA.

## A. Package, build et déploiement — JUS-001, JUS-002, JUS-013

| ID | Scénario | Procédure / résultat attendu | Résultat | Preuve |
|---|---|---|---|---|
| PKG-01 | Build Release ordinaire | Compiler sans `DeployToGta`; aucun fichier sous le vrai dossier GTA ne change. | NON EXÉCUTÉ | Horodatages et hashes avant/après |
| PKG-02 | Package canonique | Le package contient uniquement ENdll, PDB, guide et manifest; ENdll/PDB correspondent au build testé. | NON EXÉCUTÉ | Log safety + hashes |
| PKG-03 | Métadonnées | Commit, `sourceDirty=false`, version d'assembly, version informative, référence unique API majeure 2 et schéma exact 2 du manifest correspondent au binaire chargé. | NON EXÉCUTÉ | Manifest + réflexion/diagnostic |
| PKG-04 | Installation explicite | ENdll, PDB et manifest sont publiés puis relus avant le retrait des alias historiques. Le binaire précédent n'est jamais supprimé avant validation du nouveau; un échec restaure les alias déjà déplacés puis rollback les trois fichiers. Le manifest installé porte le nom stable `DonJCustomNpcPlacer.manifest.json`. | NON EXÉCUTÉ | Log de déploiement + hashes des trois fichiers |
| PKG-05 | Package corrompu | Altérer une copie du package; le déploiement échoue et conserve intégralement le binaire installé. | NON EXÉCUTÉ | Hash avant/après + sortie PowerShell |
| PKG-06 | Alias historiques | Après validation du nouveau triplet seulement, aucun `DonJCustomNpcPlacer.dll`, `DonJEnemySpawner.dll`, `.ENdll` ou `.pdb` obsolète ne subsiste. Simuler un alias verrouillé : le triplet et tous les alias reviennent à leur état initial, sans fenêtre volontairement dépourvue d'ENdll. | NON EXÉCUTÉ | Listing `Scripts` + hashes avant/après |
| PKG-07 | Build ID en jeu | Le diagnostic F10 affiche le commit/build ID et le SHA-256 correspondant au manifest publié. | NON EXÉCUTÉ | Capture F10 + manifest |
| PKG-08 | Source locale modifiée | Générer explicitement avec `-AllowDirtySource`; le manifest porte `sourceDirty=true`, le diagnostic ne le reconnaît pas comme publié et `deploy-game-ready.ps1` refuse l'installation sans toucher au binaire actif. | NON EXÉCUTÉ | Manifest + sortie PowerShell + hash avant/après |
| PKG-09 | Contrat ABI NIB | Le manifest v2 porte l'identifiant, la version et le SHA-256 du contrat ABI; altérer une référence membre ou présenter une DLL runtime incompatible doit être refusé avant toute écriture dans `Scripts`. | NON EXÉCUTÉ | Sortie du validateur + hashes du triplet avant/après |

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
| PER-03 | Fichier v1 valide | Migration explicite vers v2; avant le premier remplacement, l'original v1 est copié byte pour byte dans `_justice_state.v1.bak` et son SHA-256 est vérifié. Ce backup distinct n'est pas écrasé. | NON EXÉCUTÉ | Fichiers et hashes avant/après |
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

### Coupures transactionnelles obligatoires

Chaque ligne est exécutée pour un paiement, une confiscation/restitution et, quand applicable, une mutation police.

| ID | Point de coupure injecté | Invariant attendu | Résultat | Preuve |
|---|---|---|---|---|
| CUT-01 | Avant `WAL Prepared` | Aucun effet GTA; dette/inventaire/police inchangés. | NON EXÉCUTÉ | WAL + état avant/après |
| CUT-02 | Après snapshot `Prepared` durable, avant WAL `Attempted` | Reprise autorisée sans effet supposé; le plan peut être réévalué ou annulé si ses préconditions ont changé. | NON EXÉCUTÉ | XML/WAL rechargés + compteur native |
| CUT-03 | Après WAL `Attempted` flushé ou effet GTA tenté | Aucun replay, y compris si l'acquittement WAL a été perdu ou sa queue tronquée; état `Attempted`/`Ambiguous` conservateur. | NON EXÉCUTÉ | Compteur native + WAL |
| CUT-04 | Après résultat GTA inconnu | Pas de double débit/confiscation; litige ou restauration récupérable. | NON EXÉCUTÉ | WAL + état métier |
| CUT-05 | Après snapshot écrit | Révision disque cohérente et effet exactement une fois. | NON EXÉCUTÉ | Révisions + hashes |
| CUT-06 | Après backup remplacé | Primaire ou backup valide et sélectionné par génération/hash. | NON EXÉCUTÉ | Fichiers + hashes |
| CUT-07 | Pendant compaction WAL | Les entrées ouvertes restent récupérables; aucune transaction inventée. | NON EXÉCUTÉ | WAL tronqué + log |

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
| DOM-05 | Mort pendant poursuite | Reprise sur le même héros, une seule condamnation/capture. | NON EXÉCUTÉ | Casier + log |
| DOM-06 | Mort pendant détention | Retour en cellule sans seconde condamnation, peine ni confiscation. | NON EXÉCUTÉ | Casier/inventaire + vidéo |
| DOM-07 | Switch pendant poursuite | Le dossier reste au héros sortant; aucun wanted/cash/inventaire n'est muté sur l'entrant. | NON EXÉCUTÉ | Diagnostics des deux slots |
| DOM-08 | Switch pendant détention | Inventaire et casier ne changent pas de propriétaire; restauration police et son WAL sont achevés avant le switch, puis la révision du nouveau profil devient durable avant déblocage. | NON EXÉCUTÉ | Diagnostics révisions/slots + trace native/WAL |
| DOM-09 | Plus de vingt condamnations | L'historique visible reste borné à vingt et la condamnation de détention épinglée reste visible. | NON EXÉCUTÉ | Casier avant/après |
| DOM-10 | Plus de vingt fautes disciplinaires | L'épinglage principal résiste aux évictions et la récidive n'est pas doublée. | NON EXÉCUTÉ | Casier + XML |
| DOM-11 | Invariant impossible injecté | Un état `RemovedVerified` sans snapshot est refusé; aucune mutation monde ne suit. | NON EXÉCUTÉ | Build diagnostic + log |
| DOM-12 | Isolation des trois profils | Modifier successivement casier, dette et inventaire de chaque héros; aucune valeur ne fuit vers les deux autres. | NON EXÉCUTÉ | Diff des trois profils |

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
| Chaque frontière transactionnelle possède une reprise testée | NON EXÉCUTÉ | — |
| SHA-256 du diagnostic identique au manifest publié | NON EXÉCUTÉ | — |
| Manifest publié propre (`sourceDirty=false`) et package sale non déployable | NON EXÉCUTÉ | — |

La release reste bloquée tant qu'une ligne de cette décision vaut `FAIL`, `BLOQUÉ` ou `NON EXÉCUTÉ`, sauf dérogation écrite qui décrit précisément le risque résiduel.
