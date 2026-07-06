using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Kingmaker;                                  // Game
using Kingmaker.Controllers.Units;                // UnitMovableAreaController
using Kingmaker.Enums;                            // DestructionStage
using Kingmaker.View.Mechanics.Destructible;      // DestructionStagesViewManager
using Kingmaker.View.Mechanics.Entities;          // DestructibleEntityView
using Pathfinding;                                // AstarPath, GraphUpdateObject
using UnityEngine;

namespace DeathwatchMod
{
    // DESTROYED-COVER NAV REFRESH (general engine bugfix, tester report 2026-07-05, traced in full).
    // When a destructible (cover/barricade) is destroyed mid-combat, its cells stay unwalkable for the rest
    // of the turn or longer. Root cause is a stale-rasterization RACE, not a turn-deferred design:
    //   1. At HP 0, DestructibleEntityView.ChangeStage toggles the stage's GridNavmeshModifier and the
    //      GraphUpdateRouter queues a walkability re-rasterization within a tick or two -- BUT that scan
    //      samples LIVE physics colliders, and DestructionStagesViewManager.SwitchStages only swaps the
    //      intact prefab (which carries the colliders) for rubble AFTER an async FX delay
    //      (SwitchPrefabsDelaySeconds, Task.Delay). The scan re-reads the intact colliders -> still blocked.
    //   2. The late prefab swap is a plain SetActive -- it raises NO navmesh event, so nothing ever
    //      re-rasterizes the freed cells; release depends on per-scene prefab wiring luck.
    //   3. The blue movable-area overlay (UnitMovableAreaController) recomputes only on turn start /
    //      command end / MP change -- destruction is not a trigger, so even freed cells LOOK blocked.
    // WHY RUNTIME (not data): the race lives in engine view/pathfinding code (an async view delay vs a
    // physics-sampling graph update); no blueprint field expresses "re-rasterize after the collider swap".
    // Deliberately NOT marine-gated: this fixes a vanilla bug for whoever runs the mod; it mutates no
    // game STATE -- the pathfinding graph is client-local derived data re-read from physics, and the
    // trigger (destruction stage change) is a synced simulation event, so co-op parity vs vanilla is
    // unchanged (vanilla's own collider swap already runs on a real-time delay).
    // Fix: after the prefab swap completes, issue one GraphUpdateObject over the object's PRE-destruction
    // footprint (captured before the swap shrinks the bounds) exactly like GraphUpdateRouter.QueueRect
    // (updatePhysics + modifyTag, tag from the now-active stage modifier if any), flush it, and re-raise
    // the movable-area recompute so the overlay reflects the freed cells mid-turn.
    [HarmonyPatch(typeof(DestructibleEntityView), nameof(DestructibleEntityView.ChangeStage))]
    internal static class DestructibleEntityView_ChangeStage_CoverNavRefresh_Patch
    {
        // UnitMovableAreaController.UpdateMovableArea is private; UI-only nicety, so a miss is tolerated.
        private static readonly MethodInfo s_UpdateMovableArea =
            AccessTools.Method(typeof(UnitMovableAreaController), "UpdateMovableArea");
        private static bool s_loggedOnce;

        [HarmonyPostfix]
        private static void Postfix(DestructibleEntityView __instance, DestructionStage stage, bool onLoad)
        {
            try
            {
                if (stage != DestructionStage.Destroyed || onLoad || __instance == null) return;

                // Footprint BEFORE the rubble swap: the intact colliders are still present, so Bounds
                // covers every cell that may free up (post-swap bounds can shrink to the rubble).
                Rect footprint = __instance.Bounds;

                // The collider swap happens SwitchPrefabsDelaySeconds after this call (skipped onLoad).
                var stages = __instance.GetComponentInChildren<DestructionStagesViewManager>(true);
                if (stages == null && __instance.transform.parent != null)
                    stages = __instance.transform.parent.GetComponentInChildren<DestructionStagesViewManager>(true);
                float delay = stages != null ? stages.SwitchPrefabsDelaySeconds : 0f;

                if (__instance.isActiveAndEnabled)
                    __instance.StartCoroutine(RefreshAfterSwap(__instance, footprint, delay));
            }
            catch (Exception e) { DeathwatchModMain.LogError("[CoverNav][ERR] ChangeStage", e); }
        }

        private static IEnumerator RefreshAfterSwap(DestructibleEntityView view, Rect footprint, float delay)
        {
            // Real-time wait to match the engine's Task.Delay, + margin for SetActive/physics sync.
            yield return new WaitForSecondsRealtime(delay + 0.25f);

            bool refreshed = false;
            try
            {
                if (AstarPath.active != null)
                {
                    // Mirror GraphUpdateRouter.QueueRect/BoundsFromRect: XZ rect -> tall bounds, physics
                    // re-scan, tag from the currently active stage modifier (0 = plain walkable-if-clear).
                    int tag = (view != null && view.GridNavmeshModifier != null) ? view.GridNavmeshModifier.Tag : 0;
                    var bounds = new Bounds(
                        new Vector3(footprint.center.x, 0f, footprint.center.y),
                        new Vector3(footprint.size.x, 1000f, footprint.size.y));
                    AstarPath.active.UpdateGraphs(new GraphUpdateObject(bounds)
                    {
                        updatePhysics = true,
                        modifyTag = true,
                        setTag = tag,
                    });
                    AstarPath.active.FlushGraphUpdates();
                    refreshed = true;
                }

                // Overlay: recompute the current unit's blue movable area so freed cells show mid-turn.
                var game = Game.Instance;
                if (refreshed && game != null && game.TurnController != null && game.TurnController.TurnBasedModeActive)
                {
                    var ctrl = game.UnitMovableAreaController;
                    if (ctrl != null && ctrl.CurrentUnit != null && s_UpdateMovableArea != null)
                        s_UpdateMovableArea.Invoke(ctrl, null);
                }

                if (refreshed && !s_loggedOnce)
                {
                    s_loggedOnce = true;
                    DeathwatchModMain.Log("[CoverNav] refreshed walkability after a destructible was destroyed (first occurrence this session).");
                }
                else if (refreshed)
                    DeathwatchModMain.LogDebug("[CoverNav] walkability refreshed after destruction.");
            }
            catch (Exception e) { DeathwatchModMain.LogError("[CoverNav][ERR] RefreshAfterSwap", e); }
        }
    }
}
