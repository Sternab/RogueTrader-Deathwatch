using System;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.CharGen;               // CharGenContextVM

namespace DeathwatchMod
{
    // COMMIT-TIME ARMOUR SWAP: equip the chapter-specific Deathwatch armour item on the finished marine, so the
    // chapter pauldron (baked into that item's worn KEE) rides the EQUIPPED armour -- shows when worn, comes off
    // when removed (fixes the floating-pauldron bug). This is the one unavoidable runtime step: no Owlcat component
    // lets a feature equip an item, and the marine's armour slot is fixed at tile-selection (before the chapter).
    // CharGenContextVM.CompleteCharGen fires once on the REAL committed unit (NewGame finish) AFTER the chapter
    // feature is committed + Body.Initialize equipped the shared armour. InsertItem(force) auto-unequips the shared
    // armour back into inventory and re-renders the EE. Gated to our marine (DetectChapter) + to chapters with an
    // ArmorItemGuid (no-op for the rest until their items exist, and for non-marines).
    [HarmonyPatch(typeof(CharGenContextVM), "CompleteCharGen")]
    internal static class CharGenContextVM_CompleteCharGen_MarineArmour_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BaseUnitEntity resultUnit)
        {
            try
            {
                if (resultUnit == null) return;
                var cv = DeathwatchModMain.DetectChapter(resultUnit);
                if (cv == null) return;
                var chap = cv.Value;
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
            catch (Exception e) { DeathwatchModMain.Log("[MarineArmour][ERR] CompleteCharGen: " + e); }
        }
    }
}
