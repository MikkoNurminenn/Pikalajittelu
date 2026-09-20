using System;
using System.IO;
using System.Linq;
using MikkoMods.Storage;

public static class StorageRulesTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message);
    }
    public static void Main()
    {
        var rules = new StorageRules { World = 1234567890123L, Character = -9876543210L };
        rules.Chests.Add("12:34", new NamedChestRule { ChestId = "12:34", Name = "Mökin tina — Testihahmo",
            Rule = new ChestRule { Locked = true, Prefabs = new[] { "Tin", "TinOre" }, Categories = new[] { "Material" } } });
        string encoded = rules.Encode();
        StorageRules restored = StorageRules.Decode(encoded, rules.World, rules.Character);
        Check(restored.Get("12:34").Name == "Mökin tina — Testihahmo", "Unicode name lost");
        Check(restored.Get("12:34").Rule.Locked && restored.Get("12:34").Rule.Prefabs.SequenceEqual(new[] { "Tin", "TinOre" }), "Persisted policy lost");
        Check(restored.Encode() == encoded, "Roundtrip not stable");
        Check(!restored.Get("missing").Rule.Explicit && !restored.Get("missing").Rule.Locked, "New chest must use automatic sorting");
        Reject(() => StorageRules.Decode(encoded, rules.World + 1, rules.Character), "Rules from wrong world accepted");
        Reject(() => StorageRules.Decode(encoded, rules.World, rules.Character + 1), "Rules from wrong character accepted");
        Reject(() => StorageRules.Decode(encoded.Replace("PikalajitteluRules", "PikalajitteluRuleX"), rules.World, rules.Character), "Corrupted data accepted");
        Reject(() => StorageRules.Decode(encoded.Substring(0, encoded.Length - 2), rules.World, rules.Character), "Truncated file accepted");
        Reject(() => StorageRules.Decode("", rules.World, rules.Character), "Empty damaged file accepted");
        rules.Chests["12:34"].Name = "injected\nrow";
        Reject(() => rules.Encode(), "Control character in chest name accepted");
        rules.Chests["12:34"].Name = new String('x', 81);
        Reject(() => rules.Encode(), "Overlong chest name accepted");
        rules.Chests["12:34"].Name = "Tina";

        var quantities = StorageRules.ParseKeep(" Wood = 20, Stone=10,Coins=0 ");
        Check(quantities["wood"] == 20 && quantities["STONE"] == 10 && quantities["Coins"] == 0, "Case insensitive keep amounts failed");
        Check(StorageRules.ParseKeep("").Count == 0, "Empty quantity list failed");
        foreach (string invalid in new[] { "Wood=-1", "Wood=1.5", "Wood=two", "Wood=", "=10", "Wood=1,wood=2", "Wood=2147483648", "Wood=1000001", "Wood=1=2", "Wood\n=1" })
            Reject(() => StorageRules.ParseKeep(invalid), "Invalid quota accepted: " + invalid);
        Check(StorageRules.ParseNames(" Wood,Stone,wood, , ").Length == 2, "Duplicate item names not deduplicated");
        Reject(() => StorageRules.ParseNames("Wood,Stone\nTin"), "Control character in item list accepted");

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules-" + Guid.NewGuid().ToString("N"), "test.rules");
        Check(StorageRules.Load(path, rules.World, rules.Character).Chests.Count == 0, "First run rules not empty");
        rules.Save(path);
        Check(StorageRules.Load(path, rules.World, rules.Character).Get("12:34").Name == "Tina", "Disk roundtrip failed");
        string originalDisk = File.ReadAllText(path);
        rules.Chests["12:34"].Name = "Metallit";
        rules.Save(path);
        Check(File.ReadAllText(path + ".bak") == originalDisk, "Prior rules not backed up on replacement");
        Check(StorageRules.Load(path, rules.World, rules.Character).Get("12:34").Name == "Metallit", "Atomic replacement failed");
        Check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0, "Temp file left after successful save");
        File.WriteAllText(path, "broken");
        Reject(() => StorageRules.Load(path, rules.World, rules.Character), "Corruption silently reset rules");
        Check(File.ReadAllText(path) == "broken" && File.ReadAllText(path + ".bak") == originalDisk, "Failed load changed evidence or backup");

        var locked = StorageRules.Decode(encoded, rules.World, rules.Character).Get("12:34").Rule;
        Check(!locked.Accepts(new Stack { Prefab = "Tin", Category = "Material" }), "Locked saved rule admitted transfer");
        locked.Locked = false;
        Check(locked.Accepts(new Stack { Prefab = "Tin", Category = "Unknown" }), "Explicit prefab not accepted");
        Check(locked.Accepts(new Stack { Prefab = "Wood", Category = "Material" }), "Explicit category not accepted");
        Check(!locked.Accepts(new Stack { Prefab = "Bow", Category = "Bow" }), "Unlisted item/category admitted");
        System.Console.WriteLine("Storage rules: " + checks + " checks passed (identity separation, corruption, atomic replacement, backup, Unicode and quota validation).");
    }
}
