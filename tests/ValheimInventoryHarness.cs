using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using MikkoMods;
using MikkoMods.Storage;
using UnityEngine;
using Stack = MikkoMods.Storage.Stack;

[BepInPlugin("mikko.valheim.pikalajittelu.validation", "Pikalajittelu isolated validation", "1.0.0")]
[BepInDependency(Pikalajittelu.Id)]
public sealed partial class ValheimInventoryHarness : BaseUnityPlugin
{
    private int checks;
    private bool validationActive;
    private string isolatedSaves;
    private string ResultPath { get { return PeerRole == null ? Path.Combine(Paths.GameRootPath, "validation-result.txt") : Path.Combine(PeerRoot, PeerRole + "-result.txt"); } }
    private void Awake()
    {
        if (!File.Exists(Path.Combine(Paths.GameRootPath, "ISOLATED-VALIDATION"))) return;
        validationActive = true;
        if (Environment.GetCommandLineArgs().Contains("-storage-ui-input-test")) InstallGuiEventProbe();
        isolatedSaves = Path.Combine(Paths.GameRootPath, "validation-saves");
        if (PeerRole != null) { Directory.CreateDirectory(PeerRoot); isolatedSaves = Path.Combine(isolatedSaves, PeerRunId + "-" + PeerRole); }
        Directory.CreateDirectory(isolatedSaves);
        Utils.SetSaveDataPath(isolatedSaves);
        var patches = new Harmony("mikko.valheim.pikalajittelu.validation.isolation");
        foreach (string property in new[] { "CloudStorageSupported", "CloudStorageSupportedAndEnabled" })
            patches.Patch(AccessTools.PropertyGetter(typeof(FileHelpers), property), prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "NoCloud"));
        if (PeerRole == "client") patches.Patch(AccessTools.Method(typeof(ZNet), "ClientConnect"),
            prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "ConnectLoopback"));
    }
    private static bool NoCloud(ref bool __result) { __result = false; return false; }
    private void Update()
    {
        if (!validationActive) return;
        // User preference: all isolated test runs skip startup cinematics and
        // intro text. Use the game's own close/skip paths, leaving input gates intact.
        if (UnityEngine.Object.FindFirstObjectByType<CinematicsManager>() && CinematicsManager.IsPlaying())
        {
            CinematicsManager.Stop();
            Logger.LogInfo("Skipped isolated test startup cinematic.");
        }
        if (TextViewer.instance)
        {
            var introText = (Animator)Get(TextViewer.instance, "m_animatorIntro");
            if (introText && introText.gameObject.activeSelf && TextViewer.IsShowingIntro())
            {
                TextViewer.instance.HideIntro();
                Logger.LogInfo("Skipped isolated test introduction text.");
            }
        }
        if (Player.m_localPlayer)
        {
            Valkyrie intro = UnityEngine.Object.FindFirstObjectByType<Valkyrie>();
            if (intro && !(bool)Get(intro, "m_droppedPlayer")) Call(intro, "DropPlayer", true);
        }
    }
    private void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    private IEnumerator Start()
    {
        // This assembly is only copied to an isolated client, never the installed
        // game. Require an explicit marker before allowing the harness to run/quit.
        if (!File.Exists(Path.Combine(Paths.GameRootPath, "ISOLATED-VALIDATION"))) yield break;
        File.WriteAllText(ResultPath, "WAITING_FOR_OBJECTDB\n");
        float deadline = Time.realtimeSinceStartup + 120f;
        while ((!ObjectDB.instance || !ObjectDB.instance.GetItemPrefab("Wood")) && Time.realtimeSinceStartup < deadline) yield return null;
        bool passed = false;
        try
        {
            Check(ObjectDB.instance && ObjectDB.instance.GetItemPrefab("Wood"), "ObjectDB not ready within 120 seconds");
            Check(Path.GetFullPath(Utils.GetSaveDataPath(FileHelpers.FileSource.Local)) == Path.GetFullPath(isolatedSaves), "Save directory isolation failed");
            Check(!FileHelpers.CloudStorageSupported && !FileHelpers.CloudStorageSupportedAndEnabled, "Cloud storage isolation failed");
            Run();
            File.WriteAllText(ResultPath, "PASS\nchecks=" + checks + "\nRuntime=" + Application.unityVersion +
                "\nOnly isolated inventories were used. No player/chest/network transaction claims.\n");
            Logger.LogInfo("Pikalajittelu actual-inventory tests PASS: " + checks);
            passed = true;
        }
        catch (Exception error)
        {
            Logger.LogError(error);
            File.WriteAllText(ResultPath, "FAIL\nchecks=" + checks + "\n" + error);
        }
        if (passed && Environment.GetCommandLineArgs().Contains("-storage-world-test")) yield return GuardWorld(WorldTest());
        if (passed && Environment.GetCommandLineArgs().Contains("-storage-world-reload")) yield return GuardWorld(ReloadWorldTest());
        if (passed && PeerRole != null) yield return GuardWorld(PeerTest());
        Application.Quit();
    }
    private static ItemDrop.ItemData Item(string prefab, int count, int x, int y)
    {
        GameObject asset = ObjectDB.instance.GetItemPrefab(prefab);
        ItemDrop.ItemData item = asset.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = asset; item.m_stack = count; item.m_gridPos = new Vector2i(x, y);
        return item;
    }
    private static byte[] Save(Inventory inventory)
    {
        var package = new ZPackage(); inventory.Save(package); return package.GetArray();
    }
    private static string Fingerprint(ItemDrop.ItemData item)
    {
        return (string)typeof(Pikalajittelu).GetMethod("ItemFingerprint", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { item });
    }
    private void Run()
    {
        var source = new Inventory("Isolated source", null, 8, 4);
        var item = Item("Wood", 28, 6, 3);
        item.m_customData = new Dictionary<string, string> { { "second", "öä|line\nvalue" }, { "first", "unchanged" } };
        item.m_crafterID = 123456789L; item.m_crafterName = "Testihahmo test"; item.m_variant = 1;
        item.m_worldLevel = 1; item.m_pickedUp = true; item.m_cheated = true;
        source.GetAllItems().Add(item);
        var restored = new Inventory("Isolated restored", null, 8, 4);
        restored.Load(new ZPackage(Save(source)));
        Check(restored.GetAllItems().Count == 1, "Native load discarded a stack");
        var loaded = restored.GetAllItems()[0];
        Check(loaded.m_stack == 28 && loaded.m_gridPos.x == 6 && loaded.m_gridPos.y == 3, "Native load changed amount/position");
        Check(Fingerprint(loaded) == Fingerprint(item), "Native save/load changed item metadata");
        Check(loaded.m_shared != null && loaded.m_dropPrefab, "Native load did not hydrate item references");
        var reordered = item.Clone();
        reordered.m_customData = new Dictionary<string, string> { { "first", "unchanged" }, { "second", "öä|line\nvalue" } };
        Check(Fingerprint(item) == Fingerprint(reordered), "Dictionary insertion order changed item identity");
        reordered.m_stack = 1; reordered.m_gridPos = new Vector2i(0, 0);
        Check(Fingerprint(item) == Fingerprint(reordered), "Stack count/position must not affect merge identity");
        reordered.m_customData["first"] = "changed";
        Check(Fingerprint(item) != Fingerprint(reordered), "Custom data not included in merge identity");
        var cheated = item.Clone(); cheated.m_cheated = false;
        Check(Fingerprint(item) != Fingerprint(cheated), "Cheat provenance not included in merge identity");

        var tin = Item("Tin", 28, 0, 0);
        var tin45 = Item("Tin", 25, 0, 0); // Valheim tin's real stack limit comes from the asset.
        int limit = tin.m_shared.m_maxStackSize;
        tin45.m_stack = limit - 5;
        var bins = new[] {
            new Bin { Id = "player", Capacity = 8, Player = true, Items = new List<Stack> { Model("tin", tin) } },
            new Bin { Id = "a", Capacity = 1, Items = new List<Stack> { Model("existing", tin45) } },
            new Bin { Id = "b", Capacity = 4 }
        };
        Plan plan = StoragePlanner.Deposit(bins, null);
        Check(plan.Valid && plan.Moved == 28 && plan.Remaining == 0, "Real tin metadata did not allow the 28-tin plan");
        int total = 0;
        foreach (Bin bin in plan.After)
        {
            var inv = new Inventory("Isolated plan " + bin.Id, null, bin.Capacity, 1);
            foreach (Stack stack in bin.Items)
            {
                var copy = tin.Clone(); copy.m_stack = stack.Count; copy.m_gridPos = new Vector2i(stack.Slot, 0);
                inv.GetAllItems().Add(copy);
            }
            var roundtrip = new Inventory("Isolated reload", null, bin.Capacity, 1);
            roundtrip.Load(new ZPackage(Save(inv)));
            Check(roundtrip.GetAllItems().Sum(i => i.m_stack) == bin.Items.Sum(i => i.Count), "Native reload lost planned tin");
            total += roundtrip.GetAllItems().Sum(i => i.m_stack);
        }
        Check(total == 28 + limit - 5, "Tin conservation after real native save/load failed");
        // The new transaction changes all lists before invoking Changed. Validate
        // that its callback boundary observes a stable total in real Inventory.
        var a = new Inventory("callback-a", null, 2, 1); var b = new Inventory("callback-b", null, 2, 1);
        var original = Item("Wood", 28, 0, 0); a.GetAllItems().Add(original);
        var changed = typeof(Inventory).GetMethod("Changed", BindingFlags.Instance | BindingFlags.NonPublic);
        var callback = typeof(Inventory).GetField("m_onChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Action observe = () => Check(a.GetAllItems().Sum(i => i.m_stack) + b.GetAllItems().Sum(i => i.m_stack) == 28, "Callback observed loss or duplication");
        callback.SetValue(a, observe); callback.SetValue(b, observe);
        a.GetAllItems().Clear(); b.GetAllItems().Add(original.Clone());
        changed.Invoke(a, new object[] { false, false }); changed.Invoke(b, new object[] { false, false });
        Check(!ReferenceEquals(original, b.GetAllItems()[0]), "Destination must use a detached item");
        TestPlayerLayout();
    }
    private static Stack Model(string template, ItemDrop.ItemData item)
    {
        return new Stack { Template = template, Key = Fingerprint(item), Prefab = item.m_dropPrefab.name,
            Category = item.m_shared.m_itemType.ToString(), Count = item.m_stack, Limit = item.m_shared.m_maxStackSize, Slot = 0 };
    }

    private void TestPlayerLayout()
    {
        var inventory = new Inventory("Isolated player layout", null, 8, 4);
        var kept = Item("Wood", 10, 0, 0);
        kept.m_equipped = true; // Reference preservation is tested without a Player.
        var tin = Item("Tin", 28, 0, 1);
        inventory.GetAllItems().Add(kept); inventory.GetAllItems().Add(tin);
        Type stateType = typeof(Pikalajittelu).GetNestedType("StorageState", BindingFlags.NonPublic);
        object state = Activator.CreateInstance(stateType, true);
        stateType.GetField("Live").SetValue(state, inventory);
        stateType.GetField("Original").SetValue(state, inventory.GetAllItems().ToArray());
        stateType.GetField("Planned").SetValue(state, new List<ItemDrop.ItemData> { kept.Clone() });
        MethodInfo apply = typeof(Pikalajittelu).GetMethod("ApplyPlayerLayout", BindingFlags.NonPublic | BindingFlags.Static);
        apply.Invoke(null, new[] { state });
        Check(inventory.GetAllItems().Count == 1 && ReferenceEquals(inventory.GetAllItems()[0], kept), "Deposit replaced retained equipped object");
        stateType.GetField("Original").SetValue(state, inventory.GetAllItems().ToArray());
        stateType.GetField("Planned").SetValue(state, new List<ItemDrop.ItemData> { kept.Clone(), tin.Clone() });
        apply.Invoke(null, new[] { state });
        Check(ReferenceEquals(inventory.GetAllItems()[0], kept), "Undo replaced retained equipped object");
        Check(inventory.GetAllItems()[1].m_stack == 28 && !ReferenceEquals(inventory.GetAllItems()[1], tin), "Undo must return 28 tin as a detached object");
        var partial = tin.Clone(); partial.m_stack = 9;
        stateType.GetField("Original").SetValue(state, inventory.GetAllItems().ToArray());
        stateType.GetField("Planned").SetValue(state, new List<ItemDrop.ItemData> { kept.Clone(), partial });
        var existing = inventory.GetAllItems()[1];
        apply.Invoke(null, new[] { state });
        Check(ReferenceEquals(existing, inventory.GetAllItems()[1]) && existing.m_stack == 9, "Partial deposit lost remaining object's identity/count");
    }
}
