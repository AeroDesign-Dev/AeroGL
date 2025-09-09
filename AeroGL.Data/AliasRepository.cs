// AeroGL.Data\AliasRepository.cs
using System.Threading.Tasks;
using Dapper;

namespace AeroGL.Data
{
    using AeroGL.Core;

    public sealed class AliasRepository : IAliasRepository
    {
        public async Task<string> ResolveCode3(string code2)
        {
            using (var cn = Db.Open())
            {
                var c3 = await cn.ExecuteScalarAsync<string>(
                    "SELECT Code3 FROM Alias2To3 WHERE Code2=@c2", new { c2 = code2 });
                if (!string.IsNullOrEmpty(c3)) return c3;
                return code2 + ".001";
            }
        }
    }
}
