// AeroGL.Data / JournalHeaderRepository.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    public sealed class JournalHeaderRepository : IJournalHeaderRepository
    {
        public async Task<JournalHeader> Get(string noTran)
        {
            using (var cn = Db.Open())
                return await cn.QueryFirstOrDefaultAsync<JournalHeader>(@"
SELECT NoTran,Tanggal,Memo,Type,TotalDebet,TotalKredit
FROM JournalHeader WHERE NoTran=@no", new { no = noTran });
        }

        public async Task Upsert(JournalHeader h)
        {
            const string sql = @"
INSERT INTO JournalHeader(NoTran,Tanggal,Memo,Type)
VALUES(@NoTran,@Tanggal,@Memo,@Type)
ON CONFLICT(NoTran) DO UPDATE SET
  Tanggal=@Tanggal, Memo=@Memo, Type=@Type;";
            using (var cn = Db.Open())
                await cn.ExecuteAsync(sql, h);
        }

        public async Task Delete(string noTran)
        {
            using (var cn = Db.Open())
            {
                // by design: tidak CASCADE → kosongkan baris dulu baru boleh hapus header
                var cnt = await cn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM JournalLine WHERE NoTran=@no", new { no = noTran });
                if (cnt > 0) throw new System.InvalidOperationException(
                    "Header masih punya baris. Hapus semua barisnya dulu.");
                await cn.ExecuteAsync("DELETE FROM JournalHeader WHERE NoTran=@no", new { no = noTran });
            }
        }

        public async Task<List<JournalHeader>> All()
        {
            using (var cn = Db.Open())
            {
                var rows = await cn.QueryAsync<JournalHeader>(@"
SELECT NoTran,Tanggal,Memo,Type,TotalDebet,TotalKredit
FROM JournalHeader
ORDER BY Tanggal, NoTran");
                return rows.AsList();
            }
        }

        public async Task<List<JournalHeader>> Search(string q)
        {
            using (var cn = Db.Open())
            {
                var rows = await cn.QueryAsync<JournalHeader>(@"
SELECT NoTran,Tanggal,Memo,Type,TotalDebet,TotalKredit
FROM JournalHeader
WHERE NoTran LIKE @x OR Memo LIKE @x
ORDER BY Tanggal, NoTran", new { x = "%" + q + "%" });
                return rows.AsList();
            }
        }
    }
}
