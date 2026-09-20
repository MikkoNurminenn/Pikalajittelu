using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using MikkoMods.Storage;
using UnityEngine;

namespace MikkoMods
{
    public sealed partial class Pikalajittelu
    {
        private ConfigEntry<KeyboardShortcut> managerShortcut;
        private ConfigEntry<string> uiLanguage, keepQuantities;
        private ConfigEntry<float> uiScale;
        private StorageRules currentRules;
        private string settingsMessage = "";
        private bool rulesReadFailed;

        private string T(string finnish, string english)
        { return uiLanguage != null && uiLanguage.Value == "English" ? english : finnish; }

        private void InitializeStorageSettings()
        {
            managerShortcut = Config.Bind("Controls", "StorageWindow", new KeyboardShortcut(KeyCode.P, KeyCode.LeftAlt), "Open storage search, chest rules and settings.");
            uiLanguage = Config.Bind("Interface", "Language", "Suomi", new ConfigDescription("Interface language.", new AcceptableValueList<string>("Suomi", "English")));
            uiScale = Config.Bind("Interface", "Scale", 1f, new ConfigDescription("Storage window and text scale.", new AcceptableValueRange<float>(0.8f, 1.5f)));
            keepQuantities = Config.Bind("Protection", "KeepQuantities", "", "Keep at least these amounts: Wood=20,Stone=10. Protected stacks count toward the total.");
        }

        private string RulesPath(long world, long character)
        {
            return Path.Combine(Paths.ConfigPath, "Pikalajittelu", "rules", world + "-" + character + ".rules");
        }

        private StorageRules ReadCurrentRules()
        {
            if (!ZNet.instance || !Player.m_localPlayer) throw new InvalidOperationException(T("Maailma ei ole valmis.", "The world is not ready."));
            long world = ZNet.instance.GetWorldUID(), character = Player.m_localPlayer.GetPlayerID();
            try
            {
                currentRules = StorageRules.Load(RulesPath(world, character), world, character);
                rulesReadFailed = false;
                return currentRules;
            }
            catch (Exception error)
            {
                rulesReadFailed = true;
                Logger.LogError(error);
                throw new InvalidOperationException(T("Arkkusääntöjen lukeminen epäonnistui. Lajittelu on estetty, jotta sääntöjä ei ohiteta.",
                    "Chest rules could not be read. Sorting is stopped so your rules cannot be bypassed."), error);
            }
        }

        private string ChestDisplayName(Container chest, string id)
        {
            if (currentRules != null)
            {
                string name = currentRules.Get(id).Name;
                if (name.Length > 0) return name;
            }
            if (!chest) return T("Arkku", "Chest") + " " + id;
            Vector3 p = chest.transform.position;
            string shortId = id.Length > 4 ? id.Substring(id.Length - 4).ToUpperInvariant() : id;
            return T("Arkku", "Chest") + " " + shortId + " (" + Math.Round(p.x) + ", " + Math.Round(p.y) + ", " + Math.Round(p.z) + ")";
        }

        private static readonly int StableChestKey = "mikko.valheim.pikalajittelu.stableChestId".GetStableHashCode();
        private static string ChestId(Container chest, bool create = false)
        {
            var view = (ZNetView)View.GetValue(chest);
            ZDO data = view.GetZDO();
            string stable = data.GetString(StableChestKey, "");
            Guid id;
            if (stable.Length > 0)
            {
                if (!Guid.TryParseExact(stable, "N", out id)) throw new InvalidOperationException("Invalid persistent chest identity. No changes made.");
                return "chest:" + id.ToString("N");
            }
            if (!create) return "session:" + data.m_uid;
            if (!view.IsOwner()) throw new InvalidOperationException("Chest ownership required before creating an identity.");
            stable = Guid.NewGuid().ToString("N");
            data.Set(StableChestKey, stable);
            return "chest:" + stable;
        }

        private string CategoryName(string name)
        {
            switch (name)
            {
                case "Material": return T("Materiaalit", "Materials");
                case "Trophy": return T("Pokaalit", "Trophies");
                case "Consumable": return T("Ruoat ja juomat", "Food and potions");
                case "Ammo": return T("Ammukset", "Ammunition");
                case "Tool": return T("Työkalut", "Tools");
                case "Utility": return T("Hyötyesineet", "Utility items");
                case "Helmet": return T("Kypärät", "Helmets");
                case "Chest": return T("Rintapanssarit", "Chest armor");
                case "Legs": return T("Jalkapanssarit", "Leg armor");
                case "Shoulder": return T("Viitat", "Capes");
                case "Shield": return T("Kilvet", "Shields");
                case "Bow": return T("Jouset", "Bows");
                case "OneHandedWeapon": return T("Yhden käden aseet", "One-handed weapons");
                case "TwoHandedWeapon": return T("Kahden käden aseet", "Two-handed weapons");
                case "TwoHandedWeaponLeft": return T("Muut kahden käden aseet", "Other two-handed weapons");
                case "Fish": return T("Kalat", "Fish");
                default: return name;
            }
        }
    }
}
