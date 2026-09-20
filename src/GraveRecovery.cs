using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private bool recovering;

        private sealed class RecoveryInventory
        {
            public ZDO Data;
            public Container Live;
            public Inventory Draft;
            public byte[] Before;
            public byte[] OriginalZdoItems;
            public byte[] After;
            public bool Grave;
            public long OriginalNetworkOwner;
        }

        private void RegisterRecoveryCommand()
        {
            new Terminal.ConsoleCommand("palautahaudat", "[hahmon nimi] Siirra hahmon hautatavarat lahiarkkuihin. Vain maailman isanta.",
                (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
                {
                    if (!ZNet.instance || !ZNet.instance.IsServer() || !Player.m_localPlayer)
                    { args.Context.AddString("Vain maailman paikallinen isanta voi kayttaa tata komentoa."); return; }
                    if (busy) { args.Context.AddString("Lajittelu on kesken. Odota hetki."); return; }
                    string owner = String.Join(" ", args.Args.Skip(1).ToArray()).Trim().Trim('"');
                    if (String.IsNullOrWhiteSpace(owner)) { args.Context.AddString("Kaytto: palautahaudat kylli"); return; }
                    StartCoroutine(Organize(Player.m_localPlayer, false, owner, args.Context));
                });
            Console.SetConsoleEnabledForThisSession();
        }

        private void RecoveryMessage(Terminal terminal, string message)
        {
            if (terminal) terminal.AddString(message);
            Logger.LogInfo(message);
        }

        private static byte[] InventoryBytes(Inventory inventory)
        {
            ZPackage package = new ZPackage();
            inventory.Save(package);
            return package.GetArray();
        }

        private static Inventory DraftInventory(byte[] bytes, Inventory live, bool grave)
        {
            // Inventory(true) creates lightweight records without m_shared and even
            // omits some item flags. Use normal Load to hydrate complete item data.
            ZPackage header = new ZPackage(bytes);
            int version = header.ReadInt();
            if (version < 108 || version > 109)
                throw new InvalidOperationException("Hautatavaroiden tallennusversiota ei tueta. Ei muutoksia.");
            int expectedStacks = header.ReadUShort();
            int width = grave ? 8 : live.GetWidth();
            int height = grave ? 4 : live.GetHeight();
            var expectedTotals = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < expectedStacks; i++)
            {
                var raw = new ItemDrop.ItemData();
                int hash = ItemDrop.ItemData.Load(header, raw, (global::Version.Item)version);
                if (hash == 0) throw new InvalidOperationException("Tallennetusta esineesta puuttuu prefab-tunniste (rivi " + i + "). Ei muutoksia.");
                if (raw.m_gridPos.x < 0 || raw.m_gridPos.y < 0 || raw.m_gridPos.x > 4095 || raw.m_gridPos.y > 4095)
                    throw new InvalidOperationException("Esineen ruutupaikka ei ole tuettu. Ei muutoksia.");
                if (grave) { width = Math.Max(width, raw.m_gridPos.x + 1); height = Math.Max(height, raw.m_gridPos.y + 1); }
                string key = hash + "|" + raw.m_quality + "|" + raw.m_variant;
                long previous;
                expectedTotals.TryGetValue(key, out previous);
                expectedTotals[key] = previous + raw.m_stack;
            }
            Inventory draft = new Inventory("Recovery draft", null, width, height);
            draft.Load(new ZPackage(bytes));
            if (draft.GetAllItems().Count != expectedStacks)
                throw new InvalidOperationException("Inventaariosta luettiin " + draft.GetAllItems().Count + "/" + expectedStacks + " esinepinoa. Ei muutoksia.");
            if (draft.GetAllItems().Any(i => !i.m_dropPrefab || i.m_shared == null || i.m_stack <= 0))
                throw new InvalidOperationException("Luetun esineen prefab, SharedData tai maara ei ole kelvollinen. Ei muutoksia.");
            var actualTotals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in draft.GetAllItems())
            {
                string key = item.m_dropPrefab.name.GetStableHashCode() + "|" + item.m_quality + "|" + item.m_variant;
                long previous;
                actualTotals.TryGetValue(key, out previous);
                actualTotals[key] = previous + item.m_stack;
            }
            if (!RecoveryRules.CountsEqual(expectedTotals, actualTotals))
                throw new InvalidOperationException("Esinemaarat muuttuivat ladattaessa. Ei muutoksia.");
            return draft;
        }

        private void ValidateRecoveryDrafts()
        {
            // Run the real Unity inventory load and transfer APIs on isolated data.
            // Never insert test items into the player, a chest, a grave or a ZDO.
            GameObject prefab = ObjectDB.instance.GetItemPrefab("Wood");
            if (!prefab || !prefab.GetComponent<ItemDrop>()) throw new InvalidOperationException("Palautuksen esitesti: Wood-esinetta ei loydy.");
            ItemDrop.ItemData sample = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            // Asset templates have not run ItemDrop.Awake. A clone therefore needs
            // its prefab identity explicitly before ItemData.Save can encode it.
            sample.m_dropPrefab = prefab;
            sample.m_stack = 2;
            sample.m_gridPos = new Vector2i(15, 7);
            sample.m_crafterID = 12345L;
            sample.m_crafterName = "RecoverySelfTest";
            sample.m_customData = new Dictionary<string, string> { { "recovery-test", "preserved" } };
            var seed = new Inventory("Recovery isolated test", null, 16, 8);
            seed.GetAllItems().Add(sample);
            Inventory source = DraftInventory(InventoryBytes(seed), null, true);
            ItemDrop.ItemData restored = source.GetAllItems().Single();
            if (restored.m_shared == null || restored.m_gridPos.x != 15 || restored.m_gridPos.y != 7 || restored.m_stack != 2 ||
                restored.m_crafterID != sample.m_crafterID || restored.m_customData["recovery-test"] != "preserved")
                throw new InvalidOperationException("Palautuksen esitesti epaonnistui: esinetiedot eivat sailynet. Oikeita inventaarioita ei muutettu.");
            var destination = new Inventory("Recovery isolated destination", null, 1, 1);
            destination.MoveItemToThis(source, restored);
            Inventory checkedDestination = DraftInventory(InventoryBytes(destination), destination, false);
            if (source.GetAllItems().Count != 0 || checkedDestination.GetAllItems().Count != 1 ||
                checkedDestination.GetAllItems()[0].m_stack != 2 ||
                checkedDestination.GetAllItems()[0].m_customData["recovery-test"] != "preserved")
                throw new InvalidOperationException("Palautuksen esitesti epaonnistui: koesiirto. Oikeita inventaarioita ei muutettu.");
            Logger.LogInfo("Hautapalautuksen muistissa ajettu lataus/siirto/tallennustesti OK.");
        }

        private static Dictionary<string, long> InventoryTotals(IEnumerable<RecoveryInventory> snapshots)
        {
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in snapshots.SelectMany(s => s.Draft.GetAllItems()))
            {
                string key = item.m_dropPrefab.name + "|" + item.m_quality + "|" + item.m_variant;
                long previous;
                totals.TryGetValue(key, out previous);
                totals[key] = previous + item.m_stack;
            }
            return totals;
        }

        private static bool SameBytes(byte[] first, byte[] second)
        {
            return first == null ? second == null : second != null && first.SequenceEqual(second);
        }

        private RecoveryInventory Snapshot(Container chest, bool grave)
        {
            ZNetView view = View.GetValue(chest) as ZNetView;
            byte[] before = InventoryBytes(chest.GetInventory());
            return new RecoveryInventory {
                Data = view.GetZDO(), Live = chest, Before = before, Grave = grave,
                OriginalZdoItems = view.GetZDO().GetByteArray(ZDOVars.s_items, null),
                Draft = DraftInventory(before, chest.GetInventory(), grave),
                OriginalNetworkOwner = view.GetZDO().GetOwner()
            };
        }

        private bool ValidRecoveryState(RecoveryInventory state, Player player)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer() || !Ready(player) || !state.Data.IsValid() || !state.Data.IsOwner()) return false;
            if (!state.Grave) return OwnedReservation(state.Live, player);
            if (state.Live)
            {
                ZNetView view = View.GetValue(state.Live) as ZNetView;
                return reserved.Contains(state.Live) && state.Live.IsInUse() && view && view.IsValid() && view.IsOwner();
            }
            return !ZNetScene.instance.FindInstance(state.Data) && state.Data.GetInt(ZDOVars.s_inUse, 0) == 0;
        }

        private void ApplyInventory(RecoveryInventory state, byte[] payload)
        {
            if (state.Live)
                state.Live.GetInventory().Load(new ZPackage(payload)); // Fires Container's normal save callback.
            else
                state.Data.Set(ZDOVars.s_items, payload);
        }

        private int RecoverToReserved(Player player, List<ZDO> graves, Terminal terminal, out int changedCount)
        {
            changedCount = 0;
            var snapshots = new List<RecoveryInventory>();
            var claimed = new List<KeyValuePair<ZDO, long>>();
            int skipped = 0;
            try
            {
                try { ValidateRecoveryDrafts(); }
                catch (Exception error) { throw new InvalidOperationException("Palautuksen ESITESTI epaonnistui ennen hautatavaroiden lukemista: " + error.Message, error); }
                foreach (Container chest in reserved.Where(c => OwnedReservation(c, player)).ToArray())
                    snapshots.Add(Snapshot(chest, false));
                if (snapshots.Count == 0) throw new InvalidOperationException("Ei kaytettavia arkkuja lahella. Mene arkkujen viereen.");
                foreach (ZDO grave in graves)
                {
                    if (!grave.IsValid() || (grave.HasOwner() && !grave.IsOwner()) || grave.GetInt(ZDOVars.s_inUse, 0) != 0)
                    { skipped++; continue; }
                    byte[] stored = grave.GetByteArray(ZDOVars.s_items, null);
                    if (stored == null || stored.Length == 0) continue;
                    ZNetView liveView = ZNetScene.instance.FindInstance(grave);
                    if (liveView)
                    {
                        Container live = liveView.GetComponent<Container>();
                        if (!live || !live.GetComponent<TombStone>() || !liveView.IsOwner() || live.IsInUse()) { skipped++; continue; }
                        Load.Invoke(live, null);
                        reserved.Add(live);
                        live.SetInUse(true);
                        snapshots.Add(Snapshot(live, true));
                    }
                    else
                    {
                        long oldOwner = grave.GetOwner();
                        // Only the host may claim an UNOWNED, unloaded grave. Never
                        // steal a grave being simulated on another player's client.
                        if (!grave.IsOwner()) { claimed.Add(new KeyValuePair<ZDO, long>(grave, oldOwner)); grave.SetOwner(ZDOMan.GetSessionID()); }
                        snapshots.Add(new RecoveryInventory {
                            Data = grave, Before = (byte[])stored.Clone(), OriginalZdoItems = (byte[])stored.Clone(),
                            Grave = true, Draft = DraftInventory(stored, null, true), OriginalNetworkOwner = oldOwner
                        });
                    }
                }
                if (skipped > 0) RecoveryMessage(terminal, "Ohitettu " + skipped + " varattua/toisen pelaajan hallitsemaa hautaa. Siirtykaa pois hautojen luota ja yrita uudelleen.");
                var sourceGraves = snapshots.Where(s => s.Grave).ToArray();
                var targets = snapshots.Where(s => !s.Grave).OrderBy(s => (s.Live.transform.position - player.transform.position).sqrMagnitude).ToArray();
                var beforeTotals = InventoryTotals(snapshots);
                int moved = 0;
                foreach (RecoveryInventory grave in sourceGraves)
                {
                    foreach (ItemDrop.ItemData item in grave.Draft.GetAllItems().ToArray())
                    {
                        var preferred = targets.OrderByDescending(t => t.Draft.GetAllItems()
                            .Where(i => i.m_dropPrefab.name == item.m_dropPrefab.name).Sum(i => i.m_stack))
                            .ThenBy(t => t.Draft.GetAllItems().Count == 0 ? 0 : 1).ToArray();
                        foreach (RecoveryInventory target in preferred)
                        {
                            if (!grave.Draft.GetAllItems().Contains(item)) break;
                            int before = item.m_stack;
                            target.Draft.MoveItemToThis(grave.Draft, item);
                            moved += before - (grave.Draft.GetAllItems().Contains(item) ? item.m_stack : 0);
                        }
                    }
                }
                if (!RecoveryRules.CountsEqual(beforeTotals, InventoryTotals(snapshots)))
                    throw new InvalidOperationException("Tavaramaara muuttui suunnittelussa. Palautus peruttu ilman muutoksia.");
                if (moved == 0) return 0;
                foreach (RecoveryInventory state in snapshots)
                {
                    state.After = InventoryBytes(state.Draft);
                    if (!ValidRecoveryState(state, player) || !SameBytes(state.OriginalZdoItems, state.Data.GetByteArray(ZDOVars.s_items, null)) ||
                        (state.Live && !SameBytes(state.Before, InventoryBytes(state.Live.GetInventory()))))
                        throw new InvalidOperationException("Arkun tai haudan tila muuttui. Palautus peruttu ilman muutoksia.");
                    // Verify serialized results load completely before touching live inventories.
                    DraftInventory(state.After, state.Live ? state.Live.GetInventory() : null, state.Grave);
                }
                string backup = Path.Combine(Paths.ConfigPath, "HautapalautusBackups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(backup);
                var index = new List<string>();
                for (int i = 0; i < snapshots.Count; i++)
                {
                    File.WriteAllBytes(Path.Combine(backup, i + "-before.bin"), snapshots[i].Before);
                    File.WriteAllBytes(Path.Combine(backup, i + "-after.bin"), snapshots[i].After);
                    index.Add(i + " " + (snapshots[i].Grave ? "grave" : "chest") + " " + snapshots[i].Data.m_uid);
                }
                File.WriteAllLines(Path.Combine(backup, "index.txt"), index.ToArray());
                File.WriteAllText(Path.Combine(backup, "status.txt"), "PREPARED: no automatic replay. Raw inventory snapshots for manual recovery.");
                try
                {
                    // No yields: network messages cannot interleave with this commit.
                    // Remove from the graves before depositing the planned contents.
                    foreach (RecoveryInventory state in snapshots.OrderByDescending(s => s.Grave)) ApplyInventory(state, state.After);
                }
                catch (Exception error)
                {
                    bool rollbackFailed = false;
                    foreach (RecoveryInventory state in snapshots)
                    {
                        try { ApplyInventory(state, state.Before); }
                        catch (Exception rollbackError) { rollbackFailed = true; Logger.LogError(rollbackError); }
                    }
                    File.WriteAllText(Path.Combine(backup, "status.txt"), rollbackFailed ? "ROLLBACK_INCOMPLETE" : "ROLLED_BACK");
                    throw new InvalidOperationException("Palautus keskeytyi. Varmuuskopio: " + backup, error);
                }
                // A journal write failure after a successful commit must not trigger
                // rollback or automatic replay of already-transferred inventories.
                try { File.WriteAllText(Path.Combine(backup, "status.txt"), "COMMITTED"); }
                catch (Exception error) { Logger.LogWarning(error); }
                changedCount = snapshots.Count(s => !s.Grave && !SameBytes(s.Before, s.After));
                RecoveryMessage(terminal, "Varmuuskopio: " + backup);
                int remaining = sourceGraves.Sum(s => s.Draft.GetAllItems().Sum(i => i.m_stack));
                if (remaining > 0) RecoveryMessage(terminal, remaining + " tavaraa jai hautoihin. Vapauta arkkutilaa ja suorita komento uudelleen.");
                return moved;
            }
            finally
            {
                foreach (var entry in claimed)
                    if (entry.Key.IsValid() && entry.Key.IsOwner()) entry.Key.SetOwner(entry.Value);
            }
        }
    }
}
