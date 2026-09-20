using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private bool previewVisible, applyRequested;
        private string previewNotice = "";
        private Vector2 previewScroll;
        private int previewPage;
        private const int PreviewPageSize = 60;
        private StorageSession indexedPreview;
        private Dictionary<string, ItemDrop.ItemData> previewSamples;
        private Dictionary<string, StorageState> previewStates;
        private Rect previewRect;
        private bool cursorCaptured;
        private CursorLockMode previousLock;
        private bool previousCursor;

        private void SetPreviewClosed(bool cancel)
        {
            if (ModalVisible) blockInputThroughFrame = Time.frameCount + 1;
            previewVisible = false;
            indexedPreview = null; previewSamples = null; previewStates = null;
            managerVisible = false;
            captureBinding = -1;
            if (cancel) pendingStorage = null;
            if (cursorCaptured)
            {
                Cursor.lockState = previousLock;
                Cursor.visible = previousCursor;
                cursorCaptured = false;
            }
        }

        private void LateUpdate()
        {
            if (!ModalVisible) return;
            if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || Player.m_localPlayer.IsTeleporting() ||
                (previewVisible && (pendingStorage == null || pendingStorage.Player != Player.m_localPlayer)) ||
                (managerVisible && (managerPlayer != Player.m_localPlayer || !ZNet.instance || managerWorld != ZNet.instance.GetWorldUID())))
            { SetPreviewClosed(true); return; }
            if (!cursorCaptured)
            {
                previousLock = Cursor.lockState; previousCursor = Cursor.visible; cursorCaptured = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (!ModalVisible) return;
            PrepareStorageTheme();
            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.skin = storageSkin;
            float scale = uiScale.Value;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float availableWidth = Screen.width / scale, availableHeight = Screen.height / scale;
            float width = Mathf.Min(900, availableWidth - 24);
            float height = Mathf.Min(680, availableHeight - 24);
            previewRect = new Rect((availableWidth - width) / 2, (availableHeight - height) / 2, width, height);
            try
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(0, 0, availableWidth, availableHeight), Texture2D.whiteTexture);
                GUI.color = previousColor;
                if (managerVisible) GUI.ModalWindow(849132, previewRect, DrawStorageManager, T("Pikalajittelu 2 — varastosi", "Pikalajittelu 2 — your storage"));
                else if (pendingStorage != null) GUI.ModalWindow(849132, previewRect, DrawStoragePreview, T("Pikalajittelu 2 — siirtojen esikatselu", "Pikalajittelu 2 — transfer preview"));
            }
            finally { GUI.skin = previousSkin; GUI.matrix = previousMatrix; }
        }

        private string StorageLabel(string id)
        {
            StorageState state = previewStates[id];
            if (state.Model.Player) return T("Reppu", "Inventory");
            return ChestDisplayName(state.Chest, state.Id);
        }

        private void DrawStoragePreview(int window)
        {
            StorageSession session = pendingStorage;
            if (indexedPreview != session)
            {
                previewSamples = session.Plan.Before.SelectMany(b => b.Items).GroupBy(s => s.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => session.Templates[g.First().Template], StringComparer.Ordinal);
                previewStates = session.States.ToDictionary(s => s.Id, StringComparer.Ordinal);
                indexedPreview = session;
            }
            GUILayout.Space(12);
            GUILayout.Label(session.UndoOf != null ? T("Peru edellinen siirto", "Undo last transfer") : session.Consolidate ? T("Järjestä lähistön arkut", "Organize nearby chests") : T("Siirrä resurssit repusta arkkuihin", "Deposit inventory resources"), sectionTitle);
            GUILayout.Label(session.Plan.Moved + T(" tavaraa • ", " items • ") + session.States.Count(s => s.Chest) + T(" arkkua", " chests"));
            if (session.Plan.Remaining > 0) GUILayout.Label(session.Plan.Remaining + T(" tavaraa jää reppuun, koska sopivaa tilaa ei ole.", " items will stay in your inventory because no suitable space is available."));
            if (previewNotice.Length > 0) GUILayout.Label(previewNotice);
            int pages = Math.Max(1, (session.Plan.Moves.Count + PreviewPageSize - 1) / PreviewPageSize);
            previewPage = Mathf.Clamp(previewPage, 0, pages - 1);
            if (pages > 1)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = previewPage > 0;
                if (GUILayout.Button(T("Edellinen", "Previous"), GUILayout.Width(110))) { previewPage--; previewScroll = Vector2.zero; }
                GUI.enabled = true;
                GUILayout.Label(T("Siirrot ", "Transfers ") + (previewPage * PreviewPageSize + 1) + "–" +
                    Math.Min((previewPage + 1) * PreviewPageSize, session.Plan.Moves.Count) + " / " + session.Plan.Moves.Count);
                GUI.enabled = previewPage + 1 < pages;
                if (GUILayout.Button(T("Seuraava", "Next"), GUILayout.Width(110))) { previewPage++; previewScroll = Vector2.zero; }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            previewScroll = GUILayout.BeginScrollView(previewScroll, GUILayout.ExpandHeight(true));
            foreach (var move in session.Plan.Moves.Skip(previewPage * PreviewPageSize).Take(PreviewPageSize))
            {
                ItemDrop.ItemData sample = previewSamples[move.Key];
                string label = Localization.instance.Localize(sample.m_shared.m_name);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(move.Count + " × " + label, GUILayout.Width(Mathf.Min(240, previewRect.width * 0.33f)));
                GUILayout.Label(StorageLabel(move.From) + " → " + StorageLabel(move.To));
                GUILayout.EndHorizontal();
            }
            if (session.Plan.Moves.Count == 0)
                GUILayout.Label(session.States.Any(s => !SameBytes(s.Before, s.After)) ?
                    T("Tavarat ovat oikeissa arkuissa. Järjestetään vielä arkkujen ruudut ja pinot.", "Items are in the right chests. This will arrange their stacks and slots.") : T("Kaikki on jo järjestyksessä. Ei siirrettävää.", "Everything is organized. No items to move."));
            GUILayout.EndScrollView();
            GUILayout.Label(T("Arkut ovat käytettävissä esikatselun aikana. Sisältö tarkistetaan uudelleen ennen siirtoa.", "Chests remain available during preview. Their contents will be checked again before transfer."));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Peruuta", "Cancel"), GUILayout.Height(36))) SetPreviewClosed(true);
            GUI.enabled = session.States.Any(s => !SameBytes(s.Before, s.After));
            if (GUILayout.Button(T("Vahvista siirto", "Confirm transfer"), primaryButton, GUILayout.Height(40)))
            {
                SetPreviewClosed(false);
                applyRequested = true;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static class StorageInputPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (BlockGameplayInput) __result = false;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
        private static class StorageMousePatch
        {
            private static bool Prefix() { return instance == null || !instance.ModalVisible; }
        }
    }
}
