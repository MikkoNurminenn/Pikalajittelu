using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private int blockInputThroughFrame = -1;
        private static bool BlockGameplayInput
        {
            get { return instance != null && (instance.ModalVisible || Time.frameCount <= instance.blockInputThroughFrame); }
        }

        // Movement and mouse-look use PlayerController.TakeInput, independently
        // of Player.TakeInput. Both gates must recognize this modal window.
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        private static class StorageControllerInputPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref bool __result) { if (BlockGameplayInput) __result = false; }
        }

        // Mask gameplay queries, not Unity's IMGUI events or Input.GetKey*. The
        // latter must remain available to text fields, key capture and Escape.
        [HarmonyPatch]
        private static class StorageButtonInputPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "GetButton", "GetButtonDown", "GetButtonUp",
                    "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp", "GetKey", "GetKeyDown", "GetKeyUp",
                    "GetRadialTap", "GetRadialMultiTap", "HasDoubleTapped", "HasActiveLongPress", "IsTouchPressedDown" })
                    yield return AccessTools.Method(typeof(ZInput), name);
            }
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref bool __result) { if (BlockGameplayInput) __result = false; }
        }

        [HarmonyPatch]
        private static class StorageAxisInputPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "GetMouseScrollWheel", "GetJoyLTrigger", "GetJoyRTrigger",
                    "GetJoyRightStickX", "GetJoyRightStickY", "GetJoyLeftStickX", "GetJoyLeftStickY" })
                    yield return AccessTools.Method(typeof(ZInput), name);
            }
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref float __result) { if (BlockGameplayInput) __result = 0f; }
        }

        [HarmonyPatch]
        private static class StorageVectorInputPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "GetMouseDelta", "GetJoyRightStick", "GetJoyLeftStick" })
                    yield return AccessTools.Method(typeof(ZInput), name);
            }
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref Vector2 __result) { if (BlockGameplayInput) __result = Vector2.zero; }
        }
    }
}
