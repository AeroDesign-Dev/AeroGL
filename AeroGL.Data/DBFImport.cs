using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using NDbfReader;                 // NuGet: NDbfReader
using AeroGL.Core;

namespace AeroGL.Data
{
    public static class DbfImporter
    {
        /// <summary>
        /// Jalankan sekali untuk import awal. Akan membuat file marker "import.ok" agar tidak double-import.
        /// </summary>
        public static async Task RunOnce(string folderPath)
        {
            var marker = Path.ChangeExtension(CurrentCompany.Data.DbPath, ".importok");
            if (File.Exists(marker)) return;

            // 1. DETEKSI TAHUN: Ambil satu sampel tanggal dari GLTRAN1 buat nentuin 'Reference Year'
            int targetYear = await DetectYear(Path.Combine(folderPath, "GLTRAN1.DBF"));

            // 2. IMPORT: Gunakan targetYear yang sudah dinormalisasi ke 20xx
            await ImportGLMAS(Path.Combine(folderPath, "GLMAS.DBF"), targetYear);
            await ImportGLTRAN1(Path.Combine(folderPath, "GLTRAN1.DBF"));
            await ImportGLTRAN2(Path.Combine(folderPath, "GLTRAN2.DBF"));

            File.WriteAllText(marker, DateTime.Now.ToString("s"));
        }

        private static async Task<int> DetectYear(string path)
        {
            if (!File.Exists(path)) return DateTime.Today.Year;
            try
            {
                using (var table = Table.Open(path))
                {
                    var reader = table.OpenReader();
                    if (reader.Read())
                    {
                        string iso = ToIsoDate(reader, "TANGGAL");
                        return DateTime.Parse(iso).Year;
                    }
                }
            }
            catch { }
            return DateTime.Today.Year;
        }

        // ==========================================================
        // GLMAS.DBF -> Coa + CoaBalance (Month 0..12), Year = 0 (placeholder)
        // Field asumsi: NOREK (C), NMREK (C), TPREK (C 'D'/'K' atau N 0/1), GRREK (N 1..5),
        //               SALDO00..12 (N), DEBET00..12 (N), KREDIT00..12 (N)
        // ==========================================================
        public static async Task ImportGLMAS(string path, int targetYear)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            using (var table = Table.Open(path))
            {
                var reader = table.OpenReader();   // Reader di paketmu bukan IDisposable → jangan pakai using
                while (reader.Read())
                {
                    string code3 = CleanStr(reader, "NOREK");
                    string name = CleanStr(reader, "NMREK");
                    int tprek = ParseType(reader, "TPREK"); // 0 (Debit) / 1 (Kredit)
                    int grrek = ParseInt(reader, "GRREK");

                    const string upCoa = @"
INSERT INTO Coa(Code3,Name,Type,Grp) VALUES(@Code3,@Name,@Type,@Grp)
ON CONFLICT(Code3) DO UPDATE SET Name=@Name, Type=@Type, Grp=@Grp;";
                    await cn.ExecuteAsync(upCoa, new { Code3 = code3, Name = name, Type = tprek, Grp = grrek }, tx);

                    // 13 ember: 0..12
                    for (int m = 0; m <= 12; m++)
                    {
                        string ms = m.ToString("00");
                        decimal saldo = ParseDecimal(reader, "SALDO" + ms);
                        decimal debet = ParseDecimal(reader, "DEBET" + ms);
                        decimal kredit = ParseDecimal(reader, "KREDIT" + ms);

                        const string upBal = @"
INSERT INTO CoaBalance(Code3,Year,Month,Saldo,Debet,Kredit)
VALUES(@c, @y, @m, @sa, @de, @kr)
ON CONFLICT(Code3,Year,Month) DO UPDATE SET
  Saldo=@sa, Debet=@de, Kredit=@kr;";


                        await cn.ExecuteAsync(upBal, new { c = code3, y = targetYear, m = m, sa = saldo, de = debet, kr = kredit }, tx);
                    }
                }
                tx.Commit();
            }
        }

        // ==========================================================
        // GLTRAN1.DBF -> JournalHeader
        // Field asumsi: NOTRAN (C), TANGGAL (Date / C "yyyyMMdd"), MEMO (C)
        // Type J/M: dataset lama kemungkinan tidak punya → default 'J'
        // ==========================================================
        public static async Task ImportGLTRAN1(string path)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            using (var table = Table.Open(path))
            {
                var reader = table.OpenReader();
                while (reader.Read())
                {
                    string no = CleanStr(reader, "NOTRAN");
                    string memo = TryStr(reader, "MEMO", true);
                    string iso = ToIsoDate(reader, "TANGGAL");

                    const string upH = @"
INSERT INTO JournalHeader(NoTran,Tanggal,Memo,Type)
VALUES(@no,@tgl,@memo,'J')
ON CONFLICT(NoTran) DO UPDATE SET Tanggal=@tgl, Memo=@memo;";
                    await cn.ExecuteAsync(upH, new { no = no, tgl = iso, memo = memo }, tx);
                }
                tx.Commit();
            }
        }

        // ==========================================================
        // GLTRAN2.DBF -> JournalLine
        // Field asumsi: NOTRAN (C), NOREK (C 'xxx.xxx' 2-seg), TPTRAN (C 'D'/'K'),
        //               JUMLAH (N), NARASI (C)
        // Trigger DB akan otomatis update TotalDebet/TotalKredit di header.
        // ==========================================================
        // ========== GLTRAN2.DBF -> JournalLine ==========
        public static async Task ImportGLTRAN2(string path)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            using (var table = Table.Open(path))
            {
                var reader = table.OpenReader();
                int batch = 0;

                const string sql = @"
INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration)
VALUES(@no,@c2,@s,@a,@nar);";

                while (reader.Read())
                {
                    string no = CleanStr(reader, "NOTRAN");
                    string c2 = CleanStr(reader, "NOREK");            // 2-seg
                    string side = CleanStr(reader, "TPTRAN").ToUpper(); // 'D'/'K'
                    decimal amt = ParseDecimal(reader, "JUMLAH");

                    // === Perbaikan utama: gabungkan KETERNG1..3 ===
                    string nar = CombineNarration(reader);             // <-- baru

                    await cn.ExecuteAsync(sql, new { no = no, c2 = c2, s = side, a = amt, nar = nar }, tx);

                    if (++batch % 10000 == 0) tx.Commit();
                }
                tx.Commit();
            }
        }

        // ========================= Helpers =========================

        // Gabungkan KETERNG1..3 jadi satu string dengan newline.
        // Kalau KETERNG* kosong semua, fallback ke NARASI (jika ada).
        private static string CombineNarration(NDbfReader.Reader r)
        {
            string k1 = TryStr(r, "KETERNG1", true);
            string k2 = TryStr(r, "KETERNG2", true);
            string k3 = TryStr(r, "KETERNG3", true);

            // Trim tiap baris & buang yang kosong
            string[] lines = new[] { k1, k2, k3 };
            var nonEmpty = new System.Collections.Generic.List<string>();
            foreach (var s in lines)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    nonEmpty.Add(s.Trim());
            }

            if (nonEmpty.Count > 0)
            {
                // Normalisasi newline ke \n saja; SQLite TEXT aman
                return string.Join(" ", nonEmpty);
            }

            // Fallback: beberapa dataset lama juga pakai NARASI tunggal
            string nar = TryStr(r, "NARASI", true);
            return string.IsNullOrWhiteSpace(nar) ? null : nar.Trim();
        }


        // ========================= Helpers =========================
        private static object GetRaw(NDbfReader.Reader r, string name)
        {
            try { return r.GetValue(name); }
            catch { return null; }
        }

        private static string CleanStr(NDbfReader.Reader r, string name)
        {
            var obj = GetRaw(r, name);
            if (obj == null || obj is DBNull) return string.Empty;
            return obj.ToString().Trim();
        }

        private static string TryStr(NDbfReader.Reader r, string name, bool allowNull)
        {
            var obj = GetRaw(r, name);
            if (obj == null || obj is DBNull) return allowNull ? null : string.Empty;
            return obj.ToString().Trim();
        }

        private static int ParseInt(NDbfReader.Reader r, string name)
        {
            var obj = GetRaw(r, name);
            if (obj == null || obj is DBNull) return 0;

            if (obj is int i) return i;
            if (obj is long l) return (int)l;
            if (obj is decimal d) return (int)d;

            int v;
            if (int.TryParse(obj.ToString(), out v)) return v;
            return 0;
        }

        private static int ParseType(NDbfReader.Reader r, string name)
        {
            // TPREK bisa 'D'/'K' atau 0/1 → normalize ke 0 (Debit), 1 (Kredit)
            var s = CleanStr(r, name).ToUpperInvariant();
            if (s == "D") return 0;
            if (s == "K") return 1;
            int n;
            if (int.TryParse(s, out n)) return n == 0 ? 0 : 1;
            return 0;
        }

        private static decimal ParseDecimal(NDbfReader.Reader r, string name)
        {
            var obj = GetRaw(r, name);
            if (obj == null || obj is DBNull) return 0m;

            if (obj is decimal dec) return dec;
            if (obj is double db) return Convert.ToDecimal(db);
            if (obj is float fl) return Convert.ToDecimal(fl);
            if (obj is int i) return i;
            if (obj is long l) return l;

            decimal d;
            if (decimal.TryParse(obj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;

            return 0m;
        }

        private static string ToIsoDate(NDbfReader.Reader r, string name)
        {
            var obj = GetRaw(r, name);
            if (obj == null || obj is DBNull) return "1900-01-01";

            DateTime dt;
            string s = obj.ToString().Trim();

            if (obj is DateTime rawDt) dt = rawDt;
            else if (!DateTime.TryParse(s, out dt))
            {
                // Fallback yyyyMMdd
                if (s.Length == 8 && long.TryParse(s, out _))
                {
                    dt = new DateTime(int.Parse(s.Substring(0, 4)), int.Parse(s.Substring(4, 2)), int.Parse(s.Substring(6, 2)));
                }
                else return "1900-01-01";
            }

            // LOGIKA KUNCI: Paksa ke 20xx (Contoh: 1925 % 100 = 25 -> 2025)
            int fixedYear = 2000 + (dt.Year % 100);
            return new DateTime(fixedYear, dt.Month, dt.Day).ToString("yyyy-MM-dd");
        }
    }
}
