using System;
using System.Collections.Generic;
using MikkoMods.Storage;

public static class TransactionTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private sealed class Fake
    {
        public int Memory, Disk, Before, After;
        public bool Owned = true, FailApply, FailRestore, FailFlush, FailAfter;
        public TransactionParticipant Participant(string id)
        {
            return new TransactionParticipant {
                Id = id,
                RequireBefore = () => { if (!Owned || Memory != Before || Disk != Before) throw new Exception("stale before"); },
                RequireAfter = () => { if (!Owned || FailAfter || Memory != After || Disk != After) throw new Exception("after mismatch"); },
                Apply = () => { if (!Owned) throw new Exception("owner lost"); Memory = After; if (FailApply) throw new Exception("apply"); },
                Restore = () => { if (!Owned || FailRestore) throw new Exception("restore"); Memory = Before; },
                Flush = () => { if (!Owned || FailFlush) throw new Exception("flush"); Disk = Memory; }
            };
        }
    }
    public static void Main()
    {
        for (int fault = 0; fault < 9; fault++)
        {
            var source = new Fake { Memory = 28, Disk = 28, Before = 28, After = 0 };
            var target = new Fake { Memory = 2, Disk = 2, Before = 2, After = 30 };
            var parts = new List<TransactionParticipant> { source.Participant("source"), target.Participant("target") };
            bool prepared = false; string status = "";
            switch (fault)
            {
                case 1: target.FailApply = true; break;
                case 2: target.FailAfter = true; break;
                case 3: target.FailApply = true; source.FailRestore = true; break;
                case 4: target.FailFlush = true; break;
                case 5: target.Memory = 3; break;
            }
            bool threw = false;
            TransactionResult result = null;
            try
            {
                result = TransactionCoordinator.Run(parts, () => {
                    if (fault == 6) throw new Exception("disk full");
                    prepared = true;
                    if (fault == 7) target.Owned = false;
                }, s => { if (fault == 8) throw new Exception("status disk full"); status = s; });
            }
            catch { threw = true; }
            if (fault == 0 || fault == 8)
            {
                Check(!threw && prepared && result.Committed && source.Memory == 0 && target.Memory == 30 && source.Disk + target.Disk == 30, "Successful transfer failed");
                if (fault == 8) Check(result.JournalError != null, "Status write failure must not rollback completed transfer");
            }
            else if (fault == 5 || fault == 6 || fault == 7)
                Check(threw && source.Memory == 28 && source.Disk == 28 && target.Disk == 2, "Preflight failure mutated inventories");
            else if (fault == 1 || fault == 2)
                Check(!threw && !result.Committed && !result.RollbackIncomplete && source.Memory == 28 && target.Memory == 2 &&
                    source.Disk == 28 && target.Disk == 2 && status == "ROLLED_BACK", "Failure not completely rolled back");
            else Check(!threw && !result.Committed && result.RollbackIncomplete && status == "ROLLBACK_INCOMPLETE", "Incomplete rollback reported as safe");
        }
        // Callbacks must observe the final total, never a transient loss/duplication.
        var a = new Fake { Memory = 28, Disk = 28, Before = 28, After = 0 };
        var b = new Fake { Memory = 2, Disk = 2, Before = 2, After = 30 };
        var pa = a.Participant("a"); var pb = b.Participant("b");
        Action flushA = pa.Flush;
        pa.Flush = () => { Check(a.Memory + b.Memory == 30, "Callback observed transient item total"); flushA(); };
        Check(TransactionCoordinator.Run(new[] { pa, pb }, () => {}, s => {}).Committed, "Batch callback test failed");
        System.Console.WriteLine("Transaction coordinator: " + checks + " checks passed, including disk errors, stale state, ownership loss and rollback failures.");
    }
}
