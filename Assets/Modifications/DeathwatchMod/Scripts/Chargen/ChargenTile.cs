using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints;                       // ResourcesLibrary
using Kingmaker.Blueprints.Root;                  // BlueprintCharGenRoot
using Kingmaker.DLC;                              // BlueprintDlc
using Kingmaker.EntitySystem.Entities;            // BaseUnitEntity
using Kingmaker.UI.MVVM.VM.CharGen;               // CharGenVM, CharGenContext
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Pregen; // CharGenPregenPhaseVM, CharGenPregenSelectorItemVM
using Kingmaker.UI.Models.LevelUp;                // ChargenUnit
using UniRx;                                       // ReactiveProperty<string>

namespace DeathwatchMod
{
    // ROADMAP #8 STEP 3 -- the "Custom Space Marine" pregen tile + flag wiring (replaces the Step-1 hardwire).
    //
    // The marine tile is a (null, true) custom-style tile: selecting it routes through the vanilla
    // null=>IsCustomCharacter=true branch (CharGenSetPregen(null)), and because CreatingDeathwatchMarine is
    // set (by the HandleSetPregen prefix below), the flag-gated GetOriginPath + SetPregenUnit patches swap in
    // the Deathwatch per-session path and the Astartes race/doll. A subclass is used purely so the flag
    // prefix can identify our tile by type (both this tile and the vanilla Custom tile carry unit==null).
    internal sealed class DwMarineSelectorItemVM : CharGenPregenSelectorItemVM
    {
        // m_CharacterName is set to the localized "Create Custom Character" by the base (null, true) ctor; it is
        // a PRIVATE readonly ReactiveProperty<string> on the base (object is mutable even though the field is
        // readonly), so we re-point its .Value via reflection to the mod-localized tile name. There is no public
        // name setter and the setup methods are private/non-virtual.
        private static readonly FieldInfo s_nameField =
            AccessTools.Field(typeof(CharGenPregenSelectorItemVM), "m_CharacterName");

        public DwMarineSelectorItemVM() : base(null, true)
        {
            try
            {
                var rp = s_nameField != null ? s_nameField.GetValue(this) as ReactiveProperty<string> : null;
                if (rp != null) rp.Value = DeathwatchModMain.ModString("DW.Chargen.MarineTileName", "Custom Space Marine");
                else DeathwatchModMain.Log("[Tile][ERR] m_CharacterName field not found; tile keeps default label.");
            }
            catch (Exception e) { DeathwatchModMain.Log("[Tile][ERR] set name: " + e); }
        }
    }

    // Append exactly ONE marine tile after the premade tiles. The tiles are built in the compiler-generated
    // callback <.ctor>g__UnitsCallback|13_1 (passed to EnsureNewGamePregens/EnsureCompanionPregens) -- NOT in
    // the ctor -- so we postfix that callback, resolved by signature (single List<ChargenUnit> param) because
    // the mangled name can shift between builds. A bare [HarmonyPatch] is REQUIRED so PatchAll picks up a
    // TargetMethod-only class (without it the class is silently skipped). Allowed for NewGame AND NewCompanion
    // (the merc creation reuses this same callback) so the marine tile appears for the player AND a hired
    // mercenary. Adding to EntitiesCollection auto-wires selection (base SubscribeItems
    // ObserveAdd) and triggers the View's ObserveCountChanged redraw -> the tile shows as a second row and is
    // immediately selectable (m_IsAvailable defaults true). The marine tile is appended AFTER the callback's
    // auto-select of the first premade, so it is never the default selection (human default preserved).
    [HarmonyPatch]
    internal static class CharGenPregenPhaseVM_AddMarineTile_Patch
    {
        // HARD DLC REQUIREMENT: the marine's armour/helmet are The Infinite Museion DLC's assets, so the
        // Custom Space Marine tile only appears when that DLC is owned AND enabled (BlueprintDlc.IsActive =
        // IsAvailable && IsEnabled, backed by StoreManager.DLCCache -- populated well before the main menu
        // builds chargen, so it is safe to read here). Checked per phase build, so enabling the DLC takes
        // effect on the next chargen open. This tile is the single entry point to all marine content.
        private const string InfiniteMuseionDlc_Guid = "30938411c3c64d77b415fbe6d23bbaa0";
        private static bool s_dlcGateLogged;

        private static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(CharGenPregenPhaseVM)).FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return m.ReturnType == typeof(void)
                    && ps.Length == 1
                    && ps[0].ParameterType == typeof(List<ChargenUnit>)
                    && m.Name.Contains("UnitsCallback");
            });
        }

        [HarmonyPostfix]
        private static void Postfix(CharGenPregenPhaseVM __instance)
        {
            try
            {
                var ctx = Traverse.Create(__instance).Field("CharGenContext").GetValue<CharGenContext>();
                if (ctx == null || ctx.CharGenConfig == null) return;
                var mode = ctx.CharGenConfig.Mode;
                if (mode != CharGenConfig.CharGenMode.NewGame &&
                    mode != CharGenConfig.CharGenMode.NewCompanion) return;   // player (NewGame) or mercenary (NewCompanion) chargen

                var dlc = ResourcesLibrary.BlueprintsCache.Load(InfiniteMuseionDlc_Guid) as BlueprintDlc;
                if (dlc == null || !dlc.IsActive)
                {
                    if (!s_dlcGateLogged)
                    {
                        s_dlcGateLogged = true;
                        DeathwatchModMain.Log("[Tile] The Infinite Museion DLC is not active -- the Custom Space Marine tile is disabled (the mod's armour assets are that DLC's content).");
                    }
                    return;
                }

                var group = __instance.PregenSelectionGroup;
                if (group == null || group.EntitiesCollection == null) return;

                // Guard against double-add (callback can re-run on re-init).
                foreach (var existing in group.EntitiesCollection)
                    if (existing is DwMarineSelectorItemVM) return;

                group.EntitiesCollection.Add(new DwMarineSelectorItemVM());
                DeathwatchModMain.Log("[Tile] Appended 'Custom Space Marine' pregen tile.");
            }
            catch (Exception e) { DeathwatchModMain.Log("[Tile][ERR] add marine tile: " + e); }
        }
    }

    // ROADMAP #8 STEP 3 (label refresh): capture the live CharGenVM at its ctor so RefreshPhaseLabels can reach
    // the phase VMs. Typed ctor (verified): CharGenVM(CharGenConfig, Action, Action<BaseUnitEntity>, bool).
    [HarmonyPatch(typeof(CharGenVM), MethodType.Constructor,
        typeof(CharGenConfig), typeof(Action), typeof(Action<BaseUnitEntity>), typeof(bool))]
    internal static class CharGenVM_Ctor_Capture_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(CharGenVM __instance)
        {
            ChargenRouting.LiveCharGenVM = __instance;
        }
    }

    // FLAG SET (async-safe). SetPregen only ENQUEUES a CharGenSetPregen command; the consume side runs later in
    // ICharGenPregenHandler.HandleSetPregen -> SetUnit -> CharGenContext.SetPregenUnit -> GetOriginPath, all
    // SYNCHRONOUS within HandleSetPregen. So we set the flag in a PREFIX on HandleSetPregen (resolved by
    // signature -- it is an explicit-interface impl with a mangled name) from the live selection, which by then
    // is the final tile (single-player: IsControlMainCharacter()==true, so HandleSetPregen does NOT re-map
    // SelectedPregenEntity). This collapses set+consume into one step and removes the in-flight reorder window
    // that a SetPregen-side setter would leave open. Selecting the marine tile -> true; any other tile/null -> false.
    [HarmonyPatch]
    internal static class CharGenPregenPhaseVM_HandleSetPregen_Flag_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(CharGenPregenPhaseVM)).FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return m.ReturnType == typeof(void)
                    && ps.Length == 1
                    && ps[0].ParameterType == typeof(BaseUnitEntity)
                    && m.Name.Contains("HandleSetPregen");
            });
        }

        [HarmonyPrefix]
        private static void Prefix(CharGenPregenPhaseVM __instance)
        {
            try
            {
                var sel = __instance.SelectedPregenEntity != null ? __instance.SelectedPregenEntity.Value : null;
                DeathwatchModMain.CreatingDeathwatchMarine = sel is DwMarineSelectorItemVM;
            }
            catch (Exception e) { DeathwatchModMain.Log("[Tile][ERR] HandleSetPregen flag: " + e); }
        }

        // ROADMAP #8 STEP 3 (label refresh): a null->null tile switch (Custom Character <-> Custom Space Marine)
        // does not rebuild the phase VMs, so their cached tab labels stay stale. Re-push them now that the flag
        // (and thus GetPhaseName's output) is final for this selection.
        [HarmonyPostfix]
        private static void Postfix()
        {
            ChargenRouting.RefreshPhaseLabels();
        }
    }

    // FLAG RESET at chargen start. CharGenConfig.Create is the sole static factory (private ctor), runs exactly
    // once per chargen open for every mode, before any UI exists. Reset to FALSE here so a marine flag from a
    // previous session can never leak into a later chargen; the HandleSetPregen prefix is what re-sets it true.
    [HarmonyPatch(typeof(CharGenConfig), nameof(CharGenConfig.Create))]
    internal static class CharGenConfig_Create_ResetFlag_Patch
    {
        [HarmonyPostfix]
        private static void Postfix() { DeathwatchModMain.CreatingDeathwatchMarine = false; MarineUnitRouter.Reset(); }
    }

    // FLAG CLEAR on exit. CharGenContextVM.CompleteCharGen (finish) and CloseWithoutComplete (cancel) both
    // converge on the private CharGenContextVM.CloseCharGen, so one postfix there clears the flag on every
    // NewGame chargen exit. NOTE: patch CharGenContextVM, NOT the inner CharGenVM (which has a same-named pair).
    [HarmonyPatch(typeof(CharGenContextVM), "CloseCharGen")]
    internal static class CharGenContextVM_CloseCharGen_ClearFlag_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                DeathwatchModMain.CreatingDeathwatchMarine = false;
                MarineUnitRouter.Reset();
                // Drop the session's cached references too: the disposed CharGenVM (else it pins the whole
                // chargen VM graph between sessions) and the previous session's doll avatar/chapter.
                ChargenRouting.LiveCharGenVM = null;
                MarineDoll.DollAvatar = null;
                MarineDoll.DollChapter = null;
                ChargenRouting.RestoreVanillaGroups(BlueprintCharGenRoot.Instance.NewGameCustomChargenPath);
                ChargenRouting.RestoreVanillaGroups(BlueprintCharGenRoot.Instance.NewCompanionCustomChargenPath);   // merc path too
            }
            catch (Exception e) { DeathwatchModMain.Log("[Tile][ERR] CloseCharGen clear: " + e); }
        }
    }
}
