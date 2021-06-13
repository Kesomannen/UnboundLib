﻿using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnboundLib.GameModes;

namespace UnboundLib.Patches
{
    internal static class ArmsRacePatchUtils
    {
        internal static Type GetMethodNestedType(string method)
        {
            var nestedTypes = typeof(GM_ArmsRace).GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic);
            Type nestedType = null;

            foreach (var type in nestedTypes)
            {
                if (type.Name.Contains(method))
                {
                    nestedType = type;
                    break;
                }
            }

            return nestedType;
        }

        internal static void TriggerPlayerPickStart()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPlayerPickStart);
        }

        internal static void TriggerPlayerPickEnd()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPlayerPickEnd);
        }

        internal static void TriggerPickStart()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPickStart);
        }

        internal static void TriggerPickEnd()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPickEnd);
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "Start")]
    class GM_ArmsRace_Patch_Start
    {
        static void Prefix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookInitStart);
        }

        static void Postfix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookInitEnd);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Remove the default player joined and died -hooks. We'll add them back through the GameMode abstraction layer.
            var list = instructions.ToList();
            var newInstructions = new List<CodeInstruction>();

            var f_pmInstance = AccessTools.Field(typeof(PlayerManager), "instance");
            var m_playerDied = ExtensionMethods.GetMethodInfo(typeof(GM_ArmsRace), "PlayerDied");
            var m_addPlayerDied = ExtensionMethods.GetMethodInfo(typeof(PlayerManager), "AddPlayerDiedAction");
            var m_getPlayerJoinedAction = ExtensionMethods.GetPropertyInfo(typeof(PlayerManager), "PlayerJoinedAction").GetGetMethod();
            var m_setPlayerJoinedAction = ExtensionMethods.GetPropertyInfo(typeof(PlayerManager), "PlayerJoinedAction").GetSetMethod(true);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].LoadsField(f_pmInstance) &&
                    list[i + 2].OperandIs(m_playerDied) &&
                    list[i + 4].Calls(m_addPlayerDied))
                {
                    i += 4;
                }
                else if (
                    list[i].LoadsField(f_pmInstance) &&
                    list[i + 2].Calls(m_getPlayerJoinedAction) &&
                    list[i + 8].Calls(m_setPlayerJoinedAction))
                {
                    i += 8;
                }
                else
                {
                    newInstructions.Add(list[i]);
                }
            }

            return newInstructions;
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "DoStartGame")]
    class GM_ArmsRace_Patch_DoStartGame
    {
        static void Prefix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookGameStart);
        }

        static IEnumerator Postfix(IEnumerator e)
        {
            // We need to iterate through yields like this to get the postfix in the correct place
            while (e.MoveNext())
            {
                yield return e.Current;
            }

            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookRoundStart);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointStart);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookBattleStart);
        }
    }

    [HarmonyPatch]
    class GM_ArmsRace_TranspilerPatch_DoStartGame
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(ArmsRacePatchUtils.GetMethodNestedType("DoStartGame"), "MoveNext");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var newInstructions = new List<CodeInstruction>();

            var f_cardChoiceVisualsInstance = AccessTools.Field(typeof(CardChoiceVisuals), "instance");
            var m_cardChoiceVisualsShow = ExtensionMethods.GetMethodInfo(typeof(CardChoiceVisuals), "Show");
            var m_cardChoiceVisualsHide = ExtensionMethods.GetMethodInfo(typeof(CardChoiceVisuals), "Hide");
            var m_cardChoiceInstancePick = ExtensionMethods.GetMethodInfo(typeof(CardChoice), "DoPick");
            var f_pickPhase = ExtensionMethods.GetFieldInfo(typeof(GM_ArmsRace), "pickPhase");

            var m_triggerPickStart = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPickStart");
            var m_triggerPickEnd = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPickEnd");
            var m_triggerPlayerPickStart = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPlayerPickStart");
            var m_triggerPlayerPickEnd = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPlayerPickEnd");

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].LoadsField(f_pickPhase))
                {
                    newInstructions.Add(list[i]);
                    newInstructions.Add(list[i + 1]);
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPickStart));
                    i++;
                    continue;
                }

                if (list[i].LoadsField(f_cardChoiceVisualsInstance) && list[i + 4].Calls(m_cardChoiceVisualsShow))
                {
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPlayerPickStart));
                }

                if (list[i].Calls(m_cardChoiceInstancePick))
                {
                    newInstructions.AddRange(list.GetRange(i, 8));
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPlayerPickEnd));
                    i += 7;
                    continue;
                }

                if (list[i].Calls(m_cardChoiceVisualsHide))
                {
                    newInstructions.Add(list[i]);
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPickEnd));
                    continue;
                }

                newInstructions.Add(list[i]);
            }

            return newInstructions;
        }
    }

    [HarmonyPatch]
    class GM_ArmsRace_TranspilerPatch_RoundTransition
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(ArmsRacePatchUtils.GetMethodNestedType("RoundTransition"), "MoveNext");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var newInstructions = new List<CodeInstruction>();

            var f_cardChoiceInstance = AccessTools.Field(typeof(CardChoice), "instance");
            var m_cardChoiceInstancePick = ExtensionMethods.GetMethodInfo(typeof(CardChoice), "DoPick");
            var f_pickPhase = ExtensionMethods.GetFieldInfo(typeof(GM_ArmsRace), "pickPhase");
            var f_playerManagerInstance = AccessTools.Field(typeof(PlayerManager), "instance");
            var m_showPlayers = ExtensionMethods.GetMethodInfo(typeof(PlayerManager), "SetPlayersVisible");

            var m_triggerPickStart = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPickStart");
            var m_triggerPickEnd = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPickEnd");
            var m_triggerPlayerPickStart = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPlayerPickStart");
            var m_triggerPlayerPickEnd = ExtensionMethods.GetMethodInfo(typeof(ArmsRacePatchUtils), "TriggerPlayerPickEnd");

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].LoadsField(f_pickPhase))
                {
                    newInstructions.Add(list[i]);
                    newInstructions.Add(list[i + 1]);
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPickStart));
                    i++;
                    continue;
                }

                if (list[i].opcode == OpCodes.Ldarg_0 &&
                    list[i + 1].LoadsField(f_cardChoiceInstance) &&
                    list[i + 10].Calls(m_cardChoiceInstancePick))
                {
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPlayerPickStart));
                    newInstructions.AddRange(list.GetRange(i, 19));
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPlayerPickEnd));
                    i += 18;
                    continue;
                }

                if (list[i].LoadsField(f_playerManagerInstance) &&
                    list[i + 1].opcode == OpCodes.Ldc_I4_1 &&
                    list[i + 2].Calls(m_showPlayers))
                {
                    newInstructions.Add(new CodeInstruction(OpCodes.Call, m_triggerPickEnd));
                }

                newInstructions.Add(list[i]);
            }

            return newInstructions;
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "PointOver")]
    class GM_ArmsRace_Patch_PointOver
    {
        static void Prefix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointEnd);
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "PointTransition")]
    class GM_ArmsRace_Patch_PointTransition
    {
        static IEnumerator Postfix(IEnumerator e)
        {
            // We need to iterate through yields like this to get the postfix in the correct place
            while (e.MoveNext())
            {
                yield return e.Current;
            }

            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointStart);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookBattleStart);
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "RoundTransition")]
    class GM_ArmsRace_Patch_RoundTransition
    {
        static IEnumerator Postfix(IEnumerator e)
        {
            // We need to iterate through yields like this to get the postfix in the correct place
            while (e.MoveNext())
            {
                yield return e.Current;
            }

            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookRoundStart);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointStart);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookBattleStart);
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "RoundOver")]
    class GM_ArmsRace_Patch_RoundOver
    {
        static void Prefix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointEnd);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookRoundEnd);
        }
    }

    [HarmonyPatch(typeof(GM_ArmsRace), "GameOver")]
    class GM_ArmsRace_Patch_GameOver
    {
        static void Prefix()
        {
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookPointEnd);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookRoundEnd);
            GameModeManager.TriggerHook("ArmsRace", GameModeHooks.HookGameEnd);
        }
    }
}
