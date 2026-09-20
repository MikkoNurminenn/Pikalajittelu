using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MikkoMods.Storage
{
    public sealed class NamedChestRule
    {
        public string ChestId, Name = "";
        public ChestRule Rule = new ChestRule();
    }

    public sealed class StorageRules
    {
        public long World, Character;
        public Dictionary<string, NamedChestRule> Chests = new Dictionary<string, NamedChestRule>(StringComparer.Ordinal);

        public NamedChestRule Get(string id)
        {
            NamedChestRule entry;
            return Chests.TryGetValue(id, out entry) ? entry : new NamedChestRule { ChestId = id };
        }

        public static string[] ParseNames(string text)
        {
            string[] names = (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length > 512 || names.Any(s => s.Length > 128 || s.Any(c => Char.IsControl(c) || c == '=')))
                throw new ArgumentException("Invalid item/category list.");
            return names;
        }

        public static Dictionary<string, int> ParseKeep(string text)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                string[] pair = token.Split('=');
                int count;
                if (pair.Length != 2 || String.IsNullOrWhiteSpace(pair[0]) || pair[0].Trim().Length > 128 ||
                    !Int32.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count > 1000000 ||
                    pair[0].Any(Char.IsControl) || result.ContainsKey(pair[0].Trim()))
                    throw new ArgumentException("Use unique item=count entries, for example Wood=20,Stone=10 (0–1000000).");
                result.Add(pair[0].Trim(), count);
            }
            return result;
        }

        private static string Pack(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
        private static string Unpack(string text) { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(text)); }
        private static string Hash(string text)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        public string Encode()
        {
            if (Chests.Count > 10000) throw new ArgumentException("Too many chest rules.");
            var rows = new List<string> { "PikalajitteluRules\t1\t" + World.ToString(CultureInfo.InvariantCulture) + "\t" + Character.ToString(CultureInfo.InvariantCulture) };
            foreach (var entry in Chests.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                NamedChestRule value = entry.Value;
                if (value == null || value.Rule == null || entry.Key != value.ChestId || String.IsNullOrEmpty(value.ChestId) ||
                    value.ChestId.Length > 128 || value.Name == null || value.Name.Length > 80 || value.Name.Any(Char.IsControl))
                    throw new ArgumentException("Invalid chest rule/name.");
                rows.Add(String.Join("\t", new[] { Pack(value.ChestId), Pack(value.Name), value.Rule.Locked ? "1" : "0",
                    Pack(String.Join(",", ParseNames(String.Join(",", value.Rule.Prefabs)))),
                    Pack(String.Join(",", ParseNames(String.Join(",", value.Rule.Categories)))) }));
            }
            string payload = String.Join("\n", rows.ToArray()) + "\n";
            return payload + "SHA256\t" + Hash(payload);
        }

        public static StorageRules Decode(string text, long world, long character)
        {
            if (text == null || text.Length > 4000000) throw new InvalidDataException("Rule file is missing or too large.");
            int checksum = text.LastIndexOf("\nSHA256\t", StringComparison.Ordinal);
            if (checksum < 0 || text.Substring(checksum + 8) != Hash(text.Substring(0, checksum + 1)))
                throw new InvalidDataException("Rule checksum does not match. Original file retained.");
            string[] rows = text.Substring(0, checksum).Split('\n');
            string expected = "PikalajitteluRules\t1\t" + world.ToString(CultureInfo.InvariantCulture) + "\t" + character.ToString(CultureInfo.InvariantCulture);
            if (rows[0] != expected) throw new InvalidDataException("Rules belong to another world/character or version.");
            var result = new StorageRules { World = world, Character = character };
            for (int row = 1; row < rows.Length; row++)
            {
                string[] parts = rows[row].Split('\t');
                if (parts.Length != 5 || (parts[2] != "0" && parts[2] != "1")) throw new InvalidDataException("Malformed chest rule.");
                var rule = new NamedChestRule { ChestId = Unpack(parts[0]), Name = Unpack(parts[1]),
                    Rule = new ChestRule { Locked = parts[2] == "1", Prefabs = ParseNames(Unpack(parts[3])), Categories = ParseNames(Unpack(parts[4])) } };
                if (result.Chests.ContainsKey(rule.ChestId)) throw new InvalidDataException("Duplicate chest ID.");
                result.Chests.Add(rule.ChestId, rule);
            }
            result.Encode(); // Apply the same length/shape validation on reads.
            return result;
        }

        public static StorageRules Load(string path, long world, long character)
        {
            if (!File.Exists(path)) return new StorageRules { World = world, Character = character };
            if (new FileInfo(path).Length > 4000000) throw new InvalidDataException("Rule file too large.");
            return Decode(File.ReadAllText(path, Encoding.UTF8), world, character);
        }

        public void Save(string path)
        {
            byte[] data = Encoding.UTF8.GetBytes(Encode());
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(data, 0, data.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
