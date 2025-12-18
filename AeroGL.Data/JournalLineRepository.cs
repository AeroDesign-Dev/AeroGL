// AeroGL.Data\JournalLineRepository.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    public sealed class JournalLineRepository : IJournalLineRepository
    {
        public async Task<List<JournalLineRecord>> ListByNoTran(string noTran)
        {
            using (var cn = Db.Open())
            {
                var rows = await cn.QueryAsync<JournalLineRecord>(@"
SELECT rowid AS Id, NoTran, Code2, Side, Amount, Narration
FROM JournalLine
WHERE NoTran=@no
ORDER BY rowid", new { no = noTran });
                return rows.AsList();
            }
        }

        public async Task<JournalLineRecord> GetById(long id)
        {
            using (var cn = Db.Open())
                return await cn.QueryFirstOrDefaultAsync<JournalLineRecord>(@"
SELECT rowid AS Id, NoTran, Code2, Side, Amount, Narration
FROM JournalLine WHERE rowid=@id", new { id });
        }

        public async Task<long> Insert(JournalLine line)
        {
            const string sql = @"
INSERT INTO JournalLine(NoTran,Code2,Side,Amount,Narration)
VALUES(@NoTran,@Code2,@Side,@Amount,@Narration);";
            using (var cn = Db.Open())
            {
                await cn.ExecuteAsync(sql, line);
                var id = await cn.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
                return id;
            }
        }

        public async Task UpdateById(long id, JournalLine lineNew)
        {
            const string sql = @"
UPDATE JournalLine
SET NoTran=@NoTran, Code2=@Code2, Side=@Side, Amount=@Amount, Narration=@Narration
WHERE rowid=@Id;";
            using (var cn = Db.Open())
                await cn.ExecuteAsync(sql, new
                {
                    Id = id,
                    lineNew.NoTran,
                    lineNew.Code2,
                    lineNew.Side,
                    lineNew.Amount,
                    lineNew.Narration
                });
        }

        public async Task DeleteByNoTran(string noTran)
        {
            using (var cn = Db.Open())
                await cn.ExecuteAsync("DELETE FROM JournalLine WHERE NoTran=@no", new { no = noTran });
        }

        public async Task<decimal> GetMutationSum(string code2, System.DateTime start, System.DateTime end)
        {
            using (var cn = Db.Open())
            {
                string s = start.ToString("yyyy-MM-dd");
                string e = end.ToString("yyyy-MM-dd");

                var row = await cn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        SUM(CASE WHEN l.Side='D' THEN l.Amount ELSE 0 END) as D,
                        SUM(CASE WHEN l.Side='K' THEN l.Amount ELSE 0 END) as K
                    FROM JournalLine l
                    JOIN JournalHeader h ON h.NoTran = l.NoTran
                    WHERE l.Code2 = @c2 
                      AND h.Tanggal BETWEEN @s AND @e",
                    new { c2 = code2, s, e });

                if (row == null) return 0m;
                return (decimal)row.D - (decimal)row.K;
            }
        }

        public async Task<List<JournalLineRecord>> GetLedgerModeM(string code2, System.DateTime start, System.DateTime end)
        {
            using (var cn = Db.Open())
            {
                // Mode M: Filter Code2 + Join Header
                return (await cn.QueryAsync<JournalLineRecord>(@"
                    SELECT l.rowid AS Id, l.NoTran, h.Tanggal, l.Code2, l.Side, l.Amount, l.Narration
                    FROM JournalLine l
                    JOIN JournalHeader h ON h.NoTran = l.NoTran
                    WHERE l.Code2 = @c2 
                      AND h.Tanggal BETWEEN @s AND @e
                    ORDER BY h.Tanggal, l.NoTran, l.rowid",
                    new { c2 = code2, s = start.ToString("yyyy-MM-dd"), e = end.ToString("yyyy-MM-dd") }))
                    .AsList();
            }
        }

        public async Task<List<JournalLineRecord>> GetLedgerModeN(string code2, System.DateTime start, System.DateTime end)
        {
            using (var cn = Db.Open())
            {
                // Mode N: Subquery NoTran + Join Header
                return (await cn.QueryAsync<JournalLineRecord>(@"
                    SELECT l.rowid AS Id, l.NoTran, h.Tanggal, l.Code2, l.Side, l.Amount, l.Narration
                    FROM JournalLine l
                    JOIN JournalHeader h ON h.NoTran = l.NoTran
                    WHERE l.NoTran IN (
                        SELECT DISTINCT lx.NoTran 
                        FROM JournalLine lx 
                        JOIN JournalHeader hx ON hx.NoTran = lx.NoTran
                        WHERE lx.Code2 = @c2 
                          AND hx.Tanggal BETWEEN @s AND @e
                    )
                    ORDER BY h.Tanggal, l.NoTran, l.rowid",
                    new { c2 = code2, s = start.ToString("yyyy-MM-dd"), e = end.ToString("yyyy-MM-dd") }))
                    .AsList();
            }
        }
    }
}
