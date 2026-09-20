using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using MikkoMods.Storage;

namespace MikkoMods
{
    [BepInPlugin(Id, "Pikalajittelu", "2.0.1")]
    public sealed partial class Pikalajittelu : BaseUnityPlugin
    {
        public const string Id = "mikko.valheim.pikalajittelu";
        private static Pikalajittelu instance;
        private Harmony harmony;
        private ConfigEntry<KeyboardShortcut> shortcut;
        private ConfigEntry<KeyboardShortcut> chestShortcut;
        private ConfigEntry<float> radius;
        private ConfigEntry<bool> protectHotbar;
        private ConfigEntry<string> excluded;
        private ConfigEntry<string> keepInInventory;
        private bool busy;
        private Action<Player> pendingReservationAction;
        private Container pendingReservationTarget;
        private float nextUse;
        private readonly Dictionary<Container, ReservationReply> requests = new Dictionary<Container, ReservationReply>();
        private readonly HashSet<Container> suppressedStackReplies = new HashSet<Container>();
        private Container issuingStackRequest;
        private readonly HashSet<Container> reserved = new HashSet<Container>();
        private static readonly MethodInfo CanInput = AccessTools.Method(typeof(Player), "TakeInput");
        private static readonly MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo Load = AccessTools.Method(typeof(Container), "Load");
        private static readonly FieldInfo View = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly FieldInfo LocalInUse = AccessTools.Field(typeof(Container), "m_inUse");

        private void Awake()
        {
            instance = this;
            shortcut = Config.Bind("Controls", "Shortcut", new KeyboardShortcut(KeyCode.P), "Pikalajittelun nappain.");
            chestShortcut = Config.Bind("Controls", "ChestShortcut", new KeyboardShortcut(KeyCode.P, KeyCode.LeftShift), "Yhdista samat tavarat lahiarkkujen valilla.");
            radius = Config.Bind("Sorting", "Radius", 10f, new ConfigDescription("Sade metreina.", new AcceptableValueRange<float>(1f, 20f)));
            protectHotbar = Config.Bind("Protection", "KeepHotbar", true, "Jata pikapalkin tavarat reppuun.");
            excluded = Config.Bind("Protection", "ExcludedPrefabs", "", "Pilkuilla erotetut tavaroiden prefab-nimet, joita ei siirreta.");
            keepInInventory = Config.Bind("Protection", "KeepInInventory", "Coins,CryptKey,Wishbone,DvergrKey", "Tavarat jotka pidetaan repussa, mutta joita saa jarjestella arkkujen valilla.");
            InitializeStorageSettings();
            if (CanInput == null || CheckAccess == null || Load == null || View == null || LocalInUse == null || InventoryChanged == null || SaveChest == null)
            {
                Logger.LogError("Pelin rajapinta on muuttunut. Pikalajittelu ei kaynnisty.");
                enabled = false;
                return;
            }
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Pikalajittelu).Assembly);
            if (Config.Bind("Recovery", "EnableGraveRecovery", false, "Erillinen isannan hautapelastus. Ohittaa haudalle matkustamisen.").Value) RegisterRecoveryCommand();
            Logger.LogInfo("Pikalajittelu 2.0.1 release candidate. P = repun esikatselu, Shift+P = arkkujen esikatselu. Varmennuksen rajat: LUEMINUT.md.");
        }

        private static bool Ready(Player player)
        {
            return player && player == Player.m_localPlayer && !player.IsDead() &&
                ZNet.instance && (ZNet.instance.IsServer() || ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected) &&
                !player.IsTeleporting() && !player.InCutscene() && !player.InPlaceMode() &&
                !InventoryGui.IsVisible() && ((instance != null && instance.recovering) || (bool)CanInput.Invoke(player, null));
        }

        private void Update()
        {
            if (ModalVisible)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (captureBinding >= 0) captureBinding = -1;
                    else SetPreviewClosed(true);
                }
                UpdateStorageManager();
                return;
            }
            // Keep queued confirmation/undo until the closing click/Escape has
            // passed. Clearing the request during this guard would lose it.
            if (BlockGameplayInput) return;
            if (applyRequested && !busy)
            {
                applyRequested = false;
                if (pendingStorage != null && Ready(Player.m_localPlayer))
                    StartCoroutine(Organize(Player.m_localPlayer, pendingStorage.Consolidate, null, null, true, pendingStorage.UndoOf != null));
                return;
            }
            if (undoRequested && !busy)
            {
                undoRequested = false;
                if (lastCommitted != null && Ready(Player.m_localPlayer))
                    StartCoroutine(Organize(Player.m_localPlayer, lastCommitted.Consolidate, null, null, false, true));
                return;
            }
            if (busy || Time.unscaledTime < nextUse) return;
            if (managerShortcut.Value.IsDown() && Ready(Player.m_localPlayer))
            {
                OpenStorageManager();
                return;
            }
            bool consolidate = chestShortcut.Value.IsDown();
            if (!consolidate && !shortcut.Value.IsDown()) return;
            Player player = Player.m_localPlayer;
            if (!Ready(player)) return;
            nextUse = Time.unscaledTime + 1f;
            // A timed-out request stays registered until its late reply arrives, so a late
            // reply can never fall through to vanilla StackAll and move protected items.
            foreach (Container dead in requests.Keys.Where(c => !c).ToArray()) requests.Remove(dead);
            suppressedStackReplies.RemoveWhere(c => !c);
            StartCoroutine(Organize(player, consolidate));
        }

        private bool Eligible(Container chest, Player player, bool allowReserved = false)
        {
            if (!chest || !player || (chest.IsInUse() && !(allowReserved && reserved.Contains(chest))) || chest.m_wagon ||
                chest.GetComponentInParent<Ship>() || chest.GetComponent<TombStone>()) return false;
            Piece piece = chest.GetComponent<Piece>();
            if (!piece || piece.GetCreator() == 0) return false;
            ZNetView view = View.GetValue(chest) as ZNetView;
            if (!view || !view.IsValid() || chest.GetInventory() == null) return false;
            if ((chest.transform.position - player.transform.position).sqrMagnitude > radius.Value * radius.Value) return false;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, false, false)) return false;
            return (bool)CheckAccess.Invoke(chest, new object[] { player.GetPlayerID() });
        }

        private bool OwnedReservation(Container chest, Player player)
        {
            if (!chest || !reserved.Contains(chest) || !chest.IsInUse()) return false;
            ZNetView view = View.GetValue(chest) as ZNetView;
            return view && view.IsValid() && view.IsOwner() && Eligible(chest, player, true);
        }

        private void ReleaseReservations()
        {
            foreach (Container chest in reserved.ToArray())
            {
                try
                {
                    if (!chest) continue;
                    ZNetView view = View.GetValue(chest) as ZNetView;
                    if (view && view.IsValid() && view.IsOwner()) chest.SetInUse(false);
                    else
                    {
                        // SetInUse is a no-op after ownership changes. Clear only
                        // our local lease flag; the new owner's ZDO state is theirs.
                        LocalInUse.SetValue(chest, false);
                    }
                }
                catch (Exception error) { Logger.LogError(error); }
            }
            reserved.Clear();
        }

        private bool MovableStoredItem(ItemDrop.ItemData item, HashSet<string> ignored)
        {
            return item != null && item.m_shared != null && item.m_stack > 0 &&
                !item.m_equipped && !item.m_shared.m_questItem && item.m_dropPrefab &&
                !ignored.Contains(item.m_dropPrefab.name);
        }

        private IEnumerator Organize(Player player, bool consolidate, string graveOwner = null, Terminal terminal = null, bool applyPreview = false, bool undo = false)
        {
            // Rule saves may start directly from OnGUI instead of Update. They
            // must wait for the same closing-event guard as queued transfers.
            while (!ModalVisible && BlockGameplayInput) yield return null;
            busy = true;
            Action<Player> reservedAction = pendingReservationAction;
            Container actionTarget = pendingReservationTarget;
            pendingReservationAction = null; pendingReservationTarget = null;
            recovering = graveOwner != null;
            int skipped = 0;
            try
            {
                List<ZDO> graves = null;
                if (recovering)
                {
                    if (!ZNet.instance || !ZNet.instance.IsServer()) yield break;
                    var prefab = AccessTools.Field(typeof(Player), "m_tombstone").GetValue(player) as GameObject;
                    if (!prefab) throw new InvalidOperationException("Hautakiven prefab puuttuu.");
                    var allGraves = new List<ZDO>();
                    int scanIndex = 0;
                    while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab.name, allGraves, ref scanIndex))
                    {
                        if (!Ready(player) || !ZNet.instance || !ZNet.instance.IsServer()) yield break;
                        yield return null;
                    }
                    graves = allGraves.Where(z => z.IsValid() && RecoveryRules.OwnerMatches(z.GetString(ZDOVars.s_ownerName, ""), graveOwner))
                        .GroupBy(z => z.m_uid).Select(g => g.First()).ToList();
                    if (graves.Count == 0) { RecoveryMessage(terminal, "Hahmolle ei loytynyt hautoja: " + graveOwner); yield break; }
                    if (graves.Select(z => z.GetLong(ZDOVars.s_owner, 0L)).Distinct().Count() != 1)
                    {
                        RecoveryMessage(terminal, "Samannimisilla hahmoilla on eri omistajat. Ei siirtoja.");
                        yield break;
                    }
                    RecoveryMessage(terminal, "Loytyi " + graves.Count + " hautaa hahmolle " + graveOwner + ". Varataan lahiarkut...");
                }
                Container[] nearby = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                    .Where(c => Eligible(c, player) && (reservedAction == null || c == actionTarget))
                    .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude).ToArray();
                player.Message(MessageHud.MessageType.Center, reservedAction != null ? T("Tallennetaan arkun sääntöjä…", "Saving chest rules…") :
                    undo ? T("Valmistellaan perumista…", "Preparing undo…") : T("Luetaan lähivarastoa…", "Reading nearby storage…"));
                float totalDeadline = Time.realtimeSinceStartup + 12f;
                // A departed peer cannot answer its expired request. On the host,
                // retire that wait once native networking confirms disconnection.
                // Keep reply suppression so an already queued stale packet cannot
                // fall through to vanilla StackAll. Connected peers stay quarantined.
                if (ZNet.instance && ZNet.instance.IsServer())
                    foreach (Container stale in requests.Where(pair => pair.Value.Expired &&
                        ZNet.instance.GetPeer(pair.Value.Owner) == null).Select(pair => pair.Key).ToArray())
                        requests.Remove(stale);
                foreach (Container chest in nearby)
                {
                    if (!Ready(player) || Time.realtimeSinceStartup >= totalDeadline) break;
                    if (!Eligible(chest, player) || requests.ContainsKey(chest)) { skipped++; continue; }
                    ZNetView view = View.GetValue(chest) as ZNetView;
                    if (!view || !view.IsValid()) { skipped++; continue; }
                    // Locally owned chests need no ownership request. Reserve and
                    // load on this frame, before yielding to another message.
                    if (view.IsOwner())
                    {
                        Load.Invoke(chest, null);
                        reserved.Add(chest);
                        chest.SetInUse(true);
                        continue;
                    }
                    ReservationReply request = new ReservationReply(view.GetZDO().GetOwner());
                    requests.Add(chest, request);
                    suppressedStackReplies.Add(chest);
                    issuingStackRequest = chest;
                    try { chest.StackAll(); }
                    finally { issuingStackRequest = null; }
                    float deadline = Mathf.Min(Time.realtimeSinceStartup + 3f, totalDeadline);
                    while (!request.Granted.HasValue && Time.realtimeSinceStartup < deadline && chest && Ready(player))
                        yield return null;
                    if (!request.Granted.HasValue) { request.Expired = true; skipped++; continue; }
                    if (!request.Granted.Value) { requests.Remove(chest); skipped++; continue; }
                    view = chest ? View.GetValue(chest) as ZNetView : null;
                    deadline = Mathf.Min(Time.realtimeSinceStartup + 2f, totalDeadline);
                    while (view && view.IsValid() && !view.IsOwner() && Time.realtimeSinceStartup < deadline && Ready(player))
                        yield return null;
                    try
                    {
                        if (Ready(player) && Eligible(chest, player) && view && view.IsValid() && view.IsOwner())
                        {
                            Load.Invoke(chest, null);
                            reserved.Add(chest);
                            chest.SetInUse(true);
                        }
                        else skipped++;
                    }
                    finally { requests.Remove(chest); }
                }
                if (!Ready(player)) yield break;
                int moved = 0;
                int changed = 0;
                bool failed = false;
                string failureReason = "";
                try
                {
                    if (reservedAction != null) reservedAction(player);
                    else moved = recovering ? RecoverToReserved(player, graves, terminal, out changed) :
                        PrepareOrApplyStorage(player, consolidate, applyPreview, undo, out changed);
                }
                catch (Exception error) { Logger.LogError(error); RecoveryMessage(terminal, error.Message); failed = true; failureReason = error.GetBaseException().Message; }
                if (reservedAction != null) yield break;
                if (!recovering && previewVisible)
                {
                    if (skipped > 0) previewNotice += " " + skipped + T(" arkkua ohitettiin, koska varausta ei saatu.", " chests were skipped because they could not be reserved.");
                    yield break;
                }
                string message = failed ? T("Siirto keskeytettiin: ", "Transfer stopped: ") + failureReason :
                    undo ? T("Edellinen siirto peruttu.", "Last transfer undone.") :
                    moved > 0 ? (recovering ? T("Haudoilta palautettu ", "Recovered from graves: ") : consolidate ? T("Arkkujen välillä siirretty ", "Moved between chests: ") : T("Repusta siirretty ", "Deposited: ")) + moved + T(" tavaraa, ", " items, ") + changed + T(" arkkua.", " chests.") :
                    changed > 0 ? T("Järjestelty ", "Arranged stacks and slots in ") + changed + T(" arkun pinot ja ruudut.", " chests.") :
                    T("Ei siirrettävää tai sopivaa arkkutilaa.", "No items to move or suitable chest space.");
                if (skipped > 0 || reserved.Count < nearby.Length) message += T(" Osa arkuista ohitettiin.", " Some chests were skipped.");
                player.Message(MessageHud.MessageType.Center, message);
                if (recovering) RecoveryMessage(terminal, message);
            }
            finally
            {
                ReleaseReservations();
                foreach (ReservationReply pending in requests.Values) pending.Expired = true;
                busy = false;
                recovering = false;
            }
        }

        private bool MayMove(ItemDrop.ItemData item, Player player, HashSet<string> ignored)
        {
            if (item == null || item.m_shared == null || item.m_stack <= 0 || !item.m_dropPrefab) return false;
            return !InventoryProtection.Keep(item.m_shared.m_itemType.ToString(),
                item.m_equipped || player.IsItemEquiped(item),
                protectHotbar.Value && item.m_gridPos.y == 0,
                item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0 || item.m_shared.m_foodEitr > 0,
                item.m_shared.m_questItem, ignored.Contains(item.m_dropPrefab.name));
        }

        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        private static class StackResponsePatch
        {
            private static bool Prefix(Container __instance, long __0, bool __1)
            {
                ReservationReply request;
                if (instance == null) return true;
                if (!instance.requests.TryGetValue(__instance, out request)) return !instance.suppressedStackReplies.Contains(__instance);
                if (request.Receive(__0, __1) && request.Expired) instance.requests.Remove(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Container), "StackAll")]
        private static class StackRequestPatch
        {
            private static bool Prefix(Container __instance)
            {
                if (instance == null) return true;
                if (instance.requests.ContainsKey(__instance) || instance.reserved.Contains(__instance))
                    return instance.issuingStackRequest == __instance;
                // Only an explicit new vanilla request may consume a vanilla reply.
                instance.suppressedStackReplies.Remove(__instance);
                return true;
            }
        }

        [HarmonyPatch(typeof(Container), "Interact")]
        private static class ReservedChestPatch
        {
            private static bool Prefix(Container __instance, ref bool __result)
            {
                if (instance == null) return true;
                ReservationReply request;
                bool waiting = instance.requests.TryGetValue(__instance, out request) && !request.Expired;
                if (!instance.reserved.Contains(__instance) && !waiting) return true;
                __result = false;
                return false;
            }
        }

        private void OnDestroy()
        {
            DestroyStorageTheme();
            SetPreviewClosed(true);
            StopAllCoroutines();
            ReleaseReservations();
            if (harmony != null) harmony.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}

