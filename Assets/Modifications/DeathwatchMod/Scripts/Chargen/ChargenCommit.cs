using System;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.CharGen;               // CharGenContextVM
using Kingmaker.UnitLogic.Progression.Features;   // BlueprintRace (mercenary race correction, G3-B)

namespace DeathwatchMod
{
    // COMMIT-TIME ARMOUR SWAP + STARTING KIT: equip the chapter-specific Deathwatch armour item on the finished
    // marine, so the chapter pauldron (baked into that item's worn KEE) rides the EQUIPPED armour -- shows when
    // worn, comes off when removed (fixes the floating-pauldron bug). This is the one unavoidable runtime step:
    // no Owlcat component lets a feature equip an item, and the marine's armour slot is fixed at tile-selection
    // (before the chapter). The Deathwatch CAPE is granted here too: m_StartingInventory on the unit blueprint
    // is DEAD for chargen-created units -- BaseUnitEntity.OnInitialize skips AddStartingInventory under the
    // ChargenUnit context (BaseUnitEntity.cs:976) -- so commit is the earliest working moment.
    // CharGenContextVM.CompleteCharGen fires once on the REAL committed unit (NewGame finish) AFTER the chapter
    // feature is committed + Body.Initialize equipped the shared armour. InsertItem(force) auto-unequips the shared
    // armour back into inventory and re-renders the EE. Gated to our marine (DetectChapter) + to chapters with an
    // ArmorItemGuid (no-op for the rest until their items exist, and for non-marines). The SPECIALITY HELM is
    // swapped here too (same reason as the armour): the six speciality helm systems ride NAMED helm items whose
    // descriptions state their effects (armour-item pattern, James 2026-07-06); all six specialities swap (the
    // base Auto-senses-only Deathwatch Helm remains the chargen/fallback helm).
    [HarmonyPatch(typeof(CharGenContextVM), "CompleteCharGen")]
    internal static class CharGenContextVM_CompleteCharGen_MarineArmour_Patch
    {
        private const string DeathwatchCapeItem_Guid = "9c2b9f3f5bc74d0d8efa35f4d3109c13";   // DeathwatchCape_Item

        // Speciality feature -> named helm item.
        private static readonly System.Collections.Generic.Dictionary<string, string> SpecialityHelms =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "1ecda642d54c491a96ebf767cb12d7fb", "b2eb22477c654b2791a49f0f45e6642b" },   // Tactical   -> Tactical Cogitator Helm
            { "5812c9cea10f4420b5915c9b0196a83b", "e47ba7eea837404ea96aae4c2f1471fc" },   // Apothecary -> Diagnostor Helm
            { "6109110b5f3242828a3104f4b1180556", "1e96e14dbd774f41a92b84257ee44f5a" },   // Devastator -> Ballistic Auspex Helm
            { "a072ea57e63e4ada9148d5cdbb2ce1c0", "b5cd9138411c4f848e0f327490cb8c0d" },   // Assault    -> Assault Targeter Helm
            { "8e283ed69c0d4f569a2cb0e7963362d7", "64e74d1d7d5d4406b1d6c92302c3595d" },   // Librarian  -> Psychic Hood
            { "7f897a85210b4c68a0420dd13ddd81af", "4ca2beb6309343438f4edc90d80d4a40" },   // Techmarine -> Servo-Helm
        };

        // The selectable "Space Marine" voice mixes General + Tactical/Assault/Devastator-flavoured lines.
        // If the player chose it, swap the committed unit to the speciality-filtered variant (same soundbank,
        // filtered events) so e.g. a Devastator never barks assault-marine lines (James 2026-07-06). Any other
        // chosen voice (Apothecary, Ulfar, vanilla) is left untouched. PartUnitAsks.CustomAsks IS the chargen
        // choice; SetCustom persists it on the unit (save-safe, per-unit -- also correct for mercenaries).
        private const string SpaceMarineVoice_Guid = "2cc2468184df42e0952936e6743cc0db";  // DW_SpaceMarine_Barks
        private const string GeneralVoice_Guid = "40a4dc62d71c4757979b295b63f0ddb6";      // DW_SpaceMarine_Barks_General
        private static readonly System.Collections.Generic.Dictionary<string, string> SpecialityVoices =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "1ecda642d54c491a96ebf767cb12d7fb", "b8880ef753bc449f903e8b947629f062" },   // Tactical   -> _Tactical
            { "a072ea57e63e4ada9148d5cdbb2ce1c0", "6cbebc5c76204f059f77e20dff56b104" },   // Assault    -> _Assault
            { "6109110b5f3242828a3104f4b1180556", "5eff1364ab5f42ae993661ee9bc885e7" },   // Devastator -> _Devastator
            { "5812c9cea10f4420b5915c9b0196a83b", GeneralVoice_Guid },                     // Apothecary  -> General only
            { "8e283ed69c0d4f569a2cb0e7963362d7", GeneralVoice_Guid },                     // Librarian   -> General only
            { "7f897a85210b4c68a0420dd13ddd81af", GeneralVoice_Guid },                     // Techmarine  -> General only
        };

        [HarmonyPostfix]
        private static void Postfix(BaseUnitEntity resultUnit)
        {
            try
            {
                if (resultUnit == null) return;
                var cv = DeathwatchModMain.DetectChapter(resultUnit);
                if (cv == null) return;
                var chap = cv.Value;

                // MERCENARY MARINE = REAL MARINE (G3-B, 2026-07-08). A hired marine commits on the pre-created
                // CustomCompanion entity: Player.CreateCustomCompanion builds it from the HARDCODED
                // BlueprintRoot.CustomCompanion (Human race) and levels it BEFORE chargen opens -- before the marine
                // tile even exists -- so the NewGame config-unit swap (MarineUnitRouter) cannot give the committed
                // merc the Astartes race, and he commits Human. Everything else that makes a marine (statline + the
                // TwoHandedWeaponsInOneHand anims + immunities + the equipment markers) already rides in via the
                // chapter's DW_AstartesBaseline; the ONLY gap is Progression.Race -- which is what IsMarineUnit keys
                // on -- so without this, none of the per-unit marine patches (equip unlocks, dynamic size, weapon
                // scale) fire for a merc. Correct the race so a merc is mechanically identical to a NewGame marine.
                // SetRace is a plain race-FEATURE swap (drop the Human race feature, add DW_AstartesRace); that race
                // also grants AstartesPhysiology, which the marine already carries from the baseline -- the exact
                // two-source setup every NewGame marine has (race + baseline), so it is deduped/handled. No-op for a
                // NewGame player: he already has DW_AstartesRace, so the guid guard skips it (SetRace early-returns).
                var curRace = resultUnit.Progression != null ? resultUnit.Progression.Race : null;
                if (curRace == null || curRace.AssetGuid != DeathwatchModMain.AstartesRace_Guid)
                {
                    var dwRace = ResourcesLibrary.BlueprintsCache.Load(DeathwatchModMain.AstartesRace_Guid) as BlueprintRace;
                    if (dwRace == null)
                        DeathwatchModMain.LogError("[MarineUnit][ERR] CompleteCharGen: DW_AstartesRace unresolved -- merc keeps "
                            + (curRace != null ? curRace.name : "null") + " race; IsMarineUnit will stay false for him.");
                    else
                    {
                        resultUnit.Progression.SetRace(dwRace);
                        DeathwatchModMain.Log("[MarineUnit] mercenary marine race corrected: "
                            + (curRace != null ? curRace.name : "null") + " -> Astartes (IsMarineUnit now true; per-unit marine patches will fire).");
                    }
                }

                // SURE-FIRE SOURCE REGRESSION GUARD (was a 1.1.1 tester diagnostic): a fresh Assault marine once
                // carried the VANILLA crime-lord sure-fire scaling instead of the AGI clone. That's shipped + verified
                // fixed (the committed unit always carries the AGI clone), so the normal case is SILENT now -- log
                // ONLY the UNEXPECTED vanilla source, so a future regression report stays decisive. Detection only.
                bool vanillaSF = false;
                foreach (var f in resultUnit.Progression.Features)
                    if (f != null && f.Blueprint != null && f.Blueprint.AssetGuid == "881def61ed1e41d183e4d2788059c43a") { vanillaSF = true; break; }   // vanilla Criminal innate
                if (vanillaSF)
                    DeathwatchModMain.Log("[MarineArmour] sure-fire source at commit is the VANILLA crime-lord innate -- UNEXPECTED regression, please report this line.");

                // Speciality voice: only when the player chose the mixed "Space Marine" voice.
                var asks = resultUnit.Asks;
                if (asks != null && asks.CustomAsks != null && asks.CustomAsks.AssetGuid == SpaceMarineVoice_Guid)
                {
                    string voiceGuid = null;
                    foreach (var f in resultUnit.Progression.Features)
                        if (f != null && f.Blueprint != null && SpecialityVoices.TryGetValue(f.Blueprint.AssetGuid, out voiceGuid)) break;
                    var voiceBp = voiceGuid != null
                        ? ResourcesLibrary.BlueprintsCache.Load(voiceGuid) as Kingmaker.Visual.Sound.BlueprintUnitAsksList : null;
                    if (voiceBp != null)
                    {
                        asks.SetCustom(voiceBp);
                        if (resultUnit.View != null) resultUnit.View.UpdateAsks();
                        DeathwatchModMain.Log("[MarineArmour] speciality voice variant set: " + voiceBp.name + ".");
                    }
                }

                // Speciality helm swap (mirrors the armour swap below; base helm was equipped by Body.Initialize).
                string helmGuid = null;
                foreach (var f in resultUnit.Progression.Features)
                    if (f != null && f.Blueprint != null && SpecialityHelms.TryGetValue(f.Blueprint.AssetGuid, out helmGuid)) break;
                if (helmGuid != null)
                {
                    var helmBp = ResourcesLibrary.BlueprintsCache.Load(helmGuid) as Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentHead;
                    var head = resultUnit.Body != null ? resultUnit.Body.Head : null;
                    if (helmBp == null) DeathwatchModMain.Log("[MarineArmour] speciality helm item not found: " + helmGuid);
                    else if (head != null)
                    {
                        var oldHelm = head.MaybeItem;
                        var helmItem = resultUnit.Inventory.Add(helmBp);
                        if (helmItem != null)
                        {
                            head.InsertItem(helmItem, true);                          // auto-unequips the base helm
                            if (oldHelm != null) resultUnit.Inventory.Remove(oldHelm); // no spare in the bag
                            DeathwatchModMain.Log("[MarineArmour] equipped speciality helm " + helmBp.name + ".");
                        }
                    }
                }

                var cape = ResourcesLibrary.BlueprintsCache.Load(DeathwatchCapeItem_Guid) as Kingmaker.Blueprints.Items.BlueprintItem;
                if (cape != null)
                {
                    var capeItem = resultUnit.Inventory.Add(cape);
                    var shoulders = resultUnit.Body != null ? resultUnit.Body.Shoulders : null;
                    if (capeItem != null && shoulders != null && !shoulders.HasItem)
                        shoulders.InsertItem(capeItem, true);   // auto-equip into the empty Shoulders slot
                    DeathwatchModMain.Log("[MarineArmour] Deathwatch cape "
                        + (shoulders != null && shoulders.MaybeItem == capeItem ? "added and equipped." : "added to inventory."));
                }
                else DeathwatchModMain.Log("[MarineArmour] Deathwatch cape item not found: " + DeathwatchCapeItem_Guid);

                if (string.IsNullOrEmpty(chap.ArmorItemGuid)) return;
                var bp = ResourcesLibrary.BlueprintsCache.Load(chap.ArmorItemGuid) as Kingmaker.Blueprints.Items.Armors.BlueprintItemArmor;
                if (bp == null) { DeathwatchModMain.Log("[MarineArmour] chapter armour item not found: " + chap.ArmorItemGuid); return; }
                var slot = resultUnit.Body != null ? resultUnit.Body.Armor : null;
                if (slot == null) { DeathwatchModMain.Log("[MarineArmour] committed marine has no armour slot."); return; }
                var old = slot.MaybeItem;                  // the shared starting armour, to delete after the swap (no spare in the bag)
                var item = resultUnit.Inventory.Add(bp);   // create + add to the marine's inventory (PartItemsCollection.Add(BlueprintItem) -> ItemEntity)
                if (item == null) { DeathwatchModMain.Log("[MarineArmour] failed to create armour item."); return; }
                slot.InsertItem(item, true);               // equip it -> auto-unequips the shared armour to inventory + re-renders the EE
                if (old != null) resultUnit.Inventory.Remove(old);   // delete the unequipped shared armour so there is no spare
                DeathwatchModMain.Log("[MarineArmour] equipped " + bp.name + " on the committed marine (chapter " + chap.FeatureGuid + ").");
            }
            catch (Exception e) { DeathwatchModMain.LogError("[MarineArmour][ERR] CompleteCharGen", e); }
        }
    }
}
