using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    /// <summary>
    /// Service posting real-time CoaBalance.
    /// - Kompatibel C# 7.3
    /// - Dua mode:
    ///   (1) Self-managed (InsertLine/UpdateLine/DeleteLine) → buka cn/tx sendiri
    ///   (2) In-transaction (ctor(IDbConnection, IDbTransaction)) → pakai cn/tx caller
    /// - API posting/unposting:
    ///   a) PostLine/UnpostLine(JournalLineRecord, tanggalIso)  [butuh injeksi cn/tx]
    ///   b) PostLine/UnpostLine(tanggalIso, code2, side, amount) [bisa injeksi atau self-managed]
    ///   c) Static helper: PostLine/UnpostLine(cn, tx, line, tanggalIso)
    /// </summary>
    public sealed class PostingRealtimeService
    {
        // ===== infra =====
        private readonly IDbConnection _cn;
        private readonly IDbTransaction _tx;
        private readonly bool _ownsConnection;

        // ===== ctor =====

        // Self-managed: instance tanpa cn/tx injeksi
        public PostingRealtimeService()
        {
            _cn = null;
            _tx = null;
            _ownsConnection = true;
        }

        // In-transaction: pakai cn/tx dari caller (atomic dalam satu transaksi)
        public PostingRealtimeService(IDbConnection cn, IDbTransaction tx)
        {
            if (cn == null) throw new ArgumentNullException(nameof(cn));
            _cn = cn;
            _tx = tx;
            _ownsConnection = false;
        }

        // ===== API self-managed (per baris) =====

        public async Task<long> InsertLine(JournalLine line)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                var hdr = await GetHeaderInfo(cn, line.NoTran, tx); // validasi + period
                var code3 = await ResolveCode3Async(cn, line.Code2, tx);
                await EnsureCoaExists(cn, code3, tx);

                await ApplyDeltaToBucket(cn, code3, hdr.Year, hdr.Month, line.Side, line.Amount, +1, tx);

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
                var old = await GetLineById(cn, id, tx);
                if (old == null) throw new InvalidOperationException("Line tidak ditemukan.");

                var hdrOld = await GetHeaderInfo(cn, old.NoTran, tx);
                var hdrNew = await GetHeaderInfo(cn, newLine.NoTran, tx);

                var c3Old = await ResolveCode3Async(cn, old.Code2, tx);
                var c3New = await ResolveCode3Async(cn, newLine.Code2, tx);
                await EnsureCoaExists(cn, c3New, tx);

                // unpost lama
                await ApplyDeltaToBucket(cn, c3Old, hdrOld.Year, hdrOld.Month, old.Side, old.Amount, -1, tx);
                // post baru
                await ApplyDeltaToBucket(cn, c3New, hdrNew.Year, hdrNew.Month, newLine.Side, newLine.Amount, +1, tx);

                await UpdateLineRaw(cn, id, newLine, tx);
                tx.Commit();
            }
        }

        public async Task DeleteLine(long id)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                var old = await GetLineById(cn, id, tx);
                if (old == null) return;

                var hdr = await GetHeaderInfo(cn, old.NoTran, tx);
                var c3 = await ResolveCode3Async(cn, old.Code2, tx);

                // unpost
                await ApplyDeltaToBucket(cn, c3, hdr.Year, hdr.Month, old.Side, old.Amount, -1, tx);

                await DeleteLineRaw(cn, id, tx);
                tx.Commit();
            }
        }

        // ===== API in-transaction (dipakai di EDIT/DELETE massal) =====
        // NOTE: untuk overload JournalLineRecord, WAJIB pakai ctor yang inject cn/tx

        /// <summary> Tambahkan dampak line ke CoaBalance (pakai tanggal header). </summary>
        public Task PostLine(JournalLineRecord line, string tanggalIso)
        {
            return ApplyMovementInTx(line, tanggalIso, false);
        }

        /// <summary> Balikkan dampak line dari CoaBalance. </summary>
        public Task UnpostLine(JournalLineRecord line, string tanggalIso)
        {
            return ApplyMovementInTx(line, tanggalIso, true);
        }

        // ===== Overload 4 argumen (sesuai call-site di JournalWindow) =====
        // Bisa dipakai baik pada instance self-managed maupun injected.
        public async Task PostLine(string tanggalIso, string code2, string side, decimal amount)
        {
            if (_ownsConnection)
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    await PostOrUnpostScalar(cn, tx, tanggalIso, code2, side, amount, false);
                    tx.Commit();
                }
                return;
            }

            // injected mode
            await PostOrUnpostScalar(_cn, _tx, tanggalIso, code2, side, amount, false);
        }

        public async Task UnpostLine(string tanggalIso, string code2, string side, decimal amount)
        {
            if (_ownsConnection)
            {
                using (var cn = Db.Open())
                using (var tx = cn.BeginTransaction())
                {
                    await PostOrUnpostScalar(cn, tx, tanggalIso, code2, side, amount, true);
                    tx.Commit();
                }
                return;
            }

            // injected mode
            await PostOrUnpostScalar(_cn, _tx, tanggalIso, code2, side, amount, true);
        }

        // ===== Static helper (tetep dipertahankan) =====
        public static Task PostLine(IDbConnection cn, IDbTransaction tx, JournalLineRecord line, string tanggalIso)
        {
            var svc = new PostingRealtimeService(cn, tx);
            return svc.PostLine(line, tanggalIso);
        }

        public static Task UnpostLine(IDbConnection cn, IDbTransaction tx, JournalLineRecord line, string tanggalIso)
        {
            var svc = new PostingRealtimeService(cn, tx);
            return svc.UnpostLine(line, tanggalIso);
        }

        // ===== inti shared =====

        private async Task ApplyMovementInTx(JournalLineRecord line, string tanggalIso, bool isUnpost)
        {
            if (_ownsConnection)
                throw new InvalidOperationException("Gunakan ctor (IDbConnection, IDbTransaction) untuk Post/Unpost(JournalLineRecord, ...).");

            if (line == null) throw new ArgumentNullException(nameof(line));
            if (string.IsNullOrWhiteSpace(tanggalIso)) throw new ArgumentNullException(nameof(tanggalIso));

            var ym = YearMonth(tanggalIso);
            var code3 = await ResolveCode3Async(_cn, line.Code2, _tx);
            await EnsureCoaExists(_cn, code3, _tx);
            await EnsureCoaBucketExists(_cn, code3, ym.Year, ym.Month, _tx);

            decimal d = line.Side == "D" ? line.Amount : 0m;
            decimal k = line.Side == "K" ? line.Amount : 0m;
            if (isUnpost) { d = -d; k = -k; }

            await _cn.ExecuteAsync(@"
UPDATE CoaBalance
   SET Debet  = Debet  + @d,
       Kredit = Kredit + @k
 WHERE Code3=@c3 AND Year=@y AND Month=@m;",
   new { c3 = code3, y = ym.Year, m = ym.Month, d = d, k = k }, _tx);
        }

        private async Task PostOrUnpostScalar(IDbConnection cn, IDbTransaction tx,
                                              string tanggalIso, string code2, string side, decimal amount, bool isUnpost)
        {
            if (string.IsNullOrWhiteSpace(tanggalIso)) throw new ArgumentNullException(nameof(tanggalIso));
            if (string.IsNullOrWhiteSpace(code2)) throw new ArgumentNullException(nameof(code2));
            if (string.IsNullOrWhiteSpace(side)) throw new ArgumentNullException(nameof(side));

            var ym = YearMonth(tanggalIso);
            var code3 = await ResolveCode3Async(cn, code2, tx);
            await EnsureCoaExists(cn, code3, tx);
            await EnsureCoaBucketExists(cn, code3, ym.Year, ym.Month, tx);

            decimal d = 0m, k = 0m;
            if (string.Equals(side, "D", StringComparison.OrdinalIgnoreCase)) d = amount;
            else k = amount;
            if (isUnpost) { d = -d; k = -k; }

            await cn.ExecuteAsync(@"
UPDATE CoaBalance
   SET Debet  = Debet  + @d,
       Kredit = Kredit + @k
 WHERE Code3=@c3 AND Year=@y AND Month=@m;",
   new { c3 = code3, y = ym.Year, m = ym.Month, d = d, k = k }, tx);
        }

        private static YearMonthInfo YearMonth(string iso)
        {
            var dt = DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new YearMonthInfo { Year = dt.Year, Month = dt.Month };
        }

        private static async Task<HeaderInfo> GetHeaderInfo(IDbConnection cn, string noTran, IDbTransaction tx)
        {
            var hdr = await cn.QueryFirstOrDefaultAsync<JournalHeader>(@"
SELECT NoTran,Tanggal,Type,TotalDebet,TotalKredit,Memo
FROM JournalHeader WHERE NoTran=@no;",
                new { no = noTran }, tx);
            if (hdr == null) throw new InvalidOperationException("Header jurnal tidak ditemukan.");

            var dt = DateTime.ParseExact(hdr.Tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new HeaderInfo { TanggalIso = hdr.Tanggal, Year = dt.Year, Month = dt.Month };
        }

        private static async Task EnsureCoaExists(IDbConnection cn, string code3, IDbTransaction tx)
        {
            var cnt = await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Coa WHERE Code3=@c3;", new { c3 = code3 }, tx);
            if (cnt == 0) throw new InvalidOperationException("Akun COA belum ada: " + code3);
        }

        private static async Task EnsureCoaBucketExists(IDbConnection cn, string code3, int y, int m, IDbTransaction tx)
        {
            await cn.ExecuteAsync(@"
INSERT OR IGNORE INTO CoaBalance(Code3,Year,Month,Saldo,Debet,Kredit)
VALUES(@c3,@y,@m,0,0,0);", new { c3 = code3, y = y, m = m }, tx);
        }

        private static async Task<string> ResolveCode3Async(IDbConnection cn, string code2, IDbTransaction tx)
        {
            if (string.IsNullOrWhiteSpace(code2))
                throw new ArgumentException("Code2 kosong.", nameof(code2));

            // Map di Alias2To3; fallback ke ".001"
            var code3 = await cn.ExecuteScalarAsync<string>(
                "SELECT Code3 FROM Alias2To3 WHERE Code2=@c LIMIT 1;", new { c = code2 }, tx);
            return string.IsNullOrWhiteSpace(code3) ? (code2 + ".001") : code3;
        }

        private static async Task<JournalLineRecord> GetLineById(IDbConnection cn, long id, IDbTransaction tx)
        {
            return await cn.QueryFirstOrDefaultAsync<JournalLineRecord>(@"
SELECT rowid AS Id, NoTran, Code2, Side, Amount, Narration
FROM JournalLine
WHERE rowid=@id;", new { id = id }, tx);
        }

        private static async Task<long> InsertLineRaw(IDbConnection cn, JournalLine line, IDbTransaction tx)
        {
            const string sql = @"
INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration)
VALUES(@NoTran,@Code2,@Side,@Amount,@Narration);";
            await cn.ExecuteAsync(sql, line, tx);
            return await cn.ExecuteScalarAsync<long>("SELECT last_insert_rowid();", transaction: tx);
        }

        private static async Task UpdateLineRaw(IDbConnection cn, long id, JournalLine lineNew, IDbTransaction tx)
        {
            const string sql = @"
UPDATE JournalLine
   SET NoTran=@NoTran, Code2=@Code2, Side=@Side, Amount=@Amount, Narration=@Narration
 WHERE rowid=@Id;";
            await cn.ExecuteAsync(sql, new
            {
                Id = id,
                NoTran = lineNew.NoTran,
                Code2 = lineNew.Code2,
                Side = lineNew.Side,
                Amount = lineNew.Amount,
                Narration = lineNew.Narration
            }, tx);
        }

        private static async Task DeleteLineRaw(IDbConnection cn, long id, IDbTransaction tx)
        {
            await cn.ExecuteAsync("DELETE FROM JournalLine WHERE rowid=@id;", new { id = id }, tx);
        }

        private static async Task ApplyDeltaToBucket(
            IDbConnection cn, string code3, int year, int month,
            string side, decimal amount, int sign, IDbTransaction tx)
        {
            decimal d = 0m, k = 0m;
            if (string.Equals(side, "D", StringComparison.OrdinalIgnoreCase)) d = amount * sign;
            else k = amount * sign;

            await EnsureCoaBucketExists(cn, code3, year, month, tx);

            await cn.ExecuteAsync(@"
UPDATE CoaBalance
   SET Debet  = Debet  + @d,
       Kredit = Kredit + @k
 WHERE Code3=@c3 AND Year=@y AND Month=@m;",
   new { c3 = code3, y = year, m = month, d = d, k = k }, tx);
        }

        // ===== POCO helper =====
        private struct HeaderInfo
        {
            public string TanggalIso;
            public int Year;
            public int Month;
        }

        private struct YearMonthInfo
        {
            public int Year;
            public int Month;
        }
    }
}
