using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AeroGL.Core;
using Dapper;

namespace AeroGL.Data
{
    public class RepostingService
    {
        // Event buat ngasih kabar ke UI (biar text "Processing..." jalan)
        public event Action<string> OnProgress;

        public async Task RunReposting(int year)
        {
            using (var cn = Db.Open())
            using (var trans = cn.BeginTransaction())
            {
                try
                {
                    // ---------------------------------------------------------
                    // 1. RESET TOTAL (DEBET, KREDIT, DAN SALDO)
                    // ---------------------------------------------------------
                    OnProgress?.Invoke("Membersihkan semua sampah data...");

                    // RESET MUTASI: Debet & Kredit jadi 0 untuk semua bulan di tahun itu
                    await cn.ExecuteAsync("UPDATE CoaBalance SET Debet = 0, Kredit = 0 WHERE Year = @y", new { y = year }, trans);

                    // RESET SALDO: Hapus semua angka "jiplakan" di kolom Saldo untuk bulan 2-12
                    // Bulan 1 (Januari) TIDAK DISENTUH karena itu saldo awal tahun/migrasi
                    await cn.ExecuteAsync("UPDATE CoaBalance SET Saldo = 0 WHERE Year = @y AND Month > 1", new { y = year }, trans);

                    // ---------------------------------------------------------
                    // 2. SUM JURNAL (MURNI AGREGASI)
                    // ---------------------------------------------------------
                    OnProgress?.Invoke("Menghitung ulang mutasi jurnal...");

                    var journalSummary = await cn.QueryAsync<dynamic>(@"
                SELECT 
                    (SUBSTR(JL.Code2, 1, 7) || '.001') AS Code3, 
                    CAST(strftime('%m', JH.Tanggal) AS INTEGER) AS Month,
                    JL.Side,
                    SUM(JL.Amount) AS Total
                FROM JournalLine JL
                JOIN JournalHeader JH ON JL.NoTran = JH.NoTran
                WHERE strftime('%Y', JH.Tanggal) = CAST(@y AS TEXT)
                GROUP BY 1, 2, 3", // Grouping berdasar Code3, Month, Side
                        new { y = year }, trans);

                    // ---------------------------------------------------------
                    // 3. UPDATE KE COA BALANCE
                    // ---------------------------------------------------------
                    foreach (var item in journalSummary)
                    {
                        string col = (item.Side == "D") ? "Debet" : "Kredit";

                        // Update mutasi hasil SUM tadi
                        int affected = await cn.ExecuteAsync(
                            $"UPDATE CoaBalance SET {col} = @val WHERE Code3=@c AND Year=@y AND Month=@m",
                            new { val = item.Total, c = item.Code3, y = year, m = item.Month }, trans);

                        // Kalau barisnya belum ada di DB (misal akun baru dipakai di bulan itu)
                        if (affected == 0)
                        {
                            await cn.ExecuteAsync(@"
                        INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                        VALUES (@c, @y, @m, 0, @d, @k)",
                                new
                                {
                                    c = item.Code3,
                                    y = year,
                                    m = item.Month,
                                    d = (item.Side == "D" ? item.Total : 0),
                                    k = (item.Side == "K" ? item.Total : 0)
                                }, trans);
                        }
                    }

                    trans.Commit();
                    OnProgress?.Invoke("Reposting Berhasil (Murni Agregasi)!");
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    throw new Exception("Gagal Reposting: " + ex.Message);
                }
            }
        }
    }
}