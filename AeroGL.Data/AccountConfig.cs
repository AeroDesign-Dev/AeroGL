using AeroGL.Data;
using Dapper;
using System.Linq;

namespace AeroGL.Data
{
    public static class AccountConfig
    {
        // Default sesuai legacy
        public static string PrefixLabaDitahan { get; private set; } = "016";
        public static string PrefixLabaBerjalan { get; private set; } = "017";

        public static void Reload()
        {
            using (var cn = Db.Open())
            {
                var data = cn.Query("SELECT Key, Val FROM Config")
                             .ToDictionary(x => (string)x.Key, x => (string)x.Val);

                if (data.ContainsKey("LabaDitahan")) PrefixLabaDitahan = data["LabaDitahan"];
                if (data.ContainsKey("LabaBerjalan")) PrefixLabaBerjalan = data["LabaBerjalan"];
            }
        }

        public static void Save(string ditahan, string berjalan)
        {
            using (var cn = Db.Open())
            using (var tx = cn.BeginTransaction())
            {
                string sql = "INSERT OR REPLACE INTO Config (Key, Val) VALUES (@k, @v)";
                cn.Execute(sql, new { k = "LabaDitahan", v = ditahan }, tx);
                cn.Execute(sql, new { k = "LabaBerjalan", v = berjalan }, tx);
                tx.Commit();
            }
            Reload();
        }

        public static string Get(string key, string defaultValue = "")
        {
            using (var cn = Db.Open()) // Menggunakan database PT yang aktif
            {
                return cn.ExecuteScalar<string>(
                    "SELECT Val FROM Config WHERE Key = @k", new { k = key }) ?? defaultValue;
            }
        }

        public static void Set(string key, string val)
        {
            using (var cn = Db.Open())
            {
                // Gunakan INSERT OR REPLACE karena 'Key' adalah Primary Key
                cn.Execute(
                    "INSERT OR REPLACE INTO Config (Key, Val) VALUES (@k, @v)",
                    new { k = key, v = val });
            }
        }
    }
}