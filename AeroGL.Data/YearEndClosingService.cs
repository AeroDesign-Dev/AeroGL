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
            using (var trans = cn.BeginTransaction())
            {
                try
                {
                    string code016 = await cn.ExecuteScalarAsync<string>("SELECT Val FROM Config WHERE Key='LabaDitahan'", null, trans) ?? "016";
                    string code017 = await cn.ExecuteScalarAsync<string>("SELECT Val FROM Config WHERE Key='LabaBerjalan'", null, trans) ?? "017";

                    var allCoas = (await cn.QueryAsync<Coa>("SELECT * FROM Coa")).ToList();
                    var decBalances = (await cn.QueryAsync<CoaBalance>("SELECT * FROM CoaBalance WHERE Year=@y AND Month=12", new { y = year }, trans)).ToDictionary(x => x.Code3);

                    decBalances.TryGetValue(code017, out var row017);
                    decimal profitAccumulated = row017 != null ? (decimal)(row017.Saldo + row017.Kredit - row017.Debet) : 0m;

                    foreach (var coa in allCoas)
                    {
                        decBalances.TryGetValue(coa.Code3, out var b);
                        decimal sOld = (decimal)(b?.Saldo ?? 0);
                        decimal dOld = (decimal)(b?.Debet ?? 0);
                        decimal kOld = (decimal)(b?.Kredit ?? 0);

                        decimal endingBal = (coa.Type == AccountType.Debit) ? sOld + dOld - kOld : sOld + kOld - dOld;

                        decimal finalVal = 0;
                        if (coa.Grp == AccountGroup.Pendapatan || coa.Grp == AccountGroup.Biaya || coa.Code3 == code017)
                        {
                            finalVal = 0; // Reset P&L dan Laba Berjalan
                        }
                        else if (coa.Code3 == code016)
                        {
                            finalVal = endingBal + profitAccumulated; // Setor Laba ke Ditahan
                        }
                        else
                        {
                            finalVal = endingBal; // Carry forward Neraca (Kas, dkk)
                        }

                        // Update Month 0 (Opening) DAN Month 1 (Januari)
                        await UpsertBalance(cn, coa.Code3, nextYear, 0, finalVal, trans);
                        await UpsertBalance(cn, coa.Code3, nextYear, 1, finalVal, trans);
                    }
                    trans.Commit();
                }
                catch { trans.Rollback(); throw; }
            }
        }

        private async Task UpsertBalance(System.Data.IDbConnection cn, string code3, int y, int m, decimal val, System.Data.IDbTransaction tx)
        {
            const string sql = @"
        INSERT INTO CoaBalance (Code3, Year, Month, Saldo, Debet, Kredit)
        VALUES (@c, @y, @m, @v, 0, 0)
        ON CONFLICT(Code3, Year, Month) DO UPDATE SET Saldo = @v, Debet = 0, Kredit = 0;";
            await cn.ExecuteAsync(sql, new { c = code3, y = y, m = m, v = val }, tx);
        }
    }
}