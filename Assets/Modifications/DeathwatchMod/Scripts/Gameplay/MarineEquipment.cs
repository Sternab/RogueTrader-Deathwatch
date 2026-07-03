using System;
using System.Collections.Generic;                 // HashSet
using HarmonyLib;
using Kingmaker.Blueprints.Items.Components;      // EquipmentRestrictionHasFacts
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.Enums;                            // WeaponClassification
using Kingmaker.Items;                            // ItemEntity, ItemEntityWeapon
using Kingmaker.Items.Slots;                      // HandSlot
using Kingmaker.Mechanics.Entities;               // MechanicEntity
using Kingmaker.View;                              // UnitEntityView
using Kingmaker.Visual.CharacterSystem;           // Character, EquipmentEntity

namespace DeathwatchMod
{
    // FALLBACK NET (pending removal). The ROOT-CAUSE fix now ships in the build: the mod's <Target>_content
    // bundle is a keep-resident dependency of the persistent <Target>_BlueprintDirectReferences bundle (see
    // CreateManifestAndSettings.cs), honoured at runtime by the required MicroPatches. Once a build confirms
    // this patch's [PruneDeadEEs] line never fires across area transitions, delete this whole class.
    //
    // INVISIBLE-MARINE FIX + SELF-HEAL (engine bundle-lifecycle interaction). BundlesLoadService REFERENCE-COUNTS
    // loaded bundles (BundleData.RequestCount); when the last requester releases a bundle and the count hits 0 it
    // is Bundle.Unload(true)'d (BundlesLoadService.cs:199), which DESTROYS its assets. The mod's content bundle
    // (custom EEs: chapter pauldron, helmet) is only transiently requested, so on a transition nothing holds a
    // request on it and it unloads -- the root-cause fix above keeps a permanent request on it. The live body's
    // Character.EquippedItemsEntities / EquipmentEntities still strong-reference the corpses (the lists are only
    // appended on equip), and UpdateCharacter() reads e.name on them -- a destroyed Unity object passes the
    // != null fake-null guard yet throws NRE on .name, unwinding the whole build (invisible marine). Probing
    // .name in a try/catch is the only reliable corpse test.
    // TWO steps, prefixed onto UpdateCharacter:
    //   1. PRUNE the dead EEs so the render survives (a destroyed EE cannot render anyway).
    //   2. SELF-HEAL: pruning alone leaves the look degraded (e.g. the chapter pauldron overlay gone -> the base
    //      spaulder's built-in Blood Angels badge shows). Re-run the engine's own re-dress,
    //      UnitEntityView.UpdateBodyEquipmentModel(): it walks every body slot and re-adds each equipped item's
    //      EEs -- reloading the mod bundle on demand -- via the same path as a manual unequip/re-equip.
    //      AddEquipmentEntity has a Contains guard, so still-alive EEs no-op (idempotent). Doll-room Characters
    //      have no UnitEntityView; they just get the prune (the doll rebuilds from the healed live body).
    [HarmonyPatch(typeof(Character), "UpdateCharacter")]
    internal static class Character_PruneDeadEEs_Patch
    {
        private static bool IsDeadEE(EquipmentEntity e)
        {
            if (e == null) return true;              // Unity fake-null: cleanly-destroyed / unassigned
            try { _ = e.name; return false; }        // raw native getter -> NRE if the native object was destroyed
            catch { return true; }
        }

        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            try
            {
                if (__instance == null) return;
                int pruned = __instance.EquipmentEntities.RemoveAll(IsDeadEE);
                pruned += __instance.EquippedItemsEntities.RemoveWhere(IsDeadEE);
                if (pruned == 0) return;

                var view = __instance.GetComponentInParent<UnitEntityView>();
                if (view != null && view.EntityData != null)
                {
                    view.UpdateBodyEquipmentModel();   // re-adds every equipped item's EEs (idempotent)
                    DeathwatchModMain.Log("[PruneDeadEEs] pruned " + pruned + " destroyed EE(s) after a bundle unload and re-added the equipped items' EEs on " + view.name + ".");
                }
                else
                {
                    DeathwatchModMain.Log("[PruneDeadEEs] pruned " + pruned + " destroyed EE(s) on a viewless Character (doll room) -- rebuilds from the live body.");
                }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[PruneDeadEEs][ERR] UpdateCharacter prefix", e); }
        }
    }

    // A marine's hand rule (HandSlot.IsItemSupported, keyed on CommonSpaceMarineFact) rejects a
    // MELEE 2H weapon in one hand, so a two-handed psyker STAFF bounces with "Wrong slot". The marine has NO complete
    // 2H-melee animation set (TwoHandedHammer has an attack but no idle), so the staff is carried ONE-HANDED in the
    // OFF hand and animated as the native Fencing style (via the TwoHandedWeaponsInOneHand component on
    // AstartesPhysiology_Feature). We lift the rejection ONLY for the OFF hand + a Classification==PsykerStaff staff,
    // so it equips off-hand and plays Fencing/off idle+walk + OffHandAttack Fencing. Other 2H melee stays rejected.
    [HarmonyPatch(typeof(HandSlot), nameof(HandSlot.IsItemSupported))]
    internal static class HandSlot_IsItemSupported_LibrarianStaff_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(HandSlot __instance, ItemEntity item, ref bool __result)
        {
            try
            {
                if (__result) return;                                          // base method already allowed it
                if (__instance == null || __instance.IsPrimaryHand) return;    // OFF hand only (staff carried one-handed off-hand)
                // THIS MOD's marine only (not CommonSpaceMarineFact: vanilla Ulfar carries that, and he lacks
                // the TwoHandedWeaponsInOneHand animation support this permission depends on).
                if (!DeathwatchModMain.IsMarineUnit(__instance.Owner as BaseUnitEntity)) return;

                var weapon = item as ItemEntityWeapon;
                var bp = weapon != null ? weapon.Blueprint : null;
                if (bp == null) return;
                if (bp.Classification != WeaponClassification.PsykerStaff) return;   // psyker/force staffs only
                if (!bp.IsMelee || !bp.IsTwoHanded) return;                    // exactly the staffs the marine gate blocks

                __result = true;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[LibrarianStaffEquip][ERR] IsItemSupported", e); }
        }
    }

    // FORCE-SWORD unlock for the DW Librarian, PER-UNIT (no blueprint mutation). The 16 force swords carry an
    // INVERTED EquipmentRestrictionHasFacts blocking anyone with the Astartes marker (950565a6) -- vanilla's way
    // of keeping force swords off its own Astartes (Ulfar, Uralon). Our Librarian carries that marker via the
    // baseline, so the same rule blocked him. The mod previously STRIPPED the marker from the 16 blueprints at
    // load -- a global change that also unlocked the swords for vanilla Ulfar/Uralon (release-gate catch). Now:
    // allow the equip in a postfix ONLY for this mod's marine (IsMarineUnit = the mod-owned race) on exactly
    // these 16 items; the vanilla exclusion stays intact for everyone else. The DW marine never carries the
    // restriction's other block-facts (the Scorn/Heretic markers), so a plain allow is safe here.
    [HarmonyPatch(typeof(EquipmentRestrictionHasFacts), nameof(EquipmentRestrictionHasFacts.CanBeEquippedBy))]
    internal static class EquipmentRestrictionHasFacts_CanBeEquippedBy_ForceSword_Patch
    {
        private static readonly HashSet<string> ForceSwordGuids = new HashSet<string>
        {
            "7d145260c96243869af97d0b300d420a", // ForceSword_Item (base)
            "56c6e66548d54cdfbd0eacbf761bf7a9", // AeldariForceSword_Item
            "73dfc37152024206b3994abd34347da8", // ChaosSword_Item
            "24db44debaed469386fad90088c20ee9", // AncientForceSword_Item
            "ffec3e6ca92043b29c435d677985fc8c", // ForceSwordCH1Unique1_Item
            "4dbd3d83e1f542d5b1b35dbc9a3ce6e9", // ForceSwordCH1Unique2_Item
            "48ad9b97df454a6193d03ce18a54512d", // ForceSwordCH2Unique1_Item
            "b6e69939220e4338bc07519cae3c860e", // ForceSwordCH2Unique2_Item
            "3620f05386b44a0d86f2ffc6ee4ab0b3", // ForceSwordCH2Unique3_Item
            "6e959928644941898894055a95dcc199", // ForceSwordCH2Unique4_Item
            "75d5aa9e45b9440ca68d8ffaa99146ea", // ForceSwordCH3Unique1_Item
            "602f13303c9345e7b6887b09d6cbf85f", // ForceSwordCH3Unique2_Item
            "d38487db90974273bdede761e07c1aac", // ForceSwordCH4Unique1_Item
            "9b29810b0c1148639fd7aa1a3f5f313c", // ForceSwordCH4Unique2_Item
            "e933a559b09b45e3a89c51e2cb122062", // ForceSwordCH4Unique3_Item
            "dfcbb13715bd45d59fc09df3d95cc535", // ForceSwordCH4Unique4_Item
        };

        [HarmonyPostfix]
        private static void Postfix(EquipmentRestrictionHasFacts __instance, MechanicEntity unit, ref bool __result)
        {
            try
            {
                if (__result || __instance == null || !__instance.Inverted) return;   // only flip an inverted-restriction BLOCK
                var bp = __instance.OwnerBlueprint;
                if (bp == null || !ForceSwordGuids.Contains(bp.AssetGuid)) return;    // the 16 force swords only
                if (!DeathwatchModMain.IsMarineUnit(unit as BaseUnitEntity)) return;  // this mod's marine only
                __result = true;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[ForceSword][ERR] CanBeEquippedBy: " + e.Message); }   // Message only: hot path
        }
    }
}
