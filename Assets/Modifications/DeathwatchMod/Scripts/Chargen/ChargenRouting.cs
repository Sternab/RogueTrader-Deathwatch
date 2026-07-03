using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary, BlueprintCharGenRoot, BlueprintReferenceBase, BlueprintFeatureReference, BlueprintScriptableObject
using Kingmaker.Blueprints.Root;                  // NewGameRoot
using Kingmaker.Blueprints.Root.Strings;          // UIStrings, UICharGen
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.CharGen;               // CharGenContext, CharGenVM
using Kingmaker.UI.MVVM.VM.CharGen.Phases;        // CharGenPhaseType
using Kingmaker.UnitLogic.Progression.Features;   // BlueprintRace
using Kingmaker.UnitLogic.Levelup.Selections;     // AddFeaturesToLevelUp
using Kingmaker.UnitLogic.Levelup.CharGen;        // BlueprintRaceVisualPreset
using Kingmaker.UnitLogic.Progression.Paths;      // BlueprintOriginPath
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.Experience; // CharInfoExperienceVM
using Kingmaker.UnitLogic;                          // DollData, UnitHelper
using Kingmaker.ElementsSystem.ContextData;         // ContextData<UnitHelper.ChargenUnit>

namespace DeathwatchMod
{
    // Origin-path routing + the marine base-unit swap for the Custom Space Marine chargen.
    internal static class ChargenRouting
    {
        // ROADMAP #8 STEP 3 (label refresh). Live CharGenVM for the current chargen session, captured at its
        // ctor. A direct Custom Character <-> Custom Space Marine tile switch is null->null, which does NOT
        // rebuild the phase VMs (CharGenVM.UpdatePhases is gated on IsCustomCharacter changing), so the four tab
        // labels -- cached by value in each phase VM's PhaseName when it was built -- stay stale. We re-push them.
        internal static CharGenVM LiveCharGenVM;

        // Re-read GetPhaseName (our UICharGen.GetPhaseName postfix supplies DW or vanilla text per
        // CreatingDeathwatchMarine) and push it onto the existing phase VMs' PhaseName. The roadmap view
        // subscribes to PhaseName, so the tab text updates with no rebuild. Self-correcting both directions.
        internal static void RefreshPhaseLabels()
        {
            try
            {
                var vm = LiveCharGenVM;
                if (vm == null || vm.PhasesCollection == null) return;
                var ui = UIStrings.Instance.CharGen;   // Kingmaker.Blueprints.Root.Strings.UIStrings (already imported)
                foreach (var phase in vm.PhasesCollection)
                {
                    if (phase == null) continue;
                    switch (phase.PhaseType)           // public readonly field
                    {
                        case CharGenPhaseType.Homeworld:
                        case CharGenPhaseType.Occupation:
                        case CharGenPhaseType.MomentOfTriumph:
                        case CharGenPhaseType.DarkestHour:
                            phase.PhaseName.Value = ui.GetPhaseName(phase.PhaseType);
                            break;
                    }
                }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Tile][ERR] RefreshPhaseLabels", e); }
        }

        // The 4-group Deathwatch option-list swap on the CANONICAL NewGameCustomChargenPath. Run per chargen from
        // the GetOriginPath patch: marine -> swap to DW options here; human -> RestoreVanillaGroups. We mutate the
        // canonical (NOT a per-session copy) so the committed unit's origin path == the canonical object, which is
        // what the character sheet's reference-equality lookups need to resolve Homeworld/Origin (a deep copy broke
        // that). The original vanilla m_Features are captured once per group (s_vanillaGroups) so the human path can
        // be restored. Idempotent (skips a group already on DW).
        private static readonly Dictionary<string, BlueprintFeatureReference[]> s_vanillaGroups = new Dictionary<string, BlueprintFeatureReference[]>();
        internal static void ApplyDeathwatchGroups(BlueprintScriptableObject path)
        {
            if (path == null) return;
            FieldInfo mFeatures = AccessTools.Field(typeof(AddFeaturesToLevelUp), "m_Features");
            if (mFeatures == null) { DeathwatchModMain.LogError("[Path][ERR] AddFeaturesToLevelUp.m_Features not found."); return; }
            int swapped = 0;
            foreach (var comp in path.ComponentsArray)
            {
                var afl = comp as AddFeaturesToLevelUp;
                if (afl == null) continue;
                string[] guids;
                switch (afl.Group.ToString())
                {
                    case "ChargenHomeworld":                        // Chapters -- single source of truth: Chapters[] (alphabetical)
                        guids = DeathwatchModMain.Chapters.Select(c => c.FeatureGuid).ToArray(); break;
                    case "ChargenOccupation": guids = new[] {       // Specialities
                        "a072ea57e63e4ada9148d5cdbb2ce1c0", // Assault Marine
                        "5812c9cea10f4420b5915c9b0196a83b", // Apothecary
                        "6109110b5f3242828a3104f4b1180556", // Devastator Marine
                        "1ecda642d54c491a96ebf767cb12d7fb", // Tactical Marine
                        "7f897a85210b4c68a0420dd13ddd81af", // Techmarine
                        "8e283ed69c0d4f569a2cb0e7963362d7", // Librarian
                    }; break;
                    case "ChargenMomentOfTriumph": guids = new[] {  // Deeds of Renown
                        "a8f0c3a0d8014985b3295e2fa9f9c1a5", // Xenos Bane
                        "ba30d147720f4088aa0e2da54c717ca5", // Held the Breach
                        "20f3efd077344d05989d5e43e97b89ec", // Severed the Synapse
                        "58c024ed72fe460bafe95b8eecf184b8", // Relic Recovered
                        "e31f5378444d412ba4aee02cd124bcb4", // Witchfire Quenched
                        "cf53a350845a425d8398cccd9fa52e48", // Decapitation Strike
                        "ac4b80bb210d4889a2310dd7938218b0", // Last Brother Standing
                        "eb44334bd56f4e48ab9066b3b3786d0b", // Oath Kept in Ash
                    }; break;
                    case "ChargenDarkestHour": guids = new[] {      // Burdens of Service
                        "2371a2a7d41a4f1abe81ca7c479b4645", // Oath of Silence
                        "9851baa56eaa486eb7700fda81e13f64", // The Last Name on the Roll
                        "402aff5cc05543298c0a09f206611d2c", // Gene-Seed Debt
                        "0937a7b40d9a4dd2a3b7723ee09de172", // Censure of the Watch
                        "3f891b9d709e446380ce516680eabd06", // Scar of the Warp
                        "07af028ea38846c5920ae2e0626bda51", // The Beast Within
                        "e044e3d972e94e7f988b580818a57a83", // Machine Debt
                        "9403fbf2ae2a4c4db8c6d5730d53d48a", // Broken Chain of Command
                    }; break;
                    default: continue;
                }
                var cur = mFeatures.GetValue(afl) as BlueprintFeatureReference[];
                if (cur != null && cur.Length == guids.Length &&
                    cur.Length > 0 && cur[0] != null && cur[0].Guid == guids[0]) continue; // already these options
                // Per-PATH key: the NewGame + NewCompanion custom paths share group NAMES but differ in vanilla content
                // (Deed/Burden lists -- 24 vs 21), so a name-only key would cross-contaminate the two paths on restore.
                // Capture the CURRENT list as the restore snapshot EVERY time we swap (we only reach here when the
                // group is NOT already on DW options -- the continue above guards that): a one-shot capture went
                // stale when another runtime chargen mod added options after our first capture, and restoring the
                // stale snapshot silently deleted its additions.
                string vkey = path.AssetGuid + ":" + afl.Group.ToString();
                s_vanillaGroups[vkey] = cur;
                var refs = guids.Select(g => BlueprintReferenceBase.CreateTyped<BlueprintFeatureReference>(g)).ToArray();
                mFeatures.SetValue(afl, refs);
                swapped++;
            }
            DeathwatchModMain.Log("[Path] ApplyDeathwatchGroups: swapped " + swapped + " group(s) on " + (path != null ? path.name : "?") + ".");
        }

        // Restore the captured vanilla option lists onto the canonical path (for non-marine / human creation, and
        // on chargen close). The committed marine's sheet is unaffected: its selection is stored on the unit, keyed
        // by the canonical path object + the stable selection marker (RankEntries.Selections, which the m_Features
        // swap never touches), so the option list it currently holds is irrelevant to the lookup.
        internal static void RestoreVanillaGroups(BlueprintScriptableObject path)
        {
            if (path == null || s_vanillaGroups.Count == 0) return;
            FieldInfo mFeatures = AccessTools.Field(typeof(AddFeaturesToLevelUp), "m_Features");
            if (mFeatures == null) return;
            int restored = 0;
            foreach (var comp in path.ComponentsArray)
            {
                var afl = comp as AddFeaturesToLevelUp;
                if (afl == null) continue;
                if (!s_vanillaGroups.TryGetValue(path.AssetGuid + ":" + afl.Group.ToString(), out var vanilla)) continue;
                if ((mFeatures.GetValue(afl) as BlueprintFeatureReference[]) == vanilla) continue; // already vanilla
                mFeatures.SetValue(afl, vanilla);
                restored++;
            }
            if (restored > 0) DeathwatchModMain.Log("[Path] RestoreVanillaGroups: restored " + restored + " group(s) on " + (path != null ? path.name : "?") + ".");
        }
    }

    // Read-time setup hook: the New Game menu's StoryCampaigns getter runs once blueprints are loaded, a robust
    // place to prep the marine CREATOR's runtime bits -- the Spacemarine name list (so the Result phase can name a
    // marine) + EnsureChargenContent (the Ulfar chargen-voice preview). The mod ships as a STANDALONE character
    // creator now and no longer registers its own story campaign (the custom campaign is revisited later).
    [HarmonyPatch(typeof(NewGameRoot), nameof(NewGameRoot.StoryCampaigns), MethodType.Getter)]
    internal static class NewGameRoot_StoryCampaigns_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ChargenContent.EnsureMarineNames();
            ChargenContent.EnsureChargenContent();
        }
    }

    // CHANNEL 2 (doll preview race). For the custom path the doll's Race is set from the auto-selected
    // PORTRAIT's PortraitDollSettings.Race (-> human), NOT from the unit or CharacterRaces. After
    // SetPregenUnit builds the doll, force it to our Astartes race + marine preset. DollState.SetRace
    // turns OFF portrait-tracking, so the later auto-portrait selection can no longer revert Race to
    // human. This is a VISUAL doll change only (no Progression.SetRace), so it does NOT desync the
    // LevelUpManager / Result phase. Gated on CreatingDeathwatchMarine (the Custom Space Marine tile) -- the
    // SAME flag GetOriginPath uses, so the path-swap and the race/doll-swap can never disagree per-tile. The
    // flag is set by the HandleSetPregen prefix BEFORE SetUnit -> SetPregenUnit runs, so it is correct here.
    [HarmonyPatch(typeof(CharGenContext), nameof(CharGenContext.SetPregenUnit))]
    internal static class CharGenContext_SetPregenUnit_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(CharGenContext __instance, BaseUnitEntity unit)
        {
            try
            {
                if (!DeathwatchModMain.CreatingDeathwatchMarine) return;     // marine tile only
                if (unit != null) return;                                   // custom-character path only
                if (__instance.Doll == null) return;

                var race = ResourcesLibrary.BlueprintsCache.Load(DeathwatchModMain.AstartesRace_Guid) as BlueprintRace;
                var marinePreset = ResourcesLibrary.BlueprintsCache.Load(DeathwatchModMain.MarinePreset_Guid) as BlueprintRaceVisualPreset;
                if (race == null || marinePreset == null)
                {
                    DeathwatchModMain.LogError("[MarineUnit][ERR] SetPregenUnit: Astartes race/marine preset unresolved.");
                    return;
                }

                __instance.Doll.SetRace(race);              // also disables portrait-tracking of Race
                __instance.Doll.SetRacePreset(marinePreset);
                DeathwatchModMain.Log("[MarineUnit] Custom chargen doll -> Astartes race + marine preset.");

                // BARE FROM THE FIRST DOLL: seed the doll's mechanics EEs from the preview unit at chargen
                // init. The Deathwatch armour KEE now rides the 10 Chapter features (not AstartesPhysiology),
                // so at init -- before any chapter is picked -- the scan finds no armour and the doll is BARE
                // (race skin only) for the appearance phase. Picking a chapter re-runs this same call and the
                // armour appears. Writes only to DollState, never to TargetUnit, so commit is untouched.
                var lum = __instance.LevelUpManager != null ? __instance.LevelUpManager.Value : null;
                if (lum != null && lum.PreviewUnit != null)
                    __instance.Doll.UpdateMechanicsEntities(lum.PreviewUnit);
            }
            catch (Exception e) { DeathwatchModMain.LogError("[MarineUnit][ERR] SetPregenUnit postfix", e); }
        }
    }

    // Defensive guard for the Result/Summary phase blank-page crash. When chargen re-inits the
    // LevelUpManager (a second SetPregenUnit on the same context), the old preview unit is disposed and
    // the career path is re-applied, which fires a GLOBAL IUnitGainExperienceHandler event. A stale
    // CharInfoExperienceVM (still alive, still subscribed, but bound to the now-disposed preview unit)
    // then reads unit.Progression and throws "Can't access part PartUnitProgression of disposed unit",
    // which aborts the Result build -> blank page + stuck Apply. The engine's own RefreshData guards
    // Unit==null but NOT disposed; this prefix adds the missing disposed check so UpdateData is skipped
    // for a dead unit. Pure defensive no-op for normal play (a disposed unit has nothing to display).
    [HarmonyPatch(typeof(CharInfoExperienceVM), "UpdateData")]
    internal static class CharInfoExperienceVM_UpdateData_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CharInfoExperienceVM __instance)
        {
            var u = (__instance != null && __instance.Unit != null) ? __instance.Unit.Value : null;
            return u != null && !u.IsDisposed && !u.IsDisposingNow;   // false => skip when null/disposed
        }
    }

    // Chargen roadmap TAB LABELS. The tabs come from UICharGen.GetPhaseName(CharGenPhaseType). Rename the
    // four Deathwatch-repurposed steps to what they now select. Postfix-only -- the original vanilla
    // LocalizedStrings are left intact, so these words are not changed anywhere else they appear.
    [HarmonyPatch(typeof(UICharGen), nameof(UICharGen.GetPhaseName))]
    internal static class UICharGen_GetPhaseName_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(CharGenPhaseType type, ref string __result)
        {
            if (!DeathwatchModMain.CreatingDeathwatchMarine) return;   // ROADMAP #8 STEP 5: Custom Space Marine only; human chargen keeps vanilla tab labels
            switch (type)   // localized via the mod pack (Localization\enGB.json); English fallback if the key is missing
            {
                case CharGenPhaseType.Homeworld:       __result = DeathwatchModMain.ModString("DW.Chargen.Phase.Homeworld", "Chapter"); break;
                case CharGenPhaseType.Occupation:      __result = DeathwatchModMain.ModString("DW.Chargen.Phase.Occupation", "Speciality"); break;
                case CharGenPhaseType.MomentOfTriumph: __result = DeathwatchModMain.ModString("DW.Chargen.Phase.MomentOfTriumph", "Deed of Renown"); break;
                case CharGenPhaseType.DarkestHour:     __result = DeathwatchModMain.ModString("DW.Chargen.Phase.DarkestHour", "Burden of Service"); break;
            }
        }
    }

    // ROADMAP #8: route a Custom Space Marine's chargen onto the Deathwatch option groups -- on the CANONICAL
    // NewGameCustomChargenPath, NOT a copy. The earlier per-session deep copy kept human creation clean but BROKE
    // the in-game character sheet: it resolves Homeworld/Origin by REFERENCE equality against the canonical path
    // (CharGenUtility.GetUnitOriginPath / Progression.GetSelectedFeature), and the copy -- same GUID, different
    // object -- never matched, blanking both. Fix: swap the canonical's 4 option groups per chargen (marine -> DW,
    // human -> vanilla restore) so the unit commits on the canonical object. The option DISPLAY follows the same
    // swap (the phase reads the canonical's m_Features), so no separate filter is needed; the marine's committed
    // selection (stored on the unit, keyed by the canonical + the stable RankEntries selection marker) resolves on
    // the sheet. RestoreVanillaGroups also runs on chargen close so the canonical idles as vanilla.
    [HarmonyPatch(typeof(CharGenContext), "GetOriginPath")]
    internal static class CharGenContext_GetOriginPath_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(BlueprintOriginPath __result)
        {
            try
            {
                if (__result == null ||
                    (__result != BlueprintCharGenRoot.Instance.NewGameCustomChargenPath &&
                     __result != BlueprintCharGenRoot.Instance.NewCompanionCustomChargenPath)) return; // custom paths only (player NewGame + mercenary NewCompanion)
                if (DeathwatchModMain.CreatingDeathwatchMarine)
                    ChargenRouting.ApplyDeathwatchGroups(__result);   // canonical -> Deathwatch options
                else
                    ChargenRouting.RestoreVanillaGroups(__result);    // canonical -> vanilla options
            }
            catch (Exception e) { DeathwatchModMain.LogError("[Path][ERR] GetOriginPath", e); }
        }
    }

    // ROADMAP #8 LEAK FIX: the Custom Space Marine builds its chargen unit from a dedicated marine blueprint
    // (DW_Marine_Player_Unit -- Astartes race + armour baked in) instead of the shared DefaultPlayerCharacter, so
    // marine-ness never touches the shared base unit (which leaked armour onto vanilla human creation and shrank
    // pre-existing saves; kept as data-forward as the engine allows). The custom-character chargen uses CharGenConfig.Unit
    // as its base (CharGenContext.SetPregenUnit, custom path = unit==null -> BaseChargenUnit). We swap that
    // (readonly field, reflection) to a fresh marine-blueprint unit while the marine tile is active, and restore
    // the original for the vanilla "Create Custom Character" tile. Astartes FROM CREATION -> no mid-chargen
    // Progression.SetRace (which desyncs the LevelUpManager). Reset between sessions via the flag-reset postfixes.
    internal static class MarineUnitRouter
    {
        private const string MarinePlayerUnit_Guid = "f6f76360d9fb4f5dac5967b40329d9c9";
        private static readonly FieldInfo s_cfgUnit = AccessTools.Field(typeof(CharGenConfig), "Unit");
        private static BaseUnitEntity s_humanUnit;   // the original DefaultPlayerCharacter-built config unit
        private static BaseUnitEntity s_marineUnit;  // a marine-blueprint-built unit

        // Drop references only -- do NOT dispose. On a successful commit the marine unit IS the committed game
        // unit (Game.NewGameUnit), so disposing it here would destroy the player; on cancel the chargen's own
        // unit cleanup handles it. (Worst case a cancelled marine chargen orphans one unit -- negligible.)
        internal static void Reset()
        {
            s_humanUnit = null;
            s_marineUnit = null;
        }

        [HarmonyPatch(typeof(CharGenContext), nameof(CharGenContext.SetPregenUnit))]
        internal static class CharGenContext_SetPregenUnit_SwapConfigUnit_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(CharGenContext __instance, BaseUnitEntity unit)
            {
                try
                {
                    if (unit != null) return;                          // a real pregen tile -- not the custom path
                    var cfg = __instance.CharGenConfig;
                    if (cfg == null || s_cfgUnit == null) return;
                    if (DeathwatchModMain.CreatingDeathwatchMarine)
                    {
                        if (s_humanUnit == null) s_humanUnit = cfg.Unit;            // remember the original human unit
                        if (s_marineUnit == null || s_marineUnit.IsDisposed) s_marineUnit = CreateMarineUnit();
                        if (s_marineUnit != null && cfg.Unit != s_marineUnit) s_cfgUnit.SetValue(cfg, s_marineUnit);
                    }
                    else if (s_humanUnit != null && cfg.Unit != s_humanUnit)
                    {
                        s_cfgUnit.SetValue(cfg, s_humanUnit);                        // restore for the vanilla custom tile
                    }
                }
                catch (Exception e) { DeathwatchModMain.LogError("[MarineUnit][ERR] swap", e); }
            }
        }

        private static BaseUnitEntity CreateMarineUnit()
        {
            var bp = ResourcesLibrary.BlueprintsCache.Load(MarinePlayerUnit_Guid) as BlueprintUnit;
            if (bp == null) { DeathwatchModMain.Log("[MarineUnit] marine unit blueprint NOT FOUND (" + MarinePlayerUnit_Guid + ") -- marine falls back to the human base unit."); return null; }
            BaseUnitEntity u;
            using (ContextData<UnitHelper.ChargenUnit>.Request()) { u = bp.CreateEntity(null, true); }
            u.AttachToViewOnLoad(null);
            DeathwatchModMain.Log("[MarineUnit] built marine chargen unit from " + bp.name + ".");
            return u;
        }
    }
}
