using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary, BlueprintReferenceBase, BlueprintScriptableObject
using Kingmaker.Blueprints.Root;                  // BlueprintRoot, ProgressionRoot, PregenCharacterNames
using Kingmaker.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.Careers; // UnitProgressionVM
using Kingmaker.UnitLogic.Progression.Paths;      // BlueprintCareerPath
using Kingmaker.Visual.Sound;                     // BlueprintUnitAsksList, UnitAsksComponent (Ulfar chargen voice)

namespace DeathwatchMod
{
    // CHARGEN CONTENT PREP: the two residual RUNTIME steps -- the Spacemarine name list (so the Result phase can
    // name a marine) and the Ulfar chargen-voice preview. Everything else the creator needs is DATA on the
    // DW-owned blueprints and the registered .jbp_patches (see Blueprints\PATCHES.md).
    internal static class ChargenContent
    {
        // The Summary/Result phase generates a default+random character name via
        // PregenCharacterNames.GetNamesList(Doll.Race.RaceId, Gender, Mode). There is NO name list for
        // Race.Spacemarine, so GetNamesList finds nothing -> passes null to GetNamesFromStringExcept ->
        // null.Split() NullReferenceException -> the Summary build throws -> blank Result + stuck Apply.
        // (This is the REAL Result blocker; the later "disposed unit" spam is just teardown fallout.)
        // Owlcat-proper fix = give the Spacemarine race a name list. We clone the Human entries (Imperial
        // human names suit Astartes) for every Gender/Mode. Read-time, idempotent, global.
        // One-shot flags: the blueprint edits below persist for the game's lifetime, but the methods run from
        // the NewGameRoot.StoryCampaigns getter, which fires repeatedly. Set true only on the confirmed-done
        // paths, so the reflection + Linq scan runs once and a not-yet-loaded first call still retries.
        private static bool s_marineNamesReady;
        private static bool s_ulfarVoiceReady;

        internal static void EnsureMarineNames()
        {
            if (s_marineNamesReady) return;
            try
            {
                var root = BlueprintRoot.Instance;
                var pcn = (root != null && root.CharGenRoot != null) ? root.CharGenRoot.PregenCharacterNames : null;
                if (pcn == null) return;

                FieldInfo field = AccessTools.Field(typeof(PregenCharacterNames), "m_CharacterNames");
                if (field == null) { DeathwatchModMain.Log("[Names][ERR] PregenCharacterNames.m_CharacterNames not found."); return; }

                var list = field.GetValue(pcn) as System.Collections.Generic.List<PregenCharacterNameList>;
                if (list == null) return;
                if (list.Any(e => e != null && e.Race == Kingmaker.Blueprints.Race.Spacemarine)) { s_marineNamesReady = true; return; } // already added

                var humans = list.Where(e => e != null && e.Race == Kingmaker.Blueprints.Race.Human).ToList();
                int added = 0;
                foreach (var h in humans)
                {
                    list.Add(new PregenCharacterNameList
                    {
                        Race = Kingmaker.Blueprints.Race.Spacemarine,
                        Gender = h.Gender,
                        CharGenMode = h.CharGenMode,
                        NameList = h.NameList
                    });
                    added++;
                }
                s_marineNamesReady = true;
                DeathwatchModMain.Log("[Names] Added " + added + " Spacemarine name list(s) (cloned from Human).");
            }
            catch (Exception e) { DeathwatchModMain.Log("[Names][ERR] EnsureMarineNames: " + e); }
        }

        // Residual runtime chargen prep. NOTE the option-group swap does NOT live here: the 4 Deathwatch
        // groups are swapped IN PLACE on the CANONICAL custom origin path per session (a copy broke the
        // sheet's reference-equality lookups) -- see ChargenRouting.ApplyDeathwatchGroups, driven by
        // CharGenContext_GetOriginPath_Patch in ChargenRouting, restored for humans on close. The chargen
        // content itself (homeworld talents, speciality occupations, Librarian psyker wiring, the
        // SpaceMarineImmunity name, the force-sword unlock) is all DATA on the DW blueprints/patches or a
        // per-unit patch (EquipmentRestrictionHasFacts_CanBeEquippedBy_ForceSword_Patch in MarineEquipment).
        internal static void EnsureChargenContent()
        {
            try
            {
                // Make Ulfar's voice selectable in chargen (the m_Voices append + DisplayName are data).
                EnsureUlfarPreviewSound();
            }
            catch (Exception e) { DeathwatchModMain.Log("[Content][ERR] EnsureChargenContent: " + e); }
        }

        // ULFAR CHARGEN VOICE. Ulfar_Barks (3ea153cb) ships with PreviewSound="" and the voice selector CULLS
        // empty-preview voices (CharGenVoiceItemVM.IsEmptyVoice), so appending it to CharGenRoot.m_Voices (data,
        // CharGenRoot_PortraitsAndVoices.jbp_patch) and naming it (Ulfar_Barks_ChargenVoice.jbp_patch) is not enough.
        // PreviewSound is a component field the .jbp_patch FieldOverrides cannot reach (no array indexing into
        // Components), so this one field is set at runtime -- taken from the voice's own first Selected bark, so
        // no event name is hardcoded and a game patch to Ulfar's barks cannot silently break the preview.
        private static void EnsureUlfarPreviewSound()
        {
            if (s_ulfarVoiceReady) return;
            var asks = ResourcesLibrary.BlueprintsCache.Load("3ea153cb4f714f1798572e89c7cbd1e9") as BlueprintUnitAsksList;
            if (asks == null) { DeathwatchModMain.Log("[Voices][ERR] Ulfar_Barks blueprint not found."); return; }
            UnitAsksComponent comp = null;
            foreach (var c in asks.ComponentsArray) { comp = c as UnitAsksComponent; if (comp != null) break; }
            if (comp == null) { DeathwatchModMain.Log("[Voices][ERR] Ulfar_Barks has no UnitAsksComponent."); return; }
            if (!string.IsNullOrEmpty(comp.PreviewSound)) { s_ulfarVoiceReady = true; return; }   // already set (idempotent; or a future game fix)

            var entries = comp.Selected != null ? comp.Selected.Entries : null;
            string ev = (entries != null && entries.Length > 0 && entries[0] != null) ? entries[0].AkEvent : null;
            if (string.IsNullOrEmpty(ev)) { DeathwatchModMain.Log("[Voices][ERR] Ulfar_Barks has no Selected bark to use as a preview."); return; }
            comp.PreviewSound = ev;
            s_ulfarVoiceReady = true;
            DeathwatchModMain.Log("[Voices] Ulfar chargen voice: preview sound set to " + ev + ".");
        }

        // (The force-sword unlock used to live here as a runtime strip of the Astartes marker from the 16 force
        // swords' BLUEPRINTS -- a global change that also unlocked them for vanilla Ulfar/Uralon, caught at the
        // release gate. It is now the per-unit EquipmentRestrictionHasFacts_CanBeEquippedBy_ForceSword_Patch in
        // Gameplay\MarineEquipment.cs, scoped to this mod's marine; the vanilla exclusion stays intact.)
    }

    // ROADMAP #10: remove the "Blade Dancer" archetype (career-path dd6948ee) from a Deathwatch marine's chargen
    // career list, PER CHARACTER. The list is UnitProgressionVM.m_AllCareerPaths, rebuilt in RefreshData from the
    // GLOBAL ProgressionRoot.CareerPaths getter on every CurrentUnit change. We deliberately do NOT key off the
    // global CreatingDeathwatchMarine flag: it gates a marine SESSION, not a unit, so a non-marine built afterwards
    // in the same session (e.g. a human MERCENARY hired after a marine main char) could be wrongly affected.
    // Instead we decide marine-ness from THIS build's unit -- specifically LevelUpManager.PreviewUnit, the clone
    // that carries the in-progress chapter feature during chargen (CurrentUnit/TargetUnit only get it at Commit).
    //   PREFIX:  s_filteringForMarine = IsCharGen && chapter feature present on PreviewUnit
    //   GETTER POSTFIX: drop Blade Dancer ONLY while that flag is set (i.e. only while a marine's list is built)
    //   FINALIZER: clear the flag so it can never leak to an unrelated CareerPaths read (even on throw).
    // Humans (fresh, or a merc/companion after a marine) -> chapter feature absent -> flag false -> full list.
    // In-game sheet / level-up -> IsCharGen false -> flag never set -> getter untouched.
    [HarmonyPatch(typeof(UnitProgressionVM), "RefreshData")]
    internal static class UnitProgressionVM_CutBladeDancer_Patch
    {
        [ThreadStatic] internal static bool s_filteringForMarine;
        private static int s_lastState = -1;   // edge-triggered: log only when the per-unit Blade-Dancer decision flips

        [HarmonyPrefix]
        private static void Prefix(UnitProgressionVM __instance)
        {
            try
            {
                bool marine = __instance != null
                    && __instance.IsCharGen
                    && DeathwatchModMain.IsDeathwatchMarinePreview(
                           __instance.LevelUpManager != null ? __instance.LevelUpManager.PreviewUnit : null);
                s_filteringForMarine = marine;
                int st = marine ? 1 : 0;
                if (st != s_lastState) { s_lastState = st; DeathwatchModMain.Log("[CutBladeDancer] RefreshData; filterForMarine=" + marine); }
            }
            catch (Exception e) { DeathwatchModMain.Log("[CutBladeDancer][ERR] RefreshData prefix: " + e); }
        }

        // Finalizer, not Postfix: a postfix is SKIPPED if RefreshData throws, which would leave the GLOBAL
        // CareerPaths getter filtering Blade Dancer for every later read. A finalizer runs on both exits.
        [HarmonyFinalizer]
        private static void Finalizer() { s_filteringForMarine = false; }   // never leak the scoped decision
    }

    // Getter filter, scoped PER CHARACTER by the prefix above: drops Blade Dancer from the sequence ONLY while a
    // marine unit's RefreshData is running. RefreshData is straight-line synchronous between the getter read and
    // the postfix, so the [ThreadStatic] scope holds; outside it every other caller sees the full list.
    [HarmonyPatch(typeof(ProgressionRoot), nameof(ProgressionRoot.CareerPaths), MethodType.Getter)]
    internal static class ProgressionRoot_CutBladeDancer_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref System.Collections.Generic.IEnumerable<BlueprintCareerPath> __result)
        {
            try
            {
                if (!UnitProgressionVM_CutBladeDancer_Patch.s_filteringForMarine || __result == null) return;
                __result = __result.Where(cp => cp == null || cp.AssetGuid != DeathwatchModMain.BladeDancerCareerPath_Guid).ToArray();
            }
            catch (Exception e) { DeathwatchModMain.Log("[CutBladeDancer][ERR] CareerPaths: " + e.Message); }   // Message only: hot path
        }
    }
}
