using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace MikkoMods.Storage
{
    public sealed class HistoryMove
    {
        public string Item, From, To;
        public int Count;
    }
    public sealed class HistoryRecord
    {
        public string Id, Mode, Status;
        public long World, Character;
        public DateTime Utc;
        public int ChangedChests;
        public List<HistoryMove> Moves = new List<HistoryMove>();

        private static string Pack(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        private static string Unpack(string value) { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)); }
        private static string Hash(string value)
        { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }

        public string Encode()
        {
            if (Mode != "deposit" && Mode != "consolidate" && Mode != "undo") throw new InvalidDataException("Unknown transaction mode.");
            if (ChangedChests < 0 || Moves.Any(m => m.Count <= 0 || m.Item == null || m.From == null || m.To == null))
                throw new InvalidDataException("Invalid history quantities or labels.");
            var rows = new List<string> { "PikalajitteluHistory/1", World.ToString(CultureInfo.InvariantCulture), Character.ToString(CultureInfo.InvariantCulture),
                Utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), Mode, ChangedChests.ToString(CultureInfo.InvariantCulture) };
            rows.AddRange(Moves.Select(m => m.Count.ToString(CultureInfo.InvariantCulture) + "\t" + Pack(m.Item) + "\t" + Pack(m.From) + "\t" + Pack(m.To)));
            string payload = String.Join("\n", rows.ToArray());
            return payload + "\nSHA256\t" + Hash(payload);
        }

        public static HistoryRecord Decode(string text)
        {
            if (text == null || text.Length > 2000000) throw new InvalidDataException("History entry too large.");
            int end = text.LastIndexOf("\nSHA256\t", StringComparison.Ordinal);
            if (end < 0 || Hash(text.Substring(0, end)) != text.Substring(end + 8)) throw new InvalidDataException("History checksum mismatch.");
            string[] rows = text.Substring(0, end).Split('\n');
            if (rows.Length < 6 || rows[0] != "PikalajitteluHistory/1") throw new InvalidDataException("Unknown history version.");
            var result = new HistoryRecord { World = Int64.Parse(rows[1], CultureInfo.InvariantCulture), Character = Int64.Parse(rows[2], CultureInfo.InvariantCulture),
                Utc = DateTime.ParseExact(rows[3], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Mode = rows[4],
                ChangedChests = Int32.Parse(rows[5], CultureInfo.InvariantCulture) };
            foreach (string row in rows.Skip(6))
            {
                string[] parts = row.Split('\t');
                if (parts.Length != 4) throw new InvalidDataException("Malformed history row.");
                result.Moves.Add(new HistoryMove { Count = Int32.Parse(parts[0], CultureInfo.InvariantCulture), Item = Unpack(parts[1]), From = Unpack(parts[2]), To = Unpack(parts[3]) });
            }
            result.Encode();
            return result;
        }

        public static List<HistoryRecord> ReadRecent(string root, long world, long character, out int unreadable)
        {
            unreadable = 0;
            var records = new List<HistoryRecord>();
            if (!Directory.Exists(root)) return records;
            foreach (string path in Directory.GetDirectories(root).OrderByDescending(p => p, StringComparer.Ordinal).Take(1000))
            {
                string file = Path.Combine(path, "history.txt");
                if (!File.Exists(file)) continue;
                try
                {
                    if (new FileInfo(file).Length > 2000000) throw new InvalidDataException("History too large.");
                    HistoryRecord record = Decode(File.ReadAllText(file, Encoding.UTF8));
                    if (record.World != world || record.Character != character) continue;
                    record.Id = Path.GetFileName(path);
                    var markers = new[] { "COMMITTED", "ROLLED_BACK", "ROLLBACK_INCOMPLETE" }.Where(m => File.Exists(Path.Combine(path, m))).ToArray();
                    record.Status = markers.Length == 1 ? markers[0] : "UNCONFIRMED";
                    records.Add(record);
                    if (records.Count == 100) break;
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is InvalidDataException) && !(error is ArgumentException) && !(error is FormatException) && !(error is OverflowException) && !(error is UnauthorizedAccessException)) throw;
                    unreadable++;
                }
            }
            return records;
        }
    }
}
