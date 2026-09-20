using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using MikkoMods;
using UnityEngine;

public sealed partial class ValheimInventoryHarness
{
    private static bool observeStorageGui;
    private static readonly Dictionary<string, Rect> guiButtons = new Dictionary<string, Rect>();
    private static readonly List<Rect> guiFields = new List<Rect>();
    private static Rect guiTabs;
    private static readonly Queue<Event> widgetEvents = new Queue<Event>();
    private static Event originalGuiEvent;
    private static void BeginGuiObservation(Pikalajittelu __instance)
    {
        observeStorageGui = (bool)Get(__instance, "previewVisible") || (bool)Get(__instance, "managerVisible");
        if (observeStorageGui && Event.current.type == EventType.Repaint) { guiButtons.Clear(); guiFields.Clear(); }
        if (observeStorageGui && Event.current.type == EventType.Repaint && widgetEvents.Count > 0)
        {
            // Widget integration test, not OS input or native window dispatch.
            // Event.QueueEvent is not dispatched to player IMGUI on this build.
            originalGuiEvent = new Event(Event.current);
            Event injected = widgetEvents.Dequeue();
            if (injected.isMouse)
            {
                Vector2 origin = (Vector2)AccessTools.Method(typeof(GUIUtility), "InternalWindowToScreenPoint").Invoke(null, new object[] { Vector2.zero });
                injected.mousePosition = GUIUtility.ScreenToGUIPoint(injected.mousePosition + origin);
            }
            Event.current = injected;
        }
    }
    private static void EndGuiObservation()
    {
        if (originalGuiEvent != null) { Event.current = originalGuiEvent; originalGuiEvent = null; }
        observeStorageGui = false;
    }
    private static Rect ScreenRect(Rect local)
    {
        Vector2 a = GUIUtility.GUIToScreenPoint(local.min), b = GUIUtility.GUIToScreenPoint(local.max);
        // GUIToScreenPoint includes the OS window origin; the runtime event queue
        // expects coordinates relative to the game's client surface.
        Vector2 origin = (Vector2)AccessTools.Method(typeof(GUIUtility), "InternalWindowToScreenPoint").Invoke(null, new object[] { Vector2.zero });
        a -= origin; b -= origin;
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }
    private static void ObserveButton(GUIContent __0)
    {
        if (observeStorageGui && Event.current.type == EventType.Repaint && GUI.enabled)
            guiButtons[__0.text] = ScreenRect(GUILayoutUtility.GetLastRect());
    }
    private static void ObserveField()
    {
        if (observeStorageGui && Event.current.type == EventType.Repaint) guiFields.Add(ScreenRect(GUILayoutUtility.GetLastRect()));
    }
    private static void ObserveTabs()
    {
        if (observeStorageGui && Event.current.type == EventType.Repaint) guiTabs = ScreenRect(GUILayoutUtility.GetLastRect());
    }
    private static void QueueGuiEvent(Event item)
    { widgetEvents.Enqueue(item); }
    private IEnumerator ClickGui(Rect rect)
    {
        Check(rect.width > 1 && rect.height > 1, "UI control has no rendered rectangle");
        Debug.Log("UI_TEST_CLICK " + rect);
        QueueGuiEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = rect.center });
        for (int frame = 0; frame < 3; frame++) yield return null;
        QueueGuiEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = rect.center });
        for (int frame = 0; frame < 8; frame++) yield return null;
    }
    private IEnumerator ClickGuiButton(string label)
    {
        Check(guiButtons.ContainsKey(label), "Rendered UI button missing: " + label + "; observed=" + String.Join(",", guiButtons.Keys.ToArray()));
        yield return ClickGui(guiButtons[label]);
    }
    private IEnumerator TypeGui(string value)
    {
        foreach (char character in value)
        {
            QueueGuiEvent(new Event { type = EventType.KeyDown, character = character });
            yield return null;
            QueueGuiEvent(new Event { type = EventType.KeyUp, character = character });
            yield return null;
        }
        for (int frame = 0; frame < 4; frame++) yield return null;
    }
    private static void InstallGuiEventProbe()
    {
        var patches = new Harmony("mikko.valheim.pikalajittelu.validation.gui-events");
        // ModalWindow callbacks may run after the outer OnGUI returns. Scope
        // observations to the actual window drawing callbacks instead.
        foreach (string method in new[] { "DrawStoragePreview", "DrawStorageManager" })
            patches.Patch(AccessTools.Method(typeof(Pikalajittelu), method), prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "BeginGuiObservation"), postfix: new HarmonyMethod(typeof(ValheimInventoryHarness), "EndGuiObservation"));
        patches.Patch(AccessTools.Method(typeof(GUILayout), "DoButton"), postfix: new HarmonyMethod(typeof(ValheimInventoryHarness), "ObserveButton"));
        patches.Patch(AccessTools.Method(typeof(GUILayout), "DoTextField"), postfix: new HarmonyMethod(typeof(ValheimInventoryHarness), "ObserveField"));
        patches.Patch(AccessTools.Method(typeof(GUILayout), "Toolbar", new[] { typeof(int), typeof(string[]), typeof(GUILayoutOption[]) }), postfix: new HarmonyMethod(typeof(ValheimInventoryHarness), "ObserveTabs"));
    }
    private IEnumerator UiEventTest(Pikalajittelu plugin, Player player, Container first, Container second, string worldName)
    {
        var patches = new Harmony("mikko.valheim.pikalajittelu.validation.gui-events");
        byte[] beforePack = Save(player.GetInventory()), beforeFirst = Save(first.GetInventory()), beforeSecond = Save(second.GetInventory());
        try
        {
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return ClickGuiButton("Cancel");
            Check(!(bool)Get(plugin, "previewVisible") && Get(plugin, "pendingStorage") == null, "Mouse Cancel did not close/discard preview");
            Check(Save(player.GetInventory()).SequenceEqual(beforePack) && Save(first.GetInventory()).SequenceEqual(beforeFirst) && Save(second.GetInventory()).SequenceEqual(beforeSecond), "Cancel moved items");
            Check((bool)Call(player, "TakeInput"), "Cancel left player input blocked");
            yield return Sort(plugin, player, false, false, false);
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return ClickGuiButton("Confirm transfer");
            float deadline = Time.realtimeSinceStartup + 20;
            while (((bool)Get(plugin, "busy") || Get(plugin, "lastCommitted") == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Get(plugin, "lastCommitted") != null && Count(player.GetInventory(), "Tin") == 0, "Mouse Confirm did not commit via normal Update/coroutine");
            VerifyNetwork(first); VerifyNetwork(second);
            yield return Sort(plugin, player, false, false, true);
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return ClickGuiButton("Confirm transfer");
            deadline = Time.realtimeSinceStartup + 20;
            while (((bool)Get(plugin, "busy") || Get(plugin, "lastCommitted") != null) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Save(player.GetInventory()).SequenceEqual(beforePack) && Save(first.GetInventory()).SequenceEqual(beforeFirst) && Save(second.GetInventory()).SequenceEqual(beforeSecond), "Mouse undo did not restore exact inventories");
            Call(plugin, "OpenStorageManager");
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check(guiFields.Count == 1, "Search field not rendered");
            yield return ClickGui(guiFields[0]);
            yield return TypeGui("Tin");
            Check((string)Get(plugin, "search") == "Tin", "Keyboard events did not reach the focused search field");
            Check(!(bool)Call(player, "TakeInput"), "Typing leaked through to player input");
            yield return CaptureScreen(plugin, worldName, "events-search-en");
            Rect settings = guiTabs; settings.x += settings.width * 0.5f; settings.width *= 0.25f;
            yield return ClickGui(settings);
            Check((int)Get(plugin, "managerTab") == 2, "Mouse toolbar did not select Settings");
            yield return ClickGuiButton("P");
            QueueGuiEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.F8 });
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check(((KeyboardShortcut[])Get(plugin, "settingsKeys"))[0].MainKey == KeyCode.F8 && (int)Get(plugin, "captureBinding") == -1, "Key binding capture did not consume F8");
            yield return ClickGuiButton("F8");
            QueueGuiEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
            for (int frame = 0; frame < 8; frame++) yield return null;
            Check((int)Get(plugin, "captureBinding") == -1 && ((KeyboardShortcut[])Get(plugin, "settingsKeys"))[0].MainKey == KeyCode.F8, "Escape did not cancel binding capture");
            yield return ClickGuiButton("F8");
            QueueGuiEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.P });
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return ClickGuiButton("Save settings");
            Check((string)Get(plugin, "settingsMessage") == "Settings saved.", "Mouse settings save did not succeed");
            yield return CaptureScreen(plugin, worldName, "events-settings-en");
            yield return ClickGuiButton("Close");
            Check(!(bool)Get(plugin, "managerVisible") && (bool)Call(player, "TakeInput"), "Mouse close did not restore player input");
            yield return Sort(plugin, player, false, false, false);
        }
        finally { observeStorageGui = false; widgetEvents.Clear(); patches.UnpatchSelf(); }
    }
}
