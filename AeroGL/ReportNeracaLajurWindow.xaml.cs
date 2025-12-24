using AeroGL.Core;
using AeroGL.Data;
using Dapper;
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
using System.Printing;

namespace AeroGL
{
    public partial class ReportNeracaLajurWindow : Window
    {
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        public string CompanyName { get; set; } // fallback: "Nama PT"

        // Cache for Print
        private List<TrialRow> _lastRows;
        private DateTime _lastD1;
        private DateTime _lastD2;

        // Styles for FlowDocument
        private readonly SolidColorBrush _brushText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAEAEA"));
        private readonly SolidColorBrush _brushHeader = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9EEAF9"));
        private readonly SolidColorBrush _brushBlack = Brushes.Black;

        public ReportNeracaLajurWindow()
        {
            InitializeComponent();
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            TxtYear.Text = DateTime.Today.Year.ToString();
            CboMonth.SelectedIndex = DateTime.Today.Month - 1;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { BtnShow_Click(s, e); e.Handled = true; }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None) { BtnPrint_Click(s, e); e.Handled = true; }
            };
        }

        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int year = int.TryParse(TxtYear.Text, out var y) ? y : DateTime.Today.Year;
                int month = CboMonth.SelectedIndex + 1;
                bool ytd = CboMode.SelectedIndex == 1;

                var (d1, d2) = MakeRange(year, month, ytd);
                _lastD1 = d1; _lastD2 = d2;

                Mouse.OverrideCursor = Cursors.Wait;
                TxtInfo.Text = "Menghitung…";
                BtnShow.IsEnabled = false;

                var rows = await BuildTrialBalance(d1, d2, ytd);
                _lastRows = rows;

                // RENDER TO SCREEN
                reportDoc.Blocks.Clear();
                RenderContentToDoc(reportDoc, rows, d1, d2, false); // false = screen mode

                TxtInfo.Text = $"{rows.Count} baris. Periode: {FormatPeriod(d1, d2)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtInfo.Text = "Error.";
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnShow.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_lastRows == null || _lastRows.Count == 0)
            {
                MessageBox.Show("Belum ada data ditampilkan.");
                return;
            }

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

                // Create temporary doc for printing
                var printDoc = new FlowDocument
                {
                    PageHeight = dlg.PrintableAreaHeight,
                    PageWidth = dlg.PrintableAreaWidth,
                    PagePadding = new Thickness(36),
                    ColumnWidth = double.PositiveInfinity,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11
                };

                // Render to print doc (true = print mode/white background)
                RenderContentToDoc(printDoc, _lastRows, _lastD1, _lastD2, true);

                dlg.PrintDocument(((IDocumentPaginatorSource)printDoc).DocumentPaginator, "Neraca Lajur");
            }
        }

        // =================================================================================
        // RENDERING LOGIC (Mirip Neraca Percobaan Style)
        // =================================================================================
        private void RenderContentToDoc(FlowDocument doc, IList<TrialRow> items, DateTime d1, DateTime d2, bool isPrint)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");

            // Colors
            var brushTxt = isPrint ? _brushBlack : _brushText;
            var brushHead = isPrint ? _brushBlack : _brushHeader;
            var brushBorder = isPrint ? _brushBlack : new SolidColorBrush(Color.FromRgb(60, 60, 80)); // Grid line color
            var borderThick = new Thickness(0, 0, 1, 1); // Right & Bottom border

            // 1. Header Text
            string nama = string.IsNullOrWhiteSpace(CompanyName) ? "Nama PT" : CompanyName;

            Paragraph pHead = new Paragraph { TextAlignment = TextAlignment.Center };
            pHead.Inlines.Add(new Run(nama + "\n") { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = brushHead });
            pHead.Inlines.Add(new Run("NERACA LAJUR (WORKSHEET)\n") { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = brushHead });
            pHead.Inlines.Add(new Run(FormatPeriod(d1, d2)) { FontSize = 12, Foreground = brushTxt });
            doc.Blocks.Add(pHead);

            // 2. Table Setup
            var table = new Table { CellSpacing = 0, BorderBrush = brushBorder, BorderThickness = new Thickness(1, 1, 0, 0) };
            doc.Blocks.Add(table);

            // Columns (No, Name, 6 Data Columns, Type)
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });  // Kode
            table.Columns.Add(new TableColumn { Width = new GridLength(180) }); // Nama
            for (int i = 0; i < 6; i++) table.Columns.Add(new TableColumn { Width = new GridLength(100) }); // Angka
            table.Columns.Add(new TableColumn { Width = new GridLength(30) });  // Tipe

            var rg = new TableRowGroup();
            table.RowGroups.Add(rg);

            // 3. Header Row 1 (Grouping)
            var hGroup = new TableRow { Background = isPrint ? Brushes.LightGray : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333344")) };

            // Spacer for Kode & Nama
            hGroup.Cells.Add(new TableCell(new Paragraph(new Run(""))) { ColumnSpan = 2, BorderBrush = brushBorder, BorderThickness = borderThick });

            void AddGrp(string t)
            {
                hGroup.Cells.Add(new TableCell(new Paragraph(new Run(t)))
                {
                    ColumnSpan = 2,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = brushHead,
                    BorderBrush = brushBorder,
                    BorderThickness = borderThick
                });
            }
            AddGrp("BALANCE");
            AddGrp("LABA RUGI");
            AddGrp("NERACA");

            // Spacer for Type
            hGroup.Cells.Add(new TableCell(new Paragraph(new Run(""))) { BorderBrush = brushBorder, BorderThickness = borderThick });

            rg.Rows.Add(hGroup);

            // 4. Header Row 2 (Sub Titles)
            var hSub = new TableRow { Background = isPrint ? Brushes.LightGray : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333344")) };

            void AddSub(string t, bool center = false)
            {
                hSub.Cells.Add(new TableCell(new Paragraph(new Run(t)))
                {
                    Padding = new Thickness(5, 2, 5, 5),
                    FontWeight = FontWeights.Bold,
                    Foreground = brushHead,
                    TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
                    BorderBrush = brushBorder,
                    BorderThickness = borderThick
                });
            }
            void AddDK() { AddSub("Debet", true); AddSub("Kredit", true); }

            AddSub("NOMOR");
            AddSub("NAMA REKENING");
            AddDK(); // Balance
            AddDK(); // LR
            AddDK(); // Neraca
            AddSub("T", true);
            rg.Rows.Add(hSub);

            // 5. Data Rows
            foreach (var r in items)
            {
                bool isBold = r.Name == "TOTAL" || r.Name == "SISA LABA / RUGI";

                var tr = new TableRow();
                if (isBold && !isPrint) tr.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222233"));

                void Cell(string s, TextAlignment align, bool bold = false)
                {
                    var p = new Paragraph(new Run(s)) { TextAlignment = align };
                    p.Foreground = brushTxt;
                    if (bold) { p.FontWeight = FontWeights.Bold; p.Foreground = brushHead; }
                    if (isPrint && bold) p.Foreground = Brushes.Black;

                    tr.Cells.Add(new TableCell(p)
                    {
                        Padding = new Thickness(4, 2, 4, 2),
                        BorderBrush = brushBorder,
                        BorderThickness = borderThick
                    });
                }

                void Num(decimal? v, bool bold = false)
                {
                    string s = v.HasValue && v.Value != 0 ? v.Value.ToString("#,##0.00", id) : "-";

                    var p = new Paragraph(new Run(s)) { TextAlignment = TextAlignment.Right };

                    // Logic warna merah
                    if (v.HasValue && v.Value < 0) p.Foreground = Brushes.Red; // Atau GlobalNegativeBrush
                    else p.Foreground = brushTxt;

                    if (bold) { p.FontWeight = FontWeights.Bold; if (v >= 0) p.Foreground = brushHead; }
                    if (isPrint) p.Foreground = Brushes.Black;

                    tr.Cells.Add(new TableCell(p)
                    {
                        Padding = new Thickness(4, 2, 4, 2),
                        BorderBrush = brushBorder,
                        BorderThickness = borderThick
                    });
                }

                Cell(r.Code3, TextAlignment.Left, isBold);
                Cell(r.Name, TextAlignment.Left, isBold);

                Num(r.BalD, isBold); Num(r.BalK, isBold);
                Num(r.LRD, isBold); Num(r.LRK, isBold);
                Num(r.NerD, isBold); Num(r.NerK, isBold);

                Cell(r.TypeLetter, TextAlignment.Center, isBold);

                rg.Rows.Add(tr);
            }

            // Footer Timestamp
            doc.Blocks.Add(new Paragraph(new Run($"Dicetak: {DateTime.Now:dd-MM-yyyy HH:mm}"))
            { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0) });
        }

        // =================================================================================
        // DATA LOGIC (UNCHANGED)
        // =================================================================================
        private (DateTime from, DateTime to) MakeRange(int year, int month, bool ytd)
        {
            var d1 = ytd ? new DateTime(year, 1, 1) : new DateTime(year, month, 1);
            var d2 = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            return (d1, d2);
        }

        private static string FormatPeriod(DateTime d1, DateTime d2)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");
            if (d1.Year == d2.Year && d1.Month == d2.Month) return d1.ToString("MMMM yyyy", id);
            if (d1.Year == d2.Year) return $"{d1.ToString("MMMM", id)} – {d2.ToString("MMMM yyyy", id)}";
            return $"{d1.ToString("MMMM yyyy", id)} – {d2.ToString("MMMM yyyy", id)}";
        }

        public sealed class TrialRow
        {
            public string Code3 { get; set; }
            public string Name { get; set; }
            public int Grp { get; set; }
            public int Type { get; set; }
            public string TypeLetter => Type == 0 ? "D" : "K";

            public decimal? BalD { get; set; }
            public decimal? BalK { get; set; }
            public decimal? LRD { get; set; }
            public decimal? LRK { get; set; }
            public decimal? NerD { get; set; }
            public decimal? NerK { get; set; }
        }

        private sealed class BalRow
        {
            public string Code3 { get; set; }
            public string Name { get; set; }
            public int Grp { get; set; }
            public int Type { get; set; }
            public double Saldo { get; set; }
            public double Debet { get; set; }
            public double Kredit { get; set; }
        }

        private async Task<List<TrialRow>> BuildTrialBalance(DateTime d1, DateTime d2, bool ytd)
        {
            int year = d2.Year;
            int mto = d2.Month;

            string norm(string col) =>
                $"(CASE WHEN typeof({col})='text' THEN 1.0 * CAST(REPLACE({col}, ',', '.') AS REAL) ELSE 1.0 * {col} END)";

            string sqlMonth = $@"
SELECT  c.Code3, c.Name, c.Grp, c.Type,
        COALESCE({norm("cb.Saldo")},  0.0) AS Saldo,
        COALESCE({norm("cb.Debet")},  0.0) AS Debet,
        COALESCE({norm("cb.Kredit")}, 0.0) AS Kredit
FROM Coa c
LEFT JOIN CoaBalance cb 
       ON cb.Code3=c.Code3 AND cb.Year=@y AND cb.Month=@m
WHERE c.Code3 LIKE '%.%.%'
ORDER BY c.Code3;";

            string sqlYtd = $@"
SELECT  c.Code3, c.Name, c.Grp, c.Type,
        COALESCE((
            SELECT {norm("cb1.Saldo")}
            FROM CoaBalance cb1
            WHERE cb1.Code3=c.Code3 AND cb1.Year=@y AND cb1.Month=1
        ), 0.0) AS Saldo,
        COALESCE((
            SELECT 1.0 * SUM({norm("cb2.Debet")})
            FROM CoaBalance cb2
            WHERE cb2.Code3=c.Code3 AND cb2.Year=@y AND cb2.Month BETWEEN 1 AND @m
        ), 0.0) AS Debet,
        COALESCE((
            SELECT 1.0 * SUM({norm("cb3.Kredit")})
            FROM CoaBalance cb3
            WHERE cb3.Code3=c.Code3 AND cb3.Year=@y AND cb3.Month BETWEEN 1 AND @m
        ), 0.0) AS Kredit
FROM Coa c
WHERE c.Code3 LIKE '%.%.%'
ORDER BY c.Code3;";

            List<BalRow> rows;
            using (var cn = Db.Open())
            {
                rows = (await cn.QueryAsync<BalRow>(ytd ? sqlYtd : sqlMonth, new { y = year, m = mto })).AsList();
            }

            var list = new List<TrialRow>(rows.Count + 6);

            decimal tBalD = 0, tBalK = 0,
                    tLRD = 0, tLRK = 0,
                    tNerD = 0, tNerK = 0;

            foreach (var r in rows)
            {
                decimal saldo = (decimal)r.Saldo;
                decimal debet = (decimal)r.Debet;
                decimal kredit = (decimal)r.Kredit;

                decimal closing = (r.Type == 0) ? (saldo + debet - kredit)
                                                : (saldo + kredit - debet);

                var t = new TrialRow { Code3 = r.Code3, Name = r.Name, Grp = r.Grp, Type = r.Type };

                if (r.Type == 0)
                {
                    if (closing >= 0) t.BalD = closing; else t.BalK = Math.Abs(closing);
                }
                else
                {
                    if (closing >= 0) t.BalK = closing; else t.BalD = Math.Abs(closing);
                }

                if (r.Grp == 4 || r.Grp == 5) { t.LRD = t.BalD; t.LRK = t.BalK; }
                else { t.NerD = t.BalD; t.NerK = t.BalK; }

                tBalD += t.BalD ?? 0; tBalK += t.BalK ?? 0;
                tLRD += t.LRD ?? 0; tLRK += t.LRK ?? 0;
                tNerD += t.NerD ?? 0; tNerK += t.NerK ?? 0;

                list.Add(t);
            }

            var slr = new TrialRow { Name = "SISA LABA / RUGI" };
            slr.BalD = null;
            slr.BalK = null;

            decimal diffLR = tLRD - tLRK;
            if (diffLR < 0) slr.LRD = Math.Abs(diffLR);
            else if (diffLR > 0) slr.LRK = diffLR;

            decimal diffNer = tNerD - tNerK;
            if (diffNer < 0) slr.NerD = Math.Abs(diffNer);
            else if (diffNer > 0) slr.NerK = diffNer;

            list.Add(slr);

            var total = new TrialRow { Name = "TOTAL" };
            total.BalD = tBalD;
            total.BalK = tBalK;
            total.LRD = tLRD + (slr.LRD ?? 0);
            total.LRK = tLRK + (slr.LRK ?? 0);
            total.NerD = tNerD + (slr.NerD ?? 0);
            total.NerK = tNerK + (slr.NerK ?? 0);

            list.Add(total);

            return list;
        }
    }
}