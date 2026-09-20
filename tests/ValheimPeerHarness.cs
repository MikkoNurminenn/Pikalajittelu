using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using MikkoMods;
using UnityEngine;

public sealed partial class ValheimInventoryHarness
{
    private static Container delayedStackChest;
    private static bool holdStackRequest;
    private static long delayedStackSender, delayedStackPlayer;
    private static bool DisconnectClient { get { return Environment.GetCommandLineArgs().Contains("-storage-disconnect=client"); } }
    private static bool DelayNativeStackRequest(Container __instance, long __0, long __1)
    {
        if (!holdStackRequest || __instance != delayedStackChest) return true;
        delayedStackSender = __0; delayedStackPlayer = __1;
        Signal(File.Exists(SignalPath("client-pass")) ? "disconnect-request-held" : "request-held");
        return false;
    }
    private static string PeerRole { get { return Environment.GetCommandLineArgs().Contains("-storage-peer-host") ? "host" : Environment.GetCommandLineArgs().Contains("-storage-peer-client") ? "client" : null; } }
    private static string PeerRunId
    {
        get
        {
            string option = Environment.GetCommandLineArgs().Single(a => a.StartsWith("-storage-peer-run=", StringComparison.Ordinal));
            string id = option.Substring("-storage-peer-run=".Length);
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidOperationException("Invalid isolated peer run ID");
            return id;
        }
    }
    private static string PeerRoot { get { return Path.Combine(Path.GetDirectoryName(Paths.GameRootPath), "peer-data", PeerRunId); } }
    private static bool ConnectLoopback(ZNet __instance)
    {
        // CustomSocket's legacy ZConnector2 path is no longer polled by ZNet.Update.
        // Bootstrap the loopback socket only; native handshake/RPC/ZDO remain intact.
        var connection = new TcpClient(AddressFamily.InterNetwork);
        connection.Connect(IPAddress.Loopback, Int32.Parse(File.ReadAllText(SignalPath("listen"))));
        var socket = new ZSocket2(connection, "127.0.0.1");
        AccessTools.Method(typeof(ZNet), "Connect", new[] { typeof(ISocket) }).Invoke(__instance, new object[] { socket });
        return false;
    }
    private static string SignalPath(string name) { return Path.Combine(PeerRoot, name + ".txt"); }
    private static void Signal(string name, string value = "ready") { File.WriteAllText(SignalPath(name), value); }
    private IEnumerator WaitSignal(string name, float timeout = 120)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!File.Exists(SignalPath(name)) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(File.Exists(SignalPath(name)), "Peer timed out waiting for " + name);
    }

    private IEnumerator PeerTest()
    {
        bool host = PeerRole == "host";
        string character = "storage_peer_" + PeerRunId + "_" + PeerRole;
        var profile = new PlayerProfile(character, FileHelpers.FileSource.Local);
        profile.SetName("Storage " + PeerRole); Check(profile.Save(), "Peer character save failed");
        Game.SetProfile(character, FileHelpers.FileSource.Local);
        World world = null;
        if (host)
        {
            world = new World("StoragePeer" + PeerRunId, "PikalajitteluValidation");
            world.m_fileSource = FileHelpers.FileSource.Local; world.SaveWorldFWLData(DateTime.Now);
            ZNet.SetServer(true, false, false, "Storage isolated peer test", "", world);
            ZNet.ResetServerHost();
            AccessTools.Field(typeof(ZNet), "m_onlineBackend").SetValue(null, OnlineBackendType.CustomSocket);
        }
        else
        {
            yield return WaitSignal("listen", 300);
            ZNet.SetServer(false, false, false, "", "", null);
            ZNet.SetServerHost("127.0.0.1", Int32.Parse(File.ReadAllText(SignalPath("listen"))), OnlineBackendType.CustomSocket);
        }
        File.WriteAllText(ResultPath, "PEER_LOADING\nrole=" + PeerRole);
        Call(UnityEngine.Object.FindFirstObjectByType<FejdStartup>(), "LoadMainScene");
        float deadline = Time.realtimeSinceStartup + 240;
        while ((!ZNet.instance || !ZNetScene.instance || !Player.m_localPlayer) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(ZNet.instance && ZNetScene.instance && Player.m_localPlayer, "Peer world/player did not load");
        Player player = Player.m_localPlayer;
        player.SetGodMode(true);
        Valkyrie intro = UnityEngine.Object.FindFirstObjectByType<Valkyrie>();
        if (intro && !(bool)Get(intro, "m_droppedPlayer")) Call(intro, "DropPlayer", true);
        yield return WaitForStablePlayer(player);
        var body = player.GetComponent<Rigidbody>();
        body.useGravity = false; body.linearVelocity = Vector3.zero; body.constraints = RigidbodyConstraints.FreezeAll;
        for (int frame = 0; frame < 20; frame++) yield return null;
        if (host) yield return PeerHost(player);
        else yield return PeerClient(player);
    }

    private IEnumerator PeerHost(Player player)
    {
        Container chest = SpawnChest(player, new Vector3(2, 0, 0));
        chest.GetInventory().GetAllItems().Clear(); NotifyInventory(chest.GetInventory());
        string stable = (string)AccessTools.Method(typeof(Pikalajittelu), "ChestId").Invoke(null, new object[] { chest, true });
        Vector3 position = player.transform.position;
        Signal("fixture", stable + "\n" + position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\n" + position.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        try
        {
            Signal("listen", ((IPEndPoint)listener.LocalEndpoint).Port.ToString());
            File.WriteAllText(ResultPath, "PEER_LISTENING\nloopback only");
            float deadline = Time.realtimeSinceStartup + 240;
            while (!listener.Pending() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(listener.Pending(), "Client did not connect to loopback listener");
            TcpClient client = listener.AcceptTcpClient();
            Check(IPAddress.IsLoopback(((IPEndPoint)client.Client.RemoteEndPoint).Address), "Non-loopback test peer rejected");
            var socket = new ZSocket2(client, "127.0.0.1");
            var peer = new ZNetPeer(socket, false);
            Call(ZNet.instance, "OnNewConnection", peer);
            File.WriteAllText(ResultPath, "PEER_CONNECTED\nwaiting for client world");
            yield return WaitSignal("client-ready", 300);
            Check(peer.IsReady(), "Native peer handshake did not finish");
            ZDO data = chest.GetComponent<ZNetView>().GetZDO();
            data.SetOwner(ZDOMan.GetSessionID()); // Initial fixture ownership only.
            chest.SetInUse(true);
            ZDOMan.instance.ForceSendZDO(peer.m_uid, data.m_uid);
            Signal("open");
            yield return WaitSignal("open-rejected");
            Check(Count(chest.GetInventory(), "Tin") == 0, "Denied remote request altered host chest");
            chest.SetInUse(false);
            chest.GetInventory().GetAllItems().Add(Item("Tin", 1, 0, 0)); NotifyInventory(chest.GetInventory());
            delayedStackChest = chest; holdStackRequest = true;
            var delayPatch = new Harmony("mikko.valheim.pikalajittelu.validation.delay");
            delayPatch.Patch(AccessTools.Method(typeof(Container), "RPC_RequestStack"),
                prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "DelayNativeStackRequest"));
            ZDOMan.instance.ForceSendZDO(peer.m_uid, data.m_uid); Signal("delay-ready");
            yield return WaitSignal("request-held");
            yield return WaitSignal("request-timed-out");
            Check(delayedStackSender == peer.m_uid && Count(chest.GetInventory(), "Tin") == 1,
                "Held native request changed the host chest or came from another peer");
            holdStackRequest = false;
            Call(chest, "RPC_RequestStack", delayedStackSender, delayedStackPlayer);
            delayPatch.UnpatchSelf(); Signal("late-grant-sent");
            yield return WaitSignal("late-grant-safe");
            chest.GetComponent<ZNetView>().InvokeRPC(peer.m_uid, "RPC_StackResponse", new object[] { true });
            chest.GetComponent<ZNetView>().InvokeRPC(peer.m_uid, "RPC_StackResponse", new object[] { true });
            Signal("duplicate-grants-sent");
            yield return WaitSignal("duplicate-grants-safe");
            Check(Count(chest.GetInventory(), "Tin") == 1, "Late/duplicate grants moved client items on the host");
            // Restore the empty, host-owned fixture only after both peers observed no mutation.
            data.SetOwner(ZDOMan.GetSessionID());
            chest.GetInventory().GetAllItems().Clear(); NotifyInventory(chest.GetInventory());
            ZDOMan.instance.ForceSendZDO(peer.m_uid, data.m_uid);
            Signal("available");
            yield return WaitSignal("deposited");
            deadline = Time.realtimeSinceStartup + 30;
            while (Count(chest.GetInventory(), "Tin") != 28 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Count(chest.GetInventory(), "Tin") == 28, "Host did not receive 28 deposited tin through ZDO replication");
            Check(data.GetOwner() == peer.m_uid, "Native reservation did not transfer ownership to client");
            VerifyNetwork(chest);
            Signal("host-observed");
            yield return WaitSignal("undone");
            deadline = Time.realtimeSinceStartup + 30;
            while (Count(chest.GetInventory(), "Tin") != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Count(chest.GetInventory(), "Tin") == 0, "Host did not observe remote undo");
            VerifyNetwork(chest);
            Signal("undo-observed");
            yield return WaitSignal("native-client-ready");
            Check(data.GetOwner() == peer.m_uid, "Host-side sorting must start with a client-owned chest");
            Inventory pack = player.GetInventory(); pack.GetAllItems().Clear();
            pack.GetAllItems().Add(Item("Wood", 17, 0, 1)); NotifyInventory(pack);
            byte[] beforePack = Save(pack);
            var plugin = (Pikalajittelu)Chainloader.PluginInfos[Pikalajittelu.Id].Instance;
            yield return Sort(plugin, player, false, false, false);
            Check((bool)Get(plugin, "previewVisible") && chest.GetComponent<ZNetView>().IsOwner(), "Host could not reserve the native remote owner's chest");
            Check(Save(pack).SequenceEqual(beforePack) && Count(chest.GetInventory(), "Wood") == 0, "Host preview moved items");
            Call(plugin, "SetPreviewClosed", false);
            yield return Sort(plugin, player, false, true, false);
            Check(Count(pack, "Wood") == 0 && Count(chest.GetInventory(), "Wood") == 17 && !(bool)Get(plugin, "storageFault"), "Host-side deposit failed");
            VerifyNetwork(chest); Signal("host-deposited");
            yield return WaitSignal("native-client-observed");
            yield return Sort(plugin, player, false, false, true);
            Check((bool)Get(plugin, "previewVisible"), "Host undo preview unavailable");
            Call(plugin, "SetPreviewClosed", false);
            yield return Sort(plugin, player, false, true, true);
            Check(Save(pack).SequenceEqual(beforePack) && Count(chest.GetInventory(), "Wood") == 0, "Host undo did not restore exact inventory");
            VerifyNetwork(chest); Signal("host-undone");
            yield return WaitSignal("client-pass");
            byte[] disconnectChest = Save(chest.GetInventory());
            if (DisconnectClient)
            {
                data.SetOwner(peer.m_uid); ZDOMan.instance.ForceSendZDO(peer.m_uid, data.m_uid);
                Signal("disconnect-fixture");
                yield return WaitSignal("disconnect-ready");
                yield return Sort(plugin, player, false, false, false);
                Check(!(bool)Get(plugin, "busy") && !(bool)Get(plugin, "previewVisible"), "Disconnect left an active host operation");
                Check(Save(pack).SequenceEqual(beforePack) && Save(chest.GetInventory()).SequenceEqual(disconnectChest), "Client disconnect changed host items");
                deadline = Time.realtimeSinceStartup + 20;
                while (ZNet.instance.GetPeer(peer.m_uid) != null && Time.realtimeSinceStartup < deadline) yield return null;
                Check(ZNet.instance.GetPeer(peer.m_uid) == null, "Host did not observe the real socket disconnect");
                Check(!chest.IsInUse(), "Disconnect left a chest reservation held");
                VerifyNetwork(chest); Signal("disconnect-verified");
                yield return WaitSignal("disconnect-client-verified");
                deadline = Time.realtimeSinceStartup + 30;
                while (!chest.GetComponent<ZNetView>().IsOwner() && Time.realtimeSinceStartup < deadline) yield return null;
                Check(chest.GetComponent<ZNetView>().IsOwner(), "Native host did not reclaim the departed client's chest");
                yield return Sort(plugin, player, false, false, false);
                Check((bool)Get(plugin, "previewVisible"), "A disconnected owner's expired request permanently blocked the chest");
                Call(plugin, "SetPreviewClosed", true);
                Check(Save(pack).SequenceEqual(beforePack) && Save(chest.GetInventory()).SequenceEqual(disconnectChest), "Post-disconnect retry preview moved items");
            }
            else
            {
                delayedStackChest = chest; holdStackRequest = true;
                var dropPatch = new Harmony("mikko.valheim.pikalajittelu.validation.drop");
                dropPatch.Patch(AccessTools.Method(typeof(Container), "RPC_RequestStack"), prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "DelayNativeStackRequest"));
                Signal("disconnect-ready");
                yield return WaitSignal("disconnect-request-held");
                socket.Close(); Signal("socket-closed");
                yield return WaitSignal("disconnect-client-verified");
                Check(Save(pack).SequenceEqual(beforePack) && Save(chest.GetInventory()).SequenceEqual(disconnectChest), "Host disconnect changed server items");
                VerifyNetwork(chest); holdStackRequest = false; dropPatch.UnpatchSelf(); Signal("disconnect-verified");
            }
            File.WriteAllText(ResultPath, "PEER_HOST_PASS\nchecks=" + checks + "\nDisconnect=" + (DisconnectClient ? "client" : "host") + " during native reservation; exact inventory conservation. Both sorting directions over loopback CustomSocket. Steam transport not tested.");
        }
        finally { listener.Stop(); }
    }

    private IEnumerator PeerClient(Player player)
    {
        yield return WaitSignal("fixture");
        string[] fixture = File.ReadAllLines(SignalPath("fixture"));
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        player.transform.position = new Vector3(Single.Parse(fixture[1], culture), Single.Parse(fixture[2], culture), Single.Parse(fixture[3], culture));
        player.GetComponent<Rigidbody>().position = player.transform.position;
        ZNet.instance.SetReferencePosition(player.transform.position);
        float deadline = Time.realtimeSinceStartup + 120;
        Container chest = null;
        while (!chest && Time.realtimeSinceStartup < deadline)
        {
            chest = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).FirstOrDefault(c =>
                c.GetComponent<ZNetView>() && c.GetComponent<ZNetView>().IsValid() && StableId(c) == fixture[0]);
            yield return null;
        }
        Check(chest, "Client did not instantiate the host chest from network data");
        Inventory pack = player.GetInventory(); pack.GetAllItems().Clear();
        pack.GetAllItems().Add(Item("Tin", 28, 0, 1)); NotifyInventory(pack);
        byte[] before = Save(pack);
        Signal("client-ready");
        yield return WaitSignal("open");
        ZNetView view = chest.GetComponent<ZNetView>();
        deadline = Time.realtimeSinceStartup + 30;
        while (view.GetZDO().GetInt(ZDOVars.s_inUse, 0) != 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse, 0) == 1, "Open host-owned chest state was not replicated");
        var plugin = (Pikalajittelu)Chainloader.PluginInfos[Pikalajittelu.Id].Instance;
        yield return Sort(plugin, player, false, false, false);
        Check(!(bool)Get(plugin, "previewVisible") && Save(pack).SequenceEqual(before) && Count(chest.GetInventory(), "Tin") == 0,
            "Sorting an open remote chest was not rejected without mutation");
        Signal("open-rejected");
        yield return WaitSignal("delay-ready");
        deadline = Time.realtimeSinceStartup + 30;
        while ((view.GetZDO().GetInt(ZDOVars.s_inUse, 0) != 0 || Count(chest.GetInventory(), "Tin") != 1) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!view.IsOwner() && Count(chest.GetInventory(), "Tin") == 1, "Delayed reply fixture did not replicate");
        byte[] delayChestBefore = Save(chest.GetInventory());
        float requestStart = Time.realtimeSinceStartup;
        yield return Sort(plugin, player, false, false, false);
        Check(Time.realtimeSinceStartup - requestStart >= 2.9f, "Native request did not exercise the real reservation timeout");
        Check(!(bool)Get(plugin, "previewVisible") && !(bool)Get(plugin, "busy") && Save(pack).SequenceEqual(before), "Timed-out request mutated inventory or left sorting busy");
        Signal("request-timed-out");
        yield return WaitSignal("late-grant-sent");
        deadline = Time.realtimeSinceStartup + 30;
        var pendingRequests = (System.Collections.IDictionary)Get(plugin, "requests");
        while ((!view.IsOwner() || pendingRequests.Contains(chest)) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(view.IsOwner() && !pendingRequests.Contains(chest), "Expired request did not consume the late native grant");
        Check(Save(pack).SequenceEqual(before) && Save(chest.GetInventory()).SequenceEqual(delayChestBefore), "Late grant invoked vanilla StackAll or altered inventory");
        Signal("late-grant-safe");
        yield return WaitSignal("duplicate-grants-sent");
        // Allow native network frames to dispatch both duplicates before examining inventories.
        float duplicateDeadline = Time.realtimeSinceStartup + 2;
        while (Time.realtimeSinceStartup < duplicateDeadline) yield return null;
        Check(Save(pack).SequenceEqual(before) && Save(chest.GetInventory()).SequenceEqual(delayChestBefore), "Duplicate late grant escaped suppression");
        VerifyNetwork(chest); Signal("duplicate-grants-safe");
        yield return WaitSignal("available");
        deadline = Time.realtimeSinceStartup + 30;
        while ((view.IsOwner() || view.GetZDO().GetInt(ZDOVars.s_inUse, 0) != 0 || Count(chest.GetInventory(), "Tin") != 0) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!view.IsOwner(), "Available chest must start owned by the other process");
        yield return Sort(plugin, player, false, false, false);
        Check((bool)Get(plugin, "previewVisible") && view.IsOwner(), "Native remote reservation did not create a preview and transfer ownership");
        Check(Save(pack).SequenceEqual(before) && Count(chest.GetInventory(), "Tin") == 0, "Remote preview moved items");
        Call(plugin, "SetPreviewClosed", false);
        yield return Sort(plugin, player, false, true, false);
        Check(Count(pack, "Tin") == 0 && Count(chest.GetInventory(), "Tin") == 28 && !(bool)Get(plugin, "storageFault"), "Remote deposit failed");
        VerifyNetwork(chest); Signal("deposited");
        yield return WaitSignal("host-observed");
        yield return Sort(plugin, player, false, false, true);
        Check((bool)Get(plugin, "previewVisible"), "Remote undo preview unavailable");
        Call(plugin, "SetPreviewClosed", false);
        yield return Sort(plugin, player, false, true, true);
        Check(Save(pack).SequenceEqual(before) && Count(chest.GetInventory(), "Tin") == 0, "Remote undo did not restore exact inventory");
        VerifyNetwork(chest); Signal("undone");
        yield return WaitSignal("undo-observed");
        // Disable all sorting behavior on this peer before testing the user's
        // host-only installation scenario. Container's owner handlers stay native.
        Call(plugin, "SetPreviewClosed", true);
        plugin.enabled = false;
        ((Harmony)Get(plugin, "harmony")).UnpatchSelf();
        Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(Pikalajittelu.Id)), "Client still has sorter patches installed");
        Signal("native-client-ready");
        yield return WaitSignal("host-deposited");
        deadline = Time.realtimeSinceStartup + 30;
        while (Count(chest.GetInventory(), "Wood") != 17 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Count(chest.GetInventory(), "Wood") == 17 && !view.IsOwner(), "Native client did not observe host deposit and ownership transfer");
        Check(Save(pack).SequenceEqual(before), "Host sorting altered the remote player's inventory");
        VerifyNetwork(chest); Signal("native-client-observed");
        yield return WaitSignal("host-undone");
        deadline = Time.realtimeSinceStartup + 30;
        while (Count(chest.GetInventory(), "Wood") != 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Count(chest.GetInventory(), "Wood") == 0 && Save(pack).SequenceEqual(before), "Native client did not observe exact host undo");
        VerifyNetwork(chest); Signal("client-pass");
        byte[] disconnectChest = Save(chest.GetInventory());
        if (DisconnectClient)
        {
            yield return WaitSignal("disconnect-fixture");
            deadline = Time.realtimeSinceStartup + 20;
            while (!view.IsOwner() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(view.IsOwner(), "Disconnect fixture ownership did not arrive");
            delayedStackChest = chest; holdStackRequest = true;
            var dropPatch = new Harmony("mikko.valheim.pikalajittelu.validation.drop");
            dropPatch.Patch(AccessTools.Method(typeof(Container), "RPC_RequestStack"), prefix: new HarmonyMethod(typeof(ValheimInventoryHarness), "DelayNativeStackRequest"));
            Signal("disconnect-ready");
            yield return WaitSignal("disconnect-request-held");
            ZNet.instance.GetServerPeer().m_socket.Close(); Signal("socket-closed");
            Check(Save(pack).SequenceEqual(before) && Save(chest.GetInventory()).SequenceEqual(disconnectChest), "Disconnect changed client inventories");
            holdStackRequest = false; dropPatch.UnpatchSelf(); Signal("disconnect-client-verified");
            yield return WaitSignal("disconnect-verified");
        }
        else
        {
            ((Harmony)Get(plugin, "harmony")).PatchAll(typeof(Pikalajittelu).Assembly); plugin.enabled = true;
            yield return WaitSignal("disconnect-ready");
            yield return Sort(plugin, player, false, false, false);
            Check(!(bool)Get(plugin, "busy") && !(bool)Get(plugin, "previewVisible"), "Host disconnect left an active client operation");
            Check(Save(pack).SequenceEqual(before) && Save(chest.GetInventory()).SequenceEqual(disconnectChest), "Host disconnect changed client inventories");
            Check(File.Exists(SignalPath("socket-closed")), "Host socket was not actually closed");
            Signal("disconnect-client-verified"); yield return WaitSignal("disconnect-verified");
        }
        File.WriteAllText(ResultPath, "PEER_CLIENT_PASS\nchecks=" + checks + "\nDisconnect=" + (DisconnectClient ? "client" : "host") + " during native reservation; exact inventory conservation. Native peer tests over loopback; Steam transport not tested.");
    }
}
