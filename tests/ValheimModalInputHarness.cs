using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MikkoMods;
using UnityEngine;

public sealed partial class ValheimInventoryHarness
{
    private static bool PressedButton(ref bool __result) { __result = true; return false; }
    private static bool ActiveAxis(ref float __result) { __result = 0.75f; return false; }
    private static bool ActiveVector(ref Vector2 __result) { __result = new Vector2(0.5f, -0.25f); return false; }

    private void CheckGameplayInputMask(bool blocked)
    {
        // Inject non-zero native-query results synchronously. No frame/yield may
        // run with these patches installed; they cannot drive the test character.
        // Production postfixes must mask the queries while preserving IMGUI input.
        var poison = new Harmony("mikko.valheim.pikalajittelu.validation.pressed-input");
        string[] buttons = { "GetButton", "GetButtonDown", "GetButtonUp", "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp",
            "GetKey", "GetKeyDown", "GetKeyUp", "GetRadialTap", "GetRadialMultiTap", "HasDoubleTapped", "HasActiveLongPress", "IsTouchPressedDown" };
        string[] axes = { "GetMouseScrollWheel", "GetJoyLTrigger", "GetJoyRTrigger", "GetJoyRightStickX", "GetJoyRightStickY", "GetJoyLeftStickX", "GetJoyLeftStickY" };
        string[] vectors = { "GetMouseDelta", "GetJoyRightStick", "GetJoyLeftStick" };
        try
        {
            foreach (string name in buttons.Concat(axes).Concat(vectors))
            {
                MethodInfo method = AccessTools.Method(typeof(ZInput), name);
                string injector = method.ReturnType == typeof(bool) ? "PressedButton" : method.ReturnType == typeof(float) ? "ActiveAxis" : "ActiveVector";
                poison.Patch(method, prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), injector));
                object[] args = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)"Attack" :
                    p.ParameterType == typeof(KeyCode) ? (object)KeyCode.W : p.ParameterType == typeof(bool) ? (object)false : (object)0).ToArray();
                object value = method.Invoke(null, args);
                bool neutral = value is bool ? !(bool)value : value is float ? (float)value == 0f : (Vector2)value == Vector2.zero;
                Check(neutral == blocked, "Gameplay query " + name + " did not " + (blocked ? "block modal input" : "restore input after close"));
            }
        }
        finally { poison.UnpatchSelf(); }
    }

    private IEnumerator ModalInputIsolation(Pikalajittelu plugin, Player player)
    {
        PlayerController controller = player.GetComponent<PlayerController>();
        Check(controller, "Native PlayerController missing");
        Check(!(bool)Call(controller, "TakeInput", false) && !(bool)Call(controller, "TakeInput", true), "Movement/look controller accepted input through preview");
        CheckGameplayInputMask(true);
        Call(plugin, "SetPreviewClosed", false);
        Check(!(bool)Call(controller, "TakeInput", false), "Closing click/Escape leaked into the movement controller");
        CheckGameplayInputMask(true);
        for (int frame = 0; frame < 3; frame++) yield return null;
        Check((bool)Call(controller, "TakeInput", false), "Controller input did not return after close guard");
        CheckGameplayInputMask(false);
        Call(plugin, "OpenStorageManager");
        Check(!(bool)Call(controller, "TakeInput", false), "Alt+P manager did not block the native controller");
        CheckGameplayInputMask(true);
        Call(plugin, "SetPreviewClosed", false);
        for (int frame = 0; frame < 3; frame++) yield return null;
        Set(plugin, "previewVisible", true);
    }
}
