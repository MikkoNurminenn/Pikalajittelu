using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using MikkoMods.Storage;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private static readonly MethodInfo InventoryChanged = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static readonly MethodInfo SaveChest = AccessTools.Method(typeof(Container), "Save");
        private StorageSession pendingStorage;
        private bool storageFault;

        private sealed class StorageState
        {
            public string Id;
            public Container Chest;
            public Inventory Live;
            public byte[] Before, After, NetworkBefore;
            public ItemDrop.ItemData[] Original;
            public ItemDrop.ItemData[] OriginalCopies;
            public List<ItemDrop.ItemData> Planned;
            public Bin Model;
            public int Width, Height;
        }

        private sealed class StorageSession
        {
            public Player Player;
            public long World, Character;
            public bool Consolidate;
            public Plan Plan;
            public StorageSession UndoOf;
            public List<StorageState> States = new List<StorageState>();
            public Dictionary<string, ItemDrop.ItemData> Templates = new Dictionary<string, ItemDrop.ItemData>();
        }

        private static string ItemFingerprint(ItemDrop.ItemData original)
        {
            ItemDrop.ItemData normalized = original.Clone();
            normalized.m_stack = 1;
            normalized.m_gridPos = new Vector2i(0, 0);
            normalized.m_customData = original.m_customData.OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            ZPackage bytes = new ZPackage();
            normalized.Save(bytes);
            // Full bytes instead of a lossy name/quality key: durability, crafter,
            // variant, world level, cheat flag and custom data must all agree.
            return original.m_dropPrefab.name + ":" + Convert.ToBase64String(bytes.GetArray());
        }

        private StorageSession CaptureStorage(Player player, bool consolidate)
        {
            if (storageFault) throw new InvalidOperationException(T("Lajittelu pysäytettiin virheen vuoksi. Tarkista historia ja modin loki.", "Sorting was stopped after an error. Check history and the mod log."));
            var session = new StorageSession { Player = player, World = ZNet.instance.GetWorldUID(), Character = player.GetPlayerID(), Consolidate = consolidate };
            StorageRules rules = ReadCurrentRules();
            var keepAmounts = StorageRules.ParseKeep(keepQuantities.Value);
            Container[] chests = reserved.Where(c => OwnedReservation(c, player))
                .OrderBy(c => ((ZNetView)View.GetValue(c)).GetZDO().m_uid.ToString(), StringComparer.Ordinal).ToArray();
            if (chests.Length == 0) throw new InvalidOperationException(T("Ei käytettäviä arkkuja lähellä. Siirry lähemmäs ja sulje arkkujen ikkunat.", "No accessible chests nearby. Move closer and close their inventory windows."));
            foreach (Container chest in chests)
            {
                var view = (ZNetView)View.GetValue(chest);
                byte[] network = view.GetZDO().GetByteArray(ZDOVars.s_items, null);
                session.States.Add(new StorageState { Id = ChestId(chest, true), Chest = chest, Live = chest.GetInventory(),
                    NetworkBefore = network == null ? null : (byte[])network.Clone() });
            }
            if (!consolidate) session.States.Add(new StorageState { Id = "player:" + session.Character, Live = player.GetInventory() });
            var neverMove = new HashSet<string>(excluded.Value.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            var keepPlayer = new HashSet<string>((excluded.Value + "," + keepInInventory.Value).Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (StorageState state in session.States)
            {
                state.Before = InventoryBytes(state.Live);
                state.Width = state.Live.GetWidth(); state.Height = state.Live.GetHeight();
                state.Original = state.Live.GetAllItems().ToArray();
                state.OriginalCopies = state.Original.Select(i => i.Clone()).ToArray();
                state.Model = new Bin { Id = state.Id, Capacity = checked(state.Live.GetWidth() * state.Live.GetHeight()), Player = !state.Chest };
                if (state.Chest) state.Model.Rule = rules.Get(state.Id).Rule.Copy();
                foreach (ItemDrop.ItemData item in state.Original)
                {
                    if (item.m_shared == null || !item.m_dropPrefab || item.m_customData == null)
                        throw new InvalidOperationException(T("Esineen tiedot puuttuvat. Lajittelua ei tehty.", "An item has missing data. Sorting was cancelled."));
                    int slot = checked(item.m_gridPos.y * state.Live.GetWidth() + item.m_gridPos.x);
                    if (item.m_gridPos.x < 0 || item.m_gridPos.x >= state.Live.GetWidth() || item.m_gridPos.y < 0)
                        throw new InvalidOperationException(T("Esine on inventaarion ulkopuolella. Lajittelua ei tehty.", "An item is outside the inventory grid. Sorting was cancelled."));
                    string template = state.Id + ":" + slot;
                    session.Templates.Add(template, item);
                    state.Model.Items.Add(new Storage.Stack {
                        Template = template, Key = ItemFingerprint(item), Prefab = item.m_dropPrefab.name,
                        Category = item.m_shared.m_itemType.ToString(), Count = item.m_stack,
                        Limit = item.m_shared.m_maxStackSize, Slot = slot,
                        Fixed = state.Chest ? !MovableStoredItem(item, neverMove) : !MayMove(item, player, keepPlayer)
                    });
                }
            }
            session.Plan = consolidate ? StoragePlanner.Consolidate(session.States.Select(s => s.Model)) :
                StoragePlanner.Deposit(session.States.Select(s => s.Model), keepAmounts);
            if (!session.Plan.Valid) throw new InvalidOperationException(T("Tavarat eivät mahdu vakaaseen, sääntöjen mukaiseen järjestykseen. Vapauta tilaa tai tarkista arkkusäännöt.", "Items do not fit a stable layout under the chest rules. Free some space or adjust the rules."));
            foreach (StorageState state in session.States)
            {
                Bin after = session.Plan.After.Single(b => b.Id == state.Id);
                state.Planned = after.Items.Select(s => {
                    var copy = session.Templates[s.Template].Clone();
                    copy.m_stack = s.Count;
                    copy.m_gridPos = new Vector2i(s.Slot % state.Live.GetWidth(), s.Slot / state.Live.GetWidth());
                    return copy;
                }).ToList();
                var scratch = new Inventory("Pikalajittelu plan", null, state.Live.GetWidth(), state.Live.GetHeight());
                scratch.GetAllItems().AddRange(state.Planned);
                state.After = InventoryBytes(scratch);
            }
            return session;
        }

        private void RequireStorageOwner(StorageSession session, StorageState state)
        {
            if (!Ready(session.Player) || !ZNet.instance || ZNet.instance.GetWorldUID() != session.World ||
                session.Player.GetPlayerID() != session.Character ||
                (state.Chest ? !OwnedReservation(state.Chest, session.Player) : session.Player.GetInventory() != state.Live))
                throw new InvalidOperationException(T("Inventaarion hallinta menetettiin. Siirto keskeytettiin.", "Inventory ownership was lost. Transfer stopped."));
        }

        private void RequireStorageState(StorageSession session, StorageState state, bool after, bool restored)
        {
            RequireStorageOwner(session, state);
            byte[] expected = after ? state.After : state.Before;
            if (!SameBytes(InventoryBytes(state.Live), expected)) throw new InvalidOperationException(T("Inventaarion sisältö muuttui.", "Inventory contents changed."));
            if (state.Chest)
            {
                byte[] networkExpected = after || restored ? expected : state.NetworkBefore;
                var view = (ZNetView)View.GetValue(state.Chest);
                if (!SameBytes(view.GetZDO().GetByteArray(ZDOVars.s_items, null), networkExpected))
                    throw new InvalidOperationException(T("Arkun verkkotila muuttui.", "The chest's network state changed."));
            }
        }

        private static void DurableWrite(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }

        private int CommitStorage(StorageSession session, out int changed)
        {
            changed = 0;
            List<StorageState> states = session.States;
            var changedStates = new HashSet<StorageState>(states.Where(s => !SameBytes(s.Before, s.After)));
            if (changedStates.Count == 0) return 0;
            string journal = Path.Combine(Paths.ConfigPath, "Pikalajittelu", "transactions",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            bool restoring = false;
            var participants = states.Select(state => new TransactionParticipant {
                Id = state.Id,
                RequireBefore = () => RequireStorageState(session, state, false, restoring),
                RequireAfter = () => RequireStorageState(session, state, changedStates.Contains(state), false),
                Apply = () => {
                    RequireStorageOwner(session, state);
                    if (!changedStates.Contains(state)) return;
                    if (state.Chest)
                    {
                        state.Live.GetAllItems().Clear();
                        state.Live.GetAllItems().AddRange(state.Planned);
                    }
                    else
                    {
                        ApplyPlayerLayout(state);
                    }
                },
                Restore = () => {
                    restoring = true;
                    RequireStorageOwner(session, state);
                    for (int i = 0; i < state.Original.Length; i++)
                    {
                        state.Original[i].m_stack = state.OriginalCopies[i].m_stack;
                        state.Original[i].m_gridPos = state.OriginalCopies[i].m_gridPos;
                    }
                    state.Live.GetAllItems().Clear();
                    state.Live.GetAllItems().AddRange(state.Original);
                },
                Flush = () => {
                    RequireStorageOwner(session, state);
                    if (!restoring && !changedStates.Contains(state)) return;
                    InventoryChanged.Invoke(state.Live, new object[] { false, false });
                    if (state.Chest) SaveChest.Invoke(state.Chest, null);
                }
            }).ToArray();
            // Revalidate even unchanged participants: another chest's state affects
            // the plan and what the player approved in the preview.
            foreach (StorageState state in session.States) RequireStorageState(session, state, false, false);
            TransactionResult result = TransactionCoordinator.Run(participants, () => {
                Directory.CreateDirectory(journal);
                var index = new List<string> { "Pikalajittelu 2.0.0-dev", "world=" + session.World, "character=" + session.Character,
                    "mode=" + (session.UndoOf != null ? "undo" : session.Consolidate ? "consolidate" : "deposit"), "utc=" + DateTime.UtcNow.ToString("O") };
                for (int i = 0; i < states.Count; i++)
                {
                    DurableWrite(Path.Combine(journal, i + "-before.bin"), states[i].Before);
                    DurableWrite(Path.Combine(journal, i + "-after.bin"), states[i].After);
                    index.Add(i + "\t" + states[i].Id + "\t" + states[i].Live.GetWidth() + "x" + states[i].Live.GetHeight());
                }
                DurableWrite(Path.Combine(journal, "index.txt"), Encoding.UTF8.GetBytes(String.Join("\n", index.ToArray())));
                WriteHistory(session, journal, changedStates.Count(s => s.Chest));
                DurableWrite(Path.Combine(journal, "PREPARED"), Encoding.UTF8.GetBytes("Never replay automatically; inspect current world and character state."));
            }, status => DurableWrite(Path.Combine(journal, status), new byte[0]));
            Logger.LogInfo("Storage transaction " + (result.Committed ? "COMMITTED" : "FAILED") + ": " + journal);
            if (result.JournalError != null) Logger.LogError(result.JournalError);
            if (!result.Committed)
            {
                storageFault = true;
                Logger.LogError(result.Error);
                throw new InvalidOperationException(result.RollbackIncomplete ?
                    T("Palautus jäi kesken. Lajittelu pysäytetty. Varmuuskopio: ", "Rollback is incomplete. Sorting stopped. Backup: ") + journal :
                    T("Siirto epäonnistui ja alkuperäiset tavarat palautettiin. Lajittelu pysäytetty tarkistusta varten.", "Transfer failed and original items were restored. Sorting stopped for inspection."));
            }
            changed = changedStates.Count(s => s.Chest);
            lastCommitted = session.UndoOf == null ? session : null;
            return session.Plan.Moved;
        }

        private int PrepareOrApplyStorage(Player player, bool consolidate, bool apply, bool undo, out int changed)
        {
            changed = 0;
            StorageSession fresh = undo ? CaptureUndo(player) : CaptureStorage(player, consolidate);
            bool matches = pendingStorage != null && pendingStorage.World == fresh.World && pendingStorage.Character == fresh.Character &&
                pendingStorage.Consolidate == fresh.Consolidate && pendingStorage.UndoOf == fresh.UndoOf && pendingStorage.States.Count == fresh.States.Count &&
                fresh.States.All(s => pendingStorage.States.Any(old => old.Id == s.Id && SameBytes(old.Before, s.Before) && SameBytes(old.After, s.After)));
            if (!apply || !matches)
            {
                pendingStorage = fresh;
                previewPage = 0; previewScroll = UnityEngine.Vector2.zero;
                previewVisible = true;
                previewNotice = apply ? T("Tavarat muuttuivat. Tarkista päivitetty esikatselu.", "Items changed. Review the updated preview.") : "";
                return 0;
            }
            pendingStorage = null;
            return CommitStorage(fresh, out changed);
        }
    }
}
