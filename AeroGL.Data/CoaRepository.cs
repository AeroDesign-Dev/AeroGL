using AeroGL.Core;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            // 1) Wajib .001
            if (c == null || string.IsNullOrWhiteSpace(c.Code3) || !System.Text.RegularExpressions.Regex.IsMatch(c.Code3, @"^\d{3}\.\d{3}\.001$"))
                throw new System.InvalidOperationException("Code3 wajib format xxx.xxx.001 (hanya .001 yang diizinkan).");

            // 2) Ambil code2
            var parts = c.Code3.Split('.');
            if (parts.Length != 3) throw new System.InvalidOperationException("Code3 tidak valid.");
            var code2 = parts[0] + "." + parts[1];

            using (var cn = Db.Open())
            {
                // 3) Pastikan tidak ada COA lain dengan code2 yang sama
                //    (karena aturan kita: satu code2 hanya punya .001)
                var existingCode3 = await cn.ExecuteScalarAsync<string>(
                    "SELECT Code3 FROM Coa WHERE substr(Code3,1,7)=@code2 LIMIT 1", new { code2 });

                if (!string.IsNullOrEmpty(existingCode3) && !string.Equals(existingCode3, c.Code3, StringComparison.Ordinal))
                    throw new System.InvalidOperationException("Code2 tersebut sudah dipakai. Hanya satu akun (.001) per code2 yang diizinkan.");

                // 4) Upsert normal (hanya untuk .001)
                const string sql = @"
INSERT INTO Coa(Code3,Name,Type,Grp) VALUES(@Code3,@Name,@Type,@Grp)
ON CONFLICT(Code3) DO UPDATE SET Name=@Name, Type=@Type, Grp=@Grp;";
                await cn.ExecuteAsync(sql, c);
            }
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

        public async Task Delete(string code3)
        {
            using (var cn = Db.Open())
            {
                var cnt = await cn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM CoaBalance WHERE Code3=@c", new { c = code3 });
                if (cnt > 0)
                    throw new System.InvalidOperationException(
                        "Akun sudah memiliki saldo/mutasi. Nonaktifkan saja—jangan dihapus.");
                await cn.ExecuteAsync("DELETE FROM Coa WHERE Code3=@c", new { c = code3 });
            }
        }

        public async Task<List<Coa>> Search(string q)
        {
            using (var cn = Db.Open())
            {
                var rows = await cn.QueryAsync<Coa>(@"
SELECT Code3,Name,Type,Grp
FROM Coa
WHERE Code3 LIKE @x OR Name LIKE @x
ORDER BY Code3", new { x = "%" + q + "%" });
                return rows.AsList();
            }
        }
    }
}
