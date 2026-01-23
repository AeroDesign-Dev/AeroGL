using AeroGL.Core;
using System;
using System.Configuration;
using System.Data.SQLite;
using System.IO;

namespace AeroGL.Data
{
    public static class Db
    {
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
}
