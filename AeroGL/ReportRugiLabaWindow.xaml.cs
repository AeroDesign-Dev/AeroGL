using AeroGL.Data;
using Dapper;
using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data; // WAJIB ADA
using System.Windows.Input;
using System.Windows.Media;

namespace AeroGL
{
    public partial class ReportRugiLabaWindow : Window
    {
        public string CompanyName { get; set; } = "Nama PT";

        public ReportRugiLabaWindow()
        {
            InitializeComponent();

            TxtYear.Text = DateTime.Today.Year.ToString();
            CboMonth.SelectedIndex = DateTime.Today.Month - 1;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { BtnShow_Click(s, e); e.Handled = true; }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None) { BtnPrint_Click(s, e); e.Handled = true; }
            };
        }

        // ===== Model dari Coa / CoaBalance =====
        private sealed class BalRow
        {
            public string Code3 { get; set; }   // e.g. 020.001.001
            public string Name { get; set; }
            public string Type { get; set; }    // "D" / "K" (dipaksa di SQL)
            public double Saldo { get; set; }
            public double Debet { get; set; }
            public double Kredit { get; set; }
        }

        // ===== ViewModel yang dibinding ke XAML "Paper" =====
        public sealed class RLView
        {
            public string Title { get; set; }

            public decimal Penjualan { get; set; }
            public decimal PenjualanBersih => Penjualan;

            public decimal Hpp { get; set; }                 // nilai “biaya” sebagai angka positif
            public decimal HppNeg => -Hpp;                   // untuk ditampilkan negatif

            public decimal LabaKotor => Penjualan - Hpp;

            public decimal BiayaPenjualan { get; set; }
            public decimal BiayaPenjualanNeg => -BiayaPenjualan;

            public decimal BiayaAdmUmum { get; set; }
            public decimal BiayaAdmUmumNeg => -BiayaAdmUmum;

            public decimal TotalBiaya => BiayaPenjualan + BiayaAdmUmum;
            public decimal TotalBiayaNeg => -TotalBiaya;

            public decimal LabaOperasi => LabaKotor - TotalBiaya;

            public decimal PendapatanLain { get; set; }
            public decimal BiayaLain { get; set; }
            public decimal BiayaLainNeg => -BiayaLain;

            public decimal JumlahLL => PendapatanLain - BiayaLain;

            public decimal LabaBersih => LabaOperasi + JumlahLL;

            public decimal Koreksi { get; set; }             // diambil dari prefix 027
            public decimal LabaSetelahKoreksi => LabaBersih + Koreksi;
        }

        private static string FormatPeriod(int year, int month, bool ytd)
        {
            var id = CultureInfo.GetCultureInfo("id-ID");
            if (!ytd) return new DateTime(year, month, 1).ToString("MMMM yyyy", id);
            return $"Jan – {new DateTime(year, month, 1).ToString("MMMM yyyy", id)}";
        }

        // helper ambil prefix 3 digit
        private static string P3(string code3)
        {
            if (string.IsNullOrEmpty(code3)) return "";
            int dot = code3.IndexOf('.');
            return dot > 0 ? code3.Substring(0, dot) : code3;
        }

        private async Task<RLView> BuildView(int year, int month, bool ytd)
        {
            // normalisasi nilai REAL/TEXT dan paksa jadi REAL
            string norm(string col) =>
                $"(CASE WHEN typeof({col})='text' THEN 1.0 * CAST(REPLACE({col}, ',', '.') AS REAL) ELSE 1.0 * {col} END)";

            // pastikan Type konsisten "D"/"K" walau di DB bisa 0/1 atau D/K
            string typeExpr = "CASE WHEN CAST(c.Type AS TEXT) IN ('0','D','d') THEN 'D' ELSE 'K' END";

            string sqlMonth = $@"
SELECT c.Code3, c.Name, {typeExpr} AS Type,
       COALESCE({norm("cb.Saldo")},  0.0) AS Saldo,
       COALESCE({norm("cb.Debet")},  0.0) AS Debet,
       COALESCE({norm("cb.Kredit")}, 0.0) AS Kredit
FROM Coa c
LEFT JOIN CoaBalance cb
       ON cb.Code3=c.Code3 AND cb.Year=@y AND cb.Month=@m
WHERE c.Code3 LIKE '%.%.%'";

            string sqlYtd = $@"
SELECT c.Code3, c.Name, {typeExpr} AS Type,
       COALESCE((SELECT {norm("cb1.Saldo")}
                 FROM CoaBalance cb1
                 WHERE cb1.Code3=c.Code3 AND cb1.Year=@y AND cb1.Month=1), 0.0) AS Saldo,
       COALESCE((SELECT 1.0*SUM({norm("cb2.Debet")})
                 FROM CoaBalance cb2
                 WHERE cb2.Code3=c.Code3 AND cb2.Year=@y AND cb2.Month BETWEEN 1 AND @m), 0.0) AS Debet,
       COALESCE((SELECT 1.0*SUM({norm("cb3.Kredit")})
                 FROM CoaBalance cb3
                 WHERE cb3.Code3=c.Code3 AND cb3.Year=@y AND cb3.Month BETWEEN 1 AND @m), 0.0) AS Kredit
FROM Coa c
WHERE c.Code3 LIKE '%.%.%'";

            // 1) ambil data
            BalRow[] raw;
            using (var cn = Db.Open())
            {
                raw = (await cn.QueryAsync<BalRow>(ytd ? sqlYtd : sqlMonth, new { y = year, m = month })).ToArray();
            }

            // 2) closing akun (pattern yang kita sepakati)
            decimal Closing(BalRow r)
            {
                var s = (decimal)r.Saldo;
                var d = (decimal)r.Debet;
                var k = (decimal)r.Kredit;
                return r.Type == "D" ? (s + d - k) : (s + k - d);
            }

            // 3) agregasi per prefix sesuai template DOS
            decimal sum020 = raw.Where(r => P3(r.Code3) == "020").Sum(Closing); // Penjualan
            decimal sum021 = raw.Where(r => P3(r.Code3) == "021").Sum(Closing); // HPP
            decimal sum022 = raw.Where(r => P3(r.Code3) == "022").Sum(Closing); // Biaya Penjualan
            decimal sum023 = raw.Where(r => P3(r.Code3) == "023").Sum(Closing); // Biaya Administrasi & Umum
            decimal sum025 = raw.Where(r => P3(r.Code3) == "025").Sum(Closing); // Pendapatan Lain-lain
            decimal sum026 = raw.Where(r => P3(r.Code3) == "026").Sum(Closing); // Biaya Lain-lain
            decimal sum027 = raw.Where(r => P3(r.Code3) == "027").Sum(Closing); // Koreksi (+/-)

            // 4) susun VM (biaya dipositifkan lalu ditampilkan sebagai negatif di XAML)
            var vm = new RLView
            {
                Title = $"Print Rugi/Laba {CompanyName} — {FormatPeriod(year, month, ytd)}",

                Penjualan = sum020,
                Hpp = Math.Abs(sum021),
                BiayaPenjualan = Math.Abs(sum022),
                BiayaAdmUmum = Math.Abs(sum023),

                PendapatanLain = sum025,
                BiayaLain = Math.Abs(sum026),

                // Koreksi langsung pakai tanda hasil closing prefix 027 (bisa plus/minus)
                Koreksi = sum027
            };

            return vm;
        }

        // ===== UI events =====
        private async void BtnShow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int year = int.TryParse(TxtYear.Text, out var y) ? y : DateTime.Today.Year;
                int month = CboMonth.SelectedIndex + 1;
                bool ytd = CboMode.SelectedIndex == 1;

                Mouse.OverrideCursor = Cursors.Wait;
                TxtInfo.Text = "Menghitung…";

                var vm = await BuildView(year, month, ytd);
                DataContext = vm;

                TxtInfo.Text = FormatPeriod(year, month, ytd);
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
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                // 1) Terapkan tema hitam-putih + padding kanan/kiri
                var st = ApplyPrintTheme(Paper, dlg.PrintableAreaWidth);

                try
                {
                    dlg.PrintTicket.PageOrientation = PageOrientation.Portrait;
                    dlg.PrintVisual(Paper, "Rugi Laba");
                }
                finally
                {
                    // 2) Kembalikan tampilan preview seperti semula
                    RestorePrintTheme(Paper, st);
                }
            }
        }


        // ====== Helpers untuk print (tematik hitam-putih + padding) ======
        private sealed class PrintThemeState
        {
            public Brush PaperBg;
            public double PaperWidth;
            public Thickness PaperMargin;

            public (TextBlock tb, Brush fg)[] Tbs;
            public (Border bd, Brush bb)[] Bds;
        }

        private static void VisitVisuals(DependencyObject root, Action<DependencyObject> act)
        {
            if (root == null) return;
            act(root);
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) VisitVisuals(VisualTreeHelper.GetChild(root, i), act);
        }

        private PrintThemeState ApplyPrintTheme(FrameworkElement paper, double printableW)
        {
            const double pad = 36; // ~0.5 inch
            var st = new PrintThemeState();

            // simpan state dasar
            st.PaperWidth = paper.Width;
            st.PaperMargin = paper.Margin;

            // background: Grid (Panel) atau Border
            if (paper is Panel pnl) { st.PaperBg = pnl.Background; pnl.Background = Brushes.White; }
            else if (paper is Border brd) { st.PaperBg = brd.Background; brd.Background = Brushes.White; }

            // padding/margin + batas lebar supaya ada ruang kanan
            paper.Margin = new Thickness(pad);
            paper.Width = Math.Max(0, printableW - pad * 2);

            // hitamkan semua TextBlock & garis Border di dalamnya
            var tbs = new System.Collections.Generic.List<(TextBlock, Brush)>();
            var bds = new System.Collections.Generic.List<(Border, Brush)>();

            VisitVisuals(paper, v =>
            {
                if (v is TextBlock tb)
                {
                    tbs.Add((tb, tb.Foreground));
                    tb.Foreground = Brushes.Black;
                }
                else if (v is Border bd)
                {
                    bds.Add((bd, bd.BorderBrush));
                    bd.BorderBrush = Brushes.Black;
                }
            });

            st.Tbs = tbs.ToArray();
            st.Bds = bds.ToArray();
            return st;
        }

        private static void RestorePrintTheme(FrameworkElement paper, PrintThemeState st)
        {
            if (st == null) return;

            if (paper is Panel pnl) pnl.Background = st.PaperBg;
            else if (paper is Border brd) brd.Background = st.PaperBg;

            paper.Width = st.PaperWidth;
            paper.Margin = st.PaperMargin;

            foreach (var (tb, fg) in st.Tbs) if (tb != null) tb.Foreground = fg;
            foreach (var (bd, bb) in st.Bds) if (bd != null) bd.BorderBrush = bb;
        }

    } // <--- AKHIR CLASS ReportRugiLabaWindow
} // <--- AKHIR NAMESPACE