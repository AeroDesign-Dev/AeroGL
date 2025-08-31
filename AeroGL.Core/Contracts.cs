using System.Collections.Generic;
using System.Threading.Tasks;

namespace AeroGL.Core
{
    public enum AccountType { Debit = 0, Kredit = 1 }
    public enum AccountGroup { Aktiva = 1, Kewajiban = 2, Modal = 3, Pendapatan = 4, Beban = 5 }

    public sealed class Coa { public string Code3; public string Name; public int Type; public int Grp; }
    public sealed class CoaBalance { public string Code3; public int Year; public int Month; public decimal Saldo; public decimal Debet; public decimal Kredit; }
    public sealed class JournalHeader { public string NoTran; public string Tanggal; public string Memo; public string Type; public decimal TotalDebet; public decimal TotalKredit; }
    public sealed class JournalLine { public string NoTran; public string Code2; public string Side; public decimal Amount; public string Narration; }

    public interface ICoaRepository
    {
        Task<Coa> Get(string code3);
        Task Upsert(Coa c);
        Task<List<Coa>> All();
    }

    public interface ICoaBalanceRepository
    {
        Task<CoaBalance> Get(string code3, int year, int month);
        Task Upsert(CoaBalance b);
    }
}
