using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using AeroGL.Core;

namespace AeroGL.Data
{
    public sealed class CoaRepository : ICoaRepository
    {
        public async Task<Coa> Get(string code3)
        {
            using (var cn = Db.Open())
                return await cn.QueryFirstOrDefaultAsync<Coa>(
                    "SELECT Code3,Name,Type,Grp FROM Coa WHERE Code3=@c", new { c = code3 });
        }

        public async Task Upsert(Coa c)
        {
            const string sql = @"
                INSERT INTO Coa(Code3,Name,Type,Grp) VALUES(@Code3,@Name,@Type,@Grp)
                ON CONFLICT(Code3) DO UPDATE SET Name=@Name, Type=@Type, Grp=@Grp;";
            using (var cn = Db.Open())
                await cn.ExecuteAsync(sql, c);
        }

        public async Task<List<Coa>> All()
        {
            using (var cn = Db.Open())
            {
                var rows = await cn.QueryAsync<Coa>(
                    "SELECT Code3,Name,Type,Grp FROM Coa ORDER BY Code3");
                return rows.AsList(); // atau rows.ToList();
            }
        }
    }
}
