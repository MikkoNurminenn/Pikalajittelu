using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MikkoMods;
using MikkoMods.Storage;

// Repeatable measurements, not hardware-dependent pass/fail timing thresholds.
// Real Unity capture, ownership and disk flush costs are measured separately.
public static class StoragePerformance
{
    public static void Main()
    {
        StoragePlanner.Consolidate(Warehouse(2)); // JIT warmup
        Console.WriteLine("chests,stacks,valid,plan_ms,replan_ms,moves,reason,layout_sha256");
        foreach (int count in new[] { 20, 80, 160 })
        {
            var input = Warehouse(count);
            string before = Layout(input);
            var timer = Stopwatch.StartNew();
            Plan plan = StoragePlanner.Consolidate(input);
            long planMs = timer.ElapsedMilliseconds;
            if (before != Layout(input)) throw new Exception("Planner mutated benchmark input");
            if (!RecoveryRules.CountsEqual(StoragePlanner.Totals(input), StoragePlanner.Totals(plan.After))) throw new Exception("Benchmark conservation failure");
            long replanMs = -1;
            if (plan.Valid)
            {
                timer.Restart(); Plan again = StoragePlanner.Consolidate(plan.After); replanMs = timer.ElapsedMilliseconds;
                if (!again.Valid || Layout(plan.After) != Layout(again.After)) throw new Exception("Benchmark plan is not stable");
            }
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Layout(plan.After)))).Replace("-", "");
            Console.WriteLine(count + "," + input.Sum(b => b.Items.Count) + "," + plan.Valid + "," + planMs + "," + replanMs + "," + plan.Moves.Count + "," + plan.Reason + "," + hash);
        }
    }

    private static List<Bin> Warehouse(int count)
    {
        var random = new Random(24092026);
        var result = new List<Bin>();
        for (int chest = 0; chest < count; chest++)
        {
            var bin = new Bin { Id = "chest:" + chest.ToString("D5"), Capacity = 32, Rule = new ChestRule { Locked = chest % 17 == 0 } };
            for (int slot = 0; slot < 24; slot++)
            {
                int item = random.Next(60);
                bin.Items.Add(new Stack { Template = bin.Id + ":" + slot, Prefab = "material" + (item / 2), Category = "Material",
                    Key = "item" + item + ":" + new String((char)('a' + item % 26), 300), Limit = 50,
                    Count = random.Next(1, 51), Slot = slot, Fixed = slot == 23 });
            }
            result.Add(bin);
        }
        return result;
    }
    private static string Layout(IEnumerable<Bin> bins)
    {
        return String.Join("|", bins.OrderBy(b => b.Id).Select(b => b.Id + "=" + String.Join(";", b.Items.OrderBy(s => s.Slot)
            .Select(s => s.Key + ":" + s.Count + ":" + s.Slot + ":" + s.Fixed).ToArray())).ToArray());
    }
}
