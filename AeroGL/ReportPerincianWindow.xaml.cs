using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // Wajib untuk Async
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AeroGL.Data;
using System.Windows.Input;
using System.Printing;
using AeroGL.Core; // Sesuaikan jika perlu

namespace AeroGL
{
    public partial class ReportPerincianWindow : Window
    {
        private readonly CoaRepository _coaRepo;
        private readonly CoaBalanceRepository _balanceRepo;

        // --- DEFINISI WARNA (AeroGL Theme) ---
        private readonly SolidColorBrush _brushHeader = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE082")); // Gold
        private readonly SolidColorBrush _brushTotal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86F7A1"));  // Green
        private readonly SolidColorBrush _brushLabel = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9EEAF9"));  // Cyan

        private readonly SolidColorBrush _brushNegative;
        private readonly SolidColorBrush _brushText;
        // --- MAPPING PREFIX (Sesuai Logic DBF Legacy) ---
        private List<ReportSectionDef> _sections;

        public ReportPerincianWindow()
        {
            InitializeComponent();

            _brushNegative = (SolidColorBrush)Application.Current.Resources["GlobalNegativeBrush"];
            _brushText = (SolidColorBrush)Application.Current.Resources["GlobalTextBrush"] ?? Brushes.White;
            _coaRepo = new CoaRepository();
            _balanceRepo = new CoaBalanceRepository();

            // PANGGIL FUNGSI INISIALISASI BARU
            InitializeReportSections();

            LoadFilters();

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    // Cek agar tidak konflik jika user sedang memilih Combobox
                    if (!btnShow.IsKeyboardFocused)
                    {
                        BtnShow_Click(s, e);
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None)
                {
                    BtnPrint_Click(s, e);
                    e.Handled = true;
                }
            };
        }

        // --- FUNGSI BARU UNTUK BUILD SECTIONS SECARA DINAMIS ---
        private void InitializeReportSections()
        {
            // Helper kecil buat ambil prefix dari Config (misal "016.001.001" -> "016")
            string GetPrefix(string fullCode)
            {
                if (string.IsNullOrEmpty(fullCode)) return "???";
                var parts = fullCode.Split('.');
                return parts[0]; // Ambil bagian depan saja
            }

            // Ambil dari DB Config
            string pLabaDitahan = GetPrefix(AccountConfig.PrefixLabaDitahan);
            string pLabaBerjalan = GetPrefix(AccountConfig.PrefixLabaBerjalan);

            _sections = new List<ReportSectionDef>
            {
                new ReportSectionDef("KAS", "001", false),
                new ReportSectionDef("BANK", "002", false),
                new ReportSectionDef("PIUTANG DAGANG", "003", false),
                new ReportSectionDef("PERSEDIAAN BARANG", "004", false),
                new ReportSectionDef("PIUTANG KARYAWAN", "005", false),
                new ReportSectionDef("PAJAK DIBAYAR DIMUKA", "006", false),
                new ReportSectionDef("AKTIVA TETAP", "007", false),
                new ReportSectionDef("AKUMULASI PENYUSUTAN", "008", true),
                new ReportSectionDef("HUTANG DAGANG", "010", true),
                new ReportSectionDef("HUTANG BANK", "011", true),
                new ReportSectionDef("HUTANG LAIN-LAIN", "012", true),
                new ReportSectionDef("BIAYA YMH DIBAYAR", "013", true),
                new ReportSectionDef("PAJAK YMH DIBAYAR", "014", true),
                
                // Modal Disetor tetap Hardcode 015 (sesuai request legacy)
                new ReportSectionDef("MODAL", "015", true),

                // === BAGIAN INI JADI DINAMIS ===
                new ReportSectionDef("LABA (RUGI)", pLabaDitahan, true),
                new ReportSectionDef("LABA (RUGI) BERJALAN", pLabaBerjalan, true), 
                // ===============================

                new ReportSectionDef("PENJUALAN", "020", true),
                new ReportSectionDef("HARGA POKOK PENJUALAN", "021", false),
                new ReportSectionDef("BIAYA PENJUALAN", "022", false),
                new ReportSectionDef("BIAYA UMUM & ADM", "023", false),
                new ReportSectionDef("PENDAPATAN LAIN-LAIN", "025", true),
                new ReportSectionDef("BIAYA LAIN-LAIN", "026", false),
            };
        }

        private void LoadFilters()
        {
            int currentYear = DateTime.Now.Year;
            for (int i = currentYear - 5; i <= currentYear + 1; i++)
                cbYear.Items.Add(i);

            cbYear.SelectedItem = currentYear;

            string[] months = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            for (int i = 0; i < 12; i++)
            {
                if (!string.IsNullOrEmpty(months[i]))
                    cbMonth.Items.Add($"{i + 1:00} - {months[i]}");
            }
            cbMonth.SelectedIndex = DateTime.Now.Month - 1;
        }

        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            if (cbYear.SelectedItem == null || cbMonth.SelectedItem == null)
            {
                MessageBox.Show("Pilih Tahun dan Bulan dulu.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                btnShow.IsEnabled = false;
                txtStatus.Text = "Sedang mengambil data...";

                int year = (int)cbYear.SelectedItem;
                int month = cbMonth.SelectedIndex + 1;
                bool isYtd = cbType.SelectedIndex == 1;

                await GenerateReport(year, month, isYtd);

                txtStatus.Text = "Data berhasil dimuat.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal load report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Error.";
            }
            finally
            {
                btnShow.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            // Validasi sederhana: Cek apakah ada blok di dokumen selain judul default
            if (reportDoc.Blocks.Count <= 1)
            {
                MessageBox.Show("Belum ada data untuk dicetak. Silakan tampilkan data terlebih dahulu.",
                                "Print Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PrintDialog dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                // SIMPAN SETTING ASLI (PENTING!)
                // Kita harus mengubah ukuran dokumen sementara agar pas di kertas printer,
                // lalu mengembalikannya agar tampilan di layar tidak rusak.
                var originalPageWidth = reportDoc.PageWidth;
                var originalPageHeight = reportDoc.PageHeight;
                var originalColumnWidth = reportDoc.ColumnWidth;
                var originalPadding = reportDoc.PagePadding;

                try
                {
                    // Atur ukuran kertas sesuai pilihan printer
                    reportDoc.PageWidth = dlg.PrintableAreaWidth;
                    reportDoc.PageHeight = dlg.PrintableAreaHeight;

                    // ColumnWidth harus infinity biar ga jadi koran kecil saat diprint
                    reportDoc.ColumnWidth = double.PositiveInfinity;

                    // Padding cetak
                    reportDoc.PagePadding = new Thickness(40);

                    IDocumentPaginatorSource idp = reportDoc;
                    dlg.PrintDocument(idp.DocumentPaginator, "Laporan Perincian");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal mencetak: {ex.Message}", "Error Print", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // KEMBALIKAN SETTING ASLI
                    reportDoc.PageWidth = originalPageWidth;
                    reportDoc.PageHeight = originalPageHeight;
                    reportDoc.ColumnWidth = originalColumnWidth;
                    reportDoc.PagePadding = originalPadding;
                }
            }
        }
        // --- CORE LOGIC (FINAL FIX: FORMULA BY ACCOUNT TYPE) ---
        private async Task GenerateReport(int year, int month, bool isYtd)
        {
            reportDoc.Blocks.Clear();

            // 1. Fetch Data (Optimized)
            var taskCoa = _coaRepo.All();
            var taskBal = _balanceRepo.GetByYear(year);

            await Task.WhenAll(taskCoa, taskBal);

            var allCoas = (await taskCoa).OrderBy(c => c.Code3).ToList();
            var rawBalances = await taskBal;

            // Dictionary untuk akses cepat
            var balanceLookup = rawBalances.ToLookup(b => b.Code3);

            // 2. Header Laporan
            Paragraph title = new Paragraph(new Run("LAPORAN PERINCIAN (SUBSIDIARY LEDGER)"))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = _brushLabel
            };
            reportDoc.Blocks.Add(title);

            string modeStr = isYtd ? "TAHUNAN (YTD / SAMPAI DENGAN)" : "BULANAN";
            Paragraph subTitle = new Paragraph(new Run($"Periode: {month:00}/{year} | Mode: {modeStr}"))
            {
                TextAlignment = TextAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = _brushText
            };
            reportDoc.Blocks.Add(subTitle);

            // 3. Setup Tabel
            Table table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(85) });  // Kode
            table.Columns.Add(new TableColumn { Width = new GridLength(280) }); // Nama
            table.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Awal
            table.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Debet
            table.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Kredit
            table.Columns.Add(new TableColumn { Width = new GridLength(130) }); // Akhir

            TableRowGroup rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);
            reportDoc.Blocks.Add(table);

            // Header Tabel
            TableRow headerRow = new TableRow { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333344")) };
            headerRow.Cells.Add(CreateHeaderCell("KODE"));
            headerRow.Cells.Add(CreateHeaderCell("NAMA AKUN"));
            headerRow.Cells.Add(CreateHeaderCell("SALDO AWAL", TextAlignment.Right));
            headerRow.Cells.Add(CreateHeaderCell("DEBET", TextAlignment.Right));
            headerRow.Cells.Add(CreateHeaderCell("KREDIT", TextAlignment.Right));
            headerRow.Cells.Add(CreateHeaderCell("SALDO AKHIR", TextAlignment.Right));
            rowGroup.Rows.Add(headerRow);

            // 4. Looping Section
            foreach (var section in _sections)
            {
                var sectionAccounts = allCoas.Where(c => c.Code3.StartsWith(section.Prefix)).ToList();
                if (!sectionAccounts.Any()) continue;

                List<TableRow> validRows = new List<TableRow>();
                decimal totalSectionAkhir = 0;

                foreach (var coa in sectionAccounts)
                {
                    decimal awal = 0;
                    decimal debet = 0;
                    decimal kredit = 0;

                    // Ambil data balance
                    var myBalances = balanceLookup[coa.Code3].ToList();

                    if (isYtd)
                    {
                        // --- MODE T (YTD) ---
                        // Awal = Saldo Awal Tahun (Bulan 1)
                        var firstMonth = myBalances.FirstOrDefault(b => b.Month == 1);
                        awal = firstMonth?.Saldo ?? 0;

                        // Mutasi = Sum dari Jan s/d Bulan Dipilih
                        var rangeBalances = myBalances.Where(b => b.Month >= 1 && b.Month <= month);
                        debet = rangeBalances.Sum(b => b.Debet);
                        kredit = rangeBalances.Sum(b => b.Kredit);
                    }
                    else
                    {
                        // --- MODE B (BULANAN) ---
                        var bal = myBalances.FirstOrDefault(b => b.Month == month);
                        awal = bal?.Saldo ?? 0;
                        debet = bal?.Debet ?? 0;
                        kredit = bal?.Kredit ?? 0;
                    }

                    // --- RUMUS SALDO AKHIR (THE GOLDEN FORMULA) ---
                    decimal akhir = 0;

                    if (section.IsCreditNormal)
                    {
                        // 🟢 FORMULA AKUN KREDIT (Hutang, Modal, Pendapatan)
                        // Awal + (Penambahan via Kredit) - (Pengurangan via Debet)
                        akhir = awal + kredit - debet;
                    }
                    else
                    {
                        // 🔵 FORMULA AKUN DEBET (Harta, Biaya)
                        // Awal + (Penambahan via Debet) - (Pengurangan via Kredit)
                        akhir = awal + debet - kredit;
                    }

                    // Clean UI: Hide jika akun mati
                    if (awal == 0 && debet == 0 && kredit == 0 && akhir == 0) continue;

                    totalSectionAkhir += akhir;

                    TableRow row = new TableRow();
                    row.Cells.Add(CreateTextCell(coa.Code3));
                    row.Cells.Add(CreateTextCell(coa.Name));

                    // Display apa adanya (karena sudah Absolute Positive dari DB)
                    row.Cells.Add(CreateAmountCell(awal));
                    row.Cells.Add(CreateAmountCell(debet));
                    row.Cells.Add(CreateAmountCell(kredit));
                    row.Cells.Add(CreateAmountCell(akhir));

                    validRows.Add(row);
                }

                if (validRows.Count > 0)
                {
                    // Section Title
                    TableRow titleRow = new TableRow();
                    titleRow.Cells.Add(new TableCell(new Paragraph(new Run(section.Title)))
                    {
                        ColumnSpan = 6,
                        FontWeight = FontWeights.Bold,
                        Foreground = _brushHeader,
                        Padding = new Thickness(0, 15, 0, 5),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = new SolidColorBrush(Colors.Gray)
                    });
                    rowGroup.Rows.Add(titleRow);

                    foreach (var r in validRows) rowGroup.Rows.Add(r);

                    // Section Footer
                    TableRow footerRow = new TableRow();
                    footerRow.Cells.Add(new TableCell(new Paragraph(new Run($"TOTAL {section.Title}")))
                    {
                        ColumnSpan = 5,
                        TextAlignment = TextAlignment.Right,
                        FontWeight = FontWeights.Bold,
                        Foreground = _brushTotal,
                        Padding = new Thickness(0, 5, 5, 0),
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        BorderBrush = new SolidColorBrush(Colors.Gray)
                    });

                    var pTotal = new Paragraph(new Run(totalSectionAkhir.ToString("#,##0.00")));
                    pTotal.TextAlignment = TextAlignment.Right; pTotal.FontWeight = FontWeights.Bold;
                    pTotal.Foreground = totalSectionAkhir < 0 ? _brushNegative : _brushTotal;

                    footerRow.Cells.Add(new TableCell(pTotal)
                    { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Colors.Gray) });

                    rowGroup.Rows.Add(footerRow);
                }
            }

            if (rowGroup.Rows.Count <= 1)
            {
                rowGroup.Rows.Add(new TableRow
                {
                    Cells = { new TableCell(new Paragraph(new Run("Tidak ada data."))
                { Foreground = Brushes.Gray }) { ColumnSpan = 6, TextAlignment = TextAlignment.Center, Padding = new Thickness(10) } }
                });
            }
        }

        // --- HELPERS ---
        private TableCell CreateHeaderCell(string text, TextAlignment align = TextAlignment.Left)
        {
            return new TableCell(new Paragraph(new Run(text)))
            {
                Padding = new Thickness(6),
                TextAlignment = align,
                Foreground = _brushLabel,
                FontWeight = FontWeights.Bold
            };
        }

        private TableCell CreateTextCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text)))
            {
                Padding = new Thickness(4, 2, 4, 2),
                Foreground = _brushText
            };
        }

        private TableCell CreateAmountCell(decimal value)
        {
            string text = value == 0 ? "-" : value.ToString("#,##0.00");
            var p = new Paragraph(new Run(text)) { TextAlignment = TextAlignment.Right };
            p.Foreground = value < 0 ? _brushNegative : _brushText;
            return new TableCell(p) { Padding = new Thickness(4, 2, 4, 2) };
        }

        // Structure Definition
        private class ReportSectionDef
        {
            public string Title { get; }
            public string Prefix { get; }
            public bool IsCreditNormal { get; }
            public ReportSectionDef(string title, string prefix, bool isCreditNormal)
            { Title = title; Prefix = prefix; IsCreditNormal = isCreditNormal; }
        }
    }
}