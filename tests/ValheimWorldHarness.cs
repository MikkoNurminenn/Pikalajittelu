using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using MikkoMods;
using MikkoMods.Storage;
using UnityEngine;

public sealed partial class ValheimInventoryHarness
{
    private static object Get(object target, string name) { return AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static void Set(object target, string name, object value) { AccessTools.Field(target.GetType(), name).SetValue(target, value); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }

    private IEnumerator GuardWorld(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count > 0)
        {
            bool more = false; object current = null; Exception failure = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                File.WriteAllText(ResultPath, "WORLD_FAIL\nchecks=" + checks + "\n" + failure);
                Logger.LogError(failure);
                while (stack.Count > 0) { var disposable = stack.Pop() as IDisposable; if (disposable != null) disposable.Dispose(); }
                yield break;
            }
            if (!more) { stack.Pop(); continue; }
            IEnumerator nested = current as IEnumerator;
            if (nested != null) stack.Push(nested); else yield return current;
        }
    }

    private IEnumerator WorldTest()
    {
        string suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        string worldName = "PikalajitteluTest" + suffix;
        string characterFile = "storage_test_" + suffix;
        var profile = new PlayerProfile(characterFile, FileHelpers.FileSource.Local);
        profile.SetName("Storage Test");
        Check(profile.Save(), "Isolated character save failed");
        var world = new World(worldName, "PikalajitteluValidation");
        world.m_fileSource = FileHelpers.FileSource.Local;
        world.SaveWorldFWLData(DateTime.Now);
        Game.SetProfile(characterFile, FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "Pikalajittelu local validation", "", world);
        ZNet.ResetServerHost();
        FejdStartup menu = UnityEngine.Object.FindFirstObjectByType<FejdStartup>();
        Check(menu, "Startup scene not available");
        File.WriteAllText(ResultPath, "WORLD_LOADING\nworld=" + worldName + "\ncharacter=" + characterFile);
        Call(menu, "LoadMainScene");
        float deadline = Time.realtimeSinceStartup + 240f;
        while ((!Player.m_localPlayer || !ZNetScene.instance || !ZNet.instance) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Player.m_localPlayer && ZNetScene.instance && ZNet.instance, "Local world/player did not load in 240 seconds");
        Player player = Player.m_localPlayer;
        Check(ZNet.instance.GetWorldUID() == world.m_uid && ZNet.instance.IsServer(), "Wrong world loaded");
        Check(Path.GetFullPath(Utils.GetSaveDataPath(FileHelpers.FileSource.Local)) == Path.GetFullPath(isolatedSaves), "World save isolation changed");
        player.SetGodMode(true);
        // Use the game's intro skip path, which also releases the valkyrie's
        // transform attachment. Merely clearing Player's intro flag does not.
        Valkyrie intro = UnityEngine.Object.FindFirstObjectByType<Valkyrie>();
        if (intro && !(bool)Get(intro, "m_droppedPlayer")) Call(intro, "DropPlayer", true);
        var body = player.GetComponent<Rigidbody>();
        Pikalajittelu plugin = (Pikalajittelu)Chainloader.PluginInfos[Pikalajittelu.Id].Instance;
        yield return WaitForStablePlayer(player);
        if (body) { body.isKinematic = false; body.linearVelocity = Vector3.zero; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll; }
        File.WriteAllText(ResultPath, "WORLD_TESTING\nworld=" + worldName + "\ncharacter=" + characterFile);

        Container first = SpawnChest(player, new Vector3(2, 0, 0));
        Container second = SpawnChest(player, new Vector3(4, 0, 0));
        Inventory pack = player.GetInventory();
        pack.GetAllItems().Clear();
        var keep = Item("Wood", 10, 0, 0);
        pack.GetAllItems().Add(keep);
        pack.GetAllItems().Add(Item("Tin", 28, 0, 1));
        Inventory a = first.GetInventory(), b = second.GetInventory();
        a.GetAllItems().Clear(); b.GetAllItems().Clear();
        int limit = Item("Tin", 1, 0, 0).m_shared.m_maxStackSize;
        a.GetAllItems().Add(Item("Tin", limit - 5, 0, 0));
        for (int slot = 1; slot < a.GetWidth() * a.GetHeight(); slot++)
        {
            var stone = Item("Stone", 1, slot % a.GetWidth(), slot / a.GetWidth());
            stone.m_stack = stone.m_shared.m_maxStackSize;
            a.GetAllItems().Add(stone);
        }
        NotifyInventory(pack);
        // Player.OnInventoryChanged may mark newly picked-up fixtures. Model an
        // already stored stack of that exact item, not an unpicked prefab template.
        ItemDrop.ItemData packTin = pack.GetAllItems().Single(i => i.m_dropPrefab.name == "Tin");
        ItemDrop.ItemData storedTin = packTin.Clone();
        storedTin.m_stack = limit - 5; storedTin.m_gridPos = new Vector2i(0, 0);
        a.GetAllItems()[0] = storedTin;
        NotifyInventory(a); NotifyInventory(b);
        Check(Fingerprint(packTin) == Fingerprint(storedTin), "Partial-stack fixtures have incompatible metadata");
        byte[] initialPack = Save(pack), initialA = Save(a), initialB = Save(b);
        // Exercise the real ward permission path before any preview/transaction.
        GameObject wardPrefab = ((List<GameObject>)Get(ZNetScene.instance, "m_prefabs"))
            .FirstOrDefault(asset => asset && asset.GetComponent<PrivateArea>() && asset.GetComponent<Piece>());
        Check(wardPrefab, "Ward prefab missing");
        Logger.LogInfo("Using native ward fixture: " + wardPrefab.name);
        GameObject wardObject = UnityEngine.Object.Instantiate(wardPrefab, player.transform.position, Quaternion.identity);
        PrivateArea ward = wardObject.GetComponent<PrivateArea>();
        Check(ward, "Ward component missing");
        Set(wardObject.GetComponent<Piece>(), "m_creator", player.GetPlayerID() + 1);
        wardObject.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, player.GetPlayerID() + 1);
        Call(ward, "SetEnabled", true);
        Check(!PrivateArea.CheckAccess(first.transform.position, 0f, false, false), "Foreign ward did not deny native access");
        yield return Sort(plugin, player, false, false, false);
        Check(!(bool)Get(plugin, "previewVisible"), "Sorting bypassed a foreign ward");
        Check(Save(pack).SequenceEqual(initialPack) && Save(a).SequenceEqual(initialA) && Save(b).SequenceEqual(initialB), "Ward rejection changed items");
        Call(ward, "SetEnabled", false); ZNetScene.instance.Destroy(wardObject);
        yield return Sort(plugin, player, false, false, false);
        Check(Get(plugin, "pendingStorage") != null && (bool)Get(plugin, "previewVisible"), "Deposit preview was not opened");
        Check(Save(pack).SequenceEqual(initialPack) && Save(a).SequenceEqual(initialA) && Save(b).SequenceEqual(initialB), "Preview mutated real inventories");
        yield return ModalInputIsolation(plugin, player);
        if (Environment.GetCommandLineArgs().Contains("-storage-ui-test"))
        {
            yield return CaptureWorldUi(plugin, worldName);
            if (Environment.GetCommandLineArgs().Contains("-storage-ui-input-test"))
                yield return UiEventTest(plugin, player, first, second, worldName);
        }
        Call(plugin, "SetPreviewClosed", false);
        // Exercise the actual confirmation queue used by the GUI. It must not
        // be discarded while the closing-event input guard is still active.
        Set(plugin, "applyRequested", true);
        deadline = Time.realtimeSinceStartup + 20;
        while ((Get(plugin, "lastCommitted") == null || (bool)Get(plugin, "busy")) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!(bool)Get(plugin, "applyRequested") && Get(plugin, "lastCommitted") != null, "Confirmation queue was lost during the modal closing guard");
        Check(!(bool)Get(plugin, "storageFault") && Get(plugin, "lastCommitted") != null, "Real deposit transaction failed");
        Check(Count(pack, "Tin") == 0 && Count(a, "Tin") == limit && Count(b, "Tin") == 23,
            "Real tin distribution mismatch: player=" + Count(pack, "Tin") + ", a=" + Count(a, "Tin") + ", b=" + Count(b, "Tin") + ", limit=" + limit);
        Check(pack.GetAllItems().Contains(keep) && keep.m_stack == 10, "Protected hotbar item identity/count changed");
        VerifyNetwork(first); VerifyNetwork(second);
        if (Environment.GetCommandLineArgs().Contains("-storage-ui-test"))
        {
            Call(plugin, "OpenStorageManager"); Set(plugin, "managerTab", 3);
            yield return CaptureScreen(plugin, worldName, "history-en");
            Call(plugin, "SetPreviewClosed", false);
        }

        yield return Sort(plugin, player, false, false, true);
        Check((bool)Get(plugin, "previewVisible"), "Undo preview did not open");
        Call(plugin, "SetPreviewClosed", false);
        yield return Sort(plugin, player, false, true, true);
        Check(Get(plugin, "lastCommitted") == null && !(bool)Get(plugin, "storageFault"), "Undo did not complete");
        Check(Save(pack).SequenceEqual(initialPack) && Save(a).SequenceEqual(initialA) && Save(b).SequenceEqual(initialB), "Real undo did not restore exact inventory bytes");
        Check(pack.GetAllItems().Contains(keep), "Undo replaced the protected original object");
        VerifyNetwork(first); VerifyNetwork(second);

        // Another player's edit is represented by changing the real chest through
        // its normal inventory callback. This is a stale-state test, not a peer test.
        yield return Sort(plugin, player, false, false, false);
        Call(plugin, "SetPreviewClosed", false);
        yield return Sort(plugin, player, false, true, false);
        b.GetAllItems().Single(i => i.m_dropPrefab.name == "Tin").m_stack -= 1;
        NotifyInventory(b);
        byte[] changedPack = Save(pack), changedA = Save(a), changedB = Save(b);
        yield return Sort(plugin, player, false, false, true);
        Check(!(bool)Get(plugin, "previewVisible"), "Undo accepted a changed real chest");
        Check(Save(pack).SequenceEqual(changedPack) && Save(a).SequenceEqual(changedA) && Save(b).SequenceEqual(changedB), "Rejected undo changed real inventories");

        var named = new NamedChestRule { ChestId = StableId(first), Name = "Metallit", Rule = new ChestRule { Categories = new[] { "Material" } } };
        yield return (IEnumerator)Call(plugin, "SaveReservedChestRule", player, world.m_uid, first, named);
        Check(((StorageRules)Call(plugin, "ReadCurrentRules")).Get(StableId(first)).Name == "Metallit", "Chest editor did not persist its named rule");
        Call(plugin, "SetPreviewClosed", true);
        Container third = SpawnChest(player, new Vector3(-2, 0, 0));
        Container fourth = SpawnChest(player, new Vector3(-4, 0, 0));
        yield return FullChestConsolidation(plugin, player, first, second, third, fourth);
        TestLostOwnershipRelease(plugin, third);
        Game.instance.SavePlayerProfile(true, false);
        ZNet.instance.Save(true, false, false);
        var lines = new List<string> { worldName, characterFile, world.m_uid.ToString() };
        foreach (Container chest in new[] { first, second, third, fourth })
            lines.Add(StableId(chest) + "\t" + Convert.ToBase64String(Save(chest.GetInventory())));
        lines.Add("player\t" + Convert.ToBase64String(Save(pack)));
        File.WriteAllLines(Path.Combine(Paths.GameRootPath, "world-fixture.txt"), lines.ToArray());
        if (Environment.GetCommandLineArgs().Contains("-storage-large-test")) yield return LargeStorageTest(plugin, player, worldName);
        File.WriteAllText(ResultPath, "WORLD_PASS\nchecks=" + checks + "\nworld=" + worldName + "\nReal Player/Container deposit, preview, ZDO save, exact undo, stale-undo rejection, full-chest consolidation and undo.\nWorld restart requires a separate reload run. Remote peer not yet tested.");
    }

    private IEnumerator ReloadWorldTest()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Paths.GameRootPath, "world-fixture.txt"));
        Check(lines.Length >= 6 && lines.Length <= 16 && lines[0].StartsWith("PikalajitteluTest", StringComparison.Ordinal) &&
            lines[1].StartsWith("storage_test_", StringComparison.Ordinal) && !lines[0].Any(c => c == '/' || c == '\\') &&
            !lines[1].Any(c => c == '/' || c == '\\'), "Unexpected world fixture manifest");
        World world = World.GetCreateWorld(lines[0], FileHelpers.FileSource.Local);
        Check(world.m_uid.ToString() == lines[2], "Saved test world was replaced or not found");
        Game.SetProfile(lines[1], FileHelpers.FileSource.Local);
        ZNet.SetServer(true, false, false, "Pikalajittelu local validation", "", world);
        ZNet.ResetServerHost();
        File.WriteAllText(ResultPath, "WORLD_RELOADING\nworld=" + lines[0]);
        Call(UnityEngine.Object.FindFirstObjectByType<FejdStartup>(), "LoadMainScene");
        float deadline = Time.realtimeSinceStartup + 240;
        while ((!Player.m_localPlayer || !ZNetScene.instance || !ZNet.instance) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Player.m_localPlayer && ZNet.instance.GetWorldUID() == world.m_uid, "Saved world/player did not reload");
        Player player = Player.m_localPlayer;
        player.SetGodMode(true); player.SetIntro(false);
        var body = player.GetComponent<Rigidbody>(); if (body) { body.isKinematic = false; body.linearVelocity = Vector3.zero; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll; }
        var expected = lines.Skip(3).Select(row => row.Split('\t')).ToDictionary(pair => pair[0], pair => Convert.FromBase64String(pair[1]));
        var all = new List<ZDO>(); int scan = 0;
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("piece_chest_wood", all, ref scan)) yield return null;
        foreach (var entry in expected.Where(e => e.Key != "player"))
        {
            int key = "mikko.valheim.pikalajittelu.stableChestId".GetStableHashCode();
            ZDO data = all.SingleOrDefault(z => "chest:" + z.GetString(key, "") == entry.Key);
            Check(data != null, "Saved chest ZDO missing after process restart: " + entry.Key);
            Check(data.GetByteArray(ZDOVars.s_items, null).SequenceEqual(entry.Value), "Chest items changed across world restart");
            if (!ZNetScene.instance.FindInstance(data)) player.transform.position = data.GetPosition() + new Vector3(-2, 0, 0);
            deadline = Time.realtimeSinceStartup + 60;
            while (!ZNetScene.instance.FindInstance(data) && Time.realtimeSinceStartup < deadline) yield return null;
            ZNetView view = ZNetScene.instance.FindInstance(data);
            Check(view && view.GetComponent<Container>(), "Saved chest instance did not load");
            yield return null;
            Container chest = view.GetComponent<Container>();
            Check(Save(chest.GetInventory()).SequenceEqual(entry.Value), "Actual chest inventory changed after world reload");
            VerifyNetwork(chest);
        }
        Check(Save(player.GetInventory()).SequenceEqual(expected["player"]), "Player inventory changed across process restart");
        Pikalajittelu plugin = (Pikalajittelu)Chainloader.PluginInfos[Pikalajittelu.Id].Instance;
        var rules = (StorageRules)Call(plugin, "ReadCurrentRules");
        Check(rules.Get(lines[3].Split('\t')[0]).Name == "Metallit" && rules.Get(lines[3].Split('\t')[0]).Rule.Categories.Contains("Material"),
            "Named chest rule did not survive world/process restart");
        File.WriteAllText(ResultPath, "WORLD_RELOAD_PASS\nchecks=" + checks + "\nworld=" + lines[0] +
            "\nSaved world, actual chest instances, ZDO inventories and player inventory matched after a separate process restart.\nRemote peer not tested.");
    }

    private static IEnumerator Sort(Pikalajittelu plugin, Player player, bool consolidate, bool apply, bool undo)
    {
        while (!(bool)Get(plugin, "previewVisible") && !(bool)Get(plugin, "managerVisible") &&
            Time.frameCount <= (int)Get(plugin, "blockInputThroughFrame")) yield return null;
        yield return (IEnumerator)Call(plugin, "Organize", player, consolidate, null, null, apply, undo);
    }

    private IEnumerator WaitForStablePlayer(Player player)
    {
        // Native spawn/intro callbacks can change readiness on later frames.
        // Require a short stable interval after skipping, not a one-frame result.
        MethodInfo ready = AccessTools.Method(typeof(Pikalajittelu), "Ready");
        float deadline = Time.realtimeSinceStartup + 90;
        float stableSince = -1;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool usable = (bool)ready.Invoke(null, new object[] { player }) &&
                !UnityEngine.Object.FindFirstObjectByType<Valkyrie>();
            if (!usable) stableSince = -1;
            else if (stableSince < 0) stableSince = Time.realtimeSinceStartup;
            else if (Time.realtimeSinceStartup - stableSince >= 2f) { Check(true, "Stable player ready"); yield break; }
            yield return null;
        }
        Check(false, "Player did not stabilize after intro: cutscene=" + player.InCutscene() + ", dead=" + player.IsDead() + ", input=" + Call(player, "TakeInput"));
    }

    private IEnumerator FullChestConsolidation(Pikalajittelu plugin, Player player, Container first, Container second, Container third, Container fourth)
    {
        var rules = (StorageRules)Call(plugin, "ReadCurrentRules");
        rules.Get(StableId(first)).Rule.Locked = true;
        rules.Chests[StableId(second)] = new NamedChestRule { ChestId = StableId(second), Rule = new ChestRule { Locked = true } };
        rules.Save((string)Call(plugin, "RulesPath", ZNet.instance.GetWorldUID(), player.GetPlayerID()));
        var exclusion = (BepInEx.Configuration.ConfigEntry<string>)Get(plugin, "excluded");
        string previous = exclusion.Value;
        exclusion.Value = "Resin";
        try
        {
            Inventory c = third.GetInventory(), d = fourth.GetInventory();
            foreach (Inventory inventory in new[] { c, d })
            {
                inventory.GetAllItems().Clear();
                inventory.GetAllItems().Add(Item("Wood", inventory == c ? 20 : 10, 0, 0));
                inventory.GetAllItems().Add(Item("Stone", inventory == c ? 10 : 20, 1, 0));
                for (int slot = 2; slot < inventory.GetWidth() * inventory.GetHeight(); slot++)
                {
                    var resin = Item("Resin", 1, slot % inventory.GetWidth(), slot / inventory.GetWidth());
                    resin.m_stack = resin.m_shared.m_maxStackSize;
                    inventory.GetAllItems().Add(resin);
                }
                NotifyInventory(inventory);
            }
            byte[] beforeC = Save(c), beforeD = Save(d), pack = Save(player.GetInventory());
            byte[] lockedA = Save(first.GetInventory()), lockedB = Save(second.GetInventory());
            yield return Sort(plugin, player, true, false, false);
            Check((bool)Get(plugin, "previewVisible"), "Full-chest consolidation did not produce a preview");
            Check(Save(c).SequenceEqual(beforeC) && Save(d).SequenceEqual(beforeD), "Consolidation preview changed inventories");
            Call(plugin, "SetPreviewClosed", false);
            yield return Sort(plugin, player, true, true, false);
            Check(!(bool)Get(plugin, "storageFault") && Get(plugin, "lastCommitted") != null, "Full-chest consolidation failed");
            Check(Count(c, "Wood") == 30 && Count(c, "Stone") == 0 && Count(d, "Stone") == 30 && Count(d, "Wood") == 0,
                "Full chests did not exchange items according to existing majority");
            foreach (Inventory inventory in new[] { c, d })
                Check(inventory.GetAllItems().Where(i => i.m_dropPrefab.name == "Resin").Count(i =>
                    i.m_stack == i.m_shared.m_maxStackSize && i.m_gridPos.y * inventory.GetWidth() + i.m_gridPos.x >= 2) == 8,
                    "Protected filler stacks changed slots or count");
            Check(Save(player.GetInventory()).SequenceEqual(pack), "Consolidation changed player inventory");
            Check(Save(first.GetInventory()).SequenceEqual(lockedA) && Save(second.GetInventory()).SequenceEqual(lockedB), "Consolidation changed locked chests");
            VerifyNetwork(third); VerifyNetwork(fourth);
            yield return Sort(plugin, player, true, false, true);
            Check((bool)Get(plugin, "previewVisible"), "Consolidation undo preview missing");
            Call(plugin, "SetPreviewClosed", false);
            yield return Sort(plugin, player, true, true, true);
            Check(Get(plugin, "lastCommitted") == null && !(bool)Get(plugin, "storageFault"), "Consolidation undo failed");
            Check(Save(c).SequenceEqual(beforeC) && Save(d).SequenceEqual(beforeD), "Consolidation undo did not restore exact full chests");
            VerifyNetwork(third); VerifyNetwork(fourth);
        }
        finally { exclusion.Value = previous; }
    }

    private void TestLostOwnershipRelease(Pikalajittelu plugin, Container chest)
    {
        // Fault injection within this local test world; this does not emulate
        // network transport or prove a second client's behavior.
        var reservations = (HashSet<Container>)Get(plugin, "reserved");
        ZDO data = chest.GetComponent<ZNetView>().GetZDO();
        long owner = data.GetOwner();
        byte[] before = Save(chest.GetInventory());
        reservations.Add(chest); chest.SetInUse(true);
        Check(chest.IsInUse(), "Test lease was not set");
        try
        {
            data.SetOwner(owner == 123456789L ? 987654321L : 123456789L);
            int remoteUse = data.GetInt(ZDOVars.s_inUse, 0);
            Call(plugin, "ReleaseReservations");
            Check(!chest.IsInUse() && reservations.Count == 0, "Lost ownership left a local lease stuck");
            Check(data.GetInt(ZDOVars.s_inUse, 0) == remoteUse, "Release wrote the new owner's use state");
            Check(Save(chest.GetInventory()).SequenceEqual(before), "Lost-owner release changed items");
        }
        finally
        {
            data.SetOwner(owner);
            Call(chest, "UpdateUseVisual");
            reservations.Remove(chest);
        }
        Check(data.GetInt(ZDOVars.s_inUse, 0) == 0, "Returning owner retained stale use state");
    }

    private IEnumerator CaptureWorldUi(Pikalajittelu plugin, string worldName)
    {
        Check(UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "UI test requires a graphics device");
        var language = (BepInEx.Configuration.ConfigEntry<string>)Get(plugin, "uiLanguage");
        var scale = (BepInEx.Configuration.ConfigEntry<float>)Get(plugin, "uiScale");
        language.Value = "Suomi"; scale.Value = 1f;
        yield return CaptureScreen(plugin, worldName, "preview-fi");
        Check(!(bool)AccessTools.Method(typeof(Pikalajittelu), "Ready").Invoke(null, new object[] { Player.m_localPlayer }), "Preview did not block player input");
        Check(!(bool)Call(Player.m_localPlayer, "TakeInput"), "Actual Player.TakeInput was not blocked by the preview");
        Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Modal did not release the cursor");
        language.Value = "English";
        yield return CaptureScreen(plugin, worldName, "preview-en");
        Call(plugin, "SetPreviewClosed", false);
        Call(plugin, "OpenStorageManager"); Set(plugin, "search", "Tin");
        yield return CaptureScreen(plugin, worldName, "search-en");
        Set(plugin, "managerTab", 1);
        var observed = (IList)Get(plugin, "observed");
        Check(observed.Count == 2, "Storage manager did not list the fixture chests");
        Call(plugin, "SelectChest", observed[0]);
        yield return CaptureScreen(plugin, worldName, "chest-editor-en");
        Set(plugin, "managerTab", 2);
        scale.Value = 1.5f; Set(plugin, "settingsScale", 1.5f);
        yield return CaptureScreen(plugin, worldName, "settings-en-large-text");
        Screen.SetResolution(1280, 720, false);
        for (int frame = 0; frame < 20; frame++) yield return null;
        Check(Screen.width == 1280 && Screen.height == 720, "Small-window UI test did not change resolution");
        Set(plugin, "managerTab", 1);
        yield return CaptureScreen(plugin, worldName, "chest-editor-en-720p-large-text");
        Screen.SetResolution(1600, 900, false);
        for (int frame = 0; frame < 20; frame++) yield return null;
        scale.Value = 1f;
        Call(plugin, "SetPreviewClosed", false);
        Check(!(bool)Call(Player.m_localPlayer, "TakeInput"), "Closing event was not consumed");
        for (int frame = 0; frame < 3; frame++) yield return null;
        Check((bool)Call(Player.m_localPlayer, "TakeInput"), "Player input stayed blocked after the modal closed");
        Set(plugin, "previewVisible", true);
    }

    private IEnumerator CaptureScreen(Pikalajittelu plugin, string worldName, string name)
    {
        string directory = Path.Combine(Paths.GameRootPath, "screenshots", worldName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".png");
        for (int frame = 0; frame < 10; frame++) yield return null;
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 20;
        while ((!File.Exists(path) || new FileInfo(path).Length < 1000) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(File.Exists(path) && new FileInfo(path).Length >= 1000, "Screenshot was not produced: " + name);
        var pixels = new Texture2D(2, 2);
        try
        {
            Check(ImageConversion.LoadImage(pixels, File.ReadAllBytes(path)), "Screenshot could not be decoded: " + name);
            Check(pixels.width == Screen.width && pixels.height == Screen.height, "Screenshot dimensions differ from game surface");
            Check(pixels.GetPixels32().Count(p => p.r > 40 || p.g > 40 || p.b > 40) > 1000,
                "Screenshot is blank; UI rendering was not verified: " + name);
        }
        finally { UnityEngine.Object.Destroy(pixels); }
        File.AppendAllText(Path.Combine(directory, "index.txt"), name + "\t" + Screen.width + "x" + Screen.height + "\n");
    }
    private static string StableId(Container chest)
    { return (string)AccessTools.Method(typeof(Pikalajittelu), "ChestId").Invoke(null, new object[] { chest, false }); }
    private Container SpawnChest(Player player, Vector3 offset)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
        Check(prefab, "Wood chest prefab unavailable");
        GameObject obj = UnityEngine.Object.Instantiate(prefab, player.transform.position + offset, Quaternion.identity);
        Container chest = obj.GetComponent<Container>();
        ZNetView view = obj.GetComponent<ZNetView>();
        Check(chest && view && view.IsValid() && view.IsOwner(), "Test chest did not acquire a valid locally owned ZDO");
        // Fixture setup: mark this new piece as player-built, as normal placement does.
        Set(obj.GetComponent<Piece>(), "m_creator", player.GetPlayerID());
        view.GetZDO().Set(ZDOVars.s_creator, player.GetPlayerID());
        WearNTear wear = obj.GetComponent<WearNTear>(); if (wear) wear.enabled = false;
        return chest;
    }
    private static int Count(Inventory inventory, string name)
    { return inventory.GetAllItems().Where(i => i.m_dropPrefab && i.m_dropPrefab.name == name).Sum(i => i.m_stack); }
    private static void NotifyInventory(Inventory inventory)
    { AccessTools.Method(typeof(Inventory), "Changed").Invoke(inventory, new object[] { false, false }); }
    private void VerifyNetwork(Container chest)
    {
        byte[] stored = chest.GetComponent<ZNetView>().GetZDO().GetByteArray(ZDOVars.s_items, null);
        Check(stored != null && stored.SequenceEqual(Save(chest.GetInventory())), "Live inventory and persisted ZDO differ");
        var loaded = new Inventory("ZDO roundtrip", null, chest.GetInventory().GetWidth(), chest.GetInventory().GetHeight());
        loaded.Load(new ZPackage(stored));
        Check(Save(loaded).SequenceEqual(stored), "Stored chest inventory changed on native reload");
    }
}
