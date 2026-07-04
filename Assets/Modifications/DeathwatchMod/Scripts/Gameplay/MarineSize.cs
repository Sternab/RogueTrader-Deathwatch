using System;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.Blueprints.Items.Weapons;         // BlueprintItemWeapon
using Kingmaker.Controllers.Combat;               // PartUnitCombatState
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.Enums;                            // WeaponFamily
using Kingmaker.UnitLogic;                        // UnitHelper.SnapToGrid (extension on BaseUnitEntity)
using Kingmaker.UnitLogic.Buffs.Blueprints;       // BlueprintBuff
using Kingmaker.View;                             // UnitEntityView
using Kingmaker.View.Equipment;                   // UnitViewHandSlotData

namespace DeathwatchMod
{
    // Human-scaled weapons render tiny in an Astartes's hand: weapon scale = UnitEntityView.GetSizeScale() (1.0 for
    // the marine) x the weapon prefab's EquipmentOffsets raceScaleList[Spacemarine].WeaponScale, and generic weapons
    // carry no Spacemarine entry. Rather than ship modified prefabs (asset/FBX edits), we postfix the private
    // weapon-scale getter and apply the Astartes multiplier for a Spacemarine wielding a Force-family weapon (the
    // 1H force swords) or ANY 2H melee weapon (the off-hand-wieldable set, staffs included) that didn't already get
    // a per-race bump.
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
                if (weapon == null || (weapon.Family != WeaponFamily.Force && !(weapon.IsMelee && weapon.IsTwoHanded))) return;     // force weapons + all 2H melee (staffs included)
                float baseScale = owner.View.GetSizeScale();
                if (__result > baseScale + 0.001f) return;   // prefab already has a Spacemarine RaceScale -> leave it
                __result = baseScale * AstartesWeaponScale;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[ForceWeaponScale][ERR] OwnerWeaponScale: " + e.Message); }   // Message only: hot path
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
}
