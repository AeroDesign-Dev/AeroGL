using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AeroGL.Core;
using Dapper;

namespace AeroGL.Data
{
    public class MonthlyClosingService
    {
        public async Task RunClosing(int year, int month)
        {
            // Validasi: Bulan 12 tidak boleh pakai menu ini (Harus Year End Menu D)
            if (month < 1 || month >= 12)
                throw new Exception("Closing Bulanan hanya untuk bulan 1 s/d 11.\nUntuk Akhir Tahun (Bulan 12), gunakan Menu D.");

            int nextMonth = month + 1;

            using (var cn = Db.Open())
            {
                using (var trans = cn.BeginTransaction())
                {
                    try
                    {
                        // 1. Ambil Master COA
                        // Pastikan Grp (AccountGroup) terambil
                        var allCoa = (await cn.QueryAsync<Coa>("SELECT * FROM Coa")).ToDictionary(x => x.Code3);

                        // 2. Ambil Saldo Bulan INI (Sumber)
                        var sourceBalances = await cn.QueryAsync<CoaBalance>(@"
                            SELECT * FROM CoaBalance 
                            WHERE Year=@y AND Month=@m",
                            new { y = year, m = month }, trans);

                        foreach (var b in sourceBalances)
                        {
                            if (!allCoa.ContainsKey(b.Code3)) continue;
                            var coa = allCoa[b.Code3];

                            // 3. Hitung Saldo Akhir Bulan Ini (Ending Balance)
                            decimal saldoAkhirBulanIni = 0;

                            // Hitung berdasarkan Normal Debet/Kredit
                            if (coa.Type == AccountType.Debit)
                                saldoAkhirBulanIni = b.Saldo + b.Debet - b.Kredit;
                            else
                                saldoAkhirBulanIni = b.Saldo + b.Kredit - b.Debet;

                            // 4. Tentukan Saldo Awal Bulan DEPAN
                            decimal saldoAwalBulanDepan = 0;

                            // --- LOGIC PERUBAHAN (RESET GROUP 4 & 5) ---
                            // Group 1 (Aktiva), 2 (Hutang), 3 (Modal) -> Saldo DIBAWA (Carry Forward)
                            // Group 4 (Pendapatan), 5 (Biaya) -> Saldo DI-RESET JADI 0 (Monthly Performance)

                            if (coa.Grp == AccountGroup.Pendapatan || coa.Grp == AccountGroup.Biaya)
                            {
                                // Reset jadi 0 supaya laporan bulan depan mulai dari nol
                                saldoAwalBulanDepan = 0;
                            }
                            else
                            {
                                // Akun Neraca: Lanjut terus
                                saldoAwalBulanDepan = saldoAkhirBulanIni;
                            }

                            // 5. Eksekusi Update ke Database
                            int affected = await cn.ExecuteAsync(@"
                                UPDATE CoaBalance SET Saldo = @s 
                                WHERE Code3=@c AND Year=@y AND Month=@nextM",
                                new { s = saldoAwalBulanDepan, c = b.Code3, y = year, nextM = nextMonth }, trans);

                            if (affected == 0)
                            {
                                // Kalau record bulan depan belum ada, Insert
                                await cn.ExecuteAsync(@"
                                    INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                                    VALUES (@c, @y, @nextM, @s, 0, 0)",
                                    new { c = b.Code3, y = year, nextM = nextMonth, s = saldoAwalBulanDepan }, trans);
                            }
                        }

                        trans.Commit();
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