using AeroGL.Core;
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace AeroGL.Data
{
    public sealed class MonthlyClosingService
    {
        public async Task RunClosing(int year, int month)
        {
            if (month < 1 || month >= 12)
                throw new Exception("Closing Bulanan hanya untuk bulan 1 s/d 11. Gunakan Menu D untuk Akhir Tahun.");

            int nextMonth = month + 1;

            using (var cn = Db.Open())
            using (var trans = cn.BeginTransaction())
            {
                try
                {
                    // 1. Ambil Akun Laba Berjalan dari Config (Default 017)
                    string labaBerjalanCode = await cn.ExecuteScalarAsync<string>(
                        "SELECT Val FROM Config WHERE Key='LabaBerjalan'", null, trans) ?? "017";

                    // 2. Ambil Semua Master COA untuk mapping Group
                    var allCoa = (await cn.QueryAsync<Coa>("SELECT * FROM Coa")).ToDictionary(x => x.Code3);

                    // 3. Ambil Saldo Bulan Ini
                    var sourceBalances = (await cn.QueryAsync<CoaBalance>(@"
                        SELECT * FROM CoaBalance WHERE Year=@y AND Month=@m",
                        new { y = year, m = month }, trans)).ToList();

                    // VARIABEL UNTUK MENGHITUNG LABA BERSIH BULAN INI
                    decimal netProfitMonth = 0;

                    // 4. LOOPING PERTAMA: Hitung Laba Bersih & Proses Carry Forward Akun Biasa
                    foreach (var b in sourceBalances)
                    {
                        if (!allCoa.ContainsKey(b.Code3)) continue;
                        var coa = allCoa[b.Code3];

                        // Hitung Mutasi Bersih (P&L) atau Saldo Akhir (Neraca)
                        decimal mutasiDebet = b.Debet;
                        decimal mutasiKredit = b.Kredit;
                        decimal endingBalance = (coa.Type == AccountType.Debit)
                            ? b.Saldo + mutasiDebet - mutasiKredit
                            : b.Saldo + mutasiKredit - mutasiDebet;

                        // Jika Pendapatan (4) atau Biaya (5), tambahkan ke Net Profit
                        if (coa.Grp == AccountGroup.Pendapatan || coa.Grp == AccountGroup.Biaya)
                        {
                            // Rumus: (Kredit - Debet) untuk Pendapatan, kebalikannya untuk Biaya
                            // Tapi simpelnya: Selisih mutasi mempengaruhi Laba Berjalan
                            decimal movement = (coa.Type == AccountType.Debit) ? (mutasiDebet - mutasiKredit) : (mutasiKredit - mutasiDebet);

                            // Grp 4 (Kredit normal), Grp 5 (Debet normal). 
                            // Laba bertambah jika Kredit > Debet di Grp 4, berkurang jika Debet > Kredit di Grp 5.
                            if (coa.Grp == AccountGroup.Pendapatan) netProfitMonth += (mutasiKredit - mutasiDebet);
                            else netProfitMonth -= (mutasiDebet - mutasiKredit);
                        }

                        // Tentukan Saldo Awal Bulan Depan
                        decimal saldoAwalNext = 0;

                        // Akun Neraca (Kecuali Laba Berjalan karena akan diupdate khusus)
                        if (coa.Grp != AccountGroup.Pendapatan && coa.Grp != AccountGroup.Biaya && b.Code3 != labaBerjalanCode)
                        {
                            saldoAwalNext = endingBalance;
                            await UpsertBalance(cn, b.Code3, year, nextMonth, saldoAwalNext, trans);
                        }
                        // Reset Akun P&L jadi 0
                        else if (coa.Grp == AccountGroup.Pendapatan || coa.Grp == AccountGroup.Biaya)
                        {
                            await UpsertBalance(cn, b.Code3, year, nextMonth, 0, trans);
                        }
                    }

                    // 5. UPDATE KHUSUS: Akumulasi Laba Berjalan (017)
                    var currentLabaBal = sourceBalances.FirstOrDefault(x => x.Code3 == labaBerjalanCode);
                    decimal oldLabaSaldo = currentLabaBal?.Saldo ?? 0;
                    // Saldo Baru = Saldo Awal Bulan Ini + Profit Bulan Ini
                    decimal newLabaSaldo = oldLabaSaldo + netProfitMonth;

                    await UpsertBalance(cn, labaBerjalanCode, year, nextMonth, newLabaSaldo, trans);

                    trans.Commit();
                }
                catch (Exception)
                {
                    trans.Rollback();
                    throw;
                }
            }
        }

        private async Task UpsertBalance(IDbConnection cn, string code3, int y, int m, decimal s, IDbTransaction tx)
        {
            const string sql = @"
                INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                VALUES (@c, @y, @m, @s, 0, 0)
                ON CONFLICT(Code3, Year, Month) DO UPDATE SET Saldo = @s, Debet = 0, Kredit = 0;";
            await cn.ExecuteAsync(sql, new { c = code3, y = y, m = m, s = s }, tx);
        }
    }
}