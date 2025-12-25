using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input; // WAJIB ADA untuk Shortcut
using System.Windows.Media;
using AeroGL.Data;
using AeroGL.Core;

namespace AeroGL
{
    public partial class ReportNeracaPercobaanWindow : Window
    {
        private readonly CoaRepository _coaRepo;
        private readonly CoaBalanceRepository _balRepo;
        private readonly JournalLineRepository _journalRepo;

        // Cache data report terakhir untuk keperluan Reprint
        private List<NeracaRow> _lastReportData;
        private int _lastMonth;
        private int _lastYear;
        private bool _lastIsYtd;

        // Helper warna
        private readonly SolidColorBrush _brushText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAEAEA"));
        private readonly SolidColorBrush _brushHeader = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9EEAF9"));
        private readonly SolidColorBrush _brushLine = new SolidColorBrush(Colors.Gray);
        private readonly SolidColorBrush _brushBlack = new SolidColorBrush(Colors.Black);

        // Warna Merah Global (bisa ambil dari Resource kalau ada, atau hardcode Red)
        private readonly SolidColorBrush _brushNegative = Brushes.Red;

        private class NeracaRow
        {
            public string Code { get; set; }
            public string Name { get; set; }

            public decimal Awal_D { get; set; }
            public decimal Awal_K { get; set; }

            public decimal Mut_D { get; set; }
            public decimal Mut_K { get; set; }

            public decimal Adj_D { get; set; }
            public decimal Adj_K { get; set; }

            public decimal Akhir_D { get; set; }
            public decimal Akhir_K { get; set; }
        }

        public ReportNeracaPercobaanWindow()
        {
            InitializeComponent();
            _coaRepo = new CoaRepository();
            _balRepo = new CoaBalanceRepository();
            _journalRepo = new JournalLineRepository();

            LoadFilters();

            // === TAMBAHAN SHORTCUT ===
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    // Cek biar gak konflik kalau lagi pilih combobox
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

        private void LoadFilters()
        {
            int curYear = DateTime.Now.Year;
            for (int i = curYear - 5; i <= curYear + 1; i++) cbYear.Items.Add(i);
            cbYear.SelectedItem = curYear;

            var months = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            for (int i = 0; i < 12; i++)
                if (!string.IsNullOrEmpty(months[i])) cbMonth.Items.Add($"{i + 1:00} - {months[i]}");
            cbMonth.SelectedIndex = DateTime.Now.Month - 1;
        }

        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            await GenerateReport();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_lastReportData == null || _lastReportData.Count == 0)
            {
                MessageBox.Show("Tampilkan data terlebih dahulu sebelum print.");
                return;
            }

            PrintDialog pd = new PrintDialog();
            if (pd.ShowDialog() == true)
            {
                pd.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;

                var printDoc = new FlowDocument
                {
                    PageHeight = pd.PrintableAreaHeight,
                    PageWidth = pd.PrintableAreaWidth,
                    PagePadding = new Thickness(40),
                    ColumnWidth = double.PositiveInfinity,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11
                };

                RenderContentToDoc(printDoc, _lastReportData, _lastMonth, _lastYear, _lastIsYtd, true);
                pd.PrintDocument(((IDocumentPaginatorSource)printDoc).DocumentPaginator, "Neraca Percobaan AeroGL");
            }
        }

        private async Task GenerateReport()
        {
            if (cbYear.SelectedItem == null || cbMonth.SelectedItem == null) return;

            try
            {
                btnShow.IsEnabled = false;
                txtStatus.Text = "Memproses data...";

                int year = (int)cbYear.SelectedItem;
                int month = cbMonth.SelectedIndex + 1;
                bool isYtd = cbMode.SelectedIndex == 1;

                _lastYear = year; _lastMonth = month; _lastIsYtd = isYtd;

                DateTime dStart, dEnd;
                int saldoAwalMonth;

                if (isYtd)
                {
                    dStart = new DateTime(year, 1, 1);
                    dEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                    saldoAwalMonth = 0;
                }
                else
                {
                    dStart = new DateTime(year, month, 1);
                    dEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                    saldoAwalMonth = month - 1;
                }

                var taskCoa = _coaRepo.All();
                var taskBal = _balRepo.GetByYear(year);
                var taskMut = _journalRepo.GetTrialBalanceSummary(dStart, dEnd);

                await Task.WhenAll(taskCoa, taskBal, taskMut);

                var allCoa = (await taskCoa).OrderBy(c => c.Code3).ToList();
                var allBal = await taskBal;
                var allMut = await taskMut;
                var mutLookup = allMut.ToLookup(m => m.Code);

                List<NeracaRow> rows = new List<NeracaRow>();

                foreach (var coa in allCoa)
                {
                    // A. SALDO AWAL
                    var balRec = allBal.FirstOrDefault(b => b.Code3 == coa.Code3 && b.Month == saldoAwalMonth);
                    decimal rawAwal = balRec?.Saldo ?? 0;
                    decimal awal_D = 0, awal_K = 0;

                    if (coa.Type == AccountType.Debit) awal_D = rawAwal;
                    else awal_K = rawAwal;

                    // B. MUTASI
                    var mutsExact = mutLookup[coa.Code3].ToList();
                    List<TrialBalanceMutation> mutsAlias = new List<TrialBalanceMutation>();
                    if (coa.Code3.EndsWith(".001"))
                    {
                        string shortCode = coa.Code3.Substring(0, coa.Code3.Length - 4);
                        mutsAlias = mutLookup[shortCode].ToList();
                    }
                    var myMuts = mutsExact.Concat(mutsAlias).ToList();

                    decimal mJ_D = myMuts.Where(m => m.JournalType == "J" && m.Side == "D").Sum(x => x.TotalAmount);
                    decimal mJ_K = myMuts.Where(m => m.JournalType == "J" && m.Side == "K").Sum(x => x.TotalAmount);

                    decimal mAdj_D = myMuts.Where(m => m.JournalType == "M" && m.Side == "D").Sum(x => x.TotalAmount);
                    decimal mAdj_K = myMuts.Where(m => m.JournalType == "M" && m.Side == "K").Sum(x => x.TotalAmount);

                    // C. SALDO AKHIR
                    decimal sumDebet = awal_D + mJ_D + mAdj_D;
                    decimal sumKredit = awal_K + mJ_K + mAdj_K;
                    decimal akhir_D = 0, akhir_K = 0;

                    if (sumDebet > sumKredit) akhir_D = sumDebet - sumKredit;
                    else if (sumKredit > sumDebet) akhir_K = sumKredit - sumDebet;

                    // Filter Zero
                    if (sumDebet == 0 && sumKredit == 0) continue;

                    rows.Add(new NeracaRow
                    {
                        Code = coa.Code3,
                        Name = coa.Name,
                        Awal_D = awal_D,
                        Awal_K = awal_K,
                        Mut_D = mJ_D,
                        Mut_K = mJ_K,
                        Adj_D = mAdj_D,
                        Adj_K = mAdj_K,
                        Akhir_D = akhir_D,
                        Akhir_K = akhir_K
                    });
                }

                _lastReportData = rows;
                reportDoc.Blocks.Clear();
                RenderContentToDoc(reportDoc, rows, month, year, isYtd, false);

                txtStatus.Text = $"Selesai. {rows.Count} rekening ditampilkan.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                txtStatus.Text = "Error.";
            }
            finally { btnShow.IsEnabled = true; }
        }

        private void RenderContentToDoc(FlowDocument doc, List<NeracaRow> rows, int month, int year, bool isYtd, bool isPrint)
        {
            var brushTxt = isPrint ? _brushBlack : _brushText;
            var brushHead = isPrint ? _brushBlack : _brushHeader;
            var brushBorder = isPrint ? _brushBlack : new SolidColorBrush(Color.FromRgb(60, 60, 80));
            var cellBorderThickness = new Thickness(0, 0, 1, 1);

            Paragraph pHead = new Paragraph { TextAlignment = TextAlignment.Center };
            pHead.Inlines.Add(new Run("NERACA PERCOBAAN (10 KOLOM)\n") { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = brushHead });
            string perStr = isYtd ? "Januari s/d " : "";
            pHead.Inlines.Add(new Run($"Periode: {perStr}{month:00}/{year}") { FontSize = 12, Foreground = brushTxt });
            doc.Blocks.Add(pHead);

            Table table = new Table { CellSpacing = 0, BorderBrush = brushBorder, BorderThickness = new Thickness(1, 1, 0, 0) };

            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(160) });
            for (int i = 0; i < 8; i++) table.Columns.Add(new TableColumn { Width = new GridLength(120) });

            TableRowGroup rg = new TableRowGroup();
            table.RowGroups.Add(rg);
            doc.Blocks.Add(table);

            // --- HEADER 1 ---
            TableRow hGroup = new TableRow { Background = isPrint ? Brushes.LightGray : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333344")) };

            hGroup.Cells.Add(new TableCell(new Paragraph(new Run(""))) { ColumnSpan = 2, BorderBrush = brushBorder, BorderThickness = cellBorderThickness });

            void AddGrp(string t)
            {
                hGroup.Cells.Add(new TableCell(new Paragraph(new Run(t)))
                {
                    ColumnSpan = 2,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = brushHead,
                    BorderThickness = cellBorderThickness,
                    BorderBrush = brushBorder
                });
            }
            AddGrp("SALDO AWAL"); AddGrp("MUTASI"); AddGrp("ADJUSTMENT"); AddGrp("SALDO AKHIR");
            rg.Rows.Add(hGroup);

            // --- HEADER 2 ---
            TableRow hSub = new TableRow { Background = isPrint ? Brushes.LightGray : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333344")) };

            void AddSub(string t, bool center = false)
            {
                hSub.Cells.Add(new TableCell(new Paragraph(new Run(t)))
                {
                    Padding = new Thickness(5, 2, 5, 5),
                    FontWeight = FontWeights.Bold,
                    Foreground = brushHead,
                    TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
                    BorderThickness = cellBorderThickness,
                    BorderBrush = brushBorder
                });
            }
            void AddDK() { AddSub("Debet", true); AddSub("Kredit", true); }

            AddSub("KODE"); AddSub("NAMA REKENING");
            AddDK(); AddDK(); AddDK(); AddDK();
            rg.Rows.Add(hSub);

            // --- DATA ROWS ---
            decimal tAwD = 0, tAwK = 0, tMD = 0, tMK = 0, tAdD = 0, tAdK = 0, tAkD = 0, tAkK = 0;

            foreach (var r in rows)
            {
                tAwD += r.Awal_D; tAwK += r.Awal_K;
                tMD += r.Mut_D; tMK += r.Mut_K;
                tAdD += r.Adj_D; tAdK += r.Adj_K;
                tAkD += r.Akhir_D; tAkK += r.Akhir_K;

                TableRow row = new TableRow();

                void AddCell(string t) => row.Cells.Add(new TableCell(new Paragraph(new Run(t)))
                {
                    Padding = new Thickness(2),
                    Foreground = brushTxt,
                    BorderThickness = cellBorderThickness,
                    BorderBrush = brushBorder
                });

                void AddNum(decimal d)
                {
                    // FIX FORMAT ANGKA: Pake #,##0.00 biar ada koma
                    string s = d == 0 ? "-" : d.ToString("#,##0.00");
                    var p = new Paragraph(new Run(s)) { TextAlignment = TextAlignment.Right };

                    if (d < 0) p.Foreground = _brushNegative;
                    else p.Foreground = brushTxt;

                    if (isPrint) p.Foreground = Brushes.Black; // Kalau print selalu hitam (kecuali mau merah di kertas warna)

                    row.Cells.Add(new TableCell(p)
                    {
                        Padding = new Thickness(2),
                        BorderThickness = cellBorderThickness,
                        BorderBrush = brushBorder
                    });
                }

                AddCell(r.Code); AddCell(r.Name);
                AddNum(r.Awal_D); AddNum(r.Awal_K);
                AddNum(r.Mut_D); AddNum(r.Mut_K);
                AddNum(r.Adj_D); AddNum(r.Adj_K);
                AddNum(r.Akhir_D); AddNum(r.Akhir_K);
                rg.Rows.Add(row);
            }

            // --- TOTAL ROW ---
            TableRow tRow = new TableRow { Background = isPrint ? Brushes.LightGray : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222233")) };

            tRow.Cells.Add(new TableCell(new Paragraph(new Run("TOTAL")) { FontWeight = FontWeights.Bold, Foreground = brushHead, TextAlignment = TextAlignment.Center })
            {
                ColumnSpan = 2,
                Padding = new Thickness(5),
                BorderThickness = cellBorderThickness,
                BorderBrush = brushBorder
            });

            void AddTot(decimal d)
            {
                // FIX FORMAT ANGKA TOTAL JUGA
                var run = new Run(d.ToString("#,##0.00")) { FontWeight = FontWeights.Bold, Foreground = brushHead };
                if (isPrint) run.Foreground = Brushes.Black;

                tRow.Cells.Add(new TableCell(new Paragraph(run))
                {
                    Padding = new Thickness(2),
                    TextAlignment = TextAlignment.Right,
                    BorderThickness = cellBorderThickness,
                    BorderBrush = brushBorder
                });
            }

            AddTot(tAwD); AddTot(tAwK);
            AddTot(tMD); AddTot(tMK);
            AddTot(tAdD); AddTot(tAdK);
            AddTot(tAkD); AddTot(tAkK);
            rg.Rows.Add(tRow);
        }
    }
}