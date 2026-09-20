using System;
using System.Linq;
using MikkoMods;

public static class ConsolidationPlanTests
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static int[] Simulate(int[] input, int[] capacities, string[] ids)
    {
        int[] result = (int[])input.Clone();
        foreach (var route in ConsolidationPlan.Create(input, ids))
        {
            Check(input[route.To] > 0, "Empty chests must not become destinations.");
            Check(input[route.From] <= input[route.To], "Transfers must follow the fixed quantity ranking.");
            if (input[route.From] == input[route.To])
                Check(StringComparer.Ordinal.Compare(ids[route.To], ids[route.From]) < 0, "Tie must use stable ID.");
            int transfer = Math.Min(result[route.From], capacities[route.To] - result[route.To]);
            result[route.From] -= transfer;
            result[route.To] += transfer;
        }
        Check(result.Sum() == input.Sum(), "Item totals changed.");
        Check(result.All(n => n >= 0), "Negative inventory.");
        for (int i = 0; i < result.Length; i++) Check(result[i] <= capacities[i], "Capacity exceeded.");
        return result;
    }

    public static void Main()
    {
        Check(RecoveryRules.OwnerMatches("testihahmo", "testihahmo"), "Exact grave owner must match.");
        Check(RecoveryRules.OwnerMatches("Testihahmo", " testihahmo "), "Grave owner case/outer whitespace.");
        Check(!RecoveryRules.OwnerMatches("testihahmo2", "testihahmo"), "Partial grave owner match must never be accepted.");
        Check(!RecoveryRules.OwnerMatches("testihahmo", ""), "Empty selection must never match graves.");
        Check(!RecoveryRules.OwnerMatches(null, "testihahmo"), "Missing owner must not match.");
        var original = new System.Collections.Generic.Dictionary<string, long> { { "Wood|1|0", 100 }, { "Sword|3|0", 1 } };
        var identical = new System.Collections.Generic.Dictionary<string, long> { { "Sword|3|0", 1 }, { "Wood|1|0", 100 } };
        Check(RecoveryRules.CountsEqual(original, identical), "Unchanged recovery totals must pass.");
        identical["Wood|1|0"] = 101;
        Check(!RecoveryRules.CountsEqual(original, identical), "Duplicated grave items must be rejected.");
        identical["Wood|1|0"] = 99;
        Check(!RecoveryRules.CountsEqual(original, identical), "Lost grave items must be rejected.");
        identical["Wood|1|0"] = 100;
        identical.Remove("Sword|3|0"); identical.Add("Sword|1|0", 1);
        Check(!RecoveryRules.CountsEqual(original, identical), "Changed equipment quality must be rejected.");
        foreach (string type in new[] { "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Shield", "Helmet", "Chest", "Legs", "Shoulder", "Tool", "Torch", "Ammo", "AmmoNonEquipable", "Consumable", "Utility", "Misc", "Trinket", "UnknownFutureType" })
            Check(InventoryProtection.Keep(type, false, false, false, false, false), "Essential category was not protected: " + type);
        foreach (string type in new[] { "Material", "Trophy", "Fish", "Customization" })
        {
            Check(!InventoryProtection.Keep(type, false, false, false, false, false), "Ordinary loot was blocked: " + type);
            Check(InventoryProtection.Keep(type, true, false, false, false, false), "Equipped item was not protected.");
            Check(InventoryProtection.Keep(type, false, true, false, false, false), "Hotbar item was not protected.");
            Check(InventoryProtection.Keep(type, false, false, true, false, false), "Food was not protected.");
            Check(InventoryProtection.Keep(type, false, false, false, true, false), "Quest item was not protected.");
            Check(InventoryProtection.Keep(type, false, false, false, false, true), "Explicitly protected item was not protected.");
        }
        string[] ids = { "a", "b", "c" };
        Check(ConsolidationPlan.Create(new int[0], new string[0]).Count == 0, "Empty input.");
        Check(ConsolidationPlan.Create(new[] { 0, 0, 0 }, ids).Count == 0, "No items.");
        Check(ConsolidationPlan.Create(new[] { 0, 20, 0 }, ids).Count == 0, "Already consolidated.");
        Check(Simulate(new[] { 10, 80, 20 }, new[] { 200, 200, 200 }, ids).SequenceEqual(new[] { 0, 110, 0 }), "Most populated chest should receive all.");
        Check(Simulate(new[] { 80, 60, 10 }, new[] { 85, 100, 100 }, ids).SequenceEqual(new[] { 85, 65, 0 }), "Overflow should use next preferred chest.");
        Check(Simulate(new[] { 80, 60, 10 }, new[] { 80, 60, 10 }, ids).SequenceEqual(new[] { 80, 60, 10 }), "Full inventories must stay unchanged.");
        Check(Simulate(new[] { 10, 10, 0 }, new[] { 100, 100, 100 }, new[] { "z", "a", "b" }).SequenceEqual(new[] { 0, 20, 0 }), "Stable ID tie breaker failed.");
        var first = ConsolidationPlan.Create(new[] { 30, 30, 10 }, new[] { "z", "a", "b" });
        var second = ConsolidationPlan.Create(new[] { 10, 30, 30 }, new[] { "b", "z", "a" });
        string[] firstIds = { "z", "a", "b" };
        string[] secondIds = { "b", "z", "a" };
        Check(first.Select(r => firstIds[r.From] + ">" + firstIds[r.To]).SequenceEqual(second.Select(r => secondIds[r.From] + ">" + secondIds[r.To])), "Object enumeration order changed routing.");
        bool rejected = false;
        try { ConsolidationPlan.Create(new[] { 1 }, new string[0]); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Mismatched arrays must be rejected.");
        Random random = new Random(9020);
        for (int trial = 0; trial < 500; trial++)
        {
            int size = random.Next(2, 15);
            int[] counts = new int[size];
            int[] caps = new int[size];
            string[] keys = new string[size];
            for (int i = 0; i < size; i++)
            {
                counts[i] = random.Next(0, 101);
                caps[i] = counts[i] + random.Next(0, 101);
                keys[i] = i.ToString("D3");
            }
            Simulate(counts, caps, keys);
        }
        Console.WriteLine("PASS: " + assertions + " assertions, including 500 randomized capacity/conservation scenarios.");
        Console.WriteLine("Includes inventory protection rules. These are policy tests, not Unity or multiplayer integration tests.");
    }
}
