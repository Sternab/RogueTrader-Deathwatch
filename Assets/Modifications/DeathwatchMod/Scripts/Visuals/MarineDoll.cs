using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.Code.UI.DollRoom;                 // CharGenDollRoom
using Kingmaker.UI.DollRoom;                       // CharacterDollRoom
using Kingmaker.UnitLogic.Levelup.Selections.Doll;// DollState
using Kingmaker.UnitLogic;                          // DollData
using Kingmaker.View;                              // UnitEntityView
using Kingmaker.Visual.CharacterSystem;           // Character, CharacterAtlasData
using Kingmaker.Visual.Animation;                 // AnimationSet
using UnityEngine;                                // GameObject, Component, Animator

namespace DeathwatchMod
{
    // Marine chargen-doll avatar loader. The avatar source = a real marine Character prefab (marine animator +
    // skeleton + native scale), fed to CreateAvatar so the chargen marine is built on a marine driver, not the
    // human MaleDoll.
    internal static class MarineDoll
    {
        // Marine doll avatar source = a real marine Character prefab (marine animator + skeleton +
        // native scale), fed to CreateAvatar so the chargen marine is built on a marine driver, not the
        // human MaleDoll. Use the NAKED Ulfar prefab (BCT_SpaceMarine_UlfarNaked) rather than the
        // armoured one (352fddd2) so Ulfar's baked Space Wolf armour mesh does not bleed onto the doll;
        // our race skin + the Deathwatch armour feature-EE then render on the clean marine rig. (In-game
        // meshes come from the EEs, not this prefab, so the live body is unaffected.)
        internal const string MarineAvatar_Guid = "11d3f965d717a19469c28e5e0b332d6d";
        internal static Character MarineDollCharacter;
        internal static bool BuildingMarineDoll;

        internal static DeathwatchModMain.ChapterVisual? DollChapter;       // detected in UpdateMechanicsEntities, applied in UpdateDoll
        internal static Character DollAvatar;              // the live chargen doll Character (cached in UpdateDoll), re-textured on chapter flip

        internal static void EnsureMarineDoll()
        {
            if (MarineDollCharacter == null)
            {
                try
                {
                    var go = ResourcesLibrary.TryGetResource<GameObject>(MarineAvatar_Guid, true, true);
                    MarineDollCharacter = go != null ? go.GetComponentInChildren<Character>(true) : null;
                    if (MarineDollCharacter == null) DeathwatchModMain.LogError("[DollRig][ERR] EnsureMarineDoll: marine doll Character not found in " + MarineAvatar_Guid);
                    else DeathwatchModMain.Log("[DollRig] Loaded marine doll avatar source.");
                }
                catch (Exception e) { DeathwatchModMain.LogError("[DollRig][ERR] EnsureMarineDoll", e); }
            }
        }
    }

    // Build the chargen marine on a real MARINE doll avatar (marine animator + skeleton + native
    // scale) instead of the shared human MaleDoll — so it renders correctly sized with no deformation
    // and no transform-scale hack. We flag when CharGenDollRoom is building OUR marine, then swap the
    // avatar source CharacterDollRoom.CreateAvatar copies from. CreateAvatar copies
    // AnimatorPrefab/AnimationSet/Skeleton/localScale (NOT BakedCharacter); UpdateDoll then strips the
    // copied equipment and re-adds our chargen EEs.
    [HarmonyPatch(typeof(CharGenDollRoom), "UpdateDoll")]
    internal static class CharGenDollRoom_UpdateDoll_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(DollState dollState)
        {
            MarineDoll.BuildingMarineDoll =
                dollState != null && DeathwatchModMain.IsMarinePreset(dollState.RacePreset);
            if (MarineDoll.BuildingMarineDoll) MarineDoll.EnsureMarineDoll();
        }

        // Finalizer, not Postfix: a postfix is SKIPPED when the patched method throws, which would leak
        // BuildingMarineDoll=true into every later doll build. A finalizer runs on both exits (and a void
        // finalizer rethrows the original exception unchanged).
        [HarmonyFinalizer]
        private static void Finalizer(CharGenDollRoom __instance)
        {
            try
            {
                if (!MarineDoll.BuildingMarineDoll) return;
                var f = AccessTools.Field(typeof(CharGenDollRoom), "m_MaleDefaultAvatar");
                var avatar = f != null ? f.GetValue(__instance) as Character : null;
                if (avatar != null)
                {
                    MarineDoll.DollAvatar = avatar;   // cache for live chapter re-texture on flip
                    var anim = avatar.Animator;
                    var ees = avatar.EquipmentEntities;
                    DeathwatchModMain.LogDebug("[DollRig] post-UpdateDoll: animatorChild=" +
                        (anim == null ? "NULL" : anim.gameObject.name) +
                        " skeleton=" + (avatar.Skeleton == null ? "NULL" : avatar.Skeleton.name) +
                        " EEcount=" + (ees == null ? -1 : ees.Count) +
                        " EEs=[" + (ees == null ? "" : string.Join(",", ees.Select(e => e == null ? "null" : e.name))) + "].");
                    // Apply the chapter pauldron badge to the preview doll (in-game, the body gets it from the
                    // chapter's own AddKingmakerEquipmentEntity). No chapter picked yet -> no badge.
                    MarineBody.ApplyMarinePauldron(avatar, MarineDoll.DollChapter.GetValueOrDefault());
                }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[DollRig][ERR] UpdateDoll diag", e); }
            finally { MarineDoll.BuildingMarineDoll = false; }
        }
    }

    // Marine doll PROPORTIONS. CharGenDollRoom.UpdateDoll rebuilds the doll avatar every refresh via
    // CreateAvatar(MaleDoll), which copies the rig fields FROM the human MaleDoll (AnimatorPrefab/Skeleton/
    // AnimationSet/AtlasData) then calls OnStart() synchronously -- OnStart instantiates that AnimatorPrefab
    // as the bone rig. UpdateDoll then sets the marine Skeleton (per-bone scale), but the bone HIERARCHY is
    // the human one -> deformation (human rig + marine scale). The lever is the AnimatorPrefab. Fix: for our
    // marine doll only, lend the shared MaleDoll the marine's rig fields for the SYNCHRONOUS duration of
    // CreateAvatar (so the fresh avatar copies the rig fields BEFORE OnStart instantiates the bone rig -- the
    // MARINE rig), then restore them in the finalizer. We do NOT swap the whole source object (that made
    // CopyEquipmentFrom copy Ulfar's own EEs); CopyEquipmentFrom still copies the MaleDoll's default EEs,
    // which UpdateDoll then strips and replaces with our CollectEntities (race skin + armour) -- now binding
    // to the marine rig. Single-threaded; prefix->OnStart->finalizer run with no yields between.
    [HarmonyPatch(typeof(CharacterDollRoom), "CreateAvatar")]
    internal static class CharacterDollRoom_CreateAvatar_Patch
    {
        // Originals captured off the SHARED MaleDoll source, restored in the postfix.
        private static Animator s_savedAnimatorPrefab;
        private static AnimationSet s_savedAnimationSet;
        private static Skeleton s_savedSkeleton;
        private static CharacterAtlasData s_savedAtlasData;
        private static bool s_patched;

        // NOT 'ref': we mutate fields ON originalAvatar (the MaleDoll), not replace the arg.
        [HarmonyPrefix]
        private static void Prefix(Character originalAvatar)
        {
            s_patched = false;
            if (!MarineDoll.BuildingMarineDoll) return;
            MarineDoll.EnsureMarineDoll();
            var src = MarineDoll.MarineDollCharacter;
            if (originalAvatar == null || src == null) return;

            s_savedAnimatorPrefab = originalAvatar.AnimatorPrefab;
            s_savedAnimationSet = originalAvatar.AnimationSet;
            s_savedSkeleton = originalAvatar.Skeleton;
            s_savedAtlasData = originalAvatar.AtlasData;

            originalAvatar.AnimatorPrefab = src.AnimatorPrefab;   // governs the bone rig OnStart instantiates
            originalAvatar.AnimationSet = src.AnimationSet;
            originalAvatar.Skeleton = src.Skeleton;               // also makes SetAvatar frame the marine
            originalAvatar.AtlasData = src.AtlasData;
            s_patched = true;

            DeathwatchModMain.LogDebug("[DollRig] CreateAvatar prefix: marine AnimatorPrefab=" +
                (src.AnimatorPrefab == null ? "NULL" : src.AnimatorPrefab.name) +
                " Skeleton=" + (src.Skeleton == null ? "NULL" : src.Skeleton.name) + ".");
        }

        // Finalizer, not Postfix: a postfix is SKIPPED when the patched method throws. This restore mutates
        // the SHARED MaleDoll source -- if CreateAvatar threw mid-build and the restore were skipped, every
        // human doll for the rest of the session would build on the marine rig (and the next Prefix would
        // overwrite the saved human fields, making it permanent). A finalizer runs on both exits.
        [HarmonyFinalizer]
        private static void Finalizer(Character originalAvatar)
        {
            if (!s_patched || originalAvatar == null) return;
            originalAvatar.AnimatorPrefab = s_savedAnimatorPrefab;
            originalAvatar.AnimationSet = s_savedAnimationSet;
            originalAvatar.Skeleton = s_savedSkeleton;
            originalAvatar.AtlasData = s_savedAtlasData;
            s_patched = false;
        }
    }

    // IN-GAME body (the playable marine). DollData.CreateUnitView builds the body on the shared HUMAN
    // MaleDoll Character (human AnimatorPrefab + AnimationSet) and only swaps the Skeleton — so without
    // this, the marine would animate on a human rig in gameplay. Mirror the doll-room fix at this
    // per-spawn seam: copy the real marine Character's animator/animationset/skeleton/atlas/scale onto
    // the freshly built body. Character.OnStart (which instantiates the animator) runs LATER at
    // view-attach, so these assignments take effect. Runs on every spawn AND every load/area change
    // (the view is rebuilt from serialized DollData each time). Gated to our marine RacePreset.
    [HarmonyPatch(typeof(DollData), nameof(DollData.CreateUnitView))]
    internal static class DollData_CreateUnitView_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(DollData __instance, UnitEntityView __result)
        {
            try
            {
                if (__instance == null || !DeathwatchModMain.IsMarinePreset(__instance.RacePreset)) return;
                if (__result == null) return;

                MarineDoll.EnsureMarineDoll();
                var src = MarineDoll.MarineDollCharacter;
                if (src == null) return;

                var ch = ((Component) __result).GetComponentInChildren<Character>(true);
                if (ch == null) return;

                ch.AnimatorPrefab = src.AnimatorPrefab;
                ch.Skeleton = src.Skeleton;
                ch.AnimationSet = src.AnimationSet;
                ch.AtlasData = src.AtlasData;
                ((Component) ch).transform.localScale = ((Component) src).transform.localScale;
                DeathwatchModMain.Log("[DollRig] In-game marine body: applied marine rig/skeleton/animset/scale.");
            }
            catch (Exception e) { DeathwatchModMain.LogError("[DollRig][ERR] DollData.CreateUnitView postfix", e); }
        }
    }

    // CHAPTER-FLIP RE-TEXTURE seam. CharGenDollRoom.UpdateDoll does NOT re-run when the player clicks a
    // different chapter (the doll's EE list is unchanged), but DollState.UpdateMechanicsEntities does. So
    // detect the chosen chapter here and re-apply the pauldron (+ helmet recolour) to the live cached doll
    // avatar, so the shoulder updates immediately on a chapter switch. Gated to the marine preset.
    [HarmonyPatch(typeof(DollState), "UpdateMechanicsEntities")]
    internal static class DollState_UpdateMechanicsEntities_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(DollState __instance, BaseUnitEntity unit)
        {
            try
            {
                if (unit == null || __instance == null || !DeathwatchModMain.IsMarinePreset(__instance.RacePreset)) return;
                MarineDoll.DollChapter = DeathwatchModMain.DetectChapter(unit);
                // CharGenDollRoom.UpdateDoll does NOT re-run on a chapter flip (the doll's EE list is
                // unchanged), but THIS seam does. Re-apply the pauldron to the live cached doll avatar so the
                // shoulder updates immediately when the player clicks a different chapter.
                if (MarineDoll.DollAvatar != null)
                    MarineBody.ApplyMarinePauldron(MarineDoll.DollAvatar, MarineDoll.DollChapter.GetValueOrDefault());
            }
            catch (Exception e) { DeathwatchModMain.LogError("[DollRig][ERR] DollState.UpdateMechanicsEntities postfix", e); }
        }
    }
}
