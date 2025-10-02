using AeroGL.Core;
using AeroGL.Data;
using Dapper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AeroGL
{
    public partial class ReportNeracaLajurWindow : Window
    {
        private readonly ICoaRepository _coaRepo = new CoaRepository();
        public string CompanyName { get; set; } // fallback: "Nama PT"

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

                Mouse.OverrideCursor = Cursors.Wait;
                TxtInfo.Text = "Menghitung…";

                var rows = await BuildTrialBalance(d1, d2, ytd);
                GridRows.ItemsSource = rows;

                TxtInfo.Text = $"{rows.Count} baris. Periode: {FormatPeriod(d1, d2)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var items = GridRows.ItemsSource as IList<TrialRow>;
            if (items == null || items.Count == 0)
            {
                MessageBox.Show("Belum ada data ditampilkan.");
                return;
            }

            int year = int.TryParse(TxtYear.Text, out var y) ? y : DateTime.Today.Year;
            int month = CboMonth.SelectedIndex + 1;
            bool ytd = CboMode.SelectedIndex == 1;
            var (d1, d2) = MakeRange(year, month, ytd);

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
                double pad = 36;
                double avail = dlg.PrintableAreaWidth - pad * 2;

                var doc = BuildFlowDocument(items, d1, d2, avail, pad);
                doc.PageWidth = dlg.PrintableAreaWidth;
                doc.PageHeight = dlg.PrintableAreaHeight;

                dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Neraca Lajur");
            }
        }

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

        // ==== model untuk grid ====
        public sealed class TrialRow
        {
            public string Code3 { get; set; }
            public string Name { get; set; }
            public int Grp { get; set; }      // 1..5
            public int Type { get; set; }     // 0=Debit, 1=Kredit
            public string TypeLetter => Type == 0 ? "D" : "K";

            public decimal? BalD { get; set; }
            public decimal? BalK { get; set; }
            public decimal? LRD { get; set; }
            public decimal? LRK { get; set; }
            public decimal? NerD { get; set; }
            public decimal? NerK { get; set; }
        }

        // hasil baca Coa + CoaBalance
        private sealed class BalRow
        {
            public string Code3 { get; set; }
            public string Name { get; set; }
            public int Grp { get; set; }
            public int Type { get; set; }
            public double Saldo { get; set; }   // gunakan double agar aman dari mapping REAL
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

            // Totals per kolom (tanpa SLR & TOTAL dulu)
            decimal tBalD = 0, tBalK = 0,
                    tLRD = 0, tLRK = 0,
                    tNerD = 0, tNerK = 0;

            foreach (var r in rows)
            {
                decimal saldo = (decimal)r.Saldo;
                decimal debet = (decimal)r.Debet;
                decimal kredit = (decimal)r.Kredit;

                // closing berdasar tipe normal
                decimal closing = (r.Type == 0) ? (saldo + debet - kredit)
                                                : (saldo + kredit - debet);

                var t = new TrialRow { Code3 = r.Code3, Name = r.Name, Grp = r.Grp, Type = r.Type };

                // Kolom Balance → posisi sesuai tanda & type
                if (r.Type == 0)
                {
                    if (closing >= 0) t.BalD = closing; else t.BalK = Math.Abs(closing);
                }
                else
                {
                    if (closing >= 0) t.BalK = closing; else t.BalD = Math.Abs(closing);
                }

                // Mapping ke R/L atau Neraca (berdasar Group)
                if (r.Grp == 4 || r.Grp == 5) { t.LRD = t.BalD; t.LRK = t.BalK; }
                else { t.NerD = t.BalD; t.NerK = t.BalK; }

                // akumulasi total per kolom
                tBalD += t.BalD ?? 0; tBalK += t.BalK ?? 0;
                tLRD += t.LRD ?? 0; tLRK += t.LRK ?? 0;
                tNerD += t.NerD ?? 0; tNerK += t.NerK ?? 0;

                list.Add(t);
            }

            // ========= SISA LABA / RUGI (per kolom) =========
            // Aturan sesuai request lo:
            // diff = SUM(Debet) - SUM(Kredit); 
            // jika diff >= 0 → taruh di DEBET, else → taruh di KREDIT.
            var slr = new TrialRow { Name = "SISA LABA / RUGI" };

            // BALANCE: tidak dihitung SISA
            slr.BalD = null;
            slr.BalK = null;

            // R/L
            decimal diffLR = tLRD - tLRK;
            if (diffLR < 0) slr.LRD = Math.Abs(diffLR);   // debet kurang
            else if (diffLR > 0) slr.LRK = diffLR;             // kredit kurang
                                                               // kalau 0, dua-duanya null

            // NERACA
            decimal diffNer = tNerD - tNerK;
            if (diffNer < 0) slr.NerD = Math.Abs(diffNer); // debet kurang
            else if (diffNer > 0) slr.NerK = diffNer;           // kredit kurang

            list.Add(slr);

            // ========= TOTAL (jumlah per kolom) =========
            var total = new TrialRow { Name = "TOTAL" };

            // Balance: total akun (tanpa SISA)
            total.BalD = tBalD;
            total.BalK = tBalK;

            // R/L: total akun + SISA R/L
            total.LRD = tLRD + (slr.LRD ?? 0);
            total.LRK = tLRK + (slr.LRK ?? 0);

            // Neraca: total akun + SISA Neraca
            total.NerD = tNerD + (slr.NerD ?? 0);
            total.NerK = tNerK + (slr.NerK ?? 0);

            list.Add(total);

            return list;
        }


        // ==== printer ====
        private FlowDocument BuildFlowDocument(IList<TrialRow> items, DateTime d1, DateTime d2, double availableWidth, double pagePadding)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(pagePadding),
                ColumnWidth = double.PositiveInfinity
            };

            string nama = string.IsNullOrWhiteSpace(CompanyName) ? "Nama PT" : CompanyName;
            doc.Blocks.Add(new Paragraph(new Run(nama)) { FontSize = 16, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0) });
            doc.Blocks.Add(new Paragraph(new Run("LAPORAN NERACA LAJUR")) { FontSize = 14, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
            doc.Blocks.Add(new Paragraph(new Run(FormatPeriod(d1, d2))) { FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });

            var table = new Table { CellSpacing = 0 }; doc.Blocks.Add(table);

            double wNo = 130, wNm = Math.Max(240, availableWidth - (130 * 6 + 40 + 12 * 9));
            double w = 130, wT = 40;

            table.Columns.Add(new TableColumn { Width = new GridLength(wNo) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wNm) });
            for (int i = 0; i < 6; i++) table.Columns.Add(new TableColumn { Width = new GridLength(w) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wT) });

            var rg = new TableRowGroup(); table.RowGroups.Add(rg);

            var head = new TableRow(); rg.Rows.Add(head);
            void H(string t, bool first = false)
            {
                head.Cells.Add(new TableCell(new Paragraph(new Run(t)) { Margin = new Thickness(0) })
                {
                    Padding = new Thickness(6, 3, 6, 3),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(first ? 1 : 0, 0, 1, 1.5),
                    FontWeight = FontWeights.Bold
                });
            }
            H("NOMOR", true); H("NAMA REKENING"); H("BALANCE • DEBET"); H("BALANCE • KREDIT");
            H("R/L • DEBET"); H("R/L • KREDIT"); H("NERACA • DEBET"); H("NERACA • KREDIT"); H("T");

            TableCell C(string s, TextAlignment a = TextAlignment.Left, bool first = false, bool bold = false)
                => new TableCell(new Paragraph(new Run(s)) { Margin = new Thickness(0), TextAlignment = a })
                {
                    Padding = new Thickness(6, 2, 6, 2),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(first ? 1 : 0, 0, 1, 1),
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
                };

            string F(decimal? v) => v.HasValue ? "Rp " + v.Value.ToString("N2", id) : "";

            foreach (var r in items)
            {
                bool isSLR = string.Equals(r.Name, "SISA LABA / RUGI", StringComparison.OrdinalIgnoreCase);
                bool isTOTAL = string.Equals(r.Name, "TOTAL", StringComparison.OrdinalIgnoreCase);
                bool bold = isSLR || isTOTAL;

                var tr = new TableRow(); rg.Rows.Add(tr);
                tr.Cells.Add(C(r.Code3, TextAlignment.Left, true, bold));
                tr.Cells.Add(C(r.Name, TextAlignment.Left, false, bold));
                tr.Cells.Add(C(F(r.BalD), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(F(r.BalK), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(F(r.LRD), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(F(r.LRK), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(F(r.NerD), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(F(r.NerK), TextAlignment.Right, false, bold));
                tr.Cells.Add(C(r.TypeLetter, TextAlignment.Center, false, bold));
            }

            doc.Blocks.Add(new Paragraph(new Run($"Dicetak: {DateTime.Now:dd-MM-yyyy HH:mm}"))
            { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0) });

            return doc;
        }

    }
}
