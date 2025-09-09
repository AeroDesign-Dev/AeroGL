// AeroGL.Data\PostingRealtimeService.cs
using System;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    public sealed class PostingRealtimeService
    {
        private readonly IJournalHeaderRepository _hdr = new JournalHeaderRepository();
        private readonly IJournalLineRepository _line = new JournalLineRepository();
        private readonly IAliasRepository _alias = new AliasRepository();

        // ===== Public API =====

        public async Task<long> InsertLine(JournalLine line)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                // 1) Header & period
                var hdr = await _hdr.Get(line.NoTran);
                if (hdr == null) throw new InvalidOperationException("Header jurnal tidak ditemukan.");
                YearMonth(hdr.Tanggal, out int y, out int m);

                // 2) Resolve Code3 & guard COA exists
                var code3 = await _alias.ResolveCode3(line.Code2);
                await EnsureCoaExists(cn, code3, tx);

                // 3) Update ember
                await ApplyDeltaToBucket(cn, code3, y, m, line.Side, line.Amount, +1, tx);

                // 4) Insert line
                var id = await InsertLineRaw(cn, line, tx);

                tx.Commit();
                return id;
            }
        }

        public async Task UpdateLine(long id, JournalLine newLine)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                // Ambil old line
                var old = await _line.GetById(id);
                if (old == null) throw new InvalidOperationException("Line tidak ditemukan.");

                // Header lama/baru (NoTran bisa berubah)
                var hdrOld = await _hdr.Get(old.NoTran);
                if (hdrOld == null) throw new InvalidOperationException("Header lama tidak ditemukan.");
                YearMonth(hdrOld.Tanggal, out int yOld, out int mOld);

                var hdrNew = await _hdr.Get(newLine.NoTran);
                if (hdrNew == null) throw new InvalidOperationException("Header baru tidak ditemukan.");
                YearMonth(hdrNew.Tanggal, out int yNew, out int mNew);

                // Resolve code3 lama/baru
                var c3Old = await _alias.ResolveCode3(old.Code2);
                var c3New = await _alias.ResolveCode3(newLine.Code2);

                await EnsureCoaExists(cn, c3New, tx); // pastikan target ada

                // Kurangi ember lama
                await ApplyDeltaToBucket(cn, c3Old, yOld, mOld, old.Side, old.Amount, -1, tx);

                // Tambah ember baru
                await ApplyDeltaToBucket(cn, c3New, yNew, mNew, newLine.Side, newLine.Amount, +1, tx);

                // Update row
                await UpdateLineRaw(cn, id, newLine, tx);

                tx.Commit();
            }
        }

        public async Task DeleteLine(long id)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                var old = await _line.GetById(id);
                if (old == null) return;

                var hdr = await _hdr.Get(old.NoTran);
                if (hdr == null) throw new InvalidOperationException("Header tidak ditemukan.");
                YearMonth(hdr.Tanggal, out int y, out int m);

                var c3 = await _alias.ResolveCode3(old.Code2);

                // Kurangi ember
                await ApplyDeltaToBucket(cn, c3, y, m, old.Side, old.Amount, -1, tx);

                // Hapus row
                await DeleteLineRaw(cn, id, tx);

                tx.Commit();
            }
        }

        // ===== Internals =====

        private static void YearMonth(string iso, out int y, out int m)
        {
            var dt = DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            y = dt.Year; m = dt.Month;
        }

        private static async Task EnsureCoaExists(System.Data.IDbConnection cn, string code3, System.Data.IDbTransaction tx)
        {
            var cnt = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Coa WHERE Code3=@c3", new { c3 = code3 }, tx);
            if (cnt == 0)
                throw new InvalidOperationException("Mapping Code2→Code3 menghasilkan akun yang belum ada di COA: " + code3);
        }

        private static async Task ApplyDeltaToBucket(System.Data.IDbConnection cn,
            string code3, int year, int month, string side, decimal amount, int sign, System.Data.IDbTransaction tx)
        {
            // sign = +1 untuk tambah; -1 untuk kurangi
            decimal d = 0m, k = 0m;
            if (string.Equals(side, "D", StringComparison.OrdinalIgnoreCase)) d = amount * sign;
            else k = amount * sign;

            const string up = @"
INSERT INTO CoaBalance(Code3,Year,Month,Saldo,Debet,Kredit)
VALUES(@c3,@y,@m,0, @d, @k)
ON CONFLICT(Code3,Year,Month) DO UPDATE SET
  Debet  = Debet  + @d,
  Kredit = Kredit + @k;";
            await cn.ExecuteAsync(up, new { c3 = code3, y = year, m = month, d, k }, tx);
        }

        // Raw ops bypass repo to keep single tx
        private static async Task<long> InsertLineRaw(System.Data.IDbConnection cn, JournalLine line, System.Data.IDbTransaction tx)
        {
            const string sql = @"
INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration)
VALUES(@NoTran,@Code2,@Side,@Amount,@Narration);";
            await cn.ExecuteAsync(sql, line, tx);
            return await cn.ExecuteScalarAsync<long>("SELECT last_insert_rowid();", transaction: tx);
        }

        private static async Task UpdateLineRaw(System.Data.IDbConnection cn, long id, JournalLine lineNew, System.Data.IDbTransaction tx)
        {
            const string sql = @"
UPDATE JournalLine
SET NoTran=@NoTran, Code2=@Code2, Side=@Side, Amount=@Amount, Narration=@Narration
WHERE rowid=@Id;";
            await cn.ExecuteAsync(sql, new
            {
                Id = id,
                lineNew.NoTran,
                lineNew.Code2,
                lineNew.Side,
                lineNew.Amount,
                lineNew.Narration
            }, tx);
        }

        private static async Task DeleteLineRaw(System.Data.IDbConnection cn, long id, System.Data.IDbTransaction tx)
        {
            await cn.ExecuteAsync("DELETE FROM JournalLine WHERE rowid=@id", new { id }, tx);
        }
    }
}
