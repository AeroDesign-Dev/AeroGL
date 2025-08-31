using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    public sealed class CoaBalanceRepository : ICoaBalanceRepository
    {
        public async Task<CoaBalance> Get(string code3, int year, int month)
        {
            using (var cn = Db.Open())
                return await cn.QueryFirstOrDefaultAsync<CoaBalance>(@"
                    SELECT Code3,Year,Month,Saldo,Debet,Kredit
                    FROM CoaBalance
                    WHERE Code3=@c AND Year=@y AND Month=@m",
                new { c = code3, y = year, m = month });
        }

        public async Task Upsert(CoaBalance b)
        {
            const string sql = @"
                INSERT INTO CoaBalance(Code3,Year,Month,Saldo,Debet,Kredit)
                VALUES(@Code3,@Year,@Month,@Saldo,@Debet,@Kredit)
                ON CONFLICT(Code3,Year,Month) DO UPDATE SET
                Saldo=@Saldo, Debet=@Debet, Kredit=@Kredit;";
            using (var cn = Db.Open())
                await cn.ExecuteAsync(sql, b);
        }
    }
}