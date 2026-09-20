using System;
using System.Collections.Generic;

namespace MikkoMods
{
    public static class RecoveryRules
    {
        public static bool OwnerMatches(string actual, string requested)
        {
            return !String.IsNullOrWhiteSpace(requested) && !String.IsNullOrWhiteSpace(actual) &&
                String.Equals(actual, requested.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool CountsEqual(Dictionary<string, long> before, Dictionary<string, long> after)
        {
            if (before.Count != after.Count) return false;
            foreach (var entry in before)
            {
                long value;
                if (!after.TryGetValue(entry.Key, out value) || value != entry.Value) return false;
            }
            return true;
        }
    }
}
