using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AeroGL.Core
{
    public enum AccountType { Debit = 0, Kredit = 1 }
    public enum AccountGroup { Aktiva = 1, Hutang = 2, Modal = 3, Pendapatan = 4, Biaya = 5 }

    public sealed class Coa : INotifyPropertyChanged
    {
        private string _code3;
        public string Code3 { get => _code3; set { _code3 = value; OnPropertyChanged(); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private AccountType _type;
        public AccountType Type { get => _type; set { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeText)); } }

        private AccountGroup _grp;
        public AccountGroup Grp { get => _grp; set { _grp = value; OnPropertyChanged(); OnPropertyChanged(nameof(GrpText)); } }

        // Untuk tampilan: "Debit (0)" / "Aktiva (1)" dst.
        public string TypeText => string.Format("{0} ({1})", Type, (int)Type);
        public string GrpText => string.Format("{0} ({1})", Grp, (int)Grp);


        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

     public sealed class CoaBalance { public string Code3; public int Year; public int Month; public decimal Saldo; public decimal Debet; public decimal Kredit; }

    public sealed class JournalHeader { public string NoTran; public string Tanggal; public string Memo; public string Type; public decimal TotalDebet; public decimal TotalKredit; }
     
     public sealed class JournalLine { public string NoTran; public string Code2; public string Side; public decimal Amount; public string Narration; }


    public interface ICoaRepository
    {
        Task<Coa> Get(string code3);
        Task Upsert(Coa c);
        Task<List<Coa>> All();

        Task Delete(string code3);
        Task<List<Coa>> Search(string q); // cari by code3/name contains
    }

    public interface ICoaBalanceRepository
    {
        Task<CoaBalance> Get(string code3, int year, int month);
        Task Upsert(CoaBalance b);

        // Method lama (per akun)
        Task<List<CoaBalance>> ListByYear(string code3, int year);

        // [WAJIB DITAMBAHKAN] Method baru untuk mengambil SEMUA akun dalam 1 tahun
        Task<List<CoaBalance>> GetByYear(int year);

        Task<List<int>> YearsAvailable(string code3);

        Task<decimal> GetOpeningBalance(string code3, int year, int month);
    }

    public interface IJournalHeaderRepository
    {
        Task<JournalHeader> Get(string noTran);
        Task Upsert(JournalHeader h);      // insert/update
        Task Delete(string noTran);

        Task<List<JournalHeader>> All();          // NEW
        Task<List<JournalHeader>> Search(string q);
    }

    public sealed class JournalLineRecord
    {
        public long Id { get; set; }
        public string NoTran { get; set; }

        public string Tanggal { get; set; }

        public string Code2 { get; set; }
        public string Side { get; set; }
        public decimal Amount { get; set; }
        public string Narration { get; set; }
    }

    public interface IJournalLineRepository
    {
        Task<List<JournalLineRecord>> ListByNoTran(string noTran);
        Task<JournalLineRecord> GetById(long id);
        Task<long> Insert(JournalLine line);
        Task UpdateById(long id, JournalLine lineNew);
        Task DeleteByNoTran(string noTran);

        // [BARU] Method buat laporan buku besar
        Task<decimal> GetMutationSum(string code2, System.DateTime start, System.DateTime end);
        Task<List<JournalLineRecord>> GetLedgerModeM(string code2, System.DateTime start, System.DateTime end);
        Task<List<JournalLineRecord>> GetLedgerModeN(string code2, System.DateTime start, System.DateTime end);

        Task<List<TrialBalanceMutation>> GetTrialBalanceSummary(System.DateTime start, System.DateTime end);
    }

    public interface IAliasRepository
    {
        Task<string> ResolveCode3(string code2); // alias or code2+".001"
    }

    public class TrialBalanceMutation
    {
        public string Code { get; set; }
        public string JournalType { get; set; } // Diisi 'J' atau 'M' dari Header
        public string Side { get; set; }        // 'D' atau 'K'
        public decimal TotalAmount { get; set; }
    }
}
