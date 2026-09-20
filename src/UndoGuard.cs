using System;
using System.Collections.Generic;
using System.Linq;

namespace MikkoMods.Storage
{
    public static class UndoGuard
    {
        // A missing chest, changed inventory or different character/world rejects
        // the complete reversal. Extra nearby chests are irrelevant to this record.
        public static bool Matches(long expectedWorld, long expectedCharacter, long world, long character,
            IDictionary<string, byte[]> expected, IDictionary<string, byte[]> actual)
        {
            if (expectedWorld != world || expectedCharacter != character || expected == null || actual == null || expected.Count == 0) return false;
            foreach (var entry in expected)
            {
                byte[] value;
                if (entry.Value == null || !actual.TryGetValue(entry.Key, out value) || value == null || !entry.Value.SequenceEqual(value)) return false;
            }
            return true;
        }
    }
}
