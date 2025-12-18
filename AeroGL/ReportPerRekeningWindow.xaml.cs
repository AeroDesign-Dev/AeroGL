using AeroGL.Core;
using AeroGL.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AeroGL
{
    public partial class ReportPerRekeningWindow : Window
    {
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        private readonly ICoaBalanceRepository _balRepo = new CoaBalanceRepository();
        private readonly IJournalLineRepository _lineRepo = new JournalLineRepository();

        public ReportPerRekeningWindow()
        {
            InitializeComponent();

            // Default Tanggal: Awal Bulan ini s/d Hari ini
            var now = DateTime.Today;
            DpFrom.SelectedDate = new DateTime(now.Year, now.Month, 1);
            DpTo.SelectedDate = now;
        }

        // ==================== LOGIC UTAMA ====================
        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            if (DpFrom.SelectedDate == null || DpTo.SelectedDate == null) return;

            DateTime tglDari = DpFrom.SelectedDate.Value;
            DateTime tglSampai = DpTo.SelectedDate.Value;
            string akun1 = (TxtAkun1.Text ?? "").Trim();
            string akun2 = (TxtAkun2.Text ?? "zzzzzzz").Trim();
            bool isModeN = CboMode.SelectedIndex == 1; // 0=M, 1=N

            if (string.IsNullOrEmpty(akun2)) akun2 = "zzzzzzz";

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                TxtInfo.Text = "Sedang memproses data...";
                GridRows.ItemsSource = null;

                // 1. Ambil Akun yang masuk range
                //    (Filter string sederhana 2-segmen)
                var allCoa = await _coaRepo.All();
                var targetCoa = allCoa.Where(c =>
                    IsInRange(GetCode2(c.Code3), akun1, akun2)
                ).OrderBy(c => c.Code3).ToList();

                var reportRows = new List<LedgerRow>();

                // Variabel GRAND TOTAL (Akumulasi jalan terus antar akun)
                decimal grandTotalDebet = 0;
                decimal grandTotalKredit = 0;

                // 2. LOOPING PER AKUN
                foreach (var akun in targetCoa)
                {
                    string code2 = GetCode2(akun.Code3);

                    // --- A. Hitung Saldo Awal Murni (RESET per akun) ---
                    //    Rumus: SaldoAwalBulanIni + Mutasi(Tgl 1 s/d H-1)

                    decimal saldoAwalBulan = await _balRepo.GetOpeningBalance(akun.Code3, tglDari.Year, tglDari.Month);

                    // Koreksi Harian (jika start bukan tgl 1, atau ambil delta mutasi tgl 1 s/d start-1)
                    // Agar aman, kita hitung mutasi dari Tgl 1 bulan tsb s/d (TglDari - 1 hari)
                    DateTime tglAwalBulan = new DateTime(tglDari.Year, tglDari.Month, 1);
                    decimal koreksiHarian = 0;

                    if (tglDari > tglAwalBulan)
                    {
                        // Ambil mutasi D - K
                        koreksiHarian = await _lineRepo.GetMutationSum(code2, tglAwalBulan, tglDari.AddDays(-1));

                        // Sesuaikan tanda berdasarkan tipe akun
                        if (akun.Type == AccountType.Kredit) koreksiHarian = -koreksiHarian;
                    }

                    decimal saldoAwalFinal = saldoAwalBulan + koreksiHarian;

                    // --- B. Tarik Transaksi ---
                    List<JournalLineRecord> trans;
                    if (isModeN)
                        trans = await _lineRepo.GetLedgerModeN(code2, tglDari, tglSampai);
                    else
                        trans = await _lineRepo.GetLedgerModeM(code2, tglDari, tglSampai);

                    // --- C. Filter Tampil (Eligibility) ---
                    // Tampil jika: Saldo Awal != 0 ATAU ada transaksi
                    if (saldoAwalFinal == 0 && trans.Count == 0) continue;
                    //if (trans.Count == 0) continue;

                    // --- D. Bikin Header Akun di Grid ---
                    /*reportRows.Add(new LedgerRow
                    {
                        IsHeader = true,
                        Keterangan = $"REKENING : {akun.Code3} - {akun.Name}"
                    });

                    // Baris Saldo Awal
                    reportRows.Add(new LedgerRow
                    {
                        Keterangan = "SALDO AWAL",
                        Saldo = saldoAwalFinal,
                        IsSaldoRow = true
                    });*/
                    reportRows.Add(new LedgerRow
                    {
                        IsHeader = true,
                        Keterangan = $"REKENING : {akun.Code3} - {akun.Name}"
                    });

                    // [LOGIC BARU: SALDO AWAL ILANG JIKA GAK ADA TRANSAKSI]
                    // Hanya tampilkan baris "SALDO AWAL" kalau:
                    // 1. Saldonya bukan 0
                    // 2. DAN ada transaksi di periode ini (trans.Count > 0)
                    // Kalau gak ada transaksi, baris ini disembunyikan biar bersih kayak DOS.
                    if (saldoAwalFinal != 0 && trans.Count > 0)
                    {
                        reportRows.Add(new LedgerRow
                        {
                            Keterangan = "SALDO AWAL",
                            Saldo = saldoAwalFinal,
                            IsSaldoRow = true
                        });
                    }
                    // --- E. Loop Transaksi & Hitung Saldo Berjalan ---
                    decimal saldoBerjalan = saldoAwalFinal;
                    decimal subTotalDebet = 0;
                    decimal subTotalKredit = 0;

                    foreach (var tr in trans)
                    {
                        // 1. IDENTIFIKASI: Ini baris milik akun utama atau bukan?
                        bool isMyLine = (tr.Code2 == code2);

                        // ============================================================
                        // A. CALCULATOR (Hanya Hitung Punya Sendiri!)
                        // ============================================================
                        // Logic: Jangan pernah masukin angka 'Lawan' ke dalam kalkulator saldo/total.
                        if (isMyLine)
                        {
                            // Update Saldo Berjalan
                            if (akun.Type == AccountType.Debit) saldoBerjalan += (tr.Side == "D" ? tr.Amount : -tr.Amount);
                            else saldoBerjalan += (tr.Side == "K" ? tr.Amount : -tr.Amount);

                            // Update Total Bawah
                            subTotalDebet += (tr.Side == "D" ? tr.Amount : 0);
                            subTotalKredit += (tr.Side == "K" ? tr.Amount : 0);
                        }

                        // ============================================================
                        // B. DISPLAY FILTER (Siapa yang boleh tampil di Grid?)
                        // ============================================================
                        bool showRow = false;

                        if (isModeN)
                        {
                            // Mode N: Tampilkan SEMUA (Diri Sendiri + Lawan) biar lengkap kayak CCTV
                            // ATAU: Kalau mau persis aplikasi lama, tampilkan LAWAN saja (!isMyLine).
                            // Tapi amannya tampilkan semua dulu biar user tau konteksnya.
                            // Kalau lo mau hide diri sendiri di Mode N, ganti jadi: showRow = !isMyLine;

                            showRow = !isMyLine; // Sesuai request: "N gak boleh double visualnya"
                        }
                        else
                        {
                            // Mode M: Tampilkan Diri Sendiri saja
                            showRow = isMyLine;
                        }

                        // Kalau gak lolos sensor, skip.
                        if (!showRow) continue;

                        // ============================================================
                        // C. RENDER KE GRID
                        // ============================================================
                        reportRows.Add(new LedgerRow
                        {
                            // Parse Tanggal dengan aman
                            Tanggal = DateTime.TryParse(tr.Tanggal, out var t) ? t : DateTime.MinValue,

                            NoBukti = tr.NoTran,

                            // Di Mode N, tampilkan Kode Lawan di kolom "Perkiraan"
                            // Di Mode M, kosongin aja atau isi tr.Code2 (tapi isinya pasti akun sendiri)
                            Lawan = isModeN ? tr.Code2 : "",

                            Keterangan = tr.Narration,

                            // Tampilkan Nominal Asli baris tersebut (Biar tau lawannya nilai berapa)
                            Debet = (tr.Side == "D" ? tr.Amount : (decimal?)null),
                            Kredit = (tr.Side == "K" ? tr.Amount : (decimal?)null),

                            // TAPI Saldo Kanan Tetap Mengacu ke Saldo Akun Utama (Biar gak loncat-loncat aneh)
                            Saldo = saldoBerjalan
                        });
                    }

                    // --- F. Update Grand Total (Akumulatif) ---
                    grandTotalDebet += subTotalDebet;
                    grandTotalKredit += subTotalKredit;

                    // --- G. Footer Akun (Menampilkan Grand Total saat ini) ---
                    reportRows.Add(new LedgerRow
                    {
                        IsTotal = true,
                        Keterangan = "TOTAL S/D SAAT INI",
                        Debet = grandTotalDebet,
                        Kredit = grandTotalKredit,

                        // [LOGIC BARU SESUAI REQUEST]
                        // Kalau gak ada transaksi (trans.Count == 0), kolom Saldo di footer DIKOSONGKAN.
                        // (Sesuai gambar DOS lama: Saldo cuma muncul di baris Total kalau akunnya aktif bergerak)
                        Saldo = (trans.Count > 0) ? saldoBerjalan : (decimal?)null
                    });

                    // Spacer kosong biar enak dibaca
                    reportRows.Add(new LedgerRow());
                }

                GridRows.ItemsSource = reportRows;
                TxtInfo.Text = $"Selesai. Ditampilkan {reportRows.Count} baris.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Fitur Print ke Kertas akan diimplementasikan setelah Preview Grid OK.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            // Nanti kita copy logic FlowDocument dari report lain ke sini
        }

        // ==================== HELPER ====================

        // Ambil 2 segmen pertama dari xxx.xxx.xxx (001.001)
        private string GetCode2(string code3)
        {
            if (string.IsNullOrEmpty(code3)) return "";
            var parts = code3.Split('.');
            if (parts.Length >= 2) return $"{parts[0]}.{parts[1]}";
            return code3;
        }

        // Cek range string
        private bool IsInRange(string val, string start, string end)
        {
            return string.Compare(val, start) >= 0 && string.Compare(val, end) <= 0;
        }
    }

    // ==================== VIEW MODEL (Khusus Grid) ====================
    public class LedgerRow
    {
        public DateTime? Tanggal { get; set; }
        public string NoBukti { get; set; }
        public string Lawan { get; set; } // Kode Akun (Perkiraan)
        public string Keterangan { get; set; }

        public decimal? Debet { get; set; }
        public decimal? Kredit { get; set; }
        public decimal? Saldo { get; set; }

        public bool IsHeader { get; set; }
        public bool IsTotal { get; set; }
        public bool IsSaldoRow { get; set; } // Baris khusus saldo awal

        // Helper properties untuk display format
        public string TanggalStr => Tanggal?.ToString("dd-MM-yy") ?? "";
        public string DebetStr => FormatNum(Debet);
        public string KreditStr => FormatNum(Kredit);

        public string SaldoStr
        {
            get
            {
                if (Saldo == null) return "";
                // Format saldo negatif pakai kurung ()
                decimal val = Saldo.Value;
                string s = Math.Abs(val).ToString("#,##0.00", CultureInfo.GetCultureInfo("id-ID"));
                return val < 0 ? $"({s})" : s;
            }
        }

        private string FormatNum(decimal? val)
        {
            if (val == null || val == 0) return "";
            return val.Value.ToString("#,##0.00", CultureInfo.GetCultureInfo("id-ID"));
        }
    }
}