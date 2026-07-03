// =====================================================================================================
// Deathwatch Mod -- turn Rogue Trader's character creator into "create and play a Deathwatch Space Marine".
//
// ONE Harmony assembly, gated by ONE flag: DeathwatchModMain.CreatingDeathwatchMarine -- true only while the
// "Custom Space Marine" pregen tile is selected (set in the HandleSetPregen prefix; reset on chargen
// open/close). Plain human character creation never sees any of this.
//
// Four subsystems:
//   1. CHARGEN ROUTING    -- the marine tile (DwMarineSelectorItemVM); the per-session swap of the canonical
//      origin path's 4 option groups between Deathwatch and vanilla (ApplyDeathwatchGroups /
//      RestoreVanillaGroups); the Astartes race + marine base unit (MarineUnitRouter); renamed tab labels.
//   2. CHARGEN CONTENT    -- almost entirely DATA: the DW-owned blueprints (DW_Chapter_*, DW_Speciality_*,
//      DW_Deed_*, DW_Burden_*, DW_AstartesBaseline) + the registered .jbp_patches (see Blueprints\PATCHES.md)
//      carry the content; the runtime residue is the Spacemarine name list + the Ulfar voice preview
//      (ChargenContent) and the per-unit force-sword allow (MarineEquipment).
//   3. DOLL + BODY RENDER -- builds the chargen doll and the in-game body on a real MARINE rig (not the human
//      MaleDoll); keeps the marine bare until a chapter is chosen; toggles the feature-EE helmet.
//   4. PER-CHAPTER VISUALS -- the chosen chapter's pauldron (an additive EE overlay) + its baked Deathwatch
//      armour item, equipped at commit so the livery rides the worn armour.
//
// DATA vs RUNTIME: changes to DW-owned blueprints are authored as data; edits to VANILLA blueprints and the
// per-session path swap stay runtime Harmony (a migration plan for the rest lives in the project notes).
// Reflection (AccessTools / string-name lookups) is used throughout because the referenced Code.dll lags the
// fresh decompile and member visibility drifts between the two.
//
// FILE LAYOUT: this file is the shared HUB (entry point + widely-shared statics). The ~28 [HarmonyPatch]
// classes and their single-concern helpers live in concern files under Chargen\, Visuals\, Gameplay\ (the
// asmdef compiles Scripts\ recursively, so PatchAll(assembly) still finds every patch class).
// =====================================================================================================
using System;                                     // Exception
using System.IO;                                  // Path, Directory (mod voice-bank base path)
using System.Reflection;
using HarmonyLib;
using Kingmaker.Modding;                          // OwlcatModification, OwlcatModificationEnterPoint
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UnitLogic.Levelup.CharGen;        // BlueprintRaceVisualPreset

namespace DeathwatchMod
{
    public static class DeathwatchModMain
    {
        internal const string MarinePreset_Guid   = "d68a2e2af8c0df3aaed55e6ad4910ddc";
        // Playable Astartes race (clone of HumanRace) carrying Size=Large + AstartesPhysiology + marine preset.
        internal const string AstartesRace_Guid   = "2302e1d517f847e6aef04c8c4a24d598";
        internal const string CombatSizeLargeBuff_Guid = "dccb5117000000000000000000000001";   // DW_CombatSize_Large (dynamic-size toggle)
        // ROADMAP #10: "Blade Dancer" archetype career path (DLC2 Lex Imperialis; its moveset animates wrong on
        // an Astartes) -- removed from the Custom Space Marine chargen career list (ProgressionRoot.CareerPaths).
        internal const string BladeDancerCareerPath_Guid = "dd6948ee596346a69733d0bb107c2f42";
        // ROADMAP #8: true while the player is creating a "Custom Space Marine" (the new Pregen tile). When set,
        // the GetOriginPath postfix swaps the 4 option groups on the CANONICAL custom path to Deathwatch content
        // IN PLACE (not a copy -- a copy broke the in-game sheet's reference-equality lookups; see
        // CharGenContext_GetOriginPath_Patch). When false, the canonical path is restored to vanilla, so plain
        // human creation is untouched. Set on tile-select, cleared on complete/cancel.
        internal static bool CreatingDeathwatchMarine;

        // The Astartes baseline is DATA: an AddFacts component on each DW_Chapter_*.jbp grants the baseline
        // facts, riding the chapter selection because the engine never auto-applies race.m_Features. The
        // canonical-path option-group swap lives in ChargenRouting (ApplyDeathwatchGroups / RestoreVanillaGroups).

        public static OwlcatModification Modification { get; private set; }

        // === Per-chapter chapter visuals (pauldron + armour) ===
        // Each chapter shows its livery two ways: (1) a per-chapter pauldron EE additively overlaid on the
        // doll/body (PauldronEEGuid), and (2) a per-chapter baked Deathwatch armour ITEM (ArmorItemGuid),
        // equipped on the committed marine so the pauldron rides the worn armour (and comes off when it is
        // unequipped). Blood Angels uses the base spaulder, so it has no PauldronEEGuid overlay. The chargen
        // PREVIEW doll gets the pauldron via ApplyMarinePauldron (MarineBody); the in-game body gets it from the
        // chapter feature's own AddKingmakerEquipmentEntity, and the equipped armour item is swapped in at
        // commit (CharGenContextVM_CompleteCharGen_MarineArmour_Patch). (This replaced an earlier runtime
        // diffuse-texture swap on the shared spaulder EE.)
        internal struct ChapterVisual { public string FeatureGuid; public string PauldronEEGuid; public string ArmorItemGuid; }
        internal static readonly ChapterVisual[] Chapters =
        {
            new ChapterVisual { FeatureGuid = "d02ff6ea509442b3b0b2e97c23b267b3", PauldronEEGuid = "3f2356c4d8286394688cc508a118f25f", ArmorItemGuid = "225fc3af09194f9fbcecc49abb93554b" }, // Black Templars
            new ChapterVisual { FeatureGuid = "03376c672f294f13aeac98740ec61f56", ArmorItemGuid = "82b928822dce482e8e37c677ef97f856" },                                                      // Blood Angels = base spaulder, no overlay
            new ChapterVisual { FeatureGuid = "7e135a087189433cbd321568c989154e", PauldronEEGuid = "6cd5344cb762b594cbf98cc2709942f7", ArmorItemGuid = "f3a19e91fdfa4673be3731007ff62bb2" }, // Dark Angels
            new ChapterVisual { FeatureGuid = "40dee91477e8483291e6427e087e23a1", PauldronEEGuid = "82617673ad025964fa21daa4973d181d", ArmorItemGuid = "4f59bf804f934458ae4760f9dee59a35" }, // Imperial Fists
            new ChapterVisual { FeatureGuid = "4728d63f41b844a4b67738cd195b6502", PauldronEEGuid = "b436f756db581bd40834796078195c4f", ArmorItemGuid = "3886ab1c7dc644b894d6fae6c2be5aa9" }, // Iron Hands
            new ChapterVisual { FeatureGuid = "750624bd22584374b06fb031df9ba46c", PauldronEEGuid = "7c4937e34168d114d8141cd6ffc337ec", ArmorItemGuid = "55b089b5dbc14e48948b6f733a6e8710" }, // Raven Guard
            new ChapterVisual { FeatureGuid = "29576f957088476287bd9d11b7586726", PauldronEEGuid = "3a5cad1b284a35d4c9be69f5eb1e5237", ArmorItemGuid = "6a5b61e5591b40e7a6bad7bb31e4a5a0" }, // Salamanders
            new ChapterVisual { FeatureGuid = "ce0a98d8111b43fb8eee024c28eb1fe1", PauldronEEGuid = "466362ab9c226c04c86cd26ee8616a6d", ArmorItemGuid = "f5bdf59bb0e24b6f87c4466ebffb5d1f" }, // Space Wolves
            new ChapterVisual { FeatureGuid = "504a68917a9d47c8a05d8b8bf40a66db", PauldronEEGuid = "ced04fec50fdf184c8d11e1bb19e8005", ArmorItemGuid = "93ea3bbf14704c39adf688a197431a6b" }, // Ultramarines
            new ChapterVisual { FeatureGuid = "54787d5b3495488493df14b3797b7fdf", PauldronEEGuid = "84d61c2bf737bc24996ce7a18f383f4e", ArmorItemGuid = "a7bf4e40d307447f8cc3af8bb5aef49e" }, // White Scars
        };

        internal static ChapterVisual? DetectChapter(BaseUnitEntity unit)
        {
            if (unit == null || unit.Progression == null) return null;
            foreach (var f in unit.Progression.Features)
            {
                if (f == null || f.Blueprint == null) continue;
                string g = f.Blueprint.AssetGuid;
                for (int i = 0; i < Chapters.Length; i++)
                    if (Chapters[i].FeatureGuid == g) return Chapters[i];
            }
            return null;
        }

        // PER-CHARACTER marine test for the Blade Dancer filter. True iff the unit being assembled in this
        // chargen RefreshData is a Deathwatch Astartes, detected by the chapter feature on the PREVIEW unit.
        // The chapter feature lives on LevelUpManager.PreviewUnit (a clone) and is NOT on CurrentUnit/TargetUnit
        // until Commit, so we test the preview unit (same as the mod's own doll code), not this.Unit.Value.
        internal static bool IsDeathwatchMarinePreview(BaseUnitEntity previewUnit)
        {
            return previewUnit != null && DetectChapter(previewUnit) != null;
        }

        // Marine identity tests, centralised so detection has ONE edit point (e.g. the hireable-merc
        // m_Race-stays-Human caveat would change these, not seven call sites). IsMarineUnit matches by the
        // MOD-OWNED race blueprint (DW_AstartesRace), NOT by RaceId: vanilla Ulfar, DLC Uralon and enemy Chaos
        // Marines all carry RaceId Spacemarine via the GAME's SpaceMarineStandardRace, so a RaceId test captured
        // them too (leaking the dynamic-size buff into pure-vanilla saves). Only units created by this mod carry
        // DW_AstartesRace. IsMarinePreset = a chargen doll / DollData, by the marine visual preset.
        internal static bool IsMarineUnit(BaseUnitEntity u)
        {
            return u != null && u.Progression != null && u.Progression.Race != null
                && u.Progression.Race.AssetGuid == AstartesRace_Guid;
        }
        internal static bool IsMarinePreset(BlueprintRaceVisualPreset preset)
        {
            return preset != null && preset.AssetGuid == MarinePreset_Guid;
        }

        [OwlcatModificationEnterPoint]
        public static void Initialize(OwlcatModification modification)
        {
            Modification = modification;
            Log("[Init] Enter point reached; bootstrapping Harmony.");
            RegisterVoiceBankPath(modification);
            var harmony = new Harmony(modification.Manifest.UniqueName);
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                // ALL-OR-NOTHING: three patches resolve their targets by signature/name, so a game update can
                // fail PatchAll partway and leave the mod HALF-patched (corrupted chargen users would report as
                // save-eating). Strip OUR patches -- the id argument is REQUIRED, UnpatchAll(null) would strip
                // other mods' too -- and go dormant instead.
                try { harmony.UnpatchAll(harmony.Id); } catch { /* best effort */ }
                LogError("[Init][ERR] Harmony patching failed -- game version likely incompatible; Deathwatch disabled itself", e);
            }
        }

        // Register the mod's Audio folder as a Wwise bank base path so the two DW voice banks (DX_Ask_DWMarine
        // / DX_Ask_DWApothecary, named by the voice blueprints' SoundBanks[]) resolve from the mod instead of
        // the game's StreamingAssets. AkSoundEngine.AddBasePath is public (AK.Wwise.Unity.API, referenced) and
        // purely ADDITIVE -- base paths are never cleared (only AudioFilePackagesSettings adds one), and the Ak
        // engine is already initialized before the mod enter point runs, so calling once HERE persists through
        // to the main-menu voice-bank load. No Harmony patch needed. If the Audio folder is absent we no-op.
        private static void RegisterVoiceBankPath(OwlcatModification modification)
        {
            try
            {
                string dir = Path.Combine(modification.Path, "Audio");
                if (!Directory.Exists(dir))
                {
                    Log("[Audio] mod Audio folder not found (" + dir + "); DW banks must be in StreamingAssets.");
                    return;
                }
                var res = AkSoundEngine.AddBasePath(dir);
                Log("[Audio] Registered mod bank path " + dir + " (AddBasePath -> " + res + ").");
            }
            catch (Exception e) { LogError("[Audio][ERR] AddBasePath", e); }
        }

        // The OMM LogChannel already tags every line with the mod name ("[... - Deathwatch][Message]:"), so we
        // do NOT prepend it. Log = Message severity; LogError routes through Error severity (the Exception
        // overload attaches the stack trace) so real failures show as [Error] and are filterable in logs.
        public static void Log(string msg)
        {
            if (Modification != null)
                Modification.Logger.Log(msg);
        }

        public static void LogError(string msg)
        {
            if (Modification != null)
                Modification.Logger.Error(msg);
        }

        public static void LogError(string msg, Exception e)
        {
            if (Modification != null)
                Modification.Logger.Error(e, msg);
        }

        // Verbose developer diagnostics (rig/EE dumps, per-flip traces) are off by default so a released
        // install stays quiet; flip to true when reproducing a rendering issue. Low-volume milestone lines
        // ([Init], [Tile]) stay on Log() so a user's bug report still shows the useful trace.
        internal static bool VerboseLogging;
        public static void LogDebug(string msg)
        {
            if (VerboseLogging) Log(msg);
        }

        // Mod-localized UI string: keys live in Localization\enGB.json (loaded into the game's current pack by
        // the OwlcatModification loader). Falls back to the English default if the pack has no entry (pack not
        // loaded yet / missing key), so player-facing labels can never render blank.
        internal static string ModString(string key, string fallback)
        {
            try
            {
                var pack = Kingmaker.Localization.LocalizationManager.Instance.CurrentPack;
                string s = pack != null ? pack.GetText(key, false) : null;
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        // Player unit (StartGame_Player_Unit 3a849) stays vanilla human; the marine's race + baseline loadout live on
        // the separate DW_Marine_Player_Unit blueprint, swapped in only for the Custom Space Marine tile.
    }
}
