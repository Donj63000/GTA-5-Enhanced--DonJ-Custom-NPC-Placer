<h1 align="center">[GTA5 Enhanced] DonJ Custom NPC Placer </h1>

<p align="center">
  <strong>Solo scene creation tool for GTA V Enhanced</strong><br>
  <sub>NPCs, guards, patrols, respawn, Cartel, Ballas, high-security escort, Terminator mode, Advanced Justice, vehicles, objects, interiors, and XML saves.</sub>
</p>

<p align="center">
  <img src="images-readme/acceuil.png" alt="DonJ Custom NPC Placer - mod presentation image" width="100%">
</p>

## Installation express / Quick Start

> [!CAUTION]
> **Mode Histoire / Story Mode uniquement.** Ce mod est fait pour GTA V Enhanced en solo : ne l'utilise jamais dans GTA Online. Ferme le jeu avant de copier des fichiers — GTA aime avoir le dernier mot, mais on peut lui éviter ce débat.

> [!TIP]
> **Tu n'as rien à compiler.** Télécharge simplement le package vérifié de **DonJ Custom NPC Placer**, copie les bons fichiers aux bons endroits, puis ouvre le menu avec `F10`.

### Français : ta mission installation

Pas besoin d'être expert : suis les étapes dans l'ordre et coche-les une par une. À la fin, tu auras installé **notre** mod, `DonJ Custom NPC Placer`.

#### 1. Prépare les prérequis

| À installer | À quoi ça sert | Où cela va |
|---|---|---|
| [Microsoft .NET Framework 4.8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) | Permet d'exécuter les mods .NET. | Installation Windows normale. |
| [Script Hook V](https://www.dev-c.com/gtav/scripthookv/) | Charge les scripts GTA ; prends aussi `xinput1_4.dll` fourni pour Enhanced. | Dossier principal de GTA. |
| [NIBScriptHookVDotNet pour GTA Enhanced](https://www.patreon.com/posts/nibmods-menu-and-22783974) | Charge le mod avec l'API v2. La DLL v3 est optionnelle pour le rendu `GTA.UI.CustomSprite`; le HUD natif reste disponible sans elle. | Dossier principal de GTA. |

Dans le téléchargement NIB, choisis bien la version **GTA V Enhanced** lorsqu'elle est proposée. Les fichiers ScriptHook/NIB vont à côté de l'exécutable du jeu, **pas** dans `Scripts`.

#### 2. Trouve le bon dossier GTA

Dans Steam : **Bibliothèque** → clic droit sur **Grand Theft Auto V Enhanced** → **Gérer** → **Parcourir les fichiers locaux**.

Tu es au bon endroit si tu vois ce fichier :

```text
GTA5_Enhanced.exe
```

Crée un dossier nommé exactement `Scripts` s'il n'existe pas encore. C'est le seul sous-dossier qui recevra notre mod.

#### 3. Télécharge notre mod depuis GitHub

1. Ouvre la page [Safety du dépôt](https://github.com/Donj63000/GTA-5-Enhanced--DonJ-Custom-NPC-Placer/actions/workflows/safety.yml).
2. Choisis la dernière exécution avec une coche verte, dont la branche est **`main`** et l'événement est **`push`**.
3. Descends jusqu'à **Artifacts** et télécharge exactement :

   ```text
   DonJCustomNpcPlacer-game-ready
   ```

4. Décompresse l'archive téléchargée.

> [!IMPORTANT]
> N'utilise pas **Code → Download ZIP** : c'est le code source, pas le mod prêt à jouer. Utilise uniquement l'artefact `DonJCustomNpcPlacer-game-ready` et les noms de fichiers indiqués ci-dessous.

#### 4. Copie les fichiers au bon endroit

L'archive contient `DonJCustomNpcPlacer.ENdll`, `manifest.json`, `DonJCustomNpcPlacer.pdb` (facultatif), `Assets\Justice` avec trois PNG obligatoires et `INSTALLATION_SIMPLE.txt`.

Copie `DonJCustomNpcPlacer.ENdll`, le dossier complet `Assets\Justice` et `manifest.json` dans `Scripts`. Renomme ensuite `manifest.json` en **`DonJCustomNpcPlacer.manifest.json`**. Tu peux aussi copier le `.pdb` si tu veux des logs plus lisibles. Laisse `INSTALLATION_SIMPLE.txt` hors de `Scripts` et ne copie jamais le dossier entier de l'archive.

Ton installation doit ressembler à ceci :

```text
Grand Theft Auto V Enhanced\
├── GTA5_Enhanced.exe
├── ScriptHookV.dll
├── xinput1_4.dll
├── NIBScriptHookVDotNet.asi
├── NIBScriptHookVDotNet2.dll
├── NIBScriptHookVDotNet3.dll              (facultatif, majeure 3 ou plus)
└── Scripts\
    ├── DonJCustomNpcPlacer.ENdll
    ├── DonJCustomNpcPlacer.manifest.json
    ├── DonJCustomNpcPlacer.pdb             (facultatif)
    └── Assets\Justice\
        ├── immatriculation.png
        ├── tenue.png
        └── mandat.png
```

#### 5. Lance et teste le mod

1. Lance GTA V Enhanced et entre en **mode Histoire**.
2. Appuie sur `F10` : le menu DonJ doit s'ouvrir.
3. Pour un premier test, choisis `Placement type: NPC`, sélectionne un modèle, démarre `Precise camera placement`, puis place le PNJ avec `Enter` ou le clic gauche.

> [!TIP]
> `F10` ne fait rien ? Respire : vérifie d'abord le dossier `Scripts`, les quatre fichiers requis à la racine du jeu et le mode Histoire. Ensuite, suis la [procédure détaillée](#installation) ou le [dépannage](#troubleshooting).

### English: your installation mission

No expert knowledge needed: follow the steps in order and tick them off one at a time. At the end, you will have installed **our** mod, `DonJ Custom NPC Placer`.

#### 1. Get the prerequisites ready

| Install this | Why you need it | Where it goes |
|---|---|---|
| [Microsoft .NET Framework 4.8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) | Lets Windows run .NET mods. | Install it normally in Windows. |
| [Script Hook V](https://www.dev-c.com/gtav/scripthookv/) | Loads GTA scripts; also take the supplied Enhanced `xinput1_4.dll`. | GTA's main folder. |
| [NIBScriptHookVDotNet for GTA Enhanced](https://www.patreon.com/posts/nibmods-menu-and-22783974) | Loads the mod through API v2. The v3 DLL is optional for `GTA.UI.CustomSprite`; the native HUD remains available without it. | GTA's main folder. |

When NIB offers a choice, select the **GTA V Enhanced** version. ScriptHook/NIB files belong next to the game executable, **not** inside `Scripts`.

#### 2. Find the correct GTA folder

In Steam: **Library** → right-click **Grand Theft Auto V Enhanced** → **Manage** → **Browse local files**.

You are in the right folder when you can see:

```text
GTA5_Enhanced.exe
```

Create a folder named exactly `Scripts` if it is missing. This is the only subfolder that receives our mod.

#### 3. Download our mod from GitHub

1. Open the repository's [Safety page](https://github.com/Donj63000/GTA-5-Enhanced--DonJ-Custom-NPC-Placer/actions/workflows/safety.yml).
2. Choose the latest run with a green check mark whose branch is **`main`** and whose event is **`push`**.
3. Scroll to **Artifacts** and download exactly:

   ```text
   DonJCustomNpcPlacer-game-ready
   ```

4. Extract the downloaded archive.

> [!IMPORTANT]
> Do not use **Code → Download ZIP**: that is source code, not the playable mod. Use only the `DonJCustomNpcPlacer-game-ready` artifact and the exact file names below.

#### 4. Copy the files to the right place

The archive contains `DonJCustomNpcPlacer.ENdll`, `manifest.json`, the optional `DonJCustomNpcPlacer.pdb`, the required three PNG files under `Assets\Justice`, and `INSTALLATION_SIMPLE.txt`.

Copy `DonJCustomNpcPlacer.ENdll`, the complete `Assets\Justice` directory, and `manifest.json` into `Scripts`. Then rename `manifest.json` to **`DonJCustomNpcPlacer.manifest.json`**. You may also copy the `.pdb` for clearer logs. Keep `INSTALLATION_SIMPLE.txt` outside `Scripts`, and never copy the whole archive folder.

Your finished installation should look like this:

```text
Grand Theft Auto V Enhanced\
├── GTA5_Enhanced.exe
├── ScriptHookV.dll
├── xinput1_4.dll
├── NIBScriptHookVDotNet.asi
├── NIBScriptHookVDotNet2.dll
├── NIBScriptHookVDotNet3.dll              (optional, major 3 or newer)
└── Scripts\
    ├── DonJCustomNpcPlacer.ENdll
    ├── DonJCustomNpcPlacer.manifest.json
    ├── DonJCustomNpcPlacer.pdb             (optional)
    └── Assets\Justice\
        ├── immatriculation.png
        ├── tenue.png
        └── mandat.png
```

#### 5. Launch and test the mod

1. Launch GTA V Enhanced and enter **Story Mode**.
2. Press `F10`: the DonJ menu should open.
3. For a first test, choose `Placement type: NPC`, select a model, start `Precise camera placement`, then place the NPC with `Enter` or left click.

> [!TIP]
> Nothing happens when you press `F10`? Take a breath: first check the `Scripts` folder, the four required files in GTA's main folder, and Story Mode. Then follow the [detailed installation guide](#installation) or [troubleshooting guide](#troubleshooting).

## Table of Contents

- [Installation express / Quick Start](#installation-express--quick-start)
- [Main Features](#main-features)
- [Respawn / Automatic Respawn](#respawn--automatic-respawn)
- [Terminator Mode](#terminator-mode)
- [Advanced Justice](#advanced-justice)
- [Detailed Installation](#installation)
- [Usage](#usage)
- [Quick Example](#quick-example)
- [Saves](#saves)
- [Compatibility](#compatibility)
- [Build from Source](#build-from-source)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

<p align="center">
  <strong>Build a base, a checkpoint, an action scene, or a roleplay setup directly in story mode.</strong>
</p>

<p align="center">
  <a href="#installation-express--quick-start"><strong>Installation express / Quick Start</strong></a>
  |
  <a href="#installation"><strong>Detailed installation</strong></a>
  |
  <a href="#usage"><strong>Usage</strong></a>
  |
  <a href="#main-features"><strong>Features</strong></a>
  |
  <a href="#build-from-source"><strong>Source build</strong></a>
  |
  <a href="#report-a-bug"><strong>Report a bug</strong></a>
</p>

<p align="center">
  <img alt="GTA V Enhanced" src="https://img.shields.io/badge/GTA%20V-Enhanced-8b0000">
  <img alt="Single-player only" src="https://img.shields.io/badge/mode-single--player%20only-darkgreen">
  <img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET%20Framework-4.8-512bd4">
  <img alt="NIBScriptHookVDotNet API v2" src="https://img.shields.io/badge/NIBScriptHookVDotNet-API%20v2-blue">
  <img alt="Working status, active development" src="https://img.shields.io/badge/status-working%20%2F%20active%20dev-brightgreen">
  <img alt="Open source non-commercial license" src="https://img.shields.io/badge/license-open%20source%20non--commercial-lightgrey">
</p>

> [!IMPORTANT]
> **Project status: the mod is functional and usable in game.**
> It is still in active development so it can be refined, improve the experience, fix known limits, and add polish, but the current base already works in story mode.

<table>
  <tr>
    <td width="58%">
      <strong>Overview</strong>
      <br><br>
      <strong>DonJ Custom NPC Placer</strong> lets you quickly create custom scenes in Los Santos: <strong>armed NPCs</strong>, <strong>guards</strong>, <strong>patrols</strong>, <strong>allies</strong>, <strong>vehicles</strong>, <strong>objects</strong>, <strong>props</strong>, <strong>collectible cash</strong>, <strong>interior entrances/exits</strong>, <strong>Cartel reinforcement calls</strong>, <strong>hostile Ballas calls</strong>, <strong>high-security armored convoy escort</strong>, <strong>Terminator T-800 gameplay mode</strong>, an optional <strong>Advanced Justice</strong> system, and <strong>reusable XML saves</strong>.
      <br><br>
      The mod is designed as a clean, practical, and immersive placement tool for players who want to build their own bases, checkpoints, action scenes, secured zones, homemade missions, or roleplay setups in story mode.
    </td>
    <td width="42%">
      <strong>Highlights</strong>
      <br><br>
      <ul>
        <li><strong>Precise placement</strong> with free camera and transparent preview.</li>
        <li><strong>Configurable NPCs</strong> with weapons, health, armor, behaviors, and respawn.</li>
        <li><strong>Phone contacts</strong>: Cartel with <code>C</code>, Ballas with <code>R</code>, high-security escort with <code>L</code>.</li>
        <li><strong>Terminator mode</strong>: T-800 first-person HUD, red/night/thermal vision, resistance, and heavy melee impacts.</li>
        <li><strong>Advanced Justice</strong>: evidence-based cases, custody, and persistent plate/outfit recognition with local search warrants.</li>
        <li><strong>Collectible cash</strong> with several amounts for rewarding zones and missions.</li>
        <li><strong>Persistent scenes</strong> with automatic respawn and XML saves.</li>
      </ul>
    </td>
  </tr>
</table>

## How the Mod Works

<p align="center">
  <img src="Images/figma.png" alt="Diagram explaining how DonJ Custom NPC Placer works" width="100%">
</p>

The mod runs directly **inside GTA V Enhanced**, with no separate application to open.

| Step | What happens |
|---|---|
| **1. The game loads the mod** | When story mode starts, `ScriptHookV` and `NIBScriptHookVDotNet2` load `DonJCustomNpcPlacer.ENdll` from the `Scripts` folder. |
| **2. You open the menu** | In game, `F10` opens the custom DonJ Obsidian console. Its category rail lets you choose NPCs, vehicles, objects, interiors, scene management, Advanced Justice, or tools. |
| **3. You configure the scene** | You set the model, weapon, health, armor, behavior, patrol, respawn, vehicle, or object to place. |
| **4. You place it in the world** | Direct placement quickly places the item in front of the player. Camera placement lets you aim precisely, rotate the entity, and validate when it is clean. |
| **5. The mod manages the scene** | After placement, the mod maintains NPCs, their behaviors, blips, relationships, threats, patrols, bodyguards, the Cartel, the Ballas, the high-security escort, Terminator mode, and respawn. |
| **6. You save / reload** | Setups can be saved as XML and reloaded later to restore NPCs, vehicles, objects, portals, weapons, behaviors, and respawn options. |

In short: **you build the scene with the menu**, then the mod keeps the placed elements alive in game.

> [!CAUTION]
> **Story mode / single-player only.** Do not use this mod in GTA Online.

---

## At a Glance

<table>
  <tr>
    <td width="33%"><strong>Base or checkpoint</strong><br>Precise placement of NPCs, vehicles, objects, and cover.</td>
    <td width="33%"><strong>Guarded zone</strong><br>Allied, neutral, and hostile NPCs, patrols, defense, and respawn.</td>
    <td width="33%"><strong>Fast contacts</strong><br><code>C</code> for allied Cartel, <code>R</code> for hostile Ballas, <code>L</code> for a VIP armored escort.</td>
  </tr>
  <tr>
    <td width="33%"><strong>Clean placement</strong><br>Free camera, transparent preview, rotation, and direct placement.</td>
    <td width="33%"><strong>Persistent scenes</strong><br>Automatic respawn and XML loading for complete setups.</td>
    <td width="33%"><strong>Interiors</strong><br>Entrances/exits, extended catalog, and automatic IPL loading.</td>
  </tr>
  <tr>
    <td width="33%"><strong>Collectible loot</strong><br>Cash stacks, bags, briefcases, crates, and cash trolleys with different values.</td>
    <td width="33%"><strong>Mission rewards</strong><br>Cash objects to place in a safehouse, vault, office, or searchable zone.</td>
    <td width="33%"><strong>Gameplay value</strong><br>The player can collect cash with <code>E</code>, then the object disappears from the scene.</td>
  </tr>
  <tr>
    <td width="33%"><strong>Terminator mode</strong><br>T-800 resistance, armor, and restored state when disabled.</td>
    <td width="33%"><strong>Optical HUD</strong><br>First-person red overlay, night vision, thermal vision, reticle, and target profile.</td>
    <td width="33%"><strong>Heavy melee</strong><br>Close-range punches can throw NPCs and shove vehicles without turning gunfire into fake impacts.</td>
  </tr>
</table>

---

## New Category UI

<table>
  <tr>
    <td width="50%"><img src="images-readme/1.png" alt="New category UI - vehicles"></td>
    <td width="50%"><img src="images-readme/2.png" alt="New category UI - objects"></td>
  </tr>
  <tr>
    <td width="50%"><img src="images-readme/3.png" alt="New category UI - NPCs"></td>
    <td width="50%"><img src="images-readme/4.png" alt="New category UI - entrances and exits"></td>
  </tr>
</table>

## Main Features

### NPC Placement

The mod lets you place NPCs directly in the world with an integrated menu.

You can choose:

- the NPC model;
- the NPC category;
- the weapon;
- weapon attachments;
- health;
- armor;
- behavior;
- patrol radius;
- automatic respawn.

Available NPC categories:

- Custom / Add-on;
- Security / Police / Military;
- Gangs / Criminals;
- Multiplayer / Online;
- Services / Scenarios;
- Male civilians;
- Female civilians;
- Story / Cutscene;
- Animals;
- All NPCs.

The mod also supports custom models. Select the **Custom** model, then press `T` to enter the model name.

---

### NPC Behaviors

Each NPC can receive a different behavior:

| Behavior | Description |
|---|---|
| Static / hostile on sight | The NPC stays in position and becomes hostile when it detects a threat. |
| Attack / aggressive | The NPC actively attacks the player. |
| Neutral / passive guard | The NPC guards its area and reacts if a threat appears. |
| Ally / defense guard | The NPC defends the player against nearby threats. |
| Bodyguard / player escort | The NPC follows the player on foot or in a vehicle. |
| Neutral patrol | The NPC patrols an area without attacking immediately. |
| Hostile patrol | The NPC patrols and acts as an enemy. |
| Allied patrol | The NPC patrols and helps the player in combat. |

---

### Respawn / Automatic Respawn

> [!TIP]
> **Automatic respawn** is one of the most practical features for creating a base, checkpoint, or combat scene that stays usable for a long time.

Respawn lets a placed element **automatically reappear** after it has been killed, destroyed, or removed by the game.

It can be used to:

- restore **guards** after a fight;
- recreate **enemy or allied patrols**;
- bring back a **placed vehicle** if it explodes or disappears;
- restore a **decorative or cover object** if the game removes it;
- keep a base or checkpoint alive even after several attacks.

To use it:

1. Open the menu with `F10`.
2. Enable **Automatic respawn** before placing the element.
3. Place your NPC, vehicle, or object normally.
4. Save your setup if you want to keep this option in the XML file.

When respawn is enabled, the mod remembers the placed element's **original position**, **rotation**, **model**, equipment, and important settings.

Then, if the element disappears:

- the mod waits a short delay before recreating it;
- the player must have moved away from the area;
- the mod avoids spawning the element directly in front of the player;
- if the game refuses the spawn at that moment, the mod automatically tries again later.

In practice, this creates a cleaner scene: guards, vehicles, and objects do not brutally reappear in front of you. They mainly come back when you have left the area or when the respawn point is no longer visible, which preserves immersion.

> [!NOTE]
> Respawn is not meant to replace combat second by second in real time. If you stay in the same place while looking at the spawn point, the mod may wait before recreating the element to avoid a spawn that is too visible.

---

### Phone Calls C / R / L

<p align="center">
  <img src="images-readme/ballas.png" alt="Phone calls for Cartel with C, Ballas with R, and high-security escort with L" width="100%">
</p>

The phone lets you quickly launch three types of in-game activity: `C` calls allied Cartel protection, `R` triggers a hostile Ballas wave to create combat around the scene, and `L` calls a high-security escort with a limousine and armored convoy.

The three contacts stay visible whenever the phone is open, including during an Advanced Justice transfer or detention. In that situation they are clearly marked as temporarily unavailable, and no reinforcement, raid, or escort can spawn inside the custody area. They become usable again after release and a fresh key press.

### Cartel Call

<table>
  <tr>
    <td width="50%">
      <img src="Images/cartel4.jpg" alt="Cartel call confirmed from the player's phone">
    </td>
    <td width="50%">
      <img src="Images/cartel.jpg" alt="Cartel gunmen grouped around the player">
    </td>
  </tr>
  <tr>
    <td width="50%">
      <img src="Images/cartel2.jpg" alt="Armored Cartel Baller6 vehicles in close protection">
    </td>
    <td width="50%">
      <img src="Images/cartel3.jpg" alt="Cartel convoy during an action phase">
    </td>
  </tr>
</table>

The mod adds a **Cartel** phone contact that can be used directly in game.

When the player's phone is open, a `Phone contact` interface appears with the `Cartel` contact. Press `C` to call a protection team.

The call quickly brings in:

- up to 11 allied gunmen;
- up to 3 armored Baller6 vehicles;
- guards equipped for combat, with a Service Carbine and Machine Pistol;
- reinforced guards with `500` health and `200` armor.

The convoy appears at a reasonable distance, usually between `68 m` and `118 m`, preferably on a road and outside the player's field of view to keep the arrival immersive.

Cartel behavior:

- if the player is on foot, the vehicles approach and the men get out to follow;
- if the player is in a vehicle, the men get back into the Baller6 vehicles and escort the player;
- if there is a real threat, the guards defend the player or the other Cartel guards;
- passengers can shoot from the vehicle or get out depending on the situation;
- blocked vehicles can be reordered or moved out of view if the player gets too far away.
- the system is coded so your bodyguards eventually find you again as much as possible, as long as you are on a road or close to a usable road. If they are too far away, the mod can move them closer to the player, but out of view and never too close, to keep the illusion that they are really arriving instead of appearing in front of you.

Calling the Cartel again while a team is active orders it to withdraw. The men remain allied, return to the vehicles, leave the area, and are automatically cleaned up when they are far enough away or out of view.

You can call a new team even if an older team is still leaving the area.

### Ballas Call

The mod also adds a hostile **Ballas** call that can be used from the player's phone.

When the phone is open, press `R` to trigger a Ballas wave around the player. This key is meant to quickly create activity in story mode: ambush, base attack, street shootout, pressure on a checkpoint, or a simple dynamic event around a scene you already prepared.

The Ballas arrive as armed enemies and look for a fight with the player. Unlike the Cartel, they are not allied reinforcements: the Ballas call is designed to make the area feel alive and hostile without manually placing every NPC.

---

### High-Security Escort

The mod adds a **High-security escort** contact that can be used from the player's phone with `L`.

When the player's phone is open, press `L` to call an allied VIP convoy. The team arrives with:

- an armored limousine to transport the player;
- `4` black high-security Baller vehicles in formation;
- reinforced Cartel guards with `500` health and `200` armor;
- a combat setup with Service Carbine and Machine Pistol;
- dedicated AI so generic NPC orders do not replace convoy orders.

The escort is useful for playing a secured transfer, extraction, VIP arrival, protected escape, or homemade mission with close protection.

Main flow:

1. Open the player's phone.
2. Press `L` to call the escort.
3. When the limousine arrives, move close to it and press `F` to get in the back.
4. Place a waypoint on the map.
5. Once seated in the back of the limousine, press `L` to validate the destination.
6. The convoy drives to the waypoint in formation.

During the trip, the limousine keeps following the route toward the destination. The Baller vehicles reposition around it, drivers avoid receiving useless orders every frame, and blocked vehicles can attempt a short reverse maneuver or an out-of-view reposition if needed.

During an ambush, guards react like a real escort:

- passengers can perform drive-by shooting;
- guards can get out if the threat is close or if the vehicle is blocked;
- the limousine keeps route priority while the player is on board;
- the Baller vehicles switch to a more aggressive driving style to protect and catch up with the convoy;
- hostile relationships are applied only to valid threats to avoid breaking ambient groups.

If you press `L` again from the phone while an escort is active, the mod orders a withdrawal. The vehicles leave the area, guards are cleaned up properly when they are far enough away or out of view, and you can call a new escort after the short anti-spam delay.

---

### Terminator Mode

The mod includes a **Terminator mode** that can be enabled from the `F10` menu with the `Mode Terminator` row.

When enabled, the player receives a T-800 style combat profile:

- first-person camera is selected once when the mode starts;
- health is raised to `2000` on activation if it is below that value;
- armor is raised to `200`;
- critical hits and ragdoll reactions are reduced;
- health and armor regenerate only after a delay when the player has stopped taking damage;
- disabling the mode restores the player's previous health, armor, camera, and vision state.

The mode is designed to make the player very resistant, not permanently invincible. Heavy damage can still bring health down before regeneration has time to recover.

The Terminator HUD appears only when the mode is active **and the camera is in first person**. If you switch back to third person, the special vision is cleared; returning to first person re-applies it.

Vision modes:

| Key | Effect |
|---|---|
| `B` | Cycle Terminator vision while the mode is active |
| Red vision | Default T-800 red optical feed |
| Night vision | Green low-light vision for dark areas |
| Thermal vision | See-through thermal vision |

While aiming in first person, the HUD can display a focused target profile with type, faction, health, armor, weapon, model, and distance. The mode also adds heavy close-range melee impacts: confirmed punches can throw nearby NPCs or shove vehicles. Recent gunfire is ignored by this impact system so a close shot does not accidentally trigger a melee launch.

To leave the mode, open the `F10` menu again and select `Mode Terminator`. The mod clears the vision filters and restores the stored player state.

---

### Advanced Justice

<p align="center">
  <img src="Github%20images/justice-dossier.png" alt="Menu Justice avancée affichant le dossier judiciaire, le mandat actif, les infractions et la peine du personnage" width="100%">
</p>

<p align="center"><em>Un véritable dossier judiciaire par protagoniste, directement consultable depuis F10.</em></p>

**Justice avancée** transforme les poursuites du mode Histoire en un système cohérent et persistant. Les infractions doivent être prouvées, les témoins comptent, les sanctions s'accumulent dans un dossier propre à chaque héros et une fuite peut laisser derrière elle un mandat capable de reconnaître plus tard le véhicule, la plaque ou la tenue du joueur.

| Pilier | Comportement en jeu |
|---|---|
| **Enquête fondée sur les preuves** | Une victime, un témoin crédible, un policier ou un signal GTA corrélé doit confirmer l'acte. Le niveau de recherche seul n'invente jamais une infraction. |
| **Trois dossiers indépendants** | Michael, Franklin et Trevor conservent séparément leurs affaires, amendes, peines, mandats, condamnations et récidive. |
| **Mandat et reconnaissance persistants** | Après une fuite, la police peut mémoriser plaque, véhicule, couleurs, tenue et apparence, puis rechercher progressivement le joueur dans une zone locale limitée. |
| **Arrestation et détention jouables** | Amende, Mission Row ou Bolingbroke selon la gravité. Le joueur reste mortel; les combats entre détenus sont tolérés, mais attaquer un garde déclenche une riposte locale avec au moins deux étoiles. Une mort causée par ce garde renvoie en cellule et ajoute exactement 60 secondes. |
| **Mort policière et reprise sûre** | Une mort policière prouvée conduit au site de détention sous un masque continu, sans passage jouable à l'hôpital et sans emprisonner le mauvais protagoniste. |
| **Persistance résistante aux crashs** | XML principal, backup et journal transactionnel empêchent les doubles débits, doubles peines et reprises ambiguës après un crash ou un changement de héros. |

<p align="center">
  <img src="Github%20images/justice-prison-bolingbroke.png" alt="Détenu dans la cour de la prison de Bolingbroke avec le temps de peine restant affiché par Justice" width="100%">
</p>

<p align="center"><em>Les affaires les plus graves peuvent conduire le joueur à la prison de Bolingbroke, où la peine se purge directement en jeu.</em></p>

> [!TIP]
> Active et consulte le système dans `F10` → `JUSTICE`. Les détails d'architecture sont dans la [documentation développeur](docs/documentation-developpeur.md), et les scénarios de contrôle en jeu dans la [validation manuelle Justice](docs/validation-justice-manuelle.md).

#### Du mandat à l'identification

<p align="center">
  <img src="Github%20images/justice-mandat-actif.png" alt="Notification Justice indiquant une poursuite perdue tandis que le mandat reste actif" width="100%">
</p>

<table>
  <tr>
    <td width="50%" align="center">
      <img src="Github%20images/justice-zone-recherche.png" alt="Carte de Los Santos montrant la zone locale de recherche liée au mandat Justice" width="100%"><br>
      <sub>Le mandat crée une zone de recherche locale, bornée dans le temps et dans l'espace.</sub>
    </td>
    <td width="50%" align="center">
      <img src="Github%20images/justice-identification-police.png" alt="Joueur identifié par la police dans la zone de recherche avec un niveau de recherche de quatre étoiles" width="100%"><br>
      <sub>Un observateur crédible peut identifier progressivement le joueur et réactiver le niveau mémorisé.</sub>
    </td>
  </tr>
</table>

**Advanced Justice** is an optional story-mode legal system. It is disabled by default and remembers each protagonist's choice. The `Justice du héros joué` row in the dedicated `F10` category always enables or disables only the protagonist currently being played and names that protagonist in its value.

Justice keeps three independent character profiles: **Michael**, **Franklin**, and **Trevor** each retain their own enable/disable choice, active case, criminal record, recidivism, debt, and custody state. The `Personnage` row in `F10` cycles between files with Left/Right and explicitly marks the selection as `JOUÉ` or `CONSULTATION`; it does not redirect the activation toggle. `Payer la dette` can debit only when the selected file is the currently played canonical character and now uses the same double-confirmation safety as the other destructive actions. `Réinitialiser ce personnage` names the captured target profile and explicitly covers its record, case, recidivism, debt, and custody data. A detained active hero can be reset only through a durable release-and-restore transaction; an inactive profile or a conflicting release, payment, backup-repair, profile-switch, or reset transaction is never overwritten. If either copy of the reset precommit fails, the intent remains pending and no inventory, custody, or profile effect is applied until both the primary XML and `.bak` carry it.

Its central rule is evidence: an illegal act starts as a provisional incident and is discarded silently unless a credible victim, civilian witness, police observer, or correlated GTA report confirms that specific act. Losing the only witness before the report means no Justice charge. A wanted-level change by itself never invents a crime.

Justice also includes a persistent police-recognition layer. After a genuine escape in which GTA naturally loses the wanted level, the module records the last usable vehicle identity (model, normalized plate, and primary/secondary paint), the protagonist's outfit and visible appearance, and a **local warrant** centered on the last known position. The local warrant is a temporary search zone, not a new criminal charge: its radius and lifetime increase with the remembered wanted level. A police officer or civilian witness with a credible view must progressively identify the player inside that zone whenever the current wanted level is below the warrant's remembered floor, including while GTA still shows fewer non-zero stars. Civilian reports include an additional delay; the system does not instantly reacquire the player merely for entering the radius. Each observer's progress is tied to its handle, model, and native memory address, so a handle recycled by GTA starts with zero exposure and no inherited civilian report timer. If neither the stored nor current observer exposes a native address, only the exact same live `Ped` wrapper may retain that progress. Wanted reads fail closed to the last reliable level: an unavailable getter never fabricates a loss of stars or opens recognition on the assumption that the player is at zero.

Recognition remains identity-sensitive. The same model, plate, and paint can identify a reported vehicle; repainting that same plated vehicle neutralizes its visual report, while changing to another plate no longer matches it. A vehicle without a usable plate requires both the same model and paint. Reported outfits use the ped model, clothing components, textures, palettes, and props; changing clothes, visible appearance, or wearing a face mask progressively reduces recognition risk but does not grant unconditional immunity. Body recognition is fail-closed: without valid persisted appearance evidence and a valid current appearance signature, the player body contributes no exposure and only independently proven vehicle evidence may still identify them. The compact top-right HUD uses the plate, outfit, and local-warrant icons to show active reports and the zone timer.

The bounded queues protect the most important facts first: homicide and other serious victim incidents cannot be evicted by minor pending events. Witness collection reserves priority for the victim of a possible homicide, then living police observers, then other living credible witnesses, so a crowd or nearby corpses cannot hide decisive evidence.

Persistent GTA damage flags are edge-triggered through a bounded victim/attacker baseline, so an old hit can never be redated as a new offence. A sustained melee keeps the bounded victim scan open until combat really ends, allowing a delayed knife death to replace the earlier assault even if GTA's recent-hit timer native is temporarily unavailable. Entity identity also includes its native memory address, preventing a recycled handle from reviving a dead witness or an earlier victim. During missions, cinematics, loading, character changes, and custody, only scalar latches are synchronized; world scans resume through one clean priming pass afterward.

Pending incidents are resolved in two phases: the mod first collects and qualifies every result in reusable bounded buffers, then resolves conflicts before mutating the active case or displaying anything. A correlated violent offence therefore replaces the provisional dangerous-shooting entry without modifying the incident list while it is being traversed.

Confirming an ordinary crime never writes, raises, or maintains the player's wanted level. GTA remains the sole authority for stars initially produced by crimes; Justice only observes those changes as possible evidence and preserves its own case or warrant. The two explicit follow-up mutations are a confirmed prison escape, which applies a three-star minimum, and a later identity match from persisted recognition evidence, which can restore only the wanted minimum remembered from that earlier pursuit.

Once an incident is confirmed, the offence, warrant, or escape notice uses GTA's native top banner instead of a permanent Justice window. Only the detention belonging to the protagonist actually being played keeps a discreet single-line status at the top-left, such as `BOLINGBROKE · 08:18`; switching to either other hero hides that line and exposes the new hero's own independent Justice profile. The complete detail remains available in `F10`. Confirmed charges build an active case and may create a warrant. Prior convictions remain in a separate record and progressively increase sanctions, while the recidivism index slowly decreases after long periods of clean free-roam gameplay.

Every charge retained by the Justice state remains directly consultable from the `Justice avancée` category. Its 13-row page includes the character selector, voluntary payment, and per-character reset alongside the existing case information. `Délits du dossier` opens every retained active-case row, while `Casier judiciaire` expands every retained row stored inside the last 20 convictions, newest conviction first. To keep XML and runtime work bounded, an extreme active case above 512 rows consolidates its oldest compatible facts into an explicit `Infractions consolidées · xN` row: the original per-fact labels and circumstances are no longer claimed to be available, but their represented count and saturated sanction totals remain visible and the header counter still includes them. The Obsidian detail panel shows the selected row, its severity, fine, detention time, proven circumstances when retained, conviction date, and case totals. Arrow keys and their numeric-keypad aliases navigate line by line; `PageUp`, `PageDown`, `Home`, and `End` handle long lists; `Escape`, `Backspace`, or `NumPad0` returns to the Justice page without changing judicial state.

Possible outcomes are:

- a fine with immediate release for the lightest cases;
- detention at Mission Row for short sentences;
- detention in Bolingbroke for serious cases, with a strict maximum of 10 gameplay minutes for the judicial base sentence; guard-caused custody deaths may add full one-minute extensions beyond that base cap;
- a persistent warrant if the player escapes the police without being captured.

Fines have no gameplay balance ceiling: confirmed charges continue accumulating their full dollar value. Arithmetic and XML validation use a purely technical saturation limit of **$1,000,000,000,000** (`10^12`) to prevent overflow or corrupt data; this is not a reachable gameplay cap intended to reduce a sentence.

The sentence scale is one third of the former scale. Non-zero base sentences are now 20 seconds for vehicle destruction; 30 seconds for simple assault or hit-and-run; 40 seconds for evading police, accessory to assaulting an officer, or carjacking; 60 seconds for resisting arrest; 80 seconds for aggravated assault; 120 seconds for assaulting an officer; 140 seconds for accessory to murdering an officer; 160 seconds for manslaughter; 240 seconds for murdering a civilian; 300 seconds for escape; and 360 seconds for murdering an officer. Modifiers and recidivism use a five-second quantum, and the accumulated judicial base sentence always saturates at 600 seconds. Custody guard penalties are stored and served separately.

Mission Row is used below 300 seconds and Bolingbroke from 300 through 600 seconds. When a proven unpaid fine must be converted instead of debited, the rate is **$150 per second**, rounded to five seconds, with a minimum addition of 10 seconds and a maximum addition of 100 seconds. The site and global sentence caps still apply after conversion.

During detention, the inventory is safely snapshotted before confiscation and restored to the same protagonist only after an exact verification of weapons, ammunition, components, tints, clips, and selected weapon. Once confiscation is verified, the player may fight and defend themselves with bare hands; weapon selection, reload, and the weapon wheel remain blocked. If confiscation cannot be proved, the stricter non-destructive control lock stays active. Inmate-on-inmate and player-on-inmate fights remain ordinary GTA events and are ignored by Justice guards. Damaging an owned custody guard starts a local retaliation episode: the current wanted level is raised only to a minimum of two stars, never lowered, ambient police remain suppressed outside the prison, and the living owned guards explicitly fight the player without a global relationship change or per-frame task spam. The response ends on death, legal release, or escape. No disciplinary charge, forced taser sequence, or playable invincibility window is introduced. An incomplete restoration remains persisted and recoverable; script shutdown merges back the snapshot without calling `RemoveAll`, so an abort cannot destroy a partially restored loadout. The DLC-weapon enumeration uses one reusable 312-byte unmanaged structure required by `GET_DLC_WEAPON_DATA`; if allocation or native decoding fails, the inventory is left untouched and only weapon controls are locked.

Capture and payment are also tied to the proven canonical slot of the active Michael, Franklin, or Trevor profile. A custom Iron Man or other transformed ped may reuse the last canonical slot observed in the same session, including after an initial capture death or a custody death, but only when the persisted death latch, active profile, and last canonical identity all prove the same hero. If that proven custom respawn still exposes no cash slot, the fine is converted to detention without touching any protagonist's bank balance, and the transfer continues instead of waiting forever. An unknown identity can never adopt another profile merely because one protagonist becomes available after death. In that case the capture waits safely or returns to a warrant, without disarming or charging another protagonist. Cash writes persist one of three explicit outcomes: succeeded, rejected, or unknown. A rejected write converts the fine to detention; only a genuinely ambiguous write uses the bounded at-most-once reconciliation policy. Fine debits, voluntary payments, and escape confiscation use persisted, resumable intents so a crash cannot debit twice or remove weapons before the recovery state exists. Any fine paid voluntarily before capture is subtracted from the conviction balance during the capture precommit; a partial or complete payment can therefore never make the otherwise valid judgment XML fail.

When a proven police pursuit death creates its exact `PoliceCapture` DeathFront, Justice fades out immediately while the ped is still dead and keeps that mask across GTA's respawn. The runtime verifies the real faded/fading state on every pending tick and reissues the fade if GTA reopens the screen. The first compatible living ped is held at the sentence-appropriate custody site before the primary/backup WAL confirmation finishes; this physical holding does not judge, debit, confiscate, clear the pending front, or reveal a partially admitted prisoner. A residual WASTED/BUSTED mission flag can advance only this exact durable capture, for the same identity already contained in its holding site. If its initial fine intent is still pending when that flag appears, the early controller only re-establishes the exact capture proof; the normal custody controller then resumes the persisted at-most-once financial transaction before transfer. Once both persistence copies confirm the front, the normal capture continues exactly once. Before the single final fade-in, the runtime durably clears plate, outfit/appearance, and local-warrant evidence, cancels the announced wanted loss, observes zero wanted continuously, confirms police/dispatch suppression, makes the player mobile and mortal, enters stable custody, and starts the sentence clock in the same tick. A real protagonist switch releases the screen without ever moving or charging the incoming hero. The same durable recognition reset applies when a fully paid arrest requires no cell; that protected release keeps its durable latches until GTA explicitly reports that the residual BUSTED arrest front is false, so it cannot recreate the cleared case on the next tick.

Time pauses during loading, missions, cinematics, death, pause, and the character-change transition itself. Once another protagonist is playable, a stable inactive profile in the `Incarcerated` phase continues serving its total remaining time in gameplay without showing its HUD or applying its prison scene, police suppression, inventory, or controls to the current hero. During each uninterrupted active-gameplay interval, every profile keeps its own millisecond remainder; there is no UTC or offline catch-up. If an off-screen sentence reaches zero, release and inventory restoration wait until that exact protagonist is played again. Any custody death returns the same protagonist to the correct Mission Row or Bolingbroke cell. If, and only if, an owned guard kills the player during an active guard-retaliation episode, the crash-safe DeathFront adds exactly 60 seconds once; another inmate, a third party, suicide, or an old damage trace adds nothing. Guard extensions are served before the base sentence, survive escape/recapture, and are cleared by legal release, amnesty, or profile reset. The former prison activities, cyan cylinders, `E` interaction, scenario tasks, cooldowns, and sentence reductions have been removed. Bolingbroke's authorized volume follows a fixed eight-point perimeter around the complete prison enclosure rather than its central yard or one oversized rectangle; its outside corners no longer extend custody beyond the walls. Staying outside that perimeter for six continuous seconds counts as one escape, keeps the remaining debt and total time, confiscates the stored inventory, and applies a minimum of three wanted stars.

Legal release durably precommits both the release and one wanted-clear attempt in the primary XML and its backup before touching GTA. The attempt is deliberately **at most once**: after the hero regains control, a rejected or ambiguous result is logged but never retried later, because a late retry could erase stars earned by a new crime. Reloading an unacknowledged release therefore finishes only the XML acknowledgement, inventory, and location stages that remain safe to repeat.

Prison guards and inmates are spawned with GTA's non-temporary events enabled and neutral initial relationship groups. The scene controller ignores inmate fights, but a proven hit on an owned guard activates only the bounded local guard response described above. It yields to living peds while they are fighting, fleeing, stunned, or ragdolled, then waits for at least ten seconds of calm before any bounded navmesh return to a post or allowed area. A dead prison ped keeps its scene slot until the detention scene is torn down; it is not immediately replaced in front of the player. The player is explicitly mortal throughout playable custody and after every exit; temporary teleport protection is released and verified as false before the screen is restored.

Justice state remains a bounded XML 2.0 file. Its 16 MiB ceiling is sized for all three profiles at the documented limits (20 visible convictions and up to 512 consolidated charge summaries each), while still rejecting oversized or abusive files.

This balance change carries `sentencePolicyVersion="2"`. On the first load of any older Justice save, all former judicial data for Michael, Franklin, and Trevor is intentionally discarded: cases, charges, sentences, fines, warrants, records, recidivism, convictions, custody, discipline, and activity state start fresh. Only each protagonist's Advanced Justice ON/OFF preference is retained. Recovery tokens needed to restore an inventory, controls, transient player state, or police suppression are kept separately until their owning protagonist can be restored safely; no legacy cash, weapon-removal, or wanted effect is replayed. Once the clean primary and backup are confirmed, the version marker prevents the reset from running again.

The escape wanted mutation follows the primary-plus-backup precommit rule. If GTA does not confirm at least three stars, the attempt is durably rearmed and retried only for the same proven protagonist until the minimum is observed. Once confirmed, the operation is removed and is not replayed after GTA later lowers the wanted level naturally. The strict at-most-once rule remains reserved for the legitimate-release wanted clear.

The police-ignore and dispatch-suppression tokens used inside custody are treated as global recovery state and are restored even when the loaded Justice profile is disabled. Justice defers its profile handoff or an inactive-profile reset until both GTA natives have been restored and that restoration has been persisted; GTA's own character change is never described as reversible. Tokens found in an inactive profile after a crash are also merged into this recovery, cleared from that profile, and committed after the global restoration succeeds. A reset of the actually detained hero must first restore those natives and the inventory under its own durable reset transaction.

Because F10 does not pause the game, every Justice confirmation captures its target slot on the first input and revalidates it on the second. A canonical character switch is rejected even if it occurs before the next Justice tick. An already proven Iron Man/custom ped remains usable only while the exact same live ped handle and available model still match the captured identity. Changing the consulted file also cannot redirect a prepared reset.

The activation row and its `JOUÉ` marker use the same proven-profile gate as runtime mutations. During a GTA character transition or an uncommitted profile switch they show `IDENTIFICATION / CHANGEMENT EN COURS` and refuse activation, deactivation, or payment. A previously proven Iron Man/custom ped remains usable because its active canonical profile is preserved; a genuinely unknown or different hero is not guessed.

Cash payment remains stricter than the rest of the custom-ped runtime: the canonical GTA slot must be visible at the moment of payment. Under an Iron Man/custom model, F10 displays `indisponible` instead of promising a payment that the transaction will refuse; returning briefly to the canonical hero makes the action available again.

DonJ bodyguards, Cartel teams, and the high-security escort keep defending the player during a police pursuit. A causal token is created only after a real offensive order succeeds, and a proven attack is attributed only when it is directly tied to the current defence, nearby, recent, and witnessed; autonomous or distant actions are ignored. At confirmed capture, only a fresh, nearby police combat target is neutralized with a non-destructive hold task. Justice never calls `CLEAR_PED_TASKS`, so it does not accidentally erase driving or escort work. No ally or convoy entity is dismissed, deleted, or withdrawn: their handles and active service are preserved, while bodyguard, Cartel convoy, and high-security convoy AI are suspended for the duration of detention and resume afterward.

The Justice state is independent from scene saves and stores the three character profiles atomically in:

```text
DonJEnemySpawnerSaves\_justice_state.xml
```

Police-recognition evidence is deliberately stored outside both scene saves and the judicial XML, with independent Michael, Franklin, and Trevor records. Its normal writable location is:

```text
Scripts\DonJJusticeRecognition\JusticeRecognition.xml
```

Its recoverable backup is `JusticeRecognition.xml.bak`, and runtime diagnostics are written beside it as `JusticeRecognition.log`. The main store also examines `JusticeRecognition.xml.tmp`, `.bak.tmp`, `.rollback`, and `.bak.rollback`; the newest valid variant is republished as an identical primary/backup pair. If all six are unreadable, every existing variant is moved into `JusticeRecognition.xml.corrupt-quarantine` and a fresh redundant pair is created. The same resolved directory contains the durable critical-command journal `JusticeRecognition.critical-intents.xml` and its identical `.bak`. Capture and explicit profile/global-clear commands return durable success only after both journal copies contain their stable command ID; amnesty and profile-reset paths do not make their Justice transaction terminal until that registration succeeds. Pending command IDs are loaded and replayed after a restart. A command is acknowledged and removed only after both `JusticeRecognition.xml` and its backup contain the requested clear, then both journal copies are republished without that command; any failed stage leaves it pending for an idempotent retry. A targeted capture or clear whose canonical profile is already absent is treated as idempotently satisfied only after that absence has itself been republished to both main-store copies, then its journal command is acknowledged without creating an empty profile. If all journal variants are unreadable, the existing primary, backup, `.tmp`, `.bak.tmp`, `.rollback`, and `.bak.rollback` files are moved out of every load path into `JusticeRecognition.critical-intents.xml.corrupt-quarantine`. A fresh identical primary/backup pair is created, then the current Justice intent is registered again before amnesty or reset may become terminal. If the preferred runtime data directory cannot be created, resolution falls back to `%LOCALAPPDATA%\DonJEnemySpawner\JusticeRecognition`, then to a `DonJJusticeRecognition` directory beside the assembly only as a final compatibility fallback. The official package still requires all three HUD assets at these exact installed paths:

```text
Scripts\Assets\Justice\immatriculation.png
Scripts\Assets\Justice\tenue.png
Scripts\Assets\Justice\mandat.png
```

The package and deployment validators reject an absent, unexpected, or hash-mismatched Justice asset. The runtime keeps a text fallback only as crash protection; that fallback does not make an incomplete installation valid.

After the one-time upgrade reset, the ON/OFF row is always a reversible preference, not an amnesty. OFF pauses new Justice detection and ON resumes it without deleting the new case, sentence, fine, warrant, record, or recidivism and without clearing GTA's wanted level. The action may be temporarily unavailable only while a critical persistence or recovery boundary is still completing.

The same toggle pauses the recognition layer without erasing plates, outfits, appearance evidence, or the local warrant. OFF removes the recognition blip and clears only in-memory observer progress; ON resumes the current protagonist's persisted recognition state. It never clears GTA's wanted level. Recognition data is removed only by an explicit profile/global reset or after a confirmed capture according to the Justice integration.

`tools\repair-justice-state.ps1` remains only as a historical offline tool for one former v1 incident. It is not part of the current ON/OFF flow or the sentence-policy upgrade and must not be used to bypass the automatic, recovery-aware reset.

---

### Weapon Workshop

The mod includes a weapon workshop to customize NPC equipment.

Available options depending on the weapon:

- extended magazine;
- suppressor;
- flashlight;
- grip;
- scope;
- compensator / muzzle;
- improved barrel;
- MK2 ammunition;
- tint;
- quick presets;
- apply to already placed NPCs.

Components incompatible with a weapon are cleanly ignored.

---

### Vehicle Placement

You can place vehicles in the world with preview and rotation.

Available categories:

- Sport / Super;
- Sedans / Coupes;
- SUV / 4x4;
- Motorcycles;
- Police / Emergency;
- Military;
- Utility / Vans;
- Trucks;
- Planes / Helicopters;
- Boats;
- All vehicles.

---

### Object Placement

The mod also lets you place objects to build props, cover, checkpoints, or combat zones.

Available categories:

- Security;
- Cover / Combat;
- Cash / loot;
- Tactical gear;
- Health / survival;
- Office / IT;
- Workshop / tools;
- Furniture;
- Crates / Storage;
- Decoration;
- Lights;
- Exterior;
- Misc.

Included object examples:

- cones;
- barriers;
- concrete blocks;
- dumpsters;
- pallets;
- cash stacks;
- money bags;
- cash briefcases;
- cash crates;
- cash trolleys;
- chairs;
- tables;
- crates;
- lamps;
- tents;
- bags;
- fire extinguishers;
- decorative objects.

### Collectible Cash

<p align="center">
  <img src="images-readme/argentramassable.png" alt="Example of collectible cash with a 250,000 dollar cash crate" width="100%">
</p>

The **Cash / loot** category lets you place cash in your scenes: stacks, bills, envelopes, money bags, heist bags, briefcases, crates, gold safe, or cash trolley.

These objects give real value to the zones or missions you create. You can place a small stack in an office, a briefcase in a safehouse, a heist bag after a fight, or a big cash crate at the back of a bunker to reward exploration.

In game, the player approaches the object and presses `E` to collect it. The amount is added to the character's single-player cash, then the prop disappears and is no longer saved if you save the scene after taking it.

Amounts vary by object:

| Loot type | Indicative amount |
|---|---:|
| Single bill | `100$` |
| One-dollar bill | `1$` |
| Cash stack or pile | `10 000$` |
| Cash envelope | `2 500$` |
| Cash package | `5 000$` |
| Money bag, heist bag, or cash briefcase | `50 000$` |
| Cash trolley | `200 000$` |
| Cash crate or gold safe | `250 000$` |

Other objects remain decorative or useful depending on their type. Ammo packs, health kits, and armor objects can also become interactive when their model matches.

---

### Interiors and Portals

The mod includes a system for entrances and exits to interiors.

> [!WARNING]
> **Experimental feature.** Interior entrances/exits can still cause bugs depending on the selected interior, loaded IPLs, or game context.
> Placing guards or NPCs in some interiors can also cause unexpected behavior, especially around navigation, combat, following, spawning, or cleanup. The feature works, but this part is still being refined.

You can place:

- an **entrance** in the exterior world;
- an **exit** in the active interior;
- markers that allow travel between the two.

The catalog contains more than 150 interior locations, including:

- bunkers;
- facilities;
- online apartments;
- garages;
- houses;
- CEO offices;
- businesses;
- Diamond Casino & Resort;
- mission locations;
- special locations with IPLs.

The mod loads the required IPLs when an interior needs them.

---

### Precise Camera Placement

Camera placement lets you precisely place an NPC, vehicle, object, or portal.

During placement:

- the player is frozen;
- the player is protected;
- a free camera is enabled;
- a transparent preview of the entity is displayed;
- rotation can be adjusted before validation.

This is the recommended mode for creating clean scenes.

---

### Direct Placement

Direct placement lets you quickly place the selected element in front of the player.

The distance can be configured from `25 m` to `2500 m`, in `25 m` steps.

---

### XML Save and Load

The mod can save and reload your setups.

XML saves contain:

- NPCs;
- custom models;
- weapons;
- weapon attachments;
- behaviors;
- health;
- armor;
- vehicles;
- objects;
- interior entrances/exits;
- automatic respawn options.

The default save name is:

```text
maison.xml
```

You can change it from the mod menu.

---

## Installation

<p align="center">
  <img src="Images/trevor.png" alt="DonJ Custom NPC Placer - mod installation image" width="100%">
</p>

> [!TIP]
> For the simplest and safest setup, download the verified **`DonJCustomNpcPlacer-game-ready`** artifact produced by the latest successful `Safety` workflow triggered by a push to `main`.
> The package is generated only after the Release build, tests, and binary API validation pass. Its `manifest.json` records the exact commit, assembly version, ScriptHookVDotNet API identity and ABI-contract fingerprint, Justice schema, sizes, and SHA-256 hashes.

### Before You Start

This mod does not run by itself. GTA V Enhanced must already have the files that allow mods to work.

You must have:

- **GTA V Enhanced** on Windows;
- **Microsoft .NET Framework 4.8**;
- **ScriptHookV**;
- **NIBScriptHookVDotNet** for GTA V Enhanced;
- a **Scripts** folder in the game folder.

The current local installation has been validated with this exact stack:

| Component | Installed version / loader |
|---|---|
| GTA V Enhanced Steam | `1.0.1158.13` |
| ScriptHookV | `3889.0.1158.13` |
| Enhanced ASI loader | `xinput1_4.dll` (`1.0.0.2`) |
| NIBScriptHookVDotNet API v2 | `2.11.6` |
| Optional NIBScriptHookVDotNet HUD provider | `NIBScriptHookVDotNet3.dll`, major `3` or newer when installed |

The game folder is the folder where this file is located:

```text
GTA5_Enhanced.exe
```

Steam example:

```text
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced
```

If the `Scripts` folder does not exist, create it yourself in the game folder.

---

### 1. Install the Required Mod Files

In the main GTA V Enhanced folder, in the same location as `GTA5_Enhanced.exe`, you must have these files:

```text
ScriptHookV.dll
xinput1_4.dll
NIBScriptHookVDotNet.asi
NIBScriptHookVDotNet2.dll
```

`NIBScriptHookVDotNet3.dll` major 3 or newer is optional. When installed, it can
provide the Justice `GTA.UI.CustomSprite` renderer; without it, the supported
native P/T/M HUD fallback is used.

Useful links:

| Required file | Where to download it | Where to put it |
|---|---|---|
| `ScriptHookV.dll` `3889.0.1158.13` and Enhanced loader `xinput1_4.dll` | [Official Script Hook V - Alexander Blade](https://www.dev-c.com/gtav/scripthookv/) | In the main game folder |
| `NIBScriptHookVDotNet.asi`, `NIBScriptHookVDotNet2.dll` `2.11.6`, and optionally `NIBScriptHookVDotNet3.dll` major 3 or newer | [NIBMods Menu and .Net plugins - GTA Legacy and Enhanced - JulioNIB](https://www.patreon.com/posts/nibmods-menu-and-22783974) | In the main game folder |

For NIBScriptHookVDotNet, make sure you choose the **GTA Enhanced** version when it is offered.

The assembly is still compiled against and runs all gameplay through API v2. The
v3 DLL is not referenced by the project and is never bundled with this mod. If it
is installed, packaging and deployment validate its reflected
`GTA.UI.CustomSprite` contract strictly. If it is absent, packaging and deployment
remain valid and the native P/T/M HUD fallback is used.

Once this part is done, your main game folder should look like this:

```text
Grand Theft Auto V Enhanced
  GTA5_Enhanced.exe
  ScriptHookV.dll
  xinput1_4.dll
  NIBScriptHookVDotNet.asi
  NIBScriptHookVDotNet2.dll
  NIBScriptHookVDotNet3.dll  (optional HUD provider)
  Scripts
```

---

### 2. Install the DonJ Mod

The simplest method is to use the package generated and verified by GitHub Actions. Binaries stored manually in a source checkout must not be used as releases.

1. Open the repository's **Actions** page on GitHub.
2. Open the latest successful **Safety** run whose branch is `main` and whose
   event is `push`. Pull-request and secondary-branch runs are validation runs,
   not releases.
3. In **Artifacts**, download:

```text
DonJCustomNpcPlacer-game-ready
```

4. Extract the downloaded archive.
5. Open `manifest.json` and confirm that `manifestVersion` is `2`, `sourceDirty` is `false`,
   `scriptApi.major` is `2`, `scriptApi.abiContract.sha256` contains
   64 hexadecimal characters, and `commit` matches the latest commit shown on
   the `main` branch. `files.justiceAssets.{immatriculation,outfit,warrant}` must
   name exactly the three Justice PNG paths and provide each size and SHA-256.
   `hudRenderer` must set `optional=true`, `fallback=native`, `minimumMajor=3`,
   `typeName=GTA.UI.CustomSprite`, and `contractVersion=1`. `available` says
   whether a compatible provider was validated and, when true, its actual
   assembly name and version are recorded.
6. The verified package contains:

```text
DonJCustomNpcPlacer.ENdll
DonJCustomNpcPlacer.pdb
Assets\
  Justice\
    immatriculation.png
    tenue.png
    mandat.png
INSTALLATION_SIMPLE.txt
manifest.json
```

7. Copy `DonJCustomNpcPlacer.ENdll`, the complete `Assets\Justice` folder, and, optionally, `DonJCustomNpcPlacer.pdb` into:

```text
Grand Theft Auto V Enhanced\Scripts
```

Steam example:

```text
C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\Scripts
```

8. Copy `manifest.json` into that same `Scripts` folder and rename the copy to:

```text
DonJCustomNpcPlacer.manifest.json
```

The stable name lets the runtime diagnostic compare the loaded `.ENdll` with the exact hash and commit published by CI.

> [!IMPORTANT]
> Do not copy the package folder or `INSTALLATION_SIMPLE.txt` into `Scripts`.
> Copy the `.ENdll`, the complete `Assets\Justice` tree, the renamed manifest, and the optional matching `.pdb` from the same verified package. The three Justice PNG files are mandatory even though the PDB remains optional.

The package's `INSTALLATION_SIMPLE.txt` file also contains these steps in a simple text version. The repository keeps [the guide template](Mode-pour-jeu-ici/INSTALLATION_SIMPLE.txt), but no release binary is maintained manually there.

---

### 3. Check That the Files Are in the Right Place

In the main game folder, you must have:

```text
Grand Theft Auto V Enhanced\ScriptHookV.dll
Grand Theft Auto V Enhanced\xinput1_4.dll
Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.asi
Grand Theft Auto V Enhanced\NIBScriptHookVDotNet2.dll
Grand Theft Auto V Enhanced\NIBScriptHookVDotNet3.dll  (optional HUD provider)
```

In the `Scripts` folder, you must have:

```text
Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.ENdll
Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.manifest.json
Grand Theft Auto V Enhanced\Scripts\Assets\Justice\immatriculation.png
Grand Theft Auto V Enhanced\Scripts\Assets\Justice\tenue.png
Grand Theft Auto V Enhanced\Scripts\Assets\Justice\mandat.png
```

The manifest is part of the verified installation. It records the exact commit,
the SHA-256 of the loaded mod, and the sizes and SHA-256 hashes of exactly these
three required Justice assets. Its `hudRenderer` contract declares an optional
native fallback plus the fixed `GTA.UI.CustomSprite` contract. When a v3 provider
is present, its real assembly identity and version are recorded after strict
validation; the provider itself is never packaged.

The following matching file is optional, but you can leave it:

```text
Grand Theft Auto V Enhanced\Scripts\DonJCustomNpcPlacer.pdb
```

The `.pdb` is not required to play. It mainly helps provide more readable logs if there is a problem.

---

### 4. Launch the Mod in Game

1. Launch GTA V Enhanced.
2. Go to **story mode**.
3. Once in game, press:

```text
F10
```

The mod menu should open.

To use the phone calls:

1. Open the player's phone.
2. Display the mod contacts.
3. Press `C` to call the allied Cartel.
4. Press `R` to call a hostile Ballas wave and create activity around the player.
5. Press `L` to call or dismiss the high-security escort.

To use the high-security escort VIP route:

1. Call the escort with `L` from the phone.
2. Get in the back of the limousine with `F`.
3. Place a waypoint on the map.
4. Press `L` inside the limousine to launch the convoy toward the waypoint.

---

### If the Menu Does Not Open

Check in this order:

1. You are in **story mode**, not GTA Online.
2. `DonJCustomNpcPlacer.ENdll` is in the `Scripts` folder.
3. The folder is named exactly `Scripts`.
4. `ScriptHookV.dll` is in the main game folder.
5. The Enhanced loader `xinput1_4.dll` is in the main game folder.
6. `NIBScriptHookVDotNet.asi` is in the main game folder.
7. `NIBScriptHookVDotNet2.dll` is in the main game folder.
8. If `NIBScriptHookVDotNet3.dll` is installed, it is major 3 or newer and exposes `GTA.UI.CustomSprite`; otherwise the native HUD fallback is expected.
9. All three PNG files are present under `Scripts\Assets\Justice`.
10. No old mod file is still present in `Scripts`.

Old files to delete if they exist:

```text
Scripts\DonJCustomNpcPlacer.dll
Scripts\DonJEnemySpawner.dll
Scripts\DonJEnemySpawner.ENdll
Scripts\DonJEnemySpawner.pdb
```

---

### Updating the Mod

1. Close GTA V Enhanced and its loaders. The deployer refuses to modify `Scripts`
   while `GTA5_Enhanced`, `GTA5`, or `PlayGTAV` is running and never stops them itself.
2. Download and extract `DonJCustomNpcPlacer-game-ready` from the latest
   successful `Safety` run triggered by a `push` to `main`, then confirm its
   manifest commit matches the current `main` commit.
3. Before touching the installed files, verify that the new
   `DonJCustomNpcPlacer.ENdll` SHA-256 matches `files.binary.sha256` in the
   package's `manifest.json`.
4. Copy the verified `DonJCustomNpcPlacer.ENdll` into `Scripts`, replacing the
   file with the same name. Do not delete the installed `.ENdll` before the new
   package has been extracted and validated.
5. Copy the package's complete `Assets\Justice` directory into `Scripts`,
   replacing all three Justice PNG files as one verified set.
6. Copy the matching `DonJCustomNpcPlacer.pdb` if you want debug symbols.
7. Copy the package's `manifest.json` into `Scripts` and replace the installed
   `DonJCustomNpcPlacer.manifest.json` while keeping that stable installed name.
8. Recalculate the installed `.ENdll` and Justice-asset SHA-256 hashes and compare
   them with the manifest again. Only after this succeeds, remove the four old aliases listed above,
   including `DonJCustomNpcPlacer.dll` and `DonJEnemySpawner.*`.
9. Restart the game in story mode and press `F10`.

When updating from a source checkout, prefer `tools\deploy-game-ready.ps1`; it
validates every referenced NIB member against the API installed with GTA before
it replaces the ENdll, PDB, manifest, and the three Justice assets transactionally.
It also checks that the game/script-host processes are stopped before the first
directory creation, staging file, backup, replacement, or alias cleanup.

---

### Uninstalling

1. Close the game.
2. Delete these files from `Scripts`:

```text
Scripts\DonJCustomNpcPlacer.ENdll
Scripts\DonJCustomNpcPlacer.pdb
Scripts\DonJCustomNpcPlacer.manifest.json
Scripts\Assets\Justice\immatriculation.png
Scripts\Assets\Justice\tenue.png
Scripts\Assets\Justice\mandat.png
```

3. Scene/Justice saves and `Scripts\DonJJusticeRecognition` can be deleted
   separately if you do not want to keep them.

---

## Usage

### Open the Menu

In game, press:

```text
F10
```

The custom **DonJ Obsidian console** opens without covering the whole game world. Its responsive layout stays inside the GTA safe zone and is split into three areas:

- a left rail with the DonJ monogram and the seven categories;
- a central panel containing actions and settings;
- a right panel showing the selected value, contextual help, scene counters, the active save, and notifications.

The seven categories are **NPC**, **Vehicles**, **Objects**, **Interiors**, **Scene**, **Advanced Justice**, and **Tools**. NPC is selected when the console first opens, and each category remembers its last selected row. Placement controls appear at the top of compatible categories; save/load actions are grouped under Scene; Advanced Justice contains the Michael/Franklin/Trevor selector, case and record views, voluntary payment, and protected profile reset; Terminator mode and cleanup actions are grouped under Tools.

The Obsidian-console monogram, icons, frames, and decorative elements are drawn directly by the mod, so that console itself does not require an extra PNG, YTD, OIV, RPF, or Scaleform file. The separate police-recognition HUD is the deliberate exception and requires the three verified files under `Scripts\Assets\Justice`.

---

### Menu Controls

| Key | Action |
|---|---|
| `F10` | Open / close the menu |
| `Up` / `Down` | Navigate |
| `NumPad 8` / `NumPad 2` | Navigate |
| `Left` / `Right` | Change a value |
| `NumPad 4` / `NumPad 6` | Change a value |
| `Enter` | Confirm / open an action; confirm an armed cleanup |
| `NumPad 5` | Confirm / open an action; confirm an armed cleanup |
| `Tab` / `Shift + Tab` | Jump to the next / previous category |
| `PageUp` / `PageDown` | Scroll quickly |
| `Home` / `End` | Go to the start / end |
| `Esc` / `Backspace` / `NumPad 0` | Cancel a cleanup confirmation, close, or go back |
| `T` | Enter a custom model when the selected NPC model is `Custom` |
| `B` | Cycle Terminator vision when Terminator mode is active |

Menu navigation remains keyboard-only; the redesign does not add mouse or controller input.

---

### Phone Contact Controls

| Key / state | Action |
|---|---|
| Player phone open | Shows the `Cartel`, `Ballas`, and `High-security escort` contacts |
| Advanced Justice transfer / detention | Keeps all three contacts visible but temporarily blocks their calls |
| `C` | Call Cartel gunmen |
| `C` with an active Cartel team | Make the active team withdraw |
| `R` | Call a hostile Ballas wave to create activity around the player |
| `L` | Call the high-security escort with limousine and 4 Baller vehicles |
| `L` with an active escort | Make the active escort withdraw |
| `F` near the limousine | Get in the back of the VIP limousine |
| `L` seated in the back of the limousine | Validate the GPS waypoint and launch the convoy toward the destination |

A short anti-spam delay prevents calls from being restarted several times instantly.

---

### Camera Placement Controls

When you launch precise camera placement:

| Key / action | Effect |
|---|---|
| Mouse | Look around |
| `Z` or `W` | Move forward |
| `S` | Move backward |
| `Q` | Move left |
| `D` | Move right |
| `Space` | Move up |
| `Ctrl` | Move down |
| `Shift` | Fast movement |
| `Alt` | Slow movement |
| `A` / `E` | Rotate the placed entity |
| Left click | Place |
| `Enter` | Place |
| `NumPad 5` | Place |
| Right click | Exit placement |
| `Esc` | Exit placement |
| `Backspace` | Exit placement |

---

## Quick Example

### Create a Hostile Checkpoint

1. Press `F10`.
2. Choose `Placement type: NPC`.
3. Open the `NPC` section.
4. Select a category, for example `Security / Police / Military`.
5. Choose a model, for example a SWAT.
6. Choose a weapon.
7. Set the behavior to `Hostile patrol` or `Static / hostile on sight`.
8. Set health, armor, and patrol radius.
9. Start `Precise camera placement`.
10. Place the NPC with `Enter` or left click.
11. Repeat to create several guards.
12. Save with `Save`.

---

### Create a Guarded Base

1. Place cover objects.
2. Place vehicles.
3. Place neutral or allied NPCs.
4. Add patrols.
5. Add an entrance to an interior.
6. Place an exit inside the interior.
7. Save the setup.

---

## Saves

The mod automatically creates a save folder.

The priority folder is usually:

```text
Grand Theft Auto V Enhanced\Scripts\DonJEnemySpawnerSaves
```

If this folder is not writable, the mod can use a fallback folder, for example:

```text
Documents\Rockstar Games\GTA V Enhanced\DonJEnemySpawnerSaves
```

or:

```text
%LOCALAPPDATA%\DonJEnemySpawner\Saves
```

You can also force a custom save folder with the environment variable:

```text
DONJ_ENEMY_SPAWNER_SAVE_DIR
```

---

## In-Game Cleanup

Cleanup actions are available in the **Tools** category. They let you separately remove:

- placed NPCs;
- placed vehicles;
- placed objects;
- interior entrances/exits.

Cleanup is protected against accidental activation. Press `Enter` or `NumPad 5` once to display the styled confirmation, release the key, then press it a second time to execute the selected cleanup. Key repeat cannot validate the confirmation. `Esc`, `Backspace`, `NumPad 0`, or closing the console cancels the pending action without removing anything.

---

## Compatibility

This mod is designed for:

```text
GTA V Enhanced 1.0.1158.13
Windows x64
Story mode / solo
ScriptHookV 3889.0.1158.13 with xinput1_4.dll
NIBScriptHookVDotNet API v2 2.11.6
NIBScriptHookVDotNet3.dll major 3 or newer (optional reflected HUD provider)
.NET Framework 4.8
```

Compatibility tested with other mods:

- **JulioNIB Iron Man**;
- **JulioNIB Superman**.

These tests indicate that **DonJ Custom NPC Placer** can coexist with these mods in story mode when the dependencies are correctly installed. Compatibility can still depend on the versions of GTA V Enhanced, ScriptHookV, NIBScriptHookVDotNet, and the installed JulioNIB mods.

Not guaranteed for:

- GTA Online;
- FiveM;
- RageMP;
- older non-Enhanced versions;
- installations without NIBScriptHookVDotNet2;
- incompatible installed NIBScriptHookVDotNet3 HUD providers (absence uses the native fallback);
- pirated or modified versions of the game.

---

## Build from Source

The project targets:

```text
.NET Framework 4.8
```

Build command:

```powershell
dotnet build GTA5modDEV.sln -c Release
```

Test command:

```powershell
dotnet test GTA5modDEV.sln -c Release
```

Justice-specific coverage is split between:

- `JusticeDomainTests.cs`, for deterministic crimes, evidence, sanctions, recidivism, cases, and state transitions;
- `JusticePlayerProfilePersistenceTests.cs`, for isolated Michael/Franklin/Trevor records, legacy XML loading, stable-custody handoff, inactive sentence clocks, death-only return-to-cell routing, and off-screen release ownership;
- `JusticeUpgradeResetTests.cs`, for the one-time three-profile policy reset, ON/OFF preservation, recovery-token ownership, crash-safe resumption, quarantine, and the guarantee that current-version saves are never reset twice;
- `JusticeRuntimeContractTests.cs`, for bounded runtime cadence, witness/wanted correlation without ordinary-crime wanted writes, crash-safe fine/discard/payment transactions, custody identity, inventory, the absence of former prison activities, native notices/custody-line layout, seven-category navigation, and protected profile resets;
- `JusticeRuntimeEdgeContractTests.cs`, for consumed damage fronts, causal homicide proof, delayed hit-and-run qualification, witness sharing, queue priority, and handle-generation expiry outside custody;
- `JusticeCustodyHardeningTests.cs`, for the 312-byte DLC-weapon buffer, fail-closed inventory capture, exact durable restoration, cash outcomes, death-only return-to-cell respawn, natural prison combat, the six-second Bolingbroke perimeter escape, police suppression, and shutdown safety;
- `JusticeCustodySceneMaintenanceTests.cs`, for natural GTA events, no forced discipline or immediate dead-ped replacement, combat/stun/ragdoll suppression, the ten-second calm window, and bounded navmesh maintenance;
- `JusticeEnginePersistenceRegressionTests.cs`, for two-phase incident resolution, canonical character identity, pinned convictions, policy-reset recovery, and wanted-only repair recovery;
- `JusticeStateRepairTests.cs`, for targeted offline v1 repair, backups, hashes, refusal of unsafe inventory states, and preservation of the record;
- `JusticeUiIntegrationObservabilityTests.cs`, for the native-notification/mini-line HUD contract, safe-zone and record caches, stable log paths, and shadow-copy collection;
- `StubRuntimeBehaviorTests.cs`, for the configurable NIB v2 stub backend that records native calls, wanted, damage, tasks, world state, inventory, and money.

The Obsidian safe-zone value is cached for at least 250 ms and protected by a retry circuit breaker. The flattened criminal-record view is rebuilt only when its ledger revision changes. Runtime logs prefer a stable `Scripts` or LocalAppData location before the .NET shadow-copy directory, and the bug collector also searches the newest legacy log under the LocalAppData assembly cache.

### Safety and Non-Regression Validation

Before each addition or delivery, run the full headless suite:

```powershell
.\tools\run-safety-checks.ps1
```

If Windows blocks PowerShell script execution on your machine, run the same suite with a policy limited to the current process:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-safety-checks.ps1
```

This command restores, builds, and tests in `Release` without touching the live game, creates the canonical game-ready package, installs that package into an isolated temporary GTA tree, and verifies the build/package/deployment SHA-256 chain. It also verifies that old `DonJEnemySpawner.*` files do not reappear.

The generated file is here:

```text
src\DonJEnemySpawner\bin\Release\DonJCustomNpcPlacer.ENdll
```

An ordinary `Release` build never modifies the GTA installation. Deployment is deliberately opt-in.

To build and deliberately deploy to a validated GTA folder:

```powershell
dotnet build GTA5modDEV.sln -c Release `
  /p:DeployToGta=true `
  /p:GtaRoot="D:\Jeux\Grand Theft Auto V Enhanced"
```

The deployment first refuses to continue while `GTA5_Enhanced`, `GTA5`, or
`PlayGTAV` is active, without terminating any process. It then creates and verifies
a package, stages the new files inside the destination volume, checks their hashes,
and only then replaces and re-reads the active `.ENdll`, PDB,
`DonJCustomNpcPlacer.manifest.json`, and the three files under `Assets\Justice`.
Legacy aliases are moved only after that complete verified set exists. If an alias
is locked or any earlier validation/replacement fails, moved aliases, the previous
active files, and the previous Justice assets are rolled back in reverse order
instead of being deleted first.

To create a local package without deploying it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package-game-ready.ps1 `
  -Configuration Release `
  -OutputDirectory .\artifacts\game-ready `
  -DependencyDirectory .\tests\DonJEnemySpawner.Tests\bin\Release `
  -Force
```

---

## Troubleshooting

### The Menu Does Not Open with F10

Check that:

- you are in story mode;
- `DonJCustomNpcPlacer.ENdll` is in the `Scripts` folder;
- `NIBScriptHookVDotNet.asi` is installed;
- `NIBScriptHookVDotNet2.dll` is installed;
- if `NIBScriptHookVDotNet3.dll` is installed, it is major 3 or newer and compatible;
- all three files under `Scripts\Assets\Justice` match the manifest;
- `ScriptHookV.dll` is compatible with your game version;
- no old `DonJCustomNpcPlacer.dll` or `DonJEnemySpawner.*` alias is still present.

---

### The Mod Does Not Load

To automatically gather useful logs into the project without launching GTA, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\collect-bug-logs.ps1 -Title "short-bug" -SinceHours 24
```

Reports are created in `bug-reports\YYYYMMDD-HHMMSS-title`. This folder stays local and is ignored by Git to avoid sending personal logs to GitHub.

The runtime logger first tries stable locations under the live `Scripts` folder and LocalAppData. The collector takes `Scripts\DonJJusticeRecognition\JusticeRecognition.log` explicitly and also inspects the .NET LocalAppData assembly shadow-copy cache so that a last legacy `DonJCustomNpcPlacer.log` is not missed after a crash.

Check the following logs:

```text
Grand Theft Auto V Enhanced\NIBScriptHookVDotNet.log
Grand Theft Auto V Enhanced\ScriptHookV.log
Grand Theft Auto V Enhanced\Scripts\*.log
Grand Theft Auto V Enhanced\Scripts\DonJJusticeRecognition\JusticeRecognition.log
```

If you use other mods, also check their possible logs.

---

### A Custom Model Does Not Appear

Check that:

- the add-on model is correctly installed;
- its name is exact;
- the model can be loaded by the game;
- you selected `Custom` in the NPC menu;
- you pressed `T` to enter the model name.

---

### A Save Is Not Created

Check that the `Scripts` folder is writable.

If Windows blocks writing to the game folder, use a custom save folder with:

```text
DONJ_ENEMY_SPAWNER_SAVE_DIR
```

---

## Report a Bug

To report a problem, open a GitHub issue with:

- your GTA V Enhanced version;
- your ScriptHookV version;
- your NIBScriptHookVDotNet v2 and v3 provider versions;
- a precise description of the bug;
- reproduction steps;
- useful log files;
- a screenshot if possible.

Useful logs:

```text
NIBScriptHookVDotNet.log
ScriptHookV.log
Scripts\*.log
Scripts\DonJJusticeRecognition\JusticeRecognition.log
menyooLog.txt
```

---

## Credits

Mod developed by DonJ

C# / .NET Framework 4.8 project for GTA V Enhanced, based on ScriptHookV and NIBScriptHookVDotNet API v2.

---

## License

This project is distributed under a custom **open source, non-commercial license with mandatory attribution**.

You may use the mod for free in single-player mode, read the source code, share the mod for free, modify it, and publish a free modified version, as long as the **DonJ** name remains associated with the original project.

You are not allowed to sell the mod, sell a modified version, remove credits, present yourself as the original creator, or put the mod behind paid access without prior written authorization from DonJ.

See the [`LICENSE`](LICENSE) file for the full terms.
