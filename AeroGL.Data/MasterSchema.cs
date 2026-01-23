using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AeroGL.Data
{
    public static class MasterSchema
    {
        public static void Init()
        {
            // Sebelum buka koneksi, pastikan folder AppData/AeroGL sudah ada
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folderPath = Path.Combine(appData, "AeroGL");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            using (var cn = Db.OpenMaster()) // Menggunakan method yang sudah lu buat di Db.cs
            using (var cmd = cn.CreateCommand())
            {
                // SQL untuk membuat tabel Companies sesuai model di AeroGL.Core
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Companies (
                        Id TEXT PRIMARY KEY,           
                        Name TEXT NOT NULL,            
                        DbPath TEXT NOT NULL,          
                        Password TEXT,
                        CreatedDate TEXT NOT NULL,     
                        LastAccessed TEXT,             
                        IsActive INTEGER DEFAULT 0     
                    );";

                cmd.ExecuteNonQuery();
            }
        }
    }
}
