using System;
using System.Collections.Generic;

namespace MikkoMods.Storage
{
    public sealed class TransactionParticipant
    {
        public string Id;
        public Action RequireBefore, RequireAfter, Apply, Restore, Flush;
    }

    public sealed class TransactionResult
    {
        public bool Committed, RollbackIncomplete;
        public Exception Error, JournalError;
    }

    public static class TransactionCoordinator
    {
        // Synchronous by contract. All inventory lists change before callbacks run.
        // A durable PREPARED journal must exist before the first mutation.
        public static TransactionResult Run(IList<TransactionParticipant> participants, Action prepareJournal, Action<string> status)
        {
            if (participants == null || participants.Count == 0) throw new ArgumentException("No transaction participants.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransactionParticipant p in participants)
            {
                if (p == null || String.IsNullOrEmpty(p.Id) || !ids.Add(p.Id) || p.RequireBefore == null ||
                    p.RequireAfter == null || p.Apply == null || p.Restore == null || p.Flush == null)
                    throw new ArgumentException("Invalid transaction participant.");
                p.RequireBefore();
            }
            if (prepareJournal == null || status == null) throw new ArgumentNullException("journal");
            prepareJournal(); // An IO error here means no inventory was touched.
            foreach (TransactionParticipant p in participants) p.RequireBefore();
            var result = new TransactionResult();
            try
            {
                foreach (TransactionParticipant p in participants) p.Apply();
                foreach (TransactionParticipant p in participants) p.Flush();
                foreach (TransactionParticipant p in participants) p.RequireAfter();
                result.Committed = true;
            }
            catch (Exception error)
            {
                result.Error = error;
                // Attempt every participant even if one restore fails. Never report
                // a successful rollback until both memory and persisted state match.
                foreach (TransactionParticipant p in participants)
                    try { p.Restore(); } catch { result.RollbackIncomplete = true; }
                foreach (TransactionParticipant p in participants)
                    try { p.Flush(); } catch { result.RollbackIncomplete = true; }
                foreach (TransactionParticipant p in participants)
                    try { p.RequireBefore(); } catch { result.RollbackIncomplete = true; }
            }
            try { status(result.Committed ? "COMMITTED" : result.RollbackIncomplete ? "ROLLBACK_INCOMPLETE" : "ROLLED_BACK"); }
            catch (Exception error) { result.JournalError = error; }
            return result;
        }
    }
}
