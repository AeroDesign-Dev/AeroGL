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

        public async Task DeleteById(long id)
        {
            using (var cn = Db.Open())
                await cn.ExecuteAsync("DELETE FROM JournalLine WHERE rowid=@id", new { id });
        }
    }
}
