# Deathwatch - Play as a Space Marine

A mod for **Warhammer 40,000: Rogue Trader** that lets you create and play a Deathwatch Space Marine
in the base campaign.

- A fully converted character creator: choose your **Chapter** (10 of them, each with its own shoulder
  heraldry), **Speciality** (Tactical, Assault, Devastator, Apothecary, Techmarine, Librarian),
  **Deed of Renown** and **Burden of Service**.
- A full-size, fully armoured Astartes in-game: correct scale, Deathwatch power armour with your
  Chapter's pauldron, an Astartes starting arsenal, and a working Librarian psyker.
- Speciality identity in play: every speciality has an innate combat ability, a matched talent kit,
  wears its own named battle-helm with systems that scale off your stats (values shown live in the
  tooltip), and - with the Space Marine voice - speaks its own battle barks.
- Transhuman physiology: failed Strength and Toughness tests are rerolled once, and both
  characteristics can always be increased at level-up.
- An Astartes armoury: wield any two-handed melee weapon in one hand, use plasma and flame weapons
  at marine scale, Kick with your power armour's boots, wear capes (including a Deathwatch cape),
  and - as a Techmarine - take up the Omnissian axe.
- 11 custom portraits and 3 custom voices (Space Marine, Apothecary, Ulfar) in character creation.
- Fully localized: all mod text ships in English, German, French, Spanish, Russian, Chinese
  (Simplified), Japanese and Turkish, with terminology matched to the game's official localization.
- Sensible in the world: the marine is Large in combat but fits corridors, ladders and the command
  throne out of combat; story beats that strip your gear hand an Astartes proper wargear back.
- Also ships one general engine fix that benefits any party: cells of a destroyed destructible
  (cover, barricades) free up for movement immediately instead of staying blocked.

## Requirements

- Warhammer 40,000: Rogue Trader (current patch).
- **The Infinite Museion DLC (owned).** The Deathwatch armour, helmet and related assets this mod is
  built around are that DLC's content. The mod enforces ownership: without the DLC, the Custom Space
  Marine option does not appear in character creation. The DLC's story content does not need to be
  enabled, only owned.
- **MicroPatches** (by Microsoftenator). A required, declared dependency: the game will not load
  Deathwatch unless MicroPatches is installed **and enabled**. Most Rogue Trader mod setups already
  have it. https://www.nexusmods.com/warhammer40kroguetrader/mods/203

## Installation

### Recommended: ModFinder

[ModFinder](https://www.nexusmods.com/warhammer40kroguetrader/mods/146) is the mod manager most
Rogue Trader mod pages point you at, and it handles installation and updates for you.

1. Install Deathwatch with ModFinder (or point it at a downloaded `Deathwatch.zip`).
2. Make sure **MicroPatches** is installed and enabled too (see Requirements) - Deathwatch refuses
   to load without it.
3. Launch the game, open the **Mods** menu from the title screen, enable **Deathwatch - Play as a
   Space Marine**, and restart when prompted.
4. Start a New Game and pick the **Custom Space Marine** tile in character creation.

### Manual

1. Download `Deathwatch.zip` from the Releases page.
2. Extract it into your Rogue Trader modifications folder:
   `%USERPROFILE%\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\Modifications`
   You should end up with:
   `...\Modifications\Deathwatch\OwlcatModificationManifest.json`
   (If you see `Modifications\Deathwatch\Deathwatch\...` you extracted one level too deep.)
3. Continue from step 2 above (MicroPatches, enable in the Mods menu, restart, new game).

## Known issues and things that are BY DESIGN

- **This mod is not balanced, and does not try to be.** You are an Astartes in a campaign written
  for baseline humans; encounters will often be easy. Play it for the fantasy, not the challenge.
- **NPCs and the story do not react to you being a Space Marine.** Dialogue, cutscenes and quests
  treat you as the vanilla Rogue Trader.
- **Most human-scale equipment cannot be worn.** Deliberate: an Astartes wears Astartes wargear. If
  you want to lift the restrictions anyway, third-party tools such as ToyBox can do so.
- **Some combat arenas are cramped.** The marine is Large (2x2) in combat; a few tight maps have
  paths a Large character cannot take.
- **Some scripted animations look odd at marine scale** - sitting on the command throne, certain
  cutscene interactions, and similar human-skeleton moments.
- **The Blade Dancer archetype has broken animations on a marine.** Its moveset was made for the
  human skeleton. It is deliberately left selectable for the curious - pick it at your own risk.
- **Human capes equip but render invisible on a marine** (their visuals only exist for human
  bodies). The marine-fit capes - including the Deathwatch Cape - display normally.
- **Capes can clip through the pauldrons and backpack.** This matches how the game's own Deathwatch
  Captain renders the same cape; the cloth simulation cannot collide with armour.
- **Human hair and beard options may sit imperfectly on the marine head.**
- **Co-op with a marine is largely untested.**

## Uninstalling / save compatibility

- A save that contains a Custom Space Marine (including a marine mercenary hired into a normal
  campaign) permanently requires this mod. Uninstalling breaks that save.
- Saves without a marine are unaffected; the mod is safe to remove for them.

## Troubleshooting

- All mod log lines in `GameLogFull.txt` (same folder as `Modifications`) are on the `Deathwatch` log
  channel - search the file for `Deathwatch`. If you report a bug, please attach that file.
- If the game updates and the mod detects an incompatibility, it disables itself and logs
  `[Init][ERR] ... Deathwatch disabled itself` instead of half-working.

## Building from source

This repository contains the mod source plus the two customized build-pipeline files it needs.

1. Get Owlcat's official modification template (`WhRtModificationTemplate`) and open it in the Unity
   version it specifies.
2. Copy `Assets/Modifications/DeathwatchMod` from this repository into the template's
   `Assets/Modifications/`.
3. Copy the two customized build-task files from this repository over the template's copies:
   - `Assets/Editor/Build/Tasks/PrepareArtifacts.cs` - ships the mod's `Audio/` folder (the two voice
     soundbanks) with the build; a stock template silently drops it.
   - `Assets/Editor/Build/Tasks/CreateManifestAndSettings.cs` - carries the MicroPatches build-task
     integration (per-bundle dependency recording and the Edit-only patch filter) a stock template lacks.
4. In the template's `Packages/manifest.json`, make sure `com.unity.modules.animation` is present in
   the dependencies (alongside the stock `com.unity.modules.assetbundle`) - without it, Unity
   silently strips animation assets from the built bundles.
5. In Unity: `Assets > Modification Tools > Build`. The built mod and `Deathwatch.zip` appear in
   `Build/`. Copy the zip out immediately: the next build of any mod wipes that folder.

Design and architecture notes for modders are in [DESIGN_NOTES.md](DESIGN_NOTES.md); every edit the
mod makes to vanilla blueprints is documented in
[Assets/Modifications/DeathwatchMod/Blueprints/PATCHES.md](Assets/Modifications/DeathwatchMod/Blueprints/PATCHES.md).

## Credits

- Built on Owlcat Games' official Rogue Trader modification template.
- Thanks to the Rogue Trader modding community, whose open-source mods (PlayableX, MicroPatches,
  ReDress and others) set the conventions this mod follows.
- This is an unofficial fan modification. Warhammer 40,000 and all related marks are the property of
  Games Workshop; Rogue Trader is the property of Owlcat Games. No assets from other games are used.
