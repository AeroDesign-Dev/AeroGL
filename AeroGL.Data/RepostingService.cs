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
                    // 1. RESET MUTASI TAHUN TERKAIT JADI 0
                    // ---------------------------------------------------------
                    OnProgress?.Invoke("Resetting Debet/Kredit...");
                    await cn.ExecuteAsync(@"
                UPDATE CoaBalance 
                SET Debet = 0, Kredit = 0 
                WHERE Year = @y", new { y = year }, trans);

                    // ---------------------------------------------------------
                    // 2. HITUNG TOTAL JURNAL (AGREGASI)
                    // ---------------------------------------------------------
                    OnProgress?.Invoke("Summing Journals...");

                    // PERINGATAN: Gue benerin GROUP BY lo. 
                    // Kalau lo Group By Code2 tapi select Code3, angkanya ketimpa kalau 1 Code3 punya banyak Code2.
                    var journalSummary = await cn.QueryAsync<dynamic>(@"
                SELECT 
                    (JL.Code2 || '.001') AS Code3, 
                    CAST(strftime('%m', JH.Tanggal) AS INTEGER) AS Month,
                    JL.Side,
                    SUM(JL.Amount) AS Total
                FROM JournalLine JL
                JOIN JournalHeader JH ON JL.NoTran = JH.NoTran
                WHERE strftime('%Y', JH.Tanggal) = CAST(@y AS TEXT)
                GROUP BY 1, 2, 3", // Grouping berdasar Code3, Month, dan Side
                        new { y = year }, trans);

                    // ---------------------------------------------------------
                    // 3. MASUKKIN HASIL SUM KE COA BALANCE
                    // ---------------------------------------------------------
                    OnProgress?.Invoke("Updating CoaBalance...");

                    foreach (var item in journalSummary)
                    {
                        string col = (item.Side == "D") ? "Debet" : "Kredit";
                        string sqlUpdate = $"UPDATE CoaBalance SET {col} = @val WHERE Code3=@c AND Year=@y AND Month=@m";

                        int affected = await cn.ExecuteAsync(sqlUpdate,
                            new { val = item.Total, c = item.Code3, y = year, m = item.Month }, trans);

                        // Kalau barisnya belum ada, buat baru dengan mutasi hasil sum tadi
                        if (affected == 0)
                        {
                            string insertCol = (item.Side == "D") ? item.Total.ToString() : "0";
                            string insertColK = (item.Side == "K") ? item.Total.ToString() : "0";

                            await cn.ExecuteAsync(@"
                        INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                        VALUES (@c, @y, @m, 0, @d, @k)",
                                new { c = item.Code3, y = year, m = item.Month, d = item.Total * (item.Side == "D" ? 1 : 0), k = item.Total * (item.Side == "K" ? 1 : 0) }, trans);
                        }
                    }

                    trans.Commit();
                    OnProgress?.Invoke("Reposting Selesai!");
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