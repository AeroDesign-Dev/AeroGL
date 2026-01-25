using AeroGL.Core;
using Dapper;
using System;
using System.Configuration;
using System.Data.SQLite;
using System.IO;

namespace AeroGL.Data
{
    public static class Db
    {
        static Db()
        {
            SqlMapper.AddTypeHandler(new GuidTypeHandler());
        }
        public static SQLiteConnection Open()
        {
            // Kuncinya di sini: ambil path dari context global
            if (CurrentCompany.Data == null)
                throw new InvalidOperationException("Pilih perusahaan dulu di GateWindow!");

            var cn = new SQLiteConnection($"Data Source={CurrentCompany.Data.DbPath};Version=3;");
            cn.Open();
            return cn;
        }

        public static SQLiteConnection OpenMaster()
        {
            // Lokasi master.db tetap, misal di folder AppData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string masterPath = System.IO.Path.Combine(appData, "AeroGL", "master.db");

            var cn = new SQLiteConnection($"Data Source={masterPath};Version=3;");
            cn.Open();
            return cn;
        }
    }

    public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        // Cara simpan ke Database (Guid -> String)
        public override void SetValue(System.Data.IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
        }

        // Cara ambil dari Database (String -> Guid)
        public override Guid Parse(object value)
        {
            return Guid.Parse((string)value);
        }
    }
}
