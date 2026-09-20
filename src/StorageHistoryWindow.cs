using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using MikkoMods.Storage;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private List<HistoryRecord> historyRecords = new List<HistoryRecord>();
        private int unreadableHistory;
        private string expandedHistory;
        private string HistoryRoot { get { return Path.Combine(Paths.ConfigPath, "Pikalajittelu", "transactions"); } }

        private void WriteHistory(StorageSession session, string journal, int changed)
        {
            var record = new HistoryRecord { World = session.World, Character = session.Character, Utc = DateTime.UtcNow,
                Mode = session.UndoOf != null ? "undo" : session.Consolidate ? "consolidate" : "deposit", ChangedChests = changed };
            foreach (Movement move in session.Plan.Moves)
            {
                string template = session.Plan.Before.SelectMany(b => b.Items).First(s => s.Key == move.Key).Template;
                record.Moves.Add(new HistoryMove { Count = move.Count,
                    Item = Localization.instance.Localize(session.Templates[template].m_shared.m_name),
                    From = HistoryLabel(session, move.From), To = HistoryLabel(session, move.To) });
            }
            DurableWrite(Path.Combine(journal, "history.txt"), Encoding.UTF8.GetBytes(record.Encode()));
        }
        private string HistoryLabel(StorageSession session, string id)
        {
            StorageState state = session.States.Single(s => s.Id == id);
            return state.Model.Player ? T("Reppu", "Inventory") : ChestDisplayName(state.Chest, id);
        }
        private void RefreshHistory()
        {
            try { historyRecords = HistoryRecord.ReadRecent(HistoryRoot, managerWorld, managerPlayer.GetPlayerID(), out unreadableHistory); }
            catch (Exception error) { settingsMessage = error.Message; Logger.LogError(error); }
        }
        private void DrawStorageHistory()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("Tämän hahmon viimeiset siirrot tässä maailmassa", "Recent transfers by this character in this world"));
            if (GUILayout.Button(T("Päivitä", "Refresh"), GUILayout.Width(90))) RefreshHistory();
            GUILayout.EndHorizontal();
            GUI.enabled = !storageFault && lastCommitted != null && lastCommitted.World == managerWorld && lastCommitted.Character == managerPlayer.GetPlayerID();
            if (GUILayout.Button(T("Esikatsele viime siirron peruminen", "Preview undo of the last transfer"), GUILayout.Height(34)))
            { SetPreviewClosed(true); undoRequested = true; }
            GUI.enabled = true;
            GUILayout.Label(T("Peruminen toimii tämän pelikerran viimeiselle siirrolle, jos inventaariot eivät ole muuttuneet. Se vaatii kaikki alkuperäiset arkut.",
                "Undo is available for this session's last transfer if inventories are unchanged. All original chests must be nearby."));
            if (unreadableHistory > 0) GUILayout.Label(unreadableHistory + T(" historiatietoa ei voitu lukea. Alkuperäiset tiedostot säilytettiin.", " history entries could not be read. Original files were retained."));
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.ExpandHeight(true));
            foreach (HistoryRecord record in historyRecords)
            {
                string mode = record.Mode == "undo" ? T("Peruminen", "Undo") : record.Mode == "deposit" ? T("Repun lajittelu", "Deposit") : T("Arkkujen järjestely", "Organize chests");
                string status = record.Status == "COMMITTED" ? T("Valmis", "Complete") : record.Status == "ROLLED_BACK" ? T("Peruttu virheen vuoksi", "Rolled back after error") :
                    record.Status == "ROLLBACK_INCOMPLETE" ? T("Palautus jäi kesken — tarkista loki", "Rollback incomplete — check log") : T("Lopputila vahvistamatta — ei saa toistaa", "Outcome unconfirmed — do not replay");
                GUILayout.BeginVertical(GUI.skin.box);
                if (GUILayout.Button(record.Utc.ToLocalTime().ToString("g") + " • " + mode + " • " + status)) expandedHistory = expandedHistory == record.Id ? null : record.Id;
                GUILayout.Label(record.Moves.Sum(m => (long)m.Count) + T(" tavaraa • ", " items • ") + record.ChangedChests + T(" arkkua", " chests"));
                if (expandedHistory == record.Id)
                    foreach (HistoryMove move in record.Moves) GUILayout.Label(move.Count + " × " + move.Item + " • " + move.From + " → " + move.To);
                GUILayout.EndVertical();
            }
            if (historyRecords.Count == 0) GUILayout.Label(T("Siirtohistoria on vielä tyhjä.", "No transfers recorded yet."));
            GUILayout.EndScrollView();
        }
    }
}
