using System;
using System.Collections.Generic;                 // HashSet
using HarmonyLib;
using Kingmaker.Blueprints.Items.Components;      // EquipmentRestrictionHasFacts, EquipmentRestrictionMachineTrait
using Kingmaker.Blueprints.Items.Weapons;         // BlueprintItemWeapon
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.Enums;                            // WeaponClassification, WeaponFamily
using Kingmaker.Items;                            // ItemEntity, ItemEntityWeapon
using Kingmaker.Items.Slots;                      // HandSlot
using Kingmaker.Mechanics.Entities;               // MechanicEntity

namespace DeathwatchMod
{
    // A marine's hand rule (HandSlot.IsItemSupported, keyed on CommonSpaceMarineFact) rejects a MELEE 2H weapon
    // in one hand, so a two-handed melee weapon bounces with "Wrong slot". The marine has NO complete 2H-melee
    // animation set (TwoHandedHammer has an attack but no idle = T-pose), so 2H melee is carried ONE-HANDED in the
    // OFF hand and animated as a native one-handed style (BrutalOneHanded, set by the TwoHandedWeaponsInOneHand
    // component on AstartesPhysiology_Feature -- its style list is what actually drives the remap; this postfix is
    // the belt-and-braces equip lift). We lift the rejection ONLY for the OFF hand + any 2H MELEE weapon except the
    // rock saws (Classification Chainsaw -- James's exclusion; their HeavyOnHip style is also deliberately absent
    // from the anim component, so they bounce at the engine level too). Started as the Librarian-staff mechanism,
    // generalized to all 2H melee 2026-07-04 (tester request).
    [HarmonyPatch(typeof(HandSlot), nameof(HandSlot.IsItemSupported))]
    internal static class HandSlot_IsItemSupported_Marine2HMelee_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(HandSlot __instance, ItemEntity item, ref bool __result)
        {
            try
            {
                if (__result) return;                                          // base method already allowed it
                if (__instance == null || __instance.IsPrimaryHand) return;    // OFF hand only (carried one-handed off-hand)
                // THIS MOD's marine only (not CommonSpaceMarineFact: vanilla Ulfar carries that, and he lacks
                // the TwoHandedWeaponsInOneHand animation support this permission depends on).
                if (!DeathwatchModMain.IsMarineUnit(__instance.Owner as BaseUnitEntity)) return;

                var weapon = item as ItemEntityWeapon;
                var bp = weapon != null ? weapon.Blueprint : null;
                if (bp == null) return;
                if (!bp.IsMelee || !bp.IsTwoHanded) return;                           // 2H melee only
                if (bp.Classification == WeaponClassification.Chainsaw) return;       // rock saws stay blocked

                __result = true;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Marine2HMelee][ERR] IsItemSupported", e); }
        }
    }

    // WEAPON unlock for the DW marine, PER-UNIT (no blueprint mutation). The 16 force swords, ~50 two-handed
    // melee weapons (greatswords, power claymores, eviscerators, death-cult blades, thunder hammers, uniques)
    // AND the human plasma/flame ranged weapons (plasma guns/pistols, flamers, hand flamers, vindictor flamers)
    // all carry the SAME inverted EquipmentRestrictionHasFacts blocking anyone with the Astartes marker
    // (950565a6) -- vanilla's way of keeping them off its own Astartes (Ulfar, Uralon). Our marine carries that
    // marker via the baseline, so the rule blocked him. Allow the equip in a postfix ONLY for this mod's marine
    // (IsMarineUnit = the mod-owned race), on: the 16 one-handed force swords (GUID list), ANY 2H melee weapon
    // except the rock saws (Classification Chainsaw), and any RANGED Plasma/Flame-family weapon (tester request
    // 2026-07-04; all their anim styles -- Assault/Pistol/Rifle/HeavyOnHip -- verified present in the marine
    // animset). The vanilla exclusion stays intact for everyone else. The DW marine never carries the
    // restriction's other block-facts (the Aeldari/Drukhari armour-proficiency markers), so a plain allow is
    // safe. NOTE: only THIS inverted restriction is flipped -- an item's other restriction components (e.g.
    // ChaosSword's Corruption requirement, the axes' MachineTrait gate, heavy flamers' Strength 50) still apply,
    // and items with POSITIVE-only gates (heavy flamers, the native Astartes guns) never needed the flip.
    [HarmonyPatch(typeof(EquipmentRestrictionHasFacts), nameof(EquipmentRestrictionHasFacts.CanBeEquippedBy))]
    internal static class EquipmentRestrictionHasFacts_CanBeEquippedBy_MarineWeapons_Patch
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
                if (bp == null) return;
                var w = bp as BlueprintItemWeapon;
                bool nonSaw2HMelee = w != null && w.IsMelee && w.IsTwoHanded
                    && w.Classification != WeaponClassification.Chainsaw;             // any 2H melee except rock saws
                bool plasmaOrFlameRanged = w != null && !w.IsMelee
                    && (w.Family == WeaponFamily.Plasma || w.Family == WeaponFamily.Flame);   // plasma guns/pistols + flamers
                if (!nonSaw2HMelee && !plasmaOrFlameRanged
                    && !ForceSwordGuids.Contains(bp.AssetGuid)) return;               // ...or the 16 1H force swords
                if (!DeathwatchModMain.IsMarineUnit(unit as BaseUnitEntity)) return;  // this mod's marine only
                __result = true;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[MarineWeapons][ERR] CanBeEquippedBy: " + e.Message); }   // Message only: hot path
        }
    }

    // OMNISSIAN-AXE unlock for the DW TECHMARINE, PER-UNIT. The Omnissian/breacher axes carry an
    // EquipmentRestrictionMachineTrait (equip needs MachineTrait rank >= 1, i.e. Pasqal-style tech units). The gate
    // is pure equip PERMISSION: the axes' abilities are plain AddFactToEquipmentWielder components with no
    // machine-trait dependency, so they function fully for any wielder. James's call: a Techmarine has earned the
    // Machine Cult's rites, so allow the equip for this mod's marine WITH the Techmarine speciality only; every
    // other unit (including the mod's other specialities) keeps the vanilla gate. This restriction is used ONLY by
    // the 9 axe items (verified: no augment or other item carries it), but scoped to 2H melee weapons anyway.
    [HarmonyPatch(typeof(EquipmentRestrictionMachineTrait), nameof(EquipmentRestrictionMachineTrait.CanBeEquippedBy))]
    internal static class EquipmentRestrictionMachineTrait_CanBeEquippedBy_Techmarine_Patch
    {
        private const string TechmarineSpeciality_Guid = "7f897a85210b4c68a0420dd13ddd81af";   // DW_Speciality_Techmarine

        [HarmonyPostfix]
        private static void Postfix(EquipmentRestrictionMachineTrait __instance, MechanicEntity unit, ref bool __result)
        {
            try
            {
                if (__result || __instance == null) return;                            // only flip a BLOCK
                var w = __instance.OwnerBlueprint as BlueprintItemWeapon;
                if (w == null || !w.IsMelee || !w.IsTwoHanded) return;                 // the axes are all 2H melee
                var u = unit as BaseUnitEntity;
                if (!DeathwatchModMain.IsMarineUnit(u)) return;                        // this mod's marine only
                bool techmarine = false;
                foreach (var f in u.Progression.Features)
                    if (f != null && f.Blueprint != null && f.Blueprint.AssetGuid == TechmarineSpeciality_Guid) { techmarine = true; break; }
                if (!techmarine) return;
                __result = true;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[OmnissianAxe][ERR] CanBeEquippedBy: " + e.Message); }   // Message only: hot path
        }
    }
}
