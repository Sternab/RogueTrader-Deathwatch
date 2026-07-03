using System;
using System.IO;                                  // Path, Directory
using HarmonyLib;
using Kingmaker.Sound;                            // SoundBanksManager

namespace DeathwatchMod
{
    // Ship the two DW voice sound banks INSIDE the mod (self-contained), instead of hand-dropping them into the
    // game's StreamingAssets\Audio\GeneratedSoundBanks\Windows. RT's Wwise resolves a bank NAME (DX_Ask_DWMarine /
    // DX_Ask_DWApothecary -- named by the DW voice blueprints' SoundBanks[]) against a STACK of base paths; RT
    // itself registers one via AkSoundEngine.AddBasePath (see AudioFilePackagesSettings.EnsureInitialized). We add
    // the mod's own <mod>\Audio folder to that stack ONCE, right before the voice banks load at the main menu
    // (SoundBanksManager.LoadVoiceBanks), so the two DW banks resolve from the mod folder. AkSoundEngine is reached
    // by reflection (it lives in the Wwise plugin assembly the mod does not reference). Idempotent; a base path
    // persists for the Ak session. If the Audio folder is absent we no-op (falls back to StreamingAssets).
    [HarmonyPatch(typeof(SoundBanksManager), "LoadVoiceBanks")]
    internal static class SoundBanksManager_LoadVoiceBanks_ModBankPath_Patch
    {
        private static bool s_added;

        [HarmonyPrefix]
        private static void Prefix()
        {
            if (s_added) return;
            s_added = true;
            try
            {
                var mod = DeathwatchModMain.Modification;
                if (mod == null) return;
                string dir = Path.Combine(mod.Path, "Audio");
                if (!Directory.Exists(dir))
                {
                    DeathwatchModMain.Log("[Audio] mod Audio folder not found (" + dir + "); DW banks must be in StreamingAssets.");
                    return;
                }
                var ak = AccessTools.TypeByName("AkSoundEngine");
                var add = ak != null ? AccessTools.Method(ak, "AddBasePath", new[] { typeof(string) }) : null;
                if (add == null)
                {
                    DeathwatchModMain.Log("[Audio][ERR] AkSoundEngine.AddBasePath(string) not found via reflection.");
                    return;
                }
                var res = add.Invoke(null, new object[] { dir });
                DeathwatchModMain.Log("[Audio] Registered mod bank path " + dir + " (AddBasePath -> " + res + ").");
            }
            catch (Exception e) { DeathwatchModMain.Log("[Audio][ERR] AddBasePath: " + e); }
        }
    }
}
