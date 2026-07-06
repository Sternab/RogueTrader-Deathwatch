using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary, BlueprintReferenceBase, BlueprintScriptableObject
using Kingmaker.Blueprints.Root;                  // BlueprintRoot, ProgressionRoot, PregenCharacterNames
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
                if (field == null) { DeathwatchModMain.LogError("[Names][ERR] PregenCharacterNames.m_CharacterNames not found."); return; }

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
            catch (Exception e) { DeathwatchModMain.LogError("[Names][ERR] EnsureMarineNames", e); }
        }

        // Residual runtime chargen prep. NOTE the option-group swap does NOT live here: the 4 Deathwatch
        // groups are swapped IN PLACE on the CANONICAL custom origin path per session (a copy broke the
        // sheet's reference-equality lookups) -- see ChargenRouting.ApplyDeathwatchGroups, driven by
        // CharGenContext_GetOriginPath_Patch in ChargenRouting, restored for humans on close. The chargen
        // content itself (homeworld talents, speciality occupations, Librarian psyker wiring, the
        // SpaceMarineImmunity name, the weapon unlocks) is all DATA on the DW blueprints/patches or a
        // per-unit patch (EquipmentRestrictionHasFacts_CanBeEquippedBy_MarineGear_Patch in MarineEquipment).
        internal static void EnsureChargenContent()
        {
            try
            {
                // Make Ulfar's voice selectable in chargen (the m_Voices append + DisplayName are data).
                EnsureUlfarPreviewSound();
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Content][ERR] EnsureChargenContent", e); }
        }

        // ULFAR CHARGEN VOICE. Ulfar_Barks (3ea153cb) ships with PreviewSound="" and the voice selector CULLS
        // empty-preview voices (CharGenVoiceItemVM.IsEmptyVoice), so appending it to CharGenRoot.m_Voices (data,
        // CharGenRoot_PortraitsAndVoices.jbp_patch) and naming it (Ulfar_Barks_ChargenVoice.jbp_patch) is not enough.
        // PreviewSound is a component field the .jbp_patch FieldOverrides cannot reach (no array indexing into
        // Components), so this one field is set at runtime. THE EVENT: the 12 vanilla chargen voices use dedicated
        // 2D demo events in PC_DemoVoices_ENG.bnk; Ulfar has none, and his regular companion-bark events are
        // routed through game buses that render SILENT in the chargen preview context (verified in-game: the
        // borrowed Selected event never played, from the doll room OR the listener). So the preview is
        // Play_DW_Ulfar_Preview -- a demo event ADDED to our own DX_Ask_DWMarine.bnk (proven-audible Master-bus
        // routing, cloned from a DW sound object) that STREAMS Owlcat's own Ulfar select-line media
        // (Media\269340113.wem) untouched. Bank surgery documented in the bank's commit (a3261fd follow-up).
        private const string UlfarPreviewEvent = "Play_DW_Ulfar_Preview";

        private static void EnsureUlfarPreviewSound()
        {
            if (s_ulfarVoiceReady) return;
            var asks = ResourcesLibrary.BlueprintsCache.Load("3ea153cb4f714f1798572e89c7cbd1e9") as BlueprintUnitAsksList;
            if (asks == null) { DeathwatchModMain.LogError("[Voices][ERR] Ulfar_Barks blueprint not found."); return; }
            UnitAsksComponent comp = null;
            foreach (var c in asks.ComponentsArray) { comp = c as UnitAsksComponent; if (comp != null) break; }
            if (comp == null) { DeathwatchModMain.LogError("[Voices][ERR] Ulfar_Barks has no UnitAsksComponent."); return; }
            if (comp.PreviewSound == UlfarPreviewEvent) { s_ulfarVoiceReady = true; return; }   // idempotent

            comp.PreviewSound = UlfarPreviewEvent;
            s_ulfarVoiceReady = true;
            DeathwatchModMain.Log("[Voices] Ulfar chargen voice: preview sound set to " + UlfarPreviewEvent + ".");
        }

        // (The force-sword unlock used to live here as a runtime strip of the Astartes marker from the 16 force
        // swords' BLUEPRINTS -- a global change that also unlocked them for vanilla Ulfar/Uralon, caught at the
        // release gate. It is now the per-unit EquipmentRestrictionHasFacts_CanBeEquippedBy_MarineGear_Patch in
        // Gameplay\MarineEquipment.cs -- since generalized to all non-saw 2H melee + plasma/flame ranged + capes --
        // scoped to this mod's marine; the vanilla exclusion stays intact.)
    }

    // (Two retired patch families once lived here: the Blade Dancer chargen-list cut -- REMOVED 2026-07-06,
    // James wants the archetype selectable even though its moveset animates poorly on the Astartes skeleton;
    // the README Known Issues documents it instead. And a UnitAsksComponent.PlayPreview camera-repost patch
    // for the Ulfar preview -- retired same-day it shipped: the silence was bus routing, not distance, so the
    // fix moved into the bank itself (the Play_DW_Ulfar_Preview demo event in DX_Ask_DWMarine.bnk; see
    // EnsureUlfarPreviewSound above).)
}
