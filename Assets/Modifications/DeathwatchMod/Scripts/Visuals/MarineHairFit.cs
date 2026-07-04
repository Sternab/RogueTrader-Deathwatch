using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Visual.CharacterSystem;   // Character, EquipmentEntity, BodyPart, Skeleton
using UnityEngine;                        // Mesh, Matrix4x4, MonoBehaviour, Input, KeyCode

namespace DeathwatchMod
{
    // HUMAN HAIR/BEARD FIT ON THE MARINE HEAD. The appearance lists offer human (_M_HM / _M_Any) hair and beard
    // EEs alongside the few marine-native (_M_SM) ones. Human hair binds to the marine rig fine (same bone names)
    // but renders at exactly HUMAN size: the marine skull is a natively re-sculpted, BIGGER mesh (+15% wide,
    // +21% tall, +41% deep vs the human head; every bone in both rigs is scale 1.0, and the human-vs-marine
    // bindpose delta is pure translation) -- so the hair lands at the right POSITION but sunk into / floating on
    // the larger skull. The engine has no auto-fit: Owlcat's own fix is hand-authoring per-rig meshes.
    //
    // FIX: when a marine-rig Character composites a human hair/beard MESH part, swap the part's sharedMesh for a
    // cached clone whose BINDPOSES are right-multiplied by a scale-about-anchor matrix (bind' = bind * M is
    // mathematically identical to transforming the mesh in mesh space by M -- vertices untouched). The swap is
    // TRANSIENT: applied in a prefix on Character.BuildMesh (the merge into the single SkinnedMeshRenderer,
    // Character.cs:2102 -- CombineMeshes copies the data) and restored in a finalizer, so the shared prefab asset
    // is never left mutated and human NPCs are unaffected. Per-part bindposes are natively supported by the
    // merge (EnsureBones appends a duplicate bone entry when bindposes differ). Texture-only styles (buzz cuts,
    // stubble -- no RendererPrefab) already fit via the shared head UV layout and are naturally skipped.
    //
    // Also fixes a SIDE BUG found in the same investigation: some human beard EEs carry SkeletonModifiers (e.g.
    // EE_Beard01Medium sets Jaw_Scale to 0.7) which fire on the marine by bone-name match and shrink the actual
    // jaw. Neutralised per-character via the engine's own veto: append the marine body EE to the modifier's
    // IgnoreIfCharacterContainsEE list (Character.cs:735 intersects it with the wearer's EE list), so only
    // characters wearing a marine body ignore it -- humans keep the modifier.
    //
    // TUNING: scale/offset constants below are first-pass values from the Head01 human-vs-marine measurements.
    // With VerboseLogging=true a live tuner is spawned: Ctrl+F9 cycles the parameter, F9/Shift+F9 adjusts it,
    // and the doll rebuilds immediately. Calibrate in one chargen session, then bake the numbers here.
    internal static class MarineHairFit
    {
        // Fit parameters (F9 live-tunable when VerboseLogging; bake calibrated values here).
        internal static float HairScale = 1.15f;
        internal static float BeardScale = 1.15f;
        internal static Vector3 HairOffset = Vector3.zero;    // mesh-space; Y = up, Z = forward
        internal static Vector3 BeardOffset = Vector3.zero;

        // Human-rig anchor points in MESH space (the skull/jaw rest positions, read from the human bindposes):
        // hair scales about the skull, beards about the jaw, so growth is outward from the head, not from origin.
        private static readonly Vector3 SkullAnchor = new Vector3(0f, 1.7378f, 0.0283f);
        private static readonly Vector3 JawAnchor = new Vector3(0f, 1.6184f, 0.0346f);

        internal static WeakReference<Character> LastMarineCharacter;   // for the tuner's live rebuild

        private sealed class CloneEntry { public int Version; public Mesh Clone; }
        private static readonly Dictionary<Mesh, CloneEntry> s_clones = new Dictionary<Mesh, CloneEntry>();
        private static readonly HashSet<EquipmentEntity> s_modifiersNeutralised = new HashSet<EquipmentEntity>();
        private static int s_paramsVersion;

        // Human/Any hair+beard only: the _M_SM natives already fit, and the name gate keeps real helmets
        // (also BodyPartType.Helmet, e.g. EE_DeathwatchHelmet) and the "!EE_..._NONE" placeholders out.
        internal static bool IsHumanHair(EquipmentEntity ee)
            => ee != null && ee.name.StartsWith("EE_Hair", StringComparison.Ordinal) && !ee.name.Contains("_M_SM");
        internal static bool IsHumanBeard(EquipmentEntity ee)
            => ee != null && ee.name.StartsWith("EE_Beard", StringComparison.Ordinal) && !ee.name.Contains("_M_SM");

        internal static void BumpVersion() { s_paramsVersion++; }   // invalidates all cached clones (tuner)

        // Clone the source mesh with bindposes pre-multiplied so the part renders scaled about the anchor.
        internal static Mesh GetFittedClone(Mesh source, bool beard)
        {
            CloneEntry entry;
            if (s_clones.TryGetValue(source, out entry) && entry.Version == s_paramsVersion) return entry.Clone;

            var binds = source.bindposes;                       // returns a copy
            if (binds == null || binds.Length == 0) return null;

            float k = beard ? BeardScale : HairScale;
            Vector3 anchor = beard ? JawAnchor : SkullAnchor;
            Vector3 offset = beard ? BeardOffset : HairOffset;
            // M = translate(anchor+offset) * scale(k) * translate(-anchor): uniform scale about the anchor, then nudge.
            Matrix4x4 m = Matrix4x4.Translate(anchor + offset) * Matrix4x4.Scale(Vector3.one * k) * Matrix4x4.Translate(-anchor);
            for (int i = 0; i < binds.Length; i++) binds[i] = binds[i] * m;   // bind' = bind * M == mesh transformed by M

            if (entry != null && entry.Clone != null) UnityEngine.Object.Destroy(entry.Clone);   // stale-param clone
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_DWfit";
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.bindposes = binds;
            s_clones[source] = new CloneEntry { Version = s_paramsVersion, Clone = clone };
            return clone;
        }

        // Kill the human EE's SkeletonModifiers for marine wearers (the Jaw_Scale-0.7 bug): append the wearer's
        // marine body EE to each modifier's IgnoreIfCharacterContainsEE. Session-long mutation of the shared EE
        // asset, but scoped by the engine itself -- the veto only fires for characters wearing that body EE.
        internal static void NeutraliseSkeletonModifiers(Character character, EquipmentEntity ee)
        {
            if (ee.SkeletonModifiers == null || ee.SkeletonModifiers.Count == 0) return;
            if (s_modifiersNeutralised.Contains(ee)) return;

            EquipmentEntity marker = null;
            foreach (var e in character.EquipmentEntities)
                if (e != null && e.name.StartsWith("EE_Body", StringComparison.Ordinal) && e.name.Contains("_M_SM")) { marker = e; break; }
            if (marker == null) return;                          // no marine body EE on this character -- leave it

            foreach (var bone in ee.SkeletonModifiers)
            {
                if (bone == null) continue;
                if (bone.IgnoreIfCharacterContainsEE == null) bone.IgnoreIfCharacterContainsEE = new List<EquipmentEntity>();
                if (!bone.IgnoreIfCharacterContainsEE.Contains(marker)) bone.IgnoreIfCharacterContainsEE.Add(marker);
            }
            s_modifiersNeutralised.Add(ee);
            DeathwatchModMain.LogDebug("[HairFit] neutralised " + ee.SkeletonModifiers.Count + " skeleton modifier(s) on " + ee.name + " for marine wearers.");
        }
    }

    // The interception: swap human hair/beard meshes for fitted clones for the duration of the merge, then
    // restore. Marine rigs only (the MaleSpaceMarine skeleton -- vanilla Ulfar shares it but never wears human
    // hair, so the gate is sufficient and needs no unit lookup). BuildMesh runs on equipment change (IsDirty)
    // for doll, in-game body and inventory paperdoll alike, so one patch covers all three.
    [HarmonyPatch(typeof(Character), "BuildMesh")]
    internal static class Character_BuildMesh_MarineHairFit_Patch
    {
        // non-reentrant: BuildMesh runs on the main thread and never recurses
        private static readonly List<KeyValuePair<SkinnedMeshRenderer, Mesh>> s_swapped = new List<KeyValuePair<SkinnedMeshRenderer, Mesh>>();

        [HarmonyPrefix]
        private static void Prefix(Character __instance, Dictionary<BodyPart, EquipmentEntity> __0)
        {
            try
            {
                s_swapped.Clear();
                if (__instance == null || __0 == null) return;
                if (__instance.Skeleton == null || !__instance.Skeleton.name.Contains("MaleSpaceMarine")) return;   // marine rigs only

                foreach (var kv in __0)
                {
                    var part = kv.Key;
                    var ee = kv.Value;
                    if (part == null || ee == null) continue;
                    bool hair = MarineHairFit.IsHumanHair(ee);
                    bool beard = !hair && MarineHairFit.IsHumanBeard(ee);
                    if (!hair && !beard) continue;

                    MarineHairFit.NeutraliseSkeletonModifiers(__instance, ee);

                    var renderer = part.SkinnedRenderer;
                    var mesh = renderer != null ? renderer.sharedMesh : null;
                    if (mesh == null) continue;                  // texture-only style: already fits, nothing to do

                    var clone = MarineHairFit.GetFittedClone(mesh, beard);
                    if (clone == null) continue;
                    s_swapped.Add(new KeyValuePair<SkinnedMeshRenderer, Mesh>(renderer, mesh));
                    renderer.sharedMesh = clone;                 // transient -- restored in the finalizer below
                    DeathwatchModMain.LogDebug("[HairFit] fitted " + ee.name + " (scale " + (beard ? MarineHairFit.BeardScale : MarineHairFit.HairScale) + ").");
                }

                if (s_swapped.Count > 0)
                {
                    MarineHairFit.LastMarineCharacter = new WeakReference<Character>(__instance);
                    if (DeathwatchModMain.VerboseLogging) MarineHairFitTuner.Ensure();
                }
            }
            catch (Exception e) { DeathwatchModMain.LogError("[HairFit][ERR] BuildMesh prefix", e); }
        }

        // Finalizer, not Postfix: the shared prefab assets MUST get their original meshes back even if
        // BuildMesh throws, or every human NPC would wear marine-fitted hair.
        [HarmonyFinalizer]
        private static void Finalizer()
        {
            try
            {
                foreach (var kv in s_swapped)
                    if (kv.Key != null) kv.Key.sharedMesh = kv.Value;
            }
            catch (Exception e) { DeathwatchModMain.LogError("[HairFit][ERR] BuildMesh finalizer restore", e); }
            finally { s_swapped.Clear(); }
        }
    }

    // Live tuner (only spawned when VerboseLogging): Ctrl+F9 cycles the parameter, F9 increments, Shift+F9
    // decrements (scale steps 0.02, offset steps 0.005). Every change invalidates the clone cache and dirties
    // the last marine Character so the doll rebuilds immediately -- calibrate in one chargen session, read the
    // values from the log, bake them into MarineHairFit, and set VerboseLogging back to false.
    internal sealed class MarineHairFitTuner : MonoBehaviour
    {
        private static GameObject s_go;
        internal static void Ensure()
        {
            if (s_go != null) return;
            s_go = new GameObject("DW_HairFitTuner");
            s_go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(s_go);
            s_go.AddComponent<MarineHairFitTuner>();
            DeathwatchModMain.Log("[HairFit] tuner active: Ctrl+F9 = next parameter, F9 = +, Shift+F9 = -.");
        }

        private int m_param;   // 0 HairScale, 1 HairUp, 2 HairFwd, 3 BeardScale, 4 BeardUp, 5 BeardFwd
        private static readonly string[] Names = { "HairScale", "HairUp(Y)", "HairFwd(Z)", "BeardScale", "BeardUp(Y)", "BeardFwd(Z)" };

        private void Update()
        {
            try
            {
                if (!Input.GetKeyDown(KeyCode.F9)) return;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    m_param = (m_param + 1) % Names.Length;
                    DeathwatchModMain.Log("[HairFit] tuning parameter -> " + Names[m_param]);
                    return;
                }
                float dir = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? -1f : 1f;
                switch (m_param)
                {
                    case 0: MarineHairFit.HairScale += 0.02f * dir; break;
                    case 1: MarineHairFit.HairOffset += new Vector3(0f, 0.005f * dir, 0f); break;
                    case 2: MarineHairFit.HairOffset += new Vector3(0f, 0f, 0.005f * dir); break;
                    case 3: MarineHairFit.BeardScale += 0.02f * dir; break;
                    case 4: MarineHairFit.BeardOffset += new Vector3(0f, 0.005f * dir, 0f); break;
                    case 5: MarineHairFit.BeardOffset += new Vector3(0f, 0f, 0.005f * dir); break;
                }
                MarineHairFit.BumpVersion();
                Character ch;
                if (MarineHairFit.LastMarineCharacter != null && MarineHairFit.LastMarineCharacter.TryGetTarget(out ch) && ch != null)
                    ch.IsDirty = true;   // rebuild the visible marine right now
                DeathwatchModMain.Log("[HairFit] HairScale=" + MarineHairFit.HairScale.ToString("F2")
                    + " HairOffset=" + MarineHairFit.HairOffset.ToString("F3")
                    + " BeardScale=" + MarineHairFit.BeardScale.ToString("F2")
                    + " BeardOffset=" + MarineHairFit.BeardOffset.ToString("F3"));
            }
            catch (Exception e) { DeathwatchModMain.LogError("[HairFit][ERR] tuner", e); }
        }
    }
}
