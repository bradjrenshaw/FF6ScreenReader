using System;
using HarmonyLib;
using MelonLoader;
using FFVI_ScreenReader.Core;

namespace FFVI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for detecting battle start/end state transitions.
    /// Used to pause sonar and other field-specific features during combat.
    /// </summary>
    public static class BattleStatePatches
    {
        /// <summary>
        /// Whether the player is currently in battle.
        /// </summary>
        public static bool IsInBattle { get; private set; } = false;

        /// <summary>
        /// Event fired when battle starts.
        /// </summary>
        public static event System.Action OnBattleStart;

        /// <summary>
        /// Event fired when battle ends.
        /// </summary>
        public static event System.Action OnBattleEnd;

        internal static void NotifyBattleStart()
        {
            try
            {
                if (IsInBattle)
                    return;

                IsInBattle = true;
                MelonLogger.Msg("[BattleState] Battle started");
                OnBattleStart?.Invoke();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleState] Error in NotifyBattleStart: {ex.Message}");
            }
        }

        internal static void NotifyBattleEnd()
        {
            try
            {
                if (!IsInBattle)
                    return;

                IsInBattle = false;
                MelonLogger.Msg("[BattleState] Battle ended");
                OnBattleEnd?.Invoke();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleState] Error in NotifyBattleEnd: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch BattleController.StartInBattle to detect when combat begins.
    /// Using StartInBattle instead of StartBattle because StartBattle has overloads.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.Battle.BattleController), nameof(Il2CppLast.Battle.BattleController.StartInBattle))]
    public static class BattleController_StartInBattle_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.Battle.BattleController __instance)
        {
            try
            {
                if (__instance == null) return;
                BattleStatePatches.NotifyBattleStart();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleState] Error in StartInBattle patch: {ex.Message}");
            }
        }
    }

    // NOTE: Battle end is detected via ResultMenuController.Close in BattleResultPatches.cs
    // We hook into that patch instead of patching StartWinResult/StartLoseResult
    // which have IL2CPP interop issues. Using Close ensures sonar resumes after
    // the player dismisses the results screen, not while it's still showing.
}
