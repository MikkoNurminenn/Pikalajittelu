using System;
using System.Collections.Generic;
using System.Linq;
using MikkoMods.Storage;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private StorageSession lastCommitted;
        private bool undoRequested;

        private StorageSession CaptureUndo(Player player)
        {
            StorageSession previous = lastCommitted;
            if (storageFault || previous == null || !ZNet.instance || previous.Player != player ||
                previous.World != ZNet.instance.GetWorldUID() || previous.Character != player.GetPlayerID())
                throw new InvalidOperationException(T("Tässä pelikerrassa ei ole peruttavaa siirtoa.", "There is no transfer to undo in this session."));
            var session = new StorageSession { Player = player, World = previous.World, Character = previous.Character,
                Consolidate = previous.Consolidate, UndoOf = previous, Plan = StoragePlanner.Reverse(previous.Plan) };
            var actual = new Dictionary<string, byte[]>();
            foreach (StorageState old in previous.States)
            {
                bool isPlayer = old.Model.Player;
                Container chest = isPlayer ? null : reserved.FirstOrDefault(c => c && ChestId(c) == old.Id && OwnedReservation(c, player));
                if (!isPlayer && !chest) throw new InvalidOperationException(T("Peruminen tarvitsee kaikki alkuperäiset arkut. Siirry niiden lähelle ja sulje arkkujen ikkunat.",
                    "Undo needs all original chests. Move within range and close their inventory windows."));
                Inventory live = isPlayer ? player.GetInventory() : chest.GetInventory();
                byte[] bytes = InventoryBytes(live);
                actual.Add(old.Id, bytes);
                byte[] network = isPlayer ? null : ((ZNetView)View.GetValue(chest)).GetZDO().GetByteArray(ZDOVars.s_items, null);
                var state = new StorageState { Id = old.Id, Chest = chest, Live = live, Before = bytes, After = (byte[])old.Before.Clone(),
                    NetworkBefore = network == null ? null : (byte[])network.Clone(), Original = live.GetAllItems().ToArray(),
                    Model = session.Plan.Before.Single(b => b.Id == old.Id), Planned = old.OriginalCopies.Select(i => i.Clone()).ToList() };
                state.OriginalCopies = state.Original.Select(i => i.Clone()).ToArray();
                session.States.Add(state);
                foreach (var item in state.Planned)
                {
                    string template = old.Id + ":" + (item.m_gridPos.y * live.GetWidth() + item.m_gridPos.x);
                    session.Templates[template] = item;
                }
                if (live.GetWidth() != old.Width || live.GetHeight() != old.Height)
                    throw new InvalidOperationException(T("Inventaarion koko muuttui. Peruminen estetty.", "Inventory dimensions changed. Undo is unavailable."));
                state.Width = live.GetWidth(); state.Height = live.GetHeight();
            }
            if (!UndoGuard.Matches(previous.World, previous.Character, session.World, session.Character,
                previous.States.ToDictionary(s => s.Id, s => s.After), actual))
                throw new InvalidOperationException(T("Tavaroita on muutettu edellisen siirron jälkeen. Peruminen estetty; mitään ei muutettu.",
                    "Items changed after the last transfer. Undo is unavailable; nothing was changed."));
            return session;
        }

        private static void ApplyPlayerLayout(StorageState state)
        {
            // Preserve equipped/retained objects by slot and full metadata. Items
            // returning from chests receive new objects; no old chest references leak.
            var result = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData desired in state.Planned)
            {
                ItemDrop.ItemData existing = state.Original.FirstOrDefault(item =>
                    item.m_gridPos.x == desired.m_gridPos.x && item.m_gridPos.y == desired.m_gridPos.y &&
                    ItemFingerprint(item) == ItemFingerprint(desired));
                ItemDrop.ItemData final = existing ?? desired.Clone();
                final.m_stack = desired.m_stack;
                result.Add(final);
            }
            state.Live.GetAllItems().Clear();
            state.Live.GetAllItems().AddRange(result);
        }
    }
}
