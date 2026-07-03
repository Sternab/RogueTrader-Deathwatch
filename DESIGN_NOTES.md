# Deathwatch - Design Notes

Technical notes for players and fellow modders. Written to answer the questions a curious modder
would ask before (or instead of) reading the source.

## Design principles

- **Data-first.** Everything the engine can express as data is data: ~80 blueprints (`.jbp`) carry the
  chapters, specialities, deeds, burdens, portraits, voices, items and the Astartes baseline; five
  registered `.jbp_patch` files make the only edits to vanilla blueprints (documented in
  `Assets/Modifications/DeathwatchMod/Blueprints/PATCHES.md`). The Harmony assembly is small (~10
  focused files), and every patch that must be runtime carries a comment explaining exactly why it
  cannot be data.
- **Scoped to this mod's marine.** Gameplay patches gate on the mod-owned Astartes race blueprint, not
  on `RaceId` or Space Marine facts, which vanilla Ulfar and Uralon also carry. A player who never
  creates a marine keeps a byte-identical vanilla experience (plus three extra voices and eleven
  portraits in the character creator).
- **All-or-nothing loading.** If a game update breaks a patch target, the mod unpatches itself and logs
  "game version incompatible" instead of half-working.

## "Isn't the chapter pauldron just a fake shoulder pad rendered over the real one?"

No, and this was verified against the decompiled engine, not vibes. Rogue Trader's character system
composes a unit from EquipmentEntities (EEs) in two passes, both driven by the EE `Layer` field:

1. **Geometry:** one mesh per body-part slot. The highest-Layer part evicts lower ones from the slot
   (`Character.UpdateCharacter`), and everything is combined into a single SkinnedMeshRenderer
   (`Character.BuildMesh`). Our Layer-201 pauldron EE replaces the armour's Layer-200 spaulder in the
   slot. One spaulder is ever drawn. There is no stacking and no z-fighting.
2. **Texture:** each body-part type owns a fixed rectangle of the character's runtime texture atlas, and
   textures composite into it in Layer order (`CharacterAtlas.Build`). This is the same machinery that
   bakes a scar or tattoo over skin. Our pauldron EEs additionally set `ShowLowerMaterials: 0`, so the
   base spaulder's texture is not even included in the bake.

This is a first-class vanilla authoring pattern, not a mod trick: the game ships ~149 scar EEs at
Layer 3 (Ulfar's own face scar among them), tattoo and implant-port EEs, and, as the direct precedent,
`EE_CosmeticDirt1_M_SM`: an Owlcat-authored Layer-300 EE that composites a texture over the space marine
armour's own regions, including the exact Spaulders slot this mod uses. Dozens of vanilla EEs sit at
exactly Layer 201.

The alternative ("proper" per-chapter clones of the complete armour EE) would require physically
re-shipping all 14 meshes and 42 textures of the armour inside the mod, per chapter, for zero visual
difference. That would be a far worse outcome for everyone.

## Compatibility

**What this mod changes for everyone (marine or not):**

- Adds 11 portraits and 3 voices (DW Space Marine, DW Apothecary, Ulfar) to standard character
  creation. By design: they are usable by human characters too.
- Patches 5 vanilla blueprints as data (CharGenRoot, the Sanctioned Psyker chargen selection, one
  Chapter 3 dialogue cue, the Space Marine immunity feature's display text, Ulfar's voice name).
  Patch-style mods compose: another mod patching the same blueprints is fine in any load order. A mod
  that fully replaces one of these blueprints will suppress this mod's edits to it (most visibly, the
  DW portraits and voices vanish); load order cannot fix that.
- The psyker chargen selection's prerequisite composition becomes **Or**. Modders adding their own
  prerequisites to `SanctionedPsyker_Selection` should account for this.
- Everything else (dynamic size, weapon scaling, the force-sword allowance, equipment self-healing)
  applies only to this mod's marine. Vanilla Ulfar/Uralon and other mods' Space Marines are untouched,
  including the vanilla rule that keeps force swords off Astartes.

**Save dependence:**

- Any save containing a Custom Space Marine, including a marine mercenary hired into an otherwise
  vanilla campaign, permanently requires this mod. Uninstalling breaks that save.
- Saves without a marine are unaffected and safe to uninstall from.

**Game updates:** if a patch changes character-creation internals, the mod disables itself cleanly at
load and logs `[Init][ERR] ... Deathwatch disabled itself` rather than half-working.

**Keybinds:** none. The mod binds no hotkeys.

## For modders reading the source

- `Scripts/DeathwatchModMain.cs` is the hub (entry point, shared state, the `Chapters[]` table, the
  centralised marine-identity tests). Patch classes live in concern files under `Chargen/`, `Visuals/`
  and `Gameplay/`, named `TargetType_Method[_Purpose]_Patch` so you can grep by the method being
  patched.
- `Blueprints/PATCHES.md` documents every `.jbp_patch` (targets, operations, maintenance invariants).
- Logging: every mod line is tagged `[Deathwatch] [Area]`; errors are `[Area][ERR] context: <exception>`.
  If you are debugging an interaction, grep your `GameLogFull.txt` for `[Deathwatch]`.
