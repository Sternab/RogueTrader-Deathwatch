# Blueprint patches (.jbp_patch)

Edit-patches on **vanilla** blueprints, applied by the game's own blueprint patcher when each target
blueprint first loads. Registered in `DeathwatchPatches.asset` (`Guid` = target blueprint, `Filename` =
patch file without extension, `PatchType: 1` = Edit). Log line on apply: `Patching blueprint: <name> (<guid>)`.
Patch-style mods **compose**: another mod patching the same blueprint is fine in any load order; a mod that
fully *replaces* one of these blueprints suppresses our edits to it.

| Patch file | Target (name → guid) | What it does |
|---|---|---|
| `CharGenRoot_PortraitsAndVoices` | CharGenRoot → `92f5ea9d9306402588dc8418d4a794aa` | Appends the 11 DW portraits to `m_Portraits` and 3 voices (DW Space Marine, DW Apothecary, Ulfar) to `m_Voices`. |
| `ChargenPsyker_Selection` | SanctionedPsyker_Seelction [sic, Owlcat's typo] → `912495ad4ffc4c4da72819d2602f7976` | Sets the psyker chargen selection's prerequisite `Composition` to **Or** (`1`) and appends a `PrerequisiteFact` for the DW Librarian, so the Librarian qualifies for psyker picks and vanilla humans still do. |
| `MeatDump_WakeUp_Cue_2` | Ch3 Commorragh wake-up cue → `d7c178f942e64faebc7518eb79f7823f` | Replaces the cue's ZanniPistol give with a `Conditional`: an Astartes main character is given+equipped an Astartes Bolt Pistol + Combat Knife (prevents a weaponless forced fight); humans get the vanilla ZanniPistol unchanged. |
| `SpaceMarineImmunity_Rename` | SpaceMarineImmunity_Feature → `e4ca8c3dfa934776a624dfd5e7726374` | Sets `m_DisplayName`/`m_Description` loc keys so the otherwise-blank feature renders as "Adeptus Astartes Resilience" in the chargen features grid. |
| `Ulfar_Barks_ChargenVoice` | Ulfar_Barks → `3ea153cb4f714f1798572e89c7cbd1e9` | Sets a `DisplayName` loc key ("Ulfar") so the voice shows a name in the selector. (Its `PreviewSound` is a component field patches can't reach - set at runtime in `ChargenContent.EnsureUlfarPreviewSound`.) |
| `TakeAwayWeapons_Holder` | Prologue capture strip → `6d0b45fd8ebe413ca8d0e4779b367bd2` | Appends `RemoveItemFromPlayer` for the marine's Astartes Bolt Pistol/Combat Knife/Bolter to the capture-beat strip (vanilla only strips the StubRevolver by blueprint, so the marine stayed armed and the "Arm yourself" objective could never fire). No-op for humans - they cannot own these items in the prologue. |
| `GiveWeapons_Holder` | Abelard rescue give-back → `2e3756e28ece47508977b5628645aefc` | Appends a `Conditional` (Astartes main character): Abelard also returns the marine's own three weapons, **unequipped** like the vanilla career weapons - the "Arm yourself" objective completes via an `InsertItemInSlotTrigger` on the player's own equip, and an `Equip: true` give would auto-complete it and skip the tutorial beat. The marine additionally receives his career's vanilla human weapons (harmless inventory items). |
| `PowerBoots_Feature` | PowerBoots_Feature → `437b8247ae98465491ab35193f77e47d` | Appends (via `ComponentsPatches`) an `AddFeatureIfHasFact` with `Not: true` checking the Kick **ability** (`22d6c1b3...`): a wearer who lacks Kick from any source is granted `DW_PowerBoots_KickGrant_Feature` (hidden wrapper → `AddFacts` base Kick). The vanilla feature only *modifies* Kick (0 AP + 2×STR-bonus damage) and was dead without Arch-Militant. Wrapper instead of a direct grant: `AddFeatureIfHasFact` strips its grant when the checked fact is lost, so check(Kick)/grant(Kick) would delete itself. |
| `ForgeWorld_Selection` | ForgeWorld_Selection → `38a36879cbe44b9d982a9014cf9e29ed` | Sets the Forged-for-a-Purpose selection's prerequisite `Composition` to **Or** (`1`) and appends a `PrerequisiteFact` for DW_Chapter_IronHands, so the Iron Hands marine gets the vanilla **choose-one augment sub-menu** (`ChargenForgeWorld` group: Subskin Armour / Locomotion System / Analytics System) in chargen — exactly the `ChargenPsyker_Selection` pattern used for the Librarian. Vanilla forge-worlders still qualify via their own fact. |

## Format notes for modders

- `FieldOverrides`: `FieldName` is a dotted path into the blueprint's fields (e.g. `m_DisplayName.m_Key`).
- `ArrayPatches.OperationType` (int): `3` InsertAfterElement, `4` InsertBeforeElement, `5` InsertAtBeginning,
  `6` InsertLast, `8` RemoveElement. (`0` Override / `7` ReplaceElement are **not dispatched** by the game's
  array patcher - a "replace" must be authored as Remove + Insert, as in `MeatDump_WakeUp_Cue_2`.)
- `RemoveElement` matches the target element **by its `name` field** (supplied in `Value`); nested objects in
  `Value` deserialize by their `"$type": "<typeId>, <ClassName>"`.
- `ComponentsPatches`: `FieldValue` is a *stringified* `{"ComponentValue":{...}}`; only `6` add / `8` remove.
  ⚠️ The FieldValue is deserialized with the game's STRICT `StringEnumConverter`, which **throws on integer
  enum values** — omit `"m_Flags"` (and any other integer-written enum) from the embedded component, unlike
  regular .jbp files where `"m_Flags": 0` is fine. (Shipped incident: the PowerBoots patch with `m_Flags: 0`
  threw `Cannot read Integer as enum value` at item creation and blocked chargen completion.)
- In `ChargenPsyker_Selection`, `"FieldValue": 1` on `Prerequisites.Composition` = `Or` (enum ordinal).

## Maintenance invariants

- **MeatDump_WakeUp_Cue_2** removes the vanilla `AddItemToPlayer` by its exact element name
  (`$AddItemToPlayer$4f0e66491d7f46978576b9946413b476`). If a game update changes that element, the remove
  aborts (logged: `No item to remove found`) and the insert still runs - humans would then be offered the
  pistol twice. Re-sync the `Value` block from a fresh blueprint dump if that log line ever appears.
- One patch file per target GUID: the game's per-mod patch registry is a `Guid → Filename` dictionary, so
  registering two files for the same target silently keeps only one.
