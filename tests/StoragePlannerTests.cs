using System;
using System.Collections.Generic;
using System.Linq;
using MikkoMods;
using MikkoMods.Storage;

public static class StoragePlannerTests
{
    private static int checks;
    private static void Check(bool result, string message)
    {
        checks++;
        if (!result) throw new Exception(message);
    }
    private static Stack Item(string prefab, int count, int slot, string metadata = "plain", bool protect = false)
    {
        return new Stack { Template = prefab + slot + metadata, Key = prefab + ":" + metadata, Prefab = prefab,
            Category = "Material", Count = count, Limit = 50, Slot = slot, Fixed = protect };
    }
    private static Bin Chest(string id, int slots, params Stack[] items)
    {
        return new Bin { Id = id, Capacity = slots, Items = items.ToList() };
    }
    private static string Layout(IEnumerable<Bin> bins)
    {
        return String.Join(";", bins.OrderBy(b => b.Id).Select(b => b.Id + "=" + String.Join(",",
            b.Items.OrderBy(s => s.Slot).Select(s => s.Key + "/" + s.Count + "@" + s.Slot + "/" + s.Fixed).ToArray())).ToArray());
    }
    private static void Preserved(Plan plan, IEnumerable<Bin> original, string unchanged)
    {
        Check(Layout(original) == unchanged, "Planning mutated input");
        Check(RecoveryRules.CountsEqual(StoragePlanner.Totals(plan.Before), StoragePlanner.Totals(plan.After)), "Lost/duplicated items or metadata");
        StoragePlanner.Validate(plan.After);
    }
    private static void Throws(Action action, string reason)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, reason);
    }
    public static void Main()
    {
        var player = Chest("player", 8, Item("Tin", 28, 4)); player.Player = true;
        var nearlyFull = Chest("a", 1, Item("Tin", 45, 0));
        var spillover = Chest("b", 1);
        var input = new[] { player, nearlyFull, spillover };
        string original = Layout(input);
        Plan result = StoragePlanner.Deposit(input, null);
        Preserved(result, input, original);
        Check(result.Valid && result.Moved == 28, "28 tin must all be deposited");
        Check(result.After.Single(b => b.Id == "a").Items[0].Count == 50, "Partial stack must fill exactly");
        Check(result.After.Single(b => b.Id == "b").Items[0].Count == 23, "Remaining 23 tin must exist");
        result = StoragePlanner.Deposit(new[] { player, nearlyFull }, null);
        Check(result.Moved == 5 && result.Remaining == 23 && result.After.Single(b => b.Player).Items[0].Count == 23,
            "Full destination must leave remainder in player inventory");

        player.Items.Add(Item("Tin", 10, 0, "plain", true));
        result = StoragePlanner.Deposit(input, new Dictionary<string, int> { { "tin", 20 } });
        Check(result.Moved == 18 && result.After.Single(b => b.Player).Items.Sum(s => s.Count) == 20,
            "Keep quota includes protected hotbar stacks, case insensitive");
        Check(result.After.Single(b => b.Player).Items.Single(s => s.Fixed).Count == 10, "Protected stack must be untouched");

        var fullA = Chest("a", 2, Item("Wood", 20, 0), Item("Stone", 10, 1));
        var fullB = Chest("b", 2, Item("Wood", 10, 0), Item("Stone", 20, 1));
        input = new[] { fullA, fullB }; original = Layout(input);
        result = StoragePlanner.Consolidate(input);
        Preserved(result, input, original);
        Check(result.Valid && result.Moved == 20, "Full chests must be able to exchange items");
        Check(result.After.Single(b => b.Id == "a").Items.Single().Prefab == "Wood", "Largest existing amount wins");
        Check(result.After.Single(b => b.Id == "b").Items.Single().Prefab == "Stone", "Other chest receives stone");
        Check(Layout(StoragePlanner.Consolidate(result.After).After) == Layout(result.After), "Repeated consolidation must be stable");
        Check(Layout(StoragePlanner.Consolidate(input.Reverse()).After) == Layout(result.After), "Enumeration order must not change routing");

        fullB.Rule.Locked = true;
        result = StoragePlanner.Consolidate(input);
        Check(Layout(new[] { result.After.Single(b => b.Id == "b") }) == Layout(new[] { fullB }), "Locked chest changed");
        fullB.Rule.Locked = false;
        fullA.Rule.Prefabs = new[] { "Stone" }; fullB.Rule.Prefabs = new[] { "Wood" };
        result = StoragePlanner.Consolidate(input);
        Check(result.Valid && result.After.Single(b => b.Id == "a").Items.Single().Prefab == "Stone", "Explicit chest rules override majority");
        fullA.Rule.Prefabs = new[] { "Wood" };
        result = StoragePlanner.Consolidate(input);
        Check(!result.Valid && result.Moved == 0 && Layout(result.After) == Layout(input), "Impossible rules must cancel whole consolidation");

        player = Chest("player", 4, Item("Tin", 28, 0, "cheated")); player.Player = true;
        result = StoragePlanner.Deposit(new[] { player, nearlyFull }, null);
        Check(result.Moved == 0, "Different cheat/custom metadata must not merge into same stack");
        var custom = Chest("a", 4, Item("Wood", 1, 0, "crafter-a"), Item("Wood", 2, 1, "crafter-b"));
        result = StoragePlanner.Consolidate(new[] { custom });
        Check(result.After[0].Items.Count == 2, "Distinct crafter metadata merged");
        var fixedStack = Chest("a", 2, Item("Tin", 20, 0, "plain", true));
        result = StoragePlanner.Consolidate(new[] { fixedStack, Chest("b", 2, Item("Tin", 10, 0)) });
        Check(result.After[0].Items.Single(s => s.Fixed).Count == 20, "Consolidation modified fixed stack");

        Throws(() => StoragePlanner.Consolidate(new[] { Chest("x", 1, Item("Tin", 0, 0)) }), "Zero stack accepted");
        Throws(() => StoragePlanner.Consolidate(new[] { Chest("x", 1, Item("Tin", 51, 0)) }), "Oversized stack accepted");
        Throws(() => StoragePlanner.Consolidate(new[] { Chest("x", 1, Item("Tin", 1, 1)) }), "Out of bounds slot accepted");
        Throws(() => StoragePlanner.Consolidate(new[] { Chest("x", 2, Item("Tin", 1, 0), Item("Wood", 1, 0)) }), "Duplicate occupied slot accepted");
        Throws(() => StoragePlanner.Consolidate(new[] { Chest("x", 1), Chest("x", 1) }), "Duplicate chest identity accepted");
        Throws(() => StoragePlanner.Deposit(new[] { player }, new Dictionary<string, int> { { "Tin", -1 } }), "Negative reserve accepted");

        var random = new Random(20260920);
        int stablePlans = 0;
        for (int scenario = 0; scenario < 2500; scenario++)
        {
            var bins = new List<Bin>();
            int chests = random.Next(1, 9);
            for (int c = 0; c < chests; c++)
            {
                Bin bin = Chest("chest" + c, random.Next(1, 13));
                bin.Rule.Locked = random.Next(12) == 0;
                if (random.Next(5) == 0) bin.Rule.Prefabs = new[] { "Item" + random.Next(4) };
                for (int slot = 0; slot < bin.Capacity; slot++)
                    if (random.Next(3) != 0) bin.Items.Add(Item("Item" + random.Next(4), random.Next(1, 51), slot,
                        "variant" + random.Next(2), random.Next(8) == 0));
                bins.Add(bin);
            }
            original = Layout(bins);
            result = StoragePlanner.Consolidate(bins);
            Preserved(result, bins, original);
            Check(Layout(result.After) == Layout(StoragePlanner.Consolidate(bins.AsEnumerable().Reverse()).After), "Random nondeterministic routing");
            if (result.Valid)
            {
                stablePlans++;
                Plan again = StoragePlanner.Consolidate(result.After);
                Check(again.Valid && Layout(again.After) == Layout(result.After), "Random consolidation not idempotent: " + scenario);
            }
            Bin pack = Chest("player", 8); pack.Player = true;
            for (int slot = 0; slot < 8; slot++) pack.Items.Add(Item("Item" + random.Next(4), random.Next(1, 51), slot, "variant0", slot < 2));
            bins.Add(pack); original = Layout(bins);
            result = StoragePlanner.Deposit(bins, new Dictionary<string, int> { { "Item0", 20 } });
            Preserved(result, bins, original);
            Check(result.Valid, "Valid deposit refused");
        }
        Check(stablePlans > 100, "Planner rejected almost every generated storage; safety checks must not hide missing functionality");
        System.Console.WriteLine("Storage planner: " + checks + " checks passed (2500 randomized inventories; " + stablePlans + " stable consolidation plans). No Unity/multiplayer execution implied.");
    }
}
