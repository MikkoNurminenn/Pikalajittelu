using System;
using System.Collections.Generic;
using System.Linq;

namespace MikkoMods.Storage
{
    // These records contain no Unity objects. Planning cannot mutate game state.
    public sealed class Stack
    {
        public string Template, Key, Prefab, Category;
        public int Count, Limit, Slot;
        public bool Fixed;
        public Stack Copy() { return (Stack)MemberwiseClone(); }
    }

    public sealed class ChestRule
    {
        public bool Locked;
        public string[] Prefabs = new string[0];
        public string[] Categories = new string[0];
        public bool Explicit { get { return Prefabs.Length + Categories.Length > 0; } }
        public bool Accepts(Stack item)
        {
            return !Locked && (!Explicit || Prefabs.Contains(item.Prefab, StringComparer.OrdinalIgnoreCase) ||
                Categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase));
        }
        public ChestRule Copy()
        {
            return new ChestRule { Locked = Locked, Prefabs = (string[])Prefabs.Clone(), Categories = (string[])Categories.Clone() };
        }
    }

    public sealed class Bin
    {
        public string Id;
        public int Capacity;
        public bool Player;
        public ChestRule Rule = new ChestRule();
        public List<Stack> Items = new List<Stack>();
        public Bin Copy()
        {
            return new Bin { Id = Id, Capacity = Capacity, Player = Player, Rule = Rule.Copy(), Items = Items.Select(s => s.Copy()).ToList() };
        }
    }

    public sealed class Movement
    {
        public string From, To, Prefab, Key;
        public int Count;
    }

    public sealed class Plan
    {
        public List<Bin> Before, After;
        public List<Movement> Moves = new List<Movement>();
        public bool Valid;
        public string Reason;
        public int Remaining;
        public int Moved { get { return Moves.Sum(m => m.Count); } }
    }

    public static class StoragePlanner
    {
        public static void Validate(IEnumerable<Bin> bins)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var identities = new Dictionary<string, Stack>(StringComparer.Ordinal);
            foreach (Bin bin in bins)
            {
                if (String.IsNullOrEmpty(bin.Id) || !ids.Add(bin.Id) || bin.Capacity < 1 || bin.Capacity > 65535 || bin.Rule == null)
                    throw new ArgumentException("Invalid or duplicate inventory identity/capacity.");
                var slots = new HashSet<int>();
                foreach (Stack item in bin.Items)
                {
                    if (item == null || String.IsNullOrEmpty(item.Template) || String.IsNullOrEmpty(item.Key) ||
                        String.IsNullOrEmpty(item.Prefab) || item.Category == null || item.Count < 1 || item.Limit < 1 ||
                        item.Count > item.Limit || item.Slot < 0 || item.Slot >= bin.Capacity || !slots.Add(item.Slot))
                        throw new ArgumentException("Invalid item, stack size or occupied slot in " + bin.Id);
                    Stack known;
                    if (identities.TryGetValue(item.Key, out known) &&
                        (known.Prefab != item.Prefab || known.Category != item.Category || known.Limit != item.Limit))
                        throw new ArgumentException("Item fingerprint collision.");
                    identities[item.Key] = item;
                }
            }
        }

        public static Dictionary<string, long> Totals(IEnumerable<Bin> bins)
        {
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (Stack item in bins.SelectMany(b => b.Items))
            {
                long count;
                counts.TryGetValue(item.Key, out count);
                counts[item.Key] = checked(count + item.Count);
            }
            return counts;
        }

        private static int Amount(Bin bin, string prefab)
        {
            return bin.Items.Where(s => s.Prefab == prefab).Sum(s => s.Count);
        }

        private static int CountFor(Dictionary<string, int> counts, string key)
        {
            int value;
            return counts.TryGetValue(key, out value) ? value : 0;
        }

        private static Dictionary<string, int> Counts(Bin bin, bool byPrefab)
        {
            return bin.Items.GroupBy(s => byPrefab ? s.Prefab : s.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Count), StringComparer.Ordinal);
        }

        private static int Put(Bin target, Stack source, int quantity)
        {
            int remaining = quantity;
            foreach (Stack stack in target.Items.Where(s => !s.Fixed && s.Key == source.Key).OrderBy(s => s.Slot))
            {
                int count = Math.Min(remaining, stack.Limit - stack.Count);
                stack.Count += count;
                remaining -= count;
                if (remaining == 0) break;
            }
            var occupied = new HashSet<int>(target.Items.Select(s => s.Slot));
            for (int slot = 0; remaining > 0 && slot < target.Capacity; slot++)
            {
                if (occupied.Contains(slot)) continue;
                Stack added = source.Copy();
                added.Count = Math.Min(source.Limit, remaining);
                added.Slot = slot;
                added.Fixed = false;
                target.Items.Add(added);
                remaining -= added.Count;
            }
            return quantity - remaining;
        }

        private static Plan Start(IEnumerable<Bin> input)
        {
            var bins = input.ToList();
            Validate(bins);
            return new Plan { Before = bins.Select(b => b.Copy()).ToList(), After = bins.Select(b => b.Copy()).ToList() };
        }

        public static Plan Deposit(IEnumerable<Bin> input, IDictionary<string, int> keep)
        {
            Plan plan = Start(input);
            if (plan.After.Count(b => b.Player) != 1) throw new ArgumentException("Deposit requires one player inventory.");
            if (keep != null && keep.Any(k => k.Value < 0)) throw new ArgumentException("Keep quantities cannot be negative.");
            Bin source = plan.After.Single(b => b.Player);
            var retained = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in source.Items.GroupBy(s => s.Prefab, StringComparer.OrdinalIgnoreCase))
            {
                int desired = 0;
                if (keep != null) foreach (var entry in keep)
                    if (String.Equals(entry.Key, group.Key, StringComparison.OrdinalIgnoreCase)) desired = Math.Max(desired, entry.Value);
                retained[group.Key] = Math.Max(0, desired - group.Where(s => s.Fixed).Sum(s => s.Count));
            }
            foreach (Stack item in source.Items.Where(s => !s.Fixed).OrderBy(s => s.Slot).ToArray())
            {
                int retain = Math.Min(item.Count, retained[item.Prefab]);
                retained[item.Prefab] -= retain;
                int available = item.Count - retain;
                Bin[] targets = plan.After.Where(b => !b.Player && b.Rule.Accepts(item))
                    .OrderByDescending(b => b.Rule.Explicit)
                    .ThenByDescending(b => Amount(b, item.Prefab))
                    .ThenBy(b => b.Items.Count == 0 ? 0 : 1)
                    .ThenBy(b => b.Id, StringComparer.Ordinal).ToArray();
                foreach (Bin target in targets)
                {
                    int moved = Put(target, item, available);
                    item.Count -= moved;
                    available -= moved;
                    if (available == 0) break;
                }
                if (item.Count == 0) source.Items.Remove(item);
                plan.Remaining += available;
            }
            return Finish(plan);
        }

        public static Plan Consolidate(IEnumerable<Bin> input)
        {
            Plan original = Start(input);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<Bin> current = original.After;
            // Different metadata variants compete for the same prefab's preferred
            // chest. Resolve that interaction before showing a preview, never by
            // moving live items repeatedly. Reject cycles or non-convergence.
            for (int pass = 0; pass < 32; pass++)
            {
                string before = LayoutKey(current);
                if (!seen.Add(before)) break;
                Plan candidate = ConsolidateOnce(current);
                if (!candidate.Valid) { original.Reason = candidate.Reason; return original; }
                if (before == LayoutKey(candidate.After))
                {
                    original.After = candidate.After;
                    return Finish(original);
                }
                current = candidate.After;
            }
            original.Reason = "unstable-layout";
            return original;
        }

        private static string LayoutKey(IEnumerable<Bin> bins)
        {
            // Length prefixes avoid ambiguity in arbitrary modded item identifiers.
            var value = new System.Text.StringBuilder();
            foreach (Bin bin in bins.OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                value.Append(bin.Id.Length).Append(':').Append(bin.Id).Append('|');
                foreach (Stack stack in bin.Items.OrderBy(s => s.Slot))
                    value.Append(stack.Key.Length).Append(':').Append(stack.Key).Append('/')
                        .Append(stack.Slot).Append('/').Append(stack.Count).Append('/').Append(stack.Fixed).Append(';');
            }
            return value.ToString();
        }

        private static Plan ConsolidateOnce(IEnumerable<Bin> input)
        {
            Plan plan = Start(input);
            if (plan.Before.Any(b => b.Player)) throw new ArgumentException("Consolidation only accepts chests.");
            var preferredAmounts = plan.Before.ToDictionary(b => b.Id, b => Counts(b, true), StringComparer.Ordinal);
            // Build the final layout in scratch space. Full chests can exchange their
            // contents without dropping items or needing a physical temporary chest.
            var pools = plan.Before.Where(b => !b.Rule.Locked).SelectMany(b => b.Items)
                .Where(s => !s.Fixed).GroupBy(s => s.Key, StringComparer.Ordinal)
                .OrderBy(g => plan.After.Count(b => b.Rule.Accepts(g.First())))
                .ThenByDescending(g => g.Sum(s => (long)s.Count))
                .ThenBy(g => g.First().Prefab, StringComparer.Ordinal)
                .ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
            foreach (Bin bin in plan.After.Where(b => !b.Rule.Locked)) bin.Items.RemoveAll(s => !s.Fixed);
            foreach (var pool in pools)
            {
                Stack template = pool.OrderBy(s => s.Template, StringComparer.Ordinal).First();
                long remaining = pool.Sum(s => (long)s.Count);
                Bin[] targets = plan.After.Where(b => b.Rule.Accepts(template))
                    .OrderByDescending(b => b.Rule.Explicit)
                    .ThenByDescending(b => CountFor(preferredAmounts[b.Id], template.Prefab))
                    .ThenBy(b => b.Id, StringComparer.Ordinal).ToArray();
                foreach (Bin target in targets)
                {
                    int chunk = (int)Math.Min(Int32.MaxValue, remaining);
                    remaining -= Put(target, template, chunk);
                    if (remaining == 0) break;
                }
                if (remaining > 0)
                {
                    plan.After = plan.Before.Select(b => b.Copy()).ToList();
                    plan.Reason = "rules-or-capacity";
                    return plan; // No partial plan is ever offered for applying.
                }
            }
            return Finish(plan);
        }

        public static Plan Reverse(Plan committed)
        {
            if (committed == null || !committed.Valid) throw new ArgumentException("Only a validated committed plan can be reversed.");
            Plan reverse = Start(committed.After);
            reverse.After = committed.Before.Select(b => b.Copy()).ToList();
            return Finish(reverse);
        }

        private static Plan Finish(Plan plan)
        {
            Validate(plan.After);
            var totals = Totals(plan.Before);
            var afterById = plan.After.ToDictionary(b => b.Id, StringComparer.Ordinal);
            if (!RecoveryRules.CountsEqual(totals, Totals(plan.After)))
                throw new InvalidOperationException("Conservation failed. Plan rejected.");
            foreach (Bin before in plan.Before)
            {
                Bin after = afterById[before.Id];
                foreach (Stack pinned in before.Items.Where(s => s.Fixed || before.Rule.Locked))
                    if (!after.Items.Any(s => s.Template == pinned.Template && s.Key == pinned.Key && s.Count == pinned.Count && s.Slot == pinned.Slot))
                        throw new InvalidOperationException("Protected item changed. Plan rejected.");
            }
            // Net transfers are what the preview shows. Packing within a chest is
            // not incorrectly counted as repeatedly moving the same items.
            var beforeCounts = plan.Before.ToDictionary(b => b.Id, b => Counts(b, false), StringComparer.Ordinal);
            var afterCounts = plan.After.ToDictionary(b => b.Id, b => Counts(b, false), StringComparer.Ordinal);
            var prefabs = plan.Before.SelectMany(b => b.Items).GroupBy(s => s.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Prefab, StringComparer.Ordinal);
            var orderedBefore = plan.Before.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
            foreach (string key in totals.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var deltas = orderedBefore.Select(b => new {
                    Id = b.Id,
                    Count = CountFor(beforeCounts[b.Id], key) - CountFor(afterCounts[b.Id], key)
                }).ToArray();
                var incoming = deltas.Where(d => d.Count < 0).ToDictionary(d => d.Id, d => -d.Count);
                foreach (var from in deltas.Where(d => d.Count > 0))
                {
                    int available = from.Count;
                    foreach (string to in incoming.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray())
                    {
                        int count = Math.Min(available, incoming[to]);
                        if (count == 0) continue;
                        plan.Moves.Add(new Movement { From = from.Id, To = to, Key = key, Count = count,
                            Prefab = prefabs[key] });
                        incoming[to] -= count;
                        available -= count;
                    }
                }
            }
            plan.Valid = true;
            plan.Reason = plan.Remaining > 0 ? "some-items-did-not-fit" : "ready";
            return plan;
        }
    }
}
