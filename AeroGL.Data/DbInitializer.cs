using AeroGL.Core;
using Dapper;
using System;
using System.IO;
using System.Data.SQLite;

namespace AeroGL.Data
{
    public static class DbInitializer
    {
        public static void CreateNewDatabase(string dbPath, string companyName)
        {
            // 1. Membuat file fisik database di harddisk
            if (!File.Exists(dbPath)) SQLiteConnection.CreateFile(dbPath);

            // 2. Mekanisme Swap Context (Penting untuk keamanan sesi)
            var originalContext = CurrentCompany.Data;
            try
            {
                // Injeksi context sementara agar Db.Open() mengarah ke file baru
                CurrentCompany.Data = new Company { Id = Guid.NewGuid(), Name = companyName, DbPath = dbPath };

                // 3. Menjalankan Skema Dasar (Tabel & Triggers asli dari codebase)
                Schema.Init();

                // 4. Menjalankan Migrator untuk memastikan tabel pendukung lengkap
                SchemaMigrator.MigrateV2();

                // 5. Seeding Master Account berdasarkan scan aerogl.db
                SeedMasterAccounts();
            }
            finally
            {
                // Mengembalikan context ke awal agar aplikasi tidak "nyasar" ke DB baru secara otomatis
                CurrentCompany.Data = originalContext;
            }
        }

        private static void SeedMasterAccounts()
        {
            using (var cn = Db.Open()) // Menembak DB baru melalui context yang sudah diset
            {
                // Daftar Header Asli dari aerogl.db (Pattern: xxx.000.001)
                var headers = new[]
                {
                    new { Code3 = "001.000.001", Name = "KAS", Type = 0, Grp = 1 },
                    new { Code3 = "002.000.001", Name = "BANK", Type = 0, Grp = 1 },
                    new { Code3 = "003.000.001", Name = "PIUTANG DAGANG", Type = 0, Grp = 1 },
                    new { Code3 = "004.000.001", Name = "PERSEDIAAN", Type = 0, Grp = 1 },
                    new { Code3 = "005.000.001", Name = "PIUTANG KARYAWAN", Type = 0, Grp = 1 },
                    new { Code3 = "006.000.001", Name = "PAJAK DIBAYAR DIMUKA", Type = 0, Grp = 1 },
                    new { Code3 = "007.000.001", Name = "AKTIVA TETAP", Type = 0, Grp = 1 },
                    new { Code3 = "008.000.001", Name = "AKUMULASI PENYUSUTAN", Type = 1, Grp = 1 },
                    new { Code3 = "010.000.001", Name = "HUTANG DAGANG", Type = 1, Grp = 2 },
                    new { Code3 = "011.000.001", Name = "HUTANG BANK", Type = 1, Grp = 2 },
                    new { Code3 = "012.000.001", Name = "HUTANG LAIN2", Type = 1, Grp = 2 },
                    new { Code3 = "014.000.001", Name = "PAJAK YMH DIBAYAR", Type = 1, Grp = 2 },
                    new { Code3 = "015.000.001", Name = "MODAL", Type = 1, Grp = 3 },
                    new { Code3 = "016.000.001", Name = "LABA (RUGI)", Type = 1, Grp = 3 },
                    new { Code3 = "020.000.001", Name = "PENJUALAN", Type = 1, Grp = 4 },
                    new { Code3 = "021.000.001", Name = "HARGA POKOK PENJUALAN", Type = 0, Grp = 5 },
                    new { Code3 = "022.000.001", Name = "BIAYA PENJUALAN", Type = 0, Grp = 5 },
                    new { Code3 = "023.000.001", Name = "BIAYA UMUM & ADM", Type = 0, Grp = 5 },
                    new { Code3 = "025.000.001", Name = "PENDAPATAN LAIN2", Type = 1, Grp = 4 },
                    new { Code3 = "026.000.001", Name = "BIAYA LAIN2", Type = 0, Grp = 5 }
                };

                const string sql = "INSERT OR IGNORE INTO Coa (Code3, Name, Type, Grp) VALUES (@Code3, @Name, @Type, @Grp)";
                cn.Execute(sql, headers);
            }
        }
    }
}