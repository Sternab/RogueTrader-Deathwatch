using System;
using System.Collections.Generic;                 // List (AoE preview target list)
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.Blueprints.Items.Weapons;         // BlueprintItemWeapon
using Kingmaker.Controllers.Combat;               // PartUnitCombatState
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.Enums;                            // WeaponFamily, WeaponClassification
using Kingmaker.Mechanics.Entities;               // MechanicEntity
using Kingmaker.UI.SurfaceCombatHUD;              // AbilityTargetUIDataCache
using Kingmaker.UnitLogic;                        // UnitHelper.SnapToGrid (extension on BaseUnitEntity)
using Kingmaker.UnitLogic.Abilities;              // AbilityData, AbilityDataHelper, AbilityTargetUIData
using Kingmaker.UnitLogic.Buffs.Blueprints;       // BlueprintBuff
using Kingmaker.View;                             // UnitEntityView
using Kingmaker.View.Equipment;                   // UnitViewHandSlotData

namespace DeathwatchMod
{
    // Force weapons + psyker staffs render twig-like in an Astartes's hand: weapon scale =
    // UnitEntityView.GetSizeScale() (1.0 for the marine) x the weapon prefab's EquipmentOffsets
    // raceScaleList[Spacemarine].WeaponScale, and those slender meshes carry no Spacemarine entry. Rather than
    // ship modified prefabs (asset/FBX edits), we postfix the private weapon-scale getter and apply the Astartes
    // bump when one didn't already get a per-race bump. Other 2H melee (hammers/greatswords/eviscerators) is
    // deliberately NOT scaled: Owlcat's own marine hammer (Ulfar's Mjodlner, 5d93f3fa) uses a mesh the same size
    // as the human ThunderHammer's, with no EquipmentOffsets race entry -- their calibration for a marine-held
    // heavy weapon is native 1.0; the marine's bulk does the visual work (1.5x read comically large in-game).
    [HarmonyPatch(typeof(UnitViewHandSlotData), "OwnerWeaponScale", MethodType.Getter)]
    internal static class UnitViewHandSlotData_OwnerWeaponScale_Patch
    {
        private const float AstartesWeaponScale = 1.5f;   // matches the Spacemarine RaceScale dedicated Astartes weapons carry

        [HarmonyPostfix]
        private static void Postfix(UnitViewHandSlotData __instance, ref float __result)
        {
            try
            {
                var owner = __instance != null ? __instance.Owner : null;
                if (!DeathwatchModMain.IsMarineUnit(owner)) return;   // marine only
                if (owner.View == null) return;                       // viewless unit (not spawned yet)
                // NB: UnitViewHandSlotData.VisibleItemBlueprint getter is PRIVATE in the referenced Code.dll (and
                // also private in the newer decompile), so reach the blueprint via the public
                // VisibleItem (ItemEntity) -> Blueprint instead.
                var weapon = __instance.VisibleItem?.Blueprint as BlueprintItemWeapon;
                if (weapon == null || (weapon.Family != WeaponFamily.Force && weapon.Classification != WeaponClassification.PsykerStaff)) return;     // force weapons + psyker staffs only (2H melee stays native -- see header)
                float baseScale = owner.View.GetSizeScale();
                if (__result > baseScale + 0.001f) return;   // prefab already has a Spacemarine RaceScale -> leave it
                __result = baseScale * AstartesWeaponScale;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[MarineWeaponScale][ERR] OwnerWeaponScale: " + e.Message); }   // Message only: hot path
        }
    }

    // ================================================================================================
    // DYNAMIC MARINE SIZE -- the Deathwatch marine is mechanically Medium (1x1) in exploration (so it
    // fits the command throne, ladders and tight corridors) and Large (2x2) in combat (the full Owlcat
    // marine footprint). The VISUAL model stays full Astartes size in BOTH states.
    //   * Baseline (OriginalSize) = the Astartes race Size, now MEDIUM (DW_AstartesRace.jbp). The
    //     always-on ChangeUnitSize(Large) was removed from AstartesPhysiology_Feature.jbp (its stat
    //     bonuses stay).
    //   * Combat Large = the hidden DW_CombatSize_Large buff (ChangeUnitSize Value->Large), added on
    //     JoinCombat / removed on LeaveCombat. UnitPartSizeModifier restores OriginalSize (Medium) when
    //     the buff is gone -- save-safe (the size is re-derived from the fact list on load).
    //   * Visual: UnitEntityView_GetSizeScale_Marine_Patch forces GetSizeScale()=1f for the marine so the full-size mesh is
    //     never scaled to its mechanical footprint (same trick as Polymorph / the force-weapon patch).
    //   * Pathfinding: the traversal provider CACHES SizeRect, so after each size toggle we MUST rebuild
    //     it via the movement agent's ResetBlocker() and re-snap to grid (SnapToGrid) -- the engine does
    //     not refresh it on a size change.
    // ================================================================================================
    internal static class DynamicMarineSize
    {
        private static BlueprintBuff s_combatSizeBuff;
        private static BlueprintBuff CombatSizeBuff
        {
            get
            {
                if (s_combatSizeBuff == null)
                    s_combatSizeBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(DeathwatchModMain.CombatSizeLargeBuff_Guid);
                return s_combatSizeBuff;
            }
        }

        // Rebuild the cached pathfinding footprint + re-snap, AFTER State.Size has already changed.
        private static void RebuildFootprint(BaseUnitEntity unit)
        {
            var agent = unit.MaybeMovementAgent;            // null if the view isn't spawned yet -- safe to skip
            if (agent != null) agent.ResetBlocker();        // rebuilds the traversal provider from the now-current SizeRect
            unit.SnapToGrid();                              // re-snap to a node that fits the new footprint
        }

        internal static void OnJoin(BaseUnitEntity unit)
        {
            if (!DeathwatchModMain.IsMarineUnit(unit)) return;
            var bp = CombatSizeBuff;
            if (bp == null) { DeathwatchModMain.Log("[DynSize] combat-size buff blueprint missing"); return; }
            if (unit.Buffs.GetBuff(bp) == null) unit.Buffs.Add(bp);   // -> State.Size = Large
            RebuildFootprint(unit);
        }

        internal static void OnLeave(BaseUnitEntity unit)
        {
            if (!DeathwatchModMain.IsMarineUnit(unit)) return;
            var bp = CombatSizeBuff;
            if (bp != null)
            {
                var buff = unit.Buffs.GetBuff(bp);
                if (buff != null) buff.Remove();             // -> UnitPartSizeModifier restores OriginalSize (Medium)
            }
            RebuildFootprint(unit);
        }
    }

    [HarmonyPatch(typeof(PartUnitCombatState), nameof(PartUnitCombatState.JoinCombat))]
    internal static class PartUnitCombatState_JoinCombat_MarineSize_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PartUnitCombatState __instance)
        {
            try { DynamicMarineSize.OnJoin(__instance != null ? __instance.Owner as BaseUnitEntity : null); }
            catch (Exception e) { DeathwatchModMain.LogError("[DynSize][ERR] JoinCombat", e); }
        }
    }

    [HarmonyPatch(typeof(PartUnitCombatState), nameof(PartUnitCombatState.LeaveCombat))]
    internal static class PartUnitCombatState_LeaveCombat_MarineSize_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(PartUnitCombatState __instance)
        {
            try { DynamicMarineSize.OnLeave(__instance != null ? __instance.Owner as BaseUnitEntity : null); }
            catch (Exception e) { DeathwatchModMain.LogError("[DynSize][ERR] LeaveCombat", e); }
        }
    }

    // Keep the marine MODEL at full Astartes size regardless of its (Medium/Large) mechanical Size --
    // GetSizeScale() otherwise shrinks/grows the mesh by 0.66^(OriginalSize-State.Size).
    [HarmonyPatch(typeof(UnitEntityView), nameof(UnitEntityView.GetSizeScale))]
    internal static class UnitEntityView_GetSizeScale_Marine_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitEntityView __instance, ref float __result)
        {
            try
            {
                var u = __instance != null ? __instance.EntityData as BaseUnitEntity : null;
                if (DeathwatchModMain.IsMarineUnit(u))
                    __result = 1f;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[DynSize][ERR] GetSizeScale: " + e.Message); }   // Message only: hot path
        }
    }

    // AOE SELF-HIT PREVIEW FIX (a combat-Large size consequence). Melee AoE attack templates (thunder hammer /
    // power maul / greatsword / crozius) are authored with a one-cell notch for a MEDIUM attacker; the marine is
    // Large (2x2) in combat, so his flank tiles fall inside his own template and the aim preview shows him
    // hitting HIMSELF ("78% | 11-17" over his own head). The damage is PREVIEW-ONLY: delivery unconditionally
    // drops the caster for melee/scatter attacks (AbilityAoEPatternAttack.IsValidTarget: "(!IsMelee &&
    // !IsScatter) || entity != Caster") -- tester-verified, no attack is ever rolled against yourself. This
    // postfix restores preview<->delivery parity: remove the marine caster from his own melee/scatter AoE
    // preview list. Gated to exactly the deliveries that already exclude the caster, so it can never hide a
    // real hit (heals/friendly AoEs are neither IsMelee nor IsScatter); ranged template AoEs (flamers/grenades)
    // CAN really self-hit and are untouched. WHY RUNTIME (not data): the notch lives in the vanilla
    // attack-template blueprints -- widening it for a 2x2 attacker would change the REAL AoE shape for every
    // human wielder (a gameplay change); the preview path (GatherAffectedTargetsData -> CheckAffectedEntity)
    // has no caster exclusion and no blueprint knob. Vanilla has the same latent preview bug (Ulfar is natively
    // Large and Mjodlner's second ability IS ThunderHammer_AoE_Ability); per the per-unit rule we fix only OUR
    // marine. No logging in the body: this runs continuously while aiming.
    [HarmonyPatch(typeof(AbilityDataHelper), nameof(AbilityDataHelper.GatherAffectedTargetsData))]
    internal static class AbilityDataHelper_GatherAffectedTargetsData_MarineAoePreview_Patch
    {
        private static bool s_loggedOnce;   // one-shot execution proof (the first in-game test round could not distinguish "fix ineffective" from "fix not in the build")

        [HarmonyPostfix]
        private static void Postfix(AbilityData ability, List<AbilityTargetUIData> listToFill)
        {
            try
            {
                if (ability == null || listToFill == null || listToFill.Count == 0) return;
                if (!ability.IsMelee && !ability.IsScatter) return;    // exactly the deliveries that drop the caster
                var caster = ability.Caster as BaseUnitEntity;
                if (!DeathwatchModMain.IsMarineUnit(caster)) return;   // this mod's marine only
                for (int i = listToFill.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(listToFill[i].Target, caster))
                    {
                        listToFill.RemoveAt(i);
                        if (!s_loggedOnce)
                        {
                            s_loggedOnce = true;
                            DeathwatchModMain.Log("[AoePreview] filtered the marine's self-entry from an AoE preview.");
                        }
                    }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[AoePreview][ERR] GatherAffectedTargetsData: " + e.Message); }   // Message only: hot path
        }
    }

    // Belt-and-suspenders for the same preview bug, second seam: AbilityTargetUIDataCache.GetOrCreate constructs
    // hit data DIRECTLY (new AbilityTargetUIData(...), no pattern, no gather) for Unit-anchored abilities'
    // selection previews, and the gather itself poisons this cache with the self-entry via AddOrReplace BEFORE
    // our list postfix runs. Returning default for the marine-caster-as-his-own-melee-target renders reliably
    // HIDDEN: a default entry's Ability is null, so the overtip's blueprint gate (OvertipHitChanceBlockVM
    // UpdateProperties) calls ClearProperties -> HasHit=false -> the view hides the block. Same delivery-parity
    // gate as above; the other cache consumers are safe (LineOfSightVM is enemy+ranged-gated; the combat log's
    // target is never the melee caster -- delivery excludes him).
    [HarmonyPatch(typeof(AbilityTargetUIDataCache), nameof(AbilityTargetUIDataCache.GetOrCreate))]
    internal static class AbilityTargetUIDataCache_GetOrCreate_MarineAoePreview_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(AbilityData ability, MechanicEntity target, ref AbilityTargetUIData __result)
        {
            try
            {
                if (ability == null || target == null) return;
                if (!ReferenceEquals(target, ability.Caster)) return;             // only the caster's own self-entry
                if (!ability.IsMelee && !ability.IsScatter) return;               // exactly the deliveries that drop the caster
                if (!DeathwatchModMain.IsMarineUnit(ability.Caster as BaseUnitEntity)) return;
                __result = default(AbilityTargetUIData);                          // Ability==null -> overtip hides it
            }
            catch (Exception e) { DeathwatchModMain.LogError("[AoePreview][ERR] GetOrCreate: " + e.Message); }   // Message only: hot path
        }
    }
}
