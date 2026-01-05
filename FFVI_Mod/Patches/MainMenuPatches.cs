using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;

namespace FFVI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for detecting when the main menu (status/pause menu) opens and closes.
    /// Used to pause sonar while the menu is open.
    /// </summary>

    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.Show))]
    public static class MainMenuController_Show_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isSavePoint)
        {
            try
            {
                MelonLogger.Msg("[MainMenu] Menu opened");
                BattleStatePatches.NotifyBattleStart(); // Reuse battle pause mechanism
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MainMenuController.Show patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.Close))]
    public static class MainMenuController_Close_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                MelonLogger.Msg("[MainMenu] Menu closed");
                BattleStatePatches.NotifyBattleEnd(); // Reuse battle resume mechanism
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in MainMenuController.Close patch: {ex.Message}");
            }
        }
    }
}
