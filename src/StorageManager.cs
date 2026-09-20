using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using MikkoMods.Storage;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private bool managerVisible;
        private bool ModalVisible { get { return previewVisible || managerVisible; } }
        private Player managerPlayer;
        private long managerWorld;
        private int managerTab, captureBinding = -1;
        private string search = "", editChestId, editName = "", editItems = "", editCategories = "";
        private bool editLocked, settingsHotbar, settingsEnglish;
        private string settingsKeep = "", settingsNever = "", settingsAlways = "";
        private float settingsRadius, settingsScale, lastSearchRefresh;
        private KeyboardShortcut[] settingsKeys;
        private Vector2 managerScroll, editorScroll;
        private readonly List<ObservedChest> observed = new List<ObservedChest>();

        private sealed class ObservedItem
        {
            public string Prefab, Name, Category;
            public long Count;
        }
        private sealed class ObservedChest
        {
            public Container Chest;
            public string Id;
            public int Used, Slots;
            public List<ObservedItem> Items;
        }

        private void OpenStorageManager()
        {
            managerPlayer = Player.m_localPlayer;
            managerWorld = ZNet.instance.GetWorldUID();
            managerTab = 0; editChestId = null; settingsMessage = "";
            currentRules = null; observed.Clear();
            settingsRadius = radius.Value; settingsHotbar = protectHotbar.Value;
            settingsScale = uiScale.Value;
            settingsEnglish = uiLanguage.Value == "English";
            settingsKeep = keepQuantities.Value; settingsNever = excluded.Value; settingsAlways = keepInInventory.Value;
            settingsKeys = new[] { shortcut.Value, chestShortcut.Value, managerShortcut.Value };
            captureBinding = -1;
            try { ReadCurrentRules(); RefreshObservedStorage(); RefreshHistory(); }
            catch (Exception error) { settingsMessage = error.Message; }
            managerVisible = true;
        }

        private void RefreshObservedStorage()
        {
            observed.Clear();
            foreach (Container chest in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => Eligible(c, managerPlayer)).OrderBy(c => (c.transform.position - managerPlayer.transform.position).sqrMagnitude))
            {
                Inventory inv = chest.GetInventory();
                var entry = new ObservedChest { Chest = chest, Id = ChestId(chest), Used = inv.GetAllItems().Count,
                    Slots = inv.GetWidth() * inv.GetHeight(), Items = new List<ObservedItem>() };
                foreach (var group in inv.GetAllItems().Where(i => i != null && i.m_shared != null && i.m_dropPrefab)
                    .GroupBy(i => i.m_dropPrefab.name, StringComparer.Ordinal))
                {
                    ItemDrop.ItemData sample = group.First();
                    entry.Items.Add(new ObservedItem { Prefab = group.Key, Name = Localization.instance.Localize(sample.m_shared.m_name),
                        Category = sample.m_shared.m_itemType.ToString(), Count = group.Sum(i => (long)i.m_stack) });
                }
                observed.Add(entry);
            }
            lastSearchRefresh = Time.unscaledTime;
        }

        private void UpdateStorageManager()
        {
            if (!managerVisible || managerTab != 0 || Time.unscaledTime - lastSearchRefresh < 3f) return;
            try { RefreshObservedStorage(); }
            catch (Exception error) { settingsMessage = error.Message; }
        }

        private void DrawStorageManager(int window)
        {
            GUILayout.Space(10);
            managerTab = GUILayout.Toolbar(managerTab, new[] { T("Etsi tavaraa", "Find items"), T("Arkut", "Chests"), T("Asetukset", "Settings"), T("Historia", "History") }, GUILayout.Height(32));
            if (settingsMessage.Length > 0) GUILayout.Label(settingsMessage, GUI.skin.box);
            if (managerTab == 0) DrawStorageSearch();
            else if (managerTab == 1) DrawChestEditor();
            else if (managerTab == 2) DrawStorageSettings();
            else DrawStorageHistory();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(T("Sulje", "Close"), GUILayout.Height(34))) SetPreviewClosed(true);
        }

        private void DrawStorageSearch()
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("StorageSearch");
            search = GUILayout.TextField(search, 80, GUILayout.Height(28));
            if (GUILayout.Button(T("Päivitä", "Refresh"), GUILayout.Width(90), GUILayout.Height(28)))
            {
                try { RefreshObservedStorage(); }
                catch (Exception error) { settingsMessage = error.Message; }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(T("Hae tavaran, kategorian tai arkun nimellä.", "Search by item, category or chest name."));
            var rows = observed.Where(c => c.Chest && Eligible(c.Chest, managerPlayer)).SelectMany(c => c.Items.Select(i => new { Chest = c, Item = i }))
                .Where(row => search.Trim().Length == 0 || new[] { row.Item.Name, row.Item.Prefab, CategoryName(row.Item.Category), ChestDisplayName(row.Chest.Chest, row.Chest.Id) }
                    .Any(s => s.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(row => row.Item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Chest.Id, StringComparer.Ordinal).ToArray();
            GUILayout.Label(rows.Sum(row => row.Item.Count) + T(" tavaraa • ", " items • ") + rows.Length + T(" osumaa", " matches"));
            managerScroll = GUILayout.BeginScrollView(managerScroll, GUILayout.ExpandHeight(true));
            foreach (var row in rows.Take(120))
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(row.Item.Count + " × " + row.Item.Name, GUILayout.Width(previewRect.width * 0.36f));
                GUILayout.Label(ChestDisplayName(row.Chest.Chest, row.Chest.Id));
                GUILayout.Label(Math.Round(Vector3.Distance(row.Chest.Chest.transform.position, managerPlayer.transform.position), 1) + " m", GUILayout.Width(64));
                if (GUILayout.Button(T("Säännöt", "Rules"), GUILayout.Width(72))) { SelectChest(row.Chest); managerTab = 1; }
                GUILayout.EndHorizontal();
            }
            if (rows.Length == 0) GUILayout.Label(T("Ei osumia käytettävissä olevissa lähiarkuissa.", "No matches in nearby accessible chests."));
            if (rows.Length > 120) GUILayout.Label(T("Näytetään ensimmäiset 120 osumaa. Tarkenna hakua.", "Showing the first 120 matches. Narrow your search."));
            GUILayout.EndScrollView();
            GUILayout.Label(T("Haku näyttää ladattujen arkkujen viimeksi havaitun sisällön. Avoimet ja suojatut arkut ohitetaan.",
                "Search shows the last observed contents of loaded chests. Open and inaccessible chests are skipped."));
        }

        private void SelectChest(ObservedChest chest)
        {
            editChestId = chest.Id;
            NamedChestRule entry = currentRules == null ? new NamedChestRule { ChestId = chest.Id } : currentRules.Get(chest.Id);
            editName = entry.Name; editLocked = entry.Rule.Locked;
            editItems = String.Join(",", entry.Rule.Prefabs); editCategories = String.Join(",", entry.Rule.Categories);
            settingsMessage = "";
        }

        private void DrawChestEditor()
        {
            GUILayout.Label(T("Säännöt koskevat tämän hahmon tekemää lajittelua tässä maailmassa.",
                "Rules apply to sorting by this character in this world."));
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            managerScroll = GUILayout.BeginScrollView(managerScroll, GUILayout.Width(previewRect.width * 0.34f));
            foreach (ObservedChest chest in observed.Where(c => c.Chest))
            {
                GUI.enabled = Eligible(chest.Chest, managerPlayer);
                string label = ChestDisplayName(chest.Chest, chest.Id) + "\n" + chest.Used + "/" + chest.Slots + T(" ruutua", " slots");
                if (GUILayout.Toggle(editChestId == chest.Id, label, GUI.skin.button, GUILayout.Height(50)))
                    if (editChestId != chest.Id) SelectChest(chest);
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            ObservedChest selected = observed.FirstOrDefault(c => c.Id == editChestId && c.Chest);
            if (selected == null) GUILayout.Label(T("Valitse arkku vasemmalta.", "Choose a chest on the left."));
            else
            {
                GUILayout.Label(T("Arkun nimi", "Chest name"));
                editName = GUILayout.TextField(editName, 80);
                editLocked = GUILayout.Toggle(editLocked, T("Jätä tämä arkku lajittelun ulkopuolelle", "Exclude this chest from sorting"));
                GUILayout.Label(T("Sallitut kategoriat", "Allowed categories"));
                string[] picked = StorageRules.ParseNames(editCategories);
                string[] categories = { "Material", "Trophy", "Consumable", "Ammo", "Tool", "Utility", "Helmet", "Chest", "Legs", "Shoulder", "Shield", "Bow", "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Fish" };
                int columns = previewRect.width >= 800 ? 2 : 1;
                for (int index = 0; index < categories.Length; index++)
                {
                    if (index % columns == 0) GUILayout.BeginHorizontal();
                    string category = categories[index];
                    bool had = picked.Contains(category, StringComparer.OrdinalIgnoreCase);
                    bool now = GUILayout.Toggle(had, CategoryName(category), GUILayout.Width((previewRect.width * 0.66f - 68) / columns));
                    if (now != had)
                    {
                        var list = picked.ToList();
                        if (now) list.Add(category); else list.RemoveAll(c => String.Equals(c, category, StringComparison.OrdinalIgnoreCase));
                        editCategories = String.Join(",", list.ToArray());
                    }
                    if (index % columns == columns - 1 || index == categories.Length - 1) GUILayout.EndHorizontal();
                }
                GUILayout.Space(8);
                GUILayout.Label(T("Salli yksittäisiä tavaroita arkun nykyisestä sisällöstä:", "Allow individual items from this chest:"));
                foreach (ObservedItem item in selected.Items.OrderBy(i => i.Name))
                {
                    bool had = EditorNames(editItems).Contains(item.Prefab, StringComparer.OrdinalIgnoreCase);
                    bool now = GUILayout.Toggle(had, item.Name);
                    if (had != now)
                    {
                        var list = EditorNames(editItems).ToList();
                        if (now) list.Add(item.Prefab); else list.RemoveAll(p => String.Equals(p, item.Prefab, StringComparison.OrdinalIgnoreCase));
                        editItems = String.Join(",", list.ToArray());
                    }
                }
                GUILayout.Label(T("Tarkat esinetunnisteet (pilkuilla):", "Exact item IDs (comma separated):"));
                editItems = GUILayout.TextField(editItems, 2048);
                GUILayout.Label(T("Tyhjä valinta sallii kaikki tavarat. Valitut kategoriat TAI tavarat hyväksytään. Lukitus ohittaa molemmat.",
                    "An empty selection allows all items. Selected categories OR items are accepted. Exclusion overrides both."));
            }
            GUILayout.EndScrollView();
            if (selected != null)
            {
                GUI.enabled = !rulesReadFailed && Eligible(selected.Chest, managerPlayer);
                if (GUILayout.Button(T("Tallenna arkun säännöt", "Save chest rules"), primaryButton, GUILayout.Height(38))) SaveChestRule(selected);
                GUI.enabled = true;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void SaveChestRule(ObservedChest chest)
        {
            try
            {
                if (!Eligible(chest.Chest, managerPlayer)) throw new InvalidOperationException(T("Arkku ei ole käytettävissä.", "Chest is unavailable."));
                string[] items = StorageRules.ParseNames(editItems);
                foreach (string item in items)
                    if (!ObjectDB.instance.GetItemPrefab(item)) throw new ArgumentException(T("Tuntematon esinetunniste: ", "Unknown item ID: ") + item);
                var desired = new NamedChestRule { ChestId = chest.Id, Name = editName.Trim(),
                    Rule = new ChestRule { Locked = editLocked, Prefabs = items, Categories = StorageRules.ParseNames(editCategories) } };
                Player player = managerPlayer;
                long world = managerWorld;
                SetPreviewClosed(true);
                StartCoroutine(SaveReservedChestRule(player, world, chest.Chest, desired));
            }
            catch (Exception error) { settingsMessage = error.Message; Logger.LogError(error); }
        }

        private IEnumerator SaveReservedChestRule(Player player, long world, Container chest, NamedChestRule desired)
        {
            if (busy) yield break;
            busy = true;
            string result = T("Arkun tallennus peruttiin.", "Saving chest rules was cancelled.");
            try
            {
                pendingReservationTarget = chest;
                pendingReservationAction = currentPlayer => {
                    try
                    {
                        if (!OwnedReservation(chest, currentPlayer) || ZNet.instance.GetWorldUID() != world || currentPlayer != player)
                            throw new InvalidOperationException(T("Arkku ei ole käytettävissä.", "Chest is unavailable."));
                        StorageRules fresh = ReadCurrentRules();
                        desired.ChestId = ChestId(chest, true);
                        fresh.Chests[desired.ChestId] = desired;
                        fresh.Save(RulesPath(fresh.World, fresh.Character));
                        currentRules = fresh;
                        result = T("Arkun säännöt tallennettu.", "Chest rules saved.");
                    }
                    catch (Exception error) { result = error.Message; throw; }
                };
                yield return Organize(player, false);
            }
            finally { pendingReservationAction = null; pendingReservationTarget = null; busy = false; }
            if (Ready(player) && ZNet.instance.GetWorldUID() == world)
            {
                OpenStorageManager(); managerTab = 1;
                ObservedChest selected = observed.FirstOrDefault(c => c.Chest == chest);
                if (selected != null) SelectChest(selected);
                settingsMessage = result;
            }
        }

        private static string[] EditorNames(string value)
        {
            // A partially typed text field must not throw during OnGUI. Strict
            // validation happens only when the user saves the finished rule.
            return value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void DrawStorageSettings()
        {
            CaptureShortcutKey();
            editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.ExpandHeight(true));
            settingsEnglish = GUILayout.Toggle(settingsEnglish, "English interface");
            GUILayout.Label(T("Valikon ja tekstin koko: ", "Menu and text size: ") + Math.Round(settingsScale * 100) + "%");
            settingsScale = GUILayout.HorizontalSlider(settingsScale, 0.8f, 1.5f);
            GUILayout.Label(T("Lajittelun etäisyys: ", "Sorting distance: ") + Math.Round(settingsRadius) + " m");
            settingsRadius = GUILayout.HorizontalSlider(settingsRadius, 1, 20);
            settingsHotbar = GUILayout.Toggle(settingsHotbar, T("Suojaa pikapalkki", "Protect hotbar"));
            GUILayout.Label(T("Varusteet, ruoat, ammukset ja tehtäväesineet pysyvät repussa automaattisesti.",
                "Equipment, food, ammunition and quest items stay in your inventory automatically."));
            GUILayout.Label(T("Pidä vähintään nämä määrät (esim. Wood=20,Stone=10):", "Keep at least these amounts (e.g. Wood=20,Stone=10):"));
            settingsKeep = GUILayout.TextField(settingsKeep, 2048);
            GUILayout.Label(T("Pidä nämä tavarat aina repussa (esinetunnisteet):", "Always keep these items in your inventory (item IDs):"));
            settingsAlways = GUILayout.TextField(settingsAlways, 2048);
            GUILayout.Label(T("Älä siirrä näitä tavaroita edes arkkujen välillä:", "Never move these items, even between chests:"));
            settingsNever = GUILayout.TextField(settingsNever, 2048);
            string[] labels = { T("Repun lajittelu", "Deposit inventory"), T("Arkkujen järjestely", "Organize chests"), T("Varastovalikko", "Storage menu") };
            for (int i = 0; i < settingsKeys.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(labels[i]);
                if (GUILayout.Button(captureBinding == i ? T("Paina näppäinyhdistelmää…", "Press a key combination…") : settingsKeys[i].ToString(), GUILayout.Width(280))) captureBinding = i;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(T("Esc peruu näppäinvalinnan. Asetukset tallentuvat Tallenna-painikkeesta.",
                "Escape cancels key capture. Changes take effect when you press Save."));
            if (GUILayout.Button(T("Tallenna asetukset", "Save settings"), GUILayout.Height(34))) SaveStorageSettings();
            GUILayout.EndScrollView();
        }

        private void CaptureShortcutKey()
        {
            Event key = Event.current;
            if (captureBinding < 0 || key.type != EventType.KeyDown) return;
            if (key.keyCode == KeyCode.Escape) { captureBinding = -1; key.Use(); return; }
            if (key.keyCode == KeyCode.None || new[] { KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt }.Contains(key.keyCode)) return;
            var modifiers = new List<KeyCode>();
            foreach (KeyCode modifier in new[] { KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt })
                if (Input.GetKey(modifier)) modifiers.Add(modifier);
            settingsKeys[captureBinding] = new KeyboardShortcut(key.keyCode, modifiers.ToArray());
            captureBinding = -1;
            key.Use();
        }

        private void SaveStorageSettings()
        {
            float oldRadius = radius.Value;
            float oldScale = uiScale.Value;
            bool oldHotbar = protectHotbar.Value, oldSave = Config.SaveOnConfigSet;
            string oldKeep = keepQuantities.Value, oldNever = excluded.Value, oldAlways = keepInInventory.Value, oldLanguage = uiLanguage.Value;
            var oldKeys = new[] { shortcut.Value, chestShortcut.Value, managerShortcut.Value };
            bool saved = false;
            try
            {
                StorageRules.ParseKeep(settingsKeep);
                string never = String.Join(",", StorageRules.ParseNames(settingsNever));
                string always = String.Join(",", StorageRules.ParseNames(settingsAlways));
                if (settingsKeys.Select(k => k.ToString()).Distinct().Count() != settingsKeys.Length)
                    throw new ArgumentException(T("Valitse eri näppäinyhdistelmä jokaiselle toiminnolle.", "Choose a different shortcut for each action."));
                Config.SaveOnConfigSet = false;
                try
                {
                    radius.Value = (float)Math.Round(settingsRadius); protectHotbar.Value = settingsHotbar;
                    uiScale.Value = (float)Math.Round(settingsScale, 2);
                    keepQuantities.Value = settingsKeep.Trim(); excluded.Value = never; keepInInventory.Value = always;
                    shortcut.Value = settingsKeys[0]; chestShortcut.Value = settingsKeys[1]; managerShortcut.Value = settingsKeys[2];
                    uiLanguage.Value = settingsEnglish ? "English" : "Suomi";
                    Config.Save();
                    saved = true;
                }
                finally { Config.SaveOnConfigSet = oldSave; }
                settingsMessage = T("Asetukset tallennettu.", "Settings saved.");
                RefreshObservedStorage();
            }
            catch (Exception error)
            {
                if (!saved)
                {
                    Config.SaveOnConfigSet = false;
                    try
                    {
                        radius.Value = oldRadius; protectHotbar.Value = oldHotbar;
                        uiScale.Value = oldScale;
                        keepQuantities.Value = oldKeep; excluded.Value = oldNever; keepInInventory.Value = oldAlways;
                        uiLanguage.Value = oldLanguage; shortcut.Value = oldKeys[0]; chestShortcut.Value = oldKeys[1]; managerShortcut.Value = oldKeys[2];
                    }
                    finally { Config.SaveOnConfigSet = oldSave; }
                }
                settingsMessage = error.Message; Logger.LogError(error);
            }
        }
    }
}
