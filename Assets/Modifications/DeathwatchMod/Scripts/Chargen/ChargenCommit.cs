using System;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.CharGen;               // CharGenContextVM

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
    // swapped here too (same reason as the armour): the four speciality helm systems ride NAMED helm items whose
    // descriptions state their effects (armour-item pattern, James 2026-07-06); Librarian/Techmarine keep the
    // base Deathwatch Helm (Auto-senses only).
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
