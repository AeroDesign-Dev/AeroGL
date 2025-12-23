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
            {
                using (var trans = cn.BeginTransaction())
                {
                    try
                    {
                        // ---------------------------------------------------------
                        // 1. RESET MUTASI (Debet & Kredit) TAHUN INI JADI 0
                        // ---------------------------------------------------------
                        // Note: Saldo Awal (Month 1) JANGAN di-nol-kan, karena itu saldo bawaan migrasi.
                        OnProgress?.Invoke("Resetting Data Saldo...");
                        await cn.ExecuteAsync(@"
                            UPDATE CoaBalance 
                            SET Debet = 0, Kredit = 0 
                            WHERE Year = @y", new { y = year }, trans);

                        // ---------------------------------------------------------
                        // 2. HITUNG TOTAL JURNAL (AGREGASI)
                        // ---------------------------------------------------------
                        OnProgress?.Invoke("Menganalisa Jurnal...");

                        // Group by Akun & Bulan & Side (D/K)
                        // Inget: JournalLine nyimpen Code2 (misal 110.001), Code3 kita asumsi sama.
                        var journalSummary = await cn.QueryAsync<dynamic>(@"
                            SELECT 
                                SUBSTR(JL.Code2, 1, 7) || '.001' AS Code3,
                                CAST(strftime('%m', JH.Tanggal) AS INTEGER) AS Month,
                                JL.Side,
                                SUM(JL.Amount) AS Total
                            FROM JournalLine JL
                            JOIN JournalHeader JH ON JL.NoTran = JH.NoTran
                            WHERE strftime('%Y', JH.Tanggal) = CAST(@y AS TEXT)
                            GROUP BY JL.Code2, strftime('%m', JH.Tanggal), JL.Side",
                            new { y = year }, trans);

                        // ---------------------------------------------------------
                        // 3. POSTING MUTASI KE COA BALANCE
                        // ---------------------------------------------------------
                        OnProgress?.Invoke("Posting Mutasi Jurnal...");

                        foreach (var item in journalSummary)
                        {
                            string col = (item.Side == "D") ? "Debet" : "Kredit";
                            string sqlUpdate = $"UPDATE CoaBalance SET {col} = @val WHERE Code3=@c AND Year=@y AND Month=@m";

                            // Coba update
                            int rows = await cn.ExecuteAsync(sqlUpdate,
                                new { val = item.Total, c = item.Code3, y = year, m = item.Month }, trans);

                            // Kalau row belum ada (akun baru dipake bulan itu), Insert dulu
                            if (rows == 0)
                            {
                                await cn.ExecuteAsync(@"
                                    INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                                    VALUES (@c, @y, @m, 0, 0, 0)",
                                    new { c = item.Code3, y = year, m = item.Month }, trans);

                                // Update lagi setelah insert
                                await cn.ExecuteAsync(sqlUpdate,
                                    new { val = item.Total, c = item.Code3, y = year, m = item.Month }, trans);
                            }
                        }

                        // ---------------------------------------------------------
                        // 4. ROLLING SALDO (HITUNG ULANG SALDO BULAN 2 s/d 12)
                        // ---------------------------------------------------------
                        OnProgress?.Invoke("Menghitung Rolling Balance...");

                        // Ambil Master Coa (buat tau Type Debet/Kredit utk rumus saldo)
                        var allCoa = (await cn.QueryAsync<Coa>("SELECT * FROM Coa")).ToDictionary(x => x.Code3);

                        // Ambil semua balance tahun ini, urutkan per akun & bulan
                        var balances = (await cn.QueryAsync<CoaBalance>(
                            "SELECT * FROM CoaBalance WHERE Year=@y ORDER BY Code3, Month",
                            new { y = year }, trans)).ToList();

                        var balPerAccount = balances.GroupBy(b => b.Code3);

                        foreach (var grp in balPerAccount)
                        {
                            string code = grp.Key;
                            if (!allCoa.ContainsKey(code)) continue;

                            var coa = allCoa[code];
                            // Logic AeroGL: 
                            // AccountType.Debit (0) = Harta/Biaya -> Normal Debet
                            // AccountType.Kredit (1) = Hutang/Modal/Pendapatan -> Normal Kredit
                            bool isNormalDebet = (coa.Type == AccountType.Debit);

                            // Urutkan bulan 1..12
                            var listBal = grp.OrderBy(x => x.Month).ToList();

                            // Mulai tracking saldo dari Bulan 1 (Saldo Awal Tahun / Migrasi)
                            var firstBal = listBal.FirstOrDefault(x => x.Month == 1);
                            decimal runningSaldo = firstBal?.Saldo ?? 0;

                            // Loop dari Bulan 1 sampai 11 (untuk update saldo bulan 2..12)
                            // Saldo Bulan N = Saldo Awal Bulan N
                            for (int m = 1; m < 12; m++)
                            {
                                var current = listBal.FirstOrDefault(x => x.Month == m);
                                decimal debet = current?.Debet ?? 0;
                                decimal kredit = current?.Kredit ?? 0;

                                // Hitung Saldo Akhir bulan ini (yang jadi Saldo Awal bulan depan)
                                decimal saldoAkhir = 0;
                                if (isNormalDebet)
                                    saldoAkhir = runningSaldo + debet - kredit;
                                else
                                    saldoAkhir = runningSaldo + kredit - debet;

                                // Update Saldo Awal bulan berikutnya (m+1)
                                int nextMonth = m + 1;

                                int affected = await cn.ExecuteAsync(
                                    "UPDATE CoaBalance SET Saldo=@s WHERE Code3=@c AND Year=@y AND Month=@m",
                                    new { s = saldoAkhir, c = code, y = year, m = nextMonth }, trans);

                                // Kalau bulan depan belum ada record, insert
                                if (affected == 0)
                                {
                                    await cn.ExecuteAsync(@"
                                        INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                                        VALUES (@c, @y, @m, @s, 0, 0)",
                                        new { c = code, y = year, m = nextMonth, s = saldoAkhir }, trans);
                                }

                                // Update running saldo buat loop selanjutnya
                                runningSaldo = saldoAkhir;
                            }
                        }

                        trans.Commit();
                        OnProgress?.Invoke("Reposting Selesai!");
                    }
                    catch (Exception)
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}