using System.Collections.Generic;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private GUISkin storageSkin;
        private GUIStyle primaryButton, sectionTitle, subtleLabel;
        private readonly List<Texture2D> themeTextures = new List<Texture2D>();

        private Texture2D ThemeColor(float r, float g, float b, float a = 1f)
        {
            var image = new Texture2D(1, 1);
            image.SetPixel(0, 0, new Color(r, g, b, a));
            image.Apply(); image.hideFlags = HideFlags.HideAndDontSave;
            themeTextures.Add(image); return image;
        }

        private void PrepareStorageTheme()
        {
            if (storageSkin) return;
            storageSkin = UnityEngine.Object.Instantiate(GUI.skin);
            storageSkin.hideFlags = HideFlags.HideAndDontSave;
            Color ink = new Color(0.94f, 0.93f, 0.87f);
            Color gold = new Color(0.91f, 0.74f, 0.42f);
            Texture2D panel = ThemeColor(0.075f, 0.09f, 0.09f);
            Texture2D row = ThemeColor(0.12f, 0.14f, 0.14f);
            Texture2D button = ThemeColor(0.20f, 0.24f, 0.23f);
            Texture2D hover = ThemeColor(0.29f, 0.34f, 0.31f);
            Texture2D accent = ThemeColor(gold.r, gold.g, gold.b);
            foreach (GUIStyle style in new[] { storageSkin.label, storageSkin.box, storageSkin.button, storageSkin.toggle,
                storageSkin.textField, storageSkin.textArea, storageSkin.window })
            { style.fontSize = 15; style.normal.textColor = ink; style.hover.textColor = ink; style.focused.textColor = ink; style.active.textColor = ink; }
            storageSkin.label.wordWrap = true;
            storageSkin.label.padding = new RectOffset(2, 2, 4, 4);
            storageSkin.window.normal.background = panel;
            storageSkin.window.onNormal.background = panel;
            storageSkin.window.normal.textColor = gold;
            storageSkin.window.onNormal.textColor = gold;
            storageSkin.window.fontSize = 21;
            storageSkin.window.fontStyle = FontStyle.Bold;
            storageSkin.window.alignment = TextAnchor.UpperLeft;
            storageSkin.window.padding = new RectOffset(18, 18, 44, 18);
            storageSkin.box.normal.background = row;
            storageSkin.box.padding = new RectOffset(10, 10, 6, 6);
            storageSkin.button.normal.background = button;
            storageSkin.button.hover.background = hover;
            storageSkin.button.focused.background = hover;
            storageSkin.button.active.background = accent;
            storageSkin.button.onNormal.background = accent;
            storageSkin.button.onHover.background = accent;
            storageSkin.button.onFocused.background = accent;
            storageSkin.button.onNormal.textColor = Color.black;
            storageSkin.button.onHover.textColor = Color.black;
            storageSkin.button.onFocused.textColor = Color.black;
            storageSkin.button.active.textColor = Color.black;
            storageSkin.button.padding = new RectOffset(12, 12, 7, 7);
            storageSkin.button.margin = new RectOffset(3, 3, 3, 3);
            storageSkin.textField.normal.background = row;
            storageSkin.textField.focused.background = button;
            storageSkin.textField.padding = new RectOffset(8, 8, 7, 7);
            storageSkin.toggle.padding.top = 3;
            storageSkin.toggle.padding.bottom = 3;
            storageSkin.toggle.wordWrap = true;
            storageSkin.toggle.onNormal.textColor = gold;
            storageSkin.toggle.onHover.textColor = gold;
            primaryButton = new GUIStyle(storageSkin.button);
            primaryButton.normal.background = accent;
            primaryButton.normal.textColor = Color.black;
            primaryButton.fontStyle = FontStyle.Bold;
            sectionTitle = new GUIStyle(storageSkin.label) { fontSize = 21, fontStyle = FontStyle.Bold };
            sectionTitle.normal.textColor = gold;
            subtleLabel = new GUIStyle(storageSkin.label) { fontSize = 13 };
            subtleLabel.normal.textColor = new Color(0.67f, 0.73f, 0.70f);
        }

        private void DestroyStorageTheme()
        {
            if (storageSkin) Destroy(storageSkin);
            foreach (Texture2D texture in themeTextures) if (texture) Destroy(texture);
            themeTextures.Clear();
        }
    }
}
