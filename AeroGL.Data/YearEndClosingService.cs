using System;
using System.Linq;
using System.Threading.Tasks;
using AeroGL.Core;
using Dapper;

namespace AeroGL.Data
{
    public class YearEndClosingService
    {
        public async Task RunYearEnd(int year)
        {
            int nextYear = year + 1;

            using (var cn = Db.Open())
            {
                using (var trans = cn.BeginTransaction())
                {
                    try
                    {
                        // ---------------------------------------------------------
                        // OPSI A: STRICT REPLICATION (DOS STYLE)
                        // ---------------------------------------------------------
                        // Logic:
                        // 1. Ambil Saldo Akhir Tahun (Bulan 12).
                        // 2. Akun P&L -> Di-RESET jadi 0 buat tahun depan.
                        // 3. Akun Neraca -> Di-COPY bulat-bulat (tanpa penambahan Laba).
                        // 4. Hasilnya: Laba tahun ini HILANG, Neraca Awal Tahun Depan SELISIH.

                        var allCoa = (await cn.QueryAsync<Coa>("SELECT * FROM Coa")).ToDictionary(x => x.Code3);

                        // Ambil Saldo Bulan 12
                        var decBalances = await cn.QueryAsync<CoaBalance>(@"
                            SELECT * FROM CoaBalance WHERE Year=@y AND Month=12",
                            new { y = year }, trans);

                        foreach (var b in decBalances)
                        {
                            if (!allCoa.ContainsKey(b.Code3)) continue;
                            var coa = allCoa[b.Code3];

                            // Hitung Saldo Akhir Tahun Ini
                            decimal saldoAkhirTahun = 0;
                            if (coa.Type == AccountType.Debit)
                                saldoAkhirTahun = b.Saldo + b.Debet - b.Kredit;
                            else
                                saldoAkhirTahun = b.Saldo + b.Kredit - b.Debet;

                            // Tentukan Saldo Awal Tahun Depan
                            decimal saldoAwalTahunDepan = 0;

                            if (coa.Grp == AccountGroup.Pendapatan || coa.Grp == AccountGroup.Biaya)
                            {
                                // REPLIKASI DOS: Reset P&L jadi 0.
                                // Laba/Rugi tahun ini menguap begitu saja.
                                saldoAwalTahunDepan = 0;
                            }
                            else
                            {
                                // REPLIKASI DOS: Copy Saldo Neraca apa adanya.
                                // Laba Ditahan (016) TIDAK DITAMBAH apapun. Tetap angka lama.
                                saldoAwalTahunDepan = saldoAkhirTahun;
                            }

                            // Simpan ke Bulan 0 (Opening Balance) Tahun Depan
                            var exists = await cn.ExecuteScalarAsync<int>(@"
                                SELECT COUNT(1) FROM CoaBalance WHERE Code3=@c AND Year=@ny AND Month=0",
                                new { c = b.Code3, ny = nextYear }, trans);

                            if (exists > 0)
                            {
                                await cn.ExecuteAsync(@"
                                    UPDATE CoaBalance SET Saldo = @s 
                                    WHERE Code3=@c AND Year=@ny AND Month=0",
                                    new { s = saldoAwalTahunDepan, c = b.Code3, ny = nextYear }, trans);
                            }
                            else
                            {
                                await cn.ExecuteAsync(@"
                                    INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
                                    VALUES (@c, @ny, 0, @s, 0, 0)",
                                    new { c = b.Code3, ny = nextYear, s = saldoAwalTahunDepan }, trans);
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