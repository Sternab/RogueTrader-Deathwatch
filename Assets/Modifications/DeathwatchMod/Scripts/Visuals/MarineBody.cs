using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints.Base;                  // Gender
using Kingmaker.UnitLogic.Levelup.Selections.Doll;// DollState
using Kingmaker.Visual.CharacterSystem;           // Character, EquipmentEntity, KingmakerEquipmentEntity, BodyPartType
using Kingmaker.ResourceLinks;                     // EquipmentEntityLink

namespace DeathwatchMod
{
    // Per-chapter body visuals + the marine-bare / male-only / helmet-toggle body patches.
    internal static class MarineBody
    {
        // Per-chapter pauldron OVERLAY for the CHARGEN PREVIEW doll. The chapter-feature AddKingmakerEquipmentEntity
        // delivers the pauldron EE in-game, but no-ops for the preview (the preview unit has no spawned View, so
        // AddKingmakerEquipmentEntity.TryGetCharacter bails). So mirror it here: drop any chapter pauldron EE already
        // on the doll (handles a chapter flip), then add the chosen chapter's, onto the cached avatar. Idempotent.
        // Chargen-only -- the in-game body gets the pauldron from the real AddKingmakerEquipmentEntity.
        internal static void ApplyMarinePauldron(Character ch, DeathwatchModMain.ChapterVisual cv)
        {
            try
            {
                if (ch == null) return;
                var known = DeathwatchModMain.Chapters.Where(c => !string.IsNullOrEmpty(c.PauldronEEGuid))
                    .Select(c => new Kingmaker.ResourceLinks.EquipmentEntityLink { AssetId = c.PauldronEEGuid }).ToArray();
                if (known.Length > 0) ch.RemoveEquipmentEntities(known, false);   // clear any previous chapter's badge
                if (!string.IsNullOrEmpty(cv.PauldronEEGuid))
                    ch.AddEquipmentEntities(new[] { new Kingmaker.ResourceLinks.EquipmentEntityLink { AssetId = cv.PauldronEEGuid } }, false);
                ch.IsDirty = true;
                DeathwatchModMain.LogDebug("[Pauldron] preview pauldron EE -> " + (cv.PauldronEEGuid ?? "none(base)") + ".");
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Pauldron][ERR] ApplyMarinePauldron", e); }
        }
    }

    // Lock the marine to MALE. The marine race's "female" preset reuses the MaleSpaceMarine skeleton
    // (SpaceMarine_Standard_VisualPreset FemaleSkeleton == MaleSkeleton), so switching to female breaks the doll.
    // Gated to our marine preset; forces any non-male selection back to Male, so the gender toggle is inert for the
    // marine while leaving every other race untouched.
    [HarmonyPatch(typeof(DollState), nameof(DollState.SetGender))]
    internal static class DollState_SetGender_MarineMaleOnly_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(DollState __instance, ref Gender gender)
        {
            try
            {
                if (__instance != null && DeathwatchModMain.IsMarinePreset(__instance.RacePreset) && gender != Gender.Male)
                    gender = Gender.Male;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Gender][ERR] SetGender", e); }
        }
    }

    // CHARGEN HELMET TOGGLE for our feature-EE helmet. The helmet ships in the feature-EE
    // (AddKingmakerEquipmentEntity), so it is never slot-mapped into Character.m_EquipmentEntityToSlot.
    // The stock helmet show/hide toggle hides helmets only through ShouldHideSlot,
    // which needs that slot map -- so flipping DollState.ShowHelm does NOTHING to our helmet (a non-slot
    // EE falls through to the ShowAboveAllIgnoreLayer fallback). The toggle DOES set
    // Character.m_ShowHelmet (CharGenDollRoom.UpdateHelmetVisibility). So: postfix the EE-level
    // gate (ShouldHideEquipmentEntity, the per-EE cull) to ALSO report our helmet hidden
    // when m_ShowHelmet is false. Match the helmet by its Helmet BodyPart + ee.name containing "Helmet"
    // -- a runtime EE has no GUID, only ScriptableObject.name; the name check
    // also excludes hair (it sits in the Helmet slot but is named "EE_Hair..."). Slot-mapped helmets
    // (normal companions) already carry a correct __result, so the early-outs skip them; this only flips
    // non-slot feature-EE helmets when helmets are toggled OFF (the chargen toggle, and the in-game global
    // Show-Helmet setting -- both the desired "hide it"). Hot path: name check first (cheap, fails for
    // most EEs), reflection only for helmet-named EEs.
    [HarmonyPatch(typeof(Character), "ShouldHideEquipmentEntity")]
    internal static class Character_ShouldHideEquipmentEntity_Patch
    {
        private static readonly FieldInfo s_showHelmet = AccessTools.Field(typeof(Character), "m_ShowHelmet");

        [HarmonyPostfix]
        private static void Postfix(Character __instance, EquipmentEntity entity, ref bool __result)
        {
            try
            {
                if (__result || entity == null || __instance == null || s_showHelmet == null) return;
                if (entity.name == null || entity.name.IndexOf("Helmet", StringComparison.OrdinalIgnoreCase) < 0) return;
                if ((bool) s_showHelmet.GetValue(__instance)) return;        // helmet shown -> leave as-is
                if (entity.BodyParts == null) return;
                foreach (var bp in entity.BodyParts)
                    if (bp != null && bp.Type == BodyPartType.Helmet) { __result = true; return; }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Helmet][ERR] ShouldHideEquipmentEntity", e); }
        }
    }

    // BARE MARINE during the appearance phase. The Deathwatch armour KEE now rides the 10 Chapter features
    // (moved off AstartesPhysiology), so before a chapter is picked the doll has no armour EE. But
    // DollState.GetClothes ALWAYS falls back to the vanilla human chargen garment
    // (Root.MaleClothes) when m_EquipmentEntities is empty -- even ShowCloth=false still hits that return. So
    // without this, the pre-chapter marine would wear human rags, not be bare. Postfix GetClothes: for our
    // marine doll, when no armour EE is present, blank the result so only the preset m_Skin body
    // (EE_Body01_M_SM) shows = bare. When armour IS present (post-chapter) GetClothes already returns it, so
    // leave __result alone. Gated to the marine RacePreset so other races/dolls are untouched.
    [HarmonyPatch(typeof(DollState), "GetClothes")]
    internal static class DollState_GetClothes_MarineBare_Patch
    {
        private static readonly FieldInfo s_eqEntities = AccessTools.Field(typeof(DollState), "m_EquipmentEntities");

        [HarmonyPostfix]
        private static void Postfix(DollState __instance, ref IEnumerable<EquipmentEntityLink> __result)
        {
            try
            {
                if (__instance == null || !DeathwatchModMain.IsMarinePreset(__instance.RacePreset)) return;
                var ees = s_eqEntities != null ? s_eqEntities.GetValue(__instance) as IEnumerable<KingmakerEquipmentEntity> : null;
                bool hasArmour = ees != null && ees.Any(e => e != null);
                if (!hasArmour) __result = Array.Empty<EquipmentEntityLink>();
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Bare][ERR] GetClothes", e); }
        }
    }
}
