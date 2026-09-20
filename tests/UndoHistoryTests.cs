using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikkoMods.Storage;

public static class UndoHistoryTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static void Main()
    {
        var expected = new Dictionary<string, byte[]> { { "player", new byte[] { 1, 2 } }, { "chest", new byte[] { 3, 4 } } };
        var actual = expected.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone());
        Check(UndoGuard.Matches(10, 20, 10, 20, expected, actual), "Unchanged transfer not undoable");
        Check(!UndoGuard.Matches(10, 20, 11, 20, expected, actual), "Wrong world accepted");
        Check(!UndoGuard.Matches(10, 20, 10, 21, expected, actual), "Wrong character accepted");
        actual["chest"][1] = 5;
        Check(!UndoGuard.Matches(10, 20, 10, 20, expected, actual), "Changed chest accepted");
        actual.Remove("chest");
        Check(!UndoGuard.Matches(10, 20, 10, 20, expected, actual), "Missing chest accepted");
        actual["chest"] = new byte[] { 3, 4 }; actual["extra"] = new byte[] { 7 };
        Check(UndoGuard.Matches(10, 20, 10, 20, expected, actual), "Extra unrelated chest blocked undo");
        actual["player"] = new byte[] { 1, 2, 3 };
        Check(!UndoGuard.Matches(10, 20, 10, 20, expected, actual), "Player inventory change accepted");

        var player = new Bin { Id = "player", Capacity = 4, Player = true, Items = new List<Stack> {
            new Stack { Template = "p:1", Key = "Tin:metadata", Prefab = "Tin", Category = "Material", Count = 28, Limit = 30, Slot = 1 },
            new Stack { Template = "p:0", Key = "Sword:crafter", Prefab = "Sword", Category = "OneHandedWeapon", Count = 1, Limit = 1, Slot = 0, Fixed = true }
        } };
        Plan deposit = StoragePlanner.Deposit(new[] { player, new Bin { Id = "chest", Capacity = 2 } }, null);
        Plan reverse = StoragePlanner.Reverse(deposit);
        Check(reverse.Valid && reverse.Moved == 28 && reverse.Moves.Single().From == "chest" && reverse.Moves.Single().To == "player", "Reverse route incorrect");
        Check(reverse.After.Single(bin => bin.Player).Items.Single(s => s.Prefab == "Tin").Count == 28, "Undo did not restore 28 tin");
        Check(reverse.After.Single(bin => bin.Player).Items.Single(s => s.Prefab == "Sword").Fixed, "Protected equipment not preserved");
        Check(deposit.After.Single(bin => bin.Player).Items.Count == 1, "Reverse mutated committed plan");

        var record = new HistoryRecord { World = 10, Character = 20, Utc = new DateTime(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc),
            Mode = "deposit", ChangedChests = 1, Moves = new List<HistoryMove> { new HistoryMove { Count = 28, Item = "Tina\nlaatu 1", From = "Reppu", To = "Mökin arkku" } } };
        string encoded = record.Encode();
        HistoryRecord restored = HistoryRecord.Decode(encoded);
        Check(restored.Moves.Single().Item == "Tina\nlaatu 1" && restored.Moves.Single().To == "Mökin arkku", "Unicode or delimiters lost");
        Check(restored.Encode() == encoded, "History roundtrip unstable");
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history-" + Guid.NewGuid().ToString("N"));
        string a = Path.Combine(root, "20260920-110000-a"), b = Path.Combine(root, "20260920-110001-b"), c = Path.Combine(root, "20260920-110002-c");
        foreach (string folder in new[] { a, b, c }) Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(a, "history.txt"), encoded); File.WriteAllText(Path.Combine(a, "COMMITTED"), "");
        File.WriteAllText(Path.Combine(b, "history.txt"), encoded); File.WriteAllText(Path.Combine(b, "PREPARED"), "");
        File.WriteAllText(Path.Combine(c, "history.txt"), "broken");
        int unreadable;
        var records = HistoryRecord.ReadRecent(root, 10, 20, out unreadable);
        Check(records.Count == 2 && unreadable == 1, "Damaged history should be counted and skipped, never abort all history");
        Check(records[0].Status == "UNCONFIRMED" && records[1].Status == "COMMITTED", "Unconfirmed operation presented as complete");
        File.WriteAllText(Path.Combine(a, "ROLLED_BACK"), "");
        records = HistoryRecord.ReadRecent(root, 10, 20, out unreadable);
        Check(records.Single(r => r.Id.EndsWith("-a")).Status == "UNCONFIRMED", "Conflicting status markers trusted");
        Check(HistoryRecord.ReadRecent(root, 11, 20, out unreadable).Count == 0, "Other world's history leaked");
        Check(HistoryRecord.ReadRecent(root, 10, 21, out unreadable).Count == 0, "Other character's history leaked");
        Check(File.ReadAllText(Path.Combine(c, "history.txt")) == "broken", "Reading history modified evidence");
        System.Console.WriteLine("Undo/history: " + checks + " checks passed (28 tin reversal, identity/state guards and corrupted/incomplete journals).");
    }
}
