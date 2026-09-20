using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using MikkoMods;
using MikkoMods.Storage;
using UnityEngine;

public sealed partial class ValheimInventoryHarness
{
    private IEnumerator LargeStorageTest(Pikalajittelu plugin, Player player, string worldName)
    {
        File.WriteAllText(ResultPath, "LARGE_STORAGE_TESTING\nworld=" + worldName);
        string[] prefabs = { "Wood", "Stone", "Resin", "Flint", "GreydwarfEye", "LeatherScraps", "DeerHide", "Coal", "Amber", "Tin" };
        var random = new System.Random(94026);
        for (int index = 0; index < 80; index++)
        {
            Container chest = SpawnChest(player, new Vector3((index % 10 - 4.5f) * 0.8f, 1 + index / 40, (index / 10 % 4 - 1.5f) * 1.2f));
            Inventory inventory = chest.GetInventory(); inventory.GetAllItems().Clear();
            for (int slot = 0; slot < 8; slot++)
            {
                var item = Item(prefabs[random.Next(prefabs.Length)], 1, slot % inventory.GetWidth(), slot / inventory.GetWidth());
                item.m_stack = random.Next(1, item.m_shared.m_maxStackSize + 1);
                inventory.GetAllItems().Add(item);
            }
            NotifyInventory(inventory);
        }
        Container[] chests = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
            .Where(c => (bool)Call(plugin, "Eligible", c, player, false)).ToArray();
        Check(chests.Length == 84, "Large test did not discover all 84 fixture chests");
        var original = chests.ToDictionary(c => c, c => Save(c.GetInventory()));
        var totals = RealTotals(chests);
        byte[] packBefore = Save(player.GetInventory());
        var timer = Stopwatch.StartNew();
        yield return Sort(plugin, player, true, false, false);
        long previewMs = timer.ElapsedMilliseconds;
        Check((bool)Get(plugin, "previewVisible"), "Large storage did not produce a preview");
        var session = Get(plugin, "pendingStorage");
        var plan = (Plan)Get(session, "Plan");
        Check(plan.Valid && plan.Moves.Count > 60, "Large fixture did not exercise preview pagination");
        Check(chests.All(c => Save(c.GetInventory()).SequenceEqual(original[c])), "Large preview changed items");
        if (Environment.GetCommandLineArgs().Contains("-storage-ui-test"))
        {
            yield return CaptureScreen(plugin, worldName, "large-storage-first-page");
            Set(plugin, "previewPage", (plan.Moves.Count - 1) / 60);
            yield return CaptureScreen(plugin, worldName, "large-storage-last-page");
        }
        Call(plugin, "SetPreviewClosed", false);
        timer.Restart();
        yield return Sort(plugin, player, true, true, false);
        long commitMs = timer.ElapsedMilliseconds;
        Check(!(bool)Get(plugin, "storageFault") && Get(plugin, "lastCommitted") != null && !(bool)Get(plugin, "previewVisible"), "Large storage commit failed");
        Check(RecoveryRules.CountsEqual(totals, RealTotals(chests)), "Large storage changed real item totals/metadata");
        Check(Save(player.GetInventory()).SequenceEqual(packBefore), "Large consolidation changed player inventory");
        foreach (Container chest in chests) VerifyNetwork(chest);
        yield return Sort(plugin, player, true, false, false);
        var settled = (Plan)Get(Get(plugin, "pendingStorage"), "Plan");
        Check(settled.Valid && settled.Moves.Count == 0 && settled.Moved == 0, "Committed large warehouse is not stable");
        Call(plugin, "SetPreviewClosed", true);
        yield return Sort(plugin, player, true, false, true);
        Check((bool)Get(plugin, "previewVisible"), "Large undo preview missing");
        Call(plugin, "SetPreviewClosed", false);
        timer.Restart();
        yield return Sort(plugin, player, true, true, true);
        long undoMs = timer.ElapsedMilliseconds;
        Check(!(bool)Get(plugin, "storageFault") && Get(plugin, "lastCommitted") == null, "Large undo failed");
        Check(chests.All(c => Save(c.GetInventory()).SequenceEqual(original[c])), "Large undo did not restore exact inventory bytes");
        foreach (Container chest in chests) VerifyNetwork(chest);
        string report = "chests\tstacks\ttransfers\tpreview_ms\tcommit_ms\tundo_ms\n" + chests.Length + "\t" +
            chests.Sum(c => c.GetInventory().GetAllItems().Count) + "\t" + plan.Moves.Count + "\t" + previewMs + "\t" + commitMs + "\t" + undoMs + "\n";
        File.WriteAllText(Path.Combine(Paths.GameRootPath, "large-storage-performance.tsv"), report);
        Logger.LogInfo("Large actual-world storage PASS: " + report);
    }
    private static Dictionary<string, long> RealTotals(IEnumerable<Container> chests)
    {
        return chests.SelectMany(c => c.GetInventory().GetAllItems()).GroupBy(Fingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.m_stack), StringComparer.Ordinal);
    }
}
